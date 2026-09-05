// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.PlayMode
{

    public sealed class Spec4NormalRuntimeTests
    {
        [UnityTest]
        public IEnumerator OutfitAttacher_AttachesFbmExtraBoneAndDetachesIt()
        {
            Fixture fixture = CreateFixture("BasicGirl", "BasicMale");
            Component outfit = CreateOutfit(
                "Hair",
                "hair-1",
                "Twin Tails",
                "Root/Head/Hair",
                new[] { "BasicGirl", "BasicMale" },
                fixture.assets);

            Assert.That(InvokeBool(fixture.attacher, "TryAttach", outfit), Is.True);
            yield return null;

            Assert.That(AttachedOutfits(fixture.attacher), Has.Count.EqualTo(1));
            Transform attachedRoot = fixture.figure.transform.Find("Root/Head/Hair");
            Assert.That(attachedRoot, Is.Not.Null);
            Assert.That(attachedRoot.parent, Is.EqualTo(fixture.figure.transform.Find("Root/Head")));

            Assert.That(InvokeBool(fixture.attacher, "Detach", "hair-1"), Is.True);
            yield return null;

            Assert.That(AttachedOutfits(fixture.attacher), Is.Empty);
            Assert.That(fixture.figure.transform.Find("Root/Head/Hair"), Is.Null);
            DestroyFixture(fixture, outfit.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_AllowsNonConflictingOutfitsAndIndependentDetach()
        {
            Fixture fixture = CreateFixture("BasicGirl", "BasicMale");
            Component skirt = CreateOutfit(
                "Skirt",
                "skirt-1",
                "Blue Skirt",
                "Root/Hips/Skirt",
                new[] { "BasicGirl" },
                fixture.assets);
            Component dress = CreateOutfit(
                "Dress",
                "dress-1",
                "School Uniform",
                "Root/Hips/Dress",
                new[] { "BasicGirl" },
                fixture.assets);

            Assert.That(InvokeBool(fixture.attacher, "TryAttach", skirt), Is.True);
            Assert.That(InvokeBool(fixture.attacher, "TryAttach", dress), Is.True);
            yield return null;

            Assert.That(AttachedOutfits(fixture.attacher), Has.Count.EqualTo(2));
            Assert.That(fixture.figure.transform.Find("Root/Hips/Skirt"), Is.Not.Null);
            Assert.That(fixture.figure.transform.Find("Root/Hips/Dress"), Is.Not.Null);

            Assert.That(InvokeBool(fixture.attacher, "Detach", "dress-1"), Is.True);
            yield return null;

            Assert.That(AttachedOutfits(fixture.attacher), Has.Count.EqualTo(1));
            Assert.That(fixture.figure.transform.Find("Root/Hips/Skirt"), Is.Not.Null);
            Assert.That(fixture.figure.transform.Find("Root/Hips/Dress"), Is.Null);

            Assert.That(InvokeBool(fixture.attacher, "Detach", "skirt-1"), Is.True);
            yield return null;

            DestroyFixture(fixture, skirt.gameObject, dress.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_RejectsOverlappingExtraBoneWithoutCreatingRuntimeInstance()
        {
            Fixture fixture = CreateFixture("BasicGirl");
            Component hair1 = CreateOutfit(
                "Hair1",
                "hair-1",
                "Hair 1",
                "Root/Head/Hair",
                new[] { "BasicGirl" },
                fixture.assets);
            Component hair2 = CreateOutfit(
                "Hair2",
                "hair-2",
                "Hair 2",
                "Root/Head/Hair",
                new[] { "BasicGirl" },
                fixture.assets);

            Assert.That(InvokeBool(fixture.attacher, "TryAttach", hair1), Is.True);
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("Extra Bone path 'Root/Head/Hair' is already owned by an attached Outfit"));
            Assert.That(InvokeBool(fixture.attacher, "TryAttach", hair2), Is.False);

            Assert.That(AttachedOutfits(fixture.attacher), Has.Count.EqualTo(1));
            Assert.That(GameObject.Find("Hair2 (ShapeSync Runtime)"), Is.Null);

            Assert.That(InvokeBool(fixture.attacher, "Detach", "hair-1"), Is.True);
            yield return null;

            Assert.That(InvokeBool(fixture.attacher, "TryAttach", hair2), Is.True);
            yield return null;

            Assert.That(InvokeBool(fixture.attacher, "Detach", "hair-2"), Is.True);
            yield return null;

            DestroyFixture(fixture, hair1.gameObject, hair2.gameObject);
        }

        [UnityTest]
        public IEnumerator OutfitAttacher_RetainsRuntimeRootForLogicalAttach()
        {
            Fixture fixture = CreateFixture("BasicGirl");
            Component shirt = CreateOutfit(
                "Shirt",
                "shirt-1",
                "Pink Shirt",
                null,
                Array.Empty<string>(),
                fixture.assets);

            Assert.That(InvokeBool(fixture.attacher, "TryAttach", shirt), Is.True);
            yield return null;

            IList attachedOutfits = AttachedOutfits(fixture.attacher);
            Assert.That(attachedOutfits, Has.Count.EqualTo(1));
            object attachedOutfit = attachedOutfits[0];
            GameObject runtimeInstance = (GameObject)GetPublicProperty(attachedOutfit, "RuntimeOutfitInstance");
            IList extraRoots = (IList)GetPublicProperty(attachedOutfit, "ExtraRoots");

            // The cloned Outfit Root remains the attachment ownership boundary, even for a
            // logical attach with no renderer.  It carries any Outfit-local runtime components.
            Assert.That(runtimeInstance, Is.Not.Null);
            Assert.That(runtimeInstance.transform.parent, Is.SameAs(fixture.figure.transform));
            Assert.That(extraRoots, Is.Empty);
            Assert.That(fixture.figure.transform.Find("Shirt (ShapeSync Runtime)"), Is.SameAs(runtimeInstance.transform));

            Assert.That(InvokeBool(fixture.attacher, "Detach", "shirt-1"), Is.True);
            yield return null;

            Assert.That(InvokeBool(fixture.attacher, "Detach", "shirt-1"), Is.False);
            DestroyFixture(fixture, shirt.gameObject);
        }

        internal static Fixture CreateFixture(params string[] targetNames)
        {
            GameObject figure = new GameObject("Spec4_Figure");
            AddTransformPath(figure.transform, "Root/Head");
            AddTransformPath(figure.transform, "Root/Hips");

            figure.AddComponent<Animator>();
            Component blender = figure.AddComponent(RuntimeType("DynamicBoneBlender"));
            Component attacher = figure.AddComponent(RuntimeType("OutfitAttacher"));
            GameObject meshObject = new GameObject("Figure_Mesh");
            meshObject.transform.SetParent(figure.transform, false);
            SkinnedMeshRenderer figureRenderer = meshObject.AddComponent<SkinnedMeshRenderer>();

            IList targets = CreateRuntimeList("DynamicBoneBlendTarget");
            for (int i = 0; i < targetNames.Length; i++)
            {
                object target = Activator.CreateInstance(RuntimeType("DynamicBoneBlendTarget"));
                SetPublicField(target, "blendName", targetNames[i]);
                SetPublicField(target, "enabled", true);
                SetPublicField(target, "weight", 0f);
                targets.Add(target);
            }

            Mesh figureMesh = CreateFigureMesh(targetNames);
            figureRenderer.sharedMesh = figureMesh;
            SetPrivateField(blender, "targets", targets);
            SetPrivateField(blender, "targetSkinnedMeshRenderer", figureRenderer);
            SetPrivateField(attacher, "dynamicBoneBlender", blender);
            SetPrivateField(attacher, "figureAnimator", figure.GetComponent<Animator>());
            Fixture fixture = new Fixture(figure, blender, attacher);
            fixture.assets.Add(figureMesh);
            return fixture;
        }

        private static Mesh CreateFigureMesh(string[] targetNames)
        {
            Mesh mesh = new Mesh { name = "Spec4_Figure_Mesh" };
            Vector3[] vertices = { Vector3.zero };
            mesh.vertices = vertices;
            mesh.normals = new[] { Vector3.up };
            for (int index = 0; index < targetNames.Length; index++)
            {
                if (!string.IsNullOrEmpty(targetNames[index])) mesh.AddBlendShapeFrame(targetNames[index], 100f, new Vector3[1], new Vector3[1], new Vector3[1]);
            }
            return mesh;
        }

        internal static Component CreateOutfit(
            string objectName,
            string registryId,
            string registryName,
            string extraBonePath,
            string[] fbmNames,
            List<UnityEngine.Object> assets)
        {
            GameObject outfitRoot = new GameObject(objectName);
            if (!string.IsNullOrEmpty(extraBonePath))
            {
                AddTransformPath(outfitRoot.transform, extraBonePath);
            }

            Component outfit = outfitRoot.AddComponent(RuntimeType("ShapeSyncOutfit"));
            ScriptableObject baseRegistry = CreateRegistry(extraBonePath, assets);
            IList fbmRegistries = CreateRuntimeList("ShapeSyncOutfitFbmExtraBoneRegistry");
            for (int i = 0; i < fbmNames.Length; i++)
            {
                object entry = Activator.CreateInstance(RuntimeType("ShapeSyncOutfitFbmExtraBoneRegistry"));
                SetPublicField(entry, "blendName", fbmNames[i]);
                SetPublicField(entry, "extraBoneRegistry", CreateRegistry(extraBonePath, assets));
                fbmRegistries.Add(entry);
            }

            SetPrivateField(outfit, "registryId", registryId);
            SetPrivateField(outfit, "registryName", registryName);
            SetPrivateField(outfit, "baseExtraBoneRegistry", baseRegistry);
            SetPrivateField(outfit, "fbmExtraBoneRegistries", fbmRegistries);
            return outfit;
        }

        internal static ScriptableObject CreateRegistry(string path, List<UnityEngine.Object> assets)
        {
            ScriptableObject registry = ScriptableObject.CreateInstance(RuntimeType("CharacterBoneRegistry"));
            IList poses = CreateRuntimeList("BonePoseData");
            if (!string.IsNullOrEmpty(path))
            {
                AddPose(registry, path);
            }

            if (string.IsNullOrEmpty(path))
            {
                SetPublicField(registry, "bonePoses", poses);
            }
            assets.Add(registry);
            return registry;
        }

        internal static void AddPose(ScriptableObject registry, string path, bool hasBindpose = false, int bindposeIndex = -1)
        {
            IList poses = (IList)GetPublicField(registry, "bonePoses");
            if (poses == null)
            {
                poses = CreateRuntimeList("BonePoseData");
                SetPublicField(registry, "bonePoses", poses);
            }

            object pose = Activator.CreateInstance(RuntimeType("BonePoseData"));
            SetPublicField(pose, "boneName", path);
            SetPublicField(pose, "localPosition", Vector3.zero);
            SetPublicField(pose, "localRotation", Quaternion.identity);
            SetPublicField(pose, "localScale", Vector3.one);
            SetPublicField(pose, "bindposeIndex", bindposeIndex);
            SetPublicField(pose, "hasBindpose", hasBindpose);
            poses.Add(pose);
        }

        internal static Transform AddTransformPath(Transform root, string path)
        {
            string[] segments = path.Split('/');
            Transform current = root;
            for (int i = 0; i < segments.Length; i++)
            {
                Transform child = current.Find(segments[i]);
                if (child == null)
                {
                    child = new GameObject(segments[i]).transform;
                    child.SetParent(current, false);
                }

                current = child;
            }

            return current;
        }

        internal static IList AttachedOutfits(Component attacher)
        {
            return (IList)GetPublicProperty(attacher, "AttachedOutfits");
        }

        internal static bool InvokeBool(Component component, string methodName, params object[] arguments)
        {
            MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, $"{component.GetType().Name}.{methodName} was not found.");
            return (bool)method.Invoke(component, arguments);
        }

        internal static void InvokePrivate(Component component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Private method '{methodName}' was not found on {component.GetType().Name}.");
            method.Invoke(component, null);
        }

        internal static Type RuntimeType(string typeName)
        {
            Type type = Type.GetType($"zgock.ShapeSync.{typeName}, zgock.ShapeSync.Runtime")
                ?? Type.GetType($"{typeName}, zgock.ShapeSync.Runtime");
            Assert.That(type, Is.Not.Null, $"Runtime type '{typeName}' was not found.");
            return type;
        }

        internal static IList CreateRuntimeList(string elementTypeName)
        {
            Type listType = typeof(List<>).MakeGenericType(RuntimeType(elementTypeName));
            return (IList)Activator.CreateInstance(listType);
        }

        internal static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Private field '{fieldName}' was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        internal static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Private field '{fieldName}' was not found on {target.GetType().Name}.");
            return field.GetValue(target);
        }

        internal static void SetPublicField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Public field '{fieldName}' was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        internal static object GetPublicField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Public field '{fieldName}' was not found on {target.GetType().Name}.");
            return field.GetValue(target);
        }

        internal static object GetPublicProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, $"Public property '{propertyName}' was not found on {target.GetType().Name}.");
            return property.GetValue(target);
        }

        internal static void DestroyFixture(Fixture fixture, params GameObject[] outfitTemplates)
        {
            for (int i = 0; i < outfitTemplates.Length; i++)
            {
                UnityEngine.Object.Destroy(outfitTemplates[i]);
            }

            for (int i = 0; i < fixture.assets.Count; i++)
            {
                UnityEngine.Object.Destroy(fixture.assets[i]);
            }

            UnityEngine.Object.Destroy(fixture.figure);
        }

        internal sealed class Fixture
        {
            public readonly GameObject figure;
            public readonly Component blender;
            public readonly Component attacher;
            public readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

            public Fixture(GameObject figure, Component blender, Component attacher)
            {
                this.figure = figure;
                this.blender = blender;
                this.attacher = attacher;
            }
        }
    }

}
