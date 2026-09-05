// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Database-local marker for an optional authoring feature.
    ///
    /// The marker deliberately contains no optional-package type.  It remains
    /// loadable when the package that owns the feature is absent, allowing the
    /// Database admission layer to reject a destructive open before Unity has a
    /// chance to round-trip the missing scripts.
    /// </summary>
    public sealed class ShapeSyncDatabaseOptionalFeatureMarker : ScriptableObject
    {
        [SerializeField] private string featureId;

        /// <summary>Gets the stable optional feature identifier.</summary>
        /// <value>The feature identifier stored in this Database-local marker.</value>
        public string FeatureId => featureId;

        /// <summary>Creates a marker for one optional feature.</summary>
        /// <param name="value">The stable identifier of the optional feature.</param>
        /// <returns>A new unsaved marker containing <paramref name="value"/>.</returns>
        /// <exception cref="System.ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
        public static ShapeSyncDatabaseOptionalFeatureMarker Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new System.ArgumentException("Optional feature marker requires a feature id.", nameof(value));
            }

            ShapeSyncDatabaseOptionalFeatureMarker marker = ScriptableObject.CreateInstance<ShapeSyncDatabaseOptionalFeatureMarker>();
            marker.featureId = value;
            return marker;
        }
    }
}
