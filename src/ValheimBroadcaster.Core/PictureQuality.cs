using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Picture health is evidence about the presented composition, not
    /// world readiness, carrier reach or permission to travel.</summary>
    public readonly struct PictureHealth
    {
        public readonly bool Readable;
        public readonly string Reason;
        public PictureHealth(bool readable, string reason) { Readable = readable; Reason = reason; }
    }

    public static class PictureQuality
    {
        public const float MaximumForegroundOcclusion = .45f;
        public const float MinimumActorVisibility = .65f;
        public const float MinimumActorReadability = .8f;
        public const float MaximumPrimarySolidOcclusion = .3f;
        public const float MaximumPrimaryFoliageOcclusion = .35f;
        public const float MinimumScenicScore = .55f;

        public static PictureHealth Actor(bool cameraClear, bool inFrame, float visibility, float readability,
            float primarySolid, float primaryFoliage, float foreground, bool surveyComplete)
        {
            if (!Unit(visibility) || !Unit(readability) || !Unit(primarySolid) || !Unit(primaryFoliage) || !Unit(foreground))
                return Bad("Invalid picture-quality evidence");
            if (!cameraClear) return Bad("Camera intersects solid geometry");
            if (!inFrame) return Bad("Primary actor outside camera framing");
            if (!surveyComplete) return Bad("Foliage survey incomplete");
            if (foreground > MaximumForegroundOcclusion) return Bad("Foreground foliage obscures the picture");
            if (primarySolid > MaximumPrimarySolidOcclusion) return Bad("Primary actor obscured by solid geometry");
            if (primaryFoliage > MaximumPrimaryFoliageOcclusion) return Bad("Primary actor obscured by foliage");
            if (readability < MinimumActorReadability) return Bad("Primary actor too small for readable coverage");
            if (visibility < MinimumActorVisibility) return Bad("Insufficient visible primary body");
            return new PictureHealth(true, "Readable actor picture");
        }

        public static PictureHealth Scenic(bool cameraClear, bool focusInFrame, float score,
            float foreground, bool surveyComplete)
        {
            if (!Unit(score) || !Unit(foreground)) return Bad("Invalid picture-quality evidence");
            if (!cameraClear) return Bad("Camera intersects solid geometry");
            if (!focusInFrame) return Bad("Environmental focus outside camera framing");
            if (!surveyComplete) return Bad("Foliage survey incomplete");
            if (foreground > MaximumForegroundOcclusion) return Bad("Foreground foliage obscures the picture");
            if (score < MinimumScenicScore) return Bad("Environmental picture quality below readable threshold");
            return new PictureHealth(true, "Readable environmental picture");
        }

        private static PictureHealth Bad(string reason) => new PictureHealth(false, reason);
        private static bool Unit(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
    }
}
