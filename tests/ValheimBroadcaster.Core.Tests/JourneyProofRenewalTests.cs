using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyProofRenewalTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A changing Journey focus revokes admission immediately but can recover before its fixed deadline", Transient);
        yield return ("Repeated readability failures and missing observations cannot extend Journey proof renewal", Bounded);
        yield return ("Surface collision canopy containment and invalid geometry retire Journey proof immediately", Physical);
        yield return ("Unavailable Journey evidence cannot authorize a successor while awaiting actual validation", Unknown);
        yield return ("Invalid and reversed renewal clocks cannot revive a Journey proof", Clocks);
        yield return ("Reset and physical retirement cannot be undone by a late Journey validation result", Lifecycle);
        yield return ("A later sample cannot hide an earlier physical Journey corridor contradiction", MixedSamples);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }

    private static void Transient()
    {
        foreach (var kind in new[] { ScenicRejectionKind.Focus, ScenicRejectionKind.Foliage })
        {
            var proof = new JourneyProofRenewalPolicy(); proof.Begin(10);
            Check(proof.Available, "The admitted complete proof was lost");
            proof.Reject(kind, 11);
            Check(!proof.Available && proof.RevalidatingReadability && !proof.MustRetire(11),
                "A preference failure either retained admission or immediately destroyed its immutable plan");
            Check(proof.TryAccept(12.999) && proof.Available && !proof.RevalidatingReadability,
                "Complete timely unchanged-plan validation could not restore its proof");
            proof.Reject(kind, 15);
            Check(!proof.Available && !proof.MustRetire(16.99) && proof.MustRetire(17),
                "A later independent failure did not receive its own bounded interval");
        }
    }

    private static void Bounded()
    {
        var proof = new JourneyProofRenewalPolicy(); proof.Begin(8);
        proof.Reject(ScenicRejectionKind.Focus, 10);
        proof.Reject(ScenicRejectionKind.Foliage, 10.5);
        proof.Reject(ScenicRejectionKind.SurveyIncomplete, 11);
        proof.Reject(ScenicRejectionKind.Focus, 11.9);
        Check(!proof.MustRetire(11.999) && proof.MustRetire(12), "Retries postponed the first contradiction deadline");
        Check(!proof.TryAccept(12) && !proof.Available, "A delayed success after deadline restored permission");
        Check(!proof.TryAccept(13), "Repeated late completion bypassed finite retirement");
    }

    private static void Physical()
    {
        foreach (var kind in new[] { ScenicRejectionKind.SurfaceClearance, ScenicRejectionKind.CameraCollision,
            ScenicRejectionKind.CorridorCollision, ScenicRejectionKind.InsideVegetation,
            ScenicRejectionKind.InvalidPlan, ScenicRejectionKind.CameraFormat })
        {
            var proof = new JourneyProofRenewalPolicy(); proof.Begin(1);
            proof.Reject(kind, 2);
            Check(!proof.Available && proof.MustRetire(2) && !proof.RevalidatingReadability,
                kind + " was treated as transient editorial evidence");
            Check(!proof.TryAccept(2.01), "Late evaluator result revived physically contradicted geometry");
            proof.Reject(ScenicRejectionKind.Focus, 2.5);
            Check(proof.MustRetire(2.5), "A later framing failure hid an earlier physical contradiction");
        }
    }

    private static void Unknown()
    {
        foreach (var kind in new[] { ScenicRejectionKind.None, ScenicRejectionKind.Acquisition,
            ScenicRejectionKind.Geometry, ScenicRejectionKind.CarrierRange,
            ScenicRejectionKind.SurveyIncomplete, ScenicRejectionKind.SamplingBudget })
        {
            var proof = new JourneyProofRenewalPolicy(); proof.Begin(1);
            proof.Reject(kind, 2);
            Check(!proof.Available && !proof.MustRetire(3) && !proof.RevalidatingReadability,
                "Unknown evidence either authorized travel or invented a measured obstruction");
            Check(proof.TryAccept(3) && proof.Available, "Actual renewed observation could not restore unavailable evidence");
        }
    }

    private static void Clocks()
    {
        foreach (double clock in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity, 9 })
        {
            var proof = new JourneyProofRenewalPolicy(); proof.Begin(10);
            proof.Reject(ScenicRejectionKind.Focus, clock);
            Check(!proof.Available && proof.MustRetire(10) && !proof.TryAccept(11),
                "A nonfinite or backwards failure timestamp manufactured a retry window");
        }
        var delayed = new JourneyProofRenewalPolicy(); delayed.Begin(10);
        delayed.Reject(ScenicRejectionKind.Focus, 11);
        Check(!delayed.TryAccept(10.5) && !delayed.Available, "An earlier validation timestamp restored newer failed evidence");
    }

    private static void Lifecycle()
    {
        var proof = new JourneyProofRenewalPolicy();
        Check(!proof.Available && !proof.TryAccept(1), "An unowned proof accepted a stray completion");
        proof.Begin(1); proof.Reject(ScenicRejectionKind.Foliage, 2); proof.Reset();
        Check(!proof.Available && !proof.TryAccept(2.1), "A reset retained plan accepted a stale completion");
        proof.Begin(3);
        Check(proof.Available && !proof.MustRetire(3), "A newly admitted plan inherited the previous owner's failure");
        foreach (double clock in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { proof.Begin(clock); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid initial proof time was accepted");
        }
    }

    private static void MixedSamples()
    {
        foreach (var physical in new[] { ScenicRejectionKind.CameraCollision, ScenicRejectionKind.InsideVegetation,
            ScenicRejectionKind.SurfaceClearance, ScenicRejectionKind.CorridorCollision, ScenicRejectionKind.InvalidPlan })
        {
            var kinds = new[] { ScenicRejectionKind.SurveyIncomplete, ScenicRejectionKind.Focus,
                physical, ScenicRejectionKind.Foliage, ScenicRejectionKind.SamplingBudget, ScenicRejectionKind.None };
            for (int shift = 0; shift < kinds.Length; shift++)
            {
                var accumulated = ScenicRejectionKind.None;
                for (int i = 0; i < kinds.Length; i++)
                    accumulated = JourneyProofRenewalPolicy.Stronger(accumulated, kinds[(i + shift) % kinds.Length]);
                Check(accumulated == physical, "Batch ordering downgraded a measured physical contradiction");
                var proof = new JourneyProofRenewalPolicy(); proof.Begin(1); proof.Reject(accumulated, 2);
                Check(proof.MustRetire(2), "Mixed physical/editorial samples received an editorial retry window");
            }
        }
        Check(JourneyProofRenewalPolicy.Stronger(ScenicRejectionKind.Focus, ScenicRejectionKind.SurveyIncomplete) == ScenicRejectionKind.Focus,
            "An unknown later sample erased the earlier readability contradiction deadline");
    }
}
