using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Managed representation of a single RGBA pixel.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Pixel(byte R, byte G, byte B, byte A)
    {
        public byte R { get; init; } = R;
        public byte G { get; init; } = G;
        public byte B { get; init; } = B;
        public byte A { get; init; } = A;

        public static Pixel FromArgb(uint value)
        {
            byte r = (byte)(value & 0xFF);
            byte g = (byte)((value >> 8) & 0xFF);
            byte b = (byte)((value >> 16) & 0xFF);
            byte a = (byte)((value >> 24) & 0xFF);
            return new Pixel(r, g, b, a);
        }

        public uint ToArgb() => (uint)(R | (G << 8) | (B << 16) | (A << 24));
    }

    /// <summary>
    /// Position of a pixel inside a tile.
    /// </summary>
    public readonly record struct TilePosition(int TileIndex, int X, int Y);

    /// <summary>
    /// Tile dimension and memory layout information.
    /// </summary>
    public readonly record struct TileFormat(uint Width, uint Height, byte BitsPerPixel, uint BytesPerTile)
    {
        public uint BytesPerTile { get; init; } = BytesPerTile != 0 ? BytesPerTile : CalculateBytesPerTile(Width, Height, BitsPerPixel);

        private static uint CalculateBytesPerTile(uint width, uint height, byte bpp)
        {
            if (width == 0 || height == 0)
            {
                return 0;
            }

            ulong totalBits = (ulong)width * height * bpp;
            return (uint)((totalBits + 7) / 8);
        }
    }

    /// <summary>
    /// Complete tile configuration describing the tile sheet.
    /// </summary>
    public sealed class TileConfiguration
    {
        public uint Start { get; set; }
        public uint Size { get; set; }
        public ushort Rows { get; set; }
        public TileFormat Format { get; set; }
    }

    /// <summary>
    /// Provides pooling helpers for pixel buffers without resorting to unsafe code.
    /// </summary>
    public static class PixelBufferPool
    {
        public static IMemoryOwner<Pixel> Rent(int length) => MemoryPool<Pixel>.Shared.Rent(length);

        [ExcludeFromCodeCoverage]
        public static void Return(IMemoryOwner<Pixel>? owner)
        {
            owner?.Dispose();
        }
    }
}
