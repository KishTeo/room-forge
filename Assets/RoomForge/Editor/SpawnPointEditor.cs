using UnityEditor;
using UnityEngine;

namespace RoomForge
{
    [CustomEditor(typeof(SpawnPoint))]
    public class SpawnPointEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Marks a spot in this room prefab where YOUR game can spawn something — an " +
                "enemy, loot, a decoration. RoomForge doesn't spawn anything here itself; it " +
                "only collects every Spawn Point under a room into RoomDefinition.SpawnPoints " +
                "at runtime. In your own code:\n\n" +
                "dungeonGenerator.OnRoomPlaced += room => {\n" +
                "    foreach (var point in room.Definition.SpawnPoints)\n" +
                "        Instantiate(enemyPrefab, point.position, point.rotation);\n" +
                "};\n\n" +
                "Use room.Type / room.SpecialTag inside that callback to decide what belongs " +
                "in this particular room. The orange gizmo marks its position in the Scene view " +
                "— move this GameObject to reposition it.",
                MessageType.Info);
        }
    }
}
