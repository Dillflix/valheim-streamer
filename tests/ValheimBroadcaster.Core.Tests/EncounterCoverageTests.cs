using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class EncounterCoverageTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Compact crowd overview admits more than three opponents", CompactCrowd);
        yield return ("Large cooperative group is not capped at three companions", CooperativeGroup);
        yield return ("Sparse encounter cannot drag every actor into a tiny picture", SparseCast);
        yield return ("Focused exchange and overview use different spatial budgets", DistinctPurposes);
        yield return ("Adaptive cast reserves player and relevant opponent", PrincipalPair);
        yield return ("Mixed encounter retains nearby allies and opponents", MixedCast);
        yield return ("Per-shot source mappings survive observation reordering", ReorderedCast);
        yield return ("Crowd coverage uses the shared threat-sector convention", SharedSectors);
        yield return ("Overview requires surrounding threat sectors to be represented", SurroundingCoverage);
        yield return ("Exchange purpose requires actual principal-opponent visibility", ExchangeCoverage);
        yield return ("Unfulfilled encounter cannot disable an available player picture", AvailableFallback);
        yield return ("Fulfilled encounter outranks purely cosmetic differences", PurposeRanking);
        yield return ("Purpose improvement retains stable selection dwell", StablePurposeChange);
        yield return ("Unreadable encounter cannot gain purpose priority", UnreadablePurpose);
        yield return ("Cast geometry rejects invalid frame evidence", InvalidCast);
        yield return ("Continuous attacks cannot indefinitely hide the opponent", BoundedProtection);
        yield return ("Established exchange is protected from overview novelty", ProtectUsefulAction);
        yield return ("Lost alternative clears protected-action improvement timing", LostAlternative);
        yield return ("New scene or opponent cannot inherit cutaway deadlines", ProtectionReset);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static EncounterCastMember Actor(string id, float x, float z, bool opponent = true, bool key = false) =>
        new EncounterCastMember(id, new AutonomyPosition(x, 1, z), .5f, 2, opponent, key);
    private static List<int> Cast(IReadOnlyList<EncounterCastMember> actors, EncounterShotPurpose purpose)
    { var cast = new List<int>(); EncounterCastPolicy.Select(actors, purpose, 0, 16f / 9f, false, cast); return cast; }
    private static List<EncounterCastMember> Crowd(bool opponents)
    {
        var actors = new List<EncounterCastMember> { Actor("player", 0, 0, false) };
        for (int i = 0; i < 10; i++) actors.Add(Actor("member" + i, (i % 5) - 2, 3 + i / 5, opponents, opponents && i == 0));
        return actors;
    }
    private static void CompactCrowd() => Check(Cast(Crowd(true), EncounterShotPurpose.Overview).Count == 11,
        "Compact opponents were discarded by an arbitrary three-actor cap.");
    private static void CooperativeGroup() => Check(Cast(Crowd(false), EncounterShotPurpose.Overview).Count == 11,
        "Compact companions were discarded despite fitting the same footprint.");
    private static void SparseCast()
    {
        var actors = new[] { Actor("player", 0, 0, false), Actor("key", 2, 0, key: true), Actor("remote", 80, 0) };
        var cast = Cast(actors, EncounterShotPurpose.Overview);
        Check(cast.SequenceEqual(new[] { 0, 1 }), "A distant optional subject dominated the framing envelope.");
    }
    private static void DistinctPurposes()
    {
        var actors = new[] { Actor("player", 0, 0, false), Actor("key", 3, 0, key: true), Actor("left", -12, 0), Actor("right", 12, 0) };
        Check(Cast(actors, EncounterShotPurpose.Overview).Count == 4 && Cast(actors, EncounterShotPurpose.Exchange).Count < 4,
            "Overview and focused exchange requested the same sprawling envelope.");
    }
    private static void PrincipalPair()
    {
        var actors = Crowd(true); var cast = Cast(actors, EncounterShotPurpose.Exchange);
        Check(cast[0] == 0 && cast[1] == 1 && cast.Distinct().Count() == cast.Count,
            "Adaptive selection lost the primary/key pair or duplicated members.");
    }
    private static void MixedCast()
    {
        var actors = Crowd(true); actors.Add(Actor("ally", -1, 1, false));
        var cast = Cast(actors, EncounterShotPurpose.Overview);
        Check(cast.Contains(11) && cast.Count(i => actors[i].Opponent) > 3, "Opponent quotas erased the cooperative scene.");
    }
    private static void ReorderedCast()
    {
        var actors = Crowd(true); var first = Cast(actors, EncounterShotPurpose.Exchange).Select(i => actors[i].Id).ToArray();
        actors.Reverse(1, actors.Count - 1);
        var second = Cast(actors, EncounterShotPurpose.Exchange).Select(i => actors[i].Id).ToArray();
        Check(first.SequenceEqual(second), "Observation insertion order changed selected identities.");
    }
    private static void SharedSectors()
    {
        var origin = new AutonomyPosition(0, 0, 0);
        for (int i = 0; i < 32; i++)
        {
            float x = (float)Math.Sin(i * Math.PI / 16), z = (float)Math.Cos(i * Math.PI / 16);
            Check(EncounterCastPolicy.Sector(origin, new AutonomyPosition(x, 0, z)) == 1 << EncounterSituationPolicy.Sector(x, z),
                "Coverage invented directions by combining different angular-sector conventions.");
        }
    }
    private static void SurroundingCoverage()
    {
        var partial = EncounterCoveragePolicy.Measure(EncounterShotPurpose.Overview, true, true, 8, 1, 85, 4, 1, 1);
        var broad = EncounterCoveragePolicy.Measure(EncounterShotPurpose.Overview, true, true, 8, 1, 85, 4, 1, 21);
        Check(!partial.Fulfilled && broad.Fulfilled, "Several enemies on one side falsely explained an encirclement.");
    }
    private static void ExchangeCoverage()
    {
        Check(!EncounterCoveragePolicy.Measure(EncounterShotPurpose.Exchange, true, false, 4, 0, 3, 3, 0, 3).Fulfilled &&
            EncounterCoveragePolicy.Measure(EncounterShotPurpose.Exchange, true, true, 4, 0, 3, 1, 0, 1).Fulfilled,
            "Background enemies substituted for the principal exchange partner.");
    }
    private static void AvailableFallback()
    {
        var coverage = EncounterCoveragePolicy.Measure(EncounterShotPurpose.Player, true, false, 4, 0, 3, 0, 0, 0);
        Check(coverage.Fulfilled && EncounterCoveragePolicy.Priority(true, coverage, EncounterShotPurpose.Overview, false) == 0,
            "Missing enemy overview suppressed ordinary available footage.");
    }
    private static void PurposeRanking()
    {
        var exchange = EncounterCoveragePolicy.Measure(EncounterShotPurpose.Exchange, true, true, 1, 0, 1, 1, 0, 1);
        var player = EncounterCoveragePolicy.Measure(EncounterShotPurpose.Player, true, false, 1, 0, 1, 0, 0, 0);
        int explained = EncounterCoveragePolicy.Priority(true, exchange, EncounterShotPurpose.Exchange, true);
        int pretty = EncounterCoveragePolicy.Priority(true, player, EncounterShotPurpose.Exchange, true);
        Check(explained > pretty, "Cosmetic score defeated fulfilled encounter purpose.");
    }
    private static void StablePurposeChange()
    {
        var selection = new ProceduralShotPolicy(); var eligible = new[] { true, true }; var readable = new[] { true, true };
        selection.Select(0, new[] { .9f, .3f }, eligible, readable);
        int result = 0;
        for (int i = 1; i < 20; i++) result = selection.Select(i * .1, new[] { .25f, .85f }, eligible, readable);
        Check(result == 0, "An editorial purpose improvement bypassed readable-shot minimum dwell.");
        Check(selection.Select(2.1, new[] { .25f, .85f }, eligible, readable) == 1, "Sustained fulfilled alternative never became eligible.");
    }
    private static void UnreadablePurpose()
    {
        var claimed = new EncounterCoverage(EncounterShotPurpose.Overview, true, 6, 2, 4, "test");
        Check(EncounterCoveragePolicy.Priority(false, claimed, EncounterShotPurpose.Overview, true) == 0,
            "Encounter completeness promoted unreadable primary footage.");
    }
    private static void InvalidCast()
    {
        bool invalid = false;
        try { EncounterCastPolicy.Select(Crowd(true), EncounterShotPurpose.Overview, 0, float.NaN, false, new List<int>()); }
        catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "Invalid screen geometry entered cast planning.");
    }
    private static void BoundedProtection()
    {
        var policy = new EncounterPurposeProtection();
        for (int i = 0; i < 30; i++)
            Check(!policy.Allow(i * .1, 1, "foe", true, true, false, true), "Action beat was interrupted immediately.");
        Check(policy.Allow(3.1, 1, "foe", true, true, false, true), "Continuous action starved a stable explanatory alternative.");
    }
    private static void ProtectUsefulAction()
    {
        var policy = new EncounterPurposeProtection();
        for (int i = 0; i < 100; i++)
            Check(!policy.Allow(i * .1, 1, "foe", true, true, true, true), "A useful exchange was interrupted for editorial novelty.");
    }
    private static void LostAlternative()
    {
        var policy = new EncounterPurposeProtection();
        for (int i = 0; i <= 25; i++) policy.Allow(i * .1, 1, "foe", true, true, false, true);
        policy.Allow(2.6, 1, "foe", true, true, false, false);
        Check(!policy.Allow(2.7, 1, "foe", true, true, false, true) &&
            !policy.Allow(3.2, 1, "foe", true, true, false, true), "A vanished alternative left an inherited immediate-cut deadline.");
    }
    private static void ProtectionReset()
    {
        var policy = new EncounterPurposeProtection();
        for (int i = 0; i <= 31; i++) policy.Allow(i * .1, 1, "foe", true, true, false, true);
        Check(!policy.Allow(3.2, 2, "foe", true, true, false, true) &&
            !policy.Allow(3.3, 2, "other", true, true, false, true) &&
            policy.Allow(3.4, 2, "other", true, false, false, true), "Protection survived scene/target loss or prevented readability recovery.");
    }
}
