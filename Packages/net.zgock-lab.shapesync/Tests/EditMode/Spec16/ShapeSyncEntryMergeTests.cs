// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncEntryMergeTests
    {
        [Test]
        public void TryMerge_PartitionsAndSortsMaterialEntries()
        {
            var physical = new List<ShapeSyncShape> { new OutfitShape("coat", 10, null, new ShapeEntry[] {
                new UvsetEntry { RegistryId = "outfit", ProxyEntry = "body" }, new MeshEntry { LogicalName = "coat", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "coat-mask" } } },
                new TextureEntry { RegistryId = "", ProxyEntry = "body", LogicalName = "skin" }, new ColorEntry { RegistryId = "", ProxyEntry = "body" } }) };
            Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out List<ShapeSyncMergedEntry> mesh, out List<ShapeSyncMergedEntry> material, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(mesh, Has.Count.EqualTo(1)); Assert.That(((MeshEntry)mesh[0].Entry).Masks[0].MaskName, Is.EqualTo("coat-mask")); Assert.That(material[0].Entry, Is.TypeOf<TextureEntry>()); Assert.That(material[1].Entry, Is.TypeOf<ColorEntry>()); Assert.That(material[2].Entry, Is.TypeOf<UvsetEntry>());
        }

        [Test]
        public void TryMerge_RejectsDuplicateColorForSameTarget()
        {
            var physical = new List<ShapeSyncShape> { new SkinShape("skin", 0, null, new ShapeEntry[] { new ColorEntry { ProxyEntry = "body" }, new ColorEntry { ProxyEntry = "body" } }) };
            Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out _, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateColorEntry"));
        }

        [Test]
        public void TryMerge_UsesShapeIdAsFinalTextureOrderTieBreak()
        {
            var physical = new List<ShapeSyncShape>
            {
                new SkinShape("zeta", 0, null, new ShapeEntry[] { new TextureEntry { ProxyEntry = "body", LogicalName = "z" } }),
                new SkinShape("alpha", 0, null, new ShapeEntry[] { new TextureEntry { ProxyEntry = "body", LogicalName = "a" } })
            };

            Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out _, out List<ShapeSyncMergedEntry> material, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(material[0].ShapeId, Is.EqualTo("alpha"));
            Assert.That(material[1].ShapeId, Is.EqualTo("zeta"));
        }
    }
}
