using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RoomForge
{
    public class RoomForgeEditorWindow : EditorWindow
    {
        private static readonly RoomType[] NodeTypes =
        {
            RoomType.StartRoom,
            RoomType.EndRoom,
            RoomType.NormalRoom,
            RoomType.SpecialRoom
        };

        // Nodes are small dots — the graph itself (dots + connecting lines) stays compact and
        // legible, while each node's Type label / Dead-end / Branch Count fields live in a
        // block docked to one side of its dot, out of the way of every line between dots.
        private const float NodeDotRadius = 7f;
        private const float NodeHitRadius = 14f;
        // Must clear the self-loop icon's radius (NodeDotRadius + 7) around the dot, or the
        // loop arcs on Start/End visually overlap their own field block by a couple pixels.
        private const float NodeBlockGap = 16f;
        private const float FieldBlockWidth = 120f;
        private const float FieldBlockHeight = 71f; // label 16 + tag 18 + caption 14 + fields 18 + gaps
        private const float LabelOnlyBlockWidth = 110f;
        private const float LabelOnlyBlockHeight = 18f;

        private enum DockSide { Left, Right }

        // Bounded choices for Branch Count Min/Max — a dropdown instead of a free int field so
        // a value can't be typed that silently blows past what the dungeon size is meant to
        // support (this rule fires once per critical-path room of that type, so even small
        // numbers multiply fast across a run).
        private static readonly string[] BranchCountOptions = { "0", "1", "2", "3", "4", "5" };

        private DungeonConfigSO _config;
        private SerializedObject _serializedConfig;
        private Vector2 _scrollPosition;
        private RoomType? _selectedNode;
        private int _previewSeed = 12345;

        // OnGUI fires far more often than the config actually changes (every mouse move over
        // the window, every repaint) — regenerating the preview unconditionally on each call
        // made the window visibly laggy once GeneratePreviewRooms started retrying up to 10x on
        // validation failure. Cache the result and only recompute when something that could
        // affect it actually changed.
        private List<RoomInstance> _cachedPreviewRooms;
        private bool _cachedPreviewValidated;
        private bool _previewDirty = true;

        [MenuItem("Window/RoomForge")]
        public static void ShowWindow()
        {
            GetWindow<RoomForgeEditorWindow>("RoomForge");
        }

        private void OnGUI()
        {
            DrawConfigPicker();

            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign a DungeonConfigSO asset to start editing.", MessageType.Info);
                return;
            }

            if (_serializedConfig == null)
            {
                _serializedConfig = new SerializedObject(_config);
            }

            _serializedConfig.Update();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawValidationMessages();

            EditorGUILayout.Space();
            DrawPreview();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("rooms"), true);

            EditorGUILayout.Space();
            DrawConnectionGraph();

            EditorGUILayout.Space();
            DrawGenerationSettings();

            EditorGUILayout.EndScrollView();

            // ApplyModifiedProperties() returns whether anything actually changed this call —
            // covers both normal PropertyField edits and the raw SerializedProperty mutations
            // ToggleConnection/FindOrCreateBranchRuleIndex make when you click the graph, which
            // don't set GUI.changed. Either kind should invalidate the cached preview.
            if (_serializedConfig.ApplyModifiedProperties())
            {
                _previewDirty = true;
                Repaint();
            }
        }

        private void DrawConfigPicker()
        {
            EditorGUI.BeginChangeCheck();
            var newConfig = (DungeonConfigSO)EditorGUILayout.ObjectField("Dungeon Config", _config, typeof(DungeonConfigSO), false);
            if (EditorGUI.EndChangeCheck())
            {
                _config = newConfig;
                _serializedConfig = _config != null ? new SerializedObject(_config) : null;
                _selectedNode = null;
                _previewDirty = true;
            }
        }

        private void DrawConnectionGraph()
        {
            EditorGUILayout.LabelField("Connections & Branching", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Click a room dot, then click another to toggle a connection. Click the same dot again to toggle a self-loop (loop icon).", MessageType.None);

            string status = _selectedNode.HasValue
                ? $"Selected: {_selectedNode.Value} — click a room to connect to it, or click {_selectedNode.Value} again for a self-loop."
                : "Nothing selected — click a room to start a connection.";
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);

            Rect canvas = GUILayoutUtility.GetRect(400, 190);
            GUI.Box(canvas, GUIContent.none);

            SerializedProperty connectionsProp = _serializedConfig.FindProperty("connections");
            SerializedProperty branchRulesProp = _serializedConfig.FindProperty("branchRules");

            DrawConnectionLines(canvas, connectionsProp);

            foreach (RoomType type in NodeTypes)
            {
                DrawNode(canvas, type, branchRulesProp);
            }

            HandleNodeClicks(canvas, connectionsProp);
        }

        private void DrawNode(Rect canvas, RoomType type, SerializedProperty branchRulesProp)
        {
            Vector2 center = GetNodeCenter(canvas, type);
            bool isSelected = _selectedNode == type;

            Handles.BeginGUI();
            Handles.color = GetRoomColor(type);
            Handles.DrawSolidDisc(center, Vector3.forward, NodeDotRadius);
            Handles.color = isSelected ? Color.yellow : Color.black;
            Handles.DrawWireDisc(center, Vector3.forward, isSelected ? NodeDotRadius + 2f : NodeDotRadius);
            Handles.EndGUI();

            DockSide side = GetDockSide(type);

            // EndRoom is never the source of a branch: GraphBuilder.Build only ever calls
            // BuildBranches(current, ...) while `current` is still on the critical path, and
            // PickNextType excludes EndRoom as a mid-path candidate — EndRoom is only ever
            // appended after the loop, as a destination. A BranchRule configured here would be
            // silently ignored, so don't offer fields that can't do anything — just the label.
            if (type == RoomType.EndRoom)
            {
                // Anchored to the same y-offset from its dot as StartRoom's label (top of a
                // FieldBlockHeight-tall block), not vertically centered on the dot like a
                // label-only block normally would be — so the two top-row labels line up.
                float labelY = center.y - FieldBlockHeight / 2f;
                Rect endLabelRect = new Rect(center.x + NodeBlockGap, labelY, LabelOnlyBlockWidth, LabelOnlyBlockHeight);
                EditorGUI.LabelField(endLabelRect, type.ToString(), EditorStyles.boldLabel);
                return;
            }

            Rect blockRect = GetDockedRect(center, side, FieldBlockWidth, FieldBlockHeight);

            Rect labelRect = new Rect(blockRect.x, blockRect.y, blockRect.width, 16);
            EditorGUI.LabelField(labelRect, type.ToString(), EditorStyles.boldLabel);

            int ruleIndex = FindOrCreateBranchRuleIndex(branchRulesProp, type);
            SerializedProperty rule = branchRulesProp.GetArrayElementAtIndex(ruleIndex);
            SerializedProperty min = rule.FindPropertyRelative("branchCountMin");
            SerializedProperty max = rule.FindPropertyRelative("branchCountMax");

            SerializedProperty deadEndTag = rule.FindPropertyRelative("deadEndTag");
            Rect tagRect = new Rect(blockRect.x, labelRect.yMax + 2, blockRect.width, 18);
            EditorGUIUtility.labelWidth = 60;
            DrawDeadEndTagPopup(tagRect, deadEndTag);
            EditorGUIUtility.labelWidth = 0;

            // "Count", not "depth" — how deep a branch grows is a single config-wide setting
            // (Max Branch Depth, in Generation Settings below), not something set per node here.
            const string branchTooltip = "How many dead-end branches (using the Dead-end tag " +
                "above) grow off of EACH critical-path room of this type — not a property of a " +
                "single room, this fires every time this room type lands on the main path. " +
                "0 = never branch here. This is a COUNT, not a length: how deep each branch " +
                "grows is set once for the whole config by Max Branch Depth, in Generation " +
                "Settings below.";

            Rect captionRect = new Rect(blockRect.x, tagRect.yMax + 2, blockRect.width, 14);
            EditorGUI.LabelField(captionRect, new GUIContent("Branch Count", branchTooltip), EditorStyles.miniLabel);

            Rect fieldsRect = new Rect(blockRect.x, captionRect.yMax + 1, blockRect.width, 18);
            Rect minRect = new Rect(fieldsRect.x, fieldsRect.y, fieldsRect.width / 2f - 2, fieldsRect.height);
            Rect maxRect = new Rect(fieldsRect.x + fieldsRect.width / 2f + 2, fieldsRect.y, fieldsRect.width / 2f - 2, fieldsRect.height);

            EditorGUIUtility.labelWidth = 28;
            DrawBranchCountPopup(minRect, "Min", branchTooltip, min, max, isMin: true);
            DrawBranchCountPopup(maxRect, "Max", branchTooltip, min, max, isMin: false);
            EditorGUIUtility.labelWidth = 0;
        }

        private static void DrawBranchCountPopup(Rect rect, string labelText, string tooltip,
            SerializedProperty min, SerializedProperty max, bool isMin)
        {
            SerializedProperty edited = isMin ? min : max;
            int clampedCurrent = Mathf.Clamp(edited.intValue, 0, BranchCountOptions.Length - 1);

            var displayNames = new GUIContent[BranchCountOptions.Length];
            for (int i = 0; i < BranchCountOptions.Length; i++)
            {
                displayNames[i] = new GUIContent(BranchCountOptions[i]);
            }

            var label = new GUIContent(labelText, tooltip);
            int newValue = EditorGUI.Popup(rect, label, clampedCurrent, displayNames);

            if (newValue == clampedCurrent)
            {
                return;
            }

            edited.intValue = newValue;

            // Popup can only ever produce a value 0-5, so Min > Max is no longer something a
            // user can type — but a single edit can still put the pair out of order (e.g.
            // dragging Min above the existing Max). Nudge the other bound along instead of
            // letting the branch rule become inverted, mirroring what GraphBuilder used to do
            // silently via Math.Min/Math.Max at runtime.
            if (isMin && newValue > max.intValue)
            {
                max.intValue = newValue;
            }
            else if (!isMin && newValue < min.intValue)
            {
                min.intValue = newValue;
            }
        }

        private void DrawDeadEndTagPopup(Rect rect, SerializedProperty deadEndTag)
        {
            List<string> options = GetAvailableSpecialTags(_config);
            string currentTag = deadEndTag.stringValue;

            bool currentIsUnmatched = !string.IsNullOrEmpty(currentTag) && !options.Contains(currentTag);
            if (currentIsUnmatched)
            {
                options.Add(currentTag);
            }

            var displayNames = new GUIContent[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                string tag = options[i];
                if (string.IsNullOrEmpty(tag))
                {
                    displayNames[i] = new GUIContent("Any Special Room");
                }
                else if (currentIsUnmatched && tag == currentTag)
                {
                    displayNames[i] = new GUIContent(tag + " (no matching prefab)");
                }
                else
                {
                    displayNames[i] = new GUIContent(tag);
                }
            }

            var label = new GUIContent("Dead-end",
                "Which Special Room prefab the dead-end room of branches grown from this room type " +
                "should use. Options are pulled from Special Tags already assigned to Special Room " +
                "prefabs in the Rooms list above — set a tag there first if it's missing here. " +
                "\"Any Special Room\" allows any Special Room prefab.");

            int currentIndex = options.IndexOf(currentTag);
            int newIndex = EditorGUI.Popup(rect, label, currentIndex, displayNames);
            if (newIndex != currentIndex)
            {
                deadEndTag.stringValue = options[newIndex];
            }
        }

        private static List<string> GetAvailableSpecialTags(DungeonConfigSO config)
        {
            var options = new List<string> { string.Empty };
            foreach (DungeonConfigSO.RoomEntry entry in config.rooms)
            {
                if (entry.type != RoomType.SpecialRoom || entry.prefab == null || string.IsNullOrEmpty(entry.specialTag))
                {
                    continue;
                }

                if (!options.Contains(entry.specialTag))
                {
                    options.Add(entry.specialTag);
                }
            }

            return options;
        }

        private void HandleNodeClicks(Rect canvas, SerializedProperty connectionsProp)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0)
            {
                return;
            }

            foreach (RoomType type in NodeTypes)
            {
                if (Vector2.Distance(GetNodeCenter(canvas, type), e.mousePosition) > NodeHitRadius)
                {
                    continue;
                }

                if (_selectedNode.HasValue)
                {
                    ToggleConnection(connectionsProp, _selectedNode.Value, type);
                    _selectedNode = null;
                }
                else
                {
                    _selectedNode = type;
                }

                e.Use();
                Repaint();
                return;
            }
        }

        private static void ToggleConnection(SerializedProperty connectionsProp, RoomType from, RoomType to)
        {
            for (int i = 0; i < connectionsProp.arraySize; i++)
            {
                SerializedProperty element = connectionsProp.GetArrayElementAtIndex(i);
                var existingFrom = (RoomType)element.FindPropertyRelative("from").intValue;
                var existingTo = (RoomType)element.FindPropertyRelative("to").intValue;

                if (existingFrom == from && existingTo == to)
                {
                    connectionsProp.DeleteArrayElementAtIndex(i);
                    return;
                }
            }

            int newIndex = connectionsProp.arraySize;
            connectionsProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newElement = connectionsProp.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("from").intValue = (int)from;
            newElement.FindPropertyRelative("to").intValue = (int)to;
        }

        private static int FindOrCreateBranchRuleIndex(SerializedProperty branchRulesProp, RoomType type)
        {
            for (int i = 0; i < branchRulesProp.arraySize; i++)
            {
                SerializedProperty element = branchRulesProp.GetArrayElementAtIndex(i);
                if ((RoomType)element.FindPropertyRelative("type").intValue == type)
                {
                    return i;
                }
            }

            int newIndex = branchRulesProp.arraySize;
            branchRulesProp.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newElement = branchRulesProp.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("type").intValue = (int)type;
            newElement.FindPropertyRelative("branchCountMin").intValue = 0;
            newElement.FindPropertyRelative("branchCountMax").intValue = 0;
            return newIndex;
        }

        private static void DrawConnectionLines(Rect canvas, SerializedProperty connectionsProp)
        {
            for (int i = 0; i < connectionsProp.arraySize; i++)
            {
                SerializedProperty element = connectionsProp.GetArrayElementAtIndex(i);
                var from = (RoomType)element.FindPropertyRelative("from").intValue;
                var to = (RoomType)element.FindPropertyRelative("to").intValue;

                Vector2 fromCenter = GetNodeCenter(canvas, from);

                if (from == to)
                {
                    DrawSelfLoop(fromCenter);
                    continue;
                }

                Vector2 toCenter = GetNodeCenter(canvas, to);

                // Nodes are just dots now (fields live off to the side, out of the way), so a
                // straight line between dot centers never crosses another node's fields — only
                // needs pulling back off each dot's own edge, not full-footprint clipping.
                Vector2 direction = (toCenter - fromCenter).normalized;
                Vector2 start = fromCenter + direction * NodeDotRadius;
                Vector2 end = toCenter - direction * NodeDotRadius;

                DrawArrow(start, end);
            }
        }

        private static void DrawArrow(Vector2 from, Vector2 to)
        {
            Handles.BeginGUI();
            Handles.color = Color.cyan;
            Handles.DrawLine(from, to);

            float distance = Vector2.Distance(from, to);
            Vector2 direction = (to - from).normalized;
            float pullback = Mathf.Min(30f, distance * 0.4f);
            Vector2 arrowTip = to - direction * pullback;
            var perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 arrowLeft = arrowTip - direction * 10f + perpendicular * 6f;
            Vector2 arrowRight = arrowTip - direction * 10f - perpendicular * 6f;

            Handles.DrawLine(arrowTip, arrowLeft);
            Handles.DrawLine(arrowTip, arrowRight);
            Handles.EndGUI();
        }

        // "Recycle"-style loop icon: two arcs, 180° apart, each ending in a small arrowhead —
        // reads as "feeds back into itself" instead of the old plain circle, which looked like
        // decoration rather than a self-connection.
        private static void DrawSelfLoop(Vector2 center)
        {
            Handles.BeginGUI();
            Handles.color = Color.cyan;

            const float radius = NodeDotRadius + 7f;
            DrawLoopArc(center, radius, 15f, 150f);
            DrawLoopArc(center, radius, 195f, 150f);

            Handles.EndGUI();
        }

        private static void DrawLoopArc(Vector2 center, float radius, float startAngleDeg, float sweepDeg)
        {
            Vector3 startDir = AngleToDir(startAngleDeg);
            Handles.DrawWireArc(center, Vector3.forward, startDir, sweepDeg, radius);

            Vector3 endDir = AngleToDir(startAngleDeg + sweepDeg);
            Vector2 tip = center + (Vector2)(endDir * radius);

            // Tangent at the arc's end point, in the direction of the sweep — continues the
            // curve instead of pointing back along it.
            var tangent = new Vector2(-endDir.y, endDir.x);
            var perpendicular = new Vector2(-tangent.y, tangent.x);

            Vector2 arrowLeft = tip - tangent * 6f + perpendicular * 4f;
            Vector2 arrowRight = tip - tangent * 6f - perpendicular * 4f;
            Handles.DrawLine(tip, arrowLeft);
            Handles.DrawLine(tip, arrowRight);
        }

        private static Vector3 AngleToDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
        }

        private static Vector2 GetNodeCenter(Rect canvas, RoomType type)
        {
            switch (type)
            {
                case RoomType.StartRoom:
                    return new Vector2(canvas.x + 150f, canvas.y + 46f);
                case RoomType.EndRoom:
                    return new Vector2(canvas.x + 250f, canvas.y + 46f);
                case RoomType.NormalRoom:
                    return new Vector2(canvas.x + 150f, canvas.y + 140f);
                case RoomType.SpecialRoom:
                    return new Vector2(canvas.x + 250f, canvas.y + 140f);
                default:
                    return canvas.center;
            }
        }

        private static DockSide GetDockSide(RoomType type)
        {
            switch (type)
            {
                case RoomType.StartRoom:
                case RoomType.NormalRoom:
                    // Same side, same dot x as their top-row counterpart below/above them, so
                    // the two blocks land in the same column instead of needing separate tuning.
                    return DockSide.Left;
                case RoomType.EndRoom:
                case RoomType.SpecialRoom:
                default:
                    return DockSide.Right;
            }
        }

        private static Rect GetDockedRect(Vector2 center, DockSide side, float width, float height)
        {
            return side == DockSide.Left
                ? new Rect(center.x - NodeBlockGap - width, center.y - height / 2f, width, height)
                : new Rect(center.x + NodeBlockGap, center.y - height / 2f, width, height);
        }

        private void DrawValidationMessages()
        {
            List<(MessageType Severity, string Message)> issues = CollectConfigIssues(_config);
            if (issues.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("Configuration Issues", EditorStyles.boldLabel);
            foreach ((MessageType severity, string message) in issues)
            {
                EditorGUILayout.HelpBox(message, severity);
            }
        }

        private static List<(MessageType, string)> CollectConfigIssues(DungeonConfigSO config)
        {
            var issues = new List<(MessageType, string)>();

            if (config.minRoomCount > config.maxRoomCount)
            {
                issues.Add((MessageType.Error, "Min Room Count is greater than Max Room Count — generation will fail."));
            }

            if (!HasPrefabFor(config, RoomType.StartRoom))
            {
                issues.Add((MessageType.Error, "No Start Room prefab assigned in Rooms — the starting room will not be placed."));
            }

            if (!HasPrefabFor(config, RoomType.EndRoom))
            {
                issues.Add((MessageType.Error, "No End Room prefab assigned in Rooms — the final room will not be placed."));
            }

            if (!HasPrefabFor(config, RoomType.NormalRoom))
            {
                issues.Add((MessageType.Warning, "No Normal Room prefab assigned — most of the dungeon path will have no room placed."));
            }

            if (HasAnyBranching(config) && !HasPrefabFor(config, RoomType.SpecialRoom))
            {
                issues.Add((MessageType.Warning, "Branching is enabled (Branch Count > 0) but no Special Room prefab is assigned — dead-end rooms will be empty."));
            }

            if (config.corridorPrefab == null)
            {
                issues.Add((MessageType.Warning, "No Corridor Prefab assigned — rooms will be placed without connecting corridors."));
            }

            if (config.doorBlockerPrefab == null)
            {
                issues.Add((MessageType.Warning, "No Door Blocker Prefab assigned — unused doorways won't be sealed off, rooms will keep openings on all sides."));
            }

            if (config.gridCellSize.x <= 0 || config.gridCellSize.y <= 0)
            {
                issues.Add((MessageType.Error, "Grid Cell Size must be positive on both axes — rooms would overlap at the same position."));
            }

            if (config.corridorTileSize.x <= 0 || config.corridorTileSize.y <= 0)
            {
                issues.Add((MessageType.Error, "Corridor Tile Size must be positive on both axes — corridor generation would hang or fail."));
            }

            if (config.fillBackground)
            {
                if (config.backgroundTile == null)
                {
                    issues.Add((MessageType.Error, "Fill Background is enabled but no Background Tile is assigned — background fill will do nothing."));
                }

                if (config.backgroundTileSize.x <= 0 || config.backgroundTileSize.y <= 0)
                {
                    issues.Add((MessageType.Error, "Background Tile Size must be positive on both axes — background fill would hang or fail."));
                }
            }

            string missingSpawnPoints = FindPrefabsWithoutSpawnPoints(config);
            if (missingSpawnPoints != null)
            {
                issues.Add((MessageType.Warning, $"No SpawnPoint markers found on: {missingSpawnPoints} — OnRoomPlaced will fire for these rooms with nowhere to spawn content."));
            }

            foreach (string unmatchedTag in FindUnmatchedDeadEndTags(config))
            {
                issues.Add((MessageType.Warning, $"Branch dead-end tag '{unmatchedTag}' has no matching Special Room prefab (specialTag) — those branches will fall back to a random Special Room prefab at runtime."));
            }

            foreach (string deadConnection in FindDeadConnections(config))
            {
                issues.Add((MessageType.Warning, deadConnection));
            }

            return issues;
        }

        private static IEnumerable<string> FindUnmatchedDeadEndTags(DungeonConfigSO config)
        {
            var seen = new HashSet<string>();
            foreach (DungeonConfigSO.BranchRule rule in config.branchRules)
            {
                if (string.IsNullOrEmpty(rule.deadEndTag) || !seen.Add(rule.deadEndTag))
                {
                    continue;
                }

                bool hasMatch = config.rooms.Exists(entry =>
                    entry.type == RoomType.SpecialRoom && entry.prefab != null && entry.specialTag == rule.deadEndTag);

                if (!hasMatch)
                {
                    yield return rule.deadEndTag;
                }
            }
        }

        // GraphBuilder.PickNextType only ever consults a connection when `from` is the type
        // just placed on the critical path (never EndRoom — it's appended once, after the path
        // loop, and is never assigned to `current`) and `to` isn't Start/EndRoom (both are
        // hard-filtered out, since neither can legitimately be a "next" room mid-path). A rule
        // targeting SpecialRoom is also skipped whenever keepSpecialRoomsOffCriticalPath is on
        // (its default). Any connection matching one of these is wired up in the graph UI but
        // never actually consulted — flag it instead of leaving it to be found by experiment.
        private static IEnumerable<string> FindDeadConnections(DungeonConfigSO config)
        {
            foreach (DungeonConfigSO.ConnectionRule rule in config.connections)
            {
                if (rule.to == RoomType.StartRoom)
                {
                    yield return $"Connection {rule.from} → {rule.to} has no effect: nothing " +
                        "ever transitions back into StartRoom mid-path.";
                }
                else if (rule.to == RoomType.EndRoom)
                {
                    yield return $"Connection {rule.from} → {rule.to} has no effect: EndRoom " +
                        "is only ever placed once, after the critical path is already built.";
                }
                else if (rule.from == RoomType.EndRoom)
                {
                    yield return $"Connection {rule.from} → {rule.to} has no effect: EndRoom " +
                        "is never a source room, it's always the last room placed.";
                }
                else if (rule.to == RoomType.SpecialRoom && config.keepSpecialRoomsOffCriticalPath)
                {
                    yield return $"Connection {rule.from} → {rule.to} is ignored while " +
                        "\"Special Off Crit. Path\" is enabled in Generation Settings — Special " +
                        "Room can then only appear as a branch dead-end.";
                }
            }
        }

        private static string FindPrefabsWithoutSpawnPoints(DungeonConfigSO config)
        {
            var names = new List<string>();
            foreach (DungeonConfigSO.RoomEntry entry in config.rooms)
            {
                if (entry.prefab == null)
                {
                    continue;
                }

                if (entry.type != RoomType.NormalRoom && entry.type != RoomType.SpecialRoom)
                {
                    continue;
                }

                if (entry.prefab.GetComponentsInChildren<SpawnPoint>(true).Length == 0)
                {
                    names.Add(entry.prefab.name);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        private static bool HasPrefabFor(DungeonConfigSO config, RoomType type)
        {
            foreach (DungeonConfigSO.RoomEntry entry in config.rooms)
            {
                if (entry.type == type && entry.prefab != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyBranching(DungeonConfigSO config)
        {
            foreach (DungeonConfigSO.BranchRule rule in config.branchRules)
            {
                if (Mathf.Max(rule.branchCountMin, rule.branchCountMax) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Preview seed: {_previewSeed}", EditorStyles.miniLabel);
                if (GUILayout.Button("Reroll", GUILayout.Width(70)))
                {
                    _previewSeed = Guid.NewGuid().GetHashCode();
                    _previewDirty = true;
                }
            }

            if (_previewDirty)
            {
                _cachedPreviewRooms = GeneratePreviewRooms(out _cachedPreviewValidated);
                _previewDirty = false;
            }

            List<RoomInstance> previewRooms = _cachedPreviewRooms;
            if (previewRooms != null && previewRooms.Count > 0 && !_cachedPreviewValidated)
            {
                EditorGUILayout.HelpBox("This layout didn't pass validation even after retrying " +
                    "(room/corridor overlap or a disconnected room) — shown anyway so you can see " +
                    "it, but DungeonGenerator would keep retrying at runtime instead of using it " +
                    "as-is. Frequent misses usually mean Branch Count is set too high for how much " +
                    "room Grid Cell Size gives each room to spread out in.", MessageType.Warning);
            }

            Rect canvas = GUILayoutUtility.GetRect(400, 260);
            GUI.Box(canvas, GUIContent.none);

            if (previewRooms == null || previewRooms.Count == 0)
            {
                EditorGUI.LabelField(canvas, "Preview unavailable — check Min/Max Room Count and connection rules below.");
                return;
            }

            DrawPreviewRooms(canvas, previewRooms);
        }

        private List<RoomInstance> GeneratePreviewRooms(out bool validated)
        {
            validated = false;
            try
            {
                var random = new System.Random(_previewSeed);
                var placer = new RoomPlacer(_config, random);
                var validator = new Validator();
                ILayoutSolver layoutSolver = _config.layoutSolverType == LayoutSolverType.Compact
                    ? new CompactLayoutSolver()
                    : (ILayoutSolver)new GridLayoutSolver();

                List<RoomInstance> rooms = null;

                // Mirrors DungeonGenerator.Generate()'s retry loop (same default attempt count)
                // — without it, the preview shows raw, unvalidated single-shot layouts that a
                // real playthrough would never actually see, since Generate() keeps retrying
                // past anything Validator rejects.
                const int maxAttempts = 10;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    rooms = new GraphBuilder(_config, random).Build();
                    placer.AssignPrefabs(rooms);
                    layoutSolver.Solve(rooms, _config, random);

                    if (validator.Validate(rooms, _config))
                    {
                        validated = true;
                        break;
                    }
                }

                return rooms;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void DrawPreviewRooms(Rect canvas, List<RoomInstance> rooms)
        {
            Vector2 min = rooms[0].Position;
            Vector2 max = rooms[0].Position;
            foreach (RoomInstance room in rooms)
            {
                min = Vector2.Min(min, room.Position);
                max = Vector2.Max(max, room.Position);
            }

            Vector2 size = max - min;
            const float padding = 20f;
            float scaleX = size.x > 0.01f ? (canvas.width - padding * 2f) / size.x : 40f;
            float scaleY = size.y > 0.01f ? (canvas.height - padding * 2f) / size.y : 40f;
            float scale = Mathf.Min(scaleX, scaleY, 40f);

            Vector2 ToCanvas(Vector2 worldPos)
            {
                float x = canvas.x + padding + (worldPos.x - min.x) * scale;
                float y = canvas.yMax - padding - (worldPos.y - min.y) * scale;
                return new Vector2(x, y);
            }

            Handles.BeginGUI();
            Handles.color = Color.gray;
            var drawnEdges = new HashSet<(int, int)>();
            for (int i = 0; i < rooms.Count; i++)
            {
                foreach (RoomInstance neighbor in rooms[i].Neighbors)
                {
                    int j = rooms.IndexOf(neighbor);
                    (int, int) key = i < j ? (i, j) : (j, i);
                    if (!drawnEdges.Add(key))
                    {
                        continue;
                    }

                    // Actual corridors are always axis-aligned (horizontal segment then
                    // vertical, see CorridorGenerator.BuildPath) — never diagonal, even when
                    // the two rooms aren't grid-adjacent (e.g. a graph edge placed via
                    // GridLayoutSolver's FindNearestFreeCell fallback). Draw the same elbow
                    // here instead of a straight line, or non-adjacent connections show up as
                    // a misleading diagonal that can't actually occur at runtime.
                    Vector2 fromWorld = rooms[i].Position;
                    Vector2 toWorld = neighbor.Position;
                    Vector2 cornerWorld = new Vector2(toWorld.x, fromWorld.y);

                    Handles.DrawLine(ToCanvas(fromWorld), ToCanvas(cornerWorld));
                    Handles.DrawLine(ToCanvas(cornerWorld), ToCanvas(toWorld));
                }
            }

            Handles.EndGUI();

            // GUI.Box multiplies GUI.color onto the skin's (dark, shaded) box texture, so a
            // tint can never read as fully saturated — EditorGUI.DrawRect paints a flat color
            // with no skin texture involved, which is what "not dull" actually needs.
            foreach (RoomInstance room in rooms)
            {
                Vector2 center = ToCanvas(room.Position);

                // Box scaled to the room's actual size — with CompactLayoutSolver this is the
                // whole point of the preview (mixed room sizes get mixed spacing); a fixed
                // square would hide it. Falls back to a small fixed dot if no prefab is
                // assigned yet (Size is still Vector2.zero).
                const float sizeMultiplier = 1f / 1.5f;
                Vector2 boxSize = room.Size == Vector2.zero
                    ? new Vector2(10f, 10f) * sizeMultiplier
                    : new Vector2(Mathf.Clamp(room.Size.x * scale, 8f, 44f), Mathf.Clamp(room.Size.y * scale, 8f, 44f)) * sizeMultiplier;

                var rect = new Rect(center.x - boxSize.x / 2f, center.y - boxSize.y / 2f, boxSize.x, boxSize.y);
                EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.35f));
                EditorGUI.DrawRect(Grow(rect, -1.5f), GetRoomColor(room.Type));
            }
        }

        private static Rect Grow(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }

        private static Color GetRoomColor(RoomType type)
        {
            switch (type)
            {
                case RoomType.StartRoom:
                    return Color.green;
                case RoomType.EndRoom:
                    return Color.red;
                case RoomType.SpecialRoom:
                    return Color.yellow;
                default:
                    return new Color(0.4f, 0.62f, 1f);
            }
        }

        private void DrawGenerationSettings()
        {
            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("layoutSolverType"), new GUIContent(
                "Layout Solver Type",
                "Grid: every room sits on a uniform grid sized for the single biggest room " +
                "prefab in the config — guarantees no overlaps by construction, but small rooms " +
                "get spaced as far apart as the biggest one. Compact: spacing between each pair " +
                "of neighboring rooms is based on their own real size instead, so small rooms " +
                "don't get stretched out next to a big boss room — trades away Grid's built-in " +
                "overlap guarantee for a resolution pass plus the existing retry loop as a " +
                "safety net."));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("minRoomCount"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("maxRoomCount"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("maxBranchDepth"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("keepSpecialRoomsOffCriticalPath"),
                new GUIContent("Special Off Crit. Path", "If enabled, Special Rooms can only appear as branch dead-ends, never as a step on the critical path from Start to End."));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("mergeChance"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("gridCellSize"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("corridorTileSize"));
            if (GUILayout.Button("Auto-size from Prefabs"))
            {
                AutoSizeFromPrefabs();
            }
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("corridorPrefab"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("doorBlockerPrefab"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Background Fill", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("fillBackground"));
            if (_config.fillBackground)
            {
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty("backgroundTile"));
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty("backgroundTileSize"));
                if (GUILayout.Button("Auto-size from Background Tile"))
                {
                    AutoSizeBackgroundTile();
                }
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty("backgroundPadding"));
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty("backgroundSortingOrder"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("useRandomSeed"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty("seed"));
        }

        private void AutoSizeFromPrefabs()
        {
            Vector2 maxRoomSize = Vector2.zero;
            foreach (DungeonConfigSO.RoomEntry entry in _config.rooms)
            {
                RoomDefinition definition = entry.prefab != null ? entry.prefab.GetComponent<RoomDefinition>() : null;
                if (definition != null)
                {
                    maxRoomSize = Vector2.Max(maxRoomSize, definition.size);
                }
            }

            if (maxRoomSize == Vector2.zero)
            {
                EditorUtility.DisplayDialog("RoomForge", "No room prefab with a RoomDefinition is assigned — nothing to size from.", "OK");
                return;
            }

            SpriteRenderer corridorRenderer = _config.corridorPrefab != null
                ? _config.corridorPrefab.GetComponentInChildren<SpriteRenderer>()
                : null;

            Vector2 corridorSize = Vector2.zero;
            if (corridorRenderer != null && corridorRenderer.sprite != null)
            {
                corridorSize = Vector2.Scale(corridorRenderer.sprite.bounds.size, corridorRenderer.transform.lossyScale);
                SerializedProperty corridorProp = _serializedConfig.FindProperty("corridorTileSize");
                corridorProp.vector2Value = corridorSize;
            }

            // CorridorGenerator always steps by tileSize.x (a rotated tile's length along
            // its direction of travel), on both horizontal and vertical runs. So the gap
            // reserved between rooms must be exactly corridorSize.x on both axes — not
            // corridorSize.y, and not rounded, or a tile can come up short of the gap or
            // fail to fit at all.
            Vector2 cellSize = maxRoomSize + Vector2.one * corridorSize.x;
            SerializedProperty gridProp = _serializedConfig.FindProperty("gridCellSize");
            gridProp.vector2Value = cellSize;

            if (corridorSize == Vector2.zero)
            {
                EditorUtility.DisplayDialog("RoomForge", "Grid Cell Size was set from room prefabs only — assign a Corridor Prefab first and re-run this to leave room for corridors between rooms.", "OK");
            }
        }

        private void AutoSizeBackgroundTile()
        {
            // Only a plain Tile exposes a single Sprite to read pixel size / PPU from.
            // RuleTile/RandomTile (2D Tilemap Extras) and other custom TileBase types don't
            // have one canonical sprite, so they're left for the developer to size by hand.
            Sprite sprite = (_config.backgroundTile as Tile)?.sprite;
            if (sprite == null)
            {
                EditorUtility.DisplayDialog("RoomForge",
                    "Background Tile isn't assigned, or isn't a plain Tile asset with a Sprite " +
                    "(e.g. a RuleTile/RandomTile) — set Background Tile Size manually for those.", "OK");
                return;
            }

            SerializedProperty sizeProp = _serializedConfig.FindProperty("backgroundTileSize");
            sizeProp.vector2Value = sprite.rect.size / sprite.pixelsPerUnit;
        }
    }
}
