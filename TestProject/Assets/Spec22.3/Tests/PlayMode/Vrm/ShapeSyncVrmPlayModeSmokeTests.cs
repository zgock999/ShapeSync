// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UniVRM10;
using zgock.ShapeSync.VrmIntegration;

namespace ShapeSync.Spec22_3.Tests
{
    public sealed class ShapeSyncVrmPlayModeSmokeTests
    {
        [Test]
        public void VrmCompanionAndUniVrmAssembliesLoadInPlayMode()
        {
            Assert.That(typeof(VrmIntegrationService).Assembly.GetName().Name, Is.EqualTo("zgock.ShapeSync.VrmIntegration.Runtime"));
            Assert.That(typeof(Vrm10Instance).Assembly.GetName().Name, Is.Not.Empty);
        }
    }
}
