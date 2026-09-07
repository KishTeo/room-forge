using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class Validator
    {
        public bool Validate(List<RoomInstance> rooms, DungeonConfigSO config)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return false;
            }

            return IsFullyConnected(rooms) && !HasRoomOverlap(rooms, config) && !HasCorridorOverlap(rooms, config);
        }

        private static bool HasRoomOverlap(List<RoomInstance> rooms, DungeonConfigSO config)
        {
            // GridLayoutSolver can never produce this (uniform cell size guarantees distinct
            // cells never share world space) but a size-aware solver like CompactLayoutSolver
            // only resolves overlaps best-effort — this is the real backstop for what it
            // doesn't fully untangle, feeding the existing retry loop in DungeonGenerator.
            float margin = config.corridorTileSize.x;

            for (int i = 0; i < rooms.Count; i++)
            {
                for (int j = i + 1; j < rooms.Count; j++)
                {
                    if (RoomsOverlap(rooms[i], rooms[j], margin))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // A pair placed exactly touching (CompactLayoutSolver.PlaceRelativeTo's direct-step
        // formula) computes that same "just touching" distance through a different sequence of
        // float operations than this check does — algebraically equal, not bit-identical, so
        // the connecting axis lands a fraction of a unit shy of zero far more often than not.
        // Without slack that reads as a real overlap for every ordinary touching pair.
        private const float OverlapEpsilon = 0.05f;

        private static bool RoomsOverlap(RoomInstance a, RoomInstance b, float margin)
        {
            Vector2 delta = b.Position - a.Position;
            Vector2 combinedHalf = (a.Size + b.Size) / 2f + Vector2.one * margin;

            return combinedHalf.x - Mathf.Abs(delta.x) > OverlapEpsilon && combinedHalf.y - Mathf.Abs(delta.y) > OverlapEpsilon;
        }

        private static bool IsFullyConnected(List<RoomInstance> rooms)
        {
            var visited = new HashSet<RoomInstance> { rooms[0] };
            var queue = new Queue<RoomInstance>();
            queue.Enqueue(rooms[0]);

            while (queue.Count > 0)
            {
                RoomInstance current = queue.Dequeue();
                foreach (RoomInstance neighbor in current.Neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited.Count == rooms.Count;
        }

        private static bool HasCorridorOverlap(List<RoomInstance> rooms, DungeonConfigSO config)
        {
            var corridorGenerator = new CorridorGenerator(config.corridorTileSize);
            var builtEdges = new HashSet<(int, int)>();

            for (int i = 0; i < rooms.Count; i++)
            {
                RoomInstance a = rooms[i];
                foreach (RoomInstance b in a.Neighbors)
                {
                    int j = rooms.IndexOf(b);
                    (int, int) key = i < j ? (i, j) : (j, i);
                    if (!builtEdges.Add(key))
                    {
                        continue;
                    }

                    List<CorridorTile> path = corridorGenerator.BuildPathBetweenRooms(a.Position, a.Size, b.Position, b.Size);
                    if (PathOverlapsOtherRoom(path, rooms, a, b))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PathOverlapsOtherRoom(List<CorridorTile> path, List<RoomInstance> rooms, RoomInstance a, RoomInstance b)
        {
            foreach (CorridorTile tile in path)
            {
                foreach (RoomInstance room in rooms)
                {
                    if (room == a || room == b)
                    {
                        continue;
                    }

                    if (IsPointInsideRoom(tile.Position, room))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsPointInsideRoom(Vector2 point, RoomInstance room)
        {
            Vector2 halfSize = room.Size * 0.5f;
            Vector2 min = room.Position - halfSize;
            Vector2 max = room.Position + halfSize;

            return point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;
        }
    }
}
