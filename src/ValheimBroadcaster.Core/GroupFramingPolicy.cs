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
    /// <summary>
    /// The solved camera placement for a FramingSize-style fill: how far the
    /// camera sits on its boom and where the aim-pin lands on screen (Y-down
    /// fraction) so the body spans a target fraction of the frame height. Unlike
    /// the fit-only <see cref="GroupFramingPolicy.RequiredDistanceForVerticalSpan"/>
    /// (which only guarantees the body FITS, with the aim pinned at a constant),
    /// the fill solves BOTH unknowns so the body FILLS the frame.
    /// </summary>
    public readonly struct FramingFillResult
    {
        /// <summary>Camera distance on the boom, in metres.</summary>
        public readonly float DistanceMetres;
        /// <summary>Aim-pin screen Y (Y-down fraction of the half-frame). Negative
        /// pins the aim above centre (more foot-room), positive below (more head-room).</summary>
        public readonly float AimScreenY;
        internal FramingFillResult(float distanceMetres, float aimScreenY)
        { DistanceMetres = distanceMetres; AimScreenY = aimScreenY; }
    }

    /// <summary>
    /// The solved composition when the camera CANNOT reach the full-fill distance
    /// and must sit CLOSER, inside a tight space. The head is pinned with its
    /// headroom (the head point projects to the fixed top target screenY = -0.5 +
    /// topGap) and the frame bottom CROPS the body feet-up as the space shrinks.
    /// Unlike <see cref="FramingFillResult"/> (which solves the distance to FILL),
    /// this takes the distance as given and reports how much of the body survives.
    /// </summary>
    public readonly struct CroppedFramingResult
    {
        /// <summary>Aim-pin screen Y (Y-down fraction of the half-frame) that pins the
        /// head at its target screen position at the given distance, clamped to the
        /// composer's [-0.45, 0.45] limit.</summary>
        public readonly float AimScreenY;
        /// <summary>Metres of body visible below the aim line before the bottom frame
        /// edge crops it (>= 0; the head side is always kept).</summary>
        public readonly float VisibleBelowAimMetres;
        /// <summary>Fraction of the full body (above + belowAimFull) still in frame,
        /// clamped [0, 1]. 1 = the whole body is visible (full shot); shrinks toward 0
        /// as the camera is forced closer (feet crop away first).</summary>
        public readonly float VisibleBodyFraction;
        internal CroppedFramingResult(float aimScreenY, float visibleBelowAimMetres, float visibleBodyFraction)
        { AimScreenY = aimScreenY; VisibleBelowAimMetres = visibleBelowAimMetres; VisibleBodyFraction = visibleBodyFraction; }
    }

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
        /// FramingSize-style FILL solve (Cinemachine GroupFraming FramingSize
        /// concept): given a body that extends <paramref name="belowAimMetres"/>
        /// below and <paramref name="aboveAimMetres"/> above the AIM POINT (margins
        /// already folded in by the caller), at a FIXED vertical fov, solve BOTH the
        /// camera distance AND the aim-pin's screen Y so the body spans exactly
        /// <paramref name="framingSize"/> of the frame height - the feet land at
        /// screenY = +0.5 - bottomGap and the head at screenY = -0.5 + topGap, where
        /// topGap = topGapShare*(1-framingSize) and bottomGap = (1-topGapShare)*
        /// (1-framingSize). Unlike the fit-only solve this does NOT leave the aim at a
        /// fixed pin and waste frame on one side; it positions the aim so the body
        /// truly FILLS the chosen fraction.
        ///
        /// Exact projection model (matches the repo tests): a point at signed angle
        /// phi below the camera forward axis projects to screenY = tan(phi)/(2*tan(
        /// vfov/2)); the aim sits at phi_aim = atan(2*aimScreenY*tan(vfov/2)); feet are
        /// at phi_aim + atan(below/d), head at phi_aim - atan(above/d).
        ///
        /// Solved by bisection on the aim angle phi_aim. The feet constraint gives
        /// d_feet = below / tan(phiFeet - phi_aim) and the head constraint gives
        /// d_head = above / tan(phi_aim - phiHead), where phiFeet/phiHead are the fixed
        /// target angles of the bottom/top edges of the body band. As phi_aim rises
        /// (aim moves toward the feet) d_feet increases and d_head decreases, so
        /// g(phi_aim) = d_feet - d_head is strictly increasing - a single monotone
        /// root, found by bisection, where the two agree and the body fills the band.
        /// </summary>
        public static FramingFillResult FramingFill(
            float fixedVfovDeg, float belowAimMetres, float aboveAimMetres,
            float framingSize, float topGapShare)
        {
            if (!Finite(fixedVfovDeg) || fixedVfovDeg <= 1f || fixedVfovDeg >= 170f)
                throw new ArgumentOutOfRangeException(nameof(fixedVfovDeg));
            if (!Finite(belowAimMetres) || belowAimMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(belowAimMetres));
            if (!Finite(aboveAimMetres) || aboveAimMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(aboveAimMetres));
            if (!Finite(framingSize) || framingSize <= 0.1f || framingSize > 1f)
                throw new ArgumentOutOfRangeException(nameof(framingSize));
            if (!Finite(topGapShare) || topGapShare < 0f || topGapShare > 1f)
                throw new ArgumentOutOfRangeException(nameof(topGapShare));

            double half = fixedVfovDeg * 0.5 * Deg2Rad;
            double tanHalf = Math.Tan(half);

            double gap = 1.0 - framingSize;
            double topGap = topGapShare * gap;
            double bottomGap = (1.0 - topGapShare) * gap;
            // Target screen Y of the body's bottom (feet) and top (head) edges.
            double feetScreenY = 0.5 - bottomGap;   // Y-down: positive, toward the bottom edge.
            double headScreenY = -0.5 + topGap;      // Y-down: negative, toward the top edge.
            // Fixed target angles for those edges under this fov.
            double phiFeet = Math.Atan(2.0 * feetScreenY * tanHalf);
            double phiHead = Math.Atan(2.0 * headScreenY * tanHalf);

            // Bisect phi_aim over the span's interior. phi_aim lives strictly between
            // phiHead and phiFeet (the aim is inside the body band); clamp the search
            // to the aimScreenY [-0.45, 0.45] limit the composer and the fit solve
            // share, and guard a degenerate near-zero angle difference (< 0.5 deg).
            double phiLimit = Math.Atan(2.0 * 0.45 * tanHalf);
            double guard = 0.5 * Deg2Rad;
            double lo = Math.Max(phiHead + guard, -phiLimit);
            double hi = Math.Min(phiFeet - guard, phiLimit);
            if (hi <= lo)
                throw new ArgumentOutOfRangeException(nameof(framingSize),
                    "Framing size leaves no room to place the aim inside the frame at this fov.");

            // g(phi_aim) = d_feet - d_head is strictly increasing. Bisect for its root.
            double DFeet(double phiAim) => belowAimMetres / Math.Tan(phiFeet - phiAim);
            double DHead(double phiAim) => aboveAimMetres / Math.Tan(phiAim - phiHead);
            double g(double phiAim) => DFeet(phiAim) - DHead(phiAim);

            double gLo = g(lo), gHi = g(hi);
            double phiAimSolved;
            if (gLo >= 0.0) phiAimSolved = lo;        // Root at/left of range: clamp.
            else if (gHi <= 0.0) phiAimSolved = hi;   // Root at/right of range: clamp.
            else
            {
                double a = lo, b = hi;
                for (int i = 0; i < 80; i++)
                {
                    double mid = 0.5 * (a + b);
                    double gm = g(mid);
                    if (gm > 0.0) b = mid; else a = mid;
                    if (b - a < 1e-10) break;
                }
                phiAimSolved = 0.5 * (a + b);
            }

            double d = 0.5 * (DFeet(phiAimSolved) + DHead(phiAimSolved));
            // Invert phi_aim back to the composer's Y-down screen fraction.
            double aimScreenY = Math.Tan(phiAimSolved) / (2.0 * tanHalf);
            return new FramingFillResult((float)d, (float)aimScreenY);
        }

        /// <summary>
        /// TIGHT-SPACE cropped composition. When the physical boom room
        /// (<paramref name="availableDistanceMetres"/>) is less than the full-fill
        /// distance the camera cannot pull back far enough to fit the whole body at
        /// the fixed lens. Instead of bending the lens (inviolate) we PIN THE HEAD
        /// with its headroom and let the bottom frame edge CROP the body feet-up: the
        /// shot degrades gracefully full shot -> medium close -> close-up as the space
        /// shrinks, while the head is always kept.
        ///
        /// The head point (aim + <paramref name="aboveAimMetres"/>) is forced to the
        /// fixed top target screenY = -0.5 + <paramref name="topGapFraction"/>, whose
        /// angle below the optical axis is htheta = atan(2*(-0.5+topGap)*tan(vfov/2))
        /// (negative, toward the top edge). At this distance the head sits at
        /// phi_aim - atan(above/d) = htheta, so phi_aim = htheta + atan(above/d) and
        /// aimScreenY = tan(phi_aim)/(2*tan(vfov/2)), clamped to the composer's
        /// [-0.45, 0.45] limit. The bottom frame edge is at btheta = atan(2*0.5*
        /// tan(vfov/2)); the body visible below the aim is d*tan(btheta - phi_aim)
        /// (floored at 0). The visible body fraction is clamp01((visibleBelow + above)
        /// / (belowAimFull + above)). At the full-fill distance visibleBelow reaches
        /// belowAimFull and the fraction is ~1 (reproduces the FramingFill composition);
        /// closer distances give a decreasing fraction.
        /// </summary>
        public static CroppedFramingResult CroppedFraming(
            float fixedVfovDeg, float availableDistanceMetres, float aboveAimMetres,
            float belowAimFullMetres, float topGapFraction)
        {
            if (!Finite(fixedVfovDeg) || fixedVfovDeg <= 1f || fixedVfovDeg >= 170f)
                throw new ArgumentOutOfRangeException(nameof(fixedVfovDeg));
            if (!Finite(availableDistanceMetres) || availableDistanceMetres <= 0.5f)
                throw new ArgumentOutOfRangeException(nameof(availableDistanceMetres));
            if (!Finite(aboveAimMetres) || aboveAimMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(aboveAimMetres));
            if (!Finite(belowAimFullMetres) || belowAimFullMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(belowAimFullMetres));
            if (!Finite(topGapFraction) || topGapFraction < 0f || topGapFraction > 0.4f)
                throw new ArgumentOutOfRangeException(nameof(topGapFraction));

            double half = fixedVfovDeg * 0.5 * Deg2Rad;
            double tanHalf = Math.Tan(half);
            double d = availableDistanceMetres;

            // Head target screen Y (Y-down, negative = above centre) -> its angle below
            // the optical axis. htheta is negative (the head is toward the top edge).
            double headScreenY = -0.5 + topGapFraction;
            double htheta = Math.Atan(2.0 * headScreenY * tanHalf);
            // Pin the head: phi_aim - atan(above/d) = htheta.
            double phiAim = htheta + Math.Atan(aboveAimMetres / d);
            double aimScreenY = Math.Tan(phiAim) / (2.0 * tanHalf);
            // Clamp the aim to the composer's limit; the effective aim angle follows.
            double aimClamped = aimScreenY < -0.45 ? -0.45 : (aimScreenY > 0.45 ? 0.45 : aimScreenY);
            double phiAimEff = Math.Atan(2.0 * aimClamped * tanHalf);

            // Bottom frame edge angle, then the body visible below the aim at this
            // distance (floored at 0 - a very close shot may crop above the aim line).
            double btheta = Math.Atan(2.0 * 0.5 * tanHalf);
            double visibleBelow = d * Math.Tan(btheta - phiAimEff);
            if (visibleBelow < 0.0) visibleBelow = 0.0;

            double frac = (visibleBelow + aboveAimMetres) / (belowAimFullMetres + aboveAimMetres);
            if (frac < 0.0) frac = 0.0; else if (frac > 1.0) frac = 1.0;
            return new CroppedFramingResult((float)aimClamped, (float)visibleBelow, (float)frac);
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
        /// Combine the PLAYER's own vertical spans with an engaged enemy's spans
        /// (both measured below/above the player's aim line, the enemy's already
        /// projected to equivalent metres at the player's planar distance by the
        /// caller) into GROUP spans, weighted by the sparse-gate group weight. The
        /// FramingFill solve then receives group-consistent spans so the aim and
        /// dolly balance the whole picture instead of the player alone: a small low
        /// creature grows the below span (aim tilts DOWN, empty sky shrinks); a tall
        /// enemy grows the above span (headroom adapts).
        ///
        /// groupBelow = Lerp(playerBelow, max(playerBelow, enemyBelowEq), weight);
        /// groupAbove = Lerp(playerAbove, max(playerAbove, enemyAboveEq), weight).
        /// At weight 0 the result is the player's own spans (solo identity); at
        /// weight 1 it is the element-wise max (the group must contain both). The
        /// max keeps the player always framed - the group never crops the player to
        /// fit a smaller enemy. Returns (below, above) as a tuple; the caller folds
        /// margins before calling and feeds the result straight to FramingFill.
        /// </summary>
        public static (float Below, float Above) CombineGroupSpans(
            float playerBelow, float playerAbove, float enemyBelowEq, float enemyAboveEq, float weight)
        {
            if (!Finite(playerBelow) || playerBelow < 0f) throw new ArgumentOutOfRangeException(nameof(playerBelow));
            if (!Finite(playerAbove) || playerAbove < 0f) throw new ArgumentOutOfRangeException(nameof(playerAbove));
            if (!Finite(enemyBelowEq) || enemyBelowEq < 0f) throw new ArgumentOutOfRangeException(nameof(enemyBelowEq));
            if (!Finite(enemyAboveEq) || enemyAboveEq < 0f) throw new ArgumentOutOfRangeException(nameof(enemyAboveEq));
            if (!Finite(weight) || weight < 0f || weight > 1f) throw new ArgumentOutOfRangeException(nameof(weight));
            float below = playerBelow + (Math.Max(playerBelow, enemyBelowEq) - playerBelow) * weight;
            float above = playerAbove + (Math.Max(playerAbove, enemyAboveEq) - playerAbove) * weight;
            return (below, above);
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
