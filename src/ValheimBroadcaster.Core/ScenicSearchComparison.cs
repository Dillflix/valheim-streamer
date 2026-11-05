using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>One bounded preparation episode. Scheduled slots and skipped work
/// do not establish comparison breadth. Only completed full preflight IDs and
/// distinct, currently retained eligible compositions can finish comparison.</summary>
public sealed class ScenicSearchComparison
{
    public const int Capacity = 3, TargetEvaluated = 4;
    private const int MaximumEvaluated = 512;
    private readonly List<Entry> entries = new List<Entry>(Capacity);
    private readonly HashSet<long> evaluatedIds = new HashSet<long>();
    private readonly HashSet<long> validatedIds = new HashSet<long>();
    private readonly HashSet<long> latestValidIds = new HashSet<long>();
    private readonly HashSet<long> eligibleIds = new HashSet<long>();
    private readonly IReadOnlyList<Entry> view;
    private string? fastReason;
    private double lastDecisionAt;
    public ScenicSearchComparison() { view = entries.AsReadOnly(); }
    public readonly struct Entry
    {
        public readonly ScenicShotPlan Plan;
        public readonly double Score, ObservedAt;
        public readonly long Epoch;
        internal Entry(ScenicShotPlan plan, double score, long epoch, double observedAt)
        { Plan = plan; Score = score; Epoch = epoch; ObservedAt = observedAt; }
    }
    public bool Active { get; private set; }
    public double StartedAt { get; private set; }
    public double Deadline { get; private set; }
    public double DrainDeadline { get; private set; }
    public int Scheduled { get; private set; }
    public int Skipped { get; private set; }
    public int Planned { get; private set; }
    public int EvaluationPasses { get; private set; }
    public int Evaluated => evaluatedIds.Count;
    public int Validated => validatedIds.Count;
    public int Eligible => eligibleIds.Count;
    public int Retained => entries.Count;
    public IReadOnlyList<Entry> Entries => view;

    public void Begin(double now, double budgetSeconds, string? fastReason = null, double pendingGraceSeconds = 0)
    {
        Time(now); Time(budgetSeconds); Time(pendingGraceSeconds);
        if (!Finite(now + budgetSeconds) || !Finite(now + budgetSeconds + pendingGraceSeconds)) throw new ArgumentOutOfRangeException(nameof(budgetSeconds));
        if (fastReason != null && string.IsNullOrWhiteSpace(fastReason)) throw new ArgumentException("Expected a named fast-search purpose.", nameof(fastReason));
        Reset(); Active = true; StartedAt = lastDecisionAt = now; Deadline = now + budgetSeconds; DrainDeadline = Deadline + pendingGraceSeconds; this.fastReason = fastReason;
    }
    public void Reset()
    {
        entries.Clear(); evaluatedIds.Clear(); validatedIds.Clear(); latestValidIds.Clear(); eligibleIds.Clear();
        Active = false; StartedAt = Deadline = DrainDeadline = lastDecisionAt = 0; Scheduled = Skipped = Planned = EvaluationPasses = 0; fastReason = null;
    }
    /// <summary>Finish an episode without erasing its diagnostic result.</summary>
    public void End() { Active = false; }
    public void Schedule() { RequireActive(); Scheduled = checked(Scheduled + 1); }
    public void Skip() { RequireActive(); Skipped = checked(Skipped + 1); }
    public void PlannedRoute() { RequireActive(); Planned = checked(Planned + 1); }

    /// <summary>A salvage/recheck pass is useful work, but cannot become a new
    /// comparison alternative merely by completing the same plan ID again.</summary>
    public void EvaluatedRoute(long planId, bool geometryValid)
    {
        RequireActive();
        if (planId <= 0) throw new ArgumentOutOfRangeException(nameof(planId));
        if (!evaluatedIds.Contains(planId) && evaluatedIds.Count >= MaximumEvaluated)
            throw new InvalidOperationException("Scenic comparison exceeds bounded candidate capacity.");
        EvaluationPasses = checked(EvaluationPasses + 1); evaluatedIds.Add(planId);
        if (geometryValid) { validatedIds.Add(planId); latestValidIds.Add(planId); }
        else { latestValidIds.Remove(planId); entries.RemoveAll(entry => entry.Plan.Id == planId); }
    }

    /// <summary>Offer only after complete geometry validation and editorial
    /// eligibility. Scores are relative; a negative score is still usable.</summary>
    public bool Offer(ScenicShotPlan plan, double rank, long epoch, double observedAt)
    {
        RequireActive(); Time(observedAt);
        if (plan == null || !plan.IsDirected || plan.IsPictureOnly)
            throw new ArgumentException("Expected a fully evaluated cinematic route.", nameof(plan));
        if (!latestValidIds.Contains(plan.Id)) throw new InvalidOperationException("Unvalidated route cannot enter comparison.");
        if (!Finite(rank)) throw new ArgumentOutOfRangeException(nameof(rank));
        if (observedAt < StartedAt) throw new ArgumentOutOfRangeException(nameof(observedAt));
        eligibleIds.Add(plan.Id);
        var offered = new Entry(plan, rank, epoch, observedAt);
        // The same ID represents a new measured version of that route. Its
        // fresh evidence replaces the earlier version even if its score fell.
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].Plan.Id == plan.Id) entries.RemoveAt(i);
        foreach (Entry entry in entries)
            if (Equivalent(entry.Plan, plan) && entry.Score >= rank) return false;
        entries.RemoveAll(entry => Equivalent(entry.Plan, plan));
        entries.Add(offered); entries.Sort(Compare);
        if (entries.Count > Capacity) entries.RemoveAt(entries.Count - 1);
        foreach (Entry entry in entries) if (entry.Plan.Id == plan.Id) return true;
        return false;
    }

    /// <summary>Eligibility is current adapter evidence, never a permanent
    /// property of a route. Removing one candidate preserves other options.</summary>
    public void Prune(Predicate<Entry> retain)
    {
        if (retain == null) throw new ArgumentNullException(nameof(retain));
        entries.RemoveAll(entry => !retain(entry));
    }

    /// <summary>A reason ends search, not validation. Deadline/exhaustion with
    /// no retained candidate must remain explicitly unavailable at the adapter.</summary>
    public string? FinishReason(double now, bool hasUsablePicture, bool editDue, bool unfinishedCandidate = false)
    {
        Time(now);
        if (!Active) return null;
        if (now < lastDecisionAt) return "clock changed";
        lastDecisionAt = now;
        if (Retained > 0)
        {
            if (fastReason != null) return fastReason;
            if (!hasUsablePicture) return "picture recovery";
            if (editDue) return "edit due";
            if (Evaluated >= TargetEvaluated && Retained >= Capacity) return "comparison complete";
        }
        // Once the comparison budget ends, the adapter starts no new work.
        // One already-running first candidate may finish in a fixed drain
        // interval. Neither polling, retries nor a missing route renew it.
        if (now >= Deadline && Retained == 0 && unfinishedCandidate && now < DrainDeadline) return null;
        return now >= Deadline ? "preparation deadline" : null;
    }

    private static int Compare(Entry a, Entry b)
    {
        int score = b.Score.CompareTo(a.Score);
        return score != 0 ? score : a.Plan.Id.CompareTo(b.Plan.Id);
    }
    private static bool Equivalent(ScenicShotPlan a, ScenicShotPlan b)
    {
        // Reuse the director's 25-degree / 1.35-scale novelty boundary. A
        // parallel path a few metres away is not another useful wide option.
        // Role labels and feature radii cannot relabel the same picture.
        if (a.IsPan != b.IsPan) return false;
        return SameComposition(a, b, false) || SameComposition(a, b, true);
    }
    private static bool SameComposition(ScenicShotPlan a, ScenicShotPlan b, bool reverse)
    {
        ScenicShotSample previousA = default, previousB = default;
        for (int i = 0; i <= 4; i++)
        {
            double at = i / 4d;
            ScenicShotSample x = a.Sample(at), y = b.Sample(reverse ? 1 - at : at);
            if (x.AimPosition.Distance(y.AimPosition) > 2 ||
                Math.Abs(x.ScreenX - y.ScreenX) > .08 || Math.Abs(x.ScreenY - y.ScreenY) > .08) return false;
            double ax = x.CameraPosition.X - x.AimPosition.X, ay = x.CameraPosition.Y - x.AimPosition.Y, az = x.CameraPosition.Z - x.AimPosition.Z;
            double bx = y.CameraPosition.X - y.AimPosition.X, by = y.CameraPosition.Y - y.AimPosition.Y, bz = y.CameraPosition.Z - y.AimPosition.Z;
            double lengthA = Math.Sqrt(ax * ax + ay * ay + az * az), lengthB = Math.Sqrt(bx * bx + by * by + bz * bz);
            if ((ax * bx + ay * by + az * bz) / (lengthA * lengthB) < .906307787) return false; // cos(25 degrees)
            double scaleA = lengthA * Math.Tan(a.FieldOfView * Math.PI / 360);
            double scaleB = lengthB * Math.Tan(b.FieldOfView * Math.PI / 360);
            if (Math.Max(scaleA, scaleB) / Math.Min(scaleA, scaleB) >= 1.35) return false;
            // Keep genuinely different movement families: a rise and a lateral
            // move remain choices even when their opening composition matches.
            if (i > 0 && !SimilarMotion(previousA.CameraPosition, x.CameraPosition,
                previousB.CameraPosition, y.CameraPosition)) return false;
            previousA = x; previousB = y;
        }
        return true;
    }
    private static bool SimilarMotion(AutonomyPosition a0, AutonomyPosition a1, AutonomyPosition b0, AutonomyPosition b1)
    {
        double ax = a1.X - a0.X, ay = a1.Y - a0.Y, az = a1.Z - a0.Z;
        double bx = b1.X - b0.X, by = b1.Y - b0.Y, bz = b1.Z - b0.Z;
        double lengthA = Math.Sqrt(ax * ax + ay * ay + az * az), lengthB = Math.Sqrt(bx * bx + by * by + bz * bz);
        if (lengthA < .1 || lengthB < .1) return lengthA < .1 && lengthB < .1;
        return (ax * bx + ay * by + az * bz) / (lengthA * lengthB) >= .707106781; // 45 degrees
    }
    private void RequireActive() { if (!Active) throw new InvalidOperationException("Begin Scenic comparison before recording work."); }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static void Time(double value) { if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
}
