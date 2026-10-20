#nullable disable
using System;
using System.Runtime.InteropServices;

namespace GreenScreen
{
    // An integer-aligned crop has an equivalent full-texture submission after
    // a byte-preserving GPU copy. Unknown/fractional/flipped bounds keep their
    // original full-image 2D handoff. A packed value is one coherent cold request.
    internal static class OpenVrViewport
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Bounds { internal float UMin, VMin, UMax, VMax; }

        internal static bool TryPack(Bounds bounds, uint width, uint height, out long packed)
        {
            packed = 0;
            if (width < 32 || height < 32 || width > 16384 || height > 16384
                || !Edge(bounds.UMin, width, out uint x) || !Edge(bounds.VMin, height, out uint y)
                || !Edge(bounds.UMax, width, out uint right) || !Edge(bounds.VMax, height, out uint bottom)
                || right < x + 32 || bottom < y + 32) return false;
            uint w = right - x, h = bottom - y;
            if (x == 0 && y == 0 && w == width && h == height) return true;
            packed = unchecked((long)((ulong)x | (ulong)y << 16 | (ulong)w << 32 | (ulong)h << 48));
            return true;
        }

        private static bool Edge(float value, uint size, out uint pixel)
        {
            pixel = 0;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) return false;
            double exact = (double)value * size, rounded = Math.Round(exact);
            // Recover an integer edge encoded as a normalized float. This is
            // below 1/256 pixel, not permission to resample arbitrary bounds.
            if (Math.Abs(exact - rounded) > 1.0 / 256) return false;
            pixel = (uint)rounded;
            return true;
        }

        internal static uint X(long value) => (uint)((ulong)value & 65535);
        internal static uint Y(long value) => (uint)((ulong)value >> 16 & 65535);
        internal static uint Width(long value, uint full) => value == 0 ? full : (uint)((ulong)value >> 32 & 65535);
        internal static uint Height(long value, uint full) => value == 0 ? full : (uint)((ulong)value >> 48 & 65535);
    }
}
