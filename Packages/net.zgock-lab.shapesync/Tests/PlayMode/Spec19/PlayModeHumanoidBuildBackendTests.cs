// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.StackMachine.Tests.Spec17;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode.Spec19
{
    public sealed class PlayModeHumanoidBuildBackendTests
    {
        [Test]
        public void Backend_DisposeRejectsNewMeshPhase()
        {
            var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
            backend.Dispose();
            Assert.That(backend.TryBeginMeshPhase(default, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("PlayModeHumanoidBackendDisposed"));
            backend.Dispose();
        }

        [UnityTest]
        public IEnumerator Compiler_TransfersOneResolvedCarrier_AndDoesNotMutateSource()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                Mesh sourceMesh = fixture.Renderer.sharedMesh;
                Material sourceMaterial = fixture.Renderer.sharedMaterial;
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 20; frame++)
                {
                    operation.Pump(out result, out _);
                    yield return null;
                }
                Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), operation.Diagnostic?.message);
                using (result)
                {
                    Assert.That(result, Is.Not.Null);
                    Assert.That(result.Root, Is.Not.Null);
                    Assert.That(result.Mesh, Is.Not.Null);
                    Assert.That(result.Mesh.Mesh, Is.Not.Null);
                    Assert.That(result.Mesh.Avatar, Is.Not.Null);
                    Assert.That(result.Mesh.Avatar.isHuman, Is.True);
                    SkinnedMeshRenderer[] renderers = result.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.That(renderers, Has.Length.EqualTo(1));
                    Assert.That(renderers[0].sharedMesh, Is.SameAs(result.Mesh.Mesh));
                    Assert.That(renderers[0].sharedMesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount));
                    Assert.That(renderers[0].sharedMesh.subMeshCount, Is.EqualTo(result.Mesh.Materials.Count));
                    Assert.That(renderers[0].rootBone, Is.Not.Null);
                    Assert.That(renderers[0].bones, Has.Length.EqualTo(result.Mesh.Mesh.bindposeCount));
                    Assert.That(result.Mesh.Materials, Has.Count.EqualTo(1));
                    Assert.That(result.Mesh.Materials[0], Is.Not.SameAs(sourceMaterial));
                    Assert.That(fixture.TryCreateSourceStructureExpectation(out HumanoidMeshStructureFixture expectation, out string expectationFailure), Is.True, expectationFailure);
                    Transform[] expectedResultBones = ResolveResultBones(fixture.Figure.transform, result.Root.transform, fixture.Renderer.bones);
                    Assert.That(HumanoidMeshStructureExpectation.TryValidate(result.Mesh.Mesh, renderers[0], result.Mesh.Avatar, expectation.VertexCount, expectation.MaterialSlotCount, expectedResultBones, expectation.Bindposes, expectation.FinalBlendShapeNames, expectation.HumanBoneNames, out string structureFailure), Is.True, structureFailure);
                }
                Assert.That(fixture.Renderer.sharedMesh, Is.SameAs(sourceMesh));
                Assert.That(fixture.Renderer.sharedMaterial, Is.SameAs(sourceMaterial));
                backend.Cancel();
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SharedStructureOracle_RejectsCorruptedVertexAndBoneExpectations()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 20; frame++) { operation.Pump(out result, out _); yield return null; }
                try
                {
                    Assert.That(fixture.TryCreateSourceStructureExpectation(out HumanoidMeshStructureFixture expectation, out string expectationFailure), Is.True, expectationFailure);
                    SkinnedMeshRenderer renderer = result.Root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    Transform[] bones = ResolveResultBones(fixture.Figure.transform, result.Root.transform, fixture.Renderer.bones);
                    Assert.That(HumanoidMeshStructureExpectation.TryValidate(result.Mesh.Mesh, renderer, result.Mesh.Avatar, expectation.VertexCount + 1, expectation.MaterialSlotCount, bones, expectation.Bindposes, expectation.FinalBlendShapeNames, expectation.HumanBoneNames, out string vertexFailure), Is.False);
                    Assert.That(vertexFailure, Is.EqualTo("MeshOrMaterialSlotMismatch"));
                    Transform[] wrongBones = (Transform[])bones.Clone(); wrongBones[0] = null;
                    Assert.That(HumanoidMeshStructureExpectation.TryValidate(result.Mesh.Mesh, renderer, result.Mesh.Avatar, expectation.VertexCount, expectation.MaterialSlotCount, wrongBones, expectation.Bindposes, expectation.FinalBlendShapeNames, expectation.HumanBoneNames, out string boneFailure), Is.False);
                    Assert.That(boneFailure, Is.EqualTo("BoneTableMismatch"));
                }
                finally { result?.Dispose(); operation.Dispose(); backend.Dispose(); }
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_AttachedOutfit_MatchesSourceDerivedStructureOracle()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                fixture.AddAttachedOutfit();
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 30; frame++) { operation.Pump(out result, out _); yield return null; }
                try
                {
                    Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), operation.Diagnostic?.message);
                    Assert.That(fixture.TryCreateSourceStructureExpectation(out HumanoidMeshStructureFixture expectation, out string expectationFailure), Is.True, expectationFailure);
                    SkinnedMeshRenderer renderer = result.Root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    Transform[] bones = ResolveResultBones(fixture.Figure.transform, result.Root.transform, fixture.Renderer.bones);
                    Assert.That(HumanoidMeshStructureExpectation.TryValidate(result.Mesh.Mesh, renderer, result.Mesh.Avatar, expectation.VertexCount, expectation.MaterialSlotCount, bones, expectation.Bindposes, expectation.FinalBlendShapeNames, expectation.HumanBoneNames, out string structureFailure), Is.True, structureFailure);
                }
                finally { result?.Dispose(); operation.Dispose(); backend.Dispose(); }
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_CancelDuringCarrierCleanup_ReleasesBackendForNextBuild()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation cancelled, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                Assert.That(cancelled.Pump(out _, out StackMachineDiagnostic firstPumpDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending), firstPumpDiagnostic?.message);
                cancelled.Cancel();
                Assert.That(cancelled.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                yield return null;

                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation retry, out StackMachineDiagnostic retryDiagnostic), Is.True, retryDiagnostic?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; retry.Status == HumanoidBuildOperationStatus.Pending && frame < 20; frame++)
                {
                    retry.Pump(out result, out _);
                    yield return null;
                }
                Assert.That(retry.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), retry.Diagnostic?.message);
                result?.Dispose();
                cancelled.Dispose(); retry.Dispose(); backend.Cancel();
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_SourceMaterialMissing_RejectsStartAndLeavesBackendReusable()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                Material sourceMaterial = fixture.Renderer.sharedMaterial;
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                fixture.Renderer.sharedMaterial = null;
                Assert.That(compiler.TryCompile(fixture.Source, backend, out _, out StackMachineDiagnostic startDiagnostic), Is.False);
                Assert.That(startDiagnostic.domainCode, Is.EqualTo("MaterialProxySourceMaterialMissing"));
                fixture.Renderer.sharedMaterial = sourceMaterial;

                Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation retry, out StackMachineDiagnostic retryDiagnostic), Is.True, retryDiagnostic?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; retry.Status == HumanoidBuildOperationStatus.Pending && frame < 20; frame++)
                {
                    retry.Pump(out result, out _);
                    yield return null;
                }
                Assert.That(retry.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), retry.Diagnostic?.message);
                result?.Dispose(); retry.Dispose(); backend.Cancel();
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_MaterialTextureHostMissing_PropagatesStructuredFailure()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                fixture.Source.Document.MaterialBinding = binding;
                fixture.Source.Document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE" };
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                try
                {
                    Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 20; frame++)
                    {
                        operation.Pump(out _, out _);
                        yield return null;
                    }
                    Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                    Assert.That(operation.Diagnostic.domainCode, Is.EqualTo("HostRequired"));
                    operation.Dispose();
                }
                finally { backend.Dispose(); Object.Destroy(binding); }
            }
#else
            Assert.Ignore("AvatarBuilder fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_MaterialTextureHost_TransfersOwnedTextureToResult()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture())
            {
                GameObject hostRoot = CreateTextureHost(out TextureStackMachineHost host);
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                fixture.Source.Document.MaterialBinding = binding;
                fixture.Source.Document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 0.2 0.3 0.4 1 FILL $out COPY DROP ENDTEXTURE" };
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(null), new PlayModeHumanoidMaterialStackMachine(host));
                var compiler = new HumanoidCompiler();
                HumanoidBuildResult result = null;
                try
                {
                    Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 120; frame++) { operation.Pump(out result, out _); yield return null; }
                    Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), operation.Diagnostic?.message);
                    Assert.That(result.Mesh.Materials[0].GetTexture("_BaseMap"), Is.Not.Null);
                }
                finally { result?.Dispose(); backend.Dispose(); Object.Destroy(binding); Object.Destroy(hostRoot); }
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compiler_NormalHost_TransfersComputedNormalToResult()
        {
#if UNITY_EDITOR
            using (var fixture = new BackendFixture("face"))
            {
                GameObject hostRoot = CreateTextureHost(out TextureStackMachineHost host);
                fixture.Source.Document.MeshRecipe = new MeshRecipeDocument { wordSource = "$face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL" };
                fixture.ConfigureFigureNormal("face", "FBM_Body");
                var backend = new PlayModeHumanoidBuildBackend(new PlayModeHumanoidMeshStackMachine(host), new PlayModeHumanoidMaterialStackMachine(null));
                var compiler = new HumanoidCompiler();
                HumanoidBuildResult result = null;
                try
                {
                    Assert.That(compiler.TryCompile(fixture.Source, backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int frame = 0; operation.Status == HumanoidBuildOperationStatus.Pending && frame < 120; frame++) { operation.Pump(out result, out _); yield return null; }
                    Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), operation.Diagnostic?.message);
                    Assert.That(result.Mesh.Materials, Has.Count.EqualTo(1));
                    Assert.That(result.Mesh.Materials[0], Is.Not.Null);
                }
                finally { result?.Dispose(); backend.Dispose(); Object.Destroy(hostRoot); }
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Driver_SuccessTransfersProvenanceAndRetainsOperationUntilDispose()
        {
#if UNITY_EDITOR
            using (var fixture = new DriverInputFixture())
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = null;
                for (int frame = 0; driver.Operation != null && driver.Operation.Status == HumanoidBuildOperationStatus.Pending && frame < 40; frame++)
                {
                    driver.Pump(out result, out _);
                    yield return null;
                }

                try
                {
                    Assert.That(driver.Operation, Is.Not.Null);
                    Assert.That(driver.Operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), driver.Operation.Diagnostic?.message);
                    Assert.That(result, Is.Not.Null);
                    Assert.That(driver.TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic provenanceDiagnostic), Is.True, provenanceDiagnostic?.message);
                    provenance.Dispose();
                    Assert.That(driver.TryTakeVrmTransportProvenance(out _, out StackMachineDiagnostic repeatDiagnostic), Is.False);
                    Assert.That(repeatDiagnostic.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                }
                finally { result?.Dispose(); }

                driver.Dispose();
                Assert.That(driver.Operation, Is.Null);
            }
#else
            Assert.Ignore("Prefab authoring fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Driver_NormalHostDestroyedDuringMeshPhase_CancelsOwnedOperation()
        {
#if UNITY_EDITOR
            using (var fixture = new DriverInputFixture())
            {
                GameObject hostRoot = new GameObject("Spec19_4_DestroyedNormalHost");
                var host = hostRoot.AddComponent<TextureStackMachineHost>();
                using (var driver = new HotBakeBuildDriver(host, null))
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    Object.Destroy(hostRoot);
                    yield return null;
                    Assert.That(driver.Pump(out _, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeTextureHostDestroyed"));
                    Assert.That(driver.Operation, Is.Null);
                }
            }
#else
            Assert.Ignore("Prefab authoring fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Driver_MaterialHostDestroyedDuringActiveTextureSemantic_CancelsOwnedOperation()
        {
#if UNITY_EDITOR
            using (var fixture = new DriverInputFixture())
            {
                fixture.ConfigureMaterialTexture();
                GameObject hostRoot = CreateTextureHost(out TextureStackMachineHost host);
                using (var driver = new HotBakeBuildDriver(null, host))
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    for (int frame = 0; driver.Operation.ProgressPhase != HumanoidBuildProgressPhase.Material && frame < 40; frame++)
                    {
                        Assert.That(driver.Pump(out _, out StackMachineDiagnostic meshDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending), meshDiagnostic?.message);
                        yield return null;
                    }
                    Assert.That(driver.Operation.ProgressPhase, Is.EqualTo(HumanoidBuildProgressPhase.Material));
                    Assert.That(driver.Pump(out _, out StackMachineDiagnostic materialDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending), materialDiagnostic?.message);
                    Object.Destroy(hostRoot);
                    yield return null;
                    Assert.That(driver.Pump(out _, out StackMachineDiagnostic cancelledDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                    Assert.That(cancelledDiagnostic.domainCode, Is.EqualTo("HotBakeTextureHostDestroyed"));
                    Assert.That(driver.Operation, Is.Null);
                }
            }
#else
            Assert.Ignore("Prefab authoring fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Driver_MaterialFailureAfterMeshCompletion_ReleasesUnhandedProvenance()
        {
#if UNITY_EDITOR
            using (var fixture = new DriverInputFixture())
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                fixture.ConfigureMaterialTexture();
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
                StackMachineDiagnostic terminalDiagnostic = null;
                for (int frame = 0; status == HumanoidBuildOperationStatus.Pending && frame < 40; frame++)
                {
                    status = driver.Pump(out _, out terminalDiagnostic);
                    yield return null;
                }

                Assert.That(status, Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(terminalDiagnostic.domainCode, Is.EqualTo("HostRequired"));
                Assert.That(driver.Operation, Is.Null, "Terminal failure must dispose the operation that owns unhanded Mesh provenance.");
                Assert.That(driver.TryTakeVrmTransportProvenance(out _, out StackMachineDiagnostic provenanceDiagnostic), Is.False);
                Assert.That(provenanceDiagnostic.domainCode, Is.EqualTo("HotBakeOperationRequired"));
            }
#else
            Assert.Ignore("Prefab authoring fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Driver_CancelReleasesPendingOperation()
        {
#if UNITY_EDITOR
            using (var fixture = new DriverInputFixture())
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                Assert.That(driver.Pump(out _, out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending), pumpDiagnostic?.message);
                driver.Cancel();
                yield return null;
                Assert.That(driver.Operation, Is.Null);
            }
#else
            Assert.Ignore("Prefab authoring fixture is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static GameObject CreateTextureHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            ComputeShader normal = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            var root = new GameObject("Spec19_4_TextureHost");
            host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assign), Is.True, assign?.message);
            Assert.That(host.TryAssignNormalComputeProgram(normal, out StackMachineDiagnostic normalAssign), Is.True, normalAssign?.message);
            Assert.That(host.TryInitialize(out StackMachineDiagnostic initialize), Is.True, initialize?.message);
            return root;
        }

        private static Transform[] ResolveResultBones(Transform sourceRoot, Transform resultRoot, Transform[] sourceBones)
        {
            var values = new Transform[sourceBones.Length];
            for (int i = 0; i < values.Length; i++) values[i] = resultRoot.Find(GetRelativePath(sourceRoot, sourceBones[i]));
            return values;
        }

        private static string GetRelativePath(Transform root, Transform value)
        {
            var names = new List<string>();
            for (Transform current = value; current != null && current != root; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }


        private sealed class BackendFixture : System.IDisposable
        {
            private readonly List<Object> objects = new List<Object>();
            internal readonly GameObject Figure;
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly HumanoidBuildSource Source;

            internal BackendFixture(string entryName = "body")
            {
                Figure = new GameObject("Spec19_4_BackendFigure");
                objects.Add(Figure);
                Avatar avatar = CreateAvatar(Figure);
                objects.Add(avatar);
                Animator animator = Figure.AddComponent<Animator>();
                animator.avatar = avatar;
                Renderer = Figure.AddComponent<SkinnedMeshRenderer>();
                Mesh mesh = CreateMesh(); objects.Add(mesh);
                Renderer.sharedMesh = mesh;
                Renderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                Renderer.bones = new[] { animator.GetBoneTransform(HumanBodyBones.Hips) };
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                Material material = new Material(shader); material.SetColor("_BaseColor", Color.white); objects.Add(material);
                Renderer.sharedMaterial = material;
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); objects.Add(adapter);
                ConfigureProxy(Figure.AddComponent<MaterialProxy>(), Renderer, material, adapter, entryName);
                MeshBinding meshBinding = ScriptableObject.CreateInstance<MeshBinding>(); objects.Add(meshBinding);
                Source = new HumanoidBuildSource(Figure, new ShapeSyncDocument
                {
                    MeshBinding = meshBinding,
                    MeshRecipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" },
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$" + entryName + " MATERIAL 0.2 0.3 0.4 1 COLOR" }
                });
            }

            internal void ConfigureFigureNormal(string entryName, string targetName)
            {
                var baseTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                var targetTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                objects.Add(baseTexture); objects.Add(targetTexture);
                var binding = new SerializedObject(Source.Document.MeshBinding);
                SerializedProperty owners = binding.FindProperty("normalOwners"); owners.arraySize = 1;
                SerializedProperty owner = owners.GetArrayElementAtIndex(0); owner.FindPropertyRelative("outfitRegistryId").stringValue = string.Empty;
                SerializedProperty targets = owner.FindPropertyRelative("targets"); targets.arraySize = 2;
                SetNormalTarget(targets.GetArrayElementAtIndex(0), string.Empty, entryName, baseTexture);
                SetNormalTarget(targets.GetArrayElementAtIndex(1), targetName, entryName, targetTexture);
                binding.ApplyModifiedPropertiesWithoutUndo();
                NormalBlender blender = Figure.AddComponent<NormalBlender>();
                var blenderSerialized = new SerializedObject(blender); SerializedProperty entries = blenderSerialized.FindProperty("entries"); entries.arraySize = 1; entries.GetArrayElementAtIndex(0).stringValue = entryName; blenderSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            internal void AddAttachedOutfit()
            {
                var root = new GameObject("Spec19_4_OutfitDress"); objects.Add(root);
                var outfit = root.AddComponent<ShapeSyncOutfit>();
                var serializedOutfit = new SerializedObject(outfit); serializedOutfit.FindProperty("registryId").stringValue = "outfit.dress";
                var rendererRoot = new GameObject("renderer"); rendererRoot.transform.SetParent(root.transform, false); objects.Add(rendererRoot);
                var rootBone = new GameObject("rootBone"); rootBone.transform.SetParent(root.transform, false); objects.Add(rootBone);
                // A normal ATTACH outfit may skin directly to a Figure humanoid bone.  Its
                // rootBone remains local to satisfy the source renderer topology contract;
                // only the weighted bone needs to resolve into the final Figure table.
                var renderer = rendererRoot.AddComponent<SkinnedMeshRenderer>(); Mesh mesh = CreateMesh(); objects.Add(mesh); renderer.sharedMesh = mesh; renderer.rootBone = rootBone.transform; renderer.bones = new[] { Renderer.bones[0] };
                var profile = ScriptableObject.CreateInstance<OutfitSkinningProfile>(); objects.Add(profile); profile.SetRendererProfiles(new List<OutfitSkinningRendererProfile> { new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = mesh.bindposes } });
                serializedOutfit.FindProperty("skinningProfile").objectReferenceValue = profile; serializedOutfit.ApplyModifiedPropertiesWithoutUndo();
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); var material = new Material(shader); objects.Add(material); var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); objects.Add(adapter); ConfigureProxy(root.AddComponent<MaterialProxy>(), renderer, material, adapter, "dress");
                var binding = new SerializedObject(Source.Document.MeshBinding); SerializedProperty outfits = binding.FindProperty("outfits"); outfits.arraySize = 1; SerializedProperty entry = outfits.GetArrayElementAtIndex(0); entry.FindPropertyRelative("logicalName").stringValue = "dress"; entry.FindPropertyRelative("outfitPrefab").objectReferenceValue = root; binding.ApplyModifiedPropertiesWithoutUndo();
                Source.Document.MeshRecipe = new MeshRecipeDocument { wordSource = "$dress ATTACH" };
            }

            internal bool TryCreateSourceStructureExpectation(out HumanoidMeshStructureFixture expectation, out string failure)
            {
                expectation = null; failure = null;
                if (!HumanoidMeshLogicalCollector.TryCreate(Figure, Source.Document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)) { failure = diagnostic?.domainCode; return false; }
                if (!HumanoidMeshFbmBaker.TryBake(plan, out HumanoidMeshFbmBakeResult bake, out diagnostic)) { failure = diagnostic?.domainCode; return false; }
                using (bake)
                {
                    if (!HumanoidMeshBcpResolver.TryResolve(bake, out var bcp, out diagnostic)) { failure = diagnostic?.domainCode; return false; }
                    bake.SetBcpDeltas(bcp);
                    if (!HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow skeleton, out diagnostic)) { failure = diagnostic?.domainCode; return false; }
                    bake.SetSkeleton(skeleton);
                    if (!skeleton.TryAssignRebuiltAvatar(out diagnostic) || !HumanoidMeshBoneTable.TryCreate(bake, plan.Figure, skeleton, out HumanoidMeshBoneTable table, out diagnostic)) { failure = diagnostic?.domainCode; return false; }
                    bake.SetBoneTable(table);
                    skeleton.ResetRootTransform();
                    if (!HumanoidMeshFinalMeshBuilder.TryBuild(bake, out diagnostic) || !HumanoidMeshMaterialSlotBuilder.TryCreate(bake, out HumanoidMeshMaterialSlot[] slots, out diagnostic)) { failure = diagnostic?.domainCode; return false; }
                    bake.SetMaterialSlots(slots);
                    if (!HumanoidMeshStructureFixture.TryCreate(bake, out expectation)) { failure = "StructureFixtureCreateFailed"; return false; }
                    return true;
                }
            }

            public void Dispose()
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }

            private static void ConfigureProxy(MaterialProxy proxy, SkinnedMeshRenderer renderer, Material material, MaterialShaderAdapter adapter, string entryName)
            {
                var serialized = new SerializedObject(proxy);
                SerializedProperty entries = serialized.FindProperty("entries");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("entryName").stringValue = entryName;
                entry.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                entry.FindPropertyRelative("materialChannel").intValue = 0;
                entry.FindPropertyRelative("adapter").objectReferenceValue = adapter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.sharedMaterial = material;
            }

            private static void SetNormalTarget(SerializedProperty target, string targetName, string entryName, Texture2D texture)
            {
                target.FindPropertyRelative("targetName").stringValue = targetName;
                SerializedProperty textures = target.FindPropertyRelative("textures"); textures.arraySize = 1;
                SerializedProperty item = textures.GetArrayElementAtIndex(0); item.FindPropertyRelative("entryName").stringValue = entryName; item.FindPropertyRelative("normalTexture").objectReferenceValue = texture;
            }

            internal static Mesh CreateMesh()
            {
                var mesh = new Mesh { name = "Spec19_4_SourceMesh" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
                return mesh;
            }

            internal static Avatar CreateAvatar(GameObject root)
            {
                var bones = new List<Transform>();
                Transform hips = Add(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
                Transform spine = Add(hips, "Spine", Vector3.up * .15f, bones);
                Transform chest = Add(spine, "Chest", Vector3.up * .15f, bones);
                Transform neck = Add(chest, "Neck", Vector3.up * .15f, bones);
                Add(neck, "Head", Vector3.up * .12f, bones);
                Transform leftUpperArm = Add(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones);
                Transform leftLowerArm = Add(leftUpperArm, "LeftLowerArm", Vector3.left * .2f, bones); Add(leftLowerArm, "LeftHand", Vector3.left * .18f, bones);
                Transform rightUpperArm = Add(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones);
                Transform rightLowerArm = Add(rightUpperArm, "RightLowerArm", Vector3.right * .2f, bones); Add(rightLowerArm, "RightHand", Vector3.right * .18f, bones);
                Transform leftUpperLeg = Add(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones);
                Transform leftLowerLeg = Add(leftUpperLeg, "LeftLowerLeg", Vector3.down * .35f, bones); Add(leftLowerLeg, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
                Transform rightUpperLeg = Add(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones);
                Transform rightLowerLeg = Add(rightUpperLeg, "RightLowerLeg", Vector3.down * .35f, bones); Add(rightLowerLeg, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
                string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                var human = new HumanBone[names.Length];
                for (int i = 0; i < names.Length; i++) human[i] = new HumanBone { boneName = names[i], humanName = names[i], limit = new HumanLimit { useDefaultValues = true } };
                var skeleton = new List<SkeletonBone> { ToSkeleton(root.transform) };
                for (int i = 0; i < bones.Count; i++) skeleton.Add(ToSkeleton(bones[i]));
                return AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
            }

            private static Transform Add(Transform parent, string name, Vector3 position, List<Transform> all) { Transform value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = position; all.Add(value); return value; }
            private static SkeletonBone ToSkeleton(Transform value) => new SkeletonBone { name = value.name, position = value.localPosition, rotation = value.localRotation, scale = value.localScale };
        }

        internal sealed class DriverInputFixture : IDisposable
        {
            private const string Root = ShapeSyncTestAssetPaths.Spec19HotBakeDriverPlayModeRoot;
            private readonly GameObject authoring;

            internal DriverInputFixture()
            {
                AssetDatabase.DeleteAsset(Root);
                ShapeSyncTestAssetPaths.ConsumerFolderPath("__Spec19_4_HotBakeDriverPlayModeTests");
                Mesh mesh = BackendFixture.CreateMesh();
                AssetDatabase.CreateAsset(mesh, Root + "/Source.asset");
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                var material = new Material(shader) { name = "SourceMaterial" };
                AssetDatabase.CreateAsset(material, Root + "/Material.mat");
                var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                AssetDatabase.CreateAsset(adapter, Root + "/Adapter.asset");
                authoring = new GameObject("Spec19_4_DriverPrefabFigure");
                Avatar avatar = BackendFixture.CreateAvatar(authoring);
                AssetDatabase.CreateAsset(avatar, Root + "/Avatar.asset");
                var animator = authoring.AddComponent<Animator>(); animator.avatar = avatar;
                var renderer = authoring.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.sharedMaterial = material;
                renderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                renderer.bones = new[] { renderer.rootBone };
                ConfigureProxy(authoring.AddComponent<MaterialProxy>(), renderer, adapter);
                Prefab = PrefabUtility.SaveAsPrefabAsset(authoring, Root + "/Figure.prefab");
                var meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                AssetDatabase.CreateAsset(meshBinding, Root + "/MeshBinding.asset");
                Document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
                Document.MeshBinding = meshBinding;
                Document.MeshRecipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" };
                Document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 0.2 0.3 0.4 1 COLOR" };
                AssetDatabase.CreateAsset(Document, Root + "/Document.asset");
                AssetDatabase.SaveAssets();
            }

            internal GameObject Prefab { get; }
            internal ShapeSyncDocumentAsset Document { get; }

            internal void ConfigureMaterialTexture()
            {
                var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false, false);
                texture.Apply();
                AssetDatabase.CreateAsset(texture, Root + "/Texture.asset");
                var binding = ScriptableObject.CreateInstance<MaterialBinding>();
                var serialized = new SerializedObject(binding);
                SerializedProperty entries = serialized.FindProperty("textures"); entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("logicalName").stringValue = "source";
                entry.FindPropertyRelative("sourceTexture").objectReferenceValue = texture;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(binding, Root + "/MaterialBinding.asset");
                Document.MaterialBinding = binding;
                Document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE $source CANVAS . ENDTEXTURE" };
                EditorUtility.SetDirty(Document);
                AssetDatabase.SaveAssets();
            }

            public void Dispose()
            {
                if (authoring != null) Object.DestroyImmediate(authoring);
                AssetDatabase.DeleteAsset(Root);
            }

            private static void ConfigureProxy(MaterialProxy proxy, SkinnedMeshRenderer renderer, MaterialShaderAdapter adapter)
            {
                var serialized = new SerializedObject(proxy);
                SerializedProperty entries = serialized.FindProperty("entries"); entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("entryName").stringValue = "body";
                entry.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                entry.FindPropertyRelative("materialChannel").intValue = 0;
                entry.FindPropertyRelative("adapter").objectReferenceValue = adapter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
#endif
    }
}
