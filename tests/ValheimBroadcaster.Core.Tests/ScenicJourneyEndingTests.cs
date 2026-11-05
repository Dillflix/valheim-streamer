using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicJourneyEndingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey endings preserve exact camera, aim, composition and lens at the section seam", Seam);
        yield return ("Journey ending develops a finite view and cannot inherit kilometre or fixture timing", FiniteDevelopment);
        yield return ("Journey ending arrival focus requires a restrained local reveal, with symmetric alternatives", Arrival);
        yield return ("Journey arrival ending carries the observed subject into complete pose validation", ArrivalSubject);
        yield return ("Cruise refuge preserves the exact endpoint without requiring any decorative angular sweep", StationaryRefuge);
        yield return ("Journey refuge retains native water clearance and the endpoint's existing scale role", RefugeContract);
        yield return ("An already framed local arrival uses the exact stationary picture without a forced pan", StationaryArrival);
        yield return ("Stationary arrival refuses distant or reversing metadata and retains useful arrival pans", RefugeLimits);
    }
    private static AutonomyPosition P(float x, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Section()
    {
        var feature = new ScenicFeature("coast", ScenicFeatureKind.Coastline, P(58, 17, 9), P(1, 0), 20, 5, .9f);
        return ScenicShotPlan.Spatial(20, feature, ScenicShotRole.Traverse,
            new[] { P(0), P(24), P(48) }, new[] { P(30, 17, 9), P(54, 17, 9), P(78, 17, 9) }, 66, .08f, -.04f)
            .AsJourney(19, 11, 8);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Seam()
    {
        ScenicShotPlan section = Section(); ScenicShotSample seam = section.Sample(1);
        for (int alternative = 1; alternative < ScenicJourneyEnding.CandidateCount; alternative++)
        {
            ScenicShotPlan ending = ScenicJourneyEnding.Propose(section, 21, alternative)!;
            Check(ending != null, "Missing restrained ending candidate.");
            ScenicShotSample entry = ending!.Sample(0);
            Check(entry.CameraPosition.Distance(seam.CameraPosition) == 0 && entry.AimPosition.Distance(seam.AimPosition) == 0,
                "Ending shifted the validated seam.");
            Check(entry.ScreenX == seam.ScreenX && entry.ScreenY == seam.ScreenY && ending.FieldOfView == section.FieldOfView,
                "Ending changed composition or lens at handoff.");
            Check(!ending.IsJourney && ending.ContinuationOf == 0 && ending.Feature == section.Feature,
                "Ending retained journey ownership or changed its observed subject.");
        }
    }
    private static void FiniteDevelopment()
    {
        ScenicShotPlan section = Section().WithDuration(604800);
        ScenicShotPlan ending = ScenicJourneyEnding.Propose(section, 21, 1)!;
        Check(ending.IsPan && ending.DurationSeconds == 6 && ending.TravelMetres == 0, "Ending did not own its finite clock.");
        Check(ending.Sample(0).AimPosition.Distance(ending.Sample(.5).AimPosition) > .5 &&
            ending.Sample(.5).AimPosition.Distance(ending.Sample(1).AimPosition) > .5, "Ending falsely labels a stationary view as a developing pan.");
        for (int i = 0; i <= 60; i++)
            Check(ending.Sample(i / 60d).CameraPosition.Distance(section.Sample(1).CameraPosition) == 0,
                "Ending leaves the validated body point.");
    }
    private static void Arrival()
    {
        ScenicShotPlan section = Section(); ScenicShotSample seam = section.Sample(1);
        Check(ScenicJourneyEnding.Propose(section, 21, 0) == null, "Invented an arrival subject.");
        Check(ScenicJourneyEnding.Propose(section, 21, 0, P(1000)) == null, "Accepted distant metadata as a local reveal.");
        Check(ScenicJourneyEnding.Propose(section, 21, 0, P(18, 17, -9)) == null, "Accepted a reversing arrival pan.");
        AutonomyPosition nearby = P(78, 17, 12);
        ScenicShotPlan arrival = ScenicJourneyEnding.Propose(section, 21, 0, nearby)!;
        Check(arrival != null && arrival.Sample(1).AimPosition.Distance(nearby) < .00001,
            "Measured arrival target did not define the ending view.");
        var left = ScenicJourneyEnding.Propose(section, 21, 1)!.Sample(1).AimPosition;
        var right = ScenicJourneyEnding.Propose(section, 21, 2)!.Sample(1).AimPosition;
        Check(left.Distance(right) > 5 && Math.Abs(left.Distance(seam.CameraPosition) - right.Distance(seam.CameraPosition)) < .001,
            "Lateral ending alternatives are not bounded symmetric reveal choices.");
    }
    private static void ArrivalSubject()
    {
        ScenicShotPlan section = Section(); AutonomyPosition focus = P(78, 17, 12);
        var arrival = new ScenicFeature("observed-arrival", ScenicFeatureKind.Landscape,
            focus, P(1, 0), 16, 6, .9f);
        ScenicShotPlan ending = ScenicJourneyEnding.Propose(section, 21, 0, focus, arrival)!;
        Check(ending.Feature == arrival && ScenicJourneyEnding.AimsAtObservedArrival(ending, focus),
            "Arrival ending retained the seed subject instead of the observed destination.");
        Check(!ScenicJourneyEnding.AimsAtObservedArrival(ending, P(0)), "Reverse-looking view claims arrival framing.");
        try { ScenicJourneyEnding.Propose(section, 21, 1, null, arrival); }
        catch (ArgumentException) { return; }
        throw new Exception("Arrival metadata admitted without its observed focus.");
    }
    private static void StationaryRefuge()
    {
        ScenicShotPlan source = Section().WithDuration(604800);
        ScenicShotPlan refuge = ScenicJourneyEnding.Refuge(source, 21)!;
        Check(refuge != null && refuge.IsPictureOnly && !refuge.IsPan && !refuge.IsJourney && refuge.TravelMetres == 0,
            "Cruise refuge still requires a trajectory or decorative angular sweep");
        Check(refuge!.DurationSeconds == ScenicJourneyEnding.DurationSeconds && refuge.ContinuationOf == 0,
            "Refuge inherited the route's timing or travelling identity");
        ScenicShotSample seam = source.Sample(1);
        for (int i = 0; i <= 100; i++)
        {
            ScenicShotSample sample = refuge.Sample(i / 100d);
            Check(sample.CameraPosition.Distance(seam.CameraPosition) == 0 && sample.AimPosition.Distance(seam.AimPosition) == 0 &&
                sample.ScreenX == seam.ScreenX && sample.ScreenY == seam.ScreenY && refuge.FieldOfView == source.FieldOfView,
                "Stationary refuge changed the already validated endpoint composition");
        }
    }
    private static void RefugeContract()
    {
        foreach (bool stable in new[] { false, true })
        {
            ScenicShotPlan source = Section().WithStableWaterClearance(stable);
            ScenicShotPlan refuge = ScenicJourneyEnding.Refuge(source, 21)!;
            Check(refuge.StableWaterClearance == stable && refuge.Role == source.Role && refuge.Feature == source.Feature,
                "An unchanged endpoint acquired different water, subject or scale requirements");
            Check(!refuge.UseNativeSpline && refuge.WaypointCount == 0,
                "Stationary refuge was represented as a degenerate spline");
        }
    }
    private static void StationaryArrival()
    {
        ScenicShotPlan source = Section(); ScenicShotSample seam = source.Sample(1);
        foreach (AutonomyPosition focus in new[] { seam.AimPosition, P(78, 17, 9.5f) })
        {
            var subject = new ScenicFeature("arrived", ScenicFeatureKind.Landscape, focus, P(1, 0), 16, 6, .9f);
            ScenicShotPlan arrival = ScenicJourneyEnding.Propose(source, 21, 0, focus, subject)!;
            Check(arrival != null && arrival.IsPictureOnly && !arrival.IsPan && arrival.Feature == subject &&
                ScenicJourneyEnding.AimsAtObservedArrival(arrival, focus),
                "Already framed local subject was rejected for lacking an artificial three-degree pan");
            Check(arrival!.Sample(1).AimPosition.Distance(seam.AimPosition) == 0,
                "Stationary arrival silently recentered the exact endpoint");
        }
    }
    private static void RefugeLimits()
    {
        ScenicShotPlan source = Section();
        Check(ScenicJourneyEnding.Refuge(source, 21, P(1000)) == null &&
            ScenicJourneyEnding.Refuge(source, 21, P(18, 17, -9)) == null,
            "Stationary arrival inferred a destination from distant or reverse-facing metadata");
        AutonomyPosition focus = P(78, 17, 12);
        Check(ScenicJourneyEnding.Refuge(source, 21, focus) == null,
            "A useful angular arrival development was mislabeled as a stationary refuge");
        ScenicShotPlan arrival = ScenicJourneyEnding.Propose(source, 21, 0, focus)!;
        Check(arrival.IsPan && !arrival.IsPictureOnly && arrival.Sample(1).AimPosition.Distance(focus) < .00001,
            "A useful observed arrival lost its fully validated angular sweep");
    }
}
