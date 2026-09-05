// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncMeshMaskLifecycleTests
    {
        [Test]
        public void AttachThenDetach_ChangesFigureMaskContributionOnly()
        {
            var requested = new List<ShapeSyncShape>
            {
                new OutfitShape("coat", 10, null, new ShapeEntry[]
                {
                    new MeshEntry { LogicalName = "coat-mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "coat-mask" } } }
                })
            };

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, out List<ShapeSyncShape> attached, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(ShapeSyncEntryMerge.TryMerge(attached, out List<ShapeSyncMergedEntry> attachedMesh, out List<ShapeSyncMergedEntry> attachedMaterial, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), attachedMaterial,
                new List<ShapeSyncMergedEntry>(), attachedMesh,
                out string attachSource, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(attachSource, Is.EqualTo("FIGURE\n$body MATERIAL\nTEXTURE\n$current CANVAS\n$coat-mask\nALPHA\n.\nENDTEXTURE"));

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                attachedMaterial, new List<ShapeSyncMergedEntry>(),
                attachedMesh, new List<ShapeSyncMergedEntry>(),
                out string detachSource, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(detachSource, Is.EqualTo("FIGURE\nMATERIAL_RESET"));
            Assert.That(detachSource, Does.Not.Contain("OUTFIT"));
        }

        [Test]
        public void ResolvedMultipleShapes_KeepMaskOrderAndExcludeTagSuppressedMesh()
        {
            var requested = new List<ShapeSyncShape>
            {
                new OutfitShape("outer", 20, new[] { "coat-slot" }, new ShapeEntry[]
                {
                    new MeshEntry { LogicalName = "outer-mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "outer-mask" } } }
                }),
                new OutfitShape("inner", 10, new[] { "coat-slot" }, new ShapeEntry[]
                {
                    new MeshEntry { LogicalName = "inner-mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "inner-mask" } } }
                }),
                new OutfitShape("hat", 30, null, new ShapeEntry[]
                {
                    new MeshEntry { LogicalName = "hat-mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "hat-mask" } } }
                })
            };

            Assert.That(ShapeSyncShapeResolver.TryResolve(requested, out List<ShapeSyncShape> physical, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(physical, Has.Count.EqualTo(2));
            Assert.That(physical[0].ShapeId, Is.EqualTo("outer"));
            Assert.That(physical[1].ShapeId, Is.EqualTo("hat"));
            Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out List<ShapeSyncMergedEntry> mesh, out List<ShapeSyncMergedEntry> material, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), material,
                new List<ShapeSyncMergedEntry>(), mesh,
                out string source, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Does.Contain("$outer-mask\n$hat-mask MULTIPLY\nALPHA"));
            Assert.That(source, Does.Not.Contain("inner-mask"));
        }

        [Test]
        public void MeshMaskLower_DoesNotCreateOutfitMaterialMaskTarget()
        {
            var mesh = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "coat-mesh", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "coat-mask" } } }, 0, "coat", 0)
            };
            var material = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { RegistryId = "coat", ProxyEntry = "cloth", LogicalName = "cloth-texture" }, 0, "coat", 1),
                new ShapeSyncMergedEntry(new ColorEntry { RegistryId = "coat", ProxyEntry = "cloth", Color = UnityEngine.Color.white }, 0, "coat", 2),
                new ShapeSyncMergedEntry(new UvsetEntry { RegistryId = "coat", ProxyEntry = "cloth", ScaleX = 1f, ScaleY = 1f }, 0, "coat", 3)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), material,
                new List<ShapeSyncMergedEntry>(), mesh,
                out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Does.Contain("FIGURE\n$body MATERIAL\nTEXTURE\n$current CANVAS\n$coat-mask\nALPHA"));
            Assert.That(source, Does.Contain("$coat OUTFIT\n$cloth MATERIAL"));
            Assert.That(source.IndexOf("$coat-mask"), Is.LessThan(source.IndexOf("$coat OUTFIT")));
        }
    }
}
