using System;

namespace ValheimBroadcaster.Core;

/// <summary>A phase-independent upper surface for planned travelling shots.
/// Valheim 1.0.21's ten wave terms have amplitudes totalling 23.3; each is
/// (a*b-.2)*amplitude with a,b in [0,1]. Depth and wind scale their sum.
/// The adapter supplies the loaded volume's actual base, offset and depth.
/// This neither changes water nor replaces final live clearance checks.</summary>
public static class WaterClearanceEnvelope
{
    public const double MaximumWaveCrest = .8 * (21.8 + 1.5);
    public static bool TryHeight(double baseHeight, double surfaceOffset, double normalizedDepth,
        double windEnvelope, bool globalWaves, out float height)
    {
        height = 0;
        if (!Finite(baseHeight) || !Finite(surfaceOffset) || !Finite(normalizedDepth) ||
            !Finite(windEnvelope) || windEnvelope < 0) return false;
        // Native Mathf.Lerp clamps depth; fixed-depth water can supply values
        // outside [0,1]. Vanilla weather intensity spans [0,1]. Retain larger
        // currently observed intensities instead of clamping modified weather.
        double crest = globalWaves ? MaximumWaveCrest * Math.Max(0, Math.Min(1, normalizedDepth)) *
            Math.Max(1, windEnvelope) : 0;
        double value = baseHeight + surfaceOffset + crest + (crest > 0 ? .05 : 0);
        if (!Finite(value) || Math.Abs(value) > 20000) return false;
        height = (float)value; return true;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
