using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyRunwayTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey startup refuses the old single 48 metre section and uses measured two-section reserve", StartupReserve);
        yield return ("Journey proposals fit future gaze and remaining loading reach instead of waiting for a fixed window", LoadingFootprint);
        yield return ("Journey preparation history counts complete jobs and rejects duplicate or reversed completion timestamps", MeasuredLatency);
        yield return ("Journey reserve responds to actual slow preparation without promising unsupported cruise", SlowProducer);
        yield return ("Journey cruise changes smoothly and hold or committed braking cannot cause catch-up acceleration", SmoothCruise);
        yield return ("A stalled Journey producer reaches a finite ending rather than crawling forever", FiniteStall);
        yield return ("Connected Journey reserve sustains a kilometre with nominal, seven-second and variable production at 30 and 60 fps", DelayedKilometre);
        yield return ("Journey queue heads require exact predecessor identity and fresh complete evidence", QueueIdentity);
        yield return ("Journey runway state does not survive owner reset and rejects nonfinite input", InvalidAndReset);
        yield return ("Journey runtime budget covers its declared minimum cruise but remains finite", RuntimeBudget);
    }

    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Near(double actual, double expected, double epsilon = .000001) =>
        Check(Math.Abs(actual - expected) <= epsilon, $"Expected {expected}, got {actual}");
    private static AutonomyPosition P(float x, float z = 0) => new AutonomyPosition(x, 20, z);
    private static ScenicShotPlan Section(long id, long previous = 0, float start = 0, float length = 32)
    {
        var feature = new ScenicFeature("route", ScenicFeatureKind.Landscape, P(start + length / 2, 20), P(1), 20, 5, .9f);
        var plan = ScenicShotPlan.Spatial(id, feature, ScenicShotRole.Traverse,
            new[] { P(start), P(start + length / 2), P(start + length) },
            new[] { P(start + 21), P(start + length / 2 + 21), P(start + length + 21) }, 65);
        return previous == 0 ? plan.AsJourneyDeparture(7, length / 6d) : plan.AsJourney(7, previous, length / 6d);
    }

    private static void StartupReserve()
    {
        // The old first48m at6m/s reaches its12m braking boundary at
        // t=6s, before a perfectly finite7s continuation job can finish.
        Check(JourneyHandoffPolicy.ShouldBrake(false, 48 - 6 * 6, 6, false, false),
            "The original48m producer deadline regression was not represented");
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(5);
        Check(!policy.TryStart(1, 48, 6, out _) && !policy.Started,
            "The original eight-second single-section launch still acquired ownership");
        Check(!policy.TryStart(2, 16, 6, out _) && !policy.Started,
            "Two tiny sections manufactured a useful startup reserve");
        Check(policy.TryStart(2, 64, 6, out _) && policy.Started, "Two certified sections failed to provide usable runway");
        Near(policy.EffectiveCruise, 6);
        Near(policy.RequiredRunwaySeconds, 9.75);
    }

    private static void LoadingFootprint()
    {
        // At 6m/s the old body-only test accepted 48m from a seam40m away,
        // even though its focus projects another21m beyond the route.
        Near(JourneyRunwayPolicy.WindowMetres(6, 40, 48), 24);
        Near(JourneyRunwayPolicy.WindowMetres(6, 57, 48), 0);
        foreach (double speed in new[] { 2d, 6, 12 })
        for (int seam = 0; seam <= 100; seam++)
        {
            double window = JourneyRunwayPolicy.WindowMetres(speed, seam, 64);
            Check(window == 0 || window >= 8 && window <= 64 &&
                seam + window + JourneyRunwayPolicy.GazeReach(speed) <= 85.000001,
                "Proposal exceeded its full camera/focus loading footprint");
        }
        Near(JourneyRunwayPolicy.WindowMetres(2, 0, 1000), 64);
        Near(JourneyRunwayPolicy.WindowMetres(6, 10, 7.9), 0);
    }

    private static void MeasuredLatency()
    {
        var policy = new JourneyRunwayPolicy();
        Check(policy.ObservePreparation(10, 16), "A real completed route+ending job was ignored");
        Near(policy.PreparationSeconds, 8);
        for (int i = 0; i < 100; i++) Check(!policy.ObservePreparation(15, 16), "Polling completed work replaced measured history");
        Check(!policy.ObservePreparation(20, 19) && !policy.ObservePreparation(10, 15), "Reversed completion clocks were accepted");
        Check(policy.Samples == 1, "Invalid clocks multiplied producer evidence");
        for (int i = 0; i < 7; i++) policy.ObservePreparation(2);
        Near(policy.PreparationSeconds, 8); Check(policy.Samples == 8, "History is not bounded");
        policy.ObservePreparation(2); Near(policy.PreparationSeconds, 3);
        Check(policy.Samples == 8, "Old measurements were retained without bound");
    }

    private static void SlowProducer()
    {
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(7);
        Check(policy.TryStart(2, 64, 6, out _) && policy.EffectiveCruise < 6 && policy.EffectiveCruise >= 3,
            "Observed slow production either promised full cruise or forced an unusable crawl");
        var impossible = new JourneyRunwayPolicy(); impossible.ObservePreparation(30);
        Check(!impossible.TryStart(2, 64, 6, out _) && !impossible.Started,
            "A service time exceeding local reserve was hidden behind a near-zero launch speed");
        foreach (double speed in new[] { 2d, 6, 12 })
        {
            var bounded = new JourneyRunwayPolicy(); bounded.ObservePreparation(2);
            Check(bounded.TryStart(2, 64, speed, out _), "Feasible startup was rejected");
            Check(bounded.EffectiveCruise >= JourneyRunwayPolicy.MinimumCruise(speed) && bounded.EffectiveCruise <= speed,
                "Startup cruise exceeded the explicit useful range");
        }
    }

    private static void SmoothCruise()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(2); policy.TryStart(2, 64, 6, out _);
            double dt = 1d / fps;
            for (int i = 0; i < fps * 5; i++)
            {
                double before = policy.EffectiveCruise, after = policy.StepSpeed(6, 12, dt);
                Check(before - after <= JourneyRunwayPolicy.Deceleration * dt + .000001 && after >= 3,
                    "Falling runway caused a speed jump or indefinite crawl");
            }
            Near(policy.EffectiveCruise, 3);
            Near(policy.StepSpeed(6, 80, 100, held: true), 3);
            Near(policy.StepSpeed(6, 80, 100, braking: true), 3);
            double resumed = policy.StepSpeed(6, 80, 100);
            Check(resumed > 3 && resumed <= 3.1, "A delayed update accumulated catch-up acceleration");
            for (int i = 0; i < fps * 4; i++)
            {
                double before = policy.EffectiveCruise, after = policy.StepSpeed(6, 80, dt);
                Check(after - before <= JourneyRunwayPolicy.Acceleration * dt + .000001 && after <= 6,
                    "New validated reserve caused an acceleration surge");
            }
            Near(policy.EffectiveCruise, 6);
        }
    }

    private static void FiniteStall()
    {
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(4); policy.TryStart(2, 64, 6, out _);
        double remaining = 64, elapsed = 0; bool committed = false;
        while (!committed)
        {
            double speed = policy.StepSpeed(6, remaining, .05);
            committed = JourneyHandoffPolicy.ShouldBrake(false, remaining, speed, false, false);
            if (!committed) remaining -= speed * .05;
            elapsed += .05;
            Check(elapsed < 25 && remaining > 0, "Missing producer led to an endless crawl or unvalidated motion");
        }
        Check(remaining >= 4 && remaining <= 12, "Stalled producer did not leave a finite braking reserve");
        double speedAtCommit = policy.EffectiveCruise;
        policy.StepSpeed(6, 100, .1, braking: true);
        Check(policy.EffectiveCruise <= speedAtCommit, "Late producer evidence cancelled committed deceleration");
    }

    private static void DelayedKilometre()
    {
        // A deterministic producer fixture, not a Unity geometry simulation.
        // Every section becomes available only after its delayed route+ending
        // completion; no speculative metres count toward reserve or playback.
        foreach (int fps in new[] { 30, 60 })
        foreach (double[] latencies in new[] { new[] { 3.5 }, new[] { 7d }, new[] { 3.5, 5, 7, 4, 6 } })
        {
            var policy = new JourneyRunwayPolicy();
            policy.ObservePreparation(latencies[0]); policy.ObservePreparation(latencies[1 % latencies.Length]);
            Check(policy.TryStart(2, 64, 6, out _), "Initial two-section reserve unavailable");
            var ends = new Queue<double>(); ends.Enqueue(32); ends.Enqueue(64);
            double position = 0, tail = 64, now = 0, pendingEnd = 0, readyAt = -1, serviceSeconds = 0;
            int completed = 2, handoffs = 0;
            double dt = 1d / fps;
            while (position < 1000)
            {
                if (readyAt >= 0 && now >= readyAt)
                {
                    ends.Enqueue(pendingEnd); tail = pendingEnd; readyAt = -1; completed++;
                    policy.ObservePreparation(serviceSeconds);
                }
                while (ends.Count > 0 && position >= ends.Peek() - .000001)
                { ends.Dequeue(); handoffs++; }
                Check(ends.Count > 0, "Delayed production exhausted the certified connected path");
                // The actual carrier can lead into a certified connected next
                // section, but never into the route still being prepared.
                double carrier = Math.Min(tail, position + 24);
                if (readyAt < 0 && ends.Count < 3)
                {
                    double length = JourneyRunwayPolicy.WindowMetres(6, Math.Max(0, tail - carrier), 48);
                    if (length >= 8)
                    {
                        serviceSeconds = latencies[completed % latencies.Length];
                        pendingEnd = tail + length; readyAt = now + serviceSeconds;
                    }
                }
                double speed = policy.StepSpeed(6, tail - position, dt);
                bool nextReady = ends.Count >= 2;
                Check(!JourneyHandoffPolicy.ShouldBrake(false, ends.Peek() - position, speed, nextReady, false),
                    $"Producer failed the next handoff at {now:0.00}s/{position:0.0}m, latency {serviceSeconds}, {fps}fps");
                double step = Math.Min(speed * dt, Math.Min(1000 - position, ends.Peek() - position));
                Check(step >= 0 && position + step <= tail + .000001,
                    "The fixture consumed speculative path length before its validation completed");
                position += step; now += dt;
                Check(now < 350, "Sustained programme became an indefinite slow journey");
            }
            Check(completed > 20 && handoffs > 20, "Kilometre fixture bypassed actual producer/consumer handoffs");
            Check(now >= 1000d / 6 - .1, "Travel exceeded the requested cruise ceiling");
        }
    }

    private static void QueueIdentity()
    {
        var departure = Section(10);
        var child = Section(11, 10);
        Check(JourneyRunwayPolicy.HeadMatches(departure, 0, 7, 3, 5, 3, 6),
            "The independent certified departure could not begin the startup reserve chain");
        Check(JourneyRunwayPolicy.HeadMatches(child, 10, 7, 3, 5, 3, 6), "Fresh connected queue head was rejected");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 0, 7, 3, 5, 3, 6), "A successor was admitted as an independent departure");
        Check(!JourneyRunwayPolicy.HeadMatches(departure, 10, 7, 3, 5, 3, 6), "An independent departure supplied a connected successor");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 9, 7, 3, 5, 3, 6), "An orphan successor postponed braking");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 10, 8, 3, 5, 3, 6), "A different Journey supplied reserve");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 10, 7, 3, 5, 4, 6), "Scene-invalidated geometry supplied reserve");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 10, 7, 3, 5, 3, 18), "Old queued geometry stayed admissible at low cruise");
        Check(JourneyRunwayPolicy.HeadMatches(child, 10, 7, 3, 17, 3, 18), "An actual refreshed proof could not restore the same route");
        Check(!JourneyRunwayPolicy.HeadMatches(child, 10, 7, 3, 19, 3, 18), "A future timestamp supplied proof");
    }

    private static void InvalidAndReset()
    {
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(4); policy.TryStart(2, 64, 6, out _); policy.Reset();
        Check(!policy.Started && policy.Samples == 0 && policy.EffectiveCruise == 0, "A new Journey inherited another owner's timing state");
        foreach (double value in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { policy.ObservePreparation(value); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid preparation duration entered the latency estimator");
        }
        bool premature = false;
        try { policy.StepSpeed(6, 60, .1); } catch (InvalidOperationException) { premature = true; }
        Check(premature, "Cruise advanced before a certified startup reserve was admitted");
    }

    private static void RuntimeBudget()
    {
        var destination = new ScenicRegion("headland", "Headland", P(1000));
        Check(ScenicJourneyItinerary.TrySelect(P(0), P(1), new[] { destination }, null, 1000, 10, null,
            out var itinerary, out _), "Known destination fixture was unavailable");
        var journey = new ScenicJourneyPolicy(); journey.Start(7, itinerary!, 6);
        Check(journey.MaximumSeconds >= 1000 / JourneyRunwayPolicy.MinimumCruise(6) + 6 && journey.MaximumSeconds < 600,
            "Runtime budget expires before sustained declared minimum cruise or lost its finite bound");
        double now = 0, position = 0; long id = 10;
        var section = Section(id);
        journey.Observe(section, now, 0, true, false, P(0));
        while (position < 990)
        {
            now += .1; position += .3;
            if (position >= (id - 9) * 32) { id++; section = Section(id, id - 1, (id - 10) * 32); }
            journey.Observe(section, now, Math.Min(1, Math.Max(0, (position - (id - 10) * 32) / 32)), true, false, P((float)position));
            Check(!journey.BudgetExpired, "Continuous legitimate minimum-speed travel exhausted a nominal-speed deadline");
        }
        Check(journey.TravelMetres > 989 && journey.ShownSeconds > 300, "Budget regression did not exercise slower real presentation");
    }
}
