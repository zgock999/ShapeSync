// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniHumanoid;
using UniVRM10;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>Optional Runtime-only VRM compiler service. It never creates assets or uses Editor APIs.</summary>
    public static class VrmIntegrationService
    {
        /// <summary>Performs in-memory VRM Initialize followed by Physics Transport without mutating source roles.</summary>
        public static bool TransportPhysics(VrmTransportPhysicsRequest request, out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            // The compiler candidate is unpublished and will be committed or destroyed in this
            // transaction.  Removing a cloned source Vrm10Instance must be immediate: a normal
            // PlayMode Destroy is deferred until frame end, while TryInitialize below must add
            // the replacement Vrm10Instance in this same transaction/frame.
            VrmTransportCandidateNormalizer.Normalize(request.CandidateRoot, immediately: true);
            if (!VrmTransportPhysicsRequestValidator.TryValidate(request, out VrmTransportPhysicsContext context, out diagnostic)) return false;
            Vrm10Instance instance = null;
            VRM10Object vrm = null;
            VRM10Expression[] expressions = Array.Empty<VRM10Expression>();
            try
            {
                if (!TryInitialize(context, out instance, out vrm, out expressions, out diagnostic))
                {
                    DestroyAssets(vrm, expressions);
                    return false;
                }
                if (!TryTransportPhysics(context.Request, instance, out diagnostic))
                {
                    DestroyAssets(vrm, expressions);
                    return false;
                }
                result = new VrmTransportPhysicsResult(instance, vrm, expressions);
                return true;
            }
            catch (Exception exception)
            {
                DestroyAssets(vrm, expressions);
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmTransportUnexpectedFailure", "VRM TransportPhysics encountered an unexpected in-memory failure.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Builds a fresh VRM graph for one inactive Hot Bake clone, remapping every spring reference away from the retained template.</summary>
        internal static bool RebuildSpawnPhysics(GameObject templateRoot, GameObject spawnRoot, out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            VrmTransportCandidateNormalizer.Normalize(spawnRoot, immediately: true);
            var request = new VrmTransportPhysicsRequest(spawnRoot, templateRoot, Array.Empty<GameObject>());
            return TransportPhysics(request, out result, out diagnostic);
        }

        private static bool TryInitialize(VrmTransportPhysicsContext context, out Vrm10Instance instance, out VRM10Object vrm, out VRM10Expression[] expressions, out StackMachineDiagnostic diagnostic)
        {
            instance = null; vrm = null; expressions = Array.Empty<VRM10Expression>(); diagnostic = null;
            GameObject candidate = context.Request.CandidateRoot;
            Humanoid humanoid = candidate.GetComponent<Humanoid>() ?? candidate.AddComponent<Humanoid>();
            if (!humanoid.AssignBonesFromAnimator())
                return Reject("VrmCandidateHumanoidMapFailed", "VRM TransportPhysics could not assign the candidate Humanoid mapping from its Animator.", out diagnostic);
            foreach (var issue in humanoid.Validate())
                if (issue.IsError) return Reject("VrmCandidateHumanoidInvalid", "VRM TransportPhysics candidate Humanoid validation failed: " + issue.Message, out diagnostic);

            SkinnedMeshRenderer[] renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1 || renderers[0] == null || renderers[0].sharedMesh == null)
                return Reject("VrmCandidateRendererAmbiguous", "VRM TransportPhysics candidate must contain exactly one merged SkinnedMeshRenderer with a Mesh.", out diagnostic);

            instance = candidate.AddComponent<Vrm10Instance>();
            vrm = ScriptableObject.CreateInstance<VRM10Object>();
            vrm.name = candidate.name + "_VRM";
            instance.Vrm = vrm;
            expressions = CreateExpressions(candidate.transform, renderers[0]);
            for (int i = 0; i < expressions.Length; i++)
            {
                VRM10Expression expression = expressions[i];
                vrm.Expression.AddClip(TryGetPreset(expression.name, out ExpressionPreset preset) ? preset : ExpressionPreset.custom, expression);
            }
            return true;
        }

        private static VRM10Expression[] CreateExpressions(Transform candidateRoot, SkinnedMeshRenderer renderer)
        {
            var result = new List<VRM10Expression>();
            try
            {
                string rendererPath = GetRelativePath(candidateRoot, renderer.transform);
                Mesh mesh = renderer.sharedMesh;
                foreach (ExpressionPreset preset in Enum.GetValues(typeof(ExpressionPreset)))
                {
                    if (preset != ExpressionPreset.custom) result.Add(CreateExpression(preset.ToString(), rendererPath, mesh.GetBlendShapeIndex(BlendShapeReservedPrefixes.Vrm + preset)));
                }
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string shape = mesh.GetBlendShapeName(i);
                    if (!shape.StartsWith(BlendShapeReservedPrefixes.Vrm, StringComparison.Ordinal)) continue;
                    string name = shape.Substring(BlendShapeReservedPrefixes.Vrm.Length);
                    if (!TryGetPreset(name, out _)) result.Add(CreateExpression(name, rendererPath, i));
                }
                return result.ToArray();
            }
            catch
            {
                for (int i = 0; i < result.Count; i++) if (result[i] != null) DestroyAsset(result[i]);
                throw;
            }
        }

        private static VRM10Expression CreateExpression(string name, string rendererPath, int blendShapeIndex)
        {
            var expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = name;
            expression.MorphTargetBindings = blendShapeIndex < 0 ? Array.Empty<MorphTargetBinding>() : new[] { new MorphTargetBinding(rendererPath, blendShapeIndex, 1f) };
            expression.MaterialColorBindings = Array.Empty<MaterialColorBinding>();
            expression.MaterialUVBindings = Array.Empty<MaterialUVBinding>();
            expression.NodeTransformBindings = Array.Empty<NodeTransformBinding>();
            return expression;
        }

        private static bool TryTransportPhysics(VrmTransportPhysicsRequest request, Vrm10Instance candidate, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            candidate.SpringBone ??= new Vrm10InstanceSpringBone();
            candidate.SpringBone.ColliderGroups ??= new List<VRM10SpringBoneColliderGroup>();
            candidate.SpringBone.Springs ??= new List<Vrm10InstanceSpringBone.Spring>();
            if (!TryAppendFigurePhysics(request.FigureSourceRoot, candidate, out diagnostic)) return false;
            IReadOnlyList<GameObject> outfits = request.AttachedOutfitSourceRoots;
            for (int i = 0; i < outfits.Count; i++) if (!TryAppendOutfitPhysics(outfits[i], candidate, out diagnostic)) return false;
            return true;
        }

        private static bool TryAppendFigurePhysics(GameObject sourceRoot, Vrm10Instance candidate, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Vrm10Instance source = null;
            foreach (Vrm10Instance value in sourceRoot.GetComponentsInChildren<Vrm10Instance>(true))
            {
                if (value == null || value.Vrm == null) continue;
                if (source != null) return Reject("VrmSourceInstanceAmbiguous", "VRM TransportPhysics source role contains multiple valid Vrm10Instance components.", out diagnostic);
                source = value;
            }
            if (source == null)
                return Reject("VrmFigureSourceInstanceRequired", "VRM TransportPhysics requires a valid Vrm10Instance on the Figure source role.", out diagnostic);
            if (source.SpringBone == null) return true;
            if (source.SpringBone.Springs == null || source.SpringBone.ColliderGroups == null)
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics source SpringBone contains a null Springs or ColliderGroups list.", out diagnostic);
            if (source.SpringBone.Springs.Count == 0) return true;
            Func<Transform, Transform> mapper = value => ResolveCandidateTransform(candidate.transform, sourceRoot.transform, value);
            if (!TryValidatePhysicsMapping(source.SpringBone.Springs, mapper, out string mappingError))
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics could not remap source physics: " + mappingError, out diagnostic);
            if (!ShapeSyncVrmSpringBoneAttachment.TryCreate(source.SpringBone.ColliderGroups, source.SpringBone.Springs, null, sourceRoot.transform, candidate, mapper, out _, out string error))
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics could not transport source physics: " + error, out diagnostic);
            return true;
        }

        private static bool TryAppendOutfitPhysics(GameObject sourceRoot, Vrm10Instance candidate, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            // Outfit Builder may leave a Vrm10Instance with broken references.  It is junk data:
            // the sole authoritative Outfit physics carrier is ShapeSyncOutfitSpringBoneData.
            ShapeSyncOutfitSpringBoneData sourceData = sourceRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (sourceData == null) return true;
            if (sourceData.Springs == null || sourceData.ColliderGroups == null || sourceData.SpringColliderGroupNames == null)
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics Outfit SpringBone data contains a null ColliderGroups or SpringColliderGroupNames list.", out diagnostic);
            if (sourceData.Springs.Count == 0) return true;
            Func<Transform, Transform> mapper = value => ResolveCandidateTransform(candidate.transform, sourceRoot.transform, value);
            if (!TryValidatePhysicsMapping(sourceData.Springs, mapper, out string mappingError))
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics could not remap Outfit SpringBone data: " + mappingError, out diagnostic);
            if (!ShapeSyncVrmSpringBoneAttachment.TryCreate(sourceData.ColliderGroups, sourceData.Springs, sourceData.SpringColliderGroupNames, sourceRoot.transform, candidate, mapper, out _, out string error))
                return Reject("VrmPhysicsTransportFailed", "VRM TransportPhysics could not transport Outfit SpringBone data: " + error, out diagnostic);
            return true;
        }

        private static bool TryValidatePhysicsMapping(IReadOnlyList<Vrm10InstanceSpringBone.Spring> springs, Func<Transform, Transform> mapper, out string error)
        {
            error = null;
            for (int springIndex = 0; springIndex < springs.Count; springIndex++)
            {
                Vrm10InstanceSpringBone.Spring spring = springs[springIndex];
                if (spring == null || spring.Joints == null) continue;
                if (spring.Center != null && mapper(spring.Center) == null)
                {
                    error = "Spring '" + spring.Name + "' center could not be remapped.";
                    return false;
                }
                for (int jointIndex = 0; jointIndex < spring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint joint = spring.Joints[jointIndex];
                    if (joint == null) continue;
                    if (mapper(joint.transform) == null)
                    {
                        error = "Spring '" + spring.Name + "' joint at index " + jointIndex + " could not be remapped.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static Transform ResolveCandidateTransform(Transform candidateRoot, Transform sourceRoot, Transform source)
        {
            string path = GetRelativePath(sourceRoot, source);
            return (string.IsNullOrEmpty(path) ? candidateRoot : candidateRoot.Find(path)) ?? FindUniqueByName(candidateRoot, source.name);
        }

        private static Transform FindUniqueByName(Transform root, string name)
        {
            Transform found = null; var stack = new Stack<Transform>(); stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current.name == name) { if (found != null) return null; found = current; }
                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }
            return found;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var names = new Stack<string>(); Transform current = target;
            while (current != null && current != root) { names.Push(current.name); current = current.parent; }
            return current == root ? string.Join("/", names.ToArray()) : null;
        }

        private static bool TryGetPreset(string name, out ExpressionPreset preset) => Enum.TryParse(name, true, out preset) && preset != ExpressionPreset.custom;
        private static void DestroyAssets(VRM10Object vrm, VRM10Expression[] expressions)
        {
            if (expressions != null)
            {
                for (int i = 0; i < expressions.Length; i++)
                {
                    if (expressions[i] != null) DestroyAsset(expressions[i]);
                }
            }
            if (vrm != null) DestroyAsset(vrm);
        }

        private static void DestroyAsset(UnityEngine.Object asset)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("vrm", code, message); return false; }
    }
}
#endif
