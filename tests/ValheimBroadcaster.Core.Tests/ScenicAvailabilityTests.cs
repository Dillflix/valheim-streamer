using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicAvailabilityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Live scenic composition recovery cannot authorize region travel", LiveRecovery);
        yield return ("Scenic recovery never earns useful region dwell", NoPhantomDwell);
        yield return ("Actual scene loss still authorizes bounded scenic recovery", SceneLoss);
        yield return ("Recovered scenic availability clears the unavailable deadline", RecoveredDeadline);
        yield return ("Useful live footage overrides stale unavailable evidence", UsefulIsAvailable);
        yield return ("Scenic travel distinguishes missing picture from missing world and ordinary rotation", TravelCause);
        yield return ("Exhausted local coverage can seek fresh content with a live picture", FreshContent);
        yield return ("Editorial exhaustion preserves hold and ready-player priority", ContentControls);
        yield return ("Editorial exhaustion cannot invent a picture or bypass destination constraints", ContentLimits);
    }
    private static void FreshContent()
    {
        var director = New(); director.RegionSeconds = 120;
        director.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0), null, true,
            idleSceneAvailable: true, scenicContentExhausted: true);
        Check(director.PendingTravel?.TargetId == "next", "Finite settled coverage never allows a fresh destination");
        Check(director.PendingTravel?.Reason == "Local scenic coverage exhausted; seeking fresh subject",
            "A readable scene was mislabeled as picture/loading failure");
    }
    private static void ContentControls()
    {
        var held = New(); held.RegionSeconds = 120; held.SetHolding(true);
        held.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0), null, true,
            scenicContentExhausted: true);
        Check(held.PendingTravel == null, "Content novelty overrides an operator hold");
        var player = New();
        var players = new[] { new AutonomyPlayer("player", "Player", new AutonomyPosition(0, 0, 0), 100, true) };
        player.Tick(0, 0, players, Regions, new AutonomyPosition(0, 0, 0), "player", true,
            scenicContentExhausted: true);
        Check(player.State == AutonomyState.Following && player.PendingTravel == null,
            "Scenic novelty preempted a ready player");
    }
    private static void ContentLimits()
    {
        var unavailable = New(); unavailable.RegionSeconds = 120;
        unavailable.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0), null, false,
            idleSceneAvailable: true, scenicContentExhausted: true);
        Check(unavailable.PendingTravel == null, "Content flag bypassed actual missing-picture recovery");
        var noDestinations = New(); noDestinations.RegionSeconds = 120;
        noDestinations.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Array.Empty<ScenicRegion>(),
            new AutonomyPosition(0, 0, 0), null, true, scenicContentExhausted: true);
        Check(noDestinations.PendingTravel == null && noDestinations.State == AutonomyState.Scenic &&
            noDestinations.Reason.Contains("Retaining the readable scenic view"), "No destination discarded the live picture");
        var constrained = New(); constrained.RegionSeconds = 120; constrained.MinimumScenicTravelDistance = 600;
        constrained.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0), null, true,
            scenicContentExhausted: true);
        Check(constrained.PendingTravel == null, "Content novelty bypassed minimum distance");
    }
    private static void TravelCause()
    {
        var picture = New();
        picture.Tick(0, 1, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0), null, false,
            idleSceneAvailable: true, idleSearchExpired: true);
        Check(picture.PendingTravel?.Reason == "Scenic picture-recovery budget exhausted", "Loaded scene was mislabeled as missing geometry");
        var world = New(); Tick(world, 0, false, false); Tick(world, 11, false, false);
        Check(world.PendingTravel?.Reason == "Scenic scene-readiness deadline exhausted", "Missing scene was mislabeled as a composition failure");
        var ordinary = New(); Tick(ordinary, 0, true, true); Tick(ordinary, 1, true, true);
        Check(ordinary.PendingTravel?.Reason == "Scenic footage dwell completed", "Normal scenic rotation was mislabeled as recovery");
    }
    private static readonly ScenicRegion[] Regions = {
        new ScenicRegion("origin", "Origin", new AutonomyPosition(0, 0, 0)),
        new ScenicRegion("next", "Next", new AutonomyPosition(500, 0, 0)) };
    private static AutonomousDirector New()
    {
        var director = new AutonomousDirector { RegionSeconds = 2, UnreadyScenicSeconds = 10 };
        director.SetEnabled(true); return director;
    }
    private static void Tick(AutonomousDirector director, double time, bool useful, bool available, double dt = 1) =>
        director.Tick(time, dt, Array.Empty<AutonomyPlayer>(), Regions, new AutonomyPosition(0, 0, 0),
            null, useful, idleSceneAvailable: available);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void LiveRecovery()
    {
        var director = New();
        for (int i = 0; i <= 60; i++) Tick(director, i, false, true);
        Check(director.PendingTravel == null, "Editorial recovery was treated as an unavailable region");
        Check(director.State == AutonomyState.Scenic, "A live scenic region became a loading state");
    }
    private static void NoPhantomDwell()
    {
        var director = New();
        for (int i = 0; i <= 30; i++) Tick(director, i, false, true);
        Tick(director, 31, true, true);
        Check(director.PendingTravel == null, "Recovering frames counted towards useful dwell");
        Tick(director, 32, true, true);
        Check(director.PendingTravel?.TargetId == "next", "Useful footage no longer completes ordinary scenic dwell");
    }
    private static void SceneLoss()
    {
        var director = New(); Tick(director, 0, false, false, 0);
        Tick(director, 9, false, false); Check(director.PendingTravel == null, "Recovery travelled too early");
        Tick(director, 10, false, false);
        Check(director.PendingTravel?.TargetId == "next", "True scene exhaustion never recovered");
    }
    private static void RecoveredDeadline()
    {
        var director = New(); Tick(director, 0, false, false, 0);
        Tick(director, 9, false, true); Tick(director, 10, false, false);
        Tick(director, 19, false, false);
        Check(director.PendingTravel == null, "Old missing-scene deadline survived recovery");
        Tick(director, 20, false, false);
        Check(director.PendingTravel != null, "New sustained scene loss did not recover");
    }
    private static void UsefulIsAvailable()
    {
        var director = New(); director.RegionSeconds = 1000;
        for (int i = 0; i <= 20; i++) Tick(director, i, true, false);
        Check(director.PendingTravel == null, "Stale unavailable flag overrode proof of useful footage");
    }
}
