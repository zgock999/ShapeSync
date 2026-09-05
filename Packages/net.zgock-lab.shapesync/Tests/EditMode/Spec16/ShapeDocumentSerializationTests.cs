// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeDocumentSerializationTests
    {
 #if SHAPESYNC_RICH_TEST
        [Test]
        public void ShapeDocument_BFixture_DeserializesIrisColorAndRecompilesItsSavedMaterialRestoreSource()
        {
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Spec16/ShapeDocument_B.asset";
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(document, Is.Not.Null, "Spec16 ShapeDocument_B fixture is required for this regression test.");

            var host = new GameObject("deserializer");
            try
            {
                var deserializer = host.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(deserializer.TryDeserialize(documentPath, out List<ShapeSyncShape> restored), Is.True);
                Assert.That(deserializer.LastLoadedDocument, Is.SameAs(document));

                TextureEntry iris = FindTexture(restored, "iris");
                Assert.That(iris, Is.Not.Null);
                Assert.That(iris.UseColor, Is.True);
                Assert.That(iris.Color, Is.EqualTo(new Color32(61, 164, 96, 171)));

                Assert.That(ShapeSyncShapeResolver.TryResolve(restored, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out _, out List<ShapeSyncMergedEntry> material, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), material, out string materialBody, out diagnostic), Is.True, diagnostic?.message);
                string expectedRestore = "FIGURE\nMATERIAL_RESET" + (string.IsNullOrWhiteSpace(materialBody) || materialBody == "FIGURE" ? string.Empty : "\n" + materialBody);
                Assert.That(document.MaterialRecipe.wordSource, Is.EqualTo(expectedRestore));
                Assert.That(document.MaterialRecipe.wordSource, Does.Contain("$basicfemale-iris CANVAS 0.369435 0.8742987 0 COLORIZE"));
                Assert.That(document.MaterialRecipe.wordSource, Does.Not.Contain("FILL MULTIPLY"));
            }
            finally { Object.DestroyImmediate(host); }
        }
#endif

        [Test]
        public void ShapeDocumentSerializer_RoundTripsMixedRuntimeOrderAndCopiesParts()
        {
            var host = new GameObject("serializer");
            string fileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDocumentSerializationTests.asset"));
            try
            {
                var source = new List<ShapeSyncShape>
                {
                    new HairShape("hair", 2, new[] { "hair" }, new ShapeEntry[] { new MeshEntry { LogicalName = "hair-1" } }),
                    new MorphShape("morph", 1, null, new[] { new MorphValue { Target = "girl", Value = .5f } }),
                    new SkinShape("skin", 0, null, new ShapeEntry[] { new TextureEntry { ProxyEntry = "body", LogicalName = "skin" } })
                };

                var serializer = host.AddComponent<ShapeDocumentSerializer>();
                Assert.That(serializer.TrySerialize(fileName, source), Is.True);
                ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName);
                Assert.That(document.HairShapes, Has.Count.EqualTo(1));
                Assert.That(document.MorphShapes[0].ListPosition, Is.EqualTo(1));
                Assert.That(document.SkinShapes[0].Parts[0], Is.Not.SameAs(((PartsShape)source[2]).Parts[0]));
                Assert.That(((IShapeSyncDocument)document).MeshRecipe.wordSource, Is.Empty);

                var deserializer = host.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(deserializer.TryDeserialize(fileName, out List<ShapeSyncShape> restored), Is.True);
                Assert.That(restored, Has.Count.EqualTo(3));
                Assert.That(restored[0], Is.TypeOf<HairShape>());
                Assert.That(restored[1], Is.TypeOf<MorphShape>());
                Assert.That(restored[2], Is.TypeOf<SkinShape>());
                Assert.That(((MorphShape)restored[1]).Morphs[0].Value, Is.EqualTo(.5f));
            }
            finally { AssetDatabase.DeleteAsset(fileName); Object.DestroyImmediate(host); }
        }

        [Test]
        public void ShapeDirector_SerializesAndDeserializesThroughConfiguredInterfaces()
        {
            var gameObject = new GameObject("director");
            string fileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDirectorConfiguredSerializationTests.asset"));
            try
            {
                var director = gameObject.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                var serializer = gameObject.AddComponent<ShapeDocumentSerializer>();
                var deserializer = gameObject.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(director.TryConfigureSerialization(serializer, deserializer, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryReplaceRuntimeShape(new MorphShape("missing", 0, null, null), out diagnostic), Is.False);
                var template = ScriptableObject.CreateInstance<MorphShapeTemplate>();
                try
                {
                    template.ShapeId = "morph"; template.Morphs.Add(new MorphValue { Target = "girl", Value = .2f });
                    Assert.That(director.TryAddTemplate(template, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(director.TrySerialize(fileName), Is.True);
                    ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName);
                    Assert.That(document.MeshRecipe.wordSource, Is.EqualTo("DETACH_ALL\nMORPH_RESET"));
                    Assert.That(document.MaterialRecipe.wordSource, Is.EqualTo("FIGURE\nMATERIAL_RESET"));
                    Assert.That(director.TryReplaceRuntimeShape(new MorphShape("morph", 0, null, new[] { new MorphValue { Target = "girl", Value = .9f } }), out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(director.TryDeserialize(fileName), Is.True);
                    Assert.That(((MorphShape)director.RuntimeShapes[0]).Morphs[0].Value, Is.EqualTo(.2f));
                }
                finally { Object.DestroyImmediate(template); }
            }
            finally { AssetDatabase.DeleteAsset(fileName); Object.DestroyImmediate(gameObject); }
        }

        private static TextureEntry FindTexture(IReadOnlyList<ShapeSyncShape> shapes, string proxyEntry)
        {
            for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
            {
                if (!(shapes[shapeIndex] is PartsShape parts)) continue;
                for (int partIndex = 0; partIndex < parts.Parts.Count; partIndex++)
                {
                    if (parts.Parts[partIndex] is TextureEntry texture && texture.ProxyEntry == proxyEntry) return texture;
                }
            }
            return null;
        }

        [Test]
        public void ShapeDirector_SerializesAndDeserializesThroughCoLocatedComponents()
        {
            var gameObject = new GameObject("director");
            string fileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDirectorCoLocatedSerializationTests.asset"));
            var meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
            var materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
            string meshBindingFileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDirectorMeshBindingTests.asset"));
            string materialBindingFileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDirectorMaterialBindingTests.asset"));
            try
            {
                AssetDatabase.CreateAsset(meshBinding, meshBindingFileName);
                AssetDatabase.CreateAsset(materialBinding, materialBindingFileName);
                var director = gameObject.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                var serializedDirector = new SerializedObject(director);
                serializedDirector.FindProperty("meshBinding").objectReferenceValue = meshBinding;
                serializedDirector.FindProperty("materialBinding").objectReferenceValue = materialBinding;
                serializedDirector.ApplyModifiedPropertiesWithoutUndo();
                gameObject.AddComponent<ShapeDocumentSerializer>();
                gameObject.AddComponent<ShapeDocumentDeserializer>();
                var template = ScriptableObject.CreateInstance<MorphShapeTemplate>();
                try
                {
                    template.ShapeId = "morph";
                    template.Morphs.Add(new MorphValue { Target = "girl", Value = .2f });
                    Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(director.TrySerialize(fileName), Is.True);
                    ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName);
                    Assert.That(document.MeshBinding, Is.SameAs(meshBinding));
                    Assert.That(document.MaterialBinding, Is.SameAs(materialBinding));
                    Assert.That(document.MorphShapes[0].Morphs[0].Value, Is.EqualTo(.2f));
                    Assert.That(director.TryReplaceRuntimeShape(new MorphShape("morph", 0, null, new[] { new MorphValue { Target = "girl", Value = .9f } }), out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(director.TryDeserialize(fileName), Is.True);
                    Assert.That(((MorphShape)director.RuntimeShapes[0]).Morphs[0].Value, Is.EqualTo(.2f));
                }
                finally { Object.DestroyImmediate(template); }
            }
            finally { AssetDatabase.DeleteAsset(fileName); AssetDatabase.DeleteAsset(meshBindingFileName); AssetDatabase.DeleteAsset(materialBindingFileName); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void ShapeDirector_DeserializeWithAutoCompile_DispatchesSavedMaterialRestoreSource()
        {
            var gameObject = new GameObject("director");
            string fileName = AssetDatabase.GenerateUniqueAssetPath(ShapeSyncTestAssetPaths.ConsumerAssetPath("ShapeDirectorDeserializeRestoreTests.asset"));
            try
            {
                var director = gameObject.AddComponent<ShapeDirector>();
                var serializer = gameObject.AddComponent<ShapeDocumentSerializer>();
                var deserializer = gameObject.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(director.TryConfigureSerialization(serializer, deserializer, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(serializer.TrySerialize(fileName, new List<ShapeSyncShape>
                {
                    new SkinShape("skin", 0, null, new ShapeEntry[]
                    {
                        new TextureEntry { ProxyEntry = "iris", LogicalName = "iris", UseColor = true, Color = new Color32(171, 78, 140, 46) }
                    })
                }), Is.True);

                ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName);
                document.MaterialRecipe = new MaterialRecipeDocument
                {
                    wordSource = "FIGURE\nMATERIAL_RESET\nFIGURE\n$iris MATERIAL\nTEXTURE\n$iris CANVAS 0.3679993 0.8958215 0 COLORIZE\n.\nENDTEXTURE"
                };
                EditorUtility.SetDirty(document);
                AssetDatabase.SaveAssets();

                // No Material machine is co-located, so dispatch is expected to reject after the saved source is selected.
                Assert.That(director.TryDeserialize(fileName), Is.False);
                string materialSource = (string)typeof(ShapeDirector).GetField("lastMaterialSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(director);
                Assert.That(materialSource, Is.EqualTo(document.MaterialRecipe.wordSource));
            }
            finally { AssetDatabase.DeleteAsset(fileName); Object.DestroyImmediate(gameObject); }
        }
    }
}
