using System.Collections.Generic;
using System.Reflection;
using Mush.Testing;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(TrackDogLedSledFollowTest))]
[CanEditMultipleObjects]
public sealed class TrackDogLedSledFollowTestEditor : Editor
{
    private readonly Dictionary<string, GUIContent> labels = new();
    private readonly GUIContent scriptLabel = new("스크립트");
    private readonly GUIContent automaticDogsLabel = new("앞줄 강아지 자동 선택",
        "목록을 비워 두면 가장 앞에 있는 활성 강아지들을 자동으로 선택합니다.");
    private SerializedProperty leadDogs;
    private ReorderableList dogList;

    private void OnEnable()
    {
        labels.Clear();
        foreach (FieldInfo field in typeof(TrackDogLedSledFollowTest).GetFields(
                     BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            InspectorNameAttribute name = field.GetCustomAttribute<InspectorNameAttribute>();
            if (name == null) continue;
            TooltipAttribute tooltip = field.GetCustomAttribute<TooltipAttribute>();
            labels[field.Name] = new GUIContent(name.displayName, tooltip?.tooltip);
        }

        leadDogs = serializedObject.FindProperty("leadDogs");
        dogList = new ReorderableList(serializedObject, leadDogs, true, true, true, true)
        {
            elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, labels["leadDogs"]),
            drawNoneElementCallback = rect => EditorGUI.LabelField(rect, automaticDogsLabel),
            drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 2f;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, leadDogs.GetArrayElementAtIndex(index),
                    new GUIContent($"강아지 {index + 1}", "썰매를 앞에서 이끄는 강아지를 연결합니다."));
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(property, scriptLabel);
            }
            else if (property.name == "leadDogs")
            {
                dogList.DoLayoutList();
            }
            else if (labels.TryGetValue(property.name, out GUIContent label))
            {
                EditorGUILayout.PropertyField(property, label, true);
            }
            else
            {
                EditorGUILayout.PropertyField(property, true);
            }
        }
        serializedObject.ApplyModifiedProperties();
    }
}
