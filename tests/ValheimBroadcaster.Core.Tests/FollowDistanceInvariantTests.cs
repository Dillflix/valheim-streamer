using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class FollowDistanceInvariantTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Follow invariant stays disengaged inside the nominal band", WithinBand);
        yield return ("A parked camera with a receding subject engages pursuit", ParkedCameraEngages);
        yield return ("Regression: the hold-loop park (frozen pose, walking subject) cannot persist", HoldLoopRegression);
        yield return ("Invariant pursuit converges and disengages on catch-up", CatchUpDisengages);
        yield return ("A moving camera (active blend or tracking) never trips the invariant", MovingCameraExempt);
        yield return ("Subject teleports restart the stillness window", TeleportResets);
        yield return ("Pursuit steps are bounded and aim at the subject", StepGeometry);
        yield return ("Follow invariant rejects invalid clocks and distances", Validation);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static AutonomyPosition P(float x, float y, float z) => new AutonomyPosition(x, y, z);

    private static float Distance(AutonomyPosition a, AutonomyPosition b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return (float)Math.Sqrt(x * x + y * y + z * z);
    }

    private static void WithinBand()
    {
        var invariant = new FollowDistanceInvariant();
        // Subject walks, camera tracks at the nominal distance: never engages.
        for (int i = 0; i <= 100; i++)
        {
            float t = i * 0.05f;
            var decision = invariant.Observe(t, P(0, 2, -6 + t * 7), P(0, 1.5f, t * 7), 6f, 0.05);
            Check(!decision.Engaged, "Invariant engaged while the camera was inside its band.");
        }
        // Even a static camera stays disengaged while the subject is close.
        invariant.Reset();
        for (int i = 0; i <= 100; i++)
            Check(!invariant.Observe(i * 0.05, P(0, 2, -6), P(0, 1.5f, 2), 6f, 0.05).Engaged,
                "Invariant engaged at sub-band distance.");
    }

    private static void ParkedCameraEngages()
    {
        var invariant = new FollowDistanceInvariant();
        bool engaged = false;
        // Camera frozen at the origin; subject recedes at 7 m/s from 6m away.
        for (int i = 0; i <= 80 && !engaged; i++)
            engaged = invariant.Observe(i * 0.05, P(0, 2, 0), P(0, 1.5f, 6 + i * 0.05f * 7f), 6f, 0.05).Engaged;
        Check(engaged, "A parked camera never engaged while the subject receded past the band.");
        Check(invariant.Engaged, "Engaged state not exposed.");
    }

    private static void HoldLoopRegression()
    {
        // The live parking mechanism: a Hold presents the identical pose every
        // frame (destination unreadable, source readable - ShotTransitionPolicy
        // keeps answering Hold) while the player walks straight away. The
        // invariant is the last line that must force motion regardless.
        var hold = ShotTransitionPolicy.Decide(true, true, 1f, false, true, 0, 0, 0, 0,
            ShotRole.Tracking, ShotRole.Tracking, ShotEnvironment.Open, CinematicActivity.Idle);
        Check(hold.Mode == ShotTransitionMode.Hold, "Expected the hold branch that parks the presented pose.");
        var invariant = new FollowDistanceInvariant();
        double now = 0; bool engaged = false; double engagedAt = -1;
        for (int i = 0; i <= 200; i++, now += 1.0 / 60)
        {
            var decision = invariant.Observe(now, P(10, 3, 10), P(10, 1.5f, 14 + (float)now * 6f), 5f, 1.0 / 60);
            if (decision.Engaged && !engaged) { engaged = true; engagedAt = now; }
            if (engaged)
                Check(decision.Reason == "distance invariant engaged", "Engagement must be named for diagnostics.");
        }
        Check(engaged, "The hold-loop scenario never engaged the invariant.");
        Check(engagedAt <= 2.5, $"Engagement too slow for a live regression ({engagedAt:F2}s).");
    }

    private static void CatchUpDisengages()
    {
        var invariant = new FollowDistanceInvariant();
        AutonomyPosition camera = P(0, 2, 0);
        double now = 0;
        // Park until engaged, subject receding.
        bool engaged = false;
        for (int i = 0; i <= 90 && !engaged; i++, now += 1.0 / 30)
            engaged = invariant.Observe(now, camera, P(0, 1.5f, 6 + (float)now * 7f), 6f, 1.0 / 30).Engaged;
        Check(engaged, "Setup failed to engage.");
        // Subject stops; apply returned displacements until catch-up.
        AutonomyPosition subject = P(0, 1.5f, 6 + (float)now * 7f);
        bool disengaged = false;
        for (int i = 0; i < 600 && !disengaged; i++, now += 1.0 / 30)
        {
            var decision = invariant.Observe(now, camera, subject, 6f, 1.0 / 30);
            if (!decision.Engaged) { disengaged = true; break; }
            camera = new AutonomyPosition(camera.X + decision.Displacement.X,
                camera.Y + decision.Displacement.Y, camera.Z + decision.Displacement.Z);
        }
        Check(disengaged, "Pursuit never disengaged after catching up.");
        Check(Distance(camera, subject) <= 6f * FollowDistanceInvariant.DisengageScale + 0.01f,
            "Disengaged outside the hysteresis band.");
        Check(Distance(camera, subject) >= 5.9f, "Pursuit overshot inside the nominal distance.");
    }

    private static void MovingCameraExempt()
    {
        var invariant = new FollowDistanceInvariant();
        // Camera far outside the band but travelling (an active blend moves
        // every frame): the stillness window must keep the invariant out.
        for (int i = 0; i <= 150; i++)
        {
            float t = i * 0.05f;
            var decision = invariant.Observe(t, P(0, 2, t * 2f), P(0, 1.5f, 40 + t * 7f), 6f, 0.05);
            Check(!decision.Engaged, "Invariant hijacked a camera that was already moving.");
        }
    }

    private static void TeleportResets()
    {
        var invariant = new FollowDistanceInvariant();
        for (int i = 0; i <= 30; i++)
            invariant.Observe(i * 0.05, P(0, 2, 0), P(0, 1.5f, 6 + i * 0.05f * 7f), 6f, 0.05);
        // Subject teleports 50m: the editorial layer owns that discontinuity.
        var after = invariant.Observe(1.55, P(0, 2, 0), P(50, 1.5f, 60), 6f, 0.05);
        Check(!after.Engaged, "Invariant chased across a teleport discontinuity.");
        // And the stillness window restarted: no engagement within the window.
        for (int i = 0; i <= 15; i++)
            Check(!invariant.Observe(1.6 + i * 0.05, P(0, 2, 0), P(50, 1.5f, 60 + i * 0.05f * 7f), 6f, 0.05).Engaged,
                "Invariant engaged before a full post-teleport window.");
    }

    private static void StepGeometry()
    {
        var invariant = new FollowDistanceInvariant();
        FollowInvariantDecision last = default;
        double now = 0;
        for (int i = 0; i <= 90; i++, now += 1.0 / 30)
            last = invariant.Observe(now, P(0, 2, 0), P(0, 2, 6 + (float)now * 7f), 6f, 1.0 / 30);
        Check(last.Engaged, "Setup failed to engage.");
        float step = (float)Math.Sqrt(last.Displacement.X * last.Displacement.X +
            last.Displacement.Y * last.Displacement.Y + last.Displacement.Z * last.Displacement.Z);
        Check(step > 0, "Engaged invariant produced no motion.");
        Check(step <= FollowDistanceInvariant.MaximumPursuitSpeed / 30f + 0.001f,
            $"Pursuit step {step:F3}m exceeds the speed ceiling.");
        Check(last.Displacement.Z > 0 && Math.Abs(last.Displacement.X) < 0.001f,
            "Pursuit step does not point toward the subject.");
    }

    private static void Validation()
    {
        var invariant = new FollowDistanceInvariant();
        foreach (var (now, dt, nominal) in new (double, double, float)[]
        {
            (double.NaN, 0.05, 6f), (-1, 0.05, 6f), (1, double.NaN, 6f), (1, -0.05, 6f),
            (1, double.PositiveInfinity, 6f), (1, 0.05, 0f), (1, 0.05, -3f),
            (1, 0.05, float.NaN), (1, 0.05, 501f)
        })
        {
            bool threw = false;
            try { invariant.Observe(now, P(0, 0, 0), P(0, 0, 10), nominal, dt); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Invalid invariant clock/distance accepted.");
        }
        // A backwards clock is a world reload, not an exception: it resets.
        invariant.Observe(10, P(0, 0, 0), P(0, 0, 30), 6f, 0.05);
        var reset = invariant.Observe(1, P(0, 0, 0), P(0, 0, 30), 6f, 0.05);
        Check(!reset.Engaged, "A clock rollback must restart the window, not engage.");
    }
}
