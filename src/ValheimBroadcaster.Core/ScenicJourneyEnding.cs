using System;

namespace ValheimBroadcaster.Core;

/// <summary>A finite ending proposed at a journey section's exact endpoint.
/// Geometry here is an intention only; the adapter must validate the entire
/// pan, including its near plane and focus, before admitting the section.</summary>
public static class ScenicJourneyEnding
{
    public const int CandidateCount = 5;
    public const double DurationSeconds = 6;
    public const double MaximumPreparationSeconds = 8;

    public static ScenicShotPlan? Propose(ScenicShotPlan section, long id, int alternative,
        AutonomyPosition? observedArrivalFocus = null, ScenicFeature? arrivalSubject = null)
    {
        if (section == null || !section.IsJourney || section.Feature == null)
            throw new ArgumentException("An admitted journey section is required.", nameof(section));
        if (id <= 0 || id == section.Id || alternative < 0 || alternative >= CandidateCount)
            throw new ArgumentOutOfRangeException(nameof(alternative));
        if (arrivalSubject != null && !observedArrivalFocus.HasValue)
            throw new ArgumentException("An arrival subject needs its locally observed focus.", nameof(observedArrivalFocus));
        ScenicShotSample end = section.Sample(1);
        AutonomyPosition aim;
        if (alternative == 0)
        {
            if (!observedArrivalFocus.HasValue) return null;
            aim = observedArrivalFocus.Value;
            double distance = end.CameraPosition.Distance(aim);
            double turn = Angle(end.CameraPosition, end.AimPosition, aim);
            // A remote catalogue point is never enough to supply this value.
            // The caller supplies a locally observed arrival subject only.
            if (distance < 2 || distance > 80 || end.CameraPosition.HorizontalDistance(aim) < .75 ||
                turn < 3.05 || turn > 12) return null;
        }
        else
        {
            double degrees = alternative == 1 ? 6 : alternative == 2 ? -6 : alternative == 3 ? 3.5 : -3.5;
            double radians = degrees * Math.PI / 180;
            double x = end.AimPosition.X - end.CameraPosition.X;
            double z = end.AimPosition.Z - end.CameraPosition.Z;
            double ax = end.CameraPosition.X + x * Math.Cos(radians) + z * Math.Sin(radians);
            double az = end.CameraPosition.Z - x * Math.Sin(radians) + z * Math.Cos(radians);
            if (Math.Abs(ax) > 20000 || Math.Abs(az) > 20000) return null;
            aim = new AutonomyPosition((float)ax, end.AimPosition.Y, (float)az);
        }
        try
        {
            // No identity, position, lens or composition jump is hidden in the
            // ending. It belongs to ordinary direction after journey handoff.
            return ScenicShotPlan.Pan(id, arrivalSubject ?? section.Feature, ScenicShotRole.Establish,
                end.CameraPosition, end.AimPosition, aim, section.FieldOfView,
                end.ScreenX, end.ScreenY).WithDuration(DurationSeconds).WithStableWaterClearance(section.StableWaterClearance);
        }
        catch (ArgumentException) { return null; }
    }

    public static bool AimsAtObservedArrival(ScenicShotPlan ending, AutonomyPosition focus) =>
        ending != null && Angle(ending.Sample(1).CameraPosition, ending.Sample(1).AimPosition, focus) <= 12;

    private static double Angle(AutonomyPosition origin, AutonomyPosition a, AutonomyPosition b)
    {
        double ax = a.X - origin.X, ay = a.Y - origin.Y, az = a.Z - origin.Z;
        double bx = b.X - origin.X, by = b.Y - origin.Y, bz = b.Z - origin.Z;
        double norm = Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
        return norm < .000001 ? 180 : Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) / norm))) * 180 / Math.PI;
    }
}
