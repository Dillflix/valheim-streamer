using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Directs the view independently of the travelling body. The body
/// remains a measured local proposal. Cruise aims describe viewing directions;
/// Journey angular-depth observations screen their actual pictures while native
/// surface/corridor validation screens motion. Named arrivals retain explicit
/// subject visibility. Neither authored aim geometry nor its rate is clearance.</summary>
public static class JourneyGazePolicy
{
    public const double MaximumDepartureAngle = 70;
    public const double DegreesPerSecond = 6;
    public const double NativeDegreesPerSecondLimit = 8;
    public const double MaximumObservedFocusDistance = 64;

    public static bool CanDepart(AutonomyPosition camera, AutonomyPosition aim,
        AutonomyPosition travelDirection, out string reason)
    {
        var view = V(aim) - V(camera); var travel = V(travelDirection);
        double viewLength = view.Length;
        if (viewLength < 1 || travel.HorizontalLength < .001 || view.HorizontalLength < .001 ||
            Math.Abs(view.Y) / viewLength > .65)
        { reason = "Journey departure needs a readable outward-facing view"; return false; }
        double angle = Angle(view.Horizontal, travel.Horizontal);
        if (angle > MaximumDepartureAngle)
        { reason = "Journey departure faces away from its route; await a forward-facing composition"; return false; }
        reason = "Departure view develops the selected route"; return true;
    }

    public static bool TryBuild(IReadOnlyList<AutonomyPosition> camera, AutonomyPosition firstAim,
        double speed, AutonomyPosition? measuredWaterDirection, AutonomyPosition? observedArrivalFocus,
        out AutonomyPosition[] aims, out string reason, double contextYawDegrees = 0, double contextElevation = 0)
    {
        aims = Array.Empty<AutonomyPosition>(); reason = "Invalid journey viewing path";
        if (camera == null || camera.Count < 3 || camera.Count > 16 || !Finite(speed) || speed < 2 || speed > 12 ||
            !Finite(contextYawDegrees) || Math.Abs(contextYawDegrees) > 24 ||
            !Finite(contextElevation) || Math.Abs(contextElevation) > 8) return false;
        var distance = new double[camera.Count];
        for (int i = 1; i < camera.Count; i++)
        {
            double step = camera[i].Distance(camera[i - 1]);
            if (step < .05 || step > 16) return false;
            distance[i] = distance[i - 1] + step;
        }
        Vec firstOffset = V(firstAim) - V(camera[0]);
        if (firstOffset.Length < 1 || firstOffset.Length > 80) return false;
        if (!CanDepart(camera[0], firstAim, P((V(camera[1]) - V(camera[0])).Normalized), out reason)) return false;
        if (observedArrivalFocus.HasValue && camera[camera.Count - 1].Distance(observedArrivalFocus.Value) > MaximumObservedFocusDistance)
        { reason = "Arrival gaze requires a locally observed subject, not distant destination metadata"; return false; }
        double lookAhead = Clamp(speed * 3.5, 14, 28);
        Vec direction = firstOffset.Normalized;
        Vec? arrivalDirection = null;
        if (observedArrivalFocus.HasValue)
        {
            int last = camera.Count - 1;
            Vec offset = V(observedArrivalFocus.Value) - V(camera[last]);
            Vec tangent = V(camera[last]) - V(camera[last - 1]);
            if (offset.Length < 4 || Angle(offset, tangent) > MaximumDepartureAngle)
            { reason = "Arrival needs a separated forward-facing subject at its validated viewpoint"; return false; }
            arrivalDirection = offset.Normalized;
            if (Angle(direction, arrivalDirection.Value) > distance[last] / speed * DegreesPerSecond)
            { reason = "Arrival cannot develop its observed subject within the bounded viewing rate"; return false; }
        }
        var result = new AutonomyPosition[camera.Count]; result[0] = firstAim;
        for (int i = 1; i < camera.Count; i++)
        {
            Vec position = V(camera[i]);
            // Look along the actual proposed corridor, including its bends and
            // height changes. Aim length does not slow/stop at body knot seams.
            Vec look = PointAt(camera, distance, distance[i] + lookAhead);
            look = new Vec(look.X, look.Y - 2, look.Z);
            if (measuredWaterDirection.HasValue)
            {
                Vec water = V(measuredWaterDirection.Value).Horizontal;
                if (water.Length > .001) look += water.Normalized * (lookAhead * .16);
            }
            if (!observedArrivalFocus.HasValue)
            {
                // The exact incoming aim is retained, then the existing angular
                // limit develops a locally screened alternative view of travel.
                // This is not another writer moving the live camera's target.
                Vec offset = look - position;
                double yaw = contextYawDegrees * Math.PI / 180;
                look = position + new Vec(offset.X * Math.Cos(yaw) + offset.Z * Math.Sin(yaw),
                    offset.Y + contextElevation, offset.Z * Math.Cos(yaw) - offset.X * Math.Sin(yaw));
            }
            Vec desired = (look - position).Normalized;
            double seconds = (distance[i] - distance[i - 1]) / speed;
            Vec nextDirection = Turn(direction, desired, DegreesPerSecond * seconds);
            if (arrivalDirection.HasValue)
            {
                // An observed arrival is a complete reveal, with its final
                // orientation constrained before playback. Greedy live bearing
                // tracking can lag behind forever as that bearing accelerates
                // near the subject. Allocate the turn across the whole route;
                // soften it only when the angular budget permits the peak.
                double total = distance[camera.Count - 1];
                double angle = Angle(firstOffset, arrivalDirection.Value);
                double progress = distance[i] / total;
                if (angle * 1.5 / (total / speed) <= DegreesPerSecond) progress = Smooth(progress);
                nextDirection = Turn(firstOffset.Normalized, arrivalDirection.Value, angle * progress);
            }
            direction = nextDirection;
            // The seed's complete aim is retained at the seam. Its focus range
            // then approaches the new travel framing over the first 20 metres.
            double rangeBlend = Smooth(Clamp(distance[i] / 20, 0, 1));
            double rangeToUse = firstOffset.Length + (lookAhead - firstOffset.Length) * rangeBlend;
            Vec aim = position + direction * rangeToUse;
            if (!World(aim)) { reason = "Journey viewing path exceeds supported world bounds"; return false; }
            result[i] = P(aim);
        }
        aims = result; reason = observedArrivalFocus.HasValue ? "Locally observed arrival develops through bounded gaze" :
            measuredWaterDirection.HasValue ? "Forward corridor gaze with measured coastal context" : "Forward corridor gaze follows the measured travelling path";
        return true;
    }

    private static Vec PointAt(IReadOnlyList<AutonomyPosition> points, double[] distances, double at)
    {
        for (int i = 1; i < points.Count; i++)
            if (at <= distances[i]) return Lerp(V(points[i - 1]), V(points[i]),
                (at - distances[i - 1]) / (distances[i] - distances[i - 1]));
        int last = points.Count - 1;
        return V(points[last]) + (V(points[last]) - V(points[last - 1])).Normalized * (at - distances[last]);
    }
    private static Vec Turn(Vec from, Vec to, double maximumDegrees)
    {
        double radians = Angle(from, to) * Math.PI / 180;
        if (radians < .000001) return to;
        double fraction = Math.Min(1, maximumDegrees * Math.PI / 180 / radians);
        double sine = Math.Sin(radians);
        if (Math.Abs(sine) < .000001) return from;
        return (from * (Math.Sin((1 - fraction) * radians) / sine) + to * (Math.Sin(fraction * radians) / sine)).Normalized;
    }
    private static double Angle(Vec a, Vec b) => Math.Acos(Clamp((a.X * b.X + a.Y * b.Y + a.Z * b.Z) /
        Math.Max(.000001, a.Length * b.Length), -1, 1)) * 180 / Math.PI;
    private static double Smooth(double value) => value * value * (3 - 2 * value);
    private static double Clamp(double value, double low, double high) => Math.Max(low, Math.Min(high, value));
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool World(Vec value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z) &&
        Math.Abs(value.X) <= 20000 && Math.Abs(value.Y) <= 20000 && Math.Abs(value.Z) <= 20000;
    private static Vec Lerp(Vec a, Vec b, double t) => a + (b - a) * t;
    private static Vec V(AutonomyPosition value) => new Vec(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vec value) => new AutonomyPosition((float)value.X, (float)value.Y, (float)value.Z);
    private readonly struct Vec
    {
        internal readonly double X, Y, Z;
        internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        internal double HorizontalLength => Math.Sqrt(X * X + Z * Z);
        internal Vec Horizontal => new Vec(X, 0, Z);
        internal Vec Normalized => this * (1 / Math.Max(.000001, Length));
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double b) => new Vec(a.X * b, a.Y * b, a.Z * b);
    }
}
