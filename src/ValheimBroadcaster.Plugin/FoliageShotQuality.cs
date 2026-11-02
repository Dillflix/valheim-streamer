using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Runs after Cinemachine's body/aim/obstacle resolution. All candidates share one
// bounded, read-only foliage survey; this extension never changes world objects.
internal sealed class FoliageShotQuality : CinemachineExtension, IShotQualityEvaluator
{
    internal PlayerShotVisibility? Visibility;
    internal ScenicVisibility? Landscape;
    internal Vector3 Focus;
    internal IReadOnlyList<PlayerShotVisibility.Subject>? Subjects = null;
    internal float Aspect = 16f / 9f, NearClipPlane = 0.3f;
    internal PlayerShotVisibility.Result Result;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || Visibility == null) return;
        Vector3 position = state.GetFinalPosition();
        Quaternion rotation = state.GetFinalOrientation();
        Result = Landscape != null
            ? FromLandscape(Landscape.Evaluate(position, rotation, Focus, state.Lens.FieldOfView, Aspect, NearClipPlane))
            : Subjects == null || Subjects.Count == 0
            ? Visibility.Evaluate(position, rotation, Focus, state.Lens.FieldOfView, Aspect, NearClipPlane)
            : Visibility.Evaluate(position, rotation, Subjects, state.Lens.FieldOfView, Aspect, NearClipPlane);
        // Full projected body size, safe-frame coverage and headroom are lens-
        // aware. World distance is not a composition preference: the same image
        // at another focal length should receive the same readability score.
        state.ShotQuality = Mathf.Clamp01(Result.Score);
    }

    // Environmental quality has no synthetic standing body, size or headroom.
    // The common engine uses these focus/clearance fields under a scenic profile.
    internal static PlayerShotVisibility.Result FromLandscape(ScenicVisibility.Result view) =>
        new PlayerShotVisibility.Result(view.Score, view.CameraClear, 0, 0, view.ForegroundOcclusion,
            view.SurveyComplete, view.Reason, view.FocusVisible, view.Score, view.Score);
}
