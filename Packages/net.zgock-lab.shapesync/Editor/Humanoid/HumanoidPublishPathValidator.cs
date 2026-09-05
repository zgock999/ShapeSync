// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Validates Editor publish paths without creating assets or folders.</summary>
    public static class HumanoidPublishPathValidator
    {
        /// <summary>Converts an absolute folder selected by the Editor dialog into an existing Assets-relative folder.</summary>
        public static bool TryResolveOutputFolder(string absoluteFolder, out string assetFolder, out StackMachineDiagnostic diagnostic)
        {
            assetFolder = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(absoluteFolder)) return Reject("PublishOutputFolderRequired", "Pure Humanoid publish requires a selected output folder under Assets.", out diagnostic);
            try
            {
                string selected = Path.GetFullPath(absoluteFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string assets = Path.GetFullPath(UnityEngine.Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(selected, assets, StringComparison.OrdinalIgnoreCase)
                    && !selected.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !selected.StartsWith(assets + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return Reject("PublishOutputFolderOutsideAssets", "Pure Humanoid output folder must be under this project's Assets folder.", out diagnostic);

                string tail = selected.Length == assets.Length ? string.Empty : selected.Substring(assets.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                string relative = string.IsNullOrEmpty(tail) ? "Assets" : "Assets/" + tail;
                if (!Directory.Exists(selected))
                    return Reject("PublishOutputFolderRequired", "Pure Humanoid publish requires an existing output folder under Assets.", out diagnostic);
                // SaveFolderPanel can create a physical child directory under Assets without
                // synchronously importing it into AssetDatabase. Import the selected folder
                // before the asset-path validation used by the staging / Prefab transactions.
                if (relative != "Assets" && !AssetDatabase.IsValidFolder(relative))
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    if (!AssetDatabase.IsValidFolder(relative))
                        return Reject("PublishOutputFolderRequired", "Pure Humanoid publish requires an existing output folder under Assets.", out diagnostic);
                }
                assetFolder = relative;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PublishOutputFolderInvalid", "Pure Humanoid output folder could not be resolved.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Validates a VRM asset folder relative to the selected Pure Humanoid destination.</summary>
        public static bool TryValidateVrmRelativeFolder(string relativeFolder, out StackMachineDiagnostic diagnostic, bool requireNonEmpty = false)
        {
            diagnostic = null;
            string relative = (relativeFolder ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (requireNonEmpty && string.IsNullOrEmpty(relative))
                return Reject("VrmPublishRelativeFolderRequired", "VRM Asset Relative Folder is required when Transport VRM Physics is enabled.", out diagnostic);
            if (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(relative, "Assets", StringComparison.OrdinalIgnoreCase)
                || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || relative.Contains("/../") || relative.EndsWith("/..", StringComparison.Ordinal))
                return Reject("VrmPublishRelativeFolderInvalid", "VRM Asset Relative Folder must be relative to the selected destination and cannot contain Assets/ or ../.", out diagnostic);
            return true;
        }

        /// <summary>Validates the persistent Material/Texture/Mesh/Avatar dependency graph of a published Prefab.</summary>
        /// <remarks>Shared Shader and script infrastructure is intentionally outside the Pure Humanoid asset contract.</remarks>
        public static bool TryValidateOutputReferences(string prefabAssetPath, string outputFolder, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            string prefabPath = NormalizeAssetPath(prefabAssetPath);
            string folderPath = NormalizeAssetPath(outputFolder).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(prefabPath) || string.IsNullOrWhiteSpace(folderPath))
                return Reject("PublishOutputReferenceValidationInvalid", "Pure Humanoid output reference validation requires a Prefab path and output folder.", out diagnostic);
            if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
                return Reject("PublishOutputReferenceValidationInvalid", "Pure Humanoid output reference validation could not load the published Prefab.", out diagnostic, prefabPath);
            try
            {
                string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    string dependencyPath = NormalizeAssetPath(dependencies[i]);
                    // Third-party package assets are shared infrastructure declared by the consumer
                    // environment. ShapeSync's own package assets remain subject to output containment;
                    // package-owned resources such as UniVRM's default icon are not publish artifacts
                    // that can be copied into the output folder.
                    if (IsSharedPackageReference(dependencyPath)) continue;
                    UnityEngine.Object dependency = AssetDatabase.LoadMainAssetAtPath(dependencyPath);
                    if (!IsPureHumanoidReference(dependency)) continue;
                    if (!IsUnderFolder(dependencyPath, folderPath))
                        return Reject("PublishOutputReferenceOutsideFolder", "Pure Humanoid output references an asset outside its output folder.", out diagnostic, dependencyPath);
                }
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PublishOutputReferenceValidationFailed", "Pure Humanoid output reference validation failed.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Validates the staged Spec17 §1.3 names before the Prefab exists.</summary>
        internal static bool TryValidateOutputNaming(HumanoidPublishOutputContract contract, out StackMachineDiagnostic diagnostic)
        {
            return TryValidateOutputNaming(contract, null, out diagnostic);
        }

        /// <summary>Validates the staged Spec17 §1.3 names and the final folder Prefab name.</summary>
        internal static bool TryValidateOutputContract(string prefabAssetPath, HumanoidPublishOutputContract contract, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (contract == null) return Reject("PublishOutputContractMissing", "Pure Humanoid output contract validation requires the staging naming contract.", out diagnostic);
            string prefabPath = NormalizeAssetPath(prefabAssetPath);
            string outputFolder = NormalizeAssetPath(contract.OutputFolder).TrimEnd('/');
            string expectedPrefabPath = CombineAssetPath(outputFolder, contract.AssetPrefix + ".prefab");
            if (!string.Equals(prefabPath, expectedPrefabPath, StringComparison.Ordinal))
                return Reject("PublishPrefabNameInvalid", "Pure Humanoid Prefab must use the output folder name as its asset prefix.", out diagnostic, prefabPath);
            if (!TryValidateOutputNaming(contract, prefabPath, out diagnostic)) return false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return Reject("PublishPrefabAssetMissing", "Pure Humanoid output contract could not reload the published Prefab.", out diagnostic, prefabPath);
            return true;
        }

        private static bool TryValidateOutputNaming(HumanoidPublishOutputContract contract, string allowedPrefabPath, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (contract == null) return Reject("PublishOutputContractMissing", "Pure Humanoid output naming validation requires the staging naming contract.", out diagnostic);
            string outputFolder = NormalizeAssetPath(contract.OutputFolder).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
                return Reject("PublishOutputFolderRequired", "Pure Humanoid output naming validation requires an existing output folder.", out diagnostic, outputFolder);
            if (string.IsNullOrWhiteSpace(contract.AssetPrefix))
                return Reject("PublishOutputFolderNameRequired", "Pure Humanoid output naming validation requires an output folder name.", out diagnostic);

            var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
            string meshPath = NormalizeAssetPath(contract.MeshPath);
            string expectedMeshPath = CombineAssetPath(outputFolder, contract.AssetPrefix + ".asset");
            if (!TryValidateTypedPath(meshPath, expectedMeshPath, outputFolder, typeof(Mesh), expectedPaths, "PublishMeshNameInvalid", out diagnostic)) return false;

            if (!string.IsNullOrWhiteSpace(contract.AvatarPath))
            {
                string avatarPath = NormalizeAssetPath(contract.AvatarPath);
                string expectedAvatarPath = CombineAssetPath(outputFolder, contract.AssetPrefix + "_avatar.asset");
                if (!TryValidateTypedPath(avatarPath, expectedAvatarPath, outputFolder, typeof(Avatar), expectedPaths, "PublishAvatarNameInvalid", out diagnostic)) return false;
            }

            var materialPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < contract.Materials.Count; i++)
            {
                HumanoidPublishMaterialOutput output = contract.Materials[i];
                if (!output.MaterialId.IsValid)
                    return Reject("PublishMaterialNameInvalid", "Pure Humanoid output Material naming requires a valid MaterialId.", out diagnostic);
                string path = NormalizeAssetPath(output.AssetPath);
                string expected = CombineAssetPath(outputFolder, MaterialBaseName(contract.AssetPrefix, output.MaterialId) + ".mat");
                if (!TryValidateTypedPath(path, expected, outputFolder, typeof(Material), expectedPaths, "PublishMaterialNameInvalid", out diagnostic)) return false;
                if (!materialPaths.Add(path)) return Reject("PublishMaterialNameDuplicate", "Pure Humanoid output contains duplicate published Material paths.", out diagnostic, path);
            }

            // Atlas-owned BaseColor / Normal references are removed from the
            // individual collector when the candidate Material points at a page.
            // `contract.Textures` therefore contains only live non-Atlas maps
            // (for example shader-specific Emission / Matcap properties) and
            // pass-through materials that are intentionally outside the Schema.
            // Those assets remain required dependencies of the published
            // Material and must not be rejected merely because Atlas pages are
            // also present.

            var nextIndexByMaterial = new Dictionary<MaterialId, int>();
            var firstPathByTextureKey = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < contract.Textures.Count; i++)
            {
                HumanoidPublishTextureOutput output = contract.Textures[i];
                if (!output.MaterialId.IsValid || output.Index < 0 || string.IsNullOrWhiteSpace(output.OutputTextureKey))
                    return Reject("PublishTextureNameInvalid", "Pure Humanoid output Texture naming requires a valid MaterialId, index, and source identity.", out diagnostic);
                int expectedIndex = nextIndexByMaterial.TryGetValue(output.MaterialId, out int nextIndex) ? nextIndex : 0;
                if (output.Index != expectedIndex)
                    return Reject("PublishTextureIndexInvalid", "Pure Humanoid output Texture indices must be contiguous per MaterialId.", out diagnostic, output.Index.ToString());
                nextIndexByMaterial[output.MaterialId] = expectedIndex + 1;

                string path = NormalizeAssetPath(output.AssetPath);
                if (!IsUnderFolder(path, outputFolder) || !string.Equals(Path.GetExtension(path), ".png", StringComparison.Ordinal))
                    return Reject("PublishTextureExtensionInvalid", "Pure Humanoid output Texture assets must be PNG files under the output folder.", out diagnostic, path);
                if (firstPathByTextureKey.TryGetValue(output.OutputTextureKey, out string sharedPath))
                {
                    if (!string.Equals(path, sharedPath, StringComparison.Ordinal))
                        return Reject("PublishTextureShareInvalid", "A shared source Texture must retain the path chosen by its first published Material.", out diagnostic, path);
                }
                else
                {
                    string expected = CombineAssetPath(outputFolder, MaterialBaseName(contract.AssetPrefix, output.MaterialId) + "_" + output.Index + ".png");
                    if (!string.Equals(path, expected, StringComparison.Ordinal))
                        return Reject("PublishTextureNameInvalid", "Pure Humanoid output Texture must use its first-published MaterialId and contiguous index.", out diagnostic, path);
                    firstPathByTextureKey.Add(output.OutputTextureKey, path);
                }
                if (!TryValidateTypedPath(path, path, outputFolder, typeof(Texture2D), expectedPaths, "PublishTextureNameInvalid", out diagnostic)) return false;
            }

            var atlasKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < contract.AtlasTextures.Count; i++)
            {
                HumanoidPublishAtlasOutput output = contract.AtlasTextures[i];
                if (output.PageIndex < 0 || (output.Semantic != "basecolor" && output.Semantic != "normal"))
                    return Reject("PublishAtlasTextureNameInvalid", "Pure Humanoid Atlas Texture naming requires a valid page and semantic.", out diagnostic);
                string key = output.PageIndex + ":" + output.Semantic;
                if (!atlasKeys.Add(key)) return Reject("PublishAtlasTextureDuplicate", "Pure Humanoid output contains duplicate Atlas page Texture names.", out diagnostic, key);
                string path = NormalizeAssetPath(output.AssetPath);
                string expected = CombineAssetPath(outputFolder, contract.AssetPrefix + "_atlas" + output.PageIndex + "_" + output.Semantic + ".png");
                if (!TryValidateTypedPath(path, expected, outputFolder, typeof(Texture2D), expectedPaths, "PublishAtlasTextureNameInvalid", out diagnostic)) return false;
            }

            if (!string.IsNullOrWhiteSpace(allowedPrefabPath))
            {
                string prefabPath = NormalizeAssetPath(allowedPrefabPath);
                if (!IsUnderFolder(prefabPath, outputFolder) || !string.Equals(prefabPath, CombineAssetPath(outputFolder, contract.AssetPrefix + ".prefab"), StringComparison.Ordinal))
                    return Reject("PublishPrefabNameInvalid", "Pure Humanoid Prefab must use the output folder name as its asset prefix.", out diagnostic, prefabPath);
                expectedPaths.Add(prefabPath);
            }

            // The staging folder starts empty and VRM transport owns only a nested folder.
            // Reject any direct Pure Humanoid artifact that bypassed the contract naming plan.
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { outputFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (string.IsNullOrWhiteSpace(path) || string.Equals(path, outputFolder, StringComparison.Ordinal) || !string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), outputFolder, StringComparison.Ordinal)) continue;
                if (expectedPaths.Contains(path)) continue;
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                string extension = Path.GetExtension(path);
                if (IsPureHumanoidReference(asset) || extension == ".prefab" || extension == ".mat" || extension == ".asset" || extension == ".png")
                    return Reject("PublishOutputNameInvalid", "Pure Humanoid output contains an unplanned direct artifact.", out diagnostic, path);
            }
            return true;
        }

        private static bool TryValidateTypedPath(string actualPath, string expectedPath, string outputFolder, Type assetType, HashSet<string> expectedPaths, string code, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            string actual = NormalizeAssetPath(actualPath);
            string expected = NormalizeAssetPath(expectedPath);
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) return Reject(code, "Pure Humanoid output asset name or prefix does not match the Spec17 contract.", out diagnostic, actual);
            if (!IsUnderFolder(actual, outputFolder)) return Reject(code, "Pure Humanoid output asset is outside its output folder.", out diagnostic, actual);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(actual);
            if (asset == null || !assetType.IsInstanceOfType(asset)) return Reject(code, "Pure Humanoid output asset could not be reloaded with its contracted type.", out diagnostic, actual);
            expectedPaths.Add(actual);
            return true;
        }

        private static string MaterialBaseName(string assetPrefix, MaterialId materialId)
        {
            return string.IsNullOrEmpty(materialId.RegistryId)
                ? assetPrefix + "_" + materialId.EntryId
                : assetPrefix + "_" + materialId.RegistryId + "_" + materialId.EntryId;
        }

        private static string CombineAssetPath(string folder, string file) => NormalizeAssetPath(folder).TrimEnd('/') + "/" + file;

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string detail)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, detail: detail);
            return false;
        }

        private static bool IsPureHumanoidReference(UnityEngine.Object asset)
        {
            return asset is GameObject || asset is Material || asset is Texture
                || asset is Mesh || asset is Avatar;
        }

        private static string NormalizeAssetPath(string path) => (path ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');

        private static bool IsUnderFolder(string assetPath, string folderPath)
        {
            return string.Equals(assetPath, folderPath, StringComparison.Ordinal)
                || assetPath.StartsWith(folderPath + "/", StringComparison.Ordinal);
        }

        private static bool IsSharedPackageReference(string assetPath)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return false;
            return !IsUnderFolder(assetPath, "Packages/net.zgock-lab.shapesync")
                && !IsUnderFolder(assetPath, "Packages/net.zgock-lab.shapesync.vrm");
        }
    }
}
