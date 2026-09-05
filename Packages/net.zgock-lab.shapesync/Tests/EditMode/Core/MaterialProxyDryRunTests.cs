// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialProxyDryRunTests
    {
        [Test]
        public void DryRun_ReportsSemanticApplicationsWithoutMutatingThenCommitApplies()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var gameObject = new GameObject("MaterialProxyDryRunTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var baseTexture = new Texture2D(1, 1);
            var normalTexture = new Texture2D(1, 1);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                SetEntries(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter } });
                var values = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = baseTexture, applyNormalTexture = true, normalTexture = normalTexture };

                Assert.That(proxy.TryDryRun("Body", values, out MaterialProxyDryRunPlan plan, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                Assert.That(plan.BaseColorTexture, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                Assert.That(plan.NormalTexture, Is.EqualTo(MaterialProxySemanticApplication.Ignored));
                Assert.That(plan.Diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.SemanticUnsupported));
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));
                Assert.That(proxy.TryCommit(plan, out diagnostic), Is.True, diagnostic.message);
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(baseTexture));
            }
            finally
            {
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(baseTexture); Object.DestroyImmediate(normalTexture); Object.DestroyImmediate(source); Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Commit_RejectsPlanWhenTheEntryAdapterChangedAfterDryRun()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var gameObject = new GameObject("MaterialProxyAdapterRevisionTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var texture = new Texture2D(1, 1);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var originalAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var replacementAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                SetEntries(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = originalAdapter } });
                var values = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = texture };
                Assert.That(proxy.TryDryRun("Body", values, out MaterialProxyDryRunPlan plan, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);

                proxy.Entries[0].adapter = replacementAdapter;
                Assert.That(proxy.TryCommit(plan, out diagnostic), Is.False);
                Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.ProxyNotReady));
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));
                Assert.That(source.GetTexture("_BaseMap"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(originalAdapter); Object.DestroyImmediate(replacementAdapter); Object.DestroyImmediate(texture); Object.DestroyImmediate(source); Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void MeshCommit_RejectsPlanWhenTheEntryAdapterChangedAfterDryRun()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var gameObject = new GameObject("MaterialProxyMeshAdapterRevisionTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var normalTexture = new Texture2D(1, 1);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var originalAdapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var replacementAdapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                SetEntries(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = originalAdapter } });
                Assert.That(proxy.TryDryRunMesh("Body", new MaterialProxyMeshValues { applyNormalTexture = true, normalTexture = normalTexture }, out MaterialProxyMeshDryRunPlan plan, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);

                proxy.Entries[0].adapter = replacementAdapter;
                Assert.That(proxy.TryCommitMesh(plan, out diagnostic), Is.False);
                Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.ProxyNotReady));
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));
                Assert.That(source.GetTexture("_BumpMap"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(originalAdapter); Object.DestroyImmediate(replacementAdapter); Object.DestroyImmediate(normalTexture); Object.DestroyImmediate(source); Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void MeshRoute_RestoresOnlyNormalAndPreservesMaterialRouteProperties()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var gameObject = new GameObject("MaterialProxyMeshRouteTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var baseTexture = new Texture2D(1, 1);
            var normalTexture = new Texture2D(1, 1);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                SetEntries(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter } });
                Assert.That(proxy.TryCommit("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = baseTexture }, out MaterialProxyDiagnostic materialDiagnostic), Is.True, materialDiagnostic.message);
                Assert.That(proxy.TryDryRunMesh("Body", new MaterialProxyMeshValues { applyNormalTexture = true, normalTexture = normalTexture }, out MaterialProxyMeshDryRunPlan meshPlan, out MaterialProxyDiagnostic meshDiagnostic), Is.True, meshDiagnostic.message);
                Assert.That(meshPlan.NormalTexture, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                Assert.That(proxy.TryCommitMesh(meshPlan, out meshDiagnostic), Is.True, meshDiagnostic.message);
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(baseTexture));
                Assert.That(renderer.sharedMaterial.GetTexture("_BumpMap"), Is.SameAs(normalTexture));
                Assert.That(proxy.TryRestoreMesh("Body", out meshDiagnostic), Is.True, meshDiagnostic.message);
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(baseTexture));
                Assert.That(renderer.sharedMaterial.GetTexture("_BumpMap"), Is.SameAs(source.GetTexture("_BumpMap")));
            }
            finally
            {
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(baseTexture); Object.DestroyImmediate(normalTexture); Object.DestroyImmediate(source); Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetEntries(MaterialProxy proxy, List<MaterialProxyEntry> entries)
        {
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, entries);
        }
    }
}
