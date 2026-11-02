using System;

namespace ValheimBroadcaster.Core
{
    public enum ScenicShotStyle { Lateral, Arc, Lift, Reveal, Approach }

    public readonly struct ScenicShotSample
    {
        public readonly AutonomyPosition CameraPosition, AimPosition;
        public readonly float ScreenX, ScreenY;
        public readonly string Phase;
        internal ScenicShotSample(AutonomyPosition camera, AutonomyPosition aim, float x, float y, string phase)
        { CameraPosition = camera; AimPosition = aim; ScreenX = x; ScreenY = y; Phase = phase; }
    }

    /// <summary>
    /// An environmental shot, not a synthetic actor. The scene adapter supplies
    /// measured world-space aim endpoints and initial camera offset, then checks
    /// the resulting complete path for scene support, clearance and composition.
    /// Progress comes from usable coverage time; sampling owns no clock/history.
    /// </summary>
    public sealed class ScenicShotPlan
    {
        public long Id { get; }
        public ScenicShotStyle Style { get; }
        private readonly AutonomyPosition start, end, offset;
        private readonly float screenX, screenY;

        public ScenicShotPlan(long id, ScenicShotStyle style, AutonomyPosition aimStart,
            AutonomyPosition aimEnd, AutonomyPosition cameraOffset, float screenX = 0, float screenY = 0.08f)
        {
            if (!Enum.IsDefined(typeof(ScenicShotStyle), style)) throw new ArgumentOutOfRangeException(nameof(style));
            if (aimStart.Distance(aimEnd) > 32) throw new ArgumentOutOfRangeException(nameof(aimEnd));
            double reach = cameraOffset.Distance(default);
            if (reach < 2 || reach > 150) throw new ArgumentOutOfRangeException(nameof(cameraOffset));
            if (!Finite(screenX) || !Finite(screenY) || Math.Abs(screenX) > 0.2f || Math.Abs(screenY) > 0.2f)
                throw new ArgumentOutOfRangeException(nameof(screenX));
            Id = id; Style = style; start = aimStart; end = aimEnd; offset = cameraOffset;
            this.screenX = screenX; this.screenY = screenY;
        }

        /// <summary>Bounded non-looping movement starts/ends at rest. A paused
        /// caller samples the same progress; previews never advance state.
        /// Strength zero and dynamic=false retain the initial world viewpoint.</summary>
        public ScenicShotSample Sample(double progress, float strength = 1, bool dynamic = true)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress) || !Finite(strength) || strength < 0)
                throw new ArgumentOutOfRangeException(nameof(progress));
            double t = Math.Max(0, Math.Min(1, progress));
            double ease = t * t * t * (t * (t * 6 - 15) + 10);
            double move = dynamic ? ease * Math.Min(1.5, strength) : 0;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            double x = offset.X, y = offset.Y, z = offset.Z;
            double bodyTravel, aimTravel;
            switch (Style)
            {
                case ScenicShotStyle.Lateral:
                    bodyTravel = move; aimTravel = move * 0.25; break;
                case ScenicShotStyle.Arc:
                    // The same environmental path can support either direction;
                    // the immutable ID picks a stable side without a runtime loop.
                    double angle = ((Id & 1) == 0 ? 1 : -1) * move * Math.PI / 10;
                    x = offset.X * Math.Cos(angle) + offset.Z * Math.Sin(angle);
                    z = offset.Z * Math.Cos(angle) - offset.X * Math.Sin(angle);
                    bodyTravel = move * 0.25; aimTravel = move * 0.15; break;
                case ScenicShotStyle.Lift:
                    y += 4 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.25; break;
                case ScenicShotStyle.Reveal:
                    x *= 1 + 0.14 * move; z *= 1 + 0.14 * move;
                    y += 2.5 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.4; break;
                default: // Approach changes scale without pinning the aim to the moving camera.
                    x *= 1 - 0.15 * move; y *= 1 - 0.15 * move; z *= 1 - 0.15 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.15; break;
            }
            AutonomyPosition camera = Position(start.X + x + dx * bodyTravel,
                start.Y + y + dy * bodyTravel, start.Z + z + dz * bodyTravel);
            AutonomyPosition aim = Position(start.X + dx * aimTravel, start.Y + dy * aimTravel,
                start.Z + dz * aimTravel);
            string phase = !dynamic || strength == 0 ? "Holding environmental composition" :
                t < 0.2 ? "Establishing" : t < 0.8 ? "Developing" : "Settling";
            return new ScenicShotSample(camera, aim, screenX, screenY, phase);
        }

        private static AutonomyPosition Position(double x, double y, double z) =>
            new AutonomyPosition((float)Math.Max(-20000, Math.Min(20000, x)),
                (float)Math.Max(-20000, Math.Min(20000, y)), (float)Math.Max(-20000, Math.Min(20000, z)));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
