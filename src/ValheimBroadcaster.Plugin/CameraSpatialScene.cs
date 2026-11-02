using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal enum CameraSpaceUse { Scenic, Actor }
internal enum CameraQueryPriority { Background, OnAir }
internal enum CameraSpaceState { Unknown, Blocked, Clear }

internal readonly struct CameraSpaceObservation
{
    internal readonly CameraSpaceState State;
    internal readonly double ObservedAt;
    internal readonly long Epoch;
    // Radius of the body clearance actually queried, not a distance field.
    // The lens-dependent near rectangle is checked separately.
    internal readonly float Clearance;
    internal readonly string Reason;
    internal bool Known => State != CameraSpaceState.Unknown;
    internal bool Clear => State == CameraSpaceState.Clear;
    internal CameraSpaceObservation(CameraSpaceState state, double at, long epoch, float clearance, string reason)
    { State = state; ObservedAt = at; Epoch = epoch; Clearance = clearance; Reason = reason; }
}

/// <summary>Shared bounded observations of actual loaded camera space. This is
/// planning evidence, not permission to present a cached pose: each camera's
/// final live validator remains authoritative. No query generates terrain,
/// changes colliders, takes object ownership or adds a rendering camera.</summary>
internal sealed class CameraSpatialScene
{
    internal static readonly CameraSpatialScene Shared = new CameraSpatialScene();
    private const int PointCapacity = 512, SegmentCapacity = 256, PendingCapacity = 16;
    private const float PointLifetime = .35f, SegmentLifetime = .6f, PendingLifetime = 2f;
    private readonly Dictionary<PointKey, CameraSpaceObservation> points = new Dictionary<PointKey, CameraSpaceObservation>();
    private readonly Queue<PointKey> pointOrder = new Queue<PointKey>();
    private readonly Dictionary<EdgeKey, CameraSpaceObservation> segments = new Dictionary<EdgeKey, CameraSpaceObservation>();
    private readonly Queue<EdgeKey> segmentOrder = new Queue<EdgeKey>();
    private readonly Dictionary<EdgeKey, PendingEdge> pending = new Dictionary<EdgeKey, PendingEdge>();
    private readonly CameraObservationBudget budget = new CameraObservationBudget();
    private readonly Collider[] overlaps = new Collider[48];
    private readonly RaycastHit[] sweptHits = new RaycastHit[48];
    private readonly AutonomyPosition[] footprint = new AutonomyPosition[ScenicSurfacePolicy.SampleCount];
    private ZNet? world;
    private ZNetScene? scene;
    private Player? carrier;
    private int obstructionMask;
    private long epoch = 1;
    private long worldId;
    internal long Epoch { get { Synchronize(); return epoch; } }
    internal string Diagnostics => $"Camera space: epoch {epoch}; {points.Count} points, {segments.Count} corridors, {pending.Count} pending; " +
        $"work {budget.Spent}/{CameraObservationBudget.TotalUnits} (background {budget.BackgroundSpent}/{CameraObservationBudget.BackgroundUnits})";

    // Every physical input participates in exact equality. Rounded position or
    // omitted orientation/lens values could reuse proof across a wall/near plane.
    private readonly struct PointKey : IEquatable<PointKey>
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal readonly float Fov, Aspect, Near;
        internal readonly CameraSpaceUse Use;
        internal readonly bool Acquired;
        internal PointKey(Vector3 position, Quaternion rotation, float fov, float aspect, float near,
            CameraSpaceUse use, bool acquired)
        { Position = position; Rotation = rotation; Fov = fov; Aspect = aspect; Near = near; Use = use; Acquired = acquired; }
        public bool Equals(PointKey other) => Position.Equals(other.Position) && Rotation.Equals(other.Rotation) &&
            Fov.Equals(other.Fov) && Aspect.Equals(other.Aspect) && Near.Equals(other.Near) && Use == other.Use && Acquired == other.Acquired;
        public override bool Equals(object? value) => value is PointKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Position.GetHashCode(); h = h * 397 ^ Rotation.GetHashCode(); h = h * 397 ^ Fov.GetHashCode();
                h = h * 397 ^ Aspect.GetHashCode(); h = h * 397 ^ Near.GetHashCode(); h = h * 397 ^ (int)Use;
                return h * 397 ^ Acquired.GetHashCode();
            }
        }
    }
    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        internal readonly PointKey From, To;
        internal EdgeKey(PointKey from, PointKey to) { From = from; To = to; }
        public bool Equals(EdgeKey other) => From.Equals(other.From) && To.Equals(other.To);
        public override bool Equals(object? value) => value is EdgeKey other && Equals(other);
        public override int GetHashCode() { unchecked { return From.GetHashCode() * 397 ^ To.GetHashCode(); } }
    }
    private sealed class PendingEdge
    {
        internal readonly double Started;
        internal readonly int Steps;
        internal int Next;
        internal double EvidenceAt;
        internal Vector3 Previous;
        internal Quaternion PreviousRotation;
        internal float Clearance = float.PositiveInfinity;
        internal PendingEdge(double now, int steps) { Started = EvidenceAt = now; Steps = steps; }
    }

    internal CameraSpaceObservation ObservePoint(Vector3 position, Quaternion rotation, float fov, float aspect,
        float nearClip, CameraSpaceUse use, bool requireAcquired, CameraQueryPriority priority = CameraQueryPriority.Background)
    {
        Synchronize();
        if (!world || !scene || !carrier) return Unknown("Camera-space world binding unavailable");
        if (!Valid(position, rotation, fov, aspect, nearClip, use) || !ValidPriority(priority))
            return Unknown("Invalid camera-space query");
        var key = new PointKey(position, rotation.normalized, fov, aspect, nearClip, use, requireAcquired);
        double now = Time.unscaledTime;
        if (points.TryGetValue(key, out CameraSpaceObservation cached) && Fresh(cached, now, PointLifetime)) return cached;
        if (!budget.TrySpend(priority == CameraQueryPriority.OnAir, 6)) return Unknown("Camera-space point budget deferred");
        CameraSpaceObservation result = Measure(key);
        // Readiness and saturated physics buffers are unknown. Do not turn a
        // transient unknown result into a cached empty-space observation.
        if (result.Known) Store(points, pointOrder, key, result, PointCapacity);
        return result;
    }

    internal CameraSpaceObservation ObserveSegment(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation,
        float fov, float aspect, float nearClip, CameraSpaceUse use, bool requireAcquired,
        CameraQueryPriority priority = CameraQueryPriority.Background)
    {
        Synchronize();
        if (!world || !scene || !carrier) return Unknown("Camera-space world binding unavailable");
        if (!Valid(from, fromRotation, fov, aspect, nearClip, use) || !Valid(to, toRotation, fov, aspect, nearClip, use) || !ValidPriority(priority))
            return Unknown("Invalid camera-space corridor");
        float distance = Vector3.Distance(from, to), angle = Quaternion.Angle(fromRotation, toRotation);
        if (distance > 24f) return Unknown("Camera-space corridor exceeds local 24m planning limit");
        fromRotation = fromRotation.normalized; toRotation = toRotation.normalized;
        var key = new EdgeKey(new PointKey(from, fromRotation, fov, aspect, nearClip, use, requireAcquired),
            new PointKey(to, toRotation, fov, aspect, nearClip, use, requireAcquired));
        double now = Time.unscaledTime;
        if (segments.TryGetValue(key, out CameraSpaceObservation cached) && Fresh(cached, now, SegmentLifetime)) return cached;
        if (!pending.TryGetValue(key, out PendingEdge? work) || now < work.Started || now - work.Started > PendingLifetime)
        {
            if (work == null && pending.Count >= PendingCapacity) EvictOldestPending();
            int steps = Math.Max(1, Math.Max(Mathf.CeilToInt(distance / .75f), Mathf.CeilToInt(angle / 5f)));
            work = new PendingEdge(now, steps); pending[key] = work;
        }
        // Work resumes on subsequent frames at the identical unproven sample.
        // Returning Unknown cannot supply an edge to the planner's free graph.
        while (work.Next <= work.Steps)
        {
            float t = work.Next / (float)work.Steps;
            Vector3 point = Vector3.Lerp(from, to, t);
            Quaternion rotation = Quaternion.Slerp(fromRotation, toRotation, t);
            CameraSpaceObservation observed = ObservePoint(point, rotation, fov, aspect, nearClip, use, requireAcquired, priority);
            if (!observed.Known) return observed;
            if (!observed.Clear) return Complete(key, observed);
            // Reserve the swept checks separately; a point result may have
            // consumed the remaining budget and is retained for next frame.
            if (work.Next > 0)
            {
                if (!budget.TrySpend(priority == CameraQueryPriority.OnAir, 2)) return Unknown("Camera-space corridor budget deferred");
                CameraSpaceObservation? corridor = MeasureCorridor(work.Previous, work.PreviousRotation, point, rotation, fov, aspect, nearClip);
                if (corridor.HasValue) return Complete(key, corridor.Value);
            }
            work.EvidenceAt = Math.Min(work.EvidenceAt, observed.ObservedAt);
            work.Clearance = Math.Min(work.Clearance, observed.Clearance);
            work.Previous = point; work.PreviousRotation = rotation; work.Next++;
        }
        return Complete(key, new CameraSpaceObservation(CameraSpaceState.Clear, work.EvidenceAt, epoch,
            Math.Min(.25f, work.Clearance), "Measured loaded body/near-plane corridor"));
    }

    private CameraSpaceObservation Measure(PointKey query)
    {
        if (!world || !scene || !PlayerShotReadiness.PositionReady(query.Position, true) ||
            query.Acquired && !IdleShots.Ready(query.Position)) return Unknown("Camera-space geometry is not acquired");
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        int count = Physics.OverlapSphereNonAlloc(query.Position, .25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        CameraSpaceObservation? overlap = InspectOverlap(count, "Camera-space body intersects geometry");
        if (overlap.HasValue) return overlap.Value;
        float h = query.Near * Mathf.Tan(query.Fov * .5f * Mathf.Deg2Rad);
        Vector3 forward = query.Rotation * Vector3.forward;
        count = Physics.OverlapBoxNonAlloc(query.Position + forward * query.Near, new Vector3(h * query.Aspect, h, .04f),
            overlaps, query.Rotation, obstructionMask, QueryTriggerInteraction.Ignore);
        overlap = InspectOverlap(count, "Camera-space near plane intersects geometry");
        if (overlap.HasValue) return overlap.Value;
        if (query.Use == CameraSpaceUse.Scenic)
        {
            var pose = new ScenicPose(query.Position, query.Position + forward, query.Rotation, query.Fov, 0);
            if (!ScenicSurface.TryRequiredLift(pose, query.Aspect, query.Near, out float lift, out string reason)) return Unknown(reason);
            if (lift > .001f) return Blocked(reason);
        }
        else if (!ActorSurface(query, out CameraSpaceObservation result)) return result;
        // A second body envelope supplies measured clearance for ranking.
        // Failing this wider test does not invalidate the already clear .25m
        // body and exact near plane, and never fabricates a distance-to-wall.
        count = Physics.OverlapSphereNonAlloc(query.Position, 1.25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        float clearance = InspectOverlap(count, "Expanded camera body envelope occupied").HasValue ? .25f : 1.25f;
        return new CameraSpaceObservation(CameraSpaceState.Clear, Time.unscaledTime, epoch, clearance,
            "Loaded camera body, near plane and surface clearance observed");
    }

    private bool ActorSurface(PointKey query, out CameraSpaceObservation result)
    {
        result = default;
        Vector3 forward = query.Rotation * Vector3.forward, right = query.Rotation * Vector3.right, up = query.Rotation * Vector3.up;
        if (!ScenicSurfacePolicy.TryFootprint(ToCore(query.Position), ToCore(forward), ToCore(right), ToCore(up),
            query.Fov, query.Aspect, query.Near, footprint))
        { result = Unknown("Actor camera footprint invalid"); return false; }
        bool interior = Character.InInterior(query.Position);
        for (int i = 0; i < footprint.Length; i++)
        {
            Vector3 point = FromCore(footprint[i]);
            if (Character.InInterior(point) != interior || !PlayerShotReadiness.PositionReady(point, true) ||
                query.Acquired && !IdleShots.Ready(point))
            { result = Unknown("Actor camera footprint leaves observed scene support"); return false; }
            if (interior) continue; // Actual dungeon support, never terrain far below.
            if (!ScenicSurface.TryGroundHeight(point, out float ground) ||
                !ScenicSurface.TryWaterHeight(point, out bool water, out float waterHeight))
            { result = Unknown("Actor camera surface measurement unavailable"); return false; }
            if (point.y < ground + (i == 0 ? .3f : .12f) || water && point.y < waterHeight + (i == 0 ? .2f : .1f))
            { result = Blocked("Actor camera footprint is below its terrain/water margin"); return false; }
        }
        return true;
    }

    private CameraSpaceObservation? InspectOverlap(int count, string reason)
    {
        if (count >= overlaps.Length) return Unknown("Camera-space overlap buffer saturated");
        for (int i = 0; i < count; i++)
        {
            Collider value = overlaps[i];
            if (value && value.enabled && !value.isTrigger && !value.GetComponentInParent<Character>()) return Blocked(reason);
        }
        return null;
    }

    // Same swept envelopes as the live scenic validator, with explicit unknown
    // results for saturated buffers. A truncated physics query is not evidence
    // of either free space or a particular obstacle in the shared map.
    private CameraSpaceObservation? MeasureCorridor(Vector3 from, Quaternion fromRotation,
        Vector3 to, Quaternion toRotation, float fov, float aspect, float nearClip)
    {
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Vector3 bodyDelta = to - from;
        if (bodyDelta.sqrMagnitude > .000001f)
        {
            int count = Physics.SphereCastNonAlloc(from, .25f, bodyDelta.normalized, sweptHits,
                bodyDelta.magnitude, obstructionMask, QueryTriggerInteraction.Ignore);
            CameraSpaceObservation? body = InspectSweep(count, "Camera body corridor crosses geometry");
            if (body.HasValue) return body;
        }
        float height = nearClip * Mathf.Tan(fov * .5f * Mathf.Deg2Rad), width = height * aspect;
        Vector3 first = from + fromRotation * (Vector3.forward * nearClip);
        Vector3 last = to + toRotation * (Vector3.forward * nearClip), delta = last - first;
        Quaternion middle = Quaternion.Slerp(fromRotation, toRotation, .5f);
        float padding = (new Vector3(width, height, .04f).magnitude + nearClip) *
            Mathf.Sin(Quaternion.Angle(fromRotation, toRotation) * .5f * Mathf.Deg2Rad);
        Vector3 extents = new Vector3(width + padding, height + padding, .04f + padding);
        int overlapsCount = Physics.OverlapBoxNonAlloc(first, extents, overlaps, middle,
            obstructionMask, QueryTriggerInteraction.Ignore);
        CameraSpaceObservation? near = InspectOverlap(overlapsCount, "Camera near-plane corridor intersects geometry");
        if (near.HasValue || delta.sqrMagnitude <= .000001f) return near;
        int hits = Physics.BoxCastNonAlloc(first, extents, delta.normalized, sweptHits, middle,
            delta.magnitude, obstructionMask, QueryTriggerInteraction.Ignore);
        return InspectSweep(hits, "Camera near-plane corridor crosses geometry");
    }
    private CameraSpaceObservation? InspectSweep(int count, string reason)
    {
        if (count >= sweptHits.Length) return Unknown("Camera-space swept query buffer saturated");
        for (int i = 0; i < count; i++)
        {
            Collider value = sweptHits[i].collider;
            if (value && value.enabled && !value.isTrigger && !value.GetComponentInParent<Character>()) return Blocked(reason);
        }
        return null;
    }
    private CameraSpaceObservation Complete(EdgeKey key, CameraSpaceObservation observation)
    {
        pending.Remove(key);
        if (observation.Known) Store(segments, segmentOrder, key, observation, SegmentCapacity);
        return observation;
    }
    private void EvictOldestPending()
    {
        EdgeKey oldest = default; double at = double.PositiveInfinity;
        foreach (var pair in pending) if (pair.Value.Started < at) { oldest = pair.Key; at = pair.Value.Started; }
        pending.Remove(oldest);
    }
    private static void Store<TKey>(Dictionary<TKey, CameraSpaceObservation> cache, Queue<TKey> order,
        TKey key, CameraSpaceObservation value, int capacity) where TKey : notnull
    {
        if (!cache.ContainsKey(key))
        {
            while (cache.Count >= capacity) cache.Remove(order.Dequeue());
            order.Enqueue(key);
        }
        cache[key] = value;
    }
    private bool Fresh(CameraSpaceObservation observation, double now, float lifetime) =>
        observation.Epoch == epoch && now >= observation.ObservedAt && now - observation.ObservedAt <= lifetime;
    private CameraSpaceObservation Unknown(string reason) => new CameraSpaceObservation(CameraSpaceState.Unknown, Time.unscaledTime, epoch, 0, reason);
    private CameraSpaceObservation Blocked(string reason) => new CameraSpaceObservation(CameraSpaceState.Blocked, Time.unscaledTime, epoch, 0, reason);
    private void Synchronize()
    {
        ZNet currentWorld = ZNet.instance;
        ZNetScene currentScene = ZNetScene.instance;
        Player currentCarrier = Player.m_localPlayer;
        long currentId = currentWorld && currentScene ? currentWorld.GetWorldUID() : 0;
        // Reference identity also catches destruction/null transitions which
        // Unity's overloaded equality can otherwise treat as the same null.
        if (!ReferenceEquals(world, currentWorld) || !ReferenceEquals(scene, currentScene) ||
            !ReferenceEquals(carrier, currentCarrier) || worldId != currentId)
        {
            Reset(); world = currentWorld; scene = currentScene; carrier = currentCarrier; worldId = currentId;
        }
        budget.BeginFrame(Time.frameCount);
    }
    // Construction/snapshot revisions can invalidate all spatial evidence while
    // retaining the current world binding. Final live checks never use caches.
    internal void Invalidate()
    {
        points.Clear(); pointOrder.Clear(); segments.Clear(); segmentOrder.Clear(); pending.Clear();
        Array.Clear(overlaps, 0, overlaps.Length); Array.Clear(sweptHits, 0, sweptHits.Length);
        epoch++; if (epoch <= 0) epoch = 1;
    }
    internal void Reset()
    { Invalidate(); budget.Reset(); world = null; scene = null; carrier = null; worldId = 0; obstructionMask = 0; }
    private static bool Valid(Vector3 position, Quaternion rotation, float fov, float aspect, float nearClip, CameraSpaceUse use)
    {
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return TargetRoster.Finite(position) && Math.Abs(position.x) <= 20000 && Math.Abs(position.y) <= 20000 && Math.Abs(position.z) <= 20000 &&
            Finite(magnitude) && magnitude > .0001f && Finite(fov) && fov >= 1 && fov <= 170 &&
            Finite(aspect) && aspect > 0 && aspect <= 8 && Finite(nearClip) && nearClip > 0 && nearClip <= 5 &&
            (use == CameraSpaceUse.Scenic || use == CameraSpaceUse.Actor);
    }
    private static bool ValidPriority(CameraQueryPriority priority) => priority == CameraQueryPriority.Background || priority == CameraQueryPriority.OnAir;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
