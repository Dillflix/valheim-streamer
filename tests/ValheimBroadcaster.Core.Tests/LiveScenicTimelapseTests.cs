using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class LiveScenicTimelapseTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Live timelapse advances only from its actually presented camera route", Presentation);
        yield return ("Live timelapse yields immediately on permission loss or different camera ownership", Preemption);
        yield return ("Live timelapse preparation and missing-output waits are bounded", Deadlines);
        yield return ("Live timelapse admission counts useful coverage and enforces programme spacing", Spacing);
        yield return ("Live daylight passage rejoins moving natural time at both ends", Phase);
        yield return ("Live timelapse retains phase during a deliberate hold without treating it as failure", Hold);
    }
    private static void Check(bool v) { if (!v) throw new Exception("Live timelapse assertion failed"); }
    private static LiveScenicTimelapse New() { var p = new LiveScenicTimelapse(); p.Begin(7, 0); return p; }
    private static void Presentation()
    {
        var p = New(); p.Observe(1, true, 7, 6, .8, true, false); Check(!p.OnAir && p.Progress == 0);
        p.Observe(2, true, 7, 7, .2, true, false); Check(p.OnAir && p.Progress == .2);
        p.Observe(3, true, 7, 7, .6, false, false); Check(p.Progress == .2);
        p.Observe(4, true, 7, 7, .1, true, false); Check(p.Progress == .2);
        p.Observe(5, true, 7, 7, 1, true, false); Check(!p.Active);
    }
    private static void Preemption()
    {
        var p = New(); p.Observe(1, false, 7, 7, .2, true, false); Check(!p.Active);
        p = New(); p.Observe(1, true, 8, 7, .2, true, false); Check(!p.Active);
    }
    private static void Deadlines()
    {
        var p = New(); p.Observe(26, true, 7, 6, 0, true, false); Check(!p.Active);
        p = New(); p.Observe(1, true, 7, 7, .2, true, false); p.Observe(2, true, 7, 7, .2, false, false);
        p.Observe(5, true, 7, 7, .2, false, false); Check(!p.Active);
    }
    private static void Spacing()
    {
        var p = new LiveScenicTimelapse(); Check(!p.Opportunity(0, true));
        Check(!p.Opportunity(90, true)); // no time credit for a stalled update
        p.Request(); Check(!p.Opportunity(91, false) && p.Opportunity(92, true));
        p.Begin(7, 92); p.End(93, "finished");
        for (int i = 0; i < 1000; i++) Check(!p.Opportunity(94 + i * .1, true));
        Check(p.Opportunity(394, true));
    }
    private static void Phase()
    {
        foreach (double phase in new[] { 0, .1, .9, .999 })
        {
            Check(Math.Abs(LiveScenicTimelapse.Phase(phase, 0) - phase) < 1e-9);
            Check(Math.Abs(LiveScenicTimelapse.Phase(phase, 1, .1) - phase) < 1e-9);
        }
        Check(Math.Abs(LiveScenicTimelapse.Phase(.2, .5) - .7) < 1e-9);
        Check(Math.Abs(LiveScenicTimelapse.Phase(.25, 0, -.05) - .2) < 1e-9);
        bool rejected = false; try { LiveScenicTimelapse.Phase(.2, double.NaN); } catch (ArgumentException) { rejected = true; } Check(rejected);
    }
    private static void Hold()
    {
        var p = New(); p.Observe(1, true, 7, 7, .3, true, false);
        p.Observe(2, true, 7, 7, .3, false, false);
        p.Observe(100, true, 7, 7, .8, true, true); Check(p.Active && p.Progress == .3);
        p.Observe(101, true, 7, 7, .31, true, false); Check(p.Active && p.Progress == .31);
    }
}
