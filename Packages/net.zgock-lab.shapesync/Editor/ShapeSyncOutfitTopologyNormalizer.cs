// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Establishes a deterministic Base-to-FBM vertex correspondence for Outfit meshes.</summary>
    /// <remarks>
    /// This is an authoring-time operation.  It mutates only the Database-owned target mesh
    /// after all validation has completed; callers own the surrounding transaction and roll
    /// back the staged mesh when this method returns false.
    /// </remarks>
    internal static class ShapeSyncOutfitTopologyNormalizer
    {
        internal const double ConfidenceThreshold = 1.0;
        internal const double EquivalentCandidateDistance = 1.0e-4;
        private const int LocalFieldCount = 5;
        private const double InverseDistanceEpsilon = 1.0e-6;

        internal static bool TryNormalizeInPlace(Mesh baseMesh, Mesh targetMesh, string bindingName,
            string rendererPath, out int[] permutation, out StackMachineDiagnostic diagnostic)
        {
            return TryNormalizeInPlace(baseMesh, targetMesh, bindingName, rendererPath,
                out permutation, out _, out diagnostic);
        }

        internal static bool TryNormalizeInPlace(Mesh baseMesh, Mesh targetMesh, string bindingName,
            string rendererPath, out int[] permutation, out int[] boneMap, out StackMachineDiagnostic diagnostic)
        {
            return TryNormalizeCore(baseMesh, targetMesh, null, bindingName, rendererPath,
                null, null, null, out permutation, out boneMap, out diagnostic);
        }

        internal static bool TryNormalizeInPlace(SkinnedMeshRenderer baseRenderer, SkinnedMeshRenderer targetRenderer,
            string bindingName, string rendererPath, out int[] permutation, out StackMachineDiagnostic diagnostic)
        {
            return TryNormalizeInPlace(baseRenderer, targetRenderer, bindingName, rendererPath,
                out permutation, out _, out diagnostic);
        }

        /// <summary>
        /// Normalizes one FBM renderer and, when roots are supplied by import authoring,
        /// rebuilds the target Extra Bone hierarchy in the Base path/name space.
        /// Figure-owned bones remain untouched because they are owned by the canonical Figure.
        /// </summary>
        internal static bool TryNormalizeInPlace(SkinnedMeshRenderer baseRenderer, SkinnedMeshRenderer targetRenderer,
            string bindingName, string rendererPath, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out int[] permutation, out StackMachineDiagnostic diagnostic)
        {
            return TryNormalizeInPlace(baseRenderer, targetRenderer, bindingName, rendererPath,
                baseOutfitRoot, targetOutfitRoot, figureRoot, out permutation, out _, out diagnostic);
        }

        internal static bool TryNormalizeInPlace(SkinnedMeshRenderer baseRenderer, SkinnedMeshRenderer targetRenderer,
            string bindingName, string rendererPath, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out int[] permutation, out int[] boneMap, out StackMachineDiagnostic diagnostic)
        {
            permutation = null;
            boneMap = null;
            diagnostic = null;
            if (baseRenderer == null)
                return Fail("OutfitTopologyBaseRendererMissing", "Outfit topology normalization requires the Base renderer.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer == null)
                return Fail("OutfitTopologyTargetRendererMissing", "Outfit topology normalization requires the FBM target renderer.", bindingName, rendererPath, -1, out diagnostic);
            if (baseRenderer.sharedMesh == null)
                return Fail("OutfitTopologyBaseMeshMissing", "Outfit topology normalization requires the Base mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer.sharedMesh == null)
                return Fail("OutfitTopologyTargetMeshMissing", "Outfit topology normalization requires the FBM target mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (baseRenderer.bones == null || baseRenderer.bones.Length != baseRenderer.sharedMesh.bindposes.Length)
                return Fail("OutfitTopologyBaseBoneTableInvalid", "The Base renderer bone table does not match its mesh bindposes.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer.bones == null || targetRenderer.bones.Length != targetRenderer.sharedMesh.bindposes.Length)
                return Fail("OutfitTopologyTargetBoneTableInvalid", "The FBM target renderer bone table does not match its mesh bindposes.", bindingName, rendererPath, -1, out diagnostic);
            if (!TryValidateExtraBoneHierarchyInputs(baseRenderer, targetRenderer, baseOutfitRoot, targetOutfitRoot,
                figureRoot, out diagnostic, bindingName, rendererPath)) return false;
            if (!TryNormalizeCore(baseRenderer.sharedMesh, targetRenderer.sharedMesh, targetRenderer,
                bindingName, rendererPath, baseOutfitRoot, targetOutfitRoot, figureRoot,
                out permutation, out boneMap, out diagnostic)) return false;
            return TryNormalizeExtraBoneHierarchy(baseRenderer, targetRenderer, baseOutfitRoot, targetOutfitRoot,
                figureRoot, out diagnostic, bindingName, rendererPath);
        }

        internal static bool TryNormalizeInPlace(SkinnedMeshRenderer baseRenderer, SkinnedMeshRenderer targetRenderer,
            string bindingName, string rendererPath, out int[] permutation, out int[] boneMap,
            out StackMachineDiagnostic diagnostic)
        {
            permutation = null;
            boneMap = null;
            diagnostic = null;
            if (baseRenderer == null)
                return Fail("OutfitTopologyBaseRendererMissing", "Outfit topology normalization requires the Base renderer.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer == null)
                return Fail("OutfitTopologyTargetRendererMissing", "Outfit topology normalization requires the FBM target renderer.", bindingName, rendererPath, -1, out diagnostic);
            if (baseRenderer.sharedMesh == null)
                return Fail("OutfitTopologyBaseMeshMissing", "Outfit topology normalization requires the Base mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer.sharedMesh == null)
                return Fail("OutfitTopologyTargetMeshMissing", "Outfit topology normalization requires the FBM target mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (baseRenderer.bones == null || baseRenderer.bones.Length != baseRenderer.sharedMesh.bindposes.Length)
                return Fail("OutfitTopologyBaseBoneTableInvalid", "The Base renderer bone table does not match its mesh bindposes.", bindingName, rendererPath, -1, out diagnostic);
            if (targetRenderer.bones == null || targetRenderer.bones.Length != targetRenderer.sharedMesh.bindposes.Length)
                return Fail("OutfitTopologyTargetBoneTableInvalid", "The FBM target renderer bone table does not match its mesh bindposes.", bindingName, rendererPath, -1, out diagnostic);
            return TryNormalizeCore(baseRenderer.sharedMesh, targetRenderer.sharedMesh, targetRenderer,
                bindingName, rendererPath, null, null, null, out permutation, out boneMap, out diagnostic);
        }

        private static bool TryNormalizeCore(Mesh baseMesh, Mesh targetMesh, SkinnedMeshRenderer targetRenderer,
            string bindingName, string rendererPath, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out int[] permutation, out int[] boneMap,
            out StackMachineDiagnostic diagnostic)
        {
            permutation = null;
            boneMap = null;
            diagnostic = null;
            if (baseMesh == null) return Fail("OutfitTopologyBaseMeshMissing", "Outfit topology normalization requires the Base mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (targetMesh == null) return Fail("OutfitTopologyTargetMeshMissing", "Outfit topology normalization requires the FBM target mesh.", bindingName, rendererPath, -1, out diagnostic);
            if (baseMesh.vertexCount == targetMesh.vertexCount && baseMesh.subMeshCount == targetMesh.subMeshCount
                && HaveIdenticalIndexArrays(baseMesh, targetMesh))
            {
                permutation = Enumerable.Range(0, baseMesh.vertexCount).ToArray();
                return TryApplyBoneNormalization(targetMesh, baseMesh, permutation, targetRenderer,
                    baseOutfitRoot, targetOutfitRoot, figureRoot,
                    out boneMap, out diagnostic, bindingName, rendererPath);
            }
            if (baseMesh.vertexCount != targetMesh.vertexCount)
                return Fail("OutfitTopologyVertexCountMismatch",
                    "Base and FBM Outfit meshes have different vertex counts (expected " + baseMesh.vertexCount + ", actual " + targetMesh.vertexCount + ").",
                    bindingName, rendererPath, -1, out diagnostic);
            if (baseMesh.subMeshCount != targetMesh.subMeshCount)
                return Fail("OutfitTopologySubMeshCountMismatch",
                    "Base and FBM Outfit meshes have different submesh counts (expected " + baseMesh.subMeshCount + ", actual " + targetMesh.subMeshCount + ").",
                    bindingName, rendererPath, -1, out diagnostic);

            int vertexCount = baseMesh.vertexCount;
            Vector3[] basePositions = baseMesh.vertices;
            Vector3[] targetPositions = targetMesh.vertices;
            if (!HasFinitePositions(basePositions) || !HasFinitePositions(targetPositions))
                return Fail("OutfitTopologyPositionInvalid", "Outfit topology normalization requires finite vertex positions.", bindingName, rendererPath, -1, out diagnostic);

            string[] baseMembership;
            string[] targetMembership;
            int[][] baseIndices;
            int[][] targetIndices;
            if (!TryReadTopology(baseMesh, vertexCount, bindingName, rendererPath, out baseMembership, out baseIndices, out diagnostic)) return false;
            if (!TryReadTopology(targetMesh, vertexCount, bindingName, rendererPath, out targetMembership, out targetIndices, out diagnostic)) return false;
            if (!HaveSameIndexShape(baseIndices, targetIndices))
                return Fail("OutfitTopologyTriangleCountMismatch",
                    "Base and FBM Outfit submeshes have different triangle index counts.", bindingName, rendererPath, -1, out diagnostic,
                    "base=" + string.Join(",", baseIndices.Select(value => value.Length)) + "; target=" + string.Join(",", targetIndices.Select(value => value.Length)));

            Vector4[] baseUv0;
            Vector4[] targetUv0;
            if (!TryReadUv0(baseMesh, vertexCount, bindingName, rendererPath, out baseUv0, out diagnostic)) return false;
            if (!TryReadUv0(targetMesh, vertexCount, bindingName, rendererPath, out targetUv0, out diagnostic)) return false;
            if (!TryBuildInitialColors(baseMembership, targetMembership, baseUv0, targetUv0,
                out int[] baseColors, out int[] targetColors))
                return Fail("OutfitTopologyUv0MultisetMismatch",
                    "Base and FBM Outfit meshes do not have the same (submesh membership, UV0) multiset; they may have been exported from different assets.",
                    bindingName, rendererPath, -1, out diagnostic);

            List<int>[] baseAdjacency = BuildAdjacency(vertexCount, baseIndices);
            List<int>[] targetAdjacency = BuildAdjacency(vertexCount, targetIndices);
            RefineColors(baseAdjacency, targetAdjacency, baseColors, targetColors, out baseColors, out targetColors);
            if (!HaveSameColorMultiset(baseColors, targetColors))
                return Fail("OutfitTopologyColorMultisetMismatch",
                    "Base and FBM Outfit meshes have different topology refinement color multisets.", bindingName, rendererPath, -1, out diagnostic);

            Component[] baseComponents = BuildComponents(baseAdjacency, basePositions);
            Component[] targetComponents = BuildComponents(targetAdjacency, targetPositions);
            if (baseComponents.Length != targetComponents.Length)
                return Fail("OutfitTopologyComponentCountMismatch",
                    "Base and FBM Outfit meshes have different connected-component counts (expected " + baseComponents.Length + ", actual " + targetComponents.Length + ").",
                    bindingName, rendererPath, -1, out diagnostic);

            Dictionary<string, List<int>> baseGroups = BuildComponentGroups(baseComponents, baseColors, bindingName, rendererPath, out diagnostic);
            if (baseGroups == null) return false;
            Dictionary<string, List<int>> targetGroups = BuildComponentGroups(targetComponents, targetColors, bindingName, rendererPath, out diagnostic);
            if (targetGroups == null) return false;
            if (!HaveSameGroupShape(baseGroups, targetGroups))
                return Fail("OutfitTopologyComponentSignatureMismatch",
                    "Base and FBM Outfit connected-component signatures do not match.", bindingName, rendererPath, -1, out diagnostic);

            Dictionary<int, int>[] targetColorMaps = new Dictionary<int, int>[targetComponents.Length];
            for (int index = 0; index < targetComponents.Length; index++)
            {
                targetColorMaps[index] = BuildColorMap(targetComponents[index], targetColors);
                if (targetColorMaps[index] == null)
                    return Fail("OutfitTopologyComponentColorCollision",
                        "An FBM Outfit connected component contains duplicate refinement colors, so its vertex correspondence is not unique.",
                        bindingName, rendererPath, -1, out diagnostic);
            }

            List<string> sortedSignatures = baseGroups.Keys.OrderBy(value => value, StringComparer.Ordinal).ToList();
            var assigned = new Dictionary<int, int>();
            var usedTargetComponents = new HashSet<int>();
            var field = new List<FieldEntry>();
            string[] baseSignatureByComponent = BuildSignatureByComponent(baseGroups, baseComponents.Length);
            int anchorCount = 0;
            foreach (string signature in sortedSignatures)
            {
                List<int> baseGroup = baseGroups[signature];
                if (baseGroup.Count != 1) continue;
                int baseComponent = baseGroup[0];
                int targetComponent = targetGroups[signature][0];
                assigned.Add(baseComponent, targetComponent);
                usedTargetComponents.Add(targetComponent);
                field.Add(new FieldEntry(baseComponent, baseComponents[baseComponent].Centroid,
                    targetComponents[targetComponent].Centroid - baseComponents[baseComponent].Centroid));
                anchorCount++;
            }
            field.Sort((left, right) => left.BaseComponent.CompareTo(right.BaseComponent));
            if (anchorCount == 0)
                return Fail("OutfitTopologyAnchorMissing",
                    "No uniquely signed connected component can anchor the Base-to-FBM Outfit correspondence.",
                    bindingName, rendererPath, -1, out diagnostic);

            var pending = new HashSet<int>();
            for (int index = 0; index < baseComponents.Length; index++)
            {
                if (!assigned.ContainsKey(index)) pending.Add(index);
            }

            int propagationLimit = Math.Max(1, baseComponents.Length + 1);
            int propagationRounds = 0;
            while (pending.Count != 0 && propagationRounds++ < propagationLimit)
            {
                var scored = new List<ScoredComponent>();
                foreach (int baseComponentIndex in pending.OrderBy(value => value))
                {
                    string signature = baseSignatureByComponent[baseComponentIndex];
                    var candidates = new List<int>();
                    foreach (int candidate in targetGroups[signature])
                        if (!usedTargetComponents.Contains(candidate)) candidates.Add(candidate);
                    if (candidates.Count == 0) continue;

                    Vector3 delta = EstimateLocalDelta(baseComponents[baseComponentIndex].Centroid, field);
                    var costs = new List<CandidateCost>(candidates.Count);
                    foreach (int candidate in candidates)
                        costs.Add(new CandidateCost(candidate, ComputeCost(baseComponents[baseComponentIndex], targetComponents[candidate], baseColors, targetColorMaps[candidate], basePositions, targetPositions, delta)));
                    costs.Sort(CandidateCostComparer.Instance);
                    double ratio = costs.Count == 1 ? double.PositiveInfinity : ConfidenceRatio(costs[0].Cost, costs[1].Cost);
                    scored.Add(new ScoredComponent(baseComponentIndex, costs[0].ComponentIndex, costs[0].Cost, ratio));
                }
                if (scored.Count == 0) break;
                scored.Sort(ScoredComponentComparer.Instance);

                int takeCount = Math.Max(1, scored.Count / 3);
                int accepted = 0;
                for (int index = 0; index < takeCount; index++)
                {
                    ScoredComponent score = scored[index];
                    if (usedTargetComponents.Contains(score.TargetComponent)) continue;
                    assigned.Add(score.BaseComponent, score.TargetComponent);
                    usedTargetComponents.Add(score.TargetComponent);
                    pending.Remove(score.BaseComponent);
                    field.Add(new FieldEntry(score.BaseComponent, baseComponents[score.BaseComponent].Centroid,
                        targetComponents[score.TargetComponent].Centroid - baseComponents[score.BaseComponent].Centroid));
                    accepted++;
                }
                if (accepted == 0) break;
            }
            if (pending.Count != 0)
                return Fail("OutfitTopologyPropagationStalled",
                    "The deterministic local displacement propagation could not resolve every ambiguous Outfit component.",
                    bindingName, rendererPath, -1, out diagnostic);

            double baseBoundsDiagonal = ComputeBoundsDiagonal(basePositions);
            double equivalentDistance = EquivalentCandidateDistance * baseBoundsDiagonal;
            foreach (int baseComponentIndex in baseGroups.Values.SelectMany(value => value).OrderBy(value => value))
            {
                string signature = baseSignatureByComponent[baseComponentIndex];
                List<int> candidates = targetGroups[signature];
                if (baseGroups[signature].Count == 1) continue;
                Vector3 delta = EstimateLocalDelta(baseComponents[baseComponentIndex].Centroid, field, baseComponentIndex);
                var costs = candidates.Select(candidate => new CandidateCost(candidate,
                    ComputeCost(baseComponents[baseComponentIndex], targetComponents[candidate], baseColors, targetColorMaps[candidate], basePositions, targetPositions, delta)))
                    .ToList();
                costs.Sort(CandidateCostComparer.Instance);
                double ratio = costs.Count == 1 ? double.PositiveInfinity : ConfidenceRatio(costs[0].Cost, costs[1].Cost);
                if (costs[0].ComponentIndex != assigned[baseComponentIndex])
                    return Fail("OutfitTopologyAuditAssignmentMismatch",
                        "Leave-one-out Outfit correspondence audit selected a different FBM component than propagation.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "expectedComponent=" + assigned[baseComponentIndex].ToString(CultureInfo.InvariantCulture) + "; actualBestComponent=" + costs[0].ComponentIndex.ToString(CultureInfo.InvariantCulture));
                if (ratio < ConfidenceThreshold && MaxCandidateVertexDistance(baseComponents[baseComponentIndex],
                    targetColorMaps[costs[0].ComponentIndex], targetColorMaps[costs[1].ComponentIndex], baseColors, targetPositions) > equivalentDistance)
                    return Fail("OutfitTopologyAuditLowConfidence",
                        "Leave-one-out Outfit correspondence confidence is below the accepted threshold.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "component=" + baseComponentIndex.ToString(CultureInfo.InvariantCulture) + "; ratio=" + ratio.ToString("R", CultureInfo.InvariantCulture)
                        + "; threshold=" + ConfidenceThreshold.ToString("R", CultureInfo.InvariantCulture)
                        + "; equivalentDistance=" + equivalentDistance.ToString("R", CultureInfo.InvariantCulture));
            }

            int[] inversePermutation;
            if (!TryBuildPermutation(baseComponents, targetComponents, assigned, baseColors, targetColorMaps,
                out permutation, out inversePermutation, bindingName, rendererPath, out diagnostic)) return false;
            if (!ValidateWindingOracle(baseIndices, targetIndices, inversePermutation, bindingName, rendererPath, out diagnostic)) return false;
            if (!TryApplyPermutation(targetMesh, baseMesh, permutation, targetRenderer,
                baseOutfitRoot, targetOutfitRoot, figureRoot,
                out boneMap, out diagnostic, bindingName, rendererPath)) return false;
            return true;
        }

        private static bool TryReadTopology(Mesh mesh, int vertexCount, string bindingName, string rendererPath,
            out string[] membership, out int[][] indices, out StackMachineDiagnostic diagnostic)
        {
            membership = null;
            indices = null;
            diagnostic = null;
            var masks = new StringBuilder[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++) masks[vertex] = new StringBuilder(new string('0', mesh.subMeshCount));
            indices = new int[mesh.subMeshCount][];
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                SubMeshDescriptor descriptor = mesh.GetSubMesh(submesh);
                if (descriptor.topology != MeshTopology.Triangles)
                    return Fail("OutfitTopologySubMeshTopologyMismatch", "Outfit topology normalization requires triangle submeshes.", bindingName, rendererPath, submesh, out diagnostic,
                        "topology=" + descriptor.topology);
                int[] submeshIndices = mesh.GetIndices(submesh);
                if (submeshIndices.Length != descriptor.indexCount || (submeshIndices.Length % 3) != 0)
                    return Fail("OutfitTopologyTriangleCountMismatch", "An Outfit submesh does not contain a valid triangle index count.", bindingName, rendererPath, submesh, out diagnostic,
                        "indexCount=" + submeshIndices.Length);
                indices[submesh] = submeshIndices;
                for (int index = 0; index < submeshIndices.Length; index++)
                {
                    int vertex = submeshIndices[index];
                    if (vertex < 0 || vertex >= vertexCount)
                        return Fail("OutfitTopologyIndexInvalid", "An Outfit submesh contains an index outside its vertex range.", bindingName, rendererPath, submesh, out diagnostic,
                            "vertexIndex=" + vertex.ToString(CultureInfo.InvariantCulture));
                    masks[vertex][submesh] = '1';
                }
            }
            membership = masks.Select(value => value.ToString()).ToArray();
            return true;
        }

        private static bool HaveIdenticalIndexArrays(Mesh baseMesh, Mesh targetMesh)
        {
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
                if (!baseMesh.GetIndices(submesh).SequenceEqual(targetMesh.GetIndices(submesh))) return false;
            return true;
        }

        private static bool HaveSameIndexShape(int[][] baseIndices, int[][] targetIndices)
        {
            if (baseIndices.Length != targetIndices.Length) return false;
            for (int submesh = 0; submesh < baseIndices.Length; submesh++)
                if (baseIndices[submesh].Length != targetIndices[submesh].Length) return false;
            return true;
        }

        private static bool TryReadUv0(Mesh mesh, int vertexCount, string bindingName, string rendererPath,
            out Vector4[] uv0, out StackMachineDiagnostic diagnostic)
        {
            uv0 = null;
            diagnostic = null;
            var values = new List<Vector4>();
            mesh.GetUVs(0, values);
            if (values.Count != 0 && values.Count != vertexCount)
                return Fail("OutfitTopologyUv0Invalid", "Outfit UV0 is not a complete per-vertex channel.", bindingName, rendererPath, 0, out diagnostic,
                    "uv0Count=" + values.Count.ToString(CultureInfo.InvariantCulture) + "; vertexCount=" + vertexCount.ToString(CultureInfo.InvariantCulture));
            if (values.Count == 0)
            {
                uv0 = new Vector4[vertexCount];
                return true;
            }
            uv0 = values.ToArray();
            return true;
        }

        private static bool TryBuildInitialColors(string[] baseMembership, string[] targetMembership,
            Vector4[] baseUv0, Vector4[] targetUv0, out int[] baseColors, out int[] targetColors)
        {
            baseColors = new int[baseMembership.Length];
            targetColors = new int[targetMembership.Length];
            var palette = new Dictionary<VertexKey, int>();
            for (int index = 0; index < baseColors.Length; index++)
                baseColors[index] = Intern(palette, new VertexKey(baseMembership[index], baseUv0[index]));
            for (int index = 0; index < targetColors.Length; index++)
                targetColors[index] = Intern(palette, new VertexKey(targetMembership[index], targetUv0[index]));

            var baseCounts = new Dictionary<int, int>();
            var targetCounts = new Dictionary<int, int>();
            CountColors(baseColors, baseCounts);
            CountColors(targetColors, targetCounts);
            return baseCounts.Count == targetCounts.Count && baseCounts.All(pair => targetCounts.TryGetValue(pair.Key, out int count) && count == pair.Value);
        }

        private static void RefineColors(List<int>[] baseAdjacency, List<int>[] targetAdjacency,
            int[] baseColors, int[] targetColors, out int[] refinedBase, out int[] refinedTarget)
        {
            refinedBase = baseColors;
            refinedTarget = targetColors;
            int previousBaseClassCount = CountClasses(refinedBase);
            int previousTargetClassCount = CountClasses(refinedTarget);
            int maxRounds = Math.Max(1, Math.Max(refinedBase.Length, refinedTarget.Length));
            for (int round = 0; round < maxRounds; round++)
            {
                var palette = new Dictionary<string, int>(StringComparer.Ordinal);
                int[] nextBase = RefineOne(refinedBase, baseAdjacency, palette);
                int[] nextTarget = RefineOne(refinedTarget, targetAdjacency, palette);
                int baseClassCount = CountClasses(nextBase);
                int targetClassCount = CountClasses(nextTarget);
                refinedBase = nextBase;
                refinedTarget = nextTarget;
                if (baseClassCount == previousBaseClassCount && targetClassCount == previousTargetClassCount) break;
                previousBaseClassCount = baseClassCount;
                previousTargetClassCount = targetClassCount;
            }
        }

        private static int[] RefineOne(int[] colors, List<int>[] adjacency, Dictionary<string, int> palette)
        {
            int[] result = new int[colors.Length];
            for (int vertex = 0; vertex < colors.Length; vertex++)
            {
                int[] neighborColors = adjacency[vertex].Select(index => colors[index]).OrderBy(value => value).ToArray();
                string key = colors[vertex].ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", neighborColors);
                result[vertex] = Intern(palette, key);
            }
            return result;
        }

        private static List<int>[] BuildAdjacency(int vertexCount, int[][] indices)
        {
            var adjacency = new List<int>[vertexCount];
            var sets = new HashSet<int>[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++) sets[vertex] = new HashSet<int>();
            foreach (int[] submesh in indices)
            {
                for (int index = 0; index < submesh.Length; index += 3)
                {
                    int a = submesh[index];
                    int b = submesh[index + 1];
                    int c = submesh[index + 2];
                    sets[a].Add(b); sets[a].Add(c);
                    sets[b].Add(a); sets[b].Add(c);
                    sets[c].Add(a); sets[c].Add(b);
                }
            }
            for (int vertex = 0; vertex < vertexCount; vertex++) adjacency[vertex] = sets[vertex].OrderBy(value => value).ToList();
            return adjacency;
        }

        private static Component[] BuildComponents(List<int>[] adjacency, Vector3[] positions)
        {
            var componentByVertex = Enumerable.Repeat(-1, adjacency.Length).ToArray();
            var components = new List<Component>();
            for (int seed = 0; seed < adjacency.Length; seed++)
            {
                if (componentByVertex[seed] >= 0) continue;
                int componentIndex = components.Count;
                var vertices = new List<int>();
                var stack = new Stack<int>();
                stack.Push(seed);
                componentByVertex[seed] = componentIndex;
                while (stack.Count != 0)
                {
                    int vertex = stack.Pop();
                    vertices.Add(vertex);
                    foreach (int neighbor in adjacency[vertex])
                    {
                        if (componentByVertex[neighbor] >= 0) continue;
                        componentByVertex[neighbor] = componentIndex;
                        stack.Push(neighbor);
                    }
                }
                vertices.Sort();
                Vector3 centroid = Vector3.zero;
                for (int index = 0; index < vertices.Count; index++) centroid += positions[vertices[index]];
                centroid /= vertices.Count;
                components.Add(new Component(vertices.ToArray(), centroid));
            }
            return components.ToArray();
        }

        private static Dictionary<string, List<int>> BuildComponentGroups(Component[] components, int[] colors,
            string bindingName, string rendererPath, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int component = 0; component < components.Length; component++)
            {
                int[] componentColors = components[component].Vertices.Select(vertex => colors[vertex]).ToArray();
                if (componentColors.Distinct().Count() != componentColors.Length)
                {
                    Fail("OutfitTopologyComponentColorCollision",
                        "A connected Outfit component contains duplicate refinement colors, so its vertex correspondence is not unique.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "component=" + component.ToString(CultureInfo.InvariantCulture));
                    return null;
                }
                string signature = string.Join(",", componentColors.OrderBy(value => value));
                if (!result.TryGetValue(signature, out List<int> group)) result.Add(signature, group = new List<int>());
                group.Add(component);
            }
            return result;
        }

        private static bool HaveSameGroupShape(Dictionary<string, List<int>> baseGroups, Dictionary<string, List<int>> targetGroups)
        {
            if (baseGroups.Count != targetGroups.Count) return false;
            foreach (KeyValuePair<string, List<int>> pair in baseGroups)
                if (!targetGroups.TryGetValue(pair.Key, out List<int> target) || target.Count != pair.Value.Count) return false;
            return true;
        }

        private static Dictionary<int, int> BuildColorMap(Component component, int[] colors)
        {
            var map = new Dictionary<int, int>();
            foreach (int vertex in component.Vertices)
                if (!map.TryAdd(colors[vertex], vertex)) return null;
            return map;
        }

        private static string[] BuildSignatureByComponent(Dictionary<string, List<int>> groups, int componentCount)
        {
            var result = new string[componentCount];
            foreach (KeyValuePair<string, List<int>> group in groups)
                foreach (int component in group.Value)
                    result[component] = group.Key;
            return result;
        }

        private static Vector3 EstimateLocalDelta(Vector3 centroid, List<FieldEntry> field, int excludedComponent = -1)
        {
            var nearest = new List<NearbyField>(field.Count);
            for (int index = 0; index < field.Count; index++)
            {
                FieldEntry entry = field[index];
                if (entry.BaseComponent != excludedComponent)
                    nearest.Add(new NearbyField(entry.BaseComponent, DistanceSquared(centroid, entry.Centroid), entry.Delta));
            }
            nearest.Sort(NearbyFieldComparer.Instance);
            int takeCount = Math.Min(LocalFieldCount, nearest.Count);
            double weightSum = 0.0;
            var weighted = Vector3.zero;
            for (int index = 0; index < takeCount; index++)
            {
                double distance = Math.Sqrt(nearest[index].DistanceSquared);
                double weight = 1.0 / (distance + InverseDistanceEpsilon);
                weighted += nearest[index].Delta * (float)weight;
                weightSum += weight;
            }
            return weightSum == 0.0 ? Vector3.zero : weighted / (float)weightSum;
        }

        private static double ComputeCost(Component source, Component target, int[] baseColors, Dictionary<int, int> targetColorMap,
            Vector3[] basePositions, Vector3[] targetPositions, Vector3 delta)
        {
            double total = 0.0;
            for (int index = 0; index < source.Vertices.Length; index++)
            {
                int baseVertex = source.Vertices[index];
                if (!targetColorMap.TryGetValue(baseColors[baseVertex], out int targetVertex)) return double.PositiveInfinity;
                total += Distance(basePositions[baseVertex] + delta, targetPositions[targetVertex]);
            }
            return total / source.Vertices.Length;
        }

        private static double ConfidenceRatio(double best, double second)
        {
            if (best <= 0.0) return double.PositiveInfinity;
            return (second - best) / best;
        }

        private static double MaxCandidateVertexDistance(Component source, Dictionary<int, int> best, Dictionary<int, int> second,
            int[] baseColors, Vector3[] targetPositions)
        {
            double max = 0.0;
            foreach (int baseVertex in source.Vertices)
            {
                int color = baseColors[baseVertex];
                double distance = Distance(targetPositions[best[color]], targetPositions[second[color]]);
                if (distance > max) max = distance;
            }
            return max;
        }

        private static bool TryBuildPermutation(Component[] baseComponents, Component[] targetComponents,
            Dictionary<int, int> assigned, int[] baseColors, Dictionary<int, int>[] targetColorMaps,
            out int[] permutation, out int[] inversePermutation, string bindingName, string rendererPath,
            out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            permutation = Enumerable.Repeat(-1, baseColors.Length).ToArray();
            inversePermutation = Enumerable.Repeat(-1, baseColors.Length).ToArray();
            foreach (int baseComponent in Enumerable.Range(0, baseComponents.Length).OrderBy(value => value))
            {
                if (!assigned.TryGetValue(baseComponent, out int targetComponent) || targetComponent < 0 || targetComponent >= targetComponents.Length)
                    return Fail("OutfitTopologyPermutationIncomplete", "Outfit vertex correspondence did not assign every connected component.", bindingName, rendererPath, -1, out diagnostic);
                foreach (int baseVertex in baseComponents[baseComponent].Vertices)
                {
                    if (!targetColorMaps[targetComponent].TryGetValue(baseColors[baseVertex], out int targetVertex)
                        || permutation[baseVertex] >= 0 || inversePermutation[targetVertex] >= 0)
                        return Fail("OutfitTopologyPermutationNotBijective", "The resolved Base-to-FBM Outfit vertex correspondence is not a bijection.", bindingName, rendererPath, -1, out diagnostic,
                            "baseVertex=" + baseVertex.ToString(CultureInfo.InvariantCulture));
                    permutation[baseVertex] = targetVertex;
                    inversePermutation[targetVertex] = baseVertex;
                }
            }
            if (permutation.Any(value => value < 0) || inversePermutation.Any(value => value < 0))
                return Fail("OutfitTopologyPermutationNotBijective", "The resolved Base-to-FBM Outfit vertex correspondence is not a complete bijection.", bindingName, rendererPath, -1, out diagnostic);
            return true;
        }

        private static bool ValidateWindingOracle(int[][] baseIndices, int[][] targetIndices, int[] inversePermutation,
            string bindingName, string rendererPath, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            for (int submesh = 0; submesh < baseIndices.Length; submesh++)
            {
                var baseTriangles = new Dictionary<TriangleKey, int>();
                var targetTriangles = new Dictionary<TriangleKey, int>();
                for (int index = 0; index < baseIndices[submesh].Length; index += 3)
                {
                    AddCount(baseTriangles, TriangleKey.Normalize(baseIndices[submesh][index], baseIndices[submesh][index + 1], baseIndices[submesh][index + 2]));
                    AddCount(targetTriangles, TriangleKey.Normalize(inversePermutation[targetIndices[submesh][index]], inversePermutation[targetIndices[submesh][index + 1]], inversePermutation[targetIndices[submesh][index + 2]]));
                }
                if (baseTriangles.Count != targetTriangles.Count || baseTriangles.Any(pair => !targetTriangles.TryGetValue(pair.Key, out int count) || count != pair.Value))
                    return Fail("OutfitTopologyWindingOracleMismatch", "Base and FBM Outfit triangle multisets do not match after correspondence resolution; winding was not preserved.", bindingName, rendererPath, submesh, out diagnostic);
            }
            return true;
        }

        private static bool TryCaptureBoneWeights(Mesh mesh, string bindingName, string rendererPath, bool isBase,
            out ShapeSyncMeshBoneWeights boneWeights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            try
            {
                boneWeights = ShapeSyncMeshBoneWeights.Capture(mesh);
                return true;
            }
            catch (Exception exception)
            {
                boneWeights = null;
                string code = isBase ? "OutfitTopologyBaseBoneWeightInvalid" : "OutfitTopologyBoneWeightInvalid";
                string side = isBase ? "Base" : "FBM target";
                return Fail(code, "The " + side + " Outfit BoneWeight1 channel is invalid and cannot be normalized: " + exception.Message,
                    bindingName, rendererPath, -1, out diagnostic);
            }
        }

        private static bool TryPrepareBoneNormalization(Mesh baseMesh, Mesh targetMesh, int[] permutation,
            SkinnedMeshRenderer targetRenderer, ShapeSyncMeshBoneWeights baseBoneWeights,
            ShapeSyncMeshBoneWeights targetBoneWeights, out ShapeSyncMeshBoneWeights normalizedBoneWeights,
            out Matrix4x4[] normalizedBindposes, out Transform[] normalizedRendererBones,
            out int[] boneMap, out StackMachineDiagnostic diagnostic, string bindingName, string rendererPath,
            Transform baseOutfitRoot, Transform targetOutfitRoot, Transform figureRoot)
        {
            normalizedBoneWeights = null;
            normalizedBindposes = null;
            normalizedRendererBones = null;
            boneMap = null;
            diagnostic = null;

            int baseBindposeCount = baseMesh.bindposes.Length;
            int targetBindposeCount = targetMesh.bindposes.Length;
            if (baseBoneWeights == null && targetBoneWeights == null)
            {
                boneMap = Enumerable.Range(0, baseBindposeCount).ToArray();
                return true;
            }
            if (baseBoneWeights == null || targetBoneWeights == null)
                return Fail("OutfitTopologyBoneWeightMismatch",
                    "Base and FBM Outfit meshes do not both contain a BoneWeight1 channel.",
                    bindingName, rendererPath, -1, out diagnostic);
            if (baseBindposeCount != targetBindposeCount)
                return Fail("OutfitTopologyBindposeCountMismatch",
                    "Base and FBM Outfit meshes have different bindpose counts (expected " + baseBindposeCount + ", actual " + targetBindposeCount + ").",
                    bindingName, rendererPath, -1, out diagnostic);

            int[] targetToBase;
            if (!TryDeriveBoneMap(baseBoneWeights, targetBoneWeights, permutation, baseBindposeCount,
                out boneMap, out targetToBase, out diagnostic, bindingName, rendererPath)) return false;
            try
            {
                normalizedBoneWeights = targetBoneWeights.Remap(permutation).RemapBoneIndices(targetToBase);
            }
            catch (Exception exception)
            {
                return Fail("OutfitTopologyBoneWeightInvalid", "The normalized FBM Outfit BoneWeight1 channel is invalid: " + exception.Message,
                    bindingName, rendererPath, -1, out diagnostic);
            }

            if (!HaveExactBoneWeights(baseBoneWeights, normalizedBoneWeights, out int mismatchVertex, out int mismatchInfluence))
                return Fail("OutfitTopologyBoneWeightMismatch",
                    "FBM Outfit BoneWeight1 data does not exactly match Base after the derived bone mapping.",
                    bindingName, rendererPath, -1, out diagnostic,
                    "vertex=" + mismatchVertex.ToString(CultureInfo.InvariantCulture) + "; influence=" + mismatchInfluence.ToString(CultureInfo.InvariantCulture));

            Matrix4x4[] sourceBindposes = targetMesh.bindposes;
            normalizedBindposes = new Matrix4x4[baseBindposeCount];
            for (int baseBone = 0; baseBone < baseBindposeCount; baseBone++)
                normalizedBindposes[baseBone] = sourceBindposes[boneMap[baseBone]];

            if (targetRenderer != null)
            {
                Transform[] sourceBones = targetRenderer.bones;
                normalizedRendererBones = new Transform[baseBindposeCount];
                for (int baseBone = 0; baseBone < baseBindposeCount; baseBone++)
                    normalizedRendererBones[baseBone] = sourceBones[boneMap[baseBone]];
            }
            return true;
        }

        private sealed class ExtraBoneNormalizationPair
        {
            internal Transform Target;
            internal Transform Base;
            internal Transform TargetParent;
            internal Transform OriginalParent;
            internal int OriginalSiblingIndex;
            internal string OriginalName;
        }

        private static bool TryValidateExtraBoneHierarchyInputs(SkinnedMeshRenderer baseRenderer,
            SkinnedMeshRenderer targetRenderer, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out StackMachineDiagnostic diagnostic, string bindingName, string rendererPath)
        {
            diagnostic = null;
            if (baseRenderer == null || targetRenderer == null)
                return Fail("OutfitTopologyExtraBoneRendererInvalid", "Extra Bone hierarchy normalization requires both renderers.", bindingName, rendererPath, -1, out diagnostic);
            if (baseOutfitRoot == null || targetOutfitRoot == null || figureRoot == null)
                return Fail("OutfitTopologyExtraBoneRootsInvalid", "Extra Bone hierarchy normalization requires Base, target, and Figure roots.", bindingName, rendererPath, -1, out diagnostic);

            Transform[] baseBones = baseRenderer.bones;
            Transform[] targetBones = targetRenderer.bones;
            if (baseBones == null || targetBones == null)
                return Fail("OutfitTopologyExtraBoneBoneTableInvalid", "Extra Bone hierarchy normalization requires non-null bone tables.", bindingName, rendererPath, -1, out diagnostic);
            if (baseBones.Length != targetBones.Length)
                return Fail("OutfitTopologyExtraBoneBoneTableMismatch", "Base and FBM target bone tables must have the same length before Extra Bone hierarchy normalization.", bindingName, rendererPath, -1, out diagnostic,
                    "baseBones=" + baseBones.Length.ToString(CultureInfo.InvariantCulture) + "; targetBones=" + targetBones.Length.ToString(CultureInfo.InvariantCulture));

            for (int index = 0; index < baseBones.Length; index++)
            {
                Transform baseBone = baseBones[index];
                if (baseBone == null) continue;
                string basePath = RelativePath(baseOutfitRoot, baseBone);
                if (basePath == null || IsFigureBone(figureRoot, basePath)) continue;

                Transform targetBone = targetBones[index];
                if (targetBone == null)
                    return Fail("OutfitTopologyExtraBoneBoneTableInvalid", "An Extra Bone renderer slot has no target transform.", bindingName, rendererPath, -1, out diagnostic,
                        "boneIndex=" + index.ToString(CultureInfo.InvariantCulture) + "; basePath=" + basePath);
                if (RelativePath(targetOutfitRoot, targetBone) == null)
                    return Fail("OutfitTopologyExtraBoneTargetPathInvalid", "An Extra Bone target transform is outside the target Outfit root.", bindingName, rendererPath, -1, out diagnostic,
                        "boneIndex=" + index.ToString(CultureInfo.InvariantCulture) + "; basePath=" + basePath);

                if (baseBone.parent == null) continue;
                int baseParentIndex = FindTransformIndex(baseBones, baseBone.parent);
                if (baseParentIndex < 0 || baseParentIndex >= targetBones.Length || targetBones[baseParentIndex] == null)
                    return Fail("OutfitTopologyExtraBoneParentMissing", "An Extra Bone parent is not represented in the renderer bone table; hierarchy normalization cannot infer a safe Base path.", bindingName, rendererPath, -1, out diagnostic,
                        "boneIndex=" + index.ToString(CultureInfo.InvariantCulture) + "; basePath=" + basePath);
            }
            return true;
        }

        private static bool TryNormalizeExtraBoneHierarchy(SkinnedMeshRenderer baseRenderer,
            SkinnedMeshRenderer targetRenderer, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out StackMachineDiagnostic diagnostic, string bindingName, string rendererPath)
        {
            diagnostic = null;
            if (!TryValidateExtraBoneHierarchyInputs(baseRenderer, targetRenderer, baseOutfitRoot, targetOutfitRoot,
                figureRoot, out diagnostic, bindingName, rendererPath)) return false;

            Transform[] baseBones = baseRenderer.bones;
            Transform[] targetBones = targetRenderer.bones;

            var pairs = new List<ExtraBoneNormalizationPair>();
            var targetSet = new HashSet<Transform>();
            for (int index = 0; index < baseBones.Length; index++)
            {
                Transform baseBone = baseBones[index];
                Transform targetBone = targetBones[index];
                string basePath = RelativePath(baseOutfitRoot, baseBone);
                if (baseBone == null || targetBone == null || basePath == null || IsFigureBone(figureRoot, basePath)
                    || !targetSet.Add(targetBone)) continue;

                Transform targetParent = targetBone.parent;
                int baseParentIndex = FindTransformIndex(baseBones, baseBone.parent);
                if (baseParentIndex >= 0 && baseParentIndex < targetBones.Length && targetBones[baseParentIndex] != null)
                    targetParent = targetBones[baseParentIndex];
                pairs.Add(new ExtraBoneNormalizationPair
                {
                    Target = targetBone,
                    Base = baseBone,
                    TargetParent = targetParent,
                    OriginalParent = targetBone.parent,
                    OriginalSiblingIndex = targetBone.GetSiblingIndex(),
                    OriginalName = targetBone.name
                });
            }

            if (pairs.Count == 0) return true;

            // Preserve the import artifact byte-for-byte when the normalized bone
            // references already resolve to the same Extra Bone paths.  In particular,
            // an identity topology must not be rewritten just because this overload
            // received authoring roots.
            bool requiresRebuild = false;
            for (int index = 0; index < pairs.Count; index++)
            {
                ExtraBoneNormalizationPair pair = pairs[index];
                string targetPath = RelativePath(targetOutfitRoot, pair.Target);
                string basePath = RelativePath(baseOutfitRoot, pair.Base);
                if (!string.Equals(targetPath, basePath, StringComparison.Ordinal))
                {
                    requiresRebuild = true;
                    break;
                }
            }
            if (!requiresRebuild) return true;

            bool completed = false;
            try
            {
                // Rename every mapped Extra Bone into a temporary namespace before moving
                // branches.  This prevents cyclic FBM bone permutations from colliding with
                // names that still belong to an unmoved sibling branch.
                for (int index = 0; index < pairs.Count; index++)
                {
                    ExtraBoneNormalizationPair pair = pairs[index];
                    pair.Target.name = "__ShapeSyncNormalizedExtraBone_" + pair.Target.GetInstanceID().ToString(CultureInfo.InvariantCulture);
                }

                // The normalized renderer bone at Base slot i is the FBM bone selected by
                // boneMap[i].  Rebuild only the target-side Extra Bone tree so that this
                // selected transform is reachable at the Base path.  Figure-owned bones
                // remain untouched and continue to be resolved from the canonical Figure.
                for (int index = 0; index < pairs.Count; index++)
                {
                    ExtraBoneNormalizationPair pair = pairs[index];
                    if (pair.TargetParent != null && pair.TargetParent != pair.Target)
                        pair.Target.SetParent(pair.TargetParent, false);
                }

                for (int index = 0; index < pairs.Count; index++)
                {
                    ExtraBoneNormalizationPair pair = pairs[index];
                    pair.Target.name = pair.Base.name;
                }
                completed = true;
                return true;
            }
            catch (Exception exception)
            {
                return Fail("OutfitTopologyExtraBoneHierarchyFailed", "Extra Bone hierarchy normalization failed and was rolled back.", bindingName, rendererPath, -1, out diagnostic,
                    "exception=" + exception.GetType().Name + "; message=" + exception.Message);
            }
            finally
            {
                if (completed)
                {
                    for (int index = 0; index < pairs.Count; index++)
                    {
                        ExtraBoneNormalizationPair pair = pairs[index];
                        try { if (pair.Target != null && pair.Base != null) pair.Target.name = pair.Base.name; }
                        catch { /* Unity object teardown must not prevent the remaining names from being restored. */ }
                    }
                }
                else
                {
                    for (int index = pairs.Count - 1; index >= 0; index--)
                    {
                        ExtraBoneNormalizationPair pair = pairs[index];
                        try
                        {
                            if (pair.Target == null) continue;
                            pair.Target.name = pair.OriginalName;
                            if (pair.Target.parent != pair.OriginalParent)
                                pair.Target.SetParent(pair.OriginalParent, false);
                            if (pair.OriginalSiblingIndex >= 0)
                                pair.Target.SetSiblingIndex(pair.OriginalSiblingIndex);
                        }
                        catch { /* Preserve the original diagnostic even if Unity is tearing down the object. */ }
                    }
                }
            }
        }

        private static int FindTransformIndex(Transform[] transforms, Transform target)
        {
            if (transforms == null || target == null) return -1;
            for (int index = 0; index < transforms.Length; index++)
                if (transforms[index] == target) return index;
            return -1;
        }

        private static bool IsFigureBone(Transform figureRoot, string path)
        {
            return figureRoot != null && path != null && figureRoot.Find(path) != null;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            if (current != root) return null;
            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private static bool TryDeriveBoneMap(ShapeSyncMeshBoneWeights baseBoneWeights,
            ShapeSyncMeshBoneWeights targetBoneWeights, int[] permutation, int boneCount,
            out int[] boneMap, out int[] targetToBase, out StackMachineDiagnostic diagnostic,
            string bindingName, string rendererPath)
        {
            boneMap = null;
            targetToBase = null;
            diagnostic = null;
            int[] baseOffsets = BuildBoneWeightOffsets(baseBoneWeights.BonesPerVertex);
            int[] targetOffsets = BuildBoneWeightOffsets(targetBoneWeights.BonesPerVertex);
            var baseWeightedBones = new HashSet<int>();
            var targetWeightedBones = new HashSet<int>();
            var votes = new Dictionary<int, Dictionary<int, int>>();

            for (int vertex = 0; vertex < baseBoneWeights.BonesPerVertex.Length; vertex++)
            {
                int targetVertex = permutation[vertex];
                if (targetVertex < 0 || targetVertex >= targetBoneWeights.BonesPerVertex.Length)
                    return Fail("OutfitTopologyPermutationNotBijective", "The vertex correspondence references an invalid target vertex while deriving the bone mapping.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "baseVertex=" + vertex.ToString(CultureInfo.InvariantCulture));
                var baseInfluences = GetSortedPositiveInfluences(baseBoneWeights, baseOffsets[vertex], baseBoneWeights.BonesPerVertex[vertex], boneCount,
                    bindingName, rendererPath, true, out diagnostic);
                if (baseInfluences == null) return false;
                var targetInfluences = GetSortedPositiveInfluences(targetBoneWeights, targetOffsets[targetVertex], targetBoneWeights.BonesPerVertex[targetVertex], boneCount,
                    bindingName, rendererPath, false, out diagnostic);
                if (targetInfluences == null) return false;
                if (baseInfluences.Count != targetInfluences.Count)
                    return Fail("OutfitTopologyBoneWeightMismatch", "Base and FBM Outfit vertices have different positive BoneWeight1 influence counts after vertex correspondence.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "vertex=" + vertex.ToString(CultureInfo.InvariantCulture) + "; targetVertex=" + targetVertex.ToString(CultureInfo.InvariantCulture));

                for (int influence = 0; influence < baseInfluences.Count; influence++)
                {
                    BoneWeight1 baseWeight = baseInfluences[influence];
                    BoneWeight1 targetWeight = targetInfluences[influence];
                    if (FloatBits(baseWeight.weight) != FloatBits(targetWeight.weight))
                        return Fail("OutfitTopologyBoneWeightMismatch", "Base and FBM Outfit BoneWeight1 values differ under the resolved vertex correspondence.",
                            bindingName, rendererPath, -1, out diagnostic,
                            "vertex=" + vertex.ToString(CultureInfo.InvariantCulture) + "; targetVertex=" + targetVertex.ToString(CultureInfo.InvariantCulture)
                            + "; influence=" + influence.ToString(CultureInfo.InvariantCulture));
                    baseWeightedBones.Add(baseWeight.boneIndex);
                    targetWeightedBones.Add(targetWeight.boneIndex);
                    if (!votes.TryGetValue(baseWeight.boneIndex, out Dictionary<int, int> targetVotes))
                        votes.Add(baseWeight.boneIndex, targetVotes = new Dictionary<int, int>());
                    targetVotes[targetWeight.boneIndex] = targetVotes.TryGetValue(targetWeight.boneIndex, out int count) ? count + 1 : 1;
                }
            }

            for (int index = 0; index < boneCount; index++)
                if (baseWeightedBones.Contains(index) != targetWeightedBones.Contains(index))
                    return Fail("OutfitTopologyBoneMapNotClosed", "The weighted Base-to-FBM bone correspondence is not closed over the shared bone table.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "boneIndex=" + index.ToString(CultureInfo.InvariantCulture));

            boneMap = Enumerable.Range(0, boneCount).ToArray();
            foreach (int baseBone in baseWeightedBones)
            {
                if (!votes.TryGetValue(baseBone, out Dictionary<int, int> targetVotes) || targetVotes.Count == 0)
                    return Fail("OutfitTopologyBoneMapIncomplete", "A weighted Base bone did not receive a correspondence vote.", bindingName, rendererPath, -1, out diagnostic,
                        "baseBone=" + baseBone.ToString(CultureInfo.InvariantCulture));
                int bestTarget = -1;
                int bestCount = -1;
                int total = 0;
                bool tie = false;
                foreach (KeyValuePair<int, int> vote in targetVotes.OrderBy(pair => pair.Key))
                {
                    total += vote.Value;
                    if (vote.Value > bestCount)
                    {
                        bestTarget = vote.Key;
                        bestCount = vote.Value;
                        tie = false;
                    }
                    else if (vote.Value == bestCount)
                    {
                        tie = true;
                    }
                }
                if (tie || bestCount != total)
                    return Fail("OutfitTopologyBoneMapAmbiguous", "The Base-to-FBM bone correspondence did not achieve exact purity.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "baseBone=" + baseBone.ToString(CultureInfo.InvariantCulture) + "; best=" + bestCount.ToString(CultureInfo.InvariantCulture)
                        + "; total=" + total.ToString(CultureInfo.InvariantCulture));
                boneMap[baseBone] = bestTarget;
            }

            targetToBase = Enumerable.Repeat(-1, boneCount).ToArray();
            for (int baseBone = 0; baseBone < boneMap.Length; baseBone++)
            {
                int targetBone = boneMap[baseBone];
                if (targetBone < 0 || targetBone >= targetToBase.Length || targetToBase[targetBone] >= 0)
                    return Fail("OutfitTopologyBoneMapNotBijective", "The derived Base-to-FBM bone correspondence is not a bijection.",
                        bindingName, rendererPath, -1, out diagnostic,
                        "baseBone=" + baseBone.ToString(CultureInfo.InvariantCulture) + "; targetBone=" + targetBone.ToString(CultureInfo.InvariantCulture));
                targetToBase[targetBone] = baseBone;
            }
            if (targetToBase.Any(value => value < 0))
                return Fail("OutfitTopologyBoneMapNotBijective", "The derived Base-to-FBM bone correspondence does not cover the shared bone table.",
                    bindingName, rendererPath, -1, out diagnostic);
            return true;
        }

        private static List<BoneWeight1> GetSortedPositiveInfluences(ShapeSyncMeshBoneWeights weights, int offset, int count,
            int boneCount, string bindingName, string rendererPath, bool isBase, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var result = new List<BoneWeight1>(count);
            for (int influence = 0; influence < count; influence++)
            {
                BoneWeight1 weight = weights.Weights[offset + influence];
                if (weight.boneIndex < 0 || weight.boneIndex >= boneCount)
                {
                    Fail(isBase ? "OutfitTopologyBaseBoneIndexInvalid" : "OutfitTopologyBoneIndexInvalid",
                        "An Outfit BoneWeight1 bone index is outside the mesh bindpose range.", bindingName, rendererPath, -1, out diagnostic,
                        "boneIndex=" + weight.boneIndex.ToString(CultureInfo.InvariantCulture) + "; boneCount=" + boneCount.ToString(CultureInfo.InvariantCulture));
                    return null;
                }
                if (weight.weight > 0f) result.Add(weight);
            }
            result.Sort(BoneWeightComparer.Instance);
            return result;
        }

        private static int[] BuildBoneWeightOffsets(byte[] bonesPerVertex)
        {
            var offsets = new int[bonesPerVertex.Length];
            int offset = 0;
            for (int vertex = 0; vertex < bonesPerVertex.Length; vertex++)
            {
                offsets[vertex] = offset;
                offset += bonesPerVertex[vertex];
            }
            return offsets;
        }

        private static bool HaveExactBoneWeights(ShapeSyncMeshBoneWeights expected, ShapeSyncMeshBoneWeights actual,
            out int mismatchVertex, out int mismatchInfluence)
        {
            mismatchVertex = -1;
            mismatchInfluence = -1;
            if (expected.BonesPerVertex.Length != actual.BonesPerVertex.Length)
                return false;
            for (int vertex = 0; vertex < expected.BonesPerVertex.Length; vertex++)
                if (expected.BonesPerVertex[vertex] != actual.BonesPerVertex[vertex])
                {
                    mismatchVertex = vertex;
                    return false;
                }
            if (expected.Weights.Length != actual.Weights.Length)
                return false;
            for (int influence = 0; influence < expected.Weights.Length; influence++)
                if (expected.Weights[influence].boneIndex != actual.Weights[influence].boneIndex
                    || FloatBits(expected.Weights[influence].weight) != FloatBits(actual.Weights[influence].weight))
                {
                    mismatchInfluence = influence;
                    return false;
                }
            return true;
        }

        private static int FloatBits(float value) => BitConverter.SingleToInt32Bits(value);

        private static bool TryApplyBoneNormalization(Mesh targetMesh, Mesh baseMesh, int[] permutation,
            SkinnedMeshRenderer targetRenderer, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out int[] boneMap, out StackMachineDiagnostic diagnostic,
            string bindingName, string rendererPath)
        {
            diagnostic = null;
            boneMap = null;
            if (!TryCaptureBoneWeights(baseMesh, bindingName, rendererPath, true,
                out ShapeSyncMeshBoneWeights baseBoneWeights, out diagnostic)) return false;
            if (!TryCaptureBoneWeights(targetMesh, bindingName, rendererPath, false,
                out ShapeSyncMeshBoneWeights targetBoneWeights, out diagnostic)) return false;
            if (!TryPrepareBoneNormalization(baseMesh, targetMesh, permutation, targetRenderer,
                baseBoneWeights, targetBoneWeights, out ShapeSyncMeshBoneWeights normalizedBoneWeights,
                out Matrix4x4[] normalizedBindposes, out Transform[] normalizedRendererBones,
                out boneMap, out diagnostic, bindingName, rendererPath,
                baseOutfitRoot, targetOutfitRoot, figureRoot)) return false;
            if (normalizedBoneWeights != null) normalizedBoneWeights.Apply(targetMesh);
            if (normalizedBindposes != null) targetMesh.bindposes = normalizedBindposes;
            if (normalizedRendererBones != null) targetRenderer.bones = normalizedRendererBones;
            return true;
        }

        private static bool TryApplyPermutation(Mesh targetMesh, Mesh baseMesh, int[] permutation,
            SkinnedMeshRenderer targetRenderer, Transform baseOutfitRoot, Transform targetOutfitRoot,
            Transform figureRoot, out int[] boneMap,
            out StackMachineDiagnostic diagnostic, string bindingName, string rendererPath)
        {
            diagnostic = null;
            boneMap = null;
            int vertexCount = targetMesh.vertexCount;
            if (!TryReadOptionalAttribute(targetMesh.normals, vertexCount, "normal", bindingName, rendererPath, out Vector3[] normals, out diagnostic)) return false;
            if (!TryReadOptionalAttribute(targetMesh.tangents, vertexCount, "tangent", bindingName, rendererPath, out Vector4[] tangents, out diagnostic)) return false;
            if (!TryReadOptionalAttribute(targetMesh.colors, vertexCount, "color", bindingName, rendererPath, out Color[] colors, out diagnostic)) return false;
            if (!TryCaptureBoneWeights(baseMesh, bindingName, rendererPath, true,
                out ShapeSyncMeshBoneWeights baseBoneWeights, out diagnostic)) return false;
            if (!TryCaptureBoneWeights(targetMesh, bindingName, rendererPath, false,
                out ShapeSyncMeshBoneWeights boneWeights, out diagnostic)) return false;
            if (!TryPrepareBoneNormalization(baseMesh, targetMesh, permutation, targetRenderer,
                baseBoneWeights, boneWeights, out ShapeSyncMeshBoneWeights remappedBoneWeights,
                out Matrix4x4[] normalizedBindposes, out Transform[] normalizedRendererBones,
                out boneMap, out diagnostic, bindingName, rendererPath,
                baseOutfitRoot, targetOutfitRoot, figureRoot)) return false;

            Vector4[][] uvChannels = new Vector4[8][];
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                var values = new List<Vector4>();
                targetMesh.GetUVs(channel, values);
                if (values.Count != 0 && values.Count != vertexCount)
                    return Fail("OutfitTopologyAttributeInvalid", "An Outfit per-vertex UV channel is incomplete and cannot be remapped.", bindingName, rendererPath, channel, out diagnostic,
                        "channel=" + channel.ToString(CultureInfo.InvariantCulture) + "; valueCount=" + values.Count.ToString(CultureInfo.InvariantCulture));
                uvChannels[channel] = values.Count == 0 ? null : values.ToArray();
            }

            var blendShapes = CaptureBlendShapes(targetMesh);
            Vector3[] vertices = targetMesh.vertices;
            Vector3[] remappedVertices = Remap(vertices, permutation);
            Vector3[] remappedNormals = normals == null ? null : Remap(normals, permutation);
            Vector4[] remappedTangents = tangents == null ? null : Remap(tangents, permutation);
            Color[] remappedColors = colors == null ? null : Remap(colors, permutation);
            Vector4[][] remappedUvs = uvChannels.Select(values => values == null ? null : Remap(values, permutation)).ToArray();

            targetMesh.indexFormat = baseMesh.indexFormat;
            targetMesh.vertices = remappedVertices;
            if (remappedNormals != null) targetMesh.normals = remappedNormals;
            if (remappedTangents != null) targetMesh.tangents = remappedTangents;
            if (remappedColors != null) targetMesh.colors = remappedColors;
            if (remappedBoneWeights != null) remappedBoneWeights.Apply(targetMesh);
            if (normalizedBindposes != null) targetMesh.bindposes = normalizedBindposes;
            if (normalizedRendererBones != null) targetRenderer.bones = normalizedRendererBones;
            for (int channel = 0; channel < remappedUvs.Length; channel++)
                if (remappedUvs[channel] != null) targetMesh.SetUVs(channel, remappedUvs[channel]);

            targetMesh.subMeshCount = baseMesh.subMeshCount;
            for (int submesh = 0; submesh < baseMesh.subMeshCount; submesh++)
            {
                SubMeshDescriptor descriptor = baseMesh.GetSubMesh(submesh);
                targetMesh.SetIndices(baseMesh.GetIndices(submesh, false), descriptor.topology, submesh, true, descriptor.baseVertex);
            }
            targetMesh.ClearBlendShapes();
            foreach (BlendShapeData shape in blendShapes)
            {
                foreach (BlendShapeFrame frame in shape.Frames)
                    targetMesh.AddBlendShapeFrame(shape.Name, frame.Weight, Remap(frame.Vertices, permutation),
                        Remap(frame.Normals, permutation), Remap(frame.Tangents, permutation));
            }
            targetMesh.RecalculateBounds();
            return true;
        }

        private static bool TryReadOptionalAttribute<T>(T[] values, int vertexCount, string name, string bindingName,
            string rendererPath, out T[] result, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            result = values.Length == 0 ? null : values;
            if (values.Length != 0 && values.Length != vertexCount)
                return Fail("OutfitTopologyAttributeInvalid", "An Outfit " + name + " channel is incomplete and cannot be remapped.", bindingName, rendererPath, -1, out diagnostic,
                    "attribute=" + name + "; valueCount=" + values.Length.ToString(CultureInfo.InvariantCulture) + "; vertexCount=" + vertexCount.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        private static List<BlendShapeData> CaptureBlendShapes(Mesh mesh)
        {
            var result = new List<BlendShapeData>();
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                var frames = new List<BlendShapeFrame>();
                for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    Vector3[] vertices = new Vector3[mesh.vertexCount];
                    Vector3[] normals = new Vector3[mesh.vertexCount];
                    Vector3[] tangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                    frames.Add(new BlendShapeFrame(mesh.GetBlendShapeFrameWeight(shape, frame), vertices, normals, tangents));
                }
                result.Add(new BlendShapeData(mesh.GetBlendShapeName(shape), frames));
            }
            return result;
        }

        private static T[] Remap<T>(T[] values, int[] permutation)
        {
            var result = new T[permutation.Length];
            for (int baseVertex = 0; baseVertex < permutation.Length; baseVertex++) result[baseVertex] = values[permutation[baseVertex]];
            return result;
        }

        private static bool HasFinitePositions(Vector3[] positions)
            => positions.All(value => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z));

        private static double ComputeBoundsDiagonal(Vector3[] positions)
        {
            if (positions.Length == 0) return 0.0;
            Vector3 min = positions[0];
            Vector3 max = positions[0];
            for (int index = 1; index < positions.Length; index++)
            {
                min = Vector3.Min(min, positions[index]);
                max = Vector3.Max(max, positions[index]);
            }
            return Distance(min, max);
        }

        private static double Distance(Vector3 left, Vector3 right) => Math.Sqrt(DistanceSquared(left, right));
        private static double DistanceSquared(Vector3 left, Vector3 right)
        {
            double x = left.x - right.x;
            double y = left.y - right.y;
            double z = left.z - right.z;
            return x * x + y * y + z * z;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static int Intern(Dictionary<VertexKey, int> palette, VertexKey key)
        {
            if (palette.TryGetValue(key, out int value)) return value;
            value = palette.Count;
            palette.Add(key, value);
            return value;
        }

        private static int Intern(Dictionary<string, int> palette, string key)
        {
            if (palette.TryGetValue(key, out int value)) return value;
            value = palette.Count;
            palette.Add(key, value);
            return value;
        }

        private static void CountColors(int[] colors, Dictionary<int, int> counts)
        {
            foreach (int color in colors) counts[color] = counts.TryGetValue(color, out int count) ? count + 1 : 1;
        }

        private static bool HaveSameColorMultiset(int[] baseColors, int[] targetColors)
        {
            var baseCounts = new Dictionary<int, int>();
            var targetCounts = new Dictionary<int, int>();
            CountColors(baseColors, baseCounts);
            CountColors(targetColors, targetCounts);
            return baseCounts.Count == targetCounts.Count
                && baseCounts.All(pair => targetCounts.TryGetValue(pair.Key, out int count) && count == pair.Value);
        }

        private static int CountClasses(int[] colors) => colors.Distinct().Count();

        private static void AddCount<TKey>(Dictionary<TKey, int> counts, TKey key)
            => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

        private static bool Fail(string code, string message, string bindingName, string rendererPath, int submesh,
            out StackMachineDiagnostic diagnostic, string extra = null)
        {
            string renderer = string.IsNullOrEmpty(rendererPath) ? "<root>" : rendererPath;
            string detail = "renderer=" + renderer + "; submesh=" + (submesh < 0 ? "all" : submesh.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(extra)) detail += "; " + extra;
            diagnostic = StackMachineDiagnostic.CreateDomain("outfit-topology", code, message,
                bindingName: bindingName, detail: detail);
            return false;
        }

        private sealed class Component
        {
            internal readonly int[] Vertices;
            internal readonly Vector3 Centroid;
            internal Component(int[] vertices, Vector3 centroid) { Vertices = vertices; Centroid = centroid; }
        }

        private sealed class FieldEntry
        {
            internal readonly int BaseComponent;
            internal readonly Vector3 Centroid;
            internal readonly Vector3 Delta;
            internal FieldEntry(int baseComponent, Vector3 centroid, Vector3 delta) { BaseComponent = baseComponent; Centroid = centroid; Delta = delta; }
        }

        private readonly struct NearbyField
        {
            internal readonly int BaseComponent;
            internal readonly double DistanceSquared;
            internal readonly Vector3 Delta;
            internal NearbyField(int baseComponent, double distanceSquared, Vector3 delta) { BaseComponent = baseComponent; DistanceSquared = distanceSquared; Delta = delta; }
        }

        private sealed class NearbyFieldComparer : IComparer<NearbyField>
        {
            internal static readonly NearbyFieldComparer Instance = new NearbyFieldComparer();
            public int Compare(NearbyField left, NearbyField right)
            {
                int result = left.DistanceSquared.CompareTo(right.DistanceSquared);
                return result != 0 ? result : left.BaseComponent.CompareTo(right.BaseComponent);
            }
        }

        private readonly struct CandidateCost
        {
            internal readonly int ComponentIndex;
            internal readonly double Cost;
            internal CandidateCost(int componentIndex, double cost) { ComponentIndex = componentIndex; Cost = cost; }
        }

        private sealed class CandidateCostComparer : IComparer<CandidateCost>
        {
            internal static readonly CandidateCostComparer Instance = new CandidateCostComparer();
            public int Compare(CandidateCost left, CandidateCost right)
            {
                int result = left.Cost.CompareTo(right.Cost);
                return result != 0 ? result : left.ComponentIndex.CompareTo(right.ComponentIndex);
            }
        }

        private readonly struct ScoredComponent
        {
            internal readonly int BaseComponent;
            internal readonly int TargetComponent;
            internal readonly double Cost;
            internal readonly double Ratio;
            internal ScoredComponent(int baseComponent, int targetComponent, double cost, double ratio) { BaseComponent = baseComponent; TargetComponent = targetComponent; Cost = cost; Ratio = ratio; }
        }

        private sealed class ScoredComponentComparer : IComparer<ScoredComponent>
        {
            internal static readonly ScoredComponentComparer Instance = new ScoredComponentComparer();
            public int Compare(ScoredComponent left, ScoredComponent right)
            {
                int result = right.Ratio.CompareTo(left.Ratio);
                return result != 0 ? result : left.BaseComponent.CompareTo(right.BaseComponent);
            }
        }

        private sealed class BoneWeightComparer : IComparer<BoneWeight1>
        {
            internal static readonly BoneWeightComparer Instance = new BoneWeightComparer();
            public int Compare(BoneWeight1 left, BoneWeight1 right)
            {
                int result = right.weight.CompareTo(left.weight);
                return result != 0 ? result : left.boneIndex.CompareTo(right.boneIndex);
            }
        }

        private readonly struct VertexKey : IEquatable<VertexKey>
        {
            private readonly string Membership;
            private readonly int X;
            private readonly int Y;
            private readonly int Z;
            private readonly int W;
            internal VertexKey(string membership, Vector4 uv) { Membership = membership; X = BitConverter.SingleToInt32Bits(uv.x); Y = BitConverter.SingleToInt32Bits(uv.y); Z = BitConverter.SingleToInt32Bits(uv.z); W = BitConverter.SingleToInt32Bits(uv.w); }
            public bool Equals(VertexKey other) => string.Equals(Membership, other.Membership, StringComparison.Ordinal) && X == other.X && Y == other.Y && Z == other.Z && W == other.W;
            public override bool Equals(object obj) => obj is VertexKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Membership, X, Y, Z, W);
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int A;
            private readonly int B;
            private readonly int C;
            private TriangleKey(int a, int b, int c) { A = a; B = b; C = c; }
            internal static TriangleKey Normalize(int a, int b, int c)
            {
                if (a <= b && a <= c) return new TriangleKey(a, b, c);
                if (b <= a && b <= c) return new TriangleKey(b, c, a);
                return new TriangleKey(c, a, b);
            }
            public bool Equals(TriangleKey other) => A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B, C);
        }

        private sealed class BlendShapeData
        {
            internal readonly string Name;
            internal readonly List<BlendShapeFrame> Frames;
            internal BlendShapeData(string name, List<BlendShapeFrame> frames) { Name = name; Frames = frames; }
        }

        private sealed class BlendShapeFrame
        {
            internal readonly float Weight;
            internal readonly Vector3[] Vertices;
            internal readonly Vector3[] Normals;
            internal readonly Vector3[] Tangents;
            internal BlendShapeFrame(float weight, Vector3[] vertices, Vector3[] normals, Vector3[] tangents) { Weight = weight; Vertices = vertices; Normals = normals; Tangents = tangents; }
        }
    }
}
#endif
