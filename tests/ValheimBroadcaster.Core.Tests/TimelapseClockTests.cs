using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class TimelapseClockTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Timelapse interval sampling explicitly accelerates real source time", Rate);
        yield return ("Late timelapse renders skip slots without repeated images or catch-up", Gaps);
        yield return ("Timelapse stops at requested source duration and rejects reversed time", Lifetime);
        yield return ("Timelapse bounds finite settings frame count and long captures", Bounds);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Rate()
    {
        var clock = new TimelapseClock(2, 600, 30); clock.Start(123);
        int frames = 0;
        for (int i = 0; i <= 600 * 60; i++) if (clock.Take(123 + i / 60d)) frames++;
        Check(frames == 300 && clock.PlannedFrames == 300, "Interval endpoints changed sample count.");
        Check(clock.PlannedOutputSeconds == 10 && clock.SpeedMultiplier == 60, "Source and output clocks mixed.");
        Check(clock.MissedIntervals == 0, "Normal render cadence was counted as missing.");
    }
    private static void Gaps()
    {
        var clock = new TimelapseClock(2, 60, 30); clock.Start(0);
        Check(clock.Take(0), "First interval missing.");
        Check(clock.Take(9.9) && clock.MissedIntervals == 3, "Missed intervals were not explicit.");
        Check(!clock.Take(9.9) && !clock.Take(9.99), "Catch-up repeated a source image.");
        Check(clock.Take(10) && clock.OfferedFrames == 3, "Schedule drifted to last rendered time.");
    }
    private static void Lifetime()
    {
        var clock = new TimelapseClock(1, 10, 24); clock.Start(5);
        Check(clock.Remaining(10) == 5 && !clock.Expired(14.99) && clock.Expired(15), "Source deadline changed.");
        Check(!clock.Take(15), "Frame admitted after recording deadline.");
        Throws<InvalidOperationException>(() => clock.Take(14));
        clock.Start(20); Check(clock.Take(20) && clock.OfferedFrames == 1 && clock.MissedIntervals == 0, "Restart retained old clock.");
    }
    private static void Bounds()
    {
        Throws<ArgumentOutOfRangeException>(() => new TimelapseClock(double.NaN, 10, 30));
        Throws<ArgumentOutOfRangeException>(() => new TimelapseClock(1, double.PositiveInfinity, 30));
        Throws<ArgumentOutOfRangeException>(() => new TimelapseClock(.1, 10, 30));
        Throws<ArgumentOutOfRangeException>(() => new TimelapseClock(1, 10, 0));
        Throws<ArgumentOutOfRangeException>(() => new TimelapseClock(.25, 604800, 30));
        Check(new TimelapseClock(10, 604800, 30).PlannedFrames == 60480, "Seven-day bounded capture unavailable.");
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Invalid input accepted."); }
}
