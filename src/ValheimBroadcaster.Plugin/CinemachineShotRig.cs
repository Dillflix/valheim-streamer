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
internal sealed class CinemachineShotRig : IDisposable
{
    private const int BaseCount = ProceduralCameraGeometry.CandidateCount;
    private const int Count = ProceduralCameraGeometry.TotalCandidateCount;
    private readonly PlayerShotVisibility visibility = new PlayerShotVisibility();
    private readonly CinematicShotDirector director = new CinematicShotDirector();
    private readonly ProceduralShotPolicy stableSelection = new ProceduralShotPolicy();
    private const int PassByIndex = 7;
    private readonly PassByShotPolicy passBy = new PassByShotPolicy();
    private readonly NativeEncounterFraming encounterFraming = new NativeEncounterFraming();
    private readonly ShotSequencePolicy sequence = new ShotSequencePolicy();
    private readonly ShotContinuityPolicy continuity = new ShotContinuityPolicy();
    private readonly ShotLongevityPolicy longevity = new ShotLongevityPolicy(Count);
    private readonly NativeShotBlend nativeBlend = new NativeShotBlend();
    private NativeRoomConfiner? roomConfiner;
    private NativeClearShotRanker? clearShot;
    private readonly ScenicSplinePath scenicPath = new ScenicSplinePath();
    private readonly Vector3[] forecastPosition = new Vector3[Count];
    private readonly Quaternion[] forecastRotation = new Quaternion[Count];
    private readonly float[] forecastFov = new float[Count], forecastTime = new float[Count];
    private readonly Vector3[] forecastVelocity = new Vector3[Count], forecastAim = new Vector3[Count], forecastAimVelocity = new Vector3[Count];
    private readonly int[] forecastCandidates = new int[3];
    private float forecastAt;
    private ShotSequenceCue sequenceCue;
    private ShotScale lastScale = ShotScale.Full;
    private long shownCueRevision = -1, pictureEpoch = 1, pictureRevision;
    private bool lastCuePresented;
    internal bool PresentationConfirmed { get; set; }
    internal bool EditorialHold { get; set; }
    internal RenderedPictureMonitor? PictureMonitor { get; set; }
    internal RenderedPictureContext PictureContext { get; private set; }
    private bool DynamicEnabled(BroadcastSettings settings) => settings.DynamicShots.Value && !EditorialHold;
    // An authored scenic path is already a deliberate shot. Actor pacing must
    // not insert tiny alternate-angle edits partway through that path.
    private bool DirectedActorSelection(BroadcastSettings settings) => !IsScenic && DynamicEnabled(settings);
    private readonly PlayerSceneContext scene = new PlayerSceneContext();
    private readonly CreatureSceneContext creatureScene = new CreatureSceneContext();
    private readonly CameraSceneFrame scenicScene = new CameraSceneFrame();
    private readonly ScenicVisibility ownedLandscapeVisibility = new ScenicVisibility("fallback-live");
    private ScenicVisibility LandscapeVisibility => input.ScenicVisibility ?? ownedLandscapeVisibility;
    private CinematicShotInput input;
    private ScenicShotSample scenicSample;
    private readonly ScenicMotionClock scenicClock = new ScenicMotionClock();
    private float scenicProgress, scenicStrength = 1, nativePathStrength = 1, scenicSampleAt = -1;
    private bool nativeScenicPath;
    private bool pendingHandoff;
    private bool IsScenic => input.Kind == CinematicShotKind.Scenic;
    private readonly float[] scores = new float[Count];
    private readonly float[] editorialScores = new float[Count];
    private readonly bool[] seededEligibility = new bool[Count];
    private readonly bool[] eligible = new bool[Count];
    private readonly bool[] readable = new bool[Count];
    private readonly float[] recoveryHeadings = new float[ProceduralCameraGeometry.RecoveryCandidateCount];
    private readonly Candidate[] candidates = new Candidate[Count];
    private readonly List<PlayerShotVisibility.Subject> subjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> futureSubjects = new List<PlayerShotVisibility.Subject>(6);
    private readonly List<PlayerShotVisibility.Subject> primaryOnly = new List<PlayerShotVisibility.Subject>(1);
    private readonly List<string> visiblePlayers = new List<string>(4);
    private readonly Transform[] subjectPoints = new Transform[6];
    private GameObject? owner;
    private Transform? lookPoint;
    private CinemachineTargetGroup? group;
    private Character? actor;
    private ZNet? world;
    private ZDOID actorId;
    private string? subjectId;
    private long revision = -1;
    private Vector3 previousFocus, velocity, lastPosition, lastPoseFocus, surveyedFocus, transitionFocus, lastAimAnchor;
    private Vector3 lastPresentedAnchor, lastAnchorVelocity, transitionVelocity, renderedVelocity;
    private Quaternion lastRotation = Quaternion.identity;
    private CameraState transitionFrom;
    private bool transitionFromObservational;
    private float solvedAt = -1, lastPoseAt = -1, evaluateAt, lastFov, heading, space = 1, transitionElapsed = 2;
    private float lastCorrection = -10, predictionAt, fallbackSince = -1;
    private float unreadableSince = -1, readableSince = -1, recoverySearchAt, transitionSeconds = 1.4f;
    private int recoveryEpoch;
    private bool recoverySearch, confined;
    private long playerSceneId;
    private CameraSceneFrame? playerContextFrame, activeContext;
    private float playerContextAt = -1;
    private CinematicShotIntent intent;
    private ShotRole lastRole;
    private int solvedFrame = -1, previousChoice = -1, motionChoice = -1;
    private bool hasPose, interior, lastSucceeded, motionBlocked, lastDynamic;
    private bool lastObservational, lastGroupDollySetting, lastFreeLookSetting;
    private long shotId;
    private float approvedHeading, creatureScale = 1;
    private CinematicShotMotion acceptedMotion, approvedMotion;
    private PlayerShotVisibility.Result lastView;
    internal float NearClipPlane { get; set; } = 0.3f;
    internal float Aspect { get; set; } = 16f / 9f;
    internal Camera? RenderingCamera { get; set; }
    internal float? InitialHeading { get; set; }
    internal int PictureNamespace { get; set; }
    internal long PresentedShotId => lastSucceeded ? pictureRevision : -1;
    internal RemoteObserverClient? RemoteContext { get; set; }
    internal IReadOnlyList<string> VisiblePlayerIds => visiblePlayers;
    internal bool LastPictureUsable { get; private set; }
    internal CameraSceneFrame? CurrentPlayerScene => input.Kind == CinematicShotKind.Player && lastSucceeded &&
        world == ZNet.instance && playerContextAt >= 0 && Time.unscaledTime >= playerContextAt &&
        Time.unscaledTime - playerContextAt <= .75f ? playerContextFrame : null;
    internal string? CurrentPlayerId => CurrentPlayerScene != null ? subjectId : null;
    internal float PlayerSceneObservedAt => playerContextAt;
    internal string PictureReason => !lastSucceeded ? LastFailure :
        fallbackSince >= 0 ? "Holding the previous viewpoint while camera correction settles" :
        IsScenic && previousChoice >= 0 && candidates[previousChoice].TrajectoryBlocked
            ? candidates[previousChoice].TrajectoryReason : lastView.Reason;
    internal string LastFailure { get; private set; } = "Awaiting player camera";
    internal string Diagnostics { get; private set; } = "Procedural camera: awaiting player";

    private sealed class Candidate
    {
        internal readonly CinemachineCamera Camera;
        internal readonly CinemachineOrbitalFollow Orbit;
        internal readonly CinemachineRotationComposer Aim;
        internal readonly CinemachineDeoccluder Obstacles;
        internal readonly CinematicLensFraming Framing;
        internal readonly NativeObservationalCamera Observation;
        internal readonly NativeFreeLookProfiles Elevation;
        internal readonly ShotCompositionPolicy Placement = new ShotCompositionPolicy();
        internal readonly ShotTrajectoryPolicy Trajectory = new ShotTrajectoryPolicy();
        internal readonly ActorSpatialPlanner Spatial = new ActorSpatialPlanner();
        internal Vector3 PresentedSpatialOffset;
        internal readonly Transform TrackingPoint, AimPoint;
        internal readonly List<PlayerShotVisibility.Subject> Subjects = new List<PlayerShotVisibility.Subject>(6);
        internal ShotScale Scale = ShotScale.Full;
        internal Vector3 AimFocus;
        internal ShotTrajectoryState TrajectoryState;
        internal Vector3 PreviousAnchor, AnchorVelocity;
        internal int TrajectoryFrame = -1;
        internal float TrajectoryAt = -1;
        internal bool TrajectoryBlocked;
        internal string TrajectoryReason = "awaiting trajectory";
        internal ShotComposition Composition;
        internal CameraSceneFrame? Context;
        internal BroadcastSettings? Settings;
        internal float RelaxUntil, NextRecoveryAt, RecoveryFov, RecoveryFovUntil;
        internal Vector2 RecoveryScreen, AppliedScreen, PresentedScreen;
        internal string RecoveryReason = "none";
        internal readonly FoliageShotQuality Quality;
        internal readonly NativeScenicDolly Dolly;
        internal bool GroupShot, InterestReadable = true;
        internal bool PassBy;
        internal bool Observational, ObservationNeedsSeed, OrbitNeedsSeed, ObservationComposition;
        internal bool Continuous, MoveCarrier;
        internal long ObservationToken;
        internal float ObservationFov, ObservationSettingFov;
        internal ShotScale ObservationScale;
        internal Vector3 BodySeedPosition;
        internal Quaternion BodySeedOrientation = Quaternion.identity;
        internal Candidate(CinemachineCamera camera, CinemachineOrbitalFollow orbit,
            CinemachineRotationComposer aim, CinematicLensFraming framing, FoliageShotQuality quality, Transform trackingPoint, Transform aimPoint,
            NativeScenicDolly dolly, CinemachineDeoccluder obstacles, NativeObservationalCamera observation, NativeFreeLookProfiles elevation)
        {
            Camera = camera; Orbit = orbit; Aim = aim; Framing = framing; Quality = quality;
            TrackingPoint = trackingPoint; AimPoint = aimPoint; Dolly = dolly;
            Obstacles = obstacles; Observation = observation; Elevation = elevation;
        }
    }

    internal bool TrySolve(CinematicShotInput shot, BroadcastSettings settings, float dt, bool continuous, bool moveCarrier,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = lastPosition; rotation = lastRotation; fov = settings.FieldOfView.Value;
        Character? subject = shot.Actor;
        if (shot.Kind != CinematicShotKind.Scenic && (!subject || subject.IsDead()) ||
            shot.Kind == CinematicShotKind.Scenic && shot.ScenicPlan == null)
            return Fail("Camera subject unavailable");
        Vector3 focus = shot.Kind == CinematicShotKind.Scenic
            ? FromCore(shot.ScenicPlan!.Sample(shot.Progress, settings.ShotMotion.Value, DynamicEnabled(settings)).AimPosition)
            : shot.Kind == CinematicShotKind.Creature ? CreatureSceneContext.Focus(subject!)
            : subject!.transform.position + Vector3.up * 1.5f;
        if (!TargetRoster.Finite(focus)) return Fail("Camera focus unavailable");
        float now = Time.unscaledTime;
        bool inside = Character.InInterior(focus);
        scene.RemoteContext = RemoteContext == null ? null : RemoteContext.GetPlayerContext;
        CameraSceneFrame? observedPlayer = shot.Kind == CinematicShotKind.Player
            ? scene.Update(shot.PlayerTarget!, settings.GroupFraming.Value) : null;
        bool samePlayerScene = observedPlayer != null && playerSceneId != 0 && observedPlayer.SceneId == playerSceneId &&
            input.Kind == CinematicShotKind.Player && world == ZNet.instance && hasPose &&
            now >= lastPoseAt && now - lastPoseAt <= 0.75f;
        ZDOID id = subject ? subject.GetZDOID() : default;
        bool bindingChanged = input.Kind != shot.Kind || actor != subject || actorId != id ||
            subjectId != shot.StableId || revision != shot.Revision;
        bool discontinuity = world != ZNet.instance || solvedAt < 0 || now < solvedAt || now - solvedAt > 1f ||
            !bindingChanged && (interior != inside || shot.Kind != CinematicShotKind.Scenic && (focus - previousFocus).sqrMagnitude > 256f);
        if (bindingChanged && samePlayerScene && !discontinuity)
        {
            // A change of emphasis within an already observed group is not a new
            // scene. Retain world trajectories, shot memory and the displayed pose.
            actor = subject; actorId = id; subjectId = shot.StableId; revision = shot.Revision;
            evaluateAt = predictionAt = now; previousFocus = focus; pictureRevision++; PictureContext = default;
        }
        else if (bindingChanged || discontinuity)
        {
            bool handoff = bindingChanged && !discontinuity && hasPose && now - lastPoseAt <= 1f &&
                PlayerShotReadiness.PositionReady(lastPosition, continuous) &&
                (!moveCarrier || Player.m_localPlayer && Vector3.Distance(lastPosition + Vector3.down * 2f,
                    Player.m_localPlayer.transform.position) < 95f);
            Vector3 savedPosition = lastPosition, savedFocus = lastPoseFocus, savedAim = lastAimAnchor;
            Quaternion savedRotation = lastRotation;
            float savedFov = lastFov, savedAt = lastPoseAt;
            bool savedObservational = lastObservational;
            Reset(observedPlayer != null);
            if (handoff)
            {
                lastPosition = savedPosition; lastRotation = savedRotation; lastFov = savedFov;
                lastPoseFocus = savedFocus; lastAimAnchor = savedAim; lastPoseAt = savedAt;
                lastObservational = savedObservational;
                hasPose = true; pendingHandoff = true;
            }
            actor = subject; actorId = id; subjectId = shot.StableId;
            world = ZNet.instance; revision = shot.Revision; interior = inside;
            previousFocus = focus; heading = approvedHeading = InitialHeading ?? settings.Azimuth.Value;
            scenicClock.Reset(shot.Progress); scenicProgress = scenicClock.Progress;
            scenicStrength = Mathf.Max(0.01f, settings.ShotMotion.Value); scenicSampleAt = -1;
        }
        input = shot;
        if (solvedFrame == Time.frameCount)
        { position = lastPosition; rotation = lastRotation; fov = lastFov; return lastSucceeded; }
        EnsureCreated();
        solvedFrame = Time.frameCount;
        bool hadScenicSample = scenicSampleAt >= 0, priorNativeScenic = nativeScenicPath;
        if (IsScenic) focus = UpdateScenicSample(settings, dt, now);
        bool bodyBackendChanged = IsScenic && hadScenicSample && nativeScenicPath != priorNativeScenic;
        float step = Mathf.Clamp(dt, 0, 0.1f);
        bool priorUsable = lastSucceeded && PictureUsable(lastView);
        bool priorPresented = priorUsable && PresentationConfirmed;
        if (hasPose)
            continuity.Presented(now, ToCore(lastPosition - lastPoseFocus), lastRole, lastScale,
                priorPresented && transitionElapsed >= transitionSeconds &&
                (IsScenic || !settings.ShotScales.Value || ShotScalePolicy.MatchesScale(lastView.PrimaryFrameHeight, lastScale)));
        bool resumingPresentation = hasPose && (!lastSucceeded || fallbackSince >= 0 || pendingHandoff);
        bool nativeProfileChanged = hasPose && (lastGroupDollySetting != settings.NativeGroupDolly.Value ||
            lastFreeLookSetting != settings.NativeFreeLook.Value);
        bool modeChanged = hasPose && (lastDynamic != DynamicEnabled(settings) || bodyBackendChanged || nativeProfileChanged);
        if (modeChanged)
        {
            director.Reset(); stableSelection.Reset(); predictionAt = now;
            if (!DynamicEnabled(settings)) { approvedMotion = acceptedMotion; approvedHeading = heading; }
        }
        if (bodyBackendChanged || nativeProfileChanged)
        {
            // The comparison switch keeps the actually displayed source and
            // developed plan progress. Reset only solver/cache evidence, then
            // use the same validated transition as any other shot change.
            evaluateAt = forecastAt = 0; longevity.Reset(); pictureRevision++; PictureContext = default;
            foreach (Candidate item in candidates) item.Camera.PreviousStateIsValid = false;
        }
        lastDynamic = DynamicEnabled(settings);
        lastGroupDollySetting = settings.NativeGroupDolly.Value;
        lastFreeLookSetting = settings.NativeFreeLook.Value;
        CameraSceneFrame context = IsScenic ? ScenicContext(focus) : input.Kind == CinematicShotKind.Creature
            ? creatureScene.Update(subject!, settings.GroupFraming.Value)
            : observedPlayer!;
        if (observedPlayer != null)
        { playerSceneId = observedPlayer.SceneId; playerContextFrame = observedPlayer; playerContextAt = now; }
        activeContext = context;
        encounterFraming.Update(context, now, heading);
        if (settings.NativeConfiner.Value) roomConfiner!.Update(context, now);
        else roomConfiner!.Clear();
        confined = interior || context.Environment == ShotEnvironment.Room || context.Environment == ShotEnvironment.Doorway || context.DoorwayTransition;
        ShotSituation observedSituation = Situation(context);
        sequenceCue = sequence.Update(now, step, observedSituation, priorPresented,
            DynamicEnabled(settings) && settings.ShotSequences.Value,
            lastCuePresented && shownCueRevision == sequence.Cue.Revision,
            input.Kind == CinematicShotKind.Player ? context.ActionPhase : (ActionPhaseCue?)null);
        Vector3 actionDirection = NativeEncounterFraming.Active(context) ? context.EncounterDirection : context.HasInterest ? context.InterestFocus - focus :
            context.Velocity.sqrMagnitude >= 1 ? context.Velocity : context.FacingDirection;
        float actionConfidence = NativeEncounterFraming.Active(context) ? context.EncounterConfidence : context.HasInterest ? context.InterestConfidence :
            context.Velocity.sqrMagnitude >= 1 ? context.Confidence : context.FacingConfidence;
        continuity.Update(now, ToCore(actionDirection), Mathf.Clamp01(actionConfidence), priorPresented,
            DynamicEnabled(settings));
        ShotRole? requestedRole = settings.ShotSequences.Value && DynamicEnabled(settings)
            ? continuity.NeedsReestablish ? ShotRole.Establish : sequenceCue.Role : null;
        intent = director.UpdateIntent(now, step, observedSituation, priorPresented, requestedRole);
        creatureScale = input.Kind == CinematicShotKind.Creature && context.Subjects.Count > 0
            ? Mathf.Clamp(Mathf.Max(context.Subjects[0].Height / 1.85f, context.Subjects[0].Radius / 0.6f), 0.15f, 2f) : 1f;
        velocity = context.Velocity;
        if (!TargetRoster.Finite(velocity)) velocity = Vector3.zero;
        velocity = Vector3.ClampMagnitude(velocity, 20f);
        previousFocus = focus; solvedAt = now;
        // Aim follows the observed subject. Translation belongs to a persistent
        // world-space shot trajectory, not this frame's player displacement.
        lookPoint!.position = context.HasInterest && intent.InterestWeight > 0
            ? focus + Vector3.ClampMagnitude(context.InterestFocus - focus, confined ? 2f : 6f) * intent.InterestWeight
            : focus;
        UpdateGroup(context);
        if (ConfigurePassBy(context, focus, settings, now))
        { modeChanged = true; pictureRevision++; PictureContext = default; }
        for (int i = 0; i < Count; i++)
        {
            ConfigureScale(candidates[i], i, context, settings);
            if (ConfigureObservation(i, context, focus, settings))
            {
                evaluateAt = forecastAt = 0; director.InvalidatePending(); stableSelection.InvalidatePending();
                if (i == previousChoice)
                { modeChanged = true; pictureRevision++; PictureContext = default; }
            }
        }
        if (!IsScenic) visibility.Refresh(focus);
        int selected = modeChanged ? previousChoice : DirectedActorSelection(settings) ? director.SelectedIndex : stableSelection.SelectedIndex;
        if (selected != motionChoice)
        {
            motionChoice = selected; acceptedMotion = approvedMotion = NeutralMotion();
            approvedHeading = heading; motionBlocked = false;
        }
        CinematicShotMotion requested = !IsScenic && DynamicEnabled(settings) ? director.Motion(MotionStrength(settings)) : acceptedMotion;
        if (selected >= 0 && hasPose)
        {
            UpdateTrajectory(selected, context, focus, settings, now, dt);
            if (IsScenic) AdvanceScenic(selected, focus, settings, now, continuous, moveCarrier);
            else if (!candidates[selected].Observational && !candidates[selected].PassBy)
                AdvanceMotion(selected, requested, context, focus, settings, now, step, continuous, moveCarrier);
        }
        // Steady freezes the accepted editorial offset. It does not teleport the
        // same virtual camera back to a neutral orbit when the mode changes.
        else if (selected < 0) acceptedMotion = approvedMotion = NeutralMotion();
        UpdateRecoverySearch(now, priorUsable, selected, focus);
        bool evaluateAll = selected < 0 || now >= evaluateAt || (focus - surveyedFocus).sqrMagnitude > 4f;
        if (evaluateAll)
        {
            evaluateAt = now + 0.2f; surveyedFocus = focus;
            for (int i = 0; i < Count; i++) Evaluate(i, i == selected ? acceptedMotion : NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
            float clear = 0;
            for (int i = 0; i < BaseCount; i++) if (eligible[i] && readable[i]) clear++;
            // Space estimate is based on available readable viewpoints, not biome labels.
            space = Mathf.Lerp(space, clear / BaseCount, confined ? 0.7f : 0.35f);
        }
        else Evaluate(selected, acceptedMotion, context, focus, settings, dt, continuous, moveCarrier);
        if (settings.PredictiveShots.Value)
        {
            longevity.ObserveMotion(now, ToCore(focus), ToCore(velocity), IsScenic);
            if (evaluateAll && now >= forecastAt)
                ForecastCandidates(context, focus, settings, now, continuous, moveCarrier);
        }
        else longevity.Reset();
        PrepareSelection(focus, context, priorUsable, modeChanged, settings, now);
        var selectionEligibility = modeChanged && previousChoice >= 0 && eligible[previousChoice] && readable[previousChoice]
            ? seededEligibility : eligible;
        ShotSituation situation = Situation(context);
        int choice = DirectedActorSelection(settings)
            ? director.Select(now, step, editorialScores, selectionEligibility, context.Activity, velocity.magnitude,
                confined, space, priorPresented, readable, situation, requestedRole, settings.NativeClearShot.Value ? clearShot : null, context.ActionPhase.ProtectMoment)
            : stableSelection.Select(now, editorialScores, selectionEligibility, readable, settings.NativeClearShot.Value ? clearShot : null);
        if (choice < 0) return HoldValidPose(context, focus, now, continuous, moveCarrier,
            input.Kind == CinematicShotKind.Creature ? "Creature has no currently readable supported camera" :
                "No shot camera with loaded geometry and safe physical clearance", out position, out rotation, out fov);
        if (choice != selected && !evaluateAll)
        {
            Evaluate(choice, NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
            if (!eligible[choice])
            {
                if (previousChoice >= 0 && choice != previousChoice)
                {
                    if (DirectedActorSelection(settings)) director.RejectSelection(previousChoice);
                    else stableSelection.RejectSelection(previousChoice);
                }
                for (int i = 0; i < Count; i++) Evaluate(i, NeutralMotion(), context, focus, settings, dt, continuous, moveCarrier);
                choice = DirectedActorSelection(settings)
                    ? director.Select(now, 0, scores, eligible, context.Activity, velocity.magnitude, confined, space, false, readable, situation, requestedRole,
                        settings.NativeClearShot.Value ? clearShot : null, context.ActionPhase.ProtectMoment)
                    : stableSelection.Select(now, scores, eligible, readable, settings.NativeClearShot.Value ? clearShot : null);
                if (choice < 0) return HoldValidPose(context, focus, now, continuous, moveCarrier,
                    input.Kind == CinematicShotKind.Creature ? "Creature alternate cameras are not currently readable and supported" :
                        "Alternate shot camera geometry became unavailable", out position, out rotation, out fov);
            }
        }
        Candidate candidate = candidates[choice];
        CameraState chosen = candidate.Camera.State;
        IReadOnlyList<PlayerShotVisibility.Subject> evaluatedSubjects = candidate.Subjects;
        Vector3 desired = chosen.GetFinalPosition();
        Quaternion desiredRotation = Upright(chosen.GetFinalOrientation());
        float desiredFov = chosen.Lens.FieldOfView;
        PlayerShotVisibility.Result endpoint = candidate.Quality.Result;
        if (Quaternion.Angle(desiredRotation, chosen.GetFinalOrientation()) > 0.01f)
            endpoint = EvaluateView(desired, desiredRotation, evaluatedSubjects, desiredFov, focus);
        // A framing correction is editorial, never a loading or travel request.
        // Recover only as far toward the primary as its full body requires.
        bool bodyClipped = !IsScenic && endpoint.PrimaryReadability >= 0.5f &&
            (endpoint.PrimarySafeFrame < 0.85f || endpoint.PrimaryHeadroom < 0.025f);
        if (!IsScenic && !candidate.Observational && (!endpoint.PrimaryInFrame || bodyClipped) && now >= candidate.NextRecoveryAt)
            RecoverFraming(candidate, chosen, focus, desired, evaluatedSubjects, now,
                ref desiredRotation, ref desiredFov, ref endpoint);
        if (!endpoint.CameraClear || !PositionSupported(desired, focus, continuous, moveCarrier))
            return HoldValidPose(context, focus, now, continuous, moveCarrier,
                "Solved camera physical support changed before presentation", out position, out rotation, out fov);
        readable[choice] = PictureUsable(endpoint);
        if (!ShotCandidateRanking.Admissible(SceneKind, true, readable[choice]))
            return HoldValidPose(context, focus, now, continuous, moveCarrier,
                "Creature camera is supported but its subject is not readable", out position, out rotation, out fov);
        PlayerShotVisibility.Result actualView = endpoint;
        string transition = "tracking";
        position = desired; rotation = desiredRotation; fov = desiredFov;
        bool retainedReadablePose = false, pictureCut = false;
        bool changedShot = hasPose && (choice != previousChoice || modeChanged || resumingPresentation ||
            !PictureUsable(endpoint) || candidate.Scale != lastScale || intent.Role != lastRole);
        if (changedShot)
        {
            // A saved source is usable only if the current actors still fit that
            // pose. Last frame's quality alone cannot bless a moving transition.
            Quaternion sourceRotation = lastRotation;
            var sourceSubjects = previousChoice >= 0 ? candidates[previousChoice].Subjects : evaluatedSubjects;
            var sourceView = EvaluateView(lastPosition, sourceRotation, sourceSubjects, lastFov, focus);
            if (!PictureUsable(sourceView) && !lastObservational)
            {
                sourceRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, lastPosition, focus);
                sourceView = EvaluateView(lastPosition, sourceRotation, sourceSubjects, lastFov, focus);
            }
            bool sourceReadable = (!bindingChanged || samePlayerScene) && PictureUsable(sourceView) &&
                PositionSupported(lastPosition, focus, continuous, moveCarrier);
            float screenDelta = new Vector2(endpoint.PrimaryFocusX - sourceView.PrimaryFocusX,
                endpoint.PrimaryFocusY - sourceView.PrimaryFocusY).magnitude;
            var decision = ShotTransitionPolicy.Decide(!bindingChanged || samePlayerScene,
                sourceReadable, PictureUsable(endpoint), SegmentClear(lastPosition, desired),
                ViewpointAngle(lastPosition - lastPoseFocus, desired - focus), Vector3.Distance(lastPosition, desired),
                screenDelta, Mathf.Abs(desiredFov - lastFov), lastRole, director.Intent.Role,
                context.DoorwayTransition ? ShotEnvironment.Doorway : context.Environment, context.Activity, resumingPresentation && !sourceReadable);
            transition = decision.Reason;
            if (decision.Mode == ShotTransitionMode.Hold && sourceReadable)
            {
                // Keep a freshly evaluated live view. Foliage alone must never
                // enter the loading/slate/travel path or credit a rejected camera.
                position = lastPosition; rotation = sourceRotation; fov = lastFov;
                actualView = sourceView; retainedReadablePose = true;
                transitionElapsed = 2; transitionSeconds = 0;
                if (previousChoice >= 0)
                {
                    if (choice != previousChoice)
                    {
                        if (DirectedActorSelection(settings))
                        {
                            if (!director.RejectSelection(previousChoice)) director.AdoptValidatedSelection(now, previousChoice);
                        }
                        else if (!stableSelection.RejectSelection(previousChoice))
                            stableSelection.AdoptValidatedSelection(now, previousChoice);
                    }
                    choice = previousChoice; candidate = candidates[choice]; evaluatedSubjects = candidate.Subjects;
                    candidate.TrackingPoint.position = lastPresentedAnchor;
                    candidate.AnchorVelocity = Vector3.zero;
                    if (!IsScenic && !candidate.Observational && !candidate.PassBy)
                    {
                        SynchronizeActorTrajectory(candidate, lastPresentedAnchor, candidate.PresentedSpatialOffset);
                    }
                    candidate.Camera.ForceCameraPosition(position, rotation);
                    candidate.TrajectoryReason = "retaining a readable live viewpoint";
                }
                // Keep the normal 5 Hz standby survey. Repeatedly rejecting an
                // obscured destination must not turn all cameras into 60 Hz work.
            }
            else
            {
                transitionFrom = chosen;
                transitionFrom.RawPosition = lastPosition; transitionFrom.PositionCorrection = Vector3.zero;
                transitionFrom.RawOrientation = lastRotation; transitionFrom.OrientationCorrection = Quaternion.identity;
                transitionFrom.Lens.FieldOfView = lastFov;
                transitionFrom.ReferenceLookAt = lastAimAnchor;
                transitionFromObservational = lastObservational;
                transitionFocus = focus; transitionElapsed = 0;
                transitionSeconds = (float)decision.Seconds;
                nativeBlend.Start(transitionFrom, transitionSeconds);
                transitionVelocity = resumingPresentation || confined || space < 0.4f
                    ? Vector3.zero : Vector3.ClampMagnitude(lastAnchorVelocity, 12f);
                if (choice != previousChoice) shotId++;
                if (decision.Mode == ShotTransitionMode.Cut)
                {
                    pictureCut = true; transitionElapsed = 2;
                    if (!sourceReadable || !PictureUsable(endpoint)) NoteCorrection(now);
                }
            }
        }
        if (!retainedReadablePose && hasPose && transitionSeconds > 0 && transitionElapsed < transitionSeconds)
        {
            transitionElapsed += step;
            CameraState from = transitionFrom;
            from.RawPosition += transitionVelocity * Mathf.Min(transitionSeconds, transitionElapsed);
            if (!transitionFromObservational)
            {
                from.ReferenceLookAt += focus - transitionFocus;
                from.RawOrientation = PredictRotation(transitionFrom.RawPosition, transitionFrom.RawOrientation,
                    transitionFrom.ReferenceLookAt, from.RawPosition, from.ReferenceLookAt);
            }
            CameraState to = chosen;
            to.RawPosition = desired; to.PositionCorrection = Vector3.zero;
            to.RawOrientation = desiredRotation; to.OrientationCorrection = Quaternion.identity;
            to.Lens.FieldOfView = desiredFov;
            // Reuse upstream screen-space aim interpolation when compatible
            // participants/aim points differ. The full resulting pose still
            // passes the same readability, support and swept-path checks below.
            if (!transitionFromObservational && !candidate.Observational &&
                (!bindingChanged || samePlayerScene) && from.HasLookAt() && to.HasLookAt())
            {
                from.BlendHint |= CameraState.BlendHints.ScreenSpaceAimWhenTargetsDiffer;
                to.BlendHint |= CameraState.BlendHints.ScreenSpaceAimWhenTargetsDiffer;
            }
            else if (transitionFromObservational || candidate.Observational)
            {
                // Native Lerp otherwise also compensates orientation and FOV
                // around LookAt, even without its screen-space blend hint.
                // A fixed-heading observation requires native rotation blending.
                from.BlendHint = (from.BlendHint & ~CameraState.BlendHints.ScreenSpaceAimWhenTargetsDiffer) |
                    CameraState.BlendHints.IgnoreLookAtTarget;
                to.BlendHint = (to.BlendHint & ~CameraState.BlendHints.ScreenSpaceAimWhenTargetsDiffer) |
                    CameraState.BlendHints.IgnoreLookAtTarget;
            }
            CameraState blended = nativeBlend.Sample(from, to, transitionElapsed, transitionSeconds);
            Vector3 blendedPosition = blended.GetFinalPosition();
            Quaternion blendedRotation = Upright(blended.GetFinalOrientation());
            var blendView = EvaluateView(blendedPosition, blendedRotation, evaluatedSubjects, blended.Lens.FieldOfView, focus);
            if (PictureUsable(blendView) && PositionSupported(blendedPosition, focus, continuous, moveCarrier) &&
                SegmentClear(lastPosition, blendedPosition) && (!candidate.GroupShot ||
                    GroupViewRoute(candidate, lastPosition, lastRotation, lastFov, blendedPosition,
                        blendedRotation, blended.Lens.FieldOfView)))
            {
                position = blendedPosition; rotation = blendedRotation; fov = blended.Lens.FieldOfView;
                transition = "validated " + transitionSeconds.ToString("F2") + "s blend"; actualView = blendView;
            }
            else
            {
                transitionElapsed = 2;
                transition = "cut around unreadable or blocked transition";
                pictureCut = true; NoteCorrection(now);
            }
        }
        // A tracking solver can also sweep into a wall between clear endpoints.
        // Cut to its validated solution instead of inventing travel permission.
        else if (hasPose && !SegmentClear(lastPosition, position))
        { transition = "collision correction"; pictureCut = true; NoteCorrection(now); }
        if (hasPose && !changedShot && transitionElapsed >= transitionSeconds && PictureUsable(actualView) && step > 0)
        {
            // Bound ordinary tracking rotation without delaying a correction
            // needed to keep the subject readable or smoothing across an edit.
            Quaternion limited = Upright(Quaternion.RotateTowards(lastRotation, rotation,
                Mathf.Max(20f, intent.MaximumAngularSpeed) * step));
            if (Quaternion.Angle(limited, rotation) > .01f)
            {
                var limitedView = EvaluateView(position, limited, evaluatedSubjects, fov, focus);
                if (PictureUsable(limitedView)) { rotation = limited; actualView = limitedView; }
            }
        }
        if (hasPose && candidate.GroupShot && !pictureCut && !retainedReadablePose &&
            !GroupViewRoute(candidate, lastPosition, lastRotation, lastFov, position, rotation, fov))
        {
            // Aim recovery and angular limiting can change the presented route
            // after the body proposal. Readable endpoints remain valid cuts;
            // an interpolated move through blocked foliage does not.
            pictureCut = true; transitionElapsed = 2; NoteCorrection(now);
            transition = "cut around obstructed group movement";
        }
        Vector3 direction = rotation * Vector3.forward;
        if (!Finite(rotation) || Vector3.ProjectOnPlane(direction, Vector3.up).sqrMagnitude < 0.0001f)
            return Fail("Shot camera orientation unavailable");
        lastView = actualView;
        if (!lastView.CameraClear || !PositionSupported(position, focus, continuous, moveCarrier))
            return Fail("Presented camera geometry unavailable");
        if (!ShotCandidateRanking.Admissible(SceneKind, true, PictureUsable(lastView)))
            return Fail("Creature camera lost subject readability before presentation");
        visiblePlayers.Clear();
        for (int i = 0; PictureUsable(lastView) && i < context.Subjects.Count && i < evaluatedSubjects.Count; i++)
            if ((lastView.VisibleMask & (1 << i)) != 0 && context.Subjects[i].Player)
                visiblePlayers.Add(context.Subjects[i].Id);
        if (hasPose && now > lastPoseAt && now - lastPoseAt <= 0.25f)
        {
            Vector3 measured = Vector3.ClampMagnitude((position - lastPosition) / (now - lastPoseAt), 40f);
            renderedVelocity = Vector3.Lerp(renderedVelocity, measured, 1f - Mathf.Exp(-step * 3f));
        }
        else renderedVelocity = Vector3.zero;
        if (retainedReadablePose) lastAimAnchor += focus - lastPoseFocus;
        lastPosition = position; lastPoseFocus = focus; lastPoseAt = now;
        if (!retainedReadablePose)
        {
            lastPresentedAnchor = candidate.TrackingPoint.position;
            candidate.PresentedSpatialOffset = candidate.Spatial.Offset;
        }
        lastAnchorVelocity = retainedReadablePose ? Vector3.zero : candidate.AnchorVelocity;
        lastAimAnchor = retainedReadablePose ? lastAimAnchor : chosen.ReferenceLookAt;
        if (!retainedReadablePose && (pictureCut || lastScale != candidate.Scale || choice != previousChoice || lastRole != director.Intent.Role)) pictureRevision++;
        if (!retainedReadablePose) lastRole = director.Intent.Role;
        if (!retainedReadablePose) lastObservational = candidate.Observational;
        lastScale = candidate.Scale;
        shownCueRevision = sequenceCue.Revision;
        lastCuePresented = !retainedReadablePose && transitionElapsed >= transitionSeconds &&
            lastRole == ShotIntentPolicy.ResolveRole(Situation(context), sequenceCue.Role) &&
            lastScale == ResolvedCueScale(settings, context) &&
            (IsScenic || !settings.ShotScales.Value || ShotScalePolicy.MatchesScale(lastView.PrimaryFrameHeight, lastScale)) &&
            (!NativeEncounterFraming.Active(context) &&
                (!(lastRole == ShotRole.Interaction || lastRole == ShotRole.Detail) || !context.HasInterest) ||
                visibility.InterestVisible(position, rotation, NativeEncounterFraming.Interest(context), fov, Aspect, NearClipPlane));
        if (!retainedReadablePose && candidate.PassBy) passBy.Commit(now);
        lastRotation = rotation; lastFov = fov; hasPose = lastSucceeded = true; previousChoice = choice; fallbackSince = -1; pendingHandoff = false;
        // A safe live view remains usable footage when only its proposed motion is blocked.
        LastPictureUsable = PictureUsable(lastView);
        LastFailure = "none";
        UpdatePictureContext(choice, candidate, position, rotation, fov, now);
        Diagnostics = $"Procedural {input.Kind.ToString().ToLowerInvariant()} camera: Cinemachine 3.1.7; shot {choice + 1}/{Count}; {context.Description}; " +
            $"{(IsScenic ? input.ScenicPlan!.Style + " / " + scenicSample.Phase : DynamicEnabled(settings) ? director.Motion(MotionStrength(settings)).Family : "stable")}; " +
            (IsScenic ? $"path progress {scenicProgress:P0}; quality {lastView.Score:F2}; " :
                $"age {director.ShotAge:F1}/{director.DesiredSeconds:F0}s; quality {lastView.Score:F2}; ") +
            $"phase {context.ActionPhase.Phase}: {context.ActionPhase.Reason}; sequence {sequenceCue.Beat} / {sequence.BeatAge:F1}s; scale {candidate.Scale}; future {longevity.Evidence(choice, now).Reason}; " +
            $"space {space:F2}; {(motionBlocked ? "motion held for readability/clearance" : transition)}; role {director.Intent.Role}; environment {context.Environment}; locomotion {context.Locomotion}; " +
            $"readable {LastPictureUsable}; recovery search {(recoverySearch ? "expanded" : "normal")}; {lastView.Picture(IsScenic).Reason}; " +
            $"trajectory {(candidate.PassBy ? "world-fixed pass-by" : candidate.Observational ? "native screen/depth body" : IsScenic ? "authored world path" : candidate.TrajectoryState.Mode.ToString())}; anchor lag {(IsScenic || candidate.Observational || candidate.PassBy ? 0 : candidate.TrajectoryState.Lag):F1}m / speed {lastAnchorVelocity.magnitude:F1}m/s; " +
            $"rendered camera {renderedVelocity.magnitude:F1}m/s / subject {velocity.magnitude:F1}m/s; {candidate.TrajectoryReason}; " +
            (IsScenic || candidate.Observational || candidate.PassBy ? "" : candidate.Spatial.Diagnostics + "; ") +
            $"aim damping {settings.AimDamping.Value:F1}s / dead zone {settings.DeadZone.Value:F2}; " +
            $"placement requested ({candidate.Composition.DesiredX:F2},{candidate.Composition.DesiredY:F2}), applied ({candidate.PresentedScreen.x:F2},{candidate.PresentedScreen.y:F2}), " +
            (IsScenic ? $"environmental focus visible {lastView.PrimaryInFrame}; foreground {lastView.ForegroundOcclusion:F2}; survey complete {lastView.SurveyComplete}; " :
                $"primary focus ({lastView.PrimaryFocusX - 0.5f:F2},{lastView.PrimaryFocusY - 0.5f:F2}), body height {lastView.PrimaryFrameHeight:F2}, safe {lastView.PrimarySafeFrame:F2}; ") +
            $"{candidate.Composition.Reason}; recovery {candidate.RecoveryReason}; " +
            $"{sequenceCue.Reason}; {continuity.Reason}; {director.Reason}; {lastView.Reason}; " +
            (PictureMonitor == null ? "pixel feedback unavailable" : PictureMonitor.Feedback(PictureContext.Key, now).Reason) + "; " +
            (settings.NativeClearShot.Value ? clearShot!.Diagnostics : "ClearShot disabled; comparison selector") + "; " +
            (settings.NativeConfiner.Value ? roomConfiner!.Diagnostics : "Confiner disabled") + "; " +
            (nativeScenicPath ? $"Spline Dolly: {scenicPath.Length:F1}m path; distance {candidate.Dolly.Distance:F1}m" :
                IsScenic ? "Spline Dolly unavailable/disabled; scenic fallback" : candidate.PassBy ? passBy.Reason : candidate.Observational ? candidate.Observation.Diagnostics : "Orbital Follow actor body") +
            "; " + candidate.Framing.Diagnostics + "; " + candidate.Elevation.Description + "; native validated blend";
        return true;
    }

    private float MotionStrength(BroadcastSettings settings) => settings.ShotMotion.Value *
        (settings.ShotSequences.Value && DynamicEnabled(settings) ? sequenceCue.MotionMultiplier : 1);

    private bool PictureUsable(PlayerShotVisibility.Result view) => view.Picture(IsScenic).Readable;

    private ShotSceneKind SceneKind => IsScenic ? ShotSceneKind.Scenic : input.Kind == CinematicShotKind.Creature
        ? ShotSceneKind.Creature : ShotSceneKind.Player;

    private ShotSituation Situation(CameraSceneFrame context)
    {
        int companions = 0, opponents = 0;
        foreach (CameraSceneSubject member in context.Subjects)
            if (member.IsOpponent) opponents++; else if (!member.IsPrimary) companions++;
        return new ShotSituation(SceneKind, context.Activity, context.Confidence,
            companions, opponents, context.HasInterest, context.DoorwayTransition ? ShotEnvironment.Doorway : context.Environment, context.Locomotion, space,
            context.Encounter, context.EncounterConfidence, context.EncounterDistance);
    }

    private void UpdateRecoverySearch(float now, bool usable, int selected, Vector3 focus)
    {
        if (usable)
        {
            unreadableSince = -1;
            if (readableSince < 0) readableSince = now;
            if (now - readableSince >= 4f) recoverySearch = false;
            return;
        }
        readableSince = -1;
        if (unreadableSince < 0) unreadableSince = now;
        if (now - unreadableSince < .6f || now < recoverySearchAt) return;
        recoverySearch = true; recoverySearchAt = now + 2f; evaluateAt = 0;
        if (!IsScenic) visibility.RequestSurveyRefresh();
        float basis = hasPose ? Mathf.Atan2(lastPosition.x - focus.x, lastPosition.z - focus.z) * Mathf.Rad2Deg : heading;
        if (activeContext != null && activeContext.DoorwayTransition && activeContext.OpeningDirection.sqrMagnitude > .1f)
            basis = Mathf.Atan2(activeContext.OpeningDirection.x, activeContext.OpeningDirection.z) * Mathf.Rad2Deg;
        for (int i = 0; i < recoveryHeadings.Length; i++)
        {
            int index = BaseCount + i;
            if (index == selected) continue; // Never move a displayed recovery camera by changing its search phase.
            recoveryHeadings[i] = basis + (recoveryEpoch % 4) * 11.25f;
            if (candidates[index] != null) candidates[index].Camera.PreviousStateIsValid = false;
            longevity.Invalidate(index);
        }
        director.InvalidatePending(); stableSelection.InvalidatePending();
        recoveryEpoch++;
    }

    private PlayerShotVisibility.Result EvaluateView(Vector3 camera, Quaternion rotation,
        IReadOnlyList<PlayerShotVisibility.Subject> framed, float fov, Vector3 focus) => IsScenic
        ? FoliageShotQuality.FromLandscape(LandscapeVisibility.Evaluate(camera, rotation, focus, fov, Aspect, NearClipPlane))
        : visibility.Evaluate(camera, rotation, framed, fov, Aspect, NearClipPlane);

    private bool SegmentClear(Vector3 from, Vector3 to) =>
        (roomConfiner == null || roomConfiner.ValidateSegment(from, to)) &&
        (IsScenic ? LandscapeVisibility.SegmentClear(from, to) : visibility.SegmentClear(from, to));

    private Vector3 UpdateScenicSample(BroadcastSettings settings, float dt, float now)
    {
        if (scenicSampleAt < 0)
            scenicStrength = settings.ShotMotion.Value > 0 ? settings.ShotMotion.Value : 1f;
        if (!scenicPath.Ready) nativePathStrength = scenicStrength;
        bool wasNative = nativeScenicPath;
        nativeScenicPath = settings.NativeSplineDolly.Value && scenicPath.Prepare(input.ScenicPlan!, scenicStrength);
        bool movementBlocked = previousChoice >= 0 && candidates[previousChoice].TrajectoryBlocked;
        bool advance = scenicSampleAt >= 0 && wasNative == nativeScenicPath &&
            DynamicEnabled(settings) && settings.ShotMotion.Value > 0 && !movementBlocked;
        // Consume coverage progress even during a hold. Resuming must never
        // catch up to time spent waiting at a safe viewpoint.
        scenicProgress = scenicClock.Update(input.Progress, ScenicProgressSpeed(settings), advance);
        if (!nativeScenicPath && advance)
            scenicStrength = Mathf.Lerp(scenicStrength, settings.ShotMotion.Value, 1f - Mathf.Exp(-Mathf.Max(0, dt) * 2f));
        scenicSample = SampleScenic(scenicProgress);
        scenicSampleAt = now;
        return FromCore(scenicSample.AimPosition);
    }

    private float ScenicProgressSpeed(BroadcastSettings settings) => nativeScenicPath
        ? Mathf.Clamp(settings.ShotMotion.Value, 0, 1.5f) / Mathf.Max(.01f, nativePathStrength) : 1f;

    private ScenicShotSample SampleScenic(float progress) => nativeScenicPath && scenicPath.Ready
        ? scenicPath.Sample(progress) : input.ScenicPlan!.Sample(progress, scenicStrength);

    private CameraSceneFrame ScenicContext(Vector3 focus)
    {
        scenicScene.Subjects.Clear(); scenicScene.PrimaryFocus = scenicScene.Focus = focus;
        scenicScene.Velocity = scenicScene.GroupVelocity = scenicScene.FacingDirection = Vector3.zero;
        scenicScene.FacingConfidence = scenicScene.InterestConfidence = 0;
        scenicScene.HasInterest = false; scenicScene.Interior = interior;
        scenicScene.Environment = interior ? ShotEnvironment.Dungeon : ShotEnvironment.Open;
        scenicScene.Locomotion = ShotLocomotion.Stationary;
        scenicScene.Activity = CinematicActivity.Idle; scenicScene.Confidence = 1; scenicScene.Spread = 0;
        scenicScene.Description = "Environmental " + input.ScenicPlan!.Style.ToString().ToLowerInvariant() + " composition";
        return scenicScene;
    }

    private Vector3 ScenicCandidateOffset(int index)
    {
        if (index == 0) return Vector3.zero;
        if (index >= BaseCount)
        {
            AutonomyPosition offset = ProceduralCameraGeometry.RecoveryOffset(index - BaseCount, recoveryHeadings[index - BaseCount]);
            return new Vector3(offset.X, offset.Y, offset.Z);
        }
        var initial = input.ScenicPlan!.Sample(0);
        Vector3 outward = Vector3.ProjectOnPlane(FromCore(initial.CameraPosition) - FromCore(initial.AimPosition), Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, -outward);
        switch (index)
        {
            case 1: return right * 2f;
            case 2: return right * -2f;
            case 3: return Vector3.up * 2f;
            case 4: return right * 4f;
            case 5: return right * -4f;
            case 6: return right * 2f + Vector3.up * 3f;
            case 7: return right * -2f + Vector3.up * 3f;
            case 8: return Vector3.up * 5f;
            default: return outward * 2f + Vector3.up * 2f;
        }
    }

    private void UpdateScenicTrajectory(Candidate candidate, int index, float now, float dt)
    {
        candidate.GroupShot = false;
        float elapsed = candidate.TrajectoryAt >= 0 ? Mathf.Clamp(now - candidate.TrajectoryAt, 0, 0.25f) : Mathf.Clamp(dt, 0, 0.1f);
        bool first = candidate.TrajectoryAt < 0;
        candidate.TrajectoryAt = now; candidate.PreviousAnchor = candidate.TrackingPoint.position;
        if (!candidate.TrajectoryBlocked || now >= predictionAt)
            candidate.TrackingPoint.position = FromCore(scenicSample.CameraPosition) + ScenicCandidateOffset(index);
        candidate.AnchorVelocity = first || elapsed <= 0 ? Vector3.zero :
            Vector3.ClampMagnitude((candidate.TrackingPoint.position - candidate.PreviousAnchor) / elapsed, 12f);
        if (first) candidate.PreviousAnchor = candidate.TrackingPoint.position;
        candidate.TrajectoryReason = candidate.TrajectoryBlocked ? "environmental path held for clearance" :
            nativeScenicPath ? "native distance-based spline and independent aim" : "authored world camera and aim paths";
    }

    private void ConfigureScenicComposition(Candidate candidate, BroadcastSettings settings, float dt)
    {
        float x = settings.DeliberateComposition.Value ? scenicSample.ScreenX : 0;
        float y = settings.DeliberateComposition.Value ? scenicSample.ScreenY : 0;
        candidate.Composition = new ShotComposition(x, y, x, y, true,
            settings.DeliberateComposition.Value ? "Environmental landmark placement" : "Centered environmental comparison");
        ApplyComposition(candidate, settings, dt, Time.unscaledTime);
    }

    private void AdvanceScenic(int index, Vector3 focus, BroadcastSettings settings, float now, bool continuous, bool moveCarrier)
    {
        if (now < predictionAt) return;
        predictionAt = now + 0.2f;
        Candidate candidate = candidates[index];
        // Preview the exact native body path. Translating a previously obstacle-
        // displaced pose tests a different route from the actual Dolly solver.
        Vector3 wanted = nativeScenicPath ? FromCore(scenicSample.CameraPosition) + ScenicCandidateOffset(index) :
            lastPosition + candidate.TrackingPoint.position - lastPresentedAnchor;
        Quaternion wantedRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, wanted, focus);
        var view = EvaluateView(wanted, wantedRotation, primaryOnly, lastFov, focus);
        bool safe = view.CameraClear && SegmentClear(lastPosition, wanted) && PositionSupported(wanted, focus, continuous, moveCarrier) &&
            (!PictureUsable(lastView) || PictureUsable(view));
        if (!safe)
        {
            candidate.TrackingPoint.position = lastPresentedAnchor; candidate.AnchorVelocity = Vector3.zero;
            candidate.TrajectoryReason = "environmental movement held; seeking a supported composition";
            if (!candidate.TrajectoryBlocked) NoteCorrection(now);
            candidate.TrajectoryBlocked = true;
        }
        else
        {
            candidate.TrajectoryBlocked = false;
            candidate.TrajectoryReason = "validated environmental world path";
        }
    }

    private bool HoldValidPose(CameraSceneFrame context, Vector3 focus, float now, bool continuous,
        bool moveCarrier, string failure, out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = lastPosition; rotation = lastRotation; fov = lastFov;
        RollBackActorGuidance();
        if (!hasPose || fallbackSince >= 0 && now - fallbackSince > 0.75f ||
            !PositionSupported(position, focus, continuous, moveCarrier)) return Fail(failure);
        var viewed = previousChoice >= 0 ? candidates[previousChoice].Subjects : primaryOnly;
        PlayerShotVisibility.Result live = EvaluateView(position, rotation, viewed, fov, focus);
        if (!live.CameraClear || !live.PrimaryInFrame || live.PrimaryVisibility < 0.45f) return Fail(failure);
        if (!ShotCandidateRanking.Admissible(SceneKind, true, PictureUsable(live))) return Fail(failure);
        if (fallbackSince < 0) { fallbackSince = now; NoteCorrection(now); }
        // This is fresh live footage from a revalidated camera, not an old image
        // or an assertion that a rejected candidate has loaded successfully.
        lastAimAnchor += focus - lastPoseFocus;
        lastView = live; lastPoseFocus = focus; lastPoseAt = now; lastSucceeded = true;
        renderedVelocity = Vector3.zero;
        visiblePlayers.Clear();
        for (int i = 0; PictureUsable(live) && i < context.Subjects.Count && i < viewed.Count; i++)
            if ((live.VisibleMask & (1 << i)) != 0 && context.Subjects[i].Player)
                visiblePlayers.Add(context.Subjects[i].Id);
        LastFailure = "none"; LastPictureUsable = PictureUsable(live);
        lastCuePresented = false;
        if (previousChoice >= 0) UpdatePictureContext(previousChoice, candidates[previousChoice], position, rotation, fov, now);
        Diagnostics = "Procedural camera: holding a validated live viewpoint while camera correction settles; " + live.Reason;
        return true;
    }

    private void UpdateGroup(CameraSceneFrame frame)
    {
        subjects.Clear(); primaryOnly.Clear();
        for (int i = 0; i < frame.Subjects.Count && i < subjectPoints.Length; i++)
        {
            CameraSceneSubject s = frame.Subjects[i];
            subjectPoints[i].position = s.Focus;
            group!.Targets[i].Weight = Mathf.Max(0.01f, s.Weight * (s.IsPrimary ? 1f : intent.GroupWeightScale));
            group.Targets[i].Radius = Mathf.Clamp(s.IsOpponent ? Mathf.Max(s.Radius, s.Height * .5f) : s.Radius, 0.3f, 8f);
            subjects.Add(new PlayerShotVisibility.Subject(s.Focus, s.Weight, s.Radius, s.IsPrimary, s.Height, s.BottomOffset));
        }
        for (int i = subjects.Count; i < subjectPoints.Length; i++) group!.Targets[i].Weight = 0;
        if (subjects.Count > 0) primaryOnly.Add(subjects[0]);
        group!.DoUpdate();
    }

    private bool ConfigurePassBy(CameraSceneFrame context, Vector3 focus, BroadcastSettings settings, float now)
    {
        Candidate candidate = candidates[PassByIndex];
        bool was = candidate.PassBy;
        bool allowed = settings.NativePassBy.Value && DynamicEnabled(settings) && settings.ShotMotion.Value > 0 &&
            input.Kind == CinematicShotKind.Player && context.Activity == CinematicActivity.Travel &&
            context.Encounter == EncounterKind.None && !context.HasInterest && context.Confidence >= .6f &&
            (context.Locomotion == ShotLocomotion.Walk || context.Locomotion == ShotLocomotion.Run) &&
            context.Environment == ShotEnvironment.Open && context.EnvironmentConfidence >= .6f &&
            !context.DoorwayTransition && !confined && !(roomConfiner?.Active ?? false) && !recoverySearch &&
            space >= (passBy.Active ? .35f : .55f) && hasPose && lastSucceeded;
        Vector3 side = Vector3.Cross(Vector3.up, velocity);
        float preferredSide = hasPose && Vector3.Dot(lastPosition - focus, side) < 0 ? -1 : 1;
        candidate.PassBy = passBy.Update(now, pictureEpoch, ToCore(focus), ToCore(velocity), allowed,
            previousChoice == PassByIndex && was, preferredSide);
        if (was == candidate.PassBy) return false;
        bool displayed = previousChoice == PassByIndex && hasPose;
        candidate.BodySeedPosition = displayed ? lastPosition : candidate.Camera.PreviousStateIsValid
            ? candidate.Camera.State.GetFinalPosition() : focus + Offset(PassByIndex, NeutralMotion(), settings, confined);
        candidate.OrbitNeedsSeed = !candidate.PassBy;
        candidate.Trajectory.Reset(); candidate.Spatial.Reset(); candidate.PresentedSpatialOffset = Vector3.zero;
        candidate.Placement.Reset(); candidate.Elevation.Reset(); candidate.Framing.ResetFraming();
        candidate.TrajectoryFrame = -1; candidate.TrajectoryAt = -1;
        candidate.Camera.PreviousStateIsValid = false;
        candidate.RelaxUntil = candidate.RecoveryFovUntil = 0;
        evaluateAt = forecastAt = 0; longevity.Invalidate(PassByIndex);
        director.InvalidatePending(); stableSelection.InvalidatePending();
        return displayed;
    }

    private void UpdateTrajectory(int index, CameraSceneFrame context, Vector3 focus,
        BroadcastSettings settings, float now, float dt)
    {
        Candidate candidate = candidates[index];
        if (candidate.TrajectoryFrame == Time.frameCount) return;
        candidate.TrajectoryFrame = Time.frameCount;
        if (IsScenic) { UpdateScenicTrajectory(candidate, index, now, dt); return; }
        if (candidate.PassBy)
        {
            candidate.PreviousAnchor = candidate.TrackingPoint.position;
            candidate.TrackingPoint.position = FromCore(passBy.Plan.Camera);
            candidate.AnchorVelocity = Vector3.zero; candidate.TrajectoryBlocked = false;
            candidate.TrajectoryReason = passBy.Reason;
            return;
        }
        if (candidate.Observational)
        {
            // The native screen/depth composer follows the observed point.
            // No target-relative anchor planner may preempt its dead zones.
            candidate.TrajectoryReason = "native screen/depth response; fixed observation heading";
            candidate.TrajectoryBlocked = false;
            return;
        }
        candidate.GroupShot = settings.GroupFraming.Value && subjects.Count > 1 && index < 6;
        Vector3 center = candidate.GroupShot
            ? focus + Vector3.ClampMagnitude(context.Focus - focus, confined ? 1.5f : 4f) : focus;
        Vector3 sceneVelocity = candidate.GroupShot ? context.GroupVelocity : velocity;
        sceneVelocity = Vector3.ClampMagnitude(sceneVelocity, 20f);
        float elapsed = candidate.TrajectoryAt >= 0 ? Mathf.Clamp(now - candidate.TrajectoryAt, 0, 0.25f) : Mathf.Clamp(dt, 0, 0.1f);
        candidate.TrajectoryAt = now;
        float nominalDistance = Mathf.Max(3f, Offset(index, NeutralMotion(), settings, confined).magnitude);
        float lagBudget = Mathf.Clamp(nominalDistance * 0.55f, confined ? 1.5f : 3f, confined ? 3f : 10f);
        float framingPressure = 0;
        if (candidate.Camera.PreviousStateIsValid)
        {
            var view = candidate.Quality.Result;
            float range = Vector3.Distance(candidate.Camera.State.GetFinalPosition(), focus);
            framingPressure = Mathf.Max(1f - view.PrimaryReadability,
                Mathf.Max(Mathf.Clamp01((0.85f - view.PrimarySafeFrame) / 0.85f),
                    Mathf.InverseLerp(nominalDistance * 1.25f, nominalDistance * 1.8f, range)));
        }
        ShotTrajectoryKind kind = DynamicEnabled(settings) ? director.TrajectoryKind :
            context.Activity == CinematicActivity.Travel ? ShotTrajectoryKind.Tracking : ShotTrajectoryKind.ActivityArea;
        candidate.PreviousAnchor = candidate.TrackingPoint.position;
        candidate.TrajectoryState = candidate.Trajectory.Step(now, elapsed, ToCore(focus), ToCore(center),
            ToCore(sceneVelocity), context.Activity, kind, shotId, lagBudget, framingPressure, space,
            candidate.TrajectoryBlocked && now < predictionAt, context.Locomotion);
        if (candidate.OrbitNeedsSeed)
        {
            // Re-enter orbital tracking from the actual preceding body pose,
            // without reviving a planner left behind during observation.
            candidate.Trajectory.Synchronize(ToCore(candidate.BodySeedPosition - Offset(index, NeutralMotion(), settings, confined)), true);
            candidate.TrajectoryState = candidate.Trajectory.Preview(0);
            candidate.OrbitNeedsSeed = false;
        }
        if (candidate.TrajectoryState.WasReset) candidate.Spatial.Reset();
        if (index == previousChoice && hasPose)
            candidate.Spatial.Update(now, elapsed, context, candidate.Subjects, candidate.Trajectory,
                lastPosition, lastRotation, lastPoseFocus, lastPresentedAnchor, focus, lastFov, Aspect, NearClipPlane,
                space, visibility, roomConfiner, settings.PredictiveShots.Value && !EditorialHold &&
                lastSucceeded && PresentationConfirmed && transitionElapsed >= transitionSeconds);
        else candidate.Spatial.Suspend("spatial horizon waiting for this shot to be displayed");
        candidate.TrackingPoint.position = FromCore(candidate.TrajectoryState.Anchor) + candidate.Spatial.Offset;
        candidate.AnchorVelocity = FromCore(candidate.TrajectoryState.Velocity) + candidate.Spatial.Velocity;
        if (candidate.TrajectoryState.WasReset) candidate.PreviousAnchor = candidate.TrackingPoint.position;
        candidate.TrajectoryReason = candidate.TrajectoryBlocked ? "anchor waiting for clear path" :
            framingPressure > 0.2f ? "bounded framing correction" : "independent world trajectory";
    }

    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);

    private void Evaluate(int index, CinematicShotMotion motion, CameraSceneFrame context, Vector3 focus,
        BroadcastSettings settings, float dt, bool continuous, bool moveCarrier)
    {
        Candidate candidate = candidates[index];
        if (index >= BaseCount && !recoverySearch && index != previousChoice)
        { scores[index] = 0; eligible[index] = readable[index] = false; return; }
        // Four compact primary-only cameras remain available when a group cannot
        // be composed safely in the current room or vegetation.
        UpdateTrajectory(index, context, focus, settings, Time.unscaledTime, dt);
        candidate.Camera.Follow = candidate.TrackingPoint;
        candidate.Camera.LookAt = candidate.GroupShot ? group!.transform : candidate.AimPoint;
        bool useDolly = IsScenic && nativeScenicPath && scenicPath.Ready;
        // Exactly one Body component is active. Native spline traversal owns
        // scenery; OrbitalFollow retains its damped actor tracking behavior.
        candidate.Orbit.enabled = !useDolly && !candidate.Observational && !candidate.PassBy;
        candidate.Dolly.Enabled = useDolly;
        candidate.Aim.enabled = !candidate.Observational;
        // Deoccluder rotates its corrected view to preserve target placement.
        // Observation retains its fixed heading; scenery retains its validated
        // authored body path. Unsafe views fail complete quality/path checks
        // and yield to another candidate instead of a competing body correction.
        candidate.Obstacles.enabled = !IsScenic && !candidate.Observational && !candidate.PassBy;
        candidate.Observation.Enabled = candidate.Observational;
        if (candidate.PassBy)
        {
            // RotationComposer is the only procedural owner: world position
            // stays put as the player crosses, and no obstacle/body correction
            // may turn this shot into retreating frontal tracking.
            Vector3 fixedPosition = FromCore(passBy.Plan.Camera);
            candidate.Camera.transform.position = fixedPosition;
            if (!passBy.Active || !candidate.Camera.PreviousStateIsValid)
            {
                candidate.Camera.ForceCameraPosition(fixedPosition, Upright(Quaternion.LookRotation(candidate.AimFocus - fixedPosition, Vector3.up)));
                candidate.Camera.PreviousStateIsValid = false;
            }
        }
        if (useDolly)
            candidate.Dolly.Configure(scenicPath, scenicProgress, ScenicCandidateOffset(index), candidate.TrajectoryBlocked);
        if (candidate.Observational)
        {
            Vector3 tracked = context.PrimaryFocus;
            if (candidate.Subjects.Count > 0)
            {
                var primary = candidate.Subjects[0];
                tracked += Vector3.up * (primary.Height * ShotScalePolicy.Profile(candidate.Scale).AimHeightFraction - primary.BottomOffset);
            }
            Vector3 seed = candidate.ObservationNeedsSeed ? candidate.BodySeedPosition : candidate.Camera.State.GetFinalPosition();
            Quaternion orientation = candidate.ObservationNeedsSeed ? candidate.BodySeedOrientation : candidate.Observation.Orientation;
            float distance = Mathf.Clamp(Vector3.Dot(tracked - seed, orientation * Vector3.forward), 1f, 80f);
            if (candidate.ObservationNeedsSeed && settings.ShotScales.Value && candidate.Subjects.Count > 0)
                distance = Mathf.Clamp(ShotScalePolicy.Profile(candidate.Scale).CameraDistance(
                    candidate.Subjects[0].Height, candidate.Subjects[0].Radius, Aspect), 1f, 80f);
            float placement = settings.DeliberateComposition.Value ? index == 8 ? -.08f : .08f : 0;
            var profile = ObservationalShotPolicy.Describe(candidate.Scale, distance,
                Mathf.Clamp(settings.PositionDamping.Value * intent.PositionDampingScale, .5f, 3f), placement);
            candidate.AimFocus = tracked;
            if (!candidate.Observation.Configure(candidate.AimPoint, tracked, seed, orientation,
                profile, pictureEpoch, candidate.ObservationToken, Time.unscaledTime))
            { scores[index] = 0; eligible[index] = readable[index] = false; return; }
            candidate.ObservationNeedsSeed = false;
            candidate.Composition = new ShotComposition(placement, 0, placement, 0, true, "Fixed observational screen/depth guides");
            candidate.PresentedScreen = new Vector2(placement, 0);
        }
        Vector3 offset = IsScenic ? Vector3.zero : Offset(index, motion, settings, confined);
        candidate.Orbit.Radius = offset.magnitude;
        candidate.Orbit.HorizontalAxis.Value = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        candidate.Orbit.VerticalAxis.Value = Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
        candidate.Orbit.RadialAxis.Value = 1;
        candidate.Orbit.TrackerSettings = new TrackerSettings
        {
            BindingMode = BindingMode.WorldSpace,
            PositionDamping = new Vector3(1f, context.Locomotion == ShotLocomotion.Boat ? 1.8f : 1.2f, 1f) *
                Mathf.Clamp(settings.PositionDamping.Value * intent.PositionDampingScale, .1f, 4f),
            AngularDampingMode = AngularDampingMode.Quaternion
        };
        candidate.Aim.Damping = new Vector2(settings.AimDamping.Value, settings.AimDamping.Value * 1.2f) * intent.AimDampingScale;
        if (candidate.PassBy) candidate.Aim.Damping = new Vector2(.35f, .5f);
        candidate.Context = context; candidate.Settings = settings;
        candidate.Continuous = continuous; candidate.MoveCarrier = moveCarrier;
        // Upstream group framing sizes the lens before RotationComposer aims.
        // The group extension must not run a second, centered aim after it.
        candidate.Framing.FrameGroup = candidate.GroupShot;
        float preferredFov = IsScenic ? Mathf.Clamp(settings.FieldOfView.Value, 30f, 110f) : settings.ShotScales.Value
            ? ShotScalePolicy.Profile(candidate.Scale).VerticalFov
            : Mathf.Clamp(settings.FieldOfView.Value + intent.FovOffset, 35f, 100f);
        if (candidate.Observational) preferredFov = candidate.ObservationFov;
        if (candidate.PassBy) preferredFov = 55f;
        if (candidate.RecoveryFovUntil > Time.unscaledTime) preferredFov = Mathf.Max(preferredFov, candidate.RecoveryFov);
        candidate.Framing.FramingSize = intent.FramingSize;
        candidate.Framing.FovRange = new Vector2(Mathf.Max(35f, preferredFov - 8f), Mathf.Min(105f, preferredFov + 12f));
        candidate.Framing.Damping = Mathf.Max(1.5f, settings.AimDamping.Value * 1.5f);
        bool knownOpen = context.Environment == ShotEnvironment.Open && context.EnvironmentConfidence >= .6f &&
            !context.DoorwayTransition && !confined && !(roomConfiner?.Active ?? false);
        candidate.Framing.ConfigureDolly(Time.unscaledTime, settings.NativeGroupDolly.Value && !candidate.Observational && !candidate.PassBy,
            knownOpen, space, context.Spread, Mathf.Max(1f, Vector3.Distance(candidate.TrackingPoint.position + offset, focus)));
        if (RenderingCamera) candidate.Camera.Lens.PullInheritedPropertiesFromCamera(RenderingCamera);
        candidate.Camera.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        candidate.Camera.Lens.FieldOfView = preferredFov;
        candidate.Camera.Lens.NearClipPlane = Mathf.Max(0.01f, NearClipPlane);
        candidate.Quality.Focus = focus;
        candidate.Quality.Landscape = IsScenic ? LandscapeVisibility : null;
        candidate.Quality.Subjects = candidate.Subjects;
        candidate.Quality.Aspect = Aspect;
        candidate.Quality.NearClipPlane = NearClipPlane;
        bool rising = IsScenic && (input.ScenicPlan!.Style == ScenicShotStyle.Lift || input.ScenicPlan.Style == ScenicShotStyle.Reveal);
        float fraction = useDolly && scenicPath.Length > .01f ? candidate.Dolly.Distance / scenicPath.Length : 0;
        candidate.Elevation.Configure(settings.NativeFreeLook.Value && !candidate.Observational && !candidate.PassBy,
            !IsScenic && !candidate.Observational && !candidate.PassBy, useDolly, rising, fraction, candidate.GroupShot, candidate.Scale,
            confined, recoverySearch || candidate.TrajectoryBlocked || Time.unscaledTime < candidate.RelaxUntil ||
                Time.unscaledTime < candidate.RecoveryFovUntil, preferredFov, settings.DeliberateComposition.Value);
        float nativeDt = candidate.Observational && candidate.TrajectoryAt >= 0
            ? Mathf.Clamp(Time.unscaledTime - candidate.TrajectoryAt, 0, .25f) : dt;
        try { ManualCameraUpdate.Update(candidate.Camera, Vector3.up, nativeDt); }
        finally { candidate.Elevation.Restore(); }
        CameraState state = candidate.Camera.State;
        Vector3 solved = state.GetFinalPosition();
        if (candidate.Observational)
        {
            float elapsed = candidate.TrajectoryAt >= 0 ? Time.unscaledTime - candidate.TrajectoryAt : 0;
            candidate.AnchorVelocity = elapsed > 0 && elapsed <= .5f
                ? Vector3.ClampMagnitude((solved - candidate.TrackingPoint.position) / elapsed, 30f) : Vector3.zero;
            candidate.PreviousAnchor = candidate.TrackingPoint.position;
            candidate.TrackingPoint.position = solved; candidate.TrajectoryAt = Time.unscaledTime;
        }
        scores[index] = Mathf.Clamp01(state.ShotQuality + (candidate.GroupShot && candidate.Quality.Result.PrimaryVisibility > 0.65f ? 0.06f : 0));
        // Motion rejection does not reduce the quality of a still-readable live view.
        // Standby cameras are alternatives, not previously displayed footage.
        // Their stale positions cannot veto reacquisition after an obstacle.
        bool groupPath = !candidate.GroupShot || index != previousChoice || !hasPose || !lastSucceeded ||
            Time.unscaledTime - lastPoseAt > .5f ||
            GroupViewRoute(candidate, lastPosition, lastRotation, lastFov, solved,
                Upright(state.GetFinalOrientation()), state.Lens.FieldOfView);
        eligible[index] = candidate.Quality.Result.CameraClear && groupPath && PositionSupported(solved, focus, continuous, moveCarrier);
        readable[index] = eligible[index] && PictureUsable(candidate.Quality.Result);
        eligible[index] = ShotCandidateRanking.Admissible(SceneKind, eligible[index], readable[index]);
        if (candidate.PassBy && (!readable[index] || !passBy.Active && !PassByRouteReadable(candidate, context, continuous, moveCarrier)))
        { eligible[index] = readable[index] = false; }
        if (useDolly && readable[index]) candidate.Dolly.AcceptPosition();
        candidate.InterestReadable = IsScenic || !(context.HasInterest || NativeEncounterFraming.Active(context)) ||
            !NativeEncounterFraming.Active(context) && !(intent.Role == ShotRole.Interaction || intent.Role == ShotRole.Detail) ||
            visibility.InterestVisible(solved, state.GetFinalOrientation(), NativeEncounterFraming.Interest(context),
                state.Lens.FieldOfView, Aspect, NearClipPlane);
    }

    private bool PassByRouteReadable(Candidate candidate, CameraSceneFrame context, bool continuous, bool moveCarrier)
    {
        // Discretionary passage needs evidence at the crossing and departure,
        // not merely a clear approach. Test projected subjects without loading
        // terrain, mutating actors, or advancing another camera.
        if (candidate.Subjects.Count != 1) return false;
        Vector3 position = FromCore(passBy.Plan.Camera);
        for (int i = 0; i < 2; i++)
        {
            Vector3 futureFocus = FromCore(passBy.Plan.Origin) + FromCore(passBy.Plan.Direction) * passBy.Plan.Lead * (i == 0 ? 1 : 1.7f);
            if (!PositionSupported(position, futureFocus, continuous, moveCarrier)) return false;
            var subject = candidate.Subjects[0];
            futureSubjects.Clear();
            futureSubjects.Add(new PlayerShotVisibility.Subject(subject.Focus + futureFocus - context.PrimaryFocus,
                subject.Weight, subject.Radius, subject.Primary, subject.Height, subject.BottomOffset, subject.Scale));
            Vector3 aim = candidate.AimFocus + futureFocus - context.PrimaryFocus;
            Quaternion rotation = Upright(Quaternion.LookRotation(aim - position, Vector3.up));
            var view = visibility.Evaluate(position, rotation, futureSubjects, 55f, Aspect, NearClipPlane);
            if (!view.SurveyComplete || !PictureUsable(view)) return false;
        }
        return true;
    }

    private bool ValidateGroupTranslation(Candidate candidate, CameraState before, CameraState after)
    {
        Vector3 from = before.GetCorrectedPosition(), to = after.GetCorrectedPosition();
        if (!GroupViewRoute(candidate, from, Upright(before.GetCorrectedOrientation()), before.Lens.FieldOfView,
            to, Upright(after.GetCorrectedOrientation()), after.Lens.FieldOfView)) return false;
        var view = visibility.Evaluate(to, Upright(after.GetCorrectedOrientation()), candidate.Subjects,
            after.Lens.FieldOfView, Aspect, NearClipPlane);
        return view.CameraClear && PictureUsable(view);
    }

    private bool GroupViewRoute(Candidate candidate, Vector3 from, Quaternion fromRotation, float fromFov,
        Vector3 to, Quaternion toRotation, float toFov)
    {
        if (!PositionSupported(to, candidate.Quality.Focus, candidate.Continuous, candidate.MoveCarrier) ||
            !SegmentClear(from, to)) return false;
        float distance = Vector3.Distance(from, to), angle = Quaternion.Angle(fromRotation, toRotation);
        if (distance < .25f && angle < 2f) return true;
        int samples = distance >= 2f || angle >= 15f ? 3 : 1;
        for (int i = 1; i <= samples; i++)
        {
            float t = (float)i / (samples + 1);
            Vector3 position = Vector3.Lerp(from, to, t);
            if (!PositionSupported(position, candidate.Quality.Focus, candidate.Continuous, candidate.MoveCarrier)) return false;
            var view = visibility.Evaluate(position, Upright(Quaternion.Slerp(fromRotation, toRotation, t)),
                candidate.Subjects, Mathf.Lerp(fromFov, toFov, t), Aspect, NearClipPlane);
            if (!view.CameraClear || !PictureUsable(view)) return false;
        }
        return true;
    }

    private void ConfigureComposition(Candidate candidate, CameraState state, float dt)
    {
        if (candidate.Observational) return;
        CameraSceneFrame context = candidate.Context!;
        BroadcastSettings settings = candidate.Settings!;
        if (IsScenic)
        {
            ConfigureScenicComposition(candidate, settings, dt);
            candidate.Elevation.ApplyComposition(state, dt);
            candidate.PresentedScreen = new Vector2(candidate.Aim.Composition.ScreenPosition.x, -candidate.Aim.Composition.ScreenPosition.y);
            return;
        }
        Vector3 camera = state.GetCorrectedPosition(), anchor = state.ReferenceLookAt;
        if (!TargetRoster.Finite(anchor) || (anchor - camera).sqrMagnitude < 0.01f) return;
        Quaternion centered = Upright(Quaternion.LookRotation(anchor - camera, Vector3.up));
        float fov = state.Lens.FieldOfView;
        var framed = candidate.Subjects;
        var observation = new ShotCompositionObservation { Grouped = candidate.GroupShot, SemanticInterest = NativeEncounterFraming.Active(context) };
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
        if (context.HasInterest || observation.SemanticInterest)
        {
            Vector3 interest = ProjectPoint(NativeEncounterFraming.Interest(context), camera, centered, fov);
            if (interest.z > NearClipPlane)
            {
                observation.InterestX = Mathf.Clamp(interest.x, -1, 1);
                observation.InterestY = Mathf.Clamp(interest.y, -1, 1);
                observation.InterestConfidence = NativeEncounterFraming.Confidence(context);
                bool boundedInterest = !observation.SemanticInterest || EncounterFramingPolicy.IncludeInterestEnvelope(
                    context.EncounterDistance, interest.x, interest.y, interest.z > NearClipPlane);
                if (boundedInterest && (intent.Role == ShotRole.Interaction || intent.Role == ShotRole.Detail) && observation.InterestConfidence >= .6f)
                {
                    observation.LeftExtent = Mathf.Max(observation.LeftExtent, -interest.x + .05f);
                    observation.RightExtent = Mathf.Max(observation.RightExtent, interest.x + .05f);
                    observation.TopExtent = Mathf.Max(observation.TopExtent, interest.y + .05f);
                    observation.BottomExtent = Mathf.Max(observation.BottomExtent, -interest.y + .05f);
                }
            }
        }
        Vector3 primary = ProjectPoint(context.PrimaryFocus, camera, centered, fov);
        observation.PrimaryOffsetX = Mathf.Clamp(primary.x, -2, 2);
        observation.PrimaryOffsetY = Mathf.Clamp(primary.y, -2, 2);
        bool usable = !hasPose || lastSucceeded && PictureUsable(lastView);
        float now = Time.unscaledTime;
        candidate.Composition = candidate.Placement.Update(now, Mathf.Max(0, dt), shotId,
            context.Activity, observation, confined, space, director.RecentCorrections, usable,
            settings.DeliberateComposition.Value);
        ApplyComposition(candidate, settings, dt, now);
        candidate.Elevation.ApplyComposition(state, dt);
        candidate.PresentedScreen = new Vector2(candidate.Aim.Composition.ScreenPosition.x, -candidate.Aim.Composition.ScreenPosition.y);
    }

    private void ApplyComposition(Candidate candidate, BroadcastSettings settings, float dt, float now)
    {
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
        ref float fov, ref PlayerShotVisibility.Result result)
    {
        Vector3 envelopeCenter = focus;
        if (framed.Count > 0)
        {
            PlayerShotVisibility.Subject primary = framed[0];
            envelopeCenter = primary.Focus + Vector3.up *
                (primary.Height * ShotScalePolicy.Profile(primary.Scale).AimHeightFraction - primary.BottomOffset);
        }
        Quaternion centered = Upright(Quaternion.LookRotation(envelopeCenter - position, Vector3.up));
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
        if (!result.PrimaryInFrame || result.PrimarySafeFrame < .85f || result.PrimaryHeadroom < .025f)
        {
            float originalFov = fov;
            for (int i = 1; i <= 4; i++)
            {
                float wider = Mathf.Min(105f, originalFov + i * 5f);
                var view = visibility.Evaluate(position, rotation, framed, wider, Aspect, NearClipPlane);
                if (view.PrimaryReadability >= .8f && view.PrimarySafeFrame > result.PrimarySafeFrame)
                { result = view; fov = wider; }
                if (PictureUsable(result) && result.PrimarySafeFrame >= .9f) break;
            }
            if (fov > originalFov)
            { candidate.RecoveryFov = fov; candidate.RecoveryFovUntil = now + 4f; }
        }
        Vector3 screen = ProjectPoint(state.ReferenceLookAt, position, rotation, fov);
        if (screen.z <= NearClipPlane) return;
        candidate.AppliedScreen = candidate.RecoveryScreen = new Vector2(screen.x, screen.y);
        candidate.RelaxUntil = now + 3f; candidate.NextRecoveryAt = now + 0.6f;
        candidate.RecoveryReason = Quaternion.Angle(rotation, centered) < 0.5f ? "primary-centered safety fallback" : "relaxed placement to preserve the required subject envelope";
        var composition = candidate.Aim.Composition;
        composition.ScreenPosition = new Vector2(screen.x, -screen.y);
        candidate.Aim.Composition = composition;
        // ForceCameraPosition alone leaves RotationComposer's previous screen
        // offset intact. Re-run Aim only, at the same evaluated pose, to seed all
        // its tracking history. Body/obstacle damping remains untouched.
        state.RawPosition = position; state.PositionCorrection = Vector3.zero;
        state.RawOrientation = rotation; state.OrientationCorrection = Quaternion.identity;
        state.Lens.FieldOfView = fov;
        candidate.Aim.MutateCameraState(ref state, -1);
        rotation = Upright(state.RawOrientation);
        candidate.Aim.ForceCameraPosition(position, rotation);
        result = visibility.Evaluate(position, rotation, framed, fov, Aspect, NearClipPlane);
        NoteCorrection(now);
    }

    private void PrepareSelection(Vector3 focus, CameraSceneFrame context, bool usable, bool seed,
        BroadcastSettings settings, float now)
    {
        Array.Clear(seededEligibility, 0, Count);
        if (seed && previousChoice >= 0) seededEligibility[previousChoice] = eligible[previousChoice];
        bool preserveContinuity = usable && previousChoice >= 0 && eligible[previousChoice] && readable[previousChoice];
        Vector3 oldOffset = Vector3.ProjectOnPlane(lastPosition - lastPoseFocus, Vector3.up);
        ShotScale wanted = DesiredSequenceScale(settings, context);
        float pixelPenalty = settings.RenderedPictureEvaluation.Value && PictureMonitor != null && PictureContext.Valid
            ? PictureMonitor.Feedback(PictureContext.Key, now).Penalty : 0;
        for (int i = 0; i < Count; i++)
        {
            Candidate candidate = candidates[i];
            Vector3 position = candidate.Camera.State.GetFinalPosition();
            float score = scores[i];
            if (readable[i] && !IsScenic)
                score -= NativeEncounterFraming.Penalty(context, position,
                    Upright(candidate.Camera.State.GetFinalOrientation()), candidate.Camera.State.Lens.FieldOfView,
                    Aspect, NearClipPlane, visibility, candidate.Quality.Result, candidate.Subjects.Count);
            if (candidate.PassBy && readable[i] && (previousChoice < 0 || !candidates[previousChoice].PassBy) && director.ShotAge >= 8)
                score += .06f;
            if (settings.PredictiveShots.Value)
            {
                float age = Mathf.Max(0, now - forecastTime[i]);
                Vector3 expectedPosition = forecastPosition[i] + forecastVelocity[i] * age;
                Quaternion expectedRotation = PredictRotation(forecastPosition[i], forecastRotation[i], forecastAim[i],
                    expectedPosition, forecastAim[i] + forecastAimVelocity[i] * age);
                if ((position - expectedPosition).sqrMagnitude > 2.25f ||
                    Quaternion.Angle(candidate.Camera.State.GetFinalOrientation(), expectedRotation) > 15f ||
                    Mathf.Abs(candidate.Camera.State.Lens.FieldOfView - forecastFov[i]) > 5f)
                    longevity.Invalidate(i);
                score = longevity.AdjustScore(i, now, score, readable[i]);
            }
            if (!IsScenic && settings.ShotSequences.Value && DynamicEnabled(settings) && readable[i])
            {
                // A requested scale is a preference among healthy pictures. Keep
                // full-body and compact alternatives available at every beat.
                if (candidate.Scale != wanted) score -= .26f;
                if (!candidate.InterestReadable) score -= .12f;
            }
            if (preserveContinuity)
            {
                Vector3 offset = Vector3.ProjectOnPlane(position - focus, Vector3.up);
                if (i != previousChoice) score -= Mathf.InverseLerp(60, 150, ViewpointAngle(oldOffset, offset)) * .12f;
                if (!(candidate.PassBy && i == previousChoice))
                    score -= continuity.Penalty(ToCore(offset), intent.Role, candidate.Scale, readable[i], i == previousChoice);
            }
            // Pixel evidence belongs only to the actual displayed shot. A dark
            // current view says nothing about pixels in an unrendered candidate.
            if (i == previousChoice) score -= pixelPenalty;
            editorialScores[i] = Mathf.Clamp01(score);
        }
    }

    private ShotScale DesiredSequenceScale(BroadcastSettings settings, CameraSceneFrame context)
    {
        if (!settings.ShotScales.Value) return ShotScale.Full;
        if (IsScenic) return ShotScale.Wide;
        if (!DynamicEnabled(settings) && hasPose) return lastScale;
        if (!settings.ShotSequences.Value || !DynamicEnabled(settings))
            return IsScenic ? ShotScale.Wide : intent.Role == ShotRole.Establish ? ShotScale.Wide : ShotScale.Full;
        return continuity.NeedsReestablish ? ShotScale.Wide : ResolvedCueScale(settings, context);
    }

    private ShotScale ResolvedCueScale(BroadcastSettings settings, CameraSceneFrame context) =>
        !settings.ShotScales.Value ? ShotScale.Full : IsScenic ? ShotScale.Wide : ShotScalePolicy.Resolve(sequenceCue.Scale, Situation(context));

    private bool ConfigureObservation(int index, CameraSceneFrame context, Vector3 focus, BroadcastSettings settings)
    {
        Candidate candidate = candidates[index];
        bool requested = settings.NativePositionComposer.Value && (index == 8 || index == 9) &&
            ObservationalShotPolicy.CanUse(Situation(context), intent.Role, candidate.Scale, candidate.GroupShot,
                recovery: recoverySearch || (roomConfiner?.Active ?? false), continuing: candidate.Observational);
        bool profileChanged = requested && candidate.Observational &&
            (candidate.ObservationScale != candidate.Scale || candidate.ObservationComposition != settings.DeliberateComposition.Value ||
             !settings.ShotScales.Value && !Mathf.Approximately(candidate.ObservationSettingFov, settings.FieldOfView.Value));
        if (requested == candidate.Observational && !profileChanged) return false;
        bool displayed = index == previousChoice && hasPose;
        Vector3 offset = Offset(index, NeutralMotion(), settings, confined);
        candidate.BodySeedPosition = displayed ? lastPosition : candidate.Camera.PreviousStateIsValid
            ? candidate.Camera.State.GetFinalPosition() : focus + offset;
        candidate.BodySeedOrientation = displayed ? lastRotation : candidate.Camera.PreviousStateIsValid
            ? Upright(candidate.Camera.State.GetFinalOrientation()) : Upright(Quaternion.LookRotation(focus - candidate.BodySeedPosition, Vector3.up));
        candidate.ObservationFov = settings.ShotScales.Value ? ShotScalePolicy.Profile(candidate.Scale).VerticalFov :
            Mathf.Clamp(settings.FieldOfView.Value + intent.FovOffset, 35f, 100f);
        candidate.ObservationScale = candidate.Scale; candidate.ObservationComposition = settings.DeliberateComposition.Value;
        candidate.ObservationSettingFov = settings.FieldOfView.Value;
        candidate.Observational = requested; candidate.ObservationToken++;
        candidate.ObservationNeedsSeed = requested; candidate.OrbitNeedsSeed = !requested;
        candidate.Observation.Suspend(); candidate.Elevation.Reset(); candidate.Trajectory.Reset(); candidate.Spatial.Reset();
        candidate.PresentedSpatialOffset = Vector3.zero; candidate.Placement.Reset();
        candidate.TrajectoryFrame = -1; candidate.TrajectoryAt = -1; candidate.TrajectoryBlocked = false;
        candidate.Camera.PreviousStateIsValid = false;
        candidate.Framing.ResetFraming();
        candidate.RelaxUntil = candidate.RecoveryFovUntil = 0;
        longevity.Invalidate(index); eligible[index] = readable[index] = false;
        if (displayed)
        { acceptedMotion = approvedMotion = NeutralMotion(); motionBlocked = false; }
        return true;
    }

    private void ConfigureScale(Candidate candidate, int index, CameraSceneFrame context, BroadcastSettings settings)
    {
        candidate.GroupShot = !candidate.PassBy && !IsScenic && settings.GroupFraming.Value && subjects.Count > 1 && index < 6;
        bool observationWide = index == 8 && settings.NativePositionComposer.Value &&
            ObservationalShotPolicy.CanUse(Situation(context), intent.Role, ShotScale.Wide, false,
                recovery: recoverySearch || (roomConfiner?.Active ?? false), continuing: candidate.Observational);
        ShotScale scale = candidate.PassBy ? ShotScale.Wide : !settings.ShotScales.Value ? ShotScale.Full : IsScenic ? DesiredSequenceScale(settings, context) :
            index < 3 || observationWide ? ShotScale.Wide : index == 6 || index == 7 ? ShotScalePolicy.Resolve(ShotScale.Medium, Situation(context)) : ShotScale.Full;
        if (scale != candidate.Scale)
        {
            candidate.Scale = scale; longevity.Invalidate(index); candidate.Placement.Reset();
            candidate.RecoveryFov = candidate.RecoveryFovUntil = candidate.RelaxUntil = 0;
        }
        candidate.Subjects.Clear();
        int count = candidate.GroupShot ? subjects.Count : Math.Min(1, subjects.Count);
        for (int i = 0; i < count; i++)
        {
            PlayerShotVisibility.Subject subject = subjects[i];
            candidate.Subjects.Add(new PlayerShotVisibility.Subject(subject.Focus, subject.Weight, subject.Radius,
                subject.Primary, subject.Height, subject.BottomOffset, subject.Primary ? scale : ShotScale.Full));
        }
        candidate.AimFocus = lookPoint!.position;
        if (!IsScenic && settings.ShotScales.Value && candidate.Subjects.Count > 0)
        {
            PlayerShotVisibility.Subject primary = candidate.Subjects[0];
            candidate.AimFocus += Vector3.up * (primary.Height * ShotScalePolicy.Profile(scale).AimHeightFraction - primary.BottomOffset);
        }
        candidate.AimPoint.position = candidate.AimFocus;
    }

    // Current and at most two strongest readable alternatives are forecast at
    // 2.5 Hz. Three future samples use the existing bounded survey; no scene is
    // loaded and no virtual camera is advanced while asking a what-if question.
    private void ForecastCandidates(CameraSceneFrame context, Vector3 focus, BroadcastSettings settings,
        float now, bool continuous, bool moveCarrier)
    {
        forecastAt = now + .4f;
        if (longevity.MotionConfidence < .4f) return;
        int count = 0;
        if (previousChoice >= 0 && eligible[previousChoice] && readable[previousChoice])
            forecastCandidates[count++] = previousChoice;
        while (count < forecastCandidates.Length)
        {
            int best = -1;
            for (int i = 0; i < Count; i++)
            {
                if (!eligible[i] || !readable[i]) continue;
                bool used = false;
                for (int j = 0; j < count; j++) if (forecastCandidates[j] == i) used = true;
                if (!used && (best < 0 || scores[i] > scores[best])) best = i;
            }
            if (best < 0) break;
            forecastCandidates[count++] = best;
        }
        for (int n = 0; n < count; n++)
        {
            int index = forecastCandidates[n];
            Candidate candidate = candidates[index];
            CameraState state = candidate.Camera.State;
            forecastPosition[index] = state.GetFinalPosition(); forecastRotation[index] = state.GetFinalOrientation();
            forecastFov[index] = state.Lens.FieldOfView; forecastTime[index] = now;
            forecastVelocity[index] = candidate.AnchorVelocity;
            forecastAim[index] = state.ReferenceLookAt;
            Vector3 observedAimVelocity = candidate.GroupShot ? context.GroupVelocity : velocity;
            forecastAimVelocity[index] = TargetRoster.Finite(observedAimVelocity) ? Vector3.ClampMagnitude(observedAimVelocity, 20f) : Vector3.zero;
            if (IsScenic)
            {
                float progress = Mathf.Clamp01(scenicProgress + (DynamicEnabled(settings) && settings.ShotMotion.Value > 0 ? .5f * ScenicProgressSpeed(settings) / Mathf.Max(5f, settings.ScenicDwell.Value) : 0));
                ScenicShotSample sample = DynamicEnabled(settings) && settings.ShotMotion.Value > 0
                    ? SampleScenic(progress) : scenicSample;
                forecastVelocity[index] = (FromCore(sample.CameraPosition) - FromCore(scenicSample.CameraPosition)) * 2f;
                forecastAimVelocity[index] = (FromCore(sample.AimPosition) - focus) * 2f;
            }
            PredictedVisibility near = ForecastView(index, .5f, context, focus, settings, continuous, moveCarrier);
            PredictedVisibility middle = near == PredictedVisibility.Readable
                ? ForecastView(index, 1f, context, focus, settings, continuous, moveCarrier) : PredictedVisibility.Unknown;
            PredictedVisibility far = middle == PredictedVisibility.Readable
                ? ForecastView(index, 1.5f, context, focus, settings, continuous, moveCarrier) : PredictedVisibility.Unknown;
            longevity.Record(index, now, near, middle, far);
        }
    }

    private PredictedVisibility ForecastView(int index, float seconds, CameraSceneFrame context, Vector3 focus,
        BroadcastSettings settings, bool continuous, bool moveCarrier)
    {
        Candidate candidate = candidates[index];
        // Native screen/depth response cannot be forecast as an orbital anchor
        // or a target-centered rotation. Missing prediction remains neutral.
        if (candidate.Observational) return PredictedVisibility.Unknown;
        CameraState state = candidate.Camera.State;
        Vector3 position = state.GetFinalPosition(), future, predictedFocus;
        futureSubjects.Clear();
        if (IsScenic)
        {
            float progress = Mathf.Clamp01(scenicProgress + (DynamicEnabled(settings) && settings.ShotMotion.Value > 0 ? seconds * ScenicProgressSpeed(settings) / Mathf.Max(5f, settings.ScenicDwell.Value) : 0));
            ScenicShotSample sample = DynamicEnabled(settings) && settings.ShotMotion.Value > 0
                    ? SampleScenic(progress) : scenicSample;
            future = position + FromCore(sample.CameraPosition) - FromCore(scenicSample.CameraPosition);
            predictedFocus = FromCore(sample.AimPosition);
        }
        else
        {
            future = candidate.PassBy ? position : position + FromCore(candidate.Trajectory.PreviewCoverage(seconds).Anchor) +
                candidate.Spatial.Offset - candidate.TrackingPoint.position;
            predictedFocus = focus + velocity * seconds;
            for (int i = 0; i < candidate.Subjects.Count && i < context.Subjects.Count; i++)
            {
                PlayerShotVisibility.Subject subject = candidate.Subjects[i];
                CameraSceneSubject observed = context.Subjects[i];
                if (!TargetRoster.Finite(observed.Velocity)) return PredictedVisibility.Unknown;
                futureSubjects.Add(new PlayerShotVisibility.Subject(subject.Focus + Vector3.ClampMagnitude(observed.Velocity, 20f) * seconds,
                    subject.Weight, subject.Radius, subject.Primary, subject.Height, subject.BottomOffset, subject.Scale));
            }
        }
        if (!PositionSupported(future, predictedFocus, continuous, moveCarrier)) return PredictedVisibility.Unknown;
        Vector3 aim = state.ReferenceLookAt;
        if (!TargetRoster.Finite(aim)) aim = focus;
        Vector3 aimDelta;
        if (IsScenic) aimDelta = predictedFocus - focus;
        else if (candidate.GroupShot)
        {
            if (!TargetRoster.Finite(context.GroupVelocity)) return PredictedVisibility.Unknown;
            aimDelta = Vector3.ClampMagnitude(context.GroupVelocity, 20f) * seconds;
        }
        else
        {
            Vector3 futureAim = predictedFocus;
            if (context.HasInterest && intent.InterestWeight > 0)
            {
                Vector3 predictedInterest = context.InterestFocus;
                if (context.InterestKind == "opponent")
                {
                    CameraSceneSubject? observedOpponent = null;
                    foreach (CameraSceneSubject subject in context.Subjects)
                        if (subject.IsOpponent && (subject.Focus - context.InterestFocus).sqrMagnitude < 4f &&
                            (observedOpponent == null || (subject.Focus - context.InterestFocus).sqrMagnitude <
                                (observedOpponent.Focus - context.InterestFocus).sqrMagnitude)) observedOpponent = subject;
                    if (observedOpponent == null || !TargetRoster.Finite(observedOpponent.Velocity)) return PredictedVisibility.Unknown;
                    predictedInterest += Vector3.ClampMagnitude(observedOpponent.Velocity, 20f) * seconds;
                }
                else if (context.InterestKind != "resource") return PredictedVisibility.Unknown;
                futureAim += Vector3.ClampMagnitude(predictedInterest - predictedFocus, confined ? 2f : 6f) * intent.InterestWeight;
            }
            if (settings.ShotScales.Value && candidate.Subjects.Count > 0)
            {
                PlayerShotVisibility.Subject primary = candidate.Subjects[0];
                futureAim += Vector3.up * (primary.Height * ShotScalePolicy.Profile(candidate.Scale).AimHeightFraction - primary.BottomOffset);
            }
            aimDelta = futureAim - candidate.AimFocus;
        }
        Quaternion rotation = PredictRotation(position, state.GetFinalOrientation(), aim, future, aim + aimDelta);
        // A swept solid obstruction is useful evidence even when the camera
        // endpoint has no foliage verdict; ungenerated support stays unknown.
        if (!SegmentClear(position, future)) return PredictedVisibility.Obstructed;
        PlayerShotVisibility.Result view = EvaluateView(future, rotation, futureSubjects, state.Lens.FieldOfView, predictedFocus);
        if (!view.SurveyComplete) return PredictedVisibility.Unknown;
        return PictureUsable(view) ? PredictedVisibility.Readable : PredictedVisibility.Obstructed;
    }

    private void UpdatePictureContext(int choice, Candidate candidate, Vector3 position, Quaternion rotation, float fov, float now)
    {
        PictureRegion region = default;
        bool hasPrimary = !IsScenic && candidate.Subjects.Count > 0;
        if (hasPrimary)
        {
            var envelope = visibility.ProjectSubject(position, rotation, candidate.Subjects[0], fov, Aspect, NearClipPlane);
            hasPrimary = envelope.InFront && envelope.MaxX > envelope.MinX && envelope.MaxY > envelope.MinY;
            if (hasPrimary)
            {
                region = new PictureRegion(Mathf.Clamp01((float)envelope.MinX), Mathf.Clamp01((float)envelope.MinY),
                    Mathf.Clamp01((float)envelope.MaxX), Mathf.Clamp01((float)envelope.MaxY));
                hasPrimary = region.IsValid;
            }
        }
        // Player and wildlife solves have independent lifetimes. Namespace
        // their candidate identities so equal local epoch/revision counters
        // cannot reuse the other rig's rendered-pixel feedback.
        int pictureCandidate = PictureNamespace * Count * 2 + choice + (input.Kind == CinematicShotKind.Creature ? Count : 0);
        PictureContext = new RenderedPictureContext(new RenderedPictureKey(pictureEpoch, pictureRevision, pictureCandidate),
            region, hasPrimary, Time.frameCount, now);
    }

    internal RenderedPictureContext? BuildPictureContext() => lastSucceeded && PictureContext.Valid &&
        PictureContext.Frame == Time.frameCount && transitionElapsed >= transitionSeconds ? PictureContext : (RenderedPictureContext?)null;

    private static float ViewpointAngle(Vector3 a, Vector3 b) =>
        Vector3.Angle(Vector3.ProjectOnPlane(a, Vector3.up), Vector3.ProjectOnPlane(b, Vector3.up));

    private void AdvanceMotion(int index, CinematicShotMotion requested, CameraSceneFrame context, Vector3 focus,
        BroadcastSettings settings, float now, float step, bool continuous, bool moveCarrier)
    {
        // Trajectory and editorial-offset previews are independent. A blocked
        // decorative arc cannot freeze otherwise necessary, safe tracking.
        if (now < predictionAt) { SmoothMotion(step); return; }
        predictionAt = now + 0.2f;
        Candidate candidate = candidates[index];
        var shotSubjects = candidate.Subjects;
        Vector3 anchor = candidate.TrackingPoint.position;
        Vector3 wanted = lastPosition + anchor - lastPresentedAnchor;
        Vector3 future = wanted + FromCore(candidate.Trajectory.Preview(0.75).Anchor) + candidate.Spatial.Offset - anchor;
        Vector3 predictedFocus = focus + velocity * 0.75f;
        futureSubjects.Clear();
        for (int i = 0; i < shotSubjects.Count && i < context.Subjects.Count; i++)
        {
            CameraSceneSubject subject = context.Subjects[i];
            Vector3 predicted = subject.Focus + Vector3.ClampMagnitude(subject.Velocity, 20f) * 0.75f;
            futureSubjects.Add(new PlayerShotVisibility.Subject(predicted, subject.Weight, subject.Radius,
                subject.IsPrimary, subject.Height, subject.BottomOffset, shotSubjects[i].Scale));
        }
        Quaternion wantedRotation = PredictRotation(lastPosition, lastRotation, lastPoseFocus, wanted, focus);
        Quaternion futureRotation = PredictRotation(wanted, wantedRotation, focus, future, predictedFocus);
        // Necessary tracking uses the current supported path. Uncertain future
        // terrain or foliage can stop development, but cannot strand an otherwise
        // valid camera/carrier while the observed player keeps moving.
        var currentPath = visibility.Evaluate(wanted, wantedRotation, shotSubjects, lastFov, Aspect, NearClipPlane);
        if (!currentPath.CameraClear || !SegmentClear(lastPosition, wanted) ||
            !PositionSupported(wanted, focus, continuous, moveCarrier))
        {
            // Roll the plan back as well as its helper transform. Otherwise the
            // hidden planner could run far ahead and jump when clearance returns.
            Vector3 acceptedAnchor = index == previousChoice ? lastPresentedAnchor : candidate.PreviousAnchor;
            SynchronizeActorTrajectory(candidate, acceptedAnchor,
                index == previousChoice ? candidate.PresentedSpatialOffset : candidate.Spatial.Offset);
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
        CinematicShotMotion preview = DynamicEnabled(settings) ? director.Motion(MotionStrength(settings), 0.75) : requested;
        float wantedHeading = heading, futureHeading = heading;
        if (DynamicEnabled(settings) && settings.ShotMotion.Value > 0 && context.Activity == CinematicActivity.Travel &&
            new Vector2(velocity.x, velocity.z).sqrMagnitude > 4f && space > 0.5f && now - lastCorrection > 5f && director.RecentCorrections == 0)
        {
            float trailing = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg + 155f;
            wantedHeading = Mathf.MoveTowardsAngle(heading, trailing, 1f);
            futureHeading = Mathf.MoveTowardsAngle(wantedHeading, trailing, 3.75f);
        }
        Vector3 currentOffset = Offset(index, acceptedMotion, settings, confined);
        wanted += OffsetAtHeading(index, requested, settings, confined, wantedHeading) - currentOffset;
        future += OffsetAtHeading(index, preview, settings, confined, futureHeading) - currentOffset;
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
        return PictureUsable(near) && PictureUsable(ahead) &&
            near.Score >= Mathf.Max(0.2f, lastView.Score - 0.1f) &&
            ahead.Score >= Mathf.Max(0.2f, lastView.Score - 0.12f) &&
            SegmentClear(lastPosition, wanted) && SegmentClear(wanted, future) &&
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

    private bool PositionSupported(Vector3 position, Vector3 focus, bool continuous, bool moveCarrier)
    {
        if (roomConfiner != null && !roomConfiner.Contains(position) ||
            !TargetRoster.Finite(position) || (position - focus).sqrMagnitude < 0.75f * 0.75f ||
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

    private Vector3 OffsetAtHeading(int index, CinematicShotMotion motion, BroadcastSettings settings, bool inside, float heading)
    {
        float scale = input.Kind == CinematicShotKind.Creature ? creatureScale : 1f;
        if (index >= BaseCount)
        {
            AutonomyPosition recovery = ProceduralCameraGeometry.RecoveryOffset(index - BaseCount,
                recoveryHeadings[index - BaseCount], scale);
            return CapToCeiling(FromCore(recovery));
        }
        float distance = settings.Distance.Value * scale * intent.RadiusScale;
        float height = settings.Height.Value * scale * intent.HeightScale;
        if (settings.ShotScales.Value && activeContext != null && activeContext.Subjects.Count > 0)
        {
            Candidate candidate = candidates[index];
            CameraSceneSubject primary = activeContext.Subjects[0];
            ShotScaleProfile profile = ShotScalePolicy.Profile(candidate.Scale);
            float desired = profile.CameraDistance(primary.Height, primary.Radius, Aspect);
            if (candidate.GroupShot) desired = Mathf.Max(desired, activeContext.Spread * .8f + desired * .6f);
            // Offset geometry halves its compact candidates. Compensate so the
            // requested full/medium scale remains deliberate at those angles.
            float compact = index >= 6 ? index == 9 ? .45f : .5f : 1f;
            distance = desired / compact;
            height = Mathf.Min(height, desired * (candidate.Scale == ShotScale.Wide ? .35f : .12f));
        }
        if (activeContext != null && activeContext.Environment == ShotEnvironment.Sheltered)
        { distance = Mathf.Min(distance, 10f * scale); height = Mathf.Min(height, 3f * scale); }
        AutonomyPosition value = ProceduralCameraGeometry.Offset(index, distance, height,
            (encounterFraming.Heading + Mathf.DeltaAngle(this.heading, heading)) + motion.AzimuthOffset, inside);
        float radius = Mathf.Clamp(motion.RadiusScale, 0.8f, 1.15f);
        float lift = Mathf.Max(0.25f, value.Y + Mathf.Clamp(motion.HeightOffset, -1.5f, 2f));
        return CapToCeiling(new Vector3(value.X * radius, lift, value.Z * radius));
    }

    private Vector3 CapToCeiling(Vector3 offset)
    {
        if (activeContext != null && activeContext.EnvironmentConfidence >= .6f && activeContext.CeilingHeight > 0 && actor)
        {
            float focusHeight = activeContext.PrimaryFocus.y - actor.transform.position.y;
            offset.y = Mathf.Min(offset.y, Mathf.Max(-.3f, activeContext.CeilingHeight - focusHeight - .6f));
        }
        return offset;
    }

    private static CinematicShotMotion NeutralMotion() => new CinematicShotMotion(0, 1, 0, "hold");

    private void EnsureCreated()
    {
        if (owner) return;
        owner = new GameObject("Broadcaster procedural cameras") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        roomConfiner = new NativeRoomConfiner(owner.transform);
        clearShot = new NativeClearShotRanker(owner.transform, Count);
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
        var cameraChildren = new CinemachineCamera[Count];
        for (int i = 0; i < Count; i++)
        {
            var child = new GameObject("Player shot " + (i + 1));
            child.transform.SetParent(clearShot.CameraParent, false);
            var trackingPoint = new GameObject("Shot trajectory " + (i + 1)).transform;
            trackingPoint.SetParent(owner.transform, false);
            var camera = child.AddComponent<CinemachineCamera>();
            cameraChildren[i] = camera;
            camera.Follow = trackingPoint; camera.LookAt = lookPoint;
            camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            camera.PreviousStateIsValid = false;
            var elevationSource = NativeFreeLookProfiles.AddSource(child);
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
            // Native confinement follows obstacle correction, and the complete
            // resulting pose is scored below and revalidated before display.
            roomConfiner.Bind(camera);
            var framing = child.AddComponent<CinematicLensFraming>();
            framing.FramingSize = 0.65f;
            framing.SizeAdjustment = CinemachineGroupFraming.SizeAdjustmentModes.ZoomOnly;
            framing.LateralAdjustment = CinemachineGroupFraming.LateralAdjustmentModes.ChangeRotation;
            framing.Damping = 1.5f;
            var quality = child.AddComponent<FoliageShotQuality>(); quality.Visibility = visibility;
            var dolly = new NativeScenicDolly(camera, owner.transform);
            var observation = new NativeObservationalCamera(camera);
            var elevation = new NativeFreeLookProfiles(camera, orbit, aim, elevationSource);
            var candidate = new Candidate(camera, orbit, aim, framing, quality, trackingPoint,
                new GameObject("Shot aim " + (i + 1)).transform, dolly, obstacles, observation, elevation);
            candidate.AimPoint.SetParent(owner.transform, false);
            framing.BeforeAim = (state, delta) => ConfigureComposition(candidate, state, delta);
            framing.ValidateTranslation = (before, after) => ValidateGroupTranslation(candidate, before, after);
            candidates[i] = candidate;
        }
        clearShot.Bind(cameraChildren);
        owner.SetActive(true);
    }

    private bool Fail(string reason)
    {
        RollBackActorGuidance();
        visiblePlayers.Clear(); lastSucceeded = LastPictureUsable = lastCuePresented = false; PictureContext = default; LastFailure = reason;
        Diagnostics = "Procedural camera: " + reason;
        return false;
    }

    private static void SynchronizeActorTrajectory(Candidate candidate, Vector3 acceptedAnchor, Vector3 acceptedOffset)
    {
        candidate.Spatial.Reject(acceptedOffset);
        // Keep nominal trajectory and avoidance offset distinct. Feeding the
        // composed anchor into the nominal policy would accumulate the offset
        // again on the next frame and defeat rollback/continuity.
        candidate.Trajectory.Synchronize(ToCore(acceptedAnchor - acceptedOffset), true);
        candidate.TrajectoryState = candidate.Trajectory.Preview(0);
        candidate.TrackingPoint.position = acceptedAnchor;
        candidate.AnchorVelocity = Vector3.zero;
    }

    private void RollBackActorGuidance()
    {
        if (IsScenic || !hasPose || previousChoice < 0 || candidates[previousChoice] == null) return;
        Candidate candidate = candidates[previousChoice];
        if (!candidate.Observational && !candidate.PassBy)
            SynchronizeActorTrajectory(candidate, lastPresentedAnchor, candidate.PresentedSpatialOffset);
    }

    internal void Reset() => Reset(false);

    private void Reset(bool preservePlayerContext)
    {
        director.Reset(); stableSelection.Reset(); sequence.Reset(); continuity.Reset(); longevity.Reset();
        roomConfiner?.Clear(); clearShot?.Reset(); scenicPath.Reset(); scenicClock.Reset(); nativeBlend.Reset(); passBy.Reset(); encounterFraming.Reset();
        pictureEpoch++; pictureRevision = 0; PictureContext = default; shownCueRevision = -1; lastCuePresented = false; forecastAt = 0;
        if (!preservePlayerContext) scene.Reset(); creatureScene.Reset(); visibility.Reset(); ownedLandscapeVisibility.Reset();
        Array.Clear(scores, 0, Count); Array.Clear(eligible, 0, Count); Array.Clear(readable, 0, Count); visiblePlayers.Clear(); subjects.Clear(); primaryOnly.Clear(); futureSubjects.Clear();
        if (owner)
            foreach (Candidate candidate in candidates)
                if (candidate != null && candidate.Camera)
                {
                    candidate.Camera.PreviousStateIsValid = false; candidate.Placement.Reset(); candidate.Trajectory.Reset();
                    candidate.Spatial.Reset(); candidate.PresentedSpatialOffset = Vector3.zero;
                    candidate.Dolly.Reset(); candidate.Observation.Reset(); candidate.Elevation.Reset(); candidate.Framing.ResetFraming();
                    candidate.PassBy = false;
                    candidate.Observational = candidate.ObservationNeedsSeed = candidate.OrbitNeedsSeed = candidate.ObservationComposition = false;
                    candidate.ObservationToken = 0; candidate.BodySeedPosition = Vector3.zero; candidate.BodySeedOrientation = Quaternion.identity;
                    candidate.ObservationFov = candidate.ObservationSettingFov = 0; candidate.ObservationScale = ShotScale.Full;
                    candidate.TrajectoryFrame = -1; candidate.TrajectoryAt = -1; candidate.TrajectoryBlocked = false;
                    candidate.TrajectoryReason = "awaiting trajectory";
                    candidate.RelaxUntil = candidate.NextRecoveryAt = candidate.RecoveryFov = candidate.RecoveryFovUntil = 0; candidate.RecoveryReason = "none";
                    candidate.AppliedScreen = candidate.RecoveryScreen = candidate.PresentedScreen = Vector2.zero;
                }
        actor = null; subjectId = null; world = null; revision = -1;
        velocity = lastAnchorVelocity = renderedVelocity = transitionVelocity = Vector3.zero; hasPose = lastSucceeded = false; solvedAt = lastPoseAt = -1; solvedFrame = -1;
        evaluateAt = predictionAt = 0; previousChoice = motionChoice = -1; transitionElapsed = 2; space = 1;
        motionBlocked = false; acceptedMotion = approvedMotion = NeutralMotion(); lastCorrection = -10; fallbackSince = -1;
        playerSceneId = 0; playerContextFrame = activeContext = null; playerContextAt = -1;
        unreadableSince = readableSince = -1; recoverySearchAt = 0; recoveryEpoch = 0; recoverySearch = confined = false;
        transitionSeconds = 1.4f; intent = director.Intent;
        shotId = 0; lastDynamic = lastObservational = transitionFromObservational = false;
        lastGroupDollySetting = lastFreeLookSetting = false;
        pendingHandoff = LastPictureUsable = nativeScenicPath = false; scenicSampleAt = -1;
        LastFailure = "Awaiting player camera"; Diagnostics = "Procedural camera: awaiting player";
    }

    public void Dispose()
    {
        Reset();
        roomConfiner?.Dispose(); roomConfiner = null;
        foreach (Candidate candidate in candidates)
            if (candidate != null) { candidate.Elevation.Dispose(); candidate.Observation.Dispose(); candidate.Dolly.Dispose(); }
        scenicPath.Dispose(); clearShot?.Dispose(); clearShot = null;
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
