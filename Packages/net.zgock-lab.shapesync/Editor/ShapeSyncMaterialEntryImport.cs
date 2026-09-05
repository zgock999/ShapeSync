// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Stages Base Material Entry assets as one Database transaction.</summary>
    internal static class ShapeSyncMaterialEntryImport
    {
        internal readonly struct Rename
        {
            internal string CurrentName { get; }
            internal string NextName { get; }
            internal Rename(string currentName, string nextName) { CurrentName = currentName; NextName = nextName; }
        }

        internal static bool TrySave(string databaseAssetPath, IReadOnlyList<ShapeSyncMaterialAdapterResolver.Admission> admissions, out string diagnostic)
        {
            return TrySaveWithTextureRename(databaseAssetPath, admissions, false, out diagnostic);
        }

        /// <summary>Renames existing Material Entry identities atomically and optionally reapplies Figure_Texture resource names.</summary>
        internal static bool TryRename(string databaseAssetPath, IReadOnlyList<Rename> renames, bool renameTextures, out string diagnostic)
        {
            diagnostic = null;
            if (renames == null || renames.Count == 0) { diagnostic = "Material Entry rename requires at least one Entry."; return false; }
            var currentNames = new HashSet<string>(StringComparer.Ordinal);
            var nextNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rename rename in renames)
            {
                if (!ShapeSyncDatabaseRegistry.IsValidUserName(rename.CurrentName) || !ShapeSyncDatabaseRegistry.IsValidUserName(rename.NextName) || !currentNames.Add(rename.CurrentName) || !nextNames.Add(rename.NextName))
                { diagnostic = "Material Entry rename names must be non-empty, whitespace-free, and unique."; return false; }
            }
            try
            {
                return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, _) =>
                {
                    if (database == null || database.Registry == null) throw new InvalidOperationException("Material Entry rename requires an open Database Prefab.");
                    if (database.Registry.MaterialEntries.Count != renames.Count) throw new InvalidOperationException("Material Entry rename must address every existing Entry.");
                    foreach (Rename rename in renames)
                        if (!database.Registry.ContainsMaterialEntryName(rename.CurrentName)) throw new InvalidOperationException("Material Entry does not exist: " + rename.CurrentName);

                    var temporaryNames = new List<Rename>();
                    for (int index = 0; index < renames.Count; index++)
                    {
                        Rename rename = renames[index];
                        if (string.Equals(rename.CurrentName, rename.NextName, StringComparison.Ordinal)) continue;
                        string temporary = "__ShapeSyncMaterialEntryRename_" + index;
                        while (database.Registry.ContainsMaterialEntryName(temporary)) temporary += "_";
                        if (!database.Registry.TryRenameMaterialEntry(rename.CurrentName, temporary, out string temporaryDiagnostic)) throw new InvalidOperationException(temporaryDiagnostic);
                        temporaryNames.Add(new Rename(temporary, rename.NextName));
                    }
                    foreach (Rename rename in temporaryNames)
                        if (!database.Registry.TryRenameMaterialEntry(rename.CurrentName, rename.NextName, out string renameDiagnostic)) throw new InvalidOperationException(renameDiagnostic);
                    RenameEntryMaterialsForFigure(database);
                    if (renameTextures) ShapeSyncTextureResourceImport.RenameMaterialTexturesForFigure(database);
                }, out diagnostic);
            }
            catch (Exception exception)
            {
                diagnostic = "Material Entry rename failed before transaction commit: " + exception.Message;
                return false;
            }
        }

        /// <summary>Renames registry-only Material Entry identities without reopening or staging the Database Prefab.</summary>
        internal static bool TryRenameDirect(ShapeSyncDatabase database, IReadOnlyList<Rename> renames, bool renameTextures, out string diagnostic)
        {
            diagnostic = null;
            if (database == null || database.Registry == null) { diagnostic = "Material Entry rename requires an open Database Prefab."; return false; }
            if (renames == null || renames.Count == 0) { diagnostic = "Material Entry rename requires at least one Entry."; return false; }
            var currentNames = new HashSet<string>(StringComparer.Ordinal);
            var nextNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rename rename in renames)
            {
                if (!ShapeSyncDatabaseRegistry.IsValidUserName(rename.CurrentName) || !ShapeSyncDatabaseRegistry.IsValidUserName(rename.NextName)
                    || !currentNames.Add(rename.CurrentName) || !nextNames.Add(rename.NextName))
                { diagnostic = "Material Entry rename names must be non-empty, whitespace-free, and unique."; return false; }
            }
            var materialNames = database.Registry.MaterialEntries.Where(entry => entry != null && entry.Material != null)
                .ToDictionary(entry => entry.Material, entry => entry.Material.name);
            bool saved = ShapeSyncDatabaseDirectEdit.TryEdit(database, "Rename ShapeSync Material Entries",
                (ShapeSyncDatabaseRegistry registry, out string detail) =>
                {
                    if (registry.MaterialEntries.Count != renames.Count) { detail = "Material Entry rename must address every existing Entry."; return false; }
                    foreach (Rename rename in renames)
                        if (!registry.ContainsMaterialEntryName(rename.CurrentName)) { detail = "Material Entry does not exist: " + rename.CurrentName; return false; }
                    var temporaryNames = new List<Rename>();
                    for (int index = 0; index < renames.Count; index++)
                    {
                        Rename rename = renames[index];
                        if (string.Equals(rename.CurrentName, rename.NextName, StringComparison.Ordinal)) continue;
                        string temporary = "__ShapeSyncMaterialEntryRename_" + index;
                        while (registry.ContainsMaterialEntryName(temporary)) temporary += "_";
                        if (!registry.TryRenameMaterialEntry(rename.CurrentName, temporary, out string temporaryDiagnostic)) { detail = temporaryDiagnostic; return false; }
                        temporaryNames.Add(new Rename(temporary, rename.NextName));
                    }
                    foreach (Rename rename in temporaryNames)
                        if (!registry.TryRenameMaterialEntry(rename.CurrentName, rename.NextName, out string renameDiagnostic)) { detail = renameDiagnostic; return false; }
                    try
                    {
                        RenameEntryMaterialsForFigure(database);
                        if (renameTextures) ShapeSyncTextureResourceImport.RenameMaterialTexturesForFigure(database);
                    }
                    catch (Exception exception) { detail = exception.Message; return false; }
                    detail = null;
                    return true;
                }, out diagnostic);
            if (!saved)
                foreach (KeyValuePair<Material, string> pair in materialNames)
                    if (pair.Key != null) pair.Key.name = pair.Value;
            return saved;
        }

        private static void RenameEntryMaterialsForFigure(ShapeSyncDatabase database)
        {
            if (database == null || database.Registry == null) throw new InvalidOperationException("Material Entry rename requires one Base Figure.");
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figure, out string diagnostic) || figure == null)
                throw new InvalidOperationException(diagnostic ?? "Material Entry rename requires one Base Figure.");
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries)
            {
                if (entry == null || entry.Material == null) throw new InvalidOperationException("Material Entry rename requires an owned Material.");
                ShapeSyncEntryAssetNaming.ApplyMaterialName(entry.Material, figure.Name, entry.LogicalName);
            }
        }

        /// <summary>Stages Material Entries and, when requested by the Materials Detail, applies Figure_Texture resource names in that same transaction.</summary>
        internal static bool TrySaveWithTextureRename(string databaseAssetPath, IReadOnlyList<ShapeSyncMaterialAdapterResolver.Admission> admissions, bool renameTextures, out string diagnostic)
        {
            diagnostic = null;
            if (admissions == null || admissions.Count == 0)
            {
                diagnostic = "Material Entry save requires at least one admitted Base material slot.";
                return false;
            }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase existingDatabase, out diagnostic)) return false;
            if (!existingDatabase.Registry.TryGetSingleBaseFigure(existingDatabase, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic) || baseFigure == null)
            {
                diagnostic = baseDiagnostic ?? "Material Entry save requires exactly one Base Figure.";
                return false;
            }

            var staged = new List<StagedEntry>();
            var textures = new List<Texture>();
            var textureCopies = new Dictionary<Texture, Texture>();
            var adapterCopies = ShapeSyncMaterialAdapterResolver.CreateDatabaseAdapterCache(existingDatabase.Registry);
            var stagedAdapters = new HashSet<MaterialShaderAdapter>();
            try
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < admissions.Count; i++)
                {
                    ShapeSyncMaterialAdapterResolver.Admission admission = admissions[i];
                    if (admission == null || admission.TransientAdapter == null) throw new InvalidOperationException("Material Entry admission is missing or has been disposed.");
                    if (!names.Add(admission.LogicalName)) throw new InvalidOperationException("Material Entry admission names must be unique within one save.");
                    if (existingDatabase.Registry.ContainsMaterialEntryName(admission.LogicalName)) throw new InvalidOperationException("Material Entry name already exists in the Database: " + admission.LogicalName);
                    Material material = new Material(admission.SourceMaterial);
                    ShapeSyncEntryAssetNaming.ApplyMaterialName(material, baseFigure.Name, admission.LogicalName);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture source = material.GetTexture(property);
                        if (source == null) continue;
                        // Figure import already creates and registers Database-owned copies
                        // for its Textures.  Reuse them so the resource entity produced at
                        // Figure Import remains the one later referenced by this Entry.
                        if (AssetDatabase.GetAssetPath(source) == databaseAssetPath) continue;
                        if (!textureCopies.TryGetValue(source, out Texture copy))
                        {
                            copy = UnityEngine.Object.Instantiate(source);
                            copy.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(source)
                                ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                                : admission.LogicalName + "_" + source.name;
                            textureCopies.Add(source, copy);
                            textures.Add(copy);
                        }
                        material.SetTexture(property, copy);
                    }
                    Type adapterType = admission.TransientAdapter.GetType();
                    if (!adapterCopies.TryGetValue(adapterType, out MaterialShaderAdapter adapter))
                    {
                        adapter = UnityEngine.Object.Instantiate(admission.TransientAdapter);
                        adapter.name = adapterType.Name;
                        adapterCopies.Add(adapterType, adapter);
                        stagedAdapters.Add(adapter);
                    }
                    staged.Add(new StagedEntry(admission, material, adapter));
                }

                return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, context) =>
                {
                    var rendererMaterials = new Dictionary<SkinnedMeshRenderer, Material[]>();
                    var replacedMaterials = new HashSet<Material>();
                    var resolvedRenderers = new Dictionary<StagedEntry, SkinnedMeshRenderer>();
                    if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseEntry, out string baseDiagnostic) || baseEntry == null)
                        throw new InvalidOperationException(baseDiagnostic ?? "Material Entry save requires exactly one Base Figure.");
                    foreach (StagedEntry entry in staged)
                    {
                        SkinnedMeshRenderer renderer = FindRenderer(baseEntry.Figure, entry.Admission.BaseRelativeRendererPath);
                        if (!database.Registry.TryValidateMaterialEntry(database, entry.Admission.LogicalName, renderer, entry.Admission.MaterialSlot, entry.Admission.SourceMaterial, out string admissionDiagnostic)) throw new InvalidOperationException(admissionDiagnostic);
                        resolvedRenderers.Add(entry, renderer);
                        if (!rendererMaterials.TryGetValue(renderer, out Material[] slots))
                        {
                            slots = renderer.sharedMaterials;
                            rendererMaterials.Add(renderer, slots);
                        }
                        Material previous = slots[entry.Admission.MaterialSlot];
                        if (previous != null && previous != entry.Material) replacedMaterials.Add(previous);
                        slots[entry.Admission.MaterialSlot] = entry.Material;
                    }
                    foreach (KeyValuePair<SkinnedMeshRenderer, Material[]> pair in rendererMaterials) pair.Key.sharedMaterials = pair.Value;
                    foreach (Texture texture in textures) context.AddSubAsset(texture);
                    foreach (MaterialShaderAdapter adapter in stagedAdapters) context.AddSubAsset(adapter);
                    foreach (StagedEntry entry in staged)
                    {
                        context.AddSubAsset(entry.Material);
                        if (!database.Registry.TryRegisterMaterialEntry(database, entry.Admission.LogicalName, resolvedRenderers[entry], entry.Admission.MaterialSlot, entry.Admission.SourceMaterialName, entry.Material, entry.Adapter, out string registrationDiagnostic)) throw new InvalidOperationException(registrationDiagnostic);
                    }
                    // Resource registration is part of this same snapshot: a failed
                    // resource assignment must never leave newly staged Entries behind.
                    ShapeSyncTextureResourceImport.RegisterExistingMaterialTextures(database, databaseAssetPath);
                    ShapeSyncMaterialAdapterResolver.CanonicalizeDatabaseAdapters(database, context, databaseAssetPath, adapterCopies);
                    if (renameTextures) ShapeSyncTextureResourceImport.RenameMaterialTexturesForFigure(database);
                    // Figure import creates ownership-safe Material copies before logical
                    // Material Entries exist. Once an Entry replaces such a slot, leave no
                    // unreachable pre-Entry Material sub-asset behind.
                    var stillReferenced = new HashSet<Material>(database.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null));
                    foreach (Material previous in replacedMaterials)
                        if (!stillReferenced.Contains(previous) && AssetDatabase.GetAssetPath(previous) == databaseAssetPath)
                            context.RemoveSubAsset(previous);
                }, out diagnostic);
            }
            catch (Exception exception)
            {
                diagnostic = "Material Entry save failed before transaction commit: " + exception.Message;
                return false;
            }
            finally
            {
                // Snapshot rollback removes staged sub-assets; only uncommitted in-memory objects need destruction here.
                if (!string.IsNullOrEmpty(diagnostic))
                {
                    foreach (StagedEntry entry in staged)
                        if (entry.Material != null && !AssetDatabase.Contains(entry.Material)) UnityEngine.Object.DestroyImmediate(entry.Material);
                    foreach (MaterialShaderAdapter adapter in adapterCopies.Values)
                        if (adapter != null && !AssetDatabase.Contains(adapter)) UnityEngine.Object.DestroyImmediate(adapter);
                    foreach (Texture texture in textures)
                        if (texture != null && !AssetDatabase.Contains(texture)) UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static SkinnedMeshRenderer FindRenderer(GameObject baseFigure, string relativePath)
        {
            if (baseFigure == null) return null;
            if (relativePath == null) return null;
            Transform transform = baseFigure.transform;
            if (relativePath.Length > 0)
            {
                string[] segments = relativePath.Split('/');
                foreach (string segment in segments)
                {
                    if (!int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 0 || index >= transform.childCount) return null;
                    transform = transform.GetChild(index);
                }
            }
            return transform.GetComponent<SkinnedMeshRenderer>();
        }

        private sealed class StagedEntry
        {
            internal readonly ShapeSyncMaterialAdapterResolver.Admission Admission;
            internal readonly Material Material;
            internal readonly MaterialShaderAdapter Adapter;
            internal StagedEntry(ShapeSyncMaterialAdapterResolver.Admission admission, Material material, MaterialShaderAdapter adapter) { Admission = admission; Material = material; Adapter = adapter; }
        }
    }
}
#endif
