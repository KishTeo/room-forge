using UnityEditor;
using UnityEngine;

namespace RoomForge
{
    [CustomPropertyDrawer(typeof(DungeonConfigSO.RoomEntry))]
    public class RoomEntryDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty type = property.FindPropertyRelative("type");
            SerializedProperty prefab = property.FindPropertyRelative("prefab");
            SerializedProperty weight = property.FindPropertyRelative("weight");
            SerializedProperty specialTag = property.FindPropertyRelative("specialTag");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var typeRect = new Rect(position.x, position.y, position.width, lineHeight);
            var prefabRect = new Rect(position.x, typeRect.yMax + spacing, position.width, lineHeight);
            var weightRect = new Rect(position.x, prefabRect.yMax + spacing, position.width, lineHeight);

            EditorGUI.PropertyField(typeRect, type);
            EditorGUI.PropertyField(prefabRect, prefab);
            EditorGUI.PropertyField(weightRect, weight, new GUIContent("Weight",
                "Relative chance this prefab gets picked among OTHER prefabs of the same Type " +
                "(and same Special Tag, if set) — not a global percentage. E.g. two Normal Room " +
                "prefabs with Weight 1 and 3 get picked in a 1:3 ratio; a solo prefab with any " +
                "Weight is always picked whenever that Type/Tag is needed."));

            // Special Tag only means anything for SpecialRoom entries (RoomPlacer.PickPrefab
            // filters by it, GraphBuilder's dead-end dropdown only lists tags from SpecialRoom
            // prefabs) — showing it on Start/End/Normal rows invites filling in a value that is
            // silently ignored, which is exactly what happened before this drawer existed.
            if ((RoomType)type.intValue == RoomType.SpecialRoom)
            {
                var tagRect = new Rect(position.x, weightRect.yMax + spacing, position.width, lineHeight);
                EditorGUI.PropertyField(tagRect, specialTag, new GUIContent("Special Tag",
                    "Optional subtype, e.g. Shop, Treasure, SecretRoom. Selectable from a Branch " +
                    "Rule's Dead-end dropdown once set here."));
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty type = property.FindPropertyRelative("type");
            int lineCount = (RoomType)type.intValue == RoomType.SpecialRoom ? 4 : 3;

            return lineCount * lineHeight + (lineCount - 1) * spacing;
        }
    }
}
