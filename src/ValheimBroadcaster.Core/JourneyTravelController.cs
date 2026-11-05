using System;

namespace ValheimBroadcaster.Core;

public enum JourneyTravelState { None, Departure, Travelling, Replanning, Arriving, Completed, Cancelled }
public enum JourneyTravelAuthorization { Unknown, Granted, Revoked }
public enum JourneyTravelAvailability { Pending, Ready, Lost }
public enum JourneyTravelPreemption { None, Operator, Player }
public enum JourneyTravelNavigation { Clear, WaitingForObservation, WaitingForLoading, Blocked, LoadingFailed, SearchExhausted }
public enum JourneyTravelEditorial { Clear, Obscured, ChangingShot }
public enum JourneyTravelCause
{
    None, PreparingDeparture, RouteUnavailable, ObservationPending, LoadingPending, PhysicalRouteBlocked,
    SceneChanged, AuthorizationPending, ArrivalPending, Arrived, OperatorPreempted, PlayerPreempted,
    AuthorizationRevoked, WorldChanged, WorldLost, LoadingFailed, NavigationExhausted, DepartureExpired, RuntimeExpired,
    NavigationStalled, NoProgressExpired
}

/// <summary>Physical route evidence produced by the game adapter after its
/// complete route/ending and loading validation. This is not a candidate's
/// proposed length or a picture-quality score. Scene identity is mandatory.</summary>
public readonly struct JourneyRouteEvidence
{
    public long RouteId { get; }
    public long SceneEpoch { get; }
    public double CertifiedRemainingMetres { get; }
    public bool IsStationary { get; }
    public bool HasRoute => RouteId > 0 && SceneEpoch > 0;

    public JourneyRouteEvidence(long routeId, long sceneEpoch, double certifiedRemainingMetres, bool stationary = false)
    {
        if (routeId <= 0 || sceneEpoch <= 0 || double.IsNaN(certifiedRemainingMetres) ||
            double.IsInfinity(certifiedRemainingMetres) || certifiedRemainingMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(certifiedRemainingMetres));
        RouteId = routeId; SceneEpoch = sceneEpoch; CertifiedRemainingMetres = certifiedRemainingMetres;
        IsStationary = stationary;
    }
}

/// <summary>One observation of mission authority and physical navigation.
/// Editorial state and picture availability are diagnostic inputs, never
/// substitutes for physical proof or reasons to cancel the travel request.
/// SearchExhausted means exhausted physical navigation, not an aesthetic shot
/// search; LoadingFailed means an actual unrecoverable loading failure.</summary>
public readonly struct JourneyTravelObservation
{
    public double At { get; }
    public long WorldId { get; }
    public long SceneEpoch { get; }
    public JourneyTravelAuthorization Authorization { get; }
    public JourneyTravelAvailability Availability { get; }
    public JourneyTravelPreemption Preemption { get; }
    public JourneyTravelNavigation Navigation { get; }
    public JourneyTravelEditorial Editorial { get; }
    public bool Held { get; }
    public bool PictureAvailable { get; }
    public bool DestinationReached { get; }
    public bool ArrivalPresented { get; }
    public JourneyRouteEvidence Route { get; }
    public double? RenderedTravelMetres { get; }

    public JourneyTravelObservation(double at, long worldId, long sceneEpoch,
        JourneyTravelAuthorization authorization = JourneyTravelAuthorization.Granted,
        JourneyTravelAvailability availability = JourneyTravelAvailability.Ready,
        JourneyTravelPreemption preemption = JourneyTravelPreemption.None,
        JourneyTravelNavigation navigation = JourneyTravelNavigation.Clear,
        bool held = false, bool pictureAvailable = true, bool destinationReached = false,
        bool arrivalPresented = false, JourneyRouteEvidence route = default,
        JourneyTravelEditorial editorial = JourneyTravelEditorial.Clear, double? renderedTravelMetres = null)
    {
        if (double.IsNaN(at) || double.IsInfinity(at) || at < 0 || sceneEpoch < 0 ||
            authorization < JourneyTravelAuthorization.Unknown || authorization > JourneyTravelAuthorization.Revoked ||
            availability < JourneyTravelAvailability.Pending || availability > JourneyTravelAvailability.Lost ||
            preemption < JourneyTravelPreemption.None || preemption > JourneyTravelPreemption.Player ||
            navigation < JourneyTravelNavigation.Clear || navigation > JourneyTravelNavigation.SearchExhausted ||
            editorial < JourneyTravelEditorial.Clear || editorial > JourneyTravelEditorial.ChangingShot ||
            renderedTravelMetres.HasValue && (double.IsNaN(renderedTravelMetres.Value) ||
                double.IsInfinity(renderedTravelMetres.Value) || renderedTravelMetres.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(at));
        At = at; WorldId = worldId; SceneEpoch = sceneEpoch; Authorization = authorization; Availability = availability;
        Preemption = preemption; Navigation = navigation; Editorial = editorial; Held = held;
        PictureAvailable = pictureAvailable; DestinationReached = destinationReached; ArrivalPresented = arrivalPresented; Route = route;
        RenderedTravelMetres = renderedTravelMetres;
    }
}

/// <summary>The single lifetime owner of a Journey request. Camera cuts,
/// presentation recovery, local cache generations and route retries do not
/// replace this owner. It grants no geometry authority: only explicitly
/// accepted, current adapter-certified routes can permit physical advance.
/// Captured distance and itinerary mathematics remain in ScenicJourneyPolicy.</summary>
public sealed class JourneyTravelController
{
    public const double LocalReplanAfterSeconds = 10, MaximumNoProgressSeconds = 90;
    public long MissionId { get; private set; }
    public long WorldId { get; private set; }
    public long SceneEpoch { get; private set; }
    public long AcceptedRouteId { get; private set; }
    public JourneyTravelState State { get; private set; }
    public JourneyTravelCause Cause { get; private set; }
    public string Reason { get; private set; } = "No Journey requested";
    public bool Active => State == JourneyTravelState.Departure || State == JourneyTravelState.Travelling ||
        State == JourneyTravelState.Replanning || State == JourneyTravelState.Arriving;
    public bool SuppressesScenicRelocation => Active;
    public bool CanAdvance { get; private set; }
    public bool HasStarted { get; private set; }
    public bool Held { get; private set; }
    public bool PictureAvailable { get; private set; }
    public JourneyTravelEditorial Editorial { get; private set; }
    public JourneyTravelNavigation Navigation { get; private set; }
    public double CertifiedRemainingMetres { get; private set; }
    public double DepartureElapsedSeconds { get; private set; }
    public double TravelElapsedSeconds { get; private set; }
    public double NoProgressSeconds { get; private set; }
    public bool LocalReplanRequested => Active && HasStarted && !localReplanAcknowledged && NoProgressSeconds >= LocalReplanAfterSeconds;
    public double DepartureDeadlineSeconds { get; private set; }
    public double TravelBudgetSeconds { get; private set; }
    public double StartedAt { get; private set; }
    public double FirstRouteAt { get; private set; } = -1;
    private double observedAt;
    private double measuredTravelMetres;
    private bool authorityKnown, destinationReached, localReplanAcknowledged;
    private JourneyTravelAuthorization authorization;
    private JourneyTravelAvailability availability;

    public void Begin(long missionId, long worldId, long sceneEpoch, double now,
        double departureDeadlineSeconds, double travelBudgetSeconds)
    {
        Number(now, nameof(now)); Positive(departureDeadlineSeconds, nameof(departureDeadlineSeconds));
        Positive(travelBudgetSeconds, nameof(travelBudgetSeconds));
        if (missionId <= 0 || worldId == 0 || sceneEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(missionId));
        if (Active) throw new InvalidOperationException("An active Journey request cannot reset its own identity or deadlines.");
        Reset(); MissionId = missionId; WorldId = worldId; SceneEpoch = sceneEpoch;
        StartedAt = observedAt = now; DepartureDeadlineSeconds = departureDeadlineSeconds; TravelBudgetSeconds = travelBudgetSeconds;
        State = JourneyTravelState.Departure; Cause = JourneyTravelCause.PreparingDeparture;
        Reason = "Preparing physical departure; Journey owns the destination and relocation decision";
    }

    public JourneyTravelState Observe(JourneyTravelObservation observation)
    {
        if (!Active) return State;
        AdvanceClock(observation.At);
        // The captured cumulative distance is the progress authority. Route
        // admission, cuts and retries never renew this watchdog. Accumulate
        // sub-centimetre deltas before crediting them, so rounding jitter cannot
        // keep a stationary mission alive indefinitely.
        if (observation.RenderedTravelMetres.HasValue && observation.RenderedTravelMetres.Value > measuredTravelMetres + .01)
        { measuredTravelMetres = observation.RenderedTravelMetres.Value; NoProgressSeconds = 0; localReplanAcknowledged = false; }
        Held = observation.Held; PictureAvailable = observation.PictureAvailable; Editorial = observation.Editorial;
        Navigation = observation.Navigation; authorization = observation.Authorization; availability = observation.Availability;
        authorityKnown = observation.WorldId == WorldId && observation.SceneEpoch > 0;
        CanAdvance = false; CertifiedRemainingMetres = 0;
        if (observation.Preemption == JourneyTravelPreemption.Operator)
            return End(JourneyTravelCause.OperatorPreempted, "Journey cancelled by an explicit operator action");
        if (observation.Preemption == JourneyTravelPreemption.Player)
            return End(JourneyTravelCause.PlayerPreempted, "Journey yielded to explicit player-priority coverage");
        if (authorization == JourneyTravelAuthorization.Revoked)
            return End(JourneyTravelCause.AuthorizationRevoked, "Observer travel authorization was revoked");
        if (observation.WorldId != 0 && observation.WorldId != WorldId)
            return End(JourneyTravelCause.WorldChanged, "Journey belongs to a different world");
        if (availability == JourneyTravelAvailability.Lost)
            return End(JourneyTravelCause.WorldLost, "Journey world/session is no longer available");
        if (Navigation == JourneyTravelNavigation.LoadingFailed)
            return End(JourneyTravelCause.LoadingFailed, "World loading failed; the navigation adapter cannot recover this Journey");
        if (Navigation == JourneyTravelNavigation.SearchExhausted)
            return End(JourneyTravelCause.NavigationExhausted, "Observed physical navigation alternatives are exhausted");
        if (Expire()) return State;

        bool changedScene = observation.SceneEpoch > 0 && observation.SceneEpoch != SceneEpoch;
        if (changedScene)
        {
            SceneEpoch = observation.SceneEpoch; AcceptedRouteId = 0;
            State = JourneyTravelState.Replanning; Cause = JourneyTravelCause.SceneChanged;
            Reason = "Local scene observations changed; rebuilding physical proof for the same Journey";
        }
        destinationReached = observation.DestinationReached;
        if (authorization != JourneyTravelAuthorization.Granted)
            return Wait(JourneyTravelCause.AuthorizationPending, "Waiting for current observer/world authority; Journey retained");
        if (!authorityKnown || availability != JourneyTravelAvailability.Ready || Navigation == JourneyTravelNavigation.WaitingForLoading)
            return Wait(JourneyTravelCause.LoadingPending, "Waiting for world loading; Journey retained");
        if (Navigation == JourneyTravelNavigation.WaitingForObservation)
            return Wait(JourneyTravelCause.ObservationPending, "Waiting for physical scene observations; Journey retained");
        if (Navigation == JourneyTravelNavigation.Blocked)
        {
            AcceptedRouteId = 0;
            return Wait(JourneyTravelCause.PhysicalRouteBlocked, "Current physical route is blocked; planning a local alternative", true);
        }
        if (HasStarted && destinationReached && observation.ArrivalPresented)
        {
            State = JourneyTravelState.Completed; Cause = JourneyTravelCause.Arrived;
            Reason = "Measured destination reached and arrival presented"; AcceptedRouteId = 0; return State;
        }
        bool matching = AcceptedRouteId > 0 && observation.Route.RouteId == AcceptedRouteId &&
            observation.Route.SceneEpoch == SceneEpoch &&
            (observation.Route.CertifiedRemainingMetres > 0 || observation.Route.IsStationary);
        if (matching)
        {
            CertifiedRemainingMetres = observation.Route.CertifiedRemainingMetres;
            State = destinationReached ? JourneyTravelState.Arriving : JourneyTravelState.Travelling;
            Cause = destinationReached ? JourneyTravelCause.ArrivalPending : JourneyTravelCause.None;
            Reason = destinationReached ? "Developing verified arrival coverage" : "Travelling on the current physically certified route";
            CanAdvance = !Held;
            if (LocalReplanRequested)
            {
                Cause = JourneyTravelCause.NavigationStalled;
                Reason = "No measured travel for " + NoProgressSeconds.ToString("F1") + " seconds; preparing a local physical alternative for the same Journey";
            }
        }
        else if (destinationReached && HasStarted)
        {
            State = JourneyTravelState.Arriving; Cause = JourneyTravelCause.ArrivalPending;
            Reason = "At the measured destination; preparing readable arrival coverage";
        }
        else if (!changedScene)
            Wait(JourneyTravelCause.RouteUnavailable, "Preparing a current physically certified route; Journey retained");
        return State;
    }

    /// <summary>Call only after the game adapter has admitted this route and
    /// its safe stopping/ending strategy. A prepared candidate cannot activate
    /// travel through Observe alone. Replacing a route never renews runtime.</summary>
    public bool TryAcceptRoute(JourneyRouteEvidence route, double now, out string reason)
    {
        Number(now, nameof(now));
        if (!Active) { reason = "No active Journey can accept a route"; return false; }
        AdvanceClock(now);
        if (Expire()) { reason = Reason; return false; }
        if (!authorityKnown || authorization != JourneyTravelAuthorization.Granted || availability != JourneyTravelAvailability.Ready ||
            Held || Navigation == JourneyTravelNavigation.WaitingForLoading || Navigation == JourneyTravelNavigation.WaitingForObservation ||
            !route.HasRoute || route.SceneEpoch != SceneEpoch || (route.CertifiedRemainingMetres <= 0 && !route.IsStationary))
        { reason = "Route admission requires current physical proof, world authority and unheld operation"; return false; }
        if (route.RouteId == AcceptedRouteId)
        { reason = "This Journey route is already admitted; report remaining certified distance through Observe"; return false; }
        AcceptedRouteId = route.RouteId; CertifiedRemainingMetres = route.CertifiedRemainingMetres;
        Navigation = JourneyTravelNavigation.Clear;
        if (!HasStarted) { HasStarted = true; FirstRouteAt = now; }
        State = destinationReached ? JourneyTravelState.Arriving : JourneyTravelState.Travelling;
        Cause = destinationReached ? JourneyTravelCause.ArrivalPending : JourneyTravelCause.None;
        Reason = reason = "Accepted a physically certified route for the existing Journey"; CanAdvance = true;
        return true;
    }

    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Cancellation requires an explicit reason.", nameof(reason));
        if (Active) End(JourneyTravelCause.OperatorPreempted, reason);
    }

    /// <summary>Consume this measured stall's local replan request without
    /// renewing its deadline. A newly admitted repair must be allowed to render
    /// before it can demonstrate movement; route IDs do not start new episodes.</summary>
    public bool AcknowledgeLocalReplan()
    {
        if (!LocalReplanRequested) return false;
        localReplanAcknowledged = true; return true;
    }

    private JourneyTravelState Wait(JourneyTravelCause cause, string reason, bool forceReplan = false)
    {
        State = HasStarted || forceReplan || State == JourneyTravelState.Replanning ? JourneyTravelState.Replanning : JourneyTravelState.Departure;
        Cause = cause; Reason = reason; return State;
    }
    private JourneyTravelState End(JourneyTravelCause cause, string reason)
    {
        State = JourneyTravelState.Cancelled; Cause = cause; Reason = reason;
        CanAdvance = false; CertifiedRemainingMetres = 0; AcceptedRouteId = 0; return State;
    }
    private bool Expire()
    {
        if (!HasStarted && DepartureElapsedSeconds >= DepartureDeadlineSeconds)
        { End(JourneyTravelCause.DepartureExpired, "Departure made no physical start before its original request deadline; last state: " + Reason); return true; }
        if (HasStarted && TravelElapsedSeconds >= TravelBudgetSeconds)
        { End(JourneyTravelCause.RuntimeExpired, "Journey exhausted its original active runtime, including local replanning; last state: " + Reason); return true; }
        if (HasStarted && NoProgressSeconds >= MaximumNoProgressSeconds)
        { End(JourneyTravelCause.NoProgressExpired, "Journey made no measured travel for 90 active seconds despite local replanning; last state: " + Reason); return true; }
        return false;
    }
    private void AdvanceClock(double now)
    {
        Number(now, nameof(now));
        if (now < observedAt) throw new ArgumentOutOfRangeException(nameof(now), "Journey observations require a monotonic clock.");
        double elapsed = now - observedAt; observedAt = now;
        if (Held) return;
        if (HasStarted) { TravelElapsedSeconds += elapsed; NoProgressSeconds += elapsed; } else DepartureElapsedSeconds += elapsed;
    }
    public void Reset()
    {
        MissionId = WorldId = SceneEpoch = AcceptedRouteId = 0; State = JourneyTravelState.None; Cause = JourneyTravelCause.None;
        Reason = "No Journey requested"; CanAdvance = HasStarted = Held = PictureAvailable = authorityKnown = destinationReached = localReplanAcknowledged = false;
        Editorial = JourneyTravelEditorial.Clear; Navigation = JourneyTravelNavigation.Clear;
        authorization = JourneyTravelAuthorization.Unknown; availability = JourneyTravelAvailability.Pending;
        CertifiedRemainingMetres = DepartureElapsedSeconds = TravelElapsedSeconds = NoProgressSeconds = measuredTravelMetres =
            DepartureDeadlineSeconds = TravelBudgetSeconds = StartedAt = observedAt = 0;
        FirstRouteAt = -1;
    }
    private static void Number(double value, string name)
    { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    private static void Positive(double value, string name)
    { Number(value, name); if (value == 0) throw new ArgumentOutOfRangeException(name); }
}
