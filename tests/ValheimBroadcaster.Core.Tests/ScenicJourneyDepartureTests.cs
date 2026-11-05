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
        yield return ("Independent Journey departure survives ordinary edits and waits for minimum useful dwell", IndependentCut);
        yield return ("Ready Journey editorial waiting does not consume computation time or revive an expired departure", ReadyCutWait);
        yield return ("An unready independent Journey never parks an ordinary endpoint", IndependentEndpoint);
        yield return ("Journey departure snapshot uses the observed pose rather than prior route geometry or subject envelope", ObservedSnapshot);
        yield return ("Journey destination eviction is not withdrawal while structural report age and retry evidence remain authoritative", DestinationAge);
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

    private static void IndependentCut()
    {
        var d = new ScenicJourneyDeparture(); d.BeginIndependent(70, 4);
        var ordinary = Route(71).WithDuration(16);
        Check(!ScenicJourneyDeparture.CutReady(ordinary, 2, false), "An unrelated two-second shot could be interrupted");
        Check(d.Observe(71, 4, false, false, false, true, 2) == ScenicJourneyDepartureState.Preparing,
            "Ordinary edit cancelled an independent departure proposal");
        Check(d.Observe(72, 4, false, true, false, true, 1) == ScenicJourneyDepartureState.Ready,
            "Validated cut required its obsolete seed identity");
        Check(ScenicJourneyDeparture.CutReady(ordinary, ScenicPacingPolicy.MinimumSeconds(ordinary.Role), false),
            "Useful ordinary dwell never permitted the cut");
        Check(d.Observe(72, 4, true, true, false, true, .02) == ScenicJourneyDepartureState.Admitted,
            "Independent validated departure did not take over");
        var stale = new ScenicJourneyDeparture(); stale.BeginIndependent(70, 4);
        Check(stale.Observe(71, 5, true, true, false, true, .01) == ScenicJourneyDepartureState.Abandoned,
            "Independent cut bypassed scene validity");
    }

    private static void ReadyCutWait()
    {
        var d = new ScenicJourneyDeparture(); d.BeginIndependent(70, 4);
        d.Observe(70, 4, false, false, false, true, 8);
        d.Observe(71, 4, false, true, false, true, 9);
        Check(d.Pending && d.ElapsedSeconds == 8, "Ready editorial dwell consumed route preparation time");
        // The adapter revokes readiness when either route/ending proof expires.
        d.Observe(71, 4, false, false, false, true, 4.1);
        Check(d.State == ScenicJourneyDepartureState.Abandoned, "Revoked proof renewed preparation indefinitely");
        d.Observe(71, 4, true, true, false, true, .01);
        Check(!d.OwnsCoverage, "Late validation revived an abandoned departure");
    }

    private static void IndependentEndpoint()
    {
        var d = new ScenicJourneyDeparture(); d.BeginIndependent(70, 4);
        var director = new ScenicDirector(); director.Accept(Route(70), 0); director.Stage(Route(71));
        Check(d.Observe(70, 4, true, false, false, true, 1) == ScenicJourneyDepartureState.Preparing && !d.OwnsCoverage,
            "An unready departure owned an ordinary completed endpoint");
        director.Accept(director.TakePrepared()!, 1);
        Check(d.Observe(71, 4, false, false, false, true, 1) == ScenicJourneyDepartureState.Preparing && director.Current?.Id == 71,
            "Ordinary endpoint could not advance while independent departure prepared");
        Check(ScenicJourneyDeparture.CutReady(Route(72), 0, true), "An already due replacement could not accept valid Journey coverage");
    }

    private static void ObservedSnapshot()
    {
        var structure = new ScenicFeature("hall", ScenicFeatureKind.Structure,
            new AutonomyPosition(10, 3, 0), new AutonomyPosition(1, 0, 0), 8, 6, 1,
            measuredBounds: new PlaceBounds(new AutonomyPosition(2, 0, -4), new AutonomyPosition(18, 6, 4)));
        var picture = ScenicShotPlan.Picture(15, structure, ScenicShotRole.Detail,
            new AutonomyPosition(0, 4, -20), structure.Center, 53);
        var observed = new AutonomyPosition(3, 5, -18);
        var seed = ScenicJourneyDeparture.Snapshot(picture, observed, structure.Center, 61);
        Check(seed.Sample(0).CameraPosition.Distance(observed) < .001 && seed.FieldOfView == 61,
            "Departure ignored the observed camera position/lens");
        Check(seed.IsPictureOnly && seed.Feature!.MeasuredBounds == null && seed.Feature.Kind == ScenicFeatureKind.Landscape,
            "New Journey inherited structure framing or pretended its seed was a validated route");
        Check(!ScenicJourneyDeparture.CutReady(picture, 3.9, true) && !ScenicJourneyDeparture.CutReady(picture, 3.9, false) && ScenicJourneyDeparture.CutReady(picture, 4, false),
            "Independent availability picture lost its own minimum dwell");
    }

    private static void DestinationAge()
    {
        Check(ScenicJourneyDeparture.DestinationCurrent(true, 10, 900, false), "Static natural destination expired solely because its page was evicted");
        Check(ScenicJourneyDeparture.DestinationCurrent(false, 400, 199, false), "Unexpired structural observation was treated as withdrawn");
        Check(!ScenicJourneyDeparture.DestinationCurrent(false, 400, 200, false), "Evicted structure report remained fresh beyond its actual lifetime");
        Check(!ScenicJourneyDeparture.DestinationCurrent(true, 0, 0, true), "Fresh explicit retry evidence did not revoke the destination");
        Check(!ScenicJourneyDeparture.DestinationCurrent(true, 0, -1, false), "Clock reversal retained an unverifiable destination");
    }

    private static void FreshAttempt()
    {
        var d = new ScenicJourneyDeparture(); d.Begin(10, 4); d.Abandon("Measured obstruction");
        Check(!d.CanAttempt(10, 4), "Same failed seed retried every frame");
        Check(d.CanAttempt(11, 4) && d.CanAttempt(10, 5), "New source evidence could never receive a departure attempt");
        d.Reset(); Check(d.CanAttempt(10, 4), "Explicit new request could not reset departure evidence");
    }
}
