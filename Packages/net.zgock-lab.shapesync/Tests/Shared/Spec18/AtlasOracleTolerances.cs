// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Owns the single image-comparison tolerance shared by Atlas Oracle tests in every mode.</summary>
    internal static class AtlasOracleTolerances
    {
        internal static readonly AtlasImageOracle.PixelTolerance Default = new AtlasImageOracle.PixelTolerance(1e-3f, 2f / 255f);
    }
}
