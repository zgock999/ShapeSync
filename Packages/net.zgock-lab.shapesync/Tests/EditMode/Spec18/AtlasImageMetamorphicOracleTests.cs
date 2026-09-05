// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasImageMetamorphicOracleTests
    {
        private static readonly AtlasImageOracle.PixelTolerance Tolerance = AtlasOracleTolerances.Default;
        private static readonly Vector2[] Samples = { new Vector2(.1f,.1f), new Vector2(.2f,.8f), new Vector2(.5f,.5f), new Vector2(.8f,.2f), new Vector2(.9f,.9f) };

        [Test]
        public void TryValidate_AcceptsBaseColorAndNormalSourceToAtlasSampling()
        {
            var cell = new AtlasLayoutCell(new MaterialId("r", "a"), 0, 128, 128, 256, 256, 0); Texture2D baseSource = Gradient(new Color(.1f,.2f,.8f,1f),256); Texture2D normalSource = Gradient(new Color(.5f,.5f,1f,1f),256); RenderTexture baseAtlas = Atlas(baseSource, cell,512); RenderTexture normalAtlas = Atlas(normalSource, cell,512);
            Vector2[] atlasUvs=AtlasUvs(cell,512,Vector2.one,Vector2.zero); Assert.That(AtlasImageMetamorphicOracle.TryValidate(baseSource, baseAtlas, cell, 512, Vector2.one, Vector2.zero, Samples, atlasUvs, AtlasTextureSemantic.BaseColor, true, Tolerance, out var baseResult, out var baseDiagnostic), Is.True, $"{baseDiagnostic?.domainCode}: {baseDiagnostic?.message}; max={baseResult.MaxAbsoluteError}; rate={baseResult.ExceededPixelRatio}"); Assert.That(baseResult.ExceededPixelRatio, Is.Zero);
            Assert.That(AtlasImageMetamorphicOracle.TryValidate(normalSource, normalAtlas, cell, 512, Vector2.one, Vector2.zero, Samples, atlasUvs, AtlasTextureSemantic.Normal, false, Tolerance, out var normalResult, out var normalDiagnostic), Is.True, normalDiagnostic?.message); Assert.That(normalResult.ExceededPixelRatio, Is.Zero);
            RenderTexture renderTextureSource = SourceRenderTexture(baseSource); Assert.That(AtlasImageMetamorphicOracle.TryValidate(renderTextureSource, baseAtlas, cell, 512, Vector2.one, Vector2.zero, Samples, atlasUvs, AtlasTextureSemantic.BaseColor, true, Tolerance, out var renderTextureResult, out var renderTextureDiagnostic), Is.True, renderTextureDiagnostic?.message); Assert.That(renderTextureResult.ExceededPixelRatio, Is.Zero);
            Release(baseSource, baseAtlas); Release(normalSource, normalAtlas); Release(renderTextureSource);
        }

        [Test]
        public void TryValidate_RejectsMismatchedAtlasAndInvalidInput()
        {
            var cell = new AtlasLayoutCell(new MaterialId("r", "a"), 0, 4, 4, 8, 8, 0); Texture2D source = Gradient(Color.red); Texture2D wrong = Gradient(Color.green); RenderTexture atlas = Atlas(wrong, cell);
            Vector2[] atlasUvs=AtlasUvs(cell,16,Vector2.one,Vector2.zero); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source, atlas, cell, 16, Vector2.one, Vector2.zero, Samples, atlasUvs, AtlasTextureSemantic.BaseColor, true, Tolerance, out var mismatch, out var mismatchDiagnostic), Is.False); Assert.That(mismatchDiagnostic.domainCode, Is.EqualTo("AtlasImageMetamorphicMismatch")); Assert.That(mismatch.ExceededPixelRatio, Is.EqualTo(1f));
            Assert.That(AtlasImageMetamorphicOracle.TryValidate(source, atlas, cell, 16, Vector2.one, Vector2.zero, new Vector2[0], new Vector2[0], AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out var invalid), Is.False); Assert.That(invalid.domainCode, Is.EqualTo("AtlasImageMetamorphicInputInvalid")); Release(source, atlas); UnityEngine.Object.DestroyImmediate(wrong);
        }

        [Test]
        public void TryValidate_AcceptsNonIdentityUvsetAndGutter_AndRejectsWrongTransform()
        {
            var cell = new AtlasLayoutCell(new MaterialId("r", "gutter"), 0, 128, 128, 256, 256, 4); Vector2 scale = new Vector2(.5f,.5f), offset = new Vector2(.25f,.25f); Texture2D source = SmoothGradient(new Color(.5f,.5f,.75f,1f)); RenderTexture atlas = Atlas(source,cell,512);
            Vector2[] atlasUvs=AtlasUvs(cell,512,scale,offset); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,512,scale,offset,Samples,atlasUvs,AtlasTextureSemantic.BaseColor,true,Tolerance,out var valid,out var validDiagnostic),Is.True,validDiagnostic?.message); Assert.That(valid.ExceededPixelRatio,Is.Zero);
            Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,512,scale,offset,Samples,AtlasUvs(cell,512,Vector2.one,Vector2.zero),AtlasTextureSemantic.BaseColor,true,Tolerance,out _,out var wrongUvset),Is.False,"wrong UVSET must reject"); Assert.That(wrongUvset.domainCode,Is.EqualTo("AtlasImageMetamorphicMismatch"));
            var wrongCell = new AtlasLayoutCell(new MaterialId("r", "gutter"),0,0,128,256,256,4); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,wrongCell,512,scale,offset,Samples,AtlasUvs(wrongCell,512,scale,offset),AtlasTextureSemantic.BaseColor,true,Tolerance,out _,out var wrongCellDiagnostic),Is.False,"wrong cell must reject"); Assert.That(wrongCellDiagnostic.domainCode,Is.EqualTo("AtlasImageMetamorphicMismatch")); Release(source,atlas);
        }

        [Test]
        public void SampleBilinearClamp_UsesKnownTexelCenterValues()
        {
            var source = new Texture2D(2,2,TextureFormat.RGBAFloat,false,true); source.SetPixels(new[]{Color.red,Color.green,Color.blue,Color.white}); source.Apply(false,false);
            Assert.That(AtlasImageMetamorphicOracle.SampleBilinearClamp(source,new Vector2(.25f,.25f)),Is.EqualTo(Color.red)); Assert.That(AtlasImageMetamorphicOracle.SampleBilinearClamp(source,new Vector2(.75f,.75f)),Is.EqualTo(Color.white)); Assert.That(AtlasImageMetamorphicOracle.SampleBilinearClamp(source,new Vector2(.5f,.5f)),Is.EqualTo(new Color(.5f,.5f,.5f,1f))); UnityEngine.Object.DestroyImmediate(source);
        }

        [Test]
        public void TryValidate_RejectsInvalidAtlasUvContract()
        {
            var cell=new AtlasLayoutCell(new MaterialId("r","a"),0,4,4,8,8,0); Texture2D source=Gradient(Color.red); RenderTexture atlas=Atlas(source,cell); Vector2[] valid=AtlasUvs(cell,16,Vector2.one,Vector2.zero);
            Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,16,Vector2.one,Vector2.zero,Samples,new[]{valid[0]},AtlasTextureSemantic.BaseColor,true,Tolerance,out _,out var count),Is.False); Assert.That(count.domainCode,Is.EqualTo("AtlasImageMetamorphicInputInvalid"));
            valid[0]=new Vector2(float.NaN,.5f); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,16,Vector2.one,Vector2.zero,Samples,valid,AtlasTextureSemantic.BaseColor,true,Tolerance,out _,out var nonFinite),Is.False); Assert.That(nonFinite.domainCode,Is.EqualTo("AtlasImageMetamorphicInputInvalid"));
            valid[0]=new Vector2(.01f,.5f); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,16,Vector2.one,Vector2.zero,Samples,valid,AtlasTextureSemantic.BaseColor,true,Tolerance,out _,out var outside),Is.False); Assert.That(outside.domainCode,Is.EqualTo("AtlasImageMetamorphicInputInvalid")); Release(source,atlas);
        }

        private static Texture2D Gradient(Color bias, int size = 8) { var texture = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true); var pixels = new Color[size*size]; for (int y=0;y<size;y++) for (int x=0;x<size;x++) pixels[y*size+x] = new Color(Mathf.Clamp01(bias.r + x/(size*4f)), Mathf.Clamp01(bias.g + y/(size*4f)), bias.b, 1f); texture.SetPixels(pixels); texture.Apply(false,false); return texture; }
        private static Texture2D SmoothGradient(Color bias) { var texture = new Texture2D(256,256,TextureFormat.RGBAFloat,false,true); var pixels = new Color[256*256]; for (int y=0;y<256;y++) for (int x=0;x<256;x++) pixels[y*256+x] = new Color(bias.r+x/(256f*16f),bias.g+y/(256f*16f),bias.b,1f); texture.SetPixels(pixels); texture.Apply(false,false); return texture; }
        private static Vector2[] AtlasUvs(AtlasLayoutCell cell,int extent,Vector2 scale,Vector2 offset) { var result=new Vector2[Samples.Length]; for(int i=0;i<result.Length;i++) result[i]=AtlasUvTransform.Apply(Samples[i],scale,offset,cell,extent); return result; }
        private static RenderTexture Atlas(Texture2D source, AtlasLayoutCell cell, int extent = 16) { var page = new Texture2D(extent,extent,TextureFormat.RGBAFloat,false,true); page.SetPixels(new Color[extent*extent]); int width=cell.Width-cell.Gutter*2, height=cell.Height-cell.Gutter*2; for (int y=0;y<height;y++) for (int x=0;x<width;x++) page.SetPixel(cell.X+cell.Gutter+x,cell.Y+cell.Gutter+y,AtlasImageMetamorphicOracle.SampleBilinearClamp(source,new Vector2((float)x/(width-1),(float)y/(height-1)))); page.Apply(false,false); var atlas = new RenderTexture(extent,extent,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); atlas.Create(); Graphics.CopyTexture(page, atlas); UnityEngine.Object.DestroyImmediate(page); return atlas; }
        private static RenderTexture SourceRenderTexture(Texture2D source) { var result = new RenderTexture(source.width,source.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); result.Create(); Graphics.Blit(source,result); return result; }
        private static void Release(Texture2D source, RenderTexture atlas) { UnityEngine.Object.DestroyImmediate(source); if (RenderTexture.active==atlas) RenderTexture.active=null; atlas.Release(); UnityEngine.Object.DestroyImmediate(atlas); }
        private static void Release(RenderTexture texture) { if (RenderTexture.active==texture) RenderTexture.active=null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
    }
}
