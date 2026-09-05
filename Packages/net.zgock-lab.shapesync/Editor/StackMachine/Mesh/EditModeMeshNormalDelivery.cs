// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>One TSM-owned Normal completion retained by the EditMode Mesh adapter until compiler handoff.</summary>
    internal sealed class EditModeMeshNormalCompletion : IDisposable
    {
        private bool disposed;

        internal EditModeMeshNormalCompletion(HumanoidMeshNormalSource source, TextureCompletion completion)
        {
            Source = source;
            Completion = completion;
        }

        internal HumanoidMeshNormalSource Source { get; }
        internal TextureCompletion Completion { get; private set; }

        /// <summary>Transfers the owned Texture completion exactly once to the Compiler carrier.</summary>
        internal TextureCompletion DetachCompletion()
        {
            TextureCompletion value = Completion;
            Completion = null;
            disposed = true;
            return value;
        }

        public void Dispose()
        {
            if (disposed) return;
            Completion?.Dispose();
            Completion = null;
            disposed = true;
        }
    }

    /// <summary>One Mesh-semantic Normal completion keyed by the Core MaterialId.</summary>
    public readonly struct EditModeMeshNormalPayload
    {
        public EditModeMeshNormalPayload(MaterialId materialId, TextureCompletion completion)
        {
            MaterialId = materialId;
            Completion = completion;
        }

        public MaterialId MaterialId { get; }
        public TextureCompletion Completion { get; }
    }
}
