// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Runtime-owned binding that clones and updates one attached Outfit renderer's mesh and bindposes.
    /// </summary>
    public sealed class OutfitSkinnedMeshBinding : IDisposable
    {
        private const float WeightEpsilon = 0.0001f;

        private struct BindposeTrs
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool valid;
        }

        private sealed class FbmCache
        {
            public string blendName;
            public int blendShapeIndex;
            public BindposeTrs[] bindposes;
            public float weight;
            public bool hasWeight;
            public bool missingBindposeWarningLogged;
            public bool missingBlendShapeWarningLogged;
        }

        private struct TargetBlendShapeCache
        {
            public int blendShapeIndex;
            public int targetIndex;
            public float lastWeight;
            public bool hasWeight;
        }

        private struct PbmDifferenceBlendShapeCache
        {
            public int blendShapeIndex;
            public int fbmTargetIndex;
            public int pbmTargetIndex;
            public float lastWeight;
            public bool hasWeight;
        }

        private struct PbmDifferenceBindposeCache
        {
            public int differenceCacheIndex;
            public int fbmCacheIndex;
            public int pbmCacheIndex;
        }

        private readonly SkinnedMeshRenderer renderer;
        private readonly Mesh runtimeMesh;
        private readonly BindposeTrs[] baseBindposes;
        private readonly Matrix4x4[] exactBaseBindposes;
        private readonly bool preserveMeshBaseBindposes;
        private readonly BindposeTrs[] blendedBindposes;
        private readonly Matrix4x4[] blendedMatrices;
        private readonly FbmCache[] fbmCaches;
        private TargetBlendShapeCache[] targetBlendShapeCaches = Array.Empty<TargetBlendShapeCache>();
        private PbmDifferenceBlendShapeCache[] pbmDifferenceBlendShapeCaches = Array.Empty<PbmDifferenceBlendShapeCache>();
        private PbmDifferenceBindposeCache[] pbmDifferenceBindposeCaches = Array.Empty<PbmDifferenceBindposeCache>();

        private OutfitSkinnedMeshBinding(
            SkinnedMeshRenderer renderer,
            Mesh runtimeMesh,
            BindposeTrs[] baseBindposes,
            Matrix4x4[] exactBaseBindposes,
            bool preserveMeshBaseBindposes,
            FbmCache[] fbmCaches)
        {
            this.renderer = renderer;
            this.runtimeMesh = runtimeMesh;
            this.baseBindposes = baseBindposes;
            this.exactBaseBindposes = exactBaseBindposes;
            this.preserveMeshBaseBindposes = preserveMeshBaseBindposes;
            this.fbmCaches = fbmCaches;
            blendedBindposes = new BindposeTrs[baseBindposes.Length];
            blendedMatrices = new Matrix4x4[baseBindposes.Length];
        }

        public SkinnedMeshRenderer Renderer => renderer;

        public void ConfigureDdbTargets(IReadOnlyList<DynamicBoneBlendTarget> targets)
        {
            if (runtimeMesh == null || targets == null)
            {
                targetBlendShapeCaches = Array.Empty<TargetBlendShapeCache>();
                pbmDifferenceBlendShapeCaches = Array.Empty<PbmDifferenceBlendShapeCache>();
                return;
            }

            List<TargetBlendShapeCache> direct = new List<TargetBlendShapeCache>();
            List<PbmDifferenceBlendShapeCache> differences = new List<PbmDifferenceBlendShapeCache>();
            List<PbmDifferenceBindposeCache> bindposeDifferences = new List<PbmDifferenceBindposeCache>();
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                DynamicBoneBlendTarget target = targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.blendName))
                {
                    continue;
                }

                int directIndex = runtimeMesh.GetBlendShapeIndex(target.blendName);
                if (directIndex >= 0)
                {
                    direct.Add(new TargetBlendShapeCache { blendShapeIndex = directIndex, targetIndex = targetIndex });
                }

                if (!target.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                {
                    continue;
                }

                string pbmName = target.blendName.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                for (int fbmTargetIndex = 0; fbmTargetIndex < targets.Count; fbmTargetIndex++)
                {
                    DynamicBoneBlendTarget fbmTarget = targets[fbmTargetIndex];
                    if (fbmTarget == null || string.IsNullOrEmpty(fbmTarget.blendName) || fbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int differenceIndex = runtimeMesh.GetBlendShapeIndex($"{BlendShapeReservedPrefixes.Pbm}{fbmTarget.blendName}_{pbmName}");
                    if (differenceIndex >= 0)
                    {
                        differences.Add(new PbmDifferenceBlendShapeCache
                        {
                            blendShapeIndex = differenceIndex,
                            fbmTargetIndex = fbmTargetIndex,
                            pbmTargetIndex = targetIndex
                        });
                    }

                    int differenceBindposeCacheIndex = FindFbmCacheIndex($"{BlendShapeReservedPrefixes.Pbm}{fbmTarget.blendName}_{pbmName}");
                    int fbmBindposeCacheIndex = FindFbmCacheIndex(fbmTarget.blendName);
                    int pbmBindposeCacheIndex = FindFbmCacheIndex(target.blendName);
                    if (differenceBindposeCacheIndex >= 0 && fbmBindposeCacheIndex >= 0 && pbmBindposeCacheIndex >= 0)
                    {
                        bindposeDifferences.Add(new PbmDifferenceBindposeCache
                        {
                            differenceCacheIndex = differenceBindposeCacheIndex,
                            fbmCacheIndex = fbmBindposeCacheIndex,
                            pbmCacheIndex = pbmBindposeCacheIndex
                        });
                    }
                }
            }

            targetBlendShapeCaches = direct.ToArray();
            pbmDifferenceBlendShapeCaches = differences.ToArray();
            pbmDifferenceBindposeCaches = bindposeDifferences.ToArray();
        }

        public void ApplyTargetWeights(IReadOnlyList<float> weights)
        {
            if (renderer == null || weights == null)
            {
                return;
            }

            for (int i = 0; i < targetBlendShapeCaches.Length; i++)
            {
                TargetBlendShapeCache cache = targetBlendShapeCaches[i];
                float weight = GetWeight(weights, cache.targetIndex);
                if (!cache.hasWeight || Mathf.Abs(cache.lastWeight - weight) > WeightEpsilon)
                {
                    renderer.SetBlendShapeWeight(cache.blendShapeIndex, weight * 100f);
                    cache.lastWeight = weight;
                    cache.hasWeight = true;
                    targetBlendShapeCaches[i] = cache;
                }
            }

            for (int i = 0; i < pbmDifferenceBlendShapeCaches.Length; i++)
            {
                PbmDifferenceBlendShapeCache cache = pbmDifferenceBlendShapeCaches[i];
                float weight = GetWeight(weights, cache.fbmTargetIndex) * GetWeight(weights, cache.pbmTargetIndex);
                if (!cache.hasWeight || Mathf.Abs(cache.lastWeight - weight) > WeightEpsilon)
                {
                    renderer.SetBlendShapeWeight(cache.blendShapeIndex, weight * 100f);
                    cache.lastWeight = weight;
                    cache.hasWeight = true;
                    pbmDifferenceBlendShapeCaches[i] = cache;
                }
            }
        }

        private static float GetWeight(IReadOnlyList<float> weights, int index)
        {
            if (index < 0 || index >= weights.Count)
            {
                return 0f;
            }

            float value = weights[index];
            return IsFinite(value) ? value : 0f;
        }

        public static bool TryCreate(
            SkinnedMeshRenderer renderer,
            OutfitSkinningRendererProfile profile,
            out OutfitSkinnedMeshBinding binding,
            out string error)
        {
            return TryCreate(renderer, profile, false, out binding, out error);
        }

        public static bool TryCreate(
            SkinnedMeshRenderer renderer,
            OutfitSkinningRendererProfile profile,
            bool preserveMeshBaseBindposes,
            out OutfitSkinnedMeshBinding binding,
            out string error)
        {
            binding = null;
            error = null;
            if (renderer == null || renderer.sharedMesh == null)
            {
                error = "SkinnedMeshRenderer or sharedMesh is null.";
                return false;
            }

            if (profile == null || profile.baseBindposes == null)
            {
                error = "Outfit Skinning Profile is missing its base bindposes.";
                return false;
            }

            Mesh sourceMesh = renderer.sharedMesh;
            if (sourceMesh.bindposes == null || sourceMesh.bindposes.Length == 0 || profile.baseBindposes.Length != sourceMesh.bindposes.Length)
            {
                error = "Outfit Skinning Profile base bindpose count does not match the renderer mesh.";
                return false;
            }

            Matrix4x4[] baseSourceBindposes = preserveMeshBaseBindposes ? sourceMesh.bindposes : profile.baseBindposes;
            BindposeTrs[] baseTrs = new BindposeTrs[baseSourceBindposes.Length];
            for (int i = 0; i < baseTrs.Length; i++)
            {
                baseTrs[i] = DecomposeMatrix(baseSourceBindposes[i]);
                if (!baseTrs[i].valid)
                {
                    error = $"Outfit Skinning Profile has an invalid base bindpose at index {i}.";
                    return false;
                }
            }

            if (!TryBuildFbmCaches(sourceMesh, profile, baseTrs.Length, out FbmCache[] caches, out error))
            {
                return false;
            }

            Mesh clonedMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            clonedMesh.name = $"{sourceMesh.name} (ShapeSync Outfit Runtime)";
            renderer.sharedMesh = clonedMesh;
            binding = new OutfitSkinnedMeshBinding(renderer, clonedMesh, baseTrs, preserveMeshBaseBindposes ? baseSourceBindposes : null, preserveMeshBaseBindposes, caches);
            binding.ApplyAllBindposes();
            return true;
        }

        public void ApplyFbmWeight(FbmWeightChange change)
        {
            if (string.IsNullOrEmpty(change.BlendName))
            {
                return;
            }

            for (int i = 0; i < fbmCaches.Length; i++)
            {
                FbmCache cache = fbmCaches[i];
                if (cache.blendName != change.BlendName)
                {
                    continue;
                }

                float weight = change.Enabled && IsFinite(change.Weight) ? change.Weight : 0f;
                if (cache.hasWeight && Mathf.Abs(cache.weight - weight) <= WeightEpsilon)
                {
                    return;
                }

                cache.weight = weight;
                cache.hasWeight = true;
                if (cache.blendShapeIndex >= 0 && renderer != null)
                {
                    renderer.SetBlendShapeWeight(cache.blendShapeIndex, weight * 100f);
                }
                else if (!cache.missingBlendShapeWarningLogged)
                {
                    cache.missingBlendShapeWarningLogged = true;
                    Debug.LogWarning($"ShapeSync Outfit Renderer '{GetRendererName()}' skipped FBM BlendShape '{cache.blendName}' because it is missing.", renderer);
                }

                if (cache.bindposes == null && !cache.missingBindposeWarningLogged)
                {
                    cache.missingBindposeWarningLogged = true;
                    Debug.LogWarning($"ShapeSync Outfit Renderer '{GetRendererName()}' skipped FBM bindpose delta for '{cache.blendName}' because its target bindposes are missing.", renderer);
                }

                ApplyAllBindposes();
                return;
            }
        }

        // This is the established non-BCP attachment reconciliation path. OutfitAttacher bypasses
        // it for BCP-baked output because that profile already targets the corrected Figure rig.
        public bool TryAlignToFigureSkinning(SkinnedMeshRenderer figureRenderer, out string error)
        {
            error = null;
            if (renderer == null || runtimeMesh == null)
            {
                error = "Outfit SkinnedMeshRenderer or runtime mesh is missing.";
                return false;
            }

            if (figureRenderer == null || figureRenderer.sharedMesh == null)
            {
                error = "Figure SkinnedMeshRenderer or mesh is missing.";
                return false;
            }

            Matrix4x4[] outfitBindposes = runtimeMesh.bindposes;
            Matrix4x4[] figureBindposes = figureRenderer.sharedMesh.bindposes;
            Transform[] outfitBones = renderer.bones;
            Transform[] figureBones = figureRenderer.bones;
            if (outfitBindposes == null || outfitBones == null || outfitBindposes.Length != outfitBones.Length)
            {
                error = "Outfit runtime bindposes do not match its mapped bones.";
                return false;
            }

            if (figureBindposes == null || figureBones == null || figureBindposes.Length != figureBones.Length)
            {
                error = "Figure bindposes do not match its bones.";
                return false;
            }

            bool changed = false;
            Matrix4x4 outfitRendererLocalToWorld = renderer.transform.localToWorldMatrix;
            Matrix4x4 figureRendererWorldToLocal = figureRenderer.transform.worldToLocalMatrix;
            for (int outfitIndex = 0; outfitIndex < outfitBones.Length; outfitIndex++)
            {
                Transform outfitBone = outfitBones[outfitIndex];
                if (outfitBone == null)
                {
                    continue;
                }

                int figureIndex = FindBoneIndex(figureBones, outfitBone);
                if (figureIndex < 0)
                {
                    continue;
                }

                Transform figureBone = figureBones[figureIndex];
                Matrix4x4 figureSkinning = figureRendererWorldToLocal * figureBone.localToWorldMatrix * figureBindposes[figureIndex];
                outfitBindposes[outfitIndex] = outfitBone.worldToLocalMatrix * outfitRendererLocalToWorld * figureSkinning;
                changed = true;
            }

            if (changed)
            {
                runtimeMesh.bindposes = outfitBindposes;
            }

            return true;
        }

        public void Dispose()
        {
            if (runtimeMesh != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(runtimeMesh);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(runtimeMesh);
                }
            }
        }

        private static bool TryBuildFbmCaches(
            Mesh mesh,
            OutfitSkinningRendererProfile profile,
            int bindposeCount,
            out FbmCache[] caches,
            out string error)
        {
            error = null;
            if (profile.fbmBindposes == null || profile.fbmBindposes.Count == 0)
            {
                caches = Array.Empty<FbmCache>();
                return true;
            }

            HashSet<string> names = new HashSet<string>();
            caches = new FbmCache[profile.fbmBindposes.Count];
            for (int i = 0; i < profile.fbmBindposes.Count; i++)
            {
                OutfitSkinningFbmBindposes entry = profile.fbmBindposes[i];
                if (entry == null || string.IsNullOrEmpty(entry.blendName) || !names.Add(entry.blendName))
                {
                    error = "Outfit Skinning Profile has an empty or duplicate FBM blendName.";
                    return false;
                }

                BindposeTrs[] targetTrs = null;
                if (entry.bindposes != null && entry.bindposes.Length > 0)
                {
                    if (entry.bindposes.Length != bindposeCount)
                    {
                        error = $"Outfit Skinning Profile target bindpose count does not match for '{entry.blendName}'.";
                        return false;
                    }

                    targetTrs = new BindposeTrs[bindposeCount];
                    for (int bindposeIndex = 0; bindposeIndex < bindposeCount; bindposeIndex++)
                    {
                        targetTrs[bindposeIndex] = DecomposeMatrix(entry.bindposes[bindposeIndex]);
                        if (!targetTrs[bindposeIndex].valid)
                        {
                            error = $"Outfit Skinning Profile has an invalid target bindpose for '{entry.blendName}' at index {bindposeIndex}.";
                            return false;
                        }
                    }
                }

                caches[i] = new FbmCache
                {
                    blendName = entry.blendName,
                    blendShapeIndex = mesh.GetBlendShapeIndex(entry.blendName),
                    bindposes = targetTrs
                };
            }

            return true;
        }

        private void ApplyAllBindposes()
        {
            if (runtimeMesh == null)
            {
                return;
            }

            if (preserveMeshBaseBindposes && !HasActiveBindposeWeight())
            {
                runtimeMesh.bindposes = exactBaseBindposes;
                return;
            }

            BuildCurrentBindposes(blendedMatrices);
            runtimeMesh.bindposes = blendedMatrices;
        }

        private void BuildCurrentBindposes(Matrix4x4[] destination)
        {
            for (int bindposeIndex = 0; bindposeIndex < baseBindposes.Length; bindposeIndex++)
            {
                BindposeTrs baseTrs = baseBindposes[bindposeIndex];
                Vector3 position = baseTrs.position;
                Vector3 scale = baseTrs.scale;
                Quaternion rotation = baseTrs.rotation;

                for (int fbmIndex = 0; fbmIndex < fbmCaches.Length; fbmIndex++)
                {
                    FbmCache cache = fbmCaches[fbmIndex];
                    if (!cache.hasWeight || cache.bindposes == null)
                    {
                        continue;
                    }

                    BindposeTrs targetTrs = cache.bindposes[bindposeIndex];
                    float weight = cache.weight;
                    position += (targetTrs.position - baseTrs.position) * weight;
                    scale += (targetTrs.scale - baseTrs.scale) * weight;
                    Quaternion delta = targetTrs.rotation * Quaternion.Inverse(baseTrs.rotation);
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, delta, weight) * rotation;
                }

                for (int differenceIndex = 0; differenceIndex < pbmDifferenceBindposeCaches.Length; differenceIndex++)
                {
                    PbmDifferenceBindposeCache cache = pbmDifferenceBindposeCaches[differenceIndex];
                    FbmCache qCache = fbmCaches[cache.differenceCacheIndex];
                    FbmCache fCache = fbmCaches[cache.fbmCacheIndex];
                    FbmCache pCache = fbmCaches[cache.pbmCacheIndex];
                    if (!fCache.hasWeight || !pCache.hasWeight || qCache.bindposes == null || fCache.bindposes == null || pCache.bindposes == null)
                    {
                        continue;
                    }

                    BindposeTrs q = qCache.bindposes[bindposeIndex];
                    BindposeTrs f = fCache.bindposes[bindposeIndex];
                    BindposeTrs p = pCache.bindposes[bindposeIndex];
                    float product = fCache.weight * pCache.weight;
                    position += (q.position - baseTrs.position - (f.position - baseTrs.position) - (p.position - baseTrs.position)) * product;
                    scale += (q.scale - baseTrs.scale - (f.scale - baseTrs.scale) - (p.scale - baseTrs.scale)) * product;
                    Quaternion fullDirect = ComposePairRotation(baseTrs.rotation, f.rotation, p.rotation, cache.fbmCacheIndex, cache.pbmCacheIndex);
                    Quaternion correction = q.rotation * Quaternion.Inverse(fullDirect);
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, correction, product) * rotation;
                }

                blendedBindposes[bindposeIndex].position = position;
                blendedBindposes[bindposeIndex].rotation = NormalizeQuaternion(rotation);
                blendedBindposes[bindposeIndex].scale = scale;
                destination[bindposeIndex] = Matrix4x4.TRS(position, blendedBindposes[bindposeIndex].rotation, scale);
            }
        }

        private bool HasActiveBindposeWeight()
        {
            for (int i = 0; i < fbmCaches.Length; i++)
            {
                FbmCache cache = fbmCaches[i];
                if (cache.hasWeight && cache.bindposes != null && Mathf.Abs(cache.weight) > WeightEpsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private int FindFbmCacheIndex(string blendName)
        {
            for (int i = 0; i < fbmCaches.Length; i++)
            {
                if (fbmCaches[i].blendName == blendName)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Quaternion ComposePairRotation(Quaternion baseRotation, Quaternion fbmRotation, Quaternion pbmRotation, int fbmIndex, int pbmIndex)
        {
            Quaternion fbmDelta = fbmRotation * Quaternion.Inverse(baseRotation);
            Quaternion pbmDelta = pbmRotation * Quaternion.Inverse(baseRotation);
            return fbmIndex <= pbmIndex
                ? pbmDelta * fbmDelta * baseRotation
                : fbmDelta * pbmDelta * baseRotation;
        }

        private static int FindBoneIndex(Transform[] bones, Transform bone)
        {
            if (bones == null || bone == null)
            {
                return -1;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == bone)
                {
                    return i;
                }
            }

            return -1;
        }

        private static BindposeTrs DecomposeMatrix(Matrix4x4 matrix)
        {
            BindposeTrs result = new BindposeTrs
            {
                position = new Vector3(matrix.m03, matrix.m13, matrix.m23),
                valid = true
            };
            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            result.scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (result.scale.x > Mathf.Epsilon) right /= result.scale.x;
            if (result.scale.y > Mathf.Epsilon) up /= result.scale.y;
            if (result.scale.z > Mathf.Epsilon) forward /= result.scale.z;
            if (forward.sqrMagnitude <= Mathf.Epsilon || up.sqrMagnitude <= Mathf.Epsilon)
            {
                result.rotation = Quaternion.identity;
                result.valid = false;
                return result;
            }

            result.rotation = Quaternion.LookRotation(forward, up);
            return result;
        }

        private static Quaternion NormalizeQuaternion(Quaternion value)
        {
            float length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return length <= Mathf.Epsilon ? Quaternion.identity : new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private string GetRendererName()
        {
            return renderer != null ? renderer.name : "<destroyed>";
        }
    }
}
