// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>Concrete optional UniVRM bridge for the Spec17.6 controller.</summary>
    public sealed class HumanoidVrmTransportExecutor : IHumanoidVrmTransportExecutor
    {
        // Test seams keep the concrete Editor failure ownership paths observable without
        // moving AssetDatabase ownership into the Runtime service.
        internal static Action<GameObject> SavePrefabAsset = root => PrefabUtility.SavePrefabAsset(root);
        internal static Action SaveAllAssets = AssetDatabase.SaveAssets;

        public bool TryTransport(GameObject candidate, GameObject figureSourceRoot, ShapeSyncDocument document, HumanoidVrmTransportProvenance provenance, out IDisposable result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            if (!HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, provenance?.AttachedOutfitLogicalNames, out var outfits, out diagnostic)) return false;
            if (!VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figureSourceRoot, outfits), out VrmTransportPhysicsResult transportResult, out diagnostic)) return false;
            result = transportResult;
            return true;
        }

        public bool TryStageAssets(IDisposable transportResult, string outputFolder, string relativeFolder, string documentName, out IReadOnlyList<string> assetPaths, out StackMachineDiagnostic diagnostic)
        {
            assetPaths = Array.Empty<string>();
            if (!(transportResult is VrmTransportPhysicsResult vrmResult))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishResultTypeInvalid", "VRM asset staging requires the result created by HumanoidVrmTransportExecutor.");
                return false;
            }
            bool staged = HumanoidVrmAssetStager.TryStage(outputFolder, relativeFolder, documentName, vrmResult, out HumanoidVrmAssetStage stage, out diagnostic);
            if (stage != null) assetPaths = stage.AssetPaths;
            // Persistent partial assets cannot remain owned by the in-memory result: controller
            // failure cleanup must preserve them and report their paths as warnings.
            if (!staged && assetPaths.Count > 0) ReleasePartialStageFailureOwnership(vrmResult);
            return staged;
        }

        public bool TryFinalizeAssets(IDisposable transportResult, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!(transportResult is VrmTransportPhysicsResult vrmResult) || vrmResult.Vrm == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishResultTypeInvalid", "VRM asset finalize requires the staged result created by HumanoidVrmTransportExecutor.");
                return false;
            }
            if (publishedPrefabRoot == null || !PrefabUtility.IsPartOfPrefabAsset(publishedPrefabRoot))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishPrefabRequired", "VRM asset finalize requires the persistent published Pure Humanoid Prefab root.");
                return false;
            }
            try
            {
                Vrm10Instance publishedInstance = publishedPrefabRoot.GetComponent<Vrm10Instance>();
                if (publishedInstance == null)
                {
                    vrmResult.ReleaseAssetOwnership();
                    diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishInstanceRequired", "VRM asset finalize requires Vrm10Instance on the published Pure Humanoid Prefab root.");
                    return false;
                }
                publishedInstance.Vrm = vrmResult.Vrm;
                EditorUtility.SetDirty(publishedInstance);
                vrmResult.Vrm.Prefab = publishedPrefabRoot;
                IReadOnlyList<UniVRM10.VRM10Expression> expressions = vrmResult.Expressions;
                for (int i = 0; i < expressions.Count; i++)
                    if (expressions[i] != null) { expressions[i].Prefab = publishedPrefabRoot; EditorUtility.SetDirty(expressions[i]); }
                EditorUtility.SetDirty(vrmResult.Vrm);
                SavePrefabAsset(publishedPrefabRoot);
                SaveAllAssets();
                vrmResult.ReleaseAssetOwnership();
                return true;
            }
            catch (Exception exception)
            {
                // Staged VRM assets are already persistent at this point. Preserve them for
                // the 17.6 residual-artifact Warning path instead of letting controller cleanup
                // destroy the objects through the in-memory result.
                vrmResult.ReleaseAssetOwnership();
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishFinalizeFailed", "VRM asset finalize could not bind the published Prefab.", detail: exception.Message);
                return false;
            }
        }

        public void ReleaseAssetOwnership(IDisposable transportResult)
        {
            if (transportResult is VrmTransportPhysicsResult vrmResult) vrmResult.ReleaseAssetOwnership();
        }

        private static void ReleasePartialStageFailureOwnership(VrmTransportPhysicsResult result)
        {
            // AssetDatabase.CreateAsset transfers only the paths that completed before the
            // failure. Dispose the still-transient objects first; then release the remaining
            // persistent assets so controller cleanup cannot delete warning residuals.
            IReadOnlyList<VRM10Expression> expressions = result.Expressions;
            for (int i = 0; i < expressions.Count; i++)
            {
                VRM10Expression expression = expressions[i];
                if (expression != null && !AssetDatabase.Contains(expression)) UnityEngine.Object.DestroyImmediate(expression);
            }
            if (result.Vrm != null && !AssetDatabase.Contains(result.Vrm)) UnityEngine.Object.DestroyImmediate(result.Vrm);
            result.ReleaseAssetOwnership();
        }
    }
}
#endif
