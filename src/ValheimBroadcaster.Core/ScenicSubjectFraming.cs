using System;

namespace ValheimBroadcaster.Core;

/// <summary>The same measured envelope criteria apply to seed admission, the
/// actual terrain-fitted route and live output. A visible centre is insufficient.</summary>
public static class ScenicSubjectFraming
{
    public static bool Evaluate(ShotProjection.Envelope envelope, ScenicShotRole role, out float quality)
    {
        quality = 0;
        if (!envelope.InFront || envelope.RelativeHeight < .08 || envelope.MaxX - envelope.MinX < .1) return false;
        double minimum = role == ScenicShotRole.Establish ? .88 : role == ScenicShotRole.Detail ? .2 : .55;
        if (envelope.VisibleFraction < minimum || role == ScenicShotRole.Establish &&
            (envelope.Headroom < .02 || envelope.SafeFraction < .80)) return false;
        float coverage = (float)Math.Min(1, envelope.VisibleFraction / Math.Max(.4, minimum));
        quality = role == ScenicShotRole.Detail ? coverage : coverage * .65f +
            (float)envelope.SafeFraction * .20f + (float)envelope.HeadroomScore * .15f;
        return true;
    }

    public static int RequiredVisibleMembers(int observed, ScenicShotRole role)
    {
        if (observed < 0 || observed > 8) throw new ArgumentOutOfRangeException(nameof(observed));
        // Establishing a multi-building subject needs several independently
        // visible buildings. A detail only promises the selected local element.
        return observed == 0 ? 0 : role == ScenicShotRole.Establish ? Math.Min(observed, Math.Max(2, (observed + 1) / 2)) : 1;
    }
}
