using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicRecoveryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic blackout has a wall-clock budget across unsuccessful routes", BoundedSearch);
        yield return ("Scenic retries and accepted but unpresented plans cannot renew recovery", UnpresentedPlans);
        yield return ("Readable preceding scenic footage prevents blackout escalation", PrecedingLivePicture);
        yield return ("Background scenic preparation spends no on-air recovery budget", BackgroundPreparation);
        yield return ("Held textures are not live scenic recovery evidence", HeldImage);
        yield return ("Operator hold pauses rather than restarts scenic recovery", OperatorHold);
        yield return ("Slow scenic evaluation cannot suspend the blackout deadline", SlowFrames);
        yield return ("Fresh actual scenic output clears the recovery budget", LiveRecovery);
        yield return ("Scenic region reset clears the old blackout lifetime", RegionReset);
        yield return ("Invalid scenic recovery clocks preserve the last valid state", InvalidClock);
        yield return ("Expired scenic search escalates without another scene-loss timeout", NoDoubleTimeout);
        yield return ("Usable live scenery takes precedence over a stale search-expired flag", FootagePrecedence);
        yield return ("Expired scenic search still respects operator hold", HeldTravel);
        yield return ("Expired scenic search cannot invent an unprovided destination", MissingDestination);
        yield return ("Expired scenic search still respects travel cooldown and revisit history", TravelSafeguards);
        yield return ("Scenic loading-state churn cannot reset the on-air blackout deadline", ReadinessChurn);
        yield return ("Unpresented wildlife fallback cannot renew a scenic blackout", PendingCreature);
        yield return ("Actual readable creature footage releases the scenic blackout lifetime", LiveCreature);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }

    private static void BoundedSearch()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(100, true, false);
        recovery.Observe(109.9, true, false);
        Check(recovery.Active && !recovery.Expired && recovery.RemainingSeconds > 0,
            "Search expired before its complete allowance.");
        recovery.Observe(110, true, false);
        Check(recovery.Expired && recovery.RemainingSeconds == 0, "A loaded but empty search can remain on-air indefinitely.");
    }

    private static void UnpresentedPlans()
    {
        var recovery = new ScenicRecoveryPolicy();
        // The renderer supplies no live proof while routes are accepted/retried.
        // Route identity deliberately is not an input to this coverage lifetime.
        for (int sweep = 0; sweep <= 12; sweep++) recovery.Observe(sweep, true, false);
        Check(recovery.Expired && recovery.ElapsedSeconds == 12, "Search/plan churn reset the missing-picture deadline.");
    }

    private static void PrecedingLivePicture()
    {
        var recovery = new ScenicRecoveryPolicy();
        // A pending new plan need not be on-air yet: its validated predecessor
        // is still a real live composition, regardless of requested shot ID.
        for (int second = 0; second <= 60; second++) recovery.Observe(second, true, true);
        Check(!recovery.Active && !recovery.Expired && recovery.ElapsedSeconds == 0,
            "Useful live predecessor footage was mistaken for a blackout.");
    }

    private static void BackgroundPreparation()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(0, false, false); recovery.Observe(500, false, false);
        Check(!recovery.Active, "Off-air scenic planning was treated as failed broadcast output.");
        recovery.Observe(501, true, false);
        Check(recovery.ElapsedSeconds == 0, "Creature/background time consumed the next scenic allowance.");
        recovery.Observe(506, true, false); recovery.Observe(507, false, false);
        Check(!recovery.Active && recovery.ElapsedSeconds == 0, "Leaving scenic coverage did not release its recovery timer.");
    }

    private static void HeldImage()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(0, true, false); recovery.Observe(10, true, false);
        Check(recovery.Expired, "A cached compositor texture was able to impersonate live scenic output.");
    }

    private static void OperatorHold()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(0, true, false); recovery.Observe(4, true, false);
        recovery.Observe(4, true, false, true); recovery.Observe(1000, true, false, true);
        recovery.Observe(1001, true, false);
        Check(recovery.ElapsedSeconds == 4 && !recovery.Expired, "Operator hold was counted or erased prior recovery effort.");
        recovery.Observe(1007, true, false);
        Check(recovery.Expired, "Resuming the same failed coverage received a fresh unlimited allowance.");
    }

    private static void SlowFrames()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(0, true, false); recovery.Observe(25, true, false);
        Check(recovery.Expired && recovery.ElapsedSeconds == 25, "Long frame intervals suspended the wall-clock limit.");
    }

    private static void LiveRecovery()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(0, true, false); recovery.Observe(11, true, false);
        recovery.Observe(12, true, true);
        Check(!recovery.Active && !recovery.Expired && recovery.ElapsedSeconds == 0, "Real output did not restore scenic coverage.");
        recovery.Observe(30, true, false);
        Check(recovery.ElapsedSeconds == 0, "Healthy preceding coverage was charged to a new blackout.");
    }

    private static void RegionReset()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(100, true, false); recovery.Observe(112, true, false); recovery.Reset();
        recovery.Observe(0, true, false);
        Check(recovery.Active && recovery.ElapsedSeconds == 0 && !recovery.Expired, "Old region recovery leaked into a new session.");
    }

    private static void InvalidClock()
    {
        var recovery = new ScenicRecoveryPolicy();
        recovery.Observe(10, true, false); recovery.Observe(14, true, false);
        foreach (double time in new[] { double.NaN, double.PositiveInfinity, -1d, 13d })
        {
            bool rejected = false;
            try { recovery.Observe(time, true, true); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && recovery.Active && recovery.ElapsedSeconds == 4, "Invalid clock changed valid recovery evidence.");
        }
        recovery.Observe(20, true, false);
        Check(recovery.Expired, "Invalid input erased the original clock baseline.");
    }

    private static readonly ScenicRegion[] Regions = {
        new ScenicRegion("origin", "Origin", new AutonomyPosition(0, 0, 0)),
        new ScenicRegion("next", "Next", new AutonomyPosition(500, 0, 0)),
        new ScenicRegion("later", "Later", new AutonomyPosition(1000, 0, 0)) };
    private static AutonomousDirector Automatic()
    {
        var director = new AutonomousDirector { RegionSeconds = 120, UnreadyScenicSeconds = 10 };
        director.SetEnabled(true); return director;
    }
    private static void Tick(AutonomousDirector director, double now, bool ready, bool expired,
        IReadOnlyList<ScenicRegion>? regions = null, float position = 0) =>
        director.Tick(now, 0, Array.Empty<AutonomyPlayer>(), regions ?? Regions,
            new AutonomyPosition(position, 0, 0), null, ready,
            idleSceneAvailable: true, idleSearchExpired: expired);

    private static void NoDoubleTimeout()
    {
        var recovery = new ScenicRecoveryPolicy(); var director = Automatic();
        recovery.Observe(0, true, false); Tick(director, 0, false, recovery.Expired);
        recovery.Observe(9, true, false); Tick(director, 9, false, recovery.Expired);
        Check(director.PendingTravel == null, "Loaded-region composition search escalated before its deadline.");
        recovery.Observe(10, true, false); Tick(director, 10, false, recovery.Expired);
        Check(director.PendingTravel?.TargetId == "next", "Expired on-air recovery waited for a second ten-second loading timeout.");
    }

    private static void FootagePrecedence()
    {
        var director = Automatic();
        Tick(director, 0, true, true); Tick(director, 20, true, true);
        Check(director.PendingTravel == null && director.State == AutonomyState.Scenic,
            "A stale deadline discarded actual readable live footage.");
    }

    private static void HeldTravel()
    {
        var director = Automatic(); director.SetHolding(true);
        Tick(director, 0, false, true); Tick(director, 50, false, true);
        Check(director.PendingTravel == null, "A black-picture timeout bypassed operator hold.");
        director.SetHolding(false); Tick(director, 51, false, true);
        Check(director.PendingTravel?.TargetId == "next", "Unholding expired recovery did not permit normal region selection.");
    }

    private static void MissingDestination()
    {
        var director = Automatic();
        Tick(director, 0, false, true, Array.Empty<ScenicRegion>());
        Tick(director, 100, false, true, Array.Empty<ScenicRegion>());
        Check(director.PendingTravel == null && director.ActiveTravel == null,
            "Recovery invented a destination absent from authorized discovery.");
    }

    private static void TravelSafeguards()
    {
        var director = Automatic(); Tick(director, 10, false, true);
        long token = director.PendingTravel?.Token ?? throw new Exception("Initial recovery did not request travel.");
        director.TravelStarted(token, 10); director.TravelCompleted(token, true, 11);
        Tick(director, 12, false, true, position: 500);
        Check(director.PendingTravel == null, "The same recovery flag bypassed minimum travel spacing.");
        Tick(director, 19, false, true, position: 500);
        Check(director.PendingTravel?.TargetId == "later", "Recovery ignored fresh-region selection or revisit history.");
    }

    private static void ReadinessChurn()
    {
        var recovery = new ScenicRecoveryPolicy(); var director = Automatic();
        var coverage = new CoverageDirector { DedicatedScenicShots = true };
        // Area objects fluctuate between acquired and pending, but no frame is
        // ever shown. Neither switching to Recovery nor restarting local scenic
        // planning may manufacture a new allowance for the same black output.
        for (int second = 0; second <= 10; second++)
        {
            bool sceneReady = second % 2 == 0;
            coverage.Tick(1, null, 0, sceneReady, Array.Empty<string>(),
                usableFootage: false, scenicSearchExhausted: false);
            recovery.Observe(second,
                coverage.Mode == CoverageMode.Scenic || coverage.Mode == CoverageMode.Recovery, false);
            director.Tick(second, 1, Array.Empty<AutonomyPlayer>(), Regions,
                new AutonomyPosition(0, 0, 0), null, false,
                idleSceneAvailable: sceneReady, idleSearchExpired: recovery.Expired);
            if (second < 10) Check(director.PendingTravel == null, "Churning readiness travelled before the output budget elapsed.");
        }
        Check(recovery.Expired && director.PendingTravel?.TargetId == "next",
            "Loading/composition-state churn renewed a blackout with no actual picture.");
    }

    private static readonly string[] Deer = { "deer" };
    private static void ObserveCoverage(ScenicRecoveryPolicy recovery, CoverageDirector coverage, double time, bool usable)
    {
        bool pendingCreature = coverage.Mode == CoverageMode.Creature && !coverage.IsWildlifeCutaway && !usable;
        recovery.Observe(time, coverage.Mode == CoverageMode.Scenic || coverage.Mode == CoverageMode.Recovery || pendingCreature,
            coverage.Mode == CoverageMode.Scenic && usable);
    }

    private static void PendingCreature()
    {
        var recovery = new ScenicRecoveryPolicy(); var director = Automatic();
        var coverage = new CoverageDirector { DedicatedScenicShots = true };
        bool attemptedCreature = false;
        for (int second = 0; second <= 10; second++)
        {
            coverage.Tick(second == 0 ? 0 : 1, null, 0, true, Deer,
                usableFootage: false, scenicSearchExhausted: true);
            attemptedCreature |= coverage.Mode == CoverageMode.Creature;
            ObserveCoverage(recovery, coverage, second, false);
            Tick(director, second, false, recovery.Expired);
        }
        Check(attemptedCreature && recovery.Expired && director.PendingTravel?.TargetId == "next",
            "A selected but unrendered wildlife camera renewed the same black broadcast.");
    }

    private static void LiveCreature()
    {
        var recovery = new ScenicRecoveryPolicy();
        var coverage = new CoverageDirector { DedicatedScenicShots = true };
        for (int second = 0; second <= 3; second++)
        {
            coverage.Tick(second == 0 ? 0 : 1, null, 0, true, Deer,
                usableFootage: false, scenicSearchExhausted: true);
            ObserveCoverage(recovery, coverage, second, false);
        }
        Check(coverage.Mode == CoverageMode.Creature && recovery.ElapsedSeconds == 3,
            "Test did not retain recovery through creature admission.");
        coverage.Tick(1, null, 0, true, Deer, usableFootage: true);
        ObserveCoverage(recovery, coverage, 4, true);
        Check(!recovery.Active && recovery.ElapsedSeconds == 0, "Real readable wildlife footage failed to end the blackout.");
        recovery.Observe(30, true, false);
        Check(recovery.Active && recovery.ElapsedSeconds == 0, "Background scenic preparation consumed a later on-air allowance.");
    }
}
