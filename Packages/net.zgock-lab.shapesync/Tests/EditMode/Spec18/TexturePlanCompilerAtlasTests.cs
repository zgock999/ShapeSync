// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests
{
    public sealed class TexturePlanCompilerAtlasTests
    {
        [Test]
        public void AtlasRecipe_CompilesDirectFillAndPlaceAsPageLocalRecords()
        {
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                TextureDispatchPlan plan = Compile("$out 0.5 0.5 1 1 FILL_OUT $source 4 8 32 16 40 48 32 16 PLACE", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });

                Assert.That(plan.Records.Count, Is.EqualTo(2));
                TextureDispatchRecord fill = plan.Records[0];
                Assert.That(fill.Operation, Is.EqualTo(TextureDispatchOperation.Fill));
                Assert.That(fill.Sources, Is.Empty);
                Assert.That(fill.SourceRectangles, Is.Empty);
                Assert.That(fill.Output, Is.EqualTo("out"));
                AssertRectangle(fill.DestinationRectangle, 0, 0, 128, 128);
                AssertExtent(fill.RecordExtent, 128, 128);
                Assert.That(fill.Scalars, Is.EqualTo(new[] { 0.5f, 0.5f, 1f, 1f }));

                TextureDispatchRecord place = plan.Records[1];
                Assert.That(place.Operation, Is.EqualTo(TextureDispatchOperation.Place));
                Assert.That(place.Sources, Is.EqualTo(new[] { "source" }));
                Assert.That(place.SourceRectangles.Count, Is.EqualTo(1));
                AssertRectangle(place.SourceRectangles[0], 4, 8, 32, 16);
                AssertRectangle(place.DestinationRectangle, 40, 48, 32, 16);
                AssertExtent(place.RecordExtent, 32, 16);
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void Clear_CompilesAsTransparentBlackDirectFill()
        {
            TextureDispatchPlan plan = Compile("$out CLEAR");

            Assert.That(plan.Records.Count, Is.EqualTo(1));
            TextureDispatchRecord record = plan.Records[0];
            Assert.That(record.Operation, Is.EqualTo(TextureDispatchOperation.Fill));
            Assert.That(record.Scalars, Is.EqualTo(new[] { 0f, 0f, 0f, 0f }));
            Assert.That(record.SourceRectangles, Is.Empty);
            AssertRectangle(record.DestinationRectangle, 0, 0, 128, 128);
        }

        [Test]
        public void DirectInitialiser_AfterPriorRecordIsRejected()
        {
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                AssertCompileFails("$source 0 0 16 16 0 0 16 16 PLACE $out CLEAR", "DirectOutputFillInvalid", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void Place_RejectsOverlappingDestinationsAndNonSourceBinding()
        {
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                AssertCompileFails("$out 0 0 0 0 FILL_OUT $source 0 0 16 16 0 0 16 16 PLACE $source 16 0 16 16 8 0 16 16 PLACE", "PlaceDestinationOverlap", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });
                AssertCompileFails("$out 0 0 16 16 0 0 16 16 PLACE", "PlaceSourceRequired");
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void DirectWordsAndPlace_RejectWrongOutputAndInvalidRectangles()
        {
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                AssertCompileFails("$source 0 0 0 0 FILL_OUT", "DirectOutputFillInvalid", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });
                AssertCompileFails("$out 0 0 0 0 FILL_OUT $source 0 0 16.5 16 0 0 16 16 PLACE", "PlaceRectangleInvalid", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });
                AssertCompileFails("$out CLEAR $source 120 0 16 16 0 0 16 16 PLACE", "PlaceRectangleInvalid", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void DirectInitialiser_DoesNotLeaveATemporaryForPublish()
        {
            AssertCommonCompileFails("$out 0 0 0 0 FILL_OUT .", StackMachineDiagnosticCode.StackUnderflow);
        }

        [Test]
        public void PlanWithoutOutputWrite_IsRejected()
        {
            AssertCompileFails("0 0 0 1 FILL DROP", "OutputWriteRequired");
        }

        [Test]
        public void LegacyMultiSourceRecord_RetainsWholeSourceRectangles()
        {
            var a = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var b = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                TextureDispatchPlan plan = Compile("$a $b ADD .", new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = a }, new TextureBindingEntry { logicalName = "b", kind = TextureBindingKind.SourceTexture, sourceTexture = b });
                TextureDispatchRecord add = plan.Records[0];
                Assert.That(add.SourceRectangles.Count, Is.EqualTo(2));
                AssertRectangle(add.SourceRectangles[0], 0, 0, 128, 128);
                AssertRectangle(add.SourceRectangles[1], 0, 0, 128, 128);
                AssertRectangle(add.DestinationRectangle, 0, 0, 128, 128);
                AssertExtent(add.RecordExtent, 128, 128);
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void DispatchPlan_CollectionsCannotBeMutatedThroughPublicContract()
        {
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                TextureDispatchPlan plan = Compile("$out CLEAR $source 0 0 16 16 32 32 16 16 PLACE", new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source });

                Assert.That(plan.Records, Is.Not.InstanceOf<TextureDispatchRecord[]>());
                Assert.That(plan.Records[1].Sources, Is.Not.InstanceOf<string[]>());
                Assert.That(plan.Records[1].SourceRectangles, Is.Not.InstanceOf<TextureDispatchRectangle[]>());
                Assert.That(plan.Records[0].Scalars, Is.Not.InstanceOf<float[]>());
                Assert.Throws<System.NotSupportedException>(() => ((System.Collections.Generic.IList<TextureDispatchRecord>)plan.Records)[0] = null);
                Assert.Throws<System.NotSupportedException>(() => ((System.Collections.Generic.IList<string>)plan.Records[1].Sources)[0] = "other");
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TextureBindings_CompileThroughSerializedTemplate()
        {
            var texture2D = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var renderTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            var template = ScriptableObject.CreateInstance<TextureBindingTemplate>();
            try
            {
                Assert.That(renderTexture.Create(), Is.True);
                template.SetBindings(new[]
                {
                    new TextureTemplateEntry { word = "texture2D", kind = TextureBindingKind.SourceTexture, texture = texture2D },
                    new TextureTemplateEntry { word = "renderTexture", kind = TextureBindingKind.SourceTexture, texture = renderTexture },
                    new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall }
                });
                MaterialRecipeDocument document = CreateDocument("$out CLEAR $renderTexture 0 0 16 16 32 32 16 16 PLACE", "texture2D", "renderTexture");

                Assert.That(template.TryCreateStub(document, out TextureRecipeStub stub, out StackMachineDiagnostic templateDiagnostic), Is.True, templateDiagnostic?.message);
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
                Assert.That(context.TryGetBinding("texture2D", out TextureBinding texture2DBinding), Is.True);
                Assert.That(texture2DBinding.SourceTexture, Is.SameAs(texture2D));
                Assert.That(context.TryGetBinding("renderTexture", out TextureBinding renderTextureBinding), Is.True);
                Assert.That(renderTextureBinding.SourceTexture, Is.SameAs(renderTexture));
                Assert.That(Compile(document, stub.Bindings).Records.Count, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(texture2D); renderTexture.Release(); Object.DestroyImmediate(renderTexture); }
        }

        private static TextureDispatchPlan Compile(string wordSource, params TextureBindingEntry[] sources)
        {
            MaterialRecipeDocument document = CreateDocument(wordSource, Names(sources));
            return Compile(document, AppendOutput(sources));
        }

        private static TextureDispatchPlan Compile(MaterialRecipeDocument document, TextureBindingEntry[] bindings)
        {
            var stub = new TextureRecipeStub(document, bindings);
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);
            Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            return plan;
        }

        private static void AssertCompileFails(string wordSource, string code, params TextureBindingEntry[] sources)
        {
            MaterialRecipeDocument document = CreateDocument(wordSource, Names(sources));
            var stub = new TextureRecipeStub(document, AppendOutput(sources));
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);
            Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo(code));
        }

        private static void AssertCommonCompileFails(string wordSource, StackMachineDiagnosticCode code)
        {
            MaterialRecipeDocument document = CreateDocument(wordSource);
            var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(document, registry, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(code));
        }

        private static MaterialRecipeDocument CreateDocument(string wordSource, params string[] sources)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string source in sources) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = source, declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return document;
        }

        private static string[] Names(TextureBindingEntry[] sources)
        {
            var names = new string[sources?.Length ?? 0];
            for (int i = 0; i < names.Length; i++) names[i] = sources[i].logicalName;
            return names;
        }

        private static TextureBindingEntry[] AppendOutput(TextureBindingEntry[] sources)
        {
            int count = sources?.Length ?? 0;
            var entries = new TextureBindingEntry[count + 1];
            for (int i = 0; i < count; i++) entries[i] = sources[i];
            entries[count] = new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall };
            return entries;
        }

        private static void AssertRectangle(TextureDispatchRectangle rectangle, int x, int y, int width, int height)
        {
            Assert.That(rectangle.X, Is.EqualTo(x)); Assert.That(rectangle.Y, Is.EqualTo(y));
            Assert.That(rectangle.Width, Is.EqualTo(width)); Assert.That(rectangle.Height, Is.EqualTo(height));
        }

        private static void AssertExtent(TextureDispatchExtent extent, int width, int height)
        {
            Assert.That(extent.Width, Is.EqualTo(width)); Assert.That(extent.Height, Is.EqualTo(height));
        }
    }
}
