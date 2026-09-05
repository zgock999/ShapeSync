// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Transactional runtime binding between an Outfit PCM payload and a Figure renderer or DynamicMorphAdapter.
    /// Use the factory methods and commit/rollback lifecycle instead of constructing bindings directly.
    /// </summary>
    public sealed class ProfileControlledMorphBinding
    {
        private readonly SkinnedMeshRenderer renderer;
        private readonly int baseIndex;
        private readonly string[] fbmNames;
        private readonly int[] differenceIndices;
        private readonly DynamicMorphAdapter adapter;
        private readonly int registrationId;
        private readonly bool plugable;
        private readonly ProfileControlledMorphAsset payloadAsset;
        private DynamicMorphAdapter.PreparedPcmAttach preparedAttach;
        private bool committed;

        public string BaseBlendShapeName { get; }

        private ProfileControlledMorphBinding(SkinnedMeshRenderer renderer, string baseBlendShapeName, int baseIndex, string[] fbmNames, int[] differenceIndices)
        {
            this.renderer = renderer;
            BaseBlendShapeName = baseBlendShapeName;
            this.baseIndex = baseIndex;
            this.fbmNames = fbmNames;
            this.differenceIndices = differenceIndices;
        }

        private ProfileControlledMorphBinding(DynamicMorphAdapter adapter, int registrationId, ProfileControlledMorphAsset asset, DynamicMorphAdapter.PreparedPcmAttach preparedAttach)
        {
            this.adapter = adapter; this.registrationId = registrationId; payloadAsset = asset; fbmNames = System.Array.Empty<string>(); differenceIndices = System.Array.Empty<int>(); plugable = true;
            this.preparedAttach = preparedAttach;
        }

        public static bool TryCreate(DynamicMorphAdapter adapter, ProfileControlledMorphAsset asset, int registrationId, out ProfileControlledMorphBinding binding, out string error)
        {
            binding = null; error = null;
            if (adapter == null || asset == null) { error = "Plugable PCM requires a Dynamic Morph Adapter and payload asset."; return false; }
            if (!adapter.TryPreparePcmAttach(asset, registrationId, out DynamicMorphAdapter.PreparedPcmAttach prepared, out error)) return false;
            binding = new ProfileControlledMorphBinding(adapter, registrationId, asset, prepared); return true;
        }

        public static bool TryCreate(SkinnedMeshRenderer renderer, string outfitName, IReadOnlyList<DynamicBoneBlendTarget> targets, out ProfileControlledMorphBinding binding, out string error)
        {
            binding = null;
            error = null;
            if (renderer == null || renderer.sharedMesh == null) { error = "PCM requires the Figure Target SkinnedMeshRenderer and its Mesh."; return false; }
            if (string.IsNullOrWhiteSpace(outfitName)) { error = "PCM Outfit Name is required when Profile Controlled Morph is enabled."; return false; }
            Mesh mesh = renderer.sharedMesh;
            string baseName = BlendShapeReservedPrefixes.Pcm + outfitName;
            int baseIndex = mesh.GetBlendShapeIndex(baseName);
            if (baseIndex < 0) { error = $"Figure Mesh is missing required Base PCM '{baseName}'."; return false; }
            int count = targets != null ? targets.Count : 0;
            string[] names = new string[count];
            int[] indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                string fbm = targets[i] != null ? targets[i].blendName : null;
                names[i] = fbm;
                indices[i] = string.IsNullOrEmpty(fbm) ? -1 : mesh.GetBlendShapeIndex(BlendShapeReservedPrefixes.Pcm + fbm + "_" + outfitName);
            }
            binding = new ProfileControlledMorphBinding(renderer, baseName, baseIndex, names, indices);
            return true;
        }

        public bool Commit(out string error)
        {
            error = null;
            if (!plugable || committed) return true;
            if (adapter == null || !adapter.CommitPreparedPcmAttach(preparedAttach, out error)) return false;
            preparedAttach = null;
            committed = true;
            return true;
        }

        public void ApplyBase() { if (plugable && committed) adapter?.ApplyPcmBase(registrationId); else if (!plugable && renderer != null) renderer.SetBlendShapeWeight(baseIndex, 100f); }
        public void ApplyFbmWeight(FbmWeightChange change)
        {
            if (string.IsNullOrEmpty(change.BlendName)) return;
            if (plugable)
            {
                if (!committed) return;
                IReadOnlyList<string> names = payloadAsset != null ? payloadAsset.FbmBlendNames : System.Array.Empty<string>();
                for (int i = 0; i < names.Count; i++) if (names[i] == change.BlendName) { adapter?.ApplyPcmFbmWeight(registrationId, i, change.Enabled && IsFinite(change.Weight) ? change.Weight : 0f); return; }
                return;
            }
            for (int i = 0; i < fbmNames.Length; i++) if (fbmNames[i] == change.BlendName && differenceIndices[i] >= 0)
            {
                renderer.SetBlendShapeWeight(differenceIndices[i], (change.Enabled && IsFinite(change.Weight) ? change.Weight : 0f) * 100f);
                return;
            }
        }
        public void Dispose()
        {
            if (plugable)
            {
                if (committed) adapter?.ReleasePcmAttachment(registrationId);
                else adapter?.RollbackPreparedPcmAttach(preparedAttach);
                preparedAttach = null;
                return;
            }
            if (renderer == null) return;
            renderer.SetBlendShapeWeight(baseIndex, 0f);
            for (int i = 0; i < differenceIndices.Length; i++) if (differenceIndices[i] >= 0) renderer.SetBlendShapeWeight(differenceIndices[i], 0f);
        }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
