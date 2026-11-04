using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicArrivalPreparationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Destination work survives only its matching world token and spatial epoch", Identity);
        yield return ("Off-air arrival work does not override native teleport completion", NativeTravel);
        yield return ("Expired arrival work and clock rollback require fresh observation", Expiry);
        yield return ("Cancelled destination preparation cannot survive world or session cleanup", Cancellation);
    }
    private static readonly AutonomyPosition Point = new AutonomyPosition(512, 20, 256);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicArrivalPreparation Prepared()
    { var p = new ScenicArrivalPreparation(); p.Begin(-20, 7, Point, 3, 2); return p; }
    private static void Identity()
    {
        var p = Prepared();
        Check(p.CanAdopt(-20, 7, Point, 3, new AutonomyPosition(514, 20, 256), 8), "Matching loaded arrival lost staged work");
        Check(!p.CanAdopt(-21, 7, Point, 3, Point, 8), "Work crossed worlds");
        Check(!p.CanAdopt(-20, 8, Point, 3, Point, 8), "Work crossed travel requests");
        Check(!p.CanAdopt(-20, 7, Point, 4, Point, 8), "Work crossed a geometry revision");
        Check(!p.CanAdopt(-20, 7, new AutonomyPosition(513, 20, 256), 3, Point, 8), "Mutated destination retained work");
        Check(!p.CanAdopt(-20, 7, Point, 3, new AutonomyPosition(540, 20, 256), 8), "Remote carrier accepted arrival work");
    }
    private static void NativeTravel()
    {
        var travel = new ObserverTravel(); travel.Start(); travel.Travel();
        var p = Prepared();
        // The real native distant teleport can already have loaded terrain
        // around its moved carrier while its existing eight-second wait runs.
        travel.Tick(3, teleporting: true, ready: true, atDestination: true);
        Check(p.CanPrepare(-20, 7, Point, 3, 5), "Loaded travel interval could not prepare coverage");
        Check(travel.Phase == ObserverPhase.Travelling, "Prepared work completed native travel early");
        travel.Tick(3.1, teleporting: false, ready: true, atDestination: true);
        Check(travel.Phase == ObserverPhase.Watching && p.CanAdopt(-20, 7, Point, 3, Point, 8.1),
            "Matching completed arrival could not retain preparation");
    }
    private static void Expiry()
    {
        var p = Prepared();
        Check(!p.CanPrepare(-20, 7, Point, 3, 1), "Clock rollback revived work");
        Check(!p.CanAdopt(-20, 7, Point, 3, Point, 22.1), "Expired work bypassed fresh observation");
        Check(p.Active, "Expiry silently cleared identity and invited repeated same-request preparation");
    }
    private static void Cancellation()
    {
        var p = Prepared(); p.Reset();
        Check(!p.Active && !p.CanAdopt(-20, 7, Point, 3, Point, 8), "Cancelled work survived cleanup");
        p.Begin(31, 8, Point, 4, 9);
        Check(p.CanAdopt(31, 8, Point, 4, Point, 10), "New session could not prepare independently");
    }
}
