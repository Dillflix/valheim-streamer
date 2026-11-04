using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Fixed guides for one observational shot. Native PositionComposer owns the actual
    /// position response. These values describe breathing room, not another trajectory solver.</summary>
    public readonly struct ObservationalShotProfile
    {
        public readonly float Distance, DepthDeadZone, DeadZoneX, DeadZoneY, HardLimitX, HardLimitY,
            ScreenX, ScreenY, DampingX, DampingY, DampingZ;
        internal ObservationalShotProfile(float distance, float depth, float deadX, float deadY,
            float hardX, float hardY, float screenX, float screenY, float dampingX, float dampingY, float dampingZ)
        {
            Distance = distance; DepthDeadZone = depth; DeadZoneX = deadX; DeadZoneY = deadY;
            HardLimitX = hardX; HardLimitY = hardY; ScreenX = screenX; ScreenY = screenY;
            DampingX = dampingX; DampingY = dampingY; DampingZ = dampingZ;
        }
        public bool IsValid => Distance >= 1 && Distance <= 80 && DepthDeadZone > 0 && DepthDeadZone < Distance &&
            DeadZoneX > 0 && DeadZoneY > 0 && HardLimitX > DeadZoneX && HardLimitY > DeadZoneY &&
            Math.Abs(ScreenX) + HardLimitX * .5f <= .45f && Math.Abs(ScreenY) + HardLimitY * .5f <= .45f &&
            DampingX > 0 && DampingY > 0 && DampingZ > 0;
    }

    public static class ObservationalShotPolicy
    {
        /// <summary>Offer observational coverage alongside existing views. An eligible family still
        /// must pass actual visibility, clearance, framing and native ranking before being shown.</summary>
        public static bool CanUse(ShotSituation scene, ShotRole role, ShotScale scale, bool grouped, bool recovery = false, bool continuing = false)
        {
            if ((int)role < 0 || (int)role > (int)ShotRole.Landscape || (int)scale < 0 || (int)scale > (int)ShotScale.Medium)
                throw new ArgumentOutOfRangeException(nameof(role));
            return scene.Kind != ShotSceneKind.Scenic && !grouped && !recovery && scale != ShotScale.Medium &&
                !scene.RequiresEncounterFocus &&
                scene.Confidence >= .6f && scene.Space >= (continuing ? .3f : .45f) && (scene.Opponents == 0 || scene.ContextualPresence) &&
                (scene.Environment == ShotEnvironment.Open || scene.Environment == ShotEnvironment.Sheltered) &&
                (scene.Locomotion == ShotLocomotion.Stationary || scene.Locomotion == ShotLocomotion.Walk || scene.Locomotion == ShotLocomotion.Run) &&
                scene.Activity != CinematicActivity.Combat && (role == ShotRole.Establish || role == ShotRole.Tracking);
        }

        public static ObservationalShotProfile Describe(ShotScale scale, float cameraDistance, float damping,
            float screenX = 0, float screenY = 0)
        {
            if (scale != ShotScale.Wide && scale != ShotScale.Full || !Finite(cameraDistance) || cameraDistance < 1 || cameraDistance > 80 ||
                !Finite(damping) || damping < 0 || damping > 20 || !Finite(screenX) || !Finite(screenY))
                throw new ArgumentOutOfRangeException(nameof(cameraDistance));
            // Keep the entire hard-limit rectangle within a conservative safe area;
            // the adapter additionally evaluates the full actor envelope, not just its anchor.
            float x = Clamp(screenX, -.12f, .12f), y = Clamp(screenY, -.05f, .05f);
            bool wide = scale == ShotScale.Wide;
            float hardX = (wide ? .72f : .64f) - Math.Abs(x) * 2;
            float hardY = (wide ? .56f : .36f) - Math.Abs(y) * 2;
            float deadX = Math.Min(wide ? .4f : .3f, hardX * .68f);
            float deadY = Math.Min(wide ? .24f : .14f, hardY * .6f);
            float damp = Clamp(damping, .5f, 3);
            return new ObservationalShotProfile(cameraDistance, Math.Min(wide ? 4 : 2.5f, cameraDistance * (wide ? .3f : .22f)),
                deadX, deadY, hardX, hardY, x, y, damp, Math.Min(4, damp * 1.25f), Math.Min(4.5f, damp * 1.6f));
        }
        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Controls when guides and fixed orientation may be reseeded. Ordinary subject motion
    /// never reseeds them, even across a long walking/running shot. Losing binding or a discontinuity
    /// clears history; there is no velocity prediction or camera position calculation here.</summary>
    public sealed class ObservationalShotLease
    {
        private double lastNow = -1;
        private long scene, shot;
        private AutonomyPosition previousFocus;
        public bool IsBound { get; private set; }
        public bool Update(double now, long sceneId, long shotId, AutonomyPosition focus, bool bound = true)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!bound) { Reset(); return false; }
            bool seed = !IsBound || now < lastNow || now - lastNow > 1.25 || sceneId != scene || shotId != shot ||
                focus.Distance(previousFocus) > 16;
            IsBound = true; lastNow = now; scene = sceneId; shot = shotId; previousFocus = focus;
            return seed;
        }
        public void Reset() { IsBound = false; lastNow = -1; scene = shot = 0; previousFocus = default; }
    }
}
