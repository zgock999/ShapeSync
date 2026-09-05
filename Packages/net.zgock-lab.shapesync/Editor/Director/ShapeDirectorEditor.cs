// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Custom Inspector for editing Director configuration and detached runtime Shape values.</summary>
    [CustomEditor(typeof(ShapeDirector))]
    internal sealed class ShapeDirectorEditor : UnityEditor.Editor
    {
        private SerializedProperty templateList;
        private SerializedProperty autoCompile;
        private SerializedProperty abortOnOutfitMaterialFailure;
        private SerializedProperty meshBinding;
        private SerializedProperty materialBinding;
        private SerializedProperty serializer;
        private SerializedProperty deserializer;
        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
        private string message;
        private bool messageIsError;

        private void OnEnable()
        {
            templateList = serializedObject.FindProperty("TemplateList");
            autoCompile = serializedObject.FindProperty("autoCompile");
            abortOnOutfitMaterialFailure = serializedObject.FindProperty("abortOnOutfitMaterialFailure");
            meshBinding = serializedObject.FindProperty("meshBinding");
            materialBinding = serializedObject.FindProperty("materialBinding");
            serializer = serializedObject.FindProperty("serializer");
            deserializer = serializedObject.FindProperty("deserializer");
        }

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Template Input", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(templateList, new GUIContent("Template List"), true);
            EditorGUILayout.HelpBox("Template List is inspector input only. Its edits do not change Runtime Shapes until Sync.", MessageType.Info);
            EditorGUILayout.PropertyField(autoCompile, new GUIContent("Auto Compile"));
            EditorGUILayout.PropertyField(abortOnOutfitMaterialFailure, new GUIContent("Abort on Outfit Material Failure"));
            EditorGUILayout.PropertyField(meshBinding);
            EditorGUILayout.PropertyField(materialBinding);
            EditorGUILayout.PropertyField(serializer, new GUIContent("Serializer"));
            EditorGUILayout.PropertyField(deserializer, new GUIContent("Deserializer"));
            EditorGUILayout.HelpBox("When these references are empty, Save and Load use ShapeSerializer / ShapeDeserializer components on this Figure. The standard ShapeDocument Serializer creates its first carrier asset when Save is pressed.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();

            ShapeDirector director = (ShapeDirector)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Sync Template List to Runtime Shapes"))
                Report(director.TrySynchronizeTemplateList(out var syncDiagnostic), syncDiagnostic == null ? "Template List synchronized." : syncDiagnostic.message);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Shapes (Authoritative)", EditorStyles.boldLabel);
            DrawRuntimeShapes(director);

            EditorGUILayout.Space();
            bool compileRequested;
            bool saveRequested;
            bool loadRequested;
            using (new EditorGUILayout.HorizontalScope())
            {
                compileRequested = GUILayout.Button("Compile");
                saveRequested = GUILayout.Button("Save");
                loadRequested = GUILayout.Button("Load");
            }
            // Load can synchronously detach Outfits and invalidate/rebuild the inspected
            // hierarchy. Execute it only after the GUILayout scope has been disposed; doing so
            // inside the scope leaves EndLayoutGroup without its matching BeginLayoutGroup.
            if (compileRequested) Report(director.TryCompile(out var diagnostic), diagnostic == null ? "Compile accepted." : diagnostic.message);
            if (saveRequested && !TrySave(director, out bool saveCancelled) && !saveCancelled)
                Report(false, director.Serializer == null ? "Save failed: Figure has no ShapeSerializer component." : "Save failed: the configured ShapeSerializer rejected the selected file name.");
            if (loadRequested)
            {
                if (!TryLoad(director, out bool loadCancelled) && !loadCancelled)
                    Report(false, director.Deserializer == null ? "Load failed: Figure has no ShapeDeserializer component." : "Load failed: the configured ShapeDeserializer rejected the selected file name.");
                return;
            }
            if (director.LastTransactionDiagnostic != null) EditorGUILayout.HelpBox(director.LastTransactionDiagnostic.domainCode + ": " + director.LastTransactionDiagnostic.message, MessageType.Warning);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageIsError ? MessageType.Error : MessageType.Info);
        }

        private void DrawRuntimeShapes(ShapeDirector director)
        {
            IReadOnlyList<ShapeSyncShape> shapes = director.RuntimeShapes;
            if (shapes.Count == 0) { EditorGUILayout.HelpBox("Runtime List is empty. Add a Template to create a runtime Shape.", MessageType.None); return; }
            for (int i = 0; i < shapes.Count; i++) DrawRuntimeShape(director, shapes[i], i);
        }

        private void DrawRuntimeShape(ShapeDirector director, ShapeSyncShape source, int index)
        {
            string key = source.ShapeId + "#" + index;
            foldouts.TryGetValue(key, out bool expanded);
            expanded = EditorGUILayout.Foldout(expanded, source.GetType().Name + " — " + source.ShapeId, true);
            foldouts[key] = expanded;
            if (!expanded) return;
            if (GUILayout.Button("Remove Shape"))
            {
                if (director.TryRemoveRuntimeShape(source.ShapeId, out var removeDiagnostic))
                {
                    foldouts.Remove(key);
                    messageIsError = false;
                    message = "Runtime Shape removed.";
                    return;
                }

                messageIsError = true;
                message = removeDiagnostic.domainCode + ": " + removeDiagnostic.message;
                Debug.LogWarning("Shape Director Inspector operation rejected. " + message, director);
                return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Shape Id", source.ShapeId);
            int priority = EditorGUILayout.IntField("Priority", source.Priority);
            string tags = EditorGUILayout.TextField("Tags (comma separated)", string.Join(", ", source.Tags));
            List<string> tagValues = SplitTags(tags);
            ShapeSyncShape replacement = source;
            bool changed = priority != source.Priority || !TagsEqual(source.Tags, tagValues);

            if (source is MorphShape morph) replacement = DrawMorph(morph, priority, tagValues, ref changed);
            else if (source is PartsShape parts) replacement = DrawParts(parts, priority, tagValues, ref changed);
            if (changed)
            {
                if (!director.TryReplaceRuntimeShape(replacement, out var diagnostic)) message = diagnostic.domainCode + ": " + diagnostic.message;
                else message = "Runtime Shape updated.";
            }
            EditorGUI.indentLevel--;
        }

        private static ShapeSyncShape DrawMorph(MorphShape source, int priority, List<string> tags, ref bool changed)
        {
            var values = new List<MorphValue>(source.Morphs);
            EditorGUILayout.LabelField("Morphs", EditorStyles.miniBoldLabel);
            for (int i = 0; i < values.Count; i++)
            {
                MorphValue value = values[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string target = EditorGUILayout.TextField(value.Target);
                    float weight = DynamicBoneBlendWeightField.DrawLayout(value.Value);
                    if (GUILayout.Button("−", GUILayout.Width(24f))) { values.RemoveAt(i); changed = true; break; }
                    if (target != value.Target || weight != value.Value) { value.Target = target; value.Value = weight; values[i] = value; changed = true; }
                }
            }
            if (GUILayout.Button("Add Morph")) { values.Add(default); changed = true; }
            return changed ? new MorphShape(source.ShapeId, priority, tags, values) : source;
        }

        private static ShapeSyncShape DrawParts(PartsShape source, int priority, List<string> tags, ref bool changed)
        {
            var parts = new List<ShapeEntry>();
            for (int i = 0; i < source.Parts.Count; i++) parts.Add(source.Parts[i].Clone());
            EditorGUILayout.LabelField("Parts", EditorStyles.miniBoldLabel);
            for (int i = 0; i < parts.Count; i++)
            {
                ShapeEntry entry = parts[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(entry.GetType().Name, EditorStyles.miniBoldLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(62f))) { parts.RemoveAt(i); changed = true; break; }
                    }
                    DrawEntry(entry, ref changed);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Mesh")) { parts.Add(new MeshEntry()); changed = true; }
                if (GUILayout.Button("Add Texture")) { parts.Add(new TextureEntry()); changed = true; }
                if (GUILayout.Button("Add Color")) { parts.Add(new ColorEntry()); changed = true; }
                if (GUILayout.Button("Add UVSet")) { parts.Add(new UvsetEntry()); changed = true; }
            }
            if (!changed) return source;
            if (source is SkinShape) return new SkinShape(source.ShapeId, priority, tags, parts);
            if (source is HairShape) return new HairShape(source.ShapeId, priority, tags, parts);
            return new OutfitShape(source.ShapeId, priority, tags, parts);
        }

        private static void DrawEntry(ShapeEntry entry, ref bool changed)
        {
            if (entry is MeshEntry mesh)
            {
                string value = EditorGUILayout.TextField("Logical Name", mesh.LogicalName);
                if (value != mesh.LogicalName) { mesh.LogicalName = value; changed = true; }
                for (int i = 0; i < mesh.Masks.Count; i++)
                {
                    MeshMaskEntry mask = mesh.Masks[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Mask", GUILayout.Width(42f));
                        string proxyValue = EditorGUILayout.TextField("Proxy", mask?.ProxyEntryName);
                        string logical = EditorGUILayout.TextField("Texture", mask?.MaskName);
                        if (GUILayout.Button("-", GUILayout.Width(22f))) { mesh.Masks.RemoveAt(i); changed = true; i--; continue; }
                        if (mask == null) { mesh.Masks[i] = new MeshMaskEntry(); mask = mesh.Masks[i]; changed = true; }
                        if (proxyValue != mask.ProxyEntryName) { mask.ProxyEntryName = proxyValue; changed = true; }
                        if (logical != mask.MaskName) { mask.MaskName = logical; changed = true; }
                    }
                }
                if (GUILayout.Button("Add Mask")) { mesh.Masks.Add(new MeshMaskEntry()); changed = true; }
                return;
            }
            if (!(entry is MaterialEntry material)) return;
            string registry = EditorGUILayout.TextField("Registry Id", material.RegistryId);
            string proxy = EditorGUILayout.TextField("Proxy Entry", material.ProxyEntry);
            if (registry != material.RegistryId) { material.RegistryId = registry; changed = true; }
            if (proxy != material.ProxyEntry) { material.ProxyEntry = proxy; changed = true; }
            if (entry is TextureEntry texture)
            {
                string logical = EditorGUILayout.TextField("Texture", texture.LogicalName);
                bool useColor = EditorGUILayout.Toggle("Use Color", texture.UseColor); Color32 color = EditorGUILayout.ColorField("Color", texture.Color);
                if (logical != texture.LogicalName) { texture.LogicalName = logical; changed = true; }
                if (useColor != texture.UseColor) { texture.UseColor = useColor; changed = true; } if (!color.Equals(texture.Color)) { texture.Color = color; changed = true; }
            }
            else if (entry is ColorEntry color) { Color32 value = EditorGUILayout.ColorField("Color", color.Color); if (!value.Equals(color.Color)) { color.Color = value; changed = true; } }
            else if (entry is UvsetEntry uv) { float sx = EditorGUILayout.FloatField("Scale X", uv.ScaleX); float sy = EditorGUILayout.FloatField("Scale Y", uv.ScaleY); float ox = EditorGUILayout.FloatField("Offset X", uv.OffsetX); float oy = EditorGUILayout.FloatField("Offset Y", uv.OffsetY); if (sx != uv.ScaleX || sy != uv.ScaleY || ox != uv.OffsetX || oy != uv.OffsetY) { uv.ScaleX = sx; uv.ScaleY = sy; uv.OffsetX = ox; uv.OffsetY = oy; changed = true; } }
        }

        private void Report(bool succeeded, string failure)
        {
            messageIsError = !succeeded;
            message = succeeded ? "Operation accepted." : failure;
            if (!succeeded) Debug.LogWarning("Shape Director Inspector operation rejected. " + failure, target as ShapeDirector);
        }
        private static bool TrySave(ShapeDirector director, out bool cancelled)
        {
            cancelled = false;
            if (director.Serializer == null) return false;
            string path = EditorUtility.SaveFilePanelInProject("Save Shape Document", "ShapeDocument", "asset", "Choose the new ShapeDocument asset to create.");
            if (string.IsNullOrEmpty(path)) { cancelled = true; return false; }
            return director.TrySerialize(path);
        }
        private static bool TryLoad(ShapeDirector director, out bool cancelled)
        {
            cancelled = false;
            if (director.Deserializer == null) return false;
            string selectedPath = EditorUtility.OpenFilePanel("Load Shape Document", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(selectedPath)) { cancelled = true; return false; }
            string projectPath = FileUtil.GetProjectRelativePath(selectedPath);
            return !string.IsNullOrEmpty(projectPath) && director.TryDeserialize(projectPath);
        }
        private static List<string> SplitTags(string value) { var result = new List<string>(); foreach (string part in value.Split(',')) { string tag = part.Trim(); if (tag.Length != 0) result.Add(tag); } return result; }
        private static bool TagsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) { if (left.Count != right.Count) return false; for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false; return true; }
    }
}
