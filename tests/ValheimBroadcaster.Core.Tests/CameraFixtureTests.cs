using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ValheimBroadcaster.Core;

internal static class CameraFixtureTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Camera fixtures round-trip exact curved route, lens, composition and subject evidence", CurvedRoundTrip);
        yield return ("Camera fixtures preserve every spatial waypoint and paired aim", SpatialRoundTrip);
        yield return ("Camera fixtures preserve finite pans without inventing translation", PanRoundTrip);
        yield return ("Saved journey windows replay independently with the original constant-speed profile", JourneyWindow);
        yield return ("Camera fixture parsing rejects corruption, truncation, trailing data and newer schema", Corruption);
        yield return ("Camera fixture parsing rejects non-finite playback settings even with a valid checksum", InvalidSettings);
        yield return ("Camera fixtures require matching world UID rather than a world display name", WorldIdentity);
        yield return ("Camera fixture metadata rejects path traversal and invalid playback inputs", MetadataBounds);
        yield return ("Camera fixture store persists separately by world and loads transactionally", StoreWorlds);
        yield return ("Camera fixture atomic replacement preserves the previous file and requires explicit replacement", AtomicReplacement);
        yield return ("Camera fixture store bounds its saved collection and file size", StoreBounds);
    }

    private static AutonomyPosition P(float x, float y, float z) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature() => new ScenicFeature("coast-site", ScenicFeatureKind.Coastline,
        P(10, 3, 10), P(1, 0, 0), 12, 6, .9f, "district", "Coast approach", false,
        ScenicSubjectKind.SiteContext, "site-42", new PlaceBounds(P(4, 1, 4), P(16, 7, 16)),
        new[] { P(6, 4, 6), P(14, 4, 14) }, P(0, 0, 1),
        new[] { new PlaceBounds(P(4, 1, 4), P(8, 7, 8)), new PlaceBounds(P(12, 1, 12), P(16, 7, 16)) });
    private static ScenicShotPlan Curved() => ScenicShotPlan.Directed(123, Feature(), ScenicShotRole.Reveal,
        P(0, 8, -15), P(20, 9, -12), P(10, 3, 10), P(12, 5, 10), 61, .12f, -.07f,
        P(8, 12, -20), true).WithDuration(120);
    private static CameraFixture Fixture(ScenicShotPlan? plan = null, long world = 1234567, string name = "coast-reveal") =>
        new CameraFixture(world, name, "0.8.37+fixture-test", "motion=0.5;dynamic=True;seed=42", new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc),
            .5f, true, plan ?? Curved());
    private static CameraFixture RoundTrip(CameraFixture fixture) => CameraFixtureCodec.Decode(CameraFixtureCodec.Encode(fixture), fixture.WorldId);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException || error is InvalidDataException || error is InvalidOperationException || error is IOException) { return; }
        throw new Exception("Invalid fixture input was accepted.");
    }
    private static void SamePlan(ScenicShotPlan expected, ScenicShotPlan actual)
    {
        Check(expected.Id == actual.Id && expected.Role == actual.Role && expected.UseNativeSpline == actual.UseNativeSpline &&
            expected.DurationSeconds == actual.DurationSeconds && expected.FieldOfView == actual.FieldOfView &&
            expected.IsPan == actual.IsPan && expected.IsSpatial == actual.IsSpatial && expected.ConstantSpeed == actual.ConstantSpeed,
            "Playback identity or timing/lens changed on reload.");
        for (int i = 0; i <= 100; i++)
        {
            ScenicShotSample a = expected.Sample(i / 100d), b = actual.Sample(i / 100d);
            Check(a.CameraPosition.Distance(b.CameraPosition) == 0 && a.AimPosition.Distance(b.AimPosition) == 0 &&
                a.ScreenX == b.ScreenX && a.ScreenY == b.ScreenY, "A camera sample changed on reload.");
        }
    }
    private static void CurvedRoundTrip()
    {
        CameraFixture source = Fixture(), loaded = RoundTrip(source);
        SamePlan(source.Plan, loaded.Plan);
        Check(loaded.Plan.CameraControl!.Value.Distance(source.Plan.CameraControl!.Value) == 0 &&
            loaded.Plan.Feature!.MeasuredBounds!.Value.Minimum.Distance(P(4, 1, 4)) == 0 &&
            loaded.Plan.Feature.MemberPoints.Count == 2 && loaded.Plan.Feature.MemberBounds.Count == 2 &&
            loaded.Plan.Feature.SubjectIdentity == "site-42" && loaded.Plan.Feature.ParentId == "district" &&
            loaded.Plan.Feature.WaterDirection!.Value.Distance(P(0, 0, 1)) == 0,
            "Subject geometry or camera control was discarded.");
        Check(source.Name == loaded.Name && source.CreatedUtc == loaded.CreatedUtc && source.SourceVersion == loaded.SourceVersion &&
            source.SettingsSummary == loaded.SettingsSummary && source.MotionMultiplier == loaded.MotionMultiplier && loaded.DynamicMovement,
            "Fixture metadata was not preserved.");
        Check(CameraFixtureCodec.Encode(source).SequenceEqual(CameraFixtureCodec.Encode(loaded)), "Canonical fixture encoding changed on round trip.");
    }
    private static void SpatialRoundTrip()
    {
        var plan = ScenicShotPlan.Spatial(98, Feature(), ScenicShotRole.Traverse,
            new[] { P(0, 9, -20), P(4, 10, -22), P(12, 9, -18), P(20, 8, -15) },
            new[] { P(8, 4, 8), P(10, 5, 9), P(13, 4, 11), P(15, 5, 12) }, 57, -.1f, .06f, false).WithDuration(74);
        CameraFixture loaded = RoundTrip(Fixture(plan)); SamePlan(plan, loaded.Plan);
        for (int i = 0; i < plan.WaypointCount; i++)
            Check(plan.CameraWaypoint(i).Distance(loaded.Plan.CameraWaypoint(i)) == 0 &&
                plan.AimWaypoint(i).Distance(loaded.Plan.AimWaypoint(i)) == 0, "A spatial waypoint moved or was omitted.");
    }
    private static void PanRoundTrip()
    {
        var plan = ScenicShotPlan.Pan(95, Feature(), ScenicShotRole.Establish, P(0, 10, -30), P(10, 4, 10), P(16, 4, 10), 65).WithDuration(180);
        CameraFixture loaded = RoundTrip(Fixture(plan)); SamePlan(plan, loaded.Plan);
        Check(loaded.Plan.TravelMetres == 0 && !loaded.Plan.UseNativeSpline, "A stationary pan acquired translational movement.");
    }
    private static void JourneyWindow()
    {
        ScenicShotPlan plan = ScenicShotPlan.Spatial(123, Feature(), ScenicShotRole.Traverse,
            new[] { P(0, 9, -20), P(10, 10, -22), P(20, 9, -18) },
            new[] { P(8, 4, 8), P(10, 5, 9), P(13, 4, 11) }, 57).AsJourney(23, 122, 22).WithJourneyRamps(true, false);
        CameraFixture fixture = Fixture(plan), loaded = RoundTrip(fixture);
        Check(!fixture.Plan.IsJourney && !loaded.Plan.IsJourney && fixture.Plan.ConstantSpeed && loaded.Plan.ConstantSpeed &&
            loaded.SourceJourneyId == 23 && loaded.SourcePredecessorId == 122 && loaded.Plan.JourneyEaseIn && !loaded.Plan.JourneyEaseOut,
            "Saved window claimed continuity with a prior runtime shot or lost linear motion.");
        SamePlan(fixture.Plan, loaded.Plan);
    }
    private static void Corruption()
    {
        byte[] bytes = CameraFixtureCodec.Encode(Fixture());
        var changed = (byte[])bytes.Clone(); changed[changed.Length / 2] ^= 1;
        Reject(() => CameraFixtureCodec.Decode(changed, 1234567));
        Reject(() => CameraFixtureCodec.Decode(bytes.Take(bytes.Length - 1).ToArray(), 1234567));
        changed = (byte[])bytes.Clone(); changed[4] = 99; Rehash(changed);
        Reject(() => CameraFixtureCodec.Decode(changed, 1234567));
        changed = new byte[bytes.Length + 1]; Array.Copy(bytes, changed, bytes.Length - 32); changed[bytes.Length - 32] = 77; Rehash(changed);
        Reject(() => CameraFixtureCodec.Decode(changed, 1234567));
        Reject(() => CameraFixtureCodec.Decode(new byte[CameraFixtureCodec.MaximumBytes + 1], 1234567));
    }
    private static void InvalidSettings()
    {
        byte[] bytes = CameraFixtureCodec.Encode(Fixture());
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 16;
            for (int i = 0; i < 3; i++) { int size = reader.ReadUInt16(); stream.Position += size; }
            stream.Position += 8;
            byte[] nan = BitConverter.GetBytes(float.NaN); Array.Copy(nan, 0, bytes, (int)stream.Position, 4);
        }
        Rehash(bytes); Reject(() => CameraFixtureCodec.Decode(bytes, 1234567));
    }
    private static void WorldIdentity()
    {
        byte[] bytes = CameraFixtureCodec.Encode(Fixture());
        Reject(() => CameraFixtureCodec.Decode(bytes, 1234568)); Reject(() => CameraFixtureCodec.Decode(bytes, 0));
        Reject(() => Fixture().RequireWorld(77)); Fixture().RequireWorld(1234567);
    }
    private static void MetadataBounds()
    {
        foreach (string name in new[] { "../coast", "C:\\coast", "coast/one", "coast.one", "", " spaced ", "-dash", new string('a', 49) })
            Reject(() => Fixture(name: name));
        Check(Fixture(name: "Coast_One-2").Name == "coast_one-2", "Names are not portable across case-sensitive and insensitive filesystems.");
        Reject(() => Fixture(world: 0));
        Reject(() => new CameraFixture(1, "bad", "0.8.37", "", DateTime.UtcNow, float.NaN, true, Curved()));
        Reject(() => new CameraFixture(1, "bad", "0.8.37\nother", "", DateTime.UtcNow, 1, true, Curved()));
        Reject(() => Fixture(ScenicShotPlan.Picture(1, Feature(), ScenicShotRole.Establish, P(0, 10, -20), P(10, 4, 10), 60)));
    }
    private static void InDirectory(Action<string> run)
    {
        string directory = Path.Combine(Path.GetTempPath(), "valheim-fixture-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { run(directory); } finally { Directory.Delete(directory, true); }
    }
    private static void StoreWorlds() => InDirectory(directory =>
    {
        var store = new CameraFixtureStore(directory);
        store.Save(Fixture(world: 11)); store.Save(Fixture(world: 22));
        Check(store.Load(11, "Coast-Reveal").WorldId == 11 && store.Load(22, "coast-reveal").WorldId == 22 &&
            store.List(11).Count == 1 && store.List(33).Count == 0, "Different world UIDs shared fixture contents.");
        Reject(() => store.Load(11, "../coast-reveal"));
        Check(store.Delete(11, "coast-reveal") && !store.Delete(11, "coast-reveal") && store.List(22).Count == 1,
            "Deleting one world fixture altered another world.");
    });
    private static void AtomicReplacement() => InDirectory(directory =>
    {
        var store = new CameraFixtureStore(directory); string path = store.Save(Fixture()); byte[] first = File.ReadAllBytes(path);
        Reject(() => store.Save(Fixture(Curved().WithDuration(200))));
        Check(File.ReadAllBytes(path).SequenceEqual(first), "Failed replacement damaged the saved fixture.");
        store.Save(Fixture(Curved().WithDuration(200)), true);
        Check(store.Load(1234567, "coast-reveal").Plan.DurationSeconds == 200 && File.ReadAllBytes(path + ".bak").SequenceEqual(first),
            "Atomic replacement did not preserve the previous file.");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Completed write leaked a temporary file.");
    });
    private static void StoreBounds() => InDirectory(directory =>
    {
        var store = new CameraFixtureStore(directory);
        for (int i = 0; i < CameraFixtureStore.MaximumFixturesPerWorld; i++) store.Save(Fixture(name: "shot" + i));
        Reject(() => store.Save(Fixture(name: "overflow")));
        string path = store.Save(Fixture(name: "shot0"), true);
        File.WriteAllBytes(path, new byte[CameraFixtureCodec.MaximumBytes + 1]);
        Reject(() => store.Load(1234567, "shot0"));
        Check(store.List(1234567).Count == CameraFixtureStore.MaximumFixturesPerWorld, "A failed save altered the collection bound.");
    });
    private static void Rehash(byte[] bytes)
    {
        using var sha = SHA256.Create(); byte[] hash = sha.ComputeHash(bytes, 0, bytes.Length - 32);
        Array.Copy(hash, 0, bytes, bytes.Length - 32, 32);
    }
}
