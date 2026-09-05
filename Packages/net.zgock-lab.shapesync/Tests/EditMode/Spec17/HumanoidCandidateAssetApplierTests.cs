// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidCandidateAssetApplierTests
    {
        private static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void Apply_ChangesOnlyPersistentAssetReferencesOnResolvedCandidate()
        {
            GameObject candidate = null; GameObject avatarRoot = null; Mesh mesh = null; Material material = null; Avatar avatar = null;
            try
            {
                candidate = new GameObject("ResolvedCandidate");
                SkinnedMeshRenderer renderer = candidate.AddComponent<SkinnedMeshRenderer>();
                Transform bone = new GameObject("FinalBone").transform; bone.SetParent(candidate.transform, false);
                renderer.bones = new[] { bone }; renderer.rootBone = bone;
                avatarRoot = new GameObject("AvatarRoot"); avatar = AvatarBuilder.BuildGenericAvatar(avatarRoot, string.Empty);
                Animator animator = candidate.AddComponent<Animator>();
                mesh = new Mesh { subMeshCount = 1, bindposes = new[] { Matrix4x4.identity } };
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

                Assert.That(Invoke(candidate, CreateStage(mesh, avatar, new[] { material }), out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(renderer.sharedMesh, Is.SameAs(mesh));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { material }));
                Assert.That(renderer.bones, Is.EqualTo(new[] { bone }));
                Assert.That(renderer.rootBone, Is.SameAs(bone));
                Assert.That(animator.avatar, Is.SameAs(avatar));
            }
            finally { Destroy(candidate); Destroy(avatarRoot); Destroy(mesh); Destroy(material); Destroy(avatar); }
        }

        [Test]
        public void Apply_RejectsAmbiguousResolvedRendererWithoutMutation()
        {
            GameObject candidate = null; GameObject child = null; Mesh mesh = null; Material material = null;
            try
            {
                candidate = new GameObject("Candidate");
                SkinnedMeshRenderer renderer = candidate.AddComponent<SkinnedMeshRenderer>();
                child = new GameObject("UnexpectedRenderer"); child.transform.SetParent(candidate.transform); child.AddComponent<SkinnedMeshRenderer>();
                mesh = new Mesh { subMeshCount = 1 }; material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Assert.That(Invoke(candidate, CreateStage(mesh, null, new[] { material }), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishCandidateRendererCountInvalid"));
                Assert.That(renderer.sharedMesh, Is.Null);
            }
            finally { Destroy(child); Destroy(candidate); Destroy(mesh); Destroy(material); }
        }

        private static object CreateStage(Mesh mesh, Avatar avatar, Material[] materials)
            => Activator.CreateInstance(StageType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { mesh, avatar, materials, Array.Empty<Texture2D>(), new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("staged.asset") } }, null);
        private static bool Invoke(GameObject candidate, object stage, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { candidate, stage, null }; bool result = (bool)ApplierType.GetMethod("TryApply", Flags).Invoke(null, args); diagnostic = (StackMachineDiagnostic)args[2]; return result;
        }
        private static Type ApplierType => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCandidateAssetApplier", true);
        private static Type StageType => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidIndividualAssetStage", true);
        private static void Destroy(UnityEngine.Object value) { if (value != null) UnityEngine.Object.DestroyImmediate(value); }
    }
}
