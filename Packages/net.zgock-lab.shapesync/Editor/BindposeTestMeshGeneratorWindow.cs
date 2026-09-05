// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Generates diagnostic meshes for bindpose validation workflows.</summary>
    public class BindposeTestMeshGeneratorWindow : EditorWindow
    {
        private Mesh sourceMesh;
        private Mesh targetBindposeMesh;
        private string outputMeshName = "HumanA_ShapeSync_TargetBindposes_Mesh";

    #if SHAPESYNC_DEBUG
        [MenuItem("Tools/zgock/ShapeSync/Bindpose Test Mesh Generator")]
    #endif
        public static void ShowWindow()
        {
            GetWindow<BindposeTestMeshGeneratorWindow>("Bindpose Test Mesh Generator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Bindpose Test Mesh Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates a copy of Source Mesh and replaces its bindposes with Target Bindpose Mesh.bindposes. " +
                "Use this only for diagnosis of animation deformation issues.",
                MessageType.Info);

            sourceMesh = (Mesh)EditorGUILayout.ObjectField("Source Mesh", sourceMesh, typeof(Mesh), false);
            targetBindposeMesh = (Mesh)EditorGUILayout.ObjectField("Target Bindpose Mesh", targetBindposeMesh, typeof(Mesh), false);
            outputMeshName = EditorGUILayout.TextField("Output Mesh Name", outputMeshName);

            using (new EditorGUI.DisabledScope(sourceMesh == null || targetBindposeMesh == null || string.IsNullOrWhiteSpace(outputMeshName)))
            {
                if (GUILayout.Button("Generate Test Mesh"))
                {
                    Generate();
                }
            }
        }

        private void Generate()
        {
            if (!ValidateInputs())
            {
                return;
            }

            string outputPath = EditorUtility.SaveFilePanelInProject(
                "Save Bindpose Test Mesh",
                outputMeshName + ".asset",
                "asset",
                "Choose where to save the bindpose test mesh.");

            if (string.IsNullOrEmpty(outputPath))
            {
                return;
            }

            Mesh generatedMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            generatedMesh.name = Path.GetFileNameWithoutExtension(outputPath);
            generatedMesh.bindposes = targetBindposeMesh.bindposes;

            AssetDatabase.CreateAsset(generatedMesh, AssetDatabase.GenerateUniqueAssetPath(outputPath));
            EditorUtility.SetDirty(generatedMesh);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = generatedMesh;
            EditorUtility.DisplayDialog(
                "Bindpose Test Mesh Generated",
                $"Saved mesh:\n{outputPath}\n\nSource vertices: {sourceMesh.vertexCount}\nBindposes copied: {targetBindposeMesh.bindposes.Length}",
                "OK");
        }

        private bool ValidateInputs()
        {
            if (sourceMesh == null || targetBindposeMesh == null)
            {
                EditorUtility.DisplayDialog("Missing Mesh", "Assign both Source Mesh and Target Bindpose Mesh.", "OK");
                return false;
            }

            if (sourceMesh.vertexCount <= 0)
            {
                EditorUtility.DisplayDialog("Invalid Source Mesh", "Source Mesh has no vertices.", "OK");
                return false;
            }

            if (sourceMesh.boneWeights == null || sourceMesh.boneWeights.Length != sourceMesh.vertexCount)
            {
                EditorUtility.DisplayDialog("Invalid Source Mesh", "Source Mesh must have one BoneWeight per vertex.", "OK");
                return false;
            }

            if (sourceMesh.bindposes == null || sourceMesh.bindposes.Length == 0)
            {
                EditorUtility.DisplayDialog("Invalid Source Mesh", "Source Mesh has no bindposes.", "OK");
                return false;
            }

            if (targetBindposeMesh.bindposes == null || targetBindposeMesh.bindposes.Length == 0)
            {
                EditorUtility.DisplayDialog("Invalid Target Mesh", "Target Bindpose Mesh has no bindposes.", "OK");
                return false;
            }

            if (sourceMesh.bindposes.Length != targetBindposeMesh.bindposes.Length)
            {
                EditorUtility.DisplayDialog(
                    "Bindpose Count Mismatch",
                    $"Source bindposes={sourceMesh.bindposes.Length}, Target bindposes={targetBindposeMesh.bindposes.Length}",
                    "OK");
                return false;
            }

            return true;
        }
    }
}

