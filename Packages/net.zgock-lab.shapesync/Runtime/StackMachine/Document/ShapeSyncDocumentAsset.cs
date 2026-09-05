// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Serialized carrier that stores one ShapeSync document for Figure assignment and sharing.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/ShapeSync Document")]
    public class ShapeSyncDocumentAsset : ScriptableObject, IShapeSyncDocument
    {
        [SerializeField] private ShapeSyncDocument document = new ShapeSyncDocument();

        /// <summary>Gets or sets the detached Mesh recipe held by this serialized carrier.</summary>
        public MeshRecipeDocument MeshRecipe { get => document?.MeshRecipe; set { EnsureDocument(); document.MeshRecipe = value; } }

        /// <summary>Gets or sets the shared Mesh logical binding declaration held by this serialized carrier.</summary>
        public MeshBinding MeshBinding { get => document?.MeshBinding; set { EnsureDocument(); document.MeshBinding = value; } }

        /// <summary>Gets or sets the detached Material recipe held by this serialized carrier.</summary>
        public MaterialRecipeDocument MaterialRecipe { get => document?.MaterialRecipe; set { EnsureDocument(); document.MaterialRecipe = value; } }

        /// <summary>Gets or sets the shared Material logical binding declaration held by this serialized carrier.</summary>
        public MaterialBinding MaterialBinding { get => document?.MaterialBinding; set { EnsureDocument(); document.MaterialBinding = value; } }

        /// <summary>Creates a non-mutating snapshot of the carrier document.</summary>
        /// <param name="snapshot">A detached recipe-metadata copy that retains the carrier Binding references.</param>
        /// <param name="diagnostic">A structured failure when the stored document cannot be snapshotted.</param>
        /// <returns><see langword="true"/> when a snapshot was created without mutating this carrier.</returns>
        public bool TryGetSnapshot(out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic) => ShapeSyncDocument.TryCreateSnapshot(this, out snapshot, out diagnostic);
        /// <summary>Replaces the carrier content with a non-mutating snapshot of <paramref name="source"/>.</summary>
        /// <param name="source">The caller-owned document to snapshot into this carrier.</param>
        /// <param name="diagnostic">A structured failure when <paramref name="source"/> cannot be snapshotted.</param>
        /// <returns><see langword="true"/> when the carrier was replaced with a detached snapshot.</returns>
        public bool TrySetDocument(IShapeSyncDocument source, out StackMachineDiagnostic diagnostic)
        {
            if (!ShapeSyncDocument.TryCreateSnapshot(source, out ShapeSyncDocument snapshot, out diagnostic)) return false;
            document = snapshot;
            return true;
        }

        private void EnsureDocument()
        {
            document ??= new ShapeSyncDocument();
        }
    }
}
