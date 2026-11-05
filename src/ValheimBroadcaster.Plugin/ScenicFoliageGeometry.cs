using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal enum FoliageGeometryPrecision { RendererBounds, SubmeshBounds, TriangleClusters, Triangles }
internal enum FoliageGeometryAvailability { Pending, Exact, Unsupported }

// Read existing shared geometry only. No new Mesh objects, colliders, helper
// physics or read/write flag changes are introduced into the game scene.
internal sealed class ScenicFoliageGeometry
{
    private const int MaximumMeshes = 64, MaximumSubmeshes = 16;
    private readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>(MaximumMeshes);
    private int remainingReads, sequence;
    private readonly Queue<(Renderer Renderer, int Id)> demands = new Queue<(Renderer, int)>();
    private readonly HashSet<int> demanded = new HashSet<int>();
    private sealed class Entry
    {
        internal Mesh Mesh = null!;
        internal Bounds[]? Cells;
        internal bool Refined, CanRefine;
        internal FoliageTriangleGeometry.Builder? Builder;
        internal FoliageTriangleGeometry? Triangles;
        internal string UnavailableReason = "Mesh has not been read";
        internal int Used;
    }

    internal void BeginSurvey() { remainingReads = 2; sequence++; }
    internal void Reset() { cache.Clear(); demands.Clear(); demanded.Clear(); sequence = remainingReads = 0; }

    private void Demand(Renderer renderer)
    {
        int id = renderer.GetInstanceID();
        if (demands.Count < MaximumMeshes && demanded.Add(id)) demands.Enqueue((renderer, id));
    }
    internal void WarmDemand()
    {
        if (demands.Count == 0 || CameraWorkBudget.Shared.WithinLive) return;
        var item = demands.Dequeue(); demanded.Remove(item.Id);
        if (!item.Renderer) return;
        Exact(item.Renderer, out FoliageGeometryAvailability availability, out _);
        if (availability == FoliageGeometryAvailability.Pending) Demand(item.Renderer);
    }

    internal Bounds[]? Get(Renderer renderer, out bool pending)
    {
        pending = false;
        // Bone deformation cannot be represented by immutable mesh-local cells.
        if (!(renderer is MeshRenderer)) return null;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        Mesh? mesh = filter ? filter.sharedMesh : null;
        if (!mesh) return null;
        int id = mesh!.GetInstanceID();
        if (!cache.TryGetValue(id, out Entry? entry) || entry.Mesh != mesh)
        {
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease metadataLease)) { pending = true; return null; }
            using (metadataLease)
            {
            if (cache.Count >= MaximumMeshes)
            {
                int oldest = 0, age = int.MaxValue;
                foreach (var pair in cache) if (pair.Value.Used < age) { oldest = pair.Key; age = pair.Value.Used; }
                cache.Remove(oldest);
            }
            bool canRead = CanRead(mesh, out string unsupported);
            entry = new Entry { Mesh = mesh, Cells = SubmeshBounds(mesh), CanRefine = canRead, UnavailableReason = unsupported };
            cache[id] = entry;
            }
        }
        entry.Used = sequence;
        // A budget-deferred fallback remains eligible on the next survey.
        // It is not cached permanently as if it were a completed refinement.
        if (!entry.Refined && entry.CanRefine && remainingReads > 0 && !CameraWorkBudget.Shared.WithinLive &&
            CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease geometryLease))
        using (geometryLease)
        {
            remainingReads--; entry.CanRefine = false;
            try
            {
                Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles;
                if (FoliageGeometryMath.TryBuild(vertices, indices, out Bounds[] cells))
                {
                    entry.Cells = cells; entry.Refined = true;
                    entry.Builder = FoliageTriangleGeometry.BeginBuild(vertices, indices);
                    entry.UnavailableReason = "Exact triangle hierarchy is pending";
                }
                else entry.UnavailableReason = "Mesh triangle validation failed";
            }
            catch (UnityException) { entry.UnavailableReason = "Mesh CPU read failed"; }
            catch (ArgumentException) { entry.UnavailableReason = "Mesh triangle hierarchy input is unsupported"; }
        }
        pending = !entry.Refined && entry.CanRefine;
        return entry.Cells;
    }

    internal FoliageGeometryPrecision Precision(Renderer renderer)
    {
        MeshFilter? filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
        Mesh? mesh = filter ? filter.sharedMesh : null;
        if (!mesh || !cache.TryGetValue(mesh!.GetInstanceID(), out Entry? entry) || entry.Mesh != mesh)
            return FoliageGeometryPrecision.RendererBounds;
        return entry.Refined ? FoliageGeometryPrecision.TriangleClusters : entry.Cells != null
            ? FoliageGeometryPrecision.SubmeshBounds : FoliageGeometryPrecision.RendererBounds;
    }

    internal FoliageTriangleGeometry? Exact(Renderer renderer, out FoliageGeometryAvailability availability, out string reason)
    {
        availability = FoliageGeometryAvailability.Unsupported;
        reason = "Renderer has no supported static triangle geometry";
        bool live = CameraWorkBudget.Shared.WithinLive, pending = false;
        // A live pose may resume bounded triangle traversal, but it cannot
        // promote a new whole-mesh CPU read or hierarchy build into live work.
        if (!live) Get(renderer, out pending);
        if (!(renderer is MeshRenderer)) return null;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>(); Mesh? mesh = filter ? filter.sharedMesh : null;
        if (!mesh) { reason = "Renderer has no shared mesh"; return null; }
        if (!cache.TryGetValue(mesh!.GetInstanceID(), out Entry? entry) || entry.Mesh != mesh)
        {
            if (!mesh.isReadable) { reason = "Mesh CPU data is not readable"; return null; }
            availability = FoliageGeometryAvailability.Pending; reason = "Mesh metadata work is pending"; if (live) Demand(renderer); return null;
        }
        if (entry.Triangles != null)
        { availability = FoliageGeometryAvailability.Exact; reason = "Exact static mesh triangles"; return entry.Triangles; }
        if (entry.Builder != null)
        {
            availability = FoliageGeometryAvailability.Pending; reason = "Exact triangle hierarchy work is pending";
            if (live) { Demand(renderer); return null; }
            if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Observation, out CameraWorkBudget.Lease lease)) return null;
            using (lease)
            {
                try
                {
                    for (int i = 0; i < 8 && !entry.Builder.Complete && CameraWorkBudget.Shared.CanContinue(CameraWorkKind.Observation); i++)
                        entry.Builder.Step(64);
                }
                catch (ArgumentException) { entry.Builder = null; entry.UnavailableReason = "Exact triangle hierarchy data is invalid"; }
                catch (InvalidOperationException) { entry.Builder = null; entry.UnavailableReason = "Exact triangle hierarchy construction failed"; }
                if (entry.Builder == null)
                { availability = FoliageGeometryAvailability.Unsupported; reason = entry.UnavailableReason; return null; }
                if (!entry.Builder.Complete) return null;
                entry.Triangles = entry.Builder.Result; entry.Builder = null;
            }
            if (entry.Triangles != null)
            { availability = FoliageGeometryAvailability.Exact; reason = "Exact static mesh triangles"; return entry.Triangles; }
            entry.UnavailableReason = "Exact triangle hierarchy could not be built";
        }
        if (pending || entry.CanRefine) { availability = FoliageGeometryAvailability.Pending; reason = "Readable mesh CPU geometry work is pending"; if (live) Demand(renderer); }
        else { availability = FoliageGeometryAvailability.Unsupported; reason = entry.UnavailableReason; }
        return null;
    }

    private static bool CanRead(Mesh mesh, out string reason)
    {
        if (!mesh.isReadable) { reason = "Mesh CPU data is not readable"; return false; }
        if (mesh.vertexCount <= 0 || mesh.vertexCount > FoliageGeometryMath.MaximumVertices ||
            mesh.subMeshCount <= 0 || mesh.subMeshCount > MaximumSubmeshes)
        { reason = "Mesh vertex or submesh limits are unsupported"; return false; }
        ulong count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (mesh.GetTopology(i) != MeshTopology.Triangles) { reason = "Mesh topology is not triangles"; return false; }
            count += mesh.GetIndexCount(i);
            if (count > FoliageGeometryMath.MaximumIndices) { reason = "Mesh triangle index limit is exceeded"; return false; }
        }
        reason = count > 0 ? "Readable mesh awaits bounded CPU acquisition" : "Mesh has no triangle indices";
        return count > 0;
    }

    private static Bounds[]? SubmeshBounds(Mesh mesh)
    {
        int count = mesh.subMeshCount;
        if (count <= 0 || count > MaximumSubmeshes) return null;
        var bounds = new List<Bounds>(count);
        for (int i = 0; i < count; i++)
        {
            SubMeshDescriptor submesh = mesh.GetSubMesh(i);
            if (submesh.indexCount == 0) continue;
            if (!FoliageGeometryMath.Valid(submesh.bounds)) return null;
            bounds.Add(submesh.bounds);
        }
        return bounds.Count == 0 ? null : bounds.ToArray();
    }
}
