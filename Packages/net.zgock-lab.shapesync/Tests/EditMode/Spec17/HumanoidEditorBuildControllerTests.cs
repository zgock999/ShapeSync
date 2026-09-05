// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidEditorBuildControllerTests
    {
        private const string StageFolder = ShapeSyncTestAssetPaths.Spec17ControllerStageRoot;
        private const string StagePrefix = "__Spec17_6_ControllerStage";

        [Test]
        public void ConcreteBackend_ResolvesComputeShadersFromDevelopmentProjectByGuid()
        {
            Type type = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);
            object controller = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, Array.Empty<object>(), null);
            try
            {
                object[] arguments = { null };
                object backend = Invoke(controller, "CreateConcreteBackend", arguments);
                StackMachineDiagnostic diagnostic = (StackMachineDiagnostic)arguments[0];
                Assert.That(backend, Is.Not.Null, diagnostic?.message);
                Assert.That(diagnostic, Is.Null);
            }
            finally { ((IDisposable)controller).Dispose(); }
        }

        [Test]
        public void CloneSourceMesh_DoesNotDirtyPersistentSourceAsset()
        {
            string path = ShapeSyncTestAssetPaths.ConsumerAssetPath("zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_PersistentMesh.asset");
            Mesh fixture = CreateMeshCloneRoundTripFixture();
            AssetDatabase.CreateAsset(fixture, path);
            AssetDatabase.SaveAssets();
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Assert.That(source, Is.Not.Null);
            Assert.That(EditorUtility.IsPersistent(source), Is.True);
            Assert.That(EditorUtility.IsDirty(source), Is.False, "The source Mesh must be clean before the clone regression check.");

            Mesh clone = null;
            try
            {
                clone = ShapeSyncMeshCloneUtility.Clone(source);
                Assert.That(clone, Is.Not.Null);
                Assert.That(EditorUtility.IsPersistent(clone), Is.False);
                Assert.That(clone.vertexCount, Is.EqualTo(source.vertexCount));
                Assert.That(clone.subMeshCount, Is.EqualTo(source.subMeshCount));
                Assert.That(clone.blendShapeCount, Is.EqualTo(source.blendShapeCount));
                Assert.That(EditorUtility.IsDirty(source), Is.False, "Cloning a persistent source Mesh must not mark the source dirty.");
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (source != null) EditorUtility.ClearDirty(source);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void CloneMesh_RoundTripsVertexLayoutSubmeshesSkinningAndBlendShapes()
        {
            Mesh source = CreateMeshCloneRoundTripFixture();
            Mesh clone = null;
            try
            {
                clone = ShapeSyncMeshCloneUtility.Clone(source);
                Assert.That(clone, Is.Not.Null);
                Assert.That(clone.vertexCount, Is.EqualTo(source.vertexCount));
                Assert.That(clone.subMeshCount, Is.EqualTo(source.subMeshCount));
                Assert.That(clone.indexFormat, Is.EqualTo(source.indexFormat));
                Assert.That(clone.bindposes.Length, Is.EqualTo(source.bindposes.Length));
                for (int i = 0; i < source.bindposes.Length; i++) Assert.That(clone.bindposes[i], Is.EqualTo(source.bindposes[i]));

                VertexAttributeDescriptor[] sourceAttributes = source.GetVertexAttributes();
                VertexAttributeDescriptor[] cloneAttributes = clone.GetVertexAttributes();
                Assert.That(cloneAttributes.Length, Is.EqualTo(sourceAttributes.Length));
                for (int i = 0; i < sourceAttributes.Length; i++)
                {
                    Assert.That(cloneAttributes[i].attribute, Is.EqualTo(sourceAttributes[i].attribute));
                    Assert.That(cloneAttributes[i].format, Is.EqualTo(sourceAttributes[i].format));
                    Assert.That(cloneAttributes[i].dimension, Is.EqualTo(sourceAttributes[i].dimension));
                    Assert.That(cloneAttributes[i].stream, Is.EqualTo(sourceAttributes[i].stream));
                }

                Color[] sourceColors = source.colors;
                Color[] cloneColors = clone.colors;
                Assert.That(cloneColors.Length, Is.EqualTo(sourceColors.Length));
                for (int i = 0; i < sourceColors.Length; i++)
                {
                    Assert.That(cloneColors[i].r, Is.EqualTo(sourceColors[i].r).Within(0.0000001f));
                    Assert.That(cloneColors[i].g, Is.EqualTo(sourceColors[i].g).Within(0.0000001f));
                    Assert.That(cloneColors[i].b, Is.EqualTo(sourceColors[i].b).Within(0.0000001f));
                    Assert.That(cloneColors[i].a, Is.EqualTo(sourceColors[i].a).Within(0.0000001f));
                }

                var sourceUv2 = new List<Vector2>();
                var cloneUv2 = new List<Vector2>();
                var sourceUv3 = new List<Vector3>();
                var cloneUv3 = new List<Vector3>();
                source.GetUVs(0, sourceUv2); clone.GetUVs(0, cloneUv2);
                source.GetUVs(1, sourceUv3); clone.GetUVs(1, cloneUv3);
                Assert.That(cloneUv2, Is.EqualTo(sourceUv2));
                Assert.That(cloneUv3, Is.EqualTo(sourceUv3));
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                {
                    SubMeshDescriptor sourceDescriptor = source.GetSubMesh(submesh);
                    SubMeshDescriptor cloneDescriptor = clone.GetSubMesh(submesh);
                    Assert.That(sourceDescriptor.baseVertex, Is.GreaterThan(0), "The round-trip fixture must exercise a non-zero baseVertex.");
                    Assert.That(cloneDescriptor.baseVertex, Is.EqualTo(sourceDescriptor.baseVertex));
                    Assert.That(cloneDescriptor.indexStart, Is.EqualTo(sourceDescriptor.indexStart));
                    Assert.That(cloneDescriptor.indexCount, Is.EqualTo(sourceDescriptor.indexCount));
                    Assert.That(cloneDescriptor.firstVertex, Is.EqualTo(sourceDescriptor.firstVertex));
                    Assert.That(cloneDescriptor.vertexCount, Is.EqualTo(sourceDescriptor.vertexCount));
                    Assert.That(cloneDescriptor.topology, Is.EqualTo(sourceDescriptor.topology));
                    Assert.That(clone.GetIndices(submesh, false), Is.EqualTo(source.GetIndices(submesh, false)));
                }

                AssertVariableBoneWeightsEqual(source, clone);
                AssertBlendShapesEqual(source, clone);
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void StartAndCancel_DestroysUnpublishedCandidateAndCancelsBackend()
        {
            var figure = new GameObject("Spec17_6_Figure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(figure.name, Is.EqualTo("Spec17_6_Figure"));
                Assert.That(figure.hideFlags, Is.EqualTo(HideFlags.None));
                Assert.That(figure.activeSelf, Is.True);
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.SameAs(figure));
                Assert.That(GetProperty(controller, "SourceDocument"), Is.Not.Null);
                Assert.That(InvokeTakeProvenance(controller, out _, out StackMachineDiagnostic pendingTake), Is.False);
                Assert.That(pendingTake.domainCode, Is.EqualTo("VrmTransportBuildNotSucceeded"));

                Invoke(controller, "Cancel");

                Assert.That(backend.Cancelled, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.Null);
                Assert.That(GetProperty(controller, "SourceDocument"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
            }
            finally
            {
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void SuccessWithoutTake_DisposeDestroysEscrowedResult()
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Mesh mesh = backend.ProducedMesh;
                Assert.That(mesh, Is.Not.Null);

                ((IDisposable)controller).Dispose();

                Assert.That(candidate == null, Is.True);
                Assert.That(mesh == null, Is.True);
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void SuccessTakeProvenance_RejectsBeforeStageAndCandidateApply()
        {
            var figure = new GameObject("Spec17_6_Figure"); var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeTakeProvenance(controller, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportCandidateNotReady"));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [TestCase(HumanoidBuildPhaseStatus.Failed)]
        [TestCase(HumanoidBuildPhaseStatus.Cancelled)]
        public void TerminalMeshPhase_DestroysCandidate(HumanoidBuildPhaseStatus terminal)
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            object controller = CreateController(() => new TerminalBackend(terminal));
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                Assert.That(InvokePump(controller, out _), Is.EqualTo(terminal == HumanoidBuildPhaseStatus.Failed ? HumanoidBuildOperationStatus.Failed : HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
            }
            finally { ((IDisposable)controller).Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [TestCase(HumanoidBuildPhaseStatus.Failed)]
        [TestCase(HumanoidBuildPhaseStatus.Cancelled)]
        public void TerminalMaterialPhase_DestroysCandidateAndMeshEscrow(HumanoidBuildPhaseStatus terminal)
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new MaterialTerminalBackend(terminal); object controller = CreateController(() => backend);
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _);
                Assert.That(InvokePump(controller, out _), Is.EqualTo(terminal == HumanoidBuildPhaseStatus.Failed ? HumanoidBuildOperationStatus.Failed : HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(backend.ProducedMesh == null, Is.True);
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void Start_RejectsMissingInputAndBackendBeginWithoutCreatingCandidate()
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            object controller = CreateController(() => new RejectingBackend());
            try
            {
                Assert.That(InvokeStart(controller, null, document, out StackMachineDiagnostic figureDiagnostic), Is.False);
                Assert.That(figureDiagnostic.domainCode, Is.EqualTo("FigureRequired"));
                Assert.That(InvokeStart(controller, figure, null, out StackMachineDiagnostic documentDiagnostic), Is.False);
                Assert.That(documentDiagnostic.domainCode, Is.EqualTo("DocumentRequired"));
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic beginDiagnostic), Is.False);
                Assert.That(beginDiagnostic.domainCode, Is.EqualTo("TestBeginRejected"));
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
            }
            finally { ((IDisposable)controller).Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void DisposedApis_RejectOutsideControllerLifetime()
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                ((IDisposable)controller).Dispose();
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic startDisposed), Is.False);
                Assert.That(startDisposed.domainCode, Is.EqualTo("EditorBuildControllerDisposed"));
                Assert.That(InvokeTakeProvenance(controller, out _, out StackMachineDiagnostic provenanceDisposed), Is.False);
                Assert.That(provenanceDisposed.domainCode, Is.EqualTo("EditorBuildControllerDisposed"));
            }
            finally { ((IDisposable)controller).Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void SuccessStage_CancelClearsEscrowAndRetainsPersistentArtifactsAsWarnings()
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(GetProperty(controller, "StagedAssets"), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(StageFolder + "/" + StagePrefix + ".asset"), Is.Not.Null);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Invoke(controller, "Cancel");
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(controller, "StagedAssets"), Is.Null);
                Assert.That(candidate == null, Is.True);
                Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(StageFolder + "/" + StagePrefix + ".asset"), Is.Not.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
                ((IDisposable)controller).Dispose();
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void SuccessStage_ApplySetsCandidateForLaterVrmTransport()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out StackMachineDiagnostic stageDiagnostic), Is.True, stageDiagnostic?.message);
                Assert.That(InvokeApplyStage(controller, out StackMachineDiagnostic applyDiagnostic), Is.True, applyDiagnostic?.message);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate"); var renderer = candidate.GetComponent<SkinnedMeshRenderer>(); object stage = GetProperty(controller, "StagedAssets");
                Assert.That((bool)GetProperty(controller, "AreStagedAssetsApplied"), Is.True);
                Assert.That(renderer.sharedMesh, Is.SameAs(GetProperty(stage, "Mesh")));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(((System.Collections.IEnumerable)GetProperty(stage, "Materials")).CastMaterials()));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AppliedStage_TakeVrmProvenanceKeepsCandidateAndStageUntilPublishOwnerTakesOver()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            HumanoidVrmTransportProvenance provenance = null;
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out StackMachineDiagnostic stageDiagnostic), Is.True, stageDiagnostic?.message);
                Assert.That(InvokeApplyStage(controller, out StackMachineDiagnostic applyDiagnostic), Is.True, applyDiagnostic?.message);

                GameObject candidate = (GameObject)GetProperty(controller, "Candidate"); object stage = GetProperty(controller, "StagedAssets");
                var renderer = candidate.GetComponent<SkinnedMeshRenderer>(); Mesh stagedMesh = (Mesh)GetProperty(stage, "Mesh");
                Material[] stagedMaterials = ((System.Collections.IEnumerable)GetProperty(stage, "Materials")).CastMaterials();
                Assert.That(InvokeTakeProvenance(controller, out provenance, out StackMachineDiagnostic takeDiagnostic), Is.True, takeDiagnostic?.message);
                Assert.That(provenance.AttachedOutfitLogicalNames, Is.EqualTo(new[] { "dress" }));
                Assert.That(InvokeTakeProvenance(controller, out _, out StackMachineDiagnostic duplicate), Is.False);
                Assert.That(duplicate.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                Assert.That((bool)GetProperty(controller, "AreStagedAssetsApplied"), Is.True);
                Assert.That(GetProperty(controller, "StagedAssets"), Is.SameAs(stage));
                Assert.That(renderer.sharedMesh, Is.SameAs(stagedMesh));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(stagedMaterials));
            }
            finally { provenance?.Dispose(); ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_CancelRetainsTakenVrmProvenanceAndReportsPersistentAssets()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            HumanoidVrmTransportProvenance provenance = null;
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True);
                Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTakeProvenance(controller, out provenance, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");

                Invoke(controller, "Cancel");

                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(candidate == null, Is.True);
                Assert.That(GetProperty(controller, "StagedAssets"), Is.Null);
                Assert.That((bool)GetProperty(controller, "AreStagedAssetsApplied"), Is.False);
                Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(StageFolder + "/" + StagePrefix + ".asset"), Is.Not.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
                Assert.That(provenance.AttachedOutfitLogicalNames, Is.EqualTo(new[] { "dress" }));
            }
            finally { provenance?.Dispose(); ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_TransportEscrowsOptionalVrmResultUntilCancel()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTransport(controller, executor, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(executor.Candidate, Is.SameAs(GetProperty(controller, "Candidate")));
                Assert.That(executor.Figure, Is.SameAs(figure)); Assert.That(executor.LogicalNames, Is.EqualTo(new[] { "dress" }));
                Assert.That(GetProperty(controller, "VrmTransportResult"), Is.SameAs(executor.Result));
                Invoke(controller, "Cancel");
                Assert.That(executor.Result.Disposed, Is.True);
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_TransportFailureDisposesPartialResultAndAbortsAllEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new FailingVrmExecutor();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");

                Assert.That(InvokeTransport(controller, executor, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TestVrmTransportRejected"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(candidate == null, Is.True);
                Assert.That(GetProperty(controller, "VrmTransportResult"), Is.Null);
                Assert.That(GetProperty(controller, "StagedAssets"), Is.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_MissingVrmExecutorRejectsWithoutTakingProvenance()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);

                Assert.That(InvokeTransport(controller, null, out StackMachineDiagnostic missing), Is.False);
                Assert.That(missing.domainCode, Is.EqualTo("VrmTransportExecutorRequired"));
                Assert.That(GetProperty(controller, "Candidate"), Is.Not.Null);
                Assert.That(InvokeTransport(controller, executor, out StackMachineDiagnostic accepted), Is.True, accepted?.message);
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_VrmAssetStageAndFinalizeKeepResultUntilSuccessfulFinalize()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var outfit = new GameObject("Spec17_6_Outfit"); var prefab = new GameObject("Spec17_6_Prefab"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_happy.asset"), ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_vrm.asset") } };
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTransport(controller, executor, out _), Is.True);
                Assert.That(InvokeVrmStage(controller, executor, out StackMachineDiagnostic stageDiagnostic), Is.True, stageDiagnostic?.message);
                Assert.That(GetProperty(controller, "StagedVrmAssetPaths"), Is.EqualTo(executor.StagePaths));
                Assert.That(InvokeVrmFinalize(controller, executor, prefab, out StackMachineDiagnostic finalizeDiagnostic), Is.True, finalizeDiagnostic?.message);
                Assert.That(executor.FinalizedPrefab, Is.SameAs(prefab));
                Assert.That((bool)GetProperty(controller, "AreVrmAssetsFinalized"), Is.True);
                Invoke(controller, "Cancel");
                Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void AppliedStage_VrmAssetStageFailureRetainsPartialPathsAndAbortsEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/partial.asset") }, StageSucceeds = false };
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True); Assert.That(InvokeTransport(controller, executor, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Assert.That(InvokeVrmStage(controller, executor, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TestVrmStageRejected")); Assert.That(candidate == null, Is.True); Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(3));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AppliedStage_VrmFinalizeFailureOrExceptionRetainsStagedPathsAndAbortsEscrow(bool throws)
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var outfit = new GameObject("Spec17_6_Outfit"); var prefab = new GameObject("Spec17_6_Prefab"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_vrm.asset") }, FinalizeSucceeds = false, ThrowOnFinalize = throws };
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True); Assert.That(InvokeTransport(controller, executor, out _), Is.True); Assert.That(InvokeVrmStage(controller, executor, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Assert.That(InvokeVrmFinalize(controller, executor, prefab, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo(throws ? "VrmPublishFinalizeUnexpectedFailure" : "TestVrmFinalizeRejected"));
                Assert.That(candidate == null, Is.True); Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(3));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void StagedAssetsApplyFailure_AbortsControllerEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True);
                InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate"); Mesh finalMesh = backend.ProducedMesh;
                var unexpectedRenderer = new GameObject("UnexpectedRenderer");
                unexpectedRenderer.transform.SetParent(candidate.transform, false);
                unexpectedRenderer.AddComponent<SkinnedMeshRenderer>();
                Assert.That(InvokeApplyStage(controller, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishCandidateRendererCountInvalid"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(candidate == null, Is.True); Assert.That(finalMesh == null, Is.True); Assert.That(GetProperty(controller, "StagedAssets"), Is.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
                Assert.That(InvokeTakeProvenance(controller, out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic provenanceDiagnostic), Is.False);
                Assert.That(provenance, Is.Null);
                Assert.That(provenanceDiagnostic.domainCode, Is.EqualTo("VrmTransportOperationMissing"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void StageFailure_TransfersPartialArtifactPathsToControllerWarningEscrow()
        {
            object controller = CreateController(null); HumanoidBuildResult result = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture baseTexture = null; RenderTexture normalTexture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sampler); source.SetTexture("_BumpMap", sampler);
                target = new Material(source); baseTexture = CreateTexture(sampler); normalTexture = CreateTexture(sampler); target.SetTexture("_BaseMap", baseTexture); target.SetTexture("_BumpMap", normalTexture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); InMemoryHumanoidMesh payload = CreateMesh(source, target, adapter); Mesh finalMesh = payload.Mesh; result = new HumanoidBuildResult(payload);
                SetField(controller, "result", result); SetProperty(controller, "Status", HumanoidBuildOperationStatus.Succeeded); result = null;
                var candidate = new GameObject("Spec17_6_FailedStageCandidate"); SetField(controller, "candidate", candidate);
                int writes = 0; SetWriter((path, bytes) => { if (writes++ == 1) throw new System.IO.IOException("injected"); System.IO.File.WriteAllBytes(path, bytes); });
                Assert.That(InvokeStage(controller, StageFolder, "Look", out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishAssetStagingFailed"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(GetProperty(controller, "Diagnostic"), Is.SameAs(diagnostic));
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(1));
                Assert.That(candidate == null, Is.True);
                Assert.That(finalMesh == null, Is.True);
                Assert.That(target == null, Is.True);
                Assert.That(source == null, Is.False);
                Assert.That(InvokeTakeProvenance(controller, out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic provenanceDiagnostic), Is.False);
                Assert.That(provenance, Is.Null);
                Assert.That(provenanceDiagnostic.domainCode, Is.EqualTo("VrmTransportOperationMissing"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(1));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out StackMachineDiagnostic terminal), Is.False);
                Assert.That(terminal.domainCode, Is.EqualTo("EditorBuildResultNotReady"));
            }
            finally { SetWriter(System.IO.File.WriteAllBytes); ((IDisposable)controller).Dispose(); result?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(baseTexture); Release(normalTexture); UnityEngine.Object.DestroyImmediate(adapter); AssetDatabase.DeleteAsset(StageFolder); }
        }

        [Test]
        public void AppliedStage_CommitPrefabReloadsReferencesAndReleasesSuccessfulEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            figure.AddComponent<ShapeDirector>();
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);

                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Assert.That(InvokeCommit(controller, StageFolder, "Look", null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                string prefabPath = (string)GetProperty(controller, "PublishedPrefabAssetPath");
                Assert.That(prefabPath, Is.EqualTo(StageFolder + "/" + StagePrefix + ".prefab"));
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(prefab.activeSelf, Is.True);
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(IsShapeSyncRuntimeBehaviour), Is.False);
                Assert.That(candidate == null, Is.True);
                Assert.That(GetProperty(controller, "StagedAssets"), Is.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(0));
                Assert.That(InvokeCommit(controller, StageFolder, "Look", null, out StackMachineDiagnostic duplicate), Is.False);
                Assert.That(duplicate.domainCode, Is.EqualTo("PublishPrefabAlreadyCommitted"));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void PureHumanoidCleanup_RemovesShapeSyncRuntimeBehavioursAndRetainsUnityComponents()
        {
            var candidate = new GameObject("Spec17_6_PureCleanup"); candidate.SetActive(false);
            var child = new GameObject("Spec17_6_PureCleanupChild"); child.transform.SetParent(candidate.transform, false);
            try
            {
                candidate.AddComponent<DynamicBoneBlender>(); candidate.AddComponent<OutfitAttacher>(); candidate.AddComponent<MaterialProxy>(); candidate.AddComponent<ShapeDirector>();
                child.AddComponent<ShapeSyncOutfit>(); AudioSource retained = child.AddComponent<AudioSource>();

                Assert.That(InvokeStrip(candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(candidate.GetComponentsInChildren<MonoBehaviour>(true).Any(IsShapeSyncRuntimeBehaviour), Is.False);
                Assert.That(child.GetComponent<AudioSource>(), Is.SameAs(retained));
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void AppliedStage_CommitPrefabVrmFinalizeFailureKeepsPersistentArtifactsAsWarnings()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var outfit = new GameObject("Spec17_6_Outfit");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_vrm.asset") }, FinalizeSucceeds = false };
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTransport(controller, executor, out _), Is.True); Assert.That(InvokeVrmStage(controller, executor, out _), Is.True);

                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                Assert.That(InvokeCommit(controller, StageFolder, "Look", executor, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TestVrmFinalizeRejected"));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(StageFolder + "/" + StagePrefix + ".prefab"), Is.Not.Null);
                Assert.That(candidate == null, Is.True); Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(4));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_CommitPrefabVrmSuccessFinalizesPersistentPrefabThenReleasesEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var outfit = new GameObject("Spec17_6_Outfit");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_vrm.asset") } };
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTransport(controller, executor, out _), Is.True); Assert.That(InvokeVrmStage(controller, executor, out _), Is.True);

                Assert.That(InvokeCommit(controller, StageFolder, "Look", executor, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(executor.FinalizedPrefab, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(StageFolder + "/" + StagePrefix + ".prefab")));
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(executor.FinalizedPrefab), Is.True);
                Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(0));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_CommitPrefabSaveExceptionAbortsCandidateAndRetainsIndividualAssets()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            Func<GameObject, string, GameObject> previousSave = GetPrefabSave();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");
                SetPrefabSave((_, __) => throw new IOException("Injected Prefab save failure."));

                Assert.That(InvokeCommit(controller, StageFolder, "Look", null, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishPrefabSaveFailed"));
                Assert.That(candidate == null, Is.True);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(StageFolder + "/" + StagePrefix + ".prefab"), Is.Null);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(2));
            }
            finally { SetPrefabSave(previousSave); ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AppliedStage_CommitPrefabFailureReleasesPersistentVrmOwnershipBeforeEscrowCleanup()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); var outfit = new GameObject("Spec17_6_Outfit");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend);
            var executor = new TrackingVrmExecutor { StagePaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("VRM/Look_vrm.asset") } };
            Func<GameObject, string, GameObject> previousSave = GetPrefabSave();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                Assert.That(InvokeTransport(controller, executor, out _), Is.True); Assert.That(InvokeVrmStage(controller, executor, out _), Is.True);
                SetPrefabSave((_, __) => throw new IOException("Injected Prefab save failure."));

                Assert.That(InvokeCommit(controller, StageFolder, "Look", executor, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishPrefabSaveFailed"));
                Assert.That(executor.AssetOwnershipReleased, Is.True);
                Assert.That(executor.Result.Disposed, Is.True);
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(3));
            }
            finally { SetPrefabSave(previousSave); ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_CommitRejectsUnstagedVrmWithoutMutatingCandidateOrEscrow()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>(); figure.AddComponent<ShapeDirector>();
            var outfit = new GameObject("Spec17_6_Outfit"); var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new SuccessBackend(CreateCoreProvenance(outfit)); object controller = CreateController(() => backend); var executor = new TrackingVrmExecutor();
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True); Assert.That(InvokeTransport(controller, executor, out _), Is.True);
                GameObject candidate = (GameObject)GetProperty(controller, "Candidate");

                Assert.That(InvokeCommit(controller, StageFolder, "Look", executor, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPublishAssetsNotStaged"));
                Assert.That(GetProperty(controller, "Candidate"), Is.SameAs(candidate));
                // Candidate normalization completes at TryStart; this failed commit must not
                // reintroduce a ShapeSync component into the already-normalized clone.
                Assert.That(candidate.GetComponent<ShapeDirector>(), Is.Null);
                Assert.That(executor.Result.Disposed, Is.False);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(0));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); UnityEngine.Object.DestroyImmediate(outfit); }
        }

        [Test]
        public void AppliedStage_CommitRejectsOccupiedPrefabAndRetainsExistingArtifactAsWarning()
        {
            var figure = new GameObject("Spec17_6_Figure"); figure.AddComponent<SkinnedMeshRenderer>();
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); var backend = new SuccessBackend(); object controller = CreateController(() => backend);
            try
            {
                AssetDatabase.DeleteAsset(StageFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_ControllerStage");
                Assert.That(InvokeStart(controller, figure, document, out _), Is.True); InvokePump(controller, out _); InvokePump(controller, out _); Assert.That(InvokePump(controller, out _), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(InvokeStage(controller, StageFolder, "Look", out _), Is.True); Assert.That(InvokeApplyStage(controller, out _), Is.True);
                var preexistingSource = new GameObject("Spec17_6_PreexistingPrefab"); PrefabUtility.SaveAsPrefabAsset(preexistingSource, StageFolder + "/" + StagePrefix + ".prefab"); UnityEngine.Object.DestroyImmediate(preexistingSource);
                GameObject preexisting = AssetDatabase.LoadAssetAtPath<GameObject>(StageFolder + "/" + StagePrefix + ".prefab"); GameObject candidate = (GameObject)GetProperty(controller, "Candidate");

                Assert.That(InvokeCommit(controller, StageFolder, "Look", null, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishAssetPathOccupied"));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(StageFolder + "/" + StagePrefix + ".prefab"), Is.SameAs(preexisting));
                Assert.That(candidate == null, Is.True);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(Count((System.Collections.IEnumerable)GetProperty(controller, "ResidualArtifactPaths")), Is.EqualTo(3));
            }
            finally { ((IDisposable)controller).Dispose(); backend.Dispose(); AssetDatabase.DeleteAsset(StageFolder); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); }
        }

        private static object CreateController(Func<IHumanoidBuildBackend> factory)
        {
            Type type = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { factory }, null);
        }

        private static bool InvokeStart(object controller, GameObject figure, ShapeSyncDocumentAsset document, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { figure, document, null };
            bool result = (bool)Invoke(controller, "TryStart", args);
            diagnostic = (StackMachineDiagnostic)args[2];
            return result;
        }

        private static bool InvokeTakeProvenance(object controller, out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { null, null };
            bool result = (bool)Invoke(controller, "TryTakeVrmTransportProvenance", args);
            provenance = (HumanoidVrmTransportProvenance)args[0];
            diagnostic = (StackMachineDiagnostic)args[1];
            return result;
        }

        private static HumanoidBuildOperationStatus InvokePump(object controller, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { null };
            HumanoidBuildOperationStatus status = (HumanoidBuildOperationStatus)Invoke(controller, "Pump", args);
            diagnostic = (StackMachineDiagnostic)args[0];
            return status;
        }
        private static bool InvokeStage(object controller, string folder, string documentName, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { folder, documentName, null };
            bool success = (bool)Invoke(controller, "TryStageIndividualAssets", args);
            diagnostic = (StackMachineDiagnostic)args[2]; return success;
        }
        private static bool InvokeApplyStage(object controller, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { null }; bool success = (bool)Invoke(controller, "TryApplyStagedAssetsToCandidate", args); diagnostic = (StackMachineDiagnostic)args[0]; return success;
        }
        private static bool InvokeTransport(object controller, IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { executor, null }; bool success = (bool)Invoke(controller, "TryTransportVrmPhysics", args); diagnostic = (StackMachineDiagnostic)args[1]; return success;
        }
        private static bool InvokeVrmStage(object controller, IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { executor, "Assets", "VRM", "Look", null }; bool success = (bool)Invoke(controller, "TryStageVrmAssets", args); diagnostic = (StackMachineDiagnostic)args[4]; return success;
        }
        private static bool InvokeVrmFinalize(object controller, IHumanoidVrmTransportExecutor executor, GameObject prefab, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { executor, prefab, null }; bool success = (bool)Invoke(controller, "TryFinalizeVrmAssets", args); diagnostic = (StackMachineDiagnostic)args[2]; return success;
        }
        private static bool InvokeCommit(object controller, string folder, string documentName, IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { folder, documentName, executor, null }; bool success = (bool)Invoke(controller, "TryCommitPrefab", args); diagnostic = (StackMachineDiagnostic)args[3]; return success;
        }
        private static Func<GameObject, string, GameObject> GetPrefabSave() => (Func<GameObject, string, GameObject>)typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidPrefabCommitter", true).GetField("SavePrefabAsset", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private static void SetPrefabSave(Func<GameObject, string, GameObject> save) => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidPrefabCommitter", true).GetField("SavePrefabAsset", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, save);
        private static bool InvokeStrip(GameObject candidate, out StackMachineDiagnostic diagnostic)
        {
            MethodInfo method = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidPureHumanoidComponentStripper", true).GetMethod("TryStrip", BindingFlags.Static | BindingFlags.NonPublic);
            object[] args = { candidate, null }; bool result = (bool)method.Invoke(null, args); diagnostic = (StackMachineDiagnostic)args[1]; return result;
        }
        private static bool IsShapeSyncRuntimeBehaviour(MonoBehaviour behaviour)
        {
            string componentNamespace = behaviour?.GetType().Namespace;
            return !string.IsNullOrEmpty(componentNamespace) && (componentNamespace == "zgock.ShapeSync" || componentNamespace.StartsWith("zgock.ShapeSync.", StringComparison.Ordinal));
        }

        private static HumanoidMeshVrmTransportProvenance CreateCoreProvenance(GameObject outfit)
        {
            ConstructorInfo constructor = typeof(HumanoidMeshVrmTransportProvenance).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IReadOnlyList<HumanoidMeshSource>) }, null);
            return (HumanoidMeshVrmTransportProvenance)constructor.Invoke(new object[] { new List<HumanoidMeshSource> { new HumanoidMeshSource("dress", "outfit.dress", outfit, null, null, null) } });
        }

        private static object Invoke(object instance, string name, object[] arguments = null) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
        private static object GetProperty(object instance, string name) => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void SetProperty(object instance, string name, object value) => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static void SetField(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static int Count(System.Collections.IEnumerable values) { int count = 0; foreach (object ignored in values) count++; return count; }
        private static Mesh CreateMeshCloneRoundTripFixture()
        {
            var mesh = new Mesh { name = "Spec17_MeshCloneRoundTrip" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = new[]
            {
                new Vector3(0.1f, 0.2f, 0.3f), new Vector3(1.1f, 0.2f, 0.3f), new Vector3(0.1f, 1.2f, 0.3f), new Vector3(0.1f, 0.2f, 1.3f),
                new Vector3(1.1f, 1.2f, 0.3f), new Vector3(1.1f, 0.2f, 1.3f), new Vector3(0.1f, 1.2f, 1.3f), new Vector3(1.1f, 1.2f, 1.3f)
            };
            mesh.normals = Enumerable.Repeat(Vector3.up, mesh.vertexCount).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, -1f), mesh.vertexCount).ToArray();
            mesh.colors = new[]
            {
                new Color(0.12345f, 0.67891f, 0.22223f, 0.98765f), new Color(0.23456f, 0.78912f, 0.33334f, 0.87654f),
                new Color(0.34567f, 0.89123f, 0.44445f, 0.76543f), new Color(0.45678f, 0.91234f, 0.55556f, 0.65432f),
                new Color(0.56789f, 0.12345f, 0.66667f, 0.54321f), new Color(0.67891f, 0.23456f, 0.77778f, 0.43210f),
                new Color(0.78912f, 0.34567f, 0.88889f, 0.32109f), new Color(0.89123f, 0.45678f, 0.99999f, 0.21098f)
            };
            mesh.SetUVs(0, new[]
            {
                new Vector2(0.01f, 0.02f), new Vector2(0.11f, 0.12f), new Vector2(0.21f, 0.22f), new Vector2(0.31f, 0.32f),
                new Vector2(0.41f, 0.42f), new Vector2(0.51f, 0.52f), new Vector2(0.61f, 0.62f), new Vector2(0.71f, 0.72f)
            });
            mesh.SetUVs(1, new[]
            {
                new Vector3(1.01f, 1.02f, 1.03f), new Vector3(1.11f, 1.12f, 1.13f), new Vector3(1.21f, 1.22f, 1.23f), new Vector3(1.31f, 1.32f, 1.33f),
                new Vector3(1.41f, 1.42f, 1.43f), new Vector3(1.51f, 1.52f, 1.53f), new Vector3(1.61f, 1.62f, 1.63f), new Vector3(1.71f, 1.72f, 1.73f)
            });
            mesh.subMeshCount = 2;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false, 3);
            mesh.SetIndices(new[] { 0, 2, 3 }, MeshTopology.Triangles, 1, false, 4);
            mesh.bindposes = Enumerable.Repeat(Matrix4x4.identity, 6).ToArray();

            var bonesPerVertex = new NativeArray<byte>(new byte[] { 1, 3, 2, 1, 2, 1, 1, 1 }, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(new[]
            {
                new BoneWeight1 { boneIndex = 0, weight = 1f },
                new BoneWeight1 { boneIndex = 0, weight = 0.2f }, new BoneWeight1 { boneIndex = 1, weight = 0.3f }, new BoneWeight1 { boneIndex = 2, weight = 0.5f },
                new BoneWeight1 { boneIndex = 1, weight = 0.4f }, new BoneWeight1 { boneIndex = 3, weight = 0.6f },
                new BoneWeight1 { boneIndex = 2, weight = 1f },
                new BoneWeight1 { boneIndex = 4, weight = 0.25f }, new BoneWeight1 { boneIndex = 5, weight = 0.75f },
                new BoneWeight1 { boneIndex = 0, weight = 1f }, new BoneWeight1 { boneIndex = 5, weight = 1f }, new BoneWeight1 { boneIndex = 4, weight = 1f }
            }, Allocator.Temp);
            try { mesh.SetBoneWeights(bonesPerVertex, weights); }
            finally { bonesPerVertex.Dispose(); weights.Dispose(); }

            var shapeVertices = Enumerable.Repeat(new Vector3(0.01f, 0.02f, 0.03f), mesh.vertexCount).ToArray();
            var shapeNormals = Enumerable.Repeat(new Vector3(0.04f, 0.05f, 0.06f), mesh.vertexCount).ToArray();
            var shapeTangents = Enumerable.Repeat(new Vector3(0.07f, 0.08f, 0.09f), mesh.vertexCount).ToArray();
            mesh.AddBlendShapeFrame("RoundTripShape", 37f, shapeVertices, shapeNormals, shapeTangents);
            for (int i = 0; i < shapeVertices.Length; i++) { shapeVertices[i] *= 2f; shapeNormals[i] *= 2f; shapeTangents[i] *= 2f; }
            mesh.AddBlendShapeFrame("RoundTripShape", 100f, shapeVertices, shapeNormals, shapeTangents);
            mesh.bounds = new Bounds(new Vector3(2f, 3f, 4f), new Vector3(5f, 6f, 7f));
            return mesh;
        }

        private static void AssertVariableBoneWeightsEqual(Mesh expected, Mesh actual)
        {
            NativeArray<byte> expectedCounts = expected.GetBonesPerVertex();
            NativeArray<byte> actualCounts = actual.GetBonesPerVertex();
            NativeArray<BoneWeight1> expectedWeights = expected.GetAllBoneWeights();
            NativeArray<BoneWeight1> actualWeights = actual.GetAllBoneWeights();
            try
            {
                Assert.That(actualCounts.Length, Is.EqualTo(expectedCounts.Length));
                Assert.That(actualWeights.Length, Is.EqualTo(expectedWeights.Length));
                for (int i = 0; i < expectedCounts.Length; i++) Assert.That(actualCounts[i], Is.EqualTo(expectedCounts[i]));
                for (int i = 0; i < expectedWeights.Length; i++)
                {
                    Assert.That(actualWeights[i].boneIndex, Is.EqualTo(expectedWeights[i].boneIndex));
                    Assert.That(actualWeights[i].weight, Is.EqualTo(expectedWeights[i].weight).Within(0.0000001f));
                }
            }
            finally
            {
                expectedCounts.Dispose(); actualCounts.Dispose(); expectedWeights.Dispose(); actualWeights.Dispose();
            }
        }

        private static void AssertBlendShapesEqual(Mesh expected, Mesh actual)
        {
            Assert.That(actual.blendShapeCount, Is.EqualTo(expected.blendShapeCount));
            for (int shape = 0; shape < expected.blendShapeCount; shape++)
            {
                Assert.That(actual.GetBlendShapeName(shape), Is.EqualTo(expected.GetBlendShapeName(shape)));
                Assert.That(actual.GetBlendShapeFrameCount(shape), Is.EqualTo(expected.GetBlendShapeFrameCount(shape)));
                for (int frame = 0; frame < expected.GetBlendShapeFrameCount(shape); frame++)
                {
                    Assert.That(actual.GetBlendShapeFrameWeight(shape, frame), Is.EqualTo(expected.GetBlendShapeFrameWeight(shape, frame)).Within(0.0000001f));
                    var expectedVertices = new Vector3[expected.vertexCount]; var actualVertices = new Vector3[actual.vertexCount];
                    var expectedNormals = new Vector3[expected.vertexCount]; var actualNormals = new Vector3[actual.vertexCount];
                    var expectedTangents = new Vector3[expected.vertexCount]; var actualTangents = new Vector3[actual.vertexCount];
                    expected.GetBlendShapeFrameVertices(shape, frame, expectedVertices, expectedNormals, expectedTangents);
                    actual.GetBlendShapeFrameVertices(shape, frame, actualVertices, actualNormals, actualTangents);
                    Assert.That(actualVertices, Is.EqualTo(expectedVertices));
                    Assert.That(actualNormals, Is.EqualTo(expectedNormals));
                    Assert.That(actualTangents, Is.EqualTo(expectedTangents));
                }
            }
        }
        private static InMemoryHumanoidMesh CreateMesh(Material source, Material target, MaterialShaderAdapter adapter)
        {
            var mesh = new Mesh { subMeshCount = 1 }; var result = new InMemoryHumanoidMesh(mesh);
            Invoke(result, "TrySetMaterials", new object[] { new[] { target }, null }); Invoke(result, "TrySetMaterialSlots", new object[] { new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null }); return result;
        }
        private static InMemoryHumanoidMesh CreateResolvedMesh(Mesh mesh)
        {
            mesh.Clear();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            var root = new GameObject("Spec17_6_ResolvedHumanoid");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            Transform bone = new GameObject("FinalBone").transform;
            bone.SetParent(root.transform, false);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            return new InMemoryHumanoidMesh(root, mesh, null);
        }
        private static RenderTexture CreateTexture(Texture source) { var texture = new RenderTexture(new RenderTextureDescriptor(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0) { sRGB = false }); texture.Create(); Graphics.Blit(source, texture); return texture; }
        private static void Release(RenderTexture texture) { if (texture == null) return; if (RenderTexture.active == texture) RenderTexture.active = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        private static void SetWriter(Action<string, byte[]> writer) => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidIndividualAssetStager", true).GetField("WriteAllBytes", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, writer);

        private sealed class PendingBackend : IHumanoidBuildBackend
        {
            internal bool Cancelled { get; private set; }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public void Cancel() { Cancelled = true; }
        }

        private sealed class TrackingVrmExecutor : IHumanoidVrmTransportExecutor
        {
            internal GameObject Candidate; internal GameObject Figure; internal string[] LogicalNames; internal readonly TrackingDisposable Result = new TrackingDisposable(); internal IReadOnlyList<string> StagePaths = Array.Empty<string>(); internal GameObject FinalizedPrefab; internal bool StageSucceeds = true; internal bool FinalizeSucceeds = true; internal bool ThrowOnFinalize; internal bool AssetOwnershipReleased;
            public bool TryTransport(GameObject candidate, GameObject figureSourceRoot, ShapeSyncDocument document, HumanoidVrmTransportProvenance provenance, out IDisposable result, out StackMachineDiagnostic diagnostic)
            { Candidate = candidate; Figure = figureSourceRoot; LogicalNames = provenance.AttachedOutfitLogicalNames.ToArray(); result = Result; diagnostic = null; return true; }
            public bool TryStageAssets(IDisposable transportResult, string outputFolder, string relativeFolder, string documentName, out IReadOnlyList<string> assetPaths, out StackMachineDiagnostic diagnostic)
            { assetPaths = StagePaths; diagnostic = StageSucceeds ? null : StackMachineDiagnostic.CreateDomain("humanoid", "TestVrmStageRejected", "Injected VRM stage failure."); return StageSucceeds; }
            public bool TryFinalizeAssets(IDisposable transportResult, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic)
            { if (ThrowOnFinalize) throw new InvalidOperationException("Injected VRM finalize exception."); FinalizedPrefab = publishedPrefabRoot; diagnostic = FinalizeSucceeds ? null : StackMachineDiagnostic.CreateDomain("humanoid", "TestVrmFinalizeRejected", "Injected VRM finalize failure."); return FinalizeSucceeds; }
            public void ReleaseAssetOwnership(IDisposable transportResult) { AssetOwnershipReleased = true; }
        }
        private sealed class FailingVrmExecutor : IHumanoidVrmTransportExecutor
        {
            internal readonly TrackingDisposable Result = new TrackingDisposable();
            public bool TryTransport(GameObject candidate, GameObject figureSourceRoot, ShapeSyncDocument document, HumanoidVrmTransportProvenance provenance, out IDisposable result, out StackMachineDiagnostic diagnostic)
            { result = Result; diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "TestVrmTransportRejected", "Injected VRM transport failure."); return false; }
            public bool TryStageAssets(IDisposable transportResult, string outputFolder, string relativeFolder, string documentName, out IReadOnlyList<string> assetPaths, out StackMachineDiagnostic diagnostic)
            { assetPaths = Array.Empty<string>(); diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "TestVrmStageRejected", "Injected VRM stage failure."); return false; }
            public bool TryFinalizeAssets(IDisposable transportResult, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic)
            { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "TestVrmFinalizeRejected", "Injected VRM finalize failure."); return false; }
            public void ReleaseAssetOwnership(IDisposable transportResult) { }
        }
        private sealed class TrackingDisposable : IDisposable { internal bool Disposed; public void Dispose() { Disposed = true; } }

        private sealed class SuccessBackend : IHumanoidBuildBackend, IDisposable
        {
            private int meshPumps;
            private readonly Material source;
            private readonly UrpUnlitMaterialShaderAdapter adapter;
            private readonly HumanoidMeshVrmTransportProvenance provenance;
            internal Mesh ProducedMesh { get; private set; }
            internal SuccessBackend(HumanoidMeshVrmTransportProvenance provenance = null)
            {
                this.provenance = provenance;
                source = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = null;
                if (meshPumps++ == 0) { ProducedMesh = new Mesh(); payload = new MeshBuildPayload(CreateResolvedMesh(ProducedMesh), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, Array.Empty<HumanoidBuildSourceNormal>(), Array.Empty<HumanoidBuildComputedNormal>(), provenance); return HumanoidBuildPhaseStatus.Succeeded; }
                payload = null; return HumanoidBuildPhaseStatus.Failed;
            }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = new MaterialBuildPayload(Array.Empty<HumanoidMaterialSemanticPayload>()); diagnostic = null; return HumanoidBuildPhaseStatus.Succeeded; }
            public void Cancel() { }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        private sealed class MaterialTerminalBackend : IHumanoidBuildBackend, IDisposable
        {
            private readonly HumanoidBuildPhaseStatus status;
            private readonly Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            private readonly UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            private bool meshPumped;
            internal Mesh ProducedMesh { get; private set; }
            internal MaterialTerminalBackend(HumanoidBuildPhaseStatus status) { this.status = status; }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = null;
                if (meshPumped) { payload = null; return HumanoidBuildPhaseStatus.Failed; }
                meshPumped = true; ProducedMesh = new Mesh();
                payload = new MeshBuildPayload(CreateResolvedMesh(ProducedMesh), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, Array.Empty<HumanoidBuildSourceNormal>(), Array.Empty<HumanoidBuildComputedNormal>(), null);
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = status == HumanoidBuildPhaseStatus.Failed ? StackMachineDiagnostic.CreateDomain("humanoid", "TestMaterialFailure", "test") : null; return status; }
            public void Cancel() { }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        private sealed class TerminalBackend : IHumanoidBuildBackend
        {
            private readonly HumanoidBuildPhaseStatus status;
            internal TerminalBackend(HumanoidBuildPhaseStatus status) { this.status = status; }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = status == HumanoidBuildPhaseStatus.Failed ? StackMachineDiagnostic.CreateDomain("humanoid", "TestFailure", "test") : null; return status; }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return status; }
            public void Cancel() { }
        }
        private sealed class RejectingBackend : IHumanoidBuildBackend
        {
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "TestBeginRejected", "test"); return false; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Failed; }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return false; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Failed; }
            public void Cancel() { }
        }
    }
    internal static class CandidateApplyEnumerableExtensions
    {
        internal static Material[] CastMaterials(this System.Collections.IEnumerable values)
        {
            var result = new List<Material>(); foreach (object value in values) result.Add((Material)value); return result.ToArray();
        }
    }
}
