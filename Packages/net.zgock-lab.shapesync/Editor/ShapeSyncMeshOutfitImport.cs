// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Stages the Base shape-key artifacts for a Mesh Outfit without changing its source asset.</summary>
    internal static class ShapeSyncMeshOutfitImport
    {
        internal static bool TryValidateAxisSource(GameObject sourcePrefab, out string diagnostic)
        {
            return TryAdmitOutfitSource(sourcePrefab, out _, out diagnostic);
        }

        /// <summary>
        /// Resolves the material slots required by the Mesh Outfit contract. Before
        /// classification is saved every slot is required; afterwards only Include slots
        /// are required and Exclude/Projection slots are intentionally payload-free.
        /// </summary>
        internal static bool TryGetRequiredMaterialSlots(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            SkinnedMeshRenderer renderer, string shapeKey, out bool[] requiredSlots, out string diagnostic)
        {
            requiredSlots = null;
            diagnostic = null;
            Material[] sourceMaterials = renderer?.sharedMaterials;
            Mesh sourceMesh = renderer?.sharedMesh;
            if (sourceMaterials == null || sourceMaterials.Length == 0 || sourceMesh == null)
            {
                diagnostic = "Mesh Outfit material slot data is missing: shapeKey=" + shapeKey;
                return false;
            }
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications = outfit?.MaterialClassifications;
            if (classifications == null || classifications.Count == 0)
            {
                requiredSlots = new bool[sourceMaterials.Length];
                for (int index = 0; index < requiredSlots.Length; index++) requiredSlots[index] = true;
                return true;
            }
            if (classifications.Count != sourceMaterials.Length || classifications.Count != sourceMesh.subMeshCount)
            {
                diagnostic = "Mesh Outfit Material classification mismatch: shapeKey=" + shapeKey
                    + "; classifications=" + classifications.Count
                    + "; sourceSlots=" + sourceMaterials.Length
                    + "; sourceSubMeshes=" + sourceMesh.subMeshCount;
                return false;
            }
            requiredSlots = classifications
                .Select(entry => entry != null && entry.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include)
                .ToArray();
            return true;
        }

        /// <summary>Resolves the PBM-follow geometry selection from SubMesh order only.
        /// PBM Sources are geometry-only and therefore must not require a renderer
        /// Material array or inspect Material identity/content.</summary>
        internal static bool TryGetRequiredSubMeshSelection(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            SkinnedMeshRenderer renderer, string shapeKey, out bool[] selectedSubMeshes, out string diagnostic)
        {
            selectedSubMeshes = null;
            diagnostic = null;
            Mesh sourceMesh = renderer?.sharedMesh;
            if (sourceMesh == null)
            {
                diagnostic = "PBMFollowSourceInvalid: source Mesh is missing; shapeKey=" + shapeKey;
                return false;
            }
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications = outfit?.MaterialClassifications;
            if (classifications == null || classifications.Count == 0)
            {
                selectedSubMeshes = Enumerable.Repeat(true, sourceMesh.subMeshCount).ToArray();
                return true;
            }
            if (classifications.Count != sourceMesh.subMeshCount)
            {
                diagnostic = "PBMFollowSourceClassificationMismatch: shapeKey=" + shapeKey
                    + "; classifications=" + classifications.Count
                    + "; sourceSubMeshes=" + sourceMesh.subMeshCount;
                return false;
            }
            selectedSubMeshes = classifications
                .Select(entry => entry != null && entry.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include)
                .ToArray();
            return true;
        }

        private static bool HasMissingIncludedMaterial(Material[] materials,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications)
        {
            if (materials == null || classifications == null) return true;
            int count = Math.Min(materials.Length, classifications.Count);
            for (int index = 0; index < count; index++)
                if (classifications[index] != null
                    && classifications[index].Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include
                    && materials[index] == null)
                    return true;
            return materials.Length != classifications.Count;
        }

        internal static bool TryApplyMaterialClassifications(string databaseAssetPath, string outfitIdentity,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath) || string.IsNullOrWhiteSpace(outfitIdentity))
            {
                diagnostic = "Mesh Outfit Material classification Save requires a Database path and Outfit identity.";
                return false;
            }
            if (classifications == null || classifications.Count == 0)
            {
                diagnostic = "Mesh Outfit Material classification Save requires classification entries.";
                return false;
            }
            bool committed = ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
            {
                if (database.Registry.Outfits == null)
                    throw new InvalidOperationException("Mesh Outfit registry collection is missing.");
                ShapeSyncDatabaseRegistry.OutfitEntry existingOutfit = database.Registry.Outfits
                    .FirstOrDefault(entry => entry != null && entry.Identity == outfitIdentity);
                if (existingOutfit == null)
                    throw new InvalidOperationException("Mesh Outfit was not found: " + outfitIdentity);
                if (existingOutfit.MaterialClassifications != null && existingOutfit.MaterialClassifications.Count != 0)
                    throw new InvalidOperationException("Mesh Outfit material classification is fixed after Save. Remove and recreate the Outfit to reclassify materials.");
                // Validate every axis before changing the Registry or deleting any
                // import-time artifacts.  A failed later axis must not leave an
                // unclassified Outfit with its Base Merged prefab already removed.
                if (existingOutfit.AxisFigures == null || existingOutfit.AxisFigures.Count == 0)
                    throw new InvalidOperationException("Mesh Outfit axis collection is missing: " + outfitIdentity);
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in existingOutfit.AxisFigures.Where(entry => entry != null))
                    ValidateClassificationAxisInputs(existingOutfit, axis);
                if (!database.Registry.TrySetOutfitMaterialClassifications(outfitIdentity, classifications, out string classificationDiagnostic))
                    throw new InvalidOperationException(classificationDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.FirstOrDefault(entry => entry != null && entry.Identity == outfitIdentity);
                if (outfit == null)
                    throw new InvalidOperationException("Mesh Outfit was not found after classification admission: " + outfitIdentity);
                if (outfit.AxisFigures == null)
                    throw new InvalidOperationException("Mesh Outfit axis collection is missing: " + outfitIdentity);
                foreach (Texture oldTexture in database.Registry.RemoveIncludedTextureResourcesOwnedByOutfit(outfitIdentity))
                {
                    transaction.RemoveSubAsset(oldTexture);
                }
                var outfitMaterialEntries = new List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>();
                var adapterCopies = ShapeSyncMaterialAdapterResolver.CreateDatabaseAdapterCache(database.Registry);
                // The Base axis is the sole Outfit Material/Texture owner.  Process it
                // first so every FBM artifact can bind the canonical entries created by
                // that axis instead of importing an axis-local dependency set.
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures
                    .Where(entry => entry != null)
                    .OrderBy(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey ? 0 : 1))
                {
                    RebindAxisArtifactsToIntermediate(intermediate, axis);
                    ApplyMaterialClassificationToAxis(database.Registry, transaction, databaseAssetPath, intermediate,
                        outfitIdentity, outfit, axis, outfitMaterialEntries, adapterCopies);
                }
                outfit.SetMaterialEntries(outfitMaterialEntries);
                ShapeSyncMaterialAdapterResolver.CanonicalizeDatabaseAdapters(database, transaction, databaseAssetPath, adapterCopies);
            }, out diagnostic);
            if (!committed) return false;
            return true;
        }

        private static void ValidateClassificationAxisInputs(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis)
        {
            if (axis == null)
                throw new InvalidOperationException("Mesh Outfit axis entry is missing.");
            if (axis.SourcePrefab == null || axis.MergedPrefab == null)
                throw new InvalidOperationException("Mesh Outfit axis import artifacts are missing before classification Save: " + axis.ShapeKey);
            Material[] sourceMaterials = RequireRenderer(axis.SourcePrefab, axis.ShapeKey).sharedMaterials;
            Material[] mergedMaterials = RequireRenderer(axis.MergedPrefab, axis.ShapeKey).sharedMaterials;
            SkinnedMeshRenderer mergedRenderer = RequireRenderer(axis.MergedPrefab, axis.ShapeKey);
            IReadOnlyList<string> baseMaterialNames = outfit.AxisFigures
                .FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)?.SourceMaterialNames;
            if (sourceMaterials == null || mergedMaterials == null || mergedRenderer.sharedMesh == null || baseMaterialNames == null)
                throw new InvalidOperationException("Mesh Outfit axis material or merged Mesh data is missing before classification Save: " + axis.ShapeKey);
            if (sourceMaterials.Length != baseMaterialNames.Count || mergedMaterials.Length != mergedRenderer.sharedMesh.subMeshCount)
                throw new InvalidOperationException("Mesh Outfit source Material slots or merged submesh data are internally inconsistent before classification Save: " + axis.ShapeKey);
        }

        private static void ApplyMaterialClassificationToAxis(ShapeSyncDatabaseRegistry registry,
            ShapeSyncDatabaseTransaction.EditContext transaction, string databaseAssetPath, Transform intermediate,
            string outfitIdentity, ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis,
            List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> outfitMaterialEntries,
            Dictionary<Type, MaterialShaderAdapter> adapterCopies)
        {
            if (axis == null)
                throw new InvalidOperationException("Mesh Outfit axis entry is missing.");
            if (axis.SourcePrefab == null || axis.MergedPrefab == null)
                throw new InvalidOperationException("Mesh Outfit axis is incomplete: " + axis.ShapeKey);
            Material[] sourceMaterials = RequireRenderer(axis.SourcePrefab, axis.ShapeKey).sharedMaterials;
            Material[] mergedMaterials = RequireRenderer(axis.MergedPrefab, axis.ShapeKey).sharedMaterials;
            SkinnedMeshRenderer mergedRenderer = RequireRenderer(axis.MergedPrefab, axis.ShapeKey);
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures
                .FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            IReadOnlyList<string> baseMaterialNames = baseAxis?.SourceMaterialNames;
            if (baseMaterialNames == null || sourceMaterials == null || mergedMaterials == null || mergedRenderer.sharedMesh == null)
                throw new InvalidOperationException("Mesh Outfit axis material or merged Mesh data is missing: " + axis.ShapeKey);
            if (sourceMaterials.Length != baseMaterialNames.Count || mergedMaterials.Length != mergedRenderer.sharedMesh.subMeshCount)
                throw new InvalidOperationException("Mesh Outfit source Material slots or merged submesh data are internally inconsistent: " + axis.ShapeKey);
            if (outfit.MaterialClassifications == null)
                throw new InvalidOperationException("Mesh Outfit Material classification collection is missing: " + axis.ShapeKey);
            var classificationByBaseName = outfit.MaterialClassifications
                .Where(entry => entry != null)
                .ToDictionary(entry => entry.SourceMaterialName, StringComparer.Ordinal);
            if (baseMaterialNames == null
                || sourceMaterials.Length != baseMaterialNames.Count
                || !new HashSet<string>(baseMaterialNames, StringComparer.Ordinal).SetEquals(classificationByBaseName.Keys)
                || HasMissingIncludedMaterial(sourceMaterials, outfit.MaterialClassifications))
                throw new InvalidOperationException("Mesh Outfit Material classifications must match the Base material set and merged source submesh set: " + axis.ShapeKey);
            var classificationsBySubmesh = new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry[sourceMaterials.Length];
            for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                // All axis merges must preserve the Base submesh topology. The
                // classification table is keyed by the Base source slot and is
                // intentionally applied by that stable submesh index. A source
                // with an inserted/removed submesh is rejected above rather than
                // guessed by material names or generated logical names.
                classificationsBySubmesh[materialIndex] = classificationByBaseName[baseMaterialNames[materialIndex]];
            }
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxisForTopology = outfit.AxisFigures
                .FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer baseOutfitRenderer = null;
            Transform canonicalFigureRoot = null;
            if (axis.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                ShapeSyncDatabaseRegistry.BaseFigureEntry canonicalBaseFigure;
                string canonicalBaseFigureDiagnostic;
                if (registry.TryGetSingleBaseFigure(out canonicalBaseFigure, out canonicalBaseFigureDiagnostic)
                    && canonicalBaseFigure != null && canonicalBaseFigure.Figure != null)
                    canonicalFigureRoot = canonicalBaseFigure.Figure.transform;
            }
            if (axis.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                if (baseAxisForTopology == null || baseAxisForTopology.OutfitPrefab == null)
                    throw new InvalidOperationException(StackMachineDiagnostic.CreateDomain("outfit-topology", "OutfitTopologyBaseArtifactMissing",
                        "The Base Outfit artifact is required before an FBM Outfit axis can be classified.",
                        bindingName: outfitIdentity + "/" + axis.ShapeKey,
                        detail: "renderer=<Base>").ToString());
                baseOutfitRenderer = RequireRenderer(baseAxisForTopology.OutfitPrefab, ShapeSyncDatabaseRegistry.BaseShapeKey);
                if (baseOutfitRenderer.sharedMesh == null)
                    throw new InvalidOperationException(StackMachineDiagnostic.CreateDomain("outfit-topology", "OutfitTopologyBaseMeshMissing",
                        "The Base Outfit artifact does not contain a mesh for FBM topology normalization.",
                    bindingName: outfitIdentity + "/" + axis.ShapeKey,
                        detail: "renderer=<Base>").ToString());
            }
            GameObject included = ReplaceDerivedPrefab(axis, intermediate, transaction, outfitIdentity, baseOutfitRenderer,
                baseAxisForTopology?.OutfitPrefab?.transform, canonicalFigureRoot, mergedMaterials, sourceMaterials, classificationsBySubmesh,
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, false);
            if (axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                StageIncludedMaterials(registry, transaction, outfitIdentity, axis.ShapeKey,
                    ResolveArtifactShapeName(registry, axis.ShapeKey), included, sourceMaterials, classificationsBySubmesh,
                    outfitMaterialEntries, true, adapterCopies);
            }
            else
            {
                BindCanonicalIncludedMaterials(axis.ShapeKey, included, classificationsBySubmesh, outfitMaterialEntries);
            }
            bool hasProjection = outfit.MaterialClassifications.Any(entry => entry.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection);
            if (hasProjection) ReplaceDerivedPrefab(axis, intermediate, transaction, outfitIdentity, baseOutfitRenderer,
                baseAxisForTopology?.OutfitPrefab?.transform, canonicalFigureRoot, mergedMaterials, sourceMaterials, classificationsBySubmesh,
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection, true);
            else if (axis.ProjectionPrefab != null) UnityEngine.Object.DestroyImmediate(axis.ProjectionPrefab, true);
            RemoveMergedArtifact(axis, transaction, databaseAssetPath);
                RemoveSourceMaterialPayload(axis, transaction, databaseAssetPath);
            if (axis.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey)
                axis.ClearSourceMaterialNames();
        }

        internal static bool TryImportBase(string databaseAssetPath, string outfitIdentity, GameObject sourcePrefab, out string diagnostic)
        {
            return TryImportAxis(databaseAssetPath, outfitIdentity, ShapeSyncDatabaseRegistry.BaseShapeKey, sourcePrefab, out diagnostic);
        }

        /// <summary>Imports a complete changed FBM batch with all-or-nothing Database persistence.</summary>
        internal static bool TryImportAxes(string databaseAssetPath, string outfitIdentity,
            IReadOnlyList<KeyValuePair<string, GameObject>> sources, out string diagnostic)
        {
            diagnostic = null;
            if (sources == null || sources.Count == 0) { diagnostic = "Mesh Outfit FBM import requires at least one source."; return false; }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, GameObject> source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.Key) || !keys.Add(source.Key)) { diagnostic = "Mesh Outfit FBM import contains duplicate or empty shape keys."; return false; }
                if (!TryValidateAxisSource(source.Value, out diagnostic)) return false;
            }
            string snapshotPath = AssetDatabase.GenerateUniqueAssetPath(databaseAssetPath + ".fbm-import.snapshot");
            try
            {
                File.Copy(databaseAssetPath, snapshotPath, false);
                foreach (KeyValuePair<string, GameObject> source in sources)
                    if (!TryImportAxis(databaseAssetPath, outfitIdentity, source.Key, source.Value, out diagnostic))
                    {
                        File.Copy(snapshotPath, databaseAssetPath, true);
                        AssetDatabase.ImportAsset(databaseAssetPath, ImportAssetOptions.ForceUpdate);
                        return false;
                    }
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Mesh Outfit FBM batch import failed: " + exception.Message;
                try { if (File.Exists(snapshotPath)) { File.Copy(snapshotPath, databaseAssetPath, true); AssetDatabase.ImportAsset(databaseAssetPath, ImportAssetOptions.ForceUpdate); } } catch { }
                return false;
            }
            finally
            {
                try { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); } catch { }
            }
        }

        /// <summary>Imports one Base or registered FBM source.  Each shape key is staged explicitly; no source is inferred.</summary>
        internal static bool TryImportAxis(string databaseAssetPath, string outfitIdentity, string shapeKey, GameObject sourcePrefab, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(outfitIdentity) || string.IsNullOrWhiteSpace(shapeKey) || sourcePrefab == null)
            { diagnostic = "Mesh Outfit import requires an Outfit Id, shape key, and source Prefab."; return false; }
            // A Mesh Outfit is geometry that is later parented to the generated Figure.
            // It deliberately does not own, validate, clone, or strip an Animator/Avatar:
            // those are Figure-owned data.  Reusing Figure import admission here would make
            // an unrelated Humanoid Avatar a false prerequisite for Outfit authoring.
            if (!TryAdmitOutfitSource(sourcePrefab, out ShapeSyncFigureImportAdmission admission, out diagnostic)) return false;

            ShapeSyncFigureMeshMerger.Result sourceMerge = null;
            ShapeSyncFigureMeshMerger.Result outfitMerge = null;
            ShapeSyncFigureImport.DatabaseMaterialCopies sourceMaterials = null;
            ShapeSyncFigureImport.DatabaseMaterialCopies outfitMaterials = null;
            try
            {
                if (!ShapeSyncFigureMeshMerger.TryMergeOwned(admission.HumanoidRoot, admission.SourceRenderers, out sourceMerge, out diagnostic)) return false;
                if (!ShapeSyncFigureMeshMerger.TryMergeOwned(admission.HumanoidRoot, admission.SourceRenderers, out outfitMerge, out diagnostic)) return false;
                PreserveOutfitBoneHierarchy(admission.HumanoidRoot.transform, sourceMerge.Root.transform);
                PreserveOutfitBoneHierarchy(admission.HumanoidRoot.transform, outfitMerge.Root.transform);
                string[] sourceMaterialNames = sourceMerge.Renderer.sharedMaterials.Select(material => material == null ? null : material.name).ToArray();
                if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase database, out diagnostic)
                    || database == null || database.Registry == null)
                {
                    if (string.IsNullOrWhiteSpace(diagnostic)) diagnostic = "Mesh Outfit import requires a valid Database.";
                    return false;
                }
                string artifactShapeName = ResolveArtifactShapeName(database.Registry, shapeKey);
                string sourcePrefix = outfitIdentity + "_" + shapeKey;
                ShapeSyncDatabaseRegistry.OutfitEntry existingOutfit = database.Registry.Outfits
                    .FirstOrDefault(entry => entry != null && entry.Identity == outfitIdentity && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
                if (existingOutfit == null)
                {
                    diagnostic = "Mesh Outfit was not found: " + outfitIdentity;
                    return false;
                }
                // MaterialEntries are persistent canonical sub-asset references. Capture
                // them before loading Prefab contents so FBM overwrite can rebind to the
                // Base entries even when the transaction's temporary registry view has
                // not yet rehydrated those references.
                List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> canonicalMaterialEntries =
                    ResolveCanonicalMaterialEntries(databaseAssetPath, existingOutfit);
                if (existingOutfit.MaterialClassifications != null
                    && existingOutfit.MaterialClassifications.Count == sourceMaterialNames.Length)
                {
                    // Excluded/Projection payloads are intentionally absent after
                    // classification. Keep their recorded logical source identities so
                    // axis replacement can rebuild the Registry without reintroducing
                    // the excluded Material dependency.
                    sourceMaterialNames = sourceMaterialNames
                        .Select((name, index) => name ?? existingOutfit.MaterialClassifications[index]?.SourceMaterialName)
                        .ToArray();
                }
                if (!TryGetRequiredMaterialSlots(existingOutfit, sourceMerge.Renderer, shapeKey, out bool[] sourceRequiredSlots, out diagnostic)) return false;
                if (!TryGetRequiredMaterialSlots(existingOutfit, outfitMerge.Renderer, shapeKey, out bool[] outfitRequiredSlots, out diagnostic)) return false;
                var sourceRequiredSlotSet = new HashSet<int>();
                for (int index = 0; index < sourceRequiredSlots.Length; index++) if (sourceRequiredSlots[index]) sourceRequiredSlotSet.Add(index);
                var outfitRequiredSlotSet = new HashSet<int>();
                for (int index = 0; index < outfitRequiredSlots.Length; index++) if (outfitRequiredSlots[index]) outfitRequiredSlotSet.Add(index);
                if (!ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreateForRequiredSlots(sourcePrefix + "_Source", sourceMerge.Renderer.sharedMaterials,
                    sourceRequiredSlotSet, out sourceMaterials, out diagnostic)) return false;
                if (!ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreateForRequiredSlots(sourcePrefix + "_Merged", outfitMerge.Renderer.sharedMaterials,
                    outfitRequiredSlotSet, out outfitMaterials, out diagnostic)) return false;

                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    if (database.Registry == null || !database.Registry.Outfits.Any(entry => entry != null && entry.Identity == outfitIdentity && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh))
                        throw new InvalidOperationException("Mesh Outfit was not found: " + outfitIdentity);
                    ShapeSyncDatabaseRegistry.OutfitEntry outfitEntry = database.Registry.Outfits
                        .Single(entry => entry != null && entry.Identity == outfitIdentity);
                    string prefix = outfitIdentity + "_" + artifactShapeName;
                    ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry previousAxis = outfitEntry.AxisFigures
                        .FirstOrDefault(entry => entry != null && entry.ShapeKey == shapeKey);
                    if (previousAxis != null)
                    {
                        foreach (Texture resource in database.Registry.RemoveIncludedTextureResourcesOwnedByOutfit(outfitIdentity, shapeKey))
                            transaction.RemoveSubAsset(resource);
                        RemoveAxisArtifacts(previousAxis, transaction, databaseAssetPath);
                    }

                    Stage(database, intermediate, transaction, admission, sourceMerge, sourceMaterials, prefix + "_Source");
                    Stage(database, intermediate, transaction, admission, outfitMerge, outfitMaterials, prefix + "_Merged");
                    GameObject outfit = UnityEngine.Object.Instantiate(outfitMerge.Root);
                    outfit.name = prefix;
                    outfit.transform.SetParent(intermediate, false);
                    var axis = new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(shapeKey, sourceMerge.Root, outfitMerge.Root, outfit, null, sourceMaterialNames);
                    List<ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry> allAxes = outfitEntry.AxisFigures
                        .Where(entry => entry != null && entry.ShapeKey != shapeKey).ToList();
                    allAxes.Add(axis);
                    if (!database.Registry.TrySetOutfitAxisFigures(database, outfitIdentity, allAxes, out string registryDiagnostic))
                        throw new InvalidOperationException(registryDiagnostic);
                    ShapeSyncDatabaseRegistry.OutfitEntry updatedOutfitEntry = database.Registry.Outfits
                        .Single(entry => entry != null && entry.Identity == outfitIdentity);
                    if (updatedOutfitEntry.MaterialClassifications.Count != 0)
                    {
                        if (shapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
                            throw new InvalidOperationException("Mesh Outfit Base source is fixed after Material classification Save. Remove and recreate the Outfit to import Base again.");
                        ApplyMaterialClassificationToAxis(database.Registry, transaction, databaseAssetPath, intermediate,
                            outfitIdentity, updatedOutfitEntry, axis,
                            canonicalMaterialEntries,
                            ShapeSyncMaterialAdapterResolver.CreateDatabaseAdapterCache(database.Registry));
                    }
                    RemoveStaleArtifactsInTransaction(databaseAssetPath, database, intermediate, transaction, outfitIdentity);
                }, out diagnostic)) return false;

                sourceMerge.DetachMesh();
                outfitMerge.DetachMesh();
                sourceMaterials.Detach();
                outfitMaterials.Detach();
                return true;
            }
            finally
            {
                sourceMaterials?.Dispose();
                outfitMaterials?.Dispose();
                sourceMerge?.Dispose();
                outfitMerge?.Dispose();
            }
        }

        private static string ResolveArtifactShapeName(ShapeSyncDatabaseRegistry registry, string shapeKey)
        {
            if (string.Equals(shapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal))
            {
                if (!registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic))
                    throw new InvalidOperationException("Mesh Outfit import Base Figure diagnostic: " + baseDiagnostic);
                if (baseFigure == null || string.IsNullOrWhiteSpace(baseFigure.Name))
                    throw new InvalidOperationException("Mesh Outfit import requires exactly one saved Base Figure.");
                return baseFigure.Name;
            }
            if (!registry.FigureAxes.Any(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && axis.Name == shapeKey))
                throw new InvalidOperationException("Mesh Outfit source shape key must be Base or a registered FBM: " + shapeKey);
            return shapeKey;
        }

        private static List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> ResolveCanonicalMaterialEntries(
            string databaseAssetPath, ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> persisted = (outfit?.MaterialEntries
                ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                .Where(entry => entry != null && entry.Material != null)
                .ToList();
            if (persisted.Count != 0) return persisted;

            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit?.AxisFigures
                ?.FirstOrDefault(axis => axis != null && axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer baseRenderer = baseAxis?.OutfitPrefab?.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Material[] baseMaterials = baseRenderer?.sharedMaterials ?? Array.Empty<Material>();
            string[] logicalNames = (outfit?.MaterialClassifications
                ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry>())
                .Where(entry => entry != null && entry.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include)
                .Select(entry => entry.EntryName)
                .ToArray();
            if (logicalNames.Length == 0 || baseMaterials.Length != logicalNames.Length || baseMaterials.Any(material => material == null))
            {
                Material[] ownedMaterials = AssetDatabase.LoadAllAssetsAtPath(databaseAssetPath).OfType<Material>().ToArray();
                string basePrefix = baseAxis?.OutfitPrefab == null ? null : baseAxis.OutfitPrefab.name + "_";
                Material[] namedMaterials = logicalNames.Select(name => ownedMaterials.FirstOrDefault(material => material != null
                    && ((basePrefix != null && material.name == basePrefix + name + "_Material")
                        || material.name.EndsWith("_" + name + "_Material", StringComparison.Ordinal)))).ToArray();
                if (namedMaterials.Any(material => material == null)) return new List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>();
                baseMaterials = namedMaterials;
            }
            return logicalNames.Select((name, index) =>
                new ShapeSyncDatabaseRegistry.OutfitMaterialEntry(name, baseMaterials[index], null)).ToList();
        }

        private static GameObject ReplaceDerivedPrefab(ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis, Transform intermediate,
            ShapeSyncDatabaseTransaction.EditContext transaction, string outfitIdentity, SkinnedMeshRenderer baseOutfitRenderer,
            Transform baseOutfitRoot, Transform canonicalFigureRoot,
            Material[] mergedMaterials, Material[] sourceMaterials,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications,
            ShapeSyncDatabaseRegistry.OutfitMaterialClassification targetClassification, bool isProjection)
        {
            GameObject previous = isProjection ? axis.ProjectionPrefab : axis.OutfitPrefab;
            if (previous != null)
            {
                foreach (Material material in RequireRenderer(previous, axis.ShapeKey).sharedMaterials.Where(material => IsDerivedMaterialCopy(material, outfitIdentity)))
                    transaction.RemoveSubAsset(material);
                UnityEngine.Object.DestroyImmediate(previous, true);
            }
            GameObject derived = null;
            Mesh meshCopy = null;
            bool meshAttached = false;
            try
            {
                derived = UnityEngine.Object.Instantiate(axis.MergedPrefab);
                derived.name = isProjection ? axis.MergedPrefab.name.Replace("_Merged", "_Projection") : axis.MergedPrefab.name.Replace("_Merged", string.Empty);
                derived.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = RequireRenderer(derived, axis.ShapeKey);
                // Derived artifacts survive removal of the import-time Merged Prefab.
                // Give each one its own Database-owned Mesh rather than retaining its sharedMesh.
                bool[] selectedSubMeshes = Enumerable.Range(0, classifications.Count)
                    .Select(index => classifications[index] != null && classifications[index].Classification == targetClassification)
                    .ToArray();
                meshCopy = BuildSelectedMesh(renderer.sharedMesh, selectedSubMeshes);
                // Topology normalization must compare the Base artifact with the
                // same material-selected geometry that will be persisted.  The
                // imported Merged Prefab may contain Face/Body submeshes in
                // addition to the Outfit submeshes and would otherwise produce a
                // false vertex-count rejection before the selection is applied.
                renderer.sharedMesh = meshCopy;
                if (!isProjection && axis.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey)
                {
                    string bindingName = outfitIdentity + "/" + axis.ShapeKey;
                    string rendererPath = RelativePath(derived.transform, renderer.transform);
                    if (!ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseOutfitRenderer, renderer, bindingName, rendererPath,
                        baseOutfitRoot, derived.transform, canonicalFigureRoot,
                        out _, out StackMachineDiagnostic topologyDiagnostic))
                        throw new InvalidOperationException(topologyDiagnostic?.ToString() ?? "Outfit topology normalization failed without a diagnostic.");
                }
                meshCopy.name = derived.name + "_SkinnedMesh";
                meshCopy.RecalculateBounds();
                transaction.AddSubAsset(meshCopy);
                meshAttached = true;
                renderer.sharedMesh = meshCopy;
                // The Mesh is added as a Database sub-asset before the derived
                // Prefab is serialized.  Explicitly dirty both sides of the
                // reference so Unity does not serialize the renderer with a null
                // mesh while retaining the standalone Mesh sub-asset.
                EditorUtility.SetDirty(meshCopy);
                EditorUtility.SetDirty(renderer);
                EditorUtility.SetDirty(derived);
                var derivedMaterialList = new List<Material>();
                for (int materialIndex = 0; materialIndex < classifications.Count; materialIndex++)
                {
                    ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry classification = classifications[materialIndex];
                    if (classification != null && classification.Classification == targetClassification)
                        derivedMaterialList.Add(mergedMaterials[materialIndex]);
                }
                Material[] derivedMaterials = derivedMaterialList.ToArray();
                if (isProjection) Array.Clear(derivedMaterials, 0, derivedMaterials.Length);
                renderer.sharedMaterials = derivedMaterials;
                EditorUtility.SetDirty(renderer);
                if (isProjection) axis.ReplaceDerivedPrefabs(axis.OutfitPrefab, derived);
                else axis.ReplaceDerivedPrefabs(derived, axis.ProjectionPrefab);
                return derived;
            }
            catch
            {
                if (meshCopy != null)
                {
                    if (meshAttached) transaction.RemoveSubAsset(meshCopy);
                    else UnityEngine.Object.DestroyImmediate(meshCopy, true);
                }
                if (derived != null) UnityEngine.Object.DestroyImmediate(derived, true);
                throw;
            }
        }

        internal static Mesh BuildSelectedMesh(Mesh source, IReadOnlyList<bool> selected)
        {
            if (source == null) throw new InvalidOperationException("Source mesh is null.");
            ShapeSyncMeshBoneWeights boneWeights = ShapeSyncMeshBoneWeights.Capture(source);
            int[] remap = Enumerable.Repeat(-1, source.vertexCount).ToArray();
            var triangles = new List<int[]>();
            int nextVertex = 0;
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                if (subMesh >= selected.Count || !selected[subMesh]) continue;
                int[] sourceTriangles = source.GetTriangles(subMesh);
                int[] mapped = new int[sourceTriangles.Length];
                for (int index = 0; index < sourceTriangles.Length; index++)
                {
                    int sourceIndex = sourceTriangles[index];
                    if (sourceIndex < 0 || sourceIndex >= remap.Length)
                        throw new InvalidOperationException("Selected SubMesh contains an invalid vertex index: " + subMesh);
                    if (remap[sourceIndex] < 0) remap[sourceIndex] = nextVertex++;
                    mapped[index] = remap[sourceIndex];
                }
                triangles.Add(mapped);
            }
            var result = new Mesh
            {
                name = source.name,
                indexFormat = nextVertex > ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            result.vertices = Remap(source.vertices, remap, nextVertex);
            if (source.normals.Length == source.vertexCount) result.normals = Remap(source.normals, remap, nextVertex);
            if (source.tangents.Length == source.vertexCount) result.tangents = Remap(source.tangents, remap, nextVertex);
            if (source.colors.Length == source.vertexCount) result.colors = Remap(source.colors, remap, nextVertex);
            if (boneWeights != null) boneWeights.RemapSourceToCompact(remap, nextVertex).Apply(result);
            result.bindposes = source.bindposes;
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>();
                source.GetUVs(channel, values);
                if (values.Count == source.vertexCount) result.SetUVs(channel, Remap(values.ToArray(), remap, nextVertex));
            }
            result.subMeshCount = triangles.Count;
            for (int index = 0; index < triangles.Count; index++) result.SetTriangles(triangles[index], index, false);
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                Vector3[] vertices = new Vector3[source.vertexCount];
                Vector3[] normals = new Vector3[source.vertexCount];
                Vector3[] tangents = new Vector3[source.vertexCount];
                source.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                result.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                    Remap(vertices, remap, nextVertex), Remap(normals, remap, nextVertex), Remap(tangents, remap, nextVertex));
            }
            result.RecalculateBounds();
            return result;
        }

        private static T[] Remap<T>(T[] source, int[] remap, int count)
        {
            T[] result = new T[count];
            for (int index = 0; index < remap.Length; index++) if (remap[index] >= 0) result[remap[index]] = source[index];
            return result;
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (value == null || value == root) return string.Empty;
            var names = new List<string>();
            for (Transform current = value; current != null && current != root; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void StageIncludedMaterials(ShapeSyncDatabaseRegistry registry, ShapeSyncDatabaseTransaction.EditContext transaction,
            string outfitIdentity, string shapeKey, string artifactShapeName, GameObject includedPrefab, Material[] sourceMaterials,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications,
            List<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> outfitMaterialEntries, bool registerEntries,
            Dictionary<Type, MaterialShaderAdapter> adapterCopies)
        {
            SkinnedMeshRenderer renderer = RequireRenderer(includedPrefab, shapeKey);
            Material[] copies = renderer.sharedMaterials.ToArray();
            // Texture resources aggregate at the recorded owner boundary.  The source
            // Material copies made during import already preserve source Texture
            // identity within one axis; retain that identity here instead of creating
            // one Database Texture for every Material/property occurrence.
            var textureCopies = new Dictionary<Texture, Texture>();
            int derivedIndex = 0;
            for (int sourceIndex = 0; sourceIndex < sourceMaterials.Length; sourceIndex++)
            {
                ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry classification = sourceIndex < classifications.Count ? classifications[sourceIndex] : null;
                if (classification == null) continue;
                if (classification.Classification != ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include) continue;
                if (derivedIndex >= renderer.sharedMaterials.Length || renderer.sharedMaterials[derivedIndex] == null)
                    throw new InvalidOperationException("Mesh Outfit Include Material is missing from its derived Prefab: " + sourceMaterials[sourceIndex].name);
                Material source = renderer.sharedMaterials[derivedIndex];
                Material copy = new Material(source) { name = outfitIdentity + "_" + artifactShapeName + "_" + classification.EntryName + "_Material" };
                int propertyIndex = 0;
                foreach (string propertyName in ShapeSyncEntryAssetNaming.GetTexturePropertyNamesMainTexFirst(copy))
                {
                    Texture sourceTexture = copy.GetTexture(propertyName);
                    if (sourceTexture == null) continue;
                    if (!textureCopies.TryGetValue(sourceTexture, out Texture textureCopy))
                    {
                        string logicalName = ShapeSyncEntryAssetNaming.GetTextureName(
                            outfitIdentity + "_" + artifactShapeName, classification.EntryName, propertyIndex++);
                        textureCopy = UnityEngine.Object.Instantiate(sourceTexture);
                        textureCopy.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(sourceTexture)
                            ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                            : logicalName;
                        transaction.AddSubAsset(textureCopy);
                        if (!registry.TryRegisterTextureResource(logicalName, textureCopy,
                            ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit(outfitIdentity, shapeKey),
                            ShapeSyncDatabaseRegistry.TextureResourceUsage.OutfitIncludedMaterial, out string resourceDiagnostic))
                            throw new InvalidOperationException(resourceDiagnostic);
                        textureCopies.Add(sourceTexture, textureCopy);
                    }
                    copy.SetTexture(propertyName, textureCopy);
                }
                transaction.AddSubAsset(copy);
                if (registerEntries)
                {
                    if (!ShapeSyncMaterialAdapterResolver.TryCreateFor(copy, out MaterialShaderAdapter transientAdapter, out string adapterDiagnostic))
                        throw new InvalidOperationException(adapterDiagnostic);
                    try
                    {
                        Type adapterType = transientAdapter.GetType();
                        if (!adapterCopies.TryGetValue(adapterType, out MaterialShaderAdapter adapter))
                        {
                            adapter = UnityEngine.Object.Instantiate(transientAdapter);
                            adapter.name = adapterType.Name;
                            transaction.AddSubAsset(adapter);
                            adapterCopies.Add(adapterType, adapter);
                        }
                        outfitMaterialEntries.Add(new ShapeSyncDatabaseRegistry.OutfitMaterialEntry(classification.EntryName, copy, adapter));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(transientAdapter); }
                }
                copies[derivedIndex] = copy;
                derivedIndex++;
            }
            renderer.sharedMaterials = copies;
        }

        /// <summary>Assigns the Base Outfit canonical Material entries to an FBM artifact.
        /// FBM axes contribute geometry only; they never create Material or Texture
        /// resources of their own.</summary>
        private static void BindCanonicalIncludedMaterials(string shapeKey, GameObject includedPrefab,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry> classifications,
            IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitMaterialEntry> canonicalEntries)
        {
            SkinnedMeshRenderer renderer = RequireRenderer(includedPrefab, shapeKey);
            int includedCount = (classifications ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry>())
                .Count(entry => entry != null && entry.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include);
            if (renderer.sharedMesh == null || renderer.sharedMesh.subMeshCount != includedCount)
                throw new InvalidOperationException("Mesh Outfit FBM canonical Material slots do not match the selected submeshes: " + shapeKey);
            // FBM overwrite is a geometry/submesh operation.  Rebind any canonical
            // Database-owned Material entries that are available, but do not reject
            // the axis when the canonical payload is absent or partially missing.
            // Missing slots remain null and are resolved by the later Material authoring
            // path rather than turning a valid geometry overwrite into a false reject.
            Material[] canonicalMaterials = new Material[includedCount];
            int copyCount = Math.Min(canonicalMaterials.Length, canonicalEntries?.Count ?? 0);
            for (int index = 0; index < copyCount; index++)
                canonicalMaterials[index] = canonicalEntries[index]?.Material;
            renderer.sharedMaterials = canonicalMaterials;
            EditorUtility.SetDirty(renderer);
        }

        private static SkinnedMeshRenderer RequireRenderer(GameObject prefab, string shapeKey)
        {
            SkinnedMeshRenderer renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null) throw new InvalidOperationException("Mesh Outfit axis has no SkinnedMeshRenderer: " + shapeKey);
            return renderer;
        }

        private static bool IsDerivedMaterialCopy(Material material, string outfitIdentity)
        {
            return material != null
                && material.name.StartsWith(outfitIdentity + "_", StringComparison.Ordinal)
                && material.name.EndsWith("_Material", StringComparison.Ordinal);
        }

        private static void RemoveMergedArtifact(ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis,
            ShapeSyncDatabaseTransaction.EditContext transaction, string databaseAssetPath)
        {
            GameObject merged = axis.MergedPrefab;
            if (merged == null) return;
            SkinnedMeshRenderer renderer = RequireRenderer(merged, axis.ShapeKey);
            foreach (Material material in renderer.sharedMaterials.Where(material => IsDatabaseSubAsset(material, databaseAssetPath)).Distinct())
            {
                foreach (string propertyName in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (IsDatabaseSubAsset(texture, databaseAssetPath)) transaction.RemoveSubAsset(texture);
                }
                transaction.RemoveSubAsset(material);
            }
            if (IsDatabaseSubAsset(renderer.sharedMesh, databaseAssetPath)) transaction.RemoveSubAsset(renderer.sharedMesh);
            UnityEngine.Object.DestroyImmediate(merged, true);
            axis.RemoveMergedPrefab();
        }

        private static void RebindAxisArtifactsToIntermediate(Transform intermediate,
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis)
        {
            if (intermediate == null || axis == null) return;
            axis.RebindArtifacts(
                ResolveDirectIntermediateArtifact(intermediate, axis.SourcePrefab),
                ResolveDirectIntermediateArtifact(intermediate, axis.MergedPrefab),
                ResolveDirectIntermediateArtifact(intermediate, axis.OutfitPrefab),
                ResolveDirectIntermediateArtifact(intermediate, axis.ProjectionPrefab));
        }

        private static GameObject ResolveDirectIntermediateArtifact(Transform intermediate, GameObject value)
        {
            if (intermediate == null || value == null) return value;
            if (value.transform != null && value.transform.parent == intermediate) return value;
            Transform[] candidates = intermediate.Cast<Transform>()
                .Where(child => child != null && child.name == value.name)
                .ToArray();
            Transform valid = candidates.FirstOrDefault(child =>
            {
                SkinnedMeshRenderer renderer = child.GetComponentInChildren<SkinnedMeshRenderer>(true);
                return renderer != null && renderer.sharedMesh != null;
            });
            return (valid ?? candidates.FirstOrDefault())?.gameObject ?? value;
        }

        private static void RemoveSourceMaterialPayload(ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis,
            ShapeSyncDatabaseTransaction.EditContext transaction, string databaseAssetPath)
        {
            SkinnedMeshRenderer renderer = RequireRenderer(axis.SourcePrefab, axis.ShapeKey);
            foreach (Material material in renderer.sharedMaterials.Where(material => IsDatabaseSubAsset(material, databaseAssetPath)).Distinct())
            {
                foreach (string propertyName in material.GetTexturePropertyNames())
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (IsDatabaseSubAsset(texture, databaseAssetPath)) transaction.RemoveSubAsset(texture);
                }
                transaction.RemoveSubAsset(material);
            }
            renderer.sharedMaterials = Array.Empty<Material>();
        }

        private static void RemoveAxisArtifacts(ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis,
            ShapeSyncDatabaseTransaction.EditContext transaction, string databaseAssetPath)
        {
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (GameObject artifact in new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab }.Where(item => item != null))
            {
                foreach (SkinnedMeshRenderer renderer in artifact.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh != null) meshes.Add(renderer.sharedMesh);
                    foreach (Material material in renderer.sharedMaterials.Where(material => material != null))
                    {
                        materials.Add(material);
                        foreach (string propertyName in material.GetTexturePropertyNames())
                        {
                            Texture texture = material.GetTexture(propertyName);
                            if (texture != null) textures.Add(texture);
                        }
                }
            }
            }
            foreach (Texture texture in textures.Where(texture => IsDatabaseSubAsset(texture, databaseAssetPath))) transaction.RemoveSubAsset(texture);
            foreach (Material material in materials.Where(material => IsDatabaseSubAsset(material, databaseAssetPath))) transaction.RemoveSubAsset(material);
            foreach (Mesh mesh in meshes.Where(mesh => IsDatabaseSubAsset(mesh, databaseAssetPath))) transaction.RemoveSubAsset(mesh);
            foreach (GameObject artifact in new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab }.Where(item => item != null).Distinct())
                UnityEngine.Object.DestroyImmediate(artifact, true);
        }

        /// <summary>
        /// Removes every unreferenced artifact in the explicit Outfit prefix.
        /// The caller invokes this after rebinding the replacement axis, while
        /// still inside the same Database transaction, so replacement and cleanup
        /// either commit together or roll back together.
        /// </summary>
        private static void RemoveStaleArtifactsInTransaction(string databaseAssetPath,
            ShapeSyncDatabase database, Transform intermediate,
            ShapeSyncDatabaseTransaction.EditContext transaction, string outfitIdentity)
        {
            if (database == null || database.Registry == null || intermediate == null) return;
            string artifactPrefix = outfitIdentity + "_";
            HashSet<UnityEngine.Object> protectedObjects = CollectProtectedOutfitObjects(database, intermediate, outfitIdentity);
            foreach (Transform child in intermediate.Cast<Transform>()
                .Where(value => value != null
                    && value.name.StartsWith(artifactPrefix, StringComparison.Ordinal)
                    && !protectedObjects.Contains(value.gameObject))
                .ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject, true);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(databaseAssetPath)
                .Where(asset => asset != null
                    && !(asset is GameObject)
                    && !(asset is Component)
                    && !protectedObjects.Contains(asset)
                    && asset.name.StartsWith(artifactPrefix, StringComparison.Ordinal))
                .ToArray())
                transaction.RemoveSubAsset(asset);
        }

        private static HashSet<UnityEngine.Object> CollectProtectedOutfitObjects(ShapeSyncDatabase database, Transform intermediate, string outfitIdentity)
        {
            var protectedObjects = new HashSet<UnityEngine.Object>();
            if (database == null || database.Registry == null) return protectedObjects;
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits.Where(entry => entry != null))
            {
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(entry => entry != null))
                {
                    protectedObjects.Add(axis.SourcePrefab);
                    protectedObjects.Add(axis.MergedPrefab);
                    protectedObjects.Add(axis.OutfitPrefab);
                    protectedObjects.Add(axis.ProjectionPrefab);
                    foreach (GameObject artifact in new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab }.Where(value => value != null))
                    {
                        foreach (SkinnedMeshRenderer renderer in artifact.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            protectedObjects.Add(renderer.sharedMesh);
                            foreach (Material material in renderer.sharedMaterials ?? Array.Empty<Material>())
                            {
                                protectedObjects.Add(material);
                                foreach (string propertyName in material == null ? Array.Empty<string>() : material.GetTexturePropertyNames())
                                    protectedObjects.Add(material.GetTexture(propertyName));
                            }
                        }
                    }
                }
                foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry materialEntry in outfit.MaterialEntries.Where(entry => entry != null))
                {
                    protectedObjects.Add(materialEntry.Material);
                    protectedObjects.Add(materialEntry.Adapter);
                }
                foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry collection in outfit.CollectionEntries.Where(entry => entry != null))
                {
                    GameObject collectionSource = ResolveDirectIntermediateArtifact(intermediate, collection.SourcePrefab);
                    GameObject collectionPrefab = ResolveDirectIntermediateArtifact(intermediate, collection.CollectionPrefab);
                    collection.RebindArtifacts(collectionSource, collectionPrefab);
                    foreach (GameObject artifact in new[] { collectionSource, collectionPrefab }.Where(value => value != null))
                    {
                        protectedObjects.Add(artifact);
                        foreach (SkinnedMeshRenderer renderer in artifact.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            protectedObjects.Add(renderer.sharedMesh);
                    }
                }
            }
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources
                .Where(entry => entry != null
                    && entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit
                    && string.Equals(entry.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal)))
                protectedObjects.Add(resource.Texture);
            return protectedObjects;
        }

        private static bool IsDatabaseSubAsset(UnityEngine.Object asset, string databaseAssetPath)
        {
            return asset != null && string.Equals(AssetDatabase.GetAssetPath(asset), databaseAssetPath, StringComparison.Ordinal);
        }

        private static bool TryAdmitOutfitSource(GameObject candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            admission = null;
            diagnostic = null;
            if (candidate == null || !EditorUtility.IsPersistent(candidate))
            {
                diagnostic = "Mesh Outfit import requires a persistent source Prefab.";
                return false;
            }
            SkinnedMeshRenderer[] renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                diagnostic = "Mesh Outfit import requires at least one SkinnedMeshRenderer below the source Prefab.";
                return false;
            }
            // ShapeSyncFigureMeshMerger only needs a root and renderer order.  Keep the
            // admission object common with Figure import, while explicitly leaving the
            // Animator and Avatar unset for this Figure-shared Outfit contract.
            admission = new ShapeSyncFigureImportAdmission(candidate, candidate, null, null, renderers);
            return true;
        }

        /// <summary>
        /// Mesh merge only retains transforms needed by its merged renderer.  Outfit data may
        /// contain extra bones that are not present on the Figure, so copy missing transform
        /// hierarchy nodes without copying source renderers or Animator/Avatar payload.
        /// </summary>
        internal static void PreserveOutfitBoneHierarchy(Transform source, Transform target)
        {
            for (int sourceIndex = 0; sourceIndex < source.childCount; sourceIndex++)
            {
                Transform sourceChild = source.GetChild(sourceIndex);
                Transform targetChild = FindMatchingChild(target, sourceChild, sourceIndex);
                if (targetChild == null)
                {
                    var node = new GameObject(sourceChild.name);
                    targetChild = node.transform;
                    targetChild.SetParent(target, false);
                    targetChild.localPosition = sourceChild.localPosition;
                    targetChild.localRotation = sourceChild.localRotation;
                    targetChild.localScale = sourceChild.localScale;
                    targetChild.SetSiblingIndex(Mathf.Min(sourceIndex, target.childCount - 1));
                }
                PreserveOutfitBoneHierarchy(sourceChild, targetChild);
            }
        }

        private static Transform FindMatchingChild(Transform parent, Transform sourceChild, int preferredIndex)
        {
            if (preferredIndex < parent.childCount && parent.GetChild(preferredIndex).name == sourceChild.name)
                return parent.GetChild(preferredIndex);
            for (int index = 0; index < parent.childCount; index++)
                if (parent.GetChild(index).name == sourceChild.name) return parent.GetChild(index);
            return null;
        }

        private static void Stage(ShapeSyncDatabase database, Transform intermediate, ShapeSyncDatabaseTransaction.EditContext transaction,
            ShapeSyncFigureImportAdmission admission, ShapeSyncFigureMeshMerger.Result merge, ShapeSyncFigureImport.DatabaseMaterialCopies materials, string name)
        {
            merge.Root.name = name;
            merge.Renderer.sharedMesh.name = name + "_SkinnedMesh";
            materials.AddTo(transaction);
            merge.Renderer.sharedMaterials = materials.Materials;
            transaction.AddSubAsset(merge.Renderer.sharedMesh);
            merge.Root.transform.SetParent(intermediate, false);
            // The import-time Source/Merged Prefabs are serialized in the same
            // Database transaction.  Mark the renderer, mesh, and root dirty
            // after assigning the Database-owned Mesh so the reference survives
            // closing and reopening the Database Prefab.
            EditorUtility.SetDirty(merge.Renderer.sharedMesh);
            EditorUtility.SetDirty(merge.Renderer);
            EditorUtility.SetDirty(merge.Root);
            // Preserve Animator / Avatar as asset-local Prefab data.  Generate later owns
            // component removal from its output; authoring import must not discard the
            // only local Avatar resolution path.  The clone also retains Outfit-only bones.
        }
    }
}
