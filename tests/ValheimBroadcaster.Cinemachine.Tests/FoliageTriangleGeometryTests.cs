using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Plugin;

internal static class FoliageTriangleGeometryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Foliage triangle bounds do not occupy empty triangular corners", EmptyBoundsCorner);
        yield return ("Separated foliage cards retain an air gap inside their aggregate bounds", CardAirGap);
        yield return ("Exact foliage rays return the nearest original triangle after complete traversal", NearestRay);
        yield return ("Foliage triangle ray tests are two-sided and preserve world distance", TwoSidedRay);
        yield return ("Foliage triangle spheres test face edge and vertex distance", SphereFeatures);
        yield return ("Foliage near-plane boxes use triangle SAT rather than vertex or bounds occupancy", OrientedBoxes);
        yield return ("Foliage triangle queries preserve signed nonuniform affine transforms", SignedScale);
        yield return ("Foliage geometry builders and queries yield within their work budgets", BudgetedDenseMesh);
        yield return ("Foliage geometry retains immutable vertices and triangle indices", ImmutableInputs);
        yield return ("Degenerate foliage triangles do not invent rendered occupied area", Degenerate);
        yield return ("Malformed foliage geometry cannot resume into a clear certificate", InvalidGeometry);
        yield return ("Exact foliage triangle rays match analytic planar membership across empty bounds", PlanarSweep);
    }
    private static Vector3 P(float x, float y, float z = 0) => new Vector3(x, y, z);
    private static Vector3[] Triangle() => new[] { P(0, 0), P(4, 0), P(0, 4) };
    private static FoliageTriangleGeometry Geometry() => new FoliageTriangleGeometry(Triangle(), new[] { 0, 1, 2 });
    private static void Check(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .00001, $"Expected {expected}, got {actual}");
    private static bool Hit(FoliageTriangleGeometry.Query query, int budget = 3)
    {
        int steps = 0;
        while (!query.Complete)
        {
            long before = query.TotalWork;
            query.Step(budget);
            Check(query.TotalWork - before <= budget, "Query exceeded work budget");
            Check(++steps < 100000, "Query did not finish");
        }
        return query.Status == FoliageTriangleQueryStatus.Hit;
    }
    private static void EmptyBoundsCorner()
    {
        var mesh = Geometry(); var matrix = Matrix4x4.identity;
        Check(!Hit(mesh.BeginRay(matrix, P(3, 3, -2), P(0, 0, 1), 5)), "Empty triangle corner blocked the view");
        Check(!Hit(mesh.BeginSphere(matrix, P(3, 3, .1f), .25)), "Camera in empty triangle AABB was blocked");
        Check(!Hit(mesh.BeginBox(matrix, P(3, 3), Quaternion.identity, P(.2f, .2f, .04f))), "Empty near plane was blocked by broad bounds");
        Check(Hit(mesh.BeginRay(matrix, P(1, 1, -2), P(0, 0, 1), 5)), "Actual foliage face was ignored");
        Check(Hit(mesh.BeginSphere(matrix, P(1, 1, .2f), .25)), "Camera touching actual triangle was ignored");
    }
    private static void CardAirGap()
    {
        var mesh = new FoliageTriangleGeometry(new[] { P(-3, -2), P(-1, -2), P(-2, 2), P(1, -2), P(3, -2), P(2, 2) },
            new[] { 0, 1, 2, 3, 4, 5 });
        Check(!Hit(mesh.BeginSphere(Matrix4x4.identity, P(0, 0), .25)), "Air between cards became camera collision");
        Check(!Hit(mesh.BeginBox(Matrix4x4.identity, P(0, 0), Quaternion.identity, P(.3f, .4f, .04f))), "Near plane in card gap was blocked");
        Check(!Hit(mesh.BeginRay(Matrix4x4.identity, P(0, 0, -1), P(0, 0, 1), 3)), "View through card gap was blocked");
        Check(Hit(mesh.BeginRay(Matrix4x4.identity, P(2, 0, -1), P(0, 0, 1), 3)), "Card itself became transparent geometry");
    }
    private static void NearestRay()
    {
        var points = new List<Vector3>(); var indices = new List<int>();
        for (int i = 0; i < 40; i++)
        {
            float z = i == 17 ? 1 : 5 + i;
            int at = points.Count; points.Add(P(-2, -2, z)); points.Add(P(2, -2, z)); points.Add(P(0, 2, z));
            indices.Add(at); indices.Add(at + 1); indices.Add(at + 2);
        }
        var mesh = new FoliageTriangleGeometry(points.ToArray(), indices.ToArray());
        var ray = mesh.BeginRay(Matrix4x4.identity, P(0, 0), P(0, 0, 1), 100);
        Check(ray.Step(1) == FoliageTriangleQueryStatus.Pending && ray.TriangleIndex == -1,
            "Broad-phase traversal advertised a completed hit");
        Check(Hit(ray, 1), "Dense nearest ray missed"); Near(ray.Distance, 1);
        Check(ray.TriangleIndex == 17, "BVH sorting changed original triangle identity");
    }
    private static void TwoSidedRay()
    {
        var mesh = Geometry();
        var front = mesh.BeginRay(Matrix4x4.identity, P(1, 1, -3), P(0, 0, 5), 4);
        var back = mesh.BeginRay(Matrix4x4.identity, P(1, 1, 3), P(0, 0, -2), 4);
        Check(Hit(front) && Hit(back), "Leaf winding hid one side"); Near(front.Distance, 3); Near(back.Distance, 3);
        Check(!Hit(mesh.BeginRay(Matrix4x4.identity, P(1, 1, -3), P(0, 0, 1), 2.9)), "Ray exceeded its bounded range");
    }
    private static void SphereFeatures()
    {
        var mesh = Geometry(); var m = Matrix4x4.identity;
        Check(Hit(mesh.BeginSphere(m, P(1, 1, .25f), .25)), "Face tangency lost");
        Check(!Hit(mesh.BeginSphere(m, P(1, 1, .251f), .25)), "Sphere beyond face became collision");
        Check(Hit(mesh.BeginSphere(m, P(2, -.24f), .25)), "Edge overlap lost");
        Check(!Hit(mesh.BeginSphere(m, P(2, -.26f), .25)), "Empty space beyond edge became collision");
        Check(Hit(mesh.BeginSphere(m, P(-.15f, -.15f), .25)), "Vertex overlap lost");
        Check(!Hit(mesh.BeginSphere(m, P(-.2f, -.2f), .25)), "AABB corner distance replaced triangle distance");
    }
    private static void OrientedBoxes()
    {
        var mesh = Geometry();
        var q = new Quaternion(0, 0, (float)Math.Sin(Math.PI / 8), (float)Math.Cos(Math.PI / 8));
        Check(!Hit(mesh.BeginBox(Matrix4x4.identity, P(3, 3), q, P(.8f, .05f, .04f))), "Oriented box empty corner rejected by AABB");
        Check(Hit(mesh.BeginBox(Matrix4x4.identity, P(2.1f, 2.1f), q, P(.4f, .04f, .04f))), "Rotated thin near-plane edge overlap missed");
        var spanning = new FoliageTriangleGeometry(new[] { P(-10, -10), P(10, -10), P(0, 10) }, new[] { 0, 1, 2 });
        Check(Hit(spanning.BeginBox(Matrix4x4.identity, P(0, 0), Quaternion.identity, P(.1f, .1f, .04f))),
            "Triangle crossed box without an enclosed triangle vertex and was missed");
        Check(!Hit(spanning.BeginBox(Matrix4x4.identity, P(0, 0, .1f), Quaternion.identity, P(.1f, .1f, .04f))), "Separate triangle plane collided");
    }
    private static void SignedScale()
    {
        var m = Matrix4x4.identity;
        m.m00 = -2; m.m11 = 3; m.m22 = .5f; m.m03 = 10; m.m13 = -4; m.m23 = 2;
        var mesh = Geometry();
        var ray = mesh.BeginRay(m, P(8, -1), P(0, 0, 1), 5);
        Check(Hit(ray), "Mirrored/nonuniform transformed triangle missed"); Near(ray.Distance, 2);
        Check(Hit(mesh.BeginSphere(m, P(8, -1, 2.2f), .25)), "World sphere was incorrectly transformed as local sphere");
        Check(!Hit(mesh.BeginSphere(m, P(3, 5, 2), .25)), "Signed-scale AABB corner became occupied");
        Check(Hit(mesh.BeginBox(m, P(8, -1, 2), Quaternion.identity, P(.1f, .1f, .04f))), "Transformed triangle/box overlap missed");
        m.m01 = .75f; // Affine shear is valid too; no lossyScale approximation.
        Check(Hit(mesh.BeginRay(m, P(8.75f, -1), P(0, 0, 1), 5)), "Affine shear lost exact leaf geometry");
    }
    private static void BudgetedDenseMesh()
    {
        const int side = 36; var vertices = new Vector3[side * side]; var indices = new List<int>();
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) vertices[y * side + x] = P(x, y);
        for (int y = 0; y < side - 1; y++) for (int x = 0; x < side - 1; x++)
        { int a = y * side + x; indices.AddRange(new[] { a, a + 1, a + side, a + 1, a + side + 1, a + side }); }
        var builder = FoliageTriangleGeometry.BeginBuild(vertices, indices.ToArray());
        Check(!builder.Step(1) && builder.Result == null, "One step built an entire dense tree");
        int iterations = 0;
        while (!builder.Complete)
        {
            long before = builder.TotalWork; builder.Step(7);
            Check(builder.TotalWork - before <= 7 && ++iterations < 100000, "Builder ignored bounded incremental work");
        }
        var query = builder.Result!.BeginRay(Matrix4x4.identity, P(17.25f, 17.25f, -2), P(0, 0, 1), 5);
        Check(query.Step(1) == FoliageTriangleQueryStatus.Pending, "Unfinished tree query was reported clear");
        Check(Hit(query, 1), "Resumed dense ray failed"); Near(query.Distance, 2);
    }
    private static void ImmutableInputs()
    {
        var v = Triangle(); var indices = new[] { 0, 1, 2 };
        var builder = FoliageTriangleGeometry.BeginBuild(v, indices);
        v[1] = P(0, 0); indices[1] = 0;
        while (!builder.Step(2)) { }
        Check(Hit(builder.Result!.BeginRay(Matrix4x4.identity, P(1, 1, -1), P(0, 0, 1), 2)), "Caller mutation changed retained triangles");
    }
    private static void Degenerate()
    {
        var mesh = new FoliageTriangleGeometry(new[] { P(0, 0), P(1, 0), P(2, 0) }, new[] { 0, 1, 2 });
        Check(!Hit(mesh.BeginSphere(Matrix4x4.identity, P(1, 0), .25)), "Zero-area triangle became occupied foliage");
        Check(!Hit(mesh.BeginBox(Matrix4x4.identity, P(1, 0), Quaternion.identity, P(.2f, .2f, .04f))), "Degenerate triangle blocked near plane");
    }
    private static void InvalidGeometry()
    {
        var builder = FoliageTriangleGeometry.BeginBuild(Triangle(), new[] { 0, 1, 40 });
        bool rejected = false;
        try { builder.Step(100); } catch (ArgumentException) { rejected = true; }
        Check(rejected && builder.Result == null && builder.Failure != null, "Malformed mesh yielded a result");
        rejected = false; try { builder.Step(100); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Malformed builder resumed past the invalid triangle");
        foreach (Action bad in new Action[] {
            () => new FoliageTriangleGeometry(new[] { P(float.NaN, 0), P(1, 0), P(0, 1) }, new[] { 0, 1, 2 }),
            () => Geometry().BeginRay(Matrix4x4.identity, P(0, 0), P(0, 0), 2),
            () => Geometry().BeginRay(Matrix4x4.identity, P(0, 0), P(0, 0, 1), double.NaN),
            () => Geometry().BeginSphere(default, P(0, 0), .25),
            () => Geometry().BeginBox(Matrix4x4.identity, P(0, 0), default, P(.1f, .1f, .04f)) })
        { rejected = false; try { bad(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Invalid query/mesh admitted"); }
    }
    private static void PlanarSweep()
    {
        var mesh = Geometry();
        for (int x = -2; x <= 18; x++) for (int y = -2; y <= 18; y++)
        {
            float px = x * .25f, py = y * .25f;
            bool inside = px >= 0 && py >= 0 && px + py <= 4;
            Check(Hit(mesh.BeginRay(Matrix4x4.identity, P(px, py, -1), P(0, 0, 1), 2)) == inside,
                "Exact triangle differs from analytic plane membership at " + px + "," + py);
        }
    }
}
