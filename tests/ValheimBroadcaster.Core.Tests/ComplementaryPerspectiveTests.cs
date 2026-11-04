using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ComplementaryPerspectiveTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Complementary view requires actual established player footage and fresh return", Admission);
        yield return ("Complementary view ends on shown duration and rearms after primary coverage", Duration);
        yield return ("Unshown complementary view has an independent wall-time bound", WallTime);
        yield return ("Protected action immediately interrupts complementary coverage", Protected);
        yield return ("Lost target or return readability ends complementary view", LostReturn);
        yield return ("Cutaway scene/subject changes cannot inherit admission", Discontinuity);
        yield return ("Complementary geography preserves action-axis screen side", Geography);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ActionPhaseCue Quiet()
    { var p = new ActionPhasePolicy(); return p.Observe(0, new ActionPhaseEvidence(1, true, false, false, false, false, false, false, EncounterKind.Threat, .9f)); }
    private static ActionPhaseCue Draw()
    { var p = new ActionPhasePolicy(); return p.Observe(0, new ActionPhaseEvidence(1, true, true, false, false, false, false, false, EncounterKind.Hunting, .9f)); }
    private static ComplementaryPerspectivePolicy Established()
    {
        var p = new ComplementaryPerspectivePolicy();
        for (int i = 0; i <= 70; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), true, false, true, true, false);
        return p;
    }
    private static void Enter(ComplementaryPerspectivePolicy p)
    {
        for (int i = 71; i <= 78; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), !p.Active, p.Active, true, true, true);
        Check(p.Active, "Prepared counterpart never admitted.");
    }
    private static void Admission()
    {
        var p = new ComplementaryPerspectivePolicy();
        for (int i = 0; i <= 100; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), false, false, true, true, true);
        Check(!p.Active, "Off-air primary solve earned establishment.");
        p = Established();
        for (int i = 71; i <= 78; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), true, false, true, false, true);
        Check(!p.Active, "Complementary view admitted without a fresh return.");
        p = Established(); Enter(p);
    }
    private static void Duration()
    {
        var p = Established(); Enter(p);
        for (int i = 79; i <= 110; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), !p.Active, p.Active, true, true, true);
        Check(!p.Active && p.ShownSeconds >= ComplementaryPerspectivePolicy.MaximumShownSeconds, "Shown insert did not end.");
        for (int i = 111; i <= 350; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), true, false, true, true, true);
        Check(!p.Active, "Cutaway cooldown was bypassed.");
    }
    private static void WallTime()
    {
        var p = Established(); Enter(p);
        double shown = p.ShownSeconds;
        for (int i = 79; i <= 125; i++) p.Update(i * .1, 1, "enemy", true, Quiet(), false, false, true, true, true);
        Check(!p.Active && p.ShownSeconds == shown, "Unavailable presentation either held forever or earned false footage.");
    }
    private static void Protected()
    {
        var p = Established(); Enter(p);
        Check(!p.Update(7.9, 1, "enemy", true, Draw(), false, true, true, true, true), "A draw did not return immediately to player.");
    }
    private static void LostReturn()
    {
        var p = Established(); Enter(p);
        Check(!p.Update(7.9, 1, "enemy", true, Quiet(), false, true, true, false, true), "Lost primary return kept insert active.");
        p = Established(); Enter(p);
        Check(!p.Update(7.9, 1, "enemy", true, Quiet(), false, true, false, true, true), "Lost relationship kept unrelated footage active.");
    }
    private static void Discontinuity()
    {
        var p = Established(); Enter(p);
        Check(!p.Update(7.9, 2, "enemy", true, Quiet(), false, true, true, true, true), "New scene inherited active insertion.");
        p = Established(); Enter(p);
        Check(!p.Update(7.9, 1, "different", true, Quiet(), false, true, true, true, true), "Changed target inherited active insertion.");
    }
    private static void Geography()
    {
        var owner = new AutonomyPosition(0, 0, 0); var enemy = new AutonomyPosition(0, 0, 10);
        var from = new AutonomyPosition(5, 3, 0);
        Check(ComplementaryPerspectivePolicy.SameSide(owner, enemy, from, new AutonomyPosition(5, 3, 10)), "Same-side view rejected.");
        Check(!ComplementaryPerspectivePolicy.SameSide(owner, enemy, from, new AutonomyPosition(-5, 3, 10)), "Axis reversal admitted.");
        Check(!ComplementaryPerspectivePolicy.SameSide(owner, owner, from, from), "Degenerate relationship invented an axis.");
    }
}
