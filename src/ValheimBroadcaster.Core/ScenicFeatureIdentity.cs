using System;

namespace ValheimBroadcaster.Core;

/// <summary>Unify a legacy piece-cluster observation with an already retained
/// measured building. Never merge hierarchy levels or merely nearby subjects.</summary>
public static class ScenicFeatureIdentity
{
    public static bool SameMeasuredStructure(ScenicFeature a, ScenicFeature b)
    {
        if (a == null || b == null) throw new ArgumentNullException(a == null ? nameof(a) : nameof(b));
        if (a.Kind != ScenicFeatureKind.Structure || b.Kind != ScenicFeatureKind.Structure ||
            a.SubjectKind != ScenicSubjectKind.Environment || b.SubjectKind != ScenicSubjectKind.Environment ||
            a.ParentId == b.Id || b.ParentId == a.Id || !a.MeasuredBounds.HasValue || !b.MeasuredBounds.HasValue)
            return false;
        // Only reconcile two observation sources: persistent building versus
        // unscoped legacy cluster. Districts, rooms and sibling buildings stay distinct.
        if (!(a.ScopeLabel == "Building" && b.ScopeLabel == null || b.ScopeLabel == "Building" && a.ScopeLabel == null))
            return false;
        PlaceBounds x = a.MeasuredBounds.Value, y = b.MeasuredBounds.Value;
        double overlapX = Math.Max(0, Math.Min(x.Maximum.X, y.Maximum.X) - Math.Max(x.Minimum.X, y.Minimum.X));
        double overlapZ = Math.Max(0, Math.Min(x.Maximum.Z, y.Maximum.Z) - Math.Max(x.Minimum.Z, y.Minimum.Z));
        double union = x.Width * x.Depth + y.Width * y.Depth - overlapX * overlapZ;
        return union > 0 && overlapX * overlapZ / union >= .7 && x.VerticalGap(y) <= .25 &&
            Math.Abs(x.Maximum.Y - y.Maximum.Y) <= 2 &&
            Math.Abs(x.Center.X - y.Center.X) <= 2 && Math.Abs(x.Center.Z - y.Center.Z) <= 2;
    }
}
