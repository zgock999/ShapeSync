// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class EditModeMaterialStackMachineTests
    {
        [UnityTest]
        public IEnumerator StartAndExplicitPump_HandsOffGpuTexturePayloadOnce()
        {
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(textureCompute, Is.Not.Null);

            using (var fixture = new Fixture())
            using (var textureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var machine = new EditModeMaterialStackMachine(textureMachine))
            {
                var document = new ShapeSyncDocument
                {
                    MaterialBinding = ScriptableObject.CreateInstance<MaterialBinding>(),
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 0.2 0.3 0.4 1 FILL $out COPY DROP ENDTEXTURE 0.6 0.7 0.8 1 COLOR 2 3 0.25 0.5 UVSET" }
                };
                try
                {
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int i = 0; i < 240 && machine.Status == EditModeMaterialExecutionStatus.Pending; i++)
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                        machine.Pump(out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(pumpDiagnostic, Is.Null);
                    }

                    Assert.That(machine.Status, Is.EqualTo(EditModeMaterialExecutionStatus.Succeeded), machine.Diagnostic?.message);
                    Assert.That(machine.TryTakeResult(out EditModeMaterialBuildResult result), Is.True);
                    using (result)
                    {
                        HumanoidMaterialBuildPayload<TextureCompletion>[] payloads = result.DetachPayloads();
                        try
                        {
                            Assert.That(payloads, Has.Length.EqualTo(1));
                            Assert.That(payloads[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
                            Assert.That(payloads[0].HasMainTex, Is.True);
                            Assert.That(payloads[0].MainTex, Is.Not.Null);
                            Assert.That(payloads[0].MainTex.Texture, Is.Not.Null);
                            Assert.That(payloads[0].HasColor, Is.True);
                            Assert.That(payloads[0].Color, Is.EqualTo(new Color(0.6f, 0.7f, 0.8f, 1f)));
                            Assert.That(payloads[0].HasUvSet, Is.True);
                            Assert.That(payloads[0].UvScale, Is.EqualTo(new Vector2(2f, 3f)));
                            Assert.That(payloads[0].UvOffset, Is.EqualTo(new Vector2(0.25f, 0.5f)));
                        }
                        finally
                        {
                            for (int i = 0; i < payloads.Length; i++) payloads[i].MainTex?.Dispose();
                        }
                    }
                    Assert.That(machine.TryTakeResult(out _), Is.False);
                }
                finally { Object.DestroyImmediate(document.MaterialBinding); }
            }
        }

        [Test]
        public void StartWithoutTextureMachine_RejectsTextureRecipeWithStructuredDiagnostic()
        {
            using (var fixture = new Fixture())
            using (var machine = new EditModeMaterialStackMachine(null))
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 0 0 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(machine.Pump(out diagnostic), Is.EqualTo(EditModeMaterialExecutionStatus.Failed));
                    Assert.That(diagnostic.domainCode, Is.EqualTo("TextureMachineRequired"));
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [UnityTest]
        public IEnumerator MaterialPayloadBuilder_TransfersTakenGpuCompletionOnceWithoutSourceMutation()
        {
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(textureCompute, Is.Not.Null);

            using (var fixture = new Fixture())
            using (var textureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var machine = new EditModeMaterialStackMachine(textureMachine))
            {
                Material sourceMaterial = fixture.Renderer.sharedMaterial;
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                var document = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 0.2 0.3 0.4 1 FILL $out COPY DROP ENDTEXTURE 0.6 0.7 0.8 1 COLOR 2 3 0.25 0.5 UVSET" }
                };
                try
                {
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    EditModeMaterialExecutionStatus status = machine.Status;
                    for (int i = 0; i < 240 && status == EditModeMaterialExecutionStatus.Pending; i++)
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                        status = machine.Pump(out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(pumpDiagnostic, Is.Null, pumpDiagnostic?.message);
                    }
                    Assert.That(status, Is.EqualTo(EditModeMaterialExecutionStatus.Succeeded), machine.Diagnostic?.message);
                    Assert.That(machine.TryTakeResult(out EditModeMaterialBuildResult result), Is.True);
                    RenderTexture completionTexture = null;
                    try
                    {
                        Assert.That(EditModeHumanoidMaterialPayloadBuilder.TryCreate(result, out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                        try
                        {
                            Assert.That(payload.Entries, Has.Count.EqualTo(1));
                            HumanoidMaterialSemanticPayload entry = payload.Entries[0];
                            Assert.That(entry.MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
                            Assert.That(entry.MainTexture, Is.Not.Null);
                            completionTexture = entry.MainTexture.Texture as RenderTexture;
                            Assert.That(completionTexture, Is.Not.Null);
                            Assert.That(entry.HasColor, Is.True);
                            Assert.That(entry.Color, Is.EqualTo(new Color(0.6f, 0.7f, 0.8f, 1f)));
                            Assert.That(entry.HasUvSet, Is.True);
                            Assert.That(entry.UvScale, Is.EqualTo(new Vector2(2f, 3f)));
                            Assert.That(entry.UvOffset, Is.EqualTo(new Vector2(0.25f, 0.5f)));
                            Assert.That(fixture.Renderer.sharedMaterial, Is.SameAs(sourceMaterial));
                        }
                        finally { payload.Dispose(); }
                    }
                    finally { result.Dispose(); }
                    Assert.That(completionTexture == null || !completionTexture.IsCreated(), Is.True);
                    Assert.That(machine.TryTakeResult(out _), Is.False);
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

 #if SHAPESYNC_RICH_TEST
        [UnityTest]
        public IEnumerator ActualSpec17Fixture_CompilerKeepsShirtAndSkirtMToonTexturesInTheirOwnFinalSlots()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument documentAsset = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(textureCompute, Is.Not.Null);
            Assert.That(figurePrefab, Is.Not.Null);
            Assert.That(documentAsset, Is.Not.Null);
            Assert.That(documentAsset.TryGetSnapshot(out ShapeSyncDocument document, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject figure = PrefabUtility.InstantiatePrefab(figurePrefab) as GameObject;
            Assert.That(figure, Is.Not.Null);
            using (var normalTextureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var materialTextureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var meshMachine = new EditModeMeshStackMachine(normalTextureMachine))
            using (var materialMachine = new EditModeMaterialStackMachine(materialTextureMachine))
            {
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                var compiler = new HumanoidCompiler();
                HumanoidBuildOperation operation = null;
                HumanoidBuildResult result = null;
                try
                {
                    Assert.That(compiler.TryCompile(new HumanoidBuildSource(figure, document), backend, out operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int i = 0; i < 3600 && result == null; i++)
                    {
                        HumanoidBuildOperationStatus status = operation.Pump(out result, out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Failed), pumpDiagnostic?.message);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Cancelled), pumpDiagnostic?.message);
                        if (result == null) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                    }

                    Assert.That(result, Is.Not.Null);
                    AssertFinalMToonBaseColor(result.Mesh, new MaterialId("shirt-1", "Body"));
                    AssertFinalMToonBaseColor(result.Mesh, new MaterialId("skirt-1", "Body"));

                    string stagingParent = ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17");
                    string stagingFolder = stagingParent + "/ActualSpec17FixtureStage_" + System.Guid.NewGuid().ToString("N");
                    Assert.That(AssetDatabase.CreateFolder(stagingParent, stagingFolder.Substring(stagingFolder.LastIndexOf('/') + 1)), Is.Not.Empty);
                    try
                    {
                        // The real Figure fixture can retain optional-package components. A
                        // core-only Editor must normalize the unpublished candidate before
                        // PrefabUtility serializes it, just as the publish workflow does.
                        Assert.That(HumanoidPureHumanoidComponentStripper.TryNormalize(result.Root, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
                        Assert.That(HumanoidIndividualAssetStager.TryStage(stagingFolder, "ShapeDocument_B", result, out HumanoidIndividualAssetStage stage, out _, out StackMachineDiagnostic stageDiagnostic), Is.True, stageDiagnostic?.message);
                        AssertStagedMToonBaseColor(stage, result.Mesh, new MaterialId("shirt-1", "Body"));
                        AssertStagedMToonBaseColor(stage, result.Mesh, new MaterialId("skirt-1", "Body"));
                        AssertPersistentMToonBaseColor(stage, result.Mesh, new MaterialId("shirt-1", "Body"));
                        AssertPersistentMToonBaseColor(stage, result.Mesh, new MaterialId("skirt-1", "Body"));
                        Animator resolvedAnimator = result.Root.GetComponentInChildren<Animator>(true);
                        Transform resolvedHips = resolvedAnimator != null ? resolvedAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
                        Assert.That(resolvedHips, Is.Not.Null);
                        Vector3 expectedHipsPosition = resolvedHips.localPosition;
                        Quaternion expectedHipsRotation = resolvedHips.localRotation;
                        Vector3 expectedHipsScale = resolvedHips.localScale;
                        Assert.That(HumanoidCandidateAssetApplier.TryApply(result.Root, stage, out StackMachineDiagnostic applyDiagnostic), Is.True, applyDiagnostic?.message);
                        Assert.That(resolvedHips.localPosition, Is.EqualTo(expectedHipsPosition), "17.6 Avatar assignment must preserve the 17.2-resolved Hips local position.");
                        Assert.That(resolvedHips.localRotation, Is.EqualTo(expectedHipsRotation), "17.6 Avatar assignment must preserve the 17.2-resolved Hips local rotation.");
                        Assert.That(resolvedHips.localScale, Is.EqualTo(expectedHipsScale), "17.6 Avatar assignment must preserve the 17.2-resolved Hips local scale.");
                        // The Editor window commits on its next update tick, rather than in the
                        // same call that staged the individual assets. Preserve that boundary so
                        // this regression observes asset-import/dirty-state ordering as UI does.
                        yield return null;
                        // VRM asset staging resolves its requested subfolder with this refresh
                        // before the Prefab commit. It must not discard unsaved staged Material
                        // texture references.
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        AssertStagedMToonBaseColor(stage, result.Mesh, new MaterialId("shirt-1", "Body"));
                        AssertStagedMToonBaseColor(stage, result.Mesh, new MaterialId("skirt-1", "Body"));
                        Assert.That(HumanoidPrefabCommitter.TryCommit(result.Root, stage, stagingFolder, "ShapeDocument_B", out _, out StackMachineDiagnostic commitDiagnostic), Is.True, commitDiagnostic?.message);
                        AssertPersistentMToonBaseColor(stage, result.Mesh, new MaterialId("shirt-1", "Body"));
                        AssertPersistentMToonBaseColor(stage, result.Mesh, new MaterialId("skirt-1", "Body"));
                    }
                    finally { AssetDatabase.DeleteAsset(stagingFolder); }
                }
                finally
                {
                    result?.Dispose();
                    operation?.Dispose();
                    Object.DestroyImmediate(figure);
                }
            }
        }
#endif

        [Test]
        public void MaterialPayloadBuilder_RejectsInvalidCarrierAndDisposesOwnedCompletion()
        {
            var texture = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf);
            texture.Create();
            int releases = 0;
            var completion = new TextureCompletion(texture, _ => releases++);
            var result = new EditModeMaterialBuildResult(new[]
            {
                new HumanoidMaterialBuildPayload<TextureCompletion>(default, completion, true, false, default, false, default, default)
            });
            try
            {
                Assert.That(EditModeHumanoidMaterialPayloadBuilder.TryCreate(result, out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(payload, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeMaterialPayloadInvalid"));
                Assert.That(releases, Is.EqualTo(1));
                result.Dispose();
                Assert.That(releases, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void AssertFinalMToonBaseColor(InMemoryHumanoidMesh mesh, MaterialId materialId)
        {
            Material material = null;
            for (int i = 0; i < mesh.MaterialSlots.Count; i++)
            {
                HumanoidBuildMaterialSlot slot = mesh.MaterialSlots[i];
                if (slot.MaterialId != materialId) continue;
                material = mesh.Materials[slot.SubmeshIndex];
                break;
            }

            Assert.That(material, Is.Not.Null, $"Final Mesh has no slot for {materialId}.");
            Assert.That(material.GetTexture("_MainTex"), Is.TypeOf<RenderTexture>(), $"{materialId} MainTex must remain an in-memory RenderTexture before publish.");
            Assert.That(material.GetTexture("_ShadeTex"), Is.TypeOf<RenderTexture>(), $"{materialId} ShadeTex must remain an in-memory RenderTexture before publish.");
        }

        private static void AssertPersistentMToonBaseColor(HumanoidIndividualAssetStage stage, InMemoryHumanoidMesh mesh, MaterialId materialId)
        {
            int submeshIndex = -1;
            for (int i = 0; i < mesh.MaterialSlots.Count; i++)
            {
                if (mesh.MaterialSlots[i].MaterialId != materialId) continue;
                submeshIndex = mesh.MaterialSlots[i].SubmeshIndex;
                break;
            }

            Assert.That(submeshIndex, Is.GreaterThanOrEqualTo(0), $"Final Mesh has no slot for {materialId}.");
            string materialPath = AssetDatabase.GetAssetPath(stage.Materials[submeshIndex]);
            AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceUpdate);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(material, Is.Not.Null, $"{materialId} staged Material must reload after AssetDatabase.SaveAssets.");
            Assert.That(material.GetTexture("_MainTex"), Is.TypeOf<Texture2D>(), $"{materialId} staged MainTex must reference its published PNG.");
            Assert.That(material.GetTexture("_ShadeTex"), Is.TypeOf<Texture2D>(), $"{materialId} staged ShadeTex must reference its published PNG.");
        }

        private static void AssertStagedMToonBaseColor(HumanoidIndividualAssetStage stage, InMemoryHumanoidMesh mesh, MaterialId materialId)
        {
            int submeshIndex = -1;
            for (int i = 0; i < mesh.MaterialSlots.Count; i++)
            {
                if (mesh.MaterialSlots[i].MaterialId != materialId) continue;
                submeshIndex = mesh.MaterialSlots[i].SubmeshIndex;
                break;
            }

            Assert.That(submeshIndex, Is.GreaterThanOrEqualTo(0), $"Final Mesh has no slot for {materialId}.");
            Material material = stage.Materials[submeshIndex];
            Assert.That(material, Is.Not.Null, $"{materialId} staged Material must exist.");
            Assert.That(material.GetTexture("_MainTex"), Is.TypeOf<Texture2D>(), $"{materialId} staged MainTex must reference its published PNG before Prefab commit.");
            Assert.That(material.GetTexture("_ShadeTex"), Is.TypeOf<Texture2D>(), $"{materialId} staged ShadeTex must reference its published PNG before Prefab commit.");
        }

        [Test]
        public void MaterialPayloadBuilder_RejectsMainTextureFlagWithoutCompletion()
        {
            var result = new EditModeMaterialBuildResult(new[]
            {
                new HumanoidMaterialBuildPayload<TextureCompletion>(new MaterialId(string.Empty, "body"), null, true, false, default, false, default, default)
            });
            try
            {
                Assert.That(EditModeHumanoidMaterialPayloadBuilder.TryCreate(result, out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(payload, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeMaterialPayloadInvalid"));
            }
            finally { result.Dispose(); }
        }

        [Test]
        public void MaterialPayloadBuilder_RejectsMissingResult()
        {
            Assert.That(EditModeHumanoidMaterialPayloadBuilder.TryCreate(null, out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(payload, Is.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeMaterialResultMissing"));
        }

        private sealed class Fixture : System.IDisposable
        {
            private readonly Material material;
            private readonly MaterialShaderAdapter adapter;
            internal Fixture()
            {
                Root = new GameObject("editmode-material-machine");
                var renderer = Root.AddComponent<SkinnedMeshRenderer>();
                Renderer = renderer;
                material = new Material(Shader.Find("Unlit/Color"));
                renderer.sharedMaterial = material;
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                MaterialProxy proxy = Root.AddComponent<MaterialProxy>();
                typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter } });
            }

            internal GameObject Root { get; }
            internal SkinnedMeshRenderer Renderer { get; private set; }
            public void Dispose() { Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(Root); }
        }
    }
}
