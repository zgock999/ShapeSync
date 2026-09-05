// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Authoring source for a runtime Morph shape.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Shapes/Morph Shape Template", fileName = "MorphShapeTemplate")]
    public sealed class MorphShapeTemplate : ShapeSyncShapeTemplate
    {
        [SerializeField] private List<MorphValue> morphs = new List<MorphValue>();

        /// <summary>Gets the ordered authoring morph values.</summary>
        public List<MorphValue> Morphs => morphs;

        /// <inheritdoc />
        public override ShapeSyncShape CreateRuntimeShape() => new MorphShape(ShapeId, Priority, Tags, morphs);
    }
}
