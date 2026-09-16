// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class OutfitPriorityCutoffTests
    {
        [Test]
        public void T01_DisabledCutoff_PreservesExistingResolution()
        {
            AssertResolution(new[] { "Shoes2", "Maid" }, CreateBaseShapes(), -1);
        }

        [Test]
        public void T02_Cutoff40_RemovesOutfitsAboveCutoff()
        {
            AssertResolution(new[] { "Shoes2", "Skirt1", "Shirt1" }, CreateBaseShapes(), 40);
        }

        [Test]
        public void T03_Cutoff0_RetainsPriorityZeroOutfit()
        {
            AssertResolution(new[] { "Shoes2" }, CreateBaseShapes(), 0);
        }

        [Test]
        public void T04_NegativeCutoff_PreservesExistingResolution()
        {
            AssertResolution(new[] { "Shoes2", "Maid" }, CreateBaseShapes(), -5);
        }

        [Test]
        public void T05_Cutoff0_DoesNotRemoveHair()
        {
            var requested = CreateBaseShapes();
            requested.Add(new HairShape("TwinTail", 10, new[] { "hair" }, null));

            AssertResolution(new[] { "Shoes2", "TwinTail" }, requested, 0);
        }

        [Test]
        public void T06_Cutoff50_RetainsPriorityEqualToCutoff()
        {
            AssertResolution(new[] { "Shoes2", "Maid" }, CreateBaseShapes(), 50);
        }

        [Test]
        public void T07_DuplicateShapeId_IsRejectedBeforeCutoff()
        {
            var requested = new List<ShapeSyncShape>
            {
                new OutfitShape("Duplicate", 50, new[] { "upperbody" }, null),
                new OutfitShape("Duplicate", 0, new[] { "foot" }, null)
            };

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, 0, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.False, diagnostic?.message);
            Assert.That(physical, Is.Empty);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateShapeId"));
        }

        [Test]
        public void T08_RepeatedResolution_ProducesIdenticalPhysicalOrder()
        {
            var requested = CreateBaseShapes();

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, 40, out List<ShapeSyncShape> first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, 40, out List<ShapeSyncShape> second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            Assert.That(second, Has.Count.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].ShapeId, Is.EqualTo(first[i].ShapeId));
                Assert.That(second[i].Priority, Is.EqualTo(first[i].Priority));
            }
        }

        private static List<ShapeSyncShape> CreateBaseShapes()
        {
            return new List<ShapeSyncShape>
            {
                new OutfitShape("Maid", 50, new[] { "upperbody", "lowerbody" }, null),
                new OutfitShape("Shirt1", 40, new[] { "upperbody" }, null),
                new OutfitShape("Skirt1", 30, new[] { "lowerbody" }, null),
                new OutfitShape("Shoes2", 0, new[] { "foot" }, null)
            };
        }

        private static void AssertResolution(string[] expectedShapeIds, IReadOnlyList<ShapeSyncShape> requested, int cutoff)
        {
            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, cutoff, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(physical, Has.Count.EqualTo(expectedShapeIds.Length));
            for (int i = 0; i < expectedShapeIds.Length; i++)
            {
                Assert.That(physical[i].ShapeId, Is.EqualTo(expectedShapeIds[i]));
            }
            Assert.That(diagnostic, Is.Null);
        }

        [Test]
        public void T16_ShapeDirectorInspectorFindsOutfitPriorityCutoffField()
        {
            var host = new UnityEngine.GameObject("Spec16ov1 Inspector Cutoff");
            try
            {
                var director = host.AddComponent<ShapeDirector>();
                var serializedObject = new UnityEditor.SerializedObject(director);
                Assert.That(serializedObject.FindProperty("outfitPriorityCutoff"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
