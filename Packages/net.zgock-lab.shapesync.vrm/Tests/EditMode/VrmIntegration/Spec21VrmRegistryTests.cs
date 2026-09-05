// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UniHumanoid;
using UniVRM10;
using zgock.ShapeSync;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.VrmIntegration;
using zgock.ShapeSync.VrmIntegration.Editor;

namespace zgock.ShapeSync.Tests.EditMode.VrmIntegration
{
    public sealed class Spec21VrmRegistryTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec21VrmRegistryRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                ShapeSyncTestAssetPaths.ConsumerFolderPath("__Spec21_VrmRegistryTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void FreshDatabase_DoesNotCreateOptionalVrmRegistry()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath)
                .OfType<ShapeSyncVrmDatabaseRegistry>(), Is.Empty);
        }

        [Test]
        public void ImportExpressionReference_UsesExplicitOwnerCanonicalMaterialAndOwnedMesh()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonical = CreateCanonicalFigure(databasePath, "Figure");
            SourceVrm source = CreateSourceVrm("SourceExpression");
            Hash128 sourceHashBefore = AssetDatabase.GetAssetDependencyHash(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string importDiagnostic), Is.True, importDiagnostic);

            Assert.That(AssetDatabase.GetAssetDependencyHash(source.PrefabPath), Is.EqualTo(sourceHashBefore));
            ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference entry = registry.FigureExpressionReferences.Single();
            Assert.That(entry.FigureName, Is.EqualTo("Figure"));
            Assert.That(entry.ShapeKey, Is.EqualTo("Base"));
            Assert.That(entry.ReferencePrefab.name, Is.EqualTo("VRM_Figure"));
            Assert.That(entry.OwnerPrefab, Is.EqualTo(canonical.Root));
            Assert.That(AssetDatabase.GetAssetPath(entry.ReferencePrefab), Is.EqualTo(databasePath));
            Assert.That(entry.OwnedAssets.Any(value => value is Mesh), Is.True);
            Assert.That(entry.OwnedAssets.Any(value => value is Material || value is Texture), Is.False);

            SkinnedMeshRenderer referenceRenderer = entry.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(referenceRenderer.sharedMesh, Is.Not.Null);
            Assert.That(referenceRenderer.sharedMesh, Is.Not.EqualTo(source.Mesh));
            Assert.That(referenceRenderer.sharedMesh.name, Is.EqualTo("VRM_Figure_Mesh"));
            Assert.That(referenceRenderer.sharedMaterials.Single(), Is.EqualTo(canonical.Material));
            Assert.That(AssetDatabase.GetAssetPath(referenceRenderer.sharedMesh), Is.EqualTo(databasePath));
            Vrm10Instance referenceInstance = entry.ReferencePrefab.GetComponentsInChildren<Vrm10Instance>(true).Single();
            Assert.That(referenceInstance.Vrm, Is.Not.Null);
            Assert.That(referenceInstance.Vrm.name, Is.EqualTo("VRM_Figure_SourceExpression_Vrm"));
            Assert.That(entry.OwnedAssets.OfType<VRM10Expression>().Single().name,
                Is.EqualTo("VRM_Figure_SourceExpression_Happy"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.True, openDiagnostic);
        }

        [Test]
        public void ImportExpressionReference_MergesFaceRendererToCanonicalFigureRenderer()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonical = CreateCanonicalFigure(databasePath, "Figure", rendererName: "Figure_MergedMesh");
            SourceVrm source = CreateSourceVrm("SourceFace", rendererName: "Face");
            Hash128 sourceHashBefore = AssetDatabase.GetAssetDependencyHash(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string importDiagnostic), Is.True, importDiagnostic);

            Assert.That(AssetDatabase.GetAssetDependencyHash(source.PrefabPath), Is.EqualTo(sourceHashBefore));
            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference entry = LoadRegistry(databasePath)
                .FigureExpressionReferences.Single();
            SkinnedMeshRenderer[] renderers = entry.ReferencePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(1));
            Assert.That(renderers[0].name, Is.EqualTo("Figure_MergedMesh"));
            Assert.That(renderers[0].sharedMesh, Is.Not.EqualTo(source.Mesh));
            Assert.That(renderers[0].sharedMaterials.Single(), Is.EqualTo(canonical.Material));
            Assert.That(AssetDatabase.GetAssetPath(renderers[0].sharedMesh), Is.EqualTo(databasePath));
            Assert.That(entry.ReferencePrefab.name, Is.EqualTo("VRM_Figure"));
            Assert.That(renderers[0].sharedMesh.name, Is.EqualTo("VRM_Figure_Mesh"));
        }

        [Test]
        public void ImportFbmExpression_UsesFbmNameInReferenceName()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigure(databasePath, "Figure", createFbm: "Curvy");
            SourceVrm source = CreateSourceVrm("SourceFbm");

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Curvy",
                source.Prefab, out string diagnostic), Is.True, diagnostic);

            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference entry = LoadRegistry(databasePath)
                .FigureExpressionReferences.Single();
            Assert.That(entry.ReferencePrefab.name, Is.EqualTo("VRM_Curvy"));
            Assert.That(entry.ShapeKey, Is.EqualTo("Curvy"));
            Assert.That(entry.OwnerPrefab.name, Is.EqualTo("Curvy"));
        }

        [Test]
        public void ImportPhysicsReferences_UseCanonicalMaterialAndMeshWithoutOwningMesh()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonicalFigure = CreateCanonicalFigure(databasePath, "Figure");
            CanonicalOutfit canonicalOutfit = CreateCanonicalMeshOutfit(databasePath, "Hair");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase openedDatabase,
                out string canonicalOpenDiagnostic), Is.True, canonicalOpenDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry canonicalOutfitEntry = openedDatabase.Registry.Outfits.Single(value => value.Identity == "Hair");
            Assert.That(canonicalOutfitEntry.AxisFigures.Count, Is.EqualTo(1));
            Assert.That(canonicalOutfitEntry.AxisFigures.Single().OutfitPrefab, Is.Not.Null);
            SourceVrm source = CreateSourceVrm("SourcePhysics");

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure", source.Prefab,
                out string figureDiagnostic), Is.True, figureDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, "Hair", source.Prefab,
                out string outfitDiagnostic), Is.True, outfitDiagnostic);

            ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
            ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference figure = registry.FigurePhysicsReferences.Single();
            ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference outfit = registry.MeshOutfitPhysicsReferences.Single();
            Assert.That(figure.OwnerPrefab.name, Is.EqualTo("Figure"));
            Assert.That(outfit.OwnerPrefab, Is.EqualTo(canonicalOutfit.Root));
            Assert.That(figure.ReferencePrefab.name, Is.EqualTo("PHYS_Figure"));
            Assert.That(outfit.ReferencePrefab.name, Is.EqualTo("PHYS_Hair"));
            Assert.That(figure.OwnedAssets.Any(value => value is Mesh || value is Material || value is Texture), Is.False);
            Assert.That(outfit.OwnedAssets.Any(value => value is Mesh || value is Material || value is Texture), Is.False);

            SkinnedMeshRenderer figureRenderer = figure.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer outfitRenderer = outfit.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(figureRenderer.sharedMesh, Is.EqualTo(canonicalFigure.Mesh));
            Assert.That(figureRenderer.sharedMaterials.Single(), Is.EqualTo(canonicalFigure.Material));
            Assert.That(outfitRenderer.sharedMesh, Is.EqualTo(canonicalOutfit.Mesh));
            Assert.That(outfitRenderer.sharedMaterials.Single(), Is.EqualTo(canonicalOutfit.Material));
        }

        [Test]
        public void ImportFigurePhysicsReference_AllowsMateriallessMismatchedRendererAndUsesCanonicalSurface()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonical = CreateCanonicalFigure(databasePath, "Figure");
            SourceVrm source = CreateSourceVrm("SourcePhysicsMaterialless", rendererName: "Face");
            GameObject sourceInstance = UnityEngine.Object.Instantiate(source.Prefab);
            try
            {
                SkinnedMeshRenderer sourceRenderer = sourceInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                sourceRenderer.sharedMaterials = Array.Empty<Material>();

                Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                    sourceInstance, out string diagnostic), Is.True, diagnostic);

                ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference entry = LoadRegistry(databasePath)
                    .FigurePhysicsReferences.Single();
                SkinnedMeshRenderer referenceRenderer = entry.ReferencePrefab
                    .GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(entry.ReferencePrefab.name, Is.EqualTo("PHYS_Figure"));
                Assert.That(referenceRenderer.sharedMesh, Is.EqualTo(canonical.Mesh));
                Assert.That(referenceRenderer.sharedMaterials.Single(), Is.EqualTo(canonical.Material));
                Assert.That(entry.OwnedAssets.Any(value => value is Mesh || value is Material || value is Texture), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            }
        }

        [Test]
        public void ReimportExpression_RemovesOnlyPreviousRelationOwnedMesh()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonical = CreateCanonicalFigure(databasePath, "Figure");
            SourceVrm first = CreateSourceVrm("SourceFirst");
            SourceVrm second = CreateSourceVrm("SourceSecond");

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                first.Prefab, out string firstDiagnostic), Is.True, firstDiagnostic);
            Mesh firstOwnedMesh = LoadRegistry(databasePath).FigureExpressionReferences.Single().OwnedAssets.OfType<Mesh>().Single();
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                second.Prefab, out string secondDiagnostic), Is.True, secondDiagnostic);

            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference entry = LoadRegistry(databasePath).FigureExpressionReferences.Single();
            Assert.That(entry.OwnedAssets.OfType<Mesh>().Single(), Is.Not.EqualTo(firstOwnedMesh));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Contains(firstOwnedMesh), Is.False);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Material>(), Does.Contain(canonical.Material));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>(), Does.Contain(canonical.Mesh));
        }

        [Test]
        public void Persistence_ReopensAfterOriginalVrmAssetsAreDeleted()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonicalFigure = CreateCanonicalFigure(databasePath, "Figure");
            CanonicalOutfit canonicalOutfit = CreateCanonicalMeshOutfit(databasePath, "Hair");
            SourceVrm source = CreateSourceVrm("SourceDelete");

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string expressionDiagnostic), Is.True, expressionDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                source.Prefab, out string figurePhysicsDiagnostic), Is.True, figurePhysicsDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, "Hair",
                source.Prefab, out string outfitPhysicsDiagnostic), Is.True, outfitPhysicsDiagnostic);

            DeleteSourceVrmAssets(source);
            AssetDatabase.Refresh();

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference expression = registry.FigureExpressionReferences.Single();
            ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference figurePhysics = registry.FigurePhysicsReferences.Single();
            ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference outfitPhysics = registry.MeshOutfitPhysicsReferences.Single();

            Assert.That(expression.OwnerPrefab, Is.EqualTo(canonicalFigure.Root));
            Assert.That(figurePhysics.OwnerPrefab, Is.EqualTo(canonicalFigure.Root));
            Assert.That(outfitPhysics.OwnerPrefab, Is.EqualTo(canonicalOutfit.Root));
            AssertDatabaseOwnedReference(expression.ReferencePrefab, expression.OwnedAssets);
            AssertDatabaseOwnedReference(figurePhysics.ReferencePrefab, figurePhysics.OwnedAssets);
            AssertDatabaseOwnedReference(outfitPhysics.ReferencePrefab, outfitPhysics.OwnedAssets);

            SkinnedMeshRenderer expressionRenderer = expression.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer figurePhysicsRenderer = figurePhysics.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer outfitPhysicsRenderer = outfitPhysics.ReferencePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(expressionRenderer.sharedMesh, Is.Not.EqualTo(canonicalFigure.Mesh));
            Assert.That(expressionRenderer.sharedMaterials.Single(), Is.EqualTo(canonicalFigure.Material));
            Assert.That(figurePhysicsRenderer.sharedMesh, Is.EqualTo(canonicalFigure.Mesh));
            Assert.That(figurePhysicsRenderer.sharedMaterials.Single(), Is.EqualTo(canonicalFigure.Material));
            Assert.That(outfitPhysicsRenderer.sharedMesh, Is.EqualTo(canonicalOutfit.Mesh));
            Assert.That(outfitPhysicsRenderer.sharedMaterials.Single(), Is.EqualTo(canonicalOutfit.Material));
        }

        [Test]
        public void Open_RejectsExternalRelationOwnedAsset()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigure(databasePath, "Figure");
            SourceVrm source = CreateSourceVrm("SourceInvalidOwnership");
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string importDiagnostic), Is.True, importDiagnostic);

            ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
            SerializedObject serialized = new SerializedObject(registry);
            SerializedProperty references = serialized.FindProperty("figureExpressionReferences");
            SerializedProperty ownedAssets = references.GetArrayElementAtIndex(0).FindPropertyRelative("ownedAssets");
            ownedAssets.InsertArrayElementAtIndex(ownedAssets.arraySize);
            ownedAssets.GetArrayElementAtIndex(ownedAssets.arraySize - 1).objectReferenceValue = source.Mesh;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.False);
            Assert.That(openDiagnostic, Does.Contain("owned assets must be Database sub-assets"));
        }

        [Test]
        public void FailedImport_DoesNotLeaveOptionalRegistryOrReference()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigureWithExternalMaterial(databasePath, "Figure");
            SourceVrm source = CreateSourceVrm("SourceRollback");
            Hash128 sourceHashBefore = AssetDatabase.GetAssetDependencyHash(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string importDiagnostic), Is.False);
            Assert.That(importDiagnostic, Does.Contain("Canonical owner material must be a Database-owned Material"));
            Assert.That(AssetDatabase.GetAssetDependencyHash(source.PrefabPath), Is.EqualTo(sourceHashBefore));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath)
                .OfType<ShapeSyncVrmDatabaseRegistry>(), Is.Empty);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.True, openDiagnostic);
        }

        [Test]
        public void Open_RejectsVrmMarkerWithoutUsableVrmRegistry()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                ShapeSyncDatabaseOptionalFeatureMarker marker = ShapeSyncDatabaseOptionalFeatureMarker.Create("VRM");
                context.AddSubAsset(marker);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("VRM Registry"));
        }

        [Test]
        public void DatabaseWindowTree_ExposesFigureAndMeshOutfitVrmNavigation()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                ShapeSyncDatabaseWindow.NavigationTreeView tree = window.CreateNavigationTreeViewForTest();
                Assert.That(ShapeSyncDatabaseOptionalUiProvider.HasVrmNavigation, Is.True);
                Assert.That(tree.FigureChildDisplayNamesForTest, Does.Contain("VRM"));
                Assert.That(tree.MeshOutfitChildDisplayNamesForTest, Does.Contain("VRM"));
                tree.ApplySelectionChangeForTest(new[] { ShapeSyncDatabaseWindow.NavigationTreeView.VrmItemId });
                Assert.That(window.SelectedSection, Is.EqualTo(ShapeSyncDatabaseWindow.Section.Vrm));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void FigureVrmDetail_DerivesRowsAndSavesDraftThroughImporter()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigure(databasePath, "Figure", createFbm: "Curvy");
            SourceVrm expressionSource = CreateSourceVrm("SourceFigureExpression");
            SourceVrm physicsSource = CreateSourceVrm("SourceFigurePhysics");

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionShapeKeysForTest(window),
                    Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Curvy" }));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.False);

                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(window,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, expressionSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(window, "Curvy", expressionSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigurePhysicsInputForTest(window, physicsSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);

                Assert.That(ShapeSyncVrmDatabaseWindowUi.SaveFigureDetailForTest(window), Is.Null);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.False);

                ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
                Assert.That(registry.FigureExpressionReferences.Select(value => value.ShapeKey),
                    Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Curvy" }));
                Assert.That(registry.FigurePhysicsReferences, Has.Count.EqualTo(1));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionDatabasePrefabForTest(window,
                    ShapeSyncDatabaseRegistry.BaseShapeKey), Is.EqualTo(registry.FigureExpressionReferences
                    .Single(value => value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).ReferencePrefab));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigurePhysicsDatabasePrefabForTest(window),
                    Is.EqualTo(registry.FigurePhysicsReferences.Single().ReferencePrefab));
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(window);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void MeshOutfitVrmDetail_IgnoreRestoresAcceptedDraft()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalMeshOutfit(databasePath, "Hair");
            SourceVrm acceptedSource = CreateSourceVrm("SourceAcceptedOutfit");
            SourceVrm draftSource = CreateSourceVrm("SourceDraftOutfit");
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, "Hair",
                acceptedSource.Prefab, out string importDiagnostic), Is.True, importDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetMeshOutfitVrmInputForTest(window, "Hair", draftSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsMeshOutfitVrmDetailDirtyForTest(window, "Hair"), Is.True);

                ShapeSyncVrmDatabaseWindowUi.IgnoreMeshOutfitVrmDetailForTest(window, "Hair");

                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsMeshOutfitVrmDetailDirtyForTest(window, "Hair"), Is.False);
                ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference accepted = LoadRegistry(databasePath)
                    .MeshOutfitPhysicsReferences.Single();
                Assert.That(ShapeSyncVrmDatabaseWindowUi.MeshOutfitVrmDatabasePrefabForTest(window, "Hair"),
                    Is.EqualTo(accepted.ReferencePrefab));
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(window);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void VrmDetail_RemoveClearsDraftAndDeletesReferenceOnSave()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CanonicalFigure canonicalFigure = CreateCanonicalFigure(databasePath, "Figure", createFbm: "Curvy");
            CanonicalOutfit canonicalOutfit = CreateCanonicalMeshOutfit(databasePath, "Hair");
            SourceVrm source = CreateSourceVrm("SourceRemove");

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                source.Prefab, out string diagnostic), Is.True, diagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Curvy",
                source.Prefab, out diagnostic), Is.True, diagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                source.Prefab, out diagnostic), Is.True, diagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, "Hair",
                source.Prefab, out diagnostic), Is.True, diagnostic);

            ShapeSyncVrmDatabaseRegistry before = LoadRegistry(databasePath);
            GameObject baseExpressionPrefab = before.FigureExpressionReferences.Single(value => value.ShapeKey == "Base").ReferencePrefab;
            UnityEngine.Object[] baseExpressionAssets = before.FigureExpressionReferences.Single(value => value.ShapeKey == "Base")
                .OwnedAssets.ToArray();
            GameObject figurePhysicsPrefab = before.FigurePhysicsReferences.Single().ReferencePrefab;
            UnityEngine.Object[] figurePhysicsAssets = before.FigurePhysicsReferences.Single().OwnedAssets.ToArray();
            GameObject outfitPhysicsPrefab = before.MeshOutfitPhysicsReferences.Single().ReferencePrefab;
            UnityEngine.Object[] outfitPhysicsAssets = before.MeshOutfitPhysicsReferences.Single().OwnedAssets.ToArray();

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.ClearFigureExpressionForTest(window, "Base"), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.ClearFigurePhysicsForTest(window), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);
                Assert.That(LoadRegistry(databasePath).FigureExpressionReferences.Select(value => value.ShapeKey),
                    Is.EquivalentTo(new[] { "Base", "Curvy" }), "Remove must remain Draft-only before Save.");
                Assert.That(LoadRegistry(databasePath).FigurePhysicsReferences, Has.Count.EqualTo(1));

                string incompleteDiagnostic = ShapeSyncVrmDatabaseWindowUi.SaveFigureDetailForTest(window);
                Assert.That(incompleteDiagnostic, Does.Contain("Base and all registered FBM references"));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);
                Assert.That(LoadRegistry(databasePath).FigureExpressionReferences.Select(value => value.ShapeKey),
                    Is.EquivalentTo(new[] { "Base", "Curvy" }), "An incomplete Expression set must not be saved.");
                Assert.That(LoadRegistry(databasePath).FigurePhysicsReferences, Has.Count.EqualTo(1));

                Assert.That(ShapeSyncVrmDatabaseWindowUi.ClearFigureExpressionForTest(window, "Curvy"), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SaveFigureDetailForTest(window), Is.Null);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.False);
                ShapeSyncVrmDatabaseRegistry afterFigureSave = LoadRegistry(databasePath);
                Assert.That(afterFigureSave.FigureExpressionReferences.Select(value => value.ShapeKey),
                    Is.Empty);
                Assert.That(afterFigureSave.FigurePhysicsReferences, Is.Empty);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionDatabasePrefabForTest(window, "Base"), Is.Null);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigurePhysicsDatabasePrefabForTest(window), Is.Null);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Contains(baseExpressionPrefab), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Contains(figurePhysicsPrefab), Is.False);
                foreach (UnityEngine.Object asset in baseExpressionAssets.Concat(figurePhysicsAssets))
                    Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Contains(asset), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath), Does.Contain(canonicalFigure.Mesh));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath), Does.Contain(canonicalFigure.Material));

                Assert.That(ShapeSyncVrmDatabaseWindowUi.ClearMeshOutfitVrmForTest(window, "Hair"), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsMeshOutfitVrmDetailDirtyForTest(window, "Hair"), Is.True);
                Assert.That(LoadRegistry(databasePath).MeshOutfitPhysicsReferences, Has.Count.EqualTo(1));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SaveMeshOutfitVrmDetailForTest(window, "Hair"), Is.Null);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsMeshOutfitVrmDetailDirtyForTest(window, "Hair"), Is.False);
                Assert.That(LoadRegistry(databasePath).MeshOutfitPhysicsReferences, Is.Empty);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.MeshOutfitVrmDatabasePrefabForTest(window, "Hair"), Is.Null);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Contains(outfitPhysicsPrefab), Is.False);
                foreach (UnityEngine.Object asset in outfitPhysicsAssets)
                    Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Contains(asset), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath), Does.Contain(canonicalOutfit.Mesh));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath), Does.Contain(canonicalOutfit.Material));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out openDiagnostic), Is.True, openDiagnostic);
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(window);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FigureVrmDetail_SaveReopenAndFbmAdditionRefreshesRows()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigure(databasePath, "Figure", createFbm: "Curvy");
            SourceVrm expressionSource = CreateSourceVrm("SourceReopenExpression");
            SourceVrm physicsSource = CreateSourceVrm("SourceReopenPhysics");

            ShapeSyncDatabaseWindow firstWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(firstWindow.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(firstWindow,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, expressionSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(firstWindow, "Curvy",
                    expressionSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigurePhysicsInputForTest(firstWindow, physicsSource.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SaveFigureDetailForTest(firstWindow), Is.Null);
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(firstWindow);
                UnityEngine.Object.DestroyImmediate(firstWindow);
            }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopenedDatabase,
                out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseWindow reopenedWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(reopenedWindow.TryOpenDatabase(reopenedDatabase, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionShapeKeysForTest(reopenedWindow),
                    Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Curvy" }));
                ShapeSyncVrmDatabaseRegistry persisted = LoadRegistry(databasePath);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionDatabasePrefabForTest(reopenedWindow,
                    ShapeSyncDatabaseRegistry.BaseShapeKey), Is.EqualTo(persisted.FigureExpressionReferences
                    .Single(value => value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).ReferencePrefab));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionDatabasePrefabForTest(reopenedWindow, "Curvy"),
                    Is.EqualTo(persisted.FigureExpressionReferences.Single(value => value.ShapeKey == "Curvy").ReferencePrefab));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigurePhysicsDatabasePrefabForTest(reopenedWindow),
                    Is.EqualTo(persisted.FigurePhysicsReferences.Single().ReferencePrefab));

                AddCanonicalFbm(databasePath, "Tall");
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase afterFbmDatabase,
                    out string afterFbmOpenDiagnostic), Is.True, afterFbmOpenDiagnostic);
                Assert.That(reopenedWindow.TryOpenDatabase(afterFbmDatabase, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionShapeKeysForTest(reopenedWindow),
                    Is.EqualTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Curvy", "Tall" }));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.FigureExpressionDatabasePrefabForTest(reopenedWindow, "Tall"),
                    Is.Null);
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(reopenedWindow);
                UnityEngine.Object.DestroyImmediate(reopenedWindow);
            }
        }

        [Test]
        public void VrmDetail_DirtyGuardProtectsDatabaseSwitchAndFailedOpen()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            CreateCanonicalFigure(databasePath, "Figure");
            SourceVrm source = CreateSourceVrm("SourceDirtyGuard");

            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Second.prefab", out ShapeSyncDatabase secondDatabase,
                out createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Invalid.prefab", out ShapeSyncDatabase invalidDatabase,
                out createDiagnostic), Is.True, createDiagnostic);
            AddVrmMarkerWithoutRegistry(AssetDatabase.GetAssetPath(invalidDatabase));

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Vrm);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(window,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, source.Prefab), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);

                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryOpenDatabase(secondDatabase, out string cancelDiagnostic), Is.False);
                Assert.That(window.Database, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(databasePath));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);

                Assert.That(window.TryOpenDatabase(invalidDatabase, out cancelDiagnostic), Is.False);
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(databasePath));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.True);

                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 1;
                Assert.That(window.TryOpenDatabase(invalidDatabase, out string invalidOpenDiagnostic), Is.False);
                Assert.That(invalidOpenDiagnostic, Does.Contain("VRM Registry"));
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(databasePath));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.False);

                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Vrm);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetFigureExpressionInputForTest(window,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, source.Prefab), Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 0;
                Assert.That(window.TryOpenDatabase(secondDatabase, out string switchDiagnostic), Is.True, switchDiagnostic);
                Assert.That(AssetDatabase.GetAssetPath(window.Database), Is.EqualTo(AssetDatabase.GetAssetPath(secondDatabase)));
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsFigureDetailDirtyForTest(window), Is.False);
                Assert.That(LoadRegistry(databasePath).FigureExpressionReferences, Has.Count.EqualTo(1));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(window);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GenerationDetail_VrmPathDefaultsPersistsAndReopens()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Database.prefab", out ShapeSyncDatabase database,
                out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TryOpenDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.GenerationVrmPathForTest(window), Is.EqualTo("VRM/"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .OfType<ShapeSyncVrmDatabaseRegistry>(), Is.Empty);

                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetGenerationVrmPathForTest(window, "Generated/VRM"), Is.True);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsGenerationVrmPathDirtyForTest(window), Is.True);
                Assert.That(window.TrySaveGenerationForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.IsGenerationVrmPathDirtyForTest(window), Is.False);

                ShapeSyncVrmDatabaseRegistry registry = LoadRegistry(databasePath);
                Assert.That(registry.GenerationVrmPath, Is.EqualTo("Generated/VRM/"));
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened,
                    out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(LoadRegistry(databasePath).GenerationVrmPath, Is.EqualTo("Generated/VRM/"));
                Assert.That(window.TryOpenDatabase(reopened, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncVrmDatabaseWindowUi.GenerationVrmPathForTest(window), Is.EqualTo("Generated/VRM/"));

                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetGenerationVrmPathForTest(window, "../Outside"), Is.True);
                Assert.That(window.TrySaveGenerationForTest(out string invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic, Does.Contain("VrmGenerationPathInvalid"));
                Assert.That(LoadRegistry(databasePath).GenerationVrmPath, Is.EqualTo("Generated/VRM/"));

                Assert.That(ShapeSyncVrmDatabaseWindowUi.SetGenerationVrmPathForTest(window, "Materials"), Is.True);
                Assert.That(window.TrySaveGenerationForTest(out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("VrmGenerationPathDuplicate"));
                Assert.That(LoadRegistry(databasePath).GenerationVrmPath, Is.EqualTo("Generated/VRM/"));
            }
            finally
            {
                ShapeSyncVrmDatabaseWindowUi.ForgetStateForTest(window);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GeneratePost_CreatesInitializedVrmGraphUnderConfiguredPath()
        {
            string databasePath = Root + "/GenerationDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase created,
                out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, intermediate, transaction) =>
                {
                    GameObject figure = CreateHumanoidFigure("SlimFigure", intermediate, transaction);
                    Assert.That(database.Registry.TryRegisterBaseFigure(database, "SlimFigure", figure,
                        out string baseDiagnostic), Is.True, baseDiagnostic);
                }, out string figureDiagnostic), Is.True, figureDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, _, transaction) =>
                {
                    ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                        database, databasePath, transaction, out string registryDiagnostic);
                    Assert.That(registry, Is.Not.Null, registryDiagnostic);
                    Assert.That(registry.TrySetGenerationVrmPath("GeneratedVrm", out string pathDiagnostic),
                        Is.True, pathDiagnostic);
                }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);
            string rootPath = Root + "/Generated";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "Generated")), Is.True);

            GameObject source = database.Registry.BaseFigures.Single().Figure;
            GameObject clone = UnityEngine.Object.Instantiate(source);
            clone.name = "SlimFigure";
            string prefabPath = rootPath + "/SlimFigure.prefab";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(clone, prefabPath), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            bool generatedOk = ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic);
            if (!generatedOk) throw new InvalidOperationException("VRM Generate post diagnostic: " + generateDiagnostic);

            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Vrm10Instance instance = generated.GetComponent<Vrm10Instance>();
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.Vrm, Is.Not.Null);
            Assert.That(generated.GetComponent<Humanoid>(), Is.Not.Null);
            Assert.That(generated.GetComponent<ShapeSyncVrmIntegrationAdapter>(), Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(instance.Vrm), Is.EqualTo(rootPath + "/GeneratedVrm/VRM_SlimFigure_VRM10Object.asset"));
            Assert.That(instance.Vrm.Expression.Clips, Is.Empty,
                "An empty Expression intersection must not emit placeholder Expression assets.");
            Assert.That(generatedPaths, Does.Contain(prefabPath));
            Assert.That(generatedPaths, Does.Contain(rootPath + "/GeneratedVrm/VRM_SlimFigure_VRM10Object.asset"));
            Assert.That(generatedPaths.Any(value => value.EndsWith("VRM_SlimFigure_happy.asset", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void GeneratePost_BakesBaseDirectAndFbmMcmDifferenceWithExpressionBakerNames()
        {
            string databasePath = Root + "/ExpressionGenerationDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CanonicalFigure canonical = CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            SourceVrm baseSource = CreateSourceVrm("SourceBase", happyDelta: new[] { new Vector3(.1f, 0f, 0f), Vector3.zero, Vector3.zero });
            SourceVrm fbmSource = CreateSourceVrm("SourceFbmExpression", happyDelta: new[] { new Vector3(.3f, 0f, 0f), Vector3.zero, Vector3.zero });
            AddCustomExpressionToSource(baseSource, "custom_ha",
                new[] { new Vector3(.05f, 0f, 0f), Vector3.zero, Vector3.zero });
            AddCustomExpressionToSource(fbmSource, "custom_ha",
                new[] { new Vector3(.2f, 0f, 0f), Vector3.zero, Vector3.zero });
            Assert.That(AssetDatabase.LoadAssetAtPath<VRM10Object>(baseSource.VrmPath).Expression.Clips
                .Any(value => value.Preset == ExpressionPreset.custom && value.Clip != null), Is.True,
                "Base source custom clip was not persisted.");
            Assert.That(AssetDatabase.LoadAssetAtPath<VRM10Object>(fbmSource.VrmPath).Expression.Clips
                .Any(value => value.Preset == ExpressionPreset.custom && value.Clip != null), Is.True,
                "FBM source custom clip was not persisted.");
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                baseSource.Prefab, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Curvy",
                fbmSource.Prefab, out importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, _, transaction) =>
                {
                    ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                        database, databasePath, transaction, out string registryDiagnostic);
                    Assert.That(registry, Is.Not.Null, registryDiagnostic);
                    Assert.That(registry.TrySetGenerationVrmPath("GeneratedVrm", out string pathDiagnostic), Is.True, pathDiagnostic);
                }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);
            string rootPath = Root + "/GeneratedExpressions";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedExpressions")), Is.True);
            Mesh outputMesh = UnityEngine.Object.Instantiate(canonical.Mesh);
            outputMesh.name = "Figure_Mesh";
            string outputMeshPath = rootPath + "/Figure_Mesh.asset";
            AssetDatabase.CreateAsset(outputMesh, outputMeshPath);
            GameObject output = UnityEngine.Object.Instantiate(canonical.Root);
            output.name = "Figure";
            output.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh = outputMesh;
            string prefabPath = rootPath + "/Figure.prefab";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(output, prefabPath), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic), Is.True, generateDiagnostic);
            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Mesh generatedMesh = generated.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            int baseIndex = generatedMesh.GetBlendShapeIndex("VRM_happy");
            int mcmIndex = generatedMesh.GetBlendShapeIndex("MCM_Curvy_happy");
            Assert.That(baseIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(mcmIndex, Is.GreaterThanOrEqualTo(0));
            Vector3[] vertices = new Vector3[generatedMesh.vertexCount];
            Vector3[] normals = new Vector3[generatedMesh.vertexCount];
            Vector3[] tangents = new Vector3[generatedMesh.vertexCount];
            generatedMesh.GetBlendShapeFrameVertices(baseIndex, 0, vertices, normals, tangents);
            Assert.That(vertices[0].x, Is.EqualTo(.1f).Within(.0001f));
            generatedMesh.GetBlendShapeFrameVertices(mcmIndex, 0, vertices, normals, tangents);
            Assert.That(vertices[0].x, Is.EqualTo(.2f).Within(.0001f));
            int customIndex = generatedMesh.GetBlendShapeIndex("VRM_custom_ha");
            int customMcmIndex = generatedMesh.GetBlendShapeIndex("MCM_Curvy_custom_ha");
            Assert.That(customIndex, Is.GreaterThanOrEqualTo(0),
                "Generated BlendShapes: " + string.Join(",", Enumerable.Range(0, generatedMesh.blendShapeCount)
                    .Select(value => generatedMesh.GetBlendShapeName(value)).ToArray()));
            Assert.That(customMcmIndex, Is.GreaterThanOrEqualTo(0),
                "Generated BlendShapes: " + string.Join(",", Enumerable.Range(0, generatedMesh.blendShapeCount)
                    .Select(value => generatedMesh.GetBlendShapeName(value)).ToArray()));
            generatedMesh.GetBlendShapeFrameVertices(customIndex, 0, vertices, normals, tangents);
            Assert.That(vertices[0].x, Is.EqualTo(.05f).Within(.0001f));
            generatedMesh.GetBlendShapeFrameVertices(customMcmIndex, 0, vertices, normals, tangents);
            Assert.That(vertices[0].x, Is.EqualTo(.15f).Within(.0001f));
            Assert.That(generatedPaths.Any(value => value.EndsWith("VRM_Figure_happy.asset", StringComparison.Ordinal)), Is.True);
            Assert.That(generatedPaths.Any(value => value.EndsWith("VRM_Figure_custom_ha.asset", StringComparison.Ordinal)), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(generated.GetComponent<Vrm10Instance>().Vrm),
                Is.EqualTo(rootPath + "/GeneratedVrm/VRM_Figure_VRM10Object.asset"));
            Assert.That(generated.GetComponent<Vrm10Instance>().Vrm.Expression.Clips
                .Any(value => value.Preset == ExpressionPreset.happy && value.Clip != null), Is.True);
            Assert.That(generated.GetComponent<Vrm10Instance>().Vrm.Expression.Clips
                .Any(value => value.Preset == ExpressionPreset.custom
                    && value.Clip != null && value.Clip.name == "VRM_Figure_custom_ha"), Is.True,
                "Custom Expression must be registered in the generated VRM10Object.");
            foreach (ExpressionPreset lookPreset in new[]
            {
                ExpressionPreset.lookUp, ExpressionPreset.lookDown,
                ExpressionPreset.lookLeft, ExpressionPreset.lookRight
            })
            {
                VRM10Expression lookClip = generated.GetComponent<Vrm10Instance>().Vrm.Expression.Clips
                    .SingleOrDefault(value => value.Preset == lookPreset).Clip;
                Assert.That(lookClip, Is.Not.Null, "Look standard Expression must be retained: " + lookPreset);
                Assert.That(lookClip.MorphTargetBindings, Is.Empty,
                    "Look standard Expression must remain an empty safe asset: " + lookPreset);
            }

            Assert.That(ShapeSyncVrmGeneratePost.FinalizeGenerate(database, rootPath, generatedPaths,
                out string finalizeDiagnostic), Is.True, finalizeDiagnostic);
            generated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            UniversalExpressionProxy expressionProxy = generated.GetComponent<UniversalExpressionProxy>();
            Assert.That(expressionProxy, Is.Not.Null);
            Assert.That(expressionProxy.Expressions.Any(value => value != null && value.blendShapeName == "VRM_happy"), Is.True,
                "Generate must register baked VRM expressions in the Figure UniversalExpressionProxy.");
            Assert.That(expressionProxy.Expressions.Any(value => value != null && value.blendShapeName == "MCM_Curvy_happy"), Is.False,
                "MCM difference BlendShapes are driven through the corresponding VRM Expression entry, not as standalone proxy expressions.");
            Assert.That(expressionProxy.Expressions.Any(value => value != null && value.blendShapeName == "VRM_custom_ha"), Is.True,
                "Generated custom Expression must be registered in the Figure UniversalExpressionProxy.");
        }

        [Test]
        public void GeneratePost_EmptyBaseFbmIntersectionSucceedsWithoutExpressionAssets()
        {
            string databasePath = Root + "/EmptyExpressionGenerationDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CanonicalFigure canonical = CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            SourceVrm baseSource = CreateSourceVrm("SourceBaseOnly", happyDelta: new[] { new Vector3(.1f, 0f, 0f), Vector3.zero, Vector3.zero });
            SourceVrm fbmSource = CreateSourceVrm("SourceWithoutHappy");
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                baseSource.Prefab, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Curvy",
                fbmSource.Prefab, out importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, _, transaction) =>
                {
                    ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                        database, databasePath, transaction, out string registryDiagnostic);
                    Assert.That(registry, Is.Not.Null, registryDiagnostic);
                }, out string editDiagnostic), Is.True, editDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);
            string rootPath = Root + "/GeneratedEmptyExpressions";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedEmptyExpressions")), Is.True);
            Mesh outputMesh = UnityEngine.Object.Instantiate(canonical.Mesh);
            outputMesh.name = "Figure_Mesh";
            AssetDatabase.CreateAsset(outputMesh, rootPath + "/Figure_Mesh.asset");
            GameObject output = UnityEngine.Object.Instantiate(canonical.Root);
            output.name = "Figure";
            output.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh = outputMesh;
            string prefabPath = rootPath + "/Figure.prefab";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(output, prefabPath), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic), Is.True, generateDiagnostic);
            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(generated.GetComponent<Vrm10Instance>().Vrm.Expression.Clips, Is.Empty);
            Assert.That(generatedPaths.Any(value => value.EndsWith("VRM_Figure_happy.asset", StringComparison.Ordinal)), Is.False);
            Assert.That(generateDiagnostic, Does.Contain("intersection is empty"));
        }

        [Test]
        public void GeneratePost_RejectsIncompleteExpressionReferenceSet()
        {
            string databasePath = Root + "/IncompleteExpressionDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CanonicalFigure canonical = CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            SourceVrm baseSource = CreateSourceVrm("SourceIncompleteBase");
            SourceVrm fbmSource = CreateSourceVrm("SourceIncompleteFbm");
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Base",
                baseSource.Prefab, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, "Figure", "Curvy",
                fbmSource.Prefab, out importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryRemoveFigureExpressionReference(databasePath, "Figure", "Curvy",
                out string removeDiagnostic), Is.True, removeDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);
            string rootPath = Root + "/GeneratedIncompleteExpression";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedIncompleteExpression")), Is.True);
            GameObject output = UnityEngine.Object.Instantiate(canonical.Root);
            output.name = "Figure";
            string prefabPath = rootPath + "/Figure.prefab";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(output, prefabPath), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic), Is.False);
            Assert.That(generateDiagnostic, Does.Contain("VrmGenerateExpressionReferencesIncomplete"));
            Assert.That(generateDiagnostic, Does.Contain("Curvy"));
            Assert.That(generatedPaths, Is.Empty);
        }

        [Test]
        public void GeneratePost_TransfersPhysicsIntoFigureAndMeshOutfitPrefabs()
        {
            string databasePath = Root + "/PhysicsGenerationDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CanonicalFigure canonicalFigure = CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            CanonicalOutfit canonicalOutfit = CreateCanonicalMeshOutfit(databasePath, "Hair");
            SourceVrm source = CreateSourceVrm("SourcePhysicsGenerate");
            AddSpringBoneToSource(source.PrefabPath);
            Hash128 sourceHashBefore = AssetDatabase.GetAssetDependencyHash(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                AssetDatabase.LoadAssetAtPath<GameObject>(source.PrefabPath), out string figureImportDiagnostic), Is.True, figureImportDiagnostic);
            Assert.That(ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, "Hair",
                AssetDatabase.LoadAssetAtPath<GameObject>(source.PrefabPath), out string outfitImportDiagnostic), Is.True, outfitImportDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, _, transaction) =>
                {
                    ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                        database, databasePath, transaction, out string registryDiagnostic);
                    Assert.That(registry, Is.Not.Null, registryDiagnostic);
                    Assert.That(registry.TrySetGenerationVrmPath("GeneratedVrm", out string pathDiagnostic),
                        Is.True, pathDiagnostic);
                }, out string editDiagnostic), Is.True, editDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);
            string rootPath = Root + "/GeneratedPhysics";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedPhysics")), Is.True);
            string outfitsPath = rootPath + "/Outfits";
            Assert.That(AssetDatabase.IsValidFolder(outfitsPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(rootPath, "Outfits")), Is.True);

            GameObject figureClone = UnityEngine.Object.Instantiate(database.Registry.BaseFigures.Single().Figure);
            figureClone.name = "Figure";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(figureClone, rootPath + "/Figure.prefab"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(figureClone);
            }
            ShapeSyncDatabaseRegistry.OutfitEntry outfitEntry = database.Registry.Outfits.Single(value => value.Identity == "Hair");
            GameObject outfitClone = UnityEngine.Object.Instantiate(outfitEntry.AxisFigures.Single().OutfitPrefab);
            outfitClone.name = "Hair";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(outfitClone, outfitsPath + "/Hair.prefab"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(outfitClone);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic), Is.True, generateDiagnostic);

            GameObject generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/Figure.prefab");
            Vrm10Instance figureInstance = generatedFigure.GetComponent<Vrm10Instance>();
            Assert.That(figureInstance.SpringBone, Is.Not.Null);
            Assert.That(figureInstance.SpringBone.Springs, Has.Count.EqualTo(1));
            Assert.That(figureInstance.SpringBone.Springs[0].Joints[0].transform.root, Is.EqualTo(generatedFigure.transform));
            Assert.That(generatedPaths, Does.Not.Contain(rootPath + "/GeneratedVrm/PHYS_Figure.prefab"));
            Assert.That(generatedFigure.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.EqualTo(canonicalFigure.Mesh));
            Assert.That(generatedFigure.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials.Single(), Is.EqualTo(canonicalFigure.Material));

            GameObject generatedOutfit = AssetDatabase.LoadAssetAtPath<GameObject>(outfitsPath + "/Hair.prefab");
            ShapeSyncOutfitSpringBoneData outfitData = generatedOutfit.GetComponent<ShapeSyncOutfitSpringBoneData>();
            Assert.That(outfitData, Is.Not.Null);
            Assert.That(outfitData.Springs, Has.Count.EqualTo(1));
            Assert.That(outfitData.Springs[0].Joints[0].transform.root, Is.EqualTo(generatedOutfit.transform));
            Assert.That(generatedPaths, Does.Not.Contain(rootPath + "/GeneratedVrm/PHYS_Hair.prefab"));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/GeneratedVrm/PHYS_Figure.prefab"), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/GeneratedVrm/PHYS_Hair.prefab"), Is.Null);
            Assert.That(generatedOutfit.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.EqualTo(canonicalOutfit.Mesh));
            Assert.That(generatedOutfit.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials.Single(), Is.EqualTo(canonicalOutfit.Material));
            var regeneratedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, regeneratedPaths,
                out string regenerateDiagnostic), Is.True, regenerateDiagnostic);
            Assert.That(regeneratedPaths, Does.Not.Contain(rootPath + "/GeneratedVrm/PHYS_Figure.prefab"));
            Assert.That(regeneratedPaths, Does.Not.Contain(rootPath + "/GeneratedVrm/PHYS_Hair.prefab"));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/GeneratedVrm/PHYS_Figure.prefab"), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/GeneratedVrm/PHYS_Hair.prefab"), Is.Null);
            Assert.That(AssetDatabase.GetAssetDependencyHash(source.PrefabPath), Is.EqualTo(sourceHashBefore));
        }

        [Test]
        public void GeneratePost_PreservesMultiplePhysicsComponentsOnSharedTransform()
        {
            string databasePath = Root + "/SharedPhysicsGenerationDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            SourceVrm source = CreateSourceVrm("SourceSharedPhysicsGenerate");
            AddSharedTransformMultiColliderSpringBoneToSource(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                AssetDatabase.LoadAssetAtPath<GameObject>(source.PrefabPath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);

            string rootPath = Root + "/GeneratedSharedPhysics";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedSharedPhysics")), Is.True);
            GameObject figureClone = UnityEngine.Object.Instantiate(database.Registry.BaseFigures.Single().Figure);
            figureClone.name = "Figure";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(figureClone, rootPath + "/Figure.prefab"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(figureClone);
            }

            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath,
                new HashSet<string>(StringComparer.Ordinal), out string generateDiagnostic), Is.True, generateDiagnostic);

            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/Figure.prefab");
            Vrm10Instance instance = generated.GetComponent<Vrm10Instance>();
            Assert.That(instance.SpringBone.ColliderGroups, Has.Count.EqualTo(2));
            Assert.That(instance.SpringBone.ColliderGroups[0], Is.Not.SameAs(instance.SpringBone.ColliderGroups[1]));
            Assert.That(instance.SpringBone.ColliderGroups.Select(group => group.Name),
                Is.EqualTo(new[] { "SharedGroup0", "SharedGroup1" }));
            Assert.That(instance.SpringBone.ColliderGroups[0].Colliders, Has.Count.EqualTo(2));
            Assert.That(instance.SpringBone.ColliderGroups[1].Colliders, Has.Count.EqualTo(2));
            Assert.That(instance.SpringBone.ColliderGroups[0].Colliders[0],
                Is.Not.SameAs(instance.SpringBone.ColliderGroups[0].Colliders[1]));
            Assert.That(instance.SpringBone.ColliderGroups[1].Colliders[0],
                Is.Not.SameAs(instance.SpringBone.ColliderGroups[1].Colliders[1]));
        }

        [Test]
        public void GeneratePost_SilentlySkipsFigurePhysicsJointOnExtraBone()
        {
            string databasePath = Root + "/MissingFigurePhysicsBoneDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            CreateHumanoidCanonicalFigure(databasePath, "Figure", "Curvy");
            SourceVrm source = CreateSourceVrm("SourceMissingFigurePhysicsBone");
            AddExtraBoneSpringBoneToSource(source.PrefabPath);

            Assert.That(ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, "Figure",
                AssetDatabase.LoadAssetAtPath<GameObject>(source.PrefabPath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database,
                out string openDiagnostic), Is.True, openDiagnostic);

            string rootPath = Root + "/GeneratedMissingFigurePhysicsBone";
            Assert.That(AssetDatabase.IsValidFolder(rootPath) ||
                !string.IsNullOrEmpty(AssetDatabase.CreateFolder(Root, "GeneratedMissingFigurePhysicsBone")), Is.True);
            GameObject figureClone = UnityEngine.Object.Instantiate(database.Registry.BaseFigures.Single().Figure);
            figureClone.name = "Figure";
            string figurePath = rootPath + "/Figure.prefab";
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(figureClone, figurePath), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(figureClone);
            }

            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            Assert.That(ShapeSyncVrmGeneratePost.TryGenerate(database, rootPath, generatedPaths,
                out string generateDiagnostic), Is.True, generateDiagnostic);
            Assert.That(generateDiagnostic, Does.Not.Contain("VrmGenerateFigurePhysicsTransformMissing"));
            Assert.That(generatedPaths, Does.Not.Contain(rootPath + "/VRM/PHYS_Figure.prefab"));

            GameObject generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            Assert.That(generatedFigure, Is.Not.Null);
            Assert.That(generatedFigure.transform.Find("ExtraBone/Tip"), Is.Null);
            Vrm10Instance generatedInstance = generatedFigure.GetComponent<Vrm10Instance>();
            Assert.That(generatedInstance.SpringBone.Springs, Is.Empty);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(rootPath + "/VRM/PHYS_Figure.prefab"), Is.Null);
        }

        private static GameObject CreateHumanoidFigure(string name, Transform intermediate,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(intermediate, false);
            Transform hips = NewBone(root.transform, "Hips");
            Transform spine = NewBone(hips, "Spine");
            Transform chest = NewBone(spine, "Chest");
            Transform neck = NewBone(chest, "Neck");
            NewBone(neck, "Head");
            Transform leftUpperArm = NewBone(chest, "LeftUpperArm");
            Transform leftLowerArm = NewBone(leftUpperArm, "LeftLowerArm");
            NewBone(leftLowerArm, "LeftHand");
            Transform rightUpperArm = NewBone(chest, "RightUpperArm");
            Transform rightLowerArm = NewBone(rightUpperArm, "RightLowerArm");
            NewBone(rightLowerArm, "RightHand");
            Transform leftUpperLeg = NewBone(hips, "LeftUpperLeg");
            Transform leftLowerLeg = NewBone(leftUpperLeg, "LeftLowerLeg");
            NewBone(leftLowerLeg, "LeftFoot");
            Transform rightUpperLeg = NewBone(hips, "RightUpperLeg");
            Transform rightLowerLeg = NewBone(rightUpperLeg, "RightLowerLeg");
            NewBone(rightLowerLeg, "RightFoot");

            Animator animator = root.AddComponent<Animator>();
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, CreateHumanDescription(root.transform));
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Spec21 VRM Generate test could not create a valid Humanoid Avatar.");
            avatar.name = name + "Avatar";
            transaction.AddSubAsset(avatar);
            animator.avatar = avatar;
            return root;
        }

        private static Transform NewBone(Transform parent, string name)
        {
            GameObject bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            return bone.transform;
        }

        private static HumanDescription CreateHumanDescription(Transform root)
        {
            string[] names =
            {
                "Hips", "Spine", "Chest", "Neck", "Head",
                "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
                "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot"
            };
            var human = names.Select(name => new HumanBone { humanName = name, boneName = name }).ToArray();
            SkeletonBone[] skeleton = root.GetComponentsInChildren<Transform>(true).Select(transform => new SkeletonBone
            {
                name = transform.name,
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale
            }).ToArray();
            return new HumanDescription
            {
                human = human,
                skeleton = skeleton,
                upperArmTwist = .5f,
                lowerArmTwist = .5f,
                upperLegTwist = .5f,
                lowerLegTwist = .5f,
                armStretch = .05f,
                legStretch = .05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };
        }

        private static ShapeSyncVrmDatabaseRegistry LoadRegistry(string databasePath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<ShapeSyncVrmDatabaseRegistry>().Single();
        }

        private static void AddCanonicalFbm(string databasePath, string fbmName)
        {
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, intermediate, transaction) =>
            {
                ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure = database.Registry.BaseFigures.Single();
                SkinnedMeshRenderer canonicalRenderer = baseFigure.Figure.GetComponentInChildren<SkinnedMeshRenderer>(true);
                GameObject fbm = CreateRendererRoot(fbmName, canonicalRenderer.sharedMesh, canonicalRenderer.sharedMaterial);
                fbm.transform.SetParent(intermediate, false);
                ShapeSyncFigureImportRecord record = fbm.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(record.TryConfigure(fbm.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                    out string recordDiagnostic), Is.True, recordDiagnostic);
                ShapeSyncDatabaseRegistry.FigureAxisDraft draft = new ShapeSyncDatabaseRegistry.FigureAxisDraft(
                    fbmName, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm);
                Assert.That(database.Registry.TryAdmitFigureAxes(database, new[] { draft },
                    out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                Assert.That(database.Registry.TryCommitFigureAxes(database, admissions, new[] { fbm },
                    out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string diagnostic), Is.True, diagnostic);
        }

        private static void AddVrmMarkerWithoutRegistry(string databasePath)
        {
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                ShapeSyncDatabaseOptionalFeatureMarker marker = ShapeSyncDatabaseOptionalFeatureMarker.Create("VRM");
                context.AddSubAsset(marker);
            }, out string diagnostic), Is.True, diagnostic);
        }

        private static CanonicalFigure CreateCanonicalFigure(string databasePath, string figureName, string createFbm = null,
            string rendererName = null)
        {
            CanonicalFigure result = default;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, intermediate, transaction) =>
            {
                Mesh mesh = CreateTriangleMesh(figureName + "_CanonicalMesh");
                Material material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = figureName + "_CanonicalMaterial" };
                GameObject figure = CreateRendererRoot(figureName, mesh, material);
                if (!string.IsNullOrWhiteSpace(rendererName)) figure.transform.Find("Body").name = rendererName;
                transaction.AddSubAsset(mesh);
                transaction.AddSubAsset(material);
                figure.transform.SetParent(intermediate, false);
                Assert.That(database.Registry.TryRegisterBaseFigure(database, figureName, figure, out string registerDiagnostic), Is.True, registerDiagnostic);
                result = new CanonicalFigure(figure, mesh, material);

                if (!string.IsNullOrWhiteSpace(createFbm))
                {
                    GameObject fbm = CreateRendererRoot(createFbm, mesh, material);
                    fbm.transform.SetParent(intermediate, false);
                    ShapeSyncFigureImportRecord record = fbm.AddComponent<ShapeSyncFigureImportRecord>();
                    Assert.That(record.TryConfigure(fbm.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                        out string recordDiagnostic), Is.True, recordDiagnostic);
                    ShapeSyncDatabaseRegistry.FigureAxisDraft draft = new ShapeSyncDatabaseRegistry.FigureAxisDraft(
                        createFbm, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm);
                    Assert.That(database.Registry.TryAdmitFigureAxes(database, new[] { draft },
                        out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    Assert.That(database.Registry.TryCommitFigureAxes(database, admissions,
                        new[] { fbm },
                        out string commitDiagnostic), Is.True, commitDiagnostic);
                }
            }, out string diagnostic), Is.True, diagnostic);
            GameObject savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
            GameObject savedFigure = savedRoot.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + figureName).gameObject;
            return new CanonicalFigure(savedFigure, result.Mesh, result.Material);
        }

        private static CanonicalFigure CreateCanonicalFigureWithExternalMaterial(string databasePath, string figureName)
        {
            string materialPath = Root + "/" + figureName + "_ExternalMaterial.mat";
            Material externalMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"))
            {
                name = figureName + "_ExternalMaterial"
            };
            AssetDatabase.CreateAsset(externalMaterial, materialPath);

            CanonicalFigure result = default;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, intermediate, transaction) =>
            {
                Mesh mesh = CreateTriangleMesh(figureName + "_CanonicalMesh");
                GameObject figure = CreateRendererRoot(figureName, mesh, externalMaterial);
                transaction.AddSubAsset(mesh);
                figure.transform.SetParent(intermediate, false);
                Assert.That(database.Registry.TryRegisterBaseFigure(database, figureName, figure, out string registerDiagnostic), Is.True, registerDiagnostic);
                result = new CanonicalFigure(figure, mesh, externalMaterial);
            }, out string diagnostic), Is.True, diagnostic);
            GameObject savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
            GameObject savedFigure = savedRoot.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + figureName).gameObject;
            return new CanonicalFigure(savedFigure, result.Mesh, result.Material);
        }

        private static CanonicalFigure CreateHumanoidCanonicalFigure(string databasePath, string figureName, string createFbm)
        {
            CanonicalFigure result = default;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath,
                (database, intermediate, transaction) =>
                {
                    Mesh mesh = CreateTriangleMesh(figureName + "_CanonicalMesh");
                    Material material = new Material(Shader.Find("Hidden/InternalErrorShader"))
                    {
                        name = figureName + "_CanonicalMaterial"
                    };
                    GameObject figure = CreateHumanoidFigure(figureName, intermediate, transaction);
                    GameObject body = new GameObject("Body");
                    body.transform.SetParent(figure.transform, false);
                    SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    renderer.sharedMaterial = material;
                    renderer.rootBone = figure.transform.Find("Hips");
                    renderer.bones = new[] { renderer.rootBone };
                    transaction.AddSubAsset(mesh);
                    transaction.AddSubAsset(material);
                    Assert.That(database.Registry.TryRegisterBaseFigure(database, figureName, figure,
                        out string registerDiagnostic), Is.True, registerDiagnostic);
                    result = new CanonicalFigure(figure, mesh, material);

                    GameObject fbm = CreateRendererRoot(createFbm, mesh, material);
                    fbm.transform.SetParent(intermediate, false);
                    ShapeSyncFigureImportRecord record = fbm.AddComponent<ShapeSyncFigureImportRecord>();
                    Assert.That(record.TryConfigure(fbm.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                        out string recordDiagnostic), Is.True, recordDiagnostic);
                    var draft = new ShapeSyncDatabaseRegistry.FigureAxisDraft(
                        createFbm, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm);
                    Assert.That(database.Registry.TryAdmitFigureAxes(database, new[] { draft },
                        out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions,
                        out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    Assert.That(database.Registry.TryCommitFigureAxes(database, admissions, new[] { fbm },
                        out string commitDiagnostic), Is.True, commitDiagnostic);
                }, out string diagnostic), Is.True, diagnostic);
            GameObject savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
            GameObject savedFigure = savedRoot.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + figureName).gameObject;
            return new CanonicalFigure(savedFigure, result.Mesh, result.Material);
        }

        private static void DeleteSourceVrmAssets(SourceVrm source)
        {
            Assert.That(AssetDatabase.DeleteAsset(source.PrefabPath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(source.VrmPath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(source.ExpressionPath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(source.MeshPath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(source.MaterialPath), Is.True);
            Assert.That(AssetDatabase.DeleteAsset(source.TexturePath), Is.True);
        }

        private static void AssertDatabaseOwnedReference(GameObject referencePrefab,
            IReadOnlyList<UnityEngine.Object> ownedAssets)
        {
            Assert.That(referencePrefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(referencePrefab), Does.EndWith("Database.prefab"));
            Assert.That(ownedAssets, Is.Not.Null);
            Assert.That(ownedAssets.Any(value => value is Material || value is Texture), Is.False);
            Assert.That(ownedAssets.All(value => value != null && AssetDatabase.GetAssetPath(value).EndsWith("Database.prefab")), Is.True);
            Vrm10Instance instance = referencePrefab.GetComponentsInChildren<Vrm10Instance>(true).Single();
            Assert.That(AssetDatabase.GetAssetPath(instance.Vrm), Does.EndWith("Database.prefab"));
            Assert.That(instance.Vrm.Expression.Clips.All(value => value.Clip != null
                && AssetDatabase.GetAssetPath(value.Clip).EndsWith("Database.prefab")), Is.True);
        }

        private static CanonicalOutfit CreateCanonicalMeshOutfit(string databasePath, string identity)
        {
            CanonicalOutfit result = default;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, intermediate, transaction) =>
            {
                Mesh mesh = CreateTriangleMesh(identity + "_CanonicalMesh");
                Material material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = identity + "_CanonicalMaterial" };
                GameObject source = CreateRendererRoot(identity + "_Source", mesh, material);
                GameObject outfit = CreateRendererRoot(identity + "_Base", mesh, material);
                source.transform.SetParent(intermediate, false);
                outfit.transform.SetParent(intermediate, false);
                transaction.AddSubAsset(mesh);
                transaction.AddSubAsset(material);
                Assert.That(database.Registry.TryAddOutfit(identity, identity, ShapeSyncDatabaseRegistry.OutfitKind.Mesh,
                    out string addDiagnostic), Is.True, addDiagnostic);
                var entry = new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry("Base", source, null, outfit, null,
                    new[] { material.name });
                Assert.That(database.Registry.TrySetOutfitAxisFigures(database, identity, new[] { entry },
                    out string setDiagnostic), Is.True, setDiagnostic);
                result = new CanonicalOutfit(outfit, mesh, material);
            }, out string diagnostic), Is.True, diagnostic);
            GameObject savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
            GameObject savedOutfit = savedRoot.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + identity + "_Base").gameObject;
            return new CanonicalOutfit(savedOutfit, result.Mesh, result.Material);
        }

        private static GameObject CreateRendererRoot(string name, Mesh mesh, Material material)
        {
            GameObject root = new GameObject(name);
            GameObject body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            GameObject bone = new GameObject("Bone");
            bone.transform.SetParent(body.transform, false);
            SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.rootBone = bone.transform;
            renderer.bones = new[] { bone.transform };
            return root;
        }

        private static Mesh CreateTriangleMesh(string name)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[]
            {
                new BoneWeight { weight0 = 1f },
                new BoneWeight { weight0 = 1f },
                new BoneWeight { weight0 = 1f }
            };
            return mesh;
        }

        private static SourceVrm CreateSourceVrm(string name, string rendererName = null, Vector3[] happyDelta = null)
        {
            string prefabPath = Root + "/" + name + ".prefab";
            string vrmPath = Root + "/" + name + "_Vrm.asset";
            string expressionPath = Root + "/" + name + "_Happy.asset";
            string meshPath = Root + "/" + name + "_Mesh.asset";
            string materialPath = Root + "/" + name + "_Material.mat";
            string texturePath = Root + "/" + name + "_Texture.asset";
            VRM10Object vrm = ScriptableObject.CreateInstance<VRM10Object>();
            VRM10Expression expression = ScriptableObject.CreateInstance<VRM10Expression>();
            Mesh mesh = CreateTriangleMesh(name + "_SourceMesh");
            if (happyDelta != null)
            {
                if (happyDelta.Length != mesh.vertexCount) throw new ArgumentException("Happy delta topology mismatch.", nameof(happyDelta));
                mesh.AddBlendShapeFrame("Happy", 100f, happyDelta,
                    new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
            }
            Texture2D texture = new Texture2D(2, 2) { name = name + "_SourceTexture" };
            Material material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = name + "_SourceMaterial" };
            vrm.name = name + "_Vrm";
            expression.name = name + "_Happy";
            AssetDatabase.CreateAsset(vrm, vrmPath);
            AssetDatabase.CreateAsset(expression, expressionPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.CreateAsset(texture, texturePath);
            material.mainTexture = texture;
            AssetDatabase.CreateAsset(material, materialPath);
            vrm.Expression.Happy = expression;

            GameObject source = CreateRendererRoot(name, mesh, material);
            if (!string.IsNullOrWhiteSpace(rendererName)) source.transform.Find("Body").name = rendererName;
            if (happyDelta != null)
                expression.MorphTargetBindings = new[] { new MorphTargetBinding("Body", 0, 1f) };
            Vrm10Instance instance = source.AddComponent<Vrm10Instance>();
            instance.Vrm = vrm;
            Assert.That(PrefabUtility.SaveAsPrefabAsset(source, prefabPath), Is.Not.Null);
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            vrm.Prefab = savedPrefab;
            expression.Prefab = savedPrefab;
            EditorUtility.SetDirty(vrm);
            EditorUtility.SetDirty(expression);
            AssetDatabase.SaveAssets();
            UnityEngine.Object.DestroyImmediate(source);
            return new SourceVrm(prefabPath, vrmPath, expressionPath, meshPath, materialPath, texturePath,
                savedPrefab, mesh, material, texture);
        }

        private static void AddCustomExpressionToSource(SourceVrm source, string name, Vector3[] delta)
        {
            Assert.That(source.Mesh, Is.Not.Null);
            Assert.That(delta, Is.Not.Null);
            Assert.That(delta.Length, Is.EqualTo(source.Mesh.vertexCount));
            int existingIndex = source.Mesh.GetBlendShapeIndex(name);
            Assert.That(existingIndex, Is.EqualTo(-1));
            source.Mesh.AddBlendShapeFrame(name, 100f, delta,
                new Vector3[source.Mesh.vertexCount], new Vector3[source.Mesh.vertexCount]);

            VRM10Expression expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = name;
            expression.Prefab = source.Prefab;
            expression.MorphTargetBindings = new[]
            {
                new MorphTargetBinding("Body", source.Mesh.GetBlendShapeIndex(name), 1f)
            };
            VRM10Object vrm = AssetDatabase.LoadAssetAtPath<VRM10Object>(source.VrmPath);
            Assert.That(vrm, Is.Not.Null);
            AssetDatabase.AddObjectToAsset(expression, vrm);
            vrm.Expression.AddClip(ExpressionPreset.custom, expression);
            EditorUtility.SetDirty(source.Mesh);
            EditorUtility.SetDirty(vrm);
            EditorUtility.SetDirty(expression);
            AssetDatabase.SaveAssets();
        }

        private static void AddSpringBoneToSource(string prefabPath)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Vrm10Instance instance = contents.GetComponent<Vrm10Instance>();
                Assert.That(instance, Is.Not.Null);
                Transform jointTransform = CreateCompatiblePhysicsJoint(contents);
                VRM10SpringBoneJoint joint = jointTransform.gameObject.AddComponent<VRM10SpringBoneJoint>();
                joint.m_stiffnessForce = 0.7f;
                joint.m_gravityPower = 0.2f;
                Transform colliderTransform = contents.transform.Find("Body");
                VRM10SpringBoneCollider collider = colliderTransform.gameObject.AddComponent<VRM10SpringBoneCollider>();
                collider.Radius = 0.05f;
                VRM10SpringBoneColliderGroup group = colliderTransform.gameObject.AddComponent<VRM10SpringBoneColliderGroup>();
                group.Name = "SourceCollider";
                group.Colliders = new List<VRM10SpringBoneCollider> { collider };
                instance.SpringBone = new Vrm10InstanceSpringBone
                {
                    ColliderGroups = new List<VRM10SpringBoneColliderGroup> { group },
                    Springs = new List<Vrm10InstanceSpringBone.Spring>()
                };
                var spring = new Vrm10InstanceSpringBone.Spring("SourceSpring");
                spring.Joints.Add(joint);
                spring.ColliderGroups.Add(group);
                instance.SpringBone.Springs.Add(spring);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved), Is.Not.Null);
                Assert.That(saved, Is.True);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Transform CreateCompatiblePhysicsJoint(GameObject contents)
        {
            GameObject hips = new GameObject("Hips");
            hips.transform.SetParent(contents.transform, false);
            GameObject spine = new GameObject("Spine");
            spine.transform.SetParent(hips.transform, false);
            return spine.transform;
        }

        private static void AddExtraBoneSpringBoneToSource(string prefabPath)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Vrm10Instance instance = contents.GetComponent<Vrm10Instance>();
                Assert.That(instance, Is.Not.Null);
                GameObject extraBone = new GameObject("ExtraBone");
                extraBone.transform.SetParent(contents.transform, false);
                GameObject tip = new GameObject("Tip");
                tip.transform.SetParent(extraBone.transform, false);
                VRM10SpringBoneJoint joint = tip.AddComponent<VRM10SpringBoneJoint>();

                Transform colliderTransform = contents.transform.Find("Body");
                VRM10SpringBoneCollider collider = colliderTransform.gameObject.AddComponent<VRM10SpringBoneCollider>();
                VRM10SpringBoneColliderGroup group = colliderTransform.gameObject.AddComponent<VRM10SpringBoneColliderGroup>();
                group.Name = "SourceCollider";
                group.Colliders = new List<VRM10SpringBoneCollider> { collider };
                instance.SpringBone = new Vrm10InstanceSpringBone
                {
                    ColliderGroups = new List<VRM10SpringBoneColliderGroup> { group },
                    Springs = new List<Vrm10InstanceSpringBone.Spring>()
                };
                var spring = new Vrm10InstanceSpringBone.Spring("ExtraBoneSpring");
                spring.Joints.Add(joint);
                spring.ColliderGroups.Add(group);
                instance.SpringBone.Springs.Add(spring);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved), Is.Not.Null);
                Assert.That(saved, Is.True);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void AddSharedTransformMultiColliderSpringBoneToSource(string prefabPath)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Vrm10Instance instance = contents.GetComponent<Vrm10Instance>();
                Assert.That(instance, Is.Not.Null);
                Transform jointTransform = CreateCompatiblePhysicsJoint(contents);
                VRM10SpringBoneJoint joint = jointTransform.gameObject.AddComponent<VRM10SpringBoneJoint>();
                joint.m_stiffnessForce = 0.7f;

                GameObject secondary = new GameObject("secondary");
                secondary.transform.SetParent(contents.transform, false);
                var groups = new List<VRM10SpringBoneColliderGroup>();
                for (int groupIndex = 0; groupIndex < 2; groupIndex++)
                {
                    var colliders = new List<VRM10SpringBoneCollider>();
                    for (int colliderIndex = 0; colliderIndex < 2; colliderIndex++)
                    {
                        VRM10SpringBoneCollider collider = secondary.AddComponent<VRM10SpringBoneCollider>();
                        collider.Radius = 0.05f + colliderIndex * 0.01f;
                        colliders.Add(collider);
                    }

                    VRM10SpringBoneColliderGroup group = secondary.AddComponent<VRM10SpringBoneColliderGroup>();
                    group.Name = "SharedGroup" + groupIndex;
                    group.Colliders = colliders;
                    groups.Add(group);
                }

                instance.SpringBone = new Vrm10InstanceSpringBone
                {
                    ColliderGroups = groups,
                    Springs = new List<Vrm10InstanceSpringBone.Spring>()
                };
                var spring = new Vrm10InstanceSpringBone.Spring("SharedSpring");
                spring.Joints.Add(joint);
                spring.ColliderGroups.Add(groups[0]);
                spring.ColliderGroups.Add(groups[1]);
                instance.SpringBone.Springs.Add(spring);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved), Is.Not.Null);
                Assert.That(saved, Is.True);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private readonly struct CanonicalFigure
        {
            public CanonicalFigure(GameObject root, Mesh mesh, Material material) { Root = root; Mesh = mesh; Material = material; }
            public GameObject Root { get; }
            public Mesh Mesh { get; }
            public Material Material { get; }
        }

        private readonly struct CanonicalOutfit
        {
            public CanonicalOutfit(GameObject root, Mesh mesh, Material material) { Root = root; Mesh = mesh; Material = material; }
            public GameObject Root { get; }
            public Mesh Mesh { get; }
            public Material Material { get; }
        }

        private readonly struct SourceVrm
        {
            public SourceVrm(string prefabPath, string vrmPath, string expressionPath, string meshPath,
                string materialPath, string texturePath, GameObject prefab, Mesh mesh, Material material, Texture2D texture)
            {
                PrefabPath = prefabPath;
                VrmPath = vrmPath;
                ExpressionPath = expressionPath;
                MeshPath = meshPath;
                MaterialPath = materialPath;
                TexturePath = texturePath;
                Prefab = prefab;
                Mesh = mesh;
                Material = material;
                Texture = texture;
            }
            public string PrefabPath { get; }
            public string VrmPath { get; }
            public string ExpressionPath { get; }
            public string MeshPath { get; }
            public string MaterialPath { get; }
            public string TexturePath { get; }
            public GameObject Prefab { get; }
            public Mesh Mesh { get; }
            public Material Material { get; }
            public Texture2D Texture { get; }
        }
    }
}
#endif
