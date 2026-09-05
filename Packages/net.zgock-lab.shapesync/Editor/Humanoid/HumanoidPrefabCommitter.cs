// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Persistent Prefab artifact created by the final Editor publish transaction.</summary>
    internal sealed class HumanoidPrefabCommit
    {
        internal HumanoidPrefabCommit(string assetPath, GameObject prefabAsset)
        {
            AssetPath = assetPath;
            PrefabAsset = prefabAsset;
        }

        internal string AssetPath { get; }
        internal GameObject PrefabAsset { get; }
    }

    /// <summary>Creates and reload-verifies the Pure Humanoid Prefab after all individual assets are staged.</summary>
    internal static class HumanoidPrefabCommitter
    {
        // Kept internal so failure paths remain directly observable without exposing an Editor API.
        internal static Func<GameObject, string, GameObject> SavePrefabAsset = (candidate, path) => PrefabUtility.SaveAsPrefabAsset(candidate, path);
        internal static Action SaveAssets = AssetDatabase.SaveAssets;

        internal static bool TryCommit(GameObject candidate, HumanoidIndividualAssetStage stage, string outputFolder, string documentName, out HumanoidPrefabCommit commit, out StackMachineDiagnostic diagnostic)
        {
            commit = null;
            diagnostic = null;
            if (candidate == null) return Reject("PublishCandidateRequired", "Prefab commit requires an unpublished Pure Humanoid candidate.", out diagnostic);
            if (stage == null || stage.Mesh == null) return Reject("PublishStageRequired", "Prefab commit requires applied individual assets.", out diagnostic);
            if (stage.OutputContract == null) return Reject("PublishOutputContractMissing", "Prefab commit requires the Spec17 output naming contract captured during staging.", out diagnostic);
            if (string.IsNullOrWhiteSpace(documentName)) return Reject("PublishDocumentNameRequired", "Prefab commit requires a document name.", out diagnostic);
            if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder)) return Reject("PublishOutputFolderRequired", "Prefab commit requires an existing Assets/ output folder.", out diagnostic);
            if (!HumanoidIndividualAssetStager.TryGetOutputFolderName(outputFolder, out string assetPrefix)) return Reject("PublishOutputFolderNameRequired", "Prefab commit requires an output folder name.", out diagnostic);

            string assetPath = (outputFolder.TrimEnd('/', '\\') + "/" + assetPrefix + ".prefab").Replace('\\', '/');
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) return Reject("PublishAssetPathOccupied", "Prefab commit found an occupied output asset path.", out diagnostic);

            try
            {
                // The unpublished clone is intentionally DontSave. The emitted Prefab must not inherit it.
                SetSaveable(candidate.transform);
                candidate.SetActive(true);
                if (stage.Avatar != null && candidate.GetComponentsInChildren<Animator>(true).Length != 1)
                    return Reject("PublishCandidateAnimatorCountInvalid", "Prefab commit requires exactly one candidate Animator for the staged Avatar.", out diagnostic);
                GameObject prefab = SavePrefabAsset(candidate, assetPath);
                if (prefab == null) return Reject("PublishPrefabSaveFailed", "Prefab commit did not create a persistent Prefab asset.", out diagnostic);
                SaveAssets();
                if (!TryVerifyReload(assetPath, stage, out diagnostic)) return false;
                if (!HumanoidPublishPathValidator.TryValidateOutputContract(assetPath, stage.OutputContract, out diagnostic)) return false;
                string outputPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty;
                if (!HumanoidPublishPathValidator.TryValidateOutputReferences(assetPath, outputPath, out diagnostic)) return false;
                GameObject persistentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (persistentPrefab == null) return Reject("PublishPrefabReloadFailed", "Prefab commit could not reload its persistent Prefab asset.", out diagnostic);
                commit = new HumanoidPrefabCommit(assetPath, persistentPrefab);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PublishPrefabSaveFailed", "Prefab commit could not save the Pure Humanoid candidate.", detail: exception.Message);
                return false;
            }
        }

        internal static bool TryVerifyReload(string prefabAssetPath, HumanoidIndividualAssetStage stage, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(prefabAssetPath) || stage == null || stage.Mesh == null)
                return Reject("PublishPrefabReloadInvalid", "Prefab reload verification requires a Prefab path and staged assets.", out diagnostic);

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                if (contents == null) return Reject("PublishPrefabReloadFailed", "Prefab commit could not load the saved Prefab contents.", out diagnostic);
                if (!contents.activeSelf) return Reject("PublishPrefabInactive", "Reloaded Pure Humanoid Prefab root must be active.", out diagnostic);
                SkinnedMeshRenderer[] renderers = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length != 1) return Reject("PublishPrefabRendererCountInvalid", "Reloaded Pure Humanoid Prefab requires exactly one SkinnedMeshRenderer.", out diagnostic, renderers.Length.ToString());
                if (renderers[0].sharedMesh != stage.Mesh) return Reject("PublishPrefabMeshReferenceInvalid", "Reloaded Prefab did not retain the staged Mesh reference.", out diagnostic);
                if (renderers[0].sharedMaterials.Length != stage.Materials.Count) return Reject("PublishPrefabMaterialReferenceInvalid", "Reloaded Prefab did not retain every staged Material reference.", out diagnostic);
                for (int i = 0; i < stage.Materials.Count; i++)
                    if (renderers[0].sharedMaterials[i] != stage.Materials[i]) return Reject("PublishPrefabMaterialReferenceInvalid", "Reloaded Prefab did not retain a staged Material reference.", out diagnostic, i.ToString());
                if (renderers[0].bones == null || renderers[0].bones.Length != stage.Mesh.bindposeCount)
                    return Reject("PublishPrefabSkinningReferenceInvalid", "Reloaded Prefab did not retain every final Mesh bone reference.", out diagnostic);
                for (int i = 0; i < renderers[0].bones.Length; i++)
                    if (renderers[0].bones[i] == null) return Reject("PublishPrefabSkinningReferenceInvalid", "Reloaded Prefab has a missing final Mesh bone reference.", out diagnostic, i.ToString());
                if (renderers[0].rootBone == null) return Reject("PublishPrefabRootBoneReferenceInvalid", "Reloaded Prefab did not retain its final renderer rootBone.", out diagnostic);
                if (stage.Avatar != null)
                {
                    Animator[] animators = contents.GetComponentsInChildren<Animator>(true);
                    if (animators.Length != 1 || animators[0].avatar != stage.Avatar)
                        return Reject("PublishPrefabAvatarReferenceInvalid", "Reloaded Prefab did not retain the staged Avatar reference.", out diagnostic);
                }
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PublishPrefabReloadFailed", "Prefab commit could not verify reloaded Prefab contents.", detail: exception.Message);
                return false;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, detail: detail);
            return false;
        }

        private static void SetSaveable(Transform transform)
        {
            transform.gameObject.hideFlags = HideFlags.None;
            for (int i = 0; i < transform.childCount; i++) SetSaveable(transform.GetChild(i));
        }
    }
}
