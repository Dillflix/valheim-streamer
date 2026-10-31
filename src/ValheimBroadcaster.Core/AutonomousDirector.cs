using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public readonly struct AutonomyPosition
    {
        public readonly float X, Y, Z;
        public AutonomyPosition(float x, float y, float z)
        {
            if (!Valid(x) || !Valid(y) || !Valid(z)) throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite and within the supported world bounds.");
            X = x; Y = y; Z = z;
        }
        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 20000;
        public double Distance(AutonomyPosition other)
        { double x = X - other.X, y = Y - other.Y, z = Z - other.Z; return Math.Sqrt(x * x + y * y + z * z); }
        public double HorizontalDistance(AutonomyPosition other)
        { double x = X - other.X, z = Z - other.Z; return Math.Sqrt(x * x + z * z); }
    }

    /// <summary>Session identity survives a character respawn. SceneReady is supplied by the game adapter.</summary>
    public sealed class AutonomyPlayer
    {
        public string Id { get; }
        public string Name { get; }
        public AutonomyPosition? Position { get; }
        public double PositionFreshUntil { get; }
        public bool SceneReady { get; }
        public AutonomyPlayer(string id, string name, AutonomyPosition? position, double positionFreshUntil, bool sceneReady)
        {
            Id = AutonomousDirector.Label(id, nameof(id)); Name = AutonomousDirector.Label(name, nameof(name));
            AutonomousDirector.Time(positionFreshUntil, nameof(positionFreshUntil));
            Position = position; PositionFreshUntil = positionFreshUntil; SceneReady = sceneReady;
        }
        public bool HasFreshPosition(double now) => Position.HasValue && now < PositionFreshUntil;
    }

    /// <summary>A previously generated, read-only destination supplied by the companion or an explicit solo route.</summary>
    public sealed class ScenicRegion
    {
        public string Id { get; }
        public string Name { get; }
        public AutonomyPosition Position { get; }
        public ScenicRegion(string id, string name, AutonomyPosition position)
        { Id = AutonomousDirector.Label(id, nameof(id)); Name = AutonomousDirector.Label(name, nameof(name)); Position = position; }
    }

    public enum AutonomyState { Disabled, Following, Acquiring, Scenic, Travelling, Waiting, Paused, RouteTest }
    public enum AutonomyTravelKind { Player, Scenic, RouteTest }
    public sealed class AutonomyTravelRequest
    {
        public long Token { get; }
        public AutonomyTravelKind Kind { get; }
        public string TargetId { get; }
        public string Name { get; }
        public AutonomyPosition Position { get; }
        internal AutonomyTravelRequest(long token, AutonomyTravelKind kind, string targetId, string name, AutonomyPosition position)
        { Token = token; Kind = kind; TargetId = targetId; Name = name; Position = position; }
    }

    /// <summary>
    /// Chooses subjects and travel destinations; never performs a game/network operation.
    /// The adapter must acknowledge each request and report completion of its bounded native travel.
    /// A request is immutable while in flight; updated player coordinates are considered after arrival.
    /// </summary>
    public sealed class AutonomousDirector
    {
        private const int HistoryLimit = 128;
        private readonly Dictionary<string, double> visited = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, Failure> failures = new Dictionary<string, Failure>(StringComparer.Ordinal);
        private string? targetId, lastPlayer, requestedSelection;
        private int requestedStep;
        private bool paused, started, idleActive;
        private double lastNow, acquisitionBegan, dwell, idleDwell, idleUnreadyBegan = -1;
        private double pendingAt, lastTravelAt = double.NegativeInfinity;
        private long token;
        private int acquisitionTrips;
        private AutonomyPosition? lastPlayerDestination;
        private IReadOnlyList<ScenicRegion>? route;
        private int routeIndex;
        private bool routeArrived;
        private double routeDwell, routeArrivalAt;
        private double dwellSeconds = 30, acquisitionSeconds = 65, regionSeconds = 120;
        private double retrySeconds = 30, revisitSeconds = 1800, minTravelSeconds = 8;

        private sealed class Failure
        {
            internal double Last, Until;
            internal int Attempts;
        }

        public bool Enabled { get; private set; }
        public bool Held { get; private set; }
        public AutonomyState State { get; private set; }
        public string Reason { get; private set; } = "Automatic direction disabled";
        public string? LastFailure { get; private set; }
        public string? TargetId => targetId;
        public string? TargetName { get; private set; }
        public string? CurrentRegionId { get; private set; }
        public string? CurrentRegionName { get; private set; }
        public long Revision { get; private set; }
        public AutonomyTravelRequest? PendingTravel { get; private set; }
        public AutonomyTravelRequest? ActiveTravel { get; private set; }
        public int VisitedRegionCount => visited.Count;
        public int FailedDestinationCount => failures.Count;
        public long CompletedTravels { get; private set; }
        public long FailedTravels { get; private set; }
        public bool RouteTestActive => route != null;
        public string RouteTestResult { get; private set; } = "No solo route run";
        public int RouteIndex => routeIndex;
        public int RouteCount => route?.Count ?? 0;
        public double ReadyDwellSeconds => dwell;
        public double DwellSeconds { get => dwellSeconds; set => dwellSeconds = Positive(value, nameof(DwellSeconds)); }
        public double AcquisitionSeconds { get => acquisitionSeconds; set => acquisitionSeconds = Positive(value, nameof(AcquisitionSeconds)); }
        public double RegionSeconds { get => regionSeconds; set => regionSeconds = Positive(value, nameof(RegionSeconds)); }
        public double RetrySeconds { get => retrySeconds; set => retrySeconds = Positive(value, nameof(RetrySeconds)); }
        public double RevisitSeconds { get => revisitSeconds; set => revisitSeconds = Positive(value, nameof(RevisitSeconds)); }
        public double MinimumTravelSeconds { get => minTravelSeconds; set => minTravelSeconds = Positive(value, nameof(MinimumTravelSeconds)); }
        public double MinimumPlayerTravelDistance { get; set; } = 40;
        public double MinimumScenicTravelDistance { get; set; } = 256;
        public double MaximumScenicTravelDistance { get; set; } = 10000;
        public double UnreadyScenicSeconds { get; set; } = 10;
        public double RequestAcceptanceSeconds { get; set; } = 5;

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            paused = false; PendingTravel = ActiveTravel = null; route = null;
            SetTarget(null, 0); started = false; idleActive = false; idleDwell = 0; idleUnreadyBegan = -1;
            requestedSelection = null; requestedStep = 0;
            State = enabled ? AutonomyState.Waiting : AutonomyState.Disabled;
            Reason = enabled ? "Waiting for discovery" : "Automatic direction disabled";
        }
        /// <summary>Pause for an explicit manual trip/return. The adapter remains responsible for any in-flight travel.</summary>
        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (value) { PendingTravel = ActiveTravel = null; State = AutonomyState.Paused; Reason = "Automatic direction paused"; }
            else { acquisitionBegan = lastNow; idleUnreadyBegan = -1; }
        }
        public void SetHolding(bool value) => Held = value;
        public void Next(int direction = 1)
        {
            if (direction != 1 && direction != -1) throw new ArgumentOutOfRangeException(nameof(direction));
            requestedStep = direction; requestedSelection = null;
        }
        public void Select(string id) { requestedSelection = Label(id, nameof(id)); requestedStep = 0; }

        public void Tick(double now, double dt, IReadOnlyList<AutonomyPlayer> players, IReadOnlyList<ScenicRegion> regions,
            AutonomyPosition currentPosition, string? presentedPlayerId, bool idleShotReady, bool travelBusy = false)
        {
            Time(now, nameof(now)); Time(dt, nameof(dt));
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (players.Count > 128 || regions.Count > 1024) throw new ArgumentException("Discovery inputs exceed the bounded policy capacity.");
            if (started && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Reset the director before changing clocks/worlds.");
            Positive(MinimumPlayerTravelDistance, nameof(MinimumPlayerTravelDistance));
            Positive(MinimumScenicTravelDistance, nameof(MinimumScenicTravelDistance));
            Positive(MaximumScenicTravelDistance, nameof(MaximumScenicTravelDistance));
            if (MaximumScenicTravelDistance < MinimumScenicTravelDistance) throw new ArgumentOutOfRangeException(nameof(MaximumScenicTravelDistance));
            Positive(UnreadyScenicSeconds, nameof(UnreadyScenicSeconds)); Positive(RequestAcceptanceSeconds, nameof(RequestAcceptanceSeconds));
            lastNow = now;
            if (!Enabled) { State = AutonomyState.Disabled; return; }
            if (!started) { started = true; acquisitionBegan = now; }
            if (paused) { State = AutonomyState.Paused; Reason = "Automatic direction paused"; return; }
            if (ActiveTravel != null || travelBusy)
            {
                State = AutonomyState.Travelling;
                Reason = ActiveTravel == null ? "Waiting for controlled travel to finish" : "Travelling to " + ActiveTravel.Name;
                return;
            }
            if (PendingTravel != null)
            {
                if (now - pendingAt >= RequestAcceptanceSeconds)
                    TravelCompleted(PendingTravel.Token, false, now, "Travel request was not accepted before its deadline");
                else { State = AutonomyState.Acquiring; Reason = "Preparing travel to " + PendingTravel.Name; }
                return;
            }
            // A stall cannot count hours of unseen footage as target dwell.
            double footage = Math.Min(dt, 1);
            if (route != null) { TickRoute(now, footage, idleShotReady); return; }

            AutonomyPlayer? selected = Find(players, targetId);
            if (requestedSelection != null)
            {
                AutonomyPlayer? requested = Find(players, requestedSelection);
                requestedSelection = null;
                if (requested != null) { SetTarget(requested, now); selected = requested; }
            }
            if (requestedStep != 0)
            {
                selected = ChoosePlayer(players, now, targetId ?? lastPlayer, requestedStep);
                requestedStep = 0; SetTarget(selected, now);
            }
            if (selected == null || Blocked("p:" + selected.Id, now))
            {
                if (targetId != null) lastPlayer = targetId;
                selected = ChoosePlayer(players, now, lastPlayer, 1);
                SetTarget(selected, now);
            }
            if (selected != null && !selected.SceneReady && !selected.HasFreshPosition(now))
            {
                AutonomyPlayer? alternative = ChoosePlayer(players, now, selected.Id, 1);
                if (alternative != null) { selected = alternative; SetTarget(selected, now); }
            }
            if (selected != null && !(selected.SceneReady && string.Equals(presentedPlayerId, selected.Id, StringComparison.Ordinal)) &&
                now - acquisitionBegan >= AcquisitionSeconds)
            {
                Fail("p:" + selected.Id, now, "Could not acquire " + selected.Name + " before its deadline");
                lastPlayer = selected.Id; SetTarget(null, now); selected = null;
            }
            if (selected != null)
            {
                TargetName = selected.Name;
                if (selected.SceneReady)
                {
                    idleActive = false;
                    if (string.Equals(presentedPlayerId, selected.Id, StringComparison.Ordinal))
                    {
                        State = AutonomyState.Following; Reason = "Following " + selected.Name;
                        acquisitionBegan = now; acquisitionTrips = 0; lastPlayerDestination = null;
                        failures.Remove("p:" + selected.Id);
                        if (!Held) dwell = Math.Min(DwellSeconds, dwell + footage);
                        if (dwell >= DwellSeconds && !Held)
                        {
                            AutonomyPlayer? next = ChoosePlayer(players, now, selected.Id, 1);
                            if (next != null && next.Id != selected.Id) SetTarget(next, now);
                        }
                        return;
                    }
                    dwell = 0; State = AutonomyState.Acquiring; Reason = "Waiting for a valid camera shot of " + selected.Name;
                }
                else
                {
                    dwell = 0;
                    State = AutonomyState.Acquiring;
                    Reason = selected.HasFreshPosition(now) ? "Acquiring " + selected.Name : "Waiting for fresh coordinates for " + selected.Name;
                    if (selected.HasFreshPosition(now) && currentPosition.Distance(selected.Position!.Value) >= MinimumPlayerTravelDistance &&
                        now - lastTravelAt >= MinimumTravelSeconds && acquisitionTrips < 3 &&
                        (!lastPlayerDestination.HasValue || lastPlayerDestination.Value.Distance(selected.Position.Value) >= 24))
                    {
                        Request(AutonomyTravelKind.Player, selected.Id, selected.Name, selected.Position.Value, now);
                        return;
                    }
                }
                if (selected.SceneReady || selected.HasFreshPosition(now)) return;
                // A connected player without a usable position does not freeze idle coverage.
            }
            TickScenery(now, footage, players.Count, regions, currentPosition, idleShotReady);
        }

        private void TickScenery(double now, double footage, int connected, IReadOnlyList<ScenicRegion> regions,
            AutonomyPosition currentPosition, bool ready)
        {
            if (!idleActive)
            {
                idleActive = true; idleDwell = 0; idleUnreadyBegan = ready ? -1 : now;
                // Record the arrival area so the first roaming destination cannot immediately revisit it.
                ScenicRegion? nearest = null; double nearestDistance = 64;
                foreach (ScenicRegion region in regions)
                {
                    double distance = currentPosition.HorizontalDistance(region.Position);
                    if (distance < nearestDistance) { nearest = region; nearestDistance = distance; }
                }
                if (nearest != null)
                { CurrentRegionId = nearest.Id; CurrentRegionName = nearest.Name; Remember(nearest.Id, now); }
            }
            State = ready ? AutonomyState.Scenic : AutonomyState.Waiting;
            Reason = connected > 0 ? "Scenery while waiting for an available player" : ready ? "Exploring the current scenic region" : "Waiting for a valid scenic shot";
            if (ready) idleUnreadyBegan = -1;
            else if (idleUnreadyBegan < 0) idleUnreadyBegan = now;
            if (Held) { Reason += " (held)"; return; }
            if (ready) idleDwell += footage;
            bool exhausted = idleUnreadyBegan >= 0 && now - idleUnreadyBegan >= UnreadyScenicSeconds;
            if (idleDwell < RegionSeconds && !exhausted) return;
            if (now - lastTravelAt < MinimumTravelSeconds) { Reason = "Waiting for the travel cooldown"; return; }
            ScenicRegion? chosen = null; double oldest = double.PositiveInfinity, nearestChoice = double.PositiveInfinity;
            foreach (ScenicRegion region in regions)
            {
                double distance = currentPosition.HorizontalDistance(region.Position);
                if (region.Id == CurrentRegionId || distance < MinimumScenicTravelDistance || distance > MaximumScenicTravelDistance || Blocked("s:" + region.Id, now)) continue;
                bool seen = visited.TryGetValue(region.Id, out double lastVisit);
                if (seen && now - lastVisit < RevisitSeconds) continue;
                double rank = seen ? lastVisit : double.NegativeInfinity;
                if (chosen == null || rank < oldest || rank == oldest && (distance < nearestChoice ||
                    distance == nearestChoice && StringComparer.Ordinal.Compare(region.Id, chosen.Id) < 0))
                { chosen = region; oldest = rank; nearestChoice = distance; }
            }
            if (chosen == null)
            {
                Reason = regions.Count == 0 ? "Waiting for generated scenic destinations from the server" : "No fresh generated region available; revisit/failure cooldowns remain active";
                return;
            }
            if (!visited.ContainsKey(chosen.Id) && !HasVisitCapacity(now))
            { Reason = "Scenic history is full; waiting for a revisit cooldown before recording another region"; return; }
            Request(AutonomyTravelKind.Scenic, chosen.Id, chosen.Name, chosen.Position, now);
        }

        public void TravelStarted(long requestToken, double now)
        {
            CheckCallbackTime(now);
            if (PendingTravel == null || PendingTravel.Token != requestToken || ActiveTravel != null) return;
            ActiveTravel = PendingTravel; PendingTravel = null; lastTravelAt = now;
            if (ActiveTravel.Kind == AutonomyTravelKind.Player)
            { acquisitionTrips++; lastPlayerDestination = ActiveTravel.Position; }
            State = AutonomyState.Travelling; Reason = "Travelling to " + ActiveTravel.Name;
        }

        /// <summary>May reject a pending request before native travel starts; stale completion tokens are ignored.</summary>
        public void TravelCompleted(long requestToken, bool success, double now, string? reason = null)
        {
            CheckCallbackTime(now);
            AutonomyTravelRequest? request = ActiveTravel ?? PendingTravel;
            if (request == null || request.Token != requestToken) return;
            ActiveTravel = PendingTravel = null; lastTravelAt = now;
            if (!success)
            {
                FailedTravels++;
                Fail((request.Kind == AutonomyTravelKind.Player ? "p:" : "s:") + request.TargetId, now,
                    reason ?? "Travel to " + request.Name + " failed");
                if (request.Kind == AutonomyTravelKind.Player) { lastPlayer = request.TargetId; SetTarget(null, now); }
                else if (request.Kind == AutonomyTravelKind.RouteTest) EndRoute("Failed at " + request.Name + ": " + LastFailure);
                State = AutonomyState.Waiting; Reason = LastFailure!;
                return;
            }
            CompletedTravels++;
            if (request.Kind == AutonomyTravelKind.Player)
            { State = AutonomyState.Acquiring; Reason = "Waiting for " + request.Name + " to load"; }
            else if (request.Kind == AutonomyTravelKind.Scenic)
            {
                CurrentRegionId = request.TargetId; CurrentRegionName = request.Name; Remember(request.TargetId, now);
                idleActive = true; idleDwell = 0; idleUnreadyBegan = now;
                State = AutonomyState.Scenic; Reason = "Arrived in " + request.Name;
            }
            else
            { routeArrived = true; routeDwell = 0; routeArrivalAt = now; State = AutonomyState.RouteTest; Reason = "Checking scenic readiness at " + request.Name; }
        }

        public void BeginRouteTest(IReadOnlyList<ScenicRegion> destinations, double now)
        {
            CheckCallbackTime(now);
            if (!Enabled || ActiveTravel != null) throw new InvalidOperationException("Enable automatic direction and finish current travel first.");
            if (destinations == null || destinations.Count == 0 || destinations.Count > 32) throw new ArgumentException("A solo route needs one to 32 marked destinations.", nameof(destinations));
            var copy = new ScenicRegion[destinations.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = destinations[i] ?? throw new ArgumentException("A route destination cannot be null.", nameof(destinations));
            PendingTravel = null; SetTarget(null, now); route = copy; routeIndex = 0; routeArrived = false; routeDwell = 0;
            paused = false; Held = false; RouteTestResult = "Running " + copy.Length + " marked destinations";
            State = AutonomyState.RouteTest; Reason = RouteTestResult;
        }
        public void StopRouteTest()
        {
            if (route == null) return;
            if (PendingTravel?.Kind == AutonomyTravelKind.RouteTest) PendingTravel = null;
            if (ActiveTravel?.Kind == AutonomyTravelKind.RouteTest) ActiveTravel = null;
            EndRoute("Solo route stopped");
        }
        private void TickRoute(double now, double footage, bool ready)
        {
            ScenicRegion destination = route![routeIndex];
            State = AutonomyState.RouteTest; Reason = "Solo route " + (routeIndex + 1) + "/" + route.Count + ": " + destination.Name;
            if (routeArrived)
            {
                if (ready) routeDwell += footage;
                if (routeDwell >= 3)
                {
                    routeIndex++; routeArrived = false;
                    if (routeIndex == route.Count) EndRoute("Passed " + route.Count + " marked destinations with ready scenic footage");
                }
                else if (now - routeArrivalAt >= AcquisitionSeconds) EndRoute("Failed scenic readiness at " + destination.Name);
                return;
            }
            if (now - lastTravelAt < MinimumTravelSeconds) { Reason += " (travel cooldown)"; return; }
            Request(AutonomyTravelKind.RouteTest, destination.Id, destination.Name, destination.Position, now);
        }
        private void EndRoute(string result)
        { route = null; routeArrived = false; routeDwell = 0; RouteTestResult = result; idleActive = false; Reason = result; }

        public double RetryAfterSeconds(double now)
        {
            Time(now, nameof(now)); double earliest = double.PositiveInfinity;
            foreach (Failure failure in failures.Values) if (failure.Until > now) earliest = Math.Min(earliest, failure.Until);
            return double.IsPositiveInfinity(earliest) ? 0 : earliest - now;
        }
        public void Reset()
        {
            SetEnabled(false); PendingTravel = ActiveTravel = null; visited.Clear(); failures.Clear();
            targetId = lastPlayer = requestedSelection = null; requestedStep = 0;
            Held = paused = started = idleActive = false; idleUnreadyBegan = -1; idleDwell = dwell = 0;
            acquisitionTrips = 0; lastPlayerDestination = null; lastTravelAt = double.NegativeInfinity;
            CurrentRegionId = CurrentRegionName = TargetName = LastFailure = null;
            CompletedTravels = FailedTravels = 0; route = null; routeIndex = 0; RouteTestResult = "No solo route run";
            State = AutonomyState.Disabled; Reason = "Automatic direction disabled";
            // Do not reset token: callbacks from a previous world must never match a new request.
        }

        private void SetTarget(AutonomyPlayer? player, double now)
        {
            string? id = player?.Id;
            if (string.Equals(targetId, id, StringComparison.Ordinal)) return;
            targetId = id; TargetName = player?.Name; acquisitionBegan = now; dwell = 0; acquisitionTrips = 0; lastPlayerDestination = null;
            Revision++;
            if (id != null) { lastPlayer = id; State = AutonomyState.Acquiring; Reason = "Acquiring " + player!.Name; }
        }
        private static AutonomyPlayer? Find(IReadOnlyList<AutonomyPlayer> players, string? id)
        {
            if (id == null) return null;
            foreach (AutonomyPlayer player in players) if (player.Id == id) return player;
            return null;
        }
        private AutonomyPlayer? ChoosePlayer(IReadOnlyList<AutonomyPlayer> players, double now, string? after, int direction)
        {
            AutonomyPlayer? first = null, next = null;
            foreach (AutonomyPlayer player in players)
            {
                if ((!player.SceneReady && !player.HasFreshPosition(now)) || Blocked("p:" + player.Id, now)) continue;
                if (first == null || StringComparer.Ordinal.Compare(player.Id, first.Id) * direction < 0) first = player;
                if (after == null || StringComparer.Ordinal.Compare(player.Id, after) * direction > 0)
                    if (next == null || StringComparer.Ordinal.Compare(player.Id, next.Id) * direction < 0) next = player;
            }
            return next ?? first;
        }
        private void Request(AutonomyTravelKind kind, string id, string name, AutonomyPosition position, double now)
        {
            PendingTravel = new AutonomyTravelRequest(++token, kind, id, name, position); pendingAt = now;
            State = AutonomyState.Acquiring; Reason = "Preparing travel to " + name;
        }
        private bool Blocked(string id, double now) => failures.TryGetValue(id, out Failure failure) && now < failure.Until;
        private void Fail(string id, double now, string reason)
        {
            if (!failures.TryGetValue(id, out Failure failure))
            {
                if (failures.Count >= HistoryLimit) RemoveOldestFailure();
                failure = new Failure(); failures[id] = failure;
            }
            failure.Attempts = Math.Min(failure.Attempts + 1, 6); failure.Last = now;
            failure.Until = now + Math.Min(600, RetrySeconds * Math.Pow(2, failure.Attempts - 1)); LastFailure = reason;
        }
        private void Remember(string id, double now)
        {
            if (visited.Count >= HistoryLimit && !visited.ContainsKey(id))
            {
                string? oldest = null; double age = double.PositiveInfinity;
                foreach (var item in visited) if (now - item.Value >= RevisitSeconds && item.Value < age) { oldest = item.Key; age = item.Value; }
                if (oldest == null) return;
                visited.Remove(oldest);
            }
            visited[id] = now;
        }
        private bool HasVisitCapacity(double now)
        {
            if (visited.Count < HistoryLimit) return true;
            foreach (double last in visited.Values) if (now - last >= RevisitSeconds) return true;
            return false;
        }
        private void RemoveOldestFailure()
        {
            string? oldest = null; double age = double.PositiveInfinity;
            foreach (var item in failures) if (item.Value.Last < age) { oldest = item.Key; age = item.Value.Last; }
            if (oldest != null) failures.Remove(oldest);
        }
        private void CheckCallbackTime(double now)
        { Time(now, nameof(now)); if (started && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now)); }
        internal static string Label(string value, string name)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Expected a nonempty label of at most 128 characters.", name); return value; }
        internal static void Time(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
        private static double Positive(double value, string name)
        { Time(value, name); if (value == 0) throw new ArgumentOutOfRangeException(name); return value; }
    }
}
