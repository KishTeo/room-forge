using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class RoomDefinition : MonoBehaviour
    {
        private static readonly Dictionary<string, DoorDirection> TagToDirection = new Dictionary<string, DoorDirection>
        {
            { "DoorAnchor_N", DoorDirection.North },
            { "DoorAnchor_S", DoorDirection.South },
            { "DoorAnchor_E", DoorDirection.East },
            { "DoorAnchor_W", DoorDirection.West }
        };

        public RoomType type;
        public Vector2 size = new Vector2(8, 8);

        public Dictionary<DoorDirection, Transform> DoorAnchors { get; } = new Dictionary<DoorDirection, Transform>();
        public List<Transform> SpawnPoints { get; } = new List<Transform>();

        private void Awake()
        {
            CollectDoorAnchors();
            CollectSpawnPoints();
        }

        public void CollectDoorAnchors()
        {
            DoorAnchors.Clear();

            foreach (Transform child in transform)
            {
                if (TagToDirection.TryGetValue(child.tag, out DoorDirection direction))
                {
                    DoorAnchors[direction] = child;
                }
            }
        }

        public void CollectSpawnPoints()
        {
            SpawnPoints.Clear();

            foreach (SpawnPoint point in GetComponentsInChildren<SpawnPoint>(true))
            {
                SpawnPoints.Add(point.transform);
            }
        }
    }
}
