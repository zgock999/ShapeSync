// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Builds the explicit Include-only Outfit variants selected for Figure PBM follow.</summary>
    internal static class ShapeSyncMeshOutfitPbmFollowAuthoring
    {
        internal readonly struct Source
        {
            internal Source(string pbmAxisName, string shapeKey, GameObject prefab)
            { PbmAxisName = pbmAxisName; ShapeKey = shapeKey; Prefab = prefab; }
            internal string PbmAxisName { get; }
            internal string ShapeKey { get; }
            internal GameObject Prefab { get; }
        }

        private sealed class Staged
        {
            internal string PbmAxisName;
            internal string ShapeKey;
            internal ShapeSyncFigureMeshMerger.Result Merge;
        }

        /// <summary>Saves a complete explicit PBM-follow set. It never infers a PBM or a shape-key source.</summary>
        internal static bool TrySave(string databaseAssetPath, string outfitIdentity, IReadOnlyList<Source> sources, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath) || string.IsNullOrWhiteSpace(outfitIdentity) || sources == null)
            { diagnostic = "PBM follow Save requires a Database, Outfit Id, and explicit sources."; return false; }
            var staged = new List<Staged>();
            try
            {
                foreach (Source source in sources)
                {
                    if (string.IsNullOrWhiteSpace(source.PbmAxisName) || string.IsNullOrWhiteSpace(source.ShapeKey)
                        || !ShapeSyncMeshOutfitImport.TryValidateAxisSource(source.Prefab, out diagnostic)) return false;
                    if (!TryValidateGeometrySource(source.Prefab, out diagnostic)) return false;
                    SkinnedMeshRenderer[] renderers = source.Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (!ShapeSyncFigureMeshMerger.TryMergeOwnedGeometryOnly(source.Prefab, renderers, out ShapeSyncFigureMeshMerger.Result merge, out diagnostic)) return false;
                    ShapeSyncMeshOutfitImport.PreserveOutfitBoneHierarchy(source.Prefab.transform, merge.Root.transform);
                    staged.Add(new Staged { PbmAxisName = source.PbmAxisName, ShapeKey = source.ShapeKey, Merge = merge });
                }
                IGrouping<string, Staged>[] stagedGroups = staged.GroupBy(value => value.PbmAxisName, StringComparer.Ordinal).ToArray();
                Transform canonicalFigureRoot = null;
                if (ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase topologyDatabase, out string topologyDatabaseDiagnostic)
                    && topologyDatabase != null && topologyDatabase.Registry != null
                    && topologyDatabase.Registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseFigureDiagnostic)
                    && baseFigure != null && baseFigure.Figure != null)
                    canonicalFigureRoot = baseFigure.Figure.transform;
                foreach (IGrouping<string, Staged> group in stagedGroups)
                {
                    Staged[] baseItems = group.Where(value => value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).ToArray();
                    if (baseItems.Length != 1) continue;
                    Staged baseItem = baseItems[0];
                    foreach (Staged item in group.Where(value => value.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey))
                    {
                        if (!ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseItem.Merge.Renderer, item.Merge.Renderer,
                            outfitIdentity + "/" + group.Key + "/" + item.ShapeKey, item.Merge.Renderer == null ? "<merged>" : item.Merge.Renderer.name,
                            baseItem.Merge.Root.transform, item.Merge.Root.transform, canonicalFigureRoot,
                            out _, out StackMachineDiagnostic topologyDiagnostic))
                        {
                            diagnostic = topologyDiagnostic?.ToString() ?? "Outfit PBM bone-space normalization failed without a diagnostic.";
                            return false;
                        }
                    }
                }
                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.SingleOrDefault(entry => entry != null && entry.Identity == outfitIdentity);
                    if (outfit == null || outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
                        throw new InvalidOperationException("Mesh Outfit was not found: " + outfitIdentity);
                    var knownPbms = new HashSet<string>(database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm).Select(axis => axis.Name), StringComparer.Ordinal);
                    var expectedKeys = new HashSet<string>(database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal) { ShapeSyncDatabaseRegistry.BaseShapeKey };
                    IGrouping<string, Staged>[] groups = stagedGroups;
                    if (groups.Any(group => !knownPbms.Contains(group.Key) || group.Select(value => value.ShapeKey).Distinct(StringComparer.Ordinal).Count() != group.Count() || !new HashSet<string>(group.Select(value => value.ShapeKey), StringComparer.Ordinal).SetEquals(expectedKeys)))
                        throw new InvalidOperationException("PBM follow requires each selected Figure PBM to provide Base and every FBM source exactly once.");
                    foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry old in outfit.PbmFollows)
                        foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry oldFigure in old?.Figures ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>())
                        {
                            RemoveOldPrefab(ResolveDirectIntermediateChild(database, oldFigure?.SourcePrefab), databaseAssetPath, transaction);
                            RemoveOldPrefab(ResolveDirectIntermediateChild(database, oldFigure?.Figure), databaseAssetPath, transaction);
                        }
                    var follows = new List<ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry>();
                    foreach (IGrouping<string, Staged> group in groups.OrderBy(value => value.Key, StringComparer.Ordinal))
                    {
                        var figures = new List<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>();
                        foreach (Staged item in group.OrderBy(value => value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey ? 0 : 1).ThenBy(value => value.ShapeKey, StringComparer.Ordinal))
                        {
                            GameObject sourceCopy = CreateDatabaseSourcePrefab(database, intermediate, transaction, outfit, item);
                            figures.Add(new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(item.ShapeKey, sourceCopy,
                                CreateIncludeOnlyPrefab(databaseAssetPath, database, intermediate, transaction, outfit, item)));
                        }
                        follows.Add(new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry(group.Key, figures));
                    }
                    if (!database.Registry.TrySetOutfitPbmFollows(database, outfitIdentity, follows, out string registryDiagnostic)) throw new InvalidOperationException(registryDiagnostic);
                }, out diagnostic)) return false;
                foreach (Staged item in staged)
                {
                    item.Merge.DetachMesh();
                }
                return true;
            }
            finally
            {
                foreach (Staged item in staged)
                {
                    item.Merge?.Dispose();
                }
            }
        }

        private static bool TryValidateGeometrySource(GameObject sourcePrefab, out string diagnostic)
        {
            diagnostic = null;
            foreach (SkinnedMeshRenderer renderer in sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer?.sharedMesh;
                if (mesh == null)
                {
                    diagnostic = "PBMFollowSourceInvalid: source Mesh is missing.";
                    return false;
                }
                if (renderer.bones == null || renderer.bones.Length == 0
                    || mesh.bindposes == null || mesh.bindposes.Length != renderer.bones.Length
                    || mesh.boneWeights == null || mesh.boneWeights.Length != mesh.vertexCount)
                {
                    diagnostic = "PBMFollowSourceInvalid: source Mesh Utility-compatible weight and bindpose data is missing.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Invalidates every Mesh Outfit PBM-follow declaration when the Figure PBM/FBM
        /// axis set changes.  A follow is a complete relation to that exact Figure axis
        /// set, so it must never be renamed or completed by inference.
        /// </summary>
        internal static void ClearAllForFigureAxisChange(ShapeSyncDatabase database, string databaseAssetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            if (database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits
                .Where(entry => entry != null && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh))
            {
                foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow in outfit.PbmFollows)
                    foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry figure in follow?.Figures
                        ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>())
                    {
                        RemoveOldPrefab(ResolveDirectIntermediateChild(database, figure?.SourcePrefab), databaseAssetPath, transaction);
                        RemoveOldPrefab(ResolveDirectIntermediateChild(database, figure?.Figure), databaseAssetPath, transaction);
                    }
                outfit.SetPbmFollows(null);
            }
        }

        private static GameObject CreateDatabaseSourcePrefab(ShapeSyncDatabase database, Transform intermediate,
            ShapeSyncDatabaseTransaction.EditContext transaction, ShapeSyncDatabaseRegistry.OutfitEntry outfit, Staged item)
        {
            if (item?.Merge?.Root == null) throw new InvalidOperationException("PBMFollowSourceInvalid: merged source root is missing.");
            string figureName = item.ShapeKey;
            if (item.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                if (!database.Registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic) || baseFigure == null)
                    throw new InvalidOperationException("PBMFollowBaseFigureInvalid: " + baseDiagnostic);
                figureName = baseFigure.Name;
            }
            string sourceName = outfit.Identity + "_" + item.PbmAxisName + "_" + figureName + "_Source";
            GameObject source = UnityEngine.Object.Instantiate(item.Merge.Root);
            source.name = sourceName;
            source.transform.SetParent(intermediate, false);
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                    throw new InvalidOperationException("PBMFollowSourceInvalid: source clone renderer Mesh is missing; shapeKey=" + item.ShapeKey);
                Mesh mesh = ShapeSyncMeshCloneUtility.Clone(renderer.sharedMesh);
                mesh.name = sourceName + "_SkinnedMesh";
                transaction.AddSubAsset(mesh);
                renderer.sharedMesh = mesh;
                if (!ShapeSyncMeshOutfitImport.TryGetRequiredSubMeshSelection(outfit, renderer, item.ShapeKey, out _, out string classificationDiagnostic))
                    throw new InvalidOperationException(classificationDiagnostic);
                // PBM Source is geometry-only.  Included Material payload belongs to the
                // Outfit artifact and is rebound by CreateIncludeOnlyPrefab below; the
                // persisted Source must not retain or validate external Material refs.
                renderer.sharedMaterials = Array.Empty<Material>();
                EditorUtility.SetDirty(renderer);
            }
            EditorUtility.SetDirty(source);
            return source;
        }

        private static GameObject CreateIncludeOnlyPrefab(string databaseAssetPath, ShapeSyncDatabase database, Transform intermediate,
            ShapeSyncDatabaseTransaction.EditContext transaction, ShapeSyncDatabaseRegistry.OutfitEntry outfit, Staged item)
        {
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis = outfit.AxisFigures.SingleOrDefault(entry => entry != null && entry.ShapeKey == item.ShapeKey);
            SkinnedMeshRenderer saved = axis?.OutfitPrefab == null ? null : axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer renderer = item.Merge.Renderer;
            string savedPath = axis?.OutfitPrefab == null ? "<missing>" : AssetDatabase.GetAssetPath(axis.OutfitPrefab);
            string sourceName = item.Merge.Root == null ? "<missing>" : item.Merge.Root.name;
            if (saved == null)
                throw new InvalidOperationException("PBMFollowSourceInvalid: Saved Outfit renderer is missing; shapeKey=" + item.ShapeKey + "; savedPrefab=" + savedPath);
            if (renderer == null)
                throw new InvalidOperationException("PBMFollowSourceInvalid: Selected PBM Follow source renderer is missing; shapeKey=" + item.ShapeKey + "; source=" + sourceName);
            if (saved.sharedMesh == null || renderer.sharedMesh == null)
                throw new InvalidOperationException("PBMFollowSourceInvalid: Saved or selected source Mesh is missing; shapeKey=" + item.ShapeKey
                    + "; savedMesh=" + (saved.sharedMesh == null ? "<missing>" : saved.sharedMesh.name)
                    + "; sourceMesh=" + (renderer.sharedMesh == null ? "<missing>" : renderer.sharedMesh.name));
            if (!ShapeSyncMeshOutfitImport.TryGetRequiredSubMeshSelection(outfit, renderer, item.ShapeKey, out bool[] includedSubMeshes, out string classificationDiagnostic))
                throw new InvalidOperationException(classificationDiagnostic);
            int includedCount = includedSubMeshes.Count(value => value);
            Material[] canonicalMaterials = ResolveBaseCanonicalMaterials(databaseAssetPath, outfit, transaction, includedCount);
            if (saved.sharedMesh.subMeshCount != includedCount)
                throw new InvalidOperationException("PBMFollowSavedIncludeSlotMismatch: shapeKey=" + item.ShapeKey
                    + "; savedPrefab=" + savedPath + "; savedSubMeshes=" + saved.sharedMesh.subMeshCount
                    + "; includedSlots=" + includedCount);
            Mesh includeOnlyMesh = ShapeSyncMeshOutfitImport.BuildSelectedMesh(renderer.sharedMesh, includedSubMeshes);
            includeOnlyMesh.name = item.Merge.Root.name + "_SkinnedMesh";
            transaction.AddSubAsset(includeOnlyMesh);
            renderer.sharedMesh = includeOnlyMesh;
            string figureName = item.ShapeKey;
            if (item.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                if (!database.Registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic) || baseFigure == null)
                    throw new InvalidOperationException("PBMFollowBaseFigureInvalid: " + baseDiagnostic);
                figureName = baseFigure.Name;
            }
            GameObject prefab = item.Merge.Root;
            prefab.name = outfit.Identity + "_" + item.PbmAxisName + "_" + figureName;
            prefab.transform.SetParent(intermediate, false);
            Mesh mesh = renderer.sharedMesh;
            mesh.name = prefab.name + "_SkinnedMesh";
            mesh.RecalculateBounds();
            // PBM follow is a geometry/submesh relation.  Material payload is not
            // part of PBM authoring and must never reject registration.  Reuse a
            // Database-owned canonical slot when one is already available; absent
            // slots remain null and do not invalidate the PBM relation.
            renderer.sharedMaterials = canonicalMaterials;
            return prefab;
        }

        /// <summary>Resolves already-owned Outfit Base canonical Material slots when
        /// available.  PBM registration remains valid when classifications or their
        /// canonical Material payload are absent; missing slots are represented by
        /// null rather than causing an authoring rejection.</summary>
        private static Material[] ResolveBaseCanonicalMaterials(string databaseAssetPath,
            ShapeSyncDatabaseRegistry.OutfitEntry outfit, ShapeSyncDatabaseTransaction.EditContext transaction,
            int slotCount)
        {
            var materials = new Material[Math.Max(0, slotCount)];
            if (slotCount <= 0) return materials;
            ShapeSyncDatabase persistentDatabase = string.IsNullOrWhiteSpace(databaseAssetPath)
                ? null : AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(databaseAssetPath);
            ShapeSyncDatabaseRegistry.OutfitEntry persistentOutfit = persistentDatabase?.Registry?.Outfits
                .FirstOrDefault(entry => entry != null && entry.Identity == outfit?.Identity && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = persistentOutfit?.AxisFigures
                .SingleOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            string[] logicalNames = (persistentOutfit?.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                .Select(entry => entry?.LogicalName).Where(name => !string.IsNullOrWhiteSpace(name)).Take(slotCount).ToArray();
            Dictionary<string, Material> transactionCanonicalMaterials = (outfit?.MaterialEntries
                ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.LogicalName))
                .GroupBy(entry => entry.LogicalName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Material, StringComparer.Ordinal);
            Material[] ownedMaterials = AssetDatabase.LoadAllAssetsAtPath(databaseAssetPath).OfType<Material>().ToArray();
            string baseArtifactPrefix = baseAxis?.OutfitPrefab == null ? null : baseAxis.OutfitPrefab.name + "_";
            for (int index = 0; index < logicalNames.Length; index++)
            {
                string logicalName = logicalNames[index];
                if (transactionCanonicalMaterials.TryGetValue(logicalName, out Material registered) && registered != null)
                {
                    string registeredPath = AssetDatabase.GetAssetPath(registered);
                    // Prefab-edit transaction objects have no AssetDatabase path until
                    // commit; they are nevertheless owned by this Database transaction.
                    if (string.IsNullOrEmpty(registeredPath)
                        || string.Equals(registeredPath, databaseAssetPath, StringComparison.Ordinal))
                    {
                        materials[index] = registered;
                        continue;
                    }
                }
                Material exact = ownedMaterials.FirstOrDefault(material => material != null
                    && baseArtifactPrefix != null && material.name == baseArtifactPrefix + logicalName + "_Material");
                if (exact != null)
                {
                    materials[index] = exact;
                    continue;
                }
                Material fallback = ownedMaterials.FirstOrDefault(material => material != null
                    && material.name.EndsWith("_" + logicalName + "_Material", StringComparison.Ordinal));
                if (fallback != null)
                {
                    var canonical = new Material(fallback)
                    {
                        name = (baseArtifactPrefix ?? (outfit?.Identity + "_Base_")) + logicalName + "_Material"
                    };
                    transaction.AddSubAsset(canonical);
                    materials[index] = canonical;
                }
            }
            return materials;
        }

        private static void RemoveOldPrefab(GameObject prefab, string databaseAssetPath, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            if (prefab == null) return;
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh != null && AssetDatabase.GetAssetPath(renderer.sharedMesh) == databaseAssetPath) transaction.RemoveSubAsset(renderer.sharedMesh);
                foreach (Material material in renderer.sharedMaterials ?? Array.Empty<Material>())
                {
                    if (material == null || AssetDatabase.GetAssetPath(material) != databaseAssetPath) continue;
                    materials.Add(material);
                    foreach (string propertyName in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(propertyName);
                        if (texture != null && AssetDatabase.GetAssetPath(texture) == databaseAssetPath) textures.Add(texture);
                    }
                }
            }
            foreach (Material material in materials) transaction.RemoveSubAsset(material);
            foreach (Texture texture in textures) transaction.RemoveSubAsset(texture);
            // The direct child is a persistent object inside the Database Prefab.  Detach it
            // from the asset before destruction; plain DestroyImmediate may otherwise leave
            // the hierarchy object behind while only clearing the Registry relation.
            transaction.RemoveSubAsset(prefab);
        }

        private static GameObject ResolveDirectIntermediateChild(ShapeSyncDatabase database, GameObject candidate)
        {
            if (database == null || candidate == null) return null;
            Transform intermediate = database.transform.Find("Intermediate");
            if (intermediate == null) return null;
            return intermediate.Cast<Transform>().FirstOrDefault(child => child.name == candidate.name)?.gameObject;
        }
    }
}
#endif
