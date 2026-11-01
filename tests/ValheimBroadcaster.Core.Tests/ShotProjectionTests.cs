using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotProjectionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Framing accepts an off-center player within the screen", OffCenter);
        yield return ("Framing rejects a target behind the actual camera", BehindCamera);
        yield return ("Framing reports partial coverage at a screen edge", PartialEdge);
        yield return ("Framing respects aspect ratio and lens changes", AspectAndLens);
        yield return ("Framing excludes targets before the near plane and invalid poses", InvalidGeometry);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Shot projection assertion failed."); }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > .0001) throw new Exception($"Expected {expected}, got {actual}."); }

    private static void OffCenter()
    {
        var point = ShotProjection.Measure(3, 1, 10, .3, 90, 16.0 / 9, .3);
        Check(point.CenterInFrame && point.InFront && point.X > .5 && point.Y > .5);
        Near(1, point.VisibleFraction);
    }

    private static void BehindCamera()
    {
        var point = ShotProjection.Measure(0, 0, -10, .6, 75, 16.0 / 9, .3);
        Check(!point.CenterInFrame && !point.InFront);
        Near(0, point.VisibleFraction);
    }

    private static void PartialEdge()
    {
        // At 90 degrees and square aspect, x=z lies on the right viewport edge.
        Near(.5, ShotProjection.Measure(10, 0, 10, 1, 90, 1, .3).VisibleFraction);
        Near(0, ShotProjection.Measure(12, 0, 10, 1, 90, 1, .3).VisibleFraction);
    }

    private static void AspectAndLens()
    {
        Check(ShotProjection.Measure(12, 0, 10, .3, 90, 2, .3).CenterInFrame);
        Check(!ShotProjection.Measure(12, 0, 10, .3, 90, 1, .3).CenterInFrame);
        Check(!ShotProjection.Measure(12, 0, 10, .3, 30, 2, .3).CenterInFrame);
        Check(ShotProjection.Measure(0, 0, 5, .6, 75, 1, .3).RelativeHeight >
            ShotProjection.Measure(0, 0, 20, .6, 75, 1, .3).RelativeHeight);
    }

    private static void InvalidGeometry()
    {
        Check(!ShotProjection.Measure(0, 0, .2, .6, 75, 1, .3).InFront);
        Check(!ShotProjection.Measure(double.NaN, 0, 10, .6, 75, 1, .3).InFront);
        Check(!ShotProjection.Measure(0, 0, 10, .6, 75, 0, .3).InFront);
        Check(!ShotProjection.Measure(0, 0, 10, -.6, 75, 1, .3).InFront);
        Check(!ShotProjection.Measure(0, 0, double.MaxValue, .6, 150, 2, .3).InFront);
    }
}
