using System;

namespace ValheimBroadcaster.Core;

/// <summary>A visible landscape surface may terminate a forward composition
/// ray. This preserves its bearing rather than requiring empty air through a
/// hill. The adapter supplies the nearest observed solid hit, never metadata.</summary>
public static class JourneyLandscapeGaze
{
    public static bool TryTerminate(AutonomyPosition camera, AutonomyPosition desired,
        bool nearestHitIsTerrain, double hitDistance, double nearClip, out AutonomyPosition focus)
    {
        focus = desired;
        double range = camera.Distance(desired);
        if (!nearestHitIsTerrain || !Finite(hitDistance) || !Finite(nearClip) || nearClip <= 0 ||
            range < 1 || hitDistance < Math.Max(4, nearClip * 4) || hitDistance >= range) return false;
        // FocusClear stops its verification ray .25m before the focus. Keep the
        // authored point in observed free air immediately before the landscape.
        double distance = hitDistance - .3;
        if (distance < Math.Max(2, nearClip * 2)) return false;
        double fraction = distance / range;
        focus = new AutonomyPosition((float)(camera.X + (desired.X - camera.X) * fraction),
            (float)(camera.Y + (desired.Y - camera.Y) * fraction),
            (float)(camera.Z + (desired.Z - camera.Z) * fraction));
        return true;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
