// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Shared Material texture source binding referenced by one or more ShapeSync documents.</summary>
    /// <remarks>The reserved output <c>$out</c> is implicit and is deliberately not serialized here.</remarks>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Material Binding")]
    public sealed class MaterialBinding : ScriptableObject
    {
        [SerializeField] private List<MaterialTextureBindingEntry> textures = new List<MaterialTextureBindingEntry>();
        /// <summary>Gets the logical source textures. This list never contains the reserved <c>$out</c> binding.</summary>
        public IReadOnlyList<MaterialTextureBindingEntry> Textures => textures;
    }
}
