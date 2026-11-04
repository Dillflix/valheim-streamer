using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class WildlifeCutawayPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Wildlife cutaways require observed quiet time from every fresh player", AffirmativeEvidence);
        yield return ("Brief activity between snapshots immediately revokes wildlife permission", ActivityReset);
        yield return ("Unknown stale and incomplete activity cannot sustain a wildlife cutaway", UnknownEvidence);
        yield return ("Membership changes revoke cutaways while roster ordering does not", Membership);
        yield return ("Cutaway completion and operator suppression require fresh player coverage", RearmAndSuppression);
        yield return ("Wildlife coverage yields player priority only to eligible nearby creatures", CoveragePriority);
        yield return ("Wildlife cutaways remain finite through repeated short picture gaps", FiniteCutaway);
        yield return ("Wildlife view failure and permission loss return directly to ready players", CutawayRecovery);
        yield return ("Wildlife returns select the retained player without claiming scene readiness", RetainedPlayerReturn);
        yield return ("Repeated idle-player cutaways preserve player intervals and creature cooldowns", IntegratedSchedule);
        yield return ("Wildlife lifecycle reset removes previous roster and scheduling state", ResetAndValidation);
    }

    private static WildlifePlayerEvidence Idle(string id, double now, double duration = 60) =>
        new WildlifePlayerEvidence(id, WildlifePlayerActivity.Idle, duration, now + 2);
    private static readonly string[] Creatures = { "boar", "deer" };
    private static readonly string[] None = Array.Empty<string>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static void AffirmativeEvidence()
    {
        var policy = new WildlifeCutawayPolicy();
        var almost = new[] { Idle("a", 0, 120), Idle("b", 0, 59) };
        Check(!policy.Update(0, almost, true, false, false), "One long-idle player qualified the entire server.");
        Check(!policy.Update(1.5, almost, true, false, false), "Client time extrapolated an old 59-second observation to sixty.");
        Check(policy.Update(1.6, new[] { Idle("a", 1.6, 121), Idle("b", 1.6, 60) }, true, false, false),
            "Fresh source-confirmed idle time incurred an unnecessary additional startup minute.");
        Check(policy.IdleSeconds == 60, "The least-idle player was not the qualification limit.");
        Check(!new WildlifeCutawayPolicy().Update(0, Array.Empty<WildlifePlayerEvidence>(), true, false, false),
            "An empty roster qualified a connected-player cutaway.");
    }

    private static void ActivityReset()
    {
        var policy = new WildlifeCutawayPolicy();
        Check(policy.Update(0, new[] { Idle("a", 0, 90) }, true, false, false), "Initial quiet evidence did not qualify.");
        Check(!policy.Update(1, new[] { Idle("a", 1, 0.4) }, true, false, true),
            "A brief activity reset between reply snapshots was missed because the player was still again.");
        Check(!policy.Update(2, new[] { new WildlifePlayerEvidence("a", WildlifePlayerActivity.Active, 100, 4) }, true, false, true),
            "An active player with inconsistent duration retained wildlife permission.");
    }

    private static void UnknownEvidence()
    {
        foreach (string failure in new[] { "unknown", "stale", "incomplete" })
        {
            var policy = new WildlifeCutawayPolicy();
            policy.Update(0, new[] { Idle("a", 0, 90) }, true, false, false);
            WildlifePlayerEvidence evidence = failure == "unknown"
                ? new WildlifePlayerEvidence("a", WildlifePlayerActivity.Unknown, 90, 4)
                : failure == "stale" ? new WildlifePlayerEvidence("a", WildlifePlayerActivity.Idle, 90, 1)
                : Idle("a", 1, 90);
            Check(!policy.Update(1, new[] { evidence }, failure != "incomplete", false, true),
                failure + " evidence failed to revoke the active cutaway.");
            Check(policy.IdleSeconds == 0, "Unknown activity was presented as proven idle time.");
        }
    }

    private static void Membership()
    {
        var policy = new WildlifeCutawayPolicy();
        Check(policy.Update(0, new[] { Idle("a", 0), Idle("b", 0) }, true, false, false), "Initial roster failed.");
        Check(policy.Update(1, new[] { Idle("b", 1), Idle("a", 1) }, true, false, true),
            "Roster enumeration order interrupted a valid wildlife shot.");
        Check(!policy.Update(2, new[] { Idle("a", 2) }, true, false, true),
            "A disconnect did not immediately return attention to the players.");
        Check(!policy.Update(3, new[] { Idle("c", 3) }, true, false, false),
            "A same-count identity replacement inherited the previous player's idle interval.");
        Check(policy.RearmRemainingSeconds >= 59, "Membership change did not establish a conservative player interval.");
    }

    private static void RearmAndSuppression()
    {
        var policy = new WildlifeCutawayPolicy();
        policy.Update(0, new[] { Idle("a", 0, 100) }, true, false, false);
        policy.Update(1, new[] { Idle("a", 1, 101) }, true, false, true);
        Check(!policy.Update(25, new[] { Idle("a", 25, 125) }, true, false, false), "Completed cutaway immediately chained another.");
        Check(!policy.Update(84.9, new[] { Idle("a", 84.9, 185) }, true, false, false), "Player interval ended early.");
        Check(policy.Update(85, new[] { Idle("a", 85, 185) }, true, false, false), "Sixty seconds of player coverage failed to rearm.");
        Check(!policy.Update(86, new[] { Idle("a", 86, 186) }, true, true, true), "Operator/self/probe/route suppression was ignored.");
        Check(!policy.Update(87, new[] { Idle("a", 87, 187) }, true, false, false), "Releasing suppression immediately resumed a cutaway.");
    }

    private static void CoveragePriority()
    {
        var coverage = new CoverageDirector();
        coverage.Tick(0, "player", 1, true, Creatures);
        Check(coverage.Mode == CoverageMode.Player && !coverage.IsWildlifeCutaway,
            "Ordinary player priority changed without an explicit wildlife permission.");
        coverage.Tick(1, "player", 1, true, None, allowWildlifeCutaway: true);
        Check(coverage.Mode == CoverageMode.Player, "No creature availability caused scenic coverage with a ready player.");
        coverage.Tick(1, "player", 1, true, Creatures, true, true, true);
        Check(coverage.Mode == CoverageMode.Player, "An operator hold yielded player priority.");
        coverage.Tick(1, "self", 0, true, Creatures, allowWildlifeCutaway: true);
        Check(coverage.Mode == CoverageMode.Player && !coverage.IsWildlifeCutaway, "Self-only coverage entered a connected-player cutaway.");
        coverage.Tick(1, "player", 1, true, Creatures, allowWildlifeCutaway: true);
        Check(coverage.Mode == CoverageMode.Creature && coverage.IsWildlifeCutaway, "Eligible nearby wildlife did not receive its cutaway.");
    }

    private static void FiniteCutaway()
    {
        var coverage = new CoverageDirector();
        coverage.Tick(0, "player", 1, true, Creatures, allowWildlifeCutaway: true);
        for (int second = 1; second <= 25; second++)
        {
            coverage.Tick(1, "player", 1, true, Creatures, usableFootage: second % 3 == 0, allowWildlifeCutaway: true);
            if (second < 25) Check(coverage.IsWildlifeCutaway, "A recoverable short gap ended the cutaway prematurely.");
        }
        Check(coverage.Mode == CoverageMode.Player && !coverage.IsWildlifeCutaway,
            "Repeated short gaps extended the cutaway indefinitely beyond its configured duration.");
    }

    private static void CutawayRecovery()
    {
        foreach (string cause in new[] { "permission", "hold", "actor", "scene", "picture" })
        {
            var coverage = new CoverageDirector();
            coverage.Tick(0, "player", 1, true, Creatures, allowWildlifeCutaway: true);
            coverage.Tick(cause == "picture" ? 3 : 1, "player", 1, cause != "scene",
                cause == "actor" ? None : Creatures, cause == "hold", cause != "picture", cause != "permission");
            Check(coverage.Mode == CoverageMode.Player && !coverage.IsWildlifeCutaway,
                cause + " did not return immediately to the ready player.");
        }
        var failed = new CoverageDirector();
        failed.Tick(0, "player", 1, true, new[] { "boar" }, allowWildlifeCutaway: true);
        failed.Tick(3, "player", 1, true, new[] { "boar" }, usableFootage: false, allowWildlifeCutaway: true);
        failed.Tick(1, "player", 1, true, new[] { "boar" }, allowWildlifeCutaway: true);
        Check(failed.Mode == CoverageMode.Player, "Failed wildlife camera ignored the separate retry cooldown.");
        failed.Tick(30, "player", 1, true, new[] { "boar" }, allowWildlifeCutaway: true);
        Check(failed.IsWildlifeCutaway, "Never-shown wildlife inherited the long shown cooldown.");
        failed.Tick(0, null, 1, false, Creatures, allowWildlifeCutaway: false);
        Check(failed.Mode == CoverageMode.Recovery && !failed.IsWildlifeCutaway,
            "A lost player scene fabricated a ready player return.");
    }

    private static void IntegratedSchedule()
    {
        var policy = new WildlifeCutawayPolicy();
        var coverage = new CoverageDirector();
        double lastEnd = double.NegativeInfinity;
        var shownAt = new Dictionary<string, double>();
        int cutaways = 0;
        for (int second = 0; second <= 400; second++)
        {
            bool wasCutaway = coverage.IsWildlifeCutaway;
            bool allowed = policy.Update(second, new[] { Idle("a", second, second), Idle("b", second, second + 20) }, true, false, wasCutaway);
            coverage.Tick(second == 0 ? 0 : 1, "a", 2, true, Creatures, allowWildlifeCutaway: allowed);
            Check(coverage.Mode == CoverageMode.Player || coverage.IsWildlifeCutaway,
                "Connected-player wildlife scheduling entered scenic roaming.");
            if (!wasCutaway && coverage.IsWildlifeCutaway)
            {
                Check(second >= 60 && second - lastEnd >= 60, "A wildlife cutaway displaced the required player interval.");
                string subject = coverage.SubjectId!;
                if (shownAt.TryGetValue(subject, out double previous))
                    Check(second - previous >= 300, "Repeated wildlife ignored the five-minute shown cooldown.");
                shownAt[subject] = second; cutaways++;
            }
            if (wasCutaway && !coverage.IsWildlifeCutaway) lastEnd = second;
        }
        Check(cutaways >= 3 && cutaways <= 4, "Long-running quiet coverage starved or overused wildlife cutaways.");
    }

    private static void RetainedPlayerReturn()
    {
        var coverage = new CoverageDirector();
        coverage.Tick(0, "player", 1, true, Creatures, allowWildlifeCutaway: true);
        coverage.Tick(1, null, 1, false, Creatures, usableFootage: false, retainedPlayerId: "player");
        Check(coverage.Mode == CoverageMode.Player && coverage.SubjectId == "player" &&
            coverage.AwaitingPlayers && !coverage.IsWildlifeCutaway && coverage.ShotElapsed == 0,
            "Returning from wildlife entered scenery or fabricated ready player footage.");
        coverage.Tick(1, "player", 1, true, Creatures);
        Check(coverage.Mode == CoverageMode.Player && !coverage.AwaitingPlayers,
            "The retained player failed to resume when actual readiness returned.");
        var unavailable = new CoverageDirector();
        unavailable.Tick(0, null, 1, true, Creatures, allowWildlifeCutaway: true, retainedPlayerId: "player");
        Check(!unavailable.IsWildlifeCutaway, "A retained but unready target authorized starting a cutaway.");
    }

    private static void ResetAndValidation()
    {
        var policy = new WildlifeCutawayPolicy();
        policy.Update(100, new[] { Idle("old", 100) }, true, true, false);
        policy.Reset();
        Check(policy.Update(0, new[] { Idle("new", 0) }, true, false, false),
            "A new world inherited old membership, suppression or time state.");
        var coverage = new CoverageDirector();
        coverage.Tick(0, "player", 1, true, Creatures, allowWildlifeCutaway: true);
        coverage.Reset();
        Check(!coverage.IsWildlifeCutaway && coverage.Mode == CoverageMode.Disabled, "Coverage reset retained cutaway ownership.");
        Reject(() => policy.Update(1, new[] { Idle("a", 1), Idle("a", 1) }, true, false, false));
        Reject(() => new WildlifePlayerEvidence("a", WildlifePlayerActivity.Idle, double.NaN, 1));
        Reject(() => policy.Update(double.NaN, new[] { Idle("a", 1) }, true, false, false));
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid activity policy input was accepted.");
    }
}
