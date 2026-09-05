// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasLayoutOracleTests
    {
        [Test]
        public void Solve_NormalizesGroupsAndCreatesBothSemanticPages()
        {
            AtlasSchemaDocument document = CreateDocument(512,
                Entry("outfit", "top", 42, 2, 1),
                Entry(string.Empty, "body", -8, 1, 2),
                Entry("outfit", "ignored", 999, 0, 0, true));

            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(layout.SemanticPages.Count, Is.EqualTo(4));
            Assert.That(layout.SemanticPages[0].PageIndex, Is.EqualTo(0));
            Assert.That(layout.SemanticPages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
            Assert.That(layout.SemanticPages[1].Semantic, Is.EqualTo(AtlasTextureSemantic.Normal));
            Assert.That(layout.SemanticPages[2].PageIndex, Is.EqualTo(1));
            Assert.That(layout.Cells.Count, Is.EqualTo(2));
            Assert.That(layout.TryGetCell(new MaterialId(string.Empty, "body"), out AtlasLayoutCell body), Is.True);
            Assert.That(body.PageIndex, Is.EqualTo(0));
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "top"), out AtlasLayoutCell top), Is.True);
            Assert.That(top.PageIndex, Is.EqualTo(1));
        }

        [Test]
        public void Solve_IsInvariantToInputOrderGroupingKeyValuesAndOtherPages()
        {
            AtlasSchemaDocument first = CreateDocument(512,
                Entry("outfit", "a", 4, 2, 2),
                Entry("outfit", "b", 4, 1, 2));
            AtlasSchemaDocument second = CreateDocument(512,
                Entry("outfit", "b", 400, 1, 2),
                Entry("outfit", "other", -200, 0, 0),
                Entry("outfit", "a", 400, 2, 2));

            Assert.That(AtlasLayoutOracle.Solve(first, out AtlasLayoutResult firstLayout, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(AtlasLayoutOracle.Solve(second, out AtlasLayoutResult secondLayout, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            AssertCellEquals(firstLayout, secondLayout, new MaterialId("outfit", "a"));
            AssertCellEquals(firstLayout, secondLayout, new MaterialId("outfit", "b"));
        }

        [Test]
        public void Solve_SortsByAreaThenOrdinalMaterialIdAndKeepsCellsContainedAndDisjoint()
        {
            AtlasSchemaDocument document = CreateDocument(512,
                Entry("outfit", "z", 0, 2, 2),
                Entry("outfit", "a", 0, 2, 2),
                Entry("outfit", "large", 0, 1, 2));

            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "large"), out AtlasLayoutCell large), Is.True);
            Assert.That(large.X, Is.EqualTo(0));
            Assert.That(large.Y, Is.EqualTo(0));
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "a"), out AtlasLayoutCell a), Is.True);
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "z"), out AtlasLayoutCell z), Is.True);
            Assert.That(a.Y < z.Y || (a.Y == z.Y && a.X < z.X), Is.True);
            foreach (AtlasLayoutCell cell in layout.Cells)
            {
                Assert.That(cell.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(cell.Y, Is.GreaterThanOrEqualTo(0));
                Assert.That(cell.X + cell.Width, Is.LessThanOrEqualTo(layout.PageExtent));
                Assert.That(cell.Y + cell.Height, Is.LessThanOrEqualTo(layout.PageExtent));
            }
            Assert.That(Overlaps(large, a), Is.False);
            Assert.That(Overlaps(large, z), Is.False);
            Assert.That(Overlaps(a, z), Is.False);
        }

        [Test]
        public void Solve_RejectsOverflowWithPageDiagnosticAndAllowsAllExcluded()
        {
            AtlasSchemaDocument overflow = CreateDocument(512,
                Entry("outfit", "a", 0, 0, 0),
                Entry("outfit", "b", 0, 0, 0));
            Assert.That(AtlasLayoutOracle.Solve(overflow, out _, out StackMachineDiagnostic overflowDiagnostic), Is.False);
            Assert.That(overflowDiagnostic.domainCode, Is.EqualTo("AtlasPageOverflow"));
            Assert.That(overflowDiagnostic.detail, Does.StartWith("pageIndex=0;materialId="));

            AtlasSchemaDocument excluded = CreateDocument(512, Entry("outfit", "a", 0, 0, 0, true));
            Assert.That(AtlasLayoutOracle.Solve(excluded, out AtlasLayoutResult empty, out StackMachineDiagnostic emptyDiagnostic), Is.True, emptyDiagnostic?.message);
            Assert.That(empty.Cells, Is.Empty);
            Assert.That(empty.SemanticPages, Is.Empty);
            Assert.That(empty.TryGetCell(new MaterialId("outfit", "missing"), out _), Is.False);
        }

        [Test]
        public void Solve_PreservesGutterAndProducesTheMinimumCell()
        {
            AtlasSchemaDocument document = CreateDocument(512, new AtlasSchemaEntry(new MaterialId("outfit", "small"), 0, 3, 3, false, 4));

            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "small"), out AtlasLayoutCell cell), Is.True);
            Assert.That(cell.Width, Is.EqualTo(AtlasLayoutOracle.MinimumCellEdge));
            Assert.That(cell.Height, Is.EqualTo(AtlasLayoutOracle.MinimumCellEdge));
            Assert.That(cell.Gutter, Is.EqualTo(4));
        }

        [Test]
        public void Feasibility_IsSourceOrderInvariantAndRejectsInvalidInputs()
        {
            AtlasSchemaDocument document = CreateDocument(512,
                Entry("outfit", "a", 0, 1, 1),
                Entry("outfit", "b", 0, 1, 1),
                Entry("outfit", "c", 0, 1, 1));
            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic layoutDiagnostic), Is.True, layoutDiagnostic?.message);

            var sources = new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 1024, 2048), Source("outfit", "b", AtlasTextureSemantic.BaseColor, 1024, 2048), Source("outfit", "c", AtlasTextureSemantic.BaseColor, 1024, 2048) };
            var splitCapability = new TextureGpuCapability(2048, 64L * 1024L * 1024L, 2048);
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, splitCapability, out AtlasFeasibilityResult split, out StackMachineDiagnostic splitDiagnostic), Is.True, splitDiagnostic?.message);
            Assert.That(split.RequiredRecipeCount, Is.EqualTo(3));
            Assert.That(split.SemanticPageCount, Is.EqualTo(1));
            Assert.That(split.Pages[0].PageIndex, Is.EqualTo(0));
            Assert.That(split.Pages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
            Assert.That(split.Pages[0].RequiredRecipeCount, Is.EqualTo(3));
            var reversed = new[] { sources[2], sources[0], sources[1] };
            Assert.That(AtlasFeasibility.TryEvaluate(layout, reversed, splitCapability, out AtlasFeasibilityResult reordered, out StackMachineDiagnostic reorderedDiagnostic), Is.True, reorderedDiagnostic?.message);
            Assert.That(reordered.RequiredRecipeCount, Is.EqualTo(split.RequiredRecipeCount));

            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(256, 16L * 1024L * 1024L, 256), out _, out StackMachineDiagnostic extentDiagnostic), Is.False);
            Assert.That(extentDiagnostic.domainCode, Is.EqualTo("AtlasPageExtentUnsupportedByGpu"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(1024, 16L * 1024L * 1024L, 256), out _, out StackMachineDiagnostic gridDiagnostic), Is.False);
            Assert.That(gridDiagnostic.domainCode, Is.EqualTo("AtlasPageExceedsFixedGrid"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(2048, 33L * 1024L * 1024L, 2048), out _, out StackMachineDiagnostic budgetDiagnostic), Is.False);
            Assert.That(budgetDiagnostic.domainCode, Is.EqualTo("AtlasActualPageBudgetExceeded"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(2048, 31L * 1024L * 1024L, 2048), out _, out StackMachineDiagnostic fixedBudgetDiagnostic), Is.False);
            Assert.That(fixedBudgetDiagnostic.domainCode, Is.EqualTo("AtlasFixedGridBudgetExceeded"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(1024, 16L * 1024L * 1024L, 512), out _, out StackMachineDiagnostic cellDiagnostic), Is.False);
            Assert.That(cellDiagnostic.domainCode, Is.EqualTo("AtlasSourceExceedsFixedGrid"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, sources, new TextureGpuCapability(2048, 64L * 1024L * 1024L, 768), out _, out StackMachineDiagnostic fixedGridDiagnostic), Is.False);
            Assert.That(fixedGridDiagnostic.domainCode, Is.EqualTo("AtlasFixedGridInvalid"));
            Assert.That(AtlasFeasibility.TryEvaluate(null, sources, splitCapability, out _, out StackMachineDiagnostic nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic.domainCode, Is.EqualTo("AtlasLayoutRequired"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", (AtlasTextureSemantic)99, 1, 1) }, splitCapability, out _, out StackMachineDiagnostic semanticDiagnostic), Is.False);
            Assert.That(semanticDiagnostic.domainCode, Is.EqualTo("AtlasSemanticInvalid"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "missing", AtlasTextureSemantic.BaseColor, 1, 1) }, splitCapability, out _, out StackMachineDiagnostic unassignedDiagnostic), Is.False);
            Assert.That(unassignedDiagnostic.domainCode, Is.EqualTo("AtlasSourceNotAssigned"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 0, 1) }, splitCapability, out _, out StackMachineDiagnostic invalidExtentDiagnostic), Is.False);
            Assert.That(invalidExtentDiagnostic.domainCode, Is.EqualTo("AtlasSourceExtentInvalid"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 128, 128), Source("outfit", "a", AtlasTextureSemantic.BaseColor, 128, 128) }, splitCapability, out _, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("AtlasSourceDuplicate"));

            var fragmentedSources = new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 2048, 512), Source("outfit", "b", AtlasTextureSemantic.BaseColor, 512, 2048) };
            Assert.That(AtlasFeasibility.TryEvaluate(layout, fragmentedSources, splitCapability, out AtlasFeasibilityResult fragmented, out StackMachineDiagnostic fragmentedDiagnostic), Is.True, fragmentedDiagnostic?.message);
            Assert.That(fragmented.RequiredRecipeCount, Is.EqualTo(2));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 4096, 128) }, splitCapability, out _, out StackMachineDiagnostic oversizedDiagnostic), Is.False);
            Assert.That(oversizedDiagnostic.domainCode, Is.EqualTo("AtlasSourceExceedsFixedGrid"));
            Assert.That(oversizedDiagnostic.detail, Does.Contain("pageIndex=0;semantic=BaseColor;materialId=outfit/a"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 192, 128) }, splitCapability, out _, out StackMachineDiagnostic unsupportedDiagnostic), Is.False);
            Assert.That(unsupportedDiagnostic.domainCode, Is.EqualTo("AtlasSourceExtentUnsupported"));
            Assert.That(unsupportedDiagnostic.detail, Does.Contain("materialId=outfit/a"));
            Assert.That(AtlasFeasibility.TryEvaluate(layout, new[] { Source("outfit", "a", AtlasTextureSemantic.BaseColor, 2048, 2048) }, splitCapability, out _, out StackMachineDiagnostic freshRecipeDiagnostic), Is.False);
            Assert.That(freshRecipeDiagnostic.domainCode, Is.EqualTo("AtlasSourceExceedsFixedGrid"));
            Assert.That(freshRecipeDiagnostic.detail, Does.Contain("pageIndex=0;semantic=BaseColor;materialId=outfit/a"));

            var multipleSemantics = new[]
            {
                Source("outfit", "a", AtlasTextureSemantic.BaseColor, 1024, 2048),
                Source("outfit", "b", AtlasTextureSemantic.BaseColor, 1024, 2048),
                Source("outfit", "c", AtlasTextureSemantic.BaseColor, 1024, 2048),
                Source("outfit", "a", AtlasTextureSemantic.Normal, 128, 128)
            };
            Assert.That(AtlasFeasibility.TryEvaluate(layout, multipleSemantics, splitCapability, out AtlasFeasibilityResult multiSemantic, out StackMachineDiagnostic multiSemanticDiagnostic), Is.True, multiSemanticDiagnostic?.message);
            Assert.That(multiSemantic.RequiredRecipeCount, Is.EqualTo(3));
            Assert.That(multiSemantic.SemanticPageCount, Is.EqualTo(2));
            Assert.That(multiSemantic.Pages[0].RequiredRecipeCount, Is.EqualTo(3));
            Assert.That(multiSemantic.Pages[1].RequiredRecipeCount, Is.EqualTo(1));

            AtlasSchemaDocument multiplePagesDocument = CreateDocument(512,
                Entry("outfit", "low", -8, 1, 1),
                Entry("outfit", "high", 42, 1, 1));
            Assert.That(AtlasLayoutOracle.Solve(multiplePagesDocument, out AtlasLayoutResult multiplePagesLayout, out StackMachineDiagnostic multiplePagesLayoutDiagnostic), Is.True, multiplePagesLayoutDiagnostic?.message);
            var unorderedPageSources = new[]
            {
                Source("outfit", "high", AtlasTextureSemantic.Normal, 128, 128),
                Source("outfit", "low", AtlasTextureSemantic.Normal, 128, 128),
                Source("outfit", "high", AtlasTextureSemantic.BaseColor, 128, 128)
            };
            Assert.That(AtlasFeasibility.TryEvaluate(multiplePagesLayout, unorderedPageSources, splitCapability, out AtlasFeasibilityResult pageOrder, out StackMachineDiagnostic pageOrderDiagnostic), Is.True, pageOrderDiagnostic?.message);
            Assert.That(AtlasFeasibility.TryEvaluate(multiplePagesLayout, new[] { unorderedPageSources[1], unorderedPageSources[2], unorderedPageSources[0] }, splitCapability, out AtlasFeasibilityResult reorderedPageOrder, out StackMachineDiagnostic reorderedPageOrderDiagnostic), Is.True, reorderedPageOrderDiagnostic?.message);
            Assert.That(pageOrder.Pages.Count, Is.EqualTo(3));
            Assert.That(pageOrder.Pages[0].PageIndex, Is.EqualTo(0));
            Assert.That(pageOrder.Pages[0].Semantic, Is.EqualTo(AtlasTextureSemantic.Normal));
            Assert.That(pageOrder.Pages[1].PageIndex, Is.EqualTo(1));
            Assert.That(pageOrder.Pages[1].Semantic, Is.EqualTo(AtlasTextureSemantic.BaseColor));
            Assert.That(pageOrder.Pages[2].PageIndex, Is.EqualTo(1));
            Assert.That(pageOrder.Pages[2].Semantic, Is.EqualTo(AtlasTextureSemantic.Normal));
            for (int i = 0; i < pageOrder.Pages.Count; i++)
            {
                Assert.That(reorderedPageOrder.Pages[i].PageIndex, Is.EqualTo(pageOrder.Pages[i].PageIndex));
                Assert.That(reorderedPageOrder.Pages[i].Semantic, Is.EqualTo(pageOrder.Pages[i].Semantic));
                Assert.That(reorderedPageOrder.Pages[i].RequiredRecipeCount, Is.EqualTo(pageOrder.Pages[i].RequiredRecipeCount));
            }

            AtlasSchemaDocument excluded = CreateDocument(512, Entry("outfit", "excluded", 0, 0, 0, true));
            Assert.That(AtlasLayoutOracle.Solve(excluded, out AtlasLayoutResult empty, out StackMachineDiagnostic emptyDiagnostic), Is.True, emptyDiagnostic?.message);
            Assert.That(AtlasFeasibility.TryEvaluate(empty, null, splitCapability, out AtlasFeasibilityResult emptyFeasibility, out StackMachineDiagnostic emptyFeasibilityDiagnostic), Is.True, emptyFeasibilityDiagnostic?.message);
            Assert.That(emptyFeasibility.RequiredRecipeCount, Is.EqualTo(0));
        }

        private static AtlasSchemaEntry Entry(string registryId, string entryId, int pageIndex, int levelX, int levelY, bool excluded = false)
        {
            return new AtlasSchemaEntry(new MaterialId(registryId, entryId), pageIndex, levelX, levelY, excluded);
        }

        private static AtlasFeasibilitySource Source(string registryId, string entryId, AtlasTextureSemantic semantic, int width, int height) => new AtlasFeasibilitySource(new MaterialId(registryId, entryId), semantic, width, height);

        private static AtlasSchemaDocument CreateDocument(int extent, params AtlasSchemaEntry[] entries)
        {
            var sources = new List<AtlasSourceMaterialIdentity>();
            for (int i = 0; i < entries.Length; i++)
                sources.Add(new AtlasSourceMaterialIdentity(entries[i].MaterialId.ToMaterialId(), "source-" + i));
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, extent, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", sources), entries);
        }

        private static void AssertCellEquals(AtlasLayoutResult leftLayout, AtlasLayoutResult rightLayout, MaterialId id)
        {
            Assert.That(leftLayout.TryGetCell(id, out AtlasLayoutCell left), Is.True);
            Assert.That(rightLayout.TryGetCell(id, out AtlasLayoutCell right), Is.True);
            Assert.That(right.Width, Is.EqualTo(left.Width));
            Assert.That(right.Height, Is.EqualTo(left.Height));
            Assert.That(right.X, Is.EqualTo(left.X));
            Assert.That(right.Y, Is.EqualTo(left.Y));
        }

        private static bool Overlaps(AtlasLayoutCell left, AtlasLayoutCell right)
        {
            return left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
        }
    }
}
