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
