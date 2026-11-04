using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class MissingPlayerPictureTests
{
    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static readonly ScenicRegion[] NoRegions = Array.Empty<ScenicRegion>();
    private static readonly AutonomyPlayer[] Local = {
        new AutonomyPlayer("a", "A", Origin, 10000, true), new AutonomyPlayer("b", "B", Origin, 10000, true)
    };
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Complete picture loss and unreadable footage use the same local recovery deadline", LocalFallback);
        yield return ("A never-presented or unloaded target keeps its acquisition contract", Acquisition);
        yield return ("Readable recovery resets the complete-picture loss search", ReadableReset);
        yield return ("Hold travel and intentional wildlife do not accumulate missing-picture failure time", IntentionalInterruptions);
        yield return ("Complete picture loss without a local alternative retains bounded target retry", BoundedOutage);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static AutonomousDirector New()
    {
        var d = new AutonomousDirector { DwellSeconds = 120 }; d.SetEnabled(true);
        d.Tick(0, 1, Local, NoRegions, Origin, "a", false);
        return d;
    }
    private static void Missing(AutonomousDirector d, int now, bool travel = false, bool wildlife = false) =>
        d.Tick(now, 1, Local, NoRegions, Origin, null, false, travelBusy: travel, wildlifeCutaway: wildlife, usablePlayerFootage: false);
    private static void LocalFallback()
    {
        foreach (bool missing in new[] { true, false })
        {
            var d = New();
            for (int t = 1; t <= 12; t++)
            {
                d.Tick(t, 1, Local, NoRegions, Origin, missing ? null : "a", false, usablePlayerFootage: false);
                Check(d.TargetId == "a", "Picture repair deadline was cut short");
            }
            d.Tick(13, 1, Local, NoRegions, Origin, missing ? null : "a", false, usablePlayerFootage: false);
            Check(d.TargetId == "b" && d.PendingTravel == null && d.FailedDestinationCount == 0,
                "Missing/unreadable local footage did not choose ready B without world-acquisition failure or travel");
        }
    }
    private static void Acquisition()
    {
        var d = new AutonomousDirector(); d.SetEnabled(true);
        for (int t = 0; t <= 20; t++) Missing(d, t);
        Check(d.TargetId == "a" && d.State == AutonomyState.Acquiring, "A first picture was treated as an established lost one");
        d = New();
        var unloaded = new[] { new AutonomyPlayer("a", "A", Origin, 10000, false), Local[1] };
        for (int t = 1; t <= 20; t++) d.Tick(t, 1, unloaded, NoRegions, Origin, null, false, usablePlayerFootage: false);
        Check(d.TargetId == "a" && d.PendingTravel == null, "Physical readiness loss used the loaded-picture fallback deadline");
    }
    private static void ReadableReset()
    {
        var d = New(); for (int t = 1; t <= 11; t++) Missing(d, t);
        d.Tick(12, 1, Local, NoRegions, Origin, "a", false);
        for (int t = 13; t <= 24; t++) Missing(d, t);
        Check(d.TargetId == "a", "Recovered footage retained its prior failure time");
        Missing(d, 25); Check(d.TargetId == "b", "New uninterrupted outage did not reach its own deadline");
    }
    private static void IntentionalInterruptions()
    {
        var held = New(); for (int t = 1; t <= 10; t++) Missing(held, t);
        held.SetHolding(true); for (int t = 11; t <= 100; t++) Missing(held, t);
        Check(held.TargetId == "a" && held.FailedDestinationCount == 0, "Explicit hold expired the retained loaded target");
        held.SetHolding(false); for (int t = 101; t <= 112; t++) Missing(held, t);
        Check(held.TargetId == "a", "Held time leaked into the recovery deadline");
        Missing(held, 113); Check(held.TargetId == "b", "Unheld missing footage could not recover");

        var travelling = New(); for (int t = 1; t <= 10; t++) Missing(travelling, t);
        for (int t = 11; t <= 20; t++) Missing(travelling, t, travel: true);
        for (int t = 21; t <= 32; t++) Missing(travelling, t);
        Check(travelling.TargetId == "a", "Intentional travel accumulated camera-only failure time");
        Missing(travelling, 33); Check(travelling.TargetId == "b", "Post-travel picture outage could not recover");

        var wildlife = New();
        for (int t = 1; t <= 20; t++) Missing(wildlife, t, wildlife: true);
        Check(wildlife.WildlifeCutawayActive && wildlife.TargetId == "a", "Wildlife cutaway was mistaken for missing footage");
        for (int t = 21; t <= 32; t++) Missing(wildlife, t);
        Check(wildlife.TargetId == "a", "Wildlife cutaway leaked into player-recovery time");
        Missing(wildlife, 33); Check(wildlife.TargetId == "b", "Post-cutaway picture outage could not recover");
    }
    private static void BoundedOutage()
    {
        var d = New();
        var distant = new[] { Local[0], new AutonomyPlayer("b", "B", new AutonomyPosition(1000, 0, 0), 10000, false, 1000) };
        for (int t = 1; t < 65; t++)
        {
            d.Tick(t, 1, distant, NoRegions, Origin, null, false, usablePlayerFootage: false);
            Check(d.TargetId == "a" && d.PendingTravel == null, "Local picture search requested premature distant travel");
        }
        d.Tick(65, 1, distant, NoRegions, Origin, null, false, usablePlayerFootage: false);
        Check(d.TargetId == null && d.PendingTravel == null && d.LastFailure?.Contains("locally loaded") == true,
            "An established loaded target with no picture held the programme indefinitely");
        d.Tick(66, 1, distant, NoRegions, Origin, null, false, usablePlayerFootage: false);
        Check(d.TargetId == "b" && d.PendingTravel?.TargetId == "b", "Bounded retry did not resume normal alternate-target acquisition");

        d = New(); var solo = new[] { Local[0] };
        for (int t = 1; t <= 66; t++) d.Tick(t, 1, solo, NoRegions, Origin, null, true, usablePlayerFootage: false);
        Check(d.TargetId == null && d.State == AutonomyState.Scenic && d.PendingTravel == null,
            "With no alternative player, the failed camera target did not yield to safe idle coverage");
    }
}
