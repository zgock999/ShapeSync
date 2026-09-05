// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec17
{
    /// <summary>Detached, source-derived expectations for a finalized Humanoid Mesh carrier.</summary>
    internal sealed class HumanoidMeshStructureFixture
    {
        private HumanoidMeshStructureFixture(int vertexCount, Transform[] bones, Matrix4x4[] bindposes, int materialSlotCount, Avatar avatar, string[] finalBlendShapeNames, string[] humanBoneNames)
        { VertexCount = vertexCount; Bones = bones; Bindposes = bindposes; MaterialSlotCount = materialSlotCount; Avatar = avatar; FinalBlendShapeNames = finalBlendShapeNames; HumanBoneNames = humanBoneNames; }

        internal int VertexCount { get; }
        internal Transform[] Bones { get; }
        internal Matrix4x4[] Bindposes { get; }
        internal int MaterialSlotCount { get; }
        internal Avatar Avatar { get; }
        internal string[] FinalBlendShapeNames { get; }
        internal string[] HumanBoneNames { get; }

        internal static bool TryCreate(HumanoidMeshFbmBakeResult result, out HumanoidMeshStructureFixture fixture)
        {
            fixture = null;
            if (result?.BoneTable?.Bones == null || result.BoneTable.Bindposes == null || result.Skeleton?.Avatar == null || result.MaterialSlots == null) return false;
            int vertices = 0; var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (HumanoidMeshFbmBakedSource source in result.Sources)
            {
                if (source.Mesh == null) return false;
                vertices += source.Mesh.vertexCount;
                for (int i = 0; i < source.Mesh.blendShapeCount; i++)
                {
                    string name = source.Mesh.GetBlendShapeName(i);
                    if (!name.StartsWith("FBM_", StringComparison.Ordinal) && !name.StartsWith("PCM_", StringComparison.Ordinal) && !name.StartsWith("MCM_", StringComparison.Ordinal) && !name.StartsWith("PBM_FBM_", StringComparison.Ordinal) && !name.StartsWith("Morph_Slot_", StringComparison.Ordinal)) names.Add(name);
                }
            }
            var humanNames = new List<string>(); foreach (HumanBone human in result.Skeleton.Avatar.humanDescription.human) humanNames.Add(human.humanName);
            fixture = new HumanoidMeshStructureFixture(vertices, (Transform[])result.BoneTable.Bones.Clone(), (Matrix4x4[])result.BoneTable.Bindposes.Clone(), result.MaterialSlots.Count, result.Skeleton.Avatar, new List<string>(names).ToArray(), humanNames.ToArray());
            return true;
        }
    }
}
