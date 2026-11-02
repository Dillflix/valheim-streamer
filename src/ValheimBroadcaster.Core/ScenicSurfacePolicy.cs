using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Surface clearance for the actual scenic camera body and near
    /// plane. The adapter supplies measurements from loaded terrain and water;
    /// a loaded tile or an empty overlap query is not a height measurement.</summary>
    public static class ScenicSurfacePolicy
    {
        public const int SampleCount = 6;
        public const float BodyGroundClearance = 2f, NearGroundClearance = .5f;
        public const float BodyWaterClearance = 2f, NearWaterClearance = 1f;

        public enum SurfaceConstraint { None, Invalid, UnknownGround, UnknownWater, Ground, Water }

        public readonly struct Result
        {
            public readonly bool Known;
            public readonly float RequiredLift;
            public readonly SurfaceConstraint Constraint;
            public bool Clear => Known && RequiredLift <= 0;
            internal Result(bool known, float requiredLift, SurfaceConstraint constraint)
            { Known = known; RequiredLift = requiredLift; Constraint = constraint; }
        }

        /// <summary>Body, near-plane centre and four corners in world space.
        /// Axes come from the final rendered orientation, not target direction.
        /// The output buffer is caller-owned so live checks need no allocation.</summary>
        public static bool TryFootprint(AutonomyPosition position, AutonomyPosition forward,
            AutonomyPosition right, AutonomyPosition up, float fieldOfView, float aspect,
            float nearClip, AutonomyPosition[] output)
        {
            if (output == null || output.Length < SampleCount || !Finite(fieldOfView) ||
                fieldOfView < 1 || fieldOfView > 170 || !Finite(aspect) || aspect <= 0 ||
                !Finite(nearClip) || nearClip <= 0 || !Unit(forward) || !Unit(right) || !Unit(up) ||
                Math.Abs(Dot(forward, right)) > .02 || Math.Abs(Dot(forward, up)) > .02 ||
                Math.Abs(Dot(right, up)) > .02) return false;
            double height = nearClip * Math.Tan(fieldOfView * Math.PI / 360), width = height * aspect;
            if (!Finite(height) || !Finite(width)) return false;
            output[0] = position;
            if (!Point(position, forward, right, up, nearClip, 0, 0, out output[1])) return false;
            int index = 2;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                if (!Point(position, forward, right, up, nearClip, width * x, height * y, out output[index++]))
                    return false;
            return true;
        }

        /// <summary>Measure one actual footprint point. Unknown height always
        /// remains unknown; it is never substituted with zero or a feature's Y.</summary>
        public static Result EvaluateSample(float pointY, bool groundKnown, float groundHeight,
            bool waterPresent, float waterHeight, bool body)
        {
            if (!Finite(pointY)) return new Result(false, 0, SurfaceConstraint.Invalid);
            if (!groundKnown || !Finite(groundHeight)) return new Result(false, 0, SurfaceConstraint.UnknownGround);
            if (waterPresent && !Finite(waterHeight)) return new Result(false, 0, SurfaceConstraint.UnknownWater);
            double ground = groundHeight + (body ? (double)BodyGroundClearance : NearGroundClearance) - pointY;
            double water = waterPresent
                ? waterHeight + (body ? (double)BodyWaterClearance : NearWaterClearance) - pointY
                : double.NegativeInfinity;
            double lift = Math.Max(0, Math.Max(ground, water));
            if (!Finite(lift) || lift > float.MaxValue) return new Result(false, 0, SurfaceConstraint.Invalid);
            return new Result(true, (float)lift, lift <= 0 ? SurfaceConstraint.None :
                water > ground ? SurfaceConstraint.Water : SurfaceConstraint.Ground);
        }

        private static bool Point(AutonomyPosition position, AutonomyPosition forward, AutonomyPosition right,
            AutonomyPosition up, double depth, double x, double y, out AutonomyPosition point)
        {
            double px = position.X + forward.X * depth + right.X * x + up.X * y;
            double py = position.Y + forward.Y * depth + right.Y * x + up.Y * y;
            double pz = position.Z + forward.Z * depth + right.Z * x + up.Z * y;
            point = default;
            if (!Finite(px) || !Finite(py) || !Finite(pz) || Math.Abs(px) > 20000 || Math.Abs(py) > 20000 || Math.Abs(pz) > 20000)
                return false;
            point = new AutonomyPosition((float)px, (float)py, (float)pz); return true;
        }
        private static bool Unit(AutonomyPosition p) => Math.Abs(Dot(p, p) - 1) <= .02;
        private static double Dot(AutonomyPosition a, AutonomyPosition b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
