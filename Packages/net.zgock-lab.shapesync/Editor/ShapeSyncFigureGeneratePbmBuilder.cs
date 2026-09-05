// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Stages and atomically replaces Database PBM output on the transient Step 2 Figure mesh.</summary>
    internal static class ShapeSyncFigureGeneratePbmBuilder
    {
        // Editor test seam for transient escrow commit; it is private so Generate callers cannot alter transaction behavior.
        // Explicitly initialize the optional hook so the editor build does not report CS0649.
        private static Action beforeCommitForTests = null;

        internal static bool TryApply(ShapeSyncFigureGenerateSnapshot snapshot, ShapeSyncFigureGenerateMeshBuilder.Result figure, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (snapshot == null || figure == null || figure.Mesh == null || figure.Figure == null || figure.Avatar == null)
                return Fail("PbmGenerateInputInvalid", "PBM generation requires a Figure snapshot and transient Figure output.", out diagnostic);

            Mesh stagedMesh = null;
            var stagedAssets = new List<UnityEngine.Object>();
            Mesh previousMesh = null;
            IReadOnlyList<DynamicBoneBlendTarget> previousTargets = null;
            IReadOnlyList<UnityEngine.Object> previousAssets = null;
            bool committed = false;
            try
            {
                Mesh baseMesh = GetRenderer(snapshot.BaseFigure.GameObject).sharedMesh;
                string[] replacedNames = Enumerable.Range(0, figure.Mesh.blendShapeCount)
                    .Select(figure.Mesh.GetBlendShapeName)
                    .Where(name => name.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                    .ToArray();
                stagedMesh = ShapeSyncLegacyBuilderContracts.CreateMeshWithoutBlendShapes(figure.Mesh, replacedNames);
                var stagedTargets = new List<DynamicBoneBlendTarget>();
                foreach (ShapeSyncFigureGenerateSnapshot.Axis pbm in snapshot.Axes
                    .Where(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                    .OrderBy(axis => axis.Name, StringComparer.Ordinal))
                {
                    ShapeSyncFigureGenerateSnapshot.AxisFigure baseBinding = pbm.Figures.Single(binding => binding.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                    SkinnedMeshRenderer basePbmRenderer = GetRenderer(baseBinding.Figure);
                    if (!BlendShapeBakeUtility.TryBuildMeshDifference(baseMesh, basePbmRenderer.sharedMesh, out Vector3[] baseDelta, out Vector3[] baseNormals, out Vector3[] baseTangents))
                        throw new InvalidOperationException("PBM Base topology mismatch: " + pbm.Name);
                    BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(stagedMesh, BlendShapeReservedPrefixes.Pbm + pbm.Name, baseDelta, baseNormals, baseTangents);

                    Avatar pbmAvatar = BuildAvatar(baseBinding.Figure, figure.Avatar.humanDescription, pbm.Name);
                    CharacterBoneRegistry pbmRegistry = BonePoseUtility.ExtractFromSkinnedMeshRenderers(baseBinding.Figure.transform, new[] { basePbmRenderer });
                    stagedAssets.Add(pbmAvatar); stagedAssets.Add(pbmRegistry);
                    var pbmTarget = new DynamicBoneBlendTarget
                    {
                        blendName = BlendShapeReservedPrefixes.Pbm + pbm.Name,
                        enabled = true,
                        weight = 0f,
                        targetAvatar = pbmAvatar,
                        targetRegistry = pbmRegistry,
                        pbmDifferenceTargets = new List<DynamicBonePbmDifferenceTarget>()
                    };
                    foreach (ShapeSyncFigureGenerateSnapshot.Axis fbm in snapshot.Axes
                        .Where(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                        .OrderBy(axis => axis.Name, StringComparer.Ordinal))
                    {
                        SkinnedMeshRenderer fbmRenderer = GetRenderer(fbm.Figures.Single().Figure);
                        ShapeSyncFigureGenerateSnapshot.AxisFigure combinedBinding = pbm.Figures.Single(binding => binding.ShapeKey == fbm.Name);
                        SkinnedMeshRenderer combinedRenderer = GetRenderer(combinedBinding.Figure);
                        if (!BlendShapeBakeUtility.TryBuildMeshDifference(baseMesh, fbmRenderer.sharedMesh, out Vector3[] fbmDelta, out Vector3[] fbmNormals, out Vector3[] fbmTangents)
                            || !BlendShapeBakeUtility.TryBuildMeshDifference(baseMesh, combinedRenderer.sharedMesh, out Vector3[] combinedDelta, out Vector3[] combinedNormals, out Vector3[] combinedTangents))
                            throw new InvalidOperationException("PBM FBM topology mismatch: " + fbm.Name + "/" + pbm.Name);
                        BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(stagedMesh, BlendShapeReservedPrefixes.Pbm + fbm.Name + "_" + pbm.Name,
                            BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(combinedDelta, baseDelta), fbmDelta),
                            BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(combinedNormals, baseNormals), fbmNormals),
                            BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(combinedTangents, baseTangents), fbmTangents));
                        Avatar combinedAvatar = BuildAvatar(combinedBinding.Figure, figure.Avatar.humanDescription, fbm.Name + "_" + pbm.Name);
                        CharacterBoneRegistry combinedRegistry = BonePoseUtility.ExtractFromSkinnedMeshRenderers(combinedBinding.Figure.transform, new[] { combinedRenderer });
                        stagedAssets.Add(combinedAvatar); stagedAssets.Add(combinedRegistry);
                        pbmTarget.pbmDifferenceTargets.Add(new DynamicBonePbmDifferenceTarget
                        {
                            fbmBlendName = fbm.Name,
                            targetAvatar = combinedAvatar,
                            targetRegistry = combinedRegistry
                        });
                    }
                    stagedTargets.Add(pbmTarget);
                }

                previousMesh = figure.SwapMesh(stagedMesh);
                stagedMesh = null;
                previousTargets = figure.SwapPbmTargets(stagedTargets);
                previousAssets = figure.SwapPbmAssets(stagedAssets);
                stagedAssets.Clear();
                beforeCommitForTests?.Invoke();
                committed = true;
                if (previousMesh != null) UnityEngine.Object.DestroyImmediate(previousMesh);
                foreach (UnityEngine.Object asset in previousAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                return true;
            }
            catch (Exception exception)
            {
                Exception rollbackException = null;
                if (!committed && previousMesh != null)
                {
                    rollbackException = RestoreCommittedState(figure, previousMesh, previousTargets, previousAssets);
                }
                if (stagedMesh != null) UnityEngine.Object.DestroyImmediate(stagedMesh);
                foreach (UnityEngine.Object asset in stagedAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                string message = rollbackException == null
                    ? exception.Message
                    : exception.Message + " Rollback runtime cache reapply also failed: " + rollbackException.Message;
                return Fail("PbmGenerateInvalid", message, out diagnostic);
            }
        }

        private static Exception RestoreCommittedState(ShapeSyncFigureGenerateMeshBuilder.Result figure,
            Mesh previousMesh, IReadOnlyList<DynamicBoneBlendTarget> previousTargets, IReadOnlyList<UnityEngine.Object> previousAssets)
        {
            Exception failure = null;
            try
            {
                Mesh failedMesh = figure.SwapMesh(previousMesh);
                if (failedMesh != null) UnityEngine.Object.DestroyImmediate(failedMesh);
            }
            catch (Exception exception) { failure = exception; }
            try
            {
                if (previousTargets != null) figure.SwapPbmTargets(previousTargets);
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            try
            {
                if (previousAssets != null)
                {
                    IReadOnlyList<UnityEngine.Object> failedAssets = figure.SwapPbmAssets(previousAssets);
                    foreach (UnityEngine.Object asset in failedAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                }
            }
            catch (Exception exception) { if (failure == null) failure = exception; }
            return failure;
        }

        private static SkinnedMeshRenderer GetRenderer(GameObject figure)
        {
            if (figure == null) throw new InvalidOperationException("PBM Figure binding is missing.");
            return figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
        }

        private static Avatar BuildAvatar(GameObject figure, HumanDescription description, string targetName)
        {
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(figure, description);
            if (avatar == null || !avatar.isHuman || !avatar.isValid)
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                throw new InvalidOperationException("Could not build PBM Humanoid Avatar: " + targetName);
            }
            return avatar;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        { diagnostic = StackMachineDiagnostic.CreateDomain("figure-generate", code, message); return false; }
    }
}
#endif
