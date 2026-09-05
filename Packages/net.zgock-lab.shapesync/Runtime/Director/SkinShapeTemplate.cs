// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Authoring source for a runtime Skin shape.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Shapes/Skin Shape Template", fileName = "SkinShapeTemplate")]
    public sealed class SkinShapeTemplate : ShapeSyncShapeTemplate
    {
        [SerializeReference] private List<ShapeEntry> parts = new List<ShapeEntry>();

        /// <summary>Gets the ordered authoring part entries.</summary>
        public List<ShapeEntry> Parts => parts;

        /// <inheritdoc />
        public override ShapeSyncShape CreateRuntimeShape() => new SkinShape(ShapeId, Priority, Tags, parts);
    }
}
