// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync
{
    /// <summary>
    /// Resolves package-owned test inputs without embedding project-relative Assets paths
    /// in the package test assemblies, and provides a consumer-project staging root for
    /// generated test assets.
    /// </summary>
    public static class ShapeSyncTestAssetPaths
    {
        public const string AssetsRoot = "Assets";
        public static string AssetsPrefix => AssetsRoot + "/";
        public const string ConsumerTempRoot = AssetsRoot + "/ShapeSyncTestTemp";

        // Compile-time roots keep existing test-local path composition valid while the
        // physical location remains a consumer-project temp folder.
        public const string Spec10PcmSlotRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/Generated/Spec10PcmSlotTest";
        public const string Spec17ControllerStageRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_ControllerStage";
        public const string Spec17WindowOutputRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_WindowOutput";
        public const string Spec17StagingRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_Staging";
        public const string Spec17TexturePublishRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_TexturePublish";
        public const string Spec17VrmStageRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/VrmIntegration/__Spec17_6_VrmStage";
        public const string Spec17VrmPartialRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/VrmIntegration/__Spec17_6_VrmPartial";
        public const string Spec17VrmMissingInstanceRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/VrmIntegration/__Spec17_6_VrmMissingInstance";
        public const string Spec17VrmSaveExceptionRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/VrmIntegration/__Spec17_6_VrmSaveException";
        public const string Spec18AtlasPageStagerRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec18/__AtlasPageStager";
        public const string Spec18AtlasPublishReadbackRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec18/__AtlasPublishReadback";
        public const string Spec18AtlasCompilerBakePhaseRoot = ConsumerTempRoot + "/zgock/ShapeSync/Tests/EditMode/Spec18/__AtlasCompilerBakePhase";
        public const string Spec18AtlasEditorCandidateRoot = ConsumerTempRoot + "/__Spec18AtlasEditorCandidateTests";
        public const string Spec18AtlasEditorWindowStateRoot = ConsumerTempRoot + "/__Spec18AtlasEditorWindowStateTests";
        public const string Spec19HotBakeBuildDriverRoot = ConsumerTempRoot + "/__Spec19_4_HotBakeBuildDriverTests";
        public const string Spec19HotBakeDriverPlayModeRoot = ConsumerTempRoot + "/__Spec19_4_HotBakeDriverPlayModeTests";
        public const string Spec20DatabaseAssetRoot = ConsumerTempRoot + "/__Spec20_1_ShapeSyncDatabaseAssetTests";
        public const string Spec20DatabaseTransactionRoot = ConsumerTempRoot + "/__Spec20_1_ShapeSyncDatabaseTransactionTests";
        public const string Spec20DatabaseWindowRoot = ConsumerTempRoot + "/__Spec20_2_ShapeSyncDatabaseWindowTests";
        public const string Spec20FigureImportRoot = ConsumerTempRoot + "/__Spec20_3_ShapeSyncFigureImportTests";
        public const string Spec20MeshOutfitImportRoot = ConsumerTempRoot + "/__Spec20_7_ShapeSyncMeshOutfitImportTests";
        public const string Spec20FigureExportRoot = ConsumerTempRoot + "/__Spec20_7_ShapeSyncDatabaseFigureExportTests";
        public const string Spec20DiagnosticToolRoot = ConsumerTempRoot + "/__Spec20_9_DiagnosticToolTests";
        public const string Spec20SlimFixtureRoot = ConsumerTempRoot + "/__Spec20_9_SlimFixture";
        public const string Spec21OptionalFeatureRoot = ConsumerTempRoot + "/__Spec21_OptionalFeatureAdmissionTests";
        public const string Spec21VrmRegistryRoot = ConsumerTempRoot + "/__Spec21_VrmRegistryTests";
        public const string Spec20DatabaseAssetMissingFolder = AssetsRoot + "/__Spec20_1_MissingFolder";
        public const string Spec20DatabaseWindowMissingFolder = AssetsRoot + "/__Spec20_2_MissingFolder";

        private const string TextureStackMachineComputeGuid = "9b3984bbd2b6468cbd6bc33d76df21d1";
        private const string NormalTextureStackMachineComputeGuid = "60c577b8d7a146a3bd3de517353d9e78";
        private const string TextureStackMachineHostPrefabGuid = "48ed99d930944913842fea26e5eb2a2c";

        public static string TextureStackMachineComputePath => ResolvePackageAssetPath(TextureStackMachineComputeGuid);
        public static string NormalTextureStackMachineComputePath => ResolvePackageAssetPath(NormalTextureStackMachineComputeGuid);
        public static string TextureStackMachineHostPrefabPath => ResolvePackageAssetPath(TextureStackMachineHostPrefabGuid);

        /// <summary>
        /// Converts a logical or legacy Assets-relative test path to a consumer-owned path.
        /// The returned path is always below ConsumerTempRoot and its parent folders are
        /// created when running in the Unity Editor.
        /// </summary>
        public static string ConsumerAssetPath(string logicalPath)
        {
            if (string.IsNullOrWhiteSpace(logicalPath))
                throw new ArgumentException("A logical test asset path is required.", nameof(logicalPath));

            string normalized = logicalPath.Replace('\\', '/').Trim();
            if (string.Equals(normalized, AssetsRoot, StringComparison.OrdinalIgnoreCase))
                normalized = string.Empty;
            else if (normalized.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(AssetsPrefix.Length);

            normalized = normalized.TrimStart('/');
            if (normalized.Length == 0 || normalized == "." || normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal))
                throw new ArgumentException("The logical test asset path must name a child of the consumer temp root.", nameof(logicalPath));

            string result = ConsumerTempRoot + "/" + normalized;
#if UNITY_EDITOR
            EnsureParentFolder(result);
#endif
            return result;
        }

        /// <summary>Returns a consumer-owned folder path and creates that complete folder chain.</summary>
        public static string ConsumerFolderPath(string logicalPath)
        {
            string result = ConsumerAssetPath(logicalPath);
#if UNITY_EDITOR
            EnsureFolder(result);
#endif
            return result;
        }

        /// <summary>Builds an intentionally Assets-rooted invalid input for path validation tests.</summary>
        public static string InvalidAssetPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A relative path is required.", nameof(relativePath));
            return AssetsPrefix + relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>Builds an intentionally parent-traversing input for path validation tests.</summary>
        public static string TraversalAssetPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A relative path is required.", nameof(relativePath));
            return AssetsRoot + "/../" + relativePath.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>Maps an Assets-relative path to its consumer project's filesystem path.</summary>
        public static string AssetFileSystemPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An asset path is required.", nameof(assetPath));

            string normalized = assetPath.Replace('\\', '/').Trim();
            if (string.Equals(normalized, AssetsRoot, StringComparison.OrdinalIgnoreCase))
                return UnityEngine.Application.dataPath;
            if (!normalized.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The asset path must be below the Assets folder.", nameof(assetPath));

            return System.IO.Path.Combine(
                UnityEngine.Application.dataPath,
                normalized.Substring(AssetsPrefix.Length).Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public static void EnsureConsumerTempRoot()
        {
#if UNITY_EDITOR
            EnsureFolder(ConsumerTempRoot);
#endif
        }

        private static string ResolvePackageAssetPath(string guid)
        {
#if UNITY_EDITOR
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("ShapeSync test asset GUID could not be resolved: " + guid);
            return path;
#else
            throw new InvalidOperationException("ShapeSync package test asset paths require the Unity Editor: " + guid);
#endif
        }

#if UNITY_EDITOR
        private static void EnsureParentFolder(string assetPath)
        {
            string parent = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EnsureFolder(parent);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || string.Equals(folderPath, AssetsRoot, StringComparison.OrdinalIgnoreCase))
                return;
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = System.IO.Path.GetDirectoryName(folderPath).Replace('\\', '/');
            EnsureFolder(parent);
            string leaf = folderPath.Substring(parent.Length).TrimStart('/');
            if (leaf.Length == 0)
                return;
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        internal static void DeleteConsumerTempRoot()
        {
            if (AssetDatabase.IsValidFolder(ConsumerTempRoot))
                AssetDatabase.DeleteAsset(ConsumerTempRoot);
        }
#endif
    }

#if UNITY_EDITOR
    [SetUpFixture]
    internal sealed class ShapeSyncTestAssetCleanupFixture
    {
        [OneTimeSetUp]
        public void BeforeTests() => ShapeSyncTestAssetPaths.DeleteConsumerTempRoot();

        [OneTimeTearDown]
        public void AfterTests() => ShapeSyncTestAssetPaths.DeleteConsumerTempRoot();
    }
#endif
}
