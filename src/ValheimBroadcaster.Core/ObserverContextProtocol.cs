using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core
{
    /// <summary>Optional server observations, not simulation authority or proof of a loaded scene.
    /// State age and peer-heartbeat age are separate; receipt of another packet cannot refresh old evidence.</summary>
    public sealed class ObserverPlayerContext
    {
        public string Id { get; }
        public string CharacterId { get; }
        public ObserverPoint Position { get; }
        public ObserverPoint? Velocity { get; }
        public float StateAgeSeconds { get; }
        public float MetadataAgeSeconds { get; }
        // Age since the server observed a lower replicated health value; not an attack timestamp or attribution.
        public float? HealthLossAgeSeconds { get; }
        public ObserverPlayerContext(string id, string characterId, ObserverPoint position, ObserverPoint? velocity,
            float stateAgeSeconds, float metadataAgeSeconds, float? healthLossAgeSeconds)
        {
            Id = ObserverDiscoveryProtocol.Text(id, 64, false);
            CharacterId = ObserverDiscoveryProtocol.Text(characterId, 64, false);
            Age(stateAgeSeconds); Age(metadataAgeSeconds);
            if (healthLossAgeSeconds.HasValue) Age(healthLossAgeSeconds.Value);
            if (velocity.HasValue && (Math.Abs(velocity.Value.X) > 30 || Math.Abs(velocity.Value.Y) > 30 || Math.Abs(velocity.Value.Z) > 30))
                throw new ArgumentException("Unbounded observer velocity.");
            Position = position; Velocity = velocity; StateAgeSeconds = stateAgeSeconds;
            MetadataAgeSeconds = metadataAgeSeconds; HealthLossAgeSeconds = healthLossAgeSeconds;
        }
        public bool Fresh(double receiptAge) => receiptAge >= 0 && receiptAge < ObserverContextProtocol.FreshSeconds &&
            StateAgeSeconds + receiptAge < ObserverContextProtocol.FreshSeconds && MetadataAgeSeconds + receiptAge < ObserverContextProtocol.FreshSeconds;
        public bool RecentHealthLoss(double receiptAge) => Fresh(receiptAge) && HealthLossAgeSeconds.HasValue &&
            HealthLossAgeSeconds.Value + receiptAge < ObserverContextProtocol.EventSeconds;
        private static void Age(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > ObserverContextProtocol.MaximumAgeSeconds) throw new ArgumentOutOfRangeException(nameof(value)); }
    }

    public sealed class ObserverContextSnapshot
    {
        public long Sequence { get; }
        public long WorldId { get; }
        public bool Enabled { get; }
        public IReadOnlyList<ObserverPlayerContext> Players { get; }
        public ObserverContextSnapshot(long sequence, long worldId, bool enabled, IReadOnlyList<ObserverPlayerContext> players)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (players == null || players.Count > ObserverContextProtocol.MaxPlayers || !enabled && players.Count != 0)
                throw new ArgumentException("Invalid observer context inventory.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var copy = new ObserverPlayerContext[players.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                ObserverPlayerContext player = players[i] ?? throw new ArgumentException("Null observer context.");
                if (!ids.Add(player.Id)) throw new ArgumentException("Duplicate observer context identity.");
                copy[i] = player;
            }
            Sequence = sequence; WorldId = worldId; Enabled = enabled;
            Players = new ReadOnlyCollection<ObserverPlayerContext>(copy);
        }
    }

    // Separate optional feature protocol. Observer admission remains protocol 3;
    // a missing/disabled context service must never prevent viewing a player.
    public static class ObserverContextProtocol
    {
        public const string Request = "ValheimBroadcaster.Observer.ContextRequest1";
        public const string Reply = "ValheimBroadcaster.Observer.ContextReply1";
        public const int Version = 1, MaxPlayers = 64, MaxPacketBytes = 65536;
        public const float MaximumAgeSeconds = 3600;
        public const double FreshSeconds = 10, EventSeconds = 6, ProbeSeconds = 6;
        private const int Magic = 0x56424331;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(ObserverContextSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.Sequence); writer.Write(snapshot.WorldId);
                writer.Write(snapshot.Enabled); writer.Write((ushort)snapshot.Players.Count);
                foreach (ObserverPlayerContext player in snapshot.Players)
                {
                    Text(writer, player.Id); Text(writer, player.CharacterId); Point(writer, player.Position);
                    writer.Write(player.Velocity.HasValue); if (player.Velocity.HasValue) Point(writer, player.Velocity.Value);
                    writer.Write(player.StateAgeSeconds); writer.Write(player.MetadataAgeSeconds);
                    writer.Write(player.HealthLossAgeSeconds.HasValue);
                    if (player.HealthLossAgeSeconds.HasValue) writer.Write(player.HealthLossAgeSeconds.Value);
                }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new ArgumentException("Observer context packet too large.");
                return stream.ToArray();
            }
        }
        public static ObserverContextSnapshot Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 27 || bytes.Length > MaxPacketBytes) throw new ArgumentException("Invalid observer context size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) throw new ArgumentException("Unsupported observer context protocol.");
                long sequence = reader.ReadInt64(), world = reader.ReadInt64();
                bool enabled = Flag(reader); int count = reader.ReadUInt16();
                if (count > MaxPlayers) throw new ArgumentException("Observer context inventory too large.");
                var players = new ObserverPlayerContext[count];
                for (int i = 0; i < count; i++)
                {
                    string id = Text(reader), character = Text(reader); ObserverPoint position = Point(reader);
                    ObserverPoint? velocity = Flag(reader) ? Point(reader) : (ObserverPoint?)null;
                    float stateAge = reader.ReadSingle(), heartbeatAge = reader.ReadSingle();
                    float? lossAge = Flag(reader) ? reader.ReadSingle() : (float?)null;
                    players[i] = new ObserverPlayerContext(id, character, position, velocity, stateAge, heartbeatAge, lossAge);
                }
                if (stream.Position != stream.Length) throw new ArgumentException("Trailing observer context data.");
                return new ObserverContextSnapshot(sequence, world, enabled, players);
            }
        }
        private static void Text(BinaryWriter writer, string value)
        { byte[] bytes = Utf8.GetBytes(ObserverDiscoveryProtocol.Text(value, 64, false)); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string Text(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > 256 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new ArgumentException("Invalid observer context string.");
            return ObserverDiscoveryProtocol.Text(Utf8.GetString(reader.ReadBytes(count)), 64, false);
        }
        private static void Point(BinaryWriter writer, ObserverPoint point) { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
        private static ObserverPoint Point(BinaryReader reader) => new ObserverPoint(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        private static bool Flag(BinaryReader reader)
        { byte value = reader.ReadByte(); if (value > 1) throw new ArgumentException("Invalid observer context flag."); return value == 1; }
    }

    public sealed class ObserverContextCache
    {
        private double received = double.NegativeInfinity;
        public ObserverContextSnapshot? Snapshot { get; private set; }
        public double Age(double now) => Snapshot == null || now < received ? double.PositiveInfinity : now - received;
        public bool Accept(ObserverContextSnapshot snapshot, double now)
        {
            if (snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < received) throw new ArgumentOutOfRangeException(nameof(now));
            if (Snapshot != null && Snapshot.WorldId != snapshot.WorldId) throw new ArgumentException("Context world changed inside one connection.");
            if (Snapshot != null && snapshot.Sequence <= Snapshot.Sequence) return false;
            Snapshot = snapshot; received = now; return true;
        }
        public ObserverPlayerContext? Find(string id, string characterId, double now)
        {
            if (Snapshot?.Enabled != true) return null;
            double age = Age(now);
            foreach (ObserverPlayerContext player in Snapshot.Players)
                if (player.Id == id && player.CharacterId == characterId && player.Fresh(age)) return player;
            return null;
        }
        public void Reset() { Snapshot = null; received = double.NegativeInfinity; }
    }
}
