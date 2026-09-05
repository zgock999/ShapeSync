// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Lowers Database-owned Shape declarations into independent Spec16 Template assets.</summary>
    internal static class ShapeSyncShapeGenerator
    {
        internal static Action<UnityEngine.Object, string> CreateAsset = AssetDatabase.CreateAsset;
        internal static Action<UnityEngine.Object, UnityEngine.Object> CopySerialized = EditorUtility.CopySerialized;
        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, out string diagnostic)
            => TryGenerate(database, rootPath, null, out diagnostic);

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, IReadOnlyCollection<string> additionalGeneratedPaths, out string diagnostic)
        {
            diagnostic = null;
            string backupFolder = null;
            string catalogPath = null;
            bool catalogExists = false;
            var backups = new List<KeyValuePair<string, string>>();
            if (database?.Registry == null) { diagnostic = "ShapeGenerateSnapshotInvalid: Database Registry is required."; return false; }
            if (string.IsNullOrWhiteSpace(rootPath) || (rootPath != "Assets" && !rootPath.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(rootPath))
            { diagnostic = "ShapeGenerateOutputPathInvalid: Output root must be an existing Assets folder."; return false; }
            // ShapeSync Spec20 §2.7 places ShapeSyncShapeTemplate assets directly
            // in the caller-selected output root.  The root is already the
            // category boundary; a generator-owned `Shapes` subfolder is not a
            // part of the output contract.
            string outputPath = rootPath.TrimEnd('/');
            try
            {
                ShapeSyncDatabaseRegistry.ShapeEntry[] shapes = database.Registry.Shapes.Where(value => value != null).ToArray();
                var outputIds = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < shapes.Length; index++)
                {
                    if (string.IsNullOrWhiteSpace(shapes[index].ShapeId) || !outputIds.Add(shapes[index].ShapeId))
                    { diagnostic = "ShapeGenerateSnapshotInvalid: Shape Ids must be unique."; return false; }
                    if (shapes[index].Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph
                        && !database.Registry.TryValidateShapePartsForGeneration(shapes[index].Parts, out string partsDiagnostic))
                    {
                        diagnostic = "ShapeGenerateInputInvalid: " + shapes[index].ShapeId + ": " + partsDiagnostic;
                        return false;
                    }
                }
                EnsureFolder(outputPath);
                catalogPath = ShapeSyncGenerateCatalog.GetPath(outputPath);
                if (!ShapeSyncGenerateCatalog.TryRead(outputPath, out List<string> previousPaths, out catalogExists, out string catalogDiagnostic))
                { diagnostic = catalogDiagnostic; return false; }
                // A newly-created output folder has no prior catalog and nothing to
                // clean.  Missing-catalog is only actionable when there are existing
                // non-meta files whose ownership cannot be reconstructed safely.
                diagnostic = catalogDiagnostic;
                if (diagnostic != null
                    && diagnostic.StartsWith("ShapeGenerateCatalogMissing", StringComparison.Ordinal)
                    && !ShapeSyncGenerateCatalog.HasNonMetaFiles(outputPath, additionalGeneratedPaths))
                    diagnostic = null;
                var nextPaths = new List<string>();
                foreach (ShapeSyncDatabaseRegistry.ShapeEntry shape in shapes)
                {
                    nextPaths.Add(outputPath + "/" + shape.ShapeId + ".asset");
                }
                if (additionalGeneratedPaths != null)
                {
                    foreach (string path in additionalGeneratedPaths.OrderBy(value => value, StringComparer.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                        { diagnostic = "ShapeGenerateSnapshotInvalid: Generated output paths must be project-relative Assets paths."; return false; }
                        if (!nextPaths.Contains(path, StringComparer.Ordinal)) nextPaths.Add(path);
                    }
                }
                backupFolder = outputPath + "/__ShapeSyncShapeBackup";
                var mutationPaths = previousPaths.Concat(nextPaths).Append(catalogPath).Distinct(StringComparer.Ordinal)
                    .Where(AssetDatabase.AssetPathExists).ToArray();
                if (mutationPaths.Length != 0)
                {
                    EnsureFolder(backupFolder);
                    foreach (string sourcePath in mutationPaths)
                    {
                        // Preserve the output-relative folder structure in the rollback copy.
                        // This keeps same-named assets from different folders distinct while
                        // retaining the original filename/object-name invariant Unity expects.
                        string relativePath = sourcePath.StartsWith(outputPath + "/", StringComparison.Ordinal)
                            ? sourcePath.Substring(outputPath.Length + 1)
                            : System.IO.Path.GetFileName(sourcePath);
                        string backupPath = backupFolder + "/" + relativePath;
                        string backupParent = System.IO.Path.GetDirectoryName(backupPath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(backupParent)) EnsureFolder(backupParent);
                        if (!AssetDatabase.CopyAsset(sourcePath, backupPath)) throw new InvalidOperationException("ShapeGenerateBackupFailed: " + sourcePath);
                        backups.Add(new KeyValuePair<string, string>(sourcePath, backupPath));
                    }
                }
                foreach (string stalePath in previousPaths.Where(path => !nextPaths.Contains(path, StringComparer.Ordinal)).ToArray())
                    if (AssetDatabase.AssetPathExists(stalePath) && !AssetDatabase.DeleteAsset(stalePath))
                        throw new InvalidOperationException("ShapeGenerateStaleDeleteFailed: " + stalePath);
                foreach (ShapeSyncDatabaseRegistry.ShapeEntry shape in shapes)
                {
                    ShapeSyncShapeTemplate template = CreateTemplate(shape);
                    template.ShapeId = shape.ShapeId;
                    template.Priority = shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? 0 : shape.Priority;
                    if (shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph) template.Tags.AddRange(shape.Tags);
                    if (template is MorphShapeTemplate morph) morph.Morphs.AddRange(shape.Morphs);
                    else if (template is SkinShapeTemplate skin) AddParts(database.Registry, shape, skin.Parts);
                    else if (template is HairShapeTemplate hair) AddParts(database.Registry, shape, hair.Parts);
                    else if (template is OutfitShapeTemplate outfit) AddParts(database.Registry, shape, outfit.Parts);
                    string path = outputPath + "/" + shape.ShapeId + ".asset";
                    PersistShapeTemplate(template, path);
                }
                ShapeSyncGenerateCatalog.Write(catalogPath, nextPaths);
                AssetDatabase.SaveAssets();
                if (backupFolder != null && AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.DeleteAsset(backupFolder);
                return true;
            }
            catch (Exception exception)
            {
                for (int index = backups.Count - 1; index >= 0; index--)
                {
                    RestoreBackedUpAsset(backups[index].Key, backups[index].Value);
                }
                if (!catalogExists && catalogPath != null && AssetDatabase.AssetPathExists(catalogPath))
                    AssetDatabase.DeleteAsset(catalogPath);
                if (backupFolder != null && AssetDatabase.IsValidFolder(backupFolder)) AssetDatabase.DeleteAsset(backupFolder);
                diagnostic = "ShapeGenerateUnexpected: " + exception.Message;
                return false;
            }
        }

        private static void PersistShapeTemplate(ShapeSyncShapeTemplate template, string path)
        {
            template.name = System.IO.Path.GetFileNameWithoutExtension(path);
            ShapeSyncShapeTemplate existing = AssetDatabase.LoadAssetAtPath<ShapeSyncShapeTemplate>(path);
            if (existing != null && existing.GetType() == template.GetType())
            {
                // Keep the existing .meta/GUID.  Generate replaces serialized contents,
                // not the asset identity referenced by downstream prefabs and bindings.
                CopySerialized(template, existing);
                existing.name = template.name;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                UnityEngine.Object.DestroyImmediate(template);
                return;
            }

            // A ShapeKind change changes the concrete ScriptableObject type and therefore
            // cannot be serialized into the old object.  Preserve the existing behavior for
            // that explicit type replacement; same-kind overwrites take the GUID-preserving
            // path above.
            if (AssetDatabase.LoadMainAssetAtPath(path) != null && !AssetDatabase.DeleteAsset(path))
                throw new InvalidOperationException("ShapeGenerateOverwriteDeleteFailed: " + path);
            CreateAsset(template, path);
        }

        private static void RestoreBackedUpAsset(string sourcePath, string backupPath)
        {
            UnityEngine.Object current = AssetDatabase.LoadMainAssetAtPath(sourcePath);
            UnityEngine.Object backup = AssetDatabase.LoadMainAssetAtPath(backupPath);
            if (current != null && backup != null && current.GetType() == backup.GetType()
                && sourcePath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                CopySerialized(backup, current);
                EditorUtility.SetDirty(current);
                AssetDatabase.SaveAssetIfDirty(current);
                return;
            }
            if (AssetDatabase.AssetPathExists(sourcePath)) AssetDatabase.DeleteAsset(sourcePath);
            AssetDatabase.CopyAsset(backupPath, sourcePath);
        }

        private static ShapeSyncShapeTemplate CreateTemplate(ShapeSyncDatabaseRegistry.ShapeEntry shape) => shape.Kind switch
        {
            ShapeSyncDatabaseRegistry.ShapeKind.Morph => ScriptableObject.CreateInstance<MorphShapeTemplate>(),
            ShapeSyncDatabaseRegistry.ShapeKind.Skin => ScriptableObject.CreateInstance<SkinShapeTemplate>(),
            ShapeSyncDatabaseRegistry.ShapeKind.Hair => ScriptableObject.CreateInstance<HairShapeTemplate>(),
            ShapeSyncDatabaseRegistry.ShapeKind.Outfit => ScriptableObject.CreateInstance<OutfitShapeTemplate>(),
            _ => throw new ArgumentOutOfRangeException()
        };

        private static void AddParts(ShapeSyncDatabaseRegistry registry, ShapeSyncDatabaseRegistry.ShapeEntry shape, List<ShapeEntry> output)
        {
            foreach (ShapeSyncDatabaseRegistry.ShapeEntryDefinition part in shape.Parts)
            {
                switch (part.Kind)
                {
                    case ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh:
                        var mesh = new MeshEntry { LogicalName = part.OutfitIdentity };
                        ShapeSyncDatabaseRegistry.OutfitEntry outfit = registry.Outfits.FirstOrDefault(value => value != null && value.Identity == part.OutfitIdentity);
                        foreach (ShapeSyncDatabaseRegistry.FigureMaskEntry mask in outfit?.FigureMaskEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureMaskEntry>())
                            mesh.Masks.Add(new MeshMaskEntry { ProxyEntryName = mask.FigureMaterialEntryName, MaskName = mask.TextureResourceName });
                        output.Add(mesh); break;
                    case ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture:
                        output.Add(new TextureEntry { RegistryId = part.RegistryId, ProxyEntry = part.ProxyEntry, LogicalName = part.TextureResourceName, UseColor = part.UseColorize, Color = part.Color }); break;
                    case ShapeSyncDatabaseRegistry.ShapeEntryKind.Color:
                        output.Add(new ColorEntry { RegistryId = part.RegistryId, ProxyEntry = part.ProxyEntry, Color = part.Color }); break;
                    case ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset:
                        output.Add(new UvsetEntry { RegistryId = part.RegistryId, ProxyEntry = part.ProxyEntry, ScaleX = part.ScaleX, ScaleY = part.ScaleY, OffsetX = part.OffsetX, OffsetY = part.OffsetY }); break;
                }
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }

    /// <summary>Owns the full Figure/Outfit/Shape Generate output catalog.</summary>
    internal static class ShapeSyncGenerateCatalog
    {
        internal const string FileName = "ShapeSyncShapeCatalog.txt";
        private const string Header = "# ShapeSync generated output catalog.\n# AUTOMATICALLY GENERATED. DO NOT EDIT.\n# Editing this file can make stale-output cleanup unsafe.\n# Generated assets (one project-relative path per line):\n";

        internal static string GetPath(string rootPath) => rootPath.TrimEnd('/') + "/" + FileName;

        internal static bool TryRead(string rootPath, out List<string> paths, out bool exists, out string diagnostic)
        {
            paths = new List<string>();
            diagnostic = null;
            string absolutePath = ToAbsolutePath(GetPath(rootPath));
            exists = File.Exists(absolutePath);
            if (!exists)
            {
                diagnostic = "ShapeGenerateCatalogMissing: Previous output cleanup skipped because the catalog is missing.";
                return true;
            }
            try
            {
                foreach (string rawLine in File.ReadAllLines(absolutePath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (!line.StartsWith("Assets/", StringComparison.Ordinal) || paths.Contains(line, StringComparer.Ordinal))
                    {
                        if (!line.StartsWith("Assets/", StringComparison.Ordinal))
                        { diagnostic = "ShapeGenerateCatalogInvalid: Catalog contains a non-project asset path."; return false; }
                        diagnostic = "ShapeGenerateCatalogInvalid: Catalog contains a duplicate asset path.";
                        return false;
                    }
                    paths.Add(line);
                }
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeGenerateCatalogInvalid: Catalog could not be read: " + exception.Message;
                return false;
            }
        }

        internal static string NormalizeDiagnostic(string rootPath, IReadOnlyCollection<string> currentGeneration, string catalogDiagnostic)
        {
            if (catalogDiagnostic != null
                && catalogDiagnostic.StartsWith("ShapeGenerateCatalogMissing", StringComparison.Ordinal)
                && !HasNonMetaFiles(rootPath, currentGeneration))
                return null;
            return catalogDiagnostic;
        }

        internal static bool HasNonMetaFiles(string assetPath, IReadOnlyCollection<string> generatedPaths)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            if (!Directory.Exists(absolutePath)) return false;
            var currentGeneration = new HashSet<string>(generatedPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            return Directory.EnumerateFiles(absolutePath, "*", SearchOption.AllDirectories)
                .Any(path =>
                {
                    if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
                    string projectRelative = ToProjectRelativePath(path);
                    return !currentGeneration.Contains(projectRelative);
                });
        }

        internal static void Write(string catalogPath, IReadOnlyList<string> paths)
        {
            File.WriteAllText(ToAbsolutePath(catalogPath), Header + string.Join("\n", paths) + "\n", new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string relative = Path.GetRelativePath(projectRoot, absolutePath);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
#endif
