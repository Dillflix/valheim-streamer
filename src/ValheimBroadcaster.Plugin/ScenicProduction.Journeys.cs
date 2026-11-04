using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    private sealed class JourneySection
    {
        internal readonly ScenicShotPlan Plan, Ending;
        internal readonly long Epoch;
        internal readonly float PreparedAt;
        internal readonly bool ArrivalFramed;
        internal JourneySection(ScenicShotPlan plan, ScenicShotPlan ending, long epoch, float at, bool arrival)
        { Plan = plan; Ending = ending; Epoch = epoch; PreparedAt = at; ArrivalFramed = arrival; }
    }
    private readonly ScenicJourneyEndingPlanner journeyEndingPlanner = new ScenicJourneyEndingPlanner();
    private readonly ScenicJourneyEndingLease journeyEndingLease = new ScenicJourneyEndingLease();
    private readonly DestinationSubjectCoverage journeyDestinationSubject = new DestinationSubjectCoverage();
    private readonly ScenicFeatureSurvey journeyArrivalSurvey = new ScenicFeatureSurvey();
    private bool journeyArrivalSurveyStarted;
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
    }

    private void ClearJourneyProgramme()
    {
        journeyEndingPlanner.Dispose(); journeyEndingLease.Dispose(); journeyPendingSection = null; journeySection = null;
        journeyExitId = 0; journeyBraking = journeyPendingArrival = journeyEndingArrival = journeyExitExhausted = false;
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

    private bool CatalogueCurrent()
    {
        if (Time.unscaledTime - journeyCatalogueAt > 5 || journeyRegions.Count == 0) return false;
        if (!journey.Active || journey.Itinerary == null) return true;
        foreach (ScenicRegion region in journeyRegions)
            if (region.Id == journey.Itinerary.Destination.Id) return true;
        return false;
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
                journeyEndingReason = "Continuation unavailable: " + journeyPlanner.Reason;
                ResetJourneyPreparation(); return;
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
            journeyQueue.Add(new JourneySection(journeyPendingSection, journeyEndingPlanner.Ready,
                journeyEndingPlanner.Epoch, Math.Min(journeyPlanner.ValidatedAt, journeyEndingPlanner.ValidatedAt), journeyEndingPlanner.ArrivalFramed));
        else journeyEndingReason = "No validated ending for the next section: " + journeyEndingPlanner.Reason;
        ResetJourneyPreparation();
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
        if (current == null || usingIndependentPicture || current.Id != journeyExitId && current.JourneyId != journey.Id)
        { FinishJourney("Journey yielded to a different picture owner"); return false; }
        if (!allowActivate) return true;
        if (held)
        {
            // Held output pauses the route, not proof ageing. Discard speculative
            // work rather than resuming a timed-out search after an operator hold.
            ResetJourneyPreparation(); journeyQueue.Clear(); journeyEndingLease.Step(); return true;
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
            if (best != null && !WithinJourneyHorizon(best, carrier)) best = null;
            // Retain useful work across small carrier movements, but do not
            // spend the exit budget finishing a route now outside loaded reach.
            if (testing != null && !WithinJourneyHorizon(testing, carrier) ||
                planning && (carrier - journeyOrdinarySearchOrigin).sqrMagnitude > 72 * 72)
            {
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
        if (!CatalogueCurrent()) journeyEndingReason = "Journey destination observations expired or were withdrawn";
        if (journey.BudgetExpired || journeyRunSeconds > journey.MaximumSeconds + 30)
            journeyEndingReason = "Journey runtime expired before observed arrival";
        if (journeyEpoch != CameraSpatialScene.Shared.Epoch)
        {
            journeyQueue.Clear(); ResetJourneyPreparation(); journeyEpoch = CameraSpatialScene.Shared.Epoch;
            // Current live checks remain authoritative; an obsolete speculative
            // proof cannot supply a successor or postpone the ending decision.
        }
        if (Time.frameCount % 3 != 0 || !journeyEndingLease.CanAdmit) journeyEndingLease.Step();
        if (journeyQueue.Count > 0 && !ScenicJourneyEndingLease.IsCurrent(journeyQueue[0].Epoch,
            journeyQueue[0].PreparedAt, CameraSpatialScene.Shared.Epoch, Time.unscaledTime))
            journeyQueue.Clear();
        // Reserve every third background frame for local observations and the
        // ordinary exit planner; both otherwise share the same scenic allowance.
        if (!journeyBraking && journeyEndingReason == null && Time.frameCount % 3 != 0) StepJourneyPreparation(moveCarrier);
        double remaining = current.TravelMetres * (1 - current.MotionFraction(director.Progress));
        bool successorReady = journeyQueue.Count > 0 && journeyQueue[0].Epoch == CameraSpatialScene.Shared.Epoch;
        bool destinationNear = journeySection?.ArrivalFramed == true &&
            current.Sample(1).CameraPosition.HorizontalDistance(journey.Itinerary!.Destination.Position) <= journey.Itinerary.ArrivalRadius;
        if (!journeyBraking && JourneyHandoffPolicy.ShouldBrake(journeyBraking, remaining, journey.CruiseSpeed,
            successorReady && journeyEndingReason == null, destinationNear))
        {
            journeyBraking = true;
            if (!destinationNear && journeyEndingReason == null)
                journeyEndingReason = "Continuation was not ready before the planned braking point";
            journeyQueue.Clear(); ResetJourneyPreparation();
            ScenicTrace.Event("journey-ending-committed", journeyEndingReason ?? "Approaching the selected destination", current.Id);
        }
        if (director.Progress >= .999999)
        {
            if (!journeyBraking && successorReady)
            {
                JourneySection next = journeyQueue[0]; journeyQueue.RemoveAt(0);
                if (next.Plan.ContinuationOf != current.Id)
                { FinishJourney("Prepared continuation lost its predecessor", ScenicJourneyOutcome.Blocked); return false; }
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
        if (!journeyBraking && !journeyPlanning && journeyEndingReason == null && journeyQueue.Count < 2)
        {
            ScenicShotPlan predecessor = journeyQueue.Count > 0 ? journeyQueue[journeyQueue.Count - 1].Plan : current;
            double committed = remaining;
            foreach (JourneySection queued in journeyQueue) committed += queued.Plan.TravelMetres;
            AutonomyPosition seam = predecessor.Sample(1).CameraPosition;
            double metres = Math.Min(journey.NextWindowMetres(committed),
                Math.Max(ScenicJourneyPolicy.MinimumWindowMetres,
                    journey.Itinerary!.RouteMetres - journey.Itinerary.ProjectProgress(seam)));
            if (metres >= ScenicJourneyPolicy.MinimumWindowMetres && Player.m_localPlayer &&
                Vector3.Distance(Player.m_localPlayer.transform.position, FromCore(seam)) + metres <= 85)
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
                journeyPlanning = true;
            }
        }
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return true;
    }

    private void AdmitJourneySection(JourneySection section)
    {
        journeySection = section; director.Accept(section.Plan, Time.unscaledTime);
        Camera? camera = Camera.main;
        journeyEndingLease.Begin(section.Ending, section.Epoch, section.PreparedAt,
            camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, true);
        proposed = 0; forceReplacement = false; usingIndependentPicture = false;
        journeyPredecessor = section.Plan.Id; journeyBraking = false;
        journeyStatus = "Travelling to " + journey.Itinerary!.Destination.Name + "; validated ending ready";
    }

    private static bool WithinJourneyHorizon(ScenicShotPlan plan, Vector3 carrier) =>
        (FromCore(plan.Sample(0).CameraPosition) - carrier).sqrMagnitude <= 75 * 75 &&
        (FromCore(plan.Sample(1).CameraPosition) - carrier).sqrMagnitude <= 75 * 75;

    private bool TickDeparture(BroadcastSettings settings, ScenicShotPlan? current, bool moveCarrier,
        bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        if ((requestedJourney || manualJourney) && held) journeyRequestExpiresAt += Mathf.Max(0, Time.unscaledDeltaTime);
        if ((requestedJourney || manualJourney) && Time.unscaledTime >= journeyRequestExpiresAt)
        { AbandonDeparture("Journey not started: no complete departure programme within 90 seconds"); requestedJourney = false; return false; }
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (!journey.Active)
        {
            if (!allowActivate || held || !HasUsablePicture || usingIndependentPicture || current == null || director.NeedsReplacement ||
                !requestedJourney && (Time.unscaledTime < nextJourneyAt || director.RememberedShots < 2)) return false;
            if (!ScenicJourneyPolicy.Eligible(current) || !departure.CanAttempt(current.Id, epoch) ||
                Time.unscaledTime < nextJourneySelection || settings.ShotMotion.Value <= 0 || !CatalogueCurrent()) return false;
            double seedSeconds = current.DurationSeconds * (1 - director.Progress) / settings.ShotMotion.Value;
            if (seedSeconds > ScenicJourneyDeparture.PreparationSeconds - 2 || director.Prepared == null) return false;
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var selectionWork)) return false;
            ScenicJourneyItinerary? itinerary;
            using (selectionWork)
            {
                nextJourneySelection = Time.unscaledTime + 5;
                if (!ScenicJourneyItinerary.TrySelect(current.Sample(1).CameraPosition, ScenicJourneyPlanner.ObjectiveHeading(current),
                    journeyRegions, journeySites, settings.ScenicJourneyDistance.Value, Time.realtimeSinceStartupAsDouble,
                    recentJourneyDestinations, out itinerary, out string reason))
                { journeyStatus = reason; return false; }
            }
            departure.Begin(current.Id, epoch); journey.Start(++nextId, itinerary!, settings.ScenicJourneySpeed.Value);
            journeyDestinationSubject.Set(itinerary!.Site); journeySurveyOrigin = origin;
            journeyArrivalSurvey.Reset(); journeyArrivalSurveyStarted = false; nextJourneyArrivalSurvey = 0;
            manualJourney = requestedJourney; requestedJourney = false; journeyPredecessor = current.Id;
            ResetJourneyPreparation(); journeyEndingReason = null; journeyRunSeconds = 0; journeyExitId = 0;
            journeyBraking = journeyEndingArrival = journeyExitExhausted = false;
            journeyEpoch = epoch; journeyQueue.Clear(); journeySection = null;
            journeyStatus = "Preparing departure, route and ending for " + itinerary!.Description;
            nextJourneyTrace = 0; TraceJourney("departure-start");
        }
        bool valid = current != null && !usingIndependentPicture && HasUsablePicture && current.Id == journeyPredecessor &&
            epoch == journeyEpoch && CatalogueCurrent() && (!director.NeedsReplacement || director.Progress >= .999999);
        if (!valid || !allowActivate)
        { AbandonDeparture("Journey departure changed before admission; ordinary coverage retained"); return false; }
        if (held) { ResetJourneyPreparation(); journeyQueue.Clear(); return false; }
        StepJourneyPreparation(moveCarrier);
        if (journeyEndingReason != null) { AbandonDeparture(journeyEndingReason); return false; }
        bool ready = journeyQueue.Count > 0 && ScenicJourneyEndingLease.IsCurrent(journeyQueue[0].Epoch,
            journeyQueue[0].PreparedAt, epoch, Time.unscaledTime);
        ScenicJourneyDepartureState state = departure.Observe(current!.Id, epoch, director.Progress >= .999999,
            ready, false, true, Mathf.Max(0, Time.unscaledDeltaTime));
        if (state == ScenicJourneyDepartureState.Abandoned)
        { AbandonDeparture(departure.Reason); return false; }
        if (state == ScenicJourneyDepartureState.Admitted)
        {
            JourneySection first = journeyQueue[0]; journeyQueue.RemoveAt(0);
            if (first.Plan.ContinuationOf != 0 || first.Plan.JourneyId != journey.Id)
            { AbandonDeparture("Departure was not an independently framed validated cut"); return false; }
            AdmitJourneySection(first); journeyOwned = true; journeyRunSeconds = 0; journeyRequestExpiresAt = 0;
            // Journey owns its destination separately. Stop searching for the
            // source site's subject as the carrier leaves that region, without
            // discarding an already prepared local exit picture.
            destinationSubject.Reset(); destinationSurvey.Reset(); destinationFeatures.Clear(); nextDestinationSurvey = 0;
            TraceJourney("start"); changed = true; return true;
        }
        if (!journeyPlanning && journeyQueue.Count == 0)
        {
            double metres = journey.NextWindowMetres(); AutonomyPosition seam = current.Sample(1).CameraPosition;
            if (Player.m_localPlayer && Vector3.Distance(Player.m_localPlayer.transform.position, FromCore(seam)) + metres <= 85)
            {
                Camera? camera = Camera.main;
                journeyPlanner.Begin(current, ++nextId, journey.Id, (float)metres, (float)journey.CruiseSpeed,
                    camera ? camera.aspect : 16f / 9f, camera ? camera.nearClipPlane : .3f, moveCarrier,
                    false, journey.SteeringGoal(seam), null, departureCut: true);
                journeyPlanning = true;
            }
        }
        if (Time.unscaledTime >= nextJourneyTrace) { TraceJourney("departure-progress"); nextJourneyTrace = Time.unscaledTime + 1; }
        return false;
    }

    private void AbandonDeparture(string reason)
    {
        bool retry = manualJourney && Time.unscaledTime < journeyRequestExpiresAt;
        departure.Abandon(reason);
        if (journey.Active) journey.Finish(ScenicJourneyOutcome.Blocked, reason);
        journeyStatus = reason; TraceJourney("departure-abandoned");
        ResetJourneyPreparation(); journeyQueue.Clear(); journeyOwned = manualJourney = false;
        journeyPredecessor = 0; journeyEndingReason = null; journeySection = null;
        requestedJourney = retry; nextJourneyAt = Time.unscaledTime + 8;
    }

    private void FinishJourney(string reason, ScenicJourneyOutcome outcome = ScenicJourneyOutcome.Interrupted)
    {
        bool wasOwned = journeyOwned;
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
        ResetJourneyPreparation(); journeyQueue.Clear(); journeyPredecessor = 0; nextJourneyAt = Time.unscaledTime + 180;
        journeyStatus = reason;
        if (wasOwned) TraceJourney("finish");
        departure.Reset(); journeyRequestExpiresAt = 0;
        if (Plan?.IsJourney == true || Plan?.Id == journeyExitId) director.RequestReplacement(reason);
        journeySection = null; journeyExitId = 0; journeyBraking = false;
        journeyEndingLease.Reset();
    }

    private void TraceJourney(string phase)
    {
        if (!ScenicTrace.Enabled) return;
        double ahead = Plan?.IsJourney == true ? Plan.TravelMetres * (1 - Plan.MotionFraction(director.Progress)) : 0;
        foreach (JourneySection section in journeyQueue) ahead += section.Plan.TravelMetres;
        ScenicTrace.Record(ScenicTrace.Row("journey").Field("phase", phase).Field("journey", journey.Id)
            .Field("owned", journeyOwned).Field("departureState", departure.State.ToString()).Field("plan", Plan?.Id ?? 0)
            .Field("outcome", journey.Outcome.ToString()).Field("reason", journeyStatus)
            .Field("destination", journey.Itinerary?.Destination.Id ?? "none").Field("destinationName", journey.Itinerary?.Destination.Name ?? "none")
            .Field("knownWaypoints", journey.Itinerary?.Waypoints.Count ?? 0).Field("routeProgress", journey.RouteProgressMetres)
            .Field("targetMetres", journey.TargetMetres).Field("measuredMetres", journey.TravelMetres)
            .Field("displacementMetres", journey.DisplacementMetres).Field("advanceMetres", journey.ObjectiveAdvanceMetres)
            .Field("cruise", journey.CruiseSpeed).Field("achievedSpeed", journey.AchievedMetresPerSecond)
            .Field("shownSeconds", journey.ShownSeconds).Field("arrivalShownSeconds", journey.ArrivalShownSeconds)
            .Field("readyMetresAhead", ahead).Field("queued", journeyQueue.Count)
            .Field("endingPrepared", journeySection != null).Field("braking", journeyBraking).Field("ending", journeyExitId)
            .Field("loading", journeyPlanner.WaitingForLoading).Field("planner", journeyPlanner.Reason));
    }
}
