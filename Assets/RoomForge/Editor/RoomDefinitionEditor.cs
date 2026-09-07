using UnityEditor;
using UnityEngine;

namespace RoomForge
{
    [CustomEditor(typeof(RoomDefinition))]
    [CanEditMultipleObjects]
    public class RoomDefinitionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var definition = (RoomDefinition)target;
            Bounds? bounds = MeasureSpriteBounds(definition);

            EditorGUILayout.Space();

            if (!bounds.HasValue)
            {
                EditorGUILayout.HelpBox(
                    "No SpriteRenderer found under this prefab — nothing to measure.",
                    MessageType.Warning);
                return;
            }

            Vector2 measured = bounds.Value.size;
            EditorGUILayout.HelpBox(
                $"Measured from sprites right now: {measured.x:0.###} x {measured.y:0.###}\n" +
                "(combined bounds of every SpriteRenderer under this object — includes any " +
                "decoration that overhangs the walls, so sanity-check the result if the room " +
                "has overhanging art).",
                MessageType.Info);

            if (GUILayout.Button("Auto-size from Sprites"))
            {
                Undo.RecordObject(definition, "Auto-size Room Definition");
                definition.size = measured;
                EditorUtility.SetDirty(definition);
            }
        }

        private static Bounds? MeasureSpriteBounds(RoomDefinition definition)
        {
            SpriteRenderer[] renderers = definition.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length == 0)
            {
                return null;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
