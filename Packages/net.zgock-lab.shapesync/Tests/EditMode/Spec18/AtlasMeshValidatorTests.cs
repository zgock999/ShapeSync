// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasMeshValidatorTests
    {
        [Test]
        public void Validate_RejectsSharedVerticesUvAndMissingSubmesh()
        {
            Mesh mesh = CreateMesh();
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic shared), Is.False);
            Assert.That(shared.domainCode, Is.EqualTo("AtlasSharedVertex"));
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 2 }, null, out StackMachineDiagnostic missing), Is.False);
            Assert.That(missing.domainCode, Is.EqualTo("AtlasSubmeshMissing"));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_AcceptsDisjointMeshAndRejectsInvalidTextureExtent()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic valid), Is.True, valid?.message);
            Texture2D invalid = new Texture2D(64, 64);
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, new Texture[] { invalid }, out StackMachineDiagnostic extent), Is.False);
            Assert.That(extent.domainCode, Is.EqualTo("AtlasSourceExtentUnsupported"));
            Object.DestroyImmediate(invalid); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_RejectsUvRangeAndNonIdentityTransform()
        {
            Mesh mesh = CreateMesh();
            mesh.uv = new[] { new Vector2(-0.1f, 0f), Vector2.right, Vector2.up, Vector2.one };
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic uv), Is.False);
            Assert.That(uv.domainCode, Is.EqualTo("AtlasUv0OutOfRange"));
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            material.SetTextureScale("_BaseMap", new Vector2(2f, 1f));
            Assert.That(AtlasMeshValidator.TryValidateMainTextureTransform(material, adapter, out StackMachineDiagnostic st), Is.False);
            Assert.That(st.domainCode, Is.EqualTo("AtlasMainTextureTransformUnsupported"));
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_RejectsDegenerateUvTriangle()
        {
            Mesh mesh = CreateMesh();
            mesh.uv = new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one };
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasUv0Degenerate"));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_RequiresBaseColorAndNormalTextures_AndUsesAdapterContentForNeutrality()
        {
            Texture2D texture = new Texture2D(128, 128);
            Assert.That(AtlasMeshValidator.TryValidateSemantics(texture, null, out StackMachineDiagnostic normal), Is.False);
            Assert.That(normal.domainCode, Is.EqualTo("AtlasNormalRequired"));
            Assert.That(AtlasMeshValidator.TryValidateSemantics(texture, texture, out StackMachineDiagnostic valid), Is.True, valid?.message);
            Texture2D neutral = Solid(8, 8, new Color(.5f, .5f, 1f, 1f)); neutral.name = "DatabaseRenamedNormal";
            Texture2D neutralUnsupported = Solid(12, 16, new Color(.5f, .5f, 1f, 1f)); neutralUnsupported.name = "DatabaseRenamedNonPotNormal";
            Texture2D unsupportedNormal = Solid(8, 8, Color.red); unsupportedNormal.name = "Shader_NoneNormal.normal";
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            material.EnableKeyword("_NORMALMAP");
            Assert.That(AtlasMeshValidator.TryValidateSemantics(texture, neutral, material, adapter, "_BumpMap", out StackMachineDiagnostic contentNeutral), Is.True, contentNeutral?.message);
            Assert.That(AtlasMeshValidator.TryValidateSemantics(texture, neutralUnsupported, material, adapter, "_BumpMap", out StackMachineDiagnostic unsupportedExtentNeutral), Is.True, unsupportedExtentNeutral?.message);
            Assert.That(AtlasMeshValidator.TryValidateSemantics(texture, unsupportedNormal, material, adapter, "_BumpMap", out StackMachineDiagnostic unsupported), Is.False);
            Assert.That(unsupported.domainCode, Is.EqualTo("AtlasSourceExtentUnsupported"));
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(texture); Object.DestroyImmediate(neutral); Object.DestroyImmediate(neutralUnsupported); Object.DestroyImmediate(unsupportedNormal);
        }

        [Test]
        public void Validate_NeutralQueryUsesMaterialStateBeforeContentAndAcceptsCompressedQuantization()
        {
            Texture2D nonNeutral = Solid(8, 8, Color.red); nonNeutral.name = "DatabaseRenamedNormal";
            Texture2D compressedNeutral = Solid(8, 8, new Color(.5f, .5f, 1f, 1f)); compressedNeutral.name = "CompressedDatabaseNormal";
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            try
            {
                material.EnableKeyword("_NORMALMAP");
                Assert.That(adapter.TryGetEffectiveNeutralTexture(material, "_BumpMap", nonNeutral, out bool contentResult, out MaterialProxyDiagnostic contentDiagnostic), Is.True, contentDiagnostic.message);
                Assert.That(contentResult, Is.False);

                // Compression is platform-dependent; where supported, the shared 8/255 RGB
                // tolerance must still classify the quantized neutral value as neutral.
                if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT1)) compressedNeutral.Compress(false);
                Assert.That(adapter.TryGetEffectiveNeutralTexture(material, "_BumpMap", compressedNeutral, out bool compressedResult, out MaterialProxyDiagnostic compressedDiagnostic), Is.True, compressedDiagnostic.message);
                Assert.That(compressedResult, Is.True);

                material.DisableKeyword("_NORMALMAP");
                Assert.That(adapter.TryGetEffectiveNeutralTexture(material, "_BumpMap", nonNeutral, out bool stateResult, out MaterialProxyDiagnostic stateDiagnostic), Is.True, stateDiagnostic.message);
                Assert.That(stateResult, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(nonNeutral); Object.DestroyImmediate(compressedNeutral);
            }
        }

        [Test]
        public void Validate_TargetIncludesContextForMissingSubmesh()
        {
            Mesh mesh = CreateMesh();
            var target = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "top"), 9);
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { target }, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasSubmeshMissing"));
            Assert.That(diagnostic.detail, Does.Contain("owner=outfit;materialId=outfit/top;submesh=9"));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_SkipsExcludedTarget()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            CleanAdapter adapter = ScriptableObject.CreateInstance<CleanAdapter>();
            material.SetTextureScale("_BaseMap", new Vector2(-1f, 2f));
            var target = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "excluded"), 9, true, material, adapter, null, null, 4, true, new Vector2(.5f, .5f), Vector2.right);
            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_TargetCarriesContextForSharedVertex()
        {
            Mesh mesh = CreateMesh();
            var target = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "top"), 0);
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { target }, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.detail, Does.StartWith("owner=outfit;materialId=outfit/top;submesh=0;pageIndex=-1;cause="));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_UsesFirstIncludedTargetForContext()
        {
            Mesh mesh = CreateMesh();
            var excluded = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "skip"), 1, true);
            var target = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "top"), 0);
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { excluded, target }, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.detail, Does.StartWith("owner=outfit;materialId=outfit/top;submesh=0;pageIndex=-1;cause="));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_RejectsAdapterReportedNonOwnedUv0Texture()
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            TestAdapter adapter = ScriptableObject.CreateInstance<TestAdapter>();
            Assert.That(AtlasMeshValidator.TryValidateAdapter(material, adapter, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasNonOwnedUv0Texture"));
            Assert.That(diagnostic.detail, Is.EqualTo("property=_DetailTex"));
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material);
        }

        [Test]
        public void Validate_TargetMaterialFailuresRetainCompleteEntryContext()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Texture2D texture = new Texture2D(128, 128);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            TestAdapter adapter = ScriptableObject.CreateInstance<TestAdapter>();
            material.mainTextureOffset = Vector2.right;
            var target = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "top"), 0, false, material, adapter, texture, texture, 2);

            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic transform), Is.False);
            Assert.That(transform.domainCode, Is.EqualTo("AtlasMainTextureTransformUnsupported"));
            Assert.That(transform.detail, Does.StartWith("owner=outfit;materialId=outfit/top;submesh=0;pageIndex=2;cause="));
            material.mainTextureOffset = Vector2.zero;
            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic adapterFailure), Is.False);
            Assert.That(adapterFailure.domainCode, Is.EqualTo("AtlasNonOwnedUv0Texture"));
            Assert.That(adapterFailure.detail, Does.Contain("pageIndex=2;cause=property=_DetailTex"));

            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_TargetSemanticFailureRetainsCompleteEntryContext()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            TestAdapter adapter = ScriptableObject.CreateInstance<TestAdapter>();
            Texture2D texture = new Texture2D(128, 128);
            var target = new AtlasMeshValidator.Target("figure", new MaterialId("figure", "skin"), 0, false, material, adapter, texture, null, 4);

            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasNormalRequired"));
            Assert.That(diagnostic.detail, Does.StartWith("owner=figure;materialId=figure/skin;submesh=0;pageIndex=4;cause="));

            Object.DestroyImmediate(texture); Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_AttributesMeshFailureToTheCausalLaterTarget()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one, Vector3.right * 2f, Vector3.up * 2f };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up };
            mesh.subMeshCount = 3;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            mesh.SetIndices(new[] { 3, 4, 5 }, MeshTopology.Triangles, 1);
            mesh.SetIndices(new[] { 3, 4, 5 }, MeshTopology.Triangles, 2);
            var first = new AtlasMeshValidator.Target("figure", new MaterialId("figure", "skin"), 0, false, pageIndex: 0);
            var causal = new AtlasMeshValidator.Target("outfit", new MaterialId("outfit", "top"), 2, false, pageIndex: 3);

            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { first, causal }, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasSharedVertex"));
            Assert.That(diagnostic.detail, Does.StartWith("owner=outfit;materialId=outfit/top;submesh=2;pageIndex=3;cause="));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_ResolvedTargetRequiresCompletePayloadAndCandidateRejectsUnscopedSources()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            var target = new AtlasMeshValidator.Target("figure", new MaterialId("figure", "skin"), 0, false, pageIndex: 1);
            Texture2D texture = new Texture2D(128, 128);

            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic payload), Is.False);
            Assert.That(payload.domainCode, Is.EqualTo("AtlasBaseColorRequired"));
            Assert.That(payload.detail, Does.StartWith("owner=figure;materialId=figure/skin;submesh=0;pageIndex=1;cause="));
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { target }, new Texture[] { texture }, out StackMachineDiagnostic unscoped), Is.False);
            Assert.That(unscoped.domainCode, Is.EqualTo("AtlasSourceTexturesUnscoped"));
            Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_ReportsSharedVertexGroupAndUvSpecialValues()
        {
            Mesh mesh = CreateMesh();
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic shared), Is.False);
            Assert.That(shared.detail, Does.Contain("atlasSubmesh=0;otherSubmesh=1;sharedVertexCount=2;vertices=0,2"));
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            mesh.uv = new[] { new Vector2(float.NaN, 0f), Vector2.right, Vector2.up, Vector2.one };
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic nan), Is.False);
            Assert.That(nan.domainCode, Is.EqualTo("AtlasUv0OutOfRange"));
            mesh.uv = new[] { new Vector2(float.PositiveInfinity, 0f), Vector2.right, Vector2.up, Vector2.one };
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic infinity), Is.False);
            Assert.That(infinity.domainCode, Is.EqualTo("AtlasUv0OutOfRange"));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_RawMultipleAtlasSubmeshesRejectSharedVerticesAndMissingUv0()
        {
            Mesh mesh = CreateMesh();
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0, 1 }, null, out StackMachineDiagnostic shared), Is.False);
            Assert.That(shared.detail, Does.Contain("atlasSubmesh=0;otherSubmesh=1"));
            mesh.uv = new Vector2[0];
            Assert.That(AtlasMeshValidator.TryValidate(mesh, new[] { 0 }, null, out StackMachineDiagnostic missing), Is.False);
            Assert.That(missing.domainCode, Is.EqualTo("AtlasUv0Required"));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_ResolvedTargetAcceptsCompleteOwnedPayload()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Texture2D texture = new Texture2D(128, 128);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            CleanAdapter adapter = ScriptableObject.CreateInstance<CleanAdapter>();
            var target = new AtlasMeshValidator.Target("figure", new MaterialId("figure", "skin"), 0, false, material, adapter, texture, texture, 0);
            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_ResolvedTargetAcceptsItsAppliedUvsetAndRejectsUnexpectedTransform()
        {
            Mesh mesh = CreateMesh();
            mesh.SetIndices(new[] { 3, 3, 3 }, MeshTopology.Triangles, 1);
            Texture2D texture = new Texture2D(128, 128);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            CleanAdapter adapter = ScriptableObject.CreateInstance<CleanAdapter>();
            Vector2 scale = new Vector2(.5f, .75f);
            Vector2 offset = new Vector2(.25f, .125f);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            var target = new AtlasMeshValidator.Target("figure", new MaterialId("figure", "skin"), 0, false, material, adapter, texture, texture, 0, true, scale, offset);

            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic accepted), Is.True, accepted?.message);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            Assert.That(AtlasMeshValidator.TryValidateResolved(mesh, new[] { target }, out StackMachineDiagnostic rejected), Is.False);
            Assert.That(rejected.domainCode, Is.EqualTo("AtlasMainTextureTransformUnsupported"));
            Assert.That(rejected.detail, Does.Contain("owner=figure;materialId=figure/skin;submesh=0;pageIndex=0"));
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(material); Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Validate_UrpLitRejectsEffectiveNonOwnedDetailTexture()
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Texture2D texture = new Texture2D(128, 128);
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            material.SetTexture("_DetailAlbedoMap", texture);
            material.EnableKeyword("_DETAIL_MULX2");
            Assert.That(AtlasMeshValidator.TryValidateAdapter(material, adapter, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.detail, Is.EqualTo("property=_DetailAlbedoMap"));
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(texture); Object.DestroyImmediate(material);
        }

        [Test]
        public void Validate_UrpAdapterUsesBaseMapTransformAndDetailUvChannel()
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Texture2D texture = new Texture2D(128, 128);
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            material.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
            Assert.That(AtlasMeshValidator.TryValidateMainTextureTransform(material, adapter, out StackMachineDiagnostic st), Is.False);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTexture("_DetailAlbedoMap", texture); material.EnableKeyword("_DETAIL_MULX2"); material.SetFloat("_DetailUV", 1f);
            Assert.That(AtlasMeshValidator.TryValidateAdapter(material, adapter, out StackMachineDiagnostic uv1), Is.True, uv1?.message);
            material.SetFloat("_DetailUV", 0f);
            Assert.That(AtlasMeshValidator.TryValidateAdapter(material, adapter, out StackMachineDiagnostic uv0), Is.False);
            Object.DestroyImmediate(adapter); Object.DestroyImmediate(texture); Object.DestroyImmediate(material);
        }

        private sealed class TestAdapter : MaterialShaderAdapter
        {
            public override string ExpectedShaderName => "Universal Render Pipeline/Unlit";
            public override bool TryGetEffectiveNonOwnedUv0Texture(Material material, out string propertyName, out MaterialProxyDiagnostic diagnostic) { propertyName = "_DetailTex"; diagnostic = default; return true; }
            public override bool TryGetAtlasBaseColorTransform(Material material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic diagnostic) { propertyName = "_MainTex"; scale = material.mainTextureScale; offset = material.mainTextureOffset; diagnostic = default; return true; }
            protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination) { }
        }

        private sealed class CleanAdapter : MaterialShaderAdapter
        {
            public override string ExpectedShaderName => "Universal Render Pipeline/Unlit";
            public override bool TryGetAtlasBaseColorTransform(Material material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic diagnostic) { propertyName = "_BaseMap"; scale = material.GetTextureScale(propertyName); offset = material.GetTextureOffset(propertyName); diagnostic = default; return true; }
            protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination) { }
        }

        private static Texture2D Solid(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false);
            return texture;
        }

        private static Mesh CreateMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.subMeshCount = 2;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            mesh.SetIndices(new[] { 0, 2, 3 }, MeshTopology.Triangles, 1);
            return mesh;
        }
    }
}
