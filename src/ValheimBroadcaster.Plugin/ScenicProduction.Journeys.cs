using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    private sealed class JourneySection : IDisposable
    {
        internal readonly ScenicShotPlan Plan, Ending;
        internal readonly bool ArrivalFramed;
        internal readonly ScenicJourneySectionLease Proof = new ScenicJourneySectionLease();
        internal long Epoch => Proof.Epoch;
        internal float PreparedAt => Proof.PreparedAt;
        internal JourneySection(ScenicShotPlan plan, ScenicShotPlan ending, long epoch,
            float routeAt, float endingAt, bool arrival, bool moveCarrier)
        {
            Plan = plan; Ending = ending; ArrivalFramed = arrival;
            Camera? camera = Camera.main;
            Proof.Begin(plan, ending, epoch, routeAt, endingAt,
                camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier);
        }
        public void Dispose() => Proof.Dispose();
    }
    private readonly JourneyRunwayPolicy journeyRunway = new JourneyRunwayPolicy();
    private float journeyPreparationStarted, journeyRetryAt;
    private int journeyPreparationFailures;
    private string journeyLastPreparationFailure = "none";
    private double journeyWindowScale = 1;
    private Vector3 journeyFailureCarrier;
    private readonly ScenicJourneyEndingPlanner journeyEndingPlanner = new ScenicJourneyEndingPlanner();
    private readonly ScenicJourneyEndingLease journeyEndingLease = new ScenicJourneyEndingLease();
    private readonly DestinationSubjectCoverage journeyDestinationSubject = new DestinationSubjectCoverage();
    private readonly ScenicFeatureSurvey journeyArrivalSurvey = new ScenicFeatureSurvey();
    private bool journeyArrivalSurveyStarted;
    private ScenicShotPlan? journeyObservedPlan, journeyDepartureSeed;
    private AutonomyPosition journeyObservedPosition, journeyObservedAim;
    private float journeyObservedFov;
    private float journeyObservedSeedAt = float.NegativeInfinity, nextJourneyWaitingTrace;
    private string journeySelectionEvidence = "Selection not attempted";
    private double journeyDestinationReportedAt, journeyDestinationRetryUntil;
    private float journeyDestinationAge;

    private void RememberJourneyDeparturePose(ScenicShotPlan shown, JourneyPresentationSample? capture, Vector3 focus)
    {
        if (shown.Feature == null) return;
        Vector3 position; float fov;
        if (capture.HasValue && capture.Value.Audit.Shot == shown.Id && capture.Value.Audit.Captured.Valid &&
            capture.Value.Audit.Status == CameraRenderAuditStatus.Matched)
        { position = FromCore(capture.Value.Position); fov = (float)capture.Value.Audit.Captured.Fov; }
        else
        {
            // A missing render audit must not disable route proposals. This is
            // only a seed; native route/ending validation remains mandatory.
            Camera? camera = Camera.main;
            if (!camera) return;
            position = camera.transform.position; fov = camera.fieldOfView;
        }
        if (!TargetRoster.Finite(position) || Vector3.Distance(position, focus) < 2 ||
            Vector3.ProjectOnPlane(focus - position, Vector3.up).sqrMagnitude < .75f * .75f ||
            Vector3.Distance(position, focus) > 180 || float.IsNaN(fov) || float.IsInfinity(fov) || fov < 30 || fov > 110) return;
        journeyObservedPlan = shown; journeyObservedPosition = new AutonomyPosition(position.x, position.y, position.z);
        journeyObservedAim = new AutonomyPosition(focus.x, focus.y, focus.z); journeyObservedFov = fov;
        journeyObservedSeedAt = Time.unscaledTime;
    }

    private bool WaitForJourney(string reason)
    {
        journeyStatus = reason;
        if ((requestedJourney || manualJourney) && Time.unscaledTime >= nextJourneyWaitingTrace)
        { TraceJourney("waiting"); nextJourneyWaitingTrace = Time.unscaledTime + 1; }
        return false;
    }
    private float nextJourneyArrivalSurvey;
    private readonly List<ScenicRegion> journeyRegions = new List<ScenicRegion>(1024);
    private readonly Dictionary<string, WorldInterestSite> journeySites = new Dictionary<string, WorldInterestSite>(StringComparer.Ordinal);
    private readonly Queue<string> recentJourneyDestinations = new Queue<string>();
    private JourneySection? journeySection;
    private ScenicShotPlan? journeyPendingSection;
    private bool journeyPendingArrival, journeyBraking, journeyEndingArrival, journeyExitExhausted;
    private long journeyExitId, journeyCatalogueWorld, journeyEndRevision;
    private float journeyCatalogueAt = float.NegativeInfinity, journeyExitElapsed, nextJourneySelection;
    private ScenicRegion? journeyArrivedDestination;
    private WorldInterestSite? journeyArrivedSite;
    private Vector3 journeySurveyOrigin;
    internal bool ManualJourneyPending => !journeyOwned && (requestedJourney || manualJourney) &&
        Time.unscaledTime < journeyRequestExpiresAt;
    internal long JourneyEndRevision => journeyEndRevision;
    internal ScenicRegion? JourneyArrivedDestination => journeyArrivedDestination;
    internal WorldInterestSite? JourneyArrivedSite => journeyArrivedSite;

    internal void SetJourneyCatalogue(long world, IReadOnlyList<ScenicRegion> regions,
        IReadOnlyDictionary<string, WorldInterestSite> sites, bool fresh)
    {
        if (world != journeyCatalogueWorld)
        {
            if (journey.Active) FinishJourney("Journey catalogue belongs to another world");
            recentJourneyDestinations.Clear(); journeyCatalogueWorld = world;
        }
        journeyRegions.Clear(); journeySites.Clear();
        if (!fresh || world == 0) return;
        foreach (ScenicRegion region in regions) { if (journeyRegions.Count == 1024) break; journeyRegions.Add(region); }
        foreach (var entry in sites) journeySites[entry.Key] = entry.Value;
        journeyCatalogueAt = Time.unscaledTime;
        if (journey.Active && journey.Itinerary != null)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            string target = journey.Itinerary.Destination.Id;
            if (journeySites.TryGetValue(target, out WorldInterestSite? report))
            {
                journeyDestinationReportedAt = now; journeyDestinationAge = report.Age;
                journeyDestinationRetryUntil = now + report.RetryAfter;
            }
            foreach (ScenicRegion region in journeyRegions)
                if (region.Id == target) journeyDestinationRetryUntil = Math.Max(journeyDestinationRetryUntil, region.AvailableAfter);
        }
    }

    private void ClearJourneyProgramme()
    {
        RetireJourneyQueue(); journeySection?.Dispose();
        journeyEndingPlanner.Dispose(); journeyEndingLease.Dispose(); journeyPendingSection = null; journeySection = null;
        journeyRunway.Reset(); journeyPreparationFailures = 0; journeyRetryAt = 0; journeyWindowScale = 1;
        journeyExitId = 0; journeyBraking = journeyPendingArrival = journeyEndingArrival = journeyExitExhausted = false;
        journeyObservedPlan = journeyDepartureSeed = null; journeyObservedSeedAt = float.NegativeInfinity;
        journeySelectionEvidence = "Selection not attempted"; nextJourneyWaitingTrace = 0;
        journeyExitElapsed = 0; journeyArrivedDestination = null; journeyArrivedSite = null;
        journeyDestinationSubject.Reset();
        journeyArrivalSurvey.Reset(); journeyArrivalSurveyStarted = false; nextJourneyArrivalSurvey = 0;
        // The world-scoped destination history survives local region changes.
    }

    private void ResetJourneyPreparation()
    {
        journeyPlanner.Reset(); journeyEndingPlanner.Reset(); journeyPendingSection = null;
        journeyPendingArrival = journeyPlanning = false;
    }

    // Queue membership and pending preparation form one dependency chain.
    // Removing any predecessor must also cancel work derived from its tail.
    private void RetireJourneyQueue()
    {
        ResetJourneyPreparation();
        foreach (JourneySection section in journeyQueue) section.Dispose();
        journeyQueue.Clear();
    }

    private bool JourneyHeadReady(ScenicShotPlan predecessor)
        => journeyQueue.Count > 0 && journeyQueue[0].Proof.CanAdmit &&
            JourneyRunwayPolicy.HeadMatches(journeyQueue[0].Plan, predecessor.Id, journey.Id,
                journeyQueue[0].Epoch, journeyQueue[0].PreparedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime);

    private double JourneyRunwayMetres()
    {
        ScenicShotPlan? current = director.Current;
        double metres = journeyOwned && current?.IsJourney == true
            ? current.TravelMetres * (1 - current.MotionFraction(director.Progress)) : 0;
        long predecessor = journeyOwned ? current?.Id ?? 0 : 0;
        foreach (JourneySection section in journeyQueue)
        {
            if (!section.Proof.CanAdmit || !JourneyRunwayPolicy.HeadMatches(section.Plan, predecessor, journey.Id,
                section.Epoch, section.PreparedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime)) break;
            metres += section.Plan.TravelMetres; predecessor = section.Plan.Id;
        }
        return metres;
    }

    private void PreparationFailed(string reason)
    {
        journeyRunway.ObservePreparation(Math.Max(.01, Time.unscaledTime - journeyPreparationStarted));
        journeyPreparationFailures++; journeyLastPreparationFailure = reason;
        // A failed local search is evidence about this window, not a terminal
        // outcome. Try a materially shorter route as loading progresses.
        journeyWindowScale = Math.Max(.4, journeyWindowScale * .7);
        journeyRetryAt = Time.unscaledTime + .35f;
        journeyStatus = "Retrying continuation with a shorter loaded window: " + reason;
        ResetJourneyPreparation();
        if (journeyPreparationFailures >= 3 && !journeyOwned)
            journeyEndingReason = "Three bounded departure searches failed: " + reason;
        if (Player.m_localPlayer) journeyFailureCarrier = Player.m_localPlayer.transform.position;
        TraceJourney("preparation-retry");
    }

    private void StepJourneyProofsAndPreparation(bool moveCarrier, double remaining)
    {
        double speed = Math.Max(2, journeyRunway.EffectiveCruise);
        double seconds = remaining / speed;
        bool continuationCritical = JourneyRunwayMetres() / speed <= journeyRunway.RequiredRunwaySeconds + 2;
        bool endingUrgent = !journeyEndingLease.CanAdmit ||
            seconds <= ScenicJourneyEnding.MaximumPreparationSeconds + 2 && journeyEndingLease.ProofSecondsRemaining <= seconds + 1;
        JourneySection? queued = null;
        foreach (JourneySection section in journeyQueue)
            if (section.Proof.RefreshDue) { queued = section; break; }
        bool queueUrgent = queued != null && (!queued.Proof.CanAdmit || seconds <= journeyRunway.RequiredRunwaySeconds);
        int phase = Time.frameCount % 3;
        // Healthy work leaves every third frame to observations/ordinary exit
        // coverage. Expiring proof and runway get a turn even on that frame.
        if (phase == 0 && !continuationCritical && !endingUrgent && !queueUrgent) return;
        // Critical consumers rotate first access to the shared scenic budget.
        // Routine four-second ending refresh never runs ahead of continuation.
        if (endingUrgent && phase == 0) journeyEndingLease.Step();
        if (queueUrgent && phase == 1) queued!.Proof.Step();
        if (!journeyBraking && journeyEndingReason == null) StepJourneyPreparation(moveCarrier);
        if (queued != null && !(queueUrgent && phase == 1)) queued.Proof.Step();
        if (!(endingUrgent && phase == 0)) journeyEndingLease.Step();
    }

    private bool CatalogueCurrent()
    {
        if (Time.unscaledTime - journeyCatalogueAt > 5 || journeyRegions.Count == 0) return false;
        if (!journey.Active || journey.Itinerary == null) return true;
        // Generated destinations are a paged FIFO inventory, not withdrawal
        // messages. Keep the selected world-scoped identity when its page is
        // evicted. Structures retain the actual report age; fresh negative
        // cooldown evidence still revokes admission.
        double now = Time.realtimeSinceStartupAsDouble;
        return ScenicJourneyDeparture.DestinationCurrent(journey.Itinerary.Site == null ||
            journey.Itinerary.Site.Kind == WorldInterestKind.Natural, journeyDestinationAge,
            now - journeyDestinationReportedAt, now < journeyDestinationRetryUntil);
    }

    private void ObserveArrivalEnvironment()
    {
        ScenicJourneyItinerary? itinerary = journey.Itinerary;
        if (itinerary == null || !Player.m_localPlayer ||
            Vector3.Distance(Player.m_localPlayer.transform.position, FromCore(itinerary.Destination.Position)) > 120) return;
        if (!journeyArrivalSurveyStarted || journeyArrivalSurvey.Complete && Time.unscaledTime >= nextJourneyArrivalSurvey)
        {
            journeyArrivalSurvey.Begin(FromCore(itinerary.Destination.Position)); journeyArrivalSurveyStarted = true;
            nextJourneyArrivalSurvey = Time.unscaledTime + 3;
        }
        if (!journeyArrivalSurvey.Complete) journeyArrivalSurvey.Step();
    }

    private ScenicFeature? ObservedArrivalSubject(AutonomyPosition from, double approachMetres = 20)
    {
        ScenicJourneyItinerary? itinerary = journey.Itinerary;
        if (itinerary == null || from.HorizontalDistance(itinerary.Destination.Position) > itinerary.ArrivalRadius + approachMetres) return null;
        ScenicFeature? best = null; double nearest = double.PositiveInfinity;
        IReadOnlyList<ScenicFeature> arrivalFeatures = journeyArrivalSurveyStarted && journeyArrivalSurvey.Complete
            ? journeyArrivalSurvey.Features : survey.Features;
        foreach (ScenicFeature feature in arrivalFeatures)
        {
            double distance = feature.Center.HorizontalDistance(itinerary.ArrivalFocus);
            if (distance > 64 || distance >= nearest || from.Distance(feature.Center) > 100) continue;
            if (itinerary.Site != null && journeyDestinationSubject.Site != null && !journeyDestinationSubject.Meaningful(feature)) continue;
            if (itinerary.Destination.Kind != ScenicDestinationKind.Landscape && feature.Kind != ScenicFeatureKind.Structure) continue;
            if (itinerary.Destination.Kind == ScenicDestinationKind.Landscape &&
                itinerary.Destination.Landscape == ScenicLandscape.Coast && feature.Kind != ScenicFeatureKind.Coastline) continue;
            if (itinerary.Destination.Kind == ScenicDestinationKind.Landscape && feature.Kind == ScenicFeatureKind.Structure) continue;
            best = feature; nearest = distance;
        }
        return best;
    }

    // Admission is transactional: a route and its independently validated
    // ending form one unit. A route alone can never take over the programme.
    private void StepJourneyPreparation(bool moveCarrier)
    {
        if (!journeyPlanning) return;
        Camera? camera = Camera.main;
        if (journeyPendingSection == null)
        {
            journeyPlanner.Step(); journeyStatus = journeyPlanner.Reason;
            if (!journeyPlanner.Complete) return;
            if (journeyPlanner.Ready == null || journeyPlanner.Epoch != CameraSpatialScene.Shared.Epoch)
            {
                PreparationFailed("Continuation unavailable: " + journeyPlanner.Reason); return;
            }
            journeyPendingSection = journeyPlanner.Ready;
            ScenicFeature? subject = ObservedArrivalSubject(journeyPendingSection.Sample(1).CameraPosition);
            journeyPendingArrival = subject != null;
            journeyEndingPlanner.Begin(journeyPendingSection, ++nextId, camera ? camera.aspect : 16f / 9f,
                camera ? camera.nearClipPlane : .3f, moveCarrier,
                subject == null ? (Vector3?)null : FromCore(subject.Center), subject);
        }
        journeyEndingPlanner.Step(); journeyStatus = journeyEndingPlanner.Reason;
        if (!journeyEndingPlanner.Complete) return;
        if (journeyEndingPlanner.Ready == null && journeyPendingArrival)
        {
            // A destination may be reached without a readable arrival. Retain
            // a validated abort ending, but never credit it as the destination.
            journeyPendingArrival = false;
            journeyEndingPlanner.Begin(journeyPendingSection, ++nextId, camera ? camera.aspect : 16f / 9f,
                camera ? camera.nearClipPlane : .3f, moveCarrier); return;
        }
        if (journeyEndingPlanner.Ready != null && journeyEndingPlanner.Epoch == CameraSpatialScene.Shared.Epoch)
        {
            journeyQueue.Add(new JourneySection(journeyPendingSection, journeyEndingPlanner.Ready,
                journeyEndingPlanner.Epoch, journeyPlanner.ValidatedAt, journeyEndingPlanner.ValidatedAt,
                journeyEndingPlanner.ArrivalFramed, moveCarrier));
            journeyRunway.ObservePreparation(Math.Max(.01, Time.unscaledTime - journeyPreparationStarted));
            journeyPreparationFailures = 0; journeyWindowScale = 1; journeyRetryAt = 0;
            ResetJourneyPreparation();
        }
        else PreparationFailed("No validated ending for the next section: " + journeyEndingPlanner.Reason);
    }

    private bool TickJourney(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        ScenicShotPlan? current = director.Current;
        if (!moveCarrier || !settings.ProceduralPlayers.Value || !settings.ScenicJourneys.Value && !requestedJourney && !manualJourney || !settings.DynamicShots.Value)
        {
            if (journey.Active || requestedJourney || manualJourney)
                FinishJourney(!moveCarrier ? "Journey unavailable: protected loading-carrier movement is disabled" :
                    !settings.ProceduralPlayers.Value ? "Journey unavailable: procedural camera is disabled" :
                    !settings.DynamicShots.Value ? "Journey unavailable: dynamic camera movement is disabled" : "Automatic journeys are disabled");
            return false;
        }
        if (journeyOwned && !journey.Active)
        { FinishJourney(journey.Reason, journey.Outcome); return false; }
        if (!journeyOwned) return TickDeparture(settings, Plan, moveCarrier, held, allowActivate, out changed);
        if (current == null || usingIndependentPicture || current.Id != journeyExitId && current.JourneyId != journey.Id)
        { FinishJourney("Journey yielded to a different picture owner"); return false; }
        if (!allowActivate) return true;
        if (held)
        {
            // Held output pauses the route, not proof ageing. Discard speculative
            // work rather than resuming a timed-out search after an operator hold.
            RetireJourneyQueue(); journeyEndingLease.Step(); return true;
        }
        journeyRunSeconds += Time.unscaledDeltaTime;
        ObserveArrivalEnvironment();

        if (journeyExitId != 0)
        {
            journeyExitElapsed += Mathf.Min(Time.unscaledDeltaTime, 1);
            journeyStatus = journeyEndingArrival ? "Arrival: showing " + journey.Itinerary!.Destination.Name : "Ending the partial journey with prepared local coverage";
            if (JourneyHandoffPolicy.EndingComplete(journeyExitElapsed, director.Progress) ||
                director.BlocksMovement && director.Progress < .999999)
            {
                bool complete = journey.TargetReached;
                FinishJourney(complete ? "Arrived and presented " + journey.Itinerary!.Destination.Name :
                    journeyEndingReason ?? "Journey ended before a readable destination arrival", complete ? ScenicJourneyOutcome.Completed : ScenicJourneyOutcome.Blocked);
                journeyExitExhausted = director.Prepared == null;
                return false;
            }
            return true;
        }

        if (director.BlocksMovement && director.Progress < .999999)
        {
            FinishJourney("Journey interrupted by live validation: " + director.Reason, ScenicJourneyOutcome.Blocked);
            journeyExitExhausted = director.Prepared == null; return false;
        }
        // Local observations progress concurrently with the Journey. Keep the
        // ordinary exit pipeline near the live carrier instead of its old origin.
        Camera? liveCamera = Camera.main;
        if (Player.m_localPlayer)
        {
            Vector3 carrier = Player.m_localPlayer.transform.position;
            if (director.Prepared != null && (preparedEpoch != CameraSpatialScene.Shared.Epoch ||
                !WithinJourneyHorizon(director.Prepared, carrier))) director.TakePrepared();
            if (searching) RefreshComparisonCandidates();
            // Retain useful work across small carrier movements, but do not
            // spend the exit budget finishing a route now outside loaded reach.
            if (testing != null && !WithinJourneyHorizon(testing, carrier) ||
                planning && (carrier - journeyOrdinarySearchOrigin).sqrMagnitude > 72 * 72)
            {
                CancelComparison("journey preparation moved outside loading horizon");
                testing = null; evaluator.Reset(); spatial.Reset(); searching = planning = false; retryAt = 0;
            }
        }
        if (liveCamera && (liveCamera.transform.position - origin).sqrMagnitude > 24 * 24)
        {
            origin = liveCamera.transform.position; ScenicFoliageScene.Shared.SetRegion(origin);
            // Moving the availability anchor must not restart an in-flight
            // ordinary shot search every few seconds. Finish its bounded work;
            // refresh observations separately and revalidate at actual admission.
            if (!refreshingSurvey && (origin - journeySurveyOrigin).sqrMagnitude > 36 * 36)
            {
                refreshSurvey.Begin(origin); refreshingSurvey = true;
                journeySurveyOrigin = origin; surveyRefreshAt = Time.unscaledTime + 15;
            }
        }
        if (!CatalogueCurrent()) journeyEndingReason = "Journey destination report expired, discovery became stale, or a retry restriction was reported";
        if (journey.BudgetExpired || journeyRunSeconds > journey.MaximumSeconds + 30)
            journeyEndingReason = "Journey runtime expired before observed arrival";
        if (journeyEpoch != CameraSpatialScene.Shared.Epoch)
        {
            RetireJourneyQueue(); journeyEpoch = CameraSpatialScene.Shared.Epoch;
            // Current live checks remain authoritative; an obsolete speculative
            // proof cannot supply a successor or postpone the ending decision.
        }
        double remaining = current.TravelMetres * (1 - current.MotionFraction(director.Progress));
        // Aged proofs are renewed in place. Destroying the queue on age alone
        // makes slow travel impossible and can orphan an in-flight successor.
        StepJourneyProofsAndPreparation(moveCarrier, remaining);
        if (journeyQueue.Count > 0 && journeyQueue[0].Proof.Contradicted)
        {
            journeyLastPreparationFailure = journeyQueue[0].Proof.Reason;
            RetireJourneyQueue(); journeyRetryAt = Time.unscaledTime + .35f;
        }
        bool successorReady = JourneyHeadReady(current);
        bool destinationNear = journeySection?.ArrivalFramed == true &&
            current.Sample(1).CameraPosition.HorizontalDistance(journey.Itinerary!.Destination.Position) <= journey.Itinerary.ArrivalRadius;
        if (!journeyBraking && JourneyHandoffPolicy.ShouldBrake(journeyBraking, remaining, Math.Max(2, journeyRunway.EffectiveCruise),
            successorReady && journeyEndingReason == null, destinationNear))
        {
            journeyBraking = true;
            if (!destinationNear && journeyEndingReason == null)
                journeyEndingReason = "Continuation was not ready before the planned braking point";
            RetireJourneyQueue();
            ScenicTrace.Event("journey-ending-committed", journeyEndingReason ?? "Approaching the selected destination", current.Id);
        }
        if (director.Progress >= .999999)
        {
            if (!journeyBraking && successorReady)
            {
                JourneySection next = journeyQueue[0]; journeyQueue.RemoveAt(0);
                AdmitJourneySection(next); changed = true; return true;
            }
            if (journeySection != null && journeySection.Plan.Id == current.Id && journeyEndingLease.CanAdmit)
            {
                director.Accept(journeyEndingLease.Plan!, Time.unscaledTime); proposed = 0; forceReplacement = false;
                journeyExitId = journeySection.Ending.Id; journeyExitElapsed = 0;
                journeyEndingArrival = journeySection.ArrivalFramed && journey.AtArrival;
                usingIndependentPicture = false;
                ScenicTrace.Event("journey-ending-admitted", journeyEndingArrival ? "Observed destination arrival" : "Prepared partial-journey ending", journeyExitId);
                changed = true; return true;
            }
            FinishJourney("Journey ending proof expired or was invalidated: " + journeyEndingLease.Reason,
                ScenicJourneyOutcome.Blocked); journeyExitExhausted = director.Prepared == null; return false;
        }
        // Repeated failure at the same seam/loading state does not burn more
        // work. Certified travel may advance the horizon enough for a new
        // attempt; otherwise the normal runway/braking deadline ends safely.
        if (journeyPreparationFailures >= 3 && Player.m_localPlayer &&
            (Player.m_localPlayer.transform.position - journeyFailureCarrier).sqrMagnitude >= 8 * 8)
        { journeyPreparationFailures = 0; journeyWindowScale = 1; }
        if (!journeyBraking && !journeyPlanning && journeyEndingReason == null && journeyQueue.Count < 2 &&
            journeyPreparationFailures < 3 && Time.unscaledTime >= journeyRetryAt)
        {
            ScenicShotPlan predecessor = journeyQueue.Count > 0 ? journeyQueue[journeyQueue.Count - 1].Plan : current;
            double committed = remaining;
            foreach (JourneySection queued in journeyQueue) committed += queued.Plan.TravelMetres;
            AutonomyPosition seam = predecessor.Sample(1).CameraPosition;
            double metres = Math.Min(journey.NextWindowMetres(committed),
                Math.Max(ScenicJourneyPolicy.MinimumWindowMetres,
                    journey.Itinerary!.RouteMetres - journey.Itinerary.ProjectProgress(seam)));
            metres = Player.m_localPlayer ? JourneyRunwayPolicy.WindowMetres(journey.CruiseSpeed,
                Vector3.Distance(Player.m_localPlayer.transform.position, FromCore(seam)), metres * journeyWindowScale) : 0;
            if (metres >= ScenicJourneyPolicy.MinimumWindowMetres)
            {
                ScenicFeature? arrival = ObservedArrivalSubject(seam, metres + 8);
                if (arrival == null && seam.HorizontalDistance(journey.Itinerary.Destination.Position) <= metres + journey.Itinerary.ArrivalRadius)
                {
                    // Do not send an ordinary cruise through a catalogue point
                    // while its actual destination subject is still unobserved.
                    journeyStatus = "Awaiting loaded destination observations for the final approach";
                    return true;
                }
                journeyPlanner.Begin(predecessor, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
                    liveCamera ? liveCamera.aspect : 16f / 9f, liveCamera ? liveCamera.nearClipPlane : .3f,
                    moveCarrier, false, journey.SteeringGoal(seam), arrival == null ? (AutonomyPosition?)null : arrival.Center,
                    arrivalCenter: arrival == null ? (AutonomyPosition?)null : journey.Itinerary.Destination.Position,
                    arrivalRadius: journey.Itinerary.ArrivalRadius);
                journeyPlanning = true; journeyPreparationStarted = Time.unscaledTime;
            }
            else journeyStatus = "Waiting for the loaded camera-and-focus horizon to advance; retaining certified runway";
        }
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return true;
    }

    private void AdmitJourneySection(JourneySection section)
    {
        journeySection?.Dispose(); journeySection = section; director.Accept(section.Plan, Time.unscaledTime);
        Camera? camera = Camera.main;
        journeyEndingLease.Begin(section.Ending, section.Epoch, section.Proof.EndingValidatedAt,
            camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, true);
        proposed = 0; forceReplacement = false; usingIndependentPicture = false;
        journeyPredecessor = section.Plan.Id; journeyBraking = false;
        journeyStatus = "Travelling to " + journey.Itinerary!.Destination.Name + "; validated ending ready";
    }

    private static bool WithinJourneyHorizon(ScenicShotPlan plan, Vector3 carrier) =>
        (FromCore(plan.Sample(0).CameraPosition) - carrier).sqrMagnitude <= 75 * 75 &&
        (FromCore(plan.Sample(1).CameraPosition) - carrier).sqrMagnitude <= 75 * 75;

    private static bool WithinDepartureHorizon(ScenicShotPlan plan, Vector3 carrier)
    {
        // Match the 85m planning bound, retaining margin inside the exact
        // evaluator's 91m carrier check. Do not silently tighten it to 75m
        // only after a complete route has already been prepared.
        for (int i = 0; i <= 16; i++)
            if ((FromCore(plan.Sample(i / 16d).CameraPosition) - carrier).sqrMagnitude > 85 * 85) return false;
        return true;
    }

    private bool TickDeparture(BroadcastSettings settings, ScenicShotPlan? current, bool moveCarrier,
        bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        if ((requestedJourney || manualJourney) && held) journeyRequestExpiresAt += Mathf.Max(0, Time.unscaledDeltaTime);
        if ((requestedJourney || manualJourney) && Time.unscaledTime >= journeyRequestExpiresAt)
        {
            string blocked = journeyStatus;
            AbandonDeparture("Journey not started within 90 seconds; last wait: " + blocked);
            requestedJourney = false; return false;
        }
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (!journey.Active)
        {
            if (!requestedJourney && (Time.unscaledTime < nextJourneyAt || director.RememberedShots < 2)) return false;
            if (!allowActivate) return WaitForJourney("Journey waiting for Scenic programme ownership");
            if (held) return WaitForJourney("Journey waiting while the operator holds the picture");
            if (!HasUsablePicture || current == null)
                return WaitForJourney("Journey waiting for a readable current Scenic picture");
            if (journeyObservedPlan == null || journeyObservedPlan.Id != current.Id || Time.unscaledTime - journeyObservedSeedAt > .5f)
                return WaitForJourney("Journey waiting for the current camera's observed departure pose");
            if (!departure.CanAttempt(current.Id, epoch))
                return WaitForJourney("Journey waiting for a different departure viewpoint after the previous local rejection");
            if (Time.unscaledTime < nextJourneySelection) return WaitForJourney("Journey waiting for its bounded destination selection interval");
            if (!CatalogueCurrent()) return WaitForJourney("Journey waiting for a fresh known-destination catalogue");
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var selectionWork))
                return WaitForJourney("Journey destination selection waiting for shared camera work budget");
            ScenicJourneyItinerary? itinerary;
            ScenicShotPlan seed;
            using (selectionWork)
            {
                nextJourneySelection = Time.unscaledTime + 5;
                seed = ScenicJourneyDeparture.Snapshot(journeyObservedPlan, journeyObservedPosition, journeyObservedAim, journeyObservedFov);
                bool selected = ScenicJourneyItinerary.TrySelect(seed.Sample(0).CameraPosition,
                    ScenicJourneyPlanner.ObjectiveHeading(seed), journeyRegions, journeySites,
                    settings.ScenicJourneyDistance.Value, Time.realtimeSinceStartupAsDouble,
                    recentJourneyDestinations, out itinerary, out string reason, out ScenicJourneySelectionEvidence evidence, allowReverseDeparture: true);
                journeySelectionEvidence = evidence.Summary;
                journeyStatus = reason;
                if (ScenicTrace.Enabled)
                {
                    AutonomyPosition departurePosition = seed.Sample(0).CameraPosition;
                    AutonomyPosition heading = ScenicJourneyPlanner.ObjectiveHeading(seed);
                    ScenicTrace.Record(ScenicTrace.Row("journey-selection").Field("selected", selected).Field("reason", reason)
                        .Field("requestedMetres", settings.ScenicJourneyDistance.Value).Field("clock", Time.realtimeSinceStartupAsDouble)
                        .Field("originX", departurePosition.X).Field("originY", departurePosition.Y).Field("originZ", departurePosition.Z)
                        .Field("headingX", heading.X).Field("headingZ", heading.Z)
                        .Field("catalogue", evidence.CatalogueRegions).Field("usable", evidence.UsableObservations)
                        .Field("nearDeparture", evidence.NearDeparture).Field("connected", evidence.ConnectedObservations)
                        .Field("connectedCandidates", evidence.ConnectedCandidates).Field("bearingCandidates", evidence.BearingCandidates)
                        .Field("cooldownExcluded", evidence.CooldownExcluded).Field("staleExcluded", evidence.StaleExcluded)
                        .Field("radiusExcluded", evidence.RadiusExcluded).Field("recentExcluded", evidence.RecentExcluded)
                        .Field("headingExcluded", evidence.HeadingExcluded).Field("scaleExcluded", evidence.ScaleExcluded)
                        .Field("destination", itinerary?.Destination.Id ?? "none")
                        .Field("connectedMetadata", itinerary?.UsesConnectedObservations ?? false));
                }
                if (!selected) return false;
            }
            journeyDepartureSeed = seed;
            departure.BeginIndependent(journeyDepartureSeed.Id, epoch,
                2 * (ScenicJourneyPolicy.PreparationSeconds + ScenicJourneyEnding.MaximumPreparationSeconds));
            journey.Start(++nextId, itinerary!, settings.ScenicJourneySpeed.Value);
            journeyRunway.Reset(); journeyPreparationFailures = 0; journeyWindowScale = 1; journeyRetryAt = 0;
            journeyLastPreparationFailure = "none";
            journeyDestinationReportedAt = Time.realtimeSinceStartupAsDouble;
            journeyDestinationAge = itinerary!.Site?.Age ?? 0;
            journeyDestinationRetryUntil = 0;
            journeyDestinationSubject.Set(itinerary!.Site); journeySurveyOrigin = origin;
            journeyArrivalSurvey.Reset(); journeyArrivalSurveyStarted = false; nextJourneyArrivalSurvey = 0;
            manualJourney = requestedJourney; requestedJourney = false; journeyPredecessor = journeyDepartureSeed.Id;
            ResetJourneyPreparation(); journeyEndingReason = null; journeyRunSeconds = 0; journeyExitId = 0;
            journeyBraking = journeyEndingArrival = journeyExitExhausted = false;
            journeyEpoch = epoch; RetireJourneyQueue(); journeySection?.Dispose(); journeySection = null;
            journeyStatus = "Preparing independent departure, route and ending for " + itinerary!.Description;
            nextJourneyTrace = 0; TraceJourney("departure-start");
        }
        // The departure owns a frozen proposal, not the ordinary on-air shot.
        // An ordinary edit does not invalidate a separately validated cut.
        Player? loadingPlayer = Player.m_localPlayer;
        ScenicShotPlan? frozenSeed = journeyDepartureSeed;
        if (current == null || !HasUsablePicture || frozenSeed == null || loadingPlayer == null || !loadingPlayer ||
            epoch != journeyEpoch || !CatalogueCurrent() || !allowActivate ||
            !WithinDepartureHorizon(frozenSeed, loadingPlayer.transform.position))
        { AbandonDeparture("Journey departure lost readable coverage, its scene or local loading reach; ordinary coverage retained"); return false; }
        Vector3 carrier = loadingPlayer.transform.position;
        if (held) { RetireJourneyQueue(); return false; }
        // Reserve one background frame in three for the ordinary picture while
        // certifying the startup reserve; it retains picture ownership.
        if (Time.frameCount % 3 != 0) StepJourneyPreparation(moveCarrier);
        foreach (JourneySection queued in journeyQueue)
        {
            if (!WithinDepartureHorizon(queued.Plan, carrier) || !WithinDepartureHorizon(queued.Ending, carrier) || queued.Proof.Contradicted)
            { RetireJourneyQueue(); journeyStatus = "Departure reserve lost local reach or was contradicted; rebuilding"; break; }
            // Renew the older route/ending while its successor is prepared.
            // Every third frame gives renewal first access to the allowance.
            if (queued.Proof.RefreshDue && (Time.frameCount % 3 == 0 || !journeyPlanning)) { queued.Proof.Step(); break; }
        }
        if (journeyEndingReason != null) { AbandonDeparture(journeyEndingReason); return false; }
        double reserve = JourneyRunwayMetres();
        int freshSections = 0; long preceding = 0;
        foreach (JourneySection queued in journeyQueue)
        {
            if (!queued.Proof.CanAdmit || !JourneyRunwayPolicy.HeadMatches(queued.Plan, preceding, journey.Id,
                queued.Epoch, queued.PreparedAt, epoch, Time.unscaledTime)) break;
            freshSections++; preceding = queued.Plan.Id;
        }
        bool ready = journeyRunway.TryStart(freshSections, reserve, journey.CruiseSpeed, out string reserveReason);
        if (!ready && !journeyPlanning) journeyStatus = reserveReason;
        bool cutDue = ScenicJourneyDeparture.CutReady(current, ShownSeconds, !usingIndependentPicture && NeedsReplacement);
        ScenicJourneyDepartureState state = departure.Observe(current.Id, epoch, cutDue,
            ready, false, true, Mathf.Max(0, Time.unscaledDeltaTime));
        if (state == ScenicJourneyDepartureState.Abandoned)
        { AbandonDeparture(departure.Reason); return false; }
        if (state == ScenicJourneyDepartureState.Admitted)
        {
            JourneySection first = journeyQueue[0]; journeyQueue.RemoveAt(0);
            if (first.Plan.ContinuationOf != 0 || first.Plan.JourneyId != journey.Id)
            { AbandonDeparture("Departure was not an independently framed validated cut"); return false; }
            AdmitJourneySection(first); journeyOwned = true; journeyRunSeconds = 0; journeyRequestExpiresAt = 0;
            destinationSubject.Reset(); destinationSurvey.Reset(); destinationFeatures.Clear(); nextDestinationSurvey = 0;
            journeyDepartureSeed = null;
            TraceJourney("start"); changed = true; return true;
        }
        if (!journeyPlanning && journeyQueue.Count < 2 && Time.unscaledTime >= journeyRetryAt)
        {
            bool first = journeyQueue.Count == 0;
            ScenicShotPlan predecessor = first ? frozenSeed : journeyQueue[journeyQueue.Count - 1].Plan;
            AutonomyPosition seam = predecessor.Sample(first ? 0 : 1).CameraPosition;
            double committed = 0; foreach (JourneySection queued in journeyQueue) committed += queued.Plan.TravelMetres;
            double available = JourneyRunwayPolicy.WindowMetres(journey.CruiseSpeed,
                Vector3.Distance(carrier, FromCore(seam)), ScenicJourneyPolicy.WindowMetres);
            double metres = Math.Min(journey.NextWindowMetres(committed) * journeyWindowScale,
                first ? available * .5 : available);
            if (metres >= ScenicJourneyPolicy.MinimumWindowMetres)
            {
                Camera? camera = Camera.main;
                journeyPlanner.Begin(predecessor, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
                    camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier,
                    false, journey.SteeringGoal(seam), null, departureCut: first,
                    nativeSplineOverride: settings.NativeSplineDolly.Value);
                journeyPlanning = true; journeyPreparationStarted = Time.unscaledTime;
                journeyStatus = "Preparing startup reserve section " + (journeyQueue.Count + 1) + "/2; ordinary coverage retained";
            }
            else
            { AbandonDeparture("Journey departure cannot fit two certified sections inside loaded camera-and-focus reach; retry from the next viewpoint"); return false; }
        }
        if (ready) journeyStatus = departure.Reason;
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("departure-progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return false;
    }

    private void AbandonDeparture(string reason)
    {
        bool retry = manualJourney && Time.unscaledTime < journeyRequestExpiresAt;
        departure.Abandon(reason);
        if (journey.Active) journey.Finish(ScenicJourneyOutcome.Blocked, reason);
        journeyStatus = reason; TraceJourney("departure-abandoned");
        RetireJourneyQueue(); journeyOwned = manualJourney = false;
        journeyPredecessor = 0; journeyEndingReason = null; journeySection = null; journeyDepartureSeed = null;
        requestedJourney = retry; nextJourneyAt = Time.unscaledTime + 8;
    }

    private void FinishJourney(string reason, ScenicJourneyOutcome outcome = ScenicJourneyOutcome.Interrupted)
    {
        bool wasOwned = journeyOwned; bool wasRequested = requestedJourney || manualJourney || departure.Pending;
        if (journey.Active) journey.Finish(outcome, reason);
        if (wasOwned)
        {
            journeyEndRevision++;
            journeyArrivedDestination = journey.Outcome == ScenicJourneyOutcome.Completed ? journey.Itinerary?.Destination : null;
            journeyArrivedSite = journeyArrivedDestination == null ? null : journey.Itinerary?.Site;
            if (journey.Itinerary != null)
            {
                recentJourneyDestinations.Enqueue(journey.Itinerary.Destination.FamilyId);
                while (recentJourneyDestinations.Count > 8) recentJourneyDestinations.Dequeue();
            }
        }
        journeyOwned = false; requestedJourney = manualJourney = false;
        RetireJourneyQueue(); journeyPredecessor = 0; nextJourneyAt = Time.unscaledTime + 180;
        journeyStatus = reason;
        if (wasOwned || wasRequested) TraceJourney("finish");
        departure.Reset(); journeyRequestExpiresAt = 0;
        if (Plan?.IsJourney == true || Plan?.Id == journeyExitId) director.RequestReplacement(reason);
        journeySection?.Dispose(); journeySection = null; journeyDepartureSeed = null; journeyExitId = 0; journeyBraking = false;
        journeyEndingLease.Reset();
    }

    private void TraceJourney(string phase)
    {
        if (!ScenicTrace.Enabled) return;
        double ahead = Plan?.IsJourney == true ? Plan.TravelMetres * (1 - Plan.MotionFraction(director.Progress)) : 0;
        foreach (JourneySection section in journeyQueue) ahead += section.Plan.TravelMetres;
        ScenicTrace.Record(ScenicTrace.Row("journey").Field("phase", phase).Field("journey", journey.Id)
            .Field("owned", journeyOwned).Field("departureState", departure.State.ToString()).Field("plan", Plan?.Id ?? 0)
            .Field("requested", requestedJourney || manualJourney).Field("requestRemaining", Math.Max(0, journeyRequestExpiresAt - Time.unscaledTime))
            .Field("seed", journeyDepartureSeed?.Id ?? 0).Field("catalogueRegions", journeyRegions.Count)
            .Field("selection", journeySelectionEvidence).Field("connectedMetadata", journey.Itinerary?.UsesConnectedObservations ?? false)
            .Field("usablePicture", HasUsablePicture).Field("ordinaryPrepared", director.Prepared?.Id ?? 0)
            .Field("outcome", journey.Outcome.ToString()).Field("reason", journeyStatus)
            .Field("destination", journey.Itinerary?.Destination.Id ?? "none").Field("destinationName", journey.Itinerary?.Destination.Name ?? "none")
            .Field("knownWaypoints", journey.Itinerary?.Waypoints.Count ?? 0).Field("routeProgress", journey.RouteProgressMetres)
            .Field("targetMetres", journey.TargetMetres).Field("measuredMetres", journey.TravelMetres)
            .Field("displacementMetres", journey.DisplacementMetres).Field("advanceMetres", journey.ObjectiveAdvanceMetres)
            .Field("cruise", journey.CruiseSpeed).Field("effectiveCruise", journeyRunway.EffectiveCruise)
            .Field("preparationSeconds", journeyRunway.PreparationSeconds).Field("requiredRunwaySeconds", journeyRunway.RequiredRunwaySeconds)
            .Field("freshRunwayMetres", JourneyRunwayMetres()).Field("preparationFailures", journeyPreparationFailures)
            .Field("lastPreparationFailure", journeyLastPreparationFailure).Field("achievedSpeed", journey.AchievedMetresPerSecond)
            .Field("shownSeconds", journey.ShownSeconds).Field("arrivalShownSeconds", journey.ArrivalShownSeconds)
            .Field("readyMetresAhead", ahead).Field("queued", journeyQueue.Count)
            .Field("endingPrepared", journeySection != null).Field("braking", journeyBraking).Field("ending", journeyExitId)
            .Field("loading", journeyPlanner.WaitingForLoading).Field("planner", journeyPlanner.Reason));
    }
}
