// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>ScriptableObject authoring source for one Shape Director runtime shape.</summary>
    public abstract class ShapeSyncShapeTemplate : ScriptableObject
    {
        [SerializeField] private string shapeId;
        [SerializeField] private int priority;
        [SerializeField] private List<string> tags = new List<string>();

        /// <summary>Gets or sets the author-defined Shape identity.</summary>
        public string ShapeId { get => shapeId; set => shapeId = value; }

        /// <summary>Gets or sets the physical composition priority.</summary>
        public int Priority { get => priority; set => priority = value; }

        /// <summary>Gets the mutable author-defined exclusion tag list.</summary>
        public List<string> Tags => tags;

        /// <summary>Creates an independent Director runtime shape from this Template.</summary>
        /// <returns>A detached logical Shape carrying this Template's current authoring values.</returns>
        public abstract ShapeSyncShape CreateRuntimeShape();
    }

}
