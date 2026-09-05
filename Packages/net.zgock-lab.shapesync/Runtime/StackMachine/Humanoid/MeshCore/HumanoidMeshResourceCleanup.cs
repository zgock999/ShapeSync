// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Releases compiler-owned temporary UnityEngine objects without introducing an Editor assembly dependency.</summary>
    internal static class HumanoidMeshResourceCleanup
    {
        internal static void Destroy(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
