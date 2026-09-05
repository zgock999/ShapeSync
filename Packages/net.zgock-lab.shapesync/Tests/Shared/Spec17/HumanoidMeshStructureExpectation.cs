// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec17
{
    /// <summary>Mode-independent acceptance checks for a finalized Humanoid Mesh carrier.</summary>
    internal static class HumanoidMeshStructureExpectation
    {
        internal static bool TryValidate(Mesh finalMesh, SkinnedMeshRenderer renderer, Avatar avatar, int expectedVertexCount, int expectedMaterialSlotCount, IReadOnlyList<Transform> expectedBones, IReadOnlyList<Matrix4x4> expectedBindposes, IReadOnlyList<string> expectedBlendShapeNames, IReadOnlyList<string> requiredHumanBoneNames, out string failure)
        {
            failure = null;
            if (finalMesh == null || renderer == null || avatar == null || !avatar.isHuman || expectedVertexCount < 0 || expectedBones == null || expectedBindposes == null || expectedBlendShapeNames == null || requiredHumanBoneNames == null) return Fail("InputInvalid", out failure);
            if (finalMesh.vertexCount != expectedVertexCount || finalMesh.subMeshCount != expectedMaterialSlotCount || renderer.sharedMaterials.Length != expectedMaterialSlotCount)
                return Fail("MeshOrMaterialSlotMismatch", out failure);
            if (renderer.bones.Length != expectedBones.Count || finalMesh.bindposes.Length != expectedBindposes.Count || renderer.bones.Length != finalMesh.bindposes.Length) return Fail("BoneTableLengthMismatch", out failure);
            for (int i = 0; i < expectedBones.Count; i++) if (renderer.bones[i] != expectedBones[i] || finalMesh.bindposes[i] != expectedBindposes[i]) return Fail("BoneTableMismatch", out failure);
            var actualShapes = new HashSet<string>();
            for (int i = 0; i < finalMesh.blendShapeCount; i++) { string name = finalMesh.GetBlendShapeName(i); if (name.StartsWith("MCM_", StringComparison.Ordinal) || name.StartsWith("PBM_FBM_", StringComparison.Ordinal) || !actualShapes.Add(name)) return Fail("FinalBlendShapeInvalid", out failure); }
            if (!actualShapes.SetEquals(expectedBlendShapeNames)) return Fail("FinalBlendShapeSetMismatch", out failure);
            var humanNames = new HashSet<string>();
            foreach (HumanBone human in avatar.humanDescription.human) humanNames.Add(human.humanName);
            foreach (string boneName in requiredHumanBoneNames) if (!humanNames.Contains(boneName)) return Fail("HumanBoneMissing", out failure);
            return true;
        }

        /// <summary>Validates the Mesh-phase escrow before Material compilation has replaced the source renderer arrays.</summary>
        internal static bool TryValidateMeshEscrow(Mesh finalMesh, int materialSlotCount, IReadOnlyList<Transform> bones, Avatar avatar, int expectedVertexCount, int expectedMaterialSlotCount, IReadOnlyList<Transform> expectedBones, IReadOnlyList<Matrix4x4> expectedBindposes, IReadOnlyList<string> expectedBlendShapeNames, IReadOnlyList<string> requiredHumanBoneNames, out string failure)
        {
            failure = null;
            if (finalMesh == null || materialSlotCount < 0 || bones == null || avatar == null || !avatar.isHuman || expectedVertexCount < 0 || expectedBones == null || expectedBindposes == null || expectedBlendShapeNames == null || requiredHumanBoneNames == null) return Fail("InputInvalid", out failure);
            if (finalMesh.vertexCount != expectedVertexCount || finalMesh.subMeshCount != expectedMaterialSlotCount || materialSlotCount != expectedMaterialSlotCount)
                return Fail("MeshEscrowSlotMismatch", out failure);
            if (bones.Count != expectedBones.Count || finalMesh.bindposes.Length != expectedBindposes.Count || bones.Count != finalMesh.bindposes.Length)
                return Fail("BoneTableLengthMismatch: bones=" + bones.Count + "/expected=" + expectedBones.Count + "; finalBindposes=" + finalMesh.bindposes.Length + "/expected=" + expectedBindposes.Count, out failure);
            for (int i = 0; i < expectedBones.Count; i++) if (bones[i] != expectedBones[i] || finalMesh.bindposes[i] != expectedBindposes[i]) return Fail("BoneTableMismatch", out failure);
            var actualShapes = new HashSet<string>();
            for (int i = 0; i < finalMesh.blendShapeCount; i++) { string name = finalMesh.GetBlendShapeName(i); if (name.StartsWith("MCM_", StringComparison.Ordinal) || name.StartsWith("PBM_FBM_", StringComparison.Ordinal) || !actualShapes.Add(name)) return Fail("FinalBlendShapeInvalid", out failure); }
            if (!actualShapes.SetEquals(expectedBlendShapeNames)) return Fail("FinalBlendShapeSetMismatch: actual=" + string.Join(",", actualShapes) + "; expected=" + string.Join(",", expectedBlendShapeNames), out failure);
            var humanNames = new HashSet<string>();
            foreach (HumanBone human in avatar.humanDescription.human) humanNames.Add(human.humanName);
            foreach (string boneName in requiredHumanBoneNames) if (!humanNames.Contains(boneName)) return Fail("HumanBoneMissing", out failure);
            return true;
        }

        private static bool Fail(string code, out string failure) { failure = code; return false; }
    }
}
