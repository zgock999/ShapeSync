// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncShapeResolverTests
    {
        [Test]
        public void TryResolve_UsesPriorityAndShapeIdForExclusionThenPhysicalOrder()
        {
            var requested = new List<ShapeSyncShape>
            {
                new SkinShape("coat", 20, new[] { "outer" }, null),
                new HairShape("hair-b", 10, null, null),
                new OutfitShape("jacket", 30, new[] { "outer" }, null),
                new HairShape("hair-a", 10, null, null)
            };

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(physical, Has.Count.EqualTo(3));
            Assert.That(physical[0].ShapeId, Is.EqualTo("hair-a"));
            Assert.That(physical[1].ShapeId, Is.EqualTo("hair-b"));
            Assert.That(physical[2].ShapeId, Is.EqualTo("jacket"));
        }

        [Test]
        public void TryResolve_RejectsDuplicateShapeId()
        {
            var requested = new List<ShapeSyncShape>
            {
                new SkinShape("duplicate", 0, null, null),
                new HairShape("duplicate", 1, null, null)
            };

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(physical, Is.Empty);
            Assert.That(diagnostic.domain, Is.EqualTo("director"));
            Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateShapeId"));
        }

        [Test]
        public void RuntimeShapeClone_DeepCopiesTagsAndParts()
        {
            var source = new OutfitShape(
                "outfit",
                5,
                new[] { "wear" },
                new ShapeEntry[] { new MeshEntry { LogicalName = "mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "mask" } } }, new TextureEntry { LogicalName = "texture", UseColor = true, Color = new UnityEngine.Color32(1, 2, 3, 4) } });

            var clone = (OutfitShape)source.Clone();

            Assert.That(clone, Is.Not.SameAs(source));
            Assert.That(clone.Tags, Is.Not.SameAs(source.Tags));
            Assert.That(clone.Parts, Is.Not.SameAs(source.Parts));
            Assert.That(clone.Parts[0], Is.Not.SameAs(source.Parts[0]));
            Assert.That(((MeshEntry)clone.Parts[0]).Masks[0].ProxyEntryName, Is.EqualTo("body"));
            Assert.That(((MeshEntry)clone.Parts[0]).Masks[0], Is.Not.SameAs(((MeshEntry)source.Parts[0]).Masks[0]));
        }

        [Test]
        public void MeshMaskSchema_ExposesProxyEntryNameAndTextureEntryHasNoMaskApi()
        {
            Assert.That(typeof(MeshMaskEntry).GetProperty(nameof(MeshMaskEntry.ProxyEntryName)), Is.Not.Null);
            Assert.That(typeof(MeshMaskEntry).GetProperty(nameof(MeshMaskEntry.MaskName)), Is.Not.Null);
            Assert.That(typeof(MeshEntry).GetProperty(nameof(MeshEntry.Masks)), Is.Not.Null);
            Assert.That(typeof(TextureEntry).GetProperty("MaskName"), Is.Null);
        }
    }
}
