// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>Runtime lifecycle evidence for Spec15-1 suspend / resume escrow.</summary>
    public sealed class MaterialSuspendResumeRuntimeTests
    {
        [UnityTest]
        public IEnumerator NormalBlender_MatchingEscrowRestoresRenderTextureAtReactivateFrameZero()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            GameObject root = new GameObject("Spec15_1_NormalEscrowRuntime");
            Material source = new Material(shader);
            MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            RenderTexture texture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf);
            int released = 0;
            try
            {
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = source;
                MaterialProxy proxy = root.AddComponent<MaterialProxy>();
                MaterialProxyEntry entry = new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter };
                SetPrivate(proxy, "entries", new List<MaterialProxyEntry> { entry });
                root.AddComponent<MeshStackMachine>();
                NormalBlender blender = root.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "body" });
                yield return null;
                Assert.That(blender.TryRetry(out StackMachineDiagnostic retryDiagnostic), Is.True, retryDiagnostic?.message);
                IDictionary states = States(blender);
                Assert.That(states.Contains("body"), Is.True, "TryRetry must allocate state for the configured Normal entry.");
                object state = states["body"];
                Assert.That(state, Is.Not.Null);

                texture.Create();
                TextureDelivery delivery = Delivery(texture, () => released++);
                TextureExecutionHandle completed = Successful(delivery);
                SetStateField(state, "inFlight", completed);
                Complete(blender, state, Revision(state), completed);
                Material runtime = (Material)GetEntryField(entry, "runtimeMaterial");
                Assert.That(blender.TryGetEntryDiagnostic("body", out StackMachineDiagnostic completionDiagnostic), Is.False, completionDiagnostic?.message);
                Assert.That(runtime, Is.Not.Null, "The initial Normal delivery must create an entry-owned runtime Material.");
                int bumpMap = Shader.PropertyToID("_BumpMap");
                Assert.That(runtime.GetTexture(bumpMap), Is.SameAs(texture));

                int reactivateFrame = Time.frameCount;
                root.SetActive(false);
                runtime.SetTexture(bumpMap, null);
                root.SetActive(true);

                Assert.That(Time.frameCount, Is.EqualTo(reactivateFrame), "No frame may elapse between deactivate and reactivate observation.");
                Assert.That(runtime.GetTexture(bumpMap), Is.SameAs(texture), "Matching escrow must restore at +0, before the next Update or TSM pump.");
                Assert.That((bool)GetStateField(state, "pending"), Is.False);
                Assert.That(released, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(adapter);
                UnityEngine.Object.Destroy(source);
            }

            yield return null;
            Assert.That(released, Is.EqualTo(1));
            Assert.That(texture == null, Is.True, "Destroy must return the retained Normal RenderTexture to baseline.");
        }

        private static IDictionary States(NormalBlender blender) => (IDictionary)typeof(NormalBlender).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(blender);
        private static ulong Revision(object state) => (ulong)GetStateField(state, "revision");
        private static void SetStateField(object state, string name, object value) => state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(state, value);
        private static object GetEntryField(MaterialProxyEntry entry, string name) => typeof(MaterialProxyEntry).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry);
        private static object GetStateField(object state, string name) => state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(state);
        private static void SetPrivate(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Complete(NormalBlender blender, object state, ulong revision, TextureExecutionHandle handle) => typeof(NormalBlender).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(blender, new object[] { "body", state, revision, handle });
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
        private static TextureDelivery Delivery(Texture texture, Action released)
        {
            ConstructorInfo constructor = typeof(TextureDelivery).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(Action<Texture>) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (TextureDelivery)constructor.Invoke(new object[] { texture, new Action<Texture>(value => { released(); UnityEngine.Object.Destroy(value); }) });
        }
    }
}
