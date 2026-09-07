using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RoomForge
{
    public class BackgroundFiller
    {
        private readonly DungeonConfigSO _config;

        public BackgroundFiller(DungeonConfigSO config)
        {
            _config = config;
        }

        public void Fill(List<RoomInstance> rooms, Transform parent)
        {
            if (!_config.fillBackground || _config.backgroundTile == null)
            {
                return;
            }

            if (rooms == null || rooms.Count == 0)
            {
                return;
            }

            var backgroundObject = new GameObject("Background");
            backgroundObject.transform.SetParent(parent, false);

            var grid = backgroundObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(_config.backgroundTileSize.x, _config.backgroundTileSize.y, 1f);

            var tilemap = backgroundObject.AddComponent<Tilemap>();
            var renderer = backgroundObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = _config.backgroundSortingOrder;

            HashSet<Vector3Int> cells = CollectCells(rooms, tilemap);

            foreach (Vector3Int cell in cells)
            {
                tilemap.SetTile(cell, _config.backgroundTile);
            }
        }

        private HashSet<Vector3Int> CollectCells(List<RoomInstance> rooms, Tilemap tilemap)
        {
            var cells = new HashSet<Vector3Int>();
            float padding = _config.backgroundPadding;

            foreach (RoomInstance room in rooms)
            {
                AddRect(cells, tilemap, room.Position, room.Size, padding);
            }

            var corridorGenerator = new CorridorGenerator(_config.corridorTileSize);
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
                    foreach (CorridorTile tile in path)
                    {
                        // BuildCorridor (RoomPlacer) rotates the corridor prefab 90° for
                        // vertical segments, so its unrotated (x = length, y = width)
                        // tileSize footprint swaps axes to match.
                        Vector2 footprint = tile.IsHorizontal
                            ? _config.corridorTileSize
                            : new Vector2(_config.corridorTileSize.y, _config.corridorTileSize.x);

                        AddRect(cells, tilemap, tile.Position, footprint, padding);
                    }
                }
            }

            return cells;
        }

        private static void AddRect(HashSet<Vector3Int> cells, Tilemap tilemap, Vector2 center, Vector2 size, float padding)
        {
            Vector2 half = size / 2f + Vector2.one * padding;
            Vector3Int min = tilemap.WorldToCell(new Vector3(center.x - half.x, center.y - half.y, 0f));
            Vector3Int max = tilemap.WorldToCell(new Vector3(center.x + half.x, center.y + half.y, 0f));

            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    cells.Add(new Vector3Int(x, y, 0));
                }
            }
        }
    }
}
