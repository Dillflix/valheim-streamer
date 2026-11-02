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
        yield return ("Body composition permits deliberate off-center placement without a score penalty", OffCenterBody);
        yield return ("Full body framing detects cropped feet while the elevated focus remains visible", BodyClipping);
        yield return ("Safe frame and headroom degrade continuously before body clipping", SafeFrame);
        yield return ("Full body readability uses actual projected size and lens", BodyReadability);
        yield return ("Composition guide allows movement within its deliberate off-center tolerance", CompositionGuide);
        yield return ("Body envelope crossing the near plane is unsuitable framing", BodyNearPlane);
        yield return ("Body projection validates every corner and framing parameter", InvalidBody);
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

    private static ShotProjection.Point[] Box(double x, double bottom, double z, double height = 2,
        double halfWidth = .4, double halfDepth = .3)
    {
        var corners = new ShotProjection.Point[8];
        int index = 0;
        for (int y = 0; y < 2; y++)
        for (int depth = -1; depth <= 1; depth += 2)
        for (int width = -1; width <= 1; width += 2)
            corners[index++] = new ShotProjection.Point(x + width * halfWidth, bottom + y * height,
                z + depth * halfDepth);
        return corners;
    }

    private static ShotProjection.Envelope ViewportBox(double left, double bottom, double right, double top)
    {
        // A thin body at z=10, 90-degree vertical lens and square image.
        return ShotProjection.MeasureEnvelope(Box((left + right - 1) * 10, (bottom - .5) * 20, 10,
            (top - bottom) * 20, (right - left) * 10, 0), 90, 1, .3);
    }

    private static void OffCenterBody()
    {
        var centered = ViewportBox(.45, .35, .55, .65);
        var leftThird = ViewportBox(.28, .35, .38, .65);
        var rightThird = ViewportBox(.62, .35, .72, .65);
        Check(centered.FullyInFrame && leftThird.FullyInFrame && rightThird.FullyInFrame);
        Near(1, centered.Quality); Near(centered.Quality, leftThird.Quality); Near(centered.Quality, rightThird.Quality);
        Near(.33, leftThird.X); Near(.67, rightThird.X);
    }

    private static void BodyClipping()
    {
        // Valheim focus is root+1.5, so a center-aimed torso at z=1 can
        // remain visible while its feet extend below the image.
        Check(ShotProjection.Measure(0, 0, 1, 0, 90, 1, .3).CenterInFrame);
        var body = ShotProjection.MeasureEnvelope(Box(0, -1.5, 1, 2, .3, .05), 90, 1, .3);
        Check(body.InFront && !body.FullyInFrame && body.MinY < 0 && body.VisibleFraction < .8);
        Check(body.Y < .5 && body.Quality < .8);
        var croppedHead = ViewportBox(.4, .8, .6, 1.1);
        Check(croppedHead.InFront && !croppedHead.FullyInFrame && croppedHead.Headroom < 0);
        Near(2.0 / 3, croppedHead.VisibleFraction);
    }

    private static void SafeFrame()
    {
        var clear = ViewportBox(.2, .4, .3, .7);
        var nearSide = ViewportBox(.02, .4, .12, .7);
        var nearTop = ViewportBox(.2, .69, .3, .99);
        var lessHeadroom = ViewportBox(.2, .68, .3, .98);
        Check(clear.FullyInFrame && nearSide.FullyInFrame && nearTop.FullyInFrame);
        Check(nearSide.SafeFraction < clear.SafeFraction && nearSide.Quality < clear.Quality);
        Near(.01, nearTop.Headroom);
        Check(nearTop.HeadroomScore < lessHeadroom.HeadroomScore && nearTop.Quality < lessHeadroom.Quality);
    }

    private static void BodyReadability()
    {
        var readable = ShotProjection.MeasureEnvelope(Box(0, -1, 15, 2, .4, 0), 90, 1, .3);
        var tiny = ShotProjection.MeasureEnvelope(Box(0, -1, 100, 2, .4, 0), 90, 1, .3);
        Check(tiny.FullyInFrame && readable.FullyInFrame);
        Near(1, readable.Readability); Near(.2, tiny.Readability);
        Check(tiny.Quality < .25 && tiny.Quality < readable.Quality);
        var narrow = ShotProjection.MeasureEnvelope(Box(0, -1, 100, 2, .4, 0), 30, 1, .3);
        Check(narrow.Readability > tiny.Readability && narrow.Quality > tiny.Quality);
        // Twice the distance with half the lens tangent yields the same image.
        double equivalentFov = Math.Atan(.5) * 360 / Math.PI;
        var equivalent = ShotProjection.MeasureEnvelope(Box(0, -1, 200, 2, .4, 0), equivalentFov, 1, .3);
        Near(tiny.RelativeHeight, equivalent.RelativeHeight); Near(tiny.Quality, equivalent.Quality);
    }

    private static void CompositionGuide()
    {
        var body = ViewportBox(.28, .35, .38, .65);
        Near(0, body.PlacementError(.33, .5, .03, .05));
        Near(0, body.PlacementError(.35, .54, .03, .05));
        Check(body.PlacementError(.6, .5, .03, .05) > .2);
        Near(1, body.PlacementError(double.NaN, .5, .03, .05));
    }

    private static void BodyNearPlane()
    {
        var clipped = ShotProjection.MeasureEnvelope(Box(0, -1, .5, 2, .4, .3), 90, 1, .3);
        Check(!clipped.InFront && !clipped.FullyInFrame); Near(0, clipped.Quality);
        var behind = ShotProjection.MeasureEnvelope(Box(0, -1, -5), 90, 1, .3);
        Check(!behind.InFront); Near(0, behind.Quality);
    }

    private static void InvalidBody()
    {
        var corners = Box(0, -1, 10);
        Check(!ShotProjection.MeasureEnvelope(corners, 90, 0, .3).InFront);
        Check(!ShotProjection.MeasureEnvelope(corners, 90, 1, .3, minimumReadableHeight: 0).InFront);
        Check(!ShotProjection.MeasureEnvelope(corners, 90, 1, .3, safeMargin: .5).InFront);
        Check(!ShotProjection.MeasureEnvelope(corners, 90, 1, .3, minimumHeadroom: double.NaN).InFront);
        Check(!ShotProjection.MeasureEnvelope(new ShotProjection.Point[7], 90, 1, .3).InFront);
        corners[7] = new ShotProjection.Point(double.PositiveInfinity, 0, 10);
        Check(!ShotProjection.MeasureEnvelope(corners, 90, 1, .3).InFront);
    }
}
