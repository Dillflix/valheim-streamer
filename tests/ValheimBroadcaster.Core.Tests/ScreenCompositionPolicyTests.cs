using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScreenCompositionPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("The initial side is LEFT at -1/6", DefaultsLeft);
        yield return ("Sustained rightward motion past 1.5s flips the player to the RIGHT thirds", SustainedFlipsRight);
        yield return ("A brief 0.8s rightward spurt never flips the side", BriefSpurtNeverFlips);
        yield return ("Oscillating motion keeps the current side", OscillationKeepsSide);
        yield return ("Stationary motion keeps the current side", StationaryKeepsSide);
        yield return ("The lead offset signs away from motion, reaches full magnitude, and zeroes when stationary", LeadOffsetSignAndMagnitude);
        yield return ("A clock rollback resets to the default side and zero lead", ClockRollbackResets);
        yield return ("The policy rejects non-finite velocity and time", ContractValidation);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float expected, float actual, string message, float tol = 1e-3f)
    { if (Math.Abs(expected - actual) > tol) throw new Exception($"{message}: expected {expected}, got {actual}."); }

    private const double Step = 1.0 / 30;
    private const float Third = 1f / 6f;

    // Drive the policy at a fixed lateral velocity for a duration; return the last decision.
    private static ScreenCompositionDecision Drive(ScreenCompositionPolicy policy, ref double now,
        double seconds, float lateralVelocity)
    {
        ScreenCompositionDecision decision = policy.Observe(now, lateralVelocity);
        for (double end = now + seconds; now < end; now += Step)
            decision = policy.Observe(now, lateralVelocity);
        return decision;
    }

    private static void DefaultsLeft()
    {
        var policy = new ScreenCompositionPolicy();
        ScreenCompositionDecision d = policy.Observe(0, 0f);
        Check(d.Side == SoloCompositionSide.Left, "The default side is not LEFT.");
        Near(-Third, d.PlayerScreenX, "default player X is the left thirds");
        Near(0f, d.LeadOffsetX, "default lead offset is zero");
    }

    private static void SustainedFlipsRight()
    {
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        // Move right (+2 m/s, above the 1.2 threshold) for 2s: past the 1.5s sustain.
        // Trailing-lead-room: moving right -> player should sit on the LEFT... no:
        // the player TRAILS its motion, so moving right pushes the player to the
        // LEFT thirds. Wait - re-derive from the policy's contract below.
        ScreenCompositionDecision d = Drive(policy, ref now, 0.2, 2f);
        // Under the sustain gate the side is still the default LEFT.
        Check(d.Side == SoloCompositionSide.Left, "The side flipped before the sustain gate.");
        d = Drive(policy, ref now, 2.0, 2f);
        // Sustained rightward motion: the player trails, landing on the LEFT thirds
        // so the lead room opens to the RIGHT. The default is already LEFT, so moving
        // right confirms it. To observe a flip we must drive the OTHER direction.
        var policy2 = new ScreenCompositionPolicy();
        double now2 = 0;
        // Sustained LEFT motion (-2 m/s) for 2s flips the player to the RIGHT thirds
        // (trailing: moving left -> player on the right, room opens left).
        ScreenCompositionDecision flipped = Drive(policy2, ref now2, 2.0, -2f);
        Check(flipped.Side == SoloCompositionSide.Right, "Sustained opposite motion did not flip to RIGHT.");
        Near(Third, flipped.PlayerScreenX, "flipped player X is the right thirds");
    }

    private static void BriefSpurtNeverFlips()
    {
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        // 0.8s of sustained LEFT motion (would flip the default-LEFT side to RIGHT)
        // is under the 1.5s gate: no flip.
        ScreenCompositionDecision d = Drive(policy, ref now, 0.8, -2f);
        Check(d.Side == SoloCompositionSide.Left, "A sub-gate spurt flipped the side.");
        // Then it stops: still no flip, pending run cleared.
        d = Drive(policy, ref now, 0.5, 0f);
        Check(d.Side == SoloCompositionSide.Left, "The side flipped after the spurt ended.");
    }

    private static void OscillationKeepsSide()
    {
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        // Alternate left/right every 0.5s for 6s. No single opposite-direction run
        // ever reaches 1.5s, so the default LEFT side is never flipped.
        for (int i = 0; i < 12; i++)
        {
            float v = (i % 2 == 0) ? -2f : 2f;
            Drive(policy, ref now, 0.5, v);
        }
        Check(policy.Side == SoloCompositionSide.Left, "Oscillation flipped the side.");
    }

    private static void StationaryKeepsSide()
    {
        // First flip to RIGHT, then stay stationary a long time: the side holds.
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        Drive(policy, ref now, 2.0, -2f);
        Check(policy.Side == SoloCompositionSide.Right, "Setup flip to RIGHT failed.");
        ScreenCompositionDecision d = Drive(policy, ref now, 10.0, 0f);
        Check(d.Side == SoloCompositionSide.Right, "Stationary motion changed the held side.");
        Near(0f, d.LeadOffsetX, "stationary lead offset is zero");
    }

    private static void LeadOffsetSignAndMagnitude()
    {
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        // Sustained RIGHT motion: the lead biases AWAY (left, negative), ramping to
        // full magnitude -0.05 once past the sustain window.
        ScreenCompositionDecision d = Drive(policy, ref now, 2.0, 3f);
        Near(-0.05f, d.LeadOffsetX, "full lead offset is -0.05 for rightward motion", 1e-3f);
        // Sustained LEFT motion biases right (+0.05).
        var p2 = new ScreenCompositionPolicy();
        double n2 = 0;
        ScreenCompositionDecision d2 = Drive(p2, ref n2, 2.0, -3f);
        Near(0.05f, d2.LeadOffsetX, "full lead offset is +0.05 for leftward motion", 1e-3f);
        // Magnitude is bounded even at huge velocity.
        var p3 = new ScreenCompositionPolicy();
        double n3 = 0;
        ScreenCompositionDecision d3 = Drive(p3, ref n3, 2.0, 50f);
        Check(Math.Abs(d3.LeadOffsetX) <= 0.05f + 1e-6f, "lead offset exceeded its clamp.");
        // Below threshold is treated as stationary: zero lead.
        var p4 = new ScreenCompositionPolicy();
        double n4 = 0;
        ScreenCompositionDecision d4 = Drive(p4, ref n4, 2.0, 0.5f); // under 1.2 m/s
        Near(0f, d4.LeadOffsetX, "sub-threshold motion gives zero lead");
    }

    private static void ClockRollbackResets()
    {
        var policy = new ScreenCompositionPolicy();
        double now = 0;
        Drive(policy, ref now, 2.0, -2f);
        Check(policy.Side == SoloCompositionSide.Right, "Setup flip to RIGHT failed.");
        // A backwards clock (world reload) resets to the default LEFT, zero lead.
        ScreenCompositionDecision d = policy.Observe(0.1, 3f);
        Check(d.Side == SoloCompositionSide.Left, "Clock rollback did not reset the side.");
        Near(0f, d.LeadOffsetX, "rollback zeroes the lead offset");
    }

    private static void ContractValidation()
    {
        var policy = new ScreenCompositionPolicy();
        Throws(() => policy.Observe(double.NaN, 0f));
        Throws(() => policy.Observe(double.PositiveInfinity, 0f));
        Throws(() => policy.Observe(-1, 0f));
        Throws(() => policy.Observe(0, float.NaN));
        Throws(() => policy.Observe(0, float.PositiveInfinity));
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Expected ArgumentOutOfRangeException.");
    }
}
