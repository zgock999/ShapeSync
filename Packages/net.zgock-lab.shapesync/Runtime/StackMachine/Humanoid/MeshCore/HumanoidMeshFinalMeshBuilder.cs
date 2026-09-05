// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Builds the single unpublished final Mesh after all logical Mesh phases have completed.</summary>
    public static class HumanoidMeshFinalMeshBuilder
    {
        public static bool TryBuild(HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (bake == null || bake.Skeleton == null || bake.BoneTable == null)
                return Fail("MeshFinalizationInputRequired", "Final Mesh build requires completed geometry, skeleton, and bone table escrow.", out diagnostic);
            if (bake.FinalMesh != null)
                return Fail("MeshFinalizationAlreadyBuilt", "Final Mesh build may run only once per Mesh escrow.", out diagnostic);

            var finalByFigureBone = new Dictionary<Transform, Transform>();
            Transform[] figureBones = bake.LogicalPlan.Figure.Renderer.bones;
            for (int i = 0; i < figureBones.Length && i < bake.BoneTable.Bones.Length; i++) if (figureBones[i] != null) finalByFigureBone[figureBones[i]] = bake.BoneTable.Bones[i];

            var remapped = new List<Mesh>(bake.Sources.Count);
            try
            {
                for (int i = 0; i < bake.Sources.Count; i++)
                {
                    if (!HumanoidMeshSkinningRemapper.TryRemap(bake.Sources[i], bake.Skeleton, bake.BoneTable, bake.ExtraBoneTransforms, finalByFigureBone, bake.FbmWeights, out Mesh mesh, out diagnostic)) return false;
                    remapped.Add(mesh);
                }
                var combineSources = new List<HumanoidMeshCombineSource>(remapped.Count);
                Transform sourceFigureRenderer = bake.LogicalPlan.Figure.Renderer == null ? null : bake.LogicalPlan.Figure.Renderer.transform;
                string figureRendererPath = GetRelativePath(bake.LogicalPlan.Figure.Root == null ? null : bake.LogicalPlan.Figure.Root.transform, sourceFigureRenderer);
                Transform figureRenderer = figureRendererPath == null || bake.Skeleton.Root == null
                    ? null
                    : (string.IsNullOrEmpty(figureRendererPath) ? bake.Skeleton.Root.transform : bake.Skeleton.Root.transform.Find(figureRendererPath));
                if (figureRenderer == null) return Fail("FigureRendererRequired", "Final Mesh build requires the Figure output renderer Transform.", out diagnostic);
                for (int i = 0; i < remapped.Count; i++)
                {
                    Transform sourceRenderer = bake.Sources[i].Source.Renderer == null ? null : bake.Sources[i].Source.Renderer.transform;
                    if (sourceRenderer == null) return Fail("SourceRendererRequired", "Final Mesh build requires every source renderer Transform.", out diagnostic);
                    if (!TryCreateAttachedSourceToOutput(bake.Skeleton.Root.transform, figureRenderer, bake.Sources[i].Source.Root == null ? null : bake.Sources[i].Source.Root.transform, sourceRenderer, out Matrix4x4 sourceToOutput, out diagnostic)) return false;
                    combineSources.Add(new HumanoidMeshCombineSource(remapped[i], sourceToOutput));
                }
                if (!HumanoidMeshCombiner.TryCombine(combineSources, bake.BoneTable, out Mesh finalMesh, out int[] firstSubmeshBySource, out diagnostic)) return false;
                if (!HumanoidMeshFinalBlendShapeNormalizer.TryNormalize(finalMesh, bake.LogicalPlan, out diagnostic))
                {
                    HumanoidMeshResourceCleanup.Destroy(finalMesh);
                    return false;
                }
                bake.SetFinalMesh(finalMesh, firstSubmeshBySource);
                return true;
            }
            finally
            {
                for (int i = 0; i < remapped.Count; i++) if (remapped[i] != null) HumanoidMeshResourceCleanup.Destroy(remapped[i]);
            }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent) segments.Add(current.name);
            if (segments.Count == 0 || target == null) return null;
            Transform probe = target;
            while (probe != null && probe != root) probe = probe.parent;
            if (probe != root) return null;
            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>Returns the renderer transform after the Outfit root has been identity-attached to the Figure root, matching OutfitAttacher's retained-root pose.</summary>
        internal static bool TryCreateAttachedSourceToOutput(Transform figureRoot, Transform figureRenderer, Transform sourceRoot, Transform sourceRenderer, out Matrix4x4 sourceToOutput, out StackMachineDiagnostic diagnostic)
        {
            sourceToOutput = Matrix4x4.identity;
            diagnostic = null;
            if (figureRoot == null) return Fail("FigureRootRequired", "Final Mesh build requires the Figure root Transform.", out diagnostic);
            if (figureRenderer == null) return Fail("FigureRendererRequired", "Final Mesh build requires the Figure output renderer Transform.", out diagnostic);
            if (sourceRoot == null) return Fail("SourceRootRequired", "Final Mesh build requires every source root Transform.", out diagnostic);
            if (sourceRenderer == null) return Fail("SourceRendererRequired", "Final Mesh build requires every source renderer Transform.", out diagnostic);

            // Outfit prefabs can retain arbitrary authoring-placement offsets. OutfitAttacher
            // removes that placement by identity-parenting the retained Outfit root under the
            // Figure root. Rebuild that pose mathematically without using its transaction route.
            sourceToOutput = figureRenderer.worldToLocalMatrix
                * figureRoot.localToWorldMatrix
                * sourceRoot.worldToLocalMatrix
                * sourceRenderer.localToWorldMatrix;
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", code, message);
            return false;
        }
    }
}
