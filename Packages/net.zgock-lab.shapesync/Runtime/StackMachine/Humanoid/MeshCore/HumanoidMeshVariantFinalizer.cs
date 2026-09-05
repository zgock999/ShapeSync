// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Replaces compiler-only Figure BlendShapes with resolved PBM and VRM expected shapes.</summary>
    public static class HumanoidMeshVariantFinalizer
    {
        public static bool TryFinalize(HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (bake == null || bake.Sources.Count == 0) return Fail("VariantFinalizationInputRequired", "Variant finalization requires FBM-baked Mesh escrow.", out diagnostic);
            for (int i = 0; i < bake.Sources.Count; i++) if (!TryFinalizeSource(bake, bake.Sources[i], out diagnostic)) return false;
            return true;
        }

        private static bool TryFinalizeSource(HumanoidMeshFbmBakeResult bake, HumanoidMeshFbmBakedSource baked, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Mesh finalBase = baked.Mesh;
            Mesh source = baked.Source.Renderer == null ? null : baked.Source.Renderer.sharedMesh;
            if (finalBase == null || source == null) return Fail("VariantSourceMeshRequired", "Variant finalization requires source and candidate Meshes.", out diagnostic);

            var pbmNames = new List<string>();
            var expressionNames = new List<string>();
            for (int i = 0; i < source.blendShapeCount; i++)
            {
                string name = source.GetBlendShapeName(i);
                if (name.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                {
                    string candidate = name.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                    if (!IsPbmDifference(candidate, source, bake.LogicalPlan)) pbmNames.Add(candidate);
                }
                else if (name.StartsWith(BlendShapeReservedPrefixes.Vrm, StringComparison.Ordinal))
                {
                    string candidate = name.Substring(BlendShapeReservedPrefixes.Vrm.Length);
                    if (!string.IsNullOrWhiteSpace(candidate)) expressionNames.Add(candidate);
                }
            }

            if (!TryKeepOnlyRawBlendShapes(finalBase, bake.LogicalPlan, out diagnostic)) return false;
            for (int i = 0; i < pbmNames.Count; i++)
            {
                if (HumanoidMeshPbmBoneChangeClassifier.HasBoneChange(bake.LogicalPlan, pbmNames[i])) continue;
                if (!HumanoidMeshPbmVariantBaker.TryBakeVariant(bake, baked.Source, pbmNames[i], out Mesh variant, out diagnostic)) return false;
                try
                {
                    if (!HumanoidMeshPbmVariantBaker.TryRegisterExpectedShape(finalBase, variant, BlendShapeReservedPrefixes.Pbm + pbmNames[i], out diagnostic)) return false;
                }
                finally { HumanoidMeshResourceCleanup.Destroy(variant); }
            }
            for (int i = 0; i < expressionNames.Count; i++)
            {
                if (!HumanoidMeshExpressionVariantBaker.TryBakeAndRegister(bake, baked.Source, finalBase, expressionNames[i], out diagnostic)) return false;
            }
            return true;
        }

        private static bool IsPbmDifference(string candidate, Mesh source, HumanoidMeshLogicalPlan plan)
        {
            for (int i = 0; i < source.blendShapeCount; i++)
            {
                string fbmName = source.GetBlendShapeName(i);
                if (!HumanoidMeshFbmBlendShapeClassifier.IsFbmShape(plan, fbmName)
                    && !fbmName.StartsWith(BlendShapeReservedPrefixes.Fbm, StringComparison.Ordinal)) continue;
                if (candidate.StartsWith(fbmName + "_", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool TryKeepOnlyRawBlendShapes(Mesh mesh, HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var frames = new List<BlendShapeFrame>();
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                string name = mesh.GetBlendShapeName(shape);
                if (IsCompilerOnly(name, plan)) continue;
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                    frames.Add(new BlendShapeFrame(name, mesh.GetBlendShapeFrameWeight(shape, frame), (Vector3[])vertices.Clone(), (Vector3[])normals.Clone(), (Vector3[])tangents.Clone()));
                }
            }
            mesh.ClearBlendShapes();
            for (int i = 0; i < frames.Count; i++) mesh.AddBlendShapeFrame(frames[i].Name, frames[i].Weight, frames[i].Vertices, frames[i].Normals, frames[i].Tangents);
            return true;
        }

        private static bool IsCompilerOnly(string name, HumanoidMeshLogicalPlan plan)
        {
            return HumanoidMeshFbmBlendShapeClassifier.IsFbmShape(plan, name)
                || name.StartsWith(BlendShapeReservedPrefixes.Fbm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Pcm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Mcm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Vrm, StringComparison.Ordinal);
        }

        private readonly struct BlendShapeFrame
        {
            public BlendShapeFrame(string name, float weight, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
            {
                Name = name; Weight = weight; Vertices = vertices; Normals = normals; Tangents = tangents;
            }
            public string Name { get; }
            public float Weight { get; }
            public Vector3[] Vertices { get; }
            public Vector3[] Normals { get; }
            public Vector3[] Tangents { get; }
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message);
            return false;
        }
    }
}
