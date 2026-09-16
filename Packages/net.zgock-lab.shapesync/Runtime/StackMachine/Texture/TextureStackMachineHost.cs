// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Scene-local owner of the fixed Texture StackMachine grid, GPU queue, and Compute programs.</summary>
    /// <remarks><see cref="TextureStaticMachineFactory"/> resolves this component for Texture callers but never owns its state.</remarks>
    public sealed partial class TextureStackMachineHost : MonoBehaviour
    {
        /// <summary>Raised synchronously before this scene-scoped host releases its deliveries.</summary>
        public event Action Destroying;

        [SerializeField] private ComputeShader computeProgram;
        [SerializeField] private ComputeShader normalComputeProgram;

        private RenderTexture grid;
        private TextureHallAllocator allocator;
        private TextureGpuCapability capability;
        private int[] kernels;
        private int[] normalKernels;
        private bool initialized;
        private readonly Queue<QueuedRequest> pending = new Queue<QueuedRequest>();
        private readonly Dictionary<TextureExecutionOriginKey, QueuedRequest> pendingByOrigin = new Dictionary<TextureExecutionOriginKey, QueuedRequest>();
        private readonly HashSet<TextureDelivery> outstandingDeliveries = new HashSet<TextureDelivery>();
        private readonly HashSet<TextureDelivery> handedOffDeliveries = new HashSet<TextureDelivery>();
        private readonly HashSet<TextureSourceLease> outstandingSourceLeases = new HashSet<TextureSourceLease>();
        private readonly HashSet<TextureOutputLease> outstandingOutputLeases = new HashSet<TextureOutputLease>();
        private QueuedRequest submitted;
        private ulong nextOrigin = 1;

        private void Awake() => TextureStaticMachineFactory.Invalidate(gameObject.scene);
        private void OnEnable() => EnablePrepared();
        private void OnDisable() => DisablePrepared();
        private void Update() => UpdatePrepared();
        private void OnDestroy() => DestroyPrepared();

        public bool IsInitialized => initialized;
        public TextureGpuCapability Capability => capability;
        public RenderTexture Grid => grid;
        public ComputeShader ComputeProgram => computeProgram;
        public ComputeShader NormalComputeProgram => normalComputeProgram;
        public int PendingRequestCount => pending.Count;
        public bool HasSubmittedRequest => submitted != null;
        public int OutstandingSourceLeaseCount => outstandingSourceLeases.Count;
        public int OutstandingOutputLeaseCount => outstandingOutputLeases.Count;
        public int HandedOffDeliveryCount => handedOffDeliveries.Count;
        public long LiveTransientGpuBytes => GetLiveTransientGpuBytes();
        internal int IngestDispatchCount { get; private set; }

        public bool TryAssignComputeProgram(ComputeShader value, out StackMachineDiagnostic diagnostic)
        {
            if (initialized) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostAlreadyInitialized", "Compute program cannot change after host initialization."); return false; }
            if (value == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "ComputeProgramRequired", "TextureStackMachineHost requires a ComputeShader."); return false; }
            computeProgram = value;
            diagnostic = null;
            return true;
        }

        public bool TryAssignNormalComputeProgram(ComputeShader value, out StackMachineDiagnostic diagnostic)
        {
            if (value == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "NormalComputeProgramRequired", "Normal vector operations require a dedicated ComputeShader."); return false; }
            normalComputeProgram = value;
            normalKernels = null;
            return TryEnsureNormalKernels(out diagnostic);
        }

        public TextureExecutionOriginKey CreateOrigin()
        {
            if (nextOrigin == 0) throw new InvalidOperationException("TextureStackMachineHost origin token space is exhausted.");
            return new TextureExecutionOriginKey(this, nextOrigin++);
        }

        public bool TryValidateAdmission(TextureExecutionPlan plan, bool outputAlreadyReserved, out StackMachineDiagnostic diagnostic)
        {
            if (!initialized) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostNotInitialized", "TextureStackMachineHost is not initialized."); return false; }
            if (plan == null || plan.BindingContext == null || plan.DispatchPlan == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TexturePlanRequired", "Texture admission requires a compiled Texture execution plan."); return false; }
            var widths = new List<int>();
            var heights = new List<int>();
            foreach (string sourceName in plan.DispatchPlan.ReadSourceNames)
            {
                if (!plan.BindingContext.TryGetBinding(sourceName, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture || binding.SourceTexture == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceBindingRequired", "Compiled Texture plan refers to an unresolved source binding.", bindingName: sourceName);
                    return false;
                }
                widths.Add(binding.SourceTexture.width);
                heights.Add(binding.SourceTexture.height);
            }
            if (!outputAlreadyReserved) { widths.Add(plan.DispatchPlan.OutputWidth); heights.Add(plan.DispatchPlan.OutputHeight); }
            if (allocator != null && allocator.TryCanReserveSequence(widths, heights)) { diagnostic = null; return true; }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "AtlasLiveAdmissionRejected", "The live Texture StackMachine grid cannot admit the Atlas recipe without evicting existing halls.", detail: "sources=" + plan.DispatchPlan.ReadSourceNames.Count + ";output=" + plan.DispatchPlan.OutputWidth + "x" + plan.DispatchPlan.OutputHeight + ";outputRetained=" + outputAlreadyReserved + ";retainedSourceLeases=" + outstandingSourceLeases.Count + ";retainedOutputLeases=" + outstandingOutputLeases.Count);
            return false;
        }

        public bool TryInitialize(out StackMachineDiagnostic diagnostic) => TryInitializePrepared(out diagnostic);

        public bool TryReserveHall(int width, int height, out TextureHallAllocation allocation, out StackMachineDiagnostic diagnostic)
        {
            allocation = default;
            if (!initialized)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostNotInitialized", "TextureStackMachineHost is not initialized.");
                return false;
            }
            if (!TryValidateExtentWithinGrid(width, height, capability.FixedGridEdge, out diagnostic)) return false;
            if (!allocator.TryReserve(width, height, out allocation))
            {
                int roomsPerAxis = capability.FixedGridEdge / TextureHallAllocator.RoomEdge;
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HallReservationFailed", "The fixed Texture StackMachine grid has no compatible free hall.", detail: "request=" + width + "x" + height + "; grid=" + capability.FixedGridEdge + "x" + capability.FixedGridEdge + "; occupiedRooms=" + allocator.OccupiedRoomCount + "/" + roomsPerAxis * roomsPerAxis + "; retainedSourceLeases=" + outstandingSourceLeases.Count + "; retainedOutputLeases=" + outstandingOutputLeases.Count);
                return false;
            }
            diagnostic = null;
            return true;
        }

        public bool TryReleaseHall(TextureHallAllocation allocation) => initialized && allocator.TryRelease(allocation);

        internal bool TryEnqueue(TextureDispatchPlan plan, TextureBindingContext context, TextureExecutionOriginKey origin, TextureExecutionHandle handle, TextureExecutionOptions options, out StackMachineDiagnostic diagnostic)
            => TryEnqueuePrepared(plan, context, origin, handle, options, out diagnostic);

        internal void Cancel(TextureExecutionHandle handle) => CancelPrepared(handle);

        internal void EnqueueNotification(TextureExecutionHandle handle)
        {
            if (handle != null) notificationQueue.Enqueue(handle);
        }

        internal void FlushNotifications() => FlushPreparedNotifications();

        internal void ReleaseSourceLease(TextureSourceLease lease)
        {
            if (lease == null || !outstandingSourceLeases.Remove(lease)) return;
            lease.ReleaseFromHost(this);
        }

        internal void ReleaseOutputLease(TextureOutputLease lease)
        {
            if (lease == null || !outstandingOutputLeases.Remove(lease)) return;
            lease.ReleaseFromHost(this);
        }

        private long GetLiveTransientGpuBytes()
        {
            long bytes = 0;
            foreach (TextureDelivery delivery in outstandingDeliveries) bytes += GetTextureBytes(delivery.Texture);
            return bytes;
        }

        internal static bool TryValidateExtentWithinGrid(int width, int height, int fixedGridEdge, out StackMachineDiagnostic diagnostic)
        {
            if (TextureGpuCapabilityProbe.IsPhase0Edge(width) && TextureGpuCapabilityProbe.IsPhase0Edge(height) && width <= fixedGridEdge && height <= fixedGridEdge)
            {
                diagnostic = null;
                return true;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputExtentExceedsGrid", "Texture hall width and height must be supported extents within the fixed grid.");
            return false;
        }

        internal static bool TryValidateDeliveryReservation(long gridBytes, long outstandingDeliveryBytes, long pendingDeliveryBytes, long candidateDeliveryBytes, long gpuBudgetBytes, out StackMachineDiagnostic diagnostic)
        {
            if (gridBytes >= 0 && outstandingDeliveryBytes >= 0 && pendingDeliveryBytes >= 0 && candidateDeliveryBytes >= 0 && gridBytes <= gpuBudgetBytes && outstandingDeliveryBytes <= gpuBudgetBytes - gridBytes && pendingDeliveryBytes <= gpuBudgetBytes - gridBytes - outstandingDeliveryBytes && candidateDeliveryBytes <= gpuBudgetBytes - gridBytes - outstandingDeliveryBytes - pendingDeliveryBytes)
            {
                diagnostic = null;
                return true;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuTransientBudgetExceeded", "Texture delivery reservation exceeds the host GPU budget.");
            return false;
        }

        private static long GetTextureBytes(Texture texture) => texture == null ? 0 : (long)texture.width * texture.height * TextureGpuCapabilityProbe.BytesPerPixel;

        private bool TryCreateDelivery(int width, int height, out TextureDelivery delivery, out StackMachineDiagnostic diagnostic)
        {
            delivery = null;
            var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0)
            {
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };
            var texture = new RenderTexture(descriptor) { name = "ShapeSync.TextureStackMachine.Delivery" };
            if (!texture.Create())
            {
                Destroy(texture);
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DeliveryCreateFailed", "Failed to create the exact-extent Texture StackMachine delivery texture.");
                return false;
            }
            delivery = new TextureDelivery(texture, ReleaseDelivery, MarkDeliveryHandedOff);
            outstandingDeliveries.Add(delivery);
            diagnostic = null;
            return true;
        }

        private void ReleaseDelivery(Texture texture)
        {
            TextureDelivery released = null;
            foreach (TextureDelivery candidate in outstandingDeliveries)
            {
                if (candidate.Texture == null || candidate.Texture == texture) { released = candidate; break; }
            }
            if (released != null) outstandingDeliveries.Remove(released);
            if (released == null)
            {
                foreach (TextureDelivery candidate in handedOffDeliveries)
                {
                    if (candidate.Texture == null || candidate.Texture == texture) { released = candidate; break; }
                }
            }
            if (released != null) handedOffDeliveries.Remove(released);
            if (texture is RenderTexture renderTexture) renderTexture.Release();
            Destroy(texture);
        }

        private void MarkDeliveryHandedOff(TextureDelivery delivery)
        {
            if (delivery != null && outstandingDeliveries.Remove(delivery)) handedOffDeliveries.Add(delivery);
        }

        private static void SetCommon(CommandBuffer command, ComputeShader program, int kernel, int width, int height, TextureHallAllocation destination, TextureHallAllocation sourceA, TextureHallAllocation sourceB, TextureHallAllocation sourceC)
        {
            command.SetComputeIntParam(program, "_Width", width);
            command.SetComputeIntParam(program, "_Height", height);
            command.SetComputeVectorParam(program, "_SourceExtent", sourceA.IsValid ? new Vector4(sourceA.Width, sourceA.Height, 0f, 0f) : new Vector4(width, height, 0f, 0f));
            command.SetComputeVectorParam(program, "_DestinationOffset", new Vector4(destination.PixelX, destination.PixelY, 0f, 0f));
            command.SetComputeVectorParam(program, "_SourceAOffset", new Vector4(sourceA.PixelX, sourceA.PixelY, 0f, 0f));
            command.SetComputeVectorParam(program, "_SourceBOffset", new Vector4(sourceB.PixelX, sourceB.PixelY, 0f, 0f));
            command.SetComputeVectorParam(program, "_SourceCOffset", new Vector4(sourceC.PixelX, sourceC.PixelY, 0f, 0f));
        }

        private static void SetRecordCommon(CommandBuffer command, ComputeShader program, int kernel, TextureDispatchRecord record, TextureHallAllocation destination, TextureHallAllocation sourceA, TextureHallAllocation sourceB, TextureHallAllocation sourceC)
        {
            command.SetComputeIntParam(program, "_Width", record.RecordExtent.Width);
            command.SetComputeIntParam(program, "_Height", record.RecordExtent.Height);
            TextureDispatchRectangle sourceRectangle = SourceRectangle(record, 0, sourceA);
            command.SetComputeVectorParam(program, "_SourceExtent", new Vector4(sourceRectangle.Width, sourceRectangle.Height, 0f, 0f));
            command.SetComputeVectorParam(program, "_DestinationOffset", new Vector4(destination.PixelX + record.DestinationRectangle.X, destination.PixelY + record.DestinationRectangle.Y, 0f, 0f));
            command.SetComputeVectorParam(program, "_SourceAOffset", Offset(sourceA, SourceRectangle(record, 0, sourceA)));
            command.SetComputeVectorParam(program, "_SourceBOffset", Offset(sourceB, SourceRectangle(record, 1, sourceB)));
            command.SetComputeVectorParam(program, "_SourceCOffset", Offset(sourceC, SourceRectangle(record, 2, sourceC)));
        }

        private static TextureDispatchRectangle SourceRectangle(TextureDispatchRecord record, int index, TextureHallAllocation source)
            => index < record.SourceRectangles.Count ? record.SourceRectangles[index] : new TextureDispatchRectangle(0, 0, source.IsValid ? source.Width : record.RecordExtent.Width, source.IsValid ? source.Height : record.RecordExtent.Height);

        private static Vector4 Offset(TextureHallAllocation hall, TextureDispatchRectangle rectangle)
            => new Vector4(hall.PixelX + rectangle.X, hall.PixelY + rectangle.Y, 0f, 0f);

        private bool TryRecordKernel(TextureDispatchOperation operation, out ComputeShader program, out int kernel)
        {
            program = computeProgram;
            if (operation == TextureDispatchOperation.NormalBase || operation == TextureDispatchOperation.NormalDeltaAdd || operation == TextureDispatchOperation.NormalFinalize)
            {
                int normalIndex = operation == TextureDispatchOperation.NormalBase ? 0 : operation == TextureDispatchOperation.NormalDeltaAdd ? 1 : 2;
                program = normalComputeProgram;
                kernel = normalKernels == null ? -1 : normalKernels[normalIndex];
                return program != null && kernel >= 0;
            }
            int index = operation == TextureDispatchOperation.Fill ? 0 : operation == TextureDispatchOperation.Copy ? 1 : operation == TextureDispatchOperation.Place || operation == TextureDispatchOperation.Resample ? 7 : operation == TextureDispatchOperation.AlphaOver ? 2 : operation == TextureDispatchOperation.PremultipliedAlphaOver ? 3 : operation == TextureDispatchOperation.Add ? 4 : operation == TextureDispatchOperation.Multiply ? 5 : operation == TextureDispatchOperation.Yuv ? 6 : operation == TextureDispatchOperation.NormalWeightedBlend ? 8 : operation == TextureDispatchOperation.Alpha ? 9 : operation == TextureDispatchOperation.Subtract ? 10 : operation == TextureDispatchOperation.Colorize ? 13 : -1;
            kernel = index >= 0 && kernels != null && index < kernels.Length ? kernels[index] : -1;
            return index >= 0;
        }

        private bool TryCacheKernels(out StackMachineDiagnostic diagnostic)
        {
            string[] names = { "KFill", "KCopy", "KAlphaOver", "KPremultipliedAlphaOver", "KAdd", "KMultiply", "KYuv", "KResample", "KNormalWeightedBlend", "KAlpha", "KSubtract", "KIngest", "KPublish", "KColorize" };
            kernels = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                try { kernels[i] = computeProgram.FindKernel(names[i]); }
                catch (UnityException)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "ComputeKernelMissing", "Texture ComputeShader is missing required kernel.", detail: names[i]);
                    kernels = null;
                    return false;
                }
            }
            diagnostic = null;
            return true;
        }

        private bool TryEnsureNormalKernels(out StackMachineDiagnostic diagnostic)
        {
            if (normalKernels != null) { diagnostic = null; return true; }
            if (normalComputeProgram == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "NormalComputeProgramRequired", "Normal vector operations require an explicitly assigned dedicated ComputeShader."); return false; }
            string[] names = { "KNormalBase", "KNormalDeltaAdd", "KNormalFinalize" };
            normalKernels = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                try { normalKernels[i] = normalComputeProgram.FindKernel(names[i]); }
                catch (UnityException)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "NormalComputeKernelMissing", "Normal ComputeShader is missing a required kernel.", detail: names[i]);
                    normalKernels = null;
                    return false;
                }
            }
            diagnostic = null;
            return true;
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
    }
}
