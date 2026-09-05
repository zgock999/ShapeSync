// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Identifies one fully compiled Texture StackMachine operation.</summary>
    public enum TextureDispatchOperation
    {
        /// <summary>Fills an output hall with a literal Linear RGBA value.</summary>
        Fill,
        /// <summary>Copies one source hall to an output hall.</summary>
        Copy,
        /// <summary>Copies one source rectangle to one destination rectangle in the output hall.</summary>
        Place,
        /// <summary>Applies straight-alpha source-over compositing.</summary>
        AlphaOver,
        /// <summary>Applies premultiplied-alpha source-over compositing.</summary>
        PremultipliedAlphaOver,
        /// <summary>Replaces a source alpha channel from grayscale RGB.</summary>
        Alpha,
        /// <summary>Adds two halls component-wise.</summary>
        Add,
        /// <summary>Subtracts the second hall from the first component-wise.</summary>
        Subtract,
        /// <summary>Multiplies two halls component-wise.</summary>
        Multiply,
        /// <summary>Converts Y, U, and V source halls to RGBA.</summary>
        Yuv,
        /// <summary>Colorizes one RGBA source in YUV space using hue, saturation, and lightness scalars.</summary>
        Colorize,
        /// <summary>Resamples one hall with pixel-center bilinear clamp sampling.</summary>
        Resample,
        /// <summary>Blends two encoded normal maps by a scalar weight.</summary>
        NormalWeightedBlend,
        /// <summary>Decodes one encoded Normal map into an internal vector field.</summary>
        NormalBase,
        /// <summary>Adds an unnormalized weighted Normal delta to a vector field.</summary>
        NormalDeltaAdd,
        /// <summary>Normalizes and encodes a vector field into an output Normal map.</summary>
        NormalFinalize
    }

    /// <summary>Immutable zero-based logical rectangle in texels.</summary>
    public readonly struct TextureDispatchRectangle
    {
        /// <summary>Creates a logical rectangle.</summary>
        public TextureDispatchRectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        /// <summary>Gets the x origin.</summary>
        public int X { get; }
        /// <summary>Gets the y origin.</summary>
        public int Y { get; }
        /// <summary>Gets the width.</summary>
        public int Width { get; }
        /// <summary>Gets the height.</summary>
        public int Height { get; }
    }

    /// <summary>Immutable dispatch extent in texels.</summary>
    public readonly struct TextureDispatchExtent
    {
        /// <summary>Creates a dispatch extent.</summary>
        public TextureDispatchExtent(int width, int height) { Width = width; Height = height; }
        /// <summary>Gets the width.</summary>
        public int Width { get; }
        /// <summary>Gets the height.</summary>
        public int Height { get; }
    }

    /// <summary>Immutable operation record that contains resolved logical names, rectangles, and scalar values.</summary>
    public sealed class TextureDispatchRecord
    {
        internal TextureDispatchRecord(TextureDispatchOperation operation, string[] sources, TextureDispatchRectangle[] sourceRectangles, string output, TextureDispatchRectangle destinationRectangle, TextureDispatchExtent recordExtent, float[] scalars)
        {
            Operation = operation;
            Sources = Array.AsReadOnly(sources ?? Array.Empty<string>());
            SourceRectangles = Array.AsReadOnly(sourceRectangles ?? Array.Empty<TextureDispatchRectangle>());
            Output = output;
            DestinationRectangle = destinationRectangle;
            RecordExtent = recordExtent;
            Scalars = Array.AsReadOnly(scalars ?? Array.Empty<float>());
        }

        /// <summary>Gets the operation kind.</summary>
        public TextureDispatchOperation Operation { get; }
        /// <summary>Gets the resolved source hall names.</summary>
        public IReadOnlyList<string> Sources { get; }
        /// <summary>Gets source rectangles parallel to <see cref="Sources"/>.</summary>
        public IReadOnlyList<TextureDispatchRectangle> SourceRectangles { get; }
        /// <summary>Gets the resolved output hall name.</summary>
        public string Output { get; }
        /// <summary>Gets the page-local destination rectangle.</summary>
        public TextureDispatchRectangle DestinationRectangle { get; }
        /// <summary>Gets the exact dispatch extent.</summary>
        public TextureDispatchExtent RecordExtent { get; }
        /// <summary>Gets literal scalar operands in word order.</summary>
        public IReadOnlyList<float> Scalars { get; }
    }

    /// <summary>Immutable Texture-domain artifact derived from a validated common StackMachine plan.</summary>
    public sealed class TextureDispatchPlan : IStackMachineDomainPlan
    {
        internal TextureDispatchPlan(StackMachinePlan commonPlan, int outputWidth, int outputHeight, TextureDispatchRecord[] records, string[] readSourceNames)
        {
            CommonPlan = commonPlan;
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;
            Records = Array.AsReadOnly(records ?? Array.Empty<TextureDispatchRecord>());
            ReadSourceNames = Array.AsReadOnly(readSourceNames ?? Array.Empty<string>());
        }

        /// <inheritdoc />
        public StackMachinePlan CommonPlan { get; }
        /// <summary>Gets the compiled output hall width in texels.</summary>
        public int OutputWidth { get; }
        /// <summary>Gets the compiled output hall height in texels.</summary>
        public int OutputHeight { get; }
        /// <summary>Gets ordered, immutable domain operation records.</summary>
        public IReadOnlyList<TextureDispatchRecord> Records { get; }
        /// <summary>Gets actual source Texture logical names read by this plan.</summary>
        public IReadOnlyList<string> ReadSourceNames { get; }
    }

    /// <summary>Compiles a common plan into Texture-domain operations without allocating GPU resources.</summary>
    public static class TexturePlanCompiler
    {
        private const string TemporaryPrefix = "@texture:";
        private enum ValueKind { Number, Binding }
        private readonly struct Value
        {
            public Value(double number) { Kind = ValueKind.Number; Number = number; Binding = null; }
            public Value(string binding) { Kind = ValueKind.Binding; Number = 0d; Binding = binding; }
            public ValueKind Kind { get; }
            public double Number { get; }
            public string Binding { get; }
        }

        /// <summary>Builds the immutable Texture artifact and validates actual read sources and canvas rules.</summary>
        /// <param name="commonPlan">Validated common StackMachine plan.</param>
        /// <param name="document">Texture-domain document that supplies output directives.</param>
        /// <param name="context">Validated source and output binding lookup.</param>
        /// <param name="dispatchPlan">Immutable dispatch plan on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">Compile diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when all Texture-domain rules compile successfully.</returns>
        public static bool TryCompile(StackMachinePlan commonPlan, MaterialRecipeDocument document, TextureBindingContext context, out TextureDispatchPlan dispatchPlan, out StackMachineDiagnostic diagnostic)
        {
            dispatchPlan = null;
            if (commonPlan == null || document == null || context == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "CompileInputRequired", "Common plan, document, and TextureBindingContext are required.");
                return false;
            }

            var stack = new List<Value>();
            var records = new List<TextureDispatchRecord>();
            var placeDestinations = new List<TextureDispatchRectangle>();
            var readSources = new HashSet<string>(StringComparer.Ordinal);
            int outputWidth = document.outputWidth;
            int outputHeight = document.outputHeight;
            string canvasSource = null;
            bool firstDomainWord = true;
            bool hasOutputDirective = false;
            int temporaryIndex = 0;
            for (int i = 0; i < commonPlan.Instructions.Count; i++)
            {
                StackMachineInstruction instruction = commonPlan.Instructions[i];
                if (instruction.Op == StackMachineOp.PushNumber) { stack.Add(new Value(instruction.Number)); continue; }
                if (instruction.Op == StackMachineOp.PushBinding) { stack.Add(new Value(instruction.Binding)); continue; }
                if (instruction.Op == StackMachineOp.Drop)
                {
                    if (!TryPop(stack, 1, i, out _, out diagnostic)) return false;
                    continue;
                }
                if (TryApplyStackWord(instruction.Op, stack, i, out bool handled, out diagnostic))
                {
                    if (handled) continue;
                }
                if (instruction.Op != StackMachineOp.DomainWord)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "UnsupportedCommonWord", "Texture Phase0 accepts Number literals, logical bindings, and Texture words only.", i);
                    return false;
                }

                if (instruction.WordId == TextureWordSet.Size || instruction.WordId == TextureWordSet.RectSize || instruction.WordId == TextureWordSet.Canvas)
                {
                    if (!firstDomainWord || hasOutputDirective) return Fail("OutputDirectivePosition", "SIZE, RECTSIZE, or CANVAS may appear only once as the first Texture execution word.", i, out diagnostic);
                    hasOutputDirective = true;
                    if (instruction.WordId == TextureWordSet.Size)
                    {
                        if (!TryPop(stack, 1, i, out Value[] values, out diagnostic)) return false;
                        if (!TryExtent(values[0], out outputWidth)) return Fail("InvalidOutputExtent", "SIZE requires one integral supported power-of-two edge.", i, out diagnostic);
                        outputHeight = outputWidth;
                    }
                    else if (instruction.WordId == TextureWordSet.RectSize)
                    {
                        if (!TryPop(stack, 2, i, out Value[] values, out diagnostic)) return false;
                        if (!TryExtent(values[0], out outputWidth) || !TryExtent(values[1], out outputHeight)) return Fail("InvalidOutputExtent", "RECTSIZE requires integral supported power-of-two width and height.", i, out diagnostic);
                    }
                    else
                    {
                        if (!TryPop(stack, 1, i, out Value[] values, out diagnostic)) return false;
                        if (values[0].Kind != ValueKind.Binding || !context.TryGetBinding(values[0].Binding, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture) return Fail("CanvasSourceRequired", "CANVAS requires one SourceTexture logical binding.", i, out diagnostic);
                        outputWidth = binding.SourceTexture.width;
                        outputHeight = binding.SourceTexture.height;
                        if (!IsPhase0Edge(outputWidth) || !IsPhase0Edge(outputHeight)) return Fail("SourceExtentUnsupported", "CANVAS source width and height must each be supported power-of-two extents.", i, out diagnostic);
                        canvasSource = binding.LogicalName;
                        stack.Add(values[0]);
                    }
                    firstDomainWord = false;
                    continue;
                }

                if (instruction.WordId == TextureWordSet.Publish)
                {
                    if (!TryPop(stack, 1, i, out Value[] values, out diagnostic)) return false;
                    if (!TrySource(values[0], context, document.outputLogicalName, i, out string source, out bool readsTexture, out diagnostic)) return false;
                    if (readsTexture) readSources.Add(source);
                    if (!TryGetSourceExtent(source, context, document.outputLogicalName, outputWidth, outputHeight, out int sourceWidth, out int sourceHeight, out diagnostic)) return false;
                    if (sourceWidth != outputWidth || sourceHeight != outputHeight) return Fail("TextureExtentMismatch", "COPY and compositing operations require source and output halls of the same extent; use RESAMPLE for an explicit conversion.", i, out diagnostic);
                    TextureDispatchRectangle whole = WholeRectangle(outputWidth, outputHeight);
                    records.Add(new TextureDispatchRecord(TextureDispatchOperation.Copy, new[] { source }, new[] { whole }, document.outputLogicalName, whole, new TextureDispatchExtent(outputWidth, outputHeight), Array.Empty<float>()));
                    firstDomainWord = false;
                    continue;
                }

                if (instruction.WordId == TextureWordSet.FillOut || instruction.WordId == TextureWordSet.Clear)
                {
                    if (!TryCompileDirectInitialiser(instruction, i, stack, context, document.outputLogicalName, outputWidth, outputHeight, records.Count, out TextureDispatchRecord directRecord, out diagnostic)) return false;
                    records.Add(directRecord);
                    firstDomainWord = false;
                    continue;
                }

                if (instruction.WordId == TextureWordSet.Place)
                {
                    if (!TryCompilePlace(instruction, i, stack, context, document.outputLogicalName, outputWidth, outputHeight, placeDestinations, readSources, out TextureDispatchRecord placeRecord, out diagnostic)) return false;
                    records.Add(placeRecord);
                    firstDomainWord = false;
                    continue;
                }

                string operationOutput = instruction.WordId == TextureWordSet.Copy ? document.outputLogicalName : TemporaryPrefix + temporaryIndex++;
                if (!TryCompileWord(instruction, i, stack, context, document.outputLogicalName, operationOutput, outputWidth, outputHeight, readSources, out TextureDispatchRecord record, out diagnostic)) return false;
                records.Add(record);
                stack.Add(new Value(record.Output));
                firstDomainWord = false;
            }

            if (records.Count == 0)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TextureOperationRequired", "Texture recipes require at least one Texture word.");
                return false;
            }

            if (!string.IsNullOrEmpty(canvasSource) && !readSources.Contains(canvasSource)) return Fail("TextureCanvasSourceNotRead", "CANVAS must name an actual read source.", -1, out diagnostic);
            bool writesOutput = false;
            for (int i = 0; i < records.Count; i++) if (records[i].Output == document.outputLogicalName) { writesOutput = true; break; }
            if (!writesOutput) return Fail("OutputWriteRequired", "Texture recipes must write to the reserved OutputHall at least once.", -1, out diagnostic);

            dispatchPlan = new TextureDispatchPlan(commonPlan, outputWidth, outputHeight, records.ToArray(), Copy(readSources));
            diagnostic = null;
            return true;
        }

        private static bool IsPhase0Edge(int edge) => TextureGpuCapabilityProbe.IsPhase0Edge(edge);
        private static bool TryExtent(Value value, out int extent)
        {
            extent = 0;
            if (value.Kind != ValueKind.Number || value.Number != Math.Floor(value.Number) || value.Number > int.MaxValue || value.Number < int.MinValue) return false;
            extent = (int)value.Number;
            return IsPhase0Edge(extent);
        }

        internal static bool IsTemporary(string logicalName) => logicalName != null && logicalName.StartsWith(TemporaryPrefix, StringComparison.Ordinal);

        private static bool TryApplyStackWord(StackMachineOp operation, List<Value> stack, int instructionIndex, out bool handled, out StackMachineDiagnostic diagnostic)
        {
            handled = true;
            diagnostic = null;
            if (operation == StackMachineOp.Dup)
            {
                if (!TryPop(stack, 1, instructionIndex, out Value[] values, out diagnostic)) return false;
                stack.Add(values[0]); stack.Add(values[0]); return true;
            }
            if (operation == StackMachineOp.Swap)
            {
                if (!TryPop(stack, 2, instructionIndex, out Value[] values, out diagnostic)) return false;
                stack.Add(values[1]); stack.Add(values[0]); return true;
            }
            if (operation == StackMachineOp.Over)
            {
                if (!TryPop(stack, 2, instructionIndex, out Value[] values, out diagnostic)) return false;
                stack.Add(values[0]); stack.Add(values[1]); stack.Add(values[0]); return true;
            }
            if (operation == StackMachineOp.Rot)
            {
                if (!TryPop(stack, 3, instructionIndex, out Value[] values, out diagnostic)) return false;
                stack.Add(values[1]); stack.Add(values[2]); stack.Add(values[0]); return true;
            }
            handled = false;
            return true;
        }

        private static bool TryCompileDirectInitialiser(StackMachineInstruction instruction, int instructionIndex, List<Value> stack, TextureBindingContext context, string outputName, int outputWidth, int outputHeight, int recordCount, out TextureDispatchRecord record, out StackMachineDiagnostic diagnostic)
        {
            record = null;
            if (recordCount != 0) return Fail("DirectOutputFillInvalid", "CLEAR and FILL_OUT may appear only as the first Texture dispatch record.", instructionIndex, out diagnostic);
            int count = instruction.WordId == TextureWordSet.FillOut ? 5 : 1;
            if (!TryPop(stack, count, instructionIndex, out Value[] values, out diagnostic)) return false;
            if (!TryDestination(values[0], context, outputName, instructionIndex, out string destination, out diagnostic)) return Fail("DirectOutputFillInvalid", "CLEAR and FILL_OUT require the reserved OutputHall.", instructionIndex, out diagnostic);
            float[] scalars;
            if (instruction.WordId == TextureWordSet.FillOut)
            {
                scalars = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    Value value = values[i + 1];
                    if (value.Kind != ValueKind.Number || value.Number < 0d || value.Number > 1d || double.IsNaN(value.Number) || double.IsInfinity(value.Number)) return Fail("DirectOutputFillInvalid", "FILL_OUT requires finite [0,1] RGBA Number literals.", instructionIndex, out diagnostic);
                    scalars[i] = (float)value.Number;
                }
            }
            else scalars = new[] { 0f, 0f, 0f, 0f };
            TextureDispatchRectangle whole = WholeRectangle(outputWidth, outputHeight);
            record = new TextureDispatchRecord(TextureDispatchOperation.Fill, Array.Empty<string>(), Array.Empty<TextureDispatchRectangle>(), destination, whole, new TextureDispatchExtent(outputWidth, outputHeight), scalars);
            diagnostic = null;
            return true;
        }

        private static bool TryCompilePlace(StackMachineInstruction instruction, int instructionIndex, List<Value> stack, TextureBindingContext context, string outputName, int outputWidth, int outputHeight, List<TextureDispatchRectangle> placeDestinations, HashSet<string> readSources, out TextureDispatchRecord record, out StackMachineDiagnostic diagnostic)
        {
            record = null;
            if (!TryPop(stack, 9, instructionIndex, out Value[] values, out diagnostic)) return false;
            if (!TryPlaceSource(values[0], context, instructionIndex, out string source, out diagnostic)) return false;
            if (!TryRectangle(values, 1, "source", instructionIndex, out TextureDispatchRectangle sourceRectangle, out diagnostic) || !TryRectangle(values, 5, "destination", instructionIndex, out TextureDispatchRectangle destinationRectangle, out diagnostic)) return false;
            if (!TryGetSourceExtent(source, context, outputName, outputWidth, outputHeight, out int sourceWidth, out int sourceHeight, out diagnostic)) return false;
            if (!Contains(sourceWidth, sourceHeight, sourceRectangle) || !Contains(outputWidth, outputHeight, destinationRectangle)) return Fail("PlaceRectangleInvalid", "PLACE rectangles must be contained by their source and output extents.", instructionIndex, out diagnostic);
            for (int i = 0; i < placeDestinations.Count; i++) if (Overlaps(placeDestinations[i], destinationRectangle)) return Fail("PlaceDestinationOverlap", "PLACE destination rectangles must not overlap.", instructionIndex, out diagnostic);
            placeDestinations.Add(destinationRectangle);
            readSources.Add(source);
            record = new TextureDispatchRecord(TextureDispatchOperation.Place, new[] { source }, new[] { sourceRectangle }, outputName, destinationRectangle, new TextureDispatchExtent(destinationRectangle.Width, destinationRectangle.Height), Array.Empty<float>());
            diagnostic = null;
            return true;
        }

        private static bool TryCompileWord(StackMachineInstruction instruction, int instructionIndex, List<Value> stack, TextureBindingContext context, string outputName, string operationOutput, int outputWidth, int outputHeight, HashSet<string> readSources, out TextureDispatchRecord record, out StackMachineDiagnostic diagnostic)
        {
            record = null;
            diagnostic = null;
            string word = instruction.WordId;
            if (word == TextureWordSet.Fill)
            {
                if (!TryPop(stack, 4, instructionIndex, out Value[] values, out diagnostic)) return false;
                for (int i = 0; i < 4; i++) if (values[i].Kind != ValueKind.Number || values[i].Number < 0d || values[i].Number > 1d || double.IsNaN(values[i].Number) || double.IsInfinity(values[i].Number)) return Fail("InvalidFillColor", "FILL requires finite [0,1] RGBA Number literals.", instructionIndex, out diagnostic);
                TextureDispatchRectangle whole = WholeRectangle(outputWidth, outputHeight);
                record = new TextureDispatchRecord(TextureDispatchOperation.Fill, Array.Empty<string>(), Array.Empty<TextureDispatchRectangle>(), operationOutput, whole, new TextureDispatchExtent(outputWidth, outputHeight), new[] { (float)values[0].Number, (float)values[1].Number, (float)values[2].Number, (float)values[3].Number });
                return true;
            }

            int sourceCount = word == TextureWordSet.Yuv ? 3 : word == TextureWordSet.NormalDeltaAdd ? 3 : word == TextureWordSet.Add || word == TextureWordSet.Subtract || word == TextureWordSet.Multiply || word == TextureWordSet.AlphaOver || word == TextureWordSet.PremultipliedAlphaOver || word == TextureWordSet.Alpha || word == TextureWordSet.NormalWeightedBlend ? 2 : 1;
            bool isCopy = word == TextureWordSet.Copy;
            int colorizeScalarCount = word == TextureWordSet.Colorize ? 3 : 0;
            int count = sourceCount + (isCopy ? 1 : 0) + (word == TextureWordSet.NormalWeightedBlend || word == TextureWordSet.NormalDeltaAdd ? 1 : 0) + colorizeScalarCount;
            if (!TryPop(stack, count, instructionIndex, out Value[] operands, out diagnostic)) return false;
            int scalarIndex = word == TextureWordSet.NormalWeightedBlend ? 2 : word == TextureWordSet.NormalDeltaAdd ? 3 : -1;
            float[] colorizeScalars = null;
            if (word == TextureWordSet.NormalWeightedBlend && (operands[scalarIndex].Kind != ValueKind.Number || operands[scalarIndex].Number < 0d || operands[scalarIndex].Number > 1d || double.IsNaN(operands[scalarIndex].Number) || double.IsInfinity(operands[scalarIndex].Number))) return Fail("InvalidNormalWeight", "NORMAL_WEIGHTED_BLEND requires a finite [0,1] weight literal.", instructionIndex, out diagnostic);
            if (word == TextureWordSet.NormalDeltaAdd && (operands[scalarIndex].Kind != ValueKind.Number || double.IsNaN(operands[scalarIndex].Number) || double.IsInfinity(operands[scalarIndex].Number) || operands[scalarIndex].Number < float.MinValue || operands[scalarIndex].Number > float.MaxValue)) return Fail("InvalidNormalWeight", "NORMAL_DELTA_ADD requires a finite float-range raw weight literal.", instructionIndex, out diagnostic);
            if (word == TextureWordSet.Colorize && !TryColorizeScalars(operands, out colorizeScalars)) return Fail("InvalidColorizeParameters", "COLORIZE requires finite hue and saturation in [0,1] and lightness in [-1,1].", instructionIndex, out diagnostic);
            string destination = operationOutput;
            if (isCopy && !TryDestination(operands[1], context, outputName, instructionIndex, out destination, out diagnostic)) return false;
            var sources = new string[sourceCount];
            for (int i = 0; i < sourceCount; i++)
            {
                int operandIndex = word == TextureWordSet.AlphaOver || word == TextureWordSet.PremultipliedAlphaOver ? 1 - i : word == TextureWordSet.NormalWeightedBlend && i == 1 ? 1 : i;
                if (!TrySource(operands[operandIndex], context, outputName, instructionIndex, out sources[i], out bool readsTexture, out diagnostic)) return false;
                if (readsTexture) readSources.Add(sources[i]);
            }

            TextureDispatchOperation operation = word == TextureWordSet.Copy ? TextureDispatchOperation.Copy : word == TextureWordSet.AlphaOver ? TextureDispatchOperation.AlphaOver : word == TextureWordSet.PremultipliedAlphaOver ? TextureDispatchOperation.PremultipliedAlphaOver : word == TextureWordSet.Alpha ? TextureDispatchOperation.Alpha : word == TextureWordSet.Add ? TextureDispatchOperation.Add : word == TextureWordSet.Subtract ? TextureDispatchOperation.Subtract : word == TextureWordSet.Multiply ? TextureDispatchOperation.Multiply : word == TextureWordSet.Yuv ? TextureDispatchOperation.Yuv : word == TextureWordSet.Colorize ? TextureDispatchOperation.Colorize : word == TextureWordSet.Resample ? TextureDispatchOperation.Resample : word == TextureWordSet.NormalBase ? TextureDispatchOperation.NormalBase : word == TextureWordSet.NormalDeltaAdd ? TextureDispatchOperation.NormalDeltaAdd : word == TextureWordSet.NormalFinalize ? TextureDispatchOperation.NormalFinalize : TextureDispatchOperation.NormalWeightedBlend;
            var sourceRectangles = new TextureDispatchRectangle[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                if (!TryGetSourceExtent(sources[i], context, outputName, outputWidth, outputHeight, out int sourceWidth, out int sourceHeight, out diagnostic)) return false;
                if (operation != TextureDispatchOperation.Resample && (sourceWidth != outputWidth || sourceHeight != outputHeight)) return Fail("TextureExtentMismatch", "COPY and compositing operations require source and output halls of the same extent; use RESAMPLE for an explicit conversion.", instructionIndex, out diagnostic);
                sourceRectangles[i] = WholeRectangle(sourceWidth, sourceHeight);
            }
            TextureDispatchRectangle destinationRectangle = WholeRectangle(outputWidth, outputHeight);
            record = new TextureDispatchRecord(operation, sources, sourceRectangles, destination, destinationRectangle, new TextureDispatchExtent(outputWidth, outputHeight), word == TextureWordSet.Colorize ? colorizeScalars : scalarIndex >= 0 ? new[] { (float)operands[scalarIndex].Number } : Array.Empty<float>());
            return true;
        }

        private static bool TryColorizeScalars(Value[] operands, out float[] scalars)
        {
            scalars = null;
            if (operands.Length != 4 || operands[1].Kind != ValueKind.Number || operands[2].Kind != ValueKind.Number || operands[3].Kind != ValueKind.Number) return false;
            double hue = operands[1].Number, saturation = operands[2].Number, lightness = operands[3].Number;
            if (double.IsNaN(hue) || double.IsInfinity(hue) || double.IsNaN(saturation) || double.IsInfinity(saturation) || double.IsNaN(lightness) || double.IsInfinity(lightness) || hue < 0d || hue > 1d || saturation < 0d || saturation > 1d || lightness < -1d || lightness > 1d) return false;
            scalars = new[] { (float)hue, (float)saturation, (float)lightness };
            return true;
        }

        private static TextureDispatchRectangle WholeRectangle(int width, int height) => new TextureDispatchRectangle(0, 0, width, height);

        private static bool TryRectangle(Value[] values, int start, string label, int instructionIndex, out TextureDispatchRectangle rectangle, out StackMachineDiagnostic diagnostic)
        {
            rectangle = default;
            if (values.Length < start + 4) return Fail("PlaceRectangleInvalid", "PLACE requires complete source and destination rectangles.", instructionIndex, out diagnostic);
            int[] components = new int[4];
            for (int i = 0; i < 4; i++)
            {
                Value value = values[start + i];
                if (value.Kind != ValueKind.Number || value.Number != Math.Floor(value.Number) || value.Number < int.MinValue || value.Number > int.MaxValue) return Fail("PlaceRectangleInvalid", "PLACE " + label + " rectangle values must be finite integral texel values.", instructionIndex, out diagnostic);
                components[i] = (int)value.Number;
            }
            if (components[0] < 0 || components[1] < 0 || components[2] <= 0 || components[3] <= 0) return Fail("PlaceRectangleInvalid", "PLACE rectangle origins must be non-negative and extents must be positive.", instructionIndex, out diagnostic);
            rectangle = new TextureDispatchRectangle(components[0], components[1], components[2], components[3]);
            diagnostic = null;
            return true;
        }

        private static bool Contains(int width, int height, TextureDispatchRectangle rectangle)
            => rectangle.X <= width && rectangle.Y <= height && rectangle.Width <= width - rectangle.X && rectangle.Height <= height - rectangle.Y;

        private static bool Overlaps(TextureDispatchRectangle left, TextureDispatchRectangle right)
            => (long)left.X < (long)right.X + right.Width && (long)right.X < (long)left.X + left.Width && (long)left.Y < (long)right.Y + right.Height && (long)right.Y < (long)left.Y + left.Height;

        private static bool TryPlaceSource(Value value, TextureBindingContext context, int index, out string source, out StackMachineDiagnostic diagnostic)
        {
            source = null;
            if (value.Kind != ValueKind.Binding || !context.TryGetBinding(value.Binding, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture) return Fail("PlaceSourceRequired", "PLACE requires one SourceTexture logical binding.", index, out diagnostic);
            source = value.Binding;
            diagnostic = null;
            return true;
        }

        private static bool TryGetSourceExtent(string logicalName, TextureBindingContext context, string outputName, int outputWidth, int outputHeight, out int width, out int height, out StackMachineDiagnostic diagnostic)
        {
            width = 0;
            height = 0;
            diagnostic = null;
            if (IsTemporary(logicalName)) { width = outputWidth; height = outputHeight; return true; }
            if (!context.TryGetBinding(logicalName, out TextureBinding binding)) return Fail("TextureBindingMissing", "Texture source binding is missing.", -1, out diagnostic);
            if (binding.Kind == TextureBindingKind.OutputHall)
            {
                width = outputWidth;
                height = outputHeight;
                return true;
            }

            Texture texture = binding.SourceTexture;
            if (texture == null) return Fail("SourceTextureRequired", "Texture source binding requires a Texture.", -1, out diagnostic);
            width = texture.width;
            height = texture.height;
            return true;
        }

        private static bool TryPop(List<Value> stack, int count, int instructionIndex, out Value[] values, out StackMachineDiagnostic diagnostic)
        {
            values = null;
            diagnostic = null;
            if (stack.Count < count) return Fail("TextureStackUnderflow", "Texture domain compile stack underflow.", instructionIndex, out diagnostic);
            values = stack.GetRange(stack.Count - count, count).ToArray();
            stack.RemoveRange(stack.Count - count, count);
            return true;
        }

        private static bool TryDestination(Value value, TextureBindingContext context, string outputName, int index, out string output, out StackMachineDiagnostic diagnostic)
        {
            output = null;
            if (value.Kind != ValueKind.Binding || value.Binding != outputName || !context.TryGetBinding(value.Binding, out TextureBinding binding) || binding.Kind != TextureBindingKind.OutputHall) return Fail("OutputHallRequired", "Texture word destination must be the recipe OutputHall.", index, out diagnostic);
            output = value.Binding;
            diagnostic = null;
            return true;
        }

        private static bool TrySource(Value value, TextureBindingContext context, string outputName, int index, out string source, out bool readsTexture, out StackMachineDiagnostic diagnostic)
        {
            source = null;
            readsTexture = false;
            if (value.Kind == ValueKind.Binding && IsTemporary(value.Binding)) { source = value.Binding; diagnostic = null; return true; }
            if (value.Kind != ValueKind.Binding || !context.TryGetBinding(value.Binding, out TextureBinding binding)) return Fail("TextureSourceRequired", "Texture word source must be a logical hall.", index, out diagnostic);
            source = value.Binding;
            if (binding.Kind == TextureBindingKind.SourceTexture) { readsTexture = true; diagnostic = null; return true; }
            if (binding.Kind == TextureBindingKind.OutputHall && source == outputName) { diagnostic = null; return true; }
            return Fail("TextureSourceRequired", "Texture word source binding is invalid.", index, out diagnostic);
        }

        private static string[] Copy(HashSet<string> values)
        {
            var copy = new string[values.Count];
            values.CopyTo(copy);
            Array.Sort(copy, StringComparer.Ordinal);
            return copy;
        }

        private static bool Fail(string code, string message, int instructionIndex, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", code, message, instructionPointer: instructionIndex);
            return false;
        }
    }
}
