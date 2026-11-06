using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>
/// Pure geometry/lens math for in-place shot repair and lens banding. Kept
/// free of component state so the native camera math tests can execute it;
/// the rig remains the only caller that applies the results to a camera.
/// </summary>
internal static class ShotRepairMath
{
    /// <summary>Degrees per second the shared orbital heading may develop
    /// while arcing the live camera around an obstruction.</summary>
    internal const float ArcDegreesPerSecond = 60f;
    /// <summary>The damped heading lerp (rate 5/s) peaks at five times the
    /// remaining angular distance; bounding one steering step to this value
    /// keeps the presented arc at or below ArcDegreesPerSecond.</summary>
    internal const float ArcSteerStepDegrees = ArcDegreesPerSecond / 5f;

    /// <summary>Lens banding derived from the configured baseline so that a
    /// motivated cut changes shot grammar: wide shots open the lens, medium
    /// shots tighten it, and the baseline remains the full-shot lens. Bounds
    /// keep the result inside the rig's photographically usable range.</summary>
    internal static float ScaleBandFov(ShotScale scale, float baseline)
    {
        if (float.IsNaN(baseline) || float.IsInfinity(baseline)) return 50f;
        switch (scale)
        {
            case ShotScale.Wide: return Mathf.Min(70f, baseline + 12f);
            case ShotScale.Medium: return Mathf.Max(28f, baseline - 12f);
            default: return baseline;
        }
    }

    /// <summary>Rotate the current camera's radial about the focus to probe an
    /// alternative azimuth; height and distance are preserved. Matches the
    /// rig's heading convention (x = sin, z = cos), so a positive offset here
    /// corresponds to the same positive heading change in the orbital body.
    /// Plain trigonometry keeps this executable outside the engine runtime.</summary>
    internal static Vector3 ArcProbePosition(Vector3 focus, Vector3 current, float offsetDegrees)
    {
        Vector3 radial = current - focus;
        float radians = offsetDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        return focus + new Vector3(radial.x * cos + radial.z * sin, radial.y, radial.z * cos - radial.x * sin);
    }
}
