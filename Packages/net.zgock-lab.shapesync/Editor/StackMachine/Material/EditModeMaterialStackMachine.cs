// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    internal enum EditModeMaterialExecutionStatus { Idle, Pending, Succeeded, Failed, Cancelled }

    /// <summary>Internal caller-driven Material phase seam consumed only by the Editor Humanoid compiler backend.</summary>
    internal interface IEditModeMaterialBuildPhaseMachine
    {
        bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic);
        EditModeMaterialExecutionStatus Pump(out StackMachineDiagnostic diagnostic);
        bool TryTakeResult(out EditModeMaterialBuildResult result);
        void Cancel();
    }

    /// <summary>Owns taken EditMode Material payloads until the Compiler accepts or disposes them.</summary>
    internal sealed class EditModeMaterialBuildResult : IDisposable
    {
        private HumanoidMaterialBuildPayload<TextureCompletion>[] payloads;
        internal EditModeMaterialBuildResult(HumanoidMaterialBuildPayload<TextureCompletion>[] payloads) { this.payloads = payloads ?? Array.Empty<HumanoidMaterialBuildPayload<TextureCompletion>>(); }
        internal HumanoidMaterialBuildPayload<TextureCompletion>[] DetachPayloads() { var value = payloads; payloads = Array.Empty<HumanoidMaterialBuildPayload<TextureCompletion>>(); return value; }
        public void Dispose() { for (int i = 0; i < payloads.Length; i++) if (payloads[i].HasMainTex) payloads[i].MainTex?.Dispose(); payloads = Array.Empty<HumanoidMaterialBuildPayload<TextureCompletion>>(); }
    }

    /// <summary>EditMode adapter for the shared Humanoid Material lifecycle; it owns only TextureEditModeStackMachine integration.</summary>
    internal sealed class EditModeMaterialStackMachine : HumanoidMaterialBuildMachine<TextureCompletion>, IEditModeMaterialBuildPhaseMachine
    {
        private readonly TextureEditModeStackMachine textureMachine;
        internal EditModeMaterialStackMachine(TextureEditModeStackMachine textureMachine) { this.textureMachine = textureMachine; }
        internal new EditModeMaterialExecutionStatus Status => (EditModeMaterialExecutionStatus)base.Status;
        internal new StackMachineDiagnostic Diagnostic => base.Diagnostic;
        internal new bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            bool accepted = base.Start(figureRoot, document, out diagnostic);
            if (!accepted && diagnostic != null && diagnostic.domainCode == "HumanoidMaterialMachineBusy") diagnostic = StackMachineDiagnostic.CreateDomain("material", "EditModeMaterialMachineBusy", diagnostic.message);
            return accepted;
        }
        internal new EditModeMaterialExecutionStatus Pump(out StackMachineDiagnostic diagnostic) => (EditModeMaterialExecutionStatus)base.Pump(out diagnostic);
        internal bool TryTakeResult(out EditModeMaterialBuildResult result)
        {
            result = null;
            if (!base.TryTake(out HumanoidMaterialBuildEscrow<TextureCompletion> escrow)) return false;
            try { result = new EditModeMaterialBuildResult(escrow.DetachPayloads()); return true; }
            finally { escrow.Dispose(); }
        }
        protected override bool TryStartTexture(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            if (textureMachine == null) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureMachineRequired", "EditMode Material execution requires a TextureEditModeStackMachine."); return false; }
            return textureMachine.Start(plan, out diagnostic);
        }
        protected override bool TryPumpTexture(out bool pending, out StackMachineDiagnostic diagnostic)
        {
            TextureEditModeExecutionStatus status = textureMachine.Pump(out diagnostic);
            pending = status == TextureEditModeExecutionStatus.Pending;
            if (pending) return true;
            return status == TextureEditModeExecutionStatus.Succeeded;
        }
        protected override bool TryTakeTexture(out TextureCompletion completion, out StackMachineDiagnostic diagnostic)
        {
            completion = null; diagnostic = null;
            if (textureMachine.TryTakeCompletion(out completion)) return true;
            diagnostic = textureMachine.Diagnostic ?? StackMachineDiagnostic.CreateDomain("material", "TextureCompletionMissing", "EditMode Material Texture execution completed without a completion.");
            return false;
        }
        protected override void CancelTexture() => textureMachine?.Cancel();
        protected override void DisposeTexture(TextureCompletion completion) => completion?.Dispose();
        bool IEditModeMaterialBuildPhaseMachine.Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic) => Start(figureRoot, document, out diagnostic);
        EditModeMaterialExecutionStatus IEditModeMaterialBuildPhaseMachine.Pump(out StackMachineDiagnostic diagnostic) => Pump(out diagnostic);
        bool IEditModeMaterialBuildPhaseMachine.TryTakeResult(out EditModeMaterialBuildResult result) => TryTakeResult(out result);
        void IEditModeMaterialBuildPhaseMachine.Cancel() => Cancel();
        public override void Dispose() { base.Dispose(); textureMachine?.Dispose(); }
    }
}
