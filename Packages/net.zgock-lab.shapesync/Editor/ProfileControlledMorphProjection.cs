// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Projects Profile Controlled Morph deformation data in editor tooling.</summary>
    public static class ProfileControlledMorphProjection
    {
        /// <summary>
        /// Immutable parameters controlling one surface projection operation.
        /// </summary>
        public readonly struct Settings
        {
            public readonly float maxDistance;
            public readonly float minNormalDot;
            public readonly bool[] projectionVertices;
            public readonly Vector3 fallbackWorldDelta;

            public Settings(float maxDistance, float minNormalDot, bool[] projectionVertices, Vector3 fallbackWorldDelta)
            {
                this.maxDistance = maxDistance;
                this.minNormalDot = minNormalDot;
                this.projectionVertices = projectionVertices;
                this.fallbackWorldDelta = fallbackWorldDelta;
            }
        }

        /// <summary>
        /// Immutable output metrics and vertex deltas from a surface projection operation.
        /// </summary>
        public readonly struct Result
        {
            public readonly Vector3[] deltaVertices;
            public readonly float maxDistance;
            public readonly float meanDistance;
            public readonly int targetTriangleCount;
            public readonly int surfaceProjectedVertexCount;

            public Result(Vector3[] deltaVertices, float maxDistance, float meanDistance, int targetTriangleCount, int surfaceProjectedVertexCount)
            {
                this.deltaVertices = deltaVertices;
                this.maxDistance = maxDistance;
                this.meanDistance = meanDistance;
                this.targetTriangleCount = targetTriangleCount;
                this.surfaceProjectedVertexCount = surfaceProjectedVertexCount;
            }
        }

        private readonly struct Triangle
        {
            public readonly Vector3 a;
            public readonly Vector3 b;
            public readonly Vector3 c;
            public readonly Vector3 normal;

            public Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                normal = cross.sqrMagnitude > Mathf.Epsilon ? cross.normalized : Vector3.zero;
            }
        }

        public static bool TryBuild(
            Mesh sourceMesh,
            Transform sourceTransform,
            Mesh targetMesh,
            Transform targetTransform,
            Settings settings,
            out Result result,
            out string error)
        {
            result = default;
            error = null;

            if (sourceMesh == null || targetMesh == null || sourceTransform == null || targetTransform == null)
            {
                error = "Source Mesh, Source Transform, Target Mesh, and Target Transform are required.";
                return false;
            }

            if (!sourceMesh.isReadable || !targetMesh.isReadable)
            {
                error = "Source Mesh and Target Mesh must be Read/Write Enabled.";
                return false;
            }

            if (sourceMesh.vertexCount == 0 || targetMesh.vertexCount == 0)
            {
                error = "Source Mesh and Target Mesh must contain vertices.";
                return false;
            }

            if (!IsFinitePositive(settings.maxDistance))
            {
                error = "Max Projection Distance must be finite and greater than zero.";
                return false;
            }

            if (!IsFinite(settings.minNormalDot) || settings.minNormalDot < -1f || settings.minNormalDot > 1f)
            {
                error = "Min Normal Dot must be finite and in the range -1 to 1.";
                return false;
            }

            if (!TryBuildTargetTriangles(targetMesh, targetTransform, out List<Triangle> triangles, out error))
            {
                return false;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            bool requireNormal = settings.minNormalDot > -1f && sourceNormals.Length == sourceVertices.Length;
            Vector3[] deltas = new Vector3[sourceVertices.Length];
            if (settings.projectionVertices != null && settings.projectionVertices.Length != sourceVertices.Length)
            {
                error = "Projection vertex mask length must match Source Mesh vertex count.";
                return false;
            }
            float maxDistance = 0f;
            float totalDistance = 0f;
            float maxDistanceSquared = settings.maxDistance * settings.maxDistance;

            int surfaceProjectedVertexCount = 0;
            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                if (settings.projectionVertices != null && !settings.projectionVertices[vertexIndex])
                {
                    deltas[vertexIndex] = sourceTransform.InverseTransformVector(settings.fallbackWorldDelta);
                    continue;
                }
                // Apply the Hips correction first. Projection then resolves only the
                // remaining surface difference, while the written delta still contains
                // both the global translation and that residual.
                Vector3 sourcePoint = sourceTransform.TransformPoint(sourceVertices[vertexIndex]) + settings.fallbackWorldDelta;
                Vector3 sourceNormal = requireNormal ? sourceTransform.TransformDirection(sourceNormals[vertexIndex]).normalized : Vector3.zero;
                float nearestDistanceSquared = float.PositiveInfinity;
                Vector3 nearestPoint = Vector3.zero;

                for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
                {
                    Triangle triangle = triangles[triangleIndex];
                    if (requireNormal && Vector3.Dot(sourceNormal, triangle.normal) < settings.minNormalDot)
                    {
                        continue;
                    }

                    Vector3 candidate = ClosestPointOnTriangle(sourcePoint, triangle.a, triangle.b, triangle.c);
                    float distanceSquared = (candidate - sourcePoint).sqrMagnitude;
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                        nearestPoint = candidate;
                    }
                }

                if (float.IsPositiveInfinity(nearestDistanceSquared))
                {
                    error = $"Source vertex {vertexIndex} has no target triangle satisfying Min Normal Dot.";
                    return false;
                }

                if (nearestDistanceSquared > maxDistanceSquared)
                {
                    error = $"Source vertex {vertexIndex} projects {Mathf.Sqrt(nearestDistanceSquared):F6}m, exceeding Max Projection Distance {settings.maxDistance:F6}m.";
                    return false;
                }

                float distance = Mathf.Sqrt(nearestDistanceSquared);
                maxDistance = Mathf.Max(maxDistance, distance);
                totalDistance += distance;
                deltas[vertexIndex] = sourceTransform.InverseTransformPoint(nearestPoint) - sourceVertices[vertexIndex];
                surfaceProjectedVertexCount++;
            }

            int projectedVertexCount = sourceVertices.Length;
            if (settings.projectionVertices != null)
            {
                projectedVertexCount = 0;
                for (int i = 0; i < settings.projectionVertices.Length; i++) if (settings.projectionVertices[i]) projectedVertexCount++;
            }
            result = new Result(deltas, maxDistance, projectedVertexCount > 0 ? totalDistance / projectedVertexCount : 0f, triangles.Count, surfaceProjectedVertexCount);
            return true;
        }

        private static bool TryBuildTargetTriangles(Mesh targetMesh, Transform targetTransform, out List<Triangle> triangles, out string error)
        {
            triangles = new List<Triangle>();
            error = null;
            Vector3[] vertices = targetMesh.vertices;
            for (int subMesh = 0; subMesh < targetMesh.subMeshCount; subMesh++)
            {
                if (targetMesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    error = $"Target Mesh SubMesh {subMesh} must use triangle topology.";
                    return false;
                }

                int[] indices = targetMesh.GetTriangles(subMesh);
                for (int index = 0; index < indices.Length; index += 3)
                {
                    Vector3 a = targetTransform.TransformPoint(vertices[indices[index]]);
                    Vector3 b = targetTransform.TransformPoint(vertices[indices[index + 1]]);
                    Vector3 c = targetTransform.TransformPoint(vertices[indices[index + 2]]);
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude > Mathf.Epsilon)
                    {
                        triangles.Add(new Triangle(a, b, c));
                    }
                }
            }

            if (triangles.Count == 0)
            {
                error = "Target Mesh does not contain non-degenerate triangles.";
                return false;
            }

            return true;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edgeFromA = b - a;
            Vector3 edgeFromAtoC = c - a;
            Vector3 pointFromA = point - a;
            float edgeFromASquared = Vector3.Dot(edgeFromA, edgeFromA);
            float edgeFromAtoCSquared = Vector3.Dot(edgeFromAtoC, edgeFromAtoC);
            float edgePairDot = Vector3.Dot(edgeFromA, edgeFromAtoC);
            float planeCoordinateDeterminant = edgeFromASquared * edgeFromAtoCSquared - edgePairDot * edgePairDot;

            // First test the orthogonal projection against the triangle's barycentric domain.
            // A non-positive determinant means that the triangle is collinear or coincident;
            // those cases are handled by the segment candidates below.
            if (planeCoordinateDeterminant > Mathf.Epsilon)
            {
                float pointEdgeDot = Vector3.Dot(pointFromA, edgeFromA);
                float pointOtherEdgeDot = Vector3.Dot(pointFromA, edgeFromAtoC);
                float barycentricB = (pointEdgeDot * edgeFromAtoCSquared - pointOtherEdgeDot * edgePairDot) / planeCoordinateDeterminant;
                float barycentricC = (pointOtherEdgeDot * edgeFromASquared - pointEdgeDot * edgePairDot) / planeCoordinateDeterminant;
                float barycentricA = 1f - barycentricB - barycentricC;
                if (barycentricA >= 0f && barycentricB >= 0f && barycentricC >= 0f)
                {
                    return a + edgeFromA * barycentricB + edgeFromAtoC * barycentricC;
                }
            }

            // If the face projection is outside the barycentric domain, or the triangle is
            // degenerate, the closest point lies on one of the three closed segments.
            Vector3 bestPoint = a;
            float bestDistanceSquared = float.PositiveInfinity;

            float parameterOnAB = edgeFromASquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(pointFromA, edgeFromA) / edgeFromASquared)
                : 0f;
            Vector3 candidate = a + edgeFromA * parameterOnAB;
            float candidateDistanceSquared = (candidate - point).sqrMagnitude;
            if (candidateDistanceSquared < bestDistanceSquared)
            {
                bestPoint = candidate;
                bestDistanceSquared = candidateDistanceSquared;
            }

            Vector3 edgeFromB = c - b;
            float edgeFromBSquared = Vector3.Dot(edgeFromB, edgeFromB);
            float parameterOnBC = edgeFromBSquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(point - b, edgeFromB) / edgeFromBSquared)
                : 0f;
            candidate = b + edgeFromB * parameterOnBC;
            candidateDistanceSquared = (candidate - point).sqrMagnitude;
            if (candidateDistanceSquared < bestDistanceSquared)
            {
                bestPoint = candidate;
                bestDistanceSquared = candidateDistanceSquared;
            }

            Vector3 edgeFromC = a - c;
            float edgeFromCSquared = Vector3.Dot(edgeFromC, edgeFromC);
            float parameterOnCA = edgeFromCSquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(point - c, edgeFromC) / edgeFromCSquared)
                : 0f;
            candidate = c + edgeFromC * parameterOnCA;
            candidateDistanceSquared = (candidate - point).sqrMagnitude;
            if (candidateDistanceSquared < bestDistanceSquared)
            {
                bestPoint = candidate;
            }

            return bestPoint;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }
    }
}
