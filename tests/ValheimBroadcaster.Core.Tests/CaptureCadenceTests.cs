using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CaptureCadenceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Capture cadence preserves requested rate across non-divisible rendering rates", NonDivisibleRates);
        yield return ("Capture cadence survives long uptime without float timing drift", LongUptime);
        yield return ("Slow render and pending GPU slots are counted without catch-up bursts", SkippedSlots);
        yield return ("Capture clock session reset and input contracts are explicit", Lifecycle);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static CaptureCadence Simulate(int renderRate, int captureRate, double epoch)
    {
        var clock = new CaptureCadence(); clock.Reset(captureRate);
        for (int frame = 0; frame < renderRate * 10; frame++)
            clock.TryCapture(epoch + frame / (double)renderRate, true);
        return clock;
    }
    private static void NonDivisibleRates()
    {
        foreach (int renderRate in new[] { 50, 60, 75, 144 })
        {
            var p = Simulate(renderRate, 30, 0);
            Check(p.AdmittedSamples == 300 && p.SkippedSlots == 0,
                renderRate + " fps rendering unnecessarily lost capture slots: " + p.AdmittedSamples);
        }
        var fast = Simulate(144, 60, 0);
        Check(fast.AdmittedSamples == 600 && fast.SkippedSlots == 0, "60 fps output drifted against 144 Hz rendering");
    }
    private static void LongUptime()
    {
        var p = Simulate(75, 30, 4194304);
        Check(p.AdmittedSamples == 300 && p.SkippedSlots == 0,
            "A long-running process quantized away valid capture deadlines");
    }
    private static void SkippedSlots()
    {
        var slow = Simulate(20, 30, 0);
        Check(slow.AdmittedSamples == 200 && slow.SkippedSlots == 99 && slow.ScheduledSlots == 299,
            "Slow rendering fabricated captures or hid unavailable sampling slots");
        var p = new CaptureCadence(); p.Reset(30);
        Check(p.TryCapture(0, true), "First encoder-ready render was not sampled");
        Check(!p.TryCapture(.04, false), "A busy readback admitted a second GPU request");
        Check(!p.TryCapture(.05, true), "Readback completion triggered an early catch-up sample");
        Check(p.TryCapture(.07, true), "Next scheduled sample was lost after a busy readback");
        Check(p.TryCapture(2, true), "First render after a long stall was not sampled");
        Check(!p.TryCapture(2, true) && !p.TryCapture(2.001, true), "A stall caused a capture burst");
        Check(p.AdmittedSamples == 3 && p.SkippedSlots == 58 && p.ScheduledSlots == 61,
            "Missed render and busy-GPU slots were not accounted exactly once");
        Check(p.TryCapture(2 + 1d / 30, true), "Stall reset the original sampling phase");
    }
    private static void Lifecycle()
    {
        var p = new CaptureCadence(); bool rejected = false;
        try { p.TryCapture(0, true); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unconfigured cadence was accepted");
        p.Reset(30); p.TryCapture(100, true);
        foreach (double invalid in new[] { 99d, -1d, double.NaN, double.PositiveInfinity })
        {
            rejected = false;
            try { p.TryCapture(invalid, true); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid or backwards capture clock was accepted");
        }
        p.Reset(60);
        Check(p.TryCapture(0, true) && p.AdmittedSamples == 1 && p.SkippedSlots == 0,
            "A restarted stream inherited previous timing or counters");
    }
}
