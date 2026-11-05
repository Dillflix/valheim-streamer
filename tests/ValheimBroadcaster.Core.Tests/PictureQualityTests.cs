using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class PictureQualityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Actor health rejects foreground foliage despite clear body sightlines", Foreground);
        yield return ("Actor health requires primary visibility independently of group score", Primary);
        yield return ("Actor and scenic picture health share foreground and survey requirements", SharedHealth);
        yield return ("Picture health rejects unknown and nonfinite evidence", InvalidEvidence);
        yield return ("Readable recovery escapes an obscured .57 shot for a .66 alternative", SmallReadableImprovement);
        yield return ("Readable camera outranks an unreadable higher editorial score", ReadabilityFirst);
        yield return ("Brief foliage occlusion cannot trigger recovery cuts", BriefOcclusion);
        yield return ("Readable recovery requires fresh sustained readable evidence", RecoveryFreshness);
        yield return ("All-bad recovery advances only on persistent material improvement", AllBadProgress);
        yield return ("Poor pictures never fabricate missing physical camera geometry", PhysicalSeparation);
        yield return ("Picture quality mask validates bounded matching input", MaskValidation);
        yield return ("Changing recovery geometry invalidates comparison without losing live camera", GeometryInvalidation);
        yield return ("Rejected camera proposal restores actual source selection and original dwell", RejectedProposal);
        yield return ("Camera proposal rollback is limited to the immediately preceding selection", BoundedRollback);
        yield return ("Same-tick camera proposal chain rolls back to the actual displayed source", SameTickRollback);
        yield return ("Mode reset can adopt a revalidated displayed camera with fresh bounded dwell", AdoptAfterReset);
    }

    private static readonly bool[] Both = { true, true };
    private static void Check(bool value) { if (!value) throw new Exception("Picture quality assertion failed."); }
    private static void Equal(int expected, int actual)
    { if (expected != actual) throw new Exception($"Expected camera {expected}, got {actual}."); }
    private static PictureHealth Actor(float foreground = 0, float leaves = 0, float solid = 0,
        float visibility = 1, float readability = 1, bool complete = true) =>
        PictureQuality.Actor(true, true, visibility, readability, solid, leaves, foreground, complete);

    private static void Foreground()
    {
        Check(Actor().Readable);
        PictureHealth bad = Actor(foreground: .9f);
        Check(!bad.Readable && bad.Reason.Contains("Foreground"));
        Check(Actor(foreground: .2f).Readable); // Peripheral framing foliage is allowed.
    }
    private static void Primary()
    {
        Check(!Actor(leaves: .6f).Readable);
        Check(!Actor(solid: .4f).Readable);
        Check(!Actor(visibility: .5f).Readable);
        Check(!Actor(readability: .6f).Readable);
        Check(Actor(leaves: .2f, solid: .1f, visibility: .8f).Readable);
    }
    private static void SharedHealth()
    {
        Check(PictureQuality.Scenic(true, true, .8f, .1f, true).Readable);
        Check(!PictureQuality.Scenic(true, true, .9f, .8f, true).Readable);
        Check(!PictureQuality.Scenic(true, true, .9f, 0, false).Readable);
        Check(!Actor(complete: false).Readable);
        Check(!PictureQuality.Scenic(true, false, .9f, 0, true).Readable);
        Check(!PictureQuality.Scenic(true, true, .4f, 0, true).Readable);
    }
    private static void InvalidEvidence()
    {
        Check(!Actor(visibility: float.NaN).Readable);
        Check(!Actor(foreground: float.PositiveInfinity).Readable);
        Check(!Actor(leaves: -1).Readable);
        Check(!PictureQuality.Scenic(true, true, 2, 0, true).Readable);
        Check(!PictureQuality.Actor(false, true, 1, 1, 0, 0, 0, true).Readable);
    }
    private static void SmallReadableImprovement()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .6f }, Both, Both));
        Equal(0, policy.Select(.1, new[] { .57f, .66f }, Both, new[] { false, true }));
        Equal(0, policy.Select(.3, new[] { .57f, .66f }, Both, new[] { false, true }));
        // The live camera keeps its in-place repair window before recovery.
        Equal(0, policy.Select(.46, new[] { .57f, .66f }, Both, new[] { false, true }));
        Equal(0, policy.Select(1.4, new[] { .57f, .66f }, Both, new[] { false, true }));
        Equal(1, policy.Select(1.61, new[] { .57f, .66f }, Both, new[] { false, true }));
    }
    private static void ReadabilityFirst()
    {
        var policy = new ProceduralShotPolicy();
        Equal(1, policy.Select(0, new[] { .95f, .6f }, Both, new[] { false, true }));
        for (int i = 1; i <= 100; i++)
            Equal(1, policy.Select(i * .2, new[] { .99f, .6f }, Both, new[] { false, true }));
        Equal(1, policy.Select(20.1, new[] { .55f, .8f }, Both, new[] { true, false }));
        Equal(1, policy.Select(20.46, new[] { .55f, .8f }, Both, new[] { true, false }));
        Equal(1, policy.Select(21.4, new[] { .55f, .8f }, Both, new[] { true, false }));
        Equal(0, policy.Select(21.61, new[] { .55f, .8f }, Both, new[] { true, false }));
    }
    private static void BriefOcclusion()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .7f }, Both, Both));
        for (int i = 1; i <= 20; i++)
        {
            Equal(0, policy.Select(i * .5, new[] { .5f, .7f }, Both, new[] { false, true }));
            Equal(0, policy.Select(i * .5 + .2, new[] { .8f, .7f }, Both, Both));
        }
    }
    private static void RecoveryFreshness()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .6f }, Both, Both));
        Equal(0, policy.Select(.1, new[] { .4f, .5f }, Both, new[] { false, false }));
        // Evidence that it was a less-bad view does not prove readable recovery.
        Equal(0, policy.Select(.7, new[] { .4f, .5f }, Both, new[] { false, true }));
        Equal(0, policy.Select(.9, new[] { .4f, .5f }, Both, new[] { false, true }));
        // A long stall invalidates the comparison timer as well.
        Equal(0, policy.Select(4, new[] { .4f, .5f }, Both, new[] { false, true }));
        Equal(0, policy.Select(4.36, new[] { .4f, .5f }, Both, new[] { false, true }));
        Equal(0, policy.Select(5.3, new[] { .4f, .5f }, Both, new[] { false, true }));
        Equal(1, policy.Select(5.51, new[] { .4f, .5f }, Both, new[] { false, true }));
    }
    private static void AllBadProgress()
    {
        var policy = new ProceduralShotPolicy();
        bool[] bad = { false, false };
        Equal(0, policy.Select(0, new[] { .45f, .4f }, Both, bad));
        Equal(0, policy.Select(.1, new[] { .4f, .48f }, Both, bad));
        Equal(0, policy.Select(.6, new[] { .4f, .48f }, Both, bad));
        // Even an all-bad search honors the live camera's repair window.
        Equal(0, policy.Select(.86, new[] { .4f, .48f }, Both, bad));
        Equal(0, policy.Select(1.5, new[] { .4f, .48f }, Both, bad));
        Equal(1, policy.Select(1.61, new[] { .4f, .48f }, Both, bad));
        for (int i = 1; i <= 100; i++)
            Equal(1, policy.Select(2 + i * .2, new[] { .5f, .48f }, Both, bad));
    }
    private static void PhysicalSeparation()
    {
        var policy = new ProceduralShotPolicy();
        bool[] bad = { false, false };
        Equal(0, policy.Select(0, new[] { 0f, 0f }, Both, bad));
        Equal(0, policy.Select(.1, new[] { 0f, 0f }, Both, bad));
        Equal(1, policy.Select(.11, new[] { .8f, 0f }, new[] { false, true }, bad));
        Equal(-1, policy.Select(.12, new[] { .8f, .8f }, bad, Both));
    }
    private static void MaskValidation()
    {
        bool threw = false;
        try { new ProceduralShotPolicy().Select(0, new[] { .8f, .8f }, Both, new[] { true }); }
        catch (ArgumentException) { threw = true; }
        Check(threw);
    }
    private static void GeometryInvalidation()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .6f }, Both, Both));
        Equal(0, policy.Select(.1, new[] { .4f, .6f }, Both, new[] { false, true }));
        policy.InvalidatePending();
        Equal(0, policy.SelectedIndex);
        Equal(-1, policy.PendingIndex);
        Equal(0, policy.Select(.4, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(.6, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(.76, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(1.5, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(1, policy.Select(1.61, new[] { .4f, .7f }, Both, new[] { false, true }));
    }
    private static void RejectedProposal()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .6f }, Both, Both));
        for (double now = 1; now < 4.4; now += .9)
            Equal(0, policy.Select(now, new[] { .8f, .6f }, Both, Both));
        Equal(0, policy.Select(4.5, new[] { .5f, .9f }, Both, Both));
        Equal(1, policy.Select(5.1, new[] { .5f, .9f }, Both, Both));
        Check(policy.RejectSelection(0));
        Equal(0, policy.SelectedIndex);
        Equal(-1, policy.PendingIndex);
        // A genuinely clear replacement still needs new confirmation, but
        // retaining the already-viewed source does not restart its dwell.
        Equal(0, policy.Select(5.2, new[] { .5f, .9f }, Both, Both));
        Equal(1, policy.Select(5.8, new[] { .5f, .9f }, Both, Both));
    }
    private static void BoundedRollback()
    {
        var policy = new ProceduralShotPolicy();
        Equal(0, policy.Select(0, new[] { .8f, .6f }, Both, Both));
        Check(!policy.RejectSelection(1));
        Equal(0, policy.Select(.1, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(1.0, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(1, policy.Select(1.61, new[] { .4f, .7f }, Both, new[] { false, true }));
        Check(!policy.RejectSelection(1));
        Check(policy.RejectSelection(0));
        Check(!policy.RejectSelection(0));
        // The rollback restarts the retained camera's repair window.
        Equal(0, policy.Select(1.7, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(2.6, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(0, policy.Select(3.1, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(1, policy.Select(3.21, new[] { .4f, .7f }, Both, new[] { false, true }));
        Equal(1, policy.Select(3.3, new[] { .4f, .7f }, Both, new[] { false, true }));
        Check(!policy.RejectSelection(0));
    }
    private static void SameTickRollback()
    {
        var policy = new ProceduralShotPolicy();
        bool[] all = { true, true, true };
        Equal(0, policy.Select(0, new[] { .9f, .5f, .6f }, all, all));
        Equal(1, policy.Select(1, new[] { .9f, .8f, .9f }, new[] { false, true, false }, all));
        Equal(2, policy.Select(1, new[] { .9f, .8f, .9f }, new[] { false, false, true }, all));
        Check(policy.RejectSelection(0));
        Equal(0, policy.SelectedIndex);
    }
    private static void AdoptAfterReset()
    {
        var policy = new ProceduralShotPolicy();
        Check(!policy.AdoptValidatedSelection(0, 0));
        Equal(1, policy.Select(1, new[] { .6f, .9f }, Both, Both));
        Check(!policy.RejectSelection(0));
        Check(policy.AdoptValidatedSelection(1, 0));
        Equal(0, policy.SelectedIndex);
        Check(!policy.AdoptValidatedSelection(.9, 1));
        Check(!policy.AdoptValidatedSelection(double.NaN, 1));
        Check(!policy.AdoptValidatedSelection(1, 2));
        Equal(0, policy.Select(1.1, new[] { .6f, .9f }, Both, Both));
        Equal(0, policy.Select(1.7, new[] { .6f, .9f }, Both, Both));
        Equal(0, policy.Select(2.3, new[] { .6f, .9f }, Both, Both));
        for (double now = 3; now < 6; now += .9)
            Equal(0, policy.Select(now, new[] { .6f, .9f }, Both, Both));
        Equal(1, policy.Select(6.01, new[] { .6f, .9f }, Both, Both));
    }
}
