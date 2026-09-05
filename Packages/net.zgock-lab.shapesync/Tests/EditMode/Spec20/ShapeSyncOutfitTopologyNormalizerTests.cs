// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncOutfitTopologyNormalizerTests
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<GameObject> gameObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            meshes.Clear();
            foreach (GameObject gameObject in gameObjects) if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            gameObjects.Clear();
        }

        [Test]
        public void KnownVertexPermutationAndTriangleShuffle_RestoresAllAttributesAndWinding()
        {
            Mesh baseMesh = Track(CreateRichMesh("Base"));
            int[] expectedPermutation = { 2, 4, 1, 0, 3 };
            Mesh targetMesh = Track(CreatePermutedRichMesh(baseMesh, expectedPermutation));
            Vector3[] originalBaseVertices = baseMesh.vertices;
            int[][] originalBaseIndices = Enumerable.Range(0, baseMesh.subMeshCount).Select(baseMesh.GetIndices).ToArray();
            Matrix4x4[] originalBindposes = targetMesh.bindposes;

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Hair/Renderer", out int[] permutation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(permutation, Is.EqualTo(expectedPermutation));
            CollectionAssert.AreEqual(originalBaseVertices, baseMesh.vertices, "Base mesh must remain untouched.");
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
                CollectionAssert.AreEqual(originalBaseIndices[submesh], baseMesh.GetIndices(submesh), "Base indices must remain untouched.");
            CollectionAssert.AreEqual(baseMesh.vertices.Select(value => value + new Vector3(0.25f, -0.1f, 0.05f)).ToArray(), targetMesh.vertices);
            CollectionAssert.AreEqual(baseMesh.normals, targetMesh.normals);
            CollectionAssert.AreEqual(baseMesh.tangents, targetMesh.tangents);
            CollectionAssert.AreEqual(baseMesh.colors, targetMesh.colors);
            CollectionAssert.AreEqual(baseMesh.boneWeights, targetMesh.boneWeights);
            CollectionAssert.AreEqual(originalBindposes, targetMesh.bindposes, "bindposes must not be remapped.");
            for (int channel = 0; channel < 8; channel++)
            {
                var expected = new List<Vector4>();
                var actual = new List<Vector4>();
                baseMesh.GetUVs(channel, expected);
                targetMesh.GetUVs(channel, actual);
                CollectionAssert.AreEqual(expected, actual, "UV channel " + channel + " was not remapped with the vertex.");
            }
            for (int shape = 0; shape < baseMesh.blendShapeCount; shape++)
            {
                int actualShape = targetMesh.GetBlendShapeIndex(baseMesh.GetBlendShapeName(shape));
                Assert.That(actualShape, Is.GreaterThanOrEqualTo(0));
                for (int frame = 0; frame < baseMesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    var expectedVertices = new Vector3[baseMesh.vertexCount];
                    var expectedNormals = new Vector3[baseMesh.vertexCount];
                    var expectedTangents = new Vector3[baseMesh.vertexCount];
                    var actualVertices = new Vector3[targetMesh.vertexCount];
                    var actualNormals = new Vector3[targetMesh.vertexCount];
                    var actualTangents = new Vector3[targetMesh.vertexCount];
                    baseMesh.GetBlendShapeFrameVertices(shape, frame, expectedVertices, expectedNormals, expectedTangents);
                    targetMesh.GetBlendShapeFrameVertices(actualShape, frame, actualVertices, actualNormals, actualTangents);
                    CollectionAssert.AreEqual(expectedVertices, actualVertices);
                    CollectionAssert.AreEqual(expectedNormals, actualNormals);
                    CollectionAssert.AreEqual(expectedTangents, actualTangents);
                }
            }
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
                CollectionAssert.AreEqual(baseMesh.GetIndices(submesh), targetMesh.GetIndices(submesh), "Base index buffer must be adopted.");
        }

        [Test]
        public void AlreadyIdenticalMesh_ReturnsIdentityPermutation()
        {
            Mesh baseMesh = Track(CreateRichMesh("Base"));
            Mesh targetMesh = Track(UnityEngine.Object.Instantiate(baseMesh));
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Dress/Base", string.Empty, out int[] permutation, out int[] boneMap, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(permutation, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(boneMap, Is.EqualTo(new[] { 0, 1 }));
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
                CollectionAssert.AreEqual(baseMesh.GetIndices(submesh), targetMesh.GetIndices(submesh));
        }

        [Test]
        public void BonePermutation_NormalizesWeightsBindposesAndRendererBonesOnIdentityVertexPath()
        {
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, new[] { 1, 2, 0 }));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, new[] { 1, 2, 0 }));
            Matrix4x4[] originalTargetBindposes = targetMesh.bindposes;
            GameObject baseObject = Track(new GameObject("BaseRenderer"));
            GameObject targetObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetObject.AddComponent<SkinnedMeshRenderer>();
            Transform[] baseBones = { Track(new GameObject("BaseBone0")).transform, Track(new GameObject("BaseBone1")).transform, Track(new GameObject("BaseBone2")).transform, Track(new GameObject("BaseBone3")).transform };
            Transform[] targetBones = { Track(new GameObject("TargetBone0")).transform, Track(new GameObject("TargetBone1")).transform, Track(new GameObject("TargetBone2")).transform, Track(new GameObject("TargetBone3")).transform };
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = baseBones;
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = targetBones;

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", out int[] permutation, out int[] boneMap, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(permutation, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(boneMap, Is.EqualTo(new[] { 1, 2, 0, 3 }));
            AssertBoneWeightDataEqual(baseMesh, targetMesh);
            Assert.That(targetMesh.bindposes, Is.EqualTo(new[] { originalTargetBindposes[1], originalTargetBindposes[2], originalTargetBindposes[0], originalTargetBindposes[3] }));
            Assert.That(targetRenderer.bones, Is.EqualTo(new[] { targetBones[1], targetBones[2], targetBones[0], targetBones[3] }));
        }

        [Test]
        public void NonIdentityVertexAndBonePermutation_NormalizesBothSpacesTogether()
        {
            int[] vertexPermutation = { 2, 0, 3, 1 };
            int[] expectedBoneMap = { 1, 2, 0, 3 };
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, expectedBoneMap));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, expectedBoneMap, vertexPermutation));
            Matrix4x4[] originalTargetBindposes = targetMesh.bindposes;
            GameObject baseObject = Track(new GameObject("BaseRenderer"));
            GameObject targetObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetObject.AddComponent<SkinnedMeshRenderer>();
            Transform[] baseBones = { Track(new GameObject("BaseBone0")).transform, Track(new GameObject("BaseBone1")).transform, Track(new GameObject("BaseBone2")).transform, Track(new GameObject("BaseBone3")).transform };
            Transform[] targetBones = { Track(new GameObject("TargetBone0")).transform, Track(new GameObject("TargetBone1")).transform, Track(new GameObject("TargetBone2")).transform, Track(new GameObject("TargetBone3")).transform };
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = baseBones;
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = targetBones;

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", out int[] permutation, out int[] boneMap, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(permutation, Is.EqualTo(vertexPermutation));
            Assert.That(boneMap, Is.EqualTo(expectedBoneMap));
            AssertBoneWeightDataEqual(baseMesh, targetMesh);
            Assert.That(targetMesh.bindposes, Is.EqualTo(new[] { originalTargetBindposes[1], originalTargetBindposes[2], originalTargetBindposes[0], originalTargetBindposes[3] }));
            Assert.That(targetRenderer.bones, Is.EqualTo(new[] { targetBones[1], targetBones[2], targetBones[0], targetBones[3] }));
        }

        [Test]
        public void ExtraBoneHierarchy_ReparentsCyclicPermutationThroughTemporaryNames()
        {
            int[] expectedBoneMap = { 0, 2, 1, 3 };
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, expectedBoneMap));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, expectedBoneMap));
            GameObject baseRootObject = Track(new GameObject("BaseOutfit"));
            GameObject targetRootObject = Track(new GameObject("TargetOutfit"));
            Transform baseFigure = CreateChild(baseRootObject.transform, "FigureBone");
            Transform baseA = CreateChild(baseFigure, "A");
            Transform baseB = CreateChild(baseA, "B");
            Transform baseDummy = CreateChild(baseFigure, "Dummy");
            Transform targetFigure = CreateChild(targetRootObject.transform, "FigureBone");
            Transform targetB = CreateChild(targetFigure, "B");
            Transform targetA = CreateChild(targetB, "A");
            Transform targetDummy = CreateChild(targetFigure, "Dummy");
            GameObject figureRootObject = Track(new GameObject("CanonicalFigure"));
            CreateChild(figureRootObject.transform, "FigureBone");
            CreateChild(figureRootObject.transform, "Dummy");

            GameObject baseRendererObject = Track(new GameObject("BaseRenderer"));
            GameObject targetRendererObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseRendererObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetRendererObject.AddComponent<SkinnedMeshRenderer>();
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = new[] { baseFigure, baseA, baseB, baseDummy };
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = new[] { targetFigure, targetB, targetA, targetDummy };

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", baseRootObject.transform, targetRootObject.transform,
                figureRootObject.transform, out int[] permutation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(permutation, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(targetA.parent, Is.SameAs(targetFigure));
            Assert.That(targetB.parent, Is.SameAs(targetA));
            Assert.That(targetRootObject.transform.Find("FigureBone/A"), Is.SameAs(targetA));
            Assert.That(targetRootObject.transform.Find("FigureBone/A/B"), Is.SameAs(targetB));
            Assert.That(targetRenderer.bones, Is.EqualTo(new[] { targetFigure, targetA, targetB, targetDummy }));
        }

        [Test]
        public void ExtraBoneHierarchy_IdentityPathsDoNotRewritePrefabHierarchy()
        {
            int[] identity = { 0, 1, 2, 3 };
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, identity));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, identity));
            GameObject baseRootObject = Track(new GameObject("BaseOutfit"));
            GameObject targetRootObject = Track(new GameObject("TargetOutfit"));
            Transform baseFigure = CreateChild(baseRootObject.transform, "FigureBone");
            CreateChild(baseFigure, "Extra");
            Transform baseDummy = CreateChild(baseFigure, "Dummy");
            Transform targetFigure = CreateChild(targetRootObject.transform, "FigureBone");
            Transform targetExtra = CreateChild(targetFigure, "Extra");
            Transform targetDummy = CreateChild(targetFigure, "Dummy");
            GameObject figureRootObject = Track(new GameObject("CanonicalFigure"));
            CreateChild(figureRootObject.transform, "FigureBone");
            CreateChild(figureRootObject.transform, "Dummy");
            GameObject baseRendererObject = Track(new GameObject("BaseRenderer"));
            GameObject targetRendererObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseRendererObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetRendererObject.AddComponent<SkinnedMeshRenderer>();
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = new[] { baseFigure, baseFigure.Find("Extra"), baseDummy, baseDummy };
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = new[] { targetFigure, targetExtra, targetDummy, targetDummy };
            Transform originalParent = targetExtra.parent;
            string originalName = targetExtra.name;
            int originalSiblingIndex = targetExtra.GetSiblingIndex();

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", baseRootObject.transform, targetRootObject.transform,
                figureRootObject.transform, out _, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            Assert.That(targetExtra.parent, Is.SameAs(originalParent));
            Assert.That(targetExtra.name, Is.EqualTo(originalName));
            Assert.That(targetExtra.GetSiblingIndex(), Is.EqualTo(originalSiblingIndex));
            Assert.That(targetRootObject.transform.Find("FigureBone/Extra"), Is.SameAs(targetExtra));
        }

        [Test]
        public void ExtraBoneHierarchy_RejectsMissingRootsAndDoesNotMutateTarget()
        {
            int[] identity = { 0, 1, 2, 3 };
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, identity));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, identity));
            Matrix4x4[] originalBindposes = targetMesh.bindposes;
            GameObject baseRootObject = Track(new GameObject("BaseOutfit"));
            GameObject targetRootObject = Track(new GameObject("TargetOutfit"));
            Transform baseFigure = CreateChild(baseRootObject.transform, "FigureBone");
            Transform baseExtra = CreateChild(baseFigure, "Extra");
            Transform baseDummy = CreateChild(baseFigure, "Dummy");
            Transform targetFigure = CreateChild(targetRootObject.transform, "FigureBone");
            Transform targetExtra = CreateChild(targetFigure, "Extra");
            Transform targetDummy = CreateChild(targetFigure, "Dummy");
            GameObject figureRootObject = Track(new GameObject("CanonicalFigure"));
            CreateChild(figureRootObject.transform, "FigureBone");
            CreateChild(figureRootObject.transform, "Dummy");
            GameObject baseRendererObject = Track(new GameObject("BaseRenderer"));
            GameObject targetRendererObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseRendererObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetRendererObject.AddComponent<SkinnedMeshRenderer>();
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = new[] { baseFigure, baseExtra, baseDummy, baseDummy };
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = new[] { targetFigure, targetExtra, targetDummy, targetDummy };

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", baseRootObject.transform, null, figureRootObject.transform,
                out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyExtraBoneRootsInvalid"));
            Assert.That(targetMesh.bindposes, Is.EqualTo(originalBindposes));
            Assert.That(targetExtra.name, Is.EqualTo("Extra"));
        }

        [Test]
        public void ExtraBoneHierarchy_RejectsBoneTableMismatch()
        {
            int[] identity = { 0, 1, 2, 3 };
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, identity));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, identity));
            targetMesh.bindposes = targetMesh.bindposes.Take(3).ToArray();
            GameObject baseRootObject = Track(new GameObject("BaseOutfit"));
            GameObject targetRootObject = Track(new GameObject("TargetOutfit"));
            Transform baseFigure = CreateChild(baseRootObject.transform, "FigureBone");
            Transform baseExtra = CreateChild(baseFigure, "Extra");
            Transform baseDummy = CreateChild(baseFigure, "Dummy");
            Transform targetFigure = CreateChild(targetRootObject.transform, "FigureBone");
            Transform targetExtra = CreateChild(targetFigure, "Extra");
            Transform targetDummy = CreateChild(targetFigure, "Dummy");
            GameObject figureRootObject = Track(new GameObject("CanonicalFigure"));
            CreateChild(figureRootObject.transform, "FigureBone");
            CreateChild(figureRootObject.transform, "Dummy");
            GameObject baseRendererObject = Track(new GameObject("BaseRenderer"));
            GameObject targetRendererObject = Track(new GameObject("TargetRenderer"));
            SkinnedMeshRenderer baseRenderer = baseRendererObject.AddComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer targetRenderer = targetRendererObject.AddComponent<SkinnedMeshRenderer>();
            baseRenderer.sharedMesh = baseMesh;
            baseRenderer.bones = new[] { baseFigure, baseExtra, baseDummy, baseDummy };
            targetRenderer.sharedMesh = targetMesh;
            targetRenderer.bones = new[] { targetFigure, targetExtra, targetDummy };

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseRenderer, targetRenderer,
                "Hair/FBM", "Merged/Hair", baseRootObject.transform, targetRootObject.transform,
                figureRootObject.transform, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyExtraBoneBoneTableMismatch"));
        }

        [Test]
        public void BonePermutation_RejectsNonClosedWeightedDomainWithoutMutatingTarget()
        {
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, new[] { 1, 2, 0 }));
            Mesh targetMesh = Track(CreateBoneMappedMesh(true, new[] { 1, 2, 3 }));
            Matrix4x4[] originalBindposes = targetMesh.bindposes;
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Merged/Hair", out _, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyBoneMapNotClosed"));
            Assert.That(targetMesh.bindposes, Is.EqualTo(originalBindposes));
        }

        [Test]
        public void BonePermutation_IsDeterministicForTheSameInput()
        {
            Mesh baseMesh = Track(CreateBoneMappedMesh(false, new[] { 1, 2, 0 }));
            Mesh first = Track(CreateBoneMappedMesh(true, new[] { 1, 2, 0 }));
            Mesh second = Track(CreateBoneMappedMesh(true, new[] { 1, 2, 0 }));
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, first,
                "Hair/FBM", "Merged/Hair", out _, out int[] firstMap, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.ToString());
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, second,
                "Hair/FBM", "Merged/Hair", out _, out int[] secondMap, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.ToString());
            CollectionAssert.AreEqual(firstMap, secondMap);
        }

        [Test]
        public void AmbiguousResolution_IsDeterministicForTheSameInput()
        {
            Mesh baseMesh = Track(CreateAmbiguousPropagationMesh(false));
            Mesh first = Track(CreateAmbiguousPropagationMesh(true));
            Mesh second = Track(CreateAmbiguousPropagationMesh(true));
            int[] expectedPermutation = ExpectedAmbiguousPropagationPermutation();
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, first,
                "Hair/FBM", "Hair/Renderer", out int[] firstPermutation, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.ToString());
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, second,
                "Hair/FBM", "Hair/Renderer", out int[] secondPermutation, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.ToString());
            CollectionAssert.AreEqual(expectedPermutation, firstPermutation, "The ambiguous components must resolve to the ground-truth assignment.");
            CollectionAssert.AreEqual(expectedPermutation, secondPermutation, "The ambiguous components must resolve to the same ground-truth assignment.");
            CollectionAssert.AreEqual(firstPermutation, secondPermutation);
            CollectionAssert.AreEqual(first.vertices, second.vertices);
            for (int submesh = 0; submesh < first.subMeshCount; submesh++)
                CollectionAssert.AreEqual(first.GetIndices(submesh), second.GetIndices(submesh));
        }

        [Test]
        public void AmbiguousComponentPropagation_RestoresGroundTruth()
        {
            Mesh baseMesh = Track(CreateAmbiguousPropagationMesh(false));
            Mesh targetMesh = Track(CreateAmbiguousPropagationMesh(true));
            int[] expectedPermutation = ExpectedAmbiguousPropagationPermutation();

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Hair/Renderer", out int[] permutation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            CollectionAssert.AreEqual(expectedPermutation, permutation);
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
                CollectionAssert.AreEqual(baseMesh.GetIndices(submesh), targetMesh.GetIndices(submesh));
        }

        [Test]
        public void VariableBoneWeights_AreRemappedWithVertices()
        {
            Mesh baseMesh = Track(CreateVariableBoneWeightMesh(false));
            Mesh targetMesh = Track(CreateVariableBoneWeightMesh(true));
            int[] expectedPermutation = { 2, 4, 1, 0, 3 };

            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Hair/Renderer", out int[] permutation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.ToString());
            CollectionAssert.AreEqual(expectedPermutation, permutation);
            AssertBoneWeightDataEqual(baseMesh, targetMesh);
        }

        [Test]
        public void BuildSelectedMesh_PreservesVariableBoneWeights()
        {
            Mesh source = Track(CreateVariableBoneWeightMesh(false));
            Mesh selected = Track(ShapeSyncMeshOutfitImport.BuildSelectedMesh(source, new[] { true }));

            AssertBoneWeightDataEqual(source, selected);
        }

        [Test]
        public void VertexCountMismatch_IsRejectedWithStructuredDiagnostic()
        {
            Mesh baseMesh = Track(CreateRichMesh("Base"));
            Mesh targetMesh = Track(new Mesh { name = "Target" });
            targetMesh.vertices = new[] { Vector3.zero };
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Coat/Tall", "Merged/Renderer", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyVertexCountMismatch"));
            Assert.That(diagnostic.bindingName, Is.EqualTo("Coat/Tall"));
            StringAssert.Contains("renderer=Merged/Renderer", diagnostic.detail);
        }

        [Test]
        public void Uv0MultisetMismatch_IsRejectedAsDifferentExport()
        {
            Mesh baseMesh = Track(CreateRichMesh("Base"));
            Mesh targetMesh = Track(UnityEngine.Object.Instantiate(baseMesh));
            var uv0 = new List<Vector4>();
            targetMesh.GetUVs(0, uv0);
            uv0[0] = new Vector4(999f, 0f, 0f, 0f);
            targetMesh.SetUVs(0, uv0);
            targetMesh.SetTriangles(new[] { 1, 2, 0, 2, 3, 4 }, 0, false);
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Coat/Tall", "Merged/Renderer", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyUv0MultisetMismatch"));
            StringAssert.Contains("different assets", diagnostic.message);
        }

        [Test]
        public void NoUniqueAnchor_IsRejected()
        {
            Mesh baseMesh = Track(CreateRepeatedComponentMesh(2, false));
            Mesh targetMesh = Track(CreateRepeatedComponentMesh(2, true));
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Hair/Renderer", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyAnchorMissing"));
        }

        [Test]
        public void LeaveOneOutLowConfidence_IsRejected()
        {
            Mesh baseMesh = Track(CreateAuditMesh(false));
            Mesh targetMesh = Track(CreateAuditMesh(true));
            Assert.That(ShapeSyncOutfitTopologyNormalizer.TryNormalizeInPlace(baseMesh, targetMesh,
                "Hair/FBM", "Hair/Renderer", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutfitTopologyAuditLowConfidence"), diagnostic?.ToString());
            StringAssert.Contains("threshold=1", diagnostic.detail);
            StringAssert.Contains("renderer=Hair/Renderer", diagnostic.detail);
        }

        private Mesh Track(Mesh mesh)
        {
            meshes.Add(mesh);
            return mesh;
        }

        private GameObject Track(GameObject gameObject)
        {
            gameObjects.Add(gameObject);
            return gameObject;
        }

        private Transform CreateChild(Transform parent, string name)
        {
            GameObject child = Track(new GameObject(name));
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Mesh CreateRichMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(2f, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.up, Vector3.right, -Vector3.forward, -Vector3.up };
            mesh.tangents = new[]
            {
                new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, -1f), new Vector4(0f, 0f, 1f, 1f),
                new Vector4(1f, 1f, 0f, -1f), new Vector4(0f, 1f, 1f, 1f)
            };
            mesh.colors = new[]
            {
                Color.red, Color.green, Color.blue, Color.yellow, Color.magenta
            };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, boneIndex1 = 1, weight0 = 0.25f, weight1 = 0.75f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            mesh.bindposes = new[]
            {
                Matrix4x4.identity,
                Matrix4x4.TRS(new Vector3(2f, 3f, 4f), Quaternion.Euler(10f, 20f, 30f), Vector3.one)
            };
            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>();
                for (int vertex = 0; vertex < mesh.vertexCount; vertex++) uv.Add(new Vector4(vertex + channel * 10f, channel, vertex * 0.5f, 1f));
                mesh.SetUVs(channel, uv);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 2, 3, 4 }, 0, false);
            mesh.SetTriangles(new[] { 2, 4, 3 }, 1, false);
            var delta = new Vector3[mesh.vertexCount];
            var normalDelta = new Vector3[mesh.vertexCount];
            var tangentDelta = new Vector3[mesh.vertexCount];
            for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
            {
                delta[vertex] = new Vector3(vertex + 0.1f, vertex * 2f, -vertex);
                normalDelta[vertex] = Vector3.one * (vertex + 1f);
                tangentDelta[vertex] = Vector3.right * (vertex + 2f);
            }
            mesh.AddBlendShapeFrame("Smile", 50f, delta, normalDelta, tangentDelta);
            mesh.AddBlendShapeFrame("Smile", 100f, delta, normalDelta, tangentDelta);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePermutedRichMesh(Mesh source, int[] permutation)
        {
            var mesh = new Mesh { name = "Target" };
            mesh.indexFormat = source.indexFormat;
            Vector3 offset = new Vector3(0.25f, -0.1f, 0.05f);
            mesh.vertices = Permute(source.vertices, permutation, value => value + offset);
            mesh.normals = Permute(source.normals, permutation, value => value);
            mesh.tangents = Permute(source.tangents, permutation, value => value);
            mesh.colors = Permute(source.colors, permutation, value => value);
            mesh.boneWeights = Permute(source.boneWeights, permutation, value => value);
            mesh.bindposes = new[] { Matrix4x4.TRS(Vector3.one, Quaternion.Euler(2f, 3f, 4f), new Vector3(2f, 1f, 3f)), Matrix4x4.identity };
            for (int channel = 0; channel < 8; channel++)
            {
                var sourceUv = new List<Vector4>();
                source.GetUVs(channel, sourceUv);
                mesh.SetUVs(channel, Permute(sourceUv.ToArray(), permutation, value => value));
            }
            mesh.subMeshCount = source.subMeshCount;
            mesh.SetTriangles(new[] { permutation[2], permutation[0], permutation[1], permutation[4], permutation[2], permutation[3] }, 0, false);
            mesh.SetTriangles(new[] { permutation[4], permutation[3], permutation[2] }, 1, false);
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                var vertices = new Vector3[source.vertexCount];
                var normals = new Vector3[source.vertexCount];
                var tangents = new Vector3[source.vertexCount];
                source.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                    Permute(vertices, permutation, value => value), Permute(normals, permutation, value => value), Permute(tangents, permutation, value => value));
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBoneMappedMesh(bool target, int[] targetBoneByBase, int[] vertexPermutation = null)
        {
            var mesh = new Mesh { name = target ? "BoneMappedTarget" : "BoneMappedBase" };
            Vector3[] baseVertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f)
            };
            Vector4[] baseUvs = new[]
            {
                new Vector4(0f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 1f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f)
            };
            mesh.vertices = target && vertexPermutation != null ? Permute(baseVertices, vertexPermutation, value => value) : baseVertices;
            mesh.SetUVs(0, target && vertexPermutation != null ? Permute(baseUvs, vertexPermutation, value => value) : baseUvs);
            mesh.subMeshCount = 1;
            int[] baseIndices = { 0, 1, 2, 0, 2, 3 };
            mesh.SetTriangles(target && vertexPermutation != null ? baseIndices.Select(index => vertexPermutation[index]).ToArray() : baseIndices, 0, false);
            mesh.bindposes = new[]
            {
                Matrix4x4.TRS(new Vector3(10f, 0f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(20f, 0f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(30f, 0f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(40f, 0f, 0f), Quaternion.identity, Vector3.one)
            };
            int[] baseBones = { 0, 1, 2, 0 };
            var boneWeights = new BoneWeight1[baseBones.Length];
            for (int vertex = 0; vertex < baseBones.Length; vertex++)
            {
                int outputVertex = target && vertexPermutation != null ? vertexPermutation[vertex] : vertex;
                boneWeights[outputVertex] = new BoneWeight1 { boneIndex = target ? targetBoneByBase[baseBones[vertex]] : baseBones[vertex], weight = 1f };
            }
            var bonesPerVertex = new NativeArray<byte>(new byte[] { 1, 1, 1, 1 }, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(boneWeights, Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(bonesPerVertex, weights);
            }
            finally
            {
                bonesPerVertex.Dispose();
                weights.Dispose();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateVariableBoneWeightMesh(bool target, bool validBoneIndices = true)
        {
            int[] permutation = { 2, 4, 1, 0, 3 };
            int[] bonesPerVertex = { 5, 2, 1, 4, 3 };
            int[] baseIndices = { 0, 1, 2, 2, 3, 4 };
            var mesh = new Mesh { name = target ? "VariableBoneWeightTarget" : "VariableBoneWeightBase" };
            var baseVertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(2f, 1f, 0f)
            };
            var vertices = new Vector3[baseVertices.Length];
            var uv = new List<Vector4>(baseVertices.Length);
            for (int baseVertex = 0; baseVertex < baseVertices.Length; baseVertex++)
            {
                int outputVertex = target ? permutation[baseVertex] : baseVertex;
                vertices[outputVertex] = baseVertices[baseVertex] + (target ? new Vector3(0.25f, -0.1f, 0.05f) : Vector3.zero);
                uv.Add(Vector4.zero);
            }
            for (int baseVertex = 0; baseVertex < baseVertices.Length; baseVertex++)
            {
                int outputVertex = target ? permutation[baseVertex] : baseVertex;
                uv[outputVertex] = new Vector4(baseVertex + 1f, baseVertex * 2f, 0f, 1f);
            }
            mesh.vertices = vertices;
            mesh.SetUVs(0, uv);
            mesh.bindposes = Enumerable.Range(0, 5).Select(_ => Matrix4x4.identity).ToArray();
            SetVariableBoneWeights(mesh, bonesPerVertex, permutation, target, validBoneIndices);
            mesh.subMeshCount = 1;
            int[] indices = baseIndices.Select(index => target ? permutation[index] : index).ToArray();
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void SetVariableBoneWeights(Mesh mesh, int[] bonesPerVertex, int[] permutation, bool target, bool validBoneIndices = true)
        {
            byte[] counts = new byte[bonesPerVertex.Length];
            var orderedWeights = new List<BoneWeight1>();
            for (int outputVertex = 0; outputVertex < bonesPerVertex.Length; outputVertex++)
            {
                int baseVertex = target ? Array.IndexOf(permutation, outputVertex) : outputVertex;
                counts[outputVertex] = (byte)bonesPerVertex[baseVertex];
                for (int influence = 0; influence < bonesPerVertex[baseVertex]; influence++)
                    orderedWeights.Add(new BoneWeight1 { boneIndex = validBoneIndices ? (baseVertex + influence) % 5 : baseVertex * 10 + influence, weight = 1f / bonesPerVertex[baseVertex] });
            }
            var nativeCounts = new NativeArray<byte>(counts, Allocator.Temp);
            var nativeWeights = new NativeArray<BoneWeight1>(orderedWeights.ToArray(), Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(nativeCounts, nativeWeights);
            }
            finally
            {
                nativeCounts.Dispose();
                nativeWeights.Dispose();
            }
        }

        private static void AssertBoneWeightDataEqual(Mesh expected, Mesh actual)
        {
            NativeArray<byte> expectedCounts = expected.GetBonesPerVertex();
            NativeArray<byte> actualCounts = actual.GetBonesPerVertex();
            Assert.That(actualCounts.Length, Is.EqualTo(expectedCounts.Length));
            for (int vertex = 0; vertex < expectedCounts.Length; vertex++)
                Assert.That(actualCounts[vertex], Is.EqualTo(expectedCounts[vertex]), "Bone influence count mismatch at vertex " + vertex);

            NativeArray<BoneWeight1> expectedWeights = expected.GetAllBoneWeights();
            NativeArray<BoneWeight1> actualWeights = actual.GetAllBoneWeights();
            Assert.That(actualWeights.Length, Is.EqualTo(expectedWeights.Length));
            for (int index = 0; index < expectedWeights.Length; index++)
            {
                Assert.That(actualWeights[index].boneIndex, Is.EqualTo(expectedWeights[index].boneIndex), "Bone index mismatch at influence " + index);
                Assert.That(actualWeights[index].weight, Is.EqualTo(expectedWeights[index].weight).Within(1e-6f), "Bone weight mismatch at influence " + index);
            }
        }

        private static T[] Permute<T>(T[] source, int[] permutation, Func<T, T> transform)
        {
            var result = new T[source.Length];
            for (int baseIndex = 0; baseIndex < permutation.Length; baseIndex++) result[permutation[baseIndex]] = transform(source[baseIndex]);
            return result;
        }

        private static Mesh CreateRepeatedComponentMesh(int componentCount, bool target)
        {
            var mesh = new Mesh { name = "Repeated" };
            var vertices = new Vector3[componentCount * 3];
            var uv = new List<Vector4>();
            var indices = new int[componentCount * 3];
            for (int component = 0; component < componentCount; component++)
            for (int local = 0; local < 3; local++)
            {
                int index = component * 3 + local;
                vertices[index] = new Vector3(component * 2f + (local == 1 ? 0.5f : 0f), local == 2 ? 0.5f : 0f, 0f);
                uv.Add(new Vector4(local, local * 2f, 0f, 1f));
                indices[index] = target ? component * 3 + ((local + 1) % 3) : index;
            }
            mesh.vertices = vertices;
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateAuditMesh(bool target)
        {
            const int anchorCount = 5;
            const int ambiguousCount = 2;
            int vertexCount = (anchorCount + ambiguousCount) * 3;
            var mesh = new Mesh { name = target ? "AuditTarget" : "AuditBase" };
            var vertices = new Vector3[vertexCount];
            var uv = new List<Vector4>(vertexCount);
            var indices = new int[vertexCount];
            for (int component = 0; component < anchorCount + ambiguousCount; component++)
            for (int local = 0; local < 3; local++)
            {
                int index = component * 3 + local;
                float center = component < anchorCount ? 1f : 1f;
                if (target && component == anchorCount) center = 0f;
                if (target && component == anchorCount + 1) center = 2f;
                vertices[index] = new Vector3(center + (local == 1 ? 0.1f : 0f), local == 2 ? 0.1f : 0f, 0f);
                float uvBase = component < anchorCount ? 100f + component * 10f : 10f;
                uv.Add(new Vector4(uvBase + local, local * 2f, 0f, 1f));
                indices[index] = target ? component * 3 + ((local + 1) % 3) : index;
            }
            mesh.vertices = vertices;
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateAmbiguousPropagationMesh(bool target)
        {
            const int anchorCount = 5;
            const int ambiguousCount = 2;
            int componentCount = anchorCount + ambiguousCount;
            int[] targetSourceComponents = { 0, 1, 2, 3, 4, 6, 5 };
            var mesh = new Mesh { name = target ? "AmbiguousTarget" : "AmbiguousBase" };
            var vertices = new Vector3[componentCount * 3];
            var uv = new List<Vector4>(vertices.Length);
            var indices = new int[vertices.Length];
            for (int slot = 0; slot < componentCount; slot++)
            {
                int sourceComponent = target ? targetSourceComponents[slot] : slot;
                float center = sourceComponent * 4f + (target ? 0.75f : 0f);
                float uvBase = sourceComponent < anchorCount ? 100f + sourceComponent * 10f : 10f;
                for (int local = 0; local < 3; local++)
                {
                    int index = slot * 3 + local;
                    vertices[index] = new Vector3(center + (local == 1 ? 0.1f : 0f), local == 2 ? 0.1f : 0f, 0f);
                    uv.Add(new Vector4(uvBase + local, local * 2f, 0f, 1f));
                    indices[index] = target ? slot * 3 + ((local + 1) % 3) : index;
                }
            }
            mesh.vertices = vertices;
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] ExpectedAmbiguousPropagationPermutation()
        {
            int[] targetSlotBySourceComponent = { 0, 1, 2, 3, 4, 6, 5 };
            var permutation = new int[targetSlotBySourceComponent.Length * 3];
            for (int component = 0; component < targetSlotBySourceComponent.Length; component++)
            for (int local = 0; local < 3; local++)
                permutation[component * 3 + local] = targetSlotBySourceComponent[component] * 3 + local;
            return permutation;
        }
    }
}
#endif
