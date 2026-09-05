// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncMaterialRecipeCompilerTests
    {
        [Test]
        public void TryCompile_EmitsFigureTextureColorAndOutfitUvset()
        {
            var entries = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin" }, 0, "skin", 0),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "body", Color = new Color32(255, 128, 0, 255) }, 0, "skin", 1),
                new ShapeSyncMergedEntry(new UvsetEntry { RegistryId = "hair-1", ProxyEntry = "hair", ScaleX = 1, ScaleY = 1 }, 0, "hair", 0)
            };
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), entries, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Does.Contain("FIGURE\n$body MATERIAL\nTEXTURE\n$skin CANVAS\n.\nENDTEXTURE\n1 0.2158605 0 1 COLOR\n$hair-1 OUTFIT\n$hair MATERIAL\n1 1 0 0 UVSET"));
        }

        [Test]
        public void TryCompile_EmitsMeshOwnedMaskAfterTextureSourceOver()
        {
            var entries = new List<ShapeSyncMergedEntry> { new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin", UseColor = true, Color = new Color32(255, 0, 0, 128) }, 0, "skin", 0) };
            var mesh = new List<ShapeSyncMergedEntry> { new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "coat", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "mask" } } }, 0, "coat", 1) };
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), entries, new List<ShapeSyncMergedEntry>(), mesh, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Does.Contain("$skin CANVAS 0 1 0 COLORIZE\n$mask\nALPHA"));
            Assert.That(source, Does.Not.Contain("FILL"));
            Assert.That(source, Does.Not.Contain("MULTIPLY"));
        }

        [Test]
        public void TryCompile_EmitsMaskOnlyFromMeshEntryInDeterministicOrder()
        {
            var mesh = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "late", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "late-mask" } } }, 10, "late", 0),
                new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "early", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "early-mask" } } }, 0, "early", 1)
            };
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(), mesh, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\n$body MATERIAL\nTEXTURE\n$current CANVAS\n$early-mask\n$late-mask MULTIPLY\nALPHA\n.\nENDTEXTURE"));
        }

        [Test]
        public void TryCompile_RejectsInvalidMeshMaskEntry()
        {
            var mesh = new List<ShapeSyncMergedEntry> { new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "coat", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "" } } }, 0, "coat", 0) };
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(), mesh, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MaskLogicalNameRequired"));
        }

        [Test]
        public void TryCompile_RemovedMeshMaskResetsFigureTarget()
        {
            var currentMesh = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry { LogicalName = "coat", Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "coat-mask" } } }, 0, "coat", 0)
            };
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(), currentMesh, new List<ShapeSyncMergedEntry>(), out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\nMATERIAL_RESET"));
        }

        [Test]
        public void TryCompile_EmitsOnlyChangedMaterialTarget()
        {
            var current = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin" }, 0, "skin", 0),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "body", Color = new Color32(255, 0, 0, 255) }, 0, "skin", 1),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "face", Color = new Color32(0, 255, 0, 255) }, 0, "skin", 2)
            };
            var desired = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin" }, 0, "skin", 0),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "body", Color = new Color32(0, 0, 255, 255) }, 0, "skin", 1),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "face", Color = new Color32(0, 255, 0, 255) }, 0, "skin", 2)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(current, desired, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\n$body MATERIAL\n0 0 1 1 COLOR"));
        }

        [Test]
        public void TryCompile_RemovedMaterialSemanticResetsTargetAndReplaysDesiredGroups()
        {
            var current = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin" }, 0, "skin", 0),
                new ShapeSyncMergedEntry(new ColorEntry { ProxyEntry = "body", Color = Color.red }, 0, "skin", 1)
            };
            var desired = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new TextureEntry { ProxyEntry = "body", LogicalName = "skin" }, 0, "skin", 0)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(current, desired, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\nMATERIAL_RESET\n$body MATERIAL\nTEXTURE\n$skin CANVAS\n.\nENDTEXTURE"));
        }

        [Test]
        public void TryCompile_RemovedOutfitMaterialTargetResetsThatOutfit()
        {
            var current = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new ColorEntry { RegistryId = "hair-1", ProxyEntry = "hair", Color = Color.red }, 0, "hair", 0)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(current, new List<ShapeSyncMergedEntry>(), out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\n$hair-1 OUTFIT\nMATERIAL_RESET"));
        }

        [Test]
        public void TryCompileReset_EmitsFigureOnlyBeforeRecovery()
        {
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompileReset(out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\nMATERIAL_RESET"));
        }

        [Test]
        public void TryCompile_MaskOnlyGroupUsesCurrentCanvasAndMultipliesMasksInDeclarationOrder()
        {
            var mesh = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry
                {
                    LogicalName = "coat-mesh",
                    Masks =
                    {
                        new MeshMaskEntry { ProxyEntryName = "body", MaskName = "shirt-mask" },
                        new MeshMaskEntry { ProxyEntryName = "body", MaskName = "coat-mask" }
                    }
                }, 0, "coat", 0)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(),
                new List<ShapeSyncMergedEntry>(), mesh,
                out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("FIGURE\n$body MATERIAL\nTEXTURE\n$current CANVAS\n$shirt-mask\n$coat-mask MULTIPLY\nALPHA\n.\nENDTEXTURE"));
            Assert.That(source, Does.Not.Contain("ACOPY"));
        }

        [Test]
        public void TryCompile_RejectsIncompleteMeshMaskEntries()
        {
            var missingMask = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry
                {
                    LogicalName = "coat-mesh",
                    Masks = { new MeshMaskEntry { ProxyEntryName = "body", MaskName = "" } }
                }, 0, "coat", 0)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(),
                new List<ShapeSyncMergedEntry>(), missingMask,
                out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MaskLogicalNameRequired"));

            var missingProxy = new List<ShapeSyncMergedEntry>
            {
                new ShapeSyncMergedEntry(new MeshEntry
                {
                    LogicalName = "coat-mesh",
                    Masks = { new MeshMaskEntry { ProxyEntryName = "", MaskName = "coat-mask" } }
                }, 0, "coat", 0)
            };

            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(
                new List<ShapeSyncMergedEntry>(), new List<ShapeSyncMergedEntry>(),
                new List<ShapeSyncMergedEntry>(), missingProxy,
                out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialProxyEntryRequired"));
        }
    }
}
