using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Ordinary Scenic pace is selected by the shot's purpose. These
    /// rates describe camera travel and changes of view, not a promise of good
    /// geometry. Native trajectory preparation supplies the actual samples.</summary>
    public static class ScenicPacingPolicy
    {
        public static double MinimumSeconds(ScenicShotRole role) => role == ScenicShotRole.Detail ? 8 :
            role == ScenicShotRole.Traverse ? 10 : 12;
        public static double TravelMetresPerSecond(ScenicShotRole role, bool restrained) =>
            (role == ScenicShotRole.Detail ? .95 : role == ScenicShotRole.Establish ? 1.45 :
                role == ScenicShotRole.Reveal ? 1.2 : 1.4) * (restrained ? .7 : 1);
        public static double ViewDegreesPerSecond(ScenicShotRole role, float fieldOfView, bool restrained) =>
            (role == ScenicShotRole.Detail ? 5.5 : role == ScenicShotRole.Traverse ? 5 : 4.5) *
            Math.Max(.6, Math.Min(1.3, fieldOfView / 60d)) * (restrained ? .7 : 1);
        public static bool Restrained(ScenicFeature feature) => feature.SubjectKind == ScenicSubjectKind.Npc ||
            feature.ScopeLabel == "Opening" || feature.ScopeLabel == "Room";

        // A proposal has not yet been evaluated on its native curve. Use the
        // same role policy for authored and spatial plans; preparation replaces
        // this estimate with measured timing, without changing the trajectory.
        public static double ProposedSeconds(ScenicFeature feature, ScenicShotRole role, double travel, double visualChange)
        {
            double preferredMaximum = role == ScenicShotRole.Detail ? 14 : role == ScenicShotRole.Traverse ? 18 : 20;
            double development = Math.Max(travel / TravelMetresPerSecond(role, Restrained(feature)), visualChange / .08);
            return Math.Max(MinimumSeconds(role), Math.Max(travel / 2, Math.Min(preferredMaximum, development)));
        }
    }

    /// <summary>Immutable time-to-route-distance mapping measured before a shot
    /// is admitted. More time is assigned to a close subject turn, while low
    /// angular-change sections keep moving. Monotone cubic interpolation gives
    /// continuous velocity and cannot overshoot the validated route.</summary>
    public sealed class ScenicPacingProfile
    {
        public const int MaximumPoints = 257;
        private readonly double[] time, distance, slope;
        public int Count => time.Length;
        public double DurationSeconds { get; }
        public double TimeFraction(int index) => time[index];
        public double DistanceFraction(int index) => distance[index];

        private ScenicPacingProfile(double seconds, double[] times, double[] distances)
        {
            if (!Finite(seconds) || seconds < 1 || seconds > 604800 || times.Length < 2 || times.Length > MaximumPoints ||
                times.Length != distances.Length) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (times[0] != 0 || distances[0] != 0 || times[times.Length - 1] != 1 || distances[distances.Length - 1] != 1)
                throw new ArgumentException("Scenic pacing must preserve both route endpoints.");
            for (int i = 0; i < times.Length; i++)
                if (!Finite(times[i]) || !Finite(distances[i]) || times[i] < 0 || times[i] > 1 || distances[i] < 0 || distances[i] > 1 ||
                    i > 0 && (times[i] <= times[i - 1] || distances[i] <= distances[i - 1]))
                    throw new ArgumentException("Scenic pacing must advance monotonically.");
            DurationSeconds = seconds; time = (double[])times.Clone(); distance = (double[])distances.Clone();
            slope = new double[time.Length];
            for (int i = 0; i < slope.Length; i++)
            {
                if (i == 0) slope[i] = Secant(0);
                else if (i == slope.Length - 1) slope[i] = Secant(i - 1);
                else
                {
                    double before = Secant(i - 1), after = Secant(i);
                    double h0 = time[i] - time[i - 1], h1 = time[i + 1] - time[i];
                    double w0 = 2 * h1 + h0, w1 = h1 + 2 * h0;
                    slope[i] = (w0 + w1) / (w0 / before + w1 / after);
                }
                if (!Finite(slope[i]) || slope[i] <= 0)
                    throw new ArgumentException("Scenic pacing contains an unrepresentable rate interval.");
            }
        }

        public static ScenicPacingProfile Restore(double seconds, double[] times, double[] distances) =>
            new ScenicPacingProfile(seconds, times ?? throw new ArgumentNullException(nameof(times)),
                distances ?? throw new ArgumentNullException(nameof(distances)));

        public static ScenicPacingProfile Measure(ScenicShotRole role, float fieldOfView, bool restrained,
            IReadOnlyList<AutonomyPosition> cameras, IReadOnlyList<AutonomyPosition> aims)
        {
            if (!Enum.IsDefined(typeof(ScenicShotRole), role) || float.IsNaN(fieldOfView) || float.IsInfinity(fieldOfView) ||
                fieldOfView < 30 || fieldOfView > 110 || cameras == null || aims == null ||
                cameras.Count < 2 || cameras.Count > MaximumPoints || cameras.Count != aims.Count)
                throw new ArgumentException("Pacing requires bounded paired native trajectory samples.");
            int count = cameras.Count;
            var costs = new double[count - 1];
            double travelRate = ScenicPacingPolicy.TravelMetresPerSecond(role, restrained);
            double angleRate = ScenicPacingPolicy.ViewDegreesPerSecond(role, fieldOfView, restrained);
            for (int i = 1; i < count; i++)
            {
                double travel = cameras[i].Distance(cameras[i - 1]);
                double angle = ViewAngle(cameras[i - 1], aims[i - 1], cameras[i], aims[i]);
                costs[i - 1] = Math.Max(.00001, Math.Max(travel / travelRate, angle / angleRate));
            }
            // Anticipate local rate changes over neighbouring samples. The
            // conservative envelope never speeds up a difficult interval and
            // avoids a brake/accelerate step at a single close-focus sample.
            var smooth = new double[costs.Length];
            for (int i = 0; i < costs.Length; i++)
                for (int j = Math.Max(0, i - 8); j <= Math.Min(costs.Length - 1, i + 8); j++)
                    smooth[i] = Math.Max(smooth[i], costs[j] * Math.Exp(-Math.Abs(j - i) / 4d));
            var times = new double[count]; var distances = new double[count];
            double total = 0;
            for (int i = 1; i < count; i++) { total += smooth[i - 1]; times[i] = total; distances[i] = i / (double)(count - 1); }
            for (int i = 1; i < count; i++) times[i] /= total;
            // The endpoint ramps remove part of cruising time. Account for
            // that here instead of silently increasing the measured rates.
            double seconds = Math.Max(ScenicPacingPolicy.MinimumSeconds(role), total / (1 - ScenicMotionProfile.RampFraction));
            return new ScenicPacingProfile(seconds, times, distances);
        }

        public double DistanceAt(double fraction)
        {
            if (!Finite(fraction)) throw new ArgumentOutOfRangeException(nameof(fraction));
            if (fraction <= 0) return 0; if (fraction >= 1) return 1;
            int lo = 0, hi = time.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (time[mid] <= fraction) lo = mid; else hi = mid; }
            double h = time[hi] - time[lo], u = (fraction - time[lo]) / h, u2 = u * u, u3 = u2 * u;
            double value = (2 * u3 - 3 * u2 + 1) * distance[lo] + (u3 - 2 * u2 + u) * h * slope[lo] +
                (-2 * u3 + 3 * u2) * distance[hi] + (u3 - u2) * h * slope[hi];
            return Math.Max(distance[lo], Math.Min(distance[hi], value));
        }

        private double Secant(int i) => (distance[i + 1] - distance[i]) / (time[i + 1] - time[i]);
        private static double ViewAngle(AutonomyPosition ca, AutonomyPosition aa, AutonomyPosition cb, AutonomyPosition ab)
        {
            double ax = aa.X - ca.X, ay = aa.Y - ca.Y, az = aa.Z - ca.Z;
            double bx = ab.X - cb.X, by = ab.Y - cb.Y, bz = ab.Z - cb.Z;
            double length = Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz));
            if (length < .0001) throw new ArgumentException("Pacing requires a separated camera and aim.");
            return Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) / length))) * 180 / Math.PI;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
