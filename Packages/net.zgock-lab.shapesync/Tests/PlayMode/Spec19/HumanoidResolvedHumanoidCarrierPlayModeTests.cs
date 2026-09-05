// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.PlayMode.Spec19
{
    public sealed class HumanoidResolvedHumanoidCarrierPlayModeTests
    {
        [UnityTest]
        public IEnumerator TryBeginPromote_WaitsForDeferredCleanupBeforeSuccess()
        {
            GameObject candidate = CreateInactiveCandidate(out Transform skeleton, out Mesh mesh);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out HumanoidResolvedHumanoidCarrierOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out diagnostic), Is.EqualTo(HumanoidResolvedHumanoidCarrierStatus.Pending), diagnostic?.message);
                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));

                yield return null;

                Assert.That(operation.Pump(out diagnostic), Is.EqualTo(HumanoidResolvedHumanoidCarrierStatus.Succeeded), diagnostic?.message);
                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(1));
                Assert.That(candidate.GetComponentsInChildren<DynamicBoneBlender>(true), Is.Empty);
            }
            finally
            {
                Object.Destroy(candidate);
                Object.Destroy(mesh);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator TryBeginPromote_RejectsActiveRuntimeCandidateWithoutMutation()
        {
            GameObject candidate = CreateInactiveCandidate(out Transform skeleton, out Mesh mesh);
            candidate.SetActive(true);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("RuntimeCandidateMustBeInactive"));
                Assert.That(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true), Has.Length.EqualTo(2));
            }
            finally
            {
                Object.Destroy(candidate);
                Object.Destroy(mesh);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_RejectsCandidateActivatedBeforeCleanupCompletes()
        {
            GameObject candidate = CreateInactiveCandidate(out Transform skeleton, out Mesh mesh);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out HumanoidResolvedHumanoidCarrierOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                candidate.SetActive(true);

                Assert.That(operation.Pump(out diagnostic), Is.EqualTo(HumanoidResolvedHumanoidCarrierStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("RuntimeCandidateActivatedDuringCleanup"));
            }
            finally
            {
                Object.Destroy(candidate);
                Object.Destroy(mesh);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pump_RejectsCandidateDestroyedBeforeCleanupCompletes()
        {
            GameObject candidate = CreateInactiveCandidate(out Transform skeleton, out Mesh mesh);
            try
            {
                Assert.That(HumanoidResolvedHumanoidCarrier.TryBeginPromote(candidate, mesh, null, new[] { skeleton }, out HumanoidResolvedHumanoidCarrierOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Object.Destroy(candidate);
                yield return null;

                Assert.That(operation.Pump(out diagnostic), Is.EqualTo(HumanoidResolvedHumanoidCarrierStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("ResolvedHumanoidCandidateDestroyed"));
            }
            finally
            {
                Object.Destroy(candidate);
                Object.Destroy(mesh);
            }
            yield return null;
        }

        private static GameObject CreateInactiveCandidate(out Transform skeleton, out Mesh mesh)
        {
            var candidate = new GameObject("Spec19_4_RuntimeCandidate");
            candidate.SetActive(false);
            skeleton = new GameObject("Skeleton").transform;
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
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            return candidate;
        }
    }
}
