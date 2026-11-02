using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Dedicated environmental production: measured subjects, exact-route
/// screening, prepared edits, actual-presentation timing and bounded recovery.</summary>
internal sealed class ScenicProduction
{
    private readonly ScenicDirector director = new ScenicDirector();
    private readonly ScenicFeatureSurvey survey = new ScenicFeatureSurvey();
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicShotReadiness readiness = new ScenicShotReadiness();
    private readonly ScenicShotReadiness liveSceneReadiness = new ScenicShotReadiness();
    private ScenicShotPlan? lastPresentedPlan;
    private Vector3 lastPresentedFocus;
    internal readonly ScenicVisibility Visibility = new ScenicVisibility();
    // Candidate surveys must never recenter the live camera's foliage cache.
    private readonly ScenicVisibility planningVisibility = new ScenicVisibility();
    private Vector3 origin;
    private long nextId;
    private ScenicShotPlan? testing, best;
    private bool searching, trimmed, forceReplacement, revalidating;
    private int attempts, accepted, sweep;
    private double bestScore;
    private float retryAt, preparedAt, proposed, bestValidatedAt;
    private string lastRejection = "none";
    private bool hasSettings, plannedComposition, plannedNative;
    private float plannedFov;
    internal bool HasRegion { get; private set; }
    internal ScenicShotPlan? Plan => director.Current;
    internal bool Searching => HasRegion && (!survey.Complete || searching);
    internal bool NeedsReplacement => forceReplacement || director.Current == null || director.NeedsReplacement;
    internal bool RecoveryExhausted { get; private set; }
    internal bool CanContinue => Plan != null && readiness.CanContinue(Plan.Id, Focus((float)director.Progress));
    internal bool SceneReady => HasRegion && (CanContinue || lastPresentedPlan != null &&
        liveSceneReadiness.CanContinue(lastPresentedPlan.Id, lastPresentedFocus)
        ? PlayerShotReadiness.PositionReady(origin, true) : IdleShots.Ready(origin));
    internal string Reason { get; private set; } = "Awaiting scenic region";
    internal string SearchStatus => !survey.Complete ? survey.Diagnostics :
        Reason + "; routes " + attempts + ", accepted " + accepted + "; last rejection " + lastRejection;
    internal int RejectedViews { get; private set; }
    internal string Diagnostics => $"Scenic director: {Reason}; {director.Reason}; " +
        $"feature {Plan?.FeatureKind.ToString() ?? "none"}/{Plan?.FeatureId ?? "none"}; role {Plan?.Role.ToString() ?? "none"}; " +
        $"shot {Plan?.Id ?? 0}; progress {director.Progress:P0}; shown {director.ElapsedSeconds:F1}/{Plan?.DurationSeconds ?? 0:F1}s; " +
        $"lens {Plan?.FieldOfView ?? 0:F0}; blocked {director.BlockedSeconds:F1}s; staged {director.Prepared?.Id ?? 0}; " +
        $"search {Searching}, routes {attempts}, accepted {accepted}, rejected {RejectedViews}; last rejection {lastRejection}; " +
        $"recovery exhausted {RecoveryExhausted}; {survey.Diagnostics}";

    internal void SetRegion(Vector3 value)
    {
        Clear(); origin = value; HasRegion = TargetRoster.Finite(value);
        if (HasRegion) survey.Begin(value);
    }
    internal void Clear()
    {
        director.Reset(); survey.Reset(); evaluator.Dispose(); readiness.Reset(); liveSceneReadiness.Reset();
        lastPresentedPlan = null; lastPresentedFocus = default; Visibility.Reset(); planningVisibility.Reset();
        HasRegion = searching = trimmed = forceReplacement = RecoveryExhausted = hasSettings = revalidating = false;
        testing = best = null; attempts = accepted = sweep = RejectedViews = 0;
        retryAt = preparedAt = proposed = 0; lastRejection = "none"; Reason = "Awaiting scenic region";
        // Plan identities remain unique across region/world resets.
    }
    internal void RequestShot()
    {
        forceReplacement = true; retryAt = 0;
        if (Plan != null) director.RequestReplacement("Entering fresh scenic coverage");
    }
    internal void ResumeShot()
    {
        // Returning from an ordinary wildlife cutaway is not an editorial
        // rejection of the existing scenic plan or its prepared successor.
        // Runtime validates the returning pose before it can be presented.
        retryAt = 0;
        if (Plan != null && !NeedsReplacement) Reason = "Resuming the prepared environmental sequence";
    }
    internal float Propose(float dt, BroadcastSettings settings, bool held)
    {
        proposed = (float)director.Propose(Mathf.Max(0, dt),
            settings.DynamicShots.Value && !held && settings.ShotMotion.Value > 0,
            Mathf.Clamp(settings.ShotMotion.Value, 0, 1.5f));
        return proposed;
    }
    internal Vector3 Focus(float fraction)
    {
        if (Plan == null) return origin;
        return FromCore(Plan.Sample(fraction).AimPosition);
    }
    internal Vector3 Offset => Plan == null ? Vector3.zero :
        FromCore(Plan.Sample(proposed).CameraPosition) - Focus(proposed);
    internal bool Observe(bool live, bool readable, long planId, float progress, bool blocked, bool held, string reason)
    {
        if (Plan == null) return false;
        // Keep actual scene availability separate from admission of a requested
        // new plan. The camera may still be rendering its preceding valid plan.
        // That frame can renew only its own scene lease, never the new plan's
        // progress, dwell, or acquisition evidence.
        ScenicShotPlan? shown = planId == Plan.Id ? Plan : lastPresentedPlan?.Id == planId ? lastPresentedPlan : null;
        if (live && readable && shown != null)
        {
            lastPresentedPlan = shown;
            lastPresentedFocus = FromCore(shown.Sample(progress).AimPosition);
            liveSceneReadiness.Presented(shown.Id, lastPresentedFocus);
        }
        // Missing/stale output must advance failure time for the newly selected
        // plan, but can never credit another plan's progress or readiness.
        if (planId != Plan.Id) { live = readable = blocked = false; progress = (float)director.Progress; }
        if (live) readiness.Presented(Plan.Id, Focus(progress));
        bool requested = director.NeedsReplacement;
        director.Observe(Plan.Id, Time.unscaledTime, Mathf.Clamp01(progress), live, readable, blocked, held);
        if (live && readable && !NeedsReplacement) RecoveryExhausted = false;
        if (blocked || !readable) { lastRejection = reason; Reason = reason; }
        if (!requested && director.NeedsReplacement) retryAt = 0;
        return !requested && director.NeedsReplacement;
    }

    internal bool TickSearch(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate = true)
    {
        if (!HasRegion || !SceneReady) return false;
        if (!hasSettings || plannedFov != settings.FieldOfView.Value ||
            plannedComposition != settings.DeliberateComposition.Value || plannedNative != settings.NativeSplineDolly.Value)
        {
            hasSettings = true; plannedFov = settings.FieldOfView.Value;
            plannedComposition = settings.DeliberateComposition.Value; plannedNative = settings.NativeSplineDolly.Value;
            director.TakePrepared(); testing = best = null; searching = false; evaluator.Reset(); retryAt = 0;
        }
        if (!survey.Complete)
        {
            survey.Step(); Reason = survey.Diagnostics;
            if (!survey.Complete) return false;
        }
        // Preparing never advances the current camera or earns presentation time.
        if (allowActivate && director.Prepared != null && (!held || Plan == null) &&
            (NeedsReplacement || director.ShouldPrefetch && Time.unscaledTime - preparedAt > 4f))
        {
            ScenicShotPlan staged = director.TakePrepared()!;
            // Runtime repeats exact-pose checks. An old staged path is screened
            // again before admission rather than trusting stale loaded objects.
            if (NeedsReplacement && Time.unscaledTime - preparedAt <= 6f) return Activate(staged);
            testing = staged; trimmed = revalidating = true; searching = true; attempts = 0; accepted = 0; best = null; bestScore = double.NegativeInfinity;
            BeginEvaluation(settings, moveCarrier); return false;
        }
        if (!searching && director.Prepared == null && (NeedsReplacement || director.ShouldPrefetch || director.ElapsedSeconds >= 2) && Time.unscaledTime >= retryAt)
        {
            searching = true; revalidating = false; testing = best = null; attempts = accepted = 0; bestScore = double.NegativeInfinity;
            Reason = NeedsReplacement ? "Planning a readable moving shot" : "Preparing the next environmental shot";
        }
        if (!searching) return false;
        if (testing == null)
        {
            if (attempts >= ScenicCandidateSchedule.AttemptLimit(survey.Features, CurrentFeatureIndex(), sweep) || survey.Features.Count == 0)
                return FinishSearch(held, allowActivate);
            testing = NextCandidate(settings);
            if (testing == null) return false;
            trimmed = false;
            BeginEvaluation(settings, moveCarrier);
        }
        if (!evaluator.Step(6)) return false;
        // Surface fitting produces a new immutable route. Keep that exact
        // route for ranking, trimming, staging and playback, never the original
        // below-surface proposal that was used to begin the search.
        testing = evaluator.EvaluatedPlan ?? testing;
        if (evaluator.Accepted)
        {
            if (director.CanOffer(testing!))
            {
                accepted++;
                double score = director.Rank(testing!, evaluator.Score);
                if (score > bestScore) { best = testing; bestScore = score; bestValidatedAt = Time.unscaledTime; }
            }
            else { RejectedViews++; lastRejection = "Validated route repeats completed feature coverage"; }
        }
        else
        {
            revalidating = false;
            RejectedViews++; lastRejection = evaluator.FailureKind + ": " + evaluator.Reason;
            // A useful prefix is a new immutable route and gets its own full
            // native screening. Never replay unexamined geometry after trimming.
            if (!trimmed && evaluator.ValidatedFraction >= .45f)
            {
                try
                {
                    ScenicShotPlan prefix = testing!.Trim(Math.Max(.1, evaluator.ValidatedFraction - .04));
                    if (prefix.TravelMetres >= 5 && prefix.VisualChange >= .12)
                    {
                        testing = prefix; trimmed = true; BeginEvaluation(settings, moveCarrier); return false;
                    }
                }
                catch (ArgumentException) { }
            }
        }
        testing = null;
        if (best != null && (Plan == null || revalidating || attempts >= 4 && bestScore >= .6)) return FinishSearch(held, allowActivate);
        revalidating = false;
        return false;
    }

    private ScenicShotPlan? NextCandidate(BroadcastSettings settings)
    {
        ScenicCandidateSchedule.Candidate candidate = ScenicCandidateSchedule.Select(survey.Features, CurrentFeatureIndex(), sweep, attempts++);
        ScenicFeature feature = survey.Features[candidate.FeatureIndex];
        ScenicShotRole preferred = director.PreferredRole(feature);
        ScenicShotRole role = ScenicCandidateSchedule.Role(feature.Kind, preferred, candidate.RoleIndex);
        int view = candidate.Viewpoint;
        try
        {
            var plan = ScenicShotFactory.Create(++nextId, feature, role, view, settings.FieldOfView.Value, settings.DeliberateComposition.Value, settings.NativeSplineDolly.Value);
            if (plan.TravelMetres < 5 || plan.VisualChange < .12)
            { RejectedViews++; lastRejection = "Route has insufficient visual development"; return null; }
            if (!director.CanOffer(plan))
            { RejectedViews++; lastRejection = "Feature sequence complete or viewpoint already shown"; return null; }
            return plan;
        }
        catch (ArgumentException)
        { RejectedViews++; lastRejection = "Feature does not support this shot geometry"; return null; }
    }
    private int CurrentFeatureIndex()
    {
        for (int i = 0; i < survey.Features.Count; i++)
            if (survey.Features[i].Id == Plan?.FeatureId) return i;
        return -1;
    }
    private void BeginEvaluation(BroadcastSettings settings, bool moveCarrier)
    {
        Camera? camera = Camera.main;
        float aspect = camera ? camera.aspect : Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        float nearClip = camera ? camera.nearClipPlane : .3f;
        evaluator.Begin(testing!, planningVisibility, aspect, nearClip, moveCarrier);
    }
    private bool FinishSearch(bool held, bool allowActivate)
    {
        searching = revalidating = false; testing = null; evaluator.Reset(); sweep = (sweep + 1) % 8;
        if (best == null)
        {
            RecoveryExhausted = NeedsReplacement;
            Reason = "No fully readable moving route; " + lastRejection;
            retryAt = Time.unscaledTime + 3f;
            return false;
        }
        ScenicShotPlan selected = best; best = null;
        director.Stage(selected); preparedAt = bestValidatedAt;
        Reason = "Prepared " + selected.FeatureKind + " / " + selected.Role;
        if (allowActivate && NeedsReplacement && (!held || Plan == null) && Time.unscaledTime - preparedAt <= 6f)
            return Activate(director.TakePrepared()!);
        return false;
    }
    private bool Activate(ScenicShotPlan selected)
    {
        if (!director.CanOffer(selected))
        { retryAt = 0; Reason = "Prepared view already covered; planning a fresh feature"; return false; }
        director.Accept(selected, Time.unscaledTime); readiness.Reset();
        RecoveryExhausted = forceReplacement = false; proposed = 0;
        Reason = "Cut to " + selected.FeatureKind + " / " + selected.Role;
        return true;
    }
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
