// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasCandidateApplicatorTests
    {
        [Test]
        public void Apply_RemapCandidateOnlyRebindsSemanticPagesAndTransfersOwnership()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material candidateMaterial = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null;
            RenderTexture oldBase = null; RenderTexture oldNormal = null; RenderTexture pageBase = null; RenderTexture pageNormal = null;
            int oldReleases = 0; int pageReleases = 0;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", sourceNormal);
                candidateMaterial = new Material(source);
                oldBase = RenderTexture(); oldNormal = RenderTexture();
                candidateMaterial.SetTexture("_BaseMap", oldBase); candidateMaterial.SetTexture("_BumpMap", oldNormal);
                candidateMaterial.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); candidateMaterial.SetTextureOffset("_BaseMap", new Vector2(0.25f, 0.25f));
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                candidate = Candidate(source, candidateMaterial, adapter);
                candidate.AddOwnedTexture(new HumanoidOwnedTexture(oldBase, _ => oldReleases++));
                candidate.AddOwnedTexture(new HumanoidOwnedTexture(oldNormal, _ => oldReleases++));
                pageBase = RenderTexture(); pageNormal = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[]
                {
                    new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => pageReleases++),
                    new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => pageReleases++)
                });
                Vector2 sourceUv = candidate.Mesh.uv[0];

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(source.GetTexture("_BaseMap"), Is.SameAs(sourceBase));
                Assert.That(candidateMaterial.GetTexture("_BaseMap"), Is.SameAs(pageBase));
                Assert.That(candidateMaterial.GetTexture("_BumpMap"), Is.SameAs(pageNormal));
                Assert.That(candidateMaterial.GetTextureScale("_BaseMap"), Is.EqualTo(Vector2.one));
                Assert.That(candidateMaterial.GetTextureOffset("_BaseMap"), Is.EqualTo(Vector2.zero));
                Assert.That(candidate.AtlasPages, Is.Not.Null);
                Assert.That(candidate.AtlasPages.Pages, Has.Count.EqualTo(2));
                Assert.That(candidate.AtlasPages.Pages[0].PageIndex, Is.EqualTo(candidate.AtlasPages.Pages[1].PageIndex));
                Assert.That(candidate.AtlasPages.Pages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
                Assert.That(candidate.AtlasPages.Pages[1].Semantic, Is.EqualTo(AtlasTextureSemantic.Normal));
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(AtlasUvTransform.Apply(sourceUv, new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.25f), logical.Layout.Cells[0], logical.Layout.PageExtent)));
                Assert.That(oldReleases, Is.EqualTo(2));
                execution.Dispose(); Assert.That(pageReleases, Is.EqualTo(0));
                candidate.Dispose(); candidate = null;
                Assert.That(pageReleases, Is.EqualTo(2));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(candidateMaterial); Destroy(adapter);
                Destroy(oldBase); Destroy(oldNormal); Destroy(pageBase); Destroy(pageNormal);
            }
        }

        [Test]
        public void Apply_RejectsInvalidCandidateBeforeMutatingOrTakingExecutionOwnership()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material candidateMaterial = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null;
            RenderTexture pageBase = null; RenderTexture pageNormal = null;
            int pageReleases = 0;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sourceBase); candidateMaterial = new Material(source);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                candidate = Candidate(source, candidateMaterial, adapter);
                pageBase = RenderTexture(); pageNormal = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[]
                {
                    new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => pageReleases++),
                    new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => pageReleases++)
                });
                Vector2 originalUv = candidate.Mesh.uv[0]; Texture originalBase = candidateMaterial.GetTexture("_BaseMap");

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasCandidateValidationReadRejected"));
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(originalUv));
                Assert.That(candidateMaterial.GetTexture("_BaseMap"), Is.SameAs(originalBase));
                execution.Dispose(); Assert.That(pageReleases, Is.EqualTo(2));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(candidateMaterial); Destroy(adapter); Destroy(pageBase); Destroy(pageNormal);
            }
        }

        [Test]
        public void Apply_RejectsNormalPageWhenCandidateAdapterCannotBindNormal()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material candidateMaterial = null; UrpUnlitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null;
            RenderTexture pageBase = null; RenderTexture pageNormal = null;
            int pageReleases = 0;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); candidateMaterial = new Material(source);
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                candidate = Candidate(source, candidateMaterial, adapter);
                pageBase = RenderTexture(); pageNormal = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[]
                {
                    new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => pageReleases++),
                    new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => pageReleases++)
                });

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasBaseColorRequired"));
                Assert.That(candidateMaterial.GetTexture("_BaseMap"), Is.Null);
                execution.Dispose(); Assert.That(pageReleases, Is.EqualTo(2));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(candidateMaterial); Destroy(adapter); Destroy(pageBase); Destroy(pageNormal);
            }
        }

        [Test]
        public void Apply_NeutralNormalResultKeepsNormalBindingAndTransfersOnlyBasePage()
        {
            Texture2D sourceBase = Texture(); Texture2D neutral = NeutralNormal();
            Material source = null; Material candidateMaterial = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null; RenderTexture pageBase = null;
            int pageReleases = 0;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, neutral, true);
                Assert.That(logical.Pages, Has.Count.EqualTo(1));
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", neutral);
                candidateMaterial = new Material(source); adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); candidate = Candidate(source, candidateMaterial, adapter);
                pageBase = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[] { new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => pageReleases++) });

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(candidateMaterial.GetTexture("_BaseMap"), Is.SameAs(pageBase));
                Assert.That(candidateMaterial.GetTexture("_BumpMap"), Is.SameAs(neutral));
                candidate.Dispose(); candidate = null;
                Assert.That(pageReleases, Is.EqualTo(1));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(neutral); Destroy(source); Destroy(candidateMaterial); Destroy(adapter); Destroy(pageBase);
            }
        }

        [Test]
        public void Apply_RejectsUnexpectedCompletionBeforeMutatingOrTakingOwnership()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material candidateMaterial = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null;
            RenderTexture pageBase = null; RenderTexture pageNormal = null; RenderTexture unexpected = null;
            int pageReleases = 0;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", sourceNormal);
                candidateMaterial = new Material(source); adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); candidate = Candidate(source, candidateMaterial, adapter);
                pageBase = RenderTexture(); pageNormal = RenderTexture(); unexpected = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[]
                {
                    new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => pageReleases++),
                    new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => pageReleases++),
                    new AtlasBakerPageCompletion(99, AtlasTextureSemantic.BaseColor, unexpected, _ => pageReleases++)
                });
                Vector2 originalUv = candidate.Mesh.uv[0]; Texture originalBase = candidateMaterial.GetTexture("_BaseMap");

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasPageCompletionUnexpected"));
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(originalUv));
                Assert.That(candidateMaterial.GetTexture("_BaseMap"), Is.SameAs(originalBase));
                execution.Dispose(); Assert.That(pageReleases, Is.EqualTo(3));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(candidateMaterial); Destroy(adapter);
                Destroy(pageBase); Destroy(pageNormal); Destroy(unexpected);
            }
        }

        [Test]
        public void Apply_ChangesOnlyAtlasTargetSlotAndLeavesUntargetedUvAndMaterialUntouched()
        {
            Texture2D sourceBase = Texture(); Texture2D neutral = NeutralNormal();
            Material targetSource = null; Material target = null; Material otherSource = null; Material other = null;
            UrpLitMaterialShaderAdapter targetAdapter = null; UrpUnlitMaterialShaderAdapter otherAdapter = null; InMemoryHumanoidMesh candidate = null; RenderTexture pageBase = null; Texture2D otherBase = null;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, neutral, true);
                targetSource = new Material(Shader.Find("Universal Render Pipeline/Lit")); targetSource.SetTexture("_BaseMap", sourceBase); targetSource.SetTexture("_BumpMap", neutral);
                target = new Material(targetSource);
                otherSource = new Material(Shader.Find("Universal Render Pipeline/Unlit")); other = new Material(otherSource);
                otherBase = Texture(); other.SetTexture("_BaseMap", otherBase);
                targetAdapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); otherAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                candidate = CandidateWithUntargetedSlot(targetSource, target, targetAdapter, otherSource, other, otherAdapter);
                pageBase = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[] { new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => { }) });
                Vector2 untouchedUv = candidate.Mesh.uv[3];

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(target.GetTexture("_BaseMap"), Is.SameAs(pageBase));
                Assert.That(other.GetTexture("_BaseMap"), Is.SameAs(otherBase));
                Assert.That(candidate.Mesh.uv[3], Is.EqualTo(untouchedUv));
            }
            finally
            {
                candidate?.Dispose();
                Destroy(sourceBase); Destroy(neutral); Destroy(targetSource); Destroy(target); Destroy(otherSource); Destroy(other); Destroy(targetAdapter); Destroy(otherAdapter); Destroy(pageBase); Destroy(otherBase);
            }
        }

        [Test]
        public void Apply_AllExcludedLogicalResultIsNoOpAndLeavesAtlasPagesDetached()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material target = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null;
            try
            {
                AtlasBakerResult logical = AllExcludedLogical(sourceBase, sourceNormal);
                Assert.That(logical.Pages, Is.Empty);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", sourceNormal);
                target = new Material(source); adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); candidate = Candidate(source, target, adapter);
                Vector2 originalUv = candidate.Mesh.uv[0];
                var execution = new AtlasBakerExecutionResult(System.Array.Empty<AtlasBakerPageCompletion>());

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(candidate.AtlasPages, Is.Null);
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(originalUv));
                Assert.That(target.GetTexture("_BaseMap"), Is.SameAs(sourceBase));
                Assert.That(target.GetTexture("_BumpMap"), Is.SameAs(sourceNormal));
                execution.Dispose();
            }
            finally { candidate?.Dispose(); Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(target); Destroy(adapter); }
        }

        [Test]
        public void Apply_AcceptsOneUlpUnitBoundaryRoundingInDocumentUvSet()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material target = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null; RenderTexture pageBase = null; RenderTexture pageNormal = null;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", sourceNormal);
                target = new Material(source); target.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); target.SetTextureOffset("_BaseMap", new Vector2(0.50000006f, 0.5f));
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); candidate = Candidate(source, target, adapter);
                pageBase = RenderTexture(); pageNormal = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[] { new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => { }), new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => { }) });

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                execution.Dispose();
            }
            finally { candidate?.Dispose(); Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(target); Destroy(adapter); Destroy(pageBase); Destroy(pageNormal); }
        }

        [Test]
        public void Apply_RejectsDocumentTilingBeforeMutatingOrTakingExecutionOwnership()
        {
            Texture2D sourceBase = Texture(); Texture2D sourceNormal = Texture();
            Material source = null; Material target = null; UrpLitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh candidate = null; RenderTexture pageBase = null; RenderTexture pageNormal = null;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, sourceNormal);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sourceBase); source.SetTexture("_BumpMap", sourceNormal);
                target = new Material(source); target.SetTextureScale("_BaseMap", new Vector2(2f, 1f));
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); candidate = Candidate(source, target, adapter);
                pageBase = RenderTexture(); pageNormal = RenderTexture();
                var execution = new AtlasBakerExecutionResult(new[] { new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => { }), new AtlasBakerPageCompletion(logical.Pages[1].PageIndex, logical.Pages[1].Semantic, pageNormal, _ => { }) });
                Vector2 originalUv = candidate.Mesh.uv[0];

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasMainTextureTilingUnsupported"));
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(originalUv));
                Assert.That(target.GetTexture("_BaseMap"), Is.SameAs(sourceBase));
                execution.Dispose();
            }
            finally { candidate?.Dispose(); Destroy(sourceBase); Destroy(sourceNormal); Destroy(source); Destroy(target); Destroy(adapter); Destroy(pageBase); Destroy(pageNormal); }
        }

        [Test]
        public void Apply_RejectsAtlasVertexSharedWithUntargetedSubmeshBeforeMutation()
        {
            Texture2D sourceBase = Texture(); Texture2D neutral = NeutralNormal();
            Material targetSource = null; Material target = null; Material otherSource = null; Material other = null; UrpLitMaterialShaderAdapter targetAdapter = null; UrpUnlitMaterialShaderAdapter otherAdapter = null; InMemoryHumanoidMesh candidate = null; RenderTexture pageBase = null;
            try
            {
                AtlasBakerResult logical = Logical(sourceBase, neutral, true);
                targetSource = new Material(Shader.Find("Universal Render Pipeline/Lit")); targetSource.SetTexture("_BaseMap", sourceBase); targetSource.SetTexture("_BumpMap", neutral); target = new Material(targetSource);
                otherSource = new Material(Shader.Find("Universal Render Pipeline/Unlit")); other = new Material(otherSource);
                targetAdapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); otherAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                candidate = CandidateWithUntargetedSlot(targetSource, target, targetAdapter, otherSource, other, otherAdapter, true);
                pageBase = RenderTexture(); var execution = new AtlasBakerExecutionResult(new[] { new AtlasBakerPageCompletion(logical.Pages[0].PageIndex, logical.Pages[0].Semantic, pageBase, _ => { }) });
                Vector2 originalUv = candidate.Mesh.uv[0];

                Assert.That(AtlasCandidateApplicator.TryApply(candidate, logical, execution, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasSharedVertex"));
                Assert.That(candidate.Mesh.uv[0], Is.EqualTo(originalUv));
                execution.Dispose();
            }
            finally { candidate?.Dispose(); Destroy(sourceBase); Destroy(neutral); Destroy(targetSource); Destroy(target); Destroy(otherSource); Destroy(other); Destroy(targetAdapter); Destroy(otherAdapter); Destroy(pageBase); }
        }

        private static AtlasBakerResult Logical(Texture baseColor, Texture normal, bool normalIsNeutral = false)
        {
            var id = new MaterialId("outfit", "body");
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source-body") });
            var schema = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 0) });
            using (var operation = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(id, baseColor, normal, normalIsNeutral) }))
            {
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                return result;
            }
        }

        private static AtlasBakerResult AllExcludedLogical(Texture baseColor, Texture normal)
        {
            var id = new MaterialId("outfit", "body");
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source-body") });
            var schema = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(id, 0, 2, 2, true, 0) });
            using (var operation = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(id, baseColor, normal) }))
            {
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                return result;
            }
        }

        private static InMemoryHumanoidMesh Candidate(Material source, Material target, MaterialShaderAdapter adapter)
        {
            var mesh = new Mesh { subMeshCount = 1 };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            // A normal indexed submesh reuses vertices across triangles; this is not an inter-submesh Atlas conflict.
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            var candidate = new InMemoryHumanoidMesh(mesh);
            Assert.That(candidate.TrySetMaterials(new[] { target }, out StackMachineDiagnostic materials), Is.True, materials?.message);
            Assert.That(candidate.TrySetMaterialSlots(new[] { new HumanoidBuildMaterialSlot(new MaterialId("outfit", "body"), 0, source, adapter) }, out StackMachineDiagnostic slots), Is.True, slots?.message);
            return candidate;
        }
        private static InMemoryHumanoidMesh CandidateWithUntargetedSlot(Material targetSource, Material target, MaterialShaderAdapter targetAdapter, Material otherSource, Material other, MaterialShaderAdapter otherAdapter, bool shareTargetVertex = false)
        {
            var mesh = new Mesh { subMeshCount = 2 };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(2f, 0f), new Vector3(3f, 0f), new Vector3(2f, 1f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up };
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(shareTargetVertex ? new[] { 0, 4, 5 } : new[] { 3, 4, 5 }, 1);
            var candidate = new InMemoryHumanoidMesh(mesh);
            Assert.That(candidate.TrySetMaterials(new[] { target, other }, out StackMachineDiagnostic materials), Is.True, materials?.message);
            Assert.That(candidate.TrySetMaterialSlots(new[]
            {
                new HumanoidBuildMaterialSlot(new MaterialId("outfit", "body"), 0, targetSource, targetAdapter),
                new HumanoidBuildMaterialSlot(new MaterialId("outfit", "untargeted"), 1, otherSource, otherAdapter)
            }, out StackMachineDiagnostic slots), Is.True, slots?.message);
            return candidate;
        }
        private static Texture2D Texture() => new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        private static Texture2D NeutralNormal()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = "DatabaseRenamedNormal" };
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.5f, .5f, 1f, 1f);
            texture.SetPixels(pixels); texture.Apply(false, false);
            return texture;
        }
        private static RenderTexture RenderTexture() { var texture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32); texture.Create(); return texture; }
        private static void Destroy(Object value)
        {
            if (value is UnityEngine.RenderTexture texture && UnityEngine.RenderTexture.active == texture) UnityEngine.RenderTexture.active = null;
            if (value != null) Object.DestroyImmediate(value);
        }
    }
}
