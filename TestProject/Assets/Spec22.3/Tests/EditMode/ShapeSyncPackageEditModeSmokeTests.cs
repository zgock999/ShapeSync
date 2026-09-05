// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using zgock.ShapeSync;

namespace ShapeSync.Spec22_3.Tests
{
    public sealed class ShapeSyncPackageEditModeSmokeTests
    {
        [Test]
        public void CoreRuntimeAssemblyLoadsFromLocalPackage()
        {
            Assert.That(typeof(ShapeSyncOutfit).Assembly.GetName().Name, Is.EqualTo("zgock.ShapeSync.Runtime"));
        }

        [Test]
        public void PackageManifestsResolveFromUnityPackageManager()
        {
            PackageInfo core = PackageInfo.FindForPackageName("net.zgock-lab.shapesync");
            Assert.That(core, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(core.resolvedPath, "package.json")), Is.True);

#if SHAPESYNC_USE_UNIVRM
            PackageInfo companion = PackageInfo.FindForPackageName("net.zgock-lab.shapesync.vrm");
            Assert.That(companion, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(companion.resolvedPath, "package.json")), Is.True);
#endif
        }

#if !SHAPESYNC_USE_UNIVRM
        [Test]
        public void CoreSmokeAssemblyIsCompiledWithoutVrmDefine()
        {
            Assert.Pass();
        }
#endif
    }
}
