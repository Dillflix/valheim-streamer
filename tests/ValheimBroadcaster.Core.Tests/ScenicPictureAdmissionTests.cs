using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicPictureAdmissionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Captured sparse first-picture offers cannot starve an unfinished 2212-renderer admission", SparseOfferReplay);
        yield return ("Independent admission retains the same measured pose while later graph nodes and route resets arrive", StableProposal);
        yield return ("Dense-scene admission at 30fps completes inside the existing bounded survey lifetime", DenseScene);
        yield return ("Independent admission rejects stale proposals and revokes pending work across scene and region changes", EvidenceLifetime);
        yield return ("Confirmed pose rejection clears pending admission without manufacturing a usable picture", Contradiction);
        yield return ("Pending rejected and admitted but unrendered pictures cannot restart global scenic recovery", GlobalRecovery);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicShotPlan Picture(long id = 7) => ScenicShotPlan.Picture(id,
        new ScenicFeature("coast", ScenicFeatureKind.Coastline, new AutonomyPosition(0, 20, 0), new AutonomyPosition(1, 0, 0), 10, 5, .9f),
        ScenicShotRole.Establish, new AutonomyPosition(20, 25, 0), new AutonomyPosition(0, 20, 0), 65, .06f, -.08f);

    private static void SparseOfferReplay()
    {
        // 0.8.42 trace b652b02d: a 2212-renderer projection received only nine
        // calls, tied to newly offered nodes, and never finished collection plus
        // refinement before relocation57.916. The trace does not include an
        // eventual completion; ten calls is the minimal counterexample beyond
        // the nine observed incomplete evaluations, not a guessed mesh result.
        double[] offers = { 56.839073, 56.872406, 56.939213, 57.029301, 57.079300,
            57.145966, 57.649467, 57.716133, 57.766136 };
        Check(offers.Length < 10, "Recorded demand-only schedule unexpectedly completed the work");
        var admission = new ScenicPictureAdmission();
        Check(admission.TryQueue(Picture(), 3, 3, 56.789070, offers[0]), "Measured candidate not retained");
        int calls = 0; double acceptedAt = 0; ScenicShotPlan? accepted = null;
        for (int frame = 0; frame < 60; frame++)
        {
            double now = offers[0] + frame / 60d;
            admission.Advance(3, now, frame, candidate => {
                Check(candidate.Id == 7, "Continuing admission silently changed its exact pose");
                return ++calls >= 10 ? ScenicPictureAdmissionResult.Accepted : ScenicPictureAdmissionResult.Pending;
            }, out accepted);
            if (accepted != null) { acceptedAt = now; break; }
        }
        Check(accepted?.Id == 7 && calls == 10 && acceptedAt < 57.1,
            "Admission still waits for another graph node rather than progressing every frame");
        Check(acceptedAt < 57.916, "The demonstrated outage was hidden by extending the recovery deadline");
    }
    private static void StableProposal()
    {
        var admission = new ScenicPictureAdmission(); var first = Picture();
        admission.TryQueue(first, 3, 3, 1, 1);
        for (int i = 0; i < 10; i++)
        {
            double now = 1 + i / 60d;
            Check(!admission.TryQueue(Picture(i + 100), 3, 3, now, now), "A new node replaced unfinished independent admission");
            admission.Advance(3, now, i, candidate => {
                Check(ReferenceEquals(candidate, first), "Candidate changed after unrelated route search work");
                return ScenicPictureAdmissionResult.Pending;
            }, out _);
            admission.Advance(3, now, i, _ => throw new Exception("Duplicate work in the same frame"), out _);
        }
        Check(admission.Evaluations == 10 && ReferenceEquals(admission.Pending, first), "Pending work lost continuity");
    }
    private static void DenseScene()
    {
        var admission = new ScenicPictureAdmission(); admission.TryQueue(Picture(), 3, 3, 0, 0);
        // A dense8192-entry scene requires32 collection slices even before
        // refinement. A0.8s proposal lifetime cannot cover those32 frames at30fps.
        // The actual validator remains authoritative; this script keeps its
        // pending result for collection/refinement then completes all checks.
        int calls = 0; ScenicShotPlan? accepted = null; double at = 0;
        for (int frame = 0; frame < 90; frame++)
        {
            at = frame / 30d;
            admission.Advance(3, at, frame, _ => ++calls <= 40
                ? ScenicPictureAdmissionResult.Pending : ScenicPictureAdmissionResult.Accepted, out accepted);
            if (accepted != null) break;
        }
        Check(accepted != null && at > .8 && at < ScenicObservationPolicy.SurveyLifetime, "Normal dense-scene progress starved");
        var availability = new ScenicPictureAvailability();
        Check(!availability.TryOffer(accepted!, 3, 3, 0, at, false), "Old proposal observation was mistaken for current evidence");
        Check(availability.TryOffer(accepted!, 3, 3, at, at, false), "Completed current validation could not admit its exact pose");
    }
    private static void EvidenceLifetime()
    {
        var admission = new ScenicPictureAdmission();
        Check(!admission.TryQueue(Picture(), 2, 3, 0, 0) && !admission.TryQueue(Picture(), 3, 3, 0, .501),
            "Stale or foreign proposal entered admission");
        admission.TryQueue(Picture(), 3, 3, 1, 1);
        Check(admission.Advance(4, 1.1, 1, _ => throw new Exception("Foreign scene evaluated"), out _) == ScenicPictureAdmissionResult.Cancelled,
            "A world/scene change retained pending admission");
        admission.TryQueue(Picture(), 4, 4, 2, 2);
        Check(admission.Advance(4, 2 + ScenicObservationPolicy.SurveyLifetime, 2,
            _ => throw new Exception("Expired proposal evaluated"), out _) == ScenicPictureAdmissionResult.Expired,
            "Unknown evidence can remain pending indefinitely");
        admission.TryQueue(Picture(), 4, 4, 8, 8); admission.Reset();
        Check(admission.Pending == null && admission.Advance(4, 8.1, 3,
            _ => throw new Exception("Prior region evaluated"), out _) == ScenicPictureAdmissionResult.None,
            "Region lifecycle retained a pending viewpoint");
    }
    private static void GlobalRecovery()
    {
        var admission = new ScenicPictureAdmission(); var recovery = new ScenicRecoveryPolicy();
        var availability = new ScenicPictureAvailability(); long id = 1; int accepted = 0, rejected = 0;
        for (int frame = 0; frame <= 360; frame++)
        {
            double now = frame / 30d;
            // The production recovery update consumes actual output before
            // admission work. None of these validated proposals rendered.
            recovery.Observe(now, true, false);
            if (admission.Pending == null) admission.TryQueue(Picture(id++), 3, 3, now, now);
            ScenicPictureAdmissionResult result = admission.Advance(3, now, frame,
                _ => frame % 37 == 0 ? ScenicPictureAdmissionResult.Accepted :
                    frame % 17 == 0 ? ScenicPictureAdmissionResult.Rejected : ScenicPictureAdmissionResult.Pending,
                out ScenicShotPlan? picture);
            if (picture != null)
            { accepted++; availability.TryOffer(picture, 3, 3, now, now, false); }
            if (result == ScenicPictureAdmissionResult.Rejected) rejected++;
            if (now >= ScenicRecoveryPolicy.MaximumSearchSeconds)
                Check(recovery.Expired, "Candidate churn or successful preparation renewed the blackout deadline");
        }
        Check(accepted > 0 && rejected > 0 && recovery.ElapsedSeconds == 12,
            "Admission outcomes did not remain separate from actual live recovery evidence");
        recovery.Observe(12.1, true, true);
        Check(!recovery.Active && !recovery.Expired, "Actual readable output could not recover the broadcast");
    }
    private static void Contradiction()
    {
        var admission = new ScenicPictureAdmission(); admission.TryQueue(Picture(), 3, 3, 0, 0);
        Check(admission.Advance(3, .1, 1, _ => ScenicPictureAdmissionResult.Rejected, out var accepted) == ScenicPictureAdmissionResult.Rejected &&
            accepted == null && admission.Pending == null, "Rejected current geometry became a picture or remained pending");
    }
}
