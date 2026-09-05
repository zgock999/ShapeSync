// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Lifecycle state shared by every Humanoid Mesh build backend.</summary>
    public enum HumanoidMeshBuildStatus { Idle, Pending, Succeeded, Failed, Cancelled }

    /// <summary>Core-owned result escrow plus backend-owned Normal completions.</summary>
    /// <typeparam name="TNormal">Backend-specific in-memory Normal completion type.</typeparam>
    public sealed class HumanoidMeshBuildEscrow<TNormal> : IDisposable
    {
        private HumanoidMeshFbmBakeResult mesh;
        private TNormal[] normals;
        private readonly Action<TNormal> disposeNormal;
        internal HumanoidMeshBuildEscrow(HumanoidMeshFbmBakeResult mesh, TNormal[] normals, Action<TNormal> disposeNormal) { this.mesh = mesh; this.normals = normals ?? Array.Empty<TNormal>(); this.disposeNormal = disposeNormal; }
        /// <summary>Gets the owned final Mesh build result until it is detached or disposed.</summary>
        public HumanoidMeshFbmBakeResult Mesh => mesh;
        /// <summary>Gets the owned Normal completions without transferring their ownership.</summary>
        public IReadOnlyList<TNormal> Normals => Array.AsReadOnly(normals);
        /// <inheritdoc />
        public void Dispose() { for (int i = 0; i < normals.Length; i++) disposeNormal?.Invoke(normals[i]); normals = Array.Empty<TNormal>(); mesh?.Dispose(); mesh = null; }
        /// <summary>Transfers the final Mesh result without disposing it.</summary>
        public HumanoidMeshFbmBakeResult DetachMesh() { var value = mesh; mesh = null; return value; }
        /// <summary>Transfers the Normal completion array without disposing its entries.</summary>
        public TNormal[] DetachNormals() { var value = normals; normals = Array.Empty<TNormal>(); return value; }
    }

    /// <summary>Owns the backend-independent Humanoid Mesh phase order. Derived backends provide only Normal TSM execution.</summary>
    /// <typeparam name="TNormal">Backend-specific completion returned for one Normal Texture StackMachine execution.</typeparam>
    public abstract class HumanoidMeshBuildMachine<TNormal> : IDisposable
    {
        private readonly bool publishResolvedHumanoidRestPose;
        private HumanoidMeshLogicalPlan plan;
        private HumanoidMeshFbmBakeResult mesh;
        private readonly List<TNormal> normals = new List<TNormal>();
        private int nextNormal;
        private HumanoidMeshNormalSource activeNormal;
        private bool disposed;

        protected HumanoidMeshBuildMachine(bool publishResolvedHumanoidRestPose = false)
        {
            this.publishResolvedHumanoidRestPose = publishResolvedHumanoidRestPose;
        }

        /// <summary>Gets the current machine lifecycle state.</summary>
        public HumanoidMeshBuildStatus Status { get; private set; }
        /// <summary>Gets the terminal failure diagnostic, or null while no failure is recorded.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }
        /// <summary>Gets the number of Normal completions currently owned by this core machine.</summary>
        protected int NormalCompletionCount => normals.Count;

        /// <summary>Collects and validates the Mesh plan, then starts caller-driven execution.</summary>
        /// <param name="figureRoot">Read-only Figure root used to resolve Mesh bindings.</param>
        /// <param name="document">Detached ShapeSync document that supplies the recipe.</param>
        /// <param name="diagnostic">A structured diagnostic when the operation cannot start.</param>
        /// <returns>True when the machine entered <see cref="HumanoidMeshBuildStatus.Pending"/>.</returns>
        public bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("HumanoidMeshMachineDisposed", "Humanoid Mesh build machine has been disposed.", out diagnostic);
            if (Status == HumanoidMeshBuildStatus.Pending || plan != null || mesh != null) return Reject("HumanoidMeshMachineBusy", "Take or cancel the prior Humanoid Mesh build before starting another execution.", out diagnostic);
            DisposeNormals();
            activeNormal = null;
            nextNormal = 0;
            if (!HumanoidMeshLogicalCollector.TryCreate(figureRoot, document, out plan, out diagnostic)) { Fail(diagnostic); return false; }
            Status = HumanoidMeshBuildStatus.Pending; Diagnostic = null; return true;
        }

        /// <summary>Advances at most the active Mesh / Normal work and never schedules itself.</summary>
        /// <param name="diagnostic">The current failure diagnostic when the returned state is failed.</param>
        /// <returns>The resulting lifecycle state after this explicit pump.</returns>
        public HumanoidMeshBuildStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (Status != HumanoidMeshBuildStatus.Pending) { diagnostic = Diagnostic; return Status; }
            if (mesh == null && !TryBuildGeometry(out diagnostic)) return Fail(diagnostic);
            if (activeNormal != null) return PumpNormal(out diagnostic);
            if (!TryAdvanceNormal(out diagnostic)) return Fail(diagnostic);
            return Status;
        }

        /// <summary>Transfers the completed Mesh and Normal escrow after a successful build.</summary>
        /// <param name="result">The caller-owned escrow on success; otherwise null.</param>
        /// <returns>True only after <see cref="HumanoidMeshBuildStatus.Succeeded"/>.</returns>
        public bool TryTake(out HumanoidMeshBuildEscrow<TNormal> result)
        {
            result = null;
            if (Status != HumanoidMeshBuildStatus.Succeeded || mesh == null) return false;
            result = new HumanoidMeshBuildEscrow<TNormal>(mesh, normals.ToArray(), DisposeNormal);
            mesh = null; normals.Clear(); Status = HumanoidMeshBuildStatus.Idle; Diagnostic = null; return true;
        }

        /// <summary>Cancels active Normal work and disposes every unhanded Mesh / Normal resource owned by this machine.</summary>
        public void Cancel()
        {
            if (disposed) return;
            CancelNormal(); DisposeNormals(); mesh?.Dispose(); mesh = null; plan = null; activeNormal = null; nextNormal = 0; Diagnostic = null; Status = HumanoidMeshBuildStatus.Cancelled;
        }

        /// <inheritdoc />
        public virtual void Dispose() { if (disposed) return; Cancel(); disposed = true; }

        private bool TryBuildGeometry(out StackMachineDiagnostic diagnostic)
        {
            if (!HumanoidMeshFbmBaker.TryBake(plan, out mesh, out diagnostic)) return false;
            if (!HumanoidMeshBcpResolver.TryResolve(mesh, out var bcp, out diagnostic)) return false; mesh.SetBcpDeltas(bcp);
            if (!HumanoidMeshSkeletonBuilder.TryCreate(mesh, out var skeleton, out diagnostic)) return false; mesh.SetSkeleton(skeleton);
            if (!skeleton.TryAssignRebuiltAvatar(out diagnostic)) return false;
            if (!HumanoidMeshBoneTable.TryCreate(mesh, mesh.LogicalPlan.Figure, skeleton, out var table, out diagnostic)) return false; mesh.SetBoneTable(table);
            if (!TryMergeExtraBones(out diagnostic)) return false;
            if (!HumanoidMeshPcmBaker.TryBake(mesh, out diagnostic) || !HumanoidMeshVariantFinalizer.TryFinalize(mesh, out diagnostic)) return false;
            return true;
        }

        private bool TryMergeExtraBones(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null; var mapping = new Dictionary<Transform, Transform>(); var roots = new HashSet<string>(StringComparer.Ordinal); var table = mesh.BoneTable;
            foreach (var outfit in mesh.LogicalPlan.AttachedOutfits) { if (!HumanoidMeshExtraBoneMerger.TryMerge(outfit, mesh.Skeleton, table, roots, mesh.FbmWeights, out var merge, out diagnostic)) return false; table = merge.BoneTable; foreach (var root in merge.OwnedRootPaths) roots.Add(root); foreach (var pair in merge.FinalByOutfitTransform) { if (mapping.ContainsKey(pair.Key)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "ExtraBoneSourceConflict", "Multiple Outfit Extra Bone merges resolved the same source Transform.", detail: pair.Key.name); return false; } mapping.Add(pair.Key, pair.Value); } }
            mesh.SetBoneTable(table); mesh.SetExtraBoneTransforms(mapping); return true;
        }

        private bool TryAdvanceNormal(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (nextNormal >= plan.NormalSources.Count)
            {
                // The Pure Humanoid contract normalizes the candidate root.  Keep
                // final Mesh construction in the physical attachment pose; the
                // resolved rest pose is applied at the publish handoff below.
                mesh.Skeleton.ResetRootTransform();
                if (!HumanoidMeshFinalMeshBuilder.TryBuild(mesh, out diagnostic)
                    || !HumanoidMeshMaterialSlotBuilder.TryCreate(mesh, out HumanoidMeshMaterialSlot[] slots, out diagnostic)) return false;
                mesh.SetMaterialSlots(slots);
                if (!mesh.Skeleton.TryRestoreSampledAnimatorState(out diagnostic)) return false;
                // Rebind/Update is needed to preserve the Animator's controller
                // state, but the Editor publisher must hand off the resolved
                // Pure Humanoid rest pose instead of the sampled source pose.
                if (publishResolvedHumanoidRestPose) mesh.Skeleton.RestoreResolvedHumanoidPose();
                plan = null;
                Status = HumanoidMeshBuildStatus.Succeeded;
                return true;
            }
            activeNormal = plan.NormalSources[nextNormal];
            if (!HumanoidMeshNormalStubBuilder.TryCreate(mesh, activeNormal, out var stub, out diagnostic) || !TextureExecutionPlan.TryCreate(stub, out var texturePlan, out diagnostic)) return false;
            return TryStartNormal(texturePlan, out diagnostic);
        }

        private HumanoidMeshBuildStatus PumpNormal(out StackMachineDiagnostic diagnostic)
        {
            if (!TryPumpNormal(out bool pending, out diagnostic)) return Fail(diagnostic);
            if (pending) return Status;
            if (!TryTakeNormal(activeNormal, out TNormal completion, out diagnostic)) return Fail(diagnostic);
            normals.Add(completion); activeNormal = null; nextNormal++; if (!TryAdvanceNormal(out diagnostic)) return Fail(diagnostic); return Status;
        }

        private HumanoidMeshBuildStatus Fail(StackMachineDiagnostic diagnostic) { CancelNormal(); DisposeNormals(); mesh?.Dispose(); mesh = null; plan = null; activeNormal = null; Diagnostic = diagnostic; Status = HumanoidMeshBuildStatus.Failed; return Status; }
        /// <summary>Marks the machine failed after its Mesh escrow was handed to a derived backend for final conversion.</summary>
        /// <param name="diagnostic">The terminal structured diagnostic.</param>
        protected void FailAfterHandoff(StackMachineDiagnostic diagnostic) { Diagnostic = diagnostic; Status = HumanoidMeshBuildStatus.Failed; }
        private bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message); return false; }
        private void DisposeNormals() { for (int i = 0; i < normals.Count; i++) DisposeNormal(normals[i]); normals.Clear(); }
        /// <summary>Starts one lower-level Normal Texture execution for the supplied immutable plan.</summary>
        /// <param name="plan">The validated Normal Texture execution plan.</param>
        /// <param name="diagnostic">A structured diagnostic when dispatch fails.</param>
        /// <returns>True when the backend accepted ownership of the active Normal execution.</returns>
        protected abstract bool TryStartNormal(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic);
        /// <summary>Pumps the active lower-level Normal Texture execution once.</summary>
        /// <param name="pending">True when another explicit pump is required.</param>
        /// <param name="diagnostic">A structured diagnostic when the Normal execution fails.</param>
        /// <returns>True when the backend pump itself completed without failure.</returns>
        protected abstract bool TryPumpNormal(out bool pending, out StackMachineDiagnostic diagnostic);
        /// <summary>Takes the completed Normal result after the backend reports non-pending completion.</summary>
        /// <param name="source">The logical Normal source that requested the execution.</param>
        /// <param name="completion">The ownership-transferred completion on success.</param>
        /// <param name="diagnostic">A structured diagnostic when no valid completion is available.</param>
        /// <returns>True when ownership was transferred to this core machine.</returns>
        protected abstract bool TryTakeNormal(HumanoidMeshNormalSource source, out TNormal completion, out StackMachineDiagnostic diagnostic);
        /// <summary>Cancels the active lower-level Normal execution without rolling back higher-level artifacts.</summary>
        protected abstract void CancelNormal();
        /// <summary>Disposes one completion that remains owned by this core machine.</summary>
        /// <param name="completion">The backend completion to release.</param>
        protected abstract void DisposeNormal(TNormal completion);
    }
}
