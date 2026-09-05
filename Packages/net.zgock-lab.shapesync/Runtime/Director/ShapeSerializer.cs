// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Component base for serializers owned by the Figure hosting <see cref="ShapeDirector"/>.</summary>
    public abstract class ShapeSerializer : MonoBehaviour, IShapeSerializer
    {
        /// <inheritdoc cref="IShapeSerializer.TrySerialize"/>
        public abstract bool TrySerialize(string fileName, List<ShapeSyncShape> runtimeShapes);
    }
}
