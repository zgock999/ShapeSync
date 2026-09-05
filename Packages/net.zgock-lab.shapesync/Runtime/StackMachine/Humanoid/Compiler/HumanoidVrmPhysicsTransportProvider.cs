// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Optional runtime transport boundary that exposes no UniVRM type to Core Runtime.</summary>
    public interface IHumanoidVrmPhysicsTransporter
    {
        /// <summary>Builds in-memory VRM physics for one unpublished candidate and transfers its disposable asset ownership on success.</summary>
        bool TryTransport(GameObject candidateRoot, GameObject figureSourceRoot, IReadOnlyList<GameObject> attachedOutfitSourceRoots, out IDisposable ownership, out StackMachineDiagnostic diagnostic);
    }

    /// <summary>Optional runtime boundary that reconstructs one spawned VRM graph from its retained Hot Bake template.</summary>
    public interface IHumanoidVrmPhysicsSpawnInitializer
    {
        /// <summary>Rebinds every physics reference to <paramref name="spawnRoot"/> and transfers spawned-instance lifetime ownership on success.</summary>
        bool TryInitializeSpawn(GameObject templateRoot, GameObject spawnRoot, out StackMachineDiagnostic diagnostic);
    }

    /// <summary>Runtime registration seam for the UniVRM companion assembly.</summary>
    /// <remarks>When UniVRM is absent no factory is registered, allowing Core Runtime and Hot Bake to remain functional without VRM transport.</remarks>
    public static class HumanoidVrmPhysicsTransportProvider
    {
        private static Func<IHumanoidVrmPhysicsTransporter> factory;
        private static Func<IHumanoidVrmPhysicsSpawnInitializer> spawnInitializerFactory;

        /// <summary>Gets whether the optional runtime integration has registered a concrete transport implementation.</summary>
        public static bool IsAvailable => factory != null;

        /// <summary>Registers the concrete optional transport factory.</summary>
        /// <param name="transporterFactory">Non-null factory supplied by the UniVRM companion assembly.</param>
        public static void Register(Func<IHumanoidVrmPhysicsTransporter> transporterFactory)
        {
            if (transporterFactory == null) throw new ArgumentNullException(nameof(transporterFactory));
            factory = transporterFactory;
        }

        /// <summary>Registers the optional spawned-instance initializer supplied by the UniVRM companion assembly.</summary>
        public static void RegisterSpawnInitializer(Func<IHumanoidVrmPhysicsSpawnInitializer> initializerFactory)
        {
            if (initializerFactory == null) throw new ArgumentNullException(nameof(initializerFactory));
            spawnInitializerFactory = initializerFactory;
        }

        /// <summary>Attempts to create a concrete optional transporter without loading or referencing UniVRM from Core Runtime.</summary>
        /// <param name="transporter">Registered transporter, or null when the optional assembly is unavailable.</param>
        /// <returns>True when a concrete optional transporter was created.</returns>
        public static bool TryCreate(out IHumanoidVrmPhysicsTransporter transporter)
        {
            transporter = factory?.Invoke();
            return transporter != null;
        }

        /// <summary>Reconstructs optional VRM runtime state for one spawn. A missing optional integration is a no-op because Core Runtime cannot create a VRM template without it.</summary>
        public static bool TryInitializeSpawn(GameObject templateRoot, GameObject spawnRoot, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            IHumanoidVrmPhysicsSpawnInitializer initializer = spawnInitializerFactory?.Invoke();
            return initializer == null || initializer.TryInitializeSpawn(templateRoot, spawnRoot, out diagnostic);
        }
    }
}
