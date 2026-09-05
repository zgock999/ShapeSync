// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.Shared
{
    /// <summary>Verifies the dependency boundary used by shared Oracle and Mesh expectations.</summary>
    public static class SharedTestAssemblyContract
    {
        public static void AssertRuntimeAndNUnitAreAvailable()
        {
            Assert.That(typeof(TextureStackMachineHost).Assembly.GetName().Name, Is.EqualTo("zgock.ShapeSync.Runtime"));
        }
    }
}
