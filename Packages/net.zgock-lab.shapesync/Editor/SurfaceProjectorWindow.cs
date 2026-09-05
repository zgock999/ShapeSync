// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;

#pragma warning disable CS0618

namespace zgock.ShapeSync.Editor
{
    /// <summary>Projects source surface data onto a compatible target mesh.</summary>
    public sealed class SurfaceProjectorWindow : EditorWindow
    {
        private GameObject sourceRoot;
        private SkinnedMeshRenderer sourceRenderer;
        private SkinnedMeshRenderer targetRenderer;
        private float maxProjectionDistance = 0.05f;
        private float minNormalDot = 0f;
        private TreeViewState boneTreeState;
        private ProjectionBoneTreeView boneTree;

        internal void DrawSurfaceFitContent()
        {
            GUILayout.Label("Surface Projection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Approximates the Target surface on the Source topology. The generated result always uses nearest-surface projection and does not create PCM BlendShapes.",
                MessageType.Info);

            sourceRoot = (GameObject)EditorGUILayout.ObjectField("Source Root", sourceRoot, typeof(GameObject), true);
            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer (Projected Surface)", targetRenderer, typeof(SkinnedMeshRenderer), true);
            DrawBoneTree();

            maxProjectionDistance = EditorGUILayout.FloatField("Max Projection Distance (m)", maxProjectionDistance);
            minNormalDot = EditorGUILayout.Slider("Min Normal Dot (-1 disables)", minNormalDot, -1f, 1f);
            EditorGUILayout.HelpBox(
                "Projection exceeds the distance limit or has no normal-compatible target triangle: generation stops with an Alert.",
                MessageType.None);

            bool canGenerate = sourceRoot != null && sourceRenderer != null && targetRenderer != null && boneTree != null && boneTree.HasProjectionBones;
            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("Generate Surface-Fit SkinnedMesh Prefab", GUILayout.Height(34f)))
                {
                    GenerateTarget();
                }
            }

        }
        private void GenerateTarget()
        {
            if (!TryValidateTarget(out Mesh sourceMesh, out Mesh targetMesh, out string error)) { EditorUtility.DisplayDialog("Target Projection Validation Failed", error, "OK"); return; }
            if (!TryBuildProjectionMask(sourceMesh, out bool[] projectionMask, out Vector3 hipsDelta, out error)) { EditorUtility.DisplayDialog("Target Projection Validation Failed", error, "OK"); return; }
            ProfileControlledMorphProjection.Settings settings = new ProfileControlledMorphProjection.Settings(maxProjectionDistance, minNormalDot, projectionMask, hipsDelta);
            if (!ProfileControlledMorphProjection.TryBuild(sourceMesh, sourceRenderer.transform, targetMesh, targetRenderer.transform, settings, out ProfileControlledMorphProjection.Result projection, out error)) { EditorUtility.DisplayDialog("Target Projection Failed", error, "OK"); return; }
            string prefabPath = EditorUtility.SaveFilePanelInProject("Save Surface-Fit SkinnedMesh Prefab", sourceRoot.name + "_SurfaceFit.prefab", "prefab", "Choose where to save the projected Target SkinnedMesh Prefab.");
            if (string.IsNullOrEmpty(prefabPath)) return;
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset").Replace('\\', '/'));
            Mesh generatedMesh = CreateStaticMesh(sourceMesh);
            generatedMesh.name = Path.GetFileNameWithoutExtension(meshPath);
            Vector3[] vertices = generatedMesh.vertices; BlendShapeBakeUtility.AddScaled(vertices, projection.deltaVertices, 1f); generatedMesh.vertices = vertices; generatedMesh.RecalculateBounds();
            GameObject prefabInstance = null; bool meshSaved = false; bool prefabSaved = false;
            try
            {
                prefabInstance = Instantiate(sourceRoot); prefabInstance.name = Path.GetFileNameWithoutExtension(prefabPath);
                string rendererPath = BonePoseUtility.GetRelativePath(sourceRoot.transform, sourceRenderer.transform);
                Transform clonedRendererTransform = string.IsNullOrEmpty(rendererPath) ? prefabInstance.transform : prefabInstance.transform.Find(rendererPath);
                SkinnedMeshRenderer clonedRenderer = clonedRendererTransform != null ? clonedRendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
                if (clonedRenderer == null) throw new System.InvalidOperationException("Could not find the matching Source Renderer on the Source Root clone.");
                if (!TryRemapRendererBones(sourceRenderer, sourceRoot.transform, clonedRenderer, prefabInstance.transform, out error)) throw new System.InvalidOperationException(error);
                clonedRenderer.name = Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh";
                AssetDatabase.CreateAsset(generatedMesh, meshPath); meshSaved = true; clonedRenderer.sharedMesh = generatedMesh;
                PrefabUtility.SaveAsPrefabAsset(prefabInstance, prefabPath, out bool success); if (!success) throw new System.InvalidOperationException("Unity failed to save the generated Target SkinnedMesh Prefab asset.");
                prefabSaved = true; AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Target SkinnedMesh Generated", $"Saved prefab:\n{prefabPath}\n\nSaved mesh:\n{meshPath}\n\nSurface-projected vertices: {projection.surfaceProjectedVertexCount}", "OK");
            }
            catch (System.Exception ex) { EditorUtility.DisplayDialog("Target Projection Failed", ex.Message, "OK"); }
            finally { if (prefabInstance != null) DestroyImmediate(prefabInstance); if (!meshSaved) DestroyImmediate(generatedMesh); else if (!prefabSaved) AssetDatabase.DeleteAsset(meshPath); }
        }

        private bool TryValidateTarget(out Mesh sourceMesh, out Mesh targetMesh, out string error)
        {
            sourceMesh = sourceRenderer != null ? sourceRenderer.sharedMesh : null;
            targetMesh = targetRenderer != null ? targetRenderer.sharedMesh : null;
            error = null;
            if (sourceRoot == null || sourceRenderer == null || targetRenderer == null) { error = "Source Root, Source Renderer, and Target Renderer are required."; return false; }
            if (!IsInHierarchy(sourceRenderer.transform, sourceRoot.transform)) { error = "Source Renderer must be inside Source Root."; return false; }
            if (sourceMesh == null || targetMesh == null || !sourceMesh.isReadable || !targetMesh.isReadable) { error = "Source Mesh and Target Mesh must be Read/Write Enabled."; return false; }
            if (sourceRenderer.rootBone != null && !IsInHierarchy(sourceRenderer.rootBone, sourceRoot.transform)) { error = "Source Renderer rootBone must be inside Source Root so the generated Prefab can remap it."; return false; }
            Transform[] bones = sourceRenderer.bones;
            for (int i = 0; i < bones.Length; i++) if (bones[i] == null || !IsInHierarchy(bones[i], sourceRoot.transform)) { error = "All Source Renderer bones must be inside Source Root so the generated Prefab can remap them."; return false; }
            return true;
        }

        private static Mesh CreateStaticMesh(Mesh source)
        {
            Mesh result = new Mesh { name = source.name + " Projected Target", indexFormat = source.indexFormat, subMeshCount = source.subMeshCount };
            result.vertices = source.vertices;
            if (source.normals.Length == source.vertexCount) result.normals = source.normals;
            if (source.tangents.Length == source.vertexCount) result.tangents = source.tangents;
            if (source.colors32.Length == source.vertexCount) result.colors32 = source.colors32;
            result.bindposes = source.bindposes;
            result.boneWeights = source.boneWeights;
            for (int channel = 0; channel < 8; channel++) { List<Vector4> values = new List<Vector4>(); source.GetUVs(channel, values); if (values.Count > 0) result.SetUVs(channel, values); }
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++) result.SetTriangles(source.GetTriangles(subMesh), subMesh, false);
            result.bounds = source.bounds;
            return result;
        }

        private static bool IsInHierarchy(Transform target, Transform root)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                if (current == root) return true;
            }

            return false;
        }

        private static bool TryRemapRendererBones(SkinnedMeshRenderer source, Transform sourceRoot, SkinnedMeshRenderer destination, Transform destinationRoot, out string error)
        {
            error = null;
            Transform[] sourceBones = source.bones;
            Transform[] remappedBones = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                string path = BonePoseUtility.GetRelativePath(sourceRoot, sourceBones[i]);
                remappedBones[i] = string.IsNullOrEmpty(path) ? destinationRoot : destinationRoot.Find(path);
                if (remappedBones[i] == null)
                {
                    error = $"Could not remap Source Renderer bone at index {i} into the generated Prefab.";
                    return false;
                }
            }

            destination.bones = remappedBones;
            if (source.rootBone != null)
            {
                string rootBonePath = BonePoseUtility.GetRelativePath(sourceRoot, source.rootBone);
                destination.rootBone = string.IsNullOrEmpty(rootBonePath) ? destinationRoot : destinationRoot.Find(rootBonePath);
                if (destination.rootBone == null)
                {
                    error = "Could not remap Source Renderer rootBone into the generated Prefab.";
                    return false;
                }
            }

            return true;
        }

        private void DrawBoneTree()
        {
            if (sourceRenderer == null) return;
            if (boneTree == null || boneTree.RootBone != sourceRenderer.rootBone)
            {
                boneTreeState ??= new TreeViewState();
                boneTree = new ProjectionBoneTreeView(boneTreeState, sourceRenderer.rootBone);
            }
            EditorGUILayout.LabelField("Projection Bones (ON = Surface Projection)", EditorStyles.boldLabel);
            boneTree.OnGUI(GUILayoutUtility.GetRect(0f, 150f, GUILayout.ExpandWidth(true)));
        }

        private bool TryBuildProjectionMask(Mesh sourceMesh, out bool[] mask, out Vector3 hipsDelta, out string error)
        {
            mask = null; hipsDelta = Vector3.zero; error = null;
            if (boneTree == null) { error = "Select at least one Source Renderer bone tree."; return false; }
            Animator sourceAnimator = sourceRenderer.GetComponentInParent<Animator>();
            Animator targetAnimator = targetRenderer.GetComponentInParent<Animator>();
            Transform sourceHips = sourceAnimator != null ? sourceAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform targetHips = targetAnimator != null ? targetAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (sourceHips == null || targetHips == null) { error = "Source and Target Animators must both map HumanBodyBones.Hips."; return false; }
            BoneWeight[] weights = sourceMesh.boneWeights;
            Transform[] bones = sourceRenderer.bones;
            if (weights == null || weights.Length != sourceMesh.vertexCount) { error = "Source Mesh must have one BoneWeight per vertex."; return false; }
            mask = new bool[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i]; int index = w.boneIndex0; float value = w.weight0;
                if (w.weight1 > value) { index = w.boneIndex1; value = w.weight1; }
                if (w.weight2 > value) { index = w.boneIndex2; value = w.weight2; }
                if (w.weight3 > value) index = w.boneIndex3;
                mask[i] = index >= 0 && index < bones.Length && boneTree.IsProjectionBone(bones[index]);
            }
            hipsDelta = targetHips.position - sourceHips.position;
            return true;
        }
    }
}

internal sealed class ProjectionBoneTreeView : TreeView
{
    private readonly HashSet<Transform> enabled = new HashSet<Transform>();
    public Transform RootBone { get; }
    public bool HasProjectionBones => enabled.Count > 0;
    public ProjectionBoneTreeView(TreeViewState state, Transform rootBone) : base(state) { RootBone = rootBone; Reload(); }
    public bool IsProjectionBone(Transform bone) { for (Transform t = bone; t != null; t = t.parent) if (enabled.Contains(t)) return true; return false; }
    protected override TreeViewItem BuildRoot()
    {
        TreeViewItem root = new TreeViewItem(-1, -1, "Root");
        if (RootBone != null) Add(root, RootBone, 0);
        SetupDepthsFromParentsAndChildren(root); return root;
    }
    private void Add(TreeViewItem parent, Transform transform, int depth)
    {
        TreeViewItem item = new TreeViewItem(transform.GetInstanceID(), depth, transform.name); parent.AddChild(item);
        for (int i = 0; i < transform.childCount; i++) Add(item, transform.GetChild(i), depth + 1);
    }
    protected override void RowGUI(RowGUIArgs args)
    {
        base.RowGUI(args); Transform t = EditorUtility.InstanceIDToObject(args.item.id) as Transform; if (t == null) return;
        Rect r = args.rowRect; r.x = r.xMax - 20f; r.width = 18f; bool next = EditorGUI.Toggle(r, enabled.Contains(t));
        if (next != enabled.Contains(t)) { if (next) enabled.Add(t); else enabled.Remove(t); }
    }
}

#pragma warning restore CS0618
