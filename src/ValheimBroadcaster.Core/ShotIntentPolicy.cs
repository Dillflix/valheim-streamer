using System;

namespace ValheimBroadcaster.Core
{
    public enum ShotEnvironment { Unknown, Open, Sheltered, Room, Doorway, Dungeon }
    public enum ShotLocomotion { Stationary, Walk, Run, Vertical, Swim, Boat }
    public enum ShotSceneKind { Player, Creature, Scenic }
    public enum ShotRole { Establish, Tracking, Interaction, Action, Detail, Interior, Landscape }

    /// <summary>Evidence describing a scene, not a claim about a player's intent.
    /// Environment and locomotion must already be qualified by the adapter.</summary>
    public readonly struct ShotSituation
    {
        public readonly ShotSceneKind Kind;
        public readonly CinematicActivity Activity;
        public readonly float Confidence, Space;
        public readonly int Companions, Opponents;
        public readonly bool HasInterest;
        public readonly ShotEnvironment Environment;
        public readonly ShotLocomotion Locomotion;

        public ShotSituation(ShotSceneKind kind, CinematicActivity activity, float confidence,
            int companions, int opponents, bool hasInterest, ShotEnvironment environment,
            ShotLocomotion locomotion, float space = 1)
        {
            if ((int)kind < 0 || (int)kind > (int)ShotSceneKind.Scenic ||
                (int)activity < 0 || (int)activity > (int)CinematicActivity.Work ||
                (int)environment < 0 || (int)environment > (int)ShotEnvironment.Dungeon ||
                (int)locomotion < 0 || (int)locomotion > (int)ShotLocomotion.Boat ||
                !Unit(confidence) || !Unit(space) || companions < 0 || companions > 63 || opponents < 0 || opponents > 64)
                throw new ArgumentOutOfRangeException(nameof(confidence));
            Kind = kind; Activity = activity; Confidence = confidence; Companions = companions;
            Opponents = opponents; HasInterest = hasInterest; Environment = environment;
            Locomotion = locomotion; Space = space;
        }
        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
    }

    /// <summary>Soft composition preferences. Every role retains the full actor
    /// envelope and remains subordinate to actual visibility/physical checks.</summary>
    public readonly struct CinematicShotIntent
    {
        public readonly ShotRole Role;
        public readonly float RadiusScale, HeightScale, FovOffset, GroupWeightScale, InterestWeight,
            PositionDampingScale, AimDampingScale, MotionScale, FramingSize, MaximumAngularSpeed;
        internal CinematicShotIntent(ShotRole role, float radius, float height, float fov,
            float group, float interest, float positionDamping, float aimDamping, float motion,
            float framing, float angularSpeed)
        {
            Role = role; RadiusScale = radius; HeightScale = height; FovOffset = fov;
            GroupWeightScale = group; InterestWeight = interest; PositionDampingScale = positionDamping;
            AimDampingScale = aimDamping; MotionScale = motion; FramingSize = framing;
            MaximumAngularSpeed = angularSpeed;
        }
        public static CinematicShotIntent Neutral => new CinematicShotIntent(ShotRole.Establish,
            1, 1, 0, 1, 0, 1, 1, 1, .65f, 60);
        internal static CinematicShotIntent Ease(CinematicShotIntent from, CinematicShotIntent to, float amount) =>
            new CinematicShotIntent(to.Role, Mix(from.RadiusScale, to.RadiusScale, amount),
                Mix(from.HeightScale, to.HeightScale, amount), Mix(from.FovOffset, to.FovOffset, amount),
                Mix(from.GroupWeightScale, to.GroupWeightScale, amount), Mix(from.InterestWeight, to.InterestWeight, amount),
                Mix(from.PositionDampingScale, to.PositionDampingScale, amount), Mix(from.AimDampingScale, to.AimDampingScale, amount),
                // Urgent restrictions take effect immediately; release is eased.
                to.MotionScale < from.MotionScale ? to.MotionScale : Mix(from.MotionScale, to.MotionScale, amount),
                Mix(from.FramingSize, to.FramingSize, amount), Mix(from.MaximumAngularSpeed, to.MaximumAngularSpeed, amount));
        private static float Mix(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>Chooses what a shot should communicate. It never makes an
    /// unreadable viewpoint acceptable or moves actors to manufacture a scene.</summary>
    public sealed class ShotIntentPolicy
    {
        public const double EstablishSeconds = 6, EvidenceSeconds = 1.5;
        private double lastNow = -1, shown, pendingAge;
        private ShotRole pending = ShotRole.Establish;
        private bool lastUsable;
        public CinematicShotIntent Intent { get; private set; } = CinematicShotIntent.Neutral;
        public string Reason { get; private set; } = "Establishing scene context";

        public CinematicShotIntent Update(double now, double dt, ShotSituation scene, bool usable, ShotRole? roleOverride = null)
        {
            if (!Finite(now) || now < 0 || !Finite(dt) || dt < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (roleOverride.HasValue && ((int)roleOverride.Value < 0 || (int)roleOverride.Value > (int)ShotRole.Landscape))
                throw new ArgumentOutOfRangeException(nameof(roleOverride));
            bool gap = lastNow >= 0 && now - lastNow > 1;
            double elapsed = lastNow >= 0 && !gap ? Math.Min(dt, Math.Min(.25, now - lastNow)) : 0;
            double step = usable && lastUsable ? elapsed : 0;
            if (gap || !usable) pendingAge = 0;
            lastNow = now; lastUsable = usable; shown += step;
            ShotRole wanted = Choose(scene, shown);
            if (roleOverride.HasValue) wanted = ResolveRole(scene, roleOverride.Value);
            if (wanted != pending) { pending = wanted; pendingAge = 0; }
            pendingAge += step;
            bool urgent = wanted == ShotRole.Action || wanted == ShotRole.Interior || wanted == ShotRole.Landscape;
            ShotRole role = urgent || pendingAge >= EvidenceSeconds ? wanted : Intent.Role;
            CinematicShotIntent target = Describe(role, scene);
            // Narrow-room recovery cannot wait for a readable wide shot that
            // the room cannot support. Adapt safety-required compact framing on
            // fresh bounded time, without earning footage or editorial progress.
            double responseTime = wanted == ShotRole.Interior ? elapsed : step;
            float amount = (float)(1 - Math.Exp(-responseTime * 1.5));
            Intent = CinematicShotIntent.Ease(Intent, target, amount);
            Reason = role == ShotRole.Action ? "Retaining readable action and its participants" :
                role == ShotRole.Detail ? scene.HasInterest ? "Showing the actor and observed work point" : "Retaining composition while work-point evidence clears" :
                role == ShotRole.Interaction ? "Explaining the relationship between nearby participants" :
                role == ShotRole.Tracking ? "Following coherent travel with room ahead" :
                role == ShotRole.Interior ? "Compact composition within measured room or passage" :
                role == ShotRole.Landscape ? "Developing the environmental scene" :
                scene.Confidence < .6f ? "Establishing context; activity evidence is uncertain" : "Establishing scene context";
            return Intent;
        }

        /// <summary>Resolve a qualified sequence request before counting its
        /// presentation. This target can differ from the still-easing applied
        /// intent; that older role must not fulfill the requested beat.</summary>
        public static ShotRole ResolveRole(ShotSituation scene, ShotRole requested)
        {
            if ((int)requested < 0 || (int)requested > (int)ShotRole.Landscape)
                throw new ArgumentOutOfRangeException(nameof(requested));
            ShotRole contextual = Choose(scene, EstablishSeconds);
            if (contextual == ShotRole.Interior || contextual == ShotRole.Action || scene.Kind == ShotSceneKind.Scenic)
                return contextual;
            return Supports(requested, scene) ? requested : contextual;
        }

        // An editorial sequence cannot invent an interaction or override a
        // measured cramped-space/active-combat requirement.
        private static bool Supports(ShotRole role, ShotSituation scene)
        {
            if (role == ShotRole.Establish) return true;
            if (scene.Confidence < .6f) return false;
            if (role == ShotRole.Detail) return scene.Activity == CinematicActivity.Work && scene.HasInterest;
            if (role == ShotRole.Interaction) return scene.Companions > 0 || scene.Opponents > 0 || scene.HasInterest;
            if (role == ShotRole.Action) return scene.Activity == CinematicActivity.Combat;
            if (role == ShotRole.Tracking) return scene.Activity == CinematicActivity.Travel ||
                scene.Locomotion == ShotLocomotion.Boat || scene.Locomotion == ShotLocomotion.Swim || scene.Locomotion == ShotLocomotion.Vertical;
            return false;
        }

        private static ShotRole Choose(ShotSituation scene, double shown)
        {
            if (scene.Kind == ShotSceneKind.Scenic) return ShotRole.Landscape;
            if (scene.Environment == ShotEnvironment.Doorway ||
                (scene.Environment == ShotEnvironment.Room || scene.Environment == ShotEnvironment.Dungeon) && scene.Space < .65f)
                return ShotRole.Interior;
            if (scene.Activity == CinematicActivity.Combat && scene.Confidence >= .6f)
                return ShotRole.Action;
            if (shown < EstablishSeconds || scene.Confidence < .6f) return ShotRole.Establish;
            if (scene.Activity == CinematicActivity.Work && scene.HasInterest) return ShotRole.Detail;
            if (scene.Companions > 0 || scene.Opponents > 0) return ShotRole.Interaction;
            if (scene.Activity == CinematicActivity.Travel || scene.Locomotion == ShotLocomotion.Boat ||
                scene.Locomotion == ShotLocomotion.Swim || scene.Locomotion == ShotLocomotion.Vertical)
                return ShotRole.Tracking;
            return ShotRole.Establish;
        }

        private static CinematicShotIntent Describe(ShotRole role, ShotSituation scene)
        {
            float radius = 1, height = 1, fov = 0, group = 1, interest = 0, motion = 1, framing = .65f;
            switch (role)
            {
                case ShotRole.Establish: radius = 1.15f; height = 1.05f; fov = 4; framing = .52f; break;
                case ShotRole.Tracking: height = .85f; motion = .8f; framing = .60f; break;
                case ShotRole.Interaction: radius = 1.08f; height = .9f; fov = 2; group = 1.15f; interest = .1f; motion = .55f; framing = .66f; break;
                case ShotRole.Action: radius = .9f; height = .65f; fov = 4; group = 1.1f; interest = .15f; motion = 0; framing = .68f; break;
                case ShotRole.Detail: radius = .72f; height = .55f; fov = -5; group = .75f; interest = .25f; motion = .3f; framing = .70f; break;
                case ShotRole.Interior: radius = .62f; height = .45f; fov = 7; group = .8f; motion = .08f; framing = .65f; break;
            }
            float position = 1, aim = 1, angular = 45;
            switch (scene.Locomotion)
            {
                case ShotLocomotion.Stationary: position = 1.3f; aim = 1.2f; angular = 30; break;
                case ShotLocomotion.Run: position = .7f; aim = .8f; angular = 60; break;
                case ShotLocomotion.Vertical: position = .75f; aim = .85f; motion *= .3f; angular = 40; break;
                case ShotLocomotion.Swim: position = 1.15f; aim = 1.1f; motion *= .45f; angular = 35; break;
                case ShotLocomotion.Boat: position = 1.45f; aim = 1.3f; motion *= .4f; radius *= 1.1f; angular = 30; break;
            }
            if (scene.Locomotion == ShotLocomotion.Boat && scene.HasInterest)
                interest = Math.Max(.2f, interest);
            if (scene.Environment == ShotEnvironment.Doorway)
            { position *= .75f; aim *= .85f; motion = Math.Min(motion, .05f); }
            else if (scene.Environment == ShotEnvironment.Room || scene.Environment == ShotEnvironment.Dungeon)
                motion *= .25f;
            if (scene.Activity == CinematicActivity.Combat && scene.Confidence >= .6f)
            { aim = Math.Min(aim, .85f); motion = 0; angular = 65; }
            // The environment supplies its own world path; actor scale/movement
            // guesses must not alter a landscape plan.
            if (scene.Kind == ShotSceneKind.Scenic)
                return new CinematicShotIntent(ShotRole.Landscape, 1, 1, 0, 1, 0, 1, 1, 1, .65f, 30);
            return new CinematicShotIntent(role, radius, height, fov, group, scene.HasInterest ? interest : 0,
                position, aim, motion, framing, angular);
        }

        public void Reset()
        { lastNow = -1; shown = pendingAge = 0; lastUsable = false; pending = ShotRole.Establish; Intent = CinematicShotIntent.Neutral; Reason = "Establishing scene context"; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
