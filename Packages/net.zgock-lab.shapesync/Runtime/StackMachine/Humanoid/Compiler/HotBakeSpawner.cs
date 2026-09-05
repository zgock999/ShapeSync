// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Component owner for one bake and N caller-selected spawn targets.</summary>
    public class HotBakeSpawner : HotBakeComponentBase
    {
        [SerializeField] private List<Transform> spawnTargets = new List<Transform>();
        private readonly List<GameObject> spawned = new List<GameObject>();
        private HotBakeArtifactSceneScope scope;

        /// <summary>Gets the serialized spawn targets; each non-null target receives one Pure Humanoid instance.</summary>
        public IList<Transform> SpawnTargets => spawnTargets;
        /// <summary>Gets instances owned by this component until despawn or owner teardown.</summary>
        public IReadOnlyList<GameObject> SpawnedInstances => spawned;

        /// <summary>Creates the owner scene scope before evaluating base Startup admission.</summary>
        protected override void Start()
        {
            EnsureScope(out _);
            base.Start();
        }

        /// <summary>Begins a replacement bake after removing this owner's previous spawned instances.</summary>
        public override bool Compile(out StackMachineDiagnostic diagnostic)
        {
            if (IsCompileActive) return base.Compile(out diagnostic);
            if (!base.Compile(out diagnostic)) return false;
            DespawnAll();
            return true;
        }

        /// <summary>Pumps the active build and maintains the artifact scene-scope invariant.</summary>
        /// <remarks>Derived spawners overriding this hook must preserve one pump per frame and scope validation before using the promoted artifact.</remarks>
        protected virtual void Update()
        {
            if (scope != null && !scope.Validate(out StackMachineDiagnostic scopeDiagnostic))
                SetLastDiagnostic(scopeDiagnostic);
            if (!IsCompileActive) return;
            if (!EnsureScope(out StackMachineDiagnostic diagnostic)) { SetLastDiagnostic(diagnostic); return; }

            HumanoidBuildOperationStatus status = PumpAndCommitCompile(scope, out diagnostic);
            if (status == HumanoidBuildOperationStatus.Succeeded)
                TrySpawnAll(out diagnostic);
            if (status != HumanoidBuildOperationStatus.Pending && diagnostic != null)
                SetLastDiagnostic(diagnostic);
        }

        /// <summary>Replaces this owner's instances with one instance at every configured target after a successful bake.</summary>
        public virtual bool TrySpawnAll(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (scope == null || ArtifactSet == null || !ArtifactSet.IsAvailable || scope.ArtifactSet != ArtifactSet)
                return Reject("HotBakeArtifactUnavailable", "Hot Bake Spawner requires one completed scene-scoped artifact before spawning.", out diagnostic);
            if (!TryValidateSpawnTargets(out diagnostic)) return false;
            DespawnAll();
            var created = new List<GameObject>();
            for (int i = 0; i < spawnTargets.Count; i++)
            {
                Transform target = spawnTargets[i];
                if (target == null || !HotBakeSpawnPrimitive.TrySpawn(scope, target, Vector3.zero, Quaternion.identity, IsPhysicsTransportEnabled, out GameObject instance, out diagnostic))
                {
                    if (target == null) Reject("HotBakeSpawnTargetRequired", "Hot Bake Spawner has a null spawn target.", out diagnostic);
                    for (int j = created.Count - 1; j >= 0; j--)
                    {
                        scope.UnregisterSpawn(created[j]);
                        spawned.Remove(created[j]);
                        DestroyOwnedInstance(created[j]);
                    }
                    return false;
                }
                created.Add(instance);
                spawned.Add(instance);
            }
            return true;
        }

        /// <summary>Destroys only instances created by this component and unregisters them from its scope.</summary>
        public void DespawnAll()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                GameObject instance = spawned[i];
                scope?.UnregisterSpawn(instance);
                DestroyOwnedInstance(instance);
            }
            spawned.Clear();
        }

        /// <summary>Despawns caller-owned instances and releases the associated scene scope.</summary>
        protected override void OnDestroy()
        {
            DespawnAll();
            scope?.Dispose();
            scope = null;
            base.OnDestroy();
        }

        private bool EnsureScope(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (scope != null) return true;
            if (ScopeHost == null) return Reject("HotBakeHostRequired", "Hot Bake Spawner requires one TextureStackMachineHost before completion.", out diagnostic);
            scope = new HotBakeArtifactSceneScope(gameObject, ScopeHost);
            return true;
        }

        private bool TryValidateSpawnTargets(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            for (int i = 0; i < spawnTargets.Count; i++)
            {
                Transform target = spawnTargets[i];
                if (target == null)
                    return Reject("HotBakeSpawnTargetRequired", "Hot Bake Spawner has a null spawn target.", out diagnostic);
                if (target.gameObject.scene != scope.ArtifactSet.TemplateRoot.scene)
                    return Reject("HotBakeSpawnParentSceneMismatch", "Hot Bake spawn parent must share the artifact template scene.", out diagnostic);
            }
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message);
            return false;
        }

        private static void DestroyOwnedInstance(GameObject instance)
        {
            if (instance == null) return;
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }
    }
}
