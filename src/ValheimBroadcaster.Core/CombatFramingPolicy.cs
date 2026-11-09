using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Which player state the framing serves. Solo is the baseline
    /// behind-the-player follow; Combat drives the enemy into the opposite
    /// thirds. Weight eases 0..1 between them so the adapter never cuts.</summary>
    public enum CombatFramingPhase { Solo, Combat }

    /// <summary>One caller-sensed hostile: a stable identity, its world azimuth
    /// from the player, planar distance, whether it sits inside the player's
    /// forward cone (the caller applies the dot&gt;=cos(cone) test), and whether
    /// it is still alive. Only qualifying (in-cone, alive) candidates can ever
    /// be engaged; the rest are passed so death and cone-exit are observable.</summary>
    public readonly struct CombatCandidate
    {
        public readonly int Id;
        public readonly float BearingDegrees;
        public readonly float Distance;
        public readonly bool InFront;
        public readonly bool Alive;
        public CombatCandidate(int id, float bearingDegrees, float distance, bool inFront, bool alive)
        { Id = id; BearingDegrees = bearingDegrees; Distance = distance; InFront = inFront; Alive = alive; }
        internal bool Qualifies => Alive && InFront;
    }

    /// <summary>Outcome of one observation. TargetId is the engaged enemy (or 0
    /// when Solo); TargetAzimuthDegrees is the desired orbit horizontal axis that
    /// puts the enemy at the thirds separation; PlayerScreenSide is +1 when the
    /// enemy rides screen-right and the player screen-left, -1 mirrored; Weight
    /// is the eased 0..1 engage factor the adapter blends composition with.</summary>
    public readonly struct CombatFramingDecision
    {
        public readonly CombatFramingPhase Phase;
        public readonly float Weight;
        public readonly int TargetId;
        public readonly float TargetAzimuthDegrees;
        public readonly int PlayerScreenSide;
        public readonly string Reason;
        internal CombatFramingDecision(CombatFramingPhase phase, float weight, int targetId,
            float targetAzimuthDegrees, int playerScreenSide, string reason)
        { Phase = phase; Weight = weight; TargetId = targetId; TargetAzimuthDegrees = targetAzimuthDegrees;
          PlayerScreenSide = playerScreenSide; Reason = reason; }
    }

    /// <summary>
    /// Player STATES, pure and explicit-clock. Solo is the current follow
    /// baseline; Combat frames the closest qualifying enemy into the opposite
    /// thirds from the player. Entering Combat requires a qualifying enemy
    /// sustained past a short gate so a passing hostile never flips the frame;
    /// leaving requires NO qualifying enemy sustained a long while so staggered
    /// kills in a crowd never flicker back to Solo. The current target is sticky
    /// (challenger hysteresis), the enemy's screen side is chosen once per target
    /// from its signed bearing at engage/retarget and never flips mid-target, and
    /// a kill eases the orbit to the next closest. All raycasts, lens reads and
    /// the actual slew belong to the adapter; this owns the state machine, the
    /// selection and the pure thirds/orbit geometry only.
    /// </summary>
    public sealed class CombatFramingPolicy
    {
        /// <summary>A qualifying enemy must hold this long before Combat engages.</summary>
        public const double EngageSustainSeconds = 0.35;
        /// <summary>Combat holds until NO qualifying enemy has been seen for this
        /// long, bridging the gap between kills in a multi-enemy fight.</summary>
        public const double ReleaseSustainSeconds = 2.5;
        /// <summary>A challenger must be at least this fraction closer AND the
        /// absolute margin below to steal a still-valid current target.</summary>
        public const float ChallengerCloserFraction = 0.20f;
        /// <summary>A challenger must also beat the current target by at least
        /// this many metres - percentage alone cannot thrash at long range.</summary>
        public const float ChallengerCloserMetres = 1.0f;
        /// <summary>Engage/release easing time constant; Weight crosses this to
        /// reach the opposite rail. The adapter may ease composition faster or
        /// slower, but exposing it here lets the state own a weighted feel.</summary>
        public const double WeightEaseSeconds = 0.8;

        private CombatFramingPhase phase;
        private double lastNow = -1;
        private double qualifyingSince = -1; // First tick of the current unbroken qualifying run (Solo -> engage gate).
        private double starvedSince = -1;     // First tick with no qualifying enemy while engaged (release gate).
        private double weightSince;           // Clock origin of the current weight ramp.
        private float weightBase;             // Weight at the start of the current ramp (toward the phase rail).
        private int targetId;
        private int targetSide;               // +1 enemy-right / player-left, -1 mirrored. Sticky per target.
        private float targetAzimuth;

        public CombatFramingPhase Phase => phase;
        public int TargetId => targetId;

        /// <summary>Observe one frame. now is a monotonic clock (a rollback is a
        /// world reload and resets the machine); cameraAzimuthDegrees is the orbit
        /// horizontal axis this frame (the cam-&gt;player direction azimuth) used
        /// to pick a sticky side and solve the desired orbit azimuth;
        /// separationDegrees is the live thirds separation (see
        /// <see cref="ThirdsSeparationDegrees"/>); orbitRadius is the follow
        /// distance; candidates are the caller's sensed hostiles this frame (may
        /// be null or empty).</summary>
        public CombatFramingDecision Observe(double now, float cameraAzimuthDegrees, float separationDegrees,
            float orbitRadius, IReadOnlyList<CombatCandidate>? candidates)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!Finite(cameraAzimuthDegrees)) throw new ArgumentOutOfRangeException(nameof(cameraAzimuthDegrees));
            if (!Finite(separationDegrees) || separationDegrees <= 0 || separationDegrees >= 180)
                throw new ArgumentOutOfRangeException(nameof(separationDegrees));
            if (!Finite(orbitRadius) || orbitRadius <= 0) throw new ArgumentOutOfRangeException(nameof(orbitRadius));
            for (int i = 0; candidates != null && i < candidates.Count; i++)
            {
                CombatCandidate candidate = candidates[i];
                if (!Finite(candidate.BearingDegrees) || !Finite(candidate.Distance) || candidate.Distance < 0)
                    throw new ArgumentOutOfRangeException(nameof(candidates));
            }
            if (lastNow >= 0 && now < lastNow) Reset();
            lastNow = now;

            if (phase == CombatFramingPhase.Solo)
                return ObserveSolo(now, cameraAzimuthDegrees, separationDegrees, orbitRadius, candidates);
            return ObserveCombat(now, cameraAzimuthDegrees, separationDegrees, orbitRadius, candidates);
        }

        private CombatFramingDecision ObserveSolo(double now, float cameraAzimuthDegrees, float separationDegrees,
            float orbitRadius, IReadOnlyList<CombatCandidate>? candidates)
        {
            bool found = Closest(candidates, out CombatCandidate best);
            if (!found)
            {
                qualifyingSince = -1;
                return Settled(CombatFramingPhase.Solo, now, 0, "solo follow");
            }
            if (qualifyingSince < 0) qualifyingSince = now;
            if (now - qualifyingSince < EngageSustainSeconds)
                return Settled(CombatFramingPhase.Solo, now, 0, "confirming engagement");
            // Gate met: engage. Fix the sticky side and target now.
            phase = CombatFramingPhase.Combat;
            qualifyingSince = -1; starvedSince = -1;
            Engage(best, cameraAzimuthDegrees, separationDegrees, orbitRadius);
            return Steer(now, "combat engaged");
        }

        private CombatFramingDecision ObserveCombat(double now, float cameraAzimuthDegrees, float separationDegrees,
            float orbitRadius, IReadOnlyList<CombatCandidate>? candidates)
        {
            bool haveCurrent = TryFind(candidates, targetId, out CombatCandidate current) && current.Qualifies;
            bool haveChallenger = Closest(candidates, out CombatCandidate best);
            if (!haveChallenger)
            {
                // No qualifying enemy at all: run the release gate while holding
                // the last solved azimuth so the shot never snaps.
                if (starvedSince < 0) starvedSince = now;
                if (now - starvedSince >= ReleaseSustainSeconds)
                {
                    phase = CombatFramingPhase.Solo;
                    targetId = 0; targetSide = 0; starvedSince = -1;
                    return Settled(CombatFramingPhase.Solo, now, 0, "combat released");
                }
                return Steer(now, "bridging kill");
            }
            starvedSince = -1;
            // Retarget when the current target is gone/invalid, or a challenger
            // clears the hysteresis margin. A fresh target re-fixes its side and
            // resolves a new azimuth; the adapter eases the orbit to it.
            if (!haveCurrent || ShouldReplace(current, best))
            {
                Engage(best, cameraAzimuthDegrees, separationDegrees, orbitRadius);
                return Steer(now, haveCurrent ? "retarget" : "retarget on loss");
            }
            // Hold the current target: refresh its desired azimuth against its
            // live bearing/distance, keeping the fixed side.
            targetAzimuth = SolveOrbitAzimuth(current.BearingDegrees, current.Distance,
                orbitRadius, separationDegrees, targetSide);
            return Steer(now, "combat");
        }

        private bool ShouldReplace(CombatCandidate current, CombatCandidate challenger)
        {
            if (challenger.Id == current.Id) return false;
            return challenger.Distance <= current.Distance * (1f - ChallengerCloserFraction) &&
                   challenger.Distance <= current.Distance - ChallengerCloserMetres;
        }

        // Fix a new target: choose the sticky side from the enemy's signed
        // bearing relative to the camera-forward (cam->player) direction, then
        // solve its desired orbit azimuth.
        private void Engage(CombatCandidate target, float cameraAzimuthDegrees, float separationDegrees,
            float orbitRadius)
        {
            targetId = target.Id;
            // The orbit horizontal axis value IS the cam->player azimuth: the
            // offset is Euler(elev,h,0)*(0,0,-r), so the planar cam->player
            // direction is dir(h). No 180 flip - the axis already carries the
            // camera-forward azimuth.
            float cameraForward = Normalize(cameraAzimuthDegrees);
            float signed = DeltaAngle(cameraForward, target.BearingDegrees);
            // Enemy already left of forward (negative signed) -> it keeps the
            // left; player takes the right. Side is +1 when the enemy rides
            // screen-right. Zero bearing defaults the enemy to the right.
            targetSide = signed >= 0 ? +1 : -1;
            targetAzimuth = SolveOrbitAzimuth(target.BearingDegrees, target.Distance,
                orbitRadius, separationDegrees, targetSide);
        }

        private CombatFramingDecision Steer(double now, string reason)
        {
            RampTo(now, 1f);
            return new CombatFramingDecision(CombatFramingPhase.Combat, Weight(now), targetId, targetAzimuth, targetSide, reason);
        }

        private CombatFramingDecision Settled(CombatFramingPhase phase, double now, int side, string reason)
        {
            RampTo(now, 0f);
            return new CombatFramingDecision(phase, Weight(now), 0, targetAzimuth, side, reason);
        }

        // Linear ramp over WeightEaseSeconds toward a rail. Linear in the policy
        // keeps the math testable; the adapter applies critically damped
        // smoothing on top for the actual weighted feel. Re-aiming at the rail
        // already in progress is a no-op, so the ramp is continuous.
        private void RampTo(double now, float rail)
        {
            if (weightRail == rail) return;
            weightBase = Weight(now);
            weightRail = rail;
            weightSince = now;
        }
        private float weightRail;

        private float Weight(double now)
        {
            double t = (now - weightSince) / WeightEaseSeconds;
            if (t >= 1) return weightRail;
            if (t <= 0) return weightBase;
            return (float)(weightBase + (weightRail - weightBase) * t);
        }

        public void Reset()
        {
            phase = CombatFramingPhase.Solo;
            lastNow = qualifyingSince = starvedSince = -1;
            weightSince = 0; weightBase = 0; weightRail = 0;
            targetId = 0; targetSide = 0; targetAzimuth = 0;
        }

        // Closest qualifying candidate (in-cone, alive). Ties resolve to the
        // lower id so selection is deterministic across sensing order.
        private static bool Closest(IReadOnlyList<CombatCandidate>? candidates, out CombatCandidate best)
        {
            best = default;
            bool found = false;
            for (int i = 0; candidates != null && i < candidates.Count; i++)
            {
                CombatCandidate candidate = candidates[i];
                if (!candidate.Qualifies) continue;
                if (!found || candidate.Distance < best.Distance ||
                    (candidate.Distance == best.Distance && candidate.Id < best.Id))
                { best = candidate; found = true; }
            }
            return found;
        }

        private static bool TryFind(IReadOnlyList<CombatCandidate>? candidates, int id, out CombatCandidate found)
        {
            found = default;
            if (id == 0) return false;
            for (int i = 0; candidates != null && i < candidates.Count; i++)
                if (candidates[i].Id == id) { found = candidates[i]; return true; }
            return false;
        }

        /// <summary>The horizontal angle (degrees) between frame centre and a
        /// thirds line, from the live lens. The enemy sits one thirds line out
        /// from the player, so the cam-apparent player-to-enemy angle must equal
        /// this. hFov = 2*atan(tan(vFov/2)*aspect); the thirds line is 1/6 of the
        /// frame off centre, so sep = 2*atan(2*(1/6)*tan(hFov/2)).</summary>
        public static float ThirdsSeparationDegrees(float verticalFovDegrees, float aspect)
        {
            if (!Finite(verticalFovDegrees) || verticalFovDegrees <= 0 || verticalFovDegrees >= 180)
                throw new ArgumentOutOfRangeException(nameof(verticalFovDegrees));
            if (!Finite(aspect) || aspect <= 0 || aspect >= 10) throw new ArgumentOutOfRangeException(nameof(aspect));
            double vHalf = verticalFovDegrees * 0.5 * Deg2Rad;
            double hHalf = Math.Atan(Math.Tan(vHalf) * aspect);
            double sepHalf = Math.Atan(2.0 * (1.0 / 6.0) * Math.Tan(hHalf));
            return (float)(2.0 * sepHalf * Rad2Deg);
        }

        /// <summary>The desired orbit azimuth (the cam-&gt;player direction the orbit
        /// horizontal axis carries, degrees) that places an enemy at +/-separation
        /// from the cam-&gt;player direction.
        /// The cam-apparent player-to-enemy angle is
        /// apparent(delta) = atan2(d*sin(delta), r + d*cos(delta)), with delta the
        /// angle between the cam-&gt;player direction and the enemy bearing. Setting
        /// apparent = sep and solving gives sin(delta - sep) = (r/d)*sin(sep), so
        /// delta = sep + asin((r/d)*sin(sep)). When the enemy is closer than the
        /// orbit radius (d &lt; r) the biggest achievable apparent angle is
        /// asin(d/r); beyond it the asin argument exceeds 1 and we clamp delta to
        /// the tangent delta = acos(-d/r), which realises exactly that maximum -
        /// the best the orbit can do. side = +1 puts the enemy counter-clockwise
        /// of cam-&gt;player (screen-right under the adapter's convention); -1
        /// mirrors it.</summary>
        public static float SolveOrbitAzimuth(float enemyBearingDegrees, float enemyDistance,
            float orbitRadius, float separationDegrees, int side)
        {
            if (!Finite(enemyBearingDegrees)) throw new ArgumentOutOfRangeException(nameof(enemyBearingDegrees));
            if (!Finite(enemyDistance) || enemyDistance < 0) throw new ArgumentOutOfRangeException(nameof(enemyDistance));
            if (!Finite(orbitRadius) || orbitRadius <= 0) throw new ArgumentOutOfRangeException(nameof(orbitRadius));
            if (!Finite(separationDegrees) || separationDegrees <= 0 || separationDegrees >= 180)
                throw new ArgumentOutOfRangeException(nameof(separationDegrees));
            if (side != 1 && side != -1) throw new ArgumentOutOfRangeException(nameof(side));
            double sep = separationDegrees * Deg2Rad;
            double delta;
            if (enemyDistance <= 1e-4)
            {
                // Degenerate: enemy on top of the player. No orbit azimuth can
                // separate them; park behind the player (tangent collapses to pi).
                delta = Math.PI;
            }
            else
            {
                double arg = (orbitRadius / enemyDistance) * Math.Sin(sep);
                // Infeasible when the requested separation exceeds the orbit's
                // reach (d < r): clamp delta to the tangent acos(-d/r), which
                // realises the maximum achievable apparent angle asin(d/r).
                if (arg >= 1.0 || arg <= -1.0) delta = Math.Acos(-enemyDistance / orbitRadius);
                else delta = sep + Math.Asin(arg);
            }
            // The cam->player direction is the enemy bearing rotated back by
            // side*delta. The orbit horizontal axis value IS that cam->player
            // azimuth (offset = Euler(elev,h,0)*(0,0,-r)), so this is the axis
            // to request directly - no 180 flip.
            return Normalize((float)(enemyBearingDegrees - side * delta * Rad2Deg));
        }

        /// <summary>Shortest signed arc from one azimuth to another, degrees.</summary>
        public static float DeltaAngle(float fromDegrees, float toDegrees)
        {
            float delta = (toDegrees - fromDegrees) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }

        private static float Normalize(float degrees)
        {
            float value = degrees % 360f;
            if (value > 180f) value -= 360f;
            if (value < -180f) value += 360f;
            return value;
        }

        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
