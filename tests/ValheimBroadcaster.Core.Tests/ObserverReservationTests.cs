using System;
using ValheimBroadcaster.Core;

internal static class ObserverReservationTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Late transport identity can reserve before native admission", DelayedIdentity),
        ("Already-admitted ordinary peers cannot become observers", NoPromotion),
        ("Lost reservation state cannot promote an admitted peer", LostState),
        ("Observer restrictions survive revocation through cleanup", PinnedRestrictions),
        ("New connections do not inherit old admission or reservation", Reconnect),
        ("Disabled or non-Steam connections cannot reserve", AdmissionConditions)
    };
    private static void Check(bool ok) { if (!ok) throw new Exception("Observer reservation assertion failed"); }
    private static void DelayedIdentity()
    {
        var role = new ObserverReservation();
        Check(!role.Observe(false, true, true, false)); // socket accepted, identity absent
        Check(role.Observe(false, true, true, true)); // identity available before PeerInfo
        Check(!role.Observe(true, true, true, true)); // native admission completed
        Check(role.Reserved && role.AdmissionClosed);
    }
    private static void NoPromotion()
    {
        var role = new ObserverReservation(); role.Observe(false, true, true, false);
        role.Observe(true, true, true, false); // ordinary admission
        Check(!role.Observe(true, true, true, true)); // allowlist edit while playing
        Check(!role.Observe(false, true, true, true)); // readiness must not reopen admission
        Check(!role.Reserved && role.AdmissionClosed && role.Reason.Contains("reconnect"));
    }
    private static void LostState()
    {
        var role = new ObserverReservation();
        Check(!role.Observe(true, true, true, true));
        Check(!role.Reserved && role.AdmissionClosed);
    }
    private static void PinnedRestrictions()
    {
        var role = new ObserverReservation(); Check(role.Observe(false, true, true, true));
        role.Observe(true, false, true, false); Check(role.Reserved && role.AdmissionClosed);
        role.Observe(false, false, false, false); Check(role.Reserved);
        // Reservation is not authorization: server still requires current allowlist
        // and an authenticated, ready peer before confirming its separate lease.
    }
    private static void Reconnect()
    {
        var first = new ObserverReservation(); first.Observe(true, true, true, false);
        var next = new ObserverReservation(); Check(next.Observe(false, true, true, true));
        Check(!first.Reserved && next.Reserved && !next.AdmissionClosed);
    }
    private static void AdmissionConditions()
    {
        var role = new ObserverReservation();
        Check(!role.Observe(false, false, true, true));
        Check(!role.Observe(false, true, false, true));
        Check(!role.Reserved && !role.AdmissionClosed);
        Check(role.Observe(false, true, true, true));
    }
}
