using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicJourneyItineraryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey destination is an identified observed place rather than a projected distance point", KnownDestination);
        yield return ("A Journey cannot invent a distant endpoint absent from the destination catalogue", NoInventedDestination);
        yield return ("Sparse destination metadata can guide a Journey without pretending to certify the corridor", SparseDestination);
        yield return ("A full sparse paged catalogue does not require a fabricated chain of intermediate destinations", SparseCatalogue);
        yield return ("Destination selection reports exact freshness cooldown and scale exclusions", SelectionEvidence);
        yield return ("Sparse guidance still respects recent destinations and forward continuity", SparseExclusions);
        yield return ("Destination bearing guidance never credits unobserved travel or arrival", SparseNoCredit);
        yield return ("Independent departure can frame a known rear destination while preserving a forward preference", IndependentDepartureHeading);
        yield return ("Journey selection rejects stale destination reports and active destination cooldowns", FreshDestination);
        yield return ("Natural and coastal Journey destinations remain competitive with many structure candidates", LandscapeVariety);
        yield return ("Journey destination memory prevents immediate repeated arrivals", RecentDestination);
        yield return ("Journey steering follows an observed turning itinerary instead of its initial heading", TurningGuide);
        yield return ("A long curved Journey can complete its observed route and readable arrival", CurvedArrival);
        yield return ("Arriving at a known place needs measured travel and actual readable ending footage", ActualArrival);
        yield return ("A hidden relocation cannot turn a partial Journey into a completed arrival", HiddenArrival);
        yield return ("Held or unreadable arrival footage cannot complete a Journey", UnusableArrival);
        yield return ("A continuous guide cannot claim a teleport across missing render samples", HiddenGuideArrival);
        yield return ("Continuous flight measures a slower rendering cadence and requires shown arrival", GuidedArrival);
        yield return ("Journey catalogue and route construction retain explicit bounded inputs", Bounds);
    }

    private static AutonomyPosition P(float x, float z = 0) => new AutonomyPosition(x, 5, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double value, double expected, double tolerance = .001) =>
        Check(Math.Abs(value - expected) <= tolerance, $"Expected {expected}, got {value}");
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Unbounded input accepted"); }
    private static ScenicRegion Region(string id, float x, float z = 0, ScenicLandscape landscape = ScenicLandscape.Inland,
        ScenicDestinationKind kind = ScenicDestinationKind.Landscape, double after = 0) =>
        new ScenicRegion(id, id, P(x, z), availableAfter: after, biome: "Meadows", landscape: landscape, kind: kind);
    private static List<ScenicRegion> Line() => new List<ScenicRegion>
        { Region("bank", 80), Region("meadow", 160), Region("approach", 240), Region("headland", 320, landscape: ScenicLandscape.Coast) };
    private static ScenicJourneyItinerary Select(IReadOnlyList<ScenicRegion> regions, double metres = 320,
        IReadOnlyDictionary<string, WorldInterestSite>? sites = null)
    {
        bool found = ScenicJourneyItinerary.TrySelect(P(0), P(1), regions, sites, metres, 10, null, out var route, out string reason);
        Check(found && route != null, reason); return route!;
    }
    private static WorldInterestSite Site(float age = 0, float retry = 0) => new WorldInterestSite("headland", "", "Coastal camp",
        WorldInterestKind.Landmark, new ObserverPoint(325, 5, 8), new ObserverPoint(12, 6, 12), new ObserverPoint(320, 5, 0),
        1, 1, age, retry, "Meadows", ScenicLandscape.Coast);
    private static void KnownDestination()
    {
        var route = Select(Line(), sites: new Dictionary<string, WorldInterestSite> { ["headland"] = Site() });
        Check(route.Destination.Id == "headland" && route.Site?.Name == "Coastal camp", "Lost destination identity");
        Near(route.Destination.Position.Distance(P(320)), 0); Near(route.ArrivalFocus.X, 325);
        Near(route.RequestedMetres, 320); Near(route.RouteMetres, 320);
        Check(route.Waypoints.Count > 2 && route.WaypointIds[route.WaypointIds.Count - 1] == "headland", "Lost observed route provenance");
        Check(route.Description.Contains("coast"), "Lost landscape intent");
    }
    private static void HiddenGuideArrival()
    {
        var route = Select(Line());
        var policy = new ScenicJourneyPolicy();
        policy.Start(4, route, 12);
        policy.ObserveGuide(0, true, false, P(0));
        policy.ObserveGuide(.1, true, false, P(1));
        policy.BreakPresentation();
        policy.ObserveGuide(60, true, false, route.Destination.Position);
        Check(policy.Outcome == ScenicJourneyOutcome.Interrupted && !policy.TargetReached, "Unseen relocation counted as travel");
    }

    private static void GuidedArrival()
    {
        var route = Select(Line());
        var policy = new ScenicJourneyPolicy();
        policy.Start(7, route, 12);
        for (int i = 0; i <= 255; i++)
            policy.ObserveGuide(i * .1, true, false, P(i * 1.2f));
        Check(policy.AtArrival && !policy.TargetReached, "Travel alone counted an arrival");
        Near(policy.TravelMetres, 306, .1);
        for (int i = 1; i <= 34; i++)
        {
            double t = 25.5 + i * .1;
            policy.ObserveGuide(t, true, false, P(306));
            policy.ObserveArrival(t, true, false, P(306), true);
        }
        Check(policy.TargetReached, "Rendered arrival could not finish");
    }

    private static void NoInventedDestination()
    {
        foreach (var regions in new IReadOnlyList<ScenicRegion>[] { Array.Empty<ScenicRegion>(),
            new[] { Region("near", 40) }, new[] { Region("near", 80), Region("too-far", 2000) } })
        {
            Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), regions, null, 1000, 10, null, out var route, out _) && route == null,
                "Unknown or out-of-scale destination was fabricated");
        }
    }
    private static void SparseDestination()
    {
        var route = Select(new[] { Region("near", 80), Region("known-coast", 1000, landscape: ScenicLandscape.Coast) }, 1000);
        Check(route.Destination.Id == "known-coast" && !route.UsesConnectedObservations,
            "Sparse destination was rejected or falsely described as connected observations");
        Check(route.Waypoints.Count == 2 && route.WaypointIds[1] == "known-coast", "Invented intermediate metadata");
        Near(route.RouteMetres, 1000); Near(route.SteeringGoal(P(0)).X, 48);
        Check(Select(Line()).UsesConnectedObservations, "Available connected guidance was discarded");
    }
    private static void SparseCatalogue()
    {
        // Discovery pages contain only selected interesting destinations, not
        // every generated zone. Its 1024-entry cache may contain no local chain.
        var regions = new List<ScenicRegion> { Region("known-coast", 1000, landscape: ScenicLandscape.Coast) };
        for (int i = 1; i < 1024; i++) regions.Add(Region("distant-" + i, 4000 + i * 4, i % 7 * 64));
        Check(ScenicJourneyItinerary.TrySelect(P(0), P(1), regions, null, 1000, 10, null,
            out var route, out _, out var evidence), "Paged destination catalogue still required unprovided topology");
        Check(route != null && !route.UsesConnectedObservations && route.Destination.Id == "known-coast", "Lost actual destination");
        Check(evidence.CatalogueRegions == 1024 && evidence.UsableObservations == 1 && evidence.RadiusExcluded == 1023 &&
            evidence.NearDeparture == 0 && evidence.ConnectedObservations == 0 && evidence.BearingCandidates == 1,
            "Selection evidence did not distinguish catalogue size from route connectivity");
    }
    private static void SelectionEvidence()
    {
        var sites = new Dictionary<string, WorldInterestSite> { ["headland"] = Site(age: 600) };
        var regions = new[] { Region("headland", 1000), Region("retry", 900, after: 11),
            Region("recent", 1000), Region("behind", -1000), Region("near", 80), Region("far", 2000) };
        Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), regions, sites, 1000, 10, new[] { "recent" },
            out var route, out string reason, out var evidence) && route == null, "Ineligible sparse destination was selected");
        Check(evidence.StaleExcluded == 1 && evidence.CooldownExcluded == 1 && evidence.RadiusExcluded == 1 &&
            evidence.RecentExcluded == 1 && evidence.HeadingExcluded == 1 && evidence.ScaleExcluded == 1,
            "Rejection causes were conflated");
        Check(reason.Contains("stale 1") && reason.Contains("bearing destinations 0"), "Failure omitted measured exclusion evidence");
    }
    private static void SparseExclusions()
    {
        foreach (var regions in new[] { new[] { Region("behind", -1000) }, new[] { Region("recent", 1000) },
            new[] { Region("cooldown", 1000, after: 11) } })
            Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), regions, null, 1000, 10, new[] { "recent" },
                out _, out _), "Sparse guidance bypassed destination eligibility");
    }
    private static void SparseNoCredit()
    {
        var route = Select(new[] { Region("known", 1000) }, 1000);
        var policy = new ScenicJourneyPolicy(); policy.Start(1, route, 6);
        Near(policy.TravelMetres, 0); Near(policy.ShownSeconds, 0);
        Check(!policy.AtArrival && !policy.TargetReached && policy.Windows == 0, "Intended route claimed execution or arrival");
        var plan = Plan();
        policy.Observe(plan, 1, .5, false, false, P(1000));
        policy.ObserveArrival(1, false, false, P(1000), true);
        Near(policy.TravelMetres, 0); Check(!policy.TargetReached, "Unshown destination falsely completed the sparse journey");
    }
    private static void IndependentDepartureHeading()
    {
        var behind = Region("known-behind", -1000);
        Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), new[] { behind }, null, 1000, 10, null,
            out _, out _, out var legacy) && legacy.HeadingExcluded == 1,
            "A continuous departure silently lost its heading constraint");
        Check(ScenicJourneyItinerary.TrySelect(P(0), P(1), new[] { behind }, null, 1000, 10, null,
            out var independent, out _, out var evidence, allowReverseDeparture: true),
            "An independently framed cut inherited an irrelevant continuity veto");
        Check(independent != null && independent.Destination.Id == "known-behind" &&
            independent.Destination.Position.Distance(behind.Position) < .001 && evidence.HeadingExcluded == 0,
            "Reverse departure fabricated its target or claimed a heading rejection");
        Check(ScenicJourneyItinerary.TrySelect(P(0), P(1), new[] { behind, Region("known-ahead", 1000) }, null, 1000, 10, null,
            out var preferred, out _, out _, allowReverseDeparture: true) && preferred?.Destination.Id == "known-ahead",
            "Independent departure discarded forward continuity as a ranking preference");
    }
    private static void FreshDestination()
    {
        foreach (WorldInterestSite site in new[] { Site(age: 600), Site(age: 601), Site(retry: 1) })
            Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), Line(),
                new Dictionary<string, WorldInterestSite> { [site.Id] = site }, 320, 10, null, out _, out _), "Stale/cooled-down site became a destination");
        var line = Line(); line[3] = Region("headland", 320, after: 11);
        Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), line, null, 320, 10, null, out _, out _), "Region retry deadline ignored");
    }
    private static void LandscapeVariety()
    {
        var regions = Line();
        for (int i = 0; i < 80; i++) regions.Add(Region("ruin-" + i, 320, i % 4, kind: ScenicDestinationKind.Landmark));
        Check(Select(regions).Destination.Id == "headland", "Structure count dominated the coastal journey");
    }
    private static void RecentDestination()
    {
        Check(!ScenicJourneyItinerary.TrySelect(P(0), P(1), Line(), null, 320, 10,
            new[] { "headland" }, out _, out _), "Recently covered destination immediately repeated");
    }
    private static void TurningGuide()
    {
        var route = Select(new[] { Region("west", 160), Region("corner", 320), Region("north", 320, 160), Region("bay", 320, 320) }, 640);
        Check(route.Destination.Id == "bay", "Turning route selected an intermediate observation");
        Near(route.RouteMetres, 640);
        AutonomyPosition ahead = route.SteeringGoal(P(320, 40));
        Near(ahead.X, 320); Check(ahead.Z > 40, "Steering continued east beyond the observed turn");
        Near(route.ProjectProgress(P(320, 40)), 360);
    }
    private static ScenicShotPlan Plan() => ScenicShotPlan.Spatial(2,
        new ScenicFeature("headland", ScenicFeatureKind.Coastline, P(10, 20), P(1), 12, 5, 1), ScenicShotRole.Traverse,
        new[] { P(0), P(10), P(20) }, new[] { P(0, 20), P(10, 20), P(20, 20) }, 65).AsJourney(1, 1, 100);
    private static void CurvedArrival()
    {
        var route = Select(new[] { Region("west", 160), Region("corner", 320), Region("north", 320, 160), Region("bay", 320, 320) }, 640);
        var policy = new ScenicJourneyPolicy(); policy.Start(1, route, 6); var plan = Plan();
        int frames = (int)Math.Ceiling(route.RouteMetres * 2);
        for (int i = 0; i <= frames; i++)
            policy.Observe(plan, i / 12d, Math.Min(.9, i / 2000d), true, false, route.PositionAt(i * .5));
        Check(policy.AtArrival, "Route turn was tested against the obsolete initial heading");
        Near(policy.RouteProgressMetres, 640); Near(policy.ObjectiveAdvanceMetres, 640);
        for (int i = 1; i <= 40; i++)
        {
            double at = (frames + i) / 12d;
            policy.Observe(plan, at, .9, true, false, route.Destination.Position);
            policy.ObserveArrival(at, true, false, route.Destination.Position, true);
        }
        Check(policy.TargetReached, "Curved itinerary never completed its ending");
    }
    private static ScenicJourneyPolicy Policy()
    { var policy = new ScenicJourneyPolicy(); policy.Start(1, Select(Line()), 6); return policy; }
    private static double Travel(ScenicJourneyPolicy policy, ScenicShotPlan plan, float metres = 320)
    {
        int frames = (int)(metres / .5f);
        for (int i = 0; i <= frames; i++) policy.Observe(plan, i / 12d, Math.Min(.9, i / 2000d), true, false, P(i * .5f));
        return frames / 12d;
    }
    private static void ActualArrival()
    {
        var policy = Policy(); var plan = Plan(); double at = Travel(policy, plan);
        Check(policy.AtArrival && !policy.TargetReached, "Coordinate arrival completed without an ending");
        for (int i = 1; i <= 40; i++)
        {
            double now = at + i / 12d;
            policy.Observe(plan, now, .9, true, false, P(320));
            policy.ObserveArrival(now, true, false, P(320), true);
        }
        Check(policy.TargetReached && policy.ArrivalShownSeconds >= 3, "Readable arrival did not complete");
        policy.Finish(ScenicJourneyOutcome.Completed, "Arrival presented");
        Check(policy.Outcome == ScenicJourneyOutcome.Completed, "Valid arrival rejected");
    }
    private static void HiddenArrival()
    {
        var policy = Policy(); var plan = Plan(); double at = Travel(policy, plan, 145);
        policy.Observe(plan, at + .1, .9, false, false, null);
        policy.Observe(plan, at + .2, .9, true, false, P(320));
        for (int i = 3; i < 70; i++)
        {
            policy.Observe(plan, at + i / 10d, .9, true, false, P(320));
            policy.ObserveArrival(at + i / 10d, true, false, P(320), true);
        }
        Check(!policy.AtArrival && !policy.TargetReached, "Unobserved relocation earned the missing travel");
        policy.Finish(ScenicJourneyOutcome.Completed, "Claimed arrival");
        Check(policy.Outcome == ScenicJourneyOutcome.Blocked, "Partial travel falsely completed");
    }
    private static void UnusableArrival()
    {
        var policy = Policy(); var plan = Plan(); double at = Travel(policy, plan);
        for (int i = 1; i <= 100; i++)
        {
            double now = at + i / 12d;
            policy.Observe(plan, now, .9, true, false, P(320));
            policy.ObserveArrival(now, true, i < 50, P(320), false);
        }
        Near(policy.ArrivalShownSeconds, 0); Check(!policy.TargetReached, "Held/hidden destination earned completion");
    }
    private static void Bounds()
    {
        Reject(() => ScenicJourneyItinerary.TrySelect(P(0), P(1), new ScenicRegion[1025], null, 1000, 10, null, out _, out _));
        Reject(() => ScenicJourneyItinerary.TrySelect(P(0), P(1), Line(), null, double.NaN, 10, null, out _, out _));
        var route = Select(Line()); Reject(() => route.SteeringGoal(P(0), 193));
        Near(route.PositionAt(10000).Distance(route.Destination.Position), 0);
        var policy = Policy(); policy.Reset(); Check(policy.Itinerary == null && !policy.AtArrival && policy.ArrivalShownSeconds == 0, "Reset retained itinerary state");
    }
}
