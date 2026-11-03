using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Scene-owned renderer discovery and mesh refinement. Candidate,
/// preflight and on-air queries never move this catalogue's survey centre.
/// Local projections may change immediately without enumerating the scene.</summary>
internal sealed class ScenicFoliageScene
{
    internal static readonly ScenicFoliageScene Shared = new ScenicFoliageScene();
    private const int Capacity = 8192;
    private const float Radius = 220;
    private readonly List<Renderer> renderers = new List<Renderer>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private readonly HashSet<int> retained = new HashSet<int>();
    private readonly ScenicFoliageGeometry geometry = new ScenicFoliageGeometry();
    private Vector3 origin;
    private bool anchored;
    private long epoch = -1;
    private float refreshedAt = float.NegativeInfinity, coveredRadius;
    private int geometryFrame = -1, omitted;
    internal long Revision { get; private set; }
    internal IReadOnlyList<Renderer> Renderers => renderers;
    internal float CoverageAt(Vector3 point) => Mathf.Max(0, coveredRadius - Vector3.Distance(origin, point));

    internal void SetRegion(Vector3 point)
    {
        if (anchored && (point - origin).sqrMagnitude < .01f) return;
        Reset(); origin = point; anchored = true;
    }
    internal void Reset()
    {
        renderers.Clear(); groups.Clear(); retained.Clear(); geometry.Reset();
        refreshedAt = float.NegativeInfinity; coveredRadius = 0; anchored = false; epoch = -1;
        geometryFrame = -1; Revision++;
    }
    internal void Ensure(Vector3 requested)
    {
        long currentEpoch = CameraSpatialScene.Shared.Epoch;
        if (epoch != currentEpoch)
        {
            // Keep an explicitly supplied regional anchor through construction
            // revisions, but revoke all observations from the preceding epoch.
            Vector3 anchor = anchored ? origin : requested;
            Reset(); origin = anchor; anchored = true; epoch = currentEpoch;
        }
        float now = Time.unscaledTime;
        if (!ScenicObservationPolicy.CatalogueDue(now, refreshedAt)) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        renderers.Clear(); groups.Clear(); retained.Clear(); omitted = 0;
        coveredRadius = Radius; refreshedAt = now; Revision++;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled || renderer.bounds.SqrDistance(origin) > (Radius + 1) * (Radius + 1) ||
                renderer.GetComponentInParent<Character>() || !ScenicVisibility.Vegetation(renderer.transform)) continue;
            LODGroup? group = renderer.GetComponentInParent<LODGroup>();
            if (!group) { Add(renderer); continue; }
            // Bounded discovery metadata too. An unexamined overflow cannot
            // certify any local foreground; it is explicitly incomplete.
            if (groups.Count >= Capacity) { coveredRadius = 0; omitted++; break; }
            if (!groups.Add(group!.GetInstanceID())) continue;
            LOD[] levels = group.GetLODs(); bool found = false;
            if (levels.Length > 0)
                foreach (Renderer detail in levels[0].renderers)
                    if (detail && detail.enabled && detail.gameObject.activeInHierarchy) { Add(detail); found = true; }
            if (!found) { groups.Remove(group.GetInstanceID()); Add(renderer); }
        }
        if (ScenicTrace.Enabled)
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("catalogue").Field("revision", Revision)
                .Field("retained", renderers.Count).Field("omitted", omitted).Field("radius", coveredRadius)
                .Field("milliseconds", (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency), "origin", origin));
    }
    private void Add(Renderer renderer)
    {
        if (retained.Contains(renderer.GetInstanceID())) return;
        Bounds bounds = renderer.bounds;
        if (!FoliageGeometryMath.Valid(bounds)) { coveredRadius = 0; omitted++; return; }
        bounds.Expand(.4f);
        float distance = Mathf.Sqrt(bounds.SqrDistance(origin));
        if (distance >= Radius) return;
        if (renderers.Count >= Capacity) { coveredRadius = Mathf.Min(coveredRadius, distance); omitted++; return; }
        retained.Add(renderer.GetInstanceID()); renderers.Add(renderer);
    }
    internal Bounds[]? Geometry(Renderer renderer)
    {
        if (geometryFrame != Time.frameCount) { geometryFrame = Time.frameCount; geometry.BeginSurvey(); }
        return geometry.Get(renderer);
    }
}
