using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimBroadcaster.Plugin;

// Read existing shared geometry only. No new Mesh objects, colliders, helper
// physics or read/write flag changes are introduced into the game scene.
internal sealed class ScenicFoliageGeometry
{
    private const int MaximumMeshes = 64, MaximumSubmeshes = 16;
    private readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>(MaximumMeshes);
    private int remainingReads, sequence;
    private sealed class Entry
    {
        internal Mesh Mesh = null!;
        internal Bounds[]? Cells;
        internal bool Refined, CanRefine;
        internal int Used;
    }

    internal void BeginSurvey() { remainingReads = 2; sequence++; }
    internal void Reset() { cache.Clear(); sequence = remainingReads = 0; }

    internal Bounds[]? Get(Renderer renderer)
    {
        // Bone deformation cannot be represented by immutable mesh-local cells.
        if (!(renderer is MeshRenderer)) return null;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>();
        Mesh? mesh = filter ? filter.sharedMesh : null;
        if (!mesh) return null;
        int id = mesh!.GetInstanceID();
        if (!cache.TryGetValue(id, out Entry? entry) || entry.Mesh != mesh)
        {
            if (cache.Count >= MaximumMeshes)
            {
                int oldest = 0, age = int.MaxValue;
                foreach (var pair in cache) if (pair.Value.Used < age) { oldest = pair.Key; age = pair.Value.Used; }
                cache.Remove(oldest);
            }
            entry = new Entry { Mesh = mesh, Cells = SubmeshBounds(mesh), CanRefine = CanRead(mesh) };
            cache[id] = entry;
        }
        entry.Used = sequence;
        // A budget-deferred fallback remains eligible on the next survey.
        // It is not cached permanently as if it were a completed refinement.
        if (!entry.Refined && entry.CanRefine && remainingReads > 0)
        {
            remainingReads--; entry.CanRefine = false;
            try
            {
                if (FoliageGeometryMath.TryBuild(mesh.vertices, mesh.triangles, out Bounds[] cells))
                { entry.Cells = cells; entry.Refined = true; }
            }
            catch (UnityException) { /* Retain measured submesh/renderer bounds. */ }
        }
        return entry.Cells;
    }

    private static bool CanRead(Mesh mesh)
    {
        if (!mesh.isReadable || mesh.vertexCount <= 0 || mesh.vertexCount > FoliageGeometryMath.MaximumVertices ||
            mesh.subMeshCount <= 0 || mesh.subMeshCount > MaximumSubmeshes) return false;
        ulong count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (mesh.GetTopology(i) != MeshTopology.Triangles) return false;
            count += mesh.GetIndexCount(i);
            if (count > FoliageGeometryMath.MaximumIndices) return false;
        }
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
