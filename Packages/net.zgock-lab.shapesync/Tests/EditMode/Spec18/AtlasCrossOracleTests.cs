// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasCrossOracleTests
    {
        [Test]
        public void TryValidate_AcceptsCompleteLayerEvidenceAndRejectsWrongPageOrMissingLayer()
        {
            Assert.That(AtlasOracleFixture.TryCreate(Document(Entry("", "body", 0), Entry("outfit", "top", 1)), out AtlasOracleFixture fixture, out StackMachineDiagnostic create), Is.True, create?.message);
            var evidence = new List<AtlasOracleEntryMetadata>(fixture.Metadata);
            Assert.That(AtlasCrossOracle.TryValidate(fixture, evidence, out StackMachineDiagnostic valid), Is.True, valid?.message);
            evidence.RemoveAt(0); Assert.That(AtlasCrossOracle.TryValidate(fixture, evidence, out StackMachineDiagnostic missing), Is.False); Assert.That(missing.domainCode, Is.EqualTo("AtlasCrossOracleInputInvalid"));
            evidence = new List<AtlasOracleEntryMetadata>(fixture.Metadata); AtlasOracleEntryMetadata original=evidence[0]; evidence[0]=new AtlasOracleEntryMetadata(original.SchemaVersion,original.PackingAlgorithm,original.PageExtent,original.FigureIdentity,original.DocumentIdentity,original.MaterialId,original.SourceMaterialIdentity,original.Semantic,original.Layer,original.Participation,new AtlasLayoutCell(original.MaterialId,original.PageIndex+1,original.X,original.Y,original.Width,original.Height,original.Gutter),original.ComparisonMode);
            Assert.That(AtlasCrossOracle.TryValidate(fixture,evidence,out StackMachineDiagnostic mismatch),Is.False); Assert.That(mismatch.domainCode,Is.EqualTo("AtlasCrossOracleContextMismatch"));
            var results=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) results.Add(new AtlasCrossOracle.Evidence(context,true,null,null)); Assert.That(AtlasCrossOracle.TryValidate(fixture,results,out var resultValid),Is.True,resultValid?.message); results[0]=new AtlasCrossOracle.Evidence(fixture.Metadata[0],false,null,null); Assert.That(AtlasCrossOracle.TryValidate(fixture,results,out var missingCause),Is.False); Assert.That(missingCause.domainCode,Is.EqualTo("AtlasCrossOracleLayerResultInvalid"));
            Assert.That(AtlasOracleFixture.TryCreate(Document(Entry("a","b/c",0),Entry("a/b","c",1)),out AtlasOracleFixture collisionFixture,out StackMachineDiagnostic collisionCreate),Is.True,collisionCreate?.message); Assert.That(AtlasCrossOracle.TryValidate(collisionFixture,new List<AtlasOracleEntryMetadata>(collisionFixture.Metadata),out StackMachineDiagnostic collision),Is.True,collision?.message);
        }

        [Test]
        public void TryValidate_BindsEachSemanticAndLayerToItsActualResultAndFailureContext()
        {
            Assert.That(AtlasOracleFixture.TryCreate(Document(Entry("", "body", 0), Entry("outfit", "top", 1)), out AtlasOracleFixture fixture, out StackMachineDiagnostic create), Is.True, create?.message);
            var evidence=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) evidence.Add(CreateEvidence(fixture,context)); Assert.That(AtlasCrossOracle.TryValidate(fixture,evidence,out var accepted),Is.True,accepted?.message);
            var failures=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) failures.Add(CreateFailureEvidence(fixture,context)); Assert.That(AtlasCrossOracle.TryValidate(fixture,failures,out var failureAccepted),Is.True,failureAccepted?.message);
            AtlasCrossOracle.Evidence first=failures[0]; AtlasOracleEntryMetadata wrong=fixture.Metadata[1]; failures[0]=new AtlasCrossOracle.Evidence(first.Context,first.Succeeded,first.Diagnostic,wrong,first.MetamorphicSucceeded,first.MetamorphicDiagnostic,first.MetamorphicDiagnosticContext); Assert.That(AtlasCrossOracle.TryValidate(fixture,failures,out var mismatch),Is.False); Assert.That(mismatch.domainCode,Is.EqualTo("AtlasCrossOracleLayerResultInvalid"));
            failures=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) failures.Add(CreateFailureEvidence(fixture,context)); int imageIndex=failures.FindIndex(item=>item.Context.Layer==AtlasOracleLayer.Image); AtlasCrossOracle.Evidence image=failures[imageIndex]; failures[imageIndex]=new AtlasCrossOracle.Evidence(image.Context,image.Succeeded,image.Diagnostic,image.DiagnosticContext,image.MetamorphicSucceeded,image.MetamorphicDiagnostic,wrong); Assert.That(AtlasCrossOracle.TryValidate(fixture,failures,out var metamorphicMismatch),Is.False); Assert.That(metamorphicMismatch.domainCode,Is.EqualTo("AtlasCrossOracleLayerResultInvalid"));
            failures=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) failures.Add(CreateFailureEvidence(fixture,context)); first=failures[0]; var common=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"forged"); common.domainCode="forged"; failures[0]=new AtlasCrossOracle.Evidence(first.Context,false,common,first.Context,first.MetamorphicSucceeded,first.MetamorphicDiagnostic,first.MetamorphicDiagnosticContext); Assert.That(AtlasCrossOracle.TryValidate(fixture,failures,out var commonMismatch),Is.False); Assert.That(commonMismatch.domainCode,Is.EqualTo("AtlasCrossOracleLayerResultInvalid"));
            failures=new List<AtlasCrossOracle.Evidence>(); foreach(var context in fixture.Metadata) failures.Add(CreateFailureEvidence(fixture,context)); image=failures[failures.FindIndex(item=>item.Context.Layer==AtlasOracleLayer.Image)]; var foreign=StackMachineDiagnostic.CreateDomain("texture","foreign","forged"); failures[failures.FindIndex(item=>item.Context.Layer==AtlasOracleLayer.Image)]=new AtlasCrossOracle.Evidence(image.Context,image.Succeeded,image.Diagnostic,image.DiagnosticContext,false,foreign,image.Context); Assert.That(AtlasCrossOracle.TryValidate(fixture,failures,out var foreignMismatch),Is.False); Assert.That(foreignMismatch.domainCode,Is.EqualTo("AtlasCrossOracleLayerResultInvalid"));
        }

        private static AtlasCrossOracle.Evidence CreateEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            if(context.Layer==AtlasOracleLayer.Layout) { bool success=AtlasLayoutPropertyOracle.TryValidate(fixture.Document,fixture.Layout,out var diagnostic); return new AtlasCrossOracle.Evidence(context,success,diagnostic,success?null:context); }
            if(context.Layer==AtlasOracleLayer.MeshUv) return MeshEvidence(fixture,context);
            return ImageEvidence(fixture,context);
        }
        private static AtlasCrossOracle.Evidence CreateFailureEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            if(context.Layer==AtlasOracleLayer.Layout) { Assert.That(AtlasLayoutPropertyOracle.TryValidate(fixture.Document,null,out var diagnostic),Is.False); return new AtlasCrossOracle.Evidence(context,false,diagnostic,context); }
            if(context.Layer==AtlasOracleLayer.MeshUv) return MeshFailureEvidence(fixture,context);
            return ImageFailureEvidence(fixture,context);
        }
        private static AtlasCrossOracle.Evidence MeshEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            Mesh before=Mesh(), after=Object.Instantiate(before); try { AtlasLayoutCell cell=Cell(fixture,context); Vector2[] uv=after.uv; for(int i=0;i<uv.Length;i++) uv[i]=AtlasUvTransform.Apply(before.uv[i],Vector2.one,Vector2.zero,cell,fixture.Layout.PageExtent); after.uv=uv; var contexts=new[]{new AtlasMeshStructureOracle.Context(context.MaterialId,0,true,cell,fixture.Layout.PageExtent,Vector2.one,Vector2.zero,new AtlasMeshStructureOracle.MaterialState(new Vector4(1,1,0,0),new Vector4(1,1,0,0)),false,context.Semantic)}; var state=new AtlasMeshStructureOracle.RendererState(new[]{context.MaterialId},"root","avatar"); bool success=AtlasMeshStructureOracle.TryValidateForAtlasAcceptance(before,after,contexts,fixture.Layout,state,state,out var diagnostic); return new AtlasCrossOracle.Evidence(context,success,diagnostic,success?null:context); } finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }
        private static AtlasCrossOracle.Evidence ImageEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            Texture2D source=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true); RenderTexture image=new RenderTexture(2,2,0,RenderTextureFormat.ARGBFloat); RenderTexture atlas=new RenderTexture(fixture.Layout.PageExtent,fixture.Layout.PageExtent,0,RenderTextureFormat.ARGBFloat); source.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); source.Apply(false,false); image.Create(); atlas.Create(); Graphics.Blit(source,image); Graphics.Blit(source,atlas); try { bool success=AtlasImageOracle.TryCompare(image,source,new[]{new AtlasImageOracle.Probe(0,0,Color.white)},context.Semantic,true,new AtlasImageOracle.PixelTolerance(.001f,2f/255f),out _,out var diagnostic); AtlasLayoutCell cell=Cell(fixture,context); Vector2 oldUv=new Vector2(.5f,.5f), atlasUv=AtlasUvTransform.Apply(oldUv,Vector2.one,Vector2.zero,cell,fixture.Layout.PageExtent); bool metamorphic=AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,fixture.Layout.PageExtent,Vector2.one,Vector2.zero,new[]{oldUv},new[]{atlasUv},context.Semantic,true,new AtlasImageOracle.PixelTolerance(.001f,2f/255f),out _,out var metamorphicDiagnostic); return new AtlasCrossOracle.Evidence(context,success,diagnostic,success?null:context,metamorphic,metamorphicDiagnostic,metamorphic?null:context); } finally { if(RenderTexture.active==image||RenderTexture.active==atlas) RenderTexture.active=null; image.Release(); atlas.Release(); Object.DestroyImmediate(source); Object.DestroyImmediate(image); Object.DestroyImmediate(atlas); }
        }
        private static AtlasCrossOracle.Evidence MeshFailureEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            Mesh before=Mesh(), after=Object.Instantiate(before); try { AtlasLayoutCell cell=Cell(fixture,context); var contexts=new[]{new AtlasMeshStructureOracle.Context(context.MaterialId,0,true,cell,fixture.Layout.PageExtent,Vector2.one,Vector2.zero,new AtlasMeshStructureOracle.MaterialState(new Vector4(1,1,0,0),new Vector4(1,1,0,0)),false,context.Semantic)}; var state=new AtlasMeshStructureOracle.RendererState(new[]{context.MaterialId},"root","avatar"); Assert.That(AtlasMeshStructureOracle.TryValidateForAtlasAcceptance(before,after,contexts,fixture.Layout,state,state,out var diagnostic),Is.False); return new AtlasCrossOracle.Evidence(context,false,diagnostic,context); } finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }
        private static AtlasCrossOracle.Evidence ImageFailureEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            Texture2D source=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true); RenderTexture image=new RenderTexture(2,2,0,RenderTextureFormat.ARGBFloat); RenderTexture atlas=new RenderTexture(fixture.Layout.PageExtent,fixture.Layout.PageExtent,0,RenderTextureFormat.ARGBFloat); source.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); source.Apply(false,false); image.Create(); atlas.Create(); Graphics.Blit(source,image); Graphics.Blit(Texture2D.blackTexture,atlas); try { Assert.That(AtlasImageOracle.TryCompare(image,source,new[]{new AtlasImageOracle.Probe(0,0,Color.black)},context.Semantic,true,new AtlasImageOracle.PixelTolerance(.001f,2f/255f),out _,out var diagnostic),Is.False); AtlasLayoutCell cell=Cell(fixture,context); Vector2 oldUv=new Vector2(.5f,.5f), atlasUv=AtlasUvTransform.Apply(oldUv,Vector2.one,Vector2.zero,cell,fixture.Layout.PageExtent); Assert.That(AtlasImageMetamorphicOracle.TryValidate(source,atlas,cell,fixture.Layout.PageExtent,Vector2.one,Vector2.zero,new[]{oldUv},new[]{atlasUv},context.Semantic,true,new AtlasImageOracle.PixelTolerance(.001f,2f/255f),out _,out var metamorphicDiagnostic),Is.False); return new AtlasCrossOracle.Evidence(context,false,diagnostic,context,false,metamorphicDiagnostic,context); } finally { if(RenderTexture.active==image||RenderTexture.active==atlas) RenderTexture.active=null; image.Release(); atlas.Release(); Object.DestroyImmediate(source); Object.DestroyImmediate(image); Object.DestroyImmediate(atlas); }
        }
        private static AtlasLayoutCell Cell(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context) { Assert.That(fixture.Layout.TryGetCell(context.MaterialId,out AtlasLayoutCell cell),Is.True); return cell; }

        private static AtlasSchemaDocument Document(params AtlasSchemaEntry[] entries)
        {
            var sources=new List<AtlasSourceMaterialIdentity>(); foreach(var entry in entries) sources.Add(new AtlasSourceMaterialIdentity(entry.MaterialId.ToMaterialId(),"source:"+entry.MaterialId.EntryId));
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current,512,AtlasPackingAlgorithm.FirstFitBuddyV1,true,new AtlasValidationIdentity("figure","document",sources),entries);
        }
        private static AtlasSchemaEntry Entry(string registry,string id,int page) => new AtlasSchemaEntry(new MaterialId(registry,id),page,2,2,false);
        private static Mesh Mesh(){var mesh=new Mesh();mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};mesh.SetTriangles(new[]{0,1,2},0);return mesh;}
    }
}
