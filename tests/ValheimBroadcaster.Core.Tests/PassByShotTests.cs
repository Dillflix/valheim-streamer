using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class PassByShotTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Pass-by requires sustained straight travel before offering a shot", Warmup);
        yield return ("Accepted pass-by body stays fixed through approach crossing and departure", FixedPosition);
        yield return ("Standby pass-by plans are not treated as displayed footage", Standby);
        yield return ("Pass-by departure completes and cannot immediately repeat", DepartureCooldown);
        yield return ("Combat or constrained context immediately releases a pass-by", ContextVeto);
        yield return ("Pass-by stops gracefully and rejects reverse or veering routes", RouteChange);
        yield return ("Pass-by proximity and vertical discontinuities release fixed-camera coverage", Clearance);
        yield return ("Pass-by scene gaps and teleports discard the prior fixed point", Discontinuity);
        yield return ("Pass-by timeout ends stalled traversal and rejected shots rearm", Lifetime);
        yield return ("Pass-by side clearance bounds near-crossing pan speed", PanGeometry);
        yield return ("Pass-by rejects unsupported coordinates and invalid clocks", Bounds);
    }
    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static bool Step(PassByShotPolicy p, double t, float z, bool displayed = false, bool allowed = true, float speed = 4) =>
        p.Update(t, 1, P(z: z), P(z: speed), allowed, displayed);
    private static PassByShotPolicy Ready(float speed = 4)
    {
        var p = new PassByShotPolicy();
        for (int i = 0; i <= 20; i++) Step(p, i * .1, speed * i * .1f, speed: speed);
        Check(p.Offered && !p.Active, "Steady route not offered."); return p;
    }
    private static void Warmup()
    {
        var p = new PassByShotPolicy();
        for (int i = 0; i < 20; i++) Check(!Step(p, i * .1, i * .4f), "Unmeasured route offered early.");
        Check(Step(p, 2, 8), "Stable route unavailable.");
        p.Reset();
        for (int i = 0; i < 80; i++)
            Check(!p.Update(i * .1, 1, P(), i % 10 < 5 ? P(z: 4) : P(x: 4), true, false), "Unstable turns accumulated straight route time.");
    }
    private static void FixedPosition()
    {
        var p = Ready(); var initial = p.Plan.Camera; p.Commit(2);
        bool crossed = false, departed = false;
        for (int i = 21; i <= 53; i++)
        {
            Check(Step(p, i * .1, i * .4f, true), "Valid passage ended prematurely.");
            Check(p.Plan.Camera.Distance(initial) == 0, "Camera retreated with approaching subject.");
            crossed |= p.Reason.EndsWith("crossing"); departed |= p.Reason.EndsWith("departure");
        }
        Check(crossed && departed, "Passage never moved from front to rear coverage.");
    }
    private static void Standby()
    {
        var p = Ready(); var first = p.Plan.Camera;
        Check(Step(p, 2.1, 8.4f) && !p.Active, "Standby was committed without presentation.");
        Check(p.Plan.Camera.Distance(first) > .3, "Standby preview froze too early.");
        p.Commit(1.9); Check(!p.Active, "Stale commit accepted.");
        p.Commit(2.1); Check(p.Active, "Displayed candidate did not latch.");
        Check(!Step(p, 2.2, 8.8f, false), "A shot edited away kept its fixed point alive.");
    }
    private static void DepartureCooldown()
    {
        var p = Ready(); p.Commit(2);
        for (int i = 21; i <= 56; i++) Step(p, i * .1, i * .4f, true);
        Check(!p.Active && p.Reason == "departure complete", "Departure was extended into perpetual tracking.");
        for (int i = 57; i < 400; i++) Check(!Step(p, i * .1, i * .4f), "Departure immediately repeated.");
        bool offered = false;
        for (int i = 400; i <= 440; i++) offered |= Step(p, i * .1, i * .4f);
        Check(offered, "Finite cooldown permanently disabled pass-bys.");
    }
    private static void ContextVeto()
    {
        var p = Ready(); p.Commit(2);
        Check(!Step(p, 2.1, 8.4f, true, false) && !p.Active, "Encounter/room veto was delayed.");
        Check(!Step(p, 2.2, 8.8f), "Veto immediately rearmed a pass-by.");
    }
    private static void RouteChange()
    {
        var p = Ready(); p.Commit(2);
        Check(Step(p, 2.1, 8, true, speed: 0), "Single slow sample prematurely ended shot.");
        for (int i = 22; i <= 27; i++) Step(p, i * .1, 8, true, speed: 0);
        Check(!p.Active, "Stopped route persisted.");
        p = Ready(); p.Commit(2);
        Check(!Step(p, 2.1, 7.6f, true, speed: -4), "Reversed travel retained old plan.");
        p = Ready(); p.Commit(2);
        Check(!p.Update(2.1, 1, P(3.1f, z: 8.4f), P(z: 4), true, true), "Veering through camera was permitted.");
    }
    private static void Clearance()
    {
        var p = Ready(); p.Commit(2);
        Check(!p.Update(2.1, 1, P(y: 3.1f, z: 8.4f), P(z: 4), true, true), "Vertical movement retained roadside framing.");
        p = Ready(); p.Commit(2);
        Check(!p.Update(2.1, 1, p.Plan.Camera, P(z: 4), true, true), "Camera crossing had no clearance guard.");
    }
    private static void Discontinuity()
    {
        var p = Ready(); p.Commit(2);
        Check(!Step(p, 4, 8, true) && !p.Active, "Observation gap inherited old fixed point.");
        p = Ready(); p.Commit(2);
        Check(!p.Update(2.1, 2, P(z: 8.4f), P(z: 4), true, true), "Scene change inherited old shot.");
        p = Ready(); p.Commit(2);
        Check(!Step(p, 2.1, 90, true), "Teleport retained fixed position.");
    }
    private static void Lifetime()
    {
        var p = Ready(); p.Commit(2);
        for (int i = 21; i <= 142; i++) Step(p, i * .1, 8, true);
        Check(!p.Active, "Maximum shot lifetime failed.");
        p = Ready(); p.Reject(2, "obstructed route");
        Check(!p.Offered && !p.Active && !Step(p, 2.1, 8.4f), "Rejected geometry restarted immediately.");
    }
    private static void PanGeometry()
    {
        foreach (float speed in new[] { 1.2f, 2f, 4f, 8f, 9f })
        {
            var p = Ready(speed);
            Check(p.Plan.Clearance >= 6 && speed / p.Plan.Clearance <= .56, "Near pass demands excessive yaw speed.");
            Check(p.Plan.Along(p.Plan.Origin) < -7.9 && p.Plan.Camera.Y == p.Plan.Origin.Y + 2, "Plan does not begin ahead/beside subject.");
        }
    }
    private static void Bounds()
    {
        var p = new PassByShotPolicy();
        for (int i = 0; i <= 25; i++) Check(!p.Update(i * .1, 1, P(z: 19999), P(z: 4), true, false), "World-edge plan overflowed coordinates.");
        p = Ready(); p.Commit(2);
        Check(!p.Update(double.NaN, 1, P(), P(z: 4), true, true) && !p.Active, "Invalid clock preserved active shot.");
    }
}
