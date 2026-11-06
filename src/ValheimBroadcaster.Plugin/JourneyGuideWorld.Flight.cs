using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class JourneyGuideWorld : IJourneyFlightCorridor
{
    private readonly Collider[] corridorSolids = new Collider[96];
    private int corridorMask;

    public bool TryGetClimbSlope(AutonomyPosition from, AutonomyPosition heading, double range, out double slope)
    {
        slope = double.NegativeInfinity;
        if (!BindFrame() || !CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Live, out var work)) return false;
        using (work)
        {
            Vector3 start = new Vector3(from.X, from.Y, from.Z);
            Vector3 forward = new Vector3(heading.X, 0, heading.Z).normalized;
            if (range < 2 || forward.sqrMagnitude < .9f) return false;

            // Only observe the loaded prefix. A missing map truncates this hint;
            // it never invalidates body clearance nearby.
            float extent = 0, requested = Mathf.Min(48f, (float)range);
            for (float at = Mathf.Min(8f, requested); ; at = Mathf.Min(requested, at + 8f))
            {
                if (!ScenicSurface.TryGroundHeight(start + forward * at, out _)) break;
                extent = at;
                if (at >= requested) break;
            }
            if (extent < 2) return false;

            Vector3 across = Vector3.Cross(Vector3.up, forward);
            float width = radius + 1f;
            if (corridorMask == 0) corridorMask = mask & ~LayerMask.GetMask("terrain");
            int count = Physics.OverlapBoxNonAlloc(start + forward * (extent * .5f),
                new Vector3(width, 46f, extent * .5f), corridorSolids, Quaternion.LookRotation(forward),
                corridorMask, QueryTriggerInteraction.Ignore);
            bool measured = false;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = corridorSolids[i];
                if (!Solid(collider)) continue;
                Bounds box = collider.bounds;
                if (!TargetRoster.Finite(box.min) || !TargetRoster.Finite(box.max)) continue;
                Vector3 centre = box.center - start;
                Vector3 half = box.extents;
                float along = Vector3.Dot(centre, forward);
                float reach = Mathf.Abs(forward.x) * half.x + Mathf.Abs(forward.z) * half.z;
                float nearest = along - reach;
                float farthest = along + reach;
                float sideways = Mathf.Abs(Vector3.Dot(centre, across)) -
                    Mathf.Abs(across.x) * half.x - Mathf.Abs(across.z) * half.z;
                if (nearest > extent || farthest < 0 || sideways > width) continue;
                // A ceiling above the flight lane can be safely flown under.
                // Its top is not an altitude requirement for traversing the lane.
                if (box.min.y > start.y + radius) continue;
                float heightNeeded = box.max.y + radius + 2f - start.y;
                // Climb before reaching the leading edge; if already above it,
                // limit descent until the trailing edge passes underneath.
                float distance = heightNeeded >= 0 ? Mathf.Max(2, nearest - 2f) : Mathf.Max(2, farthest + 3f);
                double grade = heightNeeded / distance;
                slope = System.Math.Max(slope, grade);
                measured = true;
            }
            return measured;
        }
    }
}
