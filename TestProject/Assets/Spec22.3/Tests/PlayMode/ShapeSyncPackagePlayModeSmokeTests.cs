// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;

namespace ShapeSync.Spec22_3.Tests
{
    public sealed class ShapeSyncPackagePlayModeSmokeTests
    {
        [Test]
        public void CoreRuntimeAssemblyLoadsInPlayModeAssembly()
        {
            Assert.That(typeof(ShapeSyncOutfit).Assembly.GetName().Name, Is.EqualTo("zgock.ShapeSync.Runtime"));
        }

#if !SHAPESYNC_USE_UNIVRM
        [Test]
        public void CorePlayModeSmokeAssemblyIsCompiledWithoutVrmDefine()
        {
            Assert.Pass();
        }
#endif
    }
}
