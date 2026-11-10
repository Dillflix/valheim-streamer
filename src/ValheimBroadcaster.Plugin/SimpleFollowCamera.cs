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
    private const float SoloScreenX = -(1f / 6f);     // Solo LEFT THIRDS (operator order): the body fills frame-left.
    private const float SoloScreenY = 0.05f;          // Legacy seed/fallback aim-Y; the live aim is solved by FramingFill.
    private const float ThirdsScreenX = 1f / 6f;      // +/-1/6: the enemy's opposite thirds line.
    // Combat eases to a LONGER lens than solo for a compressed, pulled-in
    // battle look; the thirds solve fits both actors at any fov. A full-frame
    // focal length maps to a vertical fov by 2*atan(12/mm): solo defaults to
    // 50 mm (26.9915 deg), combat to 75 mm (18.1805 deg, tighter).
    // Combat rides a long 75 mm two-shot; when the enemy presses in close the
    // lens widens subtly toward 65 mm so the pair still breathes (operator:
    // "keep it subtle"). Smoothstepped over separation 2-6 m and eased 0.5 s so
    // measurement noise never pumps the lens.
    private const float CombatLensFarMm = 75f;        // Full-frame 75 mm: 2*atan(12/75) = 18.1805 deg.
    private const float CombatLensNearMm = 65f;       // Close-quarters floor: 2*atan(12/65) = 20.9524 deg.
    private const float CloseEnemyNearMetres = 2f;    // At/below this separation the lens rides 65 mm.
    private const float CloseEnemyFarMetres = 6f;     // At/above this separation the lens rides 75 mm.
    private float combatLensMm = CombatLensFarMm;
    private float combatLensVelocity;
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
    // Dolly group framing (GroupFraming DollyOnly semantics on the player-anchored
    // rig): the fixed lens never bends; a fit is absorbed by the camera distance.
    // dollyRadius is the live, eased orbit radius presented each frame; it replaces
    // the raw SimpleFollowDistance assignment so player feet (rule 1) and an
    // engaged enemy (rule 2) stay framed at a fixed focal length. The composer
    // keeps pinning the PLAYER, so native group framing (which needs a group
    // LookAt) is deliberately NOT used - rule-1 hard limits must stay on the player.
    // FramingSize-style fill: the fixed lens never bends; the camera distance AND the
    // aim-pin's screen Y are solved together each frame from the player's live body
    // geometry so the body spans FramingSizeConst of the frame height (fills, not
    // merely fits). TopGapShare > 0.5 puts more of the leftover gap above the head
    // (headroom) than below the feet.
    private const float FramingSizeConst = 0.9f;       // Body spans ~90% of frame height (operator: closer, larger character).
    private const float TopGapShare = 0.6f;            // 60% of the leftover gap above the head, 40% below the feet.
    // Screen-space composition layer (pure policy): solo side follows lateral motion
    // (lead room), combat shares a small lead-room bias, and the group vertical
    // balance is folded into the fill via CombineGroupSpans. This adapter measures
    // the player's lateral velocity in camera space and eases the solo X slide.
    private readonly ScreenCompositionPolicy composition = new ScreenCompositionPolicy();
    private const float SoloXEaseSeconds = 1.0f;       // Slow weighted slide through centre when the solo side flips.
    private const float LeadEaseSeconds = 0.8f;        // Eased combat lead-room bias.
    private const float VelocitySmoothSeconds = 0.3f;  // Low-pass on the lateral screen velocity.
    private float soloScreenX = SoloScreenX;           // Live eased solo thirds X (seeded LEFT).
    private float soloScreenXVelocity;
    private float leadOffsetX;                          // Live eased combat lead-room bias.
    private float leadOffsetVelocity;
    private float lateralScreenVelocity;               // Smoothed lateral velocity in camera space (m/s).
    private Vector3 lastVelocitySamplePosition;        // Previous frame player position for the velocity delta.
    private bool hasVelocitySample;
    private float soloReasonRight;                      // Diagnostic latch: >0 when the solo side is RIGHT.
    private const float AimEaseSeconds = 0.8f;         // SmoothDamp settle for the eased aim-Y.
    private float aimScreenY = SoloScreenY;            // Live eased aim-pin Y-down fraction (seeded to the legacy pin).
    private float aimScreenYVelocity;
    private const float PlayerFeetMargin = 0.25f;      // Metres of air below the player's feet.
    private const float PlayerHeadMargin = 0.35f;      // Metres of air above the player's head.
    private const float EnemyFeetMargin = 0.15f;       // Tighter enemy margins: it is the secondary subject.
    private const float EnemyHeadMargin = 0.25f;
    private const float GroupFullMetres = 10f;         // Sparse gate: full weight within this planar separation.
    private const float GroupZeroMetres = 16f;         // Beyond this the enemy never affects framing (still fights/thirds).
    private const float DollyMaxMetres = 18f;          // Absolute cap on the presented orbit radius.
    private const float DollyEaseSeconds = 0.7f;       // SmoothDamp settle (GroupFraming Damping concept), within the 1-2s ease allowance.
    private float dollyRadius;                          // Live eased orbit radius.
    private float dollyVelocity;
    private float desiredDollyRadius;                   // Target radius this frame (player + weighted enemy need).
    private bool dollyHeadBand;                         // The head-band crop is active this frame (oversized enemy).
    // Tight-space shot ladder: in caves/buildings the camera cannot reach the
    // full-fill distance, so it moves CLOSER while keeping the same lens and the
    // thirds composition - the shot degrades full -> medium close -> close-up as
    // the space shrinks (head always kept, feet crop up). Each frame a sphere-cast
    // probes the physical boom room along the ideal boom direction; a hysteresis
    // field shrinks FAST (doorways cramp quickly) and recovers SLOW (never pumps).
    private const float TightProbeRadius = 0.35f;       // Sphere-cast radius: matches the deoccluder margin ethos.
    private const float TightProbeMargin = 0.15f;       // Back off the solid hit by this before using it as the cap.
    private const float TightProbeExtra = 0.5f;         // Probe this far past the desired fill distance (headroom to detect "clear").
    private const float TightFloor = 1.3f;              // Deoccluder min distance (1) + margin: never closer than this.
    private const float TightTopGapMax = 0.13f;         // Headroom above the head at the close-up floor (frame fraction).
    private const float TightShrinkSeconds = 0.25f;     // Fast collapse when the room shrinks (target < current).
    private const float TightRecoverSeconds = 1.5f;     // Slow re-open when the room grows (target > current), no pumping.
    private const float TightBandFull = 0.85f;          // Visible-body fraction >= this reads as a full shot.
    private const float TightBandMedium = 0.45f;        // >= this (and < full) reads as a medium close-up.
    private readonly RaycastHit[] boomHits = new RaycastHit[8]; // Non-alloc sphere-cast buffer for the boom probe.
    private float availableDistance;                    // Hysteresis-smoothed physical boom room (metres).
    private float availableVelocity;
    private bool tightActive;                           // Tight mode engaged this frame (available < fill).
    private float tightFrameFraction;                   // Visible body fraction this frame (diagnostics bands).
    internal string LastFailure { get; private set; } = "Awaiting player";
    internal string Diagnostics { get; private set; } = "Simple follow: awaiting player";

    // Slightly center-left with a little headroom. Cinemachine screen guides
    // are Y-down, so positive Y places the aim point below frame centre,
    // leaving room above the character's head.
    private static readonly Vector2 ScreenPosition = new Vector2(SoloScreenX, SoloScreenY);

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
        // Seed the live dolly radius to the configured distance before the first
        // ease; a rebind/reset/warp zeroes it so it re-seeds cleanly here.
        if (dollyRadius <= 0f) dollyRadius = radius;
        lookPoint!.position = subjectPosition + Vector3.up * aimHeight;
        orbit!.Radius = dollyRadius;
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
        UpdateLateralVelocity(subjectPosition, dt);
        UpdateOcclusionRecovery(actor, subjectPosition, aimHeight, dollyRadius, dt);
        UpdateCombatFraming(actor, settings, subjectPosition, dollyRadius, soloFov, aimHeight, dt);
        // Dolly group framing: ease the live orbit radius toward the fit the fixed
        // lens cannot absorb, then present it. UpdateCombatFraming computed the
        // desired radius into desiredDollyRadius using this frame's blended lens and
        // the engaged enemy (if any); here we smooth-settle and apply it.
        dollyRadius = Mathf.SmoothDamp(dollyRadius, desiredDollyRadius, ref dollyVelocity,
            DollyEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
        dollyRadius = Mathf.Clamp(dollyRadius, 1f, DollyMaxMetres);
        orbit.Radius = dollyRadius;
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
            Diagnostics = $"Simple follow: Cinemachine 3.1.7; distance {dollyRadius:F1}m (actual {Vector3.Distance(position, subjectPosition):F1}m); " +
                $"screen ({aim!.Composition.ScreenPosition.x:F2}, {aimScreenY:F2}); recentering behind subject; fov {fov:F0}; " +
                $"occlusion {lastOcclusion * 100f:F0}%" + (steering ? $"; arc recovery → {steerTarget:F0}°" : "") +
                (combatTargetId != 0 && combatNames.TryGetValue(combatTargetId, out string? name)
                    ? $"; combat: {name} {combatDistance:F1}m, enemy {(combatSide >= 0 ? 'R' : 'L')} thirds" : "") +
                $"; fill d={dollyRadius:F1}m aimY={aimScreenY:F2}" + (dollyHeadBand ? " (head)" : "") +
                (tightActive
                    ? $"; tight d={availableDistance:F1}m frame={(tightFrameFraction >= TightBandFull ? "full" : tightFrameFraction >= TightBandMedium ? "medium" : "close-up")} {tightFrameFraction * 100f:F0}%"
                    : "") +
                (soloReasonRight > 0f ? "; side R" : "") +
                (Mathf.Abs(leadOffsetX) > 0.01f ? $"; lead {(leadOffsetX >= 0f ? "+" : "")}{leadOffsetX:F2}" : "");
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
        float radius, float soloFov, float aimHeight, float dt)
    {
        double compNow = Time.unscaledTimeAsDouble;
        if (!settings.CombatFraming.Value)
        {
            combatFov = soloFov;
            if (combatTargetId != 0) ResetCombat();
            // Solo composition: the player trails its lateral motion onto a thirds
            // line (lead room ahead). The policy picks the side; the eased soloScreenX
            // slides slowly through centre on a flip (never a snap). No combat lead
            // room and no group spans in solo (no enemy).
            ScreenCompositionDecision comp = composition.Observe(compNow, lateralScreenVelocity);
            soloReasonRight = comp.Side == SoloCompositionSide.Right ? 1f : 0f;
            leadOffsetX = Mathf.SmoothDamp(leadOffsetX, 0f, ref leadOffsetVelocity, LeadEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
            soloScreenX = Mathf.SmoothDamp(soloScreenX, comp.PlayerScreenX, ref soloScreenXVelocity, SoloXEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
            // Solo fill: solve BOTH the distance and the aim-Y from the player's body
            // so the body fills FramingSizeConst of the frame. SimpleFollowDistance is
            // NOT a floor here (demoted to a seed) - the dolly may come closer than the
            // configured distance; that is the fill's whole point.
            float soloFill = SolvePlayerDolly(actor, soloFov, aimHeight, subjectPosition, out float soloFillAimY, out float soloBelow, out float soloAbove);
            // Tight-space: cap the radius by the physical boom room and, when cramped,
            // switch the aim to the head-pinned cropped composition (feet crop up). The
            // thirds/lead X flow is COMPLETELY unchanged (operator: maintain thirds).
            UpdateBoomRoom(subjectPosition, aimHeight, soloFill, dt);
            float soloAimTarget = ResolveTightAim(soloFov, soloFill, soloFillAimY, soloBelow, soloAbove);
            // Physical space wins: cap by the boom room. The floor is TightFloor (1.3)
            // so tight spaces can bring the camera in below the 2 m fill minimum.
            desiredDollyRadius = Mathf.Clamp(Mathf.Min(soloFill, availableDistance), TightFloor, DollyMaxMetres);
            EaseAim(soloAimTarget, soloScreenX + leadOffsetX, dt);
            return;
        }
        // The thirds separation must be solved from the SAME blended fov the lens
        // carries this frame, never a read-back - as the lens tightens to 75 mm
        // the thirds lines narrow in lockstep so the enemy stays pinned on its
        // line through the whole move.
        // Close-quarters widening: previous frame's enemy separation drives the
        // 75->65 mm target (one-frame lag is invisible under the 0.5 s ease).
        float closeness = Mathf.InverseLerp(CloseEnemyNearMetres, CloseEnemyFarMetres,
            combatTargetId != 0 ? combatDistance : CloseEnemyFarMetres);
        float lensTarget = Mathf.Lerp(CombatLensNearMm, CombatLensFarMm, Mathf.SmoothStep(0f, 1f, closeness));
        combatLensMm = Mathf.SmoothDamp(combatLensMm, lensTarget, ref combatLensVelocity, 0.5f, Mathf.Infinity, Mathf.Max(dt, 0f));
        float combatFovTarget = LensFov(combatLensMm);
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
        // Screen-space composition: observe the player's lateral motion once per
        // frame. In combat the SIDE comes from CombatFramingPolicy stickiness (the
        // enemy owns the thirds), so the solo side only governs the released/solo
        // target the blend eases back toward; the lead offset is the small shared
        // bias applied to BOTH compositions this frame.
        ScreenCompositionDecision combatComp = composition.Observe(compNow, lateralScreenVelocity);
        soloReasonRight = combatComp.Side == SoloCompositionSide.Right ? 1f : 0f;
        float combatLeadTarget = decision.Phase == CombatFramingPhase.Combat ? combatComp.LeadOffsetX : 0f;
        leadOffsetX = Mathf.SmoothDamp(leadOffsetX, combatLeadTarget, ref leadOffsetVelocity, LeadEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
        // The solo rail is the policy's trailing-lead-room side; the combat rail is
        // the player opposite the enemy. Ease the solo X slowly (flip through centre)
        // and blend toward the combat rail by the engage weight. Adding leadOffsetX
        // to the composer target pivots the WHOLE composition - the enemy's thirds
        // line shifts with it automatically (its azimuth solve is relative to the aim).
        soloScreenX = Mathf.SmoothDamp(soloScreenX, combatComp.PlayerScreenX, ref soloScreenXVelocity, SoloXEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
        float sideSign = combatSide >= 0 ? 1f : -1f;
        float engagedX = -sideSign * ThirdsScreenX + leadOffsetX; // Enemy on side => player opposite, shifted by the shared lead.
        float combatScreenX = Mathf.Lerp(soloScreenX + leadOffsetX, engagedX, combatBlend);
        // Dolly fill: the fixed (blended) lens must FILL the frame with the player's
        // body (rule 1), and fit the engaged enemy while it reads as one picture with
        // the player (rule 2, sparse-gated). The lens never bends - the fit is absorbed
        // by the orbit radius; the player fill also yields the aim-Y to ease toward.
        // In combat with a live framed enemy the fill uses GROUP spans so a low
        // creature lifts the aim / a tall enemy adds headroom (vertical balance).
        float lensFov = Mathf.Clamp(combatFov, 10f, 100f);
        float playerNeed = SolvePlayerDolly(actor, lensFov, aimHeight, subjectPosition, out float fillAimY, out float combatBelow, out float combatAbove);
        // Tight-space: probe the physical boom room this frame (behavior B.1/B.2).
        UpdateBoomRoom(subjectPosition, aimHeight, playerNeed, dt);
        // When cramped, the aim comes from the player-only cropped solve (enemy fit is
        // already best-effort; documented decision #14) - the head stays pinned, feet
        // crop up. Thirds/lead X unchanged. Otherwise the fill aim owns the shot.
        float combatAimTarget = ResolveTightAim(lensFov, playerNeed, fillAimY, combatBelow, combatAbove);
        // Ease BOTH the composer X (thirds slide) and the solved aim-Y together.
        EaseAim(combatAimTarget, combatScreenX, dt);
        float effectiveNeed = playerNeed;
        dollyHeadBand = false;
        if (combatTargetId != 0)
        {
            float enemyNeed = SolveEnemyDolly(lensFov, aimHeight, subjectPosition, radius, playerNeed);
            if (enemyNeed > 0f) effectiveNeed = Mathf.Max(playerNeed, enemyNeed);
        }
        // Physical space wins (behavior B.4): the enemy need is also capped by the
        // boom room - min() applied AFTER the max chain, before the clamp. Floor
        // TightFloor (1.3) so tight combat can pull the camera in below 2 m.
        effectiveNeed = Mathf.Min(effectiveNeed, availableDistance);
        desiredDollyRadius = Mathf.Clamp(effectiveNeed, TightFloor, DollyMaxMetres);
    }

    // Tight-space boom-room probe (behavior B.1/B.2). Sphere-cast from the player
    // aim point along the CURRENT ideal boom direction (the unit vector from the aim
    // toward the ideal orbit position at the live azimuth/elevation - the same
    // geometry Layer 1 uses) and find the nearest solid. The raw room is that hit
    // distance minus a small margin, or the desired fill distance when nothing solid
    // is within reach. A hysteresis field then shrinks FAST and recovers SLOW so a
    // doorway edge never pumps the shot. Floored at TightFloor. No per-frame alloc.
    private void UpdateBoomRoom(Vector3 subjectPosition, float aimHeight, float desiredFillDistance, float dt)
    {
        if (solidMask == 0) solidMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // Re-seed the boom room to the full fill distance after a reset/rebind/warp
        // (availableDistance zeroed there) so a fresh vantage never starts cramped.
        if (availableDistance <= 0f) { availableDistance = Mathf.Max(TightFloor, desiredFillDistance); availableVelocity = 0f; }
        Vector3 aimPoint = subjectPosition + Vector3.up * aimHeight;
        float azimuth = orbit!.HorizontalAxis.Value;
        // Ideal orbit position at the current axis/elevation; the boom direction is
        // from the aim toward that position (same Euler(elev, azimuth, 0)*(0,0,-r) rig).
        Vector3 idealOffset = Quaternion.Euler(ElevationDegrees, azimuth, 0f) * new Vector3(0f, 0f, -1f);
        Vector3 boomDir = idealOffset; // Already a unit direction (the -r is just a length).
        if (!TargetRoster.Finite(boomDir) || boomDir.sqrMagnitude < 0.0001f) { availableDistance = desiredFillDistance; availableVelocity = 0f; return; }
        boomDir.Normalize();
        float maxDist = desiredFillDistance + TightProbeExtra;
        float raw = desiredFillDistance;
        int count = Physics.SphereCastNonAlloc(aimPoint, TightProbeRadius, boomDir, boomHits,
            maxDist, solidMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool hit = false;
        for (int i = 0; i < count; i++)
        {
            if (!Solid(boomHits[i].collider)) continue;
            float hd = boomHits[i].distance;
            // A zero-distance hit means the aim point is embedded in solid; ignore it
            // so an overlap at the origin does not collapse the boom to the floor.
            if (hd <= 0.01f) continue;
            if (hd < nearest) { nearest = hd; hit = true; }
        }
        if (hit) raw = Mathf.Max(0f, nearest - TightProbeMargin);
        float target = Mathf.Max(TightFloor, raw);
        // Hysteresis: shrink fast toward a smaller room, recover slow toward a larger.
        float ease = target < availableDistance ? TightShrinkSeconds : TightRecoverSeconds;
        availableDistance = Mathf.SmoothDamp(availableDistance, target, ref availableVelocity,
            ease, Mathf.Infinity, Mathf.Max(dt, 0f));
        if (availableDistance < TightFloor) availableDistance = TightFloor;
    }

    // Project the player's world velocity onto the PRESENTED camera-right axis and
    // low-pass it, so the composition policy sees lateral screen-space motion (m/s,
    // +right/-left). The camera-right axis comes from the orbit horizontal axis: the
    // cam->player planar forward is dir(h); right = forward rotated -90 deg. No
    // per-frame allocation. Before the first presented frame or on a warp this holds
    // zero (hasVelocitySample re-seeds cleanly).
    private void UpdateLateralVelocity(Vector3 subjectPosition, float dt)
    {
        if (!hasVelocitySample || dt <= 0f)
        {
            lastVelocitySamplePosition = subjectPosition;
            hasVelocitySample = true;
            return;
        }
        Vector3 delta = subjectPosition - lastVelocitySamplePosition;
        lastVelocitySamplePosition = subjectPosition;
        delta.y = 0f;
        // A teleport/warp produces a huge delta; ignore it (ResetRecovery clears the
        // sample on warp, but guard anyway so one frame never spikes the velocity).
        if (!TargetRoster.Finite(delta) || delta.sqrMagnitude > WarpDistanceMetres * WarpDistanceMetres)
        { lateralScreenVelocity = 0f; return; }
        float axis = orbit!.HorizontalAxis.Value * Mathf.Deg2Rad;
        // cam->player planar forward = (sin h, 0, cos h); camera-right is that turned
        // -90 deg about up => (cos h, 0, -sin h). Positive dot = moving screen-right.
        Vector3 right = new Vector3(Mathf.Cos(axis), 0f, -Mathf.Sin(axis));
        float lateralMetres = Vector3.Dot(delta, right);
        float instantaneous = lateralMetres / Mathf.Max(dt, 1e-4f);
        if (!(instantaneous > float.MinValue && instantaneous < float.MaxValue)) instantaneous = 0f;
        // Exponential low-pass over ~VelocitySmoothSeconds.
        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, VelocitySmoothSeconds));
        lateralScreenVelocity += (instantaneous - lateralScreenVelocity) * a;
    }

    // Ease the live aim-pin toward the solved (aimY, screenX) and write it to the
    // composer. The aim-Y is SmoothDamp'd ~0.8s so the vertical placement never snaps;
    // the X flow (solo left-thirds <-> combat opposite-thirds) is already blended by
    // the caller and only written when it actually changes.
    private void EaseAim(float targetAimY, float screenX, float dt)
    {
        aimScreenY = Mathf.SmoothDamp(aimScreenY, Mathf.Clamp(targetAimY, -0.45f, 0.45f),
            ref aimScreenYVelocity, AimEaseSeconds, Mathf.Infinity, Mathf.Max(dt, 0f));
        if (aim == null) return;
        ScreenComposerSettings composition = aim.Composition; // Struct: mutate a copy and reassign.
        Vector2 pos = composition.ScreenPosition;
        if (pos.x != screenX || pos.y != aimScreenY)
        {
            composition.ScreenPosition = new Vector2(screenX, aimScreenY);
            aim.Composition = composition;
        }
    }

    // FramingSize fill: solve BOTH the orbit radius AND the aim-pin screen Y from the
    // player's live body so the body spans FramingSizeConst of the frame height at the
    // fixed lens (fills, not merely fits). Real size: Character.GetHeight() *
    // lossyScale.y. Returns the fill distance; the SOLVED aim-Y is stored into
    // fillAimY for the caller to ease the composer toward. No DollyMax cap here (the
    // player is rule 1); the caller clamps for safety. At 50 mm this lands ~6.5 m
    // full-frame, not the ~8.2 m the fit-only solve produced.
    private float SolvePlayerDolly(Character actor, float lensFov, float aimHeight, Vector3 subjectPosition,
        out float fillAimY, out float belowAimOut, out float aboveAimOut)
    {
        float height = Mathf.Clamp(actor.GetHeight(), 0.5f, 16f) * Mathf.Max(0.1f, actor.transform.lossyScale.y);
        float belowAim = aimHeight + PlayerFeetMargin;
        float aboveAim = Mathf.Max(0f, height - aimHeight) + PlayerHeadMargin;
        // Group vertical balance (behavior C): in combat with a live framed enemy
        // (weight > 0), combine the player's spans with the enemy's vertical extents
        // (projected to equivalent metres at the player's planar distance) so the
        // fill solves a group-consistent aim/dolly - a low creature lifts the aim and
        // adds dolly, a tall enemy adds headroom. Player-only when solo or w == 0.
        if (combatTargetId != 0 && TryEnemyGroupSpans(aimHeight, subjectPosition, out float enemyBelowEq, out float enemyAboveEq, out float weight))
        {
            var group = GroupFramingPolicy.CombineGroupSpans(belowAim, aboveAim, enemyBelowEq, enemyAboveEq, weight);
            belowAim = group.Below; aboveAim = group.Above;
        }
        belowAimOut = belowAim; aboveAimOut = aboveAim;
        FramingFillResult fill = GroupFramingPolicy.FramingFill(
            lensFov, belowAim, aboveAim, FramingSizeConst, TopGapShare);
        fillAimY = fill.AimScreenY;
        return fill.DistanceMetres;
    }

    // Tight-space resolve (behavior B.3/B.5): decide this frame's aim-Y target and
    // the radius cap given the solved fill distance, the body spans and the smoothed
    // boom room. When the room is >= the fill distance (within a small deadband) we
    // are NOT cramped - keep the fill aim and the fill distance. When the room is
    // smaller, TIGHT MODE: the camera sits at the available distance and the aim
    // comes from CroppedFraming (head pinned with its headroom, feet crop up). The
    // visible-body fraction drives the diagnostics band. Returns the aim-Y target to
    // ease toward; writes the frame fraction and sets tightActive.
    private float ResolveTightAim(float lensFov, float fillDistance, float fillAimY, float belowAim, float aboveAim)
    {
        tightActive = false;
        tightFrameFraction = 1f;
        float avail = availableDistance;
        if (avail >= fillDistance - 0.1f) return fillAimY; // Room enough: the fill owns the shot.
        tightActive = true;
        // Headroom GROWS as the shot crops: at the fill boundary it matches the
        // full shot's gap (6%), ramping to TightTopGapMax by the close-up floor.
        // The full-shot 6% is too thin once the camera is near - composer damping
        // and terrain bob overshoot it and the head kisses the frame top.
        float fillTopGap = TopGapShare * (1f - FramingSizeConst);
        float nearness = Mathf.InverseLerp(fillDistance, TightFloor, avail);
        float topGap = Mathf.Lerp(fillTopGap, TightTopGapMax, nearness);
        float d = Mathf.Max(0.51f, avail);
        CroppedFramingResult crop = GroupFramingPolicy.CroppedFraming(lensFov, d, aboveAim, belowAim, topGap);
        tightFrameFraction = crop.VisibleBodyFraction;
        return crop.AimScreenY;
    }

    // Project the engaged enemy's vertical extents (below/above the PLAYER's aim
    // line) to equivalent metres at the player's planar distance, for the group
    // vertical balance. equivalentMetres = tan(angleFromAimLine) * playerPlanarDist,
    // clamped to [0, 6]. The angles reuse the presented camera pose (same model as
    // RequiredDistanceForEnemy). Returns false (player-only solve) when there is no
    // live enemy Character or the sparse weight is zero.
    private bool TryEnemyGroupSpans(float aimHeight, Vector3 subjectPosition,
        out float enemyBelowEq, out float enemyAboveEq, out float weight)
    {
        enemyBelowEq = enemyAboveEq = weight = 0f;
        Character? enemy = FindCharacter(combatTargetId);
        if (!enemy) return false;
        float scale = Mathf.Max(0.1f, enemy!.transform.lossyScale.y);
        float enemyHeight = Mathf.Clamp(enemy.GetHeight(), 0.3f, 24f) * scale;
        Vector3 enemyPos = enemy.transform.position;
        Vector3 planar = enemyPos - subjectPosition; planar.y = 0f;
        if (!TargetRoster.Finite(planar)) return false;
        float sep = planar.magnitude;
        weight = GroupFramingPolicy.GroupWeight(sep, GroupFullMetres, GroupZeroMetres);
        if (weight <= 0f) return false;
        // The player's planar (camera-space) distance is the live orbit radius; the
        // enemy stands on the ground plane, so its feet are at the aim line minus
        // aimHeight and its head at (height - aimHeight) above. Convert each extent to
        // an equivalent vertical metre at the PLAYER's planar distance via the same
        // small-angle tangent relation the enemy-need solve uses: the apparent angle
        // of a vertical extent v at the enemy's planar distance (orbit radius + axial
        // offset) is atan(v / dEnemy); the equivalent metre at the player distance is
        // tan(that angle) * dPlayer = v * dPlayer / dEnemy.
        float dPlayer = Mathf.Max(1f, dollyRadius);
        float axis = orbit!.HorizontalAxis.Value * Mathf.Deg2Rad;
        Vector3 camForward = new Vector3(Mathf.Sin(axis), 0f, Mathf.Cos(axis));
        float axialOffset = Vector3.Dot(planar, camForward);
        float dEnemy = Mathf.Max(1f, dPlayer + axialOffset);
        float enemyBelowMetres = aimHeight;                       // aim line down to the enemy feet.
        float enemyAboveMetres = Mathf.Max(0f, enemyHeight - aimHeight); // aim line up to the enemy head.
        enemyBelowEq = Mathf.Clamp(enemyBelowMetres * dPlayer / dEnemy, 0f, 6f);
        enemyAboveEq = Mathf.Clamp(enemyAboveMetres * dPlayer / dEnemy, 0f, 6f);
        return true;
    }

    // The player orbit radius at which the engaged enemy also fits vertically, the
    // enemy standing on the player ground plane at an axial depth along the current
    // camera forward. When the full body cannot fit within DollyMax the enemy is
    // oversized: retry keeping only the top head band (feet may crop, rule 3).
    // Returns 0 when there is no live enemy Character to measure.
    private float SolveEnemyDolly(float lensFov, float aimHeight, Vector3 subjectPosition,
        float radius, float playerNeed)
    {
        Character? enemy = FindCharacter(combatTargetId);
        if (!enemy) return 0f;
        float scale = Mathf.Max(0.1f, enemy!.transform.lossyScale.y);
        float enemyHeight = Mathf.Clamp(enemy.GetHeight(), 0.3f, 24f) * scale;
        Vector3 enemyPos = enemy.transform.position;
        Vector3 planar = enemyPos - subjectPosition; planar.y = 0f;
        float sep = planar.magnitude;
        if (!TargetRoster.Finite(planar)) return 0f;
        // Sparse gate: a far enemy never drags the camera back (still fights/thirds).
        float weight = GroupFramingPolicy.GroupWeight(sep, GroupFullMetres, GroupZeroMetres);
        if (weight <= 0f) return 0f;
        // Enemy axial depth relative to the player along the camera forward (planar).
        // camera->player forward is dir(orbit axis); enemy nearer the camera gives a
        // negative offset (camera must dolly further out to hold it).
        float axis = orbit!.HorizontalAxis.Value * Mathf.Deg2Rad;
        Vector3 camForward = new Vector3(Mathf.Sin(axis), 0f, Mathf.Cos(axis)); // cam->player planar direction.
        float axialOffset = Vector3.Dot(planar, camForward);
        // Enemy vertical spans from the player aim point (enemy feet on ground plane).
        float enemyBelow = aimHeight + EnemyFeetMargin;
        float enemyAbove = Mathf.Max(0f, enemyHeight - aimHeight) + EnemyHeadMargin;
        float enemyNeedFull = GroupFramingPolicy.RequiredDistanceForEnemy(
            lensFov, aimScreenY, enemyBelow, enemyAbove, axialOffset);
        float enemyNeed = enemyNeedFull;
        if (enemyNeedFull > DollyMaxMetres)
        {
            // Oversized: keep the head band (top max(0.8, 30% height)); crop feet.
            float headKeep = Mathf.Max(0.8f, 0.30f * enemyHeight);
            float aboveExtent = Mathf.Max(0f, enemyHeight - aimHeight); // head top above aim, no margin.
            float reducedBelow = GroupFramingPolicy.HeadBandBelowSpan(aboveExtent, headKeep);
            enemyNeed = GroupFramingPolicy.RequiredDistanceForEnemy(
                lensFov, aimScreenY, reducedBelow, enemyAbove, axialOffset);
            dollyHeadBand = true;
        }
        // Blend the enemy contribution in by weight: effective eases from playerNeed
        // (weight 0, enemy ignored) to the full enemy need (weight 1).
        return Mathf.Lerp(playerNeed, enemyNeed, weight);
    }

    // Bounded lookup of a live Character by instance id (the engaged combat target).
    private static Character? FindCharacter(int instanceId)
    {
        foreach (Character c in Character.GetAllCharacters())
            if (c && c.GetInstanceID() == instanceId) return c;
        return null;
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
        combatLensMm = CombatLensFarMm; combatLensVelocity = 0f;
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
        // A fresh vantage, teleport or rebind re-seeds the dolly from the configured
        // distance (0 => the next solve seeds dollyRadius = SimpleFollowDistance).
        dollyRadius = 0f; dollyVelocity = 0f; desiredDollyRadius = 0f; dollyHeadBand = false;
        // Tight-space: re-seed the boom room to "open" (0 => the next solve seeds it to
        // the desired fill distance) so a fresh vantage/warp never starts cramped.
        availableDistance = 0f; availableVelocity = 0f; tightActive = false; tightFrameFraction = 1f;
        aimScreenY = SoloScreenY; aimScreenYVelocity = 0f; // Reseed the eased aim to the legacy pin.
        // Screen-space composition: side back to LEFT, lead 0, velocity sample voided.
        composition.Reset();
        soloScreenX = SoloScreenX; soloScreenXVelocity = 0f;
        leadOffsetX = 0f; leadOffsetVelocity = 0f;
        lateralScreenVelocity = 0f; hasVelocitySample = false; soloReasonRight = 0f;
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
