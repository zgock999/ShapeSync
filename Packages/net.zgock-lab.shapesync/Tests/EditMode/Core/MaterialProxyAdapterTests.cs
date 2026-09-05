// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialProxyAdapterTests
    {
        [Test]
        public void UrpLitAdapter_DeclaresBaseColorNormalColorAndSharedUvSemantics()
        {
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            try
            {
                Assert.That(adapter.Supports(MaterialProxySemantic.BaseColorTexture), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.NormalTexture), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.Color), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.UvTransform), Is.True);
                Assert.That(adapter.AssignmentTemplates, Has.Count.EqualTo(5));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
            }
        }

        [Test]
        public void UrpUnlitAdapter_RejectsNormalSemanticByCapability()
        {
            UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                Assert.That(adapter.Supports(MaterialProxySemantic.NormalTexture), Is.False);
                Assert.That(adapter.AssignmentTemplates, Has.Count.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
            }
        }

        [Test]
        public void MToon10Adapter_IsAvailableWithoutUniVrmIntegrationTypes()
        {
            MToon10MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
            try
            {
                Assert.That(adapter.ExpectedShaderName, Is.EqualTo("VRM10/Universal Render Pipeline/MToon10"));
                Assert.That(adapter.Supports(MaterialProxySemantic.BaseColorTexture), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.NormalTexture), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.Color), Is.True);
                Assert.That(adapter.Supports(MaterialProxySemantic.UvTransform), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(adapter);
            }
        }

        [Test]
        public void MToon10Adapter_BaseColorWritePlanAssignsTheSameTextureToLitAndShade()
        {
            MToon10MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
            Texture2D texture = new Texture2D(1, 1);
            var assignments = new List<MaterialPropertyAssignment>();
            try
            {
                var values = new MaterialProxySemanticValues
                {
                    applyBaseColorTexture = true,
                    baseColorTexture = texture
                };

                Assert.That(adapter.TryBuildWritePlan(values, assignments, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);

                MaterialPropertyAssignment lit = assignments.Find(assignment => assignment.PropertyId == Shader.PropertyToID("_MainTex") && assignment.WriteKind == MaterialPropertyWriteKind.Texture);
                MaterialPropertyAssignment shade = assignments.Find(assignment => assignment.PropertyId == Shader.PropertyToID("_ShadeTex") && assignment.WriteKind == MaterialPropertyWriteKind.Texture);
                Assert.That(lit.Texture, Is.SameAs(texture));
                Assert.That(shade.Texture, Is.SameAs(texture));
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(adapter);
            }
        }

        [Test]
        public void MToon10Adapter_PublishTexturePropertiesIncludeLitAndShade()
        {
            MToon10MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<MToon10MaterialShaderAdapter>();
            var properties = new List<string>();
            try
            {
                Assert.That(adapter.TryGetPublishTextureProperties(MaterialProxySemantic.BaseColorTexture, properties, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                Assert.That(properties, Is.EqualTo(new[] { "_MainTex", "_ShadeTex" }));
            }
            finally { Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void ReadCurrentMaterial_CopiesPublicMaterialValuesWithoutReplacingTheSourceMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyAdapterTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            renderer.sharedMaterial = source;
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();

            try
            {
                source.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.4f, 1f));
                source.SetTextureScale("_BaseMap", new Vector2(2f, 3f));
                source.SetTextureOffset("_BaseMap", new Vector2(0.25f, 0.5f));
                SetEntries(proxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
                });

                Assert.That(proxy.TryReadCurrentMaterial("Body", out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                MaterialProxySemanticValues read = proxy.Entries[0].configuredValues;
                Assert.That(read.applyColor, Is.True);
                Assert.That(read.color, Is.EqualTo(source.GetColor("_BaseColor")));
                Assert.That(read.applyUvTransform, Is.True);
                Assert.That(read.uvScale, Is.EqualTo(new Vector2(2f, 3f)));
                Assert.That(read.uvOffset, Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GetCurrentBaseColorTexture_IsReadOnlyAndRejectsMissingTexture()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyCurrentTextureTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var texture = new Texture2D(1, 1);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                source.SetTexture("_BaseMap", texture);
                SetEntries(proxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
                });

                Assert.That(proxy.TryGetCurrentBaseColorTexture("Body", out Texture current, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                Assert.That(current, Is.SameAs(texture));
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));

                source.SetTexture("_BaseMap", null);
                Assert.That(proxy.TryGetCurrentBaseColorTexture("Body", out current, out diagnostic), Is.False);
                Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.CurrentTextureUnavailable));
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Commit_IsAvailableInEditModeAndUsesAnEntryOwnedClone()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            var gameObject = new GameObject("MaterialProxyEditCommitTests");
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            var source = new Material(shader);
            var proxy = gameObject.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            renderer.sharedMaterial = source;

            try
            {
                SetEntries(proxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter }
                });
                var values = new MaterialProxySemanticValues
                {
                    applyUvTransform = true,
                    uvScale = new Vector2(2f, 3f),
                    uvOffset = new Vector2(0.25f, 0.5f)
                };

                Assert.That(proxy.TryCommit("Body", values, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source));
                Assert.That(renderer.sharedMaterial.GetTextureOffset("_BaseMap"), Is.EqualTo(values.uvOffset));
                Assert.That(source.GetTextureOffset("_BaseMap"), Is.EqualTo(Vector2.zero));
                Assert.That(proxy.TryRestore("Body", out diagnostic), Is.True, diagnostic.message);
                Assert.That(renderer.sharedMaterial, Is.SameAs(source));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EntryPopulation_SelectedAdapterInitializesReadableSlotsAndLeavesUnreadableSlotsBlank()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(unlitShader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            Assert.That(litShader, Is.Not.Null, "The project must provide the Phase0 URP Lit shader.");
            var root = new GameObject("MaterialProxyEntryPopulationTests");
            var readableRenderer = root.AddComponent<SkinnedMeshRenderer>();
            var child = new GameObject("Unreadable");
            child.transform.SetParent(root.transform);
            var unreadableRenderer = child.AddComponent<SkinnedMeshRenderer>();
            var readableMaterial = new Material(unlitShader);
            var unreadableMaterial = new Material(litShader);
            var proxy = root.AddComponent<MaterialProxy>();
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            readableRenderer.sharedMaterial = readableMaterial;
            unreadableRenderer.sharedMaterial = unreadableMaterial;

            try
            {
                readableMaterial.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.4f, 1f));
                List<MaterialProxyEntry> entries = PopulateEntries(proxy, adapter, out int configuredCount);

                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(configuredCount, Is.EqualTo(1));
                MaterialProxyEntry readableEntry = entries.Find(entry => entry.renderer == readableRenderer);
                MaterialProxyEntry unreadableEntry = entries.Find(entry => entry.renderer == unreadableRenderer);
                Assert.That(readableEntry.materialChannel, Is.EqualTo(0));
                Assert.That(readableEntry.adapter, Is.SameAs(adapter));
                Assert.That(readableEntry.configuredValues.applyColor, Is.True);
                Assert.That(readableEntry.configuredValues.color, Is.EqualTo(readableMaterial.GetColor("_BaseColor")));
                Assert.That(unreadableEntry.materialChannel, Is.EqualTo(0));
                Assert.That(unreadableEntry.adapter, Is.Null);
                Assert.That(unreadableEntry.configuredValues.applyColor, Is.False);
                Assert.That(readableRenderer.sharedMaterial, Is.SameAs(readableMaterial));
                Assert.That(unreadableRenderer.sharedMaterial, Is.SameAs(unreadableMaterial));
            }
            finally
            {
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(readableMaterial);
                Object.DestroyImmediate(unreadableMaterial);
                Object.DestroyImmediate(root);
            }
        }

        private static void SetEntries(MaterialProxy proxy, List<MaterialProxyEntry> entries)
        {
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, entries);
        }

        private static List<MaterialProxyEntry> PopulateEntries(MaterialProxy proxy, MaterialShaderAdapter adapter, out int configuredCount)
        {
            Type editorType = GetMaterialProxyEditorType();
            MethodInfo method = editorType.GetMethod("TryBuildChildRendererEntries", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Entry population builder was not found.");
            object[] arguments = { proxy, adapter, null, 0 };
            Assert.That((bool)method.Invoke(null, arguments), Is.True);
            configuredCount = (int)arguments[3];
            return (List<MaterialProxyEntry>)arguments[2];
        }

        private static Type GetMaterialProxyEditorType()
        {
            Assembly editorAssembly = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "zgock.ShapeSync.Editor") { editorAssembly = assembly; break; }
            }

            Assert.That(editorAssembly, Is.Not.Null, "The ShapeSync Editor assembly must be loaded for this WhiteBox test.");
            Type editorType = editorAssembly.GetType("zgock.ShapeSync.Editor.Materials.MaterialProxyEditor");
            Assert.That(editorType, Is.Not.Null, "Material Proxy Inspector type was not found.");
            return editorType;
        }
    }
}
