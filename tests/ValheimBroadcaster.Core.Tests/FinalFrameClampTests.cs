using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class FinalFrameClampTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Final clamp requires nothing while the subject sits inside the hard band", InsideBand);
        yield return ("Final clamp is exactly zero at the band boundary", BandEdges);
        yield return ("Final clamp yaws right toward a subject escaping screen-right", OutsideRight);
        yield return ("Final clamp yaws left toward a subject escaping screen-left", OutsideLeft);
        yield return ("Final clamp pitches up toward a subject escaping the top", OutsideTop);
        yield return ("Final clamp pitches down toward a subject escaping the bottom", OutsideBottom);
        yield return ("Final clamp corrections place the subject exactly on the band", MinimalMagnitude);
        yield return ("Final clamp corrects both axes independently when both escape", BothAxes);
        yield return ("Final clamp demands a full look-at for a subject behind the camera", BehindCamera);
        yield return ("Final clamp corrections grow with the presented field of view", FovVariation);
        yield return ("Final clamp yaw respects the aspect ratio while pitch ignores it", AspectVariation);
        yield return ("Final clamp accepts an explicit band", ExplicitBand);
        yield return ("Final clamp rejects out-of-range contracts", Validation);
    }

    private const float Fov = 60f, Aspect = 16f / 9f;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    /// <summary>The view-space angle of a centred screen fraction.</summary>
    private static double Angle(double fraction, double fov, double extent) =>
        Math.Atan(2 * Math.Abs(fraction) * Math.Tan(fov * Math.PI / 360) * extent) * 180 / Math.PI;

    private static void InsideBand()
    {
        var centred = FinalFrameClamp.Correct(0, 0, true, Fov, Aspect);
        Check(!centred.Required && centred.YawDegrees == 0 && centred.PitchDegrees == 0,
            "A centred subject demanded a correction.");
        var offset = FinalFrameClamp.Correct(0.41f, -0.37f, true, Fov, Aspect);
        Check(!offset.Required && !offset.BehindCamera,
            "A subject just inside the hard band demanded a correction.");
    }

    private static void BandEdges()
    {
        Check(!FinalFrameClamp.Correct(FinalFrameClamp.HardBandX, 0, true, Fov, Aspect).Required,
            "A subject exactly on the horizontal band demanded a correction.");
        Check(!FinalFrameClamp.Correct(0, -FinalFrameClamp.HardBandY, true, Fov, Aspect).Required,
            "A subject exactly on the vertical band demanded a correction.");
        var justOut = FinalFrameClamp.Correct(FinalFrameClamp.HardBandX + 0.001f, 0, true, Fov, Aspect);
        Check(justOut.Required && justOut.YawDegrees > 0 && justOut.YawDegrees < 0.2f,
            "A subject just outside the band did not demand a tiny correction.");
    }

    private static void OutsideRight()
    {
        var correction = FinalFrameClamp.Correct(0.6f, 0, true, Fov, Aspect);
        Check(correction.Required && !correction.BehindCamera,
            "A subject outside screen-right demanded no correction.");
        Check(correction.YawDegrees > 0 && correction.PitchDegrees == 0,
            "A screen-right escape did not demand a pure positive yaw.");
        double expected = Angle(0.6, Fov, Aspect) - Angle(FinalFrameClamp.HardBandX, Fov, Aspect);
        Check(Math.Abs(correction.YawDegrees - expected) < 0.001,
            "The right-escape yaw was not the minimal band-restoring angle.");
    }

    private static void OutsideLeft()
    {
        var correction = FinalFrameClamp.Correct(-0.6f, 0, true, Fov, Aspect);
        Check(correction.Required && correction.YawDegrees < 0 && correction.PitchDegrees == 0,
            "A screen-left escape did not demand a pure negative yaw.");
        Check(correction.YawDegrees == -FinalFrameClamp.Correct(0.6f, 0, true, Fov, Aspect).YawDegrees,
            "The left correction was not symmetric with the right correction.");
    }

    private static void OutsideTop()
    {
        var correction = FinalFrameClamp.Correct(0, 0.55f, true, Fov, Aspect);
        Check(correction.Required && correction.PitchDegrees > 0 && correction.YawDegrees == 0,
            "A top escape did not demand a pure positive pitch.");
        double expected = Angle(0.55, Fov, 1) - Angle(FinalFrameClamp.HardBandY, Fov, 1);
        Check(Math.Abs(correction.PitchDegrees - expected) < 0.001,
            "The top-escape pitch was not the minimal band-restoring angle.");
    }

    private static void OutsideBottom()
    {
        var correction = FinalFrameClamp.Correct(0, -0.55f, true, Fov, Aspect);
        Check(correction.Required && correction.PitchDegrees < 0 && correction.YawDegrees == 0,
            "A bottom escape did not demand a pure negative pitch.");
        Check(correction.PitchDegrees == -FinalFrameClamp.Correct(0, 0.55f, true, Fov, Aspect).PitchDegrees,
            "The bottom correction was not symmetric with the top correction.");
    }

    private static void MinimalMagnitude()
    {
        // Rotating by the returned yaw must land the subject exactly on the
        // band: tan(subjectAngle - yaw) recovers the band's screen fraction.
        double tanHalfX = Math.Tan(Fov * Math.PI / 360) * Aspect;
        var correction = FinalFrameClamp.Correct(0.8f, 0, true, Fov, Aspect);
        double corrected = Math.Tan(Math.Atan(2 * 0.8 * tanHalfX) -
            correction.YawDegrees * Math.PI / 180) / (2 * tanHalfX);
        Check(Math.Abs(corrected - FinalFrameClamp.HardBandX) < 0.0001,
            "The yaw correction did not place the subject exactly on the band.");
        Check(FinalFrameClamp.Correct(0.9f, 0, true, Fov, Aspect).YawDegrees > correction.YawDegrees,
            "A further escape did not demand a larger correction.");
    }

    private static void BothAxes()
    {
        var correction = FinalFrameClamp.Correct(-0.7f, 0.6f, true, Fov, Aspect);
        Check(correction.Required && correction.YawDegrees < 0 && correction.PitchDegrees > 0,
            "A two-axis escape did not demand corrections on both axes.");
        Check(correction.YawDegrees == FinalFrameClamp.Correct(-0.7f, 0, true, Fov, Aspect).YawDegrees &&
            correction.PitchDegrees == FinalFrameClamp.Correct(0, 0.6f, true, Fov, Aspect).PitchDegrees,
            "Per-axis corrections changed when both axes escaped at once.");
    }

    private static void BehindCamera()
    {
        var correction = FinalFrameClamp.Correct(0, 0, false, Fov, Aspect);
        Check(correction.Required && correction.BehindCamera,
            "A subject behind the camera did not demand a full look-at.");
        Check(correction.YawDegrees == 0 && correction.PitchDegrees == 0,
            "A behind-camera correction carried screen-space angles.");
        Check(FinalFrameClamp.Correct(float.NaN, float.NaN, false, Fov, Aspect).BehindCamera,
            "Behind-camera handling required finite screen coordinates.");
    }

    private static void FovVariation()
    {
        float narrow = FinalFrameClamp.Correct(0.6f, 0, true, 30f, Aspect).YawDegrees;
        float wide = FinalFrameClamp.Correct(0.6f, 0, true, 90f, Aspect).YawDegrees;
        Check(narrow > 0 && wide > narrow,
            "A wider field of view did not demand a larger angular correction for the same fraction.");
        double expected = Angle(0.6, 90, Aspect) - Angle(FinalFrameClamp.HardBandX, 90, Aspect);
        Check(Math.Abs(wide - expected) < 0.001,
            "The wide-fov yaw was not the minimal band-restoring angle.");
    }

    private static void AspectVariation()
    {
        float wide = FinalFrameClamp.Correct(0.6f, 0, true, Fov, 2.37f).YawDegrees;
        float narrow = FinalFrameClamp.Correct(0.6f, 0, true, Fov, 1f).YawDegrees;
        Check(narrow > 0 && wide > narrow,
            "A wider aspect did not demand a larger yaw for the same horizontal fraction.");
        Check(FinalFrameClamp.Correct(0, 0.55f, true, Fov, 2.37f).PitchDegrees ==
            FinalFrameClamp.Correct(0, 0.55f, true, Fov, 1f).PitchDegrees,
            "The pitch correction depended on the aspect ratio.");
    }

    private static void ExplicitBand()
    {
        var tight = FinalFrameClamp.Correct(0.3f, 0, true, Fov, Aspect, 0.2f, 0.2f);
        Check(tight.Required && tight.YawDegrees > 0,
            "An explicit tighter band did not fire inside the default band.");
        Check(!FinalFrameClamp.Correct(0.3f, 0, true, Fov, Aspect, 0.35f, 0.35f).Required,
            "An explicit looser band fired on a subject inside it.");
    }

    private static void Validation()
    {
        try { FinalFrameClamp.Correct(0, 0, true, 0.5f, Aspect); throw new Exception("A sub-degree fov was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(0, 0, true, 175f, Aspect); throw new Exception("An excessive fov was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(0, 0, true, Fov, 0); throw new Exception("A zero aspect was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(float.NaN, 0, true, Fov, Aspect); throw new Exception("A NaN screen position was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(0, float.PositiveInfinity, true, Fov, Aspect); throw new Exception("An infinite screen position was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(0, 0, true, Fov, Aspect, 0, 0.3f); throw new Exception("A zero horizontal band was accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { FinalFrameClamp.Correct(0, 0, true, Fov, Aspect, 0.3f, 0.5f); throw new Exception("A band at the frame edge was accepted."); }
        catch (ArgumentOutOfRangeException) { }
    }
}
