using System;

namespace ValheimBroadcaster.Core;

/// <summary>Read-only observations from the world loaded around the observer.</summary>
public interface IJourneyGuideWorld
{
    bool TryGetSurface(AutonomyPosition at, out double height);

    /// <summary>Distance in metres along this straight sweep that the camera body
    /// can occupy. Stop at the first obstacle or unknown space. Include the camera
    /// envelope, ground and water; scenery merely crossing the view is irrelevant.</summary>
    double Clearance(AutonomyPosition from, AutonomyPosition to);
}

/// <summary>Flies one continuous camera guide. Each local decision has a short
/// lifetime; losing a corridor preserves the mission, destination and current pose.</summary>
public sealed class JourneyGuide
{
    private const double Brake = 6, Margin = .2;
    private const double ScanInterval = .30;
    private static readonly int[] DetourAngles = { 28, -28, 58, -58, 88, -88, 120, -120 };
    private readonly AutonomyPosition destination;
    private readonly double cruise, arrivalRadius;
    private double scanIn, altitude, yaw, pitch, speedLimit, sideMemory;
    private int detourSide;
    private Candidate selected;
    private bool initialized, lifting;
    private AutonomyPosition progressTarget;
    private double bestDistance, withoutProgress, extraHeight, holdHeightUntil;

    public AutonomyPosition Position { get; private set; }
    public AutonomyPosition Forward { get; private set; }
    public double Speed { get; private set; }
    public double Travelled { get; private set; }
    public bool Arrived { get; private set; }
    public string Status { get; private set; } = "Starting continuous journey";

    public JourneyGuide(AutonomyPosition start, AutonomyPosition destination, double cruise, double arrivalRadius)
    {
        if (!Finite(cruise) || cruise <= 0 || cruise > 12) throw new ArgumentOutOfRangeException(nameof(cruise));
        if (!Finite(arrivalRadius) || arrivalRadius <= 0) throw new ArgumentOutOfRangeException(nameof(arrivalRadius));
        Position = start;
        this.destination = destination;
        this.cruise = cruise;
        this.arrivalRadius = arrivalRadius;
        yaw = Bearing(start, destination);
        Forward = Direction(yaw, 0);
        CheckArrival();
    }

    /// <summary>steeringTarget is the next catalogue waypoint, or the destination.
    /// A paused presentation should not supply elapsed wall time on resumption.</summary>
    public void Step(double deltaSeconds, AutonomyPosition steeringTarget, IJourneyGuideWorld world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (!Finite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (Arrived || deltaSeconds == 0) return;
        double dt = Math.Min(deltaSeconds, .15);
        withoutProgress += dt;
        scanIn -= dt;
        sideMemory = Math.Max(0, sideMemory - dt);
        if (scanIn <= 0)
        {
            scanIn = ScanInterval;
            if (!world.TryGetSurface(Position, out double floor) || !Finite(floor))
            {
                speedLimit = Speed = 0;
                Status = "Waiting for terrain under the guide";
                return;
            }

            double bearing = Position.HorizontalDistance(steeringTarget) < 1 ?
                Bearing(Position, destination) : Bearing(Position, steeringTarget);
            if (!initialized)
            {
                altitude = Clamp(Position.Y - floor, 5, 18);
                yaw = bearing;
                initialized = true;
            }
            UpdateEscapeHeight(world, steeringTarget, bearing);
            SelectCourse(world, floor, bearing);
        }
        if (speedLimit <= 0) return;

        // Heading is allowed to rotate even from rest. Only actual swept
        // displacement commits motion while steering towards a new course.
        double turnRate = Speed < 2 ? 115 : 60;
        yaw += Clamp(SignedAngle(selected.Yaw - yaw), -turnRate * dt, turnRate * dt);
        pitch = lifting ? 90 : pitch + Clamp(selected.Pitch - pitch, -55 * dt, 55 * dt);
        Forward = Direction(yaw, pitch);
        double distanceLeft = Position.HorizontalDistance(destination) - arrivalRadius;
        double arrivalSpeed = Math.Sqrt(2 * Brake * Math.Max(.05, distanceLeft));
        double targetSpeed = Math.Min(speedLimit, arrivalSpeed);
        Speed = Clamp(targetSpeed, Math.Max(0, Speed - Brake * dt), Speed + 4 * dt);
        var next = Along(Position, Forward, Speed * dt);
        double requested = Position.Distance(next);
        if (requested < .00001) return;
        double clear = SafeClearance(world, Position, next);
        if (clear < requested - .00001)
        {
            // A real obstacle moved, or the rounded turn cuts inside a corner.
            // Do not slide through it, consume render distance, or abandon the trip.
            Status = "Steering around an occupied camera path";
            Speed = 0;
            return;
        }
        Position = next;
        Travelled += requested;
        CheckArrival();
    }

    private void UpdateEscapeHeight(IJourneyGuideWorld world, AutonomyPosition target, double bearing)
    {
        double distance = Position.HorizontalDistance(target);
        if (bestDistance <= 0 || progressTarget.HorizontalDistance(target) > 8)
        {
            progressTarget = target;
            bestDistance = distance;
            withoutProgress = 0;
        }
        else if (distance < bestDistance - 2)
        {
            // Return gradually to the normal altitude once well past a barrier.
            if (Travelled >= holdHeightUntil)
                extraHeight = Math.Max(0, extraHeight - (bestDistance - distance) * .18);
            bestDistance = distance;
            withoutProgress = 0;
        }
        else if (withoutProgress >= 5 && extraHeight < 36)
        {
            // A local fan can keep selecting the same sides inside a concavity.
            // Try a different altitude; its ascent is still a swept movement.
            // Do not climb in response to an unloaded frontier.
            if (world.TryGetSurface(Along(Position, Direction(bearing, 0), 6), out double floor) && Finite(floor))
            {
                extraHeight = Math.Min(36, extraHeight + 12);
                holdHeightUntil = Travelled + 45;
            }
            withoutProgress = 0;
        }
    }

    private void SelectCourse(IJourneyGuideWorld world, double floor, double bearing)
    {
        // Space for acceleration and braking beyond the next decision. A short
        // loaded prefix is enough to fly at a correspondingly lower speed.
        double horizon = Clamp(4 + cruise * .8 + cruise * cruise / (2 * Brake), 8, 40);
        lifting = Position.Y < floor + 2;
        if (lifting)
        {
            // Waves can enter a previously safe pose. Test an actual vertical
            // escape before attempting lateral travel from inside the surface.
            var up = Direction(yaw, 90);
            double clear = SafeClearance(world, Position, Along(Position, up, Math.Max(4, floor + 3 - Position.Y)));
            selected = new Candidate(yaw, 90, clear, 0, clear);
            speedLimit = Math.Min(cruise, Math.Sqrt(2 * Brake * Math.Max(0, clear - Margin)));
            if (speedLimit <= 0) Speed = 0;
            Status = speedLimit > 0 ? "Rising above the local surface" : "Waiting for vertical clearance";
            return;
        }
        Candidate direct = Probe(world, bearing, 0, horizon, 0);
        Candidate best = direct;
        if (extraHeight > 0 && !direct.Frontier &&
            direct.Clear < horizon * .55 && Position.Y + 1 < floor + altitude + extraHeight)
        {
            // Inside a concavity, repeatedly rotating toward lateral rays can
            // keep a rounded turn pinned against exactly the same wall. Lift on
            // a verified vertical line before asking the horizontal fan again.
            double rise = Math.Min(12, floor + altitude + extraHeight - Position.Y);
            double clear = SafeClearance(world, Position, Along(Position, Direction(yaw, 90), rise));
            if (clear > 1)
            {
                selected = new Candidate(yaw, 90, clear, 0, clear);
                lifting = true;
                speedLimit = Math.Min(cruise, Math.Sqrt(2 * Brake * (clear - Margin)));
                Status = "Climbing out of a blocked approach";
                return;
            }
        }
        if (!direct.Frontier && direct.Clear < horizon - Margin)
        {
            // Look above a blocking object as well as along its sides. Prefer a
            // shallow climb to orbiting an obstacle; retain the detour side when
            // two paths of equivalent length pass opposite sides of a trunk.
            if (Position.Y - floor < altitude + 36)
                Consider(ref best, Probe(world, bearing, 0, horizon, 27));
            foreach (int angle in DetourAngles)
                Consider(ref best, Probe(world, bearing, angle, horizon, 0));
            if (best.Clear < Math.Min(horizon * .45, 6) && Position.Y - floor < altitude + 36)
            {
                Consider(ref best, Probe(world, bearing, 0, horizon, 55));
                // A vertical escape retains the current view bearing while rising.
                var up = Direction(yaw, 90);
                double check = Math.Min(horizon, altitude + 36 - (Position.Y - floor));
                var end = Along(Position, up, check);
                double clear = SafeClearance(world, Position, end);
                Consider(ref best, new Candidate(yaw, 90, clear, 0, clear * .52 - 1));
            }
        }
        selected = best;
        if (best.Clear <= Margin)
        {
            speedLimit = Speed = 0;
            Status = direct.Frontier ? "Waiting for terrain ahead" : "Waiting for a clear camera path";
            return;
        }
        if (best.Side != 0)
        {
            detourSide = best.Side;
            sideMemory = 2;
        }
        // Leave room for the next scan and a comfortable braking distance.
        // v * scanTime + v^2 / (2 * brake) <= clear - margin.
        double reaction = Brake * ScanInterval;
        speedLimit = Math.Min(cruise, Math.Sqrt(reaction * reaction + 2 * Brake * (best.Clear - Margin)) - reaction);
        Status = best.Pitch > 18 ? "Climbing through the route" :
            best.Side != 0 ? "Steering around terrain or obstacles" : "Travelling toward destination";
    }

    private Candidate Probe(IJourneyGuideWorld world, double bearing, int offset, double horizon, double minimumPitch)
    {
        double candidateYaw = bearing + offset;
        var horizontal = Direction(candidateYaw, 0);
        double span = horizon, floor;
        // The loading frontier may fall between here and the nominal horizon.
        // Continue through the observable prefix instead of requiring every
        // distant sample to be resident before departure.
        while (!world.TryGetSurface(Along(Position, horizontal, span), out floor) || !Finite(floor))
        {
            span *= .5;
            if (span < .25) return new Candidate(candidateYaw, 0, 0, side: 0, score: 0, frontier: true);
        }
        double desiredSlope = (floor + altitude + extraHeight - Position.Y) / span;
        double candidatePitch = Math.Max(minimumPitch == 0 ? -20 : minimumPitch,
            Clamp(Math.Atan(desiredSlope) * 180 / Math.PI, -20, 52));
        var end = Along(Position, Direction(candidateYaw, candidatePitch), span);
        double clear = SafeClearance(world, Position, end);
        int side = Math.Sign(offset);
        double alignment = Math.Cos(offset * Math.PI / 180) * Math.Cos(candidatePitch * Math.PI / 180);
        double score = clear * (.58 + .42 * alignment) - Math.Abs(offset) * .016 - minimumPitch * .035;
        if (sideMemory > 0 && side != 0 && side != detourSide) score -= 2.3 * sideMemory / 2;
        return new Candidate(candidateYaw, candidatePitch, clear, side, score, span < horizon && clear >= Position.Distance(end) - .01);
    }

    private readonly struct Candidate
    {
        public readonly double Yaw, Pitch, Clear, Score;
        public readonly int Side;
        public readonly bool Frontier;
        public Candidate(double yaw, double pitch, double clear, int side, double score, bool frontier = false)
        { Yaw = yaw; Pitch = pitch; Clear = clear; Side = side; Score = score; Frontier = frontier; }
    }

    private static void Consider(ref Candidate best, Candidate candidate)
    {
        if (candidate.Clear > Margin && (best.Clear <= Margin || candidate.Score > best.Score)) best = candidate;
    }

    private void CheckArrival()
    {
        if (Position.HorizontalDistance(destination) > arrivalRadius) return;
        Speed = 0;
        Arrived = true;
        Status = "At destination";
    }

    private static double SafeClearance(IJourneyGuideWorld world, AutonomyPosition from, AutonomyPosition to)
    {
        double clearance = world.Clearance(from, to);
        return Finite(clearance) ? Clamp(clearance, 0, from.Distance(to)) : 0;
    }

    private static AutonomyPosition Direction(double yaw, double pitch)
    {
        double radians = Math.PI / 180, lateral = Math.Cos(pitch * radians);
        return new AutonomyPosition((float)(Math.Sin(yaw * radians) * lateral), (float)Math.Sin(pitch * radians),
            (float)(Math.Cos(yaw * radians) * lateral));
    }

    private static AutonomyPosition Along(AutonomyPosition position, AutonomyPosition direction, double distance)
        => new AutonomyPosition((float)Clamp(position.X + direction.X * distance, -20000, 20000),
            (float)Clamp(position.Y + direction.Y * distance, -20000, 20000),
            (float)Clamp(position.Z + direction.Z * distance, -20000, 20000));

    private static double Bearing(AutonomyPosition from, AutonomyPosition to) =>
        Math.Atan2(to.X - from.X, to.Z - from.Z) * 180 / Math.PI;
    private static double SignedAngle(double angle) => ((angle + 540) % 360 + 360) % 360 - 180;
    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    private static double Clamp(double value, double low, double high) => Math.Min(high, Math.Max(low, value));
}
