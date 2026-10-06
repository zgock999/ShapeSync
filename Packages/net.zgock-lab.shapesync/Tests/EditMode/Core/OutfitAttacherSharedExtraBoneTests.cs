// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class OutfitAttacherSharedExtraBoneTests
    {
        private const string SharedPath = "FigureAnchor/Shared";
        private const string TipPath = "FigureAnchor/Shared/Tip";
        private const string ExtraPath = "FigureAnchor/Shared/Extra";
        private static readonly Vector3 P0 = new Vector3(0f, 0.1f, 0f);
        private static readonly (string path, Vector3 position)[] StandardPoses =
        {
            (SharedPath, P0),
            (TipPath, P0)
        };

        private readonly List<Object> created = new List<Object>();
        private GameObject figure;
        private DynamicBoneBlender blender;
        private OutfitAttacher attacher;

        [SetUp]
        public void SetUp()
        {
            figure = new GameObject("Shared Extra Bone Figure");
            created.Add(figure);
            Transform figureAnchor = new GameObject("FigureAnchor").transform;
            figureAnchor.SetParent(figure.transform, false);
            figureAnchor.localPosition = new Vector3(10f, 0f, 0f);
            SkinnedMeshRenderer figureRenderer = figure.AddComponent<SkinnedMeshRenderer>();
            Mesh figureMesh = CreateMesh(hasPositiveBoneWeight: true);
            created.Add(figureMesh);
            figureRenderer.sharedMesh = figureMesh;
            figureRenderer.bones = new[] { figure.transform };
            figureRenderer.rootBone = figure.transform;

            blender = figure.AddComponent<DynamicBoneBlender>();
            Animator animator = figure.AddComponent<Animator>();
            CharacterBoneRegistry targetRegistry = CreateRegistry(new (string, Vector3)[0]);
            blender.ConfigureForFigure(
                figureRenderer,
                animator,
                null,
                null,
                new List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget
                    {
                        blendName = "ArmLong",
                        enabled = true,
                        weight = 0f,
                        targetRegistry = targetRegistry
                    }
                });
            attacher = figure.AddComponent<OutfitAttacher>();
            attacher.ConfigureForFigure(blender, animator);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
        }

        [Test]
        public void SharedRoot_ExactMatch_AttachesBoth([Values(false, true)] bool reverse)
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);

            bool first;
            bool second;
            if (reverse)
            {
                first = attacher.TryAttach(b);
                second = attacher.TryAttach(a);
            }
            else
            {
                first = attacher.TryAttach(a);
                second = attacher.TryAttach(b);
            }

            Assert.That(first, Is.True);
            Assert.That(second, Is.True);

            Transform figureAnchor = figure.transform.Find("FigureAnchor");
            int sharedChildCount = 0;
            for (int i = 0; i < figureAnchor.childCount; i++)
            {
                if (figureAnchor.GetChild(i).name == "Shared") sharedChildCount++;
            }
            Assert.That(sharedChildCount, Is.EqualTo(1));
            Assert.That(attacher.AttachedOutfits[0].ExtraRoots[0] == attacher.AttachedOutfits[1].ExtraRoots[0], Is.True);

            Transform sharedRoot = figure.transform.Find(SharedPath);
            for (int i = 0; i < attacher.AttachedOutfits.Count; i++)
            {
                SkinnedMeshRenderer renderer = attacher.AttachedOutfits[i].RuntimeOutfitInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(renderer.bones[0], Is.SameAs(sharedRoot));
            }
        }

        [Test]
        public void SharedRoot_DetachEitherOrder_KeepsRemainingBones([Values(0, 1)] int detachFirst)
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);
            Assert.That(attacher.TryAttach(b), Is.True);

            string firstId = detachFirst == 0 ? "a" : "b";
            string remainingId = detachFirst == 0 ? "b" : "a";
            Assert.That(attacher.Detach(firstId), Is.True);

            Transform sharedRoot = figure.transform.Find(SharedPath);
            Assert.That(sharedRoot, Is.Not.Null);
            Assert.That(attacher.AttachedOutfits, Has.Count.EqualTo(1));
            SkinnedMeshRenderer remainingRenderer = attacher.AttachedOutfits[0].RuntimeOutfitInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(remainingRenderer.bones[0], Is.Not.Null);
            Assert.That(remainingRenderer.bones[0], Is.SameAs(sharedRoot));

            Assert.That(attacher.Detach(remainingId), Is.True);
            Assert.That(figure.transform.Find(SharedPath), Is.Null);
        }

        [Test]
        public void SharedRoot_BasePoseMismatch_Rejects()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit(
                "b",
                new (string path, Vector3 position)[]
                {
                    (SharedPath, new Vector3(0f, 0.101f, 0f)),
                    (TipPath, P0)
                },
                SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Extra Bone root 'FigureAnchor/Shared' of attached Outfit 'a' cannot be shared: Base pose of 'FigureAnchor/Shared' differs.")));
            Assert.That(attacher.TryAttach(b), Is.False);
            Assert.That(attacher.AttachedOutfits, Has.Count.EqualTo(1));
        }

        [Test]
        public void SharedRoot_BasePoseWithinTolerance_Shares()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit(
                "b",
                new (string path, Vector3 position)[]
                {
                    (SharedPath, new Vector3(0f, 0.100005f, 0f)),
                    (TipPath, P0)
                },
                SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            Assert.That(attacher.TryAttach(b), Is.True);
        }

        [Test]
        public void SharedRoot_PathSetMismatch_Rejects()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit(
                "b",
                new (string path, Vector3 position)[]
                {
                    (SharedPath, P0),
                    (TipPath, P0),
                    (ExtraPath, P0)
                },
                SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Extra Bone root 'FigureAnchor/Shared' of attached Outfit 'a' cannot be shared: path 'FigureAnchor/Shared/Extra' exists only in the new Outfit.")));
            Assert.That(attacher.TryAttach(b), Is.False);
        }

        [Test]
        public void SharedRoot_FbmOnlyInAttached_Rejects()
        {
            ShapeSyncOutfit a = CreateOutfit(
                "a",
                StandardPoses,
                SharedPath,
                new[] { (SharedPath, new Vector3(0.2f, 0.1f, 0f)) });
            ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Extra Bone root 'FigureAnchor/Shared' of attached Outfit 'a' cannot be shared: FBM 'ArmLong' declares 'FigureAnchor/Shared' only in the attached Outfit.")));
            Assert.That(attacher.TryAttach(b), Is.False);
        }

        [Test]
        public void SharedRoot_FbmPoseMismatch_Rejects()
        {
            ShapeSyncOutfit a = CreateOutfit(
                "a",
                StandardPoses,
                SharedPath,
                new[] { (SharedPath, new Vector3(0.2f, 0.1f, 0f)) });
            ShapeSyncOutfit b = CreateOutfit(
                "b",
                StandardPoses,
                SharedPath,
                new[] { (SharedPath, new Vector3(0.3f, 0.1f, 0f)) });
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Extra Bone root 'FigureAnchor/Shared' of attached Outfit 'a' cannot be shared: FBM 'ArmLong' pose of 'FigureAnchor/Shared' differs.")));
            Assert.That(attacher.TryAttach(b), Is.False);
        }

        [Test]
        public void SharedRoot_TrsMatchesSingleOutfit([Values(0f, 0.7f, 1.5f)] float weight)
        {
            (string path, Vector3 position)[] poses = { (SharedPath, P0) };
            ShapeSyncOutfit a = CreateOutfit("a", poses, SharedPath, new[] { (SharedPath, new Vector3(0.2f, 0.1f, 0f)) });
            ShapeSyncOutfit b = CreateOutfit("b", poses, SharedPath, new[] { (SharedPath, new Vector3(0.2f, 0.1f, 0f)) });
            Assert.That(attacher.TryAttach(a), Is.True);
            Assert.That(attacher.TryAttach(b), Is.True);

            blender.Targets[0].weight = weight;
            SetPrivateField(blender, "applyBonePositions", true);
            SetPrivateField(blender, "applyBoneRotations", true);
            SetPrivateField(blender, "applyBoneScales", true);
            InvokePrivateMethod(blender, "CacheExtraBoneBindings");
            InvokePrivateMethod(blender, "ApplyExtraBoneTransforms");
            Vector3 bothAttachedPosition = figure.transform.Find(SharedPath).localPosition;

            Assert.That(attacher.Detach("b"), Is.True);
            InvokePrivateMethod(blender, "CacheExtraBoneBindings");
            InvokePrivateMethod(blender, "ApplyExtraBoneTransforms");
            Vector3 aOnlyPosition = figure.transform.Find(SharedPath).localPosition;

            Assert.That(Vector3.Distance(bothAttachedPosition, aOnlyPosition), Is.EqualTo(0f));
            Assert.That(aOnlyPosition.x, Is.EqualTo(0.2f * weight).Within(1e-5f));
        }

        [Test]
        public void SharedRoot_FailedAttach_LeavesSharedRootIntact()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, "FigureAnchor/Missing");
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Renderer 'Hair' bone path 'FigureAnchor/Missing' was not found on the Figure after Extra Bone transplant.")));
            Assert.That(attacher.TryAttach(b), Is.False);
            Assert.That(attacher.AttachedOutfits[0].ExtraRoots[0] == figure.transform.Find(SharedPath), Is.True);

            Assert.That(attacher.Detach("a"), Is.True);
            Assert.That(figure.transform.Find(SharedPath), Is.Null);
        }

        [Test]
        public void UndeclaredSkinOnAnotherOutfitRoot_Rejects()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            ShapeSyncOutfit u = CreateOutfit("u", new (string path, Vector3 position)[0], SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("OutfitAttacher rejected outfit attach: Renderer 'Hair' bone path 'FigureAnchor/Shared' belongs to Extra Bone root 'FigureAnchor/Shared' of another attached Outfit and is not declared by this Outfit.")));
            Assert.That(attacher.TryAttach(u), Is.False);
            Assert.That(attacher.AttachedOutfits.Count, Is.EqualTo(1));
        }

        [Test]
        public void MapOutfitTransform_SkipsForeignRootCandidates()
        {
            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            Assert.That(attacher.TryAttach(a), Is.True);

            var sourceRoot = new GameObject("Source");
            created.Add(sourceRoot);
            Transform tipSource = new GameObject("Tip").transform;
            tipSource.SetParent(sourceRoot.transform, false);
            MethodInfo mapper = typeof(OutfitAttacher).GetMethod("MapOutfitTransform", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(mapper, Is.Not.Null);

            var figureTransformByOutfitTransform = new Dictionary<Transform, Transform>();
            var foreign = new HashSet<string> { SharedPath };
            Assert.That(
                (Transform)mapper.Invoke(attacher, new object[] { sourceRoot.transform, figureTransformByOutfitTransform, foreign, tipSource }),
                Is.Null);

            foreign = new HashSet<string>();
            Assert.That(
                (Transform)mapper.Invoke(attacher, new object[] { sourceRoot.transform, figureTransformByOutfitTransform, foreign, tipSource }),
                Is.SameAs(figure.transform.Find(TipPath)));
        }
        [Test]
        public void DryRun_MatchesCommitResult([Values(0, 1, 2, 3, 4)] int scenario)
        {
            Assert.That(scenario, Is.InRange(0, 4));

            ShapeSyncOutfit a = CreateOutfit("a", StandardPoses, SharedPath);
            var commands = new List<OutfitAttacherDryRunCommand>();
            switch (scenario)
            {
                case 0:
                {
                    ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(a, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(b, null));
                    break;
                }
                case 1:
                {
                    var mismatchPoses = new[] { (SharedPath, new Vector3(0f, 0.101f, 0f)), (TipPath, P0) };
                    ShapeSyncOutfit b = CreateOutfit("b", mismatchPoses, SharedPath);
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(a, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(b, null));
                    break;
                }
                case 2:
                {
                    ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);
                    ShapeSyncOutfit c = CreateOutfit("c", StandardPoses, SharedPath);
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(a, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(b, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForDetach("a", null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(c, null));
                    break;
                }
                case 3:
                {
                    ShapeSyncOutfit b = CreateOutfit("b", StandardPoses, SharedPath);
                    var mismatchPoses = new[] { (SharedPath, new Vector3(0f, 0.101f, 0f)), (TipPath, P0) };
                    ShapeSyncOutfit d = CreateOutfit("d", mismatchPoses, SharedPath);
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(a, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(b, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForDetach("a", null));
                    commands.Add(OutfitAttacherDryRunCommand.ForDetach("b", null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(d, null));
                    break;
                }
                case 4:
                {
                    ShapeSyncOutfit u = CreateOutfit("u", new (string path, Vector3 position)[0], SharedPath);
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(a, null));
                    commands.Add(OutfitAttacherDryRunCommand.ForAttach(u, null));
                    break;
                }
            }

            bool dryRunSucceeded = attacher.TryDryRun(commands, out OutfitAttacherDryRunResult result);
            int firstCommitFailure = -1;
            for (int i = 0; i < commands.Count; i++)
            {
                OutfitAttacherDryRunCommand command = commands[i];
                bool succeeded = command.Attach ? attacher.TryAttach(command.Outfit) : attacher.Detach(command.RegistryId);
                if (!succeeded && firstCommitFailure < 0) firstCommitFailure = i;
            }

            bool expectedDryRunSucceeded = scenario != 1 && scenario != 4;
            int expectedFirstFailure = expectedDryRunSucceeded ? -1 : 1;
            Assert.That(dryRunSucceeded, Is.EqualTo(expectedDryRunSucceeded));
            Assert.That(firstCommitFailure, Is.EqualTo(expectedFirstFailure));
            if (!expectedDryRunSucceeded)
            {
                Assert.That(result.Code, Is.EqualTo("AttachPreflightRejected"));
                Assert.That(result.CommandIndex, Is.EqualTo(expectedFirstFailure));
            }
        }

        private ShapeSyncOutfit CreateOutfit(
            string registryId,
            (string path, Vector3 position)[] basePoses,
            string skinPath,
            (string path, Vector3 position)[] armLongPoses = null)
        {
            var root = new GameObject(registryId);
            created.Add(root);
            ShapeSyncOutfit outfit = root.AddComponent<ShapeSyncOutfit>();
            foreach (var pose in basePoses) EnsurePath(root.transform, pose.path);
            if (skinPath != null)
            {
                Transform skinBone = EnsurePath(root.transform, skinPath);
                var rendererObject = new GameObject("Hair");
                rendererObject.transform.SetParent(root.transform, false);
                SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                Mesh mesh = CreateMesh(hasPositiveBoneWeight: true);
                created.Add(mesh);
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { skinBone };
                renderer.rootBone = root.transform;
                OutfitSkinningProfile profile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                created.Add(profile);
                profile.SetRendererProfiles(new List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "Hair", baseBindposes = new[] { Matrix4x4.identity } }
                });
                SetPrivateField(outfit, "skinningProfile", profile);
            }
            SetPrivateField(outfit, "registryId", registryId);
            SetPrivateField(outfit, "baseExtraBoneRegistry", CreateRegistry(basePoses));
            var fbm = new List<ShapeSyncOutfitFbmExtraBoneRegistry>();
            if (armLongPoses != null) fbm.Add(new ShapeSyncOutfitFbmExtraBoneRegistry { blendName = "ArmLong", extraBoneRegistry = CreateRegistry(armLongPoses) });
            SetFbmRegistries(outfit, fbm);
            return outfit;
        }

        private CharacterBoneRegistry CreateRegistry((string path, Vector3 position)[] poses)
        {
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            created.Add(registry);
            foreach (var pose in poses)
            {
                registry.bonePoses.Add(new BonePoseData
                {
                    boneName = pose.path,
                    localPosition = pose.position,
                    localRotation = Quaternion.identity,
                    localScale = Vector3.one,
                    bindposeIndex = -1,
                    hasBindpose = false
                });
            }
            return registry;
        }

        private static Transform EnsurePath(Transform root, string path)
        {
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                Transform child = current.Find(segment);
                if (child == null)
                {
                    child = new GameObject(segment).transform;
                    child.SetParent(current, false);
                }
                current = child;
            }
            return current;
        }

        private static Mesh CreateMesh(bool hasPositiveBoneWeight)
        {
            Mesh mesh = new Mesh { name = "A10 Outfit Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            if (hasPositiveBoneWeight)
            {
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                };
            }
            return mesh;
        }

        private static void SetFbmRegistries(ShapeSyncOutfit outfit, List<ShapeSyncOutfitFbmExtraBoneRegistry> entries)
        {
            SetPrivateField(outfit, "fbmExtraBoneRegistries", entries);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }
    }
}
