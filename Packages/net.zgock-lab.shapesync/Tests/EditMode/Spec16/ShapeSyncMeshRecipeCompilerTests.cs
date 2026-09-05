// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncMeshRecipeCompilerTests
    {
        [Test]
        public void TryCompile_OrdersDetachMorphAttach()
        {
            var current = new List<ShapeSyncShape> { new OutfitShape("old", 0, null, new ShapeEntry[] { new MeshEntry { LogicalName = "old" } }), new MorphShape("m", 0, null, new[] { new MorphValue { Target = "girl", Value = 0f } }) };
            var desired = new List<ShapeSyncShape> { new OutfitShape("new", 0, null, new ShapeEntry[] { new MeshEntry { LogicalName = "new" } }), new MorphShape("m", 0, null, new[] { new MorphValue { Target = "girl", Value = .7f } }) };
            Assert.That(ShapeSyncMeshRecipeCompiler.TryCompile(current, desired, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("$old DETACH\n$girl 0.7 FBM_SET\n$new ATTACH"));
        }

        [Test]
        public void TryCompile_OrdersMorphTargetsOrdinally()
        {
            var desired = new List<ShapeSyncShape>
            {
                new MorphShape("m", 0, null, new[] { new MorphValue { Target = "zeta", Value = .1f }, new MorphValue { Target = "alpha", Value = .2f } })
            };

            Assert.That(ShapeSyncMeshRecipeCompiler.TryCompile(new List<ShapeSyncShape>(), desired, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("$alpha 0.2 FBM_SET\n$zeta 0.1 FBM_SET"));
        }

        [Test]
        public void TryCompile_ResetsCurrentOnlyMorphTargetToZero()
        {
            var current = new List<ShapeSyncShape> { new MorphShape("m", 0, null, new[] { new MorphValue { Target = "girl", Value = .7f } }) };

            Assert.That(ShapeSyncMeshRecipeCompiler.TryCompile(current, new List<ShapeSyncShape>(), out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Is.EqualTo("$girl 0 FBM_SET"));
        }

        [Test]
        public void TryCompile_RejectsDuplicateMeshLogicalName()
        {
            var desired = new List<ShapeSyncShape>
            {
                new OutfitShape("a", 0, null, new ShapeEntry[] { new MeshEntry { LogicalName = "hair" } }),
                new OutfitShape("b", 1, null, new ShapeEntry[] { new MeshEntry { LogicalName = "hair" } })
            };

            Assert.That(ShapeSyncMeshRecipeCompiler.TryCompile(new List<ShapeSyncShape>(), desired, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateMeshEntry"));
        }
    }
}
