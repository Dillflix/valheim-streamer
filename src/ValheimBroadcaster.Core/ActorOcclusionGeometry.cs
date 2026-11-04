using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>A measured body capsule, not an actor's renderer bounding box.
    /// Endpoint/radius values describe this observation only.</summary>
    public readonly struct ActorBodyCapsule
    {
        public readonly int ActorId;
        public readonly AutonomyPosition A, B;
        public readonly double Radius;
        public ActorBodyCapsule(int actorId, AutonomyPosition a, AutonomyPosition b, double radius)
        { ActorId = actorId; A = a; B = b; Radius = radius; }
    }

    public readonly struct ActorVisibilitySegment
    {
        internal readonly AutonomyPosition Start, End;
        internal readonly int SubjectId;
        internal readonly bool Valid;
        internal ActorVisibilitySegment(AutonomyPosition start, AutonomyPosition end, int subjectId)
        { Start = start; End = end; SubjectId = subjectId; Valid = true; }
    }

    /// <summary>Finite segment/capsule visibility only. It grants no physical
    /// clearance, simulation authority or scene readiness. Invalid/unidentified
    /// bodies cannot manufacture an occlusion observation.</summary>
    public static class ActorOcclusionGeometry
    {
        public static bool Occludes(AutonomyPosition camera, AutonomyPosition target, int subjectId, ActorBodyCapsule body)
            => Occludes(Prepare(camera, target, subjectId), body);

        /// <summary>Normalize and shorten the sample once, then test every
        /// nearby body without repeating the sample's square root.</summary>
        public static ActorVisibilitySegment Prepare(AutonomyPosition camera, AutonomyPosition target, int subjectId)
        {
            if (subjectId == 0 || !Finite(camera) || !Finite(target)) return default;
            double dx = target.X - camera.X, dy = target.Y - camera.Y, dz = target.Z - camera.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!Finite(length) || length <= .05) return default;
            // Do not count an intersection beyond, or just touching, the sampled
            // surface. Self is excluded by identity rather than by proximity.
            double scale = (length - .05) / length;
            var end = new AutonomyPosition((float)(camera.X + dx * scale), (float)(camera.Y + dy * scale), (float)(camera.Z + dz * scale));
            return new ActorVisibilitySegment(camera, end, subjectId);
        }

        public static bool Occludes(ActorVisibilitySegment sample, ActorBodyCapsule body)
        {
            if (!sample.Valid || body.ActorId == 0 || sample.SubjectId == body.ActorId ||
                !Finite(body.A) || !Finite(body.B) || !Finite(body.Radius) || body.Radius <= 0 || body.Radius > 32) return false;
            AutonomyPosition camera = sample.Start, end = sample.End;
            double radius = body.Radius;
            if (Math.Max(camera.X, end.X) < Math.Min(body.A.X, body.B.X) - radius ||
                Math.Min(camera.X, end.X) > Math.Max(body.A.X, body.B.X) + radius ||
                Math.Max(camera.Y, end.Y) < Math.Min(body.A.Y, body.B.Y) - radius ||
                Math.Min(camera.Y, end.Y) > Math.Max(body.A.Y, body.B.Y) + radius ||
                Math.Max(camera.Z, end.Z) < Math.Min(body.A.Z, body.B.Z) - radius ||
                Math.Min(camera.Z, end.Z) > Math.Max(body.A.Z, body.B.Z) + radius) return false;
            return SegmentDistanceSquared(camera, end, body.A, body.B) < radius * radius;
        }

        // Closest points of two finite segments, including parallel and
        // degenerate (sphere) capsules. Parameters always remain in [0,1].
        private static double SegmentDistanceSquared(AutonomyPosition p, AutonomyPosition q, AutonomyPosition a, AutonomyPosition b)
        {
            double ux = q.X - p.X, uy = q.Y - p.Y, uz = q.Z - p.Z;
            double vx = b.X - a.X, vy = b.Y - a.Y, vz = b.Z - a.Z;
            double wx = p.X - a.X, wy = p.Y - a.Y, wz = p.Z - a.Z;
            double uu = ux * ux + uy * uy + uz * uz, vv = vx * vx + vy * vy + vz * vz;
            double uv = ux * vx + uy * vy + uz * vz, uw = ux * wx + uy * wy + uz * wz;
            double vw = vx * wx + vy * wy + vz * wz;
            double s, t;
            if (uu <= 1e-12) { s = 0; t = vv <= 1e-12 ? 0 : Clamp(vw / vv); }
            else if (vv <= 1e-12) { t = 0; s = Clamp(-uw / uu); }
            else
            {
                double determinant = uu * vv - uv * uv;
                s = determinant > 1e-12 * uu * vv ? Clamp((uv * vw - uw * vv) / determinant) : 0;
                t = (uv * s + vw) / vv;
                if (t < 0) { t = 0; s = Clamp(-uw / uu); }
                else if (t > 1) { t = 1; s = Clamp((uv - uw) / uu); }
            }
            double x = wx + s * ux - t * vx, y = wy + s * uy - t * vy, z = wz + s * uz - t * vz;
            return x * x + y * y + z * z;
        }

        private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(AutonomyPosition value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
