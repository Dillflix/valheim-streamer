using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CreatureCameraAdmissionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Physically supported but hidden wildlife never becomes an optional shot", HiddenWildlife);
        yield return ("A readable wildlife candidate wins even at the lowest editorial score", ReadableWildlife);
        yield return ("Creature readability loss immediately replaces or withdraws the candidate", ReadabilityLoss);
        yield return ("Readable wildlife still requires physical support", PhysicalSupport);
        yield return ("Wildlife admission leaves player recovery behavior unchanged", PlayerRecovery);
        yield return ("Native ranking cannot reinstate a wildlife view rejected by admission", NativeCannotOverride);
    }

    private static bool[] Admission(ShotSceneKind kind, bool[] supported, bool[] readable)
    {
        var result = new bool[supported.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = ShotCandidateRanking.Admissible(kind, supported[i], readable[i]);
        return result;
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static int Select(CinematicShotDirector director, double now, float[] scores, bool[] eligible,
        bool[] readable, IShotCandidateRanker? ranker = null) => director.Select(now, .1, scores, eligible,
            CinematicActivity.Idle, 0, false, 1, false, readable, ranker: ranker);

    private static void HiddenWildlife()
    {
        var stable = new ProceduralShotPolicy(); var cinematic = new CinematicShotDirector();
        var scores = new[] { 1f, .7f, .3f }; var health = new[] { false, false, false };
        var eligible = Admission(ShotSceneKind.Creature, new[] { true, true, true }, health);
        for (int i = 0; i < 600; i++)
        {
            Check(stable.Select(i * .1, scores, eligible, health) == -1, "Stable selection aired hidden wildlife.");
            Check(Select(cinematic, i * .1, scores, eligible, health) == -1, "Cinematic selection aired hidden wildlife.");
        }
    }
    private static void ReadableWildlife()
    {
        var scores = new[] { 1f, 0f }; var health = new[] { false, true };
        var eligible = Admission(ShotSceneKind.Creature, new[] { true, true }, health);
        Check(new ProceduralShotPolicy().Select(0, scores, eligible, health) == 1, "Stable selection preferred editorial quality over admission.");
        Check(Select(new CinematicShotDirector(), 0, scores, eligible, health) == 1, "Cinematic selection preferred editorial quality over admission.");
    }
    private static void ReadabilityLoss()
    {
        var stable = new ProceduralShotPolicy(); var cinematic = new CinematicShotDirector();
        var scores = new[] { 1f, .5f }; var supported = new[] { true, true }; var health = new[] { true, true };
        var eligible = Admission(ShotSceneKind.Creature, supported, health);
        Check(stable.Select(0, scores, eligible, health) == 0 && Select(cinematic, 0, scores, eligible, health) == 0,
            "Initial readable wildlife was not selected.");
        health[0] = false; eligible = Admission(ShotSceneKind.Creature, supported, health);
        Check(stable.Select(.01, scores, eligible, health) == 1 && Select(cinematic, .01, scores, eligible, health) == 1,
            "Dwell retained an unreadable creature pose.");
        health[1] = false; eligible = Admission(ShotSceneKind.Creature, supported, health);
        Check(stable.Select(.02, scores, eligible, health) == -1 && Select(cinematic, .02, scores, eligible, health) == -1,
            "A previously shown creature bypassed current readability.");
    }
    private static void PhysicalSupport()
    {
        var health = new[] { true, true }; var eligible = Admission(ShotSceneKind.Creature, new[] { false, false }, health);
        Check(new ProceduralShotPolicy().Select(0, new[] { 1f, 1f }, eligible, health) == -1,
            "A visually readable but unsupported wildlife camera was admitted.");
    }
    private static void PlayerRecovery()
    {
        var health = new[] { false, false }; var eligible = Admission(ShotSceneKind.Player, new[] { true, true }, health);
        Check(new ProceduralShotPolicy().Select(0, new[] { 1f, .5f }, eligible, health) == 0,
            "The optional wildlife gate changed ordinary player recovery.");
        Check(Select(new CinematicShotDirector(), 0, new[] { 1f, .5f }, eligible, health) == 0,
            "The optional wildlife gate changed cinematic player recovery.");
    }
    private sealed class InvalidRanker : IShotCandidateRanker
    {
        public int Best(IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
            IReadOnlyList<bool>? readable, int incumbent, IReadOnlyList<int>? priorities = null) => 0;
    }
    private static void NativeCannotOverride()
    {
        var health = new[] { false, true }; var eligible = Admission(ShotSceneKind.Creature, new[] { true, true }, health);
        try
        {
            Select(new CinematicShotDirector(), 0, new[] { 1f, .5f }, eligible, health, new InvalidRanker());
        }
        catch (InvalidOperationException) { return; }
        throw new Exception("External ranking bypassed creature admission.");
    }
}
