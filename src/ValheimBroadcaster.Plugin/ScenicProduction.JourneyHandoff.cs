using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    private ScenicShotPlan? journeyExitCapturedPlan;
    private JourneyPresentationSample? journeyExitCapture;
    private Vector3 journeyExitCapturedFocus;
    private long journeyExitPhysicalEpoch;

    // The render audit's Scene identifies a presentation, not the physical
    // observation epoch. Retain them separately and never substitute one for
    // the other. Camera.main fallback poses are only planning seeds.
    private void RecordJourneyExitCapture(ScenicShotPlan shown, JourneyPresentationSample? capture, Vector3 focus)
    {
        if (!capture.HasValue || shown.Feature == null || !TargetRoster.Finite(focus)) return;
        JourneyPresentationSample sample = capture.Value;
        double age = Time.unscaledTimeAsDouble - sample.Time;
        long physicalEpoch = CameraSpatialScene.Shared.Epoch;
        if (sample.Audit.Shot != shown.Id || sample.Audit.Status != CameraRenderAuditStatus.Matched ||
            !sample.Audit.Captured.Valid || age < 0 || age > .25 || physicalEpoch <= 0 ||
            !FiniteExit(sample.Progress) || sample.Progress < 0 || sample.Progress > 1 ||
            journeyOwned && journeyEpoch != physicalEpoch) return;
        journeyExitCapturedPlan = shown; journeyExitCapture = sample;
        journeyExitCapturedFocus = focus; journeyExitPhysicalEpoch = physicalEpoch;
    }

    /// <summary>Called before Journey relinquishes its on-air plan. A measured
    /// capture supplies an exact composition proposal, not a geometry lease.
    /// Only complete current validation promotes it to independent coverage.</summary>
    private void PreserveJourneyExitPicture()
    {
        if (!journeyOwned) return;
        Camera? camera = Camera.main;
        Vector3 currentOrigin = camera ? camera.transform.position :
            Player.m_localPlayer ? Player.m_localPlayer.transform.position : origin;
        if (!TargetRoster.Finite(currentOrigin)) currentOrigin = origin;
        long epoch = CameraSpatialScene.Shared.Epoch;
        double now = Time.unscaledTimeAsDouble;
        ScenicShotPlan? capturedPlan = journeyExitCapturedPlan;
        JourneyPresentationSample? captured = journeyExitCapture;
        ScenicShotPlan? picture = null;
        if (capturedPlan?.Feature != null && captured.HasValue && capturedPlan.Id == Plan?.Id &&
            journeyExitPhysicalEpoch == epoch && now >= captured.Value.Time && now - captured.Value.Time <= .25)
        {
            JourneyPresentationSample sample = captured.Value;
            ScenicShotSample composition = capturedPlan.Sample(sample.Progress);
            double fov = sample.Audit.Captured.Fov;
            if (FiniteExit(fov) && fov >= 30 && fov <= 110 &&
                TargetRoster.Finite(FromCore(sample.Position)) &&
                Vector3.Distance(FromCore(sample.Position), currentOrigin) < 2)
            {
                try
                {
                    picture = ScenicShotPlan.Picture(++nextId, capturedPlan.Feature, capturedPlan.Role,
                        sample.Position, new AutonomyPosition(journeyExitCapturedFocus.x, journeyExitCapturedFocus.y, journeyExitCapturedFocus.z),
                        (float)fov, composition.ScreenX, composition.ScreenY)
                        .WithStableWaterClearance(capturedPlan.StableWaterClearance);
                }
                catch (ArgumentException) { picture = null; }
            }
        }

        // Background preparations can survive only as unshown local candidates.
        // The stale pre-Journey current plan must never become on-air implicitly.
        ScenicShotPlan? prepared = director.Prepared;
        bool keepPrepared = prepared != null && preparedEpoch == epoch &&
            Time.unscaledTime >= preparedAt && Time.unscaledTime - preparedAt <= 6 &&
            WithinJourneyHorizon(prepared, currentOrigin);
        CancelComparison("Journey ended; rebase ordinary coverage at its actual camera position");
        director.Reset(); pictures.Reset(); pictureAdmission.Reset(); pictureVisibility.Reset();
        usingIndependentPicture = false; readiness.Reset();
        testing = best = null; searching = planning = revalidating = trimmed = elevated = false;
        evaluator.Reset(); spatial.Reset(); subjects.Clear(); subjectRetry.Clear(); testingKey = null;
        origin = currentOrigin; HasRegion = TargetRoster.Finite(currentOrigin);
        journeyOrdinarySearchOrigin = origin; retryAt = proposed = 0; forceReplacement = false;
        deadlineHandled = false; RecoveryExhausted = recovery.Expired;
        survey.Begin(origin); refreshingSurvey = false; refreshSurvey.Reset(); surveyRefreshAt = Time.unscaledTime + 15;
        if (keepPrepared && prepared != null && director.SuccessorContextReady(prepared)) director.Stage(prepared);
        journeyExitCapturedPlan = null; journeyExitCapture = null; journeyExitPhysicalEpoch = 0;
        if (picture == null || !captured.HasValue) return;

        bool accepted = false;
        bool pending = true;
        if (camera && CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work))
        {
            using (work)
            {
                accepted = ScenicStationaryProof.Check(picture, pictureVisibility, camera.aspect, camera.nearClipPlane,
                    true, out ScenicRejectionKind rejection, out string reason);
                pictureAdmissionValidation = accepted ? "Exact captured Journey exit passed current stationary validation" : rejection + ": " + reason;
                pending = rejection == ScenicRejectionKind.SurveyIncomplete || rejection == ScenicRejectionKind.Geometry ||
                    rejection == ScenicRejectionKind.Acquisition;
                // Validation may itself observe a physical scene change.
                accepted &= CameraSpatialScene.Shared.Epoch == epoch;
            }
        }
        if (accepted && pictures.TryOffer(picture, epoch, CameraSpatialScene.Shared.Epoch,
            Time.unscaledTimeAsDouble, Time.unscaledTimeAsDouble, false))
        {
            usingIndependentPicture = true;
            Reason = "Retaining the validated Journey exit while ordinary scenic coverage prepares";
            ScenicTrace.Event("journey-exit-picture", Reason, picture.Id);
            return;
        }
        if (pending && pictureAdmission.TryQueue(picture, epoch, CameraSpatialScene.Shared.Epoch,
            captured.Value.Time, Time.unscaledTimeAsDouble))
        {
            nextPictureAdmissionTrace = 0;
            Reason = "Validating the captured Journey exit for independent scenic coverage";
            ScenicTrace.Event("journey-exit-picture-pending", Reason, picture.Id);
        }
    }

    private static bool FiniteExit(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
