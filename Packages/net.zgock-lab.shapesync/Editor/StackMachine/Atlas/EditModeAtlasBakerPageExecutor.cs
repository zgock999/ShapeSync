// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// EditMode Atlas page executor. It deliberately lowers one Core page into one Texture recipe;
    /// thus EditMode selects partition count one without making that choice part of the Core contract.
    /// </summary>
    public sealed class EditModeAtlasBakerPageExecutor : IAtlasBakerPageExecutor
    {
        private readonly TextureEditModeStackMachine textureMachine;
        private AtlasBakerPagePlan activePage;
        private bool disposed;

        /// <summary>Creates an executor using the supplied Texture StackMachine compute programs.</summary>
        public EditModeAtlasBakerPageExecutor(ComputeShader textureProgram, ComputeShader normalProgram = null)
            : this(new TextureEditModeStackMachine(textureProgram, normalProgram)) { }

        internal EditModeAtlasBakerPageExecutor(TextureEditModeStackMachine textureMachine)
        {
            this.textureMachine = textureMachine;
        }

        /// <inheritdoc />
        public bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("EditModeAtlasPageExecutorDisposed", "EditMode Atlas page executor has been disposed.", out diagnostic);
            if (activePage != null) return Reject("EditModeAtlasPageExecutorBusy", "Take, cancel, or complete the active Atlas page before starting another.", out diagnostic);
            if (!AtlasEditModeRecipeBuilder.TryCreate(page, out TextureExecutionPlan plan, out diagnostic)) return false;
            if (!textureMachine.Start(plan, out diagnostic)) return false;
            activePage = page;
            return true;
        }

        /// <inheritdoc />
        public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            TextureEditModeExecutionStatus status = textureMachine == null ? TextureEditModeExecutionStatus.Failed : textureMachine.Pump(out diagnostic);
            if (textureMachine == null) diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "EditModeTextureMachineRequired", "EditMode Atlas execution requires a TextureEditModeStackMachine.");
            return status == TextureEditModeExecutionStatus.Pending ? AtlasBakerExecutionStatus.Pending :
                status == TextureEditModeExecutionStatus.Succeeded ? AtlasBakerExecutionStatus.Succeeded :
                status == TextureEditModeExecutionStatus.Cancelled ? AtlasBakerExecutionStatus.Cancelled : AtlasBakerExecutionStatus.Failed;
        }

        /// <inheritdoc />
        public bool TryTakeCompletion(out AtlasBakerPageCompletion completion)
        {
            completion = null;
            if (activePage == null || textureMachine == null || !textureMachine.TryTakeCompletion(out TextureCompletion textureCompletion)) return false;
            AtlasBakerPagePlan page = activePage;
            activePage = null;
            completion = new AtlasBakerPageCompletion(page.PageIndex, page.Semantic, textureCompletion.Texture, _ => textureCompletion.Dispose());
            return true;
        }

        /// <inheritdoc />
        public void Cancel()
        {
            textureMachine?.Cancel();
            activePage = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            textureMachine?.Dispose();
            disposed = true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }
    }

    /// <summary>Builds the single EditMode Texture StackMachine recipe selected for one Core Atlas page.</summary>
    internal static class AtlasEditModeRecipeBuilder
    {
        internal static bool TryCreate(AtlasBakerPagePlan page, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
            => AtlasBakerPageRecipeBuilder.TryCreate(page, out plan, out diagnostic);
    }
}
