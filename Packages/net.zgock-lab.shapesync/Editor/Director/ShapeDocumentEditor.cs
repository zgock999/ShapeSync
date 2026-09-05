// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Inspector for the value-only ShapeDocument carrier.</summary>
    [CustomEditor(typeof(ShapeDocument))]
    internal sealed class ShapeDocumentEditor : UnityEditor.Editor
    {
        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("ShapeDocument stores detached recipes and serialized Shape values. It never stores runtime delivery, GPU, scene, or Proxy state.", MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
