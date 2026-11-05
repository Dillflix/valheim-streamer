using System;

namespace ValheimBroadcaster.Core;

/// <summary>A connected travel edge carries a camera direction as well as a
/// body position. Geometry remains the scene adapter's responsibility; this
/// policy prevents a winding body route from leaving its view facing backwards
/// and bounds the angular development at the selected, rather than requested,
/// cruise. A lower speed grants more turning time, never more clearance.</summary>
public static class JourneyNavigationGazePolicy
{
    public const double MaximumTravelViewAngle = 70;
    public static double SlowerTier(double current) => current > 6 ? 6 : 2;

    public static bool TryAim(JourneyNavigationPose previous, AutonomyPosition next,
        AutonomyPosition goal, double speed, int alternative, out AutonomyPosition aim)
    {
        aim = default;
        if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 2 || speed > 12 || alternative < 0 || alternative > 2) return false;
        Vec body = V(next), before = V(previous.Position), prior = V(previous.Aim) - before;
        Vec step = body - before;
        if (step.Length < .05 || step.Horizontal.Length < .05 || prior.Length < 2 || prior.Length > 80) return false;
        Vec target = alternative == 0 ? step.Unit : alternative == 1 ? (V(goal) - body).Unit : prior.Unit;
        target = new Vec(target.X, Clamp(target.Y, -.25, .25), target.Z).Unit;
        if (target.Horizontal.Length < .5) target = prior.Unit;
        Vec direction = Turn(prior.Unit, target, JourneyGazePolicy.DegreesPerSecond * step.Length / speed);
        if (Angle(step.Horizontal, direction.Horizontal) > MaximumTravelViewAngle + .000001) return false;
        Vec focus = body + direction * Clamp(prior.Length, 4, 64);
        if (!World(focus)) return false;
        aim = new AutonomyPosition((float)focus.X, (float)focus.Y, (float)focus.Z); return true;
    }

    public static bool ForwardCompatible(AutonomyPosition from, AutonomyPosition body, AutonomyPosition aim)
    {
        Vec travel = (V(body) - V(from)).Horizontal, look = (V(aim) - V(body)).Horizontal;
        return travel.Length >= .05 && look.Length >= .05 && Angle(travel, look) <= MaximumTravelViewAngle + .000001;
    }
    private static Vec Turn(Vec from, Vec to, double maximum)
    {
        double radians = Angle(from, to) * Math.PI / 180;
        if (radians < .000001) return to;
        double portion = Math.Min(1, maximum * Math.PI / 180 / radians), sine = Math.Sin(radians);
        if (Math.Abs(sine) < .000001) return from;
        return (from * (Math.Sin((1 - portion) * radians) / sine) + to * (Math.Sin(portion * radians) / sine)).Unit;
    }
    private static double Angle(Vec a, Vec b) => Math.Acos(Clamp((a.X * b.X + a.Y * b.Y + a.Z * b.Z) /
        Math.Max(.000001, a.Length * b.Length), -1, 1)) * 180 / Math.PI;
    private static bool World(Vec v) => !double.IsNaN(v.X + v.Y + v.Z) && !double.IsInfinity(v.X + v.Y + v.Z) &&
        Math.Abs(v.X) <= 20000 && Math.Abs(v.Y) <= 20000 && Math.Abs(v.Z) <= 20000;
    private static double Clamp(double x, double low, double high) => Math.Max(low, Math.Min(high, x));
    private static Vec V(AutonomyPosition p) => new Vec(p.X, p.Y, p.Z);
    private readonly struct Vec
    {
        internal readonly double X, Y, Z;
        internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        internal Vec Unit => this * (1 / Math.Max(.000001, Length));
        internal Vec Horizontal => new Vec(X, 0, Z);
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double x) => new Vec(a.X * x, a.Y * x, a.Z * x);
    }
}
