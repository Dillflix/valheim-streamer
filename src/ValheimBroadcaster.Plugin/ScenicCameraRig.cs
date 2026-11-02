using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Execution-only scenic camera. The dedicated director supplies the
/// immutable shot and proposed progress; this rig returns the accepted pose and
/// progress. It never chooses a different viewpoint, changes the lens or drives
/// an actor-style transition. New scenic shots enter with an intentional cut.</summary>
internal sealed class ScenicCameraRig : IDisposable
{
    private readonly ScenicPoseSolver solver = new ScenicPoseSolver();
    private readonly ScenicSplinePath path = new ScenicSplinePath();
    private readonly ScenicVisibility ownedVisibility = new ScenicVisibility();
    private ScenicShotPlan? plan;
    private ZNet? world;
    private Player? carrier;
    private ScenicPose accepted;
    private bool hasPose;
    private float aspect, nearClip;
    private long pictureEpoch = 1000000000;
    private RenderedPictureContext pictureContext;
    private readonly int[] rejected = new int[Enum.GetValues(typeof(ScenicRejectionKind)).Length];
    internal bool LastPictureUsable { get; private set; }
    internal bool MovementAccepted { get; private set; }
    internal float PresentedProgress => hasPose ? accepted.Progress : 0;
    internal long PresentedPlanId => hasPose ? plan?.Id ?? 0 : 0;
    internal ScenicRejectionKind FailureKind { get; private set; }
    internal string LastFailure { get; private set; } = "Awaiting a directed scenic shot";
    internal string Diagnostics { get; private set; } = "Scenic director: awaiting a prepared shot";
    internal RenderedPictureMonitor? PictureMonitor { get; set; }
    internal RenderedPictureContext? PictureContext => LastPictureUsable && pictureContext.Valid &&
        pictureContext.Frame == Time.frameCount ? pictureContext : (RenderedPictureContext?)null;

    internal bool TrySolve(ScenicShotPlan shot, float progress, ScenicVisibility? suppliedVisibility,
        float cameraAspect, float cameraNearClip, bool continuous, bool moveCarrier,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = default; rotation = Quaternion.identity; fov = shot.FieldOfView;
        LastPictureUsable = MovementAccepted = false; pictureContext = default;
        bool changed = !ReferenceEquals(plan, shot) || world != ZNet.instance || carrier != Player.m_localPlayer;
        if (changed)
        {
            Reset(); plan = shot; world = ZNet.instance; carrier = Player.m_localPlayer;
            aspect = cameraAspect; nearClip = cameraNearClip;
            if (!shot.IsDirected || !path.Prepare(shot, 1))
                return Reject(ScenicRejectionKind.InvalidPlan, "Cannot prepare directed scenic native route");
        }
        if (!Finite(progress) || progress < 0 || progress > 1 || !Finite(cameraAspect) || cameraAspect <= 0 ||
            !Finite(cameraNearClip) || cameraNearClip <= 0)
            return Reject(ScenicRejectionKind.InvalidPlan, "Invalid scenic progress or camera format");
        ScenicVisibility visibility = suppliedVisibility ?? ownedVisibility;
        // Aspect or near-plane changes alter the actual validated image. Recheck
        // the proposed pose, retaining the same fixed focal length and geometry.
        aspect = cameraAspect; nearClip = cameraNearClip;
        ScenicPose wanted = solver.Solve(path, shot, progress, aspect, nearClip);
        bool safe = ScenicPoseValidation.Check(wanted, shot, visibility, aspect, nearClip,
            continuous && !changed, moveCarrier, out float score, out ScenicRejectionKind failure, out string reason);
        if (safe && hasPose)
        {
            // A director proposal is normally one bounded frame of travel. A
            // large discontinuity must be prepared as a new cut, not accelerated
            // through an unexamined slice of the path in one rendered frame.
            if (Vector3.Distance(accepted.Position, wanted.Position) > 1.5f ||
                Quaternion.Angle(accepted.Rotation, wanted.Rotation) > 6f || progress < accepted.Progress - .0001f)
            { safe = false; failure = ScenicRejectionKind.SamplingBudget; reason = "Scenic proposal exceeds one continuous movement step"; }
            else safe = ScenicPoseValidation.Corridor(accepted, wanted, visibility, aspect, nearClip, out failure, out reason);
        }
        if (safe)
        {
            accepted = wanted; hasPose = true; MovementAccepted = true; FailureKind = ScenicRejectionKind.None;
            LastFailure = "none";
        }
        else
        {
            FailureKind = failure; LastFailure = reason;
            rejected[(int)failure]++;
            // Retention is deliberately only within this exact plan/world. An
            // old image is never reported as the first presentation of a new shot.
            if (!hasPose || !ScenicPoseValidation.Check(accepted, shot, visibility, aspect, nearClip,
                continuous, moveCarrier, out _, out _, out _))
            { Diagnostics = "Scenic unavailable: " + failure + " / " + reason; return false; }
        }
        position = accepted.Position; rotation = accepted.Rotation; fov = accepted.FieldOfView;
        LastPictureUsable = true;
        pictureContext = new RenderedPictureContext(new RenderedPictureKey(pictureEpoch, shot.Id, 0),
            default, false, Time.frameCount, Time.unscaledTime);
        RenderedPictureFeedback pixels = PictureMonitor != null
            ? PictureMonitor.Feedback(pictureContext.Key, Time.unscaledTime) : RenderedPictureFeedback.Unavailable("pixel monitor unavailable");
        Diagnostics = "Directed scenery: " + shot.FeatureKind + " / " + shot.Role +
            "; plan " + shot.Id + "; progress " + accepted.Progress.ToString("P0") +
            "; duration " + shot.DurationSeconds.ToString("F1") + "s; fixed lens " + fov.ToString("F0") +
            "; " + (MovementAccepted ? "developing " + (shot.UseNativeSpline ? "native spline" : "authored route") :
                "retaining accepted pose: " + FailureKind + " / " + LastFailure) +
            "; quality " + score.ToString("F2") + "; pixel penalty " + pixels.Penalty.ToString("F2") +
            "; rejected steps " + TotalRejected();
        return true;
    }

    private int TotalRejected() { int sum = 0; foreach (int count in rejected) sum += count; return sum; }
    private bool Reject(ScenicRejectionKind kind, string reason)
    {
        FailureKind = kind; LastFailure = reason; LastPictureUsable = MovementAccepted = false;
        Diagnostics = "Scenic unavailable: " + kind + " / " + reason; return false;
    }
    internal void Reset()
    {
        plan = null; world = null; carrier = null; hasPose = false; accepted = default;
        LastPictureUsable = MovementAccepted = false; FailureKind = ScenicRejectionKind.None;
        pictureContext = default; pictureEpoch++; path.Reset(); solver.Reset(); ownedVisibility.Reset();
        Array.Clear(rejected, 0, rejected.Length);
    }
    public void Dispose() { Reset(); solver.Dispose(); path.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
