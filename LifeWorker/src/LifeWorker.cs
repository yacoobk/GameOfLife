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
                using (var view = new View())
                {
                    var isConnected = true;

                    view.OnDisconnect(op =>
                    {
                        Console.Error.WriteLine("[disconnect] {0}", op.Reason);
                        isConnected = false;
                    });

                    view.OnLogMessage(op =>
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
                    
                    var maxWait = System.TimeSpan.FromMilliseconds(1000f / FramesPerSecond);
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    var isViewComplete = false;
                    var isSeeded = false;

                    // yolo - we know the gridSize, and there's only 1 worker...
                    var cellPositions = new Improbable.EntityId[gridSize,gridSize];
                    view.OnAddComponent<Improbable.Position>(op => 
                    {
                        var coords = op.Data.Get().Value.coords;
                        // yolo - we know the coords will be integer values
                        var x = (int)coords.x;
                        var z = (int)coords.z;
                        cellPositions[x,z] = op.EntityId;
                    });
                    var cellsAuthoritative = new HashSet<Improbable.EntityId>();
                    view.OnAuthorityChange<Cell.CellState>(op =>
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

                    var tick = 0;
                    while (isConnected)
                    {
                        stopwatch.Reset();
                        stopwatch.Start();
                        // Invoke callbacks.
                        using (var opList = connection.GetOpList(GetOpListTimeoutInMilliseconds))
                        {
                            view.Process(opList);
                        }
                        // Do other work here...

                        //connection.SendLogMessage(LogLevel.Info, LoggerName, "entity count"+view.Entities.Count);

                        // Check if the view is complete
                        // yolo - I know there are only gridSize*gridSize entities in the snapshot, and only 1 worker
                        if (!isViewComplete && view.Entities.Count == gridSize*gridSize)
                        {
                            isViewComplete = true;
                        }

                        // Seed the game
                        if (isViewComplete && !isSeeded)
                        {
                            //var update = new Cell.CellState.Update();
                            var update = new Cell.CellState.Update();
                            update.isAlive = true;
                            // connection.SendComponentUpdate(new EntityId(1), update);
                            // connection.SendComponentUpdate(new EntityId(2), update);
                            // connection.SendComponentUpdate(new EntityId(3), update);
                            connection.SendComponentUpdate(new EntityId(11), update);
                            connection.SendComponentUpdate(new EntityId(12), update);
                            connection.SendComponentUpdate(new EntityId(13), update);
                            // connection.SendComponentUpdate(new EntityId(21), update);
                            // connection.SendComponentUpdate(new EntityId(22), update);
                            // connection.SendComponentUpdate(new EntityId(23), update);
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
                            foreach(var c in cellsAuthoritative)
                            {
                                var coords = view.Entities[c].Get<Position>().Value.Get().Value.coords;
                                var x = (int)coords.x;
                                var z = (int)coords.z;
                                var isAlive = view.Entities[c].Get<CellState>().Value.Get().Value.isAlive;
                                var neighbours = new List<Improbable.EntityId>();
                                var liveNeighbours = 0;
                                var maxIndex = gridSize - 1;

                                // find neighbours
                                if (x > 0) {neighbours.Add(cellPositions[x - 1, z]);} // left
                                if (x > 0 && z < maxIndex) {neighbours.Add(cellPositions[x - 1, z + 1]);} // left, up
                                if (x > 0 && z > 0) {neighbours.Add(cellPositions[x - 1, z - 1]);} // left, down
                                if (z < maxIndex) {neighbours.Add(cellPositions[x , z + 1]);} // up
                                if (z > 0) {neighbours.Add(cellPositions[x, z - 1]);} // down
                                if (x < maxIndex) {neighbours.Add(cellPositions[x + 1, z]);} // right
                                if (x < maxIndex && z < maxIndex) {neighbours.Add(cellPositions[x + 1, z + 1]);} // right, up
                                if (x < maxIndex && z > 0) {neighbours.Add(cellPositions[x + 1, z - 1]);} // right, down

                                // count how many are alive
                                foreach (var n in neighbours)
                                {
                                    if (view.Entities[n].Get<CellState>().Value.Get().Value.isAlive) { liveNeighbours++; }
                                }

                                // if state needs to change then send an update
                                //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {x},{z} {isAlive}. live neighbours: {liveNeighbours}");
                                var update = new Cell.CellState.Update();
                                if (isAlive && (liveNeighbours < 2 || liveNeighbours > 3))
                                {
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {x},{z} {isAlive}. live neighbours: {liveNeighbours}. Killing...");
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Killing...");
                                    update.isAlive = false;
                                    connection.SendComponentUpdate(c, update);
                                }
                                if (!isAlive && liveNeighbours == 3)
                                {
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Cell {x},{z} {isAlive}. live neighbours: {liveNeighbours}. Spawning...");
                                    //connection.SendLogMessage(LogLevel.Info, LoggerName, $"Spawning...");
                                    update.isAlive = true;
                                    connection.SendComponentUpdate(c, update);
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
