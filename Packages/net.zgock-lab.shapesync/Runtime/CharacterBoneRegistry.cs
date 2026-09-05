// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    #if SHAPESYNC_DEBUG
    [CreateAssetMenu(fileName = "CharacterBoneRegistry", menuName = "MorphSystem/CharacterBoneRegistry")]
    #endif
    /// <summary>
    /// Immutable authoring data that maps an FBM target to its bone poses and bindposes.
    /// Runtime consumers use the recorded poses; they do not derive behavior from the optional name metadata.
    /// </summary>
    public class CharacterBoneRegistry : ScriptableObject
    {
        [Tooltip("Optional editor metadata. Runtime registry matching does not use this value.")]
        public string fbmBlendName;
        public List<BonePoseData> bonePoses = new List<BonePoseData>();
    }
}
