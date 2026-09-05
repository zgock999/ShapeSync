// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Names one potential texture semantic layout for an Atlas page group.</summary>
    public enum AtlasTextureSemantic
    {
        /// <summary>The sRGB BaseColor texture semantic.</summary>
        BaseColor,
        /// <summary>The linear tangent-space Normal texture semantic.</summary>
        Normal
    }

    /// <summary>Describes one potential semantic layout shared by a derived page group; it is never serialized into an Atlas Schema.</summary>
    /// <remarks>This value does not claim that a texture asset exists. The Baker decides whether a BaseColor or Normal texture participates from its actual material input.</remarks>
    public sealed class AtlasSemanticPage
    {
        internal AtlasSemanticPage(int pageIndex, AtlasTextureSemantic semantic, int extent)
        {
            PageIndex = pageIndex;
            Semantic = semantic;
            Extent = extent;
        }

        /// <summary>Gets the dense, evaluation-time page index.</summary>
        public int PageIndex { get; }
        /// <summary>Gets the derived texture semantic.</summary>
        public AtlasTextureSemantic Semantic { get; }
        /// <summary>Gets the square page edge in texels.</summary>
        public int Extent { get; }
    }

    /// <summary>Describes one solved Atlas cell rectangle in texels.</summary>
    public sealed class AtlasLayoutCell
    {
        internal AtlasLayoutCell(MaterialId materialId, int pageIndex, int x, int y, int width, int height, int gutter)
        {
            MaterialId = materialId;
            PageIndex = pageIndex;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Gutter = gutter;
        }

        /// <summary>Gets the stable Schema entry key.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the dense page index containing this cell.</summary>
        public int PageIndex { get; }
        /// <summary>Gets the cell x origin in texels.</summary>
        public int X { get; }
        /// <summary>Gets the cell y origin in texels.</summary>
        public int Y { get; }
        /// <summary>Gets the cell width in texels.</summary>
        public int Width { get; }
        /// <summary>Gets the cell height in texels.</summary>
        public int Height { get; }
        /// <summary>Gets the user-authored gutter width in texels.</summary>
        public int Gutter { get; }
    }

    /// <summary>Detached deterministic layout derived from one validated Atlas Schema document.</summary>
    public sealed class AtlasLayoutResult
    {
        internal AtlasLayoutResult(int pageExtent, IReadOnlyList<AtlasSemanticPage> semanticPages, IReadOnlyList<AtlasLayoutCell> cells)
        {
            PageExtent = pageExtent;
            SemanticPages = semanticPages ?? Array.Empty<AtlasSemanticPage>();
            Cells = cells ?? Array.Empty<AtlasLayoutCell>();
        }

        /// <summary>Gets the common square page edge in texels.</summary>
        public int PageExtent { get; }
        /// <summary>Gets the potential BaseColor and Normal layouts for each derived page group.</summary>
        public IReadOnlyList<AtlasSemanticPage> SemanticPages { get; }
        /// <summary>Gets solved cells for non-excluded Schema entries.</summary>
        public IReadOnlyList<AtlasLayoutCell> Cells { get; }

        /// <summary>Finds a solved cell by its stable MaterialId.</summary>
        public bool TryGetCell(MaterialId materialId, out AtlasLayoutCell cell)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                AtlasLayoutCell candidate = Cells[i];
                if (candidate != null && candidate.MaterialId.Equals(materialId)) { cell = candidate; return true; }
            }
            cell = null;
            return false;
        }
    }

    /// <summary>One actual source texture that the Baker intends to place into a semantic Atlas page.</summary>
    public sealed class AtlasFeasibilitySource
    {
        /// <summary>Creates one detached source extent record.</summary>
        public AtlasFeasibilitySource(MaterialId materialId, AtlasTextureSemantic semantic, int width, int height)
        {
            MaterialId = materialId;
            Semantic = semantic;
            Width = width;
            Height = height;
        }
        /// <summary>Gets the assigned Schema entry key.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the actual source texture semantic.</summary>
        public AtlasTextureSemantic Semantic { get; }
        /// <summary>Gets the actual source texture width.</summary>
        public int Width { get; }
        /// <summary>Gets the actual source texture height.</summary>
        public int Height { get; }
    }

    /// <summary>Reports exact backend recipe capacity for actual source textures on a GPU capability snapshot.</summary>
    public sealed class AtlasFeasibilityResult
    {
        internal AtlasFeasibilityResult(int requiredRecipeCount, IReadOnlyList<AtlasFeasibilityPage> pages)
        {
            RequiredRecipeCount = requiredRecipeCount;
            Pages = pages ?? Array.Empty<AtlasFeasibilityPage>();
        }

        /// <summary>Gets the maximum number of sequential recipes required by one actual semantic page.</summary>
        public int RequiredRecipeCount { get; }
        /// <summary>Gets the actual semantic pages and their independently required recipe counts.</summary>
        public IReadOnlyList<AtlasFeasibilityPage> Pages { get; }
        /// <summary>Gets the number of actual semantic pages that participate in the evaluation.</summary>
        public int SemanticPageCount => Pages.Count;
    }

    /// <summary>Reports the capacity of one actual Atlas semantic page without prescribing its backend partition.</summary>
    public sealed class AtlasFeasibilityPage
    {
        internal AtlasFeasibilityPage(int pageIndex, AtlasTextureSemantic semantic, int requiredRecipeCount)
        {
            PageIndex = pageIndex;
            Semantic = semantic;
            RequiredRecipeCount = requiredRecipeCount;
        }

        /// <summary>Gets the dense layout page index.</summary>
        public int PageIndex { get; }
        /// <summary>Gets the participating texture semantic.</summary>
        public AtlasTextureSemantic Semantic { get; }
        /// <summary>Gets the recipe count required for this semantic page.</summary>
        public int RequiredRecipeCount { get; }
    }

    /// <summary>Solves deterministic Atlas page normalization and first-fit cell placement.</summary>
    public static class AtlasLayoutOracle
    {
        /// <summary>Smallest representable Phase-0 Atlas cell edge.</summary>
        public const int MinimumCellEdge = 64;

        /// <summary>Solves the non-serialized layout for one validated Schema document.</summary>
        public static bool Solve(AtlasSchemaDocument document, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic)
        {
            layout = null;
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;

            var groups = new SortedDictionary<int, List<AtlasSchemaEntry>>();
            IReadOnlyList<AtlasSchemaEntry> entries = document.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasSchemaEntry entry = entries[i];
                if (entry.Excluded) continue;
                if (!groups.TryGetValue(entry.PageIndex, out List<AtlasSchemaEntry> group))
                {
                    group = new List<AtlasSchemaEntry>();
                    groups.Add(entry.PageIndex, group);
                }
                group.Add(entry);
            }

            var semanticPages = new List<AtlasSemanticPage>(groups.Count * 2);
            var cells = new List<AtlasLayoutCell>();
            int pageIndex = 0;
            foreach (KeyValuePair<int, List<AtlasSchemaEntry>> pair in groups)
            {
                List<AtlasSchemaEntry> group = pair.Value;
                group.Sort(CompareEntries);
                if (!TrySolvePage(document.PageExtent, pageIndex, group, cells, out diagnostic)) return false;
                semanticPages.Add(new AtlasSemanticPage(pageIndex, AtlasTextureSemantic.BaseColor, document.PageExtent));
                semanticPages.Add(new AtlasSemanticPage(pageIndex, AtlasTextureSemantic.Normal, document.PageExtent));
                pageIndex++;
            }
            layout = new AtlasLayoutResult(document.PageExtent, semanticPages.AsReadOnly(), cells.AsReadOnly());
            diagnostic = null;
            return true;
        }

        private static int CompareEntries(AtlasSchemaEntry left, AtlasSchemaEntry right)
        {
            long leftArea = CellArea(left);
            long rightArea = CellArea(right);
            int area = rightArea.CompareTo(leftArea);
            if (area != 0) return area;
            MaterialId leftId = left.MaterialId.ToMaterialId();
            MaterialId rightId = right.MaterialId.ToMaterialId();
            int registry = string.CompareOrdinal(leftId.RegistryId, rightId.RegistryId);
            return registry != 0 ? registry : string.CompareOrdinal(leftId.EntryId, rightId.EntryId);
        }

        private static long CellArea(AtlasSchemaEntry entry) => (long)(1 << (3 - entry.CellLevelX)) * (1 << (3 - entry.CellLevelY));

        private static bool TrySolvePage(int extent, int pageIndex, List<AtlasSchemaEntry> entries, List<AtlasLayoutCell> cells, out StackMachineDiagnostic diagnostic)
        {
            int unitsPerAxis = extent / MinimumCellEdge;
            var occupied = new bool[unitsPerAxis * unitsPerAxis];
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasSchemaEntry entry = entries[i];
                int width = extent >> entry.CellLevelX;
                int height = extent >> entry.CellLevelY;
                int widthUnits = width / MinimumCellEdge;
                int heightUnits = height / MinimumCellEdge;
                if (!TryReserve(occupied, unitsPerAxis, widthUnits, heightUnits, out int x, out int y))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasPageOverflow", "Atlas page has no free cell for its assigned MaterialId.", detail: "pageIndex=" + pageIndex + ";materialId=" + entry.MaterialId.ToMaterialId());
                    return false;
                }
                cells.Add(new AtlasLayoutCell(entry.MaterialId.ToMaterialId(), pageIndex, x * MinimumCellEdge, y * MinimumCellEdge, width, height, entry.Gutter));
            }
            diagnostic = null;
            return true;
        }

        private static bool TryReserve(bool[] occupied, int axis, int width, int height, out int x, out int y)
        {
            for (int candidateY = 0; candidateY <= axis - height; candidateY++)
            {
                for (int candidateX = 0; candidateX <= axis - width; candidateX++)
                {
                    if (!IsFree(occupied, axis, candidateX, candidateY, width, height)) continue;
                    SetOccupied(occupied, axis, candidateX, candidateY, width, height);
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }
            x = 0;
            y = 0;
            return false;
        }

        private static bool IsFree(bool[] occupied, int axis, int x, int y, int width, int height)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++)
                    if (occupied[row * axis + column]) return false;
            return true;
        }

        private static void SetOccupied(bool[] occupied, int axis, int x, int y, int width, int height)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++) occupied[row * axis + column] = true;
        }
    }

    /// <summary>Evaluates exact Atlas recipe capacity and actual page budget without inspecting live grid occupancy.</summary>
    public static class AtlasFeasibility
    {
        /// <summary>Evaluates an already solved layout, actual source texture extents, and a detached Texture GPU capability snapshot.</summary>
        public static bool TryEvaluate(AtlasLayoutResult layout, IReadOnlyList<AtlasFeasibilitySource> sources, TextureGpuCapability capability, out AtlasFeasibilityResult feasibility, out StackMachineDiagnostic diagnostic)
        {
            feasibility = null;
            if (layout == null) return Fail("AtlasLayoutRequired", "Atlas feasibility requires a solved layout.", out diagnostic);
            if (layout.PageExtent <= 0 || layout.PageExtent % AtlasLayoutOracle.MinimumCellEdge != 0) return Fail("AtlasLayoutInvalid", "Atlas layout has an invalid page extent.", out diagnostic);
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(capability.FixedGridEdge) || capability.FixedGridEdge > capability.MaxTextureSize) return Fail("AtlasFixedGridInvalid", "Texture GPU capability has an invalid fixed grid edge.", out diagnostic);
            if (capability.MaxTextureSize < layout.PageExtent) return Fail("AtlasPageExtentUnsupportedByGpu", "GPU maximum texture size cannot support the Atlas page extent.", out diagnostic);
            if (capability.FixedGridEdge < layout.PageExtent) return Fail("AtlasPageExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain one Atlas page.", out diagnostic);
            long pageArea = (long)layout.PageExtent * layout.PageExtent;
            long gridArea = (long)capability.FixedGridEdge * capability.FixedGridEdge;
            long gridBytes = gridArea * TextureGpuCapabilityProbe.BytesPerPixel;
            if (capability.GpuBudgetBytes <= 0 || gridBytes > capability.GpuBudgetBytes) return Fail("AtlasFixedGridBudgetExceeded", "GPU budget cannot retain the fixed Texture StackMachine grid.", out diagnostic);
            var groups = new List<AtlasFeasibilityGroup>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (sources != null) for (int i = 0; i < sources.Count; i++)
            {
                AtlasFeasibilitySource source = sources[i];
                if (source == null || !source.MaterialId.IsValid || source.Width <= 0 || source.Height <= 0) return Fail("AtlasSourceExtentInvalid", "Atlas feasibility requires a valid positive source extent.", out diagnostic);
                if (source.Semantic != AtlasTextureSemantic.BaseColor && source.Semantic != AtlasTextureSemantic.Normal) return Fail("AtlasSemanticInvalid", "Atlas feasibility source semantic is unsupported.", out diagnostic);
                if (!layout.TryGetCell(source.MaterialId, out AtlasLayoutCell cell)) return Fail("AtlasSourceNotAssigned", "Atlas feasibility source is not assigned by the solved layout.", out diagnostic);
                string key = cell.PageIndex + ":" + (int)source.Semantic;
                string sourceKey = key + ":" + source.MaterialId.RegistryId + "\u001f" + source.MaterialId.EntryId;
                if (!seen.Add(sourceKey)) return Fail("AtlasSourceDuplicate", "Atlas feasibility contains a duplicate MaterialId semantic source.", out diagnostic);
                string detail = SourceDetail(cell.PageIndex, source);
                if (!TextureGpuCapabilityProbe.IsPhase0Edge(source.Width) || !TextureGpuCapabilityProbe.IsPhase0Edge(source.Height)) return Fail("AtlasSourceExtentUnsupported", "Atlas feasibility source extent is unsupported by the fixed Texture StackMachine grid.", detail, out diagnostic);
                if (source.Width > capability.FixedGridEdge || source.Height > capability.FixedGridEdge) return Fail("AtlasSourceExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain an Atlas page and one source cell.", detail, out diagnostic);
                AtlasFeasibilityGroup group = FindGroup(groups, cell.PageIndex, source.Semantic);
                if (group == null) { group = new AtlasFeasibilityGroup(cell.PageIndex, source.Semantic); groups.Add(group); }
                group.Sources.Add(source);
            }
            long persistentBytes = gridBytes + pageArea * TextureGpuCapabilityProbe.BytesPerPixel * groups.Count;
            if (persistentBytes > capability.GpuBudgetBytes) return Fail("AtlasActualPageBudgetExceeded", "GPU budget cannot retain the fixed grid and all actual Atlas semantic pages.", out diagnostic);
            int requiredRecipeCount = 0;
            groups.Sort(CompareGroups);
            var pages = new List<AtlasFeasibilityPage>(groups.Count);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                AtlasFeasibilityGroup group = groups[groupIndex];
                group.Sources.Sort(CompareSources);
                int pageRecipeCount = 1;
                var allocator = new TextureHallAllocator(capability.FixedGridEdge);
                if (!allocator.TryReserve(layout.PageExtent, layout.PageExtent, out _)) return Fail("AtlasPageExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain one Atlas page.", out diagnostic);
                for (int sourceIndex = 0; sourceIndex < group.Sources.Count; sourceIndex++)
                {
                    AtlasFeasibilitySource source = group.Sources[sourceIndex];
                    if (!allocator.TryReserve(source.Width, source.Height, out _))
                    {
                        pageRecipeCount++;
                        allocator = new TextureHallAllocator(capability.FixedGridEdge);
                        if (!allocator.TryReserve(layout.PageExtent, layout.PageExtent, out _) || !allocator.TryReserve(source.Width, source.Height, out _))
                            return Fail("AtlasSourceExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain an Atlas page and one source cell.", SourceDetail(group.PageIndex, source), out diagnostic);
                    }
                }
                if (pageRecipeCount > requiredRecipeCount) requiredRecipeCount = pageRecipeCount;
                pages.Add(new AtlasFeasibilityPage(group.PageIndex, group.Semantic, pageRecipeCount));
            }
            feasibility = new AtlasFeasibilityResult(requiredRecipeCount, pages.AsReadOnly());
            diagnostic = null;
            return true;
        }

        private static int CompareSources(AtlasFeasibilitySource left, AtlasFeasibilitySource right)
        {
            long leftArea = (long)left.Width * left.Height;
            long rightArea = (long)right.Width * right.Height;
            int area = rightArea.CompareTo(leftArea);
            if (area != 0) return area;
            int registry = string.CompareOrdinal(left.MaterialId.RegistryId, right.MaterialId.RegistryId);
            return registry != 0 ? registry : string.CompareOrdinal(left.MaterialId.EntryId, right.MaterialId.EntryId);
        }

        private static AtlasFeasibilityGroup FindGroup(List<AtlasFeasibilityGroup> groups, int pageIndex, AtlasTextureSemantic semantic)
        {
            for (int i = 0; i < groups.Count; i++) if (groups[i].PageIndex == pageIndex && groups[i].Semantic == semantic) return groups[i];
            return null;
        }

        private static int CompareGroups(AtlasFeasibilityGroup left, AtlasFeasibilityGroup right)
        {
            int page = left.PageIndex.CompareTo(right.PageIndex);
            return page != 0 ? page : ((int)left.Semantic).CompareTo((int)right.Semantic);
        }

        private static string SourceDetail(int pageIndex, AtlasFeasibilitySource source) => "pageIndex=" + pageIndex + ";semantic=" + source.Semantic + ";materialId=" + source.MaterialId;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }

        private static bool Fail(string code, string message, string detail, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message, detail: detail);
            return false;
        }

        private sealed class AtlasFeasibilityGroup
        {
            public AtlasFeasibilityGroup(int pageIndex, AtlasTextureSemantic semantic)
            {
                PageIndex = pageIndex;
                Semantic = semantic;
                Sources = new List<AtlasFeasibilitySource>();
            }

            public int PageIndex { get; }
            public AtlasTextureSemantic Semantic { get; }
            public List<AtlasFeasibilitySource> Sources { get; }
        }
    }
}
