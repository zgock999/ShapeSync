// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncMorphAxisFollowTests
    {
        private const string Root = ShapeSyncTestAssetPaths.ConsumerTempRoot + "/__Spec20_ov1_ShapeSyncMorphAxisFollowTests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                ShapeSyncTestAssetPaths.EnsureConsumerTempRoot();
                AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_ov1_ShapeSyncMorphAxisFollowTests");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void MorphShapeAxes_FollowFbmAddition()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                GameObject wide = new GameObject("Wide"); wide.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer wideRenderer = wide.AddComponent<SkinnedMeshRenderer>();
                Mesh wideMesh = new Mesh { name = "Wide_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(wideMesh); wideRenderer.sharedMesh = wideMesh;
                ShapeSyncFigureImportRecord wideRecord = wide.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(wideRecord.TryConfigure(new[] { wideRenderer }, out string wideRecordDiagnostic), Is.True, wideRecordDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] wideAdmissions, out string wideAdmissionDiagnostic), Is.True, wideAdmissionDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, wideAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Wide", wide) } }, out string wideCommitDiagnostic), Is.True, wideCommitDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile", "Wide" }), "T-01-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-01-b");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Wide").Value, Is.Zero, "T-01-c");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_FollowPbmAddition()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Bust", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAdmissions, out string pbmAdmissionDiagnostic), Is.True, pbmAdmissionDiagnostic);
                GameObject basePbm = new GameObject("Base_Bust"); basePbm.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer basePbmRenderer = basePbm.AddComponent<SkinnedMeshRenderer>();
                Mesh basePbmMesh = new Mesh { name = "Base_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(basePbmMesh); basePbmRenderer.sharedMesh = basePbmMesh;
                ShapeSyncFigureImportRecord basePbmRecord = basePbm.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(basePbmRecord.TryConfigure(new[] { basePbmRenderer }, out string basePbmRecordDiagnostic), Is.True, basePbmRecordDiagnostic);
                GameObject smilePbm = new GameObject("Smile_Bust"); smilePbm.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smilePbmRenderer = smilePbm.AddComponent<SkinnedMeshRenderer>();
                Mesh smilePbmMesh = new Mesh { name = "Smile_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smilePbmMesh); smilePbmRenderer.sharedMesh = smilePbmMesh;
                ShapeSyncFigureImportRecord smilePbmRecord = smilePbm.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smilePbmRecord.TryConfigure(new[] { smilePbmRenderer }, out string smilePbmRecordDiagnostic), Is.True, smilePbmRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, pbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbm),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smilePbm)
                    }
                }, out string pbmCommitDiagnostic), Is.True, pbmCommitDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(FindShape(database, "morph-a").Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile", "Bust" }), "T-02-a");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_InitializeOnShapeAddition()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-b", "Morph B", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addBDiagnostic), Is.True, addBDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphB = FindShape(database, "morph-b");
            Assert.That(morphB.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile" }), "T-07-a");
            Assert.That(morphB.Morphs.Single().Value, Is.Zero, "T-07-b");
            Assert.That(FindShape(database, "morph-a").Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-07-c");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }


        [Test]
        public void MorphShapeAxes_LeaveNoGenerationDiagnostic()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                GameObject wide = new GameObject("Wide"); wide.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer wideRenderer = wide.AddComponent<SkinnedMeshRenderer>();
                Mesh wideMesh = new Mesh { name = "Wide_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(wideMesh); wideRenderer.sharedMesh = wideMesh;
                ShapeSyncFigureImportRecord wideRecord = wide.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(wideRecord.TryConfigure(new[] { wideRenderer }, out string wideRecordDiagnostic), Is.True, wideRecordDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] wideAdmissions, out string wideAdmissionDiagnostic), Is.True, wideAdmissionDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, wideAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Wide", wide) } }, out string wideCommitDiagnostic), Is.True, wideCommitDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            Assert.That(ShapeSyncDatabaseValidator.ValidateForGeneration(database), Is.Empty, "T-08-a");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_FollowIsIdempotent()
        {
            string[] firstTargets = null;
            float[] firstValues = null;
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                ShapeSyncDatabaseRegistry.ShapeEntry morphAFirst = contents.Registry.Shapes.Single(entry => entry != null && string.Equals(entry.ShapeId, "morph-a", StringComparison.Ordinal));
                firstTargets = morphAFirst.Morphs.Select(value => value.Target).ToArray();
                firstValues = morphAFirst.Morphs.Select(value => value.Value).ToArray();
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(firstTargets), "T-09-a");
            Assert.That(morphA.Morphs.Select(value => value.Value), Is.EqualTo(firstValues), "T-09-a");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        private static ShapeSyncDatabaseRegistry.ShapeEntry FindShape(ShapeSyncDatabase database, string shapeId) =>
            database.Registry.Shapes.Single(entry => entry != null && string.Equals(entry.ShapeId, shapeId, StringComparison.Ordinal));
    

        [Test]
        public void MorphShapeAxes_FollowFbmRemoval()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] smileAdmissions, out string smileAdmissionDiagnostic), Is.True, smileAdmissionDiagnostic);
                GameObject smile = new GameObject("Smile"); smile.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileRenderer = smile.AddComponent<SkinnedMeshRenderer>();
                Mesh smileMesh = new Mesh { name = "Smile_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileMesh); smileRenderer.sharedMesh = smileMesh;
                ShapeSyncFigureImportRecord smileRecord = smile.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileRecord.TryConfigure(new[] { smileRenderer }, out string smileRecordDiagnostic), Is.True, smileRecordDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] wideAdmissions, out string wideAdmissionDiagnostic), Is.True, wideAdmissionDiagnostic);
                GameObject wide = new GameObject("Wide"); wide.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer wideRenderer = wide.AddComponent<SkinnedMeshRenderer>();
                Mesh wideMesh = new Mesh { name = "Wide_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(wideMesh); wideRenderer.sharedMesh = wideMesh;
                ShapeSyncFigureImportRecord wideRecord = wide.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(wideRecord.TryConfigure(new[] { wideRenderer }, out string wideRecordDiagnostic), Is.True, wideRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, smileAdmissions.Concat(wideAdmissions).ToArray(), new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smile) },
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Wide", wide) }
                }, out string commitDiagnostic), Is.True, commitDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Bust", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAdmissions, out string pbmAdmissionDiagnostic), Is.True, pbmAdmissionDiagnostic);
                GameObject baseBust = new GameObject("Base_Bust"); baseBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer baseBustRenderer = baseBust.AddComponent<SkinnedMeshRenderer>();
                Mesh baseBustMesh = new Mesh { name = "Base_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(baseBustMesh); baseBustRenderer.sharedMesh = baseBustMesh;
                ShapeSyncFigureImportRecord baseBustRecord = baseBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(baseBustRecord.TryConfigure(new[] { baseBustRenderer }, out string baseBustRecordDiagnostic), Is.True, baseBustRecordDiagnostic);
                GameObject smileBust = new GameObject("Smile_Bust"); smileBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileBustRenderer = smileBust.AddComponent<SkinnedMeshRenderer>();
                Mesh smileBustMesh = new Mesh { name = "Smile_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileBustMesh); smileBustRenderer.sharedMesh = smileBustMesh;
                ShapeSyncFigureImportRecord smileBustRecord = smileBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileBustRecord.TryConfigure(new[] { smileBustRenderer }, out string smileBustRecordDiagnostic), Is.True, smileBustRecordDiagnostic);
                GameObject wideBust = new GameObject("Wide_Bust"); wideBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer wideBustRenderer = wideBust.AddComponent<SkinnedMeshRenderer>();
                Mesh wideBustMesh = new Mesh { name = "Wide_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(wideBustMesh); wideBustRenderer.sharedMesh = wideBustMesh;
                ShapeSyncFigureImportRecord wideBustRecord = wideBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(wideBustRecord.TryConfigure(new[] { wideBustRenderer }, out string wideBustRecordDiagnostic), Is.True, wideBustRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, pbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseBust),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileBust),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Wide", wideBust)
                    }
                }, out string pbmCommitDiagnostic), Is.True, pbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryRemoveFbmAxis(contents, "Wide", out GameObject[] removedFigures, out Texture[] orphanedTextures, out string removeDiagnostic), Is.True, removeDiagnostic);
            }, out string removeTransactionDiagnostic), Is.True, removeTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile" }), "T-03-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-03-b");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }


        [Test]
        public void MorphShapeAxes_FollowPbmRemoval()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Bust", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAdmissions, out string pbmAdmissionDiagnostic), Is.True, pbmAdmissionDiagnostic);
                GameObject baseBust = new GameObject("Base_Bust"); baseBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer baseBustRenderer = baseBust.AddComponent<SkinnedMeshRenderer>();
                Mesh baseBustMesh = new Mesh { name = "Base_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(baseBustMesh); baseBustRenderer.sharedMesh = baseBustMesh;
                ShapeSyncFigureImportRecord baseBustRecord = baseBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(baseBustRecord.TryConfigure(new[] { baseBustRenderer }, out string baseBustRecordDiagnostic), Is.True, baseBustRecordDiagnostic);
                GameObject smileBust = new GameObject("Smile_Bust"); smileBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileBustRenderer = smileBust.AddComponent<SkinnedMeshRenderer>();
                Mesh smileBustMesh = new Mesh { name = "Smile_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileBustMesh); smileBustRenderer.sharedMesh = smileBustMesh;
                ShapeSyncFigureImportRecord smileBustRecord = smileBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileBustRecord.TryConfigure(new[] { smileBustRenderer }, out string smileBustRecordDiagnostic), Is.True, smileBustRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, pbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseBust),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileBust)
                    }
                }, out string pbmCommitDiagnostic), Is.True, pbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryRemovePbmAxis(contents, "Bust", out GameObject[] removedFigures, out string removeDiagnostic), Is.True, removeDiagnostic);
            }, out string removeTransactionDiagnostic), Is.True, removeTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile" }), "T-04-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-04-b");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }
        [Test]
        public void MorphShapeAxes_FollowAxisRename()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryRenameFbmAxis(contents, "Smile", "Grin", out GameObject[] removedPbmFigures, out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string renameTransactionDiagnostic), Is.True, renameTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Grin" }), "T-05-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Grin").Value, Is.EqualTo(0.5f), "T-05-b");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_FollowAxisReplacement()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryPrepareFbmReplacement(contents, "Smile", "Grin", out int replacementIndex, out GameObject[] removedFigures, out Texture[] orphanedTextures, out string prepareDiagnostic), Is.True, prepareDiagnostic);
                GameObject grin = new GameObject("Grin"); grin.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer grinRenderer = grin.AddComponent<SkinnedMeshRenderer>();
                Mesh grinMesh = new Mesh { name = "Grin_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(grinMesh); grinRenderer.sharedMesh = grinMesh;
                ShapeSyncFigureImportRecord grinRecord = grin.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(grinRecord.TryConfigure(new[] { grinRenderer }, out string grinRecordDiagnostic), Is.True, grinRecordDiagnostic);
                Assert.That(contents.Registry.CommitFbmReplacement(contents, "Grin", grin, false, replacementIndex, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string replaceTransactionDiagnostic), Is.True, replaceTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Grin" }), "T-06-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Grin").Value, Is.EqualTo(0.5f), "T-06-b");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }
        [Test]
        public void MorphShapeAxes_FollowPbmRename()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] fbmAdmissions, out string fbmAdmissionDiagnostic), Is.True, fbmAdmissionDiagnostic);
                GameObject smile = new GameObject("Smile"); smile.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileRenderer = smile.AddComponent<SkinnedMeshRenderer>();
                Mesh smileMesh = new Mesh { name = "Smile_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileMesh); smileRenderer.sharedMesh = smileMesh;
                ShapeSyncFigureImportRecord smileRecord = smile.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileRecord.TryConfigure(new[] { smileRenderer }, out string smileRecordDiagnostic), Is.True, smileRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, fbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smile) } }, out string fbmCommitDiagnostic), Is.True, fbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Bust", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAdmissions, out string pbmAdmissionDiagnostic), Is.True, pbmAdmissionDiagnostic);
                GameObject baseBust = new GameObject("Base_Bust"); baseBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer baseBustRenderer = baseBust.AddComponent<SkinnedMeshRenderer>();
                Mesh baseBustMesh = new Mesh { name = "Base_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(baseBustMesh); baseBustRenderer.sharedMesh = baseBustMesh;
                ShapeSyncFigureImportRecord baseBustRecord = baseBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(baseBustRecord.TryConfigure(new[] { baseBustRenderer }, out string baseBustRecordDiagnostic), Is.True, baseBustRecordDiagnostic);
                GameObject smileBust = new GameObject("Smile_Bust"); smileBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileBustRenderer = smileBust.AddComponent<SkinnedMeshRenderer>();
                Mesh smileBustMesh = new Mesh { name = "Smile_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileBustMesh); smileBustRenderer.sharedMesh = smileBustMesh;
                ShapeSyncFigureImportRecord smileBustRecord = smileBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileBustRecord.TryConfigure(new[] { smileBustRenderer }, out string smileBustRecordDiagnostic), Is.True, smileBustRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, pbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseBust),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileBust)
                    }
                }, out string pbmCommitDiagnostic), Is.True, pbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f }, new MorphValue { Target = "Bust", Value = 0.25f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                Assert.That(contents.Registry.TryRenamePbmAxis(contents, "Bust", "Chest", out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string transactionDiagnostic), Is.True, transactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile", "Chest" }), "T-05P-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Chest").Value, Is.EqualTo(0.25f), "T-05P-b");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-05P-c");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_FollowPbmReplacement()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseFigure = new GameObject("Base"); baseFigure.transform.SetParent(intermediate, false);
                Assert.That(contents.Registry.TryRegisterBaseFigure(contents, "Base", baseFigure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] fbmAdmissions, out string fbmAdmissionDiagnostic), Is.True, fbmAdmissionDiagnostic);
                GameObject smile = new GameObject("Smile"); smile.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileRenderer = smile.AddComponent<SkinnedMeshRenderer>();
                Mesh smileMesh = new Mesh { name = "Smile_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileMesh); smileRenderer.sharedMesh = smileMesh;
                ShapeSyncFigureImportRecord smileRecord = smile.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileRecord.TryConfigure(new[] { smileRenderer }, out string smileRecordDiagnostic), Is.True, smileRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, fbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smile) } }, out string fbmCommitDiagnostic), Is.True, fbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Bust", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAdmissions, out string pbmAdmissionDiagnostic), Is.True, pbmAdmissionDiagnostic);
                GameObject baseBust = new GameObject("Base_Bust"); baseBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer baseBustRenderer = baseBust.AddComponent<SkinnedMeshRenderer>();
                Mesh baseBustMesh = new Mesh { name = "Base_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(baseBustMesh); baseBustRenderer.sharedMesh = baseBustMesh;
                ShapeSyncFigureImportRecord baseBustRecord = baseBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(baseBustRecord.TryConfigure(new[] { baseBustRenderer }, out string baseBustRecordDiagnostic), Is.True, baseBustRecordDiagnostic);
                GameObject smileBust = new GameObject("Smile_Bust"); smileBust.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileBustRenderer = smileBust.AddComponent<SkinnedMeshRenderer>();
                Mesh smileBustMesh = new Mesh { name = "Smile_Bust_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileBustMesh); smileBustRenderer.sharedMesh = smileBustMesh;
                ShapeSyncFigureImportRecord smileBustRecord = smileBust.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileBustRecord.TryConfigure(new[] { smileBustRenderer }, out string smileBustRecordDiagnostic), Is.True, smileBustRecordDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, pbmAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[]
                {
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseBust),
                        new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileBust)
                    }
                }, out string pbmCommitDiagnostic), Is.True, pbmCommitDiagnostic);
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f }, new MorphValue { Target = "Bust", Value = 0.25f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                GameObject baseChest = new GameObject("Base_Chest"); baseChest.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer baseChestRenderer = baseChest.AddComponent<SkinnedMeshRenderer>();
                Mesh baseChestMesh = new Mesh { name = "Base_Chest_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(baseChestMesh); baseChestRenderer.sharedMesh = baseChestMesh;
                ShapeSyncFigureImportRecord baseChestRecord = baseChest.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(baseChestRecord.TryConfigure(new[] { baseChestRenderer }, out string baseChestRecordDiagnostic), Is.True, baseChestRecordDiagnostic);
                GameObject smileChest = new GameObject("Smile_Chest"); smileChest.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer smileChestRenderer = smileChest.AddComponent<SkinnedMeshRenderer>();
                Mesh smileChestMesh = new Mesh { name = "Smile_Chest_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(smileChestMesh); smileChestRenderer.sharedMesh = smileChestMesh;
                ShapeSyncFigureImportRecord smileChestRecord = smileChest.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(smileChestRecord.TryConfigure(new[] { smileChestRenderer }, out string smileChestRecordDiagnostic), Is.True, smileChestRecordDiagnostic);
                Assert.That(contents.Registry.TryPreparePbmReplacement(contents, "Bust", "Chest", new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseChest),
                    new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileChest)
                }, out int replacementIndex, out GameObject[] removedFigures, out string prepareDiagnostic), Is.True, prepareDiagnostic);
                Assert.That(contents.Registry.CommitPbmReplacement(contents, "Chest", new[]
                {
                    new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding(ShapeSyncDatabaseRegistry.BaseShapeKey, baseChest),
                    new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Smile", smileChest)
                }, replacementIndex, out string commitDiagnostic), Is.True, commitDiagnostic);
            }, out string replaceTransactionDiagnostic), Is.True, replaceTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile", "Chest" }), "T-06P-a");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Chest").Value, Is.EqualTo(0.25f), "T-06P-b");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-06P-c");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_RollbackLeavesNoPartialFollow()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("FBM_Reserved", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out _, out _), Is.False);
            }, out string rollbackTransactionDiagnostic), Is.True, rollbackTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Smile" }), "T-11-a target");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Smile").Value, Is.EqualTo(0.5f), "T-11-a value");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }

        [Test]
        public void MorphShapeAxes_SurviveReimport()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
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
                Assert.That(contents.Registry.TryAddShape("morph-a", "Morph A", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
                Assert.That(contents.Registry.TrySetShapeMorphs("morph-a", new[] { new MorphValue { Target = "Smile", Value = 0.5f } }, out string morphDiagnostic), Is.True, morphDiagnostic);
                Assert.That(contents.Registry.TryAddShape("hair-a", "Hair A", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);
                GameObject wide = new GameObject("Wide"); wide.transform.SetParent(intermediate, false);
                SkinnedMeshRenderer wideRenderer = wide.AddComponent<SkinnedMeshRenderer>();
                Mesh wideMesh = new Mesh { name = "Wide_MergedSkinnedMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                transaction.AddSubAsset(wideMesh); wideRenderer.sharedMesh = wideMesh;
                ShapeSyncFigureImportRecord wideRecord = wide.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(wideRecord.TryConfigure(new[] { wideRenderer }, out string wideRecordDiagnostic), Is.True, wideRecordDiagnostic);
                Assert.That(contents.Registry.TryAdmitFigureAxes(contents, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] wideAdmissions, out string wideAdmissionDiagnostic), Is.True, wideAdmissionDiagnostic);
                Assert.That(contents.Registry.TryCommitFigureAxes(contents, wideAdmissions, new IReadOnlyList<ShapeSyncDatabaseRegistry.FigureAxisFigureBinding>[] { new[] { new ShapeSyncDatabaseRegistry.FigureAxisFigureBinding("Wide", wide) } }, out string wideCommitDiagnostic), Is.True, wideCommitDiagnostic);
            }, out string seedDiagnostic), Is.True, seedDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, intermediate, transaction) =>
            {
                Assert.That(contents.Registry.TryRenameFbmAxis(contents, "Smile", "Grin", out GameObject[] removedPbmFigures, out string renameDiagnostic), Is.True, renameDiagnostic);
            }, out string renameTransactionDiagnostic), Is.True, renameTransactionDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out string reopenDiagnostic), Is.True, reopenDiagnostic);
            ShapeSyncDatabaseRegistry.ShapeEntry morphA = FindShape(database, "morph-a");
            Assert.That(morphA.Morphs.Select(value => value.Target), Is.EqualTo(new[] { "Grin", "Wide" }), "T-10-a target");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Grin").Value, Is.EqualTo(0.5f), "T-10-a Grin value");
            Assert.That(morphA.Morphs.Single(value => value.Target == "Wide").Value, Is.EqualTo(0f), "T-10-a Wide value");
            Assert.That(FindShape(database, "hair-a").Morphs, Is.Empty, "T-01-d");
        }
}
}
#endif
