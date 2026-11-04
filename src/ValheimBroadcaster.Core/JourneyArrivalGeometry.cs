using System;

namespace ValheimBroadcaster.Core;

/// <summary>Proposes a terminal approach to a real destination without driving
/// through its subject. This is authored geometry, not a clearance certificate:
/// the adapter must fit terrain and validate the complete native camera path.</summary>
public static class JourneyArrivalGeometry
{
    public static bool TryBuild(AutonomyPosition start, AutonomyPosition incomingDirection,
        AutonomyPosition destinationCenter, double arrivalRadius, AutonomyPosition observedFocus,
        double maximumMetres, double lateralBias, out AutonomyPosition[] body, out string reason)
    {
        body = Array.Empty<AutonomyPosition>(); reason = "Invalid destination approach geometry";
        if (!Finite(arrivalRadius) || arrivalRadius < 8 || arrivalRadius > 64 ||
            !Finite(maximumMetres) || maximumMetres < 3 || maximumMetres > 80 ||
            !Finite(lateralBias) || Math.Abs(lateralBias) > 1) return false;
        Vec origin = V(start), incoming = V(incomingDirection), target = V(destinationCenter);
        Vec horizontal = new Vec(target.X - origin.X, 0, target.Z - origin.Z);
        double distance = horizontal.Length;
        if (incoming.Length < .001 || incoming.Horizontal.Length < .001 || distance < .001)
        { reason = "Arrival has no forward approach direction"; return false; }
        incoming = incoming.Normalized;
        Vec approach = horizontal.Normalized;
        if (Angle(incoming.Horizontal, approach) > 70 || Math.Abs(incoming.Y) > .65)
        { reason = "Arrival would reverse the incoming travel direction"; return false; }
        double standOff = Math.Max(8, Math.Min(12, arrivalRadius * .6));
        double advance = distance - standOff;
        if (advance < 4)
        { reason = "Camera is already too close for a forward stand-off approach"; return false; }
        if (advance > maximumMetres)
        { reason = "Destination stand-off lies beyond the validated local planning horizon"; return false; }
        Vec end = origin + approach * advance;
        Vec subject = V(observedFocus) - end;
        if (subject.Length < 4 || subject.Length > JourneyGazePolicy.MaximumObservedFocusDistance ||
            Angle(subject, approach) > JourneyGazePolicy.MaximumDepartureAngle)
        { reason = "Arrival viewpoint cannot resolve a separated forward-facing observed subject"; return false; }

        // The first pair retains the native predecessor's terminal tangent.
        // Only the later control develops the destination-facing approach.
        const double leadMetres = 2;
        Vec lead = origin + incoming * leadMetres;
        double handle = Math.Min(8, (end - lead).Length * .3);
        Vec control1 = lead + incoming * handle;
        Vec side = new Vec(-approach.Z, 0, approach.X);
        Vec control2 = end - approach * handle + side * (lateralBias * Math.Min(5, advance * .12));
        var result = new AutonomyPosition[15]; result[0] = start;
        if (!World(lead) || !World(control1) || !World(control2) || !World(end))
        { reason = "Arrival controls exceed supported world bounds"; return false; }
        result[1] = P(lead);
        Vec previous = lead; double length = leadMetres;
        for (int i = 2; i < result.Length; i++)
        {
            double t = (i - 1d) / (result.Length - 2), inverse = 1 - t;
            Vec point = lead * (inverse * inverse * inverse) + control1 * (3 * inverse * inverse * t) +
                control2 * (3 * inverse * t * t) + end * (t * t * t);
            Vec step = point - previous;
            double along = Dot(point - origin, approach);
            if (!World(point) || step.Length < .05 || step.Length > 16 || Dot(step, approach) <= 0 ||
                along > advance + .00001 || (point - target).Horizontal.Length > (previous - target).Horizontal.Length + .00001)
            { reason = "Arrival corridor would backtrack, overshoot or exceed its bounded knot spacing"; return false; }
            length += step.Length;
            if (length > maximumMetres + .00001 || length > 80)
            { reason = "Complete arrival corridor exceeds the validated local planning horizon"; return false; }
            result[i] = P(point); previous = point;
        }
        if (result[result.Length - 1].HorizontalDistance(destinationCenter) > arrivalRadius + .001)
        { reason = "Arrival viewpoint lies outside the selected destination region"; return false; }
        body = result; reason = "Forward arrival ends at a separated viewpoint inside the selected destination"; return true;
    }

    private static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Angle(Vec a, Vec b) => Math.Acos(Math.Max(-1, Math.Min(1,
        Dot(a, b) / Math.Max(.000001, a.Length * b.Length)))) * 180 / Math.PI;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool World(Vec v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z) &&
        Math.Abs(v.X) <= 20000 && Math.Abs(v.Y) <= 20000 && Math.Abs(v.Z) <= 20000;
    private static Vec V(AutonomyPosition point) => new Vec(point.X, point.Y, point.Z);
    private static AutonomyPosition P(Vec point) => new AutonomyPosition((float)point.X, (float)point.Y, (float)point.Z);
    private readonly struct Vec
    {
        internal readonly double X, Y, Z;
        internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        internal Vec Horizontal => new Vec(X, 0, Z);
        internal Vec Normalized => this * (1 / Math.Max(.000001, Length));
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double b) => new Vec(a.X * b, a.Y * b, a.Z * b);
    }
}
