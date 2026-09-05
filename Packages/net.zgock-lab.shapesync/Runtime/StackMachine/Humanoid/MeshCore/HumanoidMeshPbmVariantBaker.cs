// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Builds one PCM-adjusted PBM absolute target from read-only Figure authoring data.</summary>
    public static class HumanoidMeshPbmVariantBaker
    {
        public static bool TryCreateSourceVariant(HumanoidMeshFbmBakeResult bake, string frameName, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            return TryCreateSourceVariant(bake, new[] { frameName }, out variant, out diagnostic);
        }
        public static bool TryCreateSourceVariant(HumanoidMeshFbmBakeResult bake, string[] frameNames, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            var weights = new float[frameNames == null ? 0 : frameNames.Length];
            for (int i = 0; i < weights.Length; i++) weights[i] = 1f;
            return TryCreateSourceVariant(bake, frameNames, weights, out variant, out diagnostic);
        }

        public static bool TryCreateSourceVariant(HumanoidMeshFbmBakeResult bake, string[] frameNames, float[] frameWeights, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            return TryCreateSourceVariant(bake, bake == null ? default : bake.LogicalPlan.Figure, frameNames, frameWeights, out variant, out diagnostic);
        }

        public static bool TryCreateSourceVariant(HumanoidMeshFbmBakeResult bake, HumanoidMeshSource owner, string[] frameNames, float[] frameWeights, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            variant = null; diagnostic = null;
            Mesh source = owner.Renderer == null ? null : owner.Renderer.sharedMesh;
            if (bake == null || source == null) return Fail("VariantSourceMeshRequired", "Variant requires an authoring Mesh.", out diagnostic);
            if (frameNames == null || frameWeights == null || frameNames.Length != frameWeights.Length)
                return Fail("VariantFramePlanInvalid", "Variant source frames and weights must have the same length.", out diagnostic);
            variant = ShapeSyncMeshCloneUtility.Clone(source);
            Vector3[] vertices = source.vertices;
            for (int frame = 0; frame < frameNames.Length; frame++)
            {
                int index = source.GetBlendShapeIndex(frameNames[frame]);
                if (!float.IsFinite(frameWeights[frame]) || index < 0 || !HumanoidMeshBlendShapeUtility.TryGetDeltaAtUnityWeight(source, index, frameWeights[frame] * 100f, out Vector3[] delta, out _, out _)) { HumanoidMeshResourceCleanup.Destroy(variant); variant = null; return Fail("VariantFrameMissing", "Variant source frame is missing or unreadable.", out diagnostic, detail: frameNames[frame]); }
                for (int i = 0; i < vertices.Length; i++) vertices[i] += delta[i];
            }
            variant.vertices = vertices;
            if (owner.Root == bake.LogicalPlan.Figure.Root && !HumanoidMeshPcmBaker.TryBakeInto(variant, bake.LogicalPlan.PcmSources, bake.FbmWeights, out diagnostic)) { HumanoidMeshResourceCleanup.Destroy(variant); variant = null; return false; }
            variant.ClearBlendShapes();
            variant.RecalculateBounds(); return true;
        }
        public static bool TryRegisterExpectedShape(Mesh finalBase, Mesh variant, string pbmName, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (finalBase == null || variant == null || string.IsNullOrWhiteSpace(pbmName)) return Fail("PbmExpectedShapeInvalid", "PBM expected-shape registration requires base, variant, and PBM name.", out diagnostic);
            if (finalBase.GetBlendShapeIndex(pbmName) >= 0) return Fail("PbmExpectedShapeDuplicate", "PBM expected-shape name is already registered.", out diagnostic, detail: pbmName);
            if (!HumanoidMeshBlendShapeUtility.TryBuildDifference(finalBase, variant, out Vector3[] v, out Vector3[] n, out Vector3[] t)) return Fail("PbmExpectedShapeTopologyMismatch", "PBM variant topology does not match final base Mesh.", out diagnostic, detail: pbmName);
            HumanoidMeshBlendShapeUtility.AddFrameOrThrow(finalBase, pbmName, v, n, t);
            return true;
        }
        public static bool TryBakeFigureVariant(HumanoidMeshFbmBakeResult bake, string pbmName, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            return TryBakeVariant(bake, bake == null ? default : bake.LogicalPlan.Figure, pbmName, out variant, out diagnostic);
        }

        public static bool TryBakeVariant(HumanoidMeshFbmBakeResult bake, HumanoidMeshSource owner, string pbmName, out Mesh variant, out StackMachineDiagnostic diagnostic)
        {
            variant = null; diagnostic = null;
            if (bake == null || string.IsNullOrWhiteSpace(pbmName)) return Fail("PbmExpectedShapeInvalid", "PBM expected-shape build requires a PBM name.", out diagnostic);
            Mesh source = owner.Renderer == null ? null : owner.Renderer.sharedMesh;
            if (source == null) return Fail("VariantSourceMeshRequired", "PBM expected-shape build requires an authoring Mesh.", out diagnostic);
            var names = new System.Collections.Generic.List<string> { BlendShapeReservedPrefixes.Pbm + pbmName };
            var weights = new System.Collections.Generic.List<float> { 1f };
            foreach (var fbm in bake.FbmWeights)
            {
                if (source.GetBlendShapeIndex(fbm.Key) >= 0)
                {
                    names.Add(fbm.Key);
                    weights.Add(fbm.Value);
                }
                string difference = BlendShapeReservedPrefixes.Pbm + fbm.Key + "_" + pbmName;
                if (source.GetBlendShapeIndex(difference) < 0) continue;
                names.Add(difference);
                weights.Add(fbm.Value);
            }
            return TryCreateSourceVariant(bake, owner, names.ToArray(), weights.ToArray(), out variant, out diagnostic);
        }
        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, bindingName: binding, detail: detail); return false; }
    }
}
