// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Tests.Spec18;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class PlayModeAtlasImageOracleTests
    {
        [UnityTest]
        public IEnumerator TryComparePixels_UsesAsyncGpuReadbackAndAcceptsExactPixels()
        {
            RenderTexture actual = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            Texture2D reference = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] expected = { Color.red, Color.green, Color.blue, Color.white };
                reference.SetPixels(expected); reference.Apply(false, false);
                Graphics.Blit(reference, actual);
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(actual, 0, TextureFormat.RGBAFloat);
                yield return new WaitUntil(() => request.done);
                Assert.That(request.hasError, Is.False);
                Assert.That(AtlasImageOracle.TryComparePixels(2, 2, request.GetData<Color>().ToArray(), expected, new[] { new AtlasImageOracle.Probe(0, 0, Color.red), new AtlasImageOracle.Probe(1, 1, Color.white) }, true, AtlasOracleTolerances.Default, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(comparison.ExceededPixelCount, Is.Zero);
            }
            finally { Object.Destroy(actual); Object.Destroy(reference); }
        }

        [UnityTest]
        public IEnumerator TryComparePixels_UsesAsyncGpuReadbackAndRejectsWrongPixel()
        {
            RenderTexture actual = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            Texture2D reference = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                Color[] expected = { Color.red, Color.green, Color.blue, Color.white };
                reference.SetPixels(expected); reference.Apply(false, false);
                Graphics.Blit(reference, actual);
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(actual, 0, TextureFormat.RGBAFloat);
                yield return new WaitUntil(() => request.done);
                Assert.That(request.hasError, Is.False);
                Color[] wrong = (Color[])expected.Clone(); wrong[3] = Color.black;
                Assert.That(AtlasImageOracle.TryComparePixels(2, 2, request.GetData<Color>().ToArray(), wrong, new[] { new AtlasImageOracle.Probe(1, 1, Color.black) }, true, AtlasOracleTolerances.Default, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasImageOracleProbeMismatch"));
            }
            finally { Object.Destroy(actual); Object.Destroy(reference); }
        }

        [UnityTest]
        public IEnumerator TryValidatePixels_UsesAsyncGpuReadbackForMetamorphicSampling()
        {
            RenderTexture source = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            RenderTexture atlas = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            Texture2D reference = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                reference.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); reference.Apply(false, false);
                Graphics.Blit(reference, source); Graphics.Blit(reference, atlas);
                AsyncGPUReadbackRequest sourceRequest = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBAFloat);
                AsyncGPUReadbackRequest atlasRequest = AsyncGPUReadback.Request(atlas, 0, TextureFormat.RGBAFloat);
                yield return new WaitUntil(() => sourceRequest.done && atlasRequest.done);
                Assert.That(sourceRequest.hasError || atlasRequest.hasError, Is.False);
                var cell = new AtlasImageMetamorphicOracle.SamplingCell(0, 0, 2, 2, 0);
                Vector2[] samples = { new Vector2(.25f, .25f), new Vector2(.75f, .75f) };
                Assert.That(AtlasImageMetamorphicOracle.TryValidatePixels(2, 2, sourceRequest.GetData<Color>().ToArray(), 2, 2, atlasRequest.GetData<Color>().ToArray(), cell, 2, Vector2.one, Vector2.zero, samples, samples, AtlasTextureSemantic.BaseColor, true, AtlasOracleTolerances.Default, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(comparison.ExceededPixelCount, Is.Zero);
            }
            finally { Object.Destroy(source); Object.Destroy(atlas); Object.Destroy(reference); }
        }

        [UnityTest]
        public IEnumerator TryValidatePixels_UsesAsyncGpuReadbackAndRejectsWrongAtlas()
        {
            RenderTexture source = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            RenderTexture atlas = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat);
            Texture2D sourceReference = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            Texture2D wrongAtlasReference = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                sourceReference.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); sourceReference.Apply(false, false);
                wrongAtlasReference.SetPixels(new[] { Color.black, Color.black, Color.black, Color.black }); wrongAtlasReference.Apply(false, false);
                Graphics.Blit(sourceReference, source); Graphics.Blit(wrongAtlasReference, atlas);
                AsyncGPUReadbackRequest sourceRequest = AsyncGPUReadback.Request(source, 0, TextureFormat.RGBAFloat);
                AsyncGPUReadbackRequest atlasRequest = AsyncGPUReadback.Request(atlas, 0, TextureFormat.RGBAFloat);
                yield return new WaitUntil(() => sourceRequest.done && atlasRequest.done);
                Assert.That(sourceRequest.hasError || atlasRequest.hasError, Is.False);
                var cell = new AtlasImageMetamorphicOracle.SamplingCell(0, 0, 2, 2, 0);
                Vector2[] samples = { new Vector2(.25f, .25f), new Vector2(.75f, .75f) };
                Assert.That(AtlasImageMetamorphicOracle.TryValidatePixels(2, 2, sourceRequest.GetData<Color>().ToArray(), 2, 2, atlasRequest.GetData<Color>().ToArray(), cell, 2, Vector2.one, Vector2.zero, samples, samples, AtlasTextureSemantic.BaseColor, true, AtlasOracleTolerances.Default, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(comparison.ExceededPixelCount, Is.GreaterThan(0));
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasImageMetamorphicMismatch"));
            }
            finally { Object.Destroy(source); Object.Destroy(atlas); Object.Destroy(sourceReference); Object.Destroy(wrongAtlasReference); }
        }
    }
}
