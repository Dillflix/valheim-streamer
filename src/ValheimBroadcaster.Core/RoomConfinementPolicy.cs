using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>A measured, convex camera-space cell, not a semantic room or scene-readiness claim.</summary>
    public readonly struct RoomConfinementVolume
    {
        public readonly AutonomyPosition Center, HalfSize;
        public readonly float Yaw;
        public bool IsValid => Finite(Yaw) && HalfSize.X >= .4f && HalfSize.Y >= .35f && HalfSize.Z >= .4f &&
            HalfSize.X <= 12 && HalfSize.Y <= 8 && HalfSize.Z <= 12;
        public RoomConfinementVolume(AutonomyPosition center, AutonomyPosition halfSize, float yaw)
        {
            if (!Finite(yaw) || halfSize.X < .4f || halfSize.Y < .35f || halfSize.Z < .4f ||
                halfSize.X > 12 || halfSize.Y > 8 || halfSize.Z > 12)
                throw new ArgumentOutOfRangeException(nameof(halfSize));
            Center = center; HalfSize = halfSize;
            Yaw = ((yaw % 360) + 360) % 360;
        }
        public bool Contains(AutonomyPosition point, float inset = 0)
        {
            if (!IsValid || !Finite(inset) || inset < 0) return false;
            double angle = Yaw * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
            double x = point.X - Center.X, y = point.Y - Center.Y, z = point.Z - Center.Z;
            return Math.Abs(x * c - z * s) <= HalfSize.X - inset + .001 &&
                Math.Abs(y) <= HalfSize.Y - inset + .001 && Math.Abs(x * s + z * c) <= HalfSize.Z - inset + .001;
        }
        public bool Similar(RoomConfinementVolume other)
        {
            float yaw = Math.Abs(Yaw - other.Yaw); yaw = Math.Min(yaw, 360 - yaw);
            return Distance(Center, other.Center) <= .35 && Distance(HalfSize, other.HalfSize) <= .35 && yaw <= 2;
        }
        private static double Distance(AutonomyPosition a, AutonomyPosition b) =>
            Math.Sqrt((a.X - b.X) * (double)(a.X - b.X) + (a.Y - b.Y) * (double)(a.Y - b.Y) + (a.Z - b.Z) * (double)(a.Z - b.Z));
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }

    /// <summary>Two fresh compatible surveys acquire a volume. Missing, changed or expired evidence
    /// retires it immediately; an old room must never constrain an actor crossing a doorway.</summary>
    public sealed class RoomConfinementPolicy
    {
        private RoomConfinementVolume? pending;
        private double pendingSince = -1, observedAt = -1, lastNow = -1;
        private long sceneId;
        public bool Active { get; private set; }
        public RoomConfinementVolume Volume { get; private set; }
        public string Reason { get; private set; } = "No verified room volume";

        public void Tick(double now, long scene, AutonomyPosition primary, bool allowed)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (lastNow >= 0 && (now < lastNow || scene != sceneId)) Clear("Scene or clock changed");
            lastNow = now; sceneId = scene;
            if (!allowed) { Clear("Confinement unavailable in this scene or doorway"); return; }
            if (observedAt >= 0 && now - observedAt > .75) { Clear("Room-volume evidence expired"); return; }
            if (Active && !Volume.Contains(primary)) Clear("Subject left the verified camera cell");
        }

        public void Observe(double now, long scene, AutonomyPosition primary, RoomConfinementVolume? candidate)
        {
            Tick(now, scene, primary, true);
            if (!candidate.HasValue || !candidate.Value.IsValid || !candidate.Value.Contains(primary))
            { Clear("Structural survey incomplete, obstructed or unsupported"); return; }
            var value = candidate.Value;
            if (Active && Volume.Similar(value))
            { Volume = value; observedAt = now; Reason = "Verified interior camera cell"; return; }
            Active = false;
            if (!pending.HasValue || !pending.Value.Similar(value))
            { pending = value; pendingSince = now; }
            observedAt = now;
            if (now - pendingSince >= .35)
            { Volume = value; Active = true; Reason = "Verified interior camera cell"; }
            else Reason = "Confirming measured room boundaries";
        }

        public bool Contains(AutonomyPosition position) => !Active || Volume.Contains(position);
        // A box is convex: endpoints inside prove the straight segment remains inside.
        public bool ValidateSegment(AutonomyPosition from, AutonomyPosition to) => Contains(from) && Contains(to);
        public void Clear(string reason = "No verified room volume")
        { Active = false; pending = null; pendingSince = observedAt = -1; Reason = reason; }
        public void Reset()
        { Clear(); lastNow = -1; sceneId = 0; Volume = default; }
    }
}
