// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Reflection;
using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class NormalMapBakeWindowTests
    {
        [Test]
        public void TryBake_EncodesAreaWeightedGeometricNormalRelativeToReceiverBasis()
        {
            Mesh mesh = CreateRaisedQuadMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.True, error);
                try
                {
                    Color32 encoded = texture.GetPixel(64, 64);
                    Assert.That(encoded, Is.Not.EqualTo(new Color32(128, 128, 255, 255)));
                    Assert.That(encoded.a, Is.EqualTo(255));
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_RetainsFlatNormalBackgroundOutsideCoveredUv()
        {
            Mesh mesh = CreateSingleTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.True, error);
                try
                {
                    Assert.That(texture.GetPixel(127, 127), Is.EqualTo((Color)new Color32(128, 128, 255, 255)));
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_RejectsOverlappingUvCoverage()
        {
            Mesh mesh = CreateOverlappingTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.False);
                Assert.That(texture, Is.Null);
                Assert.That(error, Does.Contain("overlap"));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_AcceptsNonOverlappingUvTrianglesThatShareAnOutputTexel()
        {
            Mesh mesh = CreateNearByTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.True, error);
                Object.DestroyImmediate(texture);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_IgnoreUvValidation_AcceptsOverlappingUvCoverage()
        {
            Mesh mesh = CreateOverlappingTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, true, out Texture2D texture, out string error), Is.True, error);
                Object.DestroyImmediate(texture);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_ClampsBackFacingTangentSpaceNormalsToTheReceiverHemisphere()
        {
            Mesh mesh = CreateBackFacingTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.True, error);
                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Assert.That(pixels[i].b, Is.GreaterThanOrEqualTo(128), $"Pixel {i} encodes a back-facing tangent-space normal.");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_RejectsUnsupportedResolution()
        {
            Mesh mesh = CreateSingleTriangleMesh();
            try
            {
                Assert.That(TryBake(mesh, 0, 96, out Texture2D texture, out string error), Is.False);
                Assert.That(texture, Is.Null);
                Assert.That(error, Does.Contain("power of two"));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TryBake_IgnoresDegenerateGeometryTrianglesWhenOtherTrianglesAreValid()
        {
            Mesh mesh = CreateSingleTriangleMesh();
            mesh.triangles = new[] { 0, 1, 2, 0, 0, 1 };
            try
            {
                Assert.That(TryBake(mesh, 0, 128, out Texture2D texture, out string error), Is.True, error);
                Object.DestroyImmediate(texture);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        private static bool TryBake(Mesh mesh, int submesh, int resolution, out Texture2D texture, out string error)
        {
            MethodInfo method = FindTryBakeMethod(5);
            Assert.That(method, Is.Not.Null, "Normal map bake implementation must expose its internal testable entry point.");
            object[] arguments = { mesh, submesh, resolution, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            texture = arguments[3] as Texture2D;
            error = arguments[4] as string;
            return result;
        }

        private static bool TryBake(Mesh mesh, int submesh, int resolution, bool ignoreUvValidation, out Texture2D texture, out string error)
        {
            MethodInfo method = FindTryBakeMethod(6);
            Assert.That(method, Is.Not.Null, "Normal map bake implementation must expose its UV-validation entry point.");
            object[] arguments = { mesh, submesh, resolution, ignoreUvValidation, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            texture = arguments[4] as Texture2D;
            error = arguments[5] as string;
            return result;
        }

        private static MethodInfo FindTryBakeMethod(int parameterCount)
        {
            MethodInfo[] methods = typeof(NormalMapBakeWindow).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "TryBake" && methods[i].GetParameters().Length == parameterCount)
                {
                    return methods[i];
                }
            }

            return null;
        }

        private static Mesh CreateRaisedQuadMesh()
        {
            Mesh mesh = new Mesh { name = "Raised Quad" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0.5f), new Vector3(0f, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }

        private static Mesh CreateSingleTriangleMesh()
        {
            Mesh mesh = new Mesh { name = "Single Triangle" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f) };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        private static Mesh CreateBackFacingTriangleMesh()
        {
            Mesh mesh = CreateSingleTriangleMesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0f) };
            return mesh;
        }

        private static Mesh CreateOverlappingTriangleMesh()
        {
            Mesh mesh = new Mesh { name = "Overlapping UV Triangles" };
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.up,
                new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(0f, 1f, 1f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[]
            {
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            return mesh;
        }

        private static Mesh CreateNearByTriangleMesh()
        {
            Mesh mesh = CreateOverlappingTriangleMesh();
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.49f, 0f), new Vector2(0f, 0.49f),
                new Vector2(0.495f, 0f), new Vector2(0.995f, 0f), new Vector2(0.495f, 0.5f)
            };
            return mesh;
        }
    }
}
