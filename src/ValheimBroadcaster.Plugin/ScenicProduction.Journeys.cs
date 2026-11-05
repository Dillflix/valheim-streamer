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
    private bool journeyRepairing, journeyRetainsPicture;
    private float journeyRepairMissingSeconds;
    private long journeyAuthorityWorld;
    private bool journeyAuthorized, journeyWatching, journeyHeld;
    private ScenicShotPlan? journeyRepairSeed;
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
    private ScenicShotPlan? journeyDepartureSeed;
    private float nextJourneyWaitingTrace;
    private string journeySelectionEvidence = "Selection not attempted";
    private double journeyDestinationReportedAt, journeyDestinationRetryUntil;
    private float journeyDestinationAge;

    private void RememberJourneyDeparturePose(ScenicShotPlan shown, JourneyPresentationSample? capture, Vector3 focus)
        => RecordJourneyExitCapture(shown, capture, focus);

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
    private float journeyCatalogueAt = float.NegativeInfinity, nextJourneySelection;
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
        journeyPlanner.ForgetSearchEpisode();
        journeyEndingPlanner.Dispose(); journeyEndingLease.Dispose(); journeyPendingSection = null; journeySection = null;
        journeyRunway.Reset(); journeyPreparationFailures = 0; journeyRetryAt = 0; journeyWindowScale = 1;
        journeyTravel.Reset(); journeyPlayback.Reset(); journeyRepairing = journeyRetainsPicture = false; journeyRepairMissingSeconds = 0; journeyRepairSeed = null;
        journeyExitId = 0; journeyBraking = journeyPendingArrival = journeyEndingArrival = journeyExitExhausted = false;
        journeyDepartureSeed = null;
        journeySelectionEvidence = "Selection not attempted"; nextJourneyWaitingTrace = 0;
 journeyArrivedDestination = null; journeyArrivedSite = null;
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
    private void RetireJourneyQueue(int firstInvalid = 0) =>
        JourneyQueueRetention.RetireFrom(journeyQueue, firstInvalid, ResetJourneyPreparation);

    private bool JourneyHeadReady(ScenicShotPlan predecessor)
        => journeyQueue.Count > 0 && journeyQueue[0].Proof.CanAdmit &&
            JourneyRunwayPolicy.HeadMatches(journeyQueue[0].Plan, predecessor.Id, journey.Id,
                journeyQueue[0].Epoch, journeyQueue[0].PreparedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime);

    private double JourneyRunwayMetres()
    {
        ScenicShotPlan? current = journeyPlayback.Current;
        double metres = journeyOwned && !journeyRepairing && current?.IsJourney == true
            ? current.TravelMetres * (1 - current.MotionFraction(journeyPlayback.Progress)) : 0;
        long predecessor = journeyOwned && !journeyRepairing ? current?.Id ?? 0 : 0;
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
        journeyPreparationFailures++; journeyLastPreparationFailure = reason;
        // A failed local search is evidence about this window, not a terminal
        // outcome. Try a materially shorter route as loading progresses.
        journeyWindowScale = Math.Max(.4, journeyWindowScale * .7);
        journeyRetryAt = Time.unscaledTime + .35f;
        journeyStatus = "Retrying continuation with a shorter loaded window: " + reason;
        ResetJourneyPreparation();
        // A failed search is not producer throughput and does not cancel the
        // destination. The persistent request deadline bounds repeated work.
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
            journeyRunway.ObservePreparation(journeyPendingSection.TravelMetres, journeyPreparationStarted, Time.unscaledTime);
            journeyPreparationFailures = 0; journeyWindowScale = 1; journeyRetryAt = 0;
            ResetJourneyPreparation();
        }
        else PreparationFailed("No validated ending for the next section: " + journeyEndingPlanner.Reason);
    }

    // Called before ordinary direction, including while the picture is unavailable.
    internal void UpdateJourneyAuthority(long worldId, bool authorized, bool watching, bool held, bool preempted)
    {
        journeyAuthorityWorld = worldId; journeyAuthorized = authorized; journeyWatching = watching; journeyHeld = held;
        if (!journeyTravel.Active) return;
        long epoch = CameraSpatialScene.Shared.Epoch;
        bool local = watching && Player.m_localPlayer;
        ScenicShotPlan? current = journeyPlayback.Current;
        bool physicalBlocked = JourneyOwnsPicture && journeyPlayback.PhysicalBlocked;
        bool observationPending = JourneyOwnsPicture && JourneyFailurePending(journeyLiveFailure);
        JourneyRouteEvidence route = current != null && JourneyOwnsPicture && !journeyRepairing && !physicalBlocked && epoch == journeyEpoch
            ? new JourneyRouteEvidence(current.Id, epoch, current.IsJourney
                ? current.TravelMetres * (1 - current.MotionFraction(journeyPlayback.Progress)) : 0, stationary: !current.IsJourney) : default;
        journeyTravel.Observe(new JourneyTravelObservation(Time.unscaledTime, worldId, epoch,
            authorized ? JourneyTravelAuthorization.Granted : JourneyTravelAuthorization.Revoked,
            worldId == 0 ? JourneyTravelAvailability.Lost : local ? JourneyTravelAvailability.Ready : JourneyTravelAvailability.Pending,
            preempted ? JourneyTravelPreemption.Player : JourneyTravelPreemption.None,
            physicalBlocked ? JourneyTravelNavigation.Blocked : !local ? JourneyTravelNavigation.WaitingForLoading :
                observationPending ? JourneyTravelNavigation.WaitingForObservation : JourneyTravelNavigation.Clear,
            held, HasUsablePicture, journey.AtArrival, journey.TargetReached, route,
            HasUsablePicture ? JourneyTravelEditorial.Clear : JourneyTravelEditorial.ChangingShot, renderedTravelMetres: journey.TravelMetres));
        if (!journeyTravel.Active)
            FinishJourney(journeyTravel.Reason, journeyTravel.State == JourneyTravelState.Completed
                ? ScenicJourneyOutcome.Completed : ScenicJourneyOutcome.Blocked);
    }

    private bool TickJourney(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate, out bool changed)
    {
        changed = false;
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
        if (!journeyTravel.Active) return false;
        if (held || !journeyWatching || !journeyAuthorized) return JourneyOwnsPicture;
        ObserveArrivalEnvironment();
        if (journeyEpoch != CameraSpatialScene.Shared.Epoch)
        {
            journeyEpoch = CameraSpatialScene.Shared.Epoch;
            BeginJourneyCoverageRecovery("Local observations changed; rebuilding the same travel mission");
        }
        if (journeyTravel.LocalReplanRequested && !journeyRepairing)
            BeginJourneyCoverageRecovery("No measured travel progress; searching a different local departure toward the same destination");
        if (journeyRepairing) return TickJourneyRecovery(settings, Plan, moveCarrier, out changed);
        ScenicShotPlan? current = journeyPlayback.Current;
        if (current == null) { BeginJourneyCoverageRecovery("No admitted physical route remains"); return false; }
        if (journeyExitId != 0)
        {
            journeyStatus = "Arrival: showing " + journey.Itinerary!.Destination.Name;
            if (journey.TargetReached)
            { FinishJourney("Arrived and presented " + journey.Itinerary.Destination.Name, ScenicJourneyOutcome.Completed); return false; }
            if (journeyPlayback.PhysicalBlocked || journeyPlayback.ElapsedSeconds >= JourneyHandoffPolicy.EndingSeconds + JourneyHandoffPolicy.MaximumSettledSeconds)
            { BeginJourneyCoverageRecovery("Arrival needs another observed viewpoint"); return false; }
            return true;
        }
        if (journeyPlayback.PhysicalBlocked || journeyViewBlockedSeconds >= 2)
        {
            BeginJourneyCoverageRecovery(journeyPlayback.PhysicalBlocked
                ? "The live physical path is blocked; replanning toward the same destination"
                : "The current view remained obscured; replanning toward the same destination");
            return TickJourneyRecovery(settings, Plan, moveCarrier, out changed);
        }
        MaintainJourneyLocalObservations();
        if (journey.BudgetExpired)
        { FinishJourney("Journey exhausted its measured travel budget", ScenicJourneyOutcome.BudgetExpired); return false; }
        double remaining = current.TravelMetres * (1 - current.MotionFraction(journeyPlayback.Progress));
        StepJourneyProofsAndPreparation(moveCarrier, remaining);
        RefreshJourneyQueue(moveCarrier);
        bool successorReady = JourneyHeadReady(current);
        bool arrival = journeySection?.ArrivalFramed == true && journey.AtArrival;
        if (arrival && journeyEndingLease.CanAdmit &&
            remaining <= JourneyRunwayPolicy.StoppingDistance(journeyRunway.EffectiveCruise) + 2)
        {
            journeyBraking = true;
            // Arrival is the only editorial ending. A temporary empty queue
            // decelerates on the current route and can resume without a cut.
        }
        if (journeyPlayback.Progress >= .999999)
        {
            if (successorReady && !arrival)
            {
                JourneySection next = journeyQueue[0];
                if (AdmitJourneySection(next)) { journeyQueue.RemoveAt(0); changed = true; }
                return true;
            }
            if (arrival && journeyEndingLease.CanAdmit)
            {
                ScenicShotPlan ending = journeyEndingLease.Plan!;
                if (journeyTravel.TryAcceptRoute(new JourneyRouteEvidence(ending.Id, CameraSpatialScene.Shared.Epoch, 0, stationary: true), Time.unscaledTime, out _))
                {
                    journeyPlayback.Accept(ending, Time.unscaledTimeAsDouble); journeyExitId = ending.Id;
                    journeyEndingArrival = true; journeyBraking = false; proposed = 0;
                    TraceJourney("arrival-start"); changed = true;
                }
                return true;
            }
        }
        // A bounded route buffer is replenished according to useful metres,
        // available loaded reach and stopping distance, not an arbitrary count2.
        PrepareJourneyTail(settings, current, remaining, moveCarrier, false);
        journeyStatus = journeyRunway.EffectiveCruise < .1
            ? "Paused on validated route while its continuation is prepared: " + journeyPlanner.Reason
            : "Travelling to " + journey.Itinerary!.Destination.Name + "; " + journeyPlanner.Reason;
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return true;
    }

    private void MaintainJourneyLocalObservations()
    {
        Camera? camera = Camera.main;
        if (!camera || !Player.m_localPlayer) return;
        Vector3 carrier = Player.m_localPlayer.transform.position;
        if (director.Prepared != null && (preparedEpoch != CameraSpatialScene.Shared.Epoch ||
            !WithinJourneyHorizon(director.Prepared, carrier))) director.TakePrepared();
        if (testing != null && !WithinJourneyHorizon(testing, carrier) ||
            planning && (carrier - journeyOrdinarySearchOrigin).sqrMagnitude > 72 * 72)
        {
            CancelComparison("Journey moved outside ordinary planning range");
            testing = null; evaluator.Reset(); spatial.Reset(); searching = planning = false; retryAt = 0;
        }
        if ((camera.transform.position - origin).sqrMagnitude <= 24 * 24) return;
        origin = camera.transform.position; ScenicFoliageScene.Shared.SetRegion(origin);
        if (!refreshingSurvey && (origin - journeySurveyOrigin).sqrMagnitude > 36 * 36)
        {
            refreshSurvey.Begin(origin); refreshingSurvey = true;
            journeySurveyOrigin = origin; surveyRefreshAt = Time.unscaledTime + 15;
        }
    }

    private void RefreshJourneyQueue(bool moveCarrier)
    {
        if (!Player.m_localPlayer) return;
        for (int i = 0; i < journeyQueue.Count; i++)
        {
            JourneySection queued = journeyQueue[i];
            if (queued.Proof.Contradicted || !WithinDepartureHorizon(queued.Plan, Player.m_localPlayer.transform.position))
            {
                journeyLastPreparationFailure = queued.Proof.Reason;
                RetireJourneyQueue(i); journeyRetryAt = Time.unscaledTime + .35f; break;
            }
            if (queued.Proof.RefreshDue) { queued.Proof.Step(); break; }
        }
    }

    private void PrepareJourneyTail(BroadcastSettings settings, ScenicShotPlan seed, double currentRemaining, bool moveCarrier, bool independent)
    {
        if (journeyPlanning || journeyQueue.Count >= JourneyRunwayPolicy.MaximumBufferedSections ||
            Time.unscaledTime < journeyRetryAt || !Player.m_localPlayer) return;
        bool first = journeyQueue.Count == 0;
        ScenicShotPlan predecessor = first ? seed : journeyQueue[journeyQueue.Count - 1].Plan;
        AutonomyPosition seam = predecessor.Sample(independent && first ? 0 : 1).CameraPosition;
        double committed = currentRemaining;
        foreach (JourneySection queued in journeyQueue) committed += queued.Plan.TravelMetres;
        double loadedRoute = JourneyRunwayPolicy.LocalReachMetres - JourneyRunwayPolicy.GazeReach(journey.CruiseSpeed);
        if (!journeyRunway.NeedsPreparation(JourneyRunwayMetres(), journey.CruiseSpeed, loadedRoute)) return;
        double inheritedGaze = independent && first ? 0 : seam.Distance(predecessor.Sample(1).AimPosition);
        double available = JourneyRunwayPolicy.WindowMetres(journey.CruiseSpeed,
            Vector3.Distance(Player.m_localPlayer.transform.position, FromCore(seam)), ScenicJourneyPolicy.WindowMetres,
            inheritedGazeMetres: inheritedGaze);
        double metres = Math.Min(journey.NextWindowMetres(committed) * journeyWindowScale, available);
        if (metres < ScenicJourneyPolicy.MinimumWindowMetres) return;
        ScenicFeature? arrival = independent ? null : ObservedArrivalSubject(seam, metres + 8);
        Camera? camera = Camera.main;
        journeyPlanner.Begin(predecessor, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
            camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier, false,
            journey.SteeringGoal(seam), arrival?.Center, departureCut: independent && first,
            arrivalCenter: arrival == null ? (AutonomyPosition?)null : journey.Itinerary!.Destination.Position,
            arrivalRadius: journey.Itinerary!.ArrivalRadius, nativeSplineOverride: settings.NativeSplineDolly.Value,
            planningCarrier: new AutonomyPosition(Player.m_localPlayer.transform.position.x, Player.m_localPlayer.transform.position.y, Player.m_localPlayer.transform.position.z));
        journeyPlanning = true; journeyPreparationStarted = Time.unscaledTime;
    }

    private bool AdmitJourneySection(JourneySection section)
    {
        if (!section.Proof.CanAdmit || !journeyTravel.TryAcceptRoute(
            new JourneyRouteEvidence(section.Plan.Id, section.Epoch, section.Plan.TravelMetres), Time.unscaledTime, out _)) return false;
        journeySection?.Dispose(); journeySection = section; journeyPlayback.Accept(section.Plan, Time.unscaledTimeAsDouble);
        journeyLiveFailure = ScenicRejectionKind.None; journeyViewBlockedSeconds = 0;
        Camera? camera = Camera.main;
        journeyEndingLease.Begin(section.Ending, section.Epoch, section.Proof.EndingValidatedAt,
            camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, true);
        proposed = 0; forceReplacement = false; usingIndependentPicture = false;
        journeyPredecessor = section.Plan.Id; journeyBraking = false; journeyRepairing = journeyRetainsPicture = false; journeyRepairMissingSeconds = 0;
        origin = FromCore(section.Plan.Sample(0).CameraPosition);
        journeyStatus = "Travelling to " + journey.Itinerary!.Destination.Name;
        return true;
    }

    private ScenicShotPlan? SnapshotTravelOrigin(ScenicShotPlan? source)
    {
        Camera? camera = Camera.main;
        if (!camera || !Player.m_localPlayer) return null;
        Vector3 position = camera.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        forward.Normalize();
        var body = new AutonomyPosition(position.x, position.y, position.z);
        Vector3 gaze = position + forward * 24;
        var aim = new AutonomyPosition(gaze.x, gaze.y, gaze.z);
        if (source?.IsDirected == true && source.Feature != null)
            return ScenicJourneyDeparture.Snapshot(source, body, aim, camera.fieldOfView);
        // This is an untrusted proposal origin, not an assertion that an
        // interesting subject or a readable camera already exists here.
        var feature = new ScenicFeature("journey-origin", ScenicFeatureKind.Landscape, aim,
            new AutonomyPosition(forward.x, 0, forward.z), 24, 0, 0);
        return ScenicShotPlan.Picture(++nextId, feature, ScenicShotRole.Traverse, body, aim,
            Mathf.Clamp(camera.fieldOfView, 30, 100), 0, -.04f);
    }

    private void BeginJourneyCoverageRecovery(string reason)
    {
        journeyTravel.AcknowledgeLocalReplan();
        if (journeyRepairing) return;
        RetireJourneyQueue(); journeyRepairSeed = SnapshotTravelOrigin(Plan);
        journeyRepairing = true; journeyRetainsPicture = journeyPlayback.Current != null; journeyRepairMissingSeconds = 0;
        journeyExitId = 0; journeyBraking = false; journeyEndingReason = null;
        // Preserve the live accepted route pose while repair prepares. Ordinary
        // fallback must start here, never at the pre-Journey region kilometres
        // behind the actual camera. This does not admit a new picture.
        Camera? currentCamera = Camera.main;
        if (currentCamera) origin = currentCamera.transform.position;
        CancelComparison("Rebase fallback at the interrupted Journey position");
        director.Reset(); pictures.Reset(); pictureAdmission.Reset(); usingIndependentPicture = false;
        testing = best = null; searching = planning = false; evaluator.Reset(); spatial.Reset();
        subjects.Clear(); subjectRetry.Clear(); retryAt = 0; proposed = (float)journeyPlayback.Progress;
        survey.Begin(origin); refreshingSurvey = false; refreshSurvey.Reset(); surveyRefreshAt = Time.unscaledTime + 15;
        journeyPreparationFailures = 0; journeyWindowScale = 1; journeyRetryAt = 0;
        journeyStatus = reason; journey.BreakPresentation();
        TraceJourney("local-replan-start");
    }

    private bool TickJourneyRecovery(BroadcastSettings settings, ScenicShotPlan? current, bool moveCarrier, out bool changed)
    {
        changed = false;
        if (!journeyTravel.Active || !journeyWatching || !Player.m_localPlayer) return JourneyOwnsPicture;
        if (journeyRetainsPicture)
        {
            journeyRepairMissingSeconds = HasUsablePicture ? 0 : journeyRepairMissingSeconds + Mathf.Min(.1f, Time.unscaledDeltaTime);
            if (journeyRepairMissingSeconds >= 2) journeyRetainsPicture = false;
        }
        journeyRepairSeed ??= SnapshotTravelOrigin(current);
        if (journeyRepairSeed == null) return JourneyOwnsPicture;
        if (!WithinDepartureHorizon(journeyRepairSeed, Player.m_localPlayer.transform.position))
        { RetireJourneyQueue(); journeyRepairSeed = SnapshotTravelOrigin(current); }
        if (journeyRepairSeed == null) return JourneyOwnsPicture;
        StepJourneyPreparation(moveCarrier); RefreshJourneyQueue(moveCarrier);
        if (journeyQueue.Count > 0 && journeyQueue[0].Proof.CanAdmit &&
            journeyRunway.TryStart(1, JourneyRunwayMetres(), journey.CruiseSpeed, out _) && AdmitJourneySection(journeyQueue[0]))
        {
            journeyQueue.RemoveAt(0); journeyRepairSeed = null; journey.BreakPresentation();
            TraceJourney("local-replan-resumed"); changed = true; return true;
        }
        // A moving fallback may develop away from the stopped travel pose.
        // Rebase between completed search batches, using actual presentation,
        // rather than cancelling useful work on every ordinary camera cut.
        if (!journeyRetainsPicture && !journeyPlanning && journeyQueue.Count == 0 &&
            HasUsablePicture && journeyExitCapture.HasValue && journeyExitCapturedPlan?.Id == Plan?.Id &&
            Time.unscaledTimeAsDouble >= journeyExitCapture.Value.Time &&
            Time.unscaledTimeAsDouble - journeyExitCapture.Value.Time <= .25 &&
            journeyRepairSeed.Sample(0).CameraPosition.Distance(journeyExitCapture.Value.Position) > 8)
        {
            journeyRepairSeed = SnapshotTravelOrigin(Plan);
            journeyRetryAt = 0; journeyWindowScale = 1;
            TraceJourney("local-replan-rebased");
        }
        if (journeyRepairSeed == null) return JourneyOwnsPicture;
        PrepareJourneyTail(settings, journeyRepairSeed, 0, moveCarrier, true);
        journeyStatus = "Replanning the same Journey: " + journeyPlanner.Reason;
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("local-replan-progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return JourneyOwnsPicture;
    }

    private void ReleaseJourneyRepairPicture(long admittedPicture)
    {
        if (!journeyRepairing || !journeyRetainsPicture) return;
        // Called only after ordinary admission has succeeded. A pending or
        // rejected candidate cannot remove the currently readable route pose.
        journeyRetainsPicture = false;
        journey.BreakPresentation();
        ScenicTrace.Event("journey-repair-picture", "Fresh local coverage replaces the stationary Journey view; travel mission retained", admittedPicture);
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
        if (held) { if (requestedJourney) journeyRequestExpiresAt += Mathf.Max(0, Time.unscaledDeltaTime); return false; }
        if (!journeyTravel.Active && requestedJourney && Time.unscaledTime >= journeyRequestExpiresAt)
        {
            string blocked = journeyStatus;
            FinishJourney("Journey departure expired; last wait: " + blocked, ScenicJourneyOutcome.Blocked);
            requestedJourney = false; return false;
        }
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (!journey.Active)
        {
            if (FixtureActive) return false;
            if (!requestedJourney && (Time.unscaledTime < nextJourneyAt || director.RememberedShots < 2)) return false;
            if (!allowActivate) return WaitForJourney("Journey waiting for Scenic programme ownership");
            if (held) return WaitForJourney("Journey waiting while the operator holds the picture");
            if (!journeyWatching || !journeyAuthorized || journeyAuthorityWorld == 0 || epoch <= 0)
                return WaitForJourney("Journey waiting for authorized loaded world access");
            if (Time.unscaledTime < nextJourneySelection) return WaitForJourney("Journey waiting for its bounded destination selection interval");
            if (!CatalogueCurrent()) return WaitForJourney("Journey waiting for a fresh known-destination catalogue");
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var selectionWork))
                return WaitForJourney("Journey destination selection waiting for shared camera work budget");
            ScenicJourneyItinerary? itinerary;
            ScenicShotPlan seed;
            using (selectionWork)
            {
                nextJourneySelection = Time.unscaledTime + 5;
                ScenicShotPlan? originSeed = SnapshotTravelOrigin(current);
                if (originSeed == null) return WaitForJourney("Journey waiting for a physical departure origin");
                seed = originSeed;
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
            journey.Start(++nextId, itinerary!, settings.ScenicJourneySpeed.Value);
            journeyRunway.Reset(); journeyPreparationFailures = 0; journeyWindowScale = 1; journeyRetryAt = 0;
            journeyPlayback.Reset(); journeyRepairing = journeyRetainsPicture = false; journeyRepairMissingSeconds = 0; journeyRepairSeed = null;
            journeyTravel.Begin(journey.Id, journeyAuthorityWorld, epoch, Time.unscaledTime,
                requestedJourney ? Math.Max(.01, journeyRequestExpiresAt - Time.unscaledTime) : 90, journey.MaximumSeconds);
            UpdateJourneyAuthority(journeyAuthorityWorld, journeyAuthorized, journeyWatching, held, false);
            journeyLastPreparationFailure = "none";
            journeyDestinationReportedAt = Time.realtimeSinceStartupAsDouble;
            journeyDestinationAge = itinerary!.Site?.Age ?? 0;
            journeyDestinationRetryUntil = 0;
            journeyDestinationSubject.Set(itinerary!.Site); journeySurveyOrigin = origin;
            journeyArrivalSurvey.Reset(); journeyArrivalSurveyStarted = false; nextJourneyArrivalSurvey = 0;
            manualJourney = requestedJourney; requestedJourney = false; journeyPredecessor = journeyDepartureSeed.Id;
            ResetJourneyPreparation(); journeyEndingReason = null; journeyExitId = 0;
            journeyBraking = journeyEndingArrival = journeyExitExhausted = false;
            journeyEpoch = epoch; RetireJourneyQueue(); journeySection?.Dispose(); journeySection = null;
            journeyStatus = "Preparing independent departure, route and ending for " + itinerary!.Description;
            nextJourneyTrace = 0; TraceJourney("departure-start");
        }
        if (!journeyTravel.Active || !journeyWatching || !journeyAuthorized || !Player.m_localPlayer) return false;
        // An ordinary camera cut supplies no evidence against a separately
        // prepared route. Retain its identity, destination and completed work.
        if (epoch != journeyEpoch)
        {
            RetireJourneyQueue(); journeyEpoch = epoch;
            journeyDepartureSeed = SnapshotTravelOrigin(current);
        }
        if (journeyDepartureSeed == null || !WithinDepartureHorizon(journeyDepartureSeed, Player.m_localPlayer.transform.position))
        { RetireJourneyQueue(); journeyDepartureSeed = SnapshotTravelOrigin(current); }
        if (journeyDepartureSeed == null) return false;
        StepJourneyPreparation(moveCarrier); RefreshJourneyQueue(moveCarrier);
        if (journeyQueue.Count > 0 && journeyQueue[0].Proof.CanAdmit &&
            journeyRunway.TryStart(1, JourneyRunwayMetres(), journey.CruiseSpeed, out string reserveReason))
        {
            JourneySection first = journeyQueue[0];
            if (AdmitJourneySection(first))
            {
                journeyQueue.RemoveAt(0); journeyOwned = true; journeyRequestExpiresAt = 0;
                destinationSubject.Reset(); destinationSurvey.Reset(); destinationFeatures.Clear();
                journeyDepartureSeed = null;
                TraceJourney("start"); changed = true; return true;
            }
        }
        PrepareJourneyTail(settings, journeyDepartureSeed, 0, moveCarrier, true);
        journeyStatus = "Preparing physical departure to " + journey.Itinerary!.Destination.Name + ": " + journeyPlanner.Reason;
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("departure-progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return false;
    }

    private void FinishJourney(string reason, ScenicJourneyOutcome outcome = ScenicJourneyOutcome.Interrupted)
    {
        bool wasOwned = journeyOwned; bool wasRequested = requestedJourney || manualJourney || journeyTravel.Active;
        if (wasOwned) PreserveJourneyExitPicture();
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
        journeyPlanner.ForgetSearchEpisode();
        journeyStatus = reason;
        if (wasOwned || wasRequested) TraceJourney("finish");
        journeyTravel.Cancel(reason); journeyRequestExpiresAt = 0;
        if (Plan?.IsJourney == true || Plan?.Id == journeyExitId) director.RequestReplacement(reason);
        journeySection?.Dispose(); journeySection = null; journeyDepartureSeed = null; journeyExitId = 0; journeyBraking = false;
        journeyEndingLease.Reset();
        journeyPlayback.Reset(); journeyRepairing = journeyRetainsPicture = false; journeyRepairMissingSeconds = 0; journeyRepairSeed = null;
    }

    private void TraceJourney(string phase)
    {
        if (!ScenicTrace.Enabled) return;
        double ahead = Plan?.IsJourney == true ? Plan.TravelMetres * (1 - Plan.MotionFraction(journeyPlayback.Progress)) : 0;
        foreach (JourneySection section in journeyQueue) ahead += section.Plan.TravelMetres;
        ScenicTrace.Record(ScenicTrace.Row("journey").Field("phase", phase).Field("journey", journey.Id)
            .Field("owned", journeyOwned).Field("departureState", journeyTravel.State.ToString()).Field("missionCause", journeyTravel.Cause.ToString()).Field("ownsTravel", JourneyOwnsTravel).Field("plan", Plan?.Id ?? 0)
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
            .Field("localRepair", journeyRepairing).Field("missionTravelSeconds", journeyTravel.TravelElapsedSeconds)
            .Field("missionDepartureSeconds", journeyTravel.DepartureElapsedSeconds)
            .Field("noProgressSeconds", journeyTravel.NoProgressSeconds).Field("replanRequested", journeyTravel.LocalReplanRequested)
            .Field("openingCandidates", journeyPlanner.OpeningCandidates).Field("rejectedOpenings", journeyPlanner.RejectedOpenings)
            .Field("loading", journeyPlanner.WaitingForLoading).Field("planner", journeyPlanner.Reason));
    }
}
