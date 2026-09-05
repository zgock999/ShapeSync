// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Local Humanoid transform correction for one bone, expressed as position, rotation, and scale deltas.
    /// </summary>
    [Serializable]
    public sealed class ShapeSyncHumanoidBoneCorrection
    {
        public HumanBodyBones bone;
        public Vector3 localPositionDelta;
        public Quaternion localRotationDelta = Quaternion.identity;
        public Vector3 localScaleDelta;
    }

    /// <summary>
    /// Reusable authoring asset that describes the Humanoid pose correction required by an Outfit.
    /// Builders consume it when baking target meshes and Outfit bindposes.
    /// </summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Humanoid Bone Correction Profile", fileName = "HumanoidBoneCorrectionProfile")]
    public sealed class ShapeSyncHumanoidBoneCorrectionProfile : ScriptableObject
    {
        [SerializeField] private List<ShapeSyncHumanoidBoneCorrection> corrections = new List<ShapeSyncHumanoidBoneCorrection>();

        public IReadOnlyList<ShapeSyncHumanoidBoneCorrection> Corrections => corrections;

    #if UNITY_EDITOR
        public void SetCorrectionsForEditor(List<ShapeSyncHumanoidBoneCorrection> value)
        {
            corrections = value ?? new List<ShapeSyncHumanoidBoneCorrection>();
        }
    #endif
    }
}
