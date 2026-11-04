using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Scene-owned renderer discovery and mesh refinement. Candidate,
/// preflight and on-air queries never move this catalogue's survey centre.</summary>
internal sealed class ScenicFoliageScene
{
    internal static readonly ScenicFoliageScene Shared = new ScenicFoliageScene();
    private const int Capacity = 8192;
    private const float Radius = FoliageCoverageMap.Radius;
    private List<Observation> renderers = new List<Observation>(Capacity);
    private List<Observation> collecting = new List<Observation>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private readonly HashSet<int> seen = new HashSet<int>();
    private readonly ScenicFoliageGeometry geometry = new ScenicFoliageGeometry();
    private FoliageCoverageMap coverage = new FoliageCoverageMap();
    private FoliageCoverageMap collectingCoverage = new FoliageCoverageMap();
    private readonly List<GameObject> roots = new List<GameObject>(4096);
    private readonly List<Renderer> nodeRenderers = new List<Renderer>(4);
    private readonly List<Traversal> traversal = new List<Traversal>(64);
    private readonly HashSet<int> sceneHandles = new HashSet<int>();
    private readonly HashSet<int> publishedIds = new HashSet<int>();
    private FoliageCoverageMap additions = new FoliageCoverageMap();
    private FoliageCoverageMap newAdditions = new FoliageCoverageMap();
    private readonly FoliageCoverageMap pendingAdditions = new FoliageCoverageMap();
    private int sceneIndex, rootIndex, advancedFrame = -1, stepsThisFrame, totalSteps;
    private bool scanning, supplemental, published;
    private long previousRevision;
    private float scanStartedAt, nextProgressTrace;
    private readonly struct Traversal
    {
        internal readonly Transform Node; internal readonly int Child;
        internal Traversal(Transform node, int child) { Node = node; Child = child; }
    }
    private Vector3 origin, publishedOrigin;
    private bool anchored;
    private long epoch = -1;
    private float refreshedAt = float.NegativeInfinity;
    private int geometryFrame = -1, omitted, empty, repaired, unresolved, metadataOverflow;
    private readonly int[] sampleCounts = new int[4];
    internal long Revision { get; private set; }
    internal long Generation { get; private set; }
    internal IReadOnlyList<Observation> Renderers => renderers;
    internal float CoverageAt(Vector3 point, float maximum = ScenicObservationPolicy.DetailRadius) =>
        published ? coverage.CoverageAt(point, maximum) : 0;
    internal long Epoch => epoch;
    internal bool Updating => scanning;
    internal string Diagnostics => $"Foliage: {renderers.Count} retained; revision {Revision}; scan steps {totalSteps}, roots {rootIndex}/{roots.Count}; {(published ? "observed" : "initial survey")}{(scanning ? "; refresh in progress" : "")}";
    // A projection from the preceding completed catalogue remains useful away
    // from additions. Newly discovered geometry is explicitly unknown until
    // that projection incorporates it; removals need no artificial blackout.
    internal float CoverageFor(long revision, Vector3 point, float maximum)
    {
        float available = CoverageAt(point, maximum);
        if (scanning) available = pendingAdditions.CoverageAt(point, available);
        if (revision == Revision) return available;
        if (revision != previousRevision) return 0;
        return additions.CoverageAt(point, available);
    }

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
        if (!TargetRoster.Finite(point)) return;
        if (anchored && (point - origin).sqrMagnitude < 96 * 96) return;
        if (!scanning) { origin = point; anchored = true; refreshedAt = float.NegativeInfinity; }
    }
    internal void Reset()
    {
        renderers.Clear(); collecting.Clear(); groups.Clear(); seen.Clear(); geometry.Reset(); coverage.Reset(origin);
        collectingCoverage.Reset(origin); roots.Clear(); nodeRenderers.Clear(); traversal.Clear(); sceneHandles.Clear();
        publishedIds.Clear(); additions.Reset(origin); newAdditions.Reset(origin); pendingAdditions.Reset(origin); scanning = supplemental = published = false;
        refreshedAt = float.NegativeInfinity; anchored = false; epoch = -1;
        sceneIndex = rootIndex = stepsThisFrame = 0; advancedFrame = geometryFrame = -1; previousRevision = Revision++;
    }
    internal void Ensure(Vector3 requested)
    {
        long currentEpoch = CameraSpatialScene.Shared.WorldEpoch;
        Vector3 anchor = Player.m_localPlayer ? Player.m_localPlayer.transform.position : requested;
        if (epoch != currentEpoch || !anchored)
        {
            Reset(); origin = anchor; anchored = true; epoch = currentEpoch;
        }
        if (!scanning && (anchor - origin).sqrMagnitude > 96 * 96)
        { origin = anchor; refreshedAt = float.NegativeInfinity; }
        float now = Time.unscaledTime;
        if (advancedFrame != Time.frameCount) { advancedFrame = Time.frameCount; stepsThisFrame = 0; }
        if (!scanning && now >= refreshedAt && now - refreshedAt < 2) return;
        if (stepsThisFrame >= 1024 || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) return;
        using (lease)
        {
            if (!scanning) Begin(now);
            int batch = 0;
            while (batch++ < 256 && stepsThisFrame++ < 1024 && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation))
            {
                if (!Advance()) { Publish(now); break; }
            }
            if (scanning && ScenicTrace.Enabled && now >= nextProgressTrace)
            {
                nextProgressTrace = now + 1;
                ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("foliage-catalogue-progress")
                    .Field("steps", totalSteps).Field("rootsDone", rootIndex).Field("roots", roots.Count)
                    .Field("sceneIndex", sceneIndex).Field("retained", renderers.Count).Field("collected", collecting.Count)
                    .Field("published", published).Field("revision", Revision).Field("generation", Generation)
                    .Field("elapsed", now - scanStartedAt).Field("cpu", CameraWorkBudget.Shared.Diagnostics), "origin", origin));
            }
        }
    }

    private void Begin(float now)
    {
        collecting.Clear(); collectingCoverage.Reset(origin); groups.Clear(); seen.Clear();
        pendingAdditions.Reset(published ? publishedOrigin : origin);
        roots.Clear(); traversal.Clear(); sceneHandles.Clear(); nodeRenderers.Clear();
        sceneIndex = rootIndex = totalSteps = 0; scanning = true; supplemental = false; scanStartedAt = now;
        omitted = empty = repaired = unresolved = metadataOverflow = 0;
        Array.Clear(sampleCounts, 0, sampleCounts.Length);
    }

    private bool Advance()
    {
        totalSteps++;
        if (traversal.Count == 0)
        {
            while (rootIndex >= roots.Count)
            {
                roots.Clear(); rootIndex = 0;
                if (sceneIndex < SceneManager.sceneCount)
                {
                    Scene current = SceneManager.GetSceneAt(sceneIndex++);
                    if (!current.IsValid() || !current.isLoaded || !sceneHandles.Add(current.handle)) continue;
                    // Unity's root-registry snapshot is one indivisible native
                    // operation. Reuse storage, measure its cost, then slice all
                    // hierarchy traversal; never allocate a scene-wide renderer array.
                    current.GetRootGameObjects(roots);
                    if (roots.Count > 65536)
                    { roots.RemoveRange(65536, roots.Count - 65536); collectingCoverage.Invalidate(); metadataOverflow++; }
                    return true;
                }
                if (!supplemental)
                {
                    supplemental = true;
                    // Runtime-created world objects may be parented in Unity's
                    // persistent scene, which SceneManager does not enumerate.
                    if (ZNetScene.instance && !sceneHandles.Contains(ZNetScene.instance.gameObject.scene.handle))
                    { ZNetScene.instance.gameObject.scene.GetRootGameObjects(roots); return true; }
                }
                return false;
            }
            GameObject root = roots[rootIndex++];
            if (root && root.activeInHierarchy) traversal.Add(new Traversal(root.transform, -1));
            return true;
        }
        int last = traversal.Count - 1;
        Traversal item = traversal[last];
        if (!item.Node || !item.Node.gameObject.activeInHierarchy) { traversal.RemoveAt(last); return true; }
        if (item.Child < 0)
        {
            traversal[last] = new Traversal(item.Node, 0);
            // Actor renderers are owned by the separate shared body survey.
            if (item.Node.GetComponent<Character>()) { traversal.RemoveAt(last); return true; }
            nodeRenderers.Clear(); item.Node.GetComponents(nodeRenderers);
            foreach (Renderer renderer in nodeRenderers) Inspect(renderer);
            return true;
        }
        if (item.Child >= item.Node.childCount) { traversal.RemoveAt(last); return true; }
        traversal[last] = new Traversal(item.Node, item.Child + 1);
        Transform child = item.Node.GetChild(item.Child);
        if (traversal.Count >= 128) { collectingCoverage.Invalidate(); metadataOverflow++; return true; }
        traversal.Add(new Traversal(child, -1)); return true;
    }

    private void Inspect(Renderer renderer)
    {
        if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !Vegetation(renderer)) return;
        Bounds world = renderer.bounds;
        if (FoliageGeometryMath.Valid(world) && world.SqrDistance(origin) > (Radius + 1) * (Radius + 1)) return;
        if (renderer.GetComponentInParent<Character>()) return;
        LODGroup? group = renderer.GetComponentInParent<LODGroup>();
        if (!group) { Add(renderer, null); return; }
        if (groups.Contains(group!.GetInstanceID())) return;
        if (groups.Count >= Capacity) { metadataOverflow++; Add(renderer, group); return; }
        groups.Add(group.GetInstanceID());
        LOD[] levels = group.GetLODs(); bool found = false;
        if (levels.Length > 0)
            foreach (Renderer detail in levels[0].renderers)
                if (detail && detail.enabled && detail.gameObject.activeInHierarchy) found |= Add(detail, group);
        if (!found) { groups.Remove(group.GetInstanceID()); Add(renderer, group); }
    }

    private static bool Vegetation(Renderer renderer)
    {
        if (ScenicVisibility.Vegetation(renderer.transform)) return true;
        Material material = renderer.sharedMaterial;
        string shader = material && material.shader ? material.shader.name : "";
        return shader.IndexOf("vegetation", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shader.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shader.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void Publish(float now)
    {
        newAdditions.Reset(published ? publishedOrigin : origin);
        if (collectingCoverage.Unbounded) newAdditions.Invalidate();
        bool changed = !published || origin != publishedOrigin || renderers.Count != collecting.Count ||
            coverage.UnknownCells != collectingCoverage.UnknownCells || coverage.Unbounded != collectingCoverage.Unbounded;
        foreach (Observation observation in collecting)
        {
            if (publishedIds.Contains(observation.Renderer.GetInstanceID())) continue;
            changed = true;
            if (observation.TryBounds(out Bounds bound)) newAdditions.AddUnknown(bound);
            else newAdditions.Invalidate();
        }
        // Unknown cells can move without changing their count. Their presence
        // conservatively requires new projection evidence each completed sweep.
        changed |= collectingCoverage.UnknownCells > 0;
        List<Observation> previous = renderers; renderers = collecting; collecting = previous;
        FoliageCoverageMap previousCoverage = coverage; coverage = collectingCoverage; collectingCoverage = previousCoverage;
        publishedIds.Clear(); foreach (Observation observation in renderers) if (observation.Renderer) publishedIds.Add(observation.Renderer.GetInstanceID());
        if (changed)
        { previousRevision = Revision; Revision++; FoliageCoverageMap oldDelta = additions; additions = newAdditions; newAdditions = oldDelta; }
        Generation++; publishedOrigin = origin;
        refreshedAt = now; scanning = false; published = true; roots.Clear(); traversal.Clear();
        if (ScenicTrace.Enabled)
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("catalogue").Field("revision", Revision)
                .Field("retained", renderers.Count).Field("omitted", omitted).Field("empty", empty)
                .Field("repaired", repaired).Field("unresolved", unresolved).Field("unknownCells", coverage.UnknownCells)
                .Field("unbounded", coverage.Unbounded).Field("metadataOverflow", metadataOverflow)
                .Field("radius", Radius).Field("originCoverage", CoverageAt(origin))
                .Field("elapsed", now - scanStartedAt).Field("incremental", true), "origin", origin));
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
            if (FoliageGeometryMath.Valid(envelope)) { collectingCoverage.AddUnknown(envelope); PendingUnknown(envelope); }
            else { collectingCoverage.Invalidate(); pendingAdditions.Invalidate(); }
            TraceSample(renderer, source, world, local, envelope); return false;
        }
        bounds.Expand(.4f);
        if (bounds.SqrDistance(origin) >= Radius * Radius) return false;
        if (source == FoliageGeometryMath.BoundsSource.Local)
        { repaired++; TraceSample(renderer, source, world, local, bounds); }
        if (collecting.Count >= Capacity) { collectingCoverage.AddUnknown(bounds); omitted++; return false; }
        if (!publishedIds.Contains(id)) PendingUnknown(bounds);
        collecting.Add(new Observation(renderer, local)); return true;
    }

    private void PendingUnknown(Bounds bounds)
    {
        pendingAdditions.AddUnknown(bounds);
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
    internal Bounds[]? Geometry(Renderer renderer, out bool pending)
    {
        if (geometryFrame != Time.frameCount) { geometryFrame = Time.frameCount; geometry.BeginSurvey(); }
        return geometry.Get(renderer, out pending);
    }
}
