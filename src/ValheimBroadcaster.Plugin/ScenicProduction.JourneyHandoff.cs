using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    // Keep the last actual render across the transition into/out of guided flight.
    private JourneyPresentationSample? lastCameraCapture;

    private void RememberDepartureCapture(JourneyPresentationSample? capture)
    {
        if (capture.HasValue && capture.Value.Audit.Status == CameraRenderAuditStatus.Matched &&
            capture.Value.Audit.Captured.Valid) lastCameraCapture = capture;
    }

    private static Vector3 RenderedDirection(CameraRenderPose pose) =>
        new Quaternion((float)pose.Qx, (float)pose.Qy, (float)pose.Qz, (float)pose.Qw) * Vector3.forward;

    private bool TryDeparturePose(out Vector3 position, out Vector3 forward)
    {
        position = forward = default;
        if (!Player.m_localPlayer) return false;
        double age = lastCameraCapture.HasValue ? Time.unscaledTimeAsDouble - lastCameraCapture.Value.Time : double.PositiveInfinity;
        if (age >= 0 && age <= .25 && lastCameraCapture.HasValue)
        {
            position = FromCore(lastCameraCapture.Value.Position);
            forward = RenderedDirection(lastCameraCapture.Value.Audit.Captured);
        }
        else
        {
            Camera? camera = Camera.main;
            if (!camera) return false;
            position = camera.transform.position; forward = camera.transform.forward;
        }
        return TargetRoster.Finite(position) && TargetRoster.Finite(forward) &&
            (position - Player.m_localPlayer.transform.position).sqrMagnitude < 75 * 75 &&
            PlayerShotReadiness.PositionReady(position, true);
    }

    private void RebaseAfterJourney()
    {
        if (guide == null) return;
        Vector3 current = FromCore(journey.LastRenderedPosition ?? guide.Position);
        CancelComparison("Journey ended; prepare local coverage at the rendered camera");
        director.Reset(); pictures.Reset(); pictureAdmission.Reset(); pictureVisibility.Reset();
        readiness.Reset(); liveSceneReadiness.Reset(); recovery.Reset();
        lastPresentedPlan = null; lastAdmittedReadiness = lastPresentedReadiness = default;
        usingIndependentPicture = false; testing = best = null; searching = planning = revalidating = trimmed = elevated = false;
        evaluator.Reset(); spatial.Reset(); subjects.Clear(); subjectRetry.Clear(); testingKey = null;
        origin = current; HasRegion = true; retryAt = proposed = 0; forceReplacement = false;
        deadlineHandled = RecoveryExhausted = false;
        ScenicFoliageScene.Shared.SetRegion(origin); survey.Begin(origin);
        refreshingSurvey = false; refreshSurvey.Reset(); surveyRefreshAt = Time.unscaledTime + 15;
        JourneyPresentationSample? captured = lastCameraCapture;
        if (!captured.HasValue || captured.Value.Audit.Shot != journey.Id ||
            Time.unscaledTimeAsDouble - captured.Value.Time > .25) return;
        CameraRenderPose rendered = captured.Value.Audit.Captured;
        Vector3 direction = RenderedDirection(rendered);
        var focus = ToCore(current + direction * 24);
        var feature = new ScenicFeature("journey-exit", ScenicFeatureKind.Landscape, focus,
            ToCore(direction), 24, 0, 0);
        var still = ScenicShotPlan.Picture(++nextId, feature, ScenicShotRole.Establish,
            captured.Value.Position, focus, Mathf.Clamp((float)rendered.Fov, 30, 100), 0, 0).WithJourneyView();
        long epoch = CameraSpatialScene.Shared.Epoch;
        pictureAdmission.TryQueue(still, epoch, epoch, captured.Value.Time, Time.unscaledTimeAsDouble);
    }
}
