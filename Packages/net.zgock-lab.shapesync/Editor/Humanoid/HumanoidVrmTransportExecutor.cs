// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Optional UniVRM Editor-assembly seam. The main ShapeSync Editor assembly never references UniVRM types.</summary>
    public interface IHumanoidVrmTransportExecutor
    {
        /// <summary>Creates an in-memory VRM instance and physics result for the resolved Pure Humanoid candidate.</summary>
        /// <param name="candidate">The unpublished resolved Humanoid root to receive the VRM instance.</param>
        /// <param name="figureSourceRoot">The read-only Figure source role.</param>
        /// <param name="document">The detached ShapeSync document that selected the source roles.</param>
        /// <param name="provenance">The single-take ATTACH provenance transferred by the Compiler.</param>
        /// <param name="result">The executor-owned in-memory transport result on success.</param>
        /// <param name="diagnostic">A structured diagnostic when transport fails.</param>
        /// <returns>True when transport completed and <paramref name="result"/> is owned by the caller.</returns>
        bool TryTransport(GameObject candidate, GameObject figureSourceRoot, ShapeSyncDocument document, HumanoidVrmTransportProvenance provenance, out IDisposable result, out StackMachineDiagnostic diagnostic);
        /// <summary>Stages every persistent asset needed by a successful in-memory transport result.</summary>
        /// <param name="transportResult">The result returned by <see cref="TryTransport"/>.</param>
        /// <param name="outputFolder">The existing Assets-relative publish folder.</param>
        /// <param name="relativeFolder">The validated VRM subfolder relative to <paramref name="outputFolder"/>.</param>
        /// <param name="documentName">The output naming prefix selected by the caller.</param>
        /// <param name="assetPaths">Persistent asset paths retained as publish evidence on success.</param>
        /// <param name="diagnostic">A structured diagnostic when staging fails.</param>
        /// <returns>True when all required VRM assets are staged.</returns>
        bool TryStageAssets(IDisposable transportResult, string outputFolder, string relativeFolder, string documentName, out IReadOnlyList<string> assetPaths, out StackMachineDiagnostic diagnostic);
        /// <summary>Finalizes staged VRM references against the already-published Prefab root.</summary>
        /// <param name="transportResult">The previously staged transport result.</param>
        /// <param name="publishedPrefabRoot">The persistent Prefab root created by the publish transaction.</param>
        /// <param name="diagnostic">A structured diagnostic when finalization fails.</param>
        /// <returns>True when persistent VRM references were finalized.</returns>
        bool TryFinalizeAssets(IDisposable transportResult, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic);
        /// <summary>Releases executor ownership after staged assets became persistent but a later publish phase failed.</summary>
        void ReleaseAssetOwnership(IDisposable transportResult);
    }
}
