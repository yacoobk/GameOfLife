using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Improbable;
using Improbable.Worker;

namespace Cell
{
    class LifeWorker
    {
        private const string WorkerType = "LifeWorker";
        private const string LoggerName = "LifeWorker.cs";
        private const int ErrorExitStatus = 1;
        private const uint GetOpListTimeoutInMilliseconds = 100;
        const int FramesPerSecond = 1;

        static int Main(string[] arguments)
        {
            Action printUsage = () =>
            {
                Console.WriteLine("Usage: " + WorkerType + " <hostname> <port> <worker_id>");
                Console.WriteLine("Connects to the deployment.");
                Console.WriteLine("    <hostname>      - hostname of the receptionist to connect to.");
                Console.WriteLine("    <port>          - port to use.");
                Console.WriteLine("    <worker_id>     - name of the worker assigned by SpatialOS.");
            };
            if (arguments.Length < 3)
            {
                printUsage();
                return ErrorExitStatus;
            }

            Assembly.Load("GeneratedCode");

            Console.WriteLine("Worker Starting...");
            using (var connection = ConnectWorker(arguments))
            {
                using (var dispatcher = new Dispatcher())
                {
                    var isConnected = true;

                    dispatcher.OnDisconnect(op =>
                    {
                        Console.Error.WriteLine("[disconnect] {0}", op.Reason);
                        isConnected = false;
                    });

                    dispatcher.OnLogMessage(op =>
                    {
                        connection.SendLogMessage(op.Level, LoggerName, op.Message);
                        Console.WriteLine("Log Message: {0}", op.Message);
                        if (op.Level == LogLevel.Fatal)
                        {
                            Console.Error.WriteLine("Fatal error: {0}", op.Message);
                            Environment.Exit(ErrorExitStatus);
                        }
                    });

                    connection.SendLogMessage(LogLevel.Info, LoggerName,
                        "Successfully connected using TCP and the Receptionist");
                    
                    //
                    // Simulation Logic
                    //
                    var maxWait = System.TimeSpan.FromMilliseconds(1000f / FramesPerSecond);
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    var cells = new Dictionary<Improbable.EntityId, CellState.Data>();
                    var cellsAuthoritative = new HashSet<Improbable.EntityId>();
                    var cellsToUpdate = new List<KeyValuePair<Improbable.EntityId, CellEntityUpdates>>();

                    // parse cells in to a useful data structure
                    dispatcher.OnAddComponent<Cell.CellState>(op => cells.Add(op.EntityId, op.Data.Get()));

                    // TODO: only strictly care about this for cells we are not authoritative over
                    // for ones we are authoritative over, we should maintain our own state
                    dispatcher.OnComponentUpdate<Cell.CellState>(op =>
                    {
                        var cellId = op.EntityId;
                        var newCellData = cells[op.EntityId];
                        op.Update.ApplyTo(newCellData);
                        cells[op.EntityId] = newCellData;
                    });

                    dispatcher.OnAuthorityChange<Cell.CellState>(op =>
                    {
                        switch(op.Authority){
                            case Improbable.Worker.Authority.Authoritative:
                                cellsAuthoritative.Add(op.EntityId);
                                break;
                            case Improbable.Worker.Authority.NotAuthoritative:
                                cellsAuthoritative.Remove(op.EntityId);
                                break;
                        }
                    });

                    // Try to simulate the world
                    while (isConnected)
                    {
                        stopwatch.Reset();
                        stopwatch.Start();
                        // Invoke callbacks.
                        using (var opList = connection.GetOpList(GetOpListTimeoutInMilliseconds))
                        {
                            dispatcher.Process(opList);
                        }
                        // Do other work here...

                        connection.SendLogMessage(LogLevel.Info, LoggerName, $"entity count {cells.Count}, authority count {cellsAuthoritative.Count}");

                        // Check if we can calculate the next generation, if not skip iteration
                        // - if know about all neighbours and their generations are the same as or 1 above authoritative cells
                        int foundAuthoritativeCellsAndAllNeighbours = 0;
                        uint minAuthoritativeCellGen = uint.MaxValue;
                        uint maxAuthoritativeCellGen = 0;
                        uint minNeighbourCellGen = uint.MaxValue;
                        uint maxNeighbourCellGen = 0;
                        cellsToUpdate.Clear();
                        
                        foreach(var cellId in cellsAuthoritative)
                        {
                            CellState.Data cellData;
                            var foundCell = cells.TryGetValue(cellId, out cellData);
                            if (!foundCell)
                            {
                                connection.SendLogMessage(LogLevel.Error, LoggerName, $"Could not find cell {cellId.Id}");
                                break;
                            }
                            minAuthoritativeCellGen = Math.Min(minAuthoritativeCellGen, cellData.Value.generation);
                            maxAuthoritativeCellGen = Math.Max(maxAuthoritativeCellGen, cellData.Value.generation);

                            // do we have all neighbours & if so calculate their min & max generation
                            int foundNeighbours = 0;
                            int liveNeighbours = 0;
                            bool canCalcNextGen = true;
                            foreach (var neighbourId in cellData.Value.neighbours)
                            {
                                CellState.Data neighbourCellData;
                                var foundNeighbour = cells.TryGetValue(neighbourId, out neighbourCellData);
                                if (foundNeighbour)
                                {
                                    foundNeighbours++;
                                    minNeighbourCellGen = Math.Min(minNeighbourCellGen, neighbourCellData.Value.generation);
                                    maxNeighbourCellGen = Math.Max(maxNeighbourCellGen, neighbourCellData.Value.generation);
                                    if (!(cellData.Value.generation == neighbourCellData.Value.generation || cellData.Value.generation == neighbourCellData.Value.generation-1))
                                    {
                                        canCalcNextGen = false;
                                    }
                                    else if ( (cellData.Value.generation % 2 == 0 && neighbourCellData.Value.isAliveEven)
                                        || (cellData.Value.generation % 2 == 1 && neighbourCellData.Value.isAliveOdd))
                                    {
                                        liveNeighbours++;
                                    }
                                }
                                else
                                {
                                    connection.SendLogMessage(LogLevel.Warn, LoggerName, $"Could not find cell {cellId}'s neighbour {neighbourId.Id}");
                                    break;
                                }
                            }

                            if(foundNeighbours != cellData.Value.neighbours.Count) { break; } // don't have all the neighbours

                            foundAuthoritativeCellsAndAllNeighbours++;

                            if (!canCalcNextGen) continue;

                            // optimistically calculate the next gen for this authoritative cell since we have the data available
                            var currentCellIsAlive = (cellData.Value.generation % 2 == 0) ? cellData.Value.isAliveEven : cellData.Value.isAliveOdd;
                            var newCellIsAlive = calculateNextCellState(currentCellIsAlive, liveNeighbours);
                            var update = new Cell.CellState.Update();
                            var metaUpdate = new Improbable.Metadata.Update();
                            update.generation = cellData.Value.generation + 1;
                            if (cellData.Value.generation % 2 == 0) {update.isAliveOdd = newCellIsAlive;} else {update.isAliveEven = newCellIsAlive;}
                            metaUpdate.entityType = newCellIsAlive ? "alive" : "dead";
                            cellsToUpdate.Add(new KeyValuePair<EntityId, CellEntityUpdates>(cellId, new CellEntityUpdates(update, metaUpdate)));
                        }

                        // connection.SendLogMessage(LogLevel.Warn, LoggerName, $"foundAuthoritativeCellsAndAllNeighbours {foundAuthoritativeCellsAndAllNeighbours}, cellsToUpdate.Count {cellsToUpdate.Count}, minAuthoritativeCellGen {minAuthoritativeCellGen}, maxAuthoritativeCellGen {maxAuthoritativeCellGen}, minNeighbourCellGen {minNeighbourCellGen}, maxNeighbourCellGen {maxNeighbourCellGen}");
                        if(cellsToUpdate.Count > 0
                          && foundAuthoritativeCellsAndAllNeighbours == cellsAuthoritative.Count
                          && minAuthoritativeCellGen <= minNeighbourCellGen // prevents getting too far ahead vs other workers
                        )
                        {
                            updateCells(connection, cellsToUpdate, minAuthoritativeCellGen, maxAuthoritativeCellGen);
                        }

                        // Finished doing work...
                        stopwatch.Stop();
                        var waitFor = maxWait.Subtract(stopwatch.Elapsed);
                        System.Threading.Thread.Sleep(waitFor.Milliseconds > 0 ? waitFor : System.TimeSpan.Zero);
                    }

                }
            }

            return 0;
        }

        // Every cell interacts with its eight neighbours, which are the cells that are horizontally, vertically, or diagonally adjacent.
        // At each step in time, the following transitions occur:
        // 1. Any live cell with fewer than two live neighbors dies, as if by underpopulation.
        // 2. Any live cell with two or three live neighbors lives on to the next generation.
        // 3. Any live cell with more than three live neighbors dies, as if by overpopulation.
        // 4. Any dead cell with exactly three live neighbors becomes a live cell, as if by reproduction.
        private static bool calculateNextCellState(bool cellIsAlive, int liveNeighbours)
        {
            return (cellIsAlive && (liveNeighbours == 2 || liveNeighbours == 3)) || (!cellIsAlive && liveNeighbours == 3);
        }

        // Send State Updates
        private static void updateCells(Connection connection, List<KeyValuePair<Improbable.EntityId, CellEntityUpdates>> cellsToUpdate, uint minAuthoritativeCellGen, uint maxAuthoritativeCellGen)
        {
            // if we have a mix of authoritative generations then only apply updates for the lower generation
            if(minAuthoritativeCellGen == maxAuthoritativeCellGen)
            {
                connection.SendLogMessage(LogLevel.Info, LoggerName, $"** Tick {maxAuthoritativeCellGen + 1} **");
                cellsToUpdate.ForEach(cell => updateCell(connection, cell));
            }
            else
            {
                // filter to the cells which are going to be updated 1 gen above the current minAuthoritativeCellGen (i.e. the cells which currently are minAuthoritativeCellGen)
                var lowerGenCells = cellsToUpdate.Where(cell => cell.Value.cellStateUpdate.generation.Value == minAuthoritativeCellGen + 1);
                connection.SendLogMessage(LogLevel.Info, LoggerName, $"** Tick {minAuthoritativeCellGen + 1} (partial {lowerGenCells.Count()} cells)  **");
                lowerGenCells.ToList().ForEach(cell => updateCell(connection, cell));
            }
        }

        private static void updateCell(Connection connection, KeyValuePair<EntityId, CellEntityUpdates> cell)
        {
            //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {cell.Key} {cell.Value}. update: {cell.Value.cellStateUpdate.generation.Value} {cell.Value.cellStateUpdate.isAliveEven.Value} {cell.Value.cellStateUpdate.isAliveOdd.Value}");
            connection.SendComponentUpdate(cell.Key, cell.Value.cellStateUpdate);
            connection.SendComponentUpdate(cell.Key, cell.Value.metaUpdate);
        }

        private static Connection ConnectWorker(string[] arguments)
        {
            string hostname = arguments[0];
            ushort port = Convert.ToUInt16(arguments[1]);
            string workerId = arguments[2];
            var connectionParameters = new ConnectionParameters();
            connectionParameters.WorkerType = WorkerType;
            connectionParameters.Network.ConnectionType = NetworkConnectionType.Tcp;
            connectionParameters.ProtocolLogging.LogPrefix = $"C:\\Code\\SpatialProjects\\GameOfLife\\SpatialOS\\logs\\protocol\\{workerId}-log-";
            connectionParameters.EnableProtocolLoggingAtStartup = true;

            using (var future = Connection.ConnectAsync(hostname, port, workerId, connectionParameters))
            {
                return future.Get();
            }
        }


        private class CellEntityUpdates
        {
            public CellState.Update cellStateUpdate;
            public Improbable.Metadata.Update metaUpdate;
            public CellEntityUpdates(CellState.Update update, Improbable.Metadata.Update meta_uptate)
            {
                cellStateUpdate = update;
                metaUpdate = meta_uptate;
            }
        }
    }
}
