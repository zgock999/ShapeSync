// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Runtime.CompilerServices;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

[assembly: InternalsVisibleTo("zgock.ShapeSync.Tests.EditMode")]
[assembly: InternalsVisibleTo("zgock.ShapeSync.VrmIntegration.Editor")]
[assembly: InternalsVisibleTo("zgock.ShapeSync.Tests.EditMode.VrmIntegration")]

namespace zgock.ShapeSync.Editor
{
    /// <summary>Applies the minimal Spec14 and Spec15.1 runtime component graph to a Builder-generated Prefab root.</summary>
    /// <remarks>This Editor-only helper only adds co-located components and assigns their direct references. It never creates Texture hosts, dictionaries, Documents, runtime deliveries, or transactions.</remarks>
    internal static class BuilderRuntimeComponentSetup
    {
        internal static void Ensure(GameObject prefabRoot, DynamicBoneBlender dynamicBoneBlender = null)
        {
            MaterialProxy proxy = GetOrAdd<MaterialProxy>(prefabRoot);
            MaterialAttacher attacher = GetOrAdd<MaterialAttacher>(prefabRoot);
            attacher.Proxy = proxy;

            MaterialStackMachine materialStackMachine = GetOrAdd<MaterialStackMachine>(prefabRoot);
            materialStackMachine.MaterialAttacher = attacher;

            GetOrAdd<MeshStackMachine>(prefabRoot);
            NormalBlender normalBlender = GetOrAdd<NormalBlender>(prefabRoot);
            if (dynamicBoneBlender != null) normalBlender.DynamicBoneBlender = dynamicBoneBlender;
        }

        private static T GetOrAdd<T>(GameObject prefabRoot) where T : Component
        {
            T component = prefabRoot.GetComponent<T>();
            return component != null ? component : prefabRoot.AddComponent<T>();
        }
    }
}
