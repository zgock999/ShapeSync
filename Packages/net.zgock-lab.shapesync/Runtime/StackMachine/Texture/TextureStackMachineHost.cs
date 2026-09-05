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
    /// <remarks>
    /// <see cref="TextureStaticMachineFactory"/> resolves this component for Texture callers but never owns its Grid, hall allocation,
    /// request queue, delivery, or disposal lifecycle. A Host is scoped to its Unity scene and is neither persistent nor a global service.
    /// </remarks>
    public sealed class TextureStackMachineHost : MonoBehaviour
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
        private bool lifecycleEndingNotified;
        private ulong nextOrigin = 1;

        private void Awake() => TextureStaticMachineFactory.Invalidate(gameObject.scene);

        // A disabled host can no longer service an artifact set. Unity immediate destruction
        // enters this lifecycle phase before OnDestroy in EditMode.
        private void OnDisable() => NotifyLifecycleEnding();

        private sealed class QueuedRequest
        {
            public TextureDispatchPlan plan;
            public TextureBindingContext context;
            public TextureExecutionOriginKey origin;
            public TextureExecutionHandle handle;
            public TextureSourceLease sourceLease;
            public TextureOutputLease outputLease;
            public bool retainSourceLease;
            public bool retainOutputLease;
            public readonly Dictionary<string, TextureHallAllocation> sourceHalls = new Dictionary<string, TextureHallAllocation>(StringComparer.Ordinal);
            public readonly Dictionary<string, TextureHallAllocation> temporaryHalls = new Dictionary<string, TextureHallAllocation>(StringComparer.Ordinal);
            public TextureHallAllocation outputHall;
            public GraphicsFence fence;
            public TextureDelivery delivery;
            public bool stale;
            public bool cancelled;
        }

        /// <summary>Gets whether the host owns a validated fixed grid.</summary>
        public bool IsInitialized => initialized;
        /// <summary>Gets the validated capability snapshot after initialization.</summary>
        public TextureGpuCapability Capability => capability;
        /// <summary>Gets the host-owned random-write grid.</summary>
        public RenderTexture Grid => grid;
        /// <summary>Gets the explicitly assigned Compute program.</summary>
        public ComputeShader ComputeProgram => computeProgram;
        /// <summary>Gets the optional dedicated Compute program used only by Normal vector operations.</summary>
        public ComputeShader NormalComputeProgram => normalComputeProgram;

        /// <summary>Assigns the explicitly selected Compute program before initialization.</summary>
        /// <param name="value">Compute program that exposes the fixed Texture StackMachine kernel set.</param>
        /// <param name="diagnostic">Assignment diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the program was assigned before initialization.</returns>
        public bool TryAssignComputeProgram(ComputeShader value, out StackMachineDiagnostic diagnostic)
        {
            if (initialized) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostAlreadyInitialized", "Compute program cannot change after host initialization."); return false; }
            if (value == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "ComputeProgramRequired", "TextureStackMachineHost requires a ComputeShader."); return false; }
            computeProgram = value;
            diagnostic = null;
            return true;
        }

        /// <summary>Assigns the dedicated Normal-vector Compute program.</summary>
        /// <param name="value">Program exposing <c>KNormalBase</c>, <c>KNormalDeltaAdd</c>, and <c>KNormalFinalize</c>.</param>
        /// <param name="diagnostic">Assignment diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the program is accepted.</returns>
        public bool TryAssignNormalComputeProgram(ComputeShader value, out StackMachineDiagnostic diagnostic)
        {
            if (value == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "NormalComputeProgramRequired", "Normal vector operations require a dedicated ComputeShader."); return false; }
            normalComputeProgram = value;
            normalKernels = null;
            return TryEnsureNormalKernels(out diagnostic);
        }

        /// <summary>Gets the number of requests awaiting the single GPU submit slot.</summary>
        public int PendingRequestCount => pending.Count;
        /// <summary>Gets whether GPU work is currently submitted and awaiting a fence.</summary>
        public bool HasSubmittedRequest => submitted != null;
        /// <summary>Gets the number of caller-owned retained source leases that still occupy grid halls.</summary>
        public int OutstandingSourceLeaseCount => outstandingSourceLeases.Count;
        /// <summary>Gets the number of caller-owned retained output leases that still occupy grid halls.</summary>
        public int OutstandingOutputLeaseCount => outstandingOutputLeases.Count;
        /// <summary>Gets the number of host-tracked deliveries that have been handed to a caller and are excluded from transient admission budget.</summary>
        public int HandedOffDeliveryCount => handedOffDeliveries.Count;
        /// <summary>Gets GPU bytes currently charged to transient delivery admission, excluding caller-handed-off deliveries.</summary>
        public long LiveTransientGpuBytes => GetLiveTransientGpuBytes();
        /// <summary>Gets the number of source-to-grid ingest dispatches issued since host initialization.</summary>
        internal int IngestDispatchCount { get; private set; }

        /// <summary>Issues one opaque origin token owned by this scene-local host.</summary>
        /// <remarks>
        /// Callers retain the returned token for one logical request series and pass it again when a newer
        /// unsubmitted request is intended to replace the previous request. Tokens do not require explicit release;
        /// host teardown invalidates their owner identity. The sequence is deliberately host-local and is never exposed
        /// as a caller-construction API.
        /// </remarks>
        /// <returns>A new host-issued origin token.</returns>
        public TextureExecutionOriginKey CreateOrigin()
        {
            if (nextOrigin == 0) throw new InvalidOperationException("TextureStackMachineHost origin token space is exhausted.");
            return new TextureExecutionOriginKey(this, nextOrigin++);
        }

        /// <summary>Validates current live-grid capacity for one compiled request without reserving halls or enqueueing GPU work.</summary>
        /// <remarks>This is an admission check only. A later enqueue may still reject when another consumer occupies the grid first.</remarks>
        public bool TryValidateAdmission(TextureExecutionPlan plan, bool outputAlreadyReserved, out StackMachineDiagnostic diagnostic)
        {
            if (!initialized) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostNotInitialized", "TextureStackMachineHost is not initialized."); return false; }
            if (plan == null || plan.BindingContext == null || plan.DispatchPlan == null) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TexturePlanRequired", "Texture admission requires a compiled Texture execution plan."); return false; }
            var widths = new List<int>(); var heights = new List<int>();
            foreach (string sourceName in plan.DispatchPlan.ReadSourceNames)
            {
                if (!plan.BindingContext.TryGetBinding(sourceName, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture || binding.SourceTexture == null)
                { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceBindingRequired", "Compiled Texture plan refers to an unresolved source binding.", bindingName: sourceName); return false; }
                widths.Add(binding.SourceTexture.width); heights.Add(binding.SourceTexture.height);
            }
            if (!outputAlreadyReserved) { widths.Add(plan.DispatchPlan.OutputWidth); heights.Add(plan.DispatchPlan.OutputHeight); }
            if (allocator != null && allocator.TryCanReserveSequence(widths, heights)) { diagnostic = null; return true; }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "AtlasLiveAdmissionRejected", "The live Texture StackMachine grid cannot admit the Atlas recipe without evicting existing halls.", detail: "sources=" + plan.DispatchPlan.ReadSourceNames.Count + ";output=" + plan.DispatchPlan.OutputWidth + "x" + plan.DispatchPlan.OutputHeight + ";outputRetained=" + outputAlreadyReserved + ";retainedSourceLeases=" + outstandingSourceLeases.Count + ";retainedOutputLeases=" + outstandingOutputLeases.Count);
            return false;
        }

        /// <summary>Probes the device and creates the fixed random-write grid exactly once.</summary>
        /// <param name="diagnostic">Capability or allocation diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the fixed grid is ready for queued work.</returns>
        public bool TryInitialize(out StackMachineDiagnostic diagnostic)
        {
            if (initialized) { diagnostic = null; return true; }
            if (computeProgram == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "ComputeProgramRequired", "TextureStackMachineHost requires an explicitly assigned ComputeShader.");
                return false;
            }
            if (!TryCacheKernels(out diagnostic)) return false;
            if (!TextureGpuCapabilityProbe.TryProbe(out capability, out diagnostic)) return false;
            var descriptor = new RenderTextureDescriptor(capability.FixedGridEdge, capability.FixedGridEdge, GraphicsFormat.R16G16B16A16_SFloat, 0)
            {
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };
            grid = new RenderTexture(descriptor) { name = "ShapeSync.TextureStackMachine.Grid" };
            if (!grid.Create())
            {
                Destroy(grid);
                grid = null;
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GridCreateFailed", "Failed to create the fixed Texture StackMachine grid.");
                return false;
            }
            allocator = new TextureHallAllocator(capability.FixedGridEdge);
            initialized = true;
            diagnostic = null;
            return true;
        }

        /// <summary>Reserves a rectangular hall from the fixed grid after successful initialization.</summary>
        /// <param name="width">Requested supported width in texels.</param>
        /// <param name="height">Requested supported height in texels.</param>
        /// <param name="allocation">Host-owned hall allocation on success; otherwise the default value.</param>
        /// <param name="diagnostic">Reservation diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when a compatible hall was reserved; otherwise <see langword="false"/> and <paramref name="diagnostic"/> explains the rejection.</returns>
        /// <remarks>The returned allocation is owned by this host and must be released through <see cref="TryReleaseHall"/>.</remarks>
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

        /// <summary>Releases a grid hall reservation owned by this host.</summary>
        /// <param name="allocation">Allocation previously returned by <see cref="TryReserveHall"/>.</param>
        /// <returns><see langword="true"/> when the reservation belonged to this initialized host and was released.</returns>
        public bool TryReleaseHall(TextureHallAllocation allocation)
        {
            return initialized && allocator.TryRelease(allocation);
        }

        /// <summary>Queues one immutable plan. A newer unsubmitted request with the same nonzero origin replaces the older request.</summary>
        internal bool TryEnqueue(TextureDispatchPlan plan, TextureBindingContext context, TextureExecutionOriginKey origin, TextureExecutionHandle handle, TextureExecutionOptions options, out StackMachineDiagnostic diagnostic)
        {
            if (!initialized)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostNotInitialized", "TextureStackMachineHost is not initialized.");
                return false;
            }
            if (plan == null || context == null || handle == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "QueueInputRequired", "Texture plan, binding context, and execution handle are required.");
                return false;
            }
            if (origin.IsValid && !origin.BelongsTo(this))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OriginHostMismatch", "Texture execution received an origin token issued by another TextureStackMachineHost.");
                return false;
            }
            if (UsesNormalOperations(plan) && !TryEnsureNormalKernels(out diagnostic)) return false;

            TextureSourceLease sourceLease = options?.SourceLease;
            if (sourceLease != null && !sourceLease.BelongsTo(this))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseHostMismatch", "Texture execution received a retained source lease from another host.");
                return false;
            }
            if (sourceLease != null && !sourceLease.Matches(plan, context))
            {
                sourceLease.Dispose();
                sourceLease = null;
            }
            if (sourceLease != null && !sourceLease.TryAcquire())
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "Texture execution received a released retained source lease.");
                return false;
            }
            TextureOutputLease outputLease = options?.OutputLease;
            if (outputLease != null && !outputLease.BelongsTo(this))
            {
                sourceLease?.ReleaseUse();
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseHostMismatch", "Texture execution received a retained output lease from another host.");
                return false;
            }
            if (outputLease != null && !outputLease.MatchesExtent(plan.OutputWidth, plan.OutputHeight))
            {
                sourceLease?.ReleaseUse();
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseExtentMismatch", "Texture execution output extent does not match the retained output lease.");
                return false;
            }
            if (outputLease != null && !outputLease.TryAcquire(plan.OutputWidth, plan.OutputHeight))
            {
                sourceLease?.ReleaseUse();
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "Texture execution received a released or incompatible retained output lease.");
                return false;
            }

            if (origin.IsValid && pendingByOrigin.TryGetValue(origin, out QueuedRequest existing))
            {
                existing.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "RequestCoalesced", "The unsubmitted request was replaced by a newer request with the same origin."));
                Release(existing);
                RemovePending(existing);
            }
            if (origin.IsValid && submitted != null && submitted.origin == origin) submitted.stale = true;

            if (!TryValidateDeliveryReservation(plan.OutputWidth, plan.OutputHeight, out diagnostic))
            {
                sourceLease?.ReleaseUse();
                outputLease?.ReleaseUse();
                return false;
            }

            var request = new QueuedRequest { plan = plan, context = context, origin = origin, handle = handle, sourceLease = sourceLease, outputLease = outputLease, retainSourceLease = options?.RetainSourceLease == true, retainOutputLease = options?.RetainOutputLease == true };
            if (!TryReserveRequestHalls(request, out diagnostic))
            {
                Release(request);
                return false;
            }
            pending.Enqueue(request);
            if (origin.IsValid) pendingByOrigin.Add(origin, request);
            diagnostic = null;
            return true;
        }

        internal void Cancel(TextureExecutionHandle handle)
        {
            if (handle == null || handle.IsCompleted) return;
            QueuedRequest cancelled = null;
            foreach (QueuedRequest request in pending) if (ReferenceEquals(request.handle, handle)) { cancelled = request; break; }
            if (cancelled != null)
            {
                RemovePending(cancelled);
                cancelled.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "The unsubmitted Texture request was cancelled by its consumer."));
                Release(cancelled);
                return;
            }
            if (submitted == null || !ReferenceEquals(submitted.handle, handle)) return;
            submitted.cancelled = true;
            submitted.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "Submitted Texture GPU work was retained only until its fence passed after consumer cancellation."));
        }

        private void Update()
        {
            if (!initialized) return;
            if (submitted != null)
            {
                if (!submitted.fence.passed) return;
                if (submitted.cancelled)
                {
                    submitted.delivery?.Dispose();
                    Release(submitted);
                    submitted = null;
                    return;
                }
                if (submitted.stale)
                {
                    submitted.delivery?.Dispose();
                    submitted.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "RequestStale", "A newer request with the same origin superseded submitted GPU work."));
                }
                else
                {
                    TextureSourceLease retainedSourceLease = submitted.retainSourceLease ? TakeSourceLease(submitted) : null;
                    TextureOutputLease retainedOutputLease = submitted.retainOutputLease && submitted.outputLease == null ? TakeOutputLease(submitted) : null;
                    submitted.handle.CompleteGpuFence(submitted.delivery, retainedSourceLease, retainedOutputLease);
                }
                Release(submitted);
                submitted = null;
            }
            if (pending.Count == 0) return;
            QueuedRequest request = pending.Dequeue();
            if (request.origin.IsValid) pendingByOrigin.Remove(request.origin);
            if (!TrySubmit(request, out StackMachineDiagnostic diagnostic))
            {
                request.handle.CompleteFailure(diagnostic);
                request.delivery?.Dispose();
                Release(request);
                return;
            }
            submitted = request;
        }

        private bool TryReserveRequestHalls(QueuedRequest request, out StackMachineDiagnostic diagnostic)
        {
            foreach (string sourceName in request.plan.ReadSourceNames)
            {
                if (!request.context.TryGetBinding(sourceName, out TextureBinding source) || source.Kind != TextureBindingKind.SourceTexture) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceBindingRequired", "Compiled Texture plan refers to an unresolved source binding.", bindingName: sourceName); return false; }
                if (!TextureGpuCapabilityProbe.IsPhase0Edge(source.SourceTexture.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(source.SourceTexture.height)) { diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceExtentUnsupported", "Texture input width and height must each be supported power-of-two extents.", bindingName: sourceName); return false; }
            }
            if (request.sourceLease != null)
            {
                foreach (string sourceName in request.plan.ReadSourceNames)
                {
                    request.context.TryGetBinding(sourceName, out TextureBinding source);
                    if (!request.sourceLease.TryResolve(sourceName, source.SourceTexture, out TextureHallAllocation leasedHall))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "Texture execution retained source lease became invalid before submission.", bindingName: sourceName);
                        return false;
                    }
                    request.sourceHalls.Add(sourceName, leasedHall);
                }
                return TryReserveOutputHall(request, out diagnostic);
            }
            foreach (string sourceName in request.plan.ReadSourceNames)
            {
                if (!request.context.TryGetBinding(sourceName, out TextureBinding source) || source.Kind != TextureBindingKind.SourceTexture)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceBindingRequired", "Compiled Texture plan refers to an unresolved source binding.", bindingName: sourceName);
                    return false;
                }
                if (!TryReserveHall(source.SourceTexture.width, source.SourceTexture.height, out TextureHallAllocation hall, out diagnostic)) return false;
                request.sourceHalls.Add(sourceName, hall);
            }
            return TryReserveOutputHall(request, out diagnostic);
        }

        private bool TrySubmit(QueuedRequest request, out StackMachineDiagnostic diagnostic)
        {
            if (!request.retainOutputLease && !TryCreateDelivery(request.plan.OutputWidth, request.plan.OutputHeight, out request.delivery, out diagnostic)) return false;
            var command = new CommandBuffer { name = "ShapeSync.TextureStackMachine" };
            try
            {
                if (request.sourceLease == null) foreach (KeyValuePair<string, TextureHallAllocation> pair in request.sourceHalls)
                {
                    request.context.TryGetBinding(pair.Key, out TextureBinding source);
                    SetCommon(command, computeProgram, kernels[11], pair.Value.Width, pair.Value.Height, pair.Value, default, default, default);
                    command.SetComputeTextureParam(computeProgram, kernels[11], "_SourceA", source.SourceTexture);
                    command.SetComputeTextureParam(computeProgram, kernels[11], "_Destination", grid);
                    command.DispatchCompute(computeProgram, kernels[11], (pair.Value.Width + 7) / 8, (pair.Value.Height + 7) / 8, 1);
                    IngestDispatchCount++;
                }
                Dictionary<string, int> temporaryUses = CountTemporaryUses(request.plan);
                for (int i = 0; i < request.plan.Records.Count; i++)
                {
                    TextureDispatchRecord record = request.plan.Records[i];
                    if (!TryRecordKernel(record.Operation, out ComputeShader program, out int kernel))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DispatchOperationUnsupported", "Compiled Texture dispatch operation has no Compute kernel.", instructionPointer: i);
                        return false;
                    }
                    if (!TryResolveDestination(request, record.Output, out TextureHallAllocation destination, out diagnostic)) return false;
                    if (!TryResolveSource(request, record, 0, out TextureHallAllocation sourceA, out diagnostic) || !TryResolveSource(request, record, 1, out TextureHallAllocation sourceB, out diagnostic) || !TryResolveSource(request, record, 2, out TextureHallAllocation sourceC, out diagnostic)) return false;
                    if ((sourceA.IsValid && sourceA.Id == destination.Id) || (sourceB.IsValid && sourceB.Id == destination.Id) || (sourceC.IsValid && sourceC.Id == destination.Id))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "InPlaceReadWrite", "Texture operations may not read from and write to the same hall.", instructionPointer: i);
                        return false;
                    }
                    SetRecordCommon(command, program, kernel, record, destination, sourceA, sourceB, sourceC);
                    command.SetComputeTextureParam(program, kernel, "_SourceA", grid);
                    command.SetComputeTextureParam(program, kernel, "_SourceB", grid);
                    command.SetComputeTextureParam(program, kernel, "_SourceC", grid);
                    command.SetComputeTextureParam(program, kernel, "_Destination", grid);
                    if (record.Operation == TextureDispatchOperation.Fill) command.SetComputeVectorParam(program, "_FillColor", new Vector4(record.Scalars[0], record.Scalars[1], record.Scalars[2], record.Scalars[3]));
                    if (record.Operation == TextureDispatchOperation.NormalWeightedBlend || record.Operation == TextureDispatchOperation.NormalDeltaAdd) command.SetComputeFloatParam(program, "_Weight", record.Scalars[0]);
                    if (record.Operation == TextureDispatchOperation.Colorize) command.SetComputeVectorParam(program, "_Colorize", new Vector4(record.Scalars[0], record.Scalars[1], record.Scalars[2], 0f));
                    command.DispatchCompute(program, kernel, (record.RecordExtent.Width + 7) / 8, (record.RecordExtent.Height + 7) / 8, 1);
                    ReleaseConsumedTemporarySources(request, record, temporaryUses);
                }
                if (!request.retainOutputLease)
                {
                    SetCommon(command, computeProgram, kernels[12], request.plan.OutputWidth, request.plan.OutputHeight, default, request.outputHall, default, default);
                    command.SetComputeTextureParam(computeProgram, kernels[12], "_SourceA", grid);
                    command.SetComputeTextureParam(computeProgram, kernels[12], "_Destination", (RenderTexture)request.delivery.Texture);
                    command.DispatchCompute(computeProgram, kernels[12], (request.plan.OutputWidth + 7) / 8, (request.plan.OutputHeight + 7) / 8, 1);
                }
                request.fence = command.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
                Graphics.ExecuteCommandBuffer(command);
                diagnostic = null;
                return true;
            }
            catch
            {
                request.delivery?.Dispose();
                request.delivery = null;
                throw;
            }
            finally { command.Release(); }
        }

        private long GetLiveTransientGpuBytes()
        {
            long bytes = 0;
            foreach (TextureDelivery delivery in outstandingDeliveries) bytes += GetTextureBytes(delivery.Texture);
            return bytes;
        }

        private bool TryValidateDeliveryReservation(int width, int height, out StackMachineDiagnostic diagnostic)
        {
            long candidateBytes = (long)width * height * TextureGpuCapabilityProbe.BytesPerPixel;
            long pendingBytes = 0;
            foreach (QueuedRequest request in pending) pendingBytes += (long)request.plan.OutputWidth * request.plan.OutputHeight * TextureGpuCapabilityProbe.BytesPerPixel;
            long gridBytes = (long)capability.FixedGridEdge * capability.FixedGridEdge * TextureGpuCapabilityProbe.BytesPerPixel;
            return TryValidateDeliveryReservation(gridBytes, GetLiveTransientGpuBytes(), pendingBytes, candidateBytes, capability.GpuBudgetBytes, out diagnostic);
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

        private static long GetTextureBytes(Texture texture)
        {
            return texture == null ? 0 : (long)texture.width * texture.height * TextureGpuCapabilityProbe.BytesPerPixel;
        }

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
                foreach (TextureDelivery candidate in handedOffDeliveries)
                    if (candidate.Texture == null || candidate.Texture == texture) { released = candidate; break; }
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

        private bool TryResolveDestination(QueuedRequest request, string logicalName, out TextureHallAllocation hall, out StackMachineDiagnostic diagnostic)
        {
            if (logicalName == request.context.OutputLogicalName) { hall = request.outputHall; diagnostic = null; return true; }
            if (request.temporaryHalls.TryGetValue(logicalName, out hall)) { diagnostic = null; return true; }
            if (!TryReserveHall(request.plan.OutputWidth, request.plan.OutputHeight, out hall, out diagnostic)) return false;
            request.temporaryHalls.Add(logicalName, hall);
            return true;
        }

        private static Dictionary<string, int> CountTemporaryUses(TextureDispatchPlan plan)
        {
            var uses = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Records.Count; i++)
            {
                IReadOnlyList<string> sources = plan.Records[i].Sources;
                for (int j = 0; j < sources.Count; j++) if (TexturePlanCompiler.IsTemporary(sources[j])) uses[sources[j]] = uses.TryGetValue(sources[j], out int count) ? count + 1 : 1;
            }
            return uses;
        }

        private void ReleaseConsumedTemporarySources(QueuedRequest request, TextureDispatchRecord record, Dictionary<string, int> uses)
        {
            for (int i = 0; i < record.Sources.Count; i++)
            {
                string source = record.Sources[i];
                if (!TexturePlanCompiler.IsTemporary(source) || !uses.TryGetValue(source, out int remaining)) continue;
                remaining--;
                if (remaining > 0) { uses[source] = remaining; continue; }
                uses.Remove(source);
                if (request.temporaryHalls.TryGetValue(source, out TextureHallAllocation hall)) { TryReleaseHall(hall); request.temporaryHalls.Remove(source); }
            }
        }

        private static bool TryResolveSource(QueuedRequest request, TextureDispatchRecord record, int index, out TextureHallAllocation hall, out StackMachineDiagnostic diagnostic)
        {
            hall = default;
            diagnostic = null;
            if (index >= record.Sources.Count) return true;
            string logicalName = record.Sources[index];
            if (request.sourceHalls.TryGetValue(logicalName, out hall)) return true;
            if (request.temporaryHalls.TryGetValue(logicalName, out hall)) return true;
            if (request.context.TryGetBinding(logicalName, out TextureBinding binding) && binding.Kind == TextureBindingKind.OutputHall)
            {
                hall = request.outputHall;
                if (hall.IsValid) return true;
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputReadBeforeWrite", "Texture output cannot be read before an earlier operation writes it.", bindingName: logicalName);
                return false;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DispatchSourceUnresolved", "Compiled Texture record refers to an unresolved hall.", bindingName: logicalName);
            return false;
        }

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
            kernel = index >= 0 ? kernels[index] : -1;
            return index >= 0;
        }

        private void RemovePending(QueuedRequest request)
        {
            var retained = new Queue<QueuedRequest>();
            while (pending.Count > 0)
            {
                QueuedRequest item = pending.Dequeue();
                if (!ReferenceEquals(item, request)) retained.Enqueue(item);
            }
            while (retained.Count > 0) pending.Enqueue(retained.Dequeue());
            if (request.origin.IsValid) pendingByOrigin.Remove(request.origin);
        }

        private void Release(QueuedRequest request)
        {
            if (request.sourceLease == null) foreach (TextureHallAllocation hall in request.sourceHalls.Values) TryReleaseHall(hall);
            request.sourceHalls.Clear();
            request.sourceLease?.ReleaseUse();
            request.sourceLease = null;
            bool ownsOutputHall = request.outputLease == null;
            request.outputLease?.ReleaseUse();
            request.outputLease = null;
            if (ownsOutputHall && request.outputHall.IsValid) TryReleaseHall(request.outputHall);
            request.outputHall = default;
            foreach (TextureHallAllocation hall in request.temporaryHalls.Values) TryReleaseHall(hall);
            request.temporaryHalls.Clear();
        }

        private TextureSourceLease TakeSourceLease(QueuedRequest request)
        {
            var bindings = new Dictionary<string, TextureSourceLease.Binding>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, TextureHallAllocation> pair in request.sourceHalls)
            {
                request.context.TryGetBinding(pair.Key, out TextureBinding binding);
                bindings.Add(pair.Key, new TextureSourceLease.Binding(binding.SourceTexture, pair.Value));
            }
            request.sourceHalls.Clear();
            var lease = new TextureSourceLease(this, bindings);
            outstandingSourceLeases.Add(lease);
            return lease;
        }

        private bool TryReserveOutputHall(QueuedRequest request, out StackMachineDiagnostic diagnostic)
        {
            if (request.outputLease != null)
            {
                request.outputHall = request.outputLease.Hall;
                diagnostic = null;
                return true;
            }
            return TryReserveHall(request.plan.OutputWidth, request.plan.OutputHeight, out request.outputHall, out diagnostic);
        }

        private TextureOutputLease TakeOutputLease(QueuedRequest request)
        {
            TextureHallAllocation hall = request.outputHall;
            request.outputHall = default;
            var lease = new TextureOutputLease(this, hall);
            outstandingOutputLeases.Add(lease);
            return lease;
        }

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

        private void OnDestroy()
        {
            NotifyLifecycleEnding();
            TextureStaticMachineFactory.Invalidate(gameObject.scene);
            while (pending.Count > 0)
            {
                QueuedRequest request = pending.Dequeue();
                request.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "HostDestroyed", "TextureStackMachineHost was destroyed before submission."));
                Release(request);
            }
            if (submitted != null)
            {
                submitted.handle.CompleteFailure(StackMachineDiagnostic.CreateDomain("texture", "HostDestroyed", "TextureStackMachineHost was destroyed before GPU completion."));
                submitted.delivery?.Dispose();
                Release(submitted);
                submitted = null;
            }
            var deliveries = new List<TextureDelivery>(outstandingDeliveries);
            deliveries.AddRange(handedOffDeliveries);
            foreach (TextureDelivery delivery in deliveries) delivery.Dispose();
            outstandingDeliveries.Clear();
            handedOffDeliveries.Clear();
            if (outstandingSourceLeases.Count != 0 || outstandingOutputLeases.Count != 0)
            {
                StackMachineDiagnostic leak = StackMachineDiagnostic.CreateDomain("texture", "LeaseForcedReleaseOnHostDestroy", "TextureStackMachineHost destroyed caller-owned retained halls.", detail: "sourceLeases=" + outstandingSourceLeases.Count + "; outputLeases=" + outstandingOutputLeases.Count);
                Debug.LogWarning(leak.domainCode + ": " + leak.message + " detail=" + leak.detail, this);
            }
            var sourceLeases = new List<TextureSourceLease>(outstandingSourceLeases);
            foreach (TextureSourceLease sourceLease in sourceLeases) ReleaseSourceLease(sourceLease);
            outstandingSourceLeases.Clear();
            var outputLeases = new List<TextureOutputLease>(outstandingOutputLeases);
            foreach (TextureOutputLease outputLease in outputLeases) ReleaseOutputLease(outputLease);
            outstandingOutputLeases.Clear();
            pendingByOrigin.Clear();
            if (grid == null) return;
            grid.Release();
            Destroy(grid);
            grid = null;
            allocator = null;
            initialized = false;
        }

        private void NotifyLifecycleEnding()
        {
            if (lifecycleEndingNotified) return;
            lifecycleEndingNotified = true;
            Destroying?.Invoke();
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
