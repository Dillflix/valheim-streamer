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
            ("Solo target remains stable through cycling, readiness, and refresh", SelfTestLifetime)
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
