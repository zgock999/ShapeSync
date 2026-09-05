// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class DynamicMorphAdapterPcmTests
    {
        [Test]
        public void CommitAndDetach_TransferPayloadIntoReservedSlotsWithoutDetachRebuild()
        {
            GameObject figure = new GameObject("Spec10 Figure");
            Mesh source = CreateFigureMesh();
            Mesh payload = null;
            ProfileControlledMorphAsset asset = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });

                payload = Object.Instantiate(source);
                payload.ClearBlendShapes();
                payload.AddBlendShapeFrame("PCM_Coat", 100f, Deltas(source.vertexCount, 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_FBM_A_Coat", 100f, Deltas(source.vertexCount, 2f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                asset = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
                asset.ConfigureForBuild(payload, "Coat", new List<string> { "FBM_A" }, false);

                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, asset, 41, out ProfileControlledMorphBinding binding, out string error), Is.True, error);
                Assert.That(renderer.sharedMesh, Is.SameAs(source), "Preparation must not mutate the Figure renderer before OutfitAttacher commits.");
                Assert.That(binding.Commit(out error), Is.True, error);
                Mesh runtimeMesh = renderer.sharedMesh;
                Assert.That(runtimeMesh, Is.Not.SameAs(source));
                AssertFrame(runtimeMesh, 1, 1f);
                AssertFrame(runtimeMesh, 2, 2f);

                binding.ApplyBase();
                binding.ApplyFbmWeight(new FbmWeightChange("FBM_A", 0.5f, true));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(100f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(50f));
                binding.Dispose();
                Assert.That(renderer.sharedMesh, Is.SameAs(runtimeMesh), "Detach must not rebuild or replace the runtime Mesh.");
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(0f));
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(payload);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MultipleFbms_ApplyIndependentlyAndDetachClearsEveryPcmSlot()
        {
            GameObject figure = new GameObject("Spec10 Multiple FBM Figure");
            Mesh source = CreateFigureMesh(2);
            Mesh payload = null;
            ProfileControlledMorphAsset asset = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A", "BasicGirl" });

                payload = Object.Instantiate(source);
                payload.ClearBlendShapes();
                payload.AddBlendShapeFrame("PCM_Coat", 100f, Deltas(source.vertexCount, 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_FBM_A_Coat", 100f, Deltas(source.vertexCount, 2f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_BasicGirl_Coat", 100f, Deltas(source.vertexCount, 3f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                asset = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
                asset.ConfigureForBuild(payload, "Coat", new List<string> { "FBM_A", "BasicGirl" }, false);

                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, asset, 45, out ProfileControlledMorphBinding binding, out string error), Is.True, error);
                Assert.That(binding.Commit(out error), Is.True, error);
                Mesh runtimeMesh = renderer.sharedMesh;
                AssertFrame(runtimeMesh, 1, 1f);
                AssertFrame(runtimeMesh, 2, 2f);
                AssertFrame(runtimeMesh, 3, 3f);

                binding.ApplyBase();
                binding.ApplyFbmWeight(new FbmWeightChange("FBM_A", 0.25f, true));
                binding.ApplyFbmWeight(new FbmWeightChange("BasicGirl", 0.6f, true));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(100f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(25f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(60f).Within(0.001f));

                binding.ApplyFbmWeight(new FbmWeightChange("FBM_A", 0.8f, true));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(80f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(60f).Within(0.001f), "Updating one FBM must not overwrite the other PCM slot.");

                binding.Dispose();
                Assert.That(renderer.sharedMesh, Is.SameAs(runtimeMesh));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(0f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(payload);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Prepare_RejectsMismatchedBaseGeometryAndNon100FrameWeight()
        {
            GameObject figure = new GameObject("Spec10 Validation Figure");
            Mesh source = CreateFigureMesh();
            Mesh payload = null;
            ProfileControlledMorphAsset asset = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });

                payload = Object.Instantiate(source);
                payload.ClearBlendShapes();
                payload.AddBlendShapeFrame("PCM_Coat", 50f, Deltas(source.vertexCount, 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_FBM_A_Coat", 100f, Deltas(source.vertexCount, 2f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                asset = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
                asset.ConfigureForBuild(payload, "Coat", new List<string> { "FBM_A" }, false);
                Assert.That(adapter.TryPreparePcmAttach(asset, 42, out _, out string error), Is.False);
                StringAssert.Contains("weight 100", error);

                payload.ClearBlendShapes();
                Vector3[] changedVertices = payload.vertices;
                changedVertices[0] = new Vector3(9f, 0f, 0f);
                payload.vertices = changedVertices;
                payload.AddBlendShapeFrame("PCM_Coat", 100f, Deltas(source.vertexCount, 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_FBM_A_Coat", 100f, Deltas(source.vertexCount, 2f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                asset.ConfigureForBuild(payload, "Coat", new List<string> { "FBM_A" }, false);
                Assert.That(adapter.TryPreparePcmAttach(asset, 43, out _, out error), Is.False);
                StringAssert.Contains("base geometry", error);
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(payload);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MultipleAttachments_UseIndependentGroupsAndReuseDetachedGroup()
        {
            GameObject figure = new GameObject("Spec10 Multi Figure");
            Mesh source = CreateFigureMesh();
            Mesh payloadA = null;
            Mesh payloadB = null;
            ProfileControlledMorphAsset assetA = null;
            ProfileControlledMorphAsset assetB = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });
                assetA = CreatePayload(source, "CoatA", 1f, out payloadA);
                assetB = CreatePayload(source, "CoatB", 3f, out payloadB);

                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, assetA, 51, out ProfileControlledMorphBinding first, out string error), Is.True, error);
                Assert.That(first.Commit(out error), Is.True, error);
                first.ApplyBase();
                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, assetB, 52, out ProfileControlledMorphBinding second, out error), Is.True, error);
                Assert.That(second.Commit(out error), Is.True, error);
                second.ApplyBase();
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(2));
                AssertFrame(renderer.sharedMesh, 1, 1f);
                AssertFrame(renderer.sharedMesh, 3, 3f);

                Mesh beforeExhaustion = renderer.sharedMesh;
                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, assetA, 54, out _, out error), Is.False);
                StringAssert.Contains("No PCM slot group", error);
                Assert.That(renderer.sharedMesh, Is.SameAs(beforeExhaustion), "Slot exhaustion must not replace the active runtime Mesh.");
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(2));

                Mesh beforeDetach = renderer.sharedMesh;
                first.Dispose();
                Assert.That(renderer.sharedMesh, Is.SameAs(beforeDetach));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f));
                Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(100f));
                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, assetB, 53, out ProfileControlledMorphBinding reused, out error), Is.True, error);
                Assert.That(reused.Commit(out error), Is.True, error);
                reused.ApplyBase();
                AssertFrame(renderer.sharedMesh, 1, 3f);
                Assert.That(renderer.GetBlendShapeWeight(3), Is.EqualTo(100f));
                second.Dispose();
                reused.Dispose();
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(payloadA);
                Object.DestroyImmediate(payloadB);
                Object.DestroyImmediate(assetA);
                Object.DestroyImmediate(assetB);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void OutfitAttacher_CommitsOnlyAfterAttachPreparationAndDetachesPlugableBinding()
        {
            GameObject figure = new GameObject("Spec10 Attacher Figure");
            GameObject outfitRoot = new GameObject("Spec10 PCM Outfit");
            Mesh source = CreateFigureMesh();
            Mesh payload = null;
            ProfileControlledMorphAsset asset = null;
            CharacterBoneRegistry registry = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                Animator animator = figure.AddComponent<Animator>();
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(renderer, animator, null, null, new List<DynamicBoneBlendTarget> { new DynamicBoneBlendTarget { blendName = "FBM_A" } });
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                attacher.ConfigureForFigure(blender, animator);
                UniversalExpressionProxy expressions = figure.AddComponent<UniversalExpressionProxy>();
                expressions.ConfigureForFigure(renderer, blender);
                FigureMorphSyncCoordinator syncCoordinator = figure.AddComponent<FigureMorphSyncCoordinator>();
                syncCoordinator.ConfigureForFigure(blender, expressions);
                asset = CreatePayload(source, "Coat", 1f, out payload);
                registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                SetPrivateField(outfit, "registryId", "spec10-coat");
                SetPrivateField(outfit, "registryName", "Spec10 Coat");
                SetPrivateField(outfit, "baseExtraBoneRegistry", registry);
                SetPrivateField(outfit, "profileControlledMorphAsset", asset);

                Mesh beforeRejectedAttach = renderer.sharedMesh;
                SetPrivateField(outfit, "profileControlledMorphEnabled", true);
                SetPrivateField(outfit, "profileControlledMorphOutfitName", "WrongOutfitName");
                Assert.That(attacher.TryAttach(outfit), Is.False, "Plugable PCM must reject a logical Outfit name that differs from the payload before PCM preparation.");
                Assert.That(renderer.sharedMesh, Is.SameAs(beforeRejectedAttach));
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(0));
                Assert.That(attacher.AttachedOutfits, Is.Empty);
                SetPrivateField(outfit, "profileControlledMorphOutfitName", asset.OutfitName);

                Assert.That(attacher.TryAttach(outfit), Is.True);
                Assert.That(attacher.AttachedOutfits.Count, Is.EqualTo(1));
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(1));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(100f));
                Mesh runtimeMesh = renderer.sharedMesh;
                Assert.That(attacher.Detach("spec10-coat"), Is.True);
                Assert.That(attacher.AttachedOutfits.Count, Is.EqualTo(0));
                Assert.That(adapter.ActiveRegistrationCount, Is.EqualTo(0));
                Assert.That(renderer.sharedMesh, Is.SameAs(runtimeMesh));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(payload);
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void AdapterTeardown_RestoresSourceSharedMeshAndDestroysRuntimeClone()
        {
            GameObject figure = new GameObject("Spec10 Teardown Figure");
            Mesh source = CreateFigureMesh();
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });
                Mesh runtime = adapter.CreateInitialRuntimeMesh(source);
                Assert.That(renderer.sharedMesh, Is.SameAs(runtime));

                MethodInfo teardown = typeof(DynamicMorphAdapter).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(teardown, Is.Not.Null);
                teardown.Invoke(adapter, null);

                Assert.That(renderer.sharedMesh, Is.SameAs(source));
                Assert.That(runtime == null, Is.True, "Adapter teardown must destroy its runtime Mesh clone.");
            }
            finally
            {
                Object.DestroyImmediate(figure);
                Object.DestroyImmediate(source);
            }
        }

        private static Mesh CreateFigureMesh(int fbmCount = 1)
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("Body", 100f, Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount));
            ShapeSyncLegacyBuilderContracts.AddReservedPcmSlots(mesh, mesh.vertexCount, 2, fbmCount);
            return mesh;
        }

        private static ProfileControlledMorphAsset CreatePayload(Mesh source, string outfit, float baseDelta, out Mesh payload)
        {
            payload = Object.Instantiate(source);
            payload.ClearBlendShapes();
            payload.AddBlendShapeFrame("PCM_" + outfit, 100f, Deltas(source.vertexCount, baseDelta), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
            payload.AddBlendShapeFrame("PCM_FBM_A_" + outfit, 100f, Deltas(source.vertexCount, baseDelta + 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
            ProfileControlledMorphAsset asset = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
            asset.ConfigureForBuild(payload, outfit, new List<string> { "FBM_A" }, false);
            return asset;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing serialized field: " + name);
            field.SetValue(target, value);
        }

        private static Vector3[] Zeroes(int count) => new Vector3[count];

        private static Vector3[] Deltas(int count, float x)
        {
            Vector3[] result = new Vector3[count];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3(x, 0f, 0f);
            return result;
        }

        private static void AssertFrame(Mesh mesh, int shapeIndex, float expectedX)
        {
            Vector3[] vertices = new Vector3[mesh.vertexCount];
            Vector3[] normals = new Vector3[mesh.vertexCount];
            Vector3[] tangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(shapeIndex, 0, vertices, normals, tangents);
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(vertices[i], Is.EqualTo(new Vector3(expectedX, 0f, 0f)));
                Assert.That(normals[i], Is.EqualTo(Vector3.zero));
                Assert.That(tangents[i], Is.EqualTo(Vector3.zero));
            }
        }
    }

}
