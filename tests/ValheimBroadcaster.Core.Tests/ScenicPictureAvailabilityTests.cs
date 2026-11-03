using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicPictureAvailabilityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A measured single scenic picture needs no fabricated connected movement", IndependentPicture);
        yield return ("Picture admission does not weaken cinematic route movement requirements", SeparateRouteContract);
        yield return ("Independent pictures require current scene and fresh observation evidence", FreshEvidence);
        yield return ("Point discovery cannot interrupt a readable developing cinematic route", ProtectMotion);
        yield return ("Independent picture dwell counts matching live footage only", PresentationClock);
        yield return ("A settled first picture gets minimum dwell before a prepared route", SettledHandoff);
        yield return ("Unreadable first picture yields promptly to a ready replacement", FailedPictureHandoff);
        yield return ("Background graph discovery cannot cause picture flicker or repeated viewpoint cuts", StableNovelPictures);
        yield return ("Finite scenic fallback content exhaustion needs displayed footage and failed route search", ContentExhaustion);
        yield return ("A requested moving cut does not erase a retained first picture lease", RetainedPictureDuringCut);
        yield return ("Repeated prepared but unpresented routes cannot renew exhausted picture grace", UnpresentedRouteGrace);
        yield return ("A readable completed route has finite fallback content dwell", CompletedRoutePicture);
        yield return ("Region reset retires independent pictures and their content history", Reset);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicFeature Feature() => new ScenicFeature("coast", ScenicFeatureKind.Coastline,
        new AutonomyPosition(0, 20, 0), new AutonomyPosition(1, 0, 0), 10, 5, .9f);
    private static ScenicShotPlan Picture(long id = 1, float x = 20, float z = 0) =>
        ScenicShotPlan.Picture(id, Feature(), ScenicShotRole.Establish, new AutonomyPosition(x, 25, z),
            new AutonomyPosition(0, 20, 0), 65, .06f, -.08f);
    private static ScenicPictureAvailability New()
    {
        var policy = new ScenicPictureAvailability();
        Check(policy.TryOffer(Picture(), 3, 3, 0, 0, false), "Initial measured point was not admitted");
        return policy;
    }
    private static void Show(ScenicPictureAvailability policy, double start, double seconds, long id = 1)
    {
        int frames = (int)Math.Round(seconds * 20);
        for (int i = 0; i <= frames; i++) policy.Observe(id, start + i * .05, true, false);
    }
    private static void IndependentPicture()
    {
        var p = Picture();
        Check(p.IsPictureOnly && p.IsDirected && !p.UseNativeSpline && !p.IsSpatial && p.TravelMetres == 0 && p.VisualChange == 0,
            "Independent picture manufactured route or spline evidence");
        var first = p.Sample(0);
        // The exact failure boundary: observed clear space contains a usable
        // composition but no certified edge. The real graph search must reject
        // movement while independent availability admits that single picture.
        var graph = new CameraSpatialGraph(3, new[] { new CameraSpatialNode(0, first.CameraPosition,
            first.AimPosition, .9f, 1, 1, 0, 3) }, Array.Empty<CameraSpatialEdge>());
        var planner = new CameraSpatialPlanner();
        planner.Begin(graph, new CameraSpatialRequest(0, 3, ScenicShotRole.Establish));
        while (!planner.Complete) planner.Step(64);
        Check(planner.Result == null && New().Current != null,
            "Picture availability still depends on a connected movement route");
        for (int i = 0; i <= 10; i++)
        {
            var sample = p.Sample(i / 10d, 1.5f);
            Check(sample.CameraPosition.Distance(first.CameraPosition) == 0 && sample.AimPosition.Distance(first.AimPosition) == 0 &&
                sample.ScreenX == .06f && sample.ScreenY == -.08f, "Picture changed its measured pose or composition");
        }
        Check(p.RaiseCamera(2).IsPictureOnly && p.RaiseCamera(2).Sample(0).CameraPosition.Y == 27,
            "A proposed surface fit changed the picture contract");
    }
    private static void SeparateRouteContract()
    {
        bool invalidRoute = false, invalidDirector = false;
        try { ScenicShotPlan.Directed(1, Feature(), ScenicShotRole.Establish,
            new AutonomyPosition(20, 25, 0), new AutonomyPosition(20, 25, 0),
            new AutonomyPosition(0, 20, 0), new AutonomyPosition(0, 20, 0), 65); }
        catch (ArgumentException) { invalidRoute = true; }
        try { new ScenicDirector().Stage(Picture()); } catch (ArgumentException) { invalidDirector = true; }
        Check(invalidRoute && invalidDirector, "Single-picture admission weakened motion/episode validation");
    }
    private static void FreshEvidence()
    {
        var p = new ScenicPictureAvailability();
        Check(!p.TryOffer(Picture(), 2, 3, 1, 1, false), "Wrong scene entered availability");
        Check(!p.TryOffer(Picture(), 3, 3, 0, 1, false), "Old point entered availability");
        Check(!p.TryOffer(Picture(), 3, 3, 2, 1, false), "Future point entered availability");
        Check(p.Current == null && p.TryOffer(Picture(), 3, 3, 1, 1.1, false), "Fresh point was unavailable");
    }
    private static void ProtectMotion()
    {
        var p = new ScenicPictureAvailability();
        Check(!p.TryOffer(Picture(), 3, 3, 0, 0, true) && p.Current == null,
            "Background point discovery interrupted a readable cinematic route");
    }
    private static void PresentationClock()
    {
        var p = New();
        for (int i = 0; i <= 20; i++) p.Observe(99, i * .05, true, false);
        for (int i = 21; i <= 40; i++) p.Observe(1, i * .05, false, false);
        for (int i = 41; i <= 60; i++) p.Observe(1, i * .05, true, true);
        Check(p.ShownSeconds == 0 && p.TotalPictureSeconds == 0, "Wrong ID, missing or held footage earned picture dwell");
        Show(p, 3.1, 1);
        Check(Math.Abs(p.ShownSeconds - 1) < .0001, "Actual picture footage was not credited");
        p.Observe(1, 10, true, false);
        Check(Math.Abs(p.ShownSeconds - 1) < .0001, "A long frame gap counted as unseen footage");
    }
    private static void SettledHandoff()
    {
        var p = New(); Show(p, 0, 3);
        Check(!p.CanYield(3), "Prepared route caused an immediate first-picture cut");
        Show(p, 3.05, 1.1);
        Check(p.CanYield(4.15), "Four seconds of actual picture could not yield to motion");
    }
    private static void FailedPictureHandoff()
    {
        var p = New(); Show(p, 0, .2); p.Observe(1, .25, false, false);
        Check(p.CanYield(.8), "Failed picture imposed a minimum-dwell blackout");
    }
    private static void StableNovelPictures()
    {
        var p = New(); Show(p, 0, 2);
        Check(!p.TryOffer(Picture(2, 0, 20), 3, 3, 2, 2, true), "A clearer graph node cut a young valid picture");
        Show(p, 2.05, 6.1);
        Check(!p.TryOffer(Picture(2, 21, 0), 3, 3, 8.15, 8.15, true), "An equivalent viewpoint was presented repeatedly");
        Check(p.TryOffer(Picture(3, 0, 20), 3, 3, 8.15, 8.15, true), "A genuinely different measured picture was unavailable");
        Check(p.ShownSeconds == 0 && p.TotalPictureSeconds >= 8, "A new picture reset accumulated local fallback dwell");
    }
    private static void ContentExhaustion()
    {
        var p = New(); Show(p, 0, 24.1);
        Check(!p.ContentExhausted, "Displayed still footage alone proved route failure");
        p.SearchFinished(false);
        Check(p.ContentExhausted && p.Current != null, "Exhausted local content discarded its valid picture or failed to request variety");
        p.MotionPresented();
        Check(!p.ContentExhausted && p.TotalPictureSeconds == 0, "Real developed motion did not retire fallback exhaustion");
    }
    private static void RetainedPictureDuringCut()
    {
        var p = New(); Show(p, 0, 5);
        p.SearchFinished(true); // staged/accepted route has not rendered yet
        Show(p, 5.05, 2);
        Check(p.Current?.Id == 1 && p.ShownSeconds >= 7 && p.TotalPictureSeconds >= 7,
            "Attempted route admission lost the still-rendered picture's identity or clock");
        p.ClearCurrent();
        Check(p.Current == null && p.TotalPictureSeconds >= 7,
            "Switching requested plan falsely proved developed motion");
    }
    private static void CompletedRoutePicture()
    {
        var p = new ScenicPictureAvailability(); p.SearchFinished(false);
        for (int i = 0; i <= 490; i++) p.Observe(7, i * .05, true, false, retainedRoutePicture: true);
        Check(p.ContentExhausted && p.Current == null && p.ShownSeconds == 0,
            "A completed/blocked route's retained view escaped finite content dwell or was credited as a new picture");
    }
    private static void UnpresentedRouteGrace()
    {
        var p = New(); Show(p, 0, 24.1); p.RouteUnavailable();
        Check(p.ContentExhausted, "Fixture did not exhaust local settled coverage");
        p.SearchFinished(true);
        Check(!p.ContentExhausted, "Freshly prepared route did not get a brief actual-render opportunity");
        Show(p, 24.15, 1); p.SearchFinished(true);
        Check(!p.ContentExhausted, "Initial two-second pending route grace ended too early");
        Show(p, 25.2, 1.1); p.SearchFinished(true);
        Check(p.ContentExhausted && p.Current?.Id == 1,
            "Repeated staging manufactured indefinite recovery while old picture remained displayed");
        p.MotionPresented();
        Check(!p.ContentExhausted && p.TotalPictureSeconds == 0, "Real advancing route failed to clear exhaustion");
    }
    private static void Reset()
    {
        var p = New(); Show(p, 0, 25); p.SearchFinished(false); p.Reset();
        Check(p.Current == null && p.TotalPictureSeconds == 0 && !p.ContentExhausted &&
            p.TryOffer(Picture(2), 4, 4, 30, 30, false), "New region inherited a stale availability lease/history");
    }
}
