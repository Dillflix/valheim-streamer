using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core
{
    /// <summary>A short permission to alter the observer's local environment picture.
    /// It never authorizes changing the shared world clock or gameplay simulation.</summary>
    public sealed class ObserverEnvironmentSnapshot
    {
        public long Sequence { get; }
        public long WorldId { get; }
        public long RequestId { get; }
        public bool Enabled { get; }
        public int OrdinaryConnections { get; }
        public bool Granted => Enabled && OrdinaryConnections == 0;

        public ObserverEnvironmentSnapshot(long sequence, long worldId, long requestId, bool enabled, int ordinaryConnections)
        {
            if (sequence < 1 || requestId < 0 || ordinaryConnections < 0 || ordinaryConnections > ObserverEnvironmentProtocol.MaxConnections)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            if (requestId == 0 && enabled && ordinaryConnections == 0)
                throw new ArgumentException("Unsolicited environment grants are not permitted.");
            Sequence = sequence; WorldId = worldId; RequestId = requestId;
            Enabled = enabled; OrdinaryConnections = ordinaryConnections;
        }
    }

    public static class ObserverEnvironmentProtocol
    {
        public const string Request = "ValheimBroadcaster.Observer.EnvironmentRequest1";
        public const string Reply = "ValheimBroadcaster.Observer.EnvironmentReply1";
        public const int Version = 1, MaxConnections = 1024, RequestBytes = 16, ReplyBytes = 35;
        public const double LeaseSeconds = 2.5, RequestInterval = 1, ProbeSeconds = 6;
        private const int Magic = 0x56424531;

        public static byte[] EncodeRequest(long requestId)
        {
            if (requestId < 1) throw new ArgumentOutOfRangeException(nameof(requestId));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { writer.Write(Magic); writer.Write(Version); writer.Write(requestId); return stream.ToArray(); }
        }

        public static long DecodeRequest(byte[] data)
        {
            if (data == null || data.Length != RequestBytes) throw new ArgumentException("Invalid environment request size.");
            using (var reader = new BinaryReader(new MemoryStream(data, false)))
            {
                Header(reader); long request = reader.ReadInt64();
                if (request < 1) throw new ArgumentException("Invalid environment request identity.");
                return request;
            }
        }

        public static byte[] Encode(ObserverEnvironmentSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.Sequence);
                writer.Write(snapshot.WorldId); writer.Write(snapshot.RequestId); writer.Write(snapshot.Enabled);
                writer.Write((ushort)snapshot.OrdinaryConnections); return stream.ToArray();
            }
        }

        public static ObserverEnvironmentSnapshot Decode(byte[] data)
        {
            if (data == null || data.Length != ReplyBytes) throw new ArgumentException("Invalid environment reply size.");
            using (var reader = new BinaryReader(new MemoryStream(data, false)))
            {
                Header(reader); long sequence = reader.ReadInt64(), world = reader.ReadInt64(), request = reader.ReadInt64();
                byte enabled = reader.ReadByte();
                if (enabled > 1) throw new ArgumentException("Invalid environment permission flag.");
                return new ObserverEnvironmentSnapshot(sequence, world, request, enabled != 0, reader.ReadUInt16());
            }
        }

        private static void Header(BinaryReader reader)
        { if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) throw new ArgumentException("Unsupported environment protocol."); }
    }

    /// <summary>Expires from request SEND time, so a delayed old reply cannot
    /// manufacture a fresh empty-server lease. Revocations may be pushed.</summary>
    public sealed class ObserverEnvironmentPermit
    {
        private readonly Dictionary<long, double> pending = new Dictionary<long, double>();
        private long requestId, sequence;
        private double expires = -1, lastTime = -1;
        public ObserverEnvironmentSnapshot? Snapshot { get; private set; }

        public long Request(double now)
        {
            Clock(now);
            var expired = new List<long>();
            foreach (var item in pending) if (now - item.Value >= ObserverEnvironmentProtocol.LeaseSeconds) expired.Add(item.Key);
            foreach (long key in expired) pending.Remove(key);
            if (pending.Count >= 4) throw new InvalidOperationException("Too many pending environment requests.");
            long id = checked(++requestId); pending[id] = now; return id;
        }

        public bool Accept(ObserverEnvironmentSnapshot snapshot, long worldId, double now)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Clock(now);
            if (snapshot.WorldId != worldId) throw new ArgumentException("Environment world mismatch.");
            if (snapshot.Sequence <= sequence) return false;
            if (!snapshot.Granted)
            {
                sequence = snapshot.Sequence; Snapshot = snapshot;
                expires = -1; pending.Remove(snapshot.RequestId); return true;
            }
            if (!pending.TryGetValue(snapshot.RequestId, out double sent)) return false;
            pending.Remove(snapshot.RequestId);
            if (now >= sent + ObserverEnvironmentProtocol.LeaseSeconds) return false;
            sequence = snapshot.Sequence; Snapshot = snapshot;
            expires = sent + ObserverEnvironmentProtocol.LeaseSeconds;
            return true;
        }

        public bool Granted(double now)
        { Clock(now); return Snapshot?.Granted == true && now < expires; }

        public void Reset()
        { pending.Clear(); requestId = sequence = 0; expires = lastTime = -1; Snapshot = null; }

        private void Clock(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (lastTime >= 0 && now < lastTime) { pending.Clear(); expires = -1; }
            lastTime = now;
        }
    }

    /// <summary>Visual environment phase only. Pauses and stalls do not store a
    /// later jump; no world-time, sleep, spawning or growth clock is modified.</summary>
    public sealed class ScenicEnvironmentClock
    {
        private double last = -1;
        public double Fraction { get; private set; }
        public void Reset(double fraction, double now)
        {
            Time(now);
            if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction < 0 || fraction > 1)
                throw new ArgumentOutOfRangeException(nameof(fraction));
            Fraction = fraction % 1; last = now;
        }
        public double Advance(double now, double cycleSeconds, bool running)
        {
            Time(now); Cycle(cycleSeconds);
            double dt = last >= 0 && now >= last && now - last <= 1 ? now - last : 0;
            last = now;
            if (running && cycleSeconds > 0) Fraction = (Fraction + dt / cycleSeconds) % 1;
            return Fraction;
        }
        public static void Cycle(double seconds)
        { if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds != 0 && (seconds < 30 || seconds > 86400)) throw new ArgumentOutOfRangeException(nameof(seconds)); }
        private static void Time(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
