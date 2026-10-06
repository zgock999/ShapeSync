// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Optional UniVRM physics integration for a Figure root.
    /// Registers with the figure lifecycle without using a global singleton.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeSyncVrmIntegrationAdapter : MonoBehaviour, IShapeSyncOptionalVrmIntegration, IShapeSyncOptionalVrmIntegrationDryRun
    {
        private Vrm10Instance figureInstance;
        private readonly Dictionary<string, ExpressionKey> expressionKeys = new Dictionary<string, ExpressionKey>();

        private void Awake()
        {
            CacheExpressionKeys();
        }

        private void OnEnable()
        {
            CacheExpressionKeys();
            EnsureRegistered();
        }

        private void OnDisable()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Unregister(gameObject, this);
        }

        private void OnDestroy()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Unregister(gameObject, this);
        }

        public void EnsureRegistered()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Register(gameObject, this);
        }

        public bool TrySetExpressionWeight(string expressionName, float weight)
        {
            if (!isActiveAndEnabled || string.IsNullOrEmpty(expressionName)
                || !expressionKeys.TryGetValue(expressionName, out ExpressionKey key)
                || figureInstance == null || figureInstance.Runtime == null || figureInstance.Runtime.Expression == null)
            {
                return false;
            }

            figureInstance.Runtime.Expression.SetWeight(key, weight);
            return true;
        }

        public bool TryAttachOutfitPhysics(ShapeSyncOptionalVrmAttachRequest request, out IShapeSyncOptionalVrmAttachment attachment, out string error)
        {
            attachment = null;
            error = null;
            if (request.FigureRoot != gameObject || request.RuntimeOutfitRoot == null)
            {
                error = "The VRM integration adapter does not own this FigureRoot.";
                return false;
            }

            ShapeSyncOutfitSpringBoneData sourceData = request.RuntimeOutfitRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (sourceData == null || sourceData.Springs == null || sourceData.Springs.Count == 0)
            {
                // This is a normal non-VRM Outfit.  The core caller must not know
                // UniVRM types just to distinguish this from a transport failure.
                return true;
            }
            if (!ShapeSyncVrmInstanceUtility.TryGetOrCreateFigureInstance(request.FigureRoot, request.FigureAnimator, out Vrm10Instance figure, out error)) return false;

            ShapeSyncVrmSpringBoneAttachment concrete;
            bool created = ShapeSyncVrmSpringBoneAttachment.TryCreate(sourceData.ColliderGroups, sourceData.Springs, sourceData.SpringColliderGroupNames, request.RuntimeOutfitRoot.transform, figure, request.TransformMapper, out concrete, out error);
            if (!created) return false;
            attachment = concrete;
            return true;
        }

        /// <inheritdoc />
        public bool TryValidateOutfitPhysics(ShapeSyncOptionalVrmDryRunRequest request, out string error)
        {
            error = null;
            if (request.FigureRoot != gameObject || request.OutfitSourceRoot == null)
            {
                error = "The VRM integration adapter does not own this FigureRoot.";
                return false;
            }
            ShapeSyncOutfitSpringBoneData sourceData = request.OutfitSourceRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (sourceData == null || sourceData.Springs == null || sourceData.Springs.Count == 0) return true;
            if (!isActiveAndEnabled || request.FigureAnimator == null)
            {
                error = "VRM Outfit physics requires an active Figure integration and Animator.";
                return false;
            }
            return true;
        }

        private const float SharedPhysicsTolerance = 1e-6f;

        private sealed class SharedRootSpring
        {
            internal Vrm10InstanceSpringBone.Spring Spring;
            internal string[] JointPaths;
            internal string CenterPath;
            internal List<string> GroupNames;
        }

        /// <inheritdoc />
        public bool TryCompareSharedRootPhysics(GameObject existingOutfitSourceRoot, GameObject candidateOutfitSourceRoot, string rootPath, out string error)
        {
            error = null;
            if (existingOutfitSourceRoot == null || candidateOutfitSourceRoot == null || string.IsNullOrEmpty(rootPath))
            {
                error = "Shared Extra Bone physics comparison requires both Outfit sources and a root path.";
                return false;
            }

            List<SharedRootSpring> existing = CollectSharedRootSprings(existingOutfitSourceRoot, rootPath);
            List<SharedRootSpring> candidate = CollectSharedRootSprings(candidateOutfitSourceRoot, rootPath);
            if (existing.Count != candidate.Count)
            {
                error = $"Spring count under '{rootPath}' differs ({existing.Count} / {candidate.Count}).";
                return false;
            }

            var matched = new bool[candidate.Count];
            for (int existingIndex = 0; existingIndex < existing.Count; existingIndex++)
            {
                int candidateIndex = FindSameChain(existing[existingIndex], candidate, matched);
                if (candidateIndex < 0)
                {
                    error = $"Spring chain starting at '{existing[existingIndex].JointPaths[0]}' has no identical chain in the new Outfit.";
                    return false;
                }
                matched[candidateIndex] = true;
                if (!TryCompareSharedRootSpring(existing[existingIndex], candidate[candidateIndex], out error)) return false;
            }
            return true;
        }

        private static List<SharedRootSpring> CollectSharedRootSprings(GameObject outfitRoot, string rootPath)
        {
            var result = new List<SharedRootSpring>();
            ShapeSyncOutfitSpringBoneData data = outfitRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (data == null || data.Springs == null) return result;
            for (int springIndex = 0; springIndex < data.Springs.Count; springIndex++)
            {
                Vrm10InstanceSpringBone.Spring spring = data.Springs[springIndex];
                if (spring == null || spring.Joints == null || spring.Joints.Count == 0) continue;
                var jointPaths = new string[spring.Joints.Count];
                bool underRoot = false;
                for (int jointIndex = 0; jointIndex < spring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint joint = spring.Joints[jointIndex];
                    jointPaths[jointIndex] = joint != null ? GetRelativePath(outfitRoot.transform, joint.transform) : null;
                    if (IsPathUnderRoot(jointPaths[jointIndex], rootPath)) underRoot = true;
                }
                if (!underRoot) continue;

                var groupNames = new List<string>();
                for (int groupIndex = 0; spring.ColliderGroups != null && groupIndex < spring.ColliderGroups.Count; groupIndex++)
                {
                    groupNames.Add(spring.ColliderGroups[groupIndex] != null ? spring.ColliderGroups[groupIndex].Name : null);
                }
                if (data.SpringColliderGroupNames != null && springIndex < data.SpringColliderGroupNames.Count && data.SpringColliderGroupNames[springIndex] != null)
                {
                    groupNames.AddRange(data.SpringColliderGroupNames[springIndex]);
                }

                result.Add(new SharedRootSpring
                {
                    Spring = spring,
                    JointPaths = jointPaths,
                    CenterPath = spring.Center != null ? GetRelativePath(outfitRoot.transform, spring.Center) : null,
                    GroupNames = groupNames
                });
            }
            return result;
        }

        private static int FindSameChain(SharedRootSpring existing, List<SharedRootSpring> candidates, bool[] matched)
        {
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (matched[candidateIndex] || candidates[candidateIndex].JointPaths.Length != existing.JointPaths.Length) continue;
                bool same = true;
                for (int i = 0; i < existing.JointPaths.Length && same; i++) same = existing.JointPaths[i] == candidates[candidateIndex].JointPaths[i];
                if (same) return candidateIndex;
            }
            return -1;
        }

        private static bool TryCompareSharedRootSpring(SharedRootSpring existing, SharedRootSpring candidate, out string error)
        {
            error = null;
            string chain = existing.JointPaths[0];
            if (existing.CenterPath != candidate.CenterPath)
            {
                error = $"Spring chain starting at '{chain}' has a different center.";
                return false;
            }
            if (existing.GroupNames.Count != candidate.GroupNames.Count)
            {
                error = $"Spring chain starting at '{chain}' references different collider groups.";
                return false;
            }
            for (int i = 0; i < existing.GroupNames.Count; i++)
            {
                if (existing.GroupNames[i] != candidate.GroupNames[i])
                {
                    error = $"Spring chain starting at '{chain}' references different collider groups.";
                    return false;
                }
            }
            for (int jointIndex = 0; jointIndex < existing.Spring.Joints.Count; jointIndex++)
            {
                VRM10SpringBoneJoint a = existing.Spring.Joints[jointIndex];
                VRM10SpringBoneJoint b = candidate.Spring.Joints[jointIndex];
                string field = FindDifferentJointField(a, b);
                if (field != null)
                {
                    error = $"Spring joint '{existing.JointPaths[jointIndex]}' parameter '{field}' differs.";
                    return false;
                }
            }
            return true;
        }

        private static string FindDifferentJointField(VRM10SpringBoneJoint a, VRM10SpringBoneJoint b)
        {
            if (a == null || b == null) return a == b ? null : "joint";
            if (!SameFloat(a.m_stiffnessForce, b.m_stiffnessForce)) return "m_stiffnessForce";
            if (!SameFloat(a.m_gravityPower, b.m_gravityPower)) return "m_gravityPower";
            if (!SameFloat(a.m_gravityDir.x, b.m_gravityDir.x) || !SameFloat(a.m_gravityDir.y, b.m_gravityDir.y) || !SameFloat(a.m_gravityDir.z, b.m_gravityDir.z)) return "m_gravityDir";
            if (!SameFloat(a.m_dragForce, b.m_dragForce)) return "m_dragForce";
            if (!SameFloat(a.m_jointRadius, b.m_jointRadius)) return "m_jointRadius";
            if (a.m_anglelimitType != b.m_anglelimitType) return "m_anglelimitType";
            if (!SameFloat(a.m_limitSpaceOffset.x, b.m_limitSpaceOffset.x) || !SameFloat(a.m_limitSpaceOffset.y, b.m_limitSpaceOffset.y)
                || !SameFloat(a.m_limitSpaceOffset.z, b.m_limitSpaceOffset.z) || !SameFloat(a.m_limitSpaceOffset.w, b.m_limitSpaceOffset.w)) return "m_limitSpaceOffset";
            if (!SameFloat(a.m_pitch, b.m_pitch)) return "m_pitch";
            if (!SameFloat(a.m_yaw, b.m_yaw)) return "m_yaw";
            return null;
        }

        private static bool SameFloat(float a, float b) => Mathf.Abs(a - b) <= SharedPhysicsTolerance;

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            return !string.IsNullOrEmpty(path) && (path == rootPath || path.StartsWith(rootPath + "/", StringComparison.Ordinal));
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", segments) : null;
        }


        private void CacheExpressionKeys()
        {
            figureInstance = GetComponent<Vrm10Instance>();
            expressionKeys.Clear();
            if (figureInstance == null || figureInstance.Vrm == null || figureInstance.Vrm.Expression == null)
            {
                return;
            }

            foreach (var pair in figureInstance.Vrm.Expression.Clips)
            {
                if (pair.Clip == null)
                {
                    continue;
                }

                string name;
                ExpressionKey key;
                if (pair.Preset == ExpressionPreset.custom)
                {
                    name = pair.Clip.name;
                    key = ExpressionKey.CreateCustom(name);
                }
                else
                {
                    name = pair.Preset.ToString();
                    key = ExpressionKey.CreateFromPreset(pair.Preset);
                }

                if (!string.IsNullOrEmpty(name) && !expressionKeys.ContainsKey(name))
                {
                    expressionKeys.Add(name, key);
                }
                if (pair.Preset != ExpressionPreset.custom && !string.IsNullOrEmpty(pair.Clip.name) && !expressionKeys.ContainsKey(pair.Clip.name))
                {
                    // Source VRMs may give a standard preset a localized or authored
                    // asset name. The baked VRM_* shape follows that authored name.
                    expressionKeys.Add(pair.Clip.name, key);
                }
            }
        }
    }
}
#endif
