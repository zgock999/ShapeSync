// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class MaterialProxyRuntimeTests
    {
        [UnityTest]
        public IEnumerator Commit_CreatesAnEntryOwnedCloneAndRestoreReturnsTheOriginalMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyRuntimeTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            source.SetColor("_BaseColor", Color.white);
            renderer.sharedMaterial = source;
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            var values = new MaterialProxySemanticValues
            {
                applyColor = true,
                color = new Color(0.1f, 0.2f, 0.3f, 1f),
                applyUvTransform = true,
                uvScale = new Vector2(2f, 3f),
                uvOffset = new Vector2(0.25f, 0.5f)
            };

            Assert.That(proxy.TryCommit("Body", values, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
            Material runtime = renderer.sharedMaterial;
            Assert.That(runtime, Is.Not.SameAs(source));
            Assert.That(runtime.GetColor("_BaseColor").r, Is.EqualTo(values.color.r).Within(0.0001f));
            Assert.That(runtime.GetColor("_BaseColor").g, Is.EqualTo(values.color.g).Within(0.0001f));
            Assert.That(runtime.GetColor("_BaseColor").b, Is.EqualTo(values.color.b).Within(0.0001f));
            Assert.That(runtime.GetColor("_BaseColor").a, Is.EqualTo(values.color.a).Within(0.0001f));
            Assert.That(runtime.GetTextureScale("_BaseMap"), Is.EqualTo(values.uvScale));
            Assert.That(runtime.GetTextureOffset("_BaseMap"), Is.EqualTo(values.uvOffset));
            Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(Color.white));

            Assert.That(proxy.TryRestore("Body", out diagnostic), Is.True, diagnostic.message);
            Assert.That(renderer.sharedMaterial, Is.SameAs(source));

            Object.Destroy(adapter);
            Object.Destroy(source);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator Commit_WarnsForAnUnsupportedSemanticWithoutReplacingTheSourceMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyUnsupportedSemanticTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            renderer.sharedMaterial = source;
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            Assert.That(proxy.TryCommit("Body", new MaterialProxySemanticValues { applyNormalTexture = true }, out MaterialProxyDiagnostic diagnostic), Is.True);
            Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.SemanticUnsupported));
            Assert.That(renderer.sharedMaterial, Is.SameAs(source));

            Object.Destroy(adapter);
            Object.Destroy(source);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator Commit_AppliesSupportedSemanticsWhenAnUnsupportedSemanticIsAlsoRequested()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyUnsupportedSemanticMixedTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            source.SetColor("_BaseColor", Color.white);
            renderer.sharedMaterial = source;
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            var values = new MaterialProxySemanticValues
            {
                applyColor = true,
                color = new Color(0.2f, 0.3f, 0.4f, 1f),
                applyNormalTexture = true
            };
            Assert.That(proxy.TryCommit("Body", values, out MaterialProxyDiagnostic diagnostic), Is.True);
            Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.SemanticUnsupported));
            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source));
            Assert.That(renderer.sharedMaterial.GetColor("_BaseColor").r, Is.EqualTo(values.color.r).Within(0.0001f));
            Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(Color.white));

            Object.Destroy(adapter);
            Object.Destroy(source);
            Object.Destroy(gameObject);
        }

        [UnityTest]
        public IEnumerator DisableEnable_SuspendsAndResumesEveryEntryOwnedClone()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var root = new GameObject("MaterialProxyDisableRestoreTests");
            var firstRenderer = root.AddComponent<SkinnedMeshRenderer>();
            var secondObject = new GameObject("SecondRenderer");
            secondObject.transform.SetParent(root.transform);
            var secondRenderer = secondObject.AddComponent<SkinnedMeshRenderer>();
            var firstSource = new Material(shader);
            var secondSource = new Material(shader);
            firstRenderer.sharedMaterial = firstSource;
            secondRenderer.sharedMaterial = secondSource;
            var proxy = root.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "First", renderer = firstRenderer, materialChannel = 0, adapter = adapter },
                new MaterialProxyEntry { entryName = "Second", renderer = secondRenderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            Assert.That(proxy.TryCommit("First", new MaterialProxySemanticValues { applyColor = true, color = Color.red }, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
            Assert.That(proxy.TryCommit("Second", new MaterialProxySemanticValues { applyColor = true, color = Color.blue }, out diagnostic), Is.True, diagnostic.message);
            Material firstRuntime = firstRenderer.sharedMaterial;
            Material secondRuntime = secondRenderer.sharedMaterial;
            Assert.That(firstRuntime, Is.Not.SameAs(firstSource));
            Assert.That(secondRuntime, Is.Not.SameAs(secondSource));

            proxy.enabled = false;
            Assert.That(firstRenderer.sharedMaterial, Is.SameAs(firstSource));
            Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondSource));
            Assert.That(EntryState<Material>(proxy.Entries[0], "runtimeMaterial"), Is.SameAs(firstRuntime));
            Assert.That(EntryState<Material>(proxy.Entries[1], "runtimeMaterial"), Is.SameAs(secondRuntime));
            Assert.That(EntryState<bool>(proxy.Entries[0], "suspended"), Is.True);
            Assert.That(EntryState<bool>(proxy.Entries[1], "suspended"), Is.True);

            proxy.enabled = true;
            yield return null;
            Assert.That(firstRenderer.sharedMaterial, Is.SameAs(firstRuntime));
            Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondRuntime));
            Assert.That(EntryState<bool>(proxy.Entries[0], "suspended"), Is.False);
            Assert.That(EntryState<bool>(proxy.Entries[1], "suspended"), Is.False);

            Object.Destroy(adapter);
            Object.Destroy(firstSource);
            Object.Destroy(secondSource);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator Destroy_RestoresEntryOwnedClone()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var root = new GameObject("MaterialProxyDestroyRestoreTests");
            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(root.transform);
            var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            renderer.sharedMaterial = source;
            var proxy = root.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            Assert.That(proxy.TryCommit("Body", new MaterialProxySemanticValues { applyColor = true, color = Color.green }, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
            Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source));

            Object.Destroy(proxy);
            yield return null;
            Assert.That(renderer.sharedMaterial, Is.SameAs(source));

            Object.Destroy(adapter);
            Object.Destroy(source);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator Commit_IsLocalToTheTargetEntry()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var root = new GameObject("MaterialProxyEntryLocalityTests");
            var firstObject = new GameObject("FirstRenderer");
            var secondObject = new GameObject("SecondRenderer");
            firstObject.transform.SetParent(root.transform);
            secondObject.transform.SetParent(root.transform);
            var firstRenderer = firstObject.AddComponent<SkinnedMeshRenderer>();
            var secondRenderer = secondObject.AddComponent<SkinnedMeshRenderer>();
            var firstSource = new Material(shader);
            var secondSource = new Material(shader);
            secondSource.SetColor("_BaseColor", Color.white);
            firstRenderer.sharedMaterial = firstSource;
            secondRenderer.sharedMaterial = secondSource;
            var proxy = root.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "First", renderer = firstRenderer, materialChannel = 0, adapter = adapter },
                new MaterialProxyEntry { entryName = "Second", renderer = secondRenderer, materialChannel = 0, adapter = adapter }
            });

            yield return null;
            var values = new MaterialProxySemanticValues { applyColor = true, color = Color.blue };
            Assert.That(proxy.TryCommit("First", values, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
            Assert.That(firstRenderer.sharedMaterial, Is.Not.SameAs(firstSource));
            Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondSource));
            Assert.That(secondRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.white));

            Assert.That(proxy.TryRestore("First", out diagnostic), Is.True, diagnostic.message);
            Assert.That(firstRenderer.sharedMaterial, Is.SameAs(firstSource));
            Assert.That(secondRenderer.sharedMaterial, Is.SameAs(secondSource));

            Object.Destroy(adapter);
            Object.Destroy(firstSource);
            Object.Destroy(secondSource);
            Object.Destroy(root);
        }

        private static void SetEntries(MaterialProxy proxy, List<MaterialProxyEntry> entries)
        {
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, entries);
        }

        private static T EntryState<T>(MaterialProxyEntry entry, string name)
        {
            FieldInfo field = typeof(MaterialProxyEntry).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "MaterialProxyEntry state field was not found: " + name);
            return (T)field.GetValue(entry);
        }
    }
}
