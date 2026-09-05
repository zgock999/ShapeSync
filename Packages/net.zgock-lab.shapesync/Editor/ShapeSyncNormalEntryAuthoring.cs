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
    /// <summary>Commits the optional Material Entry × Base/FBM Normal matrix through Database Texture Entries.</summary>
    internal static class ShapeSyncNormalEntryAuthoring
    {
        internal readonly struct Assignment
        {
            internal Assignment(string materialEntryName, string shapeKey, Texture source)
                : this(materialEntryName, shapeKey, source, ShapeSyncDatabaseRegistry.TextureResourceOwner.FigureBase)
            {
            }

            internal Assignment(string materialEntryName, string shapeKey, Texture source, ShapeSyncDatabaseRegistry.TextureResourceOwner owner)
            {
                MaterialEntryName = materialEntryName;
                ShapeKey = shapeKey;
                Source = source;
                Owner = owner;
            }

            internal string MaterialEntryName { get; }
            internal string ShapeKey { get; }
            /// <summary>Null deliberately clears the optional matrix cell.</summary>
            internal Texture Source { get; }
            internal ShapeSyncDatabaseRegistry.TextureResourceOwner Owner { get; }
        }

        internal static bool TrySave(string databaseAssetPath, IReadOnlyList<string> figureNormalEntryMaterialNames,
            IReadOnlyList<Assignment> assignments, out string diagnostic)
        {
            diagnostic = null;
            if (figureNormalEntryMaterialNames == null || assignments == null) { diagnostic = "Normal authoring requires Figure Normal Entries and a matrix assignment list."; return false; }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase opened, out diagnostic)) return false;
            if (opened.Registry == null) { diagnostic = "Normal authoring requires a Database Registry."; return false; }

            var selected = new HashSet<string>(figureNormalEntryMaterialNames, StringComparer.Ordinal);
            if (selected.Count != figureNormalEntryMaterialNames.Count || selected.Any(string.IsNullOrWhiteSpace)
                || selected.Any(name => !opened.Registry.MaterialEntries.Any(entry => entry != null && entry.LogicalName == name)))
            { diagnostic = "Figure Normal Entries must be distinct existing Material Entries."; return false; }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Assignment assignment in assignments)
            {
                string key = assignment.MaterialEntryName + "\u001f" + assignment.ShapeKey;
                if (!keys.Add(key)) { diagnostic = "Normal matrix assignments must be unique."; return false; }
                if (assignment.Source != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(assignment.Source)))
                {
                    diagnostic = "Normal requires a persistent source Texture asset.";
                    return false;
                }
                // Use the Registry contract as the authoritative key validation without mutating it.
                bool material = opened.Registry.MaterialEntries.Any(entry => entry != null && entry.LogicalName == assignment.MaterialEntryName);
                bool shape = assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                    || opened.Registry.FigureAxes.Any(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && axis.Name == assignment.ShapeKey);
                if (!material || !shape || !selected.Contains(assignment.MaterialEntryName)) { diagnostic = "Normal requires a declared Figure Normal Entry and a Base or existing FBM key."; return false; }
            }

            foreach (string materialEntryName in selected)
            {
                bool hasPendingBase = assignments.Any(assignment => assignment.MaterialEntryName == materialEntryName
                    && assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                if (hasPendingBase)
                {
                    Assignment pendingBase = assignments.First(assignment => assignment.MaterialEntryName == materialEntryName
                        && assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                    if (pendingBase.Source != null) continue;
                    diagnostic = "Base Normal cannot be None. Remove the Figure Normal Entry to remove this Normal configuration.";
                    return false;
                }

                ShapeSyncDatabaseRegistry.NormalEntry savedBase = opened.Registry.NormalEntries.FirstOrDefault(entry => entry != null
                    && entry.MaterialEntryName == materialEntryName && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                if (savedBase != null && savedBase.Texture != null) continue;
                diagnostic = "Base Normal cannot be None. Remove the Figure Normal Entry to remove this Normal configuration.";
                return false;
            }

            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, _, context) =>
            {
                ApplyAssignments(database, databaseAssetPath, figureNormalEntryMaterialNames, assignments, context);
            }, out diagnostic);
        }

        /// <summary>Applies an already-validated Normal matrix inside an owning snapshot transaction.
        /// Database Textures are reused; external Textures are copied and registered before the Normal relation stores their logical name.</summary>
        internal static void ApplyAssignments(ShapeSyncDatabase database, string databaseAssetPath, IReadOnlyList<string> figureNormalEntryMaterialNames, IReadOnlyList<Assignment> assignments,
            ShapeSyncDatabaseTransaction.EditContext context)
        {
            var replaced = new List<Texture>();
            if (!database.Registry.TrySetFigureNormalEntries(figureNormalEntryMaterialNames, out Texture[] removedByRelation, out string relationDiagnostic))
                throw new InvalidOperationException(relationDiagnostic);
            replaced.AddRange(removedByRelation);
            foreach (Assignment assignment in assignments)
            {
                ShapeSyncDatabaseRegistry.NormalEntry existing = database.Registry.NormalEntries.FirstOrDefault(entry => entry != null
                    && entry.MaterialEntryName == assignment.MaterialEntryName && entry.ShapeKey == assignment.ShapeKey);
                if (existing != null && existing.Texture != null) replaced.Add(existing.Texture);
                Texture value = null;
                string textureResourceName = null;
                if (assignment.Source != null)
                {
                    ShapeSyncDatabaseRegistry.TextureResourceEntry existingResource = database.Registry.TextureResources
                        .FirstOrDefault(resource => resource != null && resource.Texture == assignment.Source);
                    if (existingResource != null)
                    {
                        value = existingResource.Texture;
                        textureResourceName = existingResource.LogicalName;
                    }
                    else
                    {
                        textureResourceName = GetUniqueTextureResourceName(database.Registry, GetNormalTextureResourceBaseName(assignment));
                        bool isDatabaseTexture = AssetDatabase.GetAssetPath(assignment.Source) == databaseAssetPath;
                        value = isDatabaseTexture ? assignment.Source : UnityEngine.Object.Instantiate(assignment.Source);
                        if (!isDatabaseTexture)
                        {
                            value.name = textureResourceName;
                            context.AddSubAsset(value);
                        }
                        if (!database.Registry.TryRegisterTextureResource(textureResourceName, value, assignment.Owner, out string registerDiagnostic))
                            throw new InvalidOperationException(registerDiagnostic);
                    }

                    ShapeSyncDatabaseRegistry.MaterialEntry material = database.Registry.MaterialEntries
                        .FirstOrDefault(entry => entry != null && entry.LogicalName == assignment.MaterialEntryName);
                    if (material == null) throw new InvalidOperationException("Normal requires an existing Material Entry.");
                    if (!material.TextureResourceNames.Contains(textureResourceName))
                    {
                        if (!database.Registry.TrySetMaterialEntryTextureResources(material.LogicalName,
                            material.TextureResourceNames.Concat(new[] { textureResourceName }).ToArray(), out string assignDiagnostic))
                            throw new InvalidOperationException(assignDiagnostic);
                    }
                }
                if (!database.Registry.TrySetNormalEntry(assignment.MaterialEntryName, assignment.ShapeKey, value, textureResourceName, out string setDiagnostic))
                    throw new InvalidOperationException(setDiagnostic);
            }
            foreach (Texture texture in replaced.Distinct())
            {
                // Reclaim only legacy Database-owned copies. A picked Texture is a shared source asset.
                if (!database.Registry.NormalEntries.Any(entry => entry != null && entry.Texture == texture)
                    && !database.Registry.TextureResources.Any(resource => resource != null && resource.Texture == texture)
                    && AssetDatabase.GetAssetPath(texture) == AssetDatabase.GetAssetPath(database)) context.RemoveSubAsset(texture);
            }
        }

        private static string GetUniqueTextureResourceName(ShapeSyncDatabaseRegistry registry, string baseName)
        {
            string candidate = baseName;
            int suffix = 2;
            while (registry.TextureResources.Any(resource => resource != null && resource.LogicalName == candidate)) candidate = baseName + "_" + suffix++;
            return candidate;
        }

        private static string GetNormalTextureResourceBaseName(Assignment assignment)
        {
            // Base keeps the established Entry_Base_Normal form.  An FBM is a Figure
            // variant and follows every other FBM-owned asset: FBM_Entry_*.  This uses
            // the structured shape key directly; it never infers ownership from a name.
            return assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                ? assignment.MaterialEntryName + "_" + ShapeSyncDatabaseRegistry.BaseShapeKey + "_Normal"
                : assignment.ShapeKey + "_" + assignment.MaterialEntryName + "_Normal";
        }
    }
}
#endif
