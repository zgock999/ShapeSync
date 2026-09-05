// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Commits explicit Mesh Outfit Normal cells without synthesizing missing Entry or shape-key relations.</summary>
    internal static class ShapeSyncOutfitNormalAuthoring
    {
        internal readonly struct Assignment
        {
            internal Assignment(string materialEntryName, string shapeKey, Texture source)
            { MaterialEntryName = materialEntryName; ShapeKey = shapeKey; Source = source; }
            internal string MaterialEntryName { get; }
            internal string ShapeKey { get; }
            internal Texture Source { get; }
        }

        internal static bool TrySave(string databaseAssetPath, string outfitIdentity, IReadOnlyList<string> declaredMaterialEntries,
            IReadOnlyList<Assignment> assignments, out string diagnostic)
        {
            diagnostic = null;
            if (declaredMaterialEntries == null || assignments == null) { diagnostic = "Outfit Normal authoring requires declarations and assignments."; return false; }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase database, out diagnostic)) return false;
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry?.Outfits.FirstOrDefault(item => item != null && item.Identity == outfitIdentity);
            if (outfit == null || outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Mesh) { diagnostic = "Mesh Outfit was not found: " + outfitIdentity; return false; }
            var declared = new HashSet<string>(declaredMaterialEntries, StringComparer.Ordinal);
            if (declared.Count != declaredMaterialEntries.Count || declared.Any(string.IsNullOrWhiteSpace)
                || declared.Any(name => !outfit.MaterialEntries.Any(entry => entry != null && entry.LogicalName == name)))
            { diagnostic = "Outfit Normal Entries must be distinct existing Include Material Entries."; return false; }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Assignment assignment in assignments)
            {
                if (!keys.Add(assignment.MaterialEntryName + "\u001f" + assignment.ShapeKey)
                    || !declared.Contains(assignment.MaterialEntryName)
                    || !outfit.AxisFigures.Any(axis => axis != null && axis.ShapeKey == assignment.ShapeKey))
                { diagnostic = "Outfit Normal assignments require declared Include Entries and imported Base or FBM keys."; return false; }
                if (assignment.Source == null) { diagnostic = "Outfit Normal cannot be None. Remove the Outfit Normal Entry to remove this Normal configuration."; return false; }
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(assignment.Source))) { diagnostic = "Outfit Normal requires a persistent source Texture asset."; return false; }
            }
            // The Detail may submit only changed cells, but its declared Normal Entry
            // is never valid without a Base relation.  Do not let a sparse service
            // caller bypass the same invariant by omitting the Base assignment.
            foreach (string materialEntryName in declared)
            {
                Assignment? pendingBase = assignments.Where(assignment => assignment.MaterialEntryName == materialEntryName
                    && assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Select(assignment => (Assignment?)assignment).FirstOrDefault();
                if (pendingBase.HasValue)
                {
                    if (pendingBase.Value.Source != null) continue;
                }
                else if (outfit.NormalEntries.Any(entry => entry != null && entry.MaterialEntryName == materialEntryName
                    && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey && entry.Texture != null)) continue;
                diagnostic = "Base Outfit Normal cannot be None. Remove the Outfit Normal Entry to remove this Normal configuration.";
                return false;
            }
            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (contents, _, context) =>
            {
                ShapeSyncDatabaseRegistry.OutfitEntry currentOutfit = contents.Registry.Outfits.First(item => item != null && item.Identity == outfitIdentity);
                ShapeSyncDatabaseRegistry.OutfitNormalEntry[] existingNormalEntries = currentOutfit.NormalEntries.Where(entry => entry != null).ToArray();
                var replacedResourceNames = existingNormalEntries
                    .Where(entry => entry != null && assignments.Any(assignment => assignment.MaterialEntryName == entry.MaterialEntryName && assignment.ShapeKey == entry.ShapeKey))
                    .Select(entry => entry.TextureResourceName).ToList();
                if (!contents.Registry.TrySetOutfitNormalDeclarations(outfitIdentity, declaredMaterialEntries, out Texture[] ignoredRemovedTextures, out string declarationDiagnostic))
                    throw new InvalidOperationException(declarationDiagnostic);
                replacedResourceNames.AddRange(existingNormalEntries
                    .Where(entry => !declared.Contains(entry.MaterialEntryName)).Select(entry => entry.TextureResourceName));
                foreach (Assignment assignment in assignments)
                {
                    string resourceName;
                    Texture texture;
                    ShapeSyncDatabaseRegistry.TextureResourceEntry resource = contents.Registry.TextureResources.FirstOrDefault(entry => entry != null && entry.Texture == assignment.Source);
                    bool databaseOwned = AssetDatabase.GetAssetPath(assignment.Source) == databaseAssetPath;
                    string sourceAssetGuid = null;
                    long sourceAssetLocalFileId = 0;
                    ShapeSyncDatabaseRegistry.TextureResourceOwner owner = ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit(outfitIdentity, assignment.ShapeKey);
                    if (!databaseOwned)
                    {
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assignment.Source, out sourceAssetGuid, out sourceAssetLocalFileId)
                            || string.IsNullOrWhiteSpace(sourceAssetGuid))
                            throw new InvalidOperationException("Outfit Normal source Texture requires a persistent GUID/local-id identity.");
                        resource ??= contents.Registry.FindTextureResourceByImportSource(owner, sourceAssetGuid, sourceAssetLocalFileId);
                    }
                    if (resource != null)
                    {
                        if (resource.Owner.Scope != ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit
                            || resource.Owner.OutfitIdentity != outfitIdentity || resource.Owner.SourceShapeKey != assignment.ShapeKey)
                            throw new InvalidOperationException("Outfit Normal Texture is owned by a different aggregation owner.");
                        resourceName = resource.LogicalName;
                        texture = resource.Texture;
                    }
                    else
                    {
                        resourceName = UniqueName(contents.Registry, assignment.ShapeKey + "_" + assignment.MaterialEntryName + "_Normal");
                        texture = databaseOwned ? assignment.Source : UnityEngine.Object.Instantiate(assignment.Source);
                        if (!databaseOwned) { texture.name = resourceName; context.AddSubAsset(texture); }
                        if (!contents.Registry.TryRegisterTextureResource(resourceName, texture, owner,
                            ShapeSyncDatabaseRegistry.TextureResourceUsage.General, sourceAssetGuid, sourceAssetLocalFileId, out string registerDiagnostic))
                            throw new InvalidOperationException(registerDiagnostic);
                    }
                    if (!contents.Registry.TrySetOutfitNormalEntry(outfitIdentity, assignment.MaterialEntryName, assignment.ShapeKey, texture, resourceName, out string setDiagnostic))
                        throw new InvalidOperationException(setDiagnostic);
                }
                foreach (Texture texture in contents.Registry.RemoveUnreferencedOutfitNormalTextureResources(replacedResourceNames))
                    if (texture != null && AssetDatabase.GetAssetPath(texture) == databaseAssetPath) context.RemoveSubAsset(texture);
            }, out diagnostic);
        }

        private static string UniqueName(ShapeSyncDatabaseRegistry registry, string baseName)
        {
            string candidate = baseName; int suffix = 2;
            while (registry.TextureResources.Any(entry => entry != null && entry.LogicalName == candidate)) candidate = baseName + "_" + suffix++;
            return candidate;
        }
    }
}
#endif
