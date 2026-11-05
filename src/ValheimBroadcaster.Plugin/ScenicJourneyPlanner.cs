using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Goal-directed, bounded local execution planning for a longer
/// landscape objective. Every returned corridor has passed the complete native
/// preflight. Unobserved space is a loading wait, never a usable route.</summary>
internal enum ScenicJourneyPlanningFailure { None, ObservedExhaustion, ContextInvalidated, InvalidRequest }

internal sealed class ScenicJourneyPlanner : IDisposable
{
    private readonly ScenicPathEvaluator continuationEvaluator = new ScenicPathEvaluator();
    private ScenicPathEvaluator evaluator;
    private ScenicVisibility activeVisibility;
    private readonly ScenicVisibility visibility = new ScenicVisibility("journey-ahead");
    private readonly JourneyObservationViews openingViews = new JourneyObservationViews("journey-opening", 2);
    private readonly ScenicSplinePath measured = new ScenicSplinePath();
    private readonly NativeJourneyGaze.Evaluation gaze = new NativeJourneyGaze.Evaluation();
    private readonly JourneyDepartureSpace departureSpace = new JourneyDepartureSpace();
    private readonly List<Opening> openings = new List<Opening>(JourneyDepartureSpace.CandidateCount);
    private Opening? activeOpening;
    private int pendingOpening = -1;
    private float openingWaitStarted;
    private Vector3 openingPosition, openingFocus;
    private Vector3? pendingRaisedOpening;
    private AutonomyPosition? planningCarrier;
    private long planningEpoch;
    private float searchWindowLength;
    private int candidateOption;
    private readonly JourneyNavigationPlanner continuation = new JourneyNavigationPlanner("journey-continuation", false);
    private readonly JourneyOpeningSchedule schedule = new JourneyOpeningSchedule(8, 4);
    private readonly Dictionary<int, OriginLane> lanes = new Dictionary<int, OriginLane>();
    private JourneyNavigationPlanner navigation;
    private bool navigationBound;
    private int servicedOriginSlices, activeLane = -1;
    internal ScenicJourneyPlanningFailure FailureKind { get; private set; }
    private sealed class OriginLane
    {
        internal readonly Opening Opening;
        internal readonly JourneyNavigationPlanner Navigation;
        internal readonly ScenicPathEvaluator Evaluator = new ScenicPathEvaluator();
        internal readonly ScenicVisibility Visibility;
        internal ScenicShotPlan? Candidate;
        internal bool Bound;
        internal int Slices;
        internal float RetryAt;
        internal OriginLane(Opening opening)
        { Opening = opening; Navigation = new JourneyNavigationPlanner("journey-origin-" + opening.Id, true);
          Visibility = new ScenicVisibility("journey-origin-" + opening.Id + "-preflight"); }
    }
    internal ScenicJourneyPlanner() { navigation = continuation; evaluator = continuationEvaluator; activeVisibility = visibility; }
    private sealed class Opening
    {
        internal readonly int Id;
        internal readonly Vector3 Position, Aim, Heading;
        internal readonly float Score;
        internal int Attempts;
        internal Opening(int id, Vector3 position, Vector3 aim, Vector3 heading, float score)
        { Id = id; Position = position; Aim = aim; Heading = heading; Score = score; }
    }
    private ScenicShotPlan? previous, candidate;
    private ScenicShotPlan? finalizing;
    private double finalizingDuration;
    private long finalizingEpoch;
    private float finalizingGeometryAt;
    private long nextId, journeyId;
    private float length, speed, selectedSpeed, aspect, nearClip, retryAt;
    private float verifiedSpeed, unverifiedSpeed;
    private int speedRefinements;
    private bool refiningSpeed;
    private bool moveCarrier, exitRamp, departureCut, nativeSpline;
    private int alternative;
    private AutonomyPosition? goal, arrivalFocus, arrivalCenter;
    private double arrivalRadius;
    private string lastFailure = "none";
    internal bool Complete { get; private set; }
    internal bool WaitingForLoading { get; private set; }
    internal int AttemptCount { get; private set; }
    internal int OpeningCandidates => departureSpace.Screened;
    internal int RejectedOpenings => departureSpace.Rejected;
    internal int ActiveOriginCount => lanes.Count;
    internal int ActiveOrigin => activeLane;
    internal int OriginServiceSlices => servicedOriginSlices;
    internal float OnwardValidatedAt { get; private set; }
    internal string OnwardReason { get; private set; } = "No selected onward seam";
    internal AutonomyPosition? DepartureOrigin => activeOpening == null ? (AutonomyPosition?)null : P(activeOpening.Position);
    internal ScenicShotPlan? Ready { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal string Reason { get; private set; } = "No journey corridor requested";

    internal void Begin(ScenicShotPlan predecessor, long id, long journey, float metres, float metresPerSecond,
        float cameraAspect, float cameraNearClip, bool moveObserver, bool finalWindow = false,
        AutonomyPosition? objectiveGoal = null, AutonomyPosition? observedArrivalFocus = null, bool departureCut = false,
        AutonomyPosition? arrivalCenter = null, double arrivalRadius = 0, bool? nativeSplineOverride = null,
        AutonomyPosition? planningCarrier = null)
    {
        Reset(); previous = predecessor; nextId = id; journeyId = journey;
        // Existing spatial routes support <=80m/16 knots. The programme owns
        // kilometres by joining local certificates, not by bypassing that bound.
        length = Mathf.Clamp(metres, 1, 64); speed = selectedSpeed = metresPerSecond; aspect = cameraAspect; nearClip = cameraNearClip;
        moveCarrier = moveObserver; exitRamp = finalWindow; goal = objectiveGoal; arrivalFocus = observedArrivalFocus;
        this.arrivalCenter = arrivalCenter; this.arrivalRadius = arrivalRadius;
        this.departureCut = departureCut; nativeSpline = nativeSplineOverride ?? predecessor.UseNativeSpline;
        this.planningCarrier = planningCarrier ?? (Player.m_localPlayer ? P(Player.m_localPlayer.transform.position) : (AutonomyPosition?)null);
        planningEpoch = CameraSpatialScene.Shared.Epoch;
        if (departureCut)
        {
            ScenicShotSample seed = predecessor.Sample(1);
            AutonomyPosition directionGoal = objectiveGoal ?? P(V(seed.CameraPosition) + V(ObjectiveHeading(predecessor)) * 100);
            bool retained = departureSpace.Bind(journey, planningEpoch, seed.CameraPosition, directionGoal,
                this.planningCarrier ?? seed.CameraPosition, Time.unscaledTime,
                compatibleLoadingContext: JourneyLoadingContext.ContainsPoint(seed.CameraPosition,
                    this.planningCarrier ?? seed.CameraPosition, JourneyDepartureSpace.MaximumOriginRelocation + JourneyViewPolicy.ObservationRange));
            if (!retained) { ClearOriginLanes(); openings.Clear(); searchWindowLength = 0; }
            foreach (OriginLane lane in lanes.Values)
            {
                // Outer preparation may restart with a new plan identity or
                // lens context. Keep search progress, never an old native
                // candidate/proof. Binding renews the selected graph path.
                lane.Bound = false; lane.Candidate = null; lane.Evaluator.Reset(); lane.RetryAt = 0;
            }
            // A failed long path is not evidence against a shorter corridor
            // from the same observed origin. View history remains useful.
            if (Mathf.Abs(searchWindowLength - length) > .5f)
                foreach (Opening opening in openings) opening.Attempts = 0;
            searchWindowLength = length;
        }
        Reason = departureCut ? "Surveying independent departure origins and forward views" : "Measuring goal-directed journey corridors";
    }

    internal static bool CanDepart(ScenicShotPlan preceding, AutonomyPosition routeTarget, out string reason)
    {
        ScenicShotSample end = preceding.Sample(1);
        Vector3 direction = V(routeTarget) - V(end.CameraPosition);
        if (!JourneyGazePolicy.CanDepart(end.CameraPosition, end.AimPosition, P(direction.normalized), out reason)) return false;
        return JourneyGazePolicy.CanDepart(end.CameraPosition, end.AimPosition, P(TerminalDirection(preceding)), out reason);
    }

    /// <summary>A coast starts along its measured shoreline; a ridge starts
    /// along its measured axis. Orient that axis consistently with the incoming
    /// camera movement. This is an objective heading, not distant-world evidence.</summary>
    internal static AutonomyPosition ObjectiveHeading(ScenicShotPlan preceding)
    {
        Vector3 incoming = TerminalDirection(preceding);
        Vector3 horizontal = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        if (horizontal.sqrMagnitude < .5f)
        {
            ScenicShotSample pose = preceding.Sample(1);
            horizontal = Vector3.ProjectOnPlane(V(pose.AimPosition) - V(pose.CameraPosition), Vector3.up).normalized;
        }
        if (horizontal.sqrMagnitude < .5f) horizontal = Vector3.forward;
        ScenicFeature? feature = preceding.Feature;
        Vector3 axis = horizontal;
        if (feature?.Kind == ScenicFeatureKind.Coastline && feature.WaterDirection.HasValue)
            axis = Vector3.Cross(Vector3.up, V(feature.WaterDirection.Value));
        else if (feature?.Kind == ScenicFeatureKind.Coastline || feature?.Kind == ScenicFeatureKind.Ridge)
            axis = V(feature.Direction);
        axis = Vector3.ProjectOnPlane(axis, Vector3.up);
        if (axis.sqrMagnitude < .01f) return P(horizontal);
        axis.Normalize();
        if (Vector3.Dot(axis, horizontal) < 0) axis = -axis;
        return P(axis);
    }

    internal void Step()
    {
        if (Complete || previous == null || !departureCut && Time.unscaledTime < retryAt) return;
        if (planningEpoch != CameraSpatialScene.Shared.Epoch)
        { Finish("Journey planning scene changed; discard mixed scene evidence", ScenicJourneyPlanningFailure.ContextInvalidated); return; }
        if (finalizing != null) { StepFinalization(); return; }
        if (departureCut)
        {
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var selectionWork)) return;
            using (selectionWork) if (!SelectNavigationWork()) return;
        }
        else { navigation = continuation; activeLane = -1; evaluator = continuationEvaluator; activeVisibility = visibility; }
        if (candidate == null)
        {
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                if (arrivalCenter.HasValue && arrivalFocus.HasValue)
                {
                    if (alternative >= 3) { Finish("No validated locally observed arrival approach: " + lastFailure); return; }
                    if (!TryArrivalCandidate(alternative, out candidate))
                    {
                        if (WaitingForLoading) retryAt = Time.unscaledTime + .25f;
                        else alternative++;
                        return;
                    }
                }
                else
                {
                    if (!navigationBound)
                    {
                        if (!BeginNavigation()) return;
                        if (departureCut) lanes[activeLane].Bound = true;
                    }
                    long proofBefore = navigation.ProofRefreshes, observationBefore = navigation.ObservationProgress;
                    JourneyNavigationStatus status = navigation.Step();
                    bool proofProgress = navigation.ProofRefreshes > proofBefore || navigation.ObservationProgress > observationBefore;
                    if (departureCut) schedule.ReportProofProgress(activeLane, navigation.SelectedProofPending,
                        proofProgress, status == JourneyNavigationStatus.WaitingForObservations && !proofProgress);
                    Reason = navigation.Reason + (departureCut ? "; origins " + lanes.Count + "/8; active " + activeLane +
                        "; serviced slices " + servicedOriginSlices : "; exact continuation seam");
                    WaitingForLoading = status == JourneyNavigationStatus.WaitingForObservations;
                    if (status == JourneyNavigationStatus.Searching || WaitingForLoading) return;
                    if (status != JourneyNavigationStatus.Ready || navigation.Result == null)
                    {
                        lastFailure = Reason;
                        if (departureCut && status != JourneyNavigationStatus.Invalidated)
                        {
                            RetireOrigin(activeLane, "Navigation " + status + ": " + lastFailure); return;
                        }
                        Finish("No connected route from this exact seam within the local search: " + Reason,
                            status == JourneyNavigationStatus.Invalidated ? ScenicJourneyPlanningFailure.ContextInvalidated : ScenicJourneyPlanningFailure.ObservedExhaustion); return;
                    }
                    // Geometry was searched at its useful2m/s floor. Actual
                    // native interpolation now determines the fastest safe
                    // authored pace up to the operator's request.
                    selectedSpeed = speed;
                    if (!BuildNavigationCandidate(navigation.Result, out candidate))
                    { navigation.RejectCandidate(); return; }
                }
                AttemptCount++; candidateOption = AttemptCount;
                TraceCandidate(candidate!, arrivalFocus.HasValue ? "observed-arrival" : "connected-navigation");
                if (departureCut) lanes[activeLane].Candidate = candidate;
                evaluator.Begin(candidate!, activeVisibility, aspect, nearClip, moveCarrier);
            }
        }
        bool complete = evaluator.Step(6);
        if (!complete && (evaluator.FailureKind == ScenicRejectionKind.None || evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete))
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? fitted = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && fitted != null)
        {
            ScenicShotSample seam = departureCut ? candidate!.Sample(0) : previous.Sample(1), start = fitted.Sample(0);
            if (seam.CameraPosition.Distance(start.CameraPosition) < .005 && seam.AimPosition.Distance(start.AimPosition) < .005)
            {
                finalizing = fitted; finalizingEpoch = evaluator.SceneEpoch;
                finalizingGeometryAt = Time.unscaledTime; StepFinalization(); return;
            }
            lastFailure = "Native fitting moved the exact journey seam";
        }
        else lastFailure = evaluator.FailureKind == ScenicRejectionKind.None ? evaluator.Reason : evaluator.FailureReason;
        if (evaluator.RejectedPose.HasValue && ScenicTrace.Enabled)
        {
            ScenicPose rejected = evaluator.RejectedPose.Value;
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Row("journey-candidate-rejected")
                .Field("plan", nextId).Field("openingOption", candidateOption).Field("rejection", evaluator.FailureKind.ToString())
                .Field("progress", rejected.Progress).Field("reason", lastFailure), "camera", rejected.Position), "focus", rejected.Focus));
        }
        bool wait = evaluator.FailureKind == ScenicRejectionKind.Geometry || evaluator.FailureKind == ScenicRejectionKind.Acquisition ||
            evaluator.FailureKind == ScenicRejectionKind.CarrierRange || evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete;
        if (wait)
        {
            // Unknown is neither a rejected graph edge nor a fresh route proof.
            // Keep the exact proposal and retry all native samples within the
            // original bounded planning deadline.
            evaluator.Begin(candidate!, activeVisibility, aspect, nearClip, moveCarrier);
            WaitingForLoading = true;
            if (departureCut) lanes[activeLane].RetryAt = Time.unscaledTime + .25f;
            else retryAt = Time.unscaledTime + .25f;
            Reason = "Waiting for native route observations: " + lastFailure; return;
        }
        RejectNavigationCandidate();
    }

    private bool BeginNavigation()
    {
        ScenicShotSample seam = previous!.Sample(1);
        Vector3 origin = departureCut ? activeOpening!.Position : V(seam.CameraPosition);
        Vector3 aim = departureCut ? activeOpening!.Aim : V(seam.AimPosition);
        Vector3 incoming = departureCut ? activeOpening!.Heading : TerminalDirection(previous);
        if (incoming.sqrMagnitude < .01f || Mathf.Abs(incoming.normalized.y) > .4f)
        { Finish("Incoming Journey seam has no bounded travelling tangent"); return false; }
        ScenicShotSample first = departureCut ? ScenicShotPlan.Picture(nextId, previous.Feature!, ScenicShotRole.Traverse,
            P(origin), P(aim), previous.FieldOfView, 0, -.04f).Sample(0) : seam;
        Vector3 entryAim = aim + incoming.normalized * 2;
        if (!departureCut && previous.IsSpatial)
        {
            int last = previous.WaypointCount - 1;
            float bodyStep = Vector3.Distance(V(previous.CameraWaypoint(last)), V(previous.CameraWaypoint(last - 1)));
            if (bodyStep > .05f)
                entryAim = aim + (V(previous.AimWaypoint(last)) - V(previous.AimWaypoint(last - 1))) * (2 / bodyStep);
        }
        Vector3 destination = goal.HasValue ? V(goal.Value) : origin + V(ObjectiveHeading(previous)) * 100;
        Vector3 carrier = planningCarrier.HasValue ? V(planningCarrier.Value) : origin;
        try
        {
            navigation.Begin(journeyId, planningEpoch, nextId, first, incoming, entryAim, destination, carrier,
                length, speed, previous.FieldOfView, aspect, nearClip);
            navigationBound = true; return true;
        }
        catch (ArgumentException error)
        { lastFailure = "Invalid connected navigation request: " + error.ParamName; Finish(lastFailure, ScenicJourneyPlanningFailure.InvalidRequest); return false; }
    }

    private bool BuildNavigationCandidate(JourneyNavigationPath path, out ScenicShotPlan? result)
    {
        result = null;
        if (ScenicTrace.Enabled)
        {
            CameraTraceRecord trace = ScenicTrace.Row("journey-navigation-proposal").Field("plan", nextId)
                .Field("length", path.Length).Field("goalProgress", path.GoalProgress).Field("evidenceAge", Time.unscaledTime - path.OldestEvidence)
                .Field("onward", path.OnwardPose.HasValue).Field("atGoal", path.AtGoal).Field("plannedCruise", selectedSpeed);
            if (path.OnwardPose.HasValue) ScenicTrace.Vector(trace, "onwardCamera", V(path.OnwardPose.Value.Position));
            ScenicTrace.Record(trace);
        }
        var body = new AutonomyPosition[path.Poses.Count]; var aims = new AutonomyPosition[body.Length];
        for (int i = 0; i < body.Length; i++) { body[i] = path.Poses[i].Position; aims[i] = path.Poses[i].Aim; }
        ScenicFeature source = previous!.Feature!;
        AutonomyPosition heading = goal.HasValue ? P((V(goal.Value) - V(body[0])).normalized) : ObjectiveHeading(previous);
        var feature = new ScenicFeature(source.Id, ScenicFeatureKind.Landscape, aims[aims.Length / 2], heading,
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        try
        {
            ScenicShotSample seam = previous.Sample(1);
            ScenicShotPlan proposed = ScenicShotPlan.Spatial(nextId, feature, ScenicShotRole.Traverse, body, aims,
                previous.FieldOfView, departureCut ? 0 : seam.ScreenX, departureCut ? -.04f : seam.ScreenY, nativeSpline);
            double seconds = proposed.TravelMetres / selectedSpeed / ScenicJourneyMotion.CruiseFraction(departureCut || !previous.IsJourney, exitRamp);
            result = (departureCut ? proposed.AsJourneyDeparture(journeyId, seconds) : proposed.AsJourney(journeyId, previous.Id, seconds))
                .WithJourneyRamps(departureCut || !previous.IsJourney, exitRamp).WithJourneyView();
            return true;
        }
        catch (ArgumentException error)
        { lastFailure = Reason = "Connected polyline cannot form a bounded native viewing path: " + error.ParamName; return false; }
    }

    private void RejectNavigationCandidate()
    {
        if (departureCut && lanes.TryGetValue(activeLane, out OriginLane? lane)) lane.Candidate = null;
        candidate = null; evaluator.Reset(); ClearFinalization(); WaitingForLoading = false;
        if (navigationBound) navigation.RejectCandidate(); else alternative++;
        Reason = "Native curve rejected; exploring another connected route: " + lastFailure;
    }

    internal JourneyNavigationEvidence RenewOnwardBeforeAdmission()
    {
        if (Ready == null || !Complete || Epoch != CameraSpatialScene.Shared.Epoch)
        { OnwardReason = "No selected route in the current loaded scene"; return JourneyNavigationEvidence.Blocked; }
        if (arrivalCenter.HasValue && arrivalFocus.HasValue)
        { OnwardValidatedAt = Time.unscaledTime; OnwardReason = "Observed arrival has no onward travel requirement"; return JourneyNavigationEvidence.Clear; }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work))
        { OnwardReason = "Exact onward renewal is waiting for camera work"; return JourneyNavigationEvidence.Unknown; }
        using (work)
        {
            JourneyNavigationEvidence evidence = navigation.RenewOnward(Ready);
            OnwardValidatedAt = navigation.OnwardValidatedAt; OnwardReason = navigation.OnwardReason;
            return evidence;
        }
    }
    internal void RejectReadyCandidate(string reason)
    {
        lastFailure = reason; Ready = null; Complete = false; Epoch = 0; ValidatedAt = OnwardValidatedAt = 0;
        FailureKind = ScenicJourneyPlanningFailure.None; RejectNavigationCandidate();
    }

    internal bool WithinLoadingHorizon(Vector3 carrier)
    {
        AutonomyPosition observer = P(carrier);
        if (candidate != null && !JourneyLoadingContext.ContainsPlan(candidate, observer, JourneyViewPolicy.ObservationRange)) return false;
        if (finalizing != null && !JourneyLoadingContext.ContainsPlan(finalizing, observer, JourneyViewPolicy.ObservationRange)) return false;
        if (pendingOpening >= 0 && !JourneyLoadingContext.ContainsPoint(P(openingPosition), observer, JourneyViewPolicy.ObservationRange)) return false;
        return !navigationBound || navigation.WithinLoadingHorizon(carrier);
    }

    private bool SelectNavigationWork()
    {
        bool exhausted = departureSpace.Screened >= JourneyDepartureSpace.CandidateCount;
        if (schedule.TakeDiscoveryTurn(pendingOpening >= 0, exhausted))
        {
            if (pendingOpening < 0 && departureSpace.TryNext(Time.unscaledTime, out int next))
            { pendingOpening = next; openingWaitStarted = Time.unscaledTime; }
            if (pendingOpening >= 0) ScreenOpening(pendingOpening);
            return false;
        }
        if (!schedule.TryNext(out long selected))
        {
            if (exhausted) Finish("All observed independent departure origins exhausted their bounded connected search; last evidence: " + lastFailure);
            else Reason = "Observing independent departure origins; no unknown viewpoint is authorized";
            return false;
        }
        activeLane = (int)selected;
        OriginLane lane = lanes[activeLane];
        activeOpening = lane.Opening; navigation = lane.Navigation; navigationBound = lane.Bound;
        candidate = lane.Candidate; evaluator = lane.Evaluator; activeVisibility = lane.Visibility;
        if (navigationBound && Player.m_localPlayer && !navigation.WithinLoadingHorizon(Player.m_localPlayer.transform.position))
        { RetireOrigin(activeLane, "This origin's actual required viewing horizon left the loaded region"); return false; }
        if (Time.unscaledTime < lane.RetryAt) return false;
        lane.Slices++; servicedOriginSlices++;
        if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("journey-origin-service").Field("plan", nextId)
            .Field("journey", journeyId).Field("origin", activeLane).Field("origins", lanes.Count)
            .Field("laneSlices", lane.Slices).Field("totalSlices", servicedOriginSlices));
        return true;
    }

    private void RetireOrigin(int id, string reason)
    {
        if (lanes.TryGetValue(id, out OriginLane? lane))
        { lane.Opening.Attempts = JourneyDepartureSpace.RoutesPerOpening; lane.Navigation.Reset(); lane.Evaluator.Dispose(); lane.Visibility.Reset(); lanes.Remove(id); }
        schedule.Remove(id); activeOpening = null; navigation = continuation; navigationBound = false; activeLane = -1;
        candidate = null; evaluator = continuationEvaluator; activeVisibility = visibility;
        Reason = "Retired one departure origin: " + reason;
        if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("journey-origin-retired").Field("plan", nextId)
            .Field("journey", journeyId).Field("origin", id).Field("reason", reason));
    }
    private void ClearOriginLanes()
    {
        foreach (OriginLane lane in lanes.Values) { lane.Navigation.Reset(); lane.Evaluator.Dispose(); lane.Visibility.Reset(); }
        lanes.Clear(); schedule.Reset(); openingViews.Reset(); activeLane = -1; servicedOriginSlices = 0;
        candidate = null; evaluator = continuationEvaluator; activeVisibility = visibility; navigation = continuation; navigationBound = false;
    }

    private void ScreenOpening(int id)
    {
        openingPosition = openingFocus = default;
        ScenicShotSample seed = previous!.Sample(1);
        JourneyOpeningProposal proposal;
        try
        {
            AutonomyPosition destination = goal ?? P(V(seed.CameraPosition) + V(ObjectiveHeading(previous)) * 100);
            proposal = JourneyDepartureSpace.Propose(id, seed.CameraPosition, destination, speed);
        }
        catch (ArgumentException)
        { CompleteOpening(id, false, "Departure origin or bearing lies outside supported world coordinates"); return; }
        Vector3 position = pendingRaisedOpening ?? V(proposal.Position); openingPosition = position;
        if (!WorldPoint(position) || !MeasureFloor(position, out float floor))
        { DeferOpening(id, "Departure origin terrain/water is not observed"); return; }
        position.y = Mathf.Max(position.y, floor + .75f);
        Vector3 focus;
        try { focus = V(proposal.AimAt(P(position))); }
        catch (ArgumentException)
        { CompleteOpening(id, false, "Departure gaze lies outside supported world coordinates"); return; }
        openingPosition = position; openingFocus = focus;
        Vector3 carrier = planningCarrier.HasValue ? V(planningCarrier.Value) : position;
        if (Vector3.Distance(position, V(seed.CameraPosition)) > JourneyDepartureSpace.MaximumOriginRelocation ||
            !JourneyLoadingContext.ContainsPoint(P(position), P(carrier), JourneyViewPolicy.ObservationRange))
        { CompleteOpening(id, false, "Departure opening exceeds the observed local camera-and-gaze reach"); return; }
        if (departureSpace.WasMeasured(P(position), P(focus)))
        { CompleteOpening(id, false, "Equivalent measured departure opening already considered"); return; }
        var source = previous.Feature!;
        var landscape = new ScenicFeature(source.Id, ScenicFeatureKind.Landscape, P(focus), proposal.Heading,
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        ScenicShotPlan probe = ScenicShotPlan.Picture(nextId, landscape, ScenicShotRole.Traverse, P(position), P(focus), previous.FieldOfView,
            0, -.04f).WithStableWaterClearance(true).WithJourneyView();
        var pose = new ScenicPose(position, focus, ScenicVisibility.Compose(position, focus, 0, -.04f, previous.FieldOfView, aspect), previous.FieldOfView, 0);
        if (!openingViews.TryGet(id + 1L, planningEpoch, position, out ScenicVisibility openingView))
        { DeferOpening(id, openingViews.Reason, true); return; }
        bool readable = ScenicPoseValidation.Check(pose, probe, openingView, aspect, nearClip, false, moveCarrier,
            out float quality, out ScenicRejectionKind kind, out string reason);
        if (!readable && !pendingRaisedOpening.HasValue && kind == ScenicRejectionKind.InsideVegetation &&
            openingView.TryCanopyLift(position, out float canopyLift))
        {
            Vector3 raised = position + Vector3.up * canopyLift;
            if (Vector3.Distance(raised, V(seed.CameraPosition)) <= JourneyDepartureSpace.MaximumOriginRelocation &&
                Vector3.Distance(raised, carrier) <= 70)
            {
                // Retain the exact proposed origin while its new observation
                // matures. Rechecking the buried origin every frame would keep
                // recentering the survey away from the measured escape.
                openingViews.Release(id + 1L); pendingRaisedOpening = raised; openingWaitStarted = Time.unscaledTime;
                Reason = "Screening an independent departure above the measured canopy"; return;
            }
        }
        if (!readable && (kind == ScenicRejectionKind.SurveyIncomplete || kind == ScenicRejectionKind.Geometry ||
            kind == ScenicRejectionKind.Acquisition || kind == ScenicRejectionKind.CarrierRange))
        { DeferOpening(id, reason, kind == ScenicRejectionKind.SurveyIncomplete); return; }
        // Remember only observed outcomes. Missing evidence can be retried; a
        // clipped/obstructed opening cannot reappear under a different label.
        bool unique = departureSpace.RememberMeasured(P(position), P(focus));
        if (readable && unique)
        {
            float local = 1 - Mathf.Clamp01(Vector3.Distance(position, V(seed.CameraPosition)) / 32);
            var opening = new Opening(id, position, focus, V(proposal.Heading), quality + local * .1f);
            openings.Add(opening);
            if (schedule.Register(id)) lanes.Add(id, new OriginLane(opening));
        }
        CompleteOpening(id, readable && unique, !unique ? "Equivalent measured departure opening already considered" : reason);
    }

    private void DeferOpening(int id, string reason, bool projectionPending = false)
    {
        WaitingForLoading = true; lastFailure = reason;
        Reason = "Observing departure opening " + (id + 1) + ": " + reason;
        // Give one exact foreground projection a chance to finish; moving its
        // centre every frame would reproduce the old survey starvation.
        if (projectionPending || Time.unscaledTime - openingWaitStarted < .75f) return;
        openingViews.Release(id + 1L); departureSpace.Defer(id, Time.unscaledTime); pendingOpening = -1; pendingRaisedOpening = null;
    }

    private void CompleteOpening(int id, bool readable, string reason)
    {
        openingViews.Release(id + 1L); departureSpace.Complete(id, readable); pendingOpening = -1; pendingRaisedOpening = null;
        WaitingForLoading = false; lastFailure = readable ? lastFailure : reason;
        Reason = "Departure space: " + openings.Count + " readable openings from " + departureSpace.Screened +
            " measured views; " + reason;
        if (ScenicTrace.Enabled) ScenicTrace.Record(ScenicTrace.Row("journey-opening").Field("plan", nextId).Field("opening", id)
            .Field("readable", readable).Field("screened", departureSpace.Screened).Field("retained", openings.Count)
            .Field("cameraX", openingPosition.x).Field("cameraY", openingPosition.y).Field("cameraZ", openingPosition.z)
            .Field("focusX", openingFocus.x).Field("focusY", openingFocus.y).Field("focusZ", openingFocus.z).Field("reason", reason));
    }

    private void StepFinalization()
    {
        ScenicShotPlan fitted = finalizing!;
        if (CameraSpatialScene.Shared.Epoch != finalizingEpoch)
        { Finish("Journey scene changed while verifying its native gaze"); return; }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
        using (work)
        {
            if (!measured.Ready)
            {
                // Duration follows the distance that the native body actually
                // travels. Retiming changes no normalized pose or certificate.
                if (!measured.Prepare(fitted, 1))
                { Finish("Validated journey native path could not be measured"); return; }
                selectedSpeed = speed;
                finalizingDuration = DurationAt(fitted, selectedSpeed);
                gaze.Begin(measured, finalizingDuration);
            }
            for (int i = 0; i < 64 && !gaze.Complete && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic); i++)
                gaze.Step(1);
            if (!gaze.Complete)
            { Reason = "Verifying native journey gaze: " + gaze.CheckedIntervals + "/256 intervals"; return; }
            if (!refiningSpeed && !gaze.Accepted)
            {
                if (selectedSpeed <= 2.001f)
                { lastFailure = "Native Journey gaze exceeds its angular bound at the useful2m/s floor"; RejectNavigationCandidate(); return; }
                refiningSpeed = true; verifiedSpeed = 0; unverifiedSpeed = selectedSpeed; speedRefinements = 0;
                VerifySpeed(fitted, 2); return;
            }
            if (refiningSpeed)
            {
                if (gaze.Accepted) verifiedSpeed = selectedSpeed;
                else
                {
                    unverifiedSpeed = selectedSpeed;
                    if (selectedSpeed <= 2.001f)
                    { lastFailure = "Native Journey gaze cannot maintain the useful2m/s floor"; RejectNavigationCandidate(); return; }
                }
                speedRefinements++;
                if (speedRefinements < 8 && unverifiedSpeed - verifiedSpeed > .1f)
                { VerifySpeed(fitted, (verifiedSpeed + unverifiedSpeed) * .5f); return; }
                // Retiming does not change geometry. The retained lower bound
                // is an actually verified speed, not an inferred safe rate.
                selectedSpeed = verifiedSpeed;
                finalizingDuration = DurationAt(fitted, selectedSpeed);
            }
            Ready = fitted.WithDuration(finalizingDuration).WithJourneyCruiseSpeed(selectedSpeed);
            Epoch = finalizingEpoch; ValidatedAt = finalizingGeometryAt;
            Complete = true; WaitingForLoading = false; FailureKind = ScenicJourneyPlanningFailure.None;
            Reason = "Validated " + fitted.TravelMetres.ToString("F0") + "m goal-directed corridor after " + AttemptCount + " proposals";
        }
    }

    private double DurationAt(ScenicShotPlan plan, float cruise)
    {
        double seconds = (plan.UseNativeSpline ? measured.Length : plan.TravelMetres) / cruise /
            ScenicJourneyMotion.CruiseFraction(plan.JourneyEaseIn, plan.JourneyEaseOut);
        return arrivalCenter.HasValue ? Math.Max(1, seconds) : seconds;
    }
    private void VerifySpeed(ScenicShotPlan plan, float cruise)
    {
        selectedSpeed = cruise; finalizingDuration = DurationAt(plan, cruise);
        gaze.Begin(measured, finalizingDuration);
        Reason = "Measuring the fastest bounded native Journey pace: " + cruise.ToString("F1") + "m/s";
    }

    private void ClearFinalization()
    {
        finalizing = null; finalizingDuration = 0; finalizingEpoch = 0; finalizingGeometryAt = 0;
        measured.Reset(); gaze.Reset(); refiningSpeed = false; verifiedSpeed = unverifiedSpeed = 0; speedRefinements = 0;
    }

    private void TraceCandidate(ScenicShotPlan plan, string source)
    {
        if (!ScenicTrace.Enabled) return;
        CameraTraceRecord row = ScenicTrace.Row("journey-candidate").Field("plan", plan.Id)
            .Field("source", source).Field("journey", plan.JourneyId).Field("predecessor", plan.ContinuationOf)
            .Field("option", candidateOption).Field("travel", plan.TravelMetres).Field("fov", plan.FieldOfView)
            .Field("waypoints", plan.WaypointCount).Field("native", plan.UseNativeSpline).Field("requestedCruise", speed).Field("plannedCruise", selectedSpeed);
        for (int i = 0; i < plan.WaypointCount; i++)
        {
            AutonomyPosition body = plan.CameraWaypoint(i), aim = plan.AimWaypoint(i);
            row.Field("camera" + i + "X", body.X).Field("camera" + i + "Y", body.Y).Field("camera" + i + "Z", body.Z)
                .Field("aim" + i + "X", aim.X).Field("aim" + i + "Y", aim.Y).Field("aim" + i + "Z", aim.Z);
        }
        ScenicTrace.Record(row);
    }

    private bool TryArrivalCandidate(int option, out ScenicShotPlan? result)
    {
        result = null; WaitingForLoading = false;
        ScenicShotPlan preceding = previous!;
        ScenicShotSample end = preceding.Sample(1);
        Vector3 start = V(end.CameraPosition);
        Vector3 incoming = TerminalDirection(preceding);
        if (incoming.sqrMagnitude < .001f || Mathf.Abs(incoming.y) > .85f)
        { lastFailure = Reason = "Incoming camera path has no usable travelling tangent"; return false; }
        Vector3 forward = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 desired = goal.HasValue ? Vector3.ProjectOnPlane(V(goal.Value) - start, Vector3.up).normalized : V(ObjectiveHeading(preceding));
        if (desired.sqrMagnitude < .5f) desired = forward;
        float turn = option == 1 ? 24 : option == 2 ? -24 : 0;
        float loft = 0;
        float routeLength = length;
        if (planningCarrier.HasValue)
            routeLength = Mathf.Min(routeLength, (float)JourneyRunwayPolicy.WindowMetres(speed,
                Vector3.Distance(start, V(planningCarrier.Value)), routeLength));
        if (routeLength < ScenicJourneyPolicy.MinimumWindowMetres)
        { lastFailure = Reason = "Observed departure leaves no useful loaded travel corridor"; return false; }
        var positions = new List<Vector3>(16) { start };
        var floors = new List<float>(16);
        if (!MeasureFloor(start, out float firstFloor)) return false;
        floors.Add(firstFloor);
        float clearance = Mathf.Clamp(start.y - firstFloor, .25f, 18f);
        if (arrivalCenter.HasValue && arrivalFocus.HasValue)
        {
            if (!JourneyArrivalGeometry.TryBuild(end.CameraPosition, P(incoming), arrivalCenter.Value, arrivalRadius,
                arrivalFocus.Value, routeLength, Mathf.Sin(turn * Mathf.Deg2Rad), out var approach, out string approachReason))
            { lastFailure = Reason = approachReason; return false; }
            for (int i = 1; i < approach.Length; i++)
            {
                Vector3 point = V(approach[i]);
                if (!MeasureFloor(point, out float floor)) return false;
                positions.Add(point); floors.Add(floor);
            }
        }
        if (positions.Count < 3) { lastFailure = Reason = "Insufficient continuous route distance"; return false; }
        // Future surface heights inform the complete profile before any pose
        // is admitted. Native interpolation/collision validation still follows.
        if (!JourneyTerrainProfile.TryFit(positions, floors, clearance, loft, out Vector3[] fitted, out string fitReason,
            preserveIncomingTangent: true, cruiseAltitude: null))
        { lastFailure = Reason = fitReason; return false; }
        var body = new AutonomyPosition[fitted.Length];
        body[0] = end.CameraPosition;
        for (int i = 1; i < fitted.Length; i++)
        {
            if (!WorldPoint(fitted[i])) { lastFailure = Reason = "Journey composition exceeds world bounds"; return false; }
            body[i] = P(fitted[i]);
        }
        // A sharp but usable arrival may require a slower authored pace.
        // Native finalization will independently find its fastest safe speed.
        AutonomyPosition startingAim = end.AimPosition;
        if (!JourneyGazePolicy.TryBuild(body, startingAim, 2, preceding.Feature?.WaterDirection,
            arrivalFocus, out AutonomyPosition[] aims, out string gazeReason,
            contextYawDegrees: option % 3 == 1 ? 12 : option % 3 == 2 ? -12 : 0,
            contextElevation: option / 3 % 2 == 1 ? 3 : 0))
        { lastFailure = Reason = gazeReason; return false; }
        // Lateral detours may be useful, but cannot consume the programme by
        // circling or travelling backwards relative to its explicit objective.
        Vector3 displacement = V(body[body.Length - 1]) - start;
        if (Vector3.Dot(Vector3.ProjectOnPlane(displacement, Vector3.up), desired) < routeLength * .15f)
        { lastFailure = Reason = "Candidate does not develop the journey objective"; return false; }
        ScenicFeature source = preceding.Feature!;
        var feature = new ScenicFeature(source.Id, ScenicFeatureKind.Landscape, aims[aims.Length / 2], P(desired),
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        try
        {
            ScenicShotPlan proposed = ScenicShotPlan.Spatial(nextId, feature, ScenicShotRole.Traverse, body, aims,
                preceding.FieldOfView, end.ScreenX, end.ScreenY, nativeSpline);
            double seconds = proposed.TravelMetres / speed / ScenicJourneyMotion.CruiseFraction(!preceding.IsJourney, exitRamp);
            if (arrivalCenter.HasValue) seconds = Math.Max(1, seconds);
            result = proposed.AsJourney(journeyId, preceding.Id, seconds)
                .WithJourneyRamps(!preceding.IsJourney, exitRamp).WithJourneyView(false);
            return true;
        }
        catch (ArgumentException error)
        {
            lastFailure = Reason = "Candidate exceeded native spatial path or composition bounds: " +
                error.ParamName + ": " + error.Message.Replace('\n', ' ').Replace('\r', ' ');
            return false;
        }
    }

    private bool MeasureFloor(Vector3 point, out float floor)
    {
        floor = 0;
        if (!ScenicSurface.TryGroundHeight(point, out float ground) ||
            !ScenicSurface.TryWaterHeight(point, out bool water, out float waterLevel, stableEnvelope: true))
        {
            WaitingForLoading = true;
            lastFailure = Reason = "Journey terrain/water ahead has not been observed yet"; return false;
        }
        floor = Mathf.Max(ground + ScenicSurfacePolicy.BodyGroundClearance,
            water ? waterLevel + ScenicSurfacePolicy.BodyWaterClearance : float.NegativeInfinity);
        return true;
    }
    private static Vector3 TerminalDirection(ScenicShotPlan preceding)
    {
        Vector3 end = V(preceding.Sample(1).CameraPosition);
        AutonomyPosition before = preceding.IsSpatial ? preceding.CameraWaypoint(preceding.WaypointCount - 2) : preceding.Sample(15d / 16).CameraPosition;
        return (end - V(before)).normalized;
    }
    private void Finish(string reason, ScenicJourneyPlanningFailure failure = ScenicJourneyPlanningFailure.ObservedExhaustion)
    { Complete = true; Ready = null; Reason = reason; FailureKind = failure; }
    internal void Reset()
    {
        openingViews.Reset();
        previous = candidate = Ready = null; goal = arrivalFocus = arrivalCenter = null; arrivalRadius = 0;
        continuationEvaluator.Reset(); evaluator = continuationEvaluator; visibility.Reset(); activeVisibility = visibility;
        activeOpening = null; planningCarrier = null;
        pendingOpening = -1; openingWaitStarted = 0; planningEpoch = 0;
        pendingRaisedOpening = null;
        openingPosition = openingFocus = default;
        navigationBound = false;
        ClearFinalization();
        FailureKind = ScenicJourneyPlanningFailure.None; OnwardValidatedAt = 0; OnwardReason = "No selected onward seam";
        navigation = continuation; activeLane = -1;
        Complete = WaitingForLoading = departureCut = false; alternative = AttemptCount = 0; Epoch = 0; ValidatedAt = retryAt = 0; lastFailure = "none";
    }
    internal void ForgetSearchEpisode()
    { departureSpace.Reset(); openings.Clear(); searchWindowLength = 0; ClearOriginLanes(); continuation.Reset(); navigation = continuation; navigationBound = false; }
    public void Dispose() { Reset(); ForgetSearchEpisode(); continuationEvaluator.Dispose(); measured.Dispose(); }
    private static bool WorldPoint(Vector3 point) => TargetRoster.Finite(point) &&
        Mathf.Abs(point.x) <= 20000 && Mathf.Abs(point.y) <= 20000 && Mathf.Abs(point.z) <= 20000;
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
