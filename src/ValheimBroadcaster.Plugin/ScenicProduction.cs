using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Dedicated environmental production: measured subjects, exact-route
/// screening, prepared edits, actual-presentation timing and bounded recovery.</summary>
internal sealed class ScenicProduction
{
    private readonly ScenicDirector director = new ScenicDirector();
    private readonly ScenicJourneyPolicy journey = new ScenicJourneyPolicy();
    private readonly ScenicJourneyDeparture departure = new ScenicJourneyDeparture();
    private readonly ScenicJourneyPlanner journeyPlanner = new ScenicJourneyPlanner();
    private long journeyPredecessor, journeyEpoch;
    private readonly List<ScenicShotPlan> journeyQueue = new List<ScenicShotPlan>(2);
    private float journeyRunSeconds, nextJourneyTrace;
    private bool journeyOwned;
    private float journeyRequestExpiresAt;
    private long skippedJourneySeed;
    private long tracedReplacementPlan;
    private string? tracedReplacementReason;
    private float nextJourneyAt, journeyEndpointWait = -1;
    private string? journeyEndingReason;
    private bool requestedJourney, journeyPlanning, manualJourney;
    private string journeyStatus = "No continuous journey requested";
    private bool lastMovementEnabled = true;
    private ScenicShotPlan? requestedFixture;
    private readonly ScenicPathEvaluator fixtureEvaluator = new ScenicPathEvaluator();
    private readonly ScenicVisibility fixtureVisibility = new ScenicVisibility("fixture-preflight");
    private bool fixtureEvaluating;
    private long fixturePlanId;
    private float fixtureRequestedAt, fixtureMotion = 1;
    private bool fixtureDynamic = true;
    internal long FixtureOwnerId => requestedFixture?.Id ?? (Plan?.Id == fixturePlanId ? fixturePlanId : 0);
    internal bool FixturePreparing => requestedFixture != null;
    internal bool FixtureActive => requestedFixture != null || fixturePlanId > 0 && Plan?.Id == fixturePlanId;
    internal string FixtureStatus { get; private set; } = "No camera fixture requested";
    internal bool JourneyActive => journeyOwned && journey.Active && HasUsablePicture && Plan?.JourneyId == journey.Id;
    internal string JourneyStatus => journeyStatus + $"; {(departure.Pending && !journeyOwned ? "Preparing (not on air)" : journey.Outcome.ToString())}; measured {journey.TravelMetres:F0}/{journey.TargetMetres:F0}m" +
        $"; displacement {journey.DisplacementMetres:F0}m; objective advance {journey.ObjectiveAdvanceMetres:F0}m; cruise {journey.CruiseSpeed:F1}m/s" +
        $"; achieved {journey.AchievedMetresPerSecond:F1}m/s; shown {journey.ShownSeconds:F1}s; queued {journeyQueue.Count}; ahead {journeyPlanner.Reason}";
    internal string RequestJourney()
    {
        if (journeyOwned) return JourneyStatus;
        requestedJourney = true; journeyRequestExpiresAt = Time.unscaledTime + 90; nextJourneyAt = 0; skippedJourneySeed = 0;
        if (!journey.Active) departure.Reset();
        else { manualJourney = true; requestedJourney = false; }
        return journeyStatus = "Journey requested; awaiting a validated departure from a readable moving shot (up to 90 seconds)";
    }
    internal void StopJourney()
    { FinishJourney("Journey stopped; resume ordinary local scenery", ScenicJourneyOutcome.Interrupted); }
    internal Vector3? JourneyLoadingPoint
    {
        get
        {
            if (!JourneyActive || Plan == null) return null;
            // This point lies on the CURRENT accepted route, not an unobserved
            // proposal. The observer independently checks loaded carrier geometry.
            double ahead = Math.Min(1, director.Progress + 24 / Math.Max(1, Plan.TravelMetres));
            return FromCore(Plan.Sample(ahead).CameraPosition);
        }
    }
    internal bool RequestFixture(ScenicShotPlan plan, out string reason, float playbackMotion = 1, bool playbackDynamic = true)
    {
        if (!HasRegion || plan == null || !plan.IsDirected || plan.IsPictureOnly ||
            float.IsNaN(playbackMotion) || float.IsInfinity(playbackMotion) || playbackMotion < 0 || playbackMotion > 1.5f)
        { reason = "A directed moving/pan fixture and an acquired world region are required"; return false; }
        StopJourney(); fixtureEvaluator.Reset(); fixtureEvaluating = false;
        requestedFixture = plan.AsStandalone().WithIdentity(++nextId); fixtureRequestedAt = Time.unscaledTime;
        fixtureMotion = playbackMotion; fixtureDynamic = playbackDynamic;
        reason = FixtureStatus = "Validating saved camera route while retaining the current picture"; return true;
    }
    internal bool TryGetFixturePlayback(long planId, out float motion, out bool dynamic)
    { motion = fixtureMotion; dynamic = fixtureDynamic; return fixturePlanId > 0 && fixturePlanId == planId; }
    internal void StopFixture()
    {
        requestedFixture = null; fixtureEvaluating = false; fixtureEvaluator.Reset();
        if (fixturePlanId > 0 && Plan?.Id == fixturePlanId) director.RequestReplacement("Camera fixture stopped; resume autonomous scenery");
        fixturePlanId = 0; FixtureStatus = "Camera fixture stopped";
    }
    private readonly ScenicPictureAvailability pictures = new ScenicPictureAvailability();
    private readonly ScenicVisibility pictureVisibility = new ScenicVisibility("first-picture");
    private readonly DestinationSubjectCoverage destinationSubject = new DestinationSubjectCoverage();
    private readonly ScenicFeatureSurvey destinationSurvey = new ScenicFeatureSurvey();
    private readonly List<ScenicFeature> destinationFeatures = new List<ScenicFeature>(32);
    private Vector3 destinationSurveyOrigin;
    private float nextDestinationSurvey;
    internal bool DestinationSubjectShown => destinationSubject.Shown;
    internal bool DestinationSubjectSatisfied => destinationSubject.Satisfied;
    internal bool DestinationSubjectUnavailable => destinationSubject.Unavailable;
    internal string DestinationSubjectStatus => destinationSubject.Site == null ? "No nominated destination subject" :
        destinationSubject.Site.Name + " / " + destinationSubject.Site.Id + "; destination subject footage " +
        destinationSubject.ShownSeconds.ToString("F1") + "/15s; establishing " + destinationSubject.EstablishedSeconds.ToString("F1") +
        "/6s; " + (destinationSubject.RequiresNpc ? "NPC detail " + destinationSubject.NpcSeconds.ToString("F1") + "/4s; " : "") +
        destinationSubject.CoverageScope + "; " +
        (destinationSubject.Satisfied ? "local subject sequence fulfilled" : destinationSubject.Unavailable ?
        "subject not covered within local search budget; landscape fallback" : "seeking readable coverage of the destination subject");
    private ScenicFeatureSurvey survey = new ScenicFeatureSurvey();
    private ScenicFeatureSurvey refreshSurvey = new ScenicFeatureSurvey();
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicSpatialPlanner spatial = new ScenicSpatialPlanner();
    private readonly List<ScenicFeature> subjects = new List<ScenicFeature>(32);
    private readonly Dictionary<string, float> subjectRetry = new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly ScenicShotReadiness readiness = new ScenicShotReadiness();
    private readonly ScenicShotReadiness liveSceneReadiness = new ScenicShotReadiness();
    private readonly ScenicRecoveryPolicy recovery = new ScenicRecoveryPolicy();
    private ScenicShotPlan? lastPresentedPlan;
    private bool usingIndependentPicture;
    private long motionPlanId;
    private float lastMotionProgress;
    private Vector3 lastPresentedFocus;
    private Vector3 lastAdmittedFocus;
    internal readonly ScenicVisibility Visibility = new ScenicVisibility("live");
    // Candidate surveys must never recenter the live camera's foliage cache.
    private readonly ScenicVisibility planningVisibility = new ScenicVisibility("preflight");
    private Vector3 origin;
    private long nextId;
    private long bestEpoch, preparedEpoch;
    private ScenicShotPlan? testing, best;
    private bool searching, planning, trimmed, elevated, forceReplacement, revalidating, deadlineHandled;
    private string? testingKey;
    private int attempts, accepted, sweep, sections, elevations;
    private double bestScore;
    private float retryAt, preparedAt, proposed, bestValidatedAt;
    private float surveyRefreshAt;
    private bool refreshingSurvey;
    private string lastRejection = "none";
    private bool hasSettings, plannedComposition, plannedNative;
    private float plannedFov;
    internal bool HasRegion { get; private set; }
    internal Vector3 RegionOrigin => origin;
    internal ScenicShotPlan? Plan => usingIndependentPicture ? pictures.Current : director.Current;
    internal bool ContentExhausted => pictures.ContentExhausted;
    internal bool RelocationBoundary => usingIndependentPicture
        ? pictures.CanYield(Time.unscaledTime)
        : director.Current == null || director.Progress >= .999999 || director.BlocksMovement;
    private double ShownSeconds => usingIndependentPicture ? pictures.ShownSeconds : director.ElapsedSeconds;
    internal bool Searching => HasRegion && (!survey.Complete || searching);
    internal bool NeedsReplacement => forceReplacement || director.Current == null || director.NeedsReplacement;
    internal bool RecoveryExhausted { get; private set; }
    internal bool RecoveryExpired => recovery.Expired;
    internal bool HasUsablePicture { get; private set; }
    internal bool CanContinue => Plan != null && (readiness.CanContinue(Plan.Id, lastAdmittedFocus) ||
        Plan.ContinuationOf > 0 && lastPresentedPlan?.Id == Plan.ContinuationOf &&
        liveSceneReadiness.CanContinue(lastPresentedPlan.Id, lastPresentedFocus));
    internal bool SceneReady => HasRegion && (CanContinue || lastPresentedPlan != null &&
        liveSceneReadiness.CanContinue(lastPresentedPlan.Id, lastPresentedFocus)
        ? PlayerShotReadiness.PositionReady(origin, true) : IdleShots.Ready(origin));
    internal string Reason { get; private set; } = "Awaiting scenic region";
    private string RecoveryStatus => recovery.Active ?
        $"; no usable idle picture {recovery.ElapsedSeconds:F1}s; " +
        (recovery.Expired ? "local recovery budget exhausted" : $"recovery budget {recovery.RemainingSeconds:F1}s") : "";
    internal string SearchStatus => (!survey.Complete ? survey.Diagnostics :
        Reason + "; routes " + attempts + ", accepted " + accepted + ", clear sections " + sections +
        ", canopy alternatives " + elevations + "; last rejection " + lastRejection +
        (planning ? "; " + spatial.Reason : "")) + RecoveryStatus;
    internal int RejectedViews { get; private set; }
    internal string Diagnostics => $"Scenic director: {Reason}; {director.Reason}; " +
        $"feature {Plan?.FeatureKind.ToString() ?? "none"}/{Plan?.FeatureId ?? "none"}; role {Plan?.Role.ToString() ?? "none"}; " +
        $"shot {Plan?.Id ?? 0}; progress {(Plan?.IsPictureOnly == true ? 0 : director.Progress):P0}; shown {ShownSeconds:F1}/{Plan?.DurationSeconds ?? 0:F1}s; " +
        $"independent picture {Plan?.IsPictureOnly == true}; settled coverage {pictures.TotalPictureSeconds:F1}s; content exhausted {ContentExhausted}; " +
        $"lens {Plan?.FieldOfView ?? 0:F0}; blocked {director.BlockedSeconds:F1}s; staged {director.Prepared?.Id ?? 0}; " +
        $"search {Searching}, routes {attempts}, accepted {accepted}, rejected {RejectedViews}; last rejection {lastRejection}; " +
        $"recovery exhausted {RecoveryExhausted}{RecoveryStatus}; {spatial.Diagnostics}; {survey.Diagnostics}; " +
        PlaceSurvey.Shared.Diagnostics + "; " + CameraSpatialScene.Shared.Diagnostics + "; destination: " + DestinationSubjectStatus + "; " + JourneyStatus + "; " + FixtureStatus;

    internal void SetRegion(Vector3 value)
    {
        Clear(); origin = value; HasRegion = TargetRoster.Finite(value);
        if (HasRegion) { ScenicFoliageScene.Shared.SetRegion(value); survey.Begin(value); }
        if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("region"), "origin", value));
    }
    internal void SetDestinationSubject(WorldInterestSite? site)
    {
        if (!destinationSubject.Set(site)) return;
        destinationSurvey.Reset(); destinationFeatures.Clear(); nextDestinationSurvey = 0;
        if (destinationSubject.Site != null && HasRegion)
        {
            WorldInterestSite target = destinationSubject.Site;
            Vector3 center = new Vector3(target.Center.X, target.Center.Y, target.Center.Z);
            // Broad settlements are approached at a particular local area. Do
            // not demand an unloaded whole-settlement centroid to tell its story.
            if (Vector3.ProjectOnPlane(center - origin, Vector3.up).sqrMagnitude > 64f * 64f)
                center = origin;
            destinationSurveyOrigin = center; destinationSurvey.Begin(center);
        }
        // A subject change invalidates only unshown preparation. A readable
        // current picture can remain on air while its purposeful replacement is prepared.
        director.TakePrepared(); testing = best = null; searching = planning = false;
        evaluator.Reset(); spatial.Reset(); subjectRetry.Clear(); retryAt = 0;
        if (Plan != null && destinationSubject.Prioritize) director.RequestEdit("Preparing coverage of the selected destination");
    }
    internal void Clear()
    {
        StopFixture(); StopJourney(); journey.Reset(); nextJourneyAt = 0;
        fixtureEvaluator.Dispose(); fixtureVisibility.Reset(); journeyPlanner.Dispose();
        destinationSubject.Reset(); destinationSurvey.Reset(); destinationFeatures.Clear(); nextDestinationSurvey = 0;
        director.Reset(); pictures.Reset(); pictureVisibility.Reset(); survey.Reset(); refreshSurvey.Reset(); evaluator.Dispose(); spatial.Reset(); subjects.Clear(); subjectRetry.Clear();
        refreshingSurvey = false; surveyRefreshAt = 0;
        readiness.Reset(); liveSceneReadiness.Reset(); recovery.Reset(); testingKey = null;
        lastPresentedPlan = null; lastPresentedFocus = lastAdmittedFocus = default; Visibility.Reset(); planningVisibility.Reset();
        usingIndependentPicture = false; motionPlanId = 0; lastMotionProgress = 0;
        HasRegion = searching = planning = trimmed = elevated = forceReplacement = RecoveryExhausted = hasSettings = revalidating = deadlineHandled = false;
        HasUsablePicture = false;
        testing = best = null; attempts = accepted = sweep = RejectedViews = 0;
        sections = elevations = 0;
        retryAt = preparedAt = proposed = 0; lastRejection = "none"; Reason = "Awaiting scenic region";
        bestEpoch = preparedEpoch = 0;
        // Plan identities remain unique across region/world resets.
    }
    internal void RequestShot()
    {
        forceReplacement = true; retryAt = 0;
        if (Plan != null) director.RequestEdit("Entering fresh scenic coverage");
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
        if (usingIndependentPicture) return proposed = 0;
        bool explicitFixture = fixturePlanId > 0 && Plan?.Id == fixturePlanId;
        bool travelling = journeyOwned && journey.Active;
        float motion = travelling ? 1 : explicitFixture ? fixtureMotion : Mathf.Clamp(settings.ShotMotion.Value, 0, 1.5f);
        lastMovementEnabled = (explicitFixture ? fixtureDynamic : settings.DynamicShots.Value) && motion > 0;
        double step = travelling ? ScenicJourneyPolicy.SafeDeltaSeconds(Mathf.Max(0, dt), journey.CruiseSpeed) : Mathf.Max(0, dt);
        proposed = (float)director.Propose(step, lastMovementEnabled && !held, motion);
        return proposed;
    }
    internal Vector3 Focus(float fraction)
    {
        if (Plan == null) return origin;
        return FromCore(Plan.Sample(fraction).AimPosition);
    }
    internal Vector3 Offset => Plan == null ? Vector3.zero :
        FromCore(Plan.Sample(proposed).CameraPosition) - Focus(proposed);
    internal bool Observe(bool live, bool readable, long planId, float progress, bool blocked, bool held, string reason, Vector3? actualFocus, Vector3? actualCamera = null)
    {
        if (Plan == null) return false;
        // Keep actual scene availability separate from admission of a requested
        // new plan. The camera may still be rendering its preceding valid plan.
        // That frame can renew only its own scene lease, never the new plan's
        // progress, dwell, or acquisition evidence.
        ScenicShotPlan? shown = planId == Plan.Id ? Plan : lastPresentedPlan?.Id == planId ? lastPresentedPlan : null;
        bool validFocus = actualFocus.HasValue && TargetRoster.Finite(actualFocus.Value);
        if (live && readable && shown != null && validFocus)
        {
            lastPresentedPlan = shown;
            lastPresentedFocus = actualFocus!.Value;
            liveSceneReadiness.Presented(shown.Id, lastPresentedFocus);
            if (!shown.IsPictureOnly)
            {
                // A requested cut can still be rendering the old independent
                // picture. Release that lease only after the actual route ID
                // is shown, and reset fallback dwell only for forward motion.
                if (!usingIndependentPicture && pictures.Current != null) pictures.ClearCurrent();
                if (!held && progress > .01f && (motionPlanId != shown.Id || progress > lastMotionProgress + .00001f))
                    pictures.MotionPresented();
                motionPlanId = shown.Id; lastMotionProgress = progress;
            }
        }
        if (shown?.IsPictureOnly == true)
            director.ObserveSitePicture(shown, Time.unscaledTime, live && validFocus, readable, held);
        pictures.Observe(planId, Time.unscaledTime, live && readable && validFocus, held,
            shown != null && !shown.IsPictureOnly && NeedsReplacement);
        // Missing/stale output must advance failure time for the newly selected
        // plan, but can never credit another plan's progress or readiness.
        if (planId != Plan.Id) { live = readable = blocked = false; progress = (float)director.Progress; }
        if (live && validFocus)
        { lastAdmittedFocus = actualFocus!.Value; readiness.Presented(Plan.Id, lastAdmittedFocus); }
        bool movementBlocked = director.BlocksMovement;
        ScenicShotPlan? route = director.Current;
        if (route != null)
            director.Observe(route.Id, Time.unscaledTime, planId == route.Id ? Mathf.Clamp01(progress) : (float)director.Progress,
                live && planId == route.Id, readable && planId == route.Id, blocked && planId == route.Id, held, lastMovementEnabled);
        if (route != null) journey.Observe(route, Time.unscaledTime, planId == route.Id ? Mathf.Clamp01(progress) : (float)director.Progress,
            live && readable && planId == route.Id, held, actualCamera.HasValue ?
                new AutonomyPosition(actualCamera.Value.x, actualCamera.Value.y, actualCamera.Value.z) : (AutonomyPosition?)null);
        if (live && readable && !NeedsReplacement) RecoveryExhausted = false;
        if (blocked || !readable) { lastRejection = reason; Reason = reason; }
        if (!movementBlocked && director.BlocksMovement)
        {
            retryAt = 0;
            // A graph route can pass preflight repeatedly yet never supply
            // valid moving output. That live failure is also evidence of
            // exhausted local motion; successful restaging cannot conceal it.
            if (route != null && (planId != route.Id || !live || !readable || blocked)) pictures.RouteUnavailable();
        }
        TraceReplacement();
        return !movementBlocked && director.BlocksMovement;
    }

    private long pixelReplannedId;
    internal bool ObservePixelConcern(long planId, bool reconsider, string reason)
    {
        if (!reconsider || planId <= 0 || planId == pixelReplannedId ||
            Plan?.Id != planId) return false;
        pixelReplannedId = planId;
        director.RequestEdit("Measured picture concern; prepare a different local composition: " + reason);
        retryAt = 0;
        // Darkness, fog and poor contrast never invalidate the scene lease,
        // erase the readable current view, or request autonomous relocation.
        return true;
    }

    internal void UpdateRecovery(bool onAir, bool usableLive, long planId, bool held)
    {
        // Expire preparation even while the region is unavailable and normal
        // Scenic search cannot run. A pending fixture is not an indefinite
        // exemption from missing-picture recovery or autonomous travel.
        if (requestedFixture != null && Time.unscaledTime - fixtureRequestedAt > 20)
        {
            requestedFixture = null; fixtureEvaluating = false; fixtureEvaluator.Reset();
            FixtureStatus = "Camera fixture rejected: preparation exceeded 20 seconds";
        }
        if (!onAir && journey.Active) FinishJourney("Journey yielded to player or operator coverage");
        bool knownPicture = planId > 0 && (Plan?.Id == planId || lastPresentedPlan?.Id == planId);
        HasUsablePicture = onAir && usableLive && knownPicture;
        ScenicShotPlan? actualPlan = Plan?.Id == planId ? Plan : lastPresentedPlan?.Id == planId ? lastPresentedPlan : null;
        destinationSubject.Observe(Time.unscaledTime, onAir, HasUsablePicture, held, actualPlan?.Feature, actualPlan?.Role ?? ScenicShotRole.Establish);
        recovery.Observe(Time.unscaledTime, onAir && HasRegion, HasUsablePicture, held);
        ScenicTrace.Picture(onAir && HasRegion, HasUsablePicture, held, planId, (float)recovery.ElapsedSeconds, recovery.Expired, Reason);
        if (!recovery.Active) deadlineHandled = false;
        if (usableLive && knownPicture) RecoveryExhausted = false;
        else if (recovery.Expired) RecoveryExhausted = true;
    }

    internal bool TickSearch(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate = true)
    {
        if (!HasRegion || !SceneReady) return false;
        TraceReplacement();
        if (FixtureActive && TickFixture(settings, moveCarrier, held, allowActivate, out bool fixtureChanged)) return fixtureChanged;
        if (TickJourney(settings, moveCarrier, held, allowActivate, out bool journeyChanged)) return journeyChanged;
        PlaceSurvey.Shared.Update(origin, Time.unscaledTime);
        if (destinationSubject.Site != null)
        {
            if (destinationSubject.Prioritize && destinationSurvey.Complete && Time.unscaledTime >= nextDestinationSurvey)
                destinationSurvey.Begin(destinationSurveyOrigin);
            if (!destinationSurvey.Complete && destinationSurvey.Step())
            {
                destinationFeatures.Clear();
                foreach (ScenicFeature feature in destinationSurvey.Features)
                    if (destinationSubject.Matches(feature) && destinationFeatures.Count < 16) destinationFeatures.Add(feature);
                nextDestinationSurvey = Time.unscaledTime + 10;
                // Refreshing observations is not an editorial replacement.
                // CaptureSubjects feeds these observations into the next bounded
                // search. Until a candidate is validated, retain current motion
                // and its already prepared successor even if this refresh is
                // empty, unchanged or does not match the currently shown feature.
                if (destinationSubject.Prioritize) retryAt = 0;
                if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("destination-observation")
                    .Field("features", destinationFeatures.Count).Field("prioritize", destinationSubject.Prioritize)
                    .Field("currentMatches", Plan != null && destinationSubject.Matches(Plan.Feature))
                    .Field("plan", Plan?.Id ?? 0).Field("prepared", director.Prepared?.Id ?? 0));
            }
        }
        // Finish the outstanding sweep once the on-air search budget expires.
        // Region travel may be unavailable/cooling down; bounded later sweeps
        // may still find footage, but never renew the expired output deadline.
        if (allowActivate && recovery.Expired && !deadlineHandled)
        {
            deadlineHandled = true;
            return FinishSearch(held, allowActivate);
        }
        if (!hasSettings || plannedFov != settings.FieldOfView.Value ||
            plannedComposition != settings.DeliberateComposition.Value || plannedNative != settings.NativeSplineDolly.Value)
        {
            if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("settings").Field("fov", settings.FieldOfView.Value)
                .Field("composition", settings.DeliberateComposition.Value).Field("nativeSpline", settings.NativeSplineDolly.Value)
                .Field("dynamic", settings.DynamicShots.Value).Field("motion", settings.ShotMotion.Value));
            hasSettings = true; plannedFov = settings.FieldOfView.Value;
            plannedComposition = settings.DeliberateComposition.Value; plannedNative = settings.NativeSplineDolly.Value;
            director.TakePrepared(); testing = best = null; searching = planning = false;
            evaluator.Reset(); spatial.Reset(); subjectRetry.Clear(); retryAt = 0;
        }
        if (!survey.Complete)
        {
            survey.Step(); Reason = survey.Diagnostics;
            if (!survey.Complete) return false;
        }
        RefreshEnvironment();
        // Preparing never advances the current camera or earns presentation time.
        if (allowActivate && director.Prepared != null && (!held || Plan == null) && pictures.CanYield(Time.unscaledTime) &&
            (NeedsReplacement || director.ShouldPrefetch && Time.unscaledTime - preparedAt > 4f))
        {
            ScenicShotPlan staged = director.TakePrepared()!;
            // Runtime repeats exact-pose checks. An old staged path is screened
            // again before admission rather than trusting stale loaded objects.
            if (NeedsReplacement && Time.unscaledTime - preparedAt <= 6f && preparedEpoch == CameraSpatialScene.Shared.Epoch)
                return Activate(staged, preparedEpoch);
            testing = staged; revalidating = true; trimmed = elevated = false; searching = true; attempts = 0; accepted = 0; best = null; bestScore = double.NegativeInfinity;
            planning = false; spatial.Reset(); testingKey = null;
            sections = elevations = 0;
            BeginEvaluation(settings, moveCarrier); return false;
        }
        if (!searching && director.Prepared == null && (NeedsReplacement || director.ShouldPrefetch || director.ElapsedSeconds >= 2) && Time.unscaledTime >= retryAt)
        {
            searching = true; revalidating = false; testing = best = null; attempts = accepted = 0; bestScore = double.NegativeInfinity;
            planning = false; spatial.Reset(); CaptureSubjects();
            sections = elevations = 0;
            Reason = NeedsReplacement ? "Planning a readable moving shot" : "Preparing the next environmental shot";
        }
        if (!searching) return false;
        if (testing == null)
        {
            if (!planning)
            {
                if (subjects.Count == 0 || attempts >= ScenicCandidateSchedule.SpatialAttemptLimit(subjects, CurrentFeatureIndex(), sweep))
                    return FinishSearch(held, allowActivate);
                if (!BeginSpatialCandidate(settings, moveCarrier, recovery.Active && allowActivate)) return false;
            }
            if (recovery.Active && allowActivate) spatial.PromoteUrgent();
            bool spatialComplete = spatial.Step(recovery.Active && allowActivate ? 8 : 4);
            bool admittedPicture = allowActivate && (!held || Plan == null) && TryIndependentPicture(moveCarrier);
            if (!spatialComplete) return admittedPicture;
            planning = false; testing = spatial.Plan;
            if (testing == null)
            {
                RejectedViews++; lastRejection = spatial.Reason; RememberFailure();
                return admittedPicture;
            }
            if (!director.CanOffer(testing))
            { RejectedViews++; lastRejection = "Measured route repeats completed coverage"; testing = null; RememberFailure(); return false; }
            trimmed = elevated = false;
            BeginEvaluation(settings, moveCarrier);
        }
        if (!evaluator.Step(recovery.Active && allowActivate ? 12 : 6)) return false;
        // Surface fitting produces a new immutable route. Keep that exact
        // route for ranking, trimming, staging and playback, never the original
        // below-surface proposal that was used to begin the search.
        testing = evaluator.EvaluatedPlan ?? testing;
        if (evaluator.Accepted)
        {
            if (director.CanOffer(testing!))
            {
                accepted++;
                double score = director.Rank(testing!, evaluator.Score) + (testing!.Feature == null ? 0 : destinationSubject.Priority(testing.Feature));
                if (score > bestScore)
                { best = testing; bestScore = score; bestValidatedAt = Time.unscaledTime; bestEpoch = evaluator.SceneEpoch; }
            }
            else { RejectedViews++; lastRejection = "Validated route repeats completed feature coverage"; }
        }
        else
        {
            revalidating = false;
            RejectedViews++; lastRejection = evaluator.FailureKind + ": " + evaluator.Reason;
            // Find movement from measured clear space, including an interior
            // or suffix after an obstructed opening. Never bridge an invalid
            // pose/corridor, and fully validate the regenerated native segment.
            if (!trimmed && evaluator.ReadableWindow.HasWindow && evaluator.ReadableWindow.TravelMetres >= 5)
            {
                try
                {
                    ScenicShotPlan section = evaluator.ProposeReadableSection();
                    if (section.TravelMetres >= 5 && section.VisualChange >= .12)
                    {
                        testing = section; trimmed = true; sections++; BeginEvaluation(settings, moveCarrier); return false;
                    }
                }
                catch (ArgumentException) { }
            }
            // A canopy intersection supplies a measured alternative elevation,
            // rather than repeating every azimuth at the same guessed height.
            // This is a proposal, not permission to show a known-obscured view.
            if (!elevated && evaluator.SuggestedCanopyLift > 0)
            {
                try
                {
                    ScenicShotPlan raised = testing!.RaiseCamera(evaluator.SuggestedCanopyLift);
                    if (raised.VisualChange >= .12)
                    { testing = raised; elevated = true; elevations++; BeginEvaluation(settings, moveCarrier); return false; }
                }
                catch (ArgumentException) { }
            }
            RememberFailure();
        }
        testing = null;
        if (best != null && (recovery.Active && allowActivate || Plan == null || revalidating || attempts >= 4 && bestScore >= .6))
            return FinishSearch(held, allowActivate);
        revalidating = false;
        return false;
    }

    private bool TickFixture(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        if (!settings.ProceduralPlayers.Value)
        { StopFixture(); FixtureStatus = "Camera fixture requires the procedural camera pipeline"; return false; }
        if (requestedFixture == null)
        {
            if (Plan?.Id != fixturePlanId || director.NeedsReplacement)
            { fixturePlanId = 0; FixtureStatus = "Camera fixture finished or interrupted: " + director.Reason; return false; }
            FixtureStatus = $"Playing camera fixture; progress {director.Progress:P0}; shown {director.ElapsedSeconds:F1}s";
            return true;
        }
        if (Time.unscaledTime - fixtureRequestedAt > 20)
        { requestedFixture = null; fixtureEvaluating = false; fixtureEvaluator.Reset(); FixtureStatus = "Camera fixture rejected: preparation exceeded 20 seconds"; return false; }
        if (!fixtureEvaluating)
        {
            Camera? camera = Camera.main;
            fixtureEvaluator.Begin(requestedFixture, fixtureVisibility, camera ? camera.aspect : 16f / 9f,
                camera ? camera.nearClipPlane : .3f, moveCarrier); fixtureEvaluating = true;
        }
        if (!fixtureEvaluator.Step(6)) { FixtureStatus = fixtureEvaluator.Reason; return true; }
        if (!fixtureEvaluator.Accepted || fixtureEvaluator.EvaluatedPlan == null)
        { FixtureStatus = "Camera fixture rejected: " + fixtureEvaluator.Reason; requestedFixture = null; fixtureEvaluating = false; return false; }
        if (fixtureEvaluator.EvaluatedPlan.Sample(0).CameraPosition.Distance(requestedFixture.Sample(0).CameraPosition) > .001)
        { requestedFixture = null; fixtureEvaluating = false; FixtureStatus = "Camera fixture rejected: current terrain requires changing its saved camera path"; return false; }
        if (!allowActivate || held) return true;
        ScenicShotPlan selected = fixtureEvaluator.EvaluatedPlan;
        if (fixtureEvaluator.SceneEpoch != CameraSpatialScene.Shared.Epoch)
        { fixtureEvaluating = false; return true; }
        // Explicit playback may repeat a saved composition, but follows the
        // exact same full surface/path/foliage admission and live checks.
        director.TakePrepared(); director.Accept(selected, Time.unscaledTime); readiness.Reset();
        fixturePlanId = selected.Id; requestedFixture = null; fixtureEvaluating = false;
        usingIndependentPicture = false; forceReplacement = false; proposed = 0;
        searching = planning = false; testing = best = null; evaluator.Reset(); spatial.Reset();
        FixtureStatus = "Saved camera fixture admitted after full current-scene validation";
        changed = true; return true;
    }

    private bool TickJourney(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        ScenicShotPlan? current = director.Current;
        if (!moveCarrier || !settings.ProceduralPlayers.Value || !settings.ScenicJourneys.Value && !requestedJourney && !manualJourney || !settings.DynamicShots.Value)
        { if (journey.Active || requestedJourney) StopJourney(); return false; }
        if (journeyOwned && !journey.Active)
        { FinishJourney(journey.Reason, journey.Outcome); return false; }
        if (!journeyOwned) return TickDeparture(settings, current, moveCarrier, held, allowActivate, out changed);
        if (current == null || usingIndependentPicture || current.Id != journeyPredecessor && current.JourneyId != journey.Id)
        { FinishJourney("Journey interrupted by a different scenic owner"); return false; }
        if (current.IsJourney && HasUsablePicture)
        {
            Camera? liveCamera = Camera.main;
            if (liveCamera && (liveCamera.transform.position - origin).sqrMagnitude > 24 * 24)
            {
                origin = liveCamera.transform.position; ScenicFoliageScene.Shared.SetRegion(origin);
                survey.Begin(origin); refreshSurvey.Reset(); refreshingSurvey = false; surveyRefreshAt = 0;
                PlaceSurvey.Shared.Update(origin, Time.unscaledTime);
            }
        }
        if (director.NeedsReplacement && director.Progress < .999999)
        { FinishJourney("Journey interrupted: " + director.Reason); return false; }
        if (!allowActivate || held)
        {
            // These deadlines use wall time. Restart pending work on resume,
            // preserving the accepted section and already prepared successors.
            journeyPlanner.Reset(); journeyPlanning = false; journeyEndpointWait = -1;
            return true;
        }
        journeyRunSeconds += Time.unscaledDeltaTime;
        bool exhausted = journey.BudgetExpired || journeyRunSeconds > journey.MaximumSeconds + 30;
        if (journey.TargetReached || exhausted)
        {
            journeyQueue.Clear(); journeyPlanner.Reset(); journeyPlanning = false;
            if (director.Progress < .999999) { journeyStatus = "Finishing the current validated section"; return true; }
            FinishJourney(journey.TargetReached ? "Distance and forward-progress objective reached" : "Journey stopped before its objective: bounded runtime expired",
                journey.TargetReached ? ScenicJourneyOutcome.Completed : ScenicJourneyOutcome.BudgetExpired); return false;
        }
        if (journeyEpoch != CameraSpatialScene.Shared.Epoch)
        { journeyQueue.Clear(); journeyPlanner.Reset(); journeyPlanning = false; journeyEndingReason = null;
            journeyEndpointWait = -1; journeyEpoch = CameraSpatialScene.Shared.Epoch; }
        // One on-air section plus up to two prepared successors supplies a
        // speed-scaled horizon without requiring one unacquirable giant route.
        if (journeyPlanning)
        {
            journeyPlanner.Step(); journeyStatus = journeyPlanner.Reason;
            if (journeyPlanner.Complete)
            {
                if (journeyPlanner.Ready != null && journeyPlanner.Epoch == CameraSpatialScene.Shared.Epoch)
                    journeyQueue.Add(journeyPlanner.Ready);
                else journeyEndingReason = "No validated continuation: " + journeyPlanner.Reason;
                journeyPlanner.Reset(); journeyPlanning = false;
            }
        }
        if (director.Progress >= .999999 && journeyQueue.Count > 0)
        {
            ScenicShotPlan next = journeyQueue[0]; journeyQueue.RemoveAt(0);
            if (next.ContinuationOf != current.Id)
            { FinishJourney("Prepared journey lost its predecessor identity"); return false; }
            director.Accept(next, Time.unscaledTime); proposed = 0; forceReplacement = false;
            usingIndependentPicture = false; journeyPredecessor = next.Id; journeyEndpointWait = -1;
            journeyStatus = "Traversing the validated route toward the landscape objective";
            changed = true; current = next;
        }
        if (journeyEndingReason != null && journeyQueue.Count == 0 && director.Progress >= .999999)
        { FinishJourney(journeyEndingReason, ScenicJourneyOutcome.Blocked); return false; }
        double currentRemaining = current.IsJourney ? current.TravelMetres * (1 - current.MotionFraction(director.Progress)) : 0;
        double committed = currentRemaining;
        foreach (ScenicShotPlan queued in journeyQueue) committed += queued.TravelMetres;
        double metres = journey.NextWindowMetres(committed);
        if (!journeyPlanning && journeyEndingReason == null && journeyQueue.Count < 2 && metres >= ScenicJourneyPolicy.MinimumWindowMetres &&
            (journeyQueue.Count == 0 || committed < ScenicJourneyPolicy.RequiredLookaheadMetres(journey.CruiseSpeed)))
        {
            ScenicShotPlan predecessor = journeyQueue.Count > 0 ? journeyQueue[journeyQueue.Count - 1] : current;
            Camera? camera = Camera.main;
            Vector3 seam = FromCore(predecessor.Sample(1).CameraPosition);
            // Do not spend the preparation deadline on a corridor which cannot
            // yet fit inside the live carrier's certified acquisition reach.
            // As the current accepted route develops, the same seam approaches.
            if (Player.m_localPlayer && Vector3.Distance(Player.m_localPlayer.transform.position, seam) + metres <= 85)
            {
                journeyPlanner.Begin(predecessor, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
                    camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier,
                    committed + metres >= journey.RemainingMetres - .5, journey.ObjectiveGoal);
                journeyPlanning = true;
            }
            else journeyStatus = current.IsJourney ? "Advancing the validated route to extend its loading horizon" :
                "Finishing the existing local shot while acquiring the journey start";
        }
        if (director.Progress >= .999999 && journeyQueue.Count == 0)
        {
            if (journeyEndpointWait < 0) journeyEndpointWait = Time.unscaledTime;
            if (Time.unscaledTime - journeyEndpointWait > 6)
            { FinishJourney("Continuation not ready; short/partial journey retained as incomplete", ScenicJourneyOutcome.Blocked); return false; }
        }
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return true;
    }

    // This preparation returns false until the departure is actually admitted.
    // Ordinary search, prepared successors, movement settings and editorial
    // travel therefore remain authoritative while no journey picture exists.
    private bool TickDeparture(BroadcastSettings settings, ScenicShotPlan? current, bool moveCarrier,
        bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        if ((requestedJourney || manualJourney) && held)
            journeyRequestExpiresAt += Mathf.Max(0, Time.unscaledDeltaTime);
        if ((requestedJourney || manualJourney) && Time.unscaledTime >= journeyRequestExpiresAt)
        {
            requestedJourney = manualJourney = false;
            if (journey.Active) AbandonDeparture("Journey not started: no validated departure within the 90-second request window");
            else journeyStatus = "Journey not started: no validated departure within the 90-second request window";
            return false;
        }
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (!journey.Active)
        {
            if (!allowActivate || held || !HasUsablePicture || usingIndependentPicture || current == null || director.NeedsReplacement ||
                !requestedJourney && (Time.unscaledTime < nextJourneyAt || director.RememberedShots < 2)) return false;
            if (current.HasRejectedContinuation)
            {
                if (skippedJourneySeed != current.Id)
                {
                    skippedJourneySeed = current.Id;
                    journeyStatus = "Journey departure skipped: current route ends before a known rejected continuation: " + current.ContinuationRejection;
                    ScenicTrace.Event("journey-departure", journeyStatus, current.Id);
                }
                return false;
            }
            if (!ScenicJourneyPolicy.Eligible(current) || !departure.CanAttempt(current.Id, epoch)) return false;
            if (settings.ShotMotion.Value <= 0)
            { journeyStatus = "Journey waiting for a moving seed to reach its endpoint; ordinary camera motion is disabled"; return false; }
            // A ready departure must remain fresh until its seam. Beginning at
            // the start of an ordinary 30-second shot would consume the bounded
            // preparation lifetime before that seam could possibly arrive.
            double seedSeconds = current.DurationSeconds * (1 - director.Progress) / settings.ShotMotion.Value;
            if (seedSeconds > ScenicJourneyDeparture.PreparationSeconds - 2)
            { journeyStatus = "Journey awaiting the existing shot's final ten seconds before preparing its departure"; return false; }
            if (director.Prepared == null)
            { journeyStatus = "Journey awaiting a prepared ordinary successor before spending speculative route work"; return false; }
            try
            {
                departure.Begin(current.Id, epoch);
                journey.Start(++nextId, settings.ScenicJourneyDistance.Value, settings.ScenicJourneySpeed.Value,
                    current.Sample(1).CameraPosition, ScenicJourneyPlanner.ObjectiveHeading(current));
            }
            catch (ArgumentException)
            {
                journeyStatus = "Journey not started: no bounded objective from this seed";
                departure.Abandon(journeyStatus); nextJourneyAt = Time.unscaledTime + 8; return false;
            }
            manualJourney = requestedJourney; requestedJourney = false; journeyPredecessor = current.Id;
            journeyPlanning = false; journeyEndingReason = null; journeyEndpointWait = -1; journeyRunSeconds = 0;
            journeyEpoch = epoch; journeyQueue.Clear();
            journeyStatus = departure.Reason; nextJourneyTrace = 0; TraceJourney("departure-start");
        }
        bool validSeed = current != null && !usingIndependentPicture && HasUsablePicture && current.Id == journeyPredecessor &&
            epoch == journeyEpoch && (!director.NeedsReplacement || director.Progress >= .999999);
        if (!validSeed || !allowActivate)
        { AbandonDeparture("Journey not started: the readable seed or scene changed before departure"); return false; }
        if (held)
        {
            // The native evaluator owns wall-clock deadlines. Restart pending
            // work after a hold while retaining any already validated successor.
            journeyPlanner.Reset(); journeyPlanning = false;
            departure.Observe(current!.Id, epoch, director.Progress >= .999999, journeyQueue.Count > 0, true, true, 0);
            return false;
        }
        if (journeyPlanning)
        {
            journeyPlanner.Step(); journeyStatus = "Journey departure preparation: " + journeyPlanner.Reason;
            if (journeyPlanner.Complete)
            {
                if (journeyPlanner.Ready == null || journeyPlanner.Epoch != epoch)
                { AbandonDeparture("Journey not started: " + journeyPlanner.Reason); return false; }
                journeyQueue.Add(journeyPlanner.Ready); journeyPlanner.Reset(); journeyPlanning = false;
            }
        }
        ScenicJourneyDepartureState state = departure.Observe(current!.Id, epoch, director.Progress >= .999999,
            journeyQueue.Count > 0, false, true, Mathf.Max(0, Time.unscaledDeltaTime));
        if (state == ScenicJourneyDepartureState.Abandoned)
        { AbandonDeparture("Journey not started: " + departure.Reason); return false; }
        if (state == ScenicJourneyDepartureState.Admitted)
        {
            ScenicShotPlan next = journeyQueue[0];
            if (next.ContinuationOf != current.Id || next.JourneyId != journey.Id)
            { AbandonDeparture("Journey not started: prepared departure lost its predecessor identity"); return false; }
            journeyQueue.Clear();
            // Only this successful transfer may retire ordinary preparation.
            director.TakePrepared(); evaluator.Reset(); spatial.Reset(); testing = best = null; searching = planning = false;
            director.Accept(next, Time.unscaledTime); proposed = 0; forceReplacement = false; usingIndependentPicture = false;
            journeyOwned = true; journeyPredecessor = next.Id; journeyRunSeconds = 0; journeyRequestExpiresAt = 0;
            journeyStatus = "Traversing the validated departure toward the landscape objective";
            TraceJourney("start"); changed = true; return true;
        }
        if (!journeyPlanning && journeyQueue.Count == 0)
        {
            double metres = journey.NextWindowMetres();
            Vector3 seam = FromCore(current.Sample(1).CameraPosition);
            if (Player.m_localPlayer && Vector3.Distance(Player.m_localPlayer.transform.position, seam) + metres <= 85)
            {
                Camera? camera = Camera.main;
                journeyPlanner.Begin(current, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
                    camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier,
                    metres >= journey.RemainingMetres - .5, journey.ObjectiveGoal);
                journeyPlanning = true;
            }
            else journeyStatus = "Journey departure awaiting loaded reach while ordinary Scenic coverage continues";
        }
        else if (state == ScenicJourneyDepartureState.Ready) journeyStatus = departure.Reason;
        if (Time.unscaledTime >= nextJourneyTrace)
        { TraceJourney("departure-progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return false;
    }

    private void AbandonDeparture(string reason)
    {
        bool retryRequested = manualJourney && Time.unscaledTime < journeyRequestExpiresAt;
        departure.Abandon(reason);
        if (journey.Active) journey.Finish(ScenicJourneyOutcome.Blocked, reason);
        journeyStatus = reason; TraceJourney("departure-abandoned");
        journeyPlanner.Reset(); journeyQueue.Clear(); journeyPlanning = journeyOwned = manualJourney = false;
        journeyPredecessor = 0; journeyEndingReason = null; journeyEndpointWait = -1;
        requestedJourney = retryRequested; nextJourneyAt = Time.unscaledTime + 8;
        // Do not replace/freeze the current ordinary shot or consume its staged
        // successor just because a speculative journey departure failed.
    }

    private void FinishJourney(string reason, ScenicJourneyOutcome outcome = ScenicJourneyOutcome.Interrupted)
    {
        bool wasOwned = journeyOwned || journey.Active;
        if (journey.Active) journey.Finish(outcome, reason);
        journeyOwned = false; requestedJourney = journeyPlanning = manualJourney = false; journeyEndingReason = null; journeyEndpointWait = -1;
        journeyPlanner.Reset(); journeyQueue.Clear(); journeyPredecessor = 0; nextJourneyAt = Time.unscaledTime + 180;
        journeyStatus = reason;
        if (wasOwned) TraceJourney("finish");
        departure.Reset(); journeyRequestExpiresAt = 0; skippedJourneySeed = 0;
        if (Plan?.IsJourney == true) director.RequestReplacement(reason);
    }

    private void TraceJourney(string phase)
    {
        if (!ScenicTrace.Enabled) return;
        double ahead = Plan?.IsJourney == true ? Plan.TravelMetres * (1 - Plan.MotionFraction(director.Progress)) : 0;
        foreach (ScenicShotPlan queued in journeyQueue) ahead += queued.TravelMetres;
        ScenicTrace.Record(ScenicTrace.Row("journey").Field("phase", phase).Field("journey", journey.Id)
            .Field("owned", journeyOwned).Field("departureState", departure.State.ToString())
            .Field("plan", Plan?.Id ?? 0).Field("outcome", journey.Outcome.ToString()).Field("reason", journeyStatus)
            .Field("targetMetres", journey.TargetMetres).Field("measuredMetres", journey.TravelMetres)
            .Field("displacementMetres", journey.DisplacementMetres).Field("advanceMetres", journey.ObjectiveAdvanceMetres)
            .Field("cruise", journey.CruiseSpeed).Field("achievedSpeed", journey.AchievedMetresPerSecond)
            .Field("shownSeconds", journey.ShownSeconds).Field("readyMetresAhead", ahead)
            .Field("queued", journeyQueue.Count).Field("loading", journeyPlanner.WaitingForLoading)
            .Field("planner", journeyPlanner.Reason));
    }

    private void TraceReplacement()
    {
        if (!director.NeedsReplacement)
        { tracedReplacementPlan = 0; tracedReplacementReason = null; return; }
        long id = director.Current?.Id ?? 0;
        if (tracedReplacementPlan == id && tracedReplacementReason == director.Reason) return;
        tracedReplacementPlan = id; tracedReplacementReason = director.Reason;
        if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("scenic-replacement")
            .Field("plan", id).Field("feature", director.Current?.FeatureId ?? "none")
            .Field("reason", director.Reason).Field("progress", director.Progress)
            .Field("shownSeconds", director.ElapsedSeconds).Field("blockedSeconds", director.BlockedSeconds)
            .Field("prepared", director.Prepared?.Id ?? 0));
    }

    private bool BeginSpatialCandidate(BroadcastSettings settings, bool moveCarrier, bool urgent)
    {
        ScenicCandidateSchedule.Candidate candidate = ScenicCandidateSchedule.SelectSpatial(subjects, CurrentFeatureIndex(), sweep, attempts++);
        ScenicFeature feature = subjects[candidate.FeatureIndex];
        if (!director.CanDevelopSubject(feature)) return false;
        ScenicShotRole preferred = destinationSubject.Prioritize && destinationSubject.Meaningful(feature) &&
            destinationSubject.EstablishedSeconds < DestinationSubjectCoverage.ContextSeconds ? ScenicShotRole.Establish : director.PreferredRole(feature);
        ScenicShotRole role = ScenicCandidateSchedule.Role(feature, preferred, candidate.RoleIndex);
        testingKey = feature.Id + "/" + role;
        if (subjectRetry.TryGetValue(testingKey, out float retry) && Time.unscaledTime < retry) return false;
        Camera? camera = Camera.main;
        float aspect = camera ? camera.aspect : Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        float nearClip = camera ? camera.nearClipPlane : .3f;
        spatial.Begin(++nextId, feature, role, origin, settings.FieldOfView.Value,
            settings.DeliberateComposition.Value, settings.NativeSplineDolly.Value, aspect, nearClip, moveCarrier,
            camera ? camera.transform.position : (Vector3?)null, urgent);
        planning = true;
        Reason = "Discovering clear viewpoints and connected camera corridors";
        return true;
    }
    private void RememberFailure()
    {
        if (testingKey == null) return;
        if (subjectRetry.Count >= 128) subjectRetry.Clear();
        subjectRetry[testingKey] = Time.unscaledTime + 8f;
    }
    private bool TryIndependentPicture(bool moveCarrier)
    {
        if (!spatial.HasPictureCandidate) return false;
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return false;
        using (work) return TryIndependentPictureBudgeted(moveCarrier);
    }
    private bool TryIndependentPictureBudgeted(bool moveCarrier)
    {
        // A requested editorial improvement must not discard a valid moving
        // shot for the first static graph node encountered during preparation.
        // Actual blocked/completed routes retain independent-picture fallback.
        bool protectCurrent = HasUsablePicture && (pictures.Current != null || !director.BlocksMovement);
        if (!pictures.WantsCandidate(protectCurrent)) return false;
        ScenicShotPlan? candidate = spatial.TakePictureCandidate(++nextId, out long epoch, out float observedAt);
        if (candidate == null || epoch != CameraSpatialScene.Shared.Epoch || Time.unscaledTime - observedAt > .5f) return false;
        // The spatial node is a proposal. Certify exactly the same fixed pose
        // the execution rig will render, including actual surface, near-plane,
        // loaded support, carrier range, foliage and any typed subject checks.
        Camera? camera = Camera.main;
        float aspect = camera ? camera.aspect : Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        float nearClip = camera ? camera.nearClipPlane : .3f;
        ScenicShotSample sample = candidate.Sample(0);
        Vector3 position = FromCore(sample.CameraPosition), focus = FromCore(sample.AimPosition);
        var pose = new ScenicPose(position, focus, ScenicVisibility.Compose(position, focus,
            sample.ScreenX, sample.ScreenY, candidate.FieldOfView, aspect), candidate.FieldOfView, 0);
        if (!ScenicPoseValidation.Check(pose, candidate, pictureVisibility, aspect, nearClip, false, moveCarrier,
            out _, out _, out _)) return false;
        if (!director.CanDevelopSubject(candidate.Feature!)) return false;
        if (!pictures.TryOffer(candidate, epoch, CameraSpatialScene.Shared.Epoch, observedAt,
            Time.unscaledTime, protectCurrent)) return false;
        usingIndependentPicture = true;
        readiness.Reset(); proposed = 0;
        if (director.Current != null) director.RequestReplacement("Developing a new cinematic route while a measured picture remains available");
        Reason = "Independent readable " + candidate.FeatureKind + " picture; preparing cinematic movement";
        ScenicTrace.Event("picture-admitted", Reason, candidate.Id);
        return true;
    }
    private void CaptureSubjects()
    {
        subjects.Clear();
        // Nominated subject evidence is refreshed independently around its
        // observed location, not merely around the carrier's approach point.
        foreach (ScenicFeature feature in PlaceSurvey.Shared.ScenicFeatures(origin, 80f, Plan?.FeatureId))
        {
            if (subjects.Count >= 16) break;
            subjects.Add(feature);
        }
        bool places = subjects.Count > 0;
        foreach (ScenicFeature feature in survey.Features)
        {
            if (subjects.Count >= 32) break;
            // Prefer persistent, measured building identities over the old
            // greedy piece clusters when a place survey supplies structures.
            if (places && feature.Kind == ScenicFeatureKind.Structure && feature.SubjectKind == ScenicSubjectKind.Environment) continue;
            subjects.Add(feature);
        }
        // Keep real landmark pieces even when an unrelated player place exists
        // elsewhere in the arrival survey. Deduplicate exact observed identities.
        foreach (ScenicFeature feature in destinationFeatures)
        {
            bool exists = false;
            foreach (ScenicFeature retained in subjects) if (retained.Id == feature.Id) { exists = true; break; }
            if (exists) continue;
            if (subjects.Count >= 32)
            {
                int discard = subjects.FindLastIndex(f => !destinationSubject.Matches(f));
                if (discard < 0) break;
                subjects.RemoveAt(discard);
            }
            subjects.Add(feature);
        }
        subjects.Sort((a, b) =>
        {
            int order = SubjectPriority(b).CompareTo(SubjectPriority(a));
            return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id);
        });
    }
    private float SubjectPriority(ScenicFeature feature)
    {
        float priority = feature.Confidence + (float)destinationSubject.Priority(feature) + (float)director.SubjectPriority(feature);
        if (Plan != null && feature.ParentId == Plan.FeatureId && ShownSeconds >= 2) priority += .35f;
        if (!feature.IsPartial && (feature.ScopeLabel == "Settlement" || feature.ScopeLabel == "District")) priority += .1f;
        return priority;
    }
    private int CurrentFeatureIndex()
    {
        if (destinationSubject.Prioritize)
        {
            if (Plan != null && destinationSubject.EstablishedSeconds >= DestinationSubjectCoverage.ContextSeconds && ShownSeconds >= 2)
                for (int i = 0; i < subjects.Count; i++)
                    if (subjects[i].ParentId == Plan.FeatureId && destinationSubject.Matches(subjects[i])) return i;
            // Pin one meaningful observed structure ahead of unrelated scenery
            // in every bounded search, including subsequent survey rounds.
            for (int i = 0; i < subjects.Count; i++)
                if (destinationSubject.Meaningful(subjects[i]) && subjects[i].Id != Plan?.FeatureId) return i;
            for (int i = 0; i < subjects.Count; i++) if (destinationSubject.Matches(subjects[i])) return i;
        }
        // A shown establishing view of a settlement should develop into its
        // districts/buildings. Give a child the first spatial-search budget;
        // the final editorial score still compares only validated routes.
        if (Plan != null && ShownSeconds >= 2)
            for (int i = 0; i < subjects.Count; i++)
                if (subjects[i].ParentId == Plan.FeatureId) return i;
        for (int i = 0; i < subjects.Count; i++)
            if (subjects[i].Id == Plan?.FeatureId) return i;
        return -1;
    }
    private void RefreshEnvironment()
    {
        // Replace a completed observation snapshot, never mutate the subject
        // set or immutable routes of an in-flight search. Construction and
        // removed vegetation/rocks can change while the region remains loaded.
        if (surveyRefreshAt == 0) surveyRefreshAt = Time.unscaledTime + 15f;
        if (!refreshingSurvey && Time.unscaledTime >= surveyRefreshAt)
        { refreshSurvey.Begin(origin); refreshingSurvey = true; }
        if (!refreshingSurvey || !refreshSurvey.Step()) return;
        ScenicFeatureSurvey previous = survey; survey = refreshSurvey; refreshSurvey = previous;
        refreshSurvey.Reset(); refreshingSurvey = false; surveyRefreshAt = Time.unscaledTime + 15f;
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
        searching = planning = revalidating = false; testing = null; evaluator.Reset(); spatial.Reset(); sweep = (sweep + 1) % 8;
        if (best != null && bestEpoch != CameraSpatialScene.Shared.Epoch)
        { best = null; retryAt = 0; Reason = "Observed geometry changed; replan the prepared route"; return false; }
        if (best == null)
        {
            pictures.SearchFinished(false);
            RecoveryExhausted = !HasUsablePicture && (recovery.Expired || NeedsReplacement);
            Reason = HasUsablePicture ? "Retaining a readable picture while developing fresh local coverage; " + lastRejection :
                "No fully readable moving route; " + lastRejection;
            retryAt = Time.unscaledTime + 3f;
            return false;
        }
        ScenicShotPlan selected = best; best = null;
        pictures.SearchFinished(true);
        director.Stage(selected); preparedAt = bestValidatedAt; preparedEpoch = bestEpoch;
        Reason = "Prepared " + selected.FeatureKind + " / " + selected.Role;
        if (allowActivate && NeedsReplacement && (!held || Plan == null) && pictures.CanYield(Time.unscaledTime) && Time.unscaledTime - preparedAt <= 6f)
            return Activate(director.TakePrepared()!, preparedEpoch);
        return false;
    }
    private bool Activate(ScenicShotPlan selected, long validatedEpoch)
    {
        if (validatedEpoch != CameraSpatialScene.Shared.Epoch)
        { retryAt = 0; Reason = "Observed geometry changed before scenic cut"; return false; }
        if (!director.CanOffer(selected))
        { retryAt = 0; Reason = "Prepared view already covered; planning a fresh feature"; return false; }
        director.Accept(selected, Time.unscaledTime); readiness.Reset();
        usingIndependentPicture = false;
        // Acceptance is only a proposed cut. Only rendered readable output may
        // renew the on-air recovery budget or clear its exhausted state.
        RecoveryExhausted = recovery.Expired; forceReplacement = false; proposed = 0;
        Reason = "Cut to " + selected.FeatureKind + " / " + selected.Role;
        return true;
    }
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
