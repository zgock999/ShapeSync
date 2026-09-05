// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.StackMachine.Tests.Spec17;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class EditModeMeshLogicalCollectorTests
    {
        [Test]
        public void TryCreate_CollectsAttachPcmAndBcpWithoutMutatingSources()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit oldOutfit = fixture.CreateOutfit("old", "outfit.old", false, false);
                ShapeSyncOutfit dress = fixture.CreateOutfit("dress", "outfit.dress", true, true);
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET $old DETACH DETACH_ALL $dress ATTACH", oldOutfit, dress);
                SkinnedMeshRenderer figureRenderer = fixture.FigureRenderer;
                SkinnedMeshRenderer dressRenderer = dress.GetComponentInChildren<SkinnedMeshRenderer>();
                Mesh figureMesh = figureRenderer.sharedMesh;
                Mesh dressMesh = dressRenderer.sharedMesh;
                Material figureMaterial = figureRenderer.sharedMaterial;
                Material dressMaterial = dressRenderer.sharedMaterial;
                Transform oldParent = oldOutfit.transform.parent;
                Transform dressParent = dress.transform.parent;
                MaterialProxy figureProxy = fixture.Figure.GetComponent<MaterialProxy>();
                MaterialProxy dressProxy = dress.GetComponent<MaterialProxy>();
                MaterialProxyEntry figureEntry = figureProxy.Entries[0];
                MaterialProxyEntry dressEntry = dressProxy.Entries[0];
                string figureEntryName = figureEntry.entryName;
                string dressEntryName = dressEntry.entryName;
                int figureEntryChannel = figureEntry.materialChannel;
                int dressEntryChannel = dressEntry.materialChannel;
                MaterialShaderAdapter figureEntryAdapter = figureEntry.adapter;
                MaterialShaderAdapter dressEntryAdapter = dressEntry.adapter;

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.AttachedOutfits, Has.Count.EqualTo(1));
                Assert.That(plan.AttachedOutfits[0].RegistryId, Is.EqualTo("outfit.dress"));
                Assert.That(plan.PcmSources, Has.Count.EqualTo(1));
                Assert.That(plan.BcpSources, Has.Count.EqualTo(1));
                Assert.That(oldOutfit.gameObject.activeSelf, Is.True);
                Assert.That(dress.gameObject.activeSelf, Is.True);
                Assert.That(figureRenderer.sharedMesh, Is.SameAs(figureMesh));
                Assert.That(dressRenderer.sharedMesh, Is.SameAs(dressMesh));
                Assert.That(figureRenderer.sharedMaterial, Is.SameAs(figureMaterial));
                Assert.That(dressRenderer.sharedMaterial, Is.SameAs(dressMaterial));
                Assert.That(oldOutfit.transform.parent, Is.SameAs(oldParent));
                Assert.That(dress.transform.parent, Is.SameAs(dressParent));
                Assert.That(figureProxy.Entries[0], Is.SameAs(figureEntry));
                Assert.That(dressProxy.Entries[0], Is.SameAs(dressEntry));
                Assert.That(figureEntry.entryName, Is.EqualTo(figureEntryName));
                Assert.That(dressEntry.entryName, Is.EqualTo(dressEntryName));
                Assert.That(figureEntry.materialChannel, Is.EqualTo(figureEntryChannel));
                Assert.That(dressEntry.materialChannel, Is.EqualTo(dressEntryChannel));
                Assert.That(figureEntry.adapter, Is.SameAs(figureEntryAdapter));
                Assert.That(dressEntry.adapter, Is.SameAs(dressEntryAdapter));
                Assert.That(plan.CorePlan.Operations[0].Kind, Is.EqualTo(MeshCoreOperationKind.MorphReset));
                Assert.That(plan.CorePlan.Operations[1].Kind, Is.EqualTo(MeshCoreOperationKind.Detach));
                Assert.That(plan.CorePlan.Operations[2].Kind, Is.EqualTo(MeshCoreOperationKind.DetachAll));
                Assert.That(plan.CorePlan.Operations[3].Kind, Is.EqualTo(MeshCoreOperationKind.AttachOutfit));
            }
        }

        [Test]
        public void BoneTable_AppendsExtraBonesWithoutMutatingFigureBase()
        {
            var root = new GameObject("bone-table-append");
            try
            {
                var baseBone = new GameObject("base").transform; baseBone.SetParent(root.transform, false);
                var extra = new GameObject("extra").transform; extra.SetParent(root.transform, false);
                var table = new HumanoidMeshBoneTable(
                    new[] { baseBone },
                    new[] { baseBone.worldToLocalMatrix * root.transform.localToWorldMatrix });
                Assert.That(table.TryAppendExtraBones(new[] { extra }, root.transform, out HumanoidMeshBoneTable expanded, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(table.Bones, Has.Length.EqualTo(1));
                Assert.That(expanded.Bones, Has.Length.EqualTo(2));
                Assert.That(expanded.Bones[1], Is.SameAs(extra));
                Assert.That(expanded.Bindposes[1], Is.EqualTo(extra.worldToLocalMatrix * root.transform.localToWorldMatrix));
                Assert.That(expanded.TryAppendExtraBones(new[] { extra }, root.transform, out _, out StackMachineDiagnostic duplicate), Is.False);
                Assert.That(duplicate.domainCode, Is.EqualTo("ExtraBoneTableConflict"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExtraBoneMerger_ClonesRegistrySubtreeIntoDetachedSkeletonAndAppendsBoneTable()
        {
            var outfitRoot = new GameObject("extra-bone-outfit");
            var skeletonRoot = new GameObject("extra-bone-skeleton");
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                var outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var renderer = outfitRoot.AddComponent<SkinnedMeshRenderer>();
                var extra = new GameObject("extra").transform; extra.SetParent(outfitRoot.transform, false);
                var leaf = new GameObject("leaf").transform; leaf.SetParent(extra, false);
                registry.bonePoses.Add(new BonePoseData { boneName = "extra", localPosition = Vector3.zero, localRotation = Quaternion.identity, localScale = Vector3.one });
                registry.bonePoses.Add(new BonePoseData { boneName = "extra/leaf", localPosition = Vector3.zero, localRotation = Quaternion.identity, localScale = Vector3.one });
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("baseExtraBoneRegistry").objectReferenceValue = registry;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var baseBone = new GameObject("base").transform; baseBone.SetParent(skeletonRoot.transform, false);
                var table = new HumanoidMeshBoneTable(new[] { baseBone }, new[] { baseBone.worldToLocalMatrix * skeletonRoot.transform.localToWorldMatrix });
                var source = new HumanoidMeshSource("outfit", "outfit.registry", outfitRoot, outfit, renderer, null);
                using (var escrow = new HumanoidMeshSkeletonEscrow(skeletonRoot, null, null))
                {
                    var claimed = new System.Collections.Generic.HashSet<string>();
                    Assert.That(HumanoidMeshExtraBoneMerger.TryMerge(source, escrow, table, claimed, out HumanoidMeshExtraBoneMergeResult merge, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    foreach (string rootPath in merge.OwnedRootPaths) claimed.Add(rootPath);
                    Assert.That(HumanoidMeshExtraBoneMerger.TryMerge(source, escrow, merge.BoneTable, claimed, out _, out StackMachineDiagnostic conflict), Is.False);
                    Assert.That(conflict.domainCode, Is.EqualTo("ExtraBoneRootOwned"));
                    Transform finalExtra = skeletonRoot.transform.Find("extra");
                    Transform finalLeaf = skeletonRoot.transform.Find("extra/leaf");
                    Assert.That(finalExtra, Is.Not.Null);
                    Assert.That(finalLeaf, Is.Not.Null);
                    Assert.That(merge.BoneTable.Bones, Has.Length.EqualTo(3));
                    Assert.That(merge.BoneTable.Bones[1], Is.SameAs(finalExtra));
                    Assert.That(merge.BoneTable.Bones[2], Is.SameAs(finalLeaf));
                    Assert.That(merge.FinalByOutfitTransform[extra], Is.SameAs(finalExtra));
                    Assert.That(merge.FinalByOutfitTransform[leaf], Is.SameAs(finalLeaf));
                    Assert.That(outfitRoot.transform.Find("extra/leaf"), Is.SameAs(leaf));
                }
                skeletonRoot = null;
            }
            finally
            {
                if (registry != null) Object.DestroyImmediate(registry);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
                if (skeletonRoot != null) Object.DestroyImmediate(skeletonRoot);
            }
        }

        [Test]
        public void ExtraBoneMerger_BakesResolvedFbmRegistryPoseBeforeFinalBindpose()
        {
            var outfitRoot = new GameObject("fbm-extra-outfit");
            var skeletonRoot = new GameObject("fbm-extra-skeleton");
            CharacterBoneRegistry baseRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry targetRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                var outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var renderer = outfitRoot.AddComponent<SkinnedMeshRenderer>();
                var extra = new GameObject("extra").transform; extra.SetParent(outfitRoot.transform, false);
                baseRegistry.bonePoses.Add(new BonePoseData { boneName = "extra", localPosition = Vector3.zero, localRotation = Quaternion.identity, localScale = Vector3.one });
                targetRegistry.bonePoses.Add(new BonePoseData { boneName = "extra", localPosition = new Vector3(2f, 0f, 0f), localRotation = Quaternion.identity, localScale = Vector3.one });
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("baseExtraBoneRegistry").objectReferenceValue = baseRegistry;
                SerializedProperty registries = serialized.FindProperty("fbmExtraBoneRegistries");
                registries.arraySize = 1;
                registries.GetArrayElementAtIndex(0).FindPropertyRelative("blendName").stringValue = "FBM_Test";
                registries.GetArrayElementAtIndex(0).FindPropertyRelative("extraBoneRegistry").objectReferenceValue = targetRegistry;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var baseBone = new GameObject("base").transform; baseBone.SetParent(skeletonRoot.transform, false);
                var table = new HumanoidMeshBoneTable(new[] { baseBone }, new[] { baseBone.worldToLocalMatrix * skeletonRoot.transform.localToWorldMatrix });
                var source = new HumanoidMeshSource("outfit", "outfit.registry", outfitRoot, outfit, renderer, null);
                using (var escrow = new HumanoidMeshSkeletonEscrow(skeletonRoot, null, null))
                {
                    var weights = new System.Collections.Generic.Dictionary<string, float> { { "FBM_Test", 0.5f } };
                    Assert.That(HumanoidMeshExtraBoneMerger.TryMerge(source, escrow, table, new System.Collections.Generic.HashSet<string>(), weights, out HumanoidMeshExtraBoneMergeResult merge, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Transform finalExtra = merge.FinalByOutfitTransform[extra];
                    Assert.That(finalExtra.localPosition, Is.EqualTo(new Vector3(1f, 0f, 0f)));
                    Assert.That(merge.BoneTable.Bindposes[1], Is.EqualTo(finalExtra.worldToLocalMatrix * skeletonRoot.transform.localToWorldMatrix));
                }
                skeletonRoot = null;
            }
            finally
            {
                if (baseRegistry != null) Object.DestroyImmediate(baseRegistry);
                if (targetRegistry != null) Object.DestroyImmediate(targetRegistry);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
                if (skeletonRoot != null) Object.DestroyImmediate(skeletonRoot);
            }
        }

        [Test]
        public void SkinningRemapper_RewritesOutfitSharedAndExtraBoneIndicesWithoutMutatingCandidate()
        {
            var outfitRoot = new GameObject("remap-outfit");
            var skeletonRoot = new GameObject("remap-skeleton");
            Mesh candidate = null;
            try
            {
                var sharedSource = new GameObject("shared").transform; sharedSource.SetParent(outfitRoot.transform, false);
                var extraSource = new GameObject("extra").transform; extraSource.SetParent(outfitRoot.transform, false);
                var renderer = outfitRoot.AddComponent<SkinnedMeshRenderer>();
                var sourceMesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 },
                    boneWeights = new[]
                    {
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                    }
                };
                renderer.sharedMesh = sourceMesh;
                renderer.bones = new[] { sharedSource, extraSource };
                var sharedFinal = new GameObject("shared").transform; sharedFinal.SetParent(skeletonRoot.transform, false);
                var extraFinal = new GameObject("extra").transform; extraFinal.SetParent(skeletonRoot.transform, false);
                var table = new HumanoidMeshBoneTable(
                    new[] { sharedFinal, extraFinal },
                    new[]
                    {
                        sharedFinal.worldToLocalMatrix * skeletonRoot.transform.localToWorldMatrix,
                        extraFinal.worldToLocalMatrix * skeletonRoot.transform.localToWorldMatrix
                    });
                candidate = Object.Instantiate(sourceMesh);
                var source = new HumanoidMeshSource("outfit", "outfit.registry", outfitRoot, null, renderer, null);
                using (var escrow = new HumanoidMeshSkeletonEscrow(skeletonRoot, null, null))
                {
                    var extraMap = new System.Collections.Generic.Dictionary<Transform, Transform> { { extraSource, extraFinal } };
                    Assert.That(HumanoidMeshSkinningRemapper.TryRemap(new HumanoidMeshFbmBakedSource(source, candidate), escrow, table, extraMap, out Mesh remapped, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    try
                    {
                        BoneWeight[] weights = remapped.boneWeights;
                        Assert.That(weights[0].boneIndex0, Is.EqualTo(0));
                        Assert.That(weights[1].boneIndex0, Is.EqualTo(1));
                        Assert.That(candidate.boneWeights[1].boneIndex0, Is.EqualTo(1));
                        Assert.That(remapped.bindposes, Is.EqualTo(table.Bindposes));
                    }
                    finally { Object.DestroyImmediate(remapped); }
                    candidate.boneWeights = new[]
                    {
                        new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 2, weight0 = 1f }
                    };
                    Assert.That(HumanoidMeshSkinningRemapper.TryRemap(new HumanoidMeshFbmBakedSource(source, candidate), escrow, table, extraMap, out _, out StackMachineDiagnostic invalidDiagnostic), Is.False);
                    Assert.That(invalidDiagnostic.domainCode, Is.EqualTo("MeshBoneIndexInvalid"));
                }
                skeletonRoot = null;
            }
            finally
            {
                if (candidate != null) Object.DestroyImmediate(candidate);
                if (outfitRoot != null) Object.DestroyImmediate(outfitRoot);
                if (skeletonRoot != null) Object.DestroyImmediate(skeletonRoot);
            }
        }

        [Test]
        public void MeshCombiner_MergesSubmeshesAndSameNameBlendShapeByVertexRange()
        {
            var root = new GameObject("combine-root");
            Mesh first = null;
            Mesh second = null;
            try
            {
                Transform bone = new GameObject("bone").transform; bone.SetParent(root.transform, false);
                var table = new HumanoidMeshBoneTable(new[] { bone }, new[] { bone.worldToLocalMatrix * root.transform.localToWorldMatrix });
                first = CreateCombineMesh("first", Vector3.right);
                second = CreateCombineMesh("second", Vector3.up);
                first.uv3 = new[] { Vector2.one, Vector2.right, Vector2.up };
                first.colors32 = new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12) };
                first.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                Assert.That(HumanoidMeshCombiner.TryCombine(new[] { first, second }, table, out Mesh combined, out int[] starts, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                try
                {
                    Assert.That(combined.vertexCount, Is.EqualTo(6));
                    Assert.That(combined.subMeshCount, Is.EqualTo(2));
                    Assert.That(starts, Is.EqualTo(new[] { 0, 1 }));
                    int shape = combined.GetBlendShapeIndex("PBM_Smile");
                    Assert.That(shape, Is.GreaterThanOrEqualTo(0));
                    var vertices = new Vector3[combined.vertexCount];
                    var normals = new Vector3[combined.vertexCount];
                    var tangents = new Vector3[combined.vertexCount];
                    combined.GetBlendShapeFrameVertices(shape, 0, vertices, normals, tangents);
                    Assert.That(vertices[0], Is.EqualTo(Vector3.right));
                    Assert.That(vertices[3], Is.EqualTo(Vector3.up));
                    var uv3 = new System.Collections.Generic.List<Vector4>(); combined.GetUVs(2, uv3);
                    Assert.That(uv3[0], Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
                    Assert.That(combined.colors32[0], Is.EqualTo(new Color32(1, 2, 3, 4)));
                    Assert.That(combined.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt32));
                    Assert.That(first.vertexCount, Is.EqualTo(3));
                    Assert.That(first.GetBlendShapeIndex("PBM_Smile"), Is.GreaterThanOrEqualTo(0));

                    // Destination Normalize clears every pre-existing frame before
                    // registering the final set; temporary semantic frames cannot
                    // survive because a particular removal was forgotten.
                    combined.AddBlendShapeFrame("FBM_Temporary", 100f, new Vector3[combined.vertexCount], new Vector3[combined.vertexCount], new Vector3[combined.vertexCount]);
                    combined.AddBlendShapeFrame("PCM_Temporary", 100f, new Vector3[combined.vertexCount], new Vector3[combined.vertexCount], new Vector3[combined.vertexCount]);
                    combined.AddBlendShapeFrame("MCM_Temporary", 100f, new Vector3[combined.vertexCount], new Vector3[combined.vertexCount], new Vector3[combined.vertexCount]);
                    combined.AddBlendShapeFrame("Morph_Slot_0", 100f, new Vector3[combined.vertexCount], new Vector3[combined.vertexCount], new Vector3[combined.vertexCount]);
                    Assert.That(HumanoidMeshFinalBlendShapeNormalizer.TryNormalize(combined, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
                    Assert.That(combined.GetBlendShapeIndex("PBM_Smile"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(combined.GetBlendShapeIndex("FBM_Temporary"), Is.EqualTo(-1));
                    Assert.That(combined.GetBlendShapeIndex("PCM_Temporary"), Is.EqualTo(-1));
                    Assert.That(combined.GetBlendShapeIndex("MCM_Temporary"), Is.EqualTo(-1));
                    Assert.That(combined.GetBlendShapeIndex("Morph_Slot_0"), Is.EqualTo(-1));
                }
                finally { Object.DestroyImmediate(combined); }
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MeshCombiner_ConvertsOutfitRendererLocalSpaceToFigureOutputSpace()
        {
            var root = new GameObject("combine-transform-root");
            Mesh figure = null;
            Mesh outfit = null;
            try
            {
                Transform bone = new GameObject("bone").transform; bone.SetParent(root.transform, false);
                var table = new HumanoidMeshBoneTable(new[] { bone }, new[] { bone.worldToLocalMatrix * root.transform.localToWorldMatrix });
                figure = CreateCombineMesh("figure", Vector3.right);
                outfit = CreateCombineMesh("outfit", Vector3.right);
                outfit.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                outfit.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
                Matrix4x4 outfitToFigure = Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                var sources = new[]
                {
                    new HumanoidMeshCombineSource(figure, Matrix4x4.identity),
                    new HumanoidMeshCombineSource(outfit, outfitToFigure)
                };
                Assert.That(HumanoidMeshCombiner.TryCombine(sources, table, out Mesh combined, out _, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                try
                {
                    Assert.That(Vector3.Distance(combined.vertices[3], new Vector3(5f, 0f, 0f)), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(combined.normals[3], Vector3.left), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(new Vector3(combined.tangents[3].x, combined.tangents[3].y, combined.tangents[3].z), Vector3.up), Is.LessThan(0.0001f));
                    Assert.That(combined.tangents[3].w, Is.EqualTo(1f));
                    int shape = combined.GetBlendShapeIndex("PBM_Smile");
                    var vertices = new Vector3[combined.vertexCount]; var normals = new Vector3[combined.vertexCount]; var tangents = new Vector3[combined.vertexCount];
                    combined.GetBlendShapeFrameVertices(shape, 0, vertices, normals, tangents);
                    Assert.That(Vector3.Distance(vertices[3], Vector3.up), Is.LessThan(0.0001f));
                }
                finally { Object.DestroyImmediate(combined); }
            }
            finally
            {
                if (figure != null) Object.DestroyImmediate(figure);
                if (outfit != null) Object.DestroyImmediate(outfit);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EditModeMeshStackMachine_CancelDisposesCompletedUntakenSkeletonEscrow()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                int before = CountHiddenSkeletonRoots(fixture.Figure.name);
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(CountHiddenSkeletonRoots(fixture.Figure.name), Is.EqualTo(before + 1));
                machine.Cancel();
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Cancelled));
                Assert.That(CountHiddenSkeletonRoots(fixture.Figure.name), Is.EqualTo(before));
                Assert.That(fixture.Figure, Is.Not.Null);
            }
        }

        [Test]
        public void EditModeMeshStackMachine_CompletedResultDisposeReleasesSkeletonEscrow()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                GameObject localRoot = result.Skeleton.Root;
                Avatar localAvatar = result.Skeleton.Avatar;
                Assert.That(localRoot, Is.Not.Null);
                Assert.That(localAvatar, Is.Not.Null);
                result.Dispose();
                Assert.That(localRoot == null, Is.True);
                Assert.That(localAvatar == null, Is.True);
                Assert.That(fixture.Figure, Is.Not.Null);
            }
        }

        [Test]
        public void EditModeMeshStackMachine_FinalizesAttachedOutfitExpressionAndMcm()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                Mesh outfitMesh = outfit.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                Vector3[] zeros = new Vector3[outfitMesh.vertexCount];
                outfitMesh.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                outfitMesh.AddBlendShapeFrame("VRM_Smile", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                outfitMesh.AddBlendShapeFrame("MCM_FBM_Body_Smile", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $dress ATTACH", outfit);
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Mesh finalOutfit = result.Sources[1].Mesh;
                    Assert.That(finalOutfit.GetBlendShapeIndex("FBM_Body"), Is.EqualTo(-1));
                    Assert.That(finalOutfit.GetBlendShapeIndex("MCM_FBM_Body_Smile"), Is.EqualTo(-1));
                    int expressionIndex = finalOutfit.GetBlendShapeIndex("VRM_Smile");
                    Assert.That(expressionIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalOutfit, expressionIndex, 100f, out Vector3[] delta, out _, out _), Is.True);
                    Assert.That(delta[0], Is.EqualTo(new Vector3(1f, .5f, 0f)));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_FinalizesAttachedOutfitPbm()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                Mesh outfitMesh = outfit.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                Vector3[] zeros = new Vector3[outfitMesh.vertexCount];
                outfitMesh.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                outfitMesh.AddBlendShapeFrame("PBM_Smile", 100f, zeros, zeros, zeros);
                outfitMesh.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { Vector3.right * 2f, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $dress ATTACH", outfit);
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Mesh finalOutfit = result.Sources[1].Mesh;
                    Assert.That(finalOutfit.GetBlendShapeIndex("FBM_Body"), Is.EqualTo(-1));
                    Assert.That(finalOutfit.GetBlendShapeIndex("PBM_FBM_Body_Smile"), Is.EqualTo(-1));
                    int pbmIndex = finalOutfit.GetBlendShapeIndex("PBM_Smile");
                    Assert.That(pbmIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalOutfit, pbmIndex, 100f, out Vector3[] delta, out _, out _), Is.True);
                    Assert.That(delta[0].x, Is.EqualTo(1f).Within(0.0001f));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_KeepsSamePbmNameAcrossAttachedOutfitsForMerge()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit first = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                ShapeSyncOutfit second = fixture.CreateOutfit("jacket", "outfit.jacket", false, false);
                ConfigurePbm(first.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh);
                ConfigurePbm(second.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $dress ATTACH $jacket ATTACH", first, second);
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Assert.That(result.Sources, Has.Count.EqualTo(3));
                    Assert.That(result.Sources[1].Mesh.GetBlendShapeIndex("PBM_Smile"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(result.Sources[2].Mesh.GetBlendShapeIndex("PBM_Smile"), Is.GreaterThanOrEqualTo(0));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_AppliesPcmBeforePbmAndExpressionFinalization()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", true, false);
                var outfitSerialized = new SerializedObject(outfit);
                outfitSerialized.FindProperty("profileControlledMorphOutfitName").stringValue = "dress";
                outfitSerialized.ApplyModifiedPropertiesWithoutUndo();
                Mesh source = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[source.vertexCount];
                source.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("PCM_FBM_Body_dress", 100f, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("PBM_Smile", 100f, zeros, zeros, zeros);
                source.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { Vector3.right * 2f, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("VRM_Smile", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("MCM_FBM_Body_Smile", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $dress ATTACH", outfit);
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Mesh finalMesh = result.Sources[0].Mesh;
                    Assert.That(finalMesh.vertices[0], Is.EqualTo(new Vector3(.5f, 1f, .5f)));
                    int pbm = finalMesh.GetBlendShapeIndex("PBM_Smile");
                    int expression = finalMesh.GetBlendShapeIndex("VRM_Smile");
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalMesh, pbm, 100f, out Vector3[] pbmDelta, out _, out _), Is.True);
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalMesh, expression, 100f, out Vector3[] expressionDelta, out _, out _), Is.True);
                    Assert.That(pbmDelta[0], Is.EqualTo(Vector3.right));
                    Assert.That(expressionDelta[0], Is.EqualTo(new Vector3(1f, .5f, 0f)));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_FinalizesResolvedPbmAfterFbmBake()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                Mesh source = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[source.vertexCount];
                source.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                source.AddBlendShapeFrame("PBM_Smile", 100f, zeros, zeros, zeros);
                source.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { Vector3.right * 2f, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET");
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Mesh finalMesh = result.Sources[0].Mesh;
                    Assert.That(finalMesh.GetBlendShapeIndex("FBM_Body"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("PBM_FBM_Body_Smile"), Is.EqualTo(-1));
                    int pbmIndex = finalMesh.GetBlendShapeIndex("PBM_Smile");
                    Assert.That(pbmIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalMesh, pbmIndex, 100f, out Vector3[] delta, out _, out _), Is.True);
                    Assert.That(delta[0].x, Is.EqualTo(1f).Within(0.0001f));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_StartPumpAndSingleTake_TransfersFbmBakeResult()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Pending));
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                using (result)
                {
                    Assert.That(result.LogicalPlan.Figure.Root, Is.SameAs(fixture.Figure));
                    Assert.That(result.Skeleton, Is.Not.Null);
                    Assert.That(result.Skeleton.Avatar, Is.Not.Null);
                    Assert.That(result.Skeleton.Root, Is.Not.SameAs(fixture.Figure));
                    Assert.That(result.BoneTable, Is.Not.Null);
                    Assert.That(result.BoneTable.Bones, Has.Length.EqualTo(1));
                    Assert.That(result.FinalMesh, Is.Not.Null);
                    Assert.That(result.FinalMesh.vertexCount, Is.EqualTo(result.Sources[0].Mesh.vertexCount));
                    Assert.That(result.FirstSubmeshBySource, Is.EqualTo(new[] { 0 }));
                    Assert.That(result.MaterialSlots, Has.Count.EqualTo(1));
                    Assert.That(result.MaterialSlots[0].MaterialId.RegistryId, Is.Empty);
                    Assert.That(result.MaterialSlots[0].MaterialId.EntryId, Is.EqualTo(result.LogicalPlan.Figure.MaterialProxy.Entries[0].entryName));
                    Assert.That(result.MaterialSlots[0].NewSubmeshIndex, Is.EqualTo(0));
                    Assert.That(result.MaterialSlots[0].Adapter, Is.SameAs(result.LogicalPlan.Figure.MaterialProxy.Entries[0].adapter));
                }
                Assert.That(machine.TryTakeFbmBakeResult(out _), Is.False);
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Idle));
            }
        }

        [Test]
        public void EditModeMeshStackMachine_TryTakeResult_TransfersOnlyFinalMeshPayload()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                using (result)
                {
                    Assert.That(result.Mesh, Is.Not.Null);
                    Assert.That(result.Skeleton, Is.Not.Null);
                    Assert.That(result.Avatar, Is.SameAs(result.Skeleton.Avatar));
                    Assert.That(result.BoneTable, Is.Not.Null);
                    Assert.That(result.MaterialSlots, Has.Count.EqualTo(1));
                    Assert.That(result.NormalPayloads, Is.Empty);
                }
                Assert.That(machine.TryTakeResult(out _), Is.False);
                Assert.That(machine.TryTakeFbmBakeResult(out _), Is.False);
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Idle));
            }
        }

        [Test]
        public void EditModeMeshBuildResult_TransfersOrderedVrmTransportProvenanceAndClearsOnlyCarrierNames()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit dress = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                ShapeSyncOutfit jacket = fixture.CreateOutfit("jacket", "outfit.jacket", false, false);
                ShapeSyncDocument document = fixture.CreateDocument("$dress ATTACH $jacket ATTACH", dress, jacket);
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);

                Assert.That(result.TryDetachVrmTransportProvenance(out HumanoidMeshVrmTransportProvenance provenance), Is.True);
                Assert.That(provenance.AttachedOutfitLogicalNames, Has.Count.EqualTo(2));
                Assert.That(provenance.AttachedOutfitLogicalNames[0], Is.EqualTo("dress"));
                Assert.That(provenance.AttachedOutfitLogicalNames[1], Is.EqualTo("jacket"));
                Assert.That(result.TryDetachVrmTransportProvenance(out _), Is.False);

                result.Dispose();
                Assert.That(provenance.AttachedOutfitLogicalNames, Has.Count.EqualTo(2));
                provenance.Dispose();
                Assert.That(provenance.AttachedOutfitLogicalNames, Is.Empty);
                Assert.That(dress.gameObject, Is.Not.Null);
                Assert.That(jacket.gameObject, Is.Not.Null);
            }
        }

        [Test]
        public void VrmTransportProvenance_TryCreateRejectsNullLogicalPlan()
        {
            Assert.That(HumanoidMeshVrmTransportProvenance.TryCreate(null, out HumanoidMeshVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(provenance, Is.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportLogicalPlanRequired"));
        }

        [Test]
        public void EditModeHumanoidBuildBackend_PumpsMeshAndTransfersOneDetachedCarrierWithoutSourceMutation()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            {
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                Mesh sourceMesh = fixture.FigureRenderer.sharedMesh;
                var backend = new EditModeHumanoidBuildBackend(meshMachine, null);
                var source = new HumanoidBuildSource(fixture.Figure, fixture.CreateDocument("MORPH_RESET"));

                Assert.That(backend.TryBeginMeshPhase(source, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), pumpDiagnostic?.message);
                using (payload)
                {
                    Assert.That(payload.Mesh, Is.Not.Null);
                    Assert.That(payload.Mesh.Mesh, Is.Not.Null);
                    Assert.That(payload.Mesh.Mesh, Is.Not.SameAs(sourceMesh));
                    Assert.That(payload.Mesh.Root, Is.Not.Null);
                    SkinnedMeshRenderer[] resolvedRenderers = payload.Mesh.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.That(resolvedRenderers, Has.Length.EqualTo(1));
                    Assert.That(resolvedRenderers[0].sharedMesh, Is.SameAs(payload.Mesh.Mesh));
                    Assert.That(resolvedRenderers[0].bones, Has.Length.EqualTo(payload.Mesh.Mesh.bindposeCount));
                    Assert.That(resolvedRenderers[0].rootBone, Is.Not.Null);
                    Assert.That(payload.MaterialSlots, Has.Count.EqualTo(1));
                    Assert.That(payload.MaterialSlots[0].SourceMaterial, Is.SameAs(sourceMaterial));
                    Assert.That(payload.MaterialSlots[0].SubmeshIndex, Is.EqualTo(0));
                    Assert.That(payload.SourceNormals, Is.Empty);
                    Assert.That(payload.ComputedNormals, Is.Empty);
                    Assert.That(fixture.FigureRenderer.sharedMesh, Is.SameAs(sourceMesh));
                    Assert.That(fixture.FigureRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                }

                Assert.That(backend.PumpMeshPhase(out _, out StackMachineDiagnostic repeatDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                Assert.That(repeatDiagnostic.domainCode, Is.EqualTo("EditModeMeshPhaseInactive"));
                backend.Cancel();
            }
        }

        [Test]
        public void EditModeHumanoidMeshPayloadBuilder_RejectsLostSourceMaterialAndResultDisposeReclaimsFinalMesh()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                Mesh finalMesh = result.Mesh;
                try
                {
                    fixture.FigureRenderer.sharedMaterial = null;
                    Assert.That(EditModeHumanoidMeshPayloadBuilder.TryCreate(result, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(payload, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("CompilerSourceMaterialMissing"));
                    Assert.That(result.Mesh, Is.SameAs(finalMesh));
                }
                finally
                {
                    fixture.FigureRenderer.sharedMaterial = sourceMaterial;
                    result.Dispose();
                }
                Assert.That(finalMesh == null, Is.True);
            }
        }

        [Test]
        public void EditModeHumanoidMeshPayloadBuilder_RejectsMissingFinalMeshAndInvalidMaterialSlot()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult missingMesh), Is.True);
                Mesh detached = missingMesh.DetachFinalMesh();
                try
                {
                    Assert.That(EditModeHumanoidMeshPayloadBuilder.TryCreate(missingMesh, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(payload, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeFinalMeshMissing"));
                }
                finally { missingMesh.Dispose(); Object.DestroyImmediate(detached); }

                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult invalidSlot), Is.True);
                typeof(EditModeMeshBuildResult).GetField("materialSlots", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(invalidSlot, new[] { new HumanoidMeshMaterialSlot(default, 0, null) });
                try
                {
                    Assert.That(EditModeHumanoidMeshPayloadBuilder.TryCreate(invalidSlot, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(payload, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeMaterialSlotInvalid"));
                }
                finally { invalidSlot.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidMeshPayloadBuilder_RejectsInvalidSourceAndComputedNormalCarrier()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult invalidSource), Is.True);
                var escrow = (HumanoidMeshFbmBakeResult)typeof(EditModeMeshBuildResult).GetField("escrow", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(invalidSource);
                typeof(HumanoidMeshLogicalPlan).GetField("<NormalTextureRegistrations>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(escrow.LogicalPlan, new[] { new HumanoidMeshNormalTextureRegistration(default, null) });
                try
                {
                    Assert.That(EditModeHumanoidMeshPayloadBuilder.TryCreate(invalidSource, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(payload, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeSourceNormalInvalid"));
                }
                finally { invalidSource.Dispose(); }

                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult invalidComputed), Is.True);
                typeof(EditModeMeshBuildResult).GetField("normalCompletions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(invalidComputed, new EditModeMeshNormalCompletion[] { null });
                try
                {
                    Assert.That(EditModeHumanoidMeshPayloadBuilder.TryCreate(invalidComputed, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(payload, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeComputedNormalInvalid"));
                }
                finally { invalidComputed.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_PreservesMeshPumpDiagnosticAndClearsSourceAfterFailure()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true, figureEntryName: "face"))
            using (var meshMachine = new EditModeMeshStackMachine())
            using (var materialMachine = new EditModeMaterialStackMachine(null))
            {
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                fixture.AddFigureNormalSources("face", "FBM_Body", out _, out _);
                fixture.AddFigureNormalBlender("face");
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);

                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, document), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out _, out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                Assert.That(pumpDiagnostic.domainCode, Is.EqualTo("NormalTextureMachineRequired"));

                var payload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), null, null, null);
                try
                {
                    Assert.That(backend.TryBeginMaterialPhase(payload, out StackMachineDiagnostic materialDiagnostic), Is.False);
                    Assert.That(materialDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
                }
                finally { payload.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_RejectsSingleTakeFailureAndClearsSource()
        {
            var root = new GameObject("backend-take-failure");
            try
            {
                var meshMachine = new TakeFailureMeshPhaseMachine();
                using (var materialMachine = new EditModeMaterialStackMachine(null))
                {
                    var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                    Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(backend.PumpMeshPhase(out MeshBuildPayload result, out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                    Assert.That(result, Is.Null);
                    Assert.That(pumpDiagnostic.domainCode, Is.EqualTo("EditModeMeshResultMissing"));
                    Assert.That(meshMachine.TakeCalls, Is.EqualTo(1));

                    var payload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), null, null, null);
                    try
                    {
                        Assert.That(backend.TryBeginMaterialPhase(payload, out StackMachineDiagnostic materialDiagnostic), Is.False);
                        Assert.That(materialDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
                    }
                    finally { payload.Dispose(); }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_RejectsMaterialBeginWithoutSourceMachineOrWhenMeshIsActive()
        {
            var root = new GameObject("editmode-humanoid-material-begin");
            try
            {
                var availableMaterialMachine = new TerminalMaterialPhaseMachine();
                var sourceMissingBackend = new EditModeHumanoidBuildBackend(new TakeFailureMeshPhaseMachine(), availableMaterialMachine);
                Assert.That(sourceMissingBackend.TryBeginMaterialPhase(null, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));

                var missingMachineBackend = new EditModeHumanoidBuildBackend(new TakeFailureMeshPhaseMachine(), null);
                Assert.That(missingMachineBackend.TryBeginMaterialPhase(null, out StackMachineDiagnostic machineDiagnostic), Is.False);
                Assert.That(machineDiagnostic.domainCode, Is.EqualTo("EditModeMaterialMachineRequired"));

                var busyBackend = new EditModeHumanoidBuildBackend(new TakeFailureMeshPhaseMachine(), availableMaterialMachine);
                Assert.That(busyBackend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(busyBackend.TryBeginMaterialPhase(null, out StackMachineDiagnostic busyDiagnostic), Is.False);
                Assert.That(busyDiagnostic.domainCode, Is.EqualTo("EditModeHumanoidBackendBusy"));
                busyBackend.Cancel();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_RejectsMeshMachineMissingBusyInactiveAndCancelled()
        {
            var root = new GameObject("editmode-humanoid-mesh-lifecycle");
            try
            {
                var missingMachine = new EditModeHumanoidBuildBackend(null, new TerminalMaterialPhaseMachine());
                Assert.That(missingMachine.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic missingDiagnostic), Is.False);
                Assert.That(missingDiagnostic.domainCode, Is.EqualTo("EditModeMeshMachineRequired"));

                var rejectedStartMachine = new TerminalMeshPhaseMachine { StartAccepted = false };
                var rejectedStartBackend = new EditModeHumanoidBuildBackend(rejectedStartMachine, new TerminalMaterialPhaseMachine());
                Assert.That(rejectedStartBackend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic rejectedStartDiagnostic), Is.False);
                Assert.That(rejectedStartDiagnostic, Is.Null);

                var busyMachine = new TakeFailureMeshPhaseMachine();
                var busyBackend = new EditModeHumanoidBuildBackend(busyMachine, new TerminalMaterialPhaseMachine());
                Assert.That(busyBackend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic beginDiagnostic), Is.True, beginDiagnostic?.message);
                Assert.That(busyBackend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out StackMachineDiagnostic busyDiagnostic), Is.False);
                Assert.That(busyDiagnostic.domainCode, Is.EqualTo("EditModeHumanoidBackendBusy"));
                busyBackend.Cancel();

                var inactiveBackend = new EditModeHumanoidBuildBackend(new TakeFailureMeshPhaseMachine(), new TerminalMaterialPhaseMachine());
                Assert.That(inactiveBackend.PumpMeshPhase(out _, out StackMachineDiagnostic inactiveDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                Assert.That(inactiveDiagnostic.domainCode, Is.EqualTo("EditModeMeshPhaseInactive"));

                Assert.That(inactiveBackend.PumpMaterialPhase(out _, out StackMachineDiagnostic inactiveMaterialDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                Assert.That(inactiveMaterialDiagnostic.domainCode, Is.EqualTo("EditModeMaterialPhaseInactive"));

                var cancelledBackend = new EditModeHumanoidBuildBackend(new TerminalMeshPhaseMachine { PumpStatus = EditModeMeshExecutionStatus.Cancelled }, new TerminalMaterialPhaseMachine());
                Assert.That(cancelledBackend.TryBeginMeshPhase(new HumanoidBuildSource(root, new ShapeSyncDocument()), out beginDiagnostic), Is.True, beginDiagnostic?.message);
                Assert.That(cancelledBackend.PumpMeshPhase(out _, out StackMachineDiagnostic cancelledDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Cancelled), cancelledDiagnostic?.message);
                Assert.That(cancelledBackend.TryBeginMaterialPhase(null, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_RejectsNullMeshPayloadAfterAcceptedMeshSource()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            {
                var backend = new EditModeHumanoidBuildBackend(meshMachine, new TerminalMaterialPhaseMachine());
                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, fixture.CreateDocument("MORPH_RESET")), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic meshDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), meshDiagnostic?.message);
                try
                {
                    Assert.That(backend.TryBeginMaterialPhase(null, out StackMachineDiagnostic payloadDiagnostic), Is.False);
                    Assert.That(payloadDiagnostic.domainCode, Is.EqualTo("EditModeMeshPayloadMissing"));
                }
                finally { meshPayload.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_PropagatesMaterialCancelledAndClearsAcceptedSource()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            {
                var materialMachine = new TerminalMaterialPhaseMachine { PumpStatus = EditModeMaterialExecutionStatus.Cancelled };
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, fixture.CreateDocument("MORPH_RESET")), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic meshDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), meshDiagnostic?.message);
                try
                {
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic materialStartDiagnostic), Is.True, materialStartDiagnostic?.message);
                    Assert.That(backend.PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic materialDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Cancelled), materialDiagnostic?.message);
                    Assert.That(materialPayload, Is.Null);
                    Assert.That(materialMachine.TakeCalls, Is.EqualTo(0));
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                    Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
                }
                finally { meshPayload.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_RejectsMissingMaterialSingleTakeAndClearsAcceptedSource()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            {
                var materialMachine = new TerminalMaterialPhaseMachine { PumpStatus = EditModeMaterialExecutionStatus.Succeeded, TakeAccepted = false };
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, fixture.CreateDocument("MORPH_RESET")), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic meshDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), meshDiagnostic?.message);
                try
                {
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic materialStartDiagnostic), Is.True, materialStartDiagnostic?.message);
                    Assert.That(backend.PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic materialDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                    Assert.That(materialPayload, Is.Null);
                    Assert.That(materialDiagnostic.domainCode, Is.EqualTo("EditModeMaterialResultMissing"));
                    Assert.That(materialMachine.TakeCalls, Is.EqualTo(1));
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                    Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
                }
                finally { meshPayload.Dispose(); }
            }
        }

        [Test]
        public void EditModeHumanoidBuildBackend_PreservesMaterialPumpFailureDiagnostic()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            {
                var materialMachine = new TerminalMaterialPhaseMachine
                {
                    PumpStatus = EditModeMaterialExecutionStatus.Failed,
                    PumpDiagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialPumpRejected", "Fixture rejected Material pump.")
                };
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, fixture.CreateDocument("MORPH_RESET")), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(backend.PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic meshDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), meshDiagnostic?.message);
                try
                {
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic materialStartDiagnostic), Is.True, materialStartDiagnostic?.message);
                    Assert.That(backend.PumpMaterialPhase(out _, out StackMachineDiagnostic materialDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Failed));
                    Assert.That(materialDiagnostic.domainCode, Is.EqualTo("MaterialPumpRejected"));
                    Assert.That(backend.TryBeginMaterialPhase(meshPayload, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                    Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("EditModeBuildSourceMissing"));
                }
                finally { meshPayload.Dispose(); }
            }
        }

        [UnityTest]
        public IEnumerator HumanoidCompiler_ActualEditModeBackendAppliesMaterialPayloadAfterMesh()
        {
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(textureCompute, Is.Not.Null);
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            using (var materialTextureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var materialMachine = new EditModeMaterialStackMachine(materialTextureMachine))
            {
                fixture.UseCompilerCompatibleMaterial();
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                Color sourceColor = sourceMaterial.GetColor("_BaseColor");
                Vector2 sourceScale = sourceMaterial.GetTextureScale("_BaseMap");
                Vector2 sourceOffset = sourceMaterial.GetTextureOffset("_BaseMap");
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                document.MaterialBinding = binding;
                document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$figure MATERIAL TEXTURE 0.2 0.3 0.4 1 FILL $out COPY DROP ENDTEXTURE 0.6 0.7 0.8 1 COLOR 2 3 0.25 0.5 UVSET" };
                try
                {
                    var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                    var compiler = new HumanoidCompiler();
                    Assert.That(compiler.TryCompile(new HumanoidBuildSource(fixture.Figure, document), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    HumanoidBuildResult result = null;
                    for (int i = 0; i < 240 && result == null; i++)
                    {
                        HumanoidBuildOperationStatus status = operation.Pump(out result, out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Failed), pumpDiagnostic?.message);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Cancelled), pumpDiagnostic?.message);
                        if (result == null) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                    }
                    Assert.That(result, Is.Not.Null);
                    RenderTexture mainTexture = null;
                    Avatar outputAvatar = null;
                    HumanoidVrmTransportProvenance vrmTransportProvenance = null;
                    try
                    {
                        Assert.That(operation.TryTakeVrmTransportProvenance(out vrmTransportProvenance, out StackMachineDiagnostic provenanceDiagnostic), Is.True, provenanceDiagnostic?.message);
                        Assert.That(vrmTransportProvenance.AttachedOutfitLogicalNames, Is.Empty);
                        Assert.That(operation.TryTakeVrmTransportProvenance(out _, out provenanceDiagnostic), Is.False);
                        Assert.That(provenanceDiagnostic.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                        Assert.That(result.Mesh.Materials, Has.Count.EqualTo(1));
                        outputAvatar = result.Mesh.Avatar;
                        Assert.That(outputAvatar, Is.Not.Null);
                        Assert.That(outputAvatar, Is.Not.SameAs(fixture.Figure.GetComponent<Animator>().avatar));
                        Material candidate = result.Mesh.Materials[0];
                        Assert.That(candidate, Is.Not.SameAs(sourceMaterial));
                        Assert.That(candidate.GetColor("_BaseColor"), Is.EqualTo(new Color(0.6f, 0.7f, 0.8f, 1f)));
                        Assert.That(candidate.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2f, 3f)));
                        Assert.That(candidate.GetTextureOffset("_BaseMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                        mainTexture = candidate.GetTexture("_BaseMap") as RenderTexture;
                        Assert.That(mainTexture, Is.Not.Null);
                        Assert.That(fixture.FigureRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                        Assert.That(sourceMaterial.GetColor("_BaseColor"), Is.EqualTo(sourceColor));
                        Assert.That(sourceMaterial.GetTextureScale("_BaseMap"), Is.EqualTo(sourceScale));
                        Assert.That(sourceMaterial.GetTextureOffset("_BaseMap"), Is.EqualTo(sourceOffset));
                    }
                    finally { result.Dispose(); operation.Dispose(); vrmTransportProvenance?.Dispose(); }
                    Assert.That(mainTexture == null || !mainTexture.IsCreated(), Is.True);
                    Assert.That(outputAvatar == null, Is.True);
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [Test]
        public void HumanoidCompiler_ActualEditModeBackendAbortsWhenMaterialLogicalValidationRejectsPayload()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            using (var materialMachine = new EditModeMaterialStackMachine(null))
            {
                fixture.UseCompilerCompatibleMaterial();
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                Color sourceColor = sourceMaterial.GetColor("_BaseColor");
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$figure MATERIAL 2 0 0 1 COLOR" };
                var compiler = new HumanoidCompiler();
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(fixture.Figure, document), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                try
                {
                    Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                    Assert.That(operation.Pump(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                    Assert.That(result, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialColorInvalid"));
                    Assert.That(fixture.FigureRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                    Assert.That(sourceMaterial.GetColor("_BaseColor"), Is.EqualTo(sourceColor));
                }
                finally { operation.Dispose(); }
            }
        }

        [Test]
        public void HumanoidCompiler_ActualEditModeBackendAbortsWhenCandidateAdapterRejectsSourceShader()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            using (var materialMachine = new EditModeMaterialStackMachine(null))
            {
                fixture.UseCompilerCompatibleMaterial();
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                Shader mismatchedShader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.That(mismatchedShader, Is.Not.Null);
                sourceMaterial.shader = mismatchedShader;
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                var compiler = new HumanoidCompiler();
                var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(fixture.Figure, document), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                try
                {
                    Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                    Assert.That(operation.Pump(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                    Assert.That(result, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialCloneRejected"));
                    Assert.That(materialMachine.Status, Is.EqualTo(EditModeMaterialExecutionStatus.Idle));
                    Assert.That(fixture.FigureRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                    Assert.That(sourceMaterial.shader, Is.SameAs(mismatchedShader));
                }
                finally { operation.Dispose(); }
            }
        }

        [UnityTest]
        public IEnumerator HumanoidCompiler_ActualEditModeBackendCancelsPendingMaterialGpuWork()
        {
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(textureCompute, Is.Not.Null);
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var meshMachine = new EditModeMeshStackMachine())
            using (var materialTextureMachine = new TextureEditModeStackMachine(textureCompute))
            using (var materialMachine = new EditModeMaterialStackMachine(materialTextureMachine))
            {
                fixture.UseCompilerCompatibleMaterial();
                Material sourceMaterial = fixture.FigureRenderer.sharedMaterial;
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                document.MaterialBinding = binding;
                document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$figure MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE" };
                try
                {
                    var compiler = new HumanoidCompiler();
                    var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                    Assert.That(compiler.TryCompile(new HumanoidBuildSource(fixture.Figure, document), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                    Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                    Assert.That(operation.Pump(out _, out StackMachineDiagnostic materialDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending), materialDiagnostic?.message);
                    Assert.That(materialMachine.Status, Is.EqualTo(EditModeMaterialExecutionStatus.Pending));
                    operation.Cancel();
                    Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                    Assert.That(materialMachine.Status, Is.EqualTo(EditModeMaterialExecutionStatus.Cancelled));
                    Assert.That(materialMachine.TryTakeResult(out _), Is.False);
                    Assert.That(fixture.FigureRenderer.sharedMaterial, Is.SameAs(sourceMaterial));
                    operation.Dispose();
                }
                finally { Object.DestroyImmediate(binding); }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator HumanoidCompiler_ActualEditModeBackendComputedNormalOverridesSourceNormal()
        {
            ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            Assert.That(textureCompute, Is.Not.Null);
            Assert.That(normalCompute, Is.Not.Null);
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true, figureEntryName: "face"))
            using (var meshMachine = new EditModeMeshStackMachine(new TextureEditModeStackMachine(textureCompute, normalCompute)))
            using (var materialMachine = new EditModeMaterialStackMachine(null))
            {
                fixture.UseCompilerCompatibleLitMaterial();
                Mesh sourceMesh = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[sourceMesh.vertexCount];
                sourceMesh.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var meshBinding = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = meshBinding.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                meshBinding.ApplyModifiedPropertiesWithoutUndo();
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D sourceNormal, out _);
                fixture.AddFigureNormalBlender("face");
                document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$face MATERIAL 0.6 0.7 0.8 1 COLOR" };
                try
                {
                    var compiler = new HumanoidCompiler();
                    var backend = new EditModeHumanoidBuildBackend(meshMachine, materialMachine);
                    Assert.That(compiler.TryCompile(new HumanoidBuildSource(fixture.Figure, document), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    HumanoidBuildResult result = null;
                    for (int i = 0; i < 240 && result == null; i++)
                    {
                        HumanoidBuildOperationStatus status = operation.Pump(out result, out StackMachineDiagnostic diagnostic);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Failed), diagnostic?.message);
                        if (result == null) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                    }
                    Assert.That(result, Is.Not.Null);
                    RenderTexture computedNormal = null;
                    try
                    {
                        Material candidate = result.Mesh.Materials[0];
                        computedNormal = candidate.GetTexture("_BumpMap") as RenderTexture;
                        Assert.That(computedNormal, Is.Not.Null);
                        Assert.That(candidate.GetTexture("_BumpMap"), Is.Not.SameAs(sourceNormal));
                        Assert.That(candidate.GetColor("_BaseColor"), Is.EqualTo(new Color(0.6f, 0.7f, 0.8f, 1f)));
                    }
                    finally { result.Dispose(); operation.Dispose(); }
                    Assert.That(computedNormal == null || !computedNormal.IsCreated(), Is.True);
                }
                finally { }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_TryTakeResult_MergesFigureAndOutfitMaterialSlots()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("$dress ATTACH", outfit), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                using (result)
                {
                    Assert.That(result.Mesh.subMeshCount, Is.EqualTo(2));
                    Assert.That(result.MaterialSlots, Has.Count.EqualTo(2));
                    Assert.That(result.MaterialSlots[0].MaterialId.RegistryId, Is.Empty);
                    Assert.That(result.MaterialSlots[0].NewSubmeshIndex, Is.EqualTo(0));
                    Assert.That(result.MaterialSlots[1].MaterialId.RegistryId, Is.EqualTo("outfit.dress"));
                    Assert.That(result.MaterialSlots[1].MaterialId.EntryId, Is.EqualTo(outfit.GetComponent<MaterialProxy>().Entries[0].entryName));
                    Assert.That(result.MaterialSlots[1].NewSubmeshIndex, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_TryTakeResult_MergesWeightedOutfitExtraBone()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                SkinnedMeshRenderer renderer = outfit.GetComponentInChildren<SkinnedMeshRenderer>();
                var extra = new GameObject("extra").transform; extra.SetParent(outfit.transform, false);
                renderer.bones = new[] { fixture.FigureRenderer.bones[0], extra };
                renderer.sharedMesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                renderer.sharedMesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1f }
                };
                outfit.SkinningProfile.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = renderer.sharedMesh.bindposes }
                });
                CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
                registry.bonePoses.Add(new BonePoseData { boneName = "extra", localPosition = Vector3.zero, localRotation = Quaternion.identity, localScale = Vector3.one });
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("baseExtraBoneRegistry").objectReferenceValue = registry;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                try
                {
                    Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("$dress ATTACH", outfit), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message + " " + pumpDiagnostic?.detail);
                    Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                    using (result)
                    {
                        Assert.That(result.BoneTable.Bones, Has.Length.EqualTo(2));
                        Assert.That(result.BoneTable.Bones[1].name, Is.EqualTo("extra"));
                        Assert.That(result.Mesh.boneWeights[fixture.FigureRenderer.sharedMesh.vertexCount + 1].boneIndex0, Is.EqualTo(1));
                    }
                }
                finally { Object.DestroyImmediate(registry); }
            }
        }

        [Test]
        public void EditModeMeshStackMachine_CancelClearsEscrowAndPermitsNextStart()
        {
            using (var fixture = new CollectorFixture())
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic busyDiagnostic), Is.False);
                Assert.That(busyDiagnostic.domainCode, Is.EqualTo("EditModeMeshMachineBusy"));
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Pending));
                machine.Cancel();
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Cancelled));
                Assert.That(machine.TryTakeFbmBakeResult(out _), Is.False);
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
                Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Pending));
            }
        }

        [Test]
        public void TryCreate_RejectsFigureWithZeroOrMultipleRenderers()
        {
            using (var fixture = new CollectorFixture(createFigureRenderer: false))
            {
                ShapeSyncDocument document = fixture.CreateDocument(string.Empty);
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic zeroDiagnostic), Is.False);
                Assert.That(zeroDiagnostic.domainCode, Is.EqualTo("FigureRendererCountInvalid"));

                fixture.Figure.AddComponent<SkinnedMeshRenderer>();
                fixture.CreateRendererChild(fixture.Figure, "collector-figure-extra-renderer");
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic multipleDiagnostic), Is.False);
                Assert.That(multipleDiagnostic.domainCode, Is.EqualTo("FigureRendererCountInvalid"));
            }
        }

        [Test]
        public void TryCreate_RejectsAttachedOutfitWithMultipleRenderers()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit dress = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                fixture.CreateRendererChild(dress.gameObject, "collector-outfit-extra-renderer");
                ShapeSyncDocument document = fixture.CreateDocument("$dress ATTACH", dress);

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitRendererCountInvalid"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("dress"));
            }
        }

        [Test]
        public void TryCreate_RejectsMissingMaterialAdapter()
        {
            using (var fixture = new CollectorFixture(createFigureAdapter: false))
            {
                ShapeSyncDocument document = fixture.CreateDocument(string.Empty);

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialProxyAdapterMissing"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("figure"));
            }
        }

        [Test]
        public void TryCreate_RejectsMissingMeshBindingAndInvalidOutfitRegistry()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncDocument bindingOnly = fixture.CreateDocument(string.Empty);
                bindingOnly.MeshRecipe = null;
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, bindingOnly, out HumanoidMeshLogicalPlan bindingOnlyPlan, out StackMachineDiagnostic bindingOnlyDiagnostic), Is.True, bindingOnlyDiagnostic?.message);
                Assert.That(bindingOnlyPlan.AttachedOutfits, Is.Empty);

                var missingBinding = new ShapeSyncDocument { MeshRecipe = new MeshRecipeDocument { wordSource = string.Empty } };
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, missingBinding, out _, out StackMachineDiagnostic missingDiagnostic), Is.False);
                Assert.That(missingDiagnostic.domainCode, Is.EqualTo("MeshDocumentBindingRequired"));

                ShapeSyncOutfit invalid = fixture.CreateOutfit("invalid", string.Empty, false, false);
                ShapeSyncDocument invalidDocument = fixture.CreateDocument("$invalid ATTACH", invalid);
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, invalidDocument, out _, out StackMachineDiagnostic invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic.domainCode, Is.EqualTo("OutfitBindingInvalid"));
                Assert.That(invalidDiagnostic.bindingName, Is.EqualTo("invalid"));
            }
        }

        [Test]
        public void TryCreate_RejectsDuplicateRegistryEvenWhenOnlyOneBindingIsAttached()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit first = fixture.CreateOutfit("first", "outfit.shared", false, false);
                ShapeSyncOutfit unusedSecond = fixture.CreateOutfit("second", "outfit.shared", false, false);
                ShapeSyncDocument document = fixture.CreateDocument("$first ATTACH", first, unusedSecond);

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateRegistryId"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("second"));
                Assert.That(diagnostic.detail, Is.EqualTo("outfit.shared"));
            }
        }

        [Test]
        public void TryCreate_RejectsNormalEntryWithAmbiguousOwner()
        {
            using (var fixture = new CollectorFixture(figureEntryName: "face"))
            {
                ShapeSyncOutfit dress = fixture.CreateOutfit("dress", "outfit.dress", false, false, "face");
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $dress ATTACH", dress);

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("NormalEntryOwnerInvalid"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("face"));
            }
        }

        [Test]
        public void TryCreate_RejectsNormalTemplateWithoutMatchingNormalOwner()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true, figureEntryName: "face"))
            {
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("NormalBaseTextureMissing"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("face"));
            }
        }

        [Test]
        public void TryCreate_SnapshotsMatchingNormalOwnerBaseAndFbmSources()
        {
            using (var fixture = new CollectorFixture(figureEntryName: "face"))
            {
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D baseTexture, out Texture2D targetTexture);
                fixture.AddFigureNormalBlender("face");

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.NormalSources, Has.Count.EqualTo(1));
                Assert.That(plan.NormalSources[0].Owner.Root, Is.SameAs(fixture.Figure));
                Assert.That(plan.NormalSources[0].EntryName, Is.EqualTo("face"));
                Assert.That(plan.NormalSources[0].BaseTexture, Is.SameAs(baseTexture));
                Assert.That(plan.NormalSources[0].Targets, Has.Count.EqualTo(1));
                Assert.That(plan.NormalSources[0].Targets[0].TargetName, Is.EqualTo("FBM_Body"));
                Assert.That(plan.NormalSources[0].Targets[0].Texture, Is.SameAs(targetTexture));
                Assert.That(plan.NormalTextureRegistrations, Has.Count.EqualTo(1));
                Assert.That(plan.NormalTextureRegistrations[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "face")));
                Assert.That(plan.NormalTextureRegistrations[0].NormalTexture, Is.SameAs(baseTexture));
            }
        }

        [Test]
        public void TryCreate_RegistersNormalWithoutBlenderEntryAndSkipsComputedNormal()
        {
            using (var fixture = new CollectorFixture(figureEntryName: "face"))
            {
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D baseTexture, out _);

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.NormalSources, Is.Empty);
                Assert.That(plan.NormalTextureRegistrations, Has.Count.EqualTo(1));
                Assert.That(plan.NormalTextureRegistrations[0].NormalTexture, Is.SameAs(baseTexture));
            }
        }

        [Test]
        public void TryCreate_RegistersNormalWithoutFbmTargetAndSkipsComputedNormal()
        {
            using (var fixture = new CollectorFixture(figureEntryName: "face"))
            {
                ShapeSyncDocument document = fixture.CreateDocument("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D baseTexture, out _);
                fixture.AddFigureNormalBlender("face");
                var serialized = new SerializedObject(document.MeshBinding);
                serialized.FindProperty("normalOwners").GetArrayElementAtIndex(0).FindPropertyRelative("targets").arraySize = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.NormalSources, Is.Empty);
                Assert.That(plan.NormalTextureRegistrations, Has.Count.EqualTo(1));
                Assert.That(plan.NormalTextureRegistrations[0].NormalTexture, Is.SameAs(baseTexture));
            }
        }

        [Test]
        public void NormalStubBuilder_ExpandsOnlyResolvedNonPbmFbmNormalSources()
        {
            using (var fixture = new CollectorFixture(figureEntryName: "face"))
            {
                Mesh source = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[source.vertexCount];
                source.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();
                fixture.AddFigureNormalSources("face", "FBM_Body", out _, out _);
                fixture.AddFigureNormalBlender("face");

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                Assert.That(plan.NormalSources, Has.Count.EqualTo(1));
                Assert.That(HumanoidMeshFbmBaker.TryBake(plan, out HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic bakeDiagnostic), Is.True, bakeDiagnostic?.message);
                using (bake)
                {
                    Assert.That(HumanoidMeshNormalStubBuilder.TryCreate(bake, plan.NormalSources[0], out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(stub.Document.wordSource, Does.Contain("0.5 NORMAL_DELTA_ADD"));
                    Assert.That(stub.Bindings, Has.Length.EqualTo(3));
                    Assert.That(stub.Bindings[0].logicalName, Is.EqualTo("base"));
                    Assert.That(stub.Bindings[1].logicalName, Is.EqualTo("out"));
                    Assert.That(stub.Bindings[2].logicalName, Is.EqualTo("target0"));
                }
            }
        }

        [Test]
        public void NormalStubBuilder_ExcludesPbmNormalSourceFromTextureRecipe()
        {
            Texture2D baseTexture = new Texture2D(2, 2);
            Texture2D pbmTexture = new Texture2D(2, 2);
            try
            {
                var recipe = new MeshRecipeDocument { wordSource = "$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL" };
                Assert.That(MeshStackMachineCorePlan.TryCreate(recipe, new[] { MeshCoreBinding.Normal("face") }, out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var normalSource = new HumanoidMeshNormalSource(default, "face", baseTexture, new[] { new HumanoidMeshNormalTargetSource("PBM_Body", pbmTexture) });
                var plan = new HumanoidMeshLogicalPlan(core, default, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), new[] { normalSource });

                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float>()))
                {
                    Assert.That(HumanoidMeshNormalStubBuilder.TryCreate(bake, normalSource, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(stub.Document.wordSource, Does.Not.Contain("NORMAL_DELTA_ADD"));
                    Assert.That(stub.Bindings, Has.Length.EqualTo(2));
                }
            }
            finally { Object.DestroyImmediate(baseTexture); Object.DestroyImmediate(pbmTexture); }
        }

        [UnityTest]
        public IEnumerator EditModeMeshStackMachine_PumpsNormalCompletionIntoMeshEscrow()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true, figureEntryName: "face"))
            {
                Mesh source = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[source.vertexCount];
                source.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D baseNormal, out _);
                fixture.AddFigureNormalBlender("face");
                ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
                Assert.That(textureCompute, Is.Not.Null);
                Assert.That(normalCompute, Is.Not.Null);

                using (var machine = new EditModeMeshStackMachine(new TextureEditModeStackMachine(textureCompute, normalCompute)))
                {
                    Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int i = 0; i < 240 && machine.Status == EditModeMeshExecutionStatus.Pending; i++)
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                        machine.Pump(out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(pumpDiagnostic, Is.Null, pumpDiagnostic == null ? null : pumpDiagnostic.domainCode + ": " + pumpDiagnostic.message);
                    }
                    Assert.That(machine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), machine.Diagnostic?.message);
                    Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                    using (result)
                    {
                        Assert.That(result.NormalCompletions, Has.Count.EqualTo(1));
                        Assert.That(result.NormalCompletions[0].Source.EntryName, Is.EqualTo("face"));
                        Assert.That(result.NormalCompletions[0].Completion.Texture, Is.Not.Null);
                        Assert.That(result.NormalCompletions[0].Completion.Texture.IsCreated(), Is.True);
                        Assert.That(result.NormalTextureRegistrations, Has.Count.EqualTo(1));
                        Assert.That(result.NormalTextureRegistrations[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "face")));
                        Assert.That(result.NormalTextureRegistrations[0].NormalTexture, Is.SameAs(baseNormal));
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator EditModeHumanoidBuildBackend_TransfersComputedNormalAndCancelsPendingMeshWork()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true, figureEntryName: "face"))
            {
                Mesh sourceMesh = fixture.FigureRenderer.sharedMesh;
                Vector3[] zeros = new Vector3[sourceMesh.vertexCount];
                sourceMesh.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL");
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();
                fixture.AddFigureNormalSources("face", "FBM_Body", out Texture2D baseNormal, out _);
                fixture.AddFigureNormalBlender("face");
                ComputeShader textureCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);

                using (var meshMachine = new EditModeMeshStackMachine(new TextureEditModeStackMachine(textureCompute, normalCompute)))
                {
                    var backend = new EditModeHumanoidBuildBackend(meshMachine, null);
                    Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, document), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(backend.PumpMeshPhase(out _, out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Pending), firstDiagnostic?.message);
                    backend.Cancel();
                    Assert.That(meshMachine.Status, Is.EqualTo(EditModeMeshExecutionStatus.Cancelled));

                    Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(fixture.Figure, document), out startDiagnostic), Is.True, startDiagnostic?.message);
                    MeshBuildPayload payload = null;
                    for (int i = 0; i < 240 && payload == null; i++)
                    {
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                        HumanoidBuildPhaseStatus status = backend.PumpMeshPhase(out payload, out StackMachineDiagnostic pumpDiagnostic);
                        Assert.That(status, Is.Not.EqualTo(HumanoidBuildPhaseStatus.Failed), pumpDiagnostic?.message);
                    }
                    Assert.That(payload, Is.Not.Null);
                    RenderTexture computedNormal = null;
                    try
                    {
                        Assert.That(payload.SourceNormals, Has.Count.EqualTo(1));
                        Assert.That(payload.SourceNormals[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "face")));
                        Assert.That(payload.SourceNormals[0].Texture, Is.SameAs(baseNormal));
                        Assert.That(payload.ComputedNormals, Has.Count.EqualTo(1));
                        Assert.That(payload.ComputedNormals[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "face")));
                        computedNormal = payload.ComputedNormals[0].Texture.Texture as RenderTexture;
                        Assert.That(computedNormal, Is.Not.Null);
                        Assert.That(computedNormal.IsCreated(), Is.True);
                        Assert.That(fixture.FigureRenderer.sharedMesh, Is.SameAs(sourceMesh));
                    }
                    finally { payload.Dispose(); }
                    Assert.That(computedNormal == null || !computedNormal.IsCreated(), Is.True);
                }
            }
        }

        [Test]
        public void FbmBaker_BakesSetMorphAndRemovesOnlyConsumedFbmFrames()
        {
            GameObject root = new GameObject("fbm-baker-figure");
            Mesh source = null;
            try
            {
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                source = CreateFbmBakeMesh();
                renderer.sharedMesh = source;
                var recipe = new MeshRecipeDocument { wordSource = "$body 0.5 FBM_SET" };
                recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                Assert.That(MeshStackMachineCorePlan.TryCreate(recipe, new[] { MeshCoreBinding.Morph("body", "FBM_Body") }, out MeshStackMachineCorePlan corePlan, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var logicalPlan = new HumanoidMeshLogicalPlan(corePlan, new HumanoidMeshSource(null, string.Empty, root, null, renderer, null), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());

                Assert.That(HumanoidMeshFbmBaker.TryBake(logicalPlan, out HumanoidMeshFbmBakeResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (result)
                {
                    Mesh baked = result.Sources[0].Mesh;
                    Assert.That(result.FbmWeights["FBM_Body"], Is.EqualTo(0.5f).Within(0.0001f));
                    Assert.That(baked.vertices[0].x, Is.EqualTo(0.5f).Within(0.0001f));
                    Assert.That(source.vertices[0].x, Is.EqualTo(0f).Within(0.0001f));
                    Assert.That(baked.GetBlendShapeIndex("FBM_Body"), Is.EqualTo(-1));
                    Assert.That(baked.GetBlendShapeIndex("PBM_Body"), Is.GreaterThanOrEqualTo(0));
                }
            }
            finally
            {
                if (source != null) Object.DestroyImmediate(source);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SkeletonBuilder_RejectsFigureWithoutHumanoidAnimatorWithoutMutatingSource()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncDocument document = fixture.CreateDocument("MORPH_RESET");
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                Assert.That(HumanoidMeshFbmBaker.TryBake(plan, out HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic bakeDiagnostic), Is.True, bakeDiagnostic?.message);
                using (bake)
                {
                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow escrow, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(escrow, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HumanoidAnimatorRequired"));
                    Assert.That(fixture.Figure.GetComponent<Animator>(), Is.Null);
                }
            }
        }

        [Test]
        public void SkeletonBuilder_StoresResolvedBcpInAvatarAndRestoresResolvedRestPoseOnRequest()
        {
            var root = new GameObject("skeleton-bcp-source");
            Avatar avatar = null;
            try
            {
                avatar = CreateTestHumanoidAvatar(root, "Bcp_");
                Animator sourceAnimator = root.AddComponent<Animator>();
                sourceAnimator.avatar = avatar;
                Assert.That(sourceAnimator.runtimeAnimatorController, Is.Null, "Controller未設定のHumanoid Animatorもskeleton build入力として受理する。");
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var plan = new HumanoidMeshLogicalPlan(core, new HumanoidMeshSource(null, string.Empty, root, null, null, null), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float>()))
                {
                    bake.SetBcpDeltas(new[] { new HumanoidMeshBcpDelta(HumanBodyBones.LeftUpperArm, Vector3.right, Quaternion.identity, Vector3.zero) });
                    Transform sourceBone = sourceAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    Vector3 sourcePosition = sourceBone.localPosition;

                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow escrow, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    LogAssert.NoUnexpectedReceived();
                    using (escrow)
                    {
                        Transform cloneBone = escrow.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        Assert.That(cloneBone, Is.Not.Null);
                        Assert.That(escrow.Avatar, Is.Not.Null);
                        Assert.That(escrow.Avatar, Is.Not.SameAs(avatar));
                        Assert.That(escrow.Avatar.isValid && escrow.Avatar.isHuman, Is.True);
                        Assert.That(cloneBone.localPosition.x, Is.EqualTo(sourcePosition.x).Within(0.0001f));
                        Assert.That(cloneBone.localPosition.y, Is.EqualTo(sourcePosition.y).Within(0.0001f));
                        SkeletonBone resolvedAvatarBone = FindSkeletonBone(escrow.Avatar.humanDescription, cloneBone.name);
                        Assert.That(resolvedAvatarBone.position.x, Is.EqualTo(sourcePosition.x + 1f).Within(0.0001f));
                        // The internal build pose remains authoring-space until
                        // skinning inputs are finalized. The publish path must be
                        // able to restore the resolved FBM/BCP rest pose without
                        // sampling or playing an Animator.
                        escrow.RestoreResolvedHumanoidPose();
                        Assert.That(cloneBone.localPosition.x, Is.EqualTo(sourcePosition.x + 1f).Within(0.0001f));
                        Assert.That(sourceBone.localPosition.x, Is.EqualTo(sourcePosition.x).Within(0.0001f));
                    }
                }
            }
            finally { if (avatar != null) Object.DestroyImmediate(avatar); Object.DestroyImmediate(root); }
        }

#if SHAPESYNC_RICH_TEST
        [Test]
        public void EditorHumanoidPublisher_UsesResolvedRestPoseForPureCandidate()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject figure = null;
            MeshBuildPayload meshPayload = null;
            var backend = new EditModeHumanoidBuildBackend((TextureEditModeStackMachine)null, (TextureEditModeStackMachine)null);
            try
            {
                figure = Object.Instantiate(figurePrefab);
                Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(figure, payload), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                HumanoidBuildPhaseStatus status = HumanoidBuildPhaseStatus.Pending;
                StackMachineDiagnostic pumpDiagnostic = null;
                for (int i = 0; i < 32 && status == HumanoidBuildPhaseStatus.Pending; i++)
                    status = backend.PumpMeshPhase(out meshPayload, out pumpDiagnostic);
                Assert.That(status, Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(meshPayload, Is.Not.Null);

                GameObject candidate = meshPayload.Mesh.Root;
                Assert.That(candidate, Is.Not.Null);
                Assert.That(candidate.GetComponentInChildren<DynamicBoneBlender>(true), Is.Null, "Published candidate must be Pure Humanoid.");
                Animator animator = candidate.GetComponent<Animator>();
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.avatar, Is.Not.Null);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Assert.That(hips, Is.Not.Null);
                SkeletonBone resolvedAvatarBone = FindSkeletonBone(animator.avatar.humanDescription, hips.name);
                Assert.That(hips.localPosition.x, Is.EqualTo(resolvedAvatarBone.position.x).Within(0.0001f));
                Assert.That(hips.localPosition.y, Is.EqualTo(resolvedAvatarBone.position.y).Within(0.0001f));
                Assert.That(hips.localPosition.z, Is.EqualTo(resolvedAvatarBone.position.z).Within(0.0001f));
            }
            finally
            {
                meshPayload?.Dispose();
                backend.Cancel();
                if (figure != null) Object.DestroyImmediate(figure);
            }
        }
#endif

        [Test]
        public void EditModeMeshStackMachine_FinalResolvedHumanoidRootResetsTransformWithoutMutatingSource()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                fixture.Figure.transform.localPosition = new Vector3(3f, 4f, 5f);
                fixture.Figure.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                fixture.Figure.transform.localScale = new Vector3(2f, 3f, 4f);
                Vector3 sourcePosition = fixture.Figure.transform.localPosition;
                Quaternion sourceRotation = fixture.Figure.transform.localRotation;
                Vector3 sourceScale = fixture.Figure.transform.localScale;

                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                using (result)
                {
                    Transform root = result.Skeleton.Root.transform;
                    Assert.That(root.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(root.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(root.localScale, Is.EqualTo(Vector3.one));
                }

                Assert.That(fixture.Figure.transform.localPosition, Is.EqualTo(sourcePosition));
                Assert.That(fixture.Figure.transform.localRotation, Is.EqualTo(sourceRotation));
                Assert.That(fixture.Figure.transform.localScale, Is.EqualTo(sourceScale));
            }
        }

        [Test]
        public void EditModeMeshStackMachine_KeepsWorkingResolvedHumanoidInactiveUntilPublish()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                fixture.Figure.SetActive(true);
                Assert.That(machine.Start(fixture.Figure, fixture.CreateDocument("MORPH_RESET"), out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTakeResult(out EditModeMeshBuildResult result), Is.True);
                using (result)
                {
                    Assert.That(result.Skeleton.Root.activeSelf, Is.False);
                    Assert.That(result.Skeleton.Root.activeInHierarchy, Is.False);
                }
                Assert.That(fixture.Figure.activeSelf, Is.True);
            }
        }

        [Test]
        public void TryCreate_RejectsAttachedOutfitWithoutOutfitAttacherSkinningProfile()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("skinningProfile").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, fixture.CreateDocument("$dress ATTACH", outfit), out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitSkinningProfileRequired"));
            }
        }

        [Test]
        public void TryCreate_CarriesOutfitAttacherWeightedBonePathWithoutNameFallback()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, false);
                SkinnedMeshRenderer renderer = outfit.GetComponentInChildren<SkinnedMeshRenderer>();
                Transform bone = renderer.rootBone;
                Mesh mesh = renderer.sharedMesh;
                mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                };
                outfit.SkinningProfile.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = mesh.bindposes }
                });

                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, fixture.CreateDocument("$dress ATTACH", outfit), out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.AttachedOutfits[0].WeightedBonePaths.ContainsKey(bone), Is.True);
                Assert.That(plan.AttachedOutfits[0].WeightedBonePaths[bone], Is.EqualTo("rootBone"));
            }
        }

        [Test]
        public void FinalMeshBuilder_RebuildsOutfitRendererPoseAfterIdentityRootAttach()
        {
            var figureRoot = new GameObject("final-transform-figure");
            var figureRenderer = new GameObject("figure-renderer").transform;
            var outfitRoot = new GameObject("final-transform-outfit");
            var outfitRenderer = new GameObject("outfit-renderer").transform;
            try
            {
                figureRenderer.SetParent(figureRoot.transform, false);
                figureRenderer.localPosition = new Vector3(1f, 0f, 0f);
                outfitRoot.transform.localPosition = new Vector3(601.02435f, 269.0632f, 0f);
                outfitRenderer.SetParent(outfitRoot.transform, false);
                outfitRenderer.localPosition = new Vector3(0.5f, 0f, 0f);

                Assert.That(HumanoidMeshFinalMeshBuilder.TryCreateAttachedSourceToOutput(figureRoot.transform, figureRenderer, outfitRoot.transform, outfitRenderer, out Matrix4x4 transform, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Vector3 result = transform.MultiplyPoint3x4(Vector3.zero);
                Assert.That(result.x, Is.EqualTo(-0.5f).Within(0.0001f));
                Assert.That(result.y, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(result.z, Is.EqualTo(0f).Within(0.0001f));
            }
            finally { Object.DestroyImmediate(figureRoot); Object.DestroyImmediate(outfitRoot); }
        }

        [Test]
        public void FinalMeshBuilder_MatchesOutfitAttacherRendererPoseAfterAttach()
        {
            var figureRoot = new GameObject("attach-pose-figure");
            var outfitRoot = new GameObject("attach-pose-outfit");
            Mesh figureMesh = null;
            Mesh outfitMesh = null;
            OutfitSkinningProfile skinningProfile = null;
            CharacterBoneRegistry extraBoneRegistry = null;
            try
            {
                figureRoot.transform.SetPositionAndRotation(new Vector3(13f, -2f, 7f), Quaternion.Euler(0f, 27f, 0f));
                figureRoot.transform.localScale = new Vector3(1.25f, 0.8f, 1.1f);
                Transform figureBone = new GameObject("rootBone").transform;
                figureBone.SetParent(figureRoot.transform, false);
                Transform figureRendererTransform = new GameObject("figure-renderer").transform;
                figureRendererTransform.SetParent(figureRoot.transform, false);
                figureRendererTransform.localPosition = new Vector3(1f, -0.25f, 0.5f);
                figureRendererTransform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                SkinnedMeshRenderer figureRenderer = figureRendererTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                figureMesh = CreatePoseComparisonMesh();
                figureRenderer.sharedMesh = figureMesh;
                figureRenderer.rootBone = figureBone;
                figureRenderer.bones = new[] { figureBone };

                Animator animator = figureRoot.AddComponent<Animator>();
                DynamicBoneBlender blender = figureRoot.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(figureRenderer, animator, null, null, new System.Collections.Generic.List<DynamicBoneBlendTarget>());
                OutfitAttacher attacher = figureRoot.AddComponent<OutfitAttacher>();
                attacher.ConfigureForFigure(blender, animator);

                outfitRoot.transform.SetPositionAndRotation(new Vector3(601.02435f, 269.0632f, -4f), Quaternion.Euler(11f, 43f, -9f));
                outfitRoot.transform.localScale = new Vector3(1.8f, 0.7f, 1.4f);
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var serializedOutfit = new SerializedObject(outfit);
                serializedOutfit.FindProperty("registryId").stringValue = "outfit.attach-pose";
                serializedOutfit.FindProperty("fbmExtraBoneRegistries").arraySize = 0;
                extraBoneRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
                serializedOutfit.FindProperty("baseExtraBoneRegistry").objectReferenceValue = extraBoneRegistry;
                serializedOutfit.ApplyModifiedPropertiesWithoutUndo();

                Transform outfitBone = new GameObject("rootBone").transform;
                outfitBone.SetParent(outfitRoot.transform, false);
                Transform outfitRendererTransform = new GameObject("renderer").transform;
                outfitRendererTransform.SetParent(outfitRoot.transform, false);
                outfitRendererTransform.localPosition = new Vector3(0.5f, -1.25f, 2f);
                outfitRendererTransform.localRotation = Quaternion.Euler(-5f, 17f, 31f);
                outfitRendererTransform.localScale = new Vector3(0.9f, 1.4f, 0.6f);
                SkinnedMeshRenderer outfitRenderer = outfitRendererTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                outfitMesh = CreatePoseComparisonMesh();
                outfitRenderer.sharedMesh = outfitMesh;
                outfitRenderer.rootBone = outfitBone;
                outfitRenderer.bones = new[] { outfitBone };
                skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                skinningProfile.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = outfitMesh.bindposes }
                });
                serializedOutfit.Update();
                serializedOutfit.FindProperty("skinningProfile").objectReferenceValue = skinningProfile;
                serializedOutfit.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(HumanoidMeshFinalMeshBuilder.TryCreateAttachedSourceToOutput(figureRoot.transform, figureRendererTransform, outfitRoot.transform, outfitRendererTransform, out Matrix4x4 compilerPose, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(attacher.TryAttach(outfit), Is.True);
                Assert.That(attacher.TryGetAttachedOutfit("outfit.attach-pose", out ShapeSyncOutfit attachedOutfit, out StackMachineDiagnostic attachedDiagnostic), Is.True, attachedDiagnostic?.message);
                Transform attachedRenderer = attachedOutfit.transform.Find("renderer");
                Assert.That(attachedRenderer, Is.Not.Null);

                Matrix4x4 attacherPose = figureRendererTransform.worldToLocalMatrix * attachedRenderer.localToWorldMatrix;
                AssertMatrixApproximatelyEqual(attacherPose, compilerPose);
            }
            finally
            {
                if (skinningProfile != null) Object.DestroyImmediate(skinningProfile);
                if (extraBoneRegistry != null) Object.DestroyImmediate(extraBoneRegistry);
                if (outfitMesh != null) Object.DestroyImmediate(outfitMesh);
                if (figureMesh != null) Object.DestroyImmediate(figureMesh);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figureRoot);
            }
        }

        [Test]
        public void SkeletonBuilder_MatchesOutfitAttacherBcpAvatarAfterAttach()
        {
            var figureRoot = new GameObject("attach-bcp-figure");
            var outfitRoot = new GameObject("attach-bcp-outfit");
            Avatar baseAvatar = null;
            Mesh figureMesh = null;
            Mesh outfitMesh = null;
            OutfitSkinningProfile skinningProfile = null;
            CharacterBoneRegistry extraBoneRegistry = null;
            ShapeSyncHumanoidBoneCorrectionProfile bcpProfile = null;
            ShapeSyncHumanoidBoneCorrectionProfile bcpTargetProfile = null;
            try
            {
                baseAvatar = CreateTestHumanoidAvatar(figureRoot, "BcpAttach_");
                Animator animator = figureRoot.AddComponent<Animator>();
                animator.avatar = baseAvatar;
                Transform upperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                SkinnedMeshRenderer figureRenderer = figureRoot.AddComponent<SkinnedMeshRenderer>();
                figureMesh = CreatePoseComparisonMesh();
                figureRenderer.sharedMesh = figureMesh;
                figureRenderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                figureRenderer.bones = new[] { upperArm };
                DynamicBoneBlender blender = figureRoot.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(figureRenderer, animator, baseAvatar, null, new System.Collections.Generic.List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "FBM_Body", enabled = true, weight = 0.5f, targetAvatar = baseAvatar }
                });
                OutfitAttacher attacher = figureRoot.AddComponent<OutfitAttacher>();
                attacher.ConfigureForFigure(blender, animator);

                ShapeSyncOutfit outfit = CreateRuntimeAttachOutfit(outfitRoot, "outfit.bcp-attach", GetRelativePath(figureRoot.transform, upperArm), out outfitMesh, out skinningProfile, out extraBoneRegistry);
                bcpProfile = CreateBcpProfile(HumanBodyBones.LeftUpperArm, new Vector3(0.25f, -0.5f, 0.75f), Quaternion.Euler(7f, 19f, -13f), new Vector3(0.1f, 0.2f, -0.1f));
                bcpTargetProfile = CreateBcpProfile(HumanBodyBones.LeftUpperArm, new Vector3(1.25f, 0.5f, -0.25f), Quaternion.Euler(-5f, 43f, 11f), new Vector3(0.4f, -0.1f, 0.3f));
                var serializedOutfit = new SerializedObject(outfit);
                serializedOutfit.FindProperty("humanoidBoneCorrectionProfile").objectReferenceValue = bcpProfile;
                SerializedProperty fbmProfiles = serializedOutfit.FindProperty("fbmHumanoidBoneCorrectionProfiles");
                fbmProfiles.arraySize = 1;
                fbmProfiles.GetArrayElementAtIndex(0).FindPropertyRelative("blendName").stringValue = "FBM_Body";
                fbmProfiles.GetArrayElementAtIndex(0).FindPropertyRelative("targetProfile").objectReferenceValue = bcpTargetProfile;
                serializedOutfit.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(attacher.TryAttach(outfit), Is.True);
                InvokePrivateInstanceMethod(blender, "Start");
                Assert.That(animator.avatar, Is.Not.SameAs(baseAvatar), "Runtime BCP must rebuild the Figure Avatar after the Outfit is attached.");

                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figureRoot, null, figureRenderer, null);
                Transform outfitRenderer = outfit.transform.Find("renderer");
                var outfitSource = new HumanoidMeshSource("dress", outfit.RegistryId, outfitRoot, outfit, outfitRenderer.GetComponent<SkinnedMeshRenderer>(), null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), new[] { outfitSource }, System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshBcpResolver.TryResolve(bake, out System.Collections.Generic.IReadOnlyList<HumanoidMeshBcpDelta> deltas, out StackMachineDiagnostic bcpDiagnostic), Is.True, bcpDiagnostic?.message);
                    bake.SetBcpDeltas(deltas);
                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow skeleton, out StackMachineDiagnostic skeletonDiagnostic), Is.True, skeletonDiagnostic?.message);
                    using (skeleton)
                    {
                        AssertSkeletonBoneApproximatelyEqual(animator.avatar.humanDescription, skeleton.Avatar.humanDescription, "BcpAttach_LeftUpperArm");
                    }
                }
            }
            finally
            {
                if (bcpProfile != null) Object.DestroyImmediate(bcpProfile);
                if (bcpTargetProfile != null) Object.DestroyImmediate(bcpTargetProfile);
                if (skinningProfile != null) Object.DestroyImmediate(skinningProfile);
                if (extraBoneRegistry != null) Object.DestroyImmediate(extraBoneRegistry);
                if (outfitMesh != null) Object.DestroyImmediate(outfitMesh);
                if (figureMesh != null) Object.DestroyImmediate(figureMesh);
                if (baseAvatar != null) Object.DestroyImmediate(baseAvatar);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figureRoot);
            }
        }

 #if SHAPESYNC_RICH_TEST
        [Test]
        public void ActualSpec17Fixture_CompilerSkeletonMatchesRuntimeDdbWithShoes3Bcp()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_A.asset";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject runtimeFigure = null;
            GameObject compilerFigure = null;
            GameObject backendFigure = null;
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            try
            {
                // DynamicBoneBlender is the runtime oracle. Its legacy EditMode cleanup
                // uses Object.Destroy for transient Avatars; this test verifies the
                // resulting HumanDescription, not that legacy cleanup implementation.
                LogAssert.ignoreFailingMessages = true;
                runtimeFigure = Object.Instantiate(figurePrefab);
                runtimeFigure.name = "Spec17 BCP Runtime Oracle";
                DynamicBoneBlender runtimeBlender = runtimeFigure.GetComponent<DynamicBoneBlender>();
                Animator runtimeAnimator = runtimeFigure.GetComponent<Animator>();
                MeshStackMachine runtimeMachine = runtimeFigure.GetComponent<MeshStackMachine>();
                Assert.That(runtimeBlender, Is.Not.Null);
                Assert.That(runtimeAnimator, Is.Not.Null);
                Assert.That(runtimeMachine, Is.Not.Null);

                // The runtime oracle must initialize before the Mesh transaction attaches Shoes3,
                // then observe the FBM_SET weight on its normal LateUpdate route.
                InvokePrivateInstanceMethod(runtimeBlender, "Start");
                Assert.That(runtimeMachine.TryAcceptRecipePayload(payload, out StackMachineExecutionResult runtimeResult, out StackMachineDiagnostic runtimeDiagnostic), Is.True, runtimeDiagnostic?.message ?? runtimeResult?.Diagnostic?.message);
                InvokePrivateInstanceMethod(runtimeBlender, "LateUpdate");
                Assert.That(runtimeAnimator.avatar, Is.Not.Null);
                Assert.That(runtimeAnimator.avatar.isHuman, Is.True);

                compilerFigure = Object.Instantiate(figurePrefab);
                compilerFigure.name = "Spec17 BCP Compiler Candidate";
                using (var compilerMachine = new EditModeMeshStackMachine())
                {
                    Assert.That(compilerMachine.Start(compilerFigure, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(compilerMachine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(compilerMachine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult compilerResult), Is.True);
                    using (compilerResult)
                    {
                        Assert.That(compilerResult.Skeleton, Is.Not.Null);
                        Assert.That(compilerResult.Skeleton.Avatar, Is.Not.Null);
                        Assert.That(compilerResult.Skeleton.Avatar.isHuman, Is.True);
                        AssertHumanDescriptionApproximatelyEqual(runtimeAnimator.avatar.humanDescription, compilerResult.Skeleton.Avatar.humanDescription);
                    }
                }

                // The backend promotion is a separate 17.2 boundary: it transfers the
                // resolved root, assigns the final Avatar, and hands it to Compiler Core.
                // Keep this checkpoint distinct from the raw skeleton escrow above.
                backendFigure = Object.Instantiate(figurePrefab);
                backendFigure.name = "Spec17 BCP Backend Carrier";
                using (var backendMachine = new EditModeMeshStackMachine())
                {
                    var backend = new EditModeHumanoidBuildBackend(backendMachine, null);
                    Assert.That(backend.TryBeginMeshPhase(new HumanoidBuildSource(backendFigure, payload), out StackMachineDiagnostic backendStartDiagnostic), Is.True, backendStartDiagnostic?.message);
                    Assert.That(backend.PumpMeshPhase(out MeshBuildPayload backendPayload, out StackMachineDiagnostic backendPumpDiagnostic), Is.EqualTo(HumanoidBuildPhaseStatus.Succeeded), backendPumpDiagnostic?.message);
                    try
                    {
                        Assert.That(backendPayload.Mesh, Is.Not.Null);
                        Assert.That(backendPayload.Mesh.Avatar, Is.Not.Null);
                    }
                    finally
                    {
                        backendPayload?.Dispose();
                        backend.Cancel();
                    }
                }
            }
            finally
            {
                if (backendFigure != null) Object.DestroyImmediate(backendFigure);
                if (compilerFigure != null) Object.DestroyImmediate(compilerFigure);
                if (runtimeFigure != null)
                {
                    Object.DestroyImmediate(runtimeFigure);
                }

                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void ActualSpec17DocumentA_ControllerInputMatchesPublishedAvatarBonePoseAndBindposes()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_A.asset";
            const string publishedPath = "Assets/zgock/ShapeSync/PlayTest/Spec17/Pure/DocA/DocA.prefab";
            const string controllerPath = "Assets/zgock/Assets/CC0Animation/Walking.controller";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(controller, Is.Not.Null, controllerPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject compilerFigure = null;
            GameObject published = null;
            try
            {
                compilerFigure = Object.Instantiate(figurePrefab);
                Animator sourceAnimator = compilerFigure.GetComponent<Animator>();
                Assert.That(sourceAnimator, Is.Not.Null);
                sourceAnimator.runtimeAnimatorController = controller;

                using (var machine = new EditModeMeshStackMachine())
                {
                    Assert.That(machine.Start(compilerFigure, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                    using (result)
                    {
                        published = PrefabUtility.LoadPrefabContents(publishedPath);
                        Assert.That(published, Is.Not.Null, publishedPath);
                        Animator publishedAnimator = published.GetComponent<Animator>();
                        SkinnedMeshRenderer publishedRenderer = published.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.That(publishedAnimator, Is.Not.Null);
                        Assert.That(publishedRenderer, Is.Not.Null);
                        AssertPersistedHumanoidRestPoseMatchesAvatar(published, publishedAnimator);
                        publishedAnimator.runtimeAnimatorController = controller;
                        publishedAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        published.SetActive(true);
                        publishedAnimator.Update(0f);

                        Animator actualAnimator = result.Skeleton.Animator;
                        Assert.That(actualAnimator.runtimeAnimatorController, Is.SameAs(controller));
                        AssertHumanDescriptionApproximatelyEqual(publishedAnimator.avatar.humanDescription, actualAnimator.avatar.humanDescription);
                        result.Skeleton.Root.SetActive(true);
                        actualAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        actualAnimator.Update(0f);
                        Transform expectedHips = publishedAnimator.GetBoneTransform(HumanBodyBones.Hips);
                        Transform actualHips = actualAnimator.GetBoneTransform(HumanBodyBones.Hips);
                        Assert.That(expectedHips, Is.Not.Null);
                        Assert.That(actualHips, Is.Not.Null);
                        Assert.That(Vector3.Distance(actualHips.localPosition, expectedHips.localPosition), Is.LessThan(0.0001f), "Hips local position");
                        Assert.That(Quaternion.Angle(actualHips.localRotation, expectedHips.localRotation), Is.LessThan(0.01f), "Hips local rotation");
                        Assert.That(result.FinalMesh.bindposeCount, Is.EqualTo(publishedRenderer.sharedMesh.bindposeCount));
                        for (int index = 0; index < result.FinalMesh.bindposeCount; index++)
                            AssertMatrixApproximatelyEqual(publishedRenderer.sharedMesh.bindposes[index], result.FinalMesh.bindposes[index]);
                    }
                }
            }
            finally
            {
                if (published != null) PrefabUtility.UnloadPrefabContents(published);
                if (compilerFigure != null) Object.DestroyImmediate(compilerFigure);
            }
        }

        [Test]
        public void ActualSpec17DocumentB_CompilerFinalMeshMatchesRuntimeDdbSkinnedGeometry()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject runtimeFigure = null;
            GameObject compilerFigure = null;
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                runtimeFigure = Object.Instantiate(figurePrefab);
                DynamicBoneBlender runtimeBlender = runtimeFigure.GetComponent<DynamicBoneBlender>();
                MeshStackMachine runtimeMachine = runtimeFigure.GetComponent<MeshStackMachine>();
                Animator runtimeAnimator = runtimeFigure.GetComponent<Animator>();
                SkinnedMeshRenderer runtimeFigureRenderer = runtimeFigure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(runtimeBlender, Is.Not.Null);
                Assert.That(runtimeMachine, Is.Not.Null);
                Assert.That(runtimeAnimator, Is.Not.Null);
                Assert.That(runtimeFigureRenderer, Is.Not.Null);
                DisableOptionalVrmPhysicsIntegration(runtimeFigure);
                InvokePrivateInstanceMethod(runtimeBlender, "Start");
                Assert.That(runtimeMachine.TryAcceptRecipePayload(payload, out StackMachineExecutionResult runtimeResult, out StackMachineDiagnostic runtimeDiagnostic), Is.True, runtimeDiagnostic?.message ?? runtimeResult?.Diagnostic?.message);
                runtimeAnimator.Update(0f);
                InvokePrivateInstanceMethod(runtimeBlender, "LateUpdate");

                compilerFigure = Object.Instantiate(figurePrefab);
                using (var compilerMachine = new EditModeMeshStackMachine())
                {
                    Assert.That(compilerMachine.Start(compilerFigure, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(compilerMachine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(compilerMachine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult compilerResult), Is.True);
                    using (compilerResult)
                    {
                        Assert.That(compilerResult.FinalMesh, Is.Not.Null);
                        Animator compilerAnimator = compilerResult.Skeleton.Animator;
                        Assert.That(compilerAnimator.avatar, Is.SameAs(compilerResult.Skeleton.Avatar));
                        var expected = new System.Collections.Generic.List<Vector3>();
                        AppendSkinnedVertices(runtimeFigureRenderer, runtimeFigureRenderer.transform.worldToLocalMatrix, expected);

                        SkinnedMeshRenderer compilerFigureRenderer = compilerResult.Skeleton.Root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.That(compilerFigureRenderer, Is.Not.Null);
                        Assert.That(HumanoidMeshStructureFixture.TryCreate(compilerResult, out HumanoidMeshStructureFixture structureFixture), Is.True);
                        // The Mesh escrow carries logical material slots. Candidate Materials are created by the
                        // following compiler phase, so the source renderer still has its source-material array here.
                        Assert.That(compilerResult.MaterialSlots, Has.Count.EqualTo(structureFixture.MaterialSlotCount));
                        Assert.That(HumanoidMeshStructureExpectation.TryValidateMeshEscrow(compilerResult.FinalMesh, compilerResult.MaterialSlots.Count, compilerResult.BoneTable.Bones, compilerAnimator.avatar, structureFixture.VertexCount, structureFixture.MaterialSlotCount, structureFixture.Bones, structureFixture.Bindposes, structureFixture.FinalBlendShapeNames, structureFixture.HumanBoneNames, out string structureFailure), Is.True, structureFailure);
                        var actual = new System.Collections.Generic.List<Vector3>();
                        AppendSkinnedVertexRange(compilerResult.FinalMesh, compilerResult.BoneTable.Bones, compilerFigureRenderer.transform.worldToLocalMatrix, 0, compilerResult.Sources[0].Mesh.vertexCount, actual);
                        Assert.That(actual, Has.Count.EqualTo(expected.Count));
                        const float skinningOracleTolerance = 0.0002f;
                        float maximumDistance = 0f;
                        int maximumIndex = -1;
                        for (int i = 0; i < expected.Count; i++)
                        {
                            float distance = Vector3.Distance(actual[i], expected[i]);
                            if (distance <= maximumDistance) continue;
                            maximumDistance = distance;
                            maximumIndex = i;
                        }
                        if (maximumDistance >= skinningOracleTolerance)
                            Assert.Fail("Figure skinned vertex mismatch at " + maximumIndex + ": runtime=" + expected[maximumIndex].ToString("F6") + ", compiler=" + actual[maximumIndex].ToString("F6") + ", distance=" + maximumDistance.ToString("F8") + ", tolerance=" + skinningOracleTolerance.ToString("F8") + ". " + DescribeSourceVertex(maximumIndex, new[] { runtimeFigureRenderer }, compilerResult));
                    }
                }
            }
            finally
            {
                if (compilerFigure != null) Object.DestroyImmediate(compilerFigure);
                if (runtimeFigure != null) Object.DestroyImmediate(runtimeFigure);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void ActualSpec17DocumentB_PersistedPrefabFinalMeshMatchesRuntimeDdbSkinnedGeometry()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            string parentFolder = ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17");
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            string folder = parentFolder + "/__Spec17_DocumentBPrefabSkinning_" + System.Guid.NewGuid().ToString("N");
            GameObject runtimeFigure = null;
            GameObject compilerFigure = null;
            GameObject candidate = null;
            GameObject contents = null;
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Assert.That(AssetDatabase.CreateFolder(parentFolder, folder.Substring(folder.LastIndexOf('/') + 1)), Is.Not.Empty);
                runtimeFigure = Object.Instantiate(figurePrefab);
                DynamicBoneBlender runtimeBlender = runtimeFigure.GetComponent<DynamicBoneBlender>();
                MeshStackMachine runtimeMachine = runtimeFigure.GetComponent<MeshStackMachine>();
                Animator runtimeAnimator = runtimeFigure.GetComponent<Animator>();
                SkinnedMeshRenderer runtimeFigureRenderer = runtimeFigure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(runtimeBlender, Is.Not.Null);
                Assert.That(runtimeMachine, Is.Not.Null);
                Assert.That(runtimeAnimator, Is.Not.Null);
                Assert.That(runtimeFigureRenderer, Is.Not.Null);
                DisableOptionalVrmPhysicsIntegration(runtimeFigure);
                InvokePrivateInstanceMethod(runtimeBlender, "Start");
                Assert.That(runtimeMachine.TryAcceptRecipePayload(payload, out StackMachineExecutionResult runtimeResult, out StackMachineDiagnostic runtimeDiagnostic), Is.True, runtimeDiagnostic?.message ?? runtimeResult?.Diagnostic?.message);
                runtimeAnimator.Update(0f);
                InvokePrivateInstanceMethod(runtimeBlender, "LateUpdate");

                compilerFigure = Object.Instantiate(figurePrefab);
                using (var compilerMachine = new EditModeMeshStackMachine())
                {
                    Assert.That(compilerMachine.Start(compilerFigure, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(compilerMachine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(compilerMachine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult compilerResult), Is.True);
                    using (compilerResult)
                    {
                        candidate = compilerResult.Skeleton.DetachRoot();
                        Avatar avatar = compilerResult.Skeleton.DetachAvatar();
                        Assert.That(candidate, Is.Not.Null);
                        Assert.That(avatar, Is.Not.Null);
                        Mesh mesh = compilerResult.FinalMesh;
                        Assert.That(mesh, Is.Not.Null);
                        AssetDatabase.CreateAsset(mesh, folder + "/DocumentB.asset");
                        AssetDatabase.CreateAsset(avatar, folder + "/DocumentB_avatar.asset");
                        var materials = new Material[mesh.subMeshCount];
                        for (int i = 0; i < materials.Length; i++)
                        {
                            materials[i] = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                            AssetDatabase.CreateAsset(materials[i], folder + "/DocumentB_" + i + ".mat");
                        }
                        // This is the 17.2 carrier handoff performed by
                        // EditModeHumanoidBuildBackend.TryDetachResolvedHumanoid: the resolved
                        // hierarchy receives the final Mesh and final bone table before 17.6
                        // replaces persistent asset references.
                        SkinnedMeshRenderer candidateRenderer = candidate.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.That(candidateRenderer, Is.Not.Null);
                        candidateRenderer.sharedMesh = mesh;
                        candidateRenderer.bones = compilerResult.BoneTable.Bones;
                        var stage = new HumanoidIndividualAssetStage(mesh, avatar, materials, System.Array.Empty<Texture2D>(), System.Array.Empty<string>());
                        Assert.That(HumanoidCandidateAssetApplier.TryApply(candidate, stage, out StackMachineDiagnostic applyDiagnostic), Is.True, applyDiagnostic?.message);
                        Assert.That(HumanoidPureHumanoidComponentStripper.TryNormalize(candidate, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
                        SetSaveable(candidate.transform);
                        string prefabPath = folder + "/DocumentB.prefab";
                        Assert.That(PrefabUtility.SaveAsPrefabAsset(candidate, prefabPath), Is.Not.Null);
                        AssetDatabase.SaveAssets();
                        contents = PrefabUtility.LoadPrefabContents(prefabPath);
                        Assert.That(contents, Is.Not.Null, "Persisted Document B Prefab contents must reload.");
                        SkinnedMeshRenderer persistedRenderer = contents.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.That(persistedRenderer, Is.Not.Null);

                        var expected = new System.Collections.Generic.List<Vector3>();
                        AppendSkinnedVertices(runtimeFigureRenderer, runtimeFigureRenderer.transform.worldToLocalMatrix, expected);
                        var actual = new System.Collections.Generic.List<Vector3>();
                        AppendSkinnedVertexRange(persistedRenderer.sharedMesh, persistedRenderer.bones, persistedRenderer.transform.worldToLocalMatrix, 0, compilerResult.Sources[0].Mesh.vertexCount, actual);
                        Assert.That(actual, Has.Count.EqualTo(expected.Count));
                        AssertSkinnedGeometryMatches("persisted Document B Prefab Figure", expected, actual, new[] { runtimeFigureRenderer }, compilerResult);
                    }
                }
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                if (candidate != null) Object.DestroyImmediate(candidate);
                if (compilerFigure != null) Object.DestroyImmediate(compilerFigure);
                if (runtimeFigure != null) Object.DestroyImmediate(runtimeFigure);
                AssetDatabase.DeleteAsset(folder);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [TestCase(
            "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_A.asset",
            "Assets/zgock/ShapeSync/PlayTest/Spec17/Pure/DocA/DocA.prefab")]
        [TestCase(
            "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset",
            "Assets/zgock/ShapeSync/PlayTest/Spec17/Pure/DocB/DocB.prefab")]
        public void ActualSpec17FinalPublishedPrefab_MatchesPureCompilerMeshOracle(
            string documentPath,
            string prefabPath)
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject compilerFigure = null;
            GameObject contents = null;
            try
            {
                compilerFigure = Object.Instantiate(figurePrefab);
                compilerFigure.name = "Spec17 Pure Compiler Oracle";
                using (var compilerMachine = new EditModeMeshStackMachine(null, true))
                {
                    Assert.That(compilerMachine.Start(compilerFigure, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(compilerMachine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(compilerMachine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult compilerResult), Is.True);
                    using (compilerResult)
                    {
                        Assert.That(compilerResult.FinalMesh, Is.Not.Null);
                        Assert.That(compilerResult.Skeleton, Is.Not.Null);
                        Assert.That(compilerResult.Skeleton.Avatar, Is.Not.Null);
                        Assert.That(compilerResult.BoneTable, Is.Not.Null);

                        contents = PrefabUtility.LoadPrefabContents(prefabPath);
                        Assert.That(contents, Is.Not.Null, "Final publish Prefab must exist and reload: " + prefabPath);
                        Animator persistedAnimator = contents.GetComponent<Animator>();
                        SkinnedMeshRenderer persistedRenderer = contents.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        Assert.That(persistedAnimator, Is.Not.Null, "Final publish Prefab must retain its Animator: " + prefabPath);
                        Assert.That(persistedAnimator.avatar, Is.Not.Null, "Final publish Prefab must retain its rebuilt Avatar: " + prefabPath);
                        Assert.That(persistedRenderer, Is.Not.Null, "Final publish Prefab must retain its combined SkinnedMeshRenderer: " + prefabPath);
                        Assert.That(persistedRenderer.sharedMesh, Is.Not.Null, "Final publish Prefab must retain its combined Mesh: " + prefabPath);
                        Assert.That(persistedRenderer.bones, Is.Not.Empty, "Final publish Prefab must retain its resolved bone table: " + prefabPath);

                        // These Preview11 fixtures are Pure Humanoid outputs. Their persisted
                        // hierarchy must agree with the Avatar rest skeleton and with the same
                        // resolved compiler output; a runtime DDB physical-pose oracle is a
                        // different contract and is covered by the preceding Document B test.
                        AssertPersistedHumanoidRestPoseMatchesAvatar(contents, persistedAnimator);
                        AssertHumanDescriptionApproximatelyEqual(compilerResult.Skeleton.Avatar.humanDescription, persistedAnimator.avatar.humanDescription);
                        AssertPublishedMeshMatchesCompilerResult(compilerResult, persistedRenderer, prefabPath);
                    }
                }
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                if (compilerFigure != null) Object.DestroyImmediate(compilerFigure);
            }
        }

        private static void AssertPublishedMeshMatchesCompilerResult(HumanoidMeshFbmBakeResult compilerResult, SkinnedMeshRenderer persistedRenderer, string subject)
        {
            Mesh expected = compilerResult.FinalMesh;
            Mesh actual = persistedRenderer.sharedMesh;
            Assert.That(actual.vertexCount, Is.EqualTo(expected.vertexCount), subject + " vertex count");
            Assert.That(actual.subMeshCount, Is.EqualTo(expected.subMeshCount), subject + " submesh count");
            Assert.That(actual.blendShapeCount, Is.EqualTo(expected.blendShapeCount), subject + " blendshape count");
            AssertVerticesApproximatelyEqual(expected.vertices, actual.vertices);
            Assert.That(actual.boneWeights, Is.EqualTo(expected.boneWeights), subject + " bone weights");
            Assert.That(actual.triangles, Is.EqualTo(expected.triangles), subject + " triangles");
            Assert.That(actual.bindposes, Has.Length.EqualTo(expected.bindposes.Length), subject + " bindpose count");
            for (int i = 0; i < expected.bindposes.Length; i++) AssertMatrixApproximatelyEqual(expected.bindposes[i], actual.bindposes[i]);
            Assert.That(persistedRenderer.bones, Has.Length.EqualTo(compilerResult.BoneTable.Bones.Length), subject + " bone table count");
            for (int i = 0; i < compilerResult.BoneTable.Bones.Length; i++)
            {
                string expectedPath = GetRelativePath(compilerResult.Skeleton.Root.transform, compilerResult.BoneTable.Bones[i]);
                string actualPath = GetRelativePath(persistedRenderer.transform.root, persistedRenderer.bones[i]);
                Assert.That(actualPath, Is.EqualTo(expectedPath), subject + " bone[" + i + "] path");
            }
            for (int shape = 0; shape < expected.blendShapeCount; shape++)
            {
                Assert.That(actual.GetBlendShapeName(shape), Is.EqualTo(expected.GetBlendShapeName(shape)), subject + " blendshape[" + shape + "] name");
                int frameCount = expected.GetBlendShapeFrameCount(shape);
                Assert.That(actual.GetBlendShapeFrameCount(shape), Is.EqualTo(frameCount), subject + " blendshape[" + shape + "] frame count");
                for (int frame = 0; frame < frameCount; frame++)
                {
                    Assert.That(actual.GetBlendShapeFrameWeight(shape, frame), Is.EqualTo(expected.GetBlendShapeFrameWeight(shape, frame)).Within(0.0001f), subject + " blendshape frame weight");
                    var expectedDelta = new Vector3[expected.vertexCount];
                    var actualDelta = new Vector3[actual.vertexCount];
                    expected.GetBlendShapeFrameVertices(shape, frame, expectedDelta, null, null);
                    actual.GetBlendShapeFrameVertices(shape, frame, actualDelta, null, null);
                    AssertVerticesApproximatelyEqual(expectedDelta, actualDelta);
                }
            }
        }

        /// <summary>
        /// The Spec17 mesh oracle is DynamicBoneBlender plus the runtime Mesh recipe only.
        /// Optional VRM outfit physics belongs to Spec17.5/17.6 transport and must not alter
        /// the source geometry against which the compiler's static Mesh is compared.
        /// </summary>
        private static void DisableOptionalVrmPhysicsIntegration(GameObject figureRoot)
        {
            Assert.That(figureRoot, Is.Not.Null);
            MonoBehaviour[] behaviours = figureRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is IShapeSyncOptionalVrmIntegration) behaviour.enabled = false;
            }

            Assert.That(
                ShapeSyncOptionalVrmIntegrationRegistry.TryGet(figureRoot, out _),
                Is.False,
                "Mesh Oracle must not transfer or reconstruct optional VRM Physics.");
        }

        private static void AssertSkinnedGeometryMatches(string subject, System.Collections.Generic.IReadOnlyList<Vector3> expected, System.Collections.Generic.IReadOnlyList<Vector3> actual, SkinnedMeshRenderer[] runtimeRenderers, HumanoidMeshFbmBakeResult compilerResult)
        {
            const float skinningOracleTolerance = 0.0002f;
            float maximumDistance = 0f;
            int maximumIndex = -1;
            for (int i = 0; i < expected.Count; i++)
            {
                float distance = Vector3.Distance(actual[i], expected[i]);
                if (distance <= maximumDistance) continue;
                maximumDistance = distance;
                maximumIndex = i;
            }
            if (maximumDistance >= skinningOracleTolerance)
                Assert.Fail(subject + " skinned vertex mismatch at " + maximumIndex + ": runtime=" + expected[maximumIndex].ToString("F6") + ", compiler=" + actual[maximumIndex].ToString("F6") + ", distance=" + maximumDistance.ToString("F8") + ", tolerance=" + skinningOracleTolerance.ToString("F8") + ".");
        }

        private static void SetSaveable(Transform root)
        {
            root.gameObject.hideFlags = HideFlags.None;
            for (int i = 0; i < root.childCount; i++) SetSaveable(root.GetChild(i));
        }

        private static void AppendSkinnedVertices(SkinnedMeshRenderer renderer, Matrix4x4 outputWorldToLocal, System.Collections.Generic.List<Vector3> destination)
        {
            Assert.That(renderer, Is.Not.Null);
            Mesh mesh = renderer.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            // bone.localToWorldMatrix * bindpose already produces a world-space
            // skinned point.  Do not apply renderer.localToWorldMatrix twice.
            AppendSkinnedVertices(GetRendererDeformedVertices(renderer), mesh, renderer.bones, outputWorldToLocal, destination);
        }

        private static void AppendSkinnedVertices(Mesh mesh, Transform[] bones, Matrix4x4 meshToOutput, System.Collections.Generic.List<Vector3> destination)
            => AppendSkinnedVertices(mesh.vertices, mesh, bones, meshToOutput, destination);

        // Runtime DDB is the exact end-to-end oracle for the Figure range. Attached
        // Outfits intentionally differ at this boundary: runtime keeps their FBM as a
        // renderer BlendShape route whereas Spec17 bakes and remaps them into the
        // Figure table. Those paths are covered by their dedicated remap and variant
        // whitebox tests rather than by a false renderer-to-final-table comparison.
        private static void AppendSkinnedVertexRange(Mesh mesh, Transform[] bones, Matrix4x4 meshToOutput, int offset, int count, System.Collections.Generic.List<Vector3> destination)
        {
            Assert.That(mesh, Is.Not.Null);
            Assert.That(bones, Is.Not.Null);
            Assert.That(mesh.bindposes, Has.Length.EqualTo(bones.Length));
            Assert.That(offset, Is.GreaterThanOrEqualTo(0));
            Assert.That(count, Is.GreaterThanOrEqualTo(0));
            Assert.That(offset + count, Is.LessThanOrEqualTo(mesh.vertexCount));
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = offset; i < offset + count; i++)
                destination.Add(meshToOutput.MultiplyPoint3x4(EvaluateSkinnedVertex(vertices[i], weights[i], mesh.bindposes, bones)));
        }

        private static void AppendSkinnedVertices(Vector3[] vertices, Mesh mesh, Transform[] bones, Matrix4x4 meshToOutput, System.Collections.Generic.List<Vector3> destination)
        {
            Assert.That(mesh, Is.Not.Null);
            Assert.That(bones, Is.Not.Null);
            Assert.That(mesh.bindposes, Has.Length.EqualTo(bones.Length));
            BoneWeight[] weights = mesh.boneWeights;
            Assert.That(weights, Has.Length.EqualTo(vertices.Length));
            for (int i = 0; i < vertices.Length; i++)
                destination.Add(meshToOutput.MultiplyPoint3x4(EvaluateSkinnedVertex(vertices[i], weights[i], mesh.bindposes, bones)));
        }

        private static Vector3[] GetRendererDeformedVertices(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) <= Mathf.Epsilon) continue;
                Vector3[] delta = GetBlendShapeDelta(mesh, shape, weight);
                for (int vertex = 0; vertex < vertices.Length; vertex++) vertices[vertex] += delta[vertex];
            }
            return vertices;
        }

        private static Vector3[] GetBlendShapeDelta(Mesh mesh, int shape, float weight)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shape);
            Assert.That(frameCount, Is.GreaterThan(0));
            var result = new Vector3[mesh.vertexCount];
            var lower = new Vector3[mesh.vertexCount];
            var upper = new Vector3[mesh.vertexCount];
            float firstWeight = mesh.GetBlendShapeFrameWeight(shape, 0);
            if (weight <= firstWeight)
            {
                mesh.GetBlendShapeFrameVertices(shape, 0, upper, null, null);
                float scale = Mathf.Approximately(firstWeight, 0f) ? 0f : weight / firstWeight;
                for (int i = 0; i < result.Length; i++) result[i] = upper[i] * scale;
                return result;
            }
            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shape, frame);
                if (weight > upperWeight) continue;
                mesh.GetBlendShapeFrameVertices(shape, frame - 1, lower, null, null);
                mesh.GetBlendShapeFrameVertices(shape, frame, upper, null, null);
                float lowerWeight = mesh.GetBlendShapeFrameWeight(shape, frame - 1);
                float ratio = Mathf.Approximately(upperWeight, lowerWeight) ? 0f : (weight - lowerWeight) / (upperWeight - lowerWeight);
                for (int i = 0; i < result.Length; i++) result[i] = Vector3.LerpUnclamped(lower[i], upper[i], ratio);
                return result;
            }
            mesh.GetBlendShapeFrameVertices(shape, frameCount - 1, lower, null, null);
            float lastWeight = mesh.GetBlendShapeFrameWeight(shape, frameCount - 1);
            float finalScale = Mathf.Approximately(lastWeight, 0f) ? 0f : weight / lastWeight;
            for (int i = 0; i < result.Length; i++) result[i] = lower[i] * finalScale;
            return result;
        }

        private static Vector3 EvaluateSkinnedVertex(Vector3 vertex, BoneWeight weight, Matrix4x4[] bindposes, Transform[] bones)
        {
            Vector3 result = Vector3.zero;
            Apply(weight.boneIndex0, weight.weight0);
            Apply(weight.boneIndex1, weight.weight1);
            Apply(weight.boneIndex2, weight.weight2);
            Apply(weight.boneIndex3, weight.weight3);
            return result;

            void Apply(int index, float value)
            {
                if (value <= 0f) return;
                Assert.That(index, Is.GreaterThanOrEqualTo(0).And.LessThan(bones.Length));
                Assert.That(bones[index], Is.Not.Null);
                result += (bones[index].localToWorldMatrix * bindposes[index]).MultiplyPoint3x4(vertex) * value;
            }
        }

        private static string DescribeFirstSourceVertex(SkinnedMeshRenderer runtimeRenderer, HumanoidMeshFbmBakeResult compilerResult)
        {
            Mesh runtimeMesh = runtimeRenderer.sharedMesh;
            Mesh compilerSource = compilerResult.Sources[0].Mesh;
            Mesh final = compilerResult.FinalMesh;
            BoneWeight runtimeWeight = runtimeMesh.boneWeights[0];
            BoneWeight compilerSourceWeight = compilerSource.boneWeights[0];
            BoneWeight finalWeight = final.boneWeights[0];
            Vector3 runtimeVertex = GetRendererDeformedVertices(runtimeRenderer)[0];
            Vector3 compilerSourceVertex = compilerSource.vertices[0];
            Vector3 finalVertex = final.vertices[0];
            return "raw runtime=" + runtimeVertex + ", compilerSource=" + compilerSourceVertex + ", final=" + finalVertex
                + "; weights runtime=" + runtimeWeight.boneIndex0 + "/" + runtimeWeight.weight0 + ", compilerSource=" + compilerSourceWeight.boneIndex0 + "/" + compilerSourceWeight.weight0 + ", final=" + finalWeight.boneIndex0 + "/" + finalWeight.weight0
                + "; bindpose runtime=" + runtimeMesh.bindposes[runtimeWeight.boneIndex0] + ", final=" + final.bindposes[finalWeight.boneIndex0]
                + "; bone runtime=" + GetTransformPath(runtimeRenderer.bones[runtimeWeight.boneIndex0]) + " local=" + runtimeRenderer.bones[runtimeWeight.boneIndex0].localPosition + " world=" + runtimeRenderer.bones[runtimeWeight.boneIndex0].position
                + ", final=" + GetTransformPath(compilerResult.BoneTable.Bones[finalWeight.boneIndex0]) + " local=" + compilerResult.BoneTable.Bones[finalWeight.boneIndex0].localPosition + " world=" + compilerResult.BoneTable.Bones[finalWeight.boneIndex0].position
                + "; runtime chain=" + GetTransformChain(runtimeRenderer.bones[runtimeWeight.boneIndex0])
                + "; final chain=" + GetTransformChain(compilerResult.BoneTable.Bones[finalWeight.boneIndex0]) + ".";
        }

        private static string DescribeSourceVertex(int combinedIndex, SkinnedMeshRenderer[] runtimeRenderers, HumanoidMeshFbmBakeResult compilerResult)
        {
            int sourceIndex = 0;
            int localIndex = combinedIndex;
            for (; sourceIndex < runtimeRenderers.Length; sourceIndex++)
            {
                int count = runtimeRenderers[sourceIndex].sharedMesh.vertexCount;
                if (localIndex < count) break;
                localIndex -= count;
            }
            if (sourceIndex >= runtimeRenderers.Length) return "Vertex source was outside the combined source range.";
            SkinnedMeshRenderer runtimeRenderer = runtimeRenderers[sourceIndex];
            HumanoidMeshFbmBakedSource compilerSource = compilerResult.Sources[sourceIndex];
            Mesh runtimeMesh = runtimeRenderer.sharedMesh;
            BoneWeight runtimeWeight = runtimeMesh.boneWeights[localIndex];
            BoneWeight compilerWeight = compilerSource.Mesh.boneWeights[localIndex];
            BoneWeight finalWeight = compilerResult.FinalMesh.boneWeights[combinedIndex];
            int finalBoneIndex = finalWeight.boneIndex0;
            return "source=" + sourceIndex + " logical=" + compilerSource.Source.LogicalName + " localVertex=" + localIndex
                + "; runtimeRenderer=" + GetTransformPath(runtimeRenderer.transform)
                + "; raw runtime=" + GetRendererDeformedVertices(runtimeRenderer)[localIndex] + ", compilerSource=" + compilerSource.Mesh.vertices[localIndex] + ", final=" + compilerResult.FinalMesh.vertices[combinedIndex]
                + "; weight runtime=" + runtimeWeight.boneIndex0 + "/" + runtimeWeight.weight0 + ", compiler=" + compilerWeight.boneIndex0 + "/" + compilerWeight.weight0
                + "; allWeights runtime=" + DescribeWeights(runtimeWeight, runtimeMesh.bindposes) + "; compiler=" + DescribeWeights(compilerWeight, compilerSource.Mesh.bindposes) + "; final=" + DescribeFinalWeights(finalWeight, compilerResult) + "; extra=" + DescribeExtraBoneMembership(compilerWeight, compilerSource, compilerResult)
                + "; pairedBones=" + DescribePairedBones(runtimeWeight, runtimeRenderer, compilerWeight, compilerSource, compilerResult)
                + "; correctionOracle=" + DescribeCorrectionOracle(runtimeWeight, runtimeRenderer, finalWeight, compilerResult.FinalMesh, compilerResult.BoneTable, compilerSource.Mesh.vertices[localIndex])
                + "; bone runtime=" + GetTransformPath(runtimeRenderer.bones[runtimeWeight.boneIndex0]) + ", final=" + GetTransformPath(compilerResult.BoneTable.Bones[finalBoneIndex])
                + "; boneMatrix runtime=" + runtimeRenderer.bones[runtimeWeight.boneIndex0].localToWorldMatrix + "; final=" + compilerResult.BoneTable.Bones[finalBoneIndex].localToWorldMatrix
                + "; runtime chain=" + GetTransformChain(runtimeRenderer.bones[runtimeWeight.boneIndex0])
                + "; final chain=" + GetTransformChain(compilerResult.BoneTable.Bones[finalBoneIndex])
                + "; runtime bindpose=" + runtimeMesh.bindposes[runtimeWeight.boneIndex0]
                + "; final bindpose=" + compilerResult.FinalMesh.bindposes[finalBoneIndex]
                + "; runtime Figure bindpose=" + DescribeFigureBindpose(runtimeRenderers[0], runtimeRenderer.bones[runtimeWeight.boneIndex0])
                + "; runtime activeShapes=" + DescribeActiveBlendShapes(runtimeRenderer)
                + "; runtime FBM=" + DescribeRuntimeFbmWeights(runtimeRenderers[0] == null ? null : runtimeRenderers[0].GetComponentInParent<DynamicBoneBlender>())
                + "; compiler FBM=" + string.Join(",", compilerResult.FbmWeights)
                + "; compiler BCP=" + DescribeBcpDeltas(compilerResult.BcpDeltas)
                + "; Hips runtimeAvatar=" + DescribeAvatarSkeleton(runtimeRenderers[0] == null ? null : runtimeRenderers[0].GetComponentInParent<Animator>()?.avatar, "J_Bip_C_Hips")
                + ", compilerAvatar=" + DescribeAvatarSkeleton(compilerResult.Skeleton?.Avatar, "J_Bip_C_Hips")
                + ", compilerTransform=" + DescribeTransformByName(compilerResult.Skeleton?.Root, "J_Bip_C_Hips")
                + "; profile=" + DescribeProfileBindposes(compilerSource, compilerWeight)
                + "; runtime sourceToFigure=" + (runtimeRenderers[0].transform.worldToLocalMatrix * runtimeRenderer.transform.localToWorldMatrix)
                + "; compiler sourceToFigure=" + DescribeCompilerSourceToFigure(compilerResult, compilerSource) + ".";
        }

        private static string DescribeAvatarSkeleton(Avatar avatar, string boneName)
        {
            if (avatar == null || !avatar.isHuman || string.IsNullOrEmpty(boneName)) return "<none>";
            SkeletonBone[] skeleton = avatar.humanDescription.skeleton;
            if (skeleton == null) return "<none>";
            for (int i = 0; i < skeleton.Length; i++)
                if (skeleton[i].name == boneName) return skeleton[i].position.ToString("F6") + "/" + skeleton[i].rotation.ToString("F6");
            return "<missing>";
        }

        private static string DescribeTransformByName(GameObject root, string boneName)
        {
            Transform transform = root == null ? null : FindDescendantByName(root.transform, boneName);
            return transform == null ? "<missing>" : transform.localPosition.ToString("F6") + "/" + transform.localRotation.ToString("F6");
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            return null;
        }

        private static Matrix4x4 DescribeCompilerSourceToFigure(HumanoidMeshFbmBakeResult result, HumanoidMeshFbmBakedSource source)
        {
            SkinnedMeshRenderer figureRenderer = result.LogicalPlan.Figure.Renderer;
            if (source.Source.Outfit == null) return Matrix4x4.identity;
            return figureRenderer.worldToLocalMatrix * result.LogicalPlan.Figure.Root.transform.localToWorldMatrix
                * source.Source.Root.transform.worldToLocalMatrix * source.Source.Renderer.transform.localToWorldMatrix;
        }

        private static string DescribeActiveBlendShapes(SkinnedMeshRenderer renderer)
        {
            var values = new System.Collections.Generic.List<string>();
            Mesh mesh = renderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = renderer.GetBlendShapeWeight(i);
                if (Mathf.Abs(weight) > Mathf.Epsilon) values.Add(mesh.GetBlendShapeName(i) + "=" + weight.ToString("F3"));
            }
            return string.Join(",", values);
        }

        private static string DescribeRuntimeFbmWeights(DynamicBoneBlender blender)
        {
            if (blender == null || blender.Targets == null) return "<none>";
            var values = new System.Collections.Generic.List<string>();
            for (int i = 0; i < blender.Targets.Count; i++)
            {
                DynamicBoneBlendTarget target = blender.Targets[i];
                if (target != null) values.Add(target.blendName + "=" + target.weight.ToString("F3") + "/" + target.enabled);
            }
            return string.Join(",", values);
        }

        private static string DescribeBcpDeltas(System.Collections.Generic.IReadOnlyList<HumanoidMeshBcpDelta> deltas)
        {
            if (deltas == null || deltas.Count == 0) return "<none>";
            var values = new System.Collections.Generic.List<string>(deltas.Count);
            for (int i = 0; i < deltas.Count; i++) values.Add(deltas[i].Bone + "=" + deltas[i].Position);
            return string.Join(",", values);
        }

        private static string DescribeProfileBindposes(HumanoidMeshFbmBakedSource source, BoneWeight weight)
        {
            OutfitSkinningProfile profile = source.Source.Outfit == null ? null : source.Source.Outfit.SkinningProfile;
            string path = GetProfileRendererPath(source.Source.Root == null ? null : source.Source.Root.transform, source.Source.Renderer == null ? null : source.Source.Renderer.transform);
            if (profile == null || string.IsNullOrEmpty(path) || !profile.TryGetRenderer(path, out OutfitSkinningRendererProfile renderer) || renderer.baseBindposes == null) return "<none>";
            return "path=" + path + "; "
                + Describe(weight.boneIndex0, weight.weight0) + ","
                + Describe(weight.boneIndex1, weight.weight1) + ","
                + Describe(weight.boneIndex2, weight.weight2) + ","
                + Describe(weight.boneIndex3, weight.weight3);

            string Describe(int index, float value)
            {
                if (value <= 0f || index < 0 || index >= renderer.baseBindposes.Length) return "-";
                Matrix4x4 target = default;
                if (renderer.fbmBindposes != null)
                    for (int i = 0; i < renderer.fbmBindposes.Count; i++)
                        if (renderer.fbmBindposes[i] != null && renderer.fbmBindposes[i].blendName == "BasicGirl" && renderer.fbmBindposes[i].bindposes != null && index < renderer.fbmBindposes[i].bindposes.Length)
                            target = renderer.fbmBindposes[i].bindposes[index];
                return index + "/base=" + renderer.baseBindposes[index] + "/target=" + target;
            }
        }

        private static string GetProfileRendererPath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent) parts.Add(current.name);
            if (parts.Count == 0) return null;
            Transform check = target;
            while (check != null && check != root) check = check.parent;
            if (check != root) return null;
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string DescribeFigureBindpose(SkinnedMeshRenderer figureRenderer, Transform bone)
        {
            if (figureRenderer == null || figureRenderer.sharedMesh == null || figureRenderer.bones == null) return "<none>";
            for (int i = 0; i < figureRenderer.bones.Length; i++)
                if (figureRenderer.bones[i] == bone && i < figureRenderer.sharedMesh.bindposes.Length) return figureRenderer.sharedMesh.bindposes[i].ToString();
            return "<unmapped>";
        }

        private static string DescribeWeights(BoneWeight weight, Matrix4x4[] bindposes)
        {
            return Describe(weight.boneIndex0, weight.weight0) + "," + Describe(weight.boneIndex1, weight.weight1) + "," + Describe(weight.boneIndex2, weight.weight2) + "," + Describe(weight.boneIndex3, weight.weight3);
            string Describe(int index, float value) => value <= 0f ? "-" : index + "/" + value.ToString("F6") + "/" + bindposes[index].ToString();
        }

        private static string DescribeExtraBoneMembership(BoneWeight weight, HumanoidMeshFbmBakedSource source, HumanoidMeshFbmBakeResult result)
        {
            Transform[] bones = source.Source.Renderer == null ? null : source.Source.Renderer.bones;
            return Describe(weight.boneIndex0, weight.weight0) + "," + Describe(weight.boneIndex1, weight.weight1) + "," + Describe(weight.boneIndex2, weight.weight2) + "," + Describe(weight.boneIndex3, weight.weight3);
            string Describe(int index, float value) => value <= 0f || bones == null || index < 0 || index >= bones.Length ? "-" : index + "=" + result.ExtraBoneTransforms.ContainsKey(bones[index]);
        }

        private static string DescribeFinalWeights(BoneWeight weight, HumanoidMeshFbmBakeResult result)
        {
            return Describe(weight.boneIndex0, weight.weight0) + "," + Describe(weight.boneIndex1, weight.weight1) + "," + Describe(weight.boneIndex2, weight.weight2) + "," + Describe(weight.boneIndex3, weight.weight3);
            string Describe(int index, float value)
            {
                if (value <= 0f || index < 0 || result.BoneTable == null || index >= result.BoneTable.Bones.Length || index >= result.FinalMesh.bindposes.Length) return "-";
                return index + "/" + value.ToString("F6") + "/" + GetTransformPath(result.BoneTable.Bones[index]) + "/" + result.FinalMesh.bindposes[index];
            }
        }

        private static string DescribePairedBones(BoneWeight runtimeWeight, SkinnedMeshRenderer runtimeRenderer, BoneWeight compilerWeight, HumanoidMeshFbmBakedSource compilerSource, HumanoidMeshFbmBakeResult result)
        {
            return Describe(runtimeWeight.boneIndex0, runtimeWeight.weight0, compilerWeight.boneIndex0, compilerWeight.weight0) + ","
                + Describe(runtimeWeight.boneIndex1, runtimeWeight.weight1, compilerWeight.boneIndex1, compilerWeight.weight1) + ","
                + Describe(runtimeWeight.boneIndex2, runtimeWeight.weight2, compilerWeight.boneIndex2, compilerWeight.weight2) + ","
                + Describe(runtimeWeight.boneIndex3, runtimeWeight.weight3, compilerWeight.boneIndex3, compilerWeight.weight3);

            string Describe(int runtimeIndex, float runtimeValue, int compilerIndex, float compilerValue)
            {
                if (runtimeValue <= 0f || compilerValue <= 0f || runtimeRenderer.bones == null || compilerSource.Source.Renderer == null || compilerSource.Source.Renderer.bones == null
                    || runtimeIndex < 0 || runtimeIndex >= runtimeRenderer.bones.Length || compilerIndex < 0 || compilerIndex >= compilerSource.Source.Renderer.bones.Length) return "-";
                Transform sourceBone = compilerSource.Source.Renderer.bones[compilerIndex];
                if (!result.ExtraBoneTransforms.TryGetValue(sourceBone, out Transform finalBone) || finalBone == null)
                {
                    string path = GetProfileRendererPath(compilerSource.Source.Root == null ? null : compilerSource.Source.Root.transform, sourceBone);
                    finalBone = string.IsNullOrEmpty(path) ? result.Skeleton.Root.transform : result.Skeleton.Root.transform.Find(path);
                }
                return runtimeIndex + ":" + GetTransformPath(runtimeRenderer.bones[runtimeIndex]) + "/" + runtimeRenderer.bones[runtimeIndex].localToWorldMatrix
                    + " => " + compilerIndex + ":" + GetTransformPath(finalBone) + "/" + (finalBone == null ? Matrix4x4.zero : finalBone.localToWorldMatrix);
            }
        }

        private static string DescribeCorrectionOracle(BoneWeight runtimeWeight, SkinnedMeshRenderer runtimeRenderer, BoneWeight finalWeight, Mesh finalMesh, HumanoidMeshBoneTable table, Vector3 sourceVertex)
        {
            Matrix4x4 source = Matrix4x4.zero;
            Matrix4x4 destination = Matrix4x4.zero;
            Add(runtimeWeight.boneIndex0, runtimeWeight.weight0, finalWeight.boneIndex0, finalWeight.weight0);
            Add(runtimeWeight.boneIndex1, runtimeWeight.weight1, finalWeight.boneIndex1, finalWeight.weight1);
            Add(runtimeWeight.boneIndex2, runtimeWeight.weight2, finalWeight.boneIndex2, finalWeight.weight2);
            Add(runtimeWeight.boneIndex3, runtimeWeight.weight3, finalWeight.boneIndex3, finalWeight.weight3);
            if (Mathf.Abs(destination.determinant) <= 0.0000001f) return "singular";
            Vector3 corrected = (destination.inverse * source).MultiplyPoint3x4(sourceVertex);
            Vector3 predicted = EvaluateSkin(finalWeight, finalMesh.bindposes, table.Bones, corrected);
            return "corrected=" + corrected + "; predicted=" + predicted + "; source=" + source + "; destination=" + destination;

            void Add(int runtimeIndex, float runtimeValue, int finalIndex, float finalValue)
            {
                if (runtimeValue <= 0f || finalValue <= 0f) return;
                Matrix4x4 runtimeMatrix = runtimeRenderer.bones[runtimeIndex].localToWorldMatrix * runtimeRenderer.sharedMesh.bindposes[runtimeIndex];
                Matrix4x4 finalMatrix = table.Bones[finalIndex].localToWorldMatrix * finalMesh.bindposes[finalIndex];
                for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                {
                    source[row, column] += runtimeMatrix[row, column] * runtimeValue;
                    destination[row, column] += finalMatrix[row, column] * finalValue;
                }
            }
        }

        private static Vector3 EvaluateSkin(BoneWeight weight, Matrix4x4[] bindposes, Transform[] bones, Vector3 vertex)
        {
            Vector3 output = Vector3.zero;
            Add(weight.boneIndex0, weight.weight0);
            Add(weight.boneIndex1, weight.weight1);
            Add(weight.boneIndex2, weight.weight2);
            Add(weight.boneIndex3, weight.weight3);
            return output;

            void Add(int index, float value)
            {
                if (value <= 0f) return;
                output += (bones[index].localToWorldMatrix * bindposes[index]).MultiplyPoint3x4(vertex) * value;
            }
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null) return "<null>";
            var segments = new System.Collections.Generic.List<string>();
            for (Transform current = transform; current != null; current = current.parent) segments.Add(current.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string GetTransformChain(Transform transform)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (Transform current = transform; current != null; current = current.parent)
                parts.Add(current.name + "={p=" + current.localPosition + ",r=" + current.localRotation + ",s=" + current.localScale + "}");
            parts.Reverse();
            return string.Join(" > ", parts);
        }

 #endif
        [Test]
        public void SkeletonBuilder_MatchesRuntimeDdbFigureFbmAvatarPoseWithoutExecutingDdb()
        {
            var figureRoot = new GameObject("figure-fbm-oracle");
            var targetRoot = new GameObject("figure-fbm-target");
            Avatar baseAvatar = null; Avatar targetAvatar = null; Mesh mesh = null;
            try
            {
                baseAvatar = CreateTestHumanoidAvatar(figureRoot, "FigureFbm_");
                Avatar targetSeed = CreateTestHumanoidAvatar(targetRoot, "FigureFbm_");
                HumanDescription targetDescription = targetSeed.humanDescription;
                for (int i = 0; i < targetDescription.skeleton.Length; i++)
                {
                    if (targetDescription.skeleton[i].name != "FigureFbm_LeftUpperArm") continue;
                    SkeletonBone bone = targetDescription.skeleton[i]; bone.position += new Vector3(.4f, -.2f, .1f); bone.scale += new Vector3(.2f, .1f, -.1f); targetDescription.skeleton[i] = bone;
                }
                targetAvatar = AvatarBuilder.BuildHumanAvatar(targetRoot, targetDescription);
                Object.DestroyImmediate(targetSeed);
                Animator animator = figureRoot.AddComponent<Animator>(); animator.avatar = baseAvatar;
                var renderer = figureRoot.AddComponent<SkinnedMeshRenderer>(); mesh = CreatePoseComparisonMesh(); renderer.sharedMesh = mesh; renderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips); renderer.bones = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
                DynamicBoneBlender blender = figureRoot.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(renderer, animator, baseAvatar, null, new System.Collections.Generic.List<DynamicBoneBlendTarget> { new DynamicBoneBlendTarget { blendName = "FBM_Body", enabled = true, weight = .5f, targetAvatar = targetAvatar } });
                InvokePrivateInstanceMethod(blender, "Start");

                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figureRoot, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = .5f }))
                {
                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow skeleton, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    using (skeleton) AssertSkeletonBoneApproximatelyEqual(animator.avatar.humanDescription, skeleton.Avatar.humanDescription, "FigureFbm_LeftUpperArm");
                }
            }
            finally { if (targetAvatar != null) Object.DestroyImmediate(targetAvatar); if (baseAvatar != null) Object.DestroyImmediate(baseAvatar); if (mesh != null) Object.DestroyImmediate(mesh); Object.DestroyImmediate(targetRoot); Object.DestroyImmediate(figureRoot); }
        }

        [Test]
        public void PcmBaker_MatchesOutfitAttacherLegacyPcmAfterAttachAndFinalPrefabCommit()
        {
            var figureRoot = new GameObject("attach-pcm-figure");
            var outfitRoot = new GameObject("attach-pcm-outfit");
            Mesh figureMesh = null;
            Mesh outfitMesh = null;
            OutfitSkinningProfile skinningProfile = null;
            CharacterBoneRegistry extraBoneRegistry = null;
            Mesh runtimeBaked = null;
            try
            {
                Transform figureBone = new GameObject("rootBone").transform;
                figureBone.SetParent(figureRoot.transform, false);
                SkinnedMeshRenderer figureRenderer = figureRoot.AddComponent<SkinnedMeshRenderer>();
                figureMesh = CreatePoseComparisonMesh();
                Vector3[] zeros = new Vector3[figureMesh.vertexCount];
                figureMesh.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                figureMesh.AddBlendShapeFrame("PCM_FBM_Body_dress", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, zeros, zeros);
                figureRenderer.sharedMesh = figureMesh;
                figureRenderer.rootBone = figureBone;
                figureRenderer.bones = new[] { figureBone };
                Animator animator = figureRoot.AddComponent<Animator>();
                DynamicBoneBlender blender = figureRoot.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(figureRenderer, animator, null, null, new System.Collections.Generic.List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "FBM_Body", enabled = true, weight = 0.5f }
                });
                UniversalExpressionProxy expressions = figureRoot.AddComponent<UniversalExpressionProxy>();
                FigureMorphSyncCoordinator morphCoordinator = figureRoot.AddComponent<FigureMorphSyncCoordinator>();
                morphCoordinator.ConfigureForFigure(blender, expressions);
                OutfitAttacher attacher = figureRoot.AddComponent<OutfitAttacher>();
                attacher.ConfigureForFigure(blender, animator);
                ShapeSyncOutfit outfit = CreateRuntimeAttachOutfit(outfitRoot, "outfit.pcm-attach", "rootBone", out outfitMesh, out skinningProfile, out extraBoneRegistry);
                var serializedOutfit = new SerializedObject(outfit);
                serializedOutfit.FindProperty("profileControlledMorphEnabled").boolValue = true;
                serializedOutfit.FindProperty("profileControlledMorphOutfitName").stringValue = "dress";
                serializedOutfit.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(attacher.TryAttach(outfit), Is.True);
                runtimeBaked = new Mesh();
                figureRenderer.BakeMesh(runtimeBaked);

                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figureRoot, null, figureRenderer, null);
                Transform outfitRenderer = outfit.transform.Find("renderer");
                var outfitSource = new HumanoidMeshSource("dress", outfit.RegistryId, outfitRoot, outfit, outfitRenderer.GetComponent<SkinnedMeshRenderer>(), null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, new[] { outfitSource }, new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                Mesh candidate = Object.Instantiate(figureMesh);
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figureSource, candidate) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshPcmBaker.TryBake(bake, out StackMachineDiagnostic pcmDiagnostic), Is.True, pcmDiagnostic?.message);
                    AssertVerticesApproximatelyEqual(runtimeBaked.vertices, candidate.vertices);
                    AssertPcmOracleSurvivesFinalPrefabCommit(candidate, runtimeBaked.vertices);
                }
            }
            finally
            {
                if (runtimeBaked != null) Object.DestroyImmediate(runtimeBaked);
                if (skinningProfile != null) Object.DestroyImmediate(skinningProfile);
                if (extraBoneRegistry != null) Object.DestroyImmediate(extraBoneRegistry);
                if (outfitMesh != null) Object.DestroyImmediate(outfitMesh);
                if (figureMesh != null) Object.DestroyImmediate(figureMesh);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figureRoot);
            }
        }

        private static void AssertPcmOracleSurvivesFinalPrefabCommit(Mesh compilerMesh, Vector3[] runtimeOracleVertices)
        {
            string parent = ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17");
            string folder = parent + "/__Spec17_PcmPrefabOracle_" + System.Guid.NewGuid().ToString("N");
            Assert.That(AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1)), Is.Not.Empty);
            GameObject candidate = null;
            try
            {
                Mesh stagedMesh = Object.Instantiate(compilerMesh);
                string meshPath = folder + "/PcmOracle.asset";
                AssetDatabase.CreateAsset(stagedMesh, meshPath);
                candidate = new GameObject("PcmOracleCandidate");
                Transform bone = new GameObject("rootBone").transform;
                bone.SetParent(candidate.transform, false);
                SkinnedMeshRenderer renderer = candidate.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = stagedMesh;
                renderer.bones = new[] { bone };
                renderer.rootBone = bone;
                string prefabPath = folder + "/PcmOracle.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(candidate, prefabPath), Is.Not.Null);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null);
                SkinnedMeshRenderer persistedRenderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.That(persistedRenderer, Is.Not.Null);
                AssertVerticesApproximatelyEqual(runtimeOracleVertices, persistedRenderer.sharedMesh.vertices);
            }
            finally
            {
                if (candidate != null) Object.DestroyImmediate(candidate);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void PcmBaker_MatchesOutfitAttacherPayloadPcmAfterAttach()
        {
            var figureRoot = new GameObject("attach-payload-pcm-figure");
            var outfitRoot = new GameObject("attach-payload-pcm-outfit");
            Mesh figureMesh = null;
            Mesh payloadMesh = null;
            Mesh outfitMesh = null;
            Mesh runtimeBaked = null;
            OutfitSkinningProfile skinningProfile = null;
            CharacterBoneRegistry extraBoneRegistry = null;
            ProfileControlledMorphAsset payload = null;
            try
            {
                Transform figureBone = new GameObject("rootBone").transform;
                figureBone.SetParent(figureRoot.transform, false);
                SkinnedMeshRenderer figureRenderer = figureRoot.AddComponent<SkinnedMeshRenderer>();
                figureMesh = CreatePoseComparisonMesh();
                Vector3[] zeros = new Vector3[figureMesh.vertexCount];
                figureMesh.AddBlendShapeFrame("Morph_Slot_0", 100f, zeros, zeros, zeros);
                figureMesh.AddBlendShapeFrame("Morph_Slot_1", 100f, zeros, zeros, zeros);
                figureRenderer.sharedMesh = figureMesh;
                figureRenderer.rootBone = figureBone;
                figureRenderer.bones = new[] { figureBone };

                DynamicMorphAdapter adapter = figureRoot.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(figureRenderer, 1, 0, new[] { "FBM_Body" });
                Assert.That(adapter.CreateInitialRuntimeMesh(figureMesh), Is.Not.Null);
                Assert.That(adapter.Initialize(), Is.True, "Payload PCM preflight requires the configured Figure adapter to validate its reserved slot schema.");
                Animator animator = figureRoot.AddComponent<Animator>();
                DynamicBoneBlender blender = figureRoot.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(figureRenderer, animator, null, null, new System.Collections.Generic.List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "FBM_Body", enabled = true, weight = 0.5f }
                });
                UniversalExpressionProxy expressions = figureRoot.AddComponent<UniversalExpressionProxy>();
                FigureMorphSyncCoordinator morphCoordinator = figureRoot.AddComponent<FigureMorphSyncCoordinator>();
                morphCoordinator.ConfigureForFigure(blender, expressions);
                OutfitAttacher attacher = figureRoot.AddComponent<OutfitAttacher>();
                attacher.ConfigureForFigure(blender, animator);

                payloadMesh = Object.Instantiate(figureMesh);
                payloadMesh.ClearBlendShapes();
                payloadMesh.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
                payloadMesh.AddBlendShapeFrame("PCM_FBM_Body_dress", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, zeros, zeros);
                payload = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
                payload.ConfigureForBuild(payloadMesh, "dress", new[] { "FBM_Body" }, false);

                ShapeSyncOutfit outfit = CreateRuntimeAttachOutfit(outfitRoot, "outfit.payload-pcm-attach", "rootBone", out outfitMesh, out skinningProfile, out extraBoneRegistry);
                var serializedOutfit = new SerializedObject(outfit);
                serializedOutfit.FindProperty("profileControlledMorphEnabled").boolValue = true;
                serializedOutfit.FindProperty("profileControlledMorphOutfitName").stringValue = "dress";
                serializedOutfit.FindProperty("profileControlledMorphAsset").objectReferenceValue = payload;
                serializedOutfit.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(attacher.TryAttach(outfit), Is.True);
                runtimeBaked = new Mesh();
                figureRenderer.BakeMesh(runtimeBaked);

                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figureRoot, null, figureRenderer, null);
                Transform outfitRenderer = outfit.transform.Find("renderer");
                var outfitSource = new HumanoidMeshSource("dress", outfit.RegistryId, outfitRoot, outfit, outfitRenderer.GetComponent<SkinnedMeshRenderer>(), null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, System.Array.Empty<HumanoidMeshSource>(), new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                Mesh candidate = Object.Instantiate(figureMesh);
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figureSource, candidate) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshPcmBaker.TryBake(bake, out StackMachineDiagnostic pcmDiagnostic), Is.True, pcmDiagnostic?.message);
                    AssertVerticesApproximatelyEqual(runtimeBaked.vertices, candidate.vertices);
                }
            }
            finally
            {
                if (runtimeBaked != null) Object.DestroyImmediate(runtimeBaked);
                if (payload != null) Object.DestroyImmediate(payload);
                if (payloadMesh != null) Object.DestroyImmediate(payloadMesh);
                if (skinningProfile != null) Object.DestroyImmediate(skinningProfile);
                if (extraBoneRegistry != null) Object.DestroyImmediate(extraBoneRegistry);
                if (outfitMesh != null) Object.DestroyImmediate(outfitMesh);
                if (figureMesh != null) Object.DestroyImmediate(figureMesh);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figureRoot);
            }
        }

        [Test]
        public void SkeletonBuilder_DropsDetachedNonHumanSkeletonMetadataWhenRebuildingAvatar()
        {
            var root = new GameObject("skeleton-optional-source");
            Avatar avatar = null;
            try
            {
                avatar = CreateTestHumanoidAvatar(root, "Optional_", "VRM1");
                Transform optional = root.transform.Find("VRM1");
                Assert.That(optional, Is.Not.Null);
                Object.DestroyImmediate(optional.gameObject);
                root.AddComponent<Animator>().avatar = avatar;
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var plan = new HumanoidMeshLogicalPlan(core, new HumanoidMeshSource(null, string.Empty, root, null, null, null), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float>()))
                {
                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow escrow, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    using (escrow) Assert.That(escrow.Avatar.isValid && escrow.Avatar.isHuman, Is.True);
                }
            }
            finally { if (avatar != null) Object.DestroyImmediate(avatar); Object.DestroyImmediate(root); }
        }

        [Test]
        public void BoneTable_MapsWeightedFigureBoneToSkeletonCloneAndRebuildsBindpose()
        {
            var root = new GameObject("bone-table-source"); Avatar avatar = null; Mesh mesh = null;
            try
            {
                avatar = CreateTestHumanoidAvatar(root, "Bone_");
                root.AddComponent<Animator>().avatar = avatar;
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                Transform sourceBone = root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftUpperArm);
                renderer.rootBone = root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
                renderer.bones = new[] { sourceBone };
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 }, boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } } };
                renderer.sharedMesh = mesh;
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, System.Array.Empty<HumanoidMeshFbmBakedSource>(), new System.Collections.Generic.Dictionary<string, float>()))
                {
                    Assert.That(HumanoidMeshSkeletonBuilder.TryCreate(bake, out HumanoidMeshSkeletonEscrow skeleton, out StackMachineDiagnostic skeletonDiagnostic), Is.True, skeletonDiagnostic?.message);
                    using (skeleton)
                    {
                        Assert.That(HumanoidMeshBoneTable.TryCreate(figure, skeleton, out HumanoidMeshBoneTable table, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                        Assert.That(table.Bones, Has.Length.EqualTo(1));
                        Assert.That(table.Bones[0], Is.SameAs(skeleton.Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm)));
                        Assert.That(table.Bindposes[0], Is.EqualTo(table.Bones[0].worldToLocalMatrix * skeleton.Root.transform.localToWorldMatrix));
                    }
                }
            }
            finally { if (mesh != null) Object.DestroyImmediate(mesh); if (avatar != null) Object.DestroyImmediate(avatar); Object.DestroyImmediate(root); }
        }

        [Test]
        public void EditModeMeshStackMachine_RejectsBcpBoneMissingFromFigureHumanoid()
        {
            using (var fixture = new CollectorFixture(createHumanoidAnimator: true))
            using (var machine = new EditModeMeshStackMachine())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, true);
                outfit.HumanoidBoneCorrectionProfile.SetCorrectionsForEditor(new System.Collections.Generic.List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection { bone = HumanBodyBones.LeftToes, localPositionDelta = Vector3.right, localRotationDelta = Quaternion.identity }
                });
                Transform sourceRoot = fixture.Figure.transform;
                ShapeSyncDocument document = fixture.CreateDocument("$dress ATTACH", outfit);
                Assert.That(machine.Start(fixture.Figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Failed));
                Assert.That(pumpDiagnostic.domainCode, Is.EqualTo("BcpSkeletonApplyFailed"));
                Assert.That(fixture.Figure.transform, Is.SameAs(sourceRoot));
                Assert.That(machine.TryTakeFbmBakeResult(out _), Is.False);
            }
        }

        [Test]
        public void BcpResolver_UsesFbmTargetOnlyBoneFromIdentityBase()
        {
            using (var fixture = new CollectorFixture())
            {
                ShapeSyncOutfit outfit = fixture.CreateOutfit("dress", "outfit.dress", false, true);
                ShapeSyncHumanoidBoneCorrectionProfile target = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
                target.SetCorrectionsForEditor(new System.Collections.Generic.List<ShapeSyncHumanoidBoneCorrection>
                {
                    new ShapeSyncHumanoidBoneCorrection { bone = HumanBodyBones.LeftUpperArm, localPositionDelta = Vector3.right, localRotationDelta = Quaternion.identity }
                });
                var serialized = new SerializedObject(outfit);
                SerializedProperty fbmProfiles = serialized.FindProperty("fbmHumanoidBoneCorrectionProfiles");
                fbmProfiles.arraySize = 1;
                fbmProfiles.GetArrayElementAtIndex(0).FindPropertyRelative("blendName").stringValue = "FBM_Body";
                fbmProfiles.GetArrayElementAtIndex(0).FindPropertyRelative("targetProfile").objectReferenceValue = target;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                ShapeSyncDocument document = fixture.CreateDocument("$body 0.5 FBM_SET $dress ATTACH", outfit);
                document.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                var bindingSerialized = new SerializedObject(document.MeshBinding);
                SerializedProperty morphs = bindingSerialized.FindProperty("morphs");
                morphs.arraySize = 1;
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("logicalName").stringValue = "body";
                morphs.GetArrayElementAtIndex(0).FindPropertyRelative("targetName").stringValue = "FBM_Body";
                bindingSerialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(fixture.Figure, document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic collectDiagnostic), Is.True, collectDiagnostic?.message);
                Assert.That(HumanoidMeshFbmBaker.TryBake(plan, out HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic bakeDiagnostic), Is.True, bakeDiagnostic?.message);
                using (bake)
                {
                    Assert.That(HumanoidMeshBcpResolver.TryResolve(bake, out var deltas, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(deltas, Has.Count.EqualTo(1));
                    Assert.That(deltas[0].Bone, Is.EqualTo(HumanBodyBones.LeftUpperArm));
                    Assert.That(deltas[0].Position.x, Is.EqualTo(0.5f).Within(0.0001f));
                }
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void PcmBaker_AppliesLegacyBaseAndFbmVariantDirectly()
        {
            GameObject figure = new GameObject("pcm-figure");
            GameObject outfitRoot = new GameObject("pcm-outfit");
            Mesh mesh = CreateFbmBakeMesh();
            try
            {
                var renderer = figure.AddComponent<SkinnedMeshRenderer>();
                mesh.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                mesh.AddBlendShapeFrame("PCM_FBM_Body_dress", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                renderer.sharedMesh = mesh;
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("profileControlledMorphEnabled").boolValue = true;
                serialized.FindProperty("profileControlledMorphOutfitName").stringValue = "dress";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var recipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" };
                Assert.That(MeshStackMachineCorePlan.TryCreate(recipe, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figure, null, renderer, null);
                var outfitSource = new HumanoidMeshSource("dress", "outfit.dress", outfitRoot, outfit, null, null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, System.Array.Empty<HumanoidMeshSource>(), new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                Mesh candidate = Object.Instantiate(mesh);
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figureSource, candidate) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshPcmBaker.TryBake(bake, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(candidate.vertices[0], Is.EqualTo(new Vector3(1f, 0.5f, 0f)));
                }
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(figure); Object.DestroyImmediate(outfitRoot); }
        }

        [Test]
        public void PcmBaker_AppliesEnabledPayloadBaseAndFbmVariantDirectly()
        {
            GameObject figure = new GameObject("pcm-payload-figure");
            GameObject outfitRoot = new GameObject("pcm-payload-outfit");
            Mesh mesh = CreateFbmBakeMesh();
            Mesh payloadMesh = CreateFbmBakeMesh();
            ProfileControlledMorphAsset payload = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
            try
            {
                var renderer = figure.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                payloadMesh.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                payloadMesh.AddBlendShapeFrame("PCM_FBM_Body_dress", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                payload.ConfigureForBuild(payloadMesh, "dress", new[] { "FBM_Body" }, false);
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("profileControlledMorphEnabled").boolValue = true;
                serialized.FindProperty("profileControlledMorphOutfitName").stringValue = "dress";
                serialized.FindProperty("profileControlledMorphAsset").objectReferenceValue = payload;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var recipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" };
                Assert.That(MeshStackMachineCorePlan.TryCreate(recipe, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figureSource = new HumanoidMeshSource(null, string.Empty, figure, null, renderer, null);
                var outfitSource = new HumanoidMeshSource("dress", "outfit.dress", outfitRoot, outfit, null, null);
                var plan = new HumanoidMeshLogicalPlan(core, figureSource, System.Array.Empty<HumanoidMeshSource>(), new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                Mesh candidate = Object.Instantiate(mesh);
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figureSource, candidate) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshPcmBaker.TryBake(bake, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(candidate.vertices[0], Is.EqualTo(new Vector3(1f, 0.5f, 0f)));
                }
            }
            finally { Object.DestroyImmediate(payload); Object.DestroyImmediate(payloadMesh); Object.DestroyImmediate(mesh); Object.DestroyImmediate(figure); Object.DestroyImmediate(outfitRoot); }
        }

        [Test]
        public void VariantFinalizer_ReplacesCompilerOnlyShapesWithResolvedPbmAndVrm()
        {
            GameObject root = new GameObject("variant-finalizer-figure"); Mesh source = CreateFbmBakeMesh();
            try
            {
                source.AddBlendShapeFrame("PBM_Smile", 100f, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { Vector3.right * 2f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("FBM_Thin", 100f, new[] { Vector3.left, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("PBM_FBM_Thin_Smile", 100f, new[] { Vector3.left * 2f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("VRM_Smile", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("MCM_FBM_Body_Smile", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("PCM_dress", 100f, new[] { Vector3.left, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("Raw_Blink", 100f, new[] { Vector3.down, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = source;
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figure, Object.Instantiate(source)) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshVariantFinalizer.TryFinalize(bake, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Mesh finalMesh = bake.Sources[0].Mesh;
                    Assert.That(finalMesh.GetBlendShapeIndex("FBM_Body"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("PCM_dress"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("PBM_FBM_Body_Smile"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("PBM_FBM_Thin_Smile"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("MCM_FBM_Body_Smile"), Is.EqualTo(-1));
                    Assert.That(finalMesh.GetBlendShapeIndex("PBM_Smile"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(finalMesh.GetBlendShapeIndex("VRM_Smile"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(finalMesh.GetBlendShapeIndex("Raw_Blink"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalMesh, finalMesh.GetBlendShapeIndex("PBM_Smile"), 100f, out Vector3[] pbm, out _, out _), Is.True);
                    Assert.That(pbm[0], Is.EqualTo(new Vector3(1.5f, 0f, 1f)));
                    Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalMesh, finalMesh.GetBlendShapeIndex("VRM_Smile"), 100f, out Vector3[] vrm, out _, out _), Is.True);
                    Assert.That(vrm[0], Is.EqualTo(new Vector3(1.5f, 0.5f, 0f)));
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void VariantFinalizer_SilentlySkipsBoneChangingPbmAndKeepsVrmExpression()
        {
            GameObject root = new GameObject("bone-changing-pbm-figure");
            Mesh source = CreateFbmBakeMesh();
            CharacterBoneRegistry baseRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry pbmRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                source.AddBlendShapeFrame("PBM_Smile", 100f, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("VRM_Smile", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = source;
                baseRegistry.bonePoses.Add(CreatePose("hips", Vector3.zero));
                pbmRegistry.bonePoses.Add(CreatePose("hips", Vector3.up));
                root.AddComponent<DynamicBoneBlender>().ConfigureForFigure(renderer, null, null, baseRegistry, new[]
                {
                    new DynamicBoneBlendTarget { blendName = "PBM_Smile", targetRegistry = pbmRegistry }
                });
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figure, Object.Instantiate(source)) }, new System.Collections.Generic.Dictionary<string, float>()))
                {
                    Assert.That(HumanoidMeshVariantFinalizer.TryFinalize(bake, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(bake.Sources[0].Mesh.GetBlendShapeIndex("PBM_Smile"), Is.EqualTo(-1));
                    Assert.That(bake.Sources[0].Mesh.GetBlendShapeIndex("VRM_Smile"), Is.GreaterThanOrEqualTo(0));
                }
            }
            finally { Object.DestroyImmediate(pbmRegistry); Object.DestroyImmediate(baseRegistry); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void PbmBoneChangeClassifier_DetectsFbmDifferenceAndAttachedOutfitExtraBoneChanges()
        {
            GameObject root = new GameObject("pbm-difference-figure");
            GameObject outfitRoot = new GameObject("pbm-extra-outfit");
            Mesh source = CreateFbmBakeMesh();
            CharacterBoneRegistry baseRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry sameRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry differenceRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry extraBaseRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            CharacterBoneRegistry extraPbmRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = source;
                baseRegistry.bonePoses.Add(CreatePose("hips", Vector3.zero));
                sameRegistry.bonePoses.Add(CreatePose("hips", Vector3.zero));
                differenceRegistry.bonePoses.Add(CreatePose("hips", Vector3.right));
                DynamicBoneBlendTarget pbm = new DynamicBoneBlendTarget { blendName = "PBM_Smile", targetRegistry = sameRegistry };
                pbm.pbmDifferenceTargets.Add(new DynamicBonePbmDifferenceTarget { fbmBlendName = "FBM_Body", targetRegistry = differenceRegistry });
                root.AddComponent<DynamicBoneBlender>().ConfigureForFigure(renderer, null, null, baseRegistry, new[]
                {
                    new DynamicBoneBlendTarget { blendName = "FBM_Body", targetRegistry = sameRegistry }, pbm
                });
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                extraBaseRegistry.bonePoses.Add(CreatePose("extra", Vector3.zero));
                extraPbmRegistry.bonePoses.Add(CreatePose("extra", Vector3.up));
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("baseExtraBoneRegistry").objectReferenceValue = extraBaseRegistry;
                SerializedProperty mappings = serialized.FindProperty("fbmExtraBoneRegistries");
                mappings.arraySize = 1;
                mappings.GetArrayElementAtIndex(0).FindPropertyRelative("blendName").stringValue = "PBM_Smile";
                mappings.GetArrayElementAtIndex(0).FindPropertyRelative("extraBoneRegistry").objectReferenceValue = extraPbmRegistry;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var outfitSource = new HumanoidMeshSource("outfit", "outfit.pbm", outfitRoot, outfit, null, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, new[] { outfitSource }, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                Assert.That(HumanoidMeshPbmBoneChangeClassifier.HasBoneChange(plan, "Smile"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(extraPbmRegistry); Object.DestroyImmediate(extraBaseRegistry); Object.DestroyImmediate(differenceRegistry); Object.DestroyImmediate(sameRegistry); Object.DestroyImmediate(baseRegistry);
                Object.DestroyImmediate(source); Object.DestroyImmediate(outfitRoot); Object.DestroyImmediate(root);
            }
        }

 #if SHAPESYNC_RICH_TEST
        [Test]
        public void ActualSpec17DocumentA_BoneChangingPbmArmLongIsNotRetainedInFinalMesh()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_A.asset";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(prefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject candidate = null;
            try
            {
                candidate = Object.Instantiate(prefab);
                Assert.That(HumanoidMeshLogicalCollector.TryCreate(candidate, payload, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                Assert.That(HumanoidMeshPbmBoneChangeClassifier.HasBoneChange(plan, "ArmLong"), Is.True, "The fixture's PBM_ArmLong changes Humanoid bone data and cannot remain a Pure Humanoid BlendShape.");
                using (var machine = new EditModeMeshStackMachine())
                {
                    Assert.That(machine.Start(candidate, payload, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(EditModeMeshExecutionStatus.Succeeded), pumpDiagnostic?.message);
                    Assert.That(machine.TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result), Is.True);
                    using (result)
                    {
                        var sourceNames = new System.Collections.Generic.List<string>();
                        for (int sourceIndex = 0; sourceIndex < result.Sources.Count; sourceIndex++)
                        {
                            Mesh sourceMesh = result.Sources[sourceIndex].Mesh;
                            var names = new System.Collections.Generic.List<string>();
                            for (int shapeIndex = 0; sourceMesh != null && shapeIndex < sourceMesh.blendShapeCount; shapeIndex++) names.Add(sourceMesh.GetBlendShapeName(shapeIndex));
                            sourceNames.Add(sourceIndex + "=" + string.Join(",", names));
                        }
                        Assert.That(result.FinalMesh.GetBlendShapeIndex("PBM_ArmLong"), Is.EqualTo(-1), string.Join("; ", sourceNames));
                        Assert.That(result.FinalMesh.GetBlendShapeIndex("PBM_BasicGirl_ArmLong"), Is.EqualTo(-1), string.Join("; ", sourceNames));
                    }
                }
            }
            finally
            {
                if (candidate != null) Object.DestroyImmediate(candidate);
            }
        }

 #endif
        [Test]
        public void PbmVariantBaker_RegistersResolvedFbmExpectedShape()
        {
            GameObject root = new GameObject("pbm-figure");
            Mesh source = CreateFbmBakeMesh();
            try
            {
                source.AddBlendShapeFrame("PBM_Smile", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { new Vector3(2f, 0f, 0f), Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = source;
                var recipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" };
                Assert.That(MeshStackMachineCorePlan.TryCreate(recipe, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figure, Object.Instantiate(source)) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Assert.That(HumanoidMeshPbmVariantBaker.TryBakeFigureVariant(bake, "Smile", out Mesh variant, out StackMachineDiagnostic variantDiagnostic), Is.True, variantDiagnostic?.message);
                    try
                    {
                        Mesh finalBase = Object.Instantiate(source); finalBase.ClearBlendShapes();
                        try
                        {
                            Assert.That(HumanoidMeshPbmVariantBaker.TryRegisterExpectedShape(finalBase, variant, "PBM_Smile", out StackMachineDiagnostic registerDiagnostic), Is.True, registerDiagnostic?.message);
                            Assert.That(finalBase.blendShapeCount, Is.EqualTo(1));
                            Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalBase, 0, 100f, out Vector3[] delta, out _, out _), Is.True);
                            Assert.That(delta[0].x, Is.EqualTo(1.5f).Within(0.0001f));
                        }
                        finally { Object.DestroyImmediate(finalBase); }
                    }
                    finally { Object.DestroyImmediate(variant); }
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExpressionVariantBaker_RegistersOnlyVrmExpectedShape()
        {
            GameObject root = new GameObject("expression-figure"); Mesh source = CreateFbmBakeMesh();
            try
            {
                source.AddBlendShapeFrame("VRM_Smile", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                source.AddBlendShapeFrame("MCM_FBM_Body_Smile", 100f, new[] { Vector3.up, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                var renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = source;
                Assert.That(MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, System.Array.Empty<MeshCoreBinding>(), out MeshStackMachineCorePlan core, out StackMachineDiagnostic d), Is.True, d?.message);
                var figure = new HumanoidMeshSource(null, string.Empty, root, null, renderer, null);
                var plan = new HumanoidMeshLogicalPlan(core, figure, System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshSource>(), System.Array.Empty<HumanoidMeshNormalSource>());
                using (var bake = new HumanoidMeshFbmBakeResult(plan, new[] { new HumanoidMeshFbmBakedSource(figure, Object.Instantiate(source)) }, new System.Collections.Generic.Dictionary<string, float> { ["FBM_Body"] = 0.5f }))
                {
                    Mesh finalBase = Object.Instantiate(source); finalBase.ClearBlendShapes();
                    try
                    {
                        Assert.That(HumanoidMeshExpressionVariantBaker.TryBakeAndRegister(bake, finalBase, "Smile", out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                        Assert.That(finalBase.blendShapeCount, Is.EqualTo(1));
                        Assert.That(finalBase.GetBlendShapeName(0), Is.EqualTo("VRM_Smile"));
                        Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(finalBase, 0, 100f, out Vector3[] delta, out _, out _), Is.True);
                        Assert.That(delta[0], Is.EqualTo(new Vector3(1.5f, 0.5f, 0f)));
                    }
                    finally { Object.DestroyImmediate(finalBase); }
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        private static Mesh CreateFbmBakeMesh()
        {
            var mesh = new Mesh { name = "fbm-baker-source" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2 };
            var fbmVertices = new[] { Vector3.right, Vector3.zero, Vector3.zero };
            var zeros = new Vector3[3];
            mesh.AddBlendShapeFrame("FBM_Body", 100f, fbmVertices, zeros, zeros);
            mesh.AddBlendShapeFrame("PBM_Body", 100f, new Vector3[3], zeros, zeros);
            return mesh;
        }

        private static Mesh CreateCombineMesh(string name, Vector3 shapeDelta)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            mesh.AddBlendShapeFrame("PBM_Smile", 100f, new[] { shapeDelta, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            return mesh;
        }

        private static Mesh CreatePoseComparisonMesh()
        {
            var mesh = new Mesh { name = "attach-pose-mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            return mesh;
        }

        private static BonePoseData CreatePose(string path, Vector3 position)
        {
            return new BonePoseData
            {
                boneName = path,
                localPosition = position,
                localRotation = Quaternion.identity,
                localScale = Vector3.one
            };
        }

        private static ShapeSyncOutfit CreateRuntimeAttachOutfit(GameObject root, string registryId, string bonePath, out Mesh mesh, out OutfitSkinningProfile skinningProfile, out CharacterBoneRegistry extraBoneRegistry)
        {
            ShapeSyncOutfit outfit = root.AddComponent<ShapeSyncOutfit>();
            var serializedOutfit = new SerializedObject(outfit);
            serializedOutfit.FindProperty("registryId").stringValue = registryId;
            serializedOutfit.FindProperty("fbmExtraBoneRegistries").arraySize = 0;
            extraBoneRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            serializedOutfit.FindProperty("baseExtraBoneRegistry").objectReferenceValue = extraBoneRegistry;
            serializedOutfit.ApplyModifiedPropertiesWithoutUndo();
            Transform bone = CreateChildPath(root.transform, bonePath);
            Transform rendererTransform = new GameObject("renderer").transform;
            rendererTransform.SetParent(root.transform, false);
            SkinnedMeshRenderer renderer = rendererTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
            mesh = CreatePoseComparisonMesh();
            renderer.sharedMesh = mesh;
            renderer.rootBone = bone;
            renderer.bones = new[] { bone };
            skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            skinningProfile.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile>
            {
                new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = mesh.bindposes }
            });
            serializedOutfit.Update();
            serializedOutfit.FindProperty("skinningProfile").objectReferenceValue = skinningProfile;
            serializedOutfit.ApplyModifiedPropertiesWithoutUndo();
            return outfit;
        }

        private static Transform CreateChildPath(Transform root, string path)
        {
            Transform current = root;
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                Transform next = current.Find(segments[i]);
                if (next == null)
                {
                    next = new GameObject(segments[i]).transform;
                    next.SetParent(current, false);
                }
                current = next;
            }
            return current;
        }

        private static string GetRelativePath(Transform root, Transform value)
        {
            var segments = new System.Collections.Generic.List<string>();
            Transform current = value;
            for (; current != null && current != root; current = current.parent) segments.Add(current.name);
            Assert.That(current, Is.SameAs(root));
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static ShapeSyncHumanoidBoneCorrectionProfile CreateBcpProfile(HumanBodyBones bone, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            profile.SetCorrectionsForEditor(new System.Collections.Generic.List<ShapeSyncHumanoidBoneCorrection>
            {
                new ShapeSyncHumanoidBoneCorrection { bone = bone, localPositionDelta = position, localRotationDelta = rotation, localScaleDelta = scale }
            });
            return profile;
        }

        private static void InvokePrivateInstanceMethod(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(instance, null);
        }

        private static void AssertSkeletonBoneApproximatelyEqual(HumanDescription expected, HumanDescription actual, string name)
        {
            SkeletonBone expectedBone = FindSkeletonBone(expected, name);
            SkeletonBone actualBone = FindSkeletonBone(actual, name);
            AssertVectorApproximatelyEqual(expectedBone.position, actualBone.position, name + ".position");
            AssertQuaternionApproximatelyEqual(expectedBone.rotation, actualBone.rotation, name + ".rotation");
            AssertVectorApproximatelyEqual(expectedBone.scale, actualBone.scale, name + ".scale");
        }

        private static void AssertPersistedHumanoidRestPoseMatchesAvatar(GameObject root, Animator animator)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.avatar, Is.Not.Null);
            var transformsByName = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) transformsByName[transform.name] = transform;
            foreach (SkeletonBone expected in animator.avatar.humanDescription.skeleton)
            {
                Assert.That(transformsByName.TryGetValue(expected.name, out Transform actual), Is.True, "Published Avatar skeleton missing " + expected.name);
                Assert.That(Vector3.Distance(actual.localPosition, expected.position), Is.LessThanOrEqualTo(0.00001f), expected.name + " persisted rest position");
                Assert.That(Quaternion.Angle(actual.localRotation, expected.rotation), Is.LessThanOrEqualTo(0.001f), expected.name + " persisted rest rotation");
                Assert.That(Vector3.Distance(actual.localScale, expected.scale), Is.LessThanOrEqualTo(0.00001f), expected.name + " persisted rest scale");
            }
        }

        private static void AssertHumanDescriptionApproximatelyEqual(HumanDescription expected, HumanDescription actual)
        {
            Assert.That(actual.skeleton, Is.Not.Null);
            Assert.That(expected.skeleton, Is.Not.Null);
            Assert.That(expected.human, Is.Not.Null);
            for (int i = 0; i < expected.human.Length; i++)
            {
                string boneName = expected.human[i].boneName;
                if (!string.IsNullOrEmpty(boneName)) AssertSkeletonBoneApproximatelyEqual(expected, actual, boneName);
            }
        }

        private static SkeletonBone FindSkeletonBone(HumanDescription description, string name)
        {
            for (int i = 0; i < description.skeleton.Length; i++) if (description.skeleton[i].name == name) return description.skeleton[i];
            Assert.Fail("Skeleton bone was not found: " + name);
            return default;
        }

        private static void AssertVerticesApproximatelyEqual(Vector3[] expected, Vector3[] actual)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) AssertVectorApproximatelyEqual(expected[i], actual[i], "vertex[" + i + "]");
        }

        private static void AssertVectorApproximatelyEqual(Vector3 expected, Vector3 actual, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), label + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), label + ".y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f), label + ".z");
        }

        private static void AssertQuaternionApproximatelyEqual(Quaternion expected, Quaternion actual, string label)
        {
            if (Quaternion.Dot(expected, actual) < 0f) actual = new Quaternion(-actual.x, -actual.y, -actual.z, -actual.w);
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), label + ".x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), label + ".y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f), label + ".z");
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(0.0001f), label + ".w");
        }

        private static void AssertMatrixApproximatelyEqual(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(0.0001f), $"matrix[{row},{column}]");
                }
            }
        }

        private static void ConfigurePbm(Mesh mesh)
        {
            Vector3[] zeros = new Vector3[mesh.vertexCount];
            mesh.AddBlendShapeFrame("FBM_Body", 100f, new[] { Vector3.right, Vector3.zero, Vector3.zero }, zeros, zeros);
            mesh.AddBlendShapeFrame("PBM_Smile", 100f, zeros, zeros, zeros);
            mesh.AddBlendShapeFrame("PBM_FBM_Body_Smile", 100f, new[] { Vector3.right * 2f, Vector3.zero, Vector3.zero }, zeros, zeros);
        }

        private static int CountHiddenSkeletonRoots(string sourceName)
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == sourceName && all[i].hideFlags == HideFlags.HideAndDontSave) count++;
            }
            return count;
        }

        private static Avatar CreateTestHumanoidAvatar(GameObject root, string prefix, string optionalNonHumanSkeletonBoneName = null)
        {
            var bones = new System.Collections.Generic.List<Transform>();
            Transform hips = AddHumanoidBone(root.transform, prefix + "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddHumanoidBone(hips, prefix + "Spine", new Vector3(0f, .15f, 0f), bones);
            Transform chest = AddHumanoidBone(spine, prefix + "Chest", new Vector3(0f, .15f, 0f), bones);
            Transform neck = AddHumanoidBone(chest, prefix + "Neck", new Vector3(0f, .15f, 0f), bones);
            AddHumanoidBone(neck, prefix + "Head", new Vector3(0f, .12f, 0f), bones);
            Transform lua = AddHumanoidBone(chest, prefix + "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones);
            Transform lla = AddHumanoidBone(lua, prefix + "LeftLowerArm", new Vector3(-.2f, 0f, 0f), bones);
            AddHumanoidBone(lla, prefix + "LeftHand", new Vector3(-.18f, 0f, 0f), bones);
            Transform rua = AddHumanoidBone(chest, prefix + "RightUpperArm", new Vector3(.15f, .1f, 0f), bones);
            Transform rla = AddHumanoidBone(rua, prefix + "RightLowerArm", new Vector3(.2f, 0f, 0f), bones);
            AddHumanoidBone(rla, prefix + "RightHand", new Vector3(.18f, 0f, 0f), bones);
            Transform lul = AddHumanoidBone(hips, prefix + "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones);
            Transform lll = AddHumanoidBone(lul, prefix + "LeftLowerLeg", new Vector3(0f, -.35f, 0f), bones);
            AddHumanoidBone(lll, prefix + "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rul = AddHumanoidBone(hips, prefix + "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones);
            Transform rll = AddHumanoidBone(rul, prefix + "RightLowerLeg", new Vector3(0f, -.35f, 0f), bones);
            AddHumanoidBone(rll, prefix + "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            var skeleton = new System.Collections.Generic.List<SkeletonBone> { ToSkeletonBone(root.transform) };
            for (int i = 0; i < bones.Count; i++) skeleton.Add(ToSkeletonBone(bones[i]));
            if (!string.IsNullOrEmpty(optionalNonHumanSkeletonBoneName))
            {
                var optional = new GameObject(optionalNonHumanSkeletonBoneName).transform;
                optional.SetParent(root.transform, false);
                skeleton.Add(ToSkeletonBone(optional));
            }
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var human = new HumanBone[names.Length];
            for (int i = 0; i < names.Length; i++) human[i] = new HumanBone { boneName = prefix + names[i], humanName = names[i], limit = new HumanLimit { useDefaultValues = true } };
            return AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
        }

        private static Transform AddHumanoidBone(Transform parent, string name, Vector3 position, System.Collections.Generic.List<Transform> bones)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false); bone.localPosition = position; bones.Add(bone); return bone;
        }

        private static SkeletonBone ToSkeletonBone(Transform transform) => new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };

        private sealed class TakeFailureMeshPhaseMachine : IEditModeMeshBuildPhaseMachine
        {
            internal int TakeCalls { get; private set; }
            public bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public EditModeMeshExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = null; return EditModeMeshExecutionStatus.Succeeded; }
            public bool TryTakeResult(out EditModeMeshBuildResult result) { TakeCalls++; result = null; return false; }
            public void Cancel() { }
        }

        private sealed class TerminalMeshPhaseMachine : IEditModeMeshBuildPhaseMachine
        {
            internal EditModeMeshExecutionStatus PumpStatus { get; set; } = EditModeMeshExecutionStatus.Pending;
            internal bool StartAccepted { get; set; } = true;
            public bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic) { diagnostic = null; return StartAccepted; }
            public EditModeMeshExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = null; return PumpStatus; }
            public bool TryTakeResult(out EditModeMeshBuildResult result) { result = null; return false; }
            public void Cancel() { }
        }

        private sealed class TerminalMaterialPhaseMachine : IEditModeMaterialBuildPhaseMachine
        {
            internal EditModeMaterialExecutionStatus PumpStatus { get; set; } = EditModeMaterialExecutionStatus.Pending;
            internal StackMachineDiagnostic PumpDiagnostic { get; set; }
            internal bool TakeAccepted { get; set; } = true;
            internal int TakeCalls { get; private set; }
            public bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public EditModeMaterialExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = PumpDiagnostic; return PumpStatus; }
            public bool TryTakeResult(out EditModeMaterialBuildResult result) { TakeCalls++; result = null; return TakeAccepted; }
            public void Cancel() { }
        }

        private sealed class CollectorFixture : System.IDisposable
        {
            private readonly System.Collections.Generic.List<Object> objects = new System.Collections.Generic.List<Object>();
            private readonly MaterialShaderAdapter figureAdapter;
            private readonly Material figureMaterial;

            internal CollectorFixture(bool createFigureRenderer = true, bool createFigureAdapter = true, string figureEntryName = "figure", bool createHumanoidAnimator = false)
            {
                Figure = new GameObject("collector-figure");
                objects.Add(Figure);
                if (createHumanoidAnimator)
                {
                    Avatar avatar = CreateTestHumanoidAvatar(Figure, "Fixture_");
                    Figure.AddComponent<Animator>().avatar = avatar;
                    objects.Add(avatar);
                }
                if (createFigureRenderer)
                {
                    FigureRenderer = Figure.AddComponent<SkinnedMeshRenderer>();
                    Mesh mesh = CreateMinimalMesh();
                    FigureRenderer.sharedMesh = mesh;
                    if (createHumanoidAnimator)
                    {
                        Animator animator = Figure.GetComponent<Animator>();
                        FigureRenderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                        FigureRenderer.bones = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
                    }
                    objects.Add(mesh);
                    figureMaterial = CreateMaterial();
                    figureAdapter = createFigureAdapter ? ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>() : null;
                    if (figureAdapter != null) objects.Add(figureAdapter);
                    ConfigureProxy(Figure.AddComponent<MaterialProxy>(), FigureRenderer, figureEntryName, figureMaterial, figureAdapter);
                    objects.Add(figureMaterial);
                }
            }

            internal GameObject Figure { get; }
            internal SkinnedMeshRenderer FigureRenderer { get; }

            internal void UseCompilerCompatibleMaterial()
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                figureMaterial.shader = shader;
                figureMaterial.SetColor("_BaseColor", Color.white);
            }

            internal void UseCompilerCompatibleLitMaterial()
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.That(shader, Is.Not.Null);
                figureMaterial.shader = shader;
                figureMaterial.SetColor("_BaseColor", Color.white);
                var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                objects.Add(adapter);
                var serialized = new SerializedObject(Figure.GetComponent<MaterialProxy>());
                serialized.FindProperty("entries").GetArrayElementAtIndex(0).FindPropertyRelative("adapter").objectReferenceValue = adapter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            internal ShapeSyncOutfit CreateOutfit(string logicalName, string registryId, bool pcm, bool bcp, string entryName = null)
            {
                var root = new GameObject("collector-outfit-" + logicalName);
                objects.Add(root);
                ShapeSyncOutfit outfit = root.AddComponent<ShapeSyncOutfit>();
                var outfitSerialized = new SerializedObject(outfit);
                outfitSerialized.FindProperty("registryId").stringValue = registryId;
                outfitSerialized.FindProperty("profileControlledMorphEnabled").boolValue = pcm;
                if (pcm) outfitSerialized.FindProperty("profileControlledMorphOutfitName").stringValue = logicalName;
                if (bcp) outfitSerialized.FindProperty("humanoidBoneCorrectionProfile").objectReferenceValue = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
                outfitSerialized.ApplyModifiedPropertiesWithoutUndo();
                if (bcp) objects.Add(outfit.HumanoidBoneCorrectionProfile);
                var rendererRoot = new GameObject("renderer"); rendererRoot.transform.SetParent(root.transform, false); objects.Add(rendererRoot);
                var rootBone = new GameObject("rootBone"); rootBone.transform.SetParent(root.transform, false); objects.Add(rootBone);
                SkinnedMeshRenderer renderer = rendererRoot.AddComponent<SkinnedMeshRenderer>();
                Mesh mesh = CreateMinimalMesh();
                renderer.sharedMesh = mesh;
                renderer.rootBone = rootBone.transform;
                renderer.bones = new[] { rootBone.transform };
                objects.Add(mesh);
                OutfitSkinningProfile skinning = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                skinning.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = mesh.bindposes }
                });
                outfitSerialized.Update();
                outfitSerialized.FindProperty("skinningProfile").objectReferenceValue = skinning;
                outfitSerialized.ApplyModifiedPropertiesWithoutUndo();
                objects.Add(skinning);
                Material material = CreateMaterial();
                MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                objects.Add(material);
                objects.Add(adapter);
                ConfigureProxy(root.AddComponent<MaterialProxy>(), renderer, entryName ?? logicalName, material, adapter);
                return outfit;
            }

            internal ShapeSyncDocument CreateDocument(string source, params ShapeSyncOutfit[] outfits)
            {
                MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
                objects.Add(binding);
                var serialized = new SerializedObject(binding);
                SerializedProperty entries = serialized.FindProperty("outfits");
                entries.arraySize = outfits.Length;
                for (int i = 0; i < outfits.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("logicalName").stringValue = outfits[i].gameObject.name.Replace("collector-outfit-", string.Empty);
                    entry.FindPropertyRelative("outfitPrefab").objectReferenceValue = outfits[i].gameObject;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var recipe = new MeshRecipeDocument { wordSource = source };
                for (int i = 0; i < outfits.Length; i++) recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = outfits[i].gameObject.name.Replace("collector-outfit-", string.Empty), declaredKind = StackMachineBindingKind.Resource });
                if (source.Contains("$face")) recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "face", declaredKind = StackMachineBindingKind.Resource });
                return new ShapeSyncDocument { MeshRecipe = recipe, MeshBinding = binding };
            }

            internal void AddFigureNormalSources(string entryName, string targetName, out Texture2D baseTexture, out Texture2D targetTexture)
            {
                baseTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                targetTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                objects.Add(baseTexture);
                objects.Add(targetTexture);
                MeshBinding binding = null;
                for (int i = 0; i < objects.Count; i++)
                {
                    if (objects[i] is MeshBinding candidate) { binding = candidate; break; }
                }
                Assert.That(binding, Is.Not.Null, "CreateDocument must be called before configuring Normal sources.");
                var serialized = new SerializedObject(binding);
                SerializedProperty owners = serialized.FindProperty("normalOwners");
                owners.arraySize = 1;
                SerializedProperty owner = owners.GetArrayElementAtIndex(0);
                owner.FindPropertyRelative("outfitRegistryId").stringValue = string.Empty;
                SerializedProperty targets = owner.FindPropertyRelative("targets");
                targets.arraySize = 2;
                SetNormalTarget(targets.GetArrayElementAtIndex(0), string.Empty, entryName, baseTexture);
                SetNormalTarget(targets.GetArrayElementAtIndex(1), targetName, entryName, targetTexture);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            internal void AddFigureNormalBlender(string entryName)
            {
                NormalBlender blender = Figure.AddComponent<NormalBlender>();
                var serialized = new SerializedObject(blender);
                SerializedProperty entries = serialized.FindProperty("entries");
                entries.arraySize = 1;
                entries.GetArrayElementAtIndex(0).stringValue = entryName;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            internal void CreateRendererChild(GameObject parent, string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(parent.transform, false);
                child.AddComponent<SkinnedMeshRenderer>();
                objects.Add(child);
            }

            public void Dispose()
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }

            private static Material CreateMaterial()
            {
                Shader shader = Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard");
                return new Material(shader);
            }

            private static Mesh CreateMinimalMesh()
            {
                var mesh = new Mesh();
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                return mesh;
            }

            private static void SetNormalTarget(SerializedProperty target, string targetName, string entryName, Texture2D texture)
            {
                target.FindPropertyRelative("targetName").stringValue = targetName;
                SerializedProperty textures = target.FindPropertyRelative("textures");
                textures.arraySize = 1;
                SerializedProperty item = textures.GetArrayElementAtIndex(0);
                item.FindPropertyRelative("entryName").stringValue = entryName;
                item.FindPropertyRelative("normalTexture").objectReferenceValue = texture;
            }

            private static void ConfigureProxy(MaterialProxy proxy, SkinnedMeshRenderer renderer, string entryName, Material material, MaterialShaderAdapter adapter)
            {
                renderer.sharedMaterial = material;
                var serialized = new SerializedObject(proxy);
                SerializedProperty entries = serialized.FindProperty("entries");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("entryName").stringValue = entryName;
                entry.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                entry.FindPropertyRelative("materialChannel").intValue = 0;
                entry.FindPropertyRelative("adapter").objectReferenceValue = adapter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
