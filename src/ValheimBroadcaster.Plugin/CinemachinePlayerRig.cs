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
    private readonly bool[] eligible = new bool[Count];
    private readonly Candidate[] candidates = new Candidate[Count];
    private readonly List<PlayerShotVisibility.Subject> subjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> futureSubjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> primaryOnly = new List<PlayerShotVisibility.Subject>(1);
    private readonly List<string> visiblePlayers = new List<string>(4);
    private readonly Transform[] subjectPoints = new Transform[6];
    private GameObject? owner;
    private Transform? followPoint, groupFollowPoint, lookPoint;
    private CinemachineTargetGroup? group;
    private Player? actor;
    private ZNet? world;
    private ZDOID actorId;
    private string? subjectId;
    private long revision = -1;
    private Vector3 previousFocus, velocity, lastPosition, lastPoseFocus, lastLead, surveyedFocus, transitionFocus;
    private Quaternion lastRotation = Quaternion.identity;
    private CameraState transitionFrom;
    private float solvedAt = -1, lastPoseAt = -1, evaluateAt, lastFov, heading, space = 1, transitionElapsed = 2;
    private float lastCorrection = -10, predictionAt, fallbackSince = -1;
    private int solvedFrame = -1, previousChoice = -1, motionChoice = -1;
    private bool hasPose, interior, lastSucceeded, motionBlocked;
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
        internal readonly CinemachineGroupFraming Framing;
        internal readonly FoliageShotQuality Quality;
        internal bool GroupShot;
        internal Candidate(CinemachineCamera camera, CinemachineOrbitalFollow orbit,
            CinemachineRotationComposer aim, CinemachineGroupFraming framing, FoliageShotQuality quality)
        { Camera = camera; Orbit = orbit; Aim = aim; Framing = framing; Quality = quality; }
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
            previousFocus = focus; heading = settings.Azimuth.Value;
        }
        if (solvedFrame == Time.frameCount)
        { position = lastPosition; rotation = lastRotation; fov = lastFov; return lastSucceeded; }
        EnsureCreated();
        solvedFrame = Time.frameCount;
        float step = Mathf.Clamp(dt, 0, 0.1f);
        bool priorUsable = lastSucceeded;
        scene.RemoteContext = RemoteContext == null ? null : RemoteContext.GetPlayerContext;
        PlayerSceneContext.Frame context = scene.Update(target, settings.GroupFraming.Value);
        velocity = context.Velocity;
        if (!TargetRoster.Finite(velocity)) velocity = Vector3.zero;
        velocity = Vector3.ClampMagnitude(new Vector3(velocity.x, 0, velocity.z), 20f);
        previousFocus = focus; solvedAt = now;
        // Prediction is deliberately modest. Composition dead zones, not a moving
        // look-at point, let the actor travel naturally within the frame.
        Vector3 lead = Vector3.ClampMagnitude(velocity * 0.2f, 2f);
        followPoint!.position = focus + lead;
        groupFollowPoint!.position = focus + Vector3.ClampMagnitude(context.Focus - focus, interior ? 1.5f : 4f) + lead;
        lookPoint!.position = focus;
        UpdateGroup(context);
        visibility.Refresh(focus);
        if (settings.DynamicShots.Value && context.Activity == CinematicActivity.Travel && velocity.sqrMagnitude > 4f && space > 0.5f && now - lastCorrection > 5f && director.RecentCorrections == 0)
        {
            float trailing = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg + 155f;
            // Preserve screen direction during turns; do not snap behind facing.
            heading = Mathf.MoveTowardsAngle(heading, trailing, step * 5f);
        }
        int selected = settings.DynamicShots.Value ? director.SelectedIndex : stableSelection.SelectedIndex;
        if (selected != motionChoice) { motionChoice = selected; acceptedMotion = approvedMotion = NeutralMotion(); motionBlocked = false; }
        CinematicShotMotion requested = settings.DynamicShots.Value ? director.Motion(settings.ShotMotion.Value) : NeutralMotion();
        if (selected >= 0 && hasPose && settings.DynamicShots.Value)
            AdvanceMotion(selected, requested, context, focus, settings, now, step, continuous, moveCarrier);
        else acceptedMotion = approvedMotion = NeutralMotion();
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
        int choice = settings.DynamicShots.Value
            ? director.Select(now, step, scores, eligible, context.Activity, velocity.magnitude,
                interior, space, priorUsable)
            : stableSelection.Select(now, scores, eligible);
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
        // Framing failure is corrected locally. It is not a scene-loading or
        // teleport condition. This also protects the primary in large group shots.
        if (!endpoint.PrimaryInFrame)
        {
            desiredRotation = Quaternion.LookRotation(focus - desired, Vector3.up);
            endpoint = visibility.Evaluate(desired, desiredRotation, evaluatedSubjects, desiredFov, Aspect, NearClipPlane);
            NoteCorrection(now);
        }
        if (!endpoint.CameraClear || !PositionSupported(desired, focus, continuous, moveCarrier))
            return HoldValidPose(context, focus, now, continuous, moveCarrier,
                "Solved camera physical support changed before presentation", out position, out rotation, out fov);
        PlayerShotVisibility.Result actualView = endpoint;
        string transition = "tracking";
        position = desired; rotation = desiredRotation; fov = desiredFov;
        if (hasPose && choice != previousChoice)
        {
            transitionFrom = chosen;
            transitionFrom.RawPosition = lastPosition; transitionFrom.PositionCorrection = Vector3.zero;
            transitionFrom.RawOrientation = lastRotation; transitionFrom.OrientationCorrection = Quaternion.identity;
            transitionFrom.Lens.FieldOfView = lastFov;
            transitionFrom.ReferenceLookAt = lastPoseFocus;
            transitionFocus = focus; transitionElapsed = 0;
        }
        if (hasPose && transitionElapsed < 1.4f)
        {
            transitionElapsed += step;
            CameraState from = transitionFrom;
            Vector3 movement = focus - transitionFocus;
            from.RawPosition += movement; from.ReferenceLookAt += movement;
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
        lastPosition = position; lastPoseFocus = focus; lastPoseAt = now; lastLead = lead;
        lastRotation = rotation; lastFov = fov; hasPose = lastSucceeded = true; previousChoice = choice; fallbackSince = -1;
        LastFailure = "none";
        Diagnostics = $"Procedural camera: Cinemachine 3.1.7; shot {choice + 1}/{Count}; {context.Description}; " +
            $"{(settings.DynamicShots.Value ? director.Motion(settings.ShotMotion.Value).Family : "stable")}; " +
            $"age {director.ShotAge:F1}/{director.DesiredSeconds:F0}s; quality {lastView.Score:F2}; " +
            $"space {space:F2}; {(motionBlocked ? "motion held for clearance" : transition)}; " +
            $"aim damping {settings.AimDamping.Value:F1}s / dead zone {settings.DeadZone.Value:F2}; " +
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
            subjects.Add(new PlayerShotVisibility.Subject(s.Focus, s.Weight, s.Radius, s.IsPrimary));
        }
        for (int i = subjects.Count; i < subjectPoints.Length; i++) group!.Targets[i].Weight = 0;
        if (subjects.Count > 0) primaryOnly.Add(subjects[0]);
        group!.DoUpdate();
    }

    private void Evaluate(int index, CinematicShotMotion motion, PlayerSceneContext.Frame context, Vector3 focus,
        BroadcastSettings settings, float dt, bool continuous, bool moveCarrier)
    {
        Candidate candidate = candidates[index];
        // Four compact primary-only cameras remain available when a group cannot
        // be composed safely in the current room or vegetation.
        candidate.GroupShot = settings.GroupFraming.Value && subjects.Count > 1 && index < 6;
        candidate.Camera.Follow = candidate.GroupShot ? groupFollowPoint : followPoint;
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
        var composition = ScreenComposerSettings.Default;
        composition.DeadZone.Enabled = true;
        composition.DeadZone.Size = new Vector2(settings.DeadZone.Value, settings.DeadZone.Value * 0.75f);
        composition.HardLimits.Enabled = true;
        composition.HardLimits.Size = new Vector2(0.75f, 0.70f);
        // Small, slowly changing lead room. The subject need not be nailed to an
        // exact point in the image, including when standing or changing facing.
        float side = Vector3.Dot(velocity, candidate.Camera.State.GetFinalOrientation() * Vector3.right);
        float leadRoom = context.Activity == CinematicActivity.Travel ? -Mathf.Clamp(side * 0.02f, -0.1f, 0.1f) : 0;
        Vector2 previousScreen = candidate.Aim.Composition.ScreenPosition;
        composition.ScreenPosition = Vector2.Lerp(previousScreen, new Vector2(leadRoom, 0.025f),
            1f - Mathf.Exp(-Mathf.Max(0, dt) * 0.6f));
        candidate.Aim.Composition = composition;
        candidate.Framing.enabled = candidate.GroupShot;
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
        candidate.Quality.PreferredDistance = Mathf.Max(2f, settings.Distance.Value);
        ManualCameraUpdate.Update(candidate.Camera, Vector3.up, dt);
        CameraState state = candidate.Camera.State;
        Vector3 solved = state.GetFinalPosition();
        scores[index] = Mathf.Clamp01(state.ShotQuality + (candidate.GroupShot && candidate.Quality.Result.PrimaryVisibility > 0.65f ? 0.06f : 0));
        eligible[index] = candidate.Quality.Result.CameraClear && PositionSupported(solved, focus, continuous, moveCarrier);
    }

    private void AdvanceMotion(int index, CinematicShotMotion requested, PlayerSceneContext.Frame context, Vector3 focus,
        BroadcastSettings settings, float now, float step, bool continuous, bool moveCarrier)
    {
        // Recheck prediction at 5 Hz; each presented pose still has physical checks.
        if (now < predictionAt) { SmoothMotion(step); return; }
        predictionAt = now + 0.2f;
        var shotSubjects = candidates[index].GroupShot ? subjects : primaryOnly;
        CinematicShotMotion preview = director.Motion(settings.ShotMotion.Value, 0.8);
        Vector3 currentOffset = Offset(index, acceptedMotion, settings, interior);
        Vector3 wanted = lastPosition + focus - lastPoseFocus + Offset(index, requested, settings, interior) - currentOffset;
        Vector3 future = wanted + Vector3.ClampMagnitude(velocity * 0.8f, 3f) + Offset(index, preview, settings, interior) - Offset(index, requested, settings, interior);
        Quaternion wantedRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, wanted, focus);
        Quaternion futureRotation = PredictRotation(wanted, wantedRotation, focus, future, focus + Vector3.ClampMagnitude(velocity * 0.8f, 3f));
        var near = visibility.Evaluate(wanted, wantedRotation, shotSubjects, lastFov, Aspect, NearClipPlane);
        futureSubjects.Clear();
        for (int i = 0; i < shotSubjects.Count && i < context.Subjects.Count; i++)
        {
            PlayerSceneContext.Subject subject = context.Subjects[i];
            Vector3 predicted = subject.Focus + Vector3.ClampMagnitude(subject.Velocity * 0.8f, 3f);
            futureSubjects.Add(new PlayerShotVisibility.Subject(predicted, subject.Weight, subject.Radius, subject.IsPrimary));
        }
        var ahead = visibility.Evaluate(future, futureRotation, futureSubjects, lastFov, Aspect, NearClipPlane);
        bool safe = near.CameraClear && ahead.CameraClear && near.PrimaryInFrame &&
            near.PrimaryVisibility >= Mathf.Min(0.55f, Mathf.Max(0, lastView.PrimaryVisibility - 0.12f)) &&
            ahead.Score >= Mathf.Max(0.2f, lastView.Score - 0.15f) &&
            visibility.SegmentClear(lastPosition, wanted) && visibility.SegmentClear(wanted, future) &&
            PositionSupported(wanted, focus, continuous, moveCarrier) && PositionSupported(future, focus, continuous, moveCarrier);
        if (safe) { approvedMotion = requested; motionBlocked = false; }
        else
        {
            if (!motionBlocked) NoteCorrection(now);
            motionBlocked = true; approvedMotion = acceptedMotion;
        }
        SmoothMotion(step);
    }

    private void SmoothMotion(float step)
    {
        float t = 1f - Mathf.Exp(-step * 5f);
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
        Vector3 lead = recentPose ? lastLead : Vector3.zero;
        double best = ProceduralCameraGeometry.RequiredDisplacement(
            (double)playerPosition.x - carrier.x, (double)playerPosition.y - carrier.y,
            (double)playerPosition.z - carrier.z, settings.Distance.Value, settings.Height.Value,
            settings.Azimuth.Value, inside, lead.x, lead.z);
        if (recentPose)
        {
            Vector3 continued = lastPosition + focus - lastPoseFocus + Vector3.down * 2f;
            best = Math.Min(best, Vector3.Distance(carrier, continued));
        }
        return double.IsNaN(best) || double.IsInfinity(best) ? double.PositiveInfinity : best;
    }

    private Vector3 Offset(int index, CinematicShotMotion motion, BroadcastSettings settings, bool inside)
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
        followPoint = new GameObject("Tracking focus").transform; followPoint.SetParent(owner.transform, false);
        groupFollowPoint = new GameObject("Group tracking focus").transform; groupFollowPoint.SetParent(owner.transform, false);
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
            var camera = child.AddComponent<CinemachineCamera>();
            camera.Follow = followPoint; camera.LookAt = lookPoint;
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
            var framing = child.AddComponent<CinemachineGroupFraming>();
            framing.FramingSize = 0.65f;
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.ZoomOnly;
            framing.LateralAdjustment = CinemachineGroupFraming.LateralAdjustmentModes.ChangeRotation;
            framing.Damping = 1.5f; framing.enabled = false;
            var quality = child.AddComponent<FoliageShotQuality>(); quality.Visibility = visibility;
            candidates[i] = new Candidate(camera, orbit, aim, framing, quality);
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
                if (candidate != null && candidate.Camera) candidate.Camera.PreviousStateIsValid = false;
        actor = null; subjectId = null; world = null; revision = -1;
        velocity = lastLead = Vector3.zero; hasPose = lastSucceeded = false; solvedAt = lastPoseAt = -1; solvedFrame = -1;
        evaluateAt = predictionAt = 0; previousChoice = motionChoice = -1; transitionElapsed = 2; space = 1;
        motionBlocked = false; acceptedMotion = approvedMotion = NeutralMotion(); lastCorrection = -10; fallbackSince = -1;
        LastFailure = "Awaiting player camera"; Diagnostics = "Procedural camera: awaiting player";
    }

    public void Dispose()
    {
        Reset();
        if (owner) { owner.SetActive(false); UnityEngine.Object.Destroy(owner); }
        owner = null; followPoint = groupFollowPoint = lookPoint = null; group = null;
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
