using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Keeps the admitted section's exact ending certified as its carrier
/// approaches. Refreshing neither chooses a different composition nor advances
/// the camera. An old or contradicted proof cannot authorize a later cut.</summary>
internal sealed class ScenicJourneyEndingLease : IDisposable
{
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicVisibility visibility = new ScenicVisibility("journey-ending-refresh");
    private float aspect, nearClip, started, retryAt;
    private long evaluatingEpoch;
    private bool moveCarrier, proofAvailable;

    internal ScenicShotPlan? Plan { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal bool IsRefreshing { get; private set; }
    internal bool CanAdmit => proofAvailable && Plan != null &&
        IsCurrent(Epoch, ValidatedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime);
    internal string Reason { get; private set; } = "No journey ending proof retained";

    internal static bool IsCurrent(long epoch, double validatedAt, long currentEpoch, double now)
        => JourneyHandoffPolicy.IsProofCurrent(epoch, validatedAt, currentEpoch, now);

    internal void Begin(ScenicShotPlan ending, long epoch, float validatedAt,
        float cameraAspect, float cameraNearClip, bool moveObserver)
    {
        if (ending == null || !ending.IsPan || ending.IsJourney || ending.Feature == null ||
            !Finite(cameraAspect) || cameraAspect <= 0 || !Finite(cameraNearClip) || cameraNearClip <= 0 ||
            !Finite(validatedAt) || validatedAt < 0)
            throw new ArgumentException("A finite already validated journey ending and camera format are required.");
        Reset(); Plan = ending; Epoch = epoch; ValidatedAt = validatedAt;
        aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver;
        proofAvailable = true; Reason = "Retaining the section's validated ending";
    }

    internal void Step()
    {
        if (Plan == null) return;
        float now = Time.unscaledTime;
        long sceneEpoch = CameraSpatialScene.Shared.Epoch;
        if (IsRefreshing && evaluatingEpoch != sceneEpoch)
        { evaluator.Reset(); IsRefreshing = false; retryAt = 0; }
        if (!IsRefreshing)
        {
            if (now < retryAt || CanAdmit && now - ValidatedAt < JourneyHandoffPolicy.RefreshProofAfterSeconds) return;
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                evaluator.Begin(Plan, visibility, aspect, nearClip, moveCarrier);
                evaluatingEpoch = sceneEpoch; started = now; IsRefreshing = true;
                Reason = "Refreshing complete journey ending proof";
            }
        }
        if (now - started > ScenicJourneyEnding.MaximumPreparationSeconds)
        { Reject("Ending refresh expired: " + evaluator.Reason); return; }
        bool complete = evaluator.Step(6);
        if (!complete && (evaluator.FailureKind == ScenicRejectionKind.None ||
            evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete))
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? evaluated = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && evaluated != null && evaluator.SceneEpoch == sceneEpoch &&
            IdenticalEnding(Plan, evaluated))
        {
            Epoch = sceneEpoch; ValidatedAt = now; proofAvailable = true;
            evaluator.Reset(); IsRefreshing = false;
            Reason = "The exact admitted ending has current complete scene validation";
        }
        else Reject(complete && evaluator.Accepted ? "Ending fitting changed its admitted geometry" : evaluator.Reason);
    }

    private static bool IdenticalEnding(ScenicShotPlan original, ScenicShotPlan evaluated)
    {
        if (!evaluated.IsPan || original.Feature != evaluated.Feature ||
            original.FieldOfView != evaluated.FieldOfView || original.DurationSeconds != evaluated.DurationSeconds) return false;
        // A fitted endpoint alone is not sufficient: preserve every sampled
        // point of the ending that the programme has already committed to use.
        for (int i = 0; i <= 16; i++)
        {
            ScenicShotSample before = original.Sample(i / 16d), after = evaluated.Sample(i / 16d);
            if (before.CameraPosition.Distance(after.CameraPosition) >= .005 ||
                before.AimPosition.Distance(after.AimPosition) >= .005 ||
                before.ScreenX != after.ScreenX || before.ScreenY != after.ScreenY) return false;
        }
        return true;
    }

    private void Reject(string reason)
    {
        // New negative evidence immediately revokes the preceding proof; do
        // not continue using it for the remainder of an arbitrary age window.
        proofAvailable = false; IsRefreshing = false; evaluator.Reset();
        retryAt = Time.unscaledTime + .5f; Reason = "Ending proof unavailable: " + reason;
    }

    internal void Reset()
    {
        evaluator.Reset(); visibility.Reset(); Plan = null; Epoch = evaluatingEpoch = 0;
        ValidatedAt = aspect = nearClip = started = retryAt = 0;
        IsRefreshing = proofAvailable = moveCarrier = false; Reason = "No journey ending proof retained";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
