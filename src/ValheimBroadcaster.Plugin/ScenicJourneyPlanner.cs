using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Plans one bounded continuation ahead of a readable natural shot.
/// Unknown terrain never becomes a route. The first pose and tangent belong to
/// the preceding window; alternatives steer only beyond that preserved seam.</summary>
internal sealed class ScenicJourneyPlanner : IDisposable
{
    private readonly ScenicPathEvaluator evaluator = new ScenicPathEvaluator();
    private readonly ScenicVisibility visibility = new ScenicVisibility("journey-ahead");
    private ScenicShotPlan? previous, candidate;
    private long nextId, journeyId;
    private float length, speed, aspect, nearClip, started;
    private bool moveCarrier, exitRamp, waitingForSurface;
    private int alternative;
    internal bool Complete { get; private set; }
    internal ScenicShotPlan? Ready { get; private set; }
    internal long Epoch { get; private set; }
    internal float ValidatedAt { get; private set; }
    internal string Reason { get; private set; } = "No journey window requested";

    internal void Begin(ScenicShotPlan predecessor, long id, long journey, float metres, float metresPerSecond,
        float cameraAspect, float cameraNearClip, bool moveObserver, bool finalWindow = false)
    {
        Reset(); previous = predecessor; nextId = id; journeyId = journey;
        length = metres; speed = metresPerSecond; aspect = cameraAspect; nearClip = cameraNearClip;
        moveCarrier = moveObserver; exitRamp = finalWindow; started = Time.unscaledTime;
        Reason = "Measuring the next continuous journey window";
    }

    internal void Step()
    {
        if (Complete || previous == null) return;
        if (Time.unscaledTime - started > ScenicJourneyPolicy.PreparationSeconds)
        { Finish("Journey ahead preparation expired; retain the current validated picture"); return; }
        if (candidate == null)
        {
            if (alternative >= 3) { Finish("No continuous readable corridor ahead; return to local scenic coverage"); return; }
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
            using (work)
            {
                if (!TryCandidate(alternative, out candidate)) { if (!waitingForSurface) alternative++; return; }
                evaluator.Begin(candidate!, visibility, aspect, nearClip, moveCarrier);
            }
        }
        if (!evaluator.Step(6)) { Reason = evaluator.Reason; return; }
        ScenicShotPlan? fitted = evaluator.EvaluatedPlan;
        if (evaluator.Accepted && fitted != null)
        {
            ScenicShotSample end = previous.Sample(1), start = fitted.Sample(0);
            // A uniform terrain lift is useful for an ordinary cut, but would
            // move this seam. Never permit fitting to teleport a continuation.
            if (end.CameraPosition.Distance(start.CameraPosition) < .005 && end.AimPosition.Distance(start.AimPosition) < .005)
            {
                Ready = fitted; Epoch = evaluator.SceneEpoch; ValidatedAt = Time.unscaledTime;
                Complete = true; Reason = "Complete continuous window validated; awaiting the exact seam"; return;
            }
            Reason = "Terrain fitting moved the journey seam; try a different observed continuation";
        }
        else Reason = evaluator.Reason;
        candidate = null; alternative++;
    }

    private bool TryCandidate(int option, out ScenicShotPlan? result)
    {
        result = null; waitingForSurface = false;
        ScenicShotPlan preceding = previous!;
        ScenicShotSample end = preceding.Sample(1);
        AutonomyPosition precedingKnot = preceding.IsSpatial ? preceding.CameraWaypoint(preceding.WaypointCount - 2) : preceding.Sample(15d / 16).CameraPosition;
        Vector3 start = V(end.CameraPosition), aim = V(end.AimPosition);
        Vector3 direction = start - V(precedingKnot);
        if (direction.sqrMagnitude < .001f) return false;
        direction.Normalize();
        // Keep rolling movement shallow. A steep terminal lift cannot honestly
        // become a long horizontal journey without a developed transition.
        if (Mathf.Abs(direction.y) > .35f) return false;
        int count = Mathf.Max(3, Mathf.CeilToInt(length / 8f) + 1);
        var body = new AutonomyPosition[count]; var aims = new AutonomyPosition[count];
        float turn = option == 0 ? 0 : option == 1 ? 12f : -12f;
        float step = length / (count - 1);
        Vector3 position = start, focus = aim;
        body[0] = end.CameraPosition; aims[0] = end.AimPosition;
        for (int i = 1; i < count; i++)
        {
            Vector3 forward = Quaternion.AngleAxis(turn * Mathf.Max(0, i - 1) / (count - 1), Vector3.up) * direction;
            Vector3 displacement = forward * step;
            position += displacement; focus += displacement;
            if (!ScenicSurface.TryGroundHeight(position, out float ground) ||
                !ScenicSurface.TryWaterHeight(position, out bool water, out float waterLevel))
            { waitingForSurface = true; Reason = "Journey terrain/water ahead is not observed yet"; return false; }
            float required = Mathf.Max(ground + 3f, water ? waterLevel + 3f : float.NegativeInfinity);
            // Preserve the initial tangent. Subsequent gentle elevation may
            // climb terrain, but cannot jump above a sudden wall or mountain.
            float rise = Mathf.Max(0, required - position.y);
            if (i == 1 && rise > .02f || rise > step * .25f) return false;
            position.y += rise; focus.y += rise;
            if (!WorldPoint(position) || !WorldPoint(focus)) return false;
            body[i] = P(position); aims[i] = P(focus);
        }
        ScenicFeature source = preceding.Feature!;
        var feature = new ScenicFeature(source.Id, source.Kind, aims[count / 2], source.Direction,
            source.Radius, source.Height, source.Confidence, waterDirection: source.WaterDirection);
        try
        {
            ScenicShotPlan proposed = ScenicShotPlan.Spatial(nextId, feature, ScenicShotRole.Traverse, body, aims,
                preceding.FieldOfView, end.ScreenX, end.ScreenY, preceding.UseNativeSpline);
            result = proposed.AsJourney(journeyId, preceding.Id, proposed.TravelMetres / speed /
                ScenicJourneyMotion.CruiseFraction(!preceding.IsJourney, exitRamp)).WithJourneyRamps(!preceding.IsJourney, exitRamp);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private void Finish(string reason) { Complete = true; Ready = null; Reason = reason; }
    internal void Reset()
    { previous = candidate = Ready = null; evaluator.Reset(); visibility.Reset(); Complete = false; alternative = 0; Epoch = 0; ValidatedAt = 0; }
    public void Dispose() { Reset(); evaluator.Dispose(); }
    private static bool WorldPoint(Vector3 point) => TargetRoster.Finite(point) &&
        Mathf.Abs(point.x) <= 20000 && Mathf.Abs(point.y) <= 20000 && Mathf.Abs(point.z) <= 20000;
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
