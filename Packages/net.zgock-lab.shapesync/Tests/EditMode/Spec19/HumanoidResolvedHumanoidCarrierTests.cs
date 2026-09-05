// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode.Spec19
{
    public sealed class HumanoidResolvedHumanoidCarrierTests
    {
        [Test]
        public void TryPromote_PreservesEditorCarrierOutputAndNormalizesCandidate()
        {
            var candidate = new GameObject("Spec19_4_Candidate");
            var skeleton = new GameObject("Skeleton").transform;
            skeleton.SetParent(candidate.transform, false);
            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(skeleton, false);
            var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = skeleton;
            renderer.bones = new[] { skeleton };
            rendererObject.AddComponent<DynamicBoneBlender>();
            var surplusObject = new GameObject("SurplusRenderer");
            surplusObject.transform.SetParent(candidate.transform, false);
            surplusObject.AddComponent<SkinnedMeshRenderer>();
            Mesh finalMesh = CreateMesh();

            try
            {
                skeleton.localPosition = new Vector3(1f, 2f, 3f);
                skeleton.localRotation = Quaternion.Euler(10f, 20f, 30f);
                skeleton.localScale = new Vector3(2f, 3f, 4f);

                Assert.That(HumanoidResolvedHumanoidCarrier.TryPromote(candidate, finalMesh, null, new[] { skeleton }, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);

                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
                Assert.That(renderer.sharedMesh, Is.SameAs(finalMesh));
                Assert.That(renderer.rootBone, Is.SameAs(skeleton));
                Assert.That(renderer.bones, Is.EqualTo(new[] { skeleton }));
                Assert.That(rendererObject.GetComponent<DynamicBoneBlender>(), Is.Null);
                Assert.That(skeleton.localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(Quaternion.Angle(skeleton.localRotation, Quaternion.Euler(10f, 20f, 30f)), Is.LessThan(0.001f));
                Assert.That(skeleton.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            }
            finally
            {
                Object.DestroyImmediate(finalMesh);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TryPromote_FailureLeavesCandidateOwnershipWithCaller()
        {
            var candidate = new GameObject("Spec19_4_InvalidCandidate");
            var mesh = CreateMesh();
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryPromote(candidate, mesh, null, System.Array.Empty<Transform>(), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeResolvedRendererMissing"));
                Assert.That(candidate, Is.Not.Null, "Core helper must not destroy a caller-owned candidate on failure.");
                Assert.That(mesh, Is.Not.Null, "Core helper must not destroy a caller-owned Mesh on failure.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TryBeginPromote_EditorCandidateCompletesSynchronously()
        {
            var candidate = CreateCandidate(out Transform skeleton, out _, out Mesh mesh);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out HumanoidResolvedHumanoidCarrierOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out diagnostic), Is.EqualTo(HumanoidResolvedHumanoidCarrierStatus.Succeeded), diagnostic?.message);
                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TryPromote_RestoresResolvedPoseAfterAvatarAssignment()
        {
            var candidate = CreateCandidate(out Transform skeleton, out _, out Mesh mesh);
            Avatar avatar = AvatarBuilder.BuildGenericAvatar(candidate, string.Empty);
            Animator animator = candidate.AddComponent<Animator>();
            try
            {
                Vector3 position = new Vector3(3f, 4f, 5f);
                Quaternion rotation = Quaternion.Euler(25f, 35f, 45f);
                Vector3 scale = new Vector3(2f, 3f, 4f);
                skeleton.localPosition = position;
                skeleton.localRotation = rotation;
                skeleton.localScale = scale;

                Assert.That(HumanoidResolvedHumanoidCarrier.TryPromote(candidate, mesh, avatar, new[] { skeleton }, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(animator.avatar, Is.SameAs(avatar));
                Assert.That(skeleton.localPosition, Is.EqualTo(position));
                Assert.That(Quaternion.Angle(skeleton.localRotation, rotation), Is.LessThan(0.001f));
                Assert.That(skeleton.localScale, Is.EqualTo(scale));
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TryBeginPromote_RejectsMissingRootBoneBeforeMutatingCandidate()
        {
            var candidate = CreateCandidate(out Transform skeleton, out SkinnedMeshRenderer renderer, out Mesh mesh);
            renderer.rootBone = null;
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeResolvedRootBoneMissing"));
                Assert.That(renderer.sharedMesh, Is.Null);
                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TryBeginPromote_RejectsAvatarWithoutAnimatorAndLeavesOwnershipWithCaller()
        {
            var candidate = CreateCandidate(out Transform skeleton, out _, out Mesh mesh);
            var avatarSource = new GameObject("Spec19_4_AvatarSource");
            Avatar avatar = AvatarBuilder.BuildGenericAvatar(avatarSource, string.Empty);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, avatar, new[] { skeleton }, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditModeResolvedAnimatorMissing"));
                Assert.That(candidate, Is.Not.Null);
                Assert.That(mesh, Is.Not.Null);
                Assert.That(avatar, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(avatarSource);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(candidate);
            }
        }

        private static GameObject CreateCandidate(out Transform skeleton, out SkinnedMeshRenderer renderer, out Mesh mesh)
        {
            var candidate = new GameObject("Spec19_4_Candidate");
            skeleton = new GameObject("Skeleton").transform;
            skeleton.SetParent(candidate.transform, false);
            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(skeleton, false);
            renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = skeleton;
            renderer.bones = new[] { skeleton };
            rendererObject.AddComponent<DynamicBoneBlender>();
            var surplusObject = new GameObject("SurplusRenderer");
            surplusObject.transform.SetParent(candidate.transform, false);
            surplusObject.AddComponent<SkinnedMeshRenderer>();
            mesh = CreateMesh();
            return candidate;
        }

        private static Mesh CreateMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }
    }
}
