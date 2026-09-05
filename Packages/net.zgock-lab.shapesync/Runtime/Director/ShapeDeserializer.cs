// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Component base for deserializers owned by the Figure hosting <see cref="ShapeDirector"/>.</summary>
    public abstract class ShapeDeserializer : MonoBehaviour, IShapeDeserializer
    {
        /// <inheritdoc cref="IShapeDeserializer.TryDeserialize"/>
        public abstract bool TryDeserialize(string fileName, out List<ShapeSyncShape> runtimeShapes);
    }
}
