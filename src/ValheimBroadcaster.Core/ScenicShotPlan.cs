using System;

namespace ValheimBroadcaster.Core
{
    public enum ScenicShotStyle { Lateral, Arc, Lift, Reveal, Approach }

    public readonly struct ScenicShotSample
    {
        public readonly AutonomyPosition CameraPosition, AimPosition;
        public readonly float ScreenX, ScreenY;
        public readonly string Phase;
        internal ScenicShotSample(AutonomyPosition camera, AutonomyPosition aim, float x, float y, string phase)
        { CameraPosition = camera; AimPosition = aim; ScreenX = x; ScreenY = y; Phase = phase; }

        /// <summary>Retain the authored aim/composition while a native path solver
        /// supplies its camera position.</summary>
        public ScenicShotSample WithCameraPosition(AutonomyPosition camera) =>
            new ScenicShotSample(camera, AimPosition, ScreenX, ScreenY, Phase);
    }

    /// <summary>
    /// An environmental shot, not a synthetic actor. The scene adapter supplies
    /// measured world-space aim endpoints and initial camera offset, then checks
    /// the resulting complete path for scene support, clearance and composition.
    /// Progress comes from usable coverage time; sampling owns no clock/history.
    /// </summary>
    public sealed class ScenicShotPlan
    {
        public long Id { get; }
        public ScenicShotStyle Style { get; }
        public bool IsDirected { get; }
        /// <summary>The route engine is latched at preparation so a live
        /// setting change cannot replace the geometry beneath a displayed shot.</summary>
        public bool UseNativeSpline { get; } = true;
        public string FeatureId { get; } = string.Empty;
        public ScenicFeatureKind FeatureKind { get; }
        public ScenicShotRole Role { get; }
        public double DurationSeconds { get; } = 45;
        public float FieldOfView { get; } = 75;
        public double TravelMetres { get; }
        /// <summary>Approximate screen-width change from parallax, perspective
        /// and the authored aim sweep. A planning hint, not rendered evidence.</summary>
        public double VisualChange { get; }
        public ScenicFeature? Feature { get; }
        private readonly AutonomyPosition start, end, offset;
        private readonly AutonomyPosition cameraStart, cameraEnd, cameraControl;
        private readonly bool curved;
        private readonly float screenX, screenY;

        public ScenicShotPlan(long id, ScenicShotStyle style, AutonomyPosition aimStart,
            AutonomyPosition aimEnd, AutonomyPosition cameraOffset, float screenX = 0, float screenY = 0.08f)
        {
            if (!Enum.IsDefined(typeof(ScenicShotStyle), style)) throw new ArgumentOutOfRangeException(nameof(style));
            if (aimStart.Distance(aimEnd) > 32) throw new ArgumentOutOfRangeException(nameof(aimEnd));
            double reach = cameraOffset.Distance(default);
            if (reach < 2 || reach > 150) throw new ArgumentOutOfRangeException(nameof(cameraOffset));
            if (!Finite(screenX) || !Finite(screenY) || Math.Abs(screenX) > 0.2f || Math.Abs(screenY) > 0.2f)
                throw new ArgumentOutOfRangeException(nameof(screenX));
            Id = id; Style = style; start = aimStart; end = aimEnd; offset = cameraOffset;
            this.screenX = screenX; this.screenY = screenY;
        }

        private ScenicShotPlan(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition bodyStart, AutonomyPosition bodyEnd, AutonomyPosition aimStart,
            AutonomyPosition aimEnd, float fov, float x, float y, AutonomyPosition? control, bool useNativeSpline)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (!Enum.IsDefined(typeof(ScenicShotRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (!Finite(fov) || fov < 30 || fov > 110) throw new ArgumentOutOfRangeException(nameof(fov));
            if (!Finite(x) || !Finite(y) || Math.Abs(x) > .2 || Math.Abs(y) > .2)
                throw new ArgumentOutOfRangeException(nameof(x));
            Id = id; IsDirected = true; Feature = feature; FeatureId = feature.Id; FeatureKind = feature.Kind; Role = role;
            UseNativeSpline = useNativeSpline;
            Style = role == ScenicShotRole.Reveal ? ScenicShotStyle.Reveal : role == ScenicShotRole.Detail ? ScenicShotStyle.Approach : ScenicShotStyle.Lateral;
            start = aimStart; end = aimEnd; cameraStart = bodyStart; cameraEnd = bodyEnd;
            curved = control.HasValue; cameraControl = control ?? Midpoint(bodyStart, bodyEnd);
            FieldOfView = fov; screenX = x; screenY = y;
            double travel = 0;
            AutonomyPosition previous = bodyStart;
            for (int i = 0; i <= 32; i++)
            {
                double u = i / 32d;
                AutonomyPosition body = Body(u), aim = Lerp(start, end, u);
                double reach = body.Distance(aim);
                if (reach < 2 || reach > 180 || body.HorizontalDistance(aim) < .75)
                    throw new ArgumentOutOfRangeException(nameof(bodyEnd), "Directed route must retain a finite, separated environmental aim.");
                travel += body.Distance(previous); previous = body;
            }
            if (travel < 1 || travel > 80) throw new ArgumentOutOfRangeException(nameof(bodyEnd), "Directed routes require bounded, meaningful movement.");
            TravelMetres = travel;
            VisualChange = EstimateVisualChange();
            // The route and its perceptual development determine duration. Long
            // routes retain a speed bound rather than being accelerated to fit
            // the ordinary 8–20 second scenic editing range.
            double speed = role == ScenicShotRole.Detail ? .7 : role == ScenicShotRole.Establish ? 1.35 : 1.1;
            double visualSeconds = VisualChange / (role == ScenicShotRole.Detail ? .035 : .045);
            DurationSeconds = Math.Max(8, Math.Max(travel / 2.0, Math.Min(20, Math.Max(travel / speed, visualSeconds))));
        }

        public static ScenicShotPlan Directed(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition cameraStart, AutonomyPosition cameraEnd, AutonomyPosition aimStart,
            AutonomyPosition aimEnd, float fieldOfView, float screenX = 0, float screenY = -.08f,
            AutonomyPosition? cameraControl = null, bool useNativeSpline = true) =>
            new ScenicShotPlan(id, feature, role, cameraStart, cameraEnd, aimStart, aimEnd,
                fieldOfView, screenX, screenY, cameraControl, useNativeSpline);

        /// <summary>Restrict a route to its useful prefix. This is a new route
        /// definition and must pass native-curve/visibility validation again.</summary>
        public ScenicShotPlan Trim(double endFraction)
        {
            if (!IsDirected) throw new InvalidOperationException("Only directed routes can be shortened.");
            if (double.IsNaN(endFraction) || double.IsInfinity(endFraction) || endFraction <= 0 || endFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(endFraction));
            double u = ScenicMotionProfile.Progress(endFraction);
            return Directed(Id, Feature!, Role, cameraStart, Body(u), start, Lerp(start, end, u), FieldOfView,
                screenX, screenY, curved ? Lerp(cameraStart, cameraControl, u) : (AutonomyPosition?)null, UseNativeSpline);
        }

        /// <summary>Fit a proposed route above observed terrain as one rigid
        /// vertical translation. This creates another immutable definition;
        /// it never clamps or bobs individual live camera samples. Aim, lens,
        /// horizontal path and relative rise remain authored and the complete
        /// resulting native curve must be screened again before admission.</summary>
        public ScenicShotPlan RaiseCamera(float metres)
        {
            if (!IsDirected) throw new InvalidOperationException("Only directed routes can be terrain fitted.");
            if (!Finite(metres) || metres < 0 || metres > 64)
                throw new ArgumentOutOfRangeException(nameof(metres));
            if (metres == 0) return this;
            return Directed(Id, Feature!, Role, Raised(cameraStart, metres), Raised(cameraEnd, metres),
                start, end, FieldOfView, screenX, screenY,
                curved ? Raised(cameraControl, metres) : (AutonomyPosition?)null, UseNativeSpline);
        }

        /// <summary>Bounded non-looping movement starts/ends at rest. A paused
        /// caller samples the same progress; previews never advance state.
        /// Strength zero and dynamic=false retain the initial world viewpoint.</summary>
        public ScenicShotSample Sample(double progress, float strength = 1, bool dynamic = true)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress) || !Finite(strength) || strength < 0)
                throw new ArgumentOutOfRangeException(nameof(progress));
            double t = Math.Max(0, Math.Min(1, progress));
            double ease = ScenicMotionProfile.Progress(t);
            if (IsDirected)
            {
                // Runtime strength alters the caller's progress clock, never
                // the geometry which passed acquisition validation.
                double u = dynamic && strength > 0 ? ease : 0;
                return new ScenicShotSample(Body(u), Lerp(start, end, u), screenX, screenY,
                    !dynamic || strength == 0 ? "Holding directed composition" : t < .08 ? "Entering" : t > .92 ? "Completing" : "Developing " + Role);
            }
            double move = dynamic ? ease * Math.Min(1.5, strength) : 0;
            double dx = end.X - start.X, dy = end.Y - start.Y, dz = end.Z - start.Z;
            double x = offset.X, y = offset.Y, z = offset.Z;
            double bodyTravel, aimTravel;
            switch (Style)
            {
                case ScenicShotStyle.Lateral:
                    bodyTravel = move; aimTravel = move * 0.25; break;
                case ScenicShotStyle.Arc:
                    // The same environmental path can support either direction;
                    // the immutable ID picks a stable side without a runtime loop.
                    double angle = ((Id & 1) == 0 ? 1 : -1) * move * Math.PI / 10;
                    x = offset.X * Math.Cos(angle) + offset.Z * Math.Sin(angle);
                    z = offset.Z * Math.Cos(angle) - offset.X * Math.Sin(angle);
                    bodyTravel = move * 0.25; aimTravel = move * 0.15; break;
                case ScenicShotStyle.Lift:
                    y += 4 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.25; break;
                case ScenicShotStyle.Reveal:
                    x *= 1 + 0.14 * move; z *= 1 + 0.14 * move;
                    y += 2.5 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.4; break;
                default: // Approach changes scale without pinning the aim to the moving camera.
                    x *= 1 - 0.15 * move; y *= 1 - 0.15 * move; z *= 1 - 0.15 * move;
                    bodyTravel = move * 0.2; aimTravel = move * 0.15; break;
            }
            AutonomyPosition camera = Position(start.X + x + dx * bodyTravel,
                start.Y + y + dy * bodyTravel, start.Z + z + dz * bodyTravel);
            AutonomyPosition aim = Position(start.X + dx * aimTravel, start.Y + dy * aimTravel,
                start.Z + dz * aimTravel);
            string phase = !dynamic || strength == 0 ? "Holding environmental composition" :
                t < 0.2 ? "Establishing" : t < 0.8 ? "Developing" : "Settling";
            return new ScenicShotSample(camera, aim, screenX, screenY, phase);
        }

        private static AutonomyPosition Position(double x, double y, double z) =>
            new AutonomyPosition((float)Math.Max(-20000, Math.Min(20000, x)),
                (float)Math.Max(-20000, Math.Min(20000, y)), (float)Math.Max(-20000, Math.Min(20000, z)));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private AutonomyPosition Body(double u)
        {
            if (!curved) return Lerp(cameraStart, cameraEnd, u);
            double a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, c = u * u;
            return Position(a * cameraStart.X + b * cameraControl.X + c * cameraEnd.X,
                a * cameraStart.Y + b * cameraControl.Y + c * cameraEnd.Y,
                a * cameraStart.Z + b * cameraControl.Z + c * cameraEnd.Z);
        }
        private static AutonomyPosition Lerp(AutonomyPosition a, AutonomyPosition b, double u) =>
            Position(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);
        private static AutonomyPosition Midpoint(AutonomyPosition a, AutonomyPosition b) => Lerp(a, b, .5);
        private static AutonomyPosition Raised(AutonomyPosition p, float metres) =>
            new AutonomyPosition(p.X, p.Y + metres, p.Z);
        private double EstimateVisualChange()
        {
            double value = 0;
            AutonomyPosition a = cameraStart, targetA = start;
            double tangent = Math.Tan(FieldOfView * Math.PI / 360);
            for (int i = 1; i <= 16; i++)
            {
                double u = i / 16d;
                AutonomyPosition b = Body(u), targetB = Lerp(start, end, u);
                double distance = Math.Max(2, a.Distance(targetA));
                double dx = targetA.X - a.X, dy = targetA.Y - a.Y, dz = targetA.Z - a.Z;
                double mx = b.X - a.X, my = b.Y - a.Y, mz = b.Z - a.Z;
                double along = (mx * dx + my * dy + mz * dz) / distance;
                double sideways = Math.Sqrt(Math.Max(0, mx * mx + my * my + mz * mz - along * along));
                // Translation across the sightline gives parallax; approaching
                // a measured subject changes its projected size. Aim sweeps
                // also reveal scenery even with a largely axial body route.
                value += sideways / (2 * distance * tangent) + Math.Abs(along) * Math.Min(1, Feature!.Radius / distance) / distance +
                    targetB.Distance(targetA) / (4 * distance * tangent);
                a = b; targetA = targetB;
            }
            return value;
        }
    }
}
