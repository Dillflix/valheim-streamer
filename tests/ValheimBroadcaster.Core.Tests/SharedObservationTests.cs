using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class SharedObservationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Camera background work reserves observation and live validation", ReservedWork);
        yield return ("An indivisible camera query overrun defers subsequent background work", Overrun);
        yield return ("Repeated frame synchronization cannot renew measured camera work", SameFrame);
        yield return ("Cheap candidate work is still bounded by admitted operations", CheapOperations);
        yield return ("Place survey recenter and newly exposed geometry do not invalidate old geometry", Recenter);
        yield return ("Measured construction movement and removal invalidate camera space", GeometryChanges);
        yield return ("Partial place observations do not turn rediscovery into new construction", Partial);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void ReservedWork()
    {
        var b = new CameraWorkSchedule(); b.BeginFrame(1);
        Check(b.TryStart(CameraWorkKind.Candidate), "Initial standby work rejected");
        b.Charge(CameraWorkKind.Candidate, 2);
        Check(b.TryStart(CameraWorkKind.Scenic), "Other lane could not use shared remainder");
        b.Charge(CameraWorkKind.Scenic, .25);
        Check(!b.TryStart(CameraWorkKind.Scenic), "Standby consumed reserved scene-observation time");
        Check(b.TryStart(CameraWorkKind.Observation), "Scene discovery starved behind standby work");
        b.Charge(CameraWorkKind.Observation, .75);
        Check(!b.TryStart(CameraWorkKind.Observation) && b.TryStart(CameraWorkKind.Live),
            "Background limit either exceeded or blocked current-picture validation");
    }
    private static void Overrun()
    {
        var b = new CameraWorkSchedule(); b.BeginFrame(3);
        Check(b.TryStart(CameraWorkKind.Observation), "Root snapshot could not start");
        b.Charge(CameraWorkKind.Observation, 7.5);
        Check(b.LargestOperation == 7.5 && !b.CanContinue(CameraWorkKind.Observation) &&
            !b.TryStart(CameraWorkKind.Candidate) && !b.TryStart(CameraWorkKind.Scenic), "Native overrun hidden or not contained");
        Check(b.TryStart(CameraWorkKind.Live), "Overrun discarded live validation");
    }
    private static void SameFrame()
    {
        var b = new CameraWorkSchedule(); b.BeginFrame(1); b.Charge(CameraWorkKind.Observation, 1.5);
        b.BeginFrame(1); Check(!b.TryStart(CameraWorkKind.Observation), "Same-frame calls renewed work");
        b.BeginFrame(2); Check(b.TryStart(CameraWorkKind.Observation) && b.BackgroundSpent == 0,
            "Next-frame allowance missing or unused credit accumulated");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { b.Charge(CameraWorkKind.Live, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && b.LiveMilliseconds == 0, "Invalid timing poisoned shared accounting");
        }
    }
    private static void CheapOperations()
    {
        var b = new CameraWorkSchedule(); b.BeginFrame(1);
        for (int i = 0; i < 12; i++) Check(b.TryStart(CameraWorkKind.Candidate), "Expected bounded start allowance");
        Check(!b.TryStart(CameraWorkKind.Candidate) && b.TryStart(CameraWorkKind.Live), "Zero-time candidates loop without bound");
    }
    private static PlacePieceObservation Piece(string id, float x) => new PlacePieceObservation(id,
        new PlaceBounds(new AutonomyPosition(x - 1, 0, -1), new AutonomyPosition(x + 1, 3, 1)));
    private static void Recenter()
    {
        var changes = new PlaceGeometryChanges(); var a = Piece("a", 0); var b = Piece("b", 150);
        Check(!changes.Update(new[] { a }, new AutonomyPosition(0, 0, 0), 112, true), "Initial evidence became change");
        Check(!changes.Update(new[] { a }, new AutonomyPosition(10, 0, 0), 112, true), "Carrier movement invalidated scene geometry");
        Check(!changes.Update(new[] { a, b }, new AutonomyPosition(60, 0, 0), 112, true), "Newly exposed building became changed old geometry");
    }
    private static void GeometryChanges()
    {
        var c = new PlaceGeometryChanges(); var origin = new AutonomyPosition(0, 0, 0);
        c.Update(new[] { Piece("a", 0) }, origin, 112, true);
        Check(c.Update(new[] { Piece("a", 1) }, origin, 112, true), "Measured movement did not invalidate geometry");
        Check(c.Update(new[] { Piece("a", 1), Piece("b", 5) }, origin, 112, true), "New observed construction was ignored");
        Check(c.Update(new[] { Piece("a", 1) }, origin, 112, true), "Confirmed removal was ignored");
    }
    private static void Partial()
    {
        var c = new PlaceGeometryChanges(); var origin = new AutonomyPosition(0, 0, 0);
        var a = Piece("a", 0); var b = Piece("b", 10);
        c.Update(new[] { a, b }, origin, 112, true);
        Check(!c.Update(new[] { a }, origin, 112, false), "Partial survey falsely proved removal");
        Check(!c.Update(new[] { a, b }, origin, 112, true), "Rediscovery after a partial survey falsely proved construction");
        Check(c.Update(new[] { Piece("a", 1), b }, origin, 112, false), "Directly measured movement was hidden by partial coverage");
        c.Reset(); Check(!c.Update(new[] { a }, origin, 112, true), "World reset retained old geometry history");
    }
}
