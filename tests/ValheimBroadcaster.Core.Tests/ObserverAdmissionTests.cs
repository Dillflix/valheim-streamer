using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ObserverAdmissionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("No observer request admits an ordinary player without a lease", Ordinary);
        yield return ("Explicit intent pins read-only guards before admission", Requested);
        yield return ("An admitted ordinary player cannot request observer mode later", NoPromotion);
        yield return ("Old and future protocol requests cannot reserve a role", WrongProtocol);
        yield return ("Repeated admission and matching intent preserve the selected role", Idempotent);
        yield return ("Intent alone grants no authenticated lease or discovery permission", Authentication);
        yield return ("Timed-out and revoked observers remain read-only through disconnect", Failure);
        yield return ("Reconnecting the same identity may select ordinary play", Reconnect);
    }

    private static void Check(bool ok) { if (!ok) throw new Exception("Observer admission invariant failed"); }
    private static void Ordinary()
    {
        var admission = new ObserverAdmission();
        Check(!admission.ReadOnly && !admission.AdmissionStarted);
        admission.BeginAdmission();
        Check(admission.AdmissionStarted && !admission.ReadOnly);
        // Membership is deliberately absent from role selection. Without a
        // request, the adapter must not create a handshake deadline/lease.
    }
    private static void Requested()
    {
        var admission = new ObserverAdmission();
        Check(admission.Request(ObserverProtocol.Version));
        Check(admission.ReadOnly && !admission.AdmissionStarted);
        admission.BeginAdmission();
        Check(admission.ReadOnly && admission.AdmissionStarted);
    }
    private static void NoPromotion()
    {
        var admission = new ObserverAdmission();
        admission.BeginAdmission();
        Check(!admission.Request(ObserverProtocol.Version));
        admission.BeginAdmission();
        Check(!admission.ReadOnly && !admission.Request(ObserverProtocol.Version));
    }
    private static void WrongProtocol()
    {
        foreach (int version in new[] { int.MinValue, 0, 1, 2, ObserverProtocol.Version + 1 })
        {
            var admission = new ObserverAdmission();
            Check(!admission.Request(version) && !admission.ReadOnly);
        }
    }
    private static void Idempotent()
    {
        var admission = new ObserverAdmission();
        Check(admission.Request(ObserverProtocol.Version));
        admission.BeginAdmission(); admission.BeginAdmission();
        Check(admission.Request(ObserverProtocol.Version) && admission.ReadOnly);
        Check(!admission.Request(2) && admission.ReadOnly);
    }
    private static void Authentication()
    {
        var admission = new ObserverAdmission();
        Check(admission.Request(ObserverProtocol.Version)); admission.BeginAdmission();
        var lease = new ObserverLease(100);
        Check(!lease.Confirmed && lease.MustDisconnect(100, false));
        Check(admission.ReadOnly);
        Check(lease.Confirm(ObserverProtocol.Version, 101));
        Check(lease.Confirmed && !lease.MustDisconnect(101, true));
    }
    private static void Failure()
    {
        var admission = new ObserverAdmission();
        Check(admission.Request(ObserverProtocol.Version)); admission.BeginAdmission();
        var timeout = new ObserverLease(1);
        Check(timeout.MustDisconnect(16, true) && admission.ReadOnly);
        var revoked = new ObserverLease(1); Check(revoked.Confirm(ObserverProtocol.Version, 2));
        Check(revoked.MustDisconnect(3, false) && admission.ReadOnly);
    }
    private static void Reconnect()
    {
        var previous = new ObserverAdmission();
        Check(previous.Request(ObserverProtocol.Version)); previous.BeginAdmission();
        var next = new ObserverAdmission(); next.BeginAdmission();
        Check(previous.ReadOnly && !next.ReadOnly);
    }
}
