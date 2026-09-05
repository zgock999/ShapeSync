// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UniVRM10;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.VrmIntegration;

namespace zgock.ShapeSync.Tests.EditMode.VrmIntegration
{
    /// <summary>Acceptance for registration from the define-constrained UniVRM assembly into the Core runtime seam.</summary>
    public sealed class Spec19VrmRuntimeProviderRegistrationTests
    {
        [Test]
        public void FigureVrmResolution_IgnoresHybridBakedArtifactVrmInstance()
        {
            GameObject figure = null;
            GameObject baked = null;
            VRM10Object liveVrm = null;
            VRM10Object bakedVrm = null;
            try
            {
                figure = new GameObject("Spec19_HybridVrmFigure");
                Avatar avatar = CreateHumanoidAvatar(figure);
                Animator animator = figure.AddComponent<Animator>(); animator.avatar = avatar;
                Vrm10Instance live = figure.AddComponent<Vrm10Instance>();
                liveVrm = ScriptableObject.CreateInstance<VRM10Object>(); live.Vrm = liveVrm;
                HybridHotBakeFigure hybrid = figure.AddComponent<HybridHotBakeFigure>();
                baked = new GameObject("Spec19_HybridBakedArtifact"); baked.transform.SetParent(figure.transform, false);
                Vrm10Instance artifact = baked.AddComponent<Vrm10Instance>();
                bakedVrm = ScriptableObject.CreateInstance<VRM10Object>(); artifact.Vrm = bakedVrm;
                typeof(HybridHotBakeFigure).GetField("bakedRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hybrid, baked);

                Assert.That(ShapeSyncVrmInstanceUtility.TryGetOrCreateFigureInstance(figure, animator, out Vrm10Instance resolved, out string error), Is.True, error);
                Assert.That(resolved, Is.SameAs(live), "Hybrid BakedRoot's VRM component must not become a second Figure source role.");
            }
            finally
            {
                if (figure != null) UnityEngine.Object.DestroyImmediate(figure);
                if (liveVrm != null) UnityEngine.Object.DestroyImmediate(liveVrm);
                if (bakedVrm != null) UnityEngine.Object.DestroyImmediate(bakedVrm);
            }
        }

        [Test]
        public void EditorDomainLoad_RegistersConcreteTransporter()
        {
            Assert.That(HumanoidVrmPhysicsTransportProvider.IsAvailable, Is.True);
            Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter transporter), Is.True);
            Assert.That(transporter, Is.Not.Null);
        }

        [Test]
        public void RuntimeRegistration_RegistersConcreteTransporterWithoutCoreUniVrmReference()
        {
            FieldInfo factory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(factory, Is.Not.Null);
            object original = factory.GetValue(null);
            try
            {
                factory.SetValue(null, null);
                MethodInfo register = typeof(global::zgock.ShapeSync.VrmIntegration.VrmIntegrationService).Assembly
                    .GetType("zgock.ShapeSync.VrmIntegration.HumanoidVrmPhysicsTransportRegistration", true)
                    .GetMethod("RegisterRuntimeTransport", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(register, Is.Not.Null);
                register.Invoke(null, null);
                Assert.That(HumanoidVrmPhysicsTransportProvider.IsAvailable, Is.True);
                Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter transporter), Is.True);
                Assert.That(transporter, Is.Not.Null);
            }
            finally { factory.SetValue(null, original); }
        }

        [Test]
        public void RuntimeTransporter_ForwardsServiceFailureDiagnosticWithoutOwnership()
        {
            Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter transporter), Is.True);
            Assert.That(transporter.TryTransport(null, null, Array.Empty<UnityEngine.GameObject>(), out IDisposable ownership, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(ownership, Is.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateRequired"));
        }

        [Test]
        public void InMemoryOwnership_ClonedInstancesShareVrmAssetsButOwnIndependentSpringComponents()
        {
            GameObject candidate = null;
            GameObject firstClone = null;
            GameObject secondClone = null;
            VrmTransportPhysicsResult ownership = null;
            try
            {
                candidate = new GameObject("Spec19_6_SharedCandidate");
                var instance = candidate.AddComponent<Vrm10Instance>();
                var vrm = ScriptableObject.CreateInstance<VRM10Object>();
                var expression = ScriptableObject.CreateInstance<VRM10Expression>();
                vrm.Expression.AddClip(ExpressionPreset.custom, expression);
                instance.Vrm = vrm;
                var jointRoot = new GameObject("SpringJoint");
                jointRoot.transform.SetParent(candidate.transform, false);
                var joint = jointRoot.AddComponent<VRM10SpringBoneJoint>();
                var spring = new Vrm10InstanceSpringBone.Spring("Spec19Spring");
                spring.Joints.Add(joint);
                instance.SpringBone.Springs.Add(spring);
                ownership = CreateOwnership(instance, vrm, new[] { expression });

                firstClone = UnityEngine.Object.Instantiate(candidate);
                secondClone = UnityEngine.Object.Instantiate(candidate);
                Vrm10Instance first = firstClone.GetComponent<Vrm10Instance>();
                Vrm10Instance second = secondClone.GetComponent<Vrm10Instance>();

                Assert.That(first.Vrm, Is.SameAs(vrm));
                Assert.That(second.Vrm, Is.SameAs(vrm));
                Assert.That(first.Vrm.Expression.CustomClips, Has.Member(expression));
                Assert.That(first.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(joint));
                Assert.That(second.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(joint));
                Assert.That(first.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(second.SpringBone.Springs[0].Joints[0]));
                Assert.That(first.SpringBone.Springs[0].Joints[0].transform.root, Is.SameAs(firstClone.transform));
                Assert.That(second.SpringBone.Springs[0].Joints[0].transform.root, Is.SameAs(secondClone.transform));

                ownership.Dispose();
                ownership = null;
                Assert.That(vrm == null, Is.True, "Releasing the optional ownership destroys shared in-memory assets; the later artifact set must retain it.");
            }
            finally
            {
                ownership?.Dispose();
                if (secondClone != null) UnityEngine.Object.DestroyImmediate(secondClone);
                if (firstClone != null) UnityEngine.Object.DestroyImmediate(firstClone);
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void ActualRuntimeTransport_ClonedInstancesShareAssetsAndRemapSpringJoints()
        {
            GameObject candidate = null;
            GameObject figure = null;
            GameObject firstClone = null;
            GameObject secondClone = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            IDisposable ownership = null;
            HotBakeArtifactSet artifactSet = null;
            try
            {
                candidate = CreateCandidate(out avatar, out mesh);
                figure = CreatePhysicsFigure(out figureVrm, out figureExpression);
                Vrm10Instance sourceInstance = figure.GetComponent<Vrm10Instance>();
                VRM10SpringBoneJoint sourceFirstJoint = sourceInstance.SpringBone.Springs[0].Joints[0];
                VRM10SpringBoneJoint sourceSecondJoint = sourceInstance.SpringBone.Springs[1].Joints[0];
                VRM10SpringBoneColliderGroup sourceFirstGroup = sourceInstance.SpringBone.ColliderGroups[0];
                VRM10SpringBoneColliderGroup sourceSecondGroup = sourceInstance.SpringBone.ColliderGroups[1];
                Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter transporter), Is.True);
                Assert.That(transporter.TryTransport(candidate, figure, Array.Empty<GameObject>(), out ownership, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                var transported = candidate.GetComponent<Vrm10Instance>();
                Assert.That(ownership, Is.Not.Null);
                Assert.That(transported.Vrm, Is.Not.Null);
                Assert.That(transported.SpringBone.Springs, Has.Count.EqualTo(2));
                Assert.That(transported.SpringBone.ColliderGroups, Has.Count.EqualTo(2));
                Assert.That(figure.GetComponent<Vrm10Instance>(), Is.SameAs(sourceInstance));
                Assert.That(sourceInstance.Vrm, Is.SameAs(figureVrm));
                Assert.That(sourceInstance.SpringBone.Springs[0].Joints[0], Is.SameAs(sourceFirstJoint));
                Assert.That(sourceInstance.SpringBone.Springs[1].Joints[0], Is.SameAs(sourceSecondJoint));
                Assert.That(sourceInstance.SpringBone.ColliderGroups[0], Is.SameAs(sourceFirstGroup));
                Assert.That(sourceInstance.SpringBone.ColliderGroups[1], Is.SameAs(sourceSecondGroup));

                HumanoidBuildResult buildResult = CreateArtifactBuildResult(candidate, mesh, avatar);
                Assert.That(HotBakeArtifactSet.TryCreate(buildResult, new[] { ownership }, out artifactSet, out diagnostic), Is.True, diagnostic?.message);
                ownership = null;
                Assert.That(artifactSet.TemplateRoot, Is.SameAs(candidate));
                firstClone = UnityEngine.Object.Instantiate(artifactSet.TemplateRoot);
                secondClone = UnityEngine.Object.Instantiate(artifactSet.TemplateRoot);
                Vrm10Instance first = firstClone.GetComponent<Vrm10Instance>();
                Vrm10Instance second = secondClone.GetComponent<Vrm10Instance>();
                Assert.That(first.Vrm, Is.SameAs(transported.Vrm));
                Assert.That(second.Vrm, Is.SameAs(transported.Vrm));
                Assert.That(first.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(transported.SpringBone.Springs[0].Joints[0]));
                Assert.That(second.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(transported.SpringBone.Springs[0].Joints[0]));
                Assert.That(first.SpringBone.Springs[0].Joints[0], Is.Not.SameAs(second.SpringBone.Springs[0].Joints[0]));
                Assert.That(first.SpringBone.Springs[0].Joints[0].transform.root, Is.SameAs(firstClone.transform));
                Assert.That(second.SpringBone.Springs[0].Joints[0].transform.root, Is.SameAs(secondClone.transform));
                Assert.That(first.SpringBone.Springs[1].Joints[0], Is.Not.SameAs(transported.SpringBone.Springs[1].Joints[0]));
                Assert.That(second.SpringBone.Springs[1].Joints[0], Is.Not.SameAs(transported.SpringBone.Springs[1].Joints[0]));
                Assert.That(first.SpringBone.Springs[1].Joints[0], Is.Not.SameAs(second.SpringBone.Springs[1].Joints[0]));
                Assert.That(first.SpringBone.Springs[1].Joints[0].transform.root, Is.SameAs(firstClone.transform));
                Assert.That(second.SpringBone.Springs[1].Joints[0].transform.root, Is.SameAs(secondClone.transform));
                VRM10SpringBoneColliderGroup candidateGroup = transported.SpringBone.ColliderGroups[0];
                VRM10SpringBoneColliderGroup firstGroup = first.SpringBone.ColliderGroups[0];
                VRM10SpringBoneColliderGroup secondGroup = second.SpringBone.ColliderGroups[0];
                Assert.That(firstGroup, Is.Not.SameAs(candidateGroup));
                Assert.That(secondGroup, Is.Not.SameAs(candidateGroup));
                Assert.That(firstGroup, Is.Not.SameAs(secondGroup));
                Assert.That(first.SpringBone.Springs[0].ColliderGroups[0], Is.SameAs(firstGroup));
                Assert.That(second.SpringBone.Springs[0].ColliderGroups[0], Is.SameAs(secondGroup));
                Assert.That(firstGroup.transform.root, Is.SameAs(firstClone.transform));
                Assert.That(secondGroup.transform.root, Is.SameAs(secondClone.transform));
                VRM10SpringBoneColliderGroup candidateSecondGroup = transported.SpringBone.ColliderGroups[1];
                VRM10SpringBoneColliderGroup firstSecondGroup = first.SpringBone.ColliderGroups[1];
                VRM10SpringBoneColliderGroup secondSecondGroup = second.SpringBone.ColliderGroups[1];
                Assert.That(firstSecondGroup, Is.Not.SameAs(candidateSecondGroup));
                Assert.That(secondSecondGroup, Is.Not.SameAs(candidateSecondGroup));
                Assert.That(firstSecondGroup, Is.Not.SameAs(secondSecondGroup));
                Assert.That(first.SpringBone.Springs[1].ColliderGroups[0], Is.SameAs(firstSecondGroup));
                Assert.That(second.SpringBone.Springs[1].ColliderGroups[0], Is.SameAs(secondSecondGroup));
                Assert.That(firstSecondGroup.transform.root, Is.SameAs(firstClone.transform));
                Assert.That(secondSecondGroup.transform.root, Is.SameAs(secondClone.transform));
                Assert.That(first.Vrm.Expression, Is.SameAs(transported.Vrm.Expression));
                Assert.That(second.Vrm.Expression, Is.SameAs(transported.Vrm.Expression));
                Assert.That(transported.Vrm, Is.Not.Null, "Ownership is retained during clone creation; no persistence release may occur.");
                UnityEngine.Object.DestroyImmediate(secondClone); secondClone = null;
                UnityEngine.Object.DestroyImmediate(firstClone); firstClone = null;
                artifactSet.Dispose(); artifactSet = null;
                Assert.That(candidate == null, Is.True, "The template root is released only with the final artifact-set reference.");
            }
            finally
            {
                artifactSet?.Dispose();
                ownership?.Dispose();
                if (secondClone != null) UnityEngine.Object.DestroyImmediate(secondClone);
                if (firstClone != null) UnityEngine.Object.DestroyImmediate(firstClone);
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
                if (figureExpression != null) UnityEngine.Object.DestroyImmediate(figureExpression);
                if (figureVrm != null) UnityEngine.Object.DestroyImmediate(figureVrm);
                if (figure != null) UnityEngine.Object.DestroyImmediate(figure);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        private static VrmTransportPhysicsResult CreateOwnership(Vrm10Instance instance, VRM10Object vrm, VRM10Expression[] expressions)
        {
            ConstructorInfo constructor = typeof(VrmTransportPhysicsResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Vrm10Instance), typeof(VRM10Object), typeof(VRM10Expression[]) },
                null);
            Assert.That(constructor, Is.Not.Null);
            return (VrmTransportPhysicsResult)constructor.Invoke(new object[] { instance, vrm, expressions });
        }

        private static HumanoidBuildResult CreateArtifactBuildResult(GameObject root, Mesh mesh, Avatar avatar)
        {
            ConstructorInfo constructor = typeof(HumanoidBuildResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(InMemoryHumanoidMesh) },
                null);
            Assert.That(constructor, Is.Not.Null);
            return (HumanoidBuildResult)constructor.Invoke(new object[] { new InMemoryHumanoidMesh(root, mesh, avatar) });
        }

        private static GameObject CreateCandidate(out Avatar avatar, out Mesh mesh)
        {
            var candidate = new GameObject("Spec19_6_ActualCandidate");
            avatar = CreateHumanoidAvatar(candidate);
            candidate.AddComponent<Animator>().avatar = avatar;
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 }, normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward } };
            candidate.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            return candidate;
        }

        private static GameObject CreatePhysicsFigure(out VRM10Object vrm, out VRM10Expression expression)
        {
            var figure = new GameObject("Spec19_6_PhysicsFigure");
            var instance = figure.AddComponent<Vrm10Instance>();
            vrm = ScriptableObject.CreateInstance<VRM10Object>();
            expression = ScriptableObject.CreateInstance<VRM10Expression>();
            vrm.Expression.AddClip(ExpressionPreset.custom, expression);
            instance.Vrm = vrm;
            Transform hips = new GameObject("Hips").transform; hips.SetParent(figure.transform, false);
            var firstJoint = hips.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var firstSpring = new Vrm10InstanceSpringBone.Spring("FigureSpringA"); firstSpring.Joints.Add(firstJoint);
            var firstGroup = hips.gameObject.AddComponent<VRM10SpringBoneColliderGroup>(); firstGroup.Name = "FigureColliderA";
            Transform chest = new GameObject("Chest").transform; chest.SetParent(hips, false);
            var secondJoint = chest.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var secondSpring = new Vrm10InstanceSpringBone.Spring("FigureSpringB"); secondSpring.Joints.Add(secondJoint);
            var secondGroup = chest.gameObject.AddComponent<VRM10SpringBoneColliderGroup>(); secondGroup.Name = "FigureColliderB";
            instance.SpringBone.ColliderGroups.Add(firstGroup); instance.SpringBone.ColliderGroups.Add(secondGroup);
            firstSpring.ColliderGroups.Add(firstGroup); secondSpring.ColliderGroups.Add(secondGroup);
            instance.SpringBone.Springs.Add(firstSpring); instance.SpringBone.Springs.Add(secondSpring);
            return figure;
        }

        private static Avatar CreateHumanoidAvatar(GameObject root)
        {
            var bones = new List<Transform>();
            Transform hips = AddBone(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddBone(hips, "Spine", Vector3.up * .15f, bones); Transform chest = AddBone(spine, "Chest", Vector3.up * .15f, bones); Transform neck = AddBone(chest, "Neck", Vector3.up * .15f, bones); AddBone(neck, "Head", Vector3.up * .12f, bones);
            Transform lua = AddBone(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones); Transform lla = AddBone(lua, "LeftLowerArm", Vector3.left * .2f, bones); AddBone(lla, "LeftHand", Vector3.left * .18f, bones);
            Transform rua = AddBone(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones); Transform rla = AddBone(rua, "RightLowerArm", Vector3.right * .2f, bones); AddBone(rla, "RightHand", Vector3.right * .18f, bones);
            Transform lul = AddBone(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones); Transform lll = AddBone(lul, "LeftLowerLeg", Vector3.down * .35f, bones); AddBone(lll, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rul = AddBone(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones); Transform rll = AddBone(rul, "RightLowerLeg", Vector3.down * .35f, bones); AddBone(rll, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var human = new HumanBone[names.Length]; for (int i = 0; i < names.Length; i++) human[i] = new HumanBone { boneName = names[i], humanName = names[i], limit = new HumanLimit { useDefaultValues = true } };
            var skeleton = new List<SkeletonBone> { ToSkeletonBone(root.transform) }; for (int i = 0; i < bones.Count; i++) skeleton.Add(ToSkeletonBone(bones[i]));
            return AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
        }

        private static Transform AddBone(Transform parent, string name, Vector3 position, List<Transform> bones) { Transform bone = new GameObject(name).transform; bone.SetParent(parent, false); bone.localPosition = position; bones.Add(bone); return bone; }
        private static SkeletonBone ToSkeletonBone(Transform transform) => new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };
    }
}
#endif
