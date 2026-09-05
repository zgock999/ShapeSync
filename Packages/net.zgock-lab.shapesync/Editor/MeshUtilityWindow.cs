// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Provides ShapeSync mesh merge, separation, and diagnostic editor workflows.</summary>
    public sealed class MeshUtilityWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Mesh Assembly", "Mesh Separation", "Surface Fit", "External Transfer", "Diagnostics", "Current Pose Helpers", "FBM Body Prefab", "Normal Map Bake" };
        [SerializeField] private int selectedTab;
        private SkinnedMeshMergerWindow merger;
        private SkinnedMeshSeparatorWindow separator;
        private SurfaceProjectorWindow surfaceFit;
        private ExternalTransferWindow wavefront;
        private SkinningDiagnosticsWindow diagnostics;
        private FbmBodyPrefabWindow fbmBodyPrefab;
        private NormalMapBakeWindow normalMapBake;

        [MenuItem("Tools/zgock/ShapeSync/Mesh Utility")]
        private static void Open()
        {
            GetWindowWithRect<MeshUtilityWindow>(new Rect(0f, 0f, 800f, 600f), false, "Mesh Utility");
        }

        private void OnEnable()
        {
            merger ??= CreateInstance<SkinnedMeshMergerWindow>();
            separator ??= CreateInstance<SkinnedMeshSeparatorWindow>();
            surfaceFit ??= CreateInstance<SurfaceProjectorWindow>();
            wavefront ??= CreateInstance<ExternalTransferWindow>();
            diagnostics ??= CreateInstance<SkinningDiagnosticsWindow>();
            fbmBodyPrefab ??= CreateInstance<FbmBodyPrefabWindow>();
            normalMapBake ??= CreateInstance<NormalMapBakeWindow>();
        }

        private void OnDisable()
        {
            DestroyPane(merger); DestroyPane(separator); DestroyPane(surfaceFit); DestroyPane(wavefront); DestroyPane(diagnostics); DestroyPane(fbmBodyPrefab); DestroyPane(normalMapBake);
            merger = null; separator = null; surfaceFit = null; wavefront = null; diagnostics = null; fbmBodyPrefab = null; normalMapBake = null;
        }

        private void OnGUI()
        {
            selectedTab = GUILayout.Toolbar(selectedTab, TabNames);
            EditorGUILayout.Space();
            switch (selectedTab)
            {
                case 0: merger.DrawMeshAssemblyContent(); break;
                case 1: separator.DrawMeshSeparationContent(); break;
                case 2: surfaceFit.DrawSurfaceFitContent(); break;
                case 3: wavefront.DrawExternalTransferContent(); break;
                case 4: diagnostics.DrawDiagnosticsContent(); break;
                case 5: wavefront.DrawCurrentPoseHelpersContent(); break;
                case 6: fbmBodyPrefab.DrawFbmBodyPrefabContent(); break;
                case 7: normalMapBake.DrawNormalMapBakeContent(); break;
            }
        }

        private static void DestroyPane(EditorWindow pane)
        {
            if (pane != null) DestroyImmediate(pane);
        }
    }
}
