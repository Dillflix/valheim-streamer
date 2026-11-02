using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Heightmap readiness proves that a tile exists, not that a camera is above its
// surface. Terrain is a one-sided mesh: overlap tests or rays from underneath
// cannot establish that invariant. All height measurements start above the
// actual loaded mesh and inspect it without generating or modifying the world.
internal static class ScenicSurface
{
    private static readonly AutonomyPosition[] footprint = new AutonomyPosition[ScenicSurfacePolicy.SampleCount];

    internal static bool TryGroundHeight(Vector3 point, out float height)
    {
        height = 0;
        if (!TargetRoster.Finite(point) || Character.InInterior(point)) return false;
        Heightmap map = Heightmap.FindHeightmap(point);
        if (!map || !map.isActiveAndEnabled) return false;
        MeshCollider collider = map.GetComponent<MeshCollider>();
        MeshFilter mesh = map.GetComponent<MeshFilter>();
        if (!collider || !collider.enabled || !collider.sharedMesh || collider.sharedMesh.vertexCount == 0 ||
            !mesh || !mesh.sharedMesh || mesh.sharedMesh.vertexCount == 0) return false;
        Bounds bounds = collider.bounds;
        if (!TargetRoster.Finite(bounds.min) || !TargetRoster.Finite(bounds.max)) return false;
        Vector3 above = new Vector3(point.x, bounds.max.y + 2f, point.z);
        if (!collider.Raycast(new Ray(above, Vector3.down), out RaycastHit hit, Mathf.Max(4f, bounds.size.y + 4f)) ||
            !TargetRoster.Finite(hit.point)) return false;
        height = hit.point.y;
        return true;
    }

    // A positive value is the lift needed for this actual body/near plane. The
    // planner may use it before admission; live playback must reject it instead
    // of silently moving the authored camera into a different composition.
    internal static bool TryRequiredLift(ScenicPose pose, float aspect, float nearClip,
        out float lift, out string reason)
    {
        lift = 0; reason = "Camera and near plane above loaded terrain/water";
        Vector3 forward = pose.Rotation * Vector3.forward, right = pose.Rotation * Vector3.right, up = pose.Rotation * Vector3.up;
        if (!TargetRoster.Finite(pose.Position) || !TargetRoster.Finite(forward) || !TargetRoster.Finite(right) ||
            !TargetRoster.Finite(up) || !ScenicSurfacePolicy.TryFootprint(ToCore(pose.Position), ToCore(forward),
                ToCore(right), ToCore(up), pose.FieldOfView, aspect, nearClip, footprint))
        { reason = "Invalid scenic surface query"; return false; }
        for (int i = 0; i < footprint.Length; i++)
            if (!Measure(FromCore(footprint[i]), i == 0, ref lift, ref reason)) return false;
        return true;
    }

    private static bool Measure(Vector3 point, bool body, ref float lift, ref string reason)
    {
        if (!TryGroundHeight(point, out float ground))
        { reason = "Loaded scenic terrain surface unavailable at camera footprint"; return false; }
        if (!TryWaterHeight(point, out bool waterPresent, out float waterHeight))
        { reason = "Loaded scenic water surface measurement unavailable"; return false; }
        ScenicSurfacePolicy.Result result = ScenicSurfacePolicy.EvaluateSample(point.y, true, ground, waterPresent, waterHeight, body);
        if (!result.Known) { reason = "Scenic camera surface measurement unavailable: " + result.Constraint; return false; }
        if (result.RequiredLift > lift)
        {
            lift = result.RequiredLift;
            reason = "Scenic camera footprint below " + (result.Constraint == ScenicSurfacePolicy.SurfaceConstraint.Water ? "water" : "terrain") +
                " clearance (needs " + lift.ToString("F1") + "m lift)";
        }
        return true;
    }

    // Use actual loaded water surfaces at this horizontal position, including
    // their wave height. No shared Floating cache is touched. Absence is
    // distinct from an invalid measurement so callers never invent a sea level.
    internal static bool TryWaterHeight(Vector3 point, out bool present, out float height)
    {
        present = false;
        height = float.NegativeInfinity;
        if (!TargetRoster.Finite(point)) return false;
        foreach (WaterVolume water in WaterVolume.Instances)
        {
            if (!water || !water.isActiveAndEnabled || !water.m_waterSurface || !water.m_waterSurface.enabled) continue;
            Bounds bounds = water.m_waterSurface.bounds;
            if (!TargetRoster.Finite(bounds.min) || !TargetRoster.Finite(bounds.max)) return false;
            if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) continue;
            present = true;
            float surface = water.GetWaterSurface(point, 1f);
            if (!Finite(surface) || !Finite(water.transform.position.y)) { height = float.NaN; return false; }
            // Retain a margin over the base surface when measuring a trough;
            // final playback still repeats this test as waves change.
            surface = Mathf.Max(surface, water.transform.position.y);
            height = Mathf.Max(height, surface);
        }
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static AutonomyPosition ToCore(Vector3 p) => new AutonomyPosition(p.x, p.y, p.z);
    private static Vector3 FromCore(AutonomyPosition p) => new Vector3(p.X, p.Y, p.Z);
}
