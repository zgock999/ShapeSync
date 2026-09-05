// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Authoring source for a runtime Outfit shape.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Shapes/Outfit Shape Template", fileName = "OutfitShapeTemplate")]
    public sealed class OutfitShapeTemplate : ShapeSyncShapeTemplate
    {
        [SerializeReference] private List<ShapeEntry> parts = new List<ShapeEntry>();

        /// <summary>Gets the ordered authoring part entries.</summary>
        public List<ShapeEntry> Parts => parts;

        /// <inheritdoc />
        public override ShapeSyncShape CreateRuntimeShape() => new OutfitShape(ShapeId, Priority, Tags, parts);
    }
}
