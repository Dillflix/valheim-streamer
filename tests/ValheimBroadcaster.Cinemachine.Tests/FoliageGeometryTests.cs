using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Plugin;

internal static class FoliageGeometryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Tree mesh occupancy preserves open air beneath a wide crown", TrunkAndCrown);
        yield return ("Empty geometry cannot invalidate a scene or manufacture a camera blocker", EmptyBounds);
        yield return ("Bad world bounds recover transformed local geometry instead of clearing coverage", RecoverBounds);
        yield return ("Malformed geometry remains unknown and planar leaves remain measured", UnknownBounds);
        yield return ("Unknown distant geometry cannot erase coverage near an unrelated camera", LocalUncertainty);
        yield return ("Coverage omissions retain full extent and boundary uncertainty", UncertaintyExtent);
        yield return ("Coverage reset removes prior region omissions and unlocatable data fails closed", ResetCoverage);
        yield return ("Dense omission streams stay bounded and never certify their occupied cells", BoundedCoverage);
        yield return ("Triangle groups preserve geometry crossing spatial cell boundaries", CrossingTriangle);
        yield return ("Dense mesh occupancy remains bounded without omitting triangles", BoundedGeometry);
        yield return ("Malformed and over-budget foliage geometry is rejected before refinement", InvalidGeometry);
        yield return ("Foliage bounds transform correctly through rotation and signed nonuniform scale", TransformedBounds);
        yield return ("Thin leaf surfaces retain their measured extent", ThinLeaves);
        yield return ("Canopy escape proposes clearance above stacked nearby leaf cells", CanopyEscape);
        yield return ("Canopy escape requires complete camera-inside evidence", CanopyEvidence);
        yield return ("Canopy escape rejects excessive lifts instead of clamping inside leaves", CanopyBounded);
        yield return ("Canopy escape ignores distant crowns and unused array entries", CanopyNeighborhood);
    }

    private static void EmptyBounds()
    {
        Bounds bogus = new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one);
        Check(FoliageGeometryMath.ResolveBounds(bogus, bogus, Matrix4x4.identity, true, out _) ==
            FoliageGeometryMath.BoundsSource.Empty, "An explicitly empty renderer became unknown geometry.");
        // The supplied run had 209 or 108 omitted entries well below capacity.
        // This fixture models verified-empty entries, not a recovered game mesh.
        var map = new FoliageCoverageMap(); map.Reset(Vector3.zero);
        for (int i = 0; i < 451; i++)
            Check(FoliageGeometryMath.ResolveBounds(default, default, Matrix4x4.identity, true, out _) ==
                FoliageGeometryMath.BoundsSource.Empty, "Empty entry consumed coverage.");
        Check(map.CoverageAt(Vector3.zero, 16) == 16, "Empty entries erased coverage.");
    }
    private static void RecoverBounds()
    {
        Bounds local = new Bounds(new Vector3(0, 8, 0), new Vector3(4, 16, 6));
        Matrix4x4 matrix = Matrix4x4.identity; matrix.m03 = 1000; matrix.m13 = 75; matrix.m23 = 2000;
        Check(FoliageGeometryMath.ResolveBounds(default, local, matrix, false, out Bounds resolved) ==
            FoliageGeometryMath.BoundsSource.Local && resolved.center == new Vector3(1000, 83, 2000),
            "Missing world bounds did not retain transformed local geometry.");
        Bounds world = new Bounds(new Vector3(20, 20, 20), Vector3.one);
        Check(FoliageGeometryMath.ResolveBounds(world, local, matrix, false, out resolved) ==
            FoliageGeometryMath.BoundsSource.World && resolved.center == world.center, "Valid world override was replaced.");
    }
    private static void UnknownBounds()
    {
        Bounds malformed = new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one);
        Check(FoliageGeometryMath.ResolveBounds(malformed, default, Matrix4x4.identity, false, out _) ==
            FoliageGeometryMath.BoundsSource.Unknown, "Unknown geometry became clear.");
        Matrix4x4 broken = Matrix4x4.identity; broken.m03 = float.PositiveInfinity;
        Check(FoliageGeometryMath.ResolveBounds(default, new Bounds(Vector3.zero, Vector3.one), broken, false, out _) ==
            FoliageGeometryMath.BoundsSource.Unknown, "Non-finite transform was admitted.");
        Bounds leaf = new Bounds(Vector3.zero, new Vector3(4, 0, 5));
        Check(FoliageGeometryMath.ResolveBounds(leaf, default, Matrix4x4.identity, false, out _) ==
            FoliageGeometryMath.BoundsSource.World, "Planar foliage was discarded as empty.");
    }
    private static void LocalUncertainty()
    {
        var map = new FoliageCoverageMap(); map.Reset(Vector3.zero);
        map.AddUnknown(new Bounds(new Vector3(100, 8, 8), new Vector3(10, 10, 10)));
        Check(map.CoverageAt(Vector3.zero, 16) == 16, "One distant failure poisoned the whole region.");
        Check(map.CoverageAt(new Vector3(100, 8, 8), 16) == 0, "Unknown space was advertised as clear.");
        Check(map.CoverageAt(new Vector3(220, 0, 0), 16) == 0, "Unsurveyed regional boundary became clear.");
        Check(map.CoverageAt(new Vector3(215, 0, 0), 16) == 5, "Regional boundary did not limit coverage.");
    }
    private static void UncertaintyExtent()
    {
        var map = new FoliageCoverageMap(); map.Reset(Vector3.zero);
        map.AddUnknown(new Bounds(new Vector3(30, 0, 0), new Vector3(60, 4, 4)));
        Check(map.CoverageAt(Vector3.zero, 16) == 0, "Only omission centre was retained; its nearby extent was lost.");
        map.Reset(Vector3.zero);
        map.AddUnknown(new Bounds(new Vector3(16, 8, 8), new Vector3(.1f, 4, 4)));
        Check(map.CoverageAt(new Vector3(15.9f, 8, 8), 16) == 0 &&
            map.CoverageAt(new Vector3(16.1f, 8, 8), 16) == 0, "Cell-boundary extent was clipped.");
        Check(map.CoverageAt(new Vector3(-7, 8, 8), 16) == 7, "Clearance to the unknown cell is wrong.");
    }
    private static void ResetCoverage()
    {
        var map = new FoliageCoverageMap(); map.Reset(Vector3.zero); map.Invalidate();
        Check(map.CoverageAt(new Vector3(60, 8, 8), 16) == 0, "Unlocatable geometry was silently ignored.");
        map.Reset(new Vector3(1000, 70, 2000));
        Check(map.UnknownCells == 0 && !map.Unbounded && map.CoverageAt(new Vector3(1000, 70, 2000), 16) == 16,
            "Old scene contaminated new region.");
        map.AddUnknown(new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one));
        Check(map.Unbounded, "Malformed uncertainty was dropped.");
    }
    private static void BoundedCoverage()
    {
        var map = new FoliageCoverageMap(); map.Reset(Vector3.zero);
        for (int i = 0; i < 20000; i++)
            map.AddUnknown(new Bounds(new Vector3(50 + i % 80, 8, 8), Vector3.one));
        Check(map.UnknownCells <= 28 * 28 * 28 && map.CoverageAt(Vector3.zero, 16) == 16,
            "Dense omissions became global coverage loss.");
        for (int i = 50; i < 130; i++) Check(map.CoverageAt(new Vector3(i, 8, 8), 16) == 0, "An omitted bound became clear.");
        map.AddUnknown(new Bounds(new Vector3(10000, 0, 0), Vector3.one));
        Check(map.CoverageAt(Vector3.zero, 16) == 16, "Out-of-region object poisoned local coverage.");
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

    private static void CanopyEscape()
    {
        Bounds[] canopy = { new Bounds(new Vector3(0, 8, 0), new Vector3(8, 4, 8)),
            new Bounds(new Vector3(0, 14, 0), new Vector3(10, 4, 10)),
            new Bounds(new Vector3(7, 18, 0), new Vector3(4, 4, 4)) };
        Vector3 camera = new Vector3(0, 8, 0);
        Check(FoliageGeometryMath.TryCanopyLift(camera, canopy, canopy.Length, true, 6, out float lift),
            "Measured camera-inside rejection did not offer an alternate elevation.");
        Check(Math.Abs(lift - 13) < .0001f, "Escape stopped in a higher or nearby crown.");
        Check(!FoliageGeometryMath.TryCanopyLift(camera + Vector3.up * lift, canopy, canopy.Length, true, 6, out _),
            "Clear elevated point produced another repeated lift.");
        Check(canopy[0].center.y == 8 && canopy[2].max.y == 20, "Proposal mutated scene bounds.");
    }

    private static void CanopyEvidence()
    {
        Bounds[] canopy = { new Bounds(new Vector3(0, 8, 0), new Vector3(8, 4, 8)) };
        Check(!FoliageGeometryMath.TryCanopyLift(new Vector3(0, 8, 0), canopy, 1, false, 6, out float lift) && lift == 0,
            "Incomplete coverage invented an escape.");
        Check(!FoliageGeometryMath.TryCanopyLift(Vector3.zero, canopy, 1, true, 6, out _),
            "A camera below leaves but not intersecting them was treated as inside.");
        Check(!FoliageGeometryMath.TryCanopyLift(new Vector3(float.NaN, 8, 0), canopy, 1, true, 6, out _), "Malformed camera was accepted.");
        Check(!FoliageGeometryMath.TryCanopyLift(new Vector3(0, 8, 0), canopy, 2, true, 6, out _), "Count exceeded available evidence.");
        Check(!FoliageGeometryMath.TryCanopyLift(new Vector3(0, 8, 0), canopy, 1, true, float.PositiveInfinity, out _), "Invalid foreground radius was accepted.");
    }

    private static void CanopyBounded()
    {
        Vector3 camera = new Vector3(0, 8, 0);
        Bounds[] canopy = { new Bounds(camera, new Vector3(8, 4, 8)),
            new Bounds(new Vector3(0, 40, 0), new Vector3(8, 4, 8)) };
        Check(!FoliageGeometryMath.TryCanopyLift(camera, canopy, 2, true, 6, out float lift) && lift == 0,
            "Over-budget canopy was clamped to an unsafe intermediate height.");
        canopy[1] = new Bounds(new Vector3(0, 37, 0), new Vector3(8, 4, 8));
        Check(FoliageGeometryMath.TryCanopyLift(camera, canopy, 2, true, 6, out lift) && lift == 32,
            "Exact bounded escape was rejected.");
        canopy[1] = new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one);
        Check(!FoliageGeometryMath.TryCanopyLift(camera, canopy, 2, true, 6, out _), "Malformed nearby evidence was ignored.");
    }

    private static void CanopyNeighborhood()
    {
        Vector3 camera = new Vector3(100, 8, -100);
        Bounds[] canopy = { new Bounds(camera, new Vector3(8, 4, 8)),
            new Bounds(camera + new Vector3(20, 50, 0), new Vector3(4, 4, 4)), default };
        Check(FoliageGeometryMath.TryCanopyLift(camera, canopy, 2, true, 6, out float lift) && lift == 3,
            "A distant crown or unused array entry inflated the local proposal.");
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
