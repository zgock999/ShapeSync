// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Builds the transient Base/FBM Figure mesh schema from one validated Generate snapshot.</summary>
    internal static class ShapeSyncFigureGenerateMeshBuilder
    {
        internal sealed class Result : IDisposable
        {
            internal Result(GameObject figure, Mesh mesh, Avatar avatar, IReadOnlyList<Avatar> fbmAvatars, CharacterBoneRegistry baseRegistry, IReadOnlyList<CharacterBoneRegistry> fbmRegistries, IReadOnlyList<DynamicBoneBlendTarget> fbmTargets, int pcmSlots, int pcmFirstSlot)
            { Figure = figure; Mesh = mesh; Avatar = avatar; FbmAvatars = fbmAvatars; BaseRegistry = baseRegistry; FbmRegistries = fbmRegistries; this.fbmTargets.AddRange(fbmTargets); PcmSlots = pcmSlots; PcmFirstSlot = pcmFirstSlot; }
            internal GameObject Figure { get; }
            internal Mesh Mesh { get; private set; }
            internal Avatar Avatar { get; }
            internal IReadOnlyList<Avatar> FbmAvatars { get; }
            internal CharacterBoneRegistry BaseRegistry { get; }
            internal IReadOnlyList<CharacterBoneRegistry> FbmRegistries { get; }
            internal IReadOnlyList<DynamicBoneBlendTarget> FbmTargets => fbmTargets;
            internal IReadOnlyList<DynamicBoneBlendTarget> PbmTargets => pbmTargets;
            internal IEnumerable<DynamicBoneBlendTarget> RuntimeTargets => fbmTargets.Concat(pbmTargets);
            internal int PcmSlots { get; }
            internal int PcmFirstSlot { get; }
            internal void OwnGeneratedAsset(UnityEngine.Object asset) { if (asset != null) generatedAssets.Add(asset); }
            internal Mesh SwapMesh(Mesh replacement)
            {
                if (replacement == null) throw new ArgumentNullException(nameof(replacement));
                SkinnedMeshRenderer renderer = Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                Mesh previous = Mesh;
                renderer.sharedMesh = replacement;
                Mesh = replacement;
                return previous;
            }

            internal IReadOnlyList<UnityEngine.Object> SwapPbmAssets(IReadOnlyList<UnityEngine.Object> replacement)
            {
                UnityEngine.Object[] previous = pbmAssets.ToArray();
                pbmAssets.Clear();
                if (replacement == null) return previous;
                foreach (UnityEngine.Object asset in replacement) if (asset != null) pbmAssets.Add(asset);
                return previous;
            }
            internal IReadOnlyList<DynamicBoneBlendTarget> SwapPbmTargets(IReadOnlyList<DynamicBoneBlendTarget> replacement)
            {
                DynamicBoneBlendTarget[] previous = pbmTargets.ToArray();
                pbmTargets.Clear();
                if (replacement != null) pbmTargets.AddRange(replacement.Where(target => target != null));
                return previous;
            }
            private readonly List<UnityEngine.Object> pbmAssets = new List<UnityEngine.Object>();
            private readonly List<DynamicBoneBlendTarget> fbmTargets = new List<DynamicBoneBlendTarget>();
            private readonly List<DynamicBoneBlendTarget> pbmTargets = new List<DynamicBoneBlendTarget>();
            private readonly List<UnityEngine.Object> generatedAssets = new List<UnityEngine.Object>();
            public void Dispose()
            {
                if (Figure != null) UnityEngine.Object.DestroyImmediate(Figure);
                if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
                if (Avatar != null) UnityEngine.Object.DestroyImmediate(Avatar);
                foreach (Avatar avatar in FbmAvatars) if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                if (BaseRegistry != null) UnityEngine.Object.DestroyImmediate(BaseRegistry);
                foreach (CharacterBoneRegistry registry in FbmRegistries) if (registry != null) UnityEngine.Object.DestroyImmediate(registry);
                foreach (UnityEngine.Object asset in pbmAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                foreach (UnityEngine.Object asset in generatedAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        internal static bool TryBuild(ShapeSyncFigureGenerateSnapshot snapshot, out Result result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (snapshot == null || snapshot.BaseFigure == null || snapshot.BaseFigure.GameObject == null)
                return Fail("GenerateSnapshotRequired", "Figure mesh generation requires a validated Generate snapshot.", out diagnostic);
            GameObject output = null; Mesh mesh = null; Avatar avatar = null;
            var fbmRegistries = new List<CharacterBoneRegistry>(); var fbmAvatars = new List<Avatar>();
            try
            {
                output = UnityEngine.Object.Instantiate(snapshot.BaseFigure.GameObject);
                output.name = snapshot.BaseFigure.Name;
                RemoveInputRuntimeGraph(output);
                SkinnedMeshRenderer outputRenderer = output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                Mesh sourceMesh = outputRenderer.sharedMesh;
                mesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
                mesh.name = snapshot.BaseFigure.Name + "_Mesh";
                mesh.ClearBlendShapes();
                var fbmAxes = snapshot.Axes.Where(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).OrderBy(axis => axis.Name, StringComparer.Ordinal).ToArray();
                foreach (ShapeSyncFigureGenerateSnapshot.Axis axis in fbmAxes)
                {
                    SkinnedMeshRenderer targetRenderer = axis.Figures.Single().Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                    if (!BlendShapeBakeUtility.TryBuildMeshDifference(sourceMesh, targetRenderer.sharedMesh, out Vector3[] vertices, out Vector3[] normals, out Vector3[] tangents))
                        throw new InvalidOperationException("FBM topology does not match Base: " + axis.Name);
                    BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(mesh, axis.Name, vertices, normals, tangents);
                    CharacterBoneRegistry registry = BonePoseUtility.ExtractFromSkinnedMeshRenderers(axis.Figures.Single().Figure.transform, new[] { targetRenderer });
                    registry.fbmBlendName = axis.Name;
                    fbmRegistries.Add(registry);
                    Avatar targetAvatar = AvatarBuilder.BuildHumanAvatar(axis.Figures.Single().Figure, snapshot.BaseAvatar.humanDescription);
                    if (targetAvatar == null || !targetAvatar.isHuman || !targetAvatar.isValid)
                        throw new InvalidOperationException("Could not build FBM Humanoid Avatar: " + axis.Name);
                    targetAvatar.name = snapshot.BaseFigure.Name + "_" + axis.Name + "_Avatar";
                    fbmAvatars.Add(targetAvatar);
                }
                foreach (string rawName in snapshot.KeptRawBlendShapeNames)
                    AddRawAndMcmFrames(mesh, sourceMesh, fbmAxes, rawName);
                ShapeSyncLegacyBuilderContracts.AddReservedPcmSlots(mesh, sourceMesh.vertexCount, snapshot.PcmSlots, fbmAxes.Length);
                outputRenderer.sharedMesh = mesh;
                Animator animator = output.GetComponentsInChildren<Animator>(true).Single();
                avatar = UnityEngine.Object.Instantiate(snapshot.BaseAvatar);
                avatar.name = snapshot.BaseFigure.Name + "_Avatar";
                animator.avatar = avatar;
                CharacterBoneRegistry baseRegistry = BonePoseUtility.ExtractFromAnimator(animator);
                int pcmFirstSlot = mesh.blendShapeCount - snapshot.PcmSlots * (fbmAxes.Length + 1);
                var fbmTargets = fbmAxes.Select((axis, index) => new DynamicBoneBlendTarget { blendName = axis.Name, enabled = true, weight = 0f, targetAvatar = fbmAvatars[index], targetRegistry = fbmRegistries[index] }).ToArray();
                RemoveSourceOnlyComponents(output);
                result = new Result(output, mesh, avatar, fbmAvatars.AsReadOnly(), baseRegistry, fbmRegistries.AsReadOnly(), fbmTargets, snapshot.PcmSlots, pcmFirstSlot);
                return true;
            }
            catch (Exception exception)
            {
                if (output != null) UnityEngine.Object.DestroyImmediate(output);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                foreach (Avatar value in fbmAvatars) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                foreach (CharacterBoneRegistry value in fbmRegistries) if (value != null) UnityEngine.Object.DestroyImmediate(value);
                return Fail("FigureMeshBuildInvalid", exception.Message, out diagnostic);
            }
        }

        /// <summary>Applies the already-complete transient escrow to the final runtime graph.</summary>
        internal static void ConfigureRuntimeGraph(Result result)
        {
            GameObject output = result.Figure;
            SkinnedMeshRenderer renderer = output.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Animator animator = output.GetComponentsInChildren<Animator>(true).Single();
            DynamicMorphAdapter adapter = output.AddComponent<DynamicMorphAdapter>();
            adapter.ConfigureForFigure(renderer, result.PcmSlots, result.PcmFirstSlot, result.FbmTargets.Select(target => target.blendName).ToArray());
            DynamicBoneBlender blender = output.AddComponent<DynamicBoneBlender>();
            blender.ConfigureForFigure(renderer, animator, result.Avatar, result.BaseRegistry, result.RuntimeTargets.ToList());
            UniversalExpressionProxy expressions = output.AddComponent<UniversalExpressionProxy>();
            expressions.ConfigureForFigure(renderer, blender); expressions.ClearExpressionList();
            FigureMorphSyncCoordinator coordinator = output.AddComponent<FigureMorphSyncCoordinator>(); coordinator.ConfigureForFigure(blender, expressions);
            OutfitAttacher attacher = output.AddComponent<OutfitAttacher>(); attacher.ConfigureForFigure(blender, animator);
            BuilderRuntimeComponentSetup.Ensure(output, blender);
        }

        private static void RemoveInputRuntimeGraph(GameObject output)
        {
            foreach (Component component in output.GetComponentsInChildren<Component>(true).Reverse())
            {
                if (component is ShapeDirector || component is ShapeSerializer || component is ShapeDeserializer ||
                    component is DynamicMorphAdapter || component is DynamicBoneBlender ||
                    component is UniversalExpressionProxy || component is FigureMorphSyncCoordinator || component is OutfitAttacher ||
                    component is MaterialProxy || component is MaterialAttacher || component is MaterialStackMachine ||
                    component is MeshStackMachine || component is NormalBlender)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }
        }

        private static void RemoveSourceOnlyComponents(GameObject output)
        {
            foreach (Component component in output.GetComponentsInChildren<Component>(true).Reverse())
                if (component != null && (component.GetType().FullName == "UniVRM10.Vrm10Instance" || component.GetType().FullName == "UniHumanoid.Humanoid" || component is ShapeSyncFigureImportRecord || component is ShapeSyncDatabase)) UnityEngine.Object.DestroyImmediate(component);
        }

        private static void AddRawAndMcmFrames(Mesh output, Mesh source, IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Axis> fbmAxes, string rawName)
        {
            int sourceIndex = source.GetBlendShapeIndex(rawName);
            if (sourceIndex < 0 || !BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(source, sourceIndex, 100f, out Vector3[] sourceVertices, out Vector3[] sourceNormals, out Vector3[] sourceTangents))
                throw new InvalidOperationException("Base Mesh is missing readable Extra Morph: " + rawName);
            BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(output, rawName, sourceVertices, sourceNormals, sourceTangents);
            foreach (ShapeSyncFigureGenerateSnapshot.Axis axis in fbmAxes)
            {
                Mesh target = axis.Figures.Single().Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single().sharedMesh;
                int targetIndex = target.GetBlendShapeIndex(rawName);
                if (targetIndex < 0 || !BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(target, targetIndex, 100f, out Vector3[] targetVertices, out Vector3[] targetNormals, out Vector3[] targetTangents))
                    throw new InvalidOperationException("FBM Mesh is missing readable Extra Morph: " + axis.Name + "/" + rawName);
                BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(output, BlendShapeReservedPrefixes.Mcm + axis.Name + "_" + rawName,
                    BlendShapeBakeUtility.Subtract(targetVertices, sourceVertices), BlendShapeBakeUtility.Subtract(targetNormals, sourceNormals), BlendShapeBakeUtility.Subtract(targetTangents, sourceTangents));
            }
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        { diagnostic = StackMachineDiagnostic.CreateDomain("figure-generate", code, message); return false; }
    }
}
#endif
