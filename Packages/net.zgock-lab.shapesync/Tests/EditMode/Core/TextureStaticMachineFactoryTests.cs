// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>Verifies Texture Static Machine Factory singleton resolution, configuration rejection, and StackMachine Host-reference removal.</summary>
    public sealed class TextureStaticMachineFactoryTests
    {
        [SetUp]
        public void SetUp()
        {
            TextureStaticMachineFactory.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            TextureStaticMachineFactory.ResetForTests();
            TextureStackMachineHost[] hosts = Object.FindObjectsByType<TextureStackMachineHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < hosts.Length; i++)
            {
                if (hosts[i] != null && hosts[i].gameObject.name.StartsWith("Spec15.5 Test", System.StringComparison.Ordinal)) Object.DestroyImmediate(hosts[i].gameObject);
            }
        }

        [Test]
        public void TryGetTSM_AdoptsOneExistingActiveSceneHostWithoutSettings()
        {
            Scene isolatedScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TextureStackMachineHost existing = CreateHost("Spec15.5 Test Existing");
            try
            {
                Assert.That(TextureStaticMachineFactory.TryGetTSM(null, isolatedScene, out TextureStackMachineHost resolved, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(resolved, Is.SameAs(existing));
            }
            finally
            {
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
            }
        }

        [Test]
        public void TryGetTSM_RejectsMultipleActiveSceneHosts()
        {
            CreateHost("Spec15.5 Test First");
            CreateHost("Spec15.5 Test Second");

            Assert.That(TextureStaticMachineFactory.TryGetTSM(null, SceneManager.GetActiveScene(), out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MultipleHosts"));
        }

        [Test]
        public void TryGetTSM_RechecksAfterAnotherHostInvalidatesTheSingletonCache()
        {
            CreateHost("Spec15.5 Test Cached First");
            Assert.That(TextureStaticMachineFactory.TryGetTSM(null, SceneManager.GetActiveScene(), out _, out StackMachineDiagnostic initial), Is.True, initial?.message);

            CreateHost("Spec15.5 Test Cached Second");
            Assert.That(TextureStaticMachineFactory.TryGetTSM(null, SceneManager.GetActiveScene(), out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MultipleHosts"));
        }

        [Test]
        public void TryGetTSM_RejectsMissingSettingsAndPrefabConfiguration()
        {
            Assert.That(TextureStaticMachineFactory.TryGetTSM(null, SceneManager.GetActiveScene(), out _, out StackMachineDiagnostic missingSettings), Is.False);
            Assert.That(missingSettings.domainCode, Is.EqualTo("FactoryConfigurationRequired"));

            TextureStaticMachineFactorySettings settings = ScriptableObject.CreateInstance<TextureStaticMachineFactorySettings>();
            try
            {
                Assert.That(TextureStaticMachineFactory.TryGetTSM(settings, SceneManager.GetActiveScene(), out _, out StackMachineDiagnostic missingPrefab), Is.False);
                Assert.That(missingPrefab.domainCode, Is.EqualTo("FactoryPrefabRequired"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void TryGetTSM_RejectsPrefabMissingEitherComputeProgram()
        {
            TextureStackMachineHost missingTexture = CreateHost("Spec15.5 Test Missing Texture");
            TextureStaticMachineFactorySettings textureSettings = CreateSettings(missingTexture);
            try
            {
                Assert.That(TextureStaticMachineFactory.TryValidateSettings(textureSettings, out _, out StackMachineDiagnostic textureDiagnostic), Is.False);
                Assert.That(textureDiagnostic.domainCode, Is.EqualTo("FactoryComputeProgramRequired"));
            }
            finally
            {
                Object.DestroyImmediate(textureSettings);
                Object.DestroyImmediate(missingTexture.gameObject);
            }

            TextureStackMachineHost missingNormal = CreateHost("Spec15.5 Test Missing Normal");
            Assert.That(missingNormal.TryAssignComputeProgram(LoadTextureCompute(), out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            TextureStaticMachineFactorySettings normalSettings = CreateSettings(missingNormal);
            try
            {
                Assert.That(TextureStaticMachineFactory.TryValidateSettings(normalSettings, out _, out StackMachineDiagnostic normalDiagnostic), Is.False);
                Assert.That(normalDiagnostic.domainCode, Is.EqualTo("FactoryNormalComputeProgramRequired"));
            }
            finally
            {
                Object.DestroyImmediate(normalSettings);
                Object.DestroyImmediate(missingNormal.gameObject);
            }
        }

        [Test]
        public void TryGetTSM_CreatesSingletonFromConfiguredPrefabAndRecreatesAfterDestroy()
        {
            TextureStackMachineHost prefab = AssetDatabase.LoadAssetAtPath<TextureStackMachineHost>(ShapeSyncTestAssetPaths.TextureStackMachineHostPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            TextureStaticMachineFactorySettings settings = CreateSettings(prefab);
            try
            {
                Assert.That(TextureStaticMachineFactory.TryGetTSM(settings, SceneManager.GetActiveScene(), out TextureStackMachineHost first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                Assert.That(TextureStaticMachineFactory.TryGetTSM(settings, SceneManager.GetActiveScene(), out TextureStackMachineHost cached, out StackMachineDiagnostic cachedDiagnostic), Is.True, cachedDiagnostic?.message);
                Assert.That(cached, Is.SameAs(first));
                Assert.That(first.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));

                first.gameObject.name = "Spec15.5 Test Generated";
                Object.DestroyImmediate(first.gameObject);
                Assert.That(TextureStaticMachineFactory.TryGetTSM(settings, SceneManager.GetActiveScene(), out TextureStackMachineHost recreated, out StackMachineDiagnostic recreatedDiagnostic), Is.True, recreatedDiagnostic?.message);
                Assert.That(recreated, Is.Not.SameAs(first));
                recreated.gameObject.name = "Spec15.5 Test Generated Recreated";
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void TryGetTSM_LoadsTheRequiredProjectSettingsAssetAndInstantiatesItsPrefab()
        {
            Assert.That(TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost host, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(host, Is.Not.Null);
            Assert.That(host.ComputeProgram, Is.Not.Null);
            Assert.That(host.NormalComputeProgram, Is.Not.Null);
            host.gameObject.name = "Spec15.5 Test Resource Generated";
        }

        [Test]
        public void StackMachines_DoNotRetainSerializedTextureHostReferences()
        {
            Assert.That(typeof(MaterialStackMachine).GetField("textureHost", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
            Assert.That(typeof(MaterialStackMachine).GetProperty("TextureHost", BindingFlags.Instance | BindingFlags.Public), Is.Null);
            Assert.That(typeof(MeshStackMachine).GetField("normalTextureHost", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
        }

        private static TextureStackMachineHost CreateHost(string name)
        {
            return new GameObject(name).AddComponent<TextureStackMachineHost>();
        }

        private static TextureStaticMachineFactorySettings CreateSettings(TextureStackMachineHost prefab)
        {
            TextureStaticMachineFactorySettings settings = ScriptableObject.CreateInstance<TextureStaticMachineFactorySettings>();
            SerializedObject serialized = new SerializedObject(settings);
            serialized.FindProperty("textureStackMachineHostPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        private static ComputeShader LoadTextureCompute() => AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
    }
}
