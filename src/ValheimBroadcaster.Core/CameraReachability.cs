using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Bound the actual carrier destinations for a new player-camera shot.</summary>
    public static class CameraReachability
    {
        public const double ImmediateLimit = 91;
        public static bool Safe(double targetOffsetX, double targetOffsetY, double targetOffsetZ,
            double cameraHorizontal, double cameraHeight, double azimuthDegrees)
            => RequiredDisplacement(targetOffsetX, targetOffsetY, targetOffsetZ,
                cameraHorizontal, cameraHeight, azimuthDegrees) < ImmediateLimit;

        // Conservative maximum carrier movement across the obstruction-clipped shot.
        // This is geometry only: scene loading is not evidence of distant travel.
        public static double RequiredDisplacement(double targetOffsetX, double targetOffsetY, double targetOffsetZ,
            double cameraHorizontal, double cameraHeight, double azimuthDegrees)
        {
            if (!Finite(targetOffsetX) || !Finite(targetOffsetY) || !Finite(targetOffsetZ) ||
                !Nonnegative(cameraHorizontal) || !Nonnegative(cameraHeight) ||
                !Nonnegative(azimuthDegrees) || azimuthDegrees > 360) return double.PositiveInfinity;

            // Match CameraRig's world-space offset, the player's +1.5m focus and
            // ObserverController's carrier position 2m below the rendered camera.
            double radians = azimuthDegrees * Math.PI / 180;
            double focusY = targetOffsetY + 1.5 - 2;
            double desiredX = targetOffsetX + Math.Sin(radians) * cameraHorizontal;
            double desiredY = focusY + cameraHeight;
            double desiredZ = targetOffsetZ + Math.Cos(radians) * cameraHorizontal;

            // Obstruction clipping places the camera on the focus-to-desired
            // segment. Bounding both endpoints bounds every clipped destination.
            // Actual smoothed camera displacement is checked again by the adapter.
            return Math.Max(Length(targetOffsetX, focusY, targetOffsetZ), Length(desiredX, desiredY, desiredZ));
        }

        private static double Length(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);
        private static bool Nonnegative(double value) => value >= 0 && Finite(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
