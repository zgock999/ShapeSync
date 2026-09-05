// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Tests.EditMode.Spec19
{
    public sealed class HotBakeComponentSurfaceTests
    {
        [Test]
        public void Hybrid_PruneReboundCandidate_RemovesCloneSkeletonAndRuntimeBehaviours()
        {
            var candidate = new GameObject("Spec19_HybridPruneCandidate");
            GameObject skeleton = null;
            try
            {
                candidate.AddComponent<SkinnedMeshRenderer>();
                candidate.AddComponent<Animator>();
                skeleton = new GameObject("Root"); skeleton.transform.SetParent(candidate.transform, false);
                var hips = new GameObject("J_Bip_C_Hips"); hips.transform.SetParent(skeleton.transform, false);
                skeleton.AddComponent<ShapeDirector>();

                MethodInfo prune = typeof(HybridHotBakeFigure).GetMethod("PruneReboundCandidate", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(prune, Is.Not.Null);
                prune.Invoke(null, new object[] { candidate });

                Assert.That(candidate.transform.Find("Root"), Is.Null, "The detached clone skeleton must not remain below the promoted Hybrid renderer.");
                Assert.That(candidate.GetComponent<Animator>(), Is.Null, "The baked Hybrid renderer must not retain a cloned animation owner.");
                Assert.That(candidate.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, "The baked renderer hierarchy must not retain VRM, SpringBone, Director, or other runtime behaviours.");
            }
            finally
            {
                if (candidate != null) Object.DestroyImmediate(candidate);
                if (skeleton != null) Object.DestroyImmediate(skeleton);
            }
        }

        [Test]
        public void DynamicBoneBlender_AvatarRebuildExcludesAndRestoresHybridBakedRoot()
        {
            var figure = new GameObject("Spec19_HybridAvatarFigure");
            GameObject baked = null;
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                HybridHotBakeFigure hybrid = figure.AddComponent<HybridHotBakeFigure>();
                baked = new GameObject("Spec19_HybridBakedRoot");
                baked.transform.SetParent(figure.transform, false);
                typeof(HybridHotBakeFigure).GetField("bakedRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hybrid, baked);

                var detached = new List<Transform>();
                MethodInfo exclude = typeof(DynamicBoneBlender).GetMethod("DetachAvatarBuilderExcludedRoots", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(exclude, Is.Not.Null);
                exclude.Invoke(blender, new object[] { detached });

                Assert.That(detached, Is.EquivalentTo(new[] { baked.transform }));
                Assert.That(baked.transform.parent, Is.Null, "The duplicate Hybrid skeleton must be absent while AvatarBuilder resolves human bone names.");
                for (int index = 0; index < detached.Count; index++) detached[index].SetParent(figure.transform, true);
                Assert.That(baked.transform.parent, Is.SameAs(figure.transform), "The warm artifact must be restored to the Figure hierarchy after rebuilding the Avatar.");
            }
            finally
            {
                if (figure != null) Object.DestroyImmediate(figure);
                else if (baked != null) Object.DestroyImmediate(baked);
            }
        }

        [Test]
        public void Hybrid_Awake_NormalizesSerializedRunModeToEdit()
        {
            var root = new GameObject("Spec19_9_HybridAwakeMode");
            try
            {
                HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                SerializedObject serialized = new SerializedObject(component);
                serialized.FindProperty("mode").enumValueIndex = (int)HybridHotBakeFigureMode.Run;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                typeof(HybridHotBakeFigure).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                Assert.That(component.IsRunMode, Is.False);
                Assert.That(component.Mode, Is.EqualTo(HybridHotBakeFigureMode.Edit));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Hybrid_OnDisable_RestoresLiveDisplayAndDirectorMutationGate()
        {
            var figure = new GameObject("Spec19_9_HybridDisableRestore");
            GameObject baked = null;
            try
            {
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                SkinnedMeshRenderer liveRenderer = figure.AddComponent<SkinnedMeshRenderer>();
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                component.Director = director;
                baked = new GameObject("Spec19_9_HybridDisableBaked");
                baked.transform.SetParent(figure.transform, false);
                baked.SetActive(false);
                typeof(HybridHotBakeFigure).GetField("bakedRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, baked);

                typeof(HybridHotBakeFigure).GetMethod("EnterRunMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                Assert.That(director.IsRuntimeMutationBlocked, Is.True);
                Assert.That(liveRenderer.enabled, Is.False);
                Assert.That(blender.enabled, Is.False);
                Assert.That(baked.activeSelf, Is.True);

                typeof(HybridHotBakeFigure).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                Assert.That(component.IsRunMode, Is.False);
                Assert.That(director.IsRuntimeMutationBlocked, Is.False);
                Assert.That(liveRenderer.enabled, Is.True);
                Assert.That(blender.enabled, Is.True);
                Assert.That(baked.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(figure); }
        }

        [Test]
        public void Hybrid_InspectorRunRequest_RemainsSelectedWhileWarmBakeIsPending()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_9_HybridInspectorRunRequestHost");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());

                    SerializedObject serialized = new SerializedObject(component);
                    serialized.FindProperty("mode").enumValueIndex = (int)HybridHotBakeFigureMode.Run;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    Assert.That(component.Mode, Is.EqualTo(HybridHotBakeFigureMode.Run));
                    Assert.That(component.IsRunMode, Is.False);
                    Assert.That(component.IsCompileActive, Is.True, component.LastDiagnostic?.message);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

 #if SHAPESYNC_RICH_TEST
        [Test]
        public void RichOracleFixture_ProvidesIndependentAtlasOffAndAtlasOnPublishedBaselines()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            const string atlasPath = "Assets/zgock/ShapeSync/PlayTest/Spec18/AtlasDocB-2page.asset";
            const string offPath = "Assets/zgock/ShapeSync/PlayTest/Spec17/Pure/DocB/DocB.prefab";
            const string onPath = "Assets/zgock/ShapeSync/PlayTest/Spec18/DocBAtlas/DocBAtlas.prefab";
            GameObject figure = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeSyncDocumentAsset document = AssetDatabase.LoadAssetAtPath<ShapeSyncDocumentAsset>(documentPath);
            AtlasSchema atlas = AssetDatabase.LoadAssetAtPath<AtlasSchema>(atlasPath);
            GameObject atlasOff = AssetDatabase.LoadAssetAtPath<GameObject>(offPath);
            GameObject atlasOn = AssetDatabase.LoadAssetAtPath<GameObject>(onPath);

            Assert.That(figure, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(atlas, Is.Not.Null, atlasPath);
            Assert.That(atlasOff, Is.Not.Null, offPath);
            Assert.That(atlasOn, Is.Not.Null, onPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument snapshot, out StackMachineDiagnostic documentDiagnostic), Is.True, documentDiagnostic?.message);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(atlas.ToDocument(), Is.Not.Null);
            Assert.That(atlasOff.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.GreaterThan(0));
            Assert.That(atlasOn.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.GreaterThan(0));
        }
#endif

        [Test]
        public void Compile_RejectsIncompleteSerializedInputsWithoutCreatingTransaction()
        {
            var root = new GameObject("Spec19_8_Component");
            try
            {
                var component = root.AddComponent<HotBakeSpawner>();
                ExpectHotBakeError("HotBakeStartupInputIncomplete");
                Assert.That(component.Compile(out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeStartupInputIncomplete"));
                Assert.That(component.IsCompileActive, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TerminalFailure_SurfacesOnceAndIntentionalSupersessionRemainsSilent()
        {
            var root = new GameObject("Spec19_HotBakeDiagnosticSurface");
            try
            {
                var component = root.AddComponent<TestComponent>();
                LogAssert.Expect(LogType.Error, new Regex(@"^Hot Bake 'Spec19_HotBakeDiagnosticSurface' failed \[HotBakeTerminalFailure\]:"));
                component.EmitDiagnostic(StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeTerminalFailure", "Terminal Hot Bake failure."));
                component.EmitDiagnostic(StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeTerminalFailure", "Repeated terminal Hot Bake failure."));
                component.EmitDiagnostic(StackMachineDiagnostic.CreateDomain("texture", "RequestStale", "An intended supersession remained structured only."));
                LogAssert.NoUnexpectedReceived();
                Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("RequestStale"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Startup_DoesNotBeginCompileUntilAllRequiredInputsExist()
        {
            var root = new GameObject("Spec19_8_Startup");
            try
            {
                var component = root.AddComponent<HotBakeSpawner>();
                MethodInfo start = typeof(HotBakeComponentBase).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(start, Is.Not.Null); start.Invoke(component, null);
                Assert.That(component.IsCompileActive, Is.False); Assert.That(component.LastDiagnostic, Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Inputs_ExposeSameSerializableValuesToCompileApi()
        {
            var root = new GameObject("Spec19_8_Inputs"); var prefab = new GameObject("Spec19_8_Prefab");
            try
            {
                var component = root.AddComponent<HybridHotBakeFigure>(); component.FigurePrefab = prefab; component.RequireAtlas = true;
                Assert.That(component.FigurePrefab, Is.SameAs(prefab)); Assert.That(component.RequireAtlas, Is.True); Assert.That(component.Atlas, Is.Null);
            }
            finally { Object.DestroyImmediate(prefab); Object.DestroyImmediate(root); }
        }

        [Test]
        public void PhysicsTransport_CoreOnlyFallbackIsDisabled()
        {
            var root = new GameObject("Spec19_8_VrmFlag");
            try { Assert.That(root.AddComponent<TestComponent>().PhysicsEnabled, Is.False); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CancelCompile_IsSafeWithoutActiveTransaction()
        {
            var root = new GameObject("Spec19_8_Cancel");
            try { var component = root.AddComponent<HotBakeSpawner>(); component.CancelCompile(); Assert.That(component.IsCompileActive, Is.False); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Compile_BeginsTransactionForValidFigureAndDocument()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_ValidCompile");
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>(); component.FigurePrefab = fixture.Prefab; component.Document = fixture.Document;
                    Assert.That(component.Compile(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message); Assert.That(component.IsCompileActive, Is.True);
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Startup_BeginsTransactionWhenFigureAndDocumentAreReady()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_ValidStartup");
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>(); component.FigurePrefab = fixture.Prefab; component.Document = fixture.Document;
                    typeof(HotBakeComponentBase).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsCompileActive, Is.True); component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Compile_RequiresAtlasOnlyWhenAtlasIsConfiguredAsRequired()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_RequiredAtlas");
                AtlasSchema atlas = CreateEmptyAtlasSchema();
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.RequireAtlas = true;

                    ExpectHotBakeError("HotBakeStartupInputIncomplete");
                    Assert.That(component.Compile(out StackMachineDiagnostic missingAtlas), Is.False);
                    Assert.That(missingAtlas.domainCode, Is.EqualTo("HotBakeStartupInputIncomplete"));

                    component.Atlas = atlas;
                    Assert.That(component.Compile(out StackMachineDiagnostic accepted), Is.True, accepted?.message);
                    component.CancelCompile();
                }
                finally
                {
                    Object.DestroyImmediate(atlas);
                    Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void Compile_ResolvesBothUnconfiguredHostsThroughFactory()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_9_FactoryResolvedHosts");
                try
                {
                    TextureStaticMachineFactory.ResetForTests();
                    Assert.That(TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost factoryHost, out StackMachineDiagnostic factoryDiagnostic), Is.True, factoryDiagnostic?.message);
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;

                    Assert.That(component.Compile(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(component.EffectiveNormalHost, Is.SameAs(factoryHost));
                    Assert.That(component.EffectiveMaterialHost, Is.SameAs(factoryHost));
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); TextureStaticMachineFactory.ResetForTests(); }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Compile_SharesTheSoleExplicitHostWithBothPhases(bool assignNormalHost)
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_9_ExplicitSharedHost");
                var hostRoot = new GameObject("Spec19_9_ExplicitSharedHost_TSM");
                try
                {
                    var host = hostRoot.AddComponent<TextureStackMachineHost>();
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    if (assignNormalHost) component.NormalHost = host;
                    else component.MaterialHost = host;

                    Assert.That(component.Compile(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(component.EffectiveNormalHost, Is.SameAs(host));
                    Assert.That(component.EffectiveMaterialHost, Is.SameAs(host));
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hostRoot); }
            }
        }

        [Test]
        public void Compile_RejectsConfiguredAtlasThatIsNotAnAtlasSchema()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_9_InvalidAtlasType");
                var invalidAtlas = new Texture2D(1, 1);
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.Atlas = invalidAtlas;
                    ExpectHotBakeError("HotBakeAtlasSchemaInvalid");
                    Assert.That(component.Compile(out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeAtlasSchemaInvalid"));
                    Assert.That(component.IsCompileActive, Is.False);
                }
                finally { Object.DestroyImmediate(invalidAtlas); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Compile_RejectsSecondTriggerWhileTransactionIsActive()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_ActiveCompile");
                try
                {
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    Assert.That(component.Compile(out StackMachineDiagnostic accepted), Is.True, accepted?.message);

                    ExpectHotBakeError("HotBakeCompileActive");
                    Assert.That(component.Compile(out StackMachineDiagnostic duplicate), Is.False);
                    Assert.That(duplicate.domainCode, Is.EqualTo("HotBakeCompileActive"));
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Completion_PromotesSuccessfulTransactionIntoSceneScopedArtifactSet()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_CompletionOwner");
                var hostRoot = new GameObject("Spec19_8_CompletionHost");
                try
                {
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    Assert.That(component.Compile(out StackMachineDiagnostic started), Is.True, started?.message);

                    using (var scope = new HotBakeArtifactSceneScope(root, hostRoot.AddComponent<TextureStackMachineHost>()))
                    {
                        HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
                        StackMachineDiagnostic diagnostic = null;
                        for (int step = 0; step < 12 && status == HumanoidBuildOperationStatus.Pending; step++)
                            status = component.PumpAndCommit(scope, out diagnostic);

                        Assert.That(status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), diagnostic?.message);
                        Assert.That(component.ArtifactSet, Is.Not.Null);
                        Assert.That(scope.ArtifactSet, Is.SameAs(component.ArtifactSet));
                        Assert.That(component.IsCompileActive, Is.False);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(hostRoot);
                }
            }
        }

        [Test]
        public void OnDestroy_CancelsAnInFlightCompileTransaction()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_DestroyLifecycle");
                try
                {
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    Assert.That(component.Compile(out StackMachineDiagnostic started), Is.True, started?.message);
                    Assert.That(component.IsCompileActive, Is.True);

                    component.InvokeDestroyLifecycle();
                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.ArtifactSet, Is.Null);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Spawner_StartupBakeCreatesOneSharedArtifactInstancePerTargetAndDespawnsThem()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_SpawnerOwner");
                var hostRoot = new GameObject("Spec19_8_SpawnerHost");
                var firstTarget = new GameObject("Spec19_8_SpawnerTargetA");
                var secondTarget = new GameObject("Spec19_8_SpawnerTargetB");
                try
                {
                    firstTarget.transform.localPosition = new Vector3(3f, -2f, 7f);
                    firstTarget.transform.localRotation = Quaternion.Euler(15f, 35f, -20f);
                    firstTarget.transform.localScale = new Vector3(2f, 3f, 4f);
                    secondTarget.transform.localPosition = new Vector3(-5f, 1f, 9f);
                    secondTarget.transform.localRotation = Quaternion.Euler(-10f, 70f, 25f);
                    secondTarget.transform.localScale = new Vector3(0.5f, 1.5f, 2.5f);

                    var component = root.AddComponent<TestSpawner>();
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    // This structural spawn fixture has no Vrm10Instance source role. Physics
                    // transport is covered by the dedicated true/false tests below.
                    SetPhysicsTransport(component, false);
                    component.SpawnTargets.Add(firstTarget.transform);
                    component.SpawnTargets.Add(secondTarget.transform);
                    typeof(HotBakeSpawner).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    MethodInfo update = typeof(HotBakeSpawner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int step = 0; step < 12 && component.IsCompileActive; step++) update.Invoke(component, null);

                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.ArtifactSet, Is.Not.Null, component.LastDiagnostic?.message);
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    Assert.That(component.SpawnedInstances[0].activeSelf, Is.True);
                    Assert.That(component.SpawnedInstances[0].activeInHierarchy, Is.True);
                    Assert.That(component.SpawnedInstances[1].activeSelf, Is.True);
                    Assert.That(component.SpawnedInstances[1].activeInHierarchy, Is.True);
                    Assert.That(ContainsNonSceneHierarchyObject(component.SpawnedInstances[0]), Is.False);
                    Assert.That(ContainsNonSceneHierarchyObject(component.SpawnedInstances[1]), Is.False);
                    Assert.That(component.SpawnedInstances[0].transform.parent, Is.SameAs(firstTarget.transform));
                    Assert.That(component.SpawnedInstances[1].transform.parent, Is.SameAs(secondTarget.transform));
                    Assert.That(component.SpawnedInstances[0].transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(component.SpawnedInstances[1].transform.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(component.SpawnedInstances[0].transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(component.SpawnedInstances[1].transform.localRotation, Is.EqualTo(Quaternion.identity));
                    Assert.That(component.SpawnedInstances[0].transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(component.SpawnedInstances[1].transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(component.SpawnedInstances[0].GetComponent<SkinnedMeshRenderer>().sharedMesh,
                        Is.SameAs(component.SpawnedInstances[1].GetComponent<SkinnedMeshRenderer>().sharedMesh));
                    Animator firstAnimator = component.SpawnedInstances[0].GetComponentInChildren<Animator>(true);
                    Animator secondAnimator = component.SpawnedInstances[1].GetComponentInChildren<Animator>(true);
                    Assert.That(firstAnimator, Is.Not.Null);
                    Assert.That(secondAnimator, Is.Not.Null);
                    Assert.That(firstAnimator.avatar, Is.Not.Null);
                    Assert.That(secondAnimator.avatar, Is.SameAs(firstAnimator.avatar));

                    GameObject previousFirst = component.SpawnedInstances[0];
                    HotBakeArtifactSet previousArtifact = component.ArtifactSet;
                    component.Document = null;
                    ExpectHotBakeError("HotBakeStartupInputIncomplete");
                    Assert.That(component.Compile(out StackMachineDiagnostic rejectedReplacement), Is.False);
                    Assert.That(rejectedReplacement.domainCode, Is.EqualTo("HotBakeStartupInputIncomplete"));
                    Assert.That(component.ArtifactSet, Is.SameAs(previousArtifact));
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    Assert.That(previousFirst == null, Is.False, "A rejected replacement compile must preserve the current owned instances.");
                    component.Document = fixture.Document;

                    component.SpawnTargets[1] = null;
                    Assert.That(component.TrySpawnAll(out StackMachineDiagnostic rejectedSpawn), Is.False);
                    Assert.That(rejectedSpawn.domainCode, Is.EqualTo("HotBakeSpawnTargetRequired"));
                    Assert.That(component.ArtifactSet, Is.SameAs(previousArtifact));
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    Assert.That(previousFirst == null, Is.False, "A rejected replacement spawn must preserve the current owned instances.");
                    component.SpawnTargets[1] = secondTarget.transform;

                    Assert.That(component.TrySpawnAll(out StackMachineDiagnostic replacement), Is.True, replacement?.message);
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2), "A repeated spawn request must replace, not append to, the N instances.");
                    Assert.That(previousFirst == null, Is.True, "EditMode replacement must destroy the previous component-owned instance.");

                    component.InvokeDestroyLifecycle();
                    Assert.That(component.SpawnedInstances, Is.Empty);
                    Assert.That(component.ArtifactSet, Is.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(secondTarget);
                    Object.DestroyImmediate(firstTarget);
                    Object.DestroyImmediate(hostRoot);
                }
            }
        }

        [Test]
        public void Spawner_PartialTargetFailureRollsBackAndAllowsRetry()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_SpawnerRollbackOwner");
                var hostRoot = new GameObject("Spec19_8_SpawnerRollbackHost");
                var firstTarget = new GameObject("Spec19_8_SpawnerRollbackTargetA");
                var retryTarget = new GameObject("Spec19_8_SpawnerRollbackTargetB");
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>();
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    SetPhysicsTransport(component, false);
                    component.SpawnTargets.Add(firstTarget.transform);
                    component.SpawnTargets.Add(null);
                    typeof(HotBakeSpawner).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    MethodInfo update = typeof(HotBakeSpawner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    ExpectHotBakeError("HotBakeSpawnTargetRequired");
                    for (int step = 0; step < 12 && component.IsCompileActive; step++) update.Invoke(component, null);

                    Assert.That(component.ArtifactSet, Is.Not.Null);
                    Assert.That(component.SpawnedInstances, Is.Empty, "A failed later target must roll back earlier instances from the same request.");
                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("HotBakeSpawnTargetRequired"));

                    component.SpawnTargets[1] = retryTarget.transform;
                    Assert.That(component.TrySpawnAll(out StackMachineDiagnostic retry), Is.True, retry?.message);
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(2));
                    component.DespawnAll();
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(retryTarget);
                    Object.DestroyImmediate(firstTarget);
                    Object.DestroyImmediate(hostRoot);
                }
            }
        }

        [Test]
        public void Spawner_InvalidatedArtifactRejectsZeroTargetSpawnRequest()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_SpawnerInvalidatedOwner");
                var hostRoot = new GameObject("Spec19_8_SpawnerInvalidatedHost");
                try
                {
                    var component = root.AddComponent<HotBakeSpawner>();
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    typeof(HotBakeSpawner).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    MethodInfo update = typeof(HotBakeSpawner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int step = 0; step < 12 && component.IsCompileActive; step++) update.Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Not.Null);

                    ExpectHotBakeError("HotBakeHostDestroyed");
                    Object.DestroyImmediate(hostRoot);
                    update.Invoke(component, null);
                    Assert.That(component.TrySpawnAll(out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactUnavailable"));
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    if (hostRoot != null) Object.DestroyImmediate(hostRoot);
                }
            }
        }

        [Test]
        public void Figure_StartupSpawnsOneChildAndBindsAncestorAnimatorFromFigureRoot()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var animatorRoot = new GameObject("Spec19_8_FigureAnimatorRoot");
                var intermediate = new GameObject("Spec19_8_FigureIntermediate");
                var figureRoot = new GameObject("Spec19_8_FigureRoot");
                var hostRoot = new GameObject("Spec19_8_FigureHost");
                try
                {
                    intermediate.transform.SetParent(animatorRoot.transform, false);
                    figureRoot.transform.SetParent(intermediate.transform, false);
                    Animator ancestorAnimator = animatorRoot.AddComponent<Animator>();
                    ancestorAnimator.avatar = fixture.Prefab.GetComponent<Animator>().avatar;
                    Avatar originalAvatar = ancestorAnimator.avatar;
                    var component = figureRoot.AddComponent<HotBakeFigure>();
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    SetPhysicsTransport(component, false);
                    typeof(HotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    MethodInfo update = typeof(HotBakeSpawner).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int step = 0; step < 12 && component.IsCompileActive; step++) update.Invoke(component, null);

                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.LastDiagnostic, Is.Null, component.LastDiagnostic?.message);
                    Assert.That(component.SpawnedInstances.Count, Is.EqualTo(1));
                    GameObject instance = component.SpawnedInstances[0];
                    Assert.That(instance.transform.parent, Is.SameAs(figureRoot.transform));
                    Assert.That(ancestorAnimator.avatar, Is.Not.Null);
                    Assert.That(ancestorAnimator.avatar, Is.Not.SameAs(originalAvatar));
                    Assert.That(ancestorAnimator.avatar.isHuman, Is.True);
                    Assert.That(ancestorAnimator.avatar.humanDescription.skeleton[0].name, Is.EqualTo(figureRoot.name));
                    Avatar generatedAvatar = ancestorAnimator.avatar;
                    Animator childAnimator = instance.GetComponentInChildren<Animator>(true);
                    Assert.That(childAnimator, Is.Not.Null);
                    Assert.That(childAnimator.enabled, Is.False, "The generated child Animator must not compete with the resolved Figure Animator.");

                    typeof(HotBakeFigure).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(generatedAvatar == null, Is.True, "Figure owner teardown must release its generated Avatar.");
                }
                finally
                {
                    Object.DestroyImmediate(hostRoot);
                    Object.DestroyImmediate(animatorRoot);
                }
            }
        }

        [Test]
        public void Figure_StartupRejectsWhenNoAncestorAnimatorExists()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var figureRoot = new GameObject("Spec19_8_FigureWithoutAnimator");
                try
                {
                    var component = figureRoot.AddComponent<HotBakeFigure>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    ExpectHotBakeError("AnimatorRequired");
                    typeof(HotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.SpawnedInstances, Is.Empty);
                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("AnimatorRequired"));
                }
                finally { Object.DestroyImmediate(figureRoot); }
            }
        }

        [Test]
        public void Figure_BindRejectsSkeletonOutsideBuildRootAndMissingSourceAvatar()
        {
            var animatorRoot = new GameObject("Spec19_8_FigureBindAnimatorRoot");
            var figureRoot = new GameObject("Spec19_8_FigureBindRoot");
            var externalRoot = new GameObject("Spec19_8_FigureExternalSkeleton");
            var invalidChild = new GameObject("Spec19_8_FigureInvalidChild");
            try
            {
                figureRoot.transform.SetParent(animatorRoot.transform, false);
                animatorRoot.AddComponent<Animator>();
                invalidChild.transform.SetParent(figureRoot.transform, false);
                Animator invalidAnimator = invalidChild.AddComponent<Animator>();
                Avatar genericAvatar = AvatarBuilder.BuildGenericAvatar(invalidChild, string.Empty);
                Assert.That(genericAvatar, Is.Not.Null);
                Assert.That(genericAvatar.isHuman, Is.False);
                invalidAnimator.avatar = genericAvatar;
                var component = figureRoot.AddComponent<HotBakeFigure>();
                MethodInfo prepare = typeof(HotBakeFigure).GetMethod("TryPrepareFigure", BindingFlags.Instance | BindingFlags.NonPublic);
                object[] prepareArgs = { null };
                Assert.That((bool)prepare.Invoke(component, prepareArgs), Is.True, ((StackMachineDiagnostic)prepareArgs[0])?.message);
                MethodInfo bind = typeof(HotBakeFigure).GetMethod("TryBindAnimator", BindingFlags.Instance | BindingFlags.NonPublic);

                object[] externalArgs = { externalRoot, null };
                Assert.That((bool)bind.Invoke(component, externalArgs), Is.False);
                Assert.That(((StackMachineDiagnostic)externalArgs[1]).domainCode, Is.EqualTo("HotBakeFigureSkeletonOutsideBuildRoot"));

                object[] invalidArgs = { invalidChild, null };
                Assert.That((bool)bind.Invoke(component, invalidArgs), Is.False);
                Assert.That(((StackMachineDiagnostic)invalidArgs[1]).domainCode, Is.EqualTo("HotBakeFigureSourceAvatarRequired"));
            }
            finally
            {
                Object.DestroyImmediate(externalRoot);
                Object.DestroyImmediate(animatorRoot);
            }
        }

        [Test]
        public void Director_CurrentStateDocument_IsAbsoluteAndExcludesRecoveryPrefixes()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_CurrentStateDocument");
                MaterialBinding materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    ShapeDirector director = root.AddComponent<ShapeDirector>();
                    FieldInfo meshBinding = typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo directorMaterialBinding = typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo commit = typeof(ShapeDirector).GetMethod("CommitCurrentPhysicalShapes", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(meshBinding, Is.Not.Null);
                    Assert.That(directorMaterialBinding, Is.Not.Null);
                    Assert.That(commit, Is.Not.Null);
                    meshBinding.SetValue(director, fixture.Document.MeshBinding);
                    directorMaterialBinding.SetValue(director, materialBinding);
                    commit.Invoke(director, new object[]
                    {
                        new List<ShapeSyncShape>
                        {
                            new MorphShape("current", 0, null, new[] { new MorphValue { Target = "girl", Value = 0.7f } }),
                            new SkinShape("material", 1, null, new ShapeEntry[] { new ColorEntry { ProxyEntry = "body", Color = new Color32(10, 20, 30, 255) } })
                        }
                    });

                    Assert.That(director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(document.MeshBinding, Is.SameAs(fixture.Document.MeshBinding));
                    Assert.That(document.MeshRecipe.wordSource, Is.EqualTo("$girl 0.7 FBM_SET"));
                    Assert.That(document.MeshRecipe.wordSource, Does.Not.Contain("DETACH_ALL"));
                    Assert.That(document.MeshRecipe.wordSource, Does.Not.Contain("MORPH_RESET"));
                    Assert.That(document.MaterialBinding, Is.SameAs(materialBinding));
                    Assert.That(document.MaterialRecipe.wordSource, Does.Not.Contain("MATERIAL_RESET"));
                }
                finally { Object.DestroyImmediate(materialBinding); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Director_CurrentStateDocument_RejectsMeshRecipeWithoutMeshBinding()
        {
            var root = new GameObject("Spec19_8_CurrentStateMeshBindingRequired");
            try
            {
                ShapeDirector director = root.AddComponent<ShapeDirector>();
                CommitPhysicalShapes(director, new List<ShapeSyncShape>
                {
                    new MorphShape("current", 0, null, new[] { new MorphValue { Target = "girl", Value = 0.7f } })
                });

                Assert.That(director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(document, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshBindingRequired"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Director_CurrentStateDocument_RejectsMaterialRecipeWithoutMaterialBinding()
        {
            var root = new GameObject("Spec19_8_CurrentStateMaterialBindingRequired");
            try
            {
                ShapeDirector director = root.AddComponent<ShapeDirector>();
                CommitPhysicalShapes(director, new List<ShapeSyncShape>
                {
                    new SkinShape("material", 0, null, new ShapeEntry[] { new ColorEntry { ProxyEntry = "body", Color = new Color32(10, 20, 30, 255) } })
                });

                Assert.That(director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(document, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialBindingRequired"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Director_CurrentStateDocument_EmptyCommittedStateRetainsResolvedBindingContext()
        {
            var root = new GameObject("Spec19_8_CurrentStateEmpty");
            MeshBinding meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
            MaterialBinding materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                ShapeDirector director = root.AddComponent<ShapeDirector>();
                typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, meshBinding);
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, materialBinding);
                CommitPhysicalShapes(director, new List<ShapeSyncShape>());

                Assert.That(director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(document, Is.Not.Null);
                Assert.That(document.MeshRecipe, Is.Null);
                Assert.That(document.MaterialRecipe, Is.Null);
                Assert.That(document.MeshBinding, Is.SameAs(meshBinding));
                Assert.That(document.MaterialBinding, Is.SameAs(materialBinding));
            }
            finally
            {
                Object.DestroyImmediate(materialBinding);
                Object.DestroyImmediate(meshBinding);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Hybrid_StartupBeginsInitialWarmBakeAfterDirectorRuntimeStateIsConstructed()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_HybridNoStartup");
                var hostRoot = new GameObject("Spec19_8_HybridStartupHost");
                try
                {
                    ShapeDirector director = root.AddComponent<ShapeDirector>();
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.NormalHost = host;

                    typeof(ShapeDirector).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null);
                    component.Director = director;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.IsBakePending, Is.False);
                    Assert.That(component.Revision, Is.Zero);

                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    Assert.That(component.IsCompileActive, Is.True, component.LastDiagnostic?.message);
                    Assert.That(component.IsBakePending, Is.False);
                    Assert.That(component.Revision, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Hybrid_DirectorCommitStartsLatestBakeAfterQuietWindowAndCancelsPreviousBuild()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_HybridRevisionGate");
                try
                {
                    ShapeDirector director = root.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    Assert.That(component.Revision, Is.EqualTo(1));
                    Assert.That(component.IsBakePending, Is.True);
                    Assert.That(component.IsCompileActive, Is.False);

                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsBakePending, Is.False);
                    Assert.That(component.IsCompileActive, Is.True, component.LastDiagnostic?.message);
                    Assert.That(component.ActiveRevision, Is.EqualTo(1));

                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    Assert.That(component.Revision, Is.EqualTo(2));
                    Assert.That(component.IsBakePending, Is.True);
                    Assert.That(component.IsCompileActive, Is.False);

                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsBakePending, Is.False);
                    Assert.That(component.IsCompileActive, Is.True, component.LastDiagnostic?.message);
                    Assert.That(component.ActiveRevision, Is.EqualTo(2));
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Hybrid_QuietWindowDefersBakeUntilItsClockHasElapsed()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_HybridQuietWindow");
                try
                {
                    ShapeDirector director = root.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.QuietWindowSeconds = 0.1f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsBakePending, Is.True);
                    Assert.That(component.IsCompileActive, Is.False);

                    typeof(HybridHotBakeFigure).GetField("lastCommitTime", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(component, Time.realtimeSinceStartup - component.QuietWindowSeconds - 0.01f);
                    typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.IsBakePending, Is.False);
                    Assert.That(component.IsCompileActive, Is.True, component.LastDiagnostic?.message);
                    component.CancelCompile();
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Hybrid_CompileUsesCurrentDirectorStateAndRejectsMissingDirector()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_HybridExplicitCompile");
                var missingRoot = new GameObject("Spec19_8_HybridDirectorRequired");
                try
                {
                    ShapeDirector director = root.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());

                    Assert.That(component.Compile(out StackMachineDiagnostic accepted), Is.True, accepted?.message);
                    Assert.That(component.ActiveRevision, Is.EqualTo(2));
                    Assert.That(component.IsCompileActive, Is.True);
                    component.CancelCompile();

                    HybridHotBakeFigure missing = missingRoot.AddComponent<HybridHotBakeFigure>();
                    Assert.That(missing.Compile(out StackMachineDiagnostic rejected), Is.False);
                    Assert.That(rejected.domainCode, Is.EqualTo("HotBakeDirectorRequired"));
                }
                finally { Object.DestroyImmediate(missingRoot); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Hybrid_OnDestroyUnsubscribesFromDirector()
        {
            var root = new GameObject("Spec19_8_HybridUnsubscribe");
            try
            {
                ShapeDirector director = root.AddComponent<ShapeDirector>();
                HybridHotBakeFigure component = root.AddComponent<HybridHotBakeFigure>();
                component.Director = director;
                FieldInfo startingEventField = typeof(ShapeDirector).GetField("TransactionStarting", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo eventField = typeof(ShapeDirector).GetField("TransactionCommitted", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(startingEventField, Is.Not.Null);
                Assert.That(eventField, Is.Not.Null);
                Assert.That(startingEventField.GetValue(director), Is.Not.Null, "Edit Mode artifact owners must subscribe before Inspector-driven Load can begin a transaction.");
                Assert.That(eventField.GetValue(director), Is.Not.Null);

                // EditMode immediate destruction does not guarantee Unity message dispatch;
                // invoke the component lifecycle hook directly to verify its subscription owner.
                typeof(HybridHotBakeFigure).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                Assert.That(startingEventField.GetValue(director), Is.Null);
                Assert.That(eventField.GetValue(director), Is.Null);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Hybrid_RebindCandidateMapsMatchingBonePathAndRestPoseToLiveFigure()
        {
            var figure = new GameObject("Spec19_8_HybridRebindFigure");
            GameObject candidate = null;
            Mesh mesh = null;
            try
            {
                HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                Transform liveBone = new GameObject("Bone").transform; liveBone.SetParent(figure.transform, false);
                candidate = CreateRebindCandidate("Spec19_8_Candidate", out Transform candidateBone, out SkinnedMeshRenderer renderer, out mesh, weighted: true, rootBone: true);

                Assert.That(TryRebindCandidate(component, candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(renderer.bones[0], Is.SameAs(liveBone));
                Assert.That(renderer.rootBone, Is.SameAs(liveBone));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(candidate); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void Hybrid_RebindCandidateUsesLiveMeshBindPoseInsteadOfAnimatedTransformPose()
        {
            var figure = new GameObject("Spec19_8_HybridBindPoseFigure");
            GameObject candidate = null;
            Mesh candidateMesh = null;
            Mesh liveMesh = null;
            try
            {
                HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                Transform liveBone = new GameObject("Bone").transform; liveBone.SetParent(figure.transform, false);
                // Simulate Animator's current pose: it differs from rest but must not reject rebind.
                liveBone.localPosition = Vector3.right;
                SkinnedMeshRenderer liveRenderer = figure.AddComponent<SkinnedMeshRenderer>();
                // Director/DDB may have updated the live renderer's bindpose independently of
                // the candidate.  The candidate retains its own bindpose, so this must not
                // reject a resolved weighted bone.
                liveMesh = new Mesh { bindposes = new[] { Matrix4x4.Translate(Vector3.up) } };
                liveRenderer.sharedMesh = liveMesh;
                liveRenderer.bones = new[] { liveBone };

                candidate = CreateRebindCandidate("Spec19_8_BindPoseCandidate", out _, out SkinnedMeshRenderer renderer, out candidateMesh, weighted: true, rootBone: true);

                Assert.That(TryRebindCandidate(component, candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(renderer.bones[0], Is.SameAs(liveBone));
            }
            finally { Object.DestroyImmediate(candidateMesh); Object.DestroyImmediate(liveMesh); Object.DestroyImmediate(candidate); Object.DestroyImmediate(figure); }
        }

 #if SHAPESYNC_RICH_TEST
        [Test]
        // This fixture attaches the production Hair Outfit before cloning the candidate.
        // Animator-updated Hair pose must remain rebindable because bindposes, not current
        // animated Transform TRS, define the skin rest pose contract.
        public void Hybrid_ActualAttachedHair_AnimatedPoseRebindsCandidate()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Spec19/19.9/Figure19.9Hybrid.prefab";
            const string hairPath = "Assets/zgock/ShapeSync/PlayTest/Common/Models/Hair1_PhysicsOutfit.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            GameObject hairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hairPath);
            Assert.That(prefab, Is.Not.Null, figurePath);
            Assert.That(hairPrefab, Is.Not.Null, hairPath);
            GameObject figure = Object.Instantiate(prefab);
            GameObject candidate = null;
            GameObject outfitRoot = null;
            try
            {
                HybridHotBakeFigure component = figure.GetComponent<HybridHotBakeFigure>();
                OutfitAttacher attacher = figure.GetComponent<OutfitAttacher>();
                Assert.That(component, Is.Not.Null);
                Assert.That(attacher, Is.Not.Null);
                outfitRoot = Object.Instantiate(hairPrefab);
                ShapeSyncOutfit outfit = outfitRoot.GetComponent<ShapeSyncOutfit>();
                Assert.That(outfit, Is.Not.Null);
                Assert.That(attacher.TryAttach(outfit), Is.True);

                // The candidate is captured before Animator pose changes; the live Figure is
                // then moved to a different current pose.  bindposes remain identical.
                candidate = Object.Instantiate(figure);
                Transform hair = FindDescendantByName(figure.transform, "J_Sec_Hair1_01");
                Assert.That(hair, Is.Not.Null);
                hair.localRotation *= Quaternion.Euler(8f, -11f, 5f);

                Assert.That(TryRebindCandidate(component, candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            }
            finally { Object.DestroyImmediate(candidate); Object.DestroyImmediate(outfitRoot); Object.DestroyImmediate(figure); }
        }
#endif

        [Test]
        public void Hybrid_RebindCandidateAcceptsAnimatedWeightedBoneAndRejectsWeightedMissingBone()
        {
            var figure = new GameObject("Spec19_8_HybridRebindReject");
            GameObject mismatchCandidate = null;
            GameObject missingCandidate = null;
            Mesh mismatchMesh = null;
            Mesh missingMesh = null;
            try
            {
                HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                Transform liveBone = new GameObject("Bone").transform; liveBone.SetParent(figure.transform, false); liveBone.localPosition = Vector3.right;
                mismatchCandidate = CreateRebindCandidate("Spec19_8_AnimatedCandidate", out _, out SkinnedMeshRenderer animatedRenderer, out mismatchMesh, weighted: true, rootBone: true);
                // This bone intentionally has no live renderer witness. Runtime animation may
                // change its TRS, but the candidate mesh owns the bindpose; path resolution is
                // the compatibility contract for a weighted rebind.
                Assert.That(TryRebindCandidate(component, mismatchCandidate, out StackMachineDiagnostic animated), Is.True, animated?.message);
                Assert.That(animatedRenderer.bones[0], Is.SameAs(liveBone));

                Object.DestroyImmediate(liveBone.gameObject);
                missingCandidate = CreateRebindCandidate("Spec19_8_MissingCandidate", out _, out _, out missingMesh, weighted: true, rootBone: false);
                Assert.That(TryRebindCandidate(component, missingCandidate, out StackMachineDiagnostic missing), Is.False);
                Assert.That(missing.domainCode, Is.EqualTo("HotBakeHybridWeightedBoneMissing"));
            }
            finally
            {
                Object.DestroyImmediate(mismatchMesh); Object.DestroyImmediate(missingMesh);
                Object.DestroyImmediate(mismatchCandidate); Object.DestroyImmediate(missingCandidate); Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Hybrid_RebindCandidateUsesFigureRootOnlyForUnweightedMissingBone()
        {
            var figure = new GameObject("Spec19_8_HybridRebindZeroWeight");
            GameObject candidate = null;
            Mesh mesh = null;
            try
            {
                HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                candidate = CreateRebindCandidate("Spec19_8_ZeroWeightCandidate", out _, out SkinnedMeshRenderer renderer, out mesh, weighted: false, rootBone: false);

                Assert.That(TryRebindCandidate(component, candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(renderer.bones[0], Is.SameAs(figure.transform));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(candidate); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void Hybrid_CurrentGenerationPromotesIntoSceneScopeAndNextCommitInvalidatesIt()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridPromotionHost");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);

                    Assert.That(component.LastDiagnostic, Is.Null, component.LastDiagnostic?.message);
                    Assert.That(component.BakedRoot, Is.Not.Null);
                    Assert.That(component.BakedRoot.activeSelf, Is.False, $"Hybrid promotion must remain in Edit Mode. mode={component.Mode}; runMode={component.IsRunMode}");
                    Assert.That(component.BakedRoot.transform.parent, Is.SameAs(figure.transform));
                    Assert.That(ContainsNonSceneHierarchyObject(component.BakedRoot), Is.False, "Promoted Hybrid hierarchy must clear Mesh Core HideAndDontSave flags.");
                    Assert.That(component.ArtifactSet, Is.Not.Null);
                    Assert.That(component.ArtifactSet.IsAvailable, Is.True);
                    Animator candidateAnimator = component.BakedRoot.GetComponentInChildren<Animator>(true);
                    Assert.That(candidateAnimator, Is.Null, "A promoted Hybrid artifact must retain no cloned Animator; the live Figure Animator is the sole animation owner.");

                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(component.ArtifactSet, Is.Null);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_WarmRunModeExchangesDisplayRejectsDirectorMutationAndRestoresEditState()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridRunModeHost");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                    NormalBlender normal = figure.AddComponent<NormalBlender>();
                    MaterialAttacher attacher = figure.AddComponent<MaterialAttacher>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);

                    SkinnedMeshRenderer liveRenderer = figure.GetComponentInChildren<SkinnedMeshRenderer>();
                    MaterialProxy proxy = figure.GetComponent<MaterialProxy>();
                    ulong warmRevision = component.Revision;
                    Assert.That(component.Mode, Is.EqualTo(HybridHotBakeFigureMode.Edit));
                    SerializedObject serialized = new SerializedObject(component);
                    serialized.FindProperty("mode").enumValueIndex = (int)HybridHotBakeFigureMode.Run;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    update.Invoke(component, null);
                    Assert.That(component.IsRunMode, Is.True);
                    Assert.That(component.Mode, Is.EqualTo(HybridHotBakeFigureMode.Run));
                    Assert.That(component.Revision, Is.EqualTo(warmRevision));
                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.BakedRoot.activeSelf, Is.True);
                    Assert.That(liveRenderer.enabled, Is.False);
                    Assert.That(blender.enabled && normal.enabled && attacher.enabled && proxy.enabled && director.enabled, Is.False);
                    Assert.That(director.TrySynchronizeTemplateList(out StackMachineDiagnostic blocked), Is.False);
                    Assert.That(blocked.domainCode, Is.EqualTo("DirectorRunModeMutationRejected"));
                    Assert.That(director.TryCompile(out StackMachineDiagnostic compileBlocked), Is.False);
                    Assert.That(compileBlocked.domainCode, Is.EqualTo("DirectorRunModeMutationRejected"));
                    Assert.That(director.TryDeserialize("ignored"), Is.False);
                    Assert.That(director.LastTransactionDiagnostic.domainCode, Is.EqualTo("DirectorRunModeMutationRejected"));

                    serialized.Update();
                    serialized.FindProperty("mode").enumValueIndex = (int)HybridHotBakeFigureMode.Edit;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    update.Invoke(component, null);
                    Assert.That(component.IsRunMode, Is.False);
                    Assert.That(component.Mode, Is.EqualTo(HybridHotBakeFigureMode.Edit));
                    Assert.That(component.BakedRoot.activeSelf, Is.False);
                    Assert.That(liveRenderer.enabled && blender.enabled && normal.enabled && attacher.enabled && proxy.enabled && director.enabled, Is.True);
                    Assert.That(director.IsRuntimeMutationBlocked, Is.False);

                    Assert.That(component.TrySetRunMode(true, out StackMachineDiagnostic reenter), Is.True, reenter?.message);
                    ShapeDirector replacement = figure.AddComponent<ShapeDirector>();
                    component.Director = replacement;
                    Assert.That(component.IsRunMode, Is.False);
                    Assert.That(component.BakedRoot.activeSelf, Is.False);
                    Assert.That(director.IsRuntimeMutationBlocked, Is.False);
                    Assert.That(director.enabled, Is.True);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_StaleRunModeRequestSynchronouslyBakesBeforeDisplayExchange()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridSynchronousRunHost");
                try
                {
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = hostRoot.AddComponent<TextureStackMachineHost>();

                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.TrySetRunMode(true, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.ArtifactSet, Is.Not.Null);
                    Assert.That(component.BakedRoot, Is.Not.Null);
                    Assert.That(component.BakedRoot.activeSelf, Is.True);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_HostDestroyClearsPromotedArtifactHandles()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridHostDestroy");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Not.Null);

                    ExpectHotBakeError("HotBakeHostDestroyed");
                    Object.DestroyImmediate(hostRoot);
                    update.Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("HotBakeHostDestroyed"));
                    hostRoot = null;
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_RunModeHostInvalidationRestoresLiveEditDisplay()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridRunHostInvalidation");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);

                    SkinnedMeshRenderer liveRenderer = figure.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(component.TrySetRunMode(true, out StackMachineDiagnostic enter), Is.True, enter?.message);
                    Assert.That(liveRenderer.enabled, Is.False);
                    ExpectHotBakeError("HotBakeHostDestroyed");
                    Object.DestroyImmediate(hostRoot);
                    hostRoot = null;
                    update.Invoke(component, null);

                    Assert.That(component.IsRunMode, Is.False);
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(liveRenderer.enabled, Is.True);
                    Assert.That(director.enabled, Is.True);
                    Assert.That(director.IsRuntimeMutationBlocked, Is.False);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_OnDestroyReleasesPromotedArtifactAndFigureChild()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridOwnerDestroy");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);
                    GameObject promoted = component.BakedRoot;
                    Assert.That(component.ArtifactSet, Is.Not.Null);

                    typeof(HybridHotBakeFigure).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(promoted == null, Is.True);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_OutfitTopologyInvalidationClearsPromotedArtifactHandles()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridTopologyInvalidation");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.BakedRoot == null && component.LastDiagnostic == null; i++) update.Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Not.Null);

                    HotBakeArtifactSceneScope scope = (HotBakeArtifactSceneScope)typeof(HybridHotBakeFigure).GetField("artifactScope", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
                    scope.InvalidateForOutfitTopology();
                    ExpectHotBakeError("HotBakeArtifactOutfitInvalidated");
                    update.Invoke(component, null);
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("HotBakeArtifactOutfitInvalidated"));
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_StaleTerminalGenerationIsDiscardedBeforePromotion()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridStaleGeneration");
                try
                {
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    update.Invoke(component, null);
                    Assert.That(component.IsCompileActive, Is.True);

                    typeof(HybridHotBakeFigure).GetField("revision", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, component.Revision + 1UL);
                    for (int i = 0; i < 24 && component.IsCompileActive; i++) update.Invoke(component, null);
                    Assert.That(component.IsCompileActive, Is.False);
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("HotBakeStaleGenerationDiscarded"));
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_RebindAcceptsAnimatedLiveBonePoseAndPromotesArtifact()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_9_HybridAnimatedLiveBoneRebind");
                try
                {
                    figure.transform.Find("Hips").localPosition += Vector3.right;
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = 0; i < 24 && component.LastDiagnostic == null && component.BakedRoot == null; i++) update.Invoke(component, null);

                    Assert.That(component.LastDiagnostic, Is.Null, component.LastDiagnostic?.message);
                    Assert.That(component.ArtifactSet, Is.Not.Null);
                    Assert.That(component.BakedRoot, Is.Not.Null);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        [Test]
        public void Hybrid_RebindFailureFromMissingWeightedLiveBoneDoesNotPromoteArtifact()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                GameObject figure = Object.Instantiate(fixture.Prefab);
                var hostRoot = new GameObject("Spec19_8_HybridLiveWeightedBoneReject");
                try
                {
                    Object.DestroyImmediate(figure.transform.Find("Hips").gameObject);
                    TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                    ShapeDirector director = figure.AddComponent<ShapeDirector>();
                    HybridHotBakeFigure component = figure.AddComponent<HybridHotBakeFigure>();
                    component.Director = director;
                    component.FigurePrefab = fixture.Prefab;
                    component.NormalHost = host;
                    component.QuietWindowSeconds = 0f;
                    typeof(HybridHotBakeFigure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                    CommitPhysicalShapes(director, new List<ShapeSyncShape>());
                    MethodInfo update = typeof(HybridHotBakeFigure).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                    ExpectHotBakeError("HotBakeHybridWeightedBoneMissing");
                    for (int i = 0; i < 24 && component.LastDiagnostic == null && component.BakedRoot == null; i++) update.Invoke(component, null);

                    Assert.That(component.LastDiagnostic.domainCode, Is.EqualTo("HotBakeHybridWeightedBoneMissing"));
                    Assert.That(component.ArtifactSet, Is.Null);
                    Assert.That(component.BakedRoot, Is.Null);
                }
                finally { Object.DestroyImmediate(hostRoot); Object.DestroyImmediate(figure); }
            }
        }

        private static void CommitPhysicalShapes(ShapeDirector director, List<ShapeSyncShape> shapes)
        {
            MethodInfo commit = typeof(ShapeDirector).GetMethod("CommitCurrentPhysicalShapes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(commit, Is.Not.Null);
            commit.Invoke(director, new object[] { shapes });
        }

        private static bool TryRebindCandidate(HybridHotBakeFigure component, GameObject candidate, out StackMachineDiagnostic diagnostic)
        {
            MethodInfo method = typeof(HybridHotBakeFigure).GetMethod("TryRebindCandidate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { candidate, null };
            bool result = (bool)method.Invoke(component, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[1];
            return result;
        }

        private static Transform FindDescendantByName(Transform root, string name)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
                if (transforms[index].name == name) return transforms[index];
            return null;
        }

        private static GameObject CreateRebindCandidate(string name, out Transform bone, out SkinnedMeshRenderer renderer, out Mesh mesh, bool weighted, bool rootBone)
        {
            var candidate = new GameObject(name);
            bone = new GameObject("Bone").transform; bone.SetParent(candidate.transform, false);
            renderer = candidate.AddComponent<SkinnedMeshRenderer>();
            mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                bindposes = new[] { Matrix4x4.identity },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = weighted ? 1f : 0f },
                    new BoneWeight { boneIndex0 = 0, weight0 = weighted ? 1f : 0f },
                    new BoneWeight { boneIndex0 = 0, weight0 = weighted ? 1f : 0f }
                }
            };
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = rootBone ? bone : null;
            return candidate;
        }

#if SHAPESYNC_USE_UNIVRM
        [Test]
        public void PhysicsTransport_TrueExecutesTransportBeforeArtifactCommit()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_VrmTransportOwner");
                var hostRoot = new GameObject("Spec19_8_VrmTransportHost");
                FieldInfo factory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo spawnInitializerFactory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("spawnInitializerFactory", BindingFlags.Static | BindingFlags.NonPublic);
                object original = factory.GetValue(null);
                object originalSpawnInitializer = spawnInitializerFactory.GetValue(null);
                var transporter = new RecordingTransporter();
                var spawnInitializer = new RecordingSpawnInitializer();
                try
                {
                    factory.SetValue(null, new Func<IHumanoidVrmPhysicsTransporter>(() => transporter));
                    spawnInitializerFactory.SetValue(null, new Func<IHumanoidVrmPhysicsSpawnInitializer>(() => spawnInitializer));
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.PhysicsTransport = true;
                    Assert.That(component.Compile(out StackMachineDiagnostic started), Is.True, started?.message);

                    using (var scope = new HotBakeArtifactSceneScope(root, hostRoot.AddComponent<TextureStackMachineHost>()))
                    {
                        Assert.That(PumpToTerminal(component, scope, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), diagnostic?.message);
                        Assert.That(transporter.InvocationCount, Is.EqualTo(1));
                        Assert.That(transporter.Ownership.Disposed, Is.False);
                        Assert.That(HotBakeSpawnPrimitive.TrySpawn(scope, root.transform, Vector3.zero, Quaternion.identity, true, out GameObject spawn, out diagnostic), Is.True, diagnostic?.message);
                        Assert.That(spawnInitializer.InvocationCount, Is.EqualTo(1));
                        scope.UnregisterSpawn(spawn);
                        Object.DestroyImmediate(spawn);
                    }
                    Assert.That(transporter.Ownership.Disposed, Is.True);
                }
                finally
                {
                    factory.SetValue(null, original);
                    spawnInitializerFactory.SetValue(null, originalSpawnInitializer);
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(hostRoot);
                }
            }
        }

        [Test]
        public void PhysicsTransport_FalseSkipsTransportBeforeArtifactCommit()
        {
            using (var fixture = new HotBakeBuildDriverTests.InputFixture(false, false))
            {
                var root = new GameObject("Spec19_8_VrmSkipOwner");
                var hostRoot = new GameObject("Spec19_8_VrmSkipHost");
                FieldInfo factory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo spawnInitializerFactory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("spawnInitializerFactory", BindingFlags.Static | BindingFlags.NonPublic);
                object original = factory.GetValue(null);
                object originalSpawnInitializer = spawnInitializerFactory.GetValue(null);
                try
                {
                    factory.SetValue(null, new Func<IHumanoidVrmPhysicsTransporter>(() => throw new AssertionException("Physics Transport false must not request a provider.")));
                    spawnInitializerFactory.SetValue(null, new Func<IHumanoidVrmPhysicsSpawnInitializer>(() => throw new AssertionException("Physics Transport false must not initialize a spawn.")));
                    var component = root.AddComponent<TestComponent>();
                    component.FigurePrefab = fixture.Prefab;
                    component.Document = fixture.Document;
                    component.PhysicsTransport = false;
                    Assert.That(component.Compile(out StackMachineDiagnostic started), Is.True, started?.message);

                    using (var scope = new HotBakeArtifactSceneScope(root, hostRoot.AddComponent<TextureStackMachineHost>()))
                    {
                        Assert.That(PumpToTerminal(component, scope, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), diagnostic?.message);
                        Assert.That(HotBakeSpawnPrimitive.TrySpawn(scope, root.transform, Vector3.zero, Quaternion.identity, false, out GameObject spawn, out diagnostic), Is.True, diagnostic?.message);
                        scope.UnregisterSpawn(spawn);
                        Object.DestroyImmediate(spawn);
                    }
                }
                finally
                {
                    factory.SetValue(null, original);
                    spawnInitializerFactory.SetValue(null, originalSpawnInitializer);
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(hostRoot);
                }
            }
        }

        private static HumanoidBuildOperationStatus PumpToTerminal(TestComponent component, HotBakeArtifactSceneScope scope, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
            for (int step = 0; step < 12 && status == HumanoidBuildOperationStatus.Pending; step++)
                status = component.PumpAndCommit(scope, out diagnostic);
            return status;
        }

#endif

        private static void ExpectHotBakeError(string diagnosticCode)
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[" + Regex.Escape(diagnosticCode) + @"\]"));
        }

        private static bool ContainsNonSceneHierarchyObject(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i].gameObject.hideFlags != HideFlags.None) return true;
            return false;
        }

        private static void SetPhysicsTransport(HotBakeComponentBase component, bool value)
        {
            FieldInfo field = typeof(HotBakeComponentBase).GetField("physicsTransport", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Assert.That(value, Is.False, "Physics Transport can be enabled only in a UniVRM build.");
                return;
            }
            field.SetValue(component, value);
            Assert.That((bool)field.GetValue(component), Is.EqualTo(value));
        }

#if SHAPESYNC_USE_UNIVRM
        private sealed class RecordingTransporter : IHumanoidVrmPhysicsTransporter
        {
            public TrackingOwnership Ownership { get; } = new TrackingOwnership();
            public int InvocationCount { get; private set; }
            public bool TryTransport(GameObject candidateRoot, GameObject figureSourceRoot, System.Collections.Generic.IReadOnlyList<GameObject> attachedOutfitSourceRoots, out IDisposable ownership, out StackMachineDiagnostic diagnostic)
            {
                InvocationCount++;
                ownership = Ownership;
                diagnostic = null;
                return true;
            }
        }

        private sealed class TrackingOwnership : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() { Disposed = true; }
        }

        private sealed class RecordingSpawnInitializer : IHumanoidVrmPhysicsSpawnInitializer
        {
            public int InvocationCount { get; private set; }
            public bool TryInitializeSpawn(GameObject templateRoot, GameObject spawnRoot, out StackMachineDiagnostic diagnostic)
            {
                InvocationCount++;
                diagnostic = null;
                return true;
            }
        }
#endif

        private sealed class TestComponent : HotBakeComponentBase
        {
            public bool PhysicsEnabled => IsPhysicsTransportEnabled;
            public TextureStackMachineHost EffectiveNormalHost => ResolvedNormalHost;
            public TextureStackMachineHost EffectiveMaterialHost => ResolvedMaterialHost;
            public HumanoidBuildOperationStatus PumpAndCommit(HotBakeArtifactSceneScope scope, out StackMachineDiagnostic diagnostic)
                => PumpAndCommitCompile(scope, out diagnostic);
            public void EmitDiagnostic(StackMachineDiagnostic diagnostic) => SetLastDiagnostic(diagnostic);
            public void InvokeDestroyLifecycle() => OnDestroy();
        }

        private sealed class TestSpawner : HotBakeSpawner
        {
            public void InvokeDestroyLifecycle() => OnDestroy();
        }

        private static AtlasSchema CreateEmptyAtlasSchema()
        {
            var atlas = ScriptableObject.CreateInstance<AtlasSchema>();
            var document = new AtlasSchemaDocument(
                AtlasSchemaVersion.Current,
                512,
                AtlasPackingAlgorithm.FirstFitBuddyV1,
                true,
                new AtlasValidationIdentity("figure", "document"),
                Array.Empty<AtlasSchemaEntry>());
            Assert.That(atlas.TrySetDocument(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return atlas;
        }
    }
}
