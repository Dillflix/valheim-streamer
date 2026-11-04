using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>A local query view over the shared, persistent foliage catalogue.
/// Collection is incremental; a completed view survives a budget-deferred
/// refresh. Nearest retention precedes mesh refinement so peripheral/evicted
/// objects cannot consume the camera's detailed-geometry allowance.</summary>
internal sealed class FoliageProjection
{
    internal static readonly FoliageProjection Actors = new FoliageProjection(100, "actors");
    internal const int Capacity = 512, DetailBudget = 1024, BoundsCapacity = Capacity + DetailBudget;
    private static int nextId;
    private readonly int id = ++nextId;
    private readonly string owner;
    private readonly float radius;
    private List<Entry> retained = new List<Entry>(Capacity), staging = new List<Entry>(Capacity);
    private readonly List<float> distances = new List<float>(Capacity);
    private readonly Bounds[] active = new Bounds[BoundsCapacity];
    private IReadOnlyList<ScenicFoliageScene.Observation>? source;
    private Vector3 origin, pendingOrigin, priority;
    private float coveredRadius, pendingCoverage, refreshedAt = float.NegativeInfinity;
    private long epoch = -1, revision = -1, pendingRevision, pendingGeneration;
    private int sourceIndex, refineIndex, furthest, activeFrame = -1, detailRemaining;
    private float furthestDistance;
    private bool collecting, refining, refinementPending, pendingRefinement;
    private int retainedRefineIndex, refinementFrame = -1;
    private float nextProgressTrace;
    internal int ActiveCount { get; private set; }
    internal int RetainedCount => retained.Count;
    internal int Omitted { get; private set; }
    internal int Detailed { get; private set; }
    internal int Coarse => retained.Count - Detailed;
    internal int DetailRemaining => detailRemaining;
    internal Vector3 Origin => origin;
    internal float RefreshedAt => refreshedAt;
    internal long Revision => revision;
    internal bool Updating => collecting || refining;
    internal Bounds[] ActiveBounds => active;
    internal float CoveredRadius => Mathf.Min(coveredRadius, ScenicFoliageScene.Shared.CoverageFor(revision, origin, radius));

    private readonly struct Entry
    {
        internal readonly ScenicFoliageScene.Observation Observation;
        internal readonly Bounds Coarse;
        internal readonly Bounds[]? Local;
        internal readonly Mesh? Mesh;
        internal Entry(ScenicFoliageScene.Observation observation, Bounds coarse, Bounds[]? local = null)
        {
            Observation = observation; Coarse = coarse; Local = local;
            MeshFilter? filter = local != null && observation.Renderer ? observation.Renderer.GetComponent<MeshFilter>() : null;
            Mesh = filter ? filter.sharedMesh : null;
        }
    }

    internal FoliageProjection(float radius, string owner = "scenic") { this.radius = radius; this.owner = owner; }
    internal void Reset()
    {
        retained.Clear(); staging.Clear(); distances.Clear(); source = null; collecting = refining = false;
        coveredRadius = pendingCoverage = 0; refreshedAt = float.NegativeInfinity; epoch = revision = -1;
        activeFrame = refinementFrame = -1; ActiveCount = Omitted = Detailed = retainedRefineIndex = 0;
        refinementPending = pendingRefinement = false; nextProgressTrace = 0;
    }

    internal void RequestRefresh() { if (!Updating) refreshedAt = float.NegativeInfinity; }

    internal void Refresh(Vector3 requested, Vector3? detailPriority = null)
    {
        ScenicFoliageScene scene = ScenicFoliageScene.Shared;
        scene.Ensure(requested);
        if (epoch != scene.Epoch) { Reset(); epoch = scene.Epoch; }
        float now = Time.unscaledTime;
        if (Updating && pendingGeneration != scene.Generation)
        { Trace("generation-abort", requested, true); collecting = refining = false; source = null; }
        if (!Updating)
        {
            float allowance = Mathf.Min(8, radius / 3);
            if (revision == scene.Revision && now >= refreshedAt && now - refreshedAt < 5 &&
                Vector3.Distance(requested, origin) <= allowance && CoveredRadius > allowance)
            { RefineRetained(scene, detailPriority ?? requested); return; }
            source = scene.Renderers; pendingRevision = scene.Revision; pendingGeneration = scene.Generation; pendingOrigin = requested;
            priority = detailPriority ?? requested; pendingCoverage = scene.CoverageAt(requested, radius);
            staging.Clear(); distances.Clear(); sourceIndex = refineIndex = furthest = 0;
            furthestDistance = 0; Omitted = 0; collecting = true; refining = false;
            Trace("start", requested, true);
        }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) { Trace("budget-deferred", requested); return; }
        using (lease)
        {
            int steps = 0;
            while (collecting && sourceIndex < source!.Count && steps++ < 256 &&
                CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                ScenicFoliageScene.Observation item = source[sourceIndex++];
                Renderer renderer = item.Renderer;
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                if (!item.TryBounds(out Bounds bounds))
                { pendingCoverage = item.LimitUnknownCoverage(pendingOrigin, pendingCoverage); continue; }
                Add(item, bounds);
            }
            if (collecting && sourceIndex == source!.Count)
            {
                collecting = false; refining = true; detailRemaining = DetailBudget; pendingRefinement = false;
                // Deterministic priority: containing/nearest camera geometry is
                // refined before distant leaves, independent of discovery order.
                staging.Sort((a, b) => {
                    int order = a.Coarse.SqrDistance(priority).CompareTo(b.Coarse.SqrDistance(priority));
                    return order != 0 ? order : a.Observation.Renderer.GetInstanceID().CompareTo(b.Observation.Renderer.GetInstanceID());
                });
            }
            while (refining && refineIndex < staging.Count && steps++ < 256 &&
                CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                Entry item = staging[refineIndex];
                if (item.Coarse.SqrDistance(priority) <= 16 * 16 && detailRemaining > 0 && item.Observation.Renderer)
                {
                    Bounds[]? cells = scene.Geometry(item.Observation.Renderer, out bool deferred);
                    pendingRefinement |= deferred;
                    if (cells != null && cells.Length <= detailRemaining)
                    { detailRemaining -= cells.Length; staging[refineIndex] = new Entry(item.Observation, item.Coarse, cells); }
                }
                refineIndex++;
            }
            if (refining && refineIndex == staging.Count)
            {
                List<Entry> old = retained; retained = staging; staging = old;
                origin = pendingOrigin; revision = pendingRevision; coveredRadius = pendingCoverage;
                refreshedAt = now; source = null; refining = false; activeFrame = -1;
                refinementPending = pendingRefinement; retainedRefineIndex = 0;
                Detailed = 0; foreach (Entry entry in retained) if (entry.Local != null) Detailed++;
                Trace("commit", requested, true);
            }
            if (Updating) Trace("progress", requested);
        }
    }

    private void Trace(string action, Vector3 requested, bool lifecycle = false)
    {
        if (!ScenicTrace.Enabled || !lifecycle && Time.unscaledTime < nextProgressTrace) return;
        nextProgressTrace = Time.unscaledTime + 1;
        CameraTraceRecord row = ScenicTrace.Row("foliage-projection").Field("action", action).Field("projection", id).Field("owner", owner)
            .Field("radius", radius).Field("revision", revision).Field("pendingRevision", pendingRevision)
            .Field("generation", pendingGeneration).Field("catalogueGeneration", ScenicFoliageScene.Shared.Generation)
            .Field("sourceIndex", sourceIndex).Field("sourceCount", source?.Count ?? 0)
            .Field("refineIndex", refineIndex).Field("refineCount", staging.Count)
            .Field("retained", retained.Count).Field("omitted", Omitted)
            .Field("coveredRadius", CoveredRadius).Field("pendingCoverage", pendingCoverage)
            .Field("cpu", CameraWorkBudget.Shared.Diagnostics);
        ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(row,
            "requested", requested), "retainedOrigin", origin), "pendingOrigin", pendingOrigin));
    }

    private void RefineRetained(ScenicFoliageScene scene, Vector3 priorityPoint)
    {
        if (!refinementPending || refinementFrame == Time.frameCount ||
            !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) return;
        refinementFrame = Time.frameCount;
        using (lease)
        {
            int steps = 0;
            while (retainedRefineIndex < retained.Count && steps++ < 32 && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                int at = retainedRefineIndex; Entry entry = retained[at];
                if (!entry.Observation.Renderer || !entry.Observation.TryBounds(out Bounds bounds) || bounds.SqrDistance(priorityPoint) > 256)
                { retainedRefineIndex++; continue; }
                Bounds[]? cells = scene.Geometry(entry.Observation.Renderer, out bool deferred);
                if (cells != null)
                {
                    int refunded = entry.Local?.Length ?? 0;
                    if (cells.Length <= detailRemaining + refunded)
                    {
                        detailRemaining += refunded - cells.Length;
                        retained[at] = new Entry(entry.Observation, bounds, cells); activeFrame = -1;
                    }
                }
                // Retry this nearest entry next frame when a mesh read/metadata
                // slice was deferred; never wait for a multi-second fresh survey.
                if (deferred) return;
                retainedRefineIndex++;
            }
            if (retainedRefineIndex >= retained.Count)
            {
                refinementPending = false; Detailed = 0;
                foreach (Entry entry in retained) if (entry.Local != null) Detailed++;
            }
        }
    }

    private void Add(ScenicFoliageScene.Observation item, Bounds bounds)
    {
        Bounds expanded = bounds; expanded.Expand(.4f);
        float distance = Mathf.Sqrt(expanded.SqrDistance(pendingOrigin));
        if (distance >= radius) return;
        if (staging.Count < Capacity)
        {
            staging.Add(new Entry(item, bounds)); distances.Add(distance);
            if (distance >= furthestDistance) { furthest = staging.Count - 1; furthestDistance = distance; }
            return;
        }
        Omitted++;
        if (distance >= furthestDistance) { pendingCoverage = Mathf.Min(pendingCoverage, distance); return; }
        pendingCoverage = Mathf.Min(pendingCoverage, furthestDistance);
        staging[furthest] = new Entry(item, bounds); distances[furthest] = distance;
        furthestDistance = 0;
        for (int i = 0; i < distances.Count; i++)
            if (distances[i] >= furthestDistance) { furthest = i; furthestDistance = distances[i]; }
    }

    internal void RefreshActive()
    {
        if (activeFrame == Time.frameCount) return;
        activeFrame = Time.frameCount; ActiveCount = 0;
        foreach (Entry entry in retained)
        {
            Renderer renderer = entry.Observation.Renderer;
            if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
            if (!entry.Observation.TryBounds(out Bounds observed))
            { coveredRadius = entry.Observation.LimitUnknownCoverage(origin, coveredRadius); continue; }
            MeshFilter? filter = entry.Local != null ? renderer.GetComponent<MeshFilter>() : null;
            if (entry.Local == null || !filter || filter.sharedMesh != entry.Mesh) AddActive(observed);
            else
            {
                Matrix4x4 matrix = renderer.localToWorldMatrix;
                foreach (Bounds local in entry.Local) AddActive(FoliageGeometryMath.Transform(local, matrix));
            }
        }
    }
    private void AddActive(Bounds bounds)
    {
        if (!FoliageGeometryMath.Valid(bounds)) { coveredRadius = 0; return; }
        bounds.Expand(.4f); active[ActiveCount++] = bounds;
    }
}
