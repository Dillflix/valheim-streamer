using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>
    /// Pure, deterministic full-body framing math. The mm lens targets (solo 50,
    /// combat 75) are aesthetic targets; this policy solves the minimal VERTICAL
    /// field of view (a widening-only floor) that keeps a required vertical body
    /// segment on screen when the aim point is pinned at a known screen fraction.
    /// The caller maxes the solved floor against the lens target, so the presented
    /// fov never narrows below the lens and only ever widens to rescue framing.
    ///
    /// Convention: the aim point is placed at screen fraction aimScreenY using the
    /// Cinemachine Y-down composer convention. A POSITIVE aimScreenY places the aim
    /// point BELOW frame centre (our live value is 0.05), which shifts the aim
    /// point below the camera forward axis by phi_aim = atan(2*aimScreenY*tan(vFov/2)).
    /// A segment reaching angleBelow below the aim and angleAbove above the aim then
    /// fits when phi_aim + angleBelow &lt;= vFov/2 AND angleAbove - phi_aim &lt;= vFov/2.
    /// Because phi_aim itself depends on vFov the condition is mildly nonlinear, so
    /// the minimal fov is found by bisection in [1,120] degrees to 0.01 degrees.
    /// </summary>
    public static class FrameFitPolicy
    {
        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        // The bisection search window for the solved vertical fov, degrees. The
        // upper bound is the fisheye-ish practical ceiling; the caller applies its
        // own hard cap (85) on the result.
        private const double MinFov = 1.0;
        private const double MaxFov = 120.0;
        // Bisection tolerance, degrees. Tight enough that the binding extreme lands
        // within ~1e-3 of the frame edge in screen fraction across the solved range.
        private const double Tolerance = 0.0005;

        /// <summary>The minimal vertical fov (degrees) such that a vertical segment
        /// extending <paramref name="belowAimMetres"/> below and
        /// <paramref name="aboveAimMetres"/> above the aim point fits on screen when
        /// the aim point is pinned at screen fraction <paramref name="aimScreenY"/>
        /// (Cinemachine Y-down; positive = aim below centre). The planar camera-to-
        /// aim distance is <paramref name="planarDistanceMetres"/>. Metre spans are
        /// converted to angles with atan(span/distance) and delegated to
        /// <see cref="RequiredVerticalFovFromAnglesDegrees"/>.</summary>
        public static float RequiredVerticalFovDegrees(float aimScreenY, float belowAimMetres,
            float aboveAimMetres, float planarDistanceMetres)
        {
            if (!Finite(belowAimMetres) || belowAimMetres < 0)
                throw new ArgumentOutOfRangeException(nameof(belowAimMetres));
            if (!Finite(aboveAimMetres) || aboveAimMetres < 0)
                throw new ArgumentOutOfRangeException(nameof(aboveAimMetres));
            if (!Finite(planarDistanceMetres) || planarDistanceMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(planarDistanceMetres));
            double angleBelow = Math.Atan(belowAimMetres / planarDistanceMetres) * Rad2Deg;
            double angleAbove = Math.Atan(aboveAimMetres / planarDistanceMetres) * Rad2Deg;
            return RequiredVerticalFovFromAnglesDegrees(aimScreenY,
                (float)angleBelow, (float)angleAbove);
        }

        /// <summary>The minimal vertical fov (degrees) that fits a segment which sits
        /// <paramref name="angleBelowAimDeg"/> below the aim direction and
        /// <paramref name="angleAboveAimDeg"/> above it, with the aim pinned at screen
        /// fraction <paramref name="aimScreenY"/> (Cinemachine Y-down; positive = aim
        /// below centre). Used directly for the enemy, whose bottom/top are supplied
        /// as angles relative to the camera-to-player-aim direction so a tall troll
        /// beside a small player frames correctly. Solved by bisection: for a trial
        /// fov the aim offset is phi_aim = atan(2*aimScreenY*tan(fov/2)); the fit holds
        /// when phi_aim + angleBelow &lt;= fov/2 AND angleAbove - phi_aim &lt;= fov/2.</summary>
        public static float RequiredVerticalFovFromAnglesDegrees(float aimScreenY,
            float angleBelowAimDeg, float angleAboveAimDeg)
        {
            if (!Finite(aimScreenY) || aimScreenY < -0.45f || aimScreenY > 0.45f)
                throw new ArgumentOutOfRangeException(nameof(aimScreenY));
            if (!Finite(angleBelowAimDeg) || angleBelowAimDeg < 0 || angleBelowAimDeg >= 90)
                throw new ArgumentOutOfRangeException(nameof(angleBelowAimDeg));
            if (!Finite(angleAboveAimDeg) || angleAboveAimDeg < 0 || angleAboveAimDeg >= 90)
                throw new ArgumentOutOfRangeException(nameof(angleAboveAimDeg));

            double below = angleBelowAimDeg * Deg2Rad;
            double above = angleAboveAimDeg * Deg2Rad;
            double screen = aimScreenY;

            // Fits(fov): does the trial half-fov contain both extremes? phi_aim is
            // the aim-point offset below the forward axis at this fov (Y-down => a
            // positive aimScreenY yields a positive, below-centre offset).
            bool Fits(double fovDeg)
            {
                double half = fovDeg * 0.5 * Deg2Rad;
                double phiAim = Math.Atan(2.0 * screen * Math.Tan(half));
                return (phiAim + below) <= half + 1e-12 && (above - phiAim) <= half + 1e-12;
            }

            // The maximum window never fits the segment: clamp to it (caller still
            // hard-caps the result). This is the pathological deoccluder-jammed case.
            if (!Fits(MaxFov)) return (float)MaxFov;
            // Bisection: Fits is monotonic in fov (a wider frame only ever helps),
            // so narrow [lo,hi] until the half-interval is within tolerance.
            double lo = MinFov, hi = MaxFov;
            if (Fits(lo)) return (float)lo;
            for (int i = 0; i < 40 && (hi - lo) > Tolerance; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Fits(mid)) hi = mid; else lo = mid;
            }
            return (float)hi;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
