// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>One compiler-owned, FBM-baked temporary Mesh source.</summary>
    public readonly struct HumanoidMeshFbmBakedSource
    {
        public HumanoidMeshFbmBakedSource(HumanoidMeshSource source, Mesh mesh)
        {
            Source = source;
            Mesh = mesh;
        }

        public HumanoidMeshSource Source { get; }
        public Mesh Mesh { get; }
    }

    /// <summary>Temporary Mesh escrow produced by the FBM bake phase.</summary>
    public sealed class HumanoidMeshFbmBakeResult : IDisposable
    {
        private readonly HumanoidMeshFbmBakedSource[] sources;
        private bool disposed;

        public HumanoidMeshFbmBakeResult(HumanoidMeshLogicalPlan logicalPlan, HumanoidMeshFbmBakedSource[] sources, IReadOnlyDictionary<string, float> fbmWeights)
        {
            LogicalPlan = logicalPlan;
            this.sources = sources;
            FbmWeights = new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(fbmWeights, StringComparer.Ordinal));
        }

        /// <summary>Gets the immutable logical plan that produced this geometry escrow.</summary>
        public HumanoidMeshLogicalPlan LogicalPlan { get; }
        public IReadOnlyList<HumanoidMeshFbmBakedSource> Sources => Array.AsReadOnly(sources);
        /// <summary>Gets the immutable resolved FBM_SET weights retained for skeleton and Avatar finalization.</summary>
        public IReadOnlyDictionary<string, float> FbmWeights { get; }
        public IReadOnlyList<HumanoidMeshBcpDelta> BcpDeltas { get; private set; } = Array.Empty<HumanoidMeshBcpDelta>();
        /// <summary>Gets the compiler-owned final humanoid hierarchy and rebuilt Avatar for this candidate.</summary>
        public HumanoidMeshSkeletonEscrow Skeleton { get; private set; }
        /// <summary>Gets the Figure-relative base bone table used by later Outfit remap and Mesh merge.</summary>
        public HumanoidMeshBoneTable BoneTable { get; private set; }
        /// <summary>Gets the accumulated detached Outfit Extra Bone mappings used by later Mesh remap.</summary>
        public IReadOnlyDictionary<Transform, Transform> ExtraBoneTransforms { get; private set; } = new ReadOnlyDictionary<Transform, Transform>(new Dictionary<Transform, Transform>());
        /// <summary>Gets the one compiler-owned final Mesh after skinning remap and submesh / BlendShape merge.</summary>
        public Mesh FinalMesh { get; private set; }
        /// <summary>Gets the first final submesh index for Figure then each ATTACH Outfit candidate.</summary>
        public IReadOnlyList<int> FirstSubmeshBySource { get; private set; } = Array.Empty<int>();
        public IReadOnlyList<HumanoidMeshMaterialSlot> MaterialSlots { get; private set; } = Array.Empty<HumanoidMeshMaterialSlot>();
        /// <summary>Gets the recipe-resolved source Normal registry retained for Compiler material application.</summary>
        public IReadOnlyList<HumanoidMeshNormalTextureRegistration> NormalTextureRegistrations => LogicalPlan.NormalTextureRegistrations;

        public void SetBcpDeltas(IReadOnlyList<HumanoidMeshBcpDelta> value) => BcpDeltas = value ?? Array.Empty<HumanoidMeshBcpDelta>();
        public void SetSkeleton(HumanoidMeshSkeletonEscrow value) => Skeleton = value;
        public void SetBoneTable(HumanoidMeshBoneTable value) => BoneTable = value;
        public void SetExtraBoneTransforms(IReadOnlyDictionary<Transform, Transform> value) => ExtraBoneTransforms = value ?? new ReadOnlyDictionary<Transform, Transform>(new Dictionary<Transform, Transform>());
        public void SetFinalMesh(Mesh value, int[] firstSubmeshBySource)
        {
            FinalMesh = value;
            FirstSubmeshBySource = firstSubmeshBySource == null ? Array.Empty<int>() : Array.AsReadOnly(firstSubmeshBySource);
        }
        /// <summary>Transfers the final Mesh once to the upper compiler carrier without disposing it with this escrow.</summary>
        public Mesh DetachFinalMesh()
        {
            Mesh value = FinalMesh;
            FinalMesh = null;
            return value;
        }
        /// <summary>Transfers the rebuilt Avatar once to the upper compiler carrier.</summary>
        public Avatar DetachAvatar() => Skeleton?.DetachAvatar();
        public void SetMaterialSlots(HumanoidMeshMaterialSlot[] materialSlots) => MaterialSlots = materialSlots == null ? Array.Empty<HumanoidMeshMaterialSlot>() : Array.AsReadOnly(materialSlots);

        public void Dispose()
        {
            if (disposed) return;
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].Mesh != null) HumanoidMeshResourceCleanup.Destroy(sources[i].Mesh);
            }
            Skeleton?.Dispose();
            Skeleton = null;
            BoneTable = null;
            ExtraBoneTransforms = new ReadOnlyDictionary<Transform, Transform>(new Dictionary<Transform, Transform>());
            if (FinalMesh != null) HumanoidMeshResourceCleanup.Destroy(FinalMesh);
            FinalMesh = null;
            FirstSubmeshBySource = Array.Empty<int>();
            MaterialSlots = Array.Empty<HumanoidMeshMaterialSlot>();
            disposed = true;
        }
    }

    /// <summary>
    /// Bakes the compiler's FBM_SET snapshot into detached Mesh copies and removes the consumed FBM frames.
    /// Runtime DynamicBoneBlender, OutfitAttacher and renderer state are intentionally never touched.
    /// </summary>
    public static class HumanoidMeshFbmBaker
    {
        public static bool TryBake(HumanoidMeshLogicalPlan plan, out HumanoidMeshFbmBakeResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (plan == null) return Fail("LogicalPlanRequired", "EditMode FBM bake requires a completed logical Mesh plan.", out diagnostic);
            if (!TryCollectWeights(plan.CorePlan.Operations, out Dictionary<string, float> weights, out diagnostic)) return false;

            var baked = new List<HumanoidMeshFbmBakedSource>(1 + plan.AttachedOutfits.Count);
            if (!TryBakeSource(plan.Figure, weights, baked, out diagnostic))
            {
                DestroyMeshes(baked);
                return false;
            }
            for (int i = 0; i < plan.AttachedOutfits.Count; i++)
            {
                // Runtime keeps the recipe's FBM BlendShape weight on every attached Outfit
                // renderer while OutfitSkinnedMeshBinding independently resolves the matching
                // bindpose profile.  The compiler must bake both contributions before it drops
                // ShapeSync runtime components from the Pure Humanoid.
                if (TryBakeSource(plan.AttachedOutfits[i], weights, baked, out diagnostic)) continue;
                DestroyMeshes(baked);
                return false;
            }

            result = new HumanoidMeshFbmBakeResult(plan, baked.ToArray(), weights);
            return true;
        }

        private static bool TryCollectWeights(IReadOnlyList<MeshCoreOperation> operations, out Dictionary<string, float> weights, out StackMachineDiagnostic diagnostic)
        {
            weights = new Dictionary<string, float>(StringComparer.Ordinal);
            diagnostic = null;
            for (int i = 0; i < operations.Count; i++)
            {
                MeshCoreOperation operation = operations[i];
                if (operation.Kind != MeshCoreOperationKind.SetMorph) continue;
                if (string.IsNullOrWhiteSpace(operation.TargetName) || !float.IsFinite(operation.Weight))
                    return Fail("FbmOperationInvalid", "FBM_SET must lower to one finite target name and weight.", out diagnostic, operation.LogicalName);
                weights.Add(operation.TargetName, operation.Weight);
            }
            return true;
        }

        private static bool TryBakeSource(HumanoidMeshSource source, IReadOnlyDictionary<string, float> weights, List<HumanoidMeshFbmBakedSource> baked, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Mesh sourceMesh = source.Renderer == null ? null : source.Renderer.sharedMesh;
            if (sourceMesh == null) return Fail("SourceMeshRequired", "EditMode FBM bake requires every Figure and ATTACH Outfit renderer to own a Mesh.", out diagnostic, source.LogicalName ?? "figure");

            Mesh copy = null;
            try
            {
                Vector3[] vertices = sourceMesh.vertices;
                Vector3[] normals = sourceMesh.normals;
                Vector4[] tangents = sourceMesh.tangents;
                bool hasNormals = normals.Length == vertices.Length;
                bool hasTangents = tangents.Length == vertices.Length;
                copy = ShapeSyncMeshCloneUtility.Clone(sourceMesh, copyBlendShapes: false);
                copy.name = sourceMesh.name + " (Spec17 FBM Baked)";
                copy.ClearBlendShapes();

                foreach (KeyValuePair<string, float> weight in weights)
                {
                    int shapeIndex = sourceMesh.GetBlendShapeIndex(weight.Key);
                    if (shapeIndex < 0) continue;
                    if (!HumanoidMeshBlendShapeUtility.TryGetDeltaAtUnityWeight(sourceMesh, shapeIndex, weight.Value * 100f, out Vector3[] deltaVertices, out Vector3[] deltaNormals, out Vector3[] deltaTangents))
                    {
                        HumanoidMeshResourceCleanup.Destroy(copy);
                        copy = null;
                        return Fail("FbmBlendShapeUnreadable", "FBM BlendShape must expose a readable non-zero final frame.", out diagnostic, source.LogicalName ?? "figure", weight.Key);
                    }
                    Add(vertices, deltaVertices);
                    if (hasNormals) Add(normals, deltaNormals);
                    if (hasTangents) AddTangents(tangents, deltaTangents);
                }

                copy.vertices = vertices;
                if (hasNormals) { Normalize(normals); copy.normals = normals; }
                if (hasTangents) { NormalizeTangents(tangents); copy.tangents = tangents; }
                CopyRemainingBlendShapes(sourceMesh, copy, weights);
                copy.RecalculateBounds();
                baked.Add(new HumanoidMeshFbmBakedSource(source, copy));
                return true;
            }
            catch (Exception exception)
            {
                if (copy != null) HumanoidMeshResourceCleanup.Destroy(copy);
                return Fail("FbmMeshReadFailed", "EditMode FBM bake could not read or rebuild the source Mesh.", out diagnostic, source.LogicalName ?? "figure", exception.Message);
            }
        }

        private static void CopyRemainingBlendShapes(Mesh source, Mesh destination, IReadOnlyDictionary<string, float> consumed)
        {
            var vertices = new Vector3[source.vertexCount];
            var normals = new Vector3[source.vertexCount];
            var tangents = new Vector3[source.vertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string name = source.GetBlendShapeName(shape);
                if (consumed.ContainsKey(name)) continue;
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                    destination.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(shape, frame), vertices, normals, tangents);
                }
            }
        }

        private static void Add(Vector3[] destination, Vector3[] delta)
        {
            for (int i = 0; i < destination.Length; i++) destination[i] += delta[i];
        }

        private static void AddTangents(Vector4[] destination, Vector3[] delta)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                Vector4 tangent = destination[i];
                Vector3 xyz = new Vector3(tangent.x, tangent.y, tangent.z) + delta[i];
                destination[i] = new Vector4(xyz.x, xyz.y, xyz.z, tangent.w);
            }
        }

        private static void Normalize(Vector3[] values)
        {
            for (int i = 0; i < values.Length; i++) if (values[i].sqrMagnitude > Mathf.Epsilon) values[i].Normalize();
        }

        private static void NormalizeTangents(Vector4[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                Vector4 tangent = values[i];
                Vector3 xyz = new Vector3(tangent.x, tangent.y, tangent.z);
                if (xyz.sqrMagnitude > Mathf.Epsilon) xyz.Normalize();
                values[i] = new Vector4(xyz.x, xyz.y, xyz.z, tangent.w);
            }
        }

        private static void DestroyMeshes(IReadOnlyList<HumanoidMeshFbmBakedSource> sources)
        {
            for (int i = 0; i < sources.Count; i++) if (sources[i].Mesh != null) HumanoidMeshResourceCleanup.Destroy(sources[i].Mesh);
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, bindingName: binding, detail: detail);
            return false;
        }
    }
}
