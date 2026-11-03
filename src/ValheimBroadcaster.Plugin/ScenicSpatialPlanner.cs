using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Constructs a scenic shot from observed viewpoints and connected
/// camera space. It never starts by prescribing a complete motion template.
/// The resulting native trajectory is a proposal and still requires the same
/// full-path preflight and final live-pose validation as any other scenic shot.</summary>
internal sealed class ScenicSpatialPlanner
{
    private const int MaximumNodes = 48, MaximumProbes = 96, MaximumSeeds = 16;
    private const float EdgeStep = 5f;
    private readonly ScenicVisibility visibility = new ScenicVisibility("spatial");
    private readonly CameraSpatialPlanner search = new CameraSpatialPlanner();
    private readonly CameraSpatialPlanner fallbackSearch = new CameraSpatialPlanner();
    private readonly List<CameraSpatialNode> nodes = new List<CameraSpatialNode>(MaximumNodes);
    private readonly List<CameraSpatialEdge> edges = new List<CameraSpatialEdge>(MaximumNodes * 2);
    private readonly List<ScenicPose> poses = new List<ScenicPose>(MaximumNodes);
    private readonly Queue<Probe> frontier = new Queue<Probe>();
    private readonly HashSet<Vector3Int> considered = new HashSet<Vector3Int>();
    private readonly ShotProjection.Point[] corners = new ShotProjection.Point[8];
    private readonly List<AutonomyPosition> bodyRoute = new List<AutonomyPosition>(16), aimRoute = new List<AutonomyPosition>(16);
    private ScenicFeature? feature;
    private ScenicShotRole role;
    private Probe? current;
    private Vector3 origin, focus, seedDirection;
    private Vector3? precedingCamera;
    private Bounds? subject;
    private CameraSpatialRoute? fallbackRoute;
    private long id, epoch;
    private float fov, aspect, nearClip, screenX, screenY, started, desiredDistance, desiredHeight;
    private bool moveCarrier, native, graphSearching;
    private CameraQueryPriority priority;
    private int seedIndex, nextGraphAt;

    internal bool Complete { get; private set; }
    internal ScenicShotPlan? Plan { get; private set; }
    internal float Score { get; private set; }
    internal int Attempts { get; private set; }
    internal int Rejected { get; private set; }
    internal string Reason { get; private set; } = "Awaiting a spatial scenic subject";
    internal string Diagnostics => $"Spatial scenery: {Reason}; probes {Attempts}, clear viewpoints {nodes.Count}, " +
        $"corridors {edges.Count / 2}, rejected {Rejected}; {CameraSpatialScene.Shared.Diagnostics}";

    private sealed class Probe
    {
        internal Vector3 Position;
        internal readonly int Parent;
        internal bool Fitted, Raised;
        internal int Stage, ViewSample;
        internal float WaitingAt = -1, Quality = 1, Clearance = .25f;
        internal double ObservedAt;
        internal ScenicPose Pose;
        internal Probe(Vector3 position, int parent) { Position = position; Parent = parent; }
    }

    internal void Begin(long planId, ScenicFeature source, ScenicShotRole shotRole, Vector3 regionOrigin,
        float preferredFov, bool composition, bool nativeSpline, float cameraAspect, float cameraNearClip,
        bool moveObserver, Vector3? lastCamera = null, bool urgent = false)
    {
        Reset();
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (planId <= 0 || !Enum.IsDefined(typeof(ScenicShotRole), shotRole) || !TargetRoster.Finite(regionOrigin) ||
            !Finite(preferredFov) || !Finite(cameraAspect) || cameraAspect <= 0 ||
            !Finite(cameraNearClip) || cameraNearClip <= 0 || lastCamera.HasValue && !TargetRoster.Finite(lastCamera.Value))
            throw new ArgumentOutOfRangeException(nameof(planId));
        id = planId; feature = source; role = shotRole; origin = regionOrigin;
        aspect = cameraAspect; nearClip = cameraNearClip; moveCarrier = moveObserver; native = nativeSpline;
        precedingCamera = lastCamera; epoch = CameraSpatialScene.Shared.Epoch; started = Time.unscaledTime;
        priority = urgent ? CameraQueryPriority.OnAir : CameraQueryPriority.Background;
        focus = FromCore(source.Center);
        fov = role == ScenicShotRole.Detail ? Mathf.Clamp(preferredFov - 22f, 40f, 55f) :
            role == ScenicShotRole.Establish ? Mathf.Clamp(preferredFov, 58f, 78f) : Mathf.Clamp(preferredFov - 9f, 50f, 69f);
        screenX = composition ? .06f : 0; screenY = composition ? role == ScenicShotRole.Establish ? -.08f : -.035f : 0;
        bool objectSubject = source.Kind == ScenicFeatureKind.Structure || source.Kind == ScenicFeatureKind.Rock;
        if (objectSubject)
        {
            Vector3 size = new Vector3(source.Radius * 2, Mathf.Max(1, source.Height), source.Radius * 2);
            PlaceSnapshot places = PlaceSurvey.Shared.Snapshot;
            PlaceNode? place = places.Fresh(Time.unscaledTime) ? places.Find(source.Id) : null;
            // Exported subjects have already removed buried/submerged vertical
            // extent. Keep that exposed Y range, and the actual measured X/Z
            // footprint rather than inventing a square around a thin building.
            if (place != null) { size.x = place.Bounds.Width; size.z = place.Bounds.Depth; }
            subject = new Bounds(focus, size);
        }
        // Lens and measured extent define framing distances. The local carrier
        // boundary is not stretched to make a large settlement fit: that subject
        // must yield to a smaller building/district supplied by the place model.
        desiredDistance = Mathf.Clamp(source.Radius * 1.3f + 8f, 12f, 42f);
        desiredHeight = role == ScenicShotRole.Establish ? Mathf.Clamp(source.Height * .22f + 4f, 4f, 16f) :
            role == ScenicShotRole.Reveal ? 3f : role == ScenicShotRole.Detail ? 2f : 4f;
        seedDirection = lastCamera.HasValue ? Vector3.ProjectOnPlane(lastCamera.Value - focus, Vector3.up) : FromCore(source.Direction);
        if (seedDirection.sqrMagnitude < .01f) seedDirection = FromCore(source.Direction);
        seedDirection.Normalize(); nextGraphAt = 4;
        if (subject.HasValue)
        {
            desiredDistance = float.PositiveInfinity;
            for (int i = 0; i < 8; i++) desiredDistance = Mathf.Min(desiredDistance,
                FramingDistance(Quaternion.AngleAxis(i * 45f, Vector3.up) * seedDirection));
        }
        if (desiredDistance > 78f)
        { Finish("Subject extent exceeds locally observable framing range; use a building or district"); return; }
        Reason = "Finding a measured clear viewpoint";
    }

    internal bool Step(int queryBudget = 4)
    {
        if (queryBudget < 1 || queryBudget > 16) throw new ArgumentOutOfRangeException(nameof(queryBudget));
        if (Complete || feature == null) return Complete;
        if (epoch != CameraSpatialScene.Shared.Epoch)
        { Finish("Spatial scene changed; discard the prior observation graph"); return true; }
        long begin = Stopwatch.GetTimestamp();
        bool expired = ScenicObservationPolicy.SearchExpired(Time.unscaledTime, started, priority == CameraQueryPriority.OnAir);
        TraceBudget("step", current, expired);
        if (graphSearching)
        {
            search.Step(128);
            if (TryResult()) return true;
            if (!search.Complete && !expired) return false;
            graphSearching = false;
            if (expired)
            {
                if (TryFallback()) return true;
                Finish("No useful route within the bounded local spatial search"); return true;
            }
            nextGraphAt = nodes.Count + 4;
        }
        if (expired || nodes.Count >= MaximumNodes || Attempts >= MaximumProbes)
        {
            if (edges.Count > 0) { BeginGraph(); search.Step(512); if (TryResult() || TryFallback()) return true; }
            Finish($"Camera-route search expired: {nodes.Count} clear viewpoints, {edges.Count / 2} validated corridors; last observation: {Reason}"); return true;
        }
        for (int work = 0; work < queryBudget; work++)
        {
            if (current == null)
            {
                if (frontier.Count > 0) current = frontier.Dequeue();
                else if (seedIndex < MaximumSeeds) current = Seed(seedIndex++);
                else
                {
                    if (edges.Count > 0)
                    {
                        BeginGraph(); search.Step(512);
                        if (TryResult() || TryFallback()) return true;
                    }
                    Finish("No clear connected viewpoint found for this subject"); return true;
                }
                Attempts++;
            }
            if (!Advance(current)) return false;
            current = null;
            if (nodes.Count >= nextGraphAt && edges.Count >= 4)
            { BeginGraph(); return false; }
            if ((Stopwatch.GetTimestamp() - begin) / (double)Stopwatch.Frequency >= .0025) break;
        }
        return false;
    }

    // Recovery can interrupt background preparation. Raise its share of the
    // scene-query budget without restarting the probe or extending its clock.
    internal void PromoteUrgent() => priority = CameraQueryPriority.OnAir;

    private Probe Seed(int index)
    {
        int order = index % 8;
        int sector = order == 0 ? 0 : (order + 1) / 2 * (order % 2 == 1 ? 1 : -1);
        Vector3 outward = Quaternion.AngleAxis(sector * 45f, Vector3.up) * seedDirection;
        float distance = (subject.HasValue ? FramingDistance(outward) : desiredDistance) * (index < 8 ? 1f : 1.25f);
        return new Probe(focus + outward * distance + Vector3.up * (desiredHeight + (index < 8 ? 0 : 8f)), -1);
    }

    private float FramingDistance(Vector3 outward)
    {
        Vector3 size = subject!.Value.extents, right = Vector3.Cross(Vector3.up, outward);
        float halfWidth = Mathf.Abs(right.x) * size.x + Mathf.Abs(right.z) * size.z;
        float halfDepth = Mathf.Abs(outward.x) * size.x + Mathf.Abs(outward.z) * size.z;
        float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * .5f);
        float fit = Mathf.Max(halfWidth / (tangent * aspect * .68f), size.y / (tangent * .66f)) + halfDepth;
        return Mathf.Max(8f, fit * (role == ScenicShotRole.Detail ? .62f : role == ScenicShotRole.Traverse ? .9f : 1.12f));
    }

    // true means this probe is consumed (accepted or rejected). Unknown work
    // retains the same probe and cannot become a free graph node or corridor.
    private bool Advance(Probe probe)
    {
        visibility.QueryId = id;
        visibility.QueryStage = $"probe {Attempts}, stage {probe.Stage}, sample {probe.ViewSample}";
        if (probe.Stage == 0)
        {
            // Do not spend a pending-surface wait on a point that is already
            // outside the observable carrier/region envelope.
            if (!Reachable(probe.Position)) return Reject("Viewpoint exceeds the acquired region or observer reach");
            if (!probe.Fitted)
            {
                if (!Fit(probe, out bool known))
                    return known ? Reject("The proposed viewpoint cannot fit above measured terrain/water") :
                        Wait(probe, "Terrain/water support is not yet observed at the proposed viewpoint");
                probe.Fitted = true;
            }
            if (!Reachable(probe.Position)) return Reject("Viewpoint exceeds the acquired region or observer reach");
            if (!IdleShots.Ready(probe.Position) || !IdleShots.Ready(focus))
                return Wait(probe, "Viewpoint or subject scene is not acquired yet");
            probe.Pose = MakePose(probe.Position);
            CameraSpaceObservation observed = CameraSpatialScene.Shared.ObservePoint(probe.Position, probe.Pose.Rotation,
                fov, aspect, nearClip, CameraSpaceUse.Scenic, true, priority);
            if (!observed.Known) return Wait(probe, observed.Reason);
            if (!observed.Clear) return Reject(observed.Reason);
            ScenicVisibility.Result picture = visibility.Evaluate(probe.Position, probe.Pose.Rotation, focus, fov, aspect, nearClip, subject);
            if (!picture.CameraClear) return Reject(picture.Reason);
            if (!picture.SurveyComplete) return Wait(probe, picture.Reason);
            if (!picture.CameraClear || !picture.FocusVisible ||
                !PictureQuality.Scenic(picture.CameraClear, picture.FocusVisible, picture.Score, picture.ForegroundOcclusion, true).Readable)
            {
                if (!probe.Raised && visibility.TryCanopyLift(probe.Position, out float lift))
                {
                    probe.Position += Vector3.up * lift; probe.Raised = true; probe.Fitted = false; probe.WaitingAt = -1;
                    Reason = "Measure a viewpoint above the observed canopy"; return false;
                }
                return Reject(!picture.FocusVisible ? "Subject is hidden from this measured viewpoint" : picture.Reason);
            }
            if (!Framing(probe.Pose, out float framing)) return Reject("Measured subject extent does not make a readable composition at this viewpoint");
            probe.Quality = Mathf.Clamp01(picture.Score * .8f + framing * .2f);
            probe.Clearance = observed.Clearance; probe.ObservedAt = observed.ObservedAt; probe.WaitingAt = -1;
            if (probe.Parent < 0) { Add(probe); return true; }
            probe.Stage = 1;
        }
        ScenicPose parent = poses[probe.Parent];
        if (probe.Stage == 1)
        {
            CameraSpaceObservation corridor = CameraSpatialScene.Shared.ObserveSegment(parent.Position, parent.Rotation,
                probe.Position, probe.Pose.Rotation, fov, aspect, nearClip, CameraSpaceUse.Scenic, true, priority);
            if (!corridor.Known) return Wait(probe, corridor.Reason);
            if (!corridor.Clear) return Reject(corridor.Reason);
            probe.Clearance = Mathf.Min(probe.Clearance, corridor.Clearance);
            probe.ObservedAt = Math.Min(probe.ObservedAt, corridor.ObservedAt); probe.Stage = 2; probe.WaitingAt = -1;
        }
        // Corridor geometry alone does not certify what the camera sees. Probe
        // intervening orientations at <=1.25m, retaining unknown survey work.
        int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(parent.Position, probe.Position) / 1.25f));
        if (++probe.ViewSample < samples)
        {
            ScenicPose pose = MakePose(Vector3.Lerp(parent.Position, probe.Position, probe.ViewSample / (float)samples));
            ScenicVisibility.Result view = visibility.Evaluate(pose.Position, pose.Rotation, focus, fov, aspect, nearClip, subject);
            if (!view.CameraClear) return Reject(view.Reason);
            if (!view.SurveyComplete) { probe.ViewSample--; return Wait(probe, view.Reason); }
            if (!view.CameraClear || !view.FocusVisible ||
                !PictureQuality.Scenic(view.CameraClear, view.FocusVisible, view.Score, view.ForegroundOcclusion, true).Readable || !Framing(pose, out _))
                return Reject("The connecting corridor loses readable subject framing");
            probe.Quality = Mathf.Min(probe.Quality, view.Score); probe.WaitingAt = -1;
            return false;
        }
        Add(probe); return true;
    }

    private bool Fit(Probe probe, out bool known)
    {
        known = true;
        if (Vector3.ProjectOnPlane(probe.Position - focus, Vector3.up).sqrMagnitude < 4f) return false;
        if (!ScenicSurface.TryGroundHeight(probe.Position, out float ground) ||
            !ScenicSurface.TryWaterHeight(probe.Position, out bool water, out float level))
        { known = false; return false; }
        probe.Position.y = Mathf.Max(probe.Position.y, ground + 2.5f, water ? level + 2.5f : float.NegativeInfinity);
        for (int i = 0; i < 2; i++)
        {
            ScenicPose pose = MakePose(probe.Position);
            if (!ScenicSurface.TryRequiredLift(pose, aspect, nearClip, out float lift, out _))
            { known = false; return false; }
            if (lift <= .001f) return true;
            if (lift > 16f) return false;
            probe.Position.y += lift + .1f;
        }
        return false;
    }

    private bool Reachable(Vector3 position)
    {
        if (!TargetRoster.Finite(position) || Vector3.Distance(position, origin) > 90f ||
            Vector3.ProjectOnPlane(position - focus, Vector3.up).sqrMagnitude < 4f) return false;
        return !moveCarrier || Player.m_localPlayer && Vector3.Distance(position + Vector3.down * 2f,
            Player.m_localPlayer.transform.position) < 89f;
    }

    private bool Framing(ScenicPose pose, out float quality)
    {
        quality = 1;
        if (!subject.HasValue) return true;
        Bounds bounds = subject.Value;
        Quaternion inverse = Quaternion.Inverse(pose.Rotation);
        Vector3 min = bounds.min, max = bounds.max;
        int n = 0;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 local = inverse * (new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z) - pose.Position);
            corners[n++] = new ShotProjection.Point(local.x, local.y, local.z);
        }
        ShotProjection.Envelope envelope = ShotProjection.MeasureEnvelope(corners, fov, aspect, nearClip, .10, .025, .02);
        if (!envelope.InFront || envelope.RelativeHeight < .08 || envelope.MaxX - envelope.MinX < .1) return false;
        double minimum = role == ScenicShotRole.Establish ? .88 : role == ScenicShotRole.Detail ? .2 : .55;
        if (envelope.VisibleFraction < minimum) return false;
        if (role == ScenicShotRole.Establish && (envelope.Headroom < .02 || envelope.SafeFraction < .80)) return false;
        float coverage = (float)Math.Min(1, envelope.VisibleFraction / Math.Max(.4, minimum));
        quality = role == ScenicShotRole.Detail ? coverage :
            coverage * .65f + (float)envelope.SafeFraction * .20f + (float)envelope.HeadroomScore * .15f;
        return true;
    }

    private void Add(Probe probe)
    {
        int index = nodes.Count;
        considered.Add(new Vector3Int(Mathf.RoundToInt(probe.Position.x), Mathf.RoundToInt(probe.Position.y), Mathf.RoundToInt(probe.Position.z)));
        float novelty = precedingCamera.HasValue ? Mathf.Clamp01(Vector3.Distance(probe.Position, precedingCamera.Value) / 24f) : 1;
        nodes.Add(new CameraSpatialNode(index, ToCore(probe.Position), ToCore(focus), probe.Quality,
            probe.Clearance, novelty, probe.ObservedAt, epoch)); poses.Add(probe.Pose);
        if (probe.Parent >= 0)
        {
            float quality = Mathf.Min(probe.Quality, nodes[probe.Parent].Quality);
            edges.Add(new CameraSpatialEdge(probe.Parent, index, quality, probe.Clearance, probe.ObservedAt, epoch));
            edges.Add(new CameraSpatialEdge(index, probe.Parent, quality, probe.Clearance, probe.ObservedAt, epoch));
        }
        // Grow through nearby measured space rather than prescribing a sweep.
        // Tangential, depth and rising options compete under the shared role,
        // continuity and turn cost. The queue and graph remain bounded.
        Vector3 radial = Vector3.ProjectOnPlane(probe.Position - focus, Vector3.up).normalized;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial);
        Enqueue(probe.Position + tangent * EdgeStep, index);
        Enqueue(probe.Position - tangent * EdgeStep, index);
        Enqueue(probe.Position - radial * 4f, index);
        Enqueue(probe.Position + radial * 4f, index);
        if (role == ScenicShotRole.Reveal || role == ScenicShotRole.Establish)
        {
            Enqueue(probe.Position + tangent * 4f + Vector3.up * 3f, index);
            Enqueue(probe.Position - tangent * 4f + Vector3.up * 3f, index);
        }
        Reason = "Connecting measured clear viewpoints";
    }

    private void Enqueue(Vector3 position, int parent)
    {
        if (frontier.Count >= MaximumProbes || considered.Count >= MaximumProbes * 2 || Vector3.Distance(position, origin) > 90f) return;
        var key = new Vector3Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y), Mathf.RoundToInt(position.z));
        if (considered.Add(key)) frontier.Enqueue(new Probe(position, parent));
    }

    private void BeginGraph()
    {
        var graph = new CameraSpatialGraph(epoch, nodes, edges);
        double target = role == ScenicShotRole.Detail ? 9 : role == ScenicShotRole.Establish ? 18 : 14;
        double minimum = Math.Max(role == ScenicShotRole.Detail ? 7 : 10, target * .8);
        // Retain a useful shorter measured route for bounded recovery, but do
        // not let its high score suppress a developed 14–18m shot in open space.
        // The fallback still goes through the complete native-path preflight.
        fallbackSearch.Begin(graph, new CameraSpatialRequest(Time.unscaledTime, epoch, role, 5, target, 30,
            .5f, .25f, 8));
        fallbackSearch.Step(512);
        CameraSpatialRoute? fallback = fallbackSearch.Result;
        if (fallback != null && (fallbackRoute == null || fallback.Score > fallbackRoute.Score)) fallbackRoute = fallback;
        search.Begin(graph, new CameraSpatialRequest(Time.unscaledTime, epoch, role, minimum, target, 30,
            .5f, .25f, 8)); graphSearching = true; Reason = "Selecting a useful trajectory through observed camera space";
    }

    private bool TryResult() => TryRoute(search.Result, false);
    private bool TryFallback() => TryRoute(fallbackRoute, true);
    private bool TryRoute(CameraSpatialRoute? route, bool fallback)
    {
        if (route == null || !route.IsFresh(Time.unscaledTime, CameraSpatialScene.Shared.Epoch, 8)) return false;
        bodyRoute.Clear(); aimRoute.Clear();
        foreach (CameraSpatialNode node in route.Nodes) { bodyRoute.Add(node.Position); aimRoute.Add(node.Aim); }
        try
        {
            ScenicShotPlan candidate = ScenicShotPlan.Spatial(id, feature!, role, bodyRoute, aimRoute, fov, screenX, screenY, native);
            if (candidate.TravelMetres < 5 || candidate.VisualChange < .12) return false;
            Plan = candidate; Score = Mathf.Clamp01((float)route.Score); Complete = true;
            Reason = fallback ? "Shorter measured corridor ready for complete native validation after bounded search" :
                "Developed spatial trajectory ready for complete native validation";
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private bool Wait(Probe probe, string reason)
    {
        float now = Time.unscaledTime;
        if (probe.WaitingAt < 0) probe.WaitingAt = now;
        Reason = reason;
        TraceBudget("wait", probe, ScenicObservationPolicy.WaitExpired(now, probe.WaitingAt));
        if (ScenicObservationPolicy.WaitExpired(now, probe.WaitingAt)) return Reject("Unknown spatial evidence timed out: " + reason);
        return false;
    }
    private bool Reject(string reason) { Rejected++; Reason = reason; ScenicTrace.Event("spatial-reject", reason, id); return true; }
    private void Finish(string reason) { Complete = true; Plan = null; Reason = reason; ScenicTrace.Event("spatial-finish", reason, id); }
    private void TraceBudget(string operation, Probe? probe, bool expired)
    {
        if (!ScenicTrace.Enabled) return;
        CameraTraceRecord row = ScenicTrace.Row("budget").Field("operation", operation).Field("query", id)
            .Field("started", started).Field("waitingAt", probe?.WaitingAt ?? -1)
            .Field("urgent", priority == CameraQueryPriority.OnAir).Field("expired", expired)
            .Field("probe", Attempts).Field("stage", probe?.Stage ?? -1).Field("sample", probe?.ViewSample ?? -1)
            .Field("nodes", nodes.Count).Field("edges", edges.Count).Field("rejected", Rejected).Field("pending", frontier.Count)
            .Field("reason", Reason);
        ScenicTrace.Record(probe == null ? row : ScenicTrace.Vector(row, "camera", probe.Position));
    }
    private ScenicPose MakePose(Vector3 position) => new ScenicPose(position, focus,
        ScenicVisibility.Compose(position, focus, screenX, screenY, fov, aspect), fov, 0);
    internal void Reset()
    {
        feature = null; current = null; nodes.Clear(); edges.Clear(); poses.Clear(); frontier.Clear(); considered.Clear();
        search.Reset(); fallbackSearch.Reset(); fallbackRoute = null; bodyRoute.Clear(); aimRoute.Clear();
        Plan = null; Complete = graphSearching = false; Score = 0; Attempts = Rejected = seedIndex = nextGraphAt = 0;
        subject = null; precedingCamera = null; Reason = "Awaiting a spatial scenic subject";
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
