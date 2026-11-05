using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ValheimBroadcaster.Core;

internal static class JourneyViewTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey accepts observed forest surfaces instead of requiring clear rays to a synthetic aim", ForestContext);
        yield return ("Journey distinguishes a visible facade from a near opaque wall", SurfaceDepth);
        yield return ("Journey rejects near opaque view coverage and complete foreground foliage veils", ObscuredView);
        yield return ("Journey permits ordinary peripheral parallax while retaining useful angular sectors", PeripheralContext);
        yield return ("Journey never promotes missing or saturated angular evidence into a readable view", UnknownEvidence);
        yield return ("Journey body and retained-vegetation failures remain hard regardless of image score", PhysicalPriority);
        yield return ("Journey ranks deeper open views above readable clutter without requiring every ray clear", Preference);
        yield return ("Recorded 0.8.50 hidden focus is not a near view obstruction and cannot substitute for missing grid evidence", RecordedFocus);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static JourneyViewSample[] Grid(double solid = JourneyViewPolicy.ObservationRange,
        double foliage = JourneyViewPolicy.FoliageRange)
    {
        var samples = new JourneyViewSample[JourneyViewPolicy.SampleCount];
        for (int i = 0; i < samples.Length; i++) samples[i] = new JourneyViewSample(solid, foliage);
        return samples;
    }
    private static JourneyViewPolicy.Result Evaluate(JourneyViewSample[] samples) =>
        JourneyViewPolicy.Evaluate(samples, true, true, false, .5);

    private static void ForestContext()
    {
        // Constructed angular observations, not an asserted replay of missing
        // Unity physics. A central trunk and deeper forest are useful subjects.
        var samples = Grid(8);
        samples[7] = new JourneyViewSample(3, JourneyViewPolicy.FoliageRange);
        samples[0] = samples[4] = new JourneyViewSample(5, 4);
        var result = Evaluate(samples);
        Check(result.Readable && result.NearOcclusion == 0 && result.UsefulColumns == 5,
            "Visible forest surfaces were treated as failed mandatory sightlines");
    }

    private static void SurfaceDepth()
    {
        Check(Evaluate(Grid(4)).Readable, "A visible wall four metres away was mistaken for camera blockage");
        Check(Evaluate(Grid(3, 5)).Readable && Evaluate(Grid(3, 5)).FoliageCoverage == 0,
            "Leaves behind an opaque facade incorrectly obscured the visible facade");
        Check(Evaluate(Grid(3, 2)).Status == JourneyViewStatus.Obscured,
            "Foreground leaves were incorrectly hidden by a farther opaque facade");
        Check(Evaluate(Grid(.8)).Status == JourneyViewStatus.Obscured, "A near opaque wall filled the accepted picture");
        Check(JourneyViewPolicy.SurfaceIsNearOcclusion(1.7, 1.5), "Near-plane-dependent margin was ignored");
        Check(!JourneyViewPolicy.SurfaceIsNearOcclusion(4, .5), "Meaningful local surface was labelled near occlusion");
    }

    private static void ObscuredView()
    {
        Check(Evaluate(Grid(12, 2.5)).Status == JourneyViewStatus.Obscured,
            "Dense foreground vegetation across the picture was accepted");
        var samples = Grid();
        for (int row = 0; row < 3; row++) for (int col = 1; col <= 3; col++)
            samples[row * 5 + col] = new JourneyViewSample(.7, 6);
        Check(Evaluate(samples).Status == JourneyViewStatus.Obscured,
            "Near central opaque coverage was hidden by empty picture corners");
    }

    private static void PeripheralContext()
    {
        var samples = Grid();
        for (int row = 0; row < 3; row++)
        { samples[row * 5] = new JourneyViewSample(1, 6); samples[row * 5 + 4] = new JourneyViewSample(12, 3); }
        var result = Evaluate(samples);
        Check(result.Readable && result.NearOcclusion > 0 && result.FoliageCoverage > 0 && result.UsefulColumns == 4,
            "A bounded peripheral tree/foliage passage vetoed the whole travelling composition");
    }

    private static void UnknownEvidence()
    {
        Check(JourneyViewPolicy.Evaluate(null, true, true, false, .5).Status == JourneyViewStatus.Unknown,
            "Missing angular grid authorized a picture");
        var samples = Grid(); samples[4] = new JourneyViewSample(12, 6, false);
        Check(Evaluate(samples).Status == JourneyViewStatus.Unknown, "One saturated/unloaded ray was silently ignored");
        samples[4] = new JourneyViewSample(double.NaN, 6);
        Check(Evaluate(samples).Status == JourneyViewStatus.Unknown, "Invalid evidence became geometry");
        samples[4] = new JourneyViewSample(28, 6);
        Check(Evaluate(samples).Status == JourneyViewStatus.Unknown, "A distant focus ray masqueraded as bounded local grid evidence");
        Check(JourneyViewPolicy.Evaluate(Grid(), true, false, false, .5).Status == JourneyViewStatus.Unknown,
            "Incomplete foliage survey became open space");
    }

    private static void PhysicalPriority()
    {
        Check(JourneyViewPolicy.Evaluate(Grid(), false, true, false, .5).Status == JourneyViewStatus.CameraBlocked,
            "A high quality grid overrode body or near-plane collision");
        Check(JourneyViewPolicy.Evaluate(null, true, false, true, .5).Status == JourneyViewStatus.CameraBlocked,
            "Missing grid evidence hid a measured camera/vegetation intersection");
    }

    private static void Preference()
    {
        var open = Evaluate(Grid()); var cluttered = Evaluate(Grid(4));
        Check(open.Readable && cluttered.Readable && open.Score > cluttered.Score && open.MeanDepth == 12,
            "Depth observations neither distinguish useful alternatives nor preserve clear query range");
    }

    private static void RecordedFocus()
    {
        using var fixture = Fixture();
        JsonElement root = fixture.RootElement, veto = root.GetProperty("firstFocusVeto");
        Check(root.GetProperty("sourceVersion").GetString() == "0.8.50" &&
            veto.GetProperty("focusInFrame").GetBoolean() && !veto.GetProperty("focusVisible").GetBoolean() &&
            veto.GetProperty("physicalCameraChecksPassed").GetBoolean() && veto.GetProperty("foreground").GetDouble() == 0,
            "The recorded defect is no longer the clear-body, in-frame, distant-focus veto");
        Check(!JourneyViewPolicy.SurfaceIsNearOcclusion(veto.GetProperty("focusHitDistance").GetDouble(),
            veto.GetProperty("near").GetDouble()), "Recorded distant visible surface still counted as a near opaque obstruction");
        Check(!root.GetProperty("angularGridCaptured").GetBoolean() &&
            JourneyViewPolicy.Evaluate(null, true, true, false, veto.GetProperty("near").GetDouble()).Status == JourneyViewStatus.Unknown,
            "A legacy focus ray was misrepresented as a complete newly accepted angular view");
    }

    private static JsonDocument Fixture()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "tests", "fixtures", "journey-mission", "0.8.50-focus-veto.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Sanitized 0.8.50 focus-veto fixture is unavailable");
    }
}
