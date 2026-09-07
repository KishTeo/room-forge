using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class RoomInstance
    {
        public RoomType Type { get; set; }
        public List<RoomInstance> Neighbors { get; set; }
        public Vector2 Position { get; set; }
        public GameObject Prefab { get; set; }
        public Vector2 Size { get; set; }
        public RoomDefinition Definition { get; set; }
        public bool IsBranchLeaf { get; set; }
        public string SpecialTag { get; set; }

        // The grid places every neighbor along a purely axial vector, so this always
        // resolves to one of the 4 cardinal directions — null only for a degenerate
        // same-position edge (not expected to occur, but not this method's job to assert).
        public DoorDirection? GetDirectionTo(RoomInstance neighbor)
        {
            Vector2 delta = neighbor.Position - Position;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return null;
            }

            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return delta.x > 0 ? DoorDirection.East : DoorDirection.West;
            }

            return delta.y > 0 ? DoorDirection.North : DoorDirection.South;
        }
    }
}
