using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyRunwayTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Slow production reduces cruise ahead of the physical braking boundary", AnticipateSupply);
        yield return ("One certified Journey route with a safe ending launches on physical reserve rather than a fixed section count", StartupReserve);
        yield return ("Journey proposal windows include inherited gaze and the observed loading horizon", LoadingFootprint);
        yield return ("Successful Journey producer observations measure metres per second without duplicate completion credit", MeasuredProduction);
        yield return ("Slow successful preparation adapts useful cruise without creating a startup deadlock", SlowProducer);
        yield return ("Journey braking follows stopping distance and speed changes remain bounded", SmoothCruise);
        yield return ("Exhausted certified runway reaches a real stop rather than indefinite subminimum crawling", FiniteStop);
        yield return ("Stopped Journey can resume its same path after useful certified reserve returns", Resume);
        yield return ("Journey preparation fills physical metres inside its loading horizon with a bounded variable section count", DistanceBuffer);
        yield return ("Connected Journey producer and consumer sustain travel with delayed production at 30 and 60 fps", DelayedKilometre);
        yield return ("Journey queue heads require exact predecessor identity and fresh complete evidence", QueueIdentity);
        yield return ("Journey runway state and producer history reset between owners and reject invalid input", InvalidAndReset);
        yield return ("Journey runtime budget covers useful adapted cruise while retaining a finite limit", RuntimeBudget);
        yield return ("Journey cruise anticipates a slower connected section with bounded acceleration and deceleration", SectionLookahead);
        yield return ("A late slower section strictly caps startup and seam speed in controller telemetry", SectionSeamLimit);
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

    private static void AnticipateSupply()
    {
        var ordinary = new JourneyRunwayPolicy();
        var waiting = new JourneyRunwayPolicy();
        ordinary.TryStart(1, 64, 6, out _);
        waiting.TryStart(1, 64, 6, out _);
        for (int i = 0; i < 60; i++)
        {
            ordinary.StepSpeed(6, 36, .02);
            waiting.StepSpeed(6, 36, .02, producerLeadSeconds: 20);
        }
        Check(waiting.EffectiveCruise < ordinary.EffectiveCruise - 1,
            "Playback waited until the stopping boundary to respond to slow replenishment");
    }

    private static void StartupReserve()
    {
        foreach (double cruise in new[] { 2d, 6d, 12d })
        {
            double guide = JourneyRunwayPolicy.DepartureGuideMetres(cruise, 64);
            Check(guide < 32 && new JourneyRunwayPolicy().TryStart(1, guide, cruise, out _),
                "Opening guide must provide a usable physical launch reserve");
        }
        var policy = new JourneyRunwayPolicy();
        Check(!policy.TryStart(0, 1000, 12, out _), "Uncertified distant metres launched movement");
        Check(!policy.TryStart(8, 4, 12, out _), "Eight tiny sections manufactured physical stopping room");
        policy.ObservePreparation(120);
        Check(policy.TryStart(1, 48, 12, out _), "A certified useful departure still waits for a second section or old latency");
        Check(policy.EffectiveCruise >= 2 && policy.EffectiveCruise <= 12, "Initial speed exceeded its useful range");
        Check(JourneyRunwayPolicy.StoppingDistance(policy.EffectiveCruise) +
            policy.EffectiveCruise * JourneyRunwayPolicy.MinimumUsefulSeconds <= 48.000001,
            "Initial departure did not reserve room for a physical stop");
        double speed = policy.EffectiveCruise;
        var divided = new JourneyRunwayPolicy(); divided.ObservePreparation(120);
        Check(divided.TryStart(6, 48, 12, out _), "Dividing the same physical reserve prevented launch");
        Near(divided.EffectiveCruise, speed);
        Check(new JourneyRunwayPolicy().TryStart(1, 8, 12, out _), "A short but useful certified window could not start at an adapted speed");
    }

    private static void LoadingFootprint()
    {
        Near(JourneyRunwayPolicy.WindowMetres(6, 40, 48), 24);
        Near(JourneyRunwayPolicy.WindowMetres(6, 57, 48), 0);
        Near(JourneyRunwayPolicy.WindowMetres(6, 10, 48, 60, 35), 15);
        Near(JourneyRunwayPolicy.WindowMetres(6, 0, 48, 85, 80), 0);
        Near(JourneyRunwayPolicy.WindowMetres(12, 40, 64, 85, 12, JourneyViewPolicy.ObservationRange), 33);
        Near(JourneyRunwayPolicy.WindowMetres(12, 40, 64, 85, 35, JourneyViewPolicy.ObservationRange), 10);
        foreach (double speed in new[] { 2d, 6, 12 })
        foreach (double inherited in new[] { 0d, 28, 50 })
        for (int seam = 0; seam <= 100; seam++)
        {
            double window = JourneyRunwayPolicy.WindowMetres(speed, seam, 64, 72, inherited);
            Check(window == 0 || window >= 8 && window <= 64 &&
                seam + window + Math.Max(JourneyRunwayPolicy.GazeReach(speed), inherited) <= 72.000001,
                "Proposal exceeded its full camera/focus loading footprint");
        }
        Near(JourneyRunwayPolicy.WindowMetres(2, 0, 1000), 64);
        Near(JourneyRunwayPolicy.WindowMetres(6, 10, 7.9), 0);
    }

    private static void MeasuredProduction()
    {
        var policy = new JourneyRunwayPolicy();
        Check(policy.ObservePreparation(32, 10, 18), "A complete route+ending job was ignored");
        Near(policy.ProductionMetresPerSecond, 4); Near(policy.PreparationSeconds, 10.5);
        for (int i = 0; i < 100; i++) Check(!policy.ObservePreparation(32, 17, 18), "Polling a completion fabricated production");
        Check(!policy.ObservePreparation(32, 20, 19) && !policy.ObservePreparation(32, 10, 17), "Reversed clocks entered throughput");
        Check(policy.Samples == 1, "Invalid clocks multiplied producer evidence");
        Check(policy.ObservePreparation(48, 18, 24), "A later successful candidate was ignored");
        Near(policy.ProductionMetresPerSecond, 80d / 14);
        // Duration-only legacy callers cannot invent the distance of that work.
        policy.ObservePreparation(100); Near(policy.ProductionMetresPerSecond, 80d / 14);
        for (int i = 0; i < JourneyRunwayPolicy.HistoryCapacity; i++)
            Check(policy.ObservePreparation(32, 25 + 4 * i, 29 + 4 * i), "A distinct newer successful candidate was ignored");
        Near(policy.ProductionMetresPerSecond, 8); Near(policy.PreparationSeconds, 5.5);
        Check(policy.Samples == JourneyRunwayPolicy.HistoryCapacity, "Producer history grew without bound");
    }

    private static void SlowProducer()
    {
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(32, 0, 7);
        Check(policy.TryStart(1, 32, 12, out _), "Physical startup still depends on an impossible latency-based horizon");
        Near(policy.EffectiveCruise, 32d / 7 * .9);
        var slow = new JourneyRunwayPolicy(); slow.ObservePreparation(16, 0, 60);
        Check(slow.TryStart(1, 16, 12, out _), "A completed useful path cannot start after a slow successful job");
        Near(slow.EffectiveCruise, 2);
        // The slow case can legitimately need to stop/replan later. It cannot
        // claim that its producer will replenish travel at the requested12m/s.
        Check(slow.ProductionMetresPerSecond < 2 && slow.DesiredCruise(12) == 2, "Throughput was inflated to the requested speed");
        foreach (double requested in new[] { 2d, 6, 12 })
        {
            var bounded = new JourneyRunwayPolicy(); bounded.TryStart(1, 64, requested, out _);
            Check(bounded.EffectiveCruise >= 2 && bounded.EffectiveCruise <= requested, "Startup speed escaped its bounds");
        }
    }

    private static void SmoothCruise()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var policy = new JourneyRunwayPolicy(); policy.TryStart(1, 64, 6, out _);
            double dt = 1d / fps;
            Near(policy.StepSpeed(6, 64, 100, held: true), 6);
            for (int i = 0; i < fps * 5; i++)
            {
                double before = policy.EffectiveCruise, after = policy.StepSpeed(6, 64, dt, braking: true);
                Check(before - after <= JourneyRunwayPolicy.Deceleration * dt + .000001 && after >= 0,
                    "Braking caused a speed surge or invalid speed");
            }
            Near(policy.EffectiveCruise, 0);
            double resumed = policy.StepSpeed(6, 64, 100);
            Check(resumed > 0 && resumed <= .1, "A delayed update accumulated catch-up acceleration");
            for (int i = 0; i < fps * 6; i++)
            {
                double before = policy.EffectiveCruise, after = policy.StepSpeed(6, 64, dt);
                Check(after - before <= JourneyRunwayPolicy.Acceleration * dt + .000001 && after <= 6,
                    "New proof caused an acceleration surge");
            }
            Near(policy.EffectiveCruise, 6);
        }
        Near(JourneyRunwayPolicy.StoppingDistance(12), .5 + 3 + 48);
        Near(JourneyRunwayPolicy.MaximumSafeSpeed(JourneyRunwayPolicy.StoppingDistance(12)), 12);
        Near(JourneyRunwayPolicy.MaximumSafeSpeed(.25), 0);
    }

    private static void SectionLookahead()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var policy = new JourneyRunwayPolicy();
            Check(policy.TryStart(1, 1000, 12, out _), "Lookahead fixture could not start");
            double remaining = 80, dt = 1d / fps;
            int frames = 0;
            while (remaining > 0 && frames++ < fps * 30)
            {
                double before = policy.EffectiveCruise;
                double speed = policy.StepSpeed(12, 1000, dt, sectionCruiseLimit: 12,
                    nextSectionCruiseLimit: 6, remainingSectionMetres: remaining);
                Check(before - speed <= JourneyRunwayPolicy.Deceleration * dt + .000001 &&
                    speed - before <= JourneyRunwayPolicy.Acceleration * dt + .000001,
                    "Normal anticipatory motion jumped its acceleration bound");
                Check(speed <= JourneyRunwayPolicy.SectionSpeedEnvelope(12, 6, remaining) + .000001,
                    "Cruise ignored the approaching section speed constraint");
                remaining -= speed * dt;
                Check(speed <= JourneyRunwayPolicy.SectionSpeedEnvelope(12, 6, Math.Max(0, remaining)) + .000001,
                    "The proposed frame consumed braking distance without respecting the next evaluation's envelope");
            }
            Check(remaining <= 0 && policy.EffectiveCruise <= 6 + JourneyRunwayPolicy.Deceleration * dt + .001,
                "Camera reached the slower seam without decelerating");
            policy.LimitToSection(6); Near(policy.EffectiveCruise, 6);
            for (int i = 0; i < fps * 2; i++) Near(policy.StepSpeed(12, 1000, dt, sectionCruiseLimit: 6), 6);
            // A later straighter section can accelerate again; its larger
            // certificate is a limit, not an instantaneous commanded speed.
            double prior = policy.EffectiveCruise;
            policy.LimitToSection(12); Near(policy.EffectiveCruise, prior);
            for (int i = 0; i < fps * 6; i++)
            {
                double before = policy.EffectiveCruise, after = policy.StepSpeed(12, 1000, dt);
                Check(after - before <= JourneyRunwayPolicy.Acceleration * dt + .000001,
                    "Leaving the slow section caused an acceleration jump");
            }
            Near(policy.EffectiveCruise, 12);
        }
    }

    private static void SectionSeamLimit()
    {
        var startup = new JourneyRunwayPolicy();
        Check(startup.TryStart(1, 1000, 12, out _, sectionCruiseLimit: 2), "Useful certified slow departure was refused");
        Near(startup.EffectiveCruise, 2);
        var late = new JourneyRunwayPolicy(); late.TryStart(1, 1000, 12, out _);
        Near(late.EffectiveCruise, 12);
        // Proof can arrive at the seam. There is then no distance in which to
        // anticipate it: strict current-section safety takes priority.
        Near(late.StepSpeed(12, 1000, 1d / 60, sectionCruiseLimit: 2), 2);
        Near(late.EffectiveCruise, 2);
        late.LimitToSection(12); Near(late.EffectiveCruise, 2);
        Check(late.StepSpeed(12, 1000, 1d / 60) <= 2 + JourneyRunwayPolicy.Acceleration / 60 + .000001,
            "Restoring a larger ceiling instantly changed controller speed");
        Near(JourneyRunwayPolicy.SectionSpeedEnvelope(12, 6, 36), 12);
        Near(JourneyRunwayPolicy.SectionSpeedEnvelope(12, 6, 0), 6);
    }

    private static void FiniteStop()
    {
        foreach (int fps in new[] { 10, 30, 60 })
        foreach (double runway in new[] { 8d, 16, 32, 57 })
        {
            var policy = new JourneyRunwayPolicy(); Check(policy.TryStart(1, runway, 12, out _), "Stop fixture cannot launch");
            double remaining = runway, elapsed = 0, dt = 1d / fps;
            while (!policy.Stopped)
            {
                double speed = policy.StepSpeed(12, remaining, dt);
                remaining -= speed * dt; elapsed += dt;
                Check(remaining >= JourneyRunwayPolicy.StopMarginMetres - .000001,
                    "The consumer crossed the certified runway before stopping");
                Check(remaining + .000001 >= speed * speed / (2 * JourneyRunwayPolicy.Deceleration),
                    "The consumer did not retain its physical braking distance");
                Check(elapsed < 20, "A starved producer caused an indefinite near-zero crawl");
            }
            for (int i = 0; i < fps * 20; i++) Near(policy.StepSpeed(12, remaining, dt), 0);
        }
    }

    private static void Resume()
    {
        var policy = new JourneyRunwayPolicy(); policy.TryStart(1, 32, 6, out _);
        for (int i = 0; i < 60; i++) policy.StepSpeed(6, 32, .1, braking: true);
        Check(policy.Stopped && policy.Started, "A local stop destroyed the admitted mission's speed owner");
        for (int i = 0; i < 100; i++) Near(policy.StepSpeed(6, 2, .1), 0);
        double resumed = policy.StepSpeed(6, 32, .1);
        Check(resumed > 0 && resumed <= .1 && policy.Started, "Fresh useful runway could not restart the same path smoothly");
    }

    private static void DistanceBuffer()
    {
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(32, 0, 8);
        double target = policy.TargetRunwayMetres(12, 57);
        Check(target > 32 && target <= 57, "Scheduling ignored throughput or the loading horizon");
        Check(policy.NeedsPreparation(16, 12, 57) && !policy.NeedsPreparation(57, 12, 57),
            "A fixed count displaced the distance-based replenishment threshold");
        Check(policy.TargetSectionCount(12, 57, 8) > 2 && policy.TargetSectionCount(12, 57, 48) == 1,
            "Short windows still stop filling after two entries");
        Check(policy.TargetSectionCount(12, 10000, .1) <= JourneyRunwayPolicy.MaximumBufferedSections,
            "Fragmented paths caused an unbounded memory queue");
        Near(policy.TargetRunwayMetres(12, 0), 0);
        Check(!policy.NeedsPreparation(0, 12, 0), "Unknown local horizon was treated as open space");
    }

    private static void DelayedKilometre()
    {
        // Producer/consumer arithmetic only, not a substitute for the integrated
        // mission/scene/proof tests or a live Unity geometry acceptance run.
        foreach (int fps in new[] { 30, 60 })
        foreach (double[] latencies in new[] { new[] { 3.5 }, new[] { 7d }, new[] { 3.5, 5, 7, 4, 6 } })
        {
            var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(32, 0, latencies[0]);
            Check(policy.TryStart(1, 32, 6, out _), "One physical departure could not start");
            var ends = new Queue<double>(); ends.Enqueue(32);
            double position = 0, tail = 32, now = 10, pendingEnd = 0, readyAt = -1, jobStarted = 0;
            int completed = 1, handoffs = 0; double dt = 1d / fps;
            while (position < 1000)
            {
                if (readyAt >= 0 && now >= readyAt)
                {
                    policy.ObservePreparation(pendingEnd - tail, jobStarted, now);
                    ends.Enqueue(pendingEnd); tail = pendingEnd; readyAt = -1; completed++;
                }
                while (ends.Count > 0 && position >= ends.Peek() - .000001) { ends.Dequeue(); handoffs++; }
                Check(ends.Count > 0, "Producer exhausted the current certified path");
                double carrier = Math.Min(tail, position + 24);
                double horizon = 85 - JourneyRunwayPolicy.GazeReach(6) + carrier - position;
                if (readyAt < 0 && ends.Count < JourneyRunwayPolicy.MaximumBufferedSections &&
                    policy.NeedsPreparation(tail - position, 6, horizon))
                {
                    double length = JourneyRunwayPolicy.WindowMetres(6, Math.Max(0, tail - carrier), 48);
                    if (length >= 8)
                    {
                        jobStarted = now; pendingEnd = tail + length;
                        readyAt = now + latencies[completed % latencies.Length];
                    }
                }
                double speed = policy.StepSpeed(6, tail - position, dt);
                Check(speed > 0, $"Supported delayed production stopped at {position:0.0}m/{now:0.0}s");
                double step = Math.Min(speed * dt, Math.Min(1000 - position, ends.Peek() - position));
                Check(step >= 0 && position + step <= tail + .000001, "Speculative metres were consumed before certification");
                position += step; now += dt;
                Check(now < 510, "Delayed production became indefinite low-speed travel");
            }
            Check(completed > 20 && handoffs > 20, "Long-distance fixture skipped real producer handoffs");
            Check(now - 10 >= 1000d / 6 - .1, "Travel exceeded the requested cruise ceiling");
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
        var policy = new JourneyRunwayPolicy(); policy.ObservePreparation(32, 0, 8); policy.TryStart(1, 32, 6, out _); policy.Reset();
        Check(!policy.Started && !policy.Stopped && policy.Samples == 0 && policy.EffectiveCruise == 0 && policy.ProductionMetresPerSecond == 0,
            "A new owner inherited another mission's state or throughput");
        foreach (double value in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { policy.ObservePreparation(value); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid preparation duration entered history");
            rejected = false;
            try { JourneyRunwayPolicy.MaximumSafeSpeed(value); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Unknown distance supplied stopping room");
        }
        bool premature = false;
        try { policy.StepSpeed(6, 60, .1); } catch (InvalidOperationException) { premature = true; }
        Check(premature, "Unadmitted runway advanced cruise");
    }

    private static void RuntimeBudget()
    {
        var destination = new ScenicRegion("headland", "Headland", P(1000));
        Check(ScenicJourneyItinerary.TrySelect(P(0), P(1), new[] { destination }, null, 1000, 10, null,
            out var itinerary, out _), "Known destination fixture was unavailable");
        var journey = new ScenicJourneyPolicy(); journey.Start(7, itinerary!, 6);
        double minimum = JourneyRunwayPolicy.MinimumCruise(6);
        Check(journey.MaximumSeconds >= 1000 / minimum + 6 && journey.MaximumSeconds < 1000,
            "Runtime budget excludes useful adapted cruise or lost its finite bound");
        double now = 0, position = 0; long id = 10;
        var section = Section(id); journey.Observe(section, now, 0, true, false, P(0));
        while (position < 990)
        {
            now += .1; position += minimum * .1;
            if (position >= (id - 9) * 32) { id++; section = Section(id, id - 1, (id - 10) * 32); }
            journey.Observe(section, now, Math.Min(1, Math.Max(0, (position - (id - 10) * 32) / 32)), true, false, P((float)position));
            Check(!journey.BudgetExpired, "Continuous useful adapted travel exhausted a nominal-speed deadline");
        }
        Check(journey.TravelMetres > 989 && journey.ShownSeconds > 490,
            "Budget fixture did not exercise actual minimum-speed presentation");
    }
}
