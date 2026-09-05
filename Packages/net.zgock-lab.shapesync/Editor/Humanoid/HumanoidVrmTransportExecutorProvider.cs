// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Editor-assembly registration seam for the optional UniVRM executor.
    /// The main Editor UI asks this provider only when VRM transport is selected;
    /// VrmIntegration.Editor registers the concrete implementation on domain load.
    /// </summary>
    public static class HumanoidVrmTransportExecutorProvider
    {
        private static Func<IHumanoidVrmTransportExecutor> factory;

        /// <summary>Gets whether the optional integration has registered a concrete executor factory.</summary>
        public static bool IsAvailable => factory != null;

        /// <summary>Registers the domain-local factory used to create optional UniVRM transport executors.</summary>
        /// <param name="executorFactory">A non-null factory registered by the optional UniVRM Editor assembly.</param>
        public static void Register(Func<IHumanoidVrmTransportExecutor> executorFactory)
        {
            if (executorFactory == null) throw new ArgumentNullException(nameof(executorFactory));
            factory = executorFactory;
        }

        /// <summary>Creates one optional executor without requiring the main Editor assembly to reference UniVRM.</summary>
        /// <param name="executor">The new executor when a factory is registered and succeeds; otherwise null.</param>
        /// <returns>True when a concrete executor was created.</returns>
        public static bool TryCreate(out IHumanoidVrmTransportExecutor executor)
        {
            executor = factory?.Invoke();
            return executor != null;
        }
    }
}
