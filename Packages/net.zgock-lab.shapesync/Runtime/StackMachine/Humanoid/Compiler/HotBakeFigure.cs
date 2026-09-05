// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Single-spawn Hot Bake component that binds its child skeleton to an ancestor Animator.</summary>
    public sealed class HotBakeFigure : HotBakeSpawner
    {
        private Animator targetAnimator;
        private Avatar originalAvatar;
        private Avatar ownedAvatar;

        /// <summary>Starts the single Figure warm bake after the base input admission check.</summary>
        protected override void Start()
        {
            if (!TryPrepareFigure(out StackMachineDiagnostic diagnostic)) { SetLastDiagnostic(diagnostic); return; }
            base.Start();
        }

        /// <inheritdoc />
        public override bool Compile(out StackMachineDiagnostic diagnostic)
        {
            if (!TryPrepareFigure(out diagnostic)) { SetLastDiagnostic(diagnostic); return false; }
            bool started = base.Compile(out diagnostic);
            if (started) ReleaseBoundAvatar();
            return started;
        }

        /// <summary>Spawns exactly one child then assigns a Figure-root Avatar to the resolved Animator.</summary>
        public override bool TrySpawnAll(out StackMachineDiagnostic diagnostic)
        {
            if (!TryPrepareFigure(out diagnostic)) { SetLastDiagnostic(diagnostic); return false; }
            if (!base.TrySpawnAll(out diagnostic)) return false;
            ReleaseBoundAvatar();
            GameObject instance = SpawnedInstances.Count == 1 ? SpawnedInstances[0] : null;
            if (!TryBindAnimator(instance, out diagnostic))
            {
                DespawnAll();
                SetLastDiagnostic(diagnostic);
                return false;
            }
            return true;
        }

        /// <summary>Releases the Figure artifact scene scope during component teardown.</summary>
        protected override void OnDestroy()
        {
            ReleaseBoundAvatar();
            base.OnDestroy();
        }

        private bool TryPrepareFigure(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            SpawnTargets.Clear();
            SpawnTargets.Add(transform);
            if (ShapeSyncAnimatorResolver.TryResolve(transform, out Animator resolved, out diagnostic))
            {
                targetAnimator = resolved;
                return true;
            }
            return false;
        }

        private bool TryBindAnimator(GameObject instance, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (instance == null)
                return Reject("HotBakeFigureSkeletonRequired", "Hot Bake Figure requires one spawned skeleton.", out diagnostic);
            if (!instance.transform.IsChildOf(transform))
                return Reject("HotBakeFigureSkeletonOutsideBuildRoot", "Hot Bake Figure requires its spawned skeleton to be a descendant of the Figure-side Avatar build root.", out diagnostic);
            if (targetAnimator == null)
                return Reject("HotBakeFigureAnimatorRequired", "Hot Bake Figure requires one resolved Animator.", out diagnostic);
            Animator childAnimator = instance.GetComponentInChildren<Animator>(true);
            Avatar sourceAvatar = childAnimator == null ? null : childAnimator.avatar;
            if (sourceAvatar == null || !sourceAvatar.isHuman)
                return Reject("HotBakeFigureSourceAvatarRequired", "Hot Bake Figure spawned skeleton requires one valid Humanoid source Avatar.", out diagnostic);

            HumanDescription description = sourceAvatar.humanDescription;
            // The spawned child may already have sampled its Controller while it was
            // activated for spawn.  Its current Transform pose is runtime state, not
            // an Avatar rest skeleton.  Keep the Figure-side hierarchy names (the
            // parent root is new) but take matching source skeleton TRS values from
            // the compiler-produced Avatar so parent Animator retargeting starts
            // from the same authoring pose as the child Avatar.
            description.skeleton = BuildFigureRootSkeleton(sourceAvatar);
            Avatar rebuilt = AvatarBuilder.BuildHumanAvatar(gameObject, description);
            if (rebuilt == null || !rebuilt.isValid || !rebuilt.isHuman)
            {
                if (rebuilt != null) DestroyAvatar(rebuilt);
                return Reject("HotBakeFigureAvatarBuildFailed", "Hot Bake Figure could not build a valid Humanoid Avatar from its Figure-side root.", out diagnostic);
            }

            originalAvatar = targetAnimator.avatar;
            ownedAvatar = rebuilt;
            targetAnimator.avatar = ownedAvatar;
            targetAnimator.Rebind();
            if (childAnimator != null && childAnimator != targetAnimator) childAnimator.enabled = false;
            return true;
        }

        private void ReleaseBoundAvatar()
        {
            if (ownedAvatar == null) return;
            if (targetAnimator != null && targetAnimator.avatar == ownedAvatar)
            {
                targetAnimator.avatar = originalAvatar;
                targetAnimator.Rebind();
            }
            DestroyAvatar(ownedAvatar);
            ownedAvatar = null;
            originalAvatar = null;
        }

        private static void DestroyAvatar(Avatar value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private SkeletonBone[] BuildFigureRootSkeleton(Avatar sourceAvatar)
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            SkeletonBone[] sourceSkeleton = sourceAvatar == null ? null : sourceAvatar.humanDescription.skeleton;
            var sourceByName = new Dictionary<string, SkeletonBone>();
            if (sourceSkeleton != null)
                for (int i = 0; i < sourceSkeleton.Length; i++)
                    if (!string.IsNullOrEmpty(sourceSkeleton[i].name) && !sourceByName.ContainsKey(sourceSkeleton[i].name))
                        sourceByName.Add(sourceSkeleton[i].name, sourceSkeleton[i]);
            var skeleton = new List<SkeletonBone>(transforms.Length);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform value = transforms[i];
                // AvatarBuilder receives this GameObject as its root. Its scene
                // placement is not skeleton rest data: retaining a translated or
                // rotated build-root TRS here applies that placement a second time
                // during retargeting. The Figure root is therefore the identity
                // wrapper around the source Avatar's child skeleton.
                if (value == transform)
                {
                    skeleton.Add(new SkeletonBone
                    {
                        name = value.name,
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        scale = Vector3.one
                    });
                    continue;
                }
                if (sourceByName.TryGetValue(value.name, out SkeletonBone sourcePose))
                {
                    skeleton.Add(new SkeletonBone
                    {
                        name = value.name,
                        position = sourcePose.position,
                        rotation = sourcePose.rotation,
                        scale = sourcePose.scale
                    });
                    continue;
                }
                skeleton.Add(new SkeletonBone
                {
                    name = value.name,
                    position = value.localPosition,
                    rotation = value.localRotation,
                    scale = value.localScale
                });
            }
            return skeleton.ToArray();
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message);
            return false;
        }
    }
}
