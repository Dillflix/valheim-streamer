using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

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
        // Independent first-picture admission owns one measured composition,
        // not a zero-length spline. It uses the same authored aim and the same
        // final pose validation as a developed route on every rendered frame.
        if (plan.IsPictureOnly)
        {
            ScenicShotSample picture = plan.Sample(0);
            Vector3 picturePosition = FromCore(picture.CameraPosition), pictureFocus = FromCore(picture.AimPosition);
            return new ScenicPose(picturePosition, pictureFocus,
                ScenicVisibility.Compose(picturePosition, pictureFocus, picture.ScreenX, picture.ScreenY,
                    plan.FieldOfView, aspect), plan.FieldOfView, 0);
        }
        ScenicShotSample sample = plan.IsPan ? plan.Sample(progress) : path.Sample(progress);
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
        visibility.QueryId = plan.Id; visibility.QueryStage = "pose " + pose.Progress.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        if (!TargetRoster.Finite(pose.Position) || !TargetRoster.Finite(pose.Focus) ||
            Vector3.ProjectOnPlane(pose.Focus - pose.Position, Vector3.up).sqrMagnitude < .25f)
            return Reject(ScenicRejectionKind.InvalidPlan, "Invalid scenic pose or near-vertical aim", out kind, out reason);
        // Admission must satisfy the geometry required on the very next live
        // frame as well as initial network-area acquisition. Do not admit with
        // a weaker predicate and then reject that same pose as a continuation.
        if (!PlayerShotReadiness.PositionReady(pose.Position, true) ||
            !plan.UsesJourneyView && !PlayerShotReadiness.PositionReady(pose.Focus, true))
            return Reject(ScenicRejectionKind.Geometry, "Scenic camera or feature geometry is not loaded", out kind, out reason);
        if (!continuous && (!IdleShots.Ready(pose.Position) || !plan.UsesJourneyView && !IdleShots.Ready(pose.Focus)))
            return Reject(ScenicRejectionKind.Acquisition, "Scenic camera or feature area is not acquired", out kind, out reason);
        if (!ScenicSurface.TryRequiredLift(pose, aspect, nearClip, out float lift, out string surfaceReason, plan.StableWaterClearance))
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
        if (plan.UsesJourneyView)
        {
            // Cruising and its stationary refuge describe the surrounding
            // environment. The synthetic aim encodes orientation, not a named
            // feature whose arbitrary world point must have a clear sightline.
            // Named arrivals keep the subject-framing contract below.
            ScenicVisibility.JourneyViewObservation observed = visibility.EvaluateJourney(pose.Position, pose.Rotation,
                pose.FieldOfView, aspect, nearClip);
            score = (float)observed.View.Score;
            if (observed.InsideVegetation) return Reject(ScenicRejectionKind.InsideVegetation, observed.View.Reason, out kind, out reason);
            if (!observed.CameraClear) return Reject(ScenicRejectionKind.CameraCollision, observed.View.Reason, out kind, out reason);
            if (!observed.SceneReady) return Reject(ScenicRejectionKind.Geometry, "Journey angular viewing neighbourhood is not loaded", out kind, out reason);
            if (!observed.SurveyComplete || observed.View.Status == JourneyViewStatus.Unknown)
                return Reject(ScenicRejectionKind.SurveyIncomplete, observed.View.Reason, out kind, out reason);
            if (!continuous && !IdleShots.Ready(observed.ReadinessPoint))
                return Reject(ScenicRejectionKind.Acquisition, "Journey viewing neighbourhood is not acquired", out kind, out reason);
            if (!observed.View.Readable) return Reject(ScenicRejectionKind.Foliage, observed.View.Reason, out kind, out reason);
            reason = observed.View.Reason; return true;
        }
        ScenicFeature? feature = plan.Feature;
        Bounds? subject = feature != null ? ScenicSubjectFrame.BoundsFor(feature) : null;
        ScenicVisibility.Result view = visibility.Evaluate(pose.Position, pose.Rotation, pose.Focus,
            pose.FieldOfView, aspect, nearClip, subject);
        score = view.Score;
        // Physical camera failures outrank an editorial framing failure. In
        // particular a camera inside retained vegetation must never receive
        // the short Focus/Foliage proof-renewal allowance.
        if (view.InsideVegetation) return Reject(ScenicRejectionKind.InsideVegetation, view.Reason, out kind, out reason);
        if (!view.CameraClear) return Reject(ScenicRejectionKind.CameraCollision, view.Reason, out kind, out reason);
        if (!view.SurveyComplete) return Reject(ScenicRejectionKind.SurveyIncomplete, view.Reason, out kind, out reason);
        if (feature != null && !ScenicSubjectFrame.Evaluate(feature, plan.Role, pose, aspect, nearClip, out _, out _))
            return Reject(ScenicRejectionKind.Focus, "Final scenic pose loses the measured subject envelope or deliberate scale", out kind, out reason);
        if (feature != null && !LocalScenicSiteSubjects.Validate(feature, pose, visibility, aspect, nearClip, out string subjectReason))
            return Reject(ScenicRejectionKind.Focus, subjectReason, out kind, out reason);
        if (feature != null && view.SurveyComplete && !ScenicSubjectFrame.MembersVisible(feature, plan.Role, pose, visibility, aspect, nearClip))
            return Reject(ScenicRejectionKind.Focus, "Final scenic pose does not show enough independently observed site members", out kind, out reason);
        if (!view.FocusVisible) return Reject(ScenicRejectionKind.Focus, "Scenic feature is outside the frame or hidden", out kind, out reason);
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
    private readonly ScenicRouteWindow window = new ScenicRouteWindow();
    private ScenicShotPlan? plan;
    private ScenicVisibility? visibility;
    private ScenicPose previous;
    private bool hasPrevious, previousReadable, moveCarrier, fitting, routeRejected;
    private double measuredTravel;
    private float fitLift, totalLift;
    private int fitPasses;
    private float aspect, nearClip, minimumScore, surveyWaitSeconds;
    private float surveyWaitStarted = float.NegativeInfinity;
    private int samples, passSamples;
    private long sceneEpoch;
    internal bool Complete { get; private set; }
    internal bool Accepted { get; private set; }
    internal ScenicShotPlan? EvaluatedPlan => plan;
    internal long SceneEpoch => sceneEpoch;
    internal float Score => hasPrevious ? minimumScore : 0;
    internal float ValidatedFraction { get; private set; }
    internal ScenicRouteWindow ReadableWindow => window;
    internal ScenicShotPlan ProposeReadableSection()
    {
        ScenicShotPlan section = plan!.IsPan ? plan.Slice(window.Start, window.End) : path.ProposeMeasuredSlice((float)window.Start, (float)window.End);
        return window.HasRejectedContinuation ? section.WithRejectedContinuation(window.ContinuationRejection) : section;
    }
    internal float SuggestedCanopyLift { get; private set; }
    internal ScenicPose? RejectedPose { get; private set; }
    internal ScenicPose? CanopyEscapePose { get; private set; }
    internal ScenicRejectionKind FailureKind { get; private set; }
    internal string FailureReason { get; private set; } = "none";
    internal string Reason { get; private set; } = "No scenic route prepared";

    internal void Begin(ScenicShotPlan source, ScenicVisibility survey, float cameraAspect, float cameraNearClip, bool moveObserver)
    {
        Reset(); plan = source; visibility = survey; aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver;
        sceneEpoch = CameraSpatialScene.Shared.Epoch;
        if (!source.IsDirected || !Finite(aspect) || aspect <= 0 || !Finite(nearClip) || nearClip <= 0 || !PreparePath(source))
        { Fail(ScenicRejectionKind.InvalidPlan, "Unable to prepare the directed native scenic route"); return; }
        // A minimum of 16 intervals limits unsampled curvature, aim and foliage
        // changes even on a compact route; refinement below follows actual pose.
        fitting = true; StartPass();
        minimumScore = 1; Reason = "Fitting scenic route above loaded terrain and water";
    }

    internal bool Step(int sampleBudget = 6)
    {
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return Complete;
        using (work) return StepBudgeted(sampleBudget);
    }

    private bool StepBudgeted(int sampleBudget)
    {
        if (sampleBudget < 1 || sampleBudget > 64) throw new ArgumentOutOfRangeException(nameof(sampleBudget));
        if (Complete || plan == null || visibility == null) return Complete;
        if (sceneEpoch != CameraSpatialScene.Shared.Epoch)
        { Fail(ScenicRejectionKind.Geometry, "Scenic scene changed during route validation; discard mixed spatial evidence"); return true; }
        for (int i = 0; i < sampleBudget && pending.Count > 0; i++)
        {
            if (!CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic)) return false;
            // A journey preserves its accepted seam and needs exactly one
            // fitting pass plus one full validation pass. Long sections should
            // not lose the second pass to samples spent on the first one.
            if (++samples > MaximumSamples * (plan.IsJourney ? 2 : 1) || ++passSamples > MaximumSamples)
            { Fail(ScenicRejectionKind.SamplingBudget, "Scenic route exceeds bounded survey effort"); return true; }
            float fraction = pending.Pop();
            ScenicPose pose = solver.Solve(path, plan, fraction, aspect, nearClip);
            if (!TargetRoster.Finite(pose.Position) || !TargetRoster.Finite(pose.Focus) ||
                !Finite(pose.Rotation.x) || !Finite(pose.Rotation.y) ||
                !Finite(pose.Rotation.z) || !Finite(pose.Rotation.w))
            { Fail(ScenicRejectionKind.InvalidPlan, "Native scenic solver returned a non-finite pose"); return true; }
            if (hasPrevious && (Vector3.Distance(previous.Position, pose.Position) > .75f ||
                plan.IsSpatial && path.SpatialTravelBetween(previous.Progress, fraction) > .75f ||
                !plan.UsesJourneyView && Vector3.Distance(previous.Focus, pose.Focus) > 1f ||
                Quaternion.Angle(previous.Rotation, pose.Rotation) > 3f))
            {
                if (fraction - previous.Progress < .0001f)
                { Fail(ScenicRejectionKind.SamplingBudget, "Scenic route has an unresolved pose discontinuity"); return true; }
                pending.Push(fraction); pending.Push((fraction + previous.Progress) * .5f); continue;
            }
            if (fitting)
            {
                if (!ScenicSurface.TryRequiredLift(pose, aspect, nearClip, out float lift, out string surfaceReason, plan.StableWaterClearance))
                { Fail(ScenicRejectionKind.Geometry, surfaceReason); return true; }
                fitLift = Mathf.Max(fitLift, lift);
                previous = pose; hasPrevious = true;
                if (pending.Count == 0)
                {
                    if (fitLift > .001f)
                    {
                        if (plan.IsJourney)
                        {
                            // Raising the whole route would move the already
                            // accepted predecessor endpoint. The journey planner
                            // must instead find a gradual, connected climb.
                            Fail(ScenicRejectionKind.SurfaceClearance, "Journey clearance requires moving the accepted seam; seek another connected elevation profile");
                            return true;
                        }
                        float adjustment = fitLift + .1f;
                        if (++fitPasses > 3 || totalLift + adjustment > 48f)
                        { Fail(ScenicRejectionKind.SurfaceClearance, "Scenic route cannot fit above the local surface within its elevation budget"); return true; }
                        totalLift += adjustment;
                        try { plan = plan.RaiseCamera(adjustment); }
                        catch (ArgumentException)
                        { Fail(ScenicRejectionKind.SurfaceClearance, "Terrain fitting would lose the bounded scenic aim or useful angular development"); return true; }
                        if (!PreparePath(plan))
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
            bool readable = ScenicPoseValidation.Check(pose, plan, visibility, aspect, nearClip, false, moveCarrier,
                out float score, out ScenicRejectionKind kind, out string reason);
            if (!readable)
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
                        samples--; passSamples--; pending.Push(fraction);
                        Reason = "Waiting for scenic foliage survey coverage at the same route sample";
                        return false;
                    }
                    reason += " after 2s of bounded survey retries";
                }
                // Rejection describes this measured pose, not every point on
                // the route. Continue looking for a connected usable section;
                // an occluded opening must not hide a clear middle or ending.
                RecordRejection(kind, reason);
                RejectedPose ??= pose;
                if ((kind == ScenicRejectionKind.Foliage || kind == ScenicRejectionKind.Focus || kind == ScenicRejectionKind.InsideVegetation) &&
                    visibility.TryCanopyLift(pose.Position, out float lift, plan.IsJourney && kind == ScenicRejectionKind.Foliage))
                {
                    if (lift > SuggestedCanopyLift) CanopyEscapePose = pose;
                    SuggestedCanopyLift = Mathf.Max(SuggestedCanopyLift, lift);
                }
            }
            if (Finite(surveyWaitStarted))
            {
                surveyWaitSeconds += Mathf.Max(0, Time.unscaledTime - surveyWaitStarted);
                surveyWaitStarted = float.NegativeInfinity;
                if (!routeRejected) Reason = "Screening native scenic route";
            }
            bool corridor = readable && previousReadable;
            if (hasPrevious)
            {
                measuredTravel += Vector3.Distance(previous.Position, pose.Position);
                if (corridor && !ScenicPoseValidation.Corridor(previous, pose, visibility, aspect, nearClip, out kind, out reason))
                { corridor = false; RecordRejection(kind, reason); }
            }
            bool knownRejection = kind == ScenicRejectionKind.Focus || kind == ScenicRejectionKind.Foliage ||
                kind == ScenicRejectionKind.CameraCollision || kind == ScenicRejectionKind.CorridorCollision || kind == ScenicRejectionKind.InsideVegetation ||
                kind == ScenicRejectionKind.SurfaceClearance;
            window.Observe(fraction, measuredTravel, readable, corridor, knownRejection ? reason : null);
            previous = pose; hasPrevious = true; previousReadable = readable;
            if (!routeRejected) ValidatedFraction = fraction;
            if (readable) minimumScore = Mathf.Min(minimumScore, score);
        }
        if (pending.Count == 0)
        {
            Complete = true; Accepted = !routeRejected;
            if (Accepted) Reason = "Entire native scenic route is supported and readable";
        }
        return Complete;
    }

    private bool PreparePath(ScenicShotPlan value)
    {
        if (value.IsPan) return true;
        if (!path.Prepare(value, 1)) return false;
        // Native preparation may attach an immutable ordinary-shot pacing map.
        // Preflight and the eventual on-air director must use that same plan.
        plan = path.PreparedPlan;
        return true;
    }

    private void Fail(ScenicRejectionKind kind, string reason)
    { Complete = true; Accepted = false; RecordRejection(kind, reason); pending.Clear(); }
    private void RecordRejection(ScenicRejectionKind kind, string reason)
    {
        routeRejected = true;
        if (JourneyProofRenewalPolicy.Stronger(FailureKind, kind) != kind) return;
        FailureKind = kind; FailureReason = Reason = reason;
    }
    private void StartPass()
    {
        pending.Clear(); hasPrevious = previousReadable = false; fitLift = 0; passSamples = 0;
        var boundaries = new List<float>(33);
        for (int i = 0; i <= 16; i++) boundaries.Add(i / 16f);
        // Uniform time samples alone can miss a short turn or an out-and-back
        // corridor whose endpoints are close. Include every measured knot in
        // the same native distance mapping used on air; adaptive subdivision
        // above also bounds traveled arc length rather than just its chord.
        if (plan!.IsSpatial)
            for (int i = 0; i < plan.WaypointCount; i++) boundaries.Add(path.WaypointProgress(i));
        boundaries.Sort();
        float last = float.NaN;
        for (int i = boundaries.Count - 1; i >= 0; i--)
            if (boundaries[i] != last) { pending.Push(boundaries[i]); last = boundaries[i]; }
    }
    internal void Reset()
    {
        pending.Clear(); path.Reset(); solver.Reset(); plan = null; visibility = null;
        Complete = Accepted = hasPrevious = false; minimumScore = ValidatedFraction = 0; samples = passSamples = 0;
        sceneEpoch = 0;
        fitting = false; fitLift = totalLift = 0; fitPasses = 0;
        routeRejected = previousReadable = false; measuredTravel = 0; SuggestedCanopyLift = 0; window.Reset();
        RejectedPose = CanopyEscapePose = null;
        surveyWaitSeconds = 0; surveyWaitStarted = float.NegativeInfinity;
        FailureKind = ScenicRejectionKind.None; FailureReason = "none"; Reason = "No scenic route prepared";
    }
    public void Dispose() { Reset(); solver.Dispose(); path.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
