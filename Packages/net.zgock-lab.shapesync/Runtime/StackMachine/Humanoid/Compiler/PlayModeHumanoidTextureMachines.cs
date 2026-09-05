// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>PlayMode adapter for shared Humanoid Mesh execution; it owns only Normal Texture queue handles.</summary>
    public sealed class PlayModeHumanoidMeshStackMachine : HumanoidMeshBuildMachine<TextureDelivery>
    {
        private readonly TextureExecutor executor;
        private TextureExecutionHandle handle;
        private readonly TextureExecutionOriginKey origin;

        /// <summary>Creates a Mesh adapter bound to one scene-local Texture StackMachine host.</summary>
        /// <remarks>Hot Bake publishes a Pure Humanoid, so the resolved Humanoid rest pose is part of its output contract.</remarks>
        public PlayModeHumanoidMeshStackMachine(TextureStackMachineHost host)
            : base(true)
        {
            executor = new TextureExecutor(host);
            origin = host == null ? default : host.CreateOrigin();
        }

        protected override bool TryStartNormal(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
            => executor.TryExecute(plan, origin, null, out handle, out diagnostic);

        protected override bool TryPumpNormal(out bool pending, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (handle == null) { pending = false; diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "PlayModeNormalHandleMissing", "PlayMode Mesh NORMAL execution has no active Texture handle."); return false; }
            pending = !handle.IsCompleted;
            if (pending) return true;
            diagnostic = handle.Diagnostic;
            return handle.Succeeded;
        }

        protected override bool TryTakeNormal(HumanoidMeshNormalSource source, out TextureDelivery completion, out StackMachineDiagnostic diagnostic)
        {
            completion = null; diagnostic = null;
            if (handle == null || !handle.Succeeded || handle.Result == null || !handle.Result.TryTakeDelivery(out completion))
            {
                diagnostic = handle?.Diagnostic ?? StackMachineDiagnostic.CreateDomain("mesh", "PlayModeNormalCompletionMissing", "PlayMode Mesh NORMAL execution completed without a delivery.");
                return false;
            }
            handle.Dispose(); handle = null;
            return true;
        }

        protected override void CancelNormal() { handle?.Dispose(); handle = null; }
        protected override void DisposeNormal(TextureDelivery completion) => completion?.Dispose();
    }

    /// <summary>PlayMode adapter for shared Humanoid Material execution; it owns only Texture queue handles.</summary>
    public sealed class PlayModeHumanoidMaterialStackMachine : HumanoidMaterialBuildMachine<TextureDelivery>
    {
        private readonly TextureExecutor executor;
        private TextureExecutionHandle handle;
        private readonly TextureExecutionOriginKey origin;

        /// <summary>Creates a Material adapter bound to one scene-local Texture StackMachine host.</summary>
        public PlayModeHumanoidMaterialStackMachine(TextureStackMachineHost host)
        {
            executor = new TextureExecutor(host);
            origin = host == null ? default : host.CreateOrigin();
        }

        protected override bool TryStartTexture(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
            => executor.TryExecute(plan, origin, null, out handle, out diagnostic);

        protected override bool TryPumpTexture(out bool pending, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (handle == null) { pending = false; diagnostic = StackMachineDiagnostic.CreateDomain("material", "PlayModeTextureHandleMissing", "PlayMode Material execution has no active Texture handle."); return false; }
            pending = !handle.IsCompleted;
            if (pending) return true;
            diagnostic = handle.Diagnostic;
            return handle.Succeeded;
        }

        protected override bool TryTakeTexture(out TextureDelivery completion, out StackMachineDiagnostic diagnostic)
        {
            completion = null; diagnostic = null;
            if (handle == null || !handle.Succeeded || handle.Result == null || !handle.Result.TryTakeDelivery(out completion))
            {
                diagnostic = handle?.Diagnostic ?? StackMachineDiagnostic.CreateDomain("material", "PlayModeTextureCompletionMissing", "PlayMode Material execution completed without a delivery.");
                return false;
            }
            handle.Dispose(); handle = null;
            return true;
        }

        protected override void CancelTexture() { handle?.Dispose(); handle = null; }
        protected override void DisposeTexture(TextureDelivery completion) => completion?.Dispose();
    }
}
