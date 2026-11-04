using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core
{
    /// <summary>Server-observed inactivity, not proof of intent or simulation authority.
    /// Unknown entries remain in the cohort instead of silently counting as idle.</summary>
    public sealed class ObserverPlayerActivity
    {
        public string Id { get; }
        public string CharacterId { get; }
        public bool Known { get; }
        public float ObservedIdleSeconds { get; }
        public float SampleAgeSeconds { get; }
        public float HeartbeatAgeSeconds { get; }

        public ObserverPlayerActivity(string id, string characterId, bool known, float observedIdleSeconds,
            float sampleAgeSeconds, float heartbeatAgeSeconds)
        {
            Id = ObserverDiscoveryProtocol.Text(id, 64, false);
            CharacterId = ObserverDiscoveryProtocol.Text(characterId, 64, !known);
            Bound(observedIdleSeconds, ObserverActivityProtocol.MaximumIdleSeconds);
            Bound(sampleAgeSeconds, ObserverActivityProtocol.MaximumAgeSeconds);
            Bound(heartbeatAgeSeconds, ObserverActivityProtocol.MaximumAgeSeconds);
            if (!known && observedIdleSeconds != 0) throw new ArgumentException("Unknown activity cannot claim observed idle time.");
            Known = known; ObservedIdleSeconds = observedIdleSeconds;
            SampleAgeSeconds = sampleAgeSeconds; HeartbeatAgeSeconds = heartbeatAgeSeconds;
        }

        // A newly received packet does not refresh old source evidence, and time
        // spent in transit does not extend the server's observed idle interval.
        public bool Fresh(double receiptAge) => Known && receiptAge >= 0 && receiptAge < ObserverActivityProtocol.FreshSeconds &&
            SampleAgeSeconds + receiptAge < ObserverActivityProtocol.FreshSeconds &&
            HeartbeatAgeSeconds + receiptAge < ObserverActivityProtocol.FreshSeconds;

        private static void Bound(float value, float maximum)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public sealed class ObserverActivitySnapshot
    {
        public long Sequence { get; }
        public long WorldId { get; }
        public bool Enabled { get; }
        public bool Complete { get; }
        public IReadOnlyList<ObserverPlayerActivity> Players { get; }

        public ObserverActivitySnapshot(long sequence, long worldId, bool enabled, bool complete,
            IReadOnlyList<ObserverPlayerActivity> players)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (players == null || players.Count > ObserverActivityProtocol.MaxPlayers ||
                !enabled && (complete || players.Count != 0)) throw new ArgumentException("Invalid observer activity cohort.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var characters = new HashSet<string>(StringComparer.Ordinal);
            var copy = new ObserverPlayerActivity[players.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                ObserverPlayerActivity player = players[i] ?? throw new ArgumentException("Null observer activity.");
                if (!ids.Add(player.Id)) throw new ArgumentException("Duplicate observer activity identity.");
                if (player.CharacterId.Length != 0 && !characters.Add(player.CharacterId))
                    throw new ArgumentException("Duplicate observer activity character.");
                copy[i] = player;
            }
            Sequence = sequence; WorldId = worldId; Enabled = enabled; Complete = complete;
            Players = new ReadOnlyCollection<ObserverPlayerActivity>(copy);
        }
    }

    /// <summary>Optional independent feature protocol. Admission remains 3,
    /// discovery 2 and camera context 1; old companions can omit this service.</summary>
    public static class ObserverActivityProtocol
    {
        public const string Request = "ValheimBroadcaster.Observer.ActivityRequest1";
        public const string Reply = "ValheimBroadcaster.Observer.ActivityReply1";
        public const int Version = 1, MaxPlayers = 64, MaxPacketBytes = 65536;
        public const float MaximumIdleSeconds = 86400, MaximumAgeSeconds = 3600;
        public const double FreshSeconds = 6, ProbeSeconds = 6;
        private const int Magic = 0x56424131;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(ObserverActivitySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.Sequence); writer.Write(snapshot.WorldId);
                writer.Write(snapshot.Enabled); writer.Write(snapshot.Complete); writer.Write((ushort)snapshot.Players.Count);
                foreach (ObserverPlayerActivity player in snapshot.Players)
                {
                    Text(writer, player.Id); Text(writer, player.CharacterId); writer.Write(player.Known);
                    writer.Write(player.ObservedIdleSeconds); writer.Write(player.SampleAgeSeconds); writer.Write(player.HeartbeatAgeSeconds);
                }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new ArgumentException("Observer activity packet too large.");
                return stream.ToArray();
            }
        }

        public static ObserverActivitySnapshot Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 28 || bytes.Length > MaxPacketBytes) throw new ArgumentException("Invalid observer activity size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                    throw new ArgumentException("Unsupported observer activity protocol.");
                long sequence = reader.ReadInt64(), world = reader.ReadInt64();
                bool enabled = Flag(reader), complete = Flag(reader);
                int count = reader.ReadUInt16();
                if (count > MaxPlayers) throw new ArgumentException("Observer activity cohort too large.");
                var players = new ObserverPlayerActivity[count];
                for (int i = 0; i < count; i++)
                    players[i] = new ObserverPlayerActivity(Text(reader), Text(reader), Flag(reader),
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (stream.Position != stream.Length) throw new ArgumentException("Trailing observer activity data.");
                return new ObserverActivitySnapshot(sequence, world, enabled, complete, players);
            }
        }

        private static void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(ObserverDiscoveryProtocol.Text(value, 64, true));
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > 256 || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new ArgumentException("Invalid observer activity string.");
            return ObserverDiscoveryProtocol.Text(Utf8.GetString(reader.ReadBytes(count)), 64, true);
        }
        private static bool Flag(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new ArgumentException("Invalid observer activity flag.");
            return value == 1;
        }
    }

    public sealed class ObserverActivityCache
    {
        private double received = double.NegativeInfinity;
        public ObserverActivitySnapshot? Snapshot { get; private set; }
        public double Age(double now) => Snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < received
            ? double.PositiveInfinity : now - received;
        // This establishes only a fresh complete envelope. Consumers must still
        // match discovery identities and require each individual entry's Fresh.
        public bool Fresh(double now) => Snapshot?.Enabled == true && Snapshot.Complete && Age(now) < ObserverActivityProtocol.FreshSeconds;
        public bool Accept(ObserverActivitySnapshot snapshot, double now)
        {
            if (snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < received)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (Snapshot != null && Snapshot.WorldId != snapshot.WorldId)
                throw new ArgumentException("Activity world changed inside one connection.");
            if (Snapshot != null && snapshot.Sequence <= Snapshot.Sequence) return false;
            Snapshot = snapshot; received = now; return true;
        }
        public ObserverPlayerActivity? Find(string id, string characterId, double now)
        {
            if (!Fresh(now)) return null;
            double age = Age(now);
            foreach (ObserverPlayerActivity player in Snapshot!.Players)
                if (player.Id == id && player.CharacterId == characterId && player.Fresh(age)) return player;
            return null;
        }
        public void Reset() { Snapshot = null; received = double.NegativeInfinity; }
    }
}
