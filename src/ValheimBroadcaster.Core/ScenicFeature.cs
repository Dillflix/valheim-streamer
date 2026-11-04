using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum ScenicFeatureKind { Landscape, Ridge, Coastline, Clearing, Rock, Structure }
    public enum ScenicSubjectKind { Environment, SiteContext, Npc }
    public enum ScenicShotRole { Establish, Traverse, Reveal, Detail }

    /// <summary>A measured environmental subject. Geometry and classification
    /// are supplied by loaded-world queries, never by generated map guesses.</summary>
    public sealed class ScenicFeature
    {
        public string Id { get; }
        public ScenicFeatureKind Kind { get; }
        public AutonomyPosition Center { get; }
        public AutonomyPosition Direction { get; }
        public float Radius { get; }
        public float Height { get; }
        public float Confidence { get; }
        public string? ParentId { get; }
        public string? ScopeLabel { get; }
        public bool IsPartial { get; }
        public ScenicSubjectKind SubjectKind { get; }
        public string? SubjectIdentity { get; }
        /// <summary>Immutable exposed geometry captured with this observation.
        /// A later survey must not change the envelope of an admitted shot.</summary>
        public PlaceBounds? MeasuredBounds { get; }
        public IReadOnlyList<AutonomyPosition> MemberPoints { get; }
        public IReadOnlyList<PlaceBounds> MemberBounds { get; }
        /// <summary>Observed direction from dry land towards loaded water. Null
        /// means no measured shore side; do not infer it from a map label.</summary>
        public AutonomyPosition? WaterDirection { get; }

        public ScenicFeature(string id, ScenicFeatureKind kind, AutonomyPosition center,
            AutonomyPosition direction, float radius, float height, float confidence,
            string? parentId = null, string? scopeLabel = null, bool isPartial = false,
            ScenicSubjectKind subjectKind = ScenicSubjectKind.Environment, string? subjectIdentity = null,
            PlaceBounds? measuredBounds = null, IReadOnlyList<AutonomyPosition>? memberPoints = null,
            AutonomyPosition? waterDirection = null, IReadOnlyList<PlaceBounds>? memberBounds = null)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 160) throw new ArgumentOutOfRangeException(nameof(id));
            if (!Enum.IsDefined(typeof(ScenicFeatureKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Valid(radius) || radius < 1 || radius > 120 || !Valid(height) || height < 0 || height > 120 ||
                !Valid(confidence) || confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(radius));
            double length = Math.Sqrt((double)direction.X * direction.X + (double)direction.Z * direction.Z);
            if (length < .001) throw new ArgumentOutOfRangeException(nameof(direction));
            if (parentId != null && (string.IsNullOrWhiteSpace(parentId) || parentId.Length > 160 || parentId == id) ||
                scopeLabel != null && (string.IsNullOrWhiteSpace(scopeLabel) || scopeLabel.Length > 64))
                throw new ArgumentOutOfRangeException(nameof(parentId));
            if (!Enum.IsDefined(typeof(ScenicSubjectKind), subjectKind) ||
                subjectKind != ScenicSubjectKind.Environment && string.IsNullOrWhiteSpace(subjectIdentity) ||
                subjectIdentity != null && subjectIdentity.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(subjectIdentity));
            SubjectKind = subjectKind; SubjectIdentity = subjectIdentity;
            if (memberPoints != null && memberPoints.Count > 8) throw new ArgumentOutOfRangeException(nameof(memberPoints));
            if (measuredBounds.HasValue && (measuredBounds.Value.Width <= 0 || measuredBounds.Value.Height <= 0 || measuredBounds.Value.Depth <= 0))
                throw new ArgumentOutOfRangeException(nameof(measuredBounds));
            MeasuredBounds = measuredBounds;
            var members = new AutonomyPosition[memberPoints?.Count ?? 0];
            for (int i = 0; i < members.Length; i++) members[i] = memberPoints![i];
            MemberPoints = Array.AsReadOnly(members);
            if (memberBounds != null && memberBounds.Count > 8) throw new ArgumentOutOfRangeException(nameof(memberBounds));
            var bounds = new PlaceBounds[memberBounds?.Count ?? 0];
            for (int i = 0; i < bounds.Length; i++) bounds[i] = memberBounds![i];
            MemberBounds = Array.AsReadOnly(bounds);
            if (waterDirection.HasValue)
            {
                AutonomyPosition water = waterDirection.Value;
                double norm = Math.Sqrt((double)water.X * water.X + (double)water.Z * water.Z);
                if (norm < .001) throw new ArgumentOutOfRangeException(nameof(waterDirection));
                WaterDirection = new AutonomyPosition((float)(water.X / norm), 0, (float)(water.Z / norm));
            }
            Id = id; Kind = kind; Center = center; Radius = radius; Height = height; Confidence = confidence;
            ParentId = parentId; ScopeLabel = scopeLabel; IsPartial = isPartial;
            Direction = new AutonomyPosition((float)(direction.X / length), 0, (float)(direction.Z / length));
        }

        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
