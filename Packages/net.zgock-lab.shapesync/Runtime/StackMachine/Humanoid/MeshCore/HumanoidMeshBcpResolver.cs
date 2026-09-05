// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>One compiler-resolved BCP delta for the output-owned humanoid skeleton.</summary>
    public readonly struct HumanoidMeshBcpDelta
    {
        public HumanoidMeshBcpDelta(HumanBodyBones bone, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Bone = bone; Position = position; Rotation = rotation; Scale = scale;
        }
        public HumanBodyBones Bone { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
    }

    /// <summary>Resolves Outfit BCP base/FBM profiles without mutating a source Animator or Transform.</summary>
    public static class HumanoidMeshBcpResolver
    {
        public static bool TryResolve(HumanoidMeshFbmBakeResult bake, out IReadOnlyList<HumanoidMeshBcpDelta> deltas, out StackMachineDiagnostic diagnostic)
        {
            deltas = null;
            diagnostic = null;
            if (bake == null) return Fail("FbmBakeResultRequired", "BCP resolution requires the FBM-baked Mesh escrow.", out diagnostic);
            var result = new List<HumanoidMeshBcpDelta>();
            var claimed = new HashSet<HumanBodyBones>();
            foreach (HumanoidMeshSource source in bake.LogicalPlan.BcpSources)
            {
                ShapeSyncOutfit outfit = source.Outfit;
                if (outfit == null) return Fail("BcpOutfitRequired", "BCP source has no ShapeSyncOutfit.", out diagnostic, source.LogicalName);
                if (!TryResolveOutfit(outfit, bake.FbmWeights, claimed, result, out diagnostic)) return false;
            }
            deltas = result.AsReadOnly();
            return true;
        }

        private static bool TryResolveOutfit(ShapeSyncOutfit outfit, IReadOnlyDictionary<string, float> weights, HashSet<HumanBodyBones> claimed, List<HumanoidMeshBcpDelta> result, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var baseByBone = new Dictionary<HumanBodyBones, ShapeSyncHumanoidBoneCorrection>();
            if (!TryIndex(outfit.HumanoidBoneCorrectionProfile, baseByBone, outfit.RegistryId, out diagnostic)) return false;
            var targetProfiles = new List<KeyValuePair<float, Dictionary<HumanBodyBones, ShapeSyncHumanoidBoneCorrection>>>();
            foreach (var weight in weights)
            {
                if (!outfit.TryGetFbmHumanoidBoneCorrectionProfile(weight.Key, out ShapeSyncHumanoidBoneCorrectionProfile target)) continue;
                var indexed = new Dictionary<HumanBodyBones, ShapeSyncHumanoidBoneCorrection>();
                if (!TryIndex(target, indexed, outfit.RegistryId, out diagnostic)) return false;
                targetProfiles.Add(new KeyValuePair<float, Dictionary<HumanBodyBones, ShapeSyncHumanoidBoneCorrection>>(weight.Value, indexed));
                foreach (HumanBodyBones bone in indexed.Keys) if (!baseByBone.ContainsKey(bone)) baseByBone.Add(bone, Identity(bone));
            }
            foreach (var pair in baseByBone)
            {
                if (!claimed.Add(pair.Key)) return Fail("BcpBoneConflict", "BCP humanoid bone is owned by more than one ATTACH Outfit.", out diagnostic, outfit.RegistryId, pair.Key.ToString());
                ShapeSyncHumanoidBoneCorrection value = pair.Value;
                Vector3 position = value.localPositionDelta;
                Vector3 scale = value.localScaleDelta;
                Quaternion baseRotation = Normalize(value.localRotationDelta);
                Quaternion rotation = baseRotation;
                foreach (var targetProfile in targetProfiles)
                {
                    if (!targetProfile.Value.TryGetValue(pair.Key, out ShapeSyncHumanoidBoneCorrection targetValue)) continue;
                    position += (targetValue.localPositionDelta - value.localPositionDelta) * targetProfile.Key;
                    scale += (targetValue.localScaleDelta - value.localScaleDelta) * targetProfile.Key;
                    Quaternion targetDelta = Normalize(targetValue.localRotationDelta * Quaternion.Inverse(baseRotation));
                    rotation = Normalize(Quaternion.Slerp(Quaternion.identity, targetDelta, targetProfile.Key) * rotation);
                }
                result.Add(new HumanoidMeshBcpDelta(pair.Key, position, rotation, scale));
            }
            return true;
        }

        private static bool TryIndex(ShapeSyncHumanoidBoneCorrectionProfile profile, Dictionary<HumanBodyBones, ShapeSyncHumanoidBoneCorrection> result, string owner, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (profile == null) return true;
            foreach (ShapeSyncHumanoidBoneCorrection value in profile.Corrections)
            {
                if (!IsValid(value) || !result.TryAdd(value.bone, value)) return Fail("BcpProfileInvalid", "BCP profile contains an invalid or duplicate Humanoid bone correction.", out diagnostic, owner);
            }
            return true;
        }

        private static ShapeSyncHumanoidBoneCorrection Identity(HumanBodyBones bone) => new ShapeSyncHumanoidBoneCorrection { bone = bone, localRotationDelta = Quaternion.identity };

        private static bool IsValid(ShapeSyncHumanoidBoneCorrection value) => value != null && value.bone != HumanBodyBones.LastBone && float.IsFinite(value.localPositionDelta.x) && float.IsFinite(value.localPositionDelta.y) && float.IsFinite(value.localPositionDelta.z) && float.IsFinite(value.localScaleDelta.x) && float.IsFinite(value.localScaleDelta.y) && float.IsFinite(value.localScaleDelta.z) && float.IsFinite(value.localRotationDelta.x) && float.IsFinite(value.localRotationDelta.y) && float.IsFinite(value.localRotationDelta.z) && float.IsFinite(value.localRotationDelta.w) && Squared(value.localRotationDelta) > Mathf.Epsilon;
        private static Quaternion Normalize(Quaternion value) => Squared(value) > Mathf.Epsilon ? Quaternion.Normalize(value) : Quaternion.identity;
        private static float Squared(Quaternion value) => value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, bindingName: binding, detail: detail); return false; }
    }
}
