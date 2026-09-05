// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

#if UNITY_6000_2_OR_NEWER
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncShapeAuthoringTests
    {
        private ShapeSyncDatabaseRegistry registry;

        [SetUp]
        public void SetUp() => registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(registry);

        [Test]
        public void ShapeTags_RejectRemovalWhileReferencedAndOnlyAllowVocabularyMembers()
        {
            Assert.That(registry.TrySetShapeTags(new[] { "Hair", "Formal" }, out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair-long", "Long Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 10, new[] { "Hair" }, out string addDiagnostic), Is.True, addDiagnostic);

            Assert.That(registry.TrySetShapeTags(new[] { "Formal" }, out string removalDiagnostic), Is.False);
            Assert.That(removalDiagnostic, Does.Contain("still referenced"));
            Assert.That(registry.TryUpdateShape("hair-long", "Long Hair", 10, new[] { "Unknown" }, out string updateDiagnostic), Is.False);
            Assert.That(updateDiagnostic, Does.Contain("vocabulary"));
            Assert.That(registry.Shapes[0].Tags, Is.EqualTo(new[] { "Hair" }));
        }

        [Test]
        public void MorphShape_HasFixedZeroPriorityAndImmutableId()
        {
            Assert.That(registry.TrySetShapeTags(new[] { "Hair" }, out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("morph-1", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 99, System.Array.Empty<string>(), out string addDiagnostic), Is.True, addDiagnostic);
            Assert.That(registry.TryUpdateShape("morph-1", "Updated", -5, System.Array.Empty<string>(), out string updateDiagnostic), Is.True, updateDiagnostic);

            ShapeSyncDatabaseRegistry.ShapeEntry shape = registry.Shapes[0];
            Assert.That(shape.ShapeId, Is.EqualTo("morph-1"));
            Assert.That(shape.ShapeName, Is.EqualTo("Updated"));
            Assert.That(shape.Priority, Is.Zero);
            Assert.That(shape.Tags, Is.Empty);
            Assert.That(registry.TryAddShape("morph-tagged", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, new[] { "Hair" }, out string addTagDiagnostic), Is.False);
            Assert.That(addTagDiagnostic, Does.Contain("does not accept Tags"));
            Assert.That(registry.TryUpdateShape("morph-1", "Updated", 0, new[] { "Hair" }, out string updateTagDiagnostic), Is.False);
            Assert.That(updateTagDiagnostic, Does.Contain("does not accept Tags"));
            Assert.That(registry.TryAddShape("morph-1", "Duplicate", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 1, System.Array.Empty<string>(), out string duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic, Does.Contain("already exists"));
        }

        [Test]
        public void MoveShape_ChangesAuthoringOrderOnly()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("skin", "Skin", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 12, System.Array.Empty<string>(), out string skinDiagnostic), Is.True, skinDiagnostic);
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 3, System.Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);

            Assert.That(registry.TryMoveShape("hair", true, out string moveDiagnostic), Is.True, moveDiagnostic);
            Assert.That(registry.Shapes[0].ShapeId, Is.EqualTo("hair"));
            Assert.That(registry.Shapes[0].Priority, Is.EqualTo(3));
            Assert.That(registry.TryMoveShape("hair", true, out string boundaryDiagnostic), Is.False);
            Assert.That(boundaryDiagnostic, Does.Contain("already first"));
        }

        [Test]
        public void ShapeAuthoring_SerializesTagVocabularyAndConcreteRecord()
        {
            Assert.That(registry.TrySetShapeTags(new[] { "Long Hair" }, out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair-long", "Long Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 4, new[] { "Long Hair" }, out string addDiagnostic), Is.True, addDiagnostic);

            string json = JsonUtility.ToJson(registry);
            ShapeSyncDatabaseRegistry rehydrated = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, rehydrated);
                Assert.That(rehydrated.ShapeTags, Is.EqualTo(new[] { "Long Hair" }));
                Assert.That(rehydrated.Shapes, Has.Count.EqualTo(1));
                Assert.That(rehydrated.Shapes[0].Kind, Is.EqualTo(ShapeSyncDatabaseRegistry.ShapeKind.Hair));
                Assert.That(rehydrated.Shapes[0].Tags, Is.EqualTo(new[] { "Long Hair" }));
            }
            finally
            {
                Object.DestroyImmediate(rehydrated);
            }
        }

        [Test]
        public void Parts_AreUnavailableToMorphAndRetainExplicitAuthoringOrder()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("morph", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, System.Array.Empty<string>(), out string morphDiagnostic), Is.True, morphDiagnostic);
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string hairDiagnostic), Is.True, hairDiagnostic);

            Assert.That(registry.TryAddShapePart("morph", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string morphPartDiagnostic), Is.False);
            Assert.That(morphPartDiagnostic, Does.Contain("does not accept"));
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string meshDiagnostic), Is.True, meshDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string textureDiagnostic), Is.True, textureDiagnostic);
            Assert.That(registry.TryMoveShapePart("hair", 1, true, out string moveDiagnostic), Is.True, moveDiagnostic);

            Assert.That(registry.Shapes[1].Parts[0].Kind, Is.EqualTo(ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture));
            Assert.That(registry.TryRemoveShapePart("hair", 1, out string removeDiagnostic), Is.True, removeDiagnostic);
            Assert.That(registry.Shapes[1].Parts, Has.Count.EqualTo(1));
        }

        [Test]
        public void MorphValues_RejectNonMorphAndUnknownFigureAxisTargets()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("morph", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, System.Array.Empty<string>(), out string morphDiagnostic), Is.True, morphDiagnostic);
            Assert.That(registry.TryAddShape("skin", "Skin", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 0, System.Array.Empty<string>(), out string skinDiagnostic), Is.True, skinDiagnostic);
            var value = new MorphValue { Target = "unknown-axis", Value = 30f };

            Assert.That(registry.TrySetShapeMorphs("skin", new[] { value }, out string nonMorphDiagnostic), Is.False);
            Assert.That(nonMorphDiagnostic, Does.Contain("Only Morph"));
            Assert.That(registry.TrySetShapeMorphs("morph", new[] { value }, out string targetDiagnostic), Is.False);
            Assert.That(targetDiagnostic, Does.Contain("Figure FBM or PBM"));
        }

        [Test]
        public void PartsTargets_RejectUnregisteredMeshMaterialAndTextureTargets()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string meshPartDiagnostic), Is.True, meshPartDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string texturePartDiagnostic), Is.True, texturePartDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string colorPartDiagnostic), Is.True, colorPartDiagnostic);

            Assert.That(registry.TrySetShapePartMeshOutfit("hair", 0, "material-outfit", out string meshDiagnostic), Is.False);
            Assert.That(meshDiagnostic, Does.Contain("Mesh Outfit"));
            Assert.That(registry.TrySetShapePartMaterialTarget("hair", 1, "material-outfit", "entry", out string targetDiagnostic), Is.False);
            Assert.That(targetDiagnostic, Does.Contain("Figure or Mesh Outfit"));
            Assert.That(registry.TrySetShapePartTexture("hair", 1, "source-texture", false, Color.white, out string textureDiagnostic), Is.False);
            Assert.That(textureDiagnostic, Does.Contain("Database Texture"));
            Assert.That(registry.TrySetShapePartColor("hair", 2, Color.magenta, out string colorDiagnostic), Is.True, colorDiagnostic);
            Assert.That((Color)registry.Shapes[0].Parts[2].Color, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void UvSet_PersistsFiniteScaleAndOffsetAndRejectsOtherEntryKinds()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset, out string uvPartDiagnostic), Is.True, uvPartDiagnostic);
            Assert.That(registry.TrySetShapePartUv("hair", 0, 2f, 3f, .25f, -.5f, out string uvDiagnostic), Is.True, uvDiagnostic);

            ShapeSyncDatabaseRegistry.ShapeEntryDefinition uv = registry.Shapes[0].Parts[0];
            Assert.That(uv.ScaleX, Is.EqualTo(2f));
            Assert.That(uv.ScaleY, Is.EqualTo(3f));
            Assert.That(uv.OffsetX, Is.EqualTo(.25f));
            Assert.That(uv.OffsetY, Is.EqualTo(-.5f));
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string colorPartDiagnostic), Is.True, colorPartDiagnostic);
            Assert.That(registry.TrySetShapePartUv("hair", 1, 1f, 1f, 0f, 0f, out string colorDiagnostic), Is.False);
            Assert.That(colorDiagnostic, Does.Contain("Only UVSet"));
        }

        [Test]
        public void PartsTargets_AcceptMeshOutfitAndItsMaterialEntryOnly()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = registry.Outfits[0];
            outfit.SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("body", null, null) });
            outfit.SetFigureMaskEntries(new[] { new ShapeSyncDatabaseRegistry.FigureMaskEntry("figure-body", "mask-resource") });
            typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new System.Collections.Generic.List<ShapeSyncDatabaseRegistry.MaterialEntry>
                { new ShapeSyncDatabaseRegistry.MaterialEntry("figure-body", null, string.Empty, 0, string.Empty, null, null) });
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out string meshPartDiagnostic), Is.True, meshPartDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string colorPartDiagnostic), Is.True, colorPartDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string texturePartDiagnostic), Is.True, texturePartDiagnostic);
            Assert.That(registry.TryAddShapePart("hair", ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset, out string uvPartDiagnostic), Is.True, uvPartDiagnostic);

            Assert.That(registry.TrySetShapePartMeshOutfit("hair", 0, "mesh-outfit", out string meshDiagnostic), Is.True, meshDiagnostic);
            Assert.That(registry.TrySetShapePartMaterialTarget("hair", 1, "mesh-outfit", "body", out string materialDiagnostic), Is.True, materialDiagnostic);
            Assert.That(registry.Shapes[0].Parts[0].OutfitIdentity, Is.EqualTo("mesh-outfit"));
            Assert.That(registry.Shapes[0].Parts[1].RegistryId, Is.EqualTo("mesh-outfit"));
            Assert.That(registry.Shapes[0].Parts[1].ProxyEntry, Is.EqualTo("body"));
            Assert.That(registry.TrySetShapePartMaterialTarget("hair", 3, string.Empty, "figure-body", out string figureMaterialDiagnostic), Is.True, figureMaterialDiagnostic);
            Assert.That(registry.Shapes[0].Parts[3].RegistryId, Is.Empty);
            Assert.That(registry.Shapes[0].Parts[3].ProxyEntry, Is.EqualTo("figure-body"));
            Texture2D texture = new Texture2D(1, 1);
            try
            {
                Assert.That(registry.TryRegisterTextureResource("hair-texture", texture, out string textureDiagnostic), Is.True, textureDiagnostic);
                Assert.That(registry.TrySetShapePartTexture("hair", 2, "hair-texture", true, Color.red, out string setTextureDiagnostic), Is.True, setTextureDiagnostic);
                Assert.That(registry.Shapes[0].Parts[2].TextureResourceName, Is.EqualTo("hair-texture"));
                Assert.That(registry.Shapes[0].Parts[2].UseColorize, Is.True);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void MaterialEntryRename_PropagatesFigureShapePartsButNotOutfitShapeParts()
        {
            Assert.That(registry.TrySetShapeTags(Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new List<ShapeSyncDatabaseRegistry.MaterialEntry>
                {
                    new ShapeSyncDatabaseRegistry.MaterialEntry("Body", null, string.Empty, 0, string.Empty, null, null)
                });
            Assert.That(registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            registry.Outfits[0].SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("Body", null, null) });

            Assert.That(registry.TryAddShape("figure-shape", "Figure Shape", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string figureShapeDiagnostic), Is.True, figureShapeDiagnostic);
            Assert.That(registry.TryAddShapePart("figure-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string figurePartDiagnostic), Is.True, figurePartDiagnostic);
            Assert.That(registry.TrySetShapePartMaterialTarget("figure-shape", 0, string.Empty, "Body", out string figureTargetDiagnostic), Is.True, figureTargetDiagnostic);
            Assert.That(registry.TryAddShape("outfit-shape", "Outfit Shape", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, 0, Array.Empty<string>(), out string outfitShapeDiagnostic), Is.True, outfitShapeDiagnostic);
            Assert.That(registry.TryAddShapePart("outfit-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out string outfitPartDiagnostic), Is.True, outfitPartDiagnostic);
            Assert.That(registry.TrySetShapePartMaterialTarget("outfit-shape", 0, "mesh-outfit", "Body", out string outfitTargetDiagnostic), Is.True, outfitTargetDiagnostic);

            Assert.That(registry.TryRenameMaterialEntry("Body", "BodyRenamed", out string renameDiagnostic), Is.True, renameDiagnostic);

            Assert.That(registry.Shapes.Single(shape => shape.ShapeId == "figure-shape").Parts[0].ProxyEntry, Is.EqualTo("BodyRenamed"));
            Assert.That(registry.Shapes.Single(shape => shape.ShapeId == "figure-shape").Parts[0].RegistryId, Is.Empty);
            Assert.That(registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape").Parts[0].ProxyEntry, Is.EqualTo("Body"));
            Assert.That(registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape").Parts[0].RegistryId, Is.EqualTo("mesh-outfit"));
            Assert.That(registry.TryValidateShapePartsForGeneration(registry.Shapes.Single(shape => shape.ShapeId == "figure-shape").Parts, out string figureValidationDiagnostic), Is.True, figureValidationDiagnostic);
            Assert.That(registry.TryValidateShapePartsForGeneration(registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape").Parts, out string outfitValidationDiagnostic), Is.True, outfitValidationDiagnostic);
        }

        [Test]
        public void TextureResourceRename_PropagatesEveryShapeTexturePart()
        {
            Texture2D texture = new Texture2D(1, 1) { name = "SharedTexture" };
            try
            {
                Assert.That(registry.TrySetShapeTags(Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
                Assert.That(registry.TryRegisterTextureResource("Shared", texture, out string resourceDiagnostic), Is.True, resourceDiagnostic);
                typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(registry, new List<ShapeSyncDatabaseRegistry.MaterialEntry>
                    {
                        new ShapeSyncDatabaseRegistry.MaterialEntry("Body", null, string.Empty, 0, string.Empty, null, null)
                    });
                Assert.That(registry.TryAddOutfit("mesh-outfit", "Mesh Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                registry.Outfits[0].SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("Body", null, null) });
                Assert.That(registry.TryAddShape("figure-shape", "Figure Shape", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, Array.Empty<string>(), out string figureShapeDiagnostic), Is.True, figureShapeDiagnostic);
                Assert.That(registry.TryAddShapePart("figure-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string figurePartDiagnostic), Is.True, figurePartDiagnostic);
                Assert.That(registry.TrySetShapePartMaterialTarget("figure-shape", 0, string.Empty, "Body", out string figureTargetDiagnostic), Is.True, figureTargetDiagnostic);
                Assert.That(registry.TrySetShapePartTexture("figure-shape", 0, "Shared", true, Color.red, out string figureTextureDiagnostic), Is.True, figureTextureDiagnostic);
                Assert.That(registry.TryAddShape("outfit-shape", "Outfit Shape", ShapeSyncDatabaseRegistry.ShapeKind.Outfit, 0, Array.Empty<string>(), out string outfitShapeDiagnostic), Is.True, outfitShapeDiagnostic);
                Assert.That(registry.TryAddShapePart("outfit-shape", ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out string outfitPartDiagnostic), Is.True, outfitPartDiagnostic);
                Assert.That(registry.TrySetShapePartMaterialTarget("outfit-shape", 0, "mesh-outfit", "Body", out string outfitTargetDiagnostic), Is.True, outfitTargetDiagnostic);
                Assert.That(registry.TrySetShapePartTexture("outfit-shape", 0, "Shared", false, Color.blue, out string outfitTextureDiagnostic), Is.True, outfitTextureDiagnostic);

                Assert.That(registry.TryRenameTextureResource("Shared", "SharedRenamed", out string renameDiagnostic), Is.True, renameDiagnostic);

                ShapeSyncDatabaseRegistry.ShapeEntryDefinition figurePart = registry.Shapes.Single(shape => shape.ShapeId == "figure-shape").Parts[0];
                ShapeSyncDatabaseRegistry.ShapeEntryDefinition outfitPart = registry.Shapes.Single(shape => shape.ShapeId == "outfit-shape").Parts[0];
                Assert.That(figurePart.TextureResourceName, Is.EqualTo("SharedRenamed"));
                Assert.That(outfitPart.TextureResourceName, Is.EqualTo("SharedRenamed"));
                Assert.That(figurePart.UseColorize, Is.True);
                Assert.That(outfitPart.UseColorize, Is.False);
                Assert.That(registry.TryValidateShapePartsForGeneration(new[] { figurePart }, out string figureValidationDiagnostic), Is.True, figureValidationDiagnostic);
                Assert.That(registry.TryValidateShapePartsForGeneration(new[] { outfitPart }, out string outfitValidationDiagnostic), Is.True, outfitValidationDiagnostic);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void TextureResourceRename_RenamesUnityObjectWithoutChangingTextureContentContract()
        {
            Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.5f, .5f, 1f, 1f);
            texture.SetPixels(pixels); texture.Apply(false, false);
            Color before = texture.GetPixel(0, 0);
            try
            {
                Assert.That(registry.TryRegisterTextureResource("NeutralNormal", texture, out string registerDiagnostic), Is.True, registerDiagnostic);
                Assert.That(registry.TryRenameTextureResource("NeutralNormal", "Figure_Brow_Normal", out string renameDiagnostic), Is.True, renameDiagnostic);
                Assert.That(texture.name, Is.EqualTo("Figure_Brow_Normal"));
                Assert.That(texture.GetPixel(0, 0), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ShapeParts_RejectUnconfiguredReferencesAndAcceptFigureTargetWithEmptyRegistryId()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair", "Hair", ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);

            var emptyMesh = new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh);
            Assert.That(registry.TryUpdateShapeAndContents("hair", "Hair", 0, System.Array.Empty<string>(), System.Array.Empty<MorphValue>(), new[] { emptyMesh }, out string meshDiagnostic), Is.False);
            Assert.That(meshDiagnostic, Does.Contain("requires a Mesh Outfit target"));

            var emptyColor = new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color);
            Assert.That(registry.TryUpdateShapeAndContents("hair", "Hair", 0, System.Array.Empty<string>(), System.Array.Empty<MorphValue>(), new[] { emptyColor }, out string colorDiagnostic), Is.False);
            Assert.That(colorDiagnostic, Does.Contain("requires a Material Entry target"));

            typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new System.Collections.Generic.List<ShapeSyncDatabaseRegistry.MaterialEntry>
                { new ShapeSyncDatabaseRegistry.MaterialEntry("figure-entry", null, string.Empty, 0, string.Empty, null, null) });
            var figureColor = new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color);
            figureColor.SetMaterialTarget(string.Empty, "figure-entry");
            Assert.That(registry.TryUpdateShapeAndContents("hair", "Hair", 0, System.Array.Empty<string>(), System.Array.Empty<MorphValue>(), new[] { figureColor }, out string figureDiagnostic), Is.True, figureDiagnostic);

            Texture2D texture = new Texture2D(1, 1);
            try
            {
                Assert.That(registry.TryRegisterTextureResource("hair-texture", texture, out string textureDiagnostic), Is.True, textureDiagnostic);
                var texturePart = new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture);
                texturePart.SetMaterialTarget(string.Empty, "figure-entry");
                Assert.That(registry.TryUpdateShapeAndContents("hair", "Hair", 0, System.Array.Empty<string>(), System.Array.Empty<MorphValue>(), new[] { texturePart }, out string resourceDiagnostic), Is.False);
                Assert.That(resourceDiagnostic, Does.Contain("requires a Database Texture resource"));
                texturePart.SetTexture("hair-texture", false, texturePart.Color);
                Assert.That(registry.TryUpdateShapeAndContents("hair", "Hair", 0, System.Array.Empty<string>(), System.Array.Empty<MorphValue>(), new[] { texturePart }, out string validDiagnostic), Is.True, validDiagnostic);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ShapesTree_UsesConcreteGroupsFallbackNameAndLeafCallback()
        {
            Assert.That(registry.TrySetShapeTags(System.Array.Empty<string>(), out string tagDiagnostic), Is.True, tagDiagnostic);
            Assert.That(registry.TryAddShape("hair-id", string.Empty, ShapeSyncDatabaseRegistry.ShapeKind.Hair, 0, System.Array.Empty<string>(), out string shapeDiagnostic), Is.True, shapeDiagnostic);
            string selectedId = null;
            var tree = new ShapeSyncDatabaseWindow.NavigationTreeView(new ShapeSyncTreeViewState(), _ => true,
                () => ShapeSyncDatabaseWindow.Section.General, null, null, null, () => registry.Shapes,
                id => { selectedId = id; return true; });

            Assert.That(tree.ShapeGroupDisplayNamesForTest, Is.EqualTo(new[] { "Morph Shapes", "Skin Shapes", "Hair Shapes", "Outfit Shapes" }));
            Assert.That(tree.GetShapeItemIdForTest("hair-id"), Is.GreaterThan(0));
            tree.ApplySelectionChangeForTest(new[] { tree.GetShapeItemIdForTest("hair-id") });
            Assert.That(selectedId, Is.EqualTo("hair-id"));
        }
    }
}
#endif
