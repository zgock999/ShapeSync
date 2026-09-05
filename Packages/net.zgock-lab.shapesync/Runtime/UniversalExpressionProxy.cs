// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using R3;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Figure runtime component that applies authored expressions and forwards logical expression values to optional VRM integration.
    /// </summary>
    public class UniversalExpressionProxy : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer targetSkinnedMeshRenderer;
        [SerializeField] private DynamicBoneBlender dynamicBoneBlender;
        [SerializeField] private List<UniversalExpressionEntry> expressions = new List<UniversalExpressionEntry>();

        private const float WeightEpsilon = 0.0001f;
        private const string McmPrefix = zgock.ShapeSync.BlendShapeReservedPrefixes.Mcm;
        private const string VrmPrefix = zgock.ShapeSync.BlendShapeReservedPrefixes.Vrm;
        private const string MorphSlotPrefix = zgock.ShapeSync.BlendShapeReservedPrefixes.MorphSlot;

        private Mesh cachedMesh;
        private string[] fbmBlendNames = System.Array.Empty<string>();
        private float[] fbmWeights = System.Array.Empty<float>();
        private System.IDisposable blendWeightSubscription;
        private IShapeSyncOptionalVrmIntegration optionalVrmIntegration;
        private DynamicMorphAdapter dynamicMorphAdapter;

        public IReadOnlyList<UniversalExpressionEntry> Expressions => expressions;

        /// <summary>
        /// Configures the expression runtime references on a newly created Figure prefab.
        /// </summary>
        public void ConfigureForFigure(SkinnedMeshRenderer renderer, DynamicBoneBlender blender)
        {
            targetSkinnedMeshRenderer = renderer;
            dynamicBoneBlender = blender;
        }

        private void Reset()
        {
            targetSkinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            dynamicBoneBlender = GetComponent<DynamicBoneBlender>();
        }

        private void Start()
        {
            dynamicMorphAdapter = dynamicBoneBlender != null ? dynamicBoneBlender.DynamicMorphAdapter : GetComponent<DynamicMorphAdapter>();
            ResolveOptionalVrmIntegration();
            CacheFbmTargets();
            if (dynamicBoneBlender != null)
            {
                blendWeightSubscription = dynamicBoneBlender.FbmWeightChanged.Subscribe(OnFbmWeightChanged);
            }

            RebuildCacheIfNeeded();
            ApplyAll(true);
        }

        private void OnEnable()
        {
            ResolveOptionalVrmIntegration();
        }

        private void LateUpdate()
        {
            RebuildCacheIfNeeded();
            ApplyAll(false);
        }

        public void RebuildExpressionList()
        {
            Mesh mesh = targetSkinnedMeshRenderer != null ? targetSkinnedMeshRenderer.sharedMesh : null;
            if (mesh == null)
            {
                expressions.Clear();
                return;
            }

            Dictionary<string, UniversalExpressionEntry> previous = new Dictionary<string, UniversalExpressionEntry>();
            for (int i = 0; i < expressions.Count; i++)
            {
                UniversalExpressionEntry entry = expressions[i];
                if (entry != null && !string.IsNullOrEmpty(entry.blendShapeName) && !previous.ContainsKey(entry.blendShapeName))
                {
                    previous.Add(entry.blendShapeName, entry);
                }
            }

            HashSet<string> bodyBlendNames = BuildInferredBodyBlendNameSet(mesh);
            AddConfiguredFbmNames(bodyBlendNames);

            expressions.Clear();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShape = mesh.GetBlendShapeName(i);
                if (ShouldExcludeFromExpressionList(blendShape, bodyBlendNames))
                {
                    continue;
                }

                if (previous.TryGetValue(blendShape, out UniversalExpressionEntry existing))
                {
                    expressions.Add(existing);
                }
                else
                {
                    expressions.Add(new UniversalExpressionEntry { blendShapeName = blendShape });
                }
            }

            cachedMesh = null;
            RebuildCacheIfNeeded();
        }

        /// <summary>
        /// Clears authored Expression entries without inspecting the Figure mesh.
        /// Figure Builder uses this to keep an unbaked Figure free of inferred FBM entries.
        /// </summary>
        public void ClearExpressionList()
        {
            expressions.Clear();
            cachedMesh = null;
            RebuildCacheIfNeeded();
        }

        private void CacheFbmTargets()
        {
            if (dynamicBoneBlender == null || dynamicBoneBlender.Targets == null || dynamicBoneBlender.Targets.Count == 0)
            {
                fbmBlendNames = System.Array.Empty<string>();
                fbmWeights = System.Array.Empty<float>();
                return;
            }

            List<string> names = new List<string>(dynamicBoneBlender.Targets.Count);
            List<float> weights = new List<float>(dynamicBoneBlender.Targets.Count);
            for (int i = 0; i < dynamicBoneBlender.Targets.Count; i++)
            {
                DynamicBoneBlendTarget target = dynamicBoneBlender.Targets[i];
                if (target == null || string.IsNullOrEmpty(target.blendName))
                {
                    continue;
                }

                names.Add(target.blendName);
                weights.Add(target.enabled ? target.weight : 0f);
            }

            fbmBlendNames = names.ToArray();
            fbmWeights = weights.ToArray();
        }

        private HashSet<string> BuildInferredBodyBlendNameSet(Mesh mesh)
        {
            HashSet<string> bodyBlendNames = new HashSet<string>();
            if (mesh == null)
            {
                return bodyBlendNames;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShape = mesh.GetBlendShapeName(i);
                if (TryGetMcmBlendName(blendShape, out string bodyBlendName))
                {
                    bodyBlendNames.Add(bodyBlendName);
                }
            }

            return bodyBlendNames;
        }

        private void AddConfiguredFbmNames(HashSet<string> bodyBlendNames)
        {
            if (bodyBlendNames == null || fbmBlendNames == null)
            {
                return;
            }

            for (int i = 0; i < fbmBlendNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(fbmBlendNames[i]))
                {
                    bodyBlendNames.Add(fbmBlendNames[i]);
                }
            }
        }

        private bool ShouldExcludeFromExpressionList(string blendShape, HashSet<string> bodyBlendNames)
        {
            if (string.IsNullOrEmpty(blendShape)
                || blendShape.StartsWith(McmPrefix)
                || blendShape.StartsWith(zgock.ShapeSync.BlendShapeReservedPrefixes.Pcm)
                || blendShape.StartsWith(zgock.ShapeSync.BlendShapeReservedPrefixes.Pbm)
                || zgock.ShapeSync.BlendShapeReservedPrefixes.IsMorphSlot(blendShape))
            {
                return true;
            }

            return bodyBlendNames != null && bodyBlendNames.Contains(blendShape);
        }

        private bool TryGetMcmBlendName(string blendShape, out string bodyBlendName)
        {
            bodyBlendName = null;
            if (string.IsNullOrEmpty(blendShape) || !blendShape.StartsWith(McmPrefix))
            {
                return false;
            }

            int bodyBlendStart = McmPrefix.Length;
            int separatorIndex = blendShape.IndexOf('_', bodyBlendStart);
            if (separatorIndex <= bodyBlendStart || separatorIndex >= blendShape.Length - 1)
            {
                return false;
            }

            bodyBlendName = blendShape.Substring(bodyBlendStart, separatorIndex - bodyBlendStart);
            return !string.IsNullOrEmpty(bodyBlendName);
        }

        private void RebuildCacheIfNeeded()
        {
            if (targetSkinnedMeshRenderer == null)
            {
                targetSkinnedMeshRenderer = dynamicBoneBlender != null ? dynamicBoneBlender.TargetSkinnedMeshRenderer : GetComponentInChildren<SkinnedMeshRenderer>();
            }

            Mesh mesh = targetSkinnedMeshRenderer != null ? targetSkinnedMeshRenderer.sharedMesh : null;
            if (mesh == null || mesh == cachedMesh)
            {
                return;
            }

            cachedMesh = mesh;
            CacheFbmTargets();
            for (int i = 0; i < expressions.Count; i++)
            {
                UniversalExpressionEntry entry = expressions[i];
                if (entry == null || string.IsNullOrEmpty(entry.blendShapeName))
                {
                    continue;
                }

                entry.blendShapeIndex = mesh.GetBlendShapeIndex(entry.blendShapeName);
                entry.lastAppliedWeight = -1f;
                entry.vrmExpressionName = GetVrmExpressionName(entry.blendShapeName);
                entry.mcmBindings = BuildMcmBindings(mesh, entry.blendShapeName);
            }
        }

        private void ResolveOptionalVrmIntegration()
        {
            optionalVrmIntegration = null;
            ShapeSyncOptionalVrmIntegrationRegistry.TryGet(gameObject, out optionalVrmIntegration);
        }

        private bool TrySetOptionalVrmExpressionWeight(string expressionName, float weight)
        {
            return !string.IsNullOrEmpty(expressionName)
                && optionalVrmIntegration != null
                && optionalVrmIntegration.TrySetExpressionWeight(expressionName, weight);
        }

        private ExpressionMcmBinding[] BuildMcmBindings(Mesh mesh, string expressionBlendShapeName)
        {
            if (mesh == null || fbmBlendNames == null || fbmBlendNames.Length == 0)
            {
                return System.Array.Empty<ExpressionMcmBinding>();
            }

            List<ExpressionMcmBinding> bindings = new List<ExpressionMcmBinding>(fbmBlendNames.Length);
            string expressionName = GetExpressionName(expressionBlendShapeName);
            for (int i = 0; i < fbmBlendNames.Length; i++)
            {
                string blendName = fbmBlendNames[i];
                if (string.IsNullOrEmpty(blendName))
                {
                    continue;
                }

                int index = mesh.GetBlendShapeIndex(McmPrefix + blendName + "_" + expressionName);
                if (index < 0)
                {
                    continue;
                }

                bindings.Add(new ExpressionMcmBinding
                {
                    blendName = blendName,
                    blendShapeIndex = index,
                    lastAppliedWeight = -1f
                });
            }

            return bindings.ToArray();
        }

        private string GetExpressionName(string expressionBlendShapeName)
        {
            string vrmExpressionName = GetVrmExpressionName(expressionBlendShapeName);
            return string.IsNullOrEmpty(vrmExpressionName) ? expressionBlendShapeName : vrmExpressionName;
        }

        private string GetVrmExpressionName(string expressionBlendShapeName)
        {
            return !string.IsNullOrEmpty(expressionBlendShapeName) && expressionBlendShapeName.StartsWith(VrmPrefix)
                ? expressionBlendShapeName.Substring(VrmPrefix.Length)
                : null;
        }

        private void OnFbmWeightChanged(FbmWeightChange change)
        {
            for (int i = 0; i < fbmBlendNames.Length; i++)
            {
                if (fbmBlendNames[i] == change.BlendName)
                {
                    fbmWeights[i] = change.Enabled ? change.Weight : 0f;
                    ApplyAll(false);
                    return;
                }
            }
        }

        private float GetFbmWeight(string blendName)
        {
            if (string.IsNullOrEmpty(blendName))
            {
                return 0f;
            }

            for (int i = 0; i < fbmBlendNames.Length; i++)
            {
                if (fbmBlendNames[i] == blendName)
                {
                    return fbmWeights[i];
                }
            }

            return 0f;
        }

        private void ApplyAll(bool force)
        {
            if (targetSkinnedMeshRenderer == null || cachedMesh == null)
            {
                return;
            }

            for (int i = 0; i < expressions.Count; i++)
            {
                UniversalExpressionEntry entry = expressions[i];
                if (entry == null)
                {
                    continue;
                }

                float expressionWeight = entry.enabled ? entry.weight : 0f;
                if (entry.blendShapeIndex >= 0 && (force || Mathf.Abs(expressionWeight - entry.lastAppliedWeight) > WeightEpsilon))
                {
                    // A VRM-enabled Figure has one owner for VRM_* weights: UniVRM.
                    // MCM stays ShapeSync-owned below. Without the optional adapter
                    // (including a UniVRM-free build), preserve the direct path.
                    if (!TrySetOptionalVrmExpressionWeight(entry.vrmExpressionName, expressionWeight))
                    {
                        WriteFigureBlendShapeWeight(entry.blendShapeIndex, expressionWeight * 100f);
                    }
                    entry.lastAppliedWeight = expressionWeight;
                }

                ExpressionMcmBinding[] bindings = entry.mcmBindings;
                if (bindings == null)
                {
                    continue;
                }

                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    ExpressionMcmBinding binding = bindings[bindingIndex];
                    if (binding.blendShapeIndex < 0)
                    {
                        continue;
                    }

                    float mcmWeight = expressionWeight * GetFbmWeight(binding.blendName);
                    if (force || Mathf.Abs(mcmWeight - binding.lastAppliedWeight) > WeightEpsilon)
                    {
                        WriteFigureBlendShapeWeight(binding.blendShapeIndex, mcmWeight * 100f);
                        binding.lastAppliedWeight = mcmWeight;
                        bindings[bindingIndex] = binding;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (blendWeightSubscription != null)
            {
                blendWeightSubscription.Dispose();
                blendWeightSubscription = null;
            }
        }

        public void ReapplyExpressionWeightsAfterMeshReplacement(Mesh mesh)
        {
            // PCM slot replacement is index/order preserving. Do not rebuild Expression caches;
            // write the already evaluated values to the replacement Mesh immediately.
            cachedMesh = mesh;
            ApplyAll(true);
        }

        private void WriteFigureBlendShapeWeight(int blendShapeIndex, float weight)
        {
            if (dynamicMorphAdapter != null && dynamicMorphAdapter.WriteFigureBlendShapeWeight(blendShapeIndex, weight)) return;
            if (targetSkinnedMeshRenderer != null) targetSkinnedMeshRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
        }
    }
}
