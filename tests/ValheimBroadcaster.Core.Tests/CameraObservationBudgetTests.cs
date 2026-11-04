using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CameraObservationBudgetTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Background space surveys reserve work for on-air prediction", Reserved);
        yield return ("Repeated point and corridor calls cannot renew a frame budget", SameFrame);
        yield return ("On-air work and background work share one total bound", SharedTotal);
        yield return ("Budget failures leave evidence-query allowance unchanged", FailedSpend);
        yield return ("A new frame restores bounded work without carrying unused credits", NextFrame);
        yield return ("World reset invalidates budget until an actual frame begins", Reset);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Reserved()
    {
        var budget = new CameraObservationBudget(); budget.BeginFrame(1);
        for (int i = 0; i < 6; i++) Check(budget.TrySpend(false, 6), "Expected background point allowance");
        Check(!budget.TrySpend(false, 2), "Background consumed on-air reserve");
        for (int i = 0; i < 4; i++) Check(budget.TrySpend(true, 6), "On-air reserve was unavailable");
        Check(!budget.TrySpend(true, 1), "Total bound exceeded");
    }
    private static void SameFrame()
    {
        var budget = new CameraObservationBudget(); budget.BeginFrame(10);
        Check(budget.TrySpend(false, 36), "Initial allowance missing");
        for (int i = 0; i < 100; i++) { budget.BeginFrame(10); Check(!budget.TrySpend(false, 1), "Repeated BeginFrame renewed budget"); }
    }
    private static void SharedTotal()
    {
        var budget = new CameraObservationBudget(); budget.BeginFrame(2);
        Check(budget.TrySpend(true, 30) && budget.TrySpend(false, 30), "Mixed work rejected within bound");
        Check(!budget.TrySpend(false, 1) && !budget.TrySpend(true, 1), "Mixed queries exceeded global work bound");
        Check(budget.BackgroundSpent == 30 && budget.Spent == 60, "Work accounting drifted");
    }
    private static void FailedSpend()
    {
        var budget = new CameraObservationBudget(); budget.BeginFrame(3);
        Check(!budget.TrySpend(false, 40), "Oversized background request succeeded");
        Check(budget.Spent == 0 && budget.BackgroundSpent == 0, "Rejected request consumed work");
        foreach (int units in new[] { 0, -1, 61 })
        {
            bool threw = false;
            try { budget.TrySpend(true, units); } catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw && budget.Spent == 0, "Malformed work mutated budget");
        }
    }
    private static void NextFrame()
    {
        var budget = new CameraObservationBudget(); budget.BeginFrame(4); budget.TrySpend(true, 1);
        budget.BeginFrame(5);
        Check(budget.TrySpend(true, 60) && !budget.TrySpend(true, 1), "New frame accumulated unused credits");
    }
    private static void Reset()
    {
        var budget = new CameraObservationBudget();
        Check(!budget.TrySpend(true, 1), "Uninitialized query accepted");
        budget.BeginFrame(1); budget.TrySpend(false, 6); budget.Reset();
        Check(budget.Spent == 0 && !budget.TrySpend(false, 1), "Reset retained old frame permission");
        budget.BeginFrame(1); Check(budget.TrySpend(false, 36), "New session did not receive its bounded frame");
    }
}
