// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class ProfileControlledMorphProjectionSpec7Tests
    {
        [Test]
        public void SP01_ProjectsEveryEnabledVertexToNearestTargetSurface()
        {
            Mesh source = CreateRaisedSourceMesh();
            Mesh target = CreateTargetPlane();
            GameObject sourceRoot = new GameObject("Projection Source");
            GameObject targetRoot = new GameObject("Projection Target");
            try
            {
                ProfileControlledMorphProjection.Settings settings = new ProfileControlledMorphProjection.Settings(1f, -1f, null, Vector3.zero);

                Assert.That(ProfileControlledMorphProjection.TryBuild(source, sourceRoot.transform, target, targetRoot.transform, settings, out ProfileControlledMorphProjection.Result result, out string error), Is.True, error);
                Assert.That(result.surfaceProjectedVertexCount, Is.EqualTo(source.vertexCount));
                for (int i = 0; i < result.deltaVertices.Length; i++)
                {
                    AssertDelta(result.deltaVertices[i], new Vector3(0f, 0f, -0.4f));
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sourceRoot);
                Object.DestroyImmediate(targetRoot);
            }
        }

        [Test]
        public void SP02_ProjectionOffVerticesReceiveOnlyHipsTranslation()
        {
            Mesh source = CreateRaisedSourceMesh();
            Mesh target = CreateTargetPlane();
            GameObject sourceRoot = new GameObject("Projection Source");
            GameObject targetRoot = new GameObject("Projection Target");
            try
            {
                bool[] projectionVertices = { true, true, false };
                Vector3 hipsTranslation = new Vector3(0f, 0.05f, 0f);
                ProfileControlledMorphProjection.Settings settings = new ProfileControlledMorphProjection.Settings(1f, -1f, projectionVertices, hipsTranslation);

                Assert.That(ProfileControlledMorphProjection.TryBuild(source, sourceRoot.transform, target, targetRoot.transform, settings, out ProfileControlledMorphProjection.Result result, out string error), Is.True, error);
                Assert.That(result.surfaceProjectedVertexCount, Is.EqualTo(2));
                AssertDelta(result.deltaVertices[2], hipsTranslation);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sourceRoot);
                Object.DestroyImmediate(targetRoot);
            }
        }

        private static Mesh CreateRaisedSourceMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(0.2f, 0.2f, 0.4f),
                new Vector3(0.8f, 0.2f, 0.4f),
                new Vector3(0.2f, 0.8f, 0.4f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh CreateTargetPlane()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void AssertDelta(Vector3 actual, Vector3 expected)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.00001f));
        }
    }

}
