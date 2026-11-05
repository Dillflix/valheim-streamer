using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Bounds of actual triangle groups, not a solid box around an entire tree.
// This is still conservative geometry, not texture-alpha or wind evaluation.
internal static class FoliageGeometryMath
{
    internal const int MaximumVertices = 16384, MaximumIndices = 36000, MaximumCells = 64;
    internal const float MaximumCanopyLift = 32f;

    // A bounded proposal for a different immutable route, never a live camera
    // correction or proof of visibility. Use the whole nearby vertical column
    // so escaping one leaf cell does not merely enter another cell above it.
    internal static bool TryCanopyLift(Vector3 camera, Bounds[] occupied, int count,
        bool surveyComplete, float foregroundRange, out float lift, bool measuredForegroundObstruction = false)
    {
        lift = 0;
        if (!surveyComplete || !Finite(camera) || occupied == null || count <= 0 || count > occupied.Length ||
            !Finite(foregroundRange) || foregroundRange <= 0 || foregroundRange > 16f) return false;
        bool inside = false, nearby = false;
        float top = camera.y, radiusSquared = foregroundRange * foregroundRange;
        for (int i = 0; i < count; i++)
        {
            Bounds bounds = occupied[i];
            if (!Valid(bounds)) return false;
            Vector3 min = bounds.min, max = bounds.max;
            float dx = camera.x < min.x ? min.x - camera.x : camera.x > max.x ? camera.x - max.x : 0;
            float dz = camera.z < min.z ? min.z - camera.z : camera.z > max.z ? camera.z - max.z : 0;
            if (dx == 0 && dz == 0 && camera.y >= min.y && camera.y <= max.y) inside = true;
            if (dx * dx + dz * dz <= radiusSquared)
            {
                float dy = camera.y < min.y ? min.y - camera.y : camera.y > max.y ? camera.y - max.y : 0;
                top = Math.Max(top, max.y); nearby |= dx * dx + dy * dy + dz * dz <= radiusSquared;
            }
        }
        float required = top + 1f - camera.y;
        // Do not cap an excessive lift down to a height that remains in canopy.
        if (!(inside || measuredForegroundObstruction && nearby) || !Finite(required) || required <= 0 || required > MaximumCanopyLift) return false;
        lift = required;
        return true;
    }

    internal static bool TryBuild(Vector3[] vertices, int[] indices, out Bounds[] cells)
    {
        cells = Array.Empty<Bounds>();
        if (vertices == null || indices == null || vertices.Length == 0 || vertices.Length > MaximumVertices ||
            indices.Length == 0 || indices.Length > MaximumIndices || indices.Length % 3 != 0) return false;
        Bounds whole = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 vertex in vertices)
        {
            if (!Finite(vertex)) return false;
            whole.Encapsulate(vertex);
        }
        if (whole.size.sqrMagnitude < .000001f) return false;
        var grouped = new Dictionary<int, Bounds>(MaximumCells);
        Vector3 min = whole.min, size = whole.size;
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) return false;
            Vector3 va = vertices[a], vb = vertices[b], vc = vertices[c];
            Vector3 centre = (va + vb + vc) / 3f;
            int key = Cell(centre.x, min.x, size.x) + 4 * Cell(centre.y, min.y, size.y) + 16 * Cell(centre.z, min.z, size.z);
            Bounds triangle = new Bounds(va, Vector3.zero); triangle.Encapsulate(vb); triangle.Encapsulate(vc);
            if (grouped.TryGetValue(key, out Bounds existing)) { existing.Encapsulate(triangle); grouped[key] = existing; }
            else grouped.Add(key, triangle);
        }
        // A triangle crossing a cell boundary is retained in full in its group;
        // centroid partitioning never clips or omits its actual extent.
        cells = new Bounds[grouped.Count]; int at = 0;
        for (int key = 0; key < MaximumCells; key++)
            if (grouped.TryGetValue(key, out Bounds bound)) cells[at++] = bound;
        return cells.Length > 0;
    }

    internal static Bounds Transform(Bounds local, Matrix4x4 matrix)
    {
        Vector3 e = local.extents;
        Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
        Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
        Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
        Vector3 extent = new Vector3(Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x),
            Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y), Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z));
        return new Bounds(matrix.MultiplyPoint3x4(local.center), extent * 2f);
    }
    // Empty renderers, missing world bounds and malformed geometry are different
    // observations. Never turn one renderer's missing bounds into a scene-wide
    // absence of evidence. A local fallback is transformed, not used as world space.
    internal enum BoundsSource { Empty, World, Local, Unknown }
    internal static BoundsSource ResolveBounds(Bounds world, Bounds local, Matrix4x4 matrix,
        bool empty, out Bounds resolved)
    {
        resolved = default;
        if (empty) return BoundsSource.Empty;
        if (Valid(world)) { resolved = world; return BoundsSource.World; }
        if (Valid(local))
        {
            Bounds transformed = Transform(local, matrix);
            if (Valid(transformed)) { resolved = transformed; return BoundsSource.Local; }
        }
        return BoundsSource.Unknown;
    }
    internal static bool Valid(Bounds bounds) => Finite(bounds.center) && Finite(bounds.extents) &&
        Finite(bounds.min) && Finite(bounds.max) &&
        bounds.extents.x >= 0 && bounds.extents.y >= 0 && bounds.extents.z >= 0 && bounds.size.sqrMagnitude > .000001f;
    private static int Cell(float value, float min, float size) => size <= .000001f ? 0 :
        Math.Max(0, Math.Min(3, (int)((value - min) * 4f / size)));
    private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
