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
        yield return ("Depth pressure is zero while the subject sits at the intended distance", DepthAtIntendedDistance);
        yield return ("Depth pressure rises as the projected distance leaves the intended band", DepthRises);
        yield return ("Depth pressure saturates at the depth limit", DepthSaturates);
        yield return ("Velocity toward the camera reduces projected depth pressure but never the current owed pressure", DepthInwardMotion);
        yield return ("Depth pressure combines with lateral pressure by maximum", DepthCombinesWithLateral);
        yield return ("Depth pressure rejects invalid distances", DepthValidation);
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

    private static void DepthAtIntendedDistance()
    {
        Check(FrameRetentionPolicy.DepthPressure(10f, 0, 10f) == 0,
            "A subject at the intended distance produced depth pressure.");
        Check(FrameRetentionPolicy.DepthPressure(11f, 0, 10f) == 0,
            "A subject inside the depth onset band produced depth pressure.");
        Check(FrameRetentionPolicy.DepthPointPressure(10f * FrameRetentionPolicy.DepthPressureOnset, 10f) == 0,
            "Depth pressure started before the onset factor.");
    }

    private static void DepthRises()
    {
        float near = FrameRetentionPolicy.DepthPointPressure(12.5f, 10f);
        float far = FrameRetentionPolicy.DepthPointPressure(14f, 10f);
        Check(near > 0 && far > near && far < 1,
            "Depth pressure did not rise monotonically between the onset and the limit.");
        Check(FrameRetentionPolicy.DepthPressure(10f, 5f, 10f) > 0,
            "A receding subject projected past the band produced no depth pressure.");
        Check(FrameRetentionPolicy.DepthPressure(10f, 5f, 10f) >
            FrameRetentionPolicy.DepthPressure(10f, 2f, 10f),
            "Faster recession did not raise projected depth pressure.");
    }

    private static void DepthSaturates()
    {
        Check(FrameRetentionPolicy.DepthPointPressure(10f * FrameRetentionPolicy.DepthPressureLimit, 10f) == 1,
            "Depth pressure did not saturate at the limit factor.");
        Check(FrameRetentionPolicy.DepthPointPressure(60f, 10f) == 1,
            "Depth pressure fell below one far beyond the limit.");
        Check(FrameRetentionPolicy.DepthPressure(10f, 20f, 10f) == 1,
            "A projected distance beyond the limit did not saturate pressure.");
    }

    private static void DepthInwardMotion()
    {
        Check(FrameRetentionPolicy.DepthPressure(12.5f, 5f, 10f) >
            FrameRetentionPolicy.DepthPressure(12.5f, -5f, 10f),
            "Approach velocity did not reduce the projected depth pressure.");
        float current = FrameRetentionPolicy.DepthPointPressure(13f, 10f);
        Check(current > 0 && FrameRetentionPolicy.DepthPressure(13f, -5f, 10f) == current,
            "Motion toward the camera changed the pressure owed to the current distance.");
        Check(FrameRetentionPolicy.DepthPressure(20f, -100f, 10f) == 1,
            "A projection collapsing behind the camera erased the current owed pressure.");
    }

    private static void DepthCombinesWithLateral()
    {
        float lateral = FrameRetentionPolicy.EdgePressure(0.34f, 0, 0, 0);
        float depth = FrameRetentionPolicy.DepthPressure(13f, 0, 10f);
        Check(lateral > 0 && depth > 0 && Math.Max(lateral, depth) >= lateral &&
            Math.Max(lateral, depth) >= depth,
            "Combined retention pressure fell below one of its components.");
        Check(Math.Max(FrameRetentionPolicy.EdgePressure(0, 0, 0, 0),
            FrameRetentionPolicy.DepthPressure(14f, 0, 10f)) > 0,
            "A centred receding subject produced no combined retention pressure.");
    }

    private static void DepthValidation()
    {
        try { FrameRetentionPolicy.DepthPointPressure(float.NaN, 10f); throw new Exception("A NaN distance was accepted."); }
        catch (ArgumentException) { }
        try { FrameRetentionPolicy.DepthPointPressure(10f, 0); throw new Exception("A zero intended distance was accepted."); }
        catch (ArgumentException) { }
        try { FrameRetentionPolicy.DepthPressure(10f, float.PositiveInfinity, 10f); throw new Exception("An infinite radial velocity was accepted."); }
        catch (ArgumentException) { }
        try { FrameRetentionPolicy.DepthPointPressure(-1f, 10f); throw new Exception("A negative distance was accepted."); }
        catch (ArgumentException) { }
    }
}
