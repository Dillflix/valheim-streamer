using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class EncounterSituationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Ordinary hostile presence influences framing before combat", Presence);
        yield return ("Closing pressure requires reliable persistent motion", Closing);
        yield return ("Retreating or parallel enemies do not manufacture closing pressure", Retreat);
        yield return ("Several ordinary nearby enemies establish developing pressure", Crowd);
        yield return ("Surrounding geometry distinguishes enclosure from a frontal crowd", Surrounding);
        yield return ("A half-circle cannot claim encirclement", HalfCircle);
        yield return ("Walls and neutral wildlife cannot contribute hostile pressure", Unrelated);
        yield return ("Duplicate observations cannot inflate crowd pressure", Duplicates);
        yield return ("Distant passive presence is bounded while corroborated combat remains meaningful", Distance);
        yield return ("Crowding alone never claims combat", NoInventedCombat);
        yield return ("Missing actors immediately clear local pressure", Disappearance);
        yield return ("Survey gaps discard pressure persistence", Gap);
        yield return ("Hunting and combat retain priority over hostile presence", Priority);
        yield return ("Encounter cast can include more than three allies and three enemies", LargerCast);
        yield return ("Crowds cannot evict the story owner key opponent and all allies", MixedBudget);
        yield return ("Reserved opponent sector participates in spatial diversity", RepresentativeSectors);
        yield return ("Participant admission is independent of enumeration order", StableCast);
        yield return ("Participant cast enforces its bounded observational budget", CastBounds);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Encounter situation assertion failed."); }
    private static EncounterObservation O(string id = "hostile", float x = 0, float z = 16, float closing = 0,
        bool motion = false, bool hostile = true, bool connected = true, bool attacking = false) =>
        new EncounterObservation(id, (float)Math.Sqrt(x * x + z * z), (float)Math.Sqrt(x * x + z * z), 0, 0,
            clearConnection: connected, hostile: hostile, attacking: attacking, offsetX: x, offsetZ: z,
            closingSpeed: closing, motionReliable: motion);
    private static EncounterSituation Tick(EncounterSituationPolicy p, double from, double until, params EncounterObservation[] values)
    {
        EncounterSituation result = default;
        for (double now = from; now <= until + .001; now += .25) result = p.Observe(now, values, false);
        return result;
    }
    private static void Presence()
    {
        Check(EncounterPolicy.Classify(O(), false, false).Kind == EncounterKind.Threat);
        var value = Tick(new EncounterSituationPolicy(), 0, 2, O());
        Check(value.Pressure == EncounterPressure.Presence && value.HostileCount == 1 && value.CombatantCount == 0);
    }
    private static void Closing()
    {
        var p = new EncounterSituationPolicy(); var moving = O(closing: 2, motion: true);
        Check(Tick(p, 0, .5, moving).Pressure == EncounterPressure.Presence);
        Check(Tick(p, .75, 1, moving).Pressure == EncounterPressure.Developing);
        var uncertain = Tick(new EncounterSituationPolicy(), 0, 1, O(closing: 2));
        Check(uncertain.Pressure == EncounterPressure.Presence && uncertain.ApproachingCount == 0);
    }
    private static void Retreat()
    {
        foreach (float velocity in new[] { -3f, 0f })
            Check(Tick(new EncounterSituationPolicy(), 0, 2, O(closing: velocity, motion: true)).Pressure == EncounterPressure.Presence);
    }
    private static void Crowd()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O("a", -2), O("b"), O("c", 2));
        Check(result.Pressure == EncounterPressure.Developing && result.HostileCount == 3 && !result.Encircled);
    }
    private static void Surrounding()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O("north", 0, 12), O("se", 10, -6), O("sw", -10, -6));
        Check(result.Encircled && result.OccupiedSectors == 3 && result.Pressure == EncounterPressure.Developing);
    }
    private static void HalfCircle()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O("west", -12, 0), O("north", 0, 12), O("east", 12, 0));
        Check(!result.Encircled);
    }
    private static void Unrelated()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O("wall", connected: false), O("deer", hostile: false));
        Check(result.Pressure == EncounterPressure.None && result.HostileCount == 0);
    }
    private static void Duplicates()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O(), O(), O());
        Check(result.HostileCount == 1 && result.Pressure == EncounterPressure.Presence);
    }
    private static void Distance()
    {
        Check(Tick(new EncounterSituationPolicy(), 0, 1, O(z: 35)).HostileCount == 0);
        var enemy = new EncounterObservation("ranged", 35, 35, 0, 0, targetsGroup: true, offsetZ: 35);
        var result = Tick(new EncounterSituationPolicy(), 0, 1, enemy);
        Check(result.HostileCount == 1 && result.Pressure == EncounterPressure.Engaged);
    }
    private static void NoInventedCombat()
    {
        var result = Tick(new EncounterSituationPolicy(), 0, 1, O("a", -2, 4), O("b", 0, 5), O("c", 2, 4));
        Check(result.Pressure == EncounterPressure.Developing && result.CombatantCount == 0);
        Check(Tick(new EncounterSituationPolicy(), 0, .25, O(z: 8, attacking: true)).Pressure == EncounterPressure.Engaged);
    }
    private static void Disappearance()
    {
        var p = new EncounterSituationPolicy(); Tick(p, 0, 1, O(z: 5));
        var result = p.Observe(1.25, Array.Empty<EncounterObservation>(), false);
        Check(result.Pressure == EncounterPressure.None && result.HostileCount == 0 && result.SectorMask == 0);
    }
    private static void Gap()
    {
        var p = new EncounterSituationPolicy(); Tick(p, 0, 1, O(z: 5));
        Check(p.Observe(3, new[] { O(z: 5) }, false).Pressure == EncounterPressure.Presence);
        Check(p.Observe(1, new[] { O(z: 5) }, false).Pressure == EncounterPressure.Presence);
    }
    private static void Priority()
    {
        var threat = new EncounterObservation("prominent", 1, 1, 0, 1, closingSpeed: 20, motionReliable: true);
        var hunt = new EncounterObservation("quarry", 96, 96, .985f, 0, hostile: false);
        var combat = O(z: 8, attacking: true);
        Check(EncounterPolicy.Classify(threat, false, false).Score < EncounterPolicy.Classify(hunt, true, false).Score);
        Check(EncounterPolicy.Classify(hunt, true, false).Score < EncounterPolicy.Classify(combat, false, false).Score);
    }
    private static EncounterParticipant P(string id, float x, float z, bool enemy = false, bool key = false) =>
        new EncounterParticipant(id, new AutonomyPosition(x, 0, z), opponent: enemy, keyOpponent: key, primary: id == "owner", relevance: enemy ? 4 : 0);
    private static void LargerCast()
    {
        var values = new List<EncounterParticipant> { P("owner", 0, 0) };
        for (int i = 0; i < 6; i++) { values.Add(P("friend" + i, i, 2)); values.Add(P("enemy" + i, i, 8, true, i == 0)); }
        var cast = new EncounterParticipantPolicy().Select(values);
        Check(cast.Count == 13 && cast[0] == "owner" && cast[1] == "enemy0");
    }
    private static void MixedBudget()
    {
        var values = new List<EncounterParticipant> { P("owner", 0, 0), P("friend", -2, 0), P("principal", 0, 8, true, true) };
        for (int i = 0; i < 40; i++) values.Add(P("mob" + i, i % 8, i % 5 + 10, true));
        var cast = new EncounterParticipantPolicy().Select(values);
        Check(cast.Count == 16 && cast[0] == "owner" && cast[1] == "principal" && cast[2] == "friend");
    }
    private static void RepresentativeSectors()
    {
        var cast = new EncounterParticipantPolicy().Select(new[] { P("owner", 0, 0), P("key", 0, 10, true, true),
            P("a-same", 0, 9, true), P("b-opposite", 0, -10, true) });
        Check(cast[2] == "b-opposite");
    }
    private static void StableCast()
    {
        var values = new List<EncounterParticipant> { P("owner", 0, 0), P("key", 0, 10, true, true) };
        for (int i = 0; i < 24; i++) values.Add(P("actor" + i, i % 6 - 3, i % 7 - 3, i % 2 == 0));
        var a = new List<string>(new EncounterParticipantPolicy().Select(values)); values.Reverse();
        var b = new EncounterParticipantPolicy().Select(values);
        Check(a.Count == b.Count); for (int i = 0; i < a.Count; i++) Check(a[i] == b[i]);
    }
    private static void CastBounds()
    {
        var values = new List<EncounterParticipant> { P("owner", 0, 0) };
        for (int i = 0; i < 100; i++) values.Add(P("actor" + i, i % 10, i % 8));
        Check(new EncounterParticipantPolicy().Select(values).Count == EncounterParticipantPolicy.MaximumParticipants);
        Check(new EncounterParticipantPolicy().Select(new[] { P("other", 0, 0) }).Count == 0);
        bool rejected = false;
        try { new EncounterParticipantPolicy().Select(new EncounterParticipant[129]); } catch (ArgumentException) { rejected = true; }
        Check(rejected);
    }
}
