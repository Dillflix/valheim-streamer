using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Perspective framing measurements in camera-local coordinates.
    /// This is picture composition, never a world-loading or travel decision.</summary>
    public static class ShotProjection
    {
        public readonly struct Result
        {
            public readonly double X, Y, VisibleFraction, RelativeHeight;
            public readonly bool InFront, CenterInFrame;
            internal Result(double x, double y, double fraction, double height, bool front, bool center)
            { X = x; Y = y; VisibleFraction = fraction; RelativeHeight = height; InFront = front; CenterInFrame = center; }
        }

        /// <summary>Projects a small subject bound. Partial edge coverage is a
        /// conservative rectangular approximation; it is not a rendered-pixel test.</summary>
        public static Result Measure(double x, double y, double z, double radius,
            double verticalFov, double aspect, double nearClip)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(radius) || radius < 0 ||
                !Finite(verticalFov) || verticalFov < 1 || verticalFov > 170 ||
                !Finite(aspect) || aspect <= 0 || !Finite(nearClip) || nearClip <= 0 || z <= nearClip)
                return default;
            double halfHeight = z * Math.Tan(verticalFov * Math.PI / 360);
            double halfWidth = halfHeight * aspect;
            if (!Finite(halfHeight) || !Finite(halfWidth) || halfHeight <= 0 || halfWidth <= 0) return default;
            double u = .5 + x / (2 * halfWidth), v = .5 + y / (2 * halfHeight);
            double extentX = radius / (2 * halfWidth), extentY = radius / (2 * halfHeight);
            if (!Finite(u) || !Finite(v) || !Finite(extentX) || !Finite(extentY)) return default;
            bool center = u >= 0 && u <= 1 && v >= 0 && v <= 1;
            if (radius == 0) return new Result(u, v, center ? 1 : 0, 0, true, center);
            if (extentX <= 0 || extentY <= 0) return default;
            double width = Math.Max(0, Math.Min(1, u + extentX) - Math.Max(0, u - extentX));
            double height = Math.Max(0, Math.Min(1, v + extentY) - Math.Max(0, v - extentY));
            double fraction = width / (2 * extentX) * (height / (2 * extentY));
            return new Result(u, v, Math.Max(0, Math.Min(1, fraction)), 2 * extentY, true, center);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
