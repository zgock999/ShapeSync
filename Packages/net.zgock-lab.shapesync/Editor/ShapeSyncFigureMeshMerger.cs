// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Editor-only boundary that reuses Mesh Utility's exact skinned-mesh merge implementation.</summary>
    public static class ShapeSyncFigureMeshMerger
    {
        /// <summary>Owns an unsaved merged clone and its in-memory Mesh until transferred or disposed.</summary>
        public sealed class Result : IDisposable
        {
            private Mesh ownedMesh;
            private readonly SkinnedMeshRenderer[] confirmedSourceRendererOrder;
            internal Result(GameObject root, SkinnedMeshRenderer renderer, IReadOnlyList<SkinnedMeshRenderer> sourceRendererOrder)
            {
                Root = root;
                Renderer = renderer;
                ownedMesh = renderer.sharedMesh;
                confirmedSourceRendererOrder = new SkinnedMeshRenderer[sourceRendererOrder.Count];
                for (int index = 0; index < sourceRendererOrder.Count; index++) confirmedSourceRendererOrder[index] = sourceRendererOrder[index];
            }
            /// <summary>Gets the unsaved merged hierarchy clone.</summary>
            public GameObject Root { get; }
            /// <summary>Gets the renderer containing the merged geometry.</summary>
            public SkinnedMeshRenderer Renderer { get; }
            /// <summary>Gets the caller-confirmed source renderer order separately from the merged output renderer.</summary>
            public IReadOnlyList<SkinnedMeshRenderer> ConfirmedSourceRendererOrder => Array.AsReadOnly(confirmedSourceRendererOrder);
            /// <summary>Transfers ownership of the merged Mesh to the caller.</summary>
            /// <returns>The merged Mesh, or null when ownership was already transferred.</returns>
            public Mesh DetachMesh() { Mesh mesh = ownedMesh; ownedMesh = null; return mesh; }
            /// <summary>Releases the unsaved merged hierarchy and Mesh still owned by this result.</summary>
            public void Dispose()
            {
                // A transaction may have staged the owned Mesh as a Database sub-asset
                // before a later save/validation failure.  Rollback restores the file
                // snapshot and force-reimports it; staged objects are not destroyed by
                // Result.Dispose, but become invalidated by that restoration.
                if (ownedMesh != null && !UnityEditor.EditorUtility.IsPersistent(ownedMesh))
                    UnityEngine.Object.DestroyImmediate(ownedMesh);
                if (Root != null && !UnityEditor.EditorUtility.IsPersistent(Root))
                    UnityEngine.Object.DestroyImmediate(Root);
                ownedMesh = null;
            }
        }

        /// <summary>Clones and merges the specified renderer order without changing the source hierarchy or assets.</summary>
        public static bool TryMerge(
            GameObject humanoidRoot,
            IReadOnlyList<SkinnedMeshRenderer> rendererOrder,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            return TryMergeCore(humanoidRoot, rendererOrder, null, false, out mergedRoot, out mergedRenderer, out diagnostic);
        }

        /// <summary>Clones and merges geometry without requiring source Material payload.
        /// PBM callers bind canonical Figure/Outfit Materials only after the geometry merge.</summary>
        internal static bool TryMergeGeometryOnly(
            GameObject humanoidRoot,
            IReadOnlyList<SkinnedMeshRenderer> rendererOrder,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            return TryMergeCore(humanoidRoot, rendererOrder, null, true, out mergedRoot, out mergedRenderer, out diagnostic);
        }

        private static bool TryMergeCore(
            GameObject humanoidRoot,
            IReadOnlyList<SkinnedMeshRenderer> rendererOrder,
            Action<Mesh> afterMergedMeshAllocated,
            bool allowMissingMaterials,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            mergedRoot = null;
            mergedRenderer = null;
            diagnostic = null;
            if (humanoidRoot == null)
            {
                diagnostic = "ShapeSync Figure merge requires a Humanoid root.";
                return false;
            }

            if (rendererOrder == null || rendererOrder.Count == 0)
            {
                diagnostic = "ShapeSync Figure merge requires at least one renderer.";
                return false;
            }

            var unique = new HashSet<SkinnedMeshRenderer>();
            for (int index = 0; index < rendererOrder.Count; index++)
            {
                SkinnedMeshRenderer renderer = rendererOrder[index];
                if (renderer == null || !renderer.transform.IsChildOf(humanoidRoot.transform) || !unique.Add(renderer))
                {
                    diagnostic = "ShapeSync Figure merge requires non-null, unique renderers below the Humanoid root.";
                    return false;
                }

                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || renderer.bones == null || renderer.bones.Length == 0 || mesh.bindposes == null || mesh.bindposes.Length != renderer.bones.Length || mesh.boneWeights == null || mesh.boneWeights.Length != mesh.vertexCount
                    || (!allowMissingMaterials && (renderer.sharedMaterials == null || renderer.sharedMaterials.Length < mesh.subMeshCount)))
                {
                    diagnostic = allowMissingMaterials
                        ? "ShapeSync Figure geometry merge requires Mesh Utility-compatible mesh, bone, weight, and bindpose data."
                        : "ShapeSync Figure merge requires Mesh Utility-compatible mesh, bone, weight, bindpose, and material data.";
                    return false;
                }
            }

            return allowMissingMaterials
                ? SkinnedMeshMergerWindow.TryCreateMergedCloneGeometryOnly(humanoidRoot, rendererOrder, afterMergedMeshAllocated, out mergedRoot, out mergedRenderer, out diagnostic)
                : SkinnedMeshMergerWindow.TryCreateMergedClone(humanoidRoot, rendererOrder, afterMergedMeshAllocated, out mergedRoot, out mergedRenderer, out diagnostic);
        }

        /// <summary>Creates an owned merge result for callers that must reliably clean up after save or rollback failure.</summary>
        public static bool TryMergeOwned(GameObject humanoidRoot, IReadOnlyList<SkinnedMeshRenderer> rendererOrder, out Result result, out string diagnostic)
        {
            return TryMergeOwnedCore(humanoidRoot, rendererOrder, null, false, out result, out diagnostic);
        }

        /// <summary>Creates an owned geometry-only merge for PBM sources whose renderer
        /// Material arrays are intentionally absent.</summary>
        internal static bool TryMergeOwnedGeometryOnly(GameObject humanoidRoot,
            IReadOnlyList<SkinnedMeshRenderer> rendererOrder, out Result result, out string diagnostic)
        {
            return TryMergeOwnedCore(humanoidRoot, rendererOrder, null, true, out result, out diagnostic);
        }

        /// <summary>Test-only overload that injects a build failure without relying on mutable Window state.</summary>
        internal static bool TryMergeOwnedForTests(GameObject humanoidRoot, IReadOnlyList<SkinnedMeshRenderer> rendererOrder, Action<Mesh> afterMergedMeshAllocated, out Result result, out string diagnostic)
        {
            return TryMergeOwnedCore(humanoidRoot, rendererOrder, afterMergedMeshAllocated, false, out result, out diagnostic);
        }

        private static bool TryMergeOwnedCore(GameObject humanoidRoot, IReadOnlyList<SkinnedMeshRenderer> rendererOrder,
            Action<Mesh> afterMergedMeshAllocated, bool allowMissingMaterials, out Result result, out string diagnostic)
        {
            result = null;
            if (!TryMergeCore(humanoidRoot, rendererOrder, afterMergedMeshAllocated, allowMissingMaterials,
                out GameObject root, out SkinnedMeshRenderer renderer, out diagnostic)) return false;
            result = new Result(root, renderer, rendererOrder);
            return true;
        }
    }
}
