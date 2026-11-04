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
        public double? CameraDisplacement { get; }
        public string ReadinessReason { get; }
        public AutonomyPlayer(string id, string name, AutonomyPosition? position, double positionFreshUntil, bool sceneReady,
            double? cameraDisplacement = null, string readinessReason = "Scene not ready")
        {
            Id = AutonomousDirector.Label(id, nameof(id)); Name = AutonomousDirector.Label(name, nameof(name));
            AutonomousDirector.Time(positionFreshUntil, nameof(positionFreshUntil));
            Position = position; PositionFreshUntil = positionFreshUntil; SceneReady = sceneReady;
            if (cameraDisplacement.HasValue) AutonomousDirector.Time(cameraDisplacement.Value, nameof(cameraDisplacement));
            CameraDisplacement = cameraDisplacement;
            ReadinessReason = AutonomousDirector.Label(readinessReason, nameof(readinessReason));
        }
        public bool HasFreshPosition(double now) => Position.HasValue && now < PositionFreshUntil;
        public bool NeedsTravel(AutonomyPosition current, double minimumDistance) => CameraDisplacement.HasValue
            ? CameraDisplacement.Value >= CameraReachability.ImmediateLimit
            : Position.HasValue && current.Distance(Position.Value) >= minimumDistance;
    }

    /// <summary>A previously generated, read-only destination supplied by the companion or an explicit solo route.</summary>
    public sealed class ScenicRegion
    {
        public string Id { get; }
        public string Name { get; }
        public AutonomyPosition Position { get; }
        public float Preference { get; }
        public double AvailableAfter { get; }
        public string Biome { get; }
        public ScenicLandscape Landscape { get; }
        public ScenicDestinationKind Kind { get; }
        public string FamilyId { get; }
        public ScenicRegion(string id, string name, AutonomyPosition position, float preference = 0, double availableAfter = 0,
            string biome = "", ScenicLandscape landscape = ScenicLandscape.Unknown, ScenicDestinationKind kind = ScenicDestinationKind.Landscape, string? familyId = null)
        {
            Id = AutonomousDirector.Label(id, nameof(id)); Name = AutonomousDirector.Label(name, nameof(name)); Position = position;
            WorldInterestProtocol.Number(preference, 100); AutonomousDirector.Time(availableAfter, nameof(availableAfter));
            Preference = preference; AvailableAfter = availableAfter;
            if (!Enum.IsDefined(typeof(ScenicLandscape), landscape) || !Enum.IsDefined(typeof(ScenicDestinationKind), kind)) throw new ArgumentException("Invalid scenic category.");
            Biome = ScenicDestinationDiversity.BiomeName(biome.Length == 0 && kind == ScenicDestinationKind.Landscape ? name : biome);
            Landscape = landscape; Kind = kind; FamilyId = AutonomousDirector.Label(familyId ?? id, nameof(familyId));
        }
    }

    public enum AutonomyState { Disabled, Following, Acquiring, Scenic, Travelling, Waiting, Paused, RouteTest, WildlifeCutaway }
    public enum AutonomyTravelKind { Player, Scenic, RouteTest }
    public sealed class AutonomyTravelRequest
    {
        public long Token { get; }
        public AutonomyTravelKind Kind { get; }
        public string TargetId { get; }
        public string Name { get; }
        public AutonomyPosition Position { get; }
        public string Reason { get; }
        internal AutonomyTravelRequest(long token, AutonomyTravelKind kind, string targetId, string name, AutonomyPosition position, string reason)
        { Token = token; Kind = kind; TargetId = targetId; Name = name; Position = position; Reason = reason; }
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
        private readonly PlayerCoverageHistory coverage = new PlayerCoverageHistory();
        private readonly PlayerActivitySelection activitySelection = new PlayerActivitySelection();
        private readonly PlayerPictureRecovery pictureRecovery = new PlayerPictureRecovery();
        private readonly ScenicDestinationDiversity scenicDiversity = new ScenicDestinationDiversity();
        private readonly ScenicAppearanceHistory scenicAppearanceHistory = new ScenicAppearanceHistory();
        private readonly ScenicRelocationWindow scenicRelocation = new ScenicRelocationWindow();
        private ScenicAppearanceKey? scenicAppearance;
        private readonly List<ScenicRegion> scenicCandidates = new List<ScenicRegion>(1024);
        private ScenicRegion? pendingScenic;
        private readonly List<string> activityRoster = new List<string>(128);
        private string? targetId, lastPlayer, requestedSelection;
        private int requestedStep;
        private bool paused, started, idleActive;
        private double lastNow, acquisitionBegan, dwell, idleDwell, idleUnreadyBegan = -1;
        private double pendingAt, lastTravelAt = double.NegativeInfinity;
        private long token;
        private int acquisitionTrips;
        private bool hasPresentedTarget;
        private double relatedViewBegan = -1;
        private double shotLostAt = -1, outOfReachSince = -1;
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
        public bool PresentationGraceActive { get; private set; }
        public bool WildlifeCutawayActive { get; private set; }
        public string ActivityReason => activitySelection.Reason;
        public string ScenicSelectionReason => scenicDiversity.Reason;
        public string CurrentScenicDescription => scenicDiversity.CurrentDescription;
        public int SustainedScenicVisits => scenicDiversity.CompletedVisits;
        public bool ScenicDepartureWaiting => scenicRelocation.Waiting;
        public bool CanStartWildlifeCutaway => Enabled && !paused && !Held && State == AutonomyState.Following &&
            hasPresentedTarget && targetId != null && PendingTravel == null && ActiveTravel == null && route == null &&
            requestedSelection == null && requestedStep == 0;
        public double ReacquisitionGraceSeconds { get; set; } = 3;
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
            coverage.Reset(); activitySelection.Reset(); pictureRecovery.Reset();
            scenicDiversity.StopVisit(); scenicRelocation.Reset(); pendingScenic = null;
            WildlifeCutawayActive = false;
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
            scenicRelocation.Reset();
            pictureRecovery.ResetCurrent();
            WildlifeCutawayActive = false;
            PresentationGraceActive = false; shotLostAt = outOfReachSince = -1;
            if (value) { scenicDiversity.StopVisit(); pendingScenic = null; PendingTravel = ActiveTravel = null; State = AutonomyState.Paused; Reason = "Automatic direction paused"; }
            else { acquisitionBegan = lastNow; idleUnreadyBegan = -1; }
        }
        public void SetHolding(bool value)
        {
            if (Held != value) { activitySelection.ResetRetention(); pictureRecovery.ResetCurrent(); scenicRelocation.Reset(); acquisitionBegan = lastNow; }
            Held = value;
        }
        public void Next(int direction = 1)
        {
            if (direction != 1 && direction != -1) throw new ArgumentOutOfRangeException(nameof(direction));
            requestedStep = direction; requestedSelection = null;
        }
        public void Select(string id) { requestedSelection = Label(id, nameof(id)); requestedStep = 0; }
        /// <summary>Measured local appearance informs only comparable, already
        /// eligible destinations; it cannot override source variety or cooldowns.</summary>
        public void SetScenicAppearance(ScenicAppearanceKey appearance) => scenicAppearance = appearance;

        /// <summary>Report readable, rendered player IDs; discovery/group membership is insufficient.</summary>
        public void RecordCoverage(double now, double dt, IReadOnlyList<string> visibleIds)
        {
            if (Enabled && !paused)
            {
                coverage.Record(now, dt, visibleIds);
                foreach (string id in visibleIds)
                    if (coverage.RecentlyCovered(id, now, 1)) activitySelection.MarkCovered(id, now);
            }
        }

        public void Tick(double now, double dt, IReadOnlyList<AutonomyPlayer> players, IReadOnlyList<ScenicRegion> regions,
            AutonomyPosition currentPosition, string? presentedPlayerId, bool idleShotReady, bool travelBusy = false,
            bool wildlifeCutaway = false, IReadOnlyList<PlayerActivityEvidence>? activity = null, bool usablePlayerFootage = true,
            bool? idleSceneAvailable = null, bool idleSearchExpired = false, bool? scenicFootageReady = null, double? scenicFootageDelta = null,
            bool scenicContentExhausted = false, bool relatedPlayerView = false, bool scenicJourneyActive = false, bool scenicPreparationActive = false,
            bool scenicEditBoundary = true)
        {
            Time(now, nameof(now)); Time(dt, nameof(dt));
            if (scenicFootageDelta.HasValue) Time(scenicFootageDelta.Value, nameof(scenicFootageDelta));
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (players.Count > 128 || regions.Count > 1024) throw new ArgumentException("Discovery inputs exceed the bounded policy capacity.");
            if (started && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Reset the director before changing clocks/worlds.");
            Positive(MinimumPlayerTravelDistance, nameof(MinimumPlayerTravelDistance));
            Positive(ReacquisitionGraceSeconds, nameof(ReacquisitionGraceSeconds));
            Positive(MinimumScenicTravelDistance, nameof(MinimumScenicTravelDistance));
            Positive(MaximumScenicTravelDistance, nameof(MaximumScenicTravelDistance));
            if (MaximumScenicTravelDistance < MinimumScenicTravelDistance) throw new ArgumentOutOfRangeException(nameof(MaximumScenicTravelDistance));
            Positive(UnreadyScenicSeconds, nameof(UnreadyScenicSeconds)); Positive(RequestAcceptanceSeconds, nameof(RequestAcceptanceSeconds));
            lastNow = now;
            if (!Enabled) { State = AutonomyState.Disabled; return; }
            if (!started) { started = true; acquisitionBegan = now; }
            if (paused) { State = AutonomyState.Paused; Reason = "Automatic direction paused"; return; }
            activityRoster.Clear();
            foreach (AutonomyPlayer player in players) activityRoster.Add(player.Id);
            activitySelection.Update(now, activityRoster, activity);
            // An intentional editorial cutaway is not a missing player shot. It
            // never substitutes for transport/route state or an operator command.
            if (WildlifeCutawayActive && (!wildlifeCutaway || Held || requestedSelection != null || requestedStep != 0 ||
                ActiveTravel != null || PendingTravel != null || travelBusy || route != null || Find(players, targetId) == null))
                EndWildlifeCutaway(now);
            if (ActiveTravel != null || travelBusy)
            {
                scenicRelocation.Reset();
                pictureRecovery.ResetCurrent();
                State = AutonomyState.Travelling;
                Reason = ActiveTravel == null ? "Waiting for controlled travel to finish" : "Travelling to " + ActiveTravel.Name;
                return;
            }
            if (PendingTravel != null)
            {
                scenicRelocation.Reset();
                pictureRecovery.ResetCurrent();
                if (now - pendingAt >= RequestAcceptanceSeconds)
                    TravelCompleted(PendingTravel.Token, false, now, "Travel request was not accepted before its deadline");
                else { State = AutonomyState.Acquiring; Reason = "Preparing travel to " + PendingTravel.Name; }
                return;
            }
            // A stall cannot count hours of unseen footage as target dwell.
            double footage = Math.Min(dt, 1);
            if (route != null) { scenicRelocation.Reset(); TickRoute(now, footage, idleShotReady); return; }

            AutonomyPlayer? selected = Find(players, targetId);
            if (wildlifeCutaway && selected != null && (WildlifeCutawayActive || CanStartWildlifeCutaway))
            {
                pictureRecovery.ResetCurrent();
                scenicDiversity.StopVisit();
                WildlifeCutawayActive = true;
                scenicRelocation.Reset();
                TargetName = selected.Name;
                // Rebase only acquisition clocks. Preserve target dwell and real
                // viewing credit; do not claim that the player is still rendered.
                acquisitionBegan = now; shotLostAt = outOfReachSince = -1;
                PresentationGraceActive = false;
                State = AutonomyState.WildlifeCutaway;
                Reason = "Wildlife cutaway; retaining " + selected.Name + " for return";
                return;
            }
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
            // Only a previously presented, still-loaded owner can be retained
            // during a bounded related-subject edit. The adapter supplies fresh
            // actual cutaway output AND a validated return view. No dwell,
            // player visibility, initial acquisition or travel credit is earned.
            if (!relatedPlayerView) relatedViewBegan = -1;
            if (relatedPlayerView && selected != null && hasPresentedTarget && selected.SceneReady &&
                selected.Id == presentedPlayerId && !Held && State == AutonomyState.Following)
            {
                if (relatedViewBegan < 0) relatedViewBegan = now;
                if (now - relatedViewBegan <= PlayerFollowContinuity.MaximumRelatedViewSeconds)
                {
                    Reason = "Complementary encounter view; retaining " + selected.Name + " for return";
                    pictureRecovery.ResetCurrent();
                    acquisitionBegan = now;
                    return;
                }
            }
            if (relatedPlayerView) { presentedPlayerId = null; usablePlayerFootage = false; }
            ObservePresentation(selected, now, presentedPlayerId);
            if (selected != null && !PresentationGraceActive && !selected.SceneReady && !selected.HasFreshPosition(now))
            {
                AutonomyPlayer? alternative = ChoosePlayer(players, now, selected.Id, 1);
                if (alternative != null) { selected = alternative; SetTarget(selected, now); ObservePresentation(selected, now, presentedPlayerId); }
            }
            if (selected != null && !(selected.SceneReady && (Held && hasPresentedTarget || string.Equals(presentedPlayerId, selected.Id, StringComparison.Ordinal))) &&
                now - acquisitionBegan >= AcquisitionSeconds)
            {
                // If every local camera option failed, retain the bounded
                // target retry policy. Loaded geometry does not justify an
                // endless slate, nor does it justify travel to this same actor.
                Fail("p:" + selected.Id, now, selected.SceneReady && hasPresentedTarget ?
                    "Could not restore a picture of locally loaded " + selected.Name + " before its deadline" :
                    "Could not acquire " + selected.Name + " before its deadline");
                lastPlayer = selected.Id; SetTarget(null, now); selected = null;
            }
            if (selected != null)
            {
                TargetName = selected.Name;
                if (selected.SceneReady)
                {
                    outOfReachSince = -1;
                    idleActive = false; scenicDiversity.StopVisit(); scenicRelocation.Reset();
                    bool presented = string.Equals(presentedPlayerId, selected.Id, StringComparison.Ordinal);
                    bool readablePicture = presented && usablePlayerFootage;
                    // Losing the entire camera picture is at least as serious
                    // as retaining an unreadable one. The established target is
                    // independent of this frame's presentation ID; do not erase
                    // its local-picture deadline merely because Apply failed.
                    // A never-presented or genuinely unloaded target still uses
                    // the separate acquisition/travel policy.
                    pictureRecovery.Observe(selected.Id, now, hasPresentedTarget && !Held, readablePicture);
                    if (!Held && !readablePicture && pictureRecovery.ShouldTryAlternative(now))
                    {
                        AutonomyPlayer? alternative = ChoosePlayer(players, now, selected.Id, 1, true, true);
                        if (alternative != null && alternative.Id != selected.Id)
                        {
                            pictureRecovery.LeftUnusable(selected.Id, now);
                            SetTarget(alternative, now);
                            Reason = "Trying another loaded player after sustained picture loss: " + alternative.Name;
                            return;
                        }
                    }
                    if (presented)
                    {
                        State = AutonomyState.Following; Reason = "Following " + selected.Name;
                        acquisitionBegan = now; acquisitionTrips = 0; lastPlayerDestination = null;
                        failures.Remove("p:" + selected.Id);
                        if (!Held && !usablePlayerFootage && pictureRecovery.ShouldTryAlternative(now))
                            Reason = "Seeking a clearer player view; no other local ready subject outside picture cooldown";
                        if (!Held && usablePlayerFootage) dwell = Math.Min(DwellSeconds, dwell + footage);
                        if (dwell >= DwellSeconds && !Held && usablePlayerFootage)
                        {
                            AutonomyPlayer? next = ChoosePlayer(players, now, selected.Id, 1, true);
                            if (next != null && next.Id != selected.Id)
                            {
                                if (activitySelection.RetainEncounter(selected.Id, now, usablePlayerFootage, activitySelection.Overdue(next.Id, now)))
                                    Reason = activitySelection.Reason + ": " + selected.Name;
                                else SetTarget(next, now);
                            }
                        }
                        return;
                    }
                    State = AutonomyState.Acquiring;
                    Reason = hasPresentedTarget ? "Recovering the missing camera picture of loaded player " + selected.Name :
                        "Waiting for a first valid camera shot of " + selected.Name;
                }
                else
                {
                    pictureRecovery.Observe(selected.Id, now, false, false);
                    State = AutonomyState.Acquiring;
                    bool needsTravel = selected.HasFreshPosition(now) && selected.NeedsTravel(currentPosition, MinimumPlayerTravelDistance);
                    if (!needsTravel) outOfReachSince = -1;
                    else if (outOfReachSince < 0) outOfReachSince = now;
                    bool travelConfirmed = !hasPresentedTarget || outOfReachSince >= 0 && now - outOfReachSince >= ReacquisitionGraceSeconds;
                    Reason = PresentationGraceActive ? "Brief shot interruption for " + selected.Name + ": " + selected.ReadinessReason :
                        !selected.HasFreshPosition(now) ? "Waiting for fresh coordinates for " + selected.Name :
                        !needsTravel ? "Waiting for local scene of " + selected.Name + ": " + selected.ReadinessReason :
                        "Acquiring " + selected.Name + ": " + selected.ReadinessReason;
                    if (!PresentationGraceActive && travelConfirmed && selected.Position.HasValue && selected.HasFreshPosition(now) && needsTravel &&
                        now - lastTravelAt >= MinimumTravelSeconds && acquisitionTrips < 3 &&
                        (!lastPlayerDestination.HasValue || lastPlayerDestination.Value.Distance(selected.Position.Value) >= 24))
                    {
                        Request(AutonomyTravelKind.Player, selected.Id, selected.Name, selected.Position.Value, now,
                            selected.ReadinessReason + "; carrier-to-target " + currentPosition.Distance(selected.Position.Value).ToString("F1") +
                            "m; required camera movement " + (selected.CameraDisplacement?.ToString("F1") ?? "unknown") +
                            "m; continuously out of reach " + (outOfReachSince < 0 ? 0 : now - outOfReachSince).ToString("F1") + "s; shot unavailable " + (shotLostAt < 0 ? 0 : now - shotLostAt).ToString("F1") + "s");
                        return;
                    }
                }
                if (PresentationGraceActive || selected.SceneReady || selected.HasFreshPosition(now)) return;
                // A connected player without a usable position does not freeze idle coverage.
            }
            TickScenery(now, footage, players.Count, regions, currentPosition, idleShotReady,
                idleSceneAvailable ?? idleShotReady, idleSearchExpired, scenicFootageReady ?? idleShotReady, scenicFootageDelta.HasValue ? Math.Min(footage, scenicFootageDelta.Value) : footage,
                scenicContentExhausted, scenicJourneyActive, scenicPreparationActive, scenicEditBoundary);
        }

        private void TickScenery(double now, double footage, int connected, IReadOnlyList<ScenicRegion> regions,
            AutonomyPosition currentPosition, bool ready, bool available, bool searchExpired, bool scenicPicture, double scenicFootage,
            bool contentExhausted, bool journeyActive, bool preparationActive, bool editBoundary)
        {
            // Useful footage advances dwell. A live picture recovering its
            // composition is not evidence that its loaded region is unavailable.
            available |= ready;
            if (!idleActive)
            {
                idleActive = true; idleDwell = 0; idleUnreadyBegan = available ? -1 : now; scenicDiversity.StopVisit(); scenicRelocation.Reset();
                // Record the arrival area so the first roaming destination cannot immediately revisit it.
                ScenicRegion? nearest = null; double nearestDistance = 64;
                foreach (ScenicRegion region in regions)
                {
                    double distance = currentPosition.HorizontalDistance(region.Position);
                    if (distance < nearestDistance) { nearest = region; nearestDistance = distance; }
                }
                if (nearest != null)
                {
                    CurrentRegionId = nearest.Id; CurrentRegionName = nearest.Name; Remember(nearest.Id, now);
                    // Proximity cannot prove that an initial base/landmark is
                    // framed; explicit visits carry the adapter's subject gate.
                    if (nearest.Kind == ScenicDestinationKind.Landscape) scenicDiversity.Begin(nearest);
                }
            }
            State = available ? AutonomyState.Scenic : AutonomyState.Waiting;
            Reason = connected > 0 ? "Scenery while waiting for an available player" : ready ? "Exploring the current scenic region" :
                available ? "Retaining the loaded scenic region while local composition recovers" : "Waiting for a valid scenic shot";
            if (!ready && searchExpired)
            { State = AutonomyState.Waiting; Reason = "No readable scenic output within the local recovery budget"; }
            if (available) idleUnreadyBegan = -1;
            else if (idleUnreadyBegan < 0) idleUnreadyBegan = now;
            scenicDiversity.Observe(scenicFootage, scenicPicture);
            if (scenicAppearance.HasValue && scenicDiversity.CurrentId != null)
                scenicAppearanceHistory.Observe(scenicDiversity.CurrentId, scenicAppearance.Value, scenicFootage, scenicPicture);
            if (Held) { scenicRelocation.Reset(); Reason += " (held)"; return; }
            if (ready) idleDwell += footage;
            // A loaded region can still fail to produce a picture. The client
            // reports a separate, actual-output recovery deadline; do not add
            // another scene-loading timeout after that deadline has elapsed.
            bool exhausted = !ready && searchExpired || idleUnreadyBegan >= 0 && now - idleUnreadyBegan >= UnreadyScenicSeconds;
            // Editorial exhaustion can request fresh scenery while a validated
            // live picture stays on air. It is neither missing world data nor
            // an unavailable picture, and never overrides player/hold control.
            bool seekFreshContent = ready && contentExhausted;
            // A validated finite journey or explicit camera fixture owns its
            // local editorial time. It cannot suppress player acquisition above
            // or the missing-picture/scene exhaustion evaluated here.
            if (preparationActive)
            { scenicRelocation.Reset(); Reason = "Preparing the explicitly requested camera fixture within its bounded acquisition deadline"; return; }
            if (journeyActive && ready && !exhausted)
            { scenicRelocation.Reset(); Reason = "Following a continuous scenic journey or camera fixture"; return; }
            if (idleDwell < RegionSeconds && !exhausted && !seekFreshContent) { scenicRelocation.Reset(); return; }
            if (!ready || exhausted || seekFreshContent) scenicRelocation.Reset();
            if (now - lastTravelAt < MinimumTravelSeconds) { Reason = "Waiting for the travel cooldown"; return; }
            scenicCandidates.Clear();
            foreach (ScenicRegion region in regions)
            {
                double distance = currentPosition.HorizontalDistance(region.Position);
                if (region.Id == CurrentRegionId || now < region.AvailableAfter || distance < MinimumScenicTravelDistance || distance > MaximumScenicTravelDistance || Blocked("s:" + region.Id, now)) continue;
                if (visited.TryGetValue(region.Id, out double lastVisit) && now - lastVisit < RevisitSeconds) continue;
                scenicCandidates.Add(region);
            }
            ScenicRegion? chosen = scenicDiversity.Choose(scenicCandidates, currentPosition,
                id => visited.TryGetValue(id, out double lastVisit) ? lastVisit : double.NegativeInfinity,
                scenicAppearance.HasValue ? id => scenicAppearanceHistory.Preference(id, scenicAppearance.Value) : (Func<string, double>?)null);
            if (chosen == null)
            {
                scenicRelocation.Reset();
                Reason = (ready ? "Retaining the readable scenic view. " : "") +
                    (regions.Count == 0 ? "Waiting for scenic destinations from the server" : "No fresh scenic destination available; distance, revisit and feedback cooldowns remain active");
                return;
            }
            if (!visited.ContainsKey(chosen.Id) && !HasVisitCapacity(now))
            { scenicRelocation.Reset(); Reason = "Scenic history is full; waiting for a revisit cooldown before recording another region"; return; }
            // Only normal dwell rotation waits for an edit point. A missing or
            // exhausted picture must not inherit this additional delay, and
            // no pending travel token is created while a valid shot develops.
            if (ready && !exhausted && !seekFreshContent && !scenicRelocation.CanDepart(now, editBoundary))
            {
                Reason = "Scenic region dwell completed; finishing the current shot before relocation (maximum 15s extension)";
                return;
            }
            scenicRelocation.Reset();
            pendingScenic = chosen; scenicDiversity.Attempt(chosen);
            Request(AutonomyTravelKind.Scenic, chosen.Id, chosen.Name, chosen.Position, now,
                !ready && searchExpired ? "Scenic picture-recovery budget exhausted" :
                exhausted ? "Scenic scene-readiness deadline exhausted" :
                seekFreshContent ? "Local scenic coverage exhausted; seeking fresh subject" : "Scenic footage dwell completed");
        }

        public void TravelStarted(long requestToken, double now)
        {
            CheckCallbackTime(now);
            if (PendingTravel == null || PendingTravel.Token != requestToken || ActiveTravel != null) return;
            scenicRelocation.Reset();
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
                pendingScenic = null;
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
                if (pendingScenic != null && pendingScenic.Id == request.TargetId) scenicDiversity.Begin(pendingScenic);
                pendingScenic = null;
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
            scenicDiversity.StopVisit(); pendingScenic = null;
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
            scenicDiversity.Reset(); scenicRelocation.Reset(); scenicCandidates.Clear(); pendingScenic = null;
            scenicAppearanceHistory.Reset(); scenicAppearance = null;
            coverage.Reset(); activitySelection.Reset(); pictureRecovery.Reset();
            targetId = lastPlayer = requestedSelection = null; requestedStep = 0;
            Held = paused = started = idleActive = false; idleUnreadyBegan = -1; idleDwell = dwell = 0;
            acquisitionTrips = 0; lastPlayerDestination = null; lastTravelAt = double.NegativeInfinity;
            hasPresentedTarget = PresentationGraceActive = false; shotLostAt = outOfReachSince = -1;
            relatedViewBegan = -1;
            WildlifeCutawayActive = false;
            CurrentRegionId = CurrentRegionName = TargetName = LastFailure = null;
            CompletedTravels = FailedTravels = 0; route = null; routeIndex = 0; RouteTestResult = "No solo route run";
            State = AutonomyState.Disabled; Reason = "Automatic direction disabled";
            // Do not reset token: callbacks from a previous world must never match a new request.
        }

        private void SetTarget(AutonomyPlayer? player, double now)
        {
            string? id = player?.Id;
            if (string.Equals(targetId, id, StringComparison.Ordinal)) return;
            if (id != null) scenicRelocation.Reset();
            relatedViewBegan = -1;
            activitySelection.ResetRetention(); pictureRecovery.ResetCurrent();
            targetId = id; TargetName = player?.Name; acquisitionBegan = now; dwell = 0; acquisitionTrips = 0; lastPlayerDestination = null;
            hasPresentedTarget = PresentationGraceActive = false; shotLostAt = outOfReachSince = -1;
            WildlifeCutawayActive = false;
            Revision++;
            if (id != null) { lastPlayer = id; State = AutonomyState.Acquiring; Reason = "Acquiring " + player!.Name; }
        }
        private void EndWildlifeCutaway(double now)
        {
            pictureRecovery.ResetCurrent();
            WildlifeCutawayActive = false;
            acquisitionBegan = now; shotLostAt = outOfReachSince = -1;
            PresentationGraceActive = false;
        }
        private static AutonomyPlayer? Find(IReadOnlyList<AutonomyPlayer> players, string? id)
        {
            if (id == null) return null;
            foreach (AutonomyPlayer player in players) if (player.Id == id) return player;
            return null;
        }
        private AutonomyPlayer? ChoosePlayer(IReadOnlyList<AutonomyPlayer> players, double now, string? after, int direction, bool respectCoverage = false, bool localPictureRecovery = false)
        {
            AutonomyPlayer? first = null, next = null;
            foreach (AutonomyPlayer player in players)
            {
                if ((!player.SceneReady && !player.HasFreshPosition(now)) || Blocked("p:" + player.Id, now)) continue;
                if (localPictureRecovery && !player.SceneReady || respectCoverage && pictureRecovery.OnCooldown(player.Id, now)) continue;
                if (respectCoverage && (player.Id == after || !localPictureRecovery && coverage.RecentlyCovered(player.Id, now, DwellSeconds))) continue;
                if (first == null || StringComparer.Ordinal.Compare(player.Id, first.Id) * direction < 0) first = player;
                if (after == null || StringComparer.Ordinal.Compare(player.Id, after) * direction > 0)
                    if (next == null || StringComparer.Ordinal.Compare(player.Id, next.Id) * direction < 0) next = player;
            }
            AutonomyPlayer? fallback = next ?? first;
            if (!respectCoverage || fallback == null) return fallback;
            long scene = activitySelection.SceneId(after, now);
            double rank = activitySelection.Rank(fallback.Id, now, fallback.SceneReady, scene);
            foreach (AutonomyPlayer player in players)
            {
                if (player.Id == after || (!player.SceneReady && !player.HasFreshPosition(now)) || Blocked("p:" + player.Id, now) ||
                    !localPictureRecovery && coverage.RecentlyCovered(player.Id, now, DwellSeconds)) continue;
                if (localPictureRecovery && !player.SceneReady || respectCoverage && pictureRecovery.OnCooldown(player.Id, now)) continue;
                double candidate = activitySelection.Rank(player.Id, now, player.SceneReady, scene);
                if (candidate > rank + .0001) { fallback = player; rank = candidate; }
            }
            return fallback;
        }
        private void ObservePresentation(AutonomyPlayer? player, double now, string? presentedId)
        {
            PresentationGraceActive = false;
            if (player == null) return;
            if (player.SceneReady && string.Equals(player.Id, presentedId, StringComparison.Ordinal))
            { hasPresentedTarget = true; shotLostAt = outOfReachSince = -1; return; }
            if (shotLostAt < 0) shotLostAt = now;
            PresentationGraceActive = hasPresentedTarget && now - shotLostAt < ReacquisitionGraceSeconds;
        }

        private void Request(AutonomyTravelKind kind, string id, string name, AutonomyPosition position, double now, string? reason = null)
        {
            if (kind != AutonomyTravelKind.Scenic) { scenicDiversity.StopVisit(); pendingScenic = null; }
            PendingTravel = new AutonomyTravelRequest(++token, kind, id, name, position, reason ?? (kind + " destination selected")); pendingAt = now;
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
