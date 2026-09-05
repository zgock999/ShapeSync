// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class OutfitSkinningProfileBcpBakeTests
    {
        [Test]
        public void BcpBakeMarker_DefaultsFalseForExistingProfiles()
        {
            OutfitSkinningProfile profile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            try
            {
                Assert.That(profile.UsesBcpBakedBindposes, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void BcpBakeMarker_ExplicitlySelectsStaticBindposeOutput()
        {
            OutfitSkinningProfile profile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            try
            {
                profile.SetUsesBcpBakedBindposesForEditor(true);
                Assert.That(profile.UsesBcpBakedBindposes, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
