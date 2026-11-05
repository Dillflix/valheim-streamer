using System;

namespace ValheimBroadcaster.Core;

/// <summary>Execution clock for one certified Journey section or its finite
/// arrival pan or stationary refuge. It owns no editorial replacement policy or mission deadline.
/// Proposals are previews; only matching, readable live presentation commits
/// progress. Scene validation and the Journey controller retain all authority
/// to pause, repair, admit a successor or end the programme.</summary>
public sealed class JourneyRoutePlayback
{
    private const double ProgressTolerance = .00001;
    private double acceptedAt, lastObservation = -1, proposedCeiling;
    private bool precedingLive, precedingBlocked;

    public ScenicShotPlan? Current { get; private set; }
    public double Progress { get; private set; }
    public bool HasPresented { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double BlockedSeconds { get; private set; }
    public bool PhysicalBlocked { get; private set; }
    public bool PictureAvailable { get; private set; }

    public void Accept(ScenicShotPlan plan, double at)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (!plan.IsDirected || !plan.IsJourney && !plan.IsPan && !plan.IsPictureOnly)
            throw new ArgumentException("Journey playback requires a certified section, ending pan or stationary refuge.", nameof(plan));
        Number(at, nameof(at));
        Reset(); Current = plan; acceptedAt = at;
    }

    /// <summary>The caller converts its actual cruise to this plan's authored
    /// clock before proposing. MotionFraction/native Dolly owns easing and
    /// distance mapping exactly once. No paused or skipped time accumulates.</summary>
    public double Propose(double deltaSeconds, bool canMove = true)
    {
        Number(deltaSeconds, nameof(deltaSeconds));
        if (Current == null || Current.IsPictureOnly || !HasPresented || !PictureAvailable || !canMove || PhysicalBlocked)
            return Progress;
        double proposal = Math.Min(1, Progress + Math.Min(deltaSeconds, .1) / Current.DurationSeconds);
        proposedCeiling = Math.Max(proposedCeiling, proposal);
        return proposal;
    }

    /// <summary>Scale wall time by live cruise relative to the cruise used to
    /// certify this section. Dividing by the mission request would slow an
    /// already retimed bend a second time. The controller must honor the
    /// section ceiling; output gaps and holds still use the normal preview gate.</summary>
    public double ProposeAtCruise(double deltaSeconds, double actualCruise, double requestedCruise, bool canMove = true)
    {
        Number(deltaSeconds, nameof(deltaSeconds)); Number(actualCruise, nameof(actualCruise));
        Number(requestedCruise, nameof(requestedCruise));
        if (requestedCruise < 2 || requestedCruise > 12) throw new ArgumentOutOfRangeException(nameof(requestedCruise));
        double authored = Current != null && Current.JourneyCruiseSpeed > 0 ? Current.JourneyCruiseSpeed : requestedCruise;
        if (actualCruise > Math.Min(authored, requestedCruise) + .000001)
            throw new ArgumentOutOfRangeException(nameof(actualCruise), "Live cruise exceeds the certified section speed.");
        double step = Math.Min(deltaSeconds, Math.Min(.1, ScenicJourneyPolicy.MaximumStepMetres / Math.Max(.01, actualCruise)));
        return Propose(step * actualCruise / authored, canMove);
    }

    /// <summary>A physical validation failure pauses this section without
    /// requesting an unrelated Scenic shot. Resume only after fresh validation.
    /// Missing output alone must never be reported as a physical obstruction.</summary>
    public void Pause()
    {
        if (!PhysicalBlocked) BlockedSeconds = 0;
        PhysicalBlocked = true; precedingBlocked = false;
    }

    public void Resume()
    {
        PhysicalBlocked = false; BlockedSeconds = 0; precedingBlocked = false;
    }

    public void Observe(long planId, double at, double displayedProgress,
        bool livePicture, bool readable, bool motionBlocked, bool held = false)
    {
        Number(at, nameof(at)); Number(displayedProgress, nameof(displayedProgress));
        if (displayedProgress > 1) throw new ArgumentOutOfRangeException(nameof(displayedProgress));
        if (Current == null || Current.Id != planId || at < acceptedAt || at <= lastObservation) return;
        bool live = livePicture && readable;
        // Stale or never-proposed movement cannot manufacture a rendered route.
        // Float capture conversion may differ slightly from the double preview.
        if (live && (displayedProgress > proposedCeiling + ProgressTolerance ||
                     displayedProgress + ProgressTolerance < Progress)) return;

        double gap = lastObservation >= 0 ? at - lastObservation : 0;
        double shownDelta = gap <= .25 ? gap : 0;
        double blockedDelta = gap <= 2 ? Math.Min(gap, 1) : 0;
        lastObservation = at;
        PictureAvailable = live;
        if (motionBlocked)
        {
            PhysicalBlocked = true;
            if (!held && precedingBlocked) BlockedSeconds += blockedDelta;
            else if (gap > 2 || held) BlockedSeconds = 0;
        }
        else if (live && !held) Resume();

        if (live)
        {
            HasPresented = true;
            if (!held)
            {
                Progress = Math.Max(Progress, Math.Min(proposedCeiling, displayedProgress));
                proposedCeiling = Progress;
                if (precedingLive) ElapsedSeconds += shownDelta;
            }
        }
        // A gap/hold breaks credited presentation time but retains the exact
        // last accepted route pose. It neither ages out the route nor requests
        // a replacement; fresh output can resume this same immutable section.
        precedingLive = live && !held;
        precedingBlocked = motionBlocked && !held;
    }

    public void Reset()
    {
        Current = null;
        Progress = ElapsedSeconds = BlockedSeconds = acceptedAt = proposedCeiling = 0;
        lastObservation = -1;
        HasPresented = PictureAvailable = PhysicalBlocked = precedingLive = precedingBlocked = false;
    }

    private static void Number(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
