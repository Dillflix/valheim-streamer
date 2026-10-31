using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Empty server waits safely", EmptyRoster),
            ("Roster order and duplicate names cannot destabilize identity", StableRoster),
            ("Manual cycling wraps both directions", ManualCycling),
            ("Departed target advances to next surviving ID", DisconnectFallback),
            ("Readiness is required before dwelling", ReadinessAndDwell),
            ("Unavailable target times out without skipping multiple players", Timeout),
            ("Hold pauses automatic transitions while manual selection works", Hold),
            ("Loss of readiness resets the dwell period", LostReadiness),
            ("Disabled director preserves selection without advancing", Disabled),
            ("Roster refresh and rename preserve dwell progress", RefreshPreservesProgress),
            ("Single target stays selected through dwell and timeout", SingleTarget),
            ("Large frame advances once and discards excess time", LargeDelta),
            ("Invalid inputs fail explicitly without corrupting roster", InvalidInputs),
            ("Observer changes remove an accidentally selected observer", ObserverChange),
            ("Self test isolates the observer and normal mode excludes it again", SelfTestTransition),
            ("Solo target remains stable through cycling, readiness, and refresh", SelfTestLifetime),
            ("Zero players alternates scenery and creatures", IdleAlternation),
            ("No creatures maintains scenic coverage", ScenicFallback),
            ("Creature loss immediately returns to scenery", CreatureLoss),
            ("Ready player preempts idle and scene recovery", PlayerPriority),
            ("Connected but unloaded player keeps idle footage", PendingPlayer),
            ("Unavailable scene recovers when loaded", SceneRecovery),
            ("Creature rotation is stable across snapshot ordering", CreatureRotation),
            ("Hold pauses idle timing but does not block ready players", IdleHold),
            ("Coverage reset clears session state", CoverageReset),
            ("Coverage rejects invalid timing", CoverageValidation),
            ("Acquisition is bounded for one target with cooldown", BoundedAcquisition),
            ("Available player rotation integrates with idle fallback", CoverageIntegration),
            ("24-hour simulated idle schedule stays valid", IdleSoak),
            ("Solo probe acquires and preserves readiness timing", ProbeReady),
            ("Solo probe timeout and stop restore inactive policy", ProbeTimeout),
            ("Solo probe bounds total duration and readiness loss", ProbeLifetime),
            ("Recent creatures are excluded until cooldown", CreatureCooldown),
            ("Distant removal excludes observer and must abort", RetentionAbort),
            ("Normal removal and retained observer remain untouched", RetentionAllowed),
            ("Probe failure survives cleanup and resets on restart", ProbeFailure),
            ("Bodyless session handshake, two regions, recovery and fail-closed lifecycle", SpectatorTests.Run),
            ("Bodyless coordinates, clock and RPC boundary", SpectatorTests.Boundaries)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); System.Console.WriteLine("PASS " + test.Name); }
            catch (Exception error)
            {
                failed++;
                System.Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message);
            }
        }
        System.Console.WriteLine($"{cases.Length - failed}/{cases.Length} cases passed.");
        return failed == 0 ? 0 : 1;
    }

    private static BroadcastDirector Director(params string[] ids)
    {
        var director = new BroadcastDirector(10, 3);
        director.SetRoster(ids.Select(id => new PlayerTarget(id, "Player " + id)));
        director.SetEnabled(true);
        return director;
    }

    private static void EmptyRoster()
    {
        var d = Director();
        Equal(BroadcastState.WaitingForPlayers, d.State);
        False(d.Next()); False(d.Previous()); False(d.Select("missing"));
        d.Tick(100, true);
        Equal(null, d.CurrentTarget);
        d.SetRoster(new[] { new PlayerTarget("a", "Alice") });
        Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
        d.SetRoster(Array.Empty<PlayerTarget>());
        Equal(BroadcastState.WaitingForPlayers, d.State);
    }

    private static void StableRoster()
    {
        var entries = new[] {
            new PlayerTarget("b", "Same name"), new PlayerTarget("a", "Same name"),
            new PlayerTarget("self", "Observer"), new PlayerTarget("a", "Alternate name") };
        var first = new BroadcastDirector(); var second = new BroadcastDirector();
        first.SetRoster(entries, "self"); second.SetRoster(entries.Reverse(), "self");
        Equal("a,b", string.Join(",", first.Roster.Select(p => p.Id)));
        Equal(string.Join(",", first.Roster.Select(p => p.Name)), string.Join(",", second.Roster.Select(p => p.Name)));
        True(first.Select("b"));
        first.SetRoster(new[] { new PlayerTarget("a", "ZZZ"), new PlayerTarget("b", "AAA") });
        Equal("b", first.CurrentTarget!.Id);
        Equal("AAA", first.CurrentTarget.Name);
    }

    private static void ManualCycling()
    {
        var d = Director("c", "a", "b");
        Equal("a", d.CurrentTarget!.Id);
        True(d.Previous()); Equal("c", d.CurrentTarget!.Id);
        True(d.Next()); Equal("a", d.CurrentTarget!.Id);
        True(d.Next()); Equal("b", d.CurrentTarget!.Id);
        True(d.Select("c"));
        long revision = d.SelectionRevision;
        False(d.Select("not-connected")); Equal(revision, d.SelectionRevision);
        Equal("c", d.CurrentTarget!.Id);
    }

    private static void DisconnectFallback()
    {
        var d = Director("a", "b", "c"); d.Select("b");
        d.SetRoster(new[] { new PlayerTarget("c", "C"), new PlayerTarget("a", "A") });
        Equal("c", d.CurrentTarget!.Id);
        d.SetRoster(new[] { new PlayerTarget("a", "A") });
        Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
    }

    private static void ReadinessAndDwell()
    {
        var d = Director("a", "b");
        d.Tick(2, false); Equal(BroadcastState.WaitingForTarget, d.State);
        d.Tick(9, true); Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.Following, d.State);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
    }

    private static void Timeout()
    {
        var d = Director("a", "b", "c");
        d.Tick(2.9, false); Equal("a", d.CurrentTarget!.Id);
        d.Tick(0.1, false); Equal("b", d.CurrentTarget!.Id);
        d.Tick(0, false); Equal("b", d.CurrentTarget!.Id);
        d.Tick(3, false); Equal("c", d.CurrentTarget!.Id);
    }

    private static void Hold()
    {
        var d = Director("a", "b", "c"); d.Tick(4, true); d.SetHolding(true);
        d.Tick(1000, true); Equal("a", d.CurrentTarget!.Id);
        d.SetHolding(false); d.Tick(5, true); Equal("a", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
        d.SetHolding(true); d.Tick(1000, false); Equal("b", d.CurrentTarget!.Id);
        d.Next(); Equal("c", d.CurrentTarget!.Id);
        d.SetHolding(false); d.Tick(2, false); Equal("c", d.CurrentTarget!.Id);
        d.Tick(1, false); Equal("a", d.CurrentTarget!.Id);
    }

    private static void LostReadiness()
    {
        var d = Director("a", "b"); d.Tick(9, true); d.Tick(1, false);
        d.Tick(9, true); Equal("a", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
    }

    private static void Disabled()
    {
        var d = Director("a", "b"); d.Tick(9, true); d.SetEnabled(false);
        d.Tick(100, false); Equal(BroadcastState.Disabled, d.State); Equal("a", d.CurrentTarget!.Id);
        d.Next(); Equal("b", d.CurrentTarget!.Id); Equal(BroadcastState.Disabled, d.State);
        d.SetEnabled(true); d.Tick(9, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("a", d.CurrentTarget!.Id);
    }

    private static void RefreshPreservesProgress()
    {
        var d = Director("a", "b"); d.Tick(9, true);
        long revision = d.SelectionRevision;
        d.SetRoster(new[] { new PlayerTarget("b", "Bob"), new PlayerTarget("a", "Alice renamed") });
        Equal(revision, d.SelectionRevision); Equal(BroadcastState.Following, d.State);
        Equal("Alice renamed", d.CurrentTarget!.Name);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
    }

    private static void SingleTarget()
    {
        var d = Director("a"); long revision = d.SelectionRevision;
        d.Tick(1000, true); Equal(BroadcastState.Following, d.State);
        d.Tick(1000, false); Equal(BroadcastState.WaitingForTarget, d.State);
        Equal("a", d.CurrentTarget!.Id); Equal(revision, d.SelectionRevision);
    }

    private static void LargeDelta()
    {
        var d = Director("a", "b", "c");
        d.Tick(1000, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(0, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(1000, false); Equal("c", d.CurrentTarget!.Id);
        d.Tick(0, false); Equal("c", d.CurrentTarget!.Id);
    }

    private static void InvalidInputs()
    {
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => new BroadcastDirector(invalid));
            Throws<ArgumentOutOfRangeException>(() => new BroadcastDirector(1, invalid));
        }
        var d = Director("a");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
            Throws<ArgumentOutOfRangeException>(() => d.Tick(invalid, true));
        Throws<ArgumentException>(() => new PlayerTarget(" ", "No ID"));
        Throws<ArgumentNullException>(() => d.SetRoster(null!));
        Throws<ArgumentException>(() => d.SetRoster(new PlayerTarget[] { null! }));
        Equal("a", d.CurrentTarget!.Id);
    }

    private static void ObserverChange()
    {
        var d = Director("a", "b");
        d.SetRoster(new[] { new PlayerTarget("a", "Observer"), new PlayerTarget("b", "Target") }, "a");
        Equal("b", d.CurrentTarget!.Id); Equal(1, d.Roster.Count);
    }

    private static void SelfTestTransition()
    {
        var observer = new PlayerTarget("self", "Observer");
        var other = new PlayerTarget("other", "Other player");
        var d = Director("other");
        d.Tick(5, true);
        d.SetSelfTarget(observer);
        Equal(1, d.Roster.Count);
        Equal("self", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
        False(d.Select("other"));
        d.Tick(1, true);
        Equal(BroadcastState.Following, d.State);
        d.SetRoster(new[] { observer, other }, observer.Id);
        Equal("other", d.CurrentTarget!.Id);
        False(d.Select("self"));
        d.SetSelfTarget(observer);
        d.SetRoster(new[] { observer }, observer.Id);
        Equal(BroadcastState.WaitingForPlayers, d.State);
        Equal(null, d.CurrentTarget);
    }

    private static void SelfTestLifetime()
    {
        var d = new BroadcastDirector(10, 3);
        var observer = new PlayerTarget("self", "Observer");
        d.SetSelfTarget(observer);
        Equal(BroadcastState.Disabled, d.State);
        d.SetEnabled(true);
        d.Tick(100, false);
        Equal(BroadcastState.WaitingForTarget, d.State);
        d.Next(); d.Previous(); d.Tick(100, true);
        Equal("self", d.CurrentTarget!.Id);
        long revision = d.SelectionRevision;
        d.SetSelfTarget(new PlayerTarget("self", "Renamed"));
        Equal(revision, d.SelectionRevision);
        Equal(BroadcastState.Following, d.State);
        d.SetHolding(true); d.Tick(100, false);
        Equal("self", d.CurrentTarget!.Id);
        d.SetEnabled(false);
        Throws<ArgumentNullException>(() => d.SetSelfTarget(null!));
        Equal("self", d.CurrentTarget!.Id);
        Equal(BroadcastState.Disabled, d.State);
    }

    private static CoverageDirector Coverage() => new CoverageDirector { ScenicSeconds = 10, CreatureSeconds = 5 };
    private static readonly string[] Creatures = { "boar", "deer" };
    private static readonly string[] None = Array.Empty<string>();

    private static void IdleAlternation()
    {
        var c = Coverage();
        c.Tick(0, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Creature, c.Mode); Equal("boar", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); Equal("deer", c.SubjectId);
    }
    private static void ScenicFallback()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, None);
        long revision = c.Revision;
        c.Tick(86400, null, 0, true, None); Equal(CoverageMode.Scenic, c.Mode); Equal(revision + 1, c.Revision);
        c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
    }
    private static void CreatureLoss()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures);
        c.Tick(0, null, 0, true, new[] { "deer" }, true);
        Equal(CoverageMode.Scenic, c.Mode); Equal(null, c.SubjectId);
    }
    private static void PlayerPriority()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures);
        c.Tick(0, "player", 1, false, Creatures); Equal(CoverageMode.Player, c.Mode); Equal("player", c.SubjectId);
        False(c.AwaitingPlayers);
        c.Tick(100, "player", 1, false, None); Equal("player", c.SubjectId);
        c.Tick(0, null, 0, true, None); Equal(CoverageMode.Scenic, c.Mode);
    }
    private static void PendingPlayer()
    {
        var c = Coverage(); c.Tick(0, null, 2, true, Creatures);
        True(c.AwaitingPlayers); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 2, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, "loaded", 2, true, Creatures); Equal(CoverageMode.Player, c.Mode); False(c.AwaitingPlayers);
    }
    private static void SceneRecovery()
    {
        var c = Coverage(); c.Tick(0, null, 1, false, Creatures); Equal(CoverageMode.Recovery, c.Mode); True(c.AwaitingPlayers);
        c.Tick(100, null, 1, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 1, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, null, 1, false, Creatures); Equal(CoverageMode.Recovery, c.Mode); Equal(null, c.SubjectId);
    }
    private static void CreatureRotation()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, new[] { "deer", "boar" });
        Equal("boar", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal("deer", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
    }
    private static void IdleHold()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(100, null, 0, true, Creatures, true);
        Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); c.Tick(100, null, 0, true, Creatures, true); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, "ready", 1, true, Creatures, true); Equal(CoverageMode.Player, c.Mode);
    }
    private static void CoverageReset()
    {
        var c = Coverage(); c.Tick(0, null, 1, true, Creatures); c.Tick(10, null, 1, true, Creatures);
        c.Reset(); Equal(CoverageMode.Disabled, c.Mode); False(c.AwaitingPlayers); Equal(null, c.SubjectId);
        c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal("boar", c.SubjectId);
    }
    private static void CoverageValidation()
    {
        var c = Coverage();
        Throws<ArgumentOutOfRangeException>(() => c.Tick(double.NaN, null, 0, true, None));
        Throws<ArgumentOutOfRangeException>(() => c.Tick(0, null, -1, true, None));
        Throws<ArgumentOutOfRangeException>(() => c.ScenicSeconds = 0);
        Throws<ArgumentOutOfRangeException>(() => c.CreatureSeconds = double.PositiveInfinity);
        Throws<ArgumentNullException>(() => c.Tick(0, null, 0, true, null!));
    }
    private static void BoundedAcquisition()
    {
        var a = new AcquisitionWindow();
        False(a.Tick(29, true, 20, 30)); True(a.Tick(1, true, 20, 30));
        True(a.Tick(19, true, 20, 30)); False(a.Tick(1, true, 20, 30));
        False(a.Tick(29, true, 20, 30)); True(a.Tick(1, true, 20, 30));
        False(a.Tick(0, false, 20, 30)); False(a.Active);
        False(a.Tick(0, true, 20, 30));
        Throws<ArgumentOutOfRangeException>(() => a.Tick(-1, true, 20, 30));
        True(a.Tick(100000, true, 20, 30)); // one transition, no catch-up loop
        a.Reset(); False(a.Active);
    }
    private static void CoverageIntegration()
    {
        var players = Director("a", "b"); var c = Coverage();
        c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("a", c.SubjectId);
        players.Next(); c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("b", c.SubjectId);
        players.SetRoster(new[] { new PlayerTarget("a", "A") });
        players.Tick(100, true); c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("a", c.SubjectId);
        players.SetRoster(Array.Empty<PlayerTarget>()); c.Tick(0, players.CurrentTarget?.Id, 3, true, Creatures);
        Equal(CoverageMode.Scenic, c.Mode); True(c.AwaitingPlayers);
        players.SetRoster(new[] { new PlayerTarget("c", "C") });
        c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal(CoverageMode.Player, c.Mode);
    }
    private static void IdleSoak()
    {
        var c = Coverage(); int scenic = 0, creature = 0;
        for (int second = 0; second < 86400; second++)
        {
            c.Tick(1, null, 0, true, Creatures);
            if (c.Mode == CoverageMode.Scenic) scenic++;
            else if (c.Mode == CoverageMode.Creature) { creature++; True(Creatures.Contains(c.SubjectId)); }
            else throw new Exception("Idle schedule entered unexpected state.");
        }
        True(scenic > creature && creature > 1000);
        True(c.Revision < 20000);
    }

    private static void ProbeReady()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(5, false);
        Equal(ProbeState.Acquiring, p.State); p.Tick(3, true);
        Equal(ProbeState.Viewing, p.State); Equal(8.0, p.FirstReadySeconds!.Value);
        p.Tick(10, true); Equal(8.0, p.FirstReadySeconds!.Value); True(p.Active);
    }
    private static void ProbeTimeout()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(30, false);
        Equal(ProbeState.TimedOut, p.State); False(p.Active);
        p.Tick(1, true); Equal(ProbeState.TimedOut, p.State);
        p.Start(); Equal(null, p.FirstReadySeconds); p.Stop(); False(p.Active);
        Equal(ProbeState.Stopped, p.State);
        Throws<ArgumentOutOfRangeException>(() => p.Tick(double.NaN, false));
    }
    private static void ProbeLifetime()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(10, true); p.Tick(20, false);
        Equal(ProbeState.Acquiring, p.State); p.Tick(10, false); False(p.Active);
        p.Start(); p.Tick(10, true); p.Tick(110, true);
        Equal(ProbeState.Completed, p.State); False(p.Active);
    }
    private static void CreatureCooldown()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, new[] { "boar" }); c.Tick(10, null, 0, true, new[] { "boar" });
        Equal("boar", c.SubjectId); c.Tick(5, null, 0, true, new[] { "boar" });
        long revision = c.Revision;
        c.Tick(10, null, 0, true, new[] { "boar" });
        Equal(CoverageMode.Scenic, c.Mode); True(c.Revision > revision);
        c.Tick(300, null, 0, true, new[] { "boar" }); Equal("boar", c.SubjectId);
    }

    private static void RetentionAbort()
    {
        var observer = new object(); var distantActor = new object();
        True(SceneRetention.MustAbort(true, observer, new[] { distantActor }, Array.Empty<object>()));
        True(SceneRetention.MustAbort<object>(true, null, new[] { distantActor }, Array.Empty<object>()));
        // These are the already-computed removal inputs; restoring reference alone
        // cannot make them safe. The caller must skip this pass and recompute.
        var recomputed = new[] { observer };
        False(SceneRetention.MustAbort(true, observer, recomputed, Array.Empty<object>()));
    }
    private static void RetentionAllowed()
    {
        var observer = new object();
        False(SceneRetention.MustAbort(false, observer, Array.Empty<object>(), Array.Empty<object>()));
        False(SceneRetention.MustAbort(true, observer, new[] { observer }, Array.Empty<object>()));
        False(SceneRetention.MustAbort(true, observer, Array.Empty<object>(), new[] { observer }));
    }
    private static void ProbeFailure()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(0.1, false);
        p.Fail("observer excluded"); p.Stop(); p.Tick(100, true);
        Equal(ProbeState.Failed, p.State); Equal("observer excluded", p.FailureReason); False(p.Active);
        p.Start(); Equal(null, p.FailureReason); Equal(ProbeState.Acquiring, p.State);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {(object?)expected ?? "<null>"}, got {(object?)actual ?? "<null>"}.");
    }

    private static void True(bool value) { if (!value) throw new Exception("Expected true."); }
    private static void False(bool value) { if (value) throw new Exception("Expected false."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }
}
