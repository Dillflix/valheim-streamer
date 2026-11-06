using System;

namespace ValheimBroadcaster.Core;

/// <summary>Sphere clearance over a single solid heightfield face. For every
/// horizontal point under the sphere, the terrain must be below its lower
/// hemisphere. The largest corresponding centre height over the triangle is
/// attained on its plane interior or on one of its three finite edges.</summary>
public static class JourneyTerrainContactPolicy
{
    public static bool TryRequiredHeight(AutonomyPosition camera, double radius,
        AutonomyPosition a, AutonomyPosition b, AutonomyPosition c, out double required)
    {
        required = double.NegativeInfinity;
        if (!Finite(radius) || radius <= 0) return false;

        double bx = (double)b.X - a.X, bz = (double)b.Z - a.Z, by = (double)b.Y - a.Y;
        double cx = (double)c.X - a.X, cz = (double)c.Z - a.Z, cy = (double)c.Y - a.Y;
        double determinant = bx * cz - bz * cx;
        if (Math.Abs(determinant) < 1e-10) return false;

        double gradientX = (by * cz - bz * cy) / determinant;
        double gradientZ = (bx * cy - by * cx) / determinant;
        double normalLength = Math.Sqrt(1 + gradientX * gradientX + gradientZ * gradientZ);
        double relativeX = (double)camera.X - a.X, relativeZ = (double)camera.Z - a.Z;
        double tangentX = relativeX + radius * gradientX / normalLength;
        double tangentZ = relativeZ + radius * gradientZ / normalLength;
        double u = (tangentX * cz - tangentZ * cx) / determinant;
        double v = (bx * tangentZ - bz * tangentX) / determinant;
        if (u >= 0 && v >= 0 && u + v <= 1)
        {
            required = a.Y + relativeX * gradientX + relativeZ * gradientZ + radius * normalLength;
            return true;
        }

        required = Math.Max(EdgeHeight(camera, radius, a, b),
            Math.Max(EdgeHeight(camera, radius, b, c), EdgeHeight(camera, radius, c, a)));
        return true; // -Infinity means this face lies outside the sphere's horizontal footprint.
    }

    // Intersect the sphere footprint with an edge's horizontal line. In that
    // vertical slice the hemisphere is a circle; take the supporting point of
    // the inclined edge, clamped to the portion inside the footprint.
    private static double EdgeHeight(AutonomyPosition camera, double radius, AutonomyPosition a, AutonomyPosition b)
    {
        double dx = (double)b.X - a.X, dz = (double)b.Z - a.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        double x = (double)camera.X - a.X, z = (double)camera.Z - a.Z;
        double along = (x * dx + z * dz) / length;
        double perpendicular = (x * dz - z * dx) / length;
        double spanSquared = radius * radius - perpendicular * perpendicular;
        if (spanSquared < 0) return double.NegativeInfinity;
        double span = Math.Sqrt(spanSquared);
        double low = Math.Max(0, along - span), high = Math.Min(length, along + span);
        if (low > high) return double.NegativeInfinity;
        double slope = ((double)b.Y - a.Y) / length;
        double tangent = along + span * slope / Math.Sqrt(1 + slope * slope);
        double at = Math.Max(low, Math.Min(high, tangent));
        double offset = at - along;
        return a.Y + slope * at + Math.Sqrt(Math.Max(0, spanSquared - offset * offset));
    }

    /// <summary>For a fixed horizontal footprint the required height is
    /// constant, so a vertical rise increases clearance at every point below
    /// the sphere. Requires verified coverage of the entire footprint from
    /// the caller. Deep burial additionally requires evidence that this same
    /// camera pose was already clear before the terrain changed.</summary>
    public static bool CanRise(AutonomyPosition from, AutonomyPosition to, double radius,
        double groundAtCentre, double requiredHeight, bool previouslyClear = false)
    {
        return from.X == to.X && from.Z == to.Z && to.Y > from.Y &&
            Finite(radius) && radius > 0 && Finite(groundAtCentre) && Finite(requiredHeight) &&
            requiredHeight + .002 >= groundAtCentre + radius &&
            (previouslyClear || from.Y >= groundAtCentre - radius);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
