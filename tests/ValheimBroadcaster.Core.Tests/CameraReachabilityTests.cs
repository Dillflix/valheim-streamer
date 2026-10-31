using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CameraReachabilityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Camera reachability uses signed displacement and viewing direction", Direction);
        yield return ("Camera reachability preserves stable following at large offsets", StableLargeOffset);
        yield return ("Camera reachability prevents cuts beyond immediate movement", Cut);
        yield return ("Camera reachability bounds obstruction-clipped destinations", Obstruction);
        yield return ("Camera reachability rejects its exact clearance boundary", Boundary);
        yield return ("Camera reachability rejects invalid distances and offsets", Invalid);
        yield return ("Camera reachability rejects overflowing finite offsets", Overflow);
    }

    private static void Check(bool condition) { if (!condition) throw new Exception("Camera reachability assertion failed."); }
    private static void Direction()
    {
        Check(CameraReachability.Safe(-60, 0, 0, 60, 10, 90));
        Check(!CameraReachability.Safe(-60, 0, 0, 60, 10, 270));
        Check(CameraReachability.Safe(0, 0, -60, 60, 10, 0));
        Check(!CameraReachability.Safe(0, 0, -60, 60, 10, 180));
    }
    private static void StableLargeOffset()
    {
        // Carrier already occupies camera minus2m. It must not be sent back to
        // the target because target distance plus camera offset double-counts it.
        Check(CameraReachability.Safe(-60, -9.5, 0, 60, 10, 90));
        Check(CameraReachability.Safe(-80, -39.5, 0, 80, 40, 90));
        // A modest subject movement remains immediately followable.
        Check(CameraReachability.Safe(-77, -38.5, 2, 80, 40, 90));
    }
    private static void Cut()
    {
        Check(CameraReachability.Safe(-40, 0, 0, 60, 10, 90));
        Check(!CameraReachability.Safe(40, 0, 0, 60, 10, 90));
        Check(!CameraReachability.Safe(0, 85, 0, 2, 10, 0));
    }
    private static void Obstruction()
    {
        // Desired camera is only20m away, but a blocked ray can collapse toward
        // the focus100m away. That shot still requires controlled travel.
        Check(!CameraReachability.Safe(100, .5, 0, 80, 0, 270));
        Check(CameraReachability.Safe(80, .5, 0, 80, 0, 270));
    }
    private static void Boundary()
    {
        Check(CameraReachability.Safe(90.999, .5, 0, 0, 0, 0));
        Check(!CameraReachability.Safe(91, .5, 0, 0, 0, 0));
        Check(!CameraReachability.Safe(91.001, .5, 0, 0, 0, 0));
        Check(CameraReachability.Safe(0, .5, 0, 90.999, 0, 90));
        Check(!CameraReachability.Safe(0, .5, 0, 91, 0, 90));
    }
    private static void Invalid()
    {
        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            Check(!CameraReachability.Safe(value, 0, 0, 0, 0, 0));
            Check(!CameraReachability.Safe(0, value, 0, 0, 0, 0));
            Check(!CameraReachability.Safe(0, 0, value, 0, 0, 0));
            Check(!CameraReachability.Safe(0, 0, 0, value, 0, 0));
            Check(!CameraReachability.Safe(0, 0, 0, 0, value, 0));
            Check(!CameraReachability.Safe(0, 0, 0, 0, 0, value));
        }
        Check(!CameraReachability.Safe(0, 0, 0, -1, 0, 0));
        Check(!CameraReachability.Safe(0, 0, 0, 0, -1, 0));
        Check(!CameraReachability.Safe(0, 0, 0, 0, 0, -1));
        Check(!CameraReachability.Safe(0, 0, 0, 0, 0, 361));
    }
    private static void Overflow()
    {
        Check(!CameraReachability.Safe(double.MaxValue, 0, 0, 0, 0, 0));
        Check(!CameraReachability.Safe(0, 0, 0, double.MaxValue, double.MaxValue, 90));
    }
}
