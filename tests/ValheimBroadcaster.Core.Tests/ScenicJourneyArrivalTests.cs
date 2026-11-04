using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicJourneyArrivalTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Ending a Journey establishes the actual local visit instead of immediately spending source-region dwell", FreshVisit);
        yield return ("A partial Journey ending in an unclassified area discards the source destination identity", UnknownEnding);
        yield return ("Journey ending never delays content exhaustion or missing-picture recovery", PictureRecovery);
        yield return ("Journey completion does not impose a transport cooldown on newly available players", PlayerPriority);
        yield return ("A late Journey callback cannot supersede a pending transport owner", TransportOwner);
        yield return ("Partial Journey proximity to a structure never fabricates architectural coverage", StructuralEvidence);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static AutonomyPosition P(float x) => new AutonomyPosition(x, 5, 0);
    private static readonly ScenicRegion[] Regions = {
        new ScenicRegion("source", "Original meadow", P(0)),
        new ScenicRegion("arrival", "Known coast", P(1000), landscape: ScenicLandscape.Coast),
        new ScenicRegion("next", "Another meadow", P(2000)) };
    private static AutonomousDirector Journey()
    {
        var director = new AutonomousDirector { RegionSeconds = 5, MinimumTravelSeconds = 8 };
        director.SetEnabled(true);
        for (int second = 0; second <= 10; second++)
            director.Tick(second, 1, Array.Empty<AutonomyPlayer>(), Regions, P(second * 100), null, true, scenicJourneyActive: true);
        Check(director.CurrentRegionId == "source" && director.PendingTravel == null, "Fixture did not retain the original scenic owner");
        return director;
    }
    private static void FreshVisit()
    {
        var director = Journey(); Check(director.ScenicJourneyEnded(Regions[1], 10), "Ending was not accepted");
        director.Tick(11, 1, Array.Empty<AutonomyPlayer>(), Regions, P(1000), null, true);
        Check(director.CurrentRegionId == "arrival" && director.PendingTravel == null && director.VisitedRegionCount == 2,
            "The old dwell caused immediate relocation");
        Check(director.CompletedTravels == 0 && director.FailedTravels == 0, "Physical camera travel fabricated a teleport result");
    }
    private static void UnknownEnding()
    {
        var director = Journey(); director.ScenicJourneyEnded(null, 10);
        director.Tick(11, 1, Array.Empty<AutonomyPlayer>(), Regions, P(500), null, true);
        Check(director.CurrentRegionId == null && director.CurrentRegionName == null && director.PendingTravel == null,
            "Unknown partial ending retained or invented a destination");
    }
    private static void PictureRecovery()
    {
        foreach (bool live in new[] { false, true })
        {
            var director = Journey(); director.ScenicJourneyEnded(Regions[1], 10);
            director.Tick(11, 1, Array.Empty<AutonomyPlayer>(), Regions, P(1000), null, live,
                idleSceneAvailable: true, idleSearchExpired: !live, scenicContentExhausted: live);
            Check(director.PendingTravel != null, "Journey ending delayed actual content exhaustion or a picture outage");
        }
    }
    private static void PlayerPriority()
    {
        var director = Journey(); director.ScenicJourneyEnded(Regions[1], 10);
        var player = new AutonomyPlayer("player", "Player", P(3000), 100, false, 2000);
        director.Tick(11, 1, new[] { player }, Regions, P(1000), null, true);
        Check(director.PendingTravel?.Kind == AutonomyTravelKind.Player && director.TargetId == "player",
            "New player acquisition inherited a scenic Journey ending cooldown");
    }
    private static void StructuralEvidence()
    {
        foreach (bool presented in new[] { false, true })
        {
            var director = Journey(); director.RegionSeconds = 1000;
            var village = new ScenicRegion("village", "Village", P(1000), kind: ScenicDestinationKind.Base);
            director.ScenicJourneyEnded(village, 10, presented);
            for (int second = 11; second <= 30; second++)
                director.Tick(second, 1, Array.Empty<AutonomyPlayer>(), Regions, P(1000), null, true);
            Check(director.CurrentRegionId == "village", "Ending location identity was discarded");
            Check(director.SustainedScenicVisits == (presented ? 1 : 0),
                "Structural viewing credit was not separated from merely ending nearby");
        }
    }
    private static void TransportOwner()
    {
        var director = Journey();
        var player = new AutonomyPlayer("player", "Player", P(3000), 100, false, 2000);
        director.Tick(11, 1, new[] { player }, Regions, P(1000), null, true);
        AutonomyTravelRequest request = director.PendingTravel ?? throw new Exception("Missing player transport");
        Check(!director.ScenicJourneyEnded(Regions[1], 11) && ReferenceEquals(request, director.PendingTravel),
            "Late camera callback replaced transport state");
    }
}
