using System;

namespace ValheimBroadcaster.Core;

/// <summary>Soft editorial cost between already validated scenic shots. This
/// does not validate a cut, create a blend, or make an unavailable view usable.</summary>
public static class ScenicEditContinuity
{
    public static double Score(ScenicShotPlan? outgoing, double outgoingProgress, ScenicShotPlan incoming)
    {
        if (incoming == null) throw new ArgumentNullException(nameof(incoming));
        if (double.IsNaN(outgoingProgress) || double.IsInfinity(outgoingProgress) || outgoingProgress < 0 || outgoingProgress > 1)
            throw new ArgumentOutOfRangeException(nameof(outgoingProgress));
        if (outgoing?.Feature == null || incoming.Feature == null) return 0;
        ScenicFeature a = outgoing.Feature, b = incoming.Feature;
        bool related = a.Id == b.Id || a.ParentId == b.Id || b.ParentId == a.Id ||
            a.ParentId != null && a.ParentId == b.ParentId;
        // A new place is an editorial reset. Nearby disconnected labels are
        // not: successive rock/landscape IDs can still show the same hillside.
        double neighbourhood = Math.Max(40, Math.Min(100, 2 * (a.Radius + b.Radius)));
        double locality = related ? 1 : 1 - Ramp(a.Center.Distance(b.Center), neighbourhood, neighbourhood * 2);
        if (locality == 0) return 0;
        ScenicShotSample tail = outgoing.Sample(outgoingProgress), head = incoming.Sample(0);
        double oldScale = a.Radius / Math.Max(2, tail.CameraPosition.Distance(tail.AimPosition)) /
            Math.Tan(outgoing.FieldOfView * Math.PI / 360);
        double newScale = b.Radius / Math.Max(2, head.CameraPosition.Distance(head.AimPosition)) /
            Math.Tan(incoming.FieldOfView * Math.PI / 360);
        double ratio = Math.Max(oldScale, newScale) / Math.Max(.001, Math.Min(oldScale, newScale));
        if (!related)
        {
            // Different rocks/landscape patches have incomparable envelopes.
            // A small rock at 12m and a large one at 38m can both be wide local
            // establishing views. Use authored scale/lens for that comparison.
            if (outgoing.Role != incoming.Role) return 0;
            double oldLens = Math.Tan(outgoing.FieldOfView * Math.PI / 360);
            double newLens = Math.Tan(incoming.FieldOfView * Math.PI / 360);
            ratio = Math.Max(oldLens, newLens) / Math.Min(oldLens, newLens);
        }
        double similarity = 1 - Ramp(ratio, 1.25, 2);
        // A purposeful wide-to-detail cut may cross the old viewing direction.
        // Do not defeat that scale change merely because its camera turns.
        if (similarity == 0) return 0;
        double turn = Angle(tail.AimPosition, tail.CameraPosition, head.AimPosition, head.CameraPosition);
        double directionCost = .18 * Ramp(turn, 60, 135);
        double motionCost = 0;
        if (!outgoing.IsPictureOnly && !incoming.IsPictureOnly && outgoingProgress > .01)
        {
            ScenicShotSample before = outgoing.Sample(Math.Max(0, outgoingProgress - .08));
            ScenicShotSample after = incoming.Sample(.08);
            double oldMotion = HorizontalMotion(before.CameraPosition, tail.CameraPosition, tail);
            double newMotion = HorizontalMotion(head.CameraPosition, after.CameraPosition, head);
            // Ignore tiny motion and near-axis moves whose horizontal sign is
            // unstable. Screen-relative direction matters, not world-axis sign.
            if (oldMotion * newMotion < 0)
                motionCost = .10 * Math.Min(Ramp(Math.Abs(oldMotion), .05, .4), Ramp(Math.Abs(newMotion), .05, .4));
        }
        return -locality * similarity * (directionCost + motionCost);
    }

    private static double HorizontalMotion(AutonomyPosition from, AutonomyPosition to, ScenicShotSample view)
    {
        double x = view.AimPosition.X - view.CameraPosition.X, z = view.AimPosition.Z - view.CameraPosition.Z;
        double length = Math.Sqrt(x * x + z * z);
        return length < .001 ? 0 : ((to.X - from.X) * z - (to.Z - from.Z) * x) / length;
    }

    private static double Angle(AutonomyPosition aimA, AutonomyPosition cameraA, AutonomyPosition aimB, AutonomyPosition cameraB)
    {
        double ax = aimA.X - cameraA.X, ay = aimA.Y - cameraA.Y, az = aimA.Z - cameraA.Z;
        double bx = aimB.X - cameraB.X, by = aimB.Y - cameraB.Y, bz = aimB.Z - cameraB.Z;
        double lengths = Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
        return lengths < .001 ? 0 : Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) / lengths))) * 180 / Math.PI;
    }

    private static double Ramp(double value, double from, double to) => Math.Max(0, Math.Min(1, (value - from) / (to - from)));
}
