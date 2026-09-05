// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Validated, source-only facts accepted by the Figure import admission boundary.</summary>
    public sealed class ShapeSyncFigureImportAdmission
    {
        private readonly SkinnedMeshRenderer[] sourceRenderers;

        internal ShapeSyncFigureImportAdmission(GameObject candidate, GameObject humanoidRoot, Animator animator, Avatar avatar, SkinnedMeshRenderer[] sourceRenderers)
        {
            Candidate = candidate;
            HumanoidRoot = humanoidRoot;
            Animator = animator;
            Avatar = avatar;
            this.sourceRenderers = (SkinnedMeshRenderer[])sourceRenderers.Clone();
        }

        /// <summary>Gets the originally selected source object.</summary>
        public GameObject Candidate { get; }
        /// <summary>Gets the canonical root that later steps must clone for this admitted Humanoid.</summary>
        public GameObject HumanoidRoot { get; }
        /// <summary>Gets the source Humanoid Animator resolved during admission.</summary>
        public Animator Animator { get; }
        /// <summary>Gets the valid source Avatar resolved during admission.</summary>
        public Avatar Avatar { get; }
        /// <summary>Gets an immutable snapshot of the source renderer order observed during admission.</summary>
        public IReadOnlyList<SkinnedMeshRenderer> SourceRenderers => Array.AsReadOnly(sourceRenderers);
    }

    /// <summary>Admission boundary for a persistent Humanoid source; it never clones, merges, or edits assets.</summary>
    public static class ShapeSyncFigureImport
    {
        internal static bool TryRenameBaseFigure(string databaseAssetPath, string currentName, string replacementName, out string diagnostic)
        {
            return ShapeSyncDatabaseTransaction.TryEditStructure(databaseAssetPath, (database, _) =>
            {
                if (database.Registry == null) throw new InvalidOperationException("ShapeSync Database registry is unavailable.");
                if (!database.Registry.TryRenameBaseFigure(database, currentName, replacementName, out string renameDiagnostic))
                    throw new InvalidOperationException(renameDiagnostic);
            }, out diagnostic);
        }

        /// <summary>Validates a persistent source subtree and resolves its nearest parent Humanoid Animator.</summary>
        public static bool TryAdmit(GameObject candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            admission = null;
            diagnostic = null;

            if (candidate == null)
            {
                diagnostic = "ShapeSync Figure import requires a source GameObject.";
                return false;
            }

            if (!EditorUtility.IsPersistent(candidate))
            {
                diagnostic = "ShapeSync Figure import requires a persistent source asset GameObject.";
                return false;
            }

            Animator animator = TryResolveAnimator(candidate.transform);
            if (animator == null)
            {
                diagnostic = "ShapeSync Figure import requires an Animator on the candidate or a parent.";
                return false;
            }

            Avatar avatar = animator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                diagnostic = "ShapeSync Figure import requires a valid Humanoid Avatar on the resolved Animator.";
                return false;
            }

            if (!EditorUtility.IsPersistent(avatar))
            {
                diagnostic = "ShapeSync Figure import requires a persistent source Avatar.";
                return false;
            }

            GameObject humanoidRoot = animator.gameObject;
            SkinnedMeshRenderer[] sourceRenderers = humanoidRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sourceRenderers == null || sourceRenderers.Length == 0)
            {
                diagnostic = "ShapeSync Figure import requires at least one SkinnedMeshRenderer below the candidate.";
                return false;
            }

            admission = new ShapeSyncFigureImportAdmission(candidate, humanoidRoot, animator, avatar, sourceRenderers);
            return true;
        }

        /// <summary>
        /// Admits an FBM/PBM intermediate payload.  An Animator is optional for PBM
        /// geometry, but when supplied its valid Humanoid Avatar is preserved as
        /// Database-owned Figure data rather than stripped from the merged Prefab.
        /// </summary>
        internal static bool TryAdmitAxisSource(GameObject candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            admission = null;
            diagnostic = null;
            if (candidate == null || !EditorUtility.IsPersistent(candidate))
            {
                diagnostic = "Figure-axis import requires a persistent source GameObject.";
                return false;
            }
            Animator animator = TryResolveAnimator(candidate.transform);
            Avatar avatar = null;
            GameObject humanoidRoot = candidate;
            if (animator != null)
            {
                avatar = animator.avatar;
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                {
                    diagnostic = "Figure-axis import requires a valid Humanoid Avatar when its source has an Animator.";
                    return false;
                }
                if (!EditorUtility.IsPersistent(avatar))
                {
                    diagnostic = "Figure-axis import requires a persistent source Avatar when its source has an Animator.";
                    return false;
                }
                humanoidRoot = animator.gameObject;
            }

            SkinnedMeshRenderer[] renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                diagnostic = "Figure-axis import requires at least one SkinnedMeshRenderer below the source Prefab.";
                return false;
            }
            admission = new ShapeSyncFigureImportAdmission(candidate, humanoidRoot, animator, avatar, renderers);
            return true;
        }

        /// <summary>
        /// Admits an already Database-owned Figure for an axis replacement fallback.
        /// It reuses the Database-owned merged renderer and leaves any already-local
        /// Animator/Avatar reference intact; source provenance is not retained.
        /// </summary>
        internal static bool TryAdmitStoredDatabaseFigure(GameObject databaseFigure, out ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            admission = null;
            diagnostic = null;
            if (databaseFigure == null || !EditorUtility.IsPersistent(databaseFigure))
            {
                diagnostic = "PBM replacement fallback requires a persistent Database Figure.";
                return false;
            }

            ShapeSyncFigureImportRecord record = databaseFigure.GetComponent<ShapeSyncFigureImportRecord>();
            if (record == null)
            {
                diagnostic = "PBM replacement fallback requires the existing Database Figure import record.";
                return false;
            }

            SkinnedMeshRenderer[] renderers = databaseFigure.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                diagnostic = "PBM replacement fallback requires a merged renderer on the existing Database Figure.";
                return false;
            }

            admission = new ShapeSyncFigureImportAdmission(databaseFigure, databaseFigure, null, null, renderers);
            return true;
        }

        /// <summary>
        /// Imports one admitted Humanoid as a merged, recorded child of the Database Intermediate container.
        /// The Database transaction remains the sole structural write path; the source asset is never modified.
        /// </summary>
        public static bool TryImport(string databaseAssetPath, ShapeSyncFigureImportAdmission admission, out string diagnostic)
        {
            return TryImport(databaseAssetPath, admission, admission?.HumanoidRoot != null ? admission.HumanoidRoot.name : null, out diagnostic);
        }

        /// <summary>Imports an admitted Humanoid using the requested Database-internal Prefab name.</summary>
        public static bool TryImport(string databaseAssetPath, ShapeSyncFigureImportAdmission admission, string figureName, out string diagnostic)
        {
            diagnostic = null;
            if (admission == null)
            {
                diagnostic = "ShapeSync Figure import requires a successful admission.";
                return false;
            }
            if (!ShapeSyncDatabaseRegistry.IsValidUserName(figureName))
            {
                diagnostic = "ShapeSync Figure import requires a Figure Name without whitespace.";
                return false;
            }

            // Validate the destination before allocating a merge clone or Mesh.
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out _, out diagnostic)) return false;

            ShapeSyncFigureMeshMerger.Result mergeResult = null;
            DatabaseMaterialCopies materialCopies = null;
            try
            {
                if (!ShapeSyncFigureMeshMerger.TryMergeOwned(admission.HumanoidRoot, admission.SourceRenderers, out mergeResult, out diagnostic)) return false;
                if (!DatabaseMaterialCopies.TryCreate(figureName, mergeResult.Renderer.sharedMaterials, out materialCopies, out diagnostic)) return false;

                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
                {
                    AttachMergedFigure(database, intermediate, transaction, admission, mergeResult, materialCopies, figureName);

                    string registryDiagnostic = null;
                    if (database.Registry == null || !database.Registry.TryRegisterBaseFigure(database, figureName, mergeResult.Root, out registryDiagnostic))
                    {
                        throw new InvalidOperationException(registryDiagnostic ?? "ShapeSync Database registry is unavailable.");
                    }

                    // Abstract Texture entities are created together with the Figure-owned
                    // copies.  Material Entry authoring later assigns these entities to its
                    // fixed renderer/slot entries; importing a Figure must not leave the
                    // Textures Detail empty in the interim.
                    ShapeSyncTextureResourceImport.RegisterFigureTextures(database, databaseAssetPath, materialCopies.Materials);

                }, out diagnostic)) return false;

                // The persistent sub-asset is now owned by the Database Prefab, not Result.Dispose().
                mergeResult.DetachMesh();
                materialCopies.Detach();
                return true;
            }
            finally
            {
                materialCopies?.Dispose();
                mergeResult?.Dispose();
            }
        }

        /// <summary>Stages the shared 20.3 Figure payload used by Base, FBM, and PBM import transactions.</summary>
        internal static void AttachMergedFigure(ShapeSyncDatabase database, Transform intermediate, ShapeSyncDatabaseTransaction.EditContext transaction,
            ShapeSyncFigureImportAdmission admission, ShapeSyncFigureMeshMerger.Result mergeResult, DatabaseMaterialCopies materialCopies, string figureName)
        {
            if (database == null || intermediate == null || transaction == null || admission == null || mergeResult == null || string.IsNullOrWhiteSpace(figureName))
            {
                throw new InvalidOperationException("ShapeSync Figure staging requires complete admitted Figure inputs.");
            }
            if (intermediate.Find(figureName) != null) throw new InvalidOperationException("ShapeSync Figure import name already exists in Database: " + figureName);
            Mesh mergedMesh = mergeResult.Renderer.sharedMesh;
            if (mergedMesh == null) throw new InvalidOperationException("ShapeSync Figure merge did not produce a Mesh.");
            mergedMesh.name = figureName + "_MergedSkinnedMesh";
            // PBM figures are geometry-only intermediate data. Their renderer has
            // already been rebound to saved Figure Materials; they introduce no new
            // Material/Texture copies into the Database. Base Figures and FBMs provide
            // owned copies here.
            if (materialCopies != null)
            {
                materialCopies.AddTo(transaction);
                mergeResult.Renderer.sharedMaterials = materialCopies.Materials;
            }
            transaction.AddSubAsset(mergedMesh);
            mergeResult.Root.name = figureName;
            if (admission.Animator != null)
            {
                ConfigureDatabaseLocalHumanoidAnimator(admission, mergeResult.Root, transaction, figureName);
            }
            mergeResult.Root.transform.SetParent(intermediate, false);
            // A replacement fallback may merge an existing Database Figure. Its clone
            // already carries this authoring component, whereas a source Prefab does not.
            // Reconfigure that copied carrier; never attempt a duplicate component.
            ShapeSyncFigureImportRecord record = mergeResult.Root.GetComponent<ShapeSyncFigureImportRecord>();
            if (record == null) record = mergeResult.Root.AddComponent<ShapeSyncFigureImportRecord>();
            if (record == null) throw new InvalidOperationException("ShapeSync Figure import could not create its import record.");
            if (!record.TryConfigure(new[] { mergeResult.Renderer }, out string recordDiagnostic))
            {
                throw new InvalidOperationException(recordDiagnostic);
            }
        }

        /// <summary>Rebinds every admitted Figure Animator Avatar to Database-owned clones.</summary>
        /// <remarks>Imported Figure hierarchies retain their Animator components. Every non-null
        /// Avatar reference is copied into the Database, preserving shared Avatar references
        /// inside one Figure without retaining a source-asset dependency.</remarks>
        private static void ConfigureDatabaseLocalHumanoidAnimator(
            ShapeSyncFigureImportAdmission admission,
            GameObject mergedRoot,
            ShapeSyncDatabaseTransaction.EditContext transaction,
            string figureName)
        {
            if (admission.HumanoidRoot == null || admission.Animator == null || admission.Avatar == null)
            {
                throw new InvalidOperationException("ShapeSync Figure import requires an admitted Humanoid Animator and Avatar.");
            }

            Animator clonedAdmissionAnimator = null;
            var localAvatars = new Dictionary<Avatar, Avatar>();
            foreach (Animator sourceAnimator in admission.HumanoidRoot.GetComponentsInChildren<Animator>(true))
            {
                string animatorPath = BonePoseUtility.GetRelativePath(admission.HumanoidRoot.transform, sourceAnimator.transform);
                Transform clonedAnimatorTransform = string.IsNullOrEmpty(animatorPath)
                    ? mergedRoot.transform
                    : mergedRoot.transform.Find(animatorPath);
                Animator clonedAnimator = clonedAnimatorTransform != null ? clonedAnimatorTransform.GetComponent<Animator>() : null;
                if (clonedAnimator == null)
                {
                    throw new InvalidOperationException("ShapeSync Figure import could not resolve a cloned Animator.");
                }

                if (sourceAnimator == admission.Animator) clonedAdmissionAnimator = clonedAnimator;
                Avatar sourceAvatar = sourceAnimator.avatar;
                if (sourceAvatar == null) continue;
                if (!localAvatars.TryGetValue(sourceAvatar, out Avatar localAvatar))
                {
                    localAvatar = UnityEngine.Object.Instantiate(sourceAvatar);
                    if (localAvatar == null)
                    {
                        throw new InvalidOperationException("ShapeSync Figure import could not clone an admitted Animator Avatar.");
                    }

                    localAvatar.name = sourceAvatar == admission.Avatar
                        ? figureName + "_Avatar"
                        : figureName + "_AnimatorAvatar_" + localAvatars.Count;
                    transaction.AddSubAsset(localAvatar);
                    localAvatars.Add(sourceAvatar, localAvatar);
                }
                clonedAnimator.avatar = localAvatar;
            }

            if (clonedAdmissionAnimator == null)
            {
                throw new InvalidOperationException("ShapeSync Figure import could not resolve the cloned Humanoid Animator.");
            }

            if (!clonedAdmissionAnimator.isHuman || clonedAdmissionAnimator.avatar == null || !clonedAdmissionAnimator.avatar.isValid)
            {
                throw new InvalidOperationException("ShapeSync Figure import could not create a valid Database-local Humanoid Animator.");
            }
        }

        /// <summary>Owns cloned renderer Materials and every Texture directly referenced by their shader properties until commit.</summary>
        internal sealed class DatabaseMaterialCopies : IDisposable
        {
            private readonly List<Material> materials;
            private readonly List<Texture> textures;
            private bool detached;

            private DatabaseMaterialCopies(List<Material> materials, List<Texture> textures)
            {
                this.materials = materials;
                this.textures = textures;
            }

            public Material[] Materials => materials.ToArray();

            public static bool TryCreate(string figureName, Material[] sourceMaterials, out DatabaseMaterialCopies copies, out string diagnostic)
            {
                var requiredSlots = new HashSet<int>();
                if (sourceMaterials != null)
                    for (int index = 0; index < sourceMaterials.Length; index++) requiredSlots.Add(index);
                return TryCreateForRequiredSlots(figureName, sourceMaterials, requiredSlots, out copies, out diagnostic);
            }

            /// <summary>
            /// Clones only the material slots required by the caller's contract while
            /// preserving the source array topology.  Unrequired slots are deliberately
            /// emitted as null and therefore do not pull excluded Material/Texture assets
            /// into the Database.
            /// </summary>
            internal static bool TryCreateForRequiredSlots(string figureName, Material[] sourceMaterials,
                IReadOnlyCollection<int> requiredSlots, out DatabaseMaterialCopies copies, out string diagnostic)
            {
                copies = null;
                diagnostic = null;
                if (string.IsNullOrWhiteSpace(figureName) || sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    diagnostic = "ShapeSync Figure import requires a Figure Name and at least one source Material.";
                    return false;
                }
                if (requiredSlots == null)
                {
                    diagnostic = "ShapeSync Figure import requires a material slot contract.";
                    return false;
                }
                foreach (int requiredSlot in requiredSlots)
                    if (requiredSlot < 0 || requiredSlot >= sourceMaterials.Length)
                    {
                        diagnostic = "ShapeSync Figure import material slot contract is out of range.";
                        return false;
                    }
                HashSet<int> requiredSlotSet = requiredSlots as HashSet<int> ?? new HashSet<int>(requiredSlots);

                var materials = new List<Material>(sourceMaterials.Length);
                var textures = new List<Texture>();
                var textureCopies = new Dictionary<Texture, Texture>();
                // The merged Mesh is named by the importer before it is staged. Reserve that
                // name here so a source Material or Texture named "MergedSkinnedMesh" cannot
                // collide with it in the Database Prefab's sub-asset namespace.
                var usedNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    figureName + "_MergedSkinnedMesh"
                };
                try
                {
                    for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                    {
                        if (!requiredSlotSet.Contains(materialIndex))
                        {
                            materials.Add(null);
                            continue;
                        }
                        Material source = sourceMaterials[materialIndex];
                        if (source == null) throw new InvalidOperationException("ShapeSync Figure import requires a Material for every merged submesh.");
                        Material copy = new Material(source) { name = MakeDatabaseSubAssetName(figureName, source.name, usedNames) };
                        materials.Add(copy);
                        foreach (string propertyName in copy.GetTexturePropertyNames())
                        {
                            Texture sourceTexture = copy.GetTexture(propertyName);
                            if (sourceTexture == null) continue;
                            if (!textureCopies.TryGetValue(sourceTexture, out Texture textureCopy))
                            {
                                textureCopy = UnityEngine.Object.Instantiate(sourceTexture);
                                textureCopy.name = ShapeSyncEditorTextureUtility.IsLegacyNeutralNormalPlaceholder(sourceTexture)
                                    ? ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName
                                    : MakeDatabaseSubAssetName(figureName, sourceTexture.name, usedNames);
                                textureCopies.Add(sourceTexture, textureCopy);
                                textures.Add(textureCopy);
                            }
                            copy.SetTexture(propertyName, textureCopy);
                        }
                    }
                    copies = new DatabaseMaterialCopies(materials, textures);
                    return true;
                }
                catch (Exception exception)
                {
                    foreach (Material material in materials)
                        if (material != null) UnityEngine.Object.DestroyImmediate(material);
                    foreach (Texture texture in textures) UnityEngine.Object.DestroyImmediate(texture);
                    diagnostic = "ShapeSync Figure import could not clone source Material and Texture dependencies: " + exception.Message;
                    return false;
                }
            }

            public void AddTo(ShapeSyncDatabaseTransaction.EditContext transaction)
            {
                foreach (Texture texture in textures) transaction.AddSubAsset(texture);
                foreach (Material material in materials)
                    if (material != null) transaction.AddSubAsset(material);
            }

            public void Detach() { detached = true; }

            private static string MakeDatabaseSubAssetName(string figureName, string sourceName, HashSet<string> usedNames)
            {
                string baseName = figureName + "_" + (string.IsNullOrWhiteSpace(sourceName) ? "Unnamed" : sourceName);
                string candidate = baseName;
                int suffix = 2;
                while (!usedNames.Add(candidate)) candidate = baseName + "_" + suffix++;
                return candidate;
            }

            public void Dispose()
            {
                if (detached) return;
                foreach (Material material in materials) if (material != null && !AssetDatabase.Contains(material)) UnityEngine.Object.DestroyImmediate(material);
                foreach (Texture texture in textures) if (texture != null && !AssetDatabase.Contains(texture)) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Animator TryResolveAnimator(Transform candidate)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                Animator animator = current.GetComponent<Animator>();
                if (animator != null) return animator;
            }
            return null;
        }
    }
}
