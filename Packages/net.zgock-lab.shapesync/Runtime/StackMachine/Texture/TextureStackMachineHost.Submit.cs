// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// The GPU command recording section for prepared requests (Spec15-2 §7.5, §11). Every hall and use was
    /// already reserved by <see cref="TryAcquirePreparedResources"/>; the ingest and record loops resolve only
    /// HallPlan index references and perform no allocator operation, no name parsing, and no reservation retry.
    /// The finally releases the CommandBuffer only. An unsubmitted false keeps the request's delivery, halls,
    /// and uses intact for the caller's single §10 cleanup; an exception after the Execute attempt transfers
    /// ownership to the §13.4 teardown as a fault.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Resolves one HallPlan reference against the request's pre-reserved resources: Source from the
        /// sourceHalls lookup keyed by <c>Sources[index].Name</c>, Output from outputHall, and a temporary from
        /// <c>temporaryPool[index]</c>. Pure lookup; it never touches the allocator and never re-resolves names.
        /// </summary>
        /// <param name="r">The prepared request whose lookups hold the reserved halls.</param>
        /// <param name="reference">A reference already resolved by the request's HallPlan.</param>
        /// <returns>The allocation the reference resolves to.</returns>
        private TextureHallAllocation ResolvePreparedReference(QueuedRequest r, TextureRequestHallPlan.ResolvedReference reference)
        {
            switch (reference.Kind)
            {
                case TextureRequestHallPlan.ReferenceKind.Source:
                    return r.sourceHalls[r.hallPlan.Sources[reference.Index].Name];
                case TextureRequestHallPlan.ReferenceKind.Output:
                    return r.outputHall;
                default: // TemporarySlot
                    return r.temporaryPool[reference.Index];
            }
        }

        /// <summary>
        /// Records and submits one fully prepared request. The delivery is created before Execute; registering
        /// it ends the request's delivery reservation, and a retained Output zeroes the reservation on
        /// successful submission. Only recorded ingests count toward <see cref="IngestDispatchCount"/>, and
        /// only after success. The finally releases the CommandBuffer alone; an unsubmitted failure returns
        /// false with the request's resources kept for the caller's single cleanup, while an exception after
        /// the Execute attempt enters the §13.4 fault teardown (a created fence that cannot be polled is
        /// decided there).
        /// </summary>
        /// <param name="r">The prepared request with all halls, uses, and HallPlan references resolved.</param>
        /// <param name="diagnostic">Failure diagnostic on false; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the command buffer was submitted; otherwise <see langword="false"/>.</returns>
        private bool TrySubmitPrepared(QueuedRequest r, out StackMachineDiagnostic diagnostic)
        {
            CommandBuffer command = null;
            bool submissionAttempted = false;
            int ingestCount = 0;
            try
            {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                if (testFailurePoint == TestFailurePoint.BeforeDelivery)
                {
                    testFailurePoint = TestFailurePoint.None;
                    throw new InvalidOperationException("Spec15-2 §13.5 test seam: BeforeDelivery.");
                }
#endif
                if (!r.retainOutputLease && !TryCreateDelivery(r.plan.OutputWidth, r.plan.OutputHeight, out r.delivery, out diagnostic)) return false;
                if (!r.retainOutputLease) r.reservedDeliveryBytes = 0;
                command = new CommandBuffer();
                command.name = "ShapeSync.TextureStackMachine";
                TextureRequestHallPlan.SourceEntry[] planSources = r.hallPlan.Sources;
                for (int i = 0; i < planSources.Length; i++)
                {
                    if (IsSourceCovered(r.requestedSourceLease, planSources[i])) continue;
                    TextureHallAllocation hall = r.sourceHalls[planSources[i].Name];
                    SetCommon(command, computeProgram, kernels[11], hall.Width, hall.Height, hall, default, default, default);
                    command.SetComputeTextureParam(computeProgram, kernels[11], "_SourceA", planSources[i].Texture);
                    command.SetComputeTextureParam(computeProgram, kernels[11], "_Destination", grid);
                    command.DispatchCompute(computeProgram, kernels[11], (hall.Width + 7) / 8, (hall.Height + 7) / 8, 1);
                    ingestCount++;
                }

                for (int i = 0; i < r.plan.Records.Count; i++)
                {
                    TextureDispatchRecord record = r.plan.Records[i];
                    if (!TryRecordKernel(record.Operation, out ComputeShader program, out int kernel))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DispatchOperationUnsupported", "Compiled Texture dispatch operation has no Compute kernel.", instructionPointer: i);
                        return false;
                    }
                    TextureHallAllocation destination = ResolvePreparedReference(r, r.hallPlan.RecordDestinations[i]);
                    TextureHallAllocation sourceA = record.Sources.Count > 0 ? ResolvePreparedReference(r, r.hallPlan.RecordSources[i][0]) : default;
                    TextureHallAllocation sourceB = record.Sources.Count > 1 ? ResolvePreparedReference(r, r.hallPlan.RecordSources[i][1]) : default;
                    TextureHallAllocation sourceC = record.Sources.Count > 2 ? ResolvePreparedReference(r, r.hallPlan.RecordSources[i][2]) : default;
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
                }
                if (!r.retainOutputLease)
                {
                    SetCommon(command, computeProgram, kernels[12], r.plan.OutputWidth, r.plan.OutputHeight, default, r.outputHall, default, default);
                    command.SetComputeTextureParam(computeProgram, kernels[12], "_SourceA", grid);
                    command.SetComputeTextureParam(computeProgram, kernels[12], "_Destination", (RenderTexture)r.delivery.Texture);
                    command.DispatchCompute(computeProgram, kernels[12], (r.plan.OutputWidth + 7) / 8, (r.plan.OutputHeight + 7) / 8, 1);
                }
                r.fence = command.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                if (testFailurePoint == TestFailurePoint.BeforeExecute)
                {
                    testFailurePoint = TestFailurePoint.None;
                    throw new InvalidOperationException("Spec15-2 §13.5 test seam: BeforeExecute.");
                }
#endif
                submissionAttempted = true;
                Graphics.ExecuteCommandBuffer(command);
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                // §13.5 test contact: the AfterExecute seam fires inside the post-submission section.
                if (testFailurePoint == TestFailurePoint.AfterExecute)
                {
                    testFailurePoint = TestFailurePoint.None;
                    throw new InvalidOperationException("Spec15-2 §13.5 test seam: AfterExecute.");
                }
#endif
                IngestDispatchCount += ingestCount;
                if (r.retainOutputLease) r.reservedDeliveryBytes = 0;
                diagnostic = null;
                return true;
            }
            catch
            {
                if (submissionAttempted)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuSubmissionFailed", "Texture GPU submission was attempted and failed; the host entered the fault state and hands GPU-referenced resources to retirement.");
                    TeardownPrepared(r, diagnostic, true, false);
                    return false;
                }
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuPreparationFailed", "Texture GPU preparation failed before submission; the request keeps its delivery, halls, and uses for the caller's single cleanup.");
                return false;
            }
            finally { command?.Release(); }
        }
    }
}
