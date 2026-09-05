// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class Spec3McmTestDataValidationTests
    {
        private readonly List<Mesh> createdMeshes = new List<Mesh>();

        [Test]
        public void M01_MissingOneTargetMcmMeshKeepsBaseAndOtherTargetMcm()
        {
            Mesh mesh = LoadMesh("Invalid_MissingMcmOneTarget");

            AssertHasBlendShapes(mesh, "VRM_happy", "MCM_BasicGirl_happy");
            AssertMissingBlendShapes(mesh, "MCM_BasicMale_happy");
        }

        [Test]
        public void M02_MissingAllTargetMcmMeshKeepsBaseExpression()
        {
            Mesh mesh = LoadMesh("Invalid_MissingMcmAllTargets");

            AssertHasBlendShapes(mesh, "VRM_happy");
            AssertMissingBlendShapes(mesh, "MCM_BasicGirl_happy", "MCM_BasicMale_happy");
        }

        [Test]
        public void M03_MissingBaseExpressionMeshKeepsCorrespondingMcms()
        {
            Mesh mesh = LoadMesh("Invalid_MissingBaseExpression");

            AssertMissingBlendShapes(mesh, "VRM_happy");
            AssertHasBlendShapes(mesh, "MCM_BasicGirl_happy", "MCM_BasicMale_happy");
        }

        [Test]
        public void M04_UnknownMcmBlendNameMeshContainsTheIntendedUnknownName()
        {
            Mesh mesh = LoadMesh("Invalid_UnknownMcmBlendName");

            AssertHasBlendShapes(mesh, "VRM_happy", "MCM_BasicGirl_happy", "MCM_BasicMale_happy", "MCM_Unknown_happy");
        }

        [Test]
        public void M05_MalformedMcmNameMeshContainsAllMalformedNames()
        {
            Mesh mesh = LoadMesh("Invalid_MalformedMcmName");

            AssertHasBlendShapes(mesh, "MCM_", "MCM_BasicMale", "MCM__happy");
        }

        [Test]
        public void M07_StandaloneMeshFiltersMcmAndInferredFbmNames()
        {
            GameObject instance = CreateStandaloneMcmOnlyFigure();
            try
            {
                Assert.That(ContainsDynamicBoneBlender(instance), Is.False, "M07 must not contain DynamicBoneBlender.");

                SkinnedMeshRenderer renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(renderer, Is.Not.Null, "M07 prefab has no SkinnedMeshRenderer.");
                AssertHasBlendShapes(renderer.sharedMesh, "VRM_happy", "MCM_BasicGirl_happy");

                Type proxyType = RuntimeType("UniversalExpressionProxy");
                Component proxy = instance.AddComponent(proxyType);
                proxyType.GetMethod("RebuildExpressionList", BindingFlags.Instance | BindingFlags.Public).Invoke(proxy, null);

                HashSet<string> expressionNames = new HashSet<string>();
                IList expressions = (IList)proxyType.GetProperty("Expressions", BindingFlags.Instance | BindingFlags.Public).GetValue(proxy);
                for (int i = 0; i < expressions.Count; i++)
                {
                    object entry = expressions[i];
                    if (entry != null)
                    {
                        FieldInfo blendShapeNameField = entry.GetType().GetField("blendShapeName", BindingFlags.Instance | BindingFlags.Public);
                        Assert.That(blendShapeNameField, Is.Not.Null, "UniversalExpressionEntry.blendShapeName was not found.");
                        expressionNames.Add((string)blendShapeNameField.GetValue(entry));
                    }
                }

                Assert.That(expressionNames, Does.Contain("VRM_happy"));
                Assert.That(expressionNames, Does.Not.Contain("MCM_BasicGirl_happy"));
                Assert.That(expressionNames, Does.Not.Contain("BasicGirl"));
            }
            finally
            {
                Mesh mesh = instance.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdMeshes.Count; i++)
            {
                if (createdMeshes[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdMeshes[i]);
                }
            }

            createdMeshes.Clear();
        }

        private Mesh LoadMesh(string assetName)
        {
            switch (assetName)
            {
                case "Invalid_MissingMcmOneTarget":
                    return Track(CreateMesh(assetName, "VRM_happy", "MCM_BasicGirl_happy"));
                case "Invalid_MissingMcmAllTargets":
                    return Track(CreateMesh(assetName, "VRM_happy"));
                case "Invalid_MissingBaseExpression":
                    return Track(CreateMesh(assetName, "MCM_BasicGirl_happy", "MCM_BasicMale_happy"));
                case "Invalid_UnknownMcmBlendName":
                    return Track(CreateMesh(assetName, "VRM_happy", "MCM_BasicGirl_happy", "MCM_BasicMale_happy", "MCM_Unknown_happy"));
                case "Invalid_MalformedMcmName":
                    return Track(CreateMesh(assetName, "MCM_", "MCM_BasicMale", "MCM__happy"));
                default:
                    Assert.Fail($"No in-memory fixture is defined for '{assetName}'.");
                    return null;
            }
        }

        private Mesh Track(Mesh mesh)
        {
            createdMeshes.Add(mesh);
            return mesh;
        }

        private static GameObject CreateStandaloneMcmOnlyFigure()
        {
            GameObject root = new GameObject("Spec3_M07_StandaloneMcmOnly");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMesh("Spec3_M07_Mesh", "VRM_happy", "MCM_BasicGirl_happy");
            return root;
        }

        private static Mesh CreateMesh(string name, params string[] blendShapeNames)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            Vector3[] zeroDeltas = new Vector3[mesh.vertexCount];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, zeroDeltas, zeroDeltas, zeroDeltas);
            }

            return mesh;
        }

        private static Type RuntimeType(string typeName)
        {
            Type type = Type.GetType("zgock.ShapeSync." + typeName + ", zgock.ShapeSync.Runtime")
                ?? Type.GetType(typeName + ", zgock.ShapeSync.Runtime");
            Assert.That(type, Is.Not.Null, $"Runtime type '{typeName}' was not found.");
            return type;
        }

        private static bool ContainsDynamicBoneBlender(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name == "DynamicBoneBlender")
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertHasBlendShapes(Mesh mesh, params string[] blendShapeNames)
        {
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                Assert.That(
                    mesh.GetBlendShapeIndex(blendShapeNames[i]),
                    Is.GreaterThanOrEqualTo(0),
                    $"Expected BlendShape '{blendShapeNames[i]}' was not found on '{mesh.name}'.");
            }
        }

        private static void AssertMissingBlendShapes(Mesh mesh, params string[] blendShapeNames)
        {
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                Assert.That(
                    mesh.GetBlendShapeIndex(blendShapeNames[i]),
                    Is.EqualTo(-1),
                    $"BlendShape '{blendShapeNames[i]}' must be absent from '{mesh.name}'.");
            }
        }
    }

}
