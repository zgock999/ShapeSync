// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Lifecycle state shared by every Humanoid Material build backend.</summary>
    public enum HumanoidMaterialBuildStatus { Idle, Pending, Succeeded, Failed, Cancelled }

    /// <summary>One pure Material semantic payload. The generic completion remains in-memory until its receiver disposes it.</summary>
    /// <typeparam name="TTextureCompletion">Backend-specific in-memory texture completion type.</typeparam>
    public readonly struct HumanoidMaterialBuildPayload<TTextureCompletion>
    {
        internal HumanoidMaterialBuildPayload(MaterialId materialId, TTextureCompletion mainTex, bool hasMainTex, bool hasColor, Color color, bool hasUvSet, Vector2 uvScale, Vector2 uvOffset)
        {
            MaterialId = materialId; MainTex = mainTex; HasMainTex = hasMainTex; HasColor = hasColor; Color = color; HasUvSet = hasUvSet; UvScale = uvScale; UvOffset = uvOffset;
        }
        /// <summary>Gets the final Material semantic identity.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the backend-owned MainTex completion when <see cref="HasMainTex"/> is true.</summary>
        public TTextureCompletion MainTex { get; }
        /// <summary>Gets whether this payload includes a resolved MainTex completion.</summary>
        public bool HasMainTex { get; }
        /// <summary>Gets whether this payload includes a COLOR semantic value.</summary>
        public bool HasColor { get; }
        /// <summary>Gets the resolved linear COLOR semantic value.</summary>
        public Color Color { get; }
        /// <summary>Gets whether this payload includes a UVSET semantic value.</summary>
        public bool HasUvSet { get; }
        /// <summary>Gets the resolved UVSET scale.</summary>
        public Vector2 UvScale { get; }
        /// <summary>Gets the resolved UVSET offset.</summary>
        public Vector2 UvOffset { get; }
    }

    /// <summary>Core-owned pure payload escrow. It owns taken Texture completions until a caller receives or disposes it.</summary>
    /// <typeparam name="TTextureCompletion">Backend-specific in-memory texture completion type.</typeparam>
    public sealed class HumanoidMaterialBuildEscrow<TTextureCompletion> : IDisposable
    {
        private HumanoidMaterialBuildPayload<TTextureCompletion>[] payloads;
        private readonly Action<TTextureCompletion> disposeTexture;
        internal HumanoidMaterialBuildEscrow(HumanoidMaterialBuildPayload<TTextureCompletion>[] payloads, Action<TTextureCompletion> disposeTexture) { this.payloads = payloads ?? Array.Empty<HumanoidMaterialBuildPayload<TTextureCompletion>>(); this.disposeTexture = disposeTexture; }
        /// <summary>Gets the owned semantic payloads without transferring their ownership.</summary>
        public IReadOnlyList<HumanoidMaterialBuildPayload<TTextureCompletion>> Payloads => Array.AsReadOnly(payloads);
        /// <summary>Transfers the payload array to the caller without disposing its texture completions.</summary>
        public HumanoidMaterialBuildPayload<TTextureCompletion>[] DetachPayloads() { var value = payloads; payloads = Array.Empty<HumanoidMaterialBuildPayload<TTextureCompletion>>(); return value; }
        /// <inheritdoc />
        public void Dispose() { for (int i = 0; i < payloads.Length; i++) if (payloads[i].HasMainTex) disposeTexture?.Invoke(payloads[i].MainTex); payloads = Array.Empty<HumanoidMaterialBuildPayload<TTextureCompletion>>(); }
    }

    /// <summary>Owns backend-independent Material recipe execution. Derived backends provide only EditMode or PlayMode Texture execution.</summary>
    /// <typeparam name="TTextureCompletion">Backend-specific completion returned for one Texture StackMachine execution.</typeparam>
    public abstract class HumanoidMaterialBuildMachine<TTextureCompletion> : IDisposable
    {
        private HumanoidMaterialLogicalPlan plan;
        private readonly List<HumanoidMaterialBuildPayload<TTextureCompletion>> payloads = new List<HumanoidMaterialBuildPayload<TTextureCompletion>>();
        private int targetIndex;
        private int blockIndex;
        private HumanoidMaterialTargetPlan activeTarget;
        private MaterialStackMachineBlock activeBlock;
        private HumanoidMaterialEntrySource activeEntry;
        private bool textureActive;
        private bool disposed;

        /// <summary>Gets the current machine lifecycle state.</summary>
        public HumanoidMaterialBuildStatus Status { get; private set; }
        /// <summary>Gets the terminal failure diagnostic, or null while no failure is recorded.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }

        /// <summary>Collects and validates the Material plan, then starts caller-driven execution.</summary>
        /// <param name="figureRoot">Read-only Figure root used to resolve Material bindings.</param>
        /// <param name="document">Detached ShapeSync document that supplies the recipe.</param>
        /// <param name="diagnostic">A structured diagnostic when the operation cannot start.</param>
        /// <returns>True when the machine entered <see cref="HumanoidMaterialBuildStatus.Pending"/>.</returns>
        public bool Start(GameObject figureRoot, ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("HumanoidMaterialMachineDisposed", "Humanoid Material build machine has been disposed.", out diagnostic);
            if (Status == HumanoidMaterialBuildStatus.Pending || plan != null || payloads.Count != 0) return Reject("HumanoidMaterialMachineBusy", "Take or cancel the prior Humanoid Material build before starting another execution.", out diagnostic);
            if (!HumanoidMaterialLogicalCollector.TryCreate(figureRoot, document, out plan, out diagnostic)) { Fail(diagnostic); return false; }
            targetIndex = 0; blockIndex = 0; activeTarget = null; activeBlock = null; activeEntry = default; textureActive = false; Diagnostic = null; Status = HumanoidMaterialBuildStatus.Pending; return true;
        }

        /// <summary>Advances at most the active Material / Texture work and never schedules itself.</summary>
        /// <param name="diagnostic">The current failure diagnostic when the returned state is failed.</param>
        /// <returns>The resulting lifecycle state after this explicit pump.</returns>
        public HumanoidMaterialBuildStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (Status != HumanoidMaterialBuildStatus.Pending) { diagnostic = Diagnostic; return Status; }
            if (textureActive) return PumpTexture(out diagnostic);
            while (targetIndex < plan.Targets.Count)
            {
                HumanoidMaterialTargetPlan target = plan.Targets[targetIndex];
                if (blockIndex >= target.CorePlan.Blocks.Count) { targetIndex++; blockIndex = 0; continue; }
                MaterialStackMachineBlock block = target.CorePlan.Blocks[blockIndex++];
                if (block.IsReset) continue;
                if (!target.TryGetEntry(block.BindingName, out HumanoidMaterialEntrySource entry)) return Fail(StackMachineDiagnostic.CreateDomain("material", "MaterialBindingMissing", "Material build block does not resolve to a collected MaterialId.", bindingName: block.BindingName));
                if (block.TextureSource == null) { payloads.Add(CreatePayload(entry.MaterialId, block, default, false)); continue; }
                if (!HumanoidMaterialTexturePlanBuilder.TryCreate(block, target.TextureDocument, plan.TextureBinding, out TextureExecutionPlan texturePlan, out diagnostic)) return Fail(diagnostic);
                activeTarget = target; activeBlock = block; activeEntry = entry;
                if (!TryStartTexture(texturePlan, out diagnostic)) return Fail(diagnostic);
                textureActive = true;
                return Status;
            }
            plan = null; Status = HumanoidMaterialBuildStatus.Succeeded; return Status;
        }

        /// <summary>Transfers the completed payload escrow after a successful build.</summary>
        /// <param name="result">The caller-owned escrow on success; otherwise null.</param>
        /// <returns>True only after <see cref="HumanoidMaterialBuildStatus.Succeeded"/>.</returns>
        public bool TryTake(out HumanoidMaterialBuildEscrow<TTextureCompletion> result)
        {
            result = null;
            if (Status != HumanoidMaterialBuildStatus.Succeeded) return false;
            result = new HumanoidMaterialBuildEscrow<TTextureCompletion>(payloads.ToArray(), DisposeTexture);
            payloads.Clear(); Diagnostic = null; Status = HumanoidMaterialBuildStatus.Idle; return true;
        }

        /// <summary>Cancels active backend Texture work and disposes every unhanded completion owned by this machine.</summary>
        public void Cancel()
        {
            if (disposed) return;
            if (textureActive) CancelTexture();
            DisposePayloads(); ClearActive(); plan = null; Diagnostic = null; Status = HumanoidMaterialBuildStatus.Cancelled;
        }

        /// <inheritdoc />
        public virtual void Dispose() { if (disposed) return; Cancel(); disposed = true; }

        private HumanoidMaterialBuildStatus PumpTexture(out StackMachineDiagnostic diagnostic)
        {
            if (!TryPumpTexture(out bool pending, out diagnostic)) return Fail(diagnostic);
            if (pending) return Status;
            if (!TryTakeTexture(out TTextureCompletion completion, out diagnostic)) return Fail(diagnostic);
            payloads.Add(CreatePayload(activeEntry.MaterialId, activeBlock, completion, true));
            ClearActive(); return Pump(out diagnostic);
        }

        private static HumanoidMaterialBuildPayload<TTextureCompletion> CreatePayload(MaterialId materialId, MaterialStackMachineBlock block, TTextureCompletion mainTex, bool hasMainTex)
            => new HumanoidMaterialBuildPayload<TTextureCompletion>(materialId, mainTex, hasMainTex, block.HasColor, block.Color, block.HasUvTransform, block.UvScale, block.UvOffset);

        private HumanoidMaterialBuildStatus Fail(StackMachineDiagnostic diagnostic)
        {
            if (textureActive) CancelTexture();
            DisposePayloads(); ClearActive(); plan = null; Diagnostic = diagnostic; Status = HumanoidMaterialBuildStatus.Failed; return Status;
        }

        private void ClearActive() { activeTarget = null; activeBlock = null; activeEntry = default; textureActive = false; }
        private void DisposePayloads() { for (int i = 0; i < payloads.Count; i++) if (payloads[i].HasMainTex) DisposeTexture(payloads[i].MainTex); payloads.Clear(); }
        private bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message); return false; }

        /// <summary>Starts one lower-level Texture execution for the supplied immutable plan.</summary>
        /// <param name="plan">The validated Texture execution plan.</param>
        /// <param name="diagnostic">A structured diagnostic when dispatch fails.</param>
        /// <returns>True when the backend accepted ownership of the active Texture execution.</returns>
        protected abstract bool TryStartTexture(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic);
        /// <summary>Pumps the active lower-level Texture execution once.</summary>
        /// <param name="pending">True when another explicit pump is required.</param>
        /// <param name="diagnostic">A structured diagnostic when the Texture execution fails.</param>
        /// <returns>True when the backend pump itself completed without failure.</returns>
        protected abstract bool TryPumpTexture(out bool pending, out StackMachineDiagnostic diagnostic);
        /// <summary>Takes the completed Texture result after the backend reports non-pending completion.</summary>
        /// <param name="completion">The ownership-transferred completion on success.</param>
        /// <param name="diagnostic">A structured diagnostic when no valid completion is available.</param>
        /// <returns>True when ownership was transferred to this core machine.</returns>
        protected abstract bool TryTakeTexture(out TTextureCompletion completion, out StackMachineDiagnostic diagnostic);
        /// <summary>Cancels the active lower-level Texture execution without rolling back higher-level artifacts.</summary>
        protected abstract void CancelTexture();
        /// <summary>Disposes one completion that remains owned by this core machine.</summary>
        /// <param name="completion">The backend completion to release.</param>
        protected abstract void DisposeTexture(TTextureCompletion completion);
    }
}
