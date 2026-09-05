// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Optional Database admission seam.  The core Editor assembly owns the
    /// seam; an optional integration registers the concrete validator without
    /// creating a reverse assembly reference.
    /// </summary>
    public static class ShapeSyncDatabaseOptionalRegistryProvider
    {
        private const string VrmFeatureId = "VRM";
        private static Func<string, ShapeSyncDatabaseDiagnostic> vrmValidator;
        private static VrmGenerateDelegate vrmGenerate;
        private static VrmGenerateFinalizeDelegate vrmGenerateFinalize;

        /// <summary>Core-safe Generate callback supplied by an optional integration.</summary>
        /// <param name="database">The opened Database being generated.</param>
        /// <param name="rootPath">The project-relative Generate root.</param>
        /// <param name="generatedPaths">The transaction-owned list of generated project paths.</param>
        /// <param name="diagnostic">Receives a diagnostic when the optional Generate stage fails.</param>
        /// <returns><see langword="true"/> when the optional Generate stage succeeds; otherwise, <see langword="false"/>.</returns>
        public delegate bool VrmGenerateDelegate(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic);

        /// <summary>Core-safe final Generate callback supplied by an optional integration.</summary>
        /// <param name="database">The opened Database whose generated assets are being finalized.</param>
        /// <param name="rootPath">The project-relative Generate root.</param>
        /// <param name="generatedPaths">The transaction-owned list of generated project paths.</param>
        /// <param name="diagnostic">Receives a diagnostic when the optional finalization stage fails.</param>
        /// <returns><see langword="true"/> when finalization succeeds; otherwise, <see langword="false"/>.</returns>
        public delegate bool VrmGenerateFinalizeDelegate(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic);

        /// <summary>Registers the validator for Database-local VRM information.</summary>
        /// <param name="validator">The callback that validates a Database asset path.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="validator"/> is null.</exception>
        public static void RegisterVrmValidator(Func<string, ShapeSyncDatabaseDiagnostic> validator)
        {
            vrmValidator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>Registers the optional VRM Generate post without exposing UniVRM types to Core.</summary>
        /// <param name="generate">The callback that performs the optional VRM Generate stage.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="generate"/> is null.</exception>
        public static void RegisterVrmGenerate(VrmGenerateDelegate generate)
        {
            vrmGenerate = generate ?? throw new ArgumentNullException(nameof(generate));
        }

        /// <summary>Registers the final optional VRM wiring pass after all Core Generate stages have completed.</summary>
        /// <param name="finalize">The callback that performs the final optional VRM wiring pass.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="finalize"/> is null.</exception>
        public static void RegisterVrmGenerateFinalize(VrmGenerateFinalizeDelegate finalize)
        {
            vrmGenerateFinalize = finalize ?? throw new ArgumentNullException(nameof(finalize));
        }

        /// <summary>
        /// Validates VRM information when a Database contains the Core marker.
        /// A missing validator is itself a stable, structured admission failure.
        /// </summary>
        /// <param name="assetPath">The project-relative Database Prefab path.</param>
        /// <param name="diagnostic">Receives the formatted admission diagnostic when validation fails.</param>
        /// <returns><see langword="true"/> when VRM validation succeeds or no VRM marker is present; otherwise, <see langword="false"/>.</returns>
        public static bool TryValidateVrm(string assetPath, out string diagnostic)
        {
            ShapeSyncDatabaseDiagnostic result = vrmValidator == null
                ? new ShapeSyncDatabaseDiagnostic(
                    ShapeSyncDatabaseDiagnosticCode.OptionalFeatureUnavailable,
                    ShapeSyncDatabaseEntityKind.Registry,
                    ShapeSyncDatabaseRelationKind.None,
                    VrmFeatureId,
                    "Database",
                    null,
                    "Database contains VRM information and requires SHAPESYNC_USE_UNIVRM.")
                : vrmValidator(assetPath);

            diagnostic = result == null ? null : result.ToString();
            return result == null;
        }

        /// <summary>Runs the optional VRM Generate post; no VRM provider is a no-op for Core-only Databases.</summary>
        /// <param name="database">The opened Database being generated.</param>
        /// <param name="rootPath">The project-relative Generate root.</param>
        /// <param name="generatedPaths">The transaction-owned list of generated project paths.</param>
        /// <param name="diagnostic">Receives a diagnostic when the optional Generate stage fails.</param>
        /// <returns><see langword="true"/> when the stage succeeds or no provider is registered; otherwise, <see langword="false"/>.</returns>
        public static bool TryGenerateVrm(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic)
        {
            if (vrmGenerate == null)
            {
                diagnostic = null;
                return true;
            }

            return vrmGenerate(database, rootPath, generatedPaths, out diagnostic);
        }

        /// <summary>Runs the final optional VRM wiring pass; no provider is a no-op for Core-only Databases.</summary>
        /// <param name="database">The opened Database whose generated assets are being finalized.</param>
        /// <param name="rootPath">The project-relative Generate root.</param>
        /// <param name="generatedPaths">The transaction-owned list of generated project paths.</param>
        /// <param name="diagnostic">Receives a diagnostic when finalization fails.</param>
        /// <returns><see langword="true"/> when finalization succeeds or no provider is registered; otherwise, <see langword="false"/>.</returns>
        public static bool TryFinalizeVrm(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic)
        {
            if (vrmGenerateFinalize == null)
            {
                diagnostic = null;
                return true;
            }

            return vrmGenerateFinalize(database, rootPath, generatedPaths, out diagnostic);
        }
    }
}
