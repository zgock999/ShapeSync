// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Bakes attached Outfit PCM base and FBM frames directly into the temporary Figure Mesh.</summary>
    public static class HumanoidMeshPcmBaker
    {
        public static bool TryBake(HumanoidMeshFbmBakeResult bake, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (bake == null || bake.Sources.Count == 0) return Fail("FbmBakeResultRequired", "PCM bake requires FBM-baked Mesh escrow.", out diagnostic);
            Mesh figureMesh = bake.Sources[0].Mesh;
            return TryBakeInto(figureMesh, bake.LogicalPlan.PcmSources, bake.FbmWeights, out diagnostic);
        }

        /// <summary>Applies the same attached-Outfit PCM contribution to any compiler-owned Figure variant Mesh.</summary>
        public static bool TryBakeInto(Mesh figureMesh, IReadOnlyList<HumanoidMeshSource> sources, IReadOnlyDictionary<string, float> weights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (figureMesh == null) return Fail("PcmTargetMeshRequired", "PCM bake requires a compiler-owned target Mesh.", out diagnostic);
            foreach (HumanoidMeshSource source in sources)
            {
                ShapeSyncOutfit outfit = source.Outfit;
                ProfileControlledMorphAsset payload = outfit == null ? null : outfit.ProfileControlledMorphAsset;
                // The enable flag gates both PCM formats. A configured payload is authoritative;
                // legacy frames are used only when the gate is enabled without a payload asset.
                bool legacy = outfit != null && outfit.ProfileControlledMorphEnabled && payload == null;
                if (!legacy && payload == null) continue;
                if (payload != null)
                {
                    if (!TryApplyPayload(figureMesh, payload, weights, out diagnostic)) return false;
                }
                else if (!TryApplyLegacy(figureMesh, outfit.ProfileControlledMorphOutfitName, weights, out diagnostic)) return false;
            }
            figureMesh.RecalculateBounds();
            return true;
        }

        private static bool TryApplyLegacy(Mesh mesh, string outfitName, IReadOnlyDictionary<string, float> weights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(outfitName)) return Fail("PcmLegacyNameRequired", "Legacy PCM requires an Outfit name.", out diagnostic);
            if (!TryApplyShape(mesh, BlendShapeReservedPrefixes.Pcm + outfitName, 1f, out diagnostic)) return false;
            foreach (var weight in weights) if (!TryApplyShape(mesh, BlendShapeReservedPrefixes.Pcm + weight.Key + "_" + outfitName, weight.Value, out diagnostic, allowMissing: true)) return false;
            return true;
        }

        private static bool TryApplyPayload(Mesh mesh, ProfileControlledMorphAsset payload, IReadOnlyDictionary<string, float> weights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Mesh source = payload.PayloadMesh;
            if (source == null || !source.isReadable || source.vertexCount != mesh.vertexCount) return Fail("PcmPayloadInvalid", "PCM payload Mesh is missing or incompatible with the Figure Mesh.", out diagnostic);
            if (!TryApplyShape(source, payload.BaseFrameName, 1f, mesh, out diagnostic)) return false;
            for (int i = 0; i < payload.FbmBlendNames.Count; i++)
            {
                if (!weights.TryGetValue(payload.FbmBlendNames[i], out float weight)) continue;
                if (i >= payload.FbmFrameNames.Count || !TryApplyShape(source, payload.FbmFrameNames[i], weight, mesh, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryApplyShape(Mesh source, string name, float weight, out StackMachineDiagnostic diagnostic, bool allowMissing = false) => TryApplyShape(source, name, weight, source, out diagnostic, allowMissing);
        private static bool TryApplyShape(Mesh source, string name, float weight, Mesh destination, out StackMachineDiagnostic diagnostic, bool allowMissing = false)
        {
            diagnostic = null;
            int index = source.GetBlendShapeIndex(name);
            if (index < 0) return allowMissing || Fail("PcmFrameMissing", "PCM required BlendShape frame is missing.", out diagnostic, detail: name);
            if (!HumanoidMeshBlendShapeUtility.TryGetDeltaAtUnityWeight(source, index, weight * 100f, out Vector3[] vertices, out _, out _)) return Fail("PcmFrameInvalid", "PCM BlendShape must expose a readable frame.", out diagnostic, detail: name);
            Vector3[] target = destination.vertices;
            for (int i = 0; i < target.Length; i++) target[i] += vertices[i];
            destination.vertices = target;
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, bindingName: binding, detail: detail); return false; }
    }
}
