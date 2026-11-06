using System;

namespace ValheimBroadcaster.Core;

/// <summary>Escape test for a camera sphere touching one known convex solid.
/// The surface closest to the centre defines a supporting plane. Travel with
/// a positive separating component increases clearance from that plane along
/// the entire straight segment. Apply to every starting contact independently,
/// and still sweep for new obstacles.</summary>
public static class JourneyEgressPolicy
{
    public static bool MovesOut(AutonomyPosition unitMove, AutonomyPosition separationDirection,
        double penetrationDepth, double sphereRadius)
    {
        // A centre on or inside the solid has no exterior supporting plane.
        if (!Finite(sphereRadius) || sphereRadius <= 0 || !Finite(penetrationDepth) ||
            penetrationDepth < 0 || penetrationDepth >= sphereRadius) return false;
        double moveLength = Length(unitMove);
        double normalLength = Length(separationDirection);
        if (moveLength <= .000001 || normalLength <= .000001) return false;
        double dot = (double)unitMove.X * separationDirection.X +
            (double)unitMove.Y * separationDirection.Y + (double)unitMove.Z * separationDirection.Z;
        return dot > .01 * moveLength * normalLength;
    }

    /// <summary>If the entire collider lies strictly behind a plane through the
    /// camera perpendicular to travel, distance from the camera to every point
    /// on that collider increases. This conservative test also applies to
    /// nonconvex geometry when its measured world AABB proves that condition.</summary>
    public static bool BoundsBehindMovement(AutonomyPosition centerRelativeToCamera, AutonomyPosition extents,
        AutonomyPosition unitMove)
    {
        if (extents.X < 0 || extents.Y < 0 || extents.Z < 0) return false;
        double length = Length(unitMove);
        if (length <= .000001) return false;
        double support = (double)unitMove.X * centerRelativeToCamera.X +
            (double)unitMove.Y * centerRelativeToCamera.Y + (double)unitMove.Z * centerRelativeToCamera.Z +
            (double)Math.Abs(unitMove.X) * extents.X + (double)Math.Abs(unitMove.Y) * extents.Y +
            (double)Math.Abs(unitMove.Z) * extents.Z;
        return support < -.001 * length;
    }

    private static double Length(AutonomyPosition p) => Math.Sqrt((double)p.X * p.X +
        (double)p.Y * p.Y + (double)p.Z * p.Z);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
