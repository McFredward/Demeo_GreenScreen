using System;
using GreenScreen;

internal static class OpenVrViewportChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var full = new OpenVrViewport.Bounds { UMax = 1, VMax = 1 };
        check(OpenVrViewport.TryPack(full, 4040, 4040, out long p) && p == 0, "whole OpenVR viewport needs no crop allocation");
        var observed = new OpenVrViewport.Bounds { UMax = 2857f / 4040, VMax = 2857f / 4040 };
        check(OpenVrViewport.TryPack(observed, 4040, 4040, out p)
            && OpenVrViewport.Width(p, 4040) == 2857 && OpenVrViewport.Height(p, 4040) == 2857,
            "actual 0.707178 normalized bounds recover the complete integer Demeo viewport");
        var offset = new OpenVrViewport.Bounds { UMin = .1f, VMin = .1f, UMax = .7f, VMax = .7f };
        check(OpenVrViewport.TryPack(offset, 160, 80, out p) && OpenVrViewport.X(p) == 16 && OpenVrViewport.Y(p) == 8
            && OpenVrViewport.Width(p, 160) == 96 && OpenVrViewport.Height(p, 80) == 48,
            "offset crops preserve both origin and extent in one coherent packed request");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f, .1234567f })
        {
            var b = full; b.UMin = invalid;
            check(!OpenVrViewport.TryPack(b, 4040, 4040, out _), "invalid or fractional viewport keeps the original bounds: " + invalid);
        }
        var flipped = full; flipped.UMin = 1; flipped.UMax = 0;
        check(!OpenVrViewport.TryPack(flipped, 4040, 4040, out _), "flipped viewport is not silently canonicalized");
        var tiny = full; tiny.UMax = 31f / 4040;
        check(!OpenVrViewport.TryPack(tiny, 4040, 4040, out _), "too-small viewport is forwarded without unsigned underflow");
    }
}
