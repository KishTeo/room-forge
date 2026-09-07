using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    // Cell-index bookkeeping shared by every ILayoutSolver that keeps rooms on a logical
    // grid of unique integer cells (GridLayoutSolver, CompactLayoutSolver). Everything here
    // reasons only about which cells are free/most-open and about Neighbors — never about
    // world-space size or position, which is exactly what differs between the two solvers.
    internal static class GridTopology
    {
        public static readonly Vector2Int[] Directions =
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0)
        };

        public static Vector2Int FindFreeCell(Vector2Int from, Dictionary<Vector2Int, RoomInstance> occupied, System.Random random)
        {
            var freeCandidates = new List<Vector2Int>();
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int candidate = from + dir;
                if (!occupied.ContainsKey(candidate))
                {
                    freeCandidates.Add(candidate);
                }
            }

            if (freeCandidates.Count == 0)
            {
                return FindNearestFreeCell(from, occupied);
            }

            return PickMostOpenCell(freeCandidates, occupied, random);
        }

        public static Vector2Int PickMostOpenCell(List<Vector2Int> candidates, Dictionary<Vector2Int, RoomInstance> occupied, System.Random random)
        {
            int bestScore = -1;
            var best = new List<Vector2Int>();

            foreach (Vector2Int candidate in candidates)
            {
                int score = CountFreeNeighbors(candidate, occupied);
                if (score > bestScore)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(candidate);
                }
                else if (score == bestScore)
                {
                    best.Add(candidate);
                }
            }

            return best[random.Next(best.Count)];
        }

        public static int CountFreeNeighbors(Vector2Int cell, Dictionary<Vector2Int, RoomInstance> occupied)
        {
            int count = 0;
            foreach (Vector2Int dir in Directions)
            {
                if (!occupied.ContainsKey(cell + dir))
                {
                    count++;
                }
            }

            return count;
        }

        public static Vector2Int FindNearestFreeCell(Vector2Int from, Dictionary<Vector2Int, RoomInstance> occupied)
        {
            var visited = new HashSet<Vector2Int> { from };
            var frontier = new List<Vector2Int> { from };

            while (frontier.Count > 0)
            {
                var nextFrontier = new List<Vector2Int>();
                var freeInLayer = new List<Vector2Int>();

                foreach (Vector2Int cell in frontier)
                {
                    foreach (Vector2Int dir in Directions)
                    {
                        Vector2Int candidate = cell + dir;
                        if (!visited.Add(candidate))
                        {
                            continue;
                        }

                        if (occupied.ContainsKey(candidate))
                        {
                            nextFrontier.Add(candidate);
                        }
                        else
                        {
                            freeInLayer.Add(candidate);
                        }
                    }
                }

                if (freeInLayer.Count > 0)
                {
                    // Prefer a cell that isn't collinear with `from` on either axis. A cell
                    // straight off an already-occupied cardinal neighbor (e.g. two cells north)
                    // is exactly where a room's 5th+ neighbor lands most often here — and a
                    // straight corridor to it is then guaranteed to run right through whatever
                    // occupies the cell directly in between.
                    foreach (Vector2Int cell in freeInLayer)
                    {
                        if (cell.x != from.x && cell.y != from.y)
                        {
                            return cell;
                        }
                    }

                    return freeInLayer[0];
                }

                frontier = nextFrontier;
            }

            return from;
        }

        public static void TryMergeBranchLeaves(
            List<RoomInstance> rooms,
            float mergeChance,
            Dictionary<Vector2Int, RoomInstance> occupied,
            Dictionary<RoomInstance, Vector2Int> cellOf,
            System.Random random)
        {
            foreach (RoomInstance room in rooms)
            {
                if (!room.IsBranchLeaf)
                {
                    continue;
                }

                Vector2Int cell = cellOf[room];
                var candidates = new List<RoomInstance>();

                foreach (Vector2Int dir in Directions)
                {
                    if (occupied.TryGetValue(cell + dir, out RoomInstance neighborRoom)
                        && neighborRoom != room
                        && !room.Neighbors.Contains(neighborRoom))
                    {
                        candidates.Add(neighborRoom);
                    }
                }

                if (candidates.Count == 0 || random.NextDouble() >= mergeChance)
                {
                    continue;
                }

                RoomInstance target = candidates[random.Next(candidates.Count)];
                room.Neighbors.Add(target);
                target.Neighbors.Add(room);
            }
        }
    }
}
