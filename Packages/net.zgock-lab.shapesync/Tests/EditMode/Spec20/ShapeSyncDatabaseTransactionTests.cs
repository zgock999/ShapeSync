// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncDatabaseTransactionTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20DatabaseTransactionRoot;
        private string snapshotDirectory;
        private Func<string> originalSnapshotDirectoryProvider;

        [SetUp]
        public void SetUp()
        {
            snapshotDirectory = Path.Combine(Path.GetTempPath(), "ShapeSyncDatabaseTransactionTests", Guid.NewGuid().ToString("N"));
            originalSnapshotDirectoryProvider = ShapeSyncDatabaseTransaction.SnapshotDirectoryProvider;
            ShapeSyncDatabaseTransaction.SnapshotDirectoryProvider = () => snapshotDirectory;
            if (!AssetDatabase.IsValidFolder(Root))
                ShapeSyncTestAssetPaths.EnsureConsumerTempRoot();
                AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_1_ShapeSyncDatabaseTransactionTests");
        }

        [TearDown]
        public void TearDown()
        {
            ShapeSyncDatabaseTransaction.SnapshotDirectoryProvider = originalSnapshotDirectoryProvider;
            AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(snapshotDirectory)) Directory.Delete(snapshotDirectory, true);
        }

        private string[] SnapshotFiles()
            => Directory.Exists(snapshotDirectory)
                ? Directory.GetFiles(snapshotDirectory, "*.snapshot", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

        [Test]
        public void TryEditStructure_PreservesPrefabSubAssetsAndAddsIntermediateChild()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            AddSubAssets(assetPath);

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, intermediate) =>
            {
                Assert.That(loadedDatabase.transform, Is.SameAs(intermediate.parent));
                GameObject humanoid = new GameObject("Humanoid");
                humanoid.transform.SetParent(intermediate, false);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/Humanoid"), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().Select(asset => asset.name), Does.Contain("DatabaseMesh"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Texture2D>().Select(asset => asset.name), Does.Contain("DatabaseTexture"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().Select(asset => asset.name), Does.Contain("DatabaseMaterial"));
        }

        [Test]
        public void TryEditStructure_RejectsNullCallbackWithoutChangingDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, null, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("callback"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName).childCount, Is.Zero);
        }

        [Test]
        public void TryEditStructure_UnloadsFailedCallbackChangesWithoutPersistingThem()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) =>
            {
                new GameObject("MustNotPersist").transform.SetParent(intermediate, false);
                throw new InvalidOperationException("Injected callback failure");
            }, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("Injected callback failure"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustNotPersist"), Is.Null);
        }

        [Test]
        public void TryEditStructure_RejectsCallbackThatBreaksRootContainerContract()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) =>
            {
                UnityEngine.Object.DestroyImmediate(intermediate.gameObject);
            }, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("root container contract"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
        }

        [Test]
        public void TryEditStructure_ClosesContentsWhenCallbackDestroysDatabaseRoot()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);

            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, _) =>
            {
                UnityEngine.Object.DestroyImmediate(loadedDatabase.gameObject);
            }, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("root container contract"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
        }

        [Test]
        public void TryEditStructure_RollsBackWhenUnloadPrefabContentsFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Action<GameObject> originalUnload = ShapeSyncDatabaseTransaction.UnloadPrefabContents;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = contents =>
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                    throw new IOException("Injected unload failure");
                };
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) => new GameObject("MustRollbackAfterUnloadFailure").transform.SetParent(intermediate, false), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = originalUnload;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("cleanup failed"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustRollbackAfterUnloadFailure"), Is.Null);
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_ReportsFallbackCloseFalseWhenUnloadFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Action<GameObject> originalUnload = ShapeSyncDatabaseTransaction.UnloadPrefabContents;
            Func<Scene, bool> originalClose = ShapeSyncDatabaseTransaction.ClosePreviewScene;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = contents =>
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                    throw new IOException("Injected unload failure");
                };
                ShapeSyncDatabaseTransaction.ClosePreviewScene = _ => false;
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) => new GameObject("MustRollbackAfterCompoundCleanupFailure").transform.SetParent(intermediate, false), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = originalUnload;
                ShapeSyncDatabaseTransaction.ClosePreviewScene = originalClose;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("cleanup failed; preview scene close also failed"));
            Assert.That(diagnostic, Does.Contain("could not be closed"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustRollbackAfterCompoundCleanupFailure"), Is.Null);
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_ReportsClosePreviewSceneFailureAfterRootDestruction()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Func<Scene, bool> originalClose = ShapeSyncDatabaseTransaction.ClosePreviewScene;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.ClosePreviewScene = scene =>
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                    throw new IOException("Injected preview close failure");
                };
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, _) => UnityEngine.Object.DestroyImmediate(loadedDatabase.gameObject), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.ClosePreviewScene = originalClose;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("Injected preview close failure"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_ReportsClosePreviewSceneFalseAfterRootDestruction()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Func<Scene, bool> originalClose = ShapeSyncDatabaseTransaction.ClosePreviewScene;
            Scene capturedScene = default;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.ClosePreviewScene = scene =>
                {
                    capturedScene = scene;
                    return false;
                };
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, _) => UnityEngine.Object.DestroyImmediate(loadedDatabase.gameObject), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.ClosePreviewScene = originalClose;
                if (capturedScene.IsValid()) EditorSceneManager.ClosePreviewScene(capturedScene);
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("could not be closed"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_RollsBackPersistedPrefabChangesAndCleansSnapshot()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            AddSubAssets(assetPath, out Mesh mesh, out Texture2D texture, out Material material);
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (contents, path) =>
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    return false;
                };
                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, intermediate) =>
                {
                    new GameObject("MustRollback").transform.SetParent(intermediate, false);
                    loadedDatabase.gameObject.name = "MutatedDatabase";
                    loadedDatabase.transform.localPosition = new Vector3(1f, 2f, 3f);
                    mesh.name = "MutatedMesh";
                    texture.name = "MutatedTexture";
                    material.name = "MutatedMaterial";
                    EditorUtility.SetDirty(mesh);
                    EditorUtility.SetDirty(texture);
                    EditorUtility.SetDirty(material);
                }, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("could not be saved"));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave;
            }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustRollback"), Is.Null);
            Assert.That(reloaded.gameObject.name, Is.EqualTo("ShapeSyncDatabase"));
            Assert.That(reloaded.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().Select(asset => asset.name), Does.Contain("DatabaseMesh"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Texture2D>().Select(asset => asset.name), Does.Contain("DatabaseTexture"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().Select(asset => asset.name), Does.Contain("DatabaseMaterial"));
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_ReportsSnapshotCreationFailureWithoutChangingDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Action<string, string> originalCopy = ShapeSyncDatabaseTransaction.CopyAssetFile;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.CopyAssetFile = (_, _) => throw new IOException("Injected snapshot creation failure");
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) => new GameObject("MustNotPersist").transform.SetParent(intermediate, false), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.CopyAssetFile = originalCopy;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("snapshot could not be created"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustNotPersist"), Is.Null);
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void DirectEdit_PersistsRegistryChangeWithoutSnapshot()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            int originalPcmSlots = database.Registry.PcmSlots;
            Action<string> originalReimport = ShapeSyncDatabaseDirectEdit.ReimportAsset;
            try
            {
                ShapeSyncDatabaseDirectEdit.ReimportAsset = _ => { };
                Assert.That(ShapeSyncDatabaseDirectEdit.TryEdit(database, "Set PCM Slots",
                    (ShapeSyncDatabaseRegistry registry, out string diagnostic) => registry.TrySetPcmSlots(3, out diagnostic), out string editDiagnostic), Is.True, editDiagnostic);
            }
            finally
            {
                ShapeSyncDatabaseDirectEdit.ReimportAsset = originalReimport;
            }

            Assert.That(SnapshotFiles(), Is.Empty);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.Registry.PcmSlots, Is.EqualTo(3));
        }

        [Test]
        public void DirectEdit_SaveFailureRestoresRegistryAndKeepsDraftUnaccepted()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            int originalPcmSlots = database.Registry.PcmSlots;
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseDirectEdit.SavePrefabAsset;
            Action<string> originalReimport = ShapeSyncDatabaseDirectEdit.ReimportAsset;
            bool saveCalled = false;
            try
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = (_, _) => { saveCalled = true; return false; };
                ShapeSyncDatabaseDirectEdit.ReimportAsset = _ => { };
                Assert.That(ShapeSyncDatabaseDirectEdit.TryEdit(database, "Set PCM Slots",
                    (ShapeSyncDatabaseRegistry registry, out string diagnostic) => registry.TrySetPcmSlots(5, out diagnostic), out string editDiagnostic), Is.False);
                Assert.That(editDiagnostic, Does.Contain("could not be saved"));
            }
            finally
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = originalSave;
                ShapeSyncDatabaseDirectEdit.ReimportAsset = originalReimport;
            }

            Assert.That(saveCalled, Is.True);
            Assert.That(database.Registry.PcmSlots, Is.EqualTo(originalPcmSlots), "in-memory registry must restore the preimage");
            Assert.That(SnapshotFiles(), Is.Empty);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.Registry.PcmSlots, Is.EqualTo(originalPcmSlots), "persisted registry must remain unchanged after SavePrefabAsset failure");
        }

        [Test]
        public void DirectEdit_TextureRenameSaveFailureRestoresShapeReference()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(assetPath, (contents, _, transaction) =>
            {
                Texture2D texture = new Texture2D(1, 1) { name = "SharedTexture" };
                transaction.AddSubAsset(texture);
                Assert.That(contents.Registry.TryRegisterTextureResource("Shared", texture, out string resourceDiagnostic), Is.True, resourceDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
                Assert.That(contents.Registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string partDiagnostic), Is.True, partDiagnostic);
                Assert.That(contents.Registry.TrySetShapePartTexture("hair", 0, "Shared", true, Color.white, out string textureDiagnostic), Is.True, textureDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseDirectEdit.SavePrefabAsset;
            Action<string> originalReimport = ShapeSyncDatabaseDirectEdit.ReimportAsset;
            try
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = (_, _) => false;
                ShapeSyncDatabaseDirectEdit.ReimportAsset = _ => { };
                Assert.That(ShapeSyncDatabaseDirectEdit.TryEdit(opened, "Rename Texture Resource",
                    (ShapeSyncDatabaseRegistry registry, out string diagnostic) => registry.TryRenameTextureResource("Shared", "Renamed", out diagnostic), out string renameDiagnostic), Is.False);
                Assert.That(renameDiagnostic, Does.Contain("could not be saved"));
                Assert.That(opened.Registry.TextureResources.Single().LogicalName, Is.EqualTo("Shared"));
                Assert.That(opened.Registry.Shapes.Single().Parts.Single().TextureResourceName, Is.EqualTo("Shared"));
            }
            finally
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = originalSave;
                ShapeSyncDatabaseDirectEdit.ReimportAsset = originalReimport;
            }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string reloadDiagnostic), Is.True, reloadDiagnostic);
            Assert.That(reloaded.Registry.TextureResources.Single().LogicalName, Is.EqualTo("Shared"));
            Assert.That(reloaded.Registry.Shapes.Single().Parts.Single().TextureResourceName, Is.EqualTo("Shared"));
        }

        [Test]
        public void DirectEdit_AdmissionRejectDoesNotMutateOrSave()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            int originalPcmSlots = database.Registry.PcmSlots;
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseDirectEdit.SavePrefabAsset;
            bool saveCalled = false;
            try
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = (_, _) => { saveCalled = true; return true; };
                Assert.That(ShapeSyncDatabaseDirectEdit.TryEdit(database, "Rejected Edit",
                    (ShapeSyncDatabaseRegistry registry, out string diagnostic) =>
                    {
                        diagnostic = "Admission rejected.";
                        return false;
                    }, out string editDiagnostic), Is.False);
                Assert.That(editDiagnostic, Does.Contain("Admission rejected."));
            }
            finally
            {
                ShapeSyncDatabaseDirectEdit.SavePrefabAsset = originalSave;
            }

            Assert.That(saveCalled, Is.False);
            Assert.That(database.Registry.PcmSlots, Is.EqualTo(originalPcmSlots), "admission reject must not mutate the in-memory registry");
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_RollsBackPersistedChangesWhenSaveAssetsFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            AddSubAssets(assetPath, out Mesh mesh, out Texture2D texture, out Material material);
            Action originalSaveAssets = ShapeSyncDatabaseTransaction.SaveAssets;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.SaveAssets = () => throw new IOException("Injected final save failure");
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (loadedDatabase, intermediate) =>
                {
                    new GameObject("MustRollbackAfterSaveAssets").transform.SetParent(intermediate, false);
                    loadedDatabase.gameObject.name = "MutatedDatabase";
                    loadedDatabase.transform.localPosition = new Vector3(4f, 5f, 6f);
                    mesh.name = "MutatedMesh";
                    texture.name = "MutatedTexture";
                    material.name = "MutatedMaterial";
                    EditorUtility.SetDirty(mesh);
                    EditorUtility.SetDirty(texture);
                    EditorUtility.SetDirty(material);
                }, out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.SaveAssets = originalSaveAssets;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("Injected final save failure"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustRollbackAfterSaveAssets"), Is.Null);
            Assert.That(reloaded.gameObject.name, Is.EqualTo("ShapeSyncDatabase"));
            Assert.That(reloaded.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().Select(asset => asset.name), Does.Contain("DatabaseMesh"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Texture2D>().Select(asset => asset.name), Does.Contain("DatabaseTexture"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().Select(asset => asset.name), Does.Contain("DatabaseMaterial"));
            Assert.That(SnapshotFiles(), Is.Empty);
        }

        [Test]
        public void TryEditStructure_ReportsRollbackFailureAndRetainsSnapshot()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Func<GameObject, string, bool> originalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            Action<string, string> originalRestore = ShapeSyncDatabaseTransaction.RestoreAssetFile;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (contents, path) =>
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out _);
                    return false;
                };
                ShapeSyncDatabaseTransaction.RestoreAssetFile = (_, _) => throw new IOException("Injected rollback failure");
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) => new GameObject("PersistedUntilRecovery").transform.SetParent(intermediate, false), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSave;
                ShapeSyncDatabaseTransaction.RestoreAssetFile = originalRestore;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("could not be saved"));
            Assert.That(diagnostic, Does.Contain("rollback failed"));
            Assert.That(SnapshotFiles(), Has.Length.EqualTo(1));
        }

        [Test]
        public void TryEditStructure_RollsBackAndRetainsSnapshotWhenCleanupFails()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            Action<string> originalDelete = ShapeSyncDatabaseTransaction.DeleteSnapshotFile;
            bool result = true;
            string diagnostic = null;
            try
            {
                ShapeSyncDatabaseTransaction.DeleteSnapshotFile = _ => throw new IOException("Injected cleanup failure");
                Assert.DoesNotThrow(() => result = ShapeSyncDatabaseTransaction.TryEditStructure(assetPath, (_, intermediate) => new GameObject("MustRollback").transform.SetParent(intermediate, false), out diagnostic));
            }
            finally
            {
                ShapeSyncDatabaseTransaction.DeleteSnapshotFile = originalDelete;
            }

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Does.Contain("snapshot cleanup failed"));
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out ShapeSyncDatabase reloaded, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reloaded.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/MustRollback"), Is.Null);
            Assert.That(SnapshotFiles(), Has.Length.EqualTo(1));
        }

        private static void AddSubAssets(string assetPath)
        {
            AddSubAssets(assetPath, out _, out _, out _);
        }

        private static void AddSubAssets(string assetPath, out Mesh mesh, out Texture2D texture, out Material material)
        {
            mesh = new Mesh { name = "DatabaseMesh" };
            texture = new Texture2D(1, 1) { name = "DatabaseTexture" };
            material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "DatabaseMaterial" };
            AssetDatabase.AddObjectToAsset(mesh, assetPath);
            AssetDatabase.AddObjectToAsset(texture, assetPath);
            AssetDatabase.AddObjectToAsset(material, assetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
