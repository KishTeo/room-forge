using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public readonly struct CorridorTile
    {
        public readonly Vector2 Position;
        public readonly bool IsHorizontal;

        public CorridorTile(Vector2 position, bool isHorizontal)
        {
            Position = position;
            IsHorizontal = isHorizontal;
        }
    }

    public class CorridorGenerator
    {
        private readonly Vector2 _tileSize;

        public CorridorGenerator(Vector2 tileSize)
        {
            _tileSize = tileSize;
        }

        public List<CorridorTile> BuildPath(Vector2 from, Vector2 to)
        {
            var path = new List<CorridorTile>();
            Vector2 corner = new Vector2(to.x, from.y);

            // Both segments step by tileSize.x: that's the tile's length along its
            // direction of travel in its unrotated (horizontal) form. A vertical run
            // rotates the same tile 90 degrees, so that length becomes its vertical
            // extent too — tileSize.y is only the corridor's perpendicular width and
            // never affects spacing.
            AppendSegment(path, from, corner, _tileSize.x, horizontal: true);
            AppendSegment(path, corner, to, _tileSize.x, horizontal: false);

            return path;
        }

        public List<CorridorTile> BuildPathBetweenRooms(Vector2 posA, Vector2 sizeA, Vector2 posB, Vector2 sizeB)
        {
            Vector2 direction = posB - posA;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return new List<CorridorTile>();
            }

            Vector2 unitDir = direction.normalized;
            Vector2 edgeA = posA + unitDir * HalfExtentAlongDirection(sizeA, unitDir);
            Vector2 edgeB = posB - unitDir * HalfExtentAlongDirection(sizeB, unitDir);

            if (Vector2.Dot(edgeB - edgeA, unitDir) <= 0f)
            {
                return new List<CorridorTile>();
            }

            return BuildPath(edgeA, edgeB);
        }

        private static float HalfExtentAlongDirection(Vector2 size, Vector2 direction)
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? size.x / 2f
                : size.y / 2f;
        }

        private static void AppendSegment(List<CorridorTile> path, Vector2 start, Vector2 end, float tileSize, bool horizontal)
        {
            float length = horizontal ? end.x - start.x : end.y - start.y;
            int count = Mathf.RoundToInt(Mathf.Abs(length) / tileSize);
            if (count == 0)
            {
                return;
            }

            float step = tileSize * Mathf.Sign(length);

            for (int i = 0; i < count; i++)
            {
                float offset = step * (i + 0.5f);
                Vector2 position = horizontal
                    ? new Vector2(start.x + offset, start.y)
                    : new Vector2(start.x, start.y + offset);
                path.Add(new CorridorTile(position, horizontal));
            }
        }
    }
}
