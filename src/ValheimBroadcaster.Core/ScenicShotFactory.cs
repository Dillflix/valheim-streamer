using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Builds purposeful candidate routes around measured subjects.
    /// Every candidate still requires exact native-path acquisition validation.</summary>
    public static class ScenicShotFactory
    {
        public static ScenicShotPlan Create(long id, ScenicFeature feature, ScenicShotRole role,
            int viewpoint, float preferredFov = 75, bool deliberateComposition = true, bool useNativeSpline = true)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (viewpoint < 0 || viewpoint > 31) throw new ArgumentOutOfRangeException(nameof(viewpoint));
            if (float.IsNaN(preferredFov) || float.IsInfinity(preferredFov)) throw new ArgumentOutOfRangeException(nameof(preferredFov));
            double angle = (viewpoint % 8) * Math.PI / 4;
            double ox = feature.Direction.X * Math.Cos(angle) + feature.Direction.Z * Math.Sin(angle);
            double oz = feature.Direction.Z * Math.Cos(angle) - feature.Direction.X * Math.Sin(angle);
            double rx = oz, rz = -ox;
            double radius = Math.Min(28, feature.Radius), distance, height, travel;
            float fov;
            switch (role)
            {
                case ScenicShotRole.Establish:
                    distance = Clamp(radius * 2.4 + 10, 18, 55); height = Clamp(radius * .55 + 5, 6, 22);
                    travel = Clamp(radius * .8 + 12, 14, 26); fov = (float)Clamp(preferredFov, 58, 78); break;
                case ScenicShotRole.Traverse:
                    distance = Clamp(radius * 1.6 + 6, 10, 40); height = Clamp(radius * .2 + 3, 3, 12);
                    travel = Clamp(radius * .7 + 10, 12, 24); fov = (float)Clamp(preferredFov - 10, 50, 68); break;
                case ScenicShotRole.Reveal:
                    distance = Clamp(radius * 1.8 + 7, 13, 44); height = Clamp(radius * .15 + 3, 3, 10);
                    travel = Clamp(radius * .5 + 9, 10, 20); fov = (float)Clamp(preferredFov - 8, 52, 70); break;
                case ScenicShotRole.Detail:
                    distance = Clamp(radius * 1.35 + 4, 6, 32); height = Clamp(feature.Height * .08 + 2, 2, 6);
                    travel = Clamp(radius * .35 + 5, 6, 11); fov = (float)Clamp(preferredFov - 22, 40, 55); break;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
            // A clearing is observed from an empty local overlap query. Orbiting
            // its centre at the generic landscape distance places the camera in
            // the surrounding trees instead of using the observed open patch.
            // Keep the proposed route inside that measured footprint. This is
            // only a candidate: the full route still needs actual geometry and
            // foliage validation, including elevations and native curve bends.
            if (feature.SubjectKind == ScenicSubjectKind.Npc)
            {
                distance = ScenicNpcComposition.Distance(feature.Height, feature.Radius, fov, 16f / 9f, role == ScenicShotRole.Detail);
                height = ScenicNpcComposition.Elevation(feature.Height);
            }
            bool clearing = feature.Kind == ScenicFeatureKind.Clearing;
            if (clearing)
            {
                if (radius < 6) throw new ArgumentOutOfRangeException(nameof(feature), "Clearing footprint is too small for a developed route.");
                distance = Math.Min(5, radius * .5);
                travel = Math.Min(8, radius * .85);
                height = role == ScenicShotRole.Establish ? 2.5 : 1.8;
            }
            double sign = (viewpoint / 4) % 2 == 0 ? 1 : -1;
            var center = feature.Center;
            double aimHeight = feature.Kind == ScenicFeatureKind.Structure || feature.Kind == ScenicFeatureKind.Rock ? 0 : 1.5;
            AutonomyPosition aimStart = Point(center, -rx * radius * .06 * sign, aimHeight, -rz * radius * .06 * sign);
            AutonomyPosition aimEnd = Point(center, rx * radius * .06 * sign, aimHeight, rz * radius * .06 * sign);
            AutonomyPosition start = Point(center, ox * distance - rx * travel * .5 * sign, height,
                oz * distance - rz * travel * .5 * sign);
            double finalDistance = role == ScenicShotRole.Detail ? distance * .9 : distance;
            double finalHeight = role == ScenicShotRole.Reveal ? height + (clearing ? 1.7 : Clamp(radius * .4 + 4, 5, 11)) : height;
            AutonomyPosition end = Point(center, ox * finalDistance + rx * travel * .5 * sign, finalHeight,
                oz * finalDistance + rz * travel * .5 * sign);
            AutonomyPosition? control = role == ScenicShotRole.Reveal
                ? Point(center, ox * (distance + travel * .12), (height + finalHeight) * .5, oz * (distance + travel * .12))
                : (AutonomyPosition?)null;
            return ScenicShotPlan.Directed(id, feature, role, start, end, aimStart, aimEnd, fov,
                deliberateComposition ? (float)(sign * .08) : 0,
                deliberateComposition ? role == ScenicShotRole.Establish ? -.1f : -.04f : 0, control, useNativeSpline);
        }

        private static AutonomyPosition Point(AutonomyPosition origin, double x, double y, double z) =>
            new AutonomyPosition((float)(origin.X + x), (float)(origin.Y + y), (float)(origin.Z + z));
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    }
}
