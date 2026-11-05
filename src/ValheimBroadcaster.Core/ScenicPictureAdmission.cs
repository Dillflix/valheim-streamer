using System;

namespace ValheimBroadcaster.Core;

public enum ScenicPictureAdmissionResult { None, Pending, Accepted, Rejected, Expired, Cancelled }

/// <summary>A single exact-pose proposal owns its admission work until complete.
/// New cinematic graph nodes cannot replace or starve that work. A proposal is
/// never presentation evidence: the adapter must complete all current checks.</summary>
public sealed class ScenicPictureAdmission
{
    public ScenicShotPlan? Pending { get; private set; }
    public long Epoch { get; private set; }
    public string Reason { get; private set; } = "No pending independent picture";
    public double StartedAt { get; private set; }
    public int Evaluations { get; private set; }
    private int lastFrame = -1;

    public bool TryQueue(ScenicShotPlan candidate, long observedEpoch, long currentEpoch, double observedAt, double now)
    {
        Time(now); Time(observedAt);
        if (candidate == null || !candidate.IsPictureOnly)
            throw new ArgumentException("Expected an exact independent picture proposal.", nameof(candidate));
        if (Pending != null || observedEpoch != currentEpoch || now < observedAt || now - observedAt > .5) return false;
        Pending = candidate; Epoch = observedEpoch; StartedAt = now; Evaluations = 0;
        Reason = "Validating the retained independent picture";
        return true;
    }

    public ScenicPictureAdmissionResult Advance(long currentEpoch, double now, int frame,
        Func<ScenicShotPlan, ScenicPictureAdmissionResult> evaluate, out ScenicShotPlan? admitted)
    {
        Time(now);
        if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
        admitted = null;
        if (Pending == null) return ScenicPictureAdmissionResult.None;
        if (Epoch != currentEpoch || now < StartedAt)
        { Release("Independent picture scene or clock changed"); return ScenicPictureAdmissionResult.Cancelled; }
        if (now - StartedAt >= ScenicObservationPolicy.SurveyLifetime)
        { Release("Independent picture observation deadline expired"); return ScenicPictureAdmissionResult.Expired; }
        if (frame == lastFrame) return ScenicPictureAdmissionResult.Pending;
        lastFrame = frame; Evaluations++;
        ScenicShotPlan candidate = Pending;
        ScenicPictureAdmissionResult result = evaluate(candidate);
        if (result == ScenicPictureAdmissionResult.Accepted)
        { admitted = candidate; Release("Independent picture passed complete current validation"); }
        else if (result == ScenicPictureAdmissionResult.Rejected)
            Release("Independent picture was contradicted by current validation");
        else if (result != ScenicPictureAdmissionResult.Pending)
            throw new ArgumentException("Validation must report pending, accepted or rejected.", nameof(evaluate));
        return result;
    }

    public void Reset()
    { Release("No pending independent picture"); lastFrame = -1; StartedAt = 0; Evaluations = 0; }

    private void Release(string reason)
    { Pending = null; Epoch = 0; Reason = reason; }
    private static void Time(double value)
    { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
}
