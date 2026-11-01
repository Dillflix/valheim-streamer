using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CompanionGroupPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Companion joins require persistence and shared travel joins sooner", PersistentJoin);
        yield return ("Companion distance hysteresis avoids membership chatter", ExitGrace);
        yield return ("Companion groups reject other floors, interiors and walls", SeparatedSpaces);
        yield return ("Companion groups remain bounded and anchored to primary", BoundedGroup);
        yield return ("Companion membership resets after gaps and primary changes", ResetBindings);
        yield return ("Companion disappearance removes stale members immediately", Disappearance);
    }

    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static CompanionObservation O(string id, float x, float y = 0, bool same = true, bool clear = true, bool moving = false) =>
        new CompanionObservation(id, new AutonomyPosition(x, y, 0), same, clear, moving);
    private static void Check(bool condition) { if (!condition) throw new Exception("Companion group assertion failed."); }
    private static void Tick(CompanionGroupPolicy policy, double from, double to, params CompanionObservation[] observations)
    { for (double time = from; time <= to + .001; time += .25) policy.Update(time, "primary", Origin, observations); }

    private static void PersistentJoin()
    {
        var p = new CompanionGroupPolicy();
        Tick(p, 0, 1, O("walking", 8, moving: true), O("idle", 7));
        Check(p.Members.Count == 1 && p.Members[0] == "walking");
        Tick(p, 1.25, 2, O("walking", 8, moving: true), O("idle", 7));
        Check(p.Members.Count == 2);
    }
    private static void ExitGrace()
    {
        var p = new CompanionGroupPolicy(); Tick(p, 0, 2, O("friend", 10));
        Tick(p, 2.25, 3, O("friend", 25)); Check(p.Members.Count == 1);
        Tick(p, 3.25, 3.75, O("friend", 22)); Check(p.Members.Count == 1);
        Tick(p, 4, 6, O("friend", 25)); Check(p.Members.Count == 0);
    }
    private static void SeparatedSpaces()
    {
        var p = new CompanionGroupPolicy();
        Tick(p, 0, 3, O("floor", 2, 4), O("dungeon", 2, same: false), O("wall", 2, clear: false));
        Check(p.Members.Count == 0);
        Tick(p, 3.25, 5.5, O("friend", 2)); Check(p.Members.Count == 1);
        p.Update(5.75, "primary", Origin, new[] { O("friend", 2, same: false) }); Check(p.Members.Count == 0);
    }
    private static void BoundedGroup()
    {
        var p = new CompanionGroupPolicy();
        Tick(p, 0, 3, O("a", 17), O("b", -17), O("c", 5), O("d", 6), O("e", 34));
        Check(p.Members.Count == 3 && !Contains(p.Members, "e"));
        Check(!(Contains(p.Members, "a") && Contains(p.Members, "b")));
        // Existing coherent companions keep their membership when a slightly closer stranger arrives.
        Tick(p, 3.25, 6, O("a", 17), O("c", 5), O("d", 6), O("new", 1));
        Check(!Contains(p.Members, "new"));
    }
    private static void ResetBindings()
    {
        var p = new CompanionGroupPolicy(); Tick(p, 0, 2, O("friend", 10));
        p.Update(5, "primary", Origin, new[] { O("friend", 10) }); Check(p.Members.Count == 0);
        Tick(p, 5.25, 7, O("friend", 10)); Check(p.Members.Count == 1);
        p.Update(7.25, "other", Origin, new[] { O("friend", 10) }); Check(p.Members.Count == 0);
    }
    private static void Disappearance()
    {
        var p = new CompanionGroupPolicy(); Tick(p, 0, 2, O("friend", 10));
        p.Update(2.25, "primary", Origin, Array.Empty<CompanionObservation>()); Check(p.Members.Count == 0);
    }
    private static bool Contains(IReadOnlyList<string> ids, string id)
    { foreach (string value in ids) if (value == id) return true; return false; }
}
