// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Spring Bone graph for an Outfit that intentionally has no Vrm10Instance/Humanoid.
    /// OutfitAttacher consumes this data and attaches it to the Figure Vrm10Instance.
    /// </summary>
    public sealed class ShapeSyncOutfitSpringBoneData : MonoBehaviour
    {
        [SerializeField] public List<VRM10SpringBoneColliderGroup> ColliderGroups = new List<VRM10SpringBoneColliderGroup>();
        [SerializeField] public List<Vrm10InstanceSpringBone.Spring> Springs = new List<Vrm10InstanceSpringBone.Spring>();
        [SerializeField] public List<List<string>> SpringColliderGroupNames = new List<List<string>>();
    }
}
#endif
