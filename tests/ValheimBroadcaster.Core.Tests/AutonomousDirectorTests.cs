using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class AutonomousDirectorTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Autonomy rotates from a loaded player to a distant player", NearFarRotation),
        ("Autonomy counts only actual target footage and bounds stalled-frame dwell", PresentedFootage),
        ("Autonomy uses fresh positions and preserves loaded stale-coordinate subjects", PositionFreshness),
        ("Connected players without positions leave scenic travel available", PendingPresence),
        ("Lone failed player retries with bounded exponential cooldown", LoneRetry),
        ("Unaccepted travel requests expire without repeated immediate requests", RequestExpiry),
        ("Travel destinations remain immutable through a portal update", PortalDuringTravel),
        ("Acquisition deadline includes completed native travel", TotalAcquisitionBudget),
        ("Acquisition limits repeated relocation attempts", RelocationLimit),
        ("Disconnected and stale players yield to available subjects", TargetLoss),
        ("Player arrival preempts scenery after the current trip finishes", DeferredPreemption),
        ("Hold pauses rotation but explicit next and previous still work", HoldAndManual),
        ("Explicit target selection keeps stable identity across rename", ManualSelection),
        ("Scenic travel chooses fresh deterministic destinations without early revisits", ScenicNovelty),
        ("Scenic selection enforces minimum and maximum travel distances", ScenicBounds),
        ("Unavailable scenery triggers a bounded search independent of footage dwell", ScenicRecovery),
        ("Scenic region dwell requires useful rendered footage", ScenicFootage),
        ("Failed scenic destinations yield to alternatives", ScenicFailure),
        ("Multi-hour scenery history remains bounded", BoundedHistory),
        ("Full scenic history preserves unexpired revisit exclusions", FullHistoryCooldown),
        ("Repeated distinct failures retain bounded history", BoundedFailures),
        ("Stopping clears requests and ignores late callbacks", StopClears),
        ("World reset cannot reuse a stale travel completion token", ResetTokens),
        ("Manual pause prevents travel and restarts acquisition timing on resume", ManualPause),
        ("Solo routes use the travel transaction and require ready footage", SoloRoute),
        ("Solo route failures and cancellation preserve explicit results", SoloRouteFailure),
        ("External travel cannot be interrupted by autonomous requests", ExternalTravel),
        ("Autonomy rejects invalid clocks coordinates and discovery bounds", InvalidInputs)
    };

    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static AutonomousDirector New()
    {
        var d = new AutonomousDirector { DwellSeconds = 2, RegionSeconds = 2, AcquisitionSeconds = 5,
            RetrySeconds = 10, RevisitSeconds = 1000, MinimumTravelSeconds = 1, MinimumScenicTravelDistance = 100 };
        d.SetEnabled(true); return d;
    }
    private static AutonomyPlayer P(string id, float x = 0, bool ready = true, double expires = 100000) =>
        new AutonomyPlayer(id, id, new AutonomyPosition(x, 0, 0), expires, ready);
    private static ScenicRegion R(string id, float x) => new ScenicRegion(id, "Region " + id, new AutonomyPosition(x, 0, 0));
    private static void Step(AutonomousDirector d, double now, IReadOnlyList<AutonomyPlayer>? players = null,
        IReadOnlyList<ScenicRegion>? regions = null, AutonomyPosition? position = null, string? shown = null,
        bool ready = true, double dt = 1, bool busy = false) =>
        d.Tick(now, dt, players ?? Array.Empty<AutonomyPlayer>(), regions ?? Array.Empty<ScenicRegion>(), position ?? Origin, shown, ready, busy);
    private static AutonomyTravelRequest Request(AutonomousDirector d) => d.PendingTravel ?? throw new Exception("Expected a pending travel request");
    private static void Arrive(AutonomousDirector d, double now)
    { long token = Request(d).Token; d.TravelStarted(token, now); d.TravelCompleted(token, true, now + 1); }
    private static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected an argument error"); }

    private static void NearFarRotation()
    {
        var d = New(); var players = new[] { P("b", 1000, false), P("a") };
        Step(d, 0, players, shown: "a"); Eq("a", d.TargetId);
        Step(d, 1, players, shown: "a"); Eq("b", d.TargetId);
        Step(d, 2, players); Eq("b", Request(d).TargetId); Eq(AutonomyTravelKind.Player, Request(d).Kind);
        Arrive(d, 2);
        players = new[] { P("b", 1000), P("a", 0, false) };
        Step(d, 4, players, position: new AutonomyPosition(1000, 0, 0), shown: "b");
        Step(d, 5, players, position: new AutonomyPosition(1000, 0, 0), shown: "b"); Eq("a", d.TargetId);
        Step(d, 6, players, position: new AutonomyPosition(1000, 0, 0)); Eq("a", Request(d).TargetId);
    }
    private static void PresentedFootage()
    {
        var d = New(); var players = new[] { P("a"), P("b") };
        Step(d, 0, players, shown: null); Eq(0d, d.ReadyDwellSeconds);
        Step(d, 1, players, shown: "b"); Eq("a", d.TargetId); Eq(0d, d.ReadyDwellSeconds);
        Step(d, 3600, players, shown: "a", dt: 3600); Eq("a", d.TargetId); Eq(1d, d.ReadyDwellSeconds);
        Step(d, 3601, players, shown: "a"); Eq("b", d.TargetId);
    }
    private static void PositionFreshness()
    {
        var d = New(); Step(d, 10, new[] { P("a", 1000, false, 10) });
        Assert(d.TargetId == null && d.PendingTravel == null);
        Step(d, 11, new[] { P("a", 1000, true, 10) }, shown: "a"); Eq("a", d.TargetId); Eq(AutonomyState.Following, d.State);
    }
    private static void PendingPresence()
    {
        var d = New(); var players = new[] { new AutonomyPlayer("a", "Unspawned", null, 0, false) };
        var regions = new[] { R("a", 0), R("b", 300) };
        Step(d, 0, players, regions); Assert(d.TargetId == null); Assert(d.Reason.Contains("waiting for an available player"));
        Step(d, 1, players, regions); Eq(AutonomyTravelKind.Scenic, Request(d).Kind);
    }
    private static void LoneRetry()
    {
        var d = New(); var players = new[] { P("a", 1000, false) };
        Step(d, 0, players); d.TravelCompleted(Request(d).Token, false, 0, "Unavailable destination");
        Step(d, 9, players); Assert(d.PendingTravel == null); Eq(1d, d.RetryAfterSeconds(9));
        Step(d, 10, players); d.TravelCompleted(Request(d).Token, false, 10);
        Eq(20d, d.RetryAfterSeconds(10)); Step(d, 29, players); Assert(d.PendingTravel == null);
        Step(d, 30, players); Eq("a", Request(d).TargetId);
    }
    private static void RequestExpiry()
    {
        var d = New(); var players = new[] { P("a", 1000, false) };
        Step(d, 0, players); long token = Request(d).Token;
        Step(d, 4, players); Eq(token, Request(d).Token);
        Step(d, 5, players); Assert(d.PendingTravel == null); Eq(1L, d.FailedTravels);
        Step(d, 6, players); Assert(d.PendingTravel == null);
    }
    private static void PortalDuringTravel()
    {
        var d = New(); d.AcquisitionSeconds = 20;
        Step(d, 0, new[] { P("a", 1000, false) }); long token = Request(d).Token; d.TravelStarted(token, 0);
        Step(d, 1, new[] { P("a", 2000, false) }); Eq(1000f, d.ActiveTravel!.Position.X); Assert(d.PendingTravel == null);
        d.TravelCompleted(token, true, 2);
        Step(d, 3, new[] { P("a", 2000, false) }, position: new AutonomyPosition(1000, 0, 0));
        Eq(2000f, Request(d).Position.X);
    }
    private static void TotalAcquisitionBudget()
    {
        var d = New(); var players = new[] { P("a", 1000, false) };
        Step(d, 0, players); long token = Request(d).Token; d.TravelStarted(token, 0);
        Step(d, 4, players, busy: true); d.TravelCompleted(token, true, 4);
        Step(d, 5, new[] { P("a", 2000, false) }, position: new AutonomyPosition(1000, 0, 0));
        Assert(d.PendingTravel == null && d.TargetId == null); Assert(d.LastFailure!.Contains("deadline"));
    }
    private static void RelocationLimit()
    {
        var d = New(); d.AcquisitionSeconds = 65;
        for (int i = 0; i < 3; i++)
        {
            Step(d, i * 2, new[] { P("a", (i + 1) * 1000, false) }, position: new AutonomyPosition(i * 1000, 0, 0));
            Arrive(d, i * 2);
        }
        Step(d, 6, new[] { P("a", 4000, false) }, position: new AutonomyPosition(3000, 0, 0));
        Assert(d.PendingTravel == null); Eq(3L, d.CompletedTravels);
        Step(d, 65, new[] { P("a", 4000, false) }); Assert(d.TargetId == null);
    }
    private static void TargetLoss()
    {
        var d = New(); Step(d, 0, new[] { P("a"), P("b") }, shown: "a");
        Step(d, 1, new[] { P("a", 0, false, 0), P("b") }, shown: "b"); Eq("b", d.TargetId);
        Step(d, 2, new[] { P("a") }, shown: "a"); Eq("a", d.TargetId);
    }
    private static void DeferredPreemption()
    {
        var d = New(); var regions = new[] { R("a", 0), R("b", 300) };
        Step(d, 0, regions: regions); Step(d, 1, regions: regions);
        long token = Request(d).Token; d.TravelStarted(token, 1);
        Step(d, 2, new[] { P("player", 1000, false) }, regions); Eq(AutonomyTravelKind.Scenic, d.ActiveTravel!.Kind); Assert(d.TargetId == null);
        d.TravelCompleted(token, true, 3);
        Step(d, 4, new[] { P("player", 1000, false) }, regions, new AutonomyPosition(300, 0, 0));
        Eq("player", d.TargetId); Eq(AutonomyTravelKind.Player, Request(d).Kind);
    }
    private static void HoldAndManual()
    {
        var d = New(); d.SetHolding(true); var players = new[] { P("a"), P("b") };
        Step(d, 0, players, shown: "a"); Step(d, 100, players, shown: "a"); Eq("a", d.TargetId);
        d.Next(); Step(d, 101, players, shown: "b"); Eq("b", d.TargetId);
        d.Next(-1); Step(d, 102, players, shown: "a"); Eq("a", d.TargetId);
        d.SetHolding(false); Step(d, 103, players, shown: "a"); Step(d, 104, players, shown: "a"); Eq("b", d.TargetId);
    }
    private static void ManualSelection()
    {
        var d = New(); d.DwellSeconds = 10; var players = new[] { P("a"), P("b") };
        Step(d, 0, players, shown: "a"); d.Select("b"); Step(d, 1, players, shown: "b");
        long revision = d.Revision;
        Step(d, 2, new[] { P("a"), new AutonomyPlayer("b", "Renamed", Origin, 100, true) }, shown: "b");
        Eq("b", d.TargetId); Eq("Renamed", d.TargetName); Eq(revision, d.Revision); Eq(2d, d.ReadyDwellSeconds);
    }
    private static void ScenicNovelty()
    {
        var d = New(); var regions = new[] { R("c", 600), R("b", 300), R("a", 0) };
        Step(d, 0, regions: regions); Step(d, 1, regions: regions); Eq("b", Request(d).TargetId); Arrive(d, 1);
        Step(d, 3, regions: regions, position: new AutonomyPosition(300, 0, 0));
        Step(d, 4, regions: regions, position: new AutonomyPosition(300, 0, 0)); Eq("c", Request(d).TargetId); Arrive(d, 4);
        Step(d, 6, regions: regions, position: new AutonomyPosition(600, 0, 0));
        Step(d, 7, regions: regions, position: new AutonomyPosition(600, 0, 0));
        Assert(d.PendingTravel == null); Assert(d.Reason.Contains("No fresh")); Eq(3, d.VisitedRegionCount);
    }
    private static void ScenicBounds()
    {
        var d = New(); d.MaximumScenicTravelDistance = 1000;
        var regions = new[] { R("near", 50), R("far", 2000), R("origin", 0) };
        Step(d, 0, regions: regions); Step(d, 1, regions: regions); Assert(d.PendingTravel == null);
        Step(d, 2, regions: new[] { R("c", -300), R("b", 300), R("origin", 0) }); Eq("b", Request(d).TargetId);
    }
    private static void ScenicRecovery()
    {
        var d = New(); d.RegionSeconds = 100; var regions = new[] { R("a", 0), R("b", 300) };
        Step(d, 0, regions: regions, ready: false); Step(d, 9, regions: regions, ready: false); Assert(d.PendingTravel == null);
        Step(d, 10, regions: regions, ready: false); Eq("b", Request(d).TargetId);
    }
    private static void ScenicFootage()
    {
        var d = New(); d.UnreadyScenicSeconds = 100; var regions = new[] { R("a", 0), R("b", 300) };
        Step(d, 0, regions: regions, ready: false); Step(d, 1, regions: regions, ready: false); Assert(d.PendingTravel == null);
        Step(d, 2, regions: regions); Assert(d.PendingTravel == null);
        Step(d, 3, regions: regions); Eq("b", Request(d).TargetId);
    }
    private static void ScenicFailure()
    {
        var d = New(); var regions = new[] { R("a", 0), R("b", 300), R("c", 600) };
        Step(d, 0, regions: regions); Step(d, 1, regions: regions); Eq("b", Request(d).TargetId);
        d.TravelCompleted(Request(d).Token, false, 1); Step(d, 2, regions: regions); Eq("c", Request(d).TargetId);
    }
    private static void BoundedHistory()
    {
        var d = New(); d.RegionSeconds = 1; var position = Origin; double now = 0;
        for (int i = 0; i < 500; i++)
        {
            var next = new AutonomyPosition((i + 1) % 15 * 150, 0, (i + 1) % 100 / 15 * 150);
            Step(d, now, regions: new[] { new ScenicRegion("r" + i, "Generated region", next) }, position: position);
            Arrive(d, now); now += 121; position = next;
            Assert(d.VisitedRegionCount <= 128);
        }
        Eq(128, d.VisitedRegionCount); Eq(500L, d.CompletedTravels);
    }
    private static void FullHistoryCooldown()
    {
        var d = New(); d.RegionSeconds = 1; d.RevisitSeconds = 1800;
        var position = Origin; double now = 0;
        for (int i = 0; i < 128; i++)
        {
            var next = new AutonomyPosition((i + 1) % 15 * 150, 0, (i + 1) / 15 * 150);
            Step(d, now, regions: new[] { new ScenicRegion("r" + i, "Region", next) }, position: position);
            Arrive(d, now); now += 2; position = next;
        }
        var extra = R("extra", 4000);
        Step(d, now, regions: new[] { extra, R("r0", 150) }, position: position);
        Assert(d.PendingTravel == null); Eq(128, d.VisitedRegionCount); Assert(d.Reason.Contains("history is full"));
        Step(d, now + 1, regions: new[] { R("r0", 150) }, position: position); Assert(d.PendingTravel == null);
        Step(d, 1801, regions: new[] { extra }, position: position); Eq("extra", Request(d).TargetId);
    }
    private static void BoundedFailures()
    {
        var d = New();
        for (int i = 0; i < 500; i++)
        {
            Step(d, i * 40, new[] { P("p" + i, 1000, false) });
            d.TravelCompleted(Request(d).Token, false, i * 40);
            Assert(d.FailedDestinationCount <= 128);
        }
        Eq(128, d.FailedDestinationCount);
    }
    private static void StopClears()
    {
        var d = New(); Step(d, 0, new[] { P("a", 1000, false) }); long token = Request(d).Token; d.TravelStarted(token, 0);
        d.SetEnabled(false); Step(d, 1, new[] { P("a", 1000, false) });
        d.TravelCompleted(token, true, 2); Assert(d.PendingTravel == null && d.ActiveTravel == null); Eq(0L, d.CompletedTravels); Eq(AutonomyState.Disabled, d.State);
    }
    private static void ResetTokens()
    {
        var d = New(); Step(d, 50, new[] { P("a", 1000, false) }); long old = Request(d).Token;
        d.Reset(); d.SetEnabled(true); Step(d, 0, new[] { P("b", 1000, false) });
        long current = Request(d).Token; Assert(current != old); d.TravelCompleted(old, true, 0);
        Eq(current, Request(d).Token); Eq(0L, d.CompletedTravels);
    }
    private static void ManualPause()
    {
        var d = New(); var players = new[] { P("a", 1000, false) };
        Step(d, 0, players); d.SetPaused(true); Step(d, 100, players); Assert(d.PendingTravel == null); Eq(AutonomyState.Paused, d.State);
        d.SetPaused(false); Step(d, 101, players); Eq("a", Request(d).TargetId); Assert(d.LastFailure == null);
    }
    private static void SoloRoute()
    {
        var d = New(); d.BeginRouteTest(new[] { R("one", 300), R("two", 600) }, 0);
        Step(d, 0); Eq(AutonomyTravelKind.RouteTest, Request(d).Kind); Arrive(d, 0);
        Step(d, 2); Step(d, 3); Assert(d.RouteTestActive); Step(d, 4); Step(d, 5); Eq("two", Request(d).TargetId); Arrive(d, 5);
        Step(d, 7); Step(d, 8); Step(d, 9); Assert(!d.RouteTestActive); Assert(d.RouteTestResult.StartsWith("Passed 2"));
        Step(d, 10, new[] { P("a") }, shown: "a"); Eq(AutonomyState.Following, d.State);
    }
    private static void SoloRouteFailure()
    {
        var d = New(); d.BeginRouteTest(new[] { R("one", 300) }, 0); Step(d, 0); Arrive(d, 0);
        Step(d, 6, ready: false); Assert(!d.RouteTestActive); Assert(d.RouteTestResult.StartsWith("Failed scenic readiness"));
        d.BeginRouteTest(new[] { R("two", 600) }, 7); Step(d, 7); long token = Request(d).Token; d.TravelStarted(token, 7);
        d.StopRouteTest(); d.TravelCompleted(token, true, 8); Assert(!d.RouteTestActive); Eq("Solo route stopped", d.RouteTestResult);
    }
    private static void ExternalTravel()
    {
        var d = New(); Step(d, 0, new[] { P("a", 1000, false) }, busy: true);
        Assert(d.PendingTravel == null); Eq(AutonomyState.Travelling, d.State);
        Step(d, 1, new[] { P("a", 1000, false) }); Eq("a", Request(d).TargetId);
    }
    private static void InvalidInputs()
    {
        var d = New(); Throws(() => Step(d, double.NaN)); Throws(() => Step(d, 0, dt: -1));
        Throws(() => new AutonomyPosition(float.NaN, 0, 0)); Throws(() => new AutonomyPosition(20001, 0, 0));
        Step(d, 10); Throws(() => Step(d, 9)); d.Reset(); d.SetEnabled(true); Step(d, 0);
        Throws(() => d.DwellSeconds = 0); Throws(() => d.Next(0)); Throws(() => d.Select(""));
        Throws(() => Step(d, 1, players: new AutonomyPlayer[129]));
        Throws(() => Step(d, 1, regions: new ScenicRegion[1025]));
        d.MaximumScenicTravelDistance = 50; Throws(() => Step(d, 1));
    }
}
