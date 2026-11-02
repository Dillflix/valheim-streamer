using System;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Native group framing owns group size and qualified positional adjustment.
// RotationComposer retains final aim and deliberate subject placement. The
// complete candidate still passes normal post-pipeline picture validation.
internal sealed class CinematicLensFraming : CinemachineGroupFraming
{
    private readonly GroupDollyPolicy dolly = new GroupDollyPolicy();
    private bool initialized, previousGroup, previousDolly, previousLateBody;
    private double now;
    internal bool FrameGroup;
    internal Action<CameraState, float>? BeforeAim;
    internal Func<CameraState, CameraState, bool>? ValidateTranslation;
    internal bool DollyApplied { get; private set; }
    internal bool DollyRejected { get; private set; }
    internal float DollyDistance { get; private set; }
    internal string Diagnostics { get; private set; } = "Group framing awaiting camera";

    internal void ConfigureDolly(double time, bool enabled, bool knownOpenSpace,
        float availableSpace, float groupSpread, float primaryDistance)
    {
        now = time;
        dolly.Configure(time, enabled, FrameGroup, knownOpenSpace, availableSpace, groupSpread, primaryDistance);
    }

    internal void ResetFraming()
    {
        initialized = previousGroup = previousDolly = previousLateBody = DollyApplied = DollyRejected = false;
        DollyDistance = 0; dolly.Reset();
        Diagnostics = "Group framing awaiting fresh camera";
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return;
        CameraState before = state;
        var body = vcam.GetCinemachineComponent(CinemachineCore.Stage.Body);
        bool lateBody = body != null && body.BodyAppliesAfterAim;
        // Group DollyThenZoom is a pre-Aim operation. A post-Aim body belongs
        // to a different family and only receives a native zoom fallback.
        bool warmGroup = !initialized || !vcam.PreviousStateIsValid || deltaTime < 0 ||
            FrameGroup && !previousGroup || previousLateBody != lateBody;
        bool useDolly = FrameGroup && dolly.Allowed && !lateBody && !warmGroup && ValidateTranslation != null;
        // A new group first seeds native ZoomOnly with zero positional history.
        // Its next DollyThenZoom solve keeps that valid history so native damping
        // eases translation in rather than jumping to a cold-start correction.
        bool reset = warmGroup || previousDolly && !useDolly;
        DollyApplied = DollyRejected = false; DollyDistance = 0;
        SizeAdjustment = useDolly ? SizeAdjustmentModes.DollyThenZoom : SizeAdjustmentModes.ZoomOnly;
        LateralAdjustment = LateralAdjustmentModes.ChangeRotation;
        DollyRange = useDolly ? new Vector2(-dolly.Limit, dolly.Limit) : Vector2.zero;
        SolveNative(vcam, lateBody, ref state, deltaTime, reset);

        if (!FrameGroup)
        {
            // Calling the native extension still initializes its per-camera
            // stage on a solo camera, but it cannot compete for the solo lens.
            state = before;
            Diagnostics = "Group framing inactive; solo lens owns the shot";
        }
        else if (useDolly)
        {
            Vector3 delta = state.GetCorrectedPosition() - before.GetCorrectedPosition();
            bool moving = delta.sqrMagnitude > .000001f;
            bool safe = dolly.WithinBudget(delta.x, delta.y, delta.z) && (!moving || ValidateTranslation!(before, state));
            if (!safe)
            {
                // Native group framing can add lateral parallax correction as
                // well as longitudinal dolly. Reject the entire proposal when
                // either exceeds the budget or fails the swept-view contract.
                // Recompute the lens at the original body pose; keeping the
                // rejected dolly's FOV would silently misframe the group.
                dolly.Reject(now); DollyRejected = true; useDolly = false;
                SizeAdjustment = SizeAdjustmentModes.ZoomOnly; DollyRange = Vector2.zero;
                state = before;
                SolveNative(vcam, lateBody, ref state, deltaTime, true);
                Diagnostics = dolly.Reason;
            }
            else
            {
                DollyApplied = moving; DollyDistance = delta.magnitude;
                Diagnostics = $"Native group DollyThenZoom: {DollyDistance:F2}m / {dolly.Limit:F2}m; bounded lens";
            }
        }
        else Diagnostics = !FrameGroup ? "Group framing inactive" :
            warmGroup ? "Native group zoom establishing; dolly damping seeded" :
            lateBody ? "Native group zoom retained for post-aim body" :
            ValidateTranslation == null ? "Native group zoom retained; movement validator unavailable" : dolly.Reason;

        if (FrameGroup)
        {
            if (!useDolly)
            {
                // Lens-only fallback is explicitly anchored to the supported
                // body pose, irrespective of upstream lateral/history details.
                state.RawPosition = before.RawPosition;
                state.PositionCorrection = before.PositionCorrection;
            }
            // Retain native position/lens. Only native recentering rotation is
            // suppressed so it cannot overwrite deliberate final composition.
            state.RawOrientation = before.RawOrientation;
            state.OrientationCorrection = before.OrientationCorrection;
        }
        initialized = true; previousGroup = FrameGroup; previousDolly = useDolly; previousLateBody = lateBody;
        BeforeAim?.Invoke(state, deltaTime);
    }

    private void SolveNative(CinemachineVirtualCameraBase vcam, bool lateBody,
        ref CameraState state, float deltaTime, bool reset)
    {
        bool valid = vcam.PreviousStateIsValid;
        try
        {
            // Reset only this native extension's private damping state while
            // its callback executes. Restore the camera flag before any other
            // extension/composer runs; body and aim history remain untouched.
            if (reset) vcam.PreviousStateIsValid = false;
            base.PostPipelineStageCallback(vcam, lateBody ? CinemachineCore.Stage.Body : CinemachineCore.Stage.Aim,
                ref state, deltaTime);
            // A changing preferred lens/range can outpace the native damped
            // offset for a frame. The current authored bounds remain absolute.
            if (FrameGroup) state.Lens.FieldOfView = Mathf.Clamp(state.Lens.FieldOfView, FovRange.x, FovRange.y);
        }
        finally { if (reset) vcam.PreviousStateIsValid = valid; }
    }
}
