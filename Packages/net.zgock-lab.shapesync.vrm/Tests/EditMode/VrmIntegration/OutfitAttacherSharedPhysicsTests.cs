// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM && SHAPESYNC_RICH_TEST
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync;
using zgock.ShapeSync.VrmIntegration;

namespace zgock.ShapeSync.Tests.EditMode.VrmIntegration
{
    public sealed class OutfitAttacherSharedPhysicsTests
    {
        private const string G = "Assets/zgock/ShapeSync/PlayTest/Spec4-ov1/Generated/";
        private const string L01 = "Root/J_Bip_C_Hips/J_Bip_L_UpperLeg/J_Bip_L_LowerLeg/J_Sec_L_CoatSkirtBack_01";
        private const string L02 = "Root/J_Bip_C_Hips/J_Bip_L_UpperLeg/J_Bip_L_LowerLeg/J_Sec_L_CoatSkirtBack_02";

        private GameObject figure;
        private ShapeSyncVrmIntegrationAdapter adapter;
        private OutfitAttacher attacher;
        private ShapeSyncOutfit dress;
        private ShapeSyncOutfit apron;
        private GameObject apronCopy;

        [SetUp]
        public void SetUp()
        {
            figure = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(G + "BasicFemale.prefab"));
            adapter = figure.GetComponent<ShapeSyncVrmIntegrationAdapter>();
            adapter.EnsureRegistered();
            attacher = figure.GetComponent<OutfitAttacher>();
            dress = AssetDatabase.LoadAssetAtPath<GameObject>(G + "Outfits/maid-dress.prefab").GetComponent<ShapeSyncOutfit>();
            apron = AssetDatabase.LoadAssetAtPath<GameObject>(G + "Outfits/maid-apron.prefab").GetComponent<ShapeSyncOutfit>();
        }

        [TearDown]
        public void TearDown()
        {
            if (apronCopy != null) Object.DestroyImmediate(apronCopy);
            if (figure != null) Object.DestroyImmediate(figure);
        }

        [Test]
        public void ComparePhysics_DressAndApron_EqualForAllRoots()
        {
            Assert.That(attacher.TryAttach(dress), Is.True);
            IReadOnlyList<string> roots = attacher.AttachedOutfits[0].ExtraRootPaths;
            Assert.That(roots.Count, Is.EqualTo(12));
            foreach (string root in roots)
            {
                Assert.That(adapter.TryCompareSharedRootPhysics(dress.gameObject, apron.gameObject, root, out _), Is.True);
            }
        }

        [Test]
        public void ComparePhysics_ChangedStiffness_ReportsJoint()
        {
            apronCopy = Object.Instantiate(apron.gameObject);
            ShapeSyncOutfitSpringBoneData data = apronCopy.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            for (int springIndex = 0; springIndex < data.Springs.Count; springIndex++)
            {
                Vrm10InstanceSpringBone.Spring spring = data.Springs[springIndex];
                if (spring == null || spring.Joints == null || spring.Joints.Count == 0 || spring.Joints[0] == null
                    || spring.Joints[0].name != "J_Sec_L_CoatSkirtBack_02") continue;
                spring.Joints[0].m_stiffnessForce = 0.5f;
                break;
            }

            bool equal = adapter.TryCompareSharedRootPhysics(dress.gameObject, apronCopy, L02, out string error);
            Assert.That(equal, Is.False);
            Assert.That(error, Is.EqualTo("Spring joint '" + L02 + "' parameter 'm_stiffnessForce' differs."));
            Assert.That(adapter.TryCompareSharedRootPhysics(dress.gameObject, apronCopy, L01, out _), Is.True);
        }


        [Test]
        public void SharedPhysics_DetachOwner_HandsSpringsToRemainingOutfit()
        {
            Assert.That(attacher.TryAttach(dress), Is.True);
            List<string> roots = new List<string>(attacher.AttachedOutfits[0].ExtraRootPaths);
            Assert.That(attacher.TryAttach(apron), Is.True);

            Vrm10Instance vrm = figure.GetComponent<Vrm10Instance>();
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(14));

            Assert.That(attacher.Detach("maid-dress"), Is.True);
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(12));
            foreach (Vrm10InstanceSpringBone.Spring spring in vrm.SpringBone.Springs)
            {
                Assert.That(spring, Is.Not.Null);
                Assert.That(spring.Joints, Is.Not.Null);
                Assert.That(spring.Joints.Count, Is.GreaterThan(0));
                foreach (var joint in spring.Joints)
                {
                    Assert.That(joint != null, Is.True);
                }

                string path = GetFigureRelativePath(figure.transform, spring.Joints[0].transform);
                Assert.That(roots.Contains(path), Is.True);
            }
            foreach (string rootPath in roots)
            {
                Assert.That(figure.transform.Find(rootPath), Is.Not.Null);
            }

            Assert.That(attacher.Detach("maid-apron"), Is.True);
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(0));
            foreach (string rootPath in roots)
            {
                Assert.That(figure.transform.Find(rootPath), Is.Null);
            }
        }

        [Test]
        public void SharedPhysics_DetachNonOwner_KeepsOwnerSprings()
        {
            Assert.That(attacher.TryAttach(dress), Is.True);
            Assert.That(attacher.TryAttach(apron), Is.True);

            Vrm10Instance vrm = figure.GetComponent<Vrm10Instance>();
            Assert.That(attacher.Detach("maid-apron"), Is.True);
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(14));

            Assert.That(attacher.Detach("maid-dress"), Is.True);
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(0));
        }

        [Test]
        public void SharedPhysics_ChangedStiffness_RejectsAttach()
        {
            Assert.That(attacher.TryAttach(dress), Is.True);
            apronCopy = Object.Instantiate(apron.gameObject);
            ShapeSyncOutfitSpringBoneData data = apronCopy.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            for (int springIndex = 0; springIndex < data.Springs.Count; springIndex++)
            {
                Vrm10InstanceSpringBone.Spring spring = data.Springs[springIndex];
                if (spring == null || spring.Joints == null || spring.Joints.Count == 0 || spring.Joints[0] == null
                    || spring.Joints[0].name != "J_Sec_L_CoatSkirtBack_02") continue;
                spring.Joints[0].m_stiffnessForce = 0.5f;
                break;
            }

            LogAssert.Expect(
                LogType.Warning,
                new Regex("OutfitAttacher rejected outfit attach: Extra Bone root '.*' of attached Outfit 'maid-dress' cannot be shared: physics differs: Spring joint '.*' parameter 'm_stiffnessForce' differs\\."));
            Assert.That(attacher.TryAttach(apronCopy.GetComponent<ShapeSyncOutfit>()), Is.False);

            Vrm10Instance vrm = figure.GetComponent<Vrm10Instance>();
            Assert.That(vrm.SpringBone.Springs.Count, Is.EqualTo(14));
            Assert.That(attacher.AttachedOutfits.Count, Is.EqualTo(1));
        }

        private static string GetFigureRelativePath(Transform root, Transform target)
        {
            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            if (current != root) return null;

            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
#endif
