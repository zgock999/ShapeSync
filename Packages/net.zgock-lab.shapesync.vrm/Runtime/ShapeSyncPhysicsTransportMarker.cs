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
    /// Serialized marker placed on VRM physics transport output so the companion can identify its owned objects.
    /// </summary>
    public sealed class ShapeSyncPhysicsTransportMarker : MonoBehaviour
    {
        [SerializeField] public string sourcePath;
        [SerializeField] public string sourceType;
        [SerializeField] public int sourceComponentOrdinal;
        [SerializeField] public string runId;
    }

    /// <summary>
    /// One transported physics entry, retaining the source and destination transform relationship.
    /// </summary>
    [Serializable]
    public sealed class ShapeSyncPhysicsTransportEntry
    {
        public string sourceKey;
        public int springIndex;
        public int colliderGroupIndex;
    }

}
#endif
