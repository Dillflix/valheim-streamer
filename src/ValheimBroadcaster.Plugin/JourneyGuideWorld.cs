using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Physical observations for flight through the currently loaded outdoor scene.
/// Trunks, rocks, buildings and terrain have colliders. A branch visible across
/// the picture does not close a flight lane whose camera body can pass it.</summary>
internal sealed class JourneyGuideWorld : IJourneyGuideWorld
{
    private const float SurfaceSpacing = 3f, MaxSweep = 64f, ContactSkin = .06f;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[48];
    private readonly Dictionary<Vector3, float> surfaces = new Dictionary<Vector3, float>(128);
    private readonly Dictionary<Vector3, float> guideHeights = new Dictionary<Vector3, float>(16);
    private ZNetScene? scene;
    private int frame = -1, mask;
    private float radius;

    public bool TryGetSurface(AutonomyPosition at, out double height)
    {
        height = 0;
        if (!BindFrame() || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var lease)) return false;
        using (lease)
        {
            var point = new Vector3(at.X, at.Y, at.Z);
            if (!Surface(point, out float surface)) return false;
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
        if (!BindFrame() || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var lease)) return 0;
        using (lease)
        {
            Vector3 start = new Vector3(from.X, from.Y, from.Z);
            Vector3 end = new Vector3(to.X, to.Y, to.Z);
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= .00001f || !Surface(start, out float initialSurface) || Occupied(start)) return 0;
            float previousGap = start.y - initialSurface;
            bool risingOutOfWave = previousGap < radius + ContactSkin;
            if (risingOutOfWave)
            {
                // A crest can reach a formerly clear stationary camera. Permit
                // a near-vertical rise away from it, still sweeping all solids.
                // Never apply this exception to missing terrain or burial.
                if (delta.y <= 0 || delta.x * delta.x + delta.z * delta.z > .01f * delta.y * delta.y ||
                    !ScenicSurface.TryGroundHeight(start, out float ground) ||
                    start.y < ground + radius + ContactSkin) return 0;
            }
            Vector3 direction = delta / length;
            float limit = Mathf.Min(length, MaxSweep);
            int count = Physics.SphereCastNonAlloc(start, radius, direction, hits, limit, mask, QueryTriggerInteraction.Ignore);
            // A full, unordered buffer can omit a nearer hit.
            if (count == hits.Length) return 0;
            bool clearToEnd = length <= MaxSweep;
            for (int i = 0; i < count; i++)
            {
                if (!Solid(hits[i].collider)) continue;
                limit = Mathf.Min(limit, Mathf.Max(0, hits[i].distance - ContactSkin));
                clearToEnd = false;
            }
            // PhysX sweeps protect the continuous segment against solid geometry.
            // Height measurements additionally detect missing heightmaps, the
            // undersides of one-sided terrain, and the moving water surface.
            int steps = Mathf.Max(1, Mathf.CeilToInt(limit / SurfaceSpacing));
            float verified = 0;
            for (int i = 1; i <= steps; i++)
            {
                float travelled = limit * i / steps;
                Vector3 point = start + direction * travelled;
                if (!Surface(point, out float surface)) return verified;
                float gap = point.y - surface;
                if (gap < radius + ContactSkin)
                {
                    if (!risingOutOfWave || gap <= previousGap ||
                        !ScenicSurface.TryGroundHeight(point, out float ground) ||
                        point.y < ground + radius + ContactSkin) return verified;
                }
                else risingOutOfWave = false;
                previousGap = gap;
                verified = travelled;
            }
            // Preserve the caller's exact double length for an unobstructed sweep.
            return clearToEnd ? from.Distance(to) : verified;
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

    private bool Occupied(Vector3 position)
    {
        // SphereCast alone does not report initial intersections.
        int count = Physics.OverlapSphereNonAlloc(position, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return true;
        for (int i = 0; i < count; i++)
            if (Solid(overlaps[i])) return true;
        return false;
    }

    private static bool Solid(Collider collider) =>
        collider && collider.enabled && !collider.isTrigger && !collider.GetComponentInParent<Character>();
}
