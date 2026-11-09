using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class OcclusionRecoveryPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Occlusion below the limit never arms recovery", BelowThresholdStaysIdle);
        yield return ("A short occlusion spike never engages steering", ShortSpikeNeverEngages);
        yield return ("Sustained severe occlusion engages the nearest clear azimuth", SustainedOcclusionEngages);
        yield return ("An actively resolving deoccluder defers engagement up to the grace window", DeoccluderGraceDefers);
        yield return ("A stalled deoccluder loses its grace and recovery engages", StalledDeoccluderEngages);
        yield return ("Selection prefers the smallest turn and rejects unclear candidates", NearestClearSelection);
        yield return ("Equal turns prefer the clearer side and wrap across +/-180", SelectionTieAndWrap);
        yield return ("Without a qualifying candidate the policy keeps watching", NoCandidateKeepsWatching);
        yield return ("A collapsing target azimuth is re-selected while steering", RetargetOnCollapse);
        yield return ("Release requires low occlusion sustained through the hysteresis window", DisengageHysteresis);
        yield return ("A fresh engagement is blocked during the cooldown", CooldownBlocksReengagement);
        yield return ("A clock rollback resets the machine instead of engaging", ClockRollbackResets);
        yield return ("Recovery policy rejects non-finite and out-of-range inputs", ContractValidation);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static OcclusionRecoveryCandidate C(float azimuth, float clear) =>
        new OcclusionRecoveryCandidate(azimuth, clear);

    private const double Step = 1.0 / 30;

    // Drive the policy with constant inputs for a duration; candidates are
    // supplied whenever the previous decision requested them (adapter contract).
    private static OcclusionRecoveryDecision Drive(OcclusionRecoveryPolicy policy, ref double now,
        double seconds, float occlusion, bool displacing, float azimuth,
        IReadOnlyList<OcclusionRecoveryCandidate>? probes, ref bool probed)
    {
        OcclusionRecoveryDecision decision = default;
        for (double end = now + seconds; now < end; now += Step)
        {
            decision = policy.Observe(now, occlusion, displacing, azimuth, probed ? probes : null);
            probed = decision.NeedsCandidates && probes != null;
        }
        return decision;
    }

    private static void BelowThresholdStaysIdle()
    {
        var policy = new OcclusionRecoveryPolicy();
        for (double now = 0; now < 5; now += Step)
        {
            var decision = policy.Observe(now, 0.79f, false, 10f, null);
            Check(decision.Phase == OcclusionRecoveryPhase.Idle, "Armed below the 80% occlusion limit.");
            Check(!decision.NeedsCandidates, "Requested probes while idle.");
        }
    }

    private static void ShortSpikeNeverEngages()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        for (int spike = 0; spike < 6; spike++)
        {
            // 0.3s of full occlusion (under the 0.4s sustain), then clear air.
            var during = Drive(policy, ref now, 0.3, 1f, false, 0f, probes, ref probed);
            Check(during.Phase != OcclusionRecoveryPhase.Steering, "A sub-sustain spike engaged steering.");
            var after = Drive(policy, ref now, 0.5, 0f, false, 0f, probes, ref probed);
            Check(after.Phase == OcclusionRecoveryPhase.Idle, "Policy failed to settle between spikes.");
        }
    }

    private static void SustainedOcclusionEngages()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(-70f, 0.9f), C(35f, 0.95f), C(110f, 1f) };
        bool probed = false;
        double now = 0;
        var decision = Drive(policy, ref now, EngageTime(), 0.85f, false, 0f, probes, ref probed);
        Check(decision.Phase == OcclusionRecoveryPhase.Steering, "Sustained 85% occlusion never engaged.");
        Check(decision.TargetAzimuthDegrees == 35f, "Did not pick the smallest clear turn.");
        Check(policy.Phase == OcclusionRecoveryPhase.Steering, "Phase property disagrees with the decision.");
    }

    // Time comfortably past sustain + grace for engagement-focused tests.
    private static double EngageTime() => OcclusionRecoveryPolicy.EngageSustainSeconds +
        OcclusionRecoveryPolicy.DeoccluderGraceSeconds + 0.3;

    private static void DeoccluderGraceDefers()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        // Build a 0.95 occlusion peak, then fall to 0.85 while the deoccluder
        // displaces; the sustain window completes during the falling trend.
        Drive(policy, ref now, 0.3, 0.95f, true, 0f, probes, ref probed);
        var deferred = Drive(policy, ref now, 0.3, 0.85f, true, 0f, probes, ref probed);
        Check(deferred.Phase == OcclusionRecoveryPhase.Watching, "Engaged while the deoccluder was visibly resolving.");
        // The deoccluder finishes the job: occlusion drops, no arc ever fires.
        var resolved = Drive(policy, ref now, 1.0, 0.2f, false, 0f, probes, ref probed);
        Check(resolved.Phase == OcclusionRecoveryPhase.Idle, "Recovery engaged although the deoccluder resolved the shot.");
    }

    private static void StalledDeoccluderEngages()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        // Displacing but occlusion NOT falling: the grace never applies and
        // engagement lands right after the sustain window.
        var decision = Drive(policy, ref now, OcclusionRecoveryPolicy.EngageSustainSeconds + 0.2, 0.95f, true, 0f, probes, ref probed);
        Check(decision.Phase == OcclusionRecoveryPhase.Steering, "A stalled deoccluder deferred engagement.");
        // And a falling trend only buys the bounded grace window, never more.
        var late = new OcclusionRecoveryPolicy();
        probed = false; now = 0;
        Drive(late, ref now, 0.3, 0.99f, true, 0f, probes, ref probed);
        var engaged = Drive(late, ref now, OcclusionRecoveryPolicy.EngageSustainSeconds +
            OcclusionRecoveryPolicy.DeoccluderGraceSeconds + 0.2, 0.9f, true, 0f, probes, ref probed);
        Check(engaged.Phase == OcclusionRecoveryPhase.Steering, "Grace window extended beyond its bound.");
    }

    private static void NearestClearSelection()
    {
        var policy = new OcclusionRecoveryPolicy();
        // The nearest candidates are below the 0.7 clearance floor; the first
        // qualifying one further out must win over clearer-but-farther sides.
        var probes = new[] { C(25f, 0.69f), C(-25f, 0.5f), C(-60f, 0.75f), C(120f, 1f) };
        bool probed = false;
        double now = 0;
        var decision = Drive(policy, ref now, EngageTime(), 1f, false, 10f, probes, ref probed);
        Check(decision.Phase == OcclusionRecoveryPhase.Steering, "Selection scenario never engaged.");
        Check(decision.TargetAzimuthDegrees == -60f,
            $"Expected -60 (nearest clear); got {decision.TargetAzimuthDegrees}.");
    }

    private static void SelectionTieAndWrap()
    {
        var policy = new OcclusionRecoveryPolicy();
        // From azimuth 170, the -150 candidate is a 40-degree turn across the
        // +/-180 wrap and 130 is a 40-degree turn the other way: an exact tie
        // that the clearer side (130 at 0.95) must win.
        var probes = new[] { C(-150f, 0.8f), C(130f, 0.95f), C(0f, 0.72f) };
        bool probed = false;
        double now = 0;
        var decision = Drive(policy, ref now, EngageTime(), 1f, false, 170f, probes, ref probed);
        Check(decision.Phase == OcclusionRecoveryPhase.Steering, "Tie scenario never engaged.");
        Check(decision.TargetAzimuthDegrees == 130f,
            $"Expected 130 (equal turn, clearer side); got {decision.TargetAzimuthDegrees}.");
        Check(OcclusionRecoveryPolicy.DeltaAngle(170f, -150f) == 40f, "Wrap-aware delta is wrong.");
    }

    private static void NoCandidateKeepsWatching()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 0.3f), C(-90f, 0.69f) };
        bool probed = false;
        double now = 0;
        var decision = Drive(policy, ref now, 3.0, 1f, false, 0f, probes, ref probed);
        Check(decision.Phase == OcclusionRecoveryPhase.Watching, "Engaged without any qualifying clear azimuth.");
        Check(decision.NeedsCandidates, "Must keep requesting probes while boxed in.");
    }

    private static void RetargetOnCollapse()
    {
        var policy = new OcclusionRecoveryPolicy();
        var open = new[] { C(45f, 0.95f), C(-110f, 0.85f) };
        bool probed = false;
        double now = 0;
        var engaged = Drive(policy, ref now, EngageTime(), 1f, false, 0f, open, ref probed);
        Check(engaged.Phase == OcclusionRecoveryPhase.Steering && engaged.TargetAzimuthDegrees == 45f,
            "Setup failed to steer to the near side.");
        // The chosen side slams shut; the far side stays open.
        var collapsed = new[] { C(45f, 0.2f), C(-110f, 0.85f) };
        var retargeted = Drive(policy, ref now, 0.2, 1f, false, 10f, collapsed, ref probed);
        Check(retargeted.Phase == OcclusionRecoveryPhase.Steering, "Collapse released steering entirely.");
        Check(retargeted.TargetAzimuthDegrees == -110f,
            $"Expected re-target to -110; got {retargeted.TargetAzimuthDegrees}.");
        // Everything collapses: steer to the clearest probe rather than park.
        var boxed = new[] { C(-110f, 0.4f), C(45f, 0.1f) };
        var fallback = Drive(policy, ref now, 0.2, 1f, false, 10f, boxed, ref probed);
        Check(fallback.Phase == OcclusionRecoveryPhase.Steering && fallback.TargetAzimuthDegrees == -110f,
            "With no qualifying escape, must steer toward the clearest probe.");
    }

    private static void DisengageHysteresis()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        Drive(policy, ref now, EngageTime(), 1f, false, 0f, probes, ref probed);
        Check(policy.Phase == OcclusionRecoveryPhase.Steering, "Setup failed to engage.");
        // Low occlusion shorter than the release window: steering continues.
        var brief = Drive(policy, ref now, OcclusionRecoveryPolicy.ReleaseSustainSeconds - 0.15, 0.1f, false, 45f, probes, ref probed);
        Check(brief.Phase == OcclusionRecoveryPhase.Steering, "Released before the 0.5s sustain.");
        // A bounce above the release threshold restarts the window.
        Drive(policy, ref now, 0.2, 0.5f, false, 45f, probes, ref probed);
        var bounced = Drive(policy, ref now, OcclusionRecoveryPolicy.ReleaseSustainSeconds - 0.15, 0.1f, false, 45f, probes, ref probed);
        Check(bounced.Phase == OcclusionRecoveryPhase.Steering, "Release window survived an occlusion bounce.");
        // Sustained low occlusion releases control back to recentering.
        var released = Drive(policy, ref now, OcclusionRecoveryPolicy.ReleaseSustainSeconds + 0.2, 0.1f, false, 45f, probes, ref probed);
        Check(released.Phase == OcclusionRecoveryPhase.Idle, "Never released after sustained low occlusion.");
        Check(released.Reason == "occlusion resolved; recentering resumes" || !released.NeedsCandidates,
            "Release must stop requesting probes.");
        // Mid-band occlusion (0.3..0.8) keeps steering: hysteresis, not a knife edge.
        var mid = new OcclusionRecoveryPolicy();
        probed = false; now = 0;
        Drive(mid, ref now, EngageTime(), 1f, false, 0f, probes, ref probed);
        var held = Drive(mid, ref now, 2.0, 0.5f, false, 45f, probes, ref probed);
        Check(held.Phase == OcclusionRecoveryPhase.Steering, "Mid-band occlusion released steering.");
    }

    private static void CooldownBlocksReengagement()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        Drive(policy, ref now, EngageTime(), 1f, false, 0f, probes, ref probed);
        Check(policy.Phase == OcclusionRecoveryPhase.Steering, "Setup failed to engage.");
        // Step frame-by-frame so the exact release moment is captured.
        double releasedAt = -1;
        for (int i = 0; releasedAt < 0 && i < 60; i++, now += Step)
            if (policy.Observe(now, 0.1f, false, 80f, probes).Phase == OcclusionRecoveryPhase.Idle)
                releasedAt = now;
        Check(releasedAt >= 0, "Setup failed to release.");
        // Fully occluded again immediately: nothing may engage inside ~2s.
        while (now < releasedAt + OcclusionRecoveryPolicy.CooldownSeconds - 0.1)
        {
            var decision = policy.Observe(now, 1f, false, 80f, probes);
            Check(decision.Phase == OcclusionRecoveryPhase.Idle, "Re-engaged during the cooldown.");
            now += Step;
        }
        // After the cooldown the ordinary sustain path engages again.
        var again = Drive(policy, ref now, EngageTime() + 0.2, 1f, false, 80f, probes, ref probed);
        Check(again.Phase == OcclusionRecoveryPhase.Steering, "Cooldown never expired.");
    }

    private static void ClockRollbackResets()
    {
        var policy = new OcclusionRecoveryPolicy();
        var probes = new[] { C(90f, 1f) };
        bool probed = false;
        double now = 0;
        Drive(policy, ref now, EngageTime(), 1f, false, 0f, probes, ref probed);
        Check(policy.Phase == OcclusionRecoveryPhase.Steering, "Setup failed to engage.");
        // World reload: the clock goes backwards. The machine restarts rather
        // than steering from stale state, and the sustain gate applies anew.
        var reset = policy.Observe(0.5, 1f, false, 0f, probes);
        Check(reset.Phase != OcclusionRecoveryPhase.Steering, "Steering survived a clock rollback.");
        var shortly = policy.Observe(0.6, 1f, false, 0f, probes);
        Check(shortly.Phase == OcclusionRecoveryPhase.Watching, "Rollback did not restart the sustain window.");
    }

    private static void ContractValidation()
    {
        var policy = new OcclusionRecoveryPolicy();
        var good = new[] { C(90f, 1f) };
        foreach (var (now, occlusion, azimuth) in new (double, float, float)[]
        {
            (double.NaN, 0.5f, 0f), (double.PositiveInfinity, 0.5f, 0f), (-0.1, 0.5f, 0f),
            (1, float.NaN, 0f), (1, -0.01f, 0f), (1, 1.01f, 0f), (1, float.PositiveInfinity, 0f),
            (1, 0.5f, float.NaN), (1, 0.5f, float.NegativeInfinity)
        })
        {
            bool threw = false;
            try { policy.Observe(now, occlusion, false, azimuth, good); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, $"Accepted invalid inputs ({now}, {occlusion}, {azimuth}).");
        }
        foreach (var candidate in new[]
        {
            C(float.NaN, 0.5f), C(0f, float.NaN), C(0f, -0.1f), C(0f, 1.1f), C(float.PositiveInfinity, 1f)
        })
        {
            bool threw = false;
            try { policy.Observe(1, 0.5f, false, 0f, new[] { candidate }); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Accepted an invalid candidate probe.");
        }
        // Null and empty candidate lists are legal (nothing probed yet).
        policy.Observe(1, 0.5f, false, 0f, null);
        policy.Observe(2, 0.5f, false, 0f, Array.Empty<OcclusionRecoveryCandidate>());
    }
}
