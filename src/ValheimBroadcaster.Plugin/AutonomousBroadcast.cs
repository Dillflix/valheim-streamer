using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Coordinates policy requests with the already-tested protected native travel path.
// No server positions are used as evidence that a character is actually renderable.
internal sealed class AutonomousBroadcast
{
    private readonly BroadcastSettings settings;
    private readonly RemoteObserverClient remote;
    private readonly ObserverController observer;
    private readonly PlayerShotReadiness playerShots;
    private readonly Func<TargetRoster.Target, Vector3, double> requiredPlayerCameraMovement;
    private readonly Action<AutonomyTravelRequest?> resetShot;
    private readonly Action<string> log;
    private readonly AutonomousDirector policy = new AutonomousDirector();
    private readonly List<AutonomyPlayer> players = new List<AutonomyPlayer>(64);
    private readonly List<PlayerActivityEvidence> activity = new List<PlayerActivityEvidence>(128);
    private readonly List<ScenicRegion> regions = new List<ScenicRegion>(1024);
    private readonly List<ScenicRegion> generatedRegions = new List<ScenicRegion>(1024);
    private readonly Dictionary<string, WorldInterestSite> interestSites = new Dictionary<string, WorldInterestSite>(StringComparer.Ordinal);
    private readonly DestinationFeedbackLedger destinationHistory = new DestinationFeedbackLedger();
    private readonly DestinationVisit destinationVisit = new DestinationVisit();
    private long interestTravelToken;
    private string? interestTravelId;
    private WorldInterestSite? interestTravelSite, currentInterest;
    internal WorldInterestSite? ScenicInterest => ControlsSelection && currentInterest != null && (currentInterest.Kind != WorldInterestKind.Natural || currentInterest.Landscape == ScenicLandscape.Coast) ? currentInterest : null;
    private double nextDestinations;
    private long observedJourneyEnd;
    private bool journeyCataloguePublished, lastDiscoveryFresh, journeyVisitChanged;
    private readonly ScenicAppearanceSampler appearance = new ScenicAppearanceSampler();
    private string destinationFeedback = "none";
    private readonly List<ScenicRegion> route = new List<ScenicRegion>(32);
    private long sequence = -1, worldId;
    private bool? requested;
    private bool paused, recovering;
    private int recoveries;
    private double retryRecoveryAt, stableSince = -1;
    private string recoveryReason = "";
    private string? reconnectReason;
    private string targetDiagnostics = "none", lastTravelReason = "none";
    internal bool PreservePlayerShot(string? subjectId) => ControlsSelection && policy.PresentationGraceActive && policy.TargetId == subjectId;
    internal bool WantsEnabled => requested ?? settings.Automation.Value;
    internal bool ControlsSelection => WantsEnabled && !paused && observer.RemoteSession && remote.Approved;
    internal bool Recovering => recovering;
    internal string? TargetId => ControlsSelection ? policy.TargetId : null;
    internal bool Holding => policy.Held;
    internal bool RouteActive => policy.RouteTestActive;
    internal AutonomyTravelRequest? ActiveTravel => policy.ActiveTravel;
    internal WorldInterestSite? ActiveTravelInterest => policy.ActiveTravel?.Token == interestTravelToken ? interestTravelSite : null;
    internal bool WildlifeCutawayActive => policy.WildlifeCutawayActive;
    internal bool CanStartWildlifeCutaway => ControlsSelection && observer.Active &&
        observer.Phase == ObserverPhase.Watching && !recovering && policy.CanStartWildlifeCutaway;
    internal string Status => paused ? "Automatic direction paused for manual control" : !WantsEnabled ? "Automatic direction off" :
        recovering ? "Recovering travel: " + recoveryReason : policy.Reason;
    internal string Diagnostics => "Director: " + Status + " | state " + policy.State +
        "\nTarget: " + (policy.TargetName ?? "none") + "; ready dwell " + policy.ReadyDwellSeconds.ToString("F1") + "s; retry in " + policy.RetryAfterSeconds(Time.realtimeSinceStartupAsDouble).ToString("F0") + "s" +
        "\nActivity direction: " + policy.ActivityReason + "; " + remote.EventStatus +
        "\nTarget readiness: " + targetDiagnostics + "; retaining shot " + policy.PresentationGraceActive +
        "\nLast travel decision: " + lastTravelReason +
        "\nRegion: " + (policy.CurrentRegionName ?? "initial area") + " / " + (policy.CurrentRegionId ?? "unknown") +
        "; known " + regions.Count + "; recent " + policy.VisitedRegionCount + "; completed travel " + policy.CompletedTravels + "; failed " + policy.FailedTravels +
        "\nLast travel failure: " + (policy.LastFailure ?? "none") + "; protected returns " + recoveries + "/3" +
        "\nDestination variety: " + policy.ScenicSelectionReason +
        "; finishing scenic shot " + policy.ScenicDepartureWaiting +
        "\nDestination feedback: " + destinationFeedback + "; visit footage " + destinationVisit.FootageSeconds.ToString("F1") + "s" +
        "\nSolo route: " + policy.RouteTestResult + "; marked " + route.Count;

    internal AutonomousBroadcast(BroadcastSettings settings, RemoteObserverClient remote, ObserverController observer, PlayerShotReadiness playerShots,
        Func<TargetRoster.Target, Vector3, double> requiredPlayerCameraMovement, Action<AutonomyTravelRequest?> resetShot, Action<string> log)
    {
        this.settings = settings; this.remote = remote; this.observer = observer; this.playerShots = playerShots;
        this.requiredPlayerCameraMovement = requiredPlayerCameraMovement; this.resetShot = resetShot; this.log = log;
    }

    internal void Started()
    {
        paused = recovering = false; recoveries = 0; retryRecoveryAt = 0; stableSince = -1; reconnectReason = null;
        policy.SetEnabled(WantsEnabled); policy.SetPaused(false);
    }
    internal void EndSession()
    {
        targetDiagnostics = lastTravelReason = "none";
        ResetDestinations();
        policy.Reset(); players.Clear(); regions.Clear(); route.Clear(); sequence = -1; worldId = 0;
        paused = recovering = false; recoveries = 0; stableSince = -1; reconnectReason = null;
    }
    internal void Enable(bool enabled)
    {
        destinationVisit.Reset(); interestTravelId = null; currentInterest = interestTravelSite = null;
        requested = enabled; paused = false; recovering = false; reconnectReason = null;
        policy.SetEnabled(enabled); policy.SetPaused(false);
    }
    internal void PauseForManual()
    {
        destinationVisit.Reset(); interestTravelId = null; currentInterest = interestTravelSite = null;
        paused = true; recovering = false; reconnectReason = null;
        policy.SetPaused(true);
    }
    internal void SetHolding(bool held) => policy.SetHolding(held);
    internal void RecordCoverage(double now, double dt, IReadOnlyList<string> visibleIds) => policy.RecordCoverage(now, dt, visibleIds);
    internal void Next(int direction) => policy.Next(direction);
    internal void Select(string id) { policy.Select(id); policy.SetHolding(true); }
    internal string? TakeReconnectReason() { string? value = reconnectReason; reconnectReason = null; return value; }

    internal bool CanFrame(TargetRoster.Target? target)
    {
        Player? player = target?.LoadedPlayer;
        if (!player || !Player.m_localPlayer || !playerShots.TargetReady(target, out _)) return false;
        double movement = requiredPlayerCameraMovement(target!, player.transform.position);
        return movement >= 0 && movement < CameraReachability.ImmediateLimit;
    }

    // Refresh before ordinary direction runs: an ending Journey must establish
    // its new local visit before old source-area dwell can request relocation.
    // This also feeds explicit Journey requests when automatic selection is off.
    internal void SynchronizeScenicJourney(IdleShots idle)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        ObserverDiscoverySnapshot? snapshot = remote.Discovery;
        bool discoveryFresh = remote.DiscoveryFresh;
        if (snapshot != null && worldId != snapshot.WorldId)
        {
            if (worldId != 0) { policy.Reset(); route.Clear(); policy.SetEnabled(WantsEnabled); }
            ResetDestinations(); worldId = snapshot.WorldId; sequence = -1;
            observedJourneyEnd = idle.JourneyEndRevision;
        }
        if (snapshot != null && snapshot.Sequence != sequence)
        {
            sequence = snapshot.Sequence; generatedRegions.Clear(); nextDestinations = 0;
            foreach (ObserverRegion region in snapshot.Regions)
                generatedRegions.Add(new ScenicRegion(region.Id, string.IsNullOrWhiteSpace(region.Name) ? region.Id : region.Name, Convert(region.Position)));
        }
        if (!journeyCataloguePublished || now >= nextDestinations || discoveryFresh != lastDiscoveryFresh)
        {
            nextDestinations = now + 1; interestSites.Clear();
            IReadOnlyList<WorldInterestSite> sites = remote.WorldInterests;
            foreach (WorldInterestSite site in sites) interestSites[site.Id] = site;
            regions.Clear();
            regions.AddRange(WorldDestinationPlanner.Merge(discoveryFresh ? generatedRegions : Array.Empty<ScenicRegion>(), sites, destinationHistory, now));
            idle.SetJourneyCatalogue(worldId, regions, interestSites,
                observer.RemoteSession && remote.Approved && (discoveryFresh || sites.Count > 0));
            journeyCataloguePublished = true; lastDiscoveryFresh = discoveryFresh;
        }
        if (idle.JourneyActive)
        { destinationVisit.Reset(); currentInterest = null; }
        if (observedJourneyEnd == idle.JourneyEndRevision) return;
        observedJourneyEnd = idle.JourneyEndRevision;
        if (!observer.RemoteSession || !remote.Approved || observer.Phase != ObserverPhase.Watching || !Player.m_localPlayer) return;
        AutonomyPosition position = Convert(Player.m_localPlayer.transform.position);
        ScenicRegion? arrived = idle.JourneyArrivedDestination;
        WorldInterestSite? arrivedSite = idle.JourneyArrivedSite;
        // A superseding relocation cannot inherit an earlier ending's identity.
        if (arrived != null && position.HorizontalDistance(arrived.Position) > 96)
        { arrived = null; arrivedSite = null; }
        bool destinationPresented = arrived != null;
        if (arrived == null)
        {
            double nearest = 64;
            foreach (ScenicRegion region in regions)
            {
                double distance = position.HorizontalDistance(region.Position);
                if (distance < nearest) { arrived = region; nearest = distance; }
            }
        }
        if (!policy.ScenicJourneyEnded(arrived, now, destinationPresented)) return;
        destinationVisit.Reset(); interestTravelId = null; interestTravelSite = null; journeyVisitChanged = true;
        // Partial journeys do not invent arrival at the intended destination.
        // They also must not keep imposing the now distant source site's subject.
        currentInterest = arrivedSite;
        if (currentInterest != null)
        {
            if (interestSites.TryGetValue(currentInterest.Id, out WorldInterestSite current)) currentInterest = current;
            destinationVisit.Begin(currentInterest.Id, currentInterest.Kind != WorldInterestKind.Natural || currentInterest.Landscape == ScenicLandscape.Coast);
        }
        log("Journey ended; local scenic coverage rebased to " + (arrived?.Name ?? "the observed ending area") + ".");
    }

    internal void Tick(float dt, TargetRoster roster, string? presentedId, bool idleReady, bool wildlifeCutaway = false, IReadOnlyList<PlayerActivityEvidence>? localActivities = null, bool usablePlayerFootage = true,
        bool? idleSceneAvailable = null, bool idleSearchExpired = false, bool scenicPicture = false, bool creaturePicture = false, bool destinationSubjectShown = false, bool destinationSubjectUnavailable = false, bool destinationSubjectSatisfied = false,
        bool scenicContentExhausted = false, bool relatedPlayerView = false, bool scenicJourneyActive = false, bool scenicPreparationActive = false,
        bool scenicEditBoundary = true)
    {
        if (!observer.RemoteSession || !remote.Approved || !observer.Active || !Player.m_localPlayer) return;
        policy.SetEnabled(WantsEnabled);
        if (!WantsEnabled || paused) return;
        double now = Time.realtimeSinceStartupAsDouble;
        policy.SetScenicAppearance(appearance.Sample(now));
        bool justArrived = journeyVisitChanged; journeyVisitChanged = false;
        policy.DwellSeconds = settings.Dwell.Value;
        policy.AcquisitionSeconds = settings.AcquireSeconds.Value;
        policy.RegionSeconds = settings.RegionSeconds.Value;
        policy.RevisitSeconds = settings.RegionRevisitSeconds.Value;
        policy.RetrySeconds = settings.TargetRetrySeconds.Value;
        policy.MinimumScenicTravelDistance = settings.MinimumRegionDistance.Value;
        policy.MaximumScenicTravelDistance = Math.Max(settings.MinimumRegionDistance.Value, settings.MaximumRegionDistance.Value);

        if (policy.ActiveTravel != null)
        {
            if (observer.Phase == ObserverPhase.Watching)
            {
                AutonomyTravelRequest completed = policy.ActiveTravel;
                if (interestTravelId != null && interestTravelToken == policy.ActiveTravel.Token)
                {
                    currentInterest = interestTravelSite;
                    destinationVisit.Begin(interestTravelId, currentInterest != null && (currentInterest.Kind != WorldInterestKind.Natural || currentInterest.Landscape == ScenicLandscape.Coast));
                    interestTravelId = null; interestTravelSite = null; justArrived = true;
                }
                policy.TravelCompleted(completed.Token, true, now);
                resetShot(completed);
            }
            else if (observer.Phase == ObserverPhase.Suspended)
            {
                if (interestTravelId != null && interestTravelToken == policy.ActiveTravel.Token)
                    ReportDestination(interestTravelId, DestinationOutcome.TravelFailed, 0, now);
                interestTravelId = null;
                policy.TravelCompleted(policy.ActiveTravel.Token, false, now, observer.Status);
            }
        }
        if (recovering && observer.Phase == ObserverPhase.Parked)
        {
            observer.Resume(); recovering = false; resetShot(null);
            log("Automatic recovery returned to the starting region; resuming coverage with failed destinations on cooldown.");
        }
        if (observer.Phase == ObserverPhase.Suspended)
        {
            stableSince = -1;
            if (now < retryRecoveryAt) return;
            recoveryReason = observer.Status;
            if (recoveries >= 3)
            {
                if (retryRecoveryAt < double.PositiveInfinity)
                {
                    reconnectReason = "Controlled travel recovery exhausted three protected return attempts";
                    log(reconnectReason + ". Remaining protected until reconnect or manual return.");
                    retryRecoveryAt = double.PositiveInfinity;
                }
                return;
            }
            recoveries++; recovering = true; retryRecoveryAt = now + 30;
            observer.RequestReturn();
            log("Automatic travel recovery: protected return " + recoveries + "/3.");
            return;
        }
        if (recovering || observer.Phase == ObserverPhase.Returning || observer.Phase == ObserverPhase.Parked) return;
        if (observer.Phase == ObserverPhase.Watching && (idleReady || presentedId != null))
        {
            if (stableSince < 0) stableSince = now;
            if (now - stableSince >= 60) recoveries = 0;
        }
        else stableSince = -1;

        players.Clear(); activity.Clear();
        foreach (TargetRoster.Target target in roster.Targets.Values)
        {
            Player? actor = target.LoadedPlayer;
            bool dead = actor && actor.IsDead();
            // An alive local actor is the freshest coordinate source even when its
            // area is temporarily unready. Do not fall back to an older server point.
            bool live = actor && !dead && TargetRoster.Finite(actor.transform.position);
            Vector3? position = live ? actor!.transform.position :
                !dead && remote.DiscoveryFresh ? target.PublicPosition : null;
            double? movement = null;
            if (position.HasValue)
            {
                // Share the camera adapter's candidate geometry with CanFrame.
                // Foliage quality must never increase travel distance or turn a
                // finite nearby position into a distant-acquisition request.
                double required = requiredPlayerCameraMovement(target, position.Value);
                if (required >= 0 && !double.IsNaN(required) && !double.IsInfinity(required)) movement = required;
                else position = null; // Invalid geometry cannot authorize travel.
            }
            bool areaReady = playerShots.TargetReady(target, out string readinessReason);
            bool reachable = movement.HasValue && movement.Value < CameraReachability.ImmediateLimit;
            string reason = dead ? "Character dead; waiting for respawn" : !position.HasValue ? "No fresh coordinates" :
                !reachable ? "Outside immediate camera reach" : !live ? "Character not loaded locally" :
                readinessReason;
            players.Add(new AutonomyPlayer(target.Identity.Id, string.IsNullOrWhiteSpace(target.Identity.Name) ? "Player " + target.Identity.Id : target.Identity.Name,
                position.HasValue ? Convert(position.Value) : (AutonomyPosition?)null,
                position.HasValue ? now + (live ? ObserverDiscoveryProtocol.FreshSeconds : Math.Max(0, ObserverDiscoveryProtocol.FreshSeconds - remote.DiscoveryAge)) : now,
                live && areaReady && reachable, movement, reason));
            PlayerActivityEvidence? local = null;
            if (live && localActivities != null)
                foreach (PlayerActivityEvidence item in localActivities)
                    if (item.Id == target.Identity.Id && item.Fresh(now)) { local = item; break; }
            ObserverPlayerEvents? events = !dead ? remote.GetPlayerEvents(target.Identity.Id, target.CharacterId) : null;
            PlayerActivityEvidence? observed = events == null ? null : PlayerActivitySelection.FromEvents(events, now);
            // A locally identified scene is useful even if this companion's own
            // action is unknown. Preserve fresh server activity in that case.
            PlayerActivityEvidence? merged = PlayerActivitySelection.Merge(local, observed, now);
            if (merged != null) activity.Add(merged);
        }
        IReadOnlyList<ScenicRegion> destinations = settings.ScenicTravel.Value ? regions : Array.Empty<ScenicRegion>();
        policy.Tick(now, dt, players, destinations, Convert(Player.m_localPlayer.transform.position),
            presentedId, idleReady, observer.Phase == ObserverPhase.Travelling,
            wildlifeCutaway && observer.Phase == ObserverPhase.Watching, activity, usablePlayerFootage, idleSceneAvailable, idleSearchExpired,
            scenicFootageReady: scenicPicture && (ScenicInterest == null || destinationSubjectShown), scenicFootageDelta: Math.Min(dt, .25),
            scenicContentExhausted: scenicContentExhausted, relatedPlayerView: relatedPlayerView, scenicJourneyActive: scenicJourneyActive, scenicPreparationActive: scenicPreparationActive,
            scenicEditBoundary: scenicEditBoundary);
        if (destinationVisit.Id != null && !justArrived)
        {
            // A player/creature takeover or operator interruption is not a failed destination.
            if (presentedId != null || wildlifeCutaway || creaturePicture || policy.RouteTestActive ||
                policy.State != AutonomyState.Scenic && policy.State != AutonomyState.Waiting && policy.PendingTravel?.Kind != AutonomyTravelKind.Scenic)
            { destinationVisit.Reset(); currentInterest = null; }
            else if (observer.Phase == ObserverPhase.Watching)
            {
                DestinationOutcome? outcome = destinationVisit.Observe(dt, scenicPicture,
                    idleSearchExpired || policy.PendingTravel?.Kind == AutonomyTravelKind.Scenic && !scenicContentExhausted,
                    destinationSubjectShown, destinationSubjectUnavailable, destinationSubjectSatisfied);
                if (outcome.HasValue) ReportDestination(destinationVisit.Id, outcome.Value, destinationVisit.FootageSeconds, now);
            }
        }
        targetDiagnostics = "none";
        foreach (AutonomyPlayer player in players)
            if (player.Id == policy.TargetId)
            {
                targetDiagnostics = player.ReadinessReason + "; carrier-to-target " +
                    (player.Position.HasValue ? Convert(Player.m_localPlayer.transform.position).Distance(player.Position.Value).ToString("F1") : "unknown") +
                    "m; required camera movement " + (player.CameraDisplacement?.ToString("F1") ?? "unknown") + "m";
                break;
            }
        AutonomyTravelRequest? request = policy.PendingTravel;
        if (request == null || observer.Phase != ObserverPhase.Watching) return;
        destinationVisit.Reset(); currentInterest = null;
        interestTravelId = request.Kind == AutonomyTravelKind.Scenic && interestSites.ContainsKey(request.TargetId) ? request.TargetId : null;
        interestTravelSite = interestTravelId != null ? interestSites[interestTravelId] : null;
        interestTravelToken = request.Token;
        lastTravelReason = request.Reason + (interestTravelId == null ? "" : "; server interest " + interestTravelId);
        if (ScenicTrace.Enabled && request.Kind == AutonomyTravelKind.Scenic)
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("travel").Field("reason", lastTravelReason).Field("interest", interestTravelId ?? "").Field("id", request.TargetId)
                .Field("sceneAvailable", idleSceneAvailable ?? idleReady).Field("usablePicture", idleReady)
                .Field("searchExpired", idleSearchExpired).Field("readyDwell", policy.ReadyDwellSeconds),
                "destination", new Vector3(request.Position.X, request.Position.Y, request.Position.Z)));
        try
        {
            observer.TravelTo(new Vector3(request.Position.X, request.Position.Y, request.Position.Z));
            if (observer.Phase != ObserverPhase.Travelling)
            {
                if (interestTravelId != null) ReportDestination(interestTravelId, DestinationOutcome.TravelFailed, 0, now);
                interestTravelId = null;
                policy.TravelCompleted(request.Token, false, now, observer.Status);
            }
            else
            {
                policy.TravelStarted(request.Token, now);
                log("Automatic " + request.Kind + " travel to " + request.Name + ". Reason: " + lastTravelReason);
            }
        }
        catch (InvalidOperationException error)
        {
            if (interestTravelId != null) ReportDestination(interestTravelId, DestinationOutcome.TravelFailed, 0, now);
            interestTravelId = null;
            policy.TravelCompleted(request.Token, false, now, error.Message);
        }
    }

    private void ResetDestinations()
    {
        generatedRegions.Clear(); interestSites.Clear(); destinationHistory.Clear(); destinationVisit.Reset();
        appearance.Reset(); journeyCataloguePublished = false; lastDiscoveryFresh = false; journeyVisitChanged = false;
        interestTravelId = null; currentInterest = interestTravelSite = null; interestTravelToken = 0; nextDestinations = 0; destinationFeedback = "none";
    }
    private void ReportDestination(string id, DestinationOutcome outcome, float footage, double now)
    {
        destinationHistory.Record(id, outcome, now); nextDestinations = 0;
        remote.ReportDestination(id, outcome, footage);
        destinationFeedback = id + " / " + outcome;
        log("Destination feedback: " + destinationFeedback + "; usable scenic footage " + footage.ToString("F1") + "s.");
    }

    internal string AddRoutePoint(Vector3 position)
    {
        if (!observer.RemoteSession || !remote.Approved) throw new InvalidOperationException("Join an authorized observer world first.");
        if (route.Count >= 32) throw new InvalidOperationException("A solo route supports at most 32 points.");
        var point = new ScenicRegion("route:" + route.Count, "Marked destination " + (route.Count + 1), Convert(position));
        route.Add(point);
        return point.Name + " saved at " + position.ToString("F1");
    }
    internal void StartRoute()
    {
        if (!observer.RemoteSession || !remote.Approved || observer.Phase != ObserverPhase.Watching)
            throw new InvalidOperationException("Start an authorized observer and finish current travel first.");
        Enable(true); policy.BeginRouteTest(route, Time.realtimeSinceStartupAsDouble);
    }
    internal void StopRoute() => policy.StopRouteTest();
    internal void ClearRoute() { policy.StopRouteTest(); route.Clear(); }
    private static AutonomyPosition Convert(Vector3 point) => new AutonomyPosition(point.x, point.y, point.z);
    private static AutonomyPosition Convert(ObserverPoint point) => new AutonomyPosition(point.X, point.Y, point.Z);
}
