using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    public class DungeonGenerator : MonoBehaviour
    {
        public DungeonConfigSO config;
        public int maxGenerationAttempts = 10;

        private readonly Validator _validator = new Validator();
        private Transform _dungeonRoot;

        public event Action OnGenerationComplete;
        public event Action<RoomInstance> OnRoomPlaced;

        public List<RoomInstance> Rooms { get; private set; }
        public RoomInstance StartRoom { get; private set; }
        public RoomInstance EndRoom { get; private set; }

        public void Generate()
        {
            if (config == null)
            {
                Debug.LogError("DungeonGenerator: config is not assigned.");
                return;
            }

            System.Random random = config.useRandomSeed
                ? new System.Random()
                : new System.Random(config.seed);

            var placer = new RoomPlacer(config, random);
            ILayoutSolver layoutSolver = CreateLayoutSolver(config.layoutSolverType);
            List<RoomInstance> rooms = null;
            int attempt = 0;

            while (attempt < Mathf.Max(maxGenerationAttempts, 1))
            {
                attempt++;
                rooms = new GraphBuilder(config, random).Build();
                placer.AssignPrefabs(rooms);
                layoutSolver.Solve(rooms, config, random);

                if (_validator.Validate(rooms, config))
                {
                    break;
                }

                if (attempt == maxGenerationAttempts)
                {
                    Debug.LogWarning("DungeonGenerator: no valid layout found after max attempts, using last generated layout.");
                }
            }

            Rooms = rooms;
            StartRoom = Rooms.Find(r => r.Type == RoomType.StartRoom);
            EndRoom = Rooms.Find(r => r.Type == RoomType.EndRoom);

            if (_dungeonRoot == null)
            {
                _dungeonRoot = new GameObject("Dungeon").transform;
                _dungeonRoot.SetParent(transform);
            }

            placer.PlaceRooms(Rooms, _dungeonRoot);
            placer.PlaceCorridors(Rooms, _dungeonRoot);
            new BackgroundFiller(config).Fill(Rooms, _dungeonRoot);

            foreach (RoomInstance room in Rooms)
            {
                try
                {
                    OnRoomPlaced?.Invoke(room);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            OnGenerationComplete?.Invoke();
        }

        private static ILayoutSolver CreateLayoutSolver(LayoutSolverType type)
        {
            switch (type)
            {
                case LayoutSolverType.Compact:
                    return new CompactLayoutSolver();
                default:
                    return new GridLayoutSolver();
            }
        }

        public void Regenerate()
        {
            if (_dungeonRoot != null)
            {
                for (int i = _dungeonRoot.childCount - 1; i >= 0; i--)
                {
                    var child = _dungeonRoot.GetChild(i).gameObject;
                    if (Application.isPlaying)
                        Destroy(child);
                    else
                        DestroyImmediate(child);
                }
            }

            Rooms = null;
            StartRoom = null;
            EndRoom = null;

            Generate();
        }
    }
}
