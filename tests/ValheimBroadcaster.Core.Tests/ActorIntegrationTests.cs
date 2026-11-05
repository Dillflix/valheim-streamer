using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ActorIntegrationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Encounter quality improvements keep raw-score hysteresis inside each purpose", PurposeQuality);
        yield return ("Purpose promotion preserves healthy dwell despite a lower numeric score", PromotionDwell);
        yield return ("Readable recovery outranks a higher encounter purpose without healthy dwell", ReadableBeforePurpose);
        yield return ("Native ordinal encoding matches readable purpose quality ordering", OrdinalParity);
        yield return ("Background hostile presence does not replace qualified work or travel", PresenceGrammar);
        yield return ("Developing and engaged encounters retain deliberate threat and action grammar", EscalatedGrammar);
        yield return ("Presence permits observation while developing pressure does not", PresenceObservation);
        yield return ("Established background presence yields to player activity", PresenceCredit);
        yield return ("Held encounter footage cannot establish context", UnshownCredit);
        yield return ("New encounter principal must earn its own overview", PrincipalCredit);
        yield return ("Reinforcements in the same sectors invalidate old overview credit", TopologyCredit);
        yield return ("Minor threat sector jitter does not repeatedly restart overview", SectorJitter);
        yield return ("Escalation earns fresh encounter context without breaking a protected action", ProtectedEscalation);
        yield return ("Verified image rows measure the actual projected subject instead of its mirror", VerifiedRoi);
        yield return ("Persistent pixel concern can cause an actual quality correction with bounded cooldown", PixelCorrection);
        yield return ("Pixel concern cannot invent eligibility for an unrendered alternative", PixelSafety);
    }
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static readonly bool[] All = { true, true };
    private static void PurposeQuality()
    {
        var p = new ProceduralShotPolicy(); int[] priority = { 2, 2 };
        p.Select(0, new[] { .55f, .3f }, All, All, priorities: priority);
        for (int i = 1; i < 50; i++) p.Select(i * .1, new[] { .55f, .90f }, All, All, priorities: priority);
        Check(p.Select(5.01, new[] { .55f, .90f }, All, All, priorities: priority) == 1,
            "Purpose scaling compressed a .35 quality improvement below the .15 margin.");
    }
    private static void PromotionDwell()
    {
        var p = new ProceduralShotPolicy(); p.Select(0, new[] { .95f, .4f }, All, All);
        for (int i = 1; i < 50; i++) Check(p.Select(i * .1, new[] { .95f, .4f }, All, All, priorities: new[] { 0, 2 }) == 0, "Promotion skipped dwell.");
        Check(p.Select(5.01, new[] { .95f, .4f }, All, All, priorities: new[] { 0, 2 }) == 1, "Raw-score loss vetoed established purpose promotion.");
    }
    private static void ReadableBeforePurpose()
    {
        var p = new ProceduralShotPolicy(); int[] priority = { 2, 0 };
        p.Select(0, new[] { .95f, .4f }, All, All, priorities: priority);
        p.Select(.1, new[] { .95f, .4f }, All, new[] { false, true }, priorities: priority);
        // Readable recovery still grants the live camera its repair window.
        Check(p.Select(.46, new[] { .95f, .4f }, All, new[] { false, true }, priorities: priority) == 0, "Recovery ignored the in-place repair window.");
        Check(p.Select(1.4, new[] { .95f, .4f }, All, new[] { false, true }, priorities: priority) == 0, "Recovery ignored the in-place repair window.");
        Check(p.Select(1.61, new[] { .95f, .4f }, All, new[] { false, true }, priorities: priority) == 1, "Unreadable encounter blocked readable recovery.");
    }
    private static void OrdinalParity()
    {
        foreach (bool a in new[] { false, true }) foreach (bool b in new[] { false, true })
        for (int p = 0; p < 3; p++) for (int q = 0; q < 3; q++)
        {
            float[] scores = { .95f, .4f }; int[] priorities = { p, q }; bool[] readable = { a, b };
            int best = ShotCandidateRanking.Best(scores, All, readable, -1, priorities: priorities);
            int encoded = ShotCandidateRanking.NativeQuality(scores[1], b, q) > ShotCandidateRanking.NativeQuality(scores[0], a, p) ? 1 : 0;
            Check(best == encoded, "Native scalar encoding disagrees with lexicographic policy.");
        }
        Check(ShotCandidateRanking.Best(new[] { .9f, .4f }, All, null, -1, priorities: new[] { 0, 2 }) == 1, "Missing mask lost purpose priority.");
    }
    private static ShotSituation Situation(CinematicActivity activity, EncounterPressure pressure, bool interest = false) => new ShotSituation(
        ShotSceneKind.Player, activity, .9f, 0, 2, interest, ShotEnvironment.Open, activity == CinematicActivity.Travel ? ShotLocomotion.Run : ShotLocomotion.Stationary,
        1, activity == CinematicActivity.Combat ? EncounterKind.Combat : EncounterKind.Threat, .8f, 15, pressure);
    private static void PresenceGrammar()
    {
        var travel = Situation(CinematicActivity.Travel, EncounterPressure.Presence);
        var work = Situation(CinematicActivity.Work, EncounterPressure.Presence, true);
        Check(ShotIntentPolicy.ResolveRole(travel, ShotRole.Tracking) == ShotRole.Tracking, "Presence suppressed travel.");
        Check(ShotIntentPolicy.ResolveRole(work, ShotRole.Detail) == ShotRole.Detail, "Presence suppressed work.");
        var sequence = new ShotSequencePolicy();
        for (int i = 0; i < 90; i++) sequence.Update(i * .1, .1, travel, true, true);
        Check(sequence.Cue.Role == ShotRole.Tracking && sequence.Cue.MotionMultiplier > .15f, "Presence became a perpetual threat sequence.");
    }
    private static void EscalatedGrammar()
    {
        var p = new ShotIntentPolicy();
        for (int i = 0; i < 120; i++) p.Update(i * .1, .1, Situation(CinematicActivity.Travel, EncounterPressure.Developing), true);
        Check(p.Intent.Role == ShotRole.Interaction && p.Intent.MotionScale < .2f, "Developing pressure lost restrained relationship coverage.");
        Check(ShotIntentPolicy.ResolveRole(Situation(CinematicActivity.Combat, EncounterPressure.Engaged), ShotRole.Tracking) == ShotRole.Action, "Combat lost action protection.");
    }
    private static void PresenceObservation()
    {
        Check(ObservationalShotPolicy.CanUse(Situation(CinematicActivity.Travel, EncounterPressure.Presence), ShotRole.Tracking, ShotScale.Full, false), "Ordinary context disabled observational family.");
        Check(!ObservationalShotPolicy.CanUse(Situation(CinematicActivity.Travel, EncounterPressure.Developing), ShotRole.Tracking, ShotScale.Full, false), "Threat escalation kept observational travel enabled.");
    }
    private static EncounterShotPurpose Step(EncounterEpisodeCoverage p, double now, string target = "a", int hostiles = 4, int sectors = 3,
        EncounterPressure pressure = EncounterPressure.Presence, bool shown = true, bool protect = false) => p.Update(now, 1, target, true, pressure,
            hostiles, 1, sectors, 20, false, protect, shown, target, new EncounterCoverage(EncounterShotPurpose.Overview, true, hostiles, 0, 2, "measured"));
    private static void Establish(EncounterEpisodeCoverage p) { for (int i = 0; i <= 35; i++) Step(p, i * .1); }
    private static void PresenceCredit()
    { var p = new EncounterEpisodeCoverage(); Establish(p); Check(p.ContextEstablished && Step(p, 3.6) == EncounterShotPurpose.Player, "Established presence monopolized programme."); }
    private static void UnshownCredit()
    { var p = new EncounterEpisodeCoverage(); for (int i = 0; i < 100; i++) Step(p, i * .1, shown: false); Check(!p.ContextEstablished, "Unshown candidates fulfilled context."); }
    private static void PrincipalCredit()
    {
        var p = new EncounterEpisodeCoverage(); Establish(p);
        for (int i = 36; i < 49; i++) Step(p, i * .1, "b");
        Check(!p.OverviewEstablished && Step(p, 4.9, "b") == EncounterShotPurpose.Overview, "New principal borrowed old overview credit.");
    }
    private static void TopologyCredit()
    {
        var p = new EncounterEpisodeCoverage(); Establish(p);
        for (int i = 36; i < 49; i++) Step(p, i * .1, hostiles: 8);
        Check(!p.OverviewEstablished && Step(p, 4.9, hostiles: 8) == EncounterShotPurpose.Overview, "Same-sector reinforcements borrowed old coverage.");
    }
    private static void SectorJitter()
    {
        var p = new EncounterEpisodeCoverage(); Establish(p); long revision = p.Revision;
        for (int i = 36; i < 120; i++) Step(p, i * .1, sectors: i % 2 == 0 ? 3 : 6);
        Check(p.OverviewEstablished && p.Revision == revision, "Small sector changes repeatedly reset the scene.");
    }
    private static void ProtectedEscalation()
    {
        var p = new EncounterEpisodeCoverage(); Establish(p); long revision = p.Revision;
        for (int i = 36; i < 60; i++) Step(p, i * .1, pressure: EncounterPressure.Engaged, protect: true);
        Check(p.Revision == revision, "Protected action restarted overview immediately.");
        Step(p, 6.1, pressure: EncounterPressure.Engaged);
        Check(p.Revision > revision && !p.OverviewEstablished, "Escalation reused presence-only overview.");
    }
    private static byte[] Pixels(byte value)
    { var bytes = new byte[64 * 64 * 4]; for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = bytes[i+1] = bytes[i+2] = value; bytes[i+3] = 255; } return bytes; }
    private static void VerifiedRoi()
    {
        var bytes = Pixels(100); var roi = new PictureRegion(.3f, .1f, .7f, .3f);
        for (int y = 45; y < 58; y++) for (int x = 19; x < 45; x++) bytes[(y * 64 + x) * 4] = 240;
        Check(RenderedPictureQuality.Measure(bytes, 64, 64, roi, true, ProgramRowOrder.BottomFirst).Concern > 0, "Verified bottom-first ROI used unrelated mirrored detail.");
        Check(RenderedPictureQuality.Measure(bytes, 64, 64, roi, true, ProgramRowOrder.TopFirst).Concern == 0, "Verified top-first ROI was not flipped exactly once.");
    }
    private static RenderedPictureFeedback Concern(RenderedPicturePolicy pixels, RenderedPictureKey key)
    {
        var sample = RenderedPictureQuality.Measure(Pixels(100), 64, 64, new PictureRegion(.3f,.2f,.7f,.6f), true, ProgramRowOrder.TopFirst);
        for (int i = 0; i < 5; i++) pixels.Observe(key, i * .5, i * .5 + .01, sample);
        return pixels.Feedback(key, 2.1);
    }
    private static void PixelCorrection()
    {
        var pixels = new RenderedPicturePolicy(); var key = new RenderedPictureKey(1, 2, 0); var feedback = Concern(pixels, key);
        Check(feedback.ShouldReconsider, "Persistent concern never requested correction.");
        var selection = new ProceduralShotPolicy(); selection.Select(0, new[] { .8f, .8f }, All, All);
        for (int i = 1; i <= 20; i++) selection.Select(i * .1, new[] { .8f, .8f }, All, All);
        float[] scores = { .8f - feedback.Penalty, .8f };
        for (int i = 21; i <= 51; i++) selection.Select(i * .1, scores, All, All, reconsiderPicture: true);
        Check(selection.SelectedIndex == 1, "Pixel concern remained below the normal improvement threshold forever.");
        Check(pixels.MarkReconsidered(key, 2.7) && !pixels.MarkReconsidered(key, 2.8), "One concern authorized multiple corrections.");
        var next = new RenderedPictureKey(1, 3, 1); var dark = RenderedPictureQuality.Measure(Pixels(1),64,64,default,false);
        for (int i = 6; i < 16; i++) pixels.Observe(next, i * .5, i * .5 + .01, dark);
        Check(!pixels.Feedback(next, 7.6).ShouldReconsider, "Similar night conditions immediately cycled the next shot.");
    }
    private static void PixelSafety()
    {
        var selection = new ProceduralShotPolicy(); var eligible = new[] { true, false };
        for (int i = 0; i < 100; i++) Check(selection.Select(i * .1, new[] { .2f, 1f }, eligible, All, reconsiderPicture: true) == 0, "Pixels invented a physically available camera.");
    }
}
