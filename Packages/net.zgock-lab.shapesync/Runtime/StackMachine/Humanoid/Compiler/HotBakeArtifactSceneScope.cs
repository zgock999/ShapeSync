// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Connects one artifact set to its owner, TSM host, and Figure topology lifetime.</summary>
    public sealed class HotBakeArtifactSceneScope : IDisposable
    {
        private readonly GameObject owner;
        private readonly Scene hostScene;
        private TextureStackMachineHost host;
        private ShapeDirector director;
        private OutfitAttacher outfitAttacher;
        private HotBakeArtifactSet artifactSet;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private bool disposed;

        /// <summary>Gets the latest scene-scope or invalidation diagnostic.</summary>
        public StackMachineDiagnostic LastDiagnostic { get; private set; }
        /// <summary>Gets the currently retained artifact set, or null after invalidation.</summary>
        public HotBakeArtifactSet ArtifactSet => artifactSet;
        internal Scene HostScene => hostScene;
        internal bool TryValidateForArtifact(out StackMachineDiagnostic diagnostic) => TryValidateConfiguration(out diagnostic);

        /// <summary>Creates a scene-local scope. The owner and host must start in the same scene.</summary>
        public HotBakeArtifactSceneScope(GameObject owner, TextureStackMachineHost host, ShapeDirector director = null, OutfitAttacher outfitAttacher = null)
        {
            this.owner = owner;
            this.host = host;
            hostScene = host == null ? default : host.gameObject.scene;
            this.director = director;
            this.outfitAttacher = outfitAttacher;
            if (director != null) director.TransactionCommitted += InvalidateForDirectorTransaction;
            if (outfitAttacher != null) outfitAttacher.TopologyChanged += InvalidateForOutfitTopology;
            if (host != null) host.Destroying += InvalidateForHostDestroy;
        }

        /// <summary>Accepts the completed set only while every owner and template remains in the host scene.</summary>
        public bool TrySetArtifact(HotBakeArtifactSet value, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed || value == null || !value.IsAvailable) return Reject("HotBakeArtifactUnavailable", "Hot Bake scene scope requires one available artifact set.", out diagnostic);
            if (!TryValidateConfiguration(out diagnostic)) return false;
            artifactSet?.Invalidate();
            artifactSet = value;
            return Validate(out diagnostic);
        }

        /// <summary>Polls host destruction and scene migration from the owning component update loop.</summary>
        public bool Validate(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (artifactSet == null) return true;
            if (host == null) return Invalidate("HotBakeHostDestroyed", "TextureStackMachineHost was destroyed; the Hot Bake artifact set is invalid.", out diagnostic);
            if (!IsInHostScene(owner) || !IsInHostScene(artifactSet.TemplateRoot) || !IsInHostScene(director) || !IsInHostScene(outfitAttacher)) return Invalidate("HotBakeArtifactSceneScopeViolation", "A Hot Bake scope member left the TextureStackMachineHost scene.", out diagnostic);
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                if (spawned[i] == null) { spawned.RemoveAt(i); continue; }
                if (!IsInHostScene(spawned[i])) return Invalidate("HotBakeArtifactSceneScopeViolation", "A Hot Bake spawned instance left the TextureStackMachineHost scene.", out diagnostic);
            }
            return true;
        }

        /// <summary>Registers one later Step4 spawn for scene-scope validation without taking its ownership.</summary>
        public bool TryRegisterSpawn(GameObject instance, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (instance == null || !IsInHostScene(instance)) return Invalidate("HotBakeArtifactSceneScopeViolation", "A Hot Bake spawned instance must remain in the TextureStackMachineHost scene.", out diagnostic);
            if (!spawned.Contains(instance)) spawned.Add(instance);
            return true;
        }

        /// <summary>Forgets one Step4 spawn after its caller-owned despawn; this scope never destroys it.</summary>
        public void UnregisterSpawn(GameObject instance)
        {
            if (instance != null) spawned.Remove(instance);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (director != null) director.TransactionCommitted -= InvalidateForDirectorTransaction;
            if (outfitAttacher != null) outfitAttacher.TopologyChanged -= InvalidateForOutfitTopology;
            if (host != null) host.Destroying -= InvalidateForHostDestroy;
            director = null; outfitAttacher = null; host = null;
            artifactSet?.Invalidate(); artifactSet = null;
        }

        /// <summary>Invalidates the set after a committed Shape Director transaction.</summary>
        public void InvalidateForDirectorTransaction()
        {
            // A Director transaction is the normal revision boundary for Hybrid Hot Bake.
            // Preserve the diagnostic for the owner state machine, but do not present the
            // expected stale-to-rebake transition as a user-facing warning.
            Invalidate("HotBakeArtifactDirectorInvalidated", "Shape Director committed a new Figure transaction; the Hot Bake artifact set is stale.", out _, false);
        }
        /// <summary>Invalidates the set after a successful Outfit attach or detach.</summary>
        public void InvalidateForOutfitTopology() { Invalidate("HotBakeArtifactOutfitInvalidated", "Outfit attach or detach changed Figure topology; the Hot Bake artifact set is stale.", out _); }
        // TextureStackMachineHost raises Destroying from OnDisable as well as OnDestroy.
        // A normal PlayMode shutdown therefore reaches this callback while every
        // scene-owned artifact is being torn down.  Retain the structured
        // invalidation (callers must never reuse the artifact), but do not emit a
        // user-facing warning for that expected lifecycle path.
        private void InvalidateForHostDestroy() { Invalidate("HotBakeHostDestroyed", "TextureStackMachineHost was destroyed; the Hot Bake artifact set is invalid.", out _, false); }
        private bool TryValidateConfiguration(out StackMachineDiagnostic diagnostic)
        {
            if (host == null) return Reject("HotBakeHostRequired", "Hot Bake scene scope requires one live TextureStackMachineHost.", out diagnostic);
            if (!IsInHostScene(owner) || !IsInHostScene(director) || !IsInHostScene(outfitAttacher)) return Reject("HotBakeArtifactSceneScopeViolation", "Hot Bake owner, Director, and OutfitAttacher must share the TextureStackMachineHost scene.", out diagnostic);
            diagnostic = null;
            return true;
        }
        private bool Invalidate(string code, string message, out StackMachineDiagnostic diagnostic, bool logWarning = true)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message);
            LastDiagnostic = diagnostic;
            artifactSet?.Invalidate(); artifactSet = null;
            if (logWarning) Debug.LogWarning(code + ": " + message, owner);
            return false;
        }
        private bool IsInHostScene(GameObject value) => value != null && value.scene == hostScene && value.scene.name != "DontDestroyOnLoad";
        private bool IsInHostScene(Component value) => value == null || IsInHostScene(value.gameObject);
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message); return false; }
    }
}
