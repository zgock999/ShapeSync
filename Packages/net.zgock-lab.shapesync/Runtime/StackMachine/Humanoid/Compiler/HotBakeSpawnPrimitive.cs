// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Creates caller-owned Pure Humanoid instances from one scene-local Hot Bake artifact.</summary>
    public static class HotBakeSpawnPrimitive
    {
        /// <summary>Creates one caller-owned visible instance from a retained artifact template.</summary>
        /// <param name="initializeVrmPhysics">True only when the component transaction transported VRM physics into the retained template.</param>
        /// <remarks>Spawn initialization is part of the optional Physics Transport transaction.  It must not run merely because the optional integration is installed.</remarks>
        public static bool TrySpawn(HotBakeArtifactSceneScope scope, Transform parent, Vector3 localPosition, Quaternion localRotation, bool initializeVrmPhysics, out GameObject instance, out StackMachineDiagnostic diagnostic)
        {
            instance = null;
            diagnostic = null;
            if (scope == null || scope.ArtifactSet == null || !scope.ArtifactSet.IsAvailable)
            { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeArtifactUnavailable", "Hot Bake spawn requires one available scene-scoped artifact."); return false; }
            if (parent == null)
            { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeSpawnParentRequired", "Hot Bake spawn requires one parent in the artifact scene."); return false; }
            if (parent.gameObject.scene != scope.ArtifactSet.TemplateRoot.scene)
            { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeSpawnParentSceneMismatch", "Hot Bake spawn parent must share the artifact template scene."); return false; }
            instance = Object.Instantiate(scope.ArtifactSet.TemplateRoot);
            instance.transform.SetParent(parent, false);
            instance.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            PrepareVisibleRuntimeHierarchy(instance.transform);
            if (initializeVrmPhysics && !HumanoidVrmPhysicsTransportProvider.TryInitializeSpawn(scope.ArtifactSet.TemplateRoot, instance, out diagnostic))
            {
                Object.Destroy(instance);
                instance = null;
                return false;
            }
            // The retained artifact is intentionally inactive so it never renders by itself.
            // A Spawner clone is the visible runtime product and must not inherit that state.
            instance.SetActive(true);
            if (scope.TryRegisterSpawn(instance, out diagnostic)) return true;
            Object.Destroy(instance);
            instance = null;
            return false;
        }

        internal static void PrepareVisibleRuntimeHierarchy(Transform root)
        {
            // Mesh Core's retained template is a non-persistent hidden working hierarchy.
            // Its Spawn product is a normal scene object: Play Mode owns its lifetime, so it
            // must not retain HideInHierarchy or any DontSave flags from the template.
            root.gameObject.hideFlags = HideFlags.None;
            for (int i = 0; i < root.childCount; i++) PrepareVisibleRuntimeHierarchy(root.GetChild(i));
        }
    }
}
