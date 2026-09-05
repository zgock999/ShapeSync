// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Serialized Figure expression entry that drives one BlendShape and optional MCM bindings.
    /// </summary>
    [System.Serializable]
    public class UniversalExpressionEntry
    {
        public string blendShapeName;
        public bool enabled = true;
        [Range(0f, 1f)] public float weight;

        [System.NonSerialized] public int blendShapeIndex = -1;
        [System.NonSerialized] public ExpressionMcmBinding[] mcmBindings = System.Array.Empty<ExpressionMcmBinding>();
        [System.NonSerialized] public float lastAppliedWeight = -1f;
        [System.NonSerialized] public string vrmExpressionName;
    }

    /// <summary>
    /// Runtime-resolved binding between one expression and one MCM BlendShape.
    /// </summary>
    public struct ExpressionMcmBinding
    {
        public string blendName;
        public int blendShapeIndex;
        public float lastAppliedWeight;
    }
}
