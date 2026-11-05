using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Certifies an exact-seam stationary refuge before taking a cruise
/// section on air. Only a deliberate arrival needs a complete angular sweep.
/// Both retain full camera, aim, terrain, near-plane and foliage validation.</summary>
internal sealed class ScenicJourneyEndingPlanner : IDisposable
{
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicVisibility visibility = new ScenicVisibility("journey-ending");
    private ScenicShotPlan? section, candidate;
    private AutonomyPosition? arrivalFocus;
    private ScenicFeature? arrivalSubject;
    private long id, requestedEpoch;
    private int alternative;
    private float aspect, nearClip, started;
    private bool moveCarrier;
    private string lastFailure = "none";

    internal bool Complete { get; private set; }
    internal ScenicShotPlan? Ready { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal bool ArrivalFramed { get; private set; }
    internal string Reason { get; private set; } = "No journey ending requested";

    internal void Begin(ScenicShotPlan source, long endingId, float cameraAspect, float cameraNearClip,
        bool moveObserver, Vector3? locallyObservedArrivalFocus = null, ScenicFeature? locallyObservedArrivalSubject = null)
    {
        if (source == null || !source.IsJourney || source.Feature == null || endingId <= 0 || endingId == source.Id ||
            !Finite(cameraAspect) || cameraAspect <= 0 || !Finite(cameraNearClip) || cameraNearClip <= 0 ||
            locallyObservedArrivalFocus.HasValue && !TargetRoster.Finite(locallyObservedArrivalFocus.Value) ||
            locallyObservedArrivalSubject != null && !locallyObservedArrivalFocus.HasValue)
            throw new ArgumentException("A finite journey section and camera format are required for an ending.");
        Reset(); section = source; id = endingId; aspect = cameraAspect; nearClip = cameraNearClip;
        moveCarrier = moveObserver; started = Time.unscaledTime; requestedEpoch = CameraSpatialScene.Shared.Epoch;
        arrivalSubject = locallyObservedArrivalSubject;
        if (locallyObservedArrivalFocus.HasValue)
        {
            Vector3 focus = locallyObservedArrivalFocus.Value;
            arrivalFocus = new AutonomyPosition(focus.x, focus.y, focus.z);
        }
        Reason = arrivalFocus.HasValue ? "Preparing the locally observed Journey arrival" : "Certifying the exact Journey endpoint refuge";
    }

    internal void Step()
    {
        if (Complete || section == null) return;
        if (CameraSpatialScene.Shared.Epoch != requestedEpoch)
        { Finish("Scene changed while validating journey ending"); return; }
        if (Time.unscaledTime - started > ScenicJourneyEnding.MaximumPreparationSeconds)
        { Finish("Journey ending preparation expired; last evidence: " + lastFailure); return; }
        if (candidate == null)
        {
            if (alternative >= (arrivalFocus.HasValue ? ScenicJourneyEnding.CandidateCount : 1))
            { Finish("No fully validated finite ending; last evidence: " + lastFailure); return; }
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                candidate = arrivalFocus.HasValue
                    ? ScenicJourneyEnding.Propose(section, id, alternative++, arrivalFocus, arrivalSubject)
                    : ScenicJourneyEnding.Refuge(section, id);
                if (!arrivalFocus.HasValue) alternative++;
                if (candidate == null) return;
                if (!candidate.IsPictureOnly) evaluator.Begin(candidate, visibility, aspect, nearClip, moveCarrier);
            }
        }
        if (candidate.IsPictureOnly)
        {
            StepRefuge(); return;
        }
        bool complete = evaluator.Step(6);
        if (!complete && (evaluator.FailureKind == ScenicRejectionKind.None ||
            evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete))
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? evaluated = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && evaluated != null && evaluator.SceneEpoch == requestedEpoch)
        {
            ScenicShotSample seam = section.Sample(1), entry = evaluated.Sample(0);
            if (seam.CameraPosition.Distance(entry.CameraPosition) < .005 &&
                seam.AimPosition.Distance(entry.AimPosition) < .005 &&
                evaluated.FieldOfView == section.FieldOfView && entry.ScreenX == seam.ScreenX && entry.ScreenY == seam.ScreenY)
            {
                Ready = evaluated; Epoch = requestedEpoch; ValidatedAt = Time.unscaledTime; Complete = true;
                ArrivalFramed = arrivalSubject != null && evaluated.Feature == arrivalSubject && arrivalFocus.HasValue &&
                    ScenicJourneyEnding.AimsAtObservedArrival(evaluated, arrivalFocus.Value);
                if (arrivalSubject != null && !ArrivalFramed)
                {
                    Ready = null; Complete = false; candidate = null; evaluator.Reset();
                    lastFailure = "Ending did not resolve the observed arrival subject";
                    Reason = lastFailure; return;
                }
                Reason = "Validated finite six-second Journey arrival pan"; return;
            }
            lastFailure = "Terrain fitting moved the journey ending seam";
        }
        else lastFailure = evaluator.Reason;
        candidate = null; evaluator.Reset();
        Reason = "Trying a different finite ending: " + lastFailure;
    }

    private void StepRefuge()
    {
        if (candidate == null || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
        using (work)
        {
            bool valid = ScenicStationaryProof.Check(candidate, visibility, aspect, nearClip, moveCarrier,
                out ScenicRejectionKind kind, out string reason);
            if (CameraSpatialScene.Shared.Epoch != requestedEpoch)
            { Finish("Scene changed while validating the Journey refuge"); return; }
            if (valid)
            {
                bool arrival = arrivalSubject != null && candidate.Feature == arrivalSubject && arrivalFocus.HasValue &&
                    ScenicJourneyEnding.AimsAtObservedArrival(candidate, arrivalFocus.Value);
                if (arrivalSubject != null && !arrival)
                { lastFailure = "Stationary ending does not frame the observed arrival subject"; candidate = null; return; }
                Ready = candidate; Epoch = requestedEpoch; ValidatedAt = Time.unscaledTime; Complete = true; ArrivalFramed = arrival;
                Reason = arrival ? "Validated exact stationary Journey arrival" : "Validated exact stationary Journey refuge";
                return;
            }
            lastFailure = kind + ": " + reason;
            // Retain this exact query while the observation owner supplies its
            // missing evidence. No pending result becomes a certified ending.
            if (kind == ScenicRejectionKind.SurveyIncomplete || kind == ScenicRejectionKind.Geometry || kind == ScenicRejectionKind.Acquisition)
            { Reason = "Waiting to certify the Journey refuge: " + lastFailure; return; }
            candidate = null;
            if (!arrivalFocus.HasValue) Finish("Journey endpoint refuge is not valid: " + lastFailure);
            else Reason = "Trying another observed arrival composition: " + lastFailure;
        }
    }

    private void Finish(string reason)
    { Complete = true; Ready = null; Reason = reason; }

    internal void Reset()
    {
        section = candidate = Ready = null; arrivalFocus = null; arrivalSubject = null; evaluator.Reset(); visibility.Reset();
        id = requestedEpoch = Epoch = 0; alternative = 0; aspect = nearClip = started = ValidatedAt = 0;
        moveCarrier = Complete = ArrivalFramed = false; lastFailure = "none"; Reason = "No journey ending requested";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
