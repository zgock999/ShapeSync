// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Internal capability probe seam used only to verify concrete EditMode rejection behavior.</summary>
    internal delegate bool TextureEditModeCapabilityProbe(out TextureGpuCapability capability, out StackMachineDiagnostic diagnostic);

    /// <summary>Represents the terminal state of one caller-driven EditMode Texture execution.</summary>
    public enum TextureEditModeExecutionStatus
    {
        /// <summary>No plan is active.</summary>
        Idle,
        /// <summary>GPU work was submitted and requires a later caller <see cref="TextureEditModeStackMachine.Pump"/>.</summary>
        Pending,
        /// <summary>The output is available for a single handoff.</summary>
        Succeeded,
        /// <summary>Compilation, capability, or GPU submission failed.</summary>
        Failed,
        /// <summary>The caller cancelled the active execution.</summary>
        Cancelled
    }

    /// <summary>Owns one in-memory linear RGBAHalf completion until its receiver disposes it.</summary>
    public sealed class TextureCompletion : IDisposable
    {
        private Action<RenderTexture> release;

        internal TextureCompletion(RenderTexture texture, Action<RenderTexture> release)
        {
            Texture = texture;
            this.release = release;
        }

        /// <summary>Gets the exact-extent in-memory output texture until disposal.</summary>
        public RenderTexture Texture { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            RenderTexture owned = Texture;
            Texture = null;
            Action<RenderTexture> callback = release;
            release = null;
            if (owned != null) callback?.Invoke(owned);
        }
    }

    /// <summary>
    /// Editor-only, caller-driven concrete executor for a shared <see cref="TextureExecutionPlan"/>.
    /// This class owns only its submitted dispatch and temporary GPU textures. It does not publish assets,
    /// perform GPU readback, register an Editor update callback, or own compiler rollback.
    /// </summary>
    public sealed class TextureEditModeStackMachine : IDisposable
    {
        private static readonly string[] ComputeKernelNames =
        {
            "KFill", "KCopy", "KAlphaOver", "KPremultipliedAlphaOver", "KAdd", "KMultiply", "KYuv", "KResample", "KNormalWeightedBlend", "KAlpha", "KSubtract", "KIngest", "KPublish", "KColorize"
        };
        private static readonly string[] NormalKernelNames = { "KNormalBase", "KNormalDeltaAdd", "KNormalFinalize" };

        private readonly ComputeShader computeProgram;
        private readonly ComputeShader normalComputeProgram;
        private readonly TextureEditModeCapabilityProbe capabilityProbe;
        private readonly Dictionary<string, RenderTexture> temporaryTextures = new Dictionary<string, RenderTexture>(StringComparer.Ordinal);
        private readonly Queue<TextureExecutionPlan> pendingPlans = new Queue<TextureExecutionPlan>();
        private int[] kernels;
        private int[] normalKernels;
        private GraphicsFence fence;
        private RenderTexture output;
        private TextureCompletion completion;
        private bool disposed;

        /// <summary>Creates an EditMode executor using explicitly supplied Texture compute programs.</summary>
        public TextureEditModeStackMachine(ComputeShader computeProgram, ComputeShader normalComputeProgram = null)
            : this(computeProgram, normalComputeProgram, TextureGpuCapabilityProbe.TryProbe)
        {
        }

        /// <summary>Creates the concrete executor with an internal capability seam for EditMode white-box tests.</summary>
        internal TextureEditModeStackMachine(ComputeShader computeProgram, ComputeShader normalComputeProgram, TextureEditModeCapabilityProbe capabilityProbe)
        {
            this.computeProgram = computeProgram;
            this.normalComputeProgram = normalComputeProgram;
            this.capabilityProbe = capabilityProbe ?? TextureGpuCapabilityProbe.TryProbe;
        }

        /// <summary>Gets the current execution state. Advancing Pending work requires an explicit <see cref="Pump"/> call.</summary>
        public TextureEditModeExecutionStatus Status { get; private set; }

        /// <summary>Gets the structured diagnostic for a Failed state; otherwise <see langword="null"/>.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }

        /// <summary>Starts one GPU execution. The caller must subsequently invoke <see cref="Pump"/>.</summary>
        /// <param name="plan">Immutable shared-Core texture execution plan.</param>
        /// <param name="diagnostic">Structured rejection diagnostic on failure.</param>
        /// <returns><see langword="true"/> when work was submitted to the GPU.</returns>
        public bool Start(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Fail("EditModeTextureMachineDisposed", "TextureEditModeStackMachine has been disposed.", out diagnostic);
            if (plan == null) return Fail("TextureExecutionPlanRequired", "EditMode Texture execution requires a TextureExecutionPlan.", out diagnostic);
            if (Status == TextureEditModeExecutionStatus.Pending)
            {
                if (!TryValidateExecutionEnvironment(plan, out diagnostic)) return false;
                pendingPlans.Enqueue(plan);
                return true;
            }
            if (completion != null) return Fail("TextureCompletionNotTaken", "Take or dispose the prior TextureCompletion before starting another execution.", out diagnostic);
            return StartNow(plan, out diagnostic);
        }

        private bool StartNow(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            if (!TryValidateExecutionEnvironment(plan, out diagnostic)) return SetFailure(diagnostic);

            ReleaseTransientTextures();
            ReleaseOutput();
            Diagnostic = null;
            try
            {
                if (!TryCreateOutput(plan.DispatchPlan.OutputWidth, plan.DispatchPlan.OutputHeight, out diagnostic)) return SetFailure(diagnostic);
                using (var command = new CommandBuffer { name = "ShapeSync.EditModeTextureStackMachine" })
                {
                    for (int i = 0; i < plan.DispatchPlan.Records.Count; i++)
                    {
                        TextureDispatchRecord record = plan.DispatchPlan.Records[i];
                        if (!TryResolveDestination(plan, record.Output, out RenderTexture destination, out diagnostic) ||
                            !TryResolveSource(plan, record, 0, out Texture sourceA, out diagnostic) ||
                            !TryResolveSource(plan, record, 1, out Texture sourceB, out diagnostic) ||
                            !TryResolveSource(plan, record, 2, out Texture sourceC, out diagnostic))
                            return SetFailure(diagnostic);
                        if (ReferenceEquals(destination, sourceA) || ReferenceEquals(destination, sourceB) || ReferenceEquals(destination, sourceC))
                            return SetFailure(StackMachineDiagnostic.CreateDomain("texture", "InPlaceReadWrite", "Texture operations may not read from and write to the same texture.", instructionPointer: i));
                        if (!TryGetKernel(record.Operation, out ComputeShader program, out int kernel))
                            return SetFailure(StackMachineDiagnostic.CreateDomain("texture", "DispatchOperationUnsupported", "Compiled Texture dispatch operation has no Compute kernel.", instructionPointer: i));

                        SetRecordCommon(command, program, kernel, record, sourceA, sourceB, sourceC);
                        command.SetComputeTextureParam(program, kernel, "_SourceA", sourceA ?? Texture2D.blackTexture);
                        command.SetComputeTextureParam(program, kernel, "_SourceB", sourceB ?? Texture2D.blackTexture);
                        command.SetComputeTextureParam(program, kernel, "_SourceC", sourceC ?? Texture2D.blackTexture);
                        command.SetComputeTextureParam(program, kernel, "_Destination", destination);
                        if (record.Operation == TextureDispatchOperation.Fill) command.SetComputeVectorParam(program, "_FillColor", new Vector4(record.Scalars[0], record.Scalars[1], record.Scalars[2], record.Scalars[3]));
                        if (record.Operation == TextureDispatchOperation.NormalWeightedBlend || record.Operation == TextureDispatchOperation.NormalDeltaAdd) command.SetComputeFloatParam(program, "_Weight", record.Scalars[0]);
                        if (record.Operation == TextureDispatchOperation.Colorize) command.SetComputeVectorParam(program, "_Colorize", new Vector4(record.Scalars[0], record.Scalars[1], record.Scalars[2], 0f));
                        command.DispatchCompute(program, kernel, (record.RecordExtent.Width + 7) / 8, (record.RecordExtent.Height + 7) / 8, 1);
                    }
                    fence = command.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
                    Graphics.ExecuteCommandBuffer(command);
                }
            }
            catch (Exception exception)
            {
                return SetFailure(StackMachineDiagnostic.CreateDomain("texture", "EditModeTextureDispatchFailed", "EditMode Texture GPU dispatch failed.", detail: exception.Message));
            }

            Status = TextureEditModeExecutionStatus.Pending;
            return true;
        }

        /// <summary>Advances pending GPU work once. This class never schedules this call itself.</summary>
        /// <param name="diagnostic">Terminal failure diagnostic, when any.</param>
        /// <returns>The resulting execution state.</returns>
        public TextureEditModeExecutionStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (Status == TextureEditModeExecutionStatus.Pending)
            {
                if (!fence.passed) return Status;
                ReleaseTransientTextures();
                completion = new TextureCompletion(output, ReleaseTexture);
                output = null;
                Status = TextureEditModeExecutionStatus.Succeeded;
                return Status;
            }
            if (Status == TextureEditModeExecutionStatus.Idle && pendingPlans.Count > 0)
            {
                TextureExecutionPlan next = pendingPlans.Dequeue();
                if (!StartNow(next, out diagnostic)) return Status;
            }
            else diagnostic = Diagnostic;
            return Status;
        }

        /// <summary>Transfers the terminal completion exactly once.</summary>
        public bool TryTakeCompletion(out TextureCompletion value)
        {
            value = completion;
            completion = null;
            if (value == null) return false;
            Status = TextureEditModeExecutionStatus.Idle;
            return true;
        }

        /// <summary>Cancels pending work and releases only TSM-owned temporary resources.</summary>
        public void Cancel()
        {
            if (disposed) return;
            bool hasWork = Status == TextureEditModeExecutionStatus.Pending || Status == TextureEditModeExecutionStatus.Succeeded || pendingPlans.Count > 0;
            if (!hasWork) return;
            pendingPlans.Clear();
            ReleaseTransientTextures();
            ReleaseOutput();
            completion?.Dispose();
            completion = null;
            pendingPlans.Clear();
            Status = TextureEditModeExecutionStatus.Cancelled;
            Diagnostic = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            completion?.Dispose();
            completion = null;
            ReleaseTransientTextures();
            ReleaseOutput();
            disposed = true;
        }

        private bool TryCacheKernels(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (computeProgram == null) return Fail("ComputeProgramRequired", "EditMode Texture execution requires a ComputeShader.", out diagnostic);
            if (kernels == null && !TryFindKernels(computeProgram, ComputeKernelNames, "ComputeKernelMissing", out kernels, out diagnostic)) return false;
            if (!UsesNormalOperations(plan.DispatchPlan)) return true;
            if (normalComputeProgram == null) return Fail("NormalComputeProgramRequired", "Normal Texture operations require a dedicated ComputeShader.", out diagnostic);
            return normalKernels != null || TryFindKernels(normalComputeProgram, NormalKernelNames, "NormalComputeKernelMissing", out normalKernels, out diagnostic);
        }

        private bool TryValidateExecutionEnvironment(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            return capabilityProbe(out _, out diagnostic) && TryCacheKernels(plan, out diagnostic);
        }

        private static bool TryFindKernels(ComputeShader program, string[] names, string code, out int[] found, out StackMachineDiagnostic diagnostic)
        {
            found = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                try { found[i] = program.FindKernel(names[i]); }
                catch (Exception) { found = null; return Fail(code, "Texture ComputeShader is missing a required kernel.", out diagnostic, names[i]); }
            }
            diagnostic = null;
            return true;
        }

        private bool TryCreateOutput(int width, int height, out StackMachineDiagnostic diagnostic)
        {
            output = CreateTexture(width, height, "ShapeSync.EditModeTextureStackMachine.Output");
            if (output != null) { diagnostic = null; return true; }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DeliveryCreateFailed", "Failed to create the EditMode Texture completion RenderTexture.");
            return false;
        }

        private bool TryResolveDestination(TextureExecutionPlan plan, string logicalName, out RenderTexture texture, out StackMachineDiagnostic diagnostic)
        {
            if (logicalName == plan.BindingContext.OutputLogicalName) { texture = output; diagnostic = null; return texture != null; }
            if (temporaryTextures.TryGetValue(logicalName, out texture)) { diagnostic = null; return true; }
            texture = CreateTexture(plan.DispatchPlan.OutputWidth, plan.DispatchPlan.OutputHeight, "ShapeSync.EditModeTextureStackMachine.Temporary");
            if (texture == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TemporaryTextureCreateFailed", "Failed to create an EditMode Texture temporary RenderTexture.", bindingName: logicalName); return false; }
            temporaryTextures.Add(logicalName, texture);
            diagnostic = null;
            return true;
        }

        private bool TryResolveSource(TextureExecutionPlan plan, TextureDispatchRecord record, int index, out Texture texture, out StackMachineDiagnostic diagnostic)
        {
            texture = null;
            diagnostic = null;
            if (index >= record.Sources.Count) return true;
            string logicalName = record.Sources[index];
            if (temporaryTextures.TryGetValue(logicalName, out RenderTexture temporary)) { texture = temporary; return true; }
            if (logicalName == plan.BindingContext.OutputLogicalName)
            {
                if (output == null) return Fail("OutputReadBeforeWrite", "Texture output cannot be read before an earlier operation writes it.", out diagnostic);
                texture = output;
                return true;
            }
            if (!plan.BindingContext.TryGetBinding(logicalName, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture || binding.SourceTexture == null)
                return Fail("DispatchSourceUnresolved", "Compiled Texture record refers to an unresolved source binding.", out diagnostic, logicalName);
            texture = binding.SourceTexture;
            return true;
        }

        private bool TryGetKernel(TextureDispatchOperation operation, out ComputeShader program, out int kernel)
        {
            program = computeProgram;
            if (operation == TextureDispatchOperation.NormalBase || operation == TextureDispatchOperation.NormalDeltaAdd || operation == TextureDispatchOperation.NormalFinalize)
            {
                int index = operation == TextureDispatchOperation.NormalBase ? 0 : operation == TextureDispatchOperation.NormalDeltaAdd ? 1 : 2;
                program = normalComputeProgram;
                kernel = normalKernels == null ? -1 : normalKernels[index];
                return program != null && kernel >= 0;
            }
            int computeIndex = operation == TextureDispatchOperation.Fill ? 0 : operation == TextureDispatchOperation.Copy ? 1 : operation == TextureDispatchOperation.Place || operation == TextureDispatchOperation.Resample ? 7 : operation == TextureDispatchOperation.AlphaOver ? 2 : operation == TextureDispatchOperation.PremultipliedAlphaOver ? 3 : operation == TextureDispatchOperation.Add ? 4 : operation == TextureDispatchOperation.Multiply ? 5 : operation == TextureDispatchOperation.Yuv ? 6 : operation == TextureDispatchOperation.NormalWeightedBlend ? 8 : operation == TextureDispatchOperation.Alpha ? 9 : operation == TextureDispatchOperation.Subtract ? 10 : operation == TextureDispatchOperation.Colorize ? 13 : -1;
            kernel = computeIndex < 0 || kernels == null ? -1 : kernels[computeIndex];
            return kernel >= 0;
        }

        private static bool UsesNormalOperations(TextureDispatchPlan plan)
        {
            for (int i = 0; i < plan.Records.Count; i++)
            {
                TextureDispatchOperation operation = plan.Records[i].Operation;
                if (operation == TextureDispatchOperation.NormalBase || operation == TextureDispatchOperation.NormalDeltaAdd || operation == TextureDispatchOperation.NormalFinalize) return true;
            }
            return false;
        }

        private static void SetRecordCommon(CommandBuffer command, ComputeShader program, int kernel, TextureDispatchRecord record, Texture sourceA, Texture sourceB, Texture sourceC)
        {
            command.SetComputeIntParam(program, "_Width", record.RecordExtent.Width);
            command.SetComputeIntParam(program, "_Height", record.RecordExtent.Height);
            TextureDispatchRectangle sourceRectangle = SourceRectangle(record, 0, sourceA);
            command.SetComputeVectorParam(program, "_SourceExtent", new Vector4(sourceRectangle.Width, sourceRectangle.Height, 0f, 0f));
            command.SetComputeVectorParam(program, "_DestinationOffset", new Vector4(record.DestinationRectangle.X, record.DestinationRectangle.Y, 0f, 0f));
            command.SetComputeVectorParam(program, "_SourceAOffset", Offset(SourceRectangle(record, 0, sourceA)));
            command.SetComputeVectorParam(program, "_SourceBOffset", Offset(SourceRectangle(record, 1, sourceB)));
            command.SetComputeVectorParam(program, "_SourceCOffset", Offset(SourceRectangle(record, 2, sourceC)));
        }

        private static TextureDispatchRectangle SourceRectangle(TextureDispatchRecord record, int index, Texture source)
            => index < record.SourceRectangles.Count ? record.SourceRectangles[index] : new TextureDispatchRectangle(0, 0, source == null ? record.RecordExtent.Width : source.width, source == null ? record.RecordExtent.Height : source.height);

        private static Vector4 Offset(TextureDispatchRectangle rectangle) => new Vector4(rectangle.X, rectangle.Y, 0f, 0f);

        private static RenderTexture CreateTexture(int width, int height, string name)
        {
            var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0) { enableRandomWrite = true, msaaSamples = 1, sRGB = false, useMipMap = false, autoGenerateMips = false };
            var texture = new RenderTexture(descriptor) { name = name };
            if (texture.Create()) return texture;
            UnityEngine.Object.DestroyImmediate(texture);
            return null;
        }

        private void ReleaseTransientTextures()
        {
            foreach (RenderTexture texture in temporaryTextures.Values) ReleaseTexture(texture);
            temporaryTextures.Clear();
        }

        private void ReleaseOutput()
        {
            if (output == null) return;
            ReleaseTexture(output);
            output = null;
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private bool SetFailure(StackMachineDiagnostic diagnostic)
        {
            ReleaseTransientTextures();
            ReleaseOutput();
            Diagnostic = diagnostic;
            Status = TextureEditModeExecutionStatus.Failed;
            return false;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", code, message, detail: detail);
            return false;
        }
    }
}
