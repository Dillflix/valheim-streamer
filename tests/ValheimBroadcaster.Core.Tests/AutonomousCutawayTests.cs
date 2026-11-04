using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class AutonomousCutawayTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Wildlife cutaways preserve player dwell without acquisition failure or travel", SuspendAcquisition);
        yield return ("Returning from wildlife requires actual player footage and fresh acquisition timing", ResumeEvidence);
        yield return ("Wildlife return retains honest reach-loss grace and current coordinate freshness", ReturnReachability);
        yield return ("Manual player changes and holds terminate autonomous wildlife suppression", ManualOverride);
        yield return ("A departing retained player cannot be resurrected by a wildlife cutaway", RosterChange);
        yield return ("Wildlife cannot interrupt pending active external or diagnostic travel", TravelExclusion);
        yield return ("Wildlife preserves coverage fairness without inventing companion footage", CoverageFairness);
        yield return ("Wildlife suppression clears on stop world reset and manual pause", Lifecycle);
    }

    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static readonly ScenicRegion[] NoRegions = Array.Empty<ScenicRegion>();
    private static AutonomyPlayer Player(string id, bool ready = true, double reach = 0, double expires = 10000) =>
        new AutonomyPlayer(id, id, new AutonomyPosition((float)reach, 0, 0), expires, ready, reach);
    private static AutonomousDirector New()
    {
        var director = new AutonomousDirector { DwellSeconds = 30, AcquisitionSeconds = 6, MinimumTravelSeconds = 1 };
        director.SetEnabled(true); return director;
    }
    private static void Tick(AutonomousDirector director, double now, IReadOnlyList<AutonomyPlayer> players,
        string? shown = null, bool cutaway = false, double dt = 1, bool busy = false) =>
        director.Tick(now, dt, players, NoRegions, Origin, shown, false, busy, cutaway);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static AutonomousDirector Following(IReadOnlyList<AutonomyPlayer>? players = null)
    {
        var director = New(); Tick(director, 0, players ?? new[] { Player("a") }, "a");
        Check(director.CanStartWildlifeCutaway, "A presented player did not establish cutaway eligibility.");
        return director;
    }

    private static void SuspendAcquisition()
    {
        var director = Following(); double dwell = director.ReadyDwellSeconds;
        var distant = new[] { Player("a", false, 1000), Player("b") };
        for (int second = 1; second <= 120; second++)
        {
            Tick(director, second, distant, cutaway: true);
            Check(director.WildlifeCutawayActive && director.TargetId == "a" && director.ReadyDwellSeconds == dwell,
                "An intentional cutaway advanced player dwell or changed its retained target.");
            Check(director.PendingTravel == null && director.ActiveTravel == null && director.FailedDestinationCount == 0,
                "Missing player footage during wildlife triggered acquisition failure or travel.");
            Check(!director.PresentationGraceActive, "Wildlife was misreported as player presentation grace.");
        }
    }

    private static void ResumeEvidence()
    {
        var director = Following(); var ready = new[] { Player("a") };
        Tick(director, 30, ready, cutaway: true, dt: 30);
        Tick(director, 31, ready);
        Check(!director.WildlifeCutawayActive && director.ReadyDwellSeconds == 1 &&
            director.State == AutonomyState.Acquiring && director.FailedDestinationCount == 0,
            "Return fabricated player footage or inherited the cutaway acquisition gap.");
        Tick(director, 32, ready, "a");
        Check(director.ReadyDwellSeconds == 2 && director.State == AutonomyState.Following,
            "Actual return footage failed to resume the retained dwell.");

        director = Following(); var unready = new[] { Player("a", false) };
        Tick(director, 30, unready, cutaway: true, dt: 30);
        Tick(director, 31, unready);
        Tick(director, 36.9, unready, dt: 5.9);
        Check(director.TargetId == "a" && director.FailedDestinationCount == 0 && director.PendingTravel == null,
            "Local return acquisition failed before its fresh deadline or invented distant travel.");
        Tick(director, 37.1, unready, dt: 0.2);
        Check(director.FailedDestinationCount == 1, "Return acquisition lost its normal bounded deadline.");
    }

    private static void ReturnReachability()
    {
        var director = Following(); var distant = new[] { Player("a", false, 1000) };
        Tick(director, 20, distant, cutaway: true, dt: 20);
        Tick(director, 21, distant);
        Check(director.PendingTravel == null && director.PresentationGraceActive,
            "Return immediately travelled using the hidden cutaway interval as reach-loss evidence.");
        Tick(director, 23.9, distant, dt: 2.9);
        Check(director.PendingTravel == null, "Return bypassed continuous reach-loss grace.");
        Tick(director, 24.1, distant, dt: 0.2);
        Check(director.PendingTravel?.TargetId == "a", "Genuine sustained distant movement could no longer acquire after return.");

        director = Following(); var stale = new[] { Player("a", false, 1000, 2) };
        Tick(director, 20, stale, cutaway: true, dt: 20);
        Tick(director, 21, stale);
        Tick(director, 25, stale, dt: 4);
        Check(director.PendingTravel == null, "Return used a stale coordinate retained before wildlife.");
    }

    private static void ManualOverride()
    {
        var players = new[] { Player("a"), Player("b") };
        var director = Following(players);
        Tick(director, 1, players, cutaway: true); director.Next();
        Tick(director, 2, players, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.TargetId == "b", "Manual next waited behind wildlife.");
        Tick(director, 3, players, "b"); Tick(director, 4, players, cutaway: true);
        director.Select("a"); Tick(director, 5, players, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.TargetId == "a", "Manual selection waited behind wildlife.");
        Tick(director, 6, players, "a"); Tick(director, 7, players, cutaway: true);
        director.SetHolding(true); Tick(director, 8, players, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.Held && !director.CanStartWildlifeCutaway,
            "Wildlife consumed or ignored an explicit operator hold.");
    }

    private static void RosterChange()
    {
        var director = Following(new[] { Player("a"), Player("b") });
        Tick(director, 1, new[] { Player("a"), Player("b") }, cutaway: true);
        Tick(director, 2, new[] { Player("b") }, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.TargetId == "b", "A departed retained player blocked its surviving peer.");
        Tick(director, 3, Array.Empty<AutonomyPlayer>(), cutaway: true);
        Check(director.TargetId == null && !director.WildlifeCutawayActive, "Empty roster preserved a phantom cutaway target.");
    }

    private static void TravelExclusion()
    {
        var director = New(); var distant = new[] { Player("a", false, 1000) };
        Tick(director, 0, distant, cutaway: true);
        long token = director.PendingTravel?.Token ?? throw new Exception("Unpresented target was incorrectly granted a wildlife cutaway.");
        Tick(director, 1, distant, cutaway: true);
        Check(!director.CanStartWildlifeCutaway && !director.WildlifeCutawayActive && director.PendingTravel?.Token == token,
            "Wildlife hid an existing travel request.");
        director.TravelStarted(token, 1); Tick(director, 2, distant, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.ActiveTravel?.Token == token && director.State == AutonomyState.Travelling,
            "Wildlife interrupted protected active travel.");
        director = Following(); Tick(director, 1, new[] { Player("a") }, cutaway: true, busy: true);
        Check(!director.WildlifeCutawayActive && director.State == AutonomyState.Travelling, "Wildlife ignored externally controlled travel.");
        director = Following();
        director.BeginRouteTest(new[] { new ScenicRegion("route", "route", new AutonomyPosition(1000, 0, 0)) }, 1);
        Tick(director, 1, new[] { Player("a") }, cutaway: true);
        Check(!director.WildlifeCutawayActive && director.PendingTravel?.Kind == AutonomyTravelKind.RouteTest,
            "Wildlife stole a diagnostic route.");
    }

    private static void CoverageFairness()
    {
        var players = new[] { Player("a"), Player("b") };
        var director = New(); director.DwellSeconds = 8;
        for (int second = 0; second < 6; second++)
        {
            director.RecordCoverage(second, 1, new[] { "a", "b" });
            Tick(director, second, players, "a");
        }
        for (int second = 6; second < 10; second++)
        {
            director.RecordCoverage(second, 1, Array.Empty<string>());
            Tick(director, second, players, cutaway: true);
        }
        Tick(director, 10, players, "a"); Tick(director, 11, players, "a");
        Check(director.TargetId == "a", "Cutaway reset real cooperative viewing credit.");
        Tick(director, 15, players, "a");
        Check(director.TargetId == "b", "Unseen companion coverage was renewed by wildlife footage.");
    }

    private static void Lifecycle()
    {
        var players = new[] { Player("a") }; var director = Following();
        Tick(director, 1, players, cutaway: true); director.SetPaused(true);
        Check(!director.WildlifeCutawayActive, "Manual pause retained cutaway state.");
        Tick(director, 20, players, cutaway: true); director.SetPaused(false); Tick(director, 21, players);
        Check(director.FailedDestinationCount == 0, "Resume inherited a paused cutaway failure deadline.");
        Tick(director, 22, players, "a"); Tick(director, 23, players, cutaway: true);
        director.SetEnabled(false);
        Check(!director.WildlifeCutawayActive && !director.CanStartWildlifeCutaway, "Stop retained wildlife eligibility.");
        director.Reset(); director.SetEnabled(true); Tick(director, 0, players, cutaway: true);
        Check(!director.WildlifeCutawayActive && !director.CanStartWildlifeCutaway && director.ReadyDwellSeconds == 0,
            "New world inherited prior player footage or wildlife state.");
    }
}
