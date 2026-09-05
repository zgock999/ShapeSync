// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Authoring-only exact shader resolver for Spec20.4 Material Entry admission.</summary>
    internal static class ShapeSyncMaterialAdapterResolver
    {
        internal static Dictionary<Type, MaterialShaderAdapter> CreateDatabaseAdapterCache(ShapeSyncDatabaseRegistry registry)
        {
            var result = new Dictionary<Type, MaterialShaderAdapter>();
            if (registry == null) return result;
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in registry.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialEntry>())
                if (entry != null && entry.Adapter != null && !result.ContainsKey(entry.Adapter.GetType())) result.Add(entry.Adapter.GetType(), entry.Adapter);
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in registry.Outfits ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>())
                foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry entry in outfit?.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                    if (entry != null && entry.Adapter != null && !result.ContainsKey(entry.Adapter.GetType())) result.Add(entry.Adapter.GetType(), entry.Adapter);
            return result;
        }

        internal static void CanonicalizeDatabaseAdapters(ShapeSyncDatabase database, ShapeSyncDatabaseTransaction.EditContext transaction,
            string databaseAssetPath, Dictionary<Type, MaterialShaderAdapter> cache)
        {
            if (database == null || database.Registry == null || transaction == null || cache == null) return;
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialEntry>())
            {
                if (entry?.Adapter == null) continue;
                if (!cache.TryGetValue(entry.Adapter.GetType(), out MaterialShaderAdapter canonical)) cache.Add(entry.Adapter.GetType(), entry.Adapter);
                else entry.RebindAdapter(canonical);
            }
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>())
                foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry entry in outfit?.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                {
                    if (entry?.Adapter == null) continue;
                    if (!cache.TryGetValue(entry.Adapter.GetType(), out MaterialShaderAdapter canonical)) cache.Add(entry.Adapter.GetType(), entry.Adapter);
                    else entry.RebindAdapter(canonical);
                }
            HashSet<MaterialShaderAdapter> protectedAdapters = new HashSet<MaterialShaderAdapter>(cache.Values.Where(value => value != null));
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(databaseAssetPath)
                .Where(asset => asset is MaterialShaderAdapter adapter && !protectedAdapters.Contains(adapter)).ToArray())
                transaction.RemoveSubAsset(asset);
        }

        /// <summary>Creates the deterministic draft name for one Base renderer material slot.</summary>
        internal static string CreateDefaultEntryName(int materialSlot)
        {
            if (materialSlot < 0) throw new ArgumentOutOfRangeException(nameof(materialSlot));
            return "MaterialEntry-" + materialSlot;
        }

        /// <summary>
        /// Detached Step 1 admission result. It deliberately is not serialized; Step 2 owns cloning,
        /// staging, and the persistent Material Entry / Texture resource definition.
        /// </summary>
        internal sealed class Admission : IDisposable
        {
            internal string LogicalName { get; }
            internal SkinnedMeshRenderer Renderer { get; }
            /// <summary>Stable path from the registered Base Figure, used after the save transaction reloads the Prefab.</summary>
            internal string BaseRelativeRendererPath { get; }
            internal int MaterialSlot { get; }
            internal Material SourceMaterial { get; }
            internal string SourceMaterialName { get; }
            /// <summary>Transient BaseColor texture used only for Step 4 draft preview.</summary>
            internal Texture PreviewTexture { get; }
            internal MaterialShaderAdapter TransientAdapter { get; private set; }

            internal Admission(string logicalName, SkinnedMeshRenderer renderer, string baseRelativeRendererPath, int materialSlot, Material sourceMaterial, Texture previewTexture, MaterialShaderAdapter transientAdapter)
            {
                LogicalName = logicalName;
                Renderer = renderer;
                BaseRelativeRendererPath = baseRelativeRendererPath;
                MaterialSlot = materialSlot;
                SourceMaterial = sourceMaterial;
                SourceMaterialName = sourceMaterial.name;
                PreviewTexture = previewTexture;
                TransientAdapter = transientAdapter;
            }

            public void Dispose()
            {
                if (TransientAdapter == null) return;
                if (!AssetDatabase.Contains(TransientAdapter)) UnityEngine.Object.DestroyImmediate(TransientAdapter);
                TransientAdapter = null;
            }
        }

        /// <summary>Admits a Base renderer slot and owns the transient exact adapter until disposed.</summary>
        internal static bool TryAdmit(ShapeSyncDatabase database, string logicalName, SkinnedMeshRenderer renderer, int materialSlot, Material material, out Admission admission, out string diagnostic)
        {
            admission = null;
            diagnostic = null;
            if (database == null || database.Registry == null)
            {
                diagnostic = "Material Entry requires a ShapeSync Database with its fixed registry.";
                return false;
            }
            if (!database.Registry.TryValidateMaterialEntry(database, logicalName, renderer, materialSlot, material, out diagnostic)) return false;
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseEntry, out diagnostic)
                || baseEntry == null
                || !TryGetRelativePath(baseEntry.Figure.transform, renderer.transform, out string baseRelativeRendererPath))
            {
                diagnostic ??= "Material Entry renderer could not be addressed from the registered Base Figure.";
                return false;
            }
            if (!TryCreateFor(material, out MaterialShaderAdapter adapter, out diagnostic)) return false;
            if (!TryReadBaseColorTexture(material, adapter, out Texture previewTexture, out diagnostic))
            {
                UnityEngine.Object.DestroyImmediate(adapter);
                return false;
            }
            admission = new Admission(logicalName, renderer, baseRelativeRendererPath, materialSlot, material, previewTexture, adapter);
            return true;
        }

        private static bool TryGetRelativePath(Transform root, Transform target, out string path)
        {
            path = null;
            if (root == null || target == null || (target != root && !target.IsChildOf(root))) return false;
            if (target == root)
            {
                path = string.Empty;
                return true;
            }

            var segments = new System.Collections.Generic.Stack<string>();
            for (Transform current = target; current != root; current = current.parent) segments.Push(current.GetSiblingIndex().ToString(System.Globalization.CultureInfo.InvariantCulture));
            path = string.Join("/", segments);
            return true;
        }

        private static bool TryReadBaseColorTexture(Material material, MaterialShaderAdapter adapter, out Texture texture, out string diagnostic)
        {
            texture = null;
            diagnostic = null;
            foreach (MaterialPropertyBindingTemplate binding in adapter.AssignmentTemplates)
            {
                if (binding.valueSource != MaterialPropertyValueSource.BaseColorTexture || binding.writeKind != MaterialPropertyWriteKind.Texture) continue;
                if (!material.HasProperty(binding.propertyName))
                {
                    diagnostic = "Material Entry adapter BaseColor property is missing from the Material: " + binding.propertyName;
                    return false;
                }
                texture = material.GetTexture(binding.propertyName);
                return true;
            }
            diagnostic = "Material Entry adapter does not declare a BaseColor texture property.";
            return false;
        }

        /// <summary>
        /// Creates one detached adapter for a supported shader.
        /// The caller owns the returned transient adapter until Step 2 transfers a clone into the Database.
        /// </summary>
        internal static bool TryCreateFor(Material material, out MaterialShaderAdapter adapter, out string diagnostic)
        {
            adapter = null;
            diagnostic = null;
            if (material == null || material.shader == null)
            {
                diagnostic = "Material Entry requires a Material with a shader.";
                return false;
            }

            switch (material.shader.name)
            {
                case "Universal Render Pipeline/Unlit":
                    adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                    break;
                case "Universal Render Pipeline/Lit":
                    adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                    break;
                case "VRM10/Universal Render Pipeline/MToon10":
                    adapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
                    break;
                default:
                    diagnostic = "Material Entry shader has no ShapeSync Material Shader Adapter: " + material.shader.name;
                    return false;
            }

            if (!string.Equals(adapter.ExpectedShaderName, material.shader.name, StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(adapter);
                adapter = null;
                diagnostic = "Material Entry adapter resolution did not match the Material shader.";
                return false;
            }
            return true;
        }
    }
}
#endif
