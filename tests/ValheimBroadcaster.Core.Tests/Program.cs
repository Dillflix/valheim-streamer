using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Empty server waits safely", EmptyRoster),
            ("Roster order and duplicate names cannot destabilize identity", StableRoster),
            ("Manual cycling wraps both directions", ManualCycling),
            ("Departed target advances to next surviving ID", DisconnectFallback),
            ("Readiness is required before dwelling", ReadinessAndDwell),
            ("Unavailable target times out without skipping multiple players", Timeout),
            ("Hold pauses automatic transitions while manual selection works", Hold),
            ("Loss of readiness resets the dwell period", LostReadiness),
            ("Disabled director preserves selection without advancing", Disabled),
            ("Roster refresh and rename preserve dwell progress", RefreshPreservesProgress),
            ("Single target stays selected through dwell and timeout", SingleTarget),
            ("Large frame advances once and discards excess time", LargeDelta),
            ("Invalid inputs fail explicitly without corrupting roster", InvalidInputs),
            ("Observer changes remove an accidentally selected observer", ObserverChange),
            ("Self test isolates the observer and normal mode excludes it again", SelfTestTransition),
            ("Solo target remains stable through cycling, readiness, and refresh", SelfTestLifetime),
            ("Zero players alternates scenery and creatures", IdleAlternation),
            ("No creatures maintains scenic coverage", ScenicFallback),
            ("Replacement scenery receives a fresh dwell without delaying ready players", ScenicReplacement),
            ("Creature loss immediately returns to scenery", CreatureLoss),
            ("Ready player preempts idle and scene recovery", PlayerPriority),
            ("Connected but unloaded player keeps idle footage", PendingPlayer),
            ("Unavailable scene recovers when loaded", SceneRecovery),
            ("Creature rotation is stable across snapshot ordering", CreatureRotation),
            ("Hold pauses idle timing but does not block ready players", IdleHold),
            ("Coverage reset clears session state", CoverageReset),
            ("Coverage rejects invalid timing", CoverageValidation),
            ("Acquisition is bounded for one target with cooldown", BoundedAcquisition),
            ("Available player rotation integrates with idle fallback", CoverageIntegration),
            ("24-hour simulated idle schedule stays valid", IdleSoak),
            ("Solo probe acquires and preserves readiness timing", ProbeReady),
            ("Solo probe timeout and stop restore inactive policy", ProbeTimeout),
            ("Solo probe bounds total duration and readiness loss", ProbeLifetime),
            ("Recent creatures are excluded until cooldown", CreatureCooldown),
            ("Distant removal excludes observer and must abort", RetentionAbort),
            ("Normal removal and retained observer remain untouched", RetentionAllowed),
            ("Probe failure survives cleanup and resets on restart", ProbeFailure),
            ("Observer activation excludes every multiplayer case", ObserverOfflineGate),
            ("Observer travel requires teleport completion, position and readiness", ObserverArrival),
            ("Observer timeout holds protections until explicit return", ObserverTimeout),
            ("Return cancels outbound travel without allowing another destination", ObserverCancel),
            ("Return releases observer only after verified arrival", ObserverReturn),
            ("Repeated return commands cannot postpone timeout", ObserverReturnRepeated),
            ("Failed return can be retried and preserves first failure", ObserverReturnRetry),
            ("World cleanup permits fresh observer activation", ObserverReset),
            ("Observer travel rejects invalid time without changing state", ObserverTiming),
            ("Short loading gaps hold only recently captured footage", FrameHoldWindow),
            ("Forced travel or failure slate cannot be hidden by a good frame", FrameHardFailure),
            ("Quick recovery cannot revive footage from before travel", FrameTravelBoundary),
            ("Frame reset and clock rollback cannot replay another session", FrameReset),
            ("Presentation clock rejects invalid values", FrameClock),
            ("Observer identity configuration accepts exact bounded Steam IDs", ObserverAllowlist),
            ("Observer handshake requires the matching protocol before its deadline", ObserverHandshake),
            ("Repeated handshake requests cannot extend an unconfirmed role", ObserverHandshakeDeadline),
            ("Revocation requests disconnect without clearing the pinned role", ObserverRevoke),
            ("Multiplayer return parks with protections until resumed", ObserverPark),
            ("Parked session cleanup cannot change the next offline return", ObserverParkReset)
        }.Concat(AutonomousDirectorTests.Cases)
            .Concat(ObserverAdmissionTests.Cases())
            .Concat(ObserverDiscoveryTests.Cases())
            .Concat(WorldInterestTests.Cases())
            .Concat(LandscapeCatalogueTests.Cases())
            .Concat(ScenicDestinationDiversityTests.Cases())
            .Concat(DestinationSubjectCoverageTests.Cases())
            .Concat(ConnectionRecoveryTests.Cases())
            .Concat(CameraReachabilityTests.Cases())
            .Concat(PlayerFollowContinuityTests.Cases())
            .Concat(RelatedPlayerViewTests.Cases())
            .Concat(ProceduralShotPolicyTests.Cases())
            .Concat(PictureQualityTests.Cases())
            .Concat(ProgramFramePolicyTests.Cases())
            .Concat(ProgramOrientationTests.Cases())
            .Concat(RoomConfinementTests.Cases())
            .Concat(NativeRankingTests.Cases())
            .Concat(ObservationalShotTests.Cases())
            .Concat(EncounterPolicyTests.Cases())
            .Concat(EncounterFramingTests.Cases())
            .Concat(PassByShotTests.Cases())
            .Concat(ElevationProfileTests.Cases())
            .Concat(GroupDollyTests.Cases())
            .Concat(RenderedPictureTests.Cases())
            .Concat(ShotLongevityTests.Cases())
            .Concat(ShotScaleTests.Cases())
            .Concat(ShotSequenceTests.Cases())
            .Concat(ActionPhaseTests.Cases())
            .Concat(ComplementaryPerspectiveTests.Cases())
            .Concat(ProceduralCameraGeometryTests.Cases())
            .Concat(CinematicShotDirectorTests.Cases())
            .Concat(ShotIntentTests.Cases())
            .Concat(PlayerActivitySelectionTests.Cases())
            .Concat(ShotTrajectoryPolicyTests.Cases())
            .Concat(ScenicShotPlanTests.Cases())
            .Concat(ScenicAvailabilityTests.Cases())
            .Concat(ScenicPictureAvailabilityTests.Cases())
            .Concat(ScenicSiteSequenceTests.Cases())
            .Concat(ScenicRecoveryTests.Cases())
            .Concat(ScenicTraceTests.Cases())
            .Concat(ScenicObservationSchedulingTests.Cases())
            .Concat(ScenicRouteWindowTests.Cases())
            .Concat(ScenicPresentationTests.Cases())
            .Concat(ScenicMotionTests.Cases())
            .Concat(ScenicDirectorTests.Cases())
            .Concat(ScenicCandidateScheduleTests.Cases())
            .Concat(CameraObservationBudgetTests.Cases())
            .Concat(CameraSpatialPlannerTests.Cases())
            .Concat(ActorSpatialHorizonTests.Cases())
            .Concat(SpatialTrajectoryTests.Cases())
            .Concat(PlaceHierarchyTests.Cases())
            .Concat(SpatialPlanningIntegrationTests.Cases())
            .Concat(ScenicSurfaceTests.Cases())
            .Concat(CreatureCameraAdmissionTests.Cases())
            .Concat(ScenicIntegrationTests.Cases())
            .Concat(ScenicReadinessTests.Cases())
            .Concat(IdlePresentationTests.Cases())
            .Concat(WildlifeCutawayPolicyTests.Cases())
            .Concat(AutonomousCutawayTests.Cases())
            .Concat(ObserverActivityProtocolTests.Cases())
            .Concat(ObserverEventProtocolTests.Cases())
            .Concat(ObservedIdleClockTests.Cases())
            .Concat(CompanionGroupPolicyTests.Cases())
            .Concat(SceneAwarenessTests.Cases())
            .Concat(PlayerCoverageTests.Cases())
            .Concat(ShotProjectionTests.Cases())
            .Concat(ShotCompositionPolicyTests.Cases())
            .Concat(ObserverContextTests.Cases()).ToArray();

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); System.Console.WriteLine("PASS " + test.Name); }
            catch (Exception error)
            {
                failed++;
                System.Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message);
            }
        }
        System.Console.WriteLine($"{cases.Length - failed}/{cases.Length} cases passed.");
        return failed == 0 ? 0 : 1;
    }

    private static BroadcastDirector Director(params string[] ids)
    {
        var director = new BroadcastDirector(10, 3);
        director.SetRoster(ids.Select(id => new PlayerTarget(id, "Player " + id)));
        director.SetEnabled(true);
        return director;
    }

    private static void EmptyRoster()
    {
        var d = Director();
        Equal(BroadcastState.WaitingForPlayers, d.State);
        False(d.Next()); False(d.Previous()); False(d.Select("missing"));
        d.Tick(100, true);
        Equal(null, d.CurrentTarget);
        d.SetRoster(new[] { new PlayerTarget("a", "Alice") });
        Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
        d.SetRoster(Array.Empty<PlayerTarget>());
        Equal(BroadcastState.WaitingForPlayers, d.State);
    }

    private static void StableRoster()
    {
        var entries = new[] {
            new PlayerTarget("b", "Same name"), new PlayerTarget("a", "Same name"),
            new PlayerTarget("self", "Observer"), new PlayerTarget("a", "Alternate name") };
        var first = new BroadcastDirector(); var second = new BroadcastDirector();
        first.SetRoster(entries, "self"); second.SetRoster(entries.Reverse(), "self");
        Equal("a,b", string.Join(",", first.Roster.Select(p => p.Id)));
        Equal(string.Join(",", first.Roster.Select(p => p.Name)), string.Join(",", second.Roster.Select(p => p.Name)));
        True(first.Select("b"));
        first.SetRoster(new[] { new PlayerTarget("a", "ZZZ"), new PlayerTarget("b", "AAA") });
        Equal("b", first.CurrentTarget!.Id);
        Equal("AAA", first.CurrentTarget.Name);
    }

    private static void ManualCycling()
    {
        var d = Director("c", "a", "b");
        Equal("a", d.CurrentTarget!.Id);
        True(d.Previous()); Equal("c", d.CurrentTarget!.Id);
        True(d.Next()); Equal("a", d.CurrentTarget!.Id);
        True(d.Next()); Equal("b", d.CurrentTarget!.Id);
        True(d.Select("c"));
        long revision = d.SelectionRevision;
        False(d.Select("not-connected")); Equal(revision, d.SelectionRevision);
        Equal("c", d.CurrentTarget!.Id);
    }

    private static void DisconnectFallback()
    {
        var d = Director("a", "b", "c"); d.Select("b");
        d.SetRoster(new[] { new PlayerTarget("c", "C"), new PlayerTarget("a", "A") });
        Equal("c", d.CurrentTarget!.Id);
        d.SetRoster(new[] { new PlayerTarget("a", "A") });
        Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
    }

    private static void ReadinessAndDwell()
    {
        var d = Director("a", "b");
        d.Tick(2, false); Equal(BroadcastState.WaitingForTarget, d.State);
        d.Tick(9, true); Equal("a", d.CurrentTarget!.Id);
        Equal(BroadcastState.Following, d.State);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
    }

    private static void Timeout()
    {
        var d = Director("a", "b", "c");
        d.Tick(2.9, false); Equal("a", d.CurrentTarget!.Id);
        d.Tick(0.1, false); Equal("b", d.CurrentTarget!.Id);
        d.Tick(0, false); Equal("b", d.CurrentTarget!.Id);
        d.Tick(3, false); Equal("c", d.CurrentTarget!.Id);
    }

    private static void Hold()
    {
        var d = Director("a", "b", "c"); d.Tick(4, true); d.SetHolding(true);
        d.Tick(1000, true); Equal("a", d.CurrentTarget!.Id);
        d.SetHolding(false); d.Tick(5, true); Equal("a", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
        d.SetHolding(true); d.Tick(1000, false); Equal("b", d.CurrentTarget!.Id);
        d.Next(); Equal("c", d.CurrentTarget!.Id);
        d.SetHolding(false); d.Tick(2, false); Equal("c", d.CurrentTarget!.Id);
        d.Tick(1, false); Equal("a", d.CurrentTarget!.Id);
    }

    private static void LostReadiness()
    {
        var d = Director("a", "b"); d.Tick(9, true); d.Tick(1, false);
        d.Tick(9, true); Equal("a", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
    }

    private static void Disabled()
    {
        var d = Director("a", "b"); d.Tick(9, true); d.SetEnabled(false);
        d.Tick(100, false); Equal(BroadcastState.Disabled, d.State); Equal("a", d.CurrentTarget!.Id);
        d.Next(); Equal("b", d.CurrentTarget!.Id); Equal(BroadcastState.Disabled, d.State);
        d.SetEnabled(true); d.Tick(9, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(1, true); Equal("a", d.CurrentTarget!.Id);
    }

    private static void RefreshPreservesProgress()
    {
        var d = Director("a", "b"); d.Tick(9, true);
        long revision = d.SelectionRevision;
        d.SetRoster(new[] { new PlayerTarget("b", "Bob"), new PlayerTarget("a", "Alice renamed") });
        Equal(revision, d.SelectionRevision); Equal(BroadcastState.Following, d.State);
        Equal("Alice renamed", d.CurrentTarget!.Name);
        d.Tick(1, true); Equal("b", d.CurrentTarget!.Id);
    }

    private static void SingleTarget()
    {
        var d = Director("a"); long revision = d.SelectionRevision;
        d.Tick(1000, true); Equal(BroadcastState.Following, d.State);
        d.Tick(1000, false); Equal(BroadcastState.WaitingForTarget, d.State);
        Equal("a", d.CurrentTarget!.Id); Equal(revision, d.SelectionRevision);
    }

    private static void LargeDelta()
    {
        var d = Director("a", "b", "c");
        d.Tick(1000, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(0, true); Equal("b", d.CurrentTarget!.Id);
        d.Tick(1000, false); Equal("c", d.CurrentTarget!.Id);
        d.Tick(0, false); Equal("c", d.CurrentTarget!.Id);
    }

    private static void InvalidInputs()
    {
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => new BroadcastDirector(invalid));
            Throws<ArgumentOutOfRangeException>(() => new BroadcastDirector(1, invalid));
        }
        var d = Director("a");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
            Throws<ArgumentOutOfRangeException>(() => d.Tick(invalid, true));
        Throws<ArgumentException>(() => new PlayerTarget(" ", "No ID"));
        Throws<ArgumentNullException>(() => d.SetRoster(null!));
        Throws<ArgumentException>(() => d.SetRoster(new PlayerTarget[] { null! }));
        Equal("a", d.CurrentTarget!.Id);
    }

    private static void ObserverChange()
    {
        var d = Director("a", "b");
        d.SetRoster(new[] { new PlayerTarget("a", "Observer"), new PlayerTarget("b", "Target") }, "a");
        Equal("b", d.CurrentTarget!.Id); Equal(1, d.Roster.Count);
    }

    private static void SelfTestTransition()
    {
        var observer = new PlayerTarget("self", "Observer");
        var other = new PlayerTarget("other", "Other player");
        var d = Director("other");
        d.Tick(5, true);
        d.SetSelfTarget(observer);
        Equal(1, d.Roster.Count);
        Equal("self", d.CurrentTarget!.Id);
        Equal(BroadcastState.WaitingForTarget, d.State);
        False(d.Select("other"));
        d.Tick(1, true);
        Equal(BroadcastState.Following, d.State);
        d.SetRoster(new[] { observer, other }, observer.Id);
        Equal("other", d.CurrentTarget!.Id);
        False(d.Select("self"));
        d.SetSelfTarget(observer);
        d.SetRoster(new[] { observer }, observer.Id);
        Equal(BroadcastState.WaitingForPlayers, d.State);
        Equal(null, d.CurrentTarget);
    }

    private static void SelfTestLifetime()
    {
        var d = new BroadcastDirector(10, 3);
        var observer = new PlayerTarget("self", "Observer");
        d.SetSelfTarget(observer);
        Equal(BroadcastState.Disabled, d.State);
        d.SetEnabled(true);
        d.Tick(100, false);
        Equal(BroadcastState.WaitingForTarget, d.State);
        d.Next(); d.Previous(); d.Tick(100, true);
        Equal("self", d.CurrentTarget!.Id);
        long revision = d.SelectionRevision;
        d.SetSelfTarget(new PlayerTarget("self", "Renamed"));
        Equal(revision, d.SelectionRevision);
        Equal(BroadcastState.Following, d.State);
        d.SetHolding(true); d.Tick(100, false);
        Equal("self", d.CurrentTarget!.Id);
        d.SetEnabled(false);
        Throws<ArgumentNullException>(() => d.SetSelfTarget(null!));
        Equal("self", d.CurrentTarget!.Id);
        Equal(BroadcastState.Disabled, d.State);
    }

    private static CoverageDirector Coverage() => new CoverageDirector { ScenicSeconds = 10, CreatureSeconds = 5 };
    private static readonly string[] Creatures = { "boar", "deer" };
    private static readonly string[] None = Array.Empty<string>();

    private static void IdleAlternation()
    {
        var c = Coverage();
        c.Tick(0, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Creature, c.Mode); Equal("boar", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); Equal("deer", c.SubjectId);
    }
    private static void ScenicFallback()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, None);
        long revision = c.Revision;
        c.Tick(86400, null, 0, true, None); Equal(CoverageMode.Scenic, c.Mode); Equal(revision + 1, c.Revision);
        c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
    }
    private static void ScenicReplacement()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, None);
        c.Tick(9, null, 0, true, None);
        long revision = c.Revision;
        c.ScenicPresentationStarted(); // Replace an obstructed shot near the end of its dwell.
        Equal(0d, c.ShotElapsed); Equal(revision, c.Revision);
        c.Tick(9, null, 0, true, None);
        Equal(CoverageMode.Scenic, c.Mode); Equal(9d, c.ShotElapsed); Equal(revision, c.Revision);
        c.Tick(1, null, 0, true, None); Equal(revision + 1, c.Revision);
        c.ScenicPresentationStarted();
        c.Tick(0, "ready", 1, true, None);
        Equal(CoverageMode.Player, c.Mode); Equal("ready", c.SubjectId);
    }
    private static void CreatureLoss()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures);
        c.Tick(0, null, 0, true, new[] { "deer" }, true);
        Equal(CoverageMode.Scenic, c.Mode); Equal(null, c.SubjectId);
    }
    private static void PlayerPriority()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures);
        c.Tick(0, "player", 1, false, Creatures); Equal(CoverageMode.Player, c.Mode); Equal("player", c.SubjectId);
        False(c.AwaitingPlayers);
        c.Tick(100, "player", 1, false, None); Equal("player", c.SubjectId);
        c.Tick(0, null, 0, true, None); Equal(CoverageMode.Scenic, c.Mode);
    }
    private static void PendingPlayer()
    {
        var c = Coverage(); c.Tick(0, null, 2, true, Creatures);
        True(c.AwaitingPlayers); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 2, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, "loaded", 2, true, Creatures); Equal(CoverageMode.Player, c.Mode); False(c.AwaitingPlayers);
    }
    private static void SceneRecovery()
    {
        var c = Coverage(); c.Tick(0, null, 1, false, Creatures); Equal(CoverageMode.Recovery, c.Mode); True(c.AwaitingPlayers);
        c.Tick(100, null, 1, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 1, true, Creatures); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, null, 1, false, Creatures); Equal(CoverageMode.Recovery, c.Mode); Equal(null, c.SubjectId);
    }
    private static void CreatureRotation()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, new[] { "deer", "boar" });
        Equal("boar", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal("deer", c.SubjectId);
        c.Tick(5, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal(CoverageMode.Scenic, c.Mode);
    }
    private static void IdleHold()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, Creatures); c.Tick(100, null, 0, true, Creatures, true);
        Equal(CoverageMode.Scenic, c.Mode);
        c.Tick(10, null, 0, true, Creatures); c.Tick(100, null, 0, true, Creatures, true); Equal(CoverageMode.Creature, c.Mode);
        c.Tick(0, "ready", 1, true, Creatures, true); Equal(CoverageMode.Player, c.Mode);
    }
    private static void CoverageReset()
    {
        var c = Coverage(); c.Tick(0, null, 1, true, Creatures); c.Tick(10, null, 1, true, Creatures);
        c.Reset(); Equal(CoverageMode.Disabled, c.Mode); False(c.AwaitingPlayers); Equal(null, c.SubjectId);
        c.Tick(0, null, 0, true, Creatures); c.Tick(10, null, 0, true, Creatures); Equal("boar", c.SubjectId);
    }
    private static void CoverageValidation()
    {
        var c = Coverage();
        Throws<ArgumentOutOfRangeException>(() => c.Tick(double.NaN, null, 0, true, None));
        Throws<ArgumentOutOfRangeException>(() => c.Tick(0, null, -1, true, None));
        Throws<ArgumentOutOfRangeException>(() => c.ScenicSeconds = 0);
        Throws<ArgumentOutOfRangeException>(() => c.CreatureSeconds = double.PositiveInfinity);
        Throws<ArgumentNullException>(() => c.Tick(0, null, 0, true, null!));
    }
    private static void BoundedAcquisition()
    {
        var a = new AcquisitionWindow();
        False(a.Tick(29, true, 20, 30)); True(a.Tick(1, true, 20, 30));
        True(a.Tick(19, true, 20, 30)); False(a.Tick(1, true, 20, 30));
        False(a.Tick(29, true, 20, 30)); True(a.Tick(1, true, 20, 30));
        False(a.Tick(0, false, 20, 30)); False(a.Active);
        False(a.Tick(0, true, 20, 30));
        Throws<ArgumentOutOfRangeException>(() => a.Tick(-1, true, 20, 30));
        True(a.Tick(100000, true, 20, 30)); // one transition, no catch-up loop
        a.Reset(); False(a.Active);
    }
    private static void CoverageIntegration()
    {
        var players = Director("a", "b"); var c = Coverage();
        c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("a", c.SubjectId);
        players.Next(); c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("b", c.SubjectId);
        players.SetRoster(new[] { new PlayerTarget("a", "A") });
        players.Tick(100, true); c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal("a", c.SubjectId);
        players.SetRoster(Array.Empty<PlayerTarget>()); c.Tick(0, players.CurrentTarget?.Id, 3, true, Creatures);
        Equal(CoverageMode.Scenic, c.Mode); True(c.AwaitingPlayers);
        players.SetRoster(new[] { new PlayerTarget("c", "C") });
        c.Tick(0, players.CurrentTarget!.Id, 3, true, Creatures); Equal(CoverageMode.Player, c.Mode);
    }
    private static void IdleSoak()
    {
        var c = Coverage(); int scenic = 0, creature = 0;
        for (int second = 0; second < 86400; second++)
        {
            c.Tick(1, null, 0, true, Creatures);
            if (c.Mode == CoverageMode.Scenic) scenic++;
            else if (c.Mode == CoverageMode.Creature) { creature++; True(Creatures.Contains(c.SubjectId)); }
            else throw new Exception("Idle schedule entered unexpected state.");
        }
        True(scenic > creature && creature > 1000);
        True(c.Revision < 20000);
    }

    private static void ProbeReady()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(5, false);
        Equal(ProbeState.Acquiring, p.State); p.Tick(3, true);
        Equal(ProbeState.Viewing, p.State); Equal(8.0, p.FirstReadySeconds!.Value);
        p.Tick(10, true); Equal(8.0, p.FirstReadySeconds!.Value); True(p.Active);
    }
    private static void ProbeTimeout()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(30, false);
        Equal(ProbeState.TimedOut, p.State); False(p.Active);
        p.Tick(1, true); Equal(ProbeState.TimedOut, p.State);
        p.Start(); Equal(null, p.FirstReadySeconds); p.Stop(); False(p.Active);
        Equal(ProbeState.Stopped, p.State);
        Throws<ArgumentOutOfRangeException>(() => p.Tick(double.NaN, false));
    }
    private static void ProbeLifetime()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(10, true); p.Tick(20, false);
        Equal(ProbeState.Acquiring, p.State); p.Tick(10, false); False(p.Active);
        p.Start(); p.Tick(10, true); p.Tick(110, true);
        Equal(ProbeState.Completed, p.State); False(p.Active);
    }
    private static void CreatureCooldown()
    {
        var c = Coverage(); c.Tick(0, null, 0, true, new[] { "boar" }); c.Tick(10, null, 0, true, new[] { "boar" });
        Equal("boar", c.SubjectId); c.Tick(5, null, 0, true, new[] { "boar" });
        long revision = c.Revision;
        c.Tick(10, null, 0, true, new[] { "boar" });
        Equal(CoverageMode.Scenic, c.Mode); True(c.Revision > revision);
        c.Tick(300, null, 0, true, new[] { "boar" }); Equal("boar", c.SubjectId);
    }

    private static void RetentionAbort()
    {
        var observer = new object(); var distantActor = new object();
        True(SceneRetention.MustAbort(true, observer, new[] { distantActor }, Array.Empty<object>()));
        True(SceneRetention.MustAbort<object>(true, null, new[] { distantActor }, Array.Empty<object>()));
        // These are the already-computed removal inputs; restoring reference alone
        // cannot make them safe. The caller must skip this pass and recompute.
        var recomputed = new[] { observer };
        False(SceneRetention.MustAbort(true, observer, recomputed, Array.Empty<object>()));
    }
    private static void RetentionAllowed()
    {
        var observer = new object();
        False(SceneRetention.MustAbort(false, observer, Array.Empty<object>(), Array.Empty<object>()));
        False(SceneRetention.MustAbort(true, observer, new[] { observer }, Array.Empty<object>()));
        False(SceneRetention.MustAbort(true, observer, Array.Empty<object>(), new[] { observer }));
    }
    private static void ProbeFailure()
    {
        var p = new SceneProbe(); p.Start(); p.Tick(0.1, false);
        p.Fail("observer excluded"); p.Stop(); p.Tick(100, true);
        Equal(ProbeState.Failed, p.State); Equal("observer excluded", p.FailureReason); False(p.Active);
        p.Start(); Equal(null, p.FailureReason); Equal(ProbeState.Acquiring, p.State);
    }

    private static void ObserverOfflineGate()
    {
        foreach (bool server in new[] { false, true })
        foreach (bool open in new[] { false, true })
        foreach (int peers in new[] { -1, 0, 1, 10 })
            Equal(server && !open && peers == 0, ObserverTravel.CanActivate(server, open, peers));
    }

    private static void ObserverArrival()
    {
        // Every premature combination must keep the observer protected in transit.
        foreach (bool teleporting in new[] { false, true })
        foreach (bool ready in new[] { false, true })
        foreach (bool atDestination in new[] { false, true })
        {
            var o = new ObserverTravel(); o.Start(); o.Travel();
            o.Tick(10, teleporting, ready, atDestination);
            Equal(!teleporting && ready && atDestination ? ObserverPhase.Watching : ObserverPhase.Travelling, o.Phase);
            True(o.Active);
        }
    }

    private static void ObserverTimeout()
    {
        var o = new ObserverTravel(); o.Start(); o.Travel();
        o.Tick(44, false, false, true); Equal(ObserverPhase.Travelling, o.Phase);
        o.Tick(1, false, false, true); Equal(ObserverPhase.Suspended, o.Phase);
        True(o.Active); True(o.Failure != null);
        o.Tick(1000, false, true, true); Equal(ObserverPhase.Suspended, o.Phase);
        Throws<InvalidOperationException>(() => o.Travel());
    }

    private static void ObserverCancel()
    {
        var o = new ObserverTravel(); o.Start(); o.Travel(); o.Tick(30, true, false, false);
        o.Return(); Equal(ObserverPhase.Returning, o.Phase); Equal(0d, o.ElapsedSeconds);
        Throws<InvalidOperationException>(() => o.Travel());
        Throws<InvalidOperationException>(() => o.Start());
        o.Tick(10, true, true, true); Equal(ObserverPhase.Returning, o.Phase);
    }

    private static void ObserverReturn()
    {
        var o = new ObserverTravel(); o.Start(); o.Return();
        o.Tick(8, false, false, true); True(o.Active);
        o.Tick(1, false, true, false); True(o.Active);
        o.Tick(1, false, true, true); False(o.Active); Equal(ObserverPhase.Off, o.Phase);
        o.Return(); o.Suspend("late failure"); Equal(ObserverPhase.Off, o.Phase);
        Equal(null, o.Failure);
    }

    private static void ObserverReturnRepeated()
    {
        var o = new ObserverTravel(); o.Start(); o.Return();
        for (int i = 0; i < 45; i++) { o.Return(); o.Tick(1, true, false, false); }
        Equal(ObserverPhase.Suspended, o.Phase); Equal(45d, o.ElapsedSeconds);
    }

    private static void ObserverReturnRetry()
    {
        var o = new ObserverTravel(); o.Start(); o.Return(); o.Suspend("first error");
        o.Suspend("secondary error"); Equal("first error", o.Failure);
        o.Return(); Equal(null, o.Failure); Equal(ObserverPhase.Returning, o.Phase);
        o.Tick(8, false, true, true); False(o.Active);
    }

    private static void ObserverReset()
    {
        var o = new ObserverTravel(); o.Start(); o.Travel(); o.Suspend("world unloaded");
        o.Reset(); o.Reset(); False(o.Active); Equal(null, o.Failure); Equal(0d, o.ElapsedSeconds);
        o.Start(); Equal(ObserverPhase.Watching, o.Phase);
        o.Tick(10000, false, false, false); Equal(ObserverPhase.Watching, o.Phase);
    }

    private static void ObserverTiming()
    {
        var o = new ObserverTravel(); o.Start(); o.Travel();
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => o.Tick(invalid, false, true, true));
        o.Tick(0, true, false, false); Equal(0d, o.ElapsedSeconds); Equal(ObserverPhase.Travelling, o.Phase);
        o.Tick(10000, true, false, false); Equal(ObserverPhase.Suspended, o.Phase);
    }

    private static void FrameHoldWindow()
    {
        var f = new FrameContinuity();
        Equal(FramePresentation.Slate, f.Select(1, false, false));
        f.Captured(1);
        Equal(FramePresentation.Hold, f.Select(1.01, false, false));
        Equal(FramePresentation.Hold, f.Select(1.74, false, false));
        Equal(FramePresentation.Slate, f.Select(1.75, false, false));
        Equal(FramePresentation.Live, f.Select(2, true, false));
        // Saying a shot is ready does not extend the lifetime of the old image.
        Equal(FramePresentation.Slate, f.Select(2.01, false, false));
        f.Captured(2.02); Equal(FramePresentation.Hold, f.Select(2.03, false, false));
    }
    private static void FrameHardFailure()
    {
        var f = new FrameContinuity(); f.Captured(1);
        Equal(FramePresentation.Slate, f.Select(1.01, true, true));
        Equal(FramePresentation.Slate, f.Select(1.01, false, true));
    }
    private static void FrameTravelBoundary()
    {
        var f = new FrameContinuity(); f.Captured(1);
        Equal(FramePresentation.Hold, f.Select(1.01, false, false));
        Equal(FramePresentation.Slate, f.Select(1.02, false, true));
        // Travel may finish before the old hold expires. It must not return.
        Equal(FramePresentation.Slate, f.Select(1.03, false, false));
        Equal(FramePresentation.Live, f.Select(1.04, true, false));
        Equal(FramePresentation.Slate, f.Select(1.05, false, false));
        // Only a newly captured live image can restore short-gap continuity.
        f.Captured(1.06);
        Equal(FramePresentation.Hold, f.Select(1.07, false, false));
    }
    private static void FrameReset()
    {
        var f = new FrameContinuity(); f.Captured(5);
        Equal(FramePresentation.Slate, f.Select(4, false, false));
        f.Reset(); Equal(FramePresentation.Slate, f.Select(5.01, false, false));
    }
    private static void FrameClock()
    {
        var f = new FrameContinuity();
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity })
        { Throws<ArgumentOutOfRangeException>(() => f.Captured(bad)); Throws<ArgumentOutOfRangeException>(() => f.Select(bad, true, false)); }
    }
    private static void ObserverAllowlist()
    {
        Equal(0, ObserverProtocol.SteamAllowlist("").Count);
        var ids = ObserverProtocol.SteamAllowlist("76561198000000001, 76561198000000001,76561198000000002");
        Equal(2, ids.Count); False(ids.Contains("Jason"));
        foreach (string bad in new[] { "Jason", "7656119800000000", "Steam_76561198000000001", "+76561198000000001", "76561198000000001;76561198000000002" })
            Throws<ArgumentException>(() => ObserverProtocol.SteamAllowlist(bad));
        Throws<ArgumentException>(() => ObserverProtocol.SteamAllowlist(string.Join(",", Enumerable.Range(1, 17).Select(i => (76561198000000000L + i).ToString()))));
    }
    private static void ObserverHandshake()
    {
        var lease = new ObserverLease(10);
        False(lease.Confirm(ObserverProtocol.Version + 1, 11)); False(lease.Confirmed);
        True(lease.Confirm(ObserverProtocol.Version, 12));
        False(lease.MustDisconnect(100000, true));
    }
    private static void ObserverHandshakeDeadline()
    {
        var lease = new ObserverLease(10);
        for (int i = 0; i < 15; i++) False(lease.Confirm(999, 10 + i));
        True(lease.MustDisconnect(25, true)); False(lease.Confirm(ObserverProtocol.Version, 25));
        False(lease.Confirm(ObserverProtocol.Version, 9));
    }
    private static void ObserverRevoke()
    {
        var lease = new ObserverLease(10); True(lease.Confirm(ObserverProtocol.Version, 11));
        True(lease.MustDisconnect(12, false)); True(lease.Confirmed);
        True(lease.MustDisconnect(9, true));
        Throws<ArgumentOutOfRangeException>(() => lease.MustDisconnect(double.NaN, true));
    }
    private static void ObserverPark()
    {
        var o = new ObserverTravel(); o.Start(); o.Travel(); o.Return(true);
        o.Tick(8, false, true, true); Equal(ObserverPhase.Parked, o.Phase); True(o.Active);
        o.Tick(1000, false, true, true); o.Return(); Equal(ObserverPhase.Parked, o.Phase);
        Throws<InvalidOperationException>(() => o.Travel());
        o.Resume(); Equal(ObserverPhase.Watching, o.Phase); True(o.Active);
        Throws<InvalidOperationException>(() => o.Resume());
    }
    private static void ObserverParkReset()
    {
        var o = new ObserverTravel(); o.Start(); o.Return(true); o.Tick(8, false, true, true);
        o.Reset(); o.Start(); o.Return(); o.Tick(8, false, true, true);
        Equal(ObserverPhase.Off, o.Phase); False(o.Active);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {(object?)expected ?? "<null>"}, got {(object?)actual ?? "<null>"}.");
    }

    private static void True(bool value) { if (!value) throw new Exception("Expected true."); }
    private static void False(bool value) { if (value) throw new Exception("Expected false."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }
}
