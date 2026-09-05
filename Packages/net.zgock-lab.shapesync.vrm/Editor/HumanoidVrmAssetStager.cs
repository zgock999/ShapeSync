// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.IO;
using UniVRM10;
using UnityEditor;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>Persistent VRM asset paths produced from one in-memory 17.5 result.</summary>
    public sealed class HumanoidVrmAssetStage
    {
        internal HumanoidVrmAssetStage(string assetFolder, IReadOnlyList<string> assetPaths, bool isComplete)
        {
            AssetFolder = assetFolder;
            AssetPaths = assetPaths ?? Array.Empty<string>();
            IsComplete = isComplete;
        }

        public string AssetFolder { get; }
        public IReadOnlyList<string> AssetPaths { get; }
        /// <summary>False when an AssetDatabase failure occurred after one or more persistent assets were created.</summary>
        public bool IsComplete { get; }
    }

    /// <summary>UniVRM Editor-only persistence backend. It never creates or destroys the unpublished candidate.</summary>
    public static class HumanoidVrmAssetStager
    {
        public static bool TryStage(
            string outputFolder,
            string relativeFolder,
            string documentName,
            VrmTransportPhysicsResult result,
            out HumanoidVrmAssetStage stage,
            out StackMachineDiagnostic diagnostic)
        {
            stage = null;
            diagnostic = null;
            if (result == null || result.Vrm == null)
                return Reject("VrmPublishResultRequired", "VRM asset staging requires the controller-owned in-memory VRM result.", out diagnostic);
            if (string.IsNullOrWhiteSpace(documentName))
                return Reject("VrmPublishDocumentNameRequired", "VRM asset staging requires the ShapeDocument name.", out diagnostic);
            if (!TryResolveAssetFolder(outputFolder, relativeFolder, out string assetFolder, out diagnostic)) return false;
            if (!TryGetFolderName(assetFolder, out string assetPrefix))
                return Reject("VrmPublishOutputFolderNameRequired", "VRM asset staging requires an output folder name.", out diagnostic);

            var createdPaths = new List<string>();
            try
            {
                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                IReadOnlyList<VRM10Expression> expressions = result.Expressions;
                for (int i = 0; i < expressions.Count; i++)
                {
                    VRM10Expression expression = expressions[i];
                    if (expression == null) return Reject("VrmPublishExpressionRequired", "VRM asset staging received a null Expression.", out diagnostic);
                    string expressionName = string.IsNullOrWhiteSpace(expression.name) ? "Expression" + i : expression.name;
                    if (!usedNames.Add(expressionName)) return Reject("VrmPublishExpressionNameDuplicate", "VRM asset staging received duplicate Expression names.", out diagnostic, expressionName);
                    string path = BuildPath(assetFolder, assetPrefix + "_" + expressionName + ".asset");
                    if (!TryCreateAsset(expression, path, createdPaths, out diagnostic))
                    {
                        stage = CreateStage(assetFolder, createdPaths, false);
                        return false;
                    }
                }

                string vrmPath = BuildPath(assetFolder, assetPrefix + "_vrm.asset");
                if (!TryCreateAsset(result.Vrm, vrmPath, createdPaths, out diagnostic))
                {
                    stage = CreateStage(assetFolder, createdPaths, false);
                    return false;
                }
                stage = CreateStage(assetFolder, createdPaths, true);
                return true;
            }
            catch (Exception exception)
            {
                stage = CreateStage(assetFolder, createdPaths, false);
                diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "VrmPublishAssetStagingFailed", "VRM asset staging encountered an unexpected AssetDatabase failure.", detail: exception.Message);
                return false;
            }
        }

        public static bool TryResolveAssetFolder(string outputFolder, string relativeFolder, out string assetFolder, out StackMachineDiagnostic diagnostic)
        {
            assetFolder = null;
            diagnostic = null;
            string destination = NormalizeAssetFolder(outputFolder);
            if (string.IsNullOrEmpty(destination) || !AssetDatabase.IsValidFolder(destination))
                return Reject("VrmPublishOutputFolderRequired", "VRM asset staging requires an existing output folder under Assets.", out diagnostic);
            if (!HumanoidPublishPathValidator.TryValidateVrmRelativeFolder(relativeFolder, out diagnostic)) return false;
            string relative = (relativeFolder ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

            string current = destination;
            string[] segments = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                current += "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(current) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(Path.GetDirectoryName(current)?.Replace('\\', '/') ?? destination, segments[i])) )
                    return Reject("VrmPublishAssetFolderCreateFailed", "VRM asset staging could not create the requested relative folder.", out diagnostic, current);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.IsValidFolder(current)) return Reject("VrmPublishAssetFolderCreateFailed", "VRM asset staging could not resolve the requested asset folder.", out diagnostic, current);
            assetFolder = current;
            return true;
        }

        private static bool TryCreateAsset(UnityEngine.Object asset, string path, List<string> createdPaths, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToAbsolutePath(path)))
                return Reject("VrmPublishAssetPathOccupied", "VRM asset staging found an occupied asset path.", out diagnostic, path);
            AssetDatabase.CreateAsset(asset, path);
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                return Reject("VrmPublishAssetCreateFailed", "VRM asset staging could not persist an asset.", out diagnostic, path);
            createdPaths.Add(path);
            return true;
        }

        private static string BuildPath(string folder, string fileName) => folder.TrimEnd('/') + "/" + fileName;
        private static HumanoidVrmAssetStage CreateStage(string folder, List<string> paths, bool isComplete)
            => paths.Count == 0 ? null : new HumanoidVrmAssetStage(folder, paths.AsReadOnly(), isComplete);
        private static string NormalizeAssetFolder(string path) => (path ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        private static bool TryGetFolderName(string assetFolder, out string folderName)
        {
            folderName = null;
            string normalized = NormalizeAssetFolder(assetFolder);
            int separator = normalized.LastIndexOf('/');
            string candidate = separator < 0 ? normalized : normalized.Substring(separator + 1);
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            folderName = candidate;
            return true;
        }
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        { diagnostic = StackMachineDiagnostic.CreateDomain("vrm", code, message, detail: detail); return false; }
    }
}
#endif
