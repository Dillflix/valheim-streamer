using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneySectionTimingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("High-speed Journey accepts the full loading policy window range without one-second section rejection", LoadedWindowTiming);
        yield return ("Native Journey distance finalization may retime a certified section below one second", FinalizationTiming);
        yield return ("Short Journey sections progress at commanded speed without becoming ordinary short shots", ShortSectionPlayback);
        yield return ("Journey section timing remains bounded and ordinary fixture timing remains at least one second", TimingBounds);
        yield return ("Journey routes and ending transformations preserve stable water clearance without changing ordinary scenery", StableWaterOwnership);
    }

    private static AutonomyPosition P(float x, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Plan(float length = 8) => ScenicShotPlan.Spatial(2,
        new ScenicFeature("ocean", ScenicFeatureKind.Coastline, P(4, 18, 28), P(1, 0), 20, 5, 1),
        ScenicShotRole.Traverse, new[] { P(0), P(length / 2), P(length) },
        new[] { P(28, 18), P(length / 2 + 28, 18), P(length + 28, 18) }, 65);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double actual, double expected, double tolerance = .000001) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid timing was accepted"); }

    private static void LoadedWindowTiming()
    {
        // Production limits a future section by both its camera and forward
        // gaze. At the reported 12m/s this deliberately yields 8–12m windows,
        // whose continuous-section clock is less than the old one-second floor.
        foreach (double speed in new[] { 2d, 6, 12 })
        for (int seam = 0; seam <= 85; seam++)
        {
            double length = JourneyRunwayPolicy.WindowMetres(speed, seam, 64);
            if (length == 0) continue;
            var original = Plan((float)length);
            var section = original.AsJourney(63, 1, original.TravelMetres / speed);
            Near(section.TravelMetres / section.DurationSeconds, speed);
            var departure = original.AsJourneyDeparture(63, original.TravelMetres / speed /
                ScenicJourneyMotion.CruiseFraction(true, false)).WithJourneyRamps(true, false);
            Near(departure.TravelMetres / departure.DurationSeconds /
                ScenicJourneyMotion.CruiseFraction(true, false), speed);
            Check(!original.IsJourney && !original.StableWaterClearance, "Converting a candidate changed its source");
        }
        Near(Plan().AsJourney(63, 1, 8d / 12).DurationSeconds, 2d / 3);
    }

    private static void FinalizationTiming()
    {
        var section = Plan(12).AsJourney(63, 1, 1);
        // Mirrors the production finalizer: native distance owns duration,
        // while all normalized poses and the certified identity are retained.
        var measured = section.WithDuration(11.8 / 12);
        Near(measured.DurationSeconds, 11.8 / 12);
        Check(measured.JourneyId == section.JourneyId && measured.ContinuationOf == section.ContinuationOf,
            "Native retiming lost the certified Journey relationship");
        for (int i = 0; i <= 20; i++)
        {
            Near(measured.Sample(i / 20d).CameraPosition.Distance(section.Sample(i / 20d).CameraPosition), 0);
            Near(measured.Sample(i / 20d).AimPosition.Distance(section.Sample(i / 20d).AimPosition), 0);
        }
        Near(section.DurationSeconds, 1);
    }

    private static void ShortSectionPlayback()
    {
        foreach (int fps in new[] { 30, 60 })
        {
            var section = Plan().AsJourney(63, 1, 8d / 12);
            var director = new ScenicDirector(); director.Accept(section, 0);
            director.Observe(section.Id, 0, 0, true, true, false);
            int frames = (int)Math.Round(section.DurationSeconds * fps);
            for (int frame = 1; frame <= frames; frame++)
            {
                double next = director.Propose(1d / fps);
                director.Observe(section.Id, frame / (double)fps, next, true, true, false);
                Near(section.Sample(next).CameraPosition.X, 12d * frame / fps, .00001);
            }
            Near(director.Progress, 1);
            Check(director.NeedsReplacement, "Continuous section did not finish at its measured endpoint");
        }
    }

    private static void TimingBounds()
    {
        foreach (double bad in new[] { 0d, -.1, .0999, double.NaN, double.PositiveInfinity, 604801 })
        {
            Reject(() => Plan().AsJourney(63, 1, bad));
            Reject(() => Plan().AsJourneyDeparture(63, bad));
            Reject(() => Plan().AsJourney(63, 1, 1).WithDuration(bad));
        }
        Near(Plan().AsJourney(63, 1, .1).DurationSeconds, .1);
        Near(Plan().AsJourney(63, 1, 604800).DurationSeconds, 604800);
        Reject(() => Plan().WithDuration(8d / 12));
        Reject(() => new CameraFixture(1, "short", "test", "", DateTime.UtcNow, 1, true,
            Plan().AsJourney(63, 1, 8d / 12)));
    }

    private static void StableWaterOwnership()
    {
        var ordinary = Plan(20);
        Check(!ordinary.StableWaterClearance, "Ordinary Scenic clearance was changed globally");
        var section = ordinary.AsJourney(63, 1, 20d / 12);
        Check(section.StableWaterClearance && ordinary.AsJourneyDeparture(63, 2).StableWaterClearance,
            "Journey preparation did not own future water clearance");
        var ending = ordinary.WithStableWaterClearance(section.StableWaterClearance).WithDuration(6);
        foreach (var transformed in new[] { section.WithDuration(.9), section.RaiseCamera(2), section.Slice(0, .8),
            section.AsStandalone(), ending.RaiseCamera(2), ending.Slice(.1, 1), ending.WithIdentity(3) })
            Check(transformed.StableWaterClearance, "Geometry transformation lost its water-clearance contract");
        Check(!ending.WithStableWaterClearance(false).StableWaterClearance && ending.StableWaterClearance,
            "Explicit clearance selection changed an existing plan");
    }
}
