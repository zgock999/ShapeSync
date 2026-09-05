// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR && SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniHumanoid;
using UniVRM10;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>
    /// Reconstructs Physics Reference data inside generated Prefab assets.
    /// This is the Generate-post implementation of the Physics Transporter
    /// semantics; it does not call either Transporter window or Spec17 staging.
    /// </summary>
    internal static class ShapeSyncVrmPhysicsGenerateTransfer
    {
        internal static bool TryTransferFigure(
            ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference relation,
            string targetPrefabPath,
            ICollection<string> generatedPaths,
            out string diagnostic)
        {
            diagnostic = null;
            if (!TryGetSource(relation?.ReferencePrefab, out Vrm10Instance source, out diagnostic)) return false;
            if (!TryBuildPlan(source, out TransferPlan plan, out diagnostic)) return false;
            if (plan.Springs.Count == 0 && plan.Groups.Count == 0) return true;

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(targetPrefabPath);
                if (contents == null)
                {
                    diagnostic = "VrmGenerateFigurePhysicsOutputMissing: Generated Figure Prefab could not be opened for Physics transfer.";
                    return false;
                }

                Vrm10Instance destination = contents.GetComponentInChildren<Vrm10Instance>(true);
                if (destination == null)
                {
                    diagnostic = "VrmGenerateFigurePhysicsDestinationInvalid: Generated Figure has no initialized Vrm10Instance.";
                    return false;
                }

                if (!TryBuildFigurePlan(source, contents.transform, plan, out TransferPlan figurePlan, out diagnostic))
                    return false;

                ApplyFigurePlan(source, destination, contents.transform, figurePlan);
                if (!PrefabUtility.SaveAsPrefabAsset(contents, targetPrefabPath, out bool saved) || !saved)
                {
                    diagnostic = "VrmGenerateFigurePhysicsSaveFailed: Generated Figure Prefab could not be saved.";
                    return false;
                }

                AddGeneratedPath(generatedPaths, targetPrefabPath);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "VrmGenerateFigurePhysicsFailed: " + exception.Message;
                return false;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        internal static bool TryTransferOutfit(
            ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference relation,
            string targetPrefabPath,
            ICollection<string> generatedPaths,
            out string diagnostic)
        {
            diagnostic = null;
            if (!TryGetSource(relation?.ReferencePrefab, out Vrm10Instance source, out diagnostic)) return false;
            if (!TryBuildPlan(source, out TransferPlan plan, out diagnostic)) return false;
            if (plan.Springs.Count == 0 && plan.Groups.Count == 0) return true;

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(targetPrefabPath);
                if (contents == null)
                {
                    diagnostic = "VrmGenerateOutfitPhysicsOutputMissing: Generated Outfit Prefab could not be opened for Physics transfer.";
                    return false;
                }

                ApplyOutfitPlan(source, contents.transform, plan);
                if (!PrefabUtility.SaveAsPrefabAsset(contents, targetPrefabPath, out bool saved) || !saved)
                {
                    diagnostic = "VrmGenerateOutfitPhysicsSaveFailed: Generated Outfit Prefab could not be saved.";
                    return false;
                }

                AddGeneratedPath(generatedPaths, targetPrefabPath);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "VrmGenerateOutfitPhysicsFailed: " + exception.Message;
                return false;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private sealed class TransferPlan
        {
            internal readonly List<VRM10SpringBoneColliderGroup> Groups = new List<VRM10SpringBoneColliderGroup>();
            internal readonly List<Vrm10InstanceSpringBone.Spring> Springs = new List<Vrm10InstanceSpringBone.Spring>();
        }

        private static bool TryGetSource(GameObject referencePrefab, out Vrm10Instance source, out string diagnostic)
        {
            source = null;
            diagnostic = null;
            if (referencePrefab == null)
            {
                diagnostic = "VrmGeneratePhysicsReferenceMissing: Physics Reference Prefab is missing.";
                return false;
            }

            foreach (Vrm10Instance candidate in referencePrefab.GetComponentsInChildren<Vrm10Instance>(true))
            {
                if (candidate == null || candidate.Vrm == null) continue;
                if (source != null)
                {
                    diagnostic = "VrmGeneratePhysicsReferenceInvalid: Physics Reference must contain exactly one VRM graph.";
                    return false;
                }
                source = candidate;
            }

            if (source == null)
            {
                diagnostic = "VrmGeneratePhysicsReferenceInvalid: Physics Reference has no initialized VRM graph.";
                return false;
            }
            return true;
        }

        private static bool TryBuildFigurePlan(Vrm10Instance source, Transform destinationRoot,
            TransferPlan sourcePlan, out TransferPlan figurePlan, out string diagnostic)
        {
            figurePlan = new TransferPlan();
            figurePlan.Groups.AddRange(sourcePlan.Groups);
            diagnostic = null;
            foreach (Vrm10InstanceSpringBone.Spring spring in sourcePlan.Springs)
            {
                bool skipSpring = spring.Center != null
                    && ResolveFigureBoneTransform(destinationRoot, source.transform, spring.Center) == null;
                if (skipSpring && IsSourceHumanoidTransform(source, spring.Center))
                {
                    diagnostic = "VrmGenerateFigurePhysicsTransformMissing: Figure Physics Reference Spring Center path '"
                        + GetRelativePath(source.transform, spring.Center) + "' does not resolve in the generated Figure.";
                    return false;
                }

                foreach (VRM10SpringBoneJoint joint in spring.Joints)
                {
                    if (ResolveFigureBoneTransform(destinationRoot, source.transform, joint.transform) != null)
                        continue;

                    if (IsSourceHumanoidTransform(source, joint.transform))
                    {
                        diagnostic = "VrmGenerateFigurePhysicsTransformMissing: Figure Physics Reference Spring Joint path '"
                            + GetRelativePath(source.transform, joint.transform) + "' does not resolve in the generated Figure.";
                        return false;
                    }

                    skipSpring = true;
                    break;
                }

                if (!skipSpring) figurePlan.Springs.Add(spring);
            }

            return true;
        }

        private static bool IsSourceHumanoidTransform(Vrm10Instance source, Transform target)
        {
            if (source == null || source.Humanoid == null || target == null) return false;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                if (source.Humanoid.GetBoneTransform((HumanBodyBones)i) == target) return true;
            }
            return false;
        }

        private static bool TryBuildPlan(Vrm10Instance source, out TransferPlan plan, out string diagnostic)
        {
            plan = new TransferPlan();
            diagnostic = null;
            if (source.SpringBone == null) return true;
            if (source.SpringBone.ColliderGroups == null || source.SpringBone.Springs == null)
            {
                diagnostic = "VrmGeneratePhysicsReferenceInvalid: Physics Reference SpringBone lists are incomplete.";
                return false;
            }

            var declaredGroups = new HashSet<VRM10SpringBoneColliderGroup>();
            foreach (VRM10SpringBoneColliderGroup group in source.SpringBone.ColliderGroups)
            {
                if (group == null) continue;
                if (!declaredGroups.Add(group)) continue;
                if (group.Colliders == null)
                {
                    diagnostic = "VrmGeneratePhysicsReferenceInvalid: Physics Reference ColliderGroup has no Collider list.";
                    return false;
                }
                if (GetRelativePath(source.transform, group.transform) == null)
                {
                    diagnostic = "VrmGeneratePhysicsReferenceInvalid: ColliderGroup is outside the Reference root.";
                    return false;
                }
                foreach (VRM10SpringBoneCollider collider in group.Colliders)
                {
                    if (collider == null || GetRelativePath(source.transform, collider.transform) == null)
                    {
                        diagnostic = "VrmGeneratePhysicsReferenceInvalid: Collider is outside the Reference root.";
                        return false;
                    }
                }
                plan.Groups.Add(group);
            }

            foreach (Vrm10InstanceSpringBone.Spring spring in source.SpringBone.Springs)
            {
                if (spring == null || spring.Joints == null || spring.ColliderGroups == null)
                {
                    diagnostic = "VrmGeneratePhysicsReferenceInvalid: Physics Reference contains an incomplete Spring.";
                    return false;
                }
                if (spring.Center != null && GetRelativePath(source.transform, spring.Center) == null)
                {
                    diagnostic = "VrmGeneratePhysicsReferenceInvalid: Spring Center is outside the Reference root.";
                    return false;
                }
                foreach (VRM10SpringBoneJoint joint in spring.Joints)
                {
                    if (joint == null || GetRelativePath(source.transform, joint.transform) == null)
                    {
                        diagnostic = "VrmGeneratePhysicsReferenceInvalid: Spring Joint is outside the Reference root.";
                        return false;
                    }
                }
                foreach (VRM10SpringBoneColliderGroup group in spring.ColliderGroups)
                {
                    if (group == null || !declaredGroups.Contains(group))
                    {
                        diagnostic = "VrmGeneratePhysicsReferenceInvalid: Spring references an undeclared ColliderGroup.";
                        return false;
                    }
                }
                plan.Springs.Add(spring);
            }

            return true;
        }

        private static void ApplyFigurePlan(Vrm10Instance source, Vrm10Instance destination,
            Transform destinationRoot, TransferPlan plan)
        {
            ClearFigurePhysics(destinationRoot, destination);
            destination.SpringBone = new Vrm10InstanceSpringBone
            {
                ColliderGroups = new List<VRM10SpringBoneColliderGroup>(),
                Springs = new List<Vrm10InstanceSpringBone.Spring>()
            };

            var groupMap = new Dictionary<VRM10SpringBoneColliderGroup, VRM10SpringBoneColliderGroup>();
            foreach (VRM10SpringBoneColliderGroup sourceGroup in plan.Groups)
            {
                Transform destinationTransform = EnsureRelativePath(destinationRoot, source.transform, sourceGroup.transform);
                // Multiple VRM collider groups may intentionally live on the same
                // Transform (for example, the source `secondary` node). Never
                // reuse the first component here: doing so aliases every entry in
                // SpringBone.ColliderGroups and the last group overwrites the rest.
                VRM10SpringBoneColliderGroup destinationGroup =
                    destinationTransform.gameObject.AddComponent<VRM10SpringBoneColliderGroup>();
                destinationGroup.Name = sourceGroup.Name;
                destinationGroup.Colliders = new List<VRM10SpringBoneCollider>();
                foreach (VRM10SpringBoneCollider sourceCollider in sourceGroup.Colliders)
                {
                    Transform colliderTransform = EnsureRelativePath(destinationRoot, source.transform, sourceCollider.transform);
                    // A single Transform can also carry several collider
                    // components. Allocate one destination component per source
                    // component so the group list preserves cardinality and shape.
                    VRM10SpringBoneCollider destinationCollider =
                        colliderTransform.gameObject.AddComponent<VRM10SpringBoneCollider>();
                    destinationCollider.ColliderType = sourceCollider.ColliderType;
                    destinationCollider.Offset = sourceCollider.Offset;
                    destinationCollider.Radius = sourceCollider.Radius;
                    destinationCollider.Tail = sourceCollider.Tail;
                    destinationCollider.Normal = sourceCollider.Normal;
                    destinationGroup.Colliders.Add(destinationCollider);
                }
                groupMap.Add(sourceGroup, destinationGroup);
                destination.SpringBone.ColliderGroups.Add(destinationGroup);
            }

            foreach (Vrm10InstanceSpringBone.Spring sourceSpring in plan.Springs)
            {
                var destinationSpring = new Vrm10InstanceSpringBone.Spring(sourceSpring.Name);
                if (sourceSpring.Center != null)
                    destinationSpring.Center = ResolveFigureBoneTransform(destinationRoot, source.transform, sourceSpring.Center);
                foreach (VRM10SpringBoneJoint sourceJoint in sourceSpring.Joints)
                {
                    Transform jointTransform = ResolveFigureBoneTransform(destinationRoot, source.transform, sourceJoint.transform);
                    VRM10SpringBoneJoint destinationJoint = jointTransform.GetComponent<VRM10SpringBoneJoint>()
                        ?? jointTransform.gameObject.AddComponent<VRM10SpringBoneJoint>();
                    CopyJoint(sourceJoint, destinationJoint);
                    destinationSpring.Joints.Add(destinationJoint);
                }
                foreach (VRM10SpringBoneColliderGroup sourceGroup in sourceSpring.ColliderGroups)
                    destinationSpring.ColliderGroups.Add(groupMap[sourceGroup]);
                destination.SpringBone.Springs.Add(destinationSpring);
            }

            EditorUtility.SetDirty(destination);
        }

        private static void ApplyOutfitPlan(Vrm10Instance source, Transform destinationRoot, TransferPlan plan)
        {
            ClearOutfitPhysics(destinationRoot);
            ShapeSyncOutfitSpringBoneData data = destinationRoot.GetComponent<ShapeSyncOutfitSpringBoneData>()
                ?? destinationRoot.gameObject.AddComponent<ShapeSyncOutfitSpringBoneData>();
            data.ColliderGroups = new List<VRM10SpringBoneColliderGroup>();
            data.Springs = new List<Vrm10InstanceSpringBone.Spring>();
            data.SpringColliderGroupNames = new List<List<string>>();

            var groupMap = new Dictionary<VRM10SpringBoneColliderGroup, VRM10SpringBoneColliderGroup>();
            foreach (VRM10SpringBoneColliderGroup sourceGroup in plan.Groups)
            {
                Transform destinationTransform = EnsureRelativePath(destinationRoot, source.transform, sourceGroup.transform);
                // Preserve distinct group components even when their source
                // Transform is shared by multiple groups.
                VRM10SpringBoneColliderGroup destinationGroup =
                    destinationTransform.gameObject.AddComponent<VRM10SpringBoneColliderGroup>();
                destinationGroup.Name = sourceGroup.Name;
                destinationGroup.Colliders = new List<VRM10SpringBoneCollider>();
                foreach (VRM10SpringBoneCollider sourceCollider in sourceGroup.Colliders)
                {
                    Transform colliderTransform = EnsureRelativePath(destinationRoot, source.transform, sourceCollider.transform);
                    // Preserve every source collider component, including
                    // multiple colliders on the same Transform.
                    VRM10SpringBoneCollider destinationCollider =
                        colliderTransform.gameObject.AddComponent<VRM10SpringBoneCollider>();
                    destinationCollider.ColliderType = sourceCollider.ColliderType;
                    destinationCollider.Offset = sourceCollider.Offset;
                    destinationCollider.Radius = sourceCollider.Radius;
                    destinationCollider.Tail = sourceCollider.Tail;
                    destinationCollider.Normal = sourceCollider.Normal;
                    destinationGroup.Colliders.Add(destinationCollider);
                }
                groupMap.Add(sourceGroup, destinationGroup);
                data.ColliderGroups.Add(destinationGroup);
            }

            foreach (Vrm10InstanceSpringBone.Spring sourceSpring in plan.Springs)
            {
                var destinationSpring = new Vrm10InstanceSpringBone.Spring(sourceSpring.Name);
                if (sourceSpring.Center != null)
                    destinationSpring.Center = EnsureRelativePath(destinationRoot, source.transform, sourceSpring.Center);
                foreach (VRM10SpringBoneJoint sourceJoint in sourceSpring.Joints)
                {
                    Transform jointTransform = EnsureRelativePath(destinationRoot, source.transform, sourceJoint.transform);
                    VRM10SpringBoneJoint destinationJoint = jointTransform.GetComponent<VRM10SpringBoneJoint>()
                        ?? jointTransform.gameObject.AddComponent<VRM10SpringBoneJoint>();
                    CopyJoint(sourceJoint, destinationJoint);
                    destinationSpring.Joints.Add(destinationJoint);
                }
                foreach (VRM10SpringBoneColliderGroup sourceGroup in sourceSpring.ColliderGroups)
                    destinationSpring.ColliderGroups.Add(groupMap[sourceGroup]);
                data.Springs.Add(destinationSpring);
                data.SpringColliderGroupNames.Add(new List<string>());
            }

            EditorUtility.SetDirty(data);
        }

        private static void ClearFigurePhysics(Transform root, Vrm10Instance instance)
        {
            if (instance != null && instance.SpringBone != null)
            {
                instance.SpringBone.Springs?.Clear();
                instance.SpringBone.ColliderGroups?.Clear();
            }
            ClearPhysicsComponents(root);
        }

        private static void ClearOutfitPhysics(Transform root)
        {
            ShapeSyncOutfitSpringBoneData data = root.GetComponent<ShapeSyncOutfitSpringBoneData>();
            if (data != null)
            {
                data.Springs?.Clear();
                data.ColliderGroups?.Clear();
                data.SpringColliderGroupNames?.Clear();
            }
            ClearPhysicsComponents(root);
        }

        private static void ClearPhysicsComponents(Transform root)
        {
            foreach (VRM10SpringBoneJoint joint in root.GetComponentsInChildren<VRM10SpringBoneJoint>(true))
                if (joint != null) UnityEngine.Object.DestroyImmediate(joint);
            foreach (VRM10SpringBoneColliderGroup group in root.GetComponentsInChildren<VRM10SpringBoneColliderGroup>(true))
                if (group != null) UnityEngine.Object.DestroyImmediate(group);
            foreach (VRM10SpringBoneCollider collider in root.GetComponentsInChildren<VRM10SpringBoneCollider>(true))
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }

        private static void CopyJoint(VRM10SpringBoneJoint source, VRM10SpringBoneJoint destination)
        {
            destination.m_stiffnessForce = source.m_stiffnessForce;
            destination.m_gravityPower = source.m_gravityPower;
            Vector3 gravityWorld = source.transform.TransformDirection(source.m_gravityDir);
            destination.m_gravityDir = destination.transform.InverseTransformDirection(gravityWorld);
            destination.m_dragForce = source.m_dragForce;
            destination.m_jointRadius = source.m_jointRadius;
            destination.m_anglelimitType = source.m_anglelimitType;
            destination.m_limitSpaceOffset = source.m_limitSpaceOffset;
            destination.m_pitch = source.m_pitch;
            destination.m_yaw = source.m_yaw;
        }

        private static Transform EnsureRelativePath(Transform destinationRoot, Transform sourceRoot, Transform source)
        {
            string path = GetRelativePath(sourceRoot, source);
            if (path == null) throw new InvalidOperationException("Physics Transform is outside the Reference root.");
            Transform current = destinationRoot;
            Transform sourceCurrent = sourceRoot;
            if (string.IsNullOrEmpty(path)) return current;
            foreach (string segment in path.Split('/'))
            {
                if (string.IsNullOrEmpty(segment)) continue;
                Transform child = current.Find(segment);
                Transform sourceChild = sourceCurrent == null ? null : sourceCurrent.Find(segment);
                if (child == null)
                {
                    child = new GameObject(segment).transform;
                    child.SetParent(current, false);
                    if (sourceChild != null)
                    {
                        child.localPosition = sourceChild.localPosition;
                        child.localRotation = sourceChild.localRotation;
                        child.localScale = sourceChild.localScale;
                    }
                }
                current = child;
                sourceCurrent = sourceChild;
            }
            return current;
        }

        private static Transform ResolveFigureBoneTransform(Transform destinationRoot, Transform sourceRoot, Transform source)
        {
            if (source == null) return null;
            Transform byName = FindUniqueTransformByName(destinationRoot, source.name);
            if (byName != null) return byName;
            string path = GetRelativePath(sourceRoot, source);
            return path == null ? null : string.IsNullOrEmpty(path) ? destinationRoot : destinationRoot.Find(path);
        }

        private static Transform FindUniqueTransformByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            Transform result = null;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current.name == name)
                {
                    if (result != null) return null;
                    result = current;
                }
                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }
            return result;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return current == root ? string.Join("/", names.ToArray()) : null;
        }

        private static void AddGeneratedPath(ICollection<string> generatedPaths, string path)
        {
            if (generatedPaths == null || string.IsNullOrWhiteSpace(path)) return;
            if (!generatedPaths.Contains(path)) generatedPaths.Add(path);
        }
    }
}
#endif
