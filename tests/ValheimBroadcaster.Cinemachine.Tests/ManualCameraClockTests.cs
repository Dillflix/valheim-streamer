using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using ValheimBroadcaster.CinemachineRuntime;

// Executes the production bridge clock and pinned native damping math. Unity
// component lifetime/Time.frameCount integration still requires the game runtime.
internal static class ManualCameraClockTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Standby native damping integrates actual elapsed time once across irregular frame gaps", IrregularCadence);
        yield return ("Native manual clock preserves first update and same-frame identity", FirstAndDuplicate);
        yield return ("Negative native reset is retained and starts a new elapsed interval", Reset);
        yield return ("Native test-clock mode does not compensate an absent skipped-frame multiplier", TestClock);
    }
    private static void Near(float a, float b) { if (Math.Abs(a-b) > .00001f) throw new Exception("Expected " + b + ", actual " + a); }
    private static void IrregularCadence()
    {
        var clock = new ManualCameraClock(); clock.Prepare(100, 4, .016f);
        int[] frames = { 112, 119, 121, 138 }; double[] times = { 4.23, 4.44, 4.48, 4.91 };
        int previous = 100; double last = 4;
        for (int i = 0; i < frames.Length; i++)
        {
            float input = clock.Prepare(frames[i], times[i], .017f);
            float nativeElapsed = input * (frames[i] - previous);
            Near(nativeElapsed, (float)(times[i] - last));
            Near(Damper.Damp(10, 1.5f, nativeElapsed), Damper.Damp(10, 1.5f, (float)(times[i] - last)));
            previous = frames[i]; last = times[i];
        }
    }
    private static void FirstAndDuplicate()
    {
        var clock = new ManualCameraClock(); Near(clock.Prepare(20, 2, .02f), .02f);
        Near(clock.Prepare(20, 2.01, .02f), .02f); // Native manager deduplicates this frame.
        Near(clock.Prepare(22, 2.1, .02f) * 2, .1f); // Duplicate did not advance the bridge clock.
    }
    private static void Reset()
    {
        var clock = new ManualCameraClock(); clock.Prepare(4, 1, .02f);
        Near(clock.Prepare(4, 1.03, -1), -1);
        Near(clock.Prepare(8, 1.15, .02f) * 4, .12f);
    }
    private static void TestClock()
    {
        var clock = new ManualCameraClock(); clock.Prepare(1, 0, .02f, true);
        Near(clock.Prepare(11, .3, .02f, true), .3f);
    }
}
