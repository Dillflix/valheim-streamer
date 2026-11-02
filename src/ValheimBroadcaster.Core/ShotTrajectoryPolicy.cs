using System;

namespace ValheimBroadcaster.Core
{
    public enum ShotTrajectoryKind { Establish, Arc, Dolly, Tracking, ActivityArea }

    public readonly struct ShotTrajectoryState
    {
        public readonly AutonomyPosition Anchor, Velocity;
        public readonly ShotTrajectoryKind Mode;
        public readonly bool WasReset;
        public readonly float Lag;
        internal ShotTrajectoryState(AutonomyPosition anchor, AutonomyPosition velocity,
            ShotTrajectoryKind mode, bool reset, float lag)
        { Anchor = anchor; Velocity = velocity; Mode = mode; WasReset = reset; Lag = lag; }
    }

    /// <summary>
    /// An independent world-space body target, separate from camera aiming.
    /// Establish/activity shots retain their world origin, arc/dolly bodies can
    /// drift slowly with travelling subjects, and tracking shots use common
    /// scene velocity. Continuous framing/range feedback prevents divergence.
    /// Cinemachine still supplies the actual orbital/dolly camera development;
    /// this policy never creates a second decorative camera path.
    /// </summary>
    public sealed class ShotTrajectoryPolicy
    {
        public const double MaximumAcceleration = 24, MaximumSpeed = 30;
        public const double MaximumGapSeconds = 1, DiscontinuityDistance = 16;
        private Dynamics state;
        private Vector previousSubject, center, sceneVelocity;
        private double lastNow = -1, lagBudget = 8, pressure, freedom = 1;
        private long shotId;
        private CinematicActivity activity;
        private ShotTrajectoryKind mode;
        private bool initialized, blocked;

        private struct Dynamics
        {
            internal Vector Anchor, Velocity, Origin;
        }

        // The caller already uses AutonomyPosition for finite, world-bounded
        // coordinates. Internal double arithmetic avoids frame-rate drift.
        private readonly struct Vector
        {
            internal readonly double X, Y, Z;
            internal Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal Vector(AutonomyPosition value) : this(value.X, value.Y, value.Z) { }
            internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            internal AutonomyPosition Public => new AutonomyPosition((float)X, (float)Y, (float)Z);
            public static Vector operator +(Vector a, Vector b) => new Vector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector operator -(Vector a, Vector b) => new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector operator *(Vector a, double b) => new Vector(a.X * b, a.Y * b, a.Z * b);
            internal Vector Limited(double maximum)
            { double length = Length; return length > maximum ? this * (maximum / length) : this; }
        }

        /// <summary>
        /// Call at the candidate's actual update cadence. Monotonic now accounts
        /// for standby intervals; dt=0 explicitly pauses movement. subject is used
        /// only to detect discontinuity, so a group-center change cannot teleport
        /// the body. sceneVelocity must represent the selected subject/group.
        /// </summary>
        public ShotTrajectoryState Step(double now, double dt, AutonomyPosition subject,
            AutonomyPosition sceneCenter, AutonomyPosition commonVelocity, CinematicActivity currentActivity,
            ShotTrajectoryKind kind, long currentShotId, float allowableLag, float framingPressure,
            float availableFreedom, bool blocked = false)
        {
            if (!Finite(now) || now < 0 || !Finite(dt) || dt < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (!Finite(allowableLag) || allowableLag <= 0 || !Unit(framingPressure) || !Unit(availableFreedom))
                throw new ArgumentOutOfRangeException(nameof(allowableLag));
            if ((int)kind < 0 || (int)kind > (int)ShotTrajectoryKind.ActivityArea ||
                (int)currentActivity < 0 || (int)currentActivity > (int)CinematicActivity.Work)
                throw new ArgumentOutOfRangeException(nameof(kind));

            Vector subjectPoint = new Vector(subject);
            bool reset = !initialized || now - lastNow > MaximumGapSeconds ||
                (subjectPoint - previousSubject).Length > DiscontinuityDistance;
            double elapsed = initialized && dt > 0 ? now - lastNow : 0;
            center = new Vector(sceneCenter);
            sceneVelocity = new Vector(commonVelocity).Limited(20);
            lagBudget = Math.Max(0.5, Math.Min(24, allowableLag));
            pressure = framingPressure; freedom = availableFreedom; this.blocked = blocked;
            ShotTrajectoryKind nextMode = currentActivity == CinematicActivity.Combat ? ShotTrajectoryKind.Tracking : kind;
            if (reset)
            {
                state = new Dynamics { Anchor = center, Origin = center };
                initialized = true;
            }
            else if (shotId != currentShotId || mode != nextMode || activity != currentActivity)
            {
                // A new shot gets a world origin at the existing body position.
                // It does not recenter, discard velocity, or copy actor movement.
                state.Origin = state.Anchor;
            }
            mode = nextMode; activity = currentActivity; shotId = currentShotId;
            previousSubject = subjectPoint; lastNow = now;
            if (blocked) state.Velocity = default;
            if (!reset && elapsed > 0)
                Integrate(ref state, center, sceneVelocity, Math.Min(elapsed, MaximumGapSeconds), false);
            return Result(state, center, reset);
        }

        /// <summary>Read-only short-horizon prediction for trajectory/foliage
        /// validation. Input motion is extrapolated only for this preview.</summary>
        public ShotTrajectoryState Preview(double seconds)
        {
            if (!Finite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            Dynamics predicted = state;
            double horizon = Math.Min(seconds, 0.75);
            if (initialized) Integrate(ref predicted, center, sceneVelocity, horizon, true);
            return Result(predicted, center + sceneVelocity * horizon, false);
        }

        /// <summary>Synchronize after the adapter rejects a body path. A physical
        /// constraint may stop velocity immediately; ordinary motion never does.
        /// This prevents invisible policy advance behind a retained camera.</summary>
        public void Synchronize(AutonomyPosition acceptedAnchor, bool blocked)
        {
            state.Anchor = new Vector(acceptedAnchor);
            this.blocked = blocked;
            if (blocked)
            {
                state.Velocity = default;
                state.Origin = state.Anchor;
            }
        }

        public void Reset()
        {
            initialized = blocked = false; state = default;
            previousSubject = center = sceneVelocity = default;
            lastNow = -1; lagBudget = 8; pressure = 0; freedom = 1;
            shotId = 0; activity = CinematicActivity.Idle; mode = ShotTrajectoryKind.Establish;
        }

        private void Integrate(ref Dynamics value, Vector target, Vector targetVelocity,
            double elapsed, bool predictTarget)
        {
            if (blocked || elapsed <= 0) return;
            // Small substeps make sparse standby updates behave like live camera
            // updates, without accumulating several missed frames into a snap.
            int steps = Math.Max(1, (int)Math.Ceiling(elapsed / 0.02));
            double step = elapsed / steps;
            for (int i = 0; i < steps; i++)
            {
                // Compare target and anchor at the same substep time. Sampling
                // the target at the end but the anchor at the beginning creates
                // an artificial speed*frameDuration offset between camera rates.
                Vector currentTarget = predictTarget ? target + targetVelocity * (i * step) :
                    target - targetVelocity * (elapsed - i * step);
                Advance(ref value, currentTarget, targetVelocity, step);
            }
        }

        private void Advance(ref Dynamics value, Vector target, Vector targetVelocity, double step)
        {
            double speed = targetVelocity.Length;
            // Tracking is a velocity-driven body. Other families retain a world
            // origin and develop only a fraction of coherent travel translation.
            // Orbit/dolly offset development remains the Cinemachine rig's job.
            double drift = mode == ShotTrajectoryKind.Tracking ? 1 :
                activity != CinematicActivity.Travel ? 0 :
                mode == ShotTrajectoryKind.Arc ? 0.35 * freedom :
                mode == ShotTrajectoryKind.Dolly ? 0.2 * freedom : 0;
            Vector plannedVelocity = targetVelocity * drift;
            value.Origin += plannedVelocity * step;
            Vector error = target - value.Anchor;
            double distance = error.Length;
            double ratio = Math.Min(2, distance / lagBudget);
            Vector correction;
            if (mode == ShotTrajectoryKind.Tracking)
                correction = (error * 0.8).Limited(10);
            else
            {
                // No single hold/chase threshold: range pressure grows
                // continuously. Small local movement barely disturbs an
                // establishing/work shot, while sustained movement is covered.
                double correctionSpeed = Math.Min(12, Math.Max(2, speed * (1 - drift) + 2));
                correction = distance > 0.000001 ? error * (correctionSpeed * ratio * ratio * ratio / distance) : default;
                double retention = 1 - Math.Min(1, ratio * ratio);
                correction += ((value.Origin - value.Anchor) * 0.6).Limited(2) * retention;
            }
            if (distance > 0.000001 && pressure > 0)
                correction += error * (pressure * Math.Min(12, 2 + speed * 0.6) /
                    Math.Max(distance, Math.Max(1, lagBudget * 0.25)));
            Vector desiredVelocity = (plannedVelocity + correction).Limited(MaximumSpeed);
            double acceleration = Math.Min(MaximumAcceleration, 8 + speed * 1.5 + pressure * 8);
            Vector nextVelocity = value.Velocity + (desiredVelocity - value.Velocity).Limited(acceleration * step);
            Vector next = value.Anchor + (value.Velocity + nextVelocity) * (0.5 * step);
            // Respect the same finite world-coordinate domain as inputs.
            value.Anchor = new Vector(ClampCoordinate(next.X), ClampCoordinate(next.Y), ClampCoordinate(next.Z));
            value.Velocity = new Vector(next.X == value.Anchor.X ? nextVelocity.X : 0,
                next.Y == value.Anchor.Y ? nextVelocity.Y : 0, next.Z == value.Anchor.Z ? nextVelocity.Z : 0);
        }

        private ShotTrajectoryState Result(Dynamics value, Vector target, bool reset) =>
            new ShotTrajectoryState(value.Anchor.Public, value.Velocity.Public, mode, reset, (float)(target - value.Anchor).Length);
        private static double ClampCoordinate(double value) => Math.Max(-20000, Math.Min(20000, value));
        private static bool Unit(float value) => Finite(value) && value >= 0 && value <= 1;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
