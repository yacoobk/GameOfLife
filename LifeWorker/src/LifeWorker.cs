using System;
using System.Collections.Generic;
using System.Reflection;
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
        const int gridSize = 10; // yolo hack for now

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
                    var isViewComplete = false;
                    var isSeeded = false;

                    // parse cells in to a useful data structure
                    dispatcher.OnAddComponent<Cell.CellState>(op => 
                    {
                        //cells.Add(op.EntityId, op.Data.Get().Value);
                        cells.Add(op.EntityId, op.Data.Get());
                    });

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

                    // simulate the world
                    var tick = 0;
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

                        connection.SendLogMessage(LogLevel.Info, LoggerName, $"entity count {cells.Count}");

                        // Check if the view is complete
                        // yolo - I know there are only gridSize*gridSize entities in the snapshot, and only 1 worker
                        if (!isViewComplete && cells.Count == (int)Math.Pow(gridSize,2) && cellsAuthoritative.Count == cells.Count)
                        {
                            connection.SendLogMessage(LogLevel.Info, LoggerName, "view completed");
                            isViewComplete = true;
                        }

                        // Seed the game
                        if (isViewComplete && !isSeeded)
                        {
                            connection.SendLogMessage(LogLevel.Info, LoggerName, "seeding...");
                            var update = new Cell.CellState.Update();
                            update.isAlive = true;
                            connection.SendComponentUpdate(new EntityId(12), update);
                            connection.SendComponentUpdate(new EntityId(13), update);
                            connection.SendComponentUpdate(new EntityId(14), update);
                            isSeeded = true;
                        }

                        // compute a game tick
                        // Every cell interacts with its eight neighbours, which are the cells that are horizontally, vertically, or diagonally adjacent.
                        // At each step in time, the following transitions occur:
                        // 1. Any live cell with fewer than two live neighbors dies, as if by underpopulation.
                        // 2. Any live cell with two or three live neighbors lives on to the next generation.
                        // 3. Any live cell with more than three live neighbors dies, as if by overpopulation.
                        // 4. Any dead cell with exactly three live neighbors becomes a live cell, as if by reproduction.
                        if (isViewComplete && isSeeded)
                        {
                            tick++;
                            connection.SendLogMessage(LogLevel.Info, LoggerName, $"** Tick {tick} **");
                            foreach(var cellId in cellsAuthoritative)
                            {
                                CellState.Data cellData;
                                var foundCell = cells.TryGetValue(cellId, out cellData);
                                if (!foundCell)
                                {
                                    connection.SendLogMessage(LogLevel.Error, LoggerName, $"Could not find cell {cellId.Id}");
                                    break;
                                }

                                // count how many neighbours are alive
                                var liveNeighbours = 0;
                                foreach (var neighbourId in cellData.Value.neighbours)
                                {
                                    CellState.Data neighbourCellData;
                                    var foundNeighbour = cells.TryGetValue(neighbourId, out neighbourCellData);
                                    if (!foundNeighbour)
                                    {
                                        connection.SendLogMessage(LogLevel.Error, LoggerName, $"Could not find cell {cellId.Id}'s neighbour {neighbourId.Id}");
                                        break;
                                    }
                                    if (neighbourCellData.Value.isAlive) { liveNeighbours++; }
                                }

                                // if state needs to change then send an update
                                // connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {cellId.Id} {cellData.isAlive}. live neighbours: {liveNeighbours}");
                                var update = new Cell.CellState.Update();
                                if (cellData.Value.isAlive && (liveNeighbours < 2 || liveNeighbours > 3))
                                {
                                    // connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {cellId.Id} {cellData.isAlive}. live neighbours: {liveNeighbours}. Killing...");
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Killing...");
                                    update.isAlive = false;
                                    connection.SendComponentUpdate(cellId, update);
                                }
                                if (!cellData.Value.isAlive && liveNeighbours == 3)
                                {
                                    // connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {cellId.Id} {cellData.isAlive}. live neighbours: {liveNeighbours}. Spawning...");
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Spawning...");
                                    update.isAlive = true;
                                    connection.SendComponentUpdate(cellId, update);
                                }
                            }
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

        private static Connection ConnectWorker(string[] arguments)
        {
            string hostname = arguments[0];
            ushort port = Convert.ToUInt16(arguments[1]);
            string workerId = arguments[2];
            var connectionParameters = new ConnectionParameters();
            connectionParameters.WorkerType = WorkerType;
            connectionParameters.Network.ConnectionType = NetworkConnectionType.Tcp;

            using (var future = Connection.ConnectAsync(hostname, port, workerId, connectionParameters))
            {
                return future.Get();
            }
        }
                
    }
}
