// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEditor.Build;

namespace ShapeSync.TestProject
{
    /// <summary>Sets the explicit define used by the repeatable Spec22.3 test lanes.</summary>
    public static class ShapeSyncTestLaneControl
    {
        public static void SetCoreOnly()
        {
            SetDefines(string.Empty);
        }

        public static void SetVrmEnabled()
        {
            SetDefines("SHAPESYNC_USE_UNIVRM");
        }

        public static void CleanupConsumerTemp()
        {
            if (AssetDatabase.IsValidFolder("Assets/ShapeSyncTestTemp"))
            {
                AssetDatabase.DeleteAsset("Assets/ShapeSyncTestTemp");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            UnityEngine.Debug.Log("ShapeSync Spec22.3 consumer temp cleanup completed");
            EditorApplication.Exit(0);
        }

        private static void SetDefines(string defines)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, defines);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            UnityEngine.Debug.Log("ShapeSync Spec22.3 lane defines: " + (defines.Length == 0 ? "<empty>" : defines));
            EditorApplication.Exit(0);
        }
    }
}
