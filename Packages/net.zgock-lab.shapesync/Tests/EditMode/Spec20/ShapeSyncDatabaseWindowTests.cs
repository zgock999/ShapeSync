// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;

#if UNITY_6000_2_OR_NEWER
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncDatabaseWindowTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20DatabaseWindowRoot;

        [Test]
        public void OutfitStep1_IdentityPersistsAndNameDraftIsSavedWithoutChangingIdentity()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Shirt01", null, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("Shirt01"));
                Assert.That(window.OutfitNameDraftForTest, Is.Empty);
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.EqualTo(new[] { "Shirt01" }));
                window.SetOutfitNameDraftForTest("Blue Shirt");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.EqualTo(new[] { "Blue Shirt" }));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry saved = reopened.Registry.Outfits.Single();
                Assert.That(saved.Identity, Is.EqualTo("Shirt01"));
                Assert.That(saved.DisplayName, Is.EqualTo("Blue Shirt"));
                Assert.That(saved.Kind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitKind.Mesh));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitPbmFollow_WindowHydratesSourceRowsCleanlyAndResavesWithoutArtifactConfusion()
        {
            const string databasePath = Root + "/PbmFollowWindow.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = CreateValidImportedFigure(intermediate, "Master", transaction);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                var drafts = new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject tallFbm = CreateValidImportedFigure(intermediate, "Tall", transaction);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallFbm) },
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
                GameObject baseSource = CreateValidImportedFigure(intermediate, "Coat_Master_Source", transaction);
                GameObject tallSource = CreateValidImportedFigure(intermediate, "Coat_Tall_Source", transaction);
                GameObject baseArtifact = CreateValidImportedFigure(intermediate, "Coat_Pose_Master", transaction);
                GameObject tallArtifact = CreateValidImportedFigure(intermediate, "Coat_Pose_Tall", transaction);
                Assert.That(contents.Registry.TrySetOutfitPbmFollows(contents, "Coat", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry("Pose", new[]
                    {
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseSource, baseArtifact),
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry("Tall", tallSource, tallArtifact)
                    })
                }, out string followDiagnostic), Is.True, followDiagnostic);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry savedBase = opened.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows.Single().Figures
                .Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry savedTall = opened.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows.Single().Figures
                .Single(entry => entry.ShapeKey == "Tall");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "PBMs"), Is.True);
                Assert.That(window.OutfitPbmFollowSourcePrefabForTest("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey), Is.SameAs(savedBase.SourcePrefab));
                Assert.That(window.OutfitPbmFollowSourcePrefabForTest("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey), Is.Not.SameAs(savedBase.Figure));
                Assert.That(window.OutfitPbmFollowSourcePrefabForTest("Pose", "Tall"), Is.SameAs(savedTall.SourcePrefab));
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase resaved, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(resaved.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Not.Empty);
                Assert.That(AssetDatabase.GetAssetPath(resaved.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows.Single().Figures
                    .Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab), Is.EqualTo(databasePath));
                Assert.That(savedTall.SourcePrefab, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitPbmFollow_WindowDoesNotReusePersistedSourceWhenMeshIsMissing()
        {
            const string databasePath = Root + "/PbmFollowWindowMissingSourceMesh.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = CreateValidImportedFigure(intermediate, "Master", transaction);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject tallFbm = CreateValidImportedFigure(intermediate, "Tall", transaction);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallFbm) },
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
                GameObject baseSource = CreateValidImportedFigure(intermediate, "Coat_Master_Source", transaction);
                GameObject tallSource = CreateValidImportedFigure(intermediate, "Coat_Tall_Source", transaction);
                baseSource.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh = null;
                tallSource.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh = null;
                GameObject baseArtifact = CreateValidImportedFigure(intermediate, "Coat_Pose_Master", transaction);
                GameObject tallArtifact = CreateValidImportedFigure(intermediate, "Coat_Pose_Tall", transaction);
                Assert.That(contents.Registry.TrySetOutfitPbmFollows(contents, "Coat", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry("Pose", new[]
                    {
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseSource, baseArtifact),
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry("Tall", tallSource, tallArtifact)
                    })
                }, out string followDiagnostic), Is.True, followDiagnostic);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "PBMs"), Is.True);
                Assert.That(window.OutfitPbmFollowSourcePrefabForTest("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey), Is.Null,
                    "A persisted SourcePrefab without Mesh must not be reused as the overwrite source.");
                Assert.That(window.OutfitPbmFollowSourcePrefabForTest("Pose", "Tall"), Is.Null,
                    "Every invalid persisted PBM source row must require an explicit replacement source.");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True,
                    "Clearing invalid persisted sources must make the detail dirty so the user can reselect the authoritative source.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OpenDatabase_ReloadsExistingOutfitEntriesIntoCachedTreeView()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("hair-1", "hair-1", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string registryDiagnostic), Is.True, registryDiagnostic);
            }, out string editDiagnostic), Is.True, editDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.Outfits.Select(entry => entry.Identity), Is.EqualTo(new[] { "hair-1" }));

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.Empty);

                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(tree.RootDisplayNamesForTest, Does.Contain("Outfits"));
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.EqualTo(new[] { "hair-1" }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MeshOutfitMaterials_ClassificationControlUsesThreeExclusiveRadioOptions()
        {
            Assert.That(ShapeSyncDatabaseWindow.OutfitMaterialClassificationControlLabelsForTest,
                Is.EqualTo(new[] { "Include", "Exclude", "Projection" }));
            Assert.That(ShapeSyncDatabaseWindow.OutfitMaterialClassificationControlStyleForTest,
                Is.SameAs(EditorStyles.radioButton));
        }

        [Test]
        public void MeshOutfitMaterials_EntryPreviewUsesTheMaterialMainTexture()
        {
            Shader shader = Shader.Find("Unlit/Texture");
            Assert.That(shader, Is.Not.Null, "The preview fixture requires a texture-capable shader.");
            Material material = new Material(shader) { name = "OutfitPreviewMaterial" };
            Texture2D texture = new Texture2D(1, 1) { name = "OutfitPreviewTexture" };
            try
            {
                material.mainTexture = texture;
                Assert.That(ShapeSyncDatabaseWindow.ResolveOutfitMaterialPreviewForTest(material), Is.SameAs(texture),
                    "Outfit Material Detail must expose the source Material MainTex as its Entry preview.");
                Assert.That(ShapeSyncDatabaseWindow.ResolveOutfitMaterialPreviewForTest(null), Is.Null,
                    "A missing source Material must render an empty preview instead of throwing.");
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void MeshOutfitMaterials_IncludedPersistedEntryRemainsPreviewableAfterSourceRemoval()
        {
            Shader shader = Shader.Find("Unlit/Texture");
            Assert.That(shader, Is.Not.Null, "The preview fixture requires a texture-capable shader.");
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            Material material = new Material(shader) { name = "SavedIncludedMaterial" };
            Texture2D texture = new Texture2D(1, 1) { name = "SavedIncludedTexture" };
            try
            {
                material.mainTexture = texture;
                Assert.That(registry.TryAddOutfit("hair-1", "Hair", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = registry.Outfits.Single();
                outfit.SetMaterialEntries(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("HairEntry", material, null)
                });
                Material resolved = ShapeSyncDatabaseWindow.ResolveOutfitMaterialForPreviewForTest(outfit,
                    "RemovedSourceMaterial", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "HairEntry");
                Assert.That(resolved, Is.SameAs(material));
                Assert.That(ShapeSyncDatabaseWindow.ResolveOutfitMaterialPreviewForTest(resolved), Is.SameAs(texture));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void MeshOutfitMaterials_EntryNameRemainsEditableUntilClassificationIsPersisted()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            try
            {
                Assert.That(registry.TryAddOutfit("hair-1", "Hair", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = registry.Outfits.Single();
                Assert.That(ShapeSyncDatabaseWindow.IsOutfitMaterialEntryNameEditableForTest(outfit), Is.True,
                    "An overwrite/re-registration draft must keep Entry Name editable before classification Save.");

                Assert.That(registry.TrySetOutfitMaterialClassifications("hair-1", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(
                        "HairMaterial",
                        ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include,
                        "HairEntry")
                }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
                Assert.That(ShapeSyncDatabaseWindow.IsOutfitMaterialEntryNameEditableForTest(outfit), Is.False,
                    "Persisted classification makes the material table immutable until Outfit removal/recreation.");
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void MeshOutfitMaterials_ScrollPositionIsRetainedAcrossGuiState()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Vector2 expected = new Vector2(0f, 240f);
                window.OutfitMaterialsScrollPositionForTest = expected;
                Assert.That(window.OutfitMaterialsScrollPositionForTest, Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OtherDetails_ScrollPositionsAreRetainedAcrossGuiState()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Vector2 expected = new Vector2(0f, 240f);
                window.FigureDetailScrollPositionForTest = expected;
                window.OutfitDetailScrollPositionForTest = expected;
                window.OutfitFbmsScrollPositionForTest = expected;
                window.OutfitNormalsScrollPositionForTest = expected;
                window.OutfitPbmsScrollPositionForTest = expected;
                window.OutfitCollectionScrollPositionForTest = expected;
                window.MaterialOutfitScrollPositionForTest = expected;
                window.FigureMaskScrollPositionForTest = expected;

                Assert.That(window.FigureDetailScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.OutfitDetailScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.OutfitFbmsScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.OutfitNormalsScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.OutfitPbmsScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.OutfitCollectionScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.MaterialOutfitScrollPositionForTest, Is.EqualTo(expected));
                Assert.That(window.FigureMaskScrollPositionForTest, Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitStep1_RejectsDuplicateOrWhitespaceIdentityAndRemovesSelectedEntity()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Top", "Top", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Top", "Other", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("already exists"));
                Assert.That(window.TryAddOutfitForTest("Bad Id", null, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string whitespaceDiagnostic), Is.False);
                Assert.That(whitespaceDiagnostic, Does.Contain("whitespace"));
                Assert.That(window.TryAddOutfitForTest("InvalidKind", null, (ShapeSyncDatabaseRegistry.OutfitKind)99, out string kindDiagnostic), Is.False);
                Assert.That(kindDiagnostic, Does.Contain("kind is invalid"));
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(database.Registry.Outfits, Is.Empty);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitStep1_TreeViewGroupsMeshAndMaterialOutfitsAndSelectsByIdentity()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            try
            {
                Assert.That(registry.TryAddOutfit("Shirt", "BlueShirt", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string meshDiagnostic), Is.True, meshDiagnostic);
                Assert.That(registry.TryAddOutfit("Dye", "BlueDye", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string materialDiagnostic), Is.True, materialDiagnostic);
                string selectedIdentity = null;
                var tree = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(),
                    _ => true,
                    () => ShapeSyncDatabaseWindow.Section.General,
                    () => registry.Outfits,
                    identity => { selectedIdentity = identity; return true; });
                Assert.That(tree.RootDisplayNamesForTest, Does.Contain("Outfits"));
                Assert.That(tree.MeshOutfitChildDisplayNamesForTest, Is.EqualTo(new[] { "Materials", "Normals", "FBMs", "PBMs", "Collections", "Figure Mask" }));
                tree.ApplySelectionChangeForTest(new[] { 1000 });
                Assert.That(selectedIdentity, Is.EqualTo("Shirt"));
                selectedIdentity = null;
                tree.ApplySelectionChangeForTest(new[] { 1001 });
                Assert.That(selectedIdentity, Is.EqualTo("Shirt"));
                tree.ApplySelectionChangeForTest(new[] { 1007 });
                Assert.That(selectedIdentity, Is.EqualTo("Dye"));
            }
            finally { Object.DestroyImmediate(registry); }
        }

        [Test]
        public void OutfitDetail_MoveButtonsReorderOnlyTheirOwnTreeViewGroupAndPersist()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddOutfitForTest("MeshA", "Mesh A", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddOutfitForTest("MaterialA", "Material A", ShapeSyncDatabaseRegistry.OutfitKind.Material, out addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddOutfitForTest("MeshB", "Mesh B", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddOutfitForTest("MaterialB", "Material B", ShapeSyncDatabaseRegistry.OutfitKind.Material, out addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();

                Assert.That(window.TrySelectOutfitForTest("MeshB"), Is.True);
                int meshBItemId = tree.GetOutfitItemIdForTest("MeshB");
                Assert.That(window.TryMoveSelectedOutfitForTest(true, out string moveDiagnostic), Is.True, moveDiagnostic);
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.EqualTo(new[] { "Mesh B", "Mesh A" }));
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Material), Is.EqualTo(new[] { "Material A", "Material B" }));
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("MeshB"));
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TryMoveSelectedOutfitForTest(true, out moveDiagnostic), Is.False);
                Assert.That(moveDiagnostic, Does.Contain("already first"));

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeSave, out string beforeSaveDiagnostic), Is.True, beforeSaveDiagnostic);
                Assert.That(beforeSave.Registry.Outfits.Where(entry => entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh).Select(entry => entry.Identity),
                    Is.EqualTo(new[] { "MeshA", "MeshB" }));
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { tree.GetOutfitItemIdForTest("MeshB") }));
                Assert.That(tree.SelectedItemIdsForTest, Is.Not.EqualTo(new[] { meshBItemId }),
                    "Outfit order save must update accepted selection after dynamic item IDs are rebuilt.");

                Assert.That(window.TrySelectOutfitForTest("MaterialB"), Is.True);
                Assert.That(window.TryMoveSelectedOutfitForTest(true, out moveDiagnostic), Is.True, moveDiagnostic);
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Mesh), Is.EqualTo(new[] { "Mesh B", "Mesh A" }));
                Assert.That(tree.OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind.Material), Is.EqualTo(new[] { "Material B", "Material A" }));
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { tree.GetOutfitItemIdForTest("MaterialB") }));

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(reopened.Registry.Outfits.Where(entry => entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh).Select(entry => entry.Identity),
                    Is.EqualTo(new[] { "MeshB", "MeshA" }));
                Assert.That(reopened.Registry.Outfits.Where(entry => entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Material).Select(entry => entry.Identity),
                    Is.EqualTo(new[] { "MaterialB", "MaterialA" }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapeDetail_MoveButtonsReorderDraftOnlyAndPersistOnSave()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddShapeForTest("skin-a", "Skin A", ShapeSyncDatabaseRegistry.ShapeKind.Skin, out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddShapeForTest("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, out addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddShapeForTest("skin-b", "Skin B", ShapeSyncDatabaseRegistry.ShapeKind.Skin, out addDiagnostic), Is.True, addDiagnostic);
                Assert.That(window.TryAddShapeForTest("skin-c", "Skin C", ShapeSyncDatabaseRegistry.ShapeKind.Skin, out addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();

                Assert.That(window.TrySelectShapeForTest("skin-b"), Is.True);
                Assert.That(window.TryMoveSelectedShapeForTest(true, out string moveDiagnostic), Is.True, moveDiagnostic);
                Assert.That(tree.ShapeDisplayNamesForTest(ShapeSyncDatabaseRegistry.ShapeKind.Skin), Is.EqualTo(new[] { "Skin B", "Skin A", "Skin C" }));
                Assert.That(tree.ShapeDisplayNamesForTest(ShapeSyncDatabaseRegistry.ShapeKind.Hair), Is.EqualTo(new[] { "Hair A" }));
                Assert.That(window.IsShapesDetailDirtyForTest, Is.True);
                Assert.That(window.TryMoveSelectedShapeForTest(true, out moveDiagnostic), Is.False);
                Assert.That(moveDiagnostic, Does.Contain("already first"));

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeSave, out string beforeSaveDiagnostic), Is.True, beforeSaveDiagnostic);
                Assert.That(beforeSave.Registry.Shapes.Select(entry => entry.ShapeId), Is.EqualTo(new[] { "skin-a", "hair-a", "skin-b", "skin-c" }));
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsShapesDetailDirtyForTest, Is.False);

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(reopened.Registry.Shapes.Select(entry => entry.ShapeId), Is.EqualTo(new[] { "skin-b", "skin-a", "hair-a", "skin-c" }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapeCreate_IsMemoryDraftUntilFooterSave_AndRejectsDuplicateDraftIds()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryBeginShapeDraftForTest("draft-shape", "Draft Shape", ShapeSyncDatabaseRegistry.ShapeKind.Skin, out string draftDiagnostic), Is.True, draftDiagnostic);
                Assert.That(database.Registry.Shapes.Any(entry => entry != null && entry.ShapeId == "draft-shape"), Is.False,
                    "Create must not mutate the Database before the Shape Detail footer Save.");
                Assert.That(window.SelectedShapeIdForTest, Is.EqualTo("draft-shape"));
                Assert.That(window.TryBeginShapeDraftForTest("second-draft", "Second Draft", ShapeSyncDatabaseRegistry.ShapeKind.Hair, out draftDiagnostic), Is.False);
                Assert.That(draftDiagnostic, Does.Contain("current Shape draft"));
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(database.Registry.Shapes.Any(entry => entry != null && entry.ShapeId == "draft-shape"), Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapeCreate_DiscardRemovesOnlyPendingNode_AndSavedIdBecomesReadOnly()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryBeginShapeDraftForTest("discard-shape", "Discard Shape", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, out string draftDiagnostic), Is.True, draftDiagnostic);
                Assert.That(window.IsSelectedShapeIdReadOnlyForTest, Is.False);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                Assert.That(tree.GetShapeItemIdForTest("discard-shape"), Is.GreaterThan(0));
                window.DiscardSelectedShapeDraftForTest();
                Assert.That(database.Registry.Shapes.Any(entry => entry != null && entry.ShapeId == "discard-shape"), Is.False);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapesItemId }));
                Assert.That(window.TryBeginShapeDraftForTest("saved-shape", "Saved Shape", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, out draftDiagnostic), Is.True, draftDiagnostic);
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsSelectedShapeIdReadOnlyForTest, Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitStep1_DirtyNavigationSavesIgnoresAndCancelsAndRestoresDynamicSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Top", "Top", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string topDiagnostic), Is.True, topDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Pants", "Pants", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string pantsDiagnostic), Is.True, pantsDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                int pantsChildItemId = tree.GetOutfitChildItemIdForTest("Pants", "Materials");
                Assert.That(pantsChildItemId, Is.GreaterThan(0));

                tree.ApplySelectionChangeForTest(new[] { 1001 });
                window.SetOutfitNameDraftForTest("Saved From Child");
                Assert.That(window.TrySaveOutfitForTest(out string childSaveDiagnostic), Is.True, childSaveDiagnostic);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { 1000 }));
                window.SetOutfitNameDraftForTest("Cancelled From Parent");
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                tree.ApplySelectionChangeForTest(new[] { pantsChildItemId });
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("Top"));
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { 1000 }));

                tree.ApplySelectionChangeForTest(new[] { 1000 });
                window.SetOutfitNameDraftForTest("Saved Top");
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                tree.ApplySelectionChangeForTest(new[] { pantsChildItemId });
                Assert.That(database.Registry.Outfits.Single(entry => entry.Identity == "Top").DisplayName, Is.EqualTo("Saved Top"));
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("Pants"));

                tree.ApplySelectionChangeForTest(new[] { 1000 });
                window.SetOutfitNameDraftForTest("Ignored Top");
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;
                tree.ApplySelectionChangeForTest(new[] { pantsChildItemId });
                Assert.That(database.Registry.Outfits.Single(entry => entry.Identity == "Top").DisplayName, Is.EqualTo("Saved Top"));
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("Pants"));

                tree.ApplySelectionChangeForTest(new[] { 1000 });
                window.SetOutfitNameDraftForTest("Cancelled Top");
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                tree.ApplySelectionChangeForTest(new[] { pantsChildItemId });
                Assert.That(window.SelectedOutfitIdentityForTest, Is.EqualTo("Top"));
                Assert.That(window.OutfitNameDraftForTest, Is.EqualTo("Cancelled Top"));
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { 1000 }));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OutfitStep1_TreeViewAllocatesDistinctIdsBeyondFormerKindOffsetAndRemoveSelectsOutfitsRoot()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            try
            {
                for (int index = 0; index <= 1000; index++)
                    Assert.That(registry.TryAddOutfit("Mesh" + index, null, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string meshDiagnostic), Is.True, meshDiagnostic);
                Assert.That(registry.TryAddOutfit("Material", null, ShapeSyncDatabaseRegistry.OutfitKind.Material, out string materialDiagnostic), Is.True, materialDiagnostic);
                string selectedIdentity = null;
                var tree = new ShapeSyncDatabaseWindow.NavigationTreeView(new ShapeSyncTreeViewState(), _ => true, () => ShapeSyncDatabaseWindow.Section.General,
                    () => registry.Outfits, identity => { selectedIdentity = identity; return true; });
                tree.ApplySelectionChangeForTest(new[] { 8000 });
                Assert.That(selectedIdentity, Is.EqualTo("Mesh1000"));
                tree.ApplySelectionChangeForTest(new[] { 8007 });
                Assert.That(selectedIdentity, Is.EqualTo("Material"));
            }
            finally { Object.DestroyImmediate(registry); }

            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryAddOutfitForTest("Top", null, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string addDiagnostic), Is.True, addDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView databaseTree = window.CreateNavigationTreeViewForTest();
                databaseTree.ApplySelectionChangeForTest(new[] { 1000 });
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Outfits));
                Assert.That(databaseTree.SelectedItemIdsForTest, Is.EqualTo(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.OutfitsItemId }));
                databaseTree.ApplySelectionChangeForTest(new[] { 999999 });
                Assert.That(databaseTree.SelectedItemIdsForTest, Is.EqualTo(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.OutfitsItemId }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_2_ShapeSyncDatabaseWindowTests"); }
            // Editor MCP tests are graphics-capable, so production's native confirmation
            // dialog would otherwise block unrelated test cases.  Dialog behavior is
            // exercised explicitly by MaterialsDetail_DraftsSupported... below.
            ShapeSyncDatabaseWindow.IsBatchMode = () => true;
            ShapeSyncDatabaseWindow.ConfirmTextureRename = (_, _, _, _) => false;
            ShapeSyncDatabaseWindow.ConfirmIrreversibleOutfitClassification = (_, _, _, _) => false;
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
            ShapeSyncDatabaseWindow.IsBatchMode = () => Application.isBatchMode;
            ShapeSyncDatabaseWindow.ConfirmTextureRename = EditorUtility.DisplayDialog;
            ShapeSyncDatabaseWindow.ConfirmIrreversibleOutfitClassification = EditorUtility.DisplayDialog;
        }

        [Test]
        public void WindowShell_DeclaresRequiredMenuTitleAndSplitLayoutMetrics()
        {
            Assert.That(ShapeSyncDatabaseWindow.MenuPath, Is.EqualTo("Tools/zgock/ShapeSync/ShapeSync Editor"));
            Assert.That(ShapeSyncDatabaseWindow.WindowTitle, Is.EqualTo("ShapeSync Database"));
            Assert.That(ShapeSyncDatabaseWindow.DefaultWindowWidth, Is.EqualTo(1024f));
            Assert.That(ShapeSyncDatabaseWindow.DefaultWindowHeight, Is.EqualTo(768f));
            Assert.That(ShapeSyncDatabaseWindow.TreeViewWidth, Is.EqualTo(200f));
            Assert.That(ShapeSyncDatabaseWindow.DetailSaveButtonHeight, Is.EqualTo(40f));
            Assert.That(ShapeSyncDatabaseWindow.TreeLabels, Is.EqualTo(new[] { "General", "Figure", "Materials", "Shapes", "Textures" }));
            Assert.That(ShapeSyncDatabaseWindow.DetailTitle, Is.EqualTo("General"));
            Assert.That(ShapeSyncDatabaseWindow.EmptyDatabaseMessage, Is.EqualTo("Select or create a ShapeSync Database."));
            Assert.That(ShapeSyncDatabaseWindow.FigureDetailMessage, Is.EqualTo("No Figure is selected."));
            Assert.That(ShapeSyncDatabaseWindow.ShapesDetailMessage, Is.EqualTo("No Shape is selected."));
        }

        [Test]
        public void NormalsDetail_UsesTopAddCentralScrollAndFooterSaveLayout()
        {
            ShapeSyncDatabaseWindow.NormalDetailLayout layout = ShapeSyncDatabaseWindow.GetNormalDetailLayoutForTest();
            Assert.That(layout.AddActionLabel, Is.EqualTo("Add Normal Entry"));
            Assert.That(layout.CentralScrollViewCount, Is.EqualTo(1));
            Assert.That(layout.AddActionIsAboveCentralScroll, Is.True);
            Assert.That(layout.SaveActionIsBelowCentralScroll, Is.True);
            Assert.That(layout.SaveActionLabel, Is.EqualTo("Save to Database"));
        }

        [Test]
        public void OutfitDetail_PlacesRemoveInOutfitIdRowAndUsesFullWidthSaveFooter()
        {
            ShapeSyncDatabaseWindow.OutfitDetailLayout layout = ShapeSyncDatabaseWindow.GetOutfitDetailLayoutForTest();
            Assert.That(layout.RemoveActionIsInOutfitIdRow, Is.True);
            Assert.That(layout.FooterActionCount, Is.EqualTo(1));
            Assert.That(layout.FooterActionLabel, Is.EqualTo("Save to Database"));
            Assert.That(layout.FooterSaveUsesFullWidth, Is.True);
        }

        [Test]
        public void ShapeDetail_UsesTheSameFullWidthSaveFooterAsFigureAndOutfitDetails()
        {
            ShapeSyncDatabaseWindow.ShapeDetailLayout layout = ShapeSyncDatabaseWindow.GetShapeDetailLayoutForTest();
            Assert.That(layout.FooterActionCount, Is.EqualTo(1));
            Assert.That(layout.FooterSaveActionLabel, Is.EqualTo("Save to Database"));
            Assert.That(layout.FooterSaveUsesFullWidth, Is.True);
            Assert.That(layout.SaveAppearsInContent, Is.False);
        }

        [Test]
        public void GenerationDetail_UsesFivePathFieldsAndOneFooterSaveWithCleanGenerateGuard()
        {
            ShapeSyncDatabaseWindow.GenerationDetailLayout layout = ShapeSyncDatabaseWindow.GetGenerationDetailLayoutForTest();
            Assert.That(layout.PathFieldCount, Is.EqualTo(5));
            Assert.That(layout.FooterActionCount, Is.EqualTo(1));
            Assert.That(layout.FooterSaveActionLabel, Is.EqualTo("Save to Database"));
            Assert.That(layout.SaveAppearsInFooter, Is.True);
            Assert.That(layout.GenerateRequiresCleanDraft, Is.True);
        }

        [Test]
        public void MorphShapeDetail_HidesPriorityAndTagsWhileConcreteShapeShowsBoth()
        {
            ShapeSyncDatabaseWindow.ShapeDetailLayout morph = ShapeSyncDatabaseWindow.GetShapeDetailLayoutForTest(ShapeSyncDatabaseRegistry.ShapeKind.Morph);
            ShapeSyncDatabaseWindow.ShapeDetailLayout hair = ShapeSyncDatabaseWindow.GetShapeDetailLayoutForTest(ShapeSyncDatabaseRegistry.ShapeKind.Hair);
            Assert.That(morph.ShowsPriority, Is.False);
            Assert.That(morph.ShowsTags, Is.False);
            Assert.That(hair.ShowsPriority, Is.True);
            Assert.That(hair.ShowsTags, Is.True);
        }

        [Test]
        public void MorphShapeDetail_UsesTheDirectorAndDdbRawWeightSliderRange()
        {
            Assert.That(ShapeSyncDatabaseWindow.GetMorphSliderLimitForTest(0f), Is.EqualTo(1f));
            Assert.That(ShapeSyncDatabaseWindow.GetMorphSliderLimitForTest(-1f), Is.EqualTo(1f));
            Assert.That(ShapeSyncDatabaseWindow.GetMorphSliderLimitForTest(1.01f), Is.EqualTo(2f));
            Assert.That(ShapeSyncDatabaseWindow.GetMorphSliderLimitForTest(-2.1f), Is.EqualTo(3f));
        }

        [Test]
        public void ShapePartTargetAndTextureSelectorsShareTheirDetailRows()
        {
            ShapeSyncDatabaseWindow.ShapePartEntryLayout layout = ShapeSyncDatabaseWindow.GetShapePartEntryLayoutForTest();
            Assert.That(layout.TargetAndEntryShareOneRow, Is.True);
            Assert.That(layout.TextureOwnerAndTextureShareOneRow, Is.True);
            Assert.That(layout.TextureColorizeAndPickerShareOneRow, Is.True);
            Assert.That(layout.MeshEntryHidesFigureMask, Is.True);
        }

        [Test]
        public void ShapeTagsUseCompactSelectorRowAndWrappingChips()
        {
            ShapeSyncDatabaseWindow.ShapeTagLayout layout = ShapeSyncDatabaseWindow.GetShapeTagLayoutForTest();
            Assert.That(layout.SelectorAndAddShareOneRow, Is.True);
            Assert.That(layout.ChipsWrapWithinDetailWidth, Is.True);
        }

        [Test]
        public void ShapeTagsVocabulary_PlacesAddActionInTheTopTagsHeaderAndKeepsStandardListEditor()
        {
            ShapeSyncDatabaseWindow.ShapeTagsVocabularyLayout layout = ShapeSyncDatabaseWindow.GetShapeTagsVocabularyLayoutForTest();
            Assert.That(layout.AddActionSharesHeaderRow, Is.True);
            Assert.That(layout.UsesStandardListEditor, Is.True);
            Assert.That(layout.AddActionLabel, Is.EqualTo("Add Tag"));
        }

        [Test]
        public void UvSetDetail_UsesCompactSingleRowsForScaleAndOffsetXYFields()
        {
            ShapeSyncDatabaseWindow.ShapePartUvLayout layout = ShapeSyncDatabaseWindow.GetShapePartUvLayoutForTest();
            Assert.That(layout.ScaleUsesOneRowWithXYFields, Is.True);
            Assert.That(layout.OffsetUsesOneRowWithXYFields, Is.True);
        }

        [Test]
        public void NewShapePartColorPickerDefaultsToOpaqueWhite()
        {
            Color32 expected = new Color32(255, 255, 255, 255);
            Assert.That(new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color).Color, Is.EqualTo(expected));
            Assert.That(new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture).Color, Is.EqualTo(expected));
        }

        [Test]
        public void ShapePartAdd_SeedsFirstAvailableTargetAndRejectsWhenNoCandidateExists()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                string databasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                    contents.Registry.Outfits.Single().SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("body", null, null) });
                    Assert.That(contents.Registry.TryAddShape("hair-part", "Hair Part", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
                }, out string seedDiagnostic), Is.True, seedDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateToShapeForTest("hair-part"), Is.True);

                Assert.That(window.TryAddShapePartDraftForTest(ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string meshDiagnostic), Is.True, meshDiagnostic);
                ShapeSyncDatabaseRegistry.ShapeEntryDefinition mesh = window.GetShapePartDraftForTest(0);
                Assert.That(mesh.OutfitIdentity, Is.EqualTo("mesh-outfit"));

                Assert.That(window.TryAddShapePartDraftForTest(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string colorDiagnostic), Is.True, colorDiagnostic);
                ShapeSyncDatabaseRegistry.ShapeEntryDefinition color = window.GetShapePartDraftForTest(1);
                Assert.That(color.RegistryId, Is.EqualTo("mesh-outfit"));
                Assert.That(color.ProxyEntry, Is.EqualTo("body"));
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(AssetDatabase.CreateFolder(Root, "Empty") != string.Empty, Is.True);
            string emptyRoot = Root + "/Empty";
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(emptyRoot, out ShapeSyncDatabase emptyDatabase, out string emptyCreateDiagnostic), Is.True, emptyCreateDiagnostic);
            ShapeSyncDatabaseWindow emptyWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(emptyWindow.TrySetDatabase(emptyDatabase, out string emptyBindDiagnostic), Is.True, emptyBindDiagnostic);
                Assert.That(emptyWindow.TryAddShapeForTest("empty-targets", "Empty Targets", ShapeSyncDatabaseRegistry.ShapeKind.Hair, out string addShapeDiagnostic), Is.True, addShapeDiagnostic);
                Assert.That(emptyWindow.TryNavigateToShapeForTest("empty-targets"), Is.True);
                Assert.That(emptyWindow.TryAddShapePartDraftForTest(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string noCandidateDiagnostic), Is.False);
                Assert.That(noCandidateDiagnostic, Does.Contain("no Material Entry target"));
                Assert.That(emptyWindow.ShapePartDraftCountForTest, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(emptyWindow);
                AssetDatabase.DeleteAsset(emptyRoot);
            }
        }

        [Test]
        public void ShapeParts_AreMemoryDraftsUntilTheDetailFooterSavesOneTransaction()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                string databasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                    contents.Registry.Outfits.Single().SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("body", null, null) });
                    Assert.That(contents.Registry.TryAddShape("hair-draft", "Hair Draft", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                }, out string seedDiagnostic), Is.True, seedDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateToShapeForTest("hair-draft"), Is.True);
                Assert.That(window.TryAddShapePartDraftForTest(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string draftDiagnostic), Is.True, draftDiagnostic);
                Assert.That(window.ShapePartDraftCountForTest, Is.EqualTo(1));
                Assert.That(database.Registry.Shapes.Single().Parts, Is.Empty, "Draft mutation must not write the Database before footer Save.");
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsShapesDetailDirtyForTest, Is.False, "A saved Skin/Hair/Outfit Shape draft must disable the footer Save button immediately.");
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(reopened.Registry.Shapes.Single().Parts.Select(part => part.Kind), Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.ShapeEntryKind.Color }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MorphValues_AreMemoryDraftsUntilTheDetailFooterSavesOneTransaction()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                string databasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
                {
                    GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                    Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                    Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    GameObject smile = new GameObject("Smile"); smile.transform.SetParent(intermediate, false);
                    SkinnedMeshRenderer renderer = smile.AddComponent<SkinnedMeshRenderer>();
                    Mesh mesh = new Mesh { name = "Smile_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                    transaction.AddSubAsset(mesh); renderer.sharedMesh = mesh;
                    ShapeSyncFigureImportRecord record = smile.AddComponent<ShapeSyncFigureImportRecord>();
                    Assert.That(record.TryConfigure(new[] { renderer }, out string recordDiagnostic), Is.True, recordDiagnostic);
                    Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smile) } }, out string commitDiagnostic), Is.True, commitDiagnostic);
                    Assert.That(contents.Registry.TryAddShape("morph-draft", "Morph Draft", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                }, out string seedDiagnostic), Is.True, seedDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateToShapeForTest("morph-draft"), Is.True);
                window.EnsureSelectedShapeDraftForTest();
                Assert.That(window.IsShapesDetailDirtyForTest, Is.False, "A clean Morph Shape draft must keep the footer Save button disabled.");
                Assert.That(window.ShapeMorphDraftForTest.Single().Value, Is.Zero, "A newly discovered Figure axis must be represented by an explicit zero draft.");
                Assert.That(window.TrySetShapeMorphDraftForTest("Smile", 0f), Is.True);
                Assert.That(window.ShapeMorphDraftForTest.Single().Value, Is.Zero);
                Assert.That(database.Registry.Shapes.Single().Morphs, Is.Empty, "Slider draft must not write the Database before footer Save.");
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                MorphValue saved = reopened.Registry.Shapes.Single().Morphs.Single();
                Assert.That(saved.Target, Is.EqualTo("Smile"));
                Assert.That(saved.Value, Is.Zero, "A zero Morph value is an explicit authoring value and must survive footer Save.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void WindowShell_CreatesWithGeneralSelectedWithoutChangingSelection()
        {
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WindowShell_CreatesWithExpectedTitle()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window, Is.Not.Null);
                Assert.That(window.titleContent.text, Is.EqualTo(ShapeSyncDatabaseWindow.WindowTitle));
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WindowShell_OpenWindowUsesConfiguredRectAndTitleWithoutShowingGraphics()
        {
            Func<Rect, string, ShapeSyncDatabaseWindow> originalFactory = ShapeSyncDatabaseWindow.CreateWindow;
            Rect capturedRect = default;
            string capturedTitle = null;
            ShapeSyncDatabaseWindow createdWindow = null;
            try
            {
                ShapeSyncDatabaseWindow.CreateWindow = (rect, title) =>
                {
                    capturedRect = rect;
                    capturedTitle = title;
                    createdWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                    return createdWindow;
                };

                Assert.That(ShapeSyncDatabaseWindow.OpenWindow(), Is.SameAs(createdWindow));
                Assert.That(capturedRect, Is.EqualTo(new Rect(0f, 0f, ShapeSyncDatabaseWindow.DefaultWindowWidth, ShapeSyncDatabaseWindow.DefaultWindowHeight)));
                Assert.That(capturedTitle, Is.EqualTo(ShapeSyncDatabaseWindow.WindowTitle));
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateWindow = originalFactory;
                if (createdWindow != null) Object.DestroyImmediate(createdWindow);
            }
        }

        [Test]
        public void WindowVisual_OpenWindowCreatesNativeEditorWindowWhenGraphicsAreAvailable()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("This test exercises Unity's native EditorWindow path and requires a graphics-capable Editor.");
            }

            Object originalSelection = Selection.activeObject;
            int[] windowsBefore = Resources.FindObjectsOfTypeAll<ShapeSyncDatabaseWindow>()
                .Select(window => window.GetInstanceID())
                .ToArray();
            ShapeSyncDatabaseWindow window = null;
            ShapeSyncDatabaseWindow.Section initialSection = default;
            try
            {
                window = ShapeSyncDatabaseWindow.OpenWindow();
                initialSection = window.SelectedSection;
                window.Repaint();

                Assert.That(window, Is.Not.Null);
                Assert.That(window.titleContent.text, Is.EqualTo(ShapeSyncDatabaseWindow.WindowTitle));
                // GetWindowWithRect returns the user's existing ShapeSync Editor when
                // one is already open.  Native-window smoke coverage must preserve
                // that Human Test state, including its manually chosen dimensions.
                if (!windowsBefore.Contains(window.GetInstanceID()))
                {
                    Assert.That(window.position.width, Is.EqualTo(ShapeSyncDatabaseWindow.DefaultWindowWidth).Within(0.01f));
                    Assert.That(window.position.height, Is.EqualTo(ShapeSyncDatabaseWindow.DefaultWindowHeight).Within(0.01f));
                }
                Assert.That(window.SelectedSection, Is.EqualTo(initialSection));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                if (window != null && !windowsBefore.Contains(window.GetInstanceID())) window.Close();
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void WindowVisual_OutfitRegistrationRestoresParentGuiStateAfterOutfitRemovalFlow()
        {
            if (Application.isBatchMode)
            {
                Assert.Ignore("This test exercises native EditorWindow repaint and requires a graphics-capable Editor.");
            }

            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            bool previousGuiEnabled = GUI.enabled;
            try
            {
                window.position = new Rect(20f, 20f, ShapeSyncDatabaseWindow.DefaultWindowWidth, ShapeSyncDatabaseWindow.DefaultWindowHeight);
                window.Show();
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Outfits);
                GUI.enabled = false;
                Assert.That(window.SendEvent(new Event { type = EventType.Repaint }), Is.True);
                Assert.That(GUI.enabled, Is.False, "Outfit registration must restore the parent GUI state after drawing.");
            }
            finally
            {
                GUI.enabled = previousGuiEnabled;
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WindowVisual_MaterialsAndTexturesScrollPositionsSurviveRepaintWhenGraphicsAreAvailable()
        {
            if (Application.isBatchMode) Assert.Ignore("This test exercises native EditorWindow repaint and requires a graphics-capable Editor.");

            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                var materials = new Material[16];
                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = new Material(shader) { name = "ScrollMaterial" + index };
                    context.AddSubAsset(materials[index]);
                }
                renderer.sharedMaterials = materials;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                for (int index = 0; index < 16; index++)
                {
                    Texture2D texture = new Texture2D(1, 1) { name = "ScrollTexture" + index };
                    context.AddSubAsset(texture);
                    Assert.That(contents.Registry.TryRegisterTextureResource("Texture-" + index, texture, out string textureDiagnostic), Is.True, textureDiagnostic);
                }
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                window.position = new Rect(20f, 20f, ShapeSyncDatabaseWindow.DefaultWindowWidth, 180f);
                window.Show();
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);

                window.MaterialsScrollPositionForTest = new Vector2(0f, 37f);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.SendEvent(new Event { type = EventType.Repaint }), Is.True);
                Assert.That(window.MaterialsScrollPositionForTest, Is.EqualTo(new Vector2(0f, 37f)));

                window.TexturesScrollPositionForTest = new Vector2(0f, 53f);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.SendEvent(new Event { type = EventType.Repaint }), Is.True);
                Assert.That(window.TexturesScrollPositionForTest, Is.EqualTo(new Vector2(0f, 53f)));
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void WindowShell_ShowWindowDelegatesToFactoryWithoutShowingGraphics()
        {
            Func<Rect, string, ShapeSyncDatabaseWindow> originalFactory = ShapeSyncDatabaseWindow.CreateWindow;
            int invocationCount = 0;
            ShapeSyncDatabaseWindow createdWindow = null;
            try
            {
                ShapeSyncDatabaseWindow.CreateWindow = (_, _) =>
                {
                    invocationCount++;
                    createdWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                    return createdWindow;
                };

                Assert.DoesNotThrow(ShapeSyncDatabaseWindow.ShowWindow);
                Assert.That(invocationCount, Is.EqualTo(1));
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateWindow = originalFactory;
                if (createdWindow != null) Object.DestroyImmediate(createdWindow);
            }
        }

        [Test]
        public void WindowShell_OpenWindowFactoryFailurePropagatesWithoutChangingSelection()
        {
            Func<Rect, string, ShapeSyncDatabaseWindow> originalFactory = ShapeSyncDatabaseWindow.CreateWindow;
            Object originalSelection = Selection.activeObject;
            try
            {
                ShapeSyncDatabaseWindow.CreateWindow = (_, _) => throw new InvalidOperationException("Injected window factory failure");

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => { ShapeSyncDatabaseWindow.OpenWindow(); });
                Assert.That(exception.Message, Is.EqualTo("Injected window factory failure"));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateWindow = originalFactory;
            }
        }

        [Test]
        public void GeneralBinding_AcceptsValidatedDatabaseWithoutChangingSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string diagnostic), Is.True, diagnostic);
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralBinding_RejectsNullAndInvalidDatabaseWithoutReplacingBindingOrSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase validDatabase, out string createDiagnostic), Is.True, createDiagnostic);
            const string invalidPath = Root + "/Invalid.prefab";
            GameObject invalidRoot = new GameObject("Invalid");
            invalidRoot.AddComponent<ShapeSyncDatabase>();
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(validDatabase, out string validDiagnostic), Is.True, validDiagnostic);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(invalidRoot, invalidPath), Is.Not.Null);
                Selection.activeObject = validDatabase;
                Object selectionBeforeInvalidInput = Selection.activeObject;

                Assert.That(window.TrySetDatabase(null, out string nullDiagnostic), Is.False);
                Assert.That(nullDiagnostic, Does.Contain("requires"));
                Assert.That(window.Diagnostic, Is.EqualTo(nullDiagnostic));
                Assert.That(window.Database, Is.SameAs(validDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(selectionBeforeInvalidInput));

                ShapeSyncDatabase invalidDatabase = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(invalidPath);
                Assert.That(invalidDatabase, Is.Not.Null);
                Assert.That(window.TrySetDatabase(invalidDatabase, out string invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic, Does.Contain(ShapeSyncDatabaseAsset.IntermediateContainerName));
                Assert.That(window.Diagnostic, Is.EqualTo(invalidDiagnostic));
                Assert.That(window.Database, Is.SameAs(validDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(selectionBeforeInvalidInput));
            }
            finally
            {
                Object.DestroyImmediate(invalidRoot);
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralBinding_RejectsSceneOnlyDatabaseWithoutReplacingBindingOrSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase validDatabase, out string createDiagnostic), Is.True, createDiagnostic);
            GameObject sceneOnlyObject = new GameObject("SceneOnlyDatabase");
            ShapeSyncDatabase sceneOnlyDatabase = sceneOnlyObject.AddComponent<ShapeSyncDatabase>();
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(validDatabase, out string validDiagnostic), Is.True, validDiagnostic);
                Selection.activeObject = validDatabase;
                Object selectionBeforeInvalidInput = Selection.activeObject;

                Assert.That(window.TrySetDatabase(sceneOnlyDatabase, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("asset path"));
                Assert.That(window.Database, Is.SameAs(validDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(selectionBeforeInvalidInput));
            }
            finally
            {
                Object.DestroyImmediate(sceneOnlyObject);
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_CreateBindsOnlyOnSuccessAndOpenUsesBindingAdmission()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Object originalSelection = Selection.activeObject;
            try
            {
                Assert.That(window.TryCreateDatabase(Root, out string createDiagnostic), Is.True, createDiagnostic);
                ShapeSyncDatabase created = window.Database;
                Assert.That(created, Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(created), out ShapeSyncDatabase validated, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(window.TryOpenDatabase(validated, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                Assert.That(window.Database, Is.SameAs(validated));
                Assert.That(window.Diagnostic, Is.Null);

                Assert.That(window.TryCreateDatabase(ShapeSyncTestAssetPaths.Spec20DatabaseWindowMissingFolder, out string failureDiagnostic), Is.False);
                Assert.That(failureDiagnostic, Does.Contain("folder"));
                Assert.That(window.Database, Is.SameAs(validated));
                Assert.That(window.Diagnostic, Is.EqualTo(failureDiagnostic));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_OpenSelectedPrefabRootBindsValidatedDatabaseAndRejectsInvalidPrefabWithoutChangingSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase validDatabase, out string createDiagnostic), Is.True, createDiagnostic);
            const string invalidPath = Root + "/InvalidSelectedDatabase.prefab";
            GameObject invalidRoot = new GameObject("InvalidSelectedDatabase");
            invalidRoot.AddComponent<ShapeSyncDatabase>();
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                GameObject validPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(validDatabase));
                Assert.That(validPrefabRoot, Is.Not.Null);
                Selection.activeObject = validPrefabRoot;

                Assert.That(window.TryOpenDatabase(Selection.activeObject, out string validDiagnostic), Is.True, validDiagnostic);
                Assert.That(window.Database, Is.SameAs(validDatabase));
                Assert.That(window.Diagnostic, Is.Null);
                Assert.That(Selection.activeObject, Is.SameAs(validPrefabRoot));

                Assert.That(PrefabUtility.SaveAsPrefabAsset(invalidRoot, invalidPath), Is.Not.Null);
                GameObject invalidPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(invalidPath);
                Assert.That(invalidPrefabRoot, Is.Not.Null);
                Selection.activeObject = invalidPrefabRoot;

                Assert.That(window.TryOpenDatabase(Selection.activeObject, out string invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic, Does.Contain(ShapeSyncDatabaseAsset.IntermediateContainerName));
                Assert.That(window.Diagnostic, Is.EqualTo(invalidDiagnostic));
                Assert.That(window.Database, Is.SameAs(validDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(invalidPrefabRoot));
            }
            finally
            {
                Object.DestroyImmediate(invalidRoot);
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_SaveDialogCancelLeavesBindingAndSelectionUnchanged()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, _, _, _, _) => string.Empty;

                Assert.That(window.TryCreateDatabaseWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Is.Null);
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_SaveDialogExceptionLeavesBindingAndSelectionUnchanged()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, _, _, _, _) => throw new InvalidOperationException("Injected save dialog failure");

                Assert.That(window.TryCreateDatabaseWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Injected save dialog failure"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_SaveDialogCreatesAtChosenPathAndRejectsExistingPath()
        {
            const string chosenPath = Root + "/ChosenName.prefab";
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, _, _, _, _) => chosenPath;
                Assert.That(window.TryCreateDatabaseWithDialog(out string diagnostic), Is.True, diagnostic);
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(chosenPath));
                Assert.That(window.Database.gameObject.name, Is.EqualTo("ChosenName"));

                Assert.That(window.TryCreateDatabaseWithDialog(out string existingDiagnostic), Is.False);
                Assert.That(existingDiagnostic, Does.Contain("cannot overwrite"));
                Assert.That(window.Database, Is.Not.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GeneralCommands_SaveDialogSynchronizesNewFolderBeforeCreation()
        {
            const string folderPath = Root + "/DialogCreatedFolder";
            const string chosenPath = folderPath + "/ChosenName.prefab";
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            Action originalRefresh = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            int refreshCalls = 0;
            try
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, _, _, _, _) => chosenPath;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () =>
                {
                    refreshCalls++;
                    if (!AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.CreateFolder(Root, "DialogCreatedFolder");
                };

                Assert.That(window.TryCreateDatabaseWithDialog(out string diagnostic), Is.True, diagnostic);
                Assert.That(refreshCalls, Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(chosenPath));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = originalRefresh;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GenerationDetail_FolderSelectionSynchronizesNewFolderBeforeProjectRelativeConversion()
        {
            Action originalRefresh = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            Func<string, string> originalToProjectRelativePath = ShapeSyncDatabaseWindow.ToProjectRelativePath;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            List<string> callOrder = new List<string>();
            try
            {
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () => callOrder.Add("refresh");
                ShapeSyncDatabaseWindow.ToProjectRelativePath = path =>
                {
                    callOrder.Add("relative");
                    Assert.That(callOrder, Is.EqualTo(new[] { "refresh", "relative" }));
                    return ShapeSyncTestAssetPaths.InvalidAssetPath("GeneratedFromNewFolder");
                };

                Assert.That(window.ResolveGenerationRootForTest("C:/Project/Assets/GeneratedFromNewFolder"), Is.EqualTo(ShapeSyncTestAssetPaths.InvalidAssetPath("GeneratedFromNewFolder")));
                Assert.That(callOrder, Is.EqualTo(new[] { "refresh", "relative" }));
            }
            finally
            {
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = originalRefresh;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = originalToProjectRelativePath;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GenerationDetail_SavePersistsFivePathsAndRejectsInvalidOrDuplicateDrafts()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Generation);
                window.SetGenerationPathsForTest("../Registries", "Bindings", "Materials", "Textures", "Outfits");
                Assert.That(window.TrySaveGenerationForTest(out string invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic, Does.Contain("GenerationPathInvalid"));
                Assert.That(window.IsGenerationDetailDirtyForTest, Is.True);
                window.SetGenerationPathsForTest("Shared", "Shared", "Materials", "Textures", "Outfits");
                Assert.That(window.TrySaveGenerationForTest(out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("GenerationPathDuplicate"));
                Assert.That(window.IsGenerationDetailDirtyForTest, Is.True);
                window.SetGenerationPathsForTest("Custom/Registries", "Custom/Bindings", "Custom/Materials", "Custom/Textures", "Custom/Outfits");
                Assert.That(window.TrySaveGenerationForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsGenerationDetailDirtyForTest, Is.False);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase persisted, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(persisted.Registry.GenerationPaths.RegistriesPath, Is.EqualTo("Custom/Registries"));
                Assert.That(persisted.Registry.GenerationPaths.BindingsPath, Is.EqualTo("Custom/Bindings"));
                Assert.That(persisted.Registry.GenerationPaths.MaterialsPath, Is.EqualTo("Custom/Materials"));
                Assert.That(persisted.Registry.GenerationPaths.TexturesPath, Is.EqualTo("Custom/Textures"));
                Assert.That(persisted.Registry.GenerationPaths.OutfitsPath, Is.EqualTo("Custom/Outfits"));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void GeneralCommands_SaveDialogRefreshExceptionLeavesBindingAndSelectionUnchanged()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            Action originalRefresh = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, _, _, _, _) => Root + "/RefreshFailure.prefab";
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () => throw new InvalidOperationException("Injected refresh failure");

                Assert.That(window.TryCreateDatabaseWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Injected refresh failure"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(Root + "/RefreshFailure.prefab"), Is.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = originalRefresh;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_OpenDialogBindsOnlyReturnedProjectPrefabAndPreservesSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Func<string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.OpenDatabasePanel;
            Func<string, string> originalRelativePath = ShapeSyncDatabaseWindow.ToProjectRelativePath;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                const string dialogPath = "C:/Injected/ChosenDatabase.prefab";
                string receivedTitle = null;
                string receivedExtension = null;
                ShapeSyncDatabaseWindow.OpenDatabasePanel = (title, _, extension) => { receivedTitle = title; receivedExtension = extension; return dialogPath; };
                ShapeSyncDatabaseWindow.ToProjectRelativePath = path => path == dialogPath ? AssetDatabase.GetAssetPath(database) : string.Empty;

                Assert.That(window.TryOpenDatabaseWithDialog(out string diagnostic), Is.True, diagnostic);
                Assert.That(receivedTitle, Is.EqualTo("Open ShapeSync Database"));
                Assert.That(receivedExtension, Is.EqualTo("prefab"));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.OpenDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = originalRelativePath;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [TestCase("Cancel")]
        [TestCase("DialogException")]
        [TestCase("ExternalPath")]
        public void GeneralCommands_OpenDialogFailureLeavesBindingAndSelectionUnchanged(string failure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            Func<string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.OpenDatabasePanel;
            Func<string, string> originalRelativePath = ShapeSyncDatabaseWindow.ToProjectRelativePath;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.OpenDatabasePanel = (_, _, _) => failure == "DialogException" ? throw new InvalidOperationException("Injected open dialog failure") : "C:/Outside/NotADatabase.prefab";
                if (failure == "Cancel") ShapeSyncDatabaseWindow.OpenDatabasePanel = (_, _, _) => string.Empty;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = _ => string.Empty;

                Assert.That(window.TryOpenDatabaseWithDialog(out string diagnostic), Is.False);
                if (failure == "Cancel") Assert.That(diagnostic, Is.Null);
                else Assert.That(diagnostic, Is.Not.Null.And.Not.Empty);
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.OpenDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = originalRelativePath;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_OpenDialogPathResolutionExceptionLeavesBindingAndSelectionUnchanged()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            Func<string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.OpenDatabasePanel;
            Func<string, string> originalRelativePath = ShapeSyncDatabaseWindow.ToProjectRelativePath;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.OpenDatabasePanel = (_, _, _) => "C:/Injected/ChosenDatabase.prefab";
                ShapeSyncDatabaseWindow.ToProjectRelativePath = _ => throw new InvalidOperationException("Injected path resolution failure");

                Assert.That(window.TryOpenDatabaseWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Injected path resolution failure"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.OpenDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = originalRelativePath;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [TestCase(true, ShapeSyncDatabaseAsset.IntermediateContainerName)]
        [TestCase(false, "ShapeSyncDatabase component")]
        public void GeneralCommands_OpenDialogRejectsInvalidProjectPrefabWithoutChangingBindingOrSelection(bool hasDatabaseComponent, string expectedDiagnostic)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existing, out string existingDiagnostic), Is.True, existingDiagnostic);
            string invalidPath = Root + (hasDatabaseComponent ? "/MissingIntermediate.prefab" : "/NotADatabase.prefab");
            Func<string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.OpenDatabasePanel;
            Func<string, string> originalRelativePath = ShapeSyncDatabaseWindow.ToProjectRelativePath;
            Object originalSelection = Selection.activeObject;
            GameObject invalidRoot = new GameObject("InvalidDatabase");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                if (hasDatabaseComponent) invalidRoot.AddComponent<ShapeSyncDatabase>();
                PrefabUtility.SaveAsPrefabAsset(invalidRoot, invalidPath);
                Assert.That(window.TrySetDatabase(existing, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.OpenDatabasePanel = (_, _, _) => "C:/Injected/InvalidDatabase.prefab";
                ShapeSyncDatabaseWindow.ToProjectRelativePath = _ => invalidPath;

                Assert.That(window.TryOpenDatabaseWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain(expectedDiagnostic));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existing));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.OpenDatabasePanel = originalPanel;
                ShapeSyncDatabaseWindow.ToProjectRelativePath = originalRelativePath;
                Object.DestroyImmediate(invalidRoot);
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_SaveDialogReceivesSelectedFolderAssetParentOrAssetsRoot()
        {
            const string materialPath = Root + "/SelectedForDialog.mat";
            Func<string, string, string, string, string, string> originalPanel = ShapeSyncDatabaseWindow.SaveDatabasePanel;
            Object originalSelection = Selection.activeObject;
            Material material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                AssetDatabase.CreateAsset(material, materialPath);
                Object folder = AssetDatabase.LoadAssetAtPath<Object>(Root);
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(materialPath);
                string receivedName = null;
                string receivedFolder = null;
                ShapeSyncDatabaseWindow.SaveDatabasePanel = (_, name, _, _, folderPath) => { receivedName = name; receivedFolder = folderPath; return string.Empty; };

                Selection.activeObject = folder;
                window.TryCreateDatabaseWithDialog(out _);
                Assert.That(receivedName, Is.EqualTo("ShapeSyncDatabase"));
                Assert.That(receivedFolder, Is.EqualTo(Root));

                Selection.activeObject = asset;
                window.TryCreateDatabaseWithDialog(out _);
                Assert.That(receivedFolder, Is.EqualTo(Root));

                Selection.activeObject = null;
                window.TryCreateDatabaseWithDialog(out _);
                Assert.That(receivedFolder, Is.EqualTo("Assets"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveDatabasePanel = originalPanel;
                Selection.activeObject = originalSelection;
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(0, true, false)]
        [TestCase(1, false, true)]
        [TestCase(2, false, false)]
        public void Navigation_DirtyDetailResolvesSaveIgnoreOrCancel(int dialogChoice, bool saveSucceeds, bool ignores)
        {
            Func<ShapeSyncDatabaseWindow.Section, bool> originalDirty = ShapeSyncDatabaseWindow.IsDetailDirty;
            Func<ShapeSyncDatabaseWindow.Section, string> originalSave = ShapeSyncDatabaseWindow.SaveDirtyDetail;
            Action<ShapeSyncDatabaseWindow.Section> originalIgnore = ShapeSyncDatabaseWindow.IgnoreDirtyDetail;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            int saveCalls = 0;
            int ignoreCalls = 0;
            try
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = _ => true;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = _ => { saveCalls++; return saveSucceeds ? null : "Injected save failure"; };
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = _ => ignoreCalls++;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => dialogChoice;

                bool navigated = window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure);
                Assert.That(navigated, Is.EqualTo(dialogChoice != 2 && (dialogChoice != 0 || saveSucceeds)));
                Assert.That(window.SelectedSection, Is.EqualTo(navigated ? ShapeSyncDatabaseWindow.Section.Figure : ShapeSyncDatabaseWindow.Section.General));
                Assert.That(saveCalls, Is.EqualTo(dialogChoice == 0 ? 1 : 0));
                Assert.That(ignoreCalls, Is.EqualTo(ignores ? 1 : 0));
                if (dialogChoice == 0 && !saveSucceeds) Assert.That(window.Diagnostic, Is.EqualTo("Injected save failure"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = originalDirty;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = originalSave;
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = originalIgnore;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Navigation_DirtyDialogExceptionRetainsCurrentSectionAndReportsDiagnostic()
        {
            Func<ShapeSyncDatabaseWindow.Section, bool> originalDirty = ShapeSyncDatabaseWindow.IsDetailDirty;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = _ => true;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => throw new InvalidOperationException("Injected dialog failure");

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(window.Diagnostic, Does.Contain("Injected dialog failure"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = originalDirty;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Navigation_DirtyQueryExceptionRetainsCurrentSectionAndReportsDiagnostic()
        {
            Func<ShapeSyncDatabaseWindow.Section, bool> originalDirty = ShapeSyncDatabaseWindow.IsDetailDirty;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = _ => throw new InvalidOperationException("Injected dirty query failure");

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(window.Diagnostic, Does.Contain("Injected dirty query failure"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = originalDirty;
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(0, "Could not save")]
        [TestCase(1, "Could not ignore")]
        public void Navigation_DirtyHandlerExceptionRetainsCurrentSectionAndReportsDiagnostic(int dialogChoice, string expectedDiagnostic)
        {
            Func<ShapeSyncDatabaseWindow.Section, bool> originalDirty = ShapeSyncDatabaseWindow.IsDetailDirty;
            Func<ShapeSyncDatabaseWindow.Section, string> originalSave = ShapeSyncDatabaseWindow.SaveDirtyDetail;
            Action<ShapeSyncDatabaseWindow.Section> originalIgnore = ShapeSyncDatabaseWindow.IgnoreDirtyDetail;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = _ => true;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = _ => throw new InvalidOperationException("Injected save handler failure");
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = _ => throw new InvalidOperationException("Injected ignore handler failure");
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => dialogChoice;

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(window.Diagnostic, Does.Contain(expectedDiagnostic));
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = originalDirty;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = originalSave;
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = originalIgnore;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GeneralCommands_ResolveNewDatabaseFolderFromFolderAssetOrAssetsRoot()
        {
            const string materialPath = Root + "/SelectedAsset.mat";
            Material material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                AssetDatabase.CreateAsset(material, materialPath);
                Object folder = AssetDatabase.LoadAssetAtPath<Object>(Root);
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(materialPath);

                Assert.That(ShapeSyncDatabaseWindow.GetSelectedFolderPath(folder), Is.EqualTo(Root));
                Assert.That(ShapeSyncDatabaseWindow.GetSelectedFolderPath(asset), Is.EqualTo(Root));
                Assert.That(ShapeSyncDatabaseWindow.GetSelectedFolderPath(null), Is.EqualTo("Assets"));
            }
            finally
            {
                // TearDown owns the folder and its test asset.
            }
        }

        [Test]
        public void GeneralCommands_RollsBackCreatedDatabaseWhenPostCreateAdmissionFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existingDatabase, out string existingDiagnostic), Is.True, existingDiagnostic);
            ShapeSyncDatabaseWindow.DatabaseCreator originalCreate = ShapeSyncDatabaseWindow.CreateDatabase;
            ShapeSyncDatabaseWindow.DatabaseOpener originalOpen = ShapeSyncDatabaseWindow.OpenDatabase;
            Func<string, bool> originalDelete = ShapeSyncDatabaseWindow.DeleteDatabaseAsset;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            string createdPath = null;
            try
            {
                Assert.That(window.TrySetDatabase(existingDatabase, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.CreateDatabase = (string folderPath, out ShapeSyncDatabase created, out string diagnostic) =>
                {
                    bool result = originalCreate(folderPath, out created, out diagnostic);
                    createdPath = AssetDatabase.GetAssetPath(created);
                    return result;
                };
                ShapeSyncDatabaseWindow.OpenDatabase = (string _, out ShapeSyncDatabase opened, out string diagnostic) =>
                {
                    opened = null;
                    diagnostic = "Injected post-create admission failure";
                    return false;
                };

                Assert.That(window.TryCreateDatabase(Root, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Injected post-create admission failure"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existingDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
                Assert.That(createdPath, Is.Not.Null.And.Not.Empty);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(createdPath), Is.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateDatabase = originalCreate;
                ShapeSyncDatabaseWindow.OpenDatabase = originalOpen;
                ShapeSyncDatabaseWindow.DeleteDatabaseAsset = originalDelete;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void PrefabAssignment_DefaultsOnlyBlankFigureOrFbmNames()
        {
            GameObject prefab = new GameObject("BasicFemale_Tall");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment(null, prefab), Is.EqualTo("BasicFemale_Tall"));
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment(string.Empty, prefab), Is.EqualTo("BasicFemale_Tall"));
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment("  ", prefab), Is.EqualTo("BasicFemale_Tall"));
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment("Figure_Authored", prefab), Is.EqualTo("Figure_Authored"));
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment("Fbm_Authored", prefab), Is.EqualTo("Fbm_Authored"));
                Assert.That(ShapeSyncDatabaseWindow.GetNameAfterPrefabAssignment(string.Empty, null), Is.EqualTo(string.Empty));

                window.SetFigureInputsForTest(string.Empty, null);
                window.AssignFigurePrefabFromUiForTest(prefab);
                Assert.That(window.FigureName, Is.EqualTo("BasicFemale_Tall"), "Figure Prefab assignment must fill an empty Figure Name.");
                window.SetFigureInputsForTest("Figure_Authored", null);
                window.AssignFigurePrefabFromUiForTest(prefab);
                Assert.That(window.FigureName, Is.EqualTo("Figure_Authored"), "Figure Prefab assignment must not overwrite an authored Figure Name.");

                window.SetFbmAxisDraftsForTest(new[] { string.Empty, "Fbm_Authored" }, new GameObject[] { null, null });
                Assert.That(window.AssignFbmAxisDraftPrefabFromUiForTest(0, prefab), Is.True);
                Assert.That(window.AssignFbmAxisDraftPrefabFromUiForTest(1, prefab), Is.True);
                Assert.That(window.FbmAxisDraftNamesForTest, Is.EqualTo(new[] { "BasicFemale_Tall", "Fbm_Authored" }), "New FBM Prefab assignment must fill only a blank FBM Name.");
                Assert.That(window.AssignFbmAxisDraftPrefabFromUiForTest(2, prefab), Is.False);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void GeneralCommands_RetainsCreatedDatabaseAndReportsCleanupFailureWhenRollbackCannotDelete()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existingDatabase, out string existingDiagnostic), Is.True, existingDiagnostic);
            ShapeSyncDatabaseWindow.DatabaseCreator originalCreate = ShapeSyncDatabaseWindow.CreateDatabase;
            ShapeSyncDatabaseWindow.DatabaseOpener originalOpen = ShapeSyncDatabaseWindow.OpenDatabase;
            Func<string, bool> originalDelete = ShapeSyncDatabaseWindow.DeleteDatabaseAsset;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            string createdPath = null;
            bool result = true;
            string diagnostic = null;
            try
            {
                Assert.That(window.TrySetDatabase(existingDatabase, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.CreateDatabase = (string folderPath, out ShapeSyncDatabase created, out string diagnostic) =>
                {
                    bool result = originalCreate(folderPath, out created, out diagnostic);
                    createdPath = AssetDatabase.GetAssetPath(created);
                    return result;
                };
                ShapeSyncDatabaseWindow.OpenDatabase = (string _, out ShapeSyncDatabase opened, out string diagnostic) =>
                {
                    opened = null;
                    diagnostic = "Injected post-create admission failure";
                    return false;
                };
                ShapeSyncDatabaseWindow.DeleteDatabaseAsset = _ => false;

                Assert.DoesNotThrow(() => result = window.TryCreateDatabase(Root, out diagnostic));
                Assert.That(result, Is.False);
                Assert.That(diagnostic, Does.Contain("could not be cleaned up"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existingDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
                Assert.That(createdPath, Is.Not.Null.And.Not.Empty);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(createdPath), Is.Not.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateDatabase = originalCreate;
                ShapeSyncDatabaseWindow.OpenDatabase = originalOpen;
                ShapeSyncDatabaseWindow.DeleteDatabaseAsset = originalDelete;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void GeneralCommands_RetainsCreatedDatabaseAndReportsCleanupExceptionWithoutLeaking()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase existingDatabase, out string existingDiagnostic), Is.True, existingDiagnostic);
            ShapeSyncDatabaseWindow.DatabaseCreator originalCreate = ShapeSyncDatabaseWindow.CreateDatabase;
            ShapeSyncDatabaseWindow.DatabaseOpener originalOpen = ShapeSyncDatabaseWindow.OpenDatabase;
            Func<string, bool> originalDelete = ShapeSyncDatabaseWindow.DeleteDatabaseAsset;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            string createdPath = null;
            bool result = true;
            string diagnostic = null;
            try
            {
                Assert.That(window.TrySetDatabase(existingDatabase, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.CreateDatabase = (string folderPath, out ShapeSyncDatabase created, out string createDiagnostic) =>
                {
                    bool createResult = originalCreate(folderPath, out created, out createDiagnostic);
                    createdPath = AssetDatabase.GetAssetPath(created);
                    return createResult;
                };
                ShapeSyncDatabaseWindow.OpenDatabase = (string _, out ShapeSyncDatabase opened, out string openDiagnostic) =>
                {
                    opened = null;
                    openDiagnostic = "Injected post-create admission failure";
                    return false;
                };
                ShapeSyncDatabaseWindow.DeleteDatabaseAsset = _ => throw new InvalidOperationException("Injected cleanup exception");

                Assert.DoesNotThrow(() => result = window.TryCreateDatabase(Root, out diagnostic));
                Assert.That(result, Is.False);
                Assert.That(diagnostic, Does.Contain("Injected cleanup exception"));
                Assert.That(window.Diagnostic, Is.EqualTo(diagnostic));
                Assert.That(window.Database, Is.SameAs(existingDatabase));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
                Assert.That(createdPath, Is.Not.Null.And.Not.Empty);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(createdPath), Is.Not.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.CreateDatabase = originalCreate;
                ShapeSyncDatabaseWindow.OpenDatabase = originalOpen;
                ShapeSyncDatabaseWindow.DeleteDatabaseAsset = originalDelete;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void NavigationTreeView_SelectsGeneralFigureAndShapesWithoutChangingDatabaseOrSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Object originalSelection = Selection.activeObject;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView treeView = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(), window.TryNavigateTo, () => window.SelectedSection);
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 2 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Figure));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 2 }));
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 3 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Materials));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 3 }));
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 9 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Normals));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 9 }));
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 4 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Shapes));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 4 }));
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 5 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Textures));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 5 }));
                Assert.That(treeView.RootDisplayNamesForTest, Is.EqualTo(new[] { "General", "Figure", "Outfits", "Shapes", "Textures", "Generation" }));
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 10 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Generation));
                Assert.That(window.GenerationPathsForTest, Is.EqualTo(new[] { "Registries/", "Bindings/", "Materials/", "Textures/", "Outfits/" }));
                window.SetGenerationPathsForTest("Custom/Registries", "Custom/Bindings", "Custom/Materials", "Custom/Textures", "Custom/Outfits");
                Assert.That(window.IsGenerationDetailDirtyForTest, Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 1 });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 1 }));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.IsGenerationDetailDirtyForTest, Is.False);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase persisted, out string persistedDiagnostic), Is.True, persistedDiagnostic);
                Assert.That(persisted.Registry.GenerationPaths.RegistriesPath, Is.EqualTo("Custom/Registries"));
                Assert.That(persisted.Registry.GenerationPaths.BindingsPath, Is.EqualTo("Custom/Bindings"));
                Assert.That(persisted.Registry.GenerationPaths.MaterialsPath, Is.EqualTo("Custom/Materials"));
                Assert.That(persisted.Registry.GenerationPaths.TexturesPath, Is.EqualTo("Custom/Textures"));
                Assert.That(persisted.Registry.GenerationPaths.OutfitsPath, Is.EqualTo("Custom/Outfits"));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void FigureDetail_RequiresDatabaseAndFigureNameBeforeAdmissionOrImport()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                window.SetFigureInputsForTest("MasterFigure", null);
                Assert.That(window.TrySaveFigure(out string databaseDiagnostic), Is.False);
                Assert.That(databaseDiagnostic, Does.Contain("Select or create"));
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFigureInputsForTest(string.Empty, null);
                Assert.That(window.TrySaveFigure(out string nameDiagnostic), Is.False);
                Assert.That(nameDiagnostic, Does.Contain("Figure Name"));
                Assert.That(window.DatabaseFigurePrefab, Is.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FigureDetail_ChangedInputsBecomeDirtyAndIgnoreRestoresAcceptedDraft(bool changeName, bool changePrefab)
        {
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            Object originalSelection = Selection.activeObject;
            GameObject selectionSentinel = new GameObject("SelectionSentinel");
            try
            {
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.True);
                Assert.That(window.IsFigureSaveEnabledForTest, Is.False);
                window.SetFigureInputsForTest(changeName ? "DraftFigure" : null, changePrefab ? candidate : null);
                Selection.activeObject = selectionSentinel;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;

                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(window.IsFigureSaveEnabledForTest, Is.True);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Shapes), Is.True);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Shapes));
                Assert.That(window.FigureName, Is.Null);
                Assert.That(window.FigurePrefab, Is.Null);
                Assert.That(window.IsFigureDetailDirtyForTest, Is.False);
                Assert.That(Selection.activeObject, Is.SameAs(selectionSentinel));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Selection.activeObject = originalSelection;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
                Object.DestroyImmediate(selectionSentinel);
            }
        }

        [Test]
        public void FigureDetail_DirtyCancelRetainsDraftAndCurrentSection()
        {
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            try
            {
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.True);
                window.SetFigureInputsForTest("DraftFigure", candidate);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Figure));
                Assert.That(window.FigureName, Is.EqualTo("DraftFigure"));
                Assert.That(window.FigurePrefab, Is.SameAs(candidate));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void FigureDetail_DirtySaveImportsThenAcceptsDraftBeforeNavigating()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow.FigureAdmitter originalAdmit = ShapeSyncDatabaseWindow.AdmitFigure;
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            ShapeSyncDatabaseWindow.FigureImporter originalImport = ShapeSyncDatabaseWindow.ImportFigure;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            GameObject rendererObject = new GameObject("Face");
            try
            {
                ShapeSyncFigureImportAdmission admission = new ShapeSyncFigureImportAdmission(candidate, candidate, null, null, new[] { rendererObject.AddComponent<SkinnedMeshRenderer>() });
                ShapeSyncDatabaseWindow.AdmitFigure = (GameObject _, out ShapeSyncFigureImportAdmission admitted, out string diagnostic) => { admitted = admission; diagnostic = null; return true; };
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (_, _, _) => true;
                ShapeSyncDatabaseWindow.ImportFigure = (string _, ShapeSyncFigureImportAdmission __, string ___, out string diagnostic) => { diagnostic = null; return true; };
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.True);
                window.SetFigureInputsForTest("MasterFigure", candidate);

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(window.FigureName, Is.EqualTo("MasterFigure"));
                Assert.That(window.FigurePrefab, Is.SameAs(candidate));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.False);
            }
            finally
            {
                ShapeSyncDatabaseWindow.AdmitFigure = originalAdmit;
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                ShapeSyncDatabaseWindow.ImportFigure = originalImport;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
                Object.DestroyImmediate(rendererObject);
            }
        }

        [TestCase(1, true)]
        [TestCase(2, false)]
        public void FigureDetail_DirtyTreeViewIgnoreOrCancelRestoresSelectionAndCorrectDraftState(int dialogChoice, bool expectDiscard)
        {
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            try
            {
                ShapeSyncDatabaseWindow.NavigationTreeView treeView = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(), window.TryNavigateTo, () => window.SelectedSection);
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 2 });
                window.SetFigureInputsForTest("DraftFigure", candidate);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => dialogChoice;

                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 1 });

                Assert.That(window.SelectedSection, Is.EqualTo(expectDiscard ? ShapeSyncDatabaseWindow.Section.General : ShapeSyncDatabaseWindow.Section.Figure));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { expectDiscard ? 1 : 2 }));
                Assert.That(window.FigureName, Is.EqualTo(expectDiscard ? null : "DraftFigure"));
                Assert.That(window.FigurePrefab, Is.SameAs(expectDiscard ? null : candidate));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.EqualTo(!expectDiscard));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
            }
        }

        [TestCase("Admission")]
        [TestCase("ConfirmCancel")]
        [TestCase("Import")]
        public void FigureDetail_DirtyTreeViewSaveFailureRetainsDraftSelectionBindingAndUnitySelection(string failure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow.FigureAdmitter originalAdmit = ShapeSyncDatabaseWindow.AdmitFigure;
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            ShapeSyncDatabaseWindow.FigureImporter originalImport = ShapeSyncDatabaseWindow.ImportFigure;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            GameObject rendererObject = new GameObject("Face");
            GameObject selectionSentinel = new GameObject("SelectionSentinel");
            try
            {
                ShapeSyncFigureImportAdmission admission = new ShapeSyncFigureImportAdmission(candidate, candidate, null, null, new[] { rendererObject.AddComponent<SkinnedMeshRenderer>() });
                ShapeSyncDatabaseWindow.AdmitFigure = (GameObject _, out ShapeSyncFigureImportAdmission admitted, out string diagnostic) =>
                {
                    admitted = failure == "Admission" ? null : admission;
                    diagnostic = "Injected " + failure + " failure";
                    return failure != "Admission";
                };
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (_, _, _) => failure != "ConfirmCancel";
                ShapeSyncDatabaseWindow.ImportFigure = (string _, ShapeSyncFigureImportAdmission __, string ___, out string diagnostic) => { diagnostic = "Injected import failure"; return false; };
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView treeView = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(), window.TryNavigateTo, () => window.SelectedSection);
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 2 });
                window.SetFigureInputsForTest("DraftFigure", candidate);
                Selection.activeObject = selectionSentinel;

                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 1 });

                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Figure));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 2 }));
                Assert.That(window.FigureName, Is.EqualTo("DraftFigure"));
                Assert.That(window.FigurePrefab, Is.SameAs(candidate));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(Selection.activeObject, Is.SameAs(selectionSentinel));
            }
            finally
            {
                ShapeSyncDatabaseWindow.AdmitFigure = originalAdmit;
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                ShapeSyncDatabaseWindow.ImportFigure = originalImport;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Selection.activeObject = originalSelection;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(selectionSentinel);
            }
        }

        [Test]
        public void FigureDetail_RebindingDatabaseResetsDirtyDraftAndSameAcceptedInputDoesNotPrompt()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase firstDatabase, out string firstDiagnostic), Is.True, firstDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase secondDatabase, out string secondDiagnostic), Is.True, secondDiagnostic);
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject candidate = new GameObject("DraftCandidate");
            try
            {
                Assert.That(window.TrySetDatabase(firstDatabase, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Figure), Is.True);
                window.SetFigureInputsForTest("DraftFigure", candidate);
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(window.TrySetDatabase(secondDatabase, out bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.FigureName, Is.Null);
                Assert.That(window.FigurePrefab, Is.Null);
                Assert.That(window.IsFigureDetailDirtyForTest, Is.False);

                window.SetFigureInputsForTest(null, null);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => throw new InvalidOperationException("Same input must not prompt");
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void FigureDetail_ReopeningDatabaseResolvesExistingFigurePrefabByFigureName()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (_, intermediate) =>
            {
                GameObject figure = new GameObject("MasterFigure");
                figure.transform.SetParent(intermediate, false);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFigureInputsForTest("MasterFigure", null);
                Assert.That(window.DatabaseFigurePrefab, Is.Not.Null);
                Assert.That(window.DatabaseFigurePrefab.name, Is.EqualTo("MasterFigure"));

                window.SetFigureInputsForTest("UnknownFigure", null);
                Assert.That(window.DatabaseFigurePrefab, Is.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void DatabaseRegistry_RejectsMultipleOrInvalidBaseEntries()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            ShapeSyncDatabaseRegistry invalidRegistry = null;
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject figure = new GameObject("Base");
            GameObject otherFigure = new GameObject("OtherBase");
            GameObject nestedParent = new GameObject("Nested");
            GameObject nestedFigure = new GameObject("NestedBase");
            figure.transform.SetParent(intermediate.transform, false);
            otherFigure.transform.SetParent(intermediate.transform, false);
            nestedParent.transform.SetParent(intermediate.transform, false);
            nestedFigure.transform.SetParent(nestedParent.transform, false);
            GameObject external = new GameObject("External");
            try
            {
                Assert.That(registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry empty, out string emptyDiagnostic), Is.True);
                Assert.That(empty, Is.Null); Assert.That(emptyDiagnostic, Is.Null);
                Assert.That(registry.TryRegisterBaseFigure(database, "External", external, out string externalDiagnostic), Is.False);
                Assert.That(externalDiagnostic, Does.Contain("Intermediate"));
                Assert.That(registry.TryRegisterBaseFigure(database, "NestedBase", nestedFigure, out string nestedDiagnostic), Is.False);
                Assert.That(nestedDiagnostic, Does.Contain("Intermediate"));
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", figure, out string registerDiagnostic), Is.True, registerDiagnostic);
                Assert.That(registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry entry, out string validDiagnostic), Is.True, validDiagnostic);
                Assert.That(registry.TryRegisterBaseFigure(database, "OtherBase", otherFigure, out string secondDiagnostic), Is.False);
                Assert.That(secondDiagnostic, Does.Contain("EntityCardinality"));
                Assert.That(registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry stillSingle, out string singleDiagnostic), Is.True, singleDiagnostic);
                Assert.That(stillSingle, Is.SameAs(entry));
                Assert.That(registry.BaseFigures, Has.Count.EqualTo(1));
                Object.DestroyImmediate(otherFigure); otherFigure = null;
                invalidRegistry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
                Assert.That(invalidRegistry.TryRegisterBaseFigure(database, "Base", figure, out registerDiagnostic), Is.True, registerDiagnostic);
                figure.name = "Renamed";
                Assert.That(invalidRegistry.TryGetSingleBaseFigure(database, out _, out string invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic, Does.Contain("invalid"));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(invalidRegistry); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(external); }
        }

        [Test]
        public void MaterialEntryAdmission_RequiresTheRegisteredBaseRendererExactSlotAndUniqueName()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            Material first = new Material(shader);
            Material second = new Material(shader);
            renderer.sharedMaterials = new[] { first, second };
            GameObject externalRoot = new GameObject("External");
            SkinnedMeshRenderer externalRenderer = externalRoot.AddComponent<SkinnedMeshRenderer>();
            externalRenderer.sharedMaterial = first;
            try
            {
                Assert.That(registry.TryValidateMaterialEntry(database, "Body", renderer, 0, first, out string beforeBaseDiagnostic), Is.False);
                Assert.That(beforeBaseDiagnostic, Does.Contain("requires one"));
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(registry.TryValidateMaterialEntry(database, "Body", renderer, 0, first, out string validDiagnostic), Is.True, validDiagnostic);
                Assert.That(registry.TryValidateMaterialEntry(database, "", renderer, 0, first, out string emptyDiagnostic), Is.False);
                Assert.That(emptyDiagnostic, Does.Contain("empty"));
                Assert.That(registry.TryValidateMaterialEntry(database, "Body", externalRenderer, 0, first, out string externalDiagnostic), Is.False);
                Assert.That(externalDiagnostic, Does.Contain("registered Base"));
                Assert.That(registry.TryValidateMaterialEntry(database, "Body", renderer, 2, first, out string slotDiagnostic), Is.False);
                Assert.That(slotDiagnostic, Does.Contain("slot"));
                Assert.That(registry.TryValidateMaterialEntry(database, "Body", renderer, 1, first, out string materialDiagnostic), Is.False);
                Assert.That(materialDiagnostic, Does.Contain("match"));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(externalRoot); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void ExtraMorphsDetail_MorphDraftParticipatesInDirtyAndDiscardState()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.ExtraMorphs);
                window.SetFigureMorphDraftForTest(11, new[] { "Candidate" });
                Assert.That(window.IsFigureDetailDirtyForTest, Is.False);
                Assert.That(window.IsExtraMorphsDetailDirtyForTest, Is.True);
                window.DiscardExtraMorphDraftForTest();
                Assert.That(window.PcmSlotsForTest, Is.EqualTo(11), "Extra Morphs Ignore must not discard the Figure-owned PCM draft.");
                Assert.That(window.KeptRawMorphsForTest, Is.Empty);
                Assert.That(window.IsExtraMorphsDetailDirtyForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void FigureDetail_PcmSlotsSaveWithoutFbmAndDoNotDependOnExtraMorphs()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                window.SetFigureMorphDraftForTest(17, Array.Empty<string>());
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveFigure(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(reopened.Registry.PcmSlots, Is.EqualTo(17));
                Assert.That(reopened.Registry.KeptRawBlendShapeNames, Is.Empty);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void NavigationTreeView_ExtraMorphsChildSelectsItsIndependentSection()
        {
            var state = new ShapeSyncTreeViewState();
            ShapeSyncDatabaseWindow.Section selected = ShapeSyncDatabaseWindow.Section.Figure;
            var tree = new ShapeSyncDatabaseWindow.NavigationTreeView(state, section => { selected = section; return true; }, () => selected);
            tree.ApplySelectionChangeForTest(new[] { 6 });
            Assert.That(selected, Is.EqualTo(ShapeSyncDatabaseWindow.Section.ExtraMorphs));
        }

        [Test]
        public void NavigationTreeView_FigureChildrenPlaceExtraMorphsLast()
        {
            var tree = new ShapeSyncDatabaseWindow.NavigationTreeView(
                new ShapeSyncTreeViewState(),
                _ => true,
                () => ShapeSyncDatabaseWindow.Section.Figure);

            Assert.That(tree.FigureChildDisplayNamesForTest, Is.EqualTo(new[] { "Materials", "Normals", "FBMs", "PBMs", "Extra Morphs" }));
        }

        [Test]
        public void ExtraMorphs_DirtyNavigationCancelRetainsExtraMorphsSection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.ExtraMorphs);
                window.SetFigureMorphDraftForTest(11, new[] { "Candidate" });
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Shapes), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.ExtraMorphs));
            }
            finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog; Object.DestroyImmediate(window); }
        }

        [Test]
        public void FigureAxisAdmission_RequiresBaseAndRegistersOneSharedCanonicalNamespace()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryAdmitFigureAxes(database, Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisDraft>(), out _, out string emptyBatchDiagnostic), Is.False);
                Assert.That(emptyBatchDiagnostic, Does.Contain("requires at least one"));
                Assert.That(registry.TryValidateFigureAxis(database, "Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, out string beforeBaseDiagnostic), Is.False);
                Assert.That(beforeBaseDiagnostic, Does.Contain("requires one registered Base"));

                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(registry.TryCommitFigureAxes(database, new[] { default(ShapeSyncDatabaseRegistry.FigureAxisAdmission) }, out string defaultAdmissionDiagnostic), Is.False);
                Assert.That(defaultAdmissionDiagnostic, Does.Contain("Figure bindings"));
                Assert.That(registry.FigureAxes, Is.Empty);
                Assert.That(registry.TryAdmitFigureAxes(database, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out _, out string pbmOnlyDiagnostic), Is.False);
                Assert.That(pbmOnlyDiagnostic, Does.Contain("first Figure-axis admission"));
                ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts =
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                };
                Assert.That(registry.TryAdmitFigureAxes(database, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                Assert.That(registry.TryAdmitFigureAxes(database, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Future", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] laterConflictingAdmissions, out string laterAdmissionDiagnostic), Is.True, laterAdmissionDiagnostic);
                Assert.That(registry.TryAdmitFigureAxes(database, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out _, out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("duplicated in this transaction"));
                Assert.That(registry.FigureAxes, Is.Empty);
                Assert.That(registry.TryCommitFigureAxes(database, admissions, out string bindingRequiredDiagnostic), Is.False);
                Assert.That(bindingRequiredDiagnostic, Does.Contain("Figure bindings"));
                GameObject tall = new GameObject("Tall"); tall.transform.SetParent(intermediate.transform, false);
                GameObject longArmsBase = new GameObject("Base_LongArms"); longArmsBase.transform.SetParent(intermediate.transform, false);
                GameObject longArmsTall = new GameObject("Tall_LongArms"); longArmsTall.transform.SetParent(intermediate.transform, false);
                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] bindings =
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tall) },
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, longArmsBase),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", longArmsTall)
                    }
                };
                Assert.That(registry.TryCommitFigureAxes(database, admissions, bindings, out string commitDiagnostic), Is.True, commitDiagnostic);
                Assert.That(registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
                {
                    ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }));
                GameObject renameCollision = new GameObject("Renamed_LongArms");
                renameCollision.transform.SetParent(intermediate.transform, false);
                Assert.That(registry.TryRenameBaseFigure(database, "Base", "Renamed", out string renameCollisionDiagnostic), Is.False);
                Assert.That(renameCollisionDiagnostic, Does.Contain("conflicts"));
                Assert.That(baseFigure.name, Is.EqualTo("Base"));
                Assert.That(longArmsBase.name, Is.EqualTo("Base_LongArms"));
                Object.DestroyImmediate(renameCollision);
                Assert.That(registry.TryRenameBaseFigure(database, "Base", "Renamed", out string renameBaseWithPbmDiagnostic), Is.True, renameBaseWithPbmDiagnostic);
                Assert.That(baseFigure.name, Is.EqualTo("Renamed"));
                Assert.That(longArmsBase.name, Is.EqualTo("Renamed_LongArms"));
                Assert.That(longArmsTall.name, Is.EqualTo("Tall_LongArms"));
                Assert.That(registry.TryValidateFigureAxisState(database, out string renamedPbmValidationDiagnostic), Is.True, renamedPbmValidationDiagnostic);
                Assert.That(registry.TryRenameBaseFigure(database, "Renamed", "Base", out string restoreBaseWithPbmDiagnostic), Is.True, restoreBaseWithPbmDiagnostic);

                Assert.That(registry.TryCommitFigureAxes(database, admissions, bindings, out string existingAxisDiagnostic), Is.False);
                Assert.That(existingAxisDiagnostic, Does.Contain("already exists"));
                Assert.That(registry.FigureAxes, Has.Count.EqualTo(2));

                Assert.That(registry.TryCommitFigureAxes(database, laterConflictingAdmissions, bindings, out string laterConflictDiagnostic), Is.False);
                Assert.That(laterConflictDiagnostic, Does.Contain("already exists"));
                Assert.That(registry.FigureAxes, Has.Count.EqualTo(2));
                Assert.That(registry.FigureAxes.Any(axis => axis.Name == "Future"), Is.False);

                Assert.That(registry.TryAdmitFigureAxes(database, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out _, out string finalizedDiagnostic), Is.False);
                Assert.That(finalizedDiagnostic, Does.Contain("duplicated in this transaction"));
                Assert.That(registry.TryValidateFigureAxis(database, "Invalid", (ShapeSyncDatabaseRegistry.FigureAxisKind)99, out string kindDiagnostic), Is.False);
                Assert.That(kindDiagnostic, Does.Contain("kind is invalid"));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); }
        }

        [Test]
        public void FigureAxisAdmission_RequiresEveryFbmBindingForPbmAndCommitsAllBindingsAtomically()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            GameObject tall = new GameObject("Tall");
            GameObject shortFigure = new GameObject("Short");
            GameObject longTall = new GameObject("Tall_Long");
            GameObject longShort = new GameObject("Short_Long");
            GameObject longBase = new GameObject("Base_Long");
            tall.transform.SetParent(intermediate.transform, false);
            shortFigure.transform.SetParent(intermediate.transform, false);
            longTall.transform.SetParent(intermediate.transform, false);
            longShort.transform.SetParent(intermediate.transform, false);
            longBase.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts =
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                };
                Assert.That(registry.TryAdmitFigureAxes(database, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] incomplete =
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tall) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Short", shortFigure) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", longTall) }
                };
                Assert.That(registry.TryCommitFigureAxes(database, admissions, incomplete, out string incompleteDiagnostic), Is.False);
                Assert.That(incompleteDiagnostic, Does.Contain("Base Figure binding"));
                Assert.That(registry.FigureAxes, Is.Empty);

                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] complete =
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tall) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Short", shortFigure) },
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, longBase),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", longTall),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Short", longShort)
                    }
                };
                Assert.That(registry.TryCommitFigureAxes(database, admissions, complete, out string commitDiagnostic), Is.True, commitDiagnostic);
                Assert.That(registry.FigureAxes[0].Figures.Single().Figure, Is.SameAs(tall));
                Assert.That(registry.FigureAxes[2].Figures.Select(entry => (entry.FbmName, entry.Figure)), Is.EqualTo(new[]
                {
                    (ShapeSyncDatabaseRegistry.BaseShapeKey, longBase), ("Tall", longTall), ("Short", longShort)
                }));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); }
        }

        [TestCase("FBM_Tall")]
        [TestCase("PBM_LongArms")]
        [TestCase("PCM_Waist")]
        [TestCase("MCM_Smile")]
        [TestCase("VRM_Blink")]
        [TestCase("Morph_Slot_0")]
        public void FigureAxisAdmission_RejectsEveryReservedPrefixAndEmptyName(string reservedName)
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(registry.TryValidateFigureAxis(database, reservedName, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, out string reservedDiagnostic), Is.False);
                Assert.That(reservedDiagnostic, Does.Contain("reserved prefix"));
                Assert.That(registry.TryValidateFigureAxis(database, "", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, out string emptyDiagnostic), Is.False);
                Assert.That(emptyDiagnostic, Does.Contain("must not be empty"));
                Assert.That(registry.TryValidateFigureAxis(database, ShapeSyncDatabaseRegistry.BaseShapeKey, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, out string baseKeyDiagnostic), Is.False);
                Assert.That(baseKeyDiagnostic, Does.Contain("Base Shape key"));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); }
        }

        [TestCase("Figure Name")]
        [TestCase("Entry Name")]
        [TestCase("FBM Name")]
        [TestCase("PBM Name")]
        [TestCase("Leading\tName")]
        public void UserAuthoredNames_RejectWhitespace(string value)
        {
            Assert.That(ShapeSyncDatabaseRegistry.IsValidUserName(value), Is.False);
            Assert.That(ShapeSyncDatabaseRegistry.IsValidUserName("Valid_Name"), Is.True);
        }

        [Test]
        public void UserAuthoredNames_RejectWhitespaceAtFigureEntryAndAxisSaveBoundaries()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject invalidFigure = new GameObject("Figure Name");
            invalidFigure.transform.SetParent(intermediate.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(database, "Figure Name", invalidFigure, out string figureDiagnostic), Is.False);
                Assert.That(figureDiagnostic, Does.Contain("named direct child"));
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);

                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(registry.TryValidateMaterialEntry(database, "Entry Name", renderer, 0, material, out string entryDiagnostic), Is.False);
                Assert.That(entryDiagnostic, Does.Contain("whitespace"));
                Assert.That(registry.TryRegisterMaterialEntry(database, "Entry", renderer, 0, material.name, material, adapter, out string registerEntryDiagnostic), Is.True, registerEntryDiagnostic);
                Assert.That(registry.TryRenameMaterialEntry("Entry", "Entry Name", out string renameEntryDiagnostic), Is.False);
                Assert.That(renameEntryDiagnostic, Does.Contain("whitespace"));

                Assert.That(registry.TryValidateFigureAxis(database, "FBM Name", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, out string fbmDiagnostic), Is.False);
                Assert.That(fbmDiagnostic, Does.Contain("whitespace"));
                Assert.That(registry.TryValidateFigureAxis(database, "PBM Name", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm, out string pbmDiagnostic), Is.False);
                Assert.That(pbmDiagnostic, Does.Contain("whitespace"));
                Assert.That(registry.TryRenameBaseFigure(database, "Base", "Figure Name", out string renameFigureDiagnostic), Is.False);
                Assert.That(renameFigureDiagnostic, Does.Contain("invalid"));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(databaseRoot);
            }
        }

        [Test]
        public void FigureAxisAdmission_CommitsAtomicallyAndPersistsAcrossDatabaseReopen()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                ShapeSyncDatabase databaseContents = contents.GetComponent<ShapeSyncDatabase>();
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(databaseContents.Registry.TryAdmitFigureAxes(databaseContents, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject tall = new GameObject("Tall"); tall.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                Assert.That(databaseContents.Registry.TryCommitFigureAxes(databaseContents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tall) }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Assert.That(ShapeSyncDatabaseAsset.TryLoad(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
            {
                ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
            }));
        }

        [Test]
        public void FigureAxisAdmission_RejectsCorruptSerializedAxisEntryWithoutMutatingRegistry()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base");
            baseFigure.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                SerializedObject serializedRegistry = new SerializedObject(registry);
                SerializedProperty axes = serializedRegistry.FindProperty("figureAxes");
                axes.arraySize = 1;
                axes.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = string.Empty;
                axes.GetArrayElementAtIndex(0).FindPropertyRelative("kind").enumValueIndex = (int)ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(registry.TryAdmitFigureAxes(database, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out _, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("registry entry is invalid"));
                Assert.That(registry.FigureAxes, Has.Count.EqualTo(1));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); }
        }

        [Test]
        public void FigureAxisAdmission_DerivesFbmFinalizationFromPersistedAxisEntries()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            try
            {
                SerializedObject serializedRegistry = new SerializedObject(registry);
                SerializedProperty axes = serializedRegistry.FindProperty("figureAxes");
                axes.arraySize = 1;
                axes.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "Tall";
                axes.GetArrayElementAtIndex(0).FindPropertyRelative("kind").enumValueIndex = (int)ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm;
                serializedRegistry.FindProperty("fbmAxesFinalized").boolValue = false;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(registry.FbmAxesFinalized, Is.True);
                Assert.That(registry.TryValidateFigureAxisState(out string diagnostic), Is.True, diagnostic);
            }
            finally { Object.DestroyImmediate(registry); }
        }

        [Test]
        public void FigureAxisAdmission_RejectsReopenedBindingThatIsNestedOrMissingFromPbmMatrix()
        {
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject databaseRoot = new GameObject("Database");
            ShapeSyncDatabase database = databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
            intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate.transform, false);
            GameObject tall = new GameObject("Tall"); tall.transform.SetParent(intermediate.transform, false);
            GameObject longBase = new GameObject("Base_Long"); longBase.transform.SetParent(intermediate.transform, false);
            GameObject longTall = new GameObject("Tall_Long"); longTall.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(registry.TryAdmitFigureAxes(database, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                Assert.That(registry.TryCommitFigureAxes(database, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tall) },
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, longBase),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", longTall)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
                Assert.That(registry.TryValidateFigureAxisState(database, out string validDiagnostic), Is.True, validDiagnostic);

                GameObject nestedContainer = new GameObject("Nested"); nestedContainer.transform.SetParent(intermediate.transform, false);
                tall.transform.SetParent(nestedContainer.transform, false);
                Assert.That(registry.TryValidateFigureAxisState(database, out string nestedDiagnostic), Is.False);
                Assert.That(nestedDiagnostic, Does.Contain("binding is invalid"));
                tall.transform.SetParent(intermediate.transform, false);

                SerializedObject serializedRegistry = new SerializedObject(registry);
                serializedRegistry.FindProperty("figureAxes").GetArrayElementAtIndex(1).FindPropertyRelative("figures").arraySize = 0;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(registry.TryValidateFigureAxisState(database, out string missingPbmDiagnostic), Is.False);
                Assert.That(missingPbmDiagnostic, Does.Contain("bindings are missing"));
            }
            finally { Object.DestroyImmediate(registry); Object.DestroyImmediate(databaseRoot); }
        }

        [Test]
        public void FigureAxisAdmission_RejectsAnAdmissionIssuedByAnotherRegistry()
        {
            ShapeSyncDatabaseRegistry firstRegistry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            ShapeSyncDatabaseRegistry secondRegistry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            GameObject firstRoot = new GameObject("FirstDatabase");
            GameObject secondRoot = new GameObject("SecondDatabase");
            ShapeSyncDatabase firstDatabase = firstRoot.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabase secondDatabase = secondRoot.AddComponent<ShapeSyncDatabase>();
            try
            {
                GameObject firstIntermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
                firstIntermediate.transform.SetParent(firstRoot.transform, false);
                GameObject firstBase = new GameObject("Base");
                firstBase.transform.SetParent(firstIntermediate.transform, false);
                Assert.That(firstRegistry.TryRegisterBaseFigure(firstDatabase, "Base", firstBase, out string firstBaseDiagnostic), Is.True, firstBaseDiagnostic);

                GameObject secondIntermediate = new GameObject(ShapeSyncDatabaseAsset.IntermediateContainerName);
                secondIntermediate.transform.SetParent(secondRoot.transform, false);
                GameObject secondBase = new GameObject("Base");
                secondBase.transform.SetParent(secondIntermediate.transform, false);
                Assert.That(secondRegistry.TryRegisterBaseFigure(secondDatabase, "Base", secondBase, out string secondBaseDiagnostic), Is.True, secondBaseDiagnostic);

                Assert.That(firstRegistry.TryAdmitFigureAxes(firstDatabase, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject secondTall = new GameObject("Tall"); secondTall.transform.SetParent(secondIntermediate.transform, false);
                Assert.That(secondRegistry.TryCommitFigureAxes(secondDatabase, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", secondTall) }
                }, out string commitDiagnostic), Is.False);
                Assert.That(commitDiagnostic, Does.Contain("not issued"));
                Assert.That(secondRegistry.FigureAxes, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(firstRegistry);
                Object.DestroyImmediate(secondRegistry);
                Object.DestroyImmediate(firstRoot);
                Object.DestroyImmediate(secondRoot);
            }
        }

        [Test]
        public void MaterialAdapterResolver_AdmitsAllSupportedShadersRejectsUnknownAndDisposesTransientAdapter()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
            var materials = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                ShapeSyncDatabase databaseContents = contents.GetComponent<ShapeSyncDatabase>();
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);

                var supportedShaders = new List<string>
                {
                    "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/Lit"
                };
#if SHAPESYNC_USE_UNIVRM
                supportedShaders.Add(
                    "VRM10/Universal Render Pipeline/MToon10");
#endif
                for (int i = 0; i < supportedShaders.Count; i++)
                {
                    Shader shader = Shader.Find(supportedShaders[i]);
                    Assert.That(shader, Is.Not.Null, supportedShaders[i]);
                    Material material = new Material(shader);
                    materials.Add(material);
                    Texture2D preview = new Texture2D(1, 1) { name = "Preview" + i };
                    textures.Add(preview);
                    material.SetTexture(i == 2 ? "_MainTex" : "_BaseMap", preview);
                    renderer.sharedMaterial = material;
                    Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(databaseContents, "Entry" + i, renderer, 0, material, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    MaterialShaderAdapter transientAdapter = admission.TransientAdapter;
                    Assert.That(transientAdapter.ExpectedShaderName, Is.EqualTo(shader.name));
                    Assert.That(admission.SourceMaterialName, Is.EqualTo(material.name));
                    Assert.That(admission.PreviewTexture, Is.SameAs(preview));
                    admission.Dispose();
                    Assert.That(transientAdapter == null, Is.True, "Step 1 transient adapter must not outlive admission.");
                    Assert.That(databaseContents.Registry.BaseFigures, Has.Count.EqualTo(1), "Admission must not mutate the registry before Step 2.");
                }

                Shader unsupportedShader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(unsupportedShader, Is.Not.Null);
                Material unsupported = new Material(unsupportedShader);
                try
                {
                    renderer.sharedMaterial = unsupported;
                    Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(databaseContents, "Unsupported", renderer, 0, unsupported, out ShapeSyncMaterialAdapterResolver.Admission rejected, out string rejectedDiagnostic), Is.False);
                    Assert.That(rejected, Is.Null);
                    Assert.That(rejectedDiagnostic, Does.Contain("no ShapeSync"));
                }
                finally { Object.DestroyImmediate(unsupported); }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
                foreach (Material material in materials) Object.DestroyImmediate(material);
                foreach (Texture2D texture in textures) Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void MaterialEntryAdmission_UsesSlotOrderedDefaultNamesAndAllowsNoTexturePreview()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(database));
            Material first = null;
            Material second = null;
            try
            {
                ShapeSyncDatabase databaseContents = contents.GetComponent<ShapeSyncDatabase>();
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                first = new Material(shader) { name = "First" };
                second = new Material(shader) { name = "Second" };
                renderer.sharedMaterials = new[] { first, second };
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);

                Assert.That(ShapeSyncMaterialAdapterResolver.CreateDefaultEntryName(0), Is.EqualTo("MaterialEntry-0"));
                Assert.That(ShapeSyncMaterialAdapterResolver.CreateDefaultEntryName(1), Is.EqualTo("MaterialEntry-1"));
                Assert.That(() => ShapeSyncMaterialAdapterResolver.CreateDefaultEntryName(-1), Throws.TypeOf<ArgumentOutOfRangeException>());

                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(databaseContents, ShapeSyncMaterialAdapterResolver.CreateDefaultEntryName(0), renderer, 0, first, out ShapeSyncMaterialAdapterResolver.Admission firstAdmission, out string firstDiagnostic), Is.True, firstDiagnostic);
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(databaseContents, ShapeSyncMaterialAdapterResolver.CreateDefaultEntryName(1), renderer, 1, second, out ShapeSyncMaterialAdapterResolver.Admission secondAdmission, out string secondDiagnostic), Is.True, secondDiagnostic);
                try
                {
                    Assert.That(firstAdmission.SourceMaterialName, Is.EqualTo("First"));
                    Assert.That(secondAdmission.SourceMaterialName, Is.EqualTo("Second"));
                    Assert.That(firstAdmission.PreviewTexture, Is.Null, "A Base Material without a BaseColor Texture is a valid None preview.");
                    Assert.That(secondAdmission.PreviewTexture, Is.Null);
                }
                finally { firstAdmission.Dispose(); secondAdmission.Dispose(); }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void MaterialEntryImport_AtomicallyOwnsMaterialsAdaptersAndTextureCopies()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            Texture2D sourceTexture = new Texture2D(1, 1) { name = "SourceTexture" };
            sourceTexture.SetPixel(0, 0, Color.magenta);
            sourceTexture.Apply();
            AssetDatabase.CreateAsset(sourceTexture, Root + "/SourceTexture.asset");
            Material firstSource = new Material(shader) { name = "SourceFirst" };
            Material secondSource = new Material(shader) { name = "SourceSecond" };
            firstSource.SetTexture("_BaseMap", sourceTexture);
            secondSource.SetTexture("_BaseMap", sourceTexture);
            AssetDatabase.CreateAsset(firstSource, Root + "/SourceFirst.mat");
            AssetDatabase.CreateAsset(secondSource, Root + "/SourceSecond.mat");

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("MasterFigure");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterials = new[] { firstSource, secondSource };
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "MasterFigure", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string createBaseDiagnostic), Is.True, createBaseDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer openedRenderer = opened.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", openedRenderer, 0, firstSource, out ShapeSyncMaterialAdapterResolver.Admission firstAdmission, out string firstDiagnostic), Is.True, firstDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Face", openedRenderer, 1, secondSource, out ShapeSyncMaterialAdapterResolver.Admission secondAdmission, out string secondDiagnostic), Is.True, secondDiagnostic);
            try
            {
                Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(databasePath, new[] { firstAdmission, secondAdmission }, true, out string saveDiagnostic), Is.True, saveDiagnostic);
            }
            finally { firstAdmission.Dispose(); secondAdmission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(saved.Registry.MaterialEntries, Has.Count.EqualTo(2));
            ShapeSyncDatabaseRegistry.MaterialEntry body = saved.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body");
            ShapeSyncDatabaseRegistry.MaterialEntry face = saved.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face");
            Assert.That(body.Material, Is.Not.SameAs(firstSource));
            Assert.That(face.Material, Is.Not.SameAs(secondSource));
            Assert.That(body.Material.name, Is.EqualTo("MasterFigure_Body_Material"), "Initial Entry save must use the Figure Master Name for the owned Material sub-asset.");
            Assert.That(face.Material.name, Is.EqualTo("MasterFigure_Face_Material"), "Initial Entry save must not defer the Figure prefix until a later rename.");
            Assert.That(body.Adapter, Is.SameAs(face.Adapter), "Entries using the same exact adapter must share one Database-owned Adapter sub-asset.");
            Assert.That(AssetDatabase.GetAssetPath(body.Material), Is.EqualTo(databasePath));
            Assert.That(AssetDatabase.GetAssetPath(body.Adapter), Is.EqualTo(databasePath));
            Assert.That(body.Renderer, Is.SameAs(saved.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>()));
            Assert.That(body.BaseRelativeRendererPath, Is.EqualTo(string.Empty));
            Texture bodyTexture = body.Material.GetTexture("_BaseMap");
            Texture faceTexture = face.Material.GetTexture("_BaseMap");
            Assert.That(bodyTexture, Is.Not.SameAs(sourceTexture));
            Assert.That(faceTexture, Is.SameAs(bodyTexture));
            Assert.That(AssetDatabase.GetAssetPath(bodyTexture), Is.EqualTo(databasePath));
            Assert.That(saved.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(new[] { body.Material, face.Material }));

            Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string textureRegistrationDiagnostic), Is.True, textureRegistrationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase textureRegistered, out string textureReopenDiagnostic), Is.True, textureReopenDiagnostic);
            Assert.That(textureRegistered.Registry.TextureResources, Has.Count.EqualTo(1));
            Assert.That(textureRegistered.Registry.TextureResources[0].LogicalName, Is.EqualTo("MasterFigure_Body"));
            Assert.That(textureRegistered.Registry.TextureResources[0].Texture.name, Is.EqualTo("MasterFigure_Body"), "Initial Entry save must use the Figure Master Name for the owned Texture sub-asset.");
            Assert.That(textureRegistered.Registry.TextureResources[0].Texture, Is.SameAs(textureRegistered.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material.GetTexture("_BaseMap")));
            Assert.That(textureRegistered.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").TextureResourceNames, Is.EqualTo(new[] { "MasterFigure_Body" }));
            Assert.That(textureRegistered.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face").TextureResourceNames, Is.EqualTo(new[] { "MasterFigure_Body" }));
            Assert.That(textureRegistered.Registry.MaterialEntries.All(entry => entry.TextureResourceNames.All(name =>
                textureRegistered.Registry.TextureResources.Any(resource => resource != null && resource.LogicalName == name))), Is.True,
                "The final shared-Texture logical name must resolve from every Material Entry.");
            Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string repeatResourceDiagnostic), Is.True, repeatResourceDiagnostic);
            Assert.That(textureRegistered.Registry.TextureResources, Has.Count.EqualTo(1));

            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(saved, "Body", saved.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>(), 1, face.Material, out ShapeSyncMaterialAdapterResolver.Admission duplicateAdmission, out string duplicateAdmissionDiagnostic), Is.True, duplicateAdmissionDiagnostic);
            try
            {
                Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { duplicateAdmission }, out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("already exists"));
            }
            finally { duplicateAdmission.Dispose(); }
            Assert.That(saved.Registry.MaterialEntries, Has.Count.EqualTo(2));
            Assert.That(saved.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(new[] { body.Material, face.Material }));

            Assert.That(AssetDatabase.DeleteAsset(Root + "/SourceFirst.mat"), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(Root + "/SourceSecond.mat"), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(Root + "/SourceTexture.asset"), Is.True);
            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase sourceDeleted, out string sourceDeletedDiagnostic), Is.True, sourceDeletedDiagnostic);
            ShapeSyncDatabaseRegistry.MaterialEntry sourceDeletedBody = sourceDeleted.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body");
            Assert.That(sourceDeletedBody.Material.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(sourceDeletedBody.Material.GetTexture("_BaseMap")), Is.EqualTo(databasePath));
        }

        [Test]
        public void EntryAssetNaming_UnifiesFigureAndFbmNamesAndRebindsEveryTextureAlias()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Texture2D provisional = new Texture2D(1, 1);
            Texture2D finalTexture = new Texture2D(1, 1);
            Texture2D unrelated = new Texture2D(1, 1);
            Material material = new Material(shader);
            try
            {
                material.SetTexture("_BaseMap", provisional);
                material.SetTexture("_EmissionMap", provisional);
                material.SetTexture("_BumpMap", unrelated);
                Assert.That(ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(material).ToArray(), Is.EqualTo(new Texture[] { provisional, unrelated }), "MainTex must lead and aliases must not allocate a second Texture Entry.");
                string[] orderedProperties = ShapeSyncEntryAssetNaming.GetTexturePropertyNamesMainTexFirst(material).ToArray();
                Assert.That(orderedProperties.Length, Is.GreaterThanOrEqualTo(3),
                    "Outfit Texture registration must retain all shader texture properties.");
                Assert.That(orderedProperties[0], Is.EqualTo("_BaseMap"),
                    "Outfit Texture registration must enumerate the MainTex property first.");
                Assert.That(orderedProperties, Does.Contain("_BumpMap"));
                Assert.That(orderedProperties, Does.Contain("_EmissionMap"));
                Assert.That(ShapeSyncEntryAssetNaming.GetTextureName("MasterFigure", "Body"), Is.EqualTo("MasterFigure_Body"));
                Assert.That(ShapeSyncEntryAssetNaming.GetTextureName("MasterFigure", "Body", 0), Is.EqualTo("MasterFigure_Body"));
                Assert.That(ShapeSyncEntryAssetNaming.GetTextureName("MasterFigure", "Body", 1), Is.EqualTo("MasterFigure_Body_2"));
                Assert.That(ShapeSyncEntryAssetNaming.GetTextureName("Tall", "Body"), Is.EqualTo("Tall_Body"));
                Assert.That(ShapeSyncEntryAssetNaming.GetMaterialName("Tall", "Body"), Is.EqualTo("Tall_Body_Material"));
                ShapeSyncEntryAssetNaming.ApplyMaterialName(material, "MasterFigure", "Body");
                Assert.That(material.name, Is.EqualTo("MasterFigure_Body_Material"));
                ShapeSyncEntryAssetNaming.ReplaceTextureAliases(material, provisional, finalTexture);
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(finalTexture));
                Assert.That(material.GetTexture("_EmissionMap"), Is.SameAs(finalTexture));
                Assert.That(material.GetTexture("_BumpMap"), Is.SameAs(unrelated), "Only aliases of the provisional Texture may be rebound.");
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(provisional); Object.DestroyImmediate(finalTexture); Object.DestroyImmediate(unrelated); }
        }

        [Test]
        public void EntryAssetNaming_OrdersNonMainTexturesStablyWhenMainTexIsAbsent()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Texture2D normal = new Texture2D(1, 1);
            Texture2D emission = new Texture2D(1, 1);
            Material material = new Material(shader);
            try
            {
                material.SetTexture("_BumpMap", normal);
                material.SetTexture("_EmissionMap", emission);
                Assert.That(ShapeSyncEntryAssetNaming.GetMainTexture(material), Is.Null);
                Assert.That(ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(material).ToArray(), Is.EqualTo(new Texture[] { normal, emission }), "A MainTex-less Material must retain a deterministic remaining-property order.");
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(normal); Object.DestroyImmediate(emission); }
        }

        [Test]
        public void EntryAssetNaming_RejectsIncompleteInputsBeforeChangingAssets()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Texture2D provisional = new Texture2D(1, 1);
            Texture2D finalTexture = new Texture2D(1, 1);
            Material material = new Material(shader) { name = "Unchanged" };
            try
            {
                Assert.That(() => ShapeSyncEntryAssetNaming.GetTextureName(null, "Body"), Throws.ArgumentException);
                Assert.That(() => ShapeSyncEntryAssetNaming.GetTextureName("MasterFigure", "  "), Throws.ArgumentException);
                Assert.That(() => ShapeSyncEntryAssetNaming.ApplyMaterialName(null, "MasterFigure", "Body"), Throws.ArgumentNullException);
                Assert.That(() => ShapeSyncEntryAssetNaming.ApplyMaterialName(material, "", "Body"), Throws.ArgumentException);
                Assert.That(material.name, Is.EqualTo("Unchanged"), "Invalid naming input must not partially rename the Material.");
                Assert.That(() => ShapeSyncEntryAssetNaming.ReplaceTextureAliases(null, provisional, finalTexture), Throws.ArgumentNullException);
                Assert.That(() => ShapeSyncEntryAssetNaming.ReplaceTextureAliases(material, null, finalTexture), Throws.ArgumentNullException);
                Assert.That(() => ShapeSyncEntryAssetNaming.ReplaceTextureAliases(material, provisional, null), Throws.ArgumentNullException);
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(provisional); Object.DestroyImmediate(finalTexture); }
        }

        [Test]
        public void MaterialTextureRename_PrioritizesMainTexBeforeNormalForFigureAndResourceNames()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Texture2D mainTexture = new Texture2D(1, 1) { name = "SourceMain" };
            Texture2D normalTexture = new Texture2D(1, 1) { name = "SourceNormal" };
            Material source = new Material(shader) { name = "Source" };
            source.SetTexture("_BaseMap", mainTexture);
            source.SetTexture("_BumpMap", normalTexture);
            AssetDatabase.CreateAsset(mainTexture, Root + "/MainFirstMain.asset");
            AssetDatabase.CreateAsset(normalTexture, Root + "/MainFirstNormal.asset");
            AssetDatabase.CreateAsset(source, Root + "/MainFirstSource.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("MasterFigure");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = source;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "MasterFigure", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer openedRenderer = opened.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", openedRenderer, 0, source, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { admission }, out string saveDiagnostic), Is.True, saveDiagnostic); }
            finally { admission.Dispose(); }
            Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, new[] { new ShapeSyncMaterialEntryImport.Rename("Body", "Body") }, true, out string renameDiagnostic), Is.True, renameDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamed, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.MaterialEntry entry = renamed.Registry.MaterialEntries.Single();
            Assert.That(entry.TextureResourceNames, Is.EqualTo(new[] { "MasterFigure_Body", "MasterFigure_Body_2" }), "Texture Entry logical names must place MainTex before Normal.");
            Texture persistedMain = entry.Material.mainTexture;
            Texture persistedNormal = entry.Material.GetTexture("_BumpMap");
            Assert.That(renamed.Registry.TextureResources.Single(resource => resource.Texture == persistedMain).LogicalName, Is.EqualTo("MasterFigure_Body"));
            Assert.That(renamed.Registry.TextureResources.Single(resource => resource.Texture == persistedNormal).LogicalName, Is.EqualTo("MasterFigure_Body_2"));
            Assert.That(persistedMain.name, Is.EqualTo("MasterFigure_Body"));
            Assert.That(persistedNormal.name, Is.EqualTo("MasterFigure_Body_2"));
            Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, new[] { new ShapeSyncMaterialEntryImport.Rename("Body", "Body") }, true, out string repeatRenameDiagnostic), Is.True, repeatRenameDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamedAgain, out string repeatOpenDiagnostic), Is.True, repeatOpenDiagnostic);
            ShapeSyncDatabaseRegistry.MaterialEntry repeatedEntry = renamedAgain.Registry.MaterialEntries.Single();
            Assert.That(repeatedEntry.TextureResourceNames, Is.EqualTo(new[] { "MasterFigure_Body", "MasterFigure_Body_2" }), "Repeating an identity rename must preserve the MainTex-first logical-name order.");
            Assert.That(repeatedEntry.Material.mainTexture.name, Is.EqualTo("MasterFigure_Body"));
            Assert.That(repeatedEntry.Material.GetTexture("_BumpMap").name, Is.EqualTo("MasterFigure_Body_2"));
        }

        [Test]
        public void FbmImportAll_SharesOneOwnedSubTextureAcrossMaterialEntries()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            Texture2D bodyMain = new Texture2D(1, 1) { name = "BodyMain" };
            Texture2D faceMain = new Texture2D(1, 1) { name = "FaceMain" };
            Texture2D faceEmission = new Texture2D(1, 1) { name = "FaceEmission" };
            Texture2D sharedNormal = new Texture2D(1, 1) { name = "SharedNormal" };
            Material bodySource = new Material(shader) { name = "BodySource" };
            Material faceSource = new Material(shader) { name = "FaceSource" };
            bodySource.SetTexture("_BaseMap", bodyMain); bodySource.SetTexture("_BumpMap", sharedNormal);
            faceSource.SetTexture("_BaseMap", faceMain); faceSource.SetTexture("_BumpMap", sharedNormal); faceSource.SetTexture("_EmissionMap", faceEmission);
            AssetDatabase.CreateAsset(bodyMain, Root + "/FbmSharedBodyMain.asset");
            AssetDatabase.CreateAsset(faceMain, Root + "/FbmSharedFaceMain.asset");
            AssetDatabase.CreateAsset(faceEmission, Root + "/FbmSharedFaceEmission.asset");
            AssetDatabase.CreateAsset(sharedNormal, Root + "/FbmSharedNormal.asset");
            AssetDatabase.CreateAsset(bodySource, Root + "/FbmSharedBody.mat");
            AssetDatabase.CreateAsset(faceSource, Root + "/FbmSharedFace.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("MasterFigure");
                baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { bodySource, faceSource };
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "MasterFigure", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer baseRenderer = opened.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseRenderer, 0, bodySource, out ShapeSyncMaterialAdapterResolver.Admission bodyAdmission, out string bodyAdmissionDiagnostic), Is.True, bodyAdmissionDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Face", baseRenderer, 1, faceSource, out ShapeSyncMaterialAdapterResolver.Admission faceAdmission, out string faceAdmissionDiagnostic), Is.True, faceAdmissionDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { bodyAdmission, faceAdmission }, out string materialSaveDiagnostic), Is.True, materialSaveDiagnostic); }
            finally { bodyAdmission.Dispose(); faceAdmission.Dispose(); }
            Assert.That(ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreate("Tall", new[] { bodySource, faceSource }, out ShapeSyncFigureImport.DatabaseMaterialCopies copies, out string copiesDiagnostic), Is.True, copiesDiagnostic);
            try
            {
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
                {
                    GameObject fbmFigure = new GameObject("Tall");
                    fbmFigure.transform.SetParent(intermediate, false);
                    fbmFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterials = copies.Materials;
                    copies.AddTo(context);
                    Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, true) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out string axisAdmissionDiagnostic), Is.True, axisAdmissionDiagnostic);
                    Assert.That(contents.Registry.TryCommitFigureAxes(contents, axes, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", fbmFigure) } }, out string axisCommitDiagnostic), Is.True, axisCommitDiagnostic);
                    ShapeSyncFigureAxisImport.RegisterFbmTextureEntries(contents, "Tall", true, copies, context);
                    ShapeSyncDatabaseRegistry.MaterialEntry body = contents.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body");
                    ShapeSyncDatabaseRegistry.MaterialEntry face = contents.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face");
                    Assert.That(contents.Registry.TextureResources.Where(entry => entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure && entry.Owner.SourceShapeKey == "Tall").Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Tall_Body", "Tall_Body_2", "Tall_Face", "Tall_Face_2" }));
                    Assert.That(body.TextureResourceNames, Does.Contain("Tall_Body_2"));
                    Assert.That(face.TextureResourceNames, Does.Contain("Tall_Body_2"));
                    Assert.That(face.TextureResourceNames, Does.Contain("Tall_Face_2"), "A shared Texture must not consume the Face-local suffix that the following unique Texture owns.");
                    Assert.That(fbmFigure.GetComponent<SkinnedMeshRenderer>().sharedMaterials[0].GetTexture("_BumpMap"), Is.SameAs(fbmFigure.GetComponent<SkinnedMeshRenderer>().sharedMaterials[1].GetTexture("_BumpMap")));
                    Assert.That(fbmFigure.GetComponent<SkinnedMeshRenderer>().sharedMaterials[1].GetTexture("_EmissionMap"), Is.SameAs(contents.Registry.TextureResources.Single(entry => entry.LogicalName == "Tall_Face_2").Texture));
                }, out string fbmSaveDiagnostic), Is.True, fbmSaveDiagnostic);
                copies.Detach();
            }
            finally { copies.Dispose(); }
        }

        [Test]
        public void OutfitMaterialClassification_RequiresIrreversibleConfirmationOnlyForExcludeOrProjection()
        {
            Assert.That(ShapeSyncDatabaseWindow.RequiresIrreversibleClassificationConfirmation(new[]
            {
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include
            }), Is.False);
            Assert.That(ShapeSyncDatabaseWindow.RequiresIrreversibleClassificationConfirmation(new[]
            {
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include,
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude
            }), Is.True);
            Assert.That(ShapeSyncDatabaseWindow.RequiresIrreversibleClassificationConfirmation(new[]
            {
                ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection
            }), Is.True);
        }

        [Test]
        public void FbmImportWithoutAll_BindsEveryMaterialSlotToItsFigureMaterial()
        {
            const string sourcePath = Root + "/FbmFalseTwoSlots.prefab";
            GameObject source = CreateHumanoidSourceForFigureDetail("FbmFalseTwoSlots", out Avatar avatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                Mesh mesh = renderer.sharedMesh;
                int[] triangles = mesh.triangles;
                mesh.subMeshCount = 2;
                mesh.SetTriangles(triangles, 0);
                mesh.SetTriangles(triangles, 1);
                Texture2D sourceNormal = new Texture2D(1, 1) { name = "FalseFbmNormal" };
                Material body = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FalseBody" };
                Material face = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FalseFace" };
                body.SetTexture("_BumpMap", sourceNormal);
                face.SetTexture("_BumpMap", sourceNormal);
                renderer.sharedMaterials = new[] { body, face };
                AssetDatabase.CreateAsset(avatar, Root + "/FbmFalseTwoSlotsAvatar.asset");
                AssetDatabase.CreateAsset(mesh, Root + "/FbmFalseTwoSlotsMesh.asset");
                AssetDatabase.CreateAsset(sourceNormal, Root + "/FbmFalseTwoSlotsNormal.asset");
                AssetDatabase.CreateAsset(body, Root + "/FbmFalseTwoSlotsBody.mat");
                AssetDatabase.CreateAsset(face, Root + "/FbmFalseTwoSlotsFace.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                GameObject persistent = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistent, out ShapeSyncFigureImportAdmission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                string databasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "Master", out string baseImportDiagnostic), Is.True, baseImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                SkinnedMeshRenderer baseRenderer = opened.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseRenderer, 0, baseRenderer.sharedMaterials[0], out ShapeSyncMaterialAdapterResolver.Admission bodyAdmission, out string bodyAdmissionDiagnostic), Is.True, bodyAdmissionDiagnostic);
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Face", baseRenderer, 1, baseRenderer.sharedMaterials[1], out ShapeSyncMaterialAdapterResolver.Admission faceAdmission, out string faceAdmissionDiagnostic), Is.True, faceAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { bodyAdmission, faceAdmission }, out string materialDiagnostic), Is.True, materialDiagnostic); }
                finally { bodyAdmission.Dispose(); faceAdmission.Dispose(); }
                Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath, new[] { "Body" }, new[]
                {
                    new ShapeSyncNormalEntryAuthoring.Assignment("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, sourceNormal)
                }, out string baseNormalDiagnostic), Is.True, baseNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(opened.Registry.TryAdmitFigureAxes(opened, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, false) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out string axesDiagnostic), Is.True, axesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(databasePath, new[] { new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }) }, out string importDiagnostic), Is.True, importDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string savedDiagnostic), Is.True, savedDiagnostic);
                Material[] imported = saved.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
                Assert.That(imported[0], Is.SameAs(saved.Registry.MaterialEntries.Single(entry => entry.MaterialSlot == 0).Material));
                Assert.That(imported[1], Is.SameAs(saved.Registry.MaterialEntries.Single(entry => entry.MaterialSlot == 1).Material));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Any(material => material.name.StartsWith("Tall_", StringComparison.Ordinal)), Is.False);
                ShapeSyncDatabaseRegistry.NormalEntry fbmNormal = saved.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "Body" && entry.ShapeKey == "Tall");
                Assert.That(fbmNormal.Texture, Is.Not.SameAs(sourceNormal));
                Assert.That(fbmNormal.TextureResourceName, Is.EqualTo("Tall_Body_Normal"));
                Assert.That(AssetDatabase.GetAssetPath(fbmNormal.Texture), Is.EqualTo(databasePath));
                Assert.That(saved.Registry.TextureResources.Single(entry => entry.Texture == fbmNormal.Texture).Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure));
                Assert.That(saved.Registry.TextureResources.Single(entry => entry.Texture == fbmNormal.Texture).Owner.SourceShapeKey, Is.EqualTo("Tall"));

                // The same false Import All contract must survive an existing-FBM redefinition,
                // not just its initial registration.  Re-open the Normal Detail afterwards so
                // the authoring UI is proven to expose the persisted Figure Normal relation.
                Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(databasePath, "Tall", "Tall", false, admission, out string replacementDiagnostic), Is.True, replacementDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase replacementSaved, out string replacementOpenDiagnostic), Is.True, replacementOpenDiagnostic);
                Material[] replacementMaterials = replacementSaved.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
                Assert.That(replacementMaterials[0], Is.SameAs(replacementSaved.Registry.MaterialEntries.Single(entry => entry.MaterialSlot == 0).Material));
                Assert.That(replacementMaterials[1], Is.SameAs(replacementSaved.Registry.MaterialEntries.Single(entry => entry.MaterialSlot == 1).Material));
                ShapeSyncDatabaseRegistry.NormalEntry replacementNormal = replacementSaved.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "Body" && entry.ShapeKey == "Tall");
                Assert.That(replacementNormal.Texture, Is.Not.SameAs(sourceNormal));
                Assert.That(AssetDatabase.GetAssetPath(replacementNormal.Texture), Is.EqualTo(databasePath));
                ShapeSyncDatabaseRegistry.TextureResourceOwner replacementOwner = replacementSaved.Registry.TextureResources.Single(entry => entry.Texture == replacementNormal.Texture).Owner;
                Assert.That(replacementOwner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure));
                Assert.That(replacementOwner.SourceShapeKey, Is.EqualTo("Tall"));
                ShapeSyncDatabaseWindow reopenedWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(reopenedWindow.TrySetDatabase(replacementSaved, out string windowBindDiagnostic), Is.True, windowBindDiagnostic);
                    reopenedWindow.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                    Assert.That(reopenedWindow.FigureNormalEntryMaterialNamesForTest, Does.Contain("Body"));
                    Assert.That(reopenedWindow.GetNormalDraftTextureForTest("Body", "Tall"), Is.SameAs(replacementNormal.Texture));

                    // FBM Detail has one Save boundary.  A pending Normal for an existing
                    // FBM must survive the Database rebind performed by a simultaneous
                    // new-FBM import.
                    Texture2D pendingTallNormal = new Texture2D(1, 1) { name = "PendingTallNormal" };
                    AssetDatabase.CreateAsset(pendingTallNormal, Root + "/PendingTallNormal.asset");
                    reopenedWindow.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                    Assert.That(reopenedWindow.TrySetNormalDraftForTest("Body", "Tall", pendingTallNormal), Is.True);
                    reopenedWindow.SetFbmAxisDraftsForTest(new[] { "Short" }, new[] { persistent }, new[] { false });
                    Assert.That(reopenedWindow.TrySaveFbmAxisDraftsForTest(out string combinedSaveDiagnostic), Is.True, combinedSaveDiagnostic);
                    Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase combinedSaved, out string combinedOpenDiagnostic), Is.True, combinedOpenDiagnostic);
                    ShapeSyncDatabaseRegistry.NormalEntry combinedTallNormal = combinedSaved.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "Body" && entry.ShapeKey == "Tall");
                    Assert.That(combinedTallNormal.Texture, Is.Not.SameAs(pendingTallNormal));
                    Assert.That(combinedTallNormal.Texture, Is.Not.SameAs(replacementNormal.Texture));
                    Assert.That(AssetDatabase.GetAssetPath(combinedTallNormal.Texture), Is.EqualTo(databasePath));
                    Assert.That(combinedSaved.Registry.FigureAxes.Any(axis => axis.Name == "Short" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm), Is.True);
                }
                finally { Object.DestroyImmediate(reopenedWindow); }
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TexturesDetail_RenamesDatabaseResourceAndAddsOwnedTextureThroughOneSave()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D existing = new Texture2D(1, 1) { name = "Existing" };
            Texture2D source = new Texture2D(1, 1) { name = "ExternalSource" };
            AssetDatabase.CreateAsset(source, Root + "/TextureDraftSource.asset");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(existing);
                Assert.That(contents.Registry.TryRegisterTextureResource("Texture-0", existing, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TextureDraftNamesForTest, Is.EqualTo(new[] { "Texture-0" }));
                Assert.That(window.IsTexturesSaveEnabledForTest, Is.False);
                Assert.That(window.TrySetTextureDraftNameForTest(0, "Base_Texture"), Is.True);
                Assert.That(window.TryAddTextureDraftForTest("Extra_Texture", source), Is.True);
                Assert.That(window.IsTexturesDetailDirtyForTest, Is.True);
                Assert.That(window.IsTexturesSaveEnabledForTest, Is.True);
                Assert.That(window.TrySaveTextureDraftsForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsTexturesDetailDirtyForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(AssetDatabase.DeleteAsset(Root + "/TextureDraftSource.asset"), Is.True);
            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Base_Texture", "Extra_Texture" }));
            Texture added = reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "Extra_Texture").Texture;
            Assert.That(added, Is.Not.SameAs(source));
            Assert.That(AssetDatabase.GetAssetPath(added), Is.EqualTo(databasePath));
        }

        [Test]
        public void TexturesDetail_RemoveDraftDeletesAnUnreferencedOwnedTextureOnSave()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D existing = new Texture2D(1, 1) { name = "Disposable" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(existing);
                Assert.That(contents.Registry.TryRegisterTextureResource("Disposable", existing, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TryRemoveTextureDraftForTest(0), Is.True);
                Assert.That(window.IsTexturesDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveTextureDraftsForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.TextureResources, Is.Empty);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => texture.name == "Disposable"), Is.False);
        }

        [Test]
        public void TexturesDetail_RejectedReferencedRemovalRestoresThePersistedDraft()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D referenced = new Texture2D(1, 1) { name = "Referenced" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                context.AddSubAsset(referenced);
                Assert.That(contents.Registry.TryRegisterTextureResource("Referenced", referenced, out string resourceDiagnostic), Is.True, resourceDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            ShapeSyncDatabaseWindow.TextureResourceSaver originalSaver = ShapeSyncDatabaseWindow.SaveTextureResources;
            bool saverCalled = false;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TryRemoveTextureDraftForTest(0), Is.True);
                ShapeSyncDatabaseWindow.SaveTextureResources = (string _, IReadOnlyList<ShapeSyncTextureResourceAuthoring.Rename> __,
                    IReadOnlyList<ShapeSyncTextureResourceAuthoring.Addition> ___, IReadOnlyList<ShapeSyncTextureResourceAuthoring.Removal> ____, out string diagnostic) =>
                {
                    saverCalled = true;
                    diagnostic = "Texture resource is still referenced and cannot be removed: Referenced";
                    return false;
                };

                bool saved = window.TrySaveTextureDraftsForTest(out string saveDiagnostic);
                Assert.That(saverCalled, Is.True, "The rejection seam must be invoked.");
                Assert.That(saved, Is.False, saveDiagnostic);
                Assert.That(saveDiagnostic, Does.Contain("still referenced"));
                Assert.That(window.TextureDraftNamesForTest, Is.EqualTo(new[] { "Referenced" }));
                Assert.That(window.IsTexturesDetailDirtyForTest, Is.False);
            }
            finally { ShapeSyncDatabaseWindow.SaveTextureResources = originalSaver; Object.DestroyImmediate(window); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Referenced" }));
        }

        [Test]
        public void TexturesDetail_RejectsDuplicateDraftNamesWithoutChangingDatabaseOrSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Texture2D source = new Texture2D(1, 1) { name = "Source" };
            AssetDatabase.CreateAsset(source, Root + "/DuplicateTextureDraftSource.asset");
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TryAddTextureDraftForTest("Duplicate", source), Is.True);
                Assert.That(window.TryAddTextureDraftForTest("Duplicate", source), Is.False);
                Assert.That(window.Diagnostic, Does.Contain("unique"));
                Assert.That(window.Database.Registry.TextureResources, Is.Empty);
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally { Object.DestroyImmediate(window); Selection.activeObject = originalSelection; }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TexturesDetail_DirtyNavigationSavesIgnoresOrCancelsWithSelectionRollback(int choice)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D existing = new Texture2D(1, 1) { name = "Existing" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(existing);
                Assert.That(contents.Registry.TryRegisterTextureResource("Texture-0", existing, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TrySetTextureDraftNameForTest(0, "Changed"), Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => choice;
                bool navigated = window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General);
                Assert.That(navigated, Is.EqualTo(choice != 2));
                Assert.That(window.SelectedSection, Is.EqualTo(choice == 2 ? ShapeSyncDatabaseWindow.Section.Textures : ShapeSyncDatabaseWindow.Section.General));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(reopened.Registry.TextureResources[0].LogicalName, Is.EqualTo(choice == 0 ? "Changed" : "Texture-0"));
                Assert.That(window.TextureDraftNamesForTest, Is.EqualTo(new[] { choice == 1 ? "Texture-0" : "Changed" }));
            }
            finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog; Object.DestroyImmediate(window); Selection.activeObject = originalSelection; }
        }

        [Test]
        public void TexturesDetail_RejectsNullAndNonpersistentSourcesWithoutPersistingDrafts()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Texture2D transient = new Texture2D(1, 1) { name = "Transient" };
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TryAddTextureDraftForTest("", transient), Is.False);
                Assert.That(window.TryAddTextureDraftForTest("Null", null), Is.False);
                Assert.That(window.TryAddTextureDraftForTest("Transient", transient), Is.True);
                Assert.That(window.TrySaveTextureDraftsForTest(out string saveDiagnostic), Is.False);
                Assert.That(saveDiagnostic, Does.Contain("persistent"));
                Assert.That(window.Database.Registry.TextureResources, Is.Empty);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(transient); }
        }

        [Test]
        public void TextureResourceAuthoring_SaveFailureRollsBackRenameAndAddedClone()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D existing = new Texture2D(1, 1) { name = "Existing" };
            Texture2D source = new Texture2D(1, 1) { name = "Source" };
            AssetDatabase.CreateAsset(source, Root + "/RollbackTextureSource.asset");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(existing);
                Assert.That(contents.Registry.TryRegisterTextureResource("Texture-0", existing, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncTextureResourceAuthoring.TrySave(databasePath,
                    new[] { new ShapeSyncTextureResourceAuthoring.Rename("Texture-0", "Renamed") },
                    new[] { new ShapeSyncTextureResourceAuthoring.Addition("Added", source) }, out string saveDiagnostic), Is.False);
                Assert.That(saveDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0" }));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Select(texture => texture.name), Does.Not.Contain("Added"));
        }

        [Test]
        public void TexturesDetail_DirtySaveFailureRetainsDraftTreeSelectionDatabaseAndUnitySelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D existing = new Texture2D(1, 1) { name = "Existing" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(existing);
                Assert.That(contents.Registry.TryRegisterTextureResource("Texture-0", existing, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            ShapeSyncDatabaseWindow.TextureResourceSaver originalSaver = ShapeSyncDatabaseWindow.SaveTextureResources;
            Func<GameObject, string, bool> originalDirectSave = ShapeSyncDatabaseDirectEdit.SavePrefabAsset;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Object originalSelection = Selection.activeObject;
            GameObject sentinel = new GameObject("TextureSaveFailureSelection");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                Assert.That(window.TrySetTextureDraftNameForTest(0, "Changed"), Is.True);
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = (_, _) => false;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Selection.activeObject = sentinel;
                ShapeSyncDatabaseWindow.NavigationTreeView treeView = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(), window.TryNavigateTo, () => window.SelectedSection);
                treeView.ApplySelectionChangeForTest(new List<int> { 1 });

                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Textures));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 5 }));
                Assert.That(window.TextureDraftNamesForTest, Is.EqualTo(new[] { "Changed" }));
                Assert.That(window.Diagnostic, Does.Contain("direct edit could not be saved"));
                Assert.That(Selection.activeObject, Is.SameAs(sentinel));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(reopened.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0" }));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveTextureResources = originalSaver;
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = originalDirectSave;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
                Object.DestroyImmediate(sentinel);
            }
        }

        [TestCase(true, "Base_Body")]
        [TestCase(false, "Texture-0")]
        public void MaterialsDetail_DraftsSupportedBaseSlotsAndSavesThemBeforeNavigation(bool renameTextures, string expectedTextureName)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BaseMaterial" };
            Texture2D sourceTexture = new Texture2D(1, 1) { name = "BaseTexture" };
            Texture2D sourceNormal = new Texture2D(1, 1) { name = "SourceNormal" };
            AssetDatabase.CreateAsset(sourceTexture, Root + "/BaseTexture.asset");
            AssetDatabase.CreateAsset(sourceNormal, Root + "/BaseNormal.asset");
            source.SetTexture("_BaseMap", sourceTexture);
            AssetDatabase.CreateAsset(source, Root + "/BaseMaterial.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterial = source;
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);

            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Func<string, string, string, string, bool> originalRenameConfirm = ShapeSyncDatabaseWindow.ConfirmTextureRename;
            Func<bool> originalIsBatchMode = ShapeSyncDatabaseWindow.IsBatchMode;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                bool renameDialogCalled = false;
                ShapeSyncDatabaseWindow.IsBatchMode = () => false;
                ShapeSyncDatabaseWindow.ConfirmTextureRename = (title, message, yes, no) =>
                {
                    renameDialogCalled = true;
                    Assert.That(title, Is.EqualTo("Rename Textures"));
                    Assert.That(message, Is.EqualTo("Rename Textures to [FigureName]_[EntryName]?"));
                    Assert.That(yes, Is.EqualTo("Yes")); Assert.That(no, Is.EqualTo("No"));
                    return renameTextures;
                };
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "MaterialEntry-0" }));
                Assert.That(window.IsMaterialsSaveEnabledForTest, Is.False);
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "Body"), Is.True);
                Assert.That(window.IsMaterialsDetailDirtyForTest, Is.True);
                Assert.That(window.IsMaterialsSaveEnabledForTest, Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "MaterialEntry-0" }));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase ignored, out string ignoredDiagnostic), Is.True, ignoredDiagnostic);
                Assert.That(ignored.Registry.MaterialEntries, Is.Empty);

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "Body"), Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Materials));
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "Body" }));
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True, window.Diagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(saved.Registry.MaterialEntries.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Body" }));
                Assert.That(renameDialogCalled, Is.True);
                Assert.That(saved.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { expectedTextureName }));
                Texture preview = window.ResolveMaterialEntryPreviewForTest("Body");
                Assert.That(preview, Is.Not.Null);
                Assert.That(preview, Is.Not.SameAs(sourceTexture));
                Assert.That(AssetDatabase.GetAssetPath(preview), Is.EqualTo(databasePath));
                Assert.That(window.IsMaterialsDetailDirtyForTest, Is.False);
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                ShapeSyncDatabaseWindow.ConfirmTextureRename = originalRenameConfirm;
                ShapeSyncDatabaseWindow.IsBatchMode = originalIsBatchMode;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RenameDirect_PropagatesShapePartReferencesAndPreservesOutfitNamespace()
        {
            const string databasePath = Root + "/RenameShapeParts.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BodyMaterial" };
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                Texture2D texture = new Texture2D(1, 1) { name = "SharedTexture" };
                transaction.AddSubAsset(material);
                transaction.AddSubAsset(adapter);
                transaction.AddSubAsset(texture);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryRegisterMaterialEntry(contents, "Body", renderer, 0, material.name, material, adapter, out string materialDiagnostic), Is.True, materialDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("SharedTex", texture, out string resourceDiagnostic), Is.True, resourceDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                contents.Registry.Outfits.Single().SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("Body", null, null) });

                Assert.That(contents.Registry.TryAddShape("figure-shape", "Figure Shape", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string figureShapeDiagnostic), Is.True, figureShapeDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("figure-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string figureColorDiagnostic), Is.True, figureColorDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("figure-shape", 0, string.Empty, "Body", out string figureColorTargetDiagnostic), Is.True, figureColorTargetDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("figure-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string figureTexturePartDiagnostic), Is.True, figureTexturePartDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("figure-shape", 1, string.Empty, "Body", out string figureTextureTargetDiagnostic), Is.True, figureTextureTargetDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartTexture("figure-shape", 1, "SharedTex", true, Color.white, out string figureTextureDiagnostic), Is.True, figureTextureDiagnostic);

                Assert.That(contents.Registry.TryAddShape("outfit-shape", "Outfit Shape", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, 0, Array.Empty<string>(), out string outfitShapeDiagnostic), Is.True, outfitShapeDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("outfit-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string outfitColorDiagnostic), Is.True, outfitColorDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("outfit-shape", 0, "mesh-outfit", "Body", out string outfitColorTargetDiagnostic), Is.True, outfitColorTargetDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("outfit-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string outfitTexturePartDiagnostic), Is.True, outfitTexturePartDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("outfit-shape", 1, "mesh-outfit", "Body", out string outfitTextureTargetDiagnostic), Is.True, outfitTextureTargetDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartTexture("outfit-shape", 1, "SharedTex", false, Color.white, out string outfitTextureDiagnostic), Is.True, outfitTextureDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(ShapeSyncMaterialEntryImport.TryRenameDirect(opened,
                new[] { new ShapeSyncMaterialEntryImport.Rename("Body", "BodyRenamed") }, false, out string materialRenameDiagnostic), Is.True, materialRenameDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase afterMaterialRename, out string afterMaterialOpenDiagnostic), Is.True, afterMaterialOpenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry figureShape = afterMaterialRename.Registry.Shapes.Single(shape => shape.ShapeId == "figure-shape");
            ShapeSyncDatabaseRegistry.ShapeEntry outfitShape = afterMaterialRename.Registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape");
            Assert.That(figureShape.Parts[0].ProxyEntry, Is.EqualTo("BodyRenamed"));
            Assert.That(outfitShape.Parts[0].ProxyEntry, Is.EqualTo("Body"));
            Assert.That(afterMaterialRename.Registry.TryValidateShapePartsForGeneration(figureShape.Parts, out string figureValidationDiagnostic), Is.True, figureValidationDiagnostic);
            Assert.That(afterMaterialRename.Registry.TryValidateShapePartsForGeneration(outfitShape.Parts, out string outfitValidationDiagnostic), Is.True, outfitValidationDiagnostic);

            Assert.That(ShapeSyncTextureResourceAuthoring.TryRenameDirect(afterMaterialRename,
                new[] { new ShapeSyncTextureResourceAuthoring.Rename("SharedTex", "RenamedTex") }, out string textureRenameDiagnostic), Is.True, textureRenameDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase finalDatabase, out string finalOpenDiagnostic), Is.True, finalOpenDiagnostic);
            Assert.That(finalDatabase.Registry.Shapes.Single(shape => shape.ShapeId == "figure-shape").Parts[1].TextureResourceName, Is.EqualTo("RenamedTex"));
            Assert.That(finalDatabase.Registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape").Parts[1].TextureResourceName, Is.EqualTo("RenamedTex"));
        }

        [Test]
        public void MaterialsDetail_PreviewUsesAdapterBaseColorAfterTextureResourceRename()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D baseColor = new Texture2D(1, 1) { name = "BaseColor" };
            Texture2D unrelated = new Texture2D(1, 1) { name = "Unrelated" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Body_Material" };
                material.SetTexture("_BaseMap", baseColor);
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                context.AddSubAsset(baseColor);
                context.AddSubAsset(unrelated);
                context.AddSubAsset(material);
                context.AddSubAsset(adapter);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("Unrelated", unrelated, out string unrelatedDiagnostic), Is.True, unrelatedDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("Base", baseColor, out string baseResourceDiagnostic), Is.True, baseResourceDiagnostic);
                Assert.That(contents.Registry.TryRegisterMaterialEntry(contents, "Body", renderer, 0, material.name, material, adapter, out string entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TrySetMaterialEntryTextureResources("Body", new[] { "Unrelated", "Base" }, out string assignmentDiagnostic), Is.True, assignmentDiagnostic);
                Assert.That(contents.Registry.TryRenameTextureResource("Base", "Figure_Body", out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Texture expectedBaseColor = window.Database.Registry.TextureResources.Single(resource => resource.LogicalName == "Figure_Body").Texture;
                Assert.That(window.ResolveMaterialEntryPreviewForTest("Body"), Is.SameAs(expectedBaseColor));
                Assert.That(window.ResolveMaterialEntryPreviewForTest("Body"), Is.Not.SameAs(window.Database.Registry.TextureResources.Single(resource => resource.LogicalName == "Unrelated").Texture));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MaterialsDetail_PreviewRemainsBaseColorAfterEntryRenameSaveAndRebind()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D baseColor = new Texture2D(1, 1) { name = "SourceBaseColor" };
            Texture2D normal = new Texture2D(1, 1) { name = "SourceNormal" };
            Material source = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SourceMaterial" };
            source.SetTexture("_BaseMap", baseColor);
            source.SetTexture("_BumpMap", normal);
            AssetDatabase.CreateAsset(baseColor, Root + "/SourceBaseColor.asset");
            AssetDatabase.CreateAsset(normal, Root + "/SourceNormal.asset");
            AssetDatabase.CreateAsset(source, Root + "/SourceMaterial.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = source;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string baseSetupDiagnostic), Is.True, baseSetupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer importedRenderer = opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", importedRenderer, 0, importedRenderer.sharedMaterial, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(databasePath, new[] { admission }, true, out string saveDiagnostic), Is.True, saveDiagnostic); }
            finally { admission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rebound, out string reboundDiagnostic), Is.True, reboundDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(rebound, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseRegistry.MaterialEntry entry = window.Database.Registry.MaterialEntries.Single(item => item.LogicalName == "Body");
                Texture expectedBaseColor = entry.Material.GetTexture("_BaseMap");
                Texture normalTexture = entry.Material.GetTexture("_BumpMap");
                Assert.That(entry.TextureResourceNames, Does.Contain("Base_Body"));
                Assert.That(window.ResolveMaterialEntryPreviewForTest("Body"), Is.SameAs(expectedBaseColor));
                Assert.That(window.ResolveMaterialEntryPreviewForTest("Body"), Is.Not.SameAs(normalTexture));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MaterialsDetail_OpenDatabaseEditsExistingEntriesAndSavesRenames()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D sourceTexture = new Texture2D(1, 1) { name = "SourceTexture" };
            Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "SourceMaterial" };
            source.SetTexture("_BaseMap", sourceTexture);
            AssetDatabase.CreateAsset(sourceTexture, Root + "/RenameSourceTexture.asset");
            AssetDatabase.CreateAsset(source, Root + "/RenameSourceMaterial.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("MasterFigure");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = source;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "MasterFigure", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer importedRenderer = opened.transform.Find("Intermediate/MasterFigure").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", importedRenderer, 0, importedRenderer.sharedMaterial, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { admission }, out string entrySaveDiagnostic), Is.True, entrySaveDiagnostic); }
            finally { admission.Dispose(); }

            Func<string, string, string, string, bool> originalRenameConfirm = ShapeSyncDatabaseWindow.ConfirmTextureRename;
            Func<bool> originalIsBatchMode = ShapeSyncDatabaseWindow.IsBatchMode;
            Func<string, string, string, string, string, int> originalDirtyDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsBatchMode = () => false;
                ShapeSyncDatabaseWindow.ConfirmTextureRename = (_, _, _, _) => true;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "Body" }));
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "Skin"), Is.True);
                Assert.That(window.IsMaterialsDetailDirtyForTest, Is.True);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True, window.Diagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rebound, out string reboundDiagnostic), Is.True, reboundDiagnostic);
                ShapeSyncDatabaseRegistry.MaterialEntry renamed = rebound.Registry.MaterialEntries.Single();
                Assert.That(renamed.LogicalName, Is.EqualTo("Skin"));
                Assert.That(renamed.Material.name, Is.EqualTo("MasterFigure_Skin_Material"));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Any(material => material.name == "Body_Material"), Is.False, "Entry rename must rename the owned Figure Material sub-asset with the Figure Master Name.");
                Assert.That(renamed.TextureResourceNames, Is.EqualTo(new[] { "MasterFigure_Skin" }));
                Assert.That(rebound.Registry.TextureResources.Single().Texture.name, Is.EqualTo("MasterFigure_Skin"), "Entry rename must rename the owned Figure Texture sub-asset as well as its logical resource name.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => texture.name == "MasterFigure_Body"), Is.False, "Entry rename must not leave a Texture sub-asset under its pre-rename name.");
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "Skin" }));
            }
            finally
            {
                ShapeSyncDatabaseWindow.ConfirmTextureRename = originalRenameConfirm;
                ShapeSyncDatabaseWindow.IsBatchMode = originalIsBatchMode;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDirtyDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void MaterialEntryRename_AtomicallyRejectsInvalidInputRollsBackAndExchangesExistingEntries()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material bodySource = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BodySource" };
            Material faceSource = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "FaceSource" };
            Texture2D bodyTexture = new Texture2D(1, 1) { name = "BodyTexture" };
            Texture2D faceTexture = new Texture2D(1, 1) { name = "FaceTexture" };
            bodySource.SetTexture("_BaseMap", bodyTexture);
            faceSource.SetTexture("_BaseMap", faceTexture);
            AssetDatabase.CreateAsset(bodyTexture, Root + "/RenameBodyTexture.asset");
            AssetDatabase.CreateAsset(faceTexture, Root + "/RenameFaceTexture.asset");
            AssetDatabase.CreateAsset(bodySource, Root + "/RenameBody.mat");
            AssetDatabase.CreateAsset(faceSource, Root + "/RenameFace.mat");
            Material importedBody = new Material(bodySource) { name = "Base_BodySource" };
            Material importedFace = new Material(faceSource) { name = "Base_FaceSource" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                context.AddSubAsset(importedBody); context.AddSubAsset(importedFace);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { importedBody, importedFace };
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer renderer = opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", renderer, 0, renderer.sharedMaterials[0], out ShapeSyncMaterialAdapterResolver.Admission bodyAdmission, out string bodyDiagnostic), Is.True, bodyDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Face", renderer, 1, renderer.sharedMaterials[1], out ShapeSyncMaterialAdapterResolver.Admission faceAdmission, out string faceDiagnostic), Is.True, faceDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { bodyAdmission, faceAdmission }, out string entryDiagnostic), Is.True, entryDiagnostic); }
            finally { bodyAdmission.Dispose(); faceAdmission.Dispose(); }
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Select(material => material.name), Does.Not.Contain("Base_BodySource"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Select(material => material.name), Does.Not.Contain("Base_FaceSource"));
            Texture2D normal = new Texture2D(1, 1) { name = "SourceNormal" };
            AssetDatabase.CreateAsset(normal, Root + "/SourceNormal.asset");
            Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath,
                new[] { "Body" }, new[] { new ShapeSyncNormalEntryAuthoring.Assignment("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, normal) }, out string normalDiagnostic), Is.True, normalDiagnostic);
            Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath,
                new[] { "Body", "Face" }, System.Array.Empty<ShapeSyncNormalEntryAuthoring.Assignment>(), out string missingBaseDiagnostic), Is.False);
            Assert.That(missingBaseDiagnostic, Does.Contain("Base Normal cannot be None"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rejectedMissingBaseSave, out string rejectedMissingBaseOpenDiagnostic), Is.True, rejectedMissingBaseOpenDiagnostic);
            Assert.That(rejectedMissingBaseSave.Registry.FigureNormalEntries.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "Body" }), "A new Figure Normal Entry without a Base Normal must not be persisted.");
            Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath,
                new[] { "Body" }, new[] { new ShapeSyncNormalEntryAuthoring.Assignment("Face", ShapeSyncDatabaseRegistry.BaseShapeKey, normal) }, out string unselectedSaveDiagnostic), Is.False);
            Assert.That(unselectedSaveDiagnostic, Does.Contain("declared Figure Normal Entry"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rejectedUnselectedSave, out string rejectedUnselectedOpenDiagnostic), Is.True, rejectedUnselectedOpenDiagnostic);
            Assert.That(rejectedUnselectedSave.Registry.NormalEntries.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "Body" }), "An unselected Material must not be silently persisted as a Normal Entry.");
            Assert.That(rejectedUnselectedSave.Registry.FigureNormalEntries.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "Body" }));
            Assert.That(opened.Registry.TrySetNormalEntry("Body", "MissingFbm", normal, out string missingNormalDiagnostic), Is.False);
            Assert.That(missingNormalDiagnostic, Does.Contain("Base or an existing FBM"));
            Assert.That(opened.Registry.TrySetNormalEntry("Face", ShapeSyncDatabaseRegistry.BaseShapeKey, normal, out string undeclaredNormalDiagnostic), Is.False);
            Assert.That(undeclaredNormalDiagnostic, Does.Contain("declared Figure Normal Entry"));
            Assert.That(opened.Registry.TrySetFigureNormalEntries(new[] { "Body", "Face" }, out _, out string figureNormalEntryDiagnostic), Is.True, figureNormalEntryDiagnostic);
            Assert.That(opened.Registry.TrySetNormalEntry("Face", "Pbm_Test", normal, out string pbmNormalDiagnostic), Is.False);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase normalReopened, out string normalOpenDiagnostic), Is.True, normalOpenDiagnostic);
            Assert.That(normalReopened.Registry.NormalEntries.Single().MaterialEntryName, Is.EqualTo("Body"));
            Assert.That(normalReopened.Registry.NormalEntries.Single().ShapeKey, Is.EqualTo(ShapeSyncDatabaseRegistry.BaseShapeKey));
            ShapeSyncDatabaseRegistry.NormalEntry normalEntry = normalReopened.Registry.NormalEntries.Single();
            Assert.That(normalEntry.Texture, Is.Not.SameAs(normal), "An external Normal must be copied into the Database Texture Entry registry.");
            Assert.That(normalEntry.TextureResourceName, Is.EqualTo("Body_Base_Normal"));
            Assert.That(AssetDatabase.GetAssetPath(normalEntry.Texture), Is.EqualTo(databasePath));
            Assert.That(normalReopened.Registry.TextureResources.Single(resource => resource.LogicalName == normalEntry.TextureResourceName).Texture, Is.SameAs(normalEntry.Texture));
            Assert.That(normalReopened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").TextureResourceNames, Does.Contain(normalEntry.TextureResourceName));
            int resourceCountBeforeReuse = normalReopened.Registry.TextureResources.Count;
            Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath,
                new[] { "Body" }, new[] { new ShapeSyncNormalEntryAuthoring.Assignment("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, normalEntry.Texture) }, out string reuseNormalDiagnostic), Is.True, reuseNormalDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reusedNormalDatabase, out string reuseNormalOpenDiagnostic), Is.True, reuseNormalOpenDiagnostic);
            ShapeSyncDatabaseRegistry.NormalEntry reusedNormalEntry = reusedNormalDatabase.Registry.NormalEntries.Single();
            Assert.That(reusedNormalEntry.Texture, Is.SameAs(normalEntry.Texture), "A Database Texture must be reused without another copy.");
            Assert.That(reusedNormalEntry.TextureResourceName, Is.EqualTo(normalEntry.TextureResourceName));
            Assert.That(reusedNormalDatabase.Registry.TextureResources.Count, Is.EqualTo(resourceCountBeforeReuse));
            Texture2D databaseOnlyNormal = new Texture2D(1, 1) { name = "DatabaseOnlyNormal" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (_, _, context) => context.AddSubAsset(databaseOnlyNormal), out string stageDatabaseOnlyNormalDiagnostic), Is.True, stageDatabaseOnlyNormalDiagnostic);
            Texture stagedDatabaseOnlyNormal = AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Single(texture => texture.name == "DatabaseOnlyNormal");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(stagedDatabaseOnlyNormal, out string stagedDatabaseOnlyNormalGuid, out long stagedDatabaseOnlyNormalLocalId), Is.True);
            int resourceCountBeforeDatabaseOnlyNormal = reusedNormalDatabase.Registry.TextureResources.Count;
            Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(databasePath,
                new[] { "Body" }, new[] { new ShapeSyncNormalEntryAuthoring.Assignment("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, stagedDatabaseOnlyNormal) }, out string databaseOnlyNormalDiagnostic), Is.True, databaseOnlyNormalDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase databaseOnlyNormalSaved, out string databaseOnlyNormalOpenDiagnostic), Is.True, databaseOnlyNormalOpenDiagnostic);
            ShapeSyncDatabaseRegistry.NormalEntry databaseOnlyNormalEntry = databaseOnlyNormalSaved.Registry.NormalEntries.Single();
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(databaseOnlyNormalEntry.Texture, out string savedDatabaseOnlyNormalGuid, out long savedDatabaseOnlyNormalLocalId), Is.True);
            Assert.That(savedDatabaseOnlyNormalGuid, Is.EqualTo(stagedDatabaseOnlyNormalGuid), "A Database-owned but unregistered Normal must be registered without cloning.");
            Assert.That(savedDatabaseOnlyNormalLocalId, Is.EqualTo(stagedDatabaseOnlyNormalLocalId), "A Database-owned but unregistered Normal must retain its original sub-asset identity.");
            Assert.That(databaseOnlyNormalEntry.TextureResourceName, Is.EqualTo("Body_Base_Normal_2"));
            Assert.That(databaseOnlyNormalSaved.Registry.TextureResources.Single(resource => resource.LogicalName == databaseOnlyNormalEntry.TextureResourceName).Texture, Is.SameAs(databaseOnlyNormalEntry.Texture));
            Assert.That(databaseOnlyNormalSaved.Registry.TextureResources.Count, Is.EqualTo(resourceCountBeforeDatabaseOnlyNormal + 1));
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRenameTextureResource("Body_Base_Normal_2", "NormalBaseRenamed", out string normalRenameDiagnostic), Is.True, normalRenameDiagnostic);
            }, out string normalRenameSaveDiagnostic), Is.True, normalRenameSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase normalRenamedDatabase, out string normalRenameOpenDiagnostic), Is.True, normalRenameOpenDiagnostic);
            ShapeSyncDatabaseRegistry.NormalEntry normalRenamedEntry = normalRenamedDatabase.Registry.NormalEntries.Single();
            Assert.That(normalRenamedEntry.TextureResourceName, Is.EqualTo("NormalBaseRenamed"), "Texture Entry rename must update the Generate-facing Normal logical name.");
            Assert.That(normalRenamedDatabase.Registry.TextureResources.Single(resource => resource.LogicalName == normalRenamedEntry.TextureResourceName).Texture, Is.SameAs(normalRenamedEntry.Texture));

            ShapeSyncMaterialEntryImport.Rename[] duplicate = { new ShapeSyncMaterialEntryImport.Rename("Body", "Same"), new ShapeSyncMaterialEntryImport.Rename("Face", "Same") };
            Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, duplicate, false, out string duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic, Does.Contain("unique"));
            ShapeSyncMaterialEntryImport.Rename[] blank = { new ShapeSyncMaterialEntryImport.Rename("Body", ""), new ShapeSyncMaterialEntryImport.Rename("Face", "Face") };
            Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, blank, false, out string blankDiagnostic), Is.False);
            Assert.That(blankDiagnostic, Does.Contain("non-empty"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rejected, out string rejectDiagnostic), Is.True, rejectDiagnostic);
            Assert.That(rejected.Registry.MaterialEntries.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Body", "Face" }));
            Dictionary<string, string> textureNamesBeforeFailedRename = rejected.Registry.TextureResources
                .ToDictionary(resource => resource.LogicalName, resource => resource.Texture.name, StringComparer.Ordinal);
            Dictionary<string, string> materialNamesBeforeFailedRename = rejected.Registry.MaterialEntries
                .ToDictionary(entry => entry.LogicalName, entry => entry.Material.name, StringComparer.Ordinal);

            Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, new[] { new ShapeSyncMaterialEntryImport.Rename("Body", "Skin"), new ShapeSyncMaterialEntryImport.Rename("Face", "Face") }, true, out string rollbackDiagnostic), Is.False);
                Assert.That(rollbackDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rolledBack, out string rollbackOpenDiagnostic), Is.True, rollbackOpenDiagnostic);
            Assert.That(rolledBack.Registry.MaterialEntries.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Body", "Face" }));
            Assert.That(rolledBack.Registry.MaterialEntries.Select(entry => entry.Material.name), Is.EqualTo(new[] { "Base_Body_Material", "Base_Face_Material" }));
            Assert.That(rolledBack.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0", "Texture-1", "Body_Base_Normal", "NormalBaseRenamed" }));
            Assert.That(rolledBack.Registry.TextureResources.ToDictionary(resource => resource.LogicalName, resource => resource.Texture.name, StringComparer.Ordinal),
                Is.EqualTo(textureNamesBeforeFailedRename), "A failed Entry rename must roll back each Texture sub-asset name together with its Resource name.");
            Assert.That(rolledBack.Registry.MaterialEntries.ToDictionary(entry => entry.LogicalName, entry => entry.Material.name, StringComparer.Ordinal),
                Is.EqualTo(materialNamesBeforeFailedRename), "A failed Entry rename must roll back each Material sub-asset name together with its Entry name.");

            Assert.That(ShapeSyncMaterialEntryImport.TryRename(databasePath, new[] { new ShapeSyncMaterialEntryImport.Rename("Body", "Face"), new ShapeSyncMaterialEntryImport.Rename("Face", "Body") }, false, out string swapDiagnostic), Is.True, swapDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase swapped, out string swapOpenDiagnostic), Is.True, swapOpenDiagnostic);
            Assert.That(swapped.Registry.NormalEntries.Single().MaterialEntryName, Is.EqualTo("Face"));
            Assert.That(swapped.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face").MaterialSlot, Is.EqualTo(0));
            Assert.That(swapped.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").MaterialSlot, Is.EqualTo(1));
            Assert.That(swapped.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face").Material.name, Is.EqualTo("Base_Face_Material"));
            Assert.That(swapped.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material.name, Is.EqualTo("Base_Body_Material"));
            Assert.That(swapped.Registry.TrySetNormalEntry("Face", ShapeSyncDatabaseRegistry.BaseShapeKey, normalRenamedEntry.Texture, out string externalNormalDiagnostic), Is.True, externalNormalDiagnostic);
            EditorUtility.SetDirty(swapped.Registry);
            AssetDatabase.SaveAssets();
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string externalNormalOpenDiagnostic), Is.True, externalNormalOpenDiagnostic);
        }

        [Test]
        public void MaterialsDetail_ShowsNoneForTexturelessEntryAndReportsUnsupportedShader()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material supported = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Supported" };
            Material unsupported = new Material(Shader.Find("Sprites/Default")) { name = "Unsupported" };
            AssetDatabase.CreateAsset(supported, Root + "/TexturelessSupported.mat");
            AssetDatabase.CreateAsset(unsupported, Root + "/Unsupported.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { supported, unsupported };
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "MaterialEntry-0" }));
                Assert.That(window.MaterialDraftPreviewsForTest, Is.EqualTo(new Texture[] { null }));
                Assert.That(window.MaterialDraftDiagnosticForTest, Does.Contain("no ShapeSync Material Shader Adapter"));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MaterialsDetail_RejectsDuplicateDraftNamesWithoutChangingDatabaseOrSelection()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material first = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "First" };
            Material second = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Second" };
            AssetDatabase.CreateAsset(first, Root + "/DuplicateFirst.mat");
            AssetDatabase.CreateAsset(second, Root + "/DuplicateSecond.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterials = new[] { first, second };
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);

            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Materials), Is.True);
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "Duplicate"), Is.True);
                Assert.That(window.TrySetMaterialDraftNameForTest(1, "Duplicate"), Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;

                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Materials));
                Assert.That(window.MaterialDraftNamesForTest, Is.EqualTo(new[] { "Duplicate", "Duplicate" }));
                Assert.That(window.Diagnostic, Does.Contain("unique"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase unchanged, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(unchanged.Registry.MaterialEntries, Is.Empty);
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void MaterialEntryImport_RejectsInitialSaveWithoutBaseFigureBeforeStagingAssets()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase target, out string targetCreateDiagnostic), Is.True, targetCreateDiagnostic);
            string targetPath = AssetDatabase.GetAssetPath(target);
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase source, out string sourceCreateDiagnostic), Is.True, sourceCreateDiagnostic);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            Material sourceMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Source" };
            AssetDatabase.CreateAsset(sourceMaterial, Root + "/NoBaseSource.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(sourcePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("SourceBase");
                baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = sourceMaterial;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "SourceBase", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string sourceSetupDiagnostic), Is.True, sourceSetupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(sourcePath, out ShapeSyncDatabase openedSource, out string sourceOpenDiagnostic), Is.True, sourceOpenDiagnostic);
            SkinnedMeshRenderer sourceRenderer = openedSource.transform.Find("Intermediate/SourceBase").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(openedSource, "Body", sourceRenderer, 0, sourceMaterial, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            try
            {
                Assert.That(ShapeSyncMaterialEntryImport.TrySave(targetPath, new[] { admission }, out string saveDiagnostic), Is.False);
                Assert.That(saveDiagnostic, Does.Contain("exactly one Base Figure"));
            }
            finally { admission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(targetPath, out ShapeSyncDatabase unchanged, out string targetOpenDiagnostic), Is.True, targetOpenDiagnostic);
            Assert.That(unchanged.Registry.MaterialEntries, Is.Empty);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(targetPath).OfType<Material>(), Is.Empty, "Base Figure validation must occur before Material sub-assets are staged.");
        }

        [Test]
        public void MaterialEntryImport_FailedSubAssetStageRollsBackTheDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material source = new Material(shader) { name = "Source" };
            AssetDatabase.CreateAsset(source, Root + "/Source.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMaterial = source;
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string createBaseDiagnostic), Is.True, createBaseDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>(), 0, source, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            Object[] assetsBeforeSave = AssetDatabase.LoadAllAssetsAtPath(databasePath);
            int adaptersBeforeSave = assetsBeforeSave.Count(asset => asset is MaterialShaderAdapter);
            int materialsBeforeSave = assetsBeforeSave.Count(asset => asset is Material);
            int texturesBeforeSave = assetsBeforeSave.Count(asset => asset is Texture);
            Action<Object, string> originalAdd = ShapeSyncDatabaseTransaction.AddObjectToAsset;
            try
            {
                int stagedAssetCount = 0;
                ShapeSyncDatabaseTransaction.AddObjectToAsset = (asset, path) =>
                {
                    stagedAssetCount++;
                    originalAdd(asset, path);
                    if (stagedAssetCount == 2) throw new InvalidOperationException("Injected sub-asset failure");
                };
                Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { admission }, out string saveDiagnostic), Is.False);
                Assert.That(saveDiagnostic, Does.Contain("Injected sub-asset failure"));
            }
            finally { ShapeSyncDatabaseTransaction.AddObjectToAsset = originalAdd; admission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase restored, out string restoredDiagnostic), Is.True, restoredDiagnostic);
            Assert.That(restored.Registry.MaterialEntries, Is.Empty);
            Assert.That(restored.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>().sharedMaterial.name, Is.EqualTo("Source"));
            Object[] assetsAfterRollback = AssetDatabase.LoadAllAssetsAtPath(databasePath);
            Assert.That(assetsAfterRollback.Count(asset => asset is MaterialShaderAdapter), Is.EqualTo(adaptersBeforeSave));
            Assert.That(assetsAfterRollback.Count(asset => asset is Material), Is.EqualTo(materialsBeforeSave));
            Assert.That(assetsAfterRollback.Count(asset => asset is Texture), Is.EqualTo(texturesBeforeSave));
        }

        [Test]
        public void MaterialEntryImport_SaveFailureRollsBackEntriesAndTextureResourcesTogether()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D sourceTexture = new Texture2D(1, 1) { name = "SourceTexture" };
            Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Source" };
            source.SetTexture("_BaseMap", sourceTexture);
            AssetDatabase.CreateAsset(sourceTexture, Root + "/AtomicSourceTexture.asset");
            AssetDatabase.CreateAsset(source, Root + "/AtomicSource.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMaterial = source;
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>(), 0, source, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { admission }, out string saveDiagnostic), Is.False);
                Assert.That(saveDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; admission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase restored, out string restoredDiagnostic), Is.True, restoredDiagnostic);
            Assert.That(restored.Registry.MaterialEntries, Is.Empty);
            Assert.That(restored.Registry.TextureResources, Is.Empty);
            Assert.That(restored.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>().sharedMaterial.name, Is.EqualTo("AtomicSource"));
        }

        [Test]
        public void MaterialEntryImport_ReopensEntriesAndAddsAnotherSlotWithItsOwnAdapter()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material first = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "First" };
            Material second = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Second" };
            Texture2D firstTexture = new Texture2D(1, 1) { name = "FirstTexture" };
            Texture2D secondTexture = new Texture2D(1, 1) { name = "SecondTexture" };
            AssetDatabase.CreateAsset(firstTexture, Root + "/FirstTexture.asset");
            AssetDatabase.CreateAsset(secondTexture, Root + "/SecondTexture.asset");
            first.SetTexture("_BaseMap", firstTexture);
            second.SetTexture("_BaseMap", secondTexture);
            AssetDatabase.CreateAsset(first, Root + "/First.mat");
            AssetDatabase.CreateAsset(second, Root + "/Second.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMaterials = new[] { first, second };
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string createBaseDiagnostic), Is.True, createBaseDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer openedRenderer = opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", openedRenderer, 0, first, out ShapeSyncMaterialAdapterResolver.Admission bodyAdmission, out string bodyDiagnostic), Is.True, bodyDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { bodyAdmission }, out string bodySaveDiagnostic), Is.True, bodySaveDiagnostic); }
            finally { bodyAdmission.Dispose(); }
            Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string failedResourceDiagnostic), Is.False);
                Assert.That(failedResourceDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase afterResourceRollback, out string afterResourceRollbackDiagnostic), Is.True, afterResourceRollbackDiagnostic);
            Assert.That(afterResourceRollback.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0" }));
            Assert.That(afterResourceRollback.Registry.MaterialEntries.Single().TextureResourceNames, Is.EqualTo(new[] { "Texture-0" }));

            Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string firstResourceDiagnostic), Is.True, firstResourceDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase afterFirstResource, out string afterFirstResourceDiagnostic), Is.True, afterFirstResourceDiagnostic);
            Assert.That(afterFirstResource.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0" }));

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase afterFirstSave, out string afterFirstDiagnostic), Is.True, afterFirstDiagnostic);
            SkinnedMeshRenderer afterFirstRenderer = afterFirstSave.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>();
            Assert.That(afterFirstSave.Registry.TryGetSingleBaseFigure(afterFirstSave, out _, out string resolveDiagnostic), Is.True, resolveDiagnostic);
            ShapeSyncDatabaseRegistry.MaterialEntry body = afterFirstSave.Registry.MaterialEntries.Single();
            Assert.That(body.Renderer, Is.SameAs(afterFirstRenderer));
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(afterFirstSave, "Face", afterFirstRenderer, 1, second, out ShapeSyncMaterialAdapterResolver.Admission faceAdmission, out string faceDiagnostic), Is.True, faceDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { faceAdmission }, out string faceSaveDiagnostic), Is.True, faceSaveDiagnostic); }
            finally { faceAdmission.Dispose(); }
            Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string incrementalResourceDiagnostic), Is.True, incrementalResourceDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase finalDatabase, out string finalDiagnostic), Is.True, finalDiagnostic);
            Assert.That(finalDatabase.Registry.TryGetSingleBaseFigure(finalDatabase, out _, out string finalResolveDiagnostic), Is.True, finalResolveDiagnostic);
            Assert.That(finalDatabase.Registry.MaterialEntries, Has.Count.EqualTo(2));
            Assert.That(finalDatabase.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0", "Texture-1" }));
            ShapeSyncDatabaseRegistry.MaterialEntry finalBody = finalDatabase.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body");
            ShapeSyncDatabaseRegistry.MaterialEntry finalFace = finalDatabase.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Face");
            Assert.That(finalBody.Material.name, Is.EqualTo("Base_Body_Material"), "The first incremental Entry save must apply the Base Figure prefix immediately.");
            Assert.That(finalFace.Material.name, Is.EqualTo("Base_Face_Material"), "A later incremental Entry save must use the same Figure-prefix service as the first save.");
            Assert.That(finalBody.Renderer, Is.SameAs(finalDatabase.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>()));
            Assert.That(finalFace.Renderer, Is.SameAs(finalBody.Renderer));
            Assert.That(finalFace.Adapter, Is.Not.SameAs(finalBody.Adapter));
            Assert.That(finalBody.Adapter.ExpectedShaderName, Is.EqualTo("Universal Render Pipeline/Unlit"));
            Assert.That(finalFace.Adapter.ExpectedShaderName, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(finalBody.TextureResourceNames, Is.EqualTo(new[] { "Texture-0" }));
            Assert.That(finalFace.TextureResourceNames, Is.EqualTo(new[] { "Texture-1" }));
        }

        [Test]
        public void MaterialEntryImport_ReopensDuplicateRendererNamesBySiblingIndexPath()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material first = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "First" };
            Material second = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Second" };
            AssetDatabase.CreateAsset(first, Root + "/First.mat"); AssetDatabase.CreateAsset(second, Root + "/Second.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                GameObject firstPart = new GameObject("Part"); firstPart.transform.SetParent(baseFigure.transform, false);
                GameObject secondPart = new GameObject("Part"); secondPart.transform.SetParent(baseFigure.transform, false);
                firstPart.AddComponent<SkinnedMeshRenderer>().sharedMaterial = first;
                secondPart.AddComponent<SkinnedMeshRenderer>().sharedMaterial = second;
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string baseCreationDiagnostic), Is.True, baseCreationDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Transform baseTransform = opened.transform.Find("Intermediate/Base");
            SkinnedMeshRenderer firstRenderer = baseTransform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer secondRenderer = baseTransform.GetChild(1).GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "First", firstRenderer, 0, first, out ShapeSyncMaterialAdapterResolver.Admission firstAdmission, out string firstDiagnostic), Is.True, firstDiagnostic);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Second", secondRenderer, 0, second, out ShapeSyncMaterialAdapterResolver.Admission secondAdmission, out string secondDiagnostic), Is.True, secondDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { firstAdmission, secondAdmission }, out string saveDiagnostic), Is.True, saveDiagnostic); }
            finally { firstAdmission.Dispose(); secondAdmission.Dispose(); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(reopened.Registry.TryGetSingleBaseFigure(reopened, out _, out string resolveDiagnostic), Is.True, resolveDiagnostic);
            ShapeSyncDatabaseRegistry.MaterialEntry firstEntry = reopened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "First");
            ShapeSyncDatabaseRegistry.MaterialEntry secondEntry = reopened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Second");
            Transform reopenedBase = reopened.transform.Find("Intermediate/Base");
            Assert.That(firstEntry.BaseRelativeRendererPath, Is.EqualTo("0"));
            Assert.That(secondEntry.BaseRelativeRendererPath, Is.EqualTo("1"));
            Assert.That(firstEntry.Renderer, Is.SameAs(reopenedBase.GetChild(0).GetComponent<SkinnedMeshRenderer>()));
            Assert.That(secondEntry.Renderer, Is.SameAs(reopenedBase.GetChild(1).GetComponent<SkinnedMeshRenderer>()));
            Assert.That(firstEntry.Material, Is.SameAs(firstEntry.Renderer.sharedMaterial));
            Assert.That(secondEntry.Material, Is.SameAs(secondEntry.Renderer.sharedMaterial));
        }

        [Test]
        public void TextureResourceImport_RejectsAnExternalTextureWithoutChangingResourceDefinitions()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D ownedSourceTexture = new Texture2D(1, 1) { name = "OwnedSource" };
            Texture2D externalTexture = new Texture2D(1, 1) { name = "External" };
            AssetDatabase.CreateAsset(ownedSourceTexture, Root + "/OwnedSource.asset");
            AssetDatabase.CreateAsset(externalTexture, Root + "/External.asset");
            Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Source" };
            source.SetTexture("_BaseMap", ownedSourceTexture);
            AssetDatabase.CreateAsset(source, Root + "/Source.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMaterial = source;
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
            }, out string baseCreationDiagnostic), Is.True, baseCreationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer renderer = opened.transform.Find("Intermediate/Base").GetComponent<SkinnedMeshRenderer>();
            Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", renderer, 0, source, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
            try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { admission }, out string saveDiagnostic), Is.True, saveDiagnostic); }
            finally { admission.Dispose(); }
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (databaseContents, _) =>
            {
                databaseContents.Registry.MaterialEntries.Single().Material.SetTexture("_BaseMap", externalTexture);
            }, out string corruptDiagnostic), Is.True, corruptDiagnostic);

            Assert.That(ShapeSyncTextureResourceImport.TryRegisterExistingMaterialTextures(databasePath, out string rejectionDiagnostic), Is.False);
            Assert.That(rejectionDiagnostic, Does.Contain("not owned"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rejected, out string rejectedOpenDiagnostic), Is.True, rejectedOpenDiagnostic);
            Assert.That(rejected.Registry.TextureResources.Select(resource => resource.LogicalName), Is.EqualTo(new[] { "Texture-0" }));
            Assert.That(rejected.Registry.MaterialEntries.Single().TextureResourceNames, Is.EqualTo(new[] { "Texture-0" }));
        }

        [Test]
        public void GeneralBinding_ClearsEarlierDiagnosticForAnEmptyFixedRegistry()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(null, out string failedDiagnostic), Is.False);
                Assert.That(failedDiagnostic, Is.Not.Empty);
                Assert.That(window.Diagnostic, Is.EqualTo(failedDiagnostic));

                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(bindDiagnostic, Is.Null);
                Assert.That(window.Diagnostic, Is.Null);
                Assert.That(window.FigureName, Is.Null);
                Assert.That(window.DatabaseFigurePrefab, Is.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void GeneralBinding_ReportsNestedRegistryEntryWithoutSelectingItAsBase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(created);
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                ShapeSyncDatabase databaseContents = prefabContents.GetComponent<ShapeSyncDatabase>();
                Transform intermediate = prefabContents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                GameObject registeredFigure = new GameObject("Base");
                registeredFigure.transform.SetParent(intermediate, false);
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", registeredFigure, out string registerDiagnostic), Is.True, registerDiagnostic);

                GameObject nestedParent = new GameObject("FBM-like child");
                nestedParent.transform.SetParent(intermediate, false);
                registeredFigure.transform.SetParent(nestedParent.transform, false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath), Is.Not.Null);
                EditorUtility.SetDirty(databaseContents.Registry);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabContents); }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ShapeSyncDatabase reopenedDatabase = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(assetPath);
            GameObject selectionSentinel = new GameObject("SelectionSentinel");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Selection.activeObject = selectionSentinel;
                Assert.That(window.TrySetDatabase(reopenedDatabase, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.Database, Is.SameAs(reopenedDatabase));
                Assert.That(window.Diagnostic, Does.Contain("registry entry is invalid"));
                Assert.That(window.FigureName, Is.Null);
                Assert.That(window.DatabaseFigurePrefab, Is.Null);
                Assert.That(Selection.activeObject, Is.SameAs(selectionSentinel));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(selectionSentinel); }
        }

        [Test]
        public void GeneralBinding_UsesOnlyRegistryBaseWhenFbmLikeMeshSubAssetIsMixedIntoDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(created);
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                ShapeSyncDatabase databaseContents = prefabContents.GetComponent<ShapeSyncDatabase>();
                GameObject registeredFigure = new GameObject("Base");
                registeredFigure.transform.SetParent(prefabContents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                Assert.That(databaseContents.Registry.TryRegisterBaseFigure(databaseContents, "Base", registeredFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
                EditorUtility.SetDirty(databaseContents.Registry);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(prefabContents, assetPath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefabContents); }

            Mesh mergedFbmMesh = new Mesh { name = "FBM_MergedSkinnedMesh" };
            try
            {
                AssetDatabase.AddObjectToAsset(mergedFbmMesh, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ShapeSyncDatabase reopenedDatabase = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(assetPath);
                Assert.That(Array.Exists(AssetDatabase.LoadAllAssetsAtPath(assetPath), asset => asset == mergedFbmMesh), Is.True);

                ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(window.TrySetDatabase(reopenedDatabase, out string bindDiagnostic), Is.True, bindDiagnostic);
                    Assert.That(window.Diagnostic, Is.Null);
                    Assert.That(window.FigureName, Is.EqualTo("Base"));
                    Assert.That(window.DatabaseFigurePrefab, Is.SameAs(reopenedDatabase.transform.Find("Intermediate/Base").gameObject));
                }
                finally { Object.DestroyImmediate(window); }
            }
            finally { /* TearDown owns the mixed Database asset and its mesh sub-asset. */ }
        }

        [Test]
        public void GeneralBinding_StaysEmptyWhenOnlyFbmLikeMeshSubAssetIsMixedIntoDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(created);
            Mesh mergedFbmMesh = new Mesh { name = "FBM_MergedSkinnedMesh" };
            try
            {
                AssetDatabase.AddObjectToAsset(mergedFbmMesh, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                ShapeSyncDatabase reopenedDatabase = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(assetPath);

                ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(window.TrySetDatabase(reopenedDatabase, out string bindDiagnostic), Is.True, bindDiagnostic);
                    Assert.That(window.Diagnostic, Is.Null);
                    Assert.That(window.FigureName, Is.Null);
                    Assert.That(window.DatabaseFigurePrefab, Is.Null);
                }
                finally { Object.DestroyImmediate(window); }
            }
            finally { /* TearDown owns the mixed Database asset and its mesh sub-asset. */ }
        }

        [Test]
        public void FigureDetail_SaveCommandPassesDisplayedMergedOrderAndNamedDatabaseArgumentsToImporter()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow.FigureAdmitter originalAdmit = ShapeSyncDatabaseWindow.AdmitFigure;
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            ShapeSyncDatabaseWindow.FigureImporter originalImport = ShapeSyncDatabaseWindow.ImportFigure;
            GameObject candidate = new GameObject("Candidate");
            GameObject face = new GameObject("Face");
            GameObject body = new GameObject("Body");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                SkinnedMeshRenderer faceRenderer = face.AddComponent<SkinnedMeshRenderer>();
                SkinnedMeshRenderer bodyRenderer = body.AddComponent<SkinnedMeshRenderer>();
                ShapeSyncFigureImportAdmission expectedAdmission = new ShapeSyncFigureImportAdmission(candidate, candidate, null, null, new[] { faceRenderer, bodyRenderer });
                string capturedTitle = null;
                string capturedMessage = null;
                string capturedButton = null;
                string capturedDatabasePath = null;
                string capturedName = null;
                ShapeSyncFigureImportAdmission capturedAdmission = null;
                ShapeSyncDatabaseWindow.AdmitFigure = (GameObject _, out ShapeSyncFigureImportAdmission admission, out string diagnostic) => { admission = expectedAdmission; diagnostic = null; return true; };
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (title, message, button) => { capturedTitle = title; capturedMessage = message; capturedButton = button; return true; };
                ShapeSyncDatabaseWindow.ImportFigure = (string databasePath, ShapeSyncFigureImportAdmission admission, string figureName, out string diagnostic) =>
                {
                    capturedDatabasePath = databasePath;
                    capturedAdmission = admission;
                    capturedName = figureName;
                    diagnostic = null;
                    return true;
                };
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFigureInputsForTest("MasterFigure", candidate);

                Assert.That(window.TrySaveFigure(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(capturedTitle, Is.EqualTo("Save Figure to Database"));
                Assert.That(capturedButton, Is.EqualTo("Save"));
                Assert.That(capturedMessage, Does.Contain("0: Face").And.Contain("1: Body").And.Contain("They will be merged."));
                Assert.That(capturedDatabasePath, Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(capturedAdmission, Is.SameAs(expectedAdmission));
                Assert.That(capturedName, Is.EqualTo("MasterFigure"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.AdmitFigure = originalAdmit;
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                ShapeSyncDatabaseWindow.ImportFigure = originalImport;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
                Object.DestroyImmediate(face);
                Object.DestroyImmediate(body);
            }
        }

        [Test]
        public void FigureDetail_ActualImportImmediatelyShowsNamedDatabasePrefabInSameWindow()
        {
            const string sourcePath = Root + "/WindowImportSource.prefab";
            GameObject source = CreateHumanoidSourceForFigureDetail("WindowImportSource", out Avatar avatar);
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                AssetDatabase.CreateAsset(avatar, Root + "/WindowImportAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/WindowImportMesh.asset");
                AssetDatabase.CreateAsset(renderer.sharedMaterial, Root + "/WindowImportMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (_, _, _) => true;
                ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                    window.SetFigureInputsForTest("MasterFigure", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));

                    Assert.That(window.TrySaveFigure(out string saveDiagnostic), Is.True, saveDiagnostic);
                    Assert.That(window.DatabaseFigurePrefab, Is.Not.Null);
                    Assert.That(window.DatabaseFigurePrefab.name, Is.EqualTo("MasterFigure"));
                    Assert.That(window.DatabaseFigurePrefab, Is.SameAs(window.Database.transform.Find("Intermediate/MasterFigure").gameObject));
                    Assert.That(window.Database.Registry, Is.Not.Null);
                    Assert.That(AssetDatabase.GetAssetPath(window.Database.Registry), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                    Assert.That(window.Database.Registry.BaseFigures, Has.Count.EqualTo(1));
                    Assert.That(window.Database.Registry.BaseFigures[0].Name, Is.EqualTo("MasterFigure"));
                    Assert.That(window.Database.Registry.BaseFigures[0].Figure, Is.SameAs(window.DatabaseFigurePrefab));

                    ShapeSyncDatabaseWindow reopened = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                    try
                    {
                        Assert.That(reopened.TrySetDatabase(database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                        Assert.That(reopened.FigureName, Is.EqualTo("MasterFigure"));
                        Assert.That(reopened.DatabaseFigurePrefab, Is.SameAs(window.DatabaseFigurePrefab));
                        Assert.That(reopened.FigurePrefab, Is.Null);
                        Assert.That(reopened.IsFigureDetailDirtyForTest, Is.False);
                    }
                    finally { Object.DestroyImmediate(reopened); }
                }
                finally { Object.DestroyImmediate(window); }
            }
            finally
            {
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FigureDetail_ImportImmediatelyRegistersOwnedTextureResourcesForTexturesDetail()
        {
            const string sourcePath = Root + "/WindowImportTextureSource.prefab";
            const string texturePath = Root + "/WindowImportTexture.asset";
            GameObject source = CreateHumanoidSourceForFigureDetail("WindowImportTextureSource", out Avatar avatar);
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                Texture2D sourceTexture = new Texture2D(1, 1) { name = "SourceTexture" };
                sourceTexture.SetPixel(0, 0, Color.cyan);
                sourceTexture.Apply();
                Material sourceMaterial = new Material(shader) { name = "SourceMaterial" };
                sourceMaterial.SetTexture("_BaseMap", sourceTexture);
                renderer.sharedMaterial = sourceMaterial;

                AssetDatabase.CreateAsset(avatar, Root + "/WindowImportTextureAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/WindowImportTextureMesh.asset");
                AssetDatabase.CreateAsset(sourceTexture, texturePath);
                AssetDatabase.CreateAsset(sourceMaterial, Root + "/WindowImportTextureMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (_, _, _) => true;

                ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                    window.SetFigureInputsForTest("MasterFigure", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
                    Assert.That(window.TrySaveFigure(out string saveDiagnostic), Is.True, saveDiagnostic);
                    Assert.That(window.Database.Registry.TextureResources, Has.Count.EqualTo(1));
                    Assert.That(window.Database.Registry.TextureResources[0].LogicalName, Is.EqualTo("Texture-0"));
                    Texture importedTexture = window.Database.Registry.TextureResources[0].Texture;
                    Assert.That(importedTexture, Is.Not.SameAs(sourceTexture));
                    Assert.That(AssetDatabase.GetAssetPath(importedTexture), Is.EqualTo(AssetDatabase.GetAssetPath(database)));

                    Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Textures), Is.True);
                    Assert.That(window.TextureDraftNamesForTest, Is.EqualTo(new[] { "Texture-0" }));
                    Assert.That(window.TextureDraftPreviewsForTest, Is.EqualTo(new[] { importedTexture }));
                    SkinnedMeshRenderer importedRenderer = window.DatabaseFigurePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(window.Database, "Body", importedRenderer, 0, importedRenderer.sharedMaterial, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    try
                    {
                        Assert.That(ShapeSyncMaterialEntryImport.TrySave(AssetDatabase.GetAssetPath(database), new[] { admission }, out string entryDiagnostic), Is.True, entryDiagnostic);
                    }
                    finally { admission.Dispose(); }
                }
                finally { Object.DestroyImmediate(window); }

                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase rollbackDatabase, out string rollbackCreateDiagnostic), Is.True, rollbackCreateDiagnostic);
                GameObject rollbackCandidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(rollbackCandidate, out ShapeSyncFigureImportAdmission rollbackAdmission, out string rollbackAdmissionDiagnostic), Is.True, rollbackAdmissionDiagnostic);
                Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureImport.TryImport(AssetDatabase.GetAssetPath(rollbackDatabase), rollbackAdmission, "RollbackFigure", out string rollbackDiagnostic), Is.False);
                    Assert.That(rollbackDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(rollbackDatabase), out ShapeSyncDatabase rolledBack, out string rollbackOpenDiagnostic), Is.True, rollbackOpenDiagnostic);
                Assert.That(rolledBack.Registry.BaseFigures, Is.Empty);
                Assert.That(rolledBack.Registry.TextureResources, Is.Empty);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(rollbackDatabase)).OfType<Texture>(), Is.Empty);

                Assert.That(AssetDatabase.DeleteAsset(texturePath), Is.True);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(reopened.Registry.TextureResources, Has.Count.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(reopened.Registry.TextureResources[0].Texture), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                ShapeSyncDatabaseRegistry.MaterialEntry entry = reopened.Registry.MaterialEntries.Single();
                Assert.That(entry.TextureResourceNames, Is.EqualTo(new[] { "Texture-0" }));
                Assert.That(entry.Material.GetTexture("_BaseMap"), Is.SameAs(reopened.Registry.TextureResources[0].Texture));
            }
            finally
            {
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                Object.DestroyImmediate(source);
            }
        }

        [TestCase("Cancel")]
        [TestCase("DialogException")]
        [TestCase("ImportFailure")]
        public void FigureDetail_SaveCommandRejectsConfirmAndImportFailuresWithoutChangingBinding(string failure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            ShapeSyncDatabaseWindow.FigureAdmitter originalAdmit = ShapeSyncDatabaseWindow.AdmitFigure;
            Func<string, string, string, bool> originalConfirm = ShapeSyncDatabaseWindow.ConfirmFigureImport;
            ShapeSyncDatabaseWindow.FigureImporter originalImport = ShapeSyncDatabaseWindow.ImportFigure;
            GameObject candidate = new GameObject("Candidate");
            GameObject rendererObject = new GameObject("Face");
            Object originalSelection = Selection.activeObject;
            GameObject selectionSentinel = new GameObject("SelectionSentinel");
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncFigureImportAdmission admission = new ShapeSyncFigureImportAdmission(candidate, candidate, null, null, new[] { rendererObject.AddComponent<SkinnedMeshRenderer>() });
                int importCalls = 0;
                ShapeSyncDatabaseWindow.AdmitFigure = (GameObject _, out ShapeSyncFigureImportAdmission admitted, out string diagnostic) => { admitted = admission; diagnostic = null; return true; };
                ShapeSyncDatabaseWindow.ConfirmFigureImport = (_, _, _) => failure == "DialogException" ? throw new InvalidOperationException("Injected dialog failure") : failure != "Cancel";
                ShapeSyncDatabaseWindow.ImportFigure = (string _, ShapeSyncFigureImportAdmission __, string ___, out string diagnostic) => { importCalls++; diagnostic = "Injected import failure"; return false; };
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFigureInputsForTest("MasterFigure", candidate);
                Selection.activeObject = selectionSentinel;

                Assert.That(window.TrySaveFigure(out string saveDiagnostic), Is.False);
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.DatabaseFigurePrefab, Is.Null);
                Assert.That(Selection.activeObject, Is.SameAs(selectionSentinel));
                if (failure == "Cancel")
                {
                    Assert.That(saveDiagnostic, Does.Contain("cancelled"));
                    Assert.That(importCalls, Is.Zero);
                }
                else if (failure == "DialogException")
                {
                    Assert.That(saveDiagnostic, Does.Contain("Could not confirm").And.Contain("Injected dialog failure"));
                    Assert.That(importCalls, Is.Zero);
                }
                else
                {
                    Assert.That(saveDiagnostic, Is.EqualTo("Injected import failure"));
                    Assert.That(importCalls, Is.EqualTo(1));
                }
            }
            finally
            {
                ShapeSyncDatabaseWindow.AdmitFigure = originalAdmit;
                ShapeSyncDatabaseWindow.ConfirmFigureImport = originalConfirm;
                ShapeSyncDatabaseWindow.ImportFigure = originalImport;
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(candidate);
                Object.DestroyImmediate(rendererObject);
                Selection.activeObject = originalSelection;
                Object.DestroyImmediate(selectionSentinel);
            }
        }

        [Test]
        public void FigureDetail_AddNormalEntryIsAvailableAfterOpenAndSavesBaseNormal()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "BaseMaterial" };
            Texture2D sourceNormal = new Texture2D(1, 1) { name = "FigureDetailNormal" };
            AssetDatabase.CreateAsset(sourceNormal, Root + "/FigureDetailNormal.asset");
            AssetDatabase.CreateAsset(source, Root + "/FigureDetailMaterial.mat");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<SkinnedMeshRenderer>().sharedMaterial = source;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string baseDiagnostic), Is.True, baseDiagnostic);

            Func<bool> originalIsBatchMode = ShapeSyncDatabaseWindow.IsBatchMode;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsBatchMode = () => true;
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Materials);
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "Body"), Is.True);
                Assert.That(window.TrySaveMaterialEntriesForTest(out string materialDiagnostic), Is.True, materialDiagnostic);

                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                Assert.That(window.CanAddFigureNormalEntryForTest, Is.True);
                Assert.That(window.TryAddFigureNormalEntryForTest(), Is.True);
                Assert.That(window.FigureNormalEntryMaterialNamesForTest, Is.EqualTo(new[] { "Body" }));
                Assert.That(window.HasFigureNormalEntryDraftForTest(0), Is.True, "An added Normal Entry must own its draft before its row is drawn.");
                Assert.That(window.IsNormalsDetailDirtyForTest, Is.True);
                Assert.That(window.IsFigureDetailDirtyForTest, Is.False, "Normal relation drafts belong exclusively to the Normals Detail.");
                Assert.That(window.TrySaveNormalsForTest(out string emptyNormalEntryDiagnostic), Is.False);
                Assert.That(emptyNormalEntryDiagnostic, Does.Contain("Base Normal cannot be None"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase emptyNormalEntryReopened, out string emptyNormalEntryOpenDiagnostic), Is.True, emptyNormalEntryOpenDiagnostic);
                Assert.That(emptyNormalEntryReopened.Registry.FigureNormalEntries, Is.Empty, "An incomplete Figure Normal Entry must not be persisted.");
                Assert.That(emptyNormalEntryReopened.Registry.NormalEntries, Is.Empty);
                Assert.That(window.TrySetNormalDraftForTest("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, sourceNormal), Is.True);
                Assert.That(window.TrySaveNormalsForTest(out string normalDiagnostic), Is.True, normalDiagnostic);

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                ShapeSyncDatabaseRegistry.NormalEntry normal = reopened.Registry.NormalEntries.Single();
                Assert.That(normal.MaterialEntryName, Is.EqualTo("Body"));
                Assert.That(normal.ShapeKey, Is.EqualTo(ShapeSyncDatabaseRegistry.BaseShapeKey));
                Assert.That(normal.Texture, Is.Not.SameAs(sourceNormal));
                Assert.That(normal.TextureResourceName, Is.EqualTo("Body_Base_Normal"));
                Assert.That(AssetDatabase.GetAssetPath(normal.Texture), Is.EqualTo(databasePath));

                Assert.That(window.TrySetDatabase(reopened, out string rebindDiagnostic), Is.True, rebindDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                Assert.That(window.FigureNormalEntryMaterialNamesForTest, Is.EqualTo(new[] { "Body" }));
                Assert.That(window.TryRemoveFigureNormalEntryForTest(0), Is.True);
                Assert.That(window.FigureNormalEntryMaterialNamesForTest, Is.Empty);
                Assert.That(window.IsNormalsDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveNormalsForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out string removeOpenDiagnostic), Is.True, removeOpenDiagnostic);
                Assert.That(removed.Registry.NormalEntries, Is.Empty);
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsBatchMode = originalIsBatchMode;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FbmDetail_RemoveCleansImportAllTextureResourcesAndReferences()
        {
            const string sourcePath = Root + "/FbmRemoveSource.prefab";
            GameObject source = CreateHumanoidSourceForFigureDetail("FbmRemoveSource", out Avatar avatar);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Texture2D texture = new Texture2D(1, 1) { name = "FbmRemoveTexture" };
                material.SetTexture("_BaseMap", texture);
                renderer.sharedMaterial = material;
                AssetDatabase.CreateAsset(avatar, Root + "/FbmRemoveAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/FbmRemoveMesh.asset");
                AssetDatabase.CreateAsset(texture, Root + "/FbmRemoveTexture.asset");
                AssetDatabase.CreateAsset(material, Root + "/FbmRemoveMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                GameObject persistent = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                string databasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistent, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "Master", out string importDiagnostic), Is.True, importDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                SkinnedMeshRenderer baseRenderer = opened.transform.Find("Intermediate/Master").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseRenderer, 0, baseRenderer.sharedMaterials[0], out ShapeSyncMaterialAdapterResolver.Admission materialAdmission, out string materialDiagnostic), Is.True, materialDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { materialAdmission }, out string materialSaveDiagnostic), Is.True, materialSaveDiagnostic); }
                finally { materialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFbmAxisDraftsForTest(new[] { "Tall" }, new[] { persistent }, new[] { true });
                Assert.That(window.TrySaveFbmAxisDraftsForTest(out string fbmDiagnostic), Is.True, fbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase withFbm, out string withFbmDiagnostic), Is.True, withFbmDiagnostic);
                Assert.That(withFbm.Registry.TextureResources.Any(entry => entry.LogicalName == "Tall_Body"), Is.True);
                Assert.That(withFbm.Registry.TextureResources.Single(entry => entry.LogicalName == "Tall_Body").Owner.SourceShapeKey, Is.EqualTo("Tall"));
                Assert.That(withFbm.Registry.TextureResources.Single(entry => entry.LogicalName == "Tall_Body").Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure));
                Assert.That(withFbm.Registry.MaterialEntries.Single().TextureResourceNames, Does.Contain("Tall_Body"));
                window.SetPbmAxisDraftForTest("Breath", persistent, new[] { "Tall" }, new[] { persistent });
                Assert.That(window.TrySavePbmAxisDraftsForTest(out string pbmDiagnostic), Is.True, pbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out withFbm, out withFbmDiagnostic), Is.True, withFbmDiagnostic);
                Assert.That(window.TrySetDatabase(withFbm, out bindDiagnostic), Is.True, bindDiagnostic);
                SkinnedMeshRenderer tallRenderer = withFbm.Registry.FigureAxes.Single(axis => axis.Name == "Tall").Figures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                string removedTallMeshName = tallRenderer.sharedMesh.name;
                string removedTallMaterialName = tallRenderer.sharedMaterial.name;
                SkinnedMeshRenderer removedPbmRenderer = withFbm.Registry.FigureAxes.Single(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && axis.Name == "Breath")
                    .Figures.Single(binding => binding.FbmName == "Tall").Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                string removedPbmMeshName = removedPbmRenderer.sharedMesh.name;
                string survivingBaseMaterialName = withFbm.Registry.MaterialEntries.Single().Material.name;
                Assert.That(AssetDatabase.GetAssetPath(tallRenderer.sharedMesh), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.GetAssetPath(tallRenderer.sharedMaterial), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.GetAssetPath(removedPbmRenderer.sharedMesh), Is.EqualTo(databasePath));
                Assert.That(ShapeSyncTextureResourceAuthoring.TrySave(databasePath,
                    new[] { new ShapeSyncTextureResourceAuthoring.Rename("Tall_Body", "RenamedImport") },
                    Array.Empty<ShapeSyncTextureResourceAuthoring.Addition>(), out string renameDiagnostic), Is.True, renameDiagnostic);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
                {
                    Texture2D manual = new Texture2D(1, 1) { name = "Tall_Manual" };
                    context.AddSubAsset(manual);
                    Assert.That(contents.Registry.TryRegisterTextureResource("Tall_Manual", manual, out string manualDiagnostic), Is.True, manualDiagnostic);
                }, out string manualSetupDiagnostic), Is.True, manualSetupDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out withFbm, out withFbmDiagnostic), Is.True, withFbmDiagnostic);
                Assert.That(window.TrySetDatabase(withFbm, out bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(withFbm.Registry.TrySetPcmSlots(12, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(withFbm.Registry.TrySetKeptRawBlendShapeNames(withFbm, Array.Empty<string>(), out morphDiagnostic), Is.True, morphDiagnostic);
                EditorUtility.SetDirty(withFbm.Registry);
                AssetDatabase.SaveAssets();

                Func<GameObject, string, bool> originalSavePrefabAsset = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(window.TryRemoveFbmAxisForTest("Tall", out string rollbackDiagnostic), Is.False);
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefabAsset; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rolledBack, out string rollbackOpenDiagnostic), Is.True, rollbackOpenDiagnostic);
                Assert.That(rolledBack.Registry.FigureAxes.Any(axis => axis.Name == "Tall"), Is.True);
                Assert.That(rolledBack.Registry.TextureResources.Any(entry => entry.LogicalName == "RenamedImport"), Is.True);
                Assert.That(rolledBack.Registry.TextureResources.Any(entry => entry.LogicalName == "Tall_Manual"), Is.True);
                Assert.That(rolledBack.Registry.MaterialEntries.Single().TextureResourceNames, Does.Contain("RenamedImport"));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == removedTallMeshName), Is.True, "A failed FBM removal must restore the FBM merged Mesh.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == removedPbmMeshName), Is.True, "A failed FBM removal must restore dependent PBM merged Meshes.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Any(materialAsset => materialAsset.name == removedTallMaterialName), Is.True, "A failed FBM removal must restore the FBM Material.");

                Assert.That(window.TryRemoveFbmAxisForTest("Tall", out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out string removedDiagnostic), Is.True, removedDiagnostic);
                Assert.That(removed.Registry.FigureAxes, Is.Empty);
                Assert.That(removed.Registry.TextureResources.Any(entry => entry.LogicalName == "RenamedImport"), Is.False);
                Assert.That(removed.Registry.TextureResources.Any(entry => entry.LogicalName == "Tall_Manual"), Is.True);
                Assert.That(removed.Registry.MaterialEntries.Single().TextureResourceNames, Does.Not.Contain("RenamedImport"));
                Assert.That(removed.transform.Find("Intermediate/Tall"), Is.Null);
                Assert.That(removed.Registry.FigureAxes.Any(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.False, "Removing an FBM must discard every dependent PBM Figure.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == removedTallMeshName), Is.False, "Removing an FBM must reclaim its unreferenced merged Mesh sub-asset.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == removedPbmMeshName), Is.False, "Removing an FBM must also reclaim each discarded PBM merged Mesh sub-asset.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Any(materialAsset => materialAsset.name == removedTallMaterialName), Is.False, "Removing an FBM must reclaim its unreferenced Material sub-asset.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>().Any(materialAsset => materialAsset.name == survivingBaseMaterialName), Is.True, "Removing an FBM must retain a Base Material still referenced by the Database.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(textureAsset => textureAsset.name == "RenamedImport"), Is.False, "Removing an FBM must reclaim its unreferenced owned Texture sub-asset.");
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), Is.Not.Null, "FBM removal must not modify or remove the external source Prefab.");
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(Root + "/FbmRemoveMaterial.mat"), Is.Not.Null, "FBM removal must not remove an external source Material.");
                Assert.That(removed.Registry.FbmAxesFinalized, Is.False);
                Assert.That(removed.Registry.PcmSlots, Is.EqualTo(12));
                Assert.That(removed.Registry.KeptRawBlendShapeNames, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FigureAxisImport_ImportsFbmAndCompletePbmRowsInOneOwnedDatabaseTransaction()
        {
            const string sourcePath = Root + "/AxisImportSource.prefab";
            GameObject source = CreateHumanoidSourceForFigureDetail("AxisImportSource", out Avatar avatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                Mesh sourceMesh = renderer.sharedMesh;
                sourceMesh.AddBlendShapeFrame("CommonRawShape", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
                sourceMesh.AddBlendShapeFrame("PbmOnlySourceShape", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
                sourceMesh.AddBlendShapeFrame("FBM_ReservedRaw", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
                Material sourceMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Texture2D sourceTexture = new Texture2D(1, 1) { name = "AxisImportTexture" };
                sourceMaterial.SetTexture("_BaseMap", sourceTexture);
                renderer.sharedMaterial = sourceMaterial;
                AssetDatabase.CreateAsset(avatar, Root + "/AxisImportAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/AxisImportMesh.asset");
                AssetDatabase.CreateAsset(sourceTexture, Root + "/AxisImportTexture.asset");
                AssetDatabase.CreateAsset(renderer.sharedMaterial, Root + "/AxisImportMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                GameObject persistentSource = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                SkinnedMeshRenderer persistentRenderer = persistentSource.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                Mesh persistentSourceMesh = persistentRenderer.sharedMesh;
                Material persistentSourceMaterial = persistentRenderer.sharedMaterial;
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistentSource, out ShapeSyncFigureImportAdmission admission, out string sourceAdmissionDiagnostic), Is.True, sourceAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(AssetDatabase.GetAssetPath(database), admission, "Master", out string baseImportDiagnostic), Is.True, baseImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                SkinnedMeshRenderer baseMaterialRenderer = opened.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseMaterialRenderer, 0, baseMaterialRenderer.sharedMaterial,
                    out ShapeSyncMaterialAdapterResolver.Admission baseMaterialAdmission, out string baseMaterialAdmissionDiagnostic), Is.True, baseMaterialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(AssetDatabase.GetAssetPath(database), new[] { baseMaterialAdmission }, true, out string baseMaterialSaveDiagnostic), Is.True, baseMaterialSaveDiagnostic); }
                finally { baseMaterialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out opened, out openDiagnostic), Is.True, openDiagnostic);

                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase materialGateDatabase, out string materialGateCreateDiagnostic), Is.True, materialGateCreateDiagnostic);
                string materialGatePath = AssetDatabase.GetAssetPath(materialGateDatabase);
                Assert.That(ShapeSyncFigureImport.TryImport(materialGatePath, admission, "GateMaster", out string materialGateBaseDiagnostic), Is.True, materialGateBaseDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(materialGatePath, out ShapeSyncDatabase materialGateOpened, out string materialGateOpenDiagnostic), Is.True, materialGateOpenDiagnostic);
                Assert.That(materialGateOpened.Registry.TryAdmitFigureAxes(materialGateOpened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] materialGateAxes, out string materialGateAdmissionDiagnostic), Is.True, materialGateAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(materialGatePath, new[]
                {
                    new ShapeSyncFigureAxisImportRequest(materialGateAxes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(materialGateAxes[1], new[] { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission), new ShapeSyncAxisFigureSource("Tall", admission) })
                }, out string materialGateImportDiagnostic), Is.False);
                Assert.That(materialGateImportDiagnostic, Does.Contain("saved Figure Material"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(materialGatePath, out ShapeSyncDatabase materialGateRolledBack, out string materialGateRollbackDiagnostic), Is.True, materialGateRollbackDiagnostic);
                Assert.That(materialGateRolledBack.Registry.FigureAxes, Is.Empty);
                Assert.That(materialGateRolledBack.transform.Find("Intermediate/Tall"), Is.Null);
                Assert.That(materialGateRolledBack.transform.Find("Intermediate/GateMaster_Long"), Is.Null);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(materialGatePath).OfType<Material>().Any(asset => asset.name.StartsWith("Tall_", StringComparison.Ordinal)), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(materialGatePath).OfType<Texture>().Any(asset => asset.name.StartsWith("Tall_", StringComparison.Ordinal)), Is.False);
                Assert.That(opened.Registry.TryAdmitFigureAxes(opened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out string axesDiagnostic), Is.True, axesDiagnostic);
                ShapeSyncFigureAxisImportRequest[] requests =
                {
                    new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(axes[1], new[] { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission), new ShapeSyncAxisFigureSource("Tall", admission) })
                };
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[]
                {
                    new ShapeSyncFigureAxisImportRequest(new ShapeSyncDatabaseRegistry.FigureAxisAdmission("Forged", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, new object()), new[] { new ShapeSyncAxisFigureSource("Forged", null) })
                }, out string forgedAxisDiagnostic), Is.False);
                Assert.That(forgedAxisDiagnostic, Does.Contain("not issued"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase afterForged, out string afterForgedDiagnostic), Is.True, afterForgedDiagnostic);
                Assert.That(afterForged.Registry.FigureAxes, Is.Empty);

                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[]
                {
                    new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Wrong", admission) }),
                    new ShapeSyncFigureAxisImportRequest(axes[1], new[] { new ShapeSyncAxisFigureSource("Tall", admission) })
                }, out string fbmSelfKeyDiagnostic), Is.False);
                Assert.That(fbmSelfKeyDiagnostic, Does.Contain("own FBM name"));
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[]
                {
                    new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(axes[1], new[] { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission), new ShapeSyncAxisFigureSource("Unknown", admission) })
                }, out string unknownPbmKeyDiagnostic), Is.False);
                Assert.That(unknownPbmKeyDiagnostic, Does.Contain("every FBM"));
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[]
                {
                    new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(axes[1], new[]
                    {
                        new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission),
                        new ShapeSyncAxisFigureSource("Tall", admission),
                        new ShapeSyncAxisFigureSource("Tall", admission)
                    })
                }, out string duplicatePbmKeyDiagnostic), Is.False);
                Assert.That(duplicatePbmKeyDiagnostic, Does.Contain("unique admitted"));

                ShapeSyncFigureAxisImportRequest[] stagingFailureRequests =
                {
                    new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(axes[1], new[] { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission), new ShapeSyncAxisFigureSource("Tall", null) })
                };
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), stagingFailureRequests, out string stagingFailureDiagnostic), Is.False);
                Assert.That(stagingFailureDiagnostic, Does.Contain("unique admitted source"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase afterStagingFailure, out string afterStagingFailureDiagnostic), Is.True, afterStagingFailureDiagnostic);
                Assert.That(afterStagingFailure.Registry.FigureAxes, Is.Empty);
                Assert.That(afterStagingFailure.transform.Find("Intermediate/Tall"), Is.Null);

                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), requests, out string importDiagnostic), Is.True, importDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(reopened.Registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
                {
                    ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }));
                Assert.That(reopened.Registry.FigureAxes[0].Figures.Select(entry => entry.FbmName), Is.EqualTo(new[] { "Tall" }));
                Assert.That(reopened.Registry.FigureAxes[1].Figures.Select(entry => entry.FbmName), Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
                Assert.That(reopened.Registry.FigureAxes.SelectMany(axis => axis.Figures).All(entry => entry.Figure != null && AssetDatabase.GetAssetPath(entry.Figure) == AssetDatabase.GetAssetPath(database)), Is.True);
                Assert.That(reopened.transform.Find("Intermediate/Tall"), Is.Not.Null);
                Assert.That(reopened.transform.Find("Intermediate/Master_LongArms"), Is.Not.Null);
                Assert.That(reopened.transform.Find("Intermediate/Tall_LongArms"), Is.Not.Null);
                Assert.That(reopened.transform.Find("Intermediate/Tall_LongArms").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.blendShapeCount, Is.Zero);
                // PBM owns only its merged geometry. It reuses Figure Material entries,
                // so no PBM Material/Texture sub-assets enter the Database.
                SkinnedMeshRenderer pbmRenderer = reopened.transform.Find("Intermediate/Tall_LongArms").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(pbmRenderer.sharedMaterial, Is.SameAs(reopened.Registry.MaterialEntries.Single().Material));
                Assert.That(AssetDatabase.GetAssetPath(pbmRenderer.sharedMaterial), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Material>()
                    .Any(material => material.name.StartsWith("LongArms_", StringComparison.Ordinal)), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Texture>()
                    .Any(texture => texture.name.StartsWith("LongArms_", StringComparison.Ordinal)), Is.False);
                Assert.That(persistentRenderer.sharedMesh, Is.SameAs(persistentSourceMesh));
                Assert.That(persistentRenderer.sharedMaterial, Is.SameAs(persistentSourceMaterial));
                Assert.That(persistentRenderer.sharedMesh.blendShapeCount, Is.EqualTo(3));
                Func<GameObject, string, bool> originalBaseRenameSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(AssetDatabase.GetAssetPath(database), "Master", "MasterFailed", out string failedBaseRenameDiagnostic), Is.False);
                    Assert.That(failedBaseRenameDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalBaseRenameSavePrefab; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase baseRenameRolledBack, out string baseRenameRollbackDiagnostic), Is.True, baseRenameRollbackDiagnostic);
                Assert.That(baseRenameRolledBack.Registry.BaseFigures.Single().Name, Is.EqualTo("Master"));
                Assert.That(baseRenameRolledBack.transform.Find("Intermediate/Master_LongArms"), Is.Not.Null);
                Assert.That(baseRenameRolledBack.transform.Find("Intermediate/MasterFailed_LongArms"), Is.Null);
                Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(AssetDatabase.GetAssetPath(database), "Master", "MasterRenamed", out string renameBaseWithPbmDiagnostic), Is.True, renameBaseWithPbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase baseRenamedWithPbm, out string baseRenamedWithPbmOpenDiagnostic), Is.True, baseRenamedWithPbmOpenDiagnostic);
                Assert.That(baseRenamedWithPbm.Registry.BaseFigures.Single().Name, Is.EqualTo("MasterRenamed"));
                Assert.That(baseRenamedWithPbm.transform.Find("Intermediate/Master_LongArms"), Is.Null);
                Assert.That(baseRenamedWithPbm.transform.Find("Intermediate/MasterRenamed_LongArms"), Is.Not.Null);
                Assert.That(baseRenamedWithPbm.Registry.TryValidateFigureAxisState(baseRenamedWithPbm, out string baseRenamedWithPbmValidationDiagnostic), Is.True, baseRenamedWithPbmValidationDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(AssetDatabase.GetAssetPath(database), "MasterRenamed", "Master", out string restoreBaseWithPbmDiagnostic), Is.True, restoreBaseWithPbmDiagnostic);

                Assert.That(reopened.Registry.TryAdmitFigureAxes(reopened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }, out _, out string lateFbmAdmissionDiagnostic), Is.True, lateFbmAdmissionDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase lateFbmReopened, out string lateFbmOpenDiagnostic), Is.True, lateFbmOpenDiagnostic);
                Assert.That(lateFbmReopened.Registry.FigureAxes.Any(axis => axis.Name == "Wide"), Is.False);
                Assert.That(lateFbmReopened.transform.Find("Intermediate/Wide"), Is.Null);

                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase rollbackDatabase, out string rollbackCreateDiagnostic), Is.True, rollbackCreateDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(AssetDatabase.GetAssetPath(rollbackDatabase), admission, "RollbackMaster", out string rollbackBaseDiagnostic), Is.True, rollbackBaseDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(rollbackDatabase), out ShapeSyncDatabase rollbackOpened, out string rollbackOpenDiagnostic), Is.True, rollbackOpenDiagnostic);
                Assert.That(rollbackOpened.Registry.TryAdmitFigureAxes(rollbackOpened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("LongArms", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] incompleteAxes, out string incompleteAxesDiagnostic), Is.True, incompleteAxesDiagnostic);
                ShapeSyncFigureAxisImportRequest[] incompleteRequests =
                {
                    new ShapeSyncFigureAxisImportRequest(incompleteAxes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(incompleteAxes[1], new[] { new ShapeSyncAxisFigureSource("Short", admission) }),
                    new ShapeSyncFigureAxisImportRequest(incompleteAxes[2], new[] { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission), new ShapeSyncAxisFigureSource("Tall", admission) })
                };
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(rollbackDatabase), incompleteRequests, out string incompleteImportDiagnostic), Is.False);
                Assert.That(incompleteImportDiagnostic, Does.Contain("every FBM"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(rollbackDatabase), out ShapeSyncDatabase rolledBack, out string rolledBackOpenDiagnostic), Is.True, rolledBackOpenDiagnostic);
                Assert.That(rolledBack.Registry.FigureAxes, Is.Empty);
                Assert.That(rolledBack.transform.Find("Intermediate/Tall"), Is.Null);
                Assert.That(rolledBack.transform.Find("Intermediate/LongArms_Tall"), Is.Null);

                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase saveFailureDatabase, out string saveFailureCreateDiagnostic), Is.True, saveFailureCreateDiagnostic);
                string saveFailurePath = AssetDatabase.GetAssetPath(saveFailureDatabase);
                Assert.That(ShapeSyncFigureImport.TryImport(saveFailurePath, admission, "SaveFailureMaster", out string saveFailureBaseDiagnostic), Is.True, saveFailureBaseDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(saveFailurePath, out ShapeSyncDatabase saveFailureOpened, out string saveFailureOpenDiagnostic), Is.True, saveFailureOpenDiagnostic);
                SkinnedMeshRenderer saveFailureBaseRenderer = saveFailureOpened.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(saveFailureOpened, "Body", saveFailureBaseRenderer, 0, saveFailureBaseRenderer.sharedMaterial,
                    out ShapeSyncMaterialAdapterResolver.Admission saveFailureMaterialAdmission, out string saveFailureMaterialAdmissionDiagnostic), Is.True, saveFailureMaterialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(saveFailurePath, new[] { saveFailureMaterialAdmission }, true, out string saveFailureMaterialSaveDiagnostic), Is.True, saveFailureMaterialSaveDiagnostic); }
                finally { saveFailureMaterialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(saveFailurePath, out saveFailureOpened, out saveFailureOpenDiagnostic), Is.True, saveFailureOpenDiagnostic);
                Assert.That(saveFailureOpened.Registry.TryAdmitFigureAxes(saveFailureOpened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] saveFailureAxis, out string saveFailureAdmissionDiagnostic), Is.True, saveFailureAdmissionDiagnostic);
                Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureAxisImport.TryImport(saveFailurePath, new[]
                    {
                        new ShapeSyncFigureAxisImportRequest(saveFailureAxis[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) })
                    }, out string saveFailureImportDiagnostic), Is.False);
                    Assert.That(saveFailureImportDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(saveFailurePath, out ShapeSyncDatabase saveFailureRolledBack, out string saveFailureRollbackDiagnostic), Is.True, saveFailureRollbackDiagnostic);
                Assert.That(saveFailureRolledBack.Registry.FigureAxes, Is.Empty);
                Assert.That(saveFailureRolledBack.transform.Find("Intermediate/Tall"), Is.Null);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(saveFailurePath).Any(asset => asset != null && asset.name.StartsWith("Tall", StringComparison.Ordinal)), Is.False);

                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase multiDatabase, out string multiCreateDiagnostic), Is.True, multiCreateDiagnostic);
                string multiDatabasePath = AssetDatabase.GetAssetPath(multiDatabase);
                Assert.That(ShapeSyncFigureImport.TryImport(multiDatabasePath, admission, "MultiMaster", out string multiBaseDiagnostic), Is.True, multiBaseDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase multiOpened, out string multiOpenDiagnostic), Is.True, multiOpenDiagnostic);
                SkinnedMeshRenderer multiBaseMaterialRenderer = multiOpened.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(multiOpened, "Body", multiBaseMaterialRenderer, 0, multiBaseMaterialRenderer.sharedMaterial,
                    out ShapeSyncMaterialAdapterResolver.Admission multiMaterialAdmission, out string multiMaterialAdmissionDiagnostic), Is.True, multiMaterialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(multiDatabasePath, new[] { multiMaterialAdmission }, true, out string multiMaterialSaveDiagnostic), Is.True, multiMaterialSaveDiagnostic); }
                finally { multiMaterialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out multiOpened, out multiOpenDiagnostic), Is.True, multiOpenDiagnostic);
                Assert.That(multiOpened.Registry.TryAdmitFigureAxes(multiOpened, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] multiAxes, out string multiAxesDiagnostic), Is.True, multiAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(multiDatabasePath, new[]
                {
                    new ShapeSyncFigureAxisImportRequest(multiAxes[0], new[] { new ShapeSyncAxisFigureSource("Tall", admission) }),
                    new ShapeSyncFigureAxisImportRequest(multiAxes[1], new[] { new ShapeSyncAxisFigureSource("Short", admission) }),
                    new ShapeSyncFigureAxisImportRequest(multiAxes[2], new[]
                    {
                        new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission),
                        new ShapeSyncAxisFigureSource("Tall", admission),
                        new ShapeSyncAxisFigureSource("Short", admission)
                    })
                }, out string multiImportDiagnostic), Is.True, multiImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase multiReopened, out string multiReopenDiagnostic), Is.True, multiReopenDiagnostic);
                Assert.That(multiReopened.Registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
                {
                    ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }));
                Assert.That(multiReopened.Registry.FigureAxes[2].Figures.Select(binding => binding.FbmName), Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall", "Short" }));
                Assert.That(multiReopened.transform.Find("Intermediate/Tall"), Is.Not.Null);
                Assert.That(multiReopened.transform.Find("Intermediate/Short"), Is.Not.Null);
                Assert.That(multiReopened.transform.Find("Intermediate/MultiMaster_Long"), Is.Not.Null);
                Assert.That(multiReopened.transform.Find("Intermediate/Tall_Long"), Is.Not.Null);
                Assert.That(multiReopened.transform.Find("Intermediate/Short_Long"), Is.Not.Null);
                Assert.That(multiReopened.Registry.TryGetCommonFbmRawBlendShapeNames(multiReopened, out string[] rawCandidates, out string candidateDiagnostic), Is.True, candidateDiagnostic);
                Assert.That(rawCandidates, Is.EqualTo(new[] { "CommonRawShape", "PbmOnlySourceShape" }));
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(multiDatabasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TrySetPcmSlots(12, out string morphDiagnostic), Is.True, morphDiagnostic);
                    Assert.That(contents.Registry.TrySetKeptRawBlendShapeNames(contents, new[] { "CommonRawShape" }, out morphDiagnostic), Is.True, morphDiagnostic);
                }, out string morphSaveDiagnostic), Is.True, morphSaveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase morphReopened, out string morphOpenDiagnostic), Is.True, morphOpenDiagnostic);
                Assert.That(morphReopened.Registry.PcmSlots, Is.EqualTo(12));
                Assert.That(morphReopened.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "CommonRawShape" }));
                ShapeSyncDatabaseWindow morphWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
                try
                {
                    Assert.That(morphWindow.TrySetDatabase(morphReopened, out string morphBindDiagnostic), Is.True, morphBindDiagnostic);
                    morphWindow.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                    morphWindow.SetFigureMorphDraftForTest(13, new[] { "CommonRawShape" });
                    Assert.That(morphWindow.IsFigureDetailDirtyForTest, Is.True);
                    Assert.That(morphWindow.Database.Registry.FbmAxesFinalized, Is.True, "Binding a sealed FBM Registry must retain its finalized state.");
                    Assert.That(morphWindow.TrySaveFigure(out string pcmSaveDiagnostic), Is.True, pcmSaveDiagnostic);
                    Assert.That(morphWindow.Database.Registry.FbmAxesFinalized, Is.True, "Saving PCM Slots must retain the sealed FBM Registry state.");
                    Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase pcmSavedDatabase, out string pcmSavedDiagnostic), Is.True, pcmSavedDiagnostic);
                    Assert.That(pcmSavedDatabase.Registry.PcmSlots, Is.EqualTo(13));
                    Assert.That(pcmSavedDatabase.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "CommonRawShape" }));
                    morphWindow.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.ExtraMorphs);
                    morphWindow.SetFigureMorphDraftForTest(13, new[] { "PbmOnlySourceShape" });
                    Assert.That(morphWindow.IsExtraMorphsDetailDirtyForTest, Is.True);
                    Assert.That(morphWindow.TrySaveFigure(out string figureSaveDiagnostic), Is.True, figureSaveDiagnostic);
                    Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase beforeExtraMorphSave, out string beforeExtraMorphDiagnostic), Is.True, beforeExtraMorphDiagnostic);
                    Assert.That(beforeExtraMorphSave.Registry.PcmSlots, Is.EqualTo(13));
                    Assert.That(beforeExtraMorphSave.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "CommonRawShape" }));
                    morphWindow.SetFigureInputsForTest("MustNotBeImportedByExtraMorphSave", null);
                    Assert.That(morphWindow.Database.Registry.FbmAxesFinalized, Is.True, "Extra Morph save must keep the sealed FBM Registry state.");
                    Assert.That(morphWindow.TrySaveExtraMorphsForTest(out string extraMorphSaveDiagnostic), Is.True, extraMorphSaveDiagnostic);
                    Assert.That(morphWindow.IsExtraMorphsDetailDirtyForTest, Is.False);
                    Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase savedMorphWindowDatabase, out string savedMorphWindowDiagnostic), Is.True, savedMorphWindowDiagnostic);
                    Assert.That(savedMorphWindowDatabase.Registry.PcmSlots, Is.EqualTo(13));
                    Assert.That(savedMorphWindowDatabase.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "PbmOnlySourceShape" }));
                    Assert.That(savedMorphWindowDatabase.Registry.BaseFigures.Single().Name, Is.EqualTo("MultiMaster"));
                }
                finally { Object.DestroyImmediate(morphWindow); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase fbmRedefinitionSource, out string fbmRedefinitionOpenDiagnostic), Is.True, fbmRedefinitionOpenDiagnostic);
                Assert.That(fbmRedefinitionSource.Registry.TryAdmitFigureAxes(fbmRedefinitionSource, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] fbmRedefinitionAxis, out string fbmRedefinitionAdmissionDiagnostic), Is.True, fbmRedefinitionAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(multiDatabasePath, new[]
                {
                    new ShapeSyncFigureAxisImportRequest(fbmRedefinitionAxis[0], new[] { new ShapeSyncAxisFigureSource("Wide", admission) })
                }, out string fbmRedefinitionImportDiagnostic), Is.True, fbmRedefinitionImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase fbmRedefined, out string fbmRedefinitionReopenDiagnostic), Is.True, fbmRedefinitionReopenDiagnostic);
                Assert.That(fbmRedefined.Registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
                {
                    ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }), "Appending an FBM must discard every PBM axis before committing the new FBM set.");
                Assert.That(fbmRedefined.transform.Find("Intermediate/Long_Tall"), Is.Null);
                Assert.That(fbmRedefined.transform.Find("Intermediate/Long_Short"), Is.Null);
                Assert.That(fbmRedefined.Registry.KeptRawBlendShapeNames, Is.Empty, "Appending an FBM must discard the stale Extra Morph selection.");
                Assert.That(fbmRedefined.Registry.PcmSlots, Is.EqualTo(13), "PCM Slots are a Figure attribute and must not be reset by FBM redefinition.");
                Assert.That(fbmRedefined.Registry.TryAdmitFigureAxes(fbmRedefined, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] replacementPbmAxis, out string replacementPbmAdmissionDiagnostic), Is.True, replacementPbmAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(multiDatabasePath, new[]
                {
                    new ShapeSyncFigureAxisImportRequest(replacementPbmAxis[0], new[]
                    {
                        new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission),
                        new ShapeSyncAxisFigureSource("Tall", admission),
                        new ShapeSyncAxisFigureSource("Short", admission),
                        new ShapeSyncAxisFigureSource("Wide", admission)
                    })
                }, out string replacementPbmImportDiagnostic), Is.True, replacementPbmImportDiagnostic);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(multiDatabasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TrySetKeptRawBlendShapeNames(contents, new[] { "CommonRawShape" }, out string replacementKeepDiagnostic), Is.True, replacementKeepDiagnostic);
                }, out string replacementKeepSaveDiagnostic), Is.True, replacementKeepSaveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase beforeFbmReplacement, out string beforeFbmReplacementOpenDiagnostic), Is.True, beforeFbmReplacementOpenDiagnostic);
                SkinnedMeshRenderer oldTallRenderer = beforeFbmReplacement.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>();
                Material oldTallMaterial = oldTallRenderer.sharedMaterial;
                Assert.That(AssetDatabase.GetAssetPath(oldTallMaterial), Is.EqualTo(multiDatabasePath));
                Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(multiDatabasePath, "Tall", "Tall", true, admission, out string replaceFbmDiagnostic), Is.True, replaceFbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase replacedFbm, out string replacedFbmOpenDiagnostic), Is.True, replacedFbmOpenDiagnostic);
                Object[] replacementSubAssets = AssetDatabase.LoadAllAssetsAtPath(multiDatabasePath);
                Assert.That(replacementSubAssets.OfType<Material>().Count(material => material.name == "Tall_Body_Material"), Is.EqualTo(1), "Replacing a same-name FBM must retain exactly its newly-owned Material copy.");
                Assert.That(replacementSubAssets.OfType<Texture>().Count(texture => texture.name == "Tall_Body"), Is.EqualTo(1), "Replacing a same-name FBM must retain exactly its newly-owned Texture copy.");
                Assert.That(replacedFbm.Registry.FigureAxes.Select(axis => (axis.Name, axis.Kind)), Is.EqualTo(new[]
                {
                    ("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    ("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                }));
                Assert.That(replacedFbm.transform.Find("Intermediate/Long_Tall"), Is.Null);
                Assert.That(replacedFbm.Registry.KeptRawBlendShapeNames, Is.Empty);
                Assert.That(replacedFbm.Registry.PcmSlots, Is.EqualTo(13));
                Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(multiDatabasePath, "Tall", "Tall", false, admission, out string replaceFbmWithoutMaterialsDiagnostic), Is.True, replaceFbmWithoutMaterialsDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase replacedFbmWithoutMaterials, out string replacedFbmWithoutMaterialsOpenDiagnostic), Is.True, replacedFbmWithoutMaterialsOpenDiagnostic);
                SkinnedMeshRenderer figureMaterialTallRenderer = replacedFbmWithoutMaterials.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>();
                Material figureMaterial = replacedFbmWithoutMaterials.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material;
                Assert.That(figureMaterialTallRenderer.sharedMaterial, Is.SameAs(figureMaterial), "Replacing an FBM with Import All false must bind its renderer to the Figure Material.");
                Assert.That(replacedFbmWithoutMaterials.Registry.FigureAxes.Single(axis => axis.Name == "Tall").ImportAllMaterialsAndTextures, Is.False);
                Assert.That(replacedFbmWithoutMaterials.Registry.TextureResources.Any(entry => entry != null && entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure && entry.Owner.SourceShapeKey == "Tall"), Is.False, "Replacing an FBM with Import All false must remove its former FBM Texture Entries.");
                Object[] noMaterialImportReplacementSubAssets = AssetDatabase.LoadAllAssetsAtPath(multiDatabasePath);
                Assert.That(noMaterialImportReplacementSubAssets.OfType<Material>().Count(material => material.name == "Tall_Body_Material"), Is.Zero, "Replacing an FBM with Import All false must remove its former Material copy.");
                Assert.That(noMaterialImportReplacementSubAssets.OfType<Texture>().Count(texture => texture.name == "Tall_Body"), Is.Zero, "Replacing an FBM with Import All false must remove its former Texture copy.");
                Assert.That(replacedFbmWithoutMaterials.Registry.TryAdmitFigureAxes(replacedFbmWithoutMaterials, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("RenameLong", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] renamePbmAxis, out string renamePbmAdmissionDiagnostic), Is.True, renamePbmAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(multiDatabasePath, new[]
                {
                    new ShapeSyncFigureAxisImportRequest(renamePbmAxis[0], new[]
                    {
                        new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, admission),
                        new ShapeSyncAxisFigureSource("Tall", admission),
                        new ShapeSyncAxisFigureSource("Short", admission),
                        new ShapeSyncAxisFigureSource("Wide", admission)
                    })
                }, out string renamePbmImportDiagnostic), Is.True, renamePbmImportDiagnostic);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(multiDatabasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TrySetKeptRawBlendShapeNames(contents, new[] { "CommonRawShape" }, out string renameKeepDiagnostic), Is.True, renameKeepDiagnostic);
                }, out string renameKeepSaveDiagnostic), Is.True, renameKeepSaveDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(multiDatabasePath, "Tall", "TallRenamed", out string renameFbmDiagnostic), Is.True, renameFbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase renamedFbm, out string renamedFbmOpenDiagnostic), Is.True, renamedFbmOpenDiagnostic);
                Assert.That(renamedFbm.Registry.FigureAxes.Select(axis => axis.Name), Is.EqualTo(new[] { "TallRenamed", "Short", "Wide" }));
                Assert.That(renamedFbm.transform.Find("Intermediate/Tall"), Is.Null);
                Assert.That(renamedFbm.transform.Find("Intermediate/TallRenamed"), Is.Not.Null);
                Assert.That(renamedFbm.transform.Find("Intermediate/RenameLong_Tall"), Is.Null);
                Assert.That(renamedFbm.Registry.KeptRawBlendShapeNames, Is.Empty);
                Assert.That(renamedFbm.Registry.PcmSlots, Is.EqualTo(13));
                Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(multiDatabasePath, "TallRenamed", "Tall", out string restoreFbmNameDiagnostic), Is.True, restoreFbmNameDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(multiDatabasePath, "MultiMaster", "MultiMasterRenamed", out string renameBaseFigureDiagnostic), Is.True, renameBaseFigureDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase renamedBaseFigure, out string renamedBaseFigureOpenDiagnostic), Is.True, renamedBaseFigureOpenDiagnostic);
                Assert.That(renamedBaseFigure.Registry.BaseFigures.Single().Name, Is.EqualTo("MultiMasterRenamed"));
                Assert.That(renamedBaseFigure.transform.Find("Intermediate/MultiMaster"), Is.Null);
                Assert.That(renamedBaseFigure.transform.Find("Intermediate/MultiMasterRenamed"), Is.Not.Null);
                Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(multiDatabasePath, "MultiMasterRenamed", "MultiMaster", out string restoreBaseFigureNameDiagnostic), Is.True, restoreBaseFigureNameDiagnostic);
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(multiDatabasePath, (contents, _) =>
                {
                    Assert.That(contents.Registry.TrySetKeptRawBlendShapeNames(contents, new[] { "CommonRawShape" }, out string resetKeepDiagnostic), Is.True, resetKeepDiagnostic);
                }, out string resetKeepSaveDiagnostic), Is.True, resetKeepSaveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out morphReopened, out morphOpenDiagnostic), Is.True, morphOpenDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out ShapeSyncDatabase rejectionDatabase, out string rejectionOpenDiagnostic), Is.True, rejectionOpenDiagnostic);
                Assert.That(rejectionDatabase.Registry.TrySetKeptRawBlendShapeNames(rejectionDatabase, new[] { "NotACandidate" }, out string rejectedMorphDiagnostic), Is.False);
                Assert.That(rejectedMorphDiagnostic, Does.Contain("invalid"));
                SkinnedMeshRenderer baseRenderer = morphReopened.Registry.BaseFigures[0].Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Mesh originalBaseMesh = baseRenderer.sharedMesh;
                Mesh missingBaseMesh = Object.Instantiate(originalBaseMesh);
                baseRenderer.sharedMesh = missingBaseMesh;
                missingBaseMesh.ClearBlendShapes();
                Assert.That(morphReopened.Registry.TryGetCommonFbmRawBlendShapeNames(morphReopened, out string[] baseMissingCandidates, out string baseMissingDiagnostic), Is.True, baseMissingDiagnostic);
                Assert.That(baseMissingCandidates, Is.Empty);
                baseRenderer.sharedMesh = originalBaseMesh;
                Object.DestroyImmediate(missingBaseMesh);
                SkinnedMeshRenderer missingFbmRenderer = morphReopened.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>();
                missingFbmRenderer.sharedMesh.ClearBlendShapes();
                Assert.That(morphReopened.Registry.TryGetCommonFbmRawBlendShapeNames(morphReopened, out string[] fbmMissingCandidates, out string fbmMissingDiagnostic), Is.True, fbmMissingDiagnostic);
                Assert.That(fbmMissingCandidates, Is.Empty);
                EditorUtility.SetDirty(missingFbmRenderer.sharedMesh);
                AssetDatabase.SaveAssets();
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(multiDatabasePath, out _, out string staleKeepOpenDiagnostic), Is.False);
                Assert.That(staleKeepOpenDiagnostic, Does.Contain("keep selection"));

                string databasePath = AssetDatabase.GetAssetPath(database);
                GameObject corruptedContents = PrefabUtility.LoadPrefabContents(databasePath);
                try
                {
                    ShapeSyncFigureImportRecord record = corruptedContents.transform.Find("Intermediate/Tall").GetComponent<ShapeSyncFigureImportRecord>();
                    Assert.That(record, Is.Not.Null);
                    Object.DestroyImmediate(record);
                    Assert.That(PrefabUtility.SaveAsPrefabAsset(corruptedContents, databasePath), Is.Not.Null);
                }
                finally { PrefabUtility.UnloadPrefabContents(corruptedContents); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string corruptedOpenDiagnostic), Is.False);
                Assert.That(corruptedOpenDiagnostic, Does.Contain("imported merged Figure payload"));
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void ConfigureMergeRendererForFigureDetail(SkinnedMeshRenderer renderer, Transform bone)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix };
            mesh.RecalculateNormals();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        [Test]
        public void FigureAxisDetails_WindowFbmAndPbmDraftsUseTheOwnedAxisTransaction()
        {
            const string sourcePath = Root + "/WindowAxisSource.prefab";
            const string noMainTexSourcePath = Root + "/WindowAxisNoMainTexSource.prefab";
            const string meshOnlySourcePath = Root + "/WindowAxisMeshOnlySource.prefab";
            GameObject source = CreateHumanoidSourceForFigureDetail("WindowAxisSource", out Avatar avatar);
            GameObject noMainTexSource = null;
            GameObject meshOnlySource = null;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRendererForFigureDetail(renderer, source.transform.Find("Hips"));
                Material axisMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "WindowAxisSourceMaterial" };
                Texture2D axisTexture = new Texture2D(1, 1) { name = "AxisTexture" };
                Texture2D axisNormalTexture = new Texture2D(1, 1) { name = "AxisNormalTexture" };
                axisMaterial.SetTexture("_BaseMap", axisTexture);
                axisMaterial.SetTexture("_EmissionMap", axisTexture);
                axisMaterial.SetTexture("_BumpMap", axisNormalTexture);
                renderer.sharedMaterial = axisMaterial;
                AssetDatabase.CreateAsset(avatar, Root + "/WindowAxisAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/WindowAxisMesh.asset");
                AssetDatabase.CreateAsset(axisTexture, Root + "/WindowAxisTexture.asset");
                AssetDatabase.CreateAsset(axisNormalTexture, Root + "/WindowAxisNormalTexture.asset");
                AssetDatabase.CreateAsset(axisMaterial, Root + "/WindowAxisMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                GameObject persistent = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                meshOnlySource = UnityEngine.Object.Instantiate(source);
                UnityEngine.Object.DestroyImmediate(meshOnlySource.GetComponent<Animator>());
                Texture2D fbmModelNormalTexture = new Texture2D(1, 1) { name = "FbmModelNormalTexture" };
                Material fbmModelMaterial = new Material(axisMaterial) { name = "FbmModelMaterial" };
                fbmModelMaterial.SetTexture("_BumpMap", fbmModelNormalTexture);
                meshOnlySource.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial = fbmModelMaterial;
                AssetDatabase.CreateAsset(fbmModelNormalTexture, Root + "/FbmModelNormalTexture.asset");
                AssetDatabase.CreateAsset(fbmModelMaterial, Root + "/FbmModelMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(meshOnlySource, meshOnlySourcePath), Is.Not.Null);
                GameObject persistentMeshOnly = AssetDatabase.LoadAssetAtPath<GameObject>(meshOnlySourcePath);
                Assert.That(persistentMeshOnly.GetComponent<Animator>(), Is.Null);
                noMainTexSource = UnityEngine.Object.Instantiate(source);
                noMainTexSource.name = "WindowAxisNoMainTexSource";
                Material noMainTexMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                noMainTexSource.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().sharedMaterial = noMainTexMaterial;
                AssetDatabase.CreateAsset(noMainTexMaterial, Root + "/WindowAxisNoMainTexMaterial.mat");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(noMainTexSource, noMainTexSourcePath), Is.Not.Null);
                GameObject persistentNoMainTex = AssetDatabase.LoadAssetAtPath<GameObject>(noMainTexSourcePath);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistent, out ShapeSyncFigureImportAdmission baseAdmission, out string baseAdmissionDiagnostic), Is.True, baseAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(AssetDatabase.GetAssetPath(database), baseAdmission, "Master", out string baseImportDiagnostic), Is.True, baseImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                SkinnedMeshRenderer baseRenderer = opened.transform.Find("Intermediate/Master").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseRenderer, 0, baseRenderer.sharedMaterials[0], out ShapeSyncMaterialAdapterResolver.Admission materialAdmission, out string materialAdmissionDiagnostic), Is.True, materialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(AssetDatabase.GetAssetPath(database), new[] { materialAdmission }, out string materialSaveDiagnostic), Is.True, materialSaveDiagnostic); }
                finally { materialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out opened, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                window.SetFigureInputsForTest("MasterRenamed", null);
                Assert.That(window.TrySaveFigure(out string renameBaseFromWindowDiagnostic), Is.True, renameBaseFromWindowDiagnostic);
                Assert.That(window.Database.Registry.BaseFigures.Single().Name, Is.EqualTo("MasterRenamed"));
                window.SetFigureInputsForTest("Master", null);
                Assert.That(window.TrySaveFigure(out string restoreBaseFromWindowDiagnostic), Is.True, restoreBaseFromWindowDiagnostic);
                Texture2D baseNormalSource = new Texture2D(1, 1) { name = "BaseNormal" };
                AssetDatabase.CreateAsset(baseNormalSource, Root + "/BaseNormal.asset");
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                Assert.That(window.TryAddFigureNormalEntryForTest(), Is.True);
                Assert.That(window.TrySetNormalDraftForTest("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, baseNormalSource), Is.True);
                Assert.That(window.TrySaveNormalsForTest(out string baseNormalDiagnostic), Is.True, baseNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase baseNormalSaved, out string baseNormalOpenDiagnostic), Is.True, baseNormalOpenDiagnostic);
                Texture savedBaseNormal = baseNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Texture;
                Assert.That(savedBaseNormal, Is.Not.SameAs(baseNormalSource), "An external Figure Normal must be registered as a Database Texture Entry copy.");
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(savedBaseNormal, out string baseNormalGuid, out long baseNormalLocalId), Is.True);
                window.SetFbmAxisDraftsForTest(new[] { "DiscardA", "DiscardB" }, new[] { persistent, persistent }, new[] { false, false });
                Assert.That(window.FbmAxisDraftCountForTest, Is.EqualTo(2));
                Assert.That(window.TryRemoveFbmAxisDraftForTest(1), Is.True, "Every newly added FBM Draft row must expose a Remove action.");
                Assert.That(window.FbmAxisDraftCountForTest, Is.EqualTo(1));
                Assert.That(window.TryRemoveFbmAxisDraftForTest(0), Is.True, "The final Draft row must also be removable.");
                Assert.That(window.FbmAxisDraftCountForTest, Is.Zero);
                Assert.That(ShapeSyncDatabaseWindow.PbmAddButtonLabel, Is.EqualTo("Add PBM Entry"));
                Assert.That(ShapeSyncDatabaseWindow.PbmSaveButtonLabel, Is.EqualTo("Save to Database"));
                ShapeSyncDatabaseWindow.PbmDetailLayout pbmLayout = ShapeSyncDatabaseWindow.GetPbmDetailLayoutForTest();
                Assert.That(pbmLayout.CentralScrollViewCount, Is.EqualTo(1));
                Assert.That(pbmLayout.AddActionIsAboveCentralScroll, Is.True);
                Assert.That(pbmLayout.SaveActionIsBelowCentralScroll, Is.True);
                Assert.That(pbmLayout.ShowsPbmPrefabsHeadingAfterName, Is.True);
                Assert.That(pbmLayout.HasFigureNamedFirstPrefabRow, Is.True);
                Assert.That(pbmLayout.UsesUnlabeledWidePrefabFields, Is.True);
                Assert.That(pbmLayout.HidesBaseInternalTerm, Is.True);
                window.SetFbmAxisDraftsForTest(new[] { "Tall", "NoMainTex", "Disabled" }, new[] { persistent, persistentNoMainTex, persistent }, new[] { true, true, false });
                Func<GameObject, string, bool> originalFbmSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(window.TrySaveFbmAxisDraftsForTest(out string failedFbmDiagnostic), Is.False);
                    Assert.That(failedFbmDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalFbmSavePrefab; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase failedFbmSave, out string failedFbmOpenDiagnostic), Is.True, failedFbmOpenDiagnostic);
                Assert.That(failedFbmSave.Registry.FigureAxes.Any(axis => axis.Name == "Tall"), Is.False);
                Assert.That(failedFbmSave.Registry.TextureResources.Any(entry => entry.LogicalName == "Tall_Body" || entry.LogicalName == "Tall_Body_2"), Is.False, "A failed FBM Import All must roll back both MainTex and Normal Texture Resources.");
                Object[] failedFbmSubAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database));
                Assert.That(failedFbmSubAssets.OfType<Material>().Any(material => material.name == "Tall_Body_Material"), Is.False);
                Assert.That(failedFbmSubAssets.OfType<Texture>().Any(texture => texture.name == "Tall_Body" || texture.name == "Tall_Body_2"), Is.False, "A failed FBM Import All must leave no final MainTex or Normal Texture sub-assets.");
                Assert.That(window.TrySetDatabase(failedFbmSave, out string failedFbmBindDiagnostic), Is.True, failedFbmBindDiagnostic);
                window.SetFbmAxisDraftsForTest(new[] { "Tall", "NoMainTex", "Disabled" }, new[] { persistent, persistentNoMainTex, persistent }, new[] { true, true, false });
                Assert.That(window.TrySaveFbmAxisDraftsForTest(out string fbmDiagnostic), Is.True, fbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fbmSaved, out string fbmOpenDiagnostic), Is.True, fbmOpenDiagnostic);
                Assert.That(fbmSaved.Registry.FigureAxes.Single(axis => axis.Name == "Tall").ImportAllMaterialsAndTextures, Is.True);
                Assert.That(fbmSaved.Registry.FigureAxes.Single(axis => axis.Name == "Disabled").ImportAllMaterialsAndTextures, Is.False);
                Assert.That(fbmSaved.Registry.TextureResources.Any(entry => entry.LogicalName == "Tall_Body"), Is.True);
                Assert.That(fbmSaved.Registry.TextureResources.Where(entry => entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure && entry.Owner.SourceShapeKey == "Tall").Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Tall_Body", "Tall_Body_2" }), "FBM Import All must register MainTex first, then its Normal/other Textures with the Figure-equivalent suffix rule.");
                Assert.That(fbmSaved.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").TextureResourceNames.Where(name => name.StartsWith("Tall_", StringComparison.Ordinal)), Is.EqualTo(new[] { "Tall_Body", "Tall_Body_2" }));
                Texture tallTexture = fbmSaved.Registry.TextureResources.Single(entry => entry.LogicalName == "Tall_Body").Texture;
                Texture tallNormalTexture = fbmSaved.Registry.TextureResources.Single(entry => entry.LogicalName == "Tall_Body_2").Texture;
                Material tallMaterial = fbmSaved.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                Assert.That(tallMaterial.name, Is.EqualTo("Tall_Body_Material"));
                Assert.That(tallMaterial.mainTexture, Is.SameAs(tallTexture));
                Assert.That(tallMaterial.GetTexture("_EmissionMap"), Is.SameAs(tallTexture), "Every shader property aliasing the imported MainTex must be rebound to the final Entry Texture.");
                Assert.That(tallMaterial.GetTexture("_BumpMap"), Is.SameAs(tallNormalTexture), "FBM Import All must own and register Normal/other Textures after its MainTex.");
                Object[] fbmSubAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database));
                Assert.That(fbmSubAssets.OfType<Material>().Any(material => material.name == "Tall_WindowAxisSourceMaterial"), Is.False, "FBM Import All must replace the provisional source-named Material with its Entry-named Material.");
                Assert.That(fbmSubAssets.OfType<Texture>().Any(texture => texture.name == "Tall_AxisTexture"), Is.False, "FBM Import All must replace the provisional source-named MainTex with its Entry-named Texture Resource.");
                Assert.That(fbmSubAssets.OfType<Texture>().Any(texture => texture.name == "Tall_AxisNormalTexture"), Is.False, "FBM Import All must also replace the provisional source-named Normal Texture.");
                Assert.That(fbmSaved.Registry.TextureResources.Any(entry => entry.LogicalName == "NoMainTex_Body"), Is.False, "An FBM Material without MainTex must not create a Texture Entry.");
                Assert.That(fbmSaved.Registry.TextureResources.Any(entry => entry.LogicalName == "Disabled_Body"), Is.False, "Import All false must not create a Texture Entry even when MainTex exists.");
                Assert.That(fbmSaved.Registry.TextureResources.Where(entry => entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure && entry.Owner.SourceShapeKey == "Disabled").Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Disabled_Body_Normal" }), "Import All false imports only the declared FBM Normal and names it as FBM_Entry_Normal.");
                Material figureBodyMaterial = fbmSaved.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material;
                Material disabledMaterial = fbmSaved.transform.Find("Intermediate/Disabled").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
                Assert.That(disabledMaterial, Is.SameAs(figureBodyMaterial), "Import All false must bind the FBM renderer to the Figure Material.");
                Assert.That(fbmSubAssets.OfType<Material>().Any(material => material.name.StartsWith("Disabled_", StringComparison.Ordinal)), Is.False, "Import All false must not retain a cloned FBM Material sub-asset.");
                Assert.That(fbmSubAssets.OfType<Texture>().Any(texture => texture.name.StartsWith("Disabled_", StringComparison.Ordinal) && texture.name != "Disabled_Body_Normal"), Is.False, "Import All false must not retain a cloned FBM Texture sub-asset other than its declared Normal.");
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(AssetDatabase.GetAssetPath(database), (contents, _) =>
                {
                    Assert.That(contents.Registry.TryRenameTextureResource("Tall_Body", "ManualTallBody", out string manualTextureRenameDiagnostic), Is.True, manualTextureRenameDiagnostic);
                }, out string manualTextureRenameSaveDiagnostic), Is.True, manualTextureRenameSaveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase manualTextureRenamed, out string manualTextureOpenDiagnostic), Is.True, manualTextureOpenDiagnostic);
                Assert.That(window.TrySetDatabase(manualTextureRenamed, out string manualTextureBindDiagnostic), Is.True, manualTextureBindDiagnostic);
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "TallRenamed", null, true), Is.True);
                Assert.That(window.TrySaveFbmAxisDraftsForTest(out string renameFbmFromWindowDiagnostic), Is.True, renameFbmFromWindowDiagnostic);
                Assert.That(window.Database.Registry.FigureAxes.Any(axis => axis.Name == "TallRenamed"), Is.True);
                ShapeSyncDatabaseRegistry.TextureResourceEntry manualTextureAfterFbmRename = window.Database.Registry.TextureResources.Single(entry => entry.LogicalName == "ManualTallBody");
                Assert.That(manualTextureAfterFbmRename.Owner.SourceShapeKey, Is.EqualTo("TallRenamed"));
                Assert.That(manualTextureAfterFbmRename.Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure));
                Assert.That(window.Database.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").TextureResourceNames, Does.Contain("ManualTallBody"));
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("TallRenamed", "Tall", null, true), Is.True);
                Assert.That(window.TrySaveFbmAxisDraftsForTest(out string restoreFbmFromWindowDiagnostic), Is.True, restoreFbmFromWindowDiagnostic);
                Assert.That(window.NormalDraftCountForTest, Is.EqualTo(1), "Registering FBMs must not generate Normal drafts before their selected Figure Normal Entry is edited.");
                Texture2D fbmNormal = new Texture2D(1, 1) { name = "FbmNormal" };
                AssetDatabase.CreateAsset(fbmNormal, Root + "/WindowFbmNormal.asset");
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.TryPickFbmNormalFromModelForTest("Body", "Tall"), Is.True);
                Assert.That(window.GetNormalDraftTextureForTest("Body", "Tall").name, Is.EqualTo("TallRenamedBody_2"), "FBM Pick From Model must resolve the saved FBM Material slot, never the Base Figure Material.");
                Assert.That(window.GetNormalDraftTextureForTest("Body", "Tall"), Is.Not.SameAs(axisNormalTexture));
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "Tall", persistentMeshOnly, true), Is.True);
                Assert.That(window.TryPickFbmNormalFromModelForTest("Body", "Tall"), Is.True);
                Assert.That(AssetDatabase.GetAssetPath(window.GetNormalDraftTextureForTest("Body", "Tall")), Is.EqualTo(AssetDatabase.GetAssetPath(fbmModelNormalTexture)), "A selected FBM Prefab must be merged through the axis pipeline and take precedence over the saved FBM Figure.");
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", fbmNormal), Is.True);
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "Tall", persistentNoMainTex, true), Is.True);
                Assert.That(window.TryPickFbmNormalFromModelForTest("Body", "Tall"), Is.False, "An FBM Model without a Normal must not resolve a Base Figure fallback.");
                Assert.That(window.GetNormalDraftTextureForTest("Body", "Tall"), Is.SameAs(fbmNormal), "An unresolved FBM Pick must preserve the current Normal draft.");
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "Tall", null, true), Is.True);
                ShapeSyncDatabaseWindow.FbmDetailLayout preSealLayout = ShapeSyncDatabaseWindow.GetFbmDetailLayoutForTest(false);
                ShapeSyncDatabaseWindow.FbmDetailLayout postSealLayout = ShapeSyncDatabaseWindow.GetFbmDetailLayoutForTest(true);
                Assert.That(ShapeSyncDatabaseWindow.FbmAddButtonLabel, Is.EqualTo("Add FBM Entry"));
                Assert.That(ShapeSyncDatabaseWindow.FbmSaveButtonLabel, Is.EqualTo("Save to Database"));
                Assert.That(preSealLayout.ShowsAddFbmEntry, Is.True, "Only the pre-registration FBM Detail may render the upper Add FBM Entry action.");
                Assert.That(postSealLayout.ShowsAddFbmEntry, Is.True, "A sealed FBM Detail must keep the upper Add FBM Entry action so that FBMs can be redefined.");
                Assert.That(preSealLayout.FooterActionCount, Is.EqualTo(1));
                Assert.That(postSealLayout.FooterActionCount, Is.EqualTo(1), "Registration state must not create a second Save to Database footer.");
                Assert.That(preSealLayout.FooterActionLabel, Is.EqualTo("Save to Database"));
                Assert.That(postSealLayout.FooterActionLabel, Is.EqualTo("Save to Database"));
                Assert.That(postSealLayout.ShowsEntryNameForEachNormal, Is.True, "Each FBM Normal row must identify its Material Entry before the Normal field and preview.");
                Assert.That(window.IsFbmSaveEnabledForTest, Is.True, "An FBM Normal edit belongs to FBM Detail and enables its Save to Database action.");
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", fbmNormal), Is.True);
                Assert.That(window.IsNormalsDetailDirtyForTest, Is.False, "Normal Detail owns only Figure Normal Entries and Base Normals.");
                Assert.That(window.IsFbmSaveEnabledForTest, Is.True, "FBM Detail owns the pending FBM Normal edit.");
                Assert.That(window.TrySaveFbmNormalsForTest(out string fbmNormalDiagnostic), Is.True, fbmNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fbmNormalSaved, out string fbmNormalOpenDiagnostic), Is.True, fbmNormalOpenDiagnostic);
                ShapeSyncDatabaseRegistry.NormalEntry savedFbmNormal = fbmNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == "Tall");
                Assert.That(savedFbmNormal.Texture, Is.Not.SameAs(fbmNormal), "An external FBM Normal must be registered as a Database Texture Entry copy.");
                Assert.That(savedFbmNormal.TextureResourceName, Is.EqualTo("Tall_Body_Normal"));
                Assert.That(AssetDatabase.GetAssetPath(savedFbmNormal.Texture), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(fbmNormalSaved.Registry.TextureResources.Single(resource => resource.LogicalName == savedFbmNormal.TextureResourceName).Texture, Is.SameAs(savedFbmNormal.Texture));
                Assert.That(fbmNormalSaved.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").TextureResourceNames, Does.Contain(savedFbmNormal.TextureResourceName));
                Texture persistedBaseNormal = fbmNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Texture;
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(persistedBaseNormal, out string persistedBaseNormalGuid, out long persistedBaseNormalLocalId), Is.True);
                Assert.That(persistedBaseNormalGuid, Is.EqualTo(baseNormalGuid));
                Assert.That(persistedBaseNormalLocalId, Is.EqualTo(baseNormalLocalId));
                Texture2D replacementFbmNormal = new Texture2D(1, 1) { name = "ReplacementFbmNormal" };
                AssetDatabase.CreateAsset(replacementFbmNormal, Root + "/ReplacementFbmNormal.asset");
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", replacementFbmNormal), Is.True);
                Assert.That(window.TrySaveFbmNormalsForTest(out string replaceNormalDiagnostic), Is.True, replaceNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase replacedFbmNormalSaved, out string replacedFbmNormalOpenDiagnostic), Is.True, replacedFbmNormalOpenDiagnostic);
                ShapeSyncDatabaseRegistry.NormalEntry replacedFbmNormal = replacedFbmNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == "Tall");
                Assert.That(replacedFbmNormal.Texture, Is.Not.SameAs(replacementFbmNormal));
                Assert.That(replacedFbmNormal.Texture, Is.Not.SameAs(savedFbmNormal.Texture));
                Assert.That(AssetDatabase.GetAssetPath(replacedFbmNormal.Texture), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(window.TrySetDatabase(replacedFbmNormalSaved, out string resetFbmNormalDraftsDiagnostic), Is.True, resetFbmNormalDraftsDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Texture2D ignoredFbmNormal = new Texture2D(1, 1) { name = "IgnoredFbmNormal" };
                AssetDatabase.CreateAsset(ignoredFbmNormal, Root + "/IgnoredFbmNormal.asset");
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", ignoredFbmNormal), Is.True);
                Func<string, string, string, string, string, int> originalDirtyDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
                try
                {
                    ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;
                    Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
                }
                finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDirtyDialog; }
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.Fbms), Is.True);
                Assert.That(window.GetNormalDraftTextureForTest("Body", "Tall"), Is.SameAs(replacedFbmNormal.Texture));
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", ignoredFbmNormal), Is.True);
                try
                {
                    ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                    Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                }
                finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDirtyDialog; }
                Assert.That(window.SelectedSectionForTest, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Fbms));
                Assert.That(window.GetNormalDraftTextureForTest("Body", "Tall"), Is.SameAs(ignoredFbmNormal));
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", null), Is.True);
                Assert.That(window.TrySaveFbmNormalsForTest(out string clearNormalDiagnostic), Is.False);
                Assert.That(clearNormalDiagnostic, Does.Contain("FBM Normal cannot be None"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase clearedFbmNormalSaved, out string clearedFbmNormalOpenDiagnostic), Is.True, clearedFbmNormalOpenDiagnostic);
                Assert.That(clearedFbmNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == "Tall").Texture, Is.SameAs(replacedFbmNormal.Texture), "A rejected None edit must not mutate the saved FBM Normal.");
                Assert.That(window.TrySetNormalDraftForTest("Body", "Tall", fbmNormal), Is.True);
                Assert.That(window.TrySaveFbmNormalsForTest(out string restoreNormalDiagnostic), Is.True, restoreNormalDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Materials);
                Assert.That(window.TrySetMaterialDraftNameForTest(0, "BodyRenamed"), Is.True);
                Assert.That(window.TrySaveMaterialEntriesForTest(out string renameAndNormalDiagnostic), Is.True, renameAndNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase renamedAndNormalSaved, out string renamedAndNormalOpenDiagnostic), Is.True, renamedAndNormalOpenDiagnostic);
                Assert.That(renamedAndNormalSaved.Registry.MaterialEntries.Single().LogicalName, Is.EqualTo("BodyRenamed"));
                Assert.That(renamedAndNormalSaved.Registry.NormalEntries.Single(entry => entry.ShapeKey == "Tall").MaterialEntryName, Is.EqualTo("BodyRenamed"));
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                Assert.That(window.FigureNormalEntryMaterialNamesForTest, Is.EqualTo(new[] { "BodyRenamed" }));
                Assert.That(window.TryRemoveFigureNormalEntryForTest(0), Is.True);
                Assert.That(window.TrySaveNormalsForTest(out string removeNormalRelationDiagnostic), Is.True, removeNormalRelationDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase normalRelationRemoved, out string normalRelationRemovedDiagnostic), Is.True, normalRelationRemovedDiagnostic);
                Assert.That(normalRelationRemoved.Registry.NormalEntries, Is.Empty, "Removing a Figure Normal Entry must clear Base and every FBM relation owned by that Entry.");
                window.SetPbmAxisDraftForTest("DiscardPbm", persistent, new[] { "Tall", "NoMainTex", "Disabled" }, new[] { persistent, persistentNoMainTex, persistent });
                Assert.That(window.PbmAxisDraftCountForTest, Is.EqualTo(1));
                Assert.That(window.TryRemovePbmAxisDraftForTest(0), Is.True, "Every newly added PBM Draft row must expose a Remove action.");
                Assert.That(window.PbmAxisDraftCountForTest, Is.Zero);
                window.SetPbmAxisDraftForTest("Long", persistent, new[] { "Tall", "NoMainTex", "Disabled" }, new GameObject[] { null, persistentNoMainTex, persistent });
                Assert.That(window.TrySavePbmAxisDraftsForTest(out string incompleteDiagnostic), Is.False);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase incomplete, out string incompleteOpenDiagnostic), Is.True, incompleteOpenDiagnostic);
                Assert.That(incomplete.Registry.FigureAxes.Count, Is.EqualTo(3));
                window.SetPbmAxisDraftForTest("Long", persistentMeshOnly, new[] { "Tall", "NoMainTex", "Disabled" }, new[] { persistentMeshOnly, persistentNoMainTex, persistentMeshOnly });
                Assert.That(window.TrySavePbmAxisDraftsForTest(out string pbmDiagnostic), Is.True, pbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string reopenDiagnostic), Is.True, reopenDiagnostic);
                Assert.That(reopened.Registry.FigureAxes.Select(axis => axis.Name), Is.EqualTo(new[] { "Tall", "NoMainTex", "Disabled", "Long" }));
                Assert.That(reopened.Registry.FigureAxes.Last().Figures.Select(item => item.FbmName), Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall", "NoMainTex", "Disabled" }));
                Assert.That(reopened.Registry.FigureAxes.Last().Figures.First(item => item.FbmName == ShapeSyncDatabaseRegistry.BaseShapeKey).Figure.name, Is.EqualTo("Master_Long"));
                Assert.That(reopened.Registry.FigureAxes.Last().Figures.First(item => item.FbmName == "Tall").Figure.name, Is.EqualTo("Tall_Long"));
                Assert.That(reopened.Registry.FigureAxes.Last().Figures.All(item => item.Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial
                    == reopened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "BodyRenamed").Material), Is.True);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Normals);
                Assert.That(window.TryAddFigureNormalEntryForTest(), Is.True);
                Assert.That(window.TrySetNormalDraftForTest("BodyRenamed", ShapeSyncDatabaseRegistry.BaseShapeKey, baseNormalSource), Is.True);
                Assert.That(window.TrySaveNormalsForTest(out string restoreNormalRelationDiagnostic), Is.True, restoreNormalRelationDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.TrySetNormalDraftForTest("BodyRenamed", "Tall", fbmNormal), Is.True);
                Assert.That(window.TrySaveFbmNormalsForTest(out string saveNormalBeforeRenameDiagnostic), Is.True, saveNormalBeforeRenameDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase beforeRenameWithPendingNormal, out string beforeRenameWithPendingNormalDiagnostic), Is.True, beforeRenameWithPendingNormalDiagnostic);
                Texture savedTallNormalBeforeRename = beforeRenameWithPendingNormal.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "BodyRenamed" && entry.ShapeKey == "Tall").Texture;
                Texture2D pendingFbmNormal = new Texture2D(1, 1) { name = "PendingFbmNormal" };
                AssetDatabase.CreateAsset(pendingFbmNormal, Root + "/PendingFbmNormal.asset");
                Assert.That(window.TrySetNormalDraftForTest("BodyRenamed", "Tall", pendingFbmNormal), Is.True);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "TallRenamed", null, true), Is.True);
                Assert.That(window.TrySaveFbmAxisDraftsForTest(out string renameWithPbmAndNormalDiagnostic), Is.True, renameWithPbmAndNormalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase renamedWithPbmAndNormal, out string renamedWithPbmAndNormalOpenDiagnostic), Is.True, renamedWithPbmAndNormalOpenDiagnostic);
                Assert.That(renamedWithPbmAndNormal.Registry.FigureAxes.Select(axis => axis.Name), Is.EqualTo(new[] { "TallRenamed", "NoMainTex", "Disabled" }));
                Assert.That(renamedWithPbmAndNormal.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "BodyRenamed" && entry.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey).ShapeKey, Is.EqualTo("TallRenamed"));
                ShapeSyncDatabaseRegistry.NormalEntry renamedTallNormalEntry = renamedWithPbmAndNormal.Registry.NormalEntries.Single(entry => entry.MaterialEntryName == "BodyRenamed" && entry.ShapeKey == "TallRenamed");
                Texture renamedTallNormal = renamedTallNormalEntry.Texture;
                Assert.That(renamedWithPbmAndNormal.Registry.TextureResources.Single(resource => resource.Texture == renamedTallNormal).LogicalName,
                    Is.EqualTo(renamedTallNormalEntry.TextureResourceName), "FBM rename must propagate a renamed FBM-owned Normal Texture Resource name to its Normal relation.");
                Assert.That(renamedTallNormal, Is.Not.SameAs(pendingFbmNormal), "An external FBM Normal is copied into the Database before the FBM redefinition transaction.");
                Assert.That(renamedTallNormal.name, Is.Not.EqualTo(savedTallNormalBeforeRename.name), "FBM Detail Save commits its own pending Normal before re-keying the FBM relation.");
                Assert.That(renamedWithPbmAndNormal.transform.Find("Intermediate/Tall_Long"), Is.Null);
                Assert.That(renamedWithPbmAndNormal.transform.Find("Intermediate/NoMainTex_Long"), Is.Null);
                Assert.That(renamedWithPbmAndNormal.transform.Find("Intermediate/Disabled_Long"), Is.Null);
                Assert.That(renamedWithPbmAndNormal.Registry.KeptRawBlendShapeNames, Is.Empty);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Pbms);
                window.SetPbmAxisDraftForTest("RemoveMe", persistent, new[] { "TallRenamed", "NoMainTex", "Disabled" }, new[] { persistent, persistentNoMainTex, persistent });
                Assert.That(window.TrySavePbmAxisDraftsForTest(out string recreatePbmDiagnostic), Is.True, recreatePbmDiagnostic);
                Assert.That(window.SetPbmAxisRedefinitionDraftForTest("RemoveMe", "RenamedPbm", persistent,
                    new[] { "TallRenamed", "NoMainTex", "Disabled" }, new GameObject[] { persistent, null, null }), Is.True,
                    "Unspecified PBM rows must be rebuilt from their existing Database Prefabs.");
                Assert.That(window.TrySavePbmAxisDraftsForTest(out string renamePbmDiagnostic), Is.True, renamePbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase renamedPbm, out string renamedPbmOpenDiagnostic), Is.True, renamedPbmOpenDiagnostic);
                Assert.That(renamedPbm.Registry.FigureAxes.Any(axis => axis.Name == "RenamedPbm" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True);
                Assert.That(renamedPbm.transform.Find("Intermediate/Master_RenamedPbm"), Is.Not.Null);
                Assert.That(renamedPbm.transform.Find("Intermediate/TallRenamed_RenamedPbm"), Is.Not.Null);
                Assert.That(window.TryRemovePbmAxisForTest("RenamedPbm", out string removePbmDiagnostic), Is.True, removePbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase removedPbm, out string removedPbmOpenDiagnostic), Is.True, removedPbmOpenDiagnostic);
                Assert.That(removedPbm.Registry.FigureAxes.Any(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.False);
                Assert.That(removedPbm.transform.Find("Intermediate/Master_RenamedPbm"), Is.Null);
                Assert.That(removedPbm.transform.Find("Intermediate/TallRenamed_RenamedPbm"), Is.Null);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(meshOnlySource); Object.DestroyImmediate(noMainTexSource); Object.DestroyImmediate(source); }
        }

        private static GameObject CreateHumanoidSourceForFigureDetail(string name, out Avatar avatar)
        {
            GameObject root = new GameObject(name);
            Animator animator = root.AddComponent<Animator>();
            List<Transform> bones = new List<Transform>();
            Transform hips = AddFigureDetailBone(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddFigureDetailBone(hips, "Spine", Vector3.up * .15f, bones); Transform chest = AddFigureDetailBone(spine, "Chest", Vector3.up * .15f, bones); Transform neck = AddFigureDetailBone(chest, "Neck", Vector3.up * .15f, bones); AddFigureDetailBone(neck, "Head", Vector3.up * .12f, bones);
            Transform leftUpperArm = AddFigureDetailBone(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones); Transform leftLowerArm = AddFigureDetailBone(leftUpperArm, "LeftLowerArm", Vector3.left * .2f, bones); AddFigureDetailBone(leftLowerArm, "LeftHand", Vector3.left * .18f, bones);
            Transform rightUpperArm = AddFigureDetailBone(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones); Transform rightLowerArm = AddFigureDetailBone(rightUpperArm, "RightLowerArm", Vector3.right * .2f, bones); AddFigureDetailBone(rightLowerArm, "RightHand", Vector3.right * .18f, bones);
            Transform leftUpperLeg = AddFigureDetailBone(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones); Transform leftLowerLeg = AddFigureDetailBone(leftUpperLeg, "LeftLowerLeg", Vector3.down * .35f, bones); AddFigureDetailBone(leftLowerLeg, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rightUpperLeg = AddFigureDetailBone(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones); Transform rightLowerLeg = AddFigureDetailBone(rightUpperLeg, "RightLowerLeg", Vector3.down * .35f, bones); AddFigureDetailBone(rightLowerLeg, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            HumanBone[] human = new HumanBone[names.Length]; for (int index = 0; index < names.Length; index++) human[index] = new HumanBone { boneName = names[index], humanName = names[index], limit = new HumanLimit { useDefaultValues = true } };
            List<SkeletonBone> skeleton = new List<SkeletonBone> { ToFigureDetailSkeletonBone(root.transform) }; for (int index = 0; index < bones.Count; index++) skeleton.Add(ToFigureDetailSkeletonBone(bones[index]));
            avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
            animator.avatar = avatar;
            GameObject body = new GameObject("Body"); body.transform.SetParent(root.transform, false); body.AddComponent<SkinnedMeshRenderer>();
            return root;
        }

        private static Transform AddFigureDetailBone(Transform parent, string name, Vector3 position, List<Transform> bones) { Transform bone = new GameObject(name).transform; bone.SetParent(parent, false); bone.localPosition = position; bones.Add(bone); return bone; }
        private static SkeletonBone ToFigureDetailSkeletonBone(Transform transform) => new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };

        [TestCase("Cancel")]
        [TestCase("SaveDiagnostic")]
        [TestCase("DirtyQueryException")]
        [TestCase("DialogException")]
        [TestCase("SaveException")]
        [TestCase("IgnoreException")]
        public void NavigationTreeView_RejectedDirtyNavigationRestoresItsSelectionAndPreservesBinding(string failure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Func<ShapeSyncDatabaseWindow.Section, bool> originalDirty = ShapeSyncDatabaseWindow.IsDetailDirty;
            Func<ShapeSyncDatabaseWindow.Section, string> originalSave = ShapeSyncDatabaseWindow.SaveDirtyDetail;
            Action<ShapeSyncDatabaseWindow.Section> originalIgnore = ShapeSyncDatabaseWindow.IgnoreDirtyDetail;
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            Object originalSelection = Selection.activeObject;
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = _ => true;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = _ => null;
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = _ => { };
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                if (failure == "SaveDiagnostic") { ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0; ShapeSyncDatabaseWindow.SaveDirtyDetail = _ => "Injected save failure"; }
                if (failure == "DirtyQueryException") ShapeSyncDatabaseWindow.IsDetailDirty = _ => throw new InvalidOperationException("Injected dirty query failure");
                if (failure == "DialogException") ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => throw new InvalidOperationException("Injected dialog failure");
                if (failure == "SaveException") { ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0; ShapeSyncDatabaseWindow.SaveDirtyDetail = _ => throw new InvalidOperationException("Injected save handler failure"); }
                if (failure == "IgnoreException") { ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1; ShapeSyncDatabaseWindow.IgnoreDirtyDetail = _ => throw new InvalidOperationException("Injected ignore handler failure"); }

                Assert.That(window.TrySetDatabase(database, out string bindingDiagnostic), Is.True, bindingDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView treeView = new ShapeSyncDatabaseWindow.NavigationTreeView(
                    new ShapeSyncTreeViewState(), window.TryNavigateTo, () => window.SelectedSection);
                treeView.ApplySelectionChangeForTest(new System.Collections.Generic.List<int> { 2 });

                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.General));
                Assert.That(treeView.SelectedItemIdsForTest, Is.EqualTo(new[] { 1 }));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(Selection.activeObject, Is.SameAs(originalSelection));
            }
            finally
            {
                ShapeSyncDatabaseWindow.IsDetailDirty = originalDirty;
                ShapeSyncDatabaseWindow.SaveDirtyDetail = originalSave;
                ShapeSyncDatabaseWindow.IgnoreDirtyDetail = originalIgnore;
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
                Selection.activeObject = originalSelection;
            }
        }

        [Test]
        public void ShapesMetadataDraft_NavigationSavesIgnoresAndCancels()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetShapeTags(new[] { "Tag" }, out string tagDiagnostic), Is.True, tagDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 1, new[] { "Tag" }, out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectShapeForTest("hair"), Is.True);
                window.SetSelectedShapeMetadataDraftForTest("Saved", 2, new[] { "Tag" });
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(path, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(saved.Registry.Shapes.Single().ShapeName, Is.EqualTo("Saved"));

                Assert.That(window.TrySelectShapeForTest("hair"), Is.True);
                window.SetSelectedShapeMetadataDraftForTest("Ignored", 3, new[] { "Tag" });
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.True);
                Assert.That(saved.Registry.Shapes.Single().ShapeName, Is.EqualTo("Saved"));

                Assert.That(window.TrySelectShapeForTest("hair"), Is.True);
                window.SetSelectedShapeMetadataDraftForTest("Cancelled", 4, new[] { "Tag" });
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Shapes));
            }
            finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog; Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapesTree_NavigatesToExclusiveRootTagsAndShapeDetails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetShapeTags(new[] { "Tag" }, out string tagDiagnostic), Is.True, tagDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, new[] { "Tag" }, out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                tree.ApplySelectionChangeForTest(new[] { tree.GetShapeItemIdForTest("hair") });
                Assert.That(window.ShapesDetailViewForTest, Is.EqualTo("Shape"));
                Assert.That(window.SelectedShapeIdForTest, Is.EqualTo("hair"));
                tree.ApplySelectionChangeForTest(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapesItemId });
                Assert.That(window.ShapesDetailViewForTest, Is.EqualTo("Root"));
                Assert.That(window.SelectedShapeIdForTest, Is.Null);
                tree.ApplySelectionChangeForTest(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapeTagsItemId });
                Assert.That(window.ShapesDetailViewForTest, Is.EqualTo("Tags"));
                Assert.That(window.SelectedShapeIdForTest, Is.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapesTree_RemoveResetsAcceptedSelectionToShapesRoot()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                int shapeItemId = tree.GetShapeItemIdForTest("hair");
                tree.ApplySelectionChangeForTest(new[] { shapeItemId });
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { shapeItemId }));
                Assert.That(window.TryRemoveSelectedShapeForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(window.ShapesDetailViewForTest, Is.EqualTo("Root"));
                Assert.That(window.SelectedShapeIdForTest, Is.Null);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapesItemId }));

                tree.ApplySelectionChangeForTest(new[] { 999999 });
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapesItemId }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapesTree_SaveSynchronizesAcceptedSelectionWhenWindowSelectsShapeDirectly()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                tree.ApplySelectionChangeForTest(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.ShapesItemId });
                Assert.That(window.TrySelectShapeForTest("hair"), Is.True);
                int shapeItemId = tree.GetShapeItemIdForTest("hair");
                window.SetSelectedShapeMetadataDraftForTest("Saved Hair", 1, Array.Empty<string>());
                Assert.That(window.TrySaveSelectedShapeDraftForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { shapeItemId }));

                tree.ApplySelectionChangeForTest(new[] { 999999 });
                Assert.That(tree.SelectedItemIdsForTest, Is.EqualTo(new[] { shapeItemId }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void ShapeTagsDetail_SaveIsDisabledUntilTagsDraftChanges()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetShapeTags(new[] { "Tag" }, out string tagDiagnostic), Is.True, tagDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectShapeTagsForTest(), Is.True);
                window.SetShapeTagsDraftForTest(new[] { "Tag" });
                Assert.That(window.IsShapeTagsDetailDirtyForTest, Is.False, "A clean Tags draft must disable Save.");
                window.SetShapeTagsDraftForTest(new[] { "Tag", "New Tag" });
                Assert.That(window.IsShapeTagsDetailDirtyForTest, Is.True, "A changed Tags draft must enable Save.");
                Assert.That(window.TrySaveShapeTagsForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsShapeTagsDetailDirtyForTest, Is.False, "Saving Tags must disable Save again.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void Generate_PreflightRejectsIncompleteMorphBeforeOutputMutation()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated"), Is.Not.Empty);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            bool generatorReached = false;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                AddRegistryItem(window.Database.Registry, "figureAxes", new ShapeSyncDatabaseRegistry.FigureAxisEntry(
                    "Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm));
                Assert.That(window.Database.Registry.TryAddShape("morph-id", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
                ShapeSyncFigureGenerator.BeforePersistForTests = (_, __) => generatorReached = true;
                Assert.That(window.TryGenerateForTest(generatedRoot, out string generateDiagnostic), Is.False);
                StringAssert.Contains("RelationMissing", generateDiagnostic);
                StringAssert.Contains("entity=Shape:morph-id", generateDiagnostic);
                StringAssert.Contains("target=Smile", generateDiagnostic);
                Assert.That(window.GenerateDiagnosticsForTest.Any(item => item.EntityId == "morph-id" && item.TargetId == "Smile"), Is.True);
                Assert.That(generatorReached, Is.False, "Generate preflight must reject before an output generator starts mutating assets.");
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncShapeTemplate>(generatedRoot + "/morph-id.asset"), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(generatedRoot + "/ShapeSyncShapeCatalog.txt"), Is.Null);
            }
            finally
            {
                ShapeSyncFigureGenerator.BeforePersistForTests = null;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Generate_RollsBackFigureStageWhenTheOuterPipelineFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated"), Is.Not.Empty);
            const string sentinelPath = Root + "/Generated/Sentinel.asset";
            AssetDatabase.CreateAsset(new TextAsset("before"), sentinelPath);
            string sentinelGuid = AssetDatabase.AssetPathToGUID(sentinelPath);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.GenerateFigure = (ShapeSyncDatabase db, string root, string registries, string bindings, string materials, string textures, ICollection<string> generated, out string diagnostic) =>
                {
                    AssetDatabase.CreateAsset(new TextAsset("staged"), root + "/Staged.asset");
                    diagnostic = "Injected Figure stage failure";
                    return false;
                };

                Assert.That(window.TryGenerateForTest(generatedRoot, out string generateDiagnostic), Is.False);
                StringAssert.Contains("Injected Figure stage failure", generateDiagnostic);
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(sentinelPath).text, Is.EqualTo("before"));
                Assert.That(AssetDatabase.AssetPathToGUID(sentinelPath), Is.EqualTo(sentinelGuid));
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(generatedRoot + "/Staged.asset"), Is.Null,
                    "Outer Generate rollback must remove assets staged after the snapshot.");
            }
            finally
            {
                ShapeSyncDatabaseWindow.GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                Object.DestroyImmediate(window);
            }
        }

        [TestCase("Outfit")]
        [TestCase("Shape")]
        public void Generate_RollsBackLaterStageWhenTheOuterPipelineFails(string failingStage)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated"), Is.Not.Empty);
            const string sentinelPath = Root + "/Generated/Sentinel.asset";
            AssetDatabase.CreateAsset(new TextAsset("before"), sentinelPath);
            string sentinelGuid = AssetDatabase.AssetPathToGUID(sentinelPath);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.GenerateFigure = SucceedFigureForTest;
                if (failingStage == "Outfit")
                {
                    ShapeSyncDatabaseWindow.GenerateOutfit = (ShapeSyncDatabase db, string root, string bindings, string outfits, ICollection<string> generated, out string diagnostic) =>
                    {
                        AssetDatabase.CreateAsset(new TextAsset("staged outfit"), root + "/StagedOutfit.asset");
                        diagnostic = "Injected Outfit stage failure";
                        return false;
                    };
                    ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                }
                else
                {
                    ShapeSyncDatabaseWindow.GenerateOutfit = SucceedOutfitForTest;
                    ShapeSyncDatabaseWindow.GenerateShape = (ShapeSyncDatabase db, string root, IReadOnlyCollection<string> generated, out string diagnostic) =>
                    {
                        AssetDatabase.CreateAsset(new TextAsset("staged shape"), root + "/StagedShape.asset");
                        diagnostic = "Injected Shape stage failure";
                        return false;
                    };
                }

                Assert.That(window.TryGenerateForTest(generatedRoot, out string generateDiagnostic), Is.False);
                StringAssert.Contains("Injected " + failingStage + " stage failure", generateDiagnostic);
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(sentinelPath).text, Is.EqualTo("before"));
                Assert.That(AssetDatabase.AssetPathToGUID(sentinelPath), Is.EqualTo(sentinelGuid));
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(generatedRoot + "/Staged" + failingStage + ".asset"), Is.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ShapeGenerate_MorphTemplatePreservesIdAndFixedPriorityWithoutTags()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetShapeTags(new[] { "Tag" }, out string tagDiagnostic), Is.True, tagDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-id", "Generated Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 99, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
                contents.Registry.Shapes.Single(shape => shape.ShapeId == "morph-id").SetMorphs(new[] { new MorphValue { Target = "Explicit Zero", Value = 0f } });
                Assert.That(contents.Registry.TryAddShape("skin-id", "Generated Skin", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 2, new[] { "Tag" }, out string skinDiagnostic), Is.True, skinDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-id", "Generated Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 3, new[] { "Tag" }, out string hairDiagnostic), Is.True, hairDiagnostic);
                Assert.That(contents.Registry.TryAddShape("outfit-id", "Generated Outfit", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, 4, new[] { "Tag" }, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string meshOutfitDiagnostic), Is.True, meshOutfitDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry meshOutfit = contents.Registry.Outfits.Single(outfit => outfit.Identity == "mesh-outfit");
                meshOutfit.SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("body", null, null) });
                meshOutfit.SetFigureMaskEntries(new[] { new ShapeSyncDatabaseRegistry.FigureMaskEntry("figure-body", "mask-texture") });
                Assert.That(contents.Registry.TryAddShapePart("hair-id", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string meshPartDiagnostic), Is.True, meshPartDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("hair-id", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string colorPartDiagnostic), Is.True, colorPartDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("hair-id", ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset, out string uvPartDiagnostic), Is.True, uvPartDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMeshOutfit("hair-id", 0, "mesh-outfit", out string meshTargetDiagnostic), Is.True, meshTargetDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("hair-id", 1, "mesh-outfit", "body", out string colorTargetDiagnostic), Is.True, colorTargetDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartMaterialTarget("hair-id", 2, "mesh-outfit", "body", out string uvTargetDiagnostic), Is.True, uvTargetDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartUv("hair-id", 2, 2f, 3f, .25f, -.5f, out string uvDiagnostic), Is.True, uvDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);

            Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, Root, out string generateDiagnostic), Is.True, generateDiagnostic);
            MorphShapeTemplate template = AssetDatabase.LoadAssetAtPath<MorphShapeTemplate>(Root + "/morph-id.asset");
            Assert.That(template, Is.Not.Null);
            Assert.That(template.ShapeId, Is.EqualTo("morph-id"));
            Assert.That(template.Priority, Is.Zero);
            Assert.That(template.Tags, Is.Empty);
            Assert.That(template.Morphs.Single().Target, Is.EqualTo("Explicit Zero"));
            Assert.That(template.Morphs.Single().Value, Is.Zero, "Generate must lower explicitly stored zero Morph values.");
            Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncShapeTemplate>(Root + "/Shapes/morph-id.asset"), Is.Null, "Shape Templates are emitted directly under the selected output root; no Shapes subfolder is contractual.");
            TextAsset catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/ShapeSyncShapeCatalog.txt");
            Assert.That(catalog, Is.Not.Null);
            StringAssert.StartsWith("# ShapeSync generated output catalog.", catalog.text);
            StringAssert.Contains("# AUTOMATICALLY GENERATED. DO NOT EDIT.", catalog.text);
            StringAssert.Contains(Root + "/morph-id.asset", catalog.text);
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Root + "/ShapeSyncShapeCatalog.asset"), Is.Null, "The output catalog is a human-readable text file, not a ScriptableObject asset.");
            Assert.That(AssetDatabase.LoadAssetAtPath<SkinShapeTemplate>(Root + "/skin-id.asset").ShapeId, Is.EqualTo("skin-id"));
            Assert.That(AssetDatabase.LoadAssetAtPath<HairShapeTemplate>(Root + "/hair-id.asset").ShapeId, Is.EqualTo("hair-id"));
            Assert.That(AssetDatabase.LoadAssetAtPath<OutfitShapeTemplate>(Root + "/outfit-id.asset").ShapeId, Is.EqualTo("outfit-id"));
            HairShapeTemplate hair = AssetDatabase.LoadAssetAtPath<HairShapeTemplate>(Root + "/hair-id.asset");
            Assert.That(hair.Parts.Select(part => part.GetType()), Is.EqualTo(new[] { typeof(MeshEntry), typeof(ColorEntry), typeof(UvsetEntry) }));
            Assert.That(((MeshEntry)hair.Parts[0]).Masks.Single().ProxyEntryName, Is.EqualTo("figure-body"));
            Assert.That(((MeshEntry)hair.Parts[0]).Masks.Single().MaskName, Is.EqualTo("mask-texture"));
            Assert.That(((UvsetEntry)hair.Parts[2]).ScaleX, Is.EqualTo(2f));
            string morphGuidBeforeOverwrite = AssetDatabase.AssetPathToGUID(Root + "/morph-id.asset");
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, Root, out string overwriteDiagnostic), Is.True, overwriteDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<MorphShapeTemplate>(Root + "/morph-id.asset"), Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(Root + "/morph-id.asset"), Is.EqualTo(morphGuidBeforeOverwrite), "Same-kind Shape overwrite must preserve the existing asset GUID.");
            Assert.That(AssetDatabase.LoadAssetAtPath<MorphShapeTemplate>(Root + "/morph-id 1.asset"), Is.Null);
            Action<UnityEngine.Object, UnityEngine.Object> originalCopySerialized = ShapeSyncShapeGenerator.CopySerialized;
            try
            {
                bool injectFailure = true;
                ShapeSyncShapeGenerator.CopySerialized = (source, target) =>
                {
                    if (injectFailure)
                    {
                        injectFailure = false;
                        throw new InvalidOperationException("Injected Shape asset write failure");
                    }
                    originalCopySerialized(source, target);
                };
                Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, Root, out string rollbackDiagnostic), Is.False);
                Assert.That(rollbackDiagnostic, Does.Contain("Injected Shape asset write failure"));
            }
            finally { ShapeSyncShapeGenerator.CopySerialized = originalCopySerialized; }
            Assert.That(AssetDatabase.LoadAssetAtPath<MorphShapeTemplate>(Root + "/morph-id.asset"), Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(Root + "/morph-id.asset"), Is.EqualTo(morphGuidBeforeOverwrite), "Rollback must also retain the original Shape asset GUID.");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryUpdateShape("skin-id", "Generated Morph", 2, new[] { "Tag" }, out string duplicateDiagnostic), Is.True, duplicateDiagnostic);
            }, out string duplicateTransactionDiagnostic), Is.True, duplicateTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase duplicateNames, out string duplicateOpenDiagnostic), Is.True, duplicateOpenDiagnostic);
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(duplicateNames, Root, out string duplicateGenerateDiagnostic), Is.True, duplicateGenerateDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<MorphShapeTemplate>(Root + "/morph-id.asset"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<SkinShapeTemplate>(Root + "/skin-id.asset"), Is.Not.Null, "Duplicate Shape Names are Database display values and must not collide in generated asset paths.");
            Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncShapeTemplate>(Root + "/Generated Morph.asset"), Is.Null);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRemoveShape("outfit-id", out string removeDiagnostic), Is.True, removeDiagnostic);
            }, out string staleTransactionDiagnostic), Is.True, staleTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase withoutOutfit, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(withoutOutfit, Root, out string staleGenerateDiagnostic), Is.True, staleGenerateDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<OutfitShapeTemplate>(Root + "/outfit-id.asset"), Is.Null);
        }

        [Test]
        public void ShapeGenerate_RejectsUnconfiguredPartsBeforeOutputMutation()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddShape("invalid-hair", "Invalid Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("invalid-hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string partDiagnostic), Is.True, partDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);

            Assert.That(ShapeSyncShapeGenerator.TryGenerate(reopened, Root, out string generateDiagnostic), Is.False);
            StringAssert.Contains("ShapeGenerateInputInvalid", generateDiagnostic);
            StringAssert.Contains("Mesh entry requires a Mesh Outfit target", generateDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncShapeTemplate>(Root + "/invalid-hair.asset"), Is.Null,
                "Invalid Shape Parts must be rejected before any generated asset is written.");
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/ShapeSyncShapeCatalog.txt"), Is.Null,
                "Generate preflight must not create a catalog for rejected input.");
        }

        [Test]
        public void ShapeGenerate_MissingTextCatalogSkipsStaleDeletionAndReportsDiagnostic()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddShape("stale-id", "Stale", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, Root, out string firstDiagnostic), Is.True, firstDiagnostic);
            string generatedPath = Root + "/stale-id.asset";
            string catalogPath = Root + "/ShapeSyncShapeCatalog.txt";
            Assert.That(AssetDatabase.LoadAssetAtPath<SkinShapeTemplate>(generatedPath), Is.Not.Null);
            Assert.That(AssetDatabase.DeleteAsset(catalogPath), Is.True);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRemoveShape("stale-id", out string removeDiagnostic), Is.True, removeDiagnostic);
            }, out string removeTransactionDiagnostic), Is.True, removeTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase withoutShape, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(withoutShape, Root, out string missingCatalogDiagnostic), Is.True, missingCatalogDiagnostic);
            Assert.That(missingCatalogDiagnostic, Is.Not.Null);
            if (missingCatalogDiagnostic != null)
                Assert.That(missingCatalogDiagnostic.IndexOf("ShapeGenerateCatalogMissing", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(AssetDatabase.LoadAssetAtPath<SkinShapeTemplate>(generatedPath), Is.Not.Null, "Missing catalog must not trigger stale deletion.");
        }

        [Test]
        public void ShapeGenerate_NewEmptyOutputFolderDoesNotReportMissingCatalogWarning()
        {
            const string databasePath = Root + "/NewOutputInput.prefab";
            const string outputPath = Root + "/NewOutput";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(AssetDatabase.CreateFolder(Root, "NewOutput"), Is.Not.Empty);

            Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, outputPath, out string generateDiagnostic), Is.True, generateDiagnostic);
            Assert.That(generateDiagnostic, Is.Null, "A new empty output folder has no stale output to protect, so missing catalog is not a warning.");
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(outputPath + "/ShapeSyncShapeCatalog.txt"), Is.Not.Null);
        }

        [Test]
        public void Generate_EmptyOutputWithEarlierPipelineStagesDoesNotReportMissingCatalogWarning()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated2";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated2"), Is.Not.Empty);
            const string figurePath = Root + "/Generated2/FigureStage.asset";
            const string outfitPath = Root + "/Generated2/OutfitStage.asset";

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.GenerateFigure = (ShapeSyncDatabase db, string root, string registries, string bindings, string materials, string textures, ICollection<string> generated, out string diagnostic) =>
                {
                    AssetDatabase.CreateAsset(new TextAsset("figure"), figurePath);
                    generated.Add(figurePath);
                    diagnostic = null;
                    return true;
                };
                ShapeSyncDatabaseWindow.GenerateOutfit = (ShapeSyncDatabase db, string root, string bindings, string outfits, ICollection<string> generated, out string diagnostic) =>
                {
                    AssetDatabase.CreateAsset(new TextAsset("outfit"), outfitPath);
                    generated.Add(outfitPath);
                    diagnostic = null;
                    return true;
                };
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;

                Assert.That(window.TryGenerateForTest(generatedRoot, out string generateDiagnostic), Is.True, generateDiagnostic);
                Assert.That(generateDiagnostic, Is.Null, "Files created by earlier stages of this Generate are not stale output and must not trigger a catalog warning.");
                Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(generatedRoot + "/ShapeSyncShapeCatalog.txt"), Is.Not.Null);
            }
            finally
            {
                ShapeSyncDatabaseWindow.GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Generate_InvalidFullOutputCatalogRejectsBeforeFigureStageMutation()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated2";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated2"), Is.Not.Empty);
            const string catalogPath = Root + "/Generated2/ShapeSyncShapeCatalog.txt";
            File.WriteAllText(ShapeSyncTestAssetPaths.AssetFileSystemPath(catalogPath), "not-an-assets-path\n");
            AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            bool figureReached = false;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.GenerateFigure = (ShapeSyncDatabase db, string root, string registries, string bindings, string materials, string textures, ICollection<string> generated, out string diagnostic) =>
                {
                    figureReached = true;
                    diagnostic = null;
                    return true;
                };
                Assert.That(window.TryGenerateForTest(generatedRoot, out string generateDiagnostic), Is.False);
                StringAssert.Contains("ShapeGenerateCatalogInvalid", generateDiagnostic);
                Assert.That(figureReached, Is.False, "An invalid full-output catalog must be rejected before Figure staging.");
            }
            finally
            {
                ShapeSyncDatabaseWindow.GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Generate_SuccessClearsPreviousCatalogMissingDiagnosticAfterOutputIsEmptied()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string generatedRoot = Root + "/Generated2";
            Assert.That(AssetDatabase.CreateFolder(Root, "Generated2"), Is.Not.Empty);
            const string stalePath = Root + "/Generated2/Stale.asset";
            AssetDatabase.CreateAsset(new TextAsset("stale"), stalePath);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            bool emitMissingCatalogDiagnostic = true;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                ShapeSyncDatabaseWindow.GenerateFigure = SucceedFigureForTest;
                ShapeSyncDatabaseWindow.GenerateOutfit = SucceedOutfitForTest;
                ShapeSyncDatabaseWindow.GenerateShape = (ShapeSyncDatabase db, string root, IReadOnlyCollection<string> generated, out string diagnostic) =>
                {
                    diagnostic = emitMissingCatalogDiagnostic
                        ? "ShapeGenerateCatalogMissing: Previous output cleanup skipped because the catalog is missing."
                        : null;
                    return true;
                };

                Assert.That(window.TryGenerateForTest(generatedRoot, out string firstDiagnostic), Is.True, firstDiagnostic);
                StringAssert.Contains("ShapeGenerateCatalogMissing", firstDiagnostic);
                StringAssert.Contains("ShapeGenerateCatalogMissing", window.Diagnostic);

                Assert.That(AssetDatabase.DeleteAsset(stalePath), Is.True);
                emitMissingCatalogDiagnostic = false;
                Assert.That(window.TryGenerateForTest(generatedRoot, out string secondDiagnostic), Is.True, secondDiagnostic);
                Assert.That(secondDiagnostic, Is.Null, "A successful re-Generate after emptying the output must not retain the prior warning.");
                Assert.That(window.Diagnostic, Is.Null, "The window must clear the prior catalog warning after a clean successful re-Generate.");
            }
            finally
            {
                ShapeSyncDatabaseWindow.GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
                ShapeSyncDatabaseWindow.GenerateShape = ShapeSyncShapeGenerator.TryGenerate;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ShapeGenerate_CatalogIncludesNonShapeGeneratedAssetsFromFullPipeline()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddShape("shape-id", "Shape", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);

            string generatedFigurePath = Root + "/Figure.prefab";
            string generatedRegistryPath = Root + "/Registries/Figure_Registry.asset";
            Assert.That(AssetDatabase.IsValidFolder(Root + "/Registries") || AssetDatabase.CreateFolder(Root, "Registries") != string.Empty, Is.True);
            AssetDatabase.CreateAsset(new TextAsset("figure"), generatedFigurePath.Replace(".prefab", ".asset"));
            AssetDatabase.CreateAsset(new TextAsset("registry"), generatedRegistryPath);
            string[] additionalPaths = { generatedFigurePath.Replace(".prefab", ".asset"), generatedRegistryPath };
            Assert.That(ShapeSyncShapeGenerator.TryGenerate(database, Root, additionalPaths, out string generateDiagnostic), Is.True, generateDiagnostic);

            TextAsset catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/ShapeSyncShapeCatalog.txt");
            Assert.That(catalog, Is.Not.Null);
            StringAssert.Contains(Root + "/shape-id.asset", catalog.text);
            StringAssert.Contains(additionalPaths[0], catalog.text);
            StringAssert.Contains(additionalPaths[1], catalog.text);
        }

        private static GameObject CreateValidImportedFigure(Transform intermediate, string name, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            GameObject figure = new GameObject(name);
            figure.transform.SetParent(intermediate, false);
            SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = name + "_MergedSkinnedMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            transaction.AddSubAsset(mesh);
            renderer.sharedMesh = mesh;
            ShapeSyncFigureImportRecord record = figure.AddComponent<ShapeSyncFigureImportRecord>();
            Assert.That(record.TryConfigure(new[] { renderer }, out string recordDiagnostic), Is.True, recordDiagnostic);
            return figure;
        }

        private static void AddRegistryItem<T>(ShapeSyncDatabaseRegistry registry, string fieldName, T value)
        {
            FieldInfo field = typeof(ShapeSyncDatabaseRegistry).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            ((List<T>)field.GetValue(registry)).Add(value);
        }

        private static bool SucceedFigureForTest(ShapeSyncDatabase database, string rootPath, string registriesPath,
            string bindingsPath, string materialsPath, string texturesPath, ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            return true;
        }

        private static bool SucceedOutfitForTest(ShapeSyncDatabase database, string rootPath, string bindingsPath,
            string outfitsPath, ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            return true;
        }
    }
}
#endif
