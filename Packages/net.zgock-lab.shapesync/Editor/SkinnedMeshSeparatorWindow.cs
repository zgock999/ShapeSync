// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Separates selected geometry from a skinned mesh into a new asset.</summary>
    public sealed class SkinnedMeshSeparatorWindow : EditorWindow
    {
        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private List<bool> selectedMaterials = new List<bool>();

        internal void DrawMeshSeparationContent()
        {
            EditorGUI.BeginChangeCheck();
            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Source Renderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            if (EditorGUI.EndChangeCheck()) RefreshMaterialSelection();
            DrawMaterialSelection();
            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(!CanGenerate()))
            {
                if (GUILayout.Button("Generate Separated Mesh Prefab", GUILayout.Height(34f))) Generate();
            }
        }

        private void DrawMaterialSelection()
        {
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) return;
            EnsureMaterialSelection();
            Material[] materials = sourceRenderer.sharedMaterials;
            for (int i = 0; i < selectedMaterials.Count; i++)
            {
                string name = i < materials.Length && materials[i] != null ? materials[i].name : "<Missing Material>";
                selectedMaterials[i] = EditorGUILayout.ToggleLeft($"{i}: {name}", selectedMaterials[i]);
            }
        }

        private bool CanGenerate()
        {
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) return false;
            EnsureMaterialSelection();
            for (int i = 0; i < selectedMaterials.Count; i++) if (selectedMaterials[i]) return true;
            return false;
        }

        private void RefreshMaterialSelection()
        {
            selectedMaterials.Clear();
            EnsureMaterialSelection();
        }

        private void EnsureMaterialSelection()
        {
            int count = sourceRenderer != null && sourceRenderer.sharedMesh != null ? sourceRenderer.sharedMesh.subMeshCount : 0;
            while (selectedMaterials.Count < count) selectedMaterials.Add(false);
            if (selectedMaterials.Count > count) selectedMaterials.RemoveRange(count, selectedMaterials.Count - count);
        }

        private void Generate()
        {
            if (!TryValidateGenerationInputs(out string error))
            {
                EditorUtility.DisplayDialog("SkinnedMesh Separator Validation Failed", error, "OK");
                return;
            }

            string prefabPath = EditorUtility.SaveFilePanelInProject("Save Separated Mesh Prefab", sourceRenderer.name + "_Separated.prefab", "prefab", "Choose output path.");
            if (string.IsNullOrEmpty(prefabPath)) return;
            if (!TryGenerateSeparatedMeshPrefab(prefabPath, out string meshPath, out error))
            {
                EditorUtility.DisplayDialog("SkinnedMesh Separator Generation Failed", error, "OK");
                return;
            }

            EditorUtility.DisplayDialog("SkinnedMesh Separated", $"Saved prefab:\n{prefabPath}\n\nSaved mesh:\n{meshPath}", "OK");
        }

        private bool TryGenerateSeparatedMeshPrefab(string prefabPath, out string meshPath, out string error)
        {
            meshPath = null;
            if (!TryValidateGenerationInputs(out error)) return false;

            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder))
            {
                error = "A valid output folder is required.";
                return false;
            }

            Mesh result = null;
            GameObject instance = null;
            try
            {
                result = BuildSeparatedMesh(sourceRenderer.sharedMesh, selectedMaterials);
                meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset").Replace('\\', '/'));
                result.name = Path.GetFileNameWithoutExtension(meshPath);
                AssetDatabase.CreateAsset(result, meshPath);
                Transform sourceRoot = sourceRenderer.transform.root;
                instance = Instantiate(sourceRoot.gameObject);
                instance.name = Path.GetFileNameWithoutExtension(prefabPath);
                string rendererPath = BonePoseUtility.GetRelativePath(sourceRoot, sourceRenderer.transform);
                Transform rendererTransform = string.IsNullOrEmpty(rendererPath) ? instance.transform : instance.transform.Find(rendererPath);
                SkinnedMeshRenderer renderer = rendererTransform != null ? rendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null) throw new InvalidOperationException("Could not resolve the cloned source renderer.");
                renderer.sharedMesh = result;
                renderer.name = Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh";
                renderer.bones = RemapBones(sourceRenderer, sourceRoot, instance.transform);
                renderer.rootBone = RemapTransform(sourceRenderer.rootBone, sourceRoot, instance.transform);
                if (renderer.rootBone == null) throw new InvalidOperationException("Could not resolve the cloned root bone.");
                renderer.sharedMaterials = GetSelectedMaterials(sourceRenderer.sharedMaterials, selectedMaterials);
                renderer.localBounds = sourceRenderer.localBounds;
                RemoveOtherSkinnedMeshRenderers(instance.transform, renderer);
                PruneToSkinningHierarchy(instance.transform, renderer);
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Unity failed to save the separated prefab.");
                AssetDatabase.SaveAssets();
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                if (!string.IsNullOrEmpty(meshPath)) AssetDatabase.DeleteAsset(meshPath);
                if (result != null && !AssetDatabase.Contains(result)) DestroyImmediate(result);
                meshPath = null;
                error = exception.Message;
                return false;
            }
            finally { if (instance != null) DestroyImmediate(instance); }
        }

        private bool TryValidateGenerationInputs(out string error)
        {
            error = null;
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
            {
                error = "Source Renderer and its Mesh are required.";
                return false;
            }

            EnsureMaterialSelection();
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            Material[] materials = sourceRenderer.sharedMaterials;
            if (materials.Length != sourceMesh.subMeshCount)
            {
                error = $"Material/submesh count differs (Materials {materials.Length}, SubMeshes {sourceMesh.subMeshCount}).";
                return false;
            }

            bool hasSelection = false;
            for (int i = 0; i < selectedMaterials.Count; i++)
            {
                if (materials[i] == null)
                {
                    error = $"Material slot {i} is missing.";
                    return false;
                }

                if (!selectedMaterials[i]) continue;
                hasSelection = true;
                if (sourceMesh.GetTriangles(i).Length == 0)
                {
                    error = $"Selected material slot {i} does not contain any triangles.";
                    return false;
                }
            }

            if (!hasSelection)
            {
                error = "Select at least one material.";
                return false;
            }

            if (!IsInSourceHierarchy(sourceRenderer.rootBone, sourceRenderer.transform.root))
            {
                error = "Source rootBone is outside the source hierarchy.";
                return false;
            }

            Transform[] bones = sourceRenderer.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!IsInSourceHierarchy(bones[i], sourceRenderer.transform.root))
                {
                    error = $"Source bone at index {i} is outside the source hierarchy.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsInSourceHierarchy(Transform target, Transform root)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                if (current == root) return true;
            }

            return false;
        }

        private static Mesh BuildSeparatedMesh(Mesh source, IReadOnlyList<bool> selected)
        {
            if (source == null) throw new InvalidOperationException("Source mesh is null.");
            int[] remap = new int[source.vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            List<int[]> triangles = new List<int[]>();
            List<int> selectedSubMeshes = new List<int>();
            int nextVertex = 0;
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                if (subMesh >= selected.Count || !selected[subMesh]) continue;
                int[] sourceTriangles = source.GetTriangles(subMesh);
                int[] mapped = new int[sourceTriangles.Length];
                for (int i = 0; i < sourceTriangles.Length; i++)
                {
                    int sourceIndex = sourceTriangles[i];
                    if (remap[sourceIndex] < 0) remap[sourceIndex] = nextVertex++;
                    mapped[i] = remap[sourceIndex];
                }
                selectedSubMeshes.Add(subMesh);
                triangles.Add(mapped);
            }
            if (nextVertex == 0) throw new InvalidOperationException("Selected materials do not contain any triangles.");
            Mesh result = new Mesh { indexFormat = nextVertex > ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            result.vertices = Remap(source.vertices, remap, nextVertex);
            if (source.normals.Length == source.vertexCount) result.normals = Remap(source.normals, remap, nextVertex);
            if (source.tangents.Length == source.vertexCount) result.tangents = Remap(source.tangents, remap, nextVertex);
            if (source.colors.Length == source.vertexCount) result.colors = Remap(source.colors, remap, nextVertex);
            if (source.uv.Length == source.vertexCount) result.uv = Remap(source.uv, remap, nextVertex);
            if (source.uv2.Length == source.vertexCount) result.uv2 = Remap(source.uv2, remap, nextVertex);
            if (source.boneWeights.Length == source.vertexCount) result.boneWeights = Remap(source.boneWeights, remap, nextVertex);
            result.bindposes = source.bindposes;
            result.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++) result.SetTriangles(triangles[i], i, false);
            CopyBlendShapes(source, result, remap, nextVertex);
            result.RecalculateBounds();
            return result;
        }

        private static T[] Remap<T>(T[] source, int[] remap, int count)
        {
            T[] result = new T[count];
            for (int i = 0; i < remap.Length; i++) if (remap[i] >= 0) result[remap[i]] = source[i];
            return result;
        }

        private static void CopyBlendShapes(Mesh source, Mesh result, int[] remap, int count)
        {
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
            {
                Vector3[] vertices = new Vector3[source.vertexCount]; Vector3[] normals = new Vector3[source.vertexCount]; Vector3[] tangents = new Vector3[source.vertexCount];
                source.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                result.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame), Remap(vertices, remap, count), Remap(normals, remap, count), Remap(tangents, remap, count));
            }
        }

        private static Material[] GetSelectedMaterials(Material[] materials, IReadOnlyList<bool> selected)
        {
            List<Material> result = new List<Material>();
            for (int i = 0; i < selected.Count; i++) if (selected[i]) result.Add(i < materials.Length ? materials[i] : null);
            return result.ToArray();
        }

        private static Transform[] RemapBones(SkinnedMeshRenderer sourceRenderer, Transform sourceRoot, Transform clonedRoot)
        {
            Transform[] sourceBones = sourceRenderer.bones;
            Transform[] result = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                result[i] = RemapTransform(sourceBones[i], sourceRoot, clonedRoot);
                if (result[i] == null) throw new InvalidOperationException($"Could not resolve cloned bone at index {i}.");
            }

            return result;
        }

        private static Transform RemapTransform(Transform source, Transform sourceRoot, Transform clonedRoot)
        {
            if (source == null) return null;
            string path = BonePoseUtility.GetRelativePath(sourceRoot, source);
            return string.IsNullOrEmpty(path) ? clonedRoot : clonedRoot.Find(path);
        }

        private static void RemoveOtherSkinnedMeshRenderers(Transform root, SkinnedMeshRenderer keptRenderer)
        {
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != keptRenderer) DestroyImmediate(renderers[i]);
            }
        }

        private static void PruneToSkinningHierarchy(Transform prefabRoot, SkinnedMeshRenderer renderer)
        {
            HashSet<Transform> required = new HashSet<Transform>();
            AddRequiredPath(renderer.transform, prefabRoot, required);
            AddRequiredPath(renderer.rootBone, prefabRoot, required);
            Transform[] bones = renderer.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                AddRequiredPath(bones[i], prefabRoot, required);
            }

            Transform[] transforms = prefabRoot.GetComponentsInChildren<Transform>(true);
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                Transform candidate = transforms[i];
                if (candidate != prefabRoot && !required.Contains(candidate))
                {
                    DestroyImmediate(candidate.gameObject);
                }
            }
        }

        private static void AddRequiredPath(Transform target, Transform prefabRoot, HashSet<Transform> required)
        {
            Transform current = target;
            while (current != null)
            {
                required.Add(current);
                if (current == prefabRoot) return;
                current = current.parent;
            }

            throw new InvalidOperationException("A remapped skinning transform is outside the generated prefab hierarchy.");
        }
    }
}
