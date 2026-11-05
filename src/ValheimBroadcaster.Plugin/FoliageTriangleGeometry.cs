using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

internal enum FoliageTriangleQueryStatus { Pending, Clear, Hit }

/// <summary>Immutable readable mesh triangles. Bounds only prune queries: no
/// box is treated as occupied foliage. Geometry does not evaluate texture alpha,
/// shader deformation or animated/skinned vertices. Build and query work resume
/// explicitly; no Mesh, Physics or native engine calls occur in this class.</summary>
internal sealed class FoliageTriangleGeometry
{
    internal const int MaximumVertices = 16384, MaximumIndices = 36000;
    private const int LeafTriangles = 8;
    private readonly Vector3[] vertices;
    private readonly int[] indices, order;
    private readonly Node[] nodes;
    internal int TriangleCount => indices.Length / 3;

    private FoliageTriangleGeometry(Vector3[] v, int[] i, int[] o, Node[] n)
    { vertices = v; indices = i; order = o; nodes = n; }

    // Convenience for offline math tests. Runtime callers should retain and
    // incrementally advance BeginBuild under their shared observation budget.
    internal FoliageTriangleGeometry(Vector3[] vertices, int[] indices)
    {
        Builder builder = BeginBuild(vertices, indices);
        while (!builder.Step(1024)) { }
        FoliageTriangleGeometry ready = builder.Result!;
        this.vertices = ready.vertices; this.indices = ready.indices; order = ready.order; nodes = ready.nodes;
    }

    internal static Builder BeginBuild(Vector3[] localVertices, int[] triangleIndices) => new Builder(localVertices, triangleIndices);
    internal Query BeginRay(Matrix4x4 localToWorld, Vector3 origin, Vector3 direction, double maximumDistance) =>
        Query.Ray(this, localToWorld, origin, direction, maximumDistance);
    internal Query BeginSphere(Matrix4x4 localToWorld, Vector3 center, double radius) =>
        Query.Sphere(this, localToWorld, center, radius);
    internal Query BeginBox(Matrix4x4 localToWorld, Vector3 center, Quaternion orientation, Vector3 halfExtents) =>
        Query.Box(this, localToWorld, center, orientation, halfExtents);

    private readonly struct Node
    {
        internal readonly Aabb Bounds;
        internal readonly int Start, Count, Left, Right;
        internal Node(Aabb bounds, int start, int count, int left = -1, int right = -1)
        { Bounds = bounds; Start = start; Count = count; Left = left; Right = right; }
    }

    internal sealed class Builder
    {
        private readonly Vector3[] vertices;
        private readonly int[] indices, order;
        private readonly Aabb[] triangles;
        private readonly Vec[] centers;
        private readonly List<Node> nodes = new List<Node>();
        private readonly Stack<Job> jobs = new Stack<Job>();
        private int vertexCursor, triangleCursor;
        private Job? current;
        internal bool Complete => Result != null;
        internal FoliageTriangleGeometry? Result { get; private set; }
        internal long TotalWork { get; private set; }
        internal string? Failure { get; private set; }
        private sealed class Job
        {
            internal readonly int Node, Start, Count, Depth;
            internal int Cursor, Axis, Partition;
            internal bool Partitioning;
            internal double Split;
            internal Aabb Bounds, Centers;
            internal Job(int node, int start, int count, int depth)
            { Node = node; Start = start; Count = count; Depth = depth; }
        }
        internal Builder(Vector3[] localVertices, int[] triangleIndices)
        {
            if (localVertices == null || triangleIndices == null || localVertices.Length == 0 ||
                localVertices.Length > MaximumVertices || triangleIndices.Length == 0 ||
                triangleIndices.Length > MaximumIndices || triangleIndices.Length % 3 != 0)
                throw new ArgumentException("A bounded nonempty triangle mesh is required.");
            // Input copies and the final bounded node-array copy are indivisible.
            // Validation, triangle bounds, BVH scans and partitions are stepped.
            vertices = (Vector3[])localVertices.Clone(); indices = (int[])triangleIndices.Clone();
            int count = indices.Length / 3;
            order = new int[count]; triangles = new Aabb[count]; centers = new Vec[count];
        }
        internal bool Step(int maximumWork)
        {
            Budget(maximumWork);
            if (Failure != null) throw new InvalidOperationException("Invalid mesh builder cannot resume: " + Failure);
            try { return StepCore(maximumWork); }
            catch (ArgumentException error) { Failure = error.Message; throw; }
        }
        private bool StepCore(int maximumWork)
        {
            for (int work = 0; work < maximumWork && !Complete; work++, TotalWork++)
            {
                if (vertexCursor < vertices.Length)
                { Point(vertices[vertexCursor++]); continue; }
                if (triangleCursor < order.Length)
                {
                    int triangle = triangleCursor++, at = triangle * 3;
                    for (int j = 0; j < 3; j++)
                        if (indices[at + j] < 0 || indices[at + j] >= vertices.Length)
                            throw new ArgumentException("Triangle references a missing vertex.");
                    Vec a = V(vertices[indices[at]]), b = V(vertices[indices[at + 1]]), c = V(vertices[indices[at + 2]]);
                    triangles[triangle] = new Aabb(a, a).Include(b).Include(c);
                    centers[triangle] = (a + b + c) / 3; order[triangle] = triangle;
                    if (triangleCursor == order.Length)
                    { nodes.Add(default); jobs.Push(new Job(0, 0, order.Length, 0)); }
                    continue;
                }
                if (current == null)
                {
                    if (jobs.Count == 0)
                    { Result = new FoliageTriangleGeometry(vertices, indices, order, nodes.ToArray()); break; }
                    current = jobs.Pop();
                }
                Job job = current!;
                if (!job.Partitioning)
                {
                    int triangle = order[job.Start + job.Cursor];
                    job.Bounds = job.Cursor == 0 ? triangles[triangle] : job.Bounds.Include(triangles[triangle]);
                    job.Centers = job.Cursor == 0 ? new Aabb(centers[triangle], centers[triangle]) : job.Centers.Include(centers[triangle]);
                    if (++job.Cursor < job.Count) continue;
                    if (job.Count <= LeafTriangles)
                    { nodes[job.Node] = new Node(job.Bounds, job.Start, job.Count); current = null; continue; }
                    Vec extent = job.Centers.Max - job.Centers.Min;
                    job.Axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
                    if (job.Depth >= 24 || extent[job.Axis] <= 1e-12)
                    { Split(job, job.Count / 2); continue; }
                    job.Split = (job.Centers.Min[job.Axis] + job.Centers.Max[job.Axis]) * .5;
                    job.Cursor = job.Partition = 0; job.Partitioning = true;
                }
                else
                {
                    int at = job.Start + job.Cursor;
                    if (centers[order[at]][job.Axis] < job.Split)
                    {
                        int other = job.Start + job.Partition++, saved = order[at];
                        order[at] = order[other]; order[other] = saved;
                    }
                    if (++job.Cursor == job.Count)
                        Split(job, job.Partition == 0 || job.Partition == job.Count ? job.Count / 2 : job.Partition);
                }
            }
            return Complete;
        }
        private void Split(Job job, int leftCount)
        {
            int left = nodes.Count; nodes.Add(default); int right = nodes.Count; nodes.Add(default);
            nodes[job.Node] = new Node(job.Bounds, 0, 0, left, right);
            jobs.Push(new Job(right, job.Start + leftCount, job.Count - leftCount, job.Depth + 1));
            jobs.Push(new Job(left, job.Start, leftCount, job.Depth + 1)); current = null;
        }
    }

    internal sealed class Query
    {
        private enum Kind { Ray, Sphere, Box }
        private readonly FoliageTriangleGeometry geometry;
        private readonly Matrix4x4 transform;
        private readonly Kind kind;
        private readonly Vec center, direction, half, axisX, axisY, axisZ;
        private readonly double radius;
        private readonly Aabb shapeBounds;
        private readonly int[] stack = new int[64];
        private int stacked = 1, leafCursor, leafEnd, hitTriangle = -1;
        private double nearest;
        internal FoliageTriangleQueryStatus Status { get; private set; } = FoliageTriangleQueryStatus.Pending;
        internal bool Complete => Status != FoliageTriangleQueryStatus.Pending;
        internal long TotalWork { get; private set; }
        internal double Distance => Status == FoliageTriangleQueryStatus.Hit && kind == Kind.Ray ? nearest : double.PositiveInfinity;
        internal int TriangleIndex => Status == FoliageTriangleQueryStatus.Hit ? hitTriangle : -1;

        private Query(FoliageTriangleGeometry mesh, Matrix4x4 matrix, Kind type, Vec point, Vec ray,
            double range, Vec extents, Vec x, Vec y, Vec z)
        {
            Transform(matrix); geometry = mesh; transform = matrix; kind = type;
            center = point; direction = ray; radius = range; nearest = range;
            half = extents; axisX = x; axisY = y; axisZ = z;
            Vec extent = type == Kind.Sphere ? new Vec(range, range, range) :
                Abs(x) * extents.X + Abs(y) * extents.Y + Abs(z) * extents.Z;
            shapeBounds = new Aabb(point - extent, point + extent);
        }
        internal static Query Ray(FoliageTriangleGeometry mesh, Matrix4x4 matrix, Vector3 origin, Vector3 direction, double distance)
        {
            Point(origin); Point(direction); Positive(distance, nameof(distance));
            Vec ray = V(direction); if (ray.Squared < 1e-24) throw new ArgumentException("Ray direction cannot be zero.");
            return new Query(mesh, matrix, Kind.Ray, V(origin), ray / Math.Sqrt(ray.Squared), distance, default, default, default, default);
        }
        internal static Query Sphere(FoliageTriangleGeometry mesh, Matrix4x4 matrix, Vector3 center, double radius)
        {
            Point(center); Positive(radius, nameof(radius));
            return new Query(mesh, matrix, Kind.Sphere, V(center), default, radius, default, default, default, default);
        }
        internal static Query Box(FoliageTriangleGeometry mesh, Matrix4x4 matrix, Vector3 center, Quaternion rotation, Vector3 half)
        {
            Point(center); Point(half);
            if (half.x < 0 || half.y < 0 || half.z < 0 || !Finite(rotation.x) || !Finite(rotation.y) ||
                !Finite(rotation.z) || !Finite(rotation.w)) throw new ArgumentException("Invalid oriented box.");
            double norm = Math.Sqrt((double)rotation.x * rotation.x + (double)rotation.y * rotation.y +
                (double)rotation.z * rotation.z + (double)rotation.w * rotation.w);
            if (norm < 1e-12) throw new ArgumentException("Box rotation cannot be zero.");
            double x = rotation.x / norm, y = rotation.y / norm, z = rotation.z / norm, w = rotation.w / norm;
            var ax = new Vec(1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w));
            var ay = new Vec(2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w));
            var az = new Vec(2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y));
            return new Query(mesh, matrix, Kind.Box, V(center), default, 0, V(half), ax, ay, az);
        }
        internal FoliageTriangleQueryStatus Step(int maximumWork)
        {
            Budget(maximumWork);
            for (int work = 0; work < maximumWork && !Complete; work++, TotalWork++)
            {
                if (leafCursor < leafEnd)
                {
                    int triangle = geometry.order[leafCursor++], at = triangle * 3;
                    Vec a = World(V(geometry.vertices[geometry.indices[at]]), transform);
                    Vec b = World(V(geometry.vertices[geometry.indices[at + 1]]), transform);
                    Vec c = World(V(geometry.vertices[geometry.indices[at + 2]]), transform);
                    if (Cross(b - a, c - a).Squared <= 1e-24) continue; // No rendered triangle area.
                    if (kind == Kind.Ray)
                    {
                        if (RayTriangle(center, direction, a, b, c, nearest, out double hit))
                        { nearest = hit; hitTriangle = triangle; }
                    }
                    else if (kind == Kind.Sphere ? PointTriangleSquared(center, a, b, c) <= radius * radius + 1e-12 : BoxTriangle(a, b, c))
                    { hitTriangle = triangle; Status = FoliageTriangleQueryStatus.Hit; }
                }
                else
                {
                    if (stacked == 0)
                    { Status = hitTriangle >= 0 ? FoliageTriangleQueryStatus.Hit : FoliageTriangleQueryStatus.Clear; break; }
                    Node node = geometry.nodes[stack[--stacked]];
                    Aabb bounds = World(node.Bounds, transform);
                    bool possible = kind == Kind.Ray ? RayBounds(center, direction, bounds, nearest) :
                        kind == Kind.Sphere ? bounds.DistanceSquared(center) <= radius * radius + 1e-12 : bounds.Overlaps(shapeBounds);
                    if (!possible) continue;
                    if (node.Count > 0) { leafCursor = node.Start; leafEnd = node.Start + node.Count; }
                    else { stack[stacked++] = node.Right; stack[stacked++] = node.Left; }
                }
            }
            return Status;
        }
        private bool BoxTriangle(Vec a, Vec b, Vec c)
        {
            a = ToBox(a); b = ToBox(b); c = ToBox(c);
            if (Separated(new Vec(1, 0, 0), a, b, c) || Separated(new Vec(0, 1, 0), a, b, c) ||
                Separated(new Vec(0, 0, 1), a, b, c)) return false;
            Vec e0 = b - a, e1 = c - b, e2 = a - c;
            if (Separated(Cross(e0, e1), a, b, c)) return false;
            return EdgeAxes(e0, a, b, c) && EdgeAxes(e1, a, b, c) && EdgeAxes(e2, a, b, c);
        }
        private Vec ToBox(Vec p) { p -= center; return new Vec(Dot(p, axisX), Dot(p, axisY), Dot(p, axisZ)); }
        private bool EdgeAxes(Vec edge, Vec a, Vec b, Vec c) =>
            !Separated(new Vec(0, edge.Z, -edge.Y), a, b, c) &&
            !Separated(new Vec(-edge.Z, 0, edge.X), a, b, c) &&
            !Separated(new Vec(edge.Y, -edge.X, 0), a, b, c);
        private bool Separated(Vec axis, Vec a, Vec b, Vec c)
        {
            double squared = axis.Squared; if (squared <= 1e-24) return false;
            double pa = Dot(axis, a), pb = Dot(axis, b), pc = Dot(axis, c);
            double extent = half.X * Math.Abs(axis.X) + half.Y * Math.Abs(axis.Y) + half.Z * Math.Abs(axis.Z);
            double tolerance = 1e-8 * Math.Sqrt(squared);
            return Math.Min(pa, Math.Min(pb, pc)) > extent + tolerance || Math.Max(pa, Math.Max(pb, pc)) < -extent - tolerance;
        }
    }

    private readonly struct Aabb
    {
        internal readonly Vec Min, Max;
        internal Aabb(Vec min, Vec max) { Min = min; Max = max; }
        internal Aabb Include(Vec p) => new Aabb(Minimum(Min, p), Maximum(Max, p));
        internal Aabb Include(Aabb box) => new Aabb(Minimum(Min, box.Min), Maximum(Max, box.Max));
        internal bool Overlaps(Aabb b) => Min.X <= b.Max.X && Max.X >= b.Min.X && Min.Y <= b.Max.Y && Max.Y >= b.Min.Y && Min.Z <= b.Max.Z && Max.Z >= b.Min.Z;
        internal double DistanceSquared(Vec p)
        {
            double x = Math.Max(Min.X - p.X, Math.Max(0, p.X - Max.X)),
                y = Math.Max(Min.Y - p.Y, Math.Max(0, p.Y - Max.Y)), z = Math.Max(Min.Z - p.Z, Math.Max(0, p.Z - Max.Z));
            return x * x + y * y + z * z;
        }
    }
    private readonly struct Vec
    {
        internal readonly double X, Y, Z;
        internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double this[int axis] => axis == 0 ? X : axis == 1 ? Y : Z;
        internal double Squared => X * X + Y * Y + Z * Z;
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double b) => new Vec(a.X * b, a.Y * b, a.Z * b);
        public static Vec operator /(Vec a, double b) => a * (1 / b);
    }
    private static Vec V(Vector3 v) => new Vec(v.x, v.y, v.z);
    private static Vec Abs(Vec a) => new Vec(Math.Abs(a.X), Math.Abs(a.Y), Math.Abs(a.Z));
    private static Vec Minimum(Vec a, Vec b) => new Vec(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec Maximum(Vec a, Vec b) => new Vec(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    private static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vec Cross(Vec a, Vec b) => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static Vec World(Vec p, Matrix4x4 m) => new Vec(
        m.m00 * p.X + m.m01 * p.Y + m.m02 * p.Z + m.m03,
        m.m10 * p.X + m.m11 * p.Y + m.m12 * p.Z + m.m13,
        m.m20 * p.X + m.m21 * p.Y + m.m22 * p.Z + m.m23);
    private static Aabb World(Aabb bounds, Matrix4x4 m)
    {
        Vec center = World((bounds.Min + bounds.Max) * .5, m), e = (bounds.Max - bounds.Min) * .5;
        Vec extent = new Vec(Math.Abs(m.m00) * e.X + Math.Abs(m.m01) * e.Y + Math.Abs(m.m02) * e.Z,
            Math.Abs(m.m10) * e.X + Math.Abs(m.m11) * e.Y + Math.Abs(m.m12) * e.Z,
            Math.Abs(m.m20) * e.X + Math.Abs(m.m21) * e.Y + Math.Abs(m.m22) * e.Z);
        return new Aabb(center - extent, center + extent);
    }
    private static bool RayBounds(Vec origin, Vec direction, Aabb bounds, double range)
    {
        double enter = 0, exit = range;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(direction[i]) < 1e-15)
            { if (origin[i] < bounds.Min[i] - 1e-9 || origin[i] > bounds.Max[i] + 1e-9) return false; }
            else
            {
                double a = (bounds.Min[i] - origin[i]) / direction[i], b = (bounds.Max[i] - origin[i]) / direction[i];
                enter = Math.Max(enter, Math.Min(a, b)); exit = Math.Min(exit, Math.Max(a, b));
                if (enter > exit + 1e-9) return false;
            }
        }
        return true;
    }
    private static bool RayTriangle(Vec origin, Vec direction, Vec a, Vec b, Vec c, double range, out double distance)
    {
        distance = 0; Vec e1 = b - a, e2 = c - a, h = Cross(direction, e2);
        double determinant = Dot(e1, h), tolerance = Math.Max(1e-24, Math.Sqrt(e1.Squared * e2.Squared) * 1e-12);
        if (Math.Abs(determinant) <= tolerance) return false;
        Vec from = origin - a; double inverse = 1 / determinant, u = Dot(from, h) * inverse;
        if (u < -1e-9 || u > 1 + 1e-9) return false;
        Vec q = Cross(from, e1); double v = Dot(direction, q) * inverse;
        if (v < -1e-9 || u + v > 1 + 1e-9) return false;
        double t = Dot(e2, q) * inverse;
        if (t < -1e-9 || t > range + 1e-9) return false;
        distance = Math.Max(0, Math.Min(range, t)); return true;
    }
    private static double PointTriangleSquared(Vec p, Vec a, Vec b, Vec c)
    {
        Vec ab = b - a, ac = c - a, ap = p - a; double d1 = Dot(ab, ap), d2 = Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.Squared;
        Vec bp = p - b; double d3 = Dot(ab, bp), d4 = Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.Squared;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).Squared;
        Vec cp = p - c; double d5 = Dot(ab, cp), d6 = Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.Squared;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).Squared;
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return (p - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).Squared;
        double denominator = 1 / (va + vb + vc);
        return (p - (a + ab * (vb * denominator) + ac * (vc * denominator))).Squared;
    }
    private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    private static void Point(Vector3 p)
    {
        if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || Math.Abs(p.x) > 1000000 || Math.Abs(p.y) > 1000000 || Math.Abs(p.z) > 1000000)
            throw new ArgumentException("Mesh/query point is outside the finite coordinate domain.");
    }
    private static void Positive(double n, string name)
    { if (!Finite(n) || n <= 0 || n > 1000000) throw new ArgumentOutOfRangeException(name); }
    private static void Budget(int n)
    { if (n < 1 || n > 1000000) throw new ArgumentOutOfRangeException(nameof(n)); }
    private static void Transform(Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
            if (!Finite(m[r, c]) || Math.Abs(m[r, c]) > 1000000) throw new ArgumentException("Invalid mesh transform.");
        double determinant = (double)m.m00 * (m.m11 * (double)m.m22 - m.m12 * (double)m.m21) -
            m.m01 * (m.m10 * (double)m.m22 - m.m12 * (double)m.m20) +
            m.m02 * (m.m10 * (double)m.m21 - m.m11 * (double)m.m20);
        if (Math.Abs(m.m30) > 1e-8 || Math.Abs(m.m31) > 1e-8 || Math.Abs(m.m32) > 1e-8 || Math.Abs(m.m33 - 1) > 1e-8 || Math.Abs(determinant) < 1e-15)
            throw new ArgumentException("A nonsingular affine mesh transform is required.");
    }
}
