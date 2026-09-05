// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Non-blocking provenance data used to correlate Physics transfers.</summary>
    public sealed class ShapeSyncPhysicsRootMetadata : MonoBehaviour
    {
        [SerializeField] public string sourceVrmAssetGuid;
        [SerializeField] public string sourceRootName;
        [SerializeField] public string sourceRootPath;
    }
}
