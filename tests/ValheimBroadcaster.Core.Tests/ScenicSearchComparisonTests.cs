using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class ScenicSearchComparisonTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic skipped schedule slots cannot complete candidate comparison", SkipsDoNotCompare);
        yield return ("Scenic comparison requires completed preflights and distinct retained options", RealComparison);
        yield return ("Scenic salvage passes and relabelled geometry do not inflate comparison breadth", DuplicateEvidence);
        yield return ("Scenic retained pool ranks negative scores and preserves alternatives after pruning", RetainedAlternatives);
        yield return ("Scenic failed revalidation retires the corresponding retained candidate", FailedRevalidation);
        yield return ("Scenic recovery and revalidation use first available route without waiting for breadth", FastAvailability);
        yield return ("Scenic preparation deadline ends pending or unsuccessful comparison without fabricating a route", Deadline);
        yield return ("Scenic first-candidate drain covers spatial then foliage work without renewing its hard deadline", PendingDrain);
        yield return ("Scenic comparison reset and clock validation do not carry old candidates into a new search", Lifecycle);
        yield return ("Scenic duplicate matching cannot retain overlapping equivalence neighbours", OverlappingEquivalence);
        yield return ("Scenic pool rejects nearby parallel wide routes but keeps actual scale angle and motion alternatives", MeaningfulAlternatives);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicShotPlan Plan(long id, float x, ScenicShotRole role = ScenicShotRole.Establish, float fov = 75)
    {
        var feature = new ScenicFeature("feature-" + id, ScenicFeatureKind.Structure,
            new AutonomyPosition(0, 20, 0), new AutonomyPosition(1, 0, 0), 8, 8, .8f);
        return ScenicShotPlan.Directed(id, feature, role,
            new AutonomyPosition(x, 24, -30), new AutonomyPosition(x + 10, 24, -30),
            feature.Center, feature.Center, fov);
    }
    private static bool Add(ScenicSearchComparison p, ScenicShotPlan plan, double score = .8, long epoch = 7, double at = 1)
    {
        p.Schedule(); p.PlannedRoute(); p.EvaluatedRoute(plan.Id, true);
        return p.Offer(plan, score, epoch, at);
    }
    private static void SkipsDoNotCompare()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        for (int i = 0; i < 40; i++) { p.Schedule(); p.Skip(); }
        Add(p, Plan(1, 0));
        Check(p.Scheduled == 41 && p.Skipped == 40 && p.Planned == 1 && p.Evaluated == 1 && p.Validated == 1 && p.Retained == 1,
            "Skipped work contaminated evaluated or retained counters.");
        Check(p.FinishReason(2, true, false) == null, "Forty skipped slots completed a one-option comparison.");
        Check(p.FinishReason(6, true, false) == "preparation deadline" && p.Entries[0].Plan.Id == 1,
            "Deadline did not release the only measured route.");
    }
    private static void RealComparison()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        Add(p, Plan(1, 0)); Add(p, Plan(2, 24, ScenicShotRole.Traverse)); Add(p, Plan(3, -24, ScenicShotRole.Reveal));
        Check(p.Retained == 3 && p.FinishReason(2, true, false) == null, "Three options skipped the minimum completed-comparison effort.");
        p.Schedule(); p.PlannedRoute(); p.EvaluatedRoute(4, false);
        Check(p.Evaluated == 4 && p.Validated == 3 && p.Eligible == 3 && p.FinishReason(3, true, false) == "comparison complete",
            "A completed rejected alternative did not count as real comparison work.");
    }
    private static void DuplicateEvidence()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6); var first = Plan(1, 0);
        p.Schedule(); p.PlannedRoute(); p.EvaluatedRoute(1, false);
        for (int i = 0; i < 8; i++) { p.EvaluatedRoute(1, true); p.Offer(first, .8, 7, 1); }
        Check(p.Evaluated == 1 && p.Validated == 1 && p.EvaluationPasses == 9 && p.Retained == 1,
            "Salvage/recheck work multiplied the candidate count.");
        Add(p, Plan(2, 0, ScenicShotRole.Detail), .7);
        Add(p, Plan(3, 0, ScenicShotRole.Reveal), .6);
        Add(p, Plan(4, 0, ScenicShotRole.Traverse), .5);
        Check(p.Evaluated == 4 && p.Validated == 4 && p.Eligible == 4 && p.Retained == 1 &&
            p.FinishReason(2, true, false) == null,
            "Different feature IDs or role labels made identical geometry into competing alternatives.");
        Add(p, Plan(5, 0, ScenicShotRole.Detail, 45), .7);
        Check(p.Retained == 2, "A substantial actual lens change was discarded as a relabelled view.");
    }
    private static void RetainedAlternatives()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        Add(p, Plan(1, 0), -2); Add(p, Plan(2, 24), -1); Add(p, Plan(3, -24), -.3); Add(p, Plan(4, 45), -4);
        Check(p.Retained == 3 && p.Eligible == 4 && p.Entries.Select(e => e.Plan.Id).SequenceEqual(new long[] { 3, 2, 1 }),
            "Negative editorial scores discarded valid footage or broke best-first retention.");
        p.Prune(entry => entry.Plan.Id != 3);
        Check(p.Retained == 2 && p.Entries[0].Plan.Id == 2 && p.FinishReason(3, true, false) == null,
            "Removing an ineligible winner lost alternatives or left false three-option readiness.");
        Add(p, Plan(5, -45), -.2, epoch: 8, at: 4);
        p.Prune(entry => entry.Epoch == 7 && entry.ObservedAt >= 1);
        Check(p.Retained == 2 && p.Entries.All(e => e.Epoch == 7), "Epoch filtering lost unrelated valid alternatives.");
        p.Prune(entry => false);
        Check(p.Retained == 0 && p.FinishReason(6, true, false) == "preparation deadline", "Pruning manufactured a candidate.");
    }
    private static void FailedRevalidation()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        var a = Plan(1, 0); Add(p, a); Add(p, Plan(2, 24));
        p.EvaluatedRoute(a.Id, false);
        Check(p.Retained == 1 && p.Entries[0].Plan.Id == 2 && p.Evaluated == 2 && p.EvaluationPasses == 3,
            "Failed current evidence left its old retained version eligible.");
        bool failed = false;
        try { p.Offer(a, 1, 7, 2); } catch (InvalidOperationException) { failed = true; }
        Check(failed, "Historical pass authorized a route after its latest revalidation failed.");
        p.EvaluatedRoute(a.Id, true); p.Offer(a, -1, 7, 3);
        Check(p.Evaluated == 2 && p.Retained == 2, "Fresh successful evidence did not restore the route without inventing a new comparison.");
    }
    private static void FastAvailability()
    {
        foreach (string purpose in new[] { "initial acquisition", "revalidation" })
        {
            var p = new ScenicSearchComparison(); p.Begin(0, 10, purpose);
            Check(p.FinishReason(1, false, true) == null, "Fast mode fabricated an available picture.");
            Add(p, Plan(1, 0));
            Check(p.FinishReason(1.1, false, true) == purpose && p.Evaluated == 1,
                "Fast mode waited for alternatives after its first usable route.");
        }
        var ordinary = new ScenicSearchComparison(); ordinary.Begin(0, 6); Add(ordinary, Plan(1, 0));
        Check(ordinary.FinishReason(2, false, false) == "picture recovery", "Picture loss waited for ordinary comparison breadth.");
        Check(ordinary.FinishReason(2.1, true, true) == "edit due", "An outgoing completed/blocked shot waited unnecessarily.");
    }
    private static void Deadline()
    {
        var p = new ScenicSearchComparison(); p.Begin(10, 6);
        p.Schedule(); p.PlannedRoute(); // Preflight is still pending, not rejected or validated.
        Check(p.FinishReason(15.999, true, false) == null && p.FinishReason(16, true, false) == "preparation deadline",
            "An unfinished evaluator extended the preparation deadline.");
        Check(p.Evaluated == 0 && p.Retained == 0, "Deadline manufactured completed work or usable footage.");
        p.Begin(20, 0);
        Check(p.FinishReason(20, false, true) == "preparation deadline", "An already-due preparation budget was renewed.");
    }
    private static void PendingDrain()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6, pendingGraceSeconds: 4);
        p.Schedule();
        // Recorded pipeline limits allow five seconds of spatial discovery
        // followed by two seconds of incomplete foliage projection/preflight.
        Check(p.FinishReason(4.9, true, false, true) == null, "Spatial search ended before its normal budget.");
        p.PlannedRoute();
        Check(p.FinishReason(6, true, false, true) == null && p.FinishReason(7, true, false, true) == null,
            "The first candidate was aborted between spatial discovery and full preflight.");
        p.EvaluatedRoute(1, true); p.Offer(Plan(1, 0), -.5, 7, 7.1);
        Check(p.FinishReason(7.1, true, false, false) == "preparation deadline" && p.Retained == 1 && p.Scheduled == 1,
            "Completed drain work unnecessarily waited for broader comparison.");

        p.Begin(20, 6, pendingGraceSeconds: 4); p.Schedule(); p.PlannedRoute();
        Check(p.FinishReason(26.5, true, false, true) == null, "An already-running candidate lost its bounded grace.");
        p.EvaluatedRoute(2, false);
        Check(p.FinishReason(27, true, false, false) == "preparation deadline" && p.Scheduled == 1 && p.Retained == 0,
            "A failed drained candidate renewed work or fabricated a route.");

        p.Begin(40, 6, pendingGraceSeconds: 4); p.Schedule();
        Check(p.Deadline == 46 && p.DrainDeadline == 50, "Search did not bind fixed soft/hard deadlines.");
        for (double at = 46; at < 50; at += .25)
            Check(p.FinishReason(at, true, false, true) == null && p.DrainDeadline == 50, "Polling extended or prematurely ended drain grace.");
        Check(p.FinishReason(50, true, false, true) == "preparation deadline", "Unfinished work escaped the ten-second hard bound.");
        p.Begin(60, 6, "initial acquisition"); p.Schedule();
        Check(p.FinishReason(66, false, true, true) == "preparation deadline", "Fast acquisition silently gained normal-search grace.");
    }
    private static void Lifecycle()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6); Add(p, Plan(1, 0)); p.End();
        Check(!p.Active && p.Evaluated == 1 && p.Retained == 1 && p.FinishReason(6, true, false) == null,
            "Ending search erased diagnostic results or allowed another completion.");
        bool recordAfterEnd = false;
        try { p.Schedule(); } catch (InvalidOperationException) { recordAfterEnd = true; }
        Check(recordAfterEnd, "Finished episode accepted new work.");
        p.Reset();
        Check(!p.Active && p.Entries.Count == 0 && p.Scheduled == 0 && p.Evaluated == 0 && p.FinishReason(0, true, false) == null,
            "Reset carried earlier world/search evidence into the next episode.");
        p.Begin(10, 6);
        Check(p.FinishReason(12, true, false) == null && p.FinishReason(11, true, false) == "clock changed",
            "Clock rollback within the episode was ignored.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            bool failed = false;
            try { p.Begin(invalid, 6); } catch (ArgumentOutOfRangeException) { failed = true; }
            Check(failed, "Invalid search clock accepted.");
        }
        bool invalidRank = false; p.Begin(0, 6); p.EvaluatedRoute(1, true);
        try { p.Offer(Plan(1, 0), double.NaN, 7, 1); } catch (ArgumentOutOfRangeException) { invalidRank = true; }
        Check(invalidRank, "NaN rank entered the retained comparison pool.");
    }
    private static void MeaningfulAlternatives()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        var first = Plan(1, 0); Add(p, first, .9);
        Add(p, Plan(2, 2.1f, ScenicShotRole.Traverse), .8);
        Add(p, Plan(3, 3, ScenicShotRole.Reveal), .7);
        Check(p.Retained == 1, "A few metres of parallel movement manufactured different wide compositions.");
        Add(p, Plan(4, 24), .8);
        Add(p, Plan(5, 0, ScenicShotRole.Detail, 45), .7);
        Check(p.Retained == 3, "Meaningfully different view angle or apparent scale was merged.");
        p.Begin(0, 6); Add(p, first, .9);
        var rise = ScenicShotPlan.Directed(6, first.Feature!, ScenicShotRole.Reveal,
            new AutonomyPosition(0, 24, -30), new AutonomyPosition(0, 34, -30),
            first.Feature!.Center, first.Feature.Center, 75);
        Add(p, rise, .8);
        Check(p.Retained == 2, "A vertical rise was collapsed into a lateral movement family.");
    }
    private static void OverlappingEquivalence()
    {
        var p = new ScenicSearchComparison(); p.Begin(0, 6);
        Add(p, Plan(1, 0), .5); Add(p, Plan(2, 24), .6);
        Check(p.Retained == 2, "Separated paths were unexpectedly equivalent.");
        Add(p, Plan(3, 12), .8);
        Check(p.Retained == 1 && p.Entries[0].Plan.Id == 3,
            "A better midpoint route left two equivalent neighbours inflating the pool.");
    }
}
