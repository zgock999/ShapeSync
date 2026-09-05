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
    public sealed class MaterialAttacherTests
    {
        [Test]
        public void RestoresBeforeReleasingCurrentDelivery()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    var values = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture };
                    Assert.That(setup.attacher.TryApply("Body", values, Delivery(texture, () => released++), out _), Is.True);
                    Assert.That(setup.attacher.TryRestore("Body", out MaterialAttacherResult result), Is.True, result.diagnostic.message);
                    Assert.That(released, Is.EqualTo(1)); Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void RejectsBusyAndMismatchedCandidatesWithoutReleasingCurrent()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D current = new Texture2D(1, 1); Texture2D candidate = new Texture2D(1, 1); Texture2D mismatch = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = current }, Delivery(current, () => released++), out _), Is.True);
                    SetBusy(setup.attacher, "Body", true);
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = candidate }, Delivery(candidate, () => released++), out MaterialAttacherResult busy), Is.False);
                    Assert.That(busy.code, Is.EqualTo(MaterialAttacherResultCode.EntryBusy)); Assert.That(released, Is.EqualTo(1));
                    SetBusy(setup.attacher, "Body", false);
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = mismatch }, Delivery(candidate, () => released++), out MaterialAttacherResult rejected), Is.False);
                    Assert.That(rejected.code, Is.EqualTo(MaterialAttacherResultCode.Rejected)); Assert.That(released, Is.EqualTo(2));
                    Invoke(setup.attacher, "OnDisable"); Assert.That(released, Is.EqualTo(2), "Attacher does not own a committed delivery.");
                    Invoke(setup.proxy, "OnDisable"); Assert.That(released, Is.EqualTo(2), "Proxy suspend retains its owned delivery.");
                    Invoke(setup.proxy, "OnDestroy"); Assert.That(released, Is.EqualTo(3), "Proxy destroy releases a suspended owned delivery.");
                }
                finally { Object.DestroyImmediate(current); Object.DestroyImmediate(candidate); Object.DestroyImmediate(mismatch); }
            }
        }

        [Test]
        public void RetainsCurrentDeliveryWhenProxyBecomesInactiveAndReusesItWhenReenabled()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D current = new Texture2D(1, 1); Texture2D candidate = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = current }, Delivery(current, () => released++), out _), Is.True);
                    Material runtime = setup.renderer.sharedMaterial;
                    setup.proxy.enabled = false; Invoke(setup.proxy, "OnDisable"); Invoke(setup.attacher, "Update");
                    Assert.That(released, Is.EqualTo(0)); Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                    Assert.That(setup.entry.runtimeMaterial, Is.SameAs(runtime)); Assert.That(setup.entry.suspended, Is.True);
                    setup.proxy.enabled = true; Invoke(setup.proxy, "OnEnable");
                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(runtime)); Assert.That(setup.entry.suspended, Is.False);
                    Assert.That(released, Is.EqualTo(0));
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = candidate }, Delivery(candidate, () => released++), out MaterialAttacherResult applied), Is.True, applied.diagnostic.message);
                    Assert.That(released, Is.EqualTo(1), "Replacing the resumed delivery releases the prior Proxy-owned delivery.");
                }
                finally { Object.DestroyImmediate(current); Object.DestroyImmediate(candidate); }
            }
        }

        [Test]
        public void TenSuspendResumeCyclesRetainOneRuntimeMaterialAndRenderTextureThenDestroyReturnsToBaseline()
        {
            using (Setup setup = Setup.Create())
            {
                RenderTexture texture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBHalf);
                int released = 0;
                try
                {
                    const string textureName = "Spec15_1_MaterialProxy_BaselineTexture";
                    const string runtimeMaterialName = "Spec15_1_MaterialProxy_BaselineRuntimeMaterial";
                    texture.name = textureName;
                    texture.Create();
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, DestroyingDelivery(texture, () => released++), out MaterialAttacherResult applied), Is.True, applied.diagnostic.message);
                    Material runtime = setup.entry.runtimeMaterial;
                    runtime.name = runtimeMaterialName;
                    Assert.That(LoadedObjectCount<Material>(runtimeMaterialName), Is.EqualTo(1));
                    Assert.That(LoadedObjectCount<RenderTexture>(textureName), Is.EqualTo(1));

                    for (int cycle = 0; cycle < 10; cycle++)
                    {
                        Invoke(setup.proxy, "OnDisable");
                        Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                        Assert.That(setup.entry.runtimeMaterial, Is.SameAs(runtime));
                        Assert.That(setup.entry.baseColorDelivery.Texture, Is.SameAs(texture));
                        Assert.That(released, Is.Zero);

                        Invoke(setup.proxy, "OnEnable");
                        Assert.That(setup.renderer.sharedMaterial, Is.SameAs(runtime));
                        Assert.That(setup.entry.runtimeMaterial, Is.SameAs(runtime));
                        Assert.That(setup.entry.baseColorDelivery.Texture, Is.SameAs(texture));
                        Assert.That(released, Is.Zero);
                    }

                    Invoke(setup.proxy, "OnDestroy");

                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                    Assert.That(setup.entry.runtimeMaterial, Is.Null);
                    Assert.That(setup.entry.baseColorDelivery, Is.Null);
                    Assert.That(released, Is.EqualTo(1));
                    Assert.That(texture == null, Is.True, "Terminal cleanup must return the entry-owned RenderTexture to its baseline.");
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
        public void DestroyWithoutPriorDisableRestoresRendererSlotAndReleasesEntryOwnedResources()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    Material runtime = setup.renderer.sharedMaterial;

                    Invoke(setup.proxy, "OnDestroy");

                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                    Assert.That(runtime, Is.Not.SameAs(setup.renderer.sharedMaterial));
                    Assert.That(setup.entry.runtimeMaterial, Is.Null);
                    Assert.That(setup.entry.baseColorDelivery, Is.Null);
                    Assert.That(released, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void ExplicitRestoreWhileSuspendedReleasesEscrow()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    setup.proxy.enabled = false; Invoke(setup.proxy, "OnDisable");
                    Assert.That(setup.entry.suspended, Is.True);
                    Assert.That(released, Is.EqualTo(0));

                    Assert.That(setup.proxy.TryRestore("Body", out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source));
                    Assert.That(setup.entry.runtimeMaterial, Is.Null);
                    Assert.That(setup.entry.baseColorDelivery, Is.Null);
                    Assert.That(setup.entry.suspended, Is.False);
                    Assert.That(released, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void SuspendStalenessReleasesCurrentDeliveryAndReportsWarning()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    setup.entry.renderer = null;
                    Assert.That(setup.attacher.TryRestore("Body", out MaterialAttacherResult result), Is.False); Assert.That(result.code, Is.EqualTo(MaterialAttacherResultCode.RestoreFailed)); Assert.That(released, Is.EqualTo(0));
                    Invoke(setup.attacher, "OnDisable"); Assert.That(released, Is.EqualTo(0), "Attacher does not own a committed delivery.");
                    Invoke(setup.proxy, "OnDisable"); Assert.That(released, Is.EqualTo(1), "Structural staleness releases an unsafe escrow during suspend.");
                    Assert.That(setup.proxy.LastDiagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.EscrowStale));
                    Invoke(setup.proxy, "OnDestroy"); Assert.That(released, Is.EqualTo(1), "Destroy must not double-dispose an already released escrow.");
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void SuspendWithMissingOriginalMaterialRetainsAssignedRuntimeMaterialUntilDestroy()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    Material runtime = setup.entry.runtimeMaterial;
                    setup.entry.originalMaterial = null;

                    Invoke(setup.proxy, "OnDisable");

                    Assert.That(setup.proxy.LastDiagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.EscrowStale));
                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(runtime), "Suspend must not destroy the renderer-assigned runtime Material when no original Material can restore its slot.");
                    Assert.That(setup.entry.runtimeMaterial, Is.SameAs(runtime));
                    Assert.That(setup.entry.baseColorDelivery, Is.Not.Null);
                    Assert.That(setup.entry.suspended, Is.False);
                    Assert.That(released, Is.EqualTo(0));

                    Invoke(setup.proxy, "OnDestroy");

                    Assert.That(released, Is.EqualTo(1));
                    Assert.That(setup.entry.runtimeMaterial, Is.Null);
                    Assert.That(setup.entry.baseColorDelivery, Is.Null);
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void ResumeWithMissingRendererReleasesEscrowWithoutWritingASlot() => AssertResumeStaleness((setup, _, __) => setup.entry.renderer = null, _ => { });

        [Test]
        public void ResumeWithMissingOriginalMaterialReleasesEscrowWithoutWritingASlot() => AssertResumeStaleness((setup, _, __) => setup.entry.originalMaterial = null, setup => Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source)));

        [Test]
        public void ResumeWithOutOfRangeChannelReleasesEscrowWithoutWritingASlot() => AssertResumeStaleness((setup, _, __) => setup.entry.materialChannel = 1, setup => Assert.That(setup.renderer.sharedMaterial, Is.SameAs(setup.source)));

        [Test]
        public void ResumeWithChangedRendererSlotReleasesEscrowWithoutOverwritingTheSlot() => AssertResumeStaleness((setup, _, __) => setup.renderer.sharedMaterial = null, setup => Assert.That(setup.renderer.sharedMaterial, Is.Null));

        [Test]
        public void ResumeWithDestroyedRuntimeMaterialReleasesEscrow() => AssertResumeStaleness((_, __, runtime) => Object.DestroyImmediate(runtime), _ => { });

        [Test]
        public void ResumeWithDestroyedDeliveryTextureReleasesEscrow() => AssertResumeStaleness((_, texture, __) => Object.DestroyImmediate(texture), _ => { }, 0);

        [Test]
        public void ResumeAllRetainsEarlierEscrowStaleWarningWhenLaterEntryResumes()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var root = new GameObject("MaterialProxyMultiEntryResumeTests");
            var firstRenderer = root.AddComponent<SkinnedMeshRenderer>();
            var secondObject = new GameObject("SecondRenderer");
            secondObject.transform.SetParent(root.transform);
            var secondRenderer = secondObject.AddComponent<SkinnedMeshRenderer>();
            var firstSource = new Material(shader);
            var secondSource = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var proxy = root.AddComponent<MaterialProxy>();
            firstRenderer.sharedMaterial = firstSource;
            secondRenderer.sharedMaterial = secondSource;
            var entries = new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "First", renderer = firstRenderer, materialChannel = 0, adapter = adapter },
                new MaterialProxyEntry { entryName = "Second", renderer = secondRenderer, materialChannel = 0, adapter = adapter }
            };
            SetEntries(proxy, entries);

            try
            {
                Assert.That(proxy.TryCommit("First", new MaterialProxySemanticValues { applyColor = true, color = Color.red }, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                Assert.That(proxy.TryCommit("Second", new MaterialProxySemanticValues { applyColor = true, color = Color.blue }, out diagnostic), Is.True, diagnostic.message);
                Material secondRuntime = secondRenderer.sharedMaterial;
                proxy.enabled = false; Invoke(proxy, "OnDisable");
                entries[0].renderer = null;

                proxy.enabled = true; Invoke(proxy, "OnEnable");

                Assert.That(proxy.LastDiagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.EscrowStale));
                Assert.That(entries[0].runtimeMaterial, Is.Null);
                Assert.That(entries[0].suspended, Is.False);
                Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondRuntime));
                Assert.That(entries[1].suspended, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(firstSource); Object.DestroyImmediate(secondSource); Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RestoreWithMissingOriginalMaterialKeepsProxyOwnedDeliveryUntilDestroy()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    Material runtime = setup.renderer.sharedMaterial;
                    setup.entry.originalMaterial = null;

                    Assert.That(setup.attacher.TryRestore("Body", out MaterialAttacherResult result), Is.False);
                    Assert.That(result.code, Is.EqualTo(MaterialAttacherResultCode.RestoreFailed));
                    Assert.That(result.diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.RestoreFailed));
                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(runtime));
                    Assert.That(released, Is.EqualTo(0));

                    Invoke(setup.proxy, "OnDestroy");
                    Assert.That(released, Is.EqualTo(1), "Destroy must dispose a Proxy-owned delivery that cannot be restored.");
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void RestoreWithOutOfRangeMaterialChannelKeepsProxyOwnedDeliveryUntilDestroy()
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    Material runtime = setup.renderer.sharedMaterial;
                    setup.entry.materialChannel = 1;

                    Assert.That(setup.attacher.TryRestore("Body", out MaterialAttacherResult result), Is.False);
                    Assert.That(result.code, Is.EqualTo(MaterialAttacherResultCode.RestoreFailed));
                    Assert.That(result.diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.RestoreFailed));
                    Assert.That(setup.renderer.sharedMaterial, Is.SameAs(runtime));
                    Assert.That(released, Is.EqualTo(0));

                    Invoke(setup.proxy, "OnDestroy");
                    Assert.That(released, Is.EqualTo(1), "Destroy must dispose a Proxy-owned delivery when the material channel is stale.");
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }

        [Test]
        public void RejectsProxyOnAnotherGameObjectAndReleasesCandidate()
        {
            var attacherObject = new GameObject("Attacher"); var proxyObject = new GameObject("ForeignProxy"); var attacher = attacherObject.AddComponent<MaterialAttacher>(); var proxy = proxyObject.AddComponent<MaterialProxy>(); var texture = new Texture2D(1, 1); int released = 0;
            try
            {
                attacher.Proxy = proxy;
                Assert.That(attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out MaterialAttacherResult result), Is.False);
                Assert.That(result.code, Is.EqualTo(MaterialAttacherResultCode.Rejected)); Assert.That(result.diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.ProxyNotReady)); Assert.That(released, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(proxyObject); Object.DestroyImmediate(attacherObject); }
        }

        [Test]
        public void RetainsCurrentDeliveryWhenAttacherStopsBeingCoLocatedUntilProxyDestroy()
        {
            using (Setup setup = Setup.Create())
            {
                var foreignObject = new GameObject("ForeignProxy"); var foreignProxy = foreignObject.AddComponent<MaterialProxy>(); var texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    typeof(MaterialAttacher).GetField("proxy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(setup.attacher, foreignProxy);
                    Invoke(setup.attacher, "Update"); Assert.That(released, Is.EqualTo(0), "Changing Attacher proxy reference does not dispose the original Proxy-owned delivery.");
                    Invoke(setup.proxy, "OnDisable"); Assert.That(released, Is.EqualTo(0), "Proxy suspend retains its owned delivery even after Attacher co-location changes.");
                    Invoke(setup.proxy, "OnDestroy"); Assert.That(released, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(foreignObject); }
            }
        }

        private static TextureDelivery Delivery(Texture texture, Action released)
        {
            ConstructorInfo constructor = typeof(TextureDelivery).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(Action<Texture>) }, null);
            Assert.That(constructor, Is.Not.Null); return (TextureDelivery)constructor.Invoke(new object[] { texture, new Action<Texture>(_ => released()) });
        }
        private static TextureDelivery DestroyingDelivery(Texture texture, Action released)
        {
            ConstructorInfo constructor = typeof(TextureDelivery).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(Action<Texture>) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (TextureDelivery)constructor.Invoke(new object[] { texture, new Action<Texture>(value => { released(); Object.DestroyImmediate(value); }) });
        }

        private static void SetEntries(MaterialProxy proxy, List<MaterialProxyEntry> entries) => typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, entries);

        private static void AssertResumeStaleness(Action<Setup, Texture2D, Material> makeStale, Action<Setup> assertSlot, int expectedReleaseCallbackCount = 1)
        {
            using (Setup setup = Setup.Create())
            {
                Texture2D texture = new Texture2D(1, 1); int released = 0;
                try
                {
                    Assert.That(setup.attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture }, Delivery(texture, () => released++), out _), Is.True);
                    Material runtime = setup.renderer.sharedMaterial;
                    setup.proxy.enabled = false; Invoke(setup.proxy, "OnDisable");
                    Assert.That(setup.entry.suspended, Is.True);

                    makeStale(setup, texture, runtime);
                    setup.proxy.enabled = true; Invoke(setup.proxy, "OnEnable");

                    Assert.That(setup.proxy.LastDiagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.EscrowStale));
                    Assert.That(setup.entry.runtimeMaterial, Is.Null);
                    Assert.That(setup.entry.originalMaterial, Is.Null);
                    Assert.That(setup.entry.baseColorDelivery, Is.Null);
                    Assert.That(setup.entry.suspended, Is.False);
                    Assert.That(released, Is.EqualTo(expectedReleaseCallbackCount));
                    assertSlot(setup);
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }
        private static void Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic); Assert.That(method, Is.Not.Null); method.Invoke(target, null);
        }
        private static int LoadedObjectCount<T>(string name) where T : Object
        {
            int count = 0;
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>()) if (candidate != null && candidate.name == name) count++;
            return count;
        }
        private static void SetBusy(MaterialAttacher attacher, string entryName, bool busy)
        {
            MethodInfo find = typeof(MaterialAttacher).GetMethod("FindOrCreate", BindingFlags.Instance | BindingFlags.NonPublic); object state = find.Invoke(attacher, new object[] { entryName }); state.GetType().GetField("applying", BindingFlags.Instance | BindingFlags.Public).SetValue(state, busy);
        }

        private sealed class Setup : IDisposable
        {
            internal GameObject gameObject; internal SkinnedMeshRenderer renderer; internal Material source; internal MaterialProxy proxy; internal MaterialAttacher attacher; internal UrpUnlitMaterialShaderAdapter adapter; internal MaterialProxyEntry entry;
            internal static Setup Create()
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); Assert.That(shader, Is.Not.Null);
                var setup = new Setup { gameObject = new GameObject("MaterialAttacherTests") }; setup.renderer = setup.gameObject.AddComponent<SkinnedMeshRenderer>(); setup.source = new Material(shader); setup.proxy = setup.gameObject.AddComponent<MaterialProxy>(); setup.attacher = setup.gameObject.AddComponent<MaterialAttacher>(); setup.adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); setup.entry = new MaterialProxyEntry { entryName = "Body", renderer = setup.renderer, materialChannel = 0, adapter = setup.adapter }; setup.renderer.sharedMaterial = setup.source; typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(setup.proxy, new List<MaterialProxyEntry> { setup.entry }); setup.attacher.Proxy = setup.proxy; return setup;
            }
            public void Dispose() { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(gameObject); }
        }
    }
}
