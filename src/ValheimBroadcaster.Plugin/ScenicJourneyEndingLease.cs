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
    private readonly JourneyProofRenewalPolicy proof = new JourneyProofRenewalPolicy();
    private float aspect, nearClip, started, retryAt;
    private long evaluatingEpoch;
    private bool moveCarrier;
    internal bool Contradicted => Plan != null && proof.MustRetire(Time.unscaledTime);
    internal bool RevalidatingReadability => Plan != null && proof.RevalidatingReadability && !Contradicted;
    internal double ProofSecondsRemaining => CanAdmit ? Math.Max(0, JourneyHandoffPolicy.MaximumProofAgeSeconds - (Time.unscaledTime - ValidatedAt)) : 0;
    internal bool RefreshDue => Plan != null && (!CanAdmit || Time.unscaledTime - ValidatedAt >= JourneyHandoffPolicy.RefreshProofAfterSeconds);

    internal ScenicShotPlan? Plan { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal bool IsRefreshing { get; private set; }
    internal bool CanAdmit => proof.Available && Plan != null && !Contradicted &&
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
        float cameraAspect, float cameraNearClip, bool moveObserver, bool endpoint)
    {
        if (ending == null || (endpoint ? (!ending.IsPan && !ending.IsPictureOnly) || ending.IsJourney : !ending.IsJourney) || ending.Feature == null ||
            !Finite(cameraAspect) || cameraAspect <= 0 || !Finite(cameraNearClip) || cameraNearClip <= 0 ||
            !Finite(validatedAt) || validatedAt < 0)
            throw new ArgumentException("A finite already validated journey ending and camera format are required.");
        Reset(); Plan = ending; Epoch = epoch; ValidatedAt = validatedAt;
        aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver;
        proof.Begin(validatedAt); Reason = "Retaining the exact validated Journey plan";
    }

    internal void Step(bool force = false)
    {
        if (Plan == null) return;
        float now = Time.unscaledTime;
        if (proof.MustRetire(now))
        {
            if (IsRefreshing) { evaluator.Reset(); IsRefreshing = false; }
            if (proof.RevalidatingReadability) Reason = "Journey readability proof did not recover within its 2s renewal window";
            return;
        }
        long sceneEpoch = CameraSpatialScene.Shared.Epoch;
        if (Plan.IsPictureOnly)
        {
            RefreshRefuge(now, sceneEpoch, force); return;
        }
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
        { Reject("Journey proof refresh expired: " + evaluator.Reason, ScenicRejectionKind.None); return; }
        bool complete = evaluator.Step(6);
        if (!complete && (evaluator.FailureKind == ScenicRejectionKind.None ||
            evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete))
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? evaluated = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && evaluated != null && evaluator.SceneEpoch == sceneEpoch &&
            IdenticalEnding(Plan, evaluated))
        {
            if (!proof.TryAccept(now))
            { Reject("Journey proof validation completed after its renewal window", ScenicRejectionKind.InvalidPlan); return; }
            Epoch = sceneEpoch; ValidatedAt = now;
            evaluator.Reset(); IsRefreshing = false;
            Reason = "The exact Journey plan has current complete scene validation";
        }
        else Reject(complete && evaluator.Accepted ? "Journey fitting changed its admitted geometry" :
            evaluator.FailureKind != ScenicRejectionKind.None ? evaluator.FailureReason : evaluator.Reason,
            complete && evaluator.Accepted ? ScenicRejectionKind.InvalidPlan : evaluator.FailureKind);
    }

    private void RefreshRefuge(float now, long sceneEpoch, bool force)
    {
        if (Plan == null || now < retryAt || !force && CanAdmit && now - ValidatedAt < JourneyHandoffPolicy.RefreshProofAfterSeconds) return;
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
        using (work)
        {
            if (!ScenicStationaryProof.Check(Plan, visibility, aspect, nearClip, moveCarrier,
                out ScenicRejectionKind kind, out string reason))
            { Reject(reason, kind); return; }
            if (sceneEpoch != CameraSpatialScene.Shared.Epoch)
            { Reject("Scene changed during Journey refuge renewal", ScenicRejectionKind.Geometry); return; }
            if (!proof.TryAccept(now))
            { Reject("Journey refuge validation completed after its renewal window", ScenicRejectionKind.InvalidPlan); return; }
            // This is the complete proof for an immutable single viewpoint,
            // not a newly fitted route or sixteen copies of the same sample.
            Epoch = sceneEpoch; ValidatedAt = now; IsRefreshing = false;
            Reason = "The exact stationary Journey refuge has current complete scene validation";
        }
    }

    private static bool IdenticalEnding(ScenicShotPlan original, ScenicShotPlan evaluated)
    {
        if (original.IsPan != evaluated.IsPan || original.IsPictureOnly != evaluated.IsPictureOnly || original.IsJourney != evaluated.IsJourney ||
            original.Id != evaluated.Id || original.JourneyId != evaluated.JourneyId ||
            original.ContinuationOf != evaluated.ContinuationOf || original.UseNativeSpline != evaluated.UseNativeSpline ||
            original.Feature != evaluated.Feature ||
            original.FieldOfView != evaluated.FieldOfView || original.DurationSeconds != evaluated.DurationSeconds ||
            original.StableWaterClearance != evaluated.StableWaterClearance) return false;
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

    private void Reject(string reason, ScenicRejectionKind kind)
    {
        // New negative evidence immediately revokes the preceding proof; do
        // not continue using it for the remainder of an arbitrary age window.
        proof.Reject(kind, Time.unscaledTime); IsRefreshing = false; evaluator.Reset();
        retryAt = Time.unscaledTime + .5f; Reason = "Ending proof unavailable: " + reason;
    }

    internal void Reset()
    {
        evaluator.Reset(); visibility.Reset(); Plan = null; Epoch = evaluatingEpoch = 0;
        ValidatedAt = aspect = nearClip = started = retryAt = 0;
        IsRefreshing = moveCarrier = false; proof.Reset(); Reason = "No journey ending proof retained";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
