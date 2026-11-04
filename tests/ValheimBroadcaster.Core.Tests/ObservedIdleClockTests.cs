using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ObservedIdleClockTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Observed idle requires sixty sampled quiet seconds", Quiet);
        yield return ("An action between samples immediately cancels prior idle", InterveningAction);
        yield return ("Unknown and stale sample gaps restart idle evidence", LostEvidence);
        yield return ("Sustained activity never qualifies as idle", Busy);
        yield return ("Activity without samples cannot manufacture known idle", Unobserved);
        yield return ("Observed idle reset forgets old character evidence", Reset);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Observed idle assertion failed."); }
    private static void Quiet()
    {
        var clock = new ObservedIdleClock();
        for (int i = 0; i < 60; i++) clock.Observe(i, true, false);
        Check(clock.Known && clock.ObservedIdleSeconds == 59);
        // No method observes time passing: another reader sees the same evidence.
        Check(clock.ObservedIdleSeconds == 59);
        clock.Observe(60, true, false); Check(clock.ObservedIdleSeconds == 60);
    }
    private static void InterveningAction()
    {
        var clock = new ObservedIdleClock();
        for (int i = 0; i <= 70; i++) clock.Observe(i, true, false);
        clock.Activity(70.5); Check(clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(71, true, false); Check(clock.ObservedIdleSeconds == .5);
        for (int i = 72; i <= 130; i++) clock.Observe(i, true, false);
        Check(clock.ObservedIdleSeconds == 59.5);
    }
    private static void LostEvidence()
    {
        var clock = new ObservedIdleClock();
        for (int i = 0; i <= 60; i++) clock.Observe(i, true, false);
        clock.Observe(61, false, false); Check(!clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(62, true, false); Check(clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(63, true, false); Check(clock.ObservedIdleSeconds == 1);
        clock.Observe(67, true, false); Check(clock.ObservedIdleSeconds == 0);
    }
    private static void Busy()
    {
        var clock = new ObservedIdleClock();
        for (int i = 0; i <= 100; i++) clock.Observe(i, true, true);
        Check(clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(101, true, false); Check(clock.ObservedIdleSeconds == 1);
    }
    private static void Unobserved()
    {
        var clock = new ObservedIdleClock(); clock.Activity(100);
        Check(!clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(200, true, false); Check(clock.ObservedIdleSeconds == 0);
    }
    private static void Reset()
    {
        var clock = new ObservedIdleClock();
        for (int i = 0; i <= 60; i++) clock.Observe(i, true, false);
        clock.Reset(); Check(!clock.Known && clock.ObservedIdleSeconds == 0);
        clock.Observe(3, true, false); Check(clock.ObservedIdleSeconds == 0);
    }
}
