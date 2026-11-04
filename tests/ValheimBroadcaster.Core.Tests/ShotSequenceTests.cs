using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotSequenceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Sequence develops readable work through context detail and re-establishment", WorkSequence);
        yield return ("Sequence finishes without repeating a fixed editorial cycle", FinishedSequence);
        yield return ("Unshown sequence proposals cannot earn footage or complete a beat", PresentedEvidence);
        yield return ("Unavailable requested composition abandons without false coverage", ImpossibleComposition);
        yield return ("Steady and manual hold pause sequence progression", Hold);
        yield return ("Activity changes during a manual pause cannot create stale aftermath", PausedActivityChange);
        yield return ("Interrupted coverage re-establishes instead of resuming a stale detail", Interrupted);
        yield return ("Sequence same-frame calls and large gaps cannot manufacture shown time", Clock);
        yield return ("Combat interrupts a detail promptly and retains action until evidence changes", Combat);
        yield return ("Unknown combat evidence cannot manufacture a victory or aftermath", UnknownCombat);
        yield return ("Brief activity noise cannot replace an established scene sequence", ActivityNoise);
        yield return ("Scenery and creatures receive their own scene sequence semantics", Modes);
        yield return ("Loss of work interest cannot force an unsupported detail composition", RoleEvidence);
        yield return ("Sequence overrides retain measured interior and combat safety", RoleSafety);
        yield return ("Requested role resolution distinguishes a pending beat from the applied role", RolePending);
        yield return ("Sequence clock validation retires previous editorial identity", InvalidClock);
        yield return ("Persistent action axis resists body-facing jitter", AxisStability);
        yield return ("Sustained direction changes request a wider re-establishment", AxisChange);
        yield return ("Only live wider presentation completes axis re-establishment", AxisPresentation);
        yield return ("A presentation-only gap cannot combine disjoint wide-shot coverage", AxisPresentationGap);
        yield return ("Continuity preferences never make an unreadable shot eligible", ReadabilityEscape);
        yield return ("Continuity history distinguishes redundant edits and large scale jumps", EditContinuity);
        yield return ("Stale action axes and interrupted scenes release screen geography", AxisExpiry);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ShotSituation Scene(CinematicActivity activity = CinematicActivity.Idle, bool interest = false,
        float confidence = .9f, ShotSceneKind kind = ShotSceneKind.Player, int companions = 0,
        ShotEnvironment environment = ShotEnvironment.Open, float space = 1) =>
        new ShotSituation(kind, activity, confidence, companions, 0, interest, environment,
            activity == CinematicActivity.Travel ? ShotLocomotion.Walk : ShotLocomotion.Stationary, space);
    private static void Run(ShotSequencePolicy policy, ShotSituation scene, int start, int finish,
        bool usable = true, bool enabled = true, bool presented = true)
    {
        for (int i = start; i <= finish; i++) policy.Update(i * .1, .1, scene, usable, enabled, presented);
    }
    private static void WorkSequence()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 40);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.Cue.Scale == ShotScale.Wide, "Skipped useful establishing footage.");
        Run(p, scene, 41, 80);
        Check(p.Cue.Beat == ShotSequenceBeat.Develop && p.Cue.Role == ShotRole.Detail && p.Cue.Scale == ShotScale.Full,
            "Did not develop the observed work point within context.");
        Run(p, scene, 81, 180);
        Check(p.Cue.Beat == ShotSequenceBeat.Detail && p.Cue.Scale == ShotScale.Medium, "No complementary closer beat.");
        Run(p, scene, 181, 250);
        Check(p.Cue.Beat == ShotSequenceBeat.Reestablish && p.Cue.Scale == ShotScale.Wide, "Failed to restore scene geography after detail.");
    }
    private static void FinishedSequence()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 400); long revision = p.Revision;
        Check(p.Cue.Beat == ShotSequenceBeat.Settle, "The bounded sequence did not finish.");
        Run(p, scene, 401, 1800);
        Check(p.Cue.Beat == ShotSequenceBeat.Settle && p.Revision == revision, "Unchanged work repeatedly replayed a fixed shot sequence.");
    }
    private static void PresentedEvidence()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 30); double shown = p.SequenceAge;
        Run(p, scene, 31, 110, presented: false);
        Check(p.SequenceAge == shown && p.Cue.Beat == ShotSequenceBeat.Establish, "Rejected camera proposals completed the establishing beat.");
        Run(p, scene, 111, 130);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish, "Unshown camera time leaked into resumed footage.");
        Run(p, scene, 131, 160);
        Check(p.Cue.Beat == ShotSequenceBeat.Develop, "Actually displayed footage failed to resume the sequence.");
    }
    private static void ImpossibleComposition()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 180); Check(p.Cue.Beat == ShotSequenceBeat.Detail, "Missing detail setup.");
        double shown = p.SequenceAge;
        Run(p, scene, 181, 320, presented: false);
        Check(p.Cue.Beat == ShotSequenceBeat.Settle && p.SequenceAge == shown && p.BeatAge == 0,
            "Impossible detail either stalled forever or received invented footage credit.");
    }
    private static void Hold()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 180); long revision = p.Revision; double age = p.BeatAge;
        Run(p, scene, 181, 700, enabled: false);
        Check(p.Revision == revision && p.BeatAge == age, "Manual/steady mode advanced editorial sequence.");
        Run(p, scene, 701, 710);
        Check(p.BeatAge < age + 1, "Re-enabling credited the paused interval.");
    }
    private static void Interrupted()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 180); long revision = p.Revision;
        Run(p, scene, 181, 220, usable: false);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.Revision > revision && p.SequenceAge == 0,
            "Long obscuration retained a stale mid-sequence detail.");
        revision = p.Revision; Run(p, scene, 221, 400, usable: false);
        Check(p.Revision == revision, "Unavailable footage repeatedly recreated the same sequence.");
    }
    private static void PausedActivityChange()
    {
        var p = new ShotSequencePolicy(); Run(p, Scene(CinematicActivity.Combat), 0, 100);
        Run(p, Scene(), 101, 400, enabled: false);
        Run(p, Scene(), 401, 420);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.Cue.Role == ShotRole.Establish,
            "Resuming long after a fight manufactured a freshly observed aftermath.");
    }
    private static void Clock()
    {
        var p = new ShotSequencePolicy(); var scene = Scene(CinematicActivity.Work, true);
        Run(p, scene, 0, 30); double age = p.SequenceAge;
        for (int i = 0; i < 100; i++) p.Update(3, .1, scene, true, true);
        Check(p.SequenceAge == age, "Same-frame updates doubled shown time.");
        p.Update(20, 17, scene, true, true);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.SequenceAge == 0, "A long evidence gap completed several beats.");
    }
    private static void Combat()
    {
        var p = new ShotSequencePolicy(); Run(p, Scene(CinematicActivity.Work, true), 0, 180);
        p.Update(18.1, .1, Scene(CinematicActivity.Combat, true), true, true);
        Check(p.Cue.Role == ShotRole.Action && p.Cue.Scale != ShotScale.Medium, "An active fight retained a work detail.");
        Run(p, Scene(CinematicActivity.Combat, true), 182, 500);
        Check(p.Cue.Role == ShotRole.Action && p.Cue.Beat == ShotSequenceBeat.Settle,
            "Long fight was given an unobserved outcome or repeated its scene plan.");
        Run(p, Scene(), 501, 518);
        Check(p.Cue.Beat == ShotSequenceBeat.Aftermath, "Sustained observed quiet did not retain brief aftermath context.");
        Run(p, Scene(), 519, 560);
        Check(p.Cue.Beat == ShotSequenceBeat.Settle, "Aftermath exceeded its bounded shown interval.");
    }
    private static void UnknownCombat()
    {
        var p = new ShotSequencePolicy(); Run(p, Scene(CinematicActivity.Combat, true), 0, 60);
        p.Update(6.1, .1, Scene(confidence: .2f), true, true);
        Check(p.Cue.Beat == ShotSequenceBeat.Establish && p.Cue.Role == ShotRole.Establish,
            "Lost evidence was represented as an observed ending.");
    }
    private static void ActivityNoise()
    {
        var p = new ShotSequencePolicy(); Run(p, Scene(CinematicActivity.Travel), 0, 100);
        long revision = p.Revision; Run(p, Scene(CinematicActivity.Work, true), 101, 108);
        Run(p, Scene(CinematicActivity.Travel), 109, 115);
        Check(p.Revision == revision && p.Cue.Role == ShotRole.Tracking, "A brief task blip replaced sustained travelling coverage.");
    }
    private static void Modes()
    {
        var p = new ShotSequencePolicy(); var scenery = Scene(kind: ShotSceneKind.Scenic);
        Run(p, scenery, 0, 180);
        Check(p.Cue.Role == ShotRole.Landscape && p.Cue.Beat == ShotSequenceBeat.Detail, "Scenic sequence inherited player activity roles.");
        long revision = p.Revision;
        p.Update(18.1, .1, Scene(kind: ShotSceneKind.Creature), true, true);
        Check(p.Revision > revision && p.SequenceAge == 0 && p.Cue.Role == ShotRole.Establish,
            "A new creature inherited environmental sequence credit.");
        Run(p, Scene(kind: ShotSceneKind.Creature), 182, 360);
        Check(ShotScalePolicy.Resolve(p.Cue.Scale, Scene(kind: ShotSceneKind.Creature)) != ShotScale.Medium,
            "Humanoid medium cropping was applied to an unrelated creature body.");
    }
    private static void RoleEvidence()
    {
        var p = new ShotIntentPolicy();
        for (int i = 0; i < 100; i++) p.Update(i * .1, .1, Scene(CinematicActivity.Work), true, ShotRole.Detail);
        Check(p.Intent.Role == ShotRole.Establish, "An editorial role invented a work point.");
    }
    private static void RoleSafety()
    {
        var p = new ShotIntentPolicy();
        for (int i = 0; i < 100; i++) p.Update(i * .1, .1,
            Scene(CinematicActivity.Work, true, environment: ShotEnvironment.Room, space: .3f), true, ShotRole.Detail);
        Check(p.Intent.Role == ShotRole.Interior, "Sequence detail overrode a measured room restriction.");
        p.Update(10, .1, Scene(CinematicActivity.Combat, true), true, ShotRole.Establish);
        Check(p.Intent.Role == ShotRole.Action && p.Intent.MotionScale == 0, "A sequence overrode live combat restraint.");
    }
    private static void InvalidClock()
    {
        var p = new ShotSequencePolicy(); Run(p, Scene(), 0, 30); long revision = p.Revision;
        bool threw = false;
        try { p.Update(2, .1, Scene(), true, true); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw && p.Revision > revision && p.SequenceAge == 0, "Rollback retained old editorial identity.");
    }
    private static void RolePending()
    {
        var p = new ShotIntentPolicy(); var work = Scene(CinematicActivity.Work, true);
        for (int i = 0; i <= 80; i++) p.Update(i * .1, .1, work, true, ShotRole.Establish);
        p.Update(8.1, .1, work, true, ShotRole.Detail);
        Check(p.Intent.Role == ShotRole.Establish && ShotIntentPolicy.ResolveRole(work, ShotRole.Detail) == ShotRole.Detail,
            "Pending role change was indistinguishable from applied displayed intent.");
        Check(ShotIntentPolicy.ResolveRole(Scene(CinematicActivity.Combat), ShotRole.Detail) == ShotRole.Action &&
            ShotIntentPolicy.ResolveRole(Scene(kind: ShotSceneKind.Scenic), ShotRole.Detail) == ShotRole.Landscape,
            "Role matching bypassed contextual safety and mode semantics.");
    }
    private static readonly AutonomyPosition Forward = new AutonomyPosition(0, 0, 1);
    private static readonly AutonomyPosition Back = new AutonomyPosition(0, 0, -1);
    private static readonly AutonomyPosition Right = new AutonomyPosition(5, 1, 2);
    private static readonly AutonomyPosition Left = new AutonomyPosition(-5, 1, 2);
    private static ShotContinuityPolicy AxisSetup()
    {
        var p = new ShotContinuityPolicy(); p.Update(0, Forward, .9f, true, true);
        p.Presented(0, Right, ShotRole.Tracking, ShotScale.Full, true); return p;
    }
    private static void AxisStability()
    {
        var p = AxisSetup();
        for (int i = 1; i < 100; i++) p.Update(i * .1, new AutonomyPosition(i % 2 == 0 ? .3f : -.3f, 0, 1), .9f, true, true);
        Check(p.Axis.X == 0 && p.Axis.Z == 1 && !p.NeedsReestablish, "Minor facing changes rotated the established axis.");
        Check(p.Penalty(Left, ShotRole.Tracking, ShotScale.Full, true) > p.Penalty(Right, ShotRole.Tracking, ShotScale.Full, true, true),
            "Established screen side had no continuity preference.");
    }
    private static void AxisChange()
    {
        var p = AxisSetup();
        for (int i = 1; i <= 10; i++) p.Update(i * .1, Back, .9f, true, true);
        Check(!p.NeedsReestablish && p.Axis.Z == 1, "Short reversal immediately changed the action axis.");
        for (int i = 11; i <= 20; i++) p.Update(i * .1, Back, .9f, true, true);
        Check(p.NeedsReestablish && p.Axis.Z == -1, "Sustained new course never re-established the action axis.");
        Check(p.Penalty(Right, ShotRole.Tracking, ShotScale.Full, true) > p.Penalty(Right, ShotRole.Establish, ShotScale.Wide, true),
            "New geography did not favor a wider readable view.");
    }
    private static void AxisPresentation()
    {
        var p = AxisSetup();
        for (int i = 1; i <= 20; i++) p.Update(i * .1, Back, .9f, true, true);
        for (int i = 21; i <= 35; i++) p.Presented(i * .1, Right, ShotRole.Establish, ShotScale.Wide, false);
        Check(p.NeedsReestablish, "A held or unavailable image completed a wide establishing shot.");
        p.Presented(3.6, Right, ShotRole.Establish, ShotScale.Wide, true);
        for (int i = 0; i < 30; i++) p.Presented(3.6, Right, ShotRole.Establish, ShotScale.Wide, true);
        Check(p.NeedsReestablish, "Duplicate timestamps earned broad-view coverage.");
        for (int i = 37; i <= 46; i++) p.Presented(i * .1, Right, ShotRole.Establish, ShotScale.Wide, true);
        Check(!p.NeedsReestablish, "Actual wide live coverage failed to complete re-establishment.");
    }
    private static void ReadabilityEscape()
    {
        var p = AxisSetup();
        Check(p.Penalty(Left, ShotRole.Detail, ShotScale.Medium, false) == 0,
            "Unusable geometry was treated as an editorial eligibility decision.");
        p.Presented(.1, Left, ShotRole.Action, ShotScale.Full, true);
        Check(p.Penalty(Left, ShotRole.Action, ShotScale.Full, true, true) == 0,
            "An actually displayed corrective camera could not establish its recovered side.");
    }
    private static void AxisPresentationGap()
    {
        var p = AxisSetup();
        for (int i = 1; i <= 20; i++) p.Update(i * .1, Back, .9f, true, true);
        for (int i = 21; i <= 25; i++) p.Presented(i * .1, Right, ShotRole.Establish, ShotScale.Wide, true);
        for (int i = 26; i <= 44; i++) p.Update(i * .1, Back, .9f, true, true);
        for (int i = 45; i <= 49; i++) p.Presented(i * .1, Right, ShotRole.Establish, ShotScale.Wide, true);
        Check(p.NeedsReestablish, "Unrelated fragments of a wide shot were counted as a continuous establishment.");
    }
    private static void EditContinuity()
    {
        var p = AxisSetup();
        Check(p.Penalty(Right, ShotRole.Tracking, ShotScale.Full, true) > 0 &&
            p.Penalty(Right, ShotRole.Tracking, ShotScale.Full, true, true) == 0,
            "Redundant edits were not distinguished from simply holding the current camera.");
        p.Presented(.1, Right, ShotRole.Establish, ShotScale.Wide, true);
        Check(p.Penalty(new AutonomyPosition(5, 1, -4), ShotRole.Detail, ShotScale.Medium, true) > 0,
            "A large scale jump had no continuity cost.");
    }
    private static void AxisExpiry()
    {
        var p = AxisSetup();
        for (int i = 1; i <= 40; i++) p.Update(i * .1, Forward, .1f, true, true);
        Check(!p.HasAxis, "Stale evidence constrained screen geography indefinitely.");
        p.Update(4.1, Forward, .9f, true, true); p.Update(6, Back, .9f, true, true);
        Check(p.HasAxis && p.Axis.Z == -1 && !p.NeedsReestablish, "An evidence gap carried an old axis into a fresh scene.");
    }
}
