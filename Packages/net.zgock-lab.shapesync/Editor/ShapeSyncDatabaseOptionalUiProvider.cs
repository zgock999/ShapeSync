// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Optional Database UI seam. Core owns the callbacks so the Database window
    /// can remain UniVRM-free; an optional integration supplies its own rows and
    /// persistence behavior after domain reload.
    /// </summary>
    public static class ShapeSyncDatabaseOptionalUiProvider
    {
        private static Func<bool> vrmNavigationAvailable;
        private static Action<ShapeSyncDatabaseWindow> databaseChanged;
        private static Action<ShapeSyncDatabaseWindow> drawFigureVrmDetail;
        private static Func<ShapeSyncDatabaseWindow, bool> isFigureVrmDetailDirty;
        private static Func<ShapeSyncDatabaseWindow, string> saveFigureVrmDetail;
        private static Action<ShapeSyncDatabaseWindow> ignoreFigureVrmDetail;
        private static Action<ShapeSyncDatabaseWindow, string> drawMeshOutfitVrmDetail;
        private static Func<ShapeSyncDatabaseWindow, string, bool> isMeshOutfitVrmDetailDirty;
        private static Func<ShapeSyncDatabaseWindow, string, string> saveMeshOutfitVrmDetail;
        private static Action<ShapeSyncDatabaseWindow, string> ignoreMeshOutfitVrmDetail;
        private static Action<ShapeSyncDatabaseWindow> drawGenerationVrmPath;
        private static Func<ShapeSyncDatabaseWindow, bool> isGenerationVrmPathDirty;
        private static Func<ShapeSyncDatabaseWindow, string> validateGenerationVrmPath;
        private static Func<ShapeSyncDatabaseWindow, string> saveGenerationVrmPath;
        private static Action<ShapeSyncDatabaseWindow> ignoreGenerationVrmPath;

        /// <summary>Registers the VRM navigation and Detail callbacks.</summary>
        /// <param name="navigationAvailable">Returns whether VRM navigation is available in the current assembly set.</param>
        /// <param name="onDatabaseChanged">Handles a newly bound Database window.</param>
        /// <param name="drawFigureDetail">Draws Figure and FBM VRM Detail content.</param>
        /// <param name="figureDetailDirty">Returns whether Figure VRM Detail has unsaved changes.</param>
        /// <param name="saveFigureDetail">Saves Figure VRM Detail and returns a diagnostic on failure.</param>
        /// <param name="ignoreFigureDetail">Discards Figure VRM Detail changes.</param>
        /// <param name="drawMeshOutfitDetail">Draws Mesh Outfit VRM Detail content for an Outfit identity.</param>
        /// <param name="meshOutfitDetailDirty">Returns whether Mesh Outfit VRM Detail has unsaved changes.</param>
        /// <param name="saveMeshOutfitDetail">Saves Mesh Outfit VRM Detail and returns a diagnostic on failure.</param>
        /// <param name="ignoreMeshOutfitDetail">Discards Mesh Outfit VRM Detail changes.</param>
        /// <exception cref="ArgumentNullException">Thrown when any callback is null.</exception>
        public static void RegisterVrmUi(
            Func<bool> navigationAvailable,
            Action<ShapeSyncDatabaseWindow> onDatabaseChanged,
            Action<ShapeSyncDatabaseWindow> drawFigureDetail,
            Func<ShapeSyncDatabaseWindow, bool> figureDetailDirty,
            Func<ShapeSyncDatabaseWindow, string> saveFigureDetail,
            Action<ShapeSyncDatabaseWindow> ignoreFigureDetail,
            Action<ShapeSyncDatabaseWindow, string> drawMeshOutfitDetail,
            Func<ShapeSyncDatabaseWindow, string, bool> meshOutfitDetailDirty,
            Func<ShapeSyncDatabaseWindow, string, string> saveMeshOutfitDetail,
            Action<ShapeSyncDatabaseWindow, string> ignoreMeshOutfitDetail)
        {
            vrmNavigationAvailable = navigationAvailable ?? throw new ArgumentNullException(nameof(navigationAvailable));
            databaseChanged = onDatabaseChanged ?? throw new ArgumentNullException(nameof(onDatabaseChanged));
            drawFigureVrmDetail = drawFigureDetail ?? throw new ArgumentNullException(nameof(drawFigureDetail));
            isFigureVrmDetailDirty = figureDetailDirty ?? throw new ArgumentNullException(nameof(figureDetailDirty));
            saveFigureVrmDetail = saveFigureDetail ?? throw new ArgumentNullException(nameof(saveFigureDetail));
            ignoreFigureVrmDetail = ignoreFigureDetail ?? throw new ArgumentNullException(nameof(ignoreFigureDetail));
            drawMeshOutfitVrmDetail = drawMeshOutfitDetail ?? throw new ArgumentNullException(nameof(drawMeshOutfitDetail));
            isMeshOutfitVrmDetailDirty = meshOutfitDetailDirty ?? throw new ArgumentNullException(nameof(meshOutfitDetailDirty));
            saveMeshOutfitVrmDetail = saveMeshOutfitDetail ?? throw new ArgumentNullException(nameof(saveMeshOutfitDetail));
            ignoreMeshOutfitVrmDetail = ignoreMeshOutfitDetail ?? throw new ArgumentNullException(nameof(ignoreMeshOutfitDetail));
        }

        /// <summary>Registers the optional Generation Detail VRM path callbacks.</summary>
        /// <param name="drawPath">Draws the VRM asset output path field.</param>
        /// <param name="pathDirty">Returns whether the VRM output path has unsaved changes.</param>
        /// <param name="validatePath">Validates the current VRM output path and returns a diagnostic on failure.</param>
        /// <param name="savePath">Saves the VRM output path and returns a diagnostic on failure.</param>
        /// <param name="ignorePath">Discards VRM output path changes.</param>
        /// <exception cref="ArgumentNullException">Thrown when any callback is null.</exception>
        public static void RegisterVrmGenerationUi(
            Action<ShapeSyncDatabaseWindow> drawPath,
            Func<ShapeSyncDatabaseWindow, bool> pathDirty,
            Func<ShapeSyncDatabaseWindow, string> validatePath,
            Func<ShapeSyncDatabaseWindow, string> savePath,
            Action<ShapeSyncDatabaseWindow> ignorePath)
        {
            drawGenerationVrmPath = drawPath ?? throw new ArgumentNullException(nameof(drawPath));
            isGenerationVrmPathDirty = pathDirty ?? throw new ArgumentNullException(nameof(pathDirty));
            validateGenerationVrmPath = validatePath ?? throw new ArgumentNullException(nameof(validatePath));
            saveGenerationVrmPath = savePath ?? throw new ArgumentNullException(nameof(savePath));
            ignoreGenerationVrmPath = ignorePath ?? throw new ArgumentNullException(nameof(ignorePath));
        }

        /// <summary>Returns whether the optional VRM navigation is registered.</summary>
        /// <value><see langword="true"/> when the VRM navigation callback is registered and reports availability.</value>
        public static bool HasVrmNavigation => vrmNavigationAvailable != null && vrmNavigationAvailable();

        internal static void NotifyDatabaseChanged(ShapeSyncDatabaseWindow window)
        {
            databaseChanged?.Invoke(window);
        }

        internal static bool IsFigureVrmDetailDirty(ShapeSyncDatabaseWindow window)
        {
            return isFigureVrmDetailDirty != null && isFigureVrmDetailDirty(window);
        }

        internal static string SaveFigureVrmDetail(ShapeSyncDatabaseWindow window)
        {
            return saveFigureVrmDetail == null ? null : saveFigureVrmDetail(window);
        }

        internal static void IgnoreFigureVrmDetail(ShapeSyncDatabaseWindow window)
        {
            ignoreFigureVrmDetail?.Invoke(window);
        }

        internal static bool IsMeshOutfitVrmDetailDirty(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            return isMeshOutfitVrmDetailDirty != null && isMeshOutfitVrmDetailDirty(window, outfitIdentity);
        }

        internal static string SaveMeshOutfitVrmDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            return saveMeshOutfitVrmDetail == null ? null : saveMeshOutfitVrmDetail(window, outfitIdentity);
        }

        internal static void IgnoreMeshOutfitVrmDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            ignoreMeshOutfitVrmDetail?.Invoke(window, outfitIdentity);
        }

        internal static bool IsGenerationVrmPathDirty(ShapeSyncDatabaseWindow window)
        {
            return isGenerationVrmPathDirty != null && isGenerationVrmPathDirty(window);
        }

        internal static string SaveGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            return saveGenerationVrmPath == null ? null : saveGenerationVrmPath(window);
        }

        internal static string ValidateGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            return validateGenerationVrmPath == null ? null : validateGenerationVrmPath(window);
        }

        internal static void IgnoreGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            ignoreGenerationVrmPath?.Invoke(window);
        }

        internal static bool TryDrawGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            if (drawGenerationVrmPath == null) return false;
            drawGenerationVrmPath(window);
            return true;
        }

        internal static bool TryDrawFigureVrmDetail(ShapeSyncDatabaseWindow window)
        {
            if (drawFigureVrmDetail == null) return false;
            drawFigureVrmDetail(window);
            return true;
        }

        internal static bool TryDrawMeshOutfitVrmDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            if (drawMeshOutfitVrmDetail == null) return false;
            drawMeshOutfitVrmDetail(window, outfitIdentity);
            return true;
        }
    }
}
