using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Minimal rotation that returns the live subject to the hard
    /// viewport band. Angles are degrees; positive yaw turns the view toward
    /// screen-right and positive pitch toward screen-up (Core Y-up screen
    /// coordinates), so each correction rotates the camera toward the subject.
    /// When the subject is behind the camera (or inside the near plane) no
    /// finite screen correction exists and the presenter must adopt a full
    /// look-at rotation instead.</summary>
    public readonly struct FrameClampCorrection
    {
        public readonly bool Required;
        public readonly bool BehindCamera;
        public readonly float YawDegrees;
        public readonly float PitchDegrees;
        internal FrameClampCorrection(bool required, bool behindCamera, float yawDegrees, float pitchDegrees)
        { Required = required; BehindCamera = behindCamera; YawDegrees = yawDegrees; PitchDegrees = pitchDegrees; }
    }

    /// <summary>
    /// Final unconditional presentation clamp: the live player must be inside
    /// the viewport in every presented frame, whatever pose the rig arrived
    /// at - a fresh solve, a retained hold, a slate fallback, a mid-blend
    /// sample or a distance-invariant override. Given the subject's projected
    /// screen position in centred viewport fractions (0 centre, ±0.5 the frame
    /// edge, Y-up) under the about-to-be-presented lens, it returns the
    /// minimal undamped yaw/pitch that brings the subject back inside a hard
    /// band just inside the composer's native hard limits. Inside the band the
    /// correction is exactly zero, so the clamp never fights the composer in
    /// normal operation; it only ever fires on pose paths that bypassed the
    /// composer. Pure trigonometry, no engine dependencies.
    /// </summary>
    public static class FinalFrameClamp
    {
        /// <summary>Hard band, centred viewport fractions. Chosen just inside
        /// the rig's native composer hard limits (0.55 x 0.50 total size, i.e.
        /// ±0.275/±0.25 around the composed point) scaled to the full frame so
        /// composer-solved poses sit well inside and never trigger the clamp,
        /// while anything outside is still clearly within the visible frame
        /// (±0.5) after correction.</summary>
        public const float HardBandX = 0.42f;
        public const float HardBandY = 0.38f;

        /// <summary>Correction for the default hard band.</summary>
        public static FrameClampCorrection Correct(float screenX, float screenY, bool inFront,
            float verticalFovDegrees, float aspect) =>
            Correct(screenX, screenY, inFront, verticalFovDegrees, aspect, HardBandX, HardBandY);

        /// <summary>Correction against an explicit band. screenX/screenY are
        /// centred viewport fractions of the subject under the presented lens;
        /// inFront is false when the subject projects behind the camera or
        /// inside the near plane, in which case the correction demands a full
        /// look-at rotation. The returned yaw/pitch are the exact angles that
        /// place the subject on the band boundary along each violated axis and
        /// zero along axes already inside the band.</summary>
        public static FrameClampCorrection Correct(float screenX, float screenY, bool inFront,
            float verticalFovDegrees, float aspect, float bandX, float bandY)
        {
            if (!Finite(verticalFovDegrees) || verticalFovDegrees < 1 || verticalFovDegrees > 170)
                throw new ArgumentOutOfRangeException(nameof(verticalFovDegrees));
            if (!Finite(aspect) || aspect <= 0 || aspect > 10)
                throw new ArgumentOutOfRangeException(nameof(aspect));
            if (!Finite(bandX) || bandX <= 0 || bandX >= 0.5f)
                throw new ArgumentOutOfRangeException(nameof(bandX));
            if (!Finite(bandY) || bandY <= 0 || bandY >= 0.5f)
                throw new ArgumentOutOfRangeException(nameof(bandY));
            if (!inFront) return new FrameClampCorrection(true, true, 0, 0);
            if (!Finite(screenX))
                throw new ArgumentOutOfRangeException(nameof(screenX));
            if (!Finite(screenY))
                throw new ArgumentOutOfRangeException(nameof(screenY));
            // A screen fraction x maps to the view-space angle
            // atan(2 x tan(fov/2) extent) where extent is the aspect ratio
            // horizontally and 1 vertically. The minimal correction is the
            // difference between the subject's angle and the band's angle.
            double tanHalf = Math.Tan(verticalFovDegrees * Math.PI / 360);
            float yaw = AxisCorrection(screenX, bandX, tanHalf * aspect);
            float pitch = AxisCorrection(screenY, bandY, tanHalf);
            return yaw == 0 && pitch == 0
                ? default
                : new FrameClampCorrection(true, false, yaw, pitch);
        }

        private static float AxisCorrection(float position, float band, double tanHalfExtent)
        {
            float magnitude = Math.Abs(position);
            if (magnitude <= band) return 0;
            double current = Math.Atan(2 * magnitude * tanHalfExtent);
            double target = Math.Atan(2 * band * tanHalfExtent);
            return Math.Sign(position) * (float)((current - target) * (180 / Math.PI));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
