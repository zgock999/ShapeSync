// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Editor.Materials
{
    [CustomEditor(typeof(MaterialProxy))]
    internal sealed class MaterialProxyEditor : UnityEditor.Editor
    {
        private ReorderableList entriesList;
        private readonly Dictionary<string, bool> bindingFoldouts = new Dictionary<string, bool>();

        private void OnEnable()
        {
            entriesList = new ReorderableList(serializedObject, serializedObject.FindProperty("entries"), true, true, true, true);
            entriesList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Entries");
            entriesList.elementHeightCallback = index =>
            {
                SerializedProperty element = entriesList.serializedProperty.GetArrayElementAtIndex(index);
                if (!element.isExpanded) return EditorGUIUtility.singleLineHeight + 5f;
                return EditorGUIUtility.singleLineHeight + 3f + GetEntryHeight(GetBindingFoldout(element));
            };
            entriesList.drawElementCallback = DrawEntryElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MaterialProxy proxy = (MaterialProxy)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Material Proxy Actions", EditorStyles.boldLabel);
            if (GUILayout.Button("Select Adapter and Populate Entries From Child Renderers"))
            {
                string absolutePath = EditorUtility.OpenFilePanel("Select Material Shader Adapter", Application.dataPath, "asset");
                if (!string.IsNullOrEmpty(absolutePath))
                {
                    string assetPath = FileUtil.GetProjectRelativePath(absolutePath);
                    MaterialShaderAdapter adapter = AssetDatabase.LoadAssetAtPath<MaterialShaderAdapter>(assetPath);
                    if (adapter == null)
                    {
                        Debug.LogWarning("Select a Material Shader Adapter asset.", proxy);
                    }
                    else if (TryBuildChildRendererEntries(proxy, adapter, out List<MaterialProxyEntry> generatedEntries, out int configuredCount))
                    {
                        Undo.RecordObject(proxy, "Populate Material Proxy Entries");
                        WriteEntries(generatedEntries);
                        EditorUtility.SetDirty(proxy);
                        Debug.Log($"ShapeSync Material Proxy created {generatedEntries.Count} entries; {configuredCount} were initialized by {adapter.name}.", proxy);
                    }
                }
            }

            entriesList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            MaterialProxyDiagnostic lastDiagnostic = proxy.LastDiagnostic;
            if (lastDiagnostic.code != MaterialProxyDiagnosticCode.None)
            {
                EditorGUILayout.HelpBox($"{lastDiagnostic.code}: {lastDiagnostic.message}", MessageType.Warning);
            }
        }

        private void DrawEntryElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = entriesList.serializedProperty.GetArrayElementAtIndex(index);
            float y = rect.y + 2f;
            string entryName = element.FindPropertyRelative("entryName").stringValue;
            element.isExpanded = EditorGUI.Foldout(NextRow(ref y, rect), element.isExpanded, string.IsNullOrWhiteSpace(entryName) ? "<unnamed entry>" : entryName, true);
            if (!element.isExpanded) return;

            SerializedProperty values = element.FindPropertyRelative("configuredValues");
            DrawProperty(ref y, rect, "Entry Name", element.FindPropertyRelative("entryName"));

            Rect bindingRect = NextRow(ref y, rect);
            bool bindingExpanded = EditorGUI.Foldout(bindingRect, GetBindingFoldout(element), "Renderer / Channel / Adapter", true);
            bindingFoldouts[element.propertyPath] = bindingExpanded;
            if (bindingExpanded)
            {
                DrawProperty(ref y, rect, "Renderer", element.FindPropertyRelative("renderer"));
                DrawProperty(ref y, rect, "Material Channel", element.FindPropertyRelative("materialChannel"));
                DrawProperty(ref y, rect, "Adapter", element.FindPropertyRelative("adapter"));
            }

            EditorGUI.LabelField(NextRow(ref y, rect), "Configured Values", EditorStyles.boldLabel);
            DrawTextureSemantic(ref y, rect, "Main Texture", values.FindPropertyRelative("baseColorTexture"), values.FindPropertyRelative("applyBaseColorTexture"));
            DrawTextureSemantic(ref y, rect, "Normal Texture", values.FindPropertyRelative("normalTexture"), values.FindPropertyRelative("applyNormalTexture"));
            DrawValueSemantic(ref y, rect, "Color", values.FindPropertyRelative("color"), values.FindPropertyRelative("applyColor"));
            SerializedProperty applyUvTransform = values.FindPropertyRelative("applyUvTransform");
            DrawApplyHeader(ref y, rect, "UV Transform", applyUvTransform);
            DrawUvProperty(ref y, rect, "UV Scale", values.FindPropertyRelative("uvScale"), applyUvTransform);
            DrawUvProperty(ref y, rect, "UV Offset", values.FindPropertyRelative("uvOffset"), applyUvTransform);

            entryName = element.FindPropertyRelative("entryName").stringValue;
            Rect buttonRect = NextRow(ref y, rect);
            float buttonWidth = (buttonRect.width - 4f) * 0.5f;
            MaterialProxy proxy = (MaterialProxy)target;
            if (GUI.Button(new Rect(buttonRect.x, buttonRect.y, buttonWidth, buttonRect.height), "Read Current Material"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(proxy, "Read Material Proxy Values");
                if (!proxy.TryReadCurrentMaterial(entryName, out MaterialProxyDiagnostic diagnostic)) Debug.LogWarning($"ShapeSync Material Proxy read failed: {diagnostic.code}: {diagnostic.message}", proxy);
                else EditorUtility.SetDirty(proxy);
                serializedObject.Update();
            }

            if (GUI.Button(new Rect(buttonRect.x + buttonWidth + 4f, buttonRect.y, buttonWidth, buttonRect.height), "Commit"))
            {
                serializedObject.ApplyModifiedProperties();
                if (!proxy.TryCommitConfigured(entryName, out MaterialProxyDiagnostic diagnostic)) Debug.LogWarning($"ShapeSync Material Proxy commit failed: {diagnostic.code}: {diagnostic.message}", proxy);
                else if (diagnostic.code != MaterialProxyDiagnosticCode.None) Debug.LogWarning($"ShapeSync Material Proxy commit warning: {diagnostic.code}: {diagnostic.message}", proxy);
                serializedObject.Update();
            }
        }

        private static float GetEntryHeight(bool bindingExpanded)
        {
            const float texturePreviewSize = 48f;
            int standardRows = bindingExpanded ? 11 : 8;
            return 2f + standardRows * (EditorGUIUtility.singleLineHeight + 3f) + 2f * (texturePreviewSize + 3f);
        }

        private bool GetBindingFoldout(SerializedProperty element)
        {
            return bindingFoldouts.TryGetValue(element.propertyPath, out bool expanded) && expanded;
        }

        private static Rect NextRow(ref float y, Rect elementRect, float height = 0f)
        {
            if (height <= 0f) height = EditorGUIUtility.singleLineHeight;
            var row = new Rect(elementRect.x, y, elementRect.width, height);
            y += height + 3f;
            return row;
        }

        private static void DrawProperty(ref float y, Rect elementRect, string label, SerializedProperty property)
        {
            EditorGUI.PropertyField(NextRow(ref y, elementRect), property, new GUIContent(label));
        }

        private static void DrawTextureSemantic(ref float y, Rect elementRect, string label, SerializedProperty texture, SerializedProperty apply)
        {
            const float texturePreviewSize = 48f;
            Rect row = NextRow(ref y, elementRect, texturePreviewSize);
            const float labelWidth = 110f;
            const float checkboxWidth = 18f;
            float controlY = row.y + (row.height - EditorGUIUtility.singleLineHeight) * 0.5f;
            EditorGUI.LabelField(new Rect(row.x, controlY, labelWidth, EditorGUIUtility.singleLineHeight), label);
            Rect previewRect = new Rect(row.x + labelWidth, row.y, texturePreviewSize, texturePreviewSize);
            DrawTexturePreview(previewRect, texture.objectReferenceValue as Texture);
            Rect fieldRect = new Rect(previewRect.xMax + 3f, controlY, row.width - labelWidth - texturePreviewSize - checkboxWidth - 6f, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(fieldRect, texture, GUIContent.none);
            if (EditorGUI.EndChangeCheck()) apply.boolValue = true;
            EditorGUI.PropertyField(new Rect(row.xMax - checkboxWidth, controlY, checkboxWidth, EditorGUIUtility.singleLineHeight), apply, GUIContent.none);
        }

        private static void DrawValueSemantic(ref float y, Rect elementRect, string label, SerializedProperty value, SerializedProperty apply)
        {
            Rect row = NextRow(ref y, elementRect);
            const float labelWidth = 110f;
            const float checkboxWidth = 18f;
            EditorGUI.LabelField(new Rect(row.x, row.y, labelWidth, row.height), label);
            Rect fieldRect = new Rect(row.x + labelWidth, row.y, row.width - labelWidth - checkboxWidth - 3f, row.height);
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(fieldRect, value, GUIContent.none);
            if (EditorGUI.EndChangeCheck()) apply.boolValue = true;
            EditorGUI.PropertyField(new Rect(row.xMax - checkboxWidth, row.y, checkboxWidth, row.height), apply, GUIContent.none);
        }

        private static void DrawApplyHeader(ref float y, Rect elementRect, string label, SerializedProperty apply)
        {
            Rect row = NextRow(ref y, elementRect);
            const float checkboxWidth = 18f;
            EditorGUI.LabelField(new Rect(row.x, row.y, row.width - checkboxWidth, row.height), label, EditorStyles.boldLabel);
            EditorGUI.PropertyField(new Rect(row.xMax - checkboxWidth, row.y, checkboxWidth, row.height), apply, GUIContent.none);
        }

        private static void DrawUvProperty(ref float y, Rect elementRect, string label, SerializedProperty value, SerializedProperty apply)
        {
            Rect row = NextRow(ref y, elementRect);
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(row, value, new GUIContent(label));
            if (EditorGUI.EndChangeCheck()) apply.boolValue = true;
        }

        private static void DrawTexturePreview(Rect rect, Texture texture)
        {
            if (texture == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f));
                return;
            }

            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
        }

        private static bool TryBuildChildRendererEntries(MaterialProxy proxy, MaterialShaderAdapter adapter, out List<MaterialProxyEntry> entries, out int configuredCount)
        {
            entries = new List<MaterialProxyEntry>();
            configuredCount = 0;

            SkinnedMeshRenderer[] renderers = proxy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Material[] materials = renderer.sharedMaterials;
                for (int channel = 0; channel < materials.Length; channel++)
                {
                    Material material = materials[channel];
                    if (material == null) continue;
                    var entry = new MaterialProxyEntry
                    {
                        entryName = GetEntryName(proxy.transform, renderer.transform, channel),
                        renderer = renderer,
                        materialChannel = channel
                    };
                    if (TryReadValues(adapter, material, out MaterialProxySemanticValues values, out _))
                    {
                        entry.adapter = adapter;
                        entry.configuredValues = values;
                        configuredCount++;
                    }

                    entries.Add(entry);
                }
            }

            return entries.Count > 0;
        }

        private static bool TryReadValues(MaterialShaderAdapter adapter, Material material, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            values = default;
            if (material.shader == null || material.shader.name != adapter.ExpectedShaderName)
            {
                diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ShaderMismatch, "The selected adapter does not support this Material shader.");
                return false;
            }

            var plan = new List<MaterialPropertyReadCommand>();
            if (!adapter.TryBuildReadPlan(plan, out diagnostic)) return false;
            for (int i = 0; i < plan.Count; i++)
            {
                if (!material.HasProperty(plan[i].PropertyId))
                {
                    diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter read plan is invalid for the current Material.");
                    return false;
                }
            }

            return adapter.TryReadValues(material, plan, out values, out diagnostic);
        }

        private static string GetEntryName(Transform root, Transform renderer, int materialChannel)
        {
            string path = renderer == root ? root.name : renderer.name;
            for (Transform current = renderer.parent; current != null && current != root; current = current.parent) path = current.name + "/" + path;
            return path + " [" + materialChannel + "]";
        }

        private void WriteEntries(List<MaterialProxyEntry> entries)
        {
            SerializedProperty list = serializedObject.FindProperty("entries");
            list.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry source = entries[i];
                SerializedProperty destination = list.GetArrayElementAtIndex(i);
                destination.FindPropertyRelative("entryName").stringValue = source.entryName;
                destination.FindPropertyRelative("renderer").objectReferenceValue = source.renderer;
                destination.FindPropertyRelative("materialChannel").intValue = source.materialChannel;
                destination.FindPropertyRelative("adapter").objectReferenceValue = source.adapter;
                SerializedProperty values = destination.FindPropertyRelative("configuredValues");
                values.FindPropertyRelative("applyBaseColorTexture").boolValue = source.configuredValues.applyBaseColorTexture;
                values.FindPropertyRelative("baseColorTexture").objectReferenceValue = source.configuredValues.baseColorTexture;
                values.FindPropertyRelative("applyNormalTexture").boolValue = source.configuredValues.applyNormalTexture;
                values.FindPropertyRelative("normalTexture").objectReferenceValue = source.configuredValues.normalTexture;
                values.FindPropertyRelative("applyColor").boolValue = source.configuredValues.applyColor;
                values.FindPropertyRelative("color").colorValue = source.configuredValues.color;
                values.FindPropertyRelative("applyUvTransform").boolValue = source.configuredValues.applyUvTransform;
                values.FindPropertyRelative("uvScale").vector2Value = source.configuredValues.uvScale;
                values.FindPropertyRelative("uvOffset").vector2Value = source.configuredValues.uvOffset;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    internal static class MaterialShaderAdapterCreateMenu
    {
        [MenuItem("Assets/Create/zgock/ShapeSync/Material Shader Adapters/URP Lit", priority = 100)]
        private static void CreateUrpLit() => Create<UrpLitMaterialShaderAdapter>("UrpLitMaterialShaderAdapter");

        [MenuItem("Assets/Create/zgock/ShapeSync/Material Shader Adapters/URP Unlit", priority = 101)]
        private static void CreateUrpUnlit() => Create<UrpUnlitMaterialShaderAdapter>("UrpUnlitMaterialShaderAdapter");

        [MenuItem("Assets/Create/zgock/ShapeSync/Material Shader Adapters/MToon10", priority = 102)]
        private static void CreateMToon10() => Create<MToon10MaterialShaderAdapter>("MToon10MaterialShaderAdapter");

        private static void Create<T>(string assetName) where T : MaterialShaderAdapter
        {
            T asset = ScriptableObject.CreateInstance<T>();
            ProjectWindowUtil.CreateAsset(asset, assetName + ".asset");
        }
    }
}
