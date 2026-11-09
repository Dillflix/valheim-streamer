using System;
using System.Globalization;

namespace ValheimBroadcaster.Core;

public static class PlayerFramePosition
{
    // Unity's viewport origin is bottom-left. Operators read the picture
    // top-down and should never see mirrored coordinates for a point behind it.
    public static string Describe(float x, float y, float depth)
    {
        if (!Finite(x) || !Finite(y) || !Finite(depth)) return "position unknown";
        if (depth <= 0) return "behind camera";
        if (x < 0 || x > 1 || y < 0 || y > 1) return "outside frame";
        return string.Format(CultureInfo.InvariantCulture, "{0:0}% across, {1:0}% down", 100 * x, 100 * (1 - y));
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
