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
    /// <summary>Commits Texture Detail drafts as one Database snapshot transaction.</summary>
    internal static class ShapeSyncTextureResourceAuthoring
    {
        internal readonly struct Rename
        {
            internal readonly string CurrentName;
            internal readonly string NextName;
            internal Rename(string currentName, string nextName) { CurrentName = currentName; NextName = nextName; }
        }

        internal readonly struct Addition
        {
            internal readonly string Name;
            internal readonly Texture Source;
            internal Addition(string name, Texture source) { Name = name; Source = source; }
        }

        internal readonly struct Removal
        {
            internal readonly string Name;
            internal Removal(string name) { Name = name; }
        }

        internal static bool TrySave(string databaseAssetPath, IReadOnlyList<Rename> renames, IReadOnlyList<Addition> additions, out string diagnostic)
        {
            return TrySave(databaseAssetPath, renames, additions, Array.Empty<Removal>(), out diagnostic);
        }

        internal static bool TrySave(string databaseAssetPath, IReadOnlyList<Rename> renames, IReadOnlyList<Addition> additions, IReadOnlyList<Removal> removals, out string diagnostic)
        {
            diagnostic = null;
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase opened, out diagnostic)) return false;
            if (opened.Registry == null) { diagnostic = "ShapeSync Database Texture authoring requires a Registry."; return false; }
            foreach (Addition addition in additions ?? Array.Empty<Addition>())
            {
                if (string.IsNullOrWhiteSpace(addition.Name) || addition.Source == null)
                { diagnostic = "Add New Texture requires both a Texture Name and a Texture."; return false; }
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(addition.Source)))
                { diagnostic = "Add New Texture requires a persistent source Texture asset."; return false; }
            }

            var nextNames = new HashSet<string>(StringComparer.Ordinal);
            var currentNames = new HashSet<string>(opened.Registry.TextureResources.Where(entry => entry != null).Select(entry => entry.LogicalName), StringComparer.Ordinal);
            var removedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Removal removal in removals ?? Array.Empty<Removal>())
            {
                if (string.IsNullOrWhiteSpace(removal.Name) || !currentNames.Contains(removal.Name) || !removedNames.Add(removal.Name))
                { diagnostic = "Texture resource removal is invalid."; return false; }
            }
            foreach (Rename rename in renames ?? Array.Empty<Rename>())
            {
                if (!currentNames.Contains(rename.CurrentName) || removedNames.Contains(rename.CurrentName) || string.IsNullOrWhiteSpace(rename.NextName))
                { diagnostic = "Texture resource rename is invalid."; return false; }
                if (!nextNames.Add(rename.NextName)) { diagnostic = "Texture resource names must be unique."; return false; }
            }
            foreach (string current in currentNames)
            {
                if (!removedNames.Contains(current) && !(renames ?? Array.Empty<Rename>()).Any(rename => rename.CurrentName == current) && !nextNames.Add(current))
                { diagnostic = "Texture resource names must be unique."; return false; }
            }
            foreach (Addition addition in additions ?? Array.Empty<Addition>()) if (!nextNames.Add(addition.Name)) { diagnostic = "Texture resource names must be unique."; return false; }

            var copies = new List<Texture>();
            try
            {
                return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, context) =>
                {
                    foreach (Removal removal in removals ?? Array.Empty<Removal>())
                    {
                        if (!database.Registry.TryRemoveTextureResource(removal.Name, out Texture removed, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic removalDiagnostic))
                            throw new InvalidOperationException(removalDiagnostic.ToString());
                        if (removed != null && AssetDatabase.GetAssetPath(removed) == databaseAssetPath) context.RemoveSubAsset(removed);
                    }
                    foreach (Rename rename in renames ?? Array.Empty<Rename>())
                    {
                        if (!database.Registry.TryRenameTextureResource(rename.CurrentName, rename.NextName, out string renameDiagnostic))
                            throw new InvalidOperationException(renameDiagnostic);
                    }
                    foreach (Addition addition in additions ?? Array.Empty<Addition>())
                    {
                        Texture copy = UnityEngine.Object.Instantiate(addition.Source);
                        copy.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(addition.Source)
                            ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                            : addition.Name;
                        copies.Add(copy);
                        context.AddSubAsset(copy);
                        if (!database.Registry.TryRegisterTextureResource(addition.Name, copy, out string addDiagnostic))
                            throw new InvalidOperationException(addDiagnostic);
                    }
                }, out diagnostic);
            }
            finally
            {
                if (!string.IsNullOrEmpty(diagnostic)) foreach (Texture copy in copies) if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        /// <summary>Commits rename-only Texture registry edits through the one-pass direct path.</summary>
        internal static bool TryRenameDirect(ShapeSyncDatabase database, IReadOnlyList<Rename> renames, out string diagnostic)
        {
            diagnostic = null;
            if (database == null || database.Registry == null) { diagnostic = "ShapeSync Database Texture authoring requires a Registry."; return false; }
            if (renames == null || renames.Count == 0) { diagnostic = "Texture resource rename requires at least one Entry."; return false; }
            var currentNames = new HashSet<string>(database.Registry.TextureResources.Where(entry => entry != null).Select(entry => entry.LogicalName), StringComparer.Ordinal);
            var nextNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rename rename in renames)
            {
                if (!currentNames.Contains(rename.CurrentName) || string.IsNullOrWhiteSpace(rename.NextName) || !nextNames.Add(rename.NextName))
                { diagnostic = "Texture resource rename is invalid."; return false; }
            }
            foreach (string current in currentNames)
                if (!renames.Any(rename => rename.CurrentName == current) && !nextNames.Add(current))
                { diagnostic = "Texture resource names must be unique."; return false; }
            return ShapeSyncDatabaseDirectEdit.TryEdit(database, "Rename ShapeSync Texture Resources",
                (ShapeSyncDatabaseRegistry registry, out string detail) =>
                {
                    var temporaryNames = new List<Rename>();
                    for (int index = 0; index < renames.Count; index++)
                    {
                        Rename rename = renames[index];
                        if (string.Equals(rename.CurrentName, rename.NextName, StringComparison.Ordinal)) continue;
                        string temporary = "__ShapeSyncTextureRename_" + index;
                        while (registry.TextureResources.Any(entry => entry != null && entry.LogicalName == temporary)) temporary += "_";
                        if (!registry.TryRenameTextureResource(rename.CurrentName, temporary, out string temporaryDiagnostic)) { detail = temporaryDiagnostic; return false; }
                        temporaryNames.Add(new Rename(temporary, rename.NextName));
                    }
                    foreach (Rename rename in temporaryNames)
                        if (!registry.TryRenameTextureResource(rename.CurrentName, rename.NextName, out string renameDiagnostic)) { detail = renameDiagnostic; return false; }
                    detail = null;
                    return true;
                }, out diagnostic);
        }
    }
}
#endif
