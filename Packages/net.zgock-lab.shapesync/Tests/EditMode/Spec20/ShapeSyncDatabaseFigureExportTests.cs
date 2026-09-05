// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncDatabaseFigureExportTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20FigureExportRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_7_ShapeSyncDatabaseFigureExportTests"); }
            ShapeSyncDatabaseFigureExport.SavePrefabAsset = (contents, path) => PrefabUtility.SaveAsPrefabAsset(contents, path);
            ShapeSyncDatabaseFigureExport.SaveAssets = AssetDatabase.SaveAssets;
        }

        [TearDown]
        public void TearDown()
        {
            ShapeSyncDatabaseFigureExport.SavePrefabAsset = (contents, path) => PrefabUtility.SaveAsPrefabAsset(contents, path);
            ShapeSyncDatabaseFigureExport.SaveAssets = AssetDatabase.SaveAssets;
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void TryExport_ExportsRegisteredBaseWithoutChangingDatabase()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            string exportPath = Root + "/ExportedBase.prefab";

            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, exportPath, out GameObject exported, out string diagnostic), Is.True, diagnostic);
            Assert.That(exported, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(exportPath)));
            Assert.That(exported, Is.Not.SameAs(baseFigure));
            Assert.That(exported.name, Is.EqualTo("ExportedBase"));
            Assert.That(exported.GetComponent<ShapeSyncFigureImportRecord>(), Is.Not.Null);
            Assert.That(exported.transform.Find("CopyMarker"), Is.Not.Null);
            Assert.That(exported.transform.Find("CopyMarker").localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            Assert.That(database.transform.Find("Intermediate/Base").gameObject, Is.SameAs(baseFigure));
        }

        [Test]
        public void TryExport_ExportsRegisteredFbmAndRejectsPbmLikeFigure()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: true, out string databasePath, out _, out GameObject fbmFigure);
            string exportPath = Root + "/ExportedFbm.prefab";
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);

            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, fbmFigure, exportPath, out GameObject exported, out string exportDiagnostic), Is.True, exportDiagnostic);
            Assert.That(exported.name, Is.EqualTo("ExportedFbm"));
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));

            GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(database));
            try
            {
                ShapeSyncDatabase stagedDatabase = contents.GetComponent<ShapeSyncDatabase>();
                Transform intermediate = contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                GameObject basePbm = new GameObject("Base_Smile"); basePbm.transform.SetParent(intermediate, false);
                GameObject fbmPbm = new GameObject("Tall_Smile"); fbmPbm.transform.SetParent(intermediate, false);
                Mesh basePbmMesh = AddImportedFigurePayload(basePbm, "BaseSmileMesh");
                Mesh fbmPbmMesh = AddImportedFigurePayload(fbmPbm, "TallSmileMesh");
                Assert.That(stagedDatabase.Registry.TryAdmitFigureAxes(stagedDatabase, new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] bindings =
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", fbmPbm)
                    }
                };
                Assert.That(stagedDatabase.Registry.TryCommitFigureAxes(stagedDatabase, admissions, bindings, out string commitDiagnostic), Is.True, commitDiagnostic);
                EditorUtility.SetDirty(stagedDatabase.Registry);
                string stagedDatabasePath = AssetDatabase.GetAssetPath(database);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, stagedDatabasePath), Is.Not.Null);
                AssetDatabase.AddObjectToAsset(basePbmMesh, stagedDatabasePath);
                AssetDatabase.AddObjectToAsset(fbmPbmMesh, stagedDatabasePath);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, stagedDatabasePath), Is.Not.Null);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            GameObject persistedPbm = reopened.transform.Find("Intermediate/Base_Smile").gameObject;
            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(reopened, persistedPbm, Root + "/Unsupported.prefab", out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("Base Figure or an FBM Figure"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(Root + "/Unsupported.prefab"), Is.Null);
        }

        [Test]
        public void TryExport_RejectsExternalAndOccupiedDestinationWithoutCreatingAssets()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out _, out GameObject baseFigure, out _);
            GameObject external = new GameObject("External");
            string occupiedPath = Root + "/Occupied.prefab";
            try
            {
                GameObject occupied = new GameObject("Occupied");
                try { Assert.That(PrefabUtility.SaveAsPrefabAsset(occupied, occupiedPath), Is.Not.Null); }
                finally { Object.DestroyImmediate(occupied); }
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, external, Root + "/External.prefab", out _, out string externalDiagnostic), Is.False);
                Assert.That(externalDiagnostic, Does.Contain("Intermediate Figure"));
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, occupiedPath, out _, out string occupiedDiagnostic), Is.False);
                Assert.That(occupiedDiagnostic, Does.Contain("cannot overwrite"));
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, "Outside/Invalid.prefab", out _, out string outsideDiagnostic), Is.False);
                Assert.That(outsideDiagnostic, Does.Contain("destination below"));
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, Root + "/Missing/Invalid.prefab", out _, out string folderDiagnostic), Is.False);
                Assert.That(folderDiagnostic, Does.Contain("destination below"));
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, Root + "/Invalid.asset", out _, out string extensionDiagnostic), Is.False);
                Assert.That(extensionDiagnostic, Does.Contain("destination below"));
            }
            finally { Object.DestroyImmediate(external); }
        }

        [Test]
        public void TryExport_RollsBackDestinationWhenSaveThrows()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            string exportPath = Root + "/Rollback.prefab";
            ShapeSyncDatabaseFigureExport.SavePrefabAsset = (_, path) =>
            {
                GameObject partial = new GameObject("Partial");
                PrefabUtility.SaveAsPrefabAsset(partial, path);
                Object.DestroyImmediate(partial);
                throw new InvalidOperationException("Injected export save failure");
            };

            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, exportPath, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("Injected export save failure"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(exportPath), Is.Null);
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
        }

        [Test]
        public void TryExport_RollsBackDestinationWhenSaveReturnsNullOrSaveAssetsThrows()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            string nullPath = Root + "/NullSave.prefab";
            ShapeSyncDatabaseFigureExport.SavePrefabAsset = (contents, path) =>
            {
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return null;
            };
            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, nullPath, out _, out string nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic, Does.Contain("could not save"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(nullPath), Is.Null);

            string saveAssetsPath = Root + "/SaveAssetsFailure.prefab";
            ShapeSyncDatabaseFigureExport.SavePrefabAsset = (contents, path) => PrefabUtility.SaveAsPrefabAsset(contents, path);
            ShapeSyncDatabaseFigureExport.SaveAssets = () => throw new InvalidOperationException("Injected SaveAssets failure");
            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, saveAssetsPath, out _, out string saveAssetsDiagnostic), Is.False);
            Assert.That(saveAssetsDiagnostic, Does.Contain("Injected SaveAssets failure"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(saveAssetsPath), Is.Null);
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
        }

        [Test]
        public void TryExport_UsesTemporaryContentsWithoutRebindingCallerRegistry()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: true, out _, out GameObject baseFigure, out GameObject fbmFigure);
            FieldInfo rawFigure = typeof(ShapeSyncDatabaseRegistry.AxisFigureEntry).GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo baseRawFigure = typeof(ShapeSyncDatabaseRegistry.BaseFigureEntry).GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic);
            ShapeSyncDatabaseRegistry.AxisFigureEntry fbmBinding = database.Registry.FigureAxes.Single(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Figures.Single();
            ShapeSyncDatabaseRegistry.BaseFigureEntry baseBinding = database.Registry.BaseFigures.Single();
            baseRawFigure.SetValue(baseBinding, null);
            rawFigure.SetValue(fbmBinding, null);

            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, baseFigure, Root + "/StaleBase.prefab", out _, out string baseDiagnostic), Is.True, baseDiagnostic);
            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(database, fbmFigure, Root + "/StaleFbm.prefab", out _, out string fbmDiagnostic), Is.True, fbmDiagnostic);
            Assert.That(baseRawFigure.GetValue(baseBinding), Is.Null);
            Assert.That(rawFigure.GetValue(fbmBinding), Is.Null);
        }

        [Test]
        public void TryExport_RejectsNestedExternalPrefabInstance()
        {
            const string externalPath = Root + "/ExternalFigure.prefab";
            GameObject externalSource = new GameObject("ExternalFigure");
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(externalSource, externalPath), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(externalSource); }

            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(created);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                ShapeSyncDatabase stagedDatabase = contents.GetComponent<ShapeSyncDatabase>();
                GameObject nested = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(externalPath));
                nested.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                Assert.That(stagedDatabase.Registry.TryRegisterBaseFigure(stagedDatabase, "ExternalFigure", nested, out string registerDiagnostic), Is.True, registerDiagnostic);
                EditorUtility.SetDirty(stagedDatabase.Registry);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            ShapeSyncDatabase persisted = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(databasePath);
            GameObject nestedFigure = persisted.transform.Find("Intermediate/ExternalFigure").gameObject;
            Assert.That(ShapeSyncDatabaseFigureExport.TryExport(persisted, nestedFigure, Root + "/MustReject.prefab", out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("nested external Prefab instance"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(Root + "/MustReject.prefab"), Is.Null);
        }

        [Test]
        public void FigureDetailExport_IsEnabledOnlyWhenDatabaseFigureIsResolved()
        {
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            try
            {
                Assert.That(window.IsFigureExportEnabledForTest, Is.False);
                ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out _, out GameObject baseFigure, out _);
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetFigureInputsForTest(baseFigure.name, baseFigure);
                Assert.That(window.IsFigureExportEnabledForTest, Is.True);
                window.SetFigureInputsForTest("UnknownFigure", null);
                Assert.That(window.IsFigureExportEnabledForTest, Is.False);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void FigureDetailExport_DelegatesRegisteredBaseAndSelectsExportWithoutChangingDatabaseOrDraft()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject selectedBefore = new GameObject("SelectedBefore");
            GameObject exported = new GameObject("ExportedFromWindow");
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Action refreshBefore = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            Object activeBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                window.SetFigureInputsForTest(baseFigure.name, baseFigure);
                bool dirtyBefore = window.IsFigureDetailDirtyForTest;
                string panelTitle = null;
                string defaultName = null;
                string extension = null;
                string panelMessage = null;
                string folder = null;
                ShapeSyncDatabase actualDatabase = null;
                GameObject actualFigure = null;
                string actualDestination = null;
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (title, name, ext, message, directory) =>
                {
                    panelTitle = title; defaultName = name; extension = ext; panelMessage = message; folder = directory;
                    return Root + "/WindowExport.prefab";
                };
                var operations = new List<string>();
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () => operations.Add("Refresh");
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    operations.Add("Export");
                    actualDatabase = candidateDatabase; actualFigure = candidateFigure; actualDestination = destinationPath;
                    exportedPrefab = exported; diagnostic = null; return true;
                };
                Selection.activeObject = selectedBefore;

                Assert.That(window.TryExportDatabaseFigureWithDialog(out string exportDiagnostic), Is.True, exportDiagnostic);
                Assert.That(panelTitle, Is.EqualTo("Export Figure Prefab"));
                Assert.That(defaultName, Is.EqualTo(baseFigure.name));
                Assert.That(extension, Is.EqualTo("prefab"));
                Assert.That(panelMessage, Does.Contain("folder and name"));
                Assert.That(folder, Is.Not.Empty);
                Assert.That(actualDatabase, Is.SameAs(database));
                Assert.That(actualFigure, Is.SameAs(baseFigure));
                Assert.That(actualDestination, Is.EqualTo(Root + "/WindowExport.prefab"));
                Assert.That(operations, Is.EqualTo(new[] { "Refresh", "Export" }));
                Assert.That(Selection.activeObject, Is.SameAs(exported));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.DatabaseFigurePrefab, Is.SameAs(baseFigure));
                Assert.That(window.FigureName, Is.EqualTo(baseFigure.name));
                Assert.That(window.FigurePrefab, Is.SameAs(baseFigure));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.EqualTo(dirtyBefore));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = refreshBefore;
                Selection.activeObject = activeBefore;
                Object.DestroyImmediate(selectedBefore);
                Object.DestroyImmediate(exported);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FigureDetailExport_CancelDoesNotInvokeServiceOrChangeDraftDirtyDatabaseOrSelection()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject draftPrefab = new GameObject("DraftPrefab");
            GameObject selectedBefore = new GameObject("SelectedBefore");
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Action refreshBefore = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            Object activeBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                window.SetFigureInputsForTest(baseFigure.name, baseFigure);
                window.SetFigureInputsForTest(baseFigure.name, draftPrefab);
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => string.Empty;
                int refreshCalls = 0;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () => refreshCalls++;
                int exportCalls = 0;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    exportCalls++; exportedPrefab = null; diagnostic = "must not run"; return false;
                };
                Selection.activeObject = selectedBefore;

                Assert.That(window.TryExportDatabaseFigureWithDialog(out string exportDiagnostic), Is.False);
                Assert.That(exportDiagnostic, Is.Null);
                Assert.That(exportCalls, Is.Zero);
                Assert.That(refreshCalls, Is.Zero);
                Assert.That(Selection.activeObject, Is.SameAs(selectedBefore));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.DatabaseFigurePrefab, Is.SameAs(baseFigure));
                Assert.That(window.FigureName, Is.EqualTo(baseFigure.name));
                Assert.That(window.FigurePrefab, Is.SameAs(draftPrefab));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(window.Diagnostic, Is.Null);
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = refreshBefore;
                Selection.activeObject = activeBefore;
                Object.DestroyImmediate(draftPrefab);
                Object.DestroyImmediate(selectedBefore);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FigureDetailExport_FailurePreservesDraftDirtyDatabaseAndSelection()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject draftPrefab = new GameObject("DraftPrefab");
            GameObject selectedBefore = new GameObject("SelectedBefore");
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Object activeBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                window.SetFigureInputsForTest(baseFigure.name, baseFigure);
                window.SetFigureInputsForTest(baseFigure.name, draftPrefab);
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => Root + "/Failure.prefab";
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    exportedPrefab = null; diagnostic = "Injected Figure export failure"; return false;
                };
                Selection.activeObject = selectedBefore;

                Assert.That(window.TryExportDatabaseFigureWithDialog(out string exportDiagnostic), Is.False);
                Assert.That(exportDiagnostic, Is.EqualTo("Injected Figure export failure"));
                Assert.That(window.Diagnostic, Is.EqualTo("Injected Figure export failure"));
                Assert.That(Selection.activeObject, Is.SameAs(selectedBefore));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.DatabaseFigurePrefab, Is.SameAs(baseFigure));
                Assert.That(window.FigureName, Is.EqualTo(baseFigure.name));
                Assert.That(window.FigurePrefab, Is.SameAs(draftPrefab));
                Assert.That(window.IsFigureDetailDirtyForTest, Is.True);
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                Selection.activeObject = activeBefore;
                Object.DestroyImmediate(draftPrefab);
                Object.DestroyImmediate(selectedBefore);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FbmDetailExport_DelegatesTargetFbmAndSelectsOutputWithoutChangingDatabase()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: true, out string databasePath, out _, out GameObject fbmFigure);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject selectedBefore = new GameObject("SelectedBefore");
            GameObject exported = new GameObject("ExportedFbmFromWindow");
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Object activeBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.CanExportDatabaseFigureForTest(null), Is.False);
                Assert.That(window.CanExportDatabaseFigureForTest(fbmFigure), Is.True);
                string title = null;
                string defaultName = null;
                ShapeSyncDatabase actualDatabase = null;
                GameObject actualFigure = null;
                string actualDestination = null;
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (panelTitle, name, _, _, _) =>
                {
                    title = panelTitle; defaultName = name; return Root + "/WindowFbmExport.prefab";
                };
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    actualDatabase = candidateDatabase; actualFigure = candidateFigure; actualDestination = destinationPath;
                    exportedPrefab = exported; diagnostic = null; return true;
                };
                Selection.activeObject = selectedBefore;

                Assert.That(window.TryExportFbmFigureWithDialog(fbmFigure, out string exportDiagnostic), Is.True, exportDiagnostic);
                Assert.That(title, Is.EqualTo("Export FBM Prefab"));
                Assert.That(defaultName, Is.EqualTo(fbmFigure.name));
                Assert.That(actualDatabase, Is.SameAs(database));
                Assert.That(actualFigure, Is.SameAs(fbmFigure));
                Assert.That(actualDestination, Is.EqualTo(Root + "/WindowFbmExport.prefab"));
                Assert.That(Selection.activeObject, Is.SameAs(exported));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                Selection.activeObject = activeBefore;
                Object.DestroyImmediate(selectedBefore);
                Object.DestroyImmediate(exported);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FbmDetailExport_CancelAndFailurePreserveFbmDraftDatabaseAndSelection()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(includeFbm: true, out string databasePath, out _, out GameObject fbmFigure);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            GameObject selectedBefore = new GameObject("SelectedBefore");
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Object activeBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                Assert.That(window.SetFbmAxisRedefinitionDraftForTest("Tall", "TallRenamed", null, false), Is.True);
                Assert.That(window.IsFbmSaveEnabledForTest, Is.True);
                int exportCalls = 0;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    exportCalls++; exportedPrefab = null; diagnostic = "Injected FBM export failure"; return false;
                };
                Selection.activeObject = selectedBefore;
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => string.Empty;

                Assert.That(window.TryExportFbmFigureWithDialog(fbmFigure, out string cancelDiagnostic), Is.False);
                Assert.That(cancelDiagnostic, Is.Null);
                Assert.That(exportCalls, Is.Zero);
                Assert.That(window.IsFbmSaveEnabledForTest, Is.True);
                Assert.That(window.Diagnostic, Is.Null);
                Assert.That(Selection.activeObject, Is.SameAs(selectedBefore));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));

                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => Root + "/FbmFailure.prefab";
                Assert.That(window.TryExportFbmFigureWithDialog(fbmFigure, out string failureDiagnostic), Is.False);
                Assert.That(failureDiagnostic, Is.EqualTo("Injected FBM export failure"));
                Assert.That(exportCalls, Is.EqualTo(1));
                Assert.That(window.Diagnostic, Is.EqualTo("Injected FBM export failure"));
                Assert.That(window.IsFbmSaveEnabledForTest, Is.True);
                Assert.That(Selection.activeObject, Is.SameAs(selectedBefore));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                Selection.activeObject = activeBefore;
                Object.DestroyImmediate(selectedBefore);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FbmDetailExport_DelegatesEachFigureFromMultipleFbmRows()
        {
            ShapeSyncDatabase database = CreateDatabaseWithTwoFbms(out string databasePath, out GameObject tallFigure, out GameObject shortFigure);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, string> savePanelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Object activeBefore = Selection.activeObject;
            var outputs = new List<GameObject>();
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Fbms);
                string[] destinations = { Root + "/TallRow.prefab", Root + "/ShortRow.prefab" };
                int dialogIndex = 0;
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => destinations[dialogIndex++];
                var delegated = new List<GameObject>();
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject exportedPrefab, out string diagnostic) =>
                {
                    Assert.That(candidateDatabase, Is.SameAs(database));
                    delegated.Add(candidateFigure);
                    exportedPrefab = new GameObject("Exported_" + candidateFigure.name);
                    outputs.Add(exportedPrefab);
                    diagnostic = null;
                    return true;
                };

                Assert.That(window.TryExportFbmFigureWithDialog(tallFigure, out string tallDiagnostic), Is.True, tallDiagnostic);
                Assert.That(window.TryExportFbmFigureWithDialog(shortFigure, out string shortDiagnostic), Is.True, shortDiagnostic);
                Assert.That(delegated, Is.EqualTo(new[] { tallFigure, shortFigure }));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally
            {
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = savePanelBefore;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore;
                Selection.activeObject = activeBefore;
                foreach (GameObject output in outputs) Object.DestroyImmediate(output);
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FigureDetailExport_RefreshFailureDoesNotInvokeServiceOrChangeState()
        {
            ShapeSyncDatabase database = CreateDatabaseWithFigures(false, out string databasePath, out GameObject baseFigure, out _);
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);
            ShapeSyncDatabaseWindow window = ScriptableObject.CreateInstance<ShapeSyncDatabaseWindow>();
            Func<string, string, string, string, string, string> panelBefore = ShapeSyncDatabaseWindow.SaveFigureExportPanel;
            ShapeSyncDatabaseWindow.DatabaseFigureExporter exporterBefore = ShapeSyncDatabaseWindow.ExportDatabaseFigure;
            Action refreshBefore = ShapeSyncDatabaseWindow.RefreshAssetDatabase;
            Object selectedBefore = Selection.activeObject;
            try
            {
                Assert.That(window.TrySetDatabase(database, out string openDiagnostic), Is.True, openDiagnostic);
                window.SetSelectedSectionForTest(ShapeSyncDatabaseWindow.Section.Figure);
                window.SetFigureInputsForTest(baseFigure.name, baseFigure);
                ShapeSyncDatabaseWindow.SaveFigureExportPanel = (_, _, _, _, _) => Root + "/RefreshFailure.prefab";
                ShapeSyncDatabaseWindow.RefreshAssetDatabase = () => throw new InvalidOperationException("Injected refresh failure");
                int exports = 0;
                ShapeSyncDatabaseWindow.ExportDatabaseFigure = (ShapeSyncDatabase candidateDatabase, GameObject candidateFigure, string destinationPath, out GameObject result, out string diagnostic) => { exports++; result = null; diagnostic = null; return false; };
                Assert.That(window.TryExportDatabaseFigureWithDialog(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Injected refresh failure"));
                Assert.That(exports, Is.Zero);
                Assert.That(Selection.activeObject, Is.SameAs(selectedBefore));
                Assert.That(window.Database, Is.SameAs(database));
                Assert.That(window.FigurePrefab, Is.SameAs(baseFigure));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
            }
            finally { ShapeSyncDatabaseWindow.SaveFigureExportPanel = panelBefore; ShapeSyncDatabaseWindow.ExportDatabaseFigure = exporterBefore; ShapeSyncDatabaseWindow.RefreshAssetDatabase = refreshBefore; Selection.activeObject = selectedBefore; Object.DestroyImmediate(window); }
        }

        [Test]
        public void OutfitExport_ExportsDirectDatabaseOutfitWithoutChangingDatabase()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(created);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                GameObject outfit = new GameObject("hair-1_Base_Outfit");
                outfit.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                new GameObject("OutfitMarker").transform.SetParent(outfit.transform, false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            GameObject databaseOutfit = database.transform.Find("Intermediate/hair-1_Base_Outfit").gameObject;
            Hash128 hashBefore = AssetDatabase.GetAssetDependencyHash(databasePath);

            Assert.That(ShapeSyncDatabaseOutfitExport.TryExport(database, databaseOutfit,
                Root + "/ExportedOutfit.prefab", out GameObject exported, out string diagnostic), Is.True, diagnostic);
            Assert.That(exported, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/ExportedOutfit.prefab")));
            Assert.That(exported, Is.Not.SameAs(databaseOutfit));
            Assert.That(exported.name, Is.EqualTo("ExportedOutfit"));
            Assert.That(exported.transform.Find("OutfitMarker"), Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(hashBefore));
        }

        [Test]
        public void OutfitExport_RejectsExternalAndOccupiedDestinations()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(created);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                GameObject outfit = new GameObject("Outfit");
                outfit.transform.SetParent(contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            GameObject external = new GameObject("External");
            try
            {
                Assert.That(ShapeSyncDatabaseOutfitExport.TryExport(database, external, Root + "/External.prefab", out _, out string externalDiagnostic), Is.False);
                Assert.That(externalDiagnostic, Does.Contain("Intermediate Outfit"));
                GameObject occupied = new GameObject("Occupied");
                try { Assert.That(PrefabUtility.SaveAsPrefabAsset(occupied, Root + "/Occupied.prefab"), Is.Not.Null); }
                finally { Object.DestroyImmediate(occupied); }
                GameObject databaseOutfit = database.transform.Find("Intermediate/Outfit").gameObject;
                Assert.That(ShapeSyncDatabaseOutfitExport.TryExport(database, databaseOutfit, Root + "/Occupied.prefab", out _, out string occupiedDiagnostic), Is.False);
                Assert.That(occupiedDiagnostic, Does.Contain("cannot overwrite"));
            }
            finally { Object.DestroyImmediate(external); }
        }

        private static ShapeSyncDatabase CreateDatabaseWithFigures(bool includeFbm, out string databasePath, out GameObject baseFigure, out GameObject fbmFigure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            databasePath = AssetDatabase.GetAssetPath(created);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                ShapeSyncDatabase database = contents.GetComponent<ShapeSyncDatabase>();
                Transform intermediate = contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<ShapeSyncFigureImportRecord>();
                GameObject marker = new GameObject("CopyMarker");
                marker.transform.SetParent(baseFigure.transform, false);
                marker.transform.localPosition = new Vector3(1f, 2f, 3f);
                Assert.That(database.Registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                fbmFigure = null;
                if (includeFbm)
                {
                    fbmFigure = new GameObject("Tall");
                    fbmFigure.transform.SetParent(intermediate, false);
                    fbmFigure.AddComponent<ShapeSyncFigureImportRecord>();
                    SkinnedMeshRenderer renderer = fbmFigure.AddComponent<SkinnedMeshRenderer>();
                    Mesh mesh = new Mesh { name = "TallMesh" };
                    renderer.sharedMesh = mesh;
                    Assert.That(database.Registry.TryAdmitFigureAxes(database, new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                    }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                    IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] bindings =
                    {
                        new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", fbmFigure) }
                    };
                    Assert.That(database.Registry.TryCommitFigureAxes(database, admissions, bindings, out string commitDiagnostic), Is.True, commitDiagnostic);
                    Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
                    AssetDatabase.AddObjectToAsset(mesh, databasePath);
                }
                EditorUtility.SetDirty(database.Registry);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            baseFigure = reopened.transform.Find("Intermediate/Base").gameObject;
            fbmFigure = includeFbm ? reopened.transform.Find("Intermediate/Tall").gameObject : null;
            return reopened;
        }

        private static ShapeSyncDatabase CreateDatabaseWithTwoFbms(out string databasePath, out GameObject tallFigure, out GameObject shortFigure)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            databasePath = AssetDatabase.GetAssetPath(created);
            GameObject contents = PrefabUtility.LoadPrefabContents(databasePath);
            try
            {
                ShapeSyncDatabase database = contents.GetComponent<ShapeSyncDatabase>();
                Transform intermediate = contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                GameObject baseFigure = new GameObject("Base");
                baseFigure.transform.SetParent(intermediate, false);
                baseFigure.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(database.Registry.TryRegisterBaseFigure(database, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                tallFigure = CreateFbmFigure(intermediate, "Tall", out Mesh tallMesh);
                shortFigure = CreateFbmFigure(intermediate, "Short", out Mesh shortMesh);
                ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts =
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm),
                    new ShapeSyncDatabaseRegistry.FigureAxisDraft("Short", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                };
                Assert.That(database.Registry.TryAdmitFigureAxes(database, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] admissions, out string admissionDiagnostic), Is.True, admissionDiagnostic);
                IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] bindings =
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Tall", tallFigure) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Short", shortFigure) }
                };
                Assert.That(database.Registry.TryCommitFigureAxes(database, admissions, bindings, out string commitDiagnostic), Is.True, commitDiagnostic);
                EditorUtility.SetDirty(database.Registry);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
                AssetDatabase.AddObjectToAsset(tallMesh, databasePath);
                AssetDatabase.AddObjectToAsset(shortMesh, databasePath);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, databasePath), Is.Not.Null);
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            tallFigure = reopened.transform.Find("Intermediate/Tall").gameObject;
            shortFigure = reopened.transform.Find("Intermediate/Short").gameObject;
            return reopened;
        }

        private static GameObject CreateFbmFigure(Transform intermediate, string name, out Mesh mesh)
        {
            GameObject figure = new GameObject(name);
            figure.transform.SetParent(intermediate, false);
            figure.AddComponent<ShapeSyncFigureImportRecord>();
            SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { name = name + "Mesh" };
            renderer.sharedMesh = mesh;
            return figure;
        }

        private static Mesh AddImportedFigurePayload(GameObject figure, string meshName)
        {
            figure.AddComponent<ShapeSyncFigureImportRecord>();
            SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = meshName };
            renderer.sharedMesh = mesh;
            return mesh;
        }
    }
}
#endif
