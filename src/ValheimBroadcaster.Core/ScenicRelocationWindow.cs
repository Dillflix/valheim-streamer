using System;

namespace ValheimBroadcaster.Core;

/// <summary>Ordinary region dwell requests an edit, not an immediate cut out of
/// a developing shot. Waiting remains bounded and never owns travel or picture
/// readiness. Recovery and higher-priority coverage bypass this window.</summary>
public sealed class ScenicRelocationWindow
{
    public const double MaximumExtensionSeconds = 15;
    private double began = -1;
    public bool Waiting => began >= 0;

    public bool CanDepart(double now, bool editBoundary)
    {
        if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
            throw new ArgumentOutOfRangeException(nameof(now));
        if (began >= 0 && now < began)
            throw new ArgumentOutOfRangeException(nameof(now), "Reset relocation timing when changing worlds/clocks.");
        if (editBoundary) { Reset(); return true; }
        if (began < 0) began = now;
        return now - began >= MaximumExtensionSeconds;
    }

    public void Reset() => began = -1;
}
