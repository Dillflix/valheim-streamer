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
}
