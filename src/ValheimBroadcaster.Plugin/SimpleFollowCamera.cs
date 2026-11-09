using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Minimal single-camera follow baseline: one persistent native Cinemachine rig
// whose Follow/LookAt is the covered player's transform directly. Orbital
// follow with native behind-the-subject recentering, a tight rotation composer
// and a deoccluder are the ONLY motion shaping. There are no candidates, no
// policies, no validation gates, no holds and no transitions. If a situation
// is not handled the camera simply keeps following the transform; the worst
// outcome by design is an ugly shot, never a stuck camera.
internal sealed class SimpleFollowCamera : IDisposable
{
    // A same-subject jump beyond this is a teleport/respawn: native damping
    // state is warped with the subject instead of sweeping across the world.
    private const float WarpDistanceMetres = 50f;
    // Near-level elevation so the camera rides just above the shoulder line
    // of the orbit centre at the configured radius.
    private const float ElevationDegrees = 5f;
    // The aim point sits at this fraction of character height above the root;
    // aiming at the root transform itself reads too low.
    private const float AimHeightFraction = 0.8f;
    private GameObject? owner;
    private CinemachineCamera? camera;
    private CinemachineOrbitalFollow? orbit;
    private CinemachineRotationComposer? aim;
    private CinemachineDeoccluder? obstacles;
    private Transform? lookPoint;
    private Character? subject;
    private Vector3 lastSubjectPosition;
    internal float ObstacleDisplacement => obstacles && camera ? obstacles!.GetCameraDisplacementDistance(camera!) : 0;
    // Layer 1: occlusion recovery. The policy owns all thresholds and the
    // state machine; this adapter only measures solid occlusion, probes
    // candidate azimuths on request and slews the orbit while steering.
    private const float SteerMaxDegreesPerSecond = 90f;
    private const double ProbeIntervalSeconds = 0.2;
    private static readonly float[] ProbeOffsets = { -145f, -110f, -70f, -35f, 35f, 70f, 110f, 145f };
    private readonly OcclusionRecoveryPolicy recovery = new OcclusionRecoveryPolicy();
    private readonly List<OcclusionRecoveryCandidate> candidates = new List<OcclusionRecoveryCandidate>(8);
    private readonly RaycastHit[] occlusionHits = new RaycastHit[16];
    private readonly Collider[] probeOverlaps = new Collider[8];
    private int solidMask;
    private Vector3 presentedPosition;
    private bool hasPresented;
    private float lastOcclusion;
    private bool steering;
    private float steerTarget;
    private double probeReadyAt;
    private float diagnosticsAt;
    // Combat two-shot framing: the pure policy owns the Solo<->Combat state and
    // the thirds/orbit geometry; this adapter senses hostiles, slews the orbit
    // toward the solved azimuth (unless Layer 1 recovery is steering) and eases
    // the composer's screen X between solo and the opposite thirds.
    private const float CombatConeDot = 0.17364818f; // cos(80 deg): the player forward cone.
    private const float SoloScreenX = -0.08f;
    private const float SoloScreenY = 0.05f;          // Unchanged headroom; combat only moves X.
    private const float ThirdsScreenX = 1f / 6f;      // +/-1/6: the enemy's opposite thirds line.
    // Combat eases to a LONGER lens than solo for a compressed, pulled-in
    // battle look; the thirds solve fits both actors at any fov. A full-frame
    // focal length maps to a vertical fov by 2*atan(12/mm): solo defaults to
    // 50 mm (26.9915 deg), combat to 75 mm (18.1805 deg, tighter).
    private const float CombatLensMm = 75f;           // Full-frame 75 mm: 2*atan(12/75) = 18.1805 deg.
    private static float LensFov(float mm) => 2f * Mathf.Atan(12f / Mathf.Max(1f, mm)) * Mathf.Rad2Deg;
    private readonly CombatFramingPolicy combat = new CombatFramingPolicy();
    private readonly List<CombatCandidate> combatCandidates = new List<CombatCandidate>(16);
    private readonly Dictionary<int, string> combatNames = new Dictionary<int, string>(16);
    // One eased 0..1 blend drives BOTH the thirds screen-X move and the lens
    // change, so the enemy slide and the 75 mm tighten share the same weighted
    // feel. combatFov holds the blended lens so the TrySolve lens assignment and
    // the thirds-separation solve read the identical value (no snaps, no
    // read-back from camera.Lens between the two).
    private float combatBlend;
    private float combatBlendVelocity;
    private float combatFov;
    private int combatSide;
    private int combatTargetId;
    private float combatDistance;
    internal string LastFailure { get; private set; } = "Awaiting player";
    internal string Diagnostics { get; private set; } = "Simple follow: awaiting player";

    // Slightly center-left with a little headroom. Cinemachine screen guides
    // are Y-down, so positive Y places the aim point below frame centre,
    // leaving room above the character's head.
    private static readonly Vector2 ScreenPosition = new Vector2(-0.08f, 0.05f);

    internal bool TrySolve(Character actor, BroadcastSettings settings, Camera output, float dt,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        // The failure-path default is the simple camera's own solo lens fov (the
        // 50 mm default), not settings.FieldOfView (the dormant directed knob).
        position = default; rotation = Quaternion.identity;
        fov = LensFov(Mathf.Clamp(settings.FollowLensMm.Value, 12f, 135f));
        if (!actor) return Fail("Camera subject unavailable");
        EnsureCreated();
        float radius = Mathf.Max(1f, settings.SimpleFollowDistance.Value);
        float aimHeight = Mathf.Clamp(actor.GetHeight() * AimHeightFraction, 0.5f, 14f) * Mathf.Max(0.1f, actor.transform.lossyScale.y);
        Vector3 subjectPosition = actor.transform.position;
        if (!TargetRoster.Finite(subjectPosition)) return Fail("Camera subject position unavailable");
        bool rebind = subject != actor || !camera!.Follow;
        if (rebind) Bind(actor, radius, aimHeight, subjectPosition);
        else if ((subjectPosition - lastSubjectPosition).sqrMagnitude > WarpDistanceMetres * WarpDistanceMetres)
        {
            camera!.OnTargetObjectWarped(actor.transform, subjectPosition - lastSubjectPosition);
            ResetRecovery(); // A teleported subject invalidates every stale occlusion observation.
        }
        lastSubjectPosition = subjectPosition;
        lookPoint!.position = subjectPosition + Vector3.up * aimHeight;
        orbit!.Radius = radius;
        orbit.TargetOffset = new Vector3(0f, aimHeight, 0f);
        if (output) camera!.Lens.PullInheritedPropertiesFromCamera(output);
        camera!.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        // Solo lens fov from the full-frame focal length (50 mm default =
        // 26.9915 deg). The lens clamp [12,135] mm already bounds fov to roughly
        // [10.1, 90] deg; the fov guard below is only a non-finite backstop.
        float soloFov = Mathf.Clamp(LensFov(Mathf.Clamp(settings.FollowLensMm.Value, 12f, 135f)), 10f, 100f);
        // The lens reads the blended combat fov (previous frame's blend). It is
        // seeded to soloFov on create/reset and whenever combat framing is off,
        // so outside combat this is exactly soloFov and eases to 75 mm in combat.
        if (combatFov <= 0f) combatFov = soloFov;
        camera.Lens.FieldOfView = combatFov;
        camera.Lens.NearClipPlane = Mathf.Max(0.01f, output ? output.nearClipPlane : 0.3f);
        UpdateOcclusionRecovery(actor, subjectPosition, aimHeight, radius, dt);
        UpdateCombatFraming(actor, settings, subjectPosition, radius, soloFov, dt);
        // ManualCameraUpdate owns exact elapsed time and native skipped-frame compensation.
        ManualCameraUpdate.Update(camera, Vector3.up, dt);
        CameraState state = camera.State;
        position = state.GetFinalPosition();
        rotation = state.GetFinalOrientation();
        fov = state.Lens.FieldOfView;
        if (!TargetRoster.Finite(position) || float.IsNaN(fov) || float.IsInfinity(fov))
        {
            subject = null; // Reseed from scratch on the next frame.
            return Fail("Native solve produced a non-finite pose");
        }
        presentedPosition = position; hasPresented = true;
        float now = Time.unscaledTime;
        if (now >= diagnosticsAt)
        {
            diagnosticsAt = now + 0.25f;
            Diagnostics = $"Simple follow: Cinemachine 3.1.7; distance {radius:F1}m (actual {Vector3.Distance(position, subjectPosition):F1}m); " +
                $"screen ({ScreenPosition.x:F2}, {ScreenPosition.y:F2}); recentering behind subject; fov {fov:F0}; " +
                $"occlusion {lastOcclusion * 100f:F0}%" + (steering ? $"; arc recovery → {steerTarget:F0}°" : "") +
                (combatTargetId != 0 && combatNames.TryGetValue(combatTargetId, out string? name)
                    ? $"; combat: {name} {combatDistance:F1}m, enemy {(combatSide >= 0 ? 'R' : 'L')} thirds" : "");
        }
        LastFailure = "none";
        return true;
    }

    // Layer 1: the ONLY authorized vantage-change trigger is sustained severe
    // solid occlusion of the player. The pure policy decides; when it steers,
    // the orbit's horizontal axis slews toward the verified-clear azimuth
    // (writing the axis cancels native recentering for that frame). When the
    // policy releases, this simply stops writing and the native behind-the-
    // player recentering resumes from the same axis value - the orbit solve
    // keeps all motion continuous by construction. No cuts, ever.
    private void UpdateOcclusionRecovery(Character actor, Vector3 subjectPosition, float aimHeight, float radius, float dt)
    {
        if (solidMask == 0) solidMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        float height = Mathf.Clamp(actor.GetHeight(), 0.5f, 16f) * Mathf.Max(0.1f, actor.transform.lossyScale.y);
        lastOcclusion = hasPresented ? BodyOcclusion(presentedPosition, subjectPosition, height) : 0f;
        double now = Time.unscaledTimeAsDouble;
        OcclusionRecoveryDecision decision = recovery.Observe(now, lastOcclusion,
            ObstacleDisplacement > 0.01f, orbit!.HorizontalAxis.Value, candidates);
        if (!decision.NeedsCandidates) { candidates.Clear(); probeReadyAt = 0; }
        else if (now >= probeReadyAt)
        {
            probeReadyAt = now + ProbeIntervalSeconds;
            ProbeCandidates(subjectPosition, aimHeight, radius, height);
        }
        steering = decision.Phase == OcclusionRecoveryPhase.Steering;
        if (!steering) return;
        steerTarget = decision.TargetAzimuthDegrees;
        float value = orbit.HorizontalAxis.Value;
        float remaining = Mathf.DeltaAngle(value, steerTarget);
        // Eased, bounded slew: brisk across a big arc, settling near the target.
        float speed = Mathf.Clamp(Mathf.Abs(remaining) * 2.5f, 20f, SteerMaxDegreesPerSecond);
        float step = Mathf.Min(speed * Mathf.Max(dt, 0f), Mathf.Abs(remaining));
        if (step <= 0f) return;
        float next = Mathf.MoveTowardsAngle(value, steerTarget, step);
        if (next > 180f) next -= 360f; else if (next < -180f) next += 360f;
        orbit.HorizontalAxis.Value = next;
    }

    // Layer 1.5: Solo <-> Combat player states. The pure policy owns the state
    // machine, target selection and the thirds/orbit geometry; this adapter
    // senses hostiles in the player's forward cone, slews the orbit toward the
    // solved azimuth (yielding to Layer 1 recovery, which still Observes every
    // frame) and eases the composer's screen X between solo and the enemy's
    // opposite thirds. Writing HorizontalAxis cancels native recentering that
    // frame, exactly like Layer 1; on release we stop writing and recentering
    // resumes. No cuts, ever.
    private void UpdateCombatFraming(Character actor, BroadcastSettings settings, Vector3 subjectPosition,
        float radius, float soloFov, float dt)
    {
        if (!settings.CombatFraming.Value) { combatFov = soloFov; if (combatTargetId != 0) ResetCombat(); return; }
        // The thirds separation must be solved from the SAME blended fov the lens
        // carries this frame, never a read-back - as the lens tightens to 75 mm
        // the thirds lines narrow in lockstep so the enemy stays pinned on its
        // line through the whole move.
        float combatFovTarget = LensFov(CombatLensMm);
        float blendedFov = Mathf.Lerp(soloFov, combatFovTarget, combatBlend);
        float separation = CombatFramingPolicy.ThirdsSeparationDegrees(
            Mathf.Clamp(blendedFov, 10f, 100f), Mathf.Max(0.1f, camera!.Lens.Aspect));
        SenseHostiles(actor, settings, subjectPosition);
        double now = Time.unscaledTimeAsDouble;
        float cameraAzimuth = orbit!.HorizontalAxis.Value;
        CombatFramingDecision decision = combat.Observe(now, cameraAzimuth, separation, radius, combatCandidates);
        combatTargetId = decision.TargetId;
        combatSide = decision.PlayerScreenSide;
        // Bound the name cache: only ever cleared out of combat, so no live
        // target's label is lost and no diagnostic string churns.
        if (combatTargetId == 0 && combatNames.Count > 64) combatNames.Clear();
        if (combatTargetId != 0)
            for (int i = 0; i < combatCandidates.Count; i++)
                if (combatCandidates[i].Id == combatTargetId) { combatDistance = combatCandidates[i].Distance; break; }
        // Occlusion recovery wins the orbit while it steers; the combat policy
        // still ran above so its state keeps flowing (spec priority rule).
        if (!steering && decision.Phase == CombatFramingPhase.Combat)
        {
            float remaining = Mathf.DeltaAngle(cameraAzimuth, decision.TargetAzimuthDegrees);
            // Gentler than Layer 1 recovery (20-90): slow start, brisk middle,
            // soft settle - the user's "nice and smooth and weighted" feel.
            float speed = Mathf.Clamp(Mathf.Abs(remaining) * 1.5f, 8f, 45f);
            float step = Mathf.Min(speed * Mathf.Max(dt, 0f), Mathf.Abs(remaining));
            if (step > 0f)
            {
                float next = Mathf.MoveTowardsAngle(cameraAzimuth, decision.TargetAzimuthDegrees, step);
                if (next > 180f) next -= 360f; else if (next < -180f) next += 360f;
                orbit.HorizontalAxis.Value = next;
            }
        }
        // One critically-damped ~0.8s blend toward the policy weight drives the
        // whole engage/release feel. The thirds screen-X slide (player opposite
        // the enemy) and the 35 mm lens push are both lerped off this single
        // blend, so they move together and collapse back to solo in lockstep as
        // the policy releases. No snaps.
        combatBlend = Mathf.SmoothDamp(combatBlend, decision.Weight, ref combatBlendVelocity, 0.8f, Mathf.Infinity, Mathf.Max(dt, 0f));
        combatFov = Mathf.Lerp(soloFov, combatFovTarget, combatBlend);
        float sideSign = combatSide >= 0 ? 1f : -1f;
        float engagedX = -sideSign * ThirdsScreenX; // Enemy on side => player opposite.
        float combatScreenX = Mathf.Lerp(SoloScreenX, engagedX, combatBlend);
        ScreenComposerSettings composition = aim!.Composition; // Struct: mutate a copy and reassign.
        if (composition.ScreenPosition.x != combatScreenX)
        {
            composition.ScreenPosition = new Vector2(combatScreenX, SoloScreenY);
            aim.Composition = composition;
        }
    }

    // Build the hostile candidate list with no per-frame allocations: a pooled
    // list of value structs and a name cache populated only on first sight.
    // Hostile = loaded MonsterAI the game treats as the player's enemy, alive,
    // not the player, within CombatRange and the forward cone. Mirrors the
    // proven PlayerSceneContext detection (ai is MonsterAI && BaseAI.IsEnemy).
    private void SenseHostiles(Character actor, BroadcastSettings settings, Vector3 subjectPosition)
    {
        combatCandidates.Clear();
        var player = actor as Player;
        if (!player) return;
        Vector3 forward = actor.transform.forward; forward.y = 0f;
        if (!TargetRoster.Finite(forward) || forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();
        float range = Mathf.Max(5f, settings.CombatRange.Value);
        float rangeSq = range * range;
        foreach (Character enemy in Character.GetAllCharacters())
        {
            if (!enemy || enemy == actor || enemy is Player || enemy.IsDead()) continue;
            if (!(enemy.GetBaseAI() is MonsterAI) || !BaseAI.IsEnemy(player, enemy)) continue;
            Vector3 delta = enemy.transform.position - subjectPosition; delta.y = 0f;
            float distSq = delta.sqrMagnitude;
            if (distSq > rangeSq || distSq < 0.0001f) continue;
            float distance = Mathf.Sqrt(distSq);
            Vector3 dir = delta / distance;
            bool inFront = Vector3.Dot(forward, dir) >= CombatConeDot;
            float bearing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // World azimuth (+Z = 0), matches orbit axis.
            int id = enemy.GetInstanceID();
            if (!combatNames.ContainsKey(id)) combatNames[id] = CombatName(enemy);
            combatCandidates.Add(new CombatCandidate(id, bearing, distance, inFront, true));
        }
    }

    // Raw name token, de-prefixed when trivial ($enemy_/$). Cached per id so this
    // string work happens once per enemy, never per frame.
    private static string CombatName(Character enemy)
    {
        string raw = enemy.m_name ?? "enemy";
        if (raw.StartsWith("$enemy_")) return raw.Substring(7);
        if (raw.Length > 0 && raw[0] == '$') return raw.Substring(1);
        return raw;
    }

    private void ResetCombat()
    {
        combat.Reset(); combatCandidates.Clear(); combatNames.Clear();
        combatBlend = 0f; combatBlendVelocity = 0f; combatFov = 0f; // 0 reseeds to solo fov next solve.
        combatSide = 0; combatTargetId = 0; combatDistance = 0f;
        if (aim != null)
        {
            ScreenComposerSettings composition = aim.Composition;
            composition.ScreenPosition = new Vector2(SoloScreenX, SoloScreenY);
            aim.Composition = composition;
        }
    }

    // Candidate vantages on the current orbit: ideal orbit points around the
    // player at the configured radius and elevation. A probe embedded in
    // solid geometry sees nothing; otherwise its clearance is the unblocked
    // fraction of the same five body rays used for the live measurement.
    private void ProbeCandidates(Vector3 subjectPosition, float aimHeight, float radius, float height)
    {
        candidates.Clear();
        Vector3 centre = subjectPosition + Vector3.up * aimHeight;
        float current = orbit!.HorizontalAxis.Value;
        for (int i = 0; i < ProbeOffsets.Length; i++)
        {
            float azimuth = current + ProbeOffsets[i];
            Vector3 probe = centre + Quaternion.Euler(ElevationDegrees, azimuth, 0f) * new Vector3(0f, 0f, -radius);
            float clear = PointInsideSolid(probe) ? 0f : 1f - BodyOcclusion(probe, subjectPosition, height);
            if (azimuth > 180f) azimuth -= 360f; else if (azimuth < -180f) azimuth += 360f;
            candidates.Add(new OcclusionRecoveryCandidate(azimuth, clear));
        }
    }

    // Blocked fraction of five sample points spread over the character
    // capsule (shin, knees, chest, head, above the head). Solid geometry
    // only: triggers and characters never occlude, and foliage is tracked by
    // bounds elsewhere and deliberately absent from the solid mask - leaves
    // can NEVER trigger recovery.
    private float BodyOcclusion(Vector3 from, Vector3 subjectPosition, float height)
    {
        int blocked = 0;
        if (RayBlocked(from, subjectPosition + Vector3.up * 0.3f)) blocked++;
        if (RayBlocked(from, subjectPosition + Vector3.up * (height * 0.25f))) blocked++;
        if (RayBlocked(from, subjectPosition + Vector3.up * (height * 0.55f))) blocked++;
        if (RayBlocked(from, subjectPosition + Vector3.up * (height * 0.92f))) blocked++;
        if (RayBlocked(from, subjectPosition + Vector3.up * (height + 0.2f))) blocked++;
        return blocked / 5f;
    }

    private bool RayBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (!TargetRoster.Finite(delta)) return false;
        float distance = delta.magnitude;
        if (distance <= 0.05f) return false;
        int count = Physics.RaycastNonAlloc(from, delta / distance, occlusionHits,
            Mathf.Max(0f, distance - 0.05f), solidMask, QueryTriggerInteraction.Ignore);
        if (count == occlusionHits.Length) return true; // A saturated query is not proof of clearance.
        for (int i = 0; i < count; i++) if (Solid(occlusionHits[i].collider)) return true;
        return false;
    }

    private bool PointInsideSolid(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, 0.05f, probeOverlaps, solidMask, QueryTriggerInteraction.Ignore);
        if (count == probeOverlaps.Length) return true;
        for (int i = 0; i < count; i++) if (Solid(probeOverlaps[i])) return true;
        return false;
    }

    private static bool Solid(Collider collider) => collider && collider.enabled &&
        !collider.isTrigger && !collider.GetComponentInParent<Character>();

    private void ResetRecovery()
    {
        recovery.Reset(); candidates.Clear();
        hasPresented = steering = false; lastOcclusion = 0; probeReadyAt = 0;
        ResetCombat(); // A fresh vantage, teleport or rebind voids stale combat state too.
    }

    // A fresh binding seeds the camera directly behind the subject's facing at
    // the configured radius, then lets the native components own all motion.
    private void Bind(Character actor, float radius, float aimHeight, Vector3 subjectPosition)
    {
        subject = actor;
        ResetRecovery(); // A fresh binding is a fresh vantage; stale occlusion evidence is void.
        camera!.Follow = actor.transform;
        camera.LookAt = lookPoint;
        Vector3 forward = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 centre = subjectPosition + Vector3.up * aimHeight;
        Vector3 seed = centre - forward * (radius * Mathf.Cos(ElevationDegrees * Mathf.Deg2Rad))
            + Vector3.up * (radius * Mathf.Sin(ElevationDegrees * Mathf.Deg2Rad));
        camera.ForceCameraPosition(seed, Quaternion.LookRotation(centre - seed, Vector3.up));
        // ForceCameraPosition marks the camera valid upstream; the first solve
        // must still initialize component caches from this new binding.
        camera.PreviousStateIsValid = false;
    }

    private void EnsureCreated()
    {
        if (owner) return;
        owner = new GameObject("Broadcaster simple follow camera") { hideFlags = HideFlags.HideAndDontSave };
        owner.SetActive(false);
        lookPoint = new GameObject("Simple follow aim").transform;
        lookPoint.SetParent(owner.transform, false);
        var child = new GameObject("Simple follow shot");
        child.transform.SetParent(owner.transform, false);
        camera = child.AddComponent<CinemachineCamera>();
        camera.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
        camera.PreviousStateIsValid = false;
        orbit = child.AddComponent<CinemachineOrbitalFollow>();
        orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        orbit.HorizontalAxis.Range = new Vector2(-180, 180); orbit.HorizontalAxis.Wrap = true;
        orbit.VerticalAxis.Range = new Vector2(-30, 60); orbit.VerticalAxis.Wrap = false;
        orbit.VerticalAxis.Value = orbit.VerticalAxis.Center = ElevationDegrees;
        orbit.RadialAxis.Value = 1;
        // Native stay-behind: recentering reads the follow target's forward
        // vector (the player's facing) and settles the camera behind it.
        orbit.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
        orbit.HorizontalAxis.Recentering = new InputAxis.RecenteringSettings { Enabled = true, Wait = 0f, Time = 2f };
        orbit.VerticalAxis.Recentering.Enabled = false;
        orbit.RadialAxis.Recentering.Enabled = false;
        orbit.TrackerSettings = new TrackerSettings
        {
            BindingMode = BindingMode.WorldSpace,
            // Boom (z) tightest: z lag reads as the subject outrunning the
            // frame. Vertical slightly softer so terrain bumps stay smooth.
            PositionDamping = new Vector3(0.4f, 0.5f, 0.2f),
            AngularDampingMode = AngularDampingMode.Quaternion
        };
        aim = child.AddComponent<CinemachineRotationComposer>();
        aim.CenterOnActivate = true;
        aim.Damping = new Vector2(0.15f, 0.15f);
        aim.Lookahead = new LookaheadSettings { Enabled = true, Time = 0.25f, Smoothing = 10f, IgnoreY = true };
        var composition = ScreenComposerSettings.Default;
        composition.ScreenPosition = ScreenPosition;
        composition.DeadZone.Enabled = true;
        composition.DeadZone.Size = new Vector2(0.02f, 0.02f);
        composition.HardLimits.Enabled = true;
        composition.HardLimits.Size = new Vector2(0.6f, 0.55f);
        composition.HardLimits.Offset = Vector2.zero;
        composition.Validate();
        aim.Composition = composition;
        obstacles = child.AddComponent<CinemachineDeoccluder>();
        obstacles.CollideAgainst = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        obstacles.MinimumDistanceFromTarget = 1f;
        obstacles.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
        {
            Enabled = true, CameraRadius = 0.3f, MaximumEffort = 3,
            Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraDistance,
            MinimumOcclusionTime = 0.15f, SmoothingTime = 0.3f, Damping = 0.5f, DampingWhenOccluded = 0.15f
        };
        obstacles.ShotQualityEvaluation = new CinemachineDeoccluder.QualityEvaluation { Enabled = false };
        owner.SetActive(true);
    }

    private bool Fail(string reason)
    {
        LastFailure = reason;
        Diagnostics = "Simple follow: " + reason;
        return false;
    }

    // Binding loss: drop the subject so the next player re-seeds cleanly. The
    // native components persist; there is nothing stateful worth keeping.
    internal void Reset()
    {
        subject = null;
        ResetRecovery();
        if (camera) { camera!.Follow = null; camera.PreviousStateIsValid = false; }
        LastFailure = "Awaiting player"; Diagnostics = "Simple follow: awaiting player"; diagnosticsAt = 0;
    }

    public void Dispose()
    {
        Reset();
        if (owner) UnityEngine.Object.Destroy(owner);
        owner = null; camera = null; orbit = null; aim = null; obstacles = null; lookPoint = null;
    }
}
