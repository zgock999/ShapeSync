// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Immutable source roles accepted by the single <see cref="VrmIntegrationService.TransportPhysics"/> entry point.
    /// </summary>
    public readonly struct VrmTransportPhysicsRequest
    {
        private readonly IReadOnlyList<GameObject> attachedOutfitSourceRoots;

        /// <summary>Creates a candidate and Mesh-lower source-role snapshot for one in-memory VRM transport.</summary>
        public VrmTransportPhysicsRequest(
            GameObject candidateRoot,
            GameObject figureSourceRoot,
            IReadOnlyList<GameObject> attachedOutfitSourceRoots)
        {
            CandidateRoot = candidateRoot;
            FigureSourceRoot = figureSourceRoot;
            if (attachedOutfitSourceRoots == null || attachedOutfitSourceRoots.Count == 0)
            {
                this.attachedOutfitSourceRoots = Array.Empty<GameObject>();
                return;
            }

            var snapshot = new GameObject[attachedOutfitSourceRoots.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = attachedOutfitSourceRoots[i];
            }
            this.attachedOutfitSourceRoots = Array.AsReadOnly(snapshot);
        }

        /// <summary>Gets the unpublished Pure Humanoid candidate that may be modified by the service.</summary>
        public GameObject CandidateRoot { get; }
        /// <summary>Gets the read-only Figure source role.</summary>
        public GameObject FigureSourceRoot { get; }
        /// <summary>Gets the read-only Outfit source-role snapshot accepted by Mesh ATTACH lower, in lower order.</summary>
        public IReadOnlyList<GameObject> AttachedOutfitSourceRoots => attachedOutfitSourceRoots ?? Array.Empty<GameObject>();
    }

    /// <summary>
    /// Normalizes compiler-owned candidate state before request validation. A candidate is
    /// cloned from the Figure source, therefore cloned Vrm10Instance components are only
    /// source references and must not participate in the newly initialized output.
    /// </summary>
    internal static class VrmTransportCandidateNormalizer
    {
        /// <summary>
        /// Removes every candidate-side Vrm10Instance component without destroying the
        /// VRM assets it references. This is deliberately safe for an invalid or null
        /// candidate so TransportPhysics can normalize before validation.
        /// </summary>
        internal static void Normalize(GameObject candidate, bool immediately = false)
        {
            if (candidate == null) return;
            Vrm10Instance[] existing = candidate.GetComponentsInChildren<Vrm10Instance>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                if (Application.isPlaying && !immediately) UnityEngine.Object.Destroy(existing[i]);
                else UnityEngine.Object.DestroyImmediate(existing[i]);
            }
        }
    }

    /// <summary>
    /// Owns the in-memory UniVRM assets created by <see cref="VrmIntegrationService.TransportPhysics"/>.
    /// Call <see cref="ReleaseAssetOwnership"/> only after a publisher has made every object persistent.
    /// </summary>
    public sealed class VrmTransportPhysicsResult : IDisposable
    {
        private VRM10Object vrm;
        private VRM10Expression[] expressions;
        private IReadOnlyList<VRM10Expression> expressionView;
        private bool ownsAssets;

        internal VrmTransportPhysicsResult(Vrm10Instance instance, VRM10Object vrm, VRM10Expression[] expressions)
        {
            Instance = instance;
            this.vrm = vrm;
            this.expressions = expressions ?? Array.Empty<VRM10Expression>();
            expressionView = Array.AsReadOnly(this.expressions);
            ownsAssets = true;
        }

        /// <summary>Gets the candidate-attached in-memory VRM instance.</summary>
        public Vrm10Instance Instance { get; }
        /// <summary>Gets the in-memory VRM object that is connected to <see cref="Instance"/>.</summary>
        public VRM10Object Vrm => vrm;
        /// <summary>Gets every in-memory standard and Custom Expression owned by this result.</summary>
        public IReadOnlyList<VRM10Expression> Expressions => expressionView;

        /// <summary>
        /// Transfers ScriptableObject ownership to a publisher after it has persisted all returned assets.
        /// The candidate and its <see cref="Instance"/> remain caller-owned.
        /// </summary>
        public void ReleaseAssetOwnership()
        {
            ownsAssets = false;
            vrm = null;
            expressions = Array.Empty<VRM10Expression>();
            expressionView = Array.Empty<VRM10Expression>();
        }

        /// <summary>Destroys only unpersisted in-memory VRM assets still owned by this result.</summary>
        public void Dispose()
        {
            if (!ownsAssets)
            {
                return;
            }

            ownsAssets = false;
            for (int i = 0; i < expressions.Length; i++)
            {
                if (expressions[i] != null)
                {
                    DestroyOwnedAsset(expressions[i]);
                }
            }
            expressions = Array.Empty<VRM10Expression>();
            expressionView = Array.Empty<VRM10Expression>();
            if (vrm != null)
            {
                DestroyOwnedAsset(vrm);
                vrm = null;
            }
        }

        private static void DestroyOwnedAsset(UnityEngine.Object asset)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    /// <summary>Internal validated request state shared by the Runtime implementation stages.</summary>
    internal readonly struct VrmTransportPhysicsContext
    {
        internal VrmTransportPhysicsContext(VrmTransportPhysicsRequest request, Animator candidateAnimator)
        {
            Request = request;
            CandidateAnimator = candidateAnimator;
        }

        internal VrmTransportPhysicsRequest Request { get; }
        internal Animator CandidateAnimator { get; }
    }

    /// <summary>Validates the Runtime-only candidate and source-role boundary before candidate mutation begins.</summary>
    internal static class VrmTransportPhysicsRequestValidator
    {
        internal static bool TryValidate(
            VrmTransportPhysicsRequest request,
            out VrmTransportPhysicsContext context,
            out StackMachineDiagnostic diagnostic)
        {
            context = default;
            diagnostic = null;
            if (request.CandidateRoot == null)
            {
                return Reject("VrmCandidateRequired", "VRM TransportPhysics requires an unpublished Pure Humanoid candidate.", out diagnostic);
            }
            if (request.FigureSourceRoot == null)
            {
                return Reject("VrmFigureSourceRequired", "VRM TransportPhysics requires a Figure source role.", out diagnostic);
            }
            if (OverlapsCandidateHierarchy(request.CandidateRoot, request.FigureSourceRoot))
            {
                return Reject("VrmCandidateMustBeDetached", "VRM TransportPhysics candidate and Figure source must be detached hierarchy roots.", out diagnostic);
            }

            IReadOnlyList<GameObject> outfits = request.AttachedOutfitSourceRoots;
            var seenSources = new HashSet<GameObject> { request.FigureSourceRoot };
            for (int i = 0; i < outfits.Count; i++)
            {
                GameObject outfit = outfits[i];
                if (outfit == null)
                {
                    return Reject("VrmOutfitSourceRequired", "VRM TransportPhysics source roles cannot contain a null Outfit.", out diagnostic);
                }
                if (OverlapsCandidateHierarchy(request.CandidateRoot, outfit))
                {
                    return Reject("VrmCandidateMustBeDetached", "VRM TransportPhysics candidate and Outfit source must be detached hierarchy roots.", out diagnostic);
                }
                if (!seenSources.Add(outfit))
                {
                    return Reject("VrmSourceRoleDuplicate", "VRM TransportPhysics source roles cannot contain the same Figure or Outfit more than once.", out diagnostic);
                }
            }

            Animator[] animators = request.CandidateRoot.GetComponentsInChildren<Animator>(true);
            Animator animator = request.CandidateRoot.GetComponent<Animator>();
            if (animators.Length != 1 || animator == null)
            {
                return Reject("VrmCandidateAnimatorAmbiguous", "VRM TransportPhysics candidate root must contain exactly one Animator.", out diagnostic);
            }
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                return Reject("VrmCandidateHumanoidRequired", "VRM TransportPhysics candidate requires a valid Humanoid Avatar.", out diagnostic);
            }

            context = new VrmTransportPhysicsContext(request, animator);
            return true;
        }

        private static bool OverlapsCandidateHierarchy(GameObject candidate, GameObject source)
        {
            Transform candidateTransform = candidate.transform;
            Transform sourceTransform = source.transform;
            return candidateTransform == sourceTransform
                || candidateTransform.IsChildOf(sourceTransform)
                || sourceTransform.IsChildOf(candidateTransform);
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("vrm", code, message);
            return false;
        }
    }
}
#endif
