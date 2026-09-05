// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    internal enum EditModeMeshExecutionStatus { Idle, Pending, Succeeded, Failed, Cancelled }

    /// <summary>Internal caller-driven Mesh phase seam consumed only by the Editor Humanoid compiler backend.</summary>
    internal interface IEditModeMeshBuildPhaseMachine
    {
        bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic);
        EditModeMeshExecutionStatus Pump(out StackMachineDiagnostic diagnostic);
        bool TryTakeResult(out EditModeMeshBuildResult result);
        void Cancel();
    }

    /// <summary>EditMode adapter for the shared Humanoid Mesh lifecycle; it owns only TextureEditModeStackMachine integration.</summary>
    internal sealed class EditModeMeshStackMachine : HumanoidMeshBuildMachine<EditModeMeshNormalCompletion>, IEditModeMeshBuildPhaseMachine
    {
        private readonly TextureEditModeStackMachine normalTextureMachine;

        internal EditModeMeshStackMachine() : this(null, false) { }
        internal EditModeMeshStackMachine(TextureEditModeStackMachine normalTextureMachine) : this(normalTextureMachine, false) { }
        internal EditModeMeshStackMachine(TextureEditModeStackMachine normalTextureMachine, bool publishResolvedHumanoidRestPose)
            : base(publishResolvedHumanoidRestPose)
        {
            this.normalTextureMachine = normalTextureMachine;
        }
        internal new EditModeMeshExecutionStatus Status => (EditModeMeshExecutionStatus)base.Status;
        internal new StackMachineDiagnostic Diagnostic => base.Diagnostic;
        internal new bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            bool accepted = base.Start(figureRoot, document, out diagnostic);
            if (!accepted && diagnostic != null && diagnostic.domainCode == "HumanoidMeshMachineBusy") diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "EditModeMeshMachineBusy", diagnostic.message);
            return accepted;
        }
        internal new EditModeMeshExecutionStatus Pump(out StackMachineDiagnostic diagnostic) => (EditModeMeshExecutionStatus)base.Pump(out diagnostic);

        internal bool TryTakeFbmBakeResult(out HumanoidMeshFbmBakeResult result)
        {
            result = null;
            if (NormalCompletionCount != 0 || !base.TryTake(out HumanoidMeshBuildEscrow<EditModeMeshNormalCompletion> escrow)) return false;
            result = escrow.DetachMesh();
            return result != null;
        }

        internal bool TryTakeResult(out EditModeMeshBuildResult result)
        {
            result = null;
            if (!base.TryTake(out HumanoidMeshBuildEscrow<EditModeMeshNormalCompletion> escrow)) return false;
            HumanoidMeshFbmBakeResult mesh = escrow.Mesh;
            EditModeMeshNormalCompletion[] normals = escrow.DetachNormals();
            StackMachineDiagnostic diagnostic = null;
            if (mesh == null || !EditModeMeshPayloadBuilder.TryCreateNormalPayloads(mesh, normals, out EditModeMeshNormalPayload[] payloads, out diagnostic))
            {
                for (int i = 0; i < normals.Length; i++) normals[i]?.Dispose();
                escrow.Dispose();
                FailAfterHandoff(diagnostic ?? StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadBuildFailed", "EditMode Mesh payload build failed after Core handoff."));
                return false;
            }
            var slots = new HumanoidMeshMaterialSlot[mesh.MaterialSlots.Count];
            for (int i = 0; i < slots.Length; i++) slots[i] = mesh.MaterialSlots[i];
            if (!HumanoidMeshVrmTransportProvenance.TryCreate(mesh.LogicalPlan, out HumanoidMeshVrmTransportProvenance provenance, out diagnostic))
            {
                for (int i = 0; i < normals.Length; i++) normals[i]?.Dispose();
                escrow.Dispose();
                FailAfterHandoff(diagnostic);
                return false;
            }
            result = new EditModeMeshBuildResult(escrow.DetachMesh(), slots, normals, payloads, provenance);
            return true;
        }

        protected override bool TryStartNormal(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            if (normalTextureMachine == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureMachineRequired", "EditMode Mesh NORMAL execution requires a dedicated TextureEditModeStackMachine."); return false; }
            return normalTextureMachine.Start(plan, out diagnostic);
        }

        protected override bool TryPumpNormal(out bool pending, out StackMachineDiagnostic diagnostic)
        {
            TextureEditModeExecutionStatus status = normalTextureMachine.Pump(out diagnostic);
            pending = status == TextureEditModeExecutionStatus.Pending;
            if (pending) return true;
            return status == TextureEditModeExecutionStatus.Succeeded;
        }

        protected override bool TryTakeNormal(HumanoidMeshNormalSource source, out EditModeMeshNormalCompletion completion, out StackMachineDiagnostic diagnostic)
        {
            completion = null;
            diagnostic = null;
            if (!normalTextureMachine.TryTakeCompletion(out TextureCompletion texture)) { diagnostic = normalTextureMachine.Diagnostic ?? StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureCompletionMissing", "EditMode Mesh NORMAL execution completed without a completion."); return false; }
            completion = new EditModeMeshNormalCompletion(source, texture);
            return true;
        }

        protected override void CancelNormal() => normalTextureMachine?.Cancel();
        protected override void DisposeNormal(EditModeMeshNormalCompletion completion) => completion?.Dispose();
        public override void Dispose() { base.Dispose(); normalTextureMachine?.Dispose(); }

        bool IEditModeMeshBuildPhaseMachine.Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic) => Start(figureRoot, document, out diagnostic);
        EditModeMeshExecutionStatus IEditModeMeshBuildPhaseMachine.Pump(out StackMachineDiagnostic diagnostic) => Pump(out diagnostic);
        bool IEditModeMeshBuildPhaseMachine.TryTakeResult(out EditModeMeshBuildResult result) => TryTakeResult(out result);
    }
}
