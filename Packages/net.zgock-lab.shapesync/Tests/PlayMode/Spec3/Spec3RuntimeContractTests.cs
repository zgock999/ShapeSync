// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.PlayMode
{

    public sealed class Spec3RuntimeContractTests
    {
        [UnityTest]
        public IEnumerator DynamicBoneBlender_AppliesRawWeightsWithoutClampOrRescale()
        {
            GameObject gameObject = new GameObject("Spec3_DynamicBoneBlender_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicMale");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            object target = CreateTarget("BasicMale", true, 1.25f);

            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targets", CreateTargetList(null, target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(125f).Within(0.001f));

            SetPublicField(target, "weight", -0.5f);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(-50f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_NullTargetEntryDoesNotBlockValidBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_NullTargetEntry_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targets", CreateTargetList(null, CreateTarget("BasicGirl", true, 0.4f)));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_NullBaseRegistryAndAvatarStillApplyBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_NullBaseReferences_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", null);
            SetPrivateField(blender, "baseAvatar", null);
            SetPrivateField(blender, "targets", CreateTargetList(CreateTarget("BasicGirl", true, 0.4f)));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_NullTargetRegistryAndAvatarStillApplyBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_NullTargetReferences_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            object target = CreateTarget("BasicGirl", true, 0.4f);
            SetPublicField(target, "targetRegistry", null);
            SetPublicField(target, "targetAvatar", null);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_NonHumanoidTargetAvatarSilentlySkipsHumanoidApplyAndKeepsBodyBlendShape()
        {
            GameObject avatarRoot = new GameObject("Spec3_GenericAvatarRoot");
            Avatar nonHumanoidAvatar = AvatarBuilder.BuildGenericAvatar(avatarRoot, string.Empty);
            Assert.That(nonHumanoidAvatar, Is.Not.Null);
            Assert.That(nonHumanoidAvatar.isHuman, Is.False);

            GameObject gameObject = new GameObject("Spec3_NonHumanoidTargetAvatar_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicMale");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            object target = CreateTarget("BasicMale", true, 0.4f);
            SetPublicField(target, "targetAvatar", nonHumanoidAvatar);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));
            LogAssert.NoUnexpectedReceived();

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(nonHumanoidAvatar);
            UnityEngine.Object.Destroy(avatarRoot);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_MissingBaseBindposeWarnsAndKeepsBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_MissingBaseBindpose_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject registry = CreateRegistryWithMissingBindpose();
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", registry);
            SetPrivateField(blender, "targets", CreateTargetList(CreateTarget("BasicGirl", true, 0.4f)));

            LogAssert.Expect(LogType.Warning, new Regex("bindpose blending disabled because base registry bindposes are incomplete or incompatible"));
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(registry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_OutOfRangeBaseBindposeIndexWarnsAndKeepsBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_OutOfRangeBaseBindposeIndex_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject registry = CreateRegistryWithOutOfRangeBindposeIndex();
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", registry);
            SetPrivateField(blender, "targets", CreateTargetList(CreateTarget("BasicGirl", true, 0.4f)));

            LogAssert.Expect(LogType.Warning, new Regex("bindpose blending disabled because base registry bindposes are incomplete or incompatible"));
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(registry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_MissingTargetBindposeSkipsOnlyThatTargetDeltaAndKeepsBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_MissingTargetBindpose_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = CreateRegistryWithBindpose(
                "Root",
                false,
                Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one));
            object target = CreateTarget("BasicGirl", true, 0.4f);
            SetPublicField(target, "targetRegistry", targetRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Matrix4x4 appliedBindpose = renderer.sharedMesh.bindposes[0];
            Assert.That(appliedBindpose.m03, Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(targetRegistry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_MissingTargetBonePoseSkipsThatDeltaAndKeepsBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_MissingTargetBonePose_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            SetPublicField(targetRegistry, "bonePoses", CreateBonePoseList());
            object target = CreateTarget("BasicGirl", true, 0.4f);
            SetPublicField(target, "targetRegistry", targetRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Matrix4x4 appliedBindpose = renderer.sharedMesh.bindposes[0];
            Assert.That(appliedBindpose.m03, Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));
            LogAssert.NoUnexpectedReceived();

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(targetRegistry);
            yield return null;
        }

        [Test]
        public void DynamicBoneBlender_DuplicateTargetBonePathUsesLastSerializedEntry()
        {
            GameObject gameObject = new GameObject("Spec3_DuplicateTargetBonePath_Test");
            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = CreateRegistryWithDuplicateBindposes(
                "Root",
                Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one),
                Matrix4x4.TRS(new Vector3(4f, 0f, 0f), Quaternion.identity, Vector3.one));
            SetPrivateField(blender, "baseRegistry", baseRegistry);

            MethodInfo buildMethod = blender.GetType().GetMethod("BuildTargetBindposeTrs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buildMethod, Is.Not.Null);
            Array targetBindposes = (Array)buildMethod.Invoke(blender, new object[] { targetRegistry, 1 });
            object selectedBindpose = targetBindposes.GetValue(0);
            FieldInfo positionField = selectedBindpose.GetType().GetField("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(positionField, Is.Not.Null);

            Assert.That(((Vector3)positionField.GetValue(selectedBindpose)).x, Is.EqualTo(4f).Within(0.001f));

            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(baseRegistry);
            UnityEngine.Object.DestroyImmediate(targetRegistry);
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_UnknownBaseBonePathSkipsBindingAndTargetBindposeDelta()
        {
            GameObject gameObject = new GameObject("Spec3_UnknownBaseBonePath_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("MissingRoot", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = CreateRegistryWithBindpose(
                "Root",
                true,
                Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one));
            object target = CreateTarget("BasicGirl", true, 0.4f);
            SetPublicField(target, "targetRegistry", targetRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            MethodInfo buildMethod = blender.GetType().GetMethod("BuildTargetBindposeTrs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buildMethod, Is.Not.Null);
            Array targetBindposes = (Array)buildMethod.Invoke(blender, new object[] { targetRegistry, 1 });
            object targetBindpose = targetBindposes.GetValue(0);
            FieldInfo validField = targetBindpose.GetType().GetField("valid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(validField, Is.Not.Null);
            Assert.That((bool)validField.GetValue(targetBindpose), Is.False);

            Array boneBindings = (Array)GetPrivateField(blender, "boneBindings");
            Assert.That(boneBindings.Length, Is.EqualTo(0));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(targetRegistry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_EmptyBlendNameAndDisabledTargetDoNotApplyOrBlockValidTarget()
        {
            GameObject gameObject = new GameObject("Spec3_EmptyAndDisabledTarget_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicGirl", "BasicMale");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject emptyNameRegistry = CreateRegistryWithBindpose(
                "Root",
                true,
                Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.identity, Vector3.one));
            object emptyNameTarget = CreateTarget(string.Empty, true, 1f);
            SetPublicField(emptyNameTarget, "targetRegistry", emptyNameRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            object disabledTarget = CreateTarget("BasicMale", false, 1f);
            SetPrivateField(blender, "targets", CreateTargetList(
                emptyNameTarget,
                disabledTarget,
                CreateTarget("BasicGirl", true, 0.4f)));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.sharedMesh.bindposes[0].m03, Is.EqualTo(0f).Within(0.001f));

            SetPublicField(disabledTarget, "enabled", true);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(100f).Within(0.001f));

            SetPublicField(disabledTarget, "enabled", false);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(emptyNameRegistry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_NonFiniteWeightIsIgnoredAndResetsBodyBlendShape()
        {
            GameObject gameObject = new GameObject("Spec3_NonFiniteWeight_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicMale");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = CreateRegistryWithBindpose(
                "Root",
                true,
                Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one));
            object target = CreateTarget("BasicMale", true, 1f);
            SetPublicField(target, "targetRegistry", targetRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.001f));

            LogAssert.Expect(LogType.Warning, new Regex("ignored non-finite weight for target 'BasicMale'"));
            SetPublicField(target, "weight", float.NaN);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.sharedMesh.bindposes[0].m03, Is.EqualTo(0f).Within(0.001f));

            SetPublicField(target, "weight", 1f);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(targetRegistry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_PositiveInfinityWeightIsIgnoredAndCanRecover()
        {
            GameObject gameObject = new GameObject("Spec3_InfiniteWeight_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicMale");
            mesh.bindposes = new[] { Matrix4x4.identity };
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            ScriptableObject baseRegistry = CreateRegistryWithBindpose("Root", true, Matrix4x4.identity);
            ScriptableObject targetRegistry = CreateRegistryWithBindpose(
                "Root",
                true,
                Matrix4x4.TRS(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one));
            object target = CreateTarget("BasicMale", true, 1f);
            SetPublicField(target, "targetRegistry", targetRegistry);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "baseRegistry", baseRegistry);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.001f));

            LogAssert.Expect(LogType.Warning, new Regex("ignored non-finite weight for target 'BasicMale'"));
            SetPublicField(target, "weight", float.PositiveInfinity);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.001f));
            Assert.That(renderer.sharedMesh.bindposes[0].m03, Is.EqualTo(0f).Within(0.001f));

            SetPublicField(target, "weight", 1f);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseRegistry);
            UnityEngine.Object.Destroy(targetRegistry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_IncompatibleHumanoidAvatarKeepsBaseSkeletonAndAppliesBody()
        {
            GameObject gameObject = new GameObject("Spec3_IncompatibleAvatar_Test");
            Avatar baseAvatar = CreateHumanAvatar(gameObject, "Base_");
            GameObject targetAvatarRoot = new GameObject("Spec3_IncompatibleAvatarRoot");
            Avatar incompatibleAvatar = CreateHumanAvatar(targetAvatarRoot, "Target_");
            Assert.That(baseAvatar, Is.Not.Null);
            Assert.That(baseAvatar.isHuman, Is.True);
            Assert.That(incompatibleAvatar, Is.Not.Null);
            Assert.That(incompatibleAvatar.isHuman, Is.True);

            Mesh mesh = CreateMeshWithBlendShapes("BasicMale");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;
            Animator animator = gameObject.AddComponent<Animator>();
            animator.avatar = baseAvatar;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            object target = CreateTarget("BasicMale", true, 1f);
            SetPublicField(target, "targetAvatar", incompatibleAvatar);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targetAnimator", animator);
            SetPrivateField(blender, "baseAvatar", baseAvatar);
            SetPrivateField(blender, "applyBindposes", false);
            SetPrivateField(blender, "preserveAnimatorStateOnRebind", false);
            SetPrivateField(blender, "targets", CreateTargetList(target));

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(100f).Within(0.001f));
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.That(Vector3.Distance(
                GetSkeletonBone(animator.avatar, "Base_Hips").position,
                GetSkeletonBone(baseAvatar, "Base_Hips").position), Is.LessThan(0.0001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(targetAvatarRoot);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(baseAvatar);
            UnityEngine.Object.Destroy(incompatibleAvatar);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UniversalExpressionProxy_AppliesMcmWeightAboveOneWithoutClampOrRescale()
        {
            GameObject gameObject = new GameObject("Spec3_McmOver100_Test");
            Mesh mesh = CreateMeshWithBlendShapes("BasicMale", "VRM_happy", "MCM_BasicMale_happy");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;

            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));
            SetPrivateField(blender, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(blender, "targets", CreateTargetList(CreateTarget("BasicMale", true, 1.25f)));

            Component proxy = gameObject.AddComponent(RuntimeType("UniversalExpressionProxy"));
            SetPrivateField(proxy, "targetSkinnedMeshRenderer", renderer);
            SetPrivateField(proxy, "dynamicBoneBlender", blender);

            yield return null;

            proxy.GetType().GetMethod("RebuildExpressionList").Invoke(proxy, null);
            IList expressions = (IList)proxy.GetType().GetProperty("Expressions").GetValue(proxy);
            Assert.That(expressions.Count, Is.EqualTo(1));
            SetPublicField(expressions[0], "weight", 1.25f);

            yield return null;

            Mesh runtimeMesh = renderer.sharedMesh;
            Assert.That(renderer.GetBlendShapeWeight(runtimeMesh.GetBlendShapeIndex("BasicMale")), Is.EqualTo(125f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(runtimeMesh.GetBlendShapeIndex("VRM_happy")), Is.EqualTo(125f).Within(0.001f));
            Assert.That(renderer.GetBlendShapeWeight(runtimeMesh.GetBlendShapeIndex("MCM_BasicMale_happy")), Is.EqualTo(156.25f).Within(0.001f));

            UnityEngine.Object.Destroy(gameObject);
            UnityEngine.Object.Destroy(mesh);
            yield return null;
        }

        [Test]
        public void DynamicBoneBlender_AcceptsNullAttachedOutfitSetBeforeRuntimeInitialization()
        {
            GameObject gameObject = new GameObject("Spec3_NullOutfitSet_Test");
            Component blender = gameObject.AddComponent(RuntimeType("DynamicBoneBlender"));

            Assert.DoesNotThrow(() => blender.GetType()
                .GetMethod("SetAttachedOutfitRegistrySets")
                .Invoke(blender, new object[] { null }));

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void UniversalExpressionProxy_RebuildExcludesMcmAndInferredFbmNamesInStandaloneMode()
        {
            GameObject gameObject = new GameObject("Spec3_ExpressionFilter_Test");
            Mesh mesh = CreateMeshWithBlendShapes(
                "VRM_happy",
                "smile",
                "BasicMale",
                "MCM_BasicMale_happy",
                "MCM_Unknown_sad",
                "MCM_",
                "MCM_BasicMale");
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = mesh;
            Component proxy = gameObject.AddComponent(RuntimeType("UniversalExpressionProxy"));
            SetPrivateField(proxy, "targetSkinnedMeshRenderer", renderer);

            proxy.GetType().GetMethod("RebuildExpressionList").Invoke(proxy, null);

            IList expressions = (IList)proxy.GetType().GetProperty("Expressions").GetValue(proxy);
            Assert.That(expressions.Count, Is.EqualTo(2));
            Assert.That(GetPublicField(expressions[0], "blendShapeName"), Is.EqualTo("VRM_happy"));
            Assert.That(GetPublicField(expressions[1], "blendShapeName"), Is.EqualTo("smile"));

            UnityEngine.Object.DestroyImmediate(gameObject);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        private static Mesh CreateMeshWithBlendShapes(params string[] blendShapeNames)
        {
            Mesh mesh = new Mesh { name = "Spec3_RuntimeContractMesh" };
            Vector3[] vertices =
            {
                Vector3.zero,
                Vector3.right,
                Vector3.up
            };
            Vector3[] deltas = new Vector3[vertices.Length];

            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 1, 2 };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, deltas, deltas, deltas);
            }

            return mesh;
        }

        private static Avatar CreateHumanAvatar(GameObject root, string namePrefix)
        {
            List<Transform> skeleton = new List<Transform>();
            Transform hips = CreateHumanBone(root.transform, namePrefix + "Hips", new Vector3(0f, 1f, 0f), skeleton);
            Transform spine = CreateHumanBone(hips, namePrefix + "Spine", new Vector3(0f, 0.15f, 0f), skeleton);
            Transform chest = CreateHumanBone(spine, namePrefix + "Chest", new Vector3(0f, 0.15f, 0f), skeleton);
            Transform neck = CreateHumanBone(chest, namePrefix + "Neck", new Vector3(0f, 0.15f, 0f), skeleton);
            CreateHumanBone(neck, namePrefix + "Head", new Vector3(0f, 0.12f, 0f), skeleton);

            Transform leftUpperArm = CreateHumanBone(chest, namePrefix + "LeftUpperArm", new Vector3(-0.15f, 0.1f, 0f), skeleton);
            Transform leftLowerArm = CreateHumanBone(leftUpperArm, namePrefix + "LeftLowerArm", new Vector3(-0.2f, 0f, 0f), skeleton);
            CreateHumanBone(leftLowerArm, namePrefix + "LeftHand", new Vector3(-0.18f, 0f, 0f), skeleton);
            Transform rightUpperArm = CreateHumanBone(chest, namePrefix + "RightUpperArm", new Vector3(0.15f, 0.1f, 0f), skeleton);
            Transform rightLowerArm = CreateHumanBone(rightUpperArm, namePrefix + "RightLowerArm", new Vector3(0.2f, 0f, 0f), skeleton);
            CreateHumanBone(rightLowerArm, namePrefix + "RightHand", new Vector3(0.18f, 0f, 0f), skeleton);

            Transform leftUpperLeg = CreateHumanBone(hips, namePrefix + "LeftUpperLeg", new Vector3(-0.08f, -0.35f, 0f), skeleton);
            Transform leftLowerLeg = CreateHumanBone(leftUpperLeg, namePrefix + "LeftLowerLeg", new Vector3(0f, -0.35f, 0f), skeleton);
            CreateHumanBone(leftLowerLeg, namePrefix + "LeftFoot", new Vector3(0f, -0.1f, 0.1f), skeleton);
            Transform rightUpperLeg = CreateHumanBone(hips, namePrefix + "RightUpperLeg", new Vector3(0.08f, -0.35f, 0f), skeleton);
            Transform rightLowerLeg = CreateHumanBone(rightUpperLeg, namePrefix + "RightLowerLeg", new Vector3(0f, -0.35f, 0f), skeleton);
            CreateHumanBone(rightLowerLeg, namePrefix + "RightFoot", new Vector3(0f, -0.1f, 0.1f), skeleton);

            List<SkeletonBone> skeletonBones = new List<SkeletonBone>(skeleton.Count + 1)
            {
                CreateSkeletonBone(root.transform)
            };
            for (int i = 0; i < skeleton.Count; i++)
            {
                skeletonBones.Add(CreateSkeletonBone(skeleton[i]));
            }

            HumanDescription description = new HumanDescription
            {
                human = new[]
                {
                    CreateHumanBoneMapping(namePrefix + "Hips", "Hips"),
                    CreateHumanBoneMapping(namePrefix + "Spine", "Spine"),
                    CreateHumanBoneMapping(namePrefix + "Chest", "Chest"),
                    CreateHumanBoneMapping(namePrefix + "Neck", "Neck"),
                    CreateHumanBoneMapping(namePrefix + "Head", "Head"),
                    CreateHumanBoneMapping(namePrefix + "LeftUpperArm", "LeftUpperArm"),
                    CreateHumanBoneMapping(namePrefix + "LeftLowerArm", "LeftLowerArm"),
                    CreateHumanBoneMapping(namePrefix + "LeftHand", "LeftHand"),
                    CreateHumanBoneMapping(namePrefix + "RightUpperArm", "RightUpperArm"),
                    CreateHumanBoneMapping(namePrefix + "RightLowerArm", "RightLowerArm"),
                    CreateHumanBoneMapping(namePrefix + "RightHand", "RightHand"),
                    CreateHumanBoneMapping(namePrefix + "LeftUpperLeg", "LeftUpperLeg"),
                    CreateHumanBoneMapping(namePrefix + "LeftLowerLeg", "LeftLowerLeg"),
                    CreateHumanBoneMapping(namePrefix + "LeftFoot", "LeftFoot"),
                    CreateHumanBoneMapping(namePrefix + "RightUpperLeg", "RightUpperLeg"),
                    CreateHumanBoneMapping(namePrefix + "RightLowerLeg", "RightLowerLeg"),
                    CreateHumanBoneMapping(namePrefix + "RightFoot", "RightFoot")
                },
                skeleton = skeletonBones.ToArray()
            };

            return AvatarBuilder.BuildHumanAvatar(root, description);
        }

        private static Transform CreateHumanBone(Transform parent, string name, Vector3 localPosition, List<Transform> skeleton)
        {
            GameObject bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            bone.transform.localPosition = localPosition;
            skeleton.Add(bone.transform);
            return bone.transform;
        }

        private static SkeletonBone CreateSkeletonBone(Transform transform)
        {
            return new SkeletonBone
            {
                name = transform.name,
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale
            };
        }

        private static HumanBone CreateHumanBoneMapping(string boneName, string humanName)
        {
            return new HumanBone
            {
                boneName = boneName,
                humanName = humanName,
                limit = new HumanLimit { useDefaultValues = true }
            };
        }

        private static SkeletonBone GetSkeletonBone(Avatar avatar, string boneName)
        {
            SkeletonBone[] skeleton = avatar.humanDescription.skeleton;
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (skeleton[i].name == boneName)
                {
                    return skeleton[i];
                }
            }

            Assert.Fail($"Could not find skeleton bone '{boneName}'.");
            return default;
        }

        private static ScriptableObject CreateRegistryWithMissingBindpose()
        {
            ScriptableObject registry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            object pose = Activator.CreateInstance(RuntimeType("BonePoseData"));
            SetPublicField(pose, "boneName", "Root");
            SetPublicField(pose, "bindposeIndex", 0);
            SetPublicField(pose, "hasBindpose", false);
            SetPublicField(pose, "bindpose", Matrix4x4.identity);

            Type poseListType = typeof(List<>).MakeGenericType(RuntimeType("BonePoseData"));
            IList poses = (IList)Activator.CreateInstance(poseListType);
            poses.Add(pose);
            SetPublicField(registry, "bonePoses", poses);
            return registry;
        }

        private static ScriptableObject CreateRegistryWithOutOfRangeBindposeIndex()
        {
            ScriptableObject registry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            object pose = Activator.CreateInstance(RuntimeType("BonePoseData"));
            SetPublicField(pose, "boneName", "Root");
            SetPublicField(pose, "bindposeIndex", 999);
            SetPublicField(pose, "hasBindpose", true);
            SetPublicField(pose, "bindpose", Matrix4x4.identity);

            Type poseListType = typeof(List<>).MakeGenericType(RuntimeType("BonePoseData"));
            IList poses = (IList)Activator.CreateInstance(poseListType);
            poses.Add(pose);
            SetPublicField(registry, "bonePoses", poses);
            return registry;
        }

        private static ScriptableObject CreateRegistryWithBindpose(string boneName, bool hasBindpose, Matrix4x4 bindpose)
        {
            ScriptableObject registry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            SetPublicField(registry, "bonePoses", CreateBonePoseList(CreateBonePose(boneName, hasBindpose, bindpose)));
            return registry;
        }

        private static ScriptableObject CreateRegistryWithDuplicateBindposes(string boneName, Matrix4x4 firstBindpose, Matrix4x4 lastBindpose)
        {
            ScriptableObject registry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            SetPublicField(registry, "bonePoses", CreateBonePoseList(
                CreateBonePose(boneName, true, firstBindpose),
                CreateBonePose(boneName, true, lastBindpose)));
            return registry;
        }

        private static object CreateBonePose(string boneName, bool hasBindpose, Matrix4x4 bindpose)
        {
            object pose = Activator.CreateInstance(RuntimeType("BonePoseData"));
            SetPublicField(pose, "boneName", boneName);
            SetPublicField(pose, "bindposeIndex", 0);
            SetPublicField(pose, "hasBindpose", hasBindpose);
            SetPublicField(pose, "bindpose", bindpose);
            return pose;
        }

        private static IList CreateBonePoseList(params object[] poses)
        {
            Type poseListType = typeof(List<>).MakeGenericType(RuntimeType("BonePoseData"));
            IList poseList = (IList)Activator.CreateInstance(poseListType);
            for (int i = 0; i < poses.Length; i++)
            {
                poseList.Add(poses[i]);
            }

            return poseList;
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}'.");
            field.SetValue(instance, value);
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}'.");
            return field.GetValue(instance);
        }

        private static Type RuntimeType(string typeName)
        {
            Type type = Type.GetType("zgock.ShapeSync." + typeName + ", zgock.ShapeSync.Runtime")
                ?? Type.GetType(typeName + ", zgock.ShapeSync.Runtime");
            Assert.That(type, Is.Not.Null, $"Could not find runtime type '{typeName}'.");
            return type;
        }

        private static object CreateTarget(string blendName, bool enabled, float weight)
        {
            Type targetType = RuntimeType("DynamicBoneBlendTarget");
            object target = Activator.CreateInstance(targetType);
            SetPublicField(target, "blendName", blendName);
            SetPublicField(target, "enabled", enabled);
            SetPublicField(target, "weight", weight);
            return target;
        }

        private static IList CreateTargetList(params object[] entries)
        {
            Type listType = typeof(List<>).MakeGenericType(RuntimeType("DynamicBoneBlendTarget"));
            IList targets = (IList)Activator.CreateInstance(listType);
            for (int i = 0; i < entries.Length; i++)
            {
                targets.Add(entries[i]);
            }

            return targets;
        }

        private static void SetPublicField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Could not find public field '{fieldName}'.");
            field.SetValue(instance, value);
        }

        private static object GetPublicField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Could not find public field '{fieldName}'.");
            return field.GetValue(instance);
        }
    }

}
