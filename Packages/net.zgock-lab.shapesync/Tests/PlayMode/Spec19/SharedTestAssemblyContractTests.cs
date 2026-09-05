// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.Tests.Shared;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class SharedTestAssemblyContractTests
    {
        [Test]
        public void SharedAssembly_IsAvailableToPlayMode()
        {
            SharedTestAssemblyContract.AssertRuntimeAndNUnitAreAvailable();
        }
    }
}
