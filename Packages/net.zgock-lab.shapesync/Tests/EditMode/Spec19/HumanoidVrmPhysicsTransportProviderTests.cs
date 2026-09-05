// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>Core-only acceptance for the optional runtime UniVRM provider boundary.</summary>
    public sealed class HumanoidVrmPhysicsTransportProviderTests
    {
        private static readonly FieldInfo Factory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void Provider_UsesNoVrmFallbackUntilOptionalAssemblyRegisters()
        {
            Assert.That(Factory, Is.Not.Null);
            object original = Factory.GetValue(null);
            try
            {
                Factory.SetValue(null, null);
                Assert.That(HumanoidVrmPhysicsTransportProvider.IsAvailable, Is.False);
                Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter unavailable), Is.False);
                Assert.That(unavailable, Is.Null);

                HumanoidVrmPhysicsTransportProvider.Register(() => new FakeTransporter());
                Assert.That(HumanoidVrmPhysicsTransportProvider.IsAvailable, Is.True);
                Assert.That(HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter registered), Is.True);
                Assert.That(registered, Is.TypeOf<FakeTransporter>());
            }
            finally { Factory.SetValue(null, original); }
        }

        private sealed class FakeTransporter : IHumanoidVrmPhysicsTransporter
        {
            public bool TryTransport(GameObject candidateRoot, GameObject figureSourceRoot, IReadOnlyList<GameObject> attachedOutfitSourceRoots, out IDisposable ownership, out StackMachineDiagnostic diagnostic)
            {
                ownership = null;
                diagnostic = StackMachineDiagnostic.CreateDomain("test", "FakeTransporter", "Fake transporter does not execute VRM transport.");
                return false;
            }
        }
    }
}
