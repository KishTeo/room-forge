using System.Collections.Generic;
using UnityEngine;

namespace RoomForge
{
    // Same 4-direction adjacency as GridLayoutSolver (still compatible with the fixed
    // DoorAnchor_N/S/E/W prefab doors — every neighbor is still found along one cardinal
    // axis), but the distance to each neighbor comes from the pair's actual
    // RoomDefinition.size instead of one uniform gridCellSize sized for the biggest prefab
    // in the whole config. A boss room and a closet no longer get identical spacing.
    //
    // Variable per-edge distance means, unlike a uniform grid, distinct cell indices don't
    // guarantee distinct world-space footprints (two unrelated branches can independently
    // drift into the same area) — so placement is followed by a bounded iterative overlap
    // resolution pass. It's a best-effort correction, not a guarantee: DungeonGenerator's
    // existing retry loop (Validator + maxGenerationAttempts) is the real backstop for any
    // layout this doesn't fully untangle.
    public class CompactLayoutSolver : ILayoutSolver
    {
        private const int MaxResolveIterations = 60;
        private const float Relaxation = 0.5f;

        // See Validator.OverlapEpsilon: a pair placed exactly touching by PlaceRelativeTo
        // computes that same distance through a different sequence of float operations here,
        // so the connecting axis lands a hair shy of zero far more often than not. Without
        // slack, every ordinary touching pair reads as needing a (phantom) push.
        private const float OverlapEpsilon = 0.05f;

        public void Solve(List<RoomInstance> rooms, DungeonConfigSO config, System.Random random)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return;
            }

            var occupied = new Dictionary<Vector2Int, RoomInstance>();
            var cellOf = new Dictionary<RoomInstance, Vector2Int>();
            var childrenOf = new Dictionary<RoomInstance, List<RoomInstance>>();

            RoomInstance start = rooms[0];
            occupied[Vector2Int.zero] = start;
            cellOf[start] = Vector2Int.zero;
            start.Position = Vector2.zero;

            var visited = new HashSet<RoomInstance> { start };
            var queue = new Queue<RoomInstance>();
            queue.Enqueue(start);

            float margin = config.corridorTileSize.x;

            while (queue.Count > 0)
            {
                RoomInstance current = queue.Dequeue();
                Vector2Int currentCell = cellOf[current];

                foreach (RoomInstance neighbor in current.Neighbors)
                {
                    if (visited.Contains(neighbor))
                    {
                        continue;
                    }

                    Vector2Int cell = GridTopology.FindFreeCell(currentCell, occupied, random);
                    PlaceRelativeTo(current, neighbor, cell - currentCell, margin, cell, occupied, cellOf);
                    AddChild(childrenOf, current, neighbor);
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            GridTopology.TryMergeBranchLeaves(rooms, config.mergeChance, occupied, cellOf, random);
            ResolveOverlaps(rooms, start, margin, childrenOf);
        }

        private static void AddChild(Dictionary<RoomInstance, List<RoomInstance>> childrenOf, RoomInstance parent, RoomInstance child)
        {
            if (!childrenOf.TryGetValue(parent, out List<RoomInstance> children))
            {
                children = new List<RoomInstance>();
                childrenOf[parent] = children;
            }

            children.Add(child);
        }

        private static void PlaceRelativeTo(
            RoomInstance parent,
            RoomInstance room,
            Vector2Int cellDelta,
            float margin,
            Vector2Int cell,
            Dictionary<Vector2Int, RoomInstance> occupied,
            Dictionary<RoomInstance, Vector2Int> cellOf)
        {
            occupied[cell] = room;
            cellOf[room] = cell;

            if (Mathf.Abs(cellDelta.x) + Mathf.Abs(cellDelta.y) == 1)
            {
                // Direct cardinal step from the parent — space it by the pair's actual half-sizes.
                var direction = new Vector2(cellDelta.x, cellDelta.y);
                float distance = HalfExtentAlongAxis(parent.Size, direction) + HalfExtentAlongAxis(room.Size, direction) + margin;
                room.Position = parent.Position + direction * distance;
            }
            else
            {
                // GridTopology.FindNearestFreeCell fallback (parent already has all 4 cardinal
                // cells occupied) — no single parent edge to measure from. This placement itself
                // must guarantee no overlap with the parent, not just estimate a plausible one:
                // ResolveOverlaps repairs a parent-child pair only after something else has
                // already disturbed it (see TrySeparate's ancestor-descendant branch) — it isn't
                // a substitute for placing this correctly to begin with. Offsetting by the full
                // per-axis clearance on every non-zero axis of cellDelta (rather than a single
                // scalar distance along the — usually non-unit — direction vector) guarantees
                // that regardless of how far or in which combination of directions the fallback
                // cell ended up.
                float clearanceX = (parent.Size.x + room.Size.x) / 2f + margin;
                float clearanceY = (parent.Size.y + room.Size.y) / 2f + margin;
                var offset = new Vector2(Mathf.Sign(cellDelta.x) * clearanceX, Mathf.Sign(cellDelta.y) * clearanceY);
                room.Position = parent.Position + offset;
            }
        }

        private static float HalfExtentAlongAxis(Vector2 size, Vector2 direction)
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) ? size.x / 2f : size.y / 2f;
        }

        private static void ResolveOverlaps(
            List<RoomInstance> rooms,
            RoomInstance pinned,
            float margin,
            Dictionary<RoomInstance, List<RoomInstance>> childrenOf)
        {
            for (int iteration = 0; iteration < MaxResolveIterations; iteration++)
            {
                bool overlapFound = false;

                for (int i = 0; i < rooms.Count; i++)
                {
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        if (TrySeparate(rooms[i], rooms[j], margin, pinned, childrenOf))
                        {
                            overlapFound = true;
                        }
                    }
                }

                if (!overlapFound)
                {
                    break;
                }
            }
        }

        // Moves whole placement-tree branches, never a single room in isolation — a direct
        // parent-child pair is placed exactly touching (zero slack) by PlaceRelativeTo, so
        // nudging just one of them independently would routinely manufacture a brand new
        // overlap between the two, which the next iteration "fixes" by nudging it back,
        // oscillating indefinitely instead of converging. Rigidly translating the whole
        // subtree preserves every internal parent-child gap it contains.
        private static bool TrySeparate(
            RoomInstance a,
            RoomInstance b,
            float margin,
            RoomInstance pinned,
            Dictionary<RoomInstance, List<RoomInstance>> childrenOf)
        {
            Vector2 delta = b.Position - a.Position;
            Vector2 combinedHalf = (a.Size + b.Size) / 2f + Vector2.one * margin;

            float overlapX = combinedHalf.x - Mathf.Abs(delta.x);
            float overlapY = combinedHalf.y - Mathf.Abs(delta.y);

            if (overlapX <= OverlapEpsilon || overlapY <= OverlapEpsilon)
            {
                return false;
            }

            bool aPinned = a == pinned;
            bool bPinned = b == pinned;

            if (aPinned && bPinned)
            {
                return false;
            }

            // Minimum translation vector: push apart along the axis with the smaller overlap.
            // Under-relaxed (fractional) push damps the back-and-forth between competing
            // conflicts instead of fully resolving one and re-provoking another each pass.
            Vector2 push;
            if (overlapX < overlapY)
            {
                float sign = delta.x >= 0f ? 1f : -1f;
                push = new Vector2(sign * overlapX, 0f);
            }
            else
            {
                float sign = delta.y >= 0f ? 1f : -1f;
                push = new Vector2(0f, sign * overlapY);
            }

            push *= Relaxation;

            if (aPinned)
            {
                Translate(GetSubtree(b, childrenOf), push);
                return true;
            }

            if (bPinned)
            {
                Translate(GetSubtree(a, childrenOf), -push);
                return true;
            }

            // If b is a's own descendant, moving subtree(a) would drag b along with it (b is
            // part of it) — applying a second, independent push to subtree(b) on top of that
            // isn't well-defined. Only subtree(b) moves here, by the full (undamped-by-split)
            // push, exactly like the pinned case: a is a fixed reference point for b's subtree,
            // it doesn't need to move for this pair to stop overlapping. This is also what
            // repairs a hinge edge — a direct parent-child gap that PlaceRelativeTo placed
            // exactly right, but a later, unrelated push moved the child's whole subtree just
            // enough to violate it; without this, such a pair would sit slightly overlapped
            // forever; ancestor-descendant pairs used to be skipped entirely, but that assumed
            // every such pair remains "exactly right" for the whole run, which only holds until
            // the first push involving one of their subtrees moves it.
            HashSet<RoomInstance> subtreeA = GetSubtree(a, childrenOf);
            if (subtreeA.Contains(b))
            {
                Translate(GetSubtree(b, childrenOf), push);
                return true;
            }

            HashSet<RoomInstance> subtreeB = GetSubtree(b, childrenOf);
            if (subtreeB.Contains(a))
            {
                Translate(GetSubtree(a, childrenOf), -push);
                return true;
            }

            Translate(subtreeA, -push * 0.5f);
            Translate(subtreeB, push * 0.5f);
            return true;
        }

        private static HashSet<RoomInstance> GetSubtree(RoomInstance root, Dictionary<RoomInstance, List<RoomInstance>> childrenOf)
        {
            var result = new HashSet<RoomInstance> { root };
            var stack = new Stack<RoomInstance>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                RoomInstance node = stack.Pop();
                if (!childrenOf.TryGetValue(node, out List<RoomInstance> children))
                {
                    continue;
                }

                foreach (RoomInstance child in children)
                {
                    if (result.Add(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            return result;
        }

        private static void Translate(IEnumerable<RoomInstance> rooms, Vector2 delta)
        {
            foreach (RoomInstance room in rooms)
            {
                room.Position += delta;
            }
        }
    }
}
