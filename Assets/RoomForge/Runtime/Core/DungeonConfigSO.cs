using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RoomForge
{
    [CreateAssetMenu(fileName = "DungeonConfig", menuName = "RoomForge/Dungeon Config")]
    public class DungeonConfigSO : ScriptableObject
    {
        [Serializable]
        public class RoomEntry
        {
            public RoomType type;
            public GameObject prefab;
            public int weight = 1;
            public string specialTag;
        }

        [Serializable]
        public class ConnectionRule
        {
            public RoomType from;
            public RoomType to;
        }

        [Serializable]
        public class BranchRule
        {
            public RoomType type;
            public int branchCountMin;
            public int branchCountMax;

            [Tooltip("Must match a Special Room prefab's Special Tag (set in the Rooms list above). " +
                     "The dead-end room of branches grown from this room type will use that prefab. " +
                     "Leave empty to allow any Special Room prefab.")]
            public string deadEndTag;
        }

        public List<RoomEntry> rooms = new List<RoomEntry>();
        public List<ConnectionRule> connections = new List<ConnectionRule>();
        public List<BranchRule> branchRules = new List<BranchRule>();
        public GameObject corridorPrefab;
        public GameObject doorBlockerPrefab;

        public LayoutSolverType layoutSolverType = LayoutSolverType.Grid;

        public int minRoomCount = 5;
        public int maxRoomCount = 15;
        public int maxBranchDepth = 2;
        [Tooltip("If enabled, Special Rooms can only appear as branch dead-ends, never as a step on the critical path from Start to End.")]
        public bool keepSpecialRoomsOffCriticalPath = true;
        [Range(0f, 1f)] public float mergeChance = 0f;
        public Vector2 gridCellSize = new Vector2(10, 10);
        public Vector2 corridorTileSize = new Vector2(2, 2);

        [Header("Background Fill")]
        [Tooltip("If enabled, fills the space around placed rooms and corridors with a background tile.")]
        public bool fillBackground = false;
        [Tooltip("Background tile asset (Sprite → Tile Asset).")]
        public TileBase backgroundTile;
        [Tooltip("World size of one background cell (sprite size in px / Pixels Per Unit).")]
        public Vector2 backgroundTileSize = Vector2.one;
        [Tooltip("Extra margin around rooms/corridors that also gets filled with background.")]
        public float backgroundPadding = 1f;
        [Tooltip("Tilemap sorting order — should be lower than rooms/corridors so background stays behind them.")]
        public int backgroundSortingOrder = -100;

        public bool useRandomSeed = true;
        public int seed;
    }
}
