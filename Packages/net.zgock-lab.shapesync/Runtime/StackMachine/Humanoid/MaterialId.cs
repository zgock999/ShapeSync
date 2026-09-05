// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Stable cross-domain key for one Figure or Outfit MaterialProxy entry.</summary>
    public readonly struct MaterialId : IEquatable<MaterialId>
    {
        /// <summary>Creates an ID. Figure uses an empty registry ID; Outfit uses its non-empty RegistryId.</summary>
        public MaterialId(string registryId, string entryId)
        {
            RegistryId = registryId ?? string.Empty;
            EntryId = entryId ?? string.Empty;
        }

        public string RegistryId { get; }
        public string EntryId { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(EntryId);
        public bool Equals(MaterialId other) => string.Equals(RegistryId, other.RegistryId, StringComparison.Ordinal) && string.Equals(EntryId, other.EntryId, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is MaterialId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(RegistryId), StringComparer.Ordinal.GetHashCode(EntryId));
        public override string ToString() => string.IsNullOrEmpty(RegistryId) ? EntryId : RegistryId + "/" + EntryId;
        public static bool operator ==(MaterialId left, MaterialId right) => left.Equals(right);
        public static bool operator !=(MaterialId left, MaterialId right) => !left.Equals(right);
    }
}
