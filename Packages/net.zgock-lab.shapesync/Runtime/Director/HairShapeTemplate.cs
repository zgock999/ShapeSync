// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Authoring source for a runtime Hair shape.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Shapes/Hair Shape Template", fileName = "HairShapeTemplate")]
    public sealed class HairShapeTemplate : ShapeSyncShapeTemplate
    {
        [SerializeReference] private List<ShapeEntry> parts = new List<ShapeEntry>();

        /// <summary>Gets the ordered authoring part entries.</summary>
        public List<ShapeEntry> Parts => parts;

        /// <inheritdoc />
        public override ShapeSyncShape CreateRuntimeShape() => new HairShape(ShapeId, Priority, Tags, parts);
    }
}
