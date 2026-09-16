// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Side-effect-free input revalidation for prepared requests (Spec15-2 §5.6, §7.3 step 1).
    /// Reads plan inputs and requested leases only; the queue, budgets, uses, leases, and Textures are never changed.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Revalidates one prepared request's inputs without side effects: every plan Source must still be alive with
        /// unchanged extents, and every requested lease must still belong to this host, remain acquirable, and match.
        /// The acceptance-time Source-mismatch fallback is deliberately not part of this check; a mismatch here reports
        /// terminal failure.
        /// </summary>
        /// <param name="r">The prepared request whose plan inputs and requested leases are checked.</param>
        /// <param name="diagnostic">Failure diagnostic explaining the first unmet condition; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when every input is still valid; otherwise <see langword="false"/>.</returns>
        private bool TryValidateRequestInputs(QueuedRequest r, out StackMachineDiagnostic diagnostic)
        {
            for (int i = 0; i < r.hallPlan.Sources.Length; i++)
            {
                TextureRequestHallPlan.SourceEntry entry = r.hallPlan.Sources[i];
                if (entry.Texture == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceTextureUnavailable", "A prepared request source Texture was destroyed during waiting.", bindingName: entry.Name);
                    return false;
                }
                if (entry.Width != entry.Texture.width || entry.Height != entry.Texture.height)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceExtentChanged", "A prepared request source Texture changed extents during waiting.", bindingName: entry.Name);
                    return false;
                }
            }

            if (r.requestedSourceLease != null)
            {
                if (!r.requestedSourceLease.IsValid)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "A prepared request source lease was released or invalidated during waiting.");
                    return false;
                }
                if (!r.requestedSourceLease.BelongsTo(this))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseHostMismatch", "A prepared request received a retained source lease from another host.");
                    return false;
                }
                if (!r.requestedSourceLease.CanAcquire)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "A prepared request source lease was released or invalidated during waiting.");
                    return false;
                }
                if (!r.requestedSourceLease.Matches(r.plan, r.context))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "A prepared request source lease no longer matches its plan bindings.");
                    return false;
                }
            }

            if (r.requestedOutputLease != null)
            {
                if (!r.requestedOutputLease.IsValid)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "A prepared request output lease was released or invalidated during waiting.");
                    return false;
                }
                if (!r.requestedOutputLease.BelongsTo(this))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseHostMismatch", "A prepared request received a retained output lease from another host.");
                    return false;
                }
                if (r.requestedOutputLease.IsReleaseRequested)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "A prepared request output lease was released or invalidated during waiting.");
                    return false;
                }
                if (!r.requestedOutputLease.MatchesExtent(r.plan.OutputWidth, r.plan.OutputHeight))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "A prepared request output lease extent no longer matches the plan output.");
                    return false;
                }
            }

            diagnostic = null;
            return true;
        }
    }
}
