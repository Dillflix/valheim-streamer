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
    internal bool Contradicted { get; private set; }
    internal double ProofSecondsRemaining => CanAdmit ? Math.Max(0, JourneyHandoffPolicy.MaximumProofAgeSeconds - (Time.unscaledTime - ValidatedAt)) : 0;
    internal bool RefreshDue => Plan != null && (!CanAdmit || Time.unscaledTime - ValidatedAt >= JourneyHandoffPolicy.RefreshProofAfterSeconds);

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
        BeginPlan(ending, epoch, validatedAt, cameraAspect, cameraNearClip, moveObserver, true);
    }

    internal void BeginRoute(ScenicShotPlan route, long epoch, float validatedAt,
        float cameraAspect, float cameraNearClip, bool moveObserver)
        => BeginPlan(route, epoch, validatedAt, cameraAspect, cameraNearClip, moveObserver, false);

    private void BeginPlan(ScenicShotPlan ending, long epoch, float validatedAt,
        float cameraAspect, float cameraNearClip, bool moveObserver, bool pan)
    {
        if (ending == null || (pan ? !ending.IsPan || ending.IsJourney : !ending.IsJourney) || ending.Feature == null ||
            !Finite(cameraAspect) || cameraAspect <= 0 || !Finite(cameraNearClip) || cameraNearClip <= 0 ||
            !Finite(validatedAt) || validatedAt < 0)
            throw new ArgumentException("A finite already validated journey ending and camera format are required.");
        Reset(); Plan = ending; Epoch = epoch; ValidatedAt = validatedAt;
        aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver;
        proofAvailable = true; Reason = "Retaining the exact validated Journey plan";
    }

    internal void Step(bool force = false)
    {
        if (Plan == null) return;
        float now = Time.unscaledTime;
        long sceneEpoch = CameraSpatialScene.Shared.Epoch;
        if (IsRefreshing && evaluatingEpoch != sceneEpoch)
        { evaluator.Reset(); IsRefreshing = false; retryAt = 0; }
        if (!IsRefreshing)
        {
            if (now < retryAt || !force && CanAdmit && now - ValidatedAt < JourneyHandoffPolicy.RefreshProofAfterSeconds) return;
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                evaluator.Begin(Plan, visibility, aspect, nearClip, moveCarrier);
                evaluatingEpoch = sceneEpoch; started = now; IsRefreshing = true;
                Reason = "Refreshing the complete unchanged Journey plan";
            }
        }
        if (now - started > ScenicJourneyEnding.MaximumPreparationSeconds)
        { Reject("Journey proof refresh expired: " + evaluator.Reason, false); return; }
        bool complete = evaluator.Step(6);
        if (!complete && (evaluator.FailureKind == ScenicRejectionKind.None ||
            evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete))
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? evaluated = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && evaluated != null && evaluator.SceneEpoch == sceneEpoch &&
            IdenticalEnding(Plan, evaluated))
        {
            Epoch = sceneEpoch; ValidatedAt = now; proofAvailable = true; Contradicted = false;
            evaluator.Reset(); IsRefreshing = false;
            Reason = "The exact Journey plan has current complete scene validation";
        }
        else Reject(complete && evaluator.Accepted ? "Journey fitting changed its admitted geometry" : evaluator.Reason,
            complete && evaluator.Accepted || IsContradiction(evaluator.FailureKind));
    }

    private static bool IdenticalEnding(ScenicShotPlan original, ScenicShotPlan evaluated)
    {
        if (original.IsPan != evaluated.IsPan || original.IsJourney != evaluated.IsJourney ||
            original.Id != evaluated.Id || original.JourneyId != evaluated.JourneyId ||
            original.ContinuationOf != evaluated.ContinuationOf || original.UseNativeSpline != evaluated.UseNativeSpline ||
            original.Feature != evaluated.Feature ||
            original.FieldOfView != evaluated.FieldOfView || original.DurationSeconds != evaluated.DurationSeconds) return false;
        // A fitted endpoint alone is not sufficient: preserve every sampled
        // point of the ending that the programme has already committed to use.
        for (int i = 0; i <= 64; i++)
        {
            ScenicShotSample before = original.Sample(i / 64d), after = evaluated.Sample(i / 64d);
            if (before.CameraPosition.Distance(after.CameraPosition) >= .005 ||
                before.AimPosition.Distance(after.AimPosition) >= .005 ||
                before.ScreenX != after.ScreenX || before.ScreenY != after.ScreenY) return false;
        }
        return true;
    }

    private static bool IsContradiction(ScenicRejectionKind kind) =>
        kind == ScenicRejectionKind.SurfaceClearance || kind == ScenicRejectionKind.CameraCollision ||
        kind == ScenicRejectionKind.CorridorCollision || kind == ScenicRejectionKind.Focus ||
        kind == ScenicRejectionKind.Foliage || kind == ScenicRejectionKind.InvalidPlan;

    private void Reject(string reason, bool contradiction)
    {
        // New negative evidence immediately revokes the preceding proof; do
        // not continue using it for the remainder of an arbitrary age window.
        proofAvailable = false; Contradicted = contradiction; IsRefreshing = false; evaluator.Reset();
        retryAt = Time.unscaledTime + .5f; Reason = "Ending proof unavailable: " + reason;
    }

    internal void Reset()
    {
        evaluator.Reset(); visibility.Reset(); Plan = null; Epoch = evaluatingEpoch = 0;
        ValidatedAt = aspect = nearClip = started = retryAt = 0;
        IsRefreshing = proofAvailable = moveCarrier = Contradicted = false; Reason = "No journey ending proof retained";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
