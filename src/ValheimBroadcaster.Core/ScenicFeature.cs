using System;

namespace ValheimBroadcaster.Core
{
    public enum ScenicFeatureKind { Landscape, Ridge, Coastline, Clearing, Rock, Structure }
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

        public ScenicFeature(string id, ScenicFeatureKind kind, AutonomyPosition center,
            AutonomyPosition direction, float radius, float height, float confidence,
            string? parentId = null, string? scopeLabel = null, bool isPartial = false)
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
            Id = id; Kind = kind; Center = center; Radius = radius; Height = height; Confidence = confidence;
            ParentId = parentId; ScopeLabel = scopeLabel; IsPartial = isPartial;
            Direction = new AutonomyPosition((float)(direction.X / length), 0, (float)(direction.Z / length));
        }

        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
