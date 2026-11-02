using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class EncounterPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Ranged release retains only a qualified hunting target for a bounded aftermath", ReleaseGrace);
        yield return ("Ranged draw context cannot survive gaps, backwards clocks or primary reset", ReleaseReset);
        yield return ("Ranged hunting requires active aim and a narrow supported target cone", HuntingEvidence);
        yield return ("Hunting rejects obstructed reticle direction despite nearby structural group connection", HuntingObstruction);
        yield return ("Ambiguous animals do not manufacture an exact hunting target", AmbiguousHunting);
        yield return ("Hunting acquires a stable distant subject without admitting world positions", DistantHunting);
        yield return ("Strong combat evidence preempts retained hunting", CombatPreempts);
        yield return ("Passive powerful hostile is a threat not confirmed combat", PassiveThreat);
        yield return ("A resource swing near a passive enemy does not manufacture combat", ResourceSwing);
        yield return ("Multiple combat opponents are ranked by involvement before distance", GroupEncounter);
        yield return ("Encounter rank and selection are independent of actor enumeration order", StableOrdering);
        yield return ("A retained combat target resists marginal challenger score changes", StableFocus);
        yield return ("Missing dead or blocked encounter interest is immediately invalidated", LossInvalidates);
        yield return ("Hunting cone turn clears an admitted interest", HuntTurn);
        yield return ("Stale sample gaps and backwards time reset encounter memory", ResetGaps);
        yield return ("Invalid actor distances and nonfinite evidence never become subjects", InvalidEvidence);
        yield return ("Encounter scan size remains explicitly bounded", Bounds);
        yield return ("Previously active fight retention cannot promote a distant enemy", RetentionRange);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Encounter policy assertion failed."); }
    private static EncounterObservation Hunt(string id = "deer", float distance = 60, float alignment = 1) =>
        new EncounterObservation(id, distance, distance, alignment, 0, hostile: false);
    private static EncounterObservation Fight(string id = "enemy", float distance = 8, bool exact = false, bool attack = true) =>
        new EncounterObservation(id, distance, distance, 0, .2f, targetsGroup: exact, attacking: attack);
    private static EncounterCandidate Acquire(EncounterPolicy p, EncounterObservation value, bool aim = false, double start = 0)
    { p.Update(start, aim, false, new[] { value }); return p.Update(start + .9, aim, false, new[] { value }); }
    private static void ReleaseGrace()
    {
        var p = new RangedAimContinuity();
        Check(p.Observe(0, true, EncounterKind.None));
        Check(!p.Observe(.25, false, EncounterKind.None));
        Check(p.Observe(.5, false, EncounterKind.Hunting));
        Check(p.Observe(1.5, false, EncounterKind.Hunting));
        Check(p.Observe(2.5, false, EncounterKind.Hunting));
        Check(!p.Observe(2.75, false, EncounterKind.Hunting));
    }
    private static void ReleaseReset()
    {
        var p = new RangedAimContinuity(); p.Observe(0, true, EncounterKind.Hunting);
        Check(!p.Observe(2, false, EncounterKind.Hunting));
        p.Observe(2.25, true, EncounterKind.Hunting);
        Check(!p.Observe(1, false, EncounterKind.Hunting));
        p.Observe(1.25, true, EncounterKind.Hunting); p.Reset();
        Check(!p.Observe(1.5, false, EncounterKind.Hunting));
    }
    private static void HuntingEvidence()
    {
        Check(EncounterPolicy.Classify(Hunt(), false, false).Kind == EncounterKind.None);
        Check(EncounterPolicy.Classify(Hunt(alignment: .98f), true, false).Kind == EncounterKind.None);
        Check(EncounterPolicy.Classify(Hunt(), true, false).Kind == EncounterKind.Hunting);
    }
    private static void HuntingObstruction()
    {
        var o = new EncounterObservation("deer", 15, 15, 1, 0, hostile: false, clearAimLine: false);
        Check(EncounterPolicy.Classify(o, true, false).Kind == EncounterKind.None);
    }
    private static void AmbiguousHunting()
    {
        var p = new EncounterPolicy(); var list = new[] { Hunt("a"), Hunt("b", 61, .999f) };
        p.Update(0, true, false, list); Check(p.Update(.9, true, false, list).Kind == EncounterKind.None);
    }
    private static void DistantHunting()
    {
        var p = new EncounterPolicy(); Check(p.Update(0, true, false, new[] { Hunt() }).Kind == EncounterKind.None);
        Check(p.Update(.7, true, false, new[] { Hunt() }).Kind == EncounterKind.Hunting);
        Check(p.Selected.Distance == 60 && p.Selected.Confidence < .8f);
    }
    private static void CombatPreempts()
    {
        var p = new EncounterPolicy(); Acquire(p, Hunt(), true);
        p.Update(1, true, false, new[] { Hunt(), Fight() });
        Check(p.Update(1.3, true, false, new[] { Hunt(), Fight() }).Kind == EncounterKind.Combat);
    }
    private static void PassiveThreat()
    {
        var o = new EncounterObservation("troll", 16, 16, 0, .8f);
        Check(EncounterPolicy.Classify(o, false, false).Kind == EncounterKind.Threat);
        Check(Acquire(new EncounterPolicy(), o).Kind == EncounterKind.Threat);
        Check(EncounterPolicy.Classify(new EncounterObservation("tame", 8, 8, 1, 1, hostile: false, huntable: false), true, false).Kind == EncounterKind.None);
    }
    private static void ResourceSwing()
    {
        var o = new EncounterObservation("passive", 5, 5, 0, 0);
        Check(EncounterPolicy.Classify(o, false, true).Kind == EncounterKind.None);
        Check(EncounterPolicy.Classify(new EncounterObservation("hurt", 5, 5, 0, 0, damaged: true), false, true).Kind == EncounterKind.Combat);
    }
    private static void GroupEncounter()
    {
        var p = new EncounterPolicy(); var list = new[] { Fight("near", 3), Fight("engaged", 15, true) };
        p.Update(0, false, false, list); p.Update(.3, false, false, list);
        Check(p.Selected.Id == "engaged" && p.Ranked.Count == 2);
    }
    private static void StableOrdering()
    {
        var a = new EncounterPolicy(); var b = new EncounterPolicy();
        var one = new[] { Fight("b"), Fight("a") }; var two = new[] { Fight("a"), Fight("b") };
        a.Update(0, false, false, one); b.Update(0, false, false, two);
        Check(a.Update(.3, false, false, one).Id == b.Update(.3, false, false, two).Id);
    }
    private static void StableFocus()
    {
        var p = new EncounterPolicy(); Acquire(p, Fight("old"));
        var list = new[] { Fight("old", 9), Fight("new", 8) };
        p.Update(1, false, false, list); Check(p.Update(1.9, false, false, list).Id == "old");
    }
    private static void LossInvalidates()
    {
        var p = new EncounterPolicy(); Acquire(p, Fight());
        Check(p.Update(1, false, false, Array.Empty<EncounterObservation>()).Kind == EncounterKind.None);
        var blocked = new EncounterObservation("enemy", 8, 8, 0, 1, clearConnection: false, targetsGroup: true);
        Check(EncounterPolicy.Classify(blocked, true, true).Kind == EncounterKind.None);
    }
    private static void HuntTurn()
    {
        var p = new EncounterPolicy(); Acquire(p, Hunt(), true);
        Check(p.Update(1, true, false, new[] { Hunt(alignment: .5f) }).Kind == EncounterKind.None);
    }
    private static void ResetGaps()
    {
        var p = new EncounterPolicy(); Acquire(p, Hunt(), true);
        Check(p.Update(3, true, false, new[] { Hunt() }).Kind == EncounterKind.None);
        p.Update(3.8, true, false, new[] { Hunt() });
        Check(p.Update(1, true, false, new[] { Hunt() }).Kind == EncounterKind.None);
    }
    private static void InvalidEvidence()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1, 97 })
            Check(EncounterPolicy.Classify(new EncounterObservation("bad", bad, 1, 1, 1, targetsGroup: true), true, true).Kind == EncounterKind.None);
    }
    private static void Bounds()
    {
        bool rejected = false;
        try { new EncounterPolicy().Update(0, true, true, new EncounterObservation[65]); } catch (ArgumentException) { rejected = true; }
        Check(rejected);
    }
    private static void RetentionRange()
    {
        Check(EncounterPolicy.Classify(new EncounterObservation("old", 40, 40, 0, 0, retainedCombat: true), false, false).Kind == EncounterKind.None);
    }
}
