// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Inspects skinning, bindpose, and bone mapping diagnostics.</summary>
    public class SkinningDiagnosticsWindow : EditorWindow
    {
        private SkinnedMeshRenderer sourceRenderer;
        private SkinnedMeshRenderer targetRenderer;
        private float epsilon = 0.00001f;
        private bool writeReport = true;

        internal void DrawDiagnosticsContent()
        {
            GUILayout.Label("Skinning Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Compare two merged SkinnedMeshRenderers to diagnose animation morph failures.", MessageType.Info);

            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer (HumanA)", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer (HumanB)", targetRenderer, typeof(SkinnedMeshRenderer), true);
            epsilon = EditorGUILayout.FloatField("Matrix Epsilon", epsilon);
            writeReport = EditorGUILayout.Toggle("Write Report Asset", writeReport);

            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(sourceRenderer == null || targetRenderer == null))
            {
                if (GUILayout.Button("Compare Skinning", GUILayout.Height(40f)))
                {
                    Compare();
                }
            }
        }

        private void Compare()
        {
            string report = BuildReport(sourceRenderer, targetRenderer, Mathf.Max(epsilon, 0.0000001f));
            Debug.Log(report);

            if (!writeReport)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Skinning Diagnostics Report",
                "SkinningDiagnosticsReport.txt",
                "txt",
                "Choose where to save the skinning diagnostics report.");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, report, Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();
        }

        public static string BuildReport(SkinnedMeshRenderer source, SkinnedMeshRenderer target, float matrixEpsilon)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("# Skinning Diagnostics Report");
            builder.AppendLine();
            AppendRendererSummary(builder, "Source", source);
            AppendRendererSummary(builder, "Target", target);
            builder.AppendLine();

            if (source == null || target == null || source.sharedMesh == null || target.sharedMesh == null)
            {
                builder.AppendLine("ERROR: Source/Target renderer and sharedMesh must be assigned.");
                return builder.ToString();
            }

            Mesh sourceMesh = source.sharedMesh;
            Mesh targetMesh = target.sharedMesh;
            CompareBasicMesh(builder, sourceMesh, targetMesh);
            CompareBones(builder, source, target);
            CompareBindposes(builder, sourceMesh, targetMesh, matrixEpsilon);
            CompareBoneWeights(builder, sourceMesh, targetMesh, matrixEpsilon);
            CompareBlendShapes(builder, sourceMesh, targetMesh);
            return builder.ToString();
        }

        private static void AppendRendererSummary(StringBuilder builder, string label, SkinnedMeshRenderer renderer)
        {
            builder.AppendLine($"## {label}");
            if (renderer == null)
            {
                builder.AppendLine("Renderer: <null>");
                return;
            }

            builder.AppendLine($"Renderer: {GetPath(renderer.transform)}");
            builder.AppendLine($"Mesh: {(renderer.sharedMesh != null ? renderer.sharedMesh.name : "<null>")}");
            builder.AppendLine($"Bones: {(renderer.bones != null ? renderer.bones.Length : 0)}");
            builder.AppendLine($"Materials: {(renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0)}");
        }

        private static void CompareBasicMesh(StringBuilder builder, Mesh sourceMesh, Mesh targetMesh)
        {
            builder.AppendLine("## Mesh");
            AppendEqual(builder, "vertexCount", sourceMesh.vertexCount, targetMesh.vertexCount);
            AppendEqual(builder, "subMeshCount", sourceMesh.subMeshCount, targetMesh.subMeshCount);
            AppendEqual(builder, "blendShapeCount", sourceMesh.blendShapeCount, targetMesh.blendShapeCount);

            int subMeshCount = Mathf.Min(sourceMesh.subMeshCount, targetMesh.subMeshCount);
            for (int i = 0; i < subMeshCount; i++)
            {
                AppendEqual(builder, $"subMesh[{i}].topology", sourceMesh.GetTopology(i), targetMesh.GetTopology(i));
                AppendEqual(builder, $"subMesh[{i}].indexCount", sourceMesh.GetIndexCount(i), targetMesh.GetIndexCount(i));
            }

            builder.AppendLine();
        }

        private static void CompareBones(StringBuilder builder, SkinnedMeshRenderer source, SkinnedMeshRenderer target)
        {
            builder.AppendLine("## Bones");
            Transform[] sourceBones = source.bones ?? System.Array.Empty<Transform>();
            Transform[] targetBones = target.bones ?? System.Array.Empty<Transform>();
            AppendEqual(builder, "bones.Length", sourceBones.Length, targetBones.Length);

            int count = Mathf.Min(sourceBones.Length, targetBones.Length);
            int pathMismatch = 0;
            for (int i = 0; i < count; i++)
            {
                string sourcePath = sourceBones[i] != null ? GetPath(sourceBones[i]) : "<null>";
                string targetPath = targetBones[i] != null ? GetPath(targetBones[i]) : "<null>";
                if (sourcePath != targetPath)
                {
                    pathMismatch++;
                    if (pathMismatch <= 20)
                    {
                        builder.AppendLine($"MISMATCH bones[{i}]: Source='{sourcePath}' Target='{targetPath}'");
                    }
                }
            }

            builder.AppendLine($"Bone path mismatches: {pathMismatch}");
            builder.AppendLine();
        }

        private static void CompareBindposes(StringBuilder builder, Mesh sourceMesh, Mesh targetMesh, float epsilon)
        {
            builder.AppendLine("## Bindposes");
            Matrix4x4[] sourceBindposes = sourceMesh.bindposes ?? System.Array.Empty<Matrix4x4>();
            Matrix4x4[] targetBindposes = targetMesh.bindposes ?? System.Array.Empty<Matrix4x4>();
            AppendEqual(builder, "bindposes.Length", sourceBindposes.Length, targetBindposes.Length);

            int count = Mathf.Min(sourceBindposes.Length, targetBindposes.Length);
            int mismatch = 0;
            float maxDelta = 0f;
            for (int i = 0; i < count; i++)
            {
                float delta = MaxMatrixDelta(sourceBindposes[i], targetBindposes[i]);
                if (delta > maxDelta)
                {
                    maxDelta = delta;
                }

                if (delta > epsilon)
                {
                    mismatch++;
                    if (mismatch <= 20)
                    {
                        builder.AppendLine($"MISMATCH bindposes[{i}]: maxDelta={delta}");
                    }
                }
            }

            builder.AppendLine($"Bindpose mismatches: {mismatch}");
            builder.AppendLine($"Max bindpose delta: {maxDelta}");
            builder.AppendLine();
        }

        private static void CompareBoneWeights(StringBuilder builder, Mesh sourceMesh, Mesh targetMesh, float epsilon)
        {
            builder.AppendLine("## BoneWeights");
            BoneWeight[] sourceWeights = sourceMesh.boneWeights ?? System.Array.Empty<BoneWeight>();
            BoneWeight[] targetWeights = targetMesh.boneWeights ?? System.Array.Empty<BoneWeight>();
            AppendEqual(builder, "boneWeights.Length", sourceWeights.Length, targetWeights.Length);

            int count = Mathf.Min(sourceWeights.Length, targetWeights.Length);
            int indexMismatch = 0;
            int weightMismatch = 0;
            for (int i = 0; i < count; i++)
            {
                BoneWeight source = sourceWeights[i];
                BoneWeight target = targetWeights[i];
                bool indicesEqual = source.boneIndex0 == target.boneIndex0 && source.boneIndex1 == target.boneIndex1 && source.boneIndex2 == target.boneIndex2 && source.boneIndex3 == target.boneIndex3;
                bool weightsEqual = Mathf.Abs(source.weight0 - target.weight0) <= epsilon
                    && Mathf.Abs(source.weight1 - target.weight1) <= epsilon
                    && Mathf.Abs(source.weight2 - target.weight2) <= epsilon
                    && Mathf.Abs(source.weight3 - target.weight3) <= epsilon;

                if (!indicesEqual)
                {
                    indexMismatch++;
                    if (indexMismatch <= 20)
                    {
                        builder.AppendLine($"MISMATCH boneWeight indices[{i}]: Source=({source.boneIndex0},{source.boneIndex1},{source.boneIndex2},{source.boneIndex3}) Target=({target.boneIndex0},{target.boneIndex1},{target.boneIndex2},{target.boneIndex3})");
                    }
                }

                if (!weightsEqual)
                {
                    weightMismatch++;
                    if (weightMismatch <= 20)
                    {
                        builder.AppendLine($"MISMATCH boneWeight values[{i}]: Source=({source.weight0},{source.weight1},{source.weight2},{source.weight3}) Target=({target.weight0},{target.weight1},{target.weight2},{target.weight3})");
                    }
                }
            }

            builder.AppendLine($"BoneWeight index mismatches: {indexMismatch}");
            builder.AppendLine($"BoneWeight value mismatches: {weightMismatch}");
            builder.AppendLine();
        }

        private static void CompareBlendShapes(StringBuilder builder, Mesh sourceMesh, Mesh targetMesh)
        {
            builder.AppendLine("## BlendShapes");
            int count = Mathf.Min(sourceMesh.blendShapeCount, targetMesh.blendShapeCount);
            for (int i = 0; i < count; i++)
            {
                string sourceName = sourceMesh.GetBlendShapeName(i);
                string targetName = targetMesh.GetBlendShapeName(i);
                if (sourceName != targetName)
                {
                    builder.AppendLine($"MISMATCH blendShape[{i}]: Source='{sourceName}' Target='{targetName}'");
                }
            }

            builder.AppendLine();
        }

        private static void AppendEqual<T>(StringBuilder builder, string label, T source, T target)
        {
            bool equal = Equals(source, target);
            builder.AppendLine($"{(equal ? "OK" : "MISMATCH")} {label}: Source={source} Target={target}");
        }

        private static float MaxMatrixDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0f;
            for (int i = 0; i < 16; i++)
            {
                float delta = Mathf.Abs(a[i] - b[i]);
                if (delta > max)
                {
                    max = delta;
                }
            }

            return max;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}

