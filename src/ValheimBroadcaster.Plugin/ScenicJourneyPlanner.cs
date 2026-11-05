using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Goal-directed, bounded local execution planning for a longer
/// landscape objective. Every returned corridor has passed the complete native
/// preflight. Unobserved space is a loading wait, never a usable route.</summary>
internal sealed class ScenicJourneyPlanner : IDisposable
{
    private static readonly float[] turns = { 0, 20, -20, 40, -40, 70, -70 };
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicVisibility visibility = new ScenicVisibility("journey-ahead");
    private readonly ScenicSplinePath measured = new ScenicSplinePath();
    private readonly NativeJourneyGaze.Evaluation gaze = new NativeJourneyGaze.Evaluation();
    private readonly JourneyDepartureSpace departureSpace = new JourneyDepartureSpace();
    private readonly List<Opening> openings = new List<Opening>(JourneyDepartureSpace.RetainedOpenings);
    private Opening? activeOpening;
    private int pendingOpening = -1, openingScreensSinceRoute;
    private float openingWaitStarted;
    private Vector3 openingPosition, openingFocus;
    private AutonomyPosition? planningCarrier;
    private long planningEpoch;
    private sealed class Opening
    {
        internal readonly Vector3 Position, Aim, Heading;
        internal readonly float Score;
        internal int Attempts;
        internal Opening(Vector3 position, Vector3 aim, Vector3 heading, float score)
        { Position = position; Aim = aim; Heading = heading; Score = score; }
    }
    private ScenicShotPlan? previous, candidate;
    private ScenicShotPlan? finalizing;
    private double finalizingDuration;
    private long finalizingEpoch;
    private float finalizingGeometryAt;
    private long nextId, journeyId;
    private float length, speed, aspect, nearClip, started, retryAt;
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
        length = Mathf.Clamp(metres, 1, 64); speed = metresPerSecond; aspect = cameraAspect; nearClip = cameraNearClip;
        moveCarrier = moveObserver; exitRamp = finalWindow; goal = objectiveGoal; arrivalFocus = observedArrivalFocus;
        this.arrivalCenter = arrivalCenter; this.arrivalRadius = arrivalRadius;
        this.departureCut = departureCut; nativeSpline = nativeSplineOverride ?? predecessor.UseNativeSpline; started = Time.unscaledTime;
        this.planningCarrier = planningCarrier ?? (Player.m_localPlayer ? P(Player.m_localPlayer.transform.position) : (AutonomyPosition?)null);
        planningEpoch = CameraSpatialScene.Shared.Epoch;
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
        if (Complete || previous == null || Time.unscaledTime < retryAt) return;
        if (planningEpoch != CameraSpatialScene.Shared.Epoch)
        { Finish("Journey planning scene changed; discard the old departure observations"); return; }
        if (Time.unscaledTime - started > ScenicJourneyPolicy.PreparationSeconds)
        { Finish((WaitingForLoading ? "Journey loading horizon did not become ready" : "Journey corridor search expired") + "; last evidence: " + lastFailure); return; }
        if (finalizing != null) { StepFinalization(); return; }
        if (candidate == null)
        {
            if (!departureCut && alternative >= turns.Length * 3)
            { Finish("No validated route among the goal-directed lateral/elevation alternatives; last evidence: " + lastFailure); return; }
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                if (departureCut && activeOpening == null && !SelectOpening()) return;
                AttemptCount++;
                int option = departureCut ? activeOpening!.Attempts++ : alternative;
                if (!TryCandidate(option, out candidate))
                {
                    if (WaitingForLoading)
                    { retryAt = Time.unscaledTime + .25f; if (departureCut) activeOpening!.Attempts--; }
                    else { alternative++; activeOpening = null; }
                    return;
                }
                openingScreensSinceRoute = 0;
                evaluator.Begin(candidate!, visibility, aspect, nearClip, moveCarrier);
            }
        }
        bool complete = evaluator.Step(6);
        // The generic evaluator also searches rejected routes for valid local
        // sub-ranges. A journey instead needs an entire connected continuation,
        // so a measured obstruction immediately spends the next alternative.
        if (!complete && evaluator.FailureKind == ScenicRejectionKind.None)
        { Reason = evaluator.Reason; return; }
        if (!complete && evaluator.FailureKind == ScenicRejectionKind.SurveyIncomplete)
        { Reason = evaluator.Reason; return; }
        ScenicShotPlan? fitted = evaluator.EvaluatedPlan;
        if (complete && evaluator.Accepted && fitted != null)
        {
            // A departure cut has its own authored first pose. Later sections
            // must join the preceding live section's exact complete end pose.
            ScenicShotSample end = departureCut ? candidate!.Sample(0) : previous.Sample(1), start = fitted.Sample(0);
            if (end.CameraPosition.Distance(start.CameraPosition) < .005 && end.AimPosition.Distance(start.AimPosition) < .005)
            {
                // Native gaze sampling is part of planning work too. Preserve
                // this completed geometry proof while finishing it over frames;
                // later mathematical verification cannot refresh its lifetime.
                finalizing = fitted; finalizingEpoch = evaluator.SceneEpoch;
                finalizingGeometryAt = Time.unscaledTime;
                StepFinalization(); return;
            }
            lastFailure = "Terrain fitting moved the exact journey seam";
        }
        else lastFailure = evaluator.FailureKind == ScenicRejectionKind.None ? evaluator.Reason : evaluator.FailureReason;
        ScenicRejectionKind failure = evaluator.FailureKind;
        bool wait = failure == ScenicRejectionKind.Geometry || failure == ScenicRejectionKind.Acquisition ||
            failure == ScenicRejectionKind.CarrierRange || failure == ScenicRejectionKind.SurveyIncomplete;
        if (!wait && departureCut && activeOpening != null && evaluator.ValidatedFraction <= .00001f &&
            (failure == ScenicRejectionKind.Focus || failure == ScenicRejectionKind.Foliage || failure == ScenicRejectionKind.InsideVegetation))
            activeOpening.Attempts = JourneyDepartureSpace.RoutesPerOpening;
        candidate = null; evaluator.Reset();
        WaitingForLoading = wait;
        if (wait)
        {
            // Keep the same pending corridor while the carrier advances and the
            // persistent observations mature. A missing tile is not a bad turn.
            retryAt = Time.unscaledTime + .25f;
            if (departureCut && activeOpening != null) activeOpening.Attempts--;
            Reason = "Waiting for the journey loading/observation horizon: " + lastFailure;
        }
        else { alternative++; activeOpening = null; Reason = "Trying another observed opening or goal-directed corridor: " + lastFailure; }
    }

    private bool SelectOpening()
    {
        openings.RemoveAll(opening => opening.Attempts >= JourneyDepartureSpace.RoutesPerOpening);
        Opening? best = null;
        foreach (Opening opening in openings)
            if (opening.Attempts < JourneyDepartureSpace.RoutesPerOpening &&
                (best == null || opening.Score - opening.Attempts * .15f > best.Score - best.Attempts * .15f)) best = opening;
        // Compare a small batch of distinct openings before spending full native
        // route work. Interleave further discovery after a rejected corridor.
        if (openings.Count < JourneyDepartureSpace.RetainedOpenings && (best == null || openingScreensSinceRoute < 4))
        {
            if (pendingOpening < 0 && departureSpace.TryNext(Time.unscaledTime, out int id))
            { pendingOpening = id; openingWaitStarted = Time.unscaledTime; }
            if (pendingOpening >= 0)
            { ScreenOpening(pendingOpening); return false; }
        }
        if (best != null) { activeOpening = best; return true; }
        if (departureSpace.Screened >= JourneyDepartureSpace.CandidateCount || openings.Count >= JourneyDepartureSpace.RetainedOpenings)
            Finish("No complete route from the measured departure openings; screened " + departureSpace.Screened +
                ", rejected " + departureSpace.Rejected + "; last evidence: " + lastFailure);
        else Reason = "Waiting for local departure observations; no unknown opening is authorized";
        return false;
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
        Vector3 position = V(proposal.Position); openingPosition = position;
        if (!WorldPoint(position) || !MeasureFloor(position, out float floor))
        { DeferOpening(id, "Departure origin terrain/water is not observed"); return; }
        position.y = Mathf.Max(position.y, floor + .75f);
        Vector3 focus;
        try { focus = V(proposal.AimAt(P(position))); }
        catch (ArgumentException)
        { CompleteOpening(id, false, "Departure gaze lies outside supported world coordinates"); return; }
        focus = visibility.LandscapeFocus(position, focus, nearClip);
        openingPosition = position; openingFocus = focus;
        Vector3 carrier = planningCarrier.HasValue ? V(planningCarrier.Value) : position;
        if (Vector3.Distance(position, V(seed.CameraPosition)) > JourneyDepartureSpace.MaximumOriginRelocation ||
            Vector3.Distance(position, carrier) > 70 || Vector3.Distance(focus, carrier) > JourneyRunwayPolicy.LocalReachMetres)
        { CompleteOpening(id, false, "Departure opening exceeds the observed local camera-and-gaze reach"); return; }
        if (departureSpace.WasMeasured(P(position), P(focus)))
        { CompleteOpening(id, false, "Equivalent measured departure opening already considered"); return; }
        var source = previous.Feature!;
        var landscape = new ScenicFeature(source.Id, ScenicFeatureKind.Landscape, P(focus), proposal.Heading,
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        ScenicShotPlan probe = ScenicShotPlan.Picture(nextId, landscape, ScenicShotRole.Traverse, P(position), P(focus), previous.FieldOfView,
            0, -.04f).WithStableWaterClearance(true);
        var pose = new ScenicPose(position, focus, ScenicVisibility.Compose(position, focus, 0, -.04f, previous.FieldOfView, aspect), previous.FieldOfView, 0);
        bool readable = ScenicPoseValidation.Check(pose, probe, visibility, aspect, nearClip, false, moveCarrier,
            out float quality, out ScenicRejectionKind kind, out string reason);
        if (!readable && (kind == ScenicRejectionKind.SurveyIncomplete || kind == ScenicRejectionKind.Geometry ||
            kind == ScenicRejectionKind.Acquisition || kind == ScenicRejectionKind.CarrierRange))
        { DeferOpening(id, reason); return; }
        // Remember only observed outcomes. Missing evidence can be retried; a
        // clipped/obstructed opening cannot reappear under a different label.
        bool unique = departureSpace.RememberMeasured(P(position), P(focus));
        if (readable && unique)
        {
            float local = 1 - Mathf.Clamp01(Vector3.Distance(position, V(seed.CameraPosition)) / 32);
            openings.Add(new Opening(position, focus, V(proposal.Heading), quality + local * .1f));
        }
        CompleteOpening(id, readable && unique, !unique ? "Equivalent measured departure opening already considered" : reason);
    }

    private void DeferOpening(int id, string reason)
    {
        WaitingForLoading = true; lastFailure = reason;
        Reason = "Observing departure opening " + (id + 1) + ": " + reason;
        // Give one exact foreground projection a chance to finish; moving its
        // centre every frame would reproduce the old survey starvation.
        if (Time.unscaledTime - openingWaitStarted < .75f) return;
        departureSpace.Defer(id, Time.unscaledTime); pendingOpening = -1; openingScreensSinceRoute++;
    }

    private void CompleteOpening(int id, bool readable, string reason)
    {
        departureSpace.Complete(id, readable); pendingOpening = -1; openingScreensSinceRoute++;
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
                double distance = fitted.UseNativeSpline ? measured.Length : fitted.TravelMetres;
                finalizingDuration = distance / speed /
                    ScenicJourneyMotion.CruiseFraction(fitted.JourneyEaseIn, fitted.JourneyEaseOut);
                if (arrivalCenter.HasValue) finalizingDuration = Math.Max(1, finalizingDuration);
                gaze.Begin(measured, finalizingDuration);
            }
            for (int i = 0; i < 64 && !gaze.Complete && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic); i++)
                gaze.Step(1);
            if (!gaze.Complete)
            { Reason = "Verifying native journey gaze: " + gaze.CheckedIntervals + "/256 intervals"; return; }
            if (!gaze.Accepted)
            {
                lastFailure = "Native journey gaze exceeds the bounded angular rate at the selected cruise";
                candidate = null; evaluator.Reset(); alternative++; activeOpening = null; ClearFinalization();
                Reason = "Trying another corridor with a gentler viewing path"; return;
            }
            Ready = fitted.WithDuration(finalizingDuration);
            Epoch = finalizingEpoch; ValidatedAt = finalizingGeometryAt;
            Complete = true; WaitingForLoading = false;
            Reason = "Validated " + fitted.TravelMetres.ToString("F0") + "m goal-directed corridor after " + AttemptCount + " proposals";
        }
    }

    private void ClearFinalization()
    {
        finalizing = null; finalizingDuration = 0; finalizingEpoch = 0; finalizingGeometryAt = 0;
        measured.Reset(); gaze.Reset();
    }

    private bool TryCandidate(int option, out ScenicShotPlan? result)
    {
        result = null; WaitingForLoading = false;
        ScenicShotPlan preceding = previous!;
        ScenicShotSample end = preceding.Sample(1);
        Vector3 start = departureCut ? activeOpening!.Position : V(end.CameraPosition);
        Vector3 incoming = departureCut ? activeOpening!.Heading : TerminalDirection(preceding);
        if (incoming.sqrMagnitude < .001f || Mathf.Abs(incoming.y) > .85f)
        { lastFailure = Reason = "Incoming camera path has no usable travelling tangent"; return false; }
        Vector3 forward = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 desired = goal.HasValue ? Vector3.ProjectOnPlane(V(goal.Value) - start, Vector3.up).normalized : V(ObjectiveHeading(preceding));
        if (desired.sqrMagnitude < .5f) desired = forward;
        float turn = departureCut ? option == 1 ? 24 : option == 2 ? -24 : 0 : turns[option % turns.Length];
        Vector3 targetHeading = Quaternion.AngleAxis(turn, Vector3.up) * desired;
        float loft = departureCut ? option * 6f : option / turns.Length * 8f;
        float routeLength = length;
        if (planningCarrier.HasValue)
            routeLength = Mathf.Min(routeLength, (float)JourneyRunwayPolicy.WindowMetres(speed,
                Vector3.Distance(start, V(planningCarrier.Value)), routeLength));
        if (routeLength < ScenicJourneyPolicy.MinimumWindowMetres)
        { lastFailure = Reason = "Observed departure leaves no useful loaded travel corridor"; return false; }
        var positions = new List<Vector3>(16) { start };
        var floors = new List<float>(16);
        if (!MeasureFloor(start, out float firstFloor)) return false;
        // An independent departure is not yet an owned seam. Establish its
        // route above the future wave envelope before screening any movement.
        // Later sections must retain the exact existing seam and tangent.
        if (departureCut)
        {
            start.y = Mathf.Max(start.y, firstFloor + .5f);
            positions[0] = start;
        }
        floors.Add(firstFloor);
        float clearance = Mathf.Clamp(start.y - firstFloor, .25f, 18f);
        float travelled = 0;
        float stepLength = Mathf.Max(4, (routeLength - Math.Min(2, routeLength)) / 13f);
        Vector3 position = start;
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
        else while (travelled < routeLength - .01f && positions.Count < 16)
        {
            float step = Mathf.Min(routeLength - travelled, positions.Count == 1 ? 2f : stepLength);
            // Consume a numerical/short tail with the preceding segment. A
            // leftover centimetre is not a useful control point and violates
            // Spatial's minimum separation even in completely open water.
            if (routeLength - travelled - step < .1f) step = routeLength - travelled;
            if (positions.Count == 1) position += incoming * step;
            else
            {
                // Heading changes are distributed through measured space; the
                // first short section preserves the preceding native tangent.
                forward = Vector3.RotateTowards(forward, targetHeading, step * 3f * Mathf.Deg2Rad, 0).normalized;
                position += forward * step;
            }
            travelled += step;
            if (!WorldPoint(position)) { lastFailure = Reason = "Journey objective reaches world coordinate limits"; return false; }
            if (!MeasureFloor(position, out float floor)) return false;
            positions.Add(position); floors.Add(floor);
        }
        if (positions.Count < 3) { lastFailure = Reason = "Insufficient continuous route distance"; return false; }
        // Future surface heights inform the complete profile before any pose
        // is admitted. Native interpolation/collision validation still follows.
        if (!JourneyTerrainProfile.TryFit(positions, floors, clearance, loft, out Vector3[] fitted, out string fitReason,
            preserveIncomingTangent: !departureCut))
        { lastFailure = Reason = fitReason; return false; }
        var body = new AutonomyPosition[fitted.Length];
        body[0] = departureCut ? P(start) : end.CameraPosition;
        for (int i = 1; i < fitted.Length; i++)
        {
            if (!WorldPoint(fitted[i])) { lastFailure = Reason = "Journey composition exceeds world bounds"; return false; }
            body[i] = P(fitted[i]);
        }
        AutonomyPosition startingAim = departureCut ? P(activeOpening!.Aim) : end.AimPosition;
        if (!JourneyGazePolicy.TryBuild(body, startingAim, speed, preceding.Feature?.WaterDirection,
            arrivalFocus, out AutonomyPosition[] aims, out string gazeReason,
            contextYawDegrees: option % 3 == 1 ? 12 : option % 3 == 2 ? -12 : 0,
            contextElevation: option / 3 % 2 == 1 ? 3 : 0))
        { lastFailure = Reason = gazeReason; return false; }
        if (!arrivalFocus.HasValue)
            for (int i = departureCut ? 0 : 1; i < aims.Length; i++)
                aims[i] = P(visibility.LandscapeFocus(V(body[i]), V(aims[i]), nearClip));
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
                preceding.FieldOfView, departureCut ? 0 : end.ScreenX, departureCut ? -.04f : end.ScreenY, nativeSpline);
            double seconds = proposed.TravelMetres / speed / ScenicJourneyMotion.CruiseFraction(departureCut || !preceding.IsJourney, exitRamp);
            if (arrivalCenter.HasValue) seconds = Math.Max(1, seconds);
            result = (departureCut ? proposed.AsJourneyDeparture(journeyId, seconds) : proposed.AsJourney(journeyId, preceding.Id, seconds))
                .WithJourneyRamps(departureCut || !preceding.IsJourney, exitRamp);
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
    private void Finish(string reason) { Complete = true; Ready = null; Reason = reason; }
    internal void Reset()
    {
        previous = candidate = Ready = null; goal = arrivalFocus = arrivalCenter = null; arrivalRadius = 0; evaluator.Reset(); visibility.Reset();
        departureSpace.Reset(); openings.Clear(); activeOpening = null; planningCarrier = null;
        pendingOpening = -1; openingScreensSinceRoute = 0; openingWaitStarted = 0; planningEpoch = 0;
        openingPosition = openingFocus = default;
        ClearFinalization();
        Complete = WaitingForLoading = departureCut = false; alternative = AttemptCount = 0; Epoch = 0; ValidatedAt = retryAt = 0; lastFailure = "none";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); measured.Dispose(); }
    private static bool WorldPoint(Vector3 point) => TargetRoster.Finite(point) &&
        Mathf.Abs(point.x) <= 20000 && Mathf.Abs(point.y) <= 20000 && Mathf.Abs(point.z) <= 20000;
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
