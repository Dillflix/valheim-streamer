using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class FrameRetentionPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Frame retention pressure is zero for a centred stationary subject", CentredStationary);
        yield return ("Frame retention pressure rises with position toward the frame edge", PositionPressure);
        yield return ("Frame retention pressure rises with velocity toward the frame edge", VelocityPressure);
        yield return ("Frame retention pressure saturates at the hard band", HardBand);
        yield return ("Frame retention motion toward centre never reduces current pressure", InwardMotion);
        yield return ("Frame retention rejects non-finite evidence", Validation);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void CentredStationary()
    {
        Check(FrameRetentionPolicy.EdgePressure(0, 0, 0, 0) == 0,
            "A centred stationary subject produced retention pressure.");
        Check(FrameRetentionPolicy.EdgePressure(0.2f, 0.1f, 0, 0) == 0,
            "A subject inside the safe inner region produced retention pressure.");
        Check(FrameRetentionPolicy.PointPressure(-0.27f, 0.23f) == 0,
            "Pressure started inside the onset band.");
    }

    private static void PositionPressure()
    {
        float near = FrameRetentionPolicy.PointPressure(0.30f, 0);
        float far = FrameRetentionPolicy.PointPressure(0.36f, 0);
        Check(near > 0 && far > near && far < 1,
            "Pressure did not rise monotonically between the onset and the hard band.");
        Check(FrameRetentionPolicy.PointPressure(-0.36f, 0) == far,
            "Horizontal pressure was not symmetric around the centre.");
        Check(FrameRetentionPolicy.PointPressure(0, 0.3f) > 0,
            "The vertical band produced no pressure near the frame edge.");
    }

    private static void VelocityPressure()
    {
        Check(FrameRetentionPolicy.EdgePressure(0.1f, 0, 0.5f, 0) > 0.8f,
            "Velocity toward the edge did not raise predictive pressure.");
        Check(FrameRetentionPolicy.EdgePressure(0.1f, 0, -0.5f, 0) == 0,
            "Velocity toward the centre produced pressure from a safe position.");
        Check(FrameRetentionPolicy.EdgePressure(0, -0.1f, 0, -0.5f) > 0.5f,
            "Downward escape velocity did not raise predictive pressure.");
    }

    private static void HardBand()
    {
        Check(FrameRetentionPolicy.PointPressure(FrameRetentionPolicy.PressureLimitX, 0) == 1,
            "Pressure did not saturate at the horizontal hard band.");
        Check(FrameRetentionPolicy.PointPressure(0.6f, 0) == 1,
            "Pressure fell below one beyond the hard band.");
        Check(FrameRetentionPolicy.PointPressure(0, FrameRetentionPolicy.PressureLimitY) == 1,
            "Pressure did not saturate at the vertical hard band.");
        Check(FrameRetentionPolicy.EdgePressure(0, 0, 0.8f, 0) == 1,
            "A projected position beyond the hard band did not saturate pressure.");
    }

    private static void InwardMotion()
    {
        float current = FrameRetentionPolicy.PointPressure(0.34f, 0);
        Check(current > 0 && FrameRetentionPolicy.EdgePressure(0.34f, 0, -1f, 0) == current,
            "Motion toward the centre changed the pressure owed to the current position.");
    }

    private static void Validation()
    {
        try { FrameRetentionPolicy.PointPressure(float.NaN, 0); throw new Exception("A NaN screen position was accepted."); }
        catch (ArgumentException) { }
        try { FrameRetentionPolicy.EdgePressure(0, 0, float.PositiveInfinity, 0); throw new Exception("An infinite screen velocity was accepted."); }
        catch (ArgumentException) { }
    }
}
