using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>
    /// Predictive no-escape guardrail: measures how close the main subject is
    /// to leaving the frame, before any recovery threshold fires. Pressure is
    /// 0 while the subject sits comfortably inside the safe inner region and
    /// reaches 1 at the hard band near the frame edge, evaluated both at the
    /// subject's current screen position and at its velocity-projected
    /// position a short lookahead ahead. Coordinates are centred viewport
    /// fractions (0 is screen centre, ±0.5 the edge); velocities are per
    /// second in the same units. The caller uses rising pressure to tighten
    /// aim response and accelerate body pursuit so a walking or sprinting
    /// subject can never outrun the shot; reactive recovery framing remains
    /// the last resort, unchanged.
    /// </summary>
    public static class FrameRetentionPolicy
    {
        /// <summary>Horizontal band: pressure starts when |x| exceeds the
        /// onset and saturates at the limit, still inside the frame edge.</summary>
        public const float PressureOnsetX = 0.28f, PressureLimitX = 0.42f;
        /// <summary>Vertical band, slightly tighter than the horizontal band
        /// to match the rig's narrower vertical dead zone and hard limits.</summary>
        public const float PressureOnsetY = 0.24f, PressureLimitY = 0.36f;
        /// <summary>Seconds of subject motion projected ahead when predicting
        /// an escape; chosen between aim damping (~1s) and frame time.</summary>
        public const float LookaheadSeconds = 0.6f;

        /// <summary>Pressure 0..1 for a single screen position.</summary>
        public static float PointPressure(float screenX, float screenY)
        {
            if (!Finite(screenX) || !Finite(screenY))
                throw new ArgumentException("Frame retention requires finite screen coordinates.");
            float x = Clamp01((Math.Abs(screenX) - PressureOnsetX) / (PressureLimitX - PressureOnsetX));
            float y = Clamp01((Math.Abs(screenY) - PressureOnsetY) / (PressureLimitY - PressureOnsetY));
            return Math.Max(x, y);
        }

        /// <summary>Pressure 0..1 from the current screen position and the
        /// position projected <see cref="LookaheadSeconds"/> ahead along the
        /// observed screen-space velocity. Motion toward centre never reduces
        /// the pressure already due at the current position.</summary>
        public static float EdgePressure(float screenX, float screenY, float velocityX, float velocityY)
        {
            if (!Finite(velocityX) || !Finite(velocityY))
                throw new ArgumentException("Frame retention requires finite screen velocities.");
            float current = PointPressure(screenX, screenY);
            float projected = PointPressure(screenX + velocityX * LookaheadSeconds,
                screenY + velocityY * LookaheadSeconds);
            return Math.Max(current, projected);
        }

        private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
