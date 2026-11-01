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
    private readonly Action resetShot;
    private readonly Action<string> log;
    private readonly AutonomousDirector policy = new AutonomousDirector();
    private readonly List<AutonomyPlayer> players = new List<AutonomyPlayer>(64);
    private readonly List<ScenicRegion> regions = new List<ScenicRegion>(1024);
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
    internal string Status => paused ? "Automatic direction paused for manual control" : !WantsEnabled ? "Automatic direction off" :
        recovering ? "Recovering travel: " + recoveryReason : policy.Reason;
    internal string Diagnostics => "Director: " + Status + " | state " + policy.State +
        "\nTarget: " + (policy.TargetName ?? "none") + "; ready dwell " + policy.ReadyDwellSeconds.ToString("F1") + "s; retry in " + policy.RetryAfterSeconds(Time.realtimeSinceStartup).ToString("F0") + "s" +
        "\nTarget readiness: " + targetDiagnostics + "; retaining shot " + policy.PresentationGraceActive +
        "\nLast travel decision: " + lastTravelReason +
        "\nRegion: " + (policy.CurrentRegionName ?? "initial area") + " / " + (policy.CurrentRegionId ?? "unknown") +
        "; known " + regions.Count + "; recent " + policy.VisitedRegionCount + "; completed travel " + policy.CompletedTravels + "; failed " + policy.FailedTravels +
        "\nLast travel failure: " + (policy.LastFailure ?? "none") + "; protected returns " + recoveries + "/3" +
        "\nSolo route: " + policy.RouteTestResult + "; marked " + route.Count;

    internal AutonomousBroadcast(BroadcastSettings settings, RemoteObserverClient remote, ObserverController observer,
        Action resetShot, Action<string> log)
    { this.settings = settings; this.remote = remote; this.observer = observer; this.resetShot = resetShot; this.log = log; }

    internal void Started()
    {
        paused = recovering = false; recoveries = 0; retryRecoveryAt = 0; stableSince = -1; reconnectReason = null;
        policy.SetEnabled(WantsEnabled); policy.SetPaused(false);
    }
    internal void EndSession()
    {
        targetDiagnostics = lastTravelReason = "none";
        policy.Reset(); players.Clear(); regions.Clear(); route.Clear(); sequence = -1; worldId = 0;
        paused = recovering = false; recoveries = 0; stableSince = -1; reconnectReason = null;
    }
    internal void Enable(bool enabled)
    {
        requested = enabled; paused = false; recovering = false; reconnectReason = null;
        policy.SetEnabled(enabled); policy.SetPaused(false);
    }
    internal void PauseForManual()
    {
        paused = true; recovering = false; reconnectReason = null;
        policy.SetPaused(true);
    }
    internal void SetHolding(bool held) => policy.SetHolding(held);
    internal void Next(int direction) => policy.Next(direction);
    internal void Select(string id) { policy.Select(id); policy.SetHolding(true); }
    internal string? TakeReconnectReason() { string? value = reconnectReason; reconnectReason = null; return value; }

    internal bool CanFrame(TargetRoster.Target? target)
    {
        Player? player = target?.LoadedPlayer;
        if (!player || player.IsDead() || !Player.m_localPlayer || !IdleShots.Ready(player.transform.position)) return false;
        Vector3 offset = player.transform.position - Player.m_localPlayer.transform.position;
        return CameraReachability.Safe(offset.x, offset.y, offset.z,
            settings.Distance.Value, settings.Height.Value, settings.Azimuth.Value);
    }

    internal void Tick(float dt, TargetRoster roster, string? presentedId, bool idleReady)
    {
        if (!observer.RemoteSession || !remote.Approved || !observer.Active || !Player.m_localPlayer) return;
        policy.SetEnabled(WantsEnabled);
        if (!WantsEnabled || paused) return;
        double now = Time.realtimeSinceStartup;
        ObserverDiscoverySnapshot? snapshot = remote.Discovery;
        if (snapshot != null && worldId != snapshot.WorldId)
        {
            if (worldId != 0) { policy.Reset(); route.Clear(); policy.SetEnabled(true); }
            worldId = snapshot.WorldId; sequence = -1;
        }
        if (snapshot != null && snapshot.Sequence != sequence)
        {
            sequence = snapshot.Sequence; regions.Clear();
            foreach (ObserverRegion region in snapshot.Regions)
                regions.Add(new ScenicRegion(region.Id, string.IsNullOrWhiteSpace(region.Name) ? region.Id : region.Name, Convert(region.Position)));
        }
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
                policy.TravelCompleted(policy.ActiveTravel.Token, true, now);
                resetShot();
            }
            else if (observer.Phase == ObserverPhase.Suspended)
                policy.TravelCompleted(policy.ActiveTravel.Token, false, now, observer.Status);
        }
        if (recovering && observer.Phase == ObserverPhase.Parked)
        {
            observer.Resume(); recovering = false; resetShot();
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

        players.Clear();
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
                Vector3 offset = position.Value - Player.m_localPlayer.transform.position;
                double required = CameraReachability.RequiredDisplacement(offset.x, offset.y, offset.z,
                    settings.Distance.Value, settings.Height.Value, settings.Azimuth.Value);
                if (!double.IsNaN(required) && !double.IsInfinity(required)) movement = required;
                else position = null; // Invalid geometry cannot authorize travel.
            }
            bool areaReady = live && IdleShots.Ready(actor!.transform.position);
            bool reachable = movement.HasValue && movement.Value < CameraReachability.ImmediateLimit;
            string reason = dead ? "Character dead; waiting for respawn" : !position.HasValue ? "No fresh coordinates" :
                !reachable ? "Outside immediate camera reach" : !live ? "Character not loaded locally" :
                !areaReady ? "Target area loading locally" : "Locally frameable";
            players.Add(new AutonomyPlayer(target.Identity.Id, string.IsNullOrWhiteSpace(target.Identity.Name) ? "Player " + target.Identity.Id : target.Identity.Name,
                position.HasValue ? Convert(position.Value) : (AutonomyPosition?)null,
                position.HasValue ? now + (live ? ObserverDiscoveryProtocol.FreshSeconds : Math.Max(0, ObserverDiscoveryProtocol.FreshSeconds - remote.DiscoveryAge)) : now,
                live && areaReady && reachable, movement, reason));
        }
        IReadOnlyList<ScenicRegion> destinations = remote.DiscoveryFresh && settings.ScenicTravel.Value ? regions : Array.Empty<ScenicRegion>();
        policy.Tick(now, dt, players, destinations, Convert(Player.m_localPlayer.transform.position),
            presentedId, idleReady, observer.Phase == ObserverPhase.Travelling);
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
        lastTravelReason = request.Reason;
        try
        {
            observer.TravelTo(new Vector3(request.Position.X, request.Position.Y, request.Position.Z));
            if (observer.Phase != ObserverPhase.Travelling)
                policy.TravelCompleted(request.Token, false, now, observer.Status);
            else
            {
                policy.TravelStarted(request.Token, now);
                log("Automatic " + request.Kind + " travel to " + request.Name + ". Reason: " + request.Reason);
            }
        }
        catch (InvalidOperationException error) { policy.TravelCompleted(request.Token, false, now, error.Message); }
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
        Enable(true); policy.BeginRouteTest(route, Time.realtimeSinceStartup);
    }
    internal void StopRoute() => policy.StopRouteTest();
    internal void ClearRoute() { policy.StopRouteTest(); route.Clear(); }
    private static AutonomyPosition Convert(Vector3 point) => new AutonomyPosition(point.x, point.y, point.z);
    private static AutonomyPosition Convert(ObserverPoint point) => new AutonomyPosition(point.X, point.Y, point.Z);
}
