using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Cinemachine owns virtual camera mechanics. Only CameraRig writes Valheim's
// rendered camera, after the actual pose and observer carrier are validated.
internal sealed class CinemachinePlayerRig : IDisposable
{
    private const int Count = ProceduralCameraGeometry.CandidateCount;
    private readonly PlayerShotVisibility visibility = new PlayerShotVisibility();
    private readonly CinematicShotDirector director = new CinematicShotDirector();
    private readonly ProceduralShotPolicy stableSelection = new ProceduralShotPolicy();
    private readonly PlayerSceneContext scene = new PlayerSceneContext();
    private readonly float[] scores = new float[Count];
    private readonly float[] editorialScores = new float[Count];
    private readonly bool[] seededEligibility = new bool[Count];
    private readonly bool[] eligible = new bool[Count];
    private readonly Candidate[] candidates = new Candidate[Count];
    private readonly List<PlayerShotVisibility.Subject> subjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> futureSubjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> primaryOnly = new List<PlayerShotVisibility.Subject>(1);
    private readonly List<string> visiblePlayers = new List<string>(4);
    private readonly Transform[] subjectPoints = new Transform[6];
    private GameObject? owner;
    private Transform? lookPoint;
    private CinemachineTargetGroup? group;
    private Player? actor;
    private ZNet? world;
    private ZDOID actorId;
    private string? subjectId;
    private long revision = -1;
    private Vector3 previousFocus, velocity, lastPosition, lastPoseFocus, surveyedFocus, transitionFocus, lastAimAnchor;
    private Vector3 lastPresentedAnchor, lastAnchorVelocity, transitionVelocity, renderedVelocity;
    private Quaternion lastRotation = Quaternion.identity;
    private CameraState transitionFrom;
    private float solvedAt = -1, lastPoseAt = -1, evaluateAt, lastFov, heading, space = 1, transitionElapsed = 2;
    private float lastCorrection = -10, predictionAt, fallbackSince = -1;
    private int solvedFrame = -1, previousChoice = -1, motionChoice = -1;
    private bool hasPose, interior, lastSucceeded, motionBlocked, lastDynamic;
    private long shotId;
    private float approvedHeading;
    private CinematicShotMotion acceptedMotion, approvedMotion;
    private PlayerShotVisibility.Result lastView;
    internal float NearClipPlane { get; set; } = 0.3f;
    internal float Aspect { get; set; } = 16f / 9f;
    internal Camera? RenderingCamera { get; set; }
    internal RemoteObserverClient? RemoteContext { get; set; }
    internal IReadOnlyList<string> VisiblePlayerIds => visiblePlayers;
    internal string LastFailure { get; private set; } = "Awaiting player camera";
    internal string Diagnostics { get; private set; } = "Procedural camera: awaiting player";

    private sealed class Candidate
    {
        internal readonly CinemachineCamera Camera;
        internal readonly CinemachineOrbitalFollow Orbit;
        internal readonly CinemachineRotationComposer Aim;
        internal readonly CinematicLensFraming Framing;
        internal readonly ShotCompositionPolicy Placement = new ShotCompositionPolicy();
        internal readonly ShotTrajectoryPolicy Trajectory = new ShotTrajectoryPolicy();
        internal readonly Transform TrackingPoint;
        internal ShotTrajectoryState TrajectoryState;
        internal Vector3 PreviousAnchor;
        internal int TrajectoryFrame = -1;
        internal float TrajectoryAt = -1;
        internal bool TrajectoryBlocked;
        internal string TrajectoryReason = "awaiting trajectory";
        internal ShotComposition Composition;
        internal PlayerSceneContext.Frame? Context;
        internal BroadcastSettings? Settings;
        internal float RelaxUntil, NextRecoveryAt;
        internal Vector2 RecoveryScreen, AppliedScreen;
        internal string RecoveryReason = "none";
        internal readonly FoliageShotQuality Quality;
        internal bool GroupShot;
        internal Candidate(CinemachineCamera camera, CinemachineOrbitalFollow orbit,
            CinemachineRotationComposer aim, CinematicLensFraming framing, FoliageShotQuality quality, Transform trackingPoint)
        { Camera = camera; Orbit = orbit; Aim = aim; Framing = framing; Quality = quality; TrackingPoint = trackingPoint; }
    }

    internal bool TrySolve(TargetRoster.Target target, Vector3 focus, BroadcastSettings settings,
        long selectionRevision, float dt, bool continuous, bool moveCarrier,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = lastPosition; rotation = lastRotation; fov = settings.FieldOfView.Value;
        Player? player = target.LoadedPlayer;
        if (!player || !TargetRoster.Finite(focus)) return Fail("Player camera target unavailable");
        float now = Time.unscaledTime;
        bool inside = Character.InInterior(focus);
        bool changed = actor != player || actorId != player.GetZDOID() || subjectId != target.Identity.Id ||
            world != ZNet.instance || revision != selectionRevision || interior != inside ||
            solvedAt < 0 || now < solvedAt || now - solvedAt > 1f || (focus - previousFocus).sqrMagnitude > 256f;
        if (changed)
        {
            Reset(); actor = player; actorId = player.GetZDOID(); subjectId = target.Identity.Id;
            world = ZNet.instance; revision = selectionRevision; interior = inside;
            previousFocus = focus; heading = approvedHeading = settings.Azimuth.Value;
        }
        if (solvedFrame == Time.frameCount)
        { position = lastPosition; rotation = lastRotation; fov = lastFov; return lastSucceeded; }
        EnsureCreated();
        solvedFrame = Time.frameCount;
        float step = Mathf.Clamp(dt, 0, 0.1f);
        bool priorUsable = lastSucceeded && lastView.PrimaryInFrame &&
            lastView.PrimaryVisibility >= 0.55f && lastView.PrimaryReadability >= 0.8f;
        bool resumingPresentation = hasPose && (!lastSucceeded || fallbackSince >= 0);
        bool modeChanged = hasPose && lastDynamic != settings.DynamicShots.Value;
        if (modeChanged)
        {
            director.Reset(); stableSelection.Reset(); predictionAt = now;
            if (!settings.DynamicShots.Value) { approvedMotion = acceptedMotion; approvedHeading = heading; }
        }
        lastDynamic = settings.DynamicShots.Value;
        scene.RemoteContext = RemoteContext == null ? null : RemoteContext.GetPlayerContext;
        PlayerSceneContext.Frame context = scene.Update(target, settings.GroupFraming.Value);
        velocity = context.Velocity;
        if (!TargetRoster.Finite(velocity)) velocity = Vector3.zero;
        velocity = Vector3.ClampMagnitude(new Vector3(velocity.x, 0, velocity.z), 20f);
        previousFocus = focus; solvedAt = now;
        // Aim follows the observed subject. Translation belongs to a persistent
        // world-space shot trajectory, not this frame's player displacement.
        lookPoint!.position = focus;
        UpdateGroup(context);
        visibility.Refresh(focus);
        int selected = modeChanged ? previousChoice : settings.DynamicShots.Value ? director.SelectedIndex : stableSelection.SelectedIndex;
        if (selected != motionChoice)
        {
            motionChoice = selected; acceptedMotion = approvedMotion = NeutralMotion();
            approvedHeading = heading; motionBlocked = false;
        }
        CinematicShotMotion requested = settings.DynamicShots.Value ? director.Motion(settings.ShotMotion.Value) : acceptedMotion;
        if (selected >= 0 && hasPose)
        {
            UpdateTrajectory(selected, context, focus, settings, now, dt);
            AdvanceMotion(selected, requested, context, focus, settings, now, step, continuous, moveCarrier);
        }
        // Steady freezes the accepted editorial offset. It does not teleport the
        // same virtual camera back to a neutral orbit when the mode changes.
        else if (selected < 0) acceptedMotion = approvedMotion = NeutralMotion();
        bool evaluateAll = selected < 0 || now >= evaluateAt || (focus - surveyedFocus).sqrMagnitude > 4f;
        if (evaluateAll)
        {
            evaluateAt = now + 0.2f; surveyedFocus = focus;
            for (int i = 0; i < Count; i++) Evaluate(i, i == selected ? acceptedMotion : NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
            float clear = 0;
            for (int i = 0; i < Count; i++) if (eligible[i] && candidates[i].Quality.Result.PrimaryVisibility >= 0.55f) clear++;
            // Space estimate is based on available readable viewpoints, not biome labels.
            space = Mathf.Lerp(space, clear / Count, interior ? 0.7f : 0.35f);
        }
        else Evaluate(selected, acceptedMotion, context, focus, settings, dt, continuous, moveCarrier);
        PrepareSelection(focus, context, priorUsable, modeChanged);
        var selectionEligibility = modeChanged && previousChoice >= 0 && eligible[previousChoice] ? seededEligibility : eligible;
        int choice = settings.DynamicShots.Value
            ? director.Select(now, step, editorialScores, selectionEligibility, context.Activity, velocity.magnitude,
                interior, space, priorUsable)
            : stableSelection.Select(now, editorialScores, selectionEligibility);
        if (choice < 0) return HoldValidPose(context, focus, now, continuous, moveCarrier,
            "No player camera with loaded geometry and safe physical clearance", out position, out rotation, out fov);
        if (choice != selected && !evaluateAll)
        {
            Evaluate(choice, NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
            if (!eligible[choice])
            {
                for (int i = 0; i < Count; i++) Evaluate(i, NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
                choice = settings.DynamicShots.Value
                    ? director.Select(now, 0, scores, eligible, context.Activity, velocity.magnitude, interior, space, false)
                    : stableSelection.Select(now, scores, eligible);
                if (choice < 0) return HoldValidPose(context, focus, now, continuous, moveCarrier,
                    "Alternate player camera geometry became unavailable", out position, out rotation, out fov);
            }
        }
        Candidate candidate = candidates[choice];
        CameraState chosen = candidate.Camera.State;
        var evaluatedSubjects = candidate.GroupShot ? subjects : primaryOnly;
        Vector3 desired = chosen.GetFinalPosition();
        Quaternion desiredRotation = Upright(chosen.GetFinalOrientation());
        float desiredFov = chosen.Lens.FieldOfView;
        PlayerShotVisibility.Result endpoint = candidate.Quality.Result;
        if (Quaternion.Angle(desiredRotation, chosen.GetFinalOrientation()) > 0.01f)
            endpoint = visibility.Evaluate(desired, desiredRotation, evaluatedSubjects, desiredFov, Aspect, NearClipPlane);
        // A framing correction is editorial, never a loading or travel request.
        // Recover only as far toward the primary as its full body requires.
        bool bodyClipped = endpoint.PrimaryReadability >= 0.5f &&
            (endpoint.PrimarySafeFrame < 0.85f || endpoint.PrimaryHeadroom < 0.025f);
        if (!endpoint.PrimaryInFrame || bodyClipped && now >= candidate.NextRecoveryAt)
            RecoverFraming(candidate, chosen, focus, desired, evaluatedSubjects, now,
                ref desiredRotation, desiredFov, ref endpoint);
        if (!endpoint.CameraClear || !PositionSupported(desired, focus, continuous, moveCarrier))
            return HoldValidPose(context, focus, now, continuous, moveCarrier,
                "Solved camera physical support changed before presentation", out position, out rotation, out fov);
        PlayerShotVisibility.Result actualView = endpoint;
        string transition = "tracking";
        position = desired; rotation = desiredRotation; fov = desiredFov;
        if (hasPose && (choice != previousChoice || modeChanged || resumingPresentation))
        {
            transitionFrom = chosen;
            transitionFrom.RawPosition = lastPosition; transitionFrom.PositionCorrection = Vector3.zero;
            transitionFrom.RawOrientation = lastRotation; transitionFrom.OrientationCorrection = Quaternion.identity;
            transitionFrom.Lens.FieldOfView = lastFov;
            transitionFrom.ReferenceLookAt = lastAimAnchor;
            transitionFocus = focus; transitionElapsed = 0;
            // Candidate solvers may have kept advancing while the displayed
            // picture was held. Resume from that actual stationary picture even
            // when the same candidate returns; never apply hidden motion at once.
            transitionVelocity = resumingPresentation || interior || space < 0.4f
                ? Vector3.zero : Vector3.ClampMagnitude(lastAnchorVelocity, 12f);
            if (choice != previousChoice)
            {
                shotId++;
                // A large change of viewpoint is an edit, not a fast orbit
                // through the subject. The endpoint already passed physical checks.
                if (ViewpointAngle(lastPosition - lastPoseFocus, desired - focus) > 80f)
                {
                    transitionElapsed = 2; transition = "validated continuity cut";
                    NoteCorrection(now);
                }
            }
        }
        if (hasPose && transitionElapsed < 1.4f)
        {
            transitionElapsed += step;
            CameraState from = transitionFrom;
            // Continue the outgoing camera's established world trajectory. Only
            // its aim reference follows the actor; body translation has no raw
            // target-delta feedthrough during the blend.
            from.RawPosition += transitionVelocity * Mathf.Min(1.4f, transitionElapsed);
            from.ReferenceLookAt += focus - transitionFocus;
            from.RawOrientation = PredictRotation(transitionFrom.RawPosition, transitionFrom.RawOrientation,
                transitionFrom.ReferenceLookAt, from.RawPosition, from.ReferenceLookAt);
            CameraState to = chosen;
            to.RawPosition = desired; to.PositionCorrection = Vector3.zero;
            to.RawOrientation = desiredRotation; to.OrientationCorrection = Quaternion.identity;
            // Reuse Cinemachine's composition-aware blend, preserving dead-zone
            // aim rather than recomputing a hard LookRotation every frame.
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(transitionElapsed / 1.4f));
            CameraState blended = CameraState.Lerp(from, to, t);
            Vector3 blendedPosition = blended.GetFinalPosition();
            Quaternion blendedRotation = Upright(blended.GetFinalOrientation());
            var blendView = visibility.Evaluate(blendedPosition, blendedRotation, evaluatedSubjects,
                blended.Lens.FieldOfView, Aspect, NearClipPlane);
            if (blendView.CameraClear && blendView.PrimaryInFrame && PositionSupported(blendedPosition, focus, continuous, moveCarrier) &&
                visibility.SegmentClear(lastPosition, blendedPosition))
            {
                position = blendedPosition; rotation = blendedRotation; fov = blended.Lens.FieldOfView;
                transition = "blending"; actualView = blendView;
            }
            else
            {
                transitionElapsed = 2;
                transition = "cut around blocked transition";
                NoteCorrection(now);
            }
        }
        // A tracking solver can also sweep into a wall between clear endpoints.
        // Cut to its validated solution instead of inventing travel permission.
        else if (hasPose && !visibility.SegmentClear(lastPosition, position))
        { transition = "collision correction"; NoteCorrection(now); }
        Vector3 direction = rotation * Vector3.forward;
        if (!Finite(rotation) || Vector3.ProjectOnPlane(direction, Vector3.up).sqrMagnitude < 0.0001f)
            return Fail("Player camera orientation unavailable");
        lastView = actualView;
        if (!lastView.CameraClear || !PositionSupported(position, focus, continuous, moveCarrier))
            return Fail("Presented camera geometry unavailable");
        visiblePlayers.Clear();
        for (int i = 0; i < context.Subjects.Count && i < evaluatedSubjects.Count; i++)
            if ((lastView.VisibleMask & (1 << i)) != 0 && context.Subjects[i].Player)
                visiblePlayers.Add(context.Subjects[i].Id);
        if (hasPose && now > lastPoseAt && now - lastPoseAt <= 0.25f)
        {
            Vector3 measured = Vector3.ClampMagnitude((position - lastPosition) / (now - lastPoseAt), 40f);
            renderedVelocity = Vector3.Lerp(renderedVelocity, measured, 1f - Mathf.Exp(-step * 3f));
        }
        else renderedVelocity = Vector3.zero;
        lastPosition = position; lastPoseFocus = focus; lastPoseAt = now;
        lastPresentedAnchor = candidate.TrackingPoint.position;
        lastAnchorVelocity = FromCore(candidate.TrajectoryState.Velocity);
        lastAimAnchor = chosen.ReferenceLookAt;
        lastRotation = rotation; lastFov = fov; hasPose = lastSucceeded = true; previousChoice = choice; fallbackSince = -1;
        LastFailure = "none";
        Diagnostics = $"Procedural camera: Cinemachine 3.1.7; shot {choice + 1}/{Count}; {context.Description}; " +
            $"{(settings.DynamicShots.Value ? director.Motion(settings.ShotMotion.Value).Family : "stable")}; " +
            $"age {director.ShotAge:F1}/{director.DesiredSeconds:F0}s; quality {lastView.Score:F2}; " +
            $"space {space:F2}; {(motionBlocked ? "motion held for clearance" : transition)}; " +
            $"trajectory {candidate.TrajectoryState.Mode}; anchor lag {candidate.TrajectoryState.Lag:F1}m / speed {lastAnchorVelocity.magnitude:F1}m/s; " +
            $"rendered camera {renderedVelocity.magnitude:F1}m/s / player {velocity.magnitude:F1}m/s; {candidate.TrajectoryReason}; " +
            $"aim damping {settings.AimDamping.Value:F1}s / dead zone {settings.DeadZone.Value:F2}; " +
            $"placement requested ({candidate.Composition.DesiredX:F2},{candidate.Composition.DesiredY:F2}), applied ({candidate.AppliedScreen.x:F2},{candidate.AppliedScreen.y:F2}), " +
            $"primary focus ({lastView.PrimaryFocusX - 0.5f:F2},{lastView.PrimaryFocusY - 0.5f:F2}), body height {lastView.PrimaryFrameHeight:F2}, safe {lastView.PrimarySafeFrame:F2}; " +
            $"{candidate.Composition.Reason}; recovery {candidate.RecoveryReason}; " +
            $"{director.Reason}; {lastView.Reason}";
        return true;
    }

    private bool HoldValidPose(PlayerSceneContext.Frame context, Vector3 focus, float now, bool continuous,
        bool moveCarrier, string failure, out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = lastPosition; rotation = lastRotation; fov = lastFov;
        if (!hasPose || fallbackSince >= 0 && now - fallbackSince > 0.75f ||
            !PositionSupported(position, focus, continuous, moveCarrier)) return Fail(failure);
        var viewed = previousChoice >= 0 && candidates[previousChoice].GroupShot ? subjects : primaryOnly;
        PlayerShotVisibility.Result live = visibility.Evaluate(position, rotation, viewed, fov, Aspect, NearClipPlane);
        if (!live.CameraClear || !live.PrimaryInFrame || live.PrimaryVisibility < 0.45f) return Fail(failure);
        if (fallbackSince < 0) { fallbackSince = now; NoteCorrection(now); }
        // This is fresh live footage from a revalidated camera, not an old image
        // or an assertion that a rejected candidate has loaded successfully.
        lastView = live; lastPoseFocus = focus; lastPoseAt = now; lastSucceeded = true;
        lastAimAnchor = previousChoice >= 0 && candidates[previousChoice].GroupShot ? group!.transform.position : focus;
        renderedVelocity = Vector3.zero;
        visiblePlayers.Clear();
        for (int i = 0; i < context.Subjects.Count && i < viewed.Count; i++)
            if ((live.VisibleMask & (1 << i)) != 0 && context.Subjects[i].Player)
                visiblePlayers.Add(context.Subjects[i].Id);
        LastFailure = "none";
        Diagnostics = "Procedural camera: holding a validated live viewpoint while camera correction settles; " + live.Reason;
        return true;
    }

    private void UpdateGroup(PlayerSceneContext.Frame frame)
    {
        subjects.Clear(); primaryOnly.Clear();
        for (int i = 0; i < frame.Subjects.Count && i < subjectPoints.Length; i++)
        {
            PlayerSceneContext.Subject s = frame.Subjects[i];
            subjectPoints[i].position = s.Focus;
            group!.Targets[i].Weight = Mathf.Max(0.01f, s.Weight);
            group.Targets[i].Radius = Mathf.Clamp(s.Radius, 0.3f, 3f);
            subjects.Add(new PlayerShotVisibility.Subject(s.Focus, s.Weight, s.Radius, s.IsPrimary, s.Height, s.BottomOffset));
        }
        for (int i = subjects.Count; i < subjectPoints.Length; i++) group!.Targets[i].Weight = 0;
        if (subjects.Count > 0) primaryOnly.Add(subjects[0]);
        group!.DoUpdate();
    }

    private void UpdateTrajectory(int index, PlayerSceneContext.Frame context, Vector3 focus,
        BroadcastSettings settings, float now, float dt)
    {
        Candidate candidate = candidates[index];
        if (candidate.TrajectoryFrame == Time.frameCount) return;
        candidate.TrajectoryFrame = Time.frameCount;
        candidate.GroupShot = settings.GroupFraming.Value && subjects.Count > 1 && index < 6;
        Vector3 center = candidate.GroupShot
            ? focus + Vector3.ClampMagnitude(context.Focus - focus, interior ? 1.5f : 4f) : focus;
        Vector3 sceneVelocity = candidate.GroupShot ? context.GroupVelocity : velocity;
        sceneVelocity = Vector3.ClampMagnitude(new Vector3(sceneVelocity.x, 0, sceneVelocity.z), 20f);
        float elapsed = candidate.TrajectoryAt >= 0 ? Mathf.Clamp(now - candidate.TrajectoryAt, 0, 0.25f) : Mathf.Clamp(dt, 0, 0.1f);
        candidate.TrajectoryAt = now;
        float nominalDistance = Mathf.Max(3f, Offset(index, NeutralMotion(), settings, interior).magnitude);
        float lagBudget = Mathf.Clamp(nominalDistance * 0.55f, interior ? 1.5f : 3f, interior ? 3f : 10f);
        float framingPressure = 0;
        if (candidate.Camera.PreviousStateIsValid)
        {
            var view = candidate.Quality.Result;
            float range = Vector3.Distance(candidate.Camera.State.GetFinalPosition(), focus);
            framingPressure = Mathf.Max(1f - view.PrimaryReadability,
                Mathf.Max(Mathf.Clamp01((0.85f - view.PrimarySafeFrame) / 0.85f),
                    Mathf.InverseLerp(nominalDistance * 1.25f, nominalDistance * 1.8f, range)));
        }
        ShotTrajectoryKind kind = settings.DynamicShots.Value ? director.TrajectoryKind :
            context.Activity == CinematicActivity.Travel ? ShotTrajectoryKind.Tracking : ShotTrajectoryKind.ActivityArea;
        candidate.PreviousAnchor = candidate.TrackingPoint.position;
        candidate.TrajectoryState = candidate.Trajectory.Step(now, elapsed, ToCore(focus), ToCore(center),
            ToCore(sceneVelocity), context.Activity, kind, shotId, lagBudget, framingPressure, space,
            candidate.TrajectoryBlocked && now < predictionAt);
        candidate.TrackingPoint.position = FromCore(candidate.TrajectoryState.Anchor);
        if (candidate.TrajectoryState.WasReset) candidate.PreviousAnchor = candidate.TrackingPoint.position;
        candidate.TrajectoryReason = candidate.TrajectoryBlocked ? "anchor waiting for clear path" :
            framingPressure > 0.2f ? "bounded framing correction" : "independent world trajectory";
    }

    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);

    private void Evaluate(int index, CinematicShotMotion motion, PlayerSceneContext.Frame context, Vector3 focus,
        BroadcastSettings settings, float dt, bool continuous, bool moveCarrier)
    {
        Candidate candidate = candidates[index];
        // Four compact primary-only cameras remain available when a group cannot
        // be composed safely in the current room or vegetation.
        UpdateTrajectory(index, context, focus, settings, Time.unscaledTime, dt);
        candidate.Camera.Follow = candidate.TrackingPoint;
        candidate.Camera.LookAt = candidate.GroupShot ? group!.transform : lookPoint;
        Vector3 offset = Offset(index, motion, settings, interior);
        candidate.Orbit.Radius = offset.magnitude;
        candidate.Orbit.HorizontalAxis.Value = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        candidate.Orbit.VerticalAxis.Value = Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
        candidate.Orbit.RadialAxis.Value = 1;
        candidate.Orbit.TrackerSettings = new TrackerSettings
        {
            BindingMode = BindingMode.WorldSpace,
            PositionDamping = Vector3.one * settings.PositionDamping.Value,
            AngularDampingMode = AngularDampingMode.Quaternion
        };
        candidate.Aim.Damping = new Vector2(settings.AimDamping.Value, settings.AimDamping.Value * 1.2f);
        candidate.Context = context; candidate.Settings = settings;
        // Upstream group framing sizes the lens before RotationComposer aims.
        // The group extension must not run a second, centered aim after it.
        candidate.Framing.FrameGroup = candidate.GroupShot;
        candidate.Framing.FovRange = new Vector2(Mathf.Max(35f, settings.FieldOfView.Value - 8f), Mathf.Min(105f, settings.FieldOfView.Value + 12f));
        candidate.Framing.Damping = Mathf.Max(1.5f, settings.AimDamping.Value * 1.5f);
        if (RenderingCamera) candidate.Camera.Lens.PullInheritedPropertiesFromCamera(RenderingCamera);
        candidate.Camera.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        candidate.Camera.Lens.FieldOfView = settings.FieldOfView.Value;
        candidate.Camera.Lens.NearClipPlane = Mathf.Max(0.01f, NearClipPlane);
        candidate.Quality.Focus = focus;
        candidate.Quality.Subjects = candidate.GroupShot ? subjects : primaryOnly;
        candidate.Quality.Aspect = Aspect;
        candidate.Quality.NearClipPlane = NearClipPlane;
        ManualCameraUpdate.Update(candidate.Camera, Vector3.up, dt);
        CameraState state = candidate.Camera.State;
        Vector3 solved = state.GetFinalPosition();
        scores[index] = Mathf.Clamp01(state.ShotQuality + (candidate.GroupShot && candidate.Quality.Result.PrimaryVisibility > 0.65f ? 0.06f : 0));
        eligible[index] = candidate.Quality.Result.CameraClear && PositionSupported(solved, focus, continuous, moveCarrier);
    }

    private void ConfigureComposition(Candidate candidate, CameraState state, float dt)
    {
        PlayerSceneContext.Frame context = candidate.Context!;
        BroadcastSettings settings = candidate.Settings!;
        Vector3 camera = state.GetCorrectedPosition(), anchor = state.ReferenceLookAt;
        if (!TargetRoster.Finite(anchor) || (anchor - camera).sqrMagnitude < 0.01f) return;
        Quaternion centered = Upright(Quaternion.LookRotation(anchor - camera, Vector3.up));
        float fov = state.Lens.FieldOfView;
        var framed = candidate.GroupShot ? subjects : primaryOnly;
        var observation = new ShotCompositionObservation { Grouped = candidate.GroupShot };
        float slackX = settings.DeadZone.Value * 0.5f, slackY = settings.DeadZone.Value * 0.375f;
        foreach (PlayerShotVisibility.Subject subject in framed)
        {
            var envelope = visibility.ProjectSubject(camera, centered, subject, fov, Aspect, NearClipPlane);
            if (!envelope.InFront)
            {
                observation.LeftExtent = observation.RightExtent = observation.TopExtent = observation.BottomExtent = 1;
                break;
            }
            observation.LeftExtent = Mathf.Max(observation.LeftExtent, 0.5f - (float)envelope.MinX + slackX);
            observation.RightExtent = Mathf.Max(observation.RightExtent, (float)envelope.MaxX - 0.5f + slackX);
            observation.TopExtent = Mathf.Max(observation.TopExtent, (float)envelope.MaxY - 0.5f + slackY);
            observation.BottomExtent = Mathf.Max(observation.BottomExtent, 0.5f - (float)envelope.MinY + slackY);
        }
        Vector3 projectedMotion = ProjectPoint(anchor + velocity * 0.1f, camera, centered, fov);
        observation.MotionX = Mathf.Clamp(projectedMotion.x * 10, -3, 3);
        observation.MotionY = Mathf.Clamp(projectedMotion.y * 10, -3, 3);
        observation.FacingX = Vector3.Dot(context.FacingDirection, centered * Vector3.right);
        observation.FacingConfidence = context.FacingConfidence;
        if (context.HasInterest)
        {
            Vector3 interest = ProjectPoint(context.InterestFocus, camera, centered, fov);
            if (interest.z > NearClipPlane)
            {
                observation.InterestX = Mathf.Clamp(interest.x, -1, 1);
                observation.InterestY = Mathf.Clamp(interest.y, -1, 1);
                observation.InterestConfidence = context.InterestConfidence;
            }
        }
        Vector3 primary = ProjectPoint(context.PrimaryFocus, camera, centered, fov);
        observation.PrimaryOffsetX = Mathf.Clamp(primary.x, -2, 2);
        observation.PrimaryOffsetY = Mathf.Clamp(primary.y, -2, 2);
        bool usable = !hasPose || lastSucceeded && lastView.PrimaryInFrame &&
            lastView.PrimaryVisibility >= 0.55f && lastView.PrimaryReadability >= 0.8f;
        float now = Time.unscaledTime;
        candidate.Composition = candidate.Placement.Update(now, Mathf.Max(0, dt), shotId,
            context.Activity, observation, interior, space, director.RecentCorrections, usable,
            settings.DeliberateComposition.Value);
        Vector2 wanted = now < candidate.RelaxUntil ? candidate.RecoveryScreen :
            new Vector2(candidate.Composition.ScreenX, candidate.Composition.ScreenY);
        if (now >= candidate.RelaxUntil) candidate.RecoveryReason = "none";
        // RotationComposer deliberately does not damp changes of ScreenPosition.
        // Policy easing plus this short handover preserves a corrected live pose.
        candidate.AppliedScreen = candidate.Camera.PreviousStateIsValid
            ? Vector2.Lerp(candidate.AppliedScreen, wanted, 1f - Mathf.Exp(-Mathf.Max(0, dt) / 0.35f))
            : wanted;
        var composition = ScreenComposerSettings.Default;
        composition.DeadZone.Enabled = true;
        composition.DeadZone.Size = new Vector2(settings.DeadZone.Value, settings.DeadZone.Value * 0.75f);
        composition.HardLimits.Enabled = true;
        composition.HardLimits.Size = new Vector2(0.75f, 0.70f);
        // Core projection uses Y-up; Cinemachine's composition guides use Y-down.
        composition.ScreenPosition = new Vector2(candidate.AppliedScreen.x, -candidate.AppliedScreen.y);
        // Hard limits stay centered in the actual image, independently of the
        // desired placement. A thirds preference cannot push limits offscreen.
        composition.HardLimits.Offset = new Vector2(
            -2 * composition.ScreenPosition.x / (composition.HardLimits.Size.x - composition.DeadZone.Size.x),
            -2 * composition.ScreenPosition.y / (composition.HardLimits.Size.y - composition.DeadZone.Size.y));
        composition.Validate();
        candidate.Aim.Composition = composition;
    }

    private Vector3 ProjectPoint(Vector3 point, Vector3 camera, Quaternion rotation, float fov)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - camera);
        var projected = ShotProjection.Measure(local.x, local.y, local.z, 0, fov, Aspect, Mathf.Max(0.01f, NearClipPlane));
        return projected.InFront ? new Vector3((float)projected.X - 0.5f, (float)projected.Y - 0.5f, local.z)
            : new Vector3(0, 0, -1);
    }

    private void RecoverFraming(Candidate candidate, CameraState state, Vector3 focus, Vector3 position,
        IReadOnlyList<PlayerShotVisibility.Subject> framed, float now, ref Quaternion rotation,
        float fov, ref PlayerShotVisibility.Result result)
    {
        Quaternion centered = Upright(Quaternion.LookRotation(focus - position, Vector3.up));
        Quaternion original = rotation;
        PlayerShotVisibility.Result best = result;
        Quaternion bestRotation = original;
        for (int i = 1; i <= 4; i++)
        {
            Quaternion proposed = Upright(Quaternion.Slerp(original, centered, i * 0.25f));
            var view = visibility.Evaluate(position, proposed, framed, fov, Aspect, NearClipPlane);
            if (view.PrimaryVisibility > best.PrimaryVisibility || view.PrimarySafeFrame > best.PrimarySafeFrame)
            { best = view; bestRotation = proposed; }
            if (view.PrimaryInFrame && view.PrimarySafeFrame >= 0.9f && view.PrimaryHeadroom >= 0.035f)
            { best = view; bestRotation = proposed; break; }
        }
        rotation = bestRotation; result = best;
        Vector3 screen = ProjectPoint(state.ReferenceLookAt, position, rotation, fov);
        if (screen.z <= NearClipPlane) return;
        candidate.AppliedScreen = candidate.RecoveryScreen = new Vector2(screen.x, screen.y);
        candidate.RelaxUntil = now + 3f; candidate.NextRecoveryAt = now + 0.6f;
        candidate.RecoveryReason = Quaternion.Angle(rotation, centered) < 0.5f ? "primary-centered safety fallback" : "relaxed placement to preserve full body";
        var composition = candidate.Aim.Composition;
        composition.ScreenPosition = new Vector2(screen.x, -screen.y);
        candidate.Aim.Composition = composition;
        // ForceCameraPosition alone leaves RotationComposer's previous screen
        // offset intact. Re-run Aim only, at the same evaluated pose, to seed all
        // its tracking history. Body/obstacle damping remains untouched.
        state.RawPosition = position; state.PositionCorrection = Vector3.zero;
        state.RawOrientation = rotation; state.OrientationCorrection = Quaternion.identity;
        candidate.Aim.MutateCameraState(ref state, -1);
        rotation = Upright(state.RawOrientation);
        candidate.Aim.ForceCameraPosition(position, rotation);
        result = visibility.Evaluate(position, rotation, framed, fov, Aspect, NearClipPlane);
        NoteCorrection(now);
    }

    private void PrepareSelection(Vector3 focus, PlayerSceneContext.Frame context, bool usable, bool seed)
    {
        Array.Clear(seededEligibility, 0, Count);
        if (seed && previousChoice >= 0) seededEligibility[previousChoice] = eligible[previousChoice];
        bool preserveContinuity = usable && previousChoice >= 0 && eligible[previousChoice] && scores[previousChoice] >= 0.55f;
        Vector3 oldOffset = Vector3.ProjectOnPlane(lastPosition - lastPoseFocus, Vector3.up);
        Vector3 action = context.HasInterest ? context.InterestFocus - focus :
            velocity.sqrMagnitude >= 1 ? velocity : context.FacingConfidence >= 0.5f ? context.FacingDirection : Vector3.zero;
        Vector3 axisRight = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(action, Vector3.up).normalized);
        float oldSide = Vector3.Dot(oldOffset.normalized, axisRight);
        for (int i = 0; i < Count; i++)
        {
            float score = scores[i];
            if (preserveContinuity && i != previousChoice)
            {
                Vector3 offset = Vector3.ProjectOnPlane(candidates[i].Camera.State.GetFinalPosition() - focus, Vector3.up);
                float angle = ViewpointAngle(oldOffset, offset);
                score -= Mathf.InverseLerp(60, 150, angle) * 0.25f;
                // Preserve established travel/activity screen direction during
                // planned edits, while leaving every emergency camera eligible.
                float side = Vector3.Dot(offset.normalized, axisRight);
                if (oldSide * side < -0.04f) score -= 0.18f;
            }
            editorialScores[i] = Mathf.Clamp01(score);
        }
    }

    private static float ViewpointAngle(Vector3 a, Vector3 b) =>
        Vector3.Angle(Vector3.ProjectOnPlane(a, Vector3.up), Vector3.ProjectOnPlane(b, Vector3.up));

    private void AdvanceMotion(int index, CinematicShotMotion requested, PlayerSceneContext.Frame context, Vector3 focus,
        BroadcastSettings settings, float now, float step, bool continuous, bool moveCarrier)
    {
        // Trajectory and editorial-offset previews are independent. A blocked
        // decorative arc cannot freeze otherwise necessary, safe tracking.
        if (now < predictionAt) { SmoothMotion(step); return; }
        predictionAt = now + 0.2f;
        Candidate candidate = candidates[index];
        var shotSubjects = candidate.GroupShot ? subjects : primaryOnly;
        Vector3 anchor = candidate.TrackingPoint.position;
        Vector3 wanted = lastPosition + anchor - lastPresentedAnchor;
        Vector3 future = wanted + FromCore(candidate.Trajectory.Preview(0.75).Anchor) - anchor;
        Vector3 predictedFocus = focus + velocity * 0.75f;
        futureSubjects.Clear();
        for (int i = 0; i < shotSubjects.Count && i < context.Subjects.Count; i++)
        {
            PlayerSceneContext.Subject subject = context.Subjects[i];
            Vector3 predicted = subject.Focus + Vector3.ClampMagnitude(subject.Velocity, 20f) * 0.75f;
            futureSubjects.Add(new PlayerShotVisibility.Subject(predicted, subject.Weight, subject.Radius,
                subject.IsPrimary, subject.Height, subject.BottomOffset));
        }
        Quaternion wantedRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, wanted, focus);
        Quaternion futureRotation = PredictRotation(wanted, wantedRotation, focus, future, predictedFocus);
        // Necessary tracking uses the current supported path. Uncertain future
        // terrain or foliage can stop development, but cannot strand an otherwise
        // valid camera/carrier while the observed player keeps moving.
        var currentPath = visibility.Evaluate(wanted, wantedRotation, shotSubjects, lastFov, Aspect, NearClipPlane);
        if (!currentPath.CameraClear || !visibility.SegmentClear(lastPosition, wanted) ||
            !PositionSupported(wanted, focus, continuous, moveCarrier))
        {
            // Roll the plan back as well as its helper transform. Otherwise the
            // hidden planner could run far ahead and jump when clearance returns.
            Vector3 acceptedAnchor = index == previousChoice ? lastPresentedAnchor : candidate.PreviousAnchor;
            candidate.Trajectory.Synchronize(ToCore(acceptedAnchor), true);
            candidate.TrajectoryState = candidate.Trajectory.Preview(0);
            candidate.TrackingPoint.position = acceptedAnchor;
            candidate.TrajectoryReason = "holding world anchor for clearance";
            if (!candidate.TrajectoryBlocked) NoteCorrection(now);
            candidate.TrajectoryBlocked = true;
            approvedMotion = acceptedMotion; approvedHeading = heading;
            SmoothMotion(step);
            return;
        }
        candidate.TrajectoryBlocked = false;
        candidate.TrajectoryReason = "validated world trajectory";
        CinematicShotMotion preview = settings.DynamicShots.Value ? director.Motion(settings.ShotMotion.Value, 0.75) : requested;
        float wantedHeading = heading, futureHeading = heading;
        if (settings.DynamicShots.Value && settings.ShotMotion.Value > 0 && context.Activity == CinematicActivity.Travel &&
            velocity.sqrMagnitude > 4f && space > 0.5f && now - lastCorrection > 5f && director.RecentCorrections == 0)
        {
            float trailing = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg + 155f;
            wantedHeading = Mathf.MoveTowardsAngle(heading, trailing, 1f);
            futureHeading = Mathf.MoveTowardsAngle(wantedHeading, trailing, 3.75f);
        }
        Vector3 currentOffset = Offset(index, acceptedMotion, settings, interior);
        wanted += OffsetAtHeading(index, requested, settings, interior, wantedHeading) - currentOffset;
        future += OffsetAtHeading(index, preview, settings, interior, futureHeading) - currentOffset;
        wantedRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, wanted, focus);
        futureRotation = PredictRotation(wanted, wantedRotation, focus, future, predictedFocus);
        bool safe = TrajectoryClear(wanted, wantedRotation, future, futureRotation, shotSubjects, focus, continuous, moveCarrier);
        if (safe) { approvedMotion = requested; approvedHeading = wantedHeading; motionBlocked = false; }
        else
        {
            if (!motionBlocked) NoteCorrection(now);
            motionBlocked = true; approvedMotion = acceptedMotion; approvedHeading = heading;
        }
        SmoothMotion(step);
    }

    private bool TrajectoryClear(Vector3 wanted, Quaternion wantedRotation, Vector3 future, Quaternion futureRotation,
        IReadOnlyList<PlayerShotVisibility.Subject> framed, Vector3 focus, bool continuous, bool moveCarrier)
    {
        var near = visibility.Evaluate(wanted, wantedRotation, framed, lastFov, Aspect, NearClipPlane);
        var ahead = visibility.Evaluate(future, futureRotation, futureSubjects, lastFov, Aspect, NearClipPlane);
        return near.CameraClear && ahead.CameraClear && near.PrimaryInFrame &&
            near.PrimaryVisibility >= Mathf.Min(0.55f, Mathf.Max(0, lastView.PrimaryVisibility - 0.12f)) &&
            ahead.Score >= Mathf.Max(0.2f, lastView.Score - 0.15f) &&
            visibility.SegmentClear(lastPosition, wanted) && visibility.SegmentClear(wanted, future) &&
            PositionSupported(wanted, focus, continuous, moveCarrier) && PositionSupported(future, focus, continuous, moveCarrier);
    }

    private void SmoothMotion(float step)
    {
        float t = 1f - Mathf.Exp(-step * 5f);
        heading = Mathf.LerpAngle(heading, approvedHeading, t);
        acceptedMotion = new CinematicShotMotion(
            Mathf.Lerp(acceptedMotion.AzimuthOffset, approvedMotion.AzimuthOffset, t),
            Mathf.Lerp(acceptedMotion.RadiusScale, approvedMotion.RadiusScale, t),
            Mathf.Lerp(acceptedMotion.HeightOffset, approvedMotion.HeightOffset, t), approvedMotion.Family);
    }

    private void NoteCorrection(float now)
    {
        // A long continuous wall correction is one event, not hundreds of cuts.
        if (now - lastCorrection < 1f) return;
        director.NotifyCorrection(now); lastCorrection = now;
    }

    private static Quaternion PredictRotation(Vector3 from, Quaternion rotation, Vector3 oldFocus, Vector3 to, Vector3 newFocus)
    {
        Vector3 a = oldFocus - from, b = newFocus - to;
        return a.sqrMagnitude > 0.01f && b.sqrMagnitude > 0.01f
            ? Upright(Quaternion.FromToRotation(a, b) * rotation) : rotation;
    }

    private static bool PositionSupported(Vector3 position, Vector3 focus, bool continuous, bool moveCarrier)
    {
        if (!TargetRoster.Finite(position) || (position - focus).sqrMagnitude < 0.75f * 0.75f ||
            Vector3.ProjectOnPlane(position - focus, Vector3.up).sqrMagnitude < 0.25f ||
            !PlayerShotReadiness.PositionReady(position, continuous)) return false;
        if (!moveCarrier) return true;
        Vector3 carrier = position + Vector3.down * 2f;
        return Player.m_localPlayer && TargetRoster.Finite(carrier) &&
            Vector3.Distance(carrier, Player.m_localPlayer.transform.position) < 95f &&
            PlayerShotReadiness.PositionReady(carrier, continuous);
    }

    // Cinematic novelty, group spread and foliage never authorize teleporting.
    internal double RequiredCarrierMovement(TargetRoster.Target target, Vector3 playerPosition, BroadcastSettings settings)
    {
        if (!Player.m_localPlayer || !TargetRoster.Finite(playerPosition)) return double.PositiveInfinity;
        Vector3 carrier = Player.m_localPlayer.transform.position;
        if (!TargetRoster.Finite(carrier)) return double.PositiveInfinity;
        Vector3 focus = playerPosition + Vector3.up * 1.5f;
        bool inside = Character.InInterior(focus);
        Player? loaded = target.LoadedPlayer;
        float now = Time.unscaledTime;
        bool recentPose = hasPose && loaded && loaded == actor && loaded.GetZDOID() == actorId &&
            target.Identity.Id == subjectId && world == ZNet.instance && interior == inside &&
            now >= lastPoseAt && now - lastPoseAt <= 1f && (focus - lastPoseFocus).sqrMagnitude <= 256f;
        double best = ProceduralCameraGeometry.RequiredDisplacement(
            (double)playerPosition.x - carrier.x, (double)playerPosition.y - carrier.y,
            (double)playerPosition.z - carrier.z, settings.Distance.Value, settings.Height.Value,
            settings.Azimuth.Value, inside, 0, 0);
        if (recentPose)
        {
            Vector3 continued = lastPosition + focus - lastPoseFocus + Vector3.down * 2f;
            best = Math.Min(best, Vector3.Distance(carrier, continued));
        }
        return double.IsNaN(best) || double.IsInfinity(best) ? double.PositiveInfinity : best;
    }

    private Vector3 Offset(int index, CinematicShotMotion motion, BroadcastSettings settings, bool inside) =>
        OffsetAtHeading(index, motion, settings, inside, heading);

    private static Vector3 OffsetAtHeading(int index, CinematicShotMotion motion, BroadcastSettings settings, bool inside, float heading)
    {
        AutonomyPosition value = ProceduralCameraGeometry.Offset(index, settings.Distance.Value,
            settings.Height.Value, heading + motion.AzimuthOffset, inside);
        float radius = Mathf.Clamp(motion.RadiusScale, 0.8f, 1.15f);
        return new Vector3(value.X * radius, Mathf.Max(0.5f, value.Y + Mathf.Clamp(motion.HeightOffset, -1.5f, 2f)), value.Z * radius);
    }

    private static CinematicShotMotion NeutralMotion() => new CinematicShotMotion(0, 1, 0, "hold");

    private void EnsureCreated()
    {
        if (owner) return;
        owner = new GameObject("Broadcaster procedural cameras") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        lookPoint = new GameObject("Subject focus").transform; lookPoint.SetParent(owner.transform, false);
        var groupObject = new GameObject("Cooperative scene targets"); groupObject.transform.SetParent(owner.transform, false);
        group = groupObject.AddComponent<CinemachineTargetGroup>();
        group.PositionMode = CinemachineTargetGroup.PositionModes.GroupAverage;
        group.RotationMode = CinemachineTargetGroup.RotationModes.Manual;
        group.enabled = false; // Explicit DoUpdate keeps it synchronized with manual virtual-camera updates.
        for (int i = 0; i < subjectPoints.Length; i++)
        {
            subjectPoints[i] = new GameObject("Scene subject " + i).transform;
            subjectPoints[i].SetParent(owner.transform, false);
            group.Targets.Add(new CinemachineTargetGroup.Target { Object = subjectPoints[i], Weight = 0, Radius = 0.8f });
        }
        int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        for (int i = 0; i < Count; i++)
        {
            var child = new GameObject("Player shot " + (i + 1));
            child.transform.SetParent(owner.transform, false);
            var trackingPoint = new GameObject("Shot trajectory " + (i + 1)).transform;
            trackingPoint.SetParent(owner.transform, false);
            var camera = child.AddComponent<CinemachineCamera>();
            camera.Follow = trackingPoint; camera.LookAt = lookPoint;
            camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            camera.PreviousStateIsValid = false;
            var orbit = child.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.AxisCenter;
            orbit.HorizontalAxis.Range = new Vector2(-180, 180); orbit.HorizontalAxis.Wrap = true;
            orbit.VerticalAxis.Range = new Vector2(-80, 89); orbit.VerticalAxis.Wrap = false;
            orbit.HorizontalAxis.Recentering.Enabled = false; orbit.VerticalAxis.Recentering.Enabled = false;
            orbit.RadialAxis.Recentering.Enabled = false;
            var aim = child.AddComponent<CinemachineRotationComposer>();
            aim.CenterOnActivate = true;
            aim.Lookahead.Enabled = false;
            var obstacles = child.AddComponent<CinemachineDeoccluder>();
            obstacles.CollideAgainst = mask;
            obstacles.MinimumDistanceFromTarget = 1f;
            obstacles.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
            {
                Enabled = true, CameraRadius = 0.35f, MaximumEffort = 3,
                Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraDistance,
                MinimumOcclusionTime = 0.15f, SmoothingTime = 0.4f, Damping = 1.2f, DampingWhenOccluded = 0.15f
            };
            obstacles.ShotQualityEvaluation = new CinemachineDeoccluder.QualityEvaluation { Enabled = false };
            var framing = child.AddComponent<CinematicLensFraming>();
            framing.FramingSize = 0.65f;
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.ZoomOnly;
            framing.LateralAdjustment = CinemachineGroupFraming.LateralAdjustmentModes.ChangeRotation;
            framing.Damping = 1.5f;
            var quality = child.AddComponent<FoliageShotQuality>(); quality.Visibility = visibility;
            var candidate = new Candidate(camera, orbit, aim, framing, quality, trackingPoint);
            framing.BeforeAim = (state, delta) => ConfigureComposition(candidate, state, delta);
            candidates[i] = candidate;
        }
        owner.SetActive(true);
    }

    private bool Fail(string reason)
    {
        visiblePlayers.Clear(); lastSucceeded = false; LastFailure = reason;
        Diagnostics = "Procedural camera: " + reason;
        return false;
    }

    internal void Reset()
    {
        director.Reset(); stableSelection.Reset(); scene.Reset(); visibility.Reset();
        Array.Clear(scores, 0, Count); Array.Clear(eligible, 0, Count); visiblePlayers.Clear(); subjects.Clear(); primaryOnly.Clear(); futureSubjects.Clear();
        if (owner)
            foreach (Candidate candidate in candidates)
                if (candidate != null && candidate.Camera)
                {
                    candidate.Camera.PreviousStateIsValid = false; candidate.Placement.Reset(); candidate.Trajectory.Reset();
                    candidate.TrajectoryFrame = -1; candidate.TrajectoryAt = -1; candidate.TrajectoryBlocked = false;
                    candidate.TrajectoryReason = "awaiting trajectory";
                    candidate.RelaxUntil = candidate.NextRecoveryAt = 0; candidate.RecoveryReason = "none";
                    candidate.AppliedScreen = candidate.RecoveryScreen = Vector2.zero;
                }
        actor = null; subjectId = null; world = null; revision = -1;
        velocity = lastAnchorVelocity = renderedVelocity = transitionVelocity = Vector3.zero; hasPose = lastSucceeded = false; solvedAt = lastPoseAt = -1; solvedFrame = -1;
        evaluateAt = predictionAt = 0; previousChoice = motionChoice = -1; transitionElapsed = 2; space = 1;
        motionBlocked = false; acceptedMotion = approvedMotion = NeutralMotion(); lastCorrection = -10; fallbackSince = -1;
        shotId = 0; lastDynamic = false;
        LastFailure = "Awaiting player camera"; Diagnostics = "Procedural camera: awaiting player";
    }

    public void Dispose()
    {
        Reset();
        if (owner) { owner.SetActive(false); UnityEngine.Object.Destroy(owner); }
        owner = null; lookPoint = null; group = null;
        Array.Clear(candidates, 0, Count); Array.Clear(subjectPoints, 0, subjectPoints.Length);
    }

    private static Quaternion Upright(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        return Vector3.ProjectOnPlane(forward, Vector3.up).sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward, Vector3.up) : rotation;
    }

    private static bool Finite(Quaternion value) => TargetRoster.Finite(new Vector3(value.x, value.y, value.z)) &&
        !float.IsNaN(value.w) && !float.IsInfinity(value.w);
}

// Reuse Cinemachine's bounded group-size solver without its competing centered
// aim. Its normal Aim-stage rotation would otherwise overwrite RotationComposer.
// Running the upstream calculation after Body gives Aim the final lens; keeping
// only the lens also preserves the obstacle solver's physically validated pose.
internal sealed class CinematicLensFraming : CinemachineGroupFraming
{
    internal bool FrameGroup;
    internal Action<CameraState, float>? BeforeAim;
    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return;
        Vector3 position = state.RawPosition, positionCorrection = state.PositionCorrection;
        Quaternion rotation = state.RawOrientation, rotationCorrection = state.OrientationCorrection;
        LensSettings originalLens = state.Lens;
        // Call even for solo frames: upstream initializes its callback stage only
        // while PreviousStateIsValid is false. Otherwise a group joining an
        // already-running solo camera would retain its uninitialized Finalize stage.
        base.PostPipelineStageCallback(vcam, CinemachineCore.Stage.Aim, ref state, deltaTime);
        state.RawPosition = position; state.PositionCorrection = positionCorrection;
        state.RawOrientation = rotation; state.OrientationCorrection = rotationCorrection;
        if (!FrameGroup) state.Lens = originalLens;
        BeforeAim?.Invoke(state, deltaTime);
    }
}
