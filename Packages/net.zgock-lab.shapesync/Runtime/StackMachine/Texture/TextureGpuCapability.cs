// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Immutable Phase0 GPU capability and fixed-grid budget snapshot.</summary>
    public readonly struct TextureGpuCapability
    {
        /// <summary>Creates a Texture StackMachine GPU capability snapshot.</summary>
        /// <param name="maxTextureSize">Largest supported texture dimension reported by the device.</param>
        /// <param name="gpuBudgetBytes">Host-owned GPU allocation budget in bytes.</param>
        /// <param name="fixedGridEdge">Selected supported edge for the fixed square grid.</param>
        /// <remarks>The constructor stores a detached snapshot and does not probe or allocate GPU resources.</remarks>
        public TextureGpuCapability(int maxTextureSize, long gpuBudgetBytes, int fixedGridEdge)
        {
            MaxTextureSize = maxTextureSize;
            GpuBudgetBytes = gpuBudgetBytes;
            FixedGridEdge = fixedGridEdge;
        }

        /// <summary>Gets the maximum supported texture edge.</summary>
        public int MaxTextureSize { get; }
        /// <summary>Gets the host-owned GPU allocation budget.</summary>
        public long GpuBudgetBytes { get; }
        /// <summary>Gets the selected fixed grid edge.</summary>
        public int FixedGridEdge { get; }
    }

    /// <summary>Performs narrow Phase0 texture capability probing and fixed-grid selection.</summary>
    public static class TextureGpuCapabilityProbe
    {
        /// <summary>Byte count of one Linear RGBAHalf texel.</summary>
        public const int BytesPerPixel = 8;
        /// <summary>Maximum Phase0 fixed-grid allocation in bytes.</summary>
        public const long MaxGridGpuBytes = 512L * 1024L * 1024L;
        /// <summary>Maximum combined Phase0 grid and one reserved <c>$out</c> delivery allocation in bytes.</summary>
        public const long MaxGpuBudgetBytes = MaxGridGpuBytes * 2L;

        /// <summary>Probes the current runtime device without creating GPU resources.</summary>
        /// <param name="capability">Validated capability snapshot on success; otherwise the default value.</param>
        /// <param name="diagnostic">Capability diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the device satisfies the Phase0 Texture requirements.</returns>
        public static bool TryProbe(out TextureGpuCapability capability, out StackMachineDiagnostic diagnostic)
        {
            capability = default;
            GraphicsDeviceType device = SystemInfo.graphicsDeviceType;
            if (device != GraphicsDeviceType.Direct3D11 && device != GraphicsDeviceType.Direct3D12 && device != GraphicsDeviceType.Vulkan && device != GraphicsDeviceType.Metal)
                return Fail("UnsupportedGraphicsApi", "Texture StackMachine requires Direct3D11, Direct3D12, Vulkan, or Metal.", out diagnostic);
            if (!SystemInfo.supportsComputeShaders) return Fail("ComputeShadersUnsupported", "Texture StackMachine requires compute shader support.", out diagnostic);
            if (!SystemInfo.supportsAsyncCompute) return Fail("AsyncComputeUnsupported", "Texture StackMachine requires async compute support for GraphicsFence synchronization. Use Direct3D12 or Vulkan.", out diagnostic);
            if (SystemInfo.graphicsMemorySize <= 0) return Fail("GraphicsMemoryUnknown", "Texture StackMachine requires a non-zero graphics memory size.", out diagnostic);
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Sample) || !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.LoadStore))
                return Fail("RgbaHalfLoadStoreUnsupported", "Texture StackMachine requires R16G16B16A16_SFloat sampling and load/store.", out diagnostic);

            long gpuBudget = SystemInfo.graphicsMemorySize * 1024L * 1024L / 2L;
            if (gpuBudget > MaxGpuBudgetBytes) gpuBudget = MaxGpuBudgetBytes;
            int edge = SelectFixedGridEdge(SystemInfo.maxTextureSize, gpuBudget);
            if (edge == 0) return Fail("GpuBudgetInsufficient", "GPU budget cannot reserve the minimum fixed grid and delivery texture.", out diagnostic);
            capability = new TextureGpuCapability(SystemInfo.maxTextureSize, gpuBudget, edge);
            diagnostic = null;
            return true;
        }

        /// <summary>Selects the largest Phase0 edge whose grid and one reserved <c>$out</c> delivery fit the combined GPU budget.</summary>
        /// <param name="maxTextureSize">Largest supported texture dimension.</param>
        /// <param name="gpuBudgetBytes">Available GPU budget in bytes.</param>
        /// <returns>The largest eligible Phase0 edge, or zero when no edge fits.</returns>
        public static int SelectFixedGridEdge(int maxTextureSize, long gpuBudgetBytes)
        {
            int[] edges = { 8192, 4096, 2048, 1024, 512, 256, 128 };
            for (int i = 0; i < edges.Length; i++)
            {
                int edge = edges[i];
                if (edge > maxTextureSize) continue;
                long oneTexture = (long)edge * edge * BytesPerPixel;
                if (oneTexture <= gpuBudgetBytes / 2L) return edge;
            }
            return 0;
        }

        /// <summary>Determines whether one texture extent axis is supported.</summary>
        /// <param name="edge">Width or height in texels to evaluate.</param>
        /// <returns><see langword="true"/> when <paramref name="edge"/> is a supported power-of-two extent from 128 through 8192.</returns>
        public static bool IsPhase0Edge(int edge) => edge == 128 || edge == 256 || edge == 512 || edge == 1024 || edge == 2048 || edge == 4096 || edge == 8192;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", code, message);
            return false;
        }
    }
}
