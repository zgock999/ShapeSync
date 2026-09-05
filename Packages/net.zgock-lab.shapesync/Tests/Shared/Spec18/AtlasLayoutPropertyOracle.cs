// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Checks structural properties of a solved Atlas layout without implementing placement.</summary>
    internal static class AtlasLayoutPropertyOracle
    {
        internal static bool TryValidate(AtlasSchemaDocument document, AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;
            if (layout == null) return Fail("AtlasLayoutPropertyLayoutRequired", "Atlas Layout property Oracle requires a solved layout.", out diagnostic);
            if (layout.PageExtent != document.PageExtent) return Fail("AtlasLayoutPropertyExtentMismatch", "Atlas Layout property Oracle requires the Schema and layout page extent to match.", out diagnostic);

            var expectedEntries = new Dictionary<MaterialId, AtlasSchemaEntry>();
            var normalizedPages = new SortedSet<int>();
            foreach (AtlasSchemaEntry entry in document.Entries)
            {
                if (entry.Excluded) continue;
                MaterialId materialId = entry.MaterialId.ToMaterialId();
                expectedEntries.Add(materialId, entry);
                normalizedPages.Add(entry.PageIndex);
            }

            if (!ValidateSemanticPages(layout, normalizedPages.Count, out diagnostic)) return false;

            var seenCells = new Dictionary<MaterialId, AtlasLayoutCell>();
            foreach (AtlasLayoutCell cell in layout.Cells)
            {
                if (cell == null) return Fail("AtlasLayoutPropertyCellInvalid", "Atlas Layout property Oracle requires non-null cells.", out diagnostic);
                if (!expectedEntries.TryGetValue(cell.MaterialId, out AtlasSchemaEntry entry)) return Fail("AtlasLayoutPropertyUnexpectedCell", "Atlas Layout property Oracle found a cell without a non-excluded Schema entry.", out diagnostic);
                if (!seenCells.TryAdd(cell.MaterialId, cell)) return Fail("AtlasLayoutPropertyCellDuplicate", "Atlas Layout property Oracle found duplicate MaterialId cells.", out diagnostic);
                if (cell.PageIndex != DenseIndex(normalizedPages, entry.PageIndex)) return Fail("AtlasLayoutPropertyPageMismatch", "Atlas Layout property Oracle found a cell on the wrong normalized page.", out diagnostic);
                if (cell.Width != document.PageExtent >> entry.CellLevelX || cell.Height != document.PageExtent >> entry.CellLevelY) return Fail("AtlasLayoutPropertyCellSizeMismatch", "Atlas Layout property Oracle found a cell whose extent differs from its Schema levels.", out diagnostic);
                if (cell.Gutter != entry.Gutter) return Fail("AtlasLayoutPropertyGutterMismatch", "Atlas Layout property Oracle found a cell whose gutter differs from its Schema entry.", out diagnostic);
                if (cell.X % AtlasLayoutOracle.MinimumCellEdge != 0 || cell.Y % AtlasLayoutOracle.MinimumCellEdge != 0) return Fail("AtlasLayoutPropertyGridAlignment", "Atlas Layout property Oracle found a cell origin outside the minimum-cell grid.", out diagnostic);
                if (cell.X < 0 || cell.Y < 0 || cell.Width <= 0 || cell.Height <= 0 || cell.X + cell.Width > layout.PageExtent || cell.Y + cell.Height > layout.PageExtent)
                    return Fail("AtlasLayoutPropertyContainment", "Atlas Layout property Oracle found a cell outside its page extent.", out diagnostic);
            }

            if (seenCells.Count != expectedEntries.Count) return Fail("AtlasLayoutPropertyCellMissing", "Atlas Layout property Oracle did not receive one cell for every non-excluded Schema entry.", out diagnostic);
            foreach (KeyValuePair<MaterialId, AtlasSchemaEntry> expected in expectedEntries)
                if (!seenCells.ContainsKey(expected.Key)) return Fail("AtlasLayoutPropertyCellMissing", "Atlas Layout property Oracle did not receive one cell for every non-excluded Schema entry.", out diagnostic);

            var cells = layout.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                for (int j = i + 1; j < cells.Count; j++)
                {
                    AtlasLayoutCell left = cells[i];
                    AtlasLayoutCell right = cells[j];
                    if (left.PageIndex == right.PageIndex && Overlaps(left, right))
                        return Fail("AtlasLayoutPropertyOverlap", "Atlas Layout property Oracle found overlapping cells on one page.", out diagnostic);
                }
            }
            return true;
        }

        internal static bool TryValidateSolveFailure(AtlasSchemaDocument document, StackMachineDiagnostic solveDiagnostic, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;
            if (solveDiagnostic == null || solveDiagnostic.code != StackMachineDiagnosticCode.DomainFailure || solveDiagnostic.domain != "atlas" || solveDiagnostic.domainCode != "AtlasPageOverflow" || !HasAssignedCellIdentity(document, solveDiagnostic.detail))
                return Fail("AtlasLayoutPropertyFailureDiagnosticInvalid", "Atlas Layout property Oracle requires an AtlasPageOverflow diagnostic with page and MaterialId detail.", out diagnostic);
            return true;
        }

        private static bool HasAssignedCellIdentity(AtlasSchemaDocument document, string detail)
        {
            if (string.IsNullOrEmpty(detail)) return false;
            string[] fields = detail.Split(';');
            if (fields.Length != 2 || !fields[0].StartsWith("pageIndex=", StringComparison.Ordinal) || !fields[1].StartsWith("materialId=", StringComparison.Ordinal) || !int.TryParse(fields[0].Substring("pageIndex=".Length), out int pageIndex)) return false;
            string materialId = fields[1].Substring("materialId=".Length);
            var groupingKeys = new SortedSet<int>();
            foreach (AtlasSchemaEntry entry in document.Entries)
                if (!entry.Excluded) groupingKeys.Add(entry.PageIndex);
            foreach (AtlasSchemaEntry entry in document.Entries)
            {
                if (entry.Excluded || entry.MaterialId.ToMaterialId().ToString() != materialId) continue;
                return DenseIndex(groupingKeys, entry.PageIndex) == pageIndex;
            }
            return false;
        }

        private static bool ValidateSemanticPages(AtlasLayoutResult layout, int expectedPageCount, out StackMachineDiagnostic diagnostic)
        {
            if (layout.SemanticPages.Count != expectedPageCount * 2)
                return Fail("AtlasLayoutPropertySemanticPageCount", "Atlas Layout property Oracle found an unexpected potential semantic page count.", out diagnostic);
            var seen = new HashSet<string>();
            foreach (AtlasSemanticPage page in layout.SemanticPages)
            {
                if (page == null || page.PageIndex < 0 || page.PageIndex >= expectedPageCount || page.Extent != layout.PageExtent || (page.Semantic != AtlasTextureSemantic.BaseColor && page.Semantic != AtlasTextureSemantic.Normal))
                    return Fail("AtlasLayoutPropertySemanticPageInvalid", "Atlas Layout property Oracle found an invalid potential semantic page.", out diagnostic);
                if (!seen.Add(page.PageIndex + ":" + (int)page.Semantic))
                    return Fail("AtlasLayoutPropertySemanticPageDuplicate", "Atlas Layout property Oracle found duplicate potential semantic pages.", out diagnostic);
            }
            diagnostic = null;
            return true;
        }

        private static int DenseIndex(SortedSet<int> groupingKeys, int groupingKey)
        {
            int index = 0;
            foreach (int candidate in groupingKeys)
            {
                if (candidate == groupingKey) return index;
                index++;
            }
            return -1;
        }

        private static bool Overlaps(AtlasLayoutCell left, AtlasLayoutCell right)
            => left.X < right.X + right.Width && right.X < left.X + left.Width && left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }
    }
}
