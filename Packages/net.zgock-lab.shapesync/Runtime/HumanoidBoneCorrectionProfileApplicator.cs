// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Applies a BC Profile to an Editor-owned humanoid rig for static authoring.</summary>
    public static class HumanoidBoneCorrectionProfileApplicator
    {
        public static bool TryApply(Animator animator, IReadOnlyList<ShapeSyncHumanoidBoneCorrection> corrections, out string error)
        {
            error = null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                error = "BCP bindpose bake requires a valid Humanoid Animator.";
                return false;
            }
            if (corrections == null)
            {
                error = "BCP bindpose bake corrections are missing.";
                return false;
            }

            for (int i = 0; i < corrections.Count; i++)
            {
                ShapeSyncHumanoidBoneCorrection correction = corrections[i];
                if (!TryValidate(correction, out error)) return false;
                if (animator.GetBoneTransform(correction.bone) == null)
                {
                    error = $"BCP bindpose bake bone '{correction.bone}' is not mapped by the Animator.";
                    return false;
                }
            }

            for (int i = 0; i < corrections.Count; i++)
            {
                ShapeSyncHumanoidBoneCorrection correction = corrections[i];
                Transform bone = animator.GetBoneTransform(correction.bone);
                bone.localPosition += correction.localPositionDelta;
                bone.localRotation = Normalize(correction.localRotationDelta) * bone.localRotation;
                bone.localScale += correction.localScaleDelta;
            }
            return true;
        }

        private static bool TryValidate(ShapeSyncHumanoidBoneCorrection correction, out string error)
        {
            error = null;
            if (correction == null)
            {
                error = "BCP bindpose bake contains a null correction.";
                return false;
            }
            if (!IsFinite(correction.localPositionDelta) || !IsFinite(correction.localScaleDelta)
                || !IsFinite(correction.localRotationDelta) || QuaternionLengthSquared(correction.localRotationDelta) <= Mathf.Epsilon)
            {
                error = $"BCP bindpose bake correction '{correction.bone}' is invalid.";
                return false;
            }
            return true;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(QuaternionLengthSquared(value));
            return magnitude > Mathf.Epsilon ? new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude) : Quaternion.identity;
        }

        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        private static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        private static float QuaternionLengthSquared(Quaternion value) => value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
