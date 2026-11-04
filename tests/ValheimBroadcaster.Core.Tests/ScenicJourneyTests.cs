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
        yield return ("Journey windows use monotonic linear distance and compatible entry exit ramps", JourneyProfile);
        yield return ("Continuous Scenic ownership postpones editorial travel but not player acquisition", PlayerPreemption);
        yield return ("Continuous Scenic ownership never suppresses missing-picture recovery", RecoveryPreemption);
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
    private static void JourneyEvidence()
    {
        var policy = new ScenicJourneyPolicy(); policy.Start(1, 180);
        var plan = Plan(2).AsJourney(1, 1, 20);
        policy.Observe(plan, 0, 0, true, false);
        policy.Observe(plan, .1, .1, false, false); Near(policy.TravelMetres, 0);
        policy.Observe(plan, .2, .1, true, true); Near(policy.TravelMetres, 0);
        policy.Observe(plan, .3, .1, true, false); Near(policy.TravelMetres, 2); Near(policy.ShownSeconds, .1);
        policy.Observe(plan, .3, .2, true, false); Near(policy.TravelMetres, 2);
        policy.Observe(Plan(3).AsJourney(2, 1, 20), .4, .5, true, false); Near(policy.TravelMetres, 2);
        policy.Reset(); Check(!policy.Active && policy.TravelMetres == 0, "Reset retained journey evidence");
    }
    private static void JourneyBounds()
    {
        var policy = new ScenicJourneyPolicy(); policy.Start(1, 30);
        Near(policy.NextWindowMetres(1, 1), 30); Near(policy.NextWindowMetres(1, .5), 15);
        for (int i = 0; i < 500; i++) policy.NextWindowMetres(1, 1);
        Near(policy.ShownSeconds, 0);
        var plan = Plan(2).AsJourney(1, 1, 100);
        for (int i = 0; i <= 1810; i++) policy.Observe(plan, i / 60d, i / 6000d, true, false);
        Check(!policy.CanContinue && policy.NextWindowMetres(1, 1) == 0, "Journey exceeded finite editorial ownership");
        Check(!ScenicJourneyPolicy.Eligible(ScenicShotPlan.Picture(4, plan.Feature!, ScenicShotRole.Establish, P(0), P(0, 0, 0), 60)), "Recovery picture became a travel route");
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
