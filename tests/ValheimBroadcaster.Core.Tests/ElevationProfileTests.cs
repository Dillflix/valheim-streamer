using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ElevationProfileTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Orbit elevation coordinates native lens damping and composition", Orbit);
        yield return ("Groups retain exclusive lens ownership during elevation changes", GroupLens);
        yield return ("Only rising scenic paths request FreeLook profiles", Scenic);
        yield return ("Native Dolly keeps exact distance traversal without body damping", Dolly);
        yield return ("Confined and recovery views suppress decorative elevation changes", Safety);
        yield return ("Observation camera bodies have no orbital modifier owner", Observation);
        yield return ("Composition opt-out preserves independent lens and damping profiles", CompositionOff);
        yield return ("Medium elevation profiles preserve a restrained deliberate scale", Medium);
        yield return ("Elevation lens endpoints respect supported bounds", LensBounds);
        yield return ("Elevation configuration validates malformed lens and scale inputs", Malformed);
    }
    private static ElevationProfile Profile(bool enabled = true, bool orbital = true, bool scenic = false,
        bool rising = false, bool grouped = false, ShotScale scale = ShotScale.Full, bool confined = false,
        bool recovering = false, float fov = 55, bool composition = true) =>
        ElevationProfilePolicy.Resolve(enabled, orbital, scenic, rising, grouped, scale, confined, recovering, fov, composition);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Orbit()
    {
        var p = Profile();
        Check(p.Enabled && p.Lens && p.Damping && p.Composition, "Compatible orbital profile did not coordinate its native modifiers.");
        Check(p.TopFov > 55 && p.BottomFov < 55 && p.TopDampingScale > 1 && p.BottomDampingScale < 1,
            "High/low perspectives have no distinct lens/damping intent.");
        Check(p.TopScreenY > 0 && p.BottomScreenY < 0, "Elevation has no bounded vertical composition intent.");
    }
    private static void GroupLens()
    {
        var p = Profile(grouped: true);
        Check(p.Enabled && !p.Lens && p.Damping && p.Composition, "Group framing and FreeLook compete for the lens.");
    }
    private static void Scenic()
    {
        Check(!Profile(orbital: false, scenic: true).Enabled, "A flat scenic path manufactured an elevation profile.");
        Check(Profile(orbital: false, scenic: true, rising: true).Enabled, "A rising scenic path cannot use the native modifier value source.");
    }
    private static void Dolly()
    {
        var p = Profile(orbital: false, scenic: true, rising: true);
        Check(p.Lens && p.Composition && !p.Damping, "Dolly damping changed distance-based forecast semantics.");
        Check(p.BottomFov == 55 && p.BottomScreenY == 0, "Rising path invented a descending/bottom profile.");
    }
    private static void Safety()
    {
        foreach (var p in new[] { Profile(confined: true), Profile(recovering: true), Profile(enabled: false) })
            Check(!p.Enabled && !p.Lens && !p.Damping && !p.Composition, "Disabled/constrained profile still owns a camera parameter.");
    }
    private static void Observation() => Check(!Profile(orbital: false).Enabled, "PositionComposer is modified as an orbital camera.");
    private static void CompositionOff()
    {
        var p = Profile(composition: false);
        Check(p.Enabled && p.Lens && p.Damping && !p.Composition, "Composition opt-out disabled unrelated features or retained a hidden owner.");
    }
    private static void Medium()
    {
        var p = Profile(scale: ShotScale.Medium, fov: 45);
        Check(p.TopFov - 45 <= 2 && 45 - p.BottomFov <= 1 && Math.Abs(p.TopScreenY) < .02f,
            "Elevation erased the deliberate medium scale or crowded its headroom.");
    }
    private static void LensBounds()
    {
        Check(Profile(fov: 35).BottomFov == 35 && Profile(fov: 105).TopFov == 105,
            "Native endpoint lens escaped supported bounds.");
    }
    private static void Malformed()
    {
        Throws(() => Profile(fov: float.NaN)); Throws(() => Profile(fov: float.PositiveInfinity));
        Throws(() => Profile(fov: 0)); Throws(() => Profile(scale: (ShotScale)99));
    }
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Malformed configuration was accepted.");
    }
}
