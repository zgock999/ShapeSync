// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniGLTF.Utils;
using UniVRM10;
using UnityEngine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// One Outfit's Spring Bone entries attached to a Figure Vrm10Instance.
    /// The handle is intentionally runtime-only: source Outfit objects are destroyed after attach.
    /// </summary>
    public sealed class ShapeSyncVrmSpringBoneAttachment : IShapeSyncOptionalVrmAttachment
    {
        private readonly Vrm10Instance figureInstance;
        private readonly List<VRM10SpringBoneColliderGroup> colliderGroups;
        private readonly List<Vrm10InstanceSpringBone.Spring> springs;
        private readonly List<GameObject> createdObjects;
        private readonly HashSet<VRM10SpringBoneJoint> ownedJoints;
        private readonly HashSet<Transform> ownedJointTransforms;
        private readonly List<Transform> runtimeSourceCleanupRoots;
        private bool disposed;

        private ShapeSyncVrmSpringBoneAttachment(
            Vrm10Instance figureInstance,
            List<VRM10SpringBoneColliderGroup> colliderGroups,
            List<Vrm10InstanceSpringBone.Spring> springs,
            List<GameObject> createdObjects,
            HashSet<VRM10SpringBoneJoint> ownedJoints,
            HashSet<Transform> ownedJointTransforms,
            List<Transform> runtimeSourceCleanupRoots)
        {
            this.figureInstance = figureInstance;
            this.colliderGroups = colliderGroups;
            this.springs = springs;
            this.createdObjects = createdObjects;
            this.ownedJoints = ownedJoints;
            this.ownedJointTransforms = ownedJointTransforms;
            this.runtimeSourceCleanupRoots = runtimeSourceCleanupRoots;
        }

        /// <inheritdoc />
        public IReadOnlyList<Transform> RuntimeSourceCleanupRoots => runtimeSourceCleanupRoots;

        public static bool TryCreate(
            Vrm10Instance sourceInstance,
            Vrm10Instance figureInstance,
            Func<Transform, Transform> transformMapper,
            out ShapeSyncVrmSpringBoneAttachment attachment,
            out string error)
        {
            attachment = null;
            error = null;
            if (sourceInstance == null)
            {
                error = "Source Vrm10Instance is null.";
                return false;
            }
            if (sourceInstance == figureInstance)
            {
                error = "Source and Figure Vrm10Instance must be distinct.";
                return false;
            }

            if (sourceInstance.SpringBone == null)
            {
                error = "Source Spring Bone data is unavailable.";
                return false;
            }

            return TryCreate(sourceInstance.SpringBone.ColliderGroups, sourceInstance.SpringBone.Springs,
                null, sourceInstance.transform, figureInstance, transformMapper, out attachment, out error);
        }

        public static bool TryCreate(
            IReadOnlyList<VRM10SpringBoneColliderGroup> sourceGroups,
            IReadOnlyList<Vrm10InstanceSpringBone.Spring> sourceSprings,
            IReadOnlyList<List<string>> colliderGroupNames,
            Transform sourceRoot,
            Vrm10Instance figureInstance,
            Func<Transform, Transform> transformMapper,
            out ShapeSyncVrmSpringBoneAttachment attachment,
            out string error)
        {
            attachment = null;
            error = null;
            if (figureInstance == null || figureInstance.SpringBone == null || transformMapper == null || sourceRoot == null)
            {
                error = "Figure Spring Bone data or transform mapper is unavailable.";
                return false;
            }
            if (sourceGroups == null || sourceSprings == null)
            {
                error = "Source Spring Bone lists are unavailable.";
                return false;
            }

            var groupMap = new Dictionary<VRM10SpringBoneColliderGroup, VRM10SpringBoneColliderGroup>();
            var componentMap = new Dictionary<VRM10SpringBoneCollider, VRM10SpringBoneCollider>();
            var jointMap = new Dictionary<VRM10SpringBoneJoint, VRM10SpringBoneJoint>();
            var copiedGroups = new List<VRM10SpringBoneColliderGroup>();
            var copiedSprings = new List<Vrm10InstanceSpringBone.Spring>();
            var createdObjects = new List<GameObject>();
            var ownedJoints = new HashSet<VRM10SpringBoneJoint>();
            var ownedJointTransforms = new HashSet<Transform>();
            var runtimeSourceCleanupRoots = CollectDirectColliderGroupRoots(sourceRoot, sourceGroups);

            var referencedGroups = new HashSet<VRM10SpringBoneColliderGroup>();
            for (int i = 0; i < sourceSprings.Count; i++)
            {
                Vrm10InstanceSpringBone.Spring sourceSpring = sourceSprings[i];
                if (sourceSpring == null || sourceSpring.ColliderGroups == null) continue;
                for (int groupIndex = 0; groupIndex < sourceSpring.ColliderGroups.Count; groupIndex++)
                {
                    VRM10SpringBoneColliderGroup sourceGroup = sourceSpring.ColliderGroups[groupIndex];
                    if (sourceGroup != null) referencedGroups.Add(sourceGroup);
                }
            }

            for (int i = 0; i < sourceGroups.Count; i++)
            {
                VRM10SpringBoneColliderGroup sourceGroup = sourceGroups[i];
                // Imported assets can retain stale entries that no Spring uses.  They
                // must not make an otherwise valid Outfit attachment fail.
                if (sourceGroup == null || !referencedGroups.Contains(sourceGroup)) continue;

                VRM10SpringBoneColliderGroup destinationGroup = FindExistingGroup(figureInstance, sourceGroup.Name);
                bool reused = destinationGroup != null;
                if (destinationGroup == null && !TryCopyGroup(sourceGroup, sourceRoot, figureInstance.transform, transformMapper, componentMap, createdObjects, out destinationGroup, out error))
                {
                    error = error ?? $"Spring Bone ColliderGroup at index {i} is invalid.";
                    return false;
                }

                groupMap[sourceGroup] = destinationGroup;
                if (!reused && !copiedGroups.Contains(destinationGroup)) copiedGroups.Add(destinationGroup);
            }

            var occupiedJointTransforms = new HashSet<Transform>();
            if (figureInstance.SpringBone.Springs != null)
            {
                for (int i = 0; i < figureInstance.SpringBone.Springs.Count; i++)
                {
                    Vrm10InstanceSpringBone.Spring existingSpring = figureInstance.SpringBone.Springs[i];
                    if (existingSpring == null || existingSpring.Joints == null) continue;
                    for (int j = 0; j < existingSpring.Joints.Count; j++)
                    {
                        VRM10SpringBoneJoint existingJoint = existingSpring.Joints[j];
                        if (existingJoint != null && existingJoint.transform != null)
                        {
                            occupiedJointTransforms.Add(existingJoint.transform);
                        }
                    }
                }
            }

            for (int i = 0; i < sourceSprings.Count; i++)
            {
                Vrm10InstanceSpringBone.Spring sourceSpring = sourceSprings[i];
                if (sourceSpring == null || sourceSpring.Joints == null || sourceSpring.ColliderGroups == null)
                {
                    error = $"Spring at index {i} is null or has a null list.";
                    return false;
                }

                bool overlapsExistingSpring = false;
                var sourceSpringTransforms = new HashSet<Transform>();
                for (int jointIndex = 0; jointIndex < sourceSpring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint sourceJoint = sourceSpring.Joints[jointIndex];
                    if (sourceJoint == null)
                    {
                        error = $"Spring '{sourceSpring.Name}' joint at index {jointIndex} is invalid.";
                        return false;
                    }

                    Transform destinationTransform = transformMapper(sourceJoint.transform);
                    if (destinationTransform == null || !sourceSpringTransforms.Add(destinationTransform)
                        || occupiedJointTransforms.Contains(destinationTransform))
                    {
                        overlapsExistingSpring = true;
                        break;
                    }
                }
                if (overlapsExistingSpring)
                {
                    continue;
                }

                // Spring.Name is user-visible data. Ownership is tracked through
                // transplanted joint transforms so implementation UUIDs never leak.
                var destinationSpring = new Vrm10InstanceSpringBone.Spring(sourceSpring.Name);
                bool skipSpring = false;
                if (sourceSpring.Center != null)
                {
                    destinationSpring.Center = transformMapper(sourceSpring.Center);
                    if (destinationSpring.Center == null)
                    {
                        error = $"Spring '{sourceSpring.Name}' center could not be remapped.";
                        return false;
                    }
                }

                for (int jointIndex = 0; jointIndex < sourceSpring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint sourceJoint = sourceSpring.Joints[jointIndex];
                    if (sourceJoint == null || !TryCopyJoint(sourceJoint, transformMapper, jointMap, out VRM10SpringBoneJoint destinationJoint, out error))
                    {
                        if (error != null && error.Contains("destination component could not be created"))
                        {
                            skipSpring = true;
                            break;
                        }
                        error = error ?? $"Spring '{sourceSpring.Name}' joint at index {jointIndex} is invalid.";
                        return false;
                    }

                    destinationSpring.Joints.Add(destinationJoint);
                    ownedJoints.Add(destinationJoint);
                    ownedJointTransforms.Add(destinationJoint.transform);
                }

                if (skipSpring)
                {
                    error = null;
                    continue;
                }

                for (int jointIndex = 0; jointIndex < destinationSpring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint destinationJoint = destinationSpring.Joints[jointIndex];
                    if (destinationJoint != null && destinationJoint.transform != null)
                    {
                        occupiedJointTransforms.Add(destinationJoint.transform);
                    }
                }

                for (int groupIndex = 0; groupIndex < sourceSpring.ColliderGroups.Count; groupIndex++)
                {
                    VRM10SpringBoneColliderGroup sourceGroup = sourceSpring.ColliderGroups[groupIndex];
                    if (sourceGroup == null || !groupMap.TryGetValue(sourceGroup, out VRM10SpringBoneColliderGroup destinationGroup))
                    {
                        error = $"Spring '{sourceSpring.Name}' collider group at index {groupIndex} is not in the source group list.";
                        return false;
                    }

                    destinationSpring.ColliderGroups.Add(destinationGroup);
                }

                if (colliderGroupNames != null && i < colliderGroupNames.Count && colliderGroupNames[i] != null)
                {
                    for (int groupIndex = 0; groupIndex < colliderGroupNames[i].Count; groupIndex++)
                    {
                        string groupName = colliderGroupNames[i][groupIndex];
                        if (string.IsNullOrEmpty(groupName)) continue;
                        VRM10SpringBoneColliderGroup destinationGroup = FindFigureColliderGroup(figureInstance, groupName);
                        if (destinationGroup == null)
                        {
                            error = $"Spring '{sourceSpring.Name}' references missing Figure ColliderGroup '{groupName}'.";
                            return false;
                        }
                        destinationSpring.ColliderGroups.Add(destinationGroup);
                    }
                }

                copiedSprings.Add(destinationSpring);
            }

            // FastSpringBoneBufferFactory reads DefaultTransformStates while it
            // builds its immutable local-rotation cache. Register every copied
            // Joint before exposing the new Springs and before reconstruction.
            if (!TryRegisterDefaultTransformStates(figureInstance, copiedSprings, out error))
            {
                for (int i = 0; i < createdObjects.Count; i++)
                {
                    DestroyForLifecycle(createdObjects[i]);
                }
                return false;
            }

            for (int i = 0; i < copiedGroups.Count; i++)
            {
                figureInstance.SpringBone.ColliderGroups.Add(copiedGroups[i]);
            }

            for (int i = 0; i < copiedSprings.Count; i++)
            {
                figureInstance.SpringBone.Springs.Add(copiedSprings[i]);
            }

            attachment = new ShapeSyncVrmSpringBoneAttachment(
                figureInstance,
                copiedGroups,
                copiedSprings,
                createdObjects,
                ownedJoints,
                ownedJointTransforms,
                runtimeSourceCleanupRoots);
            return true;
        }

        private static List<Transform> CollectDirectColliderGroupRoots(Transform sourceRoot, IReadOnlyList<VRM10SpringBoneColliderGroup> sourceGroups)
        {
            var roots = new List<Transform>();
            if (sourceRoot == null || sourceGroups == null)
            {
                return roots;
            }

            for (int i = 0; i < sourceGroups.Count; i++)
            {
                VRM10SpringBoneColliderGroup group = sourceGroups[i];
                if (group == null || group.transform.parent != sourceRoot || roots.Contains(group.transform))
                {
                    continue;
                }

                // The object itself must own the collider-group component. Do not
                // infer from its name (for example, no dependency on "secondary").
                roots.Add(group.transform);
            }

            return roots;
        }

        private static VRM10SpringBoneColliderGroup FindExistingGroup(Vrm10Instance figureInstance, string name)
        {
            if (figureInstance?.SpringBone?.ColliderGroups == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < figureInstance.SpringBone.ColliderGroups.Count; i++)
            {
                VRM10SpringBoneColliderGroup group = figureInstance.SpringBone.ColliderGroups[i];
                if (group != null && group.Name == name) return group;
            }
            return null;
        }

        private static VRM10SpringBoneColliderGroup FindFigureColliderGroup(Vrm10Instance figureInstance, string name)
        {
            if (figureInstance?.SpringBone?.ColliderGroups == null || string.IsNullOrEmpty(name)) return null;
            VRM10SpringBoneColliderGroup result = null;
            for (int i = 0; i < figureInstance.SpringBone.ColliderGroups.Count; i++)
            {
                VRM10SpringBoneColliderGroup candidate = figureInstance.SpringBone.ColliderGroups[i];
                if (candidate == null || candidate.Name != name) continue;
                if (result != null) return null;
                result = candidate;
            }
            return result;
        }

        public void ReconstructOnce()
        {
            if (disposed || figureInstance == null || !Application.isPlaying)
            {
                return;
            }

            figureInstance.Runtime.SpringBone.ReconstructSpringBone();
        }

        private static bool TryRegisterDefaultTransformStates(
            Vrm10Instance figureInstance,
            IReadOnlyList<Vrm10InstanceSpringBone.Spring> copiedSprings,
            out string error)
        {
            error = null;
            if (figureInstance == null)
            {
                error = "Figure Vrm10Instance is required to register Spring Bone default transform states.";
                return false;
            }

            if (!(figureInstance.DefaultTransformStates is Dictionary<Transform, TransformState> states))
            {
                error = "Figure Vrm10Instance does not expose a mutable DefaultTransformStates cache.";
                return false;
            }

            for (int springIndex = 0; springIndex < copiedSprings.Count; springIndex++)
            {
                Vrm10InstanceSpringBone.Spring spring = copiedSprings[springIndex];
                if (spring?.Joints == null) continue;
                for (int jointIndex = 0; jointIndex < spring.Joints.Count; jointIndex++)
                {
                    VRM10SpringBoneJoint joint = spring.Joints[jointIndex];
                    if (joint != null && joint.transform != null && !states.ContainsKey(joint.transform))
                    {
                        states.Add(joint.transform, new TransformState(joint.transform));
                    }
                }
            }

            return true;
        }

        public void Dispose()
        {
            RemoveEntries(true);
        }

        public void Rollback()
        {
            RemoveEntries(false);
        }

        private void RemoveEntries(bool reconstruct)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            // This runs before OutfitAttacher destroys the transplanted Extra Bone
            // roots. ReconstructSpringBone must never observe this attachment's entries.
            if (figureInstance != null && figureInstance.SpringBone != null)
            {
                RemoveOwnedSpringEntries();

                for (int i = 0; i < colliderGroups.Count; i++)
                {
                    figureInstance.SpringBone.ColliderGroups.Remove(colliderGroups[i]);
                }

                if (reconstruct && Application.isPlaying)
                {
                    figureInstance.Runtime.SpringBone.ReconstructSpringBone();
                }
            }

            for (int i = 0; i < createdObjects.Count; i++)
            {
                DestroyForLifecycle(createdObjects[i]);
            }

        }

        private static bool TryCopyGroup(
            VRM10SpringBoneColliderGroup source,
            Transform sourceRoot,
            Transform destinationRoot,
            Func<Transform, Transform> mapper,
            Dictionary<VRM10SpringBoneCollider, VRM10SpringBoneCollider> componentMap,
            List<GameObject> createdObjects,
            out VRM10SpringBoneColliderGroup destination,
            out string error)
        {
            error = null;
            destination = null;
            Transform destinationTransform = mapper(source.transform);
            if (destinationTransform == null)
            {
                destinationTransform = CreateRelativePhysicsPath(destinationRoot, GetRelativePath(sourceRoot, source.transform), createdObjects);
            }

            if (source.Colliders == null)
            {
                error = $"ColliderGroup '{source.name}' transform or collider list could not be remapped.";
                return false;
            }

            destination = destinationTransform == source.transform
                ? source
                : GetOrAddComponent<VRM10SpringBoneColliderGroup>(destinationTransform, source, source.transform.GetComponents<VRM10SpringBoneColliderGroup>());
            destination.Name = source.Name;
            destination.Colliders = new List<VRM10SpringBoneCollider>(source.Colliders.Count);
            for (int i = 0; i < source.Colliders.Count; i++)
            {
                VRM10SpringBoneCollider sourceCollider = source.Colliders[i];
                if (sourceCollider == null)
                {
                    error = $"ColliderGroup '{source.name}' contains a null collider.";
                    return false;
                }

                Transform colliderTransform = mapper(sourceCollider.transform);
                if (colliderTransform == null)
                {
                    string colliderPath = GetRelativePath(source.transform, sourceCollider.transform);
                    colliderTransform = string.IsNullOrEmpty(colliderPath)
                        ? CreatePhysicsObject(destinationTransform, sourceCollider.gameObject.name, createdObjects)
                        : CreateRelativePhysicsPath(destinationTransform, colliderPath, createdObjects);
                }

                VRM10SpringBoneCollider destinationCollider = colliderTransform == sourceCollider.transform
                    ? sourceCollider
                    : GetOrAddComponent<VRM10SpringBoneCollider>(colliderTransform, sourceCollider, sourceCollider.transform.GetComponents<VRM10SpringBoneCollider>());
                destinationCollider.ColliderType = sourceCollider.ColliderType;
                destinationCollider.Offset = sourceCollider.Offset;
                destinationCollider.Radius = sourceCollider.Radius;
                destinationCollider.Tail = sourceCollider.Tail;
                destinationCollider.Normal = sourceCollider.Normal;
                componentMap[sourceCollider] = destinationCollider;
                destination.Colliders.Add(destinationCollider);
            }

            return true;
        }

        private static Transform CreatePhysicsObject(Transform destinationRoot, string name, List<GameObject> createdObjects)
        {
            GameObject created = new GameObject(string.IsNullOrEmpty(name) ? "ShapeSync SpringBone Collider" : name);
            created.transform.SetParent(destinationRoot, false);
            createdObjects?.Add(created);
            return created.transform;
        }

        private static Transform CreateRelativePhysicsPath(Transform destinationRoot, string path, List<GameObject> createdObjects)
        {
            if (string.IsNullOrEmpty(path))
            {
                return destinationRoot;
            }

            Transform current = destinationRoot;
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i])) continue;
                Transform child = current.Find(segments[i]);
                if (child == null)
                {
                    child = CreatePhysicsObject(current, segments[i], createdObjects);
                }

                current = child;
            }

            return current;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
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

        private static void DestroyForLifecycle(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static bool TryCopyJoint(
            VRM10SpringBoneJoint source,
            Func<Transform, Transform> mapper,
            Dictionary<VRM10SpringBoneJoint, VRM10SpringBoneJoint> map,
            out VRM10SpringBoneJoint destination,
            out string error)
        {
            error = null;
            destination = null;
            if (map.TryGetValue(source, out destination))
            {
                return true;
            }

            if (source == null || source.transform == null || mapper == null)
            {
                error = "Spring Bone Joint or its Transform is null.";
                return false;
            }

            Transform destinationTransform = mapper(source.transform);
            if (destinationTransform == null)
            {
                error = $"Joint '{source.name}' transform could not be remapped.";
                return false;
            }

            destination = destinationTransform == source.transform
                ? source
                : GetOrAddComponent<VRM10SpringBoneJoint>(destinationTransform, source, source.transform.GetComponents<VRM10SpringBoneJoint>());
            if (destination == null)
            {
                error = $"Joint '{source.name}' destination component could not be created.";
                return false;
            }
            destination.m_stiffnessForce = source.m_stiffnessForce;
            destination.m_gravityPower = source.m_gravityPower;
            // UniVRM sends this serialized value directly to the spring job as
            // a world-force vector. It is not a Transform-local direction.
            destination.m_gravityDir = source.m_gravityDir;
            destination.m_dragForce = source.m_dragForce;
            destination.m_jointRadius = source.m_jointRadius;
            destination.m_anglelimitType = source.m_anglelimitType;
            destination.m_limitSpaceOffset = source.m_limitSpaceOffset;
            destination.m_pitch = source.m_pitch;
            destination.m_yaw = source.m_yaw;
            map[source] = destination;
            return true;
        }

        private static T GetOrAddComponent<T>(Transform destination, T source, T[] existing) where T : Component
        {
            if (destination == null || source == null) return null;
            if (destination == source.transform) return source;

            int ordinal = 0;
            T[] sourceComponents = source.transform.GetComponents<T>();
            for (int i = 0; i < sourceComponents.Length; i++)
            {
                if (sourceComponents[i] == source)
                {
                    ordinal = i;
                    break;
                }
            }

            T[] destinationComponents = destination.GetComponents<T>();
            if (ordinal < destinationComponents.Length && destinationComponents[ordinal] != null)
            {
                return destinationComponents[ordinal];
            }

            return destination.gameObject.AddComponent<T>();
        }

        private bool ContainsOwnedJoint(Vrm10InstanceSpringBone.Spring spring)
        {
            if (spring?.Joints == null || ownedJoints == null || ownedJoints.Count == 0) return false;
            for (int i = 0; i < spring.Joints.Count; i++)
            {
                VRM10SpringBoneJoint joint = spring.Joints[i];
                if (ownedJoints.Contains(joint)) return true;
                if (joint != null && ownedJointTransforms.Contains(joint.transform)) return true;
            }
            return false;
        }

        private bool IsOwnedSpring(Vrm10InstanceSpringBone.Spring spring)
        {
            if (spring == null)
            {
                return false;
            }

            // UniVRM may rebuild Spring records. Their mapped joint transforms
            // persist across reconstruction and are the durable ownership key.
            return springs.Contains(spring) || ContainsOwnedJoint(spring);
        }

        private void RemoveOwnedSpringEntries()
        {
            if (figureInstance?.SpringBone?.Springs == null)
            {
                return;
            }

            for (int i = figureInstance.SpringBone.Springs.Count - 1; i >= 0; i--)
            {
                if (IsOwnedSpring(figureInstance.SpringBone.Springs[i]))
                {
                    figureInstance.SpringBone.Springs.RemoveAt(i);
                }
            }
        }

    }
}
#endif
