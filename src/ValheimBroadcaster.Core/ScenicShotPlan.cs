using System;
using System.Collections.Generic;

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

        public ScenicShotSample WithAimPosition(AutonomyPosition aim) =>
            new ScenicShotSample(CameraPosition, aim, ScreenX, ScreenY, Phase);
    }

    /// <summary>
    /// An environmental shot, not a synthetic actor. The scene adapter supplies
    /// measured world-space aim endpoints and initial camera offset, then checks
    /// the resulting complete path for scene support, clearance and composition.
    /// Progress comes from usable coverage time; sampling owns no clock/history.
    /// </summary>
    public sealed class ScenicShotPlan
    {
        public long Id { get; private set; }
        public ScenicShotStyle Style { get; }
        public bool IsDirected { get; }
        /// <summary>A single independently measured live composition. It does
        /// not claim a connected movement route or earn cinematic episode
        /// credit; the availability policy owns its bounded presentation.</summary>
        public bool IsPictureOnly { get; }
        public bool IsPan { get; }
        /// <summary>The route engine is latched at preparation so a live
        /// setting change cannot replace the geometry beneath a displayed shot.</summary>
        public bool UseNativeSpline { get; } = true;
        public string FeatureId { get; } = string.Empty;
        public ScenicFeatureKind FeatureKind { get; }
        public ScenicShotRole Role { get; }
        public double DurationSeconds { get; private set; } = 45;
        public bool HasExplicitDuration { get; private set; }
        public ScenicPacingProfile? PacingProfile { get; private set; }
        public long JourneyId { get; private set; }
        public long ContinuationOf { get; private set; }
        public bool IsJourney => JourneyId > 0;
        /// <summary>Journey routes and their prepared endings are certified
        /// against a future wave envelope, not one instantaneous water height.</summary>
        public bool StableWaterClearance { get; private set; }
        /// <summary>Local salvage ended this route just before a measured
        /// rejection. Revalidating the retained prefix does not establish that
        /// extending through its discarded tail is now possible.</summary>
        public string ContinuationRejection { get; private set; } = string.Empty;
        public bool HasRejectedContinuation => ContinuationRejection.Length != 0;
        public bool ConstantSpeed { get; private set; }
        public bool JourneyEaseIn { get; private set; }
        public bool JourneyEaseOut { get; private set; }
        public AutonomyPosition? CameraControl => curved ? cameraControl : (AutonomyPosition?)null;
        public float FieldOfView { get; } = 75;
        public double TravelMetres { get; }
        /// <summary>Approximate screen-width change from parallax, perspective
        /// and the authored aim sweep. A planning hint, not rendered evidence.</summary>
        public double VisualChange { get; }
        public ScenicFeature? Feature { get; }
        public bool IsSpatial => spatialBody != null;
        public int WaypointCount => spatialBody?.Length ?? 0;
        private readonly AutonomyPosition start, end, offset;
        private readonly AutonomyPosition cameraStart, cameraEnd, cameraControl;
        private readonly bool curved;
        private readonly float screenX, screenY;
        private readonly AutonomyPosition[]? spatialBody, spatialAim;
        private readonly double[]? spatialDistance;

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
            AutonomyPosition aimEnd, float fov, float x, float y, AutonomyPosition? control, bool useNativeSpline, bool pan = false)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (!Enum.IsDefined(typeof(ScenicShotRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (!Finite(fov) || fov < 30 || fov > 110) throw new ArgumentOutOfRangeException(nameof(fov));
            if (!Finite(x) || !Finite(y) || Math.Abs(x) > .2 || Math.Abs(y) > .2)
                throw new ArgumentOutOfRangeException(nameof(x));
            Id = id; IsDirected = true; Feature = feature; FeatureId = feature.Id; FeatureKind = feature.Kind; Role = role;
            IsPan = pan; UseNativeSpline = !pan && useNativeSpline;
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
            if ((!pan && travel < 1) || travel > 80 || pan && travel > .001) throw new ArgumentOutOfRangeException(nameof(bodyEnd), "Directed routes require bounded, meaningful movement.");
            TravelMetres = travel;
            VisualChange = EstimateVisualChange();
            DurationSeconds = ScenicPacingPolicy.ProposedSeconds(feature, role, travel, VisualChange);
            if (pan)
            {
                double ax = start.X - cameraStart.X, ay = start.Y - cameraStart.Y, az = start.Z - cameraStart.Z;
                double bx = end.X - cameraStart.X, by = end.Y - cameraStart.Y, bz = end.Z - cameraStart.Z;
                double cosine = (ax * bx + ay * by + az * bz) / (cameraStart.Distance(start) * cameraStart.Distance(end));
                double degrees = Math.Acos(Math.Max(-1, Math.Min(1, cosine))) * 180 / Math.PI;
                if (degrees < 3 || degrees > 18) throw new ArgumentOutOfRangeException(nameof(aimEnd), "A restrained pan develops a bounded 3–18 degree observed composition.");
                DurationSeconds = Math.Max(10, Math.Min(24, degrees / .65));
            }
        }

        public static ScenicShotPlan Directed(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition cameraStart, AutonomyPosition cameraEnd, AutonomyPosition aimStart,
            AutonomyPosition aimEnd, float fieldOfView, float screenX = 0, float screenY = -.08f,
            AutonomyPosition? cameraControl = null, bool useNativeSpline = true) =>
            new ScenicShotPlan(id, feature, role, cameraStart, cameraEnd, aimStart, aimEnd,
                fieldOfView, screenX, screenY, cameraControl, useNativeSpline);

        /// <summary>Purposeful angular development at a clear fixed viewpoint.
        /// Unlike an emergency picture lease, this is a finite authored shot with
        /// its complete aim sweep subject to the usual route validation.</summary>
        public static ScenicShotPlan Pan(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition camera, AutonomyPosition aimStart, AutonomyPosition aimEnd, float fov,
            float screenX = 0, float screenY = -.04f) =>
            new ScenicShotPlan(id, feature, role, camera, camera, aimStart, aimEnd, fov, screenX, screenY, null, false, true);

        public static ScenicShotPlan Picture(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition camera, AutonomyPosition aim, float fieldOfView, float screenX = 0, float screenY = -.08f) =>
            new ScenicShotPlan(id, feature, role, camera, aim, fieldOfView, screenX, screenY);

        private ScenicShotPlan(long id, ScenicFeature feature, ScenicShotRole role,
            AutonomyPosition camera, AutonomyPosition aim, float fov, float x, float y)
        {
            if (id <= 0 || feature == null || !Enum.IsDefined(typeof(ScenicShotRole), role) ||
                !Finite(fov) || fov < 30 || fov > 110 || !Finite(x) || !Finite(y) || Math.Abs(x) > .2 || Math.Abs(y) > .2 ||
                camera.Distance(aim) < 2 || camera.Distance(aim) > 180 || camera.HorizontalDistance(aim) < .75)
                throw new ArgumentOutOfRangeException(nameof(camera), "A picture requires a finite, separated, deliberately composed subject.");
            Id = id; IsDirected = IsPictureOnly = true; Feature = feature; FeatureId = feature.Id;
            FeatureKind = feature.Kind; Role = role; UseNativeSpline = false;
            FieldOfView = fov; screenX = x; screenY = y; DurationSeconds = ScenicPictureAvailability.PreferredPictureSeconds;
            cameraStart = cameraEnd = cameraControl = camera; start = end = aim;
            Style = ScenicShotStyle.Lateral;
        }

        /// <summary>A finite route through measured viewpoints. Points and aims
        /// are copied, so planning buffers cannot alter an on-air trajectory.
        /// This defines a proposal: native spline smoothing and all intervening
        /// poses still need complete acquisition and live validation.</summary>
        public static ScenicShotPlan Spatial(long id, ScenicFeature feature, ScenicShotRole role,
            IReadOnlyList<AutonomyPosition> cameraPoints, IReadOnlyList<AutonomyPosition> aimPoints,
            float fov, float screenX = 0, float screenY = -.04f, bool useNativeSpline = true) =>
            new ScenicShotPlan(id, feature, role, cameraPoints, aimPoints, fov, screenX, screenY, useNativeSpline);

        private ScenicShotPlan(long id, ScenicFeature feature, ScenicShotRole role,
            IReadOnlyList<AutonomyPosition> body, IReadOnlyList<AutonomyPosition> aims,
            float fov, float x, float y, bool useNativeSpline)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (body == null || aims == null) throw new ArgumentNullException(nameof(body));
            if (body.Count < 2 || body.Count > 16 || aims.Count != body.Count)
                throw new ArgumentOutOfRangeException(nameof(body), "Spatial routes require 2–16 paired body/aim points.");
            if (!Enum.IsDefined(typeof(ScenicShotRole), role) || !Finite(fov) || fov < 30 || fov > 110 ||
                !Finite(x) || !Finite(y) || Math.Abs(x) > .2 || Math.Abs(y) > .2)
                throw new ArgumentOutOfRangeException(nameof(fov));
            spatialBody = new AutonomyPosition[body.Count]; spatialAim = new AutonomyPosition[body.Count];
            spatialDistance = new double[body.Count];
            for (int i = 0; i < body.Count; i++)
            {
                spatialBody[i] = body[i]; spatialAim[i] = aims[i];
            }
            for (int i = 0; i < spatialBody.Length; i++)
            {
                if (i > 0)
                {
                    double segment = spatialBody[i - 1].Distance(spatialBody[i]);
                    if (segment < .05 || segment > 40)
                        throw new ArgumentOutOfRangeException(nameof(body), "Spatial camera segments must have distinct, locally reachable endpoints.");
                    spatialDistance[i] = spatialDistance[i - 1] + segment;
                }
            }
            TravelMetres = spatialDistance[body.Count - 1];
            if (TravelMetres < 1 || TravelMetres > 80)
                throw new ArgumentOutOfRangeException(nameof(body), "Spatial movement must remain meaningful and bounded.");
            Id = id; IsDirected = true; Feature = feature; FeatureId = feature.Id; FeatureKind = feature.Kind; Role = role;
            UseNativeSpline = useNativeSpline; FieldOfView = fov; screenX = x; screenY = y;
            Style = role == ScenicShotRole.Reveal ? ScenicShotStyle.Reveal : role == ScenicShotRole.Detail ? ScenicShotStyle.Approach : ScenicShotStyle.Lateral;
            cameraStart = spatialBody[0]; cameraEnd = spatialBody[spatialBody.Length - 1]; cameraControl = Midpoint(cameraStart, cameraEnd);
            start = spatialAim[0]; end = spatialAim[spatialAim.Length - 1];
            // Reject pathological proposed look directions early. The adapter
            // repeats this requirement on the actual native curve adaptively.
            for (int segment = 0; segment < body.Count - 1; segment++)
            for (int sample = 0; sample <= 16; sample++)
            {
                double u = (spatialDistance[segment] + sample / 16d *
                    (spatialDistance[segment + 1] - spatialDistance[segment])) / TravelMetres;
                AutonomyPosition p = Body(u), a = Aim(u);
                double reach = p.Distance(a);
                if (reach < 2 || reach > 180 || p.HorizontalDistance(a) < .75)
                    throw new ArgumentOutOfRangeException(nameof(aims), "Spatial routes need a finite, separated environmental aim.");
            }
            VisualChange = EstimateVisualChange();
            DurationSeconds = ScenicPacingPolicy.ProposedSeconds(feature, role, TravelMetres, VisualChange);
        }

        /// <summary>Immutable retiming preserves the complete authored geometry.
        /// Capture fixtures may run for up to seven days without changing the path.
        /// Internal Journey sections can be shorter than one second: joining
        /// their certified geometry does not introduce an editorial cut.</summary>
        public ScenicShotPlan WithDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < (IsJourney ? .1 : 1) || seconds > 604800)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            var copy = (ScenicShotPlan)MemberwiseClone(); copy.DurationSeconds = seconds; copy.HasExplicitDuration = true; return copy;
        }

        public ScenicShotPlan WithStableWaterClearance(bool enabled = true)
        { var copy = (ScenicShotPlan)MemberwiseClone(); copy.StableWaterClearance = enabled; return copy; }

        /// <summary>Attach measured pre-admission timing without changing a
        /// camera point, aim or lens. An authored duration always remains the
        /// owner's choice; a restored fixture retains its recorded profile.</summary>
        public ScenicShotPlan WithPacingProfile(ScenicPacingProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!IsDirected || IsPictureOnly || IsPan || IsJourney)
                throw new InvalidOperationException("Only ordinary moving scenery accepts measured pacing.");
            var copy = (ScenicShotPlan)MemberwiseClone(); copy.PacingProfile = profile;
            if (!HasExplicitDuration) copy.DurationSeconds = profile.DurationSeconds;
            return copy;
        }

        public ScenicShotPlan WithIdentity(long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            var copy = (ScenicShotPlan)MemberwiseClone(); copy.Id = id; return copy;
        }

        public ScenicShotPlan WithRejectedContinuation(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejected continuation needs observed evidence.", nameof(reason));
            var copy = (ScenicShotPlan)MemberwiseClone(); copy.ContinuationRejection = reason; return copy;
        }

        /// <summary>A bounded, validated window belonging to one continuous
        /// environmental journey. The predecessor is checked again at the live
        /// seam; this flag is not permission to jump to an arbitrary new path.</summary>
        public ScenicShotPlan AsJourney(long journeyId, long predecessor, double seconds)
        {
            if (!IsSpatial || IsPictureOnly || journeyId <= 0 || predecessor <= 0 || predecessor == Id)
                throw new ArgumentOutOfRangeException(nameof(journeyId));
            var copy = (ScenicShotPlan)MemberwiseClone();
            copy.JourneyId = journeyId; copy.ContinuationOf = predecessor; copy.ConstantSpeed = copy.StableWaterClearance = true;
            return copy.WithDuration(seconds);
        }

        /// <summary>A separately composed and fully validated departure cut.
        /// It belongs to the journey without claiming continuity with the
        /// preceding ordinary shot's position, gaze or motion tangent.</summary>
        public ScenicShotPlan AsJourneyDeparture(long journeyId, double seconds)
        {
            if (!IsSpatial || IsPictureOnly || journeyId <= 0)
                throw new ArgumentOutOfRangeException(nameof(journeyId));
            var copy = (ScenicShotPlan)MemberwiseClone();
            copy.JourneyId = journeyId; copy.ContinuationOf = 0; copy.ConstantSpeed = copy.StableWaterClearance = true;
            return copy.WithDuration(seconds);
        }

        public ScenicShotPlan WithConstantSpeed(bool enabled = true)
        { var copy = (ScenicShotPlan)MemberwiseClone(); copy.ConstantSpeed = enabled; return copy; }

        public ScenicShotPlan AsStandalone()
        { var copy = (ScenicShotPlan)MemberwiseClone(); copy.JourneyId = copy.ContinuationOf = 0; return copy; }

        public ScenicShotPlan WithJourneyRamps(bool easeIn, bool easeOut)
        { var copy = (ScenicShotPlan)MemberwiseClone(); copy.JourneyEaseIn = easeIn; copy.JourneyEaseOut = easeOut; return copy; }

        public double MotionFraction(double progress)
        {
            if (ConstantSpeed) return ScenicJourneyMotion.Progress(progress, JourneyEaseIn, JourneyEaseOut);
            double fraction = ScenicMotionProfile.Progress(progress);
            return PacingProfile?.DistanceAt(fraction) ?? fraction;
        }

        /// <summary>Geometry-only distance/parameter sample. Native trajectory
        /// construction and timing measurement must not reshape a path by
        /// accidentally applying its presentation speed profile to knot input.</summary>
        public ScenicShotSample RouteSampleAt(double fraction)
        {
            if (!IsDirected || double.IsNaN(fraction) || double.IsInfinity(fraction))
                throw new ArgumentOutOfRangeException(nameof(fraction));
            double u = Math.Max(0, Math.Min(1, fraction));
            return new ScenicShotSample(Body(u), Aim(u), screenX, screenY, "Measured route geometry");
        }

        public AutonomyPosition CameraWaypoint(int index) => spatialBody != null && index >= 0 && index < spatialBody.Length
            ? spatialBody[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public AutonomyPosition AimWaypoint(int index) => spatialAim != null && index >= 0 && index < spatialAim.Length
            ? spatialAim[index] : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>Normalized distance to a measured viewpoint in the authored
        /// polyline. Native solvers remap their own segment lengths onto these
        /// intervals so body and aim reach their paired endpoints together.</summary>
        public double WaypointFraction(int index) => spatialDistance != null && index >= 0 && index < spatialDistance.Length
            ? spatialDistance[index] / TravelMetres : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>Sample the aim by route distance, without applying the time
        /// profile a second time. The native solver uses this after remapping
        /// native curve distance to the authored viewpoint interval.</summary>
        public AutonomyPosition RouteAimAt(double fraction)
        {
            if (!IsDirected) throw new InvalidOperationException("Only directed routes have an authored distance aim.");
            if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction < 0 || fraction > 1)
                throw new ArgumentOutOfRangeException(nameof(fraction));
            return Aim(fraction);
        }

        /// <summary>Restrict a route to its useful prefix. This is a new route
        /// definition and must pass native-curve/visibility validation again.</summary>
        public ScenicShotPlan Trim(double endFraction) => Slice(0, endFraction);

        /// <summary>Extract an authored route interval and develop it as a new
        /// finite shot with its own smooth entry/exit. Fractions use the source
        /// plan's motion profile. The linear/quadratic body and linear aim are
        /// restricted exactly; regenerated native spline geometry must still
        /// receive complete pose/corridor validation before presentation.</summary>
        public ScenicShotPlan Slice(double startFraction, double endFraction)
        {
            if (IsPictureOnly) throw new InvalidOperationException("A single picture has no movement interval to slice.");
            if (!IsDirected) throw new InvalidOperationException("Only directed routes can be restricted.");
            if (double.IsNaN(startFraction) || double.IsInfinity(startFraction) || startFraction < 0 || startFraction >= 1)
                throw new ArgumentOutOfRangeException(nameof(startFraction));
            if (double.IsNaN(endFraction) || double.IsInfinity(endFraction) || endFraction <= startFraction || endFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(endFraction));
            double from = MotionFraction(startFraction), to = MotionFraction(endFraction);
            if (IsSpatial)
            {
                var body = new List<AutonomyPosition>(WaypointCount) { Body(from) };
                var aims = new List<AutonomyPosition>(WaypointCount) { Aim(from) };
                for (int i = 1; i < WaypointCount - 1; i++)
                {
                    double point = spatialDistance![i] / TravelMetres;
                    if (point > from + .000001 && point < to - .000001)
                    { body.Add(spatialBody![i]); aims.Add(spatialAim![i]); }
                }
                body.Add(Body(to)); aims.Add(Aim(to));
                return RetainEndEvidence(Spatial(Id, Feature!, Role, body, aims, FieldOfView, screenX, screenY, UseNativeSpline), endFraction);
            }
            if (IsPan) return RetainEndEvidence(Pan(Id, Feature!, Role, cameraStart, Aim(from), Aim(to), FieldOfView, screenX, screenY), endFraction);
            // Quadratic blossom at (from,to) is the middle control of the
            // restricted curve. Keeping the old control would distort an
            // interior interval even if both selected endpoints were correct.
            AutonomyPosition? control = curved
                ? Lerp(Lerp(cameraStart, cameraControl, from), Lerp(cameraControl, cameraEnd, from), to)
                : (AutonomyPosition?)null;
            return RetainEndEvidence(Directed(Id, Feature!, Role, Body(from), Body(to), Lerp(start, end, from), Lerp(start, end, to),
                FieldOfView, screenX, screenY, control, UseNativeSpline), endFraction);
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
            if (IsPictureOnly)
                return RetainPlayback(Picture(Id, Feature!, Role, Raised(cameraStart, metres), start, FieldOfView, screenX, screenY));
            if (IsPan) return RetainPlayback(Pan(Id, Feature!, Role, Raised(cameraStart, metres), start, end, FieldOfView, screenX, screenY));
            if (IsSpatial)
            {
                var body = new AutonomyPosition[WaypointCount];
                for (int i = 0; i < body.Length; i++) body[i] = Raised(spatialBody![i], metres);
                var raised = Spatial(Id, Feature!, Role, body, spatialAim!, FieldOfView, screenX, screenY, UseNativeSpline);
                return IsJourney ? RetainEndEvidence((ContinuationOf == 0 ? raised.AsJourneyDeparture(JourneyId, DurationSeconds) :
                    raised.AsJourney(JourneyId, ContinuationOf, DurationSeconds))
                    .WithJourneyRamps(JourneyEaseIn, JourneyEaseOut), 1) : RetainPlayback(raised);
            }
            return RetainPlayback(Directed(Id, Feature!, Role, Raised(cameraStart, metres), Raised(cameraEnd, metres),
                start, end, FieldOfView, screenX, screenY,
                curved ? Raised(cameraControl, metres) : (AutonomyPosition?)null, UseNativeSpline));
        }

        private ScenicShotPlan RetainPlayback(ScenicShotPlan replacement)
        {
            // A newly fitted automatic route must measure its new view angles.
            // Explicit fixtures keep both the authored duration and speed map.
            if (HasExplicitDuration)
            {
                replacement = replacement.WithDuration(DurationSeconds);
                if (PacingProfile != null) replacement = replacement.WithPacingProfile(PacingProfile);
            }
            return RetainEndEvidence(replacement.WithConstantSpeed(ConstantSpeed)
                .WithJourneyRamps(JourneyEaseIn, JourneyEaseOut), 1);
        }

        private ScenicShotPlan RetainEndEvidence(ScenicShotPlan replacement, double endFraction)
        {
            replacement = replacement.WithStableWaterClearance(StableWaterClearance);
            return HasRejectedContinuation && endFraction >= 1 - .000001
                ? replacement.WithRejectedContinuation(ContinuationRejection) : replacement;
        }

        /// <summary>Bounded non-looping movement starts/ends at rest. A paused
        /// caller samples the same progress; previews never advance state.
        /// Strength zero and dynamic=false retain the initial world viewpoint.</summary>
        public ScenicShotSample Sample(double progress, float strength = 1, bool dynamic = true)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress) || !Finite(strength) || strength < 0)
                throw new ArgumentOutOfRangeException(nameof(progress));
            double t = Math.Max(0, Math.Min(1, progress));
            double ease = MotionFraction(t);
            if (IsDirected)
            {
                if (IsPictureOnly)
                    return new ScenicShotSample(cameraStart, start, screenX, screenY, "Settled composition while cinematic movement is prepared");
                // Runtime strength alters the caller's progress clock, never
                // the geometry which passed acquisition validation.
                double u = dynamic && strength > 0 ? ease : 0;
                return new ScenicShotSample(Body(u), Aim(u), screenX, screenY,
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
            if (IsSpatial)
            {
                int segment = SpatialSegment(u, out double fraction);
                return Lerp(spatialBody![segment], spatialBody[segment + 1], fraction);
            }
            if (!curved) return Lerp(cameraStart, cameraEnd, u);
            double a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, c = u * u;
            return Position(a * cameraStart.X + b * cameraControl.X + c * cameraEnd.X,
                a * cameraStart.Y + b * cameraControl.Y + c * cameraEnd.Y,
                a * cameraStart.Z + b * cameraControl.Z + c * cameraEnd.Z);
        }
        private AutonomyPosition Aim(double u)
        {
            if (!IsSpatial) return Lerp(start, end, u);
            int segment = SpatialSegment(u, out double fraction);
            // Journey body and aim advance together. Per-knot aim easing would
            // oscillate even a constant camera-to-aim offset during a traverse.
            if (IsJourney) return Lerp(spatialAim![segment], spatialAim[segment + 1], fraction);
            // Bounded C1 interpolation avoids an abrupt pan at a waypoint and
            // cannot overshoot the measured aim endpoints.
            double eased = fraction * fraction * (3 - 2 * fraction);
            return Lerp(spatialAim![segment], spatialAim[segment + 1], eased);
        }
        private int SpatialSegment(double u, out double fraction)
        {
            double distance = Math.Max(0, Math.Min(1, u)) * TravelMetres;
            int segment = 0;
            while (segment < WaypointCount - 2 && distance > spatialDistance![segment + 1]) segment++;
            fraction = (distance - spatialDistance![segment]) / (spatialDistance[segment + 1] - spatialDistance[segment]);
            return segment;
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
            int samples = IsSpatial ? (WaypointCount - 1) * 8 : 16;
            for (int i = 1; i <= samples; i++)
            {
                // Include each measured turn even when it lies on a short leg;
                // a global fixed grid can skip it and undercount development.
                int segment = IsSpatial ? (i - 1) / 8 : 0;
                double u = IsSpatial ? (spatialDistance![segment] + ((i - 1) % 8 + 1) / 8d *
                    (spatialDistance[segment + 1] - spatialDistance[segment])) / TravelMetres : i / 16d;
                AutonomyPosition b = Body(u), targetB = Aim(u);
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
