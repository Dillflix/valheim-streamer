using System;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using ValheimBroadcaster.CinemachineRuntime;

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
    private float diagnosticsAt;
    internal string LastFailure { get; private set; } = "Awaiting player";
    internal string Diagnostics { get; private set; } = "Simple follow: awaiting player";

    // Slightly center-left with a little headroom. Cinemachine screen guides
    // are Y-down, so positive Y places the aim point below frame centre,
    // leaving room above the character's head.
    private static readonly Vector2 ScreenPosition = new Vector2(-0.08f, 0.05f);

    internal bool TrySolve(Character actor, BroadcastSettings settings, Camera output, float dt,
        out Vector3 position, out Quaternion rotation, out float fov)
    {
        position = default; rotation = Quaternion.identity; fov = settings.FieldOfView.Value;
        if (!actor) return Fail("Camera subject unavailable");
        EnsureCreated();
        float radius = Mathf.Max(1f, settings.SimpleFollowDistance.Value);
        float aimHeight = Mathf.Clamp(actor.GetHeight() * AimHeightFraction, 0.5f, 14f) * Mathf.Max(0.1f, actor.transform.lossyScale.y);
        Vector3 subjectPosition = actor.transform.position;
        if (!TargetRoster.Finite(subjectPosition)) return Fail("Camera subject position unavailable");
        bool rebind = subject != actor || !camera!.Follow;
        if (rebind) Bind(actor, radius, aimHeight, subjectPosition);
        else if ((subjectPosition - lastSubjectPosition).sqrMagnitude > WarpDistanceMetres * WarpDistanceMetres)
            camera!.OnTargetObjectWarped(actor.transform, subjectPosition - lastSubjectPosition);
        lastSubjectPosition = subjectPosition;
        lookPoint!.position = subjectPosition + Vector3.up * aimHeight;
        orbit!.Radius = radius;
        orbit.TargetOffset = new Vector3(0f, aimHeight, 0f);
        if (output) camera!.Lens.PullInheritedPropertiesFromCamera(output);
        camera!.Lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        camera.Lens.FieldOfView = Mathf.Clamp(settings.FieldOfView.Value, 30f, 110f);
        camera.Lens.NearClipPlane = Mathf.Max(0.01f, output ? output.nearClipPlane : 0.3f);
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
        float now = Time.unscaledTime;
        if (now >= diagnosticsAt)
        {
            diagnosticsAt = now + 0.25f;
            Diagnostics = $"Simple follow: Cinemachine 3.1.7; distance {radius:F1}m (actual {Vector3.Distance(position, subjectPosition):F1}m); " +
                $"screen ({ScreenPosition.x:F2}, {ScreenPosition.y:F2}); recentering behind subject; fov {fov:F0}";
        }
        LastFailure = "none";
        return true;
    }

    // A fresh binding seeds the camera directly behind the subject's facing at
    // the configured radius, then lets the native components own all motion.
    private void Bind(Character actor, float radius, float aimHeight, Vector3 subjectPosition)
    {
        subject = actor;
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
