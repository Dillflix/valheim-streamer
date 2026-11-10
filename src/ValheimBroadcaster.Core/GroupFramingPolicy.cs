using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>
    /// Pure dolly geometry for group framing with a FIXED lens. Cinemachine's
    /// native GroupFraming in DollyOnly mode absorbs a fit by moving the camera
    /// along its boom while the lens never bends; this mirrors that math on our
    /// player-anchored orbital rig without a TargetGroup/LookAt-group (which would
    /// move the rule-1 hard-limit protection off the player onto a centroid).
    ///
    /// The lens is fixed, so for a given aim-pin screen position the aim ray sits
    /// at a constant angle below the optical axis and the available down/up
    /// half-angles from the aim are constants. Fitting a vertical span below and
    /// above the aim is then a closed-form required camera distance - no search.
    ///
    /// Screen Y is Y-DOWN (the composer convention): a positive aimScreenY places
    /// the aim-pin below frame centre, leaving headroom above. The down half-angle
    /// (aim toward feet, the bottom edge) is therefore the SMALLER one and usually
    /// dominates a full-body fit; the up half-angle (aim toward head) is larger.
    /// All distances are in metres; all angles in degrees at the public boundary.
    /// </summary>
    public static class GroupFramingPolicy
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// Closed-form camera distance that fits a vertical span reaching
        /// <paramref name="belowAimMetres"/> below and <paramref name="aboveAimMetres"/>
        /// above the aim-pin, at a FIXED vertical fov with the aim pinned at
        /// <paramref name="aimScreenY"/> (Y-down fraction of the half-frame; 0.05
        /// sits just below centre). phi_aim = atan(2*aimScreenY*tan(vfov/2)) is the
        /// aim's constant angle below the optical axis; the available half-angles
        /// are down = vfov/2 - phi_aim (toward the bottom edge) and up = vfov/2 +
        /// phi_aim (toward the top edge). Required distance is
        /// max(below/tan(down), above/tan(up)) - the farther of the two single-axis
        /// fits - so BOTH extremes land exactly on their frame edge at the result.
        /// </summary>
        public static float RequiredDistanceForVerticalSpan(
            float fixedVfovDeg, float aimScreenY, float belowAimMetres, float aboveAimMetres)
        {
            if (!Finite(fixedVfovDeg) || fixedVfovDeg <= 1f || fixedVfovDeg >= 170f)
                throw new ArgumentOutOfRangeException(nameof(fixedVfovDeg));
            if (!Finite(aimScreenY) || aimScreenY < -0.45f || aimScreenY > 0.45f)
                throw new ArgumentOutOfRangeException(nameof(aimScreenY));
            if (!Finite(belowAimMetres) || belowAimMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(belowAimMetres));
            if (!Finite(aboveAimMetres) || aboveAimMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(aboveAimMetres));

            double half = fixedVfovDeg * 0.5 * Deg2Rad;
            double tanHalf = Math.Tan(half);
            double phiAim = Math.Atan(2.0 * aimScreenY * tanHalf);
            double down = half - phiAim;
            double up = half + phiAim;
            // Both available angles must stay open; a degenerate aim that pins one
            // edge essentially at the aim leaves no room to fit anything.
            if (down <= 1.0 * Deg2Rad || up <= 1.0 * Deg2Rad)
                throw new ArgumentOutOfRangeException(nameof(aimScreenY),
                    "Aim screen Y leaves no vertical room on one side at this fov.");

            double dBelow = belowAimMetres > 0f ? belowAimMetres / Math.Tan(down) : 0.0;
            double dAbove = aboveAimMetres > 0f ? aboveAimMetres / Math.Tan(up) : 0.0;
            return (float)Math.Max(dBelow, dAbove);
        }

        /// <summary>
        /// The SPARSE GATE. A smoothstep that eases 1 -> 0 as the player-enemy
        /// planar separation grows from <paramref name="fullMetres"/> (fully
        /// coherent, weight 1) to <paramref name="zeroMetres"/> (incoherent, weight
        /// 0). Below full the weight clamps to 1, beyond zero to 0. The adapter uses
        /// this so a far enemy never drags the camera back into a sparse two-shot:
        /// the enemy only joins the framing group while it is close enough to read
        /// as one picture with the player.
        /// </summary>
        public static float GroupWeight(float separationMetres, float fullMetres, float zeroMetres)
        {
            if (!Finite(separationMetres) || separationMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(separationMetres));
            if (!Finite(fullMetres) || fullMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(fullMetres));
            if (!Finite(zeroMetres) || zeroMetres <= fullMetres)
                throw new ArgumentOutOfRangeException(nameof(zeroMetres));

            if (separationMetres <= fullMetres) return 1f;
            if (separationMetres >= zeroMetres) return 0f;
            float t = (separationMetres - fullMetres) / (zeroMetres - fullMetres);
            // Smoothstep, then invert so near = 1, far = 0.
            float s = t * t * (3f - 2f * t);
            return 1f - s;
        }

        /// <summary>
        /// Required PLAYER distance so an engaged enemy's vertical span fits, the
        /// enemy standing roughly on the player ground plane at an axial depth
        /// offset along the camera forward. Conservative: the enemy's vertical
        /// extent is treated as if at the planar distance (d_player + axialOffset),
        /// so d_player = RequiredDistanceForVerticalSpan(enemy spans) - axialOffset,
        /// floored at 1 m. axialOffset is the enemy's depth relative to the player
        /// along the camera forward (the caller computes it; it may be negative when
        /// the enemy is NEARER the camera than the player).
        /// </summary>
        public static float RequiredDistanceForEnemy(
            float fixedVfovDeg, float aimScreenY, float enemyBelowAimMetres,
            float enemyAboveAimMetres, float enemyPlanarOffsetFromPlayerMetres)
        {
            if (!Finite(enemyPlanarOffsetFromPlayerMetres))
                throw new ArgumentOutOfRangeException(nameof(enemyPlanarOffsetFromPlayerMetres));
            // Validates the fov/aim/span contracts and computes the span fit at the
            // enemy's planar distance.
            float spanDistance = RequiredDistanceForVerticalSpan(
                fixedVfovDeg, aimScreenY, enemyBelowAimMetres, enemyAboveAimMetres);
            float dPlayer = spanDistance - enemyPlanarOffsetFromPlayerMetres;
            return dPlayer < 1f ? 1f : dPlayer;
        }

        /// <summary>
        /// Head-band fallback for an enemy too tall to fit full-body within the
        /// dolly cap. Keeps the enemy's HEAD in frame (feet may crop): the aboveAim
        /// span is unchanged (head top + margin), and the belowAim span is REDUCED
        /// so only the top <paramref name="headKeepMetres"/> band of the enemy must
        /// fit. <paramref name="enemyAboveAimExtentMetres"/> is the enemy head top
        /// above the aim WITHOUT the head margin. If the band bottom (head top -
        /// headKeep) is still above the aim, nothing below the aim needs to fit and
        /// the reduced below-span is 0; otherwise it is how far the band bottom
        /// drops below the aim. headKeepMetres is max(0.8, 30% of enemy height),
        /// computed by the caller.
        /// </summary>
        public static float HeadBandBelowSpan(float enemyAboveAimExtentMetres, float headKeepMetres)
        {
            if (!Finite(enemyAboveAimExtentMetres) || enemyAboveAimExtentMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(enemyAboveAimExtentMetres));
            if (!Finite(headKeepMetres) || headKeepMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(headKeepMetres));
            float bandBottomRelAim = enemyAboveAimExtentMetres - headKeepMetres;
            // Band bottom above the aim -> no below-aim span to fit.
            return bandBottomRelAim >= 0f ? 0f : -bandBottomRelAim;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// EYELINE-ON-TOP-THIRD doctrine (operator order 2026-11-10, supersedes the
    /// vertical FramingSize fill / cropped-head mechanisms): the player's EYELINE
    /// sits on the frame's TOP-THIRD line (Y-down screen fraction -1/6), and the
    /// camera DISTANCE is solved so the feet land exactly on the bottom edge. There
    /// are NO head safety margins and NO headroom gaps anywhere in this doctrine -
    /// the eyeline pin owns the whole vertical composition. Feet margins (air below
    /// the feet) are the only vertical margin that survives, folded into belowAim by
    /// the caller (feet-in-frame is rule 1).
    ///
    /// Two pure closed/searched functions on a FIXED lens. The projection model is
    /// identical to the rest of this file: a point at signed angle phi below the
    /// camera forward axis projects to screenY = tan(phi)/(2*tan(vfov/2)); Y-down,
    /// so a negative screenY is ABOVE centre (toward the top edge). The "eye" is a
    /// point aimToEye metres ABOVE the aim point in the world (the aim sits lower,
    /// nearer the chest); the "feet" are belowAim metres BELOW the aim.
    /// </summary>
    public static class EyelineFraming
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// Closed form: the aim-pin screen Y (Y-down fraction) that places the eye
        /// exactly on <paramref name="eyelineScreenY"/> at the given distance, with
        /// the lens fixed. The eye sits at angle eyeAngle = atan(2*eyelineScreenY*T)
        /// below the optical axis (T = tan(vfov/2); negative when the eye is above
        /// centre). The aim point is <paramref name="aimToEyeMetres"/> BELOW the eye
        /// in the world, so its angle is phi_aim = eyeAngle + atan(aimToEye/distance)
        /// and the returned aimScreenY = tan(phi_aim)/(2*T), clamped to the composer's
        /// [-0.45, 0.45] limit.
        ///
        /// This is the INVARIANT at the heart of the doctrine: whatever the distance
        /// (full-fill outdoors or capped in a tight space), feeding the live distance
        /// through here keeps the eye pinned on the top-third line - the eye NEVER
        /// leaves its line, the frame only crops the feet when the camera is forced in.
        /// </summary>
        public static float AimYForEyeline(
            float fixedVfovDeg, float eyelineScreenY, float aimToEyeMetres, float distanceMetres)
        {
            if (!Finite(fixedVfovDeg) || fixedVfovDeg <= 1f || fixedVfovDeg >= 170f)
                throw new ArgumentOutOfRangeException(nameof(fixedVfovDeg));
            if (!Finite(eyelineScreenY) || eyelineScreenY < -0.45f || eyelineScreenY > 0.45f)
                throw new ArgumentOutOfRangeException(nameof(eyelineScreenY));
            if (!Finite(aimToEyeMetres) || aimToEyeMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(aimToEyeMetres));
            if (!Finite(distanceMetres) || distanceMetres <= 0.3f)
                throw new ArgumentOutOfRangeException(nameof(distanceMetres));

            double tanHalf = Math.Tan(fixedVfovDeg * 0.5 * Deg2Rad);
            double eyeAngle = Math.Atan(2.0 * eyelineScreenY * tanHalf);
            double phiAim = eyeAngle + Math.Atan(aimToEyeMetres / distanceMetres);
            double aimScreenY = Math.Tan(phiAim) / (2.0 * tanHalf);
            if (aimScreenY < -0.45) aimScreenY = -0.45;
            else if (aimScreenY > 0.45) aimScreenY = 0.45;
            return (float)aimScreenY;
        }

        /// <summary>
        /// The camera distance at which, with the eyeline pinned at
        /// <paramref name="eyelineScreenY"/>, the FEET point (<paramref name="belowAimMetres"/>
        /// below the aim) lands exactly on the bottom frame edge (screenY +0.5).
        ///
        /// Monotonicity (documented, verified by the test oracle): with the eyeline
        /// pinned, the aim angle phi_aim(d) = eyeAngle + atan(aimToEye/d) and the feet
        /// angle below the optical axis is phi_feet(d) = phi_aim(d) + atan(belowAim/d).
        /// Both atan terms SHRINK as d grows, so phi_feet(d) is strictly DECREASING in
        /// d. The feet are on the bottom edge when phi_feet(d) == half (the bottom edge
        /// angle, since screenY +0.5 maps to tan(phi)=T i.e. phi=half). Therefore
        /// g(d) = phi_feet(d) - half is strictly decreasing with a single root: at the
        /// low end the feet angle exceeds the frame (feet cropped), at the high end it
        /// sits above the edge (feet in frame with room to spare). Working in ANGLE
        /// space keeps g monotone across the whole search range (screenY space wraps
        /// through tan's pole when phi_feet exceeds 90 deg at tiny d, so it is NOT used
        /// for the bracket). Bisection on d in [0.5, 60], tolerance 1e-4.
        /// </summary>
        public static float DistanceForFeetAtEyeline(
            float fixedVfovDeg, float eyelineScreenY, float aimToEyeMetres, float belowAimMetres)
        {
            if (!Finite(fixedVfovDeg) || fixedVfovDeg <= 1f || fixedVfovDeg >= 170f)
                throw new ArgumentOutOfRangeException(nameof(fixedVfovDeg));
            if (!Finite(eyelineScreenY) || eyelineScreenY < -0.45f || eyelineScreenY > 0.45f)
                throw new ArgumentOutOfRangeException(nameof(eyelineScreenY));
            if (!Finite(aimToEyeMetres) || aimToEyeMetres < 0f)
                throw new ArgumentOutOfRangeException(nameof(aimToEyeMetres));
            if (!Finite(belowAimMetres) || belowAimMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(belowAimMetres));

            double half = fixedVfovDeg * 0.5 * Deg2Rad;
            double tanHalf = Math.Tan(half);
            double eyeAngle = Math.Atan(2.0 * eyelineScreenY * tanHalf);

            // g(d) = phi_feet(d) - half, strictly decreasing in d (see remarks).
            double G(double d) => eyeAngle + Math.Atan(aimToEyeMetres / d) + Math.Atan(belowAimMetres / d) - half;

            double lo = 0.5, hi = 60.0;
            double gLo = G(lo), gHi = G(hi);
            // g decreasing: gLo should be > 0 (feet cropped at the near end), gHi < 0
            // (feet above the edge at the far end). If the root lies outside [lo,hi]
            // the bracket clamps to the nearest endpoint (a very wide lens / tiny span
            // can fit the feet even at 0.5 m).
            if (gLo <= 0.0) return (float)lo;
            if (gHi >= 0.0) return (float)hi;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (G(mid) > 0.0) lo = mid; else hi = mid;
                if (hi - lo < 1e-4) break;
            }
            return (float)(0.5 * (lo + hi));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
