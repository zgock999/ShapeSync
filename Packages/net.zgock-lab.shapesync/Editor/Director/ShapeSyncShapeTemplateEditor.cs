// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Custom Inspector for the concrete Shape authoring Template assets.</summary>
    /// <remarks>Edits only authoring assets; it neither validates relationships nor changes a ShapeDirector Runtime List.</remarks>
    [CustomEditor(typeof(ShapeSyncShapeTemplate), true)]
    internal sealed class ShapeSyncShapeTemplateEditor : UnityEditor.Editor
    {
        private SerializedProperty shapeId;
        private SerializedProperty priority;
        private SerializedProperty tags;
        private SerializedProperty morphs;
        private SerializedProperty parts;

        private void OnEnable()
        {
            shapeId = serializedObject.FindProperty("shapeId");
            priority = serializedObject.FindProperty("priority");
            tags = serializedObject.FindProperty("tags");
            morphs = serializedObject.FindProperty("morphs");
            parts = serializedObject.FindProperty("parts");
        }

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Shape Template (Authoring)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This asset is authoring input only. Changes reach a Director Runtime List only through its explicit Sync operation.", MessageType.Info);
            EditorGUILayout.PropertyField(shapeId, new GUIContent("Shape Id"));
            EditorGUILayout.PropertyField(priority, new GUIContent("Priority"));
            EditorGUILayout.PropertyField(tags, new GUIContent("Tags"), true);

            if (morphs != null) DrawMorphs();
            if (parts != null) DrawParts();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMorphs()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Morphs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Target is the MeshBinding logical name used by FBM_SET, not the physical BlendShape targetName.", MessageType.Info);
            for (int i = 0; i < morphs.arraySize; i++)
            {
                SerializedProperty entry = morphs.GetArrayElementAtIndex(i);
                SerializedProperty target = entry.FindPropertyRelative("target");
                SerializedProperty value = entry.FindPropertyRelative("value");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(target, GUIContent.none, GUILayout.MinWidth(120f));
                    float next = DynamicBoneBlendWeightField.DrawLayout(value.floatValue);
                    if (next != value.floatValue) value.floatValue = next;
                    if (GUILayout.Button("Remove", GUILayout.Width(62f))) { morphs.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("Add Morph")) morphs.arraySize++;
        }

        private void DrawParts()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);
            for (int i = 0; i < parts.arraySize; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        SerializedProperty entry = parts.GetArrayElementAtIndex(i);
                        EditorGUILayout.LabelField(entry.managedReferenceValue == null ? "Missing Entry" : entry.managedReferenceValue.GetType().Name, EditorStyles.miniBoldLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(62f))) { parts.DeleteArrayElementAtIndex(i); break; }
                    }
                    if (i < parts.arraySize) EditorGUILayout.PropertyField(parts.GetArrayElementAtIndex(i), GUIContent.none, true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Mesh")) AddPart(new MeshEntry());
                if (GUILayout.Button("Add Texture")) AddPart(new TextureEntry());
                if (GUILayout.Button("Add Color")) AddPart(new ColorEntry());
                if (GUILayout.Button("Add UVSet")) AddPart(new UvsetEntry());
            }
        }

        private void AddPart(ShapeEntry entry)
        {
            int index = parts.arraySize;
            parts.arraySize++;
            parts.GetArrayElementAtIndex(index).managedReferenceValue = entry;
        }
    }
}
