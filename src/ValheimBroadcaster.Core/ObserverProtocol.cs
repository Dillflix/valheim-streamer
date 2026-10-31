using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimBroadcaster.Core
{
    public static class ObserverProtocol
    {
        public const int Version = 1;
        public const string Hello = "ValheimBroadcaster.Observer.Hello1";
        public const string Reply = "ValheimBroadcaster.Observer.Reply1";
        public const double HandshakeSeconds = 15;
        public static HashSet<string> SteamAllowlist(string value)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in (value ?? "").Split(','))
            {
                string id = item.Trim();
                if (id.Length != 17 || !ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) || parsed == 0)
                { if (id.Length != 0) throw new ArgumentException("Expected comma-separated 17-digit Steam IDs."); continue; }
                result.Add(id);
                if (result.Count > 16) throw new ArgumentException("At most 16 observer identities may be configured.");
            }
            return result;
        }
    }

    /// <summary>A read-only role stays pinned until disconnect, including after timeout/revocation.</summary>
    public sealed class ObserverLease
    {
        private readonly double began;
        public bool Confirmed { get; private set; }
        public ObserverLease(double now) { CheckTime(now); began = now; }
        public bool Confirm(int version, double now)
        {
            CheckTime(now);
            if (version != ObserverProtocol.Version || now < began || (!Confirmed && now - began >= ObserverProtocol.HandshakeSeconds)) return false;
            Confirmed = true;
            return true;
        }
        public bool MustDisconnect(double now, bool stillAuthorized)
        {
            CheckTime(now);
            return !stillAuthorized || now < began || (!Confirmed && now - began >= ObserverProtocol.HandshakeSeconds);
        }
        private static void CheckTime(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
    }
}
