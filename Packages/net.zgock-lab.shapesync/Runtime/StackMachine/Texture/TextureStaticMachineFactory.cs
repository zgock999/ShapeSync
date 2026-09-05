// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Resolves the one Texture StackMachine Host for the active scene.</summary>
    /// <remarks>The Factory adopts exactly one pre-placed Host or instantiates the Host Prefab configured by the required project Settings asset. It does not select among multiple Hosts, create fallback shaders, or retain Hosts across scenes.</remarks>
    public static class TextureStaticMachineFactory
    {
        private const string SettingsResourcePath = "zgock/ShapeSync/TextureStaticMachineFactorySettings";
        private static readonly Dictionary<int, TextureStackMachineHost> cachedHosts = new Dictionary<int, TextureStackMachineHost>();

        /// <summary>Gets the active scene's sole Texture StackMachine Host, creating it from the required configuration only when absent.</summary>
        /// <param name="host">The resolved scene-local Host on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic"><see langword="null"/> on success; otherwise a structured singleton, configuration, or Prefab rejection.</param>
        /// <returns><see langword="true"/> when exactly one scene-local Host was resolved.</returns>
        public static bool TryGetTSM(out TextureStackMachineHost host, out StackMachineDiagnostic diagnostic)
        {
            return TryGetTSM(Resources.Load<TextureStaticMachineFactorySettings>(SettingsResourcePath), SceneManager.GetActiveScene(), out host, out diagnostic);
        }

        internal static bool TryGetExistingTSM(out TextureStackMachineHost host)
        {
            Scene scene = SceneManager.GetActiveScene();
            host = null;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (cachedHosts.TryGetValue(scene.handle, out TextureStackMachineHost cached) && cached != null && cached.gameObject.scene == scene)
            {
                host = cached;
                return true;
            }
            cachedHosts.Remove(scene.handle);
            return false;
        }

        internal static bool TryGetTSM(TextureStaticMachineFactorySettings settings, Scene scene, out TextureStackMachineHost host, out StackMachineDiagnostic diagnostic)
        {
            host = null;
            diagnostic = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                diagnostic = Failure("ActiveSceneRequired", "Texture StackMachine Factory requires a loaded active scene.");
                return false;
            }
            TextureStackMachineHost existing = FindSingleHost(scene, out int count);
            if (count > 1)
            {
                diagnostic = Failure("MultipleHosts", "Texture StackMachine Factory found more than one TextureStackMachineHost in the active scene.");
                return false;
            }
            if (count == 1)
            {
                cachedHosts[scene.handle] = existing;
                host = existing;
                return true;
            }
            cachedHosts.Remove(scene.handle);
            if (!TryValidateSettings(settings, out TextureStackMachineHost prefab, out diagnostic)) return false;
            host = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            cachedHosts[scene.handle] = host;
            return true;
        }

        internal static void ResetForTests()
        {
            cachedHosts.Clear();
        }

        internal static void Invalidate(Scene scene)
        {
            if (scene.IsValid()) cachedHosts.Remove(scene.handle);
        }

        private static TextureStackMachineHost FindSingleHost(Scene scene, out int count)
        {
            TextureStackMachineHost found = null;
            count = 0;
            TextureStackMachineHost[] hosts = Object.FindObjectsByType<TextureStackMachineHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < hosts.Length; i++)
            {
                TextureStackMachineHost candidate = hosts[i];
                if (candidate == null || candidate.gameObject.scene != scene) continue;
                count++;
                if (count == 1) found = candidate;
            }
            return found;
        }

        internal static bool TryValidateSettings(TextureStaticMachineFactorySettings settings, out TextureStackMachineHost prefab, out StackMachineDiagnostic diagnostic)
        {
            prefab = null;
            if (settings == null)
            {
                diagnostic = Failure("FactoryConfigurationRequired", "Texture StackMachine Factory requires TextureStaticMachineFactorySettings at Resources/zgock/ShapeSync/TextureStaticMachineFactorySettings.");
                return false;
            }
            prefab = settings.TextureStackMachineHostPrefab;
            if (prefab == null)
            {
                diagnostic = Failure("FactoryPrefabRequired", "Texture StackMachine Factory Settings requires a TextureStackMachineHost Prefab.");
                return false;
            }
            if (prefab.ComputeProgram == null)
            {
                diagnostic = Failure("FactoryComputeProgramRequired", "Texture StackMachine Host Prefab requires a Texture ComputeShader.");
                return false;
            }
            if (prefab.NormalComputeProgram == null)
            {
                diagnostic = Failure("FactoryNormalComputeProgramRequired", "Texture StackMachine Host Prefab requires a Normal ComputeShader.");
                return false;
            }
            diagnostic = null;
            return true;
        }

        private static StackMachineDiagnostic Failure(string code, string message)
        {
            return StackMachineDiagnostic.CreateDomain("texture", code, message);
        }
    }
}
