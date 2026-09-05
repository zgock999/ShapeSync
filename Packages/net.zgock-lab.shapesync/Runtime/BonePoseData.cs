// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Serialized local-pose and optional bindpose data for one named skeleton bone.
    /// CharacterBoneRegistry stores these records for Figure and Outfit authoring.
    /// </summary>
    [Serializable]
    public class BonePoseData
    {
        public string boneName;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public int bindposeIndex = -1;
        public bool hasBindpose;
        public Matrix4x4 bindpose;
    }
}
