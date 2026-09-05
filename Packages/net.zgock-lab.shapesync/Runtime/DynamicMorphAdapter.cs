// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Runtime owner for Plugable PCM slot allocation and payload mesh replacement on a Figure renderer.
    /// </summary>
    [DefaultExecutionOrder(9990)]
    public sealed class DynamicMorphAdapter : MonoBehaviour
    {
        /// <summary>
        /// Serialized layout of reserved PCM slots and the normal FBM names that index each slot group.
        /// </summary>
        [Serializable]
        public sealed class SlotSchema
        {
            [SerializeField] private int pcmSlotCount;
            [SerializeField] private int firstSlotBlendShapeIndex = -1;
            [SerializeField] private int groupSize;
            [SerializeField] private string[] fbmBlendNames = Array.Empty<string>();
            [SerializeField] private string[] slotBlendShapeNames = Array.Empty<string>();

            public int PcmSlotCount => pcmSlotCount;
            public int FirstSlotBlendShapeIndex => firstSlotBlendShapeIndex;
            public int GroupSize => groupSize;
            public IReadOnlyList<string> FbmBlendNames => fbmBlendNames;
            public IReadOnlyList<string> SlotBlendShapeNames => slotBlendShapeNames;

            public void Configure(int slots, int firstIndex, string[] fbmNames)
            {
                pcmSlotCount = Mathf.Max(0, slots);
                firstSlotBlendShapeIndex = firstIndex;
                fbmBlendNames = fbmNames ?? Array.Empty<string>();
                groupSize = fbmBlendNames.Length + 1;
                slotBlendShapeNames = new string[pcmSlotCount * groupSize];
                for (int i = 0; i < slotBlendShapeNames.Length; i++) slotBlendShapeNames[i] = BlendShapeReservedPrefixes.MorphSlot + i;
            }
        }

        /// <summary>
        /// Prepared, uncommitted PCM attachment state used to preserve transactional attach semantics.
        /// </summary>
        public sealed class PreparedPcmAttach
        {
            internal readonly int RegistrationId;
            internal readonly int GroupIndex;
            internal readonly Mesh CandidateMesh;
            internal readonly ProfileControlledMorphAsset Asset;

            internal PreparedPcmAttach(int registrationId, int groupIndex, Mesh candidateMesh, ProfileControlledMorphAsset asset)
            {
                RegistrationId = registrationId;
                GroupIndex = groupIndex;
                CandidateMesh = candidateMesh;
                Asset = asset;
            }
        }

        private struct ActiveRegistration
        {
            public bool Active;
            public int RegistrationId;
            public int GroupIndex;
        }

        [SerializeField] private SkinnedMeshRenderer targetRenderer;
        [SerializeField] private SlotSchema slotSchema = new SlotSchema();
        [SerializeField] private ulong sourceGeometrySignature;

        private Mesh sourceSharedMesh;
        private Mesh runtimeMesh;
        private bool[] occupiedGroups = Array.Empty<bool>();
        private ActiveRegistration[] activeRegistrations = Array.Empty<ActiveRegistration>();
        private int activeRegistrationCount;
        private Vector3[] scratchVertices = Array.Empty<Vector3>();
        private Vector3[] scratchNormals = Array.Empty<Vector3>();
        private Vector3[] scratchTangents = Array.Empty<Vector3>();
        private string[] blendShapeNames = Array.Empty<string>();
        private bool schemaValidated;

        public SkinnedMeshRenderer TargetRenderer => targetRenderer;
        public SlotSchema Schema => slotSchema;
        public int ActiveRegistrationCount => activeRegistrationCount;
        public int FreeSlotGroups => Mathf.Max(0, slotSchema.PcmSlotCount - activeRegistrationCount);

        public void ConfigureForFigure(SkinnedMeshRenderer renderer, int pcmSlots, int firstSlotIndex, string[] fbmNames)
        {
            targetRenderer = renderer;
            sourceGeometrySignature = MeshGeometrySignature.Calculate(renderer != null ? renderer.sharedMesh : null);
            slotSchema.Configure(pcmSlots, firstSlotIndex, fbmNames);
            schemaValidated = false;
            EnsureRegistrationTables();
        }

        public Mesh CreateInitialRuntimeMesh(Mesh sourceMesh)
        {
            if (targetRenderer == null || sourceMesh == null) return null;
            sourceSharedMesh = sourceMesh;
            EnsureWorkingBuffers(sourceMesh.vertexCount);
            EnsureRegistrationTables();
            CacheBlendShapeNames(sourceMesh);
            runtimeMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            targetRenderer.sharedMesh = runtimeMesh;
            schemaValidated = ValidateSchema(sourceMesh, out _);
            return runtimeMesh;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (targetRenderer != null && sourceSharedMesh != null && runtimeMesh != null && targetRenderer.sharedMesh == runtimeMesh)
            {
                targetRenderer.sharedMesh = sourceSharedMesh;
            }

            if (runtimeMesh != null)
            {
                DestroyRuntimeMesh(runtimeMesh);
                runtimeMesh = null;
            }
        }

        public bool Initialize()
        {
            if (targetRenderer == null || targetRenderer.sharedMesh == null) return false;
            if (sourceSharedMesh == null) sourceSharedMesh = targetRenderer.sharedMesh;
            EnsureWorkingBuffers(sourceSharedMesh.vertexCount);
            CacheBlendShapeNames(sourceSharedMesh);

            EnsureRegistrationTables();
            if (schemaValidated) return true;
            schemaValidated = ValidateSchema(sourceSharedMesh, out _);
            return schemaValidated;
        }

        public bool WriteFigureBlendShapeWeight(int blendShapeIndex, float weight)
        {
            if (targetRenderer == null || blendShapeIndex < 0 || float.IsNaN(weight) || float.IsInfinity(weight)) return false;
            targetRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
            return true;
        }

        public bool TryPreparePcmAttach(ProfileControlledMorphAsset asset, int registrationId, out PreparedPcmAttach prepared, out string error)
        {
            prepared = null;
            error = null;
            if (!Initialize()) { error = "Dynamic Morph Adapter is not configured for a valid Figure renderer."; return false; }
            if (asset == null || asset.PayloadMesh == null) { error = "PCM payload asset and Mesh are required."; return false; }
            if (ContainsRegistration(registrationId)) { error = "PCM registration identity is already active."; return false; }
            if (!TryFindFreeGroup(out int groupIndex)) { error = "No PCM slot group is available on this Figure."; return false; }
            if (!ValidatePayload(asset, out error)) return false;

            Mesh candidate = null;
            try
            {
                candidate = RebuildMeshWithPayload(CurrentMesh, asset, groupIndex);
                prepared = new PreparedPcmAttach(registrationId, groupIndex, candidate, asset);
                return true;
            }
            catch (Exception exception)
            {
                if (candidate != null) DestroyRuntimeMesh(candidate);
                error = exception.Message;
                return false;
            }
        }

        public bool CommitPreparedPcmAttach(PreparedPcmAttach prepared, out string error)
        {
            error = null;
            if (prepared == null || prepared.CandidateMesh == null) { error = "PCM attach preparation is invalid."; return false; }
            if (ContainsRegistration(prepared.RegistrationId) || prepared.GroupIndex < 0 || prepared.GroupIndex >= occupiedGroups.Length || occupiedGroups[prepared.GroupIndex])
            {
                error = "PCM slot state changed before commit.";
                return false;
            }

            if (targetRenderer == null)
            {
                error = "Dynamic Morph Adapter has no target renderer at PCM commit.";
                return false;
            }

            Mesh previous = runtimeMesh;
            runtimeMesh = prepared.CandidateMesh;
            targetRenderer.sharedMesh = runtimeMesh;
            occupiedGroups[prepared.GroupIndex] = true;
            if (!TryAddRegistration(prepared.RegistrationId, prepared.GroupIndex))
            {
                targetRenderer.sharedMesh = previous;
                runtimeMesh = previous;
                occupiedGroups[prepared.GroupIndex] = false;
                error = "PCM registration table has no free entry.";
                return false;
            }
            if (previous != null) DestroyRuntimeMesh(previous);
            return true;
        }

        public void RollbackPreparedPcmAttach(PreparedPcmAttach prepared)
        {
            if (prepared != null && prepared.CandidateMesh != null && prepared.CandidateMesh != runtimeMesh) DestroyRuntimeMesh(prepared.CandidateMesh);
        }

        public bool ReleasePcmAttachment(int registrationId)
        {
            if (!TryFindRegistration(registrationId, out int registrationIndex)) return false;
            ActiveRegistration registration = activeRegistrations[registrationIndex];
            int start = slotSchema.FirstSlotBlendShapeIndex + registration.GroupIndex * slotSchema.GroupSize;
            for (int i = 0; i < slotSchema.GroupSize; i++) WriteFigureBlendShapeWeight(start + i, 0f);
            occupiedGroups[registration.GroupIndex] = false;
            activeRegistrations[registrationIndex] = default;
            activeRegistrationCount--;
            return true;
        }

        public bool ApplyPcmBase(int registrationId)
        {
            return TryGetRegistrationStart(registrationId, out int start) && WriteFigureBlendShapeWeight(start, 100f);
        }

        public bool ApplyPcmFbmWeight(int registrationId, int fbmIndex, float rawWeight)
        {
            if (fbmIndex < 0 || fbmIndex >= slotSchema.FbmBlendNames.Count) return false;
            return TryGetRegistrationStart(registrationId, out int start) && WriteFigureBlendShapeWeight(start + 1 + fbmIndex, rawWeight * 100f);
        }

        private bool TryGetRegistrationStart(int registrationId, out int start)
        {
            start = -1;
            if (!TryFindRegistration(registrationId, out int registrationIndex)) return false;
            ActiveRegistration registration = activeRegistrations[registrationIndex];
            start = slotSchema.FirstSlotBlendShapeIndex + registration.GroupIndex * slotSchema.GroupSize;
            return true;
        }

        private Mesh CurrentMesh => runtimeMesh != null ? runtimeMesh : sourceSharedMesh;

        private void EnsureWorkingBuffers(int vertexCount)
        {
            if (scratchVertices.Length == vertexCount) return;
            scratchVertices = new Vector3[vertexCount];
            scratchNormals = new Vector3[vertexCount];
            scratchTangents = new Vector3[vertexCount];
        }

        private void CacheBlendShapeNames(Mesh mesh)
        {
            if (mesh == null || blendShapeNames.Length == mesh.blendShapeCount) return;
            blendShapeNames = new string[mesh.blendShapeCount];
            for (int i = 0; i < blendShapeNames.Length; i++) blendShapeNames[i] = mesh.GetBlendShapeName(i);
        }

        private void EnsureRegistrationTables()
        {
            if (occupiedGroups.Length != slotSchema.PcmSlotCount) occupiedGroups = new bool[slotSchema.PcmSlotCount];
            if (activeRegistrations.Length != slotSchema.PcmSlotCount)
            {
                activeRegistrations = new ActiveRegistration[slotSchema.PcmSlotCount];
                activeRegistrationCount = 0;
            }
        }

        private bool ContainsRegistration(int registrationId)
        {
            return TryFindRegistration(registrationId, out _);
        }

        private bool TryFindRegistration(int registrationId, out int index)
        {
            for (int i = 0; i < activeRegistrations.Length; i++)
            {
                if (activeRegistrations[i].Active && activeRegistrations[i].RegistrationId == registrationId)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        private bool TryAddRegistration(int registrationId, int groupIndex)
        {
            for (int i = 0; i < activeRegistrations.Length; i++)
            {
                if (activeRegistrations[i].Active) continue;
                activeRegistrations[i] = new ActiveRegistration { Active = true, RegistrationId = registrationId, GroupIndex = groupIndex };
                activeRegistrationCount++;
                return true;
            }
            return false;
        }

        private bool TryFindFreeGroup(out int groupIndex)
        {
            for (int i = 0; i < occupiedGroups.Length; i++) if (!occupiedGroups[i]) { groupIndex = i; return true; }
            groupIndex = -1;
            return false;
        }

        private bool ValidateSchema(Mesh mesh, out string error)
        {
            error = null;
            if (slotSchema.GroupSize != slotSchema.FbmBlendNames.Count + 1 || slotSchema.PcmSlotCount < 0 || slotSchema.FirstSlotBlendShapeIndex < 0)
            {
                error = "PCM slot schema is invalid.";
                return false;
            }
            int expectedSlots = slotSchema.PcmSlotCount * slotSchema.GroupSize;
            if (slotSchema.FirstSlotBlendShapeIndex + expectedSlots > mesh.blendShapeCount)
            {
                error = "Figure Mesh does not contain the configured PCM slots.";
                return false;
            }
            for (int i = 0; i < expectedSlots; i++)
            {
                if (slotSchema.SlotBlendShapeNames.Count != expectedSlots || mesh.GetBlendShapeName(slotSchema.FirstSlotBlendShapeIndex + i) != slotSchema.SlotBlendShapeNames[i])
                {
                    error = "Figure PCM slot name or order does not match its schema.";
                    return false;
                }
            }
            return true;
        }

        private bool ValidatePayload(ProfileControlledMorphAsset asset, out string error)
        {
            error = null;
            Mesh payload = asset.PayloadMesh;
            if (!ValidateSchema(CurrentMesh, out error)) return false;
            if (!payload.isReadable || payload.vertexCount != sourceSharedMesh.vertexCount || payload.subMeshCount != sourceSharedMesh.subMeshCount || sourceGeometrySignature == 0UL || asset.BaseGeometrySignature != sourceGeometrySignature) { error = "PCM payload base geometry is incompatible with the Figure Mesh."; return false; }
            if (string.IsNullOrEmpty(asset.OutfitName)) { error = "PCM payload Outfit name is required."; return false; }
            if (asset.FbmBlendNames.Count != slotSchema.FbmBlendNames.Count || asset.FbmFrameNames.Count != slotSchema.FbmBlendNames.Count) { error = "PCM payload FBM schema does not match the Figure."; return false; }
            int baseIndex = payload.GetBlendShapeIndex(asset.BaseFrameName);
            if (!ValidatePayloadFrame(payload, baseIndex, "Base", out error)) return false;
            for (int i = 0; i < slotSchema.FbmBlendNames.Count; i++)
            {
                int frameIndex = asset.FbmFrameNames.Count > i ? payload.GetBlendShapeIndex(asset.FbmFrameNames[i]) : -1;
                if (asset.FbmBlendNames[i] != slotSchema.FbmBlendNames[i] || !ValidatePayloadFrame(payload, frameIndex, slotSchema.FbmBlendNames[i], out error))
                {
                    if (error == null) error = "PCM payload FBM frame schema does not match the Figure.";
                    return false;
                }
            }
            return true;
        }

        private bool ValidatePayloadFrame(Mesh payload, int shapeIndex, string label, out string error)
        {
            error = null;
            if (shapeIndex < 0 || payload.GetBlendShapeFrameCount(shapeIndex) != 1)
            {
                error = "PCM payload is missing its required " + label + " frame or contains more than one frame.";
                return false;
            }
            if (!Mathf.Approximately(payload.GetBlendShapeFrameWeight(shapeIndex, 0), 100f))
            {
                error = "PCM payload " + label + " frame must use weight 100.";
                return false;
            }
            payload.GetBlendShapeFrameVertices(shapeIndex, 0, scratchVertices, scratchNormals, scratchTangents);
            for (int i = 0; i < scratchVertices.Length; i++)
            {
                if (!IsFinite(scratchVertices[i]) || !IsFinite(scratchNormals[i]) || !IsFinite(scratchTangents[i]))
                {
                    error = "PCM payload " + label + " frame contains a non-finite delta.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static void DestroyRuntimeMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        private Mesh RebuildMeshWithPayload(Mesh source, ProfileControlledMorphAsset asset, int groupIndex)
        {
            // Unity cannot replace existing BlendShape frames in place.  Clone geometry once,
            // clear only its frames, then replay them in their original order.  This preserves
            // every vertex channel, sub-mesh descriptor and skinning datum without ShapeSync
            // allocating managed geometry arrays during attach.
            Mesh rebuilt = ShapeSyncMeshCloneUtility.Clone(source);
            rebuilt.ClearBlendShapes();

            int replacementStart = slotSchema.FirstSlotBlendShapeIndex + groupIndex * slotSchema.GroupSize;
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string name = blendShapeNames[shape];
                bool replacement = shape >= replacementStart && shape < replacementStart + slotSchema.GroupSize;
                if (replacement)
                {
                    int relative = shape - replacementStart;
                    string payloadName = relative == 0 ? asset.BaseFrameName : asset.FbmFrameNames[relative - 1];
                    int payloadIndex = asset.PayloadMesh.GetBlendShapeIndex(payloadName);
                    asset.PayloadMesh.GetBlendShapeFrameVertices(payloadIndex, 0, scratchVertices, scratchNormals, scratchTangents);
                    rebuilt.AddBlendShapeFrame(name, 100f, scratchVertices, scratchNormals, scratchTangents);
                    continue;
                }

                int frames = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frames; frame++)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, scratchVertices, scratchNormals, scratchTangents);
                    rebuilt.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(shape, frame), scratchVertices, scratchNormals, scratchTangents);
                }
            }
            return rebuilt;
        }
    }
}
