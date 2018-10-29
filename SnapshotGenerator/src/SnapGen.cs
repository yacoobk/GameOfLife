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
            const int gridSize = 10;

            Console.WriteLine(String.Format("Generating snapshot file {0}", snapshotPath));
            Assembly.Load("GeneratedCode");
            using (var snapshotOutput = new SnapshotOutputStream(snapshotPath))
            {
                var entityCounter = 1;

                for (var i = 0; i < gridSize; i++)
                {
                    for (var j = 0; j < gridSize; j++)
                    {
                        var entityId = new EntityId(entityCounter++);
                        var entity = createEntity(i, j);
                        var error = snapshotOutput.WriteEntity(entityId, entity);
                        if (error.HasValue)
                        {
                            throw new System.SystemException("error saving: " + error.Value);
                        }
                    }
                }
            }
        }

        private static Entity createEntity(int x_position, int z_position)
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
                {Cell.CellState.ComponentId, workerWriteRequirementSet}
            };

            entity.Add(new EntityAcl.Data(readRequirementSet, writeAcl));
            // Needed for the entity to be persisted in snapshots.
            entity.Add(new Persistence.Data());
            entity.Add(new Metadata.Data(entityType));
            entity.Add(new Position.Data(new Coordinates(x_position, 0, z_position)));
            entity.Add(new Cell.CellState.Data(false));
            return entity;
        }

    }
}
