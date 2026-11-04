using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class SceneAwarenessTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Structural roofs distinguish shelters and rooms without changing dungeon identity", RoomKinds);
        yield return ("Room measurements retain constraints across brief boundary noise", RoomPersistence);
        yield return ("Doorway restraint persists through exit before open-space freedom", Doorway);
        yield return ("Incomplete room surveys never retain a confident room classification", UnknownSurvey);
        yield return ("Room evidence resets after gaps and backwards clocks", RoomGap);
        yield return ("Room measurements reject invalid geometry counts", InvalidRoom);
        yield return ("Locomotion speed thresholds have persistence and hysteresis", Speeds);
        yield return ("Vertical traversal preserves its own locomotion profile", Vertical);
        yield return ("Observed swimming and ship support override speed guesses", SupportedMotion);
        yield return ("Locomotion evidence is not inherited across sample gaps", MotionGap);
        yield return ("Known group primary handoff retains companions and previous primary", GroupHandoff);
        yield return ("Group handoff refuses strangers, walls, other floors and stale evidence", InvalidHandoff);
        yield return ("Group handoff drops departed companions without fabricating positions", HandoffDeparture);
        yield return ("Generic player actions and unexplained damage do not manufacture nearby combat", CombatEvidence);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Scene awareness assertion failed."); }
    private static RoomObservation R(int roofs, int walls, bool door = false, bool complete = true, bool dungeon = false) =>
        new RoomObservation(complete, dungeon, roofs, walls, roofs == 0 ? 0 : 3, door);
    private static void RoomKinds()
    {
        var p = new RoomAwarenessPolicy();
        p.Observe(0, R(0, 0)); Check(p.Environment == ShotEnvironment.Open);
        for (double t = .25; t <= 1; t += .25) p.Observe(t, R(5, 0));
        Check(p.Environment == ShotEnvironment.Sheltered && p.CeilingHeight == 3);
        for (double t = 1.25; t <= 2; t += .25) p.Observe(t, R(5, 5));
        Check(p.Environment == ShotEnvironment.Room);
        p.Observe(2.25, R(0, 0, dungeon: true)); Check(p.Environment == ShotEnvironment.Dungeon);
    }
    private static void RoomPersistence()
    {
        var p = new RoomAwarenessPolicy(); p.Observe(0, R(5, 4));
        p.Observe(.25, R(0, 0)); p.Observe(.5, R(0, 0));
        Check(p.Environment == ShotEnvironment.Room && p.Confidence < .5);
        p.Observe(.75, R(5, 4)); Check(p.Environment == ShotEnvironment.Room && p.Confidence > .5);
        for (double t = 1; t <= 2.25; t += .25) p.Observe(t, R(0, 0));
        Check(p.Environment == ShotEnvironment.Open);
    }
    private static void Doorway()
    {
        var p = new RoomAwarenessPolicy(); p.Observe(0, R(5, 4));
        p.Observe(.25, R(3, 2, door: true)); Check(p.Environment == ShotEnvironment.Doorway && p.DoorwayTransition);
        for (double t = .5; t <= 1.5; t += .25) p.Observe(t, R(0, 0));
        Check(p.Environment == ShotEnvironment.Doorway && p.DoorwayTransition);
        p.Observe(1.75, R(0, 0)); Check(!p.DoorwayTransition && p.Environment == ShotEnvironment.Doorway);
        for (double t = 2; t <= 3; t += .25) p.Observe(t, R(0, 0));
        Check(p.Environment == ShotEnvironment.Open);
    }
    private static void UnknownSurvey()
    {
        var p = new RoomAwarenessPolicy(); p.Observe(0, R(5, 4));
        p.Observe(.25, R(5, 4, complete: false));
        Check(p.Environment == ShotEnvironment.Unknown && p.Confidence == 0 && p.CeilingHeight == 0);
    }
    private static void RoomGap()
    {
        var p = new RoomAwarenessPolicy(); p.Observe(0, R(5, 4, door: true));
        p.Observe(3, R(0, 0)); Check(p.Environment == ShotEnvironment.Open && !p.DoorwayTransition);
        p.Observe(1, R(5, 0)); Check(p.Environment == ShotEnvironment.Sheltered);
        p.Reset(); Check(p.Environment == ShotEnvironment.Unknown && p.Confidence == 0);
    }
    private static void InvalidRoom()
    {
        bool threw = false;
        try { _ = new RoomObservation(true, false, 6, 0, 1, false); } catch (ArgumentException) { threw = true; }
        Check(threw);
    }
    private static void Motion(LocomotionObservationPolicy p, double from, double through, float speed, float y = 0,
        bool swim = false, bool ship = false)
    { for (double t = from; t <= through + .001; t += .25) p.Observe(t, speed, y, swim, ship); }
    private static void Speeds()
    {
        var p = new LocomotionObservationPolicy(); Motion(p, 0, 1, 2);
        Check(p.Locomotion == ShotLocomotion.Walk);
        Motion(p, 1.25, 2, 5.2f); Check(p.Locomotion == ShotLocomotion.Run);
        Motion(p, 2.25, 3, 4.6f); Check(p.Locomotion == ShotLocomotion.Run);
        Motion(p, 3.25, 4, 3); Check(p.Locomotion == ShotLocomotion.Walk);
        Motion(p, 4.25, 5, .1f); Check(p.Locomotion == ShotLocomotion.Stationary);
    }
    private static void Vertical()
    {
        var p = new LocomotionObservationPolicy(); Motion(p, 0, 1, 2, 1);
        Check(p.Locomotion == ShotLocomotion.Vertical);
        p.Observe(1.25, 2, 0, false, false); Check(p.Locomotion == ShotLocomotion.Vertical);
        Motion(p, 1.5, 2, 2); Check(p.Locomotion == ShotLocomotion.Walk);
    }
    private static void SupportedMotion()
    {
        var p = new LocomotionObservationPolicy(); Motion(p, 0, .5, 8, swim: true);
        Check(p.Locomotion == ShotLocomotion.Swim);
        Motion(p, .75, 1.25, 0, ship: true); Check(p.Locomotion == ShotLocomotion.Boat && p.Confidence >= .8f);
    }
    private static void MotionGap()
    {
        var p = new LocomotionObservationPolicy(); Motion(p, 0, 1, 8);
        p.Observe(4, 0, 0, false, false); Check(p.Locomotion == ShotLocomotion.Stationary);
        p.Reset(); Check(p.Confidence == 0);
    }

    private static AutonomyPosition P(float x, float y = 0) => new AutonomyPosition(x, y, 0);
    private static CompanionObservation O(string id, float x, bool clear = true, float y = 0) =>
        new CompanionObservation(id, P(x, y), true, clear, false);
    private static CompanionGroupPolicy Group()
    {
        var p = new CompanionGroupPolicy();
        for (double t = 0; t <= 2; t += .25) p.Update(t, "a", P(0), new[] { O("b", 5), O("c", 8) });
        return p;
    }
    private static bool Has(IReadOnlyList<string> values, string id)
    { foreach (string value in values) if (value == id) return true; return false; }
    private static void GroupHandoff()
    {
        var p = Group(); Check(p.TryReanchor(2.25, "b", P(5), P(0), true));
        p.Update(2.25, "b", P(5), new[] { O("a", 0), O("c", 8) });
        Check(p.Members.Count == 2 && Has(p.Members, "a") && Has(p.Members, "c"));
        Check(p.TryReanchor(2.5, "c", P(8), P(5), true));
        p.Update(2.5, "c", P(8), new[] { O("a", 0), O("b", 5) });
        Check(p.Members.Count == 2 && Has(p.Members, "a") && Has(p.Members, "b"));
    }
    private static void InvalidHandoff()
    {
        var p = Group();
        Check(!p.TryReanchor(2.25, "stranger", P(3), P(0), true));
        Check(!p.TryReanchor(2.25, "b", P(5), P(0), false));
        Check(!p.TryReanchor(2.25, "b", P(5, 5), P(0), true));
        Check(!p.TryReanchor(2.25, "b", P(25), P(0), true));
        Check(!p.TryReanchor(4, "b", P(5), P(0), true));
    }
    private static void HandoffDeparture()
    {
        var p = Group(); Check(p.TryReanchor(2.25, "b", P(5), P(0), true));
        p.Update(2.25, "b", P(5), new[] { O("a", 0) });
        Check(p.Members.Count == 1 && Has(p.Members, "a") && !Has(p.Members, "c"));
    }
    private static void CombatEvidence()
    {
        Check(!ObservedEncounterEvidence.SupportsCombat(false, false, true, false, 3));
        Check(!ObservedEncounterEvidence.SupportsCombat(false, false, false, true, 3));
        Check(ObservedEncounterEvidence.SupportsCombat(false, true, false, false, 3));
        Check(ObservedEncounterEvidence.SupportsCombat(false, false, true, true, 3));
        Check(!ObservedEncounterEvidence.SupportsCombat(false, true, true, true, 13));
        Check(ObservedEncounterEvidence.SupportsCombat(true, false, false, false, 18));
    }
}
