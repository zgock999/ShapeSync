// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class NormalBlenderTests
    {
        [Test]
        public void NormalBlender_EntriesAreOnlyLogicalNames()
        {
            Assert.That(typeof(NormalBlender).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).FieldType, Is.EqualTo(typeof(List<string>)));
            Assert.That(typeof(NormalBlender).Assembly.GetType("zgock.ShapeSync.Materials.NormalBinding"), Is.Null);
        }

        [Test]
        public void NormalBlender_EmitsOnlyChangedNonPbmSnapshot()
        {
            var root = new GameObject("NormalBlender");
            try
            {
                NormalBlender blender = root.AddComponent<NormalBlender>();
                int published = 0;
                blender.SnapshotChanged += _ => published++;
                Invoke(blender, "OnDdbSnapshotChanged", new[] { new FbmWeightChange("body", .25f, true), new FbmWeightChange("PBM_smile", .5f, true) });
                Assert.That(blender.LatestSnapshot.Count, Is.EqualTo(1));
                Assert.That(blender.LatestSnapshot[0].TargetName, Is.EqualTo("body"));
                Assert.That(published, Is.EqualTo(1));
                Invoke(blender, "OnDdbSnapshotChanged", new[] { new FbmWeightChange("body", .25f, true), new FbmWeightChange("PBM_smile", .75f, true) });
                Assert.That(published, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NormalBlender_RejectsDuplicateOrEmptyEntriesBeforeMachineSubmission()
        {
            var root = new GameObject("NormalBlender");
            try
            {
                root.AddComponent<MaterialProxy>();
                root.AddComponent<MeshStackMachine>();
                NormalBlender blender = root.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "face", "face" });
                InvokeNoArgument(blender, "OnEnable");
                Assert.That(blender.TryRetry(out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("NormalEntryInvalid"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NormalBlender_DefersOriginIssuanceUntilAHostIsResolved()
        {
            var root = new GameObject("NormalBlender");
            try
            {
                root.AddComponent<MaterialProxy>();
                root.AddComponent<MeshStackMachine>();
                NormalBlender blender = root.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "face", "body" });
                InvokeNoArgument(blender, "OnEnable");
                object rawStates = typeof(NormalBlender).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blender);
                var dictionary = (System.Collections.IDictionary)rawStates;
                object face = dictionary["face"]; object body = dictionary["body"];
                var originField = face.GetType().GetField("origin");
                TextureExecutionOriginKey faceOrigin = (TextureExecutionOriginKey)originField.GetValue(face);
                TextureExecutionOriginKey bodyOrigin = (TextureExecutionOriginKey)originField.GetValue(body);
                Assert.That(faceOrigin.IsValid, Is.False, "NormalBlender must obtain origins from the resolved Texture host, not construct caller-owned values during enable.");
                Assert.That(bodyOrigin.IsValid, Is.False);
                Assert.That((bool)face.GetType().GetField("pending").GetValue(face), Is.True, "Without a DDB, enable must still derive the base Normal from the empty snapshot.");
                Assert.That((bool)body.GetType().GetField("pending").GetValue(body), Is.True, "Without a DDB, enable must still derive the base Normal from the empty snapshot.");
                Assert.That(blender.TryRetry(out _), Is.True);
                Assert.That((bool)face.GetType().GetField("pending").GetValue(face), Is.True);
                Assert.That((bool)body.GetType().GetField("pending").GetValue(body), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NormalBlender_ReplacesAcceptedDeliveryAndDisposesRejectedOrStaleCandidates()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int firstReleased = 0;
                TextureDelivery first = Delivery(() => firstReleased++);
                CompleteCurrent(fixture, Successful(first));
                Assert.That(CurrentDelivery(fixture), Is.SameAs(first));

                int replacementReleased = 0;
                TextureDelivery replacement = Delivery(() => replacementReleased++);
                CompleteCurrent(fixture, Successful(replacement));
                Assert.That(firstReleased, Is.EqualTo(1));
                Assert.That(replacementReleased, Is.EqualTo(0));
                Assert.That(CurrentDelivery(fixture), Is.SameAs(replacement));

                int rejectedReleased = 0;
                fixture.Entry.adapter = null;
                CompleteCurrent(fixture, Successful(Delivery(() => rejectedReleased++)));
                Assert.That(rejectedReleased, Is.EqualTo(1));
                Assert.That(CurrentDelivery(fixture), Is.SameAs(replacement));

                int staleReleased = 0;
                TextureExecutionHandle stale = Successful(Delivery(() => staleReleased++));
                InvokeComplete(fixture.Blender, "body", fixture.State, fixture.Revision - 1, stale);
                Assert.That(staleReleased, Is.EqualTo(1));
                Assert.That(CurrentDelivery(fixture), Is.SameAs(replacement));
            }
        }

        [Test]
        public void NormalBlender_CommitRejectDisposesCandidateAndPreservesCurrentDelivery()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int currentReleased = 0;
                TextureDelivery current = Delivery(() => currentReleased++);
                CompleteCurrent(fixture, Successful(current));
                var rejecting = ScriptableObject.CreateInstance<CommitRejectingNormalAdapter>();
                rejecting.EntryToInvalidate = fixture.Entry;
                fixture.Entry.adapter = rejecting;
                int candidateReleased = 0;
                try
                {
                    CompleteCurrent(fixture, Successful(Delivery(() => candidateReleased++)));
                    Assert.That(candidateReleased, Is.EqualTo(1));
                    Assert.That(currentReleased, Is.EqualTo(0));
                    Assert.That(CurrentDelivery(fixture), Is.SameAs(current));
                }
                finally { Object.DestroyImmediate(rejecting); }
            }
        }

        [Test]
        public void NormalBlender_SuspendResumeWithMatchingSnapshotReusesDeliveryWithoutQueue()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int released = 0;
                TextureDelivery delivery = Delivery(() => released++);
                CompleteCurrent(fixture, Successful(delivery));

                MaterialProxy proxy = fixture.Blender.GetComponent<MaterialProxy>();
                var renderer = fixture.Entry.renderer as SkinnedMeshRenderer;
                Material runtimeMaterial = fixture.Entry.runtimeMaterial;
                int bumpMap = Shader.PropertyToID("_BumpMap");
                Assert.That(proxy, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(runtimeMaterial, Is.Not.Null);
                Assert.That(renderer.sharedMaterial, Is.SameAs(runtimeMaterial));
                Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.SameAs(delivery.Texture));

                InvokeNoArgument(proxy, "OnDisable");
                Assert.That(renderer.sharedMaterial, Is.SameAs(fixture.Entry.originalMaterial));
                runtimeMaterial.SetTexture(bumpMap, null);
                InvokeNoArgument(fixture.Blender, "OnDisable");
                Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.True);

                InvokeNoArgument(proxy, "OnEnable");
                Assert.That(renderer.sharedMaterial, Is.SameAs(runtimeMaterial));
                Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.Null);
                InvokeNoArgument(fixture.Blender, "OnEnable");

                Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                Assert.That(released, Is.EqualTo(0));
                Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.False);
                Assert.That((bool)GetStateField(fixture.State, "pending"), Is.False);
                Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.SameAs(delivery.Texture));
            }
        }

        [Test]
        public void NormalBlender_TenMatchingEscrowCyclesRestoreRenderTextureBeforeUpdateThenDestroyReturnsToBaseline()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                RenderTexture texture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf);
                int released = 0;
                try
                {
                    const string textureName = "Spec15_1_NormalBlender_BaselineTexture";
                    const string runtimeMaterialName = "Spec15_1_NormalBlender_BaselineRuntimeMaterial";
                    texture.name = textureName;
                    texture.Create();
                    TextureDelivery delivery = Delivery(texture, () => released++);
                    CompleteCurrent(fixture, Successful(delivery));

                    MaterialProxy proxy = fixture.Blender.GetComponent<MaterialProxy>();
                    Material runtimeMaterial = fixture.Entry.runtimeMaterial;
                    runtimeMaterial.name = runtimeMaterialName;
                    int bumpMap = Shader.PropertyToID("_BumpMap");
                    Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.SameAs(texture));
                    Assert.That(LoadedObjectCount<Material>(runtimeMaterialName), Is.EqualTo(1));
                    Assert.That(LoadedObjectCount<RenderTexture>(textureName), Is.EqualTo(1));

                    for (int cycle = 0; cycle < 10; cycle++)
                    {
                        InvokeNoArgument(fixture.Blender, "OnDisable");
                        InvokeNoArgument(proxy, "OnDisable");
                        runtimeMaterial.SetTexture(bumpMap, null);

                        InvokeNoArgument(proxy, "OnEnable");
                        InvokeNoArgument(fixture.Blender, "OnEnable");

                        Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.SameAs(texture), "Matching escrow must restore at +0: OnEnable returns before any Update or TSM pump.");
                        Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                        Assert.That((bool)GetStateField(fixture.State, "pending"), Is.False);
                        Assert.That(released, Is.Zero);
                    }

                    InvokeNoArgument(fixture.Blender, "OnDestroy");
                    InvokeNoArgument(proxy, "OnDestroy");

                    Assert.That(CurrentDelivery(fixture), Is.Null);
                    Assert.That(fixture.Entry.runtimeMaterial, Is.Null);
                    Assert.That(released, Is.EqualTo(1));
                    Assert.That(texture == null, Is.True, "Terminal cleanup must return the Normal RenderTexture to its baseline.");
                    Assert.That(LoadedObjectCount<Material>(runtimeMaterialName), Is.Zero);
                    Assert.That(LoadedObjectCount<RenderTexture>(textureName), Is.Zero);
                }
                finally
                {
                    if (texture != null) Object.DestroyImmediate(texture);
                }
            }
        }

        [Test]
        public void NormalBlender_ResumeBeforeProxyRestoresTheSameMatchingEscrow()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int released = 0;
                TextureDelivery delivery = Delivery(() => released++);
                CompleteCurrent(fixture, Successful(delivery));

                MaterialProxy proxy = fixture.Blender.GetComponent<MaterialProxy>();
                var renderer = fixture.Entry.renderer as SkinnedMeshRenderer;
                Material runtimeMaterial = fixture.Entry.runtimeMaterial;
                int bumpMap = Shader.PropertyToID("_BumpMap");
                Assert.That(proxy, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(runtimeMaterial, Is.Not.Null);

                InvokeNoArgument(fixture.Blender, "OnDisable");
                InvokeNoArgument(proxy, "OnDisable");
                Assert.That(renderer.sharedMaterial, Is.SameAs(fixture.Entry.originalMaterial));
                runtimeMaterial.SetTexture(bumpMap, null);

                InvokeNoArgument(fixture.Blender, "OnEnable");
                Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                Assert.That(runtimeMaterial.GetTexture(bumpMap), Is.SameAs(delivery.Texture));
                Assert.That((bool)GetStateField(fixture.State, "pending"), Is.False);
                InvokeNoArgument(proxy, "OnEnable");

                Assert.That(renderer.sharedMaterial, Is.SameAs(runtimeMaterial));
                Assert.That(released, Is.EqualTo(0));
                Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.False);
                Assert.That(fixture.Blender.TryGetEntryDiagnostic("body", out _), Is.False);
            }
        }

        [Test]
        public void NormalBlender_SuspendResumeWithChangedSnapshotReleasesDeliveryAndQueues()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int released = 0;
                TextureDelivery delivery = Delivery(() => released++);
                CompleteCurrent(fixture, Successful(delivery));
                SetPrivate(fixture.Blender, "latestSnapshot", new[] { new NormalTargetWeight("body", .25f, true) });

                InvokeNoArgument(fixture.Blender, "OnDisable");
                InvokeNoArgument(fixture.Blender, "OnEnable");

                Assert.That(CurrentDelivery(fixture), Is.Null);
                Assert.That(released, Is.EqualTo(1));
                Assert.That((bool)GetStateField(fixture.State, "pending"), Is.True);
                Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.False);
                Assert.That(GetStateField(fixture.State, "diagnostic"), Is.Null);
                Assert.That(fixture.Blender.TryGetEntryDiagnostic("body", out _), Is.False);
            }
        }

        [Test]
        public void NormalBlender_SuspendResumeWithDestroyedDeliveryReportsEscrowStaleAndQueues()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                TextureDelivery delivery = Delivery(() => { });
                CompleteCurrent(fixture, Successful(delivery));

                InvokeNoArgument(fixture.Blender, "OnDisable");
                Object.DestroyImmediate(delivery.Texture);
                InvokeNoArgument(fixture.Blender, "OnEnable");

                AssertEscrowStaleAndQueued(fixture, "deliveryTexture");
            }
        }

        [Test]
        public void NormalBlender_SuspendResumeWithoutProxyReportsEscrowStaleAndQueues()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                TextureDelivery delivery = Delivery(() => { });
                CompleteCurrent(fixture, Successful(delivery));

                InvokeNoArgument(fixture.Blender, "OnDisable");
                Object.DestroyImmediate(fixture.Blender.GetComponent<MaterialProxy>());
                InvokeNoArgument(fixture.Blender, "OnEnable");

                AssertEscrowStaleAndQueued(fixture, "materialProxy");
            }
        }

        [Test]
        public void NormalBlender_DisableStopsPendingWorkAndRetainsDelivery()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int released = 0;
                TextureDelivery delivery = Delivery(() => released++);
                CompleteCurrent(fixture, Successful(delivery));
                SetStateField(fixture.State, "pending", true);
                SetStateField(fixture.State, "inFlight", new TextureExecutionHandle());

                fixture.Blender.enabled = false;
                InvokeNoArgument(fixture.Blender, "OnDisable");
                InvokeNoArgument(fixture.Blender, "Update");

                Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                Assert.That(released, Is.EqualTo(0));
                Assert.That((bool)GetStateField(fixture.State, "pending"), Is.False);
                Assert.That(GetStateField(fixture.State, "inFlight"), Is.Null);
                Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.True);
            }
        }

        [Test]
        public void NormalBlender_InactiveDdbSnapshotChangeDefersQueueUntilEnable()
        {
            var root = new GameObject("NormalBlenderDdbSubscriptionTests");
            try
            {
                DynamicBoneBlender ddb = root.AddComponent<DynamicBoneBlender>();
                ddb.ConfigureForFigure(null, null, null, null, new[] { new DynamicBoneBlendTarget { blendName = "body", enabled = true, weight = .25f } });
                InvokeNoArgument(ddb, "Start");
                root.AddComponent<MaterialProxy>();
                root.AddComponent<MeshStackMachine>();
                NormalBlender blender = root.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "body" });
                SetPrivate(blender, "dynamicBoneBlender", ddb);
                InvokeNoArgument(blender, "OnEnable");
                Assert.That(blender.TryRetry(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                var states = (System.Collections.IDictionary)typeof(NormalBlender).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blender);
                object state = states["body"];
                SetStateField(state, "pending", false);
                ulong beforeDisableRevision = Revision(state);
                int snapshotsWhileActive = 0;
                blender.SnapshotChanged += _ => snapshotsWhileActive++;

                InvokeNoArgument(blender, "OnDisable");
                ddb.Targets[0].weight = .75f;
                InvokeNoArgument(ddb, "LateUpdate");

                Assert.That(snapshotsWhileActive, Is.EqualTo(0), "Inactive NormalBlender must not observe a DDB snapshot mutation.");
                Assert.That((bool)GetStateField(state, "pending"), Is.False);
                Assert.That((bool)GetStateField(state, "suspended"), Is.True);
                Assert.That(Revision(state), Is.EqualTo(beforeDisableRevision + 1));
                Assert.That(blender.LatestSnapshot[0].Weight, Is.EqualTo(.25f));

                InvokeNoArgument(blender, "OnEnable");

                Assert.That(snapshotsWhileActive, Is.EqualTo(1), "Re-enable preserves the public SnapshotChanged publication without re-enabling inactive DDB observation.");
                Assert.That((bool)GetStateField(state, "pending"), Is.True, "The current DDB snapshot is evaluated only on enable and queues the normal re-derivation.");
                Assert.That((bool)GetStateField(state, "suspended"), Is.False);
                Assert.That(Revision(state), Is.EqualTo(beforeDisableRevision + 2));
                Assert.That(blender.LatestSnapshot[0].Weight, Is.EqualTo(.75f));
                Assert.That(blender.TryGetEntryDiagnostic("body", out _), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NormalBlender_DestroyAfterSuspendDisposesRetainedDeliveryExactlyOnce()
        {
            using (NormalFixture fixture = NormalFixture.Create())
            {
                int released = 0;
                TextureDelivery delivery = Delivery(() => released++);
                CompleteCurrent(fixture, Successful(delivery));

                InvokeNoArgument(fixture.Blender, "OnDisable");
                Assert.That(CurrentDelivery(fixture), Is.SameAs(delivery));
                InvokeNoArgument(fixture.Blender, "OnDestroy");

                Assert.That(released, Is.EqualTo(1));
                Assert.That(CurrentDelivery(fixture), Is.Null);
                var states = (System.Collections.IDictionary)typeof(NormalBlender).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Blender);
                Assert.That(states.Count, Is.Zero);
                InvokeNoArgument(fixture.Blender, "OnDestroy");
                Assert.That(released, Is.EqualTo(1));
            }
        }

        private static void Invoke(object target, string method, object argument) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new[] { argument });
        private static void InvokeNoArgument(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        private static void SetPrivate(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void CompleteCurrent(NormalFixture fixture, TextureExecutionHandle handle)
        {
            SetStateField(fixture.State, "inFlight", handle);
            InvokeComplete(fixture.Blender, "body", fixture.State, fixture.Revision, handle);
        }

        private static void InvokeComplete(NormalBlender blender, string entryName, object state, ulong revision, TextureExecutionHandle handle)
        {
            typeof(NormalBlender).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(blender, new object[] { entryName, state, revision, handle });
        }

        private static TextureDelivery CurrentDelivery(NormalFixture fixture) => (TextureDelivery)fixture.State.GetType().GetField("delivery", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(fixture.State);
        private static void AssertEscrowStaleAndQueued(NormalFixture fixture, string detail)
        {
            Assert.That(CurrentDelivery(fixture), Is.Null);
            Assert.That((bool)GetStateField(fixture.State, "pending"), Is.True);
            Assert.That((bool)GetStateField(fixture.State, "suspended"), Is.False);
            Assert.That(fixture.Blender.TryGetEntryDiagnostic("body", out StackMachineDiagnostic diagnostic), Is.True);
            Assert.That(diagnostic.domainCode, Is.EqualTo("NormalEscrowStale"));
            Assert.That(diagnostic.detail, Is.EqualTo(detail));
        }
        private static ulong Revision(object state) => (ulong)state.GetType().GetField("revision", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(state);
        private static void SetStateField(object state, string name, object value) => state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(state, value);
        private static object GetStateField(object state, string name) => state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(state);
        private static int LoadedObjectCount<T>(string name) where T : Object
        {
            int count = 0;
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>()) if (candidate != null && candidate.name == name) count++;
            return count;
        }

        private static TextureExecutionHandle Successful(TextureDelivery delivery)
        {
            var handle = new TextureExecutionHandle();
            MethodInfo completeGpuFence = typeof(TextureExecutionHandle).GetMethod(
                "CompleteGpuFence",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(TextureDelivery), typeof(TextureSourceLease), typeof(TextureOutputLease) },
                null);
            Assert.That(completeGpuFence, Is.Not.Null);
            completeGpuFence.Invoke(handle, new object[] { delivery, null, null });
            return handle;
        }

        private static TextureDelivery Delivery(Action released)
        {
            var texture = new Texture2D(1, 1);
            return Delivery(texture, released);
        }

        private static TextureDelivery Delivery(Texture texture, Action released)
        {
            ConstructorInfo constructor = typeof(TextureDelivery).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(Action<Texture>) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (TextureDelivery)constructor.Invoke(new object[] { texture, new Action<Texture>(value => { released(); Object.DestroyImmediate(value); }) });
        }

        private sealed class NormalFixture : IDisposable
        {
            private readonly GameObject root;
            private readonly Material source;
            private readonly MaterialShaderAdapter adapter;
            public NormalBlender Blender { get; private set; }
            public MaterialProxyEntry Entry { get; private set; }
            public object State { get; private set; }
            public ulong Revision => NormalBlenderTests.Revision(State);

            private NormalFixture(GameObject value, Material material, MaterialShaderAdapter shaderAdapter, NormalBlender blender, MaterialProxyEntry entry, object state)
            {
                root = value; source = material; adapter = shaderAdapter; Blender = blender; Entry = entry; State = state;
            }

            public static NormalFixture Create()
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.That(shader, Is.Not.Null);
                var root = new GameObject("NormalBlenderLifecycleTests");
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                var source = new Material(shader);
                var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                renderer.sharedMaterial = source;
                MaterialProxy proxy = root.AddComponent<MaterialProxy>();
                var entry = new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter };
                SetPrivate(proxy, "entries", new List<MaterialProxyEntry> { entry });
                root.AddComponent<MeshStackMachine>();
                NormalBlender blender = root.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "body" });
                InvokeNoArgument(blender, "OnEnable");
                Assert.That(blender.TryRetry(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                var states = (System.Collections.IDictionary)typeof(NormalBlender).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blender);
                return new NormalFixture(root, source, adapter, blender, entry, states["body"]);
            }

            public void Dispose()
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(source);
            }
        }

        private sealed class CommitRejectingNormalAdapter : MaterialShaderAdapter
        {
            public MaterialProxyEntry EntryToInvalidate { get; set; }
            public override string ExpectedShaderName => "Universal Render Pipeline/Lit";
            protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination) => AddTextureAndTransform(destination, "_BumpMap", MaterialPropertyValueSource.NormalTexture);
            protected override bool TryAppendComputedAssignments(MaterialProxySemanticValues values, List<MaterialPropertyAssignment> destination, out MaterialProxyDiagnostic diagnostic)
            {
                EntryToInvalidate.adapter = null;
                diagnostic = default;
                return true;
            }
        }
    }
}
