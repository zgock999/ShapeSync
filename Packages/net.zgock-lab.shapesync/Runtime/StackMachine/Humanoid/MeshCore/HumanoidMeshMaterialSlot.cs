// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>One final Mesh submesh's source semantic adapter identity.</summary>
    public readonly struct HumanoidMeshMaterialSlot
    {
        public HumanoidMeshMaterialSlot(MaterialId materialId, int newSubmeshIndex, MaterialShaderAdapter adapter)
        {
            MaterialId = materialId;
            NewSubmeshIndex = newSubmeshIndex;
            Adapter = adapter;
        }

        public MaterialId MaterialId { get; }
        public int NewSubmeshIndex { get; }
        public MaterialShaderAdapter Adapter { get; }
    }
}
