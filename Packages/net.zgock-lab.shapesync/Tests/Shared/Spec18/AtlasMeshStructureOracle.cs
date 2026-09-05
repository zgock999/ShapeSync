// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Checks Layer 2 UV scope and mesh structure without cloning or remapping a mesh.</summary>
    internal static class AtlasMeshStructureOracle
    {
        private static readonly Vector4 IdentityMainTexSt = new Vector4(1f, 1f, 0f, 0f);
        /// <summary>Detached renderer-level structure that remains stable while the Baker replaces candidate Materials.</summary>
        internal sealed class RendererState
        {
            private readonly IReadOnlyList<MaterialId> materialSlots;
            internal RendererState(IReadOnlyList<MaterialId> materialSlots, string rootBoneIdentity, string avatarIdentity)
            { this.materialSlots = new List<MaterialId>(materialSlots ?? Array.Empty<MaterialId>()).AsReadOnly(); RootBoneIdentity = rootBoneIdentity ?? string.Empty; AvatarIdentity = avatarIdentity ?? string.Empty; }
            internal IReadOnlyList<MaterialId> MaterialSlots => materialSlots;
            internal string RootBoneIdentity { get; }
            internal string AvatarIdentity { get; }
        }
        internal sealed class Context
        {
            internal Context(MaterialId materialId, int submesh, bool atlasTarget, AtlasLayoutCell cell, int pageExtent, Vector2 uvSetScale, Vector2 uvSetOffset, bool excluded = false, AtlasTextureSemantic semantic = (AtlasTextureSemantic)(-1))
            { MaterialId = materialId; Submesh = submesh; AtlasTarget = atlasTarget; Cell = cell; PageExtent = pageExtent; UvSetScale = uvSetScale; UvSetOffset = uvSetOffset; Excluded = excluded; Semantic = semantic; MaterialState = null; }
            internal Context(MaterialId materialId, int submesh, bool atlasTarget, AtlasLayoutCell cell, int pageExtent, Vector2 uvSetScale, Vector2 uvSetOffset, MaterialState materialState, bool excluded = false, AtlasTextureSemantic semantic = (AtlasTextureSemantic)(-1))
                : this(materialId, submesh, atlasTarget, cell, pageExtent, uvSetScale, uvSetOffset, excluded, semantic) { MaterialState = materialState; }
            internal MaterialId MaterialId { get; } internal int Submesh { get; } internal bool AtlasTarget { get; }
            internal AtlasLayoutCell Cell { get; } internal int PageExtent { get; } internal Vector2 UvSetScale { get; } internal Vector2 UvSetOffset { get; }
            internal bool Excluded { get; }
            internal AtlasTextureSemantic Semantic { get; }
            internal MaterialState MaterialState { get; }
        }

        /// <summary>Detached pre/post material texture-transform state for one material slot.</summary>
        internal sealed class MaterialState
        {
            internal MaterialState(Vector4 originalMainTexSt, Vector4 atlasMainTexSt) { OriginalMainTexSt = originalMainTexSt; AtlasMainTexSt = atlasMainTexSt; }
            internal Vector4 OriginalMainTexSt { get; }
            internal Vector4 AtlasMainTexSt { get; }
        }

        internal static bool TryValidate(Mesh original, Mesh atlas, IReadOnlyList<Context> contexts, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (original == null || atlas == null || contexts == null) return Fail("AtlasMeshStructureInputRequired", out diagnostic);
            if (original.vertexCount != atlas.vertexCount || original.subMeshCount != atlas.subMeshCount || original.blendShapeCount != atlas.blendShapeCount || original.bindposes.Length != atlas.bindposes.Length) return Fail("AtlasMeshStructureChanged", out diagnostic);
            for (int i = 0; i < original.bindposes.Length; i++) if (original.bindposes[i] != atlas.bindposes[i]) return Fail("AtlasMeshStructureBindposeChanged", out diagnostic);
            for (int i = 0; i < original.blendShapeCount; i++) if (!BlendShapeEquals(original, atlas, i)) return Fail("AtlasMeshStructureBlendShapeChanged", out diagnostic);
            BoneWeight[] beforeWeights = original.boneWeights; BoneWeight[] afterWeights = atlas.boneWeights;
            if (beforeWeights.Length != afterWeights.Length) return Fail("AtlasMeshStructureBoneWeightChanged", out diagnostic);
            for (int i = 0; i < beforeWeights.Length; i++) if (!beforeWeights[i].Equals(afterWeights[i])) return Fail("AtlasMeshStructureBoneWeightChanged", out diagnostic);
            var ownership = new Context[original.subMeshCount];
            foreach (Context context in contexts)
            {
                if (context == null || context.MaterialState == null || !context.MaterialId.IsValid || (context.Semantic != AtlasTextureSemantic.BaseColor && context.Semantic != AtlasTextureSemantic.Normal) || !float.IsFinite(context.UvSetScale.x) || !float.IsFinite(context.UvSetScale.y) || !float.IsFinite(context.UvSetOffset.x) || !float.IsFinite(context.UvSetOffset.y) || context.Submesh < 0 || context.Submesh >= ownership.Length || ownership[context.Submesh] != null || (context.AtlasTarget && (context.Cell == null || context.PageExtent <= 0 || !context.Cell.MaterialId.Equals(context.MaterialId)))) return Fail("AtlasMeshStructureContextInvalid", out diagnostic);
                ownership[context.Submesh] = context;
            }
            for (int submesh = 0; submesh < ownership.Length; submesh++)
            {
                if (ownership[submesh] == null) return Fail("AtlasMeshStructureContextMissing", out diagnostic);
                int[] before = original.GetIndices(submesh); int[] after = atlas.GetIndices(submesh);
                if (original.GetTopology(submesh) != atlas.GetTopology(submesh)) return Fail("AtlasMeshStructureTopologyChanged", out diagnostic);
                if (before.Length != after.Length) return Fail("AtlasMeshStructureIndicesChanged", out diagnostic);
                for (int i = 0; i < before.Length; i++) if (before[i] != after[i]) return Fail("AtlasMeshStructureIndicesChanged", out diagnostic);
            }
            var targetSubmeshes = new List<int>();
            for (int submesh = 0; submesh < ownership.Length; submesh++) if (ownership[submesh].AtlasTarget) targetSubmeshes.Add(submesh);
            if (targetSubmeshes.Count > 0 && !AtlasMeshValidator.TryValidate(original, targetSubmeshes, null, out diagnostic)) return false;
            foreach (Context context in contexts) if (context.AtlasTarget && !UvsetMatchesOriginalMainTexSt(context)) return Fail("AtlasMeshStructureUvsetMaterialStMismatch", out diagnostic);
            Vector2[] beforeUv = original.uv; Vector2[] afterUv = atlas.uv;
            if (beforeUv.Length != original.vertexCount || afterUv.Length != atlas.vertexCount) return Fail("AtlasMeshStructureUv0Required", out diagnostic);
            for (int submesh = 0; submesh < ownership.Length; submesh++) foreach (int vertex in original.GetIndices(submesh))
            {
                Context context = ownership[submesh]; Vector2 expected = context.AtlasTarget ? AtlasUvTransform.Apply(beforeUv[vertex], context.UvSetScale, context.UvSetOffset, context.Cell, context.PageExtent) : beforeUv[vertex];
                if (BitConverter.SingleToInt32Bits(expected.x) != BitConverter.SingleToInt32Bits(afterUv[vertex].x) || BitConverter.SingleToInt32Bits(expected.y) != BitConverter.SingleToInt32Bits(afterUv[vertex].y)) return Fail("AtlasMeshStructureUvScope", out diagnostic);
            }
            foreach (Context context in contexts) if (context.AtlasTarget ? !Vector4BitsEqual(context.MaterialState.AtlasMainTexSt, IdentityMainTexSt) : !Vector4BitsEqual(context.MaterialState.OriginalMainTexSt, context.MaterialState.AtlasMainTexSt)) return Fail("AtlasMeshStructureMaterialStScope", out diagnostic);
            return true;
        }
        internal static bool TryValidate(Mesh original, Mesh atlas, IReadOnlyList<Context> contexts, AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic)
        {
            if (!TryValidate(original, atlas, contexts, out diagnostic)) return false;
            if (layout == null) return Fail("AtlasMeshStructureLayoutRequired", out diagnostic);
            foreach (Context context in contexts) if (context.AtlasTarget)
            {
                if (context.PageExtent != layout.PageExtent || !layout.TryGetCell(context.MaterialId, out AtlasLayoutCell cell) || cell.PageIndex != context.Cell.PageIndex || cell.X != context.Cell.X || cell.Y != context.Cell.Y || cell.Width != context.Cell.Width || cell.Height != context.Cell.Height || cell.Gutter != context.Cell.Gutter) return Fail("AtlasMeshStructureLayoutContextMismatch", out diagnostic);
            }
            return true;
        }
        internal static bool TryValidate(Mesh original, Mesh atlas, IReadOnlyList<Context> contexts, RendererState originalState, RendererState atlasState, out StackMachineDiagnostic diagnostic)
        {
            if (!TryValidate(original, atlas, contexts, out diagnostic)) return false;
            if (originalState == null || atlasState == null || originalState.RootBoneIdentity != atlasState.RootBoneIdentity || originalState.AvatarIdentity != atlasState.AvatarIdentity || originalState.MaterialSlots.Count != atlasState.MaterialSlots.Count) return Fail("AtlasMeshStructureRendererStateChanged", out diagnostic);
            for (int i = 0; i < originalState.MaterialSlots.Count; i++) if (!originalState.MaterialSlots[i].Equals(atlasState.MaterialSlots[i])) return Fail("AtlasMeshStructureMaterialSlotsChanged", out diagnostic);
            foreach (Context context in contexts) if (context.Submesh >= originalState.MaterialSlots.Count || !originalState.MaterialSlots[context.Submesh].Equals(context.MaterialId)) return Fail("AtlasMeshStructureMaterialSlotsChanged", out diagnostic);
            return true;
        }
        /// <summary>Validates the complete Layer 2 acceptance contract; 18.4--18.5 use this entry, not the isolated test helpers.</summary>
        internal static bool TryValidateForAtlasAcceptance(Mesh original, Mesh atlas, IReadOnlyList<Context> contexts, AtlasLayoutResult layout, RendererState originalState, RendererState atlasState, out StackMachineDiagnostic diagnostic)
        {
            if (contexts != null) foreach (Context context in contexts) if (context != null && context.AtlasTarget == context.Excluded) return Fail("AtlasMeshStructureTargetExclusionMismatch", out diagnostic);
            if (!TryValidate(original, atlas, contexts, layout, out diagnostic)) return false;
            return TryValidateRendererState(contexts, originalState, atlasState, out diagnostic);
        }
        private static bool TryValidateRendererState(IReadOnlyList<Context> contexts, RendererState originalState, RendererState atlasState, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (originalState == null || atlasState == null || originalState.RootBoneIdentity != atlasState.RootBoneIdentity || originalState.AvatarIdentity != atlasState.AvatarIdentity || originalState.MaterialSlots.Count != atlasState.MaterialSlots.Count) return Fail("AtlasMeshStructureRendererStateChanged", out diagnostic);
            for (int i = 0; i < originalState.MaterialSlots.Count; i++) if (!originalState.MaterialSlots[i].Equals(atlasState.MaterialSlots[i])) return Fail("AtlasMeshStructureMaterialSlotsChanged", out diagnostic);
            foreach (Context context in contexts) if (context.Submesh >= originalState.MaterialSlots.Count || !originalState.MaterialSlots[context.Submesh].Equals(context.MaterialId)) return Fail("AtlasMeshStructureMaterialSlotsChanged", out diagnostic);
            return true;
        }
        private static bool Fail(string code, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, "Atlas Mesh structure Oracle rejected its input."); return false; }
        private static bool UvsetMatchesOriginalMainTexSt(Context context)
        {
            Vector4 st = context.MaterialState.OriginalMainTexSt;
            return BitConverter.SingleToInt32Bits(context.UvSetScale.x) == BitConverter.SingleToInt32Bits(st.x)
                && BitConverter.SingleToInt32Bits(context.UvSetScale.y) == BitConverter.SingleToInt32Bits(st.y)
                && BitConverter.SingleToInt32Bits(context.UvSetOffset.x) == BitConverter.SingleToInt32Bits(st.z)
                && BitConverter.SingleToInt32Bits(context.UvSetOffset.y) == BitConverter.SingleToInt32Bits(st.w);
        }
        private static bool Vector4BitsEqual(Vector4 left, Vector4 right)
        {
            return BitConverter.SingleToInt32Bits(left.x) == BitConverter.SingleToInt32Bits(right.x)
                && BitConverter.SingleToInt32Bits(left.y) == BitConverter.SingleToInt32Bits(right.y)
                && BitConverter.SingleToInt32Bits(left.z) == BitConverter.SingleToInt32Bits(right.z)
                && BitConverter.SingleToInt32Bits(left.w) == BitConverter.SingleToInt32Bits(right.w);
        }
        private static bool BlendShapeEquals(Mesh left, Mesh right, int index)
        {
            if (left.GetBlendShapeName(index) != right.GetBlendShapeName(index) || left.GetBlendShapeFrameCount(index) != right.GetBlendShapeFrameCount(index)) return false;
            var lv = new Vector3[left.vertexCount]; var ln = new Vector3[left.vertexCount]; var lt = new Vector3[left.vertexCount];
            var rv = new Vector3[right.vertexCount]; var rn = new Vector3[right.vertexCount]; var rt = new Vector3[right.vertexCount];
            for (int frame = 0; frame < left.GetBlendShapeFrameCount(index); frame++)
            {
                if (left.GetBlendShapeFrameWeight(index, frame) != right.GetBlendShapeFrameWeight(index, frame)) return false;
                left.GetBlendShapeFrameVertices(index, frame, lv, ln, lt); right.GetBlendShapeFrameVertices(index, frame, rv, rn, rt);
                for (int vertex = 0; vertex < lv.Length; vertex++) if (lv[vertex] != rv[vertex] || ln[vertex] != rn[vertex] || lt[vertex] != rt[vertex]) return false;
            }
            return true;
        }
    }
}
