using System;

namespace ValheimBroadcaster.Core;

/// <summary>Optional coarse observation of solid obstacles in the intended flight corridor.
/// Returns a suggested climb gradient. Actual motion always needs a separate swept check.</summary>
public interface IJourneyFlightCorridor
{
    bool TryGetClimbSlope(AutonomyPosition from, AutonomyPosition heading, double range, out double slope);
}

/// <summary>True when the current camera envelope needs to rise out of confirmed
/// loaded terrain. This observation never authorizes a physical move.</summary>
public interface IJourneyTerrainContact
{
    bool NeedsVerticalEscape(AutonomyPosition position);
}

public sealed partial class JourneyGuide
{
    private double contactScanIn;
    private bool inTerrainContact;
    private double requiredTerrainLift;
    public double LocalSurfaceHeight { get; private set; }
    public double HighestSurfaceAhead { get; private set; }
    private double corridorSlope = double.NegativeInfinity;

    private double solidGrade, forecastRange;
    private AutonomyPosition? passageBend, passageExit;

    private void ObserveFlightCorridor(IJourneyGuideWorld world, double bearing, double currentFloor)
    {
        corridorSlope = double.NegativeInfinity;
        solidGrade = double.NegativeInfinity;
        forecastRange = 0;
        CorridorChoice = "Terrain course";
        requiredTerrainLift = 0;
        LocalSurfaceHeight = HighestSurfaceAhead = currentFloor;
        double range = Math.Min(Position.HorizontalDistance(destination), Clamp(cruise * 4, 24, 64));
        if (range < 2) return;
        AutonomyPosition heading = Direction(bearing, 0);
        // The terrain can crest between the camera and a low endpoint. Sample
        // the loaded approach, reserving space for pitch response and the next
        // steering update as we approach a rise. A missing height truncates
        // only this forecast; the actual swept clearance still governs travel.
        double pitchLead = Math.Max(1, Speed * .55);
        double previousFloor = currentFloor;
        for (double span = Math.Min(4, range); ; span = Math.Min(span + 4, range))
        {
            if (!world.TryGetSurface(Along(Position, heading, span), out double floor) || !Finite(floor))
                break;
            // A sample can fall on either side of the actual crest. Reserve
            // half the observed rise across this interval while choosing pitch;
            // body clearance is still established by a fresh swept test.
            forecastRange = span;
            HighestSurfaceAhead = Math.Max(HighestSurfaceAhead, floor);
            double crestAllowance = Math.Abs(floor - previousFloor) * .5;
            double rise = floor + crestAllowance + altitude + extraHeight - Position.Y;
            previousFloor = floor;
            // Carry clearance across a crest rather than descending onto it.
            double run = rise > 0 ? Math.Max(1, span - pitchLead) : span + pitchLead;
            corridorSlope = Math.Max(corridorSlope, Clamp(rise / run, -.36, 1.28));
            requiredTerrainLift = Math.Max(requiredTerrainLift, rise - run * 1.28);
            if (span >= range) break;
        }
        if (world is IJourneyFlightCorridor sensor &&
            sensor.TryGetClimbSlope(Position, heading, range, out double solidSlope) && Finite(solidSlope))
            solidGrade = Clamp(solidSlope, -.36, .95);
    }

    // A collider top is an overflight option. A terrain-height measurement
    // still governs ridge anticipation; a top alone tells us nothing about
    // the free space beside a trunk or underneath an overhanging collider.
    private bool TryPassageCourse(IJourneyGuideWorld world, double bearing, out Candidate course)
    {
        course = default;
        if (escapeTarget.HasValue || extraHeight > 0 || requiredTerrainLift >= 1)
        {
            passageExit = null;
            corridorSlope = Math.Max(corridorSlope, solidGrade);
            return false;
        }

        if (passageExit.HasValue)
        {
            double reached = Math.Max(1, Speed * .15);
            if (passageBend.HasValue && Position.HorizontalDistance(passageBend.Value) <= reached)
                passageBend = null;
            var waypoint = passageBend ?? passageExit.Value;
            if (Position.HorizontalDistance(waypoint) > reached &&
                TryPassageLeg(world, Position, waypoint, out course))
            {
                if (!passageBend.HasValue) course = PassageRunway(world, waypoint, course);
                CorridorChoice = "Committed lateral passage";
                return true;
            }
            passageExit = null;
        }
        if (forecastRange < 5 || solidGrade <= corridorSlope + .015) return false;

        // Probe the actual camera envelope at the terrain-following height.
        // Work within the observed prefix and the adapter's 64 m sweep limit.
        var ahead = Direction(bearing, 0);
        double range = Math.Min(52, forecastRange);
        var end = Along(Position, ahead, range);
        double rise = Clamp(corridorSlope, -.36, 1.28);
        end = Along(end, Up, range * rise);
        double length = Position.Distance(end);
        if (length < 1) return false;
        double clear = SafeClearance(world, Position, end);
        if (clear >= length - .01)
        {
            CorridorChoice = "Clear at cruising height";
            return false;
        }

        // Aim beyond the first contact, not merely at a clear ray ending on
        // this side of the tree. Check forward room beyond that shoulder.
        double contact = clear * range / length;
        double advance = Math.Min(range, contact + 4);
        var shoulder = Along(Position, ahead, advance);
        var across = Direction(bearing + 90, 0);
        int firstSide = detourSide == 0 ? 1 : detourSide;
        // In staggered rows a diagonal can cut through this row even though
        // the next opening is reachable by continuing before turning.
        for (int shape = 0; shape < 2; shape++)
        {
            var approach = Position;
            Candidate entry = default;
            if (shape == 1)
            {
                double lead = contact - 4;
                if (lead < 2) break;
                approach = Along(Along(Position, ahead, lead), Up, rise * lead);
                if (!TryPassageLeg(world, Position, approach, out entry)) break;
            }
            foreach (int offset in PassageOffsets)
            {
                var exit = Along(shoulder, across, offset * firstSide);
                if (!world.TryGetSurface(exit, out double floor) || !Finite(floor)) continue;
                exit = Along(exit, Up, Clamp(floor + altitude - Position.Y, -advance * .36, advance * .36));
                if (!TryPassageLeg(world, approach, exit, out var leg)) continue;
                var beyond = Along(exit, ahead, Math.Max(4, cruise * .6));
                if (!world.TryGetSurface(beyond, out double nextFloor) || !Finite(nextFloor)) continue;
                beyond = Along(beyond, Up, Clamp(nextFloor - floor, -2, 2));
                if (SafeClearance(world, exit, beyond) < exit.Distance(beyond) - .01) continue;
                passageBend = shape == 0 ? (AutonomyPosition?)null : approach;
                passageExit = exit;
                detourSide = Math.Sign(offset * firstSide);
                CorridorChoice = shape == 0 ? "Entered lateral passage" : "Entered bending passage";
                course = shape == 0 ? PassageRunway(world, exit, leg) : entry;
                return true;
            }
        }
        corridorSlope = Math.Max(corridorSlope, solidGrade);
        CorridorChoice = "Overflight after passage search";
        return false;
    }

    private static readonly AutonomyPosition Up = new AutonomyPosition(0, 1, 0);
    private static readonly int[] PassageOffsets = { 3, -3, 7, -7, 12, -12 };

    // A steering waypoint need not be a stopping point. Credit only newly
    // swept space on the same heading for braking; a tight bend still slows.
    private Candidate PassageRunway(IJourneyGuideWorld world, AutonomyPosition exit, Candidate leg)
    {
        double stopping = cruise * ScanInterval + cruise * cruise / (2 * Brake) + Margin;
        if (leg.Clear >= stopping) return leg;
        var continuation = Along(exit, Direction(leg.Yaw, leg.Pitch), stopping - leg.Clear);
        double runway = leg.Clear + SafeClearance(world, exit, continuation);
        return new Candidate(leg.Yaw, leg.Pitch, runway, leg.Side, leg.Score);
    }

    private static bool TryPassageLeg(IJourneyGuideWorld world, AutonomyPosition from, AutonomyPosition exit, out Candidate leg)
    {
        leg = default;
        double length = from.Distance(exit);
        if (length < 1 || SafeClearance(world, from, exit) < length - .01) return false;
        double elevation = Math.Atan2(exit.Y - from.Y, from.HorizontalDistance(exit)) * 180 / Math.PI;
        leg = new Candidate(Bearing(from, exit), elevation, length, 0, length);
        return true;
    }

    private bool TryClimbTerrain(IJourneyGuideWorld world, Candidate direct, double horizon, double floor)
    {
        if (requiredTerrainLift < 1 || direct.Frontier || direct.Clear >= horizon - Margin) return false;

        // A loaded rise is too steep for the ordinary 52° approach. Where
        // space allows, keep moving toward it on a steeper line. If a cliff
        // face blocks even that line, gain altitude on a checked vertical.
        // A remote height sample can sit far above the local slope. Seek a
        // pass around it instead of repeatedly spending a full horizon going
        // straight up in the same column. The allowance follows the ground as
        // we cross a real mountainside; it is not a limit on world elevation.
        double liftRoom = floor + altitude + 36 - Position.Y;
        if (requiredTerrainLift > Math.Max(0, liftRoom) && !escapeTarget.HasValue &&
            TrySkirtRidge(world, direct.Yaw, horizon, floor)) return true;
        if (liftRoom <= Margin) return false;
        double climb = Math.Min(Math.Min(horizon, liftRoom), Math.Max(5, requiredTerrainLift + 3));
        const double steepPitch = 72;
        double clear = SafeClearance(world, Position, Along(Position, Direction(direct.Yaw, steepPitch), climb));
        // A usable partial diagonal already advances along the itinerary.
        lifting = clear < Math.Min(climb - Margin, Math.Max(3, Speed * ScanInterval + Margin));
        if (lifting)
        {
            clear = SafeClearance(world, Position, Along(Position, new AutonomyPosition(0, 1, 0), climb));
            if (clear < 2) { lifting = false; return false; }
        }
        selected = new Candidate(direct.Yaw, lifting ? 90 : steepPitch, clear, 0, clear);
        speedLimit = Math.Min(cruise, Math.Sqrt(2 * Brake * Math.Max(0, clear - Margin)));
        Status = "Climbing ahead of a steep ridge";
        return true;
    }

    private bool TrySkirtRidge(IJourneyGuideWorld world, double bearing, double horizon, double floor)
    {
        int firstSide = detourSide == 0 ? 1 : detourSide;
        foreach (int angle in DetourAngles)
        {
            double yawToExit = bearing + angle * firstSide;
            var exit = Along(Position, Direction(yawToExit, 0), horizon);
            if (!world.TryGetSurface(exit, out double height) || !Finite(height) || height > floor + altitude) continue;
            exit = new AutonomyPosition(exit.X, (float)Math.Max(Position.Y, height + altitude), exit.Z);
            double length = Position.Distance(exit);
            if (SafeClearance(world, Position, exit) < length - .01) continue;
            escapeTarget = exit;
            double exitPitch = Math.Atan2(exit.Y - Position.Y, Position.HorizontalDistance(exit)) * 180 / Math.PI;
            selected = new Candidate(yawToExit, exitPitch, length, Math.Sign(angle * firstSide), length);
            lifting = false;
            speedLimit = Math.Min(cruise, Math.Sqrt(2 * Brake * (length - Margin)));
            Status = "Taking a clear passage around a steep ridge";
            return true;
        }
        return false;
    }

    private bool TryLiftOutOfTerrain(IJourneyGuideWorld world, double dt)
    {
        if (!(world is IJourneyTerrainContact terrain)) return false;
        contactScanIn -= dt;
        if (contactScanIn <= 0)
        {
            bool contact = terrain.NeedsVerticalEscape(Position);
            if (contact && !inTerrainContact) Speed = 0;
            if (!contact && inTerrainContact) scanIn = 0;
            inTerrainContact = contact;
            contactScanIn = contact ? .10 : ScanInterval;
        }
        if (!inTerrainContact) return false;

        // Prioritize a measured vertical escape even while withdrawing. Take
        // only the step whose entire sweep the sensor just admitted. An
        // overhead blocker leaves navigation free to seek another direction.
        double nextSpeed = Math.Min(Math.Min(cruise, 5), Speed + 4 * dt);
        var up = new AutonomyPosition(0, 1, 0);
        var next = Along(Position, up, nextSpeed * dt);
        double step = Position.Distance(next);
        if (step <= .00001 || SafeClearance(world, Position, next) < step - .00001) return false;
        if (recentPath.Count > 0) recentPath.Clear();
        withdrawing = trackMotion = lifting = false;
        escapeTarget = rearWaypoint = null;
        scanIn = 0;
        Forward = up;
        pitch = 90;
        Position = next;
        Speed = nextSpeed;
        Travelled += step;
        Status = "Rising clear of terrain contact";
        return true;
    }

    // An obstruction high on the direct approach is grounds to climb early.
    // The lateral alternatives remain free to fly beneath or around geometry.
    private double FlightSlope(int offset, double span) =>
        Math.Abs(offset) < 1 && span > 1 ? corridorSlope : double.NegativeInfinity;
}
