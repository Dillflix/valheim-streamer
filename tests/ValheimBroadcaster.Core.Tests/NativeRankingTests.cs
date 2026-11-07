using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class NativeRankingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native quality bands keep readable footage above every obscured score", QualityBands);
        yield return ("Native score encoding rejects non-finite and unbounded feedback", InvalidQuality);
        yield return ("External camera ranking supplies the actual initial selection", AuthoritativeRank);
        yield return ("External ranking cannot admit physically invalid camera geometry", Eligibility);
        yield return ("External ranking cannot prefer obscured over readable coverage", Readability);
        yield return ("External ranking cannot manufacture an empty camera set", FalseEmpty);
        yield return ("Native selection retains recovery persistence independently of quality score", RecoveryTiming);
        yield return ("Native selection retains a healthy shot until its minimum dwell", HealthyDwell);
        yield return ("External ranker receives the actual incumbent for stable quality ties", Incumbent);
        yield return ("Rejected native selection rolls back the original displayed shot and dwell", RejectedProposal);
        yield return ("Editorial alternative ranking uses native selection with qualified candidates", PlannedRanking);
        yield return ("Unavailable footage cannot trigger a native editorial sequence cut", UnshownFootage);
        yield return ("Native comparison evidence expires across a presentation observation gap", EvidenceGap);
        yield return ("An actually empty native candidate set remains unavailable", Empty);
    }

    private sealed class Ranker : IShotCandidateRanker
    {
        internal int Forced = -2, Calls, LastIncumbent = -1, AlternativeCalls;
        public int Best(IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
            IReadOnlyList<bool>? readable, int incumbent, IReadOnlyList<int>? priorities = null)
        {
            Calls++; LastIncumbent = incumbent;
            int available = 0;
            for (int i = 0; i < eligible.Count; i++) if (eligible[i]) available++;
            if (incumbent == -1 && available > 0 && available < eligible.Count) AlternativeCalls++;
            // Test double exercises the injection/timing contract. It does not
            // claim to execute Unity or the real ClearShot component.
            return Forced != -2 ? Forced : ShotCandidateRanking.Best(scores, eligible, readable, incumbent, priorities: priorities);
        }
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("Invalid camera evidence was accepted.");
    }
    private static void QualityBands()
    {
        Check(ShotCandidateRanking.NativeQuality(0, true) > ShotCandidateRanking.NativeQuality(1, false),
            "Editorial quality overcame readable-first ordering.");
        Check(ShotCandidateRanking.NativeQuality(.8f, true) > ShotCandidateRanking.NativeQuality(.7f, true),
            "Same-health quality ordering was lost.");
        Check(ShotCandidateRanking.NativeQuality(.8f, false) > ShotCandidateRanking.NativeQuality(.7f, false),
            "All-poor candidates cannot make measured progress.");
    }
    private static void InvalidQuality()
    {
        foreach (float score in new[] { -.01f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws(() => ShotCandidateRanking.NativeQuality(score, true));
    }
    private static void AuthoritativeRank()
    {
        var rank = new Ranker { Forced = 1 };
        var p = new ProceduralShotPolicy();
        Check(p.Select(0, new[] { .9f, .7f }, new[] { true, true }, ranker: rank) == 1 && rank.Calls == 1,
            "The old custom winner silently overrode the native ranker.");
    }
    private static void Eligibility()
    {
        var rank = new Ranker { Forced = 0 };
        Throws(() => new ProceduralShotPolicy().Select(0, new[] { 1f, .5f }, new[] { false, true }, ranker: rank));
    }
    private static void Readability()
    {
        var rank = new Ranker { Forced = 0 };
        Throws(() => new ProceduralShotPolicy().Select(0, new[] { 1f, .1f }, new[] { true, true }, new[] { false, true }, rank));
    }
    private static void FalseEmpty()
    {
        var rank = new Ranker { Forced = -1 };
        Throws(() => new ProceduralShotPolicy().Select(0, new[] { .1f }, new[] { true }, ranker: rank));
    }
    private static void RecoveryTiming()
    {
        var rank = new Ranker(); var p = new ProceduralShotPolicy();
        var scores = new[] { .9f, .3f }; var eligible = new[] { true, true };
        Check(p.Select(0, scores, eligible, new[] { true, false }, rank) == 0, "Initial shot missing.");
        var health = new[] { false, true };
        Check(p.Select(.1, scores, eligible, health, rank) == 0, "One fresh sample caused a cut.");
        Check(p.Select(.4, scores, eligible, health, rank) == 0, "Recovery persistence was bypassed.");
        // The repair window protects the eligible live camera before a
        // streak-qualified readable challenger may take over.
        Check(p.Select(.46, scores, eligible, health, rank) == 0, "Recovery ignored the in-place repair window.");
        Check(p.Select(1.4, scores, eligible, health, rank) == 0, "Recovery ignored the in-place repair window.");
        Check(p.Select(1.61, scores, eligible, health, rank) == 1, "Readable recovery waited for healthy-shot dwell.");
    }
    private static void HealthyDwell()
    {
        // Dwell-then-switch survives only without readability evidence; with
        // it a readable live shot is never replaced by a better score.
        var rank = new Ranker(); var p = new ProceduralShotPolicy();
        var eligible = new[] { true, true };
        p.Select(0, new[] { .8f, .5f }, eligible, null, rank);
        var improved = new[] { .7f, 1f };
        for (int i = 1; i < 50; i++) Check(p.Select(i * .1, improved, eligible, null, rank) == 0, "Healthy dwell lost.");
        Check(p.Select(5, improved, eligible, null, rank) == 1, "Confirmed better shot never activated.");
    }
    private static void Incumbent()
    {
        var rank = new Ranker { Forced = 1 }; var p = new ProceduralShotPolicy();
        p.Select(0, new[] { .8f, .8f }, new[] { true, true }, ranker: rank);
        rank.Forced = -2;
        Check(p.Select(.1, new[] { .8f, .8f }, new[] { true, true }, ranker: rank) == 1 && rank.LastIncumbent == 1,
            "A quality tie lost the actual current camera identity.");
    }
    private static void RejectedProposal()
    {
        // Rollback semantics are exercised on the legacy no-readability path,
        // where improvement proposals can still arise against a healthy shot.
        var rank = new Ranker(); var p = new ProceduralShotPolicy(); var eligible = new[] { true, true };
        p.Select(0, new[] { .8f, .5f }, eligible, null, rank);
        for (int i = 1; i <= 50; i++) p.Select(i * .1, new[] { .7f, 1f }, eligible, null, rank);
        Check(p.SelectedIndex == 1 && p.RejectSelection(0) && p.SelectedIndex == 0, "Denied transition retained the proposed camera.");
        p.Select(5.1, new[] { .7f, 1f }, eligible, null, rank);
        Check(p.Select(5.61, new[] { .7f, 1f }, eligible, null, rank) == 1,
            "Rejecting an unshown proposal invented a fresh healthy dwell for the old shot.");
    }
    private static void PlannedRanking()
    {
        // Deliberate planned edits remain only for callers without readability
        // evidence; with it the director never plans against a readable shot.
        var rank = new Ranker(); var p = new CinematicShotDirector();
        var scores = new[] { .8f, .79f, .78f }; var eligible = new[] { true, true, true };
        int first = -1; bool changed = false;
        for (int i = 0; i < 400; i++)
        {
            int chosen = p.Select(i * .1, .1, scores, eligible, CinematicActivity.Idle, 0, false, 1,
                ranker: rank);
            if (first < 0) first = chosen;
            changed |= chosen != first;
        }
        Check(changed && rank.AlternativeCalls > 0 && rank.Calls > 800,
            "The director did not delegate its qualified editorial alternatives to the native ranker.");
    }
    private static void UnshownFootage()
    {
        var rank = new Ranker(); var p = new CinematicShotDirector();
        var scores = new[] { .8f, .79f }; var eligible = new[] { true, true }; var readable = new[] { true, true };
        for (int i = 0; i < 600; i++)
            Check(p.Select(i * .1, .1, scores, eligible, CinematicActivity.Idle, 0, false, 1,
                usableFootage: false, readable: readable, ranker: rank) == 0,
                "Held or slate time triggered an editorial camera change.");
        Check(p.ShotAge == 0 && rank.AlternativeCalls == 0, "Unshown footage earned native editorial duration.");
    }
    private static void EvidenceGap()
    {
        var rank = new Ranker(); var p = new ProceduralShotPolicy(); var eligible = new[] { true, true };
        p.Select(0, new[] { .8f, .5f }, eligible, ranker: rank);
        p.Select(.1, new[] { .6f, .9f }, eligible, ranker: rank);
        Check(p.Select(5, new[] { .6f, .9f }, eligible, ranker: rank) == 0, "Stale pending evidence survived a gap.");
        Check(p.Select(5.51, new[] { .6f, .9f }, eligible, ranker: rank) == 1, "Fresh native evidence did not resume selection.");
    }
    private static void Empty()
    {
        Check(new ProceduralShotPolicy().Select(0, new[] { 1f }, new[] { false }, ranker: new Ranker()) == -1,
            "Native ranking resurrected an ineligible camera.");
    }
}
