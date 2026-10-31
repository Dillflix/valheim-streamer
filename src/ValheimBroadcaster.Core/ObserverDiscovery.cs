using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core
{
    public readonly struct ObserverPoint
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public ObserverPoint(float x, float y, float z)
        {
            if (!Valid(x) || !Valid(y) || !Valid(z)) throw new ArgumentOutOfRangeException(nameof(x), "Observer coordinates must be finite and bounded.");
            X = x; Y = y; Z = z;
        }
        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 20000;
    }

    public sealed class ObserverPlayer
    {
        public string Id { get; }
        public string CharacterId { get; }
        public string Name { get; }
        public ObserverPoint? Position { get; }
        public bool IsSpawned { get; }
        public ObserverPlayer(string id, string characterId, string name, ObserverPoint? position, bool isSpawned)
        {
            Id = ObserverDiscoveryProtocol.Text(id, 64, false);
            CharacterId = ObserverDiscoveryProtocol.Text(characterId, 64, true);
            Name = ObserverDiscoveryProtocol.Text(name, 64, true);
            if (position.HasValue && (!isSpawned || characterId.Length == 0)) throw new ArgumentException("A destination requires a spawned character identity.");
            Position = position; IsSpawned = isSpawned;
        }
    }

    public sealed class ObserverRegion
    {
        public string Id { get; }
        public string Name { get; }
        public ObserverPoint Position { get; }
        public ObserverRegion(string id, string name, ObserverPoint position)
        { Id = ObserverDiscoveryProtocol.Text(id, 64, false); Name = ObserverDiscoveryProtocol.Text(name, 64, true); Position = position; }
    }

    public sealed class ObserverDiscoverySnapshot
    {
        public long Sequence { get; }
        public long WorldId { get; }
        public bool DiscoveryEnabled { get; }
        public bool SharePrivatePositions { get; }
        public IReadOnlyList<ObserverPlayer> Players { get; }
        public IReadOnlyList<ObserverRegion> Regions { get; }
        public ObserverDiscoverySnapshot(long sequence, long worldId, bool discoveryEnabled, bool sharePrivatePositions,
            IReadOnlyList<ObserverPlayer> players, IReadOnlyList<ObserverRegion> regions)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (players == null || regions == null || players.Count > ObserverDiscoveryProtocol.MaxPlayers || regions.Count > ObserverDiscoveryProtocol.MaxCachedRegions)
                throw new ArgumentException("Observer discovery inventory exceeds its bounds.");
            if (!discoveryEnabled && (players.Count != 0 || regions.Count != 0)) throw new ArgumentException("Disabled discovery cannot carry destinations.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var playerCopy = new ObserverPlayer[players.Count];
            for (int i = 0; i < players.Count; i++)
            {
                ObserverPlayer player = players[i] ?? throw new ArgumentException("Null player.");
                if (!ids.Add(player.Id)) throw new ArgumentException("Duplicate observer player ID.");
                playerCopy[i] = player;
            }
            ids.Clear();
            var regionCopy = new ObserverRegion[regions.Count];
            for (int i = 0; i < regions.Count; i++)
            {
                ObserverRegion region = regions[i] ?? throw new ArgumentException("Null region.");
                if (!ids.Add(region.Id)) throw new ArgumentException("Duplicate observer region ID.");
                regionCopy[i] = region;
            }
            Sequence = sequence; WorldId = worldId; DiscoveryEnabled = discoveryEnabled; SharePrivatePositions = sharePrivatePositions;
            Players = new ReadOnlyCollection<ObserverPlayer>(playerCopy);
            Regions = new ReadOnlyCollection<ObserverRegion>(regionCopy);
        }
    }

    // Small, explicit wire schema. Strings have a bounded unsigned-16-bit UTF-8
    // byte length; no untrusted native ReadString/ReadPackage allocation is used.
    public static class ObserverDiscoveryProtocol
    {
        public const string Request = "ValheimBroadcaster.Observer.DiscoveryRequest2";
        public const string Reply = "ValheimBroadcaster.Observer.DiscoveryReply2";
        public const int Version = 2;
        public const int MaxPacketBytes = 32768, MaxPlayers = 64, MaxRegions = 128, MaxCachedRegions = 1024;
        public const double FreshSeconds = 10;
        private const int Magic = 0x56424432;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string Text(string value, int maximum, bool empty)
        {
            if (value == null || value.Length > maximum || (!empty && value.Length == 0)) throw new ArgumentException("Invalid observer text length.");
            foreach (char character in value) if (char.IsControl(character)) throw new ArgumentException("Observer text contains control characters.");
            return value;
        }

        public static byte[] Encode(ObserverDiscoverySnapshot snapshot)
        {
            if (snapshot == null || snapshot.Regions.Count > MaxRegions) throw new ArgumentException("Snapshot exceeds wire page limit.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(snapshot.Sequence); writer.Write(snapshot.WorldId);
                writer.Write(snapshot.DiscoveryEnabled); writer.Write(snapshot.SharePrivatePositions);
                writer.Write((ushort)snapshot.Players.Count);
                foreach (ObserverPlayer player in snapshot.Players)
                {
                    WriteText(writer, player.Id); WriteText(writer, player.CharacterId); WriteText(writer, player.Name);
                    writer.Write(player.IsSpawned); writer.Write(player.Position.HasValue);
                    if (player.Position.HasValue) WritePoint(writer, player.Position.Value);
                }
                writer.Write((ushort)snapshot.Regions.Count);
                foreach (ObserverRegion region in snapshot.Regions)
                { WriteText(writer, region.Id); WriteText(writer, region.Name); WritePoint(writer, region.Position); }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new ArgumentException("Observer discovery packet exceeds its bound.");
                return stream.ToArray();
            }
        }

        public static ObserverDiscoverySnapshot Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 30 || bytes.Length > MaxPacketBytes) throw new ArgumentException("Invalid observer packet size.");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) throw new ArgumentException("Observer discovery version mismatch.");
                long sequence = reader.ReadInt64(), world = reader.ReadInt64();
                bool enabled = ReadFlag(reader), sharePrivate = ReadFlag(reader);
                int count = reader.ReadUInt16();
                if (count > MaxPlayers) throw new ArgumentException("Too many observer players.");
                var players = new ObserverPlayer[count];
                for (int i = 0; i < count; i++)
                {
                    string id = ReadText(reader), character = ReadText(reader), name = ReadText(reader);
                    bool spawned = ReadFlag(reader), located = ReadFlag(reader);
                    players[i] = new ObserverPlayer(id, character, name, located ? ReadPoint(reader) : (ObserverPoint?)null, spawned);
                }
                count = reader.ReadUInt16();
                if (count > MaxRegions) throw new ArgumentException("Too many observer regions.");
                var regions = new ObserverRegion[count];
                for (int i = 0; i < count; i++) regions[i] = new ObserverRegion(ReadText(reader), ReadText(reader), ReadPoint(reader));
                if (stream.Position != stream.Length) throw new ArgumentException("Trailing observer discovery data.");
                return new ObserverDiscoverySnapshot(sequence, world, enabled, sharePrivate, players, regions);
            }
        }

        private static bool ReadFlag(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new ArgumentException("Invalid observer flag.");
            return value == 1;
        }
        private static void WritePoint(BinaryWriter writer, ObserverPoint point)
        { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
        private static ObserverPoint ReadPoint(BinaryReader reader) => new ObserverPoint(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Utf8.GetBytes(Text(text, 64, true));
            if (bytes.Length > 256) throw new ArgumentException("Observer text encoding exceeds its bound.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length > 256 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new ArgumentException("Invalid observer text byte length.");
            return Text(Utf8.GetString(reader.ReadBytes(length)), 64, true);
        }
    }

    /// <summary>Receipt-time freshness and bounded per-world accumulation of generated-region pages.</summary>
    public sealed class ObserverDiscoveryCache
    {
        private readonly Dictionary<string, ObserverRegion> regions = new Dictionary<string, ObserverRegion>(StringComparer.Ordinal);
        private readonly Queue<string> regionOrder = new Queue<string>();
        private double received = double.NegativeInfinity;
        public ObserverDiscoverySnapshot? Snapshot { get; private set; }
        public bool Fresh(double now) => Snapshot != null && now >= received && now - received < ObserverDiscoveryProtocol.FreshSeconds;
        public double Age(double now) => Snapshot == null || now < received ? double.PositiveInfinity : now - received;
        public bool Accept(ObserverDiscoverySnapshot snapshot, double now)
        {
            if (snapshot == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (now < received) throw new ArgumentOutOfRangeException(nameof(now), "Observer receipt clock moved backwards.");
            if (Snapshot != null && snapshot.WorldId != Snapshot.WorldId) throw new ArgumentException("World changed inside an observer connection.");
            if (Snapshot != null && snapshot.Sequence <= Snapshot.Sequence) return false;
            if (!snapshot.DiscoveryEnabled) { regions.Clear(); regionOrder.Clear(); }
            foreach (ObserverRegion region in snapshot.Regions)
            {
                if (!regions.ContainsKey(region.Id))
                {
                    if (regions.Count >= ObserverDiscoveryProtocol.MaxCachedRegions) regions.Remove(regionOrder.Dequeue());
                    regionOrder.Enqueue(region.Id);
                }
                regions[region.Id] = region;
            }
            var current = new List<ObserverRegion>(regions.Values);
            current.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            Snapshot = new ObserverDiscoverySnapshot(snapshot.Sequence, snapshot.WorldId, snapshot.DiscoveryEnabled, snapshot.SharePrivatePositions, snapshot.Players, current);
            received = now;
            return true;
        }
        public void Reset() { Snapshot = null; regions.Clear(); regionOrder.Clear(); received = double.NegativeInfinity; }
    }
}
