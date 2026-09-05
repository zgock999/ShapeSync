// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>
    /// Normalizes the destination Pure Humanoid Mesh after source merge.
    /// It clears every destination BlendShape before registering only final output
    /// shapes, so no legacy or compiler-temporary removal path can be missed.
    /// </summary>
    public static class HumanoidMeshFinalBlendShapeNormalizer
    {
        /// <summary>Clears all destination BlendShapes and restores only final raw, PBM, and VRM expression frames.</summary>
        public static bool TryNormalize(Mesh mesh, out StackMachineDiagnostic diagnostic)
            => TryNormalize(mesh, null, out diagnostic);

        /// <summary>
        /// Clears all destination BlendShapes and restores only final frames for the supplied logical plan.
        /// PBM frames which require Humanoid bone state, and all PBM difference frames used to
        /// construct a PBM variant, are compiler-temporary and are not representable by the
        /// Pure Humanoid Mesh output.
        /// </summary>
        public static bool TryNormalize(Mesh mesh, HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (mesh == null) return Fail("FinalMeshRequired", "Final Mesh BlendShape normalization requires a destination Mesh.", out diagnostic);

            var frames = new List<Frame>();
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                string name = mesh.GetBlendShapeName(shape);
                if (!IsFinalShape(name, plan)) continue;
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                    frames.Add(new Frame(name, mesh.GetBlendShapeFrameWeight(shape, frame), (Vector3[])vertices.Clone(), (Vector3[])normals.Clone(), (Vector3[])tangents.Clone()));
                }
            }

            mesh.ClearBlendShapes();
            for (int i = 0; i < frames.Count; i++)
            {
                Frame frame = frames[i];
                mesh.AddBlendShapeFrame(frame.Name, frame.Weight, frame.Vertices, frame.Normals, frame.Tangents);
            }
            return true;
        }

        private static bool IsFinalShape(string name, HumanoidMeshLogicalPlan plan)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (HumanoidMeshFbmBlendShapeClassifier.IsFbmShape(plan, name)) return false;
            if (name.StartsWith(BlendShapeReservedPrefixes.Fbm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Pcm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.Mcm, StringComparison.Ordinal)
                || name.StartsWith(BlendShapeReservedPrefixes.MorphSlot, StringComparison.Ordinal)) return false;
            if (!name.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal)) return true;

            string candidate = name.Substring(BlendShapeReservedPrefixes.Pbm.Length);
            if (HumanoidMeshPbmBoneChangeClassifier.HasBoneChange(plan, candidate)) return false;
            return !IsPbmDifferenceFrame(candidate, plan);
        }

        private static bool IsPbmDifferenceFrame(string candidate, HumanoidMeshLogicalPlan plan)
        {
            Mesh source = plan == null || plan.Figure.Renderer == null ? null : plan.Figure.Renderer.sharedMesh;
            if (source == null) return false;
            for (int i = 0; i < source.blendShapeCount; i++)
            {
                string sourceName = source.GetBlendShapeName(i);
                if (!sourceName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal)) continue;
                string directPbm = sourceName.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                if (!string.IsNullOrEmpty(directPbm) && candidate.EndsWith("_" + directPbm, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private readonly struct Frame
        {
            public Frame(string name, float weight, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
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

    /// <summary>Classifies FBM frames from the Figure's authoritative DynamicBoneBlender targets.</summary>
    internal static class HumanoidMeshFbmBlendShapeClassifier
    {
        internal static bool IsFbmShape(HumanoidMeshLogicalPlan plan, string name)
        {
            if (plan == null || string.IsNullOrEmpty(name)) return false;
            DynamicBoneBlender blender = plan.Figure.Root == null ? null : plan.Figure.Root.GetComponent<DynamicBoneBlender>();
            IReadOnlyList<DynamicBoneBlendTarget> targets = blender == null ? null : blender.Targets;
            for (int index = 0; targets != null && index < targets.Count; index++)
                if (targets[index] != null && string.Equals(targets[index].blendName, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
