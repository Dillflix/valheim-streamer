using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotIntentTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Shot purpose establishes context before sustained travel emphasis", EstablishTravel);
        yield return ("Shot purpose does not advance through unavailable footage", PausedEvidence);
        yield return ("Work detail requires a supported action point and confidence", WorkEvidence);
        yield return ("Combat changes purpose promptly and suppresses decorative movement", Combat);
        yield return ("Brief activity changes do not repeatedly change the shot purpose", EvidencePersistence);
        yield return ("Measured confined room and doorway constrain shot scale and movement", Room);
        yield return ("Unreadable wide footage does not prevent compact room recovery", RoomRecovery);
        yield return ("Repeated same-frame intent updates do not manufacture footage", DuplicateTime);
        yield return ("Spacious rooms retain meaningful group composition", SpaciousRoom);
        yield return ("Scenic paths do not inherit actor size or locomotion assumptions", Scenic);
        yield return ("Purpose scale and lens ease instead of snapping on activity changes", SmoothScale);
        yield return ("Movement profiles distinguish sprinting from water and vessel motion", MovementProfiles);
        yield return ("A healthy transition source is retained over an unreadable destination", UnreadableDestination);
        yield return ("Recovery and distinct blocked paths cut to the validated replacement", RecoveryTransitions);
        yield return ("Obstructed paths toward similar viewpoints hold for in-place repair", JumpCutGuard);
        yield return ("An obstructed move returns to a readable source instead of cutting forward", ObstructedBlendReturn);
        yield return ("Transitions distinguish same-scene changes from new scenes and action angles", SceneTransitions);
        yield return ("Doorway handoffs have bounded brief transitions", DoorwayTransition);
        yield return ("Purpose and transition validation reject invalid evidence", InvalidEvidence);
        yield return ("Readable mask prevents timed novelty selecting a hidden actor", ReadableNovelty);
        yield return ("Readable replacement beats fixed improvement margin after persistence", ReadableRecovery);
        yield return ("Locomotion profiles bound vertical and vessel trajectory acceleration", TrajectoryProfiles);
        yield return ("Rejected camera proposals retain displayed shot age and motion history", RejectedProposal);
        yield return ("Same-frame camera reselection can roll back to the displayed source", RejectedProposalChain);
        yield return ("A reset director can adopt the validated displayed pose after a denied proposal", AdoptAfterReset);
    }

    private static ShotSituation Scene(CinematicActivity activity = CinematicActivity.Idle,
        float confidence = .9f, int companions = 0, int opponents = 0, bool interest = false,
        ShotEnvironment environment = ShotEnvironment.Open, ShotLocomotion locomotion = ShotLocomotion.Stationary,
        float space = 1, ShotSceneKind kind = ShotSceneKind.Player) =>
        new ShotSituation(kind, activity, confidence, companions, opponents, interest, environment, locomotion, space);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static CinematicShotIntent Run(ShotIntentPolicy policy, ShotSituation situation, int first = 0, int end = 200, bool usable = true)
    {
        CinematicShotIntent intent = policy.Intent;
        for (int i = first; i <= end; i++) intent = policy.Update(i * .1, .1, situation, usable);
        return intent;
    }
    private static void EstablishTravel()
    {
        var policy = new ShotIntentPolicy();
        var scene = Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Walk);
        Check(Run(policy, scene, end: 40).Role == ShotRole.Establish, "Travel skipped the establishing context.");
        CinematicShotIntent result = Run(policy, scene, 41, 120);
        Check(result.Role == ShotRole.Tracking && result.RadiusScale < 1.1f,
            "Sustained travel never developed into participant coverage.");
    }
    private static void PausedEvidence()
    {
        var policy = new ShotIntentPolicy();
        var scene = Scene(CinematicActivity.Work, interest: true);
        Run(policy, scene, end: 20);
        CinematicShotIntent before = policy.Intent;
        Run(policy, scene, 21, 300, false);
        Check(policy.Intent.Role == ShotRole.Establish && policy.Intent.RadiusScale == before.RadiusScale,
            "Unavailable footage advanced the semantic shot or scale.");
        Run(policy, scene, 301, 330);
        Check(policy.Intent.Role == ShotRole.Establish, "Held footage counted toward established coverage.");
        Check(Run(policy, scene, 331, 400).Role == ShotRole.Detail, "Useful footage did not resume shot progression.");
    }
    private static void WorkEvidence()
    {
        var unknown = new ShotIntentPolicy();
        Check(Run(unknown, Scene(CinematicActivity.Work, confidence: .4f, interest: true)).Role == ShotRole.Establish,
            "Weak evidence was represented as a definite work detail.");
        var missing = new ShotIntentPolicy();
        Check(Run(missing, Scene(CinematicActivity.Work)).Role == ShotRole.Establish,
            "The director invented an activity point.");
        var known = new ShotIntentPolicy();
        var result = Run(known, Scene(CinematicActivity.Work, interest: true));
        Check(result.Role == ShotRole.Detail && result.InterestWeight > .2f && result.RadiusScale < .85f,
            "Supported work did not receive a closer actor-plus-action composition.");
    }
    private static void Combat()
    {
        var policy = new ShotIntentPolicy();
        Run(policy, Scene(), end: 80);
        CinematicShotIntent before = policy.Intent;
        var result = policy.Update(8.1, .1, Scene(CinematicActivity.Combat, opponents: 1, interest: true), true);
        Check(result.Role == ShotRole.Action && result.MotionScale == 0, "Combat did not promptly suppress decoration.");
        Check(Math.Abs(result.RadiusScale - before.RadiusScale) < .1f, "Combat snapped the camera scale.");
    }
    private static void EvidencePersistence()
    {
        var policy = new ShotIntentPolicy();
        Run(policy, Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Walk));
        for (int i = 201; i < 211; i++)
            policy.Update(i * .1, .1, Scene(CinematicActivity.Work, interest: true), true);
        Check(policy.Intent.Role == ShotRole.Tracking, "A brief activity blip changed the shot role.");
        Run(policy, Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Walk), 211, 240);
        Check(policy.Intent.Role == ShotRole.Tracking, "A vanished activity point left pending intent behind.");
    }
    private static void Room()
    {
        var policy = new ShotIntentPolicy();
        var result = Run(policy, Scene(environment: ShotEnvironment.Room, space: .3f));
        Check(result.Role == ShotRole.Interior && result.RadiusScale < .7f && result.HeightScale < .5f && result.MotionScale < .1f,
            "A measured tight room retained an open-landscape camera preference.");
        var door = policy.Update(20.1, .1, Scene(environment: ShotEnvironment.Doorway, space: .8f), true);
        Check(door.Role == ShotRole.Interior && door.MotionScale <= .05f, "A doorway did not quiet discretionary movement.");
    }
    private static void SpaciousRoom()
    {
        var policy = new ShotIntentPolicy();
        var result = Run(policy, Scene(companions: 2, environment: ShotEnvironment.Room, space: .9f));
        Check(result.Role == ShotRole.Interaction && result.GroupWeightScale > 1,
            "All rooms were treated as cramped primary-only views.");
    }
    private static void RoomRecovery()
    {
        var policy = new ShotIntentPolicy();
        Run(policy, Scene(), end: 20);
        Run(policy, Scene(environment: ShotEnvironment.Room, space: .2f), 21, 60, false);
        Check(policy.Intent.Role == ShotRole.Interior && policy.Intent.RadiusScale < .7f,
            "Unusable footage froze the wide composition that prevented room recovery.");
        Run(policy, Scene(CinematicActivity.Work, interest: true), 61, 70);
        Check(policy.Intent.Role != ShotRole.Detail, "Room-recovery frames falsely established work coverage.");
    }
    private static void DuplicateTime()
    {
        var policy = new ShotIntentPolicy();
        policy.Update(0, .1, Scene(CinematicActivity.Travel), true);
        for (int i = 0; i < 200; i++) policy.Update(.1, .1, Scene(CinematicActivity.Travel), true);
        Check(policy.Intent.Role == ShotRole.Establish, "Same-frame evaluations exhausted the establishing interval.");
    }
    private static void Scenic()
    {
        var policy = new ShotIntentPolicy();
        var result = Run(policy, Scene(CinematicActivity.Work, interest: true, locomotion: ShotLocomotion.Boat, kind: ShotSceneKind.Scenic));
        Check(result.Role == ShotRole.Landscape && result.RadiusScale == 1 && result.HeightScale == 1 &&
            result.FovOffset == 0 && result.InterestWeight == 0 && result.MotionScale == 1,
            "Scenic development inherited actor-purpose offsets.");
    }
    private static void SmoothScale()
    {
        var policy = new ShotIntentPolicy();
        Run(policy, Scene(), end: 100);
        float radius = policy.Intent.RadiusScale, fov = policy.Intent.FovOffset;
        for (int i = 101; i <= 200; i++)
        {
            var intent = policy.Update(i * .1, .1, Scene(environment: ShotEnvironment.Room, space: .3f), true);
            Check(Math.Abs(intent.RadiusScale - radius) < .08f && Math.Abs(intent.FovOffset - fov) < .5f,
                "A role change snapped its scale or lens preference.");
            Check(intent.RadiusScale >= .6f && intent.FovOffset <= 7, "Scale escaped its composition bounds.");
            radius = intent.RadiusScale; fov = intent.FovOffset;
        }
    }
    private static void MovementProfiles()
    {
        var run = Run(new ShotIntentPolicy(), Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Run));
        var boat = Run(new ShotIntentPolicy(), Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Boat));
        var swim = Run(new ShotIntentPolicy(), Scene(CinematicActivity.Travel, locomotion: ShotLocomotion.Swim));
        Check(run.PositionDampingScale < boat.PositionDampingScale && run.AimDampingScale < swim.AimDampingScale &&
            run.MaximumAngularSpeed > boat.MaximumAngularSpeed && boat.MotionScale < run.MotionScale,
            "Locomotion situations all use the same motion response.");
    }
    private static ShotTransitionDecision Transition(bool sameScene = true, bool source = true, bool destination = true,
        bool path = true, float angle = 10, float distance = 2, float screen = .05f, float fov = 0,
        ShotRole from = ShotRole.Establish, ShotRole to = ShotRole.Establish,
        ShotEnvironment environment = ShotEnvironment.Open, CinematicActivity activity = CinematicActivity.Idle,
        bool recovering = false) =>
        ShotTransitionPolicy.Decide(sameScene, source, destination, path, angle, distance, screen, fov,
            from, to, environment, activity, recovering);
    private static void UnreadableDestination() => Check(Transition(destination: false).Mode == ShotTransitionMode.Hold,
        "An aesthetic transition discarded readable source footage.");
    private static void RecoveryTransitions()
    {
        Check(Transition(source: false).Mode == ShotTransitionMode.Cut &&
            Transition(path: false, angle: 40).Mode == ShotTransitionMode.Cut &&
            Transition(recovering: true).Mode == ShotTransitionMode.Cut,
            "Recovery blended through a failed or stale picture.");
    }
    private static void JumpCutGuard()
    {
        Check(Transition(path: false).Mode == ShotTransitionMode.Hold,
            "An obstructed move toward a near-identical viewpoint cut instead of waiting for repair.");
        Check(Transition(path: false, angle: 40).Mode == ShotTransitionMode.Cut &&
            Transition(path: false, fov: 15).Mode == ShotTransitionMode.Cut &&
            Transition(path: false, screen: .3f).Mode == ShotTransitionMode.Cut,
            "A visually distinct obstructed destination lost its motivated cut.");
    }
    private static void ObstructedBlendReturn()
    {
        var glide = ShotTransitionPolicy.DecideObstructedBlend(true, true);
        Check(glide.Mode == ShotTransitionMode.Return && glide.Seconds > 0 &&
            glide.Seconds <= ShotTransitionPolicy.ReturnSeconds,
            "A readable source did not receive a bounded return glide.");
        var snap = ShotTransitionPolicy.DecideObstructedBlend(true, false);
        Check(snap.Mode == ShotTransitionMode.Return && snap.Seconds == 0,
            "An unsupported blended pose lingered through a timed glide.");
        Check(ShotTransitionPolicy.DecideObstructedBlend(false, true).Mode == ShotTransitionMode.Cut &&
            ShotTransitionPolicy.DecideObstructedBlend(false, false).Mode == ShotTransitionMode.Cut,
            "A failed move without a readable return path did not adopt the validated destination.");
    }
    private static void SceneTransitions()
    {
        Check(Transition().Mode == ShotTransitionMode.Blend && Transition(sameScene: false).Mode == ShotTransitionMode.Cut,
            "Scene continuity did not affect the transition.");
        Check(Transition(angle: 40, activity: CinematicActivity.Combat).Mode == ShotTransitionMode.Cut &&
            Transition(to: ShotRole.Detail, fov: 15).Mode == ShotTransitionMode.Cut,
            "Meaningful action/scale changes were swept through a generic blend.");
    }
    private static void DoorwayTransition()
    {
        var doorway = Transition(environment: ShotEnvironment.Doorway, angle: 20, distance: 6);
        var open = Transition(angle: 20, distance: 6);
        Check(doorway.Mode == ShotTransitionMode.Blend && doorway.Seconds <= .65 && doorway.Seconds < open.Seconds,
            "Doorway handoff used the same long blend as open terrain.");
    }
    private static void InvalidEvidence()
    {
        bool sceneRejected = false, transitionRejected = false;
        try { Scene(confidence: float.NaN); } catch (ArgumentOutOfRangeException) { sceneRejected = true; }
        try { Transition(angle: float.PositiveInfinity); } catch (ArgumentOutOfRangeException) { transitionRejected = true; }
        Check(sceneRejected && transitionRejected, "Invalid evidence reached the director.");
        var policy = new ShotIntentPolicy();
        Run(policy, Scene(companions: 2)); policy.Reset();
        Check(policy.Intent.Role == ShotRole.Establish && policy.Intent.RadiusScale == 1,
            "New scene retained the previous scene's intent.");
    }
    private static void ReadableNovelty()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i < 1200; i++)
            Check(director.Select(i * .1, .1, new[] { .7f, .99f }, new[] { true, true }, CinematicActivity.Idle,
                0, false, 1, true, new[] { true, false }) == 0, "Novelty picked a high-scoring unreadable alternative.");
    }
    private static void ReadableRecovery()
    {
        var director = new CinematicShotDirector();
        var eligible = new[] { true, true };
        director.Select(0, .1, new[] { .9f, .66f }, eligible, CinematicActivity.Idle, 0, false, 1, true, new[] { true, true });
        Check(director.Select(.1, .1, new[] { .57f, .66f }, eligible, CinematicActivity.Idle, 0, false, 1, false,
            new[] { false, true }) == 0, "Recovery skipped obstruction persistence.");
        // The streak-qualified readable challenger still waits for the live
        // camera's in-place repair window before recovery may switch.
        Check(director.Select(.6, .1, new[] { .57f, .66f }, eligible, CinematicActivity.Idle, 0, false, 1, false,
            new[] { false, true }) == 0, "Recovery switched during the in-place repair window.");
        Check(director.Select(1.2, .1, new[] { .57f, .66f }, eligible, CinematicActivity.Idle, 0, false, 1, false,
            new[] { false, true }) == 0, "Recovery switched during the in-place repair window.");
        Check(director.Select(1.61, .1, new[] { .57f, .66f }, eligible, CinematicActivity.Idle, 0, false, 1, false,
            new[] { false, true }) == 1, "Fixed improvement margin trapped an unreadable shot.");
        Check(director.ShotAge == 0 && director.RecentCorrections > 0, "Recovery was counted as shown footage or missed settling.");
    }
    private static void TrajectoryProfiles()
    {
        static ShotTrajectoryState Step(ShotTrajectoryPolicy policy, double time, ShotLocomotion locomotion) =>
            policy.Step(time, .1, new AutonomyPosition(0, (float)time * 5, 0), new AutonomyPosition(0, (float)time * 5, 0),
                new AutonomyPosition(0, 5, 0), CinematicActivity.Travel, ShotTrajectoryKind.Tracking, 0, 8, 0, 1,
                locomotion: locomotion);
        var boat = new ShotTrajectoryPolicy(); var run = new ShotTrajectoryPolicy();
        Step(boat, 0, ShotLocomotion.Boat); Step(run, 0, ShotLocomotion.Run);
        var slow = Step(boat, .1, ShotLocomotion.Boat); var fast = Step(run, .1, ShotLocomotion.Run);
        Check(slow.Velocity.Y > 0 && slow.Velocity.Y < fast.Velocity.Y && slow.Velocity.Y <= 1.001,
            "Vessel vertical movement ignored its acceleration profile or lost Y tracking.");
    }
    private static void RejectedProposal()
    {
        var director = new CinematicShotDirector();
        var all = new[] { true, true, true };
        for (int i = 0; i <= 100; i++)
            director.Select(i * .1, .1, new[] { .9f, .7f, .6f }, all, CinematicActivity.Idle, 0, false, 1, true, all);
        string family = director.Motion().Family;
        director.Select(10.1, .1, new[] { .7f, .95f, .6f }, all, CinematicActivity.Idle, 0, false, 1, true, all);
        Check(director.Select(10.7, .1, new[] { .7f, .95f, .6f }, all, CinematicActivity.Idle, 0, false, 1, true, all) == 1,
            "The test did not propose a new camera.");
        Check(director.RejectSelection(0) && director.SelectedIndex == 0 && director.ShotAge > 10 &&
            director.Motion().Family == family && director.RecentCorrections == 0,
            "Rejected proposal reset shown-shot progress or fabricated camera activity.");
        Check(!director.RejectSelection(0), "The same proposal was rolled back twice.");
        Check(director.Select(10.8, .1, new[] { .9f, .2f, .1f }, all, CinematicActivity.Idle, 0, false, 1, true, all) == 0 &&
            director.ShotAge > 10, "The following frame advanced the rejected candidate instead of the displayed source.");
    }
    private static void RejectedProposalChain()
    {
        var director = new CinematicShotDirector();
        var all = new[] { true, true, true };
        for (int i = 0; i <= 60; i++)
            director.Select(i * .1, .1, new[] { .9f, .7f, .6f }, all, CinematicActivity.Idle, 0, false, 1, true, all);
        director.Select(6.1, .1, new[] { .6f, .95f, .7f }, all, CinematicActivity.Idle, 0, false, 1, true, all);
        Check(director.Select(6.7, .1, new[] { .6f, .95f, .7f }, all, CinematicActivity.Idle, 0, false, 1, true, all) == 1,
            "The first proposal did not change the camera.");
        Check(director.Select(6.7, 0, new[] { .6f, 0f, .8f }, new[] { true, false, true },
            CinematicActivity.Idle, 0, false, 1, false, all) == 2,
            "The invalidated proposal did not trigger same-frame reselection.");
        Check(director.RejectSelection(0) && director.SelectedIndex == 0 && director.ShotAge > 6 && director.RecentCorrections == 0,
            "Reselection lost the original displayed-shot checkpoint.");
        Check(director.Select(6.8, .1, new[] { .9f, .2f, .1f }, all, CinematicActivity.Idle, 0, false, 1, true, all) == 0,
            "The following frame reused a rejected intermediate proposal.");
    }
    private static void AdoptAfterReset()
    {
        var director = new CinematicShotDirector();
        var all = new[] { true, true, true };
        director.Select(0, .1, new[] { .3f, .9f, .4f }, all, CinematicActivity.Idle, 0, false, 1, true, all);
        director.Reset();
        // Mode change re-evaluates candidate 1 at a different pose, but the
        // adapter has separately revalidated its actual old displayed pose.
        Check(director.Select(5, .1, new[] { .8f, .2f, .3f }, all, CinematicActivity.Idle, 0, false, 1,
            true, new[] { false, false, false }) == 0 && !director.RejectSelection(1),
            "The test unexpectedly had a prior policy checkpoint after reset.");
        director.AdoptValidatedSelection(5, 1);
        Check(director.SelectedIndex == 1 && director.ShotAge == 0,
            "Fresh displayed pose was not adopted after mode reset.");
        Check(director.Select(5.1, .1, new[] { .2f, .8f, .3f }, all, CinematicActivity.Idle, 0, false, 1, true, all) == 1 &&
            director.ShotAge > 0 && director.Motion().AzimuthOffset == 0,
            "Following frame advanced a denied candidate or decorated immediately after adopting a held pose.");
    }
}
