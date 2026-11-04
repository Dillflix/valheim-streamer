using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core
{
    /// <summary>Timestamped replicated evidence, not proof of combat, intent or an exact interaction target.</summary>
    public sealed class ObserverPlayerEvents
    {
        public string Id { get; }
        public string CharacterId { get; }
        public bool Known { get; }
        public float SampleAgeSeconds { get; }
        public float HeartbeatAgeSeconds { get; }
        public float? MovementAgeSeconds { get; }
        public float? ActionAgeSeconds { get; }
        public float? HealthLossAgeSeconds { get; }
        public float? WorkAgeSeconds { get; }

        public ObserverPlayerEvents(string id, string characterId, bool known, float sampleAgeSeconds,
            float heartbeatAgeSeconds, float? movementAgeSeconds = null, float? actionAgeSeconds = null,
            float? healthLossAgeSeconds = null, float? workAgeSeconds = null)
        {
            Id = ObserverDiscoveryProtocol.Text(id, 64, false);
            CharacterId = ObserverDiscoveryProtocol.Text(characterId, 64, !known);
            Bound(sampleAgeSeconds); Bound(heartbeatAgeSeconds);
            if (movementAgeSeconds.HasValue) Bound(movementAgeSeconds.Value);
            if (actionAgeSeconds.HasValue) Bound(actionAgeSeconds.Value);
            if (healthLossAgeSeconds.HasValue) Bound(healthLossAgeSeconds.Value);
            if (workAgeSeconds.HasValue) Bound(workAgeSeconds.Value);
            if (!known && (movementAgeSeconds.HasValue || actionAgeSeconds.HasValue ||
                healthLossAgeSeconds.HasValue || workAgeSeconds.HasValue))
                throw new ArgumentException("Unknown event evidence cannot claim observed events.");
            Known = known; SampleAgeSeconds = sampleAgeSeconds; HeartbeatAgeSeconds = heartbeatAgeSeconds;
            MovementAgeSeconds = movementAgeSeconds; ActionAgeSeconds = actionAgeSeconds;
            HealthLossAgeSeconds = healthLossAgeSeconds; WorkAgeSeconds = workAgeSeconds;
        }
        public bool Fresh(double receiptAge) => Known && receiptAge >= 0 && receiptAge < ObserverEventProtocol.FreshSeconds &&
            SampleAgeSeconds + receiptAge < ObserverEventProtocol.FreshSeconds &&
            HeartbeatAgeSeconds + receiptAge < ObserverEventProtocol.FreshSeconds;
        public ObserverPlayerEvents Elapsed(float seconds)
        {
            Bound(seconds);
            return new ObserverPlayerEvents(Id, CharacterId, Known, Age(SampleAgeSeconds, seconds), Age(HeartbeatAgeSeconds, seconds),
                Age(MovementAgeSeconds, seconds), Age(ActionAgeSeconds, seconds), Age(HealthLossAgeSeconds, seconds), Age(WorkAgeSeconds, seconds));
        }
        private static float Age(float value, float seconds) => Math.Min(ObserverEventProtocol.MaximumAgeSeconds, value + seconds);
        private static float? Age(float? value, float seconds) => value.HasValue ? Age(value.Value, seconds) : (float?)null;
        private static void Bound(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > ObserverEventProtocol.MaximumAgeSeconds)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public sealed class ObserverEventSnapshot
    {
        public long Sequence { get; }
        public long WorldId { get; }
        public bool Enabled { get; }
        public IReadOnlyList<ObserverPlayerEvents> Players { get; }
        public ObserverEventSnapshot(long sequence, long worldId, bool enabled, IReadOnlyList<ObserverPlayerEvents> players)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (players == null || players.Count > ObserverEventProtocol.MaxPlayers || !enabled && players.Count != 0)
                throw new ArgumentException("Invalid observer event cohort.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var characters = new HashSet<string>(StringComparer.Ordinal);
            var copy = new ObserverPlayerEvents[players.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                ObserverPlayerEvents player = players[i] ?? throw new ArgumentException("Null observer events.");
                if (!ids.Add(player.Id) || player.CharacterId.Length != 0 && !characters.Add(player.CharacterId))
                    throw new ArgumentException("Duplicate observer event identity.");
                copy[i] = player;
            }
            Sequence = sequence; WorldId = worldId; Enabled = enabled;
            Players = new ReadOnlyCollection<ObserverPlayerEvents>(copy);
        }
    }

    /// <summary>Optional independent protocol: no change to admission, discovery, context or idle activity.</summary>
    public static class ObserverEventProtocol
    {
        public const string Request = "ValheimBroadcaster.Observer.EventRequest1";
        public const string Reply = "ValheimBroadcaster.Observer.EventReply1";
        public const int Version = 1, MaxPlayers = 64, MaxPacketBytes = 65536;
        public const float MaximumAgeSeconds = 3600, EventHistorySeconds = 30;
        public const double FreshSeconds = 6, ProbeSeconds = 6;
        private const int Magic = 0x56424531;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(ObserverEventSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.Sequence); writer.Write(snapshot.WorldId);
                writer.Write(snapshot.Enabled); writer.Write((ushort)snapshot.Players.Count);
                foreach (ObserverPlayerEvents player in snapshot.Players)
                {
                    Text(writer, player.Id); Text(writer, player.CharacterId); writer.Write(player.Known);
                    writer.Write(player.SampleAgeSeconds); writer.Write(player.HeartbeatAgeSeconds);
                    Age(writer, player.MovementAgeSeconds); Age(writer, player.ActionAgeSeconds);
                    Age(writer, player.HealthLossAgeSeconds); Age(writer, player.WorkAgeSeconds);
                }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new ArgumentException("Observer event packet too large.");
                return stream.ToArray();
            }
        }
        public static ObserverEventSnapshot Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 27 || bytes.Length > MaxPacketBytes) throw new ArgumentException("Invalid observer event size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) throw new ArgumentException("Unsupported observer event protocol.");
                long sequence = reader.ReadInt64(), world = reader.ReadInt64();
                bool enabled = Flag(reader); int count = reader.ReadUInt16();
                if (count > MaxPlayers) throw new ArgumentException("Observer event cohort too large.");
                var players = new ObserverPlayerEvents[count];
                for (int i = 0; i < count; i++)
                    players[i] = new ObserverPlayerEvents(Text(reader), Text(reader), Flag(reader), reader.ReadSingle(), reader.ReadSingle(),
                        Age(reader), Age(reader), Age(reader), Age(reader));
                if (stream.Position != stream.Length) throw new ArgumentException("Trailing observer event data.");
                return new ObserverEventSnapshot(sequence, world, enabled, players);
            }
        }
        private static void Age(BinaryWriter writer, float? value) { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
        private static float? Age(BinaryReader reader) => Flag(reader) ? reader.ReadSingle() : (float?)null;
        private static void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(ObserverDiscoveryProtocol.Text(value, 64, true));
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > 256 || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new ArgumentException("Invalid observer event string.");
            return ObserverDiscoveryProtocol.Text(Utf8.GetString(reader.ReadBytes(count)), 64, true);
        }
        private static bool Flag(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new ArgumentException("Invalid observer event flag.");
            return value == 1;
        }
    }

    public sealed class ObserverEventCache
    {
        private double received = double.NegativeInfinity;
        public ObserverEventSnapshot? Snapshot { get; private set; }
        public double Age(double now) => Snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < received
            ? double.PositiveInfinity : now - received;
        public bool Fresh(double now) => Snapshot?.Enabled == true && Age(now) < ObserverEventProtocol.FreshSeconds;
        public bool Accept(ObserverEventSnapshot snapshot, double now)
        {
            if (snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < received)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (Snapshot != null && Snapshot.WorldId != snapshot.WorldId)
                throw new ArgumentException("Event world changed inside one connection.");
            if (Snapshot != null && snapshot.Sequence <= Snapshot.Sequence) return false;
            Snapshot = snapshot; received = now; return true;
        }
        public ObserverPlayerEvents? Find(string id, string characterId, double now)
        {
            if (!Fresh(now)) return null;
            foreach (ObserverPlayerEvents player in Snapshot!.Players)
                if (player.Id == id && player.CharacterId == characterId && player.Fresh(Age(now))) return player;
            return null;
        }
        public void Reset() { Snapshot = null; received = double.NegativeInfinity; }
    }
}
