// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>One backend-owned cumulative recipe segment for a logical Atlas page.</summary>
    public sealed class AtlasBakerPageRecipePartition
    {
        internal AtlasBakerPageRecipePartition(AtlasBakerPageOperation[] operations, bool initializesOutput) { Operations = Array.AsReadOnly(operations); InitializesOutput = initializesOutput; }
        /// <summary>Gets the ordered page-local operations executed by this segment.</summary>
        public IReadOnlyList<AtlasBakerPageOperation> Operations { get; }
        /// <summary>Gets whether this segment owns the page's one initial FILL_OUT.</summary>
        public bool InitializesOutput { get; }
    }

    /// <summary>Partitions one logical page using the same empty-grid capacity model as <see cref="AtlasFeasibility"/>.</summary>
    /// <remarks>Partitioning is a backend concern. PLACE rectangles never overlap, therefore the capacity ordering below cannot change page pixels.</remarks>
    public static class AtlasBakerPageRecipePartitioner
    {
        /// <summary>Creates deterministic cumulative segments for the supplied host capability.</summary>
        public static bool TryCreate(AtlasBakerPagePlan page, TextureGpuCapability capability, out IReadOnlyList<AtlasBakerPageRecipePartition> partitions, out StackMachineDiagnostic diagnostic)
        {
            partitions = null;
            if (page == null || page.Extent <= 0 || page.Operations == null || page.Operations.Count == 0) return Reject("AtlasBakerPageInvalid", "Atlas page partitioning requires a non-empty page plan.", out diagnostic);
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(capability.FixedGridEdge) || capability.FixedGridEdge < page.Extent) return Reject("AtlasPageExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain the Atlas page.", out diagnostic);
            var places = new List<AtlasBakerPageOperation>();
            AtlasBakerPageOperation fill = null;
            for (int i = 0; i < page.Operations.Count; i++)
            {
                AtlasBakerPageOperation operation = page.Operations[i];
                if (operation == null) return Reject("AtlasBakerPageOperationInvalid", "Atlas page contains a null operation.", out diagnostic);
                if (operation.Kind == AtlasBakerPageOperationKind.FillOut) { if (i != 0 || fill != null) return Reject("AtlasBakerFillOrderInvalid", "Atlas FILL_OUT must be the first and only fill operation.", out diagnostic); fill = operation; }
                else if (operation.Kind == AtlasBakerPageOperationKind.Place && operation.Source != null) places.Add(operation);
                else return Reject("AtlasBakerPlaceInvalid", "Atlas PLACE requires a source Texture.", out diagnostic);
            }
            if (fill == null) return Reject("AtlasBakerFillRequired", "Atlas page partitioning requires an initial FILL_OUT operation.", out diagnostic);
            places.Sort(ComparePlaces);
            var values = new List<AtlasBakerPageRecipePartition>();
            var current = new List<AtlasBakerPageOperation> { fill };
            var allocator = NewAllocator(page.Extent, capability.FixedGridEdge);
            for (int i = 0; i < places.Count; i++)
            {
                AtlasBakerPageOperation place = places[i];
                bool reserved = allocator.TryReserve(place.Source.width, place.Source.height, out _);
                if (!reserved)
                {
                    values.Add(new AtlasBakerPageRecipePartition(current.ToArray(), values.Count == 0));
                    current.Clear();
                    allocator = NewAllocator(page.Extent, capability.FixedGridEdge);
                    reserved = allocator.TryReserve(place.Source.width, place.Source.height, out _);
                }
                if (!reserved) return Reject("AtlasSourceExceedsFixedGrid", "Texture StackMachine fixed grid cannot contain the Atlas page and one source texture.", out diagnostic);
                current.Add(place);
            }
            values.Add(new AtlasBakerPageRecipePartition(current.ToArray(), values.Count == 0));
            partitions = values.AsReadOnly(); diagnostic = null; return true;
        }

        private static TextureHallAllocator NewAllocator(int extent, int gridEdge) { var allocator = new TextureHallAllocator(gridEdge); allocator.TryReserve(extent, extent, out _); return allocator; }
        private static int ComparePlaces(AtlasBakerPageOperation left, AtlasBakerPageOperation right) { long la = (long)left.Source.width * left.Source.height, ra = (long)right.Source.width * right.Source.height; int area = ra.CompareTo(la); if (area != 0) return area; int registry = string.CompareOrdinal(left.MaterialId.RegistryId, right.MaterialId.RegistryId); return registry != 0 ? registry : string.CompareOrdinal(left.MaterialId.EntryId, right.MaterialId.EntryId); }
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message); return false; }
    }
}
