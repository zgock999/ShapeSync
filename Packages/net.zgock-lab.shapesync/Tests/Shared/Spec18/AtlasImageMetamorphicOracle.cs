// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Validates the source-UV to atlas-UV sampling invariant without executing the Baker or TSM.</summary>
    internal static class AtlasImageMetamorphicOracle
    {
        internal sealed class SamplingCell
        {
            internal SamplingCell(int x, int y, int width, int height, int gutter) { X = x; Y = y; Width = width; Height = height; Gutter = gutter; }
            internal int X { get; }
            internal int Y { get; }
            internal int Width { get; }
            internal int Height { get; }
            internal int Gutter { get; }
        }

        internal static bool TryValidate(Texture source, RenderTexture atlas, AtlasLayoutCell cell, int pageExtent, Vector2 sourceUvSetScale, Vector2 sourceUvSetOffset, IReadOnlyList<Vector2> oldUvs, IReadOnlyList<Vector2> atlasUvs, AtlasTextureSemantic semantic, bool linear, AtlasImageOracle.PixelTolerance tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic)
        {
            comparison = default; diagnostic = null;
            if (source == null || !(source is Texture2D) && !(source is RenderTexture) || atlas == null || cell == null || oldUvs == null || atlasUvs == null || oldUvs.Count == 0 || oldUvs.Count != atlasUvs.Count || pageExtent != atlas.width || pageExtent != atlas.height || (semantic != AtlasTextureSemantic.BaseColor && semantic != AtlasTextureSemantic.Normal)) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
            float minX=(cell.X+cell.Gutter+.5f)/pageExtent, minY=(cell.Y+cell.Gutter+.5f)/pageExtent, maxX=(cell.X+cell.Width-cell.Gutter-.5f)/pageExtent, maxY=(cell.Y+cell.Height-cell.Gutter-.5f)/pageExtent;
            if (!Finite(sourceUvSetScale) || !Finite(sourceUvSetOffset) || minX>maxX || minY>maxY) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
            for(int i=0;i<oldUvs.Count;i++) if(!Finite(oldUvs[i]) || !Finite(atlasUvs[i]) || atlasUvs[i].x<minX || atlasUvs[i].x>maxX || atlasUvs[i].y<minY || atlasUvs[i].y>maxY) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
            Texture2D sourceReadback = source as Texture2D; bool destroySourceReadback = false; if (sourceReadback == null) { sourceReadback = Readback((RenderTexture)source); destroySourceReadback = true; } Texture2D readback = Readback(atlas);
            try { return TryValidatePixels(sourceReadback.width, sourceReadback.height, sourceReadback.GetPixels(), readback.width, readback.height, readback.GetPixels(), new SamplingCell(cell.X, cell.Y, cell.Width, cell.Height, cell.Gutter), pageExtent, sourceUvSetScale, sourceUvSetOffset, oldUvs, atlasUvs, semantic, linear, tolerance, out comparison, out diagnostic); }
            finally { UnityEngine.Object.DestroyImmediate(readback); if (destroySourceReadback) UnityEngine.Object.DestroyImmediate(sourceReadback); }
        }

        /// <summary>Validates caller-read pixels, so PlayMode callers can use AsyncGPUReadback.</summary>
        internal static bool TryValidatePixels(int sourceWidth, int sourceHeight, IReadOnlyList<Color> sourcePixels, int atlasWidth, int atlasHeight, IReadOnlyList<Color> atlasPixels, SamplingCell cell, int pageExtent, Vector2 sourceUvSetScale, Vector2 sourceUvSetOffset, IReadOnlyList<Vector2> oldUvs, IReadOnlyList<Vector2> atlasUvs, AtlasTextureSemantic semantic, bool linear, AtlasImageOracle.PixelTolerance tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic)
        {
            comparison = default; diagnostic = null;
            if (sourceWidth <= 0 || sourceHeight <= 0 || sourcePixels == null || sourcePixels.Count != sourceWidth * sourceHeight || atlasWidth <= 0 || atlasHeight <= 0 || atlasPixels == null || atlasPixels.Count != atlasWidth * atlasHeight || cell == null || oldUvs == null || atlasUvs == null || oldUvs.Count == 0 || oldUvs.Count != atlasUvs.Count || pageExtent != atlasWidth || pageExtent != atlasHeight || (semantic != AtlasTextureSemantic.BaseColor && semantic != AtlasTextureSemantic.Normal)) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
            float minX=(cell.X+cell.Gutter+.5f)/pageExtent, minY=(cell.Y+cell.Gutter+.5f)/pageExtent, maxX=(cell.X+cell.Width-cell.Gutter-.5f)/pageExtent, maxY=(cell.Y+cell.Height-cell.Gutter-.5f)/pageExtent;
            if (!Finite(sourceUvSetScale) || !Finite(sourceUvSetOffset) || minX>maxX || minY>maxY) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
            float max = 0f; int exceeded = 0;
            for(int i=0;i<oldUvs.Count;i++)
            {
                if(!Finite(oldUvs[i]) || !Finite(atlasUvs[i]) || atlasUvs[i].x<minX || atlasUvs[i].x>maxX || atlasUvs[i].y<minY || atlasUvs[i].y>maxY) return Fail("AtlasImageMetamorphicInputInvalid", out diagnostic);
                Vector2 sourceUv = Vector2.Scale(oldUvs[i], sourceUvSetScale) + sourceUvSetOffset; Color expected = SampleBilinearClamp(sourceWidth, sourceHeight, sourcePixels, sourceUv); Color actual = SampleBilinearClamp(atlasWidth, atlasHeight, atlasPixels, atlasUvs[i]); float error = MaxAbsolute(expected, actual); if (error > max) max = error;
                if (!Within(expected, actual, linear, tolerance)) exceeded++;
            }
            comparison = new AtlasImageOracle.Comparison(max, exceeded, oldUvs.Count);
            return exceeded == 0 || Fail("AtlasImageMetamorphicMismatch", out diagnostic);
        }

        internal static Color SampleBilinearClamp(Texture2D texture, Vector2 uv)
        {
            float x = Mathf.Clamp01(uv.x) * texture.width - .5f, y = Mathf.Clamp01(uv.y) * texture.height - .5f; int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, texture.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, texture.height - 1), x1 = Mathf.Min(x0 + 1, texture.width - 1), y1 = Mathf.Min(y0 + 1, texture.height - 1); return Color.Lerp(Color.Lerp(texture.GetPixel(x0, y0), texture.GetPixel(x1, y0), x - Mathf.Floor(x)), Color.Lerp(texture.GetPixel(x0, y1), texture.GetPixel(x1, y1), x - Mathf.Floor(x)), y - Mathf.Floor(y));
        }
        private static Color SampleBilinearClamp(int width, int height, IReadOnlyList<Color> pixels, Vector2 uv) { float x = Mathf.Clamp01(uv.x) * width - .5f, y = Mathf.Clamp01(uv.y) * height - .5f; int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, height - 1), x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1); return Color.Lerp(Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], x - Mathf.Floor(x)), Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], x - Mathf.Floor(x)), y - Mathf.Floor(y)); }
        private static Texture2D Readback(RenderTexture source) { RenderTexture previous = RenderTexture.active; var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true); try { RenderTexture.active = source; copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); copy.Apply(false, false); return copy; } finally { RenderTexture.active = previous; } }
        private static bool Within(Color expected, Color actual, bool linear, AtlasImageOracle.PixelTolerance tolerance) { return Within(expected.r, actual.r, linear, tolerance) && Within(expected.g, actual.g, linear, tolerance) && Within(expected.b, actual.b, linear, tolerance) && Within(expected.a, actual.a, linear, tolerance); }
        private static bool Within(float expected, float actual, bool linear, AtlasImageOracle.PixelTolerance tolerance) { return Mathf.Abs(expected - actual) <= (linear ? tolerance.LinearRelative * Mathf.Max(Mathf.Abs(expected), .0001f) : tolerance.SrgbAbsolute); }
        private static float MaxAbsolute(Color a, Color b) { return Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b), Mathf.Abs(a.a-b.a)); }
        private static bool Finite(Vector2 value) { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y); }
        private static bool Fail(string code, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, "Atlas image metamorphic Oracle rejected its input."); return false; }
    }
}
