// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using zgock.ShapeSync;
using zgock.ShapeSync.Editor;

#if UNITY_6000_2_OR_NEWER
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class Spec21OptionalFeatureAdmissionTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec21OptionalFeatureRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                ShapeSyncTestAssetPaths.ConsumerFolderPath("__Spec21_OptionalFeatureAdmissionTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void FreshDatabase_UsesExistingFixedRegistryAndHasNoOptionalMarker()
        {
            string databasePath = Root + "/Database.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True,
                createDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.True, openDiagnostic);
            UnityEngine.Object[] localAssets = AssetDatabase.LoadAllAssetsAtPath(databasePath);
            Assert.That(localAssets.OfType<ShapeSyncDatabaseRegistry>().Count(), Is.EqualTo(1));
            Assert.That(localAssets.OfType<ShapeSyncDatabaseOptionalFeatureMarker>(), Is.Empty);
        }

        [Test]
        public void VrmMarker_UsesStructuredOptionalAdmissionDiagnostic()
        {
            string databasePath = Root + "/Database.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True,
                createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (_, _, context) =>
            {
                context.AddSubAsset(ShapeSyncDatabaseOptionalFeatureMarker.Create("VRM"));
            }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string diagnostic), Is.False);
#if SHAPESYNC_USE_UNIVRM
            Assert.That(diagnostic, Does.Contain("VRM Registry"));
#else
            Assert.That(diagnostic, Does.Contain("OptionalFeatureUnavailable"));
            Assert.That(diagnostic, Does.Contain("SHAPESYNC_USE_UNIVRM"));
#endif
        }

        [Test]
        public void NavigationTree_WithoutOptionalProvider_HidesVrmNodes()
        {
            ShapeSyncDatabaseWindow.NavigationTreeView tree = new ShapeSyncDatabaseWindow.NavigationTreeView(
                new ShapeSyncTreeViewState(),
                _ => true,
                () => ShapeSyncDatabaseWindow.Section.General);

            Assert.That(tree.FigureChildDisplayNamesForTest, Does.Not.Contain("VRM"));
            Assert.That(tree.MeshOutfitChildDisplayNamesForTest, Does.Not.Contain("VRM"));
        }

        [Test]
        public void ManualTransporterWindows_FollowTheDebugIsolationBoundary()
        {
            const string vrmWindowTypeName =
                "zgock.ShapeSync.VrmIntegration.Editor.VrmTransporterWindow, zgock.ShapeSync.VrmIntegration.Editor";
            const string physicsWindowTypeName =
                "zgock.ShapeSync.VrmIntegration.Editor.PhysicsTransporterWindow, zgock.ShapeSync.VrmIntegration.Editor";

#if SHAPESYNC_USE_UNIVRM && SHAPESYNC_DEBUG
            Assert.That(System.Type.GetType(vrmWindowTypeName), Is.Not.Null);
            Assert.That(System.Type.GetType(physicsWindowTypeName), Is.Not.Null);
#else
            Assert.That(System.Type.GetType(vrmWindowTypeName), Is.Null);
            Assert.That(System.Type.GetType(physicsWindowTypeName), Is.Null);
#endif
        }
    }
}
