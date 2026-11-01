using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class PlayerCoverageTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Group coverage requires sustained readable footage without duplicate credit", MeaningfulFootage);
        yield return ("Missing frames and stalled callbacks cannot manufacture group coverage", MissingFootage);
        yield return ("Viewing credit expires and remains bounded across player churn", ExpiryAndCapacity);
        yield return ("Ordinary automatic rotation skips a covered companion but manual controls do not", OrdinarySelection);
        yield return ("An entirely covered group retains its primary until a companion needs coverage", WholeGroup);
        yield return ("Autonomous rotation credits companions and still covers an unseen player", AutonomousSelection);
        yield return ("Covered unavailable targets do not suppress recovery and world reset clears credit", RecoveryAndReset);
    }

    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Equal(string expected, string? actual) { if (expected != actual) throw new Exception($"Expected {expected}; got {actual}"); }

    private static void MeaningfulFootage()
    {
        var history = new PlayerCoverageHistory();
        for (int i = 1; i <= 5; i++)
        {
            history.Record(i, 1, new[] { "a", "a", "a" });
            history.Record(i, 1, new[] { "a" });
            Assert(!history.RecentlyCovered("a", i, 30));
        }
        history.Record(6, 1, new[] { "a", "b" });
        Assert(history.RecentlyCovered("a", 6, 30));
        Assert(!history.RecentlyCovered("b", 6, 30));
    }

    private static void MissingFootage()
    {
        var history = new PlayerCoverageHistory();
        for (int i = 1; i <= 5; i++) history.Record(i, 1, new[] { "a" });
        history.Record(6, 1, Array.Empty<string>());
        for (int i = 7; i <= 11; i++) history.Record(i, 1, new[] { "a" });
        Assert(!history.RecentlyCovered("a", 11, 30), "An unseen interval must break the qualifying run");
        history.Record(100, 89, new[] { "a" });
        Assert(!history.RecentlyCovered("a", 100, 30), "A long stall is not footage");
        history.Record(101, 1, new[] { "a" });
        Assert(!history.RecentlyCovered("a", 101, 30));
    }

    private static void ExpiryAndCapacity()
    {
        var history = new PlayerCoverageHistory();
        for (int i = 1; i <= 6; i++) history.Record(i, 1, new[] { "a" });
        Assert(history.RecentlyCovered("a", 35.9, 30));
        Assert(!history.RecentlyCovered("a", 36, 30));
        for (int i = 7; i < 2000; i++) history.Record(i, 1, new[] { "p" + i });
        Assert(history.Count == PlayerCoverageHistory.Capacity);
        history.Reset(); Assert(history.Count == 0);
        history.Record(0, 0, new[] { "a" });
        Assert(!history.RecentlyCovered("a", 0, 30));
    }

    private static BroadcastDirector Ordinary(params string[] ids)
    {
        var director = new BroadcastDirector(10, 2);
        var players = new List<PlayerTarget>();
        foreach (string id in ids) players.Add(new PlayerTarget(id, id));
        director.SetRoster(players); director.SetEnabled(true); return director;
    }

    private static void OrdinarySelection()
    {
        var director = Ordinary("a", "b", "c");
        for (int i = 1; i <= 10; i++)
        { director.RecordCoverage(i, 1, new[] { "a", "b" }); director.Tick(1, true); }
        Equal("c", director.CurrentTarget?.Id);
        director.Previous(); Equal("b", director.CurrentTarget?.Id);
        director.Next(); Equal("c", director.CurrentTarget?.Id);
        director.Select("a"); Equal("a", director.CurrentTarget?.Id);
    }

    private static void WholeGroup()
    {
        var director = Ordinary("a", "b");
        for (int i = 1; i <= 100; i++)
        {
            director.RecordCoverage(i, 1, new[] { "a", "b" }); director.Tick(1, true);
            Equal("a", director.CurrentTarget?.Id);
        }
        for (int i = 101; i <= 111; i++)
        { director.RecordCoverage(i, 1, new[] { "a" }); director.Tick(1, true); }
        Equal("b", director.CurrentTarget?.Id);
    }

    private static AutonomyPlayer Player(string id, bool ready = true, bool fresh = true) =>
        new AutonomyPlayer(id, id, fresh ? new AutonomyPosition(0, 0, 0) : (AutonomyPosition?)null, fresh ? 10000 : 0, ready);
    private static void Step(AutonomousDirector director, double now, IReadOnlyList<AutonomyPlayer> players, string? shown) =>
        director.Tick(now, 1, players, Array.Empty<ScenicRegion>(), new AutonomyPosition(0, 0, 0), shown, false);

    private static void AutonomousSelection()
    {
        var director = new AutonomousDirector { DwellSeconds = 10 };
        director.SetEnabled(true);
        var players = new[] { Player("a"), Player("b"), Player("c") };
        for (int i = 1; i <= 10; i++)
        { director.RecordCoverage(i, 1, new[] { "a", "b" }); Step(director, i, players, "a"); }
        Equal("c", director.TargetId);
        director.Next(-1); Step(director, 11, players, "c"); Equal("b", director.TargetId);
        director.Select("a"); Step(director, 12, players, "b"); Equal("a", director.TargetId);
    }

    private static void RecoveryAndReset()
    {
        var director = new AutonomousDirector { DwellSeconds = 10 };
        director.SetEnabled(true);
        var players = new[] { Player("a"), Player("b") };
        for (int i = 1; i <= 10; i++)
        { director.RecordCoverage(i, 1, new[] { "a", "b" }); Step(director, i, players, "a"); }
        Equal("a", director.TargetId);
        Step(director, 11, new[] { Player("b") }, null); Equal("b", director.TargetId);
        director.Reset(); director.SetEnabled(true);
        for (int i = 1; i <= 10; i++) Step(director, i, players, "a");
        Equal("b", director.TargetId);

        var ordinary = Ordinary("a", "b");
        for (int i = 1; i <= 6; i++) ordinary.RecordCoverage(i, 1, new[] { "a", "b" });
        ordinary.Tick(2, false); Equal("b", ordinary.CurrentTarget?.Id);
        ordinary.SetEnabled(false); ordinary.SetEnabled(true); ordinary.Select("a");
        ordinary.Tick(10, true); Equal("b", ordinary.CurrentTarget?.Id);
    }
}
