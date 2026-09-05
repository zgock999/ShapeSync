// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Merges compatible skinned meshes for ShapeSync authoring workflows.</summary>
    public class SkinnedMeshMergerWindow : EditorWindow
    {
        private GameObject rootObject;
        private GameObject previousRootObject;
        private readonly List<SkinnedMeshRenderer> sourceRenderers = new List<SkinnedMeshRenderer>();
        private bool removeSourceRendererObjectsInPrefab = true;
        private bool removeVrmComponentsInPrefab = true;
        private Vector2 rendererListScroll;

        internal void DrawMeshAssemblyContent()
        {
            GUILayout.Label("Skinned Mesh Merger (Preprocess)", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Merge Body/Face or other split SkinnedMeshRenderers into one renderer before running BlendShape Generator. " +
                "Use the same renderer order for HumanA and HumanB.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            rootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && rootObject != previousRootObject)
            {
                sourceRenderers.Clear();
                previousRootObject = rootObject;
            }
            removeSourceRendererObjectsInPrefab = EditorGUILayout.Toggle("Remove Source Renderer Objects", removeSourceRendererObjectsInPrefab);
            removeVrmComponentsInPrefab = EditorGUILayout.Toggle("Remove VRM Components", removeVrmComponentsInPrefab);
            EditorGUILayout.HelpBox("ON is the normal Spec2 output: remove stale UniVRM10.Vrm10Instance references after merge. Turn OFF only for normalized VRM10Instance preparation or diagnostics before expression baking.", MessageType.None);

            using (new EditorGUI.DisabledScope(rootObject == null))
            {
                if (GUILayout.Button("Collect Skinned Mesh Renderers From Root"))
                {
                    CollectRenderersFromRoot();
                }
            }

            DrawRendererList();

            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(rootObject == null || sourceRenderers.Count == 0))
            {
                if (GUILayout.Button("Generate Merged Prefab", GUILayout.Height(34f)))
                {
                    GenerateMergedPrefab();
                }
            }
        }

        private void CollectRenderersFromRoot()
        {
            sourceRenderers.Clear();
            if (rootObject == null)
            {
                return;
            }

            SkinnedMeshRenderer[] renderers = rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                sourceRenderers.Add(renderer);
            }
        }

        private void DrawRendererList()
        {
            EditorGUILayout.Space();
            GUILayout.Label("Source Renderers", EditorStyles.boldLabel);

            rendererListScroll = EditorGUILayout.BeginScrollView(rendererListScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(260f));
            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                sourceRenderers[i] = (SkinnedMeshRenderer)EditorGUILayout.ObjectField($"{i}", sourceRenderers[i], typeof(SkinnedMeshRenderer), true);

                using (new EditorGUI.DisabledScope(i == 0))
                {
                    if (GUILayout.Button("Up", GUILayout.Width(42f)))
                    {
                        SwapRenderers(i, i - 1);
                    }
                }

                using (new EditorGUI.DisabledScope(i == sourceRenderers.Count - 1))
                {
                    if (GUILayout.Button("Down", GUILayout.Width(50f)))
                    {
                        SwapRenderers(i, i + 1);
                    }
                }

                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    sourceRenderers.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Slot"))
            {
                sourceRenderers.Add(null);
            }

            if (GUILayout.Button("Clear"))
            {
                sourceRenderers.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SwapRenderers(int a, int b)
        {
            SkinnedMeshRenderer temp = sourceRenderers[a];
            sourceRenderers[a] = sourceRenderers[b];
            sourceRenderers[b] = temp;
        }

        private void GenerateMergedPrefab()
        {
            if (!ValidateInputs())
            {
                return;
            }

            string prefabPath = EditorUtility.SaveFilePanelInProject(
                "Save Merged Prefab",
                rootObject.name + "_Merged.prefab",
                "prefab",
                "Choose where to save the merged prefab.");

            if (string.IsNullOrEmpty(prefabPath))
            {
                return;
            }

            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog("Invalid Path", "Could not resolve the selected prefab folder.", "OK");
                return;
            }

            string outputBaseName = Path.GetFileNameWithoutExtension(prefabPath);
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, outputBaseName + "_Mesh.asset").Replace('\\', '/'));
            GameObject prefabInstance = null;
            Mesh mergedMesh = null;
            ShapeSyncFigureMeshMerger.Result mergeResult = null;
            bool meshAssetCreated = false;
            bool retainedOutput = false;
            try
            {
                if (!ShapeSyncFigureMeshMerger.TryMergeOwned(rootObject, sourceRenderers, out mergeResult, out string diagnostic))
                {
                    EditorUtility.DisplayDialog("Skinned Mesh Merge Failed", diagnostic, "OK");
                    return;
                }

                prefabInstance = mergeResult.Root;
                SkinnedMeshRenderer mergedRenderer = mergeResult.Renderer;
                prefabInstance.name = outputBaseName;
                mergedMesh = mergeResult.DetachMesh();
                mergedMesh.name = outputBaseName + "_Mesh";
                AssetDatabase.CreateAsset(mergedMesh, meshPath);
                meshAssetCreated = true;
                mergedRenderer.localBounds = mergedMesh.bounds;
                RemoveVrmComponentsIfRequested(prefabInstance);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(prefabInstance, prefabPath, out success);
                if (!success)
                {
                    EditorUtility.DisplayDialog("Prefab Save Failed", "Unity failed to save the generated merged prefab asset.", "OK");
                    AssetDatabase.DeleteAsset(meshPath);
                    return;
                }

                EditorUtility.SetDirty(mergedMesh);
                AssetDatabase.SaveAssets();
                retainedOutput = true;
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Skinned Mesh Merged", $"Saved prefab:\n{prefabPath}\n\nSaved mesh:\n{meshPath}", "OK");
            }
            finally
            {
                if (prefabInstance != null)
                {
                    DestroyImmediate(prefabInstance);
                }
                CleanupUnretainedMergedOutput(mergedMesh, meshPath, meshAssetCreated, retainedOutput);
                mergeResult?.Dispose();
            }
        }

        /// <summary>Releases a generated Mesh when the merged Prefab output was not committed.</summary>
        internal static void CleanupUnretainedMergedOutput(Mesh mergedMesh, string meshPath, bool meshAssetCreated, bool retainedOutput)
        {
            if (retainedOutput) return;
            if (meshAssetCreated) AssetDatabase.DeleteAsset(meshPath);
            else if (mergedMesh != null) DestroyImmediate(mergedMesh);
        }


        private void RemoveVrmComponentsIfRequested(GameObject prefabInstance)
        {
            if (!removeVrmComponentsInPrefab || prefabInstance == null)
            {
                return;
            }

            Component[] components = prefabInstance.GetComponentsInChildren<Component>(true);
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                System.Type type = component.GetType();
                if (IsRemovableVrmComponent(type))
                {
                    DestroyImmediate(component);
                }
            }
        }
        private bool IsRemovableVrmComponent(System.Type type)
        {
            return type != null && type.FullName == "UniVRM10.Vrm10Instance";
        }
        private bool ValidateInputs()
        {
            if (rootObject == null)
            {
                EditorUtility.DisplayDialog("Missing Root", "Assign the root GameObject.", "OK");
                return false;
            }

            if (sourceRenderers.Count == 0)
            {
                EditorUtility.DisplayDialog("Missing Renderers", "Add at least one SkinnedMeshRenderer.", "OK");
                return false;
            }

            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                SkinnedMeshRenderer renderer = sourceRenderers[i];
                if (renderer == null)
                {
                    EditorUtility.DisplayDialog("Invalid Renderer", $"Renderer slot {i} is empty.", "OK");
                    return false;
                }

                if (!renderer.transform.IsChildOf(rootObject.transform))
                {
                    EditorUtility.DisplayDialog("Invalid Renderer", $"Renderer '{renderer.name}' is not under the root object.", "OK");
                    return false;
                }

                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    EditorUtility.DisplayDialog("Missing Mesh", $"Renderer '{renderer.name}' has no shared mesh.", "OK");
                    return false;
                }

                if (renderer.bones == null || renderer.bones.Length == 0)
                {
                    EditorUtility.DisplayDialog("Missing Bones", $"Renderer '{renderer.name}' has no bones.", "OK");
                    return false;
                }

                if (mesh.bindposes == null || mesh.bindposes.Length != renderer.bones.Length)
                {
                    EditorUtility.DisplayDialog("Invalid Bindposes", $"Renderer '{renderer.name}' bindpose count must match its bones count.", "OK");
                    return false;
                }

                if (mesh.boneWeights == null || mesh.boneWeights.Length != mesh.vertexCount)
                {
                    EditorUtility.DisplayDialog("Invalid Bone Weights", $"Renderer '{renderer.name}' must have one BoneWeight per vertex.", "OK");
                    return false;
                }

                if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length < mesh.subMeshCount)
                {
                    EditorUtility.DisplayDialog("Invalid Materials", $"Renderer '{renderer.name}' must have at least one material per submesh.", "OK");
                    return false;
                }
            }

            return true;
        }

        private static SkinnedMeshRenderer[] ResolveClonedRenderers(GameObject root, IReadOnlyList<SkinnedMeshRenderer> renderers, Transform clonedRoot)
        {
            SkinnedMeshRenderer[] clonedRenderers = new SkinnedMeshRenderer[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                string path = BonePoseUtility.GetRelativePath(root.transform, renderers[i].transform);
                Transform clonedTransform = string.IsNullOrEmpty(path) ? clonedRoot : clonedRoot.Find(path);
                SkinnedMeshRenderer clonedRenderer = clonedTransform != null ? clonedTransform.GetComponent<SkinnedMeshRenderer>() : null;
                if (clonedRenderer == null)
                {
                    return null;
                }

                clonedRenderers[i] = clonedRenderer;
            }

            return clonedRenderers;
        }

        private static Transform CreateMergedRendererTransform(Transform clonedRoot, string objectName)
        {
            GameObject mergedObject = new GameObject(objectName);
            Transform mergedTransform = mergedObject.transform;
            mergedTransform.SetParent(clonedRoot, false);
            mergedTransform.localPosition = Vector3.zero;
            mergedTransform.localRotation = Quaternion.identity;
            mergedTransform.localScale = Vector3.one;
            return mergedTransform;
        }

        private static void CopyRendererSettings(SkinnedMeshRenderer source, SkinnedMeshRenderer destination)
        {
            destination.updateWhenOffscreen = source.updateWhenOffscreen;
            destination.skinnedMotionVectors = source.skinnedMotionVectors;
            destination.quality = source.quality;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
            destination.lightProbeUsage = source.lightProbeUsage;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.probeAnchor = source.probeAnchor;
        }

        private static Mesh BuildMergedMesh(IReadOnlyList<SkinnedMeshRenderer> renderers, Transform mergedRendererTransform,
            Action<Mesh> afterMergedMeshAllocated,
            out Transform[] mergedBones, out Material[] mergedMaterials)
        {
            BoneMergeData boneData = BuildBoneMergeData(renderers, mergedRendererTransform);
            mergedBones = boneData.bones.ToArray();

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<BoneWeight> boneWeights = new List<BoneWeight>();
            List<int[]> subMeshTriangles = new List<int[]>();
            List<Material> materials = new List<Material>();
            List<int> rendererVertexOffsets = new List<int>();
            List<Matrix4x4> rendererToMergedLocalMatrices = new List<Matrix4x4>();

            bool includeNormals = AllMeshesHaveNormals(renderers);
            bool includeTangents = AllMeshesHaveTangents(renderers);
            bool includeUv0 = AllMeshesHaveUv(renderers, 0);
            bool includeUv1 = AllMeshesHaveUv(renderers, 1);
            bool includeColors = AnyMeshHasColors(renderers);

            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Mesh mesh = renderer.sharedMesh;
                int vertexOffset = vertices.Count;
                Matrix4x4 toMergedLocal = mergedRendererTransform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                rendererVertexOffsets.Add(vertexOffset);
                rendererToMergedLocalMatrices.Add(toMergedLocal);

                Vector3[] sourceVertices = mesh.vertices;
                for (int i = 0; i < sourceVertices.Length; i++)
                {
                    vertices.Add(toMergedLocal.MultiplyPoint3x4(sourceVertices[i]));
                }

                if (includeNormals)
                {
                    Vector3[] sourceNormals = mesh.normals;
                    for (int i = 0; i < sourceNormals.Length; i++)
                    {
                        normals.Add(toMergedLocal.MultiplyVector(sourceNormals[i]).normalized);
                    }
                }

                if (includeTangents)
                {
                    Vector4[] sourceTangents = mesh.tangents;
                    for (int i = 0; i < sourceTangents.Length; i++)
                    {
                        Vector3 tangent = toMergedLocal.MultiplyVector(new Vector3(sourceTangents[i].x, sourceTangents[i].y, sourceTangents[i].z)).normalized;
                        tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, sourceTangents[i].w));
                    }
                }

                AppendUv(mesh, 0, includeUv0, uv0);
                AppendUv(mesh, 1, includeUv1, uv1);
                AppendColors(mesh, includeColors, colors);
                AppendBoneWeights(mesh.boneWeights, renderer, boneData.boneIndexLookup, boneWeights);

                Material[] rendererMaterials = renderer.sharedMaterials ?? Array.Empty<Material>();
                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    int[] triangles = mesh.GetTriangles(subMeshIndex, true);
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        triangles[i] += vertexOffset;
                    }

                    subMeshTriangles.Add(triangles);
                    materials.Add(subMeshIndex < rendererMaterials.Length ? rendererMaterials[subMeshIndex] : null);
                }
            }

            Mesh mergedMesh = new Mesh { name = "MergedSkinnedMesh" };
            try
            {
                afterMergedMeshAllocated?.Invoke(mergedMesh);
                if (vertices.Count > 65535) mergedMesh.indexFormat = IndexFormat.UInt32;
                mergedMesh.SetVertices(vertices);
                if (includeNormals) mergedMesh.SetNormals(normals);
                if (includeTangents) mergedMesh.SetTangents(tangents);
                if (includeUv0) mergedMesh.SetUVs(0, uv0);
                if (includeUv1) mergedMesh.SetUVs(1, uv1);
                if (includeColors) mergedMesh.SetColors(colors);
                mergedMesh.boneWeights = boneWeights.ToArray();
                mergedMesh.bindposes = boneData.bindposes.ToArray();
                mergedMesh.subMeshCount = subMeshTriangles.Count;
                CopyBlendShapes(renderers, rendererVertexOffsets, rendererToMergedLocalMatrices, vertices.Count, mergedMesh);
                for (int i = 0; i < subMeshTriangles.Count; i++) mergedMesh.SetTriangles(subMeshTriangles[i], i, true);
                if (!includeNormals) mergedMesh.RecalculateNormals();
                if (!includeTangents) mergedMesh.RecalculateTangents();
                mergedMesh.RecalculateBounds();
                mergedMaterials = materials.ToArray();
                return mergedMesh;
            }
            catch
            {
                DestroyImmediate(mergedMesh);
                throw;
            }
        }

        private static void CopyBlendShapes(
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            IReadOnlyList<int> rendererVertexOffsets,
            IReadOnlyList<Matrix4x4> rendererToMergedLocalMatrices,
            int mergedVertexCount,
            Mesh mergedMesh)
        {
            HashSet<string> usedNames = new HashSet<string>();
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Mesh sourceMesh = renderer.sharedMesh;
                if (sourceMesh == null || sourceMesh.blendShapeCount == 0)
                {
                    continue;
                }

                int vertexOffset = rendererVertexOffsets[rendererIndex];
                Matrix4x4 toMergedLocal = rendererToMergedLocalMatrices[rendererIndex];
                int sourceVertexCount = sourceMesh.vertexCount;

                Vector3[] sourceDeltaVertices = new Vector3[sourceVertexCount];
                Vector3[] sourceDeltaNormals = new Vector3[sourceVertexCount];
                Vector3[] sourceDeltaTangents = new Vector3[sourceVertexCount];

                for (int shapeIndex = 0; shapeIndex < sourceMesh.blendShapeCount; shapeIndex++)
                {
                    string sourceShapeName = sourceMesh.GetBlendShapeName(shapeIndex);
                    string mergedShapeName = MakeUniqueBlendShapeName(sourceShapeName, renderer.name, usedNames);
                    int frameCount = sourceMesh.GetBlendShapeFrameCount(shapeIndex);

                    for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        float frameWeight = sourceMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
                        sourceMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);

                        Vector3[] mergedDeltaVertices = new Vector3[mergedVertexCount];
                        Vector3[] mergedDeltaNormals = new Vector3[mergedVertexCount];
                        Vector3[] mergedDeltaTangents = new Vector3[mergedVertexCount];

                        for (int vertexIndex = 0; vertexIndex < sourceVertexCount; vertexIndex++)
                        {
                            int mergedIndex = vertexOffset + vertexIndex;
                            mergedDeltaVertices[mergedIndex] = toMergedLocal.MultiplyVector(sourceDeltaVertices[vertexIndex]);
                            mergedDeltaNormals[mergedIndex] = toMergedLocal.MultiplyVector(sourceDeltaNormals[vertexIndex]).normalized;
                            mergedDeltaTangents[mergedIndex] = toMergedLocal.MultiplyVector(sourceDeltaTangents[vertexIndex]).normalized;
                        }

                        mergedMesh.AddBlendShapeFrame(mergedShapeName, frameWeight, mergedDeltaVertices, mergedDeltaNormals, mergedDeltaTangents);
                    }
                }
            }
        }

        private static string MakeUniqueBlendShapeName(string sourceShapeName, string rendererName, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrEmpty(sourceShapeName) ? "BlendShape" : sourceShapeName;
            if (usedNames.Add(baseName))
            {
                return baseName;
            }

            string rendererPrefixedName = string.IsNullOrEmpty(rendererName) ? baseName : rendererName + "/" + baseName;
            if (usedNames.Add(rendererPrefixedName))
            {
                return rendererPrefixedName;
            }

            int suffix = 1;
            string candidate;
            do
            {
                candidate = rendererPrefixedName + "_" + suffix;
                suffix++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
        }
        private static BoneMergeData BuildBoneMergeData(IReadOnlyList<SkinnedMeshRenderer> renderers, Transform mergedRendererTransform)
        {
            BoneMergeData data = new BoneMergeData();
            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Transform[] rendererBones = renderer.bones;
                for (int boneIndex = 0; boneIndex < rendererBones.Length; boneIndex++)
                {
                    Transform bone = rendererBones[boneIndex];
                    if (bone == null)
                    {
                        continue;
                    }

                    if (!data.boneIndexLookup.ContainsKey(bone))
                    {
                        data.boneIndexLookup.Add(bone, data.bones.Count);
                        data.bones.Add(bone);
                        data.bindposes.Add(bone.worldToLocalMatrix * mergedRendererTransform.localToWorldMatrix);
                    }
                }
            }

            return data;
        }

        private static void AppendBoneWeights(BoneWeight[] sourceWeights, SkinnedMeshRenderer renderer, Dictionary<Transform, int> boneIndexLookup, List<BoneWeight> destination)
        {
            Transform[] bones = renderer.bones;
            for (int i = 0; i < sourceWeights.Length; i++)
            {
                BoneWeight weight = sourceWeights[i];
                weight.boneIndex0 = RemapBoneIndex(bones, weight.boneIndex0, boneIndexLookup);
                weight.boneIndex1 = RemapBoneIndex(bones, weight.boneIndex1, boneIndexLookup);
                weight.boneIndex2 = RemapBoneIndex(bones, weight.boneIndex2, boneIndexLookup);
                weight.boneIndex3 = RemapBoneIndex(bones, weight.boneIndex3, boneIndexLookup);
                destination.Add(weight);
            }
        }

        private static int RemapBoneIndex(Transform[] bones, int sourceIndex, Dictionary<Transform, int> boneIndexLookup)
        {
            if (sourceIndex < 0 || sourceIndex >= bones.Length || bones[sourceIndex] == null)
            {
                return 0;
            }

            return boneIndexLookup.TryGetValue(bones[sourceIndex], out int remappedIndex) ? remappedIndex : 0;
        }

        private static void AppendUv(Mesh mesh, int channel, bool include, List<Vector2> destination)
        {
            if (!include)
            {
                return;
            }

            List<Vector2> source = new List<Vector2>();
            mesh.GetUVs(channel, source);
            if (source.Count == mesh.vertexCount)
            {
                destination.AddRange(source);
                return;
            }

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                destination.Add(Vector2.zero);
            }
        }

        private static void AppendColors(Mesh mesh, bool include, List<Color> destination)
        {
            if (!include)
            {
                return;
            }

            Color[] source = mesh.colors;
            if (source != null && source.Length == mesh.vertexCount)
            {
                destination.AddRange(source);
                return;
            }

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                destination.Add(Color.white);
            }
        }

        private static bool AllMeshesHaveNormals(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Mesh mesh = renderers[i].sharedMesh;
                if (mesh.normals == null || mesh.normals.Length != mesh.vertexCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllMeshesHaveTangents(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Mesh mesh = renderers[i].sharedMesh;
                if (mesh.tangents == null || mesh.tangents.Length != mesh.vertexCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllMeshesHaveUv(IReadOnlyList<SkinnedMeshRenderer> renderers, int channel)
        {
            List<Vector2> uvs = new List<Vector2>();
            for (int i = 0; i < renderers.Count; i++)
            {
                Mesh mesh = renderers[i].sharedMesh;
                uvs.Clear();
                mesh.GetUVs(channel, uvs);
                if (uvs.Count != mesh.vertexCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AnyMeshHasColors(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Mesh mesh = renderers[i].sharedMesh;
                if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform RemapRootBone(SkinnedMeshRenderer sourceRenderer, Transform root, Transform clonedRoot)
        {
            if (sourceRenderer.rootBone == null)
            {
                return null;
            }

            string rootBonePath = BonePoseUtility.GetRelativePath(root, sourceRenderer.rootBone);
            return string.IsNullOrEmpty(rootBonePath) ? clonedRoot : clonedRoot.Find(rootBonePath);
        }

        private sealed class BoneMergeData
        {
            public readonly List<Transform> bones = new List<Transform>();
            public readonly List<Matrix4x4> bindposes = new List<Matrix4x4>();
            public readonly Dictionary<Transform, int> boneIndexLookup = new Dictionary<Transform, int>();
        }

        internal static bool TryCreateMergedClone(
            GameObject root,
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            Action<Mesh> afterMergedMeshAllocated,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            return TryCreateMergedCloneCore(root, renderers, afterMergedMeshAllocated,
                out mergedRoot, out mergedRenderer, out diagnostic);
        }

        /// <summary>Creates a merged clone for geometry-only callers such as Figure PBM.
        /// Missing source Materials are represented as null slots; the caller is responsible
        /// for assigning any canonical output Materials after the merge.</summary>
        internal static bool TryCreateMergedCloneGeometryOnly(
            GameObject root,
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            Action<Mesh> afterMergedMeshAllocated,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            return TryCreateMergedCloneCore(root, renderers, afterMergedMeshAllocated,
                out mergedRoot, out mergedRenderer, out diagnostic);
        }

        private static bool TryCreateMergedCloneCore(
            GameObject root,
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            Action<Mesh> afterMergedMeshAllocated,
            out GameObject mergedRoot,
            out SkinnedMeshRenderer mergedRenderer,
            out string diagnostic)
        {
            mergedRoot = null;
            mergedRenderer = null;
            diagnostic = null;
            try
            {
                mergedRoot = Instantiate(root);
                mergedRoot.name = root.name + "_Merged";
                SkinnedMeshRenderer[] clonedRenderers = ResolveClonedRenderers(root, renderers, mergedRoot.transform);
                if (clonedRenderers == null)
                {
                    diagnostic = "Mesh Utility Merger could not resolve a cloned source renderer.";
                    DestroyImmediate(mergedRoot);
                    mergedRoot = null;
                    return false;
                }

                Transform mergedTransform = CreateMergedRendererTransform(mergedRoot.transform, root.name + "_MergedMesh");
                mergedRenderer = mergedTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                CopyRendererSettings(clonedRenderers[0], mergedRenderer);
                Mesh mergedMesh = BuildMergedMesh(clonedRenderers, mergedTransform, afterMergedMeshAllocated,
                    out Transform[] mergedBones, out Material[] mergedMaterials);
                if (mergedMesh == null)
                {
                    diagnostic = "Mesh Utility Merger could not build a merged mesh.";
                    DestroyImmediate(mergedRoot);
                    mergedRoot = null;
                    mergedRenderer = null;
                    return false;
                }

                mergedRenderer.sharedMesh = mergedMesh;
                mergedRenderer.bones = mergedBones;
                mergedRenderer.rootBone = RemapRootBone(clonedRenderers[0], root.transform, mergedRoot.transform) ?? mergedRoot.transform;
                mergedRenderer.sharedMaterials = mergedMaterials;
                mergedRenderer.localBounds = mergedMesh.bounds;
                RemoveSourceRenderers(clonedRenderers, mergedRoot.transform, true);
                return true;
            }
            catch (System.Exception exception)
            {
                diagnostic = "Mesh Utility Merger failed: " + exception.Message;
                if (mergedRenderer != null && mergedRenderer.sharedMesh != null) DestroyImmediate(mergedRenderer.sharedMesh);
                if (mergedRoot != null) DestroyImmediate(mergedRoot);
                mergedRoot = null;
                mergedRenderer = null;
                return false;
            }
        }

    private static void RemoveSourceRenderers(SkinnedMeshRenderer[] clonedRenderers, Transform clonedRoot, bool removeSourceRendererObjects)
        {
            for (int i = clonedRenderers.Length - 1; i >= 0; i--)
            {
                SkinnedMeshRenderer renderer = clonedRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!removeSourceRendererObjects)
                {
                    renderer.enabled = false;
                    continue;
                }

                GameObject rendererObject = renderer.gameObject;
                Transform rendererTransform = rendererObject.transform;
                bool canRemoveObject = rendererTransform != clonedRoot && rendererTransform.childCount == 0;
                if (canRemoveObject)
                {
                    DestroyImmediate(rendererObject);
                }
                else
                {
                    DestroyImmediate(renderer);
                }
            }
        }
    }
}




