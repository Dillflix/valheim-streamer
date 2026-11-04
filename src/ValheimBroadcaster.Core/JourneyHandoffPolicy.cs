using System;

namespace ValheimBroadcaster.Core;

/// <summary>Commit to a prepared ending before the available route runs out.
/// A late successor must not cancel a braking manoeuvre and cause a speed surge.</summary>
public static class JourneyHandoffPolicy
{
    public const double EndingSeconds = 6, MaximumSettledSeconds = 3;
    public const double MaximumProofAgeSeconds = 12, RefreshProofAfterSeconds = 4;
    public static double BrakingDistance(double speed) => Math.Max(4, speed * 2);
    public static bool ShouldEnd(double remainingMetres, double speed, bool successorReady, bool arrival)
        => remainingMetres <= BrakingDistance(speed) && (!successorReady || arrival);
    public static bool ShouldBrake(bool alreadyCommitted, double remainingMetres, double speed, bool successorReady, bool arrival)
        => alreadyCommitted || ShouldEnd(remainingMetres, speed, successorReady, arrival);
    public static double BrakingScale(double remainingMetres, double speed)
        => Math.Max(.1, Math.Min(1, Math.Sqrt(Math.Max(0, remainingMetres) / BrakingDistance(speed))));
    public static bool EndingComplete(double shownSeconds, double progress)
        => progress >= .999999 || shownSeconds >= EndingSeconds + MaximumSettledSeconds;
    public static bool IsProofCurrent(long proofEpoch, double validatedAt, long currentEpoch, double now)
        => proofEpoch > 0 && proofEpoch == currentEpoch && Finite(validatedAt) && Finite(now) &&
            validatedAt >= 0 && now >= validatedAt && now - validatedAt <= MaximumProofAgeSeconds;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
