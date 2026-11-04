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
    internal ScenicShotPlan? Plan => usingIndependentPicture ? pictures.Current : director.Current;
    internal bool ContentExhausted => pictures.ContentExhausted;
    private double ShownSeconds => usingIndependentPicture ? pictures.ShownSeconds : director.ElapsedSeconds;
    internal bool Searching => HasRegion && (!survey.Complete || searching);
    internal bool NeedsReplacement => forceReplacement || director.Current == null || director.NeedsReplacement;
    internal bool RecoveryExhausted { get; private set; }
    internal bool RecoveryExpired => recovery.Expired;
    internal bool HasUsablePicture { get; private set; }
    internal bool CanContinue => Plan != null && readiness.CanContinue(Plan.Id, lastAdmittedFocus);
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
        PlaceSurvey.Shared.Diagnostics + "; " + CameraSpatialScene.Shared.Diagnostics + "; destination: " + DestinationSubjectStatus;

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
        if (Plan != null && destinationSubject.Prioritize) director.RequestReplacement("Preparing coverage of the selected destination");
    }
    internal void Clear()
    {
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
        if (usingIndependentPicture) return proposed = 0;
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
    internal bool Observe(bool live, bool readable, long planId, float progress, bool blocked, bool held, string reason, Vector3? actualFocus)
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
        bool requested = director.NeedsReplacement;
        ScenicShotPlan? route = director.Current;
        if (route != null)
            director.Observe(route.Id, Time.unscaledTime, planId == route.Id ? Mathf.Clamp01(progress) : (float)director.Progress,
                live && planId == route.Id, readable && planId == route.Id, blocked && planId == route.Id, held);
        if (live && readable && !NeedsReplacement) RecoveryExhausted = false;
        if (blocked || !readable) { lastRejection = reason; Reason = reason; }
        if (!requested && director.NeedsReplacement)
        {
            retryAt = 0;
            // A graph route can pass preflight repeatedly yet never supply
            // valid moving output. That live failure is also evidence of
            // exhausted local motion; successful restaging cannot conceal it.
            if (route != null && (planId != route.Id || !live || !readable || blocked)) pictures.RouteUnavailable();
        }
        return !requested && director.NeedsReplacement;
    }

    private long pixelReplannedId;
    internal bool ObservePixelConcern(long planId, bool reconsider, string reason)
    {
        if (!reconsider || planId <= 0 || planId == pixelReplannedId ||
            Plan?.Id != planId) return false;
        pixelReplannedId = planId;
        director.RequestReplacement("Measured picture concern; prepare a different local composition: " + reason);
        retryAt = 0;
        // Darkness, fog and poor contrast never invalidate the scene lease,
        // erase the readable current view, or request autonomous relocation.
        return true;
    }

    internal void UpdateRecovery(bool onAir, bool usableLive, long planId, bool held)
    {
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
                if (destinationSubject.Prioritize)
                {
                    // Newly loaded subject evidence must enter scheduling now,
                    // not after an unrelated prepared shot's full lifetime.
                    director.TakePrepared(); testing = best = null; searching = planning = false;
                    evaluator.Reset(); spatial.Reset(); retryAt = 0;
                    if (Plan != null && !destinationSubject.Matches(Plan.Feature))
                        director.RequestReplacement("Loaded destination subject available for a purposeful shot");
                }
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
        bool protectCurrent = HasUsablePicture && (pictures.Current != null || !NeedsReplacement);
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
