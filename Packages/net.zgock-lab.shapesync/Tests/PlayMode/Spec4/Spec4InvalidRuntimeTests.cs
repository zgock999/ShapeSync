// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.PlayMode
{

    public sealed class Spec4InvalidRuntimeTests
    {
        [UnityTest]
        public IEnumerator OutfitAttacher_RejectsNullBaseRegistry()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component outfit = Spec4NormalRuntimeTests.CreateOutfit("NullBase", "e01", "E01", null, Array.Empty<string>(), fixture.assets);
            Spec4NormalRuntimeTests.SetPrivateField(outfit, "baseExtraBoneRegistry", null);

            LogAssert.Expect(LogType.Warning, new Regex("Base Extra Bone Registry is null"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", outfit), Is.False);
            Assert.That(Spec4NormalRuntimeTests.AttachedOutfits(fixture.attacher), Is.Empty);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, outfit.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_RejectsIncompleteFbmRegistryEntry()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component outfit = Spec4NormalRuntimeTests.CreateOutfit("NullFbm", "e02", "E02", null, Array.Empty<string>(), fixture.assets);
            IList entries = Spec4NormalRuntimeTests.CreateRuntimeList("ShapeSyncOutfitFbmExtraBoneRegistry");
            entries.Add(Activator.CreateInstance(Spec4NormalRuntimeTests.RuntimeType("ShapeSyncOutfitFbmExtraBoneRegistry")));
            Spec4NormalRuntimeTests.SetPrivateField(outfit, "fbmExtraBoneRegistries", entries);

            LogAssert.Expect(LogType.Warning, new Regex("FBM Extra Bone Registry entry is incomplete"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", outfit), Is.False);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, outfit.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_RejectsEmptyAndDuplicateRegistryIds()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component emptyId = Spec4NormalRuntimeTests.CreateOutfit("EmptyId", string.Empty, "E03", null, Array.Empty<string>(), fixture.assets);
            Component first = Spec4NormalRuntimeTests.CreateOutfit("First", "duplicate-id", "E04", null, Array.Empty<string>(), fixture.assets);
            Component duplicate = Spec4NormalRuntimeTests.CreateOutfit("Duplicate", "duplicate-id", "E04", null, Array.Empty<string>(), fixture.assets);

            LogAssert.Expect(LogType.Warning, new Regex("registryId is empty"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", emptyId), Is.False);
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", first), Is.True);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("registryId 'duplicate-id' is already attached"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", duplicate), Is.False);
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "Detach", "duplicate-id"), Is.True);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, emptyId.gameObject, first.gameObject, duplicate.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_RejectsDuplicateAndBindposeBasePaths()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component duplicatePath = Spec4NormalRuntimeTests.CreateOutfit("DuplicatePath", "e05", "E05", "Root/Head/Hair", Array.Empty<string>(), fixture.assets);
            ScriptableObject duplicateRegistry = (ScriptableObject)Spec4NormalRuntimeTests.GetPrivateField(duplicatePath, "baseExtraBoneRegistry");
            Spec4NormalRuntimeTests.AddPose(duplicateRegistry, "Root/Head/Hair");

            LogAssert.Expect(LogType.Warning, new Regex("contains duplicate bone path 'Root/Head/Hair'"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", duplicatePath), Is.False);

            Component bindpose = Spec4NormalRuntimeTests.CreateOutfit("Bindpose", "e07", "E07", "Root/Head/Bindpose", Array.Empty<string>(), fixture.assets);
            ScriptableObject bindposeRegistry = (ScriptableObject)Spec4NormalRuntimeTests.GetPrivateField(bindpose, "baseExtraBoneRegistry");
            IList poses = (IList)Spec4NormalRuntimeTests.GetPublicField(bindposeRegistry, "bonePoses");
            object pose = poses[0];
            Spec4NormalRuntimeTests.SetPublicField(pose, "hasBindpose", true);

            LogAssert.Expect(LogType.Warning, new Regex("contains bindpose reference bone 'Root/Head/Bindpose'"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", bindpose), Is.False);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, duplicatePath.gameObject, bindpose.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_AllowsBaseLessFbmPathAndRejectsInvalidFbmKeys()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component baseLess = Spec4NormalRuntimeTests.CreateOutfit("BaseLess", "e08", "E08", "Root/Head/Hair", new[] { "BasicGirl" }, fixture.assets);
            IList baseLessEntries = (IList)Spec4NormalRuntimeTests.GetPrivateField(baseLess, "fbmExtraBoneRegistries");
            object baseLessEntry = baseLessEntries[0];
            ScriptableObject targetRegistry = (ScriptableObject)Spec4NormalRuntimeTests.GetPublicField(baseLessEntry, "extraBoneRegistry");
            IList targetPoses = (IList)Spec4NormalRuntimeTests.GetPublicField(targetRegistry, "bonePoses");
            Spec4NormalRuntimeTests.SetPublicField(targetPoses[0], "boneName", "Root/Head/OtherHair");

            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", baseLess), Is.True);
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "Detach", "e08"), Is.True);

            Component unknown = Spec4NormalRuntimeTests.CreateOutfit("Unknown", "e09", "E09", null, new[] { "NotATarget" }, fixture.assets);
            LogAssert.Expect(LogType.Warning, new Regex("blendName 'NotATarget' is neither a DynamicBoneBlender target nor a resolved PBM difference pair"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", unknown), Is.False);

            Component duplicate = Spec4NormalRuntimeTests.CreateOutfit("DuplicateFbm", "e11", "E11", null, new[] { "BasicGirl", "BasicGirl" }, fixture.assets);
            LogAssert.Expect(LogType.Warning, new Regex("blendName 'BasicGirl' is duplicated"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", duplicate), Is.False);

            Component empty = Spec4NormalRuntimeTests.CreateOutfit("EmptyFbm", "e10", "E10", null, new[] { "BasicGirl" }, fixture.assets);
            IList emptyEntries = (IList)Spec4NormalRuntimeTests.GetPrivateField(empty, "fbmExtraBoneRegistries");
            Spec4NormalRuntimeTests.SetPublicField(emptyEntries[0], "blendName", string.Empty);
            LogAssert.Expect(LogType.Warning, new Regex("FBM Extra Bone Registry entry is incomplete"));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", empty), Is.False);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, baseLess.gameObject, unknown.gameObject, duplicate.gameObject, empty.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_AllowsResolvedPbmDifferenceExtraBoneRegistry()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl", "PBM_BreastSize");
            Component outfit = Spec4NormalRuntimeTests.CreateOutfit(
                "PbmDifference",
                "pbm-difference",
                "PBM Difference",
                "Root/Head/Hair",
                new[] { "PBM_BasicGirl_BreastSize" },
                fixture.assets);

            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", outfit), Is.True);
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "Detach", "pbm-difference"), Is.True);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, outfit.gameObject);
        }

        [UnityTest]
        public IEnumerator DynamicBoneBlender_WarnsOnlyOnceWhenAttachedExtraBoneIsDestroyed()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component outfit = Spec4NormalRuntimeTests.CreateOutfit("MissingExtra", "w01", "W01", "Root/Head/Hair", new[] { "BasicGirl" }, fixture.assets);

            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", outfit), Is.True);
            Spec4NormalRuntimeTests.InvokePrivate(fixture.blender, "InitializeCache");
            Transform attachedRoot = fixture.figure.transform.Find("Root/Head/Hair");
            Assert.That(attachedRoot, Is.Not.Null);
            UnityEngine.Object.Destroy(attachedRoot.gameObject);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("skipped missing Extra Bone path 'Root/Head/Hair'"));
            Spec4NormalRuntimeTests.InvokePrivate(fixture.blender, "ApplyExtraBoneTransforms");
            Spec4NormalRuntimeTests.InvokePrivate(fixture.blender, "ApplyExtraBoneTransforms");

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, outfit.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_AllowsFbmRegistryThatOmitsBasePaths()
        {
            Spec4NormalRuntimeTests.Fixture fixture = Spec4NormalRuntimeTests.CreateFixture("BasicGirl");
            Component outfit = Spec4NormalRuntimeTests.CreateOutfit("BaseOnly", "s01", "S01", "Root/Head/Hair", new[] { "BasicGirl" }, fixture.assets);
            IList entries = (IList)Spec4NormalRuntimeTests.GetPrivateField(outfit, "fbmExtraBoneRegistries");
            ScriptableObject targetRegistry = (ScriptableObject)Spec4NormalRuntimeTests.GetPublicField(entries[0], "extraBoneRegistry");
            Spec4NormalRuntimeTests.SetPublicField(targetRegistry, "bonePoses", Spec4NormalRuntimeTests.CreateRuntimeList("BonePoseData"));

            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "TryAttach", outfit), Is.True);
            yield return null;
            Assert.That(Spec4NormalRuntimeTests.AttachedOutfits(fixture.attacher), Has.Count.EqualTo(1));
            Assert.That(Spec4NormalRuntimeTests.InvokeBool(fixture.attacher, "Detach", "s01"), Is.True);

            yield return null;
            Spec4NormalRuntimeTests.DestroyFixture(fixture, outfit.gameObject);
        }

    }

}
