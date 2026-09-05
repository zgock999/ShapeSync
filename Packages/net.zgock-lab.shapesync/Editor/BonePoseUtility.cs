// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Captures and applies serialized local bone pose data in editor workflows.</summary>
    public static class BonePoseUtility
    {
    #if SHAPESYNC_DEBUG
        [MenuItem("Tools/zgock/ShapeSync/Create Bone Registry From Selected Animator")]
    #endif
        public static void CreateFromSelectedAnimator()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Select a GameObject with an Animator.", "OK");
                return;
            }

            Animator animator = selected.GetComponentInParent<Animator>();
            if (animator == null)
            {
                animator = selected.GetComponentInChildren<Animator>();
            }

            if (animator == null)
            {
                EditorUtility.DisplayDialog("Animator Not Found", "Could not find an Animator on the selected object or its hierarchy.", "OK");
                return;
            }

            CharacterBoneRegistry registry = ExtractFromAnimator(animator);
            SaveRegistry(registry, animator.gameObject.name + "_BoneRegistry.asset");
        }

    #if SHAPESYNC_DEBUG
        [MenuItem("Tools/zgock/ShapeSync/Create Bone Registry From Selected Skinned Meshes")]
    #endif
        public static void CreateFromSelectedSkinnedMeshes()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Select a GameObject with SkinnedMeshRenderer components.", "OK");
                return;
            }

            SkinnedMeshRenderer[] renderers = selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                EditorUtility.DisplayDialog("SkinnedMeshRenderer Not Found", "Could not find SkinnedMeshRenderer components under the selected object.", "OK");
                return;
            }

            CharacterBoneRegistry registry = ExtractFromSkinnedMeshRenderers(selected.transform, renderers);
            SaveRegistry(registry, selected.name + "_BoneRegistry.asset");
        }

        public static CharacterBoneRegistry ExtractFromAnimator(Animator animator)
        {
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            if (animator == null)
            {
                return registry;
            }

            Transform root = animator.transform;
            HashSet<Transform> visited = new HashSet<Transform>();

            if (animator.isHuman && animator.avatar != null && animator.avatar.isValid)
            {
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                    AddBonePose(registry, root, bone, visited);
                }
            }

            SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            AddRendererBones(registry, root, renderers, visited);
            return registry;
        }

        public static CharacterBoneRegistry ExtractFromSkinnedMeshRenderers(Transform root, SkinnedMeshRenderer[] renderers)
        {
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            if (root == null || renderers == null)
            {
                return registry;
            }

            HashSet<Transform> visited = new HashSet<Transform>();
            AddRendererBones(registry, root, renderers, visited);
            return registry;
        }

        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return string.Empty;
            }

            if (root == target)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return target.name;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void OverwriteRegistry(CharacterBoneRegistry targetRegistry, CharacterBoneRegistry sourceRegistry)
        {
            if (targetRegistry == null || sourceRegistry == null)
            {
                return;
            }

            targetRegistry.bonePoses.Clear();
            for (int i = 0; i < sourceRegistry.bonePoses.Count; i++)
            {
                BonePoseData source = sourceRegistry.bonePoses[i];
                targetRegistry.bonePoses.Add(new BonePoseData
                {
                    boneName = source.boneName,
                    localPosition = source.localPosition,
                    localRotation = source.localRotation,
                    localScale = source.localScale,
                    bindposeIndex = source.bindposeIndex,
                    hasBindpose = source.hasBindpose,
                    bindpose = source.bindpose
                });
            }

            EditorUtility.SetDirty(targetRegistry);
            AssetDatabase.SaveAssets();
        }

        private static void AddRendererBones(CharacterBoneRegistry registry, Transform root, SkinnedMeshRenderer[] renderers, HashSet<Transform> visited)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || renderer.bones == null)
                {
                    continue;
                }

                Matrix4x4[] bindposes = renderer.sharedMesh != null ? renderer.sharedMesh.bindposes : null;
                for (int j = 0; j < renderer.bones.Length; j++)
                {
                    bool hasBindpose = bindposes != null && j < bindposes.Length;
                    AddBonePose(registry, root, renderer.bones[j], visited, j, hasBindpose, hasBindpose ? bindposes[j] : Matrix4x4.identity);
                }

                AddBonePose(registry, root, renderer.rootBone, visited, -1, false, Matrix4x4.identity);
            }
        }

        private static void AddBonePose(CharacterBoneRegistry registry, Transform root, Transform bone, HashSet<Transform> visited)
        {
            AddBonePose(registry, root, bone, visited, -1, false, Matrix4x4.identity);
        }

        private static void AddBonePose(CharacterBoneRegistry registry, Transform root, Transform bone, HashSet<Transform> visited, int bindposeIndex, bool hasBindpose, Matrix4x4 bindpose)
        {
            if (registry == null || root == null || bone == null)
            {
                return;
            }

            string boneName = GetRelativePath(root, bone);
            BonePoseData existing = FindBonePose(registry, boneName);
            if (existing != null)
            {
                if (hasBindpose && !existing.hasBindpose)
                {
                    existing.bindposeIndex = bindposeIndex;
                    existing.hasBindpose = true;
                    existing.bindpose = bindpose;
                }
                return;
            }

            visited.Add(bone);
            registry.bonePoses.Add(new BonePoseData
            {
                boneName = boneName,
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale,
                bindposeIndex = bindposeIndex,
                hasBindpose = hasBindpose,
                bindpose = bindpose
            });
        }

        private static BonePoseData FindBonePose(CharacterBoneRegistry registry, string boneName)
        {
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose != null && pose.boneName == boneName)
                {
                    return pose;
                }
            }

            return null;
        }

        private static void SaveRegistry(CharacterBoneRegistry registry, string defaultName)
        {
            if (registry == null)
            {
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Character Bone Registry",
                defaultName,
                "asset",
                "Choose where to save the CharacterBoneRegistry asset.");

            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(registry);
                return;
            }

            AssetDatabase.CreateAsset(registry, AssetDatabase.GenerateUniqueAssetPath(path));
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = registry;
        }
    }
}

