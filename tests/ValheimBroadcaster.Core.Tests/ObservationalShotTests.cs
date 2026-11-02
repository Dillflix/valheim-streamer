using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ObservationalShotTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Observational shots admit stationary walking and running actors", MovementEligibility);
        yield return ("Observational shots do not replace group combat or recovery views", CoverageRestrictions);
        yield return ("Unknown confined and transition scenes retain established camera families", SceneRestrictions);
        yield return ("Observation space hysteresis prevents repeated body changes without forgiving unsafe context", SpaceHysteresis);
        yield return ("Special locomotion and medium details retain appropriate framing", LocomotionAndScale);
        yield return ("Observational native guides reserve distinct horizontal vertical and depth freedom", MeaningfulDeadZones);
        yield return ("Off-center observational guides remain nested within safe screen bounds", GuideBounds);
        yield return ("Native depth freedom remains positive bounded and outside the near plane", DepthBounds);
        yield return ("A sustained run cannot continuously reseed observational camera history", SustainedRun);
        yield return ("Approach retreat and lateral drift retain one fixed observational shot", DepthDrift);
        yield return ("Standby observations preserve native body history across normal cadence", StandbyCadence);
        yield return ("Lost subject binding discards observational history before resuming", LostBinding);
        yield return ("New scene and explicit shot boundary reseed observational composition", ShotBoundaries);
        yield return ("Teleport gap and clock discontinuity cannot reuse stale observational state", Discontinuities);
        yield return ("Invalid observational geometry and profile inputs are rejected", InvalidInputs);
    }
    private static ShotSituation Scene(ShotLocomotion movement = ShotLocomotion.Stationary,
        ShotEnvironment environment = ShotEnvironment.Open, float confidence = .9f, float space = 1,
        CinematicActivity activity = CinematicActivity.Idle, int companions = 0, int opponents = 0,
        ShotSceneKind kind = ShotSceneKind.Player) =>
        new ShotSituation(kind, activity, confidence, companions, opponents, false, environment, movement, space);
    private static bool Eligible(ShotSituation scene, ShotRole role = ShotRole.Establish, ShotScale scale = ShotScale.Full,
        bool grouped = false, bool recovery = false) => ObservationalShotPolicy.CanUse(scene, role, scale, grouped, recovery);
    private static AutonomyPosition P(float x = 0, float y = 1, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void MovementEligibility()
    {
        foreach (var locomotion in new[] { ShotLocomotion.Stationary, ShotLocomotion.Walk, ShotLocomotion.Run })
        {
            Check(Eligible(Scene(movement: locomotion, activity: CinematicActivity.Travel), ShotRole.Tracking), "Ordinary locomotion lost observational coverage.");
            Check(Eligible(Scene(movement: locomotion, kind: ShotSceneKind.Creature)), "Observed creature could not use native observation.");
        }
        Check(Eligible(Scene(environment: ShotEnvironment.Sheltered)), "Known open shelter lost eligibility.");
    }
    private static void CoverageRestrictions()
    {
        Check(!Eligible(Scene(), grouped: true), "Observational body replaced group framing.");
        Check(!Eligible(Scene(), recovery: true), "Recovery search lost its compact views.");
        Check(!Eligible(Scene(activity: CinematicActivity.Combat)) && !Eligible(Scene(opponents: 1)), "Active opposition used quiet observation.");
        foreach (var role in new[] { ShotRole.Action, ShotRole.Detail, ShotRole.Interaction, ShotRole.Interior, ShotRole.Landscape })
            Check(!Eligible(Scene(), role), "Specific activity intent was replaced.");
        Check(Eligible(Scene(companions: 2)), "A primary-only cutaway was denied merely because companions exist nearby.");
    }
    private static void SceneRestrictions()
    {
        foreach (var environment in new[] { ShotEnvironment.Unknown, ShotEnvironment.Room, ShotEnvironment.Doorway, ShotEnvironment.Dungeon })
            Check(!Eligible(Scene(environment: environment)), "Constrained/unknown scene admitted observational drift.");
        Check(!Eligible(Scene(confidence: .59f)) && !Eligible(Scene(space: .44f)), "Unsupported scene evidence admitted a new family.");
        Check(!Eligible(Scene(kind: ShotSceneKind.Scenic)), "Scenery was assigned an actor-tracking body.");
    }
    private static void LocomotionAndScale()
    {
        foreach (var locomotion in new[] { ShotLocomotion.Vertical, ShotLocomotion.Swim, ShotLocomotion.Boat })
            Check(!Eligible(Scene(movement: locomotion)), "Special movement lost its dedicated response.");
        Check(!Eligible(Scene(), scale: ShotScale.Medium), "Tight detail was given broad drift guides.");
        Check(Eligible(Scene(), scale: ShotScale.Wide), "Readable wide observational family was unavailable.");
    }
    private static void SpaceHysteresis()
    {
        Check(!Eligible(Scene(space: .4f)), "Marginal space started a new observational family.");
        Check(ObservationalShotPolicy.CanUse(Scene(space: .4f), ShotRole.Tracking, ShotScale.Full, false, continuing: true),
            "Ordinary score fluctuation discarded an established observational body.");
        Check(!ObservationalShotPolicy.CanUse(Scene(space: .29f), ShotRole.Tracking, ShotScale.Full, false, continuing: true),
            "Severely constrained space retained observational movement.");
        Check(!ObservationalShotPolicy.CanUse(Scene(environment: ShotEnvironment.Doorway), ShotRole.Tracking, ShotScale.Full, false, continuing: true),
            "Hysteresis overrode doorway restrictions.");
        Check(!ObservationalShotPolicy.CanUse(Scene(activity: CinematicActivity.Combat), ShotRole.Tracking, ShotScale.Full, false, continuing: true),
            "Hysteresis overrode action coverage.");
    }
    private static void MeaningfulDeadZones()
    {
        var full = ObservationalShotPolicy.Describe(ShotScale.Full, 6, 1);
        var wide = ObservationalShotPolicy.Describe(ShotScale.Wide, 12, 1);
        Check(full.IsValid && wide.IsValid, "Valid profiles not accepted.");
        Check(full.DeadZoneX >= .25 && full.DeadZoneY >= .1 && full.DepthDeadZone >= 1, "Observation remained effectively hard locked.");
        Check(wide.DeadZoneX > full.DeadZoneX && wide.DeadZoneY > full.DeadZoneY, "Scale did not affect permitted picture movement.");
        Check(full.DampingZ > full.DampingX && full.DampingY > full.DampingX, "All axes use the same response.");
    }
    private static void GuideBounds()
    {
        foreach (var scale in new[] { ShotScale.Wide, ShotScale.Full })
        foreach (float x in new[] { -.12f, 0, .12f, 4 })
        foreach (float y in new[] { -.05f, 0, .05f, -3 })
        {
            var p = ObservationalShotPolicy.Describe(scale, 8, 1, x, y);
            Check(p.IsValid && p.DeadZoneX < p.HardLimitX && p.DeadZoneY < p.HardLimitY, "Dead guide escaped hard guide.");
            Check(Math.Abs(p.ScreenX) + p.HardLimitX * .5 < .46 && Math.Abs(p.ScreenY) + p.HardLimitY * .5 < .46,
                "Offset guide moved outside safe screen area.");
        }
    }
    private static void DepthBounds()
    {
        foreach (float distance in new[] { 1f, 3f, 10f, 80f })
        foreach (var scale in new[] { ShotScale.Wide, ShotScale.Full })
        {
            var p = ObservationalShotPolicy.Describe(scale, distance, 1);
            Check(p.Distance == distance && p.DepthDeadZone > 0 && p.DepthDeadZone <= 4, "Depth freedom unbounded or absent.");
            Check(p.Distance - p.DepthDeadZone * .5f >= p.Distance * .84f, "Depth guide allowed excessive approach.");
        }
    }
    private static void SustainedRun()
    {
        var lease = new ObservationalShotLease(); Check(lease.Update(0, 1, 1, P()), "First shot did not seed.");
        for (int i = 1; i <= 900; i++)
            Check(!lease.Update(i * .1, 1, 1, P(i * .8f)), "Continuous running repeatedly restarted native history.");
    }
    private static void DepthDrift()
    {
        var lease = new ObservationalShotLease(); lease.Update(0, 1, 1, P());
        Check(!lease.Update(.1, 1, 1, P(z: 1)), "Approach reseeded fixed depth guides.");
        Check(!lease.Update(.2, 1, 1, P(z: -1)), "Retreat reseeded fixed depth guides.");
        Check(!lease.Update(.3, 1, 1, P(x: 2)), "Lateral crossing reseeded fixed screen guides.");
    }
    private static void StandbyCadence()
    {
        var lease = new ObservationalShotLease(); lease.Update(0, 1, 1, P());
        for (int i = 1; i <= 30; i++) Check(!lease.Update(i * .35, 1, 1, P(i * .5f)), "Normal standby cadence reset native damping.");
    }
    private static void LostBinding()
    {
        var lease = new ObservationalShotLease(); lease.Update(0, 1, 1, P());
        Check(!lease.Update(.1, 1, 1, P(), false) && !lease.IsBound, "Missing subject retained binding.");
        Check(lease.Update(.2, 1, 1, P()) && lease.IsBound, "Rebinding reused stale shot settings.");
    }
    private static void ShotBoundaries()
    {
        var lease = new ObservationalShotLease(); lease.Update(0, 1, 1, P());
        Check(lease.Update(.1, 1, 2, P()), "Explicit shot change kept old composition.");
        Check(lease.Update(.2, 2, 2, P()), "New scene inherited fixed orientation.");
        lease.Reset(); Check(lease.Update(.3, 2, 2, P()), "Teardown did not discard shot history.");
    }
    private static void Discontinuities()
    {
        var lease = new ObservationalShotLease(); lease.Update(0, 1, 1, P());
        Check(lease.Update(.1, 1, 1, P(20)), "Teleport inherited an old body history.");
        Check(lease.Update(2, 1, 1, P(20)), "Long observation gap retained history.");
        Check(lease.Update(.5, 1, 1, P(20)), "Clock discontinuity retained history.");
    }
    private static void InvalidInputs()
    {
        Check(!default(ObservationalShotProfile).IsValid, "Absent profile accepted.");
        foreach (float value in new[] { 0, float.NaN, float.PositiveInfinity, 81 })
        {
            bool threw = false;
            try { ObservationalShotPolicy.Describe(ShotScale.Full, value, 1); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Invalid distance accepted.");
        }
        bool medium = false;
        try { ObservationalShotPolicy.Describe(ShotScale.Medium, 3, 1); }
        catch (ArgumentOutOfRangeException) { medium = true; }
        Check(medium, "Unsupported medium profile constructed.");
    }
}
