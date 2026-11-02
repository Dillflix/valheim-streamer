using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicMotionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Long landscapes begin useful movement and avoid a midpoint speed surge", Cruise);
        yield return ("Scenic ramps retain continuous speed and rest at both endpoints", RampContinuity);
        yield return ("Scenic motion progress stays monotonic bounded and symmetric", ProfileBounds);
        yield return ("Readable blocked footage earns no deferred scenic camera movement", BlockedClock);
        yield return ("Scenic manual pause resumes without rewinding or repaying elapsed progress", PausedClock);
        yield return ("Scenic clock supports finite completion and new-plan reset", ResetClock);
        yield return ("Scenic progress rejects invalid values", InvalidValues);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Near(double a, double b, double epsilon = .000001) => Check(Math.Abs(a - b) < epsilon, $"{a} != {b}");
    private static void Cruise()
    {
        Check(ScenicMotionProfile.Progress(3d / 45) > .025, "Opening still spends seconds nearly static.");
        double prior = ScenicMotionProfile.Progress(.2), step = ScenicMotionProfile.Progress(.3) - prior;
        for (int i = 3; i < 9; i++)
            Near(ScenicMotionProfile.Progress((i + 1) / 10d) - ScenicMotionProfile.Progress(i / 10d), step);
        Check(step < .11, "Cruise speed still concentrates motion in a midpoint burst.");
    }
    private static double Speed(double t) => (ScenicMotionProfile.Progress(t + .000001) - ScenicMotionProfile.Progress(t - .000001)) / .000002;
    private static void RampContinuity()
    {
        Near(Speed(0), 0, .00001); Near(Speed(1), 0, .00001);
        foreach (double join in new[] { .08, .92 })
            Near(Speed(join - .00001), Speed(join + .00001), .00001);
    }
    private static void ProfileBounds()
    {
        double previous = 0;
        for (int i = 0; i <= 10000; i++)
        {
            double progress = ScenicMotionProfile.Progress(i / 10000d);
            Check(progress >= previous && progress <= 1, "Motion reversed or escaped endpoints.");
            Near(progress + ScenicMotionProfile.Progress(1 - i / 10000d), 1);
            previous = progress;
        }
        Near(ScenicMotionProfile.Progress(-2), 0); Near(ScenicMotionProfile.Progress(3), 1);
    }
    private static void BlockedClock()
    {
        var clock = new ScenicMotionClock(); clock.Update(.2f, 1, true);
        for (int i = 21; i <= 70; i++) Near(clock.Update(i / 100f, 1, false), .2);
        Near(clock.Update(.71f, 1, true), .21);
        Near(clock.Update(.72f, 1, true), .22);
    }
    private static void PausedClock()
    {
        var clock = new ScenicMotionClock(); clock.Reset(.3f);
        Near(clock.Update(.4f, 0, true), .3);
        Near(clock.Update(.6f, 1, false), .3);
        Near(clock.Update(.61f, 1.5f, true), .315);
        Near(clock.Update(.61f, 1.5f, true), .315);
    }
    private static void ResetClock()
    {
        var clock = new ScenicMotionClock(); clock.Update(.8f, 1.5f, true); Near(clock.Progress, 1);
        clock.Reset(); Near(clock.Update(.1f, 1, true), .1);
        Near(clock.Update(.05f, 1, true), .1); // A regressing input cannot rewind a displayed camera.
        Near(clock.Update(.1f, 1, true), .1); // Or double-count the same coverage after recovery.
    }
    private static void InvalidValues()
    {
        var clock = new ScenicMotionClock();
        Throws(() => clock.Update(float.NaN, 1, true)); Throws(() => clock.Update(0, -1, true));
        Throws(() => ScenicMotionProfile.Progress(double.PositiveInfinity));
    }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentOutOfRangeException) { return; } throw new Exception("Invalid progress accepted."); }
}
