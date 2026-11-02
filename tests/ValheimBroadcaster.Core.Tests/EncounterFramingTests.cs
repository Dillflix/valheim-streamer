using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class EncounterFramingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Hunting prefers a visible target from a rear oblique viewpoint", HuntingAngle);
        yield return ("Melee prefers spatial separation and visible opponents", CombatAngle);
        yield return ("Unknown encounters do not impose editorial penalties", Unknown);
        yield return ("Distant targets never become compulsory framing envelopes", BoundedEnvelope);
        yield return ("Encounter axis changes are bounded and do not reseed on target turns", AxisRate);
        yield return ("Encounter axis recovers smoothly after target loss", AxisRelease);
        yield return ("Encounter axis crosses angle wrap by the shortest path", AxisWrap);
        yield return ("Encounter axis resets across scene discontinuities", AxisReset);
        yield return ("Hunting holds steady without claiming active melee", HuntingIntent);
        yield return ("Nearby threat establishes relationship without claiming combat", ThreatIntent);
        yield return ("Active combat interrupts hunting sequence promptly", InterruptHunt);
        yield return ("Qualified hunting survives uncertain generic action evidence", QualifiedHunt);
        yield return ("Encounter shots retain whole-body framing and suppress observation", WholeBody);
        yield return ("Semantic hunting interest takes lead-room priority over travel", LeadRoom);
        yield return ("No encounter target visibility cannot outweigh primary readability", BoundedPenalty);
        yield return ("Encounter framing rejects nonfinite evidence", InvalidEvidence);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static ShotSituation Scene(EncounterKind kind, float confidence = .8f, float distance = 8,
        CinematicActivity activity = CinematicActivity.Idle) => new ShotSituation(ShotSceneKind.Player,
        activity, .9f, 0, kind == EncounterKind.Combat ? 1 : 0, true, ShotEnvironment.Open,
        ShotLocomotion.Stationary, 1, kind, confidence, distance);
    private static float Penalty(EncounterKind kind, float dot, bool visible = true, int opponents = 1, int seen = 1) =>
        EncounterFramingPolicy.ViewPenalty(kind, .8f, dot, visible, .05f, opponents, seen);
    private static void HuntingAngle() => Check(Penalty(EncounterKind.Hunting, -.75f) < Penalty(EncounterKind.Hunting, 1) &&
        Penalty(EncounterKind.Hunting, -.75f) < Penalty(EncounterKind.Hunting, -.75f, false), "Hunter lost target-axis preference.");
    private static void CombatAngle() => Check(Penalty(EncounterKind.Combat, 0) < Penalty(EncounterKind.Combat, 1) &&
        Penalty(EncounterKind.Combat, 0, opponents: 3, seen: 3) < Penalty(EncounterKind.Combat, 0, opponents: 3, seen: 1),
        "Melee did not favor both participant visibility and a separating angle.");
    private static void Unknown() => Check(EncounterFramingPolicy.ViewPenalty(EncounterKind.None, 1, 1, false, 0, 0, 0) == 0 &&
        EncounterFramingPolicy.ViewPenalty(EncounterKind.Hunting, .4f, 1, false, 0, 0, 0) == 0 &&
        !EncounterFramingPolicy.Meaningful((EncounterKind)(-1), 1), "Unknown evidence imposed a target preference.");
    private static void BoundedEnvelope() => Check(EncounterFramingPolicy.IncludeInterestEnvelope(8, .2f, .1f, true) &&
        !EncounterFramingPolicy.IncludeInterestEnvelope(60, .1f, .1f, true) &&
        !EncounterFramingPolicy.IncludeInterestEnvelope(8, .45f, .1f, true) &&
        !EncounterFramingPolicy.IncludeInterestEnvelope(8, 0, 0, false), "Distant/offscreen/behind target forced the lens.");
    private static void AxisRate()
    {
        var axis = new EncounterAxisPolicy();
        float previous = axis.Update(0, 1, 0, EncounterKind.Hunting, 1, 0, 1);
        for (int i = 1; i <= 40; i++)
        {
            float heading = axis.Update(i * .1, 1, 0, EncounterKind.Hunting, 1, i < 20 ? 0 : 1, i < 20 ? 1 : 0);
            Check(Math.Abs(heading - previous) <= .801, "Target turn snapped the candidate axis."); previous = heading;
        }
    }
    private static void AxisRelease()
    {
        var axis = new EncounterAxisPolicy();
        for (int i = 0; i <= 20; i++) axis.Update(i * .1, 1, 0, EncounterKind.Combat, 1, 0, 1);
        float before = axis.Heading;
        float after = axis.Update(2.1, 1, 0, EncounterKind.None, 0, 0, 0);
        Check(after > 0 && after < before && before - after <= 1.601, "Target loss snapped the rig heading.");
    }
    private static void AxisWrap()
    {
        var axis = new EncounterAxisPolicy(); axis.Update(0, 1, 179, EncounterKind.None, 0, 0, 0);
        float next = axis.Update(.1, 1, -179, EncounterKind.None, 0, 0, 0);
        Check(next > 179, "Angle wrap took the long route.");
    }
    private static void AxisReset()
    {
        var axis = new EncounterAxisPolicy(); axis.Update(0, 1, 0, EncounterKind.Hunting, 1, 0, 1);
        axis.Update(.1, 1, 0, EncounterKind.Hunting, 1, 0, 1);
        Check(axis.Update(.2, 2, 75, EncounterKind.Combat, 1, 0, 1) == 75 &&
            axis.Update(3, 2, 90, EncounterKind.Combat, 1, 0, 1) == 90, "World/gap retained an old encounter axis.");
    }
    private static void HuntingIntent()
    {
        var policy = new ShotIntentPolicy(); CinematicShotIntent result = default;
        for (int i = 0; i <= 80; i++) result = policy.Update(i * .1, .1, Scene(EncounterKind.Hunting, distance: 70), true);
        Check(result.Role == ShotRole.Interaction && result.MotionScale == 0 && result.InterestWeight > 0,
            "Hunter did not receive stable semantic coverage.");
    }
    private static void ThreatIntent()
    {
        var policy = new ShotIntentPolicy(); CinematicShotIntent result = default;
        for (int i = 0; i <= 80; i++) result = policy.Update(i * .1, .1, Scene(EncounterKind.Threat), true);
        Check(result.Role == ShotRole.Interaction && result.MotionScale <= .15f, "Threat proximity was lost or invented combat.");
    }
    private static void InterruptHunt()
    {
        var policy = new ShotSequencePolicy();
        for (int i = 0; i <= 50; i++) policy.Update(i * .1, .1, Scene(EncounterKind.Hunting), true, true);
        var next = policy.Update(5.1, .1, Scene(EncounterKind.Combat, activity: CinematicActivity.Combat), true, true);
        Check(next.Role == ShotRole.Action, "Combat waited for hunting sequence to finish.");
    }
    private static void QualifiedHunt()
    {
        var scene = new ShotSituation(ShotSceneKind.Player, CinematicActivity.Idle, .35f, 0, 0, true,
            ShotEnvironment.Open, ShotLocomotion.Stationary, 1, EncounterKind.Hunting, .65f, 65);
        var sequence = new ShotSequencePolicy(); var intent = new ShotIntentPolicy();
        ShotSequenceCue cue = default;
        for (int i = 0; i <= 70; i++)
        {
            cue = sequence.Update(i * .1, .1, scene, true, true);
            intent.Update(i * .1, .1, scene, true, cue.Role);
        }
        Check(cue.Role == ShotRole.Interaction && intent.Intent.Role == ShotRole.Interaction &&
            cue.MotionMultiplier == 0, "Uncertain generic action erased the qualified hunting evidence.");
    }
    private static void WholeBody()
    {
        foreach (EncounterKind kind in new[] { EncounterKind.Hunting, EncounterKind.Threat, EncounterKind.Combat })
        {
            ShotSituation scene = Scene(kind);
            Check(ShotScalePolicy.Resolve(ShotScale.Medium, scene) == ShotScale.Full &&
                !ObservationalShotPolicy.CanUse(scene, ShotRole.Establish, ShotScale.Wide, false), "Encounter was cropped or entered observation.");
        }
    }
    private static void LeadRoom()
    {
        var policy = new ShotCompositionPolicy();
        var value = new ShotCompositionObservation { SemanticInterest = true, InterestX = .3f,
            InterestConfidence = .8f, MotionX = -.1f, LeftExtent = .1f, RightExtent = .1f, TopExtent = .1f, BottomExtent = .1f };
        var result = policy.Update(0, 0, 1, CinematicActivity.Travel, value, false, 1, 0);
        Check(result.DesiredX < 0, "Locomotion erased target lead room.");
    }
    private static void BoundedPenalty() => Check(Penalty(EncounterKind.Combat, 1, false, 3, 0) <= .36f,
        "Encounter score could overrule the separate readability gate.");
    private static void InvalidEvidence()
    {
        bool failed = false;
        try { EncounterFramingPolicy.ViewPenalty(EncounterKind.Combat, 1, float.NaN, true, 0, 1, 1); }
        catch (ArgumentOutOfRangeException) { failed = true; }
        Check(failed, "NaN framing evidence accepted.");
    }
}
