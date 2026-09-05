// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Compares an in-memory atlas page against a test-generated reference without invoking the Baker or TSM.</summary>
    internal static class AtlasImageOracle
    {
        internal readonly struct PixelTolerance
        {
            internal PixelTolerance(float linearRelative, float srgbAbsolute) { LinearRelative = linearRelative; SrgbAbsolute = srgbAbsolute; }
            internal float LinearRelative { get; }
            internal float SrgbAbsolute { get; }
        }

        internal readonly struct Probe
        {
            internal Probe(int x, int y, Color expected) { X = x; Y = y; Expected = expected; }
            internal int X { get; }
            internal int Y { get; }
            internal Color Expected { get; }
        }

        internal readonly struct Comparison
        {
            internal Comparison(float maxAbsoluteError, int exceededPixelCount, int pixelCount) { MaxAbsoluteError = maxAbsoluteError; ExceededPixelCount = exceededPixelCount; PixelCount = pixelCount; }
            internal float MaxAbsoluteError { get; }
            internal int ExceededPixelCount { get; }
            internal int PixelCount { get; }
            internal float ExceededPixelRatio => PixelCount == 0 ? 0f : (float)ExceededPixelCount / PixelCount;
        }

        internal static bool TryCompare(RenderTexture actual, Texture2D reference, IReadOnlyList<Probe> probes, AtlasTextureSemantic semantic, bool linear, PixelTolerance tolerance, out Comparison comparison, out StackMachineDiagnostic diagnostic)
        {
            comparison = default; diagnostic = null;
            if (actual == null || reference == null || probes == null || probes.Count == 0 || (semantic != AtlasTextureSemantic.BaseColor && semantic != AtlasTextureSemantic.Normal) || tolerance.LinearRelative < 0f || tolerance.SrgbAbsolute < 0f) return Fail("AtlasImageOracleInputInvalid", out diagnostic);
            if (actual.width != reference.width || actual.height != reference.height) return Fail("AtlasImageOracleExtentMismatch", out diagnostic);
            return TryComparePixels(actual.width, actual.height, Readback(actual), reference.GetPixels(), probes, linear, tolerance, out comparison, out diagnostic);
        }

        /// <summary>Compares caller-read pixels, keeping PlayMode GPU readback outside the Oracle.</summary>
        internal static bool TryComparePixels(int width, int height, IReadOnlyList<Color> actual, IReadOnlyList<Color> expected, IReadOnlyList<Probe> probes, bool linear, PixelTolerance tolerance, out Comparison comparison, out StackMachineDiagnostic diagnostic)
        {
            comparison = default; diagnostic = null;
            if (width <= 0 || height <= 0 || actual == null || expected == null || probes == null || probes.Count == 0 || actual.Count != width * height || expected.Count != width * height || tolerance.LinearRelative < 0f || tolerance.SrgbAbsolute < 0f) return Fail("AtlasImageOracleInputInvalid", out diagnostic);
            float maxError = 0f; int exceeded = 0;
            for (int i = 0; i < actual.Count; i++)
            {
                float error = MaxAbsolute(actual[i], expected[i]); if (error > maxError) maxError = error;
                if (!Within(actual[i], expected[i], linear, tolerance)) exceeded++;
            }
            comparison = new Comparison(maxError, exceeded, actual.Count);
            foreach (Probe probe in probes)
            {
                if (probe.X < 0 || probe.Y < 0 || probe.X >= width || probe.Y >= height || !BitsEqual(actual[probe.Y * width + probe.X], probe.Expected)) return Fail("AtlasImageOracleProbeMismatch", out diagnostic);
            }
            return exceeded == 0 || Fail("AtlasImageOraclePixelToleranceExceeded", out diagnostic);
        }

        private static Color[] Readback(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active; var readback = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = source; readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); readback.Apply(false, false); return readback.GetPixels(); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
        }

        private static bool Within(Color actual, Color expected, bool linear, PixelTolerance tolerance)
        {
            return Within(actual.r, expected.r, linear, tolerance) && Within(actual.g, expected.g, linear, tolerance) && Within(actual.b, expected.b, linear, tolerance) && Within(actual.a, expected.a, linear, tolerance);
        }
        private static bool Within(float actual, float expected, bool linear, PixelTolerance tolerance) { return Mathf.Abs(actual - expected) <= (linear ? tolerance.LinearRelative * Mathf.Max(Mathf.Abs(expected), 0.0001f) : tolerance.SrgbAbsolute); }
        private static bool BitsEqual(Color actual, Color expected) { return BitConverter.SingleToInt32Bits(actual.r) == BitConverter.SingleToInt32Bits(expected.r) && BitConverter.SingleToInt32Bits(actual.g) == BitConverter.SingleToInt32Bits(expected.g) && BitConverter.SingleToInt32Bits(actual.b) == BitConverter.SingleToInt32Bits(expected.b) && BitConverter.SingleToInt32Bits(actual.a) == BitConverter.SingleToInt32Bits(expected.a); }
        private static float MaxAbsolute(Color a, Color b) { return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)); }
        private static bool Fail(string code, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, "Atlas image Oracle rejected its input."); return false; }
    }
}
