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
        yield return ("A flickering challenger never accumulates a clear streak against a readable shot", FlickeringChallenger);
        yield return ("A challenger restoring readability needs persistence and a sustained clear streak", SustainedClearStreak);
        yield return ("An unreadable eligible shot is retained through its repair window", RepairWindow);
        yield return ("Purpose promotion is denied before the minimum shot age and allowed after", PromotionNeedsAge);
        yield return ("A readable live selection is never displaced by a stronger readable challenger", ReadableLiveRetained);
        yield return ("Purpose promotion cannot displace a readable live selection", PromotionCannotDisplaceReadable);
        yield return ("Picture reconsideration cannot displace a readable live selection", ReconsiderCannotDisplaceReadable);
        yield return ("Recovery still replaces the live shot once its picture is lost", RecoveryAfterReadableRetention);
        yield return ("Callers without readability evidence keep legacy improvement switching", LegacyImprovementWithoutReadability);
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
        Equal(0, policy.Select(5, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(5.3, new[] { .5f, .9f }, Both));
        // The improvement disappears briefly, so the old timer must not survive.
        Equal(0, policy.Select(5.4, new[] { .8f, .81f }, Both));
        Equal(0, policy.Select(5.5, new[] { .5f, .9f }, Both));
        Equal(0, policy.Select(5.9, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(6.01, new[] { .5f, .9f }, Both));
    }

    private static void HealthyDwell()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        for (double now = .1; now < 5; now += .7)
            Equal(0, policy.Select(now, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(5.01, new[] { .5f, .9f }, Both));
        for (double now = 5.1; now < 10.01; now += .7)
            Equal(1, policy.Select(now, new[] { .9f, .5f }, Both));
        Equal(0, policy.Select(10.03, new[] { .9f, .5f }, Both));
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
    private static void FlickeringChallenger()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .9f, .5f }, Both, Both));
        // The challenger's readability alternates every tick, so its clear
        // streak restarts forever and the readable shot is never replaced.
        for (int i = 1; i <= 120; i++)
            Equal(0, policy.Select(i * .1, new[] { .6f, .9f }, Both, new[] { true, i % 2 == 0 }));
    }

    private static void SustainedClearStreak()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .6f, .9f }, Both, new[] { true, false }));
        // The live picture is lost while every alternative is hidden too. When
        // the challenger finally clears after the repair window, persistence
        // alone is not enough: its readable streak must also mature first.
        var lost = new[] { false, false };
        Equal(0, policy.Select(.2, new[] { .6f, .9f }, Both, lost));
        Equal(0, policy.Select(1.0, new[] { .6f, .9f }, Both, lost));
        var restoring = new[] { false, true };
        Equal(0, policy.Select(1.72, new[] { .6f, .9f }, Both, restoring));
        Equal(0, policy.Select(2.0, new[] { .6f, .9f }, Both, restoring));
        Equal(1, policy.Select(2.08, new[] { .6f, .9f }, Both, restoring));
    }

    private static void RepairWindow()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .9f, .6f }, Both, Both));
        // The live shot loses readability but stays eligible: the adapter
        // keeps it through the repair window even though the challenger has
        // already proven persistence and a sufficient clear streak.
        var lost = new[] { false, true };
        Equal(0, policy.Select(.5, new[] { .9f, .6f }, Both, lost));
        Equal(0, policy.Select(1.0, new[] { .9f, .6f }, Both, lost));
        Equal(0, policy.Select(1.5, new[] { .9f, .6f }, Both, lost));
        Equal(0, policy.Select(1.99, new[] { .9f, .6f }, Both, lost));
        Equal(1, policy.Select(2.01, new[] { .9f, .6f }, Both, lost));
    }

    private static void PromotionNeedsAge()
    {
        // Promotion can only act where auditions are permitted at all: callers
        // without readability evidence. A readable live selection is covered by
        // PromotionCannotDisplaceReadable instead.
        var policy = new ProceduralShotPolicy();
        int[] priorities = { 0, 1 };
        Equal(0, policy.Select(0, new[] { .9f, .5f }, Both));
        // Before the minimum shot age the promotion cannot bypass the score
        // margin, so no pending comparison may even be established.
        for (double now = .1; now < 2; now += .5)
        {
            Equal(0, policy.Select(now, new[] { .9f, .5f }, Both, priorities: priorities));
            Equal(-1, policy.PendingIndex);
        }
        Equal(0, policy.Select(2.0, new[] { .9f, .5f }, Both, priorities: priorities));
        Equal(1, policy.PendingIndex);
        for (double now = 2.5; now < 5; now += .5)
            Equal(0, policy.Select(now, new[] { .9f, .5f }, Both, priorities: priorities));
        Equal(1, policy.Select(5.01, new[] { .9f, .5f }, Both, priorities: priorities));
    }

    private static void ReadableLiveRetained()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .9f, .6f }, Both, Both));
        // The challenger is readable, beats every margin, outlives persistence,
        // the clear-streak requirement and healthy dwell — and still never
        // replaces a readable live shot. No audition may even be pended.
        for (int i = 1; i <= 200; i++)
        {
            Equal(0, policy.Select(i * .1, new[] { .5f, .95f }, Both, Both));
            Equal(-1, policy.PendingIndex);
        }
    }

    private static void PromotionCannotDisplaceReadable()
    {
        var policy = new ProceduralShotPolicy();
        int[] priorities = { 0, 2 };
        Equal(0, policy.Select(0, new[] { .9f, .5f }, Both, Both));
        // Far beyond the minimum shot age and healthy dwell, a higher editorial
        // purpose still cannot start an audition against a readable picture.
        for (int i = 1; i <= 100; i++)
        {
            Equal(0, policy.Select(i * .1, new[] { .9f, .5f }, Both, Both, priorities: priorities));
            Equal(-1, policy.PendingIndex);
        }
    }

    private static void ReconsiderCannotDisplaceReadable()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .8f }, Both, Both));
        // A persistent pixel concern lowers the margin and penalizes the live
        // score, but a readable picture is not an impaired one: no switch.
        for (int i = 1; i <= 100; i++)
        {
            Equal(0, policy.Select(i * .1, new[] { .58f, .8f }, Both, Both, reconsiderPicture: true));
            Equal(-1, policy.PendingIndex);
        }
    }

    private static void RecoveryAfterReadableRetention()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .9f, .6f }, Both, Both));
        // A long-standing stronger challenger is ignored while the live picture
        // stays readable...
        for (int i = 1; i <= 100; i++)
            Equal(0, policy.Select(i * .1, new[] { .6f, .9f }, Both, Both));
        // ...but once readability is lost the unchanged recovery pipeline runs:
        // the repair window protects the live camera, then the streak-qualified
        // readable challenger takes over.
        var lost = new[] { false, true };
        Equal(0, policy.Select(10.1, new[] { .6f, .9f }, Both, lost));
        Equal(0, policy.Select(11.0, new[] { .6f, .9f }, Both, lost));
        Equal(0, policy.Select(11.5, new[] { .6f, .9f }, Both, lost));
        Equal(1, policy.Select(11.61, new[] { .6f, .9f }, Both, lost));
    }

    private static void LegacyImprovementWithoutReadability()
    {
        // Without readability evidence the policy cannot assert the live shot
        // is fine, so scenic-style callers keep the legacy improvement path.
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .5f }, Both));
        for (double now = .1; now < 5; now += .4)
            Equal(0, policy.Select(now, new[] { .5f, .9f }, Both));
        Equal(1, policy.Select(5.01, new[] { .5f, .9f }, Both));
    }

    private static void Throws(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Expected invalid camera policy input to be rejected.");
    }
}
