using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ActorSpatialHorizonTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Actor spatial horizon plans a connected detour before the nominal future is blocked", Detour);
        yield return ("Actor spatial horizon never treats clear endpoints as proof of clear edges", BlockedCorridors);
        yield return ("Actor spatial horizon defers an exact immutable probe then rejects unknown evidence", UnknownEvidence);
        yield return ("Actor spatial horizon discards stale evidence and a different world epoch", Staleness);
        yield return ("Actor spatial horizon bounds all-clear and all-blocked search work", BoundedWork);
        yield return ("Actor spatial horizon preserves correction and inertia while replanning", ReplanContinuity);
        yield return ("Actor prediction extends only for coherent confident open-space motion", AdaptiveHorizon);
        yield return ("Actor spatial horizon uses reachable bounded correction trajectories", ReachableCorrection);
        yield return ("Actor spatial horizon proves an escape at 20 FPS within a shared observation budget", SlowFrameDetour);
        yield return ("Actor spatial guidance reverses and settles without copying target changes", GuidanceContinuity);
        yield return ("Actor spatial guidance rollback cannot secretly advance behind a retained shot", Rollback);
        yield return ("Actor spatial horizon rejects malformed queries and stays within world bounds", Validation);
    }

    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ActorSpatialHorizon New(double seconds = 1.5, double freedom = 1, AutonomyPosition offset = default)
    {
        var value = new ActorSpatialHorizon();
        value.Begin(10, 4, P(), new[] { P(z: 2), P(z: 4), P(z: 6) }, P(1), offset, freedom, seconds);
        return value;
    }
    private static void Complete(ActorSpatialHorizon value, Func<ActorSpatialProbe, bool> clear)
    {
        int attempts = 0;
        while (value.TryProbe(10.1, 4, out ActorSpatialProbe p))
        {
            Check(++attempts <= ActorSpatialHorizon.MaximumProbes, "Spatial work escaped its finite bound.");
            value.Observe(true, clear(p), 1);
        }
    }

    private static void Detour()
    {
        ActorSpatialHorizon value = New();
        Complete(value, p => p.Seconds < 1 || Math.Abs(p.Position.X) > .9);
        Check(value.Complete && value.Found, "The planner failed to join an observed lateral escape corridor.");
        Check(Math.Abs(value.FirstCorrection.X) > 1,
            "The future obstruction did not cause an early reachable lateral guide.");
    }

    private static void BlockedCorridors()
    {
        ActorSpatialHorizon value = New();
        Complete(value, p => p.Seconds < 1);
        Check(value.Complete && !value.Found, "Disconnected clear nodes produced a complete route.");
    }

    private static void UnknownEvidence()
    {
        ActorSpatialHorizon value = New();
        Check(value.TryProbe(10.01, 4, out ActorSpatialProbe first), "First probe missing.");
        Check(value.TryProbe(10.1, 4, out ActorSpatialProbe retained) && first.Position.Distance(retained.Position) == 0 &&
            first.From.Distance(retained.From) == 0 && first.Seconds == retained.Seconds && value.Probes == 0,
            "Deferred corridor changed endpoints or consumed unobserved proof.");
        value.Observe(false, true, 1);
        while (value.TryProbe(10.1, 4, out _)) value.Observe(false, true, 1);
        Check(value.Complete && !value.Found, "Unknown evidence admitted a route.");
    }

    private static void Staleness()
    {
        var old = New(); old.TryProbe(10.1, 4, out _);
        Check(!old.TryProbe(11.5, 4, out _) && !old.Found, "Expired observations remained executable.");
        var world = New(); world.TryProbe(10.1, 4, out _);
        Check(!world.TryProbe(10.1, 5, out _) && !world.Found, "Another world's observations remained executable.");
        var rewind = New();
        Check(!rewind.TryProbe(9, 4, out _) && !rewind.Found, "Clock reversal retained proof.");
    }

    private static void BoundedWork()
    {
        var clear = New(); Complete(clear, p => true);
        Check(clear.Found && clear.Probes <= ActorSpatialHorizon.MaximumProbes && clear.FirstCorrection.Distance(P()) < .001,
            "An unobstructed shot was perturbed or work became unbounded.");
        var blocked = New(); Complete(blocked, p => false);
        Check(!blocked.Found && blocked.Probes == ActorSpatialHorizon.Branches + 1,
            "An entirely blocked root expanded disconnected future layers.");
    }

    private static void ReplanContinuity()
    {
        var guidance = new ActorSpatialGuidance();
        for (int i = 0; i < 12; i++) guidance.Step(1d / 60, P(3), 1);
        AutonomyPosition offset = guidance.Offset, velocity = guidance.Velocity;
        var horizon = new ActorSpatialHorizon();
        horizon.Begin(10, 4, P(), new[] { P(z: 2), P(z: 4), P(z: 6) }, P(1), offset, 1, 1.5, velocity);
        Complete(horizon, p => true);
        Check(guidance.Offset.Distance(offset) == 0 && guidance.Velocity.Distance(velocity) == 0,
            "A planning round mutated the live anchor or its velocity.");
        guidance.Step(1d / 60, horizon.FirstCorrection, 1);
        Check(guidance.Velocity.Distance(velocity) <= 4d / 60 + .00001,
            "Adopting the next plan reset or snapped current movement.");
    }

    private static void AdaptiveHorizon()
    {
        double sustained = ActorSpatialHorizon.PredictionSeconds(false, false, 1, 1, 8);
        Check(Math.Abs(sustained - 4.5) < .001, "Sustained sprinting has no useful future horizon.");
        Check(ActorSpatialHorizon.PredictionSeconds(true, false, 1, 1, 8) <= 1.5 &&
            ActorSpatialHorizon.PredictionSeconds(false, true, 1, 1, 8) <= 1.5 &&
            ActorSpatialHorizon.PredictionSeconds(false, false, 0, 1, 8) <= 1.5,
            "A confined, combat, or changing direction scene inherited long-range certainty.");
        Check(ActorSpatialHorizon.PredictionSeconds(false, false, 1, .5, 8) < sustained &&
            ActorSpatialHorizon.PredictionSeconds(false, false, 1, 1, 20) <= 3,
            "Confidence or finite corridor reach did not shorten the forecast.");
        var longPlan = New(sustained); double farthest = 0;
        Complete(longPlan, p => { farthest = Math.Max(farthest, p.Seconds); return true; });
        Check(Math.Abs(farthest - sustained) < .001, "The adapter's horizon was ignored by timed search layers.");
    }

    private static void ReachableCorrection()
    {
        var value = New();
        while (value.TryProbe(10.1, 4, out ActorSpatialProbe p))
        {
            if (p.Seconds <= .5)
                Check(p.Correction.Distance(P()) <= .501,
                    "A first-layer sidestep exceeded what 4m/s² acceleration can reach in half a second.");
            value.Observe(true, true, 1);
        }
    }

    private static void GuidanceContinuity()
    {
        var value = new ActorSpatialGuidance();
        for (int i = 0; i < 1800; i++)
        {
            AutonomyPosition before = value.Offset, velocity = value.Velocity;
            AutonomyPosition goal = i < 180 ? P(3) : i < 360 ? P(-3) : P();
            value.Step(1d / 60, goal, 1);
            Check(value.Offset.Distance(before) <= 3d / 60 + .0001, "Guidance snapped position.");
            Check(value.Velocity.Distance(velocity) <= 4d / 60 + .002,
                "Guidance snapped velocity during reversal or ordinary stopping.");
            if (i == 180) Check(value.Offset.X > 0, "A new goal instantly teleported the camera.");
        }
        Check(value.Offset.Distance(P()) < .01 && value.Velocity.Distance(P()) < .01, "Guidance did not settle.");
    }

    private static void SlowFrameDetour()
    {
        var value = New();
        int remaining = 0, pendingCount = -1;
        double now = 10;
        while (value.Active && now - 10 < ActorSpatialHorizon.EvidenceLifetime)
        {
            int budget = 60;
            for (int work = 0; work < 6 && value.TryProbe(now, 4, out ActorSpatialProbe p); work++)
            {
                if (pendingCount != value.Probes)
                {
                    pendingCount = value.Probes;
                    remaining = 6 + (int)Math.Ceiling(p.Position.Distance(p.From) / .75) * 8;
                }
                int spent = Math.Min(budget, remaining); budget -= spent; remaining -= spent;
                if (remaining > 0) break;
                value.Observe(true, p.Seconds < 1 || Math.Abs(p.Position.X) > .9, 1);
            }
            now += 1d / 20;
        }
        Check(value.Found && Math.Abs(value.FirstCorrection.X) > 1,
            "Low-frame-rate spatial sampling starved a connected readable escape.");
        Check(value.Probes < 20, "The planner waited for exhaustive siblings despite an already complete readable route.");
    }

    private static void Rollback()
    {
        var value = new ActorSpatialGuidance();
        for (int i = 0; i < 60; i++) value.Step(1d / 60, P(2), 1);
        value.Reject(P(.2f));
        Check(value.Offset.Distance(P(.2f)) < .001 && value.Velocity.Distance(P()) == 0, "Rejected movement survived rollback.");
        value.Step(1d / 60, P(.2f), 1);
        Check(value.Offset.Distance(P(.2f)) < .001, "A retained anchor continued moving secretly.");
        value.Reset(); Check(value.Offset.Distance(P()) == 0 && value.Velocity.Distance(P()) == 0, "World reset retained guidance.");
    }

    private static void Validation()
    {
        bool threw = false;
        try { New(double.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "Nonfinite forecast duration accepted.");
        var boundary = new ActorSpatialHorizon();
        boundary.Begin(10, 4, P(19999), new[] { P(20000), P(20000), P(20000) }, P(1), P(), 1);
        Complete(boundary, p => true);
        Check(boundary.Complete, "World-boundary alternatives faulted rather than being rejected.");
    }
}
