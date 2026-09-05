// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;

using NUnit.Framework;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class Spec3OptionalVrmBoundaryTests
    {
        [Test]
        public void OptionalVrmAttachmentBoundary_DoesNotExposeConcreteUniVrmAttachmentFromRegistryState()
        {
            Type attachmentContract = FindRuntimeType("zgock.ShapeSync.IShapeSyncOptionalVrmAttachment");
            Type registryType = FindRuntimeType("zgock.ShapeSync.AttachedOutfitRegistrySet");
            Assert.That(attachmentContract, Is.Not.Null);
            Assert.That(registryType, Is.Not.Null);
            Assert.That(registryType.GetProperty("SpringBoneAttachment", BindingFlags.Instance | BindingFlags.Public).PropertyType, Is.EqualTo(attachmentContract));
        }

        private static Type FindRuntimeType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
