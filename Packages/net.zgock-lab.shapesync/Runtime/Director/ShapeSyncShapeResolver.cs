// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Pure exclusion resolver that converts requested runtime Shapes into physical composition order.</summary>
    public static class ShapeSyncShapeResolver
    {
        private sealed class RequestedShapeComparer : IComparer<ShapeSyncShape>
        {
            internal static readonly RequestedShapeComparer Instance = new RequestedShapeComparer();

            public int Compare(ShapeSyncShape left, ShapeSyncShape right)
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : string.CompareOrdinal(left.ShapeId, right.ShapeId);
            }
        }

        private sealed class PhysicalShapeComparer : IComparer<ShapeSyncShape>
        {
            internal static readonly PhysicalShapeComparer Instance = new PhysicalShapeComparer();

            public int Compare(ShapeSyncShape left, ShapeSyncShape right)
            {
                int priority = left.Priority.CompareTo(right.Priority);
                return priority != 0 ? priority : string.CompareOrdinal(left.ShapeId, right.ShapeId);
            }
        }

        /// <summary>Resolves tag exclusion and returns physical composition order without mutating the requested list.</summary>
        /// <param name="requestedShapes">The current logical Shape list.</param>
        /// <param name="physicalShapes">The retained Shapes in physical composition order.</param>
        /// <param name="diagnostic">A structured reject when Director cannot resolve its own input unambiguously.</param>
        /// <returns><see langword="true"/> when physical order was resolved.</returns>
        public static bool TryResolve(IReadOnlyList<ShapeSyncShape> requestedShapes, out List<ShapeSyncShape> physicalShapes, out StackMachineDiagnostic diagnostic)
        {
            physicalShapes = new List<ShapeSyncShape>();
            diagnostic = null;
            if (requestedShapes == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapesRequired", "Shape Director requires a runtime Shape list.");
                return false;
            }

            var requested = new List<ShapeSyncShape>(requestedShapes.Count);
            var shapeIds = new HashSet<string>();
            for (int i = 0; i < requestedShapes.Count; i++)
            {
                ShapeSyncShape shape = requestedShapes[i];
                if (shape == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapeRequired", "Shape Director cannot resolve a null runtime Shape.", detail: i.ToString());
                    return false;
                }
                if (!shapeIds.Add(shape.ShapeId))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateShapeId", "Shape Director cannot resolve duplicate ShapeId values.", bindingName: shape.ShapeId);
                    return false;
                }
                requested.Add(shape);
            }

            requested.Sort(RequestedShapeComparer.Instance);
            var occupiedTags = new HashSet<string>();
            for (int i = 0; i < requested.Count; i++)
            {
                ShapeSyncShape shape = requested[i];
                bool excluded = false;
                for (int tagIndex = 0; tagIndex < shape.Tags.Count; tagIndex++)
                {
                    if (occupiedTags.Contains(shape.Tags[tagIndex]))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded) continue;

                physicalShapes.Add(shape);
                for (int tagIndex = 0; tagIndex < shape.Tags.Count; tagIndex++) occupiedTags.Add(shape.Tags[tagIndex]);
            }

            physicalShapes.Sort(PhysicalShapeComparer.Instance);
            return true;
        }
    }
}
