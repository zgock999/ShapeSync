// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Captures the selected Generate output before staging Figure, Outfit, and Shape assets.
    /// Restore is file-based so existing .meta files (and therefore GUIDs) remain untouched.
    /// </summary>
    internal sealed class ShapeSyncGenerateOutputSnapshot : IDisposable
    {
        private readonly string rootAbsolutePath;
        private readonly string backupAbsolutePath;
        private readonly HashSet<string> originalFiles;
        private bool completed;

        private ShapeSyncGenerateOutputSnapshot(string projectRoot, string backupRoot, IEnumerable<string> files)
        {
            rootAbsolutePath = projectRoot;
            backupAbsolutePath = backupRoot;
            originalFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        }

        internal static bool TryCreate(string assetRoot, out ShapeSyncGenerateOutputSnapshot snapshot, out string diagnostic)
        {
            snapshot = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(assetRoot) || !assetRoot.StartsWith("Assets", StringComparison.Ordinal)
                || (assetRoot.Length > "Assets".Length && assetRoot["Assets".Length] != '/'))
            {
                diagnostic = "GenerateOutputSnapshotRootInvalid: Output root must be a project Assets path.";
                return false;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string normalizedRoot = assetRoot.TrimEnd('/');
            string absoluteRoot = Path.Combine(projectRoot, normalizedRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteRoot))
            {
                diagnostic = "GenerateOutputSnapshotRootInvalid: Output root folder does not exist: " + assetRoot;
                return false;
            }

            string backupRoot = Path.Combine(projectRoot, "Temp", "ShapeSyncGenerateSnapshot_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(backupRoot);
                string[] files = Directory.GetFiles(absoluteRoot, "*", SearchOption.AllDirectories);
                foreach (string source in files)
                {
                    string relative = Path.GetRelativePath(absoluteRoot, source);
                    string destination = Path.Combine(backupRoot, relative);
                    string parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    File.Copy(source, destination, false);
                }
                snapshot = new ShapeSyncGenerateOutputSnapshot(absoluteRoot, backupRoot,
                    files.Select(path => Path.GetRelativePath(absoluteRoot, path)));
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteDirectory(backupRoot);
                diagnostic = "GenerateOutputSnapshotCreateFailed: " + exception.Message;
                return false;
            }
        }

        internal bool TryRestore(out string diagnostic)
        {
            diagnostic = null;
            try
            {
                foreach (string current in Directory.GetFiles(rootAbsolutePath, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(rootAbsolutePath, current);
                    if (!originalFiles.Contains(relative)) File.Delete(current);
                }
                foreach (string relative in originalFiles)
                {
                    string source = Path.Combine(backupAbsolutePath, relative);
                    string destination = Path.Combine(rootAbsolutePath, relative);
                    string parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    File.Copy(source, destination, true);
                }
                RemoveNonOriginalDirectories();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                completed = true;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "GenerateOutputRollbackFailed: " + exception.Message;
                return false;
            }
        }

        internal bool TryCommit(out string diagnostic)
        {
            diagnostic = null;
            try
            {
                TryDeleteDirectory(backupAbsolutePath);
                completed = true;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "GenerateOutputSnapshotCleanupFailed: " + exception.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (!completed) TryDeleteDirectory(backupAbsolutePath);
        }

        private void RemoveNonOriginalDirectories()
        {
            var originalDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootAbsolutePath };
            foreach (string relative in originalFiles)
            {
                string directory = Path.GetDirectoryName(Path.Combine(rootAbsolutePath, relative));
                while (!string.IsNullOrEmpty(directory) && directory.StartsWith(rootAbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    originalDirectories.Add(directory);
                    if (string.Equals(directory, rootAbsolutePath, StringComparison.OrdinalIgnoreCase)) break;
                    directory = Path.GetDirectoryName(directory);
                }
            }
            foreach (string directory in Directory.GetDirectories(rootAbsolutePath, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length).ToArray())
                if (!originalDirectories.Contains(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, false);
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            Directory.Delete(path, true);
        }
    }
}
#endif
