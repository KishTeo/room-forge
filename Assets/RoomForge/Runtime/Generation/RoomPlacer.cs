using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class RoomPlacer
    {
        private readonly DungeonConfigSO _config;
        private readonly System.Random _random;

        public RoomPlacer(DungeonConfigSO config, System.Random random)
        {
            _config = config;
            _random = random;
        }

        public void AssignPrefabs(List<RoomInstance> rooms)
        {
            foreach (RoomInstance room in rooms)
            {
                room.Prefab = PickPrefab(room.Type, room.SpecialTag);
                RoomDefinition definition = room.Prefab != null ? room.Prefab.GetComponent<RoomDefinition>() : null;
                room.Size = definition != null ? definition.size : Vector2.zero;
            }
        }

        public void PlaceRooms(List<RoomInstance> rooms, Transform parent)
        {
            foreach (RoomInstance room in rooms)
            {
                if (room.Prefab == null)
                {
                    continue;
                }

                GameObject instance = Object.Instantiate(room.Prefab, room.Position, Quaternion.identity, parent);
                room.Definition = instance.GetComponent<RoomDefinition>();
            }

            CloseUnusedDoors(rooms);
        }

        private void CloseUnusedDoors(List<RoomInstance> rooms)
        {
            if (_config.doorBlockerPrefab == null)
            {
                return;
            }

            foreach (RoomInstance room in rooms)
            {
                if (room.Definition == null)
                {
                    continue;
                }

                var connectedDirections = new HashSet<DoorDirection>();
                foreach (RoomInstance neighbor in room.Neighbors)
                {
                    DoorDirection? direction = room.GetDirectionTo(neighbor);
                    if (direction.HasValue)
                    {
                        connectedDirections.Add(direction.Value);
                    }
                }

                foreach (KeyValuePair<DoorDirection, Transform> anchor in room.Definition.DoorAnchors)
                {
                    if (connectedDirections.Contains(anchor.Key))
                    {
                        continue;
                    }

                    Quaternion rotation = anchor.Value.rotation * BlockerRotation(anchor.Key);
                    Object.Instantiate(_config.doorBlockerPrefab, anchor.Value.position, rotation, anchor.Value);

                    // The open-doorway graphic lives directly on the anchor, so it has to be
                    // hidden (not the anchor GameObject deactivated — that would also disable
                    // the blocker we just parented under it).
                    SpriteRenderer doorSprite = anchor.Value.GetComponent<SpriteRenderer>();
                    if (doorSprite != null)
                    {
                        doorSprite.enabled = false;
                    }
                }
            }
        }

        // Mirrors CorridorGenerator's convention: the blocker prefab's unrotated form is a
        // horizontal wall segment (fits a North/South opening); an East/West opening sits in
        // a vertical wall, so the same piece rotates 90° to match.
        private static Quaternion BlockerRotation(DoorDirection direction)
        {
            return direction == DoorDirection.East || direction == DoorDirection.West
                ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.identity;
        }

        public void PlaceCorridors(List<RoomInstance> rooms, Transform parent)
        {
            if (rooms == null || rooms.Count == 0 || _config.corridorPrefab == null)
            {
                return;
            }

            var corridorGenerator = new CorridorGenerator(_config.corridorTileSize);
            var builtEdges = new HashSet<(int, int)>();

            for (int i = 0; i < rooms.Count; i++)
            {
                RoomInstance current = rooms[i];
                foreach (RoomInstance neighbor in current.Neighbors)
                {
                    int j = rooms.IndexOf(neighbor);
                    (int, int) key = i < j ? (i, j) : (j, i);
                    if (!builtEdges.Add(key))
                    {
                        continue;
                    }

                    BuildCorridor(corridorGenerator, current, neighbor, parent);
                }
            }
        }

        private void BuildCorridor(CorridorGenerator corridorGenerator, RoomInstance a, RoomInstance b, Transform parent)
        {
            if (a.Definition == null || b.Definition == null)
            {
                return;
            }

            List<CorridorTile> path = corridorGenerator.BuildPathBetweenRooms(a.Position, a.Definition.size, b.Position, b.Definition.size);

            foreach (CorridorTile tile in path)
            {
                Quaternion rotation = tile.IsHorizontal ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
                Object.Instantiate(_config.corridorPrefab, tile.Position, rotation, parent);
            }
        }

        private GameObject PickPrefab(RoomType type, string tag)
        {
            var candidates = _config.rooms.FindAll(entry => entry.type == type && entry.prefab != null);
            if (candidates.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(tag))
            {
                var tagged = candidates.FindAll(entry => entry.specialTag == tag);
                if (tagged.Count > 0)
                {
                    candidates = tagged;
                }
                else
                {
                    Debug.LogWarning($"RoomPlacer: no {type} prefab tagged '{tag}' — falling back to any {type} prefab.");
                }
            }

            int totalWeight = 0;
            foreach (DungeonConfigSO.RoomEntry entry in candidates)
            {
                totalWeight += Mathf.Max(entry.weight, 1);
            }

            int roll = _random.Next(totalWeight);
            int cumulative = 0;

            foreach (DungeonConfigSO.RoomEntry entry in candidates)
            {
                cumulative += Mathf.Max(entry.weight, 1);
                if (roll < cumulative)
                {
                    return entry.prefab;
                }
            }

            return candidates[candidates.Count - 1].prefab;
        }
    }
}
