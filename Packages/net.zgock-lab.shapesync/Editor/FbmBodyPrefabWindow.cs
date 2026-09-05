// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Exports an FBM body prefab from a selected ShapeSync Figure.</summary>
    public sealed class FbmBodyPrefabWindow : EditorWindow
    {
        [SerializeField] private GameObject sourceFigureRoot;
        [SerializeField] private string selectedFbmName;

        public void DrawFbmBodyPrefabContent()
        {
            EditorGUILayout.LabelField("FBM Body Prefab", EditorStyles.boldLabel);
            sourceFigureRoot = (GameObject)EditorGUILayout.ObjectField("ShapeSync Figure Root", sourceFigureRoot, typeof(GameObject), true);
            EditorGUILayout.HelpBox(
                "Creates a static Humanoid body Prefab for one normal FBM. The output has the FBM visual shape, target registry bone pose, and matching captured bindposes. Use the output and its posed duplicate as the Source and Current Pose roots in BC Profile Builder. PBM targets are excluded.",
                MessageType.Info);

            IReadOnlyList<string> names = GetNormalFbmNames(sourceFigureRoot);
            int selectedIndex = Array.IndexOf(ToArray(names), selectedFbmName);
            selectedIndex = EditorGUILayout.Popup("FBM", selectedIndex, ToArray(names));
            selectedFbmName = selectedIndex >= 0 && selectedIndex < names.Count ? names[selectedIndex] : null;

            using (new EditorGUI.DisabledScope(sourceFigureRoot == null || string.IsNullOrEmpty(selectedFbmName)))
            {
                if (GUILayout.Button("Generate FBM Body Prefab", GUILayout.Height(34f)))
                {
                    GenerateFbmBodyPrefab();
                }
            }
        }

        internal static IReadOnlyList<string> GetNormalFbmNames(GameObject figureRoot)
        {
            DynamicBoneBlender blender = figureRoot != null ? figureRoot.GetComponent<DynamicBoneBlender>() : null;
            if (blender == null || blender.Targets == null)
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>();
            HashSet<string> unique = new HashSet<string>();
            for (int i = 0; i < blender.Targets.Count; i++)
            {
                DynamicBoneBlendTarget target = blender.Targets[i];
                if (target == null
                    || string.IsNullOrWhiteSpace(target.blendName)
                    || target.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal)
                    || !unique.Add(target.blendName))
                {
                    continue;
                }

                names.Add(target.blendName);
            }

            return names;
        }

        private void GenerateFbmBodyPrefab()
        {
            if (!TryResolveInputs(out DynamicBoneBlendTarget target, out SkinnedMeshRenderer sourceRenderer, out Animator sourceAnimator, out string error))
            {
                EditorUtility.DisplayDialog("Generate FBM Body Prefab Failed", error, "OK");
                return;
            }

            string prefabPath = EditorUtility.SaveFilePanelInProject(
                "Save FBM Body Prefab",
                sourceFigureRoot.name + "_" + selectedFbmName + "_Body",
                "prefab",
                "Choose the generated FBM Body Prefab path.");
            if (string.IsNullOrEmpty(prefabPath))
            {
                return;
            }

            string folder = System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string meshPath = folder + "/" + System.IO.Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset";
            GameObject clone = null;
            Mesh bakedMesh = null;
            Mesh targetBindposeMesh = null;
            string createdMeshPath = null;
            string createdPrefabPath = null;
            try
            {
                clone = Instantiate(sourceFigureRoot);
                clone.name = sourceFigureRoot.name;
                clone.hideFlags = HideFlags.HideAndDontSave;

                string rendererPath = BonePoseUtility.GetRelativePath(sourceFigureRoot.transform, sourceRenderer.transform);
                Transform rendererTransform = string.IsNullOrEmpty(rendererPath) ? clone.transform : clone.transform.Find(rendererPath);
                SkinnedMeshRenderer cloneRenderer = rendererTransform != null ? rendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
                Animator cloneAnimator = clone.GetComponentInChildren<Animator>(true);
                if (cloneRenderer == null || cloneAnimator == null)
                {
                    throw new InvalidOperationException("Could not resolve the source SkinnedMeshRenderer and Humanoid Animator in the temporary Figure clone.");
                }

                cloneAnimator.avatar = target.targetAvatar;
                cloneAnimator.Rebind();
                if (!TryApplyRegistryPose(clone.transform, target.targetRegistry, out error))
                {
                    throw new InvalidOperationException(error);
                }

                targetBindposeMesh = ShapeSyncMeshCloneUtility.Clone(cloneRenderer.sharedMesh);
                targetBindposeMesh.name = cloneRenderer.sharedMesh.name + " (FBM Body Source)";
                if (!TryApplyRegistryBindposes(targetBindposeMesh, target.targetRegistry, out error))
                {
                    throw new InvalidOperationException(error);
                }

                cloneRenderer.sharedMesh = targetBindposeMesh;
                SetOnlyFbmWeight(cloneRenderer, selectedFbmName);
                bakedMesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh" };
                cloneRenderer.BakeMesh(bakedMesh);
                if (!ExternalTransferWindow.TryConfigureCapturedPoseSkinning(cloneRenderer, cloneRenderer.sharedMesh, bakedMesh, out error))
                {
                    throw new InvalidOperationException(error);
                }

                createdMeshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
                AssetDatabase.CreateAsset(bakedMesh, createdMeshPath);
                bakedMesh = null;
                cloneRenderer.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(createdMeshPath);
                RemoveDynamicFigureComponents(clone);
                ClearHideFlagsForPrefabSave(clone);
                createdPrefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(clone, createdPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(createdPrefabPath);
                EditorUtility.DisplayDialog(
                    "FBM Body Prefab Generated",
                    $"Generated '{selectedFbmName}' FBM Body Prefab. Duplicate it in the Scene, pose the duplicate, then use the original and duplicate as the Source and Current Pose roots in BC Profile Builder.\n\nPrefab:\n{createdPrefabPath}\n\nMesh:\n{createdMeshPath}",
                    "OK");
            }
            catch (Exception exception)
            {
                if (!string.IsNullOrEmpty(createdMeshPath))
                {
                    AssetDatabase.DeleteAsset(createdMeshPath);
                }

                EditorUtility.DisplayDialog("Generate FBM Body Prefab Failed", exception.Message, "OK");
            }
            finally
            {
                if (bakedMesh != null)
                {
                    DestroyImmediate(bakedMesh);
                }

                if (targetBindposeMesh != null)
                {
                    DestroyImmediate(targetBindposeMesh);
                }

                if (clone != null)
                {
                    DestroyImmediate(clone);
                }
            }
        }

        private bool TryResolveInputs(
            out DynamicBoneBlendTarget resolvedTarget,
            out SkinnedMeshRenderer sourceRenderer,
            out Animator sourceAnimator,
            out string error)
        {
            resolvedTarget = null;
            sourceRenderer = null;
            sourceAnimator = null;
            error = null;
            DynamicBoneBlender blender = sourceFigureRoot != null ? sourceFigureRoot.GetComponent<DynamicBoneBlender>() : null;
            if (blender == null || blender.Targets == null)
            {
                error = "ShapeSync Figure Root must contain DynamicBoneBlender targets.";
                return false;
            }

            sourceRenderer = blender.TargetSkinnedMeshRenderer != null
                ? blender.TargetSkinnedMeshRenderer
                : sourceFigureRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            sourceAnimator = sourceFigureRoot.GetComponentInChildren<Animator>(true);
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null || sourceAnimator == null)
            {
                error = "ShapeSync Figure Root requires a SkinnedMeshRenderer with a shared Mesh and a Humanoid Animator.";
                return false;
            }

            for (int i = 0; i < blender.Targets.Count; i++)
            {
                DynamicBoneBlendTarget candidate = blender.Targets[i];
                if (candidate != null && candidate.blendName == selectedFbmName)
                {
                    resolvedTarget = candidate;
                    break;
                }
            }

            if (resolvedTarget == null
                || string.IsNullOrWhiteSpace(resolvedTarget.blendName)
                || resolvedTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
            {
                error = "Select a normal FBM target configured on ShapeSync Figure Root.";
                return false;
            }

            if (resolvedTarget.targetAvatar == null || !resolvedTarget.targetAvatar.isHuman || resolvedTarget.targetRegistry == null)
            {
                error = $"FBM '{selectedFbmName}' requires a valid Humanoid target Avatar and CharacterBoneRegistry.";
                return false;
            }

            if (sourceRenderer.sharedMesh.GetBlendShapeIndex(selectedFbmName) < 0)
            {
                error = $"ShapeSync Figure Mesh does not contain FBM BlendShape '{selectedFbmName}'.";
                return false;
            }

            return true;
        }

        internal static bool TryApplyRegistryPose(Transform root, CharacterBoneRegistry registry, out string error)
        {
            error = null;
            if (root == null || registry == null || registry.bonePoses == null || registry.bonePoses.Count == 0)
            {
                error = "FBM target requires a non-empty CharacterBoneRegistry.";
                return false;
            }

            int applied = 0;
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose == null)
                {
                    continue;
                }

                Transform bone = string.IsNullOrEmpty(pose.boneName) ? root : root.Find(pose.boneName);
                if (bone == null)
                {
                    continue;
                }

                bone.localPosition = pose.localPosition;
                bone.localRotation = pose.localRotation;
                bone.localScale = pose.localScale;
                applied++;
            }

            if (applied == 0)
            {
                error = "FBM CharacterBoneRegistry did not resolve any bones below ShapeSync Figure Root.";
                return false;
            }

            return true;
        }

        internal static bool TryApplyRegistryBindposes(Mesh mesh, CharacterBoneRegistry registry, out string error)
        {
            error = null;
            if (mesh == null || registry == null || registry.bonePoses == null)
            {
                error = "FBM Body Prefab requires a Mesh and CharacterBoneRegistry.";
                return false;
            }

            Matrix4x4[] bindposes = mesh.bindposes;
            if (bindposes == null || bindposes.Length == 0)
            {
                error = "ShapeSync Figure Mesh has no bindposes.";
                return false;
            }

            bool[] assigned = new bool[bindposes.Length];
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose == null || !pose.hasBindpose || pose.bindposeIndex < 0 || pose.bindposeIndex >= bindposes.Length)
                {
                    continue;
                }

                bindposes[pose.bindposeIndex] = pose.bindpose;
                assigned[pose.bindposeIndex] = true;
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                if (!assigned[i])
                {
                    error = $"FBM CharacterBoneRegistry does not provide bindpose index {i}.";
                    return false;
                }
            }

            mesh.bindposes = bindposes;
            return true;
        }

        private static void SetOnlyFbmWeight(SkinnedMeshRenderer renderer, string fbmName)
        {
            Mesh mesh = renderer.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                renderer.SetBlendShapeWeight(i, 0f);
            }

            renderer.SetBlendShapeWeight(mesh.GetBlendShapeIndex(fbmName), 100f);
        }

        private static void RemoveDynamicFigureComponents(GameObject root)
        {
            DynamicBoneBlender[] blenders = root.GetComponentsInChildren<DynamicBoneBlender>(true);
            for (int i = 0; i < blenders.Length; i++) DestroyImmediate(blenders[i]);
            DynamicMorphAdapter[] adapters = root.GetComponentsInChildren<DynamicMorphAdapter>(true);
            for (int i = 0; i < adapters.Length; i++) DestroyImmediate(adapters[i]);
            UniversalExpressionProxy[] expressions = root.GetComponentsInChildren<UniversalExpressionProxy>(true);
            for (int i = 0; i < expressions.Length; i++) DestroyImmediate(expressions[i]);
            FigureMorphSyncCoordinator[] coordinators = root.GetComponentsInChildren<FigureMorphSyncCoordinator>(true);
            for (int i = 0; i < coordinators.Length; i++) DestroyImmediate(coordinators[i]);
        }

        private static void ClearHideFlagsForPrefabSave(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject gameObject = transforms[transformIndex].gameObject;
                gameObject.hideFlags = HideFlags.None;
                Component[] components = gameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (components[componentIndex] != null)
                    {
                        components[componentIndex].hideFlags = HideFlags.None;
                    }
                }
            }
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }
    }
}
