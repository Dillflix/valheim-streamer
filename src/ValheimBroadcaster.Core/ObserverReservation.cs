namespace ValheimBroadcaster.Core
{
    /// <summary>One connection's role can be reserved only before native admission.
    /// Reservation pins restrictions; it does not grant authenticated observer access.</summary>
    public sealed class ObserverReservation
    {
        public bool Reserved { get; private set; }
        public bool AdmissionClosed { get; private set; }
        public string Reason { get; private set; } = "Connection not assessed";

        public bool Observe(bool peerReady, bool enabled, bool steamTransport, bool allowlisted)
        {
            if (peerReady) AdmissionClosed = true;
            if (Reserved) { Reason = "Observer restrictions pinned"; return false; }
            if (AdmissionClosed) Reason = "Native admission completed without an observer reservation; reconnect required";
            else if (!enabled) Reason = "Observer access disabled";
            else if (!steamTransport) Reason = "Transport is not Steam";
            else if (!allowlisted) Reason = "Transport Steam identity unavailable or not allowlisted";
            else { Reserved = true; Reason = "Observer restrictions reserved before admission"; return true; }
            return false;
        }
    }
}
