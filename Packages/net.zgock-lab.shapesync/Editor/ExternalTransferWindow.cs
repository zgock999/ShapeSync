// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Transfers compatible ShapeSync data from an external source asset.</summary>
    public sealed class ExternalTransferWindow : EditorWindow
    {
        internal enum ExternalUnitSystem
        {
            UnityMeters,
            DazStudioCentimeters,
            PoserEightFeet
        }

        private GameObject sourceRoot;
        private SkinnedMeshRenderer sourceRenderer;
        private bool requireSignature = true;
        private bool exportCurrentPose;
        private ExternalUnitSystem externalUnitSystem;
        private float importPositionThreshold = 0.0001f;

        internal void DrawExternalTransferContent()
        {
            EditorGUILayout.LabelField("Safe Wavefront Transfer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Exports Unity vertex order as OBJ v/vn order. Export reconstructs safe adjacent triangle pairs as quads for DCC editing while preserving v/vn index order. Import accepts only the same vertex count and writes positions/normals into a new Mesh and Prefab; imported face records are ignored and the Unity Mesh keeps its original triangles. Source assets are never modified. Unit conversion scales positions only; it never changes or recenters XYZ axes.", MessageType.Info);
            sourceRoot = (GameObject)EditorGUILayout.ObjectField("Source Root", sourceRoot, typeof(GameObject), true);
            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            externalUnitSystem = (ExternalUnitSystem)EditorGUILayout.EnumPopup("External Unit System", externalUnitSystem);
            EditorGUILayout.HelpBox(GetUnitSystemDescription(externalUnitSystem), MessageType.None);
            exportCurrentPose = EditorGUILayout.Toggle("Export Current Pose", exportCurrentPose);
            requireSignature = EditorGUILayout.Toggle("Require Export Signature", requireSignature);
            importPositionThreshold = Mathf.Max(0f, EditorGUILayout.FloatField("Import Position Threshold (m)", importPositionThreshold));

            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(sourceRenderer == null || sourceRenderer.sharedMesh == null))
            {
                if (GUILayout.Button("Export Ordered OBJ", GUILayout.Height(34f))) Export();
            }
            using (new EditorGUI.DisabledScope(sourceRoot == null || sourceRenderer == null || sourceRenderer.sharedMesh == null))
            {
                if (GUILayout.Button("Import Ordered OBJ to New Prefab", GUILayout.Height(34f))) Import();
            }
        }

        internal void DrawCurrentPoseHelpersContent()
        {
            EditorGUILayout.LabelField("Current Pose Helpers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Bakes the current Bone and BlendShape pose into a valid SkinnedMesh Prefab. Its captured bindposes make the current pose render without a second deformation, while preserving BoneWeight data for use as a Surface Fit Source. Humanoid Bone Collection Profile capture remains in the dedicated profile tool.", MessageType.Info);
            sourceRoot = (GameObject)EditorGUILayout.ObjectField("Source Root", sourceRoot, typeof(GameObject), true);
            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(sourceRoot == null || sourceRenderer == null || sourceRenderer.sharedMesh == null))
            {
                if (GUILayout.Button("Generate Current Pose SkinnedMesh Prefab", GUILayout.Height(34f))) GenerateCurrentPoseTarget();
            }
        }

        private void Export()
        {
            Mesh mesh = sourceRenderer.sharedMesh;
            if (!mesh.isReadable) { Alert("Export Failed", "Source Mesh must be Read/Write Enabled."); return; }
            string path = EditorUtility.SaveFilePanel("Export Ordered OBJ", Application.dataPath, mesh.name + "_SafeTransfer", "obj");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Mesh baked = null;
                try
                {
                    Vector3[] vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    if (exportCurrentPose)
                    {
                        baked = new Mesh();
                        sourceRenderer.BakeMesh(baked);
                        if (baked.vertexCount != mesh.vertexCount) throw new InvalidOperationException("Baked Mesh vertex count does not match the Source Mesh.");
                        vertices = baked.vertices;
                        normals = baked.normals;
                    }
                    float positionScale = GetExportPositionScale(externalUnitSystem);
                    SafeWavefrontTransfer.Write(mesh, vertices, normals, path, positionScale);
                    EditorUtility.DisplayDialog("Ordered OBJ Exported", "Vertex order was written as OBJ v/vn order.\nPosition scale: " + positionScale.ToString("R", CultureInfo.InvariantCulture) + "." + (exportCurrentPose ? "\nCurrent Bone/BlendShape pose was baked." : "") + "\n\n" + path, "OK");
                }
                finally { if (baked != null) DestroyImmediate(baked); }
            }
            catch (Exception ex) { Alert("Export Failed", ex.Message); }
        }

        private void Import()
        {
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (!sourceMesh.isReadable) { Alert("Import Failed", "Source Mesh must be Read/Write Enabled."); return; }
            string objPath = EditorUtility.OpenFilePanel("Import Ordered OBJ", Application.dataPath, "obj");
            if (string.IsNullOrEmpty(objPath)) return;
            float positionScale = GetImportPositionScale(externalUnitSystem);
            if (!SafeWavefrontTransfer.TryRead(objPath, sourceMesh, requireSignature, positionScale, importPositionThreshold, out Vector3[] positions, out Vector3[] normals, out string error)) { Alert("Import Validation Failed", error); return; }
            string prefabPath = EditorUtility.SaveFilePanelInProject("Save Safe Wavefront Prefab", sourceRoot.name + "_Wavefront.prefab", "prefab", "Choose the output Prefab path.");
            if (string.IsNullOrEmpty(prefabPath)) return;
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset").Replace('\\', '/'));
            Mesh mesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            GameObject clone = null;
            bool meshSaved = false;
            try
            {
                mesh.name = Path.GetFileNameWithoutExtension(meshPath);
                mesh.vertices = positions;
                if (normals != null) mesh.normals = normals;
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath); meshSaved = true;
                clone = Instantiate(sourceRoot); clone.name = Path.GetFileNameWithoutExtension(prefabPath);
                string rendererPath = BonePoseUtility.GetRelativePath(sourceRoot.transform, sourceRenderer.transform);
                Transform rendererTransform = string.IsNullOrEmpty(rendererPath) ? clone.transform : clone.transform.Find(rendererPath);
                SkinnedMeshRenderer renderer = rendererTransform != null ? rendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null) throw new InvalidOperationException("Could not locate the Source Renderer on the output Prefab clone.");
                renderer.sharedMesh = mesh;
                renderer.name = Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh";
                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Unity failed to save the output Prefab.");
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Safe Wavefront Prefab Generated", "Saved prefab:\n" + prefabPath + "\n\nSaved mesh:\n" + meshPath, "OK");
            }
            catch (Exception ex)
            {
                if (meshSaved) AssetDatabase.DeleteAsset(meshPath); else DestroyImmediate(mesh);
                Alert("Import Failed", ex.Message);
            }
            finally { if (clone != null) DestroyImmediate(clone); }
        }

        private void GenerateCurrentPoseTarget()
        {
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (!sourceMesh.isReadable) { Alert("Current Pose Prefab Failed", "Source Mesh must be Read/Write Enabled."); return; }
            string prefabPath = EditorUtility.SaveFilePanelInProject("Save Current Pose SkinnedMesh Prefab", sourceRoot.name + "_CurrentPose.prefab", "prefab", "Choose the Current Pose SkinnedMesh Prefab output path.");
            if (string.IsNullOrEmpty(prefabPath)) return;

            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset").Replace('\\', '/'));
            Mesh baked = new Mesh();
            GameObject clone = null;
            bool meshSaved = false;
            try
            {
                sourceRenderer.BakeMesh(baked);
                if (baked.vertexCount != sourceMesh.vertexCount) throw new InvalidOperationException("Baked Mesh vertex count does not match the Source Mesh.");
                if (!TryConfigureCapturedPoseSkinning(sourceRenderer, sourceMesh, baked, out string skinningError)) throw new InvalidOperationException(skinningError);
                baked.name = Path.GetFileNameWithoutExtension(meshPath);
                baked.RecalculateBounds();
                AssetDatabase.CreateAsset(baked, meshPath); meshSaved = true;

                clone = Instantiate(sourceRoot); clone.name = Path.GetFileNameWithoutExtension(prefabPath);
                string rendererPath = BonePoseUtility.GetRelativePath(sourceRoot.transform, sourceRenderer.transform);
                Transform rendererTransform = string.IsNullOrEmpty(rendererPath) ? clone.transform : clone.transform.Find(rendererPath);
                SkinnedMeshRenderer renderer = rendererTransform != null ? rendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null) throw new InvalidOperationException("Could not locate the Source Renderer on the Current Pose Prefab clone.");

                // The captured bindposes make the clone's current bone matrices identity skin matrices.
                // Keep its bones and BoneWeight data so the Prefab can be used as a Surface Fit Source.
                renderer.sharedMesh = baked;
                renderer.name = Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh";

                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Unity failed to save the Current Pose Prefab.");
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Current Pose SkinnedMesh Generated", "The current Bone/BlendShape pose was baked into a valid SkinnedMesh Prefab.\n\nPrefab:\n" + prefabPath + "\n\nMesh:\n" + meshPath + "\n\nUse this renderer as a Surface Fit Source or Figure PCM Builder Target.", "OK");
            }
            catch (Exception ex)
            {
                if (meshSaved) AssetDatabase.DeleteAsset(meshPath); else DestroyImmediate(baked);
                Alert("Current Pose Prefab Failed", ex.Message);
            }
            finally { if (clone != null) DestroyImmediate(clone); }
        }

        internal static bool TryConfigureCapturedPoseSkinning(SkinnedMeshRenderer sourceRenderer, Mesh sourceMesh, Mesh bakedMesh, out string error)
        {
            error = null;
            if (sourceRenderer == null || sourceMesh == null || bakedMesh == null)
            {
                error = "Source Renderer, Source Mesh, and baked Mesh are required.";
                return false;
            }

            Transform[] bones = sourceRenderer.bones;
            BoneWeight[] weights = sourceMesh.boneWeights;
            if (bones == null || bones.Length == 0 || weights == null || weights.Length != sourceMesh.vertexCount)
            {
                error = "Source Renderer must have one BoneWeight per vertex and at least one bone to create a Current Pose SkinnedMesh Prefab.";
                return false;
            }

            Matrix4x4[] capturedBindposes = new Matrix4x4[bones.Length];
            Matrix4x4 rendererLocalToWorld = sourceRenderer.transform.localToWorldMatrix;
            for (int index = 0; index < bones.Length; index++)
            {
                if (bones[index] == null)
                {
                    error = $"Source Renderer bone at index {index} is missing.";
                    return false;
                }

                capturedBindposes[index] = bones[index].worldToLocalMatrix * rendererLocalToWorld;
            }

            bakedMesh.boneWeights = weights;
            bakedMesh.bindposes = capturedBindposes;
            return true;
        }

        internal static float GetExportPositionScale(ExternalUnitSystem unitSystem)
        {
            switch (unitSystem)
            {
                case ExternalUnitSystem.DazStudioCentimeters: return 100f;
                // Measured from DAZ Studio's Poser export: one Unity meter is 0.41010499 Poser units.
                case ExternalUnitSystem.PoserEightFeet: return 1f / 2.4384f;
                default: return 1f;
            }
        }

        internal static float GetImportPositionScale(ExternalUnitSystem unitSystem)
        {
            return 1f / GetExportPositionScale(unitSystem);
        }

        private static string GetUnitSystemDescription(ExternalUnitSystem unitSystem)
        {
            switch (unitSystem)
            {
                case ExternalUnitSystem.DazStudioCentimeters: return "DAZ Studio: export positions x100 (1 Unity meter = 100 DAZ centimeters); import positions /100.";
                case ExternalUnitSystem.PoserEightFeet: return "Poser (measured DAZ Poser export): export positions x0.41010499; import positions x2.4384. XYZ axes and origin remain unchanged.";
                default: return "Unity: export/import positions unchanged (1 Unity unit = 1 meter).";
            }
        }

        private static void Alert(string title, string message) { EditorUtility.DisplayDialog(title, message, "OK"); }
    }
}

internal static class SafeWavefrontTransfer
{
    private const string VertexCountPrefix = "# ShapeSyncSafeWavefrontTransfer VertexCount ";
    private const string TopologyHashPrefix = "# ShapeSyncSafeWavefrontTransfer TopologyHash ";

    public static void Write(Mesh mesh, string path)
    {
        Write(mesh, mesh.vertices, mesh.normals, path);
    }

    public static void Write(Mesh mesh, Vector3[] vertices, Vector3[] normals, string path)
    {
        Write(mesh, vertices, normals, path, 1f);
    }

    public static void Write(Mesh mesh, Vector3[] vertices, Vector3[] normals, string path, float positionScale)
    {
        if (vertices == null || vertices.Length != mesh.vertexCount) throw new InvalidOperationException("Export vertex count must match the Source Mesh.");
        if (normals != null && normals.Length != 0 && normals.Length != mesh.vertexCount) throw new InvalidOperationException("Export normal count must be zero or match the Source Mesh.");
        if (positionScale <= 0f || float.IsNaN(positionScale) || float.IsInfinity(positionScale)) throw new ArgumentOutOfRangeException(nameof(positionScale), "Position scale must be finite and greater than zero.");
        using (StreamWriter writer = new StreamWriter(path, false))
        {
            writer.WriteLine(VertexCountPrefix + vertices.Length);
            writer.WriteLine(TopologyHashPrefix + ComputeTopologyHash(mesh));
            writer.WriteLine("# Positions and normals are indexed identically. Do not add, remove, or reorder v/vn records.");
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 position = vertices[i] * positionScale;
                writer.WriteLine("v " + Format(position.x) + " " + Format(position.y) + " " + Format(position.z));
            }
            if (normals != null && normals.Length == vertices.Length) for (int i = 0; i < normals.Length; i++) writer.WriteLine("vn " + Format(normals[i].x) + " " + Format(normals[i].y) + " " + Format(normals[i].z));
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                writer.WriteLine("g ShapeSync_SubMesh_" + subMesh);
                int[] triangles = mesh.GetTriangles(subMesh);
                List<int[]> faces = BuildExportFaces(triangles);
                for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
                {
                    int[] face = faces[faceIndex];
                    writer.Write("f");
                    for (int vertex = 0; vertex < face.Length; vertex++)
                    {
                        int index = face[vertex] + 1;
                        writer.Write(" " + index + "//" + index);
                    }
                    writer.WriteLine();
                }
            }
        }
    }

    internal static List<int[]> BuildExportFaces(int[] triangles)
    {
        List<int[]> faces = new List<int[]>();
        if (triangles == null) return faces;
        bool[] consumed = new bool[triangles.Length / 3];
        for (int first = 0; first < consumed.Length; first++)
        {
            if (consumed[first]) continue;
            int firstOffset = first * 3;
            int[] quad = null;
            for (int second = first + 1; second < consumed.Length; second++)
            {
                if (consumed[second] || !TryBuildQuad(triangles, firstOffset, second * 3, out quad)) continue;
                consumed[first] = true;
                consumed[second] = true;
                break;
            }
            if (quad != null) faces.Add(quad);
            else faces.Add(new[] { triangles[firstOffset], triangles[firstOffset + 1], triangles[firstOffset + 2] });
        }
        return faces;
    }

    private static bool TryBuildQuad(int[] triangles, int firstOffset, int secondOffset, out int[] quad)
    {
        quad = null;
        Edge[] edges =
        {
            new Edge(triangles[firstOffset], triangles[firstOffset + 1]),
            new Edge(triangles[firstOffset + 1], triangles[firstOffset + 2]),
            new Edge(triangles[firstOffset + 2], triangles[firstOffset]),
            new Edge(triangles[secondOffset], triangles[secondOffset + 1]),
            new Edge(triangles[secondOffset + 1], triangles[secondOffset + 2]),
            new Edge(triangles[secondOffset + 2], triangles[secondOffset])
        };
        int sharedFirst = -1;
        int sharedSecond = -1;
        for (int first = 0; first < 3 && sharedFirst < 0; first++)
        {
            for (int second = 3; second < 6; second++)
            {
                if (edges[first].from == edges[second].to && edges[first].to == edges[second].from)
                {
                    sharedFirst = first;
                    sharedSecond = second;
                    break;
                }
            }
        }
        if (sharedFirst < 0) return false;

        List<Edge> boundary = new List<Edge>(4);
        for (int index = 0; index < edges.Length; index++) if (index != sharedFirst && index != sharedSecond) boundary.Add(edges[index]);
        List<int> ordered = new List<int>(4) { boundary[0].from };
        int next = boundary[0].to;
        for (int count = 1; count < 4; count++)
        {
            ordered.Add(next);
            int nextEdge = -1;
            for (int edge = 1; edge < boundary.Count; edge++)
            {
                if (boundary[edge].from == next) { nextEdge = edge; break; }
            }
            if (nextEdge < 0) return false;
            next = boundary[nextEdge].to;
            boundary.RemoveAt(nextEdge);
        }
        if (next != ordered[0] || new HashSet<int>(ordered).Count != 4) return false;
        quad = ordered.ToArray();
        return true;
    }

    private readonly struct Edge
    {
        public readonly int from;
        public readonly int to;
        public Edge(int from, int to) { this.from = from; this.to = to; }
    }

    public static bool TryRead(string path, Mesh source, bool requireSignature, out Vector3[] positions, out Vector3[] normals, out string error)
    {
        return TryRead(path, source, requireSignature, 1f, 0f, out positions, out normals, out error);
    }

    public static bool TryRead(string path, Mesh source, bool requireSignature, float positionScale, float unchangedPositionThreshold, out Vector3[] positions, out Vector3[] normals, out string error)
    {
        positions = null; normals = null; error = null;
        if (positionScale <= 0f || float.IsNaN(positionScale) || float.IsInfinity(positionScale)) { error = "Import position scale must be finite and greater than zero."; return false; }
        if (unchangedPositionThreshold < 0f || float.IsNaN(unchangedPositionThreshold) || float.IsInfinity(unchangedPositionThreshold)) { error = "Import position threshold must be finite and non-negative."; return false; }
        List<Vector3> readPositions = new List<Vector3>();
        List<Vector3> readNormals = new List<Vector3>();
        int? signedVertexCount = null; string signedTopologyHash = null;
        try
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.StartsWith(VertexCountPrefix, StringComparison.Ordinal)) { if (int.TryParse(line.Substring(VertexCountPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) signedVertexCount = count; continue; }
                if (line.StartsWith(TopologyHashPrefix, StringComparison.Ordinal)) { signedTopologyHash = line.Substring(TopologyHashPrefix.Length).Trim(); continue; }
                if (line.StartsWith("v ", StringComparison.Ordinal)) { if (!TryParseVector(line, 2, out Vector3 value)) { error = "OBJ contains an invalid vertex record."; return false; } readPositions.Add(value); }
                else if (line.StartsWith("vn ", StringComparison.Ordinal)) { if (!TryParseVector(line, 3, out Vector3 value)) { error = "OBJ contains an invalid normal record."; return false; } readNormals.Add(value); }
            }
        }
        catch (Exception ex) { error = ex.Message; return false; }
        if (requireSignature && (!signedVertexCount.HasValue || signedTopologyHash == null)) { error = "OBJ does not contain the Safe Wavefront Transfer signature. Disable Require Export Signature only if you independently guarantee its vertex order."; return false; }
        if (signedVertexCount.HasValue && signedVertexCount.Value != source.vertexCount) { error = "OBJ signature vertex count does not match the Source Mesh."; return false; }
        if (signedTopologyHash != null && signedTopologyHash != ComputeTopologyHash(source)) { error = "OBJ signature topology hash does not match the Source Mesh."; return false; }
        if (readPositions.Count != source.vertexCount) { error = "OBJ position count " + readPositions.Count + " does not match Source Mesh vertex count " + source.vertexCount + "."; return false; }
        if (readNormals.Count > 0 && readNormals.Count != source.vertexCount) { error = "OBJ normal count must be zero or match Source Mesh vertex count."; return false; }
        positions = new Vector3[source.vertexCount];
        float unchangedThresholdSquared = unchangedPositionThreshold * unchangedPositionThreshold;
        Vector3[] sourcePositions = source.vertices;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 importedPosition = readPositions[i] * positionScale;
            positions[i] = (importedPosition - sourcePositions[i]).sqrMagnitude <= unchangedThresholdSquared ? sourcePositions[i] : importedPosition;
        }
        normals = readNormals.Count == 0 ? null : readNormals.ToArray(); return true;
    }

    private static bool TryParseVector(string line, int prefixLength, out Vector3 value)
    {
        value = default; string[] parts = line.Substring(prefixLength).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value.x) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value.y) && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value.z);
    }

    private static string Format(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    private static string ComputeTopologyHash(Mesh mesh)
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)mesh.vertexCount) * 16777619;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                hash = (hash ^ (uint)triangles.Length) * 16777619;
                for (int i = 0; i < triangles.Length; i++) hash = (hash ^ (uint)triangles[i]) * 16777619;
            }
            return hash.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
