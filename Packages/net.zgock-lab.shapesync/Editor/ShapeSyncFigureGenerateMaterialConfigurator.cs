// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Editor-only serialization of existing Step 4 material runtime inputs; it adds no runtime API or behavior.</summary>
    internal static class ShapeSyncFigureGenerateMaterialConfigurator
    {
        internal static bool TryConfigure(ShapeSyncFigureGenerateSnapshot snapshot, ShapeSyncFigureGenerateMeshBuilder.Result figure, out MaterialBinding binding, out MeshBinding normalBinding, out StackMachineDiagnostic diagnostic)
        {
            binding = null; normalBinding = null; diagnostic = null;
            try
            {
                MaterialProxy proxy = figure.Figure.GetComponent<MaterialProxy>();
                NormalBlender normal = figure.Figure.GetComponent<NormalBlender>();
                if (proxy == null || normal == null) throw new InvalidOperationException("Generated Figure is missing MaterialProxy or NormalBlender.");
                SkinnedMeshRenderer renderer = figure.Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                SerializedObject proxySo = new SerializedObject(proxy); SerializedProperty entries = proxySo.FindProperty("entries"); entries.arraySize = snapshot.MaterialEntries.Count;
                for (int i = 0; i < snapshot.MaterialEntries.Count; i++) { var source = snapshot.MaterialEntries.OrderBy(x => x.MaterialSlot).ElementAt(i); var entry = entries.GetArrayElementAtIndex(i); entry.FindPropertyRelative("entryName").stringValue = source.LogicalName; entry.FindPropertyRelative("renderer").objectReferenceValue = renderer; entry.FindPropertyRelative("materialChannel").intValue = source.MaterialSlot; entry.FindPropertyRelative("adapter").objectReferenceValue = source.Adapter; }
                proxySo.ApplyModifiedPropertiesWithoutUndo();
                Material[] outputMaterials = renderer.sharedMaterials.ToArray();
                var textureClones = new Dictionary<Texture, Texture2D>();
                foreach (var resource in snapshot.TextureResources.Where(value => value.Texture is Texture2D)) { Texture2D clone = (Texture2D)ShapeSyncEditorTextureUtility.Clone(resource.Texture); clone.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(resource.Texture) ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName : resource.LogicalName; textureClones.Add(resource.Texture, clone); figure.OwnGeneratedAsset(clone); }
                foreach (var source in snapshot.MaterialEntries.OrderBy(x => x.MaterialSlot))
                {
                    Material clone = UnityEngine.Object.Instantiate(source.MaterialAsset);
                    clone.name = source.LogicalName;
                    var baseColorProperties = new List<string>();
                    if (!source.Adapter.TryGetPublishTextureProperties(MaterialProxySemantic.BaseColorTexture, baseColorProperties, out MaterialProxyDiagnostic baseColorDiagnostic))
                        throw new InvalidOperationException(baseColorDiagnostic.message);
                    var normalProperties = new List<string>();
                    if (!source.Adapter.TryGetPublishTextureProperties(MaterialProxySemantic.NormalTexture, normalProperties, out MaterialProxyDiagnostic normalDiagnostic))
                        throw new InvalidOperationException(normalDiagnostic.message);
                    // Texture Resources own every Material texture property, not only the
                    // two semantic properties consumed by MaterialProxy.  Rebind all aliases
                    // before persistence so an overwrite never serializes a Database sub-asset
                    // reference through an auxiliary shader property (for example Emission).
                    foreach (string property in clone.GetTexturePropertyNames())
                    {
                        Texture texture = clone.GetTexture(property);
                        if (texture != null && textureClones.TryGetValue(texture, out Texture2D replacement)) clone.SetTexture(property, replacement);
                    }
                    outputMaterials[source.MaterialSlot] = clone;
                    figure.OwnGeneratedAsset(clone);
                }
                renderer.sharedMaterials = outputMaterials;
                ShapeSyncFigureGenerateSnapshot.Normal[] runtimeNormals = snapshot.NormalEntries.ToArray();
                SerializedObject normalSo = new SerializedObject(normal); var normalEntries = normalSo.FindProperty("entries");
                string[] runtimeNormalEntryNames = runtimeNormals.Select(entry => entry.MaterialEntryName).Distinct(StringComparer.Ordinal).ToArray();
                normalEntries.arraySize = runtimeNormalEntryNames.Length;
                for (int i = 0; i < normalEntries.arraySize; i++) normalEntries.GetArrayElementAtIndex(i).stringValue = runtimeNormalEntryNames[i];
                normalSo.FindProperty("dynamicBoneBlender").objectReferenceValue = figure.Figure.GetComponent<DynamicBoneBlender>(); normalSo.ApplyModifiedPropertiesWithoutUndo();
                binding = ScriptableObject.CreateInstance<MaterialBinding>(); figure.OwnGeneratedAsset(binding); SerializedObject bindingSo = new SerializedObject(binding); var textures = bindingSo.FindProperty("textures"); var resources = snapshot.TextureResources.Where(x => x.Texture is Texture2D).ToArray(); textures.arraySize = resources.Length; for (int i = 0; i < resources.Length; i++) { var entry = textures.GetArrayElementAtIndex(i); entry.FindPropertyRelative("logicalName").stringValue = resources[i].LogicalName; entry.FindPropertyRelative("sourceTexture").objectReferenceValue = textureClones[resources[i].Texture]; } bindingSo.ApplyModifiedPropertiesWithoutUndo();
                normalBinding = ScriptableObject.CreateInstance<MeshBinding>();
                figure.OwnGeneratedAsset(normalBinding);
                SerializedObject normalBindingSo = new SerializedObject(normalBinding);
                ShapeSyncFigureGenerateSnapshot.Axis[] morphAxes = snapshot.Axes
                    .Where(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm || axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                    .OrderBy(axis => axis.Kind).ThenBy(axis => axis.Name, StringComparer.Ordinal).ToArray();
                SerializedProperty morphs = normalBindingSo.FindProperty("morphs");
                morphs.arraySize = morphAxes.Length;
                for (int i = 0; i < morphAxes.Length; i++)
                {
                    ShapeSyncFigureGenerateSnapshot.Axis axis = morphAxes[i];
                    string targetName = axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                        ? axis.Name
                        : BlendShapeReservedPrefixes.Pbm + axis.Name;
                    if (!figure.RuntimeTargets.Any(target => target != null && string.Equals(target.blendName, targetName, StringComparison.Ordinal)))
                        throw new InvalidOperationException("Generated DynamicBoneBlender target is missing for Figure axis: " + axis.Name);
                    SerializedProperty morph = morphs.GetArrayElementAtIndex(i);
                    morph.FindPropertyRelative("logicalName").stringValue = axis.Name;
                    morph.FindPropertyRelative("targetName").stringValue = targetName;
                }
                var normalGroups = runtimeNormals.GroupBy(value => value.ShapeKey)
                    .ToDictionary(value => value.Key, value => value.OrderBy(entry => entry.MaterialEntryName, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
                SerializedProperty owners = normalBindingSo.FindProperty("normalOwners");
                if (normalGroups.Count == 0)
                {
                    owners.arraySize = 0;
                    normalBindingSo.ApplyModifiedPropertiesWithoutUndo();
                    return true;
                }
                owners.arraySize = 1;
                SerializedProperty owner = owners.GetArrayElementAtIndex(0);
                owner.FindPropertyRelative("outfitRegistryId").stringValue = string.Empty;
                SerializedProperty targets = owner.FindPropertyRelative("targets");
                string[] targetKeys = new[] { ShapeSyncDatabaseRegistry.BaseShapeKey }
                    .Concat(normalGroups.Keys.Where(key => key != ShapeSyncDatabaseRegistry.BaseShapeKey).OrderBy(key => key, StringComparer.Ordinal)).ToArray();
                targets.arraySize = targetKeys.Length;
                for (int i = 0; i < targetKeys.Length; i++)
                {
                    SerializedProperty target = targets.GetArrayElementAtIndex(i);
                    string shapeKey = targetKeys[i];
                    target.FindPropertyRelative("targetName").stringValue = shapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey ? string.Empty : shapeKey;
                    ShapeSyncFigureGenerateSnapshot.Normal[] values = normalGroups.TryGetValue(shapeKey, out ShapeSyncFigureGenerateSnapshot.Normal[] resolved)
                        ? resolved : Array.Empty<ShapeSyncFigureGenerateSnapshot.Normal>();
                    SerializedProperty normalTextures = target.FindPropertyRelative("textures");
                    normalTextures.arraySize = values.Length;
                    for (int j = 0; j < values.Length; j++)
                    {
                        if (!textureClones.TryGetValue(values[j].Texture, out Texture2D normalTexture))
                            throw new InvalidOperationException("Declared Normal Texture must resolve to a generated Texture2D.");
                        SerializedProperty normalEntry = normalTextures.GetArrayElementAtIndex(j);
                        normalEntry.FindPropertyRelative("entryName").stringValue = values[j].MaterialEntryName;
                        normalEntry.FindPropertyRelative("normalTexture").objectReferenceValue = normalTexture;
                    }
                }
                normalBindingSo.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
            catch (Exception ex)
            {
                if (binding != null) UnityEngine.Object.DestroyImmediate(binding);
                if (normalBinding != null) UnityEngine.Object.DestroyImmediate(normalBinding);
                binding = null;
                normalBinding = null;
                diagnostic = StackMachineDiagnostic.CreateDomain("figure-generate", "MaterialGenerateInvalid", ex.Message);
                return false;
            }
        }
    }
}
#endif
