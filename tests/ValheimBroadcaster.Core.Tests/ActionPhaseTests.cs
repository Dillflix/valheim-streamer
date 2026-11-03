using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ActionPhaseTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Observed ranged draw protects action and bounded ambiguous follow-through", Draw);
        yield return ("Missing action evidence cannot manufacture an aftermath", Unknown);
        yield return ("Observed quiet establishes aftermath without claiming outcome", Quiet);
        yield return ("Sustained movement then stop establishes arrival context", Arrival);
        yield return ("Action-phase scene changes discard stale protection", SceneBoundary);
        yield return ("Protected sequence beats retain shown clocks but defer timed edits", SequenceProtection);
        yield return ("Observed arrival reestablishes an already settled sequence", ArrivalSequence);
        yield return ("Action protection suppresses novelty without blocking physical recovery", DirectorProtection);
        yield return ("Duplicate phase samples cannot manufacture transitions", Duplicate);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ActionPhaseEvidence Evidence(bool draw = false, bool attack = false, bool moving = false,
        EncounterKind encounter = EncounterKind.None, bool fresh = true, long scene = 1) =>
        new ActionPhaseEvidence(scene, fresh, draw, attack, false, moving, false, false, encounter, .9f);
    private static void Draw()
    {
        var p = new ActionPhasePolicy();
        Check(p.Observe(0, Evidence(draw:true)).ProtectMoment, "Draw failed to protect framing.");
        for (int i = 1; i <= 50; i++) p.Observe(i * .1, Evidence(draw:true));
        Check(p.Cue.Phase == ObservedActionPhase.Aim && !p.Cue.ComplementaryWindow, "Aiming allowed a cutaway.");
        p.Observe(5.1, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.FollowThrough && p.Cue.ProtectMoment && p.Cue.Reason.Contains("unconfirmed"), "Ended draw invented a release or lost its follow-through.");
        for (int i = 52; i <= 70; i++) p.Observe(i * .1, Evidence());
        Check(!p.Cue.ProtectMoment, "Old draw protected editing indefinitely.");
    }
    private static void Unknown()
    {
        var p = new ActionPhasePolicy(); p.Observe(0, Evidence(attack:true));
        p.Observe(.5, Evidence(fresh:false));
        Check(p.Cue.Phase == ObservedActionPhase.Unknown && !p.Cue.ComplementaryWindow, "Lost evidence became a safe cutaway/ending.");
        for (int i = 6; i <= 40; i++) p.Observe(i * .1, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.Observe, "Missing action evidence manufactured aftermath.");
    }
    private static void Quiet()
    {
        var p = new ActionPhasePolicy(); p.Observe(0, Evidence(encounter:EncounterKind.Combat));
        for (int i = 1; i <= 22; i++) p.Observe(i * .1, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.Aftermath && p.Cue.Reason.Contains("no outcome"), "Quiet was not represented as uncertain aftermath.");
        for (int i = 23; i <= 80; i++) p.Observe(i * .1, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.Observe, "Aftermath never retired.");
    }
    private static void Arrival()
    {
        var p = new ActionPhasePolicy();
        for (int i = 0; i <= 50; i++) p.Observe(i * .1, Evidence(moving:true));
        p.Observe(5.1, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.Arrival, "Sustained motion ending missed arrival context.");
        var shortMove = new ActionPhasePolicy(); shortMove.Observe(0, Evidence(moving:true)); shortMove.Observe(.2, Evidence());
        Check(shortMove.Cue.Phase == ObservedActionPhase.Observe, "Brief motion twitch invented an arrival.");
    }
    private static void SceneBoundary()
    {
        var p = new ActionPhasePolicy(); p.Observe(0, Evidence(draw:true));
        p.Observe(.1, Evidence(scene:2));
        Check(p.Cue.Phase == ObservedActionPhase.Observe && !p.Cue.ProtectMoment, "Another scene inherited protected draw.");
        p.Observe(5, Evidence());
        Check(p.Cue.Phase == ObservedActionPhase.Observe, "Gap manufactured arrival/aftermath.");
    }
    private static void SequenceProtection()
    {
        var p = new ShotSequencePolicy(); var phase = new ActionPhasePolicy();
        var scene = new ShotSituation(ShotSceneKind.Player, CinematicActivity.Idle, .9f, 0, 0, false, ShotEnvironment.Open, ShotLocomotion.Stationary, 1);
        for (int i = 0; i <= 150; i++)
            p.Update(i * .1, .1, scene, true, true, true, phase.Observe(i * .1, Evidence(draw:true)));
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.SequenceAge > 10, "Protected readable moment lost shown credit or timed out into another beat.");
        for (int i = 151; i <= 170; i++)
            p.Update(i * .1, .1, scene, true, true, true, phase.Observe(i * .1, Evidence()));
        Check(p.Cue.Beat != ShotSequenceBeat.Establish, "Editing did not resume after bounded follow-through.");
    }
    private static void ArrivalSequence()
    {
        var p = new ShotSequencePolicy(); var phase = new ActionPhasePolicy();
        var scene = new ShotSituation(ShotSceneKind.Player, CinematicActivity.Idle, .9f, 0, 0, false, ShotEnvironment.Open, ShotLocomotion.Stationary, 1);
        for (int i = 0; i <= 400; i++) p.Update(i * .1, .1, scene, true, true);
        for (int i = 401; i <= 450; i++)
            p.Update(i * .1, .1, scene, true, true, true, phase.Observe(i * .1, Evidence(moving:true)));
        p.Update(45.1, .1, scene, true, true, true, phase.Observe(45.1, Evidence()));
        Check(p.Cue.Beat == ShotSequenceBeat.Reestablish, "Arrival failed to reopen settled geography.");
    }
    private static void DirectorProtection()
    {
        var p = new CinematicShotDirector(); float[] scores = { .95f, .95f, .95f }; bool[] valid = { true, true, true };
        for (int i = 0; i <= 1000; i++)
            Check(p.Select(i * .1, .1, scores, valid, CinematicActivity.Idle, 0, false, 1, true, valid, protectMoment:true) == 0,
                "A protected action received discretionary timed edits.");
        Check(p.Select(100.1, .1, scores, new[] { false, true, true }, CinematicActivity.Idle, 0, false, 1, true, valid, protectMoment:true) != 0,
            "Action protection retained unsafe geometry.");
    }
    private static void Duplicate()
    {
        var p = new ActionPhasePolicy(); p.Observe(0, Evidence(draw:true)); long revision = p.Cue.Revision;
        p.Observe(0, Evidence());
        Check(p.Cue.Revision == revision && p.Cue.Phase == ObservedActionPhase.Aim, "Same-time call manufactured a phase boundary.");
    }
}
