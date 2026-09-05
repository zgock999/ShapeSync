// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasUvTransformTests
    {
        [Test]
        public void Apply_FoldsUvsetAndMapsCellCornersWithHalfTexelInset()
        {
            Assert.That(AtlasLayoutOracle.Solve(Document(), out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(layout.TryGetCell(new MaterialId("outfit", "skin"), out AtlasLayoutCell cell), Is.True);
            Vector2 min = AtlasUvTransform.Apply(Vector2.zero, Vector2.one, Vector2.zero, cell, layout.PageExtent);
            Vector2 max = AtlasUvTransform.Apply(Vector2.one, Vector2.one, Vector2.zero, cell, layout.PageExtent);
            Assert.That(min, Is.EqualTo(new Vector2((cell.X + cell.Gutter + .5f) / layout.PageExtent, (cell.Y + cell.Gutter + .5f) / layout.PageExtent)));
            Assert.That(max, Is.EqualTo(new Vector2((cell.X + cell.Width - cell.Gutter - .5f) / layout.PageExtent, (cell.Y + cell.Height - cell.Gutter - .5f) / layout.PageExtent)));
            Vector2 folded = AtlasUvTransform.Apply(new Vector2(.25f, .5f), new Vector2(2f, .5f), new Vector2(.1f, .2f), cell, layout.PageExtent);
            Vector2 direct = AtlasUvTransform.Apply(new Vector2(.6f, .45f), Vector2.one, Vector2.zero, cell, layout.PageExtent);
            Assert.That(folded, Is.EqualTo(direct));

            Vector2 repeated = AtlasUvTransform.Apply(new Vector2(.25f, .5f), new Vector2(2f, .5f), new Vector2(.1f, .2f), cell, layout.PageExtent);
            Assert.That(BitConverter.SingleToInt32Bits(repeated.x), Is.EqualTo(BitConverter.SingleToInt32Bits(folded.x)));
            Assert.That(BitConverter.SingleToInt32Bits(repeated.y), Is.EqualTo(BitConverter.SingleToInt32Bits(folded.y)));
            Assert.That(folded.x, Is.InRange(min.x, max.x));
            Assert.That(folded.y, Is.InRange(min.y, max.y));

            Vector2 unclamped = AtlasUvTransform.Apply(Vector2.zero, Vector2.one, new Vector2(-.25f, 1.25f), cell, layout.PageExtent);
            Assert.That(unclamped.x, Is.EqualTo(min.x + (max.x - min.x) * -.25f));
            Assert.That(unclamped.y, Is.EqualTo(min.y + (max.y - min.y) * 1.25f));
            Assert.That(unclamped.x, Is.LessThan(min.x));
            Assert.That(unclamped.y, Is.GreaterThan(max.y));

            Assert.That(layout.SemanticPages, Has.Exactly(1).Matches<AtlasSemanticPage>(page => page.PageIndex == cell.PageIndex && page.Semantic == AtlasTextureSemantic.BaseColor));
            Assert.That(layout.SemanticPages, Has.Exactly(1).Matches<AtlasSemanticPage>(page => page.PageIndex == cell.PageIndex && page.Semantic == AtlasTextureSemantic.Normal));
            Vector2 baseColorUv = AtlasUvTransform.Apply(new Vector2(.2f, .8f), new Vector2(.5f, .75f), new Vector2(.25f, -.125f), cell, layout.PageExtent);
            Vector2 normalUv = AtlasUvTransform.Apply(new Vector2(.2f, .8f), new Vector2(.5f, .75f), new Vector2(.25f, -.125f), cell, layout.PageExtent);
            Assert.That(BitConverter.SingleToInt32Bits(baseColorUv.x), Is.EqualTo(BitConverter.SingleToInt32Bits(normalUv.x)));
            Assert.That(BitConverter.SingleToInt32Bits(baseColorUv.y), Is.EqualTo(BitConverter.SingleToInt32Bits(normalUv.y)));
        }

        private static AtlasSchemaDocument Document()
        {
            MaterialId id = new MaterialId("outfit", "skin");
            var entries = new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 4) };
            var sources = new List<AtlasSourceMaterialIdentity> { new AtlasSourceMaterialIdentity(id, "source") };
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", sources), entries);
        }
    }
}
