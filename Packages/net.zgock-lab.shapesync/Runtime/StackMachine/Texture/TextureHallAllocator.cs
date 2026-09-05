// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Immutable reservation of one rectangular hall inside the fixed grid.</summary>
    public readonly struct TextureHallAllocation
    {
        internal TextureHallAllocation(int id, int roomX, int roomY, int roomWidth, int roomHeight, int width, int height)
        {
            Id = id;
            RoomX = roomX;
            RoomY = roomY;
            RoomWidth = roomWidth;
            RoomHeight = roomHeight;
            Width = width;
            Height = height;
        }

        /// <summary>Gets the allocator-owned reservation identifier.</summary>
        public int Id { get; }
        /// <summary>Gets the room-grid x origin.</summary>
        public int RoomX { get; }
        /// <summary>Gets the room-grid y origin.</summary>
        public int RoomY { get; }
        /// <summary>Gets the reservation width in 128-texel allocator rooms.</summary>
        public int RoomWidth { get; }
        /// <summary>Gets the reservation height in 128-texel allocator rooms.</summary>
        public int RoomHeight { get; }
        /// <summary>Gets the texel width.</summary>
        public int Width { get; }
        /// <summary>Gets the texel height.</summary>
        public int Height { get; }
        /// <summary>Gets the texel x origin inside the grid.</summary>
        public int PixelX => RoomX * TextureHallAllocator.RoomEdge;
        /// <summary>Gets the texel y origin inside the grid.</summary>
        public int PixelY => RoomY * TextureHallAllocator.RoomEdge;
        /// <summary>Gets whether this represents a successful allocation.</summary>
        public bool IsValid => Id > 0;
    }

    /// <summary>Fixed-grid, edge-aligned 2D first-fit allocator for Texture StackMachine halls.</summary>
    public sealed class TextureHallAllocator
    {
        /// <summary>Texel edge of one allocation room.</summary>
        public const int RoomEdge = 128;

        private readonly bool[] occupied;
        private readonly HashSet<int> activeIds = new HashSet<int>();
        private readonly int roomsPerAxis;
        private int nextId = 1;

        /// <summary>Creates an allocator for one fixed square grid edge.</summary>
        /// <param name="gridEdge">Phase0 square edge of the host-owned grid.</param>
        public TextureHallAllocator(int gridEdge)
        {
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(gridEdge)) throw new ArgumentOutOfRangeException(nameof(gridEdge));
            roomsPerAxis = gridEdge / RoomEdge;
            occupied = new bool[roomsPerAxis * roomsPerAxis];
        }

        /// <summary>Gets the fixed grid edge.</summary>
        public int GridEdge => roomsPerAxis * RoomEdge;
        /// <summary>Gets the number of currently occupied rooms.</summary>
        public int OccupiedRoomCount { get; private set; }

        /// <summary>Reserves an edge-aligned rectangular hall with deterministic y-then-x first-fit placement.</summary>
        /// <param name="width">Requested power-of-two texel width.</param>
        /// <param name="height">Requested power-of-two texel height.</param>
        /// <param name="allocation">New allocation on success; otherwise the default value.</param>
        /// <returns><see langword="true"/> when a compatible free hall is available; otherwise <see langword="false"/> without changing allocator state.</returns>
        public bool TryReserve(int width, int height, out TextureHallAllocation allocation)
        {
            allocation = default;
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(width) || !TextureGpuCapabilityProbe.IsPhase0Edge(height) || width > GridEdge || height > GridEdge) return false;
            int roomWidth = width / RoomEdge;
            int roomHeight = height / RoomEdge;
            for (int y = 0; y <= roomsPerAxis - roomHeight; y++)
            {
                for (int x = 0; x <= roomsPerAxis - roomWidth; x++)
                {
                    if (!IsFree(x, y, roomWidth, roomHeight)) continue;
                    SetOccupied(x, y, roomWidth, roomHeight, true);
                    allocation = new TextureHallAllocation(nextId++, x, y, roomWidth, roomHeight, width, height);
                    activeIds.Add(allocation.Id);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Releases a reservation obtained from this allocator.</summary>
        /// <param name="allocation">Allocation previously returned by <see cref="TryReserve"/>.</param>
        /// <returns><see langword="true"/> when the active reservation was released.</returns>
        public bool TryRelease(TextureHallAllocation allocation)
        {
            if (!allocation.IsValid || !activeIds.Contains(allocation.Id) || allocation.Width <= 0 || allocation.Height <= 0 || allocation.RoomWidth <= 0 || allocation.RoomHeight <= 0 || allocation.RoomX < 0 || allocation.RoomY < 0 || allocation.RoomX + allocation.RoomWidth > roomsPerAxis || allocation.RoomY + allocation.RoomHeight > roomsPerAxis) return false;
            for (int y = allocation.RoomY; y < allocation.RoomY + allocation.RoomHeight; y++)
                for (int x = allocation.RoomX; x < allocation.RoomX + allocation.RoomWidth; x++)
                    if (!occupied[y * roomsPerAxis + x]) return false;
            SetOccupied(allocation.RoomX, allocation.RoomY, allocation.RoomWidth, allocation.RoomHeight, false);
            activeIds.Remove(allocation.Id);
            return true;
        }

        /// <summary>Checks whether an ordered hall sequence fits without mutating this allocator.</summary>
        internal bool TryCanReserveSequence(IReadOnlyList<int> widths, IReadOnlyList<int> heights)
        {
            if (widths == null || heights == null || widths.Count != heights.Count) return false;
            var simulation = new TextureHallAllocator(GridEdge);
            Array.Copy(occupied, simulation.occupied, occupied.Length);
            simulation.OccupiedRoomCount = OccupiedRoomCount;
            for (int i = 0; i < widths.Count; i++) if (!simulation.TryReserve(widths[i], heights[i], out _)) return false;
            return true;
        }

        private bool IsFree(int x, int y, int width, int height)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++)
                    if (occupied[row * roomsPerAxis + column]) return false;
            return true;
        }

        private void SetOccupied(int x, int y, int width, int height, bool value)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++)
                    occupied[row * roomsPerAxis + column] = value;
            OccupiedRoomCount += value ? width * height : -width * height;
        }
    }
}
