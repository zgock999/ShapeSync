// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Creates editor-owned texture copies without requiring imported textures to be readable.</summary>
    internal static class ShapeSyncEditorTextureUtility
    {
        // Compatibility-only identity used while preserving legacy source names during
        // editor copies. Atlas neutrality is determined from MaterialShaderAdapter state/content.
        internal const string LegacyNeutralNormalPlaceholderName = "Shader_NoneNormal.normal";

        internal static bool IsLegacyNeutralNormalPlaceholder(Texture texture)
            => texture != null && texture.width == 8 && texture.height == 8
                && string.Equals(texture.name, LegacyNeutralNormalPlaceholderName, StringComparison.Ordinal);

        internal static Texture Clone(Texture source)
        {
            if (source is not Texture2D sourceTexture || sourceTexture.isReadable)
                return UnityEngine.Object.Instantiate(source);

            RenderTexture previous = RenderTexture.active;
            RenderTexture staging = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                Graphics.Blit(sourceTexture, staging);
                RenderTexture.active = staging;
                var copy = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(staging);
            }
        }
    }

    /// <summary>Authoring-only persistence for Material Outfit Texture Entries and Figure Masks.</summary>
    internal static class ShapeSyncOutfitTextureAuthoring
    {
        internal readonly struct MaterialTextureInput
        {
            internal readonly string EntryName;
            internal readonly Texture Source;
            internal MaterialTextureInput(string entryName, Texture source) { EntryName = entryName; Source = source; }
        }

        internal readonly struct FigureMaskInput
        {
            internal readonly string FigureMaterialEntryName;
            internal readonly Texture Source;
            internal FigureMaskInput(string materialEntryName, Texture source) { FigureMaterialEntryName = materialEntryName; Source = source; }
        }

        internal static bool TrySaveMaterialOutfitTextures(string databaseAssetPath, string outfitIdentity,
            IReadOnlyList<MaterialTextureInput> inputs, out string diagnostic)
        {
            return TrySave(databaseAssetPath, outfitIdentity, inputs, null, out diagnostic);
        }

        internal static bool TrySaveFigureMasks(string databaseAssetPath, string outfitIdentity,
            IReadOnlyList<FigureMaskInput> inputs, out string diagnostic)
        {
            return TrySave(databaseAssetPath, outfitIdentity, null, inputs, out diagnostic);
        }

        private static bool TrySave(string databaseAssetPath, string outfitIdentity,
            IReadOnlyList<MaterialTextureInput> materialInputs, IReadOnlyList<FigureMaskInput> maskInputs, out string diagnostic)
        {
            diagnostic = null;
            bool materialMode = materialInputs != null;
            if (string.IsNullOrWhiteSpace(outfitIdentity) || materialMode == (maskInputs != null))
            { diagnostic = "Outfit Texture authoring requires exactly one target collection."; return false; }

            IEnumerable<Texture> sources = materialMode ? materialInputs.Select(input => input.Source) : maskInputs.Select(input => input.Source);
            if (sources.Any(source => source == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source))))
            { diagnostic = "Outfit Texture authoring requires persistent source Texture assets."; return false; }

            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, context) =>
            {
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry?.Outfits
                    .FirstOrDefault(value => value != null && value.Identity == outfitIdentity);
                if (outfit == null) throw new InvalidOperationException("Outfit was not found: " + outfitIdentity);
                if (materialMode && outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Material)
                    throw new InvalidOperationException("Material Outfit was not found: " + outfitIdentity);
                if (!materialMode && outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
                    throw new InvalidOperationException("Figure Mask requires a Mesh Outfit: " + outfitIdentity);

                IReadOnlyDictionary<string, string> existingResourceNames = materialMode
                    ? outfit.MaterialOutfitTextureEntries.Where(entry => entry != null)
                        .ToDictionary(entry => entry.EntryName, entry => entry.TextureResourceName, StringComparer.Ordinal)
                    : outfit.FigureMaskEntries.Where(entry => entry != null)
                        .ToDictionary(entry => entry.FigureMaterialEntryName, entry => entry.TextureResourceName, StringComparer.Ordinal);
                string[] oldNames = existingResourceNames.Values.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
                var oldNameSet = new HashSet<string>(oldNames, StringComparer.Ordinal);
                var nextEntryNames = new HashSet<string>(StringComparer.Ordinal);
                var nextResourceNames = new HashSet<string>(StringComparer.Ordinal);

                if (materialMode)
                {
                    var entries = new List<ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry>();
                    foreach (MaterialTextureInput input in materialInputs)
                    {
                        if (!ShapeSyncDatabaseRegistry.IsValidUserName(input.EntryName) || !nextEntryNames.Add(input.EntryName))
                            throw new InvalidOperationException("Material Outfit Texture Entry names must be distinct and contain no whitespace.");
                        string resourceName = existingResourceNames.TryGetValue(input.EntryName, out string existingResourceName)
                            ? existingResourceName : outfitIdentity + "_" + input.EntryName;
                        nextResourceNames.Add(resourceName);
                        UpsertResource(database.Registry, context, resourceName, input.Source, outfitIdentity,
                            ShapeSyncDatabaseRegistry.TextureResourceUsage.MaterialOutfit, oldNameSet);
                        entries.Add(new ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry(input.EntryName, resourceName));
                    }
                    if (!database.Registry.TrySetMaterialOutfitTextureEntries(outfitIdentity, entries, out string setDiagnostic))
                        throw new InvalidOperationException(setDiagnostic);
                }
                else
                {
                    var entries = new List<ShapeSyncDatabaseRegistry.FigureMaskEntry>();
                    foreach (FigureMaskInput input in maskInputs)
                    {
                        if (!database.Registry.ContainsMaterialEntryName(input.FigureMaterialEntryName) || !nextEntryNames.Add(input.FigureMaterialEntryName))
                            throw new InvalidOperationException("Figure Masks must target distinct existing Figure Material Entries.");
                        string resourceName = existingResourceNames.TryGetValue(input.FigureMaterialEntryName, out string existingResourceName)
                            ? existingResourceName : outfitIdentity + "_" + input.FigureMaterialEntryName + "_Mask";
                        nextResourceNames.Add(resourceName);
                        UpsertResource(database.Registry, context, resourceName, input.Source, outfitIdentity,
                            ShapeSyncDatabaseRegistry.TextureResourceUsage.FigureMask, oldNameSet);
                        entries.Add(new ShapeSyncDatabaseRegistry.FigureMaskEntry(input.FigureMaterialEntryName, resourceName));
                    }
                    if (!database.Registry.TrySetFigureMaskEntries(outfitIdentity, entries, out string setDiagnostic))
                        throw new InvalidOperationException(setDiagnostic);
                }

                foreach (string oldName in oldNames.Where(name => !nextResourceNames.Contains(name)))
                {
                    if (!database.Registry.TryRemoveTextureResource(oldName, out Texture removed, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic removeDiagnostic))
                        throw new InvalidOperationException(removeDiagnostic.ToString());
                    if (removed != null && AssetDatabase.GetAssetPath(removed) == databaseAssetPath) context.RemoveSubAsset(removed);
                }
            }, out diagnostic);
        }

        private static void UpsertResource(ShapeSyncDatabaseRegistry registry, ShapeSyncDatabaseTransaction.EditContext context,
            string resourceName, Texture source, string outfitIdentity, ShapeSyncDatabaseRegistry.TextureResourceUsage usage, ISet<string> oldNames)
        {
            ShapeSyncDatabaseRegistry.TextureResourceEntry existing = registry.TextureResources.FirstOrDefault(entry => entry != null && entry.LogicalName == resourceName);
            if (existing != null && !oldNames.Contains(resourceName))
                throw new InvalidOperationException("Texture resource logical name already exists: " + resourceName);
            if (existing != null && (existing.Owner.Scope != ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit
                || !string.Equals(existing.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal)
                || existing.Usage != usage))
                throw new InvalidOperationException("Existing Outfit Texture resource owner or usage does not match: " + resourceName);
            Texture copy = ShapeSyncEditorTextureUtility.Clone(source);
            copy.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(source)
                ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                : resourceName;
            context.AddSubAsset(copy);
            if (existing == null)
            {
                if (!registry.TryRegisterTextureResource(resourceName, copy, ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit(outfitIdentity), usage, out string diagnostic))
                    throw new InvalidOperationException(diagnostic);
            }
            else
            {
                Texture previous = existing.Texture;
                existing.SetTexture(copy);
                if (previous != null) context.RemoveSubAsset(previous);
            }
        }

    }
}
#endif
