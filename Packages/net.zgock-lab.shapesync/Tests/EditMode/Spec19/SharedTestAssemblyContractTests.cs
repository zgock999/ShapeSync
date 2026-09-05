// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.Tests.Shared;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class SharedTestAssemblyContractTests
    {
        [Test]
        public void SharedAssembly_IsAvailableToEditMode()
        {
            SharedTestAssemblyContract.AssertRuntimeAndNUnitAreAvailable();
        }
    }
}
