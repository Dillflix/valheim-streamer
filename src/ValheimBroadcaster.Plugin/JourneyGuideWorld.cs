using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Physical observations for flight through the currently loaded outdoor scene.
/// Trunks, rocks, buildings and terrain have colliders. A branch visible across
/// the picture does not close a flight lane whose camera body can pass it.</summary>
internal sealed partial class JourneyGuideWorld : IJourneyGuideWorld
{
    private const float SurfaceSpacing = 3f, MaxSweep = 64f, ContactSkin = .06f;
    private Collider[] overlaps = new Collider[32];
    private RaycastHit[] hits = new RaycastHit[48];
    private readonly Dictionary<Vector3, float> surfaces = new Dictionary<Vector3, float>(128);
    private readonly Dictionary<Vector3, float> guideHeights = new Dictionary<Vector3, float>(16);
    private ZNetScene? scene;
    private int frame = -1, mask;
    private float radius;
    private int leavingCount;
    private readonly Collider[] leaving = new Collider[128];

    private enum Rejection { None, SceneUnavailable, Budget, MissingSurface, OverlapCapacity,
        UnsupportedOverlap, InsideCollider, IntoContact, HitCapacity, SweepHit, SurfaceClearance }
    private readonly long[] rejected = new long[(int)Rejection.SurfaceClearance + 1];
    private long sweepCount, outwardCount;
    private Rejection lastReject;
    private Vector3 rejectedFrom, rejectedTo, rejectedAt;
    private Collider? rejectedSolid;
    private int rejectedFrame, scanFrame = -1, scanCount;
    private long rejectSequence, surfaceMisses;
    private Vector3 lastMissingHeight;
    private float rejectedRadius;
    private Bounds rejectedBounds;
    private readonly ScanSample[] scan = new ScanSample[24];
    private struct ScanSample
    {
        internal Vector3 Delta;
        internal float Clear;
        internal Rejection Reason;
    }
    internal string LastScan => DescribeScan(0, 12);
    internal string LastScanTail => DescribeScan(12, 24);
    private string DescribeScan(int first, int end)
    {
        if (scanCount <= first) return "none";
        var result = new StringBuilder().Append("frame ").Append(scanFrame);
        for (int i = first; i < Mathf.Min(end, scanCount); i++)
        {
            ScanSample item = scan[i];
            Vector3 d = item.Delta;
            float azimuth = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg;
            result.AppendFormat("; {0:F0}/{1:F0} {2:F1}/{3:F1} {4}", azimuth, pitch, item.Clear, d.magnitude, item.Reason);
        }
        return result.ToString();
    }
    internal void ResetDiagnostics()
    {
        Array.Clear(rejected, 0, rejected.Length); sweepCount = outwardCount = 0;
        lastReject = Rejection.None; rejectedSolid = null; scanFrame = -1; scanCount = 0; surfaceMisses = 0;
    }
    internal string LastRejection
    {
        get
        {
            if (lastReject == Rejection.None) return "none";
            string obstacle = rejectedSolid ? $"; {rejectedSolid!.name}/{rejectedSolid.GetType().Name}#{rejectedSolid.GetInstanceID()}, layer {rejectedSolid.gameObject.layer}, centre {rejectedBounds.center:F2}, extents {rejectedBounds.extents:F2}" : "";
            return $"{lastReject} at frame {rejectedFrame}; {rejectedFrom:F2} to {rejectedTo:F2}; sample {rejectedAt:F2}, radius {rejectedRadius:F2}{obstacle}";
        }
    }
    internal string SweepSummary
    {
        get
        {
            var result = new StringBuilder().Append("sweeps ").Append(sweepCount)
                .Append(", separating sweeps ").Append(outwardCount).Append(", missing height queries ").Append(surfaceMisses);
            if (surfaceMisses > 0) result.AppendFormat(" last at {0:F2}", lastMissingHeight);
            for (int i = 1; i < rejected.Length; i++)
                if (rejected[i] > 0) result.Append(", ").Append((Rejection)i).Append(' ').Append(rejected[i]);
            return result.ToString();
        }
    }
    private void Rejected(Rejection reason, Vector3 from, Vector3 to, Vector3 sample, Collider? solid = null)
    {
        rejectSequence++; rejected[(int)reason]++; lastReject = reason; rejectedFrame = Time.frameCount;
        rejectedFrom = from; rejectedTo = to; rejectedAt = sample; rejectedSolid = solid; rejectedRadius = radius;
        rejectedBounds = solid ? solid!.bounds : default;
    }

    public bool TryGetSurface(AutonomyPosition at, out double height)
    {
        height = 0;
        if (!BindFrame() || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var lease)) return false;
        using (lease)
        {
            var point = new Vector3(at.X, at.Y, at.Z);
            if (!Surface(point, out float surface))
            { surfaceMisses++; lastMissingHeight = point; return false; }
            if (!guideHeights.TryGetValue(point, out float preferred))
            {
                // Steer above a phase-independent wave envelope when available.
                // Only the actual surface blocks travel: a theoretical crest must
                // not lock a clear departure below the preferred cruise altitude.
                preferred = surface;
                if (ScenicSurface.TryWaterHeight(point, out bool water, out float envelope, stableEnvelope: true) && water)
                    preferred = Mathf.Max(preferred, envelope);
                guideHeights[point] = preferred;
            }
            height = preferred;
            return true;
        }
    }

    public double Clearance(AutonomyPosition from, AutonomyPosition to)
    {
        sweepCount++;
        Vector3 start = new Vector3(from.X, from.Y, from.Z);
        Vector3 end = new Vector3(to.X, to.Y, to.Z);
        long before = rejectSequence;
        double clear = MeasureClearance(start, end, from.Distance(to));
        Vector3 delta = end - start;
        // Record the latest fan while skipping tiny frame-sized advances. Only
        // diagnostics format these fixed-size samples into strings.
        if (delta.sqrMagnitude >= 1)
        {
            if (scanFrame != Time.frameCount) { scanFrame = Time.frameCount; scanCount = 0; }
            if (scanCount < scan.Length)
                scan[scanCount++] = new ScanSample { Delta = delta, Clear = (float)clear,
                    Reason = rejectSequence == before ? Rejection.None : lastReject };
        }
        return clear;
    }

    private double MeasureClearance(Vector3 start, Vector3 end, double requested)
    {
        if (!BindFrame())
        { Rejected(Rejection.SceneUnavailable, start, end, start); return 0; }
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var lease))
        { Rejected(Rejection.Budget, start, end, start); return 0; }
        using (lease)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= .00001f) return 0;
            if (!Surface(start, out float initialSurface))
            { Rejected(Rejection.MissingSurface, start, end, start); return 0; }
            Vector3 direction = delta / length;
            if (!CanLeaveStart(start, end, direction)) return 0;
            float previousGap = start.y - initialSurface;
            bool risingOutOfWave = previousGap < radius + ContactSkin;
            if (risingOutOfWave)
            {
                // A crest can reach a formerly clear stationary camera. Permit
                // a near-vertical rise away from it, still sweeping all solids.
                // Never apply this exception to missing terrain or burial.
                if (delta.y <= 0 || delta.x * delta.x + delta.z * delta.z > .01f * delta.y * delta.y ||
                    !ScenicSurface.TryGroundHeight(start, out float ground) ||
                    start.y < ground + radius + ContactSkin)
                { Rejected(Rejection.SurfaceClearance, start, end, start); return 0; }
            }
            float limit = Mathf.Min(length, MaxSweep);
            int count = Physics.SphereCastNonAlloc(start, radius, direction, hits, limit, mask, QueryTriggerInteraction.Ignore);
            // A full, unordered buffer can omit a nearer hit.
            if (count == hits.Length && hits.Length < 128)
            {
                hits = new RaycastHit[128];
                count = Physics.SphereCastNonAlloc(start, radius, direction, hits, limit, mask, QueryTriggerInteraction.Ignore);
            }
            if (count == hits.Length)
            { Rejected(Rejection.HitCapacity, start, end, start); return 0; }
            bool clearToEnd = length <= MaxSweep;
            Collider? nearest = null;
            for (int i = 0; i < count; i++)
            {
                Collider solid = hits[i].collider;
                if (!Solid(solid) || IsLeaving(solid)) continue;
                float hitLimit = Mathf.Max(0, hits[i].distance - ContactSkin);
                if (hitLimit <= limit)
                {
                    limit = hitLimit;
                    nearest = solid;
                }
                clearToEnd = false;
            }
            if (nearest) Rejected(Rejection.SweepHit, start, end, start + direction * limit, nearest);
            // PhysX sweeps protect the continuous segment against solid geometry.
            // Height measurements additionally detect missing heightmaps, the
            // undersides of one-sided terrain, and the moving water surface.
            int steps = Mathf.Max(1, Mathf.CeilToInt(limit / SurfaceSpacing));
            float verified = 0;
            for (int i = 1; i <= steps; i++)
            {
                float travelled = limit * i / steps;
                Vector3 point = start + direction * travelled;
                if (!Surface(point, out float surface))
                { Rejected(Rejection.MissingSurface, start, end, point); return verified; }
                float gap = point.y - surface;
                if (gap < radius + ContactSkin)
                {
                    if (!risingOutOfWave || gap <= previousGap ||
                        !ScenicSurface.TryGroundHeight(point, out float ground) ||
                        point.y < ground + radius + ContactSkin)
                    { Rejected(Rejection.SurfaceClearance, start, end, point); return verified; }
                }
                else risingOutOfWave = false;
                previousGap = gap;
                verified = travelled;
            }
            // Preserve the caller's exact double length for an unobstructed sweep.
            if (leavingCount > 0 && verified > 0) outwardCount++;
            return clearToEnd ? requested : verified;
        }
    }

    private bool BindFrame()
    {
        if (!ZNetScene.instance || !ZNet.instance) return false;
        if (scene == ZNetScene.instance && frame == Time.frameCount) return radius > 0 && mask != 0;
        scene = ZNetScene.instance;
        frame = Time.frameCount;
        surfaces.Clear();
        guideHeights.Clear();
        if (mask == 0) mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Camera camera = Camera.main;
        radius = 0;
        if (!camera || camera.nearClipPlane <= 0 || camera.aspect <= 0 ||
            camera.fieldOfView <= 0 || camera.fieldOfView >= 175) return false;
        // Enclose the optical centre and the near rectangle at every yaw/pitch.
        float near = camera.nearClipPlane;
        float h = near * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        radius = Mathf.Max(.25f, new Vector3(h * camera.aspect, h, near).magnitude);
        return radius > 0 && mask != 0;
    }

    private bool Surface(Vector3 point, out float surface)
    {
        if (surfaces.TryGetValue(point, out surface)) return !float.IsNaN(surface);
        bool known = ScenicSurface.TryGroundHeight(point, out surface);
        if (known)
        {
            known = ScenicSurface.TryWaterHeight(point, out bool water, out float height);
            if (known && water) surface = Mathf.Max(surface, height);
        }
        surfaces[point] = known ? surface : float.NaN;
        return known;
    }

    private bool CanLeaveStart(Vector3 start, Vector3 end, Vector3 direction)
    {
        leavingCount = 0;
        int count = Physics.OverlapSphereNonAlloc(start, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length && overlaps.Length < 128)
        {
            overlaps = new Collider[128];
            count = Physics.OverlapSphereNonAlloc(start, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
        }
        if (count == overlaps.Length)
        { Rejected(Rejection.OverlapCapacity, start, end, start); return false; }
        for (int i = 0; i < count; i++)
        {
            Collider solid = overlaps[i];
            if (!Solid(solid)) continue;
            if (!(solid is BoxCollider || solid is SphereCollider || solid is CapsuleCollider ||
                  solid is MeshCollider mesh && mesh.convex))
            {
                // For a concave mesh there is no single closest-point supporting
                // plane. Escape is provable if *every* point in its world bounds
                // is behind the movement plane at the camera centre.
                Bounds bounds = solid.bounds;
                Vector3 relative = bounds.center - start;
                Vector3 extents = bounds.extents;
                if (!WithinCoreBounds(relative) || !WithinCoreBounds(extents) ||
                    !JourneyEgressPolicy.BoundsBehindMovement(new AutonomyPosition(relative.x, relative.y, relative.z),
                        new AutonomyPosition(extents.x, extents.y, extents.z),
                        new AutonomyPosition(direction.x, direction.y, direction.z)))
                { Rejected(Rejection.UnsupportedOverlap, start, end, start, solid); return false; }
                leaving[leavingCount++] = solid;
                continue;
            }

            // A closest-point plane supports a convex solid. A straight move
            // strictly away from that plane can only increase distance from it.
            // Every initial contact must independently permit the same move.
            Vector3 separator = start - solid.ClosestPoint(start);
            float distance = separator.magnitude;
            if (!TargetRoster.Finite(separator) || distance < .0001f)
            { Rejected(Rejection.InsideCollider, start, end, start, solid); return false; }
            if (distance > radius + .0001f) continue;
            Vector3 normal = separator / distance;
            if (!JourneyEgressPolicy.MovesOut(new AutonomyPosition(direction.x, direction.y, direction.z),
                new AutonomyPosition(normal.x, normal.y, normal.z), Mathf.Max(0, radius - distance), radius))
            { Rejected(Rejection.IntoContact, start, end, start, solid); return false; }
            leaving[leavingCount++] = solid;
        }
        return true;
    }

    private static bool WithinCoreBounds(Vector3 vector) => TargetRoster.Finite(vector) &&
        Mathf.Abs(vector.x) <= 20000f && Mathf.Abs(vector.y) <= 20000f && Mathf.Abs(vector.z) <= 20000f;

    private bool IsLeaving(Collider solid)
    {
        for (int i = 0; i < leavingCount; i++)
            if (leaving[i] == solid) return true;
        return false;
    }

    private static bool Solid(Collider collider) =>
        collider && collider.enabled && !collider.isTrigger && !collider.GetComponentInParent<Character>();
}
