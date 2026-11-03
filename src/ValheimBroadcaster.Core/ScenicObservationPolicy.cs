using System;

namespace ValheimBroadcaster.Core;

public enum ScenicSurveyRefresh { Initial, Throttled, Reused, Recentered, Expired, ClockReset }
[Flags]
public enum ScenicCoverageGap { None = 0, RetainedRadius = 1, DetailRadius = 2 }

/// <summary>The live foliage adapter and offline replay share these decisions.
/// A coverage gap is missing evidence, not proof of an obstructed picture.</summary>
public static class ScenicObservationPolicy
{
    public const float ForegroundRange = 6, DetailRadius = 16, ReuseDistance = 8;
    public const float SurveyThrottle = .5f, SurveyLifetime = 5;
    public const float SearchSeconds = 5, UrgentSearchSeconds = 1.5f, UnknownSeconds = .8f;

    public static ScenicSurveyRefresh Refresh(float now, float refreshedAt, float displacement, float coveredRadius)
    {
        if (float.IsNegativeInfinity(refreshedAt)) return ScenicSurveyRefresh.Initial;
        if (now < refreshedAt) return ScenicSurveyRefresh.ClockReset;
        float age = now - refreshedAt;
        if (age < SurveyThrottle) return ScenicSurveyRefresh.Throttled;
        if (age < SurveyLifetime && displacement <= ReuseDistance &&
            displacement + ForegroundRange < coveredRadius) return ScenicSurveyRefresh.Reused;
        return age >= SurveyLifetime ? ScenicSurveyRefresh.Expired : ScenicSurveyRefresh.Recentered;
    }

    public static bool NeedsRefresh(ScenicSurveyRefresh action) =>
        action != ScenicSurveyRefresh.Throttled && action != ScenicSurveyRefresh.Reused;

    public static ScenicCoverageGap Coverage(float displacement, float coveredRadius)
    {
        float needed = displacement + ForegroundRange;
        return (needed < coveredRadius ? ScenicCoverageGap.None : ScenicCoverageGap.RetainedRadius) |
            (needed < DetailRadius ? ScenicCoverageGap.None : ScenicCoverageGap.DetailRadius);
    }

    public static bool SearchExpired(float now, float started, bool urgent) =>
        now - started >= (urgent ? UrgentSearchSeconds : SearchSeconds);
    public static bool WaitExpired(float now, float waitingAt) => waitingAt >= 0 && now - waitingAt >= UnknownSeconds;
}
