using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Perspective framing measurements in camera-local coordinates.
    /// This is picture composition, never a world-loading or travel decision.</summary>
    public static class ShotProjection
    {
        public readonly struct Point
        {
            public readonly double X, Y, Z;
            public Point(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        /// <summary>The projected full-body envelope. Safe-frame/headroom and
        /// readability measure composition, not physical clearance or scene readiness.</summary>
        public readonly struct Envelope
        {
            public readonly double MinX, MinY, MaxX, MaxY, VisibleFraction, SafeFraction;
            public readonly double Readability, Headroom, HeadroomScore, Quality;
            public readonly bool InFront;
            public double X => (MinX + MaxX) * .5;
            public double Y => (MinY + MaxY) * .5;
            public double RelativeHeight => MaxY - MinY;
            public bool FullyInFrame => InFront && VisibleFraction >= .999;
            internal Envelope(double minX, double minY, double maxX, double maxY,
                double visible, double safe, double readability, double headroomScore)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
                VisibleFraction = visible; SafeFraction = safe; Readability = readability;
                Headroom = 1 - maxY; HeadroomScore = headroomScore; InFront = true;
                // No preference for the image center: every suitably sized body
                // wholly within the safe frame receives the same composition score.
                Quality = visible * (.5 + .5 * safe) * (.6 + .4 * headroomScore) * readability;
            }

            /// <summary>Optional error against an explicit composition guide.
            /// Its tolerance preserves freedom within the frame rather than hard-locking a point.</summary>
            public double PlacementError(double desiredX, double desiredY, double toleranceX, double toleranceY)
            {
                if (!InFront || !Finite(desiredX) || !Finite(desiredY) || !Finite(toleranceX) ||
                    !Finite(toleranceY) || toleranceX < 0 || toleranceY < 0) return 1;
                double x = Math.Max(0, Math.Abs(X - desiredX) - toleranceX);
                double y = Math.Max(0, Math.Abs(Y - desiredY) - toleranceY);
                return Math.Min(1, Math.Sqrt(x * x + y * y));
            }
        }

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

        /// <summary>Project eight camera-local corners of a standing/crouching
        /// actor envelope, including its feet and head. A body crossing the near
        /// plane is unsuitable framing even when the camera itself is physically clear.</summary>
        public static Envelope MeasureEnvelope(IReadOnlyList<Point> corners, double verticalFov, double aspect,
            double nearClip, double minimumReadableHeight = .05, double safeMargin = .05, double minimumHeadroom = .035)
        {
            if (corners == null || corners.Count != 8 || !Finite(minimumReadableHeight) || minimumReadableHeight <= 0 ||
                !Finite(safeMargin) || safeMargin < 0 || safeMargin >= .5 ||
                !Finite(minimumHeadroom) || minimumHeadroom <= 0 || minimumHeadroom >= .5) return default;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int i = 0; i < corners.Count; i++)
            {
                Point corner = corners[i];
                Result projected = Measure(corner.X, corner.Y, corner.Z, 0, verticalFov, aspect, nearClip);
                if (!projected.InFront) return default;
                minX = Math.Min(minX, projected.X); minY = Math.Min(minY, projected.Y);
                maxX = Math.Max(maxX, projected.X); maxY = Math.Max(maxY, projected.Y);
            }
            double width = maxX - minX, height = maxY - minY;
            if (!Finite(width) || !Finite(height) || width <= 0 || height <= 0) return default;
            double visible = Coverage(minX, minY, maxX, maxY, 0);
            double safe = Coverage(minX, minY, maxX, maxY, safeMargin);
            double readability = Math.Min(1, height / minimumReadableHeight);
            double headroom = Math.Max(0, Math.Min(1, (1 - maxY) / minimumHeadroom));
            return new Envelope(minX, minY, maxX, maxY, visible, safe, readability, headroom);
        }

        private static double Coverage(double minX, double minY, double maxX, double maxY, double margin)
        {
            double width = Math.Max(0, Math.Min(1 - margin, maxX) - Math.Max(margin, minX));
            double height = Math.Max(0, Math.Min(1 - margin, maxY) - Math.Max(margin, minY));
            return Math.Max(0, Math.Min(1, width / (maxX - minX) * (height / (maxY - minY))));
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
