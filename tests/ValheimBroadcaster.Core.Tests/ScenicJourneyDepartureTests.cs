using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicJourneyDepartureTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recorded unready journey departure cannot park or replace ordinary coverage", RecordedUnreadyDeparture);
        yield return ("A validated journey departure owns coverage only at its matching seed endpoint", EndpointAdmission);
        yield return ("Stale seed scene or unavailable coverage abandons a pending journey departure", StaleDeparture);
        yield return ("Operator hold does not consume the active departure preparation budget", HeldDeparture);
        yield return ("An expired departure never gains ownership even if it later reports ready", DepartureExpiry);
        yield return ("A failed departure cannot retry the same seed until its scene or explicit request changes", FreshAttempt);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicShotPlan Route(long id)
    {
        var feature = new ScenicFeature("coast", ScenicFeatureKind.Coastline,
            new AutonomyPosition(10, 2, 0), new AutonomyPosition(1, 0, 0), 8, 5, 1);
        return ScenicShotPlan.Spatial(id, feature, ScenicShotRole.Traverse,
            new[] { new AutonomyPosition(0, 4, -20), new AutonomyPosition(10, 4, -20), new AutonomyPosition(20, 4, -20) },
            new[] { new AutonomyPosition(0, 2, 0), new AutonomyPosition(10, 2, 0), new AutonomyPosition(20, 2, 0) }, 65);
    }

    private static void RecordedUnreadyDeparture()
    {
        // 0.8.39 capture: seed70 was presented from241.513, reached its
        // endpoint249.522, and no successor ever became valid. The old owner
        // waited at that endpoint until254.6 instead of releasing ordinary work.
        var departure = new ScenicJourneyDeparture(); departure.Begin(70, 3);
        var director = new ScenicDirector(); var seed = Route(70).WithDuration(8.009);
        var ordinaryNext = Route(81); director.Accept(seed, 241.513); director.Stage(ordinaryNext);
        director.Observe(70, 241.513, 0, true, true, false);
        double at = 241.513;
        while (director.Progress < .999999)
        {
            double dt = Math.Min(.016, 249.522 - at);
            if (dt < .000001) dt = .000001;
            double progress = director.Propose(dt); at += dt;
            director.Observe(70, at, progress, true, true, false);
            departure.Observe(70, 3, director.Progress >= .999999, false, false, true, dt);
            Check(!departure.OwnsCoverage, "An unvalidated first section acquired editorial ownership");
            Check(ReferenceEquals(director.Prepared, ordinaryNext), "Speculative departure consumed the ordinary successor");
        }
        Check(departure.State == ScenicJourneyDepartureState.Abandoned, "Unready endpoint introduced a parked departure wait");
        Check(departure.ElapsedSeconds < 8.02, "Unready departure added the former six-second endpoint delay");
        director.Accept(director.TakePrepared()!, at);
        Check(director.Current?.Id == 81, "Ordinary coverage could not continue at its existing endpoint");
    }

    private static void EndpointAdmission()
    {
        var d = new ScenicJourneyDeparture(); d.Begin(10, 4);
        Check(d.Observe(10, 4, false, true, false, true, 3) == ScenicJourneyDepartureState.Ready && !d.OwnsCoverage,
            "Preparation readiness acquired the picture before reaching its seam");
        Check(d.Observe(10, 4, true, true, false, true, 2) == ScenicJourneyDepartureState.Admitted && d.OwnsCoverage,
            "Matching validated endpoint did not transfer coverage ownership");
    }

    private static void StaleDeparture()
    {
        foreach (int cause in new[] { 0, 1, 2 })
        {
            var d = new ScenicJourneyDeparture(); d.Begin(10, 4);
            d.Observe(cause == 0 ? 11 : 10, cause == 1 ? 5 : 4, true, true, false, cause != 2, .016);
            Check(d.State == ScenicJourneyDepartureState.Abandoned && !d.OwnsCoverage,
                "Ready work crossed a seed, scene or coverage ownership change");
        }
    }

    private static void HeldDeparture()
    {
        var d = new ScenicJourneyDeparture(); d.Begin(10, 4);
        d.Observe(10, 4, false, false, false, true, 5);
        d.Observe(10, 4, false, false, true, true, 120);
        Check(d.ElapsedSeconds == 5 && d.Pending && !d.OwnsCoverage, "Holding the picture spent preparation time or acquired ownership");
        d.Observe(10, 4, true, true, false, true, 1);
        Check(d.OwnsCoverage, "A valid resumed departure inherited an expired wall-clock deadline");
    }

    private static void DepartureExpiry()
    {
        foreach (bool ready in new[] { false, true })
        {
            var d = new ScenicJourneyDeparture(); d.Begin(10, 4);
            d.Observe(10, 4, false, ready, false, true, 12.1);
            Check(d.State == ScenicJourneyDepartureState.Abandoned, "Stale departure remained pending beyond its finite lifetime");
            d.Observe(10, 4, true, true, false, true, .016);
            Check(!d.OwnsCoverage, "A late completion revived expired departure work");
        }
    }

    private static void FreshAttempt()
    {
        var d = new ScenicJourneyDeparture(); d.Begin(10, 4); d.Abandon("Measured obstruction");
        Check(!d.CanAttempt(10, 4), "Same failed seed retried every frame");
        Check(d.CanAttempt(11, 4) && d.CanAttempt(10, 5), "New source evidence could never receive a departure attempt");
        d.Reset(); Check(d.CanAttempt(10, 4), "Explicit new request could not reset departure evidence");
    }
}
