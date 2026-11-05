using System;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal static class NativeJourneyGaze
{
    // Native distance interpolation, terrain fitting and the paired aim path
    // must satisfy the rate together. Knot-only bounds cannot establish that.
    internal static bool WithinRate(ScenicSplinePath path, double duration)
    {
        var evaluation = new Evaluation(); evaluation.Begin(path, duration);
        while (!evaluation.Complete) evaluation.Step(256);
        return evaluation.Accepted;
    }

    /// <summary>Keep native curve verification incremental under the caller's
    /// measured camera-work allowance. Yielding never restarts the check or
    /// turns a partially checked path into accepted geometry.</summary>
    internal sealed class Evaluation
    {
        private const int Intervals = 256;
        private ScenicSplinePath? path;
        private ScenicShotPlan? prepared;
        private ScenicShotSample previous;
        private double duration;
        internal int CheckedIntervals { get; private set; }
        internal bool Complete { get; private set; }
        internal bool Accepted { get; private set; }

        internal void Begin(ScenicSplinePath source, double seconds)
        {
            Reset();
            if (source == null || !source.Ready || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            { Complete = true; return; }
            path = source; prepared = source.PreparedPlan; duration = seconds; previous = source.Sample(0);
        }

        internal bool Step(int intervals)
        {
            if (intervals < 1 || intervals > Intervals) throw new ArgumentOutOfRangeException(nameof(intervals));
            if (Complete) return true;
            if (path == null || !path.Ready || !ReferenceEquals(prepared, path.PreparedPlan))
            { Complete = true; Accepted = false; return true; }
            for (int i = 0; i < intervals && CheckedIntervals < Intervals; i++)
            {
                // Sample uses MotionFraction internally, so these are equal
                // time intervals, including ramp acceleration. Dividing the
                // supplied duration by CruiseFraction again would count ramps
                // twice. The caller may retime without rebuilding geometry.
                ScenicShotSample next = path.Sample((CheckedIntervals + 1) / (float)Intervals);
                double ax = previous.AimPosition.X - previous.CameraPosition.X,
                    ay = previous.AimPosition.Y - previous.CameraPosition.Y, az = previous.AimPosition.Z - previous.CameraPosition.Z;
                double bx = next.AimPosition.X - next.CameraPosition.X,
                    by = next.AimPosition.Y - next.CameraPosition.Y, bz = next.AimPosition.Z - next.CameraPosition.Z;
                double lengths = Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
                CheckedIntervals++;
                if (lengths < .001) { Complete = true; Accepted = false; return true; }
                double cosine = Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) / lengths));
                double rate = Math.Acos(cosine) * 180 / Math.PI * Intervals / duration;
                if (double.IsNaN(rate) || double.IsInfinity(rate) || rate > JourneyGazePolicy.NativeDegreesPerSecondLimit)
                { Complete = true; Accepted = false; return true; }
                previous = next;
            }
            if (CheckedIntervals == Intervals) { Complete = true; Accepted = true; }
            return Complete;
        }

        internal void Reset()
        {
            path = null; prepared = null; previous = default; duration = 0;
            CheckedIntervals = 0; Complete = Accepted = false;
        }
    }
}
