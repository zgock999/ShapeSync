// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasBakerOperationTests
    {
        [Test]
        public void Pump_ProducesDeterministicPageLocalFillAndDisjointPlaces_ForTexture2DAndRenderTexture()
        {
            Texture2D baseColor = Texture(128, 128);
            RenderTexture normal = RenderTexture(128, 128);
            try
            {
                AtlasSchemaDocument schema = Schema(Entry("body", 1, 1, 64));
                var operation = new AtlasBakerOperation(schema, Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal) });

                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages.Count, Is.EqualTo(2));
                AssertPage(result.Pages[0], AtlasTextureSemantic.BaseColor, Color.clear, baseColor);
                AssertPage(result.Pages[1], AtlasTextureSemantic.Normal, new Color(.5f, .5f, 1f, 1f), normal);
                Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic duplicate), Is.False);
                Assert.That(duplicate.domainCode, Is.EqualTo("AtlasBakerResultAlreadyTaken"));
            }
            finally { Object.DestroyImmediate(baseColor); normal.Release(); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_NeutralNormalResult_OmitsNormalPlaceAndKeepsBasePage_RegardlessOfTextureName()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D placeholder = Texture(8, 8); placeholder.name = "DatabaseRenamedNormal";
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, placeholder, true) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages.Count, Is.EqualTo(1));
                Assert.That(result.Pages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(placeholder); }
        }

        [Test]
        public void Pump_NeutralNormalSource_IsPlacedAndResampledWhenItIsNotTheEightByEightPlaceholder()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D neutral = Texture(128, 128); neutral.name = "DatabaseRenamedNormal";
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 1, 1, 64)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, neutral, true) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages.Count, Is.EqualTo(2));
                AssertPage(result.Pages[1], AtlasTextureSemantic.Normal, new Color(.5f, .5f, 1f, 1f), neutral);
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(neutral); }
        }

        [Test]
        public void Pump_NeutralNormalWithUnsupportedExtent_IsSourceLessRegardlessOfShape()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D neutral = Texture(12, 16); neutral.name = "DatabaseRenamedNormal";
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, neutral, true) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages.Count, Is.EqualTo(1));
                Assert.That(result.Pages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(neutral); }
        }

        [Test]
        public void Pump_NonNeutralEightByEightNormalStillRejectsUnsupportedExtent()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(8, 8); normal.name = "DatabaseRenamedNormal";
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal, false) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Failed));
                Assert.That(operation.Diagnostic.domainCode, Is.EqualTo("AtlasSourceExtentUnsupported"));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_ReconcilesMissingExcludedAndPassThroughEntries_WithoutCreatingPages()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasSchemaDocument schema = Schema(Entry("missing", 3, 3), Entry("excluded", 3, 3, 0, true));
                var operation = new AtlasBakerOperation(schema, Current("missing", "excluded", "extra"), new[] { new AtlasBakerMaterialInput(Id("excluded"), baseColor, normal), new AtlasBakerMaterialInput(Id("extra"), baseColor, normal) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages, Is.Empty);
                Assert.That(result.Reconciliation, Has.Count.EqualTo(3));
                Assert.That(result.Reconciliation[0].Code, Is.EqualTo("AtlasSchemaEntryMissingFromFinal"));
                Assert.That(result.Reconciliation[1].Code, Is.EqualTo("AtlasSchemaEntryExcluded"));
                Assert.That(result.Reconciliation[2].Code, Is.EqualTo("AtlasFinalMaterialNotInSchema"));
                Assert.That(result.Reconciliation[2].Severity, Is.EqualTo(AtlasBakerReconciliationSeverity.Warning));
                Assert.That(result.Reconciliation[2].Message, Does.Contain("owner=outfit"));
                Assert.That(result.Reconciliation[2].Message, Does.Contain("materialId=outfit/extra"));
                Assert.That(result.Reconciliation[2].Message, Does.Contain("schemaDocument=document;currentDocument=document"));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_RejectsStaleIdentity()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                var current = new AtlasValidationIdentity("other-figure", "document", new[] { new AtlasSourceMaterialIdentity(Id("body"), "source-body") });
                var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), current, new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Failed));
                Assert.That(operation.Diagnostic.domainCode, Is.EqualTo("AtlasValidationIdentityMismatch"));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [TestCase("duplicate", "AtlasFinalMaterialDuplicate")]
        [TestCase("missing-provenance", "AtlasCurrentSourceMaterialIdentityMissing")]
        [TestCase("missing-semantic", "AtlasSemanticTextureRequired")]
        [TestCase("unsupported-extent", "AtlasSourceExtentUnsupported")]
        [TestCase("invalid-interior", "AtlasCellInteriorInvalid")]
        [TestCase("null-input", "AtlasFinalMaterialInvalid")]
        [TestCase("invalid-material-id", "AtlasFinalMaterialInvalid")]
        public void Pump_RejectsInvalidFinalInputAndKeepsFailureTerminal(string scenario, string expectedCode)
        {
            Texture2D baseColor = Texture(scenario == "unsupported-extent" ? 64 : 128, scenario == "unsupported-extent" ? 64 : 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasSchemaEntry entry = scenario == "invalid-interior" ? Entry("body", 3, 3, 32) : Entry("body", 2, 2);
                AtlasValidationIdentity current = scenario == "missing-provenance" ? new AtlasValidationIdentity("figure", "document") : Current("body");
                AtlasBakerMaterialInput[] inputs = scenario == "null-input"
                    ? new AtlasBakerMaterialInput[] { null }
                    : scenario == "invalid-material-id"
                        ? new[] { new AtlasBakerMaterialInput(default, baseColor, normal) }
                    : scenario == "duplicate"
                    ? new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal), new AtlasBakerMaterialInput(Id("body"), baseColor, normal) }
                    : new[] { new AtlasBakerMaterialInput(Id("body"), scenario == "missing-semantic" ? null : baseColor, normal) };
                var operation = new AtlasBakerOperation(Schema(entry), current, inputs);

                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Failed));
                Assert.That(operation.Diagnostic.domainCode, Is.EqualTo(expectedCode));
                StackMachineDiagnostic first = operation.Diagnostic;
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Failed));
                Assert.That(operation.Diagnostic, Is.SameAs(first));
                Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic unavailable), Is.False);
                Assert.That(unavailable.domainCode, Is.EqualTo("AtlasBakerResultUnavailable"));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void CancelAfterSuccessDoesNotChangeResultAndDisposeRejectsFurtherAccess()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded));
                operation.Cancel();
                Assert.That(operation.Status, Is.EqualTo(AtlasBakerOperationStatus.Succeeded));
                operation.Dispose();
                Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic disposed), Is.False);
                Assert.That(disposed.domainCode, Is.EqualTo("AtlasBakerOperationDisposed"));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_AllowsAspectMismatchAsWarningAndPreservesDifferentSourceAndDestinationRectangles()
        {
            Texture2D baseColor = Texture(128, 256);
            Texture2D normal = Texture(128, 128);
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("body", 1, 1)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), baseColor, normal) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Reconciliation, Has.Count.EqualTo(1));
                Assert.That(result.Reconciliation[0].Code, Is.EqualTo("AtlasSourceAspectMismatch"));
                Assert.That(result.Reconciliation[0].Severity, Is.EqualTo(AtlasBakerReconciliationSeverity.Warning));
                AtlasBakerPageOperation place = result.Pages[0].Operations[1];
                Assert.That(place.SourceRectangle.Width, Is.EqualTo(128));
                Assert.That(place.SourceRectangle.Height, Is.EqualTo(256));
                Assert.That(place.DestinationRectangle.Width, Is.EqualTo(256));
                Assert.That(place.DestinationRectangle.Height, Is.EqualTo(256));
            }
            finally { Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_SortsMultiplePlacesByMaterialIdAndKeepsDestinationsDisjoint()
        {
            Texture2D a = Texture(128, 128), b = Texture(128, 128), normalA = Texture(128, 128), normalB = Texture(128, 128);
            try
            {
                var operation = new AtlasBakerOperation(Schema(Entry("z", 2, 2), Entry("a", 2, 2)), Current("z", "a"), new[] { new AtlasBakerMaterialInput(Id("z"), a, normalA), new AtlasBakerMaterialInput(Id("a"), b, normalB) });
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages, Has.Count.EqualTo(2));
                foreach (AtlasBakerPagePlan page in result.Pages)
                {
                    Assert.That(page.Operations, Has.Count.EqualTo(3));
                    Assert.That(page.Operations[1].MaterialId.EntryId, Is.EqualTo("a"));
                    Assert.That(page.Operations[2].MaterialId.EntryId, Is.EqualTo("z"));
                    Assert.That(Overlaps(page.Operations[1].DestinationRectangle, page.Operations[2].DestinationRectangle), Is.False);
                }
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(normalA); Object.DestroyImmediate(normalB); }
        }

        [Test]
        public void CancelAndDispose_KeepTerminalLifecycleAndDoNotExposeAResult()
        {
            var operation = new AtlasBakerOperation(Schema(Entry("body", 2, 2)), Current("body"), new[] { new AtlasBakerMaterialInput(Id("body"), null, null) });
            operation.Cancel();
            Assert.That(operation.Status, Is.EqualTo(AtlasBakerOperationStatus.Cancelled));
            Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Cancelled));
            Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic cancelled), Is.False);
            Assert.That(cancelled.domainCode, Is.EqualTo("AtlasBakerResultUnavailable"));
            operation.Dispose();
            Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic disposed), Is.False);
            Assert.That(disposed.domainCode, Is.EqualTo("AtlasBakerOperationDisposed"));
        }

        private static void AssertPage(AtlasBakerPagePlan page, AtlasTextureSemantic semantic, Color clear, Texture source)
        {
            Assert.That(page.Semantic, Is.EqualTo(semantic));
            Assert.That(page.Operations, Has.Count.EqualTo(2));
            Assert.That(page.Operations[0].Kind, Is.EqualTo(AtlasBakerPageOperationKind.FillOut));
            Assert.That(page.Operations[0].FillColor, Is.EqualTo(clear));
            Assert.That(page.Operations[1].Kind, Is.EqualTo(AtlasBakerPageOperationKind.Place));
            Assert.That(page.Operations[1].Source, Is.SameAs(source));
            Assert.That(page.Operations[1].SourceRectangle.Width, Is.EqualTo(128));
            Assert.That(page.Operations[1].DestinationRectangle.X, Is.EqualTo(64));
            Assert.That(page.Operations[1].DestinationRectangle.Y, Is.EqualTo(64));
        }

        private static AtlasSchemaDocument Schema(params AtlasSchemaEntry[] entries)
        {
            var sources = new List<AtlasSourceMaterialIdentity>();
            for (int i = 0; i < entries.Length; i++) sources.Add(new AtlasSourceMaterialIdentity(entries[i].MaterialId.ToMaterialId(), "source-" + entries[i].MaterialId.EntryId));
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", sources), entries);
        }
        private static AtlasSchemaEntry Entry(string id, int levelX, int levelY, int gutter = 0, bool excluded = false) => new AtlasSchemaEntry(Id(id), 0, levelX, levelY, excluded, gutter);
        private static AtlasValidationIdentity Current(params string[] ids)
        {
            var sources = new List<AtlasSourceMaterialIdentity>();
            for (int i = 0; i < ids.Length; i++) sources.Add(new AtlasSourceMaterialIdentity(Id(ids[i]), "source-" + ids[i]));
            return new AtlasValidationIdentity("figure", "document", sources);
        }
        private static MaterialId Id(string id) => new MaterialId("outfit", id);
        private static bool Overlaps(TextureDispatchRectangle left, TextureDispatchRectangle right)
            => left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
        private static Texture2D Texture(int width, int height) => new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        private static RenderTexture RenderTexture(int width, int height)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            Assert.That(texture.Create(), Is.True);
            return texture;
        }
    }
}
