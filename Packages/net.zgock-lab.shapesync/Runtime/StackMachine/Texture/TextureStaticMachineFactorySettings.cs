// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Project-owned explicit configuration for the Texture StackMachine Factory.</summary>
    /// <remarks>When resolved through <see cref="TextureStaticMachineFactory"/>, the required instance is loaded from the fixed <c>Assets/Resources/zgock/ShapeSync</c> path. Its Host Prefab may be supplied by the ShapeSync package.</remarks>
    public sealed class TextureStaticMachineFactorySettings : ScriptableObject
    {
        [SerializeField] private TextureStackMachineHost textureStackMachineHostPrefab;

        /// <summary>Gets the required Host Prefab instantiated when the active scene has no Texture StackMachine Host.</summary>
        public TextureStackMachineHost TextureStackMachineHostPrefab => textureStackMachineHostPrefab;
    }
}
