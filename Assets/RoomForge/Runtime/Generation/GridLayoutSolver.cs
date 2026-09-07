using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class GridLayoutSolver : ILayoutSolver
    {
        public void Solve(List<RoomInstance> rooms, DungeonConfigSO config, System.Random random)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return;
            }

            var occupied = new Dictionary<Vector2Int, RoomInstance>();
            var cellOf = new Dictionary<RoomInstance, Vector2Int>();

            RoomInstance start = rooms[0];
            PlaceRoom(start, Vector2Int.zero, config.gridCellSize, occupied, cellOf);

            var visited = new HashSet<RoomInstance> { start };
            var queue = new Queue<RoomInstance>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                RoomInstance current = queue.Dequeue();
                Vector2Int currentCell = cellOf[current];

                foreach (RoomInstance neighbor in current.Neighbors)
                {
                    if (visited.Contains(neighbor))
                    {
                        continue;
                    }

                    Vector2Int cell = GridTopology.FindFreeCell(currentCell, occupied, random);
                    PlaceRoom(neighbor, cell, config.gridCellSize, occupied, cellOf);
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            GridTopology.TryMergeBranchLeaves(rooms, config.mergeChance, occupied, cellOf, random);
        }

        private static void PlaceRoom(
            RoomInstance room,
            Vector2Int cell,
            Vector2 cellSize,
            Dictionary<Vector2Int, RoomInstance> occupied,
            Dictionary<RoomInstance, Vector2Int> cellOf)
        {
            occupied[cell] = room;
            cellOf[room] = cell;
            room.Position = new Vector2(cell.x * cellSize.x, cell.y * cellSize.y);
        }
    }
}
