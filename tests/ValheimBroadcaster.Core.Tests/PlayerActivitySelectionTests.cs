using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class PlayerActivitySelectionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Activity selection requires persistent fresh evidence", Persistence);
        yield return ("Stale low-confidence and implausibly future activity cannot rank", Freshness);
        yield return ("Unknown companion action preserves scene identity without inventing activity", SceneIdentity);
        yield return ("Loaded current-scene activity is preferred over costly equivalent acquisition", AcquisitionCost);
        yield return ("Overdue uncovered players outrank activity and retain longest-wait ordering", Fairness);
        yield return ("Departed player activity and scene identity are removed", Departure);
        yield return ("Observed encounters and aftermath have a hard extension deadline", EncounterBound);
        yield return ("Unreadable stale or overdue alternatives prevent encounter retention", RetentionVeto);
        yield return ("Autonomous activity chooses sustained action without interrupting nominal dwell", AutonomousChoice);
        yield return ("Autonomous encounters retain beyond dwell but resume fair rotation", AutonomousRetention);
        yield return ("Manual next overrides encounter retention and activity order", ManualOverride);
        yield return ("Unusable player footage pauses dwell without declaring acquisition loss", UnreadableFootage);
        yield return ("Ordinary director retains encounters and respects readable-footage timing", OrdinaryDirector);
        yield return ("Activity evidence reset preserves strict monotonic-session behavior", Lifecycle);
        yield return ("Specific replicated work outranks generic action without inventing combat", EventSpecificity);
        yield return ("Fresh positive server evidence survives weak local idle and unknown context", EvidenceMerge);
        yield return ("Sustained bad picture can select only another local ready player", LocalPictureFallback);
        yield return ("Picture fallback cooldown prevents rapid cycling between bad viewpoints", PictureCooldown);
        yield return ("Readable recovery and manual hold reset the bad-picture deadline", PictureReset);
        yield return ("Ordinary picture recovery requires explicit local ready alternatives", OrdinaryPictureFallback);
        yield return ("Startup acquisition and target departure remain independent of picture recovery", PhysicalLifecycle);
    }
    private static readonly string[] Ids = { "a", "b", "c" };
    private static readonly AutonomyPosition Origin = new AutonomyPosition(0, 0, 0);
    private static readonly ScenicRegion[] NoRegions = Array.Empty<ScenicRegion>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static PlayerActivityEvidence E(string id, double now, PlayerActivityKind kind = PlayerActivityKind.Action, float confidence = .8f, long scene = 0) =>
        new PlayerActivityEvidence(id, kind, confidence, now + 5, scene);
    private static void Update(PlayerActivitySelection policy, double now, params PlayerActivityEvidence[] evidence) => policy.Update(now, Ids, evidence);
    private static void Persistence()
    {
        var p = new PlayerActivitySelection(); Update(p, 0, E("c", 0)); Update(p, 1, E("c", 1));
        Check(p.Rank("c", 1, false, 0) == p.Rank("b", 1, false, 0), "A transient event influenced selection.");
        Update(p, 2, E("c", 2)); Check(p.Rank("c", 2, false, 0) > p.Rank("b", 2, false, 0), "Persistent action did not matter.");
        Update(p, 10, E("c", 10)); Check(p.Rank("c", 10, false, 0) == p.Rank("b", 10, false, 0), "A long gap proved continuous activity.");
        Update(p, 11); Update(p, 12, E("c", 12));
        Check(p.Rank("c", 12, false, 0) == p.Rank("b", 12, false, 0), "Missing activity retained its old persistence.");
    }
    private static void Freshness()
    {
        var p = new PlayerActivitySelection();
        for (int t = 0; t < 5; t++) Update(p, t, new PlayerActivityEvidence("a", PlayerActivityKind.Combat, 1, 0),
            E("b", t, PlayerActivityKind.Combat, .2f), new PlayerActivityEvidence("c", PlayerActivityKind.Combat, 1, 1000));
        Check(p.Rank("a", 4, true, 0) == p.Rank("b", 4, true, 0) && p.Rank("b", 4, true, 0) == p.Rank("c", 4, true, 0),
            "Stale, low-confidence or unbounded evidence acquired priority.");
    }
    private static void SceneIdentity()
    {
        var p = new PlayerActivitySelection(); Update(p, 0, E("b", 0, PlayerActivityKind.Unknown, 1, 7));
        Check(p.SceneId("b", 0) == 7 && p.Rank("b", 0, true, 7) > p.Rank("a", 0, true, 7), "Known companion scene was discarded.");
        Check(!p.RetainEncounter("b", 0, true, false), "An unknown companion action invented combat.");
        Update(p, 1); Check(p.SceneId("b", 1) == 0, "Missing scene evidence persisted.");
    }
    private static void AcquisitionCost()
    {
        var p = new PlayerActivitySelection();
        for (int t = 0; t <= 2; t++) Update(p, t, E("a", t, scene: 5), E("b", t, scene: 5), E("c", t));
        Check(p.Rank("a", 2, true, 5) > p.Rank("c", 2, false, 5), "Equivalent distant action beat a loaded encounter.");
        Check(p.Rank("b", 2, true, 5) > p.Rank("c", 2, true, 5), "Persistent scene offered no continuity preference.");
    }
    private static void Fairness()
    {
        var p = new PlayerActivitySelection();
        for (int t = 0; t <= 130; t++)
        { Update(p, t, E("a", t, PlayerActivityKind.Combat)); p.MarkCovered("a", t); if (t == 5) p.MarkCovered("c", t); }
        Check(p.Overdue("b", 130) && p.Rank("b", 130, false, 0) > p.Rank("a", 130, true, 0), "Activity starved an overdue player.");
        Check(p.Rank("b", 130, false, 0) > p.Rank("c", 130, false, 0), "Oldest overdue player was not first.");
    }
    private static void Departure()
    {
        var p = new PlayerActivitySelection(); Update(p, 0, E("a", 0, scene: 8)); p.Update(1, new[] { "b" }, new[] { E("a", 1, scene: 8) });
        Check(p.SceneId("a", 1) == 0 && !p.Overdue("a", 1000), "A departed player retained scheduling evidence.");
    }
    private static void EncounterBound()
    {
        var p = new PlayerActivitySelection();
        for (int t = 0; t <= 2; t++) Update(p, t, E("a", t, PlayerActivityKind.Combat));
        Check(p.RetainEncounter("a", 2, true, false), "Confirmed combat was not retained.");
        for (int t = 3; t < 22; t++) { Update(p, t, E("a", t, PlayerActivityKind.Combat)); Check(p.RetainEncounter("a", t, true, false), "Encounter ended early."); }
        Update(p, 22, E("a", 22, PlayerActivityKind.Combat)); Check(!p.RetainEncounter("a", 22, true, false), "Continuous combat extended retention forever.");
        p.ResetRetention(); Check(p.RetainEncounter("a", 22, true, false), "A new selected encounter could not begin.");
        Update(p, 23, E("a", 23, PlayerActivityKind.Idle)); Check(p.RetainEncounter("a", 23, true, false), "Immediate aftermath was lost.");
        Update(p, 25, E("a", 25, PlayerActivityKind.Idle)); Check(!p.RetainEncounter("a", 25, true, false), "Aftermath lingered beyond its bound.");
    }
    private static void RetentionVeto()
    {
        var p = new PlayerActivitySelection(); for (int t = 0; t <= 2; t++) Update(p, t, E("a", t, PlayerActivityKind.Combat));
        Check(!p.RetainEncounter("a", 2, false, false) && !p.RetainEncounter("a", 2, true, true), "Unreadability or starvation was overridden by combat.");
        Update(p, 3); Check(!p.RetainEncounter("a", 3, true, false), "Missing activity held an encounter.");
    }
    private static AutonomousDirector Director(double dwell = 5) { var d = new AutonomousDirector { DwellSeconds = dwell }; d.SetEnabled(true); return d; }
    private static AutonomyPlayer[] Players() => new[] { new AutonomyPlayer("a", "a", Origin, 10000, true), new AutonomyPlayer("b", "b", Origin, 10000, true), new AutonomyPlayer("c", "c", Origin, 10000, true) };
    private static void Tick(AutonomousDirector d, double now, PlayerActivityEvidence[]? evidence = null, bool readable = true) =>
        d.Tick(now, 1, Players(), NoRegions, Origin, d.TargetId ?? "a", false, activity: evidence, usablePlayerFootage: readable);
    private static void AutonomousChoice()
    {
        var d = Director(); for (int t = 0; t < 4; t++) { Tick(d, t, new[] { E("c", t) }); Check(d.TargetId == "a", "Activity cut short nominal dwell."); }
        Tick(d, 4, new[] { E("c", 4) }); Check(d.TargetId == "c", "Sustained action did not influence next selection.");
    }
    private static void AutonomousRetention()
    {
        var d = Director();
        for (int t = 0; t < 24; t++) { Tick(d, t, new[] { E("a", t, PlayerActivityKind.Combat) }); Check(d.TargetId == "a", "Combat was interrupted before extension elapsed."); }
        Tick(d, 24, new[] { E("a", 24, PlayerActivityKind.Combat) }); Check(d.TargetId == "b", "Combat retention bypassed its hard cap.");
    }
    private static void ManualOverride()
    {
        var d = Director(); for (int t = 0; t < 6; t++) Tick(d, t, new[] { E("a", t, PlayerActivityKind.Combat), E("c", t) });
        d.Next(); Tick(d, 6, new[] { E("a", 6, PlayerActivityKind.Combat), E("c", 6) });
        Check(d.TargetId == "b", "Manual next was replaced by encounter retention or activity priority.");
    }
    private static void UnreadableFootage()
    {
        var d = Director(); var solo = new[] { Players()[0] };
        d.Tick(0, 1, solo, NoRegions, Origin, "a", false); double dwell = d.ReadyDwellSeconds;
        for (int t = 1; t <= 90; t++) d.Tick(t, 1, solo, NoRegions, Origin, "a", false, usablePlayerFootage: false);
        Check(d.TargetId == "a" && d.ReadyDwellSeconds == dwell && d.State == AutonomyState.Following && d.PendingTravel == null && d.FailedDestinationCount == 0,
            "Foliage paused physical continuity, triggered travel, or earned viewing credit.");
    }
    private static void OrdinaryDirector()
    {
        var d = new BroadcastDirector(5); d.SetRoster(new[] { new PlayerTarget("a", "a"), new PlayerTarget("b", "b") }); d.SetEnabled(true);
        for (int t = 0; t <= 5; t++) { d.RecordCoverage(t, 1, Array.Empty<string>()); d.Tick(1, true, new[] { E("a", t, PlayerActivityKind.Combat) }); }
        Check(d.CurrentTarget?.Id == "a", "Ordinary mode ignored encounter retention.");
        d.Next(); Check(d.CurrentTarget?.Id == "b", "Ordinary manual next did not override retention.");
        for (int t = 6; t < 90; t++) { d.RecordCoverage(t, 1, Array.Empty<string>()); d.Tick(1, true, usablePlayerFootage: false); }
        Check(d.CurrentTarget?.Id == "b" && d.State == BroadcastState.Following, "Unreadable normal footage advanced dwell or lost physical readiness.");
    }
    private static void EventSpecificity()
    {
        var work = new ObserverPlayerEvents("a", "char", true, 0, 0, 0, 0, null, 0);
        Check(PlayerActivitySelection.FromEvents(work, 0)?.Kind == PlayerActivityKind.Work, "Generic crafting animation hid specific work evidence.");
        var damage = new ObserverPlayerEvents("a", "char", true, 0, 0, 0, 0, 0, 0);
        Check(PlayerActivitySelection.FromEvents(damage, 0)?.Kind == PlayerActivityKind.Action, "Health loss was mislabeled as combat or ignored behind work.");
        Check(PlayerActivitySelection.FromEvents(work.Elapsed(6), 6) == null, "Stale event evidence was renewed.");
    }
    private static void EvidenceMerge()
    {
        var remote = E("a", 0, PlayerActivityKind.Work);
        Check(PlayerActivitySelection.Merge(E("a", 0, PlayerActivityKind.Idle), remote, 0)?.Kind == PlayerActivityKind.Work, "Local idle erased replicated crafting.");
        var companion = E("a", 0, PlayerActivityKind.Unknown, 1, 42);
        var merged = PlayerActivitySelection.Merge(companion, remote, 0);
        Check(merged?.Kind == PlayerActivityKind.Work && merged.SceneId == 42, "Unknown companion scene erased server work or lost continuity.");
        Check(PlayerActivitySelection.Merge(E("a", 0, PlayerActivityKind.Combat, .9f), remote, 0)?.Kind == PlayerActivityKind.Combat,
            "Strong local encounter evidence was overwritten by generic remote information.");
        Check(PlayerActivitySelection.Merge(E("a", 0, PlayerActivityKind.Combat, .2f), remote, 0)?.Kind == PlayerActivityKind.Work,
            "Weak local inference erased reliable work evidence.");
    }
    private static void LocalPictureFallback()
    {
        var d = Director(60); Tick(d, 0);
        for (int t = 1; t <= 12; t++) { Tick(d, t, readable: false); Check(d.TargetId == "a", "Picture search window was not respected."); }
        Tick(d, 13, readable: false); Check(d.TargetId == "b" && d.PendingTravel == null, "Loaded fallback failed or requested travel.");
        d = Director(60); var distant = new[] { Players()[0], new AutonomyPlayer("b", "b", new AutonomyPosition(1000, 0, 0), 10000, false, 1000) };
        for (int t = 0; t < 90; t++) d.Tick(t, 1, distant, NoRegions, Origin, "a", false, usablePlayerFootage: false);
        Check(d.TargetId == "a" && d.PendingTravel == null && d.FailedDestinationCount == 0, "Bad foliage selected or travelled to a distant player.");
    }
    private static void PictureCooldown()
    {
        var d = Director(60); var pair = new[] { Players()[0], Players()[1] };
        for (int t = 0; t <= 12; t++) d.Tick(t, 1, pair, NoRegions, Origin, d.TargetId ?? "a", false, usablePlayerFootage: false);
        Check(d.TargetId == "b", "Initial local recovery did not occur after twelve seconds.");
        for (int t = 13; t < 42; t++) { d.Tick(t, 1, pair, NoRegions, Origin, "b", false, usablePlayerFootage: false); Check(d.TargetId == "b", "A failed picture was retried inside its cooldown."); }
        d.Tick(42, 1, pair, NoRegions, Origin, "b", false, usablePlayerFootage: false);
        Check(d.TargetId == "a", "A failed picture could never be retried after its cooldown.");
    }
    private static void PictureReset()
    {
        var d = Director(60); for (int t = 0; t < 10; t++) Tick(d, t, readable: false);
        Tick(d, 10, readable: true); for (int t = 11; t < 22; t++) Tick(d, t, readable: false);
        Check(d.TargetId == "a", "Readable recovery retained an old obstruction timer.");
        d.SetHolding(true); for (int t = 22; t < 60; t++) Tick(d, t, readable: false);
        Check(d.TargetId == "a", "Manual hold allowed picture-driven target changes.");
        d.SetHolding(false); Tick(d, 60, readable: false); Check(d.TargetId == "a", "Unholding inherited the held obstruction deadline.");
    }
    private static void OrdinaryPictureFallback()
    {
        var d = new BroadcastDirector(60); d.SetRoster(new[] { new PlayerTarget("a", "a"), new PlayerTarget("b", "b") }); d.SetEnabled(true);
        for (int t = 0; t < 20; t++) { d.RecordCoverage(t, 1, Array.Empty<string>()); d.Tick(1, true, usablePlayerFootage: false); }
        Check(d.CurrentTarget?.Id == "a", "Ordinary mode guessed another target was locally ready.");
        d.RecordCoverage(20, 1, Array.Empty<string>()); d.Tick(1, true, usablePlayerFootage: false, locallyReadyIds: new[] { "b" });
        Check(d.CurrentTarget?.Id == "b", "Explicitly ready local alternative was not used.");
    }
    private static void PhysicalLifecycle()
    {
        var d = Director(60); var pair = new[] { Players()[0], Players()[1] };
        for (int t = 0; t < 20; t++) d.Tick(t, 1, pair, NoRegions, Origin, null, false, usablePlayerFootage: false);
        Check(d.TargetId == "a" && d.State == AutonomyState.Acquiring, "Startup without any presented picture triggered quality fallback.");
        d = Director(60);
        for (int t = 0; t <= 12; t++) d.Tick(t, 1, pair, NoRegions, Origin, d.TargetId ?? "a", false, usablePlayerFootage: false);
        Check(d.TargetId == "b", "Fixture did not leave a failed first view.");
        d.Tick(13, 1, new[] { pair[0] }, NoRegions, Origin, null, false, usablePlayerFootage: false);
        Check(d.TargetId == "a", "A departed target's picture state prevented normal surviving-player acquisition.");
    }
    private static void Lifecycle()
    {
        var p = new PlayerActivitySelection(); Update(p, 100, E("a", 100)); p.Reset(); Update(p, 0);
        Check(p.SceneId("a", 0) == 0 && !p.Overdue("a", 0), "Session reset retained old evidence.");
        bool rejected = false; try { Update(p, -1); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Negative observation time was accepted.");
    }
}
