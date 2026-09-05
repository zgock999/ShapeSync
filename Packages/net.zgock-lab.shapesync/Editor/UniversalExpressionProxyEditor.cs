// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Custom Inspector for configuring a <see cref="UniversalExpressionProxy"/>.</summary>
    [CustomEditor(typeof(UniversalExpressionProxy))]
    public class UniversalExpressionProxyEditor : UnityEditor.Editor
    {
        private const float NameColumnWidth = 260f;
    private const float ToggleColumnWidth = 28f;

    private SerializedProperty targetSkinnedMeshRendererProperty;
    private SerializedProperty dynamicBoneBlenderProperty;
    private SerializedProperty expressionsProperty;

    private void OnEnable()
    {
        targetSkinnedMeshRendererProperty = serializedObject.FindProperty("targetSkinnedMeshRenderer");
        dynamicBoneBlenderProperty = serializedObject.FindProperty("dynamicBoneBlender");
        expressionsProperty = serializedObject.FindProperty("expressions");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(targetSkinnedMeshRendererProperty);
        EditorGUILayout.PropertyField(dynamicBoneBlenderProperty);

        EditorGUILayout.Space();
        DrawToolbar();
        DrawExpressionRows();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Expressions", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Auto Rebuild Expression List", GUILayout.Width(210f)))
        {
            serializedObject.ApplyModifiedProperties();
            UniversalExpressionProxy proxy = (UniversalExpressionProxy)target;
            Undo.RecordObject(proxy, "Rebuild Expression List");
            proxy.RebuildExpressionList();
            EditorUtility.SetDirty(proxy);
            serializedObject.Update();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawExpressionRows()
    {
        if (expressionsProperty == null || !expressionsProperty.isArray || expressionsProperty.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No expression entries. Assign a target renderer and rebuild the expression list.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("BlendShape", EditorStyles.miniBoldLabel, GUILayout.Width(NameColumnWidth));
        GUILayout.Label("On", EditorStyles.miniBoldLabel, GUILayout.Width(ToggleColumnWidth));
        GUILayout.Label("Weight", EditorStyles.miniBoldLabel, GUILayout.MinWidth(160f));
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < expressionsProperty.arraySize; i++)
        {
            SerializedProperty entry = expressionsProperty.GetArrayElementAtIndex(i);
            SerializedProperty nameProperty = entry.FindPropertyRelative("blendShapeName");
            SerializedProperty enabledProperty = entry.FindPropertyRelative("enabled");
            SerializedProperty weightProperty = entry.FindPropertyRelative("weight");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(nameProperty, GUIContent.none, GUILayout.Width(NameColumnWidth));
            enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(ToggleColumnWidth));

            using (new EditorGUI.DisabledScope(!enabledProperty.boolValue))
            {
                weightProperty.floatValue = EditorGUILayout.Slider(weightProperty.floatValue, 0f, 1f, GUILayout.MinWidth(160f));
            }

            EditorGUILayout.EndHorizontal();
        }
    }
    }
}
