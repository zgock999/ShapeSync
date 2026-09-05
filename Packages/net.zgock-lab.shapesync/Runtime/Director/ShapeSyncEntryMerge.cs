// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Compiler-local context retained for one physical Shape entry.</summary>
    public sealed class ShapeSyncMergedEntry
    {
        /// <summary>Initializes compiler-local context for one physical entry.</summary>
        /// <param name="entry">The physical entry contributed by a resolved Shape.</param>
        /// <param name="priority">The source Shape composition priority.</param>
        /// <param name="shapeId">The source Shape logical identity.</param>
        /// <param name="listPosition">The entry's position in its source parts list.</param>
        public ShapeSyncMergedEntry(ShapeEntry entry, int priority, string shapeId, int listPosition)
        { Entry = entry; Priority = priority; ShapeId = shapeId; ListPosition = listPosition; }

        /// <summary>Gets the physical entry.</summary>
        public ShapeEntry Entry { get; }
        /// <summary>Gets the source Shape priority.</summary>
        public int Priority { get; }
        /// <summary>Gets the source Shape identity.</summary>
        public string ShapeId { get; }
        /// <summary>Gets the entry position within the source Shape parts list.</summary>
        public int ListPosition { get; }
    }

    /// <summary>Directly partitions resolved physical Shapes into Mesh and Material compiler inputs.</summary>
    public static class ShapeSyncEntryMerge
    {
        /// <summary>Partitions non-Morph physical Shapes without resolving any Binding or scene object.</summary>
        /// <param name="physicalShapes">Resolved physical Shapes in composition order.</param>
        /// <param name="meshEntries">Mesh-domain entries on success.</param>
        /// <param name="materialEntries">Material-domain entries in Material compile order on success.</param>
        /// <param name="diagnostic">A structured reject for an unsupported or ambiguous entry.</param>
        /// <returns><see langword="true"/> when the inputs were partitioned without ownership resolution.</returns>
        public static bool TryMerge(IReadOnlyList<ShapeSyncShape> physicalShapes, out List<ShapeSyncMergedEntry> meshEntries, out List<ShapeSyncMergedEntry> materialEntries, out StackMachineDiagnostic diagnostic)
        {
            meshEntries = new List<ShapeSyncMergedEntry>();
            materialEntries = new List<ShapeSyncMergedEntry>();
            diagnostic = null;
            if (physicalShapes == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "PhysicalShapesRequired", "Entry Merge requires resolved physical Shapes.");
                return false;
            }

            for (int shapeIndex = 0; shapeIndex < physicalShapes.Count; shapeIndex++)
            {
                if (!(physicalShapes[shapeIndex] is PartsShape shape)) continue;
                IReadOnlyList<ShapeEntry> parts = shape.Parts;
                for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    ShapeEntry entry = parts[partIndex];
                    if (entry == null)
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("director", "ShapeEntryRequired", "Entry Merge cannot compile a null ShapeEntry.", detail: shape.ShapeId + ":" + partIndex);
                        return false;
                    }
                    var merged = new ShapeSyncMergedEntry(entry, shape.Priority, shape.ShapeId, partIndex);
                    if (entry is MeshEntry) meshEntries.Add(merged);
                    else if (entry is MaterialEntry) materialEntries.Add(merged);
                    else
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("director", "UnsupportedShapeEntry", "Entry Merge received an unsupported ShapeEntry type.", detail: entry.GetType().Name);
                        return false;
                    }
                }
            }

            materialEntries.Sort(CompareMaterial);
            return TryValidateMaterialSemantics(materialEntries, out diagnostic);
        }

        private static int CompareMaterial(ShapeSyncMergedEntry left, ShapeSyncMergedEntry right)
        {
            var leftEntry = (MaterialEntry)left.Entry;
            var rightEntry = (MaterialEntry)right.Entry;
            int result = string.CompareOrdinal(leftEntry.RegistryId, rightEntry.RegistryId);
            if (result != 0) return result;
            result = string.CompareOrdinal(leftEntry.ProxyEntry, rightEntry.ProxyEntry);
            if (result != 0) return result;
            result = MaterialTypeOrder(leftEntry).CompareTo(MaterialTypeOrder(rightEntry));
            if (result != 0) return result;
            result = left.Priority.CompareTo(right.Priority);
            if (result != 0) return result;
            result = left.ListPosition.CompareTo(right.ListPosition);
            return result != 0 ? result : string.CompareOrdinal(left.ShapeId, right.ShapeId);
        }

        private static int MaterialTypeOrder(MaterialEntry entry) => entry is TextureEntry ? 0 : entry is ColorEntry ? 1 : entry is UvsetEntry ? 2 : int.MaxValue;

        private static bool TryValidateMaterialSemantics(List<ShapeSyncMergedEntry> materialEntries, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var colors = new HashSet<string>();
            var uvsets = new HashSet<string>();
            for (int i = 0; i < materialEntries.Count; i++)
            {
                var entry = (MaterialEntry)materialEntries[i].Entry;
                string key = entry.RegistryId + "\n" + entry.ProxyEntry;
                if (entry is ColorEntry && !colors.Add(key))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateColorEntry", "A Material target may contain only one ColorEntry.", bindingName: entry.ProxyEntry);
                    return false;
                }
                if (entry is UvsetEntry && !uvsets.Add(key))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateUvsetEntry", "A Material target may contain only one UvsetEntry.", bindingName: entry.ProxyEntry);
                    return false;
                }
            }
            return true;
        }
    }
}
