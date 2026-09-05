// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Defines the reserved BlendShape name prefixes shared by ShapeSync Runtime,
    /// Editor, VRM Integration, and tests.
    /// </summary>
    public static class BlendShapeReservedPrefixes
    {
        public const string Fbm = "FBM_";
        public const string Pbm = "PBM_";
        public const string Pcm = "PCM_";
        public const string Mcm = "MCM_";
        public const string Vrm = "VRM_";
        public const string MorphSlot = "Morph_Slot_";

        public static bool IsMorphSlot(string value)
        {
            return !string.IsNullOrEmpty(value)
                && value.StartsWith(MorphSlot, StringComparison.Ordinal);
        }

        public static bool IsReserved(string value)
        {
            return !string.IsNullOrEmpty(value)
                && (value.StartsWith(Fbm, StringComparison.Ordinal)
                    || value.StartsWith(Pbm, StringComparison.Ordinal)
                    || value.StartsWith(Pcm, StringComparison.Ordinal)
                    || value.StartsWith(Mcm, StringComparison.Ordinal)
                    || value.StartsWith(Vrm, StringComparison.Ordinal)
                    || value.StartsWith(MorphSlot, StringComparison.Ordinal));
        }
    }
}
