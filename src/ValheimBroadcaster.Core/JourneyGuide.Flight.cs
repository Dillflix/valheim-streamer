using System;

namespace ValheimBroadcaster.Core;

/// <summary>Optional coarse observation of solid obstacles in the intended flight corridor.
/// Returns a suggested climb gradient. Actual motion always needs a separate swept check.</summary>
public interface IJourneyFlightCorridor
{
    bool TryGetClimbSlope(AutonomyPosition from, AutonomyPosition heading, double range, out double slope);
}

public sealed partial class JourneyGuide
{
    private double corridorSlope = double.NegativeInfinity;

    private void ObserveFlightCorridor(IJourneyGuideWorld world, double bearing)
    {
        corridorSlope = double.NegativeInfinity;
        if (!(world is IJourneyFlightCorridor sensor)) return;
        double range = Math.Min(Position.HorizontalDistance(destination), Clamp(cruise * 4, 18, 48));
        if (range < 2) return;
        if (sensor.TryGetClimbSlope(Position, Direction(bearing, 0), range, out double slope) && Finite(slope))
            corridorSlope = Clamp(slope, -.36, .95);
    }

    // An obstruction high on the direct approach is grounds to climb early.
    // The lateral alternatives remain free to fly beneath or around geometry.
    private double FlightSlope(int offset, double span) =>
        Math.Abs(offset) < 1 && span > 1 ? corridorSlope : double.NegativeInfinity;
}
