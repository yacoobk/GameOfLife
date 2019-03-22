using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Timers;
using Improbable;
using Improbable.Worker;

namespace Life
{
    class SnapGen
    {
        private const int ErrorExitStatus = 1;
        private const int gridSize = 100;

        static int Main(string[] arguments)
        {
            Assembly.Load("GeneratedCode");

            Action printUsage = () =>
            {
                Console.WriteLine("Usage: Client <snapshotfile> generates a snapshot.");
            };

            if (arguments.Length != 1)
            {
                printUsage();
                return ErrorExitStatus;
            }
            else
            {
                Console.WriteLine("Generating Snapshot...");
                GenerateSnapshot(arguments[0]);
                return 0;
            }

        }

        private static void GenerateSnapshot(string snapshotPath)
        {
            Console.WriteLine(String.Format("Generating snapshot file {0}", snapshotPath));
            Assembly.Load("GeneratedCode");
            using (var snapshotOutput = new SnapshotOutputStream(snapshotPath))
            {
                var entityCounter = 1;
                var rnd = new Random();

                for (var i = 0; i < gridSize; i++)
                {
                    for (var j = 0; j < gridSize; j++)
                    {
                        var entityId = new EntityId(entityCounter++);                        
                        var neighbours = getNeighbours(entityId);
                        var entity = createEntity(j, i, neighbours, rnd);
                        var error = snapshotOutput.WriteEntity(entityId, entity);
                        if (error.HasValue)
                        {
                            throw new System.SystemException("error saving: " + error.Value);
                        }
                    }
                }
            }
        }

        private static Improbable.Collections.List<EntityId> getNeighbours(EntityId id)
        {
            var neighbours = new Improbable.Collections.List<EntityId>();
            // grid is a square with IDs like this for 5x5 grid:
            // 21, 22, 23, 24, 25
            // 16, 17, 18, 19, 20
            // 11, 12, 13, 14, 15
            // 6, 7, 8, 9, 10
            // 1, 2, 3, 4, 5
            var rowIndex = (id.Id - 1) / gridSize; // zero-based row index
            var colIndex = (id.Id - 1) % gridSize; //  zero-based column index

            var existsLeft = (colIndex > 0);
            var existsRight = (colIndex < gridSize - 1);
            var existsUp = (rowIndex < gridSize - 1);
            var existsDown = (rowIndex > 0);

            if (existsLeft) {neighbours.Add(new EntityId(id.Id-1));} // left
            if (existsLeft && existsUp) {neighbours.Add(new EntityId(id.Id-1+gridSize));} // left, up
            if (existsLeft && existsDown) {neighbours.Add(new EntityId(id.Id-1-gridSize));} // left, down
            if (existsUp) {neighbours.Add(new EntityId(id.Id+gridSize));} // up
            if (existsDown) {neighbours.Add(new EntityId(id.Id-gridSize));} // down
            if (existsRight) {neighbours.Add(new EntityId(id.Id+1));} // right
            if (existsRight && existsUp) {neighbours.Add(new EntityId(id.Id+1+gridSize));} // right, up
            if (existsRight && existsDown) {neighbours.Add(new EntityId(id.Id+1-gridSize));} // right, down

            return neighbours;
        }

        private static Entity createEntity(int x_position, int z_position, Improbable.Collections.List<EntityId> neighbours, Random rnd)
        {
            var entity = new Entity();
            const string entityType = "CellularAutomata";
            // Defines worker attribute requirements for workers that can read a component.
            // workers with an attribute of "client" OR "lifeSimulation" will have read access
            var readRequirementSet = new WorkerRequirementSet(
                new Improbable.Collections.List<WorkerAttributeSet>
                {
                    new WorkerAttributeSet(new Improbable.Collections.List<string> {"lifeSimulation"}),
                    new WorkerAttributeSet(new Improbable.Collections.List<string> {"client"}),
                });

            // Defines worker attribute requirements for workers that can write to a component.
            // workers with an attribute of "lifeSimulation" will have write access
            var workerWriteRequirementSet = new WorkerRequirementSet(
                new Improbable.Collections.List<WorkerAttributeSet>
                {
                    new WorkerAttributeSet(new Improbable.Collections.List<string> {"lifeSimulation"}),
                });
            
            var writeAcl = new Improbable.Collections.Map<uint, WorkerRequirementSet>
            {
                {EntityAcl.ComponentId, workerWriteRequirementSet},
                {Position.ComponentId, workerWriteRequirementSet},
                {Cell.CellState.ComponentId, workerWriteRequirementSet},
                {Metadata.ComponentId, workerWriteRequirementSet}
            };

            entity.Add(new EntityAcl.Data(readRequirementSet, writeAcl));
            // Needed for the entity to be persisted in snapshots.
            entity.Add(new Persistence.Data());
            entity.Add(new Metadata.Data(entityType));
            entity.Add(new Position.Data(new Coordinates(x_position, 0, z_position)));
            var start_state = (rnd.NextDouble() > 0.5);
            entity.Add(new Cell.CellState.Data(new Cell.CellStateData(neighbours, 0, start_state, false)));
            return entity;
        }

    }
}
