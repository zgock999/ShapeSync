// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class BlendShapeReservedPrefixesTests
    {
        [TestCase("FBM_Target")]
        [TestCase("PBM_BreastSize")]
        [TestCase("PCM_Shoes")]
        [TestCase("MCM_Target_Smile")]
        [TestCase("VRM_happy")]
        [TestCase("Morph_Slot_0")]
        public void IsReserved_RecognizesEveryReservedPrefix(string value)
        {
            Assert.That(zgock.ShapeSync.BlendShapeReservedPrefixes.IsReserved(value), Is.True);
        }

        [Test]
        public void IsReserved_DoesNotRecognizeOrdinaryExpression()
        {
            Assert.That(zgock.ShapeSync.BlendShapeReservedPrefixes.IsReserved("Smile"), Is.False);
        }

        [Test]
        public void IsMorphSlot_RecognizesOnlyMorphSlotPrefix()
        {
            Assert.That(zgock.ShapeSync.BlendShapeReservedPrefixes.IsMorphSlot("Morph_Slot_10"), Is.True);
            Assert.That(zgock.ShapeSync.BlendShapeReservedPrefixes.IsMorphSlot("PCM_Shoes"), Is.False);
        }
    }

}
