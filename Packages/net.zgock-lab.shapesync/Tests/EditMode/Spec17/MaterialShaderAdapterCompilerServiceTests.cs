// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialShaderAdapterCompilerServiceTests
    {
        [Test]
        public void CloneAndApplyInMemory_CopiesSourceAndAppliesBaseColorColorAndUvWithoutSourceMutation()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            Material source = new Material(shader) { name = "source" };
            Texture2D originalTexture = new Texture2D(1, 1);
            Texture2D generatedTexture = new Texture2D(1, 1);
            UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                source.SetTexture("_BaseMap", originalTexture);
                source.SetColor("_BaseColor", Color.white);
                Assert.That(adapter.TryCreateInMemoryClone(source, out Material clone, out MaterialProxyDiagnostic cloneDiagnostic), Is.True, cloneDiagnostic.message);
                try
                {
                    var values = new MaterialProxySemanticValues
                    {
                        applyBaseColorTexture = true,
                        baseColorTexture = generatedTexture,
                        applyColor = true,
                        color = new Color(0.2f, 0.3f, 0.4f, 1f),
                        applyUvTransform = true,
                        uvScale = new Vector2(2f, 3f),
                        uvOffset = new Vector2(0.25f, 0.5f)
                    };
                    Assert.That(adapter.TryApplyInMemory(clone, values, out MaterialShaderAdapterApplyResult result, out MaterialProxyDiagnostic applyDiagnostic), Is.True, applyDiagnostic.message);
                    Assert.That(applyDiagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.None));
                    Assert.That(result.BaseColorTexture, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                    Assert.That(result.Color, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                    Assert.That(result.UvTransform, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                    Assert.That(clone.shader, Is.SameAs(source.shader));
                    Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(generatedTexture));
                    Assert.That(Vector4.Distance(clone.GetColor("_BaseColor"), values.color), Is.LessThan(0.0001f));
                    Assert.That(clone.GetTextureScale("_BaseMap"), Is.EqualTo(values.uvScale));
                    Assert.That(clone.GetTextureOffset("_BaseMap"), Is.EqualTo(values.uvOffset));
                    Assert.That(source.GetTexture("_BaseMap"), Is.SameAs(originalTexture));
                    Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(Color.white));
                }
                finally { Object.DestroyImmediate(clone); }
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(generatedTexture); Object.DestroyImmediate(originalTexture); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ApplyInMemory_ReportsUnsupportedNormalAsWarningAndNoOp()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Material source = new Material(shader);
            Texture2D normal = new Texture2D(1, 1);
            UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                Assert.That(adapter.TryCreateInMemoryClone(source, out Material clone, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                try
                {
                    Assert.That(adapter.TryApplyInMemory(clone, new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = normal }, out MaterialShaderAdapterApplyResult result, out diagnostic), Is.True, diagnostic.message);
                    Assert.That(result.NormalTexture, Is.EqualTo(MaterialProxySemanticApplication.Ignored));
                    Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.SemanticUnsupported));
                }
                finally { Object.DestroyImmediate(clone); }
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(normal); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ApplyInMemory_AppliesNormalDirectlyThroughTheLitAdapter()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material source = new Material(shader);
            Texture2D normal = new Texture2D(1, 1);
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            try
            {
                Assert.That(adapter.TryCreateInMemoryClone(source, out Material clone, out MaterialProxyDiagnostic diagnostic), Is.True, diagnostic.message);
                try
                {
                    Assert.That(adapter.TryApplyInMemory(clone, new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = normal }, out MaterialShaderAdapterApplyResult result, out diagnostic), Is.True, diagnostic.message);
                    Assert.That(result.NormalTexture, Is.EqualTo(MaterialProxySemanticApplication.Applied));
                    Assert.That(clone.GetTexture("_BumpMap"), Is.SameAs(normal));
                }
                finally { Object.DestroyImmediate(clone); }
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(normal); Object.DestroyImmediate(source); }
        }

        [Test]
        public void CloneAndApplyInMemory_RejectShaderMismatchAndMissingMappedProperty()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material mismatched = new Material(lit);
            Material source = new Material(unlit);
            UrpUnlitMaterialShaderAdapter unlitAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            MissingPropertyAdapter missingPropertyAdapter = ScriptableObject.CreateInstance<MissingPropertyAdapter>();
            try
            {
                Assert.That(unlitAdapter.TryCreateInMemoryClone(mismatched, out _, out MaterialProxyDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.ShaderMismatch));
                Assert.That(missingPropertyAdapter.TryCreateInMemoryClone(source, out Material clone, out diagnostic), Is.True, diagnostic.message);
                try
                {
                    Assert.That(missingPropertyAdapter.TryApplyInMemory(clone, new MaterialProxySemanticValues { applyColor = true, color = Color.white }, out _, out diagnostic), Is.False);
                    Assert.That(diagnostic.code, Is.EqualTo(MaterialProxyDiagnosticCode.RequiredPropertyMissing));
                }
                finally { Object.DestroyImmediate(clone); }
            }
            finally { Object.DestroyImmediate(missingPropertyAdapter); Object.DestroyImmediate(unlitAdapter); Object.DestroyImmediate(source); Object.DestroyImmediate(mismatched); }
        }

        private sealed class MissingPropertyAdapter : MaterialShaderAdapter
        {
            public override string ExpectedShaderName => "Universal Render Pipeline/Unlit";
            protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination) => destination.Add(new MaterialPropertyBindingTemplate { propertyName = "_ShapeSyncMissing", writeKind = MaterialPropertyWriteKind.Color, valueSource = MaterialPropertyValueSource.Color, required = true });
        }
    }
}
