// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>One admitted source Figure assigned to a shape key (Base or FBM) while importing an axis.</summary>
    internal readonly struct ShapeSyncAxisFigureSource
    {
        internal ShapeSyncAxisFigureSource(string fbmName, ShapeSyncFigureImportAdmission admission)
        {
            FbmName = fbmName;
            Admission = admission;
        }
        internal string FbmName { get; }
        internal ShapeSyncFigureImportAdmission Admission { get; }
    }

    /// <summary>One admitted Figure axis and all source Figures that realize it.</summary>
    internal readonly struct ShapeSyncFigureAxisImportRequest
    {
        internal ShapeSyncFigureAxisImportRequest(ShapeSyncDatabaseRegistry.FigureAxisAdmission axis, IReadOnlyList<ShapeSyncAxisFigureSource> sources)
        {
            Axis = axis;
            Sources = sources;
        }
        internal ShapeSyncDatabaseRegistry.FigureAxisAdmission Axis { get; }
        internal IReadOnlyList<ShapeSyncAxisFigureSource> Sources { get; }
    }

    /// <summary>
    /// Imports FBM/PBM merged Figures in one Database snapshot transaction.
    /// A PBM is represented by one explicit Base Figure and one Figure per FBM: there is no
    /// silent fallback to a Base Figure or to a partially entered PBM row set.
    /// </summary>
    internal static class ShapeSyncFigureAxisImport
    {
        internal static bool TryRemovePbm(string databaseAssetPath, string axisName, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath)) { diagnostic = "PBM removal requires a Database Prefab path."; return false; }
            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, transaction) =>
            {
                if (!database.Registry.TryRemovePbmAxis(database, axisName, out GameObject[] figures, out string removeDiagnostic))
                    throw new InvalidOperationException(removeDiagnostic);
                ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                Avatar[] removedAvatars = GetDatabaseOwnedAvatars(figures, databaseAssetPath);
                foreach (GameObject figure in figures) UnityEngine.Object.DestroyImmediate(figure);
                RemoveUnreferencedAvatars(database, transaction, removedAvatars);
            }, out diagnostic);
        }

        internal static bool TryRenamePbm(string databaseAssetPath, string currentName, string replacementName, out string diagnostic)
        {
            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, transaction) =>
            {
                if (!database.Registry.TryRenamePbmAxis(database, currentName, replacementName, out string renameDiagnostic))
                    throw new InvalidOperationException(renameDiagnostic);
                ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
            }, out diagnostic);
        }

        internal static bool TryReplacePbm(string databaseAssetPath, string currentName, string replacementName,
            IReadOnlyList<ShapeSyncAxisFigureSource> sources, out string diagnostic)
        {
            diagnostic = null;
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase databaseAsset, out diagnostic)) return false;
            var expected = new HashSet<string>(databaseAsset.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal)
            { ShapeSyncDatabaseRegistry.BaseShapeKey };
            if (sources == null || sources.Count != expected.Count || sources.Any(source => source.Admission == null)
                || !new HashSet<string>(sources.Select(source => source.FbmName), StringComparer.Ordinal).SetEquals(expected))
            { diagnostic = "PBM replacement requires one admitted Base Figure and one admitted Figure for every FBM."; return false; }
            var staged = new List<StagedFigure>();
            try
            {
                var stageRequest = new ShapeSyncFigureAxisImportRequest(
                    new ShapeSyncDatabaseRegistry.FigureAxisAdmission(replacementName, ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm, new object()), sources);
                if (!TryStageAll(databaseAsset.Registry, new[] { stageRequest }, staged, out diagnostic)) return false;
                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    var bindings = staged.Select(item => new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(item.FbmName, item.Merge.Root)).ToArray();
                    if (!database.Registry.TryPreparePbmReplacement(database, currentName, replacementName, bindings,
                        out int replacementIndex, out GameObject[] removedFigures, out string prepareDiagnostic)) throw new InvalidOperationException(prepareDiagnostic);
                    ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                    // Existing Database Figures may themselves be the fallback sources for
                    // unspecified rows. Attach the new clones before removing those sources.
                    // Duplicate sibling names are transient inside this one transaction.
                    foreach (StagedFigure item in staged)
                    {
                        BindDatabaseFigureMaterials(database.Registry, item.Merge);
                        ShapeSyncFigureImport.AttachMergedFigure(database, intermediate, transaction, item.Admission, item.Merge, item.Materials, item.DatabaseFigureName);
                    }
                    Avatar[] removedAvatars = GetDatabaseOwnedAvatars(removedFigures, databaseAssetPath);
                    foreach (GameObject figure in removedFigures) UnityEngine.Object.DestroyImmediate(figure);
                    RemoveUnreferencedAvatars(database, transaction, removedAvatars);
                    if (!database.Registry.CommitPbmReplacement(database, replacementName, bindings, replacementIndex, out string commitDiagnostic))
                        throw new InvalidOperationException(commitDiagnostic);
                }, out diagnostic)) return false;
                foreach (StagedFigure item in staged) item.Detach();
                return true;
            }
            finally { foreach (StagedFigure item in staged) item.Dispose(); }
        }

        internal static bool TryRemoveFbm(string databaseAssetPath, string axisName, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath)) { diagnostic = "FBM removal requires a Database Prefab path."; return false; }
            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, transaction) =>
            {
                if (!database.Registry.TryRemoveFbmAxis(database, axisName, out GameObject[] figures, out Texture[] orphanedTextures, out string removeDiagnostic))
                    throw new InvalidOperationException(removeDiagnostic);
                ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                ShapeSyncMeshOutfitCollectionAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                Mesh[] removedMeshes = figures.SelectMany(figure => figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .Select(renderer => renderer.sharedMesh).Where(mesh => mesh != null && AssetDatabase.GetAssetPath(mesh) == databaseAssetPath).Distinct().ToArray();
                Material[] removedMaterials = figures.SelectMany(figure => figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null && AssetDatabase.GetAssetPath(material) == databaseAssetPath).Distinct().ToArray();
                Texture[] removedMaterialTextures = removedMaterials.SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture))
                    .Where(texture => texture != null && AssetDatabase.GetAssetPath(texture) == databaseAssetPath).Distinct().ToArray();
                Avatar[] removedAvatars = GetDatabaseOwnedAvatars(figures, databaseAssetPath);
                foreach (GameObject figure in figures) UnityEngine.Object.DestroyImmediate(figure);
                RemoveUnreferencedAvatars(database, transaction, removedAvatars);

                var referencedMeshes = new HashSet<Mesh>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(renderer => renderer.sharedMesh).Where(mesh => mesh != null));
                foreach (Mesh mesh in removedMeshes) if (!referencedMeshes.Contains(mesh)) transaction.RemoveSubAsset(mesh);
                var referencedMaterials = new HashSet<Material>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null));
                foreach (Material material in removedMaterials) if (!referencedMaterials.Contains(material)) transaction.RemoveSubAsset(material);
                var referencedTextures = new HashSet<Texture>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                    .SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture)).Where(texture => texture != null));
                foreach (Texture texture in database.Registry.TextureResources.Select(entry => entry?.Texture).Where(texture => texture != null)) referencedTextures.Add(texture);
                foreach (Texture texture in orphanedTextures.Concat(removedMaterialTextures).Distinct())
                    if (!referencedTextures.Contains(texture)) transaction.RemoveSubAsset(texture);
            }, out diagnostic);
        }

        internal static bool TryRenameFbm(string databaseAssetPath, string currentName, string replacementName, out string diagnostic)
        {
            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, transaction) =>
            {
                if (!database.Registry.TryRenameFbmAxis(database, currentName, replacementName, out GameObject[] removedPbmFigures, out string renameDiagnostic))
                    throw new InvalidOperationException(renameDiagnostic);
                ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                ShapeSyncMeshOutfitCollectionAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                Avatar[] removedAvatars = GetDatabaseOwnedAvatars(removedPbmFigures, databaseAssetPath);
                foreach (GameObject figure in removedPbmFigures) UnityEngine.Object.DestroyImmediate(figure);
                RemoveUnreferencedAvatars(database, transaction, removedAvatars);
            }, out diagnostic);
        }

        /// <summary>
        /// Reimports an existing FBM from a newly selected source Prefab.  The FBM may be
        /// renamed at the same time.  PBM / Extra Morph data is invalidated atomically;
        /// Figure Normal relations follow the FBM name and PCM Slots are left untouched.
        /// </summary>
        internal static bool TryReplaceFbm(string databaseAssetPath, string currentName, string replacementName,
            bool importMaterialsAndTextures, ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            diagnostic = null;
            if (admission == null) { diagnostic = "FBM replacement requires an admitted source Figure."; return false; }
            if (admission.Animator == null || admission.Avatar == null)
            {
                diagnostic = "FBM replacement requires a source Figure with a valid Humanoid Animator and Avatar.";
                return false;
            }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase databaseAsset, out diagnostic)) return false;
            if (databaseAsset.Registry == null) { diagnostic = "ShapeSync Database registry is unavailable."; return false; }
            if (!ShapeSyncFigureMeshMerger.TryMergeOwned(admission.HumanoidRoot, admission.SourceRenderers, out ShapeSyncFigureMeshMerger.Result merge, out diagnostic)) return false;
            ShapeSyncFigureImport.DatabaseMaterialCopies materials = null;
            try
            {
                if (importMaterialsAndTextures
                    && !ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreate(replacementName, merge.Renderer.sharedMaterials, out materials, out diagnostic)) return false;
                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    if (!database.Registry.TryPrepareFbmReplacement(database, currentName, replacementName,
                        out int replacementIndex, out GameObject[] removedFigures, out Texture[] orphanedTextures, out string prepareDiagnostic))
                        throw new InvalidOperationException(prepareDiagnostic);
                    ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                    ShapeSyncMeshOutfitCollectionAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                    Material[] replacedMaterials = removedFigures.SelectMany(figure => figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null && AssetDatabase.GetAssetPath(material) == databaseAssetPath).Distinct().ToArray();
                    Texture[] replacedTextures = replacedMaterials.SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture))
                        .Where(texture => texture != null && AssetDatabase.GetAssetPath(texture) == databaseAssetPath).Distinct().ToArray();
                    Avatar[] removedAvatars = GetDatabaseOwnedAvatars(removedFigures, databaseAssetPath);
                    foreach (Texture texture in orphanedTextures) transaction.RemoveSubAsset(texture);
                    foreach (GameObject figure in removedFigures) UnityEngine.Object.DestroyImmediate(figure);
                    RemoveUnreferencedAvatars(database, transaction, removedAvatars);
                    IReadOnlyDictionary<string, Texture> normalSourcesBeforeMaterialBind = importMaterialsAndTextures
                        ? null
                        : CaptureDeclaredNormalSources(database.Registry, merge.Renderer);
                    if (!importMaterialsAndTextures) BindDatabaseFigureMaterials(database.Registry, merge);
                    ShapeSyncFigureImport.AttachMergedFigure(database, intermediate, transaction, admission, merge, materials, replacementName);
                    if (!database.Registry.CommitFbmReplacement(database, replacementName, merge.Root, importMaterialsAndTextures, replacementIndex, out string commitDiagnostic))
                        throw new InvalidOperationException(commitDiagnostic);
                    RegisterFbmTextureEntries(database, replacementName, importMaterialsAndTextures, materials, transaction);
                    if (!importMaterialsAndTextures)
                        RegisterFbmNormalEntries(database, replacementName, normalSourcesBeforeMaterialBind, transaction);
                    var referencedMaterials = new HashSet<Material>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null));
                    foreach (Material material in replacedMaterials) if (!referencedMaterials.Contains(material)) transaction.RemoveSubAsset(material);
                    var referencedTextures = new HashSet<Texture>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                        .SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture)).Where(texture => texture != null));
                    foreach (Texture texture in database.Registry.TextureResources.Select(entry => entry?.Texture).Where(texture => texture != null)) referencedTextures.Add(texture);
                    foreach (Texture texture in replacedTextures) if (!referencedTextures.Contains(texture)) transaction.RemoveSubAsset(texture);
                }, out diagnostic)) return false;
                merge.DetachMesh();
                materials?.Detach();
                return true;
            }
            finally
            {
                materials?.Dispose();
                merge.Dispose();
            }
        }

        private sealed class StagedFigure : IDisposable
        {
            internal readonly int RequestIndex;
            internal readonly string FbmName;
            internal readonly string DatabaseFigureName;
            internal readonly ShapeSyncFigureImportAdmission Admission;
            internal readonly ShapeSyncFigureMeshMerger.Result Merge;
            internal readonly ShapeSyncFigureImport.DatabaseMaterialCopies Materials;

            internal StagedFigure(int requestIndex, string fbmName, string databaseFigureName, ShapeSyncFigureImportAdmission admission, ShapeSyncFigureMeshMerger.Result merge, ShapeSyncFigureImport.DatabaseMaterialCopies materials)
            {
                RequestIndex = requestIndex;
                FbmName = fbmName;
                DatabaseFigureName = databaseFigureName;
                Admission = admission;
                Merge = merge;
                Materials = materials;
            }

            internal void Detach() { Merge.DetachMesh(); Materials?.Detach(); }
            public void Dispose() { Materials?.Dispose(); Merge.Dispose(); }
        }

        internal static bool TryImport(string databaseAssetPath, IReadOnlyList<ShapeSyncFigureAxisImportRequest> requests, out string diagnostic)
        {
            diagnostic = null;
            if (requests == null || requests.Count == 0)
            {
                diagnostic = "Figure-axis import requires at least one admitted axis request.";
                return false;
            }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase databaseAsset, out diagnostic)) return false;
            if (databaseAsset.Registry == null)
            {
                diagnostic = "ShapeSync Database registry is unavailable.";
                return false;
            }
            ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes = requests.Select(request => request.Axis).ToArray();
            if (!databaseAsset.Registry.TryValidateFigureAxisAdmissions(databaseAsset, axes, out diagnostic)) return false;
            if (!TryValidateRequestSources(databaseAsset.Registry, requests, axes, out diagnostic)) return false;

            var staged = new List<StagedFigure>();
            try
            {
                if (!TryStageAll(databaseAsset.Registry, requests, staged, out diagnostic)) return false;
                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    if (database.Registry == null) throw new InvalidOperationException("ShapeSync Database registry is unavailable.");
                    if (axes.Any(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) && database.Registry.FbmAxesFinalized)
                    {
                        if (!database.Registry.TryClearPbmAndExtraMorphsForFbmRedefinition(database, out GameObject[] removedPbmFigures, out string clearDiagnostic))
                            throw new InvalidOperationException(clearDiagnostic);
                        ShapeSyncMeshOutfitPbmFollowAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                        ShapeSyncMeshOutfitCollectionAuthoring.ClearAllForFigureAxisChange(database, databaseAssetPath, transaction);
                        Avatar[] removedAvatars = GetDatabaseOwnedAvatars(removedPbmFigures, databaseAssetPath);
                        foreach (GameObject figure in removedPbmFigures) UnityEngine.Object.DestroyImmediate(figure);
                        RemoveUnreferencedAvatars(database, transaction, removedAvatars);
                    }
                    var bindings = new List<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[requests.Count];
                    var normalSourcesBeforeMaterialBind = new Dictionary<StagedFigure, IReadOnlyDictionary<string, Texture>>();
                    for (int i = 0; i < bindings.Length; i++) bindings[i] = new List<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>();
                    foreach (StagedFigure item in staged)
                    {
                        if (axes[item.RequestIndex].Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                            && !axes[item.RequestIndex].ImportAllMaterialsAndTextures)
                            normalSourcesBeforeMaterialBind.Add(item, CaptureDeclaredNormalSources(database.Registry, item.Merge.Renderer));
                        if (axes[item.RequestIndex].Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm
                            || (axes[item.RequestIndex].Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                                && !axes[item.RequestIndex].ImportAllMaterialsAndTextures))
                            BindDatabaseFigureMaterials(database.Registry, item.Merge);
                        ShapeSyncFigureImport.AttachMergedFigure(database, intermediate, transaction, item.Admission, item.Merge, item.Materials, item.DatabaseFigureName);
                        bindings[item.RequestIndex].Add(new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(item.FbmName, item.Merge.Root));
                    }
                    IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] readonlyBindings = bindings.Cast<IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>>().ToArray();
                    if (!database.Registry.TryCommitFigureAxes(database, axes, readonlyBindings, out string registryDiagnostic))
                    {
                        throw new InvalidOperationException(registryDiagnostic);
                    }
                    RegisterRequestedFbmTextureEntries(database, requests, staged, transaction);
                    RegisterRequestedFbmNormalEntries(database, requests, staged, normalSourcesBeforeMaterialBind, transaction);
                }, out diagnostic)) return false;
                foreach (StagedFigure item in staged) item.Detach();
                return true;
            }
            finally
            {
                foreach (StagedFigure item in staged) item.Dispose();
            }
        }

        /// <summary>Registers the optional FBM-specific MainTex entries from the already-owned FBM material clones.</summary>
        private static void RegisterRequestedFbmTextureEntries(ShapeSyncDatabase database, IReadOnlyList<ShapeSyncFigureAxisImportRequest> requests, IReadOnlyList<StagedFigure> staged, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                ShapeSyncFigureAxisImportRequest request = requests[requestIndex];
                if (request.Axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm || !request.Axis.ImportAllMaterialsAndTextures) continue;
                StagedFigure figure = staged.Single(item => item.RequestIndex == requestIndex && item.FbmName == request.Axis.Name);
                RegisterFbmTextureEntries(database, request.Axis.Name, true, figure.Materials, transaction);
            }
        }

        /// <summary>Imports declared Figure Normal relations for each FBM. Import All false still
        /// imports selected Normals, but must capture them before the renderer is rebound to Figure Materials.</summary>
        private static void RegisterRequestedFbmNormalEntries(ShapeSyncDatabase database, IReadOnlyList<ShapeSyncFigureAxisImportRequest> requests,
            IReadOnlyList<StagedFigure> staged, IReadOnlyDictionary<StagedFigure, IReadOnlyDictionary<string, Texture>> normalSourcesBeforeMaterialBind,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                ShapeSyncFigureAxisImportRequest request = requests[requestIndex];
                if (request.Axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm || request.Axis.ImportAllMaterialsAndTextures) continue;
                StagedFigure figure = staged.Single(item => item.RequestIndex == requestIndex && item.FbmName == request.Axis.Name);
                normalSourcesBeforeMaterialBind.TryGetValue(figure, out IReadOnlyDictionary<string, Texture> sources);
                RegisterFbmNormalEntries(database, request.Axis.Name, sources, transaction);
            }
        }

        private static void RegisterFbmNormalEntries(ShapeSyncDatabase database, string fbmName,
            IReadOnlyDictionary<string, Texture> normalSourcesBeforeMaterialBind, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            string[] declaredEntries = database.Registry.FigureNormalEntries.Where(entry => entry != null)
                .Select(entry => entry.MaterialEntryName).Distinct(StringComparer.Ordinal).ToArray();
            if (declaredEntries.Length == 0) return;

            if (normalSourcesBeforeMaterialBind == null) return;

            var assignments = new List<ShapeSyncNormalEntryAuthoring.Assignment>();
            foreach (string materialEntryName in declaredEntries)
                if (normalSourcesBeforeMaterialBind.TryGetValue(materialEntryName, out Texture normal) && normal != null)
                    assignments.Add(new ShapeSyncNormalEntryAuthoring.Assignment(materialEntryName, fbmName, normal,
                        ShapeSyncDatabaseRegistry.TextureResourceOwner.FigureFbm(fbmName)));
            if (assignments.Count != 0)
                ShapeSyncNormalEntryAuthoring.ApplyAssignments(database, AssetDatabase.GetAssetPath(database), declaredEntries, assignments, transaction);
        }

        private static IReadOnlyDictionary<string, Texture> CaptureDeclaredNormalSources(ShapeSyncDatabaseRegistry registry, SkinnedMeshRenderer renderer)
        {
            var sources = new Dictionary<string, Texture>(StringComparer.Ordinal);
            if (registry == null || renderer == null) return sources;
            foreach (ShapeSyncDatabaseRegistry.FigureNormalEntry normalEntry in registry.FigureNormalEntries)
            {
                if (normalEntry == null || sources.ContainsKey(normalEntry.MaterialEntryName)) continue;
                ShapeSyncDatabaseRegistry.MaterialEntry materialEntry = registry.MaterialEntries.FirstOrDefault(entry => entry != null && entry.LogicalName == normalEntry.MaterialEntryName);
                if (materialEntry == null || materialEntry.MaterialSlot < 0 || materialEntry.MaterialSlot >= renderer.sharedMaterials.Length) continue;
                Texture normal = ResolveNormalTexture(renderer.sharedMaterials[materialEntry.MaterialSlot]);
                if (normal != null) sources.Add(normalEntry.MaterialEntryName, normal);
            }
            return sources;
        }

        private static Texture ResolveNormalTexture(Material material)
        {
            if (material == null) return null;
            foreach (string property in material.GetTexturePropertyNames())
                if (property.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0)
                    return material.GetTexture(property);
            return null;
        }

        internal static void RegisterFbmTextureEntries(ShapeSyncDatabase database, string fbmName, bool importMaterialsAndTextures,
            ShapeSyncFigureImport.DatabaseMaterialCopies materials, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            if (!importMaterialsAndTextures) return;
            GameObject figure = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + fbmName)?.gameObject;
            if (figure == null) throw new InvalidOperationException("FBM Texture import requires the attached Database Figure: " + fbmName);
            Material[] stagedMaterials = materials.Materials;
            var provisionalMaterials = new HashSet<Material>();
            var provisionalTextures = new HashSet<Texture>();
            var importedTextures = new Dictionary<Texture, ImportedTexture>();
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries)
            {
                if (entry == null || entry.MaterialSlot < 0 || entry.MaterialSlot >= stagedMaterials.Length) continue;
                // Import All is defined by the FBM Material's MainTex. An arbitrary shader
                // Texture property is not a substitute.
                Material provisionalMaterial = stagedMaterials[entry.MaterialSlot];
                Texture provisionalTexture = provisionalMaterial == null ? null : ShapeSyncEntryAssetNaming.GetMainTexture(provisionalMaterial);
                if (provisionalTexture == null) continue;
                Material material = new Material(provisionalMaterial);
                ShapeSyncEntryAssetNaming.ApplyMaterialName(material, fbmName, entry.LogicalName);
                var importedResourceNames = new List<string>();
                int textureIndex = 0;
                foreach (Texture sourceTexture in ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(provisionalMaterial))
                {
                    if (!importedTextures.TryGetValue(sourceTexture, out ImportedTexture imported))
                    {
                        string logicalName = ShapeSyncEntryAssetNaming.GetTextureName(fbmName, entry.LogicalName, textureIndex++);
                        Texture texture = UnityEngine.Object.Instantiate(sourceTexture);
                        texture.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(sourceTexture)
                            ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                            : logicalName;
                        transaction.AddSubAsset(texture);
                        if (!database.Registry.TryRegisterTextureResource(logicalName, texture, ShapeSyncDatabaseRegistry.TextureResourceOwner.FigureFbm(fbmName), out string diagnostic)) throw new InvalidOperationException(diagnostic);
                        imported = new ImportedTexture(logicalName, texture);
                        importedTextures.Add(sourceTexture, imported);
                    }
                    // Every property alias of this source Texture must follow its owned
                    // Entry Texture. This applies to MainTex and to Normal/other Textures.
                    ShapeSyncEntryAssetNaming.ReplaceTextureAliases(material, sourceTexture, imported.Texture);
                    provisionalTextures.Add(sourceTexture);
                    importedResourceNames.Add(imported.LogicalName);
                }
                transaction.AddSubAsset(material);
                ReplaceFigureMaterial(figure, provisionalMaterial, material);
                provisionalMaterials.Add(provisionalMaterial);
                string[] resourceNames = entry.TextureResourceNames.Concat(importedResourceNames).Distinct(StringComparer.Ordinal).ToArray();
                if (!database.Registry.TrySetMaterialEntryTextureResources(entry.LogicalName, resourceNames, out string assignmentDiagnostic)) throw new InvalidOperationException(assignmentDiagnostic);
            }
            var referencedMaterials = new HashSet<Material>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null));
            foreach (Material material in provisionalMaterials)
                if (!referencedMaterials.Contains(material)) transaction.RemoveSubAsset(material);
            var referencedTextures = new HashSet<Texture>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
                .SelectMany(material => material.GetTexturePropertyNames().Select(material.GetTexture)).Where(texture => texture != null));
            foreach (Texture texture in database.Registry.TextureResources.Select(resource => resource?.Texture).Where(texture => texture != null)) referencedTextures.Add(texture);
            foreach (Texture texture in provisionalTextures)
                if (!referencedTextures.Contains(texture)) transaction.RemoveSubAsset(texture);
        }

        private readonly struct ImportedTexture
        {
            internal ImportedTexture(string logicalName, Texture texture) { LogicalName = logicalName; Texture = texture; }
            internal string LogicalName { get; }
            internal Texture Texture { get; }
        }

        private static void ReplaceFigureMaterial(GameObject figure, Material current, Material replacement)
        {
            foreach (SkinnedMeshRenderer renderer in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < slots.Length; index++)
                    if (slots[index] == current) { slots[index] = replacement; changed = true; }
                if (changed) renderer.sharedMaterials = slots;
            }
        }

        /// <summary>Validates every FBM/PBM source row before allocating a merge clone or Material copy.</summary>
        private static bool TryValidateRequestSources(ShapeSyncDatabaseRegistry registry, IReadOnlyList<ShapeSyncFigureAxisImportRequest> requests,
            IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisAdmission> axes, out string diagnostic)
        {
            diagnostic = null;
            var expectedFbmNames = new HashSet<string>(registry.FigureAxes
                .Where(entry => entry != null && entry.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                .Select(entry => entry.Name), StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.FigureAxisAdmission axis in axes)
            {
                if (axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) expectedFbmNames.Add(axis.Name);
            }
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                ShapeSyncFigureAxisImportRequest request = requests[requestIndex];
                if (request.Sources == null || request.Sources.Count == 0)
                {
                    diagnostic = "Figure-axis import requires a source Figure for every axis.";
                    return false;
                }
                var sourceFbmNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (ShapeSyncAxisFigureSource source in request.Sources)
                {
                    if (source.Admission == null || string.IsNullOrWhiteSpace(source.FbmName) || !sourceFbmNames.Add(source.FbmName))
                    {
                        diagnostic = "Figure-axis import requires unique admitted source Figures keyed by FBM name.";
                        return false;
                    }
                }
                if (request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    && (sourceFbmNames.Count != 1 || !sourceFbmNames.Contains(request.Axis.Name)))
                {
                    diagnostic = "FBM requires exactly one source Figure keyed by its own FBM name.";
                    return false;
                }
                if (request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    && (request.Sources[0].Admission.Animator == null || request.Sources[0].Admission.Avatar == null))
                {
                    diagnostic = "FBM import requires a source Figure with a valid Humanoid Animator and Avatar.";
                    return false;
                }
                if (request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm
                    && !sourceFbmNames.SetEquals(expectedFbmNames.Append(ShapeSyncDatabaseRegistry.BaseShapeKey)))
                {
                    diagnostic = "PBM requires exactly one Base source Figure and one source Figure for every FBM.";
                    return false;
                }
            }
            return true;
        }

        private static bool TryStageAll(ShapeSyncDatabaseRegistry registry, IReadOnlyList<ShapeSyncFigureAxisImportRequest> requests, List<StagedFigure> staged, out string diagnostic)
        {
            diagnostic = null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                ShapeSyncFigureAxisImportRequest request = requests[requestIndex];
                if (request.Sources == null || request.Sources.Count == 0)
                {
                    diagnostic = "Figure-axis import requires a source Figure for every axis.";
                    return false;
                }
                var sourceNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (ShapeSyncAxisFigureSource source in request.Sources)
                {
                    if (source.Admission == null || string.IsNullOrWhiteSpace(source.FbmName) || !sourceNames.Add(source.FbmName))
                    {
                        diagnostic = "Figure-axis import requires unique admitted source Figures keyed by FBM name.";
                        return false;
                    }
                    string figureName = request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                        ? request.Axis.Name
                        : GetPbmFigureName(registry, source.FbmName, request.Axis.Name);
                    if (!names.Add(figureName))
                    {
                        diagnostic = "Figure-axis import would create duplicate Database Figure names: " + figureName;
                        return false;
                    }
                    bool geometryOnly = request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm;
                    ShapeSyncFigureMeshMerger.Result merge;
                    bool merged = geometryOnly
                        ? ShapeSyncFigureMeshMerger.TryMergeOwnedGeometryOnly(source.Admission.HumanoidRoot, source.Admission.SourceRenderers, out merge, out diagnostic)
                        : ShapeSyncFigureMeshMerger.TryMergeOwned(source.Admission.HumanoidRoot, source.Admission.SourceRenderers, out merge, out diagnostic);
                    if (!merged) return false;
                    // PBM contributes geometric deformation only.  The owned merged Mesh can
                    // retain source blend-shape frames, so remove them before it reaches the
                    // Database; FBM raw-shape selection is handled later by Step 3.
                    if (request.Axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                    {
                        merge.Renderer.sharedMesh.ClearBlendShapes();
                    }
                    ShapeSyncFigureImport.DatabaseMaterialCopies materials = null;
                    // PBM is a geometry-only Figure axis. Its merged mesh is the sole
                    // owned payload; Material/Texture import belongs only to Base/FBM.
                    if (request.Axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm
                        && (request.Axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm || request.Axis.ImportAllMaterialsAndTextures)
                        && !ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreate(figureName, merge.Renderer.sharedMaterials, out materials, out diagnostic))
                    {
                        merge.Dispose();
                        return false;
                    }
                    staged.Add(new StagedFigure(requestIndex, source.FbmName, figureName, source.Admission, merge, materials));
                }
            }
            return true;
        }

        private static Avatar[] GetDatabaseOwnedAvatars(IEnumerable<GameObject> figures, string databaseAssetPath)
        {
            if (figures == null || string.IsNullOrWhiteSpace(databaseAssetPath)) return Array.Empty<Avatar>();
            return figures.Where(figure => figure != null)
                .SelectMany(figure => figure.GetComponentsInChildren<Animator>(true))
                .Select(animator => animator == null ? null : animator.avatar)
                .Where(avatar => avatar != null && AssetDatabase.GetAssetPath(avatar) == databaseAssetPath)
                .Distinct()
                .ToArray();
        }

        private static void RemoveUnreferencedAvatars(ShapeSyncDatabase database, ShapeSyncDatabaseTransaction.EditContext transaction, IEnumerable<Avatar> candidates)
        {
            if (database == null || transaction == null || candidates == null) return;
            var referenced = new HashSet<Avatar>(database.GetComponentsInChildren<Animator>(true)
                .Select(animator => animator == null ? null : animator.avatar)
                .Where(avatar => avatar != null));
            foreach (Avatar avatar in candidates)
                if (avatar != null && !referenced.Contains(avatar)) transaction.RemoveSubAsset(avatar);
        }

        private static string GetPbmFigureName(ShapeSyncDatabaseRegistry registry, string sourceFbmName, string pbmName)
        {
            if (registry == null) throw new InvalidOperationException("PBM import requires the ShapeSync Database registry.");
            string ownerName = sourceFbmName;
            if (sourceFbmName == ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                if (!registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic) || baseFigure == null)
                    throw new InvalidOperationException("PBM import Base Figure diagnostic: " + baseDiagnostic);
                ownerName = baseFigure.Name;
            }
            if (string.IsNullOrWhiteSpace(ownerName)) throw new InvalidOperationException("PBM import requires the Base Figure name.");
            return ownerName + "_" + pbmName;
        }

        private static void BindDatabaseFigureMaterials(ShapeSyncDatabaseRegistry registry, ShapeSyncFigureMeshMerger.Result merge)
        {
            if (registry == null || merge == null) throw new InvalidOperationException("Axis import requires Database Figure Materials.");
            SkinnedMeshRenderer renderer = merge.Renderer;
            if (renderer == null) throw new InvalidOperationException("Axis import did not produce a merged SkinnedMeshRenderer.");
            Material[] materials = renderer.sharedMaterials;
            // Material Entries are admitted from the already-merged Base Figure, whose
            // slots are the canonical combined-mesh order. FBM/PBM merge uses that
            // same ordered Material slot contract, including Figures with multiple sources.
            for (int slot = 0; slot < materials.Length; slot++)
            {
                ShapeSyncDatabaseRegistry.MaterialEntry entry = registry.MaterialEntries.SingleOrDefault(candidate => candidate != null && candidate.MaterialSlot == slot);
                if (entry == null || entry.Material == null)
                    throw new InvalidOperationException("Axis import requires one saved Figure Material for every merged Material slot.");
                materials[slot] = entry.Material;
            }
            renderer.sharedMaterials = materials;
        }
    }
}
