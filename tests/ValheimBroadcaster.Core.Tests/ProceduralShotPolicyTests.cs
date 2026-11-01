using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ProceduralShotPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Procedural views retain stable ties and minor score changes", StableView);
        yield return ("Procedural switching requires persistent material improvement", PersistentImprovement);
        yield return ("Healthy shot dwell prevents quick reverse cuts", HealthyDwell);
        yield return ("Bad foliage composition can escape before healthy dwell expires", PoorView);
        yield return ("Physical invalidation bypasses camera hysteresis immediately", UnsafeView);
        yield return ("Foliage scores alone never declare all camera geometry unavailable", SoftOcclusion);
        yield return ("Camera candidate reset and evidence gaps discard stale preference", ResetAndGap);
        yield return ("Procedural camera policy rejects invalid clocks and candidate scores", InvalidInputs);
    }

    private static void Equal(int expected, int actual)
    { if (expected != actual) throw new Exception($"Expected candidate {expected}, got {actual}."); }
    private static readonly bool[] Both = { true, true };

    private static void StableView()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .8f }, Both));
        for (int i = 1; i <= 1200; i++)
            Equal(0, policy.Select(i * .05, new[] { .8f, i % 2 == 0 ? .82f : .79f }, Both));
        Equal(-1, policy.PendingIndex);
    }

    private static void PersistentImprovement()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        Equal(0, policy.Select(2, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(2.3, new[] { .5f, .9f }, Both));
        // The improvement disappears briefly, so the old timer must not survive.
        Equal(0, policy.Select(2.4, new[] { .8f, .81f }, Both));
        Equal(0, policy.Select(2.5, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(2.9, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(3.01, new[] { .5f, .9f }, Both));
    }

    private static void HealthyDwell()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        Equal(0, policy.Select(.1, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(.7, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(1.4, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(2.01, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(2.1, new[] { .9f, .5f }, Both));
        Equal(1, policy.Select(2.7, new[] { .9f, .5f }, Both));
        Equal(1, policy.Select(3.4, new[] { .9f, .5f }, Both));
        Equal(0, policy.Select(4.02, new[] { .9f, .5f }, Both));
    }

    private static void PoorView()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        Equal(0, policy.Select(.1, new[] { .1f, .7f }, Both));
        Equal(1, policy.Select(.61, new[] { .1f, .7f }, Both));
    }

    private static void UnsafeView()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .9f, .4f }, Both));
        Equal(1, policy.Select(.01, new[] { .9f, .4f }, new[] { false, true }));
        Equal(-1, policy.Select(.02, new[] { .9f, .4f }, new[] { false, false }));
        Equal(0, policy.Select(.03, new[] { .3f, .2f }, Both));
    }

    private static void SoftOcclusion()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { 0f, 0f }, Both));
        Equal(0, policy.Select(.1, new[] { 0f, 0f }, Both));
        Equal(0, policy.Select(.2, new[] { 0f, .8f }, Both));
        Equal(1, policy.Select(.71, new[] { 0f, .8f }, Both));
        Equal(1, policy.Select(.8, new[] { 0f, 0f }, Both));
    }

    private static void ResetAndGap()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        Equal(0, policy.Select(2, new[] { .1f, .9f }, Both));
        // No observations during a long stall cannot prove persistent occlusion.
        Equal(0, policy.Select(10, new[] { .1f, .9f }, Both));
        Equal(1, policy.Select(10.51, new[] { .1f, .9f }, Both));
        policy.Reset();
        Equal(-1, policy.SelectedIndex);
        Equal(0, policy.Select(0, new[] { .8f, .8f }, Both));
        Equal(2, policy.Select(.1, new[] { .3f, .4f, .9f }, new[] { true, true, true }));
        Equal(-1, policy.Select(.2, Array.Empty<float>(), Array.Empty<bool>()));
    }

    private static void InvalidInputs()
    {
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, .5 })
        {
            var policy = new ProceduralShotPolicy();
            policy.Select(1, new[] { .8f, .5f }, Both);
            Throws(() => policy.Select(bad, new[] { .8f, .5f }, Both));
            Equal(-1, policy.SelectedIndex);
        }
        foreach (float bad in new[] { -.01f, 1.01f, float.NaN, float.PositiveInfinity })
            Throws(() => new ProceduralShotPolicy().Select(0, new[] { bad, .5f }, Both));
        Throws(() => new ProceduralShotPolicy().Select(0, new[] { .5f }, Both));
        Throws(() => new ProceduralShotPolicy().Select(0, new float[33], new bool[33]));
    }
    private static void Throws(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Expected invalid camera policy input to be rejected.");
    }
}
