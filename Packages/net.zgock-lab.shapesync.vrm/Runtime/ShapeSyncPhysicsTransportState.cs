// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Tracks SpringBone components owned by one debug metadata transport run.
    /// This must remain a dedicated MonoBehaviour script so generated Prefabs
    /// retain a resolvable script GUID when metadata output is enabled.
    /// </summary>
    public sealed class ShapeSyncPhysicsTransportState : MonoBehaviour
    {
        [SerializeField] public string sourceRootName;
        [SerializeField] public string runId;
        [SerializeField] public List<ShapeSyncPhysicsTransportEntry> entries = new List<ShapeSyncPhysicsTransportEntry>();
        [NonSerialized] public readonly List<Vrm10InstanceSpringBone.Spring> ownedSprings = new List<Vrm10InstanceSpringBone.Spring>();
        [NonSerialized] public readonly List<VRM10SpringBoneColliderGroup> ownedColliderGroups = new List<VRM10SpringBoneColliderGroup>();
    }
}
#endif
