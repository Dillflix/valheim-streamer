using System;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Cinemachine solves virtual cameras only. The existing game-camera adapter is
// the sole writer of the rendered camera and validates carrier movement first.
internal sealed class CinemachinePlayerRig : IDisposable
{
    private const int Count = ProceduralCameraGeometry.CandidateCount;
    private readonly PlayerShotVisibility visibility = new PlayerShotVisibility();
    private readonly ProceduralShotPolicy selection = new ProceduralShotPolicy();
    private readonly float[] scores = new float[Count];
    private readonly bool[] eligible = new bool[Count];
    private readonly Candidate[] candidates = new Candidate[Count];
    private GameObject? owner;
    private Transform? followPoint, lookPoint;
    private Player? actor;
    private ZNet? world;
    private ZDOID actorId;
    private string? subjectId;
    private long revision = -1;
    private Vector3 previousFocus, velocity, lastPosition, lastPoseFocus, lastLead, surveyedFocus;
    private Quaternion lastRotation = Quaternion.identity;
    private float solvedAt = -1, lastPoseAt = -1, evaluateAt, lastFov;
    private int solvedFrame = -1;
    private bool hasPose, interior, lastSucceeded;
    internal float NearClipPlane { get; set; } = 0.3f;
    internal float Aspect { get; set; } = 16f / 9f;
    internal string LastFailure { get; private set; } = "Awaiting player camera";
    internal string Diagnostics { get; private set; } = "Procedural camera: awaiting player";

    private sealed class Candidate
    {
        internal readonly CinemachineCamera Camera;
        internal readonly CinemachineFollow Follow;
        internal readonly FoliageShotQuality Quality;
        internal Candidate(CinemachineCamera camera, CinemachineFollow follow, FoliageShotQuality quality)
        { Camera = camera; Follow = follow; Quality = quality; }
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
            previousFocus = focus;
        }
        if (solvedFrame == Time.frameCount)
        { position = lastPosition; rotation = lastRotation; fov = lastFov; return lastSucceeded; }
        EnsureCreated();
        solvedFrame = Time.frameCount;
        float step = Mathf.Clamp(dt, 0, 0.1f);
        if (!changed && dt > 0.001f && dt < 0.5f)
        {
            Vector3 measured = (focus - previousFocus) / dt;
            measured.y = 0;
            measured = Vector3.ClampMagnitude(measured, 20f);
            velocity = Vector3.Lerp(velocity, measured, 1f - Mathf.Exp(-4f * step));
        }
        else velocity = Vector3.zero;
        previousFocus = focus; solvedAt = now;
        Vector3 lead = Vector3.ClampMagnitude(velocity * 0.3f, 3f);
        // Translational look-ahead follows sustained movement without rotating
        // the preferred world heading whenever the actor turns or stops.
        followPoint!.position = focus + lead;
        lookPoint!.position = focus;
        visibility.Refresh(focus);
        int selected = selection.SelectedIndex;
        bool evaluateAll = selected < 0 || now >= evaluateAt || (focus - surveyedFocus).sqrMagnitude > 4f;
        if (evaluateAll)
        {
            evaluateAt = now + 0.2f; surveyedFocus = focus;
            for (int i = 0; i < Count; i++) Evaluate(i, focus, settings, dt, continuous, moveCarrier);
        }
        else Evaluate(selected, focus, settings, dt, continuous, moveCarrier);
        int choice = selection.Select(now, scores, eligible);
        if (choice < 0) return Fail("No player camera with loaded geometry and safe physical clearance");
        // A newly chosen standby candidate may have been scored up to 0.2s ago.
        // Refresh it before using its pose; the CM bridge guards duplicate calls.
        if (choice != selected && !evaluateAll)
        {
            Evaluate(choice, focus, settings, dt, continuous, moveCarrier);
            if (!eligible[choice])
            {
                for (int i = 0; i < Count; i++) Evaluate(i, focus, settings, dt, continuous, moveCarrier);
                choice = selection.Select(now, scores, eligible);
                if (choice < 0) return Fail("Alternate player camera geometry became unavailable");
            }
        }
        CameraState chosen = candidates[choice].Camera.State;
        Vector3 desired = chosen.GetFinalPosition();
        Vector3 visible = desired;
        string transition = choice != selected ? "reframing" : "tracking";
        if (hasPose)
        {
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, settings.Smoothing.Value) * step);
            Vector3 interpolated = Vector3.Lerp(lastPosition, desired, blend);
            var view = visibility.Evaluate(interpolated, focus, settings.FieldOfView.Value, Aspect, NearClipPlane);
            if (view.CameraClear && PositionSupported(interpolated, focus, continuous, moveCarrier) &&
                visibility.SegmentClear(lastPosition, interpolated))
                visible = interpolated;
            else
                transition = "cut around blocked transition";
            // If the old path is now in a wall, a validated cut to a candidate is
            // preferable to smoothing through it. Foliage alone never forces a cut.
        }
        Vector3 viewDirection = focus - visible;
        if (Vector3.ProjectOnPlane(viewDirection, Vector3.up).sqrMagnitude < 0.25f ||
            viewDirection.sqrMagnitude < 0.75f * 0.75f)
            return Fail("Player camera has no stable viewing direction");
        position = visible;
        rotation = visible == desired ? chosen.GetFinalOrientation() : Quaternion.LookRotation(viewDirection, Vector3.up);
        if (!Finite(rotation)) return Fail("Player camera orientation unavailable");
        fov = chosen.Lens.FieldOfView;
        lastPosition = position; lastPoseFocus = focus; lastPoseAt = now; lastLead = lead;
        lastRotation = rotation; lastFov = fov; hasPose = lastSucceeded = true;
        PlayerShotVisibility.Result result = candidates[choice].Quality.Result;
        LastFailure = "none";
        Diagnostics = $"Procedural camera: Cinemachine 3.1.7; shot {choice + 1}/{Count}; quality {scores[choice]:F2}; {result.Reason}; {transition}; lead {lead.magnitude:F1}m";
        return true;
    }

    private void Evaluate(int index, Vector3 focus, BroadcastSettings settings, float dt, bool continuous, bool moveCarrier)
    {
        Candidate candidate = candidates[index];
        candidate.Follow.FollowOffset = Offset(index, settings, interior);
        candidate.Follow.TrackerSettings = new TrackerSettings
        {
            BindingMode = BindingMode.WorldSpace,
            PositionDamping = Vector3.one * Mathf.Clamp(1f / settings.Smoothing.Value, 0.03f, 2f),
            AngularDampingMode = AngularDampingMode.Quaternion
        };
        candidate.Camera.Lens.FieldOfView = settings.FieldOfView.Value;
        candidate.Camera.Lens.NearClipPlane = Mathf.Max(0.01f, NearClipPlane);
        candidate.Quality.Focus = focus;
        candidate.Quality.Aspect = Aspect;
        candidate.Quality.NearClipPlane = NearClipPlane;
        candidate.Quality.PreferredDistance = Mathf.Max(2f, settings.Distance.Value);
        ManualCameraUpdate.Update(candidate.Camera, Vector3.up, dt);
        CameraState state = candidate.Camera.State;
        Vector3 solved = state.GetFinalPosition();
        scores[index] = Mathf.Clamp01(state.ShotQuality * (index == 0 ? 1f : 0.97f));
        eligible[index] = candidate.Quality.Result.CameraClear && PositionSupported(solved, focus, continuous, moveCarrier);
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

    // The travel policy uses geometry only. Occluded or incomplete foliage
    // surveys cannot turn a nearby player into a teleport request.
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
        // Use the lead that actually produced a recent successful pose. A blanket
        // look-ahead allowance rejects reachable shots near the travel threshold.
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

    private static Vector3 Offset(int index, BroadcastSettings settings, bool inside)
    {
        AutonomyPosition offset = ProceduralCameraGeometry.Offset(index, settings.Distance.Value,
            settings.Height.Value, settings.Azimuth.Value, inside);
        return new Vector3(offset.X, offset.Y, offset.Z);
    }

    private void EnsureCreated()
    {
        if (owner) return;
        owner = new GameObject("Broadcaster procedural cameras") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        followPoint = new GameObject("Tracking focus").transform; followPoint.SetParent(owner.transform, false);
        lookPoint = new GameObject("Subject focus").transform; lookPoint.SetParent(owner.transform, false);
        int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        for (int i = 0; i < Count; i++)
        {
            var child = new GameObject("Player shot " + (i + 1));
            child.transform.SetParent(owner.transform, false);
            var camera = child.AddComponent<CinemachineCamera>();
            camera.Follow = followPoint; camera.LookAt = lookPoint;
            camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            camera.PreviousStateIsValid = false;
            var follow = child.AddComponent<CinemachineFollow>();
            child.AddComponent<CinemachineHardLookAt>();
            var obstacles = child.AddComponent<CinemachineDeoccluder>();
            obstacles.CollideAgainst = mask;
            obstacles.MinimumDistanceFromTarget = 1f;
            obstacles.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
            {
                Enabled = true, CameraRadius = 0.35f, MaximumEffort = 3,
                Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraDistance,
                MinimumOcclusionTime = 0, SmoothingTime = 0.25f, Damping = 0.6f, DampingWhenOccluded = 0
            };
            obstacles.ShotQualityEvaluation = new CinemachineDeoccluder.QualityEvaluation { Enabled = false };
            var quality = child.AddComponent<FoliageShotQuality>();
            quality.Visibility = visibility;
            candidates[i] = new Candidate(camera, follow, quality);
        }
        owner.SetActive(true);
    }

    private bool Fail(string reason)
    {
        lastSucceeded = false; LastFailure = reason;
        Diagnostics = "Procedural camera: " + reason;
        return false;
    }

    internal void Reset()
    {
        selection.Reset(); visibility.Reset(); Array.Clear(scores, 0, Count); Array.Clear(eligible, 0, Count);
        if (owner)
            foreach (Candidate candidate in candidates)
                if (candidate != null && candidate.Camera) candidate.Camera.PreviousStateIsValid = false;
        actor = null; subjectId = null; world = null; revision = -1;
        velocity = lastLead = Vector3.zero; hasPose = lastSucceeded = false; solvedAt = lastPoseAt = -1; solvedFrame = -1; evaluateAt = 0;
        LastFailure = "Awaiting player camera"; Diagnostics = "Procedural camera: awaiting player";
    }

    public void Dispose()
    {
        Reset();
        if (owner) { owner.SetActive(false); UnityEngine.Object.Destroy(owner); }
        owner = null; followPoint = lookPoint = null;
        Array.Clear(candidates, 0, Count);
    }

    private static bool Finite(Quaternion value) => TargetRoster.Finite(new Vector3(value.x, value.y, value.z)) &&
        !float.IsNaN(value.w) && !float.IsInfinity(value.w);
}
