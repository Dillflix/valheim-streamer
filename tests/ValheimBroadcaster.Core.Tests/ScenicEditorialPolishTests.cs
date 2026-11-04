using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicEditorialPolishTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Final scenic envelopes reject visible-center but cropped establishing views", FinalEnvelope);
        yield return ("Scenic member coverage scales independently of a visible parent midpoint", MemberEvidence);
        yield return ("Scenic immutable subject observations cannot change with survey buffers", ImmutableMembers);
        yield return ("Confined scenic pan is finite useful angular motion with no translation", ConfinedPan);
        yield return ("Scenic child footage cannot falsely establish the whole district", HonestHierarchy);
        yield return ("Observed scenic entrances require sustained building context", EntranceSequence);
        yield return ("Scenic appearance records sustained shown footage and resets per session", AppearanceEvidence);
        yield return ("Appearance preference changes comparable real timestamp revisits but preserves novelty", AppearanceRevisits);
        yield return ("Coast normal remains horizontal normalized measured evidence", CoastEvidence);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature(string id, string? parent = null, string? scope = null) =>
        new ScenicFeature(id, ScenicFeatureKind.Structure, P(0, 4, 0), P(0, 0, 1), 6, 6, .9f, parent, scope);
    private static ShotProjection.Envelope Envelope(float halfWidth, float height, float depth)
    {
        var points = new List<ShotProjection.Point>();
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            points.Add(new ShotProjection.Point(x * halfWidth, y * height, depth + z));
        return ShotProjection.MeasureEnvelope(points, 65, 16d / 9, .1, .10, .025, .02);
    }
    private static void FinalEnvelope()
    {
        Check(ScenicSubjectFraming.Evaluate(Envelope(3, 3, 16), ScenicShotRole.Establish, out _), "Readable full subject rejected");
        Check(!ScenicSubjectFraming.Evaluate(Envelope(20, 12, 8), ScenicShotRole.Establish, out _), "Centre visibility concealed lost establishing envelope");
        Check(!ScenicSubjectFraming.Evaluate(Envelope(1, 1, 100), ScenicShotRole.Establish, out _), "A tiny distant subject earned context credit");
    }
    private static void MemberEvidence()
    {
        Check(ScenicSubjectFraming.RequiredVisibleMembers(8, ScenicShotRole.Establish) == 4, "Whole settlement only required its midpoint");
        Check(ScenicSubjectFraming.RequiredVisibleMembers(2, ScenicShotRole.Establish) == 2, "Two-building context omitted one building");
        Check(ScenicSubjectFraming.RequiredVisibleMembers(0, ScenicShotRole.Detail) == 0, "An entrance acquired unrelated sibling requirements");
    }
    private static void ImmutableMembers()
    {
        var bounds = new PlaceBounds(P(-2, 0, -4), P(2, 8, 4));
        var members = new List<PlaceBounds> { bounds };
        var f = new ScenicFeature("site", ScenicFeatureKind.Structure, P(0, 4, 0), P(0, 0, 1), 8, 8, .9f,
            measuredBounds: bounds, memberBounds: members);
        members.Clear();
        Check(f.MemberBounds.Count == 1 && f.MeasuredBounds!.Value.Width == 4 && f.MeasuredBounds.Value.Depth == 8,
            "Mutable survey altered admitted geometry");
    }
    private static void ConfinedPan()
    {
        var pan = ScenicShotPlan.Pan(1, Feature("room"), ScenicShotRole.Reveal, P(0, 6, -18), P(-1.2f, 4, 0), P(1.2f, 4, 0), 65);
        Check(pan.IsPan && !pan.IsPictureOnly && !pan.UseNativeSpline && pan.TravelMetres == 0 && pan.DurationSeconds >= 10,
            "Pan was mistaken for an emergency still or fabricated movement route");
        Check(pan.Sample(0).CameraPosition.Distance(pan.Sample(1).CameraPosition) < .001 &&
            pan.Sample(0).AimPosition.Distance(pan.Sample(1).AimPosition) > 2, "Pan did not develop the observed composition");
        var raised = pan.RaiseCamera(3);
        Check(raised.IsPan && raised.Sample(.5).CameraPosition.Y == pan.Sample(.5).CameraPosition.Y + 3 &&
            raised.Sample(.5).AimPosition.Distance(pan.Sample(.5).AimPosition) < .001, "Terrain fit changed pan aim/identity");
        bool rejected = false;
        try { ScenicShotPlan.Pan(2, Feature("bad"), ScenicShotRole.Reveal, P(0, 6, -18), P(-12, 4), P(12, 4), 65); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Unrestrained rotation accepted as gentle confined motion");
    }
    private static void Feed(ScenicSiteSequence sequence, ScenicFeature feature, ScenicShotRole role, double from, double seconds)
    { for (int i = 0; i <= seconds * 10; i++) sequence.Observe(from + i * .1, feature, role, true, false); }
    private static void HonestHierarchy()
    {
        var sequence = new ScenicSiteSequence();
        var district = Feature("district", scope: "District"); var building = Feature("building", "district", "Building");
        Feed(sequence, building, ScenicShotRole.Establish, 0, 7);
        Check(sequence.ContextShown(building) && !sequence.ContextShown(district), "One building falsely established the whole district");
        Feed(sequence, district, ScenicShotRole.Establish, 8, 7);
        Check(sequence.ContextShown(district), "Actual full district output did not earn context");
    }
    private static void EntranceSequence()
    {
        var sequence = new ScenicSiteSequence(); var house = Feature("house", scope: "Building"); var door = Feature("door", "house", "Opening");
        Check(!sequence.CanDevelop(door), "Door detail had no establishing context");
        Feed(sequence, house, ScenicShotRole.Establish, 0, 6.3);
        Check(sequence.CanDevelop(door) && sequence.Priority(door) > sequence.Priority(house), "Established house did not invite entrance detail");
        Feed(sequence, door, ScenicShotRole.Detail, 6.4, 4.2);
        Check(sequence.Priority(door) < 0, "Shown entrance remained a perpetual priority");
    }
    private static void AppearanceEvidence()
    {
        var memory = new ScenicAppearanceHistory();
        var day = new ScenicAppearanceKey(1, .7f, .001f, true, "Clear"); var fog = new ScenicAppearanceKey(.15f, .2f, .025f, true, "Mist");
        for (int i = 0; i < 30; i++) memory.Observe("coast", day, 1, false);
        Check(memory.Preference("coast", fog) == 0, "Unshown trip earned appearance credit");
        for (int i = 0; i < 15; i++) memory.Observe("coast", day, 1, true);
        Check(memory.Preference("coast", fog) > .1 && memory.Preference("coast", day) == 0, "Sustained appearance did not distinguish changed conditions");
        memory.Reset(); Check(memory.Preference("coast", fog) == 0, "World/session reset borrowed prior appearance credit");
    }
    private static void AppearanceRevisits()
    {
        var diversity = new ScenicDestinationDiversity();
        var older = new ScenicRegion("old", "Meadows", P(100, 0), 0);
        var changed = new ScenicRegion("changed", "Meadows", P(120, 0), 0);
        var eligible = new[] { older, changed };
        Check(diversity.Choose(eligible, P(), id => id == "old" ? 1000 : 1040, id => id == "changed" ? .18 : 0)!.Id == "changed",
            "Dimensionless bonus could not change meaningful timestamp recency");
        Check(diversity.Choose(eligible, P(), id => id == "old" ? double.NegativeInfinity : 1040, id => id == "changed" ? .18 : 0)!.Id == "old",
            "Weather novelty displaced a genuinely unvisited destination");
    }
    private static void CoastEvidence()
    {
        var f = new ScenicFeature("coast", ScenicFeatureKind.Coastline, P(), P(1, 0), 18, 3, .9f, waterDirection: P(0, -8, 12));
        Check(f.WaterDirection.HasValue && f.WaterDirection.Value.Y == 0 && f.WaterDirection.Value.Z == 1,
            "Shore normal included terrain slope or lost water-side evidence");
    }
}
