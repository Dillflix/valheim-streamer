using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal static class ScenicSubjectFrame
{
    private static readonly ShotProjection.Point[] corners = new ShotProjection.Point[8];
    private static readonly RaycastHit[] hits = new RaycastHit[24];

    internal static Bounds? BoundsFor(ScenicFeature feature)
    {
        if (feature.MeasuredBounds.HasValue) return ToBounds(feature.MeasuredBounds.Value);
        if (feature.Kind != ScenicFeatureKind.Structure && feature.Kind != ScenicFeatureKind.Rock) return null;
        return new Bounds(FromCore(feature.Center), new Vector3(feature.Radius * 2, Mathf.Max(1, feature.Height), feature.Radius * 2));
    }

    internal static bool Evaluate(ScenicFeature feature, ScenicShotRole role, ScenicPose pose,
        float aspect, float nearClip, out float quality, out PictureRegion region)
    {
        quality = 1; region = default;
        Bounds? measured = BoundsFor(feature);
        if (!measured.HasValue) return true;
        Bounds bounds = measured.Value;
        Quaternion inverse = Quaternion.Inverse(pose.Rotation);
        Vector3 min = bounds.min, max = bounds.max;
        int n = 0;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
        {
            Vector3 p = inverse * (new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z) - pose.Position);
            corners[n++] = new ShotProjection.Point(p.x, p.y, p.z);
        }
        ShotProjection.Envelope e = ShotProjection.MeasureEnvelope(corners, pose.FieldOfView, aspect, nearClip, .10, .025, .02);
        if (e.InFront) region = new PictureRegion(Mathf.Clamp01((float)e.MinX), Mathf.Clamp01((float)e.MinY),
            Mathf.Clamp01((float)e.MaxX), Mathf.Clamp01((float)e.MaxY));
        return ScenicSubjectFraming.Evaluate(e, role, out quality);
    }

    internal static bool MembersVisible(ScenicFeature feature, ScenicShotRole role, ScenicPose pose,
        ScenicVisibility visibility, float aspect, float nearClip)
    {
        int required = ScenicSubjectFraming.RequiredVisibleMembers(feature.MemberBounds.Count, role), visible = 0;
        if (required == 0) return true;
        foreach (PlaceBounds member in feature.MemberBounds)
        {
            Bounds bounds = ToBounds(member);
            // Upper facade/roof samples, not the ground-level midpoint of a district.
            Vector3 point = bounds.center + Vector3.up * bounds.extents.y * .45f;
            if (!visibility.SubjectPointVisible(pose.Position, pose.Rotation, point, pose.FieldOfView, aspect, nearClip)) continue;
            Vector3 delta = point - pose.Position;
            int count = Physics.RaycastNonAlloc(pose.Position, delta.normalized, hits, Mathf.Max(0, delta.magnitude - .15f),
                LayerMask.GetMask("Default", "static_solid", "piece", "terrain"), QueryTriggerInteraction.Ignore);
            bool clear = count < hits.Length;
            for (int i = 0; i < count && clear; i++)
            {
                Collider collider = hits[i].collider;
                if (!collider || !collider.enabled || collider.isTrigger) continue;
                if (collider.GetComponentInParent<Heightmap>() || !bounds.Contains(hits[i].point)) clear = false;
            }
            if (clear && ++visible >= required) return true;
        }
        return false;
    }
    private static Bounds ToBounds(PlaceBounds b) => new Bounds(FromCore(b.Center), new Vector3(b.Width, b.Height, b.Depth));
    private static Vector3 FromCore(AutonomyPosition p) => new Vector3(p.X, p.Y, p.Z);
}
