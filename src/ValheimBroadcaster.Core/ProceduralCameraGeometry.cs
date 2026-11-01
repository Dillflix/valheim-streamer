using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Shared player-camera offsets and geometric carrier reachability.
    /// Picture quality and scene availability are evaluated separately.</summary>
    public static class ProceduralCameraGeometry
    {
        public const int CandidateCount = 10;
        private static readonly double[] Angles = { 0, -35, 35, -75, 75, 180, 0, -55, 55, 180 };
        private static readonly double[] Distances = { 1, 1, 1, 1, 1, 1, .5, .5, .5, .45 };
        private static readonly double[] Heights = { 1, 1, 1, .65, .65, .8, .25, .25, .25, .2 };

        public static AutonomyPosition Offset(int index, double horizontal, double height, double azimuth, bool interior)
        {
            if (index < 0 || index >= CandidateCount) throw new ArgumentOutOfRangeException(nameof(index));
            if (!SettingsValid(horizontal, height, azimuth))
                throw new ArgumentException("Procedural camera dimensions and azimuth must be finite and dimensions nonnegative.");
            double radians = ((azimuth + Angles[index]) % 360) * Math.PI / 180;
            double radius = Math.Max(1.5, (interior ? Math.Min(horizontal, 6) : horizontal) * Distances[index]);
            double lift = Math.Max(.5, (interior ? Math.Min(height, 1.8) : height) * Heights[index]);
            return new AutonomyPosition((float)(Math.Sin(radians) * radius), (float)lift, (float)(Math.Cos(radians) * radius));
        }

        // targetOffset is the target player's position minus the current carrier.
        // The nominal candidate endpoint is sufficient to prove a reachable shot
        // exists. Requiring the focus endpoint too rejects valid wide-offset views.
        // The adapter still checks the actual solved/interpolated destination.
        public static double RequiredDisplacement(double targetOffsetX, double targetOffsetY, double targetOffsetZ,
            double horizontal, double height, double azimuth, bool interior, double leadX = 0, double leadZ = 0)
        {
            if (!Finite(targetOffsetX) || !Finite(targetOffsetY) || !Finite(targetOffsetZ) ||
                !Finite(leadX) || !Finite(leadZ) || !SettingsValid(horizontal, height, azimuth))
                return double.PositiveInfinity;
            double best = double.PositiveInfinity;
            for (int i = 0; i < CandidateCount; i++)
            {
                AutonomyPosition offset = Offset(i, horizontal, height, azimuth, interior);
                double x = targetOffsetX + offset.X + leadX;
                double y = targetOffsetY + 1.5 + offset.Y - 2;
                double z = targetOffsetZ + offset.Z + leadZ;
                best = Math.Min(best, Math.Sqrt(x * x + y * y + z * z));
            }
            return best;
        }

        private static bool SettingsValid(double horizontal, double height, double azimuth) =>
            Finite(horizontal) && horizontal >= 0 && horizontal <= float.MaxValue &&
            Finite(height) && height >= 0 && height <= float.MaxValue && Finite(azimuth);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
