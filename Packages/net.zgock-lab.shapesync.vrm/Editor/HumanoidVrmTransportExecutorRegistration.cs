// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using UnityEditor;
using UnityEngine;
using UniVRM10;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>Registers the UniVRM concrete executor after every Editor domain reload.</summary>
    [InitializeOnLoad]
    internal static class HumanoidVrmTransportExecutorRegistration
    {
        static HumanoidVrmTransportExecutorRegistration()
        {
            HumanoidVrmTransportExecutorProvider.Register(() => new HumanoidVrmTransportExecutor());
            HumanoidPureHumanoidComponentStripper.RegisterOptionalCandidateNormalizer(RemoveClonedVrmInstances);
        }

        private static void RemoveClonedVrmInstances(GameObject candidate)
        {
            if (candidate == null) return;
            Vrm10Instance[] instances = candidate.GetComponentsInChildren<Vrm10Instance>(true);
            for (int i = 0; i < instances.Length; i++) if (instances[i] != null) Object.DestroyImmediate(instances[i]);
        }
    }
}
#endif
