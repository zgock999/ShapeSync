// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Pure Phase-0 Atlas UV transform shared by the Baker and Oracle Layer 2.</summary>
    /// <remarks>The caller supplies a validated layout cell and page extent. This method has no state and is the sole owner of the floating-point operation order used for Atlas UVs.</remarks>
    public static class AtlasUvTransform
    {
        /// <summary>Transforms one source UV by UVSET and an Atlas cell, including the half-texel inset.</summary>
        /// <param name="sourceUv">The source mesh UV0.</param>
        /// <param name="uvScale">The resolved Document UVSET scale.</param>
        /// <param name="uvOffset">The resolved Document UVSET offset.</param>
        /// <param name="cell">The solved cell for the Atlas target material.</param>
        /// <param name="pageExtent">The validated square Atlas page edge in texels.</param>
        /// <returns>The linear Atlas UV after applying UVSET and the inset cell mapping. It is inside the cell when the resolved UVSET result is within [0,1].</returns>
        public static Vector2 Apply(Vector2 sourceUv, Vector2 uvScale, Vector2 uvOffset, AtlasLayoutCell cell, int pageExtent)
        {
            Vector2 uv = Vector2.Scale(sourceUv, uvScale) + uvOffset;
            float minX = (cell.X + cell.Gutter + 0.5f) / pageExtent;
            float minY = (cell.Y + cell.Gutter + 0.5f) / pageExtent;
            float maxX = (cell.X + cell.Width - cell.Gutter - 0.5f) / pageExtent;
            float maxY = (cell.Y + cell.Height - cell.Gutter - 0.5f) / pageExtent;
            return new Vector2(minX + (maxX - minX) * uv.x, minY + (maxY - minY) * uv.y);
        }
    }
}
