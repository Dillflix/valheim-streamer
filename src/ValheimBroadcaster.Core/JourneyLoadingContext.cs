using System;

namespace ValheimBroadcaster.Core;

/// <summary>Retention of planning work depends on every observation it still
/// needs, not just the old route origin. This is a loading-range check only;
/// it never certifies terrain, foliage, collision clearance or admission.</summary>
public static class JourneyLoadingContext
{
    public static bool ContainsPoint(AutonomyPosition point, AutonomyPosition carrier, double observationRadius = 0)
    {
        if (double.IsNaN(observationRadius) || double.IsInfinity(observationRadius) || observationRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(observationRadius));
        return point.Distance(carrier) + observationRadius <= JourneyRunwayPolicy.LocalReachMetres;
    }

    public static bool ContainsPlan(ScenicShotPlan plan, AutonomyPosition carrier, double travelViewDepth)
    {
        if (plan == null || !plan.IsDirected) return false;
        for (int i = 0; i <= 16; i++)
        {
            ScenicShotSample pose = plan.Sample(i / 16d);
            if (!ContainsPoint(pose.CameraPosition, carrier, plan.UsesJourneyView ? travelViewDepth : 0) ||
                !plan.UsesJourneyView && !ContainsPoint(pose.AimPosition, carrier)) return false;
        }
        return true;
    }
}
