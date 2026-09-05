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
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncMeshOutfitImportTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20MeshOutfitImportRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_7_ShapeSyncMeshOutfitImportTests"); }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void TryImportBase_MergesPersistentMeshOutfitPreservesAnimatorAndKeepsSourceUntouched()
        {
            const string sourcePath = Root + "/CoatSource.prefab";
            const string databasePath = Root + "/Database.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryValidateAxisSource(null, out string nullSourceDiagnostic), Is.False);
            Assert.That(nullSourceDiagnostic, Does.Contain("persistent source Prefab"));
            Assert.That(ShapeSyncMeshOutfitImport.TryValidateAxisSource(source, out string sourceAdmissionDiagnostic), Is.True, sourceAdmissionDiagnostic);
            Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);

            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = CreateValidImportedFigure(intermediate, "Master", transaction);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash), "Mesh Outfit import must not modify its source Prefab.");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);

            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis = reopened.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures.Single();
            Assert.That(axis.ShapeKey, Is.EqualTo(ShapeSyncDatabaseRegistry.BaseShapeKey));
            Assert.That(axis.SourcePrefab.name, Is.EqualTo("Coat_Master_Source"));
            Assert.That(axis.MergedPrefab.name, Is.EqualTo("Coat_Master_Merged"));
            Assert.That(axis.OutfitPrefab.name, Is.EqualTo("Coat_Master"));
            Assert.That(axis.ProjectionPrefab, Is.Null);
            foreach (GameObject artifact in new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab })
            {
                Assert.That(artifact.transform.parent, Is.SameAs(reopened.transform.Find("Intermediate")));
                Assert.That(artifact.GetComponentsInChildren<Animator>(true), Has.Length.EqualTo(1), "Outfit import preserves the source Animator/Avatar resolution path; Generate owns later component removal.");
                Assert.That(artifact.GetComponentsInChildren<Transform>(true).Any(transform => transform.name == "ExtraBone"), Is.True, "Outfit-only bones are part of the imported Outfit hierarchy and must not be stripped with Animator/Avatar.");
                SkinnedMeshRenderer renderer = artifact.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(databasePath));
                Assert.That(renderer.sharedMaterials.All(material => material != null && AssetDatabase.GetAssetPath(material) == databasePath), Is.True);
            }
            string sourceMaterialName = axis.SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection, null)
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out reopened, out openDiagnostic), Is.True, openDiagnostic);
            axis = reopened.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures.Single();
            Assert.That(axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials.All(material => material == null), Is.True, "Include-only Outfit artifact must omit Projection Materials.");
            Assert.That(axis.ProjectionPrefab, Is.Not.Null);
            Assert.That(axis.ProjectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials.All(material => material == null), Is.True, "Projection carries geometry only; its Material and Texture payload is not retained.");
            Assert.That(axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null);
            Assert.That(axis.ProjectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh), Is.EqualTo(databasePath));
            Assert.That(AssetDatabase.GetAssetPath(axis.ProjectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh), Is.EqualTo(databasePath));
            Assert.That(axis.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials.All(material => material == null), Is.True,
                "Classification removes the source artifact Material / Texture payload from the Database.");
            Assert.That(axis.MergedPrefab, Is.Null, "Merged Prefab is an import-time artifact and is removed after classification Save.");
            Transform intermediate = reopened.transform.Find("Intermediate");
            Assert.That(intermediate.Cast<Transform>().Count(child => child != null && child.name == "Coat_Master"), Is.EqualTo(1),
                "Classification replacement must not leave an empty same-named Outfit prefab orphan in Intermediate.");
            Assert.That(intermediate.Cast<Transform>().Count(child => child != null && child.name == "Coat_Master_Merged"), Is.EqualTo(0),
                "Classification Save must remove the import-time Merged prefab from Intermediate.");
            GameObject persistedOutfit = intermediate.Cast<Transform>().Single(child => child != null && child.name == "Coat_Master").gameObject;
            Assert.That(persistedOutfit.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null,
                "The surviving same-named Outfit prefab must retain its derived Mesh.");
            Assert.That(reopened.Registry.TextureResources, Is.Empty, "Projection creates neither Material Entries nor Texture resources.");
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Again")
            }, out string fixedDiagnostic), Is.False);
            Assert.That(fixedDiagnostic, Does.Contain("fixed after Save"));
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string reimportDiagnostic), Is.False);
            Assert.That(reimportDiagnostic, Does.Contain("fixed after Material classification Save"));
        }

        [Test]
        public void TryImportBase_SaveFailureDoesNotDestroyStagedPersistentMergeMesh()
        {
            const string sourcePath = Root + "/RollbackSource.prefab";
            const string databasePath = Root + "/RollbackDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = CreateValidImportedFigure(intermediate, "Master", transaction);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            string saveDiagnostic;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                LogAssert.NoUnexpectedReceived();
                Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out saveDiagnostic), Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave; }

            Assert.That(saveDiagnostic, Does.Contain("could not be saved"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rolledBack, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(rolledBack.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures, Is.Empty,
                "A failed import must not publish a partial axis entry.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>()
                .Where(mesh => mesh.name.Contains("Coat_Master", StringComparison.Ordinal)), Is.Empty,
                "Rollback must reclaim staged Outfit merge Mesh sub-assets; Result.Dispose must not destroy them directly.");
        }

        [Test]
        public void TryImportBase_ReimportSweepsStaleDuplicateNameDuringReplacement()
        {
            const string sourcePath = Root + "/DuplicateNameSource.prefab";
            const string databasePath = Root + "/DuplicateNameDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master");
                baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string firstImportDiagnostic), Is.True, firstImportDiagnostic);
            const string staleMaterialName = "Coat_Master_StaleMaterial";
            const string staleTextureName = "Coat_Master_StaleTexture";
            const string staleAdapterName = "Coat_Master_StaleAdapter";
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                // Reproduce stale artifacts left by an earlier failed/retried import.
                // Both an empty placeholder and a valid-but-unreferenced artifact
                // must be reclaimed by the same Outfit-prefix sweep.
                GameObject stale = new GameObject("Coat_Master");
                stale.AddComponent<SkinnedMeshRenderer>();
                stale.transform.SetParent(intermediate, false);
                Mesh staleMesh = new Mesh { name = "Coat_Master_StaleMesh" };
                Texture2D staleTexture = new Texture2D(2, 2) { name = staleTextureName };
                Material staleMaterial = new Material(Shader.Find("Standard")) { name = staleMaterialName };
                staleMaterial.mainTexture = staleTexture;
                MaterialShaderAdapter staleAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                staleAdapter.name = staleAdapterName;
                transaction.AddSubAsset(staleMesh);
                transaction.AddSubAsset(staleTexture);
                transaction.AddSubAsset(staleMaterial);
                transaction.AddSubAsset(staleAdapter);
                GameObject validStale = new GameObject("Coat_Master_StaleValid");
                validStale.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer validRenderer = validStale.AddComponent<SkinnedMeshRenderer>();
                validRenderer.sharedMesh = staleMesh;
                validRenderer.sharedMaterials = new[] { staleMaterial };
            }, out string staleSubAssetDiagnostic), Is.True, staleSubAssetDiagnostic);

            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string reimportDiagnostic), Is.True, reimportDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis = reopened.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures.Single();
            SkinnedMeshRenderer renderer = axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(reopened.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName).Cast<Transform>()
                .Count(child => child.name == "Coat_Master"), Is.EqualTo(1),
                "Overwrite re-registration must sweep the stale same-name hierarchy artifact before staging the replacement.");
            Assert.That(reopened.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName).Cast<Transform>()
                .Any(child => child.name == "Coat_Master_StaleValid"), Is.False,
                "Overwrite re-registration must remove valid-but-unreferenced Outfit-prefix hierarchy artifacts.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath)
                .Any(asset => asset != null && (asset.name == "Coat_Master_StaleMesh"
                    || asset.name == staleMaterialName
                    || asset.name == staleTextureName
                    || asset.name == staleAdapterName)), Is.False,
                "Overwrite re-registration must sweep stale Outfit-prefix Mesh, Material, Texture, and Adapter sub-assets.");
            Assert.That(renderer.sharedMesh, Is.Not.Null, "Reimport must retain the newly staged Outfit Mesh after stale cleanup.");
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(databasePath));
        }

        [Test]
        public void TryImportAxes_ReimportSweepsStaleFbmArtifactsDuringReplacement()
        {
            const string sourcePath = Root + "/FbmDuplicateNameSource.prefab";
            const string databasePath = Root + "/FbmDuplicateNameDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string baseImportDiagnostic), Is.True, "Base import: " + baseImportDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat",
                new[] { new KeyValuePair<string, GameObject>("Tall", source) }, out string firstFbmDiagnostic), Is.True, "First FBM import: " + firstFbmDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject stale = new GameObject("Coat_Tall");
                stale.transform.SetParent(intermediate, false);
                transaction.AddSubAsset(new Mesh { name = "Coat_Tall_StaleMesh" });
            }, out string staleDiagnostic), Is.True, staleDiagnostic);

            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat",
                new[] { new KeyValuePair<string, GameObject>("Tall", source) }, out string reimportDiagnostic), Is.True, "FBM reimport: " + reimportDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis = reopened.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures.Single(entry => entry.ShapeKey == "Tall");
            Transform reopenedIntermediate = reopened.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            string childNames = reopenedIntermediate == null ? "<missing>" : string.Join(",", reopenedIntermediate.Cast<Transform>().Select(child => child.name));
            Assert.That(reopenedIntermediate.Cast<Transform>()
                .Count(child => child.name == "Coat_Tall"), Is.EqualTo(1),
                "Axis prefab=" + (axis.OutfitPrefab == null ? "<null>" : axis.OutfitPrefab.name) + "; children=" + childNames);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath)
                .Any(asset => asset != null && asset.name == "Coat_Tall_StaleMesh"), Is.False);
            Assert.That(axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null);
        }

        [Test]
        public void TryImportAxes_ReimportPreservesCollectionDeclarationAndArtifacts()
        {
            const string sourcePath = Root + "/FbmReimportInvalidatesDerivedSource.prefab";
            const string databasePath = Root + "/FbmReimportInvalidatesDerivedDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "FbmReimportInvalidatesDerived");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone,
                false, new[]
                {
                    new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                    new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", source)
                }, out string collectionDiagnostic), Is.True, collectionDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase before, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(before.Registry.Outfits.Single(entry => entry.Identity == "Coat").CollectionKind,
                Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone));

            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string reimportDiagnostic), Is.True, reimportDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase after, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = after.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            Assert.That(outfit.CollectionKind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone));
            Assert.That(outfit.CollectionEntries, Has.Count.EqualTo(2));
            AssertCollectionArtifactsPresent(after, "Coat");
        }

        [Test]
        public void MaterialClassification_CanBeSavedBeforeFbmSourcesAndAppliesToLaterFbmImport()
        {
            const string sourcePath = Root + "/ClassificationBeforeFbmSource.prefab";
            const string databasePath = Root + "/ClassificationBeforeFbmDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string addDiagnostic), Is.True, addDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = opened.Registry.Outfits.Single(entry => entry.Identity == "Coat").AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase classified, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(classified.Registry.Outfits.Single().MaterialEntries, Has.Count.EqualTo(1),
                "Classification must persist the Base canonical Material Entry before a later FBM import.");
            Assert.That(classified.Registry.Outfits.Single().MaterialEntries.Single().Material, Is.Not.Null,
                "Classification canonical Material Entry must retain its Database sub-asset reference.");
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = opened.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            Assert.That(outfit.AxisFigures.Select(axis => axis.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            Assert.That(outfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall").MergedPrefab, Is.Null);
            Assert.That(outfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall").OutfitPrefab, Is.Not.Null);
        }

        [Test]
        public void MaterialClassification_PersistsIncludeOnlyEntryNamesAndRejectsNamesOutsideTheirClassification()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(Root + "/Classification.prefab", out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string path = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master");
                baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetOutfitMaterialClassifications("Coat", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry("Shirt", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Top"),
                    new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry("Body", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection, null),
                    new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry("Hidden", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude, null)
                }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(path, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = reopened.Registry.Outfits.Single();
            Assert.That(outfit.MaterialClassifications.Select(entry => entry.SourceMaterialName), Is.EqualTo(new[] { "Shirt", "Body", "Hidden" }));
            Assert.That(outfit.MaterialClassifications.Single(entry => entry.SourceMaterialName == "Shirt").EntryName, Is.EqualTo("Top"));
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                Assert.That(contents.Registry.TrySetOutfitMaterialClassifications("Coat", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry("Body", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection, "MustNotExist")
                }, out string rejectedDiagnostic), Is.False);
                Assert.That(rejectedDiagnostic, Does.Contain("fixed after Save"));
            }, out string rejectTransactionDiagnostic), Is.True, rejectTransactionDiagnostic);
        }

        [Test]
        public void IncludeClassification_PersistsOutfitLocalMaterialEntryAndAdapter()
        {
            const string sourcePath = Root + "/IncludeSource.prefab";
            const string databasePath = Root + "/IncludeDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath, additionalMaterialNames: new[] { "CoatMaterialSecond" });
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            string[] sourceMaterialNames = opened.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntrySecond")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitMaterialEntry[] entries = opened.Registry.Outfits.Single().MaterialEntries.ToArray();
            Assert.That(entries.Select(entry => entry.LogicalName), Is.EquivalentTo(new[] { "CoatEntry", "CoatEntrySecond" }));
            Assert.That(entries.All(entry => entry.Material != null && entry.Adapter != null), Is.True);
            Assert.That(entries.Select(entry => entry.Adapter).Distinct().Count(), Is.EqualTo(1),
                "One Database Material Adapter instance must be shared by all Outfit Materials using the same shader adapter type.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<MaterialShaderAdapter>().Count(), Is.EqualTo(1),
                "The Database must not retain an unreferenced duplicate Material Adapter sub-asset.");
        }

        [Test]
        public void AdapterCanonicalization_FigureAndMultipleOutfitsShareOneDatabaseAdapter()
        {
            const string databasePath = Root + "/AdapterCanonicalization.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, intermediate, transaction) =>
            {
                GameObject figure = new GameObject("Base");
                figure.transform.SetParent(intermediate, false);
                Material figureMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Base_Material" };
                SkinnedMeshRenderer figureRenderer = figure.AddComponent<SkinnedMeshRenderer>();
                figureRenderer.sharedMaterial = figureMaterial;
                MaterialShaderAdapter figureAdapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
                MaterialShaderAdapter firstOutfitAdapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
                MaterialShaderAdapter secondOutfitAdapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
                transaction.AddSubAsset(figureMaterial);
                transaction.AddSubAsset(figureAdapter);
                transaction.AddSubAsset(firstOutfitAdapter);
                transaction.AddSubAsset(secondOutfitAdapter);
                Assert.That(database.Registry.TryRegisterBaseFigure(database, "Base", figure, out string figureDiagnostic), Is.True, figureDiagnostic);
                Assert.That(database.Registry.TryRegisterMaterialEntry(database, "Body", figureRenderer, 0, figureMaterial.name, figureMaterial, figureAdapter, out string materialDiagnostic), Is.True, materialDiagnostic);
                Assert.That(database.Registry.TryAddOutfit("OutfitA", "OutfitA", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string firstOutfitDiagnostic), Is.True, firstOutfitDiagnostic);
                Assert.That(database.Registry.TryAddOutfit("OutfitB", "OutfitB", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string secondOutfitDiagnostic), Is.True, secondOutfitDiagnostic);
                database.Registry.Outfits.Single(entry => entry.Identity == "OutfitA").SetMaterialEntries(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("BodyA", figureMaterial, firstOutfitAdapter)
                });
                database.Registry.Outfits.Single(entry => entry.Identity == "OutfitB").SetMaterialEntries(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("BodyB", figureMaterial, secondOutfitAdapter)
                });
                Dictionary<Type, MaterialShaderAdapter> cache = ShapeSyncMaterialAdapterResolver.CreateDatabaseAdapterCache(database.Registry);
                ShapeSyncMaterialAdapterResolver.CanonicalizeDatabaseAdapters(database, transaction, databasePath, cache);
            }, out string saveDiagnostic), Is.True, saveDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            MaterialShaderAdapter canonical = reopened.Registry.MaterialEntries.Single().Adapter;
            Assert.That(reopened.Registry.Outfits.SelectMany(entry => entry.MaterialEntries).Select(entry => entry.Adapter), Is.All.SameAs(canonical));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<MaterialShaderAdapter>().Count(), Is.EqualTo(1),
                "Figure plus multiple Outfit registrations must canonicalize one Adapter sub-asset per shader Adapter type.");
        }

        [Test]
        public void MaterialOutfitTextureEntries_PersistWithOutfitOwnerAndRejectForeignOwner()
        {
            const string databasePath = Root + "/MaterialOutfitTextures.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, transaction) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                Texture2D owned = new Texture2D(1, 1) { name = "Skin_Albedo" };
                transaction.AddSubAsset(owned);
                Assert.That(contents.Registry.TryRegisterTextureResource("Skin_Albedo", owned,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Skin"), ShapeSyncDatabaseRegistry.TextureResourceUsage.MaterialOutfit, out string resourceDiagnostic), Is.True, resourceDiagnostic);
                Assert.That(contents.Registry.TrySetMaterialOutfitTextureEntries("Skin", new[]
                {
                    new ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry("Albedo", "Skin_Albedo")
                }, out string entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TrySetMaterialOutfitTextureEntries("Skin", new[]
                {
                    new ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry("Albedo", "Skin_Albedo")
                }, out entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Foreign", "Foreign", ShapeSyncDatabaseRegistry.OutfitKind.Material, out outfitDiagnostic), Is.True, outfitDiagnostic);
                Texture2D foreign = new Texture2D(1, 1) { name = "Foreign_Albedo" };
                transaction.AddSubAsset(foreign);
                Assert.That(contents.Registry.TryRegisterTextureResource("Foreign_Albedo", foreign,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Foreign"), ShapeSyncDatabaseRegistry.TextureResourceUsage.MaterialOutfit, out resourceDiagnostic), Is.True, resourceDiagnostic);
                Assert.That(contents.Registry.TrySetMaterialOutfitTextureEntries("Skin", new[]
                {
                    new ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry("Foreign", "Foreign_Albedo")
                }, out entryDiagnostic), Is.False);
                Assert.That(entryDiagnostic, Does.Contain("owner"));
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = reopened.Registry.Outfits.Single(entry => entry.Identity == "Skin");
            Assert.That(outfit.MaterialOutfitTextureEntries.Single().TextureResourceName, Is.EqualTo("Skin_Albedo"));
            Assert.That(reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "Skin_Albedo").Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit));
            Assert.That(reopened.Registry.TrySetFigureMaskEntries("Skin", new[]
            {
                new ShapeSyncDatabaseRegistry.FigureMaskEntry("MissingFigureEntry", "Skin_Albedo")
            }, out string maskDiagnostic), Is.False);
            Assert.That(maskDiagnostic, Does.Contain("Mesh Outfit was not found"));
        }

        [Test]
        public void MaterialOutfitTextureAuthoring_CopiesAndRemovesOnlyItsOwnedAbstractTexture()
        {
            const string databasePath = Root + "/MaterialOutfitTextureAuthoring.prefab";
            Texture source = CreatePersistentTexture(Root + "/SkinSource.asset", "SkinSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.MaterialTextureInput("Albedo", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.TextureResourceEntry resource = opened.Registry.TextureResources.Single();
            Assert.That(resource.LogicalName, Is.EqualTo("Skin_Albedo"));
            Assert.That(resource.Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit));
            Assert.That(resource.Owner.OutfitIdentity, Is.EqualTo("Skin"));
            Assert.That(resource.Usage, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceUsage.MaterialOutfit));
            Assert.That(AssetDatabase.GetAssetPath(resource.Texture), Is.EqualTo(databasePath));
            Assert.That(opened.Registry.Outfits.Single().MaterialOutfitTextureEntries.Single().EntryName, Is.EqualTo("Albedo"));

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRenameTextureResource("Skin_Albedo", "Skin_CustomAlbedo", out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string renameSaveDiagnostic), Is.True, renameSaveDiagnostic);
            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.MaterialTextureInput("Albedo", source)
            }, out saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().MaterialOutfitTextureEntries.Single().TextureResourceName, Is.EqualTo("Skin_CustomAlbedo"));
            Assert.That(opened.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Skin_CustomAlbedo" }));

            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", Array.Empty<ShapeSyncOutfitTextureAuthoring.MaterialTextureInput>(), out saveDiagnostic), Is.False);
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().MaterialOutfitTextureEntries.Single().TextureResourceName, Is.EqualTo("Skin_CustomAlbedo"));
            Assert.That(opened.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Skin_CustomAlbedo" }));

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", Array.Empty<ShapeSyncOutfitTextureAuthoring.MaterialTextureInput>(), out saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.TextureResources, Is.Empty);
            Assert.That(opened.Registry.Outfits.Single().MaterialOutfitTextureEntries, Is.Empty);
        }

        [Test]
        public void MaterialOutfitTextureAuthoring_CopiesNonReadableImportedTextureWithoutChangingSourceImporter()
        {
            const string pngPath = Root + "/NonReadableSource.png";
            const string databasePath = Root + "/NonReadableMaterialOutfit.prefab";
            var sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            sourceTexture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            sourceTexture.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), pngPath.Replace('/', Path.DirectorySeparatorChar)), sourceTexture.EncodeToPNG());
            Object.DestroyImmediate(sourceTexture);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.isReadable = false;
            importer.SaveAndReimport();
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.isReadable, Is.False);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.MaterialTextureInput("Albedo", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(pngPath)).isReadable, Is.False,
                "Material Outfit save must not mutate the source importer Read/Write setting.");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Texture persisted = opened.Registry.TextureResources.Single().Texture;
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted.width, Is.EqualTo(source.width));
            Assert.That(persisted.height, Is.EqualTo(source.height));
            Assert.That(AssetDatabase.GetAssetPath(persisted), Is.EqualTo(databasePath));
        }

        [Test]
        public void MaterialOutfitWindowDraft_MarksDirtyAndSavesThroughTheAuthoringTransaction()
        {
            const string databasePath = Root + "/MaterialOutfitWindow.prefab";
            Texture source = CreatePersistentTexture(Root + "/MaterialOutfitWindowSource.asset", "MaterialOutfitWindowSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Skin"), Is.True);
                Assert.That(window.TryAddMaterialOutfitTextureDraftForTest("Albedo", source), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(window.Database.Registry.Outfits.Single().MaterialOutfitTextureEntries.Single().EntryName, Is.EqualTo("Albedo"));
                Assert.That(window.TrySelectOutfitForTest("Skin"), Is.True);
                Assert.That(window.TryRenameMaterialOutfitTextureDraftForTest("Albedo", "RenamedAlbedo"), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.Database.Registry.Outfits.Single().MaterialOutfitTextureEntries.Single().EntryName, Is.EqualTo("RenamedAlbedo"));
                Assert.That(window.Database.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Skin_RenamedAlbedo" }));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void MaterialOutfitWindowDraft_LastRemovalStaysEmptyUntilSave()
        {
            const string databasePath = Root + "/MaterialOutfitLastRemoval.prefab";
            Texture source = CreatePersistentTexture(Root + "/MaterialOutfitLastRemovalSource.asset", "MaterialOutfitLastRemovalSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(databasePath, "Skin", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.MaterialTextureInput("Albedo", source)
            }, out string initialSaveDiagnostic), Is.True, initialSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Skin"), Is.True);
                Assert.That(window.MaterialOutfitTextureDraftNamesForTest, Is.EqualTo(new[] { "Albedo" }));
                Assert.That(window.TryRemoveMaterialOutfitTextureDraftForTest("Albedo"), Is.True);
                Assert.That(window.MaterialOutfitTextureDraftNamesForTest, Is.Empty,
                    "Removing the last Material Outfit Texture must not rehydrate the saved entry into the draft.");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.MaterialOutfitTextureDraftNamesForTest, Is.Empty);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(removed.Registry.Outfits.Single().MaterialOutfitTextureEntries, Is.Empty);
            Assert.That(removed.Registry.TextureResources, Is.Empty);
        }

        [Test]
        public void FigureMaskAuthoring_PersistsTargetsRejectsRemovalAndPropagatesTextureRename()
        {
            const string databasePath = Root + "/FigureMaskAuthoring.prefab";
            Texture source = CreatePersistentTexture(Root + "/MaskSource.asset", "MaskSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Body_Material" };
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                context.AddSubAsset(material);
                context.AddSubAsset(adapter);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryRegisterMaterialEntry(contents, "Body", renderer, 0, material.name, material, adapter, out string entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveFigureMasks(databasePath, "Coat", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.FigureMaskInput("Body", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = opened.Registry.Outfits.Single();
            ShapeSyncDatabaseRegistry.FigureMaskEntry mask = outfit.FigureMaskEntries.Single();
            Assert.That(mask.FigureMaterialEntryName, Is.EqualTo("Body"));
            Assert.That(mask.TextureResourceName, Is.EqualTo("Coat_Body_Mask"));
            ShapeSyncDatabaseRegistry.TextureResourceEntry resource = opened.Registry.TextureResources.Single();
            Assert.That(resource.Owner.OutfitIdentity, Is.EqualTo("Coat"));
            Assert.That(resource.Usage, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceUsage.FigureMask));
            Assert.That(opened.Registry.TryRemoveTextureResource(resource.LogicalName, out _, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic removalDiagnostic), Is.False);
            Assert.That(removalDiagnostic.Code, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceDiagnosticCode.ReferencedByFigureMask));
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRenameTextureResource("Coat_Body_Mask", "Coat_Body_Coverage", out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string renameSaveDiagnostic), Is.True, renameSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            outfit = opened.Registry.Outfits.Single();
            Assert.That(outfit.FigureMaskEntries.Single().TextureResourceName, Is.EqualTo("Coat_Body_Coverage"));

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveFigureMasks(databasePath, "Coat", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.FigureMaskInput("Body", source)
            }, out saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().FigureMaskEntries.Single().TextureResourceName, Is.EqualTo("Coat_Body_Coverage"));
            Assert.That(opened.Registry.TextureResources.Select(entry => entry.LogicalName), Is.EqualTo(new[] { "Coat_Body_Coverage" }));

            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveFigureMasks(databasePath, "Coat", Array.Empty<ShapeSyncOutfitTextureAuthoring.FigureMaskInput>(), out saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().FigureMaskEntries, Is.Empty);
            Assert.That(opened.Registry.TextureResources, Is.Empty);
        }

        [Test]
        public void FigureMaskWindowDraft_MarksDirtyRetainsOnCancelAndSavesThroughTheAuthoringTransaction()
        {
            const string databasePath = Root + "/FigureMaskWindow.prefab";
            Texture source = CreatePersistentTexture(Root + "/FigureMaskWindowSource.asset", "FigureMaskWindowSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Body_Material" };
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                context.AddSubAsset(material);
                context.AddSubAsset(adapter);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryRegisterMaterialEntry(contents, "Body", renderer, 0, material.name, material, adapter, out string entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "Figure Mask"), Is.True);
                Assert.That(window.TryAddFigureMaskDraftForTest("Body", source), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(window.Database.Registry.Outfits.Single().FigureMaskEntries.Single().FigureMaterialEntryName, Is.EqualTo("Body"));
            }
            finally
            {
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog;
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FigureMaskWindowDraft_LastRemovalStaysEmptyUntilSave()
        {
            const string databasePath = Root + "/FigureMaskLastRemoval.prefab";
            Texture source = CreatePersistentTexture(Root + "/FigureMaskLastRemovalSource.asset", "FigureMaskLastRemovalSource");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, context) =>
            {
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Body_Material" };
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                context.AddSubAsset(material);
                context.AddSubAsset(adapter);
                SkinnedMeshRenderer renderer = baseFigure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryRegisterMaterialEntry(contents, "Body", renderer, 0, material.name, material, adapter, out string entryDiagnostic), Is.True, entryDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncOutfitTextureAuthoring.TrySaveFigureMasks(databasePath, "Coat", new[]
            {
                new ShapeSyncOutfitTextureAuthoring.FigureMaskInput("Body", source)
            }, out string initialSaveDiagnostic), Is.True, initialSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "Figure Mask"), Is.True);
                Assert.That(window.FigureMaskDraftNamesForTest, Is.EqualTo(new[] { "Body" }));
                Assert.That(window.TryRemoveFigureMaskDraftForTest("Body"), Is.True);
                Assert.That(window.FigureMaskDraftNamesForTest, Is.Empty,
                    "Removing the last Figure Mask must not rehydrate the saved entry into the draft.");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.FigureMaskDraftNamesForTest, Is.Empty);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(removed.Registry.Outfits.Single().FigureMaskEntries, Is.Empty);
            Assert.That(removed.Registry.TextureResources, Is.Empty);
        }

        [Test]
        public void MixedClassification_PersistsOnlyIncludedSubMeshGeometryAndMaterials()
        {
            const string sourcePath = Root + "/MixedSource.prefab";
            const string databasePath = Root + "/MixedDatabase.prefab";
            CreatePersistentMultiMaterialSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string importedDiagnostic), Is.True, importedDiagnostic);
            string[] materialNames = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude, null)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer renderer = opened.Registry.Outfits.Single().AxisFigures.Single().OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMesh.GetTriangles(0), Has.Length.EqualTo(3));
            Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(1), "Exclude must not remain as an empty SubMesh in the irreversible Include-only Outfit artifact.");
            Assert.That(renderer.sharedMesh.vertexCount, Is.EqualTo(3), "Vertices referenced only by excluded SubMeshes must not remain.");
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(renderer.sharedMaterials[0], Is.Not.Null, "The compacted Include Material must occupy the only retained SubMesh slot.");
        }

        [Test]
        public void PbmFollowSource_ValidatesMergedSlotSetBeforeCompactingIncludedSubMeshes()
        {
            Mesh merged = new Mesh { name = "Merged13" };
            try
            {
                merged.subMeshCount = 13;
                merged.vertices = Enumerable.Range(0, 39).Select(index => new Vector3(index, 0f, 0f)).ToArray();
                for (int subMesh = 0; subMesh < 13; subMesh++)
                    merged.SetTriangles(new[] { subMesh * 3, subMesh * 3 + 1, subMesh * 3 + 2 }, subMesh);

                bool[] included = Enumerable.Range(0, 13).Select(index => index == 1 || index == 4 || index == 7 || index == 10).ToArray();
                Assert.That(merged.subMeshCount, Is.EqualTo(13), "PBM Follow must validate the complete pre-classification Merged slot set first.");
                Mesh compacted = ShapeSyncMeshOutfitImport.BuildSelectedMesh(merged, included);
                try
                {
                    Assert.That(compacted.subMeshCount, Is.EqualTo(4), "Only Included submeshes are compared with the saved classified Outfit slots.");
                    Assert.That(compacted.GetTriangles(0), Has.Length.EqualTo(3));
                    Assert.That(compacted.GetTriangles(3), Has.Length.EqualTo(3));
                }
                finally { Object.DestroyImmediate(compacted); }
            }
            finally { Object.DestroyImmediate(merged); }
        }

        [Test]
        public void MaterialClassification_RejectsAnEntryNotPresentInEveryAxisSource()
        {
            const string sourcePath = Root + "/CoverageSource.prefab";
            const string databasePath = Root + "/CoverageDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string importedDiagnostic), Is.True, importedDiagnostic);
            string sourceMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry("NotInSource", ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude, null)
            }, out string saveDiagnostic), Is.False);
            Assert.That(saveDiagnostic, Does.Contain("Base material set"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().MaterialClassifications, Is.Empty, "Rejected classification must rollback its registry mutation.");
            Assert.That(opened.Registry.Outfits.Single().AxisFigures.Single().MergedPrefab, Is.Not.Null, "Rejected classification must not delete the reversible Merged artifact.");
        }

        [Test]
        public void MaterialClassification_PreflightsAllAxesBeforeRegistryMutationWhenMergedArtifactIsMissing()
        {
            const string sourcePath = Root + "/MissingMergedSource.prefab";
            const string databasePath = Root + "/MissingMergedDatabase.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                contents.Registry.Outfits.Single().AxisFigures.Single().RemoveMergedPrefab();
            }, out string removeDiagnostic), Is.True, removeDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = opened.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string saveDiagnostic), Is.False);
            Assert.That(saveDiagnostic, Does.Contain("before classification Save"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.Outfits.Single().MaterialClassifications, Is.Empty, "Preflight rejection must not persist the classification table.");
        }

        [Test]
        public void MaterialClassification_BindsBaseCanonicalMaterialToBaseAndFbmWithoutAxisLocalTextureOwners()
        {
            const string databasePath = Root + "/BaseFbmDatabase.prefab";
            const string basePath = Root + "/BaseSource.prefab";
            CreatePersistentSkinnedSource(basePath, "SharedMaterial", "Base");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(basePath), out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", AssetDatabase.LoadAssetAtPath<GameObject>(basePath))
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = imported.Registry.Outfits.Single();
            Assert.That(outfit.AxisFigures.Select(axis => axis.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            string sourceMaterialName = outfit.AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.Single();
            Assert.That(outfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall").SourceMaterialNames.Single(), Is.EqualTo(sourceMaterialName));
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase classified, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.TextureResourceEntry[] resources = classified.Registry.TextureResources.ToArray();
            Assert.That(resources, Has.Length.EqualTo(1), "Outfit Material/Texture payload is owned only by the Base canonical entry.");
            Assert.That(resources.Single().Owner.SourceShapeKey, Is.EqualTo(ShapeSyncDatabaseRegistry.BaseShapeKey));
            ShapeSyncDatabaseRegistry.OutfitEntry classifiedOutfit = classified.Registry.Outfits.Single();
            Material canonical = classifiedOutfit.MaterialEntries.Single().Material;
            Assert.That(AssetDatabase.GetAssetPath(canonical), Is.EqualTo(databasePath),
                "Outfit Base canonical Material must be persisted as a Database sub-asset.");
            SkinnedMeshRenderer baseRenderer = classifiedOutfit.AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
                .OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer fbmRenderer = classifiedOutfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall")
                .OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(baseRenderer.sharedMaterials.Single(), Is.SameAs(canonical));
            Assert.That(fbmRenderer.sharedMaterials.Single(), Is.SameAs(canonical),
                "Outfit FBM must bind the Base canonical Material rather than import an axis-local Material.");
            Assert.That(fbmRenderer.sharedMaterials.Single().GetTexture("_BaseMap"), Is.SameAs(canonical.GetTexture("_BaseMap")));
            Assert.That(classified.Registry.Outfits.Single().AxisFigures.All(axis => axis.MergedPrefab == null && axis.OutfitPrefab != null), Is.True);
            Assert.That(classified.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames, Is.Not.Empty,
                "The Base axis retains the classification source Material identities.");
            Assert.That(classified.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == "Tall").SourceMaterialNames, Is.Empty,
                "FBM axes must not persist Material identities after Include classification Save.");
        }

        [Test]
        public void MaterialClassification_MapsBaseTableToFbmSubmeshIndicesWhenSourceIdentityDiffers()
        {
            const string databasePath = Root + "/FbmMismatchDatabase.prefab";
            const string basePath = Root + "/MismatchBase.prefab";
            const string tallPath = Root + "/MismatchTall.prefab";
            CreatePersistentSkinnedSource(basePath, "BaseMaterial", "MismatchBase");
            CreatePersistentSkinnedSource(tallPath, "TallMaterial", "MismatchTall");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(basePath), out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", AssetDatabase.LoadAssetAtPath<GameObject>(tallPath))
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string baseMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.Single();
            string fbmMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == "Tall").SourceMaterialNames.Single();
            Assert.That(fbmMaterialName, Is.Not.EqualTo(baseMaterialName));

            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = reopened.Registry.Outfits.Single();
            Assert.That(outfit.MaterialClassifications, Has.Count.EqualTo(1));
            Assert.That(outfit.AxisFigures.All(axis => axis.MergedPrefab == null && axis.OutfitPrefab != null), Is.True);
            Assert.That(outfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall").OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials[0], Is.Not.Null);
        }

        [Test]
        public void MaterialClassification_RejectsFbmAdditionalSubmeshInsteadOfGuessingClassification()
        {
            const string databasePath = Root + "/FbmAdditionalSubmeshDatabase.prefab";
            const string basePath = Root + "/FbmAdditionalSubmeshBase.prefab";
            const string tallPath = Root + "/FbmAdditionalSubmeshTall.prefab";
            CreatePersistentSkinnedSource(basePath, "BaseMaterial", "FbmAdditionalBase");
            CreatePersistentSkinnedSource(tallPath, "TallMaterial", "FbmAdditionalTall", "TallExtraMaterial");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(basePath), out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", AssetDatabase.LoadAssetAtPath<GameObject>(tallPath))
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string baseMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.Single();

            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string saveDiagnostic), Is.False);
            Assert.That(saveDiagnostic, Does.Contain("submesh"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry reopenedOutfit = reopened.Registry.Outfits.Single();
            Assert.That(reopenedOutfit.MaterialClassifications, Is.Empty, "A topology mismatch must reject before any classification is persisted.");
            Assert.That(reopenedOutfit.AxisFigures.Single(axis => axis.ShapeKey == "Tall").SourceMaterialNames, Has.Count.EqualTo(2),
                "A topology mismatch must leave the imported source metadata intact for correction/reimport.");
        }

        [Test]
        public void MaterialClassification_SelectsFbmMergedSubmeshByBaseMaterialIndex()
        {
            const string databasePath = Root + "/FbmClassifiedSubmeshDatabase.prefab";
            const string basePath = Root + "/FbmClassifiedSubmeshBase.prefab";
            const string tallPath = Root + "/FbmClassifiedSubmeshTall.prefab";
            CreatePersistentSkinnedSource(basePath, "BaseBody", "FbmClassifiedBase", "BaseTrim");
            CreatePersistentSkinnedSource(tallPath, "TallBody", "FbmClassifiedTall", "TallTrim");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(basePath), out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", AssetDatabase.LoadAssetAtPath<GameObject>(tallPath))
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] baseMaterialNames = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude, null),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Trim")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer renderer = reopened.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == "Tall").OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(renderer.sharedMaterials[0], Is.Not.Null, "The FBM Trim submesh follows the Base Trim Include classification.");
            Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(renderer.sharedMesh.GetTriangles(0), Has.Length.EqualTo(3));
        }

        [Test]
        public void MaterialClassification_DoesNotCollapseFbmSubmeshesWithDuplicateMaterialNames()
        {
            const string databasePath = Root + "/FbmDuplicateMaterialDatabase.prefab";
            const string basePath = Root + "/FbmDuplicateMaterialBase.prefab";
            const string tallPath = Root + "/FbmDuplicateMaterialTall.prefab";
            CreatePersistentSkinnedSource(basePath, "BaseBody", "FbmDuplicateBase", "BaseTrim");
            CreatePersistentSkinnedSource(tallPath, "SharedFbmMaterial", "FbmDuplicateTall", "SharedFbmMaterial");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(basePath), out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", AssetDatabase.LoadAssetAtPath<GameObject>(tallPath))
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] baseMaterialNames = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude, null),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(baseMaterialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Trim")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out openDiagnostic), Is.True, openDiagnostic);
            SkinnedMeshRenderer renderer = reopened.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == "Tall").OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(renderer.sharedMaterials[0], Is.Not.Null);
            Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(renderer.sharedMesh.GetTriangles(0), Has.Length.EqualTo(3));
        }

        [Test]
        public void TryImportAxes_RollsBackEveryEarlierFbmWhenALaterAxisIsRejected()
        {
            const string databasePath = Root + "/BatchRollbackDatabase.prefab";
            const string sourcePath = Root + "/BatchSource.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            CreateDatabaseWithFbmAxes(databasePath, "Tall", "Short");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string baseDiagnostic), Is.True, baseDiagnostic);

            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", source),
                new KeyValuePair<string, GameObject>("UnknownAxis", source)
            }, out string batchDiagnostic), Is.False);
            Assert.That(batchDiagnostic, Does.Contain("Base or a registered FBM"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reopened.Registry.Outfits.Single().AxisFigures.Select(axis => axis.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey }),
                "The failed batch must restore the pre-batch Database rather than retain Tall.");
        }

        [Test]
        public void IncludeClassification_AggregatesSharedTextureWithinOneOwner()
        {
            const string databasePath = Root + "/SharedTextureDatabase.prefab";
            const string sourcePath = Root + "/SharedTextureSource.prefab";
            CreatePersistentMultiMaterialSkinnedSource(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] materialNames = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "First"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Second")
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.TextureResources, Has.Count.EqualTo(1), "One owner must aggregate a shared input Texture.");
            SkinnedMeshRenderer renderer = opened.Registry.Outfits.Single().AxisFigures.Single().OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMaterials[0].GetTexture("_BaseMap"), Is.SameAs(renderer.sharedMaterials[1].GetTexture("_BaseMap")));
        }

        [Test]
        public void MixedIncludeAndProjection_PersistsBothDerivedPrefabsForBaseAndFbm()
        {
            const string databasePath = Root + "/IncludeProjectionDatabase.prefab";
            const string sourcePath = Root + "/IncludeProjectionSource.prefab";
            CreatePersistentMultiMaterialSkinnedSource(sourcePath);
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] materialNames = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Included"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(materialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection, null)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out openDiagnostic), Is.True, openDiagnostic);
            foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in opened.Registry.Outfits.Single().AxisFigures)
            {
                SkinnedMeshRenderer included = axis.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer projection = axis.ProjectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(axis.MergedPrefab, Is.Null);
                Assert.That(included.sharedMesh.subMeshCount, Is.EqualTo(1));
                Assert.That(included.sharedMesh.GetTriangles(0), Has.Length.EqualTo(3));
                Assert.That(included.sharedMaterials.Length, Is.EqualTo(1));
                Assert.That(included.sharedMaterials[0], Is.Not.Null);
                Assert.That(projection.sharedMesh.subMeshCount, Is.EqualTo(1));
                Assert.That(projection.sharedMesh.GetTriangles(0), Has.Length.EqualTo(3));
                Assert.That(projection.sharedMaterials.Length, Is.EqualTo(1));
                Assert.That(projection.sharedMaterials.All(material => material == null), Is.True);
            }
            Assert.That(opened.Registry.TextureResources, Has.Count.EqualTo(1), "Include Texture resources are owned only by the Base canonical Material.");
        }

        [Test]
        public void OutfitNormals_SaveExplicitBaseAndFbmCellsWithDistinctOwnersAndReimport()
        {
            const string databasePath = Root + "/NormalDatabase.prefab";
            const string sourcePath = Root + "/NormalSource.prefab";
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "Normal");
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[] { new KeyValuePair<string, GameObject>("Tall", source) }, out importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);

            Texture sourceTexture = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", "Tall", sourceTexture)
            }, out string missingBaseDiagnostic), Is.False);
            Assert.That(missingBaseDiagnostic, Does.Contain("Base Outfit Normal"));
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, sourceTexture),
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", "Tall", sourceTexture)
            }, out string normalDiagnostic), Is.True, normalDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = opened.Registry.Outfits.Single();
            Assert.That(outfit.NormalDeclarations.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "CoatEntry" }));
            Assert.That(outfit.NormalEntries.Select(entry => entry.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            Assert.That(outfit.NormalEntries.Select(entry => entry.Texture).Distinct().Count(), Is.EqualTo(2));
            Assert.That(outfit.NormalEntries.All(entry => entry.Texture != sourceTexture && AssetDatabase.GetAssetPath(entry.Texture) == databasePath), Is.True,
                "External Normal Textures must be copied into the Database.");
            Assert.That(opened.Registry.TextureResources.Where(entry => entry.LogicalName.EndsWith("_Normal")).Select(entry => entry.Owner.SourceShapeKey),
                Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            int resourceCount = opened.Registry.TextureResources.Count;
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" },
                outfit.NormalEntries.Where(entry => entry.ShapeKey == "Tall")
                    .Select(entry => new ShapeSyncOutfitNormalAuthoring.Assignment(entry.MaterialEntryName, entry.ShapeKey, entry.Texture)).ToArray(), out normalDiagnostic), Is.True, normalDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reused, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reused.Registry.TextureResources.Count, Is.EqualTo(resourceCount), "Database-owned Normal Textures must reuse their existing resource.");
            Assert.That(reused.Registry.Outfits.Single().NormalEntries.Any(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey && entry.Texture != null), Is.True,
                "Sparse FBM save must retain the already committed Base relation.");
            string oldResourceName = reused.Registry.Outfits.Single().NormalEntries.First().TextureResourceName;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                Assert.That(contents.Registry.TryRenameTextureResource(oldResourceName, "RenamedOutfitNormal", out string renameDiagnostic), Is.True, renameDiagnostic), out string renameTransactionDiagnostic), Is.True, renameTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(renamed.Registry.Outfits.Single().NormalEntries.Any(entry => entry.TextureResourceName == "RenamedOutfitNormal"), Is.True);
        }

        [Test]
        public void OutfitNormals_RejectsUndeclaredEntryAndProtectsReferencedTextureWithStructuredDiagnostic()
        {
            const string databasePath = Root + "/NormalRejectDatabase.prefab";
            const string sourcePath = Root + "/NormalRejectSource.prefab";
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "NormalReject");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            Texture sourceTexture = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, System.Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out string missingBaseDiagnostic), Is.False);
            Assert.That(missingBaseDiagnostic, Does.Contain("Base Outfit Normal"));
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, sourceTexture)
            }, out string normalDiagnostic), Is.True, normalDiagnostic);
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "Unknown" }, System.Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out normalDiagnostic), Is.False);
            Assert.That(normalDiagnostic, Does.Contain("Include Material Entries"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out openDiagnostic), Is.True, openDiagnostic);
            string resourceName = opened.Registry.Outfits.Single().NormalEntries.Single().TextureResourceName;
            Assert.That(opened.Registry.TryRemoveTextureResource(resourceName, out _, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic structured), Is.False);
            Assert.That(structured.Code, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceDiagnosticCode.ReferencedByNormalEntry));
        }

        [Test]
        public void OutfitNormals_ReplacingTextureReclaimsTheSupersededGeneralResource()
        {
            const string databasePath = Root + "/NormalReplaceDatabase.prefab";
            const string sourcePath = Root + "/NormalReplaceSource.prefab";
            GameObject source = PrepareClassifiedSingleOutfit(databasePath, sourcePath);
            Texture first = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Texture second = CreatePersistentTexture(Root + "/ReplacementNormal.asset", "ReplacementNormal");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, first)
            }, out string firstDiagnostic), Is.True, firstDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeReplace, out string openDiagnostic), Is.True, openDiagnostic);
            string supersededName = beforeReplace.Registry.Outfits.Single().NormalEntries.Single().TextureResourceName;
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, second)
            }, out string replaceDiagnostic), Is.True, replaceDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase replaced, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(replaced.Registry.Outfits.Single().NormalEntries.Single().TextureResourceName, Is.Not.EqualTo(supersededName));
            Assert.That(replaced.Registry.TextureResources.Any(entry => entry.LogicalName == supersededName), Is.False);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => texture.name == supersededName), Is.False);
        }

        [Test]
        public void OutfitNormalsWindowDraft_LastRemovalStaysEmptyUntilSave()
        {
            const string databasePath = Root + "/NormalLastRemovalDatabase.prefab";
            const string sourcePath = Root + "/NormalLastRemovalSource.prefab";
            GameObject source = PrepareClassifiedSingleOutfit(databasePath, sourcePath);
            Texture normal = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, normal)
            }, out string initialSaveDiagnostic), Is.True, initialSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "Normals"), Is.True);
                Assert.That(window.OutfitNormalEntryMaterialNamesForTest, Is.EqualTo(new[] { "CoatEntry" }));
                Assert.That(window.TryRemoveOutfitNormalEntryForTest("CoatEntry"), Is.True);
                Assert.That(window.OutfitNormalEntryMaterialNamesForTest, Is.Empty,
                    "Removing the last Outfit Normal declaration must not rehydrate the saved entry into the draft.");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(window.OutfitNormalEntryMaterialNamesForTest, Is.Empty);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(removed.Registry.Outfits.Single().NormalDeclarations, Is.Empty);
            Assert.That(removed.Registry.Outfits.Single().NormalEntries, Is.Empty);
        }

        [Test]
        public void OutfitNormals_RemovingDeclarationReclaimsItsGeneralResource()
        {
            const string databasePath = Root + "/NormalDeclarationRemoveDatabase.prefab";
            const string sourcePath = Root + "/NormalDeclarationRemoveSource.prefab";
            GameObject source = PrepareClassifiedSingleOutfit(databasePath, sourcePath);
            Texture normal = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, normal)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeRemove, out string openDiagnostic), Is.True, openDiagnostic);
            string removedName = beforeRemove.Registry.Outfits.Single().NormalEntries.Single().TextureResourceName;
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", Array.Empty<string>(), Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out string removeDiagnostic), Is.True, removeDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(removed.Registry.Outfits.Single().NormalDeclarations, Is.Empty);
            Assert.That(removed.Registry.Outfits.Single().NormalEntries, Is.Empty);
            Assert.That(removed.Registry.TextureResources.Any(entry => entry.LogicalName == removedName), Is.False);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => texture.name == removedName), Is.False);
        }

        [Test]
        public void OutfitNormals_RemovingDeclarationKeepsTheReferencedIncludeMaterialTexture()
        {
            const string databasePath = Root + "/NormalMaterialResourceDatabase.prefab";
            const string sourcePath = Root + "/NormalMaterialResourceSource.prefab";
            PrepareClassifiedSingleOutfit(databasePath, sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Texture materialTexture = opened.Registry.Outfits.Single().MaterialEntries.Single().Material.GetTexture("_BaseMap");
            string materialResourceName = opened.Registry.TextureResources.Single(entry => entry.Texture == materialTexture).LogicalName;
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "CoatEntry" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, materialTexture)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", Array.Empty<string>(), Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out string removeDiagnostic), Is.True, removeDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase retained, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(retained.Registry.TextureResources.Any(entry => entry.LogicalName == materialResourceName && entry.Texture == materialTexture), Is.True,
                "Normal relation removal must not reclaim an Include Material resource.");
        }

        [Test]
        public void OutfitNormals_AggregateExternalTextureWithinOwnerAndReclaimOnlyAfterItsLastRelation()
        {
            const string databasePath = Root + "/SharedNormalDatabase.prefab";
            const string sourcePath = Root + "/SharedNormalSource.prefab";
            CreatePersistentMultiMaterialSkinnedSource(sourcePath);
            Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] sourceMaterialNames = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialNames[0], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "First"),
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialNames[1], ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "Second")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            Texture externalTexture = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials[0].GetTexture("_BaseMap");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(externalTexture, out string externalGuid, out long externalLocalFileId), Is.True);
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "First" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("First", ShapeSyncDatabaseRegistry.BaseShapeKey, externalTexture)
            }, out string firstSaveDiagnostic), Is.True, firstSaveDiagnostic);
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "First", "Second" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("Second", ShapeSyncDatabaseRegistry.BaseShapeKey, externalTexture)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash), "Normal authoring must not modify its source Prefab.");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase shared, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry sharedOutfit = shared.Registry.Outfits.Single();
            string sharedResourceName = sharedOutfit.NormalEntries.First().TextureResourceName;
            Assert.That(sharedOutfit.NormalEntries.Select(entry => entry.TextureResourceName).Distinct(), Is.EqualTo(new[] { sharedResourceName }));
            ShapeSyncDatabaseRegistry.TextureResourceEntry sharedResource = shared.Registry.TextureResources.Single(entry => entry.Usage == ShapeSyncDatabaseRegistry.TextureResourceUsage.General);
            Assert.That(sharedResource.SourceAssetGuid, Is.EqualTo(externalGuid));
            Assert.That(sharedResource.SourceAssetLocalFileId, Is.EqualTo(externalLocalFileId));

            Texture replacement = CreatePersistentTexture(Root + "/SharedNormalReplacement.asset", "SharedNormalReplacement");
            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "First", "Second" }, new[]
            {
                new ShapeSyncOutfitNormalAuthoring.Assignment("First", ShapeSyncDatabaseRegistry.BaseShapeKey, replacement)
            }, out string replaceFirstDiagnostic), Is.True, replaceFirstDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase oneReplaced, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry replacedOutfit = oneReplaced.Registry.Outfits.Single();
            string replacementResourceName = replacedOutfit.NormalEntries.Single(entry => entry.MaterialEntryName == "First").TextureResourceName;
            Assert.That(replacementResourceName, Is.Not.EqualTo(sharedResourceName));
            Assert.That(replacedOutfit.NormalEntries.Single(entry => entry.MaterialEntryName == "Second").TextureResourceName, Is.EqualTo(sharedResourceName));
            Assert.That(oneReplaced.Registry.TextureResources.Any(entry => entry.LogicalName == sharedResourceName), Is.True,
                "Replacing one shared relation must retain the resource used by the other relation.");

            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", new[] { "Second" }, Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out string removeFirstDiagnostic), Is.True, removeFirstDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase oneRelation, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(oneRelation.Registry.Outfits.Single().NormalEntries.Single().TextureResourceName, Is.EqualTo(sharedResourceName));
            Assert.That(oneRelation.Registry.TextureResources.Any(entry => entry.LogicalName == sharedResourceName), Is.True);
            Assert.That(oneRelation.Registry.TextureResources.Any(entry => entry.LogicalName == replacementResourceName), Is.False,
                "Removing the replaced relation must reclaim its now-unreferenced resource.");

            Assert.That(ShapeSyncOutfitNormalAuthoring.TrySave(databasePath, "Coat", Array.Empty<string>(), Array.Empty<ShapeSyncOutfitNormalAuthoring.Assignment>(), out string removeLastDiagnostic), Is.True, removeLastDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase noRelations, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(noRelations.Registry.TextureResources.Any(entry => entry.LogicalName == sharedResourceName), Is.False);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => texture.name == sharedResourceName), Is.False);
            Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash));
        }

        [Test]
        public void OutfitRemove_ReclaimsAllOutfitOwnedTextureResourcesIncludingNormals()
        {
            const string databasePath = Root + "/NormalRemoveDatabase.prefab";
            const string sourcePath = Root + "/NormalRemoveSource.prefab";
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "NormalRemove");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeRemove, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, int> originalDialog = ShapeSyncDatabaseWindow.DisplayDirtyDialog;
            try
            {
                Assert.That(window.TrySetDatabase(beforeRemove, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "Normals"), Is.True);
                Assert.That(window.TryAddOutfitNormalEntryForTest(), Is.True);
                Texture sourceTexture = source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.GetTexture("_BaseMap");
                Assert.That(window.TrySetOutfitNormalDraftForTest("CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, sourceTexture), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                ShapeSyncDatabaseWindow.DisplayDirtyDialog = (_, _, _, _, _) => 2;
                Assert.That(window.TryNavigateTo(ShapeSyncDatabaseWindow.Section.General), Is.False, "Cancel must retain the Outfit Normal draft.");
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string normalDiagnostic), Is.True, normalDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out beforeRemove, out openDiagnostic), Is.True, openDiagnostic);
                string[] ownedResourceNames = beforeRemove.Registry.TextureResources.Where(entry => entry.Owner.OutfitIdentity == "Coat").Select(entry => entry.LogicalName).ToArray();
                ShapeSyncDatabaseRegistry.OutfitEntry outfitBeforeRemove = beforeRemove.Registry.Outfits.Single();
                string[] ownedArtifactNames = outfitBeforeRemove.AxisFigures.Where(axis => axis != null)
                    .SelectMany(axis => new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab })
                    .Where(asset => asset != null).Select(asset => asset.name).Concat(
                        outfitBeforeRemove.AxisFigures.Where(axis => axis != null).SelectMany(axis => new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab })
                            .Where(asset => asset != null).SelectMany(asset => asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            .SelectMany(renderer => new UnityEngine.Object[] { renderer.sharedMesh }.Concat(renderer.sharedMaterials ?? Array.Empty<Material>()))
                            .Where(asset => asset != null).Select(asset => asset.name)).Distinct().ToArray();
                string[] ownerSubAssetNamesBeforeRemove = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .Where(asset => asset != null && asset.name.StartsWith("Coat_", StringComparison.Ordinal))
                    .Select(asset => asset.name).Distinct().ToArray();
                Assert.That(ownerSubAssetNamesBeforeRemove.Any(name => name.Contains("_Source", StringComparison.Ordinal)), Is.True,
                    "The remove path must cover import-time Base_Source sub-assets even when they are not referenced by a classified renderer.");
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(removed.Registry.Outfits, Is.Empty);
                Assert.That(removed.Registry.TextureResources.Any(entry => ownedResourceNames.Contains(entry.LogicalName)), Is.False);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Texture>().Any(texture => ownedResourceNames.Contains(texture.name)), Is.False);
                string[] remainingArtifactNames = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .Where(asset => ownedArtifactNames.Contains(asset.name)).Select(asset => asset.name).Distinct().ToArray();
                Assert.That(remainingArtifactNames, Is.Empty,
                    "Removing an Outfit must reclaim its Database-owned prefab, Mesh, and Material artifacts. Remaining: " + string.Join(", ", remainingArtifactNames));
                string[] remainingOwnerSubAssets = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .Where(asset => asset != null && asset.name.StartsWith("Coat_", StringComparison.Ordinal)
                        && asset.name.Contains("_Source_", StringComparison.Ordinal))
                    .Select(asset => asset.name).Distinct().ToArray();
                Assert.That(remainingOwnerSubAssets, Is.Empty,
                    "Removing an Outfit must reclaim all owner-named Base_Source/FBM sub-assets. Remaining: " + string.Join(", ", remainingOwnerSubAssets));
            }
            finally { ShapeSyncDatabaseWindow.DisplayDirtyDialog = originalDialog; Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitRemove_ReclaimsDerivedMeshesAcrossBaseAndFbmAxes()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string databasePath = Root + "/MultiAxisRemoveDatabase_" + suffix + ".prefab";
            string sourcePath = Root + "/MultiAxisRemoveSource_" + suffix + ".prefab";
            PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeRemove, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = beforeRemove.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            string duplicateArtifactName = outfit.AxisFigures.Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).OutfitPrefab.name;
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                // Reproduce an orphan left by an earlier failed import: same
                // artifact name, but not reachable from the Registry and with no Mesh.
                GameObject stale = new GameObject(duplicateArtifactName);
                stale.AddComponent<SkinnedMeshRenderer>();
                stale.transform.SetParent(intermediate, false);
            }, out string duplicateDiagnostic), Is.True, duplicateDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out beforeRemove, out openDiagnostic), Is.True, openDiagnostic);
            outfit = beforeRemove.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            string[] ownedArtifactNames = outfit.AxisFigures.Where(axis => axis != null)
                .SelectMany(axis => new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab })
                .Where(asset => asset != null).Select(asset => asset.name).Concat(
                    outfit.AxisFigures.Where(axis => axis != null)
                        .SelectMany(axis => new[] { axis.SourcePrefab, axis.MergedPrefab, axis.OutfitPrefab, axis.ProjectionPrefab })
                        .Where(asset => asset != null).SelectMany(asset => asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        .SelectMany(renderer => new UnityEngine.Object[] { renderer.sharedMesh }.Concat(renderer.sharedMaterials ?? Array.Empty<Material>()))
                        .Where(asset => asset != null).Select(asset => asset.name)).Distinct().ToArray();
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(beforeRemove, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Coat"), Is.True);
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(removed.Registry.Outfits, Is.Empty);
                string[] remainingArtifactNames = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .Where(asset => ownedArtifactNames.Contains(asset.name)).Select(asset => asset.name).Distinct().ToArray();
                Assert.That(remainingArtifactNames, Is.Empty,
                    "Removing an Outfit must reclaim every Base/FBM Prefab Mesh and Material, including stale duplicate Prefabs. Remaining: " + string.Join(", ", remainingArtifactNames));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitRemove_BeforeClassification_ReclaimsBaseSourceSubAssets()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string databasePath = Root + "/UnclassifiedRemoveDatabase_" + suffix + ".prefab";
            string sourcePath = Root + "/UnclassifiedRemoveSource_" + suffix + ".prefab";
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "UnclassifiedRemove");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master");
                baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            string[] sourceAssetsBeforeRemove = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                .Where(asset => asset != null && asset.name.Contains("_Source_", StringComparison.Ordinal))
                .Select(asset => asset.name).Distinct().ToArray();
            Assert.That(sourceAssetsBeforeRemove, Is.Not.Empty, "The unclassified Base import must create Source sub-assets.");

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(opened, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Coat"), Is.True);
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                string[] remainingSourceAssets = AssetDatabase.LoadAllAssetsAtPath(databasePath)
                    .Where(asset => asset != null && asset.name.Contains("_Source_", StringComparison.Ordinal))
                    .Select(asset => asset.name).Distinct().ToArray();
                Assert.That(remainingSourceAssets, Is.Empty,
                    "Classification前のOutfit削除後にBase_Sourceサブアセットを残してはならない。残存: " + string.Join(", ", remainingSourceAssets));
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static GameObject PrepareClassifiedSingleOutfit(string databasePath, string sourcePath)
        {
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "Normal");
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, intermediate) =>
            {
                GameObject baseFigure = new GameObject("Master"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportBase(databasePath, "Coat", source, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string sourceMaterialName = imported.Registry.Outfits.Single().AxisFigures.Single().SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "CoatEntry")
            }, out string classificationDiagnostic), Is.True, classificationDiagnostic);
            return source;
        }

        private static GameObject PrepareClassifiedOutfitWithFbm(string databasePath, string sourcePath, string fbmName,
            params string[] additionalMaterialNames)
        {
            CreatePersistentSkinnedSource(sourcePath, "CoatMaterial", "PbmFollow", additionalMaterialNames ?? Array.Empty<string>());
            CreateDatabaseWithFbmAxis(databasePath, fbmName);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new KeyValuePair<string, GameObject>(fbmName, source)
            }, out string importDiagnostic), Is.True, importDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase imported, out string openDiagnostic), Is.True, openDiagnostic);
            string[] sourceMaterialNames = imported.Registry.Outfits.Single().AxisFigures
                .Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.ToArray();
            var classifications = sourceMaterialNames.Select((name, index) =>
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(name,
                    index == 0 ? ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include : ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude,
                    index == 0 ? "CoatEntry" : null)).ToArray();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Coat", classifications,
                out string classificationDiagnostic), Is.True, classificationDiagnostic);
            return source;
        }

        private static Texture2D CreatePersistentTexture(string path, string textureName)
        {
            var texture = new Texture2D(1, 1) { name = textureName };
            texture.SetPixel(0, 0, Color.magenta);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CreatePersistentSkinnedSource(string path, string materialName = "CoatMaterial", string assetPrefix = "Coat", params string[] additionalMaterialNames)
        {
            GameObject root = new GameObject("CoatSource");
            root.AddComponent<Animator>();
            GameObject bone = new GameObject("Bone");
            bone.transform.SetParent(root.transform, false);
            GameObject extraBone = new GameObject("ExtraBone");
            extraBone.transform.SetParent(bone.transform, false);
            GameObject meshObject = new GameObject("Coat");
            meshObject.transform.SetParent(root.transform, false);
            SkinnedMeshRenderer renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = "CoatMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            string[] materialNames = new[] { materialName }.Concat(additionalMaterialNames ?? Array.Empty<string>()).ToArray();
            mesh.subMeshCount = materialNames.Length;
            for (int subMeshIndex = 0; subMeshIndex < materialNames.Length; subMeshIndex++)
                mesh.SetTriangles(new[] { 0, 1, 2 }, subMeshIndex);
            // Keep the top-level Extra Bone parent in the renderer bone table as an
            // unweighted slot.  Outfit topology normalization uses the table to
            // reconstruct the parent path for the weighted Bone slot.
            mesh.bindposes = new[]
            {
                bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix,
                root.transform.worldToLocalMatrix * root.transform.localToWorldMatrix
            };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            renderer.sharedMesh = mesh;
            renderer.rootBone = bone.transform;
            renderer.bones = new[] { bone.transform, root.transform };
            Material[] materials = new Material[materialNames.Length];
            for (int materialIndex = 0; materialIndex < materialNames.Length; materialIndex++)
            {
                Material material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = materialNames[materialIndex] };
                Texture2D baseColor = new Texture2D(1, 1) { name = assetPrefix + "BaseColor" + materialIndex };
                baseColor.SetPixel(0, 0, Color.white);
                baseColor.Apply();
                material.SetTexture("_BaseMap", baseColor);
                AssetDatabase.CreateAsset(baseColor, Root + "/" + assetPrefix + "BaseColor" + materialIndex + ".asset");
                AssetDatabase.CreateAsset(material, Root + "/" + assetPrefix + "Material" + materialIndex + ".mat");
                materials[materialIndex] = material;
            }
            renderer.sharedMaterials = materials;
            AssetDatabase.CreateAsset(mesh, Root + "/" + assetPrefix + "Mesh.asset");
            Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void OutfitPbmFollow_PersistsOnlySelectedFigurePbmWithCompleteBaseAndFbmPrefabs()
        {
            const string databasePath = Root + "/PbmFollowDatabase.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var pbmDraft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, pbmDraft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admitDiagnostic), Is.True, admitDiagnostic);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm), new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm) }
                }, out string pbmDiagnostic), Is.True, pbmDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                GameObject baseSource = CreateValidImportedFigure(intermediate, "Coat_Master_Source", transaction);
                GameObject tallSource = CreateValidImportedFigure(intermediate, "Coat_Tall_Source", transaction);
                GameObject baseOutfit = CreateValidImportedFigure(intermediate, "Coat_Pose_Master", transaction);
                GameObject tallOutfit = CreateValidImportedFigure(intermediate, "Coat_Pose_Tall", transaction);
                Assert.That(contents.Registry.TrySetOutfitPbmFollows(contents, "Coat", new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry("Pose", new[]
                    {
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseSource, baseOutfit),
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry("Tall", tallSource, tallOutfit)
                    })
                }, out string followDiagnostic), Is.True, followDiagnostic);
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow = reopened.Registry.Outfits.Single().PbmFollows.Single();
            Assert.That(follow.PbmAxisName, Is.EqualTo("Pose"));
            Assert.That(follow.Figures.Select(value => value.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            Assert.That(follow.Figures.All(value => value.Figure != null && value.Figure.transform.parent == reopened.transform.Find("Intermediate")), Is.True);
        }

        [Test]
        public void OutfitPbmFollow_SaveDoesNotRequireCanonicalMaterialPayload()
        {
            const string databasePath = Root + "/PbmFollowWithoutMaterialsDatabase.prefab";
            const string sourcePath = Root + "/PbmFollowWithoutMaterialsSource.prefab";
            CreatePersistentSkinnedSource(sourcePath);
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string outfitImportDiagnostic), Is.True, outfitImportDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);

            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry baseFollow = reopened.Registry.Outfits.Single()
                .PbmFollows.Single().Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer renderer = baseFollow.Figure.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(renderer.sharedMesh.subMeshCount));
            Assert.That(renderer.sharedMaterials.All(material => material == null), Is.True,
                "PBM registration must not synthesize or require Material payload when the Outfit has no canonical Material Entries.");
        }

        [Test]
        public void OutfitPbmFollow_SaveBuildsIncludeOnlyArtifactAndFigurePbmRenameReclaimsIt()
        {
            const string databasePath = Root + "/PbmFollowAuthoringDatabase.prefab";
            const string sourcePath = Root + "/PbmFollowSource.prefab";
            GameObject source = PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject basePbmFigure = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbmFigure = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbmFigure),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbmFigure)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);

            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = saved.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry savedFollow = outfit.PbmFollows.Single();
            Assert.That(savedFollow.Figures.All(figure => figure.SourcePrefab != null && figure.SourcePrefab.transform.parent == saved.transform.Find("Intermediate")), Is.True,
                "PBM Follow Registry must retain a Database-owned source on every selected shape key.");
            Assert.That(savedFollow.Figures.All(figure => figure.SourcePrefab != source), Is.True);
            GameObject followPrefab = outfit.PbmFollows.Single().Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Figure;
            SkinnedMeshRenderer followRenderer = followPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(followPrefab.name, Is.EqualTo("Coat_Pose_Master"));
            Assert.That(followPrefab.transform.parent, Is.SameAs(saved.transform.Find("Intermediate")));
            Assert.That(followRenderer.sharedMaterials.Length, Is.EqualTo(1));
            Material followMaterial = followRenderer.sharedMaterials[0];
            if (followMaterial != null)
            {
                Assert.That(AssetDatabase.GetAssetPath(followMaterial), Is.EqualTo(databasePath),
                    "When present, PBM Follow Material payload must remain Database-owned.");
                Assert.That(outfit.MaterialEntries.Where(entry => entry != null && entry.Material != null)
                    .Select(entry => entry.Material), Does.Contain(followMaterial),
                    "When present, PBM Follow must bind an Outfit canonical Material Entry, not an axis-local copy.");
            }
            Assert.That(AssetDatabase.GetAssetPath(followRenderer.sharedMesh), Is.EqualTo(databasePath));
            Assert.That(AssetDatabase.GetAssetPath(savedFollow.Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab), Is.EqualTo(databasePath),
                "The complete source side must be Database-owned.");
            Assert.That(savedFollow.Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab
                .GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.subMeshCount, Is.EqualTo(source.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.subMeshCount));

            GameObject oldSourcePrefab = savedFollow.Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab;
            Mesh oldSourceMesh = oldSourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            Mesh oldFollowMesh = followRenderer.sharedMesh;
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string replaceDiagnostic), Is.True, replaceDiagnostic);
            Assert.That(followPrefab == null, Is.True, "Reopen-safe PBM follow replacement must destroy the old derived Prefab.");
            Assert.That(oldSourcePrefab == null, Is.True, "Reopen-safe PBM follow replacement must destroy the old source clone.");
            Assert.That(oldSourceMesh == null, Is.True, "Reopen-safe PBM follow replacement must remove the old source Mesh.");
            Assert.That(oldFollowMesh == null, Is.True, "Reopen-safe PBM follow replacement must remove the old Database-owned Mesh.");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), Is.Not.Null,
                "Overwriting the derived artifact must never delete or replace the external PBM Follow source.");

            Assert.That(ShapeSyncFigureAxisImport.TryRenamePbm(databasePath, "Pose", "RenamedPose", out string renameDiagnostic), Is.True, renameDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(renamed.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Empty, "Figure PBM rename must clear the saved follow declaration.");
            Assert.That(renamed.transform.Find("Intermediate/Coat_Pose_Master"), Is.Null, "Figure PBM rename must reclaim the old follow Prefab.");
            Assert.That(renamed.transform.Find("Intermediate").Cast<Transform>()
                .Any(child => child.name.StartsWith("Coat_Pose_", StringComparison.Ordinal) && child.name.EndsWith("_Source", StringComparison.Ordinal)), Is.False,
                "Figure PBM rename must reclaim the old source clone.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == "Coat_Pose_Master_SkinnedMesh"), Is.False, "Figure PBM rename must reclaim the old follow Mesh.");

            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("RenamedPose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("RenamedPose", "Tall", source)
            }, out string resaveDiagnostic), Is.True, resaveDiagnostic);
            int persistedSourceSubMeshCount = ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase beforeSourceDelete, out openDiagnostic)
                ? beforeSourceDelete.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows.Single().Figures
                    .Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab
                    .GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.subMeshCount
                : -1;
            Assert.That(persistedSourceSubMeshCount, Is.GreaterThan(0), openDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase sourceOwnedDatabase, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(AssetDatabase.DeleteAsset(sourcePath), Is.True, "The external PBM source must be removable after Save.");
            AssetDatabase.Refresh();
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), Is.Null);
            ShapeSyncDatabaseWindow sourceOwnedWindow = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(sourceOwnedWindow.TrySetDatabase(sourceOwnedDatabase, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(sourceOwnedWindow.TrySelectOutfitChildForTest("Coat", "PBMs"), Is.True);
                GameObject hydratedSource = sourceOwnedWindow.OutfitPbmFollowSourcePrefabForTest("RenamedPose", ShapeSyncDatabaseRegistry.BaseShapeKey);
                Assert.That(hydratedSource, Is.Not.Null, "Window hydrate must retain the Database-owned source after external source deletion.");
                Assert.That(AssetDatabase.GetAssetPath(hydratedSource), Is.EqualTo(databasePath));
                Assert.That(hydratedSource.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.subMeshCount, Is.EqualTo(persistedSourceSubMeshCount));
                Assert.That(sourceOwnedWindow.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(sourceOwnedWindow.TrySaveOutfitForTest(out string sourceOwnedSaveDiagnostic), Is.True, sourceOwnedSaveDiagnostic);
            }
            finally { Object.DestroyImmediate(sourceOwnedWindow); }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase sourceOwnedResaved, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(AssetDatabase.GetAssetPath(sourceOwnedResaved.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows.Single()
                .Figures.Single(figure => figure.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab), Is.EqualTo(databasePath));
            Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(databasePath, "Tall", "RenamedTall", out string renameFbmDiagnostic), Is.True, renameFbmDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase fbmRenamed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(fbmRenamed.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Empty, "FBM rename invalidates the Figure PBM matrix and must clear every follow declaration.");
            Assert.That(fbmRenamed.transform.Find("Intermediate/Coat_RenamedPose_Master"), Is.Null, "FBM rename must reclaim the affected follow Prefab.");
            Assert.That(fbmRenamed.transform.Find("Intermediate").Cast<Transform>()
                .Any(child => child.name.StartsWith("Coat_RenamedPose_", StringComparison.Ordinal) && child.name.EndsWith("_Source", StringComparison.Ordinal)), Is.False,
                "FBM rename must reclaim the affected source clone.");
        }

        [Test]
        public void OutfitPbmFollow_ResaveUsesSubMeshSelectionWhenSourceMaterialSlotsAreEmpty()
        {
            const string databasePath = Root + "/PbmFollowIncludedSlotDatabase.prefab";
            const string sourcePath = Root + "/PbmFollowIncludedSlotSource.prefab";
            GameObject source = PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall", "CoatExcludedMaterial");

            GameObject sourceContents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                SkinnedMeshRenderer renderer = sourceContents.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Material[] materials = renderer.sharedMaterials;
                Assert.That(materials, Has.Length.EqualTo(2));
                materials[1] = null;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(sourceContents, sourcePath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(sourceContents); }
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);

            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Coat", new[]
            {
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string classifiedOverwriteDiagnostic), Is.True, classifiedOverwriteDiagnostic,
                "A classified FBM overwrite must use the saved Include classification rather than rejecting an empty Exclude slot.");

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);

            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry[] savedFigures = saved.Registry.Outfits.Single().PbmFollows.Single().Figures.ToArray();
            Assert.That(savedFigures, Has.Length.EqualTo(2));
            foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry figure in savedFigures)
            {
                SkinnedMeshRenderer renderer = figure.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(renderer.sharedMaterials, Is.Empty, "PBM Source is geometry-only and must not retain Material payload.");
            }

            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(saved, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "PBMs"), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(window.TrySaveOutfitForTest(out string resaveDiagnostic), Is.True, resaveDiagnostic);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }

            GameObject invalidSourceContents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                SkinnedMeshRenderer renderer = invalidSourceContents.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Material[] materials = renderer.sharedMaterials;
                materials[0] = null;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(invalidSourceContents, sourcePath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(invalidSourceContents); }
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string includedDiagnostic), Is.True, includedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase unchanged, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(unchanged.Registry.Outfits.Single().PbmFollows, Is.Not.Empty, "PBM resave must remain valid when Source Material payload is absent.");
            Assert.That(unchanged.Registry.Outfits.Single().PbmFollows.Single().Figures
                .Select(figure => figure.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterials),
                Has.All.Empty);
        }

        [Test]
        public void OutfitPbmFollow_SaveRejectsUnknownPbmAndIncompleteShapeSetWithoutMutation()
        {
            const string databasePath = Root + "/PbmFollowRejectDatabase.prefab";
            const string sourcePath = Root + "/PbmFollowRejectSource.prefab";
            GameObject source = PrepareClassifiedSingleOutfit(databasePath, sourcePath);
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Unknown", ShapeSyncDatabaseRegistry.BaseShapeKey, source)
            }, out string unknownDiagnostic), Is.False);
            Assert.That(unknownDiagnostic, Does.Contain("PBM follow requires each selected Figure PBM"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase unchanged, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(unchanged.Registry.Outfits.Single().PbmFollows, Is.Empty);
        }

        [Test]
        public void OutfitPbmFollow_SaveRejectsSourceClassificationSubmeshMismatchBeforeRegistryMutation()
        {
            const string databasePath = Root + "/PbmFollowMismatchDatabase.prefab";
            const string classifiedSourcePath = Root + "/PbmFollowMismatchClassifiedSource.prefab";
            const string mismatchSourcePath = Root + "/PbmFollowMismatchSource.prefab";
            PrepareClassifiedSingleOutfit(databasePath, classifiedSourcePath);
            CreatePersistentMultiMaterialSkinnedSource(mismatchSourcePath);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject tallFbm = CreateValidImportedFigure(intermediate, "Tall", transaction);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallFbm) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm), new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", CreateValidImportedFigure(intermediate, "Tall_Pose", transaction)) }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);

            GameObject mismatchSource = AssetDatabase.LoadAssetAtPath<GameObject>(mismatchSourcePath);
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, mismatchSource),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", mismatchSource)
            }, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("PBMFollowSourceClassificationMismatch"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase unchanged, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(unchanged.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Empty,
                "A source slot/submesh contract rejection must not mutate the saved PBM Follow relation.");
        }

        [Test]
        public void OutfitPbmFollow_FigurePbmRemoveReclaimsEveryFollowArtifact()
        {
            const string removeDatabasePath = Root + "/PbmFollowRemoveDatabase.prefab";
            const string removeSourcePath = Root + "/PbmFollowRemoveSource.prefab";
            PrepareSavedPbmFollow(removeDatabasePath, removeSourcePath);

            Assert.That(ShapeSyncFigureAxisImport.TryRemovePbm(removeDatabasePath, "Pose", out string removeDiagnostic), Is.True, removeDiagnostic);
            AssertNoSavedPbmFollow(removeDatabasePath, "Pose", "PBM removal must clear every saved follow artifact.");

        }

        [Test]
        public void OutfitPbmFollow_FigureFbmRemoveReclaimsEveryFollowArtifactAndRollbackRestoresIt()
        {
            const string removeDatabasePath = Root + "/PbmFollowFbmRemoveDatabase.prefab";
            const string removeSourcePath = Root + "/PbmFollowFbmRemoveSource.prefab";
            PrepareSavedPbmFollow(removeDatabasePath, removeSourcePath);

            Assert.That(ShapeSyncFigureAxisImport.TryRemoveFbm(removeDatabasePath, "Tall", out string removeDiagnostic), Is.True, removeDiagnostic);
            AssertNoSavedPbmFollow(removeDatabasePath, "Pose", "FBM removal must clear every saved follow artifact.");

            const string rollbackDatabasePath = Root + "/PbmFollowRemoveRollbackDatabase.prefab";
            const string rollbackSourcePath = Root + "/PbmFollowRemoveRollbackSource.prefab";
            PrepareSavedPbmFollow(rollbackDatabasePath, rollbackSourcePath);
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncFigureAxisImport.TryRemovePbm(rollbackDatabasePath, "Pose", out string rollbackDiagnostic), Is.False);
                Assert.That(rollbackDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave; }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(rollbackDatabasePath, out ShapeSyncDatabase rolledBack, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(rolledBack.Registry.FigureAxes.Any(axis => axis.Name == "Pose" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True);
            Assert.That(rolledBack.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Not.Empty);
            Assert.That(rolledBack.transform.Find("Intermediate/Coat_Pose_Master"), Is.Not.Null, "Failed PBM removal must restore the follow Prefab.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(rollbackDatabasePath).OfType<Mesh>().Any(mesh => mesh.name == "Coat_Pose_Master_SkinnedMesh"), Is.True,
                "Failed PBM removal must restore the follow Mesh.");
        }

        [Test]
        public void OutfitPbmFollow_WindowSelectionIsDirtyAndSaveUsesThePbmAuthoringTransaction()
        {
            const string databasePath = Root + "/PbmFollowWindowDatabase.prefab";
            const string sourcePath = Root + "/PbmFollowWindowSource.prefab";
            GameObject source = PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string authoringDiagnostic), Is.True, authoringDiagnostic);

            var window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
                Assert.That(window.TrySetDatabase(saved, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "PBMs"), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False,
                    "A persisted PBM Follow source/artifact pair must hydrate as clean; dirty comparison uses SourcePrefab.");
                Assert.That(window.TrySetOutfitPbmFollowDraftForTest("Pose", false, ShapeSyncDatabaseRegistry.BaseShapeKey, null), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase cleared, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(cleared.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Empty);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitCollection_SavePersistsSourcesAndDatabaseCopiesThenNoneReclaimsCopies()
        {
            const string databasePath = Root + "/CollectionDatabase.prefab";
            const string baseSourcePath = Root + "/BaseCollectionSource.prefab";
            const string tallSourcePath = Root + "/TallCollectionSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            CreatePersistentSkinnedSource(baseSourcePath, assetPrefix: "BaseCollection");
            CreatePersistentSkinnedSource(tallSourcePath, assetPrefix: "TallCollection");
            GameObject baseSource = AssetDatabase.LoadAssetAtPath<GameObject>(baseSourcePath);
            GameObject tallSource = AssetDatabase.LoadAssetAtPath<GameObject>(tallSourcePath);
            Hash128 baseSourceHash = AssetDatabase.GetAssetDependencyHash(baseSourcePath);
            Hash128 tallSourceHash = AssetDatabase.GetAssetDependencyHash(tallSourcePath);

            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, new[]
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, baseSource),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", tallSource)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(AssetDatabase.GetAssetDependencyHash(baseSourcePath), Is.EqualTo(baseSourceHash));
            Assert.That(AssetDatabase.GetAssetDependencyHash(tallSourcePath), Is.EqualTo(tallSourceHash));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = saved.Registry.Outfits.Single();
            Assert.That(outfit.CollectionKind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone));
            Assert.That(outfit.UseProjectionForFullCollection, Is.False);
            Assert.That(outfit.CollectionEntries.Select(entry => entry.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry entry in outfit.CollectionEntries)
            {
                Assert.That(entry.SourcePrefab, Is.Not.Null, "The source Prefab is an explicit Registry input, not inferred from a generated name.");
                Assert.That(AssetDatabase.GetAssetPath(entry.SourcePrefab), Is.EqualTo(databasePath), "Collection source inputs are copied into the Database; Registry retains no external asset reference.");
                Assert.That(entry.CollectionPrefab.name, Is.EqualTo("Coat_" + entry.ShapeKey + "_Collection"));
                Assert.That(entry.CollectionPrefab.transform.parent, Is.SameAs(saved.transform.Find("Intermediate")));
                Assert.That(entry.SourcePrefab.GetComponentsInChildren<Animator>(true), Is.Empty, "Collection source records Figure shape only and must not retain an external Avatar reference.");
                Assert.That(entry.CollectionPrefab.GetComponentsInChildren<Animator>(true), Is.Empty, "Collection output records Figure shape only and must not retain an external Avatar reference.");
                SkinnedMeshRenderer renderer = entry.CollectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(databasePath));
                Assert.That(renderer.sharedMaterials, Is.Empty);
            }
            GameObject oldSourceCopy = outfit.CollectionEntries.First().SourcePrefab;
            GameObject oldCopy = outfit.CollectionEntries.First().CollectionPrefab;

            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.None, false,
                Array.Empty<ShapeSyncMeshOutfitCollectionAuthoring.Source>(), out string clearDiagnostic), Is.True, clearDiagnostic);
            Assert.That(oldCopy == null, Is.True, "No Collection removes the previously Database-owned Collection Prefab.");
            Assert.That(oldSourceCopy == null, Is.True, "No Collection also removes the Database-owned Collection source Prefab.");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase cleared, out openDiagnostic), Is.True, openDiagnostic);
            outfit = cleared.Registry.Outfits.Single();
            Assert.That(outfit.CollectionKind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.None));
            Assert.That(outfit.CollectionEntries, Is.Empty);
        }

        [Test]
        public void OutfitCollection_SaveRejectsIncompleteKeysAndUnavailableProjectionWithoutMutation()
        {
            const string databasePath = Root + "/CollectionRejectDatabase.prefab";
            const string sourcePath = Root + "/CollectionRejectSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            CreatePersistentSkinnedSource(sourcePath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);

            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, new[]
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source)
            }, out string incompleteDiagnostic), Is.False);
            Assert.That(incompleteDiagnostic, Does.Contain("every FBM"));
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, true, new[]
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", source)
            }, out string projectionDiagnostic), Is.False);
            Assert.That(projectionDiagnostic, Does.Contain("Projection"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase unchanged, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(unchanged.Registry.Outfits.Single().CollectionEntries, Is.Empty);
        }

        [Test]
        public void OutfitCollection_SaveRejectsSceneObjectAndPersistsFullProjectionCollection()
        {
            const string databasePath = Root + "/CollectionFullProjectionDatabase.prefab";
            const string sourcePath = Root + "/CollectionFullProjectionSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                var axes = new List<ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry>();
                foreach (string shapeKey in new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" })
                {
                    GameObject source = CreateValidImportedFigure(intermediate, "Coat_" + shapeKey + "_Source", transaction);
                    GameObject outfit = CreateValidImportedFigure(intermediate, "Coat_" + shapeKey, transaction);
                    GameObject projection = CreateValidImportedFigure(intermediate, "Coat_" + shapeKey + "_Projection", transaction);
                    axes.Add(new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(shapeKey, source, null, outfit, projection, new[] { "CoatMaterial" }));
                }
                Assert.That(contents.Registry.TrySetOutfitAxisFigures(contents, "Coat", axes, out string axisDiagnostic), Is.True, axisDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "CollectionFullProjection");
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var sceneObject = new GameObject("CollectionSceneObject");
            try
            {
                Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, new[]
                {
                    new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, sceneObject),
                    new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", sceneObject)
                }, out string sceneDiagnostic), Is.False);
                Assert.That(sceneDiagnostic, Does.Contain("persistent Prefab"));
            }
            finally { Object.DestroyImmediate(sceneObject); }

            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, true, new[]
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, sourcePrefab),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", sourcePrefab)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfitEntry = saved.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            Assert.That(outfitEntry.CollectionKind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full));
            Assert.That(outfitEntry.UseProjectionForFullCollection, Is.True);
            Assert.That(outfitEntry.CollectionEntries, Has.Count.EqualTo(2));
        }

        [Test]
        public void OutfitCollection_RegistryRejectsExternalSourceAndResaveRollbackRestoresArtifacts()
        {
            const string databasePath = Root + "/CollectionOwnershipRollbackDatabase.prefab";
            const string sourcePath = Root + "/CollectionOwnershipRollbackSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "CollectionOwnershipRollback");
            GameObject externalSource = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                GameObject baseOutput = CreateValidImportedFigure(intermediate, "ExternalSourceBaseOutput", transaction);
                GameObject tallOutput = CreateValidImportedFigure(intermediate, "ExternalSourceTallOutput", transaction);
                Assert.That(contents.Registry.TrySetOutfitCollection(contents, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, externalSource, baseOutput),
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry("Tall", externalSource, tallOutput)
                }, out string ownershipDiagnostic), Is.False);
                Assert.That(ownershipDiagnostic, Does.Contain("Database-owned"));
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            ShapeSyncMeshOutfitCollectionAuthoring.Source[] complete =
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, externalSource),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", externalSource)
            };
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, complete, out string firstSaveDiagnostic), Is.True, firstSaveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry oldEntry = saved.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            GameObject oldSource = oldEntry.SourcePrefab;
            GameObject oldOutput = oldEntry.CollectionPrefab;

            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, complete, out string secondSaveDiagnostic), Is.True, secondSaveDiagnostic);
            Assert.That(oldSource == null, Is.True, "Re-save must reclaim the prior Database-owned Collection source.");
            Assert.That(oldOutput == null, Is.True, "Re-save must reclaim the prior Database-owned Collection output.");

            Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, complete, out string rollbackDiagnostic), Is.False);
                Assert.That(rollbackDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase rolledBack, out openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry restored = rolledBack.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            Assert.That(restored.SourcePrefab, Is.Not.Null, "Failed re-save must restore the prior source artifact.");
            Assert.That(restored.CollectionPrefab, Is.Not.Null, "Failed re-save must restore the prior output artifact.");
            Assert.That(restored.CollectionPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null, "Failed re-save must restore the prior owned Mesh.");
        }

        [Test]
        public void DatabaseWindow_CollectionDraftMarksDirtyAndSavesThroughTheBcpPcmDetail()
        {
            const string databasePath = Root + "/CollectionWindowDatabase.prefab";
            const string sourcePath = Root + "/CollectionWindowSource.prefab";
            const string tallSourcePath = Root + "/CollectionWindowTallSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "CollectionWindow");
            CreatePersistentSkinnedSource(tallSourcePath, assetPrefix: "CollectionWindowTall");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject tallSource = AssetDatabase.LoadAssetAtPath<GameObject>(tallSourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            var window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitChildForTest("Coat", "Collections"), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.False);
                Assert.That(window.TrySetOutfitCollectionDraftForTest(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, source), Is.True);
                Assert.That(window.TrySetOutfitCollectionDraftForTest(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false,
                    "Tall", tallSource), Is.True);
                Assert.That(window.IsOutfitDetailDirtyForTest, Is.True);
                Assert.That(window.TrySaveOutfitForTest(out string saveDiagnostic), Is.True, saveDiagnostic);
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(AssetDatabase.GetAssetPath(saved.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourcePrefab), Is.EqualTo(databasePath));
        }

        [Test]
        public void OutfitCollection_FigureAxisRenameInvalidatesAndReclaimsEveryCollectionArtifact()
        {
            const string databasePath = Root + "/CollectionAxisInvalidationDatabase.prefab";
            const string sourcePath = Root + "/CollectionAxisInvalidationSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "CollectionAxisInvalidation");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, new[]
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry old = saved.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == "Tall");

            Func<GameObject, string, bool> originalRenameSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(databasePath, "Tall", "RenamedTall", out string rollbackRenameDiagnostic), Is.False);
                Assert.That(rollbackRenameDiagnostic, Does.Contain("could not be saved"));
            }
            finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalRenameSave; }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renameRolledBack, out openDiagnostic), Is.True, openDiagnostic);
            AssertCollectionArtifactsPresent(renameRolledBack, "Coat");

            Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(databasePath, "Tall", "RenamedTall", out string renameDiagnostic), Is.True, renameDiagnostic);
            Assert.That(old.SourcePrefab == null, Is.True);
            Assert.That(old.CollectionPrefab == null, Is.True);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(renamed.Registry.Outfits.Single().CollectionKind, Is.EqualTo(ShapeSyncDatabaseRegistry.OutfitCollectionKind.None));
            Assert.That(renamed.Registry.Outfits.Single().CollectionEntries, Is.Empty);
            AssertCollectionArtifactsAbsent(renamed, "Coat");
        }

        [Test]
        public void OutfitCollection_OutfitRemovalAndRejectedReplacementDoNotLeaveOrLoseArtifacts()
        {
            const string databasePath = Root + "/CollectionRemovalDatabase.prefab";
            const string sourcePath = Root + "/CollectionRemovalSource.prefab";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            CreatePersistentSkinnedSource(sourcePath, assetPrefix: "CollectionRemoval");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            ShapeSyncMeshOutfitCollectionAuthoring.Source[] complete =
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitCollectionAuthoring.Source("Tall", source)
            };
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, complete, out string saveDiagnostic), Is.True, saveDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase saved, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry retained = saved.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databasePath, "Coat", ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false,
                new[] { complete[0] }, out string rejectedDiagnostic), Is.False);
            Assert.That(rejectedDiagnostic, Does.Contain("every FBM"));
            Assert.That(retained.SourcePrefab, Is.Not.Null, "A rejected replacement rolls back the previous Collection source.");
            Assert.That(retained.CollectionPrefab, Is.Not.Null, "A rejected replacement rolls back the previous Collection output.");

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out saved, out openDiagnostic), Is.True, openDiagnostic);
            retained = saved.Registry.Outfits.Single().CollectionEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            var window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(saved, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Coat"), Is.True);
                Func<GameObject, string, bool> originalRemovalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(window.TryRemoveSelectedOutfitForTest(out string rollbackRemovalDiagnostic), Is.False);
                    Assert.That(rollbackRemovalDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalRemovalSave; }
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removalRolledBack, out openDiagnostic), Is.True, openDiagnostic);
            AssertCollectionArtifactsPresent(removalRolledBack, "Coat");
            window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.TrySetDatabase(removalRolledBack, out string bindDiagnostic), Is.True, bindDiagnostic);
                Assert.That(window.TrySelectOutfitForTest("Coat"), Is.True);
                Assert.That(window.TryRemoveSelectedOutfitForTest(out string removeDiagnostic), Is.True, removeDiagnostic);
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(retained.SourcePrefab == null, Is.True);
            Assert.That(retained.CollectionPrefab == null, Is.True);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase removed, out openDiagnostic), Is.True, openDiagnostic);
            Assert.That(removed.Registry.Outfits, Is.Empty);
            AssertCollectionArtifactsAbsent(removed, "Coat");
        }

        [Test]
        public void OutfitGenerate_MeshOutfitPublishesIndependentMeshMaterialAdapterAndRuntimeIdentity()
        {
            const string databasePath = Root + "/GenerateDatabase.prefab";
            const string sourcePath = Root + "/GenerateSource.prefab";
            const string outputPath = Root + "/Generated";
            PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Vest", "Vest", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("MaterialOnly", "MaterialOnly", ShapeSyncDatabaseRegistry.OutfitKind.Material, out outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string outfitSetupDiagnostic), Is.True, outfitSetupDiagnostic);
            Assert.That(ShapeSyncMeshOutfitImport.TryImportAxes(databasePath, "Vest", new[]
            {
                new KeyValuePair<string, GameObject>(ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new KeyValuePair<string, GameObject>("Tall", source)
            }, out string vestImportDiagnostic), Is.True, vestImportDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase vestDatabase, out openDiagnostic), Is.True, openDiagnostic);
            string vestMaterialName = vestDatabase.Registry.Outfits.Single(entry => entry.Identity == "Vest").AxisFigures
                .Single(axis => axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).SourceMaterialNames.Single();
            Assert.That(ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(databasePath, "Vest", new[]
            {
                new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(vestMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include, "VestEntry")
            }, out string vestClassificationDiagnostic), Is.True, vestClassificationDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out openDiagnostic), Is.True, openDiagnostic);

            AssetDatabase.CreateFolder(Root, "Generated");
            AssetDatabase.CreateFolder(Root + "/Generated", "Bindings");
            MeshBinding generatedBinding = ScriptableObject.CreateInstance<MeshBinding>();
            AssetDatabase.CreateAsset(generatedBinding, Root + "/Generated/Bindings/Master_MeshBinding.asset");

            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out string generateDiagnostic), Is.True, generateDiagnostic);

            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Coat.prefab");
            Assert.That(generated, Is.Not.Null);
            ShapeSyncOutfit runtimeOutfit = generated.GetComponent<ShapeSyncOutfit>();
            Assert.That(runtimeOutfit, Is.Not.Null);
            Assert.That(runtimeOutfit.RegistryId, Is.EqualTo("Coat"));
            Assert.That(runtimeOutfit.RegistryName, Is.EqualTo("Coat"));
            Assert.That(runtimeOutfit.SkinningProfile, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(runtimeOutfit.SkinningProfile), Is.EqualTo(outputPath + "/Coat_SkinningProfile.asset").IgnoreCase);
            Assert.That(runtimeOutfit.BaseExtraBoneRegistry, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(runtimeOutfit.BaseExtraBoneRegistry).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(runtimeOutfit.FbmExtraBoneRegistries, Has.Count.EqualTo(1));
            Assert.That(runtimeOutfit.FbmExtraBoneRegistries[0].blendName, Is.EqualTo("Tall"));
            Assert.That(runtimeOutfit.FbmExtraBoneRegistries[0].extraBoneRegistry, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(runtimeOutfit.FbmExtraBoneRegistries[0].extraBoneRegistry).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);

            SkinnedMeshRenderer renderer = generated.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(renderer.sharedMesh.GetBlendShapeIndex("Tall"), Is.GreaterThanOrEqualTo(0));
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BaseMap")).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);
            MaterialProxy proxy = generated.GetComponent<MaterialProxy>();
            Assert.That(proxy.Entries, Has.Count.EqualTo(1));
            Assert.That(proxy.Entries[0].entryName, Is.EqualTo("CoatEntry"));
            Assert.That(proxy.Entries[0].renderer, Is.SameAs(renderer));
            Assert.That(proxy.Entries[0].adapter, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(proxy.Entries[0].adapter).StartsWith(outputPath + "/", StringComparison.OrdinalIgnoreCase), Is.True);
            MeshBinding reloadedBinding = AssetDatabase.LoadAssetAtPath<MeshBinding>(Root + "/Generated/Bindings/Master_MeshBinding.asset");
            GameObject generatedVest = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Vest.prefab");
            Assert.That(generatedVest, Is.Not.Null);
            Assert.That(reloadedBinding.Outfits.Select(entry => (entry.logicalName, entry.outfitPrefab)), Is.EqualTo(new[]
            {
                ("Coat", generated),
                ("Vest", generatedVest)
            }), "Generated MeshBinding must map every Mesh Outfit Id to its generated Outfit prefab and exclude Material Outfits.");

            // The second Generate must update the same output assets without clearing
            // references held by the generated prefab.
            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out generateDiagnostic), Is.True, generateDiagnostic);
            generated = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Coat.prefab");
            renderer = generated.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMesh, Is.Not.Null);
            Assert.That(renderer.sharedMaterial, Is.Not.Null);
            Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(generated.GetComponent<MaterialProxy>().Entries.Single().adapter, Is.Not.Null);
            reloadedBinding = AssetDatabase.LoadAssetAtPath<MeshBinding>(Root + "/Generated/Bindings/Master_MeshBinding.asset");
            Assert.That(reloadedBinding.Outfits.Select(entry => entry.logicalName), Is.EqualTo(new[] { "Coat", "Vest" }),
                "A repeated Generate must preserve the complete Mesh Outfit binding table.");
        }

#if SHAPESYNC_RICH_TEST
        [Test]
        public void OutfitGenerateCleanup_RemovesSourceContainersAndVrmComponentsButKeepsMergedRendererAndBones()
        {
            MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod(
                "RemoveGeneratedOutfitSourceArtifacts", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            GameObject root = new GameObject("GeneratedOutfit");
            GameObject hair = new GameObject("Hair");
            GameObject face = new GameObject("Face");
            GameObject body = new GameObject("Body");
            GameObject skeleton = new GameObject("Root");
            GameObject bone = new GameObject("Bone");
            GameObject merged = new GameObject("MergedMesh");
            hair.transform.SetParent(root.transform);
            face.transform.SetParent(root.transform);
            body.transform.SetParent(root.transform);
            skeleton.transform.SetParent(root.transform);
            bone.transform.SetParent(skeleton.transform);
            merged.transform.SetParent(root.transform);
            merged.AddComponent<SkinnedMeshRenderer>();
            Type springColliderType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UniVRM10.VRM10SpringBoneCollider"))
                .FirstOrDefault(type => type != null);
            if (springColliderType != null) bone.gameObject.AddComponent(springColliderType);
            try
            {
                method.Invoke(null, new object[] { root });

                Assert.That(root.transform.Find("Hair"), Is.Null);
                Assert.That(root.transform.Find("Face"), Is.Null);
                Assert.That(root.transform.Find("Body"), Is.Null);
                Assert.That(root.transform.Find("Root/Bone"), Is.Not.Null);
                Assert.That(root.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null);
                Assert.That(root.transform.Find("Root/Bone").GetComponents<Component>().Any(component =>
                    component != null && (component.GetType().FullName ?? string.Empty).IndexOf("SpringBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
                    "VRM collider components must be removed without deleting the registered Extra Bone transform.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            const string humanTestGeneratedOutfitPath = "Assets/zgock/ShapeSync/PlayTest/Spec20/Generated/Outfits/hair-1.prefab";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(humanTestGeneratedOutfitPath);
            if (source == null)
            {
                string consumerFixturePath = ShapeSyncTestAssetPaths.ConsumerAssetPath("zgock/ShapeSync/PlayTest/Spec20/Generated/Outfits/hair-1.prefab");
                source = AssetDatabase.LoadAssetAtPath<GameObject>(consumerFixturePath);
            }
            if (source == null) Assert.Ignore("The Human Test Generated Outfit fixture is not present.");
            GameObject instance = Object.Instantiate(source);
            try
            {
                method.Invoke(null, new object[] { instance });
                foreach (Component component in instance.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component is Transform) continue;
                    string fullName = component.GetType().FullName ?? string.Empty;
                    Assert.That(fullName.StartsWith("VRM.", StringComparison.Ordinal)
                        || fullName.StartsWith("VRM10.", StringComparison.Ordinal)
                        || fullName.StartsWith("UniVRM.", StringComparison.Ordinal)
                        || fullName.StartsWith("UniVRM10.", StringComparison.Ordinal)
                        || string.Equals(fullName, "UniHumanoid.Humanoid", StringComparison.Ordinal), Is.False,
                        "Generated Outfit must not retain source VRM component: " + fullName);
                }
                Assert.That(instance.GetComponentsInChildren<Animator>(true), Is.Empty,
                    "Generated Outfit must not retain an Animator or external Avatar reference.");
                Assert.That(instance.transform.Find("Hair"), Is.Null);
                Assert.That(instance.transform.Find("Face"), Is.Null);
                Assert.That(instance.transform.Find("Body"), Is.Null);
                Assert.That(instance.transform.Find("secondary"), Is.Null);
                Assert.That(instance.GetComponentInChildren<SkinnedMeshRenderer>(true), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
#endif

        [Test]
        public void OutfitGenerateExtraBoneRegistry_ExcludesRendererContainersButKeepsExtraBones()
        {
            MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod(
                "BuildExtraBoneRegistry", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            GameObject outfit = new GameObject("Outfit");
            GameObject root = new GameObject("Root");
            root.transform.SetParent(outfit.transform);
            GameObject extra = new GameObject("ExtraBone");
            extra.transform.SetParent(root.transform);
            GameObject face = new GameObject("Face");
            face.transform.SetParent(outfit.transform);
            GameObject faceMesh = new GameObject("FaceMesh");
            faceMesh.transform.SetParent(face.transform);
            faceMesh.AddComponent<SkinnedMeshRenderer>();
            GameObject figure = new GameObject("Figure");
            try
            {
                zgock.ShapeSync.CharacterBoneRegistry registry = (zgock.ShapeSync.CharacterBoneRegistry)method.Invoke(null, new object[] { outfit, figure, string.Empty });
                string[] paths = registry.bonePoses.Select(pose => pose.boneName).ToArray();
                Assert.That(paths, Does.Contain("Root/ExtraBone"));
                Assert.That(paths, Does.Not.Contain("Face"));
                Assert.That(paths.Any(path => path.StartsWith("Face/", StringComparison.Ordinal)), Is.False);
                Object.DestroyImmediate(registry);
            }
            finally
            {
                Object.DestroyImmediate(outfit);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void OutfitGenerate_UsesRendererStructureInsteadOfRendererNames()
        {
            const string databasePath = Root + "/GenerateStructuralRendererDatabase.prefab";
            const string sourcePath = Root + "/GenerateStructuralRendererSource.prefab";
            const string outputPath = Root + "/GeneratedStructuralRenderer";
            PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry tall = contents.Registry.Outfits.Single().AxisFigures.Single(axis => axis.ShapeKey == "Tall");
                SkinnedMeshRenderer renderer = tall.OutfitPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                renderer.gameObject.name = "RendererNameDoesNotParticipateInMatching";
            }, out string renameDiagnostic), Is.True, renameDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);

            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out string generateDiagnostic), Is.True, generateDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Coat.prefab"), Is.Not.Null);
        }

        [Test]
        public void OutfitGenerate_RejectsRendererStructuralMismatchWithoutNameFallback()
        {
            GameObject baseRoot = new GameObject("StructuralBase");
            GameObject targetRoot = new GameObject("StructuralTarget");
            Mesh baseMesh = new Mesh { name = "StructuralBaseMesh" };
            baseMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            baseMesh.triangles = new[] { 0, 1, 2 };
            Mesh targetMesh = new Mesh { name = "StructuralTargetMesh" };
            targetMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            targetMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            baseRoot.AddComponent<SkinnedMeshRenderer>().sharedMesh = baseMesh;
            targetRoot.AddComponent<SkinnedMeshRenderer>().sharedMesh = targetMesh;
            try
            {
                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("TryResolveStructuralRenderers", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { baseRoot, targetRoot, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.False);
                Assert.That((string)arguments[3], Does.Contain("SubMeshStructure"));
            }
            finally
            {
                Object.DestroyImmediate(baseMesh);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(baseRoot);
                Object.DestroyImmediate(targetRoot);
            }
        }

        [Test]
        public void OutfitGenerate_MapsDeltaBySubMeshVertexRangeWhenUnusedVertexCountsDiffer()
        {
            Mesh baseMesh = new Mesh { name = "SubMeshRangeBase" };
            baseMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, Vector3.back };
            baseMesh.triangles = new[] { 0, 1, 2 };
            Mesh targetMesh = new Mesh { name = "SubMeshRangeTarget" };
            targetMesh.vertices = new[] { Vector3.one, Vector3.right, Vector3.up };
            targetMesh.triangles = new[] { 0, 1, 2 };
            try
            {
                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("TryBuildVertexDelta", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { baseMesh, targetMesh, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.True, (string)arguments[3]);
                Vector3[] delta = (Vector3[])arguments[2];
                Assert.That(delta, Has.Length.EqualTo(baseMesh.vertexCount));
                Assert.That(delta[0], Is.EqualTo(Vector3.one));
                Assert.That(delta[3], Is.EqualTo(Vector3.zero));
                Assert.That(delta[4], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(baseMesh);
                Object.DestroyImmediate(targetMesh);
            }
        }

        [Test]
        public void OutfitGenerate_PbmFbmCombinationStoresPbmOnlyDelta()
        {
            Mesh baseMesh = CreateDeltaMesh(new[] { Vector3.zero, Vector3.right, Vector3.up });
            Mesh fbmMesh = CreateDeltaMesh(new[] { new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 0f) });
            Mesh basePbmMesh = CreateDeltaMesh(new[] { new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(2f, 1f, 0f) });
            Mesh combinedMesh = CreateDeltaMesh(new[] { new Vector3(4f, 0f, 0f), new Vector3(5f, 0f, 0f), new Vector3(4f, 1f, 0f) });
            try
            {
                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("TryBuildPbmDifferenceDelta", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { baseMesh, combinedMesh, fbmMesh, basePbmMesh, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.True, (string)arguments[5]);
                Vector3[] delta = (Vector3[])arguments[4];
                Assert.That(delta, Has.Length.EqualTo(3));
                Assert.That(delta[0], Is.EqualTo(new Vector3(1f, 0f, 0f)), "PBM_[FBM]_[PBM] must subtract both the Base PBM and FBM contributions from the combined target.");
                Assert.That(delta[1], Is.EqualTo(new Vector3(1f, 0f, 0f)));
                Assert.That(delta[2], Is.EqualTo(new Vector3(1f, 0f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(baseMesh);
                Object.DestroyImmediate(fbmMesh);
                Object.DestroyImmediate(basePbmMesh);
                Object.DestroyImmediate(combinedMesh);
            }
        }

        [Test]
        public void OutfitGenerate_PbmFollowPublishesBaseAndFbmFramesWithoutRegistryLeakage()
        {
            const string databasePath = Root + "/GeneratePbmDatabase.prefab";
            const string sourcePath = Root + "/GeneratePbmSource.prefab";
            const string outputPath = Root + "/GeneratedPbm";
            PrepareSavedPbmFollow(databasePath, sourcePath);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            ConfigurePbmFollowDifferenceFixture(database);

            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out string generateDiagnostic), Is.True, generateDiagnostic);

            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Coat.prefab");
            SkinnedMeshRenderer renderer = generated.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer.sharedMesh.GetBlendShapeIndex("PBM_Pose"), Is.GreaterThanOrEqualTo(0));
            int differenceIndex = renderer.sharedMesh.GetBlendShapeIndex("PBM_Tall_Pose");
            Assert.That(differenceIndex, Is.GreaterThanOrEqualTo(0));
            var vertices = new Vector3[renderer.sharedMesh.vertexCount];
            renderer.sharedMesh.GetBlendShapeFrameVertices(differenceIndex, 0, vertices, new Vector3[vertices.Length], new Vector3[vertices.Length]);
            Assert.That(vertices[0].x, Is.EqualTo(1f).Within(0.0001f),
                "PBM_[FBM]_[PBM] must be the PBM Baker difference: combined - Base PBM - FBM.");
            string generatedMeshPath = AssetDatabase.GetAssetPath(renderer.sharedMesh);
            Assert.That(generatedMeshPath, Does.StartWith(outputPath + "/"));
            string generatedMeshGuid = AssetDatabase.AssetPathToGUID(generatedMeshPath);

            // Re-generation must update the existing Mesh asset in place.  Changing only
            // the combined PBM source makes the expected difference unambiguous while
            // preserving the output GUID used by the generated prefab.
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow = outfit.PbmFollows.Single(entry => entry.PbmAxisName == "Pose");
            SetMeshVertices(follow.Figures.Single(entry => entry.ShapeKey == "Tall").Figure,
                new[] { new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f) });
            AssetDatabase.SaveAssets();
            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out generateDiagnostic), Is.True, generateDiagnostic);

            generated = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Coat.prefab");
            renderer = generated.GetComponentInChildren<SkinnedMeshRenderer>(true);
            differenceIndex = renderer.sharedMesh.GetBlendShapeIndex("PBM_Tall_Pose");
            vertices = new Vector3[renderer.sharedMesh.vertexCount];
            renderer.sharedMesh.GetBlendShapeFrameVertices(differenceIndex, 0, vertices, new Vector3[vertices.Length], new Vector3[vertices.Length]);
            Assert.That(vertices[0].x, Is.EqualTo(2f).Within(0.0001f),
                "Re-generation must replace the existing PBM difference frame, not retain the previous payload.");
            Assert.That(AssetDatabase.AssetPathToGUID(generatedMeshPath), Is.EqualTo(generatedMeshGuid),
                "Re-generation must preserve the generated Mesh GUID.");
            AssetDatabase.ImportAsset(generatedMeshPath, ImportAssetOptions.ForceUpdate);
            Mesh reloadedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(generatedMeshPath);
            int reloadedDifferenceIndex = reloadedMesh.GetBlendShapeIndex("PBM_Tall_Pose");
            var reloadedVertices = new Vector3[reloadedMesh.vertexCount];
            reloadedMesh.GetBlendShapeFrameVertices(reloadedDifferenceIndex, 0, reloadedVertices,
                new Vector3[reloadedVertices.Length], new Vector3[reloadedVertices.Length]);
            Assert.That(reloadedVertices[0].x, Is.EqualTo(2f).Within(0.0001f),
                "The overwritten Mesh asset must retain the PBM difference after AssetDatabase reimport.");
            Assert.That(generated.GetComponentsInChildren<ShapeSyncDatabase>(true), Is.Empty);
        }

        [Test]
        public void OutfitGenerate_MaterialOutfitWithOwnedTextureDoesNotRequireAMeshRuntimePrefab()
        {
            const string databasePath = Root + "/GenerateMaterialOutfitDatabase.prefab";
            const string outputPath = Root + "/GeneratedMaterialOutfit";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, transaction) =>
            {
                Assert.That(contents.Registry.TryAddOutfit("Skin", "Skin", ShapeSyncDatabaseRegistry.OutfitKind.Material, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                Texture2D texture = new Texture2D(1, 1) { name = "Skin_Albedo" };
                transaction.AddSubAsset(texture);
                Assert.That(contents.Registry.TryRegisterTextureResource("Skin_Albedo", texture,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Skin"), ShapeSyncDatabaseRegistry.TextureResourceUsage.MaterialOutfit, out string resourceDiagnostic), Is.True, resourceDiagnostic);
                Assert.That(contents.Registry.TrySetMaterialOutfitTextureEntries("Skin", new[]
                {
                    new ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry("Albedo", "Skin_Albedo")
                }, out string entryDiagnostic), Is.True, entryDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string openDiagnostic), Is.True, openDiagnostic);

            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out string generateDiagnostic), Is.True, generateDiagnostic);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(outputPath + "/Skin.prefab"), Is.Null);
        }

        [Test]
        public void OutfitGenerate_CollectionBoneProfileUsesFigureHumanoidAsPathAuthority()
        {
            GameObject figure = CreateHumanoidGeneratorSource("CollectionFigure");
            GameObject collection = CreateHumanoidGeneratorSource("CollectionTarget");
            try
            {
                Transform figureSpine = figure.transform.Find("Hips/Spine");
                Transform collectionSpine = collection.transform.Find("Hips/Spine");
                Assert.That(figureSpine, Is.Not.Null);
                Assert.That(collectionSpine, Is.Not.Null);
                collectionSpine.localPosition += new Vector3(0.025f, 0f, 0f);

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod(
                    "BuildCollectionBoneProfile", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null, "Collection bone transfer helper must remain present.");
                ShapeSyncHumanoidBoneCorrectionProfile profile = (ShapeSyncHumanoidBoneCorrectionProfile)method.Invoke(null, new object[] { figure, collection });
                Assert.That(profile, Is.Not.Null);
                Assert.That(profile.Corrections.Any(value => value != null
                    && value.bone == HumanBodyBones.Spine
                    && Mathf.Abs(value.localPositionDelta.x - 0.025f) <= 0.0001f), Is.True,
                    "Collection correction must be keyed by the Figure Humanoid bone, not by a collection-local Animator.");
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(collection);
            }
        }

        [Test]
        public void OutfitGenerate_ProjectionCollectionResolvesSingleRendererWithoutFigurePathNameMatch()
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = new ShapeSyncDatabaseRegistry.OutfitEntry(
                "Shoes", "Shoes", ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
            GameObject projection = new GameObject("Shoes_Base_Projection");
            Mesh mesh = new Mesh { name = "ShoesProjectionMesh" };
            GameObject rendererObject = new GameObject("BasicFemaleShoes1_MergedMesh");
            rendererObject.transform.SetParent(projection.transform, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            try
            {
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis =
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(
                        ShapeSyncDatabaseRegistry.BaseShapeKey, null, null, null, projection, Array.Empty<string>());
                outfit.SetAxisFigures(new[] { axis });
                outfit.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, true, Array.Empty<ShapeSyncDatabaseRegistry.OutfitCollectionEntry>());

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod(
                    "FindCollectionRenderer", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object result = method.Invoke(null, new object[]
                {
                    outfit,
                    null,
                    ShapeSyncDatabaseRegistry.BaseShapeKey,
                    "BasicFemale_MergedMesh"
                });
                Assert.That(result, Is.SameAs(renderer),
                    "Projection resolution must use the single structural Renderer, not the Figure Renderer name/path.");
            }
            finally
            {
                Object.DestroyImmediate(projection);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void OutfitGenerate_ProjectionCollectionRejectsAmbiguousRendererPayload()
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = new ShapeSyncDatabaseRegistry.OutfitEntry(
                "Shoes", "Shoes", ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
            GameObject projection = new GameObject("Shoes_Base_Projection");
            Mesh firstMesh = new Mesh { name = "ShoesProjectionMesh1" };
            Mesh secondMesh = new Mesh { name = "ShoesProjectionMesh2" };
            try
            {
                GameObject first = new GameObject("ProjectionRendererA");
                first.transform.SetParent(projection.transform, false);
                first.AddComponent<SkinnedMeshRenderer>().sharedMesh = firstMesh;
                GameObject second = new GameObject("ProjectionRendererB");
                second.transform.SetParent(projection.transform, false);
                second.AddComponent<SkinnedMeshRenderer>().sharedMesh = secondMesh;
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis =
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(
                        ShapeSyncDatabaseRegistry.BaseShapeKey, null, null, null, projection, Array.Empty<string>());
                outfit.SetAxisFigures(new[] { axis });
                outfit.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, true, Array.Empty<ShapeSyncDatabaseRegistry.OutfitCollectionEntry>());

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod(
                    "FindCollectionRenderer", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[]
                {
                    outfit,
                    null,
                    ShapeSyncDatabaseRegistry.BaseShapeKey,
                    "BasicFemale_MergedMesh"
                }));
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message, Does.Contain("OutfitGenerateCollectionRendererAmbiguous"));
            }
            finally
            {
                Object.DestroyImmediate(projection);
                Object.DestroyImmediate(firstMesh);
                Object.DestroyImmediate(secondMesh);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmDeltaSubtractsBaseTargetFbmAndBoneCorrectionTerms()
        {
            MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("Subtract", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Vector3[] result = (Vector3[])method.Invoke(null, new object[]
            {
                (object)new Vector3[][]
                {
                    new[] { new Vector3(10f, 2f, 0f) },
                    new[] { new Vector3(3f, 1f, 0f) },
                    new[] { new Vector3(2f, 0f, 0f) },
                    new[] { new Vector3(1f, 0f, 0f) },
                    new[] { new Vector3(0.5f, 0f, 0f) }
                }
            });
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].x, Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(result[0].y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void OutfitGenerate_CollectionPcmProjectionMaskExcludesVerticesOutsideCorrectedBoneNeighborhood()
        {
            GameObject root = CreateHumanoidGeneratorSource("PcmProjectionMaskSource");
            Mesh mesh = new Mesh { name = "PcmProjectionMaskMesh" };
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                Transform leftUpperArm = root.transform.Find("Hips/Spine/Chest/LeftUpperArm");
                Transform hips = root.transform.Find("Hips");
                Assert.That(leftUpperArm, Is.Not.Null);
                Assert.That(hips, Is.Not.Null);
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { hips, leftUpperArm };
                mesh.vertices = new[] { Vector3.zero, Vector3.right };
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f }
                };
                renderer.sharedMesh = mesh;
                profile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection
                    {
                        bone = HumanBodyBones.LeftUpperArm,
                        localRotationDelta = Quaternion.AngleAxis(10f, Vector3.forward)
                    }
                });

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("TryBuildProfileProjectionMask", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { root, renderer, mesh, profile, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.True, arguments[5] as string);
                bool[] mask = (bool[])arguments[4];
                Assert.That(mask, Is.EqualTo(new[] { false, true }), "PCM projection must not affect vertices weighted only to unrelated bones.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmProjectionLeavesExcludedBoneVerticesUnchanged()
        {
            GameObject root = CreateHumanoidGeneratorSource("PcmProjectionDeltaSource");
            Mesh sourceMesh = new Mesh { name = "PcmProjectionDeltaSourceMesh" };
            Mesh targetMesh = new Mesh { name = "PcmProjectionDeltaTargetMesh" };
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                Transform leftUpperArm = root.transform.Find("Hips/Spine/Chest/LeftUpperArm");
                Transform hips = root.transform.Find("Hips");
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { hips, leftUpperArm };
                sourceMesh.vertices = new[] { new Vector3(0f, 0f, 1f), new Vector3(0.1f, 0f, 0.01f) };
                sourceMesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f }
                };
                renderer.sharedMesh = sourceMesh;
                targetMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                targetMesh.triangles = new[] { 0, 1, 2 };
                profile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection
                    {
                        bone = HumanBodyBones.LeftUpperArm,
                        localRotationDelta = Quaternion.AngleAxis(10f, Vector3.forward)
                    }
                });

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("BuildCollectionProjectionDelta", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                object[] arguments = { root, renderer, sourceMesh, targetMesh, profile, new Vector3[sourceMesh.vertexCount], "LeftUpperArm" };
                Vector3[] result = (Vector3[])method.Invoke(null, arguments);
                Assert.That(result[0], Is.EqualTo(Vector3.zero), "Vertices outside the corrected bone neighbourhood must not be projected.");
                Assert.That(result[1].z, Is.LessThan(-0.009f), "The selected vertex must receive the PCM surface residual.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmProjectionMaskSkipsPositionOnlyHipsCorrection()
        {
            GameObject root = CreateHumanoidGeneratorSource("PcmProjectionHipsSource");
            Mesh mesh = new Mesh { name = "PcmProjectionHipsMesh" };
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                Transform hips = root.transform.Find("Hips");
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { hips };
                mesh.vertices = new[] { Vector3.zero };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
                renderer.sharedMesh = mesh;
                profile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection
                    {
                        bone = HumanBodyBones.Hips,
                        localPositionDelta = Vector3.up,
                        localRotationDelta = Quaternion.identity,
                        localScaleDelta = Vector3.zero
                    }
                });

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("TryBuildProfileProjectionMask", BindingFlags.NonPublic | BindingFlags.Static);
                object[] arguments = { root, renderer, mesh, profile, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.True, arguments[5] as string);
                Assert.That((bool[])arguments[4], Is.EqualTo(new[] { false }), "Position-only Hips correction is represented by global movement, not surface projection.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmProjectionEmptyProfileProducesZeroResidual()
        {
            GameObject root = CreateHumanoidGeneratorSource("PcmProjectionEmptyProfileSource");
            Mesh sourceMesh = new Mesh { name = "PcmProjectionEmptySourceMesh" };
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                sourceMesh.vertices = new[] { Vector3.zero };
                sourceMesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
                renderer.sharedMesh = sourceMesh;
                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("BuildCollectionProjectionDelta", BindingFlags.NonPublic | BindingFlags.Static);
                Vector3[] result = (Vector3[])method.Invoke(null, new object[]
                {
                    root, renderer, sourceMesh, null, profile, new Vector3[sourceMesh.vertexCount], "Empty"
                });
                Assert.That(result, Is.EqualTo(new[] { Vector3.zero }));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmProjectionRejectsResidualBeyondConfiguredDistance()
        {
            GameObject root = CreateHumanoidGeneratorSource("PcmProjectionDistanceSource");
            Mesh sourceMesh = new Mesh { name = "PcmProjectionDistanceSourceMesh" };
            Mesh targetMesh = new Mesh { name = "PcmProjectionDistanceTargetMesh" };
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                Transform leftUpperArm = root.transform.Find("Hips/Spine/Chest/LeftUpperArm");
                Transform hips = root.transform.Find("Hips");
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = new[] { hips, leftUpperArm };
                sourceMesh.vertices = new[] { new Vector3(0f, 0f, 1f) };
                sourceMesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 1, weight0 = 1f } };
                renderer.sharedMesh = sourceMesh;
                targetMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                targetMesh.triangles = new[] { 0, 1, 2 };
                profile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection
                    {
                        bone = HumanBodyBones.LeftUpperArm,
                        localRotationDelta = Quaternion.identity
                    }
                });

                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("BuildCollectionProjectionDelta", BindingFlags.NonPublic | BindingFlags.Static);
                TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[]
                {
                    root, renderer, sourceMesh, targetMesh, profile, new Vector3[sourceMesh.vertexCount], "Distance"
                }));
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message, Does.Contain("OutfitGenerateCollectionPcmProjectionFailed"));
                Assert.That(exception.InnerException.Message, Does.Contain("exceeding Max Projection Distance"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OutfitGenerate_CollectionPcmPayloadStoresMaskedBaseAndFbmResidualFrames()
        {
            const string databasePath = Root + "/PcmPayloadDatabase.prefab";
            const string generatedFolder = Root + "/PcmGenerated";
            const string payloadFolder = Root + "/PcmPayload";
            CreateDatabaseWithFbmAxis(databasePath, "Tall");
            EnsureTestFolder(generatedFolder);
            EnsureTestFolder(payloadFolder);

            GameObject generatedFigure = CreateHumanoidGeneratorSource("Master");
            Transform hips = generatedFigure.transform.Find("Hips");
            Transform leftUpperArm = generatedFigure.transform.Find("Hips/Spine/Chest/LeftUpperArm");
            SkinnedMeshRenderer generatedRenderer = generatedFigure.AddComponent<SkinnedMeshRenderer>();
            Mesh generatedMesh = new Mesh { name = "PcmPayloadFigureMesh" };
            generatedMesh.vertices = new[]
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0.1f, 0f, 0.01f),
                new Vector3(0.2f, 0f, 0.01f)
            };
            generatedMesh.triangles = new[] { 0, 1, 2 };
            generatedMesh.bindposes = new[] { hips.worldToLocalMatrix * generatedFigure.transform.localToWorldMatrix, leftUpperArm.worldToLocalMatrix * generatedFigure.transform.localToWorldMatrix };
            generatedMesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f }
            };
            generatedMesh.AddBlendShapeFrame("Tall", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
            generatedRenderer.sharedMesh = generatedMesh;
            generatedRenderer.bones = new[] { hips, leftUpperArm };
            generatedRenderer.rootBone = hips;
            string generatedPath = generatedFolder + "/Master.prefab";
            AssetDatabase.CreateAsset(generatedFigure.GetComponent<Animator>().avatar, generatedFolder + "/Master_Avatar.asset");
            AssetDatabase.CreateAsset(generatedMesh, generatedFolder + "/Master_Mesh.asset");
            Assert.That(PrefabUtility.SaveAsPrefabAsset(generatedFigure, generatedPath), Is.Not.Null);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Object.DestroyImmediate(generatedFigure);
            generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(generatedPath);
            Assert.That(generatedFigure, Is.Not.Null);
            Assert.That(generatedFigure.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
            Assert.That(generatedFigure.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh, Is.Not.Null);

            GameObject baseProjection = CreatePcmProjectionPrefab("PcmPayloadBaseProjection");
            GameObject fbmProjection = CreatePcmProjectionPrefab("PcmPayloadFbmProjection");
            GameObject runtimeRoot = new GameObject("PcmPayloadRuntimeOutfit");
            ShapeSyncHumanoidBoneCorrectionProfile baseProfile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            ShapeSyncHumanoidBoneCorrectionProfile fbmProfile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            try
            {
                var correction = new ShapeSyncHumanoidBoneCorrection
                {
                    bone = HumanBodyBones.LeftUpperArm,
                    localRotationDelta = Quaternion.identity
                };
                baseProfile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection> { correction });
                fbmProfile.SetCorrectionsForEditor(new List<ShapeSyncHumanoidBoneCorrection> { new ShapeSyncHumanoidBoneCorrection
                {
                    bone = HumanBodyBones.LeftUpperArm,
                    localRotationDelta = Quaternion.identity
                }});
                var outfit = new ShapeSyncDatabaseRegistry.OutfitEntry("Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
                outfit.SetAxisFigures(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseProjection, baseProjection, baseProjection, baseProjection, new[] { "Material" }),
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry("Tall", fbmProjection, fbmProjection, fbmProjection, fbmProjection, new[] { "Material" })
                });
                outfit.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, true, new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseProjection, baseProjection),
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry("Tall", fbmProjection, fbmProjection)
                });
                var fbmProfiles = new[] { new ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile { blendName = "Tall", targetProfile = fbmProfile } };
                Type profilesType = typeof(ShapeSyncOutfitGenerator).GetNestedType("CollectionProfiles", BindingFlags.NonPublic);
                object profiles = Activator.CreateInstance(profilesType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new object[] { baseProfile, fbmProfiles }, null);
                ShapeSyncOutfit runtimeOutfit = runtimeRoot.AddComponent<ShapeSyncOutfit>();
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
                MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("ConfigureCollectionPcmPayload", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(method, Is.Not.Null);
                method.Invoke(null, new object[] { database, runtimeOutfit, outfit, profiles, generatedFolder, payloadFolder });

                ProfileControlledMorphAsset payload = runtimeOutfit.ProfileControlledMorphAsset;
                Assert.That(payload, Is.Not.Null);
                Mesh payloadMesh = payload.PayloadMesh;
                int baseIndex = payloadMesh.GetBlendShapeIndex("PCM_Coat");
                int fbmIndex = payloadMesh.GetBlendShapeIndex("PCM_Tall_Coat");
                Assert.That(baseIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(fbmIndex, Is.GreaterThanOrEqualTo(0));
                Vector3[] baseFrame = new Vector3[payloadMesh.vertexCount];
                Vector3[] fbmFrame = new Vector3[payloadMesh.vertexCount];
                payloadMesh.GetBlendShapeFrameVertices(baseIndex, 0, baseFrame, null, null);
                payloadMesh.GetBlendShapeFrameVertices(fbmIndex, 0, fbmFrame, null, null);
                Assert.That(baseFrame[0], Is.EqualTo(Vector3.zero), "Base PCM must not project the Hips-weighted vertex.");
                Assert.That(baseFrame[1].z, Is.LessThan(-0.009f));
                Assert.That(baseFrame[2].z, Is.LessThan(-0.009f));
                Assert.That(fbmFrame.All(value => value == Vector3.zero), Is.True, "FBM PCM must store target projection residual minus Base residual.");
            }
            finally
            {
                Object.DestroyImmediate(baseProfile);
                Object.DestroyImmediate(fbmProfile);
                Object.DestroyImmediate(runtimeRoot);
                Object.DestroyImmediate(baseProjection);
                Object.DestroyImmediate(fbmProjection);
                if (generatedFigure != null && AssetDatabase.GetAssetPath(generatedFigure) == string.Empty) Object.DestroyImmediate(generatedFigure);
            }
        }

        private static GameObject CreatePcmProjectionPrefab(string name)
        {
            GameObject root = new GameObject(name);
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            renderer.sharedMesh = mesh;
            return root;
        }

        [Test]
        public void OutfitGenerate_InvalidSnapshotRejectsBeforeCreatingOutputFolder()
        {
            const string databasePath = Root + "/GeneratePreflightDatabase.prefab";
            const string outputPath = Root + "/GeneratePreflightInvalid";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                GameObject figure = new GameObject("Master");
                figure.transform.SetParent(contents.transform.Find("Intermediate"), false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", figure, out string figureDiagnostic), Is.True, figureDiagnostic);
                Assert.That(contents.Registry.TryAddOutfit("Broken", "Broken", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string openDiagnostic), Is.True, openDiagnostic);

            Assert.That(ShapeSyncOutfitGenerator.TryGenerate(database, outputPath, "Bindings", string.Empty, out string generateDiagnostic), Is.False);
            Assert.That(generateDiagnostic, Does.Contain("Base Outfit Prefab"));
            Assert.That(AssetDatabase.IsValidFolder(outputPath), Is.False, "Input-contract rejection must happen before an output folder is created.");
        }

        [Test]
        public void OutfitGenerate_NormalBindingTransfersOutfitOwnerAndResolvesMaterialBindingTexture()
        {
            const string databasePath = Root + "/GenerateNormalBindingDatabase.prefab";
            const string bindingsPath = Root + "/Bindings";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                GameObject figure = new GameObject("Master");
                figure.transform.SetParent(contents.transform.Find("Intermediate"), false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", figure, out string figureDiagnostic), Is.True, figureDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            EnsureTestFolder(bindingsPath);
            Texture2D texture = new Texture2D(1, 1) { name = "Coat_Normal" };
            AssetDatabase.CreateAsset(texture, Root + "/Coat_Normal.asset");
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Coat_Normal.asset");
            MaterialBinding materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
            AssetDatabase.CreateAsset(materialBinding, bindingsPath + "/Master_MaterialBinding.asset");
            SerializedObject materialSerialized = new SerializedObject(materialBinding);
            SerializedProperty materialTextures = materialSerialized.FindProperty("textures");
            materialTextures.arraySize = 1;
            materialTextures.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "Coat_Normal";
            materialTextures.GetArrayElementAtIndex(0).FindPropertyRelative("sourceTexture").objectReferenceValue = texture;
            materialSerialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            materialBinding = AssetDatabase.LoadAssetAtPath<MaterialBinding>(bindingsPath + "/Master_MaterialBinding.asset");
            Assert.That(materialBinding.Textures.Single().logicalName, Is.EqualTo("Coat_Normal"));
            Assert.That(materialBinding.Textures.Single().sourceTexture, Is.SameAs(texture));
            MeshBinding meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
            AssetDatabase.CreateAsset(meshBinding, bindingsPath + "/Master_MeshBinding.asset");
            AssetDatabase.SaveAssets();
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string openDiagnostic), Is.True, openDiagnostic);

            Type generatedNormalType = typeof(ShapeSyncOutfitGenerator).GetNestedType("GeneratedNormal", BindingFlags.NonPublic);
            Assert.That(generatedNormalType, Is.Not.Null);
            object generatedNormal = Activator.CreateInstance(generatedNormalType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new object[] { "Coat", "CoatEntry", ShapeSyncDatabaseRegistry.BaseShapeKey, "Coat_Normal" }, null);
            Type listType = typeof(List<>).MakeGenericType(generatedNormalType);
            object generatedNormals = Activator.CreateInstance(listType);
            listType.GetMethod("Add").Invoke(generatedNormals, new[] { generatedNormal });
            MethodInfo method = typeof(ShapeSyncOutfitGenerator).GetMethod("ConfigureFigureNormalBindings", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new[] { database, Root, "Bindings", generatedNormals });

            Assert.That(meshBinding.NormalOwners, Has.Count.EqualTo(1));
            Assert.That(meshBinding.NormalOwners[0].outfitRegistryId, Is.EqualTo("Coat"));
            Assert.That(meshBinding.NormalOwners[0].targets.Single().targetName, Is.EqualTo(string.Empty));
            Assert.That(meshBinding.NormalOwners[0].targets.Single().textures.Single().entryName, Is.EqualTo("CoatEntry"));
            Assert.That(meshBinding.NormalOwners[0].targets.Single().textures.Single().normalTexture, Is.SameAs(texture));
        }

        private static GameObject CreateHumanoidGeneratorSource(string name)
        {
            GameObject root = new GameObject(name);
            Animator animator = root.AddComponent<Animator>();
            var bones = new List<Transform>();
            Transform hips = AddGeneratorBone(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddGeneratorBone(hips, "Spine", Vector3.up * .15f, bones);
            Transform chest = AddGeneratorBone(spine, "Chest", Vector3.up * .15f, bones);
            Transform neck = AddGeneratorBone(chest, "Neck", Vector3.up * .15f, bones);
            AddGeneratorBone(neck, "Head", Vector3.up * .12f, bones);
            Transform leftUpperArm = AddGeneratorBone(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones);
            Transform leftLowerArm = AddGeneratorBone(leftUpperArm, "LeftLowerArm", Vector3.left * .2f, bones);
            AddGeneratorBone(leftLowerArm, "LeftHand", Vector3.left * .18f, bones);
            Transform rightUpperArm = AddGeneratorBone(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones);
            Transform rightLowerArm = AddGeneratorBone(rightUpperArm, "RightLowerArm", Vector3.right * .2f, bones);
            AddGeneratorBone(rightLowerArm, "RightHand", Vector3.right * .18f, bones);
            Transform leftUpperLeg = AddGeneratorBone(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones);
            Transform leftLowerLeg = AddGeneratorBone(leftUpperLeg, "LeftLowerLeg", Vector3.down * .35f, bones);
            AddGeneratorBone(leftLowerLeg, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rightUpperLeg = AddGeneratorBone(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones);
            Transform rightLowerLeg = AddGeneratorBone(rightUpperLeg, "RightLowerLeg", Vector3.down * .35f, bones);
            AddGeneratorBone(rightLowerLeg, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var human = names.Select(value => new HumanBone { boneName = value, humanName = value, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            var skeleton = new List<SkeletonBone> { new SkeletonBone { name = root.name, position = root.transform.localPosition, rotation = root.transform.localRotation, scale = root.transform.localScale } };
            skeleton.AddRange(bones.Select(value => new SkeletonBone { name = value.name, position = value.localPosition, rotation = value.localRotation, scale = value.localScale }));
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
            animator.avatar = avatar;
            return root;
        }

        private static Transform AddGeneratorBone(Transform parent, string name, Vector3 position, ICollection<Transform> bones)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = position;
            bones.Add(bone);
            return bone;
        }

        private static void EnsureTestFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) Assert.That(AssetDatabase.CreateFolder(current, parts[index]), Is.Not.Empty);
                current = next;
            }
        }

        private static void AssertCollectionArtifactsPresent(ShapeSyncDatabase database, string outfitIdentity)
        {
            string databaseAssetPath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.Single(entry => entry.Identity == outfitIdentity);
            Assert.That(outfit.CollectionEntries, Is.Not.Empty);
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry entry in outfit.CollectionEntries)
            {
                Assert.That(entry.SourcePrefab, Is.Not.Null);
                Assert.That(entry.CollectionPrefab, Is.Not.Null);
                Assert.That(entry.SourcePrefab.transform.parent, Is.SameAs(database.transform.Find("Intermediate")));
                Assert.That(entry.CollectionPrefab.transform.parent, Is.SameAs(database.transform.Find("Intermediate")));
                foreach (GameObject prefab in new[] { entry.SourcePrefab, entry.CollectionPrefab })
                {
                    SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.That(renderers, Is.Not.Empty);
                    foreach (SkinnedMeshRenderer renderer in renderers)
                    {
                        Assert.That(renderer.sharedMesh, Is.Not.Null);
                        Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(databaseAssetPath));
                    }
                }
            }
        }

        private static void AssertCollectionArtifactsAbsent(ShapeSyncDatabase database, string outfitIdentity)
        {
            Transform intermediate = database.transform.Find("Intermediate");
            Assert.That(intermediate.Cast<Transform>().Where(child => child.name.StartsWith(outfitIdentity + "_", StringComparison.Ordinal)
                && child.name.Contains("_Collection")).ToArray(), Is.Empty);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>()
                .Where(mesh => mesh.name.StartsWith(outfitIdentity + "_", StringComparison.Ordinal) && mesh.name.Contains("_Collection")).ToArray(), Is.Empty);
        }

        private static void CreateDatabaseWithFbmAxis(string databasePath, params string[] fbmNames)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = CreateValidImportedFigure(intermediate, "Master", transaction);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Master", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts = fbmNames
                    .Select(name => new ShapeSyncDatabaseRegistry.FigureAxisDraft(name, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)).ToArray();
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] bindings = new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[fbmNames.Length];
                for (int index = 0; index < fbmNames.Length; index++)
                {
                    GameObject figure = CreateValidImportedFigure(intermediate, fbmNames[index], transaction);
                    bindings[index] = new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(fbmNames[index], figure) };
                }
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, bindings, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
        }

        private static GameObject PrepareSavedPbmFollow(string databasePath, string sourcePath)
        {
            GameObject source = PrepareClassifiedOutfitWithFbm(databasePath, sourcePath, "Tall");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                var draft = new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Pose", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) };
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, draft, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                GameObject basePbm = CreateValidImportedFigure(intermediate, "Master_Pose", transaction);
                GameObject tallPbm = CreateValidImportedFigure(intermediate, "Tall_Pose", transaction);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, admissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallPbm)
                    }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string axisDiagnostic), Is.True, axisDiagnostic);
            Assert.That(ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(databasePath, "Coat", new[]
            {
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", ShapeSyncDatabaseRegistry.BaseShapeKey, source),
                new ShapeSyncMeshOutfitPbmFollowAuthoring.Source("Pose", "Tall", source)
            }, out string saveDiagnostic), Is.True, saveDiagnostic);
            return source;
        }

        private static void ConfigurePbmFollowDifferenceFixture(ShapeSyncDatabase database)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.Single(entry => entry.Identity == "Coat");
            ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow = outfit.PbmFollows.Single(entry => entry.PbmAxisName == "Pose");
            SetMeshVertices(outfit.AxisFigures.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).OutfitPrefab,
                new[] { Vector3.zero, Vector3.right, Vector3.up });
            SetMeshVertices(outfit.AxisFigures.Single(entry => entry.ShapeKey == "Tall").OutfitPrefab,
                new[] { new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 0f) });
            SetMeshVertices(follow.Figures.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Figure,
                new[] { new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(2f, 1f, 0f) });
            SetMeshVertices(follow.Figures.Single(entry => entry.ShapeKey == "Tall").Figure,
                new[] { new Vector3(4f, 0f, 0f), new Vector3(5f, 0f, 0f), new Vector3(4f, 1f, 0f) });
            AssetDatabase.SaveAssets();
        }

        private static void SetMeshVertices(GameObject prefab, Vector3[] vertices)
        {
            SkinnedMeshRenderer renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sharedMesh, Is.Not.Null);
            Assert.That(renderer.sharedMesh.vertexCount, Is.EqualTo(vertices.Length));
            renderer.sharedMesh.vertices = vertices;
            EditorUtility.SetDirty(renderer.sharedMesh);
        }

        private static void AssertNoSavedPbmFollow(string databasePath, string oldPbmName, string message)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(database.Registry.Outfits.Single(entry => entry.Identity == "Coat").PbmFollows, Is.Empty, message);
            Assert.That(database.transform.Find("Intermediate/Coat_" + oldPbmName + "_Master"), Is.Null, message);
            Assert.That(database.transform.Find("Intermediate").Cast<Transform>()
                .Any(child => child.name.StartsWith("Coat_" + oldPbmName + "_", StringComparison.Ordinal) && child.name.EndsWith("_Source", StringComparison.Ordinal)), Is.False, message);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Mesh>().Any(mesh => mesh.name == "Coat_" + oldPbmName + "_Master_SkinnedMesh"), Is.False, message);
        }

        private static void CreateDatabaseWithFbmAxes(string databasePath, params string[] fbmNames)
        {
            CreateDatabaseWithFbmAxis(databasePath, fbmNames);
        }

        private static Mesh CreateDeltaMesh(Vector3[] vertices)
        {
            Mesh mesh = new Mesh { name = "DeltaMesh" };
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
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

        private static void CreatePersistentMultiMaterialSkinnedSource(string path)
        {
            GameObject root = new GameObject("MixedSource");
            GameObject bone = new GameObject("Bone"); bone.transform.SetParent(root.transform, false);
            GameObject meshObject = new GameObject("Coat"); meshObject.transform.SetParent(root.transform, false);
            SkinnedMeshRenderer renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = "MixedMesh", subMeshCount = 2 };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.right * 2f, Vector3.right * 3f, Vector3.right * 2f + Vector3.up };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            // Keep the top-level Extra Bone parent in the renderer bone table as an
            // unweighted slot so topology normalization can reconstruct the parent path.
            mesh.bindposes = new[]
            {
                bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix,
                root.transform.worldToLocalMatrix * root.transform.localToWorldMatrix
            };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 6).ToArray();
            renderer.sharedMesh = mesh; renderer.rootBone = bone.transform; renderer.bones = new[] { bone.transform, root.transform };
            Material keep = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Keep" };
            Material discard = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Discard" };
            Texture2D sharedTexture = new Texture2D(1, 1) { name = "SharedTexture" };
            sharedTexture.SetPixel(0, 0, Color.white);
            sharedTexture.Apply();
            keep.SetTexture("_BaseMap", sharedTexture);
            discard.SetTexture("_BaseMap", sharedTexture);
            renderer.sharedMaterials = new[] { keep, discard };
            AssetDatabase.CreateAsset(mesh, Root + "/MixedMesh.asset");
            AssetDatabase.CreateAsset(sharedTexture, Root + "/SharedTexture.asset");
            AssetDatabase.CreateAsset(keep, Root + "/Keep.mat");
            AssetDatabase.CreateAsset(discard, Root + "/Discard.mat");
            Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
            Object.DestroyImmediate(root);
        }
    }
}
#endif
