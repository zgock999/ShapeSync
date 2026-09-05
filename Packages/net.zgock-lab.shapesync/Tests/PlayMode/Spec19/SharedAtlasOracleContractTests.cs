// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.StackMachine.Tests.Spec18;
using zgock.ShapeSync.StackMachine.Tests.Spec17;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class SharedAtlasOracleContractTests
    {
        [Test]
        public void AtlasOracleTolerance_IsAvailableWithTheSpec18ClosedValue()
        {
            AtlasImageOracle.PixelTolerance tolerance = AtlasOracleTolerances.Default;

            Assert.That(tolerance.LinearRelative, Is.EqualTo(1e-3f));
            Assert.That(tolerance.SrgbAbsolute, Is.EqualTo(2f / 255f));
        }

        [Test]
        public void SharedAssembly_ExposesAllOracleLayersAndMeshExpectationToPlayMode()
        {
            System.Reflection.Assembly assembly = typeof(AtlasImageOracle).Assembly;

            Assert.That(typeof(AtlasCrossOracle).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(AtlasImageMetamorphicOracle).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(AtlasLayoutPropertyOracle).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(AtlasMeshStructureOracle).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(AtlasOracleFixture).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(HumanoidMeshStructureFixture).Assembly, Is.SameAs(assembly));
            Assert.That(typeof(HumanoidMeshStructureExpectation).Assembly, Is.SameAs(assembly));
        }
    }
}
