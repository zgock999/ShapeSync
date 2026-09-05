// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncDatabaseAssetTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20DatabaseAssetRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
                ShapeSyncTestAssetPaths.EnsureConsumerTempRoot();
                AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_1_ShapeSyncDatabaseAssetTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void TryLoad_LoadsRootComponentFromDatabasePrefab()
        {
            const string assetPath = Root + "/Database.prefab";
            GameObject source = new GameObject("Database");
            source.AddComponent<ShapeSyncDatabase>();
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, assetPath), Is.Not.Null);
                Hash128 dependencyHashBeforeLoad = AssetDatabase.GetAssetDependencyHash(assetPath);
                Assert.That(ShapeSyncDatabaseAsset.TryLoad(assetPath, out ShapeSyncDatabase database, out string diagnostic), Is.True, diagnostic);
                Assert.That(database, Is.Not.Null);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(assetPath), Is.TypeOf<GameObject>());
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(assetPath), Is.SameAs(database));
                Assert.That(AssetDatabase.GetAssetDependencyHash(assetPath), Is.EqualTo(dependencyHashBeforeLoad));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryLoad_RejectsNonPrefabAndPrefabWithoutDatabaseRootComponent()
        {
            const string emptyPrefabPath = Root + "/NotDatabase.prefab";
            const string materialPath = Root + "/NotDatabase.mat";
            GameObject source = new GameObject("NotDatabase");
            Material material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, emptyPrefabPath), Is.Not.Null);
                AssetDatabase.CreateAsset(material, materialPath);

                Assert.That(ShapeSyncDatabaseAsset.TryLoad(emptyPrefabPath, out _, out string missingComponentDiagnostic), Is.False);
                Assert.That(missingComponentDiagnostic, Does.Contain("ShapeSyncDatabase"));
                Assert.That(ShapeSyncDatabaseAsset.TryLoad(materialPath, out _, out string nonPrefabDiagnostic), Is.False);
                Assert.That(nonPrefabDiagnostic, Does.Contain("Prefab"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryLoadRootValidation_RejectsTypedComponentOutsideMainAssetRoot()
        {
            GameObject mainAsset = new GameObject("MainAsset");
            GameObject otherObject = new GameObject("OtherObject");
            ShapeSyncDatabase typedComponent = otherObject.AddComponent<ShapeSyncDatabase>();
            try
            {
                MethodInfo validationMethod = typeof(ShapeSyncDatabaseAsset).GetMethod("TryValidateRootComponent", BindingFlags.Static | BindingFlags.NonPublic);
                object[] arguments = { mainAsset, typedComponent, null };

                Assert.That((bool)validationMethod.Invoke(null, arguments), Is.False);
                Assert.That((string)arguments[2], Does.Contain("main-asset root"));
            }
            finally
            {
                Object.DestroyImmediate(mainAsset);
                Object.DestroyImmediate(otherObject);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void TryLoad_RejectsBlankAssetPath(string assetPath)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryLoad(assetPath, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("asset path"));
        }

        [Test]
        public void TryCreate_CreatesUniqueDatabaseWithIntermediateContainerAndSupportsOpen()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase first, out string firstDiagnostic), Is.True, firstDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase second, out string secondDiagnostic), Is.True, secondDiagnostic);

            string firstPath = AssetDatabase.GetAssetPath(first);
            string secondPath = AssetDatabase.GetAssetPath(second);
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(firstPath), Is.TypeOf<GameObject>());
            Assert.That(first.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
            Assert.That(first.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName).childCount, Is.Zero);
            Assert.That(first.Registry, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(first.Registry), Is.EqualTo(firstPath));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(firstPath).Count(asset => asset is ShapeSyncDatabaseRegistry), Is.EqualTo(1));
            AssetDatabase.ImportAsset(firstPath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(firstPath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(reopened.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName), Is.Not.Null);
            Assert.That(reopened.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName).childCount, Is.Zero);
        }

        [Test]
        public void TryOpen_RejectsDatabaseWithMultipleFixedRegistriesWithoutChangingItsAsset()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string assetPath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseRegistry extra = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            extra.name = "UnexpectedRegistry";
            try
            {
                AssetDatabase.AddObjectToAsset(extra, assetPath);
                AssetDatabase.SaveAssets();
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out _, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("exactly one"));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).Count(asset => asset is ShapeSyncDatabaseRegistry), Is.EqualTo(2));
            }
            finally { /* TearDown owns the deliberately invalid test asset. */ }
        }

        [Test]
        public void TryOpen_RejectsExternalDatabaseTextureWithoutLocalDuplicateCounterpart()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D external = new Texture2D(1, 1) { name = "ExternalDatabaseTexture" };
            AssetDatabase.CreateAsset(external, Root + "/ExternalDatabaseTexture.asset");
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
            {
                Assert.That(contents.Registry.TryRegisterTextureResource("External", external, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out _, out string openDiagnostic), Is.False);
            Assert.That(openDiagnostic, Does.Contain("local counterpart"));
            Assert.That(AssetDatabase.GetAssetPath(external), Is.EqualTo(Root + "/ExternalDatabaseTexture.asset"));
        }

        [Test]
        public void TryOpen_AllowsExternalAvatarReferenceUsedByOutfitAuthoringWithoutRebindingIt()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            const string externalAvatarPath = Root + "/OutfitExternalAvatar.asset";
            GameObject avatarSource = new GameObject("OutfitAvatarSource");
            Avatar externalAvatar = null;
            try
            {
                externalAvatar = AvatarBuilder.BuildGenericAvatar(avatarSource, string.Empty);
                Assert.That(externalAvatar, Is.Not.Null);
                externalAvatar.name = "humanoid";
                AssetDatabase.CreateAsset(externalAvatar, externalAvatarPath);
                AssetDatabase.SaveAssets();
                externalAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(externalAvatarPath);
                Assert.That(externalAvatar, Is.Not.Null);

                Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databasePath, (contents, _) =>
                {
                    GameObject outfitCarrier = new GameObject("OutfitExternalAvatarCarrier");
                    outfitCarrier.transform.SetParent(contents.transform, false);
                    outfitCarrier.AddComponent<Animator>().avatar = externalAvatar;
                }, out string setupDiagnostic), Is.True, setupDiagnostic);

                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                Animator persistedAnimator = opened.GetComponentInChildren<Animator>(true);
                Assert.That(persistedAnimator, Is.Not.Null);
                Assert.That(persistedAnimator.avatar, Is.SameAs(externalAvatar));
                Assert.That(AssetDatabase.GetAssetPath(persistedAnimator.avatar), Is.EqualTo(externalAvatarPath));
            }
            finally
            {
                Object.DestroyImmediate(avatarSource);
            }
        }

        [Test]
        public void TryOpen_RebindsExternalTextureToTheDuplicateLocalSubAsset()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D local = new Texture2D(1, 1) { name = "SharedTexture" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(local);
                Assert.That(contents.Registry.TryRegisterTextureResource("Shared", local, out string registerDiagnostic), Is.True, registerDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Texture2D external = new Texture2D(1, 1) { name = "SharedTexture" };
            AssetDatabase.CreateAsset(external, Root + "/ExternalSharedTexture.asset");
            external.name = "SharedTexture";
            EditorUtility.SetDirty(external);
            AssetDatabase.SaveAssets();
            SerializedObject serialized = new SerializedObject(database.Registry);
            serialized.FindProperty("textureResources").GetArrayElementAtIndex(0).FindPropertyRelative("texture").objectReferenceValue = external;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
            Assert.That(opened.Registry.TextureResources.Single().Texture, Is.Not.SameAs(external));
            Assert.That(AssetDatabase.GetAssetPath(opened.Registry.TextureResources.Single().Texture), Is.EqualTo(databasePath));
            Assert.That(AssetDatabase.GetAssetPath(external), Is.EqualTo(Root + "/ExternalSharedTexture.asset"));
        }

        [Test]
        public void TryOpen_RejectsDatabaseWithoutIntermediateContainer()
        {
            const string assetPath = Root + "/DatabaseWithoutContainer.prefab";
            GameObject source = new GameObject("DatabaseWithoutContainer");
            source.AddComponent<ShapeSyncDatabase>();
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, assetPath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryLoad(assetPath, out _, out _), Is.True);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(assetPath, out _, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain(ShapeSyncDatabaseAsset.IntermediateContainerName));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase("")]
        [TestCase(ShapeSyncTestAssetPaths.Spec20DatabaseAssetMissingFolder)]
        [TestCase("Packages")]
        public void TryCreate_RejectsInvalidOutputFolder(string folderPath)
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(folderPath, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("folder"));
        }

        [Test]
        public void TextureResourceOwner_IsStructuredPersistentAndDoesNotExposeLegacyInference()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D figureTexture = new Texture2D(1, 1) { name = "FigureTexture" };
            Texture2D outfitTexture = new Texture2D(1, 1) { name = "OutfitTexture" };
            Texture2D outerTexture = new Texture2D(1, 1) { name = "OutfitOuterTexture" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(figureTexture);
                context.AddSubAsset(outfitTexture);
                context.AddSubAsset(outerTexture);
                Assert.That(contents.Registry.TryRegisterTextureResource("FigureBase", figureTexture, out string figureDiagnostic), Is.True, figureDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("OutfitTop", outfitTexture,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Top", "Tall"), out string topDiagnostic), Is.True, topDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("OutfitOuter", outerTexture,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Outer", "Tall"), out string outerDiagnostic), Is.True, outerDiagnostic);
                Assert.That(contents.Registry.TryRegisterTextureResource("OutfitTopDuplicate", outfitTexture,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Top", "Tall"), out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("distinct Database-owned Texture"));
            }, out string saveDiagnostic), Is.True, saveDiagnostic);

            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            ShapeSyncDatabaseRegistry.TextureResourceEntry figure = reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "FigureBase");
            ShapeSyncDatabaseRegistry.TextureResourceEntry outfit = reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "OutfitTop");
            Assert.That(figure.Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure));
            Assert.That(figure.Owner.OutfitIdentity, Is.Empty);
            Assert.That(figure.Owner.SourceShapeKey, Is.Empty);
            Assert.That(outfit.Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit));
            Assert.That(outfit.Owner.OutfitIdentity, Is.EqualTo("Top"));
            Assert.That(outfit.Owner.SourceShapeKey, Is.EqualTo("Tall"));
            Assert.That(reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "OutfitOuter").Owner.OutfitIdentity, Is.EqualTo("Outer"));
            Assert.That(reopened.Registry.TextureResources.Single(entry => entry.LogicalName == "OutfitOuter").Texture, Is.Not.SameAs(outfit.Texture));
            Assert.That(typeof(ShapeSyncDatabaseRegistry).GetMethod("TryGetLegacyFbmImportOwner", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
        }

        [Test]
        public void TextureResourceRemoval_ReportsStructuredMaterialAndNormalReferencesWithoutInspectingOwner()
        {
            Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            string databasePath = AssetDatabase.GetAssetPath(database);
            Texture2D texture = new Texture2D(1, 1) { name = "Owned" };
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (contents, _, context) =>
            {
                context.AddSubAsset(texture);
                Assert.That(contents.Registry.TryRegisterTextureResource("OutfitOwned", texture,
                    ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("Top"), out string registerDiagnostic), Is.True, registerDiagnostic);
                var material = new ShapeSyncDatabaseRegistry.MaterialEntry("Body", null, string.Empty, 0, "Body", null, null);
                material.SetTextureResourceNames(new[] { "OutfitOwned" });
                typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(contents.Registry, new List<ShapeSyncDatabaseRegistry.MaterialEntry> { material });
                Assert.That(contents.Registry.TryRemoveTextureResource("OutfitOwned", out Texture removedForMaterial, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic materialDiagnostic), Is.False);
                Assert.That(removedForMaterial, Is.Null);
                Assert.That(materialDiagnostic.Code, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceDiagnosticCode.ReferencedByMaterialEntry));
                Assert.That(materialDiagnostic.ReferenceName, Is.EqualTo("Body"));
                material.SetTextureResourceNames(System.Array.Empty<string>());
                typeof(ShapeSyncDatabaseRegistry).GetField("normalEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(contents.Registry, new List<ShapeSyncDatabaseRegistry.NormalEntry> { new ShapeSyncDatabaseRegistry.NormalEntry("Body", "Tall", "OutfitOwned", texture) });
                Assert.That(contents.Registry.TryRemoveTextureResource("OutfitOwned", out Texture removedForNormal, out ShapeSyncDatabaseRegistry.TextureResourceDiagnostic normalDiagnostic), Is.False);
                Assert.That(removedForNormal, Is.Null);
                Assert.That(normalDiagnostic.Code, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceDiagnosticCode.ReferencedByNormalEntry));
                Assert.That(normalDiagnostic.ReferenceName, Is.EqualTo("Body"));
                Assert.That(normalDiagnostic.ShapeKey, Is.EqualTo("Tall"));
            }, out string editDiagnostic), Is.True, editDiagnostic);
        }
    }
}
#endif
