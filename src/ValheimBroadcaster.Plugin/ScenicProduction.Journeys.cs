using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicProduction
{
    private readonly JourneyGuideWorld guideWorld = new JourneyGuideWorld();
    private readonly List<ScenicRegion> journeyRegions = new List<ScenicRegion>(1024);
    private readonly Dictionary<string, WorldInterestSite> journeySites = new Dictionary<string, WorldInterestSite>(StringComparer.Ordinal);
    private readonly Queue<string> recentJourneyDestinations = new Queue<string>();
    private readonly DestinationSubjectCoverage arrivalSubject = new DestinationSubjectCoverage();
    private readonly ScenicFeatureSurvey arrivalSurvey = new ScenicFeatureSurvey();
    private JourneyGuide? guide;
    private bool requestedJourney, authorized, watching, heldJourney, pendingPose, arrivalStarted, arrivalSurveyStarted;
    private long journeyWorld, authorityWorld;
    private float nextJourneyAt, requestDeadline, catalogueTime = float.NegativeInfinity, nextSelection, nextTrace;
    private float noAdvance, authorityWait, nextArrivalAim;
    private double observedMetres;
    private double lastGuideCaptureTime;
    private bool MissingGuideRender => Time.unscaledTimeAsDouble - lastGuideCaptureTime > .5;
    private string GuideWork => heldJourney ? "Operator hold" : MissingGuideRender ?
        "Waiting for presentation of guide pose" : guide?.Status ?? "Selecting destination";
    private int lastGuideFrame = -1;
    private Vector3 gaze, arrivalAim;
    private string journeyStatus = "No Journey requested";

    internal long JourneyEndRevision { get; private set; }
    internal ScenicRegion? JourneyArrivedDestination { get; private set; }
    internal WorldInterestSite? JourneyArrivedSite { get; private set; }
    internal bool JourneyActive => guide != null;
    internal bool JourneyOwnsTravel => JourneyActive || requestedJourney;
    internal bool ManualJourneyPending => requestedJourney;
    internal long JourneyGuideId => guide == null ? 0 : journey.Id;
    internal float JourneyGuideProgress => guide == null ? 0 : Mathf.Clamp01((float)(journey.Itinerary!.ProjectProgress(guide.Position) / journey.TargetMetres));
    internal Vector3? JourneyLoadingPoint => guide == null || heldJourney ? null :
        FromCore(guide.Position) + FromCore(guide.Forward) * 22f;
    internal string JourneyStatus => journeyStatus + (journey.Itinerary == null ? "" :
        $"; {journey.Itinerary.Destination.Name}; rendered {journey.TravelMetres:F1}/{journey.TargetMetres:F1}m; cruise {guide?.Speed ?? 0:F1}/{journey.CruiseSpeed:F1}m/s; arrival shown {journey.ArrivalShownSeconds:F1}s");

    private static AutonomyPosition ToCore(Vector3 point) => new AutonomyPosition(point.x, point.y, point.z);

    internal string RequestJourney()
    {
        if (JourneyActive) return JourneyStatus;
        if (FixtureActive) StopFixture();
        requestedJourney = true; requestDeadline = Time.unscaledTime + 90; nextSelection = nextJourneyAt = 0;
        return journeyStatus = "Journey requested; selecting a known destination";
    }

    internal void StopJourney()
    {
        if (JourneyOwnsTravel) FinishJourney("Journey stopped by operator", ScenicJourneyOutcome.Interrupted);
    }

    internal void SetJourneyCatalogue(long world, IReadOnlyList<ScenicRegion> regions,
        IReadOnlyDictionary<string, WorldInterestSite> sites, bool fresh)
    {
        if (world != journeyWorld)
        {
            if (JourneyActive) FinishJourney("Journey world changed", ScenicJourneyOutcome.Interrupted);
            recentJourneyDestinations.Clear(); journeyWorld = world; nextSelection = 0;
        }
        journeyRegions.Clear(); journeySites.Clear(); catalogueTime = float.NegativeInfinity;
        if (!fresh || world == 0) return;
        foreach (ScenicRegion region in regions) { if (journeyRegions.Count == 1024) break; journeyRegions.Add(region); }
        foreach (var entry in sites) journeySites[entry.Key] = entry.Value;
        catalogueTime = Time.unscaledTime;
    }

    internal void UpdateJourneyAuthority(long world, bool allowed, bool canWatch, bool held, bool preempted)
    {
        authorized = allowed; watching = canWatch; heldJourney = held; authorityWorld = world;
        if (!JourneyActive) return;
        if (world == 0 || world != journeyWorld || !allowed || preempted)
        {
            FinishJourney(preempted ? "Player/operator coverage preempted Journey" : "Journey lost its authorized world session",
                ScenicJourneyOutcome.Interrupted);
            return;
        }
        if (held) return;
        float elapsed = Mathf.Max(0, Time.unscaledDeltaTime);
        authorityWait = canWatch && Player.m_localPlayer ? 0 : authorityWait + elapsed;
        if (authorityWait >= 30) { FinishJourney("Observer remained unavailable for 30 seconds", ScenicJourneyOutcome.Blocked); return; }
        if (authorityWait > 0) return;
        if (journey.TravelMetres - observedMetres >= .25) { observedMetres = journey.TravelMetres; noAdvance = 0; }
        else noAdvance += elapsed;
        if (noAdvance >= 35)
            FinishJourney("No rendered travel progress for 35s: " + GuideWork, ScenicJourneyOutcome.Blocked);
    }

    private bool TickJourney(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate, out bool changed)
    {
        changed = false;
        if (!moveCarrier || !settings.ProceduralPlayers.Value || !settings.DynamicShots.Value)
        {
            if (JourneyOwnsTravel) FinishJourney("Journey requires an active observer and moving procedural cameras", ScenicJourneyOutcome.Interrupted);
            return false;
        }
        if (guide != null)
        {
            if (journey.TargetReached || !journey.Active)
            {
                FinishJourney(journey.TargetReached ? "Arrived at " + journey.Itinerary!.Destination.Name : journey.Reason,
                    journey.TargetReached ? ScenicJourneyOutcome.Completed : journey.Outcome);
                changed = true; return false;
            }
            if (!held && journey.ShownSeconds >= journey.MaximumSeconds)
            {
                FinishJourney("Journey exhausted its on-air travel time", ScenicJourneyOutcome.BudgetExpired);
                changed = true; return false;
            }
            if (Time.unscaledTime >= nextTrace)
            {
                journeyStatus = GuideWork;
                TraceJourney("progress"); nextTrace = Time.unscaledTime + 1;
            }
            return true;
        }
        if (held)
        {
            if (requestedJourney) requestDeadline += Mathf.Max(0, Time.unscaledDeltaTime);
            return false;
        }
        if (requestedJourney && Time.unscaledTime >= requestDeadline)
        {
            FinishJourney("Destination selection expired: " + journeyStatus, ScenicJourneyOutcome.Blocked);
            return false;
        }
        if (!requestedJourney && (!settings.ScenicJourneys.Value || Time.unscaledTime < nextJourneyAt ||
            director.RememberedShots < 2) || !allowActivate || FixtureActive) return false;
        if (!authorized || !watching || authorityWorld == 0 || journeyWorld != authorityWorld || !Player.m_localPlayer)
        { journeyStatus = "Waiting for authorized observer session"; return false; }
        if (Time.unscaledTime - catalogueTime > 5 || journeyRegions.Count == 0)
        { journeyStatus = "Waiting for fresh scenic destination catalogue"; return false; }
        if (Time.unscaledTime < nextSelection) return false;
        if (!TryDeparturePose(out Vector3 position, out Vector3 look))
        { journeyStatus = "Waiting for loaded camera position"; return false; }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return false;
        ScenicJourneyItinerary? itinerary;
        using (work)
        {
            nextSelection = Time.unscaledTime + 5;
            Vector3 horizontal = Vector3.ProjectOnPlane(look, Vector3.up);
            if (horizontal.sqrMagnitude < .01f) horizontal = Vector3.forward;
            bool selected = ScenicJourneyItinerary.TrySelect(ToCore(position), ToCore(horizontal.normalized), journeyRegions, journeySites,
                settings.ScenicJourneyDistance.Value, Time.realtimeSinceStartupAsDouble, recentJourneyDestinations, out itinerary,
                out journeyStatus, out var evidence, allowReverseDeparture: true);
            if (ScenicTrace.Enabled)
                ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("journey-selection"), "origin", position)
                    .Field("requestedMetres", settings.ScenicJourneyDistance.Value).Field("selected", selected)
                    .Field("reason", journeyStatus).Field("evidence", evidence.Summary).Field("destination", itinerary?.Destination.Id ?? "none")
                    .Field("connectedMetadata", itinerary?.UsesConnectedObservations ?? false));
            if (!selected || itinerary == null) return false;
        }
        guideWorld.ResetDiagnostics();
        journey.Start(++nextId, itinerary, settings.ScenicJourneySpeed.Value);
        guide = new JourneyGuide(ToCore(position), itinerary.Destination.Position, journey.CruiseSpeed, itinerary.ArrivalRadius);
        Vector3 departureBearing = Vector3.ProjectOnPlane(FromCore(itinerary.SteeringGoal(ToCore(position))) - position, Vector3.up);
        if (departureBearing.sqrMagnitude < .01f) departureBearing = FromCore(guide.Forward);
        gaze = (departureBearing.normalized + Vector3.down * .075f).normalized;
        arrivalAim = FromCore(itinerary.ArrivalFocus);
        arrivalSubject.Set(itinerary.Site); arrivalSurvey.Reset(); arrivalSurveyStarted = arrivalStarted = false;
        nextArrivalAim = noAdvance = authorityWait = 0; observedMetres = 0; lastGuideFrame = -1;
        lastGuideCaptureTime = Time.unscaledTimeAsDouble;
        pendingPose = true; requestedJourney = false; JourneyArrivedDestination = null; JourneyArrivedSite = null;
        journeyStatus = "Continuous guide starting toward " + itinerary.Destination.Name;
        TraceJourney("departure-start"); nextTrace = Time.unscaledTime + 1;
        changed = true; return true;
    }

    internal bool TryGetJourneyGuidePose(float dt, bool held, out Vector3 position, out Vector3 focus)
    {
        position = focus = default;
        if (guide == null || !authorized || !watching || !Player.m_localPlayer) return false;
        ScenicJourneyItinerary itinerary = journey.Itinerary!;
        if (lastGuideFrame != Time.frameCount && !held && !heldJourney && !pendingPose)
        {
            // Propose a single frame from the position actually rendered last.
            // If rendering fails, retry exactly this pose until it appears.
            lastGuideFrame = Time.frameCount;
            guide.Step(Mathf.Clamp(dt, 0, .15f), itinerary.SteeringGoal(guide.Position), guideWorld);
            Vector3 steering = FromCore(guide.Forward);
            Vector3 heading = Vector3.ProjectOnPlane(steering, Vector3.up);
            if (heading.sqrMagnitude < .04f) heading = Vector3.ProjectOnPlane(gaze, Vector3.up);
            Vector3 desired = (heading.normalized + Vector3.up * Mathf.Clamp(steering.y * .45f - .075f, -.3f, .15f)).normalized;
            double distance = guide.Position.HorizontalDistance(itinerary.Destination.Position);
            if (distance < 100)
            {
                RefreshArrivalAim(itinerary);
                Vector3 aim = arrivalAim - FromCore(guide.Position);
                if (aim.sqrMagnitude > 1)
                {
                    float weight = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp((float)itinerary.ArrivalRadius + 4, 100, (float)distance));
                    desired = Vector3.Slerp(desired, aim.normalized, weight).normalized;
                }
            }
            gaze = Vector3.RotateTowards(gaze, desired, Mathf.Max(0, dt) * .85f, 0).normalized;
            pendingPose = true;
        }
        position = FromCore(guide.Position); focus = position + gaze * 32;
        return TargetRoster.Finite(position) && TargetRoster.Finite(focus);
    }

    private void RefreshArrivalAim(ScenicJourneyItinerary itinerary)
    {
        if (!arrivalSurveyStarted)
        { arrivalSurvey.Begin(FromCore(itinerary.Destination.Position)); arrivalSurveyStarted = true; }
        if (!arrivalSurvey.Complete) arrivalSurvey.Step();
        if (Time.unscaledTime < nextArrivalAim) return;
        nextArrivalAim = Time.unscaledTime + .5f;
        ScenicFeature? best = null;
        double nearest = double.PositiveInfinity;
        if (arrivalSurvey.Complete && itinerary.Site != null)
            foreach (ScenicFeature feature in arrivalSurvey.Features)
            {
                double distance = feature.Center.HorizontalDistance(itinerary.ArrivalFocus);
                if (distance <= 64 && distance < nearest && arrivalSubject.Meaningful(feature))
                { best = feature; nearest = distance; }
            }
        if (best != null) { arrivalAim = FromCore(best.Center); return; }
        Vector3 target = FromCore(itinerary.ArrivalFocus);
        if (guideWorld.TryGetSurface(itinerary.ArrivalFocus, out double surface))
            target.y = (float)surface + (itinerary.Site == null ? 2 : Mathf.Clamp(itinerary.Site.Extents.Y * .4f, 2, 10));
        arrivalAim = target;
    }

    private void ObserveGuidePicture(bool usable, long id, bool held, JourneyPresentationSample? capture)
    {
        if (guide == null) return;
        if (!usable || id != journey.Id || !capture.HasValue || capture.Value.Audit.Shot != id ||
            capture.Value.Audit.Status != CameraRenderAuditStatus.Matched ||
            capture.Value.Position.Distance(guide.Position) > .10)
        { journey.BreakPresentation(); return; }
        JourneyPresentationSample actual = capture.Value;
        RememberDepartureCapture(actual);
        lastGuideCaptureTime = actual.Time;
        pendingPose = false;
        journey.ObserveGuide(actual.Time, true, held, actual.Position);
        bool atGoal = actual.Position.HorizontalDistance(journey.Itinerary!.Destination.Position) <= journey.Itinerary.ArrivalRadius;
        if (atGoal && !arrivalStarted)
        { arrivalStarted = true; TraceJourney("arrival-start"); }
        Vector3 direction = arrivalAim - FromCore(actual.Position);
        // The actual rendered lens can include a steeply downward destination
        // even when the rig's restrained pitch keeps the horizon in frame.
        float withinFrame = Mathf.Cos(Mathf.Clamp((float)actual.Audit.Captured.Fov * .44f, 10, 35) * Mathf.Deg2Rad);
        bool framed = direction.sqrMagnitude > .01f &&
            Vector3.Dot(RenderedDirection(actual.Audit.Captured), direction.normalized) >= withinFrame;
        journey.ObserveArrival(actual.Time, true, held, actual.Position, atGoal && framed);
    }

    private void FinishJourney(string reason, ScenicJourneyOutcome outcome)
    {
        if (guide != null)
        {
            journey.Finish(outcome, reason); journeyStatus = journey.Reason;
            TraceJourney("finish");
            JourneyEndRevision++;
            ScenicJourneyItinerary itinerary = journey.Itinerary!;
            JourneyArrivedDestination = journey.Outcome == ScenicJourneyOutcome.Completed ? itinerary.Destination : null;
            JourneyArrivedSite = JourneyArrivedDestination == null ? null : itinerary.Site;
            recentJourneyDestinations.Enqueue(itinerary.Destination.FamilyId);
            while (recentJourneyDestinations.Count > 8) recentJourneyDestinations.Dequeue();
            if (journeyWorld != 0 && authorityWorld == journeyWorld && ZNet.instance && Player.m_localPlayer)
                RebaseAfterJourney();
        }
        else journeyStatus = reason;
        guide = null; pendingPose = requestedJourney = arrivalStarted = false;
        arrivalSurvey.Reset(); arrivalSubject.Reset(); nextJourneyAt = Time.unscaledTime + 180;
    }

    private void ClearJourneyProgramme()
    {
        guide = null; pendingPose = requestedJourney = arrivalStarted = false;
        lastCameraCapture = null; JourneyArrivedDestination = null; JourneyArrivedSite = null;
        noAdvance = 0; nextSelection = 0; arrivalSurvey.Reset(); arrivalSubject.Reset();
    }

    private void TraceJourney(string phase)
    {
        if (!ScenicTrace.Enabled || guide == null) return;
        string biome = "unknown", surface = "unknown";
        if (journey.LastRenderedPosition.HasValue && WorldGenerator.instance != null &&
            CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out var observation))
        using (observation)
        {
            Vector3 point = FromCore(journey.LastRenderedPosition.Value);
            biome = WorldGenerator.instance.GetBiome(point).ToString();
            if (ScenicSurface.TryGroundHeight(point, out float ground) &&
                ScenicSurface.TryWaterHeight(point, out bool water, out float level, stableEnvelope: true))
                surface = water && level > ground ? "water" : "land";
        }
        ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("journey"), "guide", FromCore(guide.Position))
            .Field("journeyEngine", "guide").Field("phase", phase).Field("journey", journey.Id)
            .Field("owned", true).Field("journeyOwnsPicture", journey.LastRenderedPosition.HasValue)
            .Field("travelSection", true).Field("section", journey.Id).Field("departureState", GuidePhase.ToString())
            .Field("outcome", journey.Outcome.ToString()).Field("missionCause", journeyStatus)
            .Field("destination", journey.Itinerary!.Destination.Id).Field("destinationName", journey.Itinerary.Destination.Name)
            .Field("destinationX", journey.Itinerary.Destination.Position.X).Field("destinationZ", journey.Itinerary.Destination.Position.Z)
            .Field("arrivalRadius", journey.Itinerary.ArrivalRadius).Field("targetMetres", journey.TargetMetres)
            .Field("measuredMetres", journey.TravelMetres).Field("routeProgress", journey.RouteProgressMetres)
            .Field("advanceMetres", journey.ObjectiveAdvanceMetres).Field("displacementMetres", journey.DisplacementMetres)
            .Field("shownSeconds", journey.ShownSeconds).Field("arrivalShownSeconds", journey.ArrivalShownSeconds)
            .Field("effectiveCruise", guide.Speed).Field("cruise", journey.CruiseSpeed).Field("noProgressSeconds", noAdvance)
            .Field("biomeAtRenderedPosition", biome).Field("surfaceAtRenderedPosition", surface)
            .Field("reason", journeyStatus).Field("awaitingRender", MissingGuideRender).Field("guideStatus", guide.Status)
            .Field("lastSweepRejection", guideWorld.LastRejection).Field("sweepCounts", guideWorld.SweepSummary)
            .Field("lastSweepScan", guideWorld.LastScan).Field("lastSweepScan2", guideWorld.LastScanTail));
    }
}
