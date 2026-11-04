using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class GroupDollyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Open group dolly remains bounded independently of group spread", BoundedOpen);
        yield return ("Single subjects and disabled group dolly leave the solo lens in control", Solo);
        yield return ("Unknown rooms forests and limited alternatives retain zoom framing", Constrained);
        yield return ("Close subjects receive a smaller group translation budget", CloseScale);
        yield return ("Native lateral plus longitudinal correction shares one movement budget", TotalDisplacement);
        yield return ("Rejected group dolly switches to zoom before a bounded retry", Rejection);
        yield return ("Repeated group observations cannot renew blocked-dolly cooldown", Cooldown);
        yield return ("Invalid group scene evidence cannot retain a previous translation allowance", InvalidEvidence);
        yield return ("Group translation rejects nonfinite vectors and oversize coordinates", InvalidMotion);
        yield return ("World reset and reversed time retire group dolly history", Reset);
    }

    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static GroupDollyPolicy Open(float spread = 10, float distance = 20)
    {
        var result = new GroupDollyPolicy();
        result.Configure(0, true, true, true, 1, spread, distance);
        return result;
    }
    private static void BoundedOpen()
    {
        var p = Open(100, 100);
        Check(p.Allowed && p.Limit == GroupDollyPolicy.MaximumDistance, "Large group created an unbounded second trajectory.");
        Check(p.WithinBudget(0, 0, p.Limit) && !p.WithinBudget(0, 0, p.Limit + .1f), "Longitudinal motion escaped its budget.");
    }
    private static void Solo()
    {
        var p = Open();
        p.Configure(1, true, false, true, 1, 10, 20);
        Check(!p.Allowed && !p.WithinBudget(0, 0, 0), "Solo composition retained native group translation authority.");
        p.Configure(2, false, true, true, 1, 10, 20);
        Check(!p.Allowed, "Disabled group dolly remained active.");
    }
    private static void Constrained()
    {
        var p = Open();
        p.Configure(1, true, true, false, 1, 10, 20);
        Check(!p.Allowed, "Uncertain or enclosed space permitted group translation.");
        p.Configure(2, true, true, true, .59f, 10, 20);
        Check(!p.Allowed, "A geometrically constrained camera space permitted group translation.");
        p.Configure(3, true, true, true, 1, 10, 2.9f);
        Check(!p.Allowed, "Camera already too close gained group dolly authority.");
    }
    private static void CloseScale()
    {
        var close = Open(10, 4); var wide = Open(10, 20); var tightGroup = Open(0, 20);
        Check(close.Limit < wide.Limit && tightGroup.Limit < wide.Limit, "Budget did not reflect local subject scale.");
        Check(close.Limit <= 1.21f && tightGroup.Limit <= .76f, "Compact framing received an excessive position correction.");
    }
    private static void TotalDisplacement()
    {
        var p = Open();
        Check(!p.WithinBudget(p.Limit * .8f, 0, p.Limit * .8f),
            "A native XY correction was admitted merely because longitudinal dolly was within range.");
        Check(p.WithinBudget(p.Limit * .5f, p.Limit * .5f, p.Limit * .5f), "Bounded mixed-axis movement was unnecessarily forbidden.");
    }
    private static void Rejection()
    {
        var p = Open(); p.Reject(.2);
        Check(!p.Allowed, "Rejected movement stayed enabled in the current solve.");
        p.Configure(2.19, true, true, true, 1, 10, 20);
        Check(!p.Allowed, "Blocked view retried too quickly.");
        p.Configure(2.21, true, true, true, 1, 10, 20);
        Check(p.Allowed, "Bounded retry never released after fresh open-space observations.");
    }
    private static void Cooldown()
    {
        var p = Open(); p.Reject(1);
        for (int i = 10; i < 30; i++)
        {
            p.Configure(i * .1, true, true, true, 1, 10, 20);
            Check(!p.Allowed, "Repeated observations prematurely released blocked motion.");
        }
        p.Configure(3, true, true, true, 1, 10, 20);
        Check(p.Allowed, "Observations renewed the rejection deadline forever.");
    }
    private static void InvalidEvidence()
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var p = Open(); p.Configure(1, true, true, true, 1, value, 20);
            Check(!p.Allowed, "Invalid group spread retained old motion permission.");
            p = Open(); p.Configure(1, true, true, true, value, 10, 20);
            Check(!p.Allowed, "Invalid camera-space observation retained old permission.");
            p = Open(); p.Configure(1, true, true, true, 1, 10, value);
            Check(!p.Allowed, "Invalid subject distance retained old permission.");
        }
    }
    private static void InvalidMotion()
    {
        var p = Open();
        Check(!p.WithinBudget(float.NaN, 0, 0) && !p.WithinBudget(0, float.PositiveInfinity, 0) &&
            !p.WithinBudget(float.MaxValue, float.MaxValue, float.MaxValue), "Invalid native displacement passed the total-motion budget.");
    }
    private static void Reset()
    {
        var p = Open(); p.Reject(10); p.Reset();
        Check(!p.Allowed, "Reset retained a prior camera allowance.");
        p.Configure(11, true, true, true, 1, 10, 20);
        Check(p.Allowed, "New world inherited the previous rejection cooldown.");
        p.Reject(12); p.Configure(1, true, true, true, 1, 10, 20);
        Check(p.Allowed, "A reversed clock retained a distant future rejection.");
    }
}
