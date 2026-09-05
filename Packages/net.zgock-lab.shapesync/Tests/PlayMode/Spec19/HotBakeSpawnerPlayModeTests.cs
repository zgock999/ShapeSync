// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Tests.PlayMode.Spec19
{
    public sealed class HotBakeSpawnerPlayModeTests
    {
        [UnityTest]
        public IEnumerator Figure_OwnerDestroyReleasesSpawnedChildAndGeneratedAvatar()
        {
#if UNITY_EDITOR
            using (var fixture = new PlayModeHumanoidBuildBackendTests.DriverInputFixture())
            {
                var animatorRoot = new GameObject("Spec19_8_FigurePlayAnimator");
                var figureRoot = new GameObject("Spec19_8_FigurePlayRoot");
                var hostRoot = new GameObject("Spec19_8_FigurePlayHost");
                try
                {
                    figureRoot.transform.SetParent(animatorRoot.transform, false);
                    Animator animator = animatorRoot.AddComponent<Animator>();
                    animator.avatar = fixture.Prefab.GetComponent<Animator>().avatar;
                    var figure = figureRoot.AddComponent<HotBakeFigure>();
                    figure.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    figure.FigurePrefab = fixture.Prefab; figure.Document = fixture.Document;
                    Assert.That(figure.Compile(out var start), Is.True, start?.message);
                    for (int frame = 0; figure.IsCompileActive && frame < 30; frame++) yield return null;
                    Assert.That(figure.SpawnedInstances.Count, Is.EqualTo(1), figure.LastDiagnostic?.message);
                    GameObject child = figure.SpawnedInstances[0]; Avatar generated = animator.avatar;
                    Object.Destroy(figureRoot); yield return null;
                    Assert.That(child == null, Is.True);
                    Assert.That(generated == null, Is.True);
                }
                finally { if (hostRoot != null) Object.Destroy(hostRoot); if (animatorRoot != null) Object.Destroy(animatorRoot); }
            }
#else
            Assert.Ignore("The Hot Bake input fixture is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CompileApi_CompletionAndReplacement_DestroyOwnedInstancesAtFrameEnd()
        {
#if UNITY_EDITOR
            using (var fixture = new PlayModeHumanoidBuildBackendTests.DriverInputFixture())
            {
                var owner = new GameObject("Spec19_8_SpawnerPlayModeOwner");
                var hostRoot = new GameObject("Spec19_8_SpawnerPlayModeHost");
                var firstTarget = new GameObject("Spec19_8_SpawnerPlayModeTargetA");
                var secondTarget = new GameObject("Spec19_8_SpawnerPlayModeTargetB");
                try
                {
                    var component = owner.AddComponent<HotBakeSpawner>();
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.SpawnTargets.Add(firstTarget.transform);
                    component.SpawnTargets.Add(secondTarget.transform);

                    Assert.That(component.Compile(out var start), Is.True, start?.message);
                    for (int frame = 0; component.IsCompileActive && frame < 30; frame++) yield return null;

                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.ArtifactSet, Is.Not.Null, component.LastDiagnostic?.message);
                    Assert.That(component.LastDiagnostic, Is.Null, "Startup must not leave an active-compile diagnostic after an API-started completion.");
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    GameObject previousFirst = component.SpawnedInstances[0];
                    Assert.That(component.TrySpawnAll(out var replacement), Is.True, replacement?.message);
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    Assert.That(previousFirst, Is.Not.Null, "PlayMode Destroy is deferred until the frame end.");
                    yield return null;
                    Assert.That(previousFirst == null, Is.True, "Replacement must destroy only the previous component-owned instance at frame end.");

                    GameObject replacementFirst = component.SpawnedInstances[0];
                    Object.Destroy(owner);
                    Assert.That(replacementFirst, Is.Not.Null, "Owner teardown also uses frame-end Destroy in PlayMode.");
                    yield return null;
                    Assert.That(replacementFirst == null, Is.True, "Owner teardown must destroy component-owned instances.");
                }
                finally
                {
                    if (owner != null) Object.Destroy(owner);
                    if (secondTarget != null) Object.Destroy(secondTarget);
                    if (firstTarget != null) Object.Destroy(firstTarget);
                    if (hostRoot != null) Object.Destroy(hostRoot);
                }
            }
#else
            Assert.Ignore("The Hot Bake input fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Hybrid_WarmModeExchangeStopsEditingComponentsAndKeepsAnimator()
        {
#if UNITY_EDITOR
            using (var fixture = new PlayModeHumanoidBuildBackendTests.DriverInputFixture())
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridPlayModeHost");
                try
                {
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                    NormalBlender normal = figure.AddComponent<NormalBlender>();
                    MaterialAttacher attacher = figure.AddComponent<MaterialAttacher>();
                    HybridHotBakeFigure hybrid = figure.AddComponent<HybridHotBakeFigure>();
                    hybrid.Director = director;
                    hybrid.FigurePrefab = fixture.Prefab;
                    hybrid.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    hybrid.QuietWindowSeconds = 0f;
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    for (int frame = 0; hybrid.BakedRoot == null && hybrid.LastDiagnostic == null && frame < 40; frame++) yield return null;

                    SkinnedMeshRenderer liveRenderer = figure.GetComponentInChildren<SkinnedMeshRenderer>();
                    MaterialProxy proxy = figure.GetComponent<MaterialProxy>();
                    Animator animator = figure.GetComponent<Animator>();
                    Assert.That(hybrid.BakedRoot, Is.Not.Null, hybrid.LastDiagnostic?.message);
                    Assert.That(hybrid.TrySetRunMode(true, out var enter), Is.True, enter?.message);
                    Assert.That(hybrid.BakedRoot.activeSelf, Is.True);
                    Assert.That(liveRenderer.enabled, Is.False);
                    Assert.That(blender.enabled && normal.enabled && attacher.enabled && proxy.enabled && director.enabled, Is.False);
                    Assert.That(animator == null || animator.enabled, Is.True);
                    yield return null;
                    Assert.That(animator == null || animator.enabled, Is.True);

                    Assert.That(hybrid.TrySetRunMode(false, out var leave), Is.True, leave?.message);
                    Assert.That(hybrid.BakedRoot.activeSelf, Is.False);
                    Assert.That(liveRenderer.enabled && blender.enabled && normal.enabled && attacher.enabled && proxy.enabled && director.enabled, Is.True);
                }
                finally { if (figure != null) Object.Destroy(figure); if (hostRoot != null) Object.Destroy(hostRoot); }
            }
#else
            Assert.Ignore("The Hot Bake input fixture is Editor-only.");
            yield break;
#endif
        }

 #if SHAPESYNC_RICH_TEST
        [UnityTest]
        public IEnumerator Hybrid_ProductionScene_StartupWarmBakePromotesArtifact()
        {
#if UNITY_EDITOR
            const string scenePath = "Assets/zgock/ShapeSync/PlayTest/Spec19/19.9/19.9-3.unity";
            // Scene scope is part of the Hot Bake contract.  Load exactly as Human Test
            // does, rather than additively beside the test bootstrap's Texture host.
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "The production Hybrid Human Test scene must load in PlayMode.");

            HybridHotBakeFigure hybrid = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length && hybrid == null; rootIndex++)
                hybrid = roots[rootIndex].GetComponentInChildren<HybridHotBakeFigure>(true);
            Assert.That(hybrid, Is.Not.Null, "19.9-3 must contain the Hybrid Hot Bake Figure.");
            for (int frame = 0; frame < 300 && (hybrid.BakedRoot == null || hybrid.ArtifactSet == null || hybrid.ArtifactSet.MaterialSlots.Count == 0) && hybrid.LastDiagnostic == null; frame++) yield return null;

            Assert.That(hybrid.LastDiagnostic, Is.Null, FormatDiagnostic(hybrid.LastDiagnostic));
            Assert.That(hybrid.BakedRoot, Is.Not.Null, "The Director-current startup state must promote one warm artifact.");
            Assert.That(hybrid.BakedRoot.transform.parent, Is.SameAs(hybrid.transform));
            Assert.That(hybrid.BakedRoot.activeSelf, Is.False, "Startup warm bake must remain in Edit Mode until Run is requested.");
#else
            Assert.Ignore("The production scene load test is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Hybrid_ProductionScene_BakedFaceNormalMatchesRuntimeEscrowPixelsInRunMode()
        {
#if UNITY_EDITOR
            const string scenePath = "Assets/zgock/ShapeSync/PlayTest/Spec19/19.9/19.9-3.unity";
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);

            HybridHotBakeFigure hybrid = FindComponentInScene<HybridHotBakeFigure>(scene);
            Assert.That(hybrid, Is.Not.Null);
            for (int frame = 0; frame < 300 && hybrid.BakedRoot == null && hybrid.LastDiagnostic == null; frame++) yield return null;
            Assert.That(hybrid.LastDiagnostic, Is.Null, FormatDiagnostic(hybrid.LastDiagnostic));
            Assert.That(hybrid.BakedRoot, Is.Not.Null);

            NormalBlender normalBlender = hybrid.GetComponentInChildren<NormalBlender>(true);
            Assert.That(normalBlender, Is.Not.Null, "The production Figure must expose its Figure-local NormalBlender.");
            Texture runtimeEscrow = null;
            for (int frame = 0; frame < 300 && runtimeEscrow == null; frame++)
            {
                runtimeEscrow = GetEscrowedNormalTexture(normalBlender, "face");
                if (runtimeEscrow == null) yield return null;
            }
            Assert.That(runtimeEscrow, Is.Not.Null, "Face must retain its NormalBlender escrow before Run Mode is entered.");

            // Startup can first promote a topology-only warm artifact while the Director's
            // material transaction is still settling.  Explicit Compile is the contractual
            // API trigger and snapshots the already-resolved runtime escrow for this Oracle.
            Assert.That(hybrid.Compile(out StackMachineDiagnostic compile), Is.True, FormatDiagnostic(compile));
            for (int frame = 0; frame < 300 && (hybrid.IsCompileActive || hybrid.ArtifactSet == null || hybrid.ArtifactSet.MaterialSlots.Count == 0) && hybrid.LastDiagnostic == null; frame++) yield return null;
            Assert.That(hybrid.LastDiagnostic, Is.Null, FormatDiagnostic(hybrid.LastDiagnostic));
            HotBakeArtifactSet artifact = hybrid.ArtifactSet;
            Assert.That(artifact, Is.Not.Null);
            Assert.That(artifact.MaterialSlots.Count, Is.GreaterThan(0), "Explicit Compile must produce the Director-current Figure material slots.");
            Assert.That(artifact.MaterialSlots.Count, Is.EqualTo(artifact.Materials.Count));

            int faceIndex = -1;
            var observedMaterialIds = new List<string>();
            for (int index = 0; index < artifact.MaterialSlots.Count; index++)
            {
                HumanoidBuildMaterialSlot slot = artifact.MaterialSlots[index];
                observedMaterialIds.Add(slot.MaterialId.RegistryId + ":" + slot.MaterialId.EntryId);
                // RegistryId is an implementation-level physical owner identity and is not
                // guaranteed to be empty for the Figure.  `face` is the authored Figure
                // entry under this production fixture and is unique in its final slots.
                if (slot.MaterialId.EntryId == "face") { faceIndex = index; break; }
            }
            Assert.That(faceIndex, Is.GreaterThanOrEqualTo(0), "The baked Figure must retain the Figure-local face material slot. observed=" + string.Join(",", observedMaterialIds.ToArray()));
            Assert.That(hybrid.TrySetRunMode(true, out StackMachineDiagnostic enter), Is.True, FormatDiagnostic(enter));
            yield return null;

            Texture baked = GetNormalTexture(artifact.Materials[faceIndex]);
            Assert.That(baked, Is.Not.Null, "The baked face material must have a Normal texture.");
            Assert.That(baked.width, Is.EqualTo(runtimeEscrow.width), "Face Normal width must match Runtime escrow.");
            Assert.That(baked.height, Is.EqualTo(runtimeEscrow.height), "Face Normal height must match Runtime escrow.");
            bool matched = false;
            yield return TexturesMatchPixels(runtimeEscrow, baked, value => matched = value);
            Assert.That(matched, Is.True, "Baked Face Normal must pixel-match the retained Runtime escrow in Run Mode.");
#else
            Assert.Ignore("The production scene load test is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Hybrid_ProductionScene_BakedMeshContainsNoFbmBlendShapes()
        {
#if UNITY_EDITOR
            const string scenePath = "Assets/zgock/ShapeSync/PlayTest/Spec19/19.9/19.9-3.unity";
            Scene scene = EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            HybridHotBakeFigure hybrid = FindComponentInScene<HybridHotBakeFigure>(scene);
            Assert.That(hybrid, Is.Not.Null);
            for (int frame = 0; frame < 300 && hybrid.BakedRoot == null && hybrid.LastDiagnostic == null; frame++) yield return null;
            Assert.That(hybrid.LastDiagnostic, Is.Null, FormatDiagnostic(hybrid.LastDiagnostic));
            Assert.That(hybrid.BakedRoot, Is.Not.Null);
            DynamicBoneBlender blender = hybrid.GetComponentInChildren<DynamicBoneBlender>(true);
            Assert.That(blender, Is.Not.Null, "The production Figure must provide the authoritative FBM target registry.");
            var fbmNames = new HashSet<string>();
            IReadOnlyList<DynamicBoneBlendTarget> targets = blender.Targets;
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                if (targets[targetIndex] != null && !string.IsNullOrWhiteSpace(targets[targetIndex].blendName)) fbmNames.Add(targets[targetIndex].blendName);
            SkinnedMeshRenderer[] renderers = hybrid.BakedRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            var retained = new List<string>();
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Mesh mesh = renderers[rendererIndex].sharedMesh;
                for (int shapeIndex = 0; mesh != null && shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    string name = mesh.GetBlendShapeName(shapeIndex);
                    if (fbmNames.Contains(name)) retained.Add(renderers[rendererIndex].name + ":" + name);
                }
            }
            Assert.That(retained, Is.Empty, "FBM deformation is baked into final vertices; its runtime BlendShape frames must not survive. retained=" + string.Join(",", retained.ToArray()));
#else
            Assert.Ignore("The production scene load test is Editor-only.");
            yield break;
#endif
        }

#endif
        private static void CommitPhysicalShapes(ShapeDirector director, List<ShapeSyncShape> shapes)
        {
            MethodInfo commit = typeof(ShapeDirector).GetMethod("CommitCurrentPhysicalShapes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(commit, Is.Not.Null);
            commit.Invoke(director, new object[] { shapes });
        }

        private static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
        {
            if (diagnostic == null) return null;
            string formatted = diagnostic.domainCode + ": " + diagnostic.message;
            if (!string.IsNullOrEmpty(diagnostic.bindingName)) formatted += " binding=" + diagnostic.bindingName;
            if (!string.IsNullOrEmpty(diagnostic.detail)) formatted += " detail=" + diagnostic.detail;
            return formatted;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                T value = roots[index].GetComponentInChildren<T>(true);
                if (value != null) return value;
            }
            return null;
        }

        private static Texture GetNormalTexture(Material material) => material != null && material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;

        private static Texture GetEscrowedNormalTexture(NormalBlender blender, string entryName)
        {
            if (blender == null || string.IsNullOrEmpty(entryName)) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var states = typeof(NormalBlender).GetField("states", flags)?.GetValue(blender) as IDictionary;
            object state = states == null || !states.Contains(entryName) ? null : states[entryName];
            object delivery = state?.GetType().GetField("delivery", flags)?.GetValue(state);
            return delivery?.GetType().GetProperty("Texture", flags)?.GetValue(delivery, null) as Texture;
        }

        private static IEnumerator TexturesMatchPixels(Texture expected, Texture actual, Action<bool> complete)
        {
            // Imported Normal maps can use a compressed platform format that rejects direct
            // ReadPixels / AsyncGPUReadback. Normalize both semantic inputs through the same
            // linear ARGB32 render target before comparing their sampled pixels. ARGB32 is
            // supported by this Player's readback path; both sides undergo identical 8-bit
            // quantization, so an equality failure still indicates semantic divergence.
            Color32[] expectedPixels = null;
            Color32[] actualPixels = null;
            for (int attempt = 0; attempt < 2 && expectedPixels == null; attempt++)
            {
                RenderTexture expectedCopy = RenderTexture.GetTemporary(expected.width, expected.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture actualCopy = RenderTexture.GetTemporary(actual.width, actual.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                try
                {
                    Graphics.Blit(expected, expectedCopy);
                    Graphics.Blit(actual, actualCopy);
                    AsyncGPUReadbackRequest expectedRequest = AsyncGPUReadback.Request(expectedCopy, 0, TextureFormat.RGBA32);
                    AsyncGPUReadbackRequest actualRequest = AsyncGPUReadback.Request(actualCopy, 0, TextureFormat.RGBA32);
                    while (!expectedRequest.done || !actualRequest.done) yield return null;
                    if (!expectedRequest.hasError && !actualRequest.hasError)
                    {
                        expectedPixels = expectedRequest.GetData<Color32>().ToArray();
                        actualPixels = actualRequest.GetData<Color32>().ToArray();
                    }
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(actualCopy);
                    RenderTexture.ReleaseTemporary(expectedCopy);
                }
                if (expectedPixels == null) yield return null;
            }
            Assert.That(expectedPixels, Is.Not.Null, "Normalized Normal pixel readback failed after one clean-frame retry.");
            if (expectedPixels.Length != actualPixels.Length) { complete(false); yield break; }
            for (int index = 0; index < expectedPixels.Length; index++)
                if (!expectedPixels[index].Equals(actualPixels[index])) { complete(false); yield break; }
            complete(true);
        }
    }
}
