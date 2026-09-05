// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Authoring-only root component for one ShapeSync Database Prefab.
    /// Generated runtime Figures must not retain this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeSyncDatabase : MonoBehaviour
    {
        [SerializeField] private ShapeSyncDatabaseRegistry registry;
        /// <summary>Fixed authoring registry owned by this Database Prefab.</summary>
        internal ShapeSyncDatabaseRegistry Registry => registry;
        internal void SetRegistryForAuthoring(ShapeSyncDatabaseRegistry value) => registry = value;
    }
}
