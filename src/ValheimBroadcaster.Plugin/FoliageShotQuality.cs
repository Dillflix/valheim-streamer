using Unity.Cinemachine;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Runs after Cinemachine's body/aim/obstacle resolution. All candidates share one
// bounded, read-only foliage survey; this extension never changes world objects.
internal sealed class FoliageShotQuality : CinemachineExtension, IShotQualityEvaluator
{
    internal PlayerShotVisibility? Visibility;
    internal Vector3 Focus;
    internal float Aspect = 16f / 9f, NearClipPlane = 0.3f, PreferredDistance = 18f;
    internal PlayerShotVisibility.Result Result;

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || Visibility == null) return;
        Vector3 position = state.GetFinalPosition();
        Result = Visibility.Evaluate(position, Focus, state.Lens.FieldOfView, Aspect, NearClipPlane);
        // A shortened but readable forest shot is useful. Penalize severe
        // compression without making wide framing mandatory under a canopy.
        float distance = Vector3.Distance(position, Focus);
        float framing = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(distance / Mathf.Max(2f, PreferredDistance * 0.5f)));
        state.ShotQuality = Mathf.Clamp01(Result.Score * framing);
    }
}
