using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class OperatorCoverageTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic override excludes ready players and creatures without falsifying connection presence", ScenicOverride);
        yield return ("Scenic override preserves autonomous travel while a ready or distant player remains connected", ScenicTravel);
        yield return ("Mode changes preserve native player travel and apply Scenic intent on arrival", TravelHandoff);
        yield return ("Auto restores player acquisition after intentional Scenic coverage", RestoreAuto);
        yield return ("Player override suppresses wildlife cutaways and retains safe scenic fallback", PlayerPreference);
        yield return ("Creature override selects eligible local subjects and falls back safely on loss", CreaturePreference);
        yield return ("Creature override respects failed-view cooldowns and does not claim unavailable footage", CreatureFailure);
        yield return ("Scene resets preserve operator preference and session cleanup can clear it", Lifetime);
        yield return ("Automatic travel pause retains forced Scenic coverage around ready players", PausedTravel);
        yield return ("Operator mode parsing accepts only named modes", Parse);
    }

    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static readonly string[] Wildlife = { "deer", "boar" };
    private static AutonomyPlayer Player(bool ready = true, float distance = 0) =>
        new AutonomyPlayer("player", "Player", new AutonomyPosition(distance, 0, 0), 1000, ready);
    private static AutonomousDirector Director()
    {
        var result = new AutonomousDirector { RegionSeconds = 2, MinimumTravelSeconds = 1, MinimumScenicTravelDistance = 100 };
        result.SetEnabled(true); return result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Step(AutonomousDirector director, double now, CoveragePreference preference,
        IReadOnlyList<AutonomyPlayer> players, IReadOnlyList<ScenicRegion>? regions = null, bool busy = false,
        AutonomyPosition? position = null)
        => director.Tick(now, 1, players, regions ?? Array.Empty<ScenicRegion>(), position ?? Origin,
            null, true, travelBusy: busy, preference: preference);

    private static void ScenicOverride()
    {
        var coverage = new CoverageDirector { DedicatedScenicShots = true };
        coverage.Tick(0, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Player, "Initial player coverage missing");
        coverage.Preference = CoveragePreference.Scenic;
        for (int i = 0; i < 90; i++) coverage.Tick(1, "player", 1, true, Wildlife, allowWildlifeCutaway: true, retainedPlayerId: "player");
        Check(coverage.Mode == CoverageMode.Scenic && !coverage.IsWildlifeCutaway && !coverage.AwaitingPlayers,
            "Connected players or wildlife took over forced Scenic coverage");
        coverage.Tick(1, "player", 1, false, Wildlife);
        Check(coverage.Mode == CoverageMode.Recovery, "Mode override bypassed actual scene readiness");
    }

    private static void ScenicTravel()
    {
        foreach (var player in new[] { Player(), Player(false, 500) })
        {
            var director = Director(); var roster = new[] { player };
            var regions = new[] { new ScenicRegion("coast", "Coast", new AutonomyPosition(1000, 0, 0)) };
            for (int i = 0; i < 8 && director.PendingTravel == null; i++)
                Step(director, i, CoveragePreference.Scenic, roster, regions);
            Check(director.TargetId == null && director.PendingTravel?.Kind == AutonomyTravelKind.Scenic,
                "Forced scenery either reacquired a player or disabled destination travel");
            Check(roster.Length == 1 && ReferenceEquals(roster[0], player), "Operator intent altered actual discovery presence");
        }
    }

    private static void TravelHandoff()
    {
        var director = Director(); var roster = new[] { Player(false, 500) };
        Step(director, 0, CoveragePreference.Auto, roster);
        var request = director.PendingTravel ?? throw new Exception("No initial distant player acquisition");
        Check(request.Kind == AutonomyTravelKind.Player, "Wrong initial travel kind");
        director.TravelStarted(request.Token, 0);
        Step(director, 1, CoveragePreference.Scenic, roster, busy: true);
        Check(director.ActiveTravel?.Token == request.Token && director.PendingTravel == null,
            "Mode selection replaced or cancelled an active native teleport");
        director.TravelCompleted(request.Token, true, 2);
        Step(director, 3, CoveragePreference.Scenic, new[] { Player() }, position: request.Position);
        Check(director.TargetId == null && director.ActiveTravel == null && director.PendingTravel == null,
            "Completed player travel overrode requested Scenic mode");
    }

    private static void RestoreAuto()
    {
        var director = Director(); var roster = new[] { Player() };
        var coverage = new CoverageDirector { Preference = CoveragePreference.Scenic };
        Step(director, 0, coverage.Preference, roster);
        coverage.Tick(0, "player", 1, true, Wildlife);
        coverage.Preference = CoveragePreference.Auto;
        Step(director, 1, coverage.Preference, roster);
        coverage.Tick(1, director.TargetId, 1, true, Wildlife);
        Check(director.TargetId == "player" && coverage.Mode == CoverageMode.Player,
            "Returning to Auto failed to restore player coverage");
    }

    private static void PlayerPreference()
    {
        var coverage = new CoverageDirector { Preference = CoveragePreference.Player };
        coverage.Tick(1, "player", 1, true, Wildlife, allowWildlifeCutaway: true);
        Check(coverage.Mode == CoverageMode.Player && !coverage.IsWildlifeCutaway, "Wildlife interrupted Player override");
        for (int i = 0; i < 90; i++) coverage.Tick(1, null, 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Scenic && coverage.AwaitingPlayers, "Missing player did not get a safe scenic fallback");
        coverage.Tick(1, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Player, "Player return did not preempt its fallback");
    }

    private static void CreaturePreference()
    {
        var coverage = new CoverageDirector { Preference = CoveragePreference.Creature };
        coverage.Tick(0, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Creature && !coverage.IsWildlifeCutaway && !coverage.AwaitingPlayers,
            "Creature override remained gated behind a ready player or scenic dwell");
        coverage.Tick(1, "player", 1, true, Array.Empty<string>());
        Check(coverage.Mode == CoverageMode.Scenic, "Lost creature did not yield to a safe local picture");
        coverage.Tick(1, "player", 1, false, Wildlife);
        Check(coverage.Mode == CoverageMode.Recovery, "Creature override bypassed scene availability");
        coverage.Preference = CoveragePreference.Scenic;
        coverage.Tick(1, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Scenic, "Explicit Scenic mode retained a creature");
    }

    private static void CreatureFailure()
    {
        var coverage = new CoverageDirector { Preference = CoveragePreference.Creature };
        var one = new[] { "deer" };
        coverage.Tick(0, "player", 1, true, one);
        for (int i = 0; i < 4; i++) coverage.Tick(1, "player", 1, true, one, usableFootage: false);
        Check(coverage.Mode == CoverageMode.Scenic, "Unshowable creature remained selected forever");
        for (int i = 0; i < 5; i++) coverage.Tick(1, "player", 1, true, one);
        Check(coverage.Mode == CoverageMode.Scenic, "Override bypassed failed-view cooldown");
    }

    private static void Lifetime()
    {
        var coverage = new CoverageDirector { Preference = CoveragePreference.Scenic };
        coverage.Reset(); coverage.Tick(0, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Scenic, "A region reset erased the operator's override");
        coverage.Reset(); coverage.Preference = CoveragePreference.Auto;
        coverage.Tick(0, "player", 1, true, Wildlife);
        Check(coverage.Mode == CoverageMode.Player, "Session cleanup could not restore the default policy");
    }

    private static void PausedTravel()
    {
        var director = Director(); director.SetEnabled(false);
        var coverage = new CoverageDirector { Preference = CoveragePreference.Scenic };
        Step(director, 0, coverage.Preference, new[] { Player() },
            new[] { new ScenicRegion("coast", "Coast", new AutonomyPosition(1000, 0, 0)) });
        coverage.Tick(1, "player", 1, true, Wildlife);
        Check(director.PendingTravel == null && coverage.Mode == CoverageMode.Scenic,
            "Pausing autonomous travel undid the explicit coverage preference");
    }

    private static void Parse()
    {
        foreach (CoveragePreference mode in Enum.GetValues(typeof(CoveragePreference)))
            Check(CoveragePreferences.TryParse(mode.ToString(), out var parsed) && parsed == mode, "Named mode not accepted");
        foreach (string? invalid in new[] { "1", "-1", "scenery", "Auto,Scenic", "", null })
            Check(!CoveragePreferences.TryParse(invalid, out _), "Unrecognized/numeric mode silently accepted");
    }
}
