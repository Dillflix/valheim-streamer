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
    private readonly JourneyObservationViews views;
    private readonly int pendingLimit;
    private readonly bool departure;
    private long epoch, query;
    private float speed, fov, aspect, near, screenX, screenY;
    private Vector3 goal;
    private bool active;
    private long mission;
    private float episodeAt, requestedSpeed;
    private JourneyNavigationRequest? request;
    internal float PlanningSpeed => speed;
    internal bool SelectedProofPending => search.SelectedProofPending;
    internal long ProofRefreshes => search.ProofRefreshes;
    internal long ObservationProgress { get; private set; }
    private const long AdmissionOnwardToken = 1_000_000_000;
    private JourneyNavigationPose? admissionFrom, admissionTo;
    internal float OnwardValidatedAt { get; private set; }
    internal string OnwardReason { get; private set; } = "Onward seam has not been renewed";
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

    internal JourneyNavigationPlanner(string owner, bool independent)
    { departure = independent; pendingLimit = independent ? 1 : 8; views = new JourneyObservationViews(owner, pendingLimit + 1); }

    internal void Begin(long mission, long sceneEpoch, long planId, ScenicShotSample first, Vector3 incoming,
        Vector3 entryAim, Vector3 destination, Vector3 carrier, float metres, float metresPerSecond, float fieldOfView, float cameraAspect, float nearClip)
    {
        Vector3 proposedEntry = V(first.CameraPosition) + incoming.normalized * 2f;
        bool same = active && this.mission == mission && epoch == sceneEpoch && Time.unscaledTime >= episodeAt &&
            Time.unscaledTime - episodeAt < 120 && boundStart.Position.Distance(first.CameraPosition) < .005 &&
            boundStart.Aim.Distance(first.AimPosition) < .005 && boundEntry.Position.Distance(P(proposedEntry)) < .005 &&
            boundEntry.Aim.Distance(P(entryAim)) < .005 && Vector3.Distance(goal, destination) < .05f &&
            Mathf.Abs(requestedSpeed - metresPerSecond) < .001f &&
            Mathf.Abs(fov - fieldOfView) < .001f && Mathf.Abs(aspect - cameraAspect) < .001f &&
            Mathf.Abs(near - nearClip) < .001f && Mathf.Abs(screenX - first.ScreenX) < .001f && Mathf.Abs(screenY - first.ScreenY) < .001f &&
            WithinLoadingHorizon(carrier);
        query = planId;
        if (same)
        {
            if (search.Status == JourneyNavigationStatus.Ready) search.ResumeCandidateValidation();
            Reason = "Resuming the existing connected navigation episode; fresh proof still required"; return;
        }
        Reset(); this.mission = mission; epoch = sceneEpoch; query = planId; goal = destination; speed = 2;
        requestedSpeed = metresPerSecond; episodeAt = Time.unscaledTime;
        fov = fieldOfView; aspect = cameraAspect; near = nearClip; screenX = first.ScreenX; screenY = first.ScreenY;
        Vector3 start = V(first.CameraPosition), aim = V(first.AimPosition), entry = start + incoming.normalized * 2f;
        var opening = new JourneyNavigationPose(first.CameraPosition, first.AimPosition);
        // Both complete poses are immutable continuation constraints. In
        // particular, a new search cannot rotate the first two aim knots.
        var firstEdge = new JourneyNavigationPose(P(entry), P(entryAim));
        boundStart = opening; boundEntry = firstEdge;
        request = new JourneyNavigationRequest(mission, epoch, opening, firstEdge, P(destination), P(carrier),
            targetMetres: departure ? JourneyRunwayPolicy.DepartureGuideMetres(metresPerSecond, metres) : 56, maximumMetres: 64,
            reachMetres: JourneyRunwayPolicy.LocalReachMetres,
            stepMetres: 4, maximumNodes: 512, maximumEdges: 8192, viewDepth: JourneyViewPolicy.ObservationRange, maximumPending: pendingLimit, forNativePreflight: true);
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
        Reason = search.Diagnostics + "; planning " + speed.ToString("F1") + "m/s; " + Reason;
        if (Reason.Length > 600) Reason = search.Diagnostics;
        return status;
    }

    internal void RejectCandidate()
    {
        search.RejectCandidate(); pending.Clear(); views.Reset(); ClearOnward();
        Reason = "Native curve rejected; retaining the connected search frontier";
    }

    internal JourneyNavigationEvidence RenewOnward(ScenicShotPlan plan)
    {
        if (!active || epoch != CameraSpatialScene.Shared.Epoch || search.Result == null)
        { OnwardReason = "The selected navigation context no longer matches the loaded scene"; return JourneyNavigationEvidence.Blocked; }
        if (!search.Result.OnwardPose.HasValue)
        {
            if (!search.Result.AtGoal)
            { OnwardReason = "A continuing route has no measured onward seam"; return JourneyNavigationEvidence.Blocked; }
            OnwardValidatedAt = Time.unscaledTime; OnwardReason = "The local goal needs no onward continuation";
            return JourneyNavigationEvidence.Clear;
        }
        // Renew the seam the final native plan will actually hand to the next
        // planner. Fitting may change its terminal tangent, so the graph's
        // earlier polyline witness alone cannot certify this handoff.
        int last = plan.WaypointCount - 1;
        if (!plan.IsSpatial || last < 1)
        { OnwardReason = "The selected journey has no exact terminal tangent"; return JourneyNavigationEvidence.Blocked; }
        Vector3 end = V(plan.CameraWaypoint(last)), before = V(plan.CameraWaypoint(last - 1));
        float distance = Vector3.Distance(end, before);
        if (distance < .05f)
        { OnwardReason = "The selected journey has a degenerate terminal tangent"; return JourneyNavigationEvidence.Blocked; }
        var from = new JourneyNavigationPose(plan.CameraWaypoint(last), plan.AimWaypoint(last));
        Vector3 bodyTo = end + (end - before) * (2f / distance);
        Vector3 aimTo = V(from.Aim) + (V(from.Aim) - V(plan.AimWaypoint(last - 1))) * (2f / distance);
        if (!TargetRoster.Finite(bodyTo) || !TargetRoster.Finite(aimTo))
        { OnwardReason = "The exact onward seam is nonfinite"; return JourneyNavigationEvidence.Blocked; }
        var to = new JourneyNavigationPose(P(bodyTo), P(aimTo));
        bool same = admissionFrom.HasValue && admissionTo.HasValue &&
            admissionFrom.Value.Position.Distance(from.Position) < .001 && admissionFrom.Value.Aim.Distance(from.Aim) < .001 &&
            admissionTo.Value.Position.Distance(to.Position) < .001 && admissionTo.Value.Aim.Distance(to.Aim) < .001;
        if (!same)
        {
            pending.Remove(AdmissionOnwardToken); views.Release(AdmissionOnwardToken);
            admissionFrom = from; admissionTo = to; OnwardValidatedAt = 0;
        }
        if (OnwardValidatedAt > 0 && Time.unscaledTime - OnwardValidatedAt < 1f)
            return JourneyNavigationEvidence.Clear;
        JourneyNavigationProbe renewal;
        try { renewal = JourneyNavigationProbe.ExactContinuation(AdmissionOnwardToken, from, to); }
        catch (ArgumentOutOfRangeException)
        { OnwardReason = "The exact onward seam exceeds supported local geometry"; return JourneyNavigationEvidence.Blocked; }
        JourneyNavigationObservation observed = Observe(renewal);
        OnwardReason = Reason;
        if (observed.Evidence == JourneyNavigationEvidence.Clear)
        {
            OnwardValidatedAt = (float)observed.ObservedAt;
            if (Time.unscaledTime - OnwardValidatedAt > JourneyNavigationSearch.MaximumEvidenceAge)
            { OnwardReason = "Exact onward evidence expired during renewal"; return JourneyNavigationEvidence.Unknown; }
        }
        return observed.Evidence;
    }
    private void ClearOnward()
    { admissionFrom = admissionTo = null; OnwardValidatedAt = 0; OnwardReason = "Onward seam has not been renewed"; }

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
        if (!retained || !double.IsPositiveInfinity(work!.EvidenceAt) && Time.unscaledTime - work.EvidenceAt > JourneyNavigationSearch.MaximumEvidenceAge)
        {
            // Each lane bounds its unresolved probes. One additional slot
            // belongs to selected-path proof refresh while the unrelated
            // frontier remains pending. Never evict work
            // that the Core scheduler still owns.
            if (!retained && pending.Count >= pendingLimit + 1) return Unknown(probe, "Navigation observation backpressure retains existing partial work");
            views.Release(probe.Id);
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
            observation.Position = point; observation.Positioned = true; currentDemand = P(point); ObservationProgress++;
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
                if (!views.TryGet(probe.Id, epoch, observation.Position, out ScenicVisibility visibility))
                    return Unknown(probe, views.Reason);
                visibility.QueryId = query; visibility.QueryStage = "navigation " + probe.Id + " gaze " + observation.Option;
                var view = visibility.EvaluateJourney(observation.Position, observation.Rotation, fov, aspect, near);
                // Positive body evidence outranks missing unrelated survey
                // coverage; changing gaze cannot move an intersecting camera.
                if (view.InsideVegetation) return Blocked(probe, view.View.Reason);
                if (!view.CameraClear) { views.Release(probe.Id); observation.Option++; continue; }
                if (!view.SceneReady || !view.SurveyComplete || view.View.Status == JourneyViewStatus.Unknown)
                    return Unknown(probe, view.View.Reason);
                if (!view.View.Usable)
                { views.Release(probe.Id); observation.Option++; continue; }
                if (!IdleShots.Ready(view.ReadinessPoint)) return Unknown(probe, "Navigation view has not been acquired");
                observation.Quality = view.View.Score; observation.EvidenceAt = Math.Min(observation.EvidenceAt, physical.ObservedAt);
                observation.ViewSelected = true; views.Release(probe.Id); ObservationProgress++;
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
                    observation.EvidenceAt = Math.Min(observation.EvidenceAt, corridor.ObservedAt); observation.Swept = true; ObservationProgress++;
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
                if (!views.TryGet(probe.Id, epoch, middle, out ScenicVisibility visibility))
                    return Unknown(probe, views.Reason);
                visibility.QueryId = query; visibility.QueryStage = "navigation " + probe.Id + " midpoint";
                var view = visibility.EvaluateJourney(middle, Quaternion.Slerp(startRotation, observation.Rotation, .5f), fov, aspect, near);
                if (view.InsideVegetation) return Blocked(probe, view.View.Reason);
                if (!view.CameraClear) { views.Release(probe.Id); observation.Option++; observation.ViewSelected = observation.Swept = false; continue; }
                if (!view.SceneReady || !view.SurveyComplete || view.View.Status == JourneyViewStatus.Unknown)
                    return Unknown(probe, view.View.Reason);
                if (!view.View.Usable)
                { views.Release(probe.Id); observation.Option++; observation.ViewSelected = observation.Swept = false; continue; }
                if (!IdleShots.Ready(view.ReadinessPoint)) return Unknown(probe, "Intermediate navigation view has not been acquired");
                observation.Quality = Math.Min(observation.Quality, view.View.Score); observation.ViewSample++; ObservationProgress++;
            }
            pending.Remove(probe.Id); views.Release(probe.Id);
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
        Reason = reason; pending.Remove(probe.Id); views.Release(probe.Id);
        Trace(probe, "Blocked", reason, V(probe.Position), V(probe.RequiredAim ?? probe.From.Aim));
        return new JourneyNavigationObservation(JourneyNavigationEvidence.Blocked,
            new JourneyNavigationPose(probe.Position, probe.RequiredAim ?? probe.From.Aim), 0, epoch, Time.unscaledTime);
    }
    private void Trace(JourneyNavigationProbe probe, string evidence, string reason, Vector3 body, Vector3 aim)
    {
        if (!ScenicTrace.Enabled) return;
        CameraTraceRecord row = ScenicTrace.Row("journey-navigation-edge").Field("plan", query).Field("probe", probe.Id)
            .Field("evidence", evidence).Field("onward", probe.IsOnward).Field("entry", probe.IsEntry).Field("start", probe.IsStart)
            .Field("requiredAim", probe.RequiredAim.HasValue).Field("planningCruise", speed).Field("reason", reason);
        ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(row,
            "fromCamera", V(probe.From.Position)), "fromFocus", V(probe.From.Aim)), "camera", body), "focus", aim));
    }
    internal void Reset() { search = new JourneyNavigationSearch(); pending.Clear(); views.Reset(); currentDemand = null; active = false; ObservationProgress = 0; ClearOnward(); }
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
