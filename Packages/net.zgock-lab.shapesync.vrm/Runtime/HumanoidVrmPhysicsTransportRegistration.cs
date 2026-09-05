// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>Registers the UniVRM transport in both runtime and Edit Mode domains.</summary>
    internal static class HumanoidVrmPhysicsTransportRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterRuntimeTransport()
        {
            HumanoidVrmPhysicsTransportProvider.Register(() => new Transporter());
            HumanoidVrmPhysicsTransportProvider.RegisterSpawnInitializer(() => new SpawnInitializer());
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorTransport()
        {
            RegisterRuntimeTransport();
        }
#endif

        private sealed class Transporter : IHumanoidVrmPhysicsTransporter
        {
            public bool TryTransport(GameObject candidateRoot, GameObject figureSourceRoot, IReadOnlyList<GameObject> attachedOutfitSourceRoots, out IDisposable ownership, out StackMachineDiagnostic diagnostic)
            {
                ownership = null;
                var request = new VrmTransportPhysicsRequest(candidateRoot, figureSourceRoot, attachedOutfitSourceRoots);
                if (!VrmIntegrationService.TransportPhysics(request, out VrmTransportPhysicsResult result, out diagnostic)) return false;
                ownership = result;
                return true;
            }
        }

        private sealed class SpawnInitializer : IHumanoidVrmPhysicsSpawnInitializer
        {
            public bool TryInitializeSpawn(GameObject templateRoot, GameObject spawnRoot, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = null;
                if (templateRoot == null || spawnRoot == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmSpawnTemplateRequired", "VRM spawn initialization requires both the retained template and its spawned clone.");
                    return false;
                }
                if (!VrmIntegrationService.RebuildSpawnPhysics(templateRoot, spawnRoot, out VrmTransportPhysicsResult ownership, out diagnostic)) return false;
                if (ownership == null) return true;
                VrmSpawnPhysicsOwnership.Attach(spawnRoot, ownership);
                return true;
            }
        }

        private sealed class VrmSpawnPhysicsOwnership : MonoBehaviour
        {
            private VrmTransportPhysicsResult ownership;

            internal static void Attach(GameObject root, VrmTransportPhysicsResult result)
            {
                if (root == null || result == null) return;
                VrmSpawnPhysicsOwnership holder = root.AddComponent<VrmSpawnPhysicsOwnership>();
                holder.ownership = result;
            }

            private void OnDestroy()
            {
                ownership?.Dispose();
                ownership = null;
            }
        }
    }
}
#endif
