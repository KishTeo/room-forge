using System;
using System.Collections.Generic;

namespace RoomForge
{
    public class GraphBuilder
    {
        private readonly DungeonConfigSO _config;
        private readonly Random _random;

        public GraphBuilder(DungeonConfigSO config, Random random)
        {
            _config = config;
            _random = random;
        }

        public List<RoomInstance> Build()
        {
            int pathLength = _random.Next(_config.minRoomCount, _config.maxRoomCount + 1);
            pathLength = Math.Max(pathLength, 2);

            RoomInstance start = CreateRoom(RoomType.StartRoom);
            var rooms = new List<RoomInstance> { start };

            RoomInstance current = start;
            int criticalPathCount = 1;
            while (criticalPathCount < pathLength - 1)
            {
                RoomType nextType = PickNextType(current.Type);
                RoomInstance next = CreateRoom(nextType);
                Connect(current, next);
                rooms.Add(next);
                criticalPathCount++;

                BuildBranches(current, rooms);
                current = next;
            }

            BuildBranches(current, rooms);

            RoomInstance end = CreateRoom(RoomType.EndRoom);
            Connect(current, end);
            rooms.Add(end);

            return rooms;
        }

        private void BuildBranches(RoomInstance from, List<RoomInstance> rooms)
        {
            DungeonConfigSO.BranchRule rule = FindBranchRule(from.Type);
            if (rule == null)
            {
                return;
            }

            int min = Math.Min(rule.branchCountMin, rule.branchCountMax);
            int max = Math.Max(rule.branchCountMin, rule.branchCountMax);
            int branchCount = _random.Next(min, max + 1);

            for (int i = 0; i < branchCount; i++)
            {
                int depth = _random.Next(1, Math.Max(_config.maxBranchDepth, 1) + 1);
                BuildDeadEndBranch(from, depth, rooms, rule.deadEndTag);
            }
        }

        private void BuildDeadEndBranch(RoomInstance root, int depth, List<RoomInstance> rooms, string deadEndTag)
        {
            RoomInstance branchCurrent = root;
            RoomInstance leaf = null;
            for (int i = 0; i < depth; i++)
            {
                bool isLast = i == depth - 1;
                RoomType type = isLast ? RoomType.SpecialRoom : RoomType.NormalRoom;
                RoomInstance node = CreateRoom(type);
                Connect(branchCurrent, node);
                rooms.Add(node);
                branchCurrent = node;
                if (isLast)
                {
                    leaf = node;
                }
            }

            if (leaf != null)
            {
                leaf.IsBranchLeaf = true;
                leaf.SpecialTag = deadEndTag;
            }
        }

        private DungeonConfigSO.BranchRule FindBranchRule(RoomType type)
        {
            foreach (DungeonConfigSO.BranchRule rule in _config.branchRules)
            {
                if (rule.type == type)
                {
                    return rule;
                }
            }

            return null;
        }

        private RoomType PickNextType(RoomType from)
        {
            var allowedTypes = new List<RoomType>();
            foreach (DungeonConfigSO.ConnectionRule rule in _config.connections)
            {
                if (rule.from != from || rule.to == RoomType.StartRoom || rule.to == RoomType.EndRoom)
                {
                    continue;
                }

                if (_config.keepSpecialRoomsOffCriticalPath && rule.to == RoomType.SpecialRoom)
                {
                    continue;
                }

                allowedTypes.Add(rule.to);
            }

            if (allowedTypes.Count == 0)
            {
                return RoomType.NormalRoom;
            }

            return allowedTypes[_random.Next(allowedTypes.Count)];
        }

        private static RoomInstance CreateRoom(RoomType type)
        {
            return new RoomInstance
            {
                Type = type,
                Neighbors = new List<RoomInstance>()
            };
        }

        private static void Connect(RoomInstance a, RoomInstance b)
        {
            a.Neighbors.Add(b);
            b.Neighbors.Add(a);
        }
    }
}
