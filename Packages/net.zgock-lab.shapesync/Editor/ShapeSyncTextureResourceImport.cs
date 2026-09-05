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
    /// <summary>Registers already Database-owned Material Textures as logical resources.</summary>
    internal static class ShapeSyncTextureResourceImport
    {
        internal static bool TryRegisterExistingMaterialTextures(string databaseAssetPath, out string diagnostic)
        {
            diagnostic = null;
            return ShapeSyncDatabaseTransaction.TryEditStructure(databaseAssetPath, (database, _) =>
            {
                RegisterExistingMaterialTextures(database, databaseAssetPath);
            }, out diagnostic);
        }

        /// <summary>
        /// Registers the Database-owned Texture copies created by Figure import before
        /// Material Entries are authored.  This deliberately has no entry assignment:
        /// the subsequent Material Entry transaction attaches these already-defined
        /// abstract Textures to its entries.
        /// </summary>
        internal static void RegisterFigureTextures(ShapeSyncDatabase database, string databaseAssetPath, IEnumerable<Material> materials)
        {
            if (database == null || database.Registry == null || string.IsNullOrEmpty(databaseAssetPath) || materials == null)
                throw new InvalidOperationException("Figure Texture registration requires an open Database Prefab and imported Materials.");

            var registered = new HashSet<Texture>();
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.LogicalName) || resource.Texture == null || AssetDatabase.GetAssetPath(resource.Texture) != databaseAssetPath)
                    throw new InvalidOperationException("Texture resource registry contains an external or invalid Texture.");
                registered.Add(resource.Texture);
            }

            int nextName = database.Registry.TextureResources.Count;
            foreach (Material material in materials)
            {
                if (material == null || AssetDatabase.GetAssetPath(material) != databaseAssetPath)
                    throw new InvalidOperationException("Figure Material is not owned by the Database.");
                foreach (Texture texture in ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(material))
                {
                    if (!registered.Add(texture)) continue;
                    if (AssetDatabase.GetAssetPath(texture) != databaseAssetPath)
                        throw new InvalidOperationException("Figure Material Texture is not owned by the Database.");

                    string resourceName;
                    do { resourceName = "Texture-" + nextName++; }
                    while (database.Registry.TextureResources.Any(resource => resource != null && resource.LogicalName == resourceName));
                    if (!database.Registry.TryRegisterTextureResource(resourceName, texture, out string registerDiagnostic))
                        throw new InvalidOperationException(registerDiagnostic);
                }
            }
        }

        /// <summary>Registers material Textures inside an already-open Database transaction. Throws on an invalid ownership contract.</summary>
        internal static void RegisterExistingMaterialTextures(ShapeSyncDatabase database, string databaseAssetPath)
        {
            if (database == null || database.Registry == null || string.IsNullOrEmpty(databaseAssetPath)) throw new InvalidOperationException("Texture resource registration requires an open Database Prefab.");
            var namesByTexture = new Dictionary<Texture, string>();
            int nextName = database.Registry.TextureResources.Count;
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.LogicalName) || resource.Texture == null || AssetDatabase.GetAssetPath(resource.Texture) != databaseAssetPath)
                    throw new InvalidOperationException("Texture resource registry contains an external or invalid Texture.");
                namesByTexture.Add(resource.Texture, resource.LogicalName);
            }
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries)
            {
                if (entry == null || entry.Material == null) throw new InvalidOperationException("Material Entry is invalid.");
                var resourceNames = new List<string>();
                foreach (Texture texture in ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(entry.Material))
                {
                    if (AssetDatabase.GetAssetPath(texture) != databaseAssetPath) throw new InvalidOperationException("Material Entry Texture is not owned by the Database: " + entry.LogicalName);
                    if (!namesByTexture.TryGetValue(texture, out string resourceName))
                    {
                        do { resourceName = "Texture-" + nextName++; }
                        while (database.Registry.TextureResources.Any(resource => resource != null && resource.LogicalName == resourceName));
                        if (!database.Registry.TryRegisterTextureResource(resourceName, texture, out string registerDiagnostic)) throw new InvalidOperationException(registerDiagnostic);
                        namesByTexture.Add(texture, resourceName);
                    }
                    if (!resourceNames.Contains(resourceName)) resourceNames.Add(resourceName);
                }
                if (!database.Registry.TrySetMaterialEntryTextureResources(entry.LogicalName, resourceNames, out string assignmentDiagnostic)) throw new InvalidOperationException(assignmentDiagnostic);
            }
        }

        /// <summary>Applies the Figure/Material Entry naming convention once, keeping shared resources singular.</summary>
        internal static void RenameMaterialTexturesForFigure(ShapeSyncDatabase database)
        {
            if (database == null || database.Registry == null) throw new InvalidOperationException("Texture resource rename requires one Base Figure.");
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figure, out string diagnostic) || figure == null)
                throw new InvalidOperationException(diagnostic ?? "Texture resource rename requires one Base Figure.");
            var assigned = new HashSet<Texture>();
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries)
            {
                if (entry == null) throw new InvalidOperationException("Material Entry is invalid.");
                int textureIndex = 0;
                foreach (string currentName in entry.TextureResourceNames)
                {
                    ShapeSyncDatabaseRegistry.TextureResourceEntry resource = database.Registry.TextureResources.FirstOrDefault(item => item != null && item.LogicalName == currentName);
                    if (resource == null) throw new InvalidOperationException("Material Entry references an unknown Texture resource.");
                    if (!assigned.Add(resource.Texture)) continue; // A shared resource belongs to the first registry Entry.
                    string candidate = ShapeSyncEntryAssetNaming.GetTextureName(figure.Name, entry.LogicalName, textureIndex++);
                    string nextName = candidate;
                    int collision = 1;
                    while (database.Registry.TextureResources.Any(resource => resource != null && resource.LogicalName == nextName && resource.LogicalName != currentName)) nextName = candidate + "_" + collision++;
                    if (!database.Registry.TryRenameTextureResource(currentName, nextName, out string renameDiagnostic)) throw new InvalidOperationException(renameDiagnostic);
                }
            }
        }
    }
}
#endif
