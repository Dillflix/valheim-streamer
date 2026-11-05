using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Incremental bridge between the bounded connected search and actual
/// loaded Unity space. An edge is proposed only after body/near-plane sweeps and
/// locally readable angular views; the resulting polyline is never a native
/// spline certificate. ScenicPathEvaluator still validates that exact curve.</summary>
internal sealed class JourneyNavigationPlanner
{
    private JourneyNavigationSearch search = new JourneyNavigationSearch();
    private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
    private readonly ScenicVisibility visibility;
    private long epoch, query;
    private float speed, fov, aspect, near, screenX, screenY;
    private Vector3 goal;
    private bool active;
    private long mission;
    private float episodeAt, requestedMetres, requestedSpeed;
    private JourneyNavigationRequest? request;
    internal float PlanningSpeed => speed;
    private JourneyNavigationPose boundStart, boundEntry;
    private AutonomyPosition? currentDemand;
    internal string Reason { get; private set; } = "No navigation search";
    internal JourneyNavigationPath? Result => search.Result;
    internal bool Active => active;
    internal bool MatchesOpening(Vector3 position, Vector3 aim) => active &&
        Vector3.Distance(V(boundStart.Position), position) < .005f && Vector3.Distance(V(boundStart.Aim), aim) < .005f;

    private sealed class Pending
    {
        internal float LastServiced;
        internal Vector3 Position, Aim;
        internal Quaternion Rotation;
        internal int Option, ViewSample;
        internal bool Positioned, ViewSelected, Swept;
        internal double EvidenceAt = double.PositiveInfinity, Quality = 1;
        internal Pending(float now) { LastServiced = now; }
    }

    internal JourneyNavigationPlanner(ScenicVisibility observations) { visibility = observations; }

    internal void Begin(long mission, long sceneEpoch, long planId, ScenicShotSample first, Vector3 incoming,
        Vector3 entryAim, Vector3 destination, Vector3 carrier, float metres, float metresPerSecond, float fieldOfView, float cameraAspect, float nearClip)
    {
        Vector3 proposedEntry = V(first.CameraPosition) + incoming.normalized * 2f;
        bool same = active && this.mission == mission && epoch == sceneEpoch && Time.unscaledTime >= episodeAt &&
            Time.unscaledTime - episodeAt < 120 && boundStart.Position.Distance(first.CameraPosition) < .005 &&
            boundStart.Aim.Distance(first.AimPosition) < .005 && boundEntry.Position.Distance(P(proposedEntry)) < .005 &&
            boundEntry.Aim.Distance(P(entryAim)) < .005 && Vector3.Distance(goal, destination) < .05f &&
            Mathf.Abs(requestedMetres - metres) < .05f && Mathf.Abs(requestedSpeed - metresPerSecond) < .001f &&
            Mathf.Abs(fov - fieldOfView) < .001f && Mathf.Abs(aspect - cameraAspect) < .001f &&
            Mathf.Abs(near - nearClip) < .001f && Mathf.Abs(screenX - first.ScreenX) < .001f && Mathf.Abs(screenY - first.ScreenY) < .001f &&
            WithinLoadingHorizon(carrier);
        query = planId;
        if (same)
        {
            if (search.Status == JourneyNavigationStatus.Ready) search.ResumeCandidateValidation();
            Reason = "Resuming the existing connected navigation episode; fresh proof still required"; return;
        }
        Reset(); this.mission = mission; epoch = sceneEpoch; query = planId; goal = destination; speed = metresPerSecond;
        requestedMetres = metres; requestedSpeed = metresPerSecond; episodeAt = Time.unscaledTime;
        fov = fieldOfView; aspect = cameraAspect; near = nearClip; screenX = first.ScreenX; screenY = first.ScreenY;
        Vector3 start = V(first.CameraPosition), aim = V(first.AimPosition), entry = start + incoming.normalized * 2f;
        var opening = new JourneyNavigationPose(first.CameraPosition, first.AimPosition);
        // Both complete poses are immutable continuation constraints. In
        // particular, a new search cannot rotate the first two aim knots.
        var firstEdge = new JourneyNavigationPose(P(entry), P(entryAim));
        boundStart = opening; boundEntry = firstEdge;
        request = new JourneyNavigationRequest(mission, epoch, opening, firstEdge, P(destination), P(carrier),
            targetMetres: Math.Max(8, Math.Min(56, metres)), maximumMetres: Math.Min(64, Math.Max(12, metres)),
            reachMetres: JourneyRunwayPolicy.LocalReachMetres,
            stepMetres: 4, maximumNodes: 512, maximumEdges: 8192, viewDepth: JourneyViewPolicy.ObservationRange, maximumPending: 8);
        search.Begin(request, Time.unscaledTime);
        active = true; Reason = "Searching connected loaded camera space";
    }

    internal JourneyNavigationStatus Step()
    {
        JourneyNavigationStatus status = search.Status;
        for (int i = 0; i < 8 && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic) && CameraSpatialScene.Shared.BackgroundQueryAvailable; i++)
        {
            status = search.Step(1, Time.unscaledTime, CameraSpatialScene.Shared.Epoch, Observe);
            if (status != JourneyNavigationStatus.Searching) break;
        }
        if ((status == JourneyNavigationStatus.Exhausted || status == JourneyNavigationStatus.BudgetExhausted) && speed > 2.001f)
        {
            speed = (float)JourneyNavigationGazePolicy.SlowerTier(speed); pending.Clear();
            search.Begin(request!, Time.unscaledTime);
            Reason = "Replanning the connected route at " + speed.ToString("F1") + "m/s to allow bounded forward gaze through turns";
            return JourneyNavigationStatus.Searching;
        }
        Reason = search.Diagnostics + "; planning " + speed.ToString("F1") + "m/s; " + Reason;
        if (Reason.Length > 600) Reason = search.Diagnostics;
        return status;
    }

    internal void RejectCandidate()
    {
        search.RejectCandidate(); pending.Clear();
        Reason = "Native curve rejected; retaining the connected search frontier";
    }

    internal bool WithinLoadingHorizon(Vector3 carrier)
    {
        if (!active) return true;
        AutonomyPosition observer = P(carrier);
        if (!JourneyLoadingContext.ContainsPoint(boundStart.Position, observer, JourneyViewPolicy.ObservationRange) ||
            !JourneyLoadingContext.ContainsPoint(boundEntry.Position, observer, JourneyViewPolicy.ObservationRange)) return false;
        if (search.Result != null)
        {
            foreach (JourneyNavigationPose pose in search.Result.Poses)
                if (!JourneyLoadingContext.ContainsPoint(pose.Position, observer, JourneyViewPolicy.ObservationRange)) return false;
            if (search.Result.OnwardPose.HasValue && !JourneyLoadingContext.ContainsPoint(search.Result.OnwardPose.Value.Position, observer, JourneyViewPolicy.ObservationRange)) return false;
            return true;
        }
        return !currentDemand.HasValue || JourneyLoadingContext.ContainsPoint(currentDemand.Value, observer, JourneyViewPolicy.ObservationRange);
    }

    private JourneyNavigationObservation Observe(JourneyNavigationProbe probe)
    {
        currentDemand = probe.Position;
        if (!CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic))
            return Unknown(probe, "Connected navigation work deferred");
        bool retained = pending.TryGetValue(probe.Id, out Pending? work);
        if (!retained || Time.unscaledTime - work!.LastServiced > 2f ||
            !double.IsPositiveInfinity(work.EvidenceAt) && Time.unscaledTime - work.EvidenceAt > JourneyNavigationSearch.MaximumEvidenceAge)
        {
            // Core admits at most eight unresolved probes. One additional
            // slot belongs to its selected-path proof refresh, which may run
            // while the unrelated frontier remains pending. Never evict work
            // that the Core scheduler still owns.
            if (!retained && pending.Count >= 9) return Unknown(probe, "Navigation observation backpressure retains existing partial work");
            work = new Pending(Time.unscaledTime); pending[probe.Id] = work;
        }
        Pending observation = work!;
        observation.LastServiced = Time.unscaledTime;
        Vector3 from = V(probe.From.Position), fromAim = V(probe.From.Aim);
        if (!observation.Positioned)
        {
            Vector3 point = V(probe.Position);
            if (!ScenicSurface.TryGroundHeight(point, out float ground) ||
                !ScenicSurface.TryWaterHeight(point, out bool water, out float waterHeight, stableEnvelope: true))
                return Unknown(probe, "Navigation surface observation is unavailable");
            float floor = Mathf.Max(ground + ScenicSurfacePolicy.BodyGroundClearance,
                water ? waterHeight + ScenicSurfacePolicy.BodyWaterClearance : float.NegativeInfinity);
            if (point.y < floor + (probe.IsStart || probe.IsEntry || probe.IsOnward ? 0 : .15f))
            {
                if (probe.IsStart || probe.IsEntry || probe.IsOnward) return Blocked(probe, "Exact incoming seam is below its measured surface envelope");
                point.y = floor + .15f;
            }
            float horizontal = Vector3.ProjectOnPlane(point - from, Vector3.up).magnitude;
            if (!probe.IsStart && (horizontal < .1f || Math.Abs(point.y - from.y) > horizontal * JourneyTerrainProfile.MaximumGrade + .001f))
                return Blocked(probe, "Navigation edge exceeds the bounded climb grade");
            observation.Position = point; observation.Positioned = true; currentDemand = P(point);
        }
        while (CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic))
        {
            if (!observation.ViewSelected)
            {
                int alternatives = probe.RequiredAim.HasValue ? 1 : 3;
                if (observation.Option >= alternatives) return Blocked(probe, "No readable bounded gaze for this connected body edge");
                if (probe.RequiredAim.HasValue)
                {
                    observation.Aim = V(probe.RequiredAim.Value);
                    if (!probe.IsStart && !JourneyNavigationGazePolicy.ForwardCompatible(probe.From.Position, P(observation.Position), P(observation.Aim)))
                        return Blocked(probe, "Exact viewing seam faces away from its travel direction");
                }
                else if (JourneyNavigationGazePolicy.TryAim(probe.From, P(observation.Position), P(goal), speed, observation.Option, out AutonomyPosition chosenAim))
                    observation.Aim = V(chosenAim);
                else { observation.Option++; continue; }
                observation.Rotation = ScenicVisibility.Compose(observation.Position, observation.Aim, screenX, screenY, fov, aspect);
                CameraSpaceObservation physical = CameraSpatialScene.Shared.ObservePoint(observation.Position, observation.Rotation,
                    fov, aspect, near, CameraSpaceUse.Journey, true);
                if (!physical.Known) return Unknown(probe, physical.Reason);
                if (!physical.Clear) { observation.Option++; continue; }
                visibility.QueryId = query; visibility.QueryStage = "navigation " + probe.Id + " gaze " + observation.Option;
                var view = visibility.EvaluateJourney(observation.Position, observation.Rotation, fov, aspect, near);
                if (!view.SceneReady || !view.SurveyComplete || view.View.Status == JourneyViewStatus.Unknown)
                    return Unknown(probe, view.View.Reason);
                if (!view.CameraClear || view.InsideVegetation || !view.View.Readable)
                { observation.Option++; continue; }
                if (!IdleShots.Ready(view.ReadinessPoint)) return Unknown(probe, "Navigation view has not been acquired");
                observation.Quality = view.View.Score; observation.EvidenceAt = Math.Min(observation.EvidenceAt, physical.ObservedAt);
                observation.ViewSelected = true;
            }
            if (!observation.Swept)
            {
                if (probe.IsStart) observation.Swept = true;
                else
                {
                    Quaternion firstRotation = ScenicVisibility.Compose(from, fromAim, screenX, screenY, fov, aspect);
                    CameraSpaceObservation corridor = CameraSpatialScene.Shared.ObserveSegment(from, firstRotation,
                        observation.Position, observation.Rotation, fov, aspect, near, CameraSpaceUse.Journey, true);
                    if (!corridor.Known) return Unknown(probe, corridor.Reason);
                    if (!corridor.Clear)
                    { observation.Option++; observation.ViewSelected = false; continue; }
                    observation.EvidenceAt = Math.Min(observation.EvidenceAt, corridor.ObservedAt); observation.Swept = true;
                }
            }
            // A clear endpoint is not enough: inspect the intermediate picture
            // before the full native spline pass. Physical sweeps above use
            // <=.75m steps and include the actual rotating near rectangle.
            if (!probe.IsStart && observation.ViewSample == 0)
            {
                if (!CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Scenic)) break;
                Vector3 middle = Vector3.Lerp(from, observation.Position, .5f);
                Quaternion startRotation = ScenicVisibility.Compose(from, fromAim, screenX, screenY, fov, aspect);
                visibility.QueryStage = "navigation " + probe.Id + " midpoint";
                var view = visibility.EvaluateJourney(middle, Quaternion.Slerp(startRotation, observation.Rotation, .5f), fov, aspect, near);
                if (!view.SceneReady || !view.SurveyComplete || view.View.Status == JourneyViewStatus.Unknown)
                    return Unknown(probe, view.View.Reason);
                if (!view.CameraClear || view.InsideVegetation || !view.View.Readable)
                { observation.Option++; observation.ViewSelected = observation.Swept = false; continue; }
                if (!IdleShots.Ready(view.ReadinessPoint)) return Unknown(probe, "Intermediate navigation view has not been acquired");
                observation.Quality = Math.Min(observation.Quality, view.View.Score); observation.ViewSample++;
            }
            pending.Remove(probe.Id);
            Reason = "Connected camera edge and viewing neighbourhood observed";
            var result = new JourneyNavigationObservation(JourneyNavigationEvidence.Clear,
                new JourneyNavigationPose(P(observation.Position), P(observation.Aim)), observation.Quality, epoch, observation.EvidenceAt);
            Trace(probe, "Clear", Reason, observation.Position, observation.Aim);
            return result;
        }
        return Unknown(probe, "Navigation observation resumed next frame");
    }

    private JourneyNavigationObservation Unknown(JourneyNavigationProbe probe, string reason)
    {
        Reason = reason;
        return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown,
            new JourneyNavigationPose(probe.Position, probe.RequiredAim ?? probe.From.Aim), 0, epoch, Time.unscaledTime);
    }
    private JourneyNavigationObservation Blocked(JourneyNavigationProbe probe, string reason)
    {
        Reason = reason; pending.Remove(probe.Id);
        Trace(probe, "Blocked", reason, V(probe.Position), V(probe.RequiredAim ?? probe.From.Aim));
        return new JourneyNavigationObservation(JourneyNavigationEvidence.Blocked,
            new JourneyNavigationPose(probe.Position, probe.RequiredAim ?? probe.From.Aim), 0, epoch, Time.unscaledTime);
    }
    private void Trace(JourneyNavigationProbe probe, string evidence, string reason, Vector3 body, Vector3 aim)
    {
        if (!ScenicTrace.Enabled) return;
        ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Row("journey-navigation-edge")
            .Field("plan", query).Field("probe", probe.Id).Field("evidence", evidence).Field("onward", probe.IsOnward)
            .Field("reason", reason), "camera", body), "focus", aim));
    }
    internal void Reset() { search = new JourneyNavigationSearch(); pending.Clear(); currentDemand = null; active = false; }
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
