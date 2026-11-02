using System;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// One alternative Body owner. PositionComposer tracks the actual observed point
// against fixed screen/depth guides and a fixed orientation, so subjects may move
// through the picture before the native body responds. There is no Aim component
// for this family; the rig disables Orbit/Dolly/RotationComposer before Configure.
internal sealed class NativeObservationalCamera : IDisposable
{
    private readonly CinemachineCamera camera;
    private readonly CinemachinePositionComposer composer;
    private readonly ObservationalShotLease lease = new ObservationalShotLease();
    private ObservationalShotProfile profile;
    private Transform? boundTarget;
    private Quaternion orientation = Quaternion.identity;
    internal Quaternion Orientation => orientation;
    internal bool Enabled
    {
        get => composer && composer.enabled;
        set { if (!value) Suspend(); else if (composer) composer.enabled = true; }
    }
    internal string Diagnostics => Enabled && lease.IsBound
        ? $"Position Composer observation; fixed heading; free screen {profile.DeadZoneX:P0} x {profile.DeadZoneY:P0}; depth {profile.DepthDeadZone:F1}m"
        : "Position Composer observation inactive";

    internal NativeObservationalCamera(CinemachineCamera camera)
    {
        if (!camera) throw new ArgumentNullException(nameof(camera));
        this.camera = camera;
        composer = camera.gameObject.AddComponent<CinemachinePositionComposer>();
        composer.enabled = false;
        composer.CenterOnActivate = false;
        composer.Lookahead.Enabled = false;
        composer.TargetOffset = Vector3.zero;
    }

    internal bool Configure(Transform target, Vector3 trackedFocus, Vector3 seedPosition, Quaternion seedOrientation,
        ObservationalShotProfile requestedProfile, long sceneId, long shotToken, float now)
    {
        if (!camera || !composer || !target || !Point(trackedFocus) || !Point(seedPosition) || !Rotation(seedOrientation) ||
            !requestedProfile.IsValid || !Finite(now) || now < 0)
        { Suspend(); return false; }
        if (boundTarget != target) lease.Reset();
        bool reseed = lease.Update(now, sceneId, shotToken, new AutonomyPosition(trackedFocus.x, trackedFocus.y, trackedFocus.z));
        target.position = trackedFocus;
        boundTarget = target;
        camera.Follow = target;
        // LookAt is still useful to deocclusion/quality extensions; no Aim owner
        // uses it to erase the intentional screen/depth movement.
        camera.LookAt = target;
        composer.enabled = true;
        if (reseed)
        {
            Vector3 forward = seedOrientation * Vector3.forward;
            if (Vector3.ProjectOnPlane(forward, Vector3.up).sqrMagnitude < .01f)
            { Suspend(); return false; }
            orientation = Quaternion.LookRotation(forward, Vector3.up);
            profile = requestedProfile;
            composer.CameraDistance = profile.Distance;
            composer.DeadZoneDepth = profile.DepthDeadZone;
            composer.Damping = new Vector3(profile.DampingX, profile.DampingY, profile.DampingZ);
            composer.Composition = Composition(profile);
            // Guide/distance changes intentionally bypass damping upstream. They
            // are therefore installed only once at a real shot/binding boundary.
            camera.ForceCameraPosition(seedPosition, orientation);
            // ForceCameraPosition marks the camera valid upstream. The first
            // composer solve must still initialize its desired-distance and
            // guide caches from this newly latched shot, not an older/default one.
            camera.PreviousStateIsValid = false;
        }
        // The native camera reads its transform's orientation before Body. Only
        // orientation is fixed here; native PositionComposer retains body history.
        camera.transform.rotation = orientation;
        return true;
    }

    internal static ScreenComposerSettings Composition(ObservationalShotProfile value)
    {
        if (!value.IsValid) throw new ArgumentOutOfRangeException(nameof(value));
        var settings = ScreenComposerSettings.Default;
        // Core screen coordinates are Y-up; Cinemachine guides are Y-down.
        settings.ScreenPosition = new Vector2(value.ScreenX, -value.ScreenY);
        settings.DeadZone.Enabled = true;
        settings.DeadZone.Size = new Vector2(value.DeadZoneX, value.DeadZoneY);
        settings.HardLimits.Enabled = true;
        settings.HardLimits.Size = new Vector2(value.HardLimitX, value.HardLimitY);
        settings.HardLimits.Offset = Vector2.zero;
        settings.Validate();
        return settings;
    }

    internal void Suspend()
    {
        if (composer) composer.enabled = false;
        lease.Reset(); boundTarget = null;
    }
    internal void Reset() { Suspend(); profile = default; orientation = Quaternion.identity; }
    public void Dispose() { Reset(); if (composer) UnityEngine.Object.Destroy(composer); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Point(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) &&
        Mathf.Abs(value.x) <= 20000 && Mathf.Abs(value.y) <= 20000 && Mathf.Abs(value.z) <= 20000;
    private static bool Rotation(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
        value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .5f &&
        value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w < 1.5f;
}
