// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Bakes an editor-only tangent-space normal-map test texture from one submesh's area-weighted geometric normals.
    /// </summary>
    public sealed class NormalMapBakeWindow : EditorWindow
    {
        private static readonly int[] SupportedResolutions = { 128, 256, 512, 1024, 2048, 4096 };

        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private int selectedSubmesh;
        [SerializeField] private int resolution = 1024;
        [SerializeField] private bool ignoreUvValidation;

        /// <summary>Draws the Mesh Utility pane that creates a normal-map test asset without modifying its source mesh.</summary>
        public void DrawNormalMapBakeContent()
        {
            EditorGUILayout.LabelField("Normal Map Bake", EditorStyles.boldLabel);
            sourceRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "SkinnedMeshRenderer", sourceRenderer, typeof(SkinnedMeshRenderer), true);
            EditorGUILayout.HelpBox(
                "Creates test data only. The selected submesh's triangle-area-weighted geometric normals are encoded relative to its existing vertex normal/tangent basis and rasterized through UV0. UV0 triangles outside 0..1, degenerate UV triangles, or overlapping UV interiors are rejected unless Ignore UV Validation is enabled. The source mesh and scene are not modified.",
                MessageType.Info);

            Mesh mesh = sourceRenderer != null ? sourceRenderer.sharedMesh : null;
            int submeshCount = mesh != null ? mesh.subMeshCount : 0;
            if (submeshCount > 0)
            {
                selectedSubmesh = Mathf.Clamp(selectedSubmesh, 0, submeshCount - 1);
                Material[] materials = sourceRenderer.sharedMaterials;
                string[] labels = new string[submeshCount];
                for (int i = 0; i < labels.Length; i++)
                {
                    Material material = i < materials.Length ? materials[i] : null;
                    labels[i] = $"Submesh {i}: {(material != null ? material.name : "(No Material)")}";
                }
                selectedSubmesh = EditorGUILayout.Popup("Material Submesh", selectedSubmesh, labels);
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a SkinnedMeshRenderer with a shared Mesh.", MessageType.Warning);
            }

            int resolutionIndex = Array.IndexOf(SupportedResolutions, resolution);
            resolutionIndex = EditorGUILayout.Popup("Resolution", Mathf.Max(0, resolutionIndex), Array.ConvertAll(SupportedResolutions, value => value.ToString()));
            resolution = SupportedResolutions[resolutionIndex];
            ignoreUvValidation = EditorGUILayout.Toggle("Ignore UV Validation", ignoreUvValidation);

            using (new EditorGUI.DisabledScope(mesh == null || submeshCount == 0))
            {
                if (GUILayout.Button("Bake Tangent-Space Normal Map", GUILayout.Height(34f)))
                {
                    BakeAsset(mesh);
                }
            }
        }

        private void BakeAsset(Mesh mesh)
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save Generated Normal Map",
                $"{mesh.name}_Submesh{selectedSubmesh}_GeneratedNormal",
                "asset",
                "Choose the generated Texture2D asset path.");
            if (string.IsNullOrEmpty(assetPath)) return;

            if (!TryBake(mesh, selectedSubmesh, resolution, ignoreUvValidation, out Texture2D texture, out string error))
            {
                EditorUtility.DisplayDialog("Normal Map Bake Failed", error, "OK");
                return;
            }

            try
            {
                AssetDatabase.CreateAsset(texture, AssetDatabase.GenerateUniqueAssetPath(assetPath));
                AssetDatabase.SaveAssets();
                Selection.activeObject = texture;
                texture = null;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Normal Map Bake Failed", exception.Message, "OK");
            }
            finally
            {
                if (texture != null) DestroyImmediate(texture);
            }
        }

        internal static bool TryBake(Mesh mesh, int submesh, int textureResolution, out Texture2D texture, out string error)
        {
            return TryBake(mesh, submesh, textureResolution, false, out texture, out error);
        }

        internal static bool TryBake(Mesh mesh, int submesh, int textureResolution, bool ignoreUvValidation, out Texture2D texture, out string error)
        {
            texture = null;
            error = null;
            if (mesh == null)
            {
                error = "A source Mesh is required.";
                return false;
            }

            if (submesh < 0 || submesh >= mesh.subMeshCount)
            {
                error = $"Submesh {submesh} does not exist on Mesh '{mesh.name}'.";
                return false;
            }

            if (textureResolution < 128 || textureResolution > 4096 || !Mathf.IsPowerOfTwo(textureResolution))
            {
                error = "Resolution must be a power of two from 128 through 4096.";
                return false;
            }

            Vector3[] vertices;
            Vector3[] normals;
            Vector4[] tangents;
            Vector2[] uv;
            int[] triangles;
            try
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
                tangents = mesh.tangents;
                uv = mesh.uv;
                triangles = mesh.GetTriangles(submesh);
            }
            catch (Exception exception)
            {
                error = $"Mesh '{mesh.name}' must be readable: {exception.Message}";
                return false;
            }

            if (vertices.Length == 0 || normals.Length != vertices.Length || tangents.Length != vertices.Length || uv.Length != vertices.Length)
            {
                error = "The selected Mesh requires position, normal, tangent, and UV0 data for every vertex.";
                return false;
            }

            if (triangles.Length == 0 || triangles.Length % 3 != 0)
            {
                error = $"Submesh {submesh} requires triangle topology.";
                return false;
            }

            Vector3[] geometricNormals = new Vector3[vertices.Length];
            bool[] rasterizableTriangles = new bool[triangles.Length / 3];
            int rasterizableTriangleCount = 0;
            for (int triangleOffset = 0; triangleOffset < triangles.Length; triangleOffset += 3)
            {
                int a = triangles[triangleOffset];
                int b = triangles[triangleOffset + 1];
                int c = triangles[triangleOffset + 2];
                if (!IsValidIndex(a, vertices.Length) || !IsValidIndex(b, vertices.Length) || !IsValidIndex(c, vertices.Length))
                {
                    error = $"Submesh {submesh} contains an invalid vertex index.";
                    return false;
                }

                Vector3 areaWeightedNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (areaWeightedNormal.sqrMagnitude <= 0.000000000001f)
                {
                    continue;
                }

                rasterizableTriangles[triangleOffset / 3] = true;
                rasterizableTriangleCount++;
                geometricNormals[a] += areaWeightedNormal;
                geometricNormals[b] += areaWeightedNormal;
                geometricNormals[c] += areaWeightedNormal;
            }

            if (rasterizableTriangleCount == 0)
            {
                error = $"Submesh {submesh} contains no non-degenerate geometry triangles.";
                return false;
            }

            for (int i = 0; i < geometricNormals.Length; i++)
            {
                if (geometricNormals[i].sqrMagnitude <= 0.000000000001f) continue;
                geometricNormals[i].Normalize();
            }

            Color32[] pixels = new Color32[textureResolution * textureResolution];
            Color32 flat = new Color32(128, 128, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = flat;
            int[] owners = new int[pixels.Length];
            for (int i = 0; i < owners.Length; i++) owners[i] = -1;

            for (int triangleOffset = 0; triangleOffset < triangles.Length; triangleOffset += 3)
            {
                int triangleIndex = triangleOffset / 3;
                if (!rasterizableTriangles[triangleIndex]) continue;
                if (!TryRasterizeTriangle(
                    triangles[triangleOffset], triangles[triangleOffset + 1], triangles[triangleOffset + 2],
                    triangleIndex, triangles, textureResolution, ignoreUvValidation, uv, normals, tangents, geometricNormals, owners, pixels, out error))
                {
                    return false;
                }
            }

            texture = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false, true)
            {
                name = $"{mesh.name}_Submesh{submesh}_GeneratedNormal",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return true;
        }

        private static bool TryRasterizeTriangle(
            int firstIndex,
            int secondIndex,
            int thirdIndex,
            int triangleIndex,
            int[] triangles,
            int textureResolution,
            bool ignoreUvValidation,
            Vector2[] uv,
            Vector3[] receiverNormals,
            Vector4[] receiverTangents,
            Vector3[] geometricNormals,
            int[] owners,
            Color32[] pixels,
            out string error)
        {
            error = null;
            RasterVertex first = new RasterVertex(firstIndex, uv[firstIndex]);
            RasterVertex second = new RasterVertex(secondIndex, uv[secondIndex]);
            RasterVertex third = new RasterVertex(thirdIndex, uv[thirdIndex]);
            if (!ignoreUvValidation && (!IsUnitUv(first.Uv) || !IsUnitUv(second.Uv) || !IsUnitUv(third.Uv)))
            {
                error = $"Submesh UV0 triangle {triangleIndex} is outside the 0..1 bake domain.";
                return false;
            }

            float signedArea = Edge(first.Uv, second.Uv, third.Uv);
            if (Mathf.Abs(signedArea) <= 0.00000001f)
            {
                if (ignoreUvValidation) return true;
                error = $"Submesh UV0 triangle {triangleIndex} is degenerate.";
                return false;
            }

            if (signedArea < 0f)
            {
                RasterVertex swap = second;
                second = third;
                third = swap;
                signedArea = -signedArea;
            }

            int minX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(first.Uv.x, second.Uv.x, third.Uv.x) * textureResolution - 0.5f), 0, textureResolution - 1);
            int maxX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(first.Uv.x, second.Uv.x, third.Uv.x) * textureResolution - 0.5f), 0, textureResolution - 1);
            int minY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(first.Uv.y, second.Uv.y, third.Uv.y) * textureResolution - 0.5f), 0, textureResolution - 1);
            int maxY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(first.Uv.y, second.Uv.y, third.Uv.y) * textureResolution - 0.5f), 0, textureResolution - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2((x + 0.5f) / textureResolution, (y + 0.5f) / textureResolution);
                    float weightFirst = Edge(second.Uv, third.Uv, point);
                    float weightSecond = Edge(third.Uv, first.Uv, point);
                    float weightThird = Edge(first.Uv, second.Uv, point);
                    if (!IsInsideTopLeft(weightFirst, second.Uv, third.Uv)
                        || !IsInsideTopLeft(weightSecond, third.Uv, first.Uv)
                        || !IsInsideTopLeft(weightThird, first.Uv, second.Uv))
                    {
                        continue;
                    }

                    int pixelIndex = y * textureResolution + x;
                    if (owners[pixelIndex] >= 0)
                    {
                        if (!ignoreUvValidation && HasInteriorUvOverlap(owners[pixelIndex], triangleIndex, triangles, uv))
                        {
                            error = $"Submesh UV0 triangles {owners[pixelIndex]} and {triangleIndex} overlap in UV0.";
                            return false;
                        }

                        // Neighboring or near-by islands can address the same output texel without sharing UV area.
                        // Keep the first write deterministically; the triangles themselves remain non-overlapping.
                        continue;
                    }

                    owners[pixelIndex] = triangleIndex;
                    weightFirst /= signedArea;
                    weightSecond /= signedArea;
                    weightThird /= signedArea;
                    pixels[pixelIndex] = EncodeTangentSpaceNormal(
                        first.Index, second.Index, third.Index,
                        weightFirst, weightSecond, weightThird,
                        receiverNormals, receiverTangents, geometricNormals);
                }
            }

            return true;
        }

        private static bool HasInteriorUvOverlap(int firstTriangle, int secondTriangle, int[] triangles, Vector2[] uv)
        {
            Vector2 firstA = uv[triangles[firstTriangle * 3]];
            Vector2 firstB = uv[triangles[firstTriangle * 3 + 1]];
            Vector2 firstC = uv[triangles[firstTriangle * 3 + 2]];
            Vector2 secondA = uv[triangles[secondTriangle * 3]];
            Vector2 secondB = uv[triangles[secondTriangle * 3 + 1]];
            Vector2 secondC = uv[triangles[secondTriangle * 3 + 2]];

            return IsStrictlyInside(firstA, secondA, secondB, secondC)
                || IsStrictlyInside(firstB, secondA, secondB, secondC)
                || IsStrictlyInside(firstC, secondA, secondB, secondC)
                || IsStrictlyInside(secondA, firstA, firstB, firstC)
                || IsStrictlyInside(secondB, firstA, firstB, firstC)
                || IsStrictlyInside(secondC, firstA, firstB, firstC)
                || IsStrictlyInside((firstA + firstB + firstC) / 3f, secondA, secondB, secondC)
                || IsStrictlyInside((secondA + secondB + secondC) / 3f, firstA, firstB, firstC)
                || HasProperIntersection(firstA, firstB, secondA, secondB)
                || HasProperIntersection(firstA, firstB, secondB, secondC)
                || HasProperIntersection(firstA, firstB, secondC, secondA)
                || HasProperIntersection(firstB, firstC, secondA, secondB)
                || HasProperIntersection(firstB, firstC, secondB, secondC)
                || HasProperIntersection(firstB, firstC, secondC, secondA)
                || HasProperIntersection(firstC, firstA, secondA, secondB)
                || HasProperIntersection(firstC, firstA, secondB, secondC)
                || HasProperIntersection(firstC, firstA, secondC, secondA);
        }

        private static bool IsStrictlyInside(Vector2 point, Vector2 first, Vector2 second, Vector2 third)
        {
            const float epsilon = 0.0000001f;
            float firstEdge = Edge(first, second, point);
            float secondEdge = Edge(second, third, point);
            float thirdEdge = Edge(third, first, point);
            return (firstEdge > epsilon && secondEdge > epsilon && thirdEdge > epsilon)
                || (firstEdge < -epsilon && secondEdge < -epsilon && thirdEdge < -epsilon);
        }

        private static bool HasProperIntersection(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
        {
            const float epsilon = 0.0000001f;
            float firstStartSide = Edge(secondStart, secondEnd, firstStart);
            float firstEndSide = Edge(secondStart, secondEnd, firstEnd);
            float secondStartSide = Edge(firstStart, firstEnd, secondStart);
            float secondEndSide = Edge(firstStart, firstEnd, secondEnd);
            return ((firstStartSide > epsilon && firstEndSide < -epsilon) || (firstStartSide < -epsilon && firstEndSide > epsilon))
                && ((secondStartSide > epsilon && secondEndSide < -epsilon) || (secondStartSide < -epsilon && secondEndSide > epsilon));
        }

        private static Color32 EncodeTangentSpaceNormal(
            int firstIndex, int secondIndex, int thirdIndex,
            float firstWeight, float secondWeight, float thirdWeight,
            Vector3[] receiverNormals, Vector4[] receiverTangents, Vector3[] geometricNormals)
        {
            Vector3 sourceNormal = (geometricNormals[firstIndex] * firstWeight + geometricNormals[secondIndex] * secondWeight + geometricNormals[thirdIndex] * thirdWeight).normalized;
            Vector3 receiverNormal = (receiverNormals[firstIndex] * firstWeight + receiverNormals[secondIndex] * secondWeight + receiverNormals[thirdIndex] * thirdWeight).normalized;
            Vector4 interpolatedTangent = receiverTangents[firstIndex] * firstWeight + receiverTangents[secondIndex] * secondWeight + receiverTangents[thirdIndex] * thirdWeight;
            Vector3 tangent = new Vector3(interpolatedTangent.x, interpolatedTangent.y, interpolatedTangent.z).normalized;
            if (sourceNormal.sqrMagnitude <= 0.000000000001f || receiverNormal.sqrMagnitude <= 0.000000000001f || tangent.sqrMagnitude <= 0.000000000001f)
            {
                return new Color32(128, 128, 255, 255);
            }

            Vector3 bitangent = Vector3.Cross(receiverNormal, tangent).normalized * (interpolatedTangent.w < 0f ? -1f : 1f);
            Vector3 tangentSpace = new Vector3(
                Vector3.Dot(sourceNormal, tangent),
                Vector3.Dot(sourceNormal, bitangent),
                Vector3.Dot(sourceNormal, receiverNormal)).normalized;
            if (tangentSpace.z <= 0f)
            {
                // Tangent-space normal maps represent the receiver-facing hemisphere only.
                // Source/receiver discontinuities can otherwise emit an invalid back-facing texel.
                tangentSpace.z = 0.0001f;
                tangentSpace.Normalize();
            }
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt((tangentSpace.x * 0.5f + 0.5f) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((tangentSpace.y * 0.5f + 0.5f) * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt((tangentSpace.z * 0.5f + 0.5f) * 255f), 0, 255),
                255);
        }

        private static bool IsInsideTopLeft(float edge, Vector2 start, Vector2 end)
        {
            const float epsilon = 0.0000001f;
            if (edge > epsilon) return true;
            if (edge < -epsilon) return false;
            float deltaY = end.y - start.y;
            float deltaX = end.x - start.x;
            return deltaY > 0f || (Mathf.Abs(deltaY) <= epsilon && deltaX < 0f);
        }

        private static float Edge(Vector2 start, Vector2 end, Vector2 point)
        {
            return (end.x - start.x) * (point.y - start.y) - (end.y - start.y) * (point.x - start.x);
        }

        private static bool IsUnitUv(Vector2 value)
        {
            return value.x >= 0f && value.x <= 1f && value.y >= 0f && value.y <= 1f;
        }

        private static bool IsValidIndex(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private readonly struct RasterVertex
        {
            public RasterVertex(int index, Vector2 uv)
            {
                Index = index;
                Uv = uv;
            }

            public int Index { get; }
            public Vector2 Uv { get; }
        }
    }
}
