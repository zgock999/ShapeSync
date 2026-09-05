// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasLayoutPropertyOracleTests
    {
        [Test]
        public void TryValidate_AcceptsAllCellShapesPagesAndGutter()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "whole", -8, 0, 0),
                Entry("outfit", "square", 42, 1, 1, false, 4),
                Entry("outfit", "horizontal", 42, 1, 3),
                Entry("outfit", "vertical", 42, 3, 1));

            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic solveDiagnostic), Is.True, solveDiagnostic?.message);
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [Test]
        public void TryValidate_AcceptsAllExcludedSchema()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", -8, 0, 0, true),
                Entry("outfit", "top", 42, 3, 3, true));

            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out StackMachineDiagnostic solveDiagnostic), Is.True, solveDiagnostic?.message);
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [Test]
        public void TryValidate_IsInvariantToInputOrderGroupingKeyAndOtherPages()
        {
            AtlasSchemaDocument first = Document(
                Entry("outfit", "a", 4, 2, 2),
                Entry("outfit", "b", 4, 1, 2));
            AtlasSchemaDocument second = Document(
                Entry("outfit", "b", 400, 1, 2),
                Entry("outfit", "other", -200, 0, 0),
                Entry("outfit", "a", 400, 2, 2));

            Assert.That(AtlasLayoutOracle.Solve(first, out AtlasLayoutResult firstLayout, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(AtlasLayoutOracle.Solve(second, out AtlasLayoutResult secondLayout, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(first, firstLayout, out StackMachineDiagnostic firstPropertyDiagnostic), Is.True, firstPropertyDiagnostic?.message);
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(second, secondLayout, out StackMachineDiagnostic secondPropertyDiagnostic), Is.True, secondPropertyDiagnostic?.message);
            AssertCellEquals(firstLayout, secondLayout, new MaterialId("outfit", "a"));
            AssertCellEquals(firstLayout, secondLayout, new MaterialId("outfit", "b"));
        }

        [Test]
        public void TryValidate_RejectsIndependentStructuralViolations()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 1, 1),
                Entry("outfit", "top", 0, 1, 1));
            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult solved, out StackMachineDiagnostic solveDiagnostic), Is.True, solveDiagnostic?.message);

            var overlapCells = new List<AtlasLayoutCell>
            {
                solved.Cells[0],
                new AtlasLayoutCell(solved.Cells[1].MaterialId, solved.Cells[1].PageIndex, solved.Cells[0].X, solved.Cells[0].Y, solved.Cells[1].Width, solved.Cells[1].Height, solved.Cells[1].Gutter)
            };
            var overlap = new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, overlapCells.AsReadOnly());
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, overlap, out StackMachineDiagnostic overlapDiagnostic), Is.False);
            Assert.That(overlapDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyOverlap"));

            var missing = new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell> { solved.Cells[0] }.AsReadOnly());
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, missing, out StackMachineDiagnostic missingDiagnostic), Is.False);
            Assert.That(missingDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyCellMissing"));

            var offGridCells = new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(solved.Cells[0].MaterialId, solved.Cells[0].PageIndex, 1, solved.Cells[0].Y, solved.Cells[0].Width, solved.Cells[0].Height, solved.Cells[0].Gutter),
                solved.Cells[1]
            };
            var offGrid = new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, offGridCells.AsReadOnly());
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, offGrid, out StackMachineDiagnostic offGridDiagnostic), Is.False);
            Assert.That(offGridDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyGridAlignment"));

            var duplicate = new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                solved.Cells[0], solved.Cells[0], solved.Cells[1]
            }.AsReadOnly());
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, duplicate, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyCellDuplicate"));

            var nullCell = new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                null, solved.Cells[1]
            }.AsReadOnly());
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, nullCell, out StackMachineDiagnostic nullCellDiagnostic), Is.False);
            Assert.That(nullCellDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyCellInvalid"));
        }

        [Test]
        public void TryValidate_RejectsMissingLayoutAndExtentMismatch()
        {
            AtlasSchemaDocument document = Document(Entry(string.Empty, "body", 0, 1, 1));

            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, null, out StackMachineDiagnostic missingDiagnostic), Is.False);
            Assert.That(missingDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyLayoutRequired"));
            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult solved, out StackMachineDiagnostic solveDiagnostic), Is.True, solveDiagnostic?.message);
            var wrongExtent = new AtlasLayoutResult(1024, solved.SemanticPages, solved.Cells);
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, wrongExtent, out StackMachineDiagnostic extentDiagnostic), Is.False);
            Assert.That(extentDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyExtentMismatch"));
        }

        [Test]
        public void TryValidate_RejectsEachSchemaToLayoutContractViolation()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 1, 1, false, 4),
                Entry("outfit", "top", 0, 1, 1));
            Assert.That(AtlasLayoutOracle.Solve(document, out AtlasLayoutResult solved, out StackMachineDiagnostic solveDiagnostic), Is.True, solveDiagnostic?.message);
            AtlasLayoutCell body = solved.Cells[0];
            AtlasLayoutCell top = solved.Cells[1];

            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(body.MaterialId, 1, body.X, body.Y, body.Width, body.Height, body.Gutter), top
            }.AsReadOnly()), "AtlasLayoutPropertyPageMismatch");
            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(body.MaterialId, body.PageIndex, body.X, body.Y, body.Width - AtlasLayoutOracle.MinimumCellEdge, body.Height, body.Gutter), top
            }.AsReadOnly()), "AtlasLayoutPropertyCellSizeMismatch");
            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(body.MaterialId, body.PageIndex, body.X, body.Y, body.Width, body.Height, 0), top
            }.AsReadOnly()), "AtlasLayoutPropertyGutterMismatch");
            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(body.MaterialId, body.PageIndex, solved.PageExtent, body.Y, body.Width, body.Height, body.Gutter), top
            }.AsReadOnly()), "AtlasLayoutPropertyContainment");
            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, solved.SemanticPages, new List<AtlasLayoutCell>
            {
                new AtlasLayoutCell(new MaterialId("outfit", "unexpected"), body.PageIndex, body.X, body.Y, body.Width, body.Height, body.Gutter), top
            }.AsReadOnly()), "AtlasLayoutPropertyUnexpectedCell");
            var invalidSemanticPages = new List<AtlasSemanticPage>
            {
                new AtlasSemanticPage(0, AtlasTextureSemantic.BaseColor, solved.PageExtent),
                new AtlasSemanticPage(0, AtlasTextureSemantic.Normal, solved.PageExtent - 1)
            };
            AssertReject(document, new AtlasLayoutResult(solved.PageExtent, invalidSemanticPages.AsReadOnly(), solved.Cells), "AtlasLayoutPropertySemanticPageInvalid");
        }

        [Test]
        public void TryValidateSolveFailure_AcceptsOverflowAndRejectsIncompleteDiagnostic()
        {
            AtlasSchemaDocument document = Document(
                Entry(string.Empty, "body", 0, 0, 0),
                Entry("outfit", "top", 0, 0, 0));

            Assert.That(AtlasLayoutOracle.Solve(document, out _, out StackMachineDiagnostic overflow), Is.False);
            Assert.That(AtlasLayoutPropertyOracle.TryValidateSolveFailure(document, overflow, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            StackMachineDiagnostic incomplete = StackMachineDiagnostic.CreateDomain("atlas", "AtlasPageOverflow", "overflow", detail: "pageIndex=0");
            Assert.That(AtlasLayoutPropertyOracle.TryValidateSolveFailure(document, incomplete, out StackMachineDiagnostic incompleteDiagnostic), Is.False);
            Assert.That(incompleteDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyFailureDiagnosticInvalid"));
            StackMachineDiagnostic unknownMaterial = StackMachineDiagnostic.CreateDomain("atlas", "AtlasPageOverflow", "overflow", detail: "pageIndex=0;materialId=missing");
            Assert.That(AtlasLayoutPropertyOracle.TryValidateSolveFailure(document, unknownMaterial, out StackMachineDiagnostic unknownMaterialDiagnostic), Is.False);
            Assert.That(unknownMaterialDiagnostic.domainCode, Is.EqualTo("AtlasLayoutPropertyFailureDiagnosticInvalid"));
        }

        private static AtlasSchemaDocument Document(params AtlasSchemaEntry[] entries)
        {
            var sources = new List<AtlasSourceMaterialIdentity>();
            foreach (AtlasSchemaEntry entry in entries)
                sources.Add(new AtlasSourceMaterialIdentity(entry.MaterialId.ToMaterialId(), "source:" + entry.MaterialId.RegistryId + "/" + entry.MaterialId.EntryId));
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", sources), entries);
        }

        private static AtlasSchemaEntry Entry(string registryId, string entryId, int page, int levelX, int levelY, bool excluded = false, int gutter = 0)
            => new AtlasSchemaEntry(new MaterialId(registryId, entryId), page, levelX, levelY, excluded, gutter);

        private static void AssertCellEquals(AtlasLayoutResult leftLayout, AtlasLayoutResult rightLayout, MaterialId materialId)
        {
            Assert.That(leftLayout.TryGetCell(materialId, out AtlasLayoutCell left), Is.True);
            Assert.That(rightLayout.TryGetCell(materialId, out AtlasLayoutCell right), Is.True);
            Assert.That(right.X, Is.EqualTo(left.X));
            Assert.That(right.Y, Is.EqualTo(left.Y));
            Assert.That(right.Width, Is.EqualTo(left.Width));
            Assert.That(right.Height, Is.EqualTo(left.Height));
            Assert.That(right.Gutter, Is.EqualTo(left.Gutter));
        }

        private static void AssertReject(AtlasSchemaDocument document, AtlasLayoutResult layout, string expectedCode)
        {
            Assert.That(AtlasLayoutPropertyOracle.TryValidate(document, layout, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo(expectedCode));
        }
    }
}
