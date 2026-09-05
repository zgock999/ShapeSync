// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasOracleFixtureTests
    {
        [Test]
        public void TryCreate_CapturesDetachedMetadataForBothSemantics()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", -10, 1, 2),
                Entry("outfit", "top", 20, 2, 1));

            Assert.That(AtlasOracleFixture.TryCreate(document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(fixture.Document, Is.Not.SameAs(document));
            Assert.That(fixture.Document, Is.Not.SameAs(fixture.Document));
            Assert.That(fixture.Document.AtlasSchemaVersion, Is.EqualTo(AtlasSchemaVersion.Current));
            Assert.That(fixture.Document.PackingAlgorithm, Is.EqualTo(AtlasPackingAlgorithm.FirstFitBuddyV1));
            Assert.That(fixture.Layout.PageExtent, Is.EqualTo(512));
            Assert.That(fixture.Metadata, Has.Count.EqualTo(12));
            Assert.That(fixture.Metadata, Has.Exactly(1).Matches<AtlasOracleEntryMetadata>(entry => entry.SchemaVersion == AtlasSchemaVersion.Current && entry.PackingAlgorithm == AtlasPackingAlgorithm.FirstFitBuddyV1 && entry.MaterialId.Equals(new MaterialId(string.Empty, "body")) && entry.SourceMaterialIdentity == "source:/body" && entry.Semantic == AtlasTextureSemantic.BaseColor && entry.Layer == AtlasOracleLayer.Layout && entry.Participation == AtlasOracleSemanticParticipation.Potential && entry.PageIndex == 0 && entry.FigureIdentity == "figure" && entry.DocumentIdentity == "document" && entry.ComparisonMode == AtlasOracleComparisonMode.ExactStructure));
            Assert.That(fixture.Metadata, Has.Exactly(1).Matches<AtlasOracleEntryMetadata>(entry => entry.MaterialId.Equals(new MaterialId("outfit", "top")) && entry.Semantic == AtlasTextureSemantic.Normal && entry.Layer == AtlasOracleLayer.Image && entry.Participation == AtlasOracleSemanticParticipation.Potential && entry.PageIndex == 1 && entry.Gutter == 0 && entry.ComparisonMode == AtlasOracleComparisonMode.PixelTolerance));
            Assert.That(fixture.Metadata, Has.Exactly(4).Matches<AtlasOracleEntryMetadata>(entry => entry.Layer == AtlasOracleLayer.Layout && entry.ComparisonMode == AtlasOracleComparisonMode.ExactStructure));
            Assert.That(fixture.Metadata, Has.Exactly(4).Matches<AtlasOracleEntryMetadata>(entry => entry.Layer == AtlasOracleLayer.MeshUv && entry.ComparisonMode == AtlasOracleComparisonMode.ExactStructure));
            Assert.That(fixture.Metadata, Has.Exactly(4).Matches<AtlasOracleEntryMetadata>(entry => entry.Layer == AtlasOracleLayer.Image && entry.ComparisonMode == AtlasOracleComparisonMode.PixelTolerance));
        }

        [Test]
        public void TryCreate_EmitsOnePotentialContextPerEntrySemanticAndLayer()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 1, 2),
                Entry("outfit", "top", 1, 2, 1));

            Assert.That(AtlasOracleFixture.TryCreate(document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(fixture.Metadata, Has.Count.EqualTo(12));

            MaterialId[] materialIds =
            {
                new MaterialId(string.Empty, "body"),
                new MaterialId("outfit", "top"),
            };
            AtlasTextureSemantic[] semantics =
            {
                AtlasTextureSemantic.BaseColor,
                AtlasTextureSemantic.Normal,
            };
            AtlasOracleLayer[] layers =
            {
                AtlasOracleLayer.Layout,
                AtlasOracleLayer.MeshUv,
                AtlasOracleLayer.Image,
            };

            foreach (MaterialId materialId in materialIds)
            {
                foreach (AtlasTextureSemantic semantic in semantics)
                {
                    foreach (AtlasOracleLayer layer in layers)
                    {
                        AtlasOracleComparisonMode expectedMode = layer == AtlasOracleLayer.Image
                            ? AtlasOracleComparisonMode.PixelTolerance
                            : AtlasOracleComparisonMode.ExactStructure;
                        Assert.That(CountContexts(fixture.Metadata, materialId, semantic, layer, expectedMode), Is.EqualTo(1),
                            $"Expected one potential {layer} context for {materialId} {semantic}.");
                    }
                }
            }

            foreach (AtlasOracleEntryMetadata entry in fixture.Metadata)
                Assert.That(entry.Participation, Is.EqualTo(AtlasOracleSemanticParticipation.Potential));
        }

        [Test]
        public void TryCreate_MetadataMatchesResolvedCellAndSourceProvenance()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", -10, 1, 2, false, 4),
                Entry("outfit", "top", 20, 2, 1));

            Assert.That(AtlasOracleFixture.TryCreate(document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);

            foreach (AtlasOracleEntryMetadata entry in fixture.Metadata)
            {
                Assert.That(fixture.Layout.TryGetCell(entry.MaterialId, out AtlasLayoutCell cell), Is.True);
                Assert.That(entry.PageIndex, Is.EqualTo(cell.PageIndex));
                Assert.That(entry.X, Is.EqualTo(cell.X));
                Assert.That(entry.Y, Is.EqualTo(cell.Y));
                Assert.That(entry.Width, Is.EqualTo(cell.Width));
                Assert.That(entry.Height, Is.EqualTo(cell.Height));
                Assert.That(entry.Gutter, Is.EqualTo(cell.Gutter));
                string expectedSource = entry.MaterialId.Equals(new MaterialId(string.Empty, "body"))
                    ? "source:/body"
                    : "source:outfit/top";
                Assert.That(entry.SourceMaterialIdentity, Is.EqualTo(expectedSource));
            }
        }

        [Test]
        public void TryCreate_DoesNotPublishExcludedEntriesAsOracleMetadata()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 1, 1),
                Entry("outfit", "ignored", 0, 0, 0, true));

            Assert.That(AtlasOracleFixture.TryCreate(document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(fixture.Metadata, Has.Count.EqualTo(6));
            Assert.That(fixture.Metadata, Has.None.Matches<AtlasOracleEntryMetadata>(entry => entry.MaterialId.Equals(new MaterialId("outfit", "ignored"))));
        }

        [Test]
        public void TryCreate_AcceptsAllExcludedSchemaWithoutPagesOrMetadata()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 0, 0, true),
                Entry("outfit", "top", 99, 3, 3, true));

            Assert.That(AtlasOracleFixture.TryCreate(document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(fixture.Layout.SemanticPages, Is.Empty);
            Assert.That(fixture.Layout.Cells, Is.Empty);
            Assert.That(fixture.Metadata, Is.Empty);
        }

        [Test]
        public void TryCreate_PropagatesSolvedLayoutOverflowAsStructuredDiagnostic()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 0, 0),
                Entry("outfit", "top", 0, 0, 0));

            Assert.That(AtlasOracleFixture.TryCreate(document, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasPageOverflow"));
        }

        [Test]
        public void TryCreate_RejectsMissingOrMalformedSchemaWithStructuredDiagnostic()
        {
            Assert.That(AtlasOracleFixture.TryCreate(null, out _, out StackMachineDiagnostic missing), Is.False);
            Assert.That(missing.domainCode, Is.EqualTo("AtlasSchemaRequired"));

            AtlasSchemaDocument malformed = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 256, AtlasPackingAlgorithm.FirstFitBuddyV1, true,
                new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(new MaterialId(string.Empty, "body"), "source") }),
                new[] { Entry(string.Empty, "body", 0, 1, 1) });
            Assert.That(AtlasOracleFixture.TryCreate(malformed, out _, out StackMachineDiagnostic invalid), Is.False);
            Assert.That(invalid.domainCode, Is.EqualTo("AtlasPageExtentUnsupported"));
        }

        [Test]
        public void Metadata_IsReadOnlyAndRetainsCellValuesWithoutUnityReferences()
        {
            Assert.That(AtlasOracleFixture.TryCreate(Document(Entry(string.Empty, "body", 0, 2, 3, false, 4)), out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var list = fixture.Metadata as IList<AtlasOracleEntryMetadata>;
            Assert.That(list, Is.Not.Null);
            Assert.That(() => list[0] = null, Throws.TypeOf<System.NotSupportedException>());
            AtlasOracleEntryMetadata entry = fixture.Metadata[0];
            Assert.That(entry.X, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.Width, Is.GreaterThan(0));
            Assert.That(entry.Height, Is.GreaterThan(0));
            Assert.That(entry.X + entry.Width, Is.LessThanOrEqualTo(entry.PageExtent));
            Assert.That(entry.Y + entry.Height, Is.LessThanOrEqualTo(entry.PageExtent));
        }

        private static AtlasSchemaDocument Document(params AtlasSchemaEntry[] entries)
        {
            var identities = new List<AtlasSourceMaterialIdentity>();
            foreach (AtlasSchemaEntry entry in entries)
                identities.Add(new AtlasSourceMaterialIdentity(entry.MaterialId.ToMaterialId(), "source:" + entry.MaterialId.RegistryId + "/" + entry.MaterialId.EntryId));
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", identities), entries);
        }

        private static AtlasSchemaEntry Entry(string registryId, string entryId, int page, int levelX, int levelY, bool excluded = false, int gutter = 0)
            => new AtlasSchemaEntry(new MaterialId(registryId, entryId), page, levelX, levelY, excluded, gutter);

        private static int CountContexts(IReadOnlyList<AtlasOracleEntryMetadata> entries, MaterialId materialId, AtlasTextureSemantic semantic, AtlasOracleLayer layer, AtlasOracleComparisonMode comparisonMode)
        {
            int count = 0;
            foreach (AtlasOracleEntryMetadata entry in entries)
            {
                if (entry.MaterialId.Equals(materialId) &&
                    entry.Semantic == semantic &&
                    entry.Layer == layer &&
                    entry.Participation == AtlasOracleSemanticParticipation.Potential &&
                    entry.ComparisonMode == comparisonMode)
                    count++;
            }
            return count;
        }
    }
}
