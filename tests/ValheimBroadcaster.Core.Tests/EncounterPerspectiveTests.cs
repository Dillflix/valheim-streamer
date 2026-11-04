using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class EncounterPerspectiveTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Closing hostile observation prevents a complementary insert before combat", Closing);
        yield return ("Surrounding hostiles retain player geography while calm related views remain possible", Surrounding);
        yield return ("Lost hostile observations release insert precaution without inventing engagement", Loss);
    }
    private static EncounterObservation Enemy(string id, float x, float z, float closing = 0) =>
        new EncounterObservation(id, (float)Math.Sqrt(x*x + z*z), (float)Math.Sqrt(x*x + z*z), 0, .1f,
            offsetX: x, offsetZ: z, closingSpeed: closing, motionReliable: true);
    private static void Check(bool condition) { if (!condition) throw new Exception("Encounter perspective precaution failed"); }
    private static void Closing()
    {
        var policy = new EncounterSituationPolicy();
        var value = policy.Observe(0, new[] { Enemy("approaching", 0, 18, 2) }, false);
        Check(value.CombatantCount == 0 && !EncounterPerspectivePolicy.AllowsInsert(value));
    }
    private static void Surrounding()
    {
        var policy = new EncounterSituationPolicy();
        Check(EncounterPerspectivePolicy.AllowsInsert(policy.Observe(0, new[] { Enemy("near", 0, 12) }, false)));
        var value = policy.Observe(.25, new[] { Enemy("north", 0, 8), Enemy("southwest", -8, -8), Enemy("southeast", 8, -8) }, false);
        Check(value.Encircled && !EncounterPerspectivePolicy.AllowsInsert(value));
    }
    private static void Loss()
    {
        var policy = new EncounterSituationPolicy();
        policy.Observe(0, new[] { Enemy("approaching", 0, 18, 2) }, false);
        var value = policy.Observe(.25, Array.Empty<EncounterObservation>(), false);
        Check(value.Pressure == EncounterPressure.None && EncounterPerspectivePolicy.AllowsInsert(value));
    }
}
