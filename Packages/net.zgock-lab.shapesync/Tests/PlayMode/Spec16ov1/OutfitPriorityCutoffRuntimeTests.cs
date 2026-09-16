// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class OutfitPriorityCutoffRuntimeTests
    {
        [Test]
        public void T10_AutoCompileCutoffChangesCommittedPhysicalShapes()
        {
            CreateFixture(out GameObject host, out ShapeDirector director, out List<ShapeSyncShapeTemplate> templates);
            try
            {
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Maid");

                int commits = 0;
                director.TransactionCommitted += () => commits++;
                director.OutfitPriorityCutoff = 40;

                Assert.That(commits, Is.EqualTo(1));
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Skirt1", "Shirt1");
            }
            finally
            {
                DestroyFixture(host, templates);
            }
        }

        [Test]
        public void T11_AutoCompileDisabledCutoffDoesNotCommit()
        {
            CreateFixture(out GameObject host, out ShapeDirector director, out List<ShapeSyncShapeTemplate> templates);
            try
            {
                int commits = 0;
                director.AutoCompile = false;
                director.TransactionCommitted += () => commits++;
                director.OutfitPriorityCutoff = 40;

                Assert.That(commits, Is.EqualTo(0));
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Maid");
            }
            finally
            {
                DestroyFixture(host, templates);
            }
        }

        [Test]
        public void T12_AssigningSameCutoffDoesNotCommit()
        {
            CreateFixture(out GameObject host, out ShapeDirector director, out List<ShapeSyncShapeTemplate> templates);
            try
            {
                int commits = 0;
                director.TransactionCommitted += () => commits++;
                director.OutfitPriorityCutoff = 40;
                Assert.That(commits, Is.EqualTo(1));

                commits = 0;
                director.OutfitPriorityCutoff = 40;

                Assert.That(commits, Is.EqualTo(0));
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Skirt1", "Shirt1");
            }
            finally
            {
                DestroyFixture(host, templates);
            }
        }

        [Test]
        public void T13_CutoffDoesNotMutateRuntimeShapes()
        {
            CreateFixture(out GameObject host, out ShapeDirector director, out List<ShapeSyncShapeTemplate> templates);
            try
            {
                var beforeIds = new string[director.RuntimeShapes.Count];
                var beforePriorities = new int[director.RuntimeShapes.Count];
                for (int i = 0; i < director.RuntimeShapes.Count; i++)
                {
                    beforeIds[i] = director.RuntimeShapes[i].ShapeId;
                    beforePriorities[i] = director.RuntimeShapes[i].Priority;
                }

                director.OutfitPriorityCutoff = 40;

                Assert.That(director.RuntimeShapes.Count, Is.EqualTo(beforeIds.Length));
                for (int i = 0; i < director.RuntimeShapes.Count; i++)
                {
                    Assert.That(director.RuntimeShapes[i].ShapeId, Is.EqualTo(beforeIds[i]));
                    Assert.That(director.RuntimeShapes[i].Priority, Is.EqualTo(beforePriorities[i]));
                }
            }
            finally
            {
                DestroyFixture(host, templates);
            }
        }

        private static void CreateFixture(
            out GameObject host,
            out ShapeDirector director,
            out List<ShapeSyncShapeTemplate> templates)
        {
            host = new GameObject("Spec16ov1 Outfit Priority Cutoff");
            director = host.AddComponent<ShapeDirector>();
            director.AutoCompile = false;
            templates = new List<ShapeSyncShapeTemplate>();

            AddOutfit(director, templates, "Maid", 50, "upperbody", "lowerbody");
            AddOutfit(director, templates, "Shirt1", 40, "upperbody");
            AddOutfit(director, templates, "Skirt1", 30, "lowerbody");
            AddOutfit(director, templates, "Shoes2", 0, "foot");

            director.AutoCompile = true;
            Assert.That(director.TryCompile(out _), Is.True);
        }

        private static void AddOutfit(
            ShapeDirector director,
            List<ShapeSyncShapeTemplate> templates,
            string shapeId,
            int priority,
            params string[] tags)
        {
            var template = ScriptableObject.CreateInstance<OutfitShapeTemplate>();
            template.ShapeId = shapeId;
            template.Priority = priority;
            template.Tags.AddRange(tags);
            Assert.That(director.TryAddTemplate(template, out _), Is.True);
            templates.Add(template);
        }

        private static void AssertIds(IReadOnlyList<ShapeSyncShape> actual, params string[] expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].ShapeId, Is.EqualTo(expected[i]));
            }
        }

        private static void DestroyFixture(GameObject host, List<ShapeSyncShapeTemplate> templates)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                Object.DestroyImmediate(templates[i]);
            }

            Object.DestroyImmediate(host);
        }

        [Test]
        public void T14_LoadWithCutoffCommitsDocumentThenOneCutoffRecompile()
        {
            CreateLoadFixture(40, out GameObject host, out ShapeDirector director, out ShapeDocument document);
            try
            {
                int commits = 0;
                director.TransactionCommitted += () => commits++;

                Assert.That(director.TryLoadDocument(document, out _), Is.True);
                Assert.That(commits, Is.EqualTo(2));
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Skirt1", "Shirt1");
            }
            finally
            {
                DestroyLoadFixture(host, document);
            }
        }

        [Test]
        public void T15_LoadWithoutCutoffCommitsDocumentOnly()
        {
            CreateLoadFixture(-1, out GameObject host, out ShapeDirector director, out ShapeDocument document);
            try
            {
                int commits = 0;
                director.TransactionCommitted += () => commits++;

                Assert.That(director.TryLoadDocument(document, out _), Is.True);
                Assert.That(commits, Is.EqualTo(1));
                AssertIds(director.CurrentPhysicalShapes, "Shoes2", "Maid");
            }
            finally
            {
                DestroyLoadFixture(host, document);
            }
        }

        private static void CreateLoadFixture(
            int cutoff,
            out GameObject host,
            out ShapeDirector director,
            out ShapeDocument document)
        {
            host = new GameObject("Spec16ov1 Load Cutoff");
            director = host.AddComponent<ShapeDirector>();
            host.AddComponent<ShapeDocumentDeserializer>();
            director.AutoCompile = false;
            director.OutfitPriorityCutoff = cutoff;

            document = ScriptableObject.CreateInstance<ShapeDocument>();
            var outfits = new List<SerializedOutfitShape>
            {
                CreateSerializedOutfit("Maid", 50, 0, "upperbody", "lowerbody"),
                CreateSerializedOutfit("Shirt1", 40, 1, "upperbody"),
                CreateSerializedOutfit("Skirt1", 30, 2, "lowerbody"),
                CreateSerializedOutfit("Shoes2", 0, 3, "foot")
            };
            document.ReplaceShapes(null, null, null, outfits);
            director.AutoCompile = true;
        }

        private static SerializedOutfitShape CreateSerializedOutfit(
            string shapeId,
            int priority,
            int listPosition,
            params string[] tags)
        {
            var outfit = new SerializedOutfitShape
            {
                ShapeId = shapeId,
                Priority = priority,
                ListPosition = listPosition
            };
            outfit.Tags.AddRange(tags);
            return outfit;
        }

        private static void DestroyLoadFixture(GameObject host, ShapeDocument document)
        {
            Object.DestroyImmediate(document);
            Object.DestroyImmediate(host);
        }
    }
}
