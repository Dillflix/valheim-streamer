using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Which thirds line the player's own composition trails toward. The
    /// character sits on the thirds line BEHIND its screen-space lateral motion so
    /// the empty two-thirds ahead reads as lead room. Left is the operator's
    /// standing default.</summary>
    public enum SoloCompositionSide { Left, Right }

    /// <summary>One observation of the screen-space composition layer. PlayerScreenX
    /// is the solo thirds target the player should trail toward (-1/6 LEFT or +1/6
    /// RIGHT); LeadOffsetX is the small shared lateral bias (in screen fractions,
    /// [-0.05, +0.05]) applied to BOTH the player and the enemy thirds placement in
    /// combat, pushed toward the side AWAY from the sustained lateral motion so the
    /// frame opens ahead of the action; Side is the resolved solo side; Reason is a
    /// short diagnostic token.</summary>
    public readonly struct ScreenCompositionDecision
    {
        /// <summary>The solo thirds target for the player, screen-X fraction: -1/6
        /// when the player rides the LEFT thirds, +1/6 when RIGHT.</summary>
        public readonly float PlayerScreenX;
        /// <summary>The shared combat lead-room bias, screen-X fraction in
        /// [-0.05, +0.05]. Positive shifts the whole composition right; negative
        /// left. Zero when stationary or oscillating.</summary>
        public readonly float LeadOffsetX;
        /// <summary>The resolved solo side this observation holds.</summary>
        public readonly SoloCompositionSide Side;
        /// <summary>Short diagnostic token describing the decision.</summary>
        public readonly string Reason;
        internal ScreenCompositionDecision(float playerScreenX, float leadOffsetX,
            SoloCompositionSide side, string reason)
        { PlayerScreenX = playerScreenX; LeadOffsetX = leadOffsetX; Side = side; Reason = reason; }
    }

    /// <summary>
    /// The screen-space composition layer, pure and explicit-clock (house style:
    /// sealed, double clock, ArgumentOutOfRangeException contracts, zero per-frame
    /// allocations). It owns two coupled outputs, both driven by the player's
    /// sustained lateral screen-space motion:
    ///
    /// - The SOLO SIDE the player trails toward (lead room): the character sits on
    ///   the thirds line behind its lateral motion, leaving the empty two-thirds
    ///   ahead. The side only flips when sustained opposite lateral motion holds
    ///   past <see cref="SustainSeconds"/>; brief spurts and oscillation keep the
    ///   current side. The initial/default side is LEFT (the operator's standing
    ///   preference). The adapter eases the actual screen-X slide; this only decides
    ///   the target rail.
    /// - The COMBAT LEAD OFFSET (shared, small): a lateral bias toward the side AWAY
    ///   from the sustained motion, clamped to [-0.05, +0.05], zero when stationary.
    ///   The adapter eases it and adds it to both the player and enemy thirds
    ///   placement in combat only.
    ///
    /// The same sustain machinery drives both: a direction is only "sustained" once
    /// the lateral velocity has held one sign past the motion threshold for
    /// <see cref="SustainSeconds"/>. A backwards clock is a world reload and resets
    /// the machine. The adapter computes the lateral velocity (player world velocity
    /// projected onto the presented camera-right axis) and the easing; this owns the
    /// hysteresis and the targets only.
    /// </summary>
    public sealed class ScreenCompositionPolicy
    {
        /// <summary>The player thirds line is 1/6 off centre.</summary>
        public const float ThirdsScreenX = 1f / 6f;
        /// <summary>Lateral speed (m/s in camera space) below which the player reads
        /// as stationary: no side pressure, lead offset decays to zero.</summary>
        public const float MotionThresholdMetresPerSecond = 1.2f;
        /// <summary>Sustained opposite lateral motion must hold this long before the
        /// solo side flips (and before the lead offset reaches full magnitude).</summary>
        public const double SustainSeconds = 2.0;
        /// <summary>Full magnitude of the combat lead offset (screen fraction) once
        /// the motion direction is sustained.</summary>
        public const float LeadOffsetMagnitude = 0.05f;
        /// <summary>The look-target aim must be off the camera forward by at least
        /// this horizontal angle to open LOOK ROOM toward it. Below this the motion
        /// trailing rule stays in charge.</summary>
        public const float LookRoomThresholdDegrees = 10f;

        private double lastNow = -1;
        private SoloCompositionSide side = SoloCompositionSide.Left; // Operator's standing default.
        // The direction currently being sustained toward a flip (+1 right, -1 left,
        // 0 none) and the clock origin of that unbroken run. A run is only toward
        // the side OPPOSITE the current one; motion confirming the current side
        // clears the pending run.
        private int pendingDirection;
        private double pendingSince = -1;
        // The lead direction (away from sustained motion) and the clock origin of the
        // current sustained-motion run (either sign). The lead offset ramps 0..1 over
        // SustainSeconds while a single motion sign holds past the threshold.
        private int motionDirection;
        private double motionSince = -1;

        /// <summary>The solo side currently held.</summary>
        public SoloCompositionSide Side => side;

        /// <summary>
        /// Observe one frame. <paramref name="now"/> is a monotonic clock (a rollback
        /// resets the machine). <paramref name="lateralScreenVelocity"/> is the
        /// player's lateral velocity in camera space (m/s, +right / -left), smoothed
        /// by the adapter. <paramref name="lookOffsetDegrees"/> is the signed
        /// horizontal angle of the look-target aim relative to the CAMERA forward
        /// (+right / -left; caller passes DeltaAngle(cameraAzimuth, aimAzimuth), 0
        /// when the aim is invalid). Returns the solo thirds target, the shared combat
        /// lead offset, the resolved side and a reason.
        /// </summary>
        public ScreenCompositionDecision Observe(double now, float lateralScreenVelocity, float lookOffsetDegrees = 0f)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!Finite(lateralScreenVelocity)) throw new ArgumentOutOfRangeException(nameof(lateralScreenVelocity));
            if (!Finite(lookOffsetDegrees)) throw new ArgumentOutOfRangeException(nameof(lookOffsetDegrees));
            if (lastNow >= 0 && now < lastNow) Reset();
            lastNow = now;

            int sign = lateralScreenVelocity > MotionThresholdMetresPerSecond ? +1
                     : lateralScreenVelocity < -MotionThresholdMetresPerSecond ? -1 : 0;

            // --- Solo side arbiter (LOOK ROOM and trailing lead room) ---
            // One sustain machine feeds from two wants; LOOK ROOM wins conflicts.
            //   Trailing motion: the player trails its motion, so moving RIGHT (+1)
            //   wants the player on the LEFT thirds (room opens right); moving LEFT
            //   wants RIGHT. want(motion) = +1(right motion) -> Left, -1 -> Right.
            //   Look room: the player gives room TOWARD the look target, so the player
            //   sits OPPOSITE the look direction - looking RIGHT (+off) wants the
            //   player on the LEFT thirds, looking LEFT wants RIGHT. Same mapping, so
            //   look reuses the sign->side rule below with a +1/-1 look sign.
            // Look OUTRANKS motion: when the look is off past the threshold it is the
            // sole want; motion fills in only when the look is centred/invalid.
            int lookSign = lookOffsetDegrees > LookRoomThresholdDegrees ? +1
                         : lookOffsetDegrees < -LookRoomThresholdDegrees ? -1 : 0;
            int wantSign = lookSign != 0 ? lookSign : sign;
            bool lookDriven = lookSign != 0;

            // want(s): s > 0 -> player LEFT (room/look opens right), s < 0 -> RIGHT.
            SoloCompositionSide wantSide = wantSign > 0 ? SoloCompositionSide.Left
                                         : wantSign < 0 ? SoloCompositionSide.Right
                                         : side;
            if (wantSign == 0 || wantSide == side)
            {
                // Stationary/centred, or the want confirms the current side: no flip.
                pendingDirection = 0; pendingSince = -1;
            }
            else
            {
                // The want opposes the current side. Start or continue the sustain run.
                // A change of want sign restarts the run (a look want replacing a
                // motion want of the other sign re-arms the same single timer).
                if (pendingDirection != wantSign) { pendingDirection = wantSign; pendingSince = now; }
                if (now - pendingSince >= SustainSeconds)
                {
                    side = wantSide;
                    pendingDirection = 0; pendingSince = -1;
                }
            }

            // --- Combat lead offset (shared bias away from sustained motion) ---
            // The lead opens the frame AHEAD of the motion: moving RIGHT biases the
            // whole composition LEFT (negative), and vice versa. Ramp 0..1 over
            // SustainSeconds while one sign holds; zero when stationary or the sign
            // flips (the run restarts).
            float leadOffset;
            if (sign == 0)
            {
                motionDirection = 0; motionSince = -1;
                leadOffset = 0f;
            }
            else
            {
                if (motionDirection != sign) { motionDirection = sign; motionSince = now; }
                double t = (now - motionSince) / SustainSeconds;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                // Lead is AWAY from the motion: motion +1 (right) -> offset negative.
                leadOffset = (float)(-sign * LeadOffsetMagnitude * t);
            }

            float playerScreenX = side == SoloCompositionSide.Left ? -ThirdsScreenX : ThirdsScreenX;
            string reason = pendingSince >= 0 ? (lookDriven ? "confirming look room" : "confirming side flip")
                          : lookDriven ? "look room"
                          : sign == 0 ? "stationary"
                          : "leading motion";
            return new ScreenCompositionDecision(playerScreenX, leadOffset, side, reason);
        }

        /// <summary>Reset to the default LEFT side, no pending flip, zero lead. Called
        /// on a clock rollback and by the adapter on Reset/rebind/warp.</summary>
        public void Reset()
        {
            lastNow = -1;
            side = SoloCompositionSide.Left;
            pendingDirection = 0; pendingSince = -1;
            motionDirection = 0; motionSince = -1;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
