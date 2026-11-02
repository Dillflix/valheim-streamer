using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class IdlePresentationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic duration advances only while actual footage is usable", ScenicFootage);
        yield return ("Unshowable scenery can try a fresh creature without fabricated dwell", ScenicFailureEscape);
        yield return ("Unshown creature coverage falls back without claiming the long shown cooldown", FailedCreature);
        yield return ("Brief creature picture gaps pause progress and recover without forced cuts", BriefCreatureGap);
        yield return ("Operator-held live creature coverage records presentation without consuming dwell", HeldLiveCreature);
        yield return ("Unseen creature selection preempted by a player does not count as shown", PreemptedSelection);
        yield return ("Player priority and lost-scene recovery override idle pacing and holds", PriorityAndReset);
    }

    private static readonly string[] Creature = { "deer" };
    private static readonly string[] None = Array.Empty<string>();
    private static CoverageDirector Director() => new CoverageDirector { ScenicSeconds = 5, CreatureSeconds = 10 };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void BeginCreature(CoverageDirector director)
    {
        director.Tick(0, null, 0, true, Creature, usableFootage: false);
        director.Tick(5, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Creature, "Test failed to establish creature selection.");
    }

    private static void ScenicFootage()
    {
        var director = Director();
        director.Tick(0, null, 0, true, None, usableFootage: false);
        long revision = director.Revision;
        director.Tick(60, null, 0, true, None, usableFootage: false);
        Check(director.ShotElapsed == 0 && director.Revision == revision,
            "Searching/held imagery consumed the unseen scenic path.");
        director.Tick(3, null, 0, true, Creature);
        director.Tick(2, null, 0, true, Creature, usableFootage: false);
        Check(director.ShotElapsed == 3 && director.Revision == revision,
            "A presentation gap advanced or replaced the scenic shot.");
        director.Tick(2, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Creature, "Five usable seconds did not complete the scenic dwell.");
    }

    private static void ScenicFailureEscape()
    {
        var director = Director();
        director.Tick(0, null, 0, true, Creature, usableFootage: false);
        director.Tick(2.9, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Scenic && director.ShotElapsed == 0,
            "A short scenic search was abandoned or counted as footage.");
        director.ScenicPresentationStarted(); // A provisional replacement is still not footage.
        director.Tick(0.2, null, 0, true, Creature, true, false);
        Check(director.Mode == CoverageMode.Creature && director.ShotElapsed == 0,
            "Failed scenery starved an available creature or fabricated presentation time.");
        director.Tick(3, null, 0, true, Creature, usableFootage: false);
        long revision = director.Revision;
        director.Tick(10, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Scenic && director.Revision == revision && director.ShotElapsed == 0,
            "Unshowable scenery ignored the creature failure cooldown or spun shot revisions.");
        director.Tick(21, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Creature,
            "A recovered creature remained starved after its failed-view backoff expired.");
        director.Tick(10, null, 0, true, Creature); // Actually shown: normal long cooldown applies.
        revision = director.Revision;
        director.Tick(31, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Scenic && director.Revision == revision,
            "Scenic fallback repeated a recently shown creature before its cooldown.");
    }

    private static void FailedCreature()
    {
        var director = Director(); BeginCreature(director);
        director.Tick(2.9, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Creature && director.ShotElapsed == 0,
            "Unavailable creature footage was counted or abandoned too early.");
        director.Tick(0.2, null, 0, true, Creature, true, false);
        Check(director.Mode == CoverageMode.Scenic,
            "An operator hold trapped coverage on an unshowable creature.");
        director.Tick(5, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Scenic, "The failed view was retried immediately.");
        director.Tick(26, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Creature,
            "A creature never shown inherited the long presentation cooldown.");
        director.Tick(10, null, 0, true, Creature);
        director.Tick(31, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Scenic,
            "Successfully presented creatures ignored the normal repeat cooldown.");
    }

    private static void BriefCreatureGap()
    {
        var director = Director(); BeginCreature(director);
        director.Tick(4, null, 0, true, Creature);
        director.Tick(2, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Creature && director.ShotElapsed == 4,
            "A short picture gap consumed creature dwell.");
        director.Tick(1, null, 0, true, Creature);
        director.Tick(2, null, 0, true, Creature, usableFootage: false);
        Check(director.Mode == CoverageMode.Creature && director.ShotElapsed == 5,
            "Recovered footage failed to clear consecutive failure timing.");
        director.Tick(5, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Scenic, "Creature did not complete ten seconds of usable footage.");
    }

    private static void HeldLiveCreature()
    {
        var director = Director(); BeginCreature(director);
        director.Tick(1, null, 0, true, Creature, true, true);
        Check(director.ShotElapsed == 0 && director.Mode == CoverageMode.Creature,
            "Holding a live creature shot consumed dwell.");
        director.Tick(0, null, 0, true, None); // Actor departs while held.
        director.Tick(31, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Scenic,
            "An actually shown held creature was immediately eligible again.");
    }

    private static void PreemptedSelection()
    {
        var director = Director(); BeginCreature(director);
        director.Tick(0, "player", 1, true, Creature, usableFootage: false);
        director.Tick(0, null, 0, true, Creature, usableFootage: false);
        director.Tick(5, null, 0, true, Creature);
        Check(director.Mode == CoverageMode.Creature,
            "A selected but never presented creature was recorded as seen.");
    }

    private static void PriorityAndReset()
    {
        var director = Director(); BeginCreature(director);
        director.Tick(1, "player", 1, false, Creature, true, false);
        Check(director.Mode == CoverageMode.Player, "A ready player failed to preempt a bad idle shot.");
        director.Tick(1, null, 1, false, Creature, true, false);
        Check(director.Mode == CoverageMode.Recovery && director.AwaitingPlayers,
            "A lost scene retained an invalid idle picture.");
        director.Reset(); BeginCreature(director);
        director.Tick(3, null, 0, true, Creature, usableFootage: false);
        director.Reset(); BeginCreature(director);
        Check(director.ShotElapsed == 0, "A new world inherited previous view failures or shot progress.");
    }
}
