// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasImageOracleTests
    {
        private static readonly AtlasImageOracle.PixelTolerance Tolerance = AtlasOracleTolerances.Default;

        [Test]
        public void TryCompare_AcceptsBaseColorAndNormalDisjointCellsWithGutterAndClearRegion()
        {
            Texture2D baseReference = Page(new Color(.8f, .1f, .2f, 1f), new Color(.1f, .3f, .9f, 1f)); RenderTexture baseActual = Target(baseReference);
            Texture2D normalReference = Page(new Color(.5f, .5f, 1f, 1f), new Color(.6f, .4f, 1f, 1f)); RenderTexture normalActual = Target(normalReference);
            var probes = new List<AtlasImageOracle.Probe>(); AddCellProbes(probes, 2, 2, new Color(.8f, .1f, .2f, 1f)); AddCellProbes(probes, 16, 2, new Color(.1f, .3f, .9f, 1f)); probes.Add(new AtlasImageOracle.Probe(0, 0, Color.clear)); probes.Add(new AtlasImageOracle.Probe(14, 8, Color.clear));
            Assert.That(AtlasImageOracle.TryCompare(baseActual, baseReference, probes, AtlasTextureSemantic.BaseColor, true, Tolerance, out var baseResult, out var baseDiagnostic), Is.True, baseDiagnostic?.message); Assert.That(baseResult.ExceededPixelCount, Is.Zero); Assert.That(baseResult.ExceededPixelRatio, Is.Zero); Assert.That(baseResult.MaxAbsoluteError, Is.LessThanOrEqualTo(1e-4f));
            var normalProbes = new List<AtlasImageOracle.Probe>(); AddCellProbes(normalProbes, 2, 2, new Color(.5f, .5f, 1f, 1f)); AddCellProbes(normalProbes, 16, 2, new Color(.6f, .4f, 1f, 1f)); normalProbes.Add(new AtlasImageOracle.Probe(14, 8, Color.clear));
            Assert.That(AtlasImageOracle.TryCompare(normalActual, normalReference, normalProbes, AtlasTextureSemantic.Normal, true, Tolerance, out var normalResult, out var normalDiagnostic), Is.True, normalDiagnostic?.message); Assert.That(normalResult.ExceededPixelCount, Is.Zero);
            Release(baseReference, baseActual); Release(normalReference, normalActual);
        }

        [Test]
        public void TryCompare_RejectsProbeExtentAndPixelToleranceViolations()
        {
            Texture2D reference = Solid(new Color(.5f, .5f, .5f, 1f)); Texture2D near = Solid(new Color(.5004f, .5f, .5f, 1f)); RenderTexture nearActual = Target(near);
            var referenceProbe = new[] { new AtlasImageOracle.Probe(1, 1, new Color(.5f, .5f, .5f, 1f)) }; var actualProbe = new[] { new AtlasImageOracle.Probe(1, 1, new Color(.5004f, .5f, .5f, 1f)) };
            Assert.That(AtlasImageOracle.TryCompare(nearActual, reference, actualProbe, AtlasTextureSemantic.BaseColor, true, Tolerance, out var accepted, out var acceptedDiagnostic), Is.True, acceptedDiagnostic?.message); Assert.That(accepted.ExceededPixelCount, Is.Zero); Assert.That(AtlasImageOracle.TryCompare(nearActual, reference, referenceProbe, AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out var structural), Is.False); Assert.That(structural.domainCode, Is.EqualTo("AtlasImageOracleProbeMismatch"));
            Texture2D far = Solid(new Color(.51f, .5f, .5f, 1f)); RenderTexture farActual = Target(far); var farProbe = new[] { new AtlasImageOracle.Probe(1, 1, new Color(.51f, .5f, .5f, 1f)) }; Assert.That(AtlasImageOracle.TryCompare(farActual, reference, farProbe, AtlasTextureSemantic.BaseColor, true, Tolerance, out var farResult, out var pixels), Is.False); Assert.That(pixels.domainCode, Is.EqualTo("AtlasImageOraclePixelToleranceExceeded")); Assert.That(farResult.ExceededPixelCount, Is.EqualTo(farResult.PixelCount)); Assert.That(farResult.ExceededPixelRatio, Is.EqualTo(1f));
            Texture2D srgb = Solid(new Color(.5f + 1f / 255f, .5f, .5f, 1f)); RenderTexture srgbActual = Target(srgb); var srgbProbe = new[] { new AtlasImageOracle.Probe(1, 1, new Color(.5f + 1f / 255f, .5f, .5f, 1f)) }; Assert.That(AtlasImageOracle.TryCompare(srgbActual, reference, srgbProbe, AtlasTextureSemantic.BaseColor, false, Tolerance, out _, out var srgbDiagnostic), Is.True, srgbDiagnostic?.message);
            RenderTexture wrongExtent = new RenderTexture(3, 3, 0, RenderTextureFormat.ARGBFloat); wrongExtent.Create(); Assert.That(AtlasImageOracle.TryCompare(wrongExtent, reference, referenceProbe, AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out var extent), Is.False); Assert.That(extent.domainCode, Is.EqualTo("AtlasImageOracleExtentMismatch"));
            Release(reference, nearActual); UnityEngine.Object.DestroyImmediate(near); Release(far, farActual); UnityEngine.Object.DestroyImmediate(far); Release(srgb, srgbActual); UnityEngine.Object.DestroyImmediate(srgb); Release(wrongExtent);
        }

        [Test]
        public void TryCompare_RejectsMissingProbeAndInvalidSemanticInput()
        {
            Texture2D reference = Solid(Color.white); RenderTexture actual = Target(reference); var probe = new[] { new AtlasImageOracle.Probe(1, 1, Color.white) };
            Assert.That(AtlasImageOracle.TryCompare(actual, reference, new AtlasImageOracle.Probe[0], AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out var missingProbe), Is.False); Assert.That(missingProbe.domainCode, Is.EqualTo("AtlasImageOracleInputInvalid"));
            Assert.That(AtlasImageOracle.TryCompare(actual, reference, probe, (AtlasTextureSemantic)99, true, Tolerance, out _, out var semantic), Is.False); Assert.That(semantic.domainCode, Is.EqualTo("AtlasImageOracleInputInvalid"));
            Assert.That(AtlasImageOracle.TryCompare(null, reference, probe, AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out var missingActual), Is.False); Assert.That(missingActual.domainCode, Is.EqualTo("AtlasImageOracleInputInvalid")); Release(reference, actual);
        }

        private static Texture2D Page(Color left, Color right)
        {
            var page = new Texture2D(30, 16, TextureFormat.RGBAFloat, false, true); page.SetPixels(new Color[480]); page.Apply(false, false); Texture2D leftSource = Cell(left); Texture2D rightSource = Cell(right); Graphics.CopyTexture(leftSource, 0, 0, 0, 0, 12, 12, page, 0, 0, 2, 2); Graphics.CopyTexture(rightSource, 0, 0, 0, 0, 12, 12, page, 0, 0, 16, 2); UnityEngine.Object.DestroyImmediate(leftSource); UnityEngine.Object.DestroyImmediate(rightSource); return page;
        }
        private static void AddCellProbes(List<AtlasImageOracle.Probe> probes, int x, int y, Color color) { probes.Add(new AtlasImageOracle.Probe(x + 6, y + 6, color)); probes.Add(new AtlasImageOracle.Probe(x + 2, y + 2, color)); probes.Add(new AtlasImageOracle.Probe(x + 9, y + 2, color)); probes.Add(new AtlasImageOracle.Probe(x + 2, y + 9, color)); probes.Add(new AtlasImageOracle.Probe(x + 9, y + 9, color)); for (int i = 0; i < 8; i++) probes.Add(new AtlasImageOracle.Probe(x + 2 + i, y + 2 + i, color)); }
        private static Texture2D Cell(Color color) { var texture = new Texture2D(12, 12, TextureFormat.RGBAFloat, false, true); var pixels = new Color[144]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color; texture.SetPixels(pixels); texture.Apply(false, false); return texture; }
        private static Texture2D Solid(Color color) { var texture = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true); texture.SetPixels(new[] { color, color, color, color, color, color, color, color, color, color, color, color, color, color, color, color }); texture.Apply(false, false); return texture; }
        private static RenderTexture Target(Texture2D reference) { var target = new RenderTexture(reference.width, reference.height, 0, RenderTextureFormat.ARGBFloat); target.Create(); Graphics.Blit(reference, target); return target; }
        private static void Release(Texture2D texture, RenderTexture target) { UnityEngine.Object.DestroyImmediate(texture); Release(target); }
        private static void Release(RenderTexture target) { if (target == null) return; if (RenderTexture.active == target) RenderTexture.active = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); }
    }
}
