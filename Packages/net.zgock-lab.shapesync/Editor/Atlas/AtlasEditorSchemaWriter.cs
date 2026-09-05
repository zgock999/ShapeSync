// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using UnityEditor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor.Atlas
{
    /// <summary>Persists only verified Atlas Editor input as an input-only <see cref="AtlasSchema"/> asset.</summary>
    public static class AtlasEditorSchemaWriter
    {
        /// <summary>Creates one Atlas Schema asset from the current successful Dry Run state.</summary>
        public static bool TryCreateSchemaAsset(AtlasEditorState state, string assetPath, out AtlasSchema schema, out StackMachineDiagnostic diagnostic)
        {
            schema = null;
            if (state == null || !state.CanGenerate || state.Snapshot == null || state.LayoutPreview == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorVerificationRequired", "Atlas Editor requires a successful Dry Run before saving a Schema.");
                return false;
            }
            if (!TryNormalizeNewAssetPath(assetPath, out string normalizedPath) || AssetDatabase.LoadMainAssetAtPath(normalizedPath) != null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorAssetPathInvalid", "Atlas Editor requires a new .asset path under Assets.", detail: assetPath ?? string.Empty);
                return false;
            }
            AtlasSchemaDocument document = AtlasEditorValidationService.CreateDocument(state);
            schema = UnityEngine.ScriptableObject.CreateInstance<AtlasSchema>();
            if (!schema.TrySetDocument(document, out diagnostic)) { UnityEngine.Object.DestroyImmediate(schema); schema = null; return false; }
            try
            {
                AssetDatabase.CreateAsset(schema, normalizedPath);
                AssetDatabase.SaveAssets();
                if (AssetDatabase.LoadAssetAtPath<AtlasSchema>(normalizedPath) == schema) { diagnostic = null; return true; }
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorAssetSaveFailed", "Atlas Editor could not save the Schema asset.", detail: exception.Message);
                UnityEngine.Object.DestroyImmediate(schema);
                schema = null;
                return false;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorAssetSaveFailed", "Atlas Editor could not save the Schema asset.", detail: normalizedPath);
            UnityEngine.Object.DestroyImmediate(schema);
            schema = null;
            return false;
        }

        private static bool TryNormalizeNewAssetPath(string assetPath, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return false;
            if (Path.IsPathRooted(assetPath)) return false;
            string assetsRoot = Path.GetFullPath(UnityEngine.Application.dataPath);
            string projectRoot = Directory.GetParent(assetsRoot).FullName;
            string fullPath;
            try { fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath)); }
            catch (Exception) { return false; }
            string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(fullPath))) return false;
            normalizedPath = "Assets" + fullPath.Substring(assetsRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
            return true;
        }
    }
}
