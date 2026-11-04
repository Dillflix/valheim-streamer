using System;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal static class NativeJourneyGaze
{
    // Native distance interpolation, terrain fitting and the paired aim path
    // must satisfy the rate together. Knot-only bounds cannot establish that.
    internal static bool WithinRate(ScenicSplinePath path, double duration)
    {
        if (!path.Ready || double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0) return false;
        ScenicShotSample previous = path.Sample(0);
        const int samples = 256;
        for (int i = 1; i <= samples; i++)
        {
            ScenicShotSample next = path.Sample(i / (float)samples);
            double ax = previous.AimPosition.X - previous.CameraPosition.X,
                ay = previous.AimPosition.Y - previous.CameraPosition.Y, az = previous.AimPosition.Z - previous.CameraPosition.Z;
            double bx = next.AimPosition.X - next.CameraPosition.X,
                by = next.AimPosition.Y - next.CameraPosition.Y, bz = next.AimPosition.Z - next.CameraPosition.Z;
            double lengths = Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
            if (lengths < .001) return false;
            double cosine = Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) / lengths));
            double rate = Math.Acos(cosine) * 180 / Math.PI * samples / duration;
            if (rate > JourneyGazePolicy.NativeDegreesPerSecondLimit) return false;
            previous = next;
        }
        return true;
    }
}
