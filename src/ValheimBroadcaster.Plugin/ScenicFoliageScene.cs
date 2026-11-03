using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Scene-owned renderer discovery and mesh refinement. Candidate,
/// preflight and on-air queries never move this catalogue's survey centre.</summary>
internal sealed class ScenicFoliageScene
{
    internal static readonly ScenicFoliageScene Shared = new ScenicFoliageScene();
    private const int Capacity = 8192;
    private const float Radius = FoliageCoverageMap.Radius;
    private readonly List<Observation> renderers = new List<Observation>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private readonly HashSet<int> seen = new HashSet<int>();
    private readonly ScenicFoliageGeometry geometry = new ScenicFoliageGeometry();
    private readonly FoliageCoverageMap coverage = new FoliageCoverageMap();
    private Vector3 origin;
    private bool anchored;
    private long epoch = -1;
    private float refreshedAt = float.NegativeInfinity;
    private int geometryFrame = -1, omitted, empty, repaired, unresolved, metadataOverflow;
    private readonly int[] sampleCounts = new int[4];
    internal long Revision { get; private set; }
    internal IReadOnlyList<Observation> Renderers => renderers;
    internal float CoverageAt(Vector3 point) => coverage.CoverageAt(point, ScenicObservationPolicy.DetailRadius);

    internal readonly struct Observation
    {
        internal readonly Renderer Renderer;
        private readonly Bounds fallback;
        internal Observation(Renderer renderer, Bounds local) { Renderer = renderer; fallback = local; }
        internal bool TryBounds(out Bounds bounds)
        {
            bounds = default;
            if (!Renderer || !Renderer.enabled || !Renderer.gameObject.activeInHierarchy) return false;
            Bounds world = Renderer.bounds;
            Bounds local = FoliageGeometryMath.Valid(fallback) ? fallback :
                FoliageGeometryMath.Valid(world) ? default : Renderer.localBounds;
            // Same source and transform at discovery, projection and live pose.
            return FoliageGeometryMath.ResolveBounds(world, local,
                FoliageGeometryMath.Valid(world) ? Matrix4x4.identity : Renderer.localToWorldMatrix, false, out bounds)
                != FoliageGeometryMath.BoundsSource.Unknown;
        }
        internal float LimitUnknownCoverage(Vector3 point, float available)
        {
            Bounds envelope = Renderer ? Envelope(Renderer.GetComponentInParent<LODGroup>()) : default;
            if (!FoliageGeometryMath.Valid(envelope)) return 0;
            envelope.Expand(.4f);
            return Mathf.Min(available, Mathf.Sqrt(envelope.SqrDistance(point)));
        }
    }

    internal void SetRegion(Vector3 point)
    {
        if (anchored && (point - origin).sqrMagnitude < .01f) return;
        Reset(); origin = point; anchored = true;
    }
    internal void Reset()
    {
        renderers.Clear(); groups.Clear(); seen.Clear(); geometry.Reset(); coverage.Reset(origin);
        refreshedAt = float.NegativeInfinity; anchored = false; epoch = -1;
        geometryFrame = -1; Revision++;
    }
    internal void Ensure(Vector3 requested)
    {
        long currentEpoch = CameraSpatialScene.Shared.Epoch;
        if (epoch != currentEpoch)
        {
            Vector3 anchor = anchored ? origin : requested;
            Reset(); origin = anchor; anchored = true; epoch = currentEpoch;
        }
        float now = Time.unscaledTime;
        if (!ScenicObservationPolicy.CatalogueDue(now, refreshedAt)) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        renderers.Clear(); groups.Clear(); seen.Clear(); coverage.Reset(origin);
        omitted = empty = repaired = unresolved = metadataOverflow = 0;
        Array.Clear(sampleCounts, 0, sampleCounts.Length);
        refreshedAt = now; Revision++;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled) continue;
            Bounds world = renderer.bounds;
            // Bad world bounds must be resolved before distance filtering.
            if (FoliageGeometryMath.Valid(world) && world.SqrDistance(origin) > (Radius + 1) * (Radius + 1)) continue;
            if (renderer.GetComponentInParent<Character>() || !ScenicVisibility.Vegetation(renderer.transform)) continue;
            LODGroup? group = renderer.GetComponentInParent<LODGroup>();
            if (!group) { Add(renderer, null); continue; }
            if (groups.Contains(group!.GetInstanceID())) continue;
            // A full dedup cache is not lost geometry. Continue inspecting raw
            // renderers; bounded storage overflow is localized by actual bounds.
            if (groups.Count >= Capacity) { metadataOverflow++; Add(renderer, group); continue; }
            groups.Add(group.GetInstanceID());
            LOD[] levels = group.GetLODs(); bool found = false;
            if (levels.Length > 0)
                foreach (Renderer detail in levels[0].renderers)
                    if (detail && detail.enabled && detail.gameObject.activeInHierarchy)
                        found |= Add(detail, group);
            // An empty preferred LOD is not a retained renderer. Let the loaded
            // representation contribute instead of suppressing the entire tree.
            if (!found) { groups.Remove(group.GetInstanceID()); Add(renderer, group); }
        }
        if (ScenicTrace.Enabled)
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("catalogue").Field("revision", Revision)
                .Field("retained", renderers.Count).Field("omitted", omitted).Field("empty", empty)
                .Field("repaired", repaired).Field("unresolved", unresolved).Field("unknownCells", coverage.UnknownCells)
                .Field("unbounded", coverage.Unbounded).Field("metadataOverflow", metadataOverflow)
                .Field("radius", Radius).Field("originCoverage", CoverageAt(origin))
                .Field("milliseconds", (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency), "origin", origin));
    }

    private bool Add(Renderer renderer, LODGroup? group)
    {
        int id = renderer.GetInstanceID();
        if (seen.Contains(id)) return false;
        // Bound dedup metadata even when the actual renderer stream overflows.
        if (seen.Count < Capacity * 2) seen.Add(id);
        Bounds world = renderer.bounds, local = default;
        bool noGeometry = false;
        if (!FoliageGeometryMath.Valid(world))
        {
            local = renderer.localBounds;
            if (renderer is MeshRenderer)
            {
                MeshFilter? filter = renderer.GetComponent<MeshFilter>();
                Mesh? mesh = filter ? filter.sharedMesh : null;
                noGeometry = !mesh || mesh!.vertexCount == 0;
                if (!noGeometry && !FoliageGeometryMath.Valid(local)) local = mesh!.bounds;
            }
            else if (renderer is SkinnedMeshRenderer skin) noGeometry = !skin.sharedMesh || skin.sharedMesh.vertexCount == 0;
            else if (renderer is ParticleSystemRenderer)
            {
                ParticleSystem? particles = renderer.GetComponent<ParticleSystem>();
                // A particle source with a live tail is not assumed empty.
                noGeometry = particles && particles!.particleCount == 0 && !particles.trails.enabled;
            }
            else if (renderer is LineRenderer line) noGeometry = line.positionCount < 2;
            else if (renderer is TrailRenderer trail) noGeometry = trail.positionCount < 2;
        }
        var source = FoliageGeometryMath.ResolveBounds(world, local,
            FoliageGeometryMath.Valid(world) ? Matrix4x4.identity : renderer.localToWorldMatrix, noGeometry, out Bounds bounds);
        if (source == FoliageGeometryMath.BoundsSource.Empty)
        { empty++; TraceSample(renderer, source, world, local, default); return false; }
        if (source == FoliageGeometryMath.BoundsSource.Unknown)
        {
            unresolved++;
            // Retain uncertainty at the measured LOD envelope, not everywhere
            // within 220 metres. Truly unlocatable data remains explicitly unknown.
            Bounds envelope = Envelope(group);
            if (FoliageGeometryMath.Valid(envelope)) coverage.AddUnknown(envelope); else coverage.Invalidate();
            TraceSample(renderer, source, world, local, envelope); return false;
        }
        bounds.Expand(.4f);
        if (bounds.SqrDistance(origin) >= Radius * Radius) return false;
        if (source == FoliageGeometryMath.BoundsSource.Local)
        { repaired++; TraceSample(renderer, source, world, local, bounds); }
        if (renderers.Count >= Capacity) { coverage.AddUnknown(bounds); omitted++; return false; }
        renderers.Add(new Observation(renderer, local)); return true;
    }

    private static Bounds Envelope(LODGroup? group) => group ? FoliageGeometryMath.Transform(
        new Bounds(group!.localReferencePoint, Vector3.one * group.size), group.transform.localToWorldMatrix) : default;

    private void TraceSample(Renderer renderer, FoliageGeometryMath.BoundsSource disposition, Bounds world, Bounds local, Bounds envelope)
    {
        if (!ScenicTrace.Enabled || sampleCounts[(int)disposition]++ >= 4) return;
        CameraTraceRecord row = ScenicTrace.Row("renderer-bounds").Field("revision", Revision)
            .Field("rendererType", renderer.GetType().Name).Field("disposition", disposition.ToString());
        // No object/player names. Bounded examples distinguish geometry causes;
        // the preceding version recorded only an ambiguous omission count.
        foreach (var pair in new[] { ("world", world), ("local", local), ("envelope", envelope) })
            row = ScenicTrace.Vector(ScenicTrace.Vector(row, pair.Item1 + "Centre", pair.Item2.center), pair.Item1 + "Extent", pair.Item2.extents);
        ScenicTrace.Record(row);
    }
    internal Bounds[]? Geometry(Renderer renderer)
    {
        if (geometryFrame != Time.frameCount) { geometryFrame = Time.frameCount; geometry.BeginSurvey(); }
        return geometry.Get(renderer);
    }
}
