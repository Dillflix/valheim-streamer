using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyTravelControllerTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A Journey request owns ordinary relocation from request through departure without a picture", RequestOwnership);
        yield return ("Prepared physical candidates cannot start Journey movement without explicit admission", ExplicitAdmission);
        yield return ("Ordinary picture cuts and obscuration cannot cancel or reset Journey travel", EditorialIndependence);
        yield return ("Physical blockage replans the same Journey and revokes movement until a new certified route is admitted", PhysicalReplanning);
        yield return ("Local observation epochs revoke old routes without resetting world-scoped Journey ownership", SceneRebinding);
        yield return ("Departure deadline survives ordinary cuts, repeated attempts and local scene changes", DepartureDeadline);
        yield return ("Journey runtime begins with first accepted travel and includes every later replan", RuntimeDeadline);
        yield return ("Operator holds pause active clocks without allowing motion or resetting deadlines", HoldClocks);
        yield return ("Temporary authority and loading loss suspend navigation while explicit revocation remains terminal", AvailabilityAndAuthority);
        yield return ("Player and operator preemption are explicit terminal inputs rather than inferred picture loss", ExplicitPreemption);
        yield return ("Journey arrival requires actual destination and presented arrival evidence", Arrival);
        yield return ("A certified stationary arrival pan can advance orientation without inventing travel distance", StationaryArrival);
        yield return ("A stopped Journey retains ownership briefly then requests a local alternative without revoking safe geometry", LocalProgressWatchdog);
        yield return ("New routes and ordinary cuts cannot renew the measured Journey no-progress deadline", NoProgressDeadline);
        yield return ("Acknowledging a local replan permits its replacement to render without renewing the stall deadline", AcknowledgedRepair);
        yield return ("Only captured forward travel renews the Journey watchdog while holds pause it", MeasuredProgressAndHolds);
        yield return ("Exhausted physical navigation and actual loading failure are distinct from aesthetic failure", TerminalNavigation);
        yield return ("Journey terminal identity cannot revive itself and malformed clocks do not mutate it", LifecycleValidation);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .000001, $"Expected {expected}, got {actual}");
    private static JourneyRouteEvidence Route(long id = 75, long epoch = 5, double metres = 28) => new JourneyRouteEvidence(id, epoch, metres);
    private static JourneyTravelController New(double departure = 90, double travel = 900)
    { var c = new JourneyTravelController(); c.Begin(109, 7, 5, 0, departure, travel); return c; }
    private static JourneyTravelController Started()
    {
        var c = New(); c.Observe(new JourneyTravelObservation(0, 7, 5));
        Check(c.TryAcceptRoute(Route(), 0, out _), "Fresh validated route could not start"); return c;
    }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Malformed input accepted"); }

    private static void RequestOwnership()
    {
        var c = New();
        Check(c.Active && c.SuppressesScenicRelocation && c.State == JourneyTravelState.Departure && !c.CanAdvance,
            "Request did not own its destination before a shot or route exists");
        c.Observe(new JourneyTravelObservation(1, 0, 0, availability: JourneyTravelAvailability.Pending, pictureAvailable: false));
        Check(c.Active && c.SuppressesScenicRelocation && !c.CanAdvance && c.Cause == JourneyTravelCause.LoadingPending,
            "Temporary unavailable scene cancelled the request or authorized travel");
        Near(c.DepartureElapsedSeconds, 1);
    }

    private static void ExplicitAdmission()
    {
        var c = New();
        Check(!c.TryAcceptRoute(Route(), 0, out _), "No current authority still admitted a route");
        c.Observe(new JourneyTravelObservation(1, 7, 5, route: Route()));
        Check(!c.CanAdvance && !c.HasStarted, "An off-air candidate activated travel through observation");
        Check(!c.TryAcceptRoute(Route(epoch: 4), 1, out _), "An old-scene route was admitted");
        Check(!c.TryAcceptRoute(Route(metres: 0), 1, out _), "Empty certified distance authorized travel");
        Check(c.TryAcceptRoute(Route(), 1, out _) && c.CanAdvance && c.HasStarted && c.State == JourneyTravelState.Travelling,
            "Explicit current route did not activate travel");
        Check(!c.TryAcceptRoute(Route(), 2, out _), "Duplicate admission reset an already admitted route");
        Near(c.FirstRouteAt, 1); Near(c.TravelElapsedSeconds, 1);
    }

    private static void EditorialIndependence()
    {
        var c = Started();
        foreach (JourneyTravelEditorial editorial in Enum.GetValues(typeof(JourneyTravelEditorial)))
        {
            c.Observe(new JourneyTravelObservation((int)editorial + 1, 7, 5, pictureAvailable: false,
                route: Route(metres: 20), editorial: editorial));
            Check(c.Active && c.SuppressesScenicRelocation && c.CanAdvance && c.State == JourneyTravelState.Travelling && c.MissionId == 109,
                "A picture change altered physical navigation or mission identity");
        }
        c.Observe(new JourneyTravelObservation(4, 7, 5, pictureAvailable: false));
        Check(c.Active && c.SuppressesScenicRelocation && !c.CanAdvance && c.State == JourneyTravelState.Replanning,
            "Missing current route proof either ended the mission or authorized unsupported movement");
        c.Observe(new JourneyTravelObservation(4.067, 7, 5, route: Route(metres: 19)));
        Check(c.CanAdvance && c.MissionId == 109, "Fresh proof after the recorded67ms handoff lost the Journey");
        Near(c.FirstRouteAt, 0); Near(c.TravelElapsedSeconds, 4.067);
    }

    private static void PhysicalReplanning()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(1, 7, 5, navigation: JourneyTravelNavigation.Blocked, route: Route()));
        Check(c.Active && !c.CanAdvance && c.AcceptedRouteId == 0 && c.Cause == JourneyTravelCause.PhysicalRouteBlocked,
            "A collision left the old movement certificate authoritative");
        c.Observe(new JourneyTravelObservation(2, 7, 5, route: Route()));
        Check(!c.CanAdvance, "Repeated stale route evidence revived a revoked route without new admission");
        Check(c.TryAcceptRoute(Route(81), 2, out _) && c.CanAdvance && c.MissionId == 109,
            "Fresh certified local alternative could not resume the same Journey");
        Near(c.FirstRouteAt, 0); Near(c.TravelElapsedSeconds, 2);
    }

    private static void SceneRebinding()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(4, 7, 6, route: Route()));
        Check(c.Active && c.SuppressesScenicRelocation && c.SceneEpoch == 6 && !c.CanAdvance && c.AcceptedRouteId == 0 &&
            c.Cause == JourneyTravelCause.SceneChanged, "A local scene refresh cancelled the mission or retained old proof");
        Check(!c.TryAcceptRoute(Route(81), 4, out _), "Old-scene successor crossed a new observation boundary");
        Check(c.TryAcceptRoute(Route(81, 6), 4, out _), "Fresh new-scene route could not resume");
        Near(c.TravelElapsedSeconds, 4); Near(c.FirstRouteAt, 0);
        c.Observe(new JourneyTravelObservation(5, 8, 6));
        Check(!c.Active && c.Cause == JourneyTravelCause.WorldChanged, "An actual world change retained the old mission");
    }

    private static void DepartureDeadline()
    {
        var c = New();
        for (int i = 1; i < 90; i++)
            c.Observe(new JourneyTravelObservation(i, 7, 5 + i / 20, pictureAvailable: i % 3 != 0,
                editorial: JourneyTravelEditorial.ChangingShot));
        Check(c.Active && !c.HasStarted, "Pending retries prematurely cancelled departure");
        c.Observe(new JourneyTravelObservation(90, 7, 9));
        Check(c.State == JourneyTravelState.Cancelled && c.Cause == JourneyTravelCause.DepartureExpired && !c.SuppressesScenicRelocation,
            "Cuts, retries or recentering renewed the original request deadline");
        Check(!c.TryAcceptRoute(Route(90, 9), 90, out _), "Late prepared route revived an expired request");
    }

    private static void RuntimeDeadline()
    {
        var c = New(departure: 90, travel: 20);
        c.Observe(new JourneyTravelObservation(80, 7, 5));
        Check(c.TryAcceptRoute(Route(), 80, out _), "Valid late departure was charged a travel clock before movement");
        Near(c.TravelElapsedSeconds, 0); Near(c.DepartureElapsedSeconds, 80);
        c.Observe(new JourneyTravelObservation(85, 7, 5, navigation: JourneyTravelNavigation.Blocked));
        c.Observe(new JourneyTravelObservation(90, 7, 5));
        Check(c.TryAcceptRoute(Route(80), 90, out _), "Local repair could not resume before the runtime limit");
        c.Observe(new JourneyTravelObservation(99, 7, 5, route: Route(80)));
        Check(c.Active, "Original travel lifetime ended early");
        c.Observe(new JourneyTravelObservation(100, 7, 5, route: Route(80)));
        Check(c.Cause == JourneyTravelCause.RuntimeExpired && !c.Active, "A new route renewed runtime spent replanning");
    }

    private static void HoldClocks()
    {
        var c = New(departure: 10, travel: 20);
        c.Observe(new JourneyTravelObservation(2, 7, 5, held: true));
        c.Observe(new JourneyTravelObservation(102, 7, 5, held: false));
        Near(c.DepartureElapsedSeconds, 2);
        Check(c.TryAcceptRoute(Route(), 102, out _), "Unholding did not retain the departure allowance");
        c.Observe(new JourneyTravelObservation(104, 7, 5, held: true, route: Route()));
        Check(!c.CanAdvance && c.SuppressesScenicRelocation, "Held route moved or released mission ownership");
        c.Observe(new JourneyTravelObservation(204, 7, 5, held: false, route: Route()));
        Near(c.TravelElapsedSeconds, 2);
        c.Observe(new JourneyTravelObservation(222, 7, 5, route: Route()));
        Check(!c.Active && c.Cause == JourneyTravelCause.RuntimeExpired, "Hold reset rather than paused runtime");
    }

    private static void AvailabilityAndAuthority()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(1, 7, 5, authorization: JourneyTravelAuthorization.Unknown, route: Route()));
        Check(c.Active && !c.CanAdvance && c.Cause == JourneyTravelCause.AuthorizationPending, "Temporary absent authority was treated as revocation");
        c.Observe(new JourneyTravelObservation(2, 7, 5, availability: JourneyTravelAvailability.Pending));
        Check(c.Active && !c.CanAdvance && c.Cause == JourneyTravelCause.LoadingPending, "Pending world loading terminated the mission");
        c.Observe(new JourneyTravelObservation(3, 7, 5, navigation: JourneyTravelNavigation.WaitingForObservation));
        Check(c.Active && !c.CanAdvance && c.Cause == JourneyTravelCause.ObservationPending, "Missing survey was treated as clear geometry");
        c.Observe(new JourneyTravelObservation(4, 7, 5, route: Route()));
        Check(c.CanAdvance, "Fresh physical observations failed to resume the retained route");
        c.Observe(new JourneyTravelObservation(5, 7, 5, authorization: JourneyTravelAuthorization.Revoked));
        Check(!c.Active && c.Cause == JourneyTravelCause.AuthorizationRevoked, "Actual revocation did not terminate navigation");
    }

    private static void ExplicitPreemption()
    {
        foreach (var pair in new[] { (JourneyTravelPreemption.Operator, JourneyTravelCause.OperatorPreempted),
            (JourneyTravelPreemption.Player, JourneyTravelCause.PlayerPreempted) })
        {
            var c = Started(); c.Observe(new JourneyTravelObservation(1, 7, 5, preemption: pair.Item1, route: Route()));
            Check(!c.Active && !c.CanAdvance && !c.SuppressesScenicRelocation && c.Cause == pair.Item2,
                "Explicit higher-priority owner could not cancel travel");
        }
        var pending = New(); pending.Cancel("Operator stopped the pending Journey");
        Check(!pending.Active && pending.Reason.Contains("pending"), "Explicit pending cancellation was lost");
    }

    private static void Arrival()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(1, 7, 5, arrivalPresented: true, route: Route()));
        Check(c.State == JourneyTravelState.Travelling, "A pretty ending away from the destination completed the mission");
        c.Observe(new JourneyTravelObservation(2, 7, 5, destinationReached: true, pictureAvailable: false));
        Check(c.State == JourneyTravelState.Arriving && c.SuppressesScenicRelocation && !c.CanAdvance,
            "Arrival planning either completed prematurely or triggered ordinary relocation");
        c.Observe(new JourneyTravelObservation(3, 7, 5, destinationReached: true, arrivalPresented: true));
        Check(c.State == JourneyTravelState.Completed && c.Cause == JourneyTravelCause.Arrived && !c.SuppressesScenicRelocation,
            "Actual destination and presented arrival did not complete the mission");
        var pending = New(); pending.Observe(new JourneyTravelObservation(1, 7, 5, destinationReached: true, arrivalPresented: true));
        Check(pending.Active && pending.State != JourneyTravelState.Completed, "An unstarted proposal claimed a finished journey");
    }

    private static void StationaryArrival()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(1, 7, 5, destinationReached: true, route: Route(metres: 0)));
        Check(!c.CanAdvance && c.State == JourneyTravelState.Arriving,
            "Exhausted translation certificate authorized further movement");
        var arrival = new JourneyRouteEvidence(81, 5, 0, stationary: true);
        Check(arrival.HasRoute && arrival.IsStationary && c.TryAcceptRoute(arrival, 1, out _),
            "A fully validated fixed-position arrival could not be admitted");
        Check(c.CanAdvance && c.State == JourneyTravelState.Arriving, "Stationary arrival could not develop its orientation");
        Near(c.CertifiedRemainingMetres, 0);
        c.Observe(new JourneyTravelObservation(2, 7, 5, destinationReached: true, route: arrival));
        Check(c.CanAdvance && c.SuppressesScenicRelocation, "Stationary proof was lost on the next authority observation");
        Near(c.CertifiedRemainingMetres, 0); Near(c.FirstRouteAt, 0); Near(c.TravelElapsedSeconds, 2);
        c.Observe(new JourneyTravelObservation(3, 7, 5, destinationReached: true, arrivalPresented: true, route: arrival));
        Check(c.State == JourneyTravelState.Completed && !c.CanAdvance, "Presented stationary arrival failed to complete");
    }

    private static void TerminalNavigation()
    {
        foreach (var pair in new[] { (JourneyTravelNavigation.LoadingFailed, JourneyTravelCause.LoadingFailed),
            (JourneyTravelNavigation.SearchExhausted, JourneyTravelCause.NavigationExhausted) })
        {
            var c = Started(); c.Observe(new JourneyTravelObservation(1, 7, 5, navigation: pair.Item1));
            Check(!c.Active && c.Cause == pair.Item2, "An explicit unrecoverable physical failure retained mission authority");
        }
        var waiting = Started();
        waiting.Observe(new JourneyTravelObservation(1, 7, 5, navigation: JourneyTravelNavigation.WaitingForLoading,
            pictureAvailable: false, editorial: JourneyTravelEditorial.Obscured));
        Check(waiting.Active && !waiting.CanAdvance, "A temporarily obscured/loading scene became terminal");
    }

    private static void LocalProgressWatchdog()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(6, 7, 5, route: Route(), renderedTravelMetres: 0));
        Check(c.Active && c.SuppressesScenicRelocation && c.CanAdvance && !c.LocalReplanRequested,
            "A short continuation wait cancelled or replanned usable travel too early");
        c.Observe(new JourneyTravelObservation(10, 7, 5, route: Route(), renderedTravelMetres: 0));
        Check(c.Active && c.LocalReplanRequested && c.CanAdvance && c.AcceptedRouteId == 75 &&
            c.Cause == JourneyTravelCause.NavigationStalled, "Stalled motion did not request an alternative while preserving safe proof");
        Near(c.NoProgressSeconds, 10);
        c.Observe(new JourneyTravelObservation(11, 7, 5, route: Route(metres: 27), renderedTravelMetres: 1));
        Check(c.Active && !c.LocalReplanRequested && c.Cause == JourneyTravelCause.None,
            "Real rendered movement did not end the local stall episode");
        Near(c.NoProgressSeconds, 0);
    }

    private static void NoProgressDeadline()
    {
        var c = Started();
        for (int at = 10; at < 90; at += 10)
        {
            c.Observe(new JourneyTravelObservation(at, 7, 5, pictureAvailable: false,
                editorial: JourneyTravelEditorial.ChangingShot, renderedTravelMetres: 0));
            Check(c.TryAcceptRoute(Route(75 + at), at, out _), "A certified alternative failed before the real deadline");
            Check(c.Active && c.LocalReplanRequested, "A new route or cut reset measured no-progress time");
        }
        c.Observe(new JourneyTravelObservation(90, 7, 5, route: Route(155), renderedTravelMetres: 0));
        Check(!c.Active && c.Cause == JourneyTravelCause.NoProgressExpired && !c.SuppressesScenicRelocation,
            "Repeated route IDs hid a stationary mission for the entire multi-kilometre travel budget");
        Check(!c.TryAcceptRoute(Route(200), 90, out _), "Late route revived the expired physical mission");
    }

    private static void AcknowledgedRepair()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(10, 7, 5, route: Route(), renderedTravelMetres: 0));
        Check(c.AcknowledgeLocalReplan() && !c.AcknowledgeLocalReplan(), "Replan request was not consumed exactly once per stall");
        Check(c.TryAcceptRoute(Route(81), 12, out _), "Certified repair could not start");
        c.Observe(new JourneyTravelObservation(13, 7, 5, route: Route(81), renderedTravelMetres: 0));
        Check(c.CanAdvance && !c.LocalReplanRequested, "Repair was immediately discarded before it could present motion");
        Near(c.NoProgressSeconds, 13);
        c.Observe(new JourneyTravelObservation(90, 7, 5, route: Route(81), renderedTravelMetres: 0));
        Check(!c.Active && c.Cause == JourneyTravelCause.NoProgressExpired, "Acknowledging repair renewed the actual stall budget");
        var resumed = Started();
        resumed.Observe(new JourneyTravelObservation(10, 7, 5, route: Route(), renderedTravelMetres: 0));
        resumed.AcknowledgeLocalReplan();
        resumed.Observe(new JourneyTravelObservation(11, 7, 5, route: Route(), renderedTravelMetres: 1));
        resumed.Observe(new JourneyTravelObservation(21, 7, 5, route: Route(), renderedTravelMetres: 1));
        Check(resumed.LocalReplanRequested, "A new measured stall after resumed travel could not request a new repair");
    }

    private static void MeasuredProgressAndHolds()
    {
        var c = Started();
        c.Observe(new JourneyTravelObservation(8, 7, 5, held: true, route: Route(), renderedTravelMetres: 0));
        c.Observe(new JourneyTravelObservation(108, 7, 5, route: Route(), renderedTravelMetres: 0));
        Near(c.NoProgressSeconds, 8);
        c.Observe(new JourneyTravelObservation(110, 7, 5, route: Route(), renderedTravelMetres: .005));
        Check(c.LocalReplanRequested, "Sub-centimetre rounding jitter renewed a stalled mission");
        c.Observe(new JourneyTravelObservation(111, 7, 5, route: Route(), renderedTravelMetres: .02));
        Near(c.NoProgressSeconds, 0);
        c.Observe(new JourneyTravelObservation(112, 7, 5, route: Route(), renderedTravelMetres: .01));
        Near(c.NoProgressSeconds, 1);
        // A fresh rendered measurement on the deadline observation wins over
        // stale accumulated time; the world actually moved before this sample.
        c.Observe(new JourneyTravelObservation(201, 7, 5, route: Route(), renderedTravelMetres: 1));
        Check(c.Active && !c.LocalReplanRequested, "A real new rendered position lost to stale watchdog time");
        Near(c.NoProgressSeconds, 0);
        Reject(() => new JourneyTravelObservation(202, 7, 5, renderedTravelMetres: double.NaN));
        Reject(() => new JourneyTravelObservation(202, 7, 5, renderedTravelMetres: -1));
    }

    private static void LifecycleValidation()
    {
        var c = Started(); bool repeated = false;
        try { c.Begin(110, 7, 5, 1, 90, 900); } catch (InvalidOperationException) { repeated = true; }
        Check(repeated && c.MissionId == 109, "Repeated start replaced active identity/deadlines");
        c.Observe(new JourneyTravelObservation(2, 7, 5, route: Route()));
        Reject(() => c.Observe(new JourneyTravelObservation(1, 7, 5)));
        Near(c.TravelElapsedSeconds, 2); Check(c.Active, "Bad clock partially mutated mission state");
        Reject(() => new JourneyRouteEvidence(1, 1, double.NaN));
        Reject(() => new JourneyTravelObservation(double.NaN, 7, 5));
        Reject(() => new JourneyTravelController().Begin(1, 0, 1, 0, 90, 900));
        c.Cancel("Explicit stop"); c.Observe(new JourneyTravelObservation(3, 7, 5, route: Route()));
        Check(!c.Active && !c.TryAcceptRoute(Route(90), 3, out _), "Terminal mission revived from observations");
        c.Reset(); Check(c.State == JourneyTravelState.None && c.MissionId == 0 && c.FirstRouteAt == -1 && !c.SuppressesScenicRelocation,
            "World teardown retained old mission state");
        c.Begin(110, 8, 6, 4, 90, 900); Check(c.Active && !c.HasStarted && c.MissionId == 110, "Fresh request could not own a new lifecycle");
    }
}
