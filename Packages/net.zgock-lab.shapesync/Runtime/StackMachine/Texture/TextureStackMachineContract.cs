// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Classifies a Texture StackMachine logical binding.</summary>
    public enum TextureBindingKind
    {
        /// <summary>Read-only Texture input consumed by the recipe.</summary>
        SourceTexture,
        /// <summary>Reserved writable logical output hall named <c>out</c>.</summary>
        OutputHall
    }

    /// <summary>Describes one non-persistent logical Texture StackMachine binding.</summary>
    [Serializable]
    public sealed class TextureBindingEntry
    {
        /// <summary>Logical recipe name without the Forth <c>$</c> prefix.</summary>
        public string logicalName;
        /// <summary>Whether this entry supplies a source texture or names the output hall.</summary>
        public TextureBindingKind kind;
        /// <summary>Source texture required only when <see cref="kind"/> is <see cref="TextureBindingKind.SourceTexture"/>.</summary>
        public Texture sourceTexture;
    }

    /// <summary>Detached version-2 Texture StackMachine recipe document.</summary>
    /// <remarks>
    /// A document declares exactly one reserved <c>$out</c> output. Its width and height are independent
    /// supported extents; runtime output, GPU reservations, and delivery ownership are not serialized here.
    /// </remarks>
    [Serializable]
    public sealed class MaterialRecipeDocument : StackMachineRecipeDocument
    {
        /// <summary>Gets or sets the Texture-domain contract version.</summary>
        /// <remarks>Only version <c>2</c> is accepted; version 1 has no compatibility path.</remarks>
        public int textureDomainVersion = 2;
        /// <summary>Gets or sets the reserved output logical name.</summary>
        /// <remarks>The value must be <c>out</c>, which is exposed to recipes as <c>$out</c>.</remarks>
        public string outputLogicalName = "out";
        /// <summary>Gets or sets the reserved output width in texels.</summary>
        /// <remarks>The value must be a supported power-of-two extent from 128 through 8192.</remarks>
        public int outputWidth = 128;
        /// <summary>Gets or sets the reserved output height in texels.</summary>
        /// <remarks>The value must be a supported power-of-two extent from 128 through 8192.</remarks>
        public int outputHeight = 128;

        /// <inheritdoc />
        public override bool TryValidateDomain(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (textureDomainVersion != 2)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "UnsupportedTextureDomainVersion", "Texture StackMachine requires domain version 2.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(outputLogicalName))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLogicalNameRequired", "Texture recipes require an output logical name.");
                return false;
            }

            if (!TextureGpuCapabilityProbe.IsPhase0Edge(outputWidth) || !TextureGpuCapabilityProbe.IsPhase0Edge(outputHeight))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "InvalidOutputExtent", "Texture output width and height must each be a supported power-of-two extent.");
                return false;
            }

            return true;
        }
    }

    /// <summary>Non-persistent runtime input for the Spec14.2 Texture StackMachine.</summary>
    public sealed class TextureRecipeStub
    {
        /// <summary>Creates a stub from an in-memory document and binding entries.</summary>
        /// <param name="document">Domain document to validate and compile.</param>
        /// <param name="bindings">Non-persistent logical binding entries.</param>
        public TextureRecipeStub(MaterialRecipeDocument document, TextureBindingEntry[] bindings)
        {
            Document = document;
            Bindings = bindings;
        }

        /// <summary>Gets the in-memory domain document.</summary>
        public MaterialRecipeDocument Document { get; }
        /// <summary>Gets the non-persistent binding entries.</summary>
        public TextureBindingEntry[] Bindings { get; }
    }

    /// <summary>Resolved non-persistent Texture binding exposed only to the Texture domain.</summary>
    public readonly struct TextureBinding
    {
        /// <summary>Creates a resolved binding.</summary>
        /// <param name="logicalName">Logical name without the Forth <c>$</c> prefix.</param>
        /// <param name="kind">Whether the binding is an input texture or output hall.</param>
        /// <param name="sourceTexture">Texture supplied by a source binding; otherwise <see langword="null"/>.</param>
        public TextureBinding(string logicalName, TextureBindingKind kind, Texture sourceTexture)
        {
            LogicalName = logicalName;
            Kind = kind;
            SourceTexture = sourceTexture;
        }

        /// <summary>Gets the logical binding name.</summary>
        public string LogicalName { get; }
        /// <summary>Gets the binding kind.</summary>
        public TextureBindingKind Kind { get; }
        /// <summary>Gets the source texture for source bindings.</summary>
        public Texture SourceTexture { get; }
    }

    /// <summary>Compile-time lookup for a validated <see cref="TextureRecipeStub"/>.</summary>
    public sealed class TextureBindingContext : IStackMachineBindingContext
    {
        private readonly Dictionary<string, TextureBinding> byName;
        private readonly MaterialRecipeDocument document;
        private readonly string outputLogicalName;

        private TextureBindingContext(MaterialRecipeDocument document, Dictionary<string, TextureBinding> byName)
        {
            this.document = document;
            this.byName = byName;
            outputLogicalName = document.outputLogicalName;
        }

        /// <summary>Gets the validated in-memory document that owns this binding context.</summary>
        public MaterialRecipeDocument Document => document;

        /// <summary>Gets the output logical name snapshotted when this context was validated.</summary>
        /// <remarks>Concrete execution must use this value instead of rereading mutable document fields.</remarks>
        public string OutputLogicalName => outputLogicalName;

        /// <summary>Resolves a logical binding without rebuilding the lookup.</summary>
        /// <param name="logicalName">Logical name without the Forth prefix.</param>
        /// <param name="binding">Resolved binding.</param>
        /// <returns><see langword="true"/> when the binding exists.</returns>
        public bool TryGetBinding(string logicalName, out TextureBinding binding) => byName.TryGetValue(logicalName, out binding);

        /// <summary>Creates and validates a compile-time context from an in-memory recipe stub.</summary>
        /// <param name="stub">Non-persistent recipe input.</param>
        /// <param name="context">Resolved context on success.</param>
        /// <param name="diagnostic">Validation failure on rejection.</param>
        /// <returns><see langword="true"/> when the stub can be compiled.</returns>
        public static bool TryCreate(TextureRecipeStub stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic)
        {
            context = null;
            if (stub == null || stub.Document == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "RecipeStubRequired", "TextureRecipeStub and its document are required.");
                return false;
            }

            if (!StackMachineRecipeSerialization.TryValidateDocument(stub.Document, out diagnostic)) return false;
            if (stub.Bindings == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "BindingsRequired", "TextureRecipeStub bindings are required.");
                return false;
            }

            var entries = new Dictionary<string, TextureBinding>(StringComparer.Ordinal);
            bool hasOutput = false;
            for (int i = 0; i < stub.Bindings.Length; i++)
            {
                TextureBindingEntry entry = stub.Bindings[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName) || entries.ContainsKey(entry.logicalName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "InvalidBinding", "Texture bindings must have unique logical names.");
                    return false;
                }

                if (entry.kind == TextureBindingKind.SourceTexture && entry.sourceTexture == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceTextureRequired", "A source binding requires a Texture.", bindingName: entry.logicalName);
                    return false;
                }

                if (entry.kind == TextureBindingKind.OutputHall)
                {
                    if (hasOutput || entry.logicalName != "out" || stub.Document.outputLogicalName != "out")
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputBindingInvalid", "Texture recipes require exactly one reserved $out OutputHall.", bindingName: entry.logicalName);
                        return false;
                    }

                    if (entry.sourceTexture != null)
                    {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputHallHasTexture", "OutputHall cannot hold a source Texture.", bindingName: entry.logicalName);
                        return false;
                    }

                    hasOutput = true;
                }

                entries.Add(entry.logicalName, new TextureBinding(entry.logicalName, entry.kind, entry.sourceTexture));
            }

            if (!hasOutput)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputBindingRequired", "A TextureRecipeStub requires one OutputHall.", bindingName: stub.Document.outputLogicalName);
                return false;
            }

            if (stub.Document.bindings == null || stub.Document.bindings.Count != entries.Count)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "BindingDeclarationMismatch", "Document binding declarations must exactly match stub bindings.");
                return false;
            }

            foreach (StackMachineBindingDeclaration declaration in stub.Document.bindings)
            {
                if (declaration == null || declaration.declaredKind != StackMachineBindingKind.Resource || !entries.ContainsKey(declaration.logicalName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "BindingDeclarationMismatch", "Every Texture binding must have one Resource declaration.");
                    return false;
                }
            }

            context = new TextureBindingContext(stub.Document, entries);
            diagnostic = null;
            return true;
        }
    }

    /// <summary>Registers the fixed Spec14.2 Texture StackMachine word set.</summary>
    public sealed class TextureWordSet : StackMachineWordSetBase
    {
        /// <summary>Fills a destination hall with a Linear RGBA literal.</summary>
        public const string Fill = "FILL";
        /// <summary>Fills the reserved output hall directly with a Linear RGBA literal.</summary>
        /// <remarks>Stack signature: <c>( $out r g b a -- )</c>.</remarks>
        public const string FillOut = "FILL_OUT";
        /// <summary>Clears the reserved output hall directly to transparent black.</summary>
        /// <remarks>Stack signature: <c>( $out -- )</c>.</remarks>
        public const string Clear = "CLEAR";
        /// <summary>Copies one source rectangle directly into one output rectangle.</summary>
        /// <remarks>Stack signature: <c>( $source srcX srcY srcWidth srcHeight dstX dstY dstWidth dstHeight -- )</c>.</remarks>
        public const string Place = "PLACE";
        /// <summary>Copies a source hall into a destination hall.</summary>
        public const string Copy = "COPY";
        /// <summary>Publishes the top image result to the reserved output hall and removes it from the data stack.</summary>
        public const string Publish = ".";
        /// <summary>Composites the top straight-alpha foreground over the preceding background.</summary>
        public const string AlphaOver = "ACOPY";
        /// <summary>Composites the top premultiplied-alpha foreground over the preceding background.</summary>
        public const string PremultipliedAlphaOver = "PACOPY";
        /// <summary>Replaces a source alpha channel with the average RGB value of a grayscale source.</summary>
        public const string Alpha = "ALPHA";
        /// <summary>Adds two source halls component-wise.</summary>
        public const string Add = "ADD";
        /// <summary>Subtracts the top source hall component-wise from the preceding source hall.</summary>
        public const string Subtract = "SUB";
        /// <summary>Multiplies two source halls component-wise.</summary>
        public const string Multiply = "MULTIPLY";
        /// <summary>Converts Y, U, and V source halls to RGBA.</summary>
        public const string Yuv = "YUV";
        /// <summary>Colorizes one RGBA source in YUV space with hue, saturation, and lightness controls.</summary>
        /// <remarks>Stack signature: <c>( image hue saturation lightness -- image )</c>. Hue and saturation are in <c>[0,1]</c>; lightness is in <c>[-1,1]</c>.</remarks>
        public const string Colorize = "COLORIZE";
        /// <summary>Resamples a source hall into a destination hall.</summary>
        public const string Resample = "RESAMPLE";
        /// <summary>Blends two encoded normal maps by a scalar weight.</summary>
        public const string NormalWeightedBlend = "NORMAL_WEIGHTED_BLEND";
        /// <summary>Decodes an encoded Normal map into an internal vector-field hall.</summary>
        public const string NormalBase = "NORMAL_BASE";
        /// <summary>Adds one unnormalized weighted Normal delta to an internal vector-field hall.</summary>
        public const string NormalDeltaAdd = "NORMAL_DELTA_ADD";
        /// <summary>Normalizes and encodes an internal Normal vector-field hall.</summary>
        public const string NormalFinalize = "NORMAL_FINALIZE";
        /// <summary>Declares a square output extent before all texture operations.</summary>
        /// <remarks>Stack signature: <c>( edge -- )</c>. This is equivalent to <c>edge edge RECTSIZE</c>.</remarks>
        public const string Size = "SIZE";
        /// <summary>Declares a rectangular output extent before all texture operations.</summary>
        /// <remarks>Stack signature: <c>( width height -- )</c>. Each axis must be a supported power-of-two extent.</remarks>
        public const string RectSize = "RECTSIZE";
        /// <summary>Declares a source texture's native rectangular extent as the output canvas while preserving that source on the stack.</summary>
        /// <remarks>Stack signature: <c>( $source -- $source )</c>. The source must be read by the recipe and both axes must be supported extents.</remarks>
        public const string Canvas = "CANVAS";

        /// <inheritdoc />
        protected override void RegisterWords(StackMachineWordRegistry registry)
        {
            registry.TryRegisterDomainWord(Fill, Fill, new StackMachineWordSignature(new[] { StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(FillOut, FillOut, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number }, null));
            registry.TryRegisterDomainWord(Clear, Clear, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, null));
            registry.TryRegisterDomainWord(Place, Place, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number }, null));
            registry.TryRegisterDomainWord(Copy, Copy, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Publish, Publish, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, null));
            registry.TryRegisterDomainWord(AlphaOver, AlphaOver, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(PremultipliedAlphaOver, PremultipliedAlphaOver, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Alpha, Alpha, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Add, Add, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Subtract, Subtract, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Multiply, Multiply, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Yuv, Yuv, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Colorize, Colorize, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number, StackMachineValueTag.Number, StackMachineValueTag.Number }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Resample, Resample, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(NormalWeightedBlend, NormalWeightedBlend, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(NormalBase, NormalBase, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(NormalDeltaAdd, NormalDeltaAdd, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle, StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(NormalFinalize, NormalFinalize, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
            registry.TryRegisterDomainWord(Size, Size, new StackMachineWordSignature(new[] { StackMachineValueTag.Number }, null));
            registry.TryRegisterDomainWord(RectSize, RectSize, new StackMachineWordSignature(new[] { StackMachineValueTag.Number, StackMachineValueTag.Number }, null));
            registry.TryRegisterDomainWord(Canvas, Canvas, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, new[] { StackMachineValueTag.ResourceHandle }));
        }

        /// <inheritdoc />
        public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            return instruction.WordId == Fill || instruction.WordId == Copy || instruction.WordId == Publish || instruction.WordId == AlphaOver || instruction.WordId == PremultipliedAlphaOver || instruction.WordId == Alpha || instruction.WordId == Add || instruction.WordId == Subtract || instruction.WordId == Multiply || instruction.WordId == Yuv || instruction.WordId == Colorize || instruction.WordId == Resample || instruction.WordId == NormalWeightedBlend || instruction.WordId == NormalBase || instruction.WordId == NormalDeltaAdd || instruction.WordId == NormalFinalize || instruction.WordId == Size || instruction.WordId == RectSize || instruction.WordId == Canvas;
        }
    }

    /// <summary>Normalizes Texture-domain <c>#RRGGBBAA</c> literals before common compilation.</summary>
    public static class TextureColorLiteralNormalizer
    {
        /// <summary>Creates a detached document whose color literals are invariant-culture Number literals.</summary>
        /// <param name="source">Source document to normalize.</param>
        /// <param name="normalized">Detached normalized document.</param>
        /// <param name="diagnostic">Malformed color literal diagnostic.</param>
        /// <returns><see langword="true"/> when every color literal is valid.</returns>
        public static bool TryNormalizeForCompile(MaterialRecipeDocument source, out MaterialRecipeDocument normalized, out StackMachineDiagnostic diagnostic)
        {
            normalized = null;
            if (source == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "RecipeDocumentRequired", "MaterialRecipeDocument is required.");
                return false;
            }

            var text = source.wordSource ?? string.Empty;
            var output = new StringBuilder(text.Length);
            var sourceMap = new List<StackMachineSourceMapEntry>();
            int tokenIndex = 0;
            int normalizedTokenIndex = 0;
            int cursor = 0;
            while (cursor < text.Length)
            {
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                if (cursor >= text.Length) break;
                int offset = cursor;
                while (cursor < text.Length && !char.IsWhiteSpace(text[cursor])) cursor++;
                string token = text.Substring(offset, cursor - offset);
                if (output.Length > 0) output.Append(' ');
                if (token.Length > 0 && token[0] == '#')
                {
                    if (!TryExpand(token, output))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "MalformedColorLiteral", "Texture color literals must use exactly #RRGGBBAA.", tokenIndex, bindingName: null, detail: token);
                        return false;
                    }
                    for (int component = 0; component < 4; component++) sourceMap.Add(new StackMachineSourceMapEntry { tokenIndex = normalizedTokenIndex++, sourceOffset = offset });
                }
                else
                {
                    output.Append(token);
                    sourceMap.Add(new StackMachineSourceMapEntry { tokenIndex = normalizedTokenIndex++, sourceOffset = offset });
                }
                tokenIndex++;
            }

            normalized = Copy(source);
            normalized.wordSource = output.ToString();
            normalized.diagnosticSourceMap.Clear();
            normalized.diagnosticSourceMap.AddRange(sourceMap);
            diagnostic = null;
            return true;
        }

        private static bool TryExpand(string token, StringBuilder output)
        {
            if (token.Length != 9) return false;
            for (int i = 1; i < token.Length; i++) if (!Uri.IsHexDigit(token[i])) return false;
            for (int component = 0; component < 4; component++)
            {
                int value = Convert.ToInt32(token.Substring(1 + component * 2, 2), 16);
                if (component > 0) output.Append(' ');
                output.Append(((double)value / 255.0).ToString("R", CultureInfo.InvariantCulture));
            }
            return true;
        }

        private static MaterialRecipeDocument Copy(MaterialRecipeDocument source)
        {
            var copy = new MaterialRecipeDocument
            {
                recipeFormatVersion = source.recipeFormatVersion,
                wordSource = source.wordSource,
                textureDomainVersion = source.textureDomainVersion,
                outputLogicalName = source.outputLogicalName,
                outputWidth = source.outputWidth,
                outputHeight = source.outputHeight,
                provenance = source.provenance == null ? new StackMachineProvenance() : new StackMachineProvenance { author = source.provenance.author, note = source.provenance.note }
            };
            if (source.bindings != null) for (int i = 0; i < source.bindings.Count; i++)
            {
                StackMachineBindingDeclaration item = source.bindings[i];
                copy.bindings.Add(item == null ? null : new StackMachineBindingDeclaration { logicalName = item.logicalName, declaredKind = item.declaredKind });
            }
            if (source.capabilities != null) copy.capabilities.AddRange(source.capabilities);
            if (source.diagnosticSourceMap != null) for (int i = 0; i < source.diagnosticSourceMap.Count; i++)
            {
                StackMachineSourceMapEntry item = source.diagnosticSourceMap[i];
                copy.diagnosticSourceMap.Add(item == null ? null : new StackMachineSourceMapEntry { tokenIndex = item.tokenIndex, sourceOffset = item.sourceOffset });
            }
            return copy;
        }
    }

}
