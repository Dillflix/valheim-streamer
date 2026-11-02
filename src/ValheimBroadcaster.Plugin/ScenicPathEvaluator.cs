using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal enum ScenicRejectionKind
{
    None, InvalidPlan, Acquisition, Geometry, CarrierRange, CameraCollision,
    CorridorCollision, Focus, Foliage, SurveyIncomplete, SamplingBudget, CameraFormat, SurfaceClearance
}

internal readonly struct ScenicPose
{
    internal readonly Vector3 Position, Focus;
    internal readonly Quaternion Rotation;
    internal readonly float FieldOfView, Progress;
    internal ScenicPose(Vector3 position, Vector3 focus, Quaternion rotation, float fov, float progress)
    { Position = position; Focus = focus; Rotation = rotation; FieldOfView = fov; Progress = progress; }
}

/// <summary>One deterministic native Body solve. Both acquisition and playback
/// execute this exact pipeline. Aim is the authored world-space composition;
/// it has no tracking history, automatic lens modifier or obstacle displacement.
/// The virtual camera has no Unity Camera and never renders a second view.</summary>
internal sealed class ScenicPoseSolver : IDisposable
{
    private GameObject? owner;
    private CinemachineCamera? camera;
    private NativeScenicDolly? dolly;

    internal ScenicPose Solve(ScenicSplinePath path, ScenicShotPlan plan, float progress, float aspect, float nearClip)
    {
        ScenicShotSample sample = path.Sample(progress);
        Vector3 expected = FromCore(sample.CameraPosition), focus = FromCore(sample.AimPosition);
        if (!plan.UseNativeSpline)
        {
            dolly?.Reset();
            return new ScenicPose(expected, focus, ScenicVisibility.Compose(expected, focus, sample.ScreenX, sample.ScreenY,
                plan.FieldOfView, aspect), plan.FieldOfView, Mathf.Clamp01(progress));
        }
        EnsureCreated();
        camera!.transform.SetPositionAndRotation(expected, Quaternion.LookRotation(focus - expected, Vector3.up));
        camera.PreviousStateIsValid = false;
        camera.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        camera.Lens.FieldOfView = plan.FieldOfView;
        camera.Lens.NearClipPlane = nearClip;
        dolly!.Configure(path, progress, Vector3.zero);
        // Negative delta explicitly resets native history and permits multiple
        // bounded survey samples in one frame. There is no damped preview pose
        // which differs from the pose that will later be displayed.
        ManualCameraUpdate.Update(camera, Vector3.up, -1);
        Vector3 position = camera.State.GetFinalPosition();
        Quaternion rotation = ScenicVisibility.Compose(position, focus, sample.ScreenX, sample.ScreenY,
            plan.FieldOfView, aspect);
        return new ScenicPose(position, focus, rotation, plan.FieldOfView, Mathf.Clamp01(progress));
    }

    private void EnsureCreated()
    {
        if (owner) return;
        owner = new GameObject("Broadcaster directed scenic solver") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        // The spline container is a sibling of the moving virtual camera. Its
        // world-space curve must not inherit a native camera transform write.
        var cameraObject = new GameObject("Directed scenic virtual camera") { hideFlags = HideFlags.HideAndDontSave };
        cameraObject.transform.SetParent(owner.transform, false);
        camera = cameraObject.AddComponent<CinemachineCamera>();
        camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
        dolly = new NativeScenicDolly(camera, owner.transform);
        owner.SetActive(true);
    }

    internal void Reset() { dolly?.Reset(); if (camera) camera.PreviousStateIsValid = false; }
    public void Dispose()
    {
        dolly?.Dispose(); dolly = null; camera = null;
        if (owner) UnityEngine.Object.Destroy(owner);
        owner = null;
    }
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}

/// <summary>Shared validation of the actual native scenic output. Readiness,
/// collisions and aesthetic rejection remain separate diagnostic evidence.</summary>
internal static class ScenicPoseValidation
{
    internal static bool Check(ScenicPose pose, ScenicShotPlan plan, ScenicVisibility visibility,
        float aspect, float nearClip, bool continuous, bool moveCarrier,
        out float score, out ScenicRejectionKind kind, out string reason)
    {
        score = 0; kind = ScenicRejectionKind.None; reason = "Readable directed scenic view";
        if (!TargetRoster.Finite(pose.Position) || !TargetRoster.Finite(pose.Focus) ||
            Vector3.ProjectOnPlane(pose.Focus - pose.Position, Vector3.up).sqrMagnitude < .25f)
            return Reject(ScenicRejectionKind.InvalidPlan, "Invalid scenic pose or near-vertical aim", out kind, out reason);
        // Admission must satisfy the geometry required on the very next live
        // frame as well as initial network-area acquisition. Do not admit with
        // a weaker predicate and then reject that same pose as a continuation.
        if (!PlayerShotReadiness.PositionReady(pose.Position, true) ||
            !PlayerShotReadiness.PositionReady(pose.Focus, true))
            return Reject(ScenicRejectionKind.Geometry, "Scenic camera or feature geometry is not loaded", out kind, out reason);
        if (!continuous && (!IdleShots.Ready(pose.Position) || !IdleShots.Ready(pose.Focus)))
            return Reject(ScenicRejectionKind.Acquisition, "Scenic camera or feature area is not acquired", out kind, out reason);
        if (!ScenicSurface.TryRequiredLift(pose, aspect, nearClip, out float lift, out string surfaceReason))
            return Reject(ScenicRejectionKind.Geometry, surfaceReason, out kind, out reason);
        if (lift > .001f)
            return Reject(ScenicRejectionKind.SurfaceClearance, surfaceReason, out kind, out reason);
        if (moveCarrier)
        {
            Vector3 carrier = pose.Position + Vector3.down * 2f;
            if (!Player.m_localPlayer || Vector3.Distance(carrier, Player.m_localPlayer.transform.position) >= 91f)
                return Reject(ScenicRejectionKind.CarrierRange, "Scenic carrier route exceeds local reach", out kind, out reason);
            if (!PlayerShotReadiness.PositionReady(carrier, true))
                return Reject(ScenicRejectionKind.Geometry, "Scenic carrier geometry is not loaded", out kind, out reason);
            if (!continuous && !IdleShots.Ready(carrier))
                return Reject(ScenicRejectionKind.Acquisition, "Scenic carrier area is not acquired", out kind, out reason);
        }
        Bounds? subject = null;
        ScenicFeature? feature = plan.Feature;
        if (feature != null && (feature.Kind == ScenicFeatureKind.Rock || feature.Kind == ScenicFeatureKind.Structure))
            subject = new Bounds(new Vector3(feature.Center.X, feature.Center.Y, feature.Center.Z),
                new Vector3(feature.Radius * 2f, Mathf.Max(1f, feature.Height), feature.Radius * 2f));
        ScenicVisibility.Result view = visibility.Evaluate(pose.Position, pose.Rotation, pose.Focus,
            pose.FieldOfView, aspect, nearClip, subject);
        score = view.Score;
        if (!view.CameraClear) return Reject(ScenicRejectionKind.CameraCollision, view.Reason, out kind, out reason);
        if (!view.SurveyComplete) return Reject(ScenicRejectionKind.SurveyIncomplete, view.Reason, out kind, out reason);
        if (!view.FocusVisible) return Reject(ScenicRejectionKind.Focus, view.Reason, out kind, out reason);
        if (!PictureQuality.Scenic(view.CameraClear, view.FocusVisible, view.Score, view.ForegroundOcclusion, view.SurveyComplete).Readable)
            return Reject(ScenicRejectionKind.Foliage, view.Reason, out kind, out reason);
        return true;
    }

    internal static bool Corridor(ScenicPose from, ScenicPose to, ScenicVisibility visibility, float aspect,
        float nearClip, out ScenicRejectionKind kind, out string reason)
    {
        kind = ScenicRejectionKind.None; reason = "Validated scenic corridor";
        if (!visibility.SegmentClear(from.Position, to.Position))
            return Reject(ScenicRejectionKind.CorridorCollision, "Scenic body corridor crosses solid geometry", out kind, out reason);
        // Sweep the full near rectangle as well as the body. A clear centreline
        // or a few corner rays cannot certify the interior of the near plane.
        if (!visibility.NearPlaneCorridorClear(from.Position, from.Rotation, to.Position, to.Rotation,
            to.FieldOfView, aspect, nearClip))
            return Reject(ScenicRejectionKind.CorridorCollision, "Scenic near-plane corridor crosses solid geometry", out kind, out reason);
        return true;
    }

    private static bool Reject(ScenicRejectionKind failure, string text, out ScenicRejectionKind kind, out string reason)
    { kind = failure; reason = text; return false; }
}

/// <summary>Incremental screening of the complete native route. Ordered adaptive
/// subdivision bounds spatial and angular gaps and records a usable prefix for
/// a separately regenerated and revalidated shorter plan.</summary>
internal sealed class ScenicPathEvaluator : IDisposable
{
    private const int MaximumSamples = 512;
    private readonly ScenicPoseSolver solver = new ScenicPoseSolver();
    private readonly ScenicSplinePath path = new ScenicSplinePath();
    private readonly Stack<float> pending = new Stack<float>();
    private ScenicShotPlan? plan;
    private ScenicVisibility? visibility;
    private ScenicPose previous;
    private bool hasPrevious, moveCarrier, fitting;
    private float fitLift, totalLift;
    private int fitPasses;
    private float aspect, nearClip, minimumScore, surveyWaitSeconds;
    private float surveyWaitStarted = float.NegativeInfinity;
    private int samples;
    internal bool Complete { get; private set; }
    internal bool Accepted { get; private set; }
    internal ScenicShotPlan? EvaluatedPlan => plan;
    internal float Score => hasPrevious ? minimumScore : 0;
    internal float ValidatedFraction { get; private set; }
    internal ScenicRejectionKind FailureKind { get; private set; }
    internal string Reason { get; private set; } = "No scenic route prepared";

    internal void Begin(ScenicShotPlan source, ScenicVisibility survey, float cameraAspect, float cameraNearClip, bool moveObserver)
    {
        Reset(); plan = source; visibility = survey; aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver;
        if (!source.IsDirected || !Finite(aspect) || aspect <= 0 || !Finite(nearClip) || nearClip <= 0 || !path.Prepare(source, 1))
        { Fail(ScenicRejectionKind.InvalidPlan, "Unable to prepare the directed native scenic route"); return; }
        // A minimum of 16 intervals limits unsampled curvature, aim and foliage
        // changes even on a compact route; refinement below follows actual pose.
        fitting = true; StartPass();
        minimumScore = 1; Reason = "Fitting scenic route above loaded terrain and water";
    }

    internal bool Step(int sampleBudget = 6)
    {
        if (sampleBudget < 1 || sampleBudget > 64) throw new ArgumentOutOfRangeException(nameof(sampleBudget));
        if (Complete || plan == null || visibility == null) return Complete;
        for (int i = 0; i < sampleBudget && pending.Count > 0; i++)
        {
            if (++samples > MaximumSamples) { Fail(ScenicRejectionKind.SamplingBudget, "Scenic route exceeds bounded survey effort"); return true; }
            float fraction = pending.Pop();
            ScenicPose pose = solver.Solve(path, plan, fraction, aspect, nearClip);
            if (hasPrevious && (Vector3.Distance(previous.Position, pose.Position) > .75f ||
                Vector3.Distance(previous.Focus, pose.Focus) > 1f || Quaternion.Angle(previous.Rotation, pose.Rotation) > 3f))
            {
                if (fraction - previous.Progress < .0001f)
                { Fail(ScenicRejectionKind.SamplingBudget, "Scenic route has an unresolved pose discontinuity"); return true; }
                pending.Push(fraction); pending.Push((fraction + previous.Progress) * .5f); continue;
            }
            if (fitting)
            {
                if (!ScenicSurface.TryRequiredLift(pose, aspect, nearClip, out float lift, out string surfaceReason))
                { Fail(ScenicRejectionKind.Geometry, surfaceReason); return true; }
                fitLift = Mathf.Max(fitLift, lift);
                previous = pose; hasPrevious = true;
                if (pending.Count == 0)
                {
                    if (fitLift > .001f)
                    {
                        float adjustment = fitLift + .1f;
                        if (++fitPasses > 3 || totalLift + adjustment > 48f)
                        { Fail(ScenicRejectionKind.SurfaceClearance, "Scenic route cannot fit above the local surface within its elevation budget"); return true; }
                        totalLift += adjustment;
                        plan = plan.RaiseCamera(adjustment);
                        if (!path.Prepare(plan, 1))
                        { Fail(ScenicRejectionKind.InvalidPlan, "Cannot prepare terrain-fitted scenic route"); return true; }
                        solver.Reset();
                    }
                    else
                    {
                        fitting = false;
                        Reason = "Screening the terrain-fitted native scenic route";
                    }
                    StartPass();
                    // Immutable whole-route placement is finalized before the
                    // collision/readability pass. Playback never clamps Y or
                    // changes a route in reaction to the terrain underneath it.
                    return false;
                }
                continue;
            }
            if (!ScenicPoseValidation.Check(pose, plan, visibility, aspect, nearClip, false, moveCarrier,
                out float score, out ScenicRejectionKind kind, out string reason))
            {
                // A cache whose recenter is throttled is missing evidence, not
                // evidence that this route is obscured. Retry this exact sample
                // on a later frame; never skip it or advance validated progress.
                // Total waiting per route remains bounded even if several
                // positions need their own refreshed foreground survey.
                if (kind == ScenicRejectionKind.SurveyIncomplete)
                {
                    float now = Time.unscaledTime;
                    if (!Finite(surveyWaitStarted) || now < surveyWaitStarted) surveyWaitStarted = now;
                    if (surveyWaitSeconds + now - surveyWaitStarted < 2f)
                    {
                        samples--; pending.Push(fraction);
                        Reason = "Waiting for scenic foliage survey coverage at the same route sample";
                        return false;
                    }
                    reason += " after 2s of bounded survey retries";
                }
                Fail(kind, reason); return true;
            }
            if (Finite(surveyWaitStarted))
            {
                surveyWaitSeconds += Mathf.Max(0, Time.unscaledTime - surveyWaitStarted);
                surveyWaitStarted = float.NegativeInfinity;
                Reason = "Screening native scenic route";
            }
            if (hasPrevious && !ScenicPoseValidation.Corridor(previous, pose, visibility, aspect, nearClip, out kind, out reason))
            { Fail(kind, reason); return true; }
            previous = pose; hasPrevious = true; ValidatedFraction = fraction; minimumScore = Mathf.Min(minimumScore, score);
        }
        if (pending.Count == 0)
        { Complete = Accepted = true; Reason = "Entire native scenic route is supported and readable"; }
        return Complete;
    }

    private void Fail(ScenicRejectionKind kind, string reason)
    { Complete = true; Accepted = false; FailureKind = kind; Reason = reason; pending.Clear(); }
    private void StartPass()
    {
        pending.Clear(); hasPrevious = false; fitLift = 0;
        for (int i = 16; i >= 0; i--) pending.Push(i / 16f);
    }
    internal void Reset()
    {
        pending.Clear(); path.Reset(); solver.Reset(); plan = null; visibility = null;
        Complete = Accepted = hasPrevious = false; minimumScore = ValidatedFraction = 0; samples = 0;
        fitting = false; fitLift = totalLift = 0; fitPasses = 0;
        surveyWaitSeconds = 0; surveyWaitStarted = float.NegativeInfinity;
        FailureKind = ScenicRejectionKind.None; Reason = "No scenic route prepared";
    }
    public void Dispose() { Reset(); solver.Dispose(); path.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
