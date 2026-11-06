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
    private double corridorSlope = double.NegativeInfinity;

    private void ObserveFlightCorridor(IJourneyGuideWorld world, double bearing, double currentFloor)
    {
        corridorSlope = double.NegativeInfinity;
        requiredTerrainLift = 0;
        double range = Math.Min(Position.HorizontalDistance(destination), Clamp(cruise * 4, 24, 48));
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
            corridorSlope = Math.Max(corridorSlope, Clamp(solidSlope, -.36, .95));
    }

    private bool TryClimbTerrain(IJourneyGuideWorld world, Candidate direct, double horizon)
    {
        if (requiredTerrainLift < 1 || direct.Frontier || direct.Clear >= horizon - Margin) return false;

        // A loaded rise is too steep for the ordinary 52° approach. Where
        // space allows, keep moving toward it on a steeper line. If a cliff
        // face blocks even that line, gain altitude on a checked vertical.
        double climb = Math.Min(horizon, Math.Max(5, requiredTerrainLift + 3));
        const double steepPitch = 72;
        double clear = SafeClearance(world, Position, Along(Position, Direction(direct.Yaw, steepPitch), climb));
        lifting = clear < climb - Margin;
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
