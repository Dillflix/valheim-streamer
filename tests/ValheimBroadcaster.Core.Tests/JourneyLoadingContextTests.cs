using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ValheimBroadcaster.Core;

internal static class JourneyLoadingContextTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recorded 0.8.50 fallback displacement invalidates old Journey observation reach", RecordedFallbackReach);
        yield return ("Journey loading reach retains named subjects but bounds generic travel view depth", SubjectAndTravelReach);
        yield return ("Journey loading reach checks interior path observations instead of endpoints alone", InteriorPathReach);
    }

    private static void Check(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }

    private static AutonomyPosition P(float x, float z = 0) => new AutonomyPosition(x, 0, z);
    private static ScenicFeature Feature(AutonomyPosition center) => new ScenicFeature(
        "loading-context", ScenicFeatureKind.Landscape, center, P(1), 10, 5, 1);

    private static AutonomyPosition Position(JsonElement row)
    { return new AutonomyPosition(row[0].GetSingle(), row[1].GetSingle(), row[2].GetSingle()); }

    private static JsonDocument Fixture()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "tests", "fixtures", "journey-mission", "0.8.50-focus-veto.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Sanitized 0.8.50 Journey observation fixture was not found.");
    }

    private static void RecordedFallbackReach()
    {
        using var fixture = Fixture();
        JsonElement root = fixture.RootElement, fallback = root.GetProperty("fallbackContext");
        Check(root.GetProperty("sourceVersion").GetString() == "0.8.50", "Wrong recorded failure version");
        AutonomyPosition seed = Position(fallback.GetProperty("repairSeed").GetProperty("camera"));
        AutonomyPosition aim = Position(fallback.GetProperty("repairSeed").GetProperty("aim"));
        AutonomyPosition fallbackCamera = Position(fallback.GetProperty("fallbackPicture").GetProperty("camera"));
        Check(Math.Abs(seed.Distance(fallbackCamera) - fallback.GetProperty("cameraDisplacement").GetDouble()) < .001,
            "The sanitized coordinates no longer preserve the measured fallback displacement");
        var picture = ScenicShotPlan.Picture(1, Feature(aim), ScenicShotRole.Traverse, seed, aim, 75).WithJourneyView();

        // The capture does not contain the carrier transform. These observed
        // cameras are explicitly used as synthetic carrier proxies to replay
        // the reach decision, not as proof of actual world-loading boundaries.
        Check(JourneyLoadingContext.ContainsPoint(seed, fallbackCamera),
            "Fixture no longer exposes why checking only the old origin misses the problem");
        Check(!JourneyLoadingContext.ContainsPlan(picture, fallbackCamera, JourneyViewPolicy.ObservationRange),
            "A 75-metre fallback retained planning work whose observation footprint exceeds local reach");
        Check(JourneyLoadingContext.ContainsPlan(picture, seed, JourneyViewPolicy.ObservationRange),
            "The same observation failed while the synthetic carrier remains at its origin");
        var nearby = new AutonomyPosition(seed.X + 2, seed.Y, seed.Z);
        Check(JourneyLoadingContext.ContainsPlan(picture, nearby, JourneyViewPolicy.ObservationRange),
            "Ordinary small camera movement needlessly invalidated the planning context");
    }

    private static void SubjectAndTravelReach()
    {
        var subject = ScenicShotPlan.Picture(2, Feature(P(100)), ScenicShotRole.Establish, P(10), P(100), 75);
        Check(!JourneyLoadingContext.ContainsPlan(subject, P(0), 12),
            "An unloaded named subject was treated as a generic direction");
        Check(JourneyLoadingContext.ContainsPlan(subject.WithJourneyView(), P(0), 12),
            "Generic travel context unnecessarily requires loading its artificial distant aim point");
        var nearEdge = ScenicShotPlan.Picture(3, Feature(P(80)), ScenicShotRole.Traverse, P(74), P(80), 75).WithJourneyView();
        Check(!JourneyLoadingContext.ContainsPlan(nearEdge, P(0), 12),
            "A camera inside local reach retained a view footprint outside local reach");
        Check(JourneyLoadingContext.ContainsPlan(nearEdge, P(2), 12),
            "A fully contained view footprint was rejected");
    }

    private static void InteriorPathReach()
    {
        // This is deliberately constructed geometry, not recovered world data.
        // Both endpoints have room for a 12-metre view, but the middle does not.
        var body = new[] { P(40), P(78), P(60) };
        var aim = new[] { P(40, 10), P(78, 10), P(60, 10) };
        var route = ScenicShotPlan.Spatial(4, Feature(P(60, 10)), ScenicShotRole.Traverse,
            body, aim, 75, useNativeSpline: false).WithJourneyView();
        Check(JourneyLoadingContext.ContainsPoint(route.Sample(0).CameraPosition, P(0), 12) &&
            JourneyLoadingContext.ContainsPoint(route.Sample(1).CameraPosition, P(0), 12),
            "Constructed route endpoints do not isolate the interior observation failure");
        Check(!JourneyLoadingContext.ContainsPlan(route, P(0), 12),
            "Only endpoints were considered, retaining an interior observation outside local reach");
        Check(JourneyLoadingContext.ContainsPlan(route, P(10), 12),
            "The full route was not retained when every sampled observation fits the new context");
    }
}
