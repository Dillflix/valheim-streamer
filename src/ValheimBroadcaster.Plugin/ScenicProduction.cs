using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Dedicated environmental production: measured subjects, exact-route
/// screening, prepared edits, actual-presentation timing and bounded recovery.</summary>
internal sealed partial class ScenicProduction
{
    private readonly ScenicDirector director = new ScenicDirector();
    private readonly ScenicJourneyPolicy journey = new ScenicJourneyPolicy();
    private readonly ScenicJourneyDeparture departure = new ScenicJourneyDeparture();
    private readonly ScenicJourneyPlanner journeyPlanner = new ScenicJourneyPlanner();
    private readonly ScenicSplinePath journeyLoadingPath = new ScenicSplinePath();
    private long journeyPredecessor, journeyEpoch;
    private readonly List<JourneySection> journeyQueue = new List<JourneySection>(2);
    private float journeyRunSeconds, nextJourneyTrace;
    private bool journeyOwned;
    private float journeyRequestExpiresAt;
    private long tracedReplacementPlan;
    private string? tracedReplacementReason;
    private float nextJourneyAt;
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
    internal bool JourneyActive => journeyOwned && journey.Active &&
        (journeyRecovery.Active || HasUsablePicture && (Plan?.JourneyId == journey.Id || Plan?.Id == journeyExitId));
    internal string JourneyStatus => journeyStatus + $"; {(departure.Pending && !journeyOwned ? "Preparing (not on air)" : requestedJourney ? "Requested (not on air)" : journey.Outcome.ToString())}; measured {journey.TravelMetres:F0}/{journey.TargetMetres:F0}m" +
        $"; destination {journey.Itinerary?.Description ?? "not selected"}; route progress {journey.RouteProgressMetres:F0}m; arrival shown {journey.ArrivalShownSeconds:F1}s" +
        $"; displacement {journey.DisplacementMetres:F0}m; objective advance {journey.ObjectiveAdvanceMetres:F0}m; requested cruise {journey.CruiseSpeed:F1}m/s; effective cruise {journeyRunway.EffectiveCruise:F1}m/s" +
        $"; achieved {journey.AchievedMetresPerSecond:F1}m/s; shown {journey.ShownSeconds:F1}s; queued {journeyQueue.Count}; certified runway {JourneyRunwayMetres():F0}m; preparation {journeyRunway.PreparationSeconds:F1}s; ahead {journeyPlanner.Reason}; selection {journeySelectionEvidence}";
    internal string RequestJourney()
    {
        if (journeyOwned) return JourneyStatus;
        requestedJourney = true; journeyRequestExpiresAt = Time.unscaledTime + 90; nextJourneyAt = 0;
        if (!journey.Active) departure.Reset();
        else { manualJourney = true; requestedJourney = false; }
        journeyStatus = "Journey requested; preparing an independently validated departure (up to 90 seconds)";
        nextJourneyWaitingTrace = 0; TraceJourney("request"); return journeyStatus;
    }
    internal void StopJourney()
    { FinishJourney("Journey stopped; resume ordinary local scenery", ScenicJourneyOutcome.Interrupted); }
    internal Vector3? JourneyLoadingPoint
    {
        get
        {
            if (!JourneyActive || Plan == null) return null;
            if (journeyRecovery.Active)
            {
                if (!HasUsablePicture) return null;
                Camera? currentCamera = Camera.main;
                // Repair owns no speculative loading path. Keep the protected
                // carrier with the actual independently validated picture.
                return currentCamera ? currentCamera.transform.position : FromCore(Plan.Sample(proposed).CameraPosition);
            }
            if (!Plan.IsJourney || journeyBraking) return FromCore(Plan.Sample(1).CameraPosition);
            // Lead may cross a seam only through already certified successors.
            // The observer still enforces its 32m camera tether and live loaded
            // geometry checks; a pending proposal never moves the carrier.
            ScenicShotPlan lead = Plan;
            double distance = lead.TravelMetres * lead.MotionFraction(director.Progress) + 24;
            long predecessor = lead.Id;
            foreach (JourneySection section in journeyQueue)
            {
                if (distance <= lead.TravelMetres || !section.Proof.CanAdmit ||
                    !JourneyRunwayPolicy.HeadMatches(section.Plan, predecessor, journey.Id,
                        section.Epoch, section.PreparedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime)) break;
                distance -= lead.TravelMetres; lead = section.Plan; predecessor = lead.Id;
            }
            double fraction = Math.Min(1, distance / Math.Max(1, lead.TravelMetres));
            double low = 0, high = 1;
            for (int i = 0; i < 24; i++)
            {
                double middle = (low + high) * .5;
                if (lead.MotionFraction(middle) < fraction) low = middle; else high = middle;
            }
            if (!journeyLoadingPath.Ready || journeyLoadingPath.PreparedPlan?.Id != lead.Id)
            {
                // A present fallback keeps the observer's smooth lead/return
                // path engaged. Null would snap the carrier back to the camera
                // whenever shared work postpones preparing the next spline.
                Camera? camera = Camera.main;
                Vector3 fallback = camera ? camera.transform.position : FromCore(Plan.Sample(director.Progress).CameraPosition);
                if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return fallback;
                using (work) if (!journeyLoadingPath.Prepare(lead, 1)) return fallback;
            }
            return FromCore(journeyLoadingPath.Sample((float)((low + high) * .5)).CameraPosition);
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
    private readonly ScenicPictureAdmission pictureAdmission = new ScenicPictureAdmission();
    private string pictureAdmissionValidation = "Not evaluated";
    private float nextPictureAdmissionTrace;
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
    private Vector3 journeyOrdinarySearchOrigin;
    private long nextId;
    private long bestEpoch, preparedEpoch;
    private ScenicShotPlan? testing, best;
    private bool searching, planning, trimmed, elevated, forceReplacement, revalidating, deadlineHandled;
    private string? testingKey;
    private int attempts, sweep, sections, elevations;
    private readonly ScenicSearchComparison comparison = new ScenicSearchComparison();
    private int searchCurrentIndex;
    private bool developingSearch;
    private string comparisonMode = "idle", comparisonEnd = "not started", lastComparison = "none";
    private float nextComparisonTrace;
    private float retryAt, preparedAt, proposed, bestValidatedAt;
    private float surveyRefreshAt;
    private bool refreshingSurvey;
    private string lastRejection = "none";
    private bool hasSettings, plannedComposition, plannedNative;
    private float plannedFov;
    internal bool HasRegion { get; private set; }
    internal Vector3 RegionOrigin => origin;
    internal ScenicShotPlan? Plan => usingIndependentPicture ? pictures.Current : director.Current;
    internal bool ContentExhausted => pictures.ContentExhausted || journeyExitExhausted;
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
    internal string SearchStatus => (!journeyOwned && (ManualJourneyPending || departure.Pending) ? "Journey: " + journeyStatus + "; " : "") + (!survey.Complete ? survey.Diagnostics :
        Reason + "; " + ComparisonStatus + "; salvage attempts " + sections +
        ", canopy alternatives " + elevations + "; last rejection " + lastRejection +
        (planning ? "; " + spatial.Reason : "")) + RecoveryStatus;
    internal int RejectedViews { get; private set; }
    internal string Diagnostics => $"Scenic director: {Reason}; {director.Reason}; " +
        $"feature {Plan?.FeatureKind.ToString() ?? "none"}/{Plan?.FeatureId ?? "none"}; role {Plan?.Role.ToString() ?? "none"}; " +
        $"shot {Plan?.Id ?? 0}; progress {(Plan?.IsPictureOnly == true ? 0 : director.Progress):P0}; shown {ShownSeconds:F1}/{Plan?.DurationSeconds ?? 0:F1}s; " +
        $"independent picture {Plan?.IsPictureOnly == true}; settled coverage {pictures.TotalPictureSeconds:F1}s; content exhausted {ContentExhausted}; " +
        $"lens {Plan?.FieldOfView ?? 0:F0}; blocked {director.BlockedSeconds:F1}s; staged {director.Prepared?.Id ?? 0}; " +
        $"search {Searching}; {ComparisonSummary}; last comparison: {lastComparison}; rejected {RejectedViews}; last rejection {lastRejection}; " +
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
        if (journeyOwned) return;
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
        CancelComparison("preparation invalidated");
        director.TakePrepared(); testing = best = null; searching = planning = false;
        evaluator.Reset(); spatial.Reset(); subjectRetry.Clear(); retryAt = 0;
        if (Plan != null && destinationSubject.Prioritize) director.RequestEdit("Preparing coverage of the selected destination");
    }
    internal void Clear()
    {
        StopFixture(); StopJourney(); journey.Reset(); nextJourneyAt = 0;
        ClearJourneyProgramme();
        fixtureEvaluator.Dispose(); fixtureVisibility.Reset(); journeyPlanner.Dispose(); journeyLoadingPath.Dispose();
        destinationSubject.Reset(); destinationSurvey.Reset(); destinationFeatures.Clear(); nextDestinationSurvey = 0;
        director.Reset(); pictures.Reset(); pictureAdmission.Reset(); pictureVisibility.Reset(); survey.Reset(); refreshSurvey.Reset(); evaluator.Dispose(); spatial.Reset(); subjects.Clear(); subjectRetry.Clear();
        refreshingSurvey = false; surveyRefreshAt = 0;
        readiness.Reset(); liveSceneReadiness.Reset(); recovery.Reset(); testingKey = null;
        lastPresentedPlan = null; lastPresentedFocus = lastAdmittedFocus = default; Visibility.Reset(); planningVisibility.Reset();
        usingIndependentPicture = false; motionPlanId = 0; lastMotionProgress = 0;
        HasRegion = searching = planning = trimmed = elevated = forceReplacement = RecoveryExhausted = hasSettings = revalidating = deadlineHandled = false;
        HasUsablePicture = false;
        testing = best = null; attempts = sweep = RejectedViews = 0;
        comparison.Reset(); comparisonMode = "idle"; comparisonEnd = "not started"; lastComparison = "none";
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
        bool travelling = journeyOwned && journey.Active && (!journeyRecovery.Active || journeyRecovery.OwnsPreparedEnding);
        float motion = travelling ? 1 : explicitFixture ? fixtureMotion : Mathf.Clamp(settings.ShotMotion.Value, 0, 1.5f);
        lastMovementEnabled = (explicitFixture ? fixtureDynamic : settings.DynamicShots.Value) && motion > 0;
        double step = Mathf.Max(0, dt);
        if (travelling && journeyExitId == 0 && Plan?.IsJourney == true)
        {
            double speed = journeyRunway.StepSpeed(journey.CruiseSpeed, JourneyRunwayMetres(), step, held, journeyBraking);
            // Plans were timed at requested cruise. Changing the playback clock
            // preserves their certified geometry and exact seams.
            step = ScenicJourneyPolicy.SafeDeltaSeconds(step, speed) * speed / journey.CruiseSpeed;
            if (journeyBraking)
                step *= JourneyHandoffPolicy.BrakingScale(Plan.TravelMetres * (1 - Plan.MotionFraction(director.Progress)), speed);
        }
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
    internal bool Observe(bool live, bool readable, long planId, float progress, bool blocked, bool held, string reason, Vector3? actualFocus, JourneyPresentationSample? actualCapture = null)
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
            RememberJourneyDeparturePose(shown, actualCapture, lastPresentedFocus);
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
        if (route != null && actualCapture.HasValue)
        {
            JourneyPresentationSample captured = actualCapture.Value;
            journey.Observe(route, captured.Time, captured.Progress, live && readable && planId == route.Id, held, captured.Position);
            if (route.Id == journeyExitId)
                journey.ObserveArrival(captured.Time, live && readable, held, captured.Position, journeyEndingArrival);
        }
        else journey.BreakPresentation();
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
        if (journeyOwned && !held && recovery.Expired)
        {
            FinishJourney("Journey lost its live scene; ordinary recovery owns acquisition", ScenicJourneyOutcome.Blocked);
            journeyExitExhausted = true;
        }
        ScenicTrace.Picture(onAir && HasRegion, HasUsablePicture, held, planId, (float)recovery.ElapsedSeconds, recovery.Expired, Reason);
        if (!recovery.Active) deadlineHandled = false;
        if (usableLive && knownPicture) RecoveryExhausted = false;
        else if (recovery.Expired) RecoveryExhausted = true;
    }

    internal bool TickSearch(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate = true)
    {
        if (!HasRegion || !SceneReady) return false;
        // Scene discovery has its own lifetime. Advance it while terrain/site
        // subjects are being collected too, before any camera candidate exists.
        // Ensure shares the existing observation budget and per-frame work cap;
        // this is not a synchronous scene scan or a new readiness predicate.
        ScenicFoliageScene.Shared.Ensure(origin);
        TraceReplacement();
        if (FixtureActive && TickFixture(settings, moveCarrier, held, allowActivate, out bool fixtureChanged)) return fixtureChanged;
        bool journeyControls = TickJourney(settings, moveCarrier, held, allowActivate, out bool journeyChanged);
        if (journeyChanged) return true;
        // Journey execution owns the on-air plan, not the whole planning pipeline.
        // Continue local surveys and stage ordinary exit coverage under the shared budget.
        allowActivate &= !journeyControls;
        // A previously measured clear node keeps progressing toward actual
        // admission even if route search changes subject, finishes or resets.
        // This owns no footage until complete current validation succeeds.
        if (pictureAdmission.Pending != null && allowActivate && (!held || Plan == null) && TryIndependentPicture(moveCarrier))
            return true;
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
            return FinishSearch(held, allowActivate, "picture recovery deadline");
        }
        if (!hasSettings || plannedFov != settings.FieldOfView.Value ||
            plannedComposition != settings.DeliberateComposition.Value || plannedNative != settings.NativeSplineDolly.Value)
        {
            if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("settings").Field("fov", settings.FieldOfView.Value)
                .Field("composition", settings.DeliberateComposition.Value).Field("nativeSpline", settings.NativeSplineDolly.Value)
                .Field("dynamic", settings.DynamicShots.Value).Field("motion", settings.ShotMotion.Value));
            hasSettings = true; plannedFov = settings.FieldOfView.Value;
            plannedComposition = settings.DeliberateComposition.Value; plannedNative = settings.NativeSplineDolly.Value;
            CancelComparison("preparation invalidated");
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
        if (allowActivate && director.Prepared != null && director.SuccessorContextReady(director.Prepared) && (!held || Plan == null) && pictures.CanYield(Time.unscaledTime) &&
            (NeedsReplacement || director.ShouldPrefetch && Time.unscaledTime - preparedAt > 4f))
        {
            ScenicShotPlan staged = director.TakePrepared()!;
            // Runtime repeats exact-pose checks. An old staged path is screened
            // again before admission rather than trusting stale loaded objects.
            if (NeedsReplacement && Time.unscaledTime - preparedAt <= 6f && preparedEpoch == CameraSpatialScene.Shared.Epoch)
                return Activate(staged, preparedEpoch);
            testing = staged; revalidating = true; trimmed = elevated = false; searching = true; attempts = 0; best = null;
            planning = false; spatial.Reset(); testingKey = null;
            sections = elevations = 0;
            BeginComparison("revalidation"); comparison.PlannedRoute();
            BeginEvaluation(settings, moveCarrier); return false;
        }
        if (!searching && director.Prepared == null && (NeedsReplacement || director.ShouldPrefetch || director.ElapsedSeconds >= 2) && Time.unscaledTime >= retryAt)
        {
            journeyOrdinarySearchOrigin = origin;
            searching = true; revalidating = false; testing = best = null; attempts = 0;
            planning = false; spatial.Reset(); CaptureSubjects();
            searchCurrentIndex = CurrentFeatureIndex();
            developingSearch = HasUsablePicture && !director.BlocksMovement;
            sections = elevations = 0;
            BeginComparison(allowActivate && !HasUsablePicture ? "acquisition" : "comparison");
            Reason = NeedsReplacement ? "Planning a readable moving shot" : "Preparing the next environmental shot";
        }
        if (!searching) return false;
        RefreshComparisonCandidates();
        TraceComparison("progress");
        string? stopReason = comparison.FinishReason(Time.unscaledTime,
            !allowActivate || HasUsablePicture, allowActivate && !held &&
            (forceReplacement || !usingIndependentPicture && director.BlocksMovement), planning || testing != null);
        if (stopReason != null) return FinishSearch(held, allowActivate, stopReason);
        if (testing == null)
        {
            if (!planning)
            {
                if (subjects.Count == 0 || attempts >= ScenicCandidateSchedule.SpatialAttemptLimit(subjects, searchCurrentIndex, sweep))
                    return FinishSearch(held, allowActivate, "schedule exhausted");
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
            comparison.PlannedRoute();
            if (!director.CanOffer(testing))
            { comparison.Skip(); RejectedViews++; lastRejection = "Measured route repeats completed coverage"; testing = null; RememberFailure(); return false; }
            trimmed = elevated = false;
            BeginEvaluation(settings, moveCarrier);
        }
        if (!evaluator.Step(recovery.Active && allowActivate ? 12 : 6)) return false;
        // Surface fitting produces a new immutable route. Keep that exact
        // route for ranking, trimming, staging and playback, never the original
        // below-surface proposal that was used to begin the search.
        testing = evaluator.EvaluatedPlan ?? testing;
        comparison.EvaluatedRoute(testing!.Id, evaluator.Accepted);
        if (evaluator.Accepted)
        {
            if (director.CanOffer(testing!))
            {
                double score = director.Rank(testing!, evaluator.Score) + (testing!.Feature == null ? 0 : destinationSubject.Priority(testing.Feature));
                comparison.Offer(testing!, score, evaluator.SceneEpoch, Time.unscaledTime);
                RefreshComparisonCandidates();
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
        stopReason = comparison.FinishReason(Time.unscaledTime, !allowActivate || HasUsablePicture,
            allowActivate && !held && (forceReplacement || !usingIndependentPicture && director.BlocksMovement), planning || testing != null);
        if (stopReason != null) return FinishSearch(held, allowActivate, stopReason);
        if (comparisonMode == "revalidation") return FinishSearch(held, allowActivate, "revalidation failed");
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
        CancelComparison("fixture admitted");
        searching = planning = false; testing = best = null; evaluator.Reset(); spatial.Reset();
        FixtureStatus = "Saved camera fixture admitted after full current-scene validation";
        changed = true; return true;
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
        comparison.Schedule();
        ScenicCandidateSchedule.Candidate candidate = ScenicCandidateSchedule.SelectSpatial(subjects, searchCurrentIndex, sweep, attempts++,
            developing: developingSearch);
        ScenicFeature feature = subjects[candidate.FeatureIndex];
        if (!director.CanDevelopSubject(feature)) { comparison.Skip(); return false; }
        ScenicShotRole preferred = urgent ? director.PreferredRole(feature) : director.PreferredNextRole(feature);
        bool anticipatedDevelopment = !urgent && director.Current?.FeatureId == feature.Id &&
            director.Current.Role == ScenicShotRole.Establish && director.ElapsedSeconds >= 2 && preferred != ScenicShotRole.Establish;
        if (destinationSubject.Prioritize && destinationSubject.Meaningful(feature) &&
            destinationSubject.EstablishedSeconds < DestinationSubjectCoverage.ContextSeconds && !anticipatedDevelopment)
            preferred = ScenicShotRole.Establish;
        ScenicShotRole role = ScenicCandidateSchedule.Role(feature, preferred, candidate.RoleIndex);
        testingKey = feature.Id + "/" + role;
        if (subjectRetry.TryGetValue(testingKey, out float retry) && Time.unscaledTime < retry) { comparison.Skip(); return false; }
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
        if (pictureAdmission.Pending == null && !spatial.HasPictureCandidate) return false;
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return false;
        using (work) return TryIndependentPictureBudgeted(moveCarrier);
    }
    private bool TryIndependentPictureBudgeted(bool moveCarrier)
    {
        // A requested editorial improvement must not discard a valid moving
        // shot for the first static graph node encountered during preparation.
        // Actual blocked/completed routes retain independent-picture fallback.
        bool protectCurrent = HasUsablePicture && (pictures.Current != null || !director.BlocksMovement);
        if (!pictures.WantsCandidate(protectCurrent)) { pictureAdmission.Reset(); return false; }
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (pictureAdmission.Pending == null)
        {
            ScenicShotPlan? proposal = spatial.TakePictureCandidate(++nextId, out long observedEpoch, out float observedAt);
            if (proposal == null || !pictureAdmission.TryQueue(proposal, observedEpoch, epoch, observedAt, Time.unscaledTime)) return false;
            pictureAdmissionValidation = "Not evaluated"; nextPictureAdmissionTrace = 0;
            ScenicTrace.Event("picture-admission-pending", "Retaining an exact pose while independent validation completes", proposal.Id);
        }
        long pendingId = pictureAdmission.Pending!.Id;
        ScenicPictureAdmissionResult result = pictureAdmission.Advance(epoch, Time.unscaledTime, Time.frameCount,
            candidate => ValidateIndependentPicture(candidate, moveCarrier), out ScenicShotPlan? admitted);
        if (ScenicTrace.Enabled && (result != ScenicPictureAdmissionResult.Pending || Time.unscaledTime >= nextPictureAdmissionTrace))
        {
            nextPictureAdmissionTrace = Time.unscaledTime + 1;
            ScenicTrace.Record(ScenicTrace.Row("picture-admission").Field("plan", pendingId).Field("result", result.ToString())
                .Field("attempts", pictureAdmission.Evaluations).Field("elapsed", Time.unscaledTime - pictureAdmission.StartedAt)
                .Field("reason", pictureAdmission.Reason).Field("validation", pictureAdmissionValidation));
        }
        if (admitted == null) return false;
        // This timestamp belongs to the just-completed full pose check, not to
        // the older graph proposal. No pending or merely remembered view is freshened.
        if (!pictures.TryOffer(admitted, epoch, CameraSpatialScene.Shared.Epoch, Time.unscaledTime,
            Time.unscaledTime, protectCurrent)) return false;
        usingIndependentPicture = true;
        journeyExitExhausted = false;
        readiness.Reset(); proposed = 0;
        if (director.Current != null) director.RequestReplacement("Developing a new cinematic route while a measured picture remains available");
        Reason = "Independent readable " + admitted.FeatureKind + " picture; preparing cinematic movement";
        ScenicTrace.Event("picture-admitted", Reason, admitted.Id);
        return true;
    }
    private ScenicPictureAdmissionResult ValidateIndependentPicture(ScenicShotPlan candidate, bool moveCarrier)
    {
        // Repeat the complete current surface, near-plane, carrier, framing,
        // foliage and typed-subject checks at this exact immutable pose.
        Camera? camera = Camera.main;
        float aspect = camera ? camera.aspect : Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        float nearClip = camera ? camera.nearClipPlane : .3f;
        ScenicShotSample sample = candidate.Sample(0);
        Vector3 position = FromCore(sample.CameraPosition), focus = FromCore(sample.AimPosition);
        var pose = new ScenicPose(position, focus, ScenicVisibility.Compose(position, focus,
            sample.ScreenX, sample.ScreenY, candidate.FieldOfView, aspect), candidate.FieldOfView, 0);
        if (!ScenicPoseValidation.Check(pose, candidate, pictureVisibility, aspect, nearClip, false, moveCarrier,
            out _, out ScenicRejectionKind rejection, out string rejectionReason))
        {
            pictureAdmissionValidation = rejection + ": " + rejectionReason;
            return rejection == ScenicRejectionKind.SurveyIncomplete || rejection == ScenicRejectionKind.Geometry ||
                rejection == ScenicRejectionKind.Acquisition ? ScenicPictureAdmissionResult.Pending : ScenicPictureAdmissionResult.Rejected;
        }
        bool develop = director.CanDevelopSubject(candidate.Feature!);
        pictureAdmissionValidation = develop ? "Complete current pose validation passed" : "Subject no longer eligible for development";
        return develop ? ScenicPictureAdmissionResult.Accepted : ScenicPictureAdmissionResult.Rejected;
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
            foreach (ScenicFeature retained in subjects)
                if (retained.Id == feature.Id || ScenicFeatureIdentity.SameMeasuredStructure(retained, feature)) { exists = true; break; }
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
    private string ComparisonSummary => $"{comparisonMode}: scheduled {comparison.Scheduled}, skipped {comparison.Skipped}, planned {comparison.Planned}, " +
        $"evaluated {comparison.Evaluated} ({comparison.EvaluationPasses} passes), validated {comparison.Validated}, eligible {comparison.Eligible}, retained {comparison.Retained}/3; " +
        (searching ? (Time.unscaledTime < comparison.Deadline ? $"comparison budget {comparison.Deadline - Time.unscaledTime:F1}s" :
            $"finishing pending evaluation; hard limit {Math.Max(0, comparison.DrainDeadline - Time.unscaledTime):F1}s") : "ended: " + comparisonEnd);
    private string ComparisonStatus => ComparisonSummary +
        (comparisonMode == "revalidation" ? "; prior comparison: " + lastComparison : "");

    private void BeginComparison(string mode)
    {
        comparisonMode = mode; comparisonEnd = "running"; nextComparisonTrace = 0;
        comparison.Begin(Time.unscaledTime, mode == "comparison" ? 6 : 10,
            mode == "comparison" ? null : mode, pendingGraceSeconds: mode == "comparison" ? 4 : 0);
        TraceComparison("start");
    }
    private void CancelComparison(string reason)
    {
        bool active = searching && comparison.Active;
        searching = false; comparisonEnd = reason;
        if (active)
        {
            TraceComparison("cancel");
            if (comparisonMode != "revalidation") lastComparison = ComparisonSummary;
        }
        comparison.End(); best = null;
    }
    private void RefreshComparisonCandidates()
    {
        if (!searching || !comparison.Active) { best = null; return; }
        long epoch = CameraSpatialScene.Shared.Epoch;
        comparison.Prune(entry => entry.Epoch == epoch && entry.Plan.Id != director.Current?.Id && director.CanOffer(entry.Plan) &&
            (!journeyOwned || !Player.m_localPlayer || WithinJourneyHorizon(entry.Plan, Player.m_localPlayer.transform.position)));
        best = null;
        // Prefer current validation when available. An older best may still be
        // staged, but keeps its original timestamp and must be revalidated.
        foreach (ScenicSearchComparison.Entry entry in comparison.Entries)
        {
            bool fresh = Time.unscaledTime >= entry.ObservedAt && Time.unscaledTime - entry.ObservedAt <= 6;
            if (best != null && (!fresh || Time.unscaledTime - bestValidatedAt <= 6)) continue;
            best = entry.Plan; bestEpoch = entry.Epoch; bestValidatedAt = (float)entry.ObservedAt;
        }
    }
    private void TraceComparison(string action)
    {
        if (!ScenicTrace.Enabled || action == "progress" && Time.unscaledTime < nextComparisonTrace) return;
        nextComparisonTrace = Time.unscaledTime + 1;
        ScenicTrace.Record(ScenicTrace.Row("scenic-search").Field("action", action).Field("mode", comparisonMode)
            .Field("scheduled", comparison.Scheduled).Field("skipped", comparison.Skipped).Field("planned", comparison.Planned)
            .Field("evaluated", comparison.Evaluated).Field("evaluationPasses", comparison.EvaluationPasses)
            .Field("validated", comparison.Validated).Field("eligible", comparison.Eligible).Field("retained", comparison.Retained)
            .Field("salvageAttempts", sections).Field("canopyAlternatives", elevations)
            .Field("startedAt", comparison.StartedAt).Field("deadline", comparison.Deadline).Field("drainDeadline", comparison.DrainDeadline)
            .Field("reason", comparisonEnd).Field("selected", best?.Id ?? 0));
    }

    private bool FinishSearch(bool held, bool allowActivate, string endReason)
    {
        RefreshComparisonCandidates();
        bool active = searching && comparison.Active;
        comparisonEnd = endReason; searching = false;
        if (active)
        {
            TraceComparison("finish");
            if (comparisonMode != "revalidation") lastComparison = ComparisonSummary;
        }
        comparison.End();
        planning = revalidating = false; testing = null; evaluator.Reset(); spatial.Reset(); sweep = (sweep + 1) % 8;
        if (best != null && bestEpoch != CameraSpatialScene.Shared.Epoch)
        { best = null; retryAt = 0; Reason = "Observed geometry changed; replan the prepared route"; return false; }
        if (best == null)
        {
            pictures.SearchFinished(false);
            RecoveryExhausted = !HasUsablePicture && (recovery.Expired || NeedsReplacement);
            Reason = HasUsablePicture ? "Retaining a readable picture while developing fresh local coverage; " + lastRejection :
                "No fully readable moving route; " + lastRejection;
            retryAt = Time.unscaledTime + (comparisonMode == "revalidation" ? 0 : 3f);
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
        if (!director.SuccessorContextReady(selected))
        { director.Stage(selected); Reason = "Prepared subject development awaits shown establishing context"; return false; }
        if (!director.CanOffer(selected))
        { retryAt = 0; Reason = "Prepared view already covered; planning a fresh feature"; return false; }
        director.Accept(selected, Time.unscaledTime); readiness.Reset();
        usingIndependentPicture = false;
        journeyExitExhausted = false;
        // Acceptance is only a proposed cut. Only rendered readable output may
        // renew the on-air recovery budget or clear its exhausted state.
        RecoveryExhausted = recovery.Expired; forceReplacement = false; proposed = 0;
        Reason = "Cut to " + selected.FeatureKind + " / " + selected.Role;
        return true;
    }
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
