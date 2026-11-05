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
    private ScenicShotPlan? previous, candidate;
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
    internal ScenicShotPlan? Ready { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal string Reason { get; private set; } = "No journey corridor requested";

    internal void Begin(ScenicShotPlan predecessor, long id, long journey, float metres, float metresPerSecond,
        float cameraAspect, float cameraNearClip, bool moveObserver, bool finalWindow = false,
        AutonomyPosition? objectiveGoal = null, AutonomyPosition? observedArrivalFocus = null, bool departureCut = false,
        AutonomyPosition? arrivalCenter = null, double arrivalRadius = 0, bool? nativeSplineOverride = null)
    {
        Reset(); previous = predecessor; nextId = id; journeyId = journey;
        // Existing spatial routes support <=80m/16 knots. The programme owns
        // kilometres by joining local certificates, not by bypassing that bound.
        length = Mathf.Clamp(metres, 1, 64); speed = metresPerSecond; aspect = cameraAspect; nearClip = cameraNearClip;
        moveCarrier = moveObserver; exitRamp = finalWindow; goal = objectiveGoal; arrivalFocus = observedArrivalFocus;
        this.arrivalCenter = arrivalCenter; this.arrivalRadius = arrivalRadius;
        this.departureCut = departureCut; nativeSpline = nativeSplineOverride ?? predecessor.UseNativeSpline; started = Time.unscaledTime;
        Reason = "Measuring goal-directed journey corridors";
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
        if (Time.unscaledTime - started > ScenicJourneyPolicy.PreparationSeconds)
        { Finish((WaitingForLoading ? "Journey loading horizon did not become ready" : "Journey corridor search expired") + "; last evidence: " + lastFailure); return; }
        if (candidate == null)
        {
            if (alternative >= turns.Length * 3)
            { Finish("No validated route among the goal-directed lateral/elevation alternatives; last evidence: " + lastFailure); return; }
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                AttemptCount++;
                if (!TryCandidate(alternative, out candidate))
                {
                    if (WaitingForLoading) retryAt = Time.unscaledTime + .25f;
                    else alternative++;
                    return;
                }
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
                // Duration follows the distance that the native body actually
                // travels, including its rounded turns, rather than the knot
                // polygon. Retiming changes no normalized pose or certificate.
                using var measured = new ScenicSplinePath();
                if (!measured.Prepare(fitted, 1))
                { Finish("Validated journey native path could not be measured"); return; }
                double distance = fitted.UseNativeSpline ? measured.Length : fitted.TravelMetres;
                double duration = distance / speed /
                    ScenicJourneyMotion.CruiseFraction(fitted.JourneyEaseIn, fitted.JourneyEaseOut);
                if (arrivalCenter.HasValue) duration = Math.Max(1, duration);
                if (!NativeJourneyGaze.WithinRate(measured, duration))
                {
                    lastFailure = "Native journey gaze exceeds the bounded angular rate at the selected cruise";
                    candidate = null; evaluator.Reset(); alternative++;
                    Reason = "Trying another corridor with a gentler viewing path"; return;
                }
                Ready = fitted.WithDuration(duration);
                Epoch = evaluator.SceneEpoch; ValidatedAt = Time.unscaledTime;
                Complete = true; WaitingForLoading = false;
                Reason = "Validated " + fitted.TravelMetres.ToString("F0") + "m goal-directed corridor after " + AttemptCount + " proposals"; return;
            }
            lastFailure = "Terrain fitting moved the exact journey seam";
        }
        else lastFailure = evaluator.Reason;
        ScenicRejectionKind failure = evaluator.FailureKind;
        bool wait = failure == ScenicRejectionKind.Geometry || failure == ScenicRejectionKind.Acquisition ||
            failure == ScenicRejectionKind.CarrierRange || failure == ScenicRejectionKind.SurveyIncomplete;
        candidate = null; evaluator.Reset();
        WaitingForLoading = wait;
        if (wait)
        {
            // Keep the same pending corridor while the carrier advances and the
            // persistent observations mature. A missing tile is not a bad turn.
            retryAt = Time.unscaledTime + .25f;
            Reason = "Waiting for the journey loading/observation horizon: " + lastFailure;
        }
        else { alternative++; Reason = "Trying another goal-directed corridor: " + lastFailure; }
    }

    private bool TryCandidate(int option, out ScenicShotPlan? result)
    {
        result = null; WaitingForLoading = false;
        ScenicShotPlan preceding = previous!;
        ScenicShotSample end = preceding.Sample(1);
        Vector3 start = V(end.CameraPosition);
        Vector3 incoming = departureCut && goal.HasValue ? Vector3.ProjectOnPlane(V(goal.Value) - start, Vector3.up).normalized : TerminalDirection(preceding);
        if (incoming.sqrMagnitude < .001f || Mathf.Abs(incoming.y) > .85f)
        { lastFailure = Reason = "Incoming camera path has no usable travelling tangent"; return false; }
        Vector3 forward = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        Vector3 desired = goal.HasValue ? Vector3.ProjectOnPlane(V(goal.Value) - start, Vector3.up).normalized : V(ObjectiveHeading(preceding));
        if (desired.sqrMagnitude < .5f) desired = forward;
        Vector3 targetHeading = Quaternion.AngleAxis(turns[option % turns.Length], Vector3.up) * desired;
        float loft = option / turns.Length * 8f;
        var positions = new List<Vector3>(16) { start };
        var floors = new List<float>(16);
        if (!MeasureFloor(start, out float firstFloor)) return false;
        floors.Add(firstFloor);
        float clearance = Mathf.Clamp(start.y - firstFloor, .25f, 18f);
        float travelled = 0;
        float stepLength = Mathf.Max(4, (length - Math.Min(2, length)) / 13f);
        Vector3 position = start;
        if (arrivalCenter.HasValue && arrivalFocus.HasValue)
        {
            if (!JourneyArrivalGeometry.TryBuild(end.CameraPosition, P(incoming), arrivalCenter.Value, arrivalRadius,
                arrivalFocus.Value, length, Mathf.Sin(turns[option % turns.Length] * Mathf.Deg2Rad), out var approach, out string approachReason))
            { lastFailure = Reason = approachReason; return false; }
            for (int i = 1; i < approach.Length; i++)
            {
                Vector3 point = V(approach[i]);
                if (!MeasureFloor(point, out float floor)) return false;
                positions.Add(point); floors.Add(floor);
            }
        }
        else while (travelled < length - .01f && positions.Count < 16)
        {
            float step = Mathf.Min(length - travelled, positions.Count == 1 ? 2f : stepLength);
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
        if (!JourneyTerrainProfile.TryFit(positions, floors, clearance, loft, out Vector3[] fitted, out string fitReason))
        { lastFailure = Reason = fitReason; return false; }
        var body = new AutonomyPosition[fitted.Length];
        body[0] = end.CameraPosition;
        for (int i = 1; i < fitted.Length; i++)
        {
            if (!WorldPoint(fitted[i])) { lastFailure = Reason = "Journey composition exceeds world bounds"; return false; }
            body[i] = P(fitted[i]);
        }
        AutonomyPosition startingAim = departureCut ? P(start + Vector3.ProjectOnPlane(incoming, Vector3.up).normalized *
            Mathf.Clamp(speed * 3.5f, 14, 28) + Vector3.down * 2) : end.AimPosition;
        if (!JourneyGazePolicy.TryBuild(body, startingAim, speed, preceding.Feature?.WaterDirection,
            arrivalFocus, out AutonomyPosition[] aims, out string gazeReason))
        { lastFailure = Reason = gazeReason; return false; }
        // Lateral detours may be useful, but cannot consume the programme by
        // circling or travelling backwards relative to its explicit objective.
        Vector3 displacement = V(body[body.Length - 1]) - start;
        if (Vector3.Dot(Vector3.ProjectOnPlane(displacement, Vector3.up), desired) < length * .15f)
        { lastFailure = Reason = "Candidate does not develop the journey objective"; return false; }
        ScenicFeature source = preceding.Feature!;
        var feature = new ScenicFeature(source.Id, source.Kind, aims[aims.Length / 2], P(desired),
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        try
        {
            ScenicShotPlan proposed = ScenicShotPlan.Spatial(nextId, feature, ScenicShotRole.Traverse, body, aims,
                preceding.FieldOfView, end.ScreenX, end.ScreenY, nativeSpline);
            double seconds = proposed.TravelMetres / speed / ScenicJourneyMotion.CruiseFraction(departureCut || !preceding.IsJourney, exitRamp);
            if (arrivalCenter.HasValue) seconds = Math.Max(1, seconds);
            result = (departureCut ? proposed.AsJourneyDeparture(journeyId, seconds) : proposed.AsJourney(journeyId, preceding.Id, seconds))
                .WithJourneyRamps(departureCut || !preceding.IsJourney, exitRamp);
            return true;
        }
        catch (ArgumentException)
        { lastFailure = Reason = "Candidate exceeded native spatial path or composition bounds"; return false; }
    }

    private bool MeasureFloor(Vector3 point, out float floor)
    {
        floor = 0;
        if (!ScenicSurface.TryGroundHeight(point, out float ground) ||
            !ScenicSurface.TryWaterHeight(point, out bool water, out float waterLevel))
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
        Complete = WaitingForLoading = departureCut = false; alternative = AttemptCount = 0; Epoch = 0; ValidatedAt = retryAt = 0; lastFailure = "none";
    }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool WorldPoint(Vector3 point) => TargetRoster.Finite(point) &&
        Mathf.Abs(point.x) <= 20000 && Mathf.Abs(point.y) <= 20000 && Mathf.Abs(point.z) <= 20000;
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
