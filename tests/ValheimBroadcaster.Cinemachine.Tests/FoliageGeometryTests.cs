using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Plugin;

internal static class FoliageGeometryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Tree mesh occupancy preserves open air beneath a wide crown", TrunkAndCrown);
        yield return ("Triangle groups preserve geometry crossing spatial cell boundaries", CrossingTriangle);
        yield return ("Dense mesh occupancy remains bounded without omitting triangles", BoundedGeometry);
        yield return ("Malformed and over-budget foliage geometry is rejected before refinement", InvalidGeometry);
        yield return ("Foliage bounds transform correctly through rotation and signed nonuniform scale", TransformedBounds);
        yield return ("Thin leaf surfaces retain their measured extent", ThinLeaves);
    }

    private static void TrunkAndCrown()
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        Box(vertices, indices, new Vector3(-.3f, 0, -.3f), new Vector3(.3f, 8, .3f));
        Box(vertices, indices, new Vector3(-5, 8, -5), new Vector3(5, 14, 5));
        Check(FoliageGeometryMath.TryBuild(vertices.ToArray(), indices.ToArray(), out Bounds[] cells), "Tree geometry was not retained.");
        var emptyAir = new Vector3(2, 3, 0);
        Check(Contains(new Bounds(new Vector3(0, 7, 0), new Vector3(10, 14, 10)), emptyAir), "Fixture does not reproduce the whole-tree false positive.");
        foreach (Bounds cell in cells) Check(!Contains(cell, emptyAir), "Empty air below the crown is still classified as solid tree occupancy.");
        Check(Any(cells, new Vector3(.3f, 3, 0)), "Trunk was lost while opening empty air.");
        Check(Any(cells, new Vector3(3, 14, 3)), "Crown was lost while opening empty air.");
    }

    private static void CrossingTriangle()
    {
        Vector3[] vertices = { new Vector3(-10, -3, -7), new Vector3(12, -2, -8), new Vector3(0, 15, 10),
            new Vector3(-12, 12, 9), new Vector3(-11, 13, 10), new Vector3(-10, 12, 9) };
        Check(FoliageGeometryMath.TryBuild(vertices, new[] { 0, 1, 2, 3, 4, 5 }, out Bounds[] cells), "Crossing mesh failed.");
        for (int a = 0; a <= 20; a++)
        for (int b = 0; b <= 20 - a; b++)
        {
            Vector3 sample = vertices[0] * (a / 20f) + vertices[1] * (b / 20f) + vertices[2] * ((20 - a - b) / 20f);
            Check(Any(cells, sample), "A triangle crossing cell boundaries was clipped.");
        }
    }

    private static void BoundedGeometry()
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        for (int x = 0; x < 8; x++)
        for (int y = 0; y < 8; y++)
        for (int z = 0; z < 8; z++)
        {
            int at = vertices.Count;
            vertices.Add(new Vector3(x, y, z)); vertices.Add(new Vector3(x + .1f, y, z)); vertices.Add(new Vector3(x, y + .1f, z));
            indices.Add(at); indices.Add(at + 1); indices.Add(at + 2);
        }
        Check(FoliageGeometryMath.TryBuild(vertices.ToArray(), indices.ToArray(), out Bounds[] cells), "Dense mesh failed.");
        Check(cells.Length <= FoliageGeometryMath.MaximumCells, "Cell count exceeded bounded storage.");
        foreach (Vector3 vertex in vertices) Check(Any(cells, vertex), "Dense mesh dropped a triangle vertex.");
    }

    private static void InvalidGeometry()
    {
        Vector3[] triangle = { Vector3.zero, Vector3.right, Vector3.up };
        Check(!FoliageGeometryMath.TryBuild(triangle, new[] { 0, 1 }, out _), "Incomplete triangle was accepted.");
        Check(!FoliageGeometryMath.TryBuild(triangle, new[] { 0, 1, 4 }, out _), "Out-of-range index was accepted.");
        Check(!FoliageGeometryMath.TryBuild(triangle, new[] { 0, -1, 2 }, out _), "Negative index was accepted.");
        Check(!FoliageGeometryMath.TryBuild(new[] { new Vector3(float.NaN, 0, 0), Vector3.right, Vector3.up }, new[] { 0, 1, 2 }, out _), "Non-finite geometry was accepted.");
        Check(!FoliageGeometryMath.TryBuild(new Vector3[FoliageGeometryMath.MaximumVertices + 1], new[] { 0, 1, 2 }, out _), "Vertex budget was ignored.");
        Check(!FoliageGeometryMath.TryBuild(triangle, new int[FoliageGeometryMath.MaximumIndices + 3], out _), "Index budget was ignored.");
        Check(!FoliageGeometryMath.TryBuild(Array.Empty<Vector3>(), Array.Empty<int>(), out _), "Empty geometry was accepted.");
    }

    private static void TransformedBounds()
    {
        Bounds local = new Bounds(new Vector3(2, 4, -3), new Vector3(4, 8, 6));
        // A 90-degree rotation about Z with signed nonuniform scale and offset.
        // Build the matrix directly: no Unity scene or engine-native TRS call.
        Matrix4x4 matrix = Matrix4x4.identity;
        matrix.m00 = 0; matrix.m10 = -2; matrix.m01 = -3; matrix.m11 = 0; matrix.m22 = .5f;
        matrix.m03 = 100; matrix.m13 = -7; matrix.m23 = 30;
        Bounds actual = FoliageGeometryMath.Transform(local, matrix);
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(x, y, z));
            Vector3 world = matrix.MultiplyPoint3x4(corner);
            min = Vector3.Min(min, world); max = Vector3.Max(max, world);
            Check(Contains(actual, world), "World transform omitted a mesh-bound corner.");
        }
        Check((actual.min - min).sqrMagnitude < .000001f && (actual.max - max).sqrMagnitude < .000001f,
            "World bounds expanded or shifted beyond the transformed corners.");
    }

    private static void ThinLeaves()
    {
        Vector3[] vertices = { new Vector3(-1, 6, 0), new Vector3(1, 6, 0), new Vector3(0, 8, 0) };
        Check(FoliageGeometryMath.TryBuild(vertices, new[] { 0, 1, 2 }, out Bounds[] cells), "A planar leaf was treated as missing geometry.");
        Check(cells.Length == 1 && FoliageGeometryMath.Valid(cells[0]), "Planar bounds were invalid.");
        Check(Any(cells, new Vector3(0, 7, 0)) && !Any(cells, new Vector3(0, 7, 1)), "Planar leaf extent was changed.");
    }

    private static void Box(List<Vector3> vertices, List<int> indices, Vector3 min, Vector3 max)
    {
        int start = vertices.Count;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++) vertices.Add(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
        foreach (int index in new[] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1,
            2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 }) indices.Add(start + index);
    }
    private static bool Any(Bounds[] bounds, Vector3 point)
    { foreach (Bounds bound in bounds) if (Contains(bound, point)) return true; return false; }
    private static bool Contains(Bounds b, Vector3 p) => p.x >= b.min.x - .0001f && p.x <= b.max.x + .0001f &&
        p.y >= b.min.y - .0001f && p.y <= b.max.y + .0001f && p.z >= b.min.z - .0001f && p.z <= b.max.z + .0001f;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
