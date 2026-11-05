using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicJourneyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Incoming Scenic windows present their exact start before motion advances", SeamFirstFrame);
        yield return ("Slow Scenic playback completes its full route instead of the original airtime", SlowRoute);
        yield return ("Changing Scenic motion speed preserves accepted progress and full-route completion", Retiming);
        yield return ("Disabled Scenic movement uses an explicit stationary dwell reason", DisabledMotion);
        yield return ("Fixture retiming and re-identification preserve geometry and leave the source immutable", FixtureClone);
        yield return ("Fixture durations reject invalid and unbounded clocks", FixtureDurationBounds);
        yield return ("Journey windows retain finite ownership and distance only from matching live footage", JourneyEvidence);
        yield return ("Journey preparation never earns progress or extends its bounded lifetime", JourneyBounds);
        yield return ("A distance-directed journey covers one kilometre at its own cruise speed at 30 and 60 fps", KilometreJourney);
        yield return ("Journey lookahead scales with speed while live steps remain within validated sampling", SpeedAndLookahead);
        yield return ("Thirty-two metres of blocked travel can never be reported as a completed kilometre", IncompleteOutcome);
        yield return ("Journey distance is measured from accepted poses rather than authored progress", ActualDistance);
        yield return ("Looping travel does not satisfy the forward journey objective", LoopingTravel);
        yield return ("A relocation hidden by held missing rejected or stale footage cannot fulfill a journey", UnobservedGoalAdvance);
        yield return ("Journey planning excludes distance already committed to its current route", CommittedDistance);
        yield return ("Journey windows use monotonic linear distance and compatible entry exit ramps", JourneyProfile);
        yield return ("Authored journey fallback preserves its view offset without changing ordinary scenic aim easing", AuthoredJourneyAim);
        yield return ("A salvaged scenic endpoint cannot become a journey into its known rejected tail", RejectedDeparture);
        yield return ("Continuous Scenic ownership postpones editorial travel but not player acquisition", PlayerPreemption);
        yield return ("Continuous Scenic ownership never suppresses missing-picture recovery", RecoveryPreemption);
        yield return ("Manual Journey preparation postpones voluntary relocation but preserves players recovery and expiry", ManualDeparturePriority);
        yield return ("Bounded fixture preparation defers scenic recovery only until its owner expires", FixturePreparation);
        yield return ("Fixture ownership does not depend on destination-subject feedback", FixtureWithoutDestination);
    }

    private static AutonomyPosition P(float x, float y = 4, float z = -20) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Plan(long id = 1) => ScenicShotPlan.Spatial(id,
        new ScenicFeature("coast", ScenicFeatureKind.Coastline, P(10, 2, 0), P(1, 0, 0), 8, 5, 1),
        ScenicShotRole.Traverse, new[] { P(0), P(10), P(20) }, new[] { P(0, 2, 0), P(10, 2, 0), P(20, 2, 0) }, 65);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double a, double b, double tolerance = .0001) => Check(Math.Abs(a - b) <= tolerance, $"Expected {a} near {b}");
    private static void Reject(Action call) { try { call(); } catch (ArgumentException) { return; } throw new Exception("Invalid value was accepted"); }

    private static void RejectedDeparture()
    {
        var original = Plan();
        Check(ScenicJourneyPolicy.Eligible(original), "Ordinary unblocked natural routes lost journey eligibility");
        var clipped = original.WithRejectedContinuation("Focus hidden immediately beyond the retained prefix");
        Check(!ScenicJourneyPolicy.Eligible(clipped) && !original.HasRejectedContinuation, "Known rejected tail was extended or source was mutated");
        foreach (var copy in new[] { clipped.WithIdentity(3), clipped.WithDuration(120), clipped.WithConstantSpeed(),
            clipped.AsStandalone(), clipped.RaiseCamera(2), clipped.Slice(.1, 1) })
            Check(copy.HasRejectedContinuation && copy.ContinuationRejection == clipped.ContinuationRejection,
                "Retiming, re-identification, fitting or endpoint-preserving slicing erased boundary evidence");
        var intentionalPrefix = clipped.Slice(0, .5);
        Check(!intentionalPrefix.HasRejectedContinuation && ScenicJourneyPolicy.Eligible(intentionalPrefix),
            "An intentionally earlier fixture endpoint inherited a rejected tail it no longer reaches");
        Reject(() => original.WithRejectedContinuation(" "));
    }

    private static void SeamFirstFrame()
    {
        foreach (double dt in new[] { 1d / 30, 1d / 60 })
        {
            var d = new ScenicDirector(); var plan = Plan(2).AsJourney(1, 1, 20); d.Accept(plan, 0);
            Near(d.Propose(dt), 0);
            d.Observe(plan.Id, 0, 0, false, false, false); Near(d.Propose(dt), 0);
            d.Observe(plan.Id, dt, 0, true, true, false);
            Check(d.Propose(dt) > 0, "A matching start frame did not release continuous movement");
        }
    }
    private static void SlowRoute()
    {
        var plan = Plan(); var d = new ScenicDirector(); d.Accept(plan, 0); d.Observe(plan.Id, 0, 0, true, true, false);
        for (int frame = 1; frame <= (int)Math.Ceiling(plan.DurationSeconds * 120); frame++)
        {
            double progress = d.Propose(1d / 60, true, .5f);
            d.Observe(plan.Id, frame / 60d, progress, true, true, false);
            if (frame / 60d < plan.DurationSeconds * 1.9) Check(!d.NeedsReplacement, "Reduced speed truncated a still-useful route");
        }
        Near(d.Progress, 1); Check(d.NeedsReplacement && d.Reason.Contains("route completed"), "Full path completion was not reported");
    }
    private static void Retiming()
    {
        var d = new ScenicDirector(); var plan = Plan(); d.Accept(plan, 0); d.Observe(plan.Id, 0, 0, true, true, false);
        for (int i = 1; i <= 600; i++)
        {
            float speed = i < 120 ? 1 : .25f;
            double before = d.Progress, next = d.Propose(1d / 60, true, speed);
            Check(next >= before && next - before < .002, "Retiming jumped the route");
            d.Observe(plan.Id, i / 60d, next, true, true, false);
        }
        Check(!d.NeedsReplacement && d.Progress < 1, "Retiming prematurely expired the movement");
    }
    private static void DisabledMotion()
    {
        var d = new ScenicDirector(); var plan = Plan().WithDuration(4); d.Accept(plan, 0);
        for (int i = 0; i <= 241; i++) d.Observe(plan.Id, i / 60d, 0, true, true, false, movementEnabled: false);
        Check(d.NeedsReplacement && d.Reason.Contains("Stationary") && d.Progress == 0, "Stationary dwell claimed completed motion");
    }
    private static void FixtureClone()
    {
        var plan = Plan(); var copy = plan.WithDuration(3600).WithIdentity(7).WithConstantSpeed();
        Check(copy.Id == 7 && plan.Id == 1 && copy.DurationSeconds == 3600 && plan.DurationSeconds != 3600, "Clone mutated source");
        Near(copy.Sample(0).CameraPosition.Distance(plan.Sample(0).CameraPosition), 0);
        Near(copy.Sample(1).CameraPosition.Distance(plan.Sample(1).CameraPosition), 0);
        var journey = copy.AsJourney(1, 2, 60).WithJourneyRamps(true, false);
        var standalone = journey.AsStandalone();
        Check(!standalone.IsJourney && standalone.ContinuationOf == 0 && standalone.ConstantSpeed && standalone.JourneyEaseIn, "Standalone fixture lost its exact profile or retained another session owner");
    }
    private static void FixtureDurationBounds()
    {
        foreach (double value in new[] { 0d, -1, double.NaN, double.PositiveInfinity, 604801 }) Reject(() => Plan().WithDuration(value));
        Near(Plan().WithDuration(604800).DurationSeconds, 604800);
    }
    private static ScenicJourneyPolicy Journey(double target = 1000, double speed = 6)
    { var p = new ScenicJourneyPolicy(); p.Start(1, target, speed, P(0), new AutonomyPosition(1, 0, 0)); return p; }
    private static void JourneyEvidence()
    {
        var policy = Journey(); var plan = Plan(2).AsJourney(1, 1, 20);
        policy.Observe(plan, 0, 0, true, false, P(0));
        policy.Observe(plan, .1, .1, false, false, P(.6f)); Near(policy.TravelMetres, 0);
        policy.Observe(plan, .2, .1, true, true, P(.6f)); Near(policy.TravelMetres, 0);
        policy.Observe(plan, .3, .1, true, false, P(.6f)); Near(policy.TravelMetres, 0);
        policy.Observe(plan, .4, .2, true, false, P(1.2f)); Near(policy.TravelMetres, .6); Near(policy.ShownSeconds, .1);
        policy.Observe(plan, .4, .3, true, false, P(1.8f)); Near(policy.TravelMetres, .6);
        policy.Observe(Plan(3).AsJourney(2, 1, 20), .5, .5, true, false, P(2)); Near(policy.TravelMetres, .6);
        policy.Reset(); Check(!policy.Active && policy.TravelMetres == 0, "Reset retained journey evidence");
    }
    private static void JourneyBounds()
    {
        var policy = Journey(256, 2);
        Near(policy.NextWindowMetres(), 32);
        for (int i = 0; i < 500; i++) policy.NextWindowMetres();
        Near(policy.ShownSeconds, 0); Near(policy.TravelMetres, 0);
        var plan = Plan(2).AsJourney(1, 1, 100);
        for (int i = 0; i <= 15000; i++) policy.Observe(plan, i / 60d, 0, true, false, P(0));
        Check(policy.BudgetExpired && !policy.CanContinue && policy.NextWindowMetres() == 0, "Motionless shown footage retained unlimited journey ownership");
        Check(!ScenicJourneyPolicy.Eligible(ScenicShotPlan.Picture(4, plan.Feature!, ScenicShotRole.Establish, P(0), P(0, 0, 0), 60)), "Recovery picture became a travel route");
        Reject(() => Journey(double.NaN)); Reject(() => Journey(255)); Reject(() => Journey(5001)); Reject(() => Journey(1000, 1));
    }
    private static void KilometreJourney()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var policy = Journey(); double at = 0, position = 0; long id = 1;
            while (policy.CanContinue)
            {
                double length = policy.NextWindowMetres();
                Check(length >= 8 && length <= 64, "Invalid local window for a developed journey");
                var plan = ScenicShotPlan.Spatial(++id,
                    new ScenicFeature("coast", ScenicFeatureKind.Coastline, P((float)position, 2, 0), P(1, 0, 0), 8, 5, 1),
                    ScenicShotRole.Traverse, new[] { P((float)position), P((float)(position + length / 2)), P((float)(position + length)) },
                    new[] { P((float)position, 2, 0), P((float)(position + length / 2), 2, 0), P((float)(position + length), 2, 0) }, 65)
                    .AsJourney(1, id - 1, length / policy.CruiseSpeed).WithJourneyRamps(false, false);
                var director = new ScenicDirector(); director.Accept(plan, at); director.Observe(plan.Id, at, 0, true, true, false);
                policy.Observe(plan, at, 0, true, false, P((float)position));
                while (director.Progress < .999999)
                {
                    double dt = ScenicJourneyPolicy.SafeDeltaSeconds(1d / fps, policy.CruiseSpeed);
                    double progress = director.Propose(dt);
                    at += 1d / fps;
                    director.Observe(plan.Id, at, progress, true, true, false);
                    AutonomyPosition actual = plan.Sample(progress).CameraPosition;
                    policy.Observe(plan, at, progress, true, false, actual);
                    Check(policy.Active, "A valid moving journey was interrupted");
                }
                position += length;
                Check(at < 180, "One kilometre at six metres per second took longer than its cinematic target");
            }
            Check(policy.TargetReached && policy.TravelMetres >= 999.5 && policy.DisplacementMetres >= 999.5,
                "The completed journey did not actually traverse one kilometre");
            Check(policy.AchievedMetresPerSecond > 5.9 && policy.AchievedMetresPerSecond <= 6.01,
                "Achieved speed did not match the independently configured cruise speed");
            policy.Finish(ScenicJourneyOutcome.Completed, "Distance objective reached");
            Check(policy.Outcome == ScenicJourneyOutcome.Completed && !policy.Active && policy.TravelMetres >= 999.5,
                "Final result lost the measured kilometre");
        }
    }
    private static void SpeedAndLookahead()
    {
        foreach (double speed in new[] { 2d, 4, 6, 8, 12 })
        {
            double window = ScenicJourneyPolicy.WindowMetresForSpeed(speed);
            Check(window >= 32 && window <= 64, "A single continuation exceeded locally certifiable geometry");
            Near(ScenicJourneyPolicy.RequiredLookaheadMetres(speed) / speed, 12);
            int sections = (int)Math.Ceiling(ScenicJourneyPolicy.RequiredLookaheadMetres(speed) / window);
            Check(sections * window >= speed * 12, "The queue horizon is too short for its preparation interval");
            foreach (double dt in new[] { 1d / 60, 1d / 30, .1, .5 })
                Check(ScenicJourneyPolicy.SafeDeltaSeconds(dt, speed) * speed <= .750001,
                    "High cruise speed proposed more movement than live corridor sampling can validate");
        }
        Reject(() => ScenicJourneyPolicy.SafeDeltaSeconds(double.NaN, 6));
    }
    private static void IncompleteOutcome()
    {
        var policy = Journey(); var plan = Plan(2).AsJourney(1, 1, 20);
        for (int i = 0; i <= 64; i++) policy.Observe(plan, i / 12d, Math.Min(1, i / 64d), true, false, P(i * .5f));
        Near(policy.TravelMetres, 32); Check(!policy.TargetReached, "A short local segment achieved a kilometre objective");
        policy.Finish(ScenicJourneyOutcome.Completed, "Planner could not continue");
        Check(policy.Outcome == ScenicJourneyOutcome.Blocked && policy.Reason.Contains("not reached") && policy.TravelMetres == 32,
            "Failed continuation was mislabeled completed or its achieved distance discarded");
    }
    private static void ActualDistance()
    {
        var policy = Journey(); var plan = Plan(2).AsJourney(1, 1, 20);
        policy.Observe(plan, 0, 0, true, false, P(0));
        policy.Observe(plan, .1, 1, true, false, P(.5f));
        Near(policy.TravelMetres, .5); // Authored progress would falsely claim20m.
        policy.Observe(plan, .2, 1, true, false); Near(policy.TravelMetres, .5);
        policy.Observe(plan, .3, 1, true, false, P(1)); Near(policy.TravelMetres, .5);
        policy.Observe(plan, .4, 1, true, false, P(100));
        Check(policy.Outcome == ScenicJourneyOutcome.Interrupted && policy.TravelMetres == .5,
            "A teleport was counted as a cinematic journey");
    }
    private static void LoopingTravel()
    {
        var policy = Journey(256); var plan = Plan(2).AsJourney(1, 1, 200);
        for (int i = 0; i <= 600; i++)
        {
            float x = (i % 40 <= 20 ? i % 40 : 40 - i % 40) * .5f;
            policy.Observe(plan, i / 30d, i / 600d, true, false, P(x));
        }
        Check(policy.TravelMetres >= 256 && !policy.TargetReached && policy.ObjectiveAdvanceMetres < 20,
            "Repeated local travel was reported as a developed distance objective");
        policy.Finish(ScenicJourneyOutcome.Completed, "Loop ended");
        Check(policy.Outcome == ScenicJourneyOutcome.Blocked, "A loop can be called a completed journey");
    }
    private static void CommittedDistance()
    {
        var policy = Journey(256); var plan = Plan(2).AsJourney(1, 1, 200);
        for (int i = 0; i <= 400; i++) policy.Observe(plan, i / 30d, i / 400d, true, false, P(i * .5f));
        Near(policy.NextWindowMetres(56), 0);
        Near(policy.NextWindowMetres(30), 26);
        Near(policy.NextWindowMetres(52), 8);
        Reject(() => policy.NextWindowMetres(-1));
    }
    private static void UnobservedGoalAdvance()
    {
        for (int gapKind = 0; gapKind < 4; gapKind++)
        {
            var policy = Journey(256); var plan = Plan(2).AsJourney(1, 1, 200);
            for (int i = 0; i <= 600; i++)
            {
                float x = (i % 40 <= 20 ? i % 40 : 40 - i % 40) * .5f;
                policy.Observe(plan, i / 30d, i / 600d, true, false, P(x));
            }
            if (gapKind < 3)
                policy.Observe(plan, 20.1, 1, gapKind != 2, gapKind == 0,
                    gapKind == 1 ? (AutonomyPosition?)null : P(300));
            policy.Observe(plan, 20.4, 1, true, false, P(300));
            Near(policy.TravelMetres, 300); Near(policy.ObjectiveAdvanceMetres, 300);
            Check(!policy.TargetReached && policy.RemainingMetres > 160,
                "Unshown relocation supplied the missing forward objective");
            policy.Finish(ScenicJourneyOutcome.Completed, "Resumed beyond the objective");
            Check(policy.Outcome == ScenicJourneyOutcome.Blocked, "A hidden relocation completed the journey");
        }
    }
    private static void JourneyProfile()
    {
        foreach (bool entry in new[] { false, true }) foreach (bool exit in new[] { false, true })
        {
            Near(ScenicJourneyMotion.Progress(0, entry, exit), 0); Near(ScenicJourneyMotion.Progress(1, entry, exit), 1);
            double before = 0;
            for (int i = 1; i <= 10000; i++)
            { double next = ScenicJourneyMotion.Progress(i / 10000d, entry, exit); Check(next >= before && next - before < .00012, "Journey profile reverses or jumps"); before = next; }
            double firstSpeed = ScenicJourneyMotion.Progress(.00001, entry, exit) / .00001 * ScenicJourneyMotion.CruiseFraction(entry, exit);
            double lastSpeed = (1 - ScenicJourneyMotion.Progress(.99999, entry, exit)) / .00001 * ScenicJourneyMotion.CruiseFraction(entry, exit);
            Near(firstSpeed, entry ? 0 : 1, .001); Near(lastSpeed, exit ? 0 : 1, .001);
        }
    }
    private static void AuthoredJourneyAim()
    {
        var body = new[] { P(0, 12, -20), P(8, 14, -18), P(40, 17, -12) };
        var aim = new AutonomyPosition[body.Length];
        for (int i = 0; i < body.Length; i++) aim[i] = P(body[i].X + 3, body[i].Y - 2, body[i].Z + 20);
        var ordinary = ScenicShotPlan.Spatial(2, Plan().Feature!, ScenicShotRole.Traverse, body, aim, 65,
            useNativeSpline: false).WithConstantSpeed();
        var journey = ordinary.AsJourney(1, 1, ordinary.TravelMetres / 6).WithJourneyRamps(false, false);
        Check(!journey.UseNativeSpline, "Fallback case unexpectedly uses native body execution");
        for (int i = 0; i <= 200; i++)
        {
            ScenicShotSample actual = journey.Sample(i / 200d);
            Near(actual.AimPosition.X - actual.CameraPosition.X, 3);
            Near(actual.AimPosition.Y - actual.CameraPosition.Y, -2);
            Near(actual.AimPosition.Z - actual.CameraPosition.Z, 20);
        }
        // At one quarter of the first leg the ordinary route keeps its original
        // smoothstep aim, while a journey advances its aim with the body.
        double progress = ordinary.WaypointFraction(1) * .25;
        ScenicShotSample original = ordinary.Sample(progress);
        Near(original.CameraPosition.X, 2);
        Near(original.AimPosition.X, 3 + 8 * .15625);
        Check(Math.Abs(original.AimPosition.X - original.CameraPosition.X - 3) > .5,
            "Journey aim behavior leaked into ordinary scenic composition");
    }
    private static AutonomousDirector Automatic()
    { var d = new AutonomousDirector { RegionSeconds = 1, MinimumTravelSeconds = 1, MinimumScenicTravelDistance = 100 }; d.SetEnabled(true); return d; }
    private static ScenicRegion[] Regions() => new[] { new ScenicRegion("near", "near", P(0)), new ScenicRegion("far", "far", P(1000)) };
    private static void PlayerPreemption()
    {
        var d = Automatic();
        for (int i = 0; i < 20; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true, scenicJourneyActive: true);
        Check(d.PendingTravel == null, "Ordinary region dwell cut a continuous journey");
        d.Tick(20, 1, new[] { new AutonomyPlayer("p", "Player", P(0), 100, true) }, Regions(), P(0), "p", true, scenicJourneyActive: true);
        Check(d.TargetId == "p", "Journey suppressed a newly available player");
    }
    private static void ManualDeparturePriority()
    {
        var waiting = Automatic();
        for (int i = 0; i < 20; i++)
            waiting.Tick(i, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true,
                scenicContentExhausted: true, scenicJourneyPending: true);
        Check(waiting.PendingTravel == null && waiting.State == AutonomyState.Scenic && waiting.Reason.Contains("Preparing"),
            "Ordinary dwell or novelty exhaustion cancelled the bounded requested departure");
        waiting.Tick(20, 1, new[] { new AutonomyPlayer("p", "Player", P(0), 100, true) }, Regions(), P(0), "p", true,
            scenicJourneyPending: true);
        Check(waiting.TargetId == "p", "Pending manual Journey blocked available player coverage");

        var recovery = Automatic();
        recovery.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true, scenicJourneyPending: true);
        recovery.Tick(20, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, false,
            idleSearchExpired: true, scenicJourneyPending: true);
        Check(recovery.PendingTravel != null, "Pending manual Journey suppressed actual missing-picture recovery");

        var expired = Automatic();
        expired.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true, scenicJourneyPending: true);
        expired.Tick(20, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true, scenicJourneyPending: false);
        Check(expired.PendingTravel != null, "An expired or stopped manual request kept owning voluntary travel");
    }
    private static void FixturePreparation()
    {
        var d = Automatic();
        d.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true);
        d.Tick(20, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, false, idleSearchExpired: true, scenicPreparationActive: true);
        Check(d.PendingTravel == null, "A bounded explicit fixture preparation was preempted by idle travel");
        d.Tick(21, 1, new[] { new AutonomyPlayer("p", "Player", P(0), 100, true) }, Regions(), P(0), "p", true, scenicPreparationActive: true);
        Check(d.TargetId == "p", "Fixture preparation blocked player acquisition");
        var expired = Automatic(); expired.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true);
        expired.Tick(20, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, false, idleSearchExpired: true, scenicPreparationActive: true);
        expired.Tick(21, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, false, idleSearchExpired: true);
        Check(expired.PendingTravel != null, "Expired fixture ownership suppressed normal recovery");
    }
    private static void FixtureWithoutDestination()
    {
        var d = Automatic();
        for (int i = 0; i < 20; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true,
            scenicFootageReady: false, scenicJourneyActive: true);
        Check(d.PendingTravel == null, "An old nominated destination interrupted a readable explicit fixture");
    }
    private static void RecoveryPreemption()
    {
        var d = Automatic(); d.Tick(0, 0, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, true, scenicJourneyActive: true);
        d.Tick(20, 1, Array.Empty<AutonomyPlayer>(), Regions(), P(0), null, false, idleSearchExpired: true, scenicJourneyActive: true);
        Check(d.PendingTravel != null, "Journey suppressed missing-picture recovery");
    }
}
