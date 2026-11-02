using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotCompositionPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Composition deliberately places a stationary solo subject beyond the dead zone", Stationary);
        yield return ("Composition preserves side under noisy motion and accepts sustained turns", MotionEvidence);
        yield return ("Composition retains lead room when a moving player stops", StoppedPlayer);
        yield return ("Composition activity interest outranks unrelated facing evidence", ActivityInterest);
        yield return ("Composition leaves headroom and respects asymmetric subject extents", Headroom);
        yield return ("Composition large projected envelopes fall back to neutral framing", LargeEnvelope);
        yield return ("Composition restrains offsets in cramped space and after corrections", Constrained);
        yield return ("Composition group bounds keep the primary player prominent", GroupPrimary);
        yield return ("Composition gaps discard pending turns without moving an unseen camera", Gaps);
        yield return ("Composition paused frames do not accumulate direction evidence", PausedFrames);
        yield return ("Composition toggles and shot changes preserve damping", ModeContinuity);
        yield return ("Composition is mirror-consistent for facing movement and activity", Mirror);
        yield return ("Composition standby cadence matches live evaluation cadence", Cadence);
        yield return ("Composition reset removes prior subject direction and history", Reset);
    }

    private static ShotCompositionObservation Solo => new ShotCompositionObservation
    { LeftExtent = 0.05f, RightExtent = 0.05f, TopExtent = 0.06f, BottomExtent = 0.18f };
    private static ShotComposition Tick(ShotCompositionPolicy policy, double time, ShotCompositionObservation observation,
        CinematicActivity activity = CinematicActivity.Idle, long shot = 0, bool confined = false,
        float freedom = 1, int corrections = 0, bool usable = true, bool enabled = true) =>
        policy.Update(time, 1.0 / 60, shot, activity, observation, confined, freedom, corrections, usable, enabled);
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Stationary()
    {
        var policy = new ShotCompositionPolicy();
        ShotComposition first = Tick(policy, 0, Solo);
        Check(Math.Abs(first.ScreenX) > 0.12f / 2 && first.ScreenY > 0.03,
            "Stationary composition remained effectively centred inside the default dead zone.");
        for (int i = 1; i <= 600; i++)
        {
            ShotComposition next = Tick(policy, i * 0.1, Solo);
            Check(next.ScreenX == first.ScreenX && next.ScreenY == first.ScreenY,
                "An idle composition introduced arbitrary periodic drift.");
        }
        Check(first.FitsEnvelope, "Normal solo envelope was rejected.");
    }

    private static void MotionEvidence()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.MotionX = 0.08f;
        Check(Tick(policy, 0, observation, CinematicActivity.Travel).ScreenX < -0.1,
            "Travel failed to provide room ahead of rightward screen motion.");
        for (int i = 1; i <= 30; i++)
        {
            observation.MotionX = i % 2 == 0 ? -0.02f : 0.02f;
            Check(Tick(policy, i * 0.1, observation, CinematicActivity.Travel).ScreenX < -0.1,
                "Motion noise flipped the established composition.");
        }
        observation.MotionX = -0.08f;
        for (int i = 31; i <= 35; i++)
            Check(Tick(policy, i * 0.1, observation, CinematicActivity.Travel).DesiredX < 0,
                "A momentary reversal flipped the subject's screen side.");
        for (int i = 36; i <= 75; i++) Tick(policy, i * 0.1, observation, CinematicActivity.Travel);
        Check(policy.Current.ScreenX > 0.1 && policy.Current.DesiredX > 0,
            "Persistent opposite movement never obtained appropriate lead room.");
    }

    private static void StoppedPlayer()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.MotionX = -0.1f;
        Tick(policy, 0, observation, CinematicActivity.Travel);
        observation.MotionX = 0;
        for (int i = 1; i <= 150; i++) Tick(policy, i * 0.1, observation);
        Check(policy.Current.ScreenX > 0.1, "Stopping returned to an unrelated default screen side.");
    }

    private static void ActivityInterest()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.FacingX = -1; observation.FacingConfidence = 1;
        observation.InterestX = 0.25f; observation.InterestConfidence = 0.9f;
        Tick(policy, 0, observation, CinematicActivity.Work);
        Check(policy.Current.ScreenX < -0.08, "Body-facing evidence overrode a relevant observed work target.");
        observation.InterestConfidence = 0.2f;
        for (int i = 1; i <= 20; i++)
            Check(Tick(policy, i * 0.1, observation, CinematicActivity.Work).DesiredX < 0,
                "Briefly missing activity evidence abruptly reversed the shot.");
        for (int i = 21; i <= 80; i++) Tick(policy, i * 0.1, observation, CinematicActivity.Work);
        Check(policy.Current.ScreenX > 0.08, "The director retained vanished activity evidence indefinitely.");
    }

    private static void Headroom()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.TopExtent = 0.39f;
        observation.BottomExtent = 0.3f;
        ShotComposition result = Tick(policy, 0, observation);
        Check(result.FitsEnvelope && result.ScreenY + observation.TopExtent <= 0.4101f,
            "Upper placement consumed required headroom.");
        observation.TopExtent = 0.1f; observation.BottomExtent = 0.48f;
        result = Tick(policy, 0.1, observation);
        Check(result.FitsEnvelope && result.ScreenY - observation.BottomExtent >= -0.4401f,
            "Asymmetric full-body extent was cropped by centered-anchor assumptions.");
        observation.LeftExtent = 0.42f; observation.RightExtent = 0.42f;
        result = Tick(policy, 0.2, observation);
        Check(Math.Abs(result.ScreenX) <= 0.0201f,
            "A narrow projected viewport retained unsafe thirds placement.");
    }

    private static void LargeEnvelope()
    {
        var policy = new ShotCompositionPolicy();
        Tick(policy, 0, Solo);
        ShotCompositionObservation observation = Solo;
        observation.LeftExtent = observation.RightExtent = 0.5f;
        ShotComposition result = Tick(policy, 0.1, observation);
        Check(!result.FitsEnvelope && result.ScreenX == 0 && result.ScreenY == 0,
            "An impossible subject envelope was presented as a valid thirds composition.");
    }

    private static void Constrained()
    {
        var policy = new ShotCompositionPolicy();
        ShotComposition initial = Tick(policy, 0, Solo);
        for (int i = 1; i <= 60; i++) Tick(policy, i * 0.1, Solo, confined: true, freedom: 0.3f);
        Check(Math.Abs(policy.Current.ScreenX) < Math.Abs(initial.ScreenX) * 0.1,
            "Cramped geometry retained large aesthetic composition offsets.");
        for (int i = 61; i <= 120; i++) Tick(policy, i * 0.1, Solo, corrections: 3);
        Check(Math.Abs(policy.Current.DesiredX) <= 0.03,
            "Frequent camera corrections failed to reduce discretionary framing.");
    }

    private static void GroupPrimary()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.Grouped = true;
        observation.PrimaryOffsetX = 0.38f;
        observation.LeftExtent = 0.17f; observation.RightExtent = 0.43f;
        ShotComposition result = Tick(policy, 0, observation);
        Check(result.FitsEnvelope && result.ScreenX + observation.PrimaryOffsetX <= 0.2801f,
            "Group framing marginalized the primary subject.");
        Check(result.ScreenX - observation.LeftExtent >= -0.4401f &&
            result.ScreenX + observation.RightExtent <= 0.4401f,
            "Primary bias pushed another requested group member beyond the envelope.");
    }

    private static void Gaps()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo;
        observation.MotionX = 0.1f;
        Tick(policy, 0, observation, CinematicActivity.Travel);
        observation.MotionX = -0.1f;
        for (int i = 1; i <= 6; i++) Tick(policy, i * 0.1, observation, CinematicActivity.Travel);
        float previous = policy.Current.ScreenX;
        Tick(policy, 10, observation, CinematicActivity.Travel, usable: false);
        Tick(policy, 20, observation, CinematicActivity.Travel);
        Check(policy.Current.ScreenX == previous, "Unseen time advanced composition easing.");
        for (int i = 1; i <= 5; i++)
            Check(Tick(policy, 20 + i * 0.1, observation, CinematicActivity.Travel).DesiredX < 0,
                "Old pending reversal survived the observation gap.");
        for (int i = 6; i <= 50; i++) Tick(policy, 20 + i * 0.1, observation, CinematicActivity.Travel);
        Check(policy.Current.ScreenX > 0.1, "Fresh evidence could not establish composition after a gap.");
    }

    private static void ModeContinuity()
    {
        var policy = new ShotCompositionPolicy();
        ShotComposition first = Tick(policy, 0, Solo);
        ShotComposition disabled = Tick(policy, 0.1, Solo, enabled: false);
        Check(disabled.ScreenX < -0.1 && disabled.DesiredX == 0,
            "Disabling composition snapped the camera target to center.");
        for (int i = 2; i <= 70; i++) Tick(policy, i * 0.1, Solo, enabled: false);
        Check(Math.Abs(policy.Current.ScreenX) < 0.001, "Disabled placement did not converge to neutral.");
        ShotComposition enabled = Tick(policy, 7.1, Solo, shot: 1);
        Check(Math.Abs(enabled.ScreenX) < 0.03 && enabled.DesiredX > 0.1,
            "A new deliberate shot reset composition damping.");
        Check(first.ScreenX < 0, "Initial composition was not deliberate.");
    }

    private static void PausedFrames()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo; observation.MotionX = 0.1f;
        ShotComposition before = Tick(policy, 0, observation, CinematicActivity.Travel);
        observation.MotionX = -0.1f;
        for (int i = 1; i <= 120; i++)
        {
            ShotComposition paused = policy.Update(i / 60.0, 0, 0, CinematicActivity.Travel,
                observation, false, 1, 0);
            Check(paused.ScreenX == before.ScreenX && paused.DesiredX == before.DesiredX,
                "A paused game accumulated camera evidence or moved the composition.");
        }
        for (int i = 1; i <= 5; i++)
            Check(Tick(policy, 2 + i * 0.1, observation, CinematicActivity.Travel).DesiredX < 0,
                "Resuming counted paused time as a persistent direction change.");
        for (int i = 6; i <= 50; i++) Tick(policy, 2 + i * 0.1, observation, CinematicActivity.Travel);
        Check(policy.Current.ScreenX > 0.1, "Paused evidence prevented valid updates after resume.");
    }

    private static void Mirror()
    {
        foreach (CinematicActivity activity in new[] { CinematicActivity.Idle, CinematicActivity.Travel, CinematicActivity.Work })
        {
            var left = new ShotCompositionPolicy(); var right = new ShotCompositionPolicy();
            ShotCompositionObservation a = Solo, b = Solo;
            if (activity == CinematicActivity.Idle)
            { a.FacingX = -0.8f; b.FacingX = 0.8f; a.FacingConfidence = b.FacingConfidence = 1; }
            else if (activity == CinematicActivity.Travel) { a.MotionX = -0.1f; b.MotionX = 0.1f; }
            else { a.InterestX = -0.2f; b.InterestX = 0.2f; a.InterestConfidence = b.InterestConfidence = 0.9f; }
            for (int i = 0; i <= 60; i++)
            {
                ShotComposition first = Tick(left, i * 0.1, a, activity);
                ShotComposition second = Tick(right, i * 0.1, b, activity);
                Check(Math.Abs(first.ScreenX + second.ScreenX) < 0.0001 && first.ScreenY == second.ScreenY,
                    "Mirrored actor evidence did not produce mirrored composition.");
            }
        }
    }

    private static void Cadence()
    {
        var live = new ShotCompositionPolicy(); var standby = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo; observation.MotionX = 0.1f;
        Tick(live, 0, observation, CinematicActivity.Travel); Tick(standby, 0, observation, CinematicActivity.Travel);
        observation.MotionX = -0.1f;
        for (int i = 1; i <= 300; i++)
        {
            Tick(live, i / 60.0, observation, CinematicActivity.Travel);
            if (i % 12 == 0) Tick(standby, i / 60.0, observation, CinematicActivity.Travel);
        }
        Check(live.Current.ScreenX > 0.1 && standby.Current.ScreenX > 0.1 &&
            Math.Abs(live.Current.ScreenX - standby.Current.ScreenX) < 0.015,
            "Standby update cadence changed direction confirmation or damping materially.");
    }

    private static void Reset()
    {
        var policy = new ShotCompositionPolicy();
        ShotCompositionObservation observation = Solo; observation.FacingX = -1; observation.FacingConfidence = 1;
        Tick(policy, 5, observation);
        Check(policy.Current.ScreenX > 0, "Fixture did not establish a right-side subject.");
        policy.Reset();
        Check(policy.Current.ScreenX == 0 && policy.Current.ScreenY == 0, "Reset retained old placement.");
        Check(Tick(policy, 0, Solo).ScreenX < 0, "Previous subject evidence leaked into the new shot.");
    }
}
