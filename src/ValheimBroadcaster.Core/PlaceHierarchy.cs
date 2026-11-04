using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum PlaceKind { Building, District, Settlement, Room, Opening, Courtyard }

    /// <summary>Observed world-space extent. An extent is a subject/context
    /// hint, never proof that its interior is navigable or fully loaded.</summary>
    public readonly struct PlaceBounds
    {
        public readonly AutonomyPosition Minimum, Maximum;
        public PlaceBounds(AutonomyPosition minimum, AutonomyPosition maximum)
        {
            if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z ||
                maximum.X - minimum.X > 512 || maximum.Y - minimum.Y > 256 || maximum.Z - minimum.Z > 512)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            Minimum = minimum; Maximum = maximum;
        }
        public AutonomyPosition Center => new AutonomyPosition((Minimum.X + Maximum.X) * .5f,
            (Minimum.Y + Maximum.Y) * .5f, (Minimum.Z + Maximum.Z) * .5f);
        public float Width => Maximum.X - Minimum.X;
        public float Depth => Maximum.Z - Minimum.Z;
        public float Height => Maximum.Y - Minimum.Y;
        public float Radius => (float)Math.Sqrt(Width * Width + Depth * Depth) * .5f;
        public bool Contains(AutonomyPosition p, float margin = 0) => p.X >= Minimum.X - margin && p.X <= Maximum.X + margin &&
            p.Y >= Minimum.Y - margin && p.Y <= Maximum.Y + margin && p.Z >= Minimum.Z - margin && p.Z <= Maximum.Z + margin;
        public double HorizontalGap(PlaceBounds b)
        {
            double x = Math.Max(0, Math.Max(Minimum.X - b.Maximum.X, b.Minimum.X - Maximum.X));
            double z = Math.Max(0, Math.Max(Minimum.Z - b.Maximum.Z, b.Minimum.Z - Maximum.Z));
            return Math.Sqrt(x * x + z * z);
        }
        public double VerticalGap(PlaceBounds b) => Math.Max(0, Math.Max(Minimum.Y - b.Maximum.Y, b.Minimum.Y - Maximum.Y));
        public PlaceBounds Union(PlaceBounds b) => new PlaceBounds(
            new AutonomyPosition(Math.Min(Minimum.X, b.Minimum.X), Math.Min(Minimum.Y, b.Minimum.Y), Math.Min(Minimum.Z, b.Minimum.Z)),
            new AutonomyPosition(Math.Max(Maximum.X, b.Maximum.X), Math.Max(Maximum.Y, b.Maximum.Y), Math.Max(Maximum.Z, b.Maximum.Z)));
    }

    public readonly struct PlacePieceObservation
    {
        public readonly string Id;
        public readonly PlaceBounds Bounds;
        public readonly bool PlayerConstructed, HasDoor;
        public PlacePieceObservation(string id, PlaceBounds bounds, bool playerConstructed = false, bool hasDoor = false)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id; Bounds = bounds; PlayerConstructed = playerConstructed; HasDoor = hasDoor;
        }
    }

    /// <summary>A directly measured local room/opening/open yard. Supplying
    /// a courtyard requires observed open sky and surrounding structure; it
    /// must not be inferred merely from the gap between two building centres.</summary>
    public readonly struct PlaceSpaceObservation
    {
        public readonly string Id;
        public readonly PlaceKind Kind;
        public readonly PlaceBounds Bounds;
        public readonly float Confidence;
        public PlaceSpaceObservation(string id, PlaceKind kind, PlaceBounds bounds, float confidence)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96 ||
                (kind != PlaceKind.Room && kind != PlaceKind.Opening && kind != PlaceKind.Courtyard) ||
                float.IsNaN(confidence) || float.IsInfinity(confidence) || confidence < .5f || confidence > 1)
                throw new ArgumentOutOfRangeException(nameof(id));
            Id = id; Kind = kind; Bounds = bounds; Confidence = confidence;
        }
    }

    public sealed class PlaceNode
    {
        public string Id { get; }
        public string? ParentId { get; internal set; }
        public PlaceKind Kind { get; }
        public PlaceBounds Bounds { get; }
        public bool IsPartial { get; }
        public float Confidence { get; }
        public int PieceCount { get; }
        public bool HasPlayerConstruction { get; }
        public IReadOnlyList<string> Children => childrenView;
        private readonly IReadOnlyList<string> childrenView;
        private readonly List<string> children = new List<string>();
        internal PlaceNode(string id, PlaceKind kind, PlaceBounds bounds, bool partial, float confidence, int count, bool built)
        { Id = id; Kind = kind; Bounds = bounds; IsPartial = partial; Confidence = confidence; PieceCount = count; HasPlayerConstruction = built; childrenView = children.AsReadOnly(); }
        internal void AddChild(PlaceNode child) { children.Add(child.Id); child.ParentId = Id; }
    }

    public sealed class PlaceSnapshot
    {
        public long Revision { get; }
        public double ObservedAt { get; }
        public AutonomyPosition Origin { get; }
        public float Radius { get; }
        public bool CoverageComplete { get; }
        public IReadOnlyList<PlaceNode> Nodes { get; }
        internal PlaceSnapshot(long revision, double now, AutonomyPosition origin, float radius, bool complete, List<PlaceNode> nodes)
        { Revision = revision; ObservedAt = now; Origin = origin; Radius = radius; CoverageComplete = complete; Nodes = nodes.AsReadOnly(); }
        public bool Fresh(double now, double maximumAge = 6) => !double.IsNaN(now) && !double.IsInfinity(now) &&
            !double.IsNaN(maximumAge) && !double.IsInfinity(maximumAge) && maximumAge >= 0 && now >= ObservedAt && now - ObservedAt <= maximumAge;
        public PlaceNode? FindContext(AutonomyPosition position)
        {
            PlaceNode? best = null;
            foreach (PlaceNode node in Nodes)
            {
                if (!node.Bounds.Contains(position, 1.5f)) continue;
                if (best == null || Rank(node.Kind) > Rank(best.Kind) || Rank(node.Kind) == Rank(best.Kind) && node.Bounds.Radius < best.Bounds.Radius)
                    best = node;
            }
            return best;
        }
        public PlaceNode? Find(string? id)
        { if (id == null) return null; foreach (PlaceNode node in Nodes) if (node.Id == id) return node; return null; }
        private static int Rank(PlaceKind kind) => kind == PlaceKind.Room || kind == PlaceKind.Opening ? 4 : kind == PlaceKind.Courtyard ? 3 :
            kind == PlaceKind.Building ? 2 : kind == PlaceKind.District ? 1 : 0;
    }

    /// <summary>Bounded hierarchy of observed structural connectivity and
    /// spatial relationships. It does not infer ownership, function, a whole
    /// remote base, or navigable room interiors from component bounds.</summary>
    public sealed class PlaceHierarchy
    {
        public const int MaximumPieces = 512, MaximumSpaces = 32, MaximumBuildings = 128;
        private sealed class Record
        {
            internal string Id = "";
            internal HashSet<string> Members = new HashSet<string>(StringComparer.Ordinal);
            internal double At;
            internal PlaceKind Kind;
        }
        private sealed class Group
        {
            internal PlaceBounds Bounds;
            internal readonly List<int> Indices = new List<int>();
            internal readonly List<string> Members = new List<string>();
            internal bool Built, Partial;
            internal Group(PlaceBounds bounds) { Bounds = bounds; }
        }
        private readonly List<Record> history = new List<Record>(256);
        private long sequence, revision;
        private string fingerprint = "";
        public PlaceSnapshot Snapshot { get; private set; } = new PlaceSnapshot(0, 0, default, 0, false, new List<PlaceNode>());

        public PlaceSnapshot Update(IReadOnlyList<PlacePieceObservation> pieces, IReadOnlyList<PlaceSpaceObservation> spaces,
            AutonomyPosition origin, float radius, bool coverageComplete, double now)
        {
            if (pieces == null || spaces == null || pieces.Count > MaximumPieces || spaces.Count > MaximumSpaces ||
                float.IsNaN(radius) || float.IsInfinity(radius) || radius < 8 || radius > 180 ||
                double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < Snapshot.ObservedAt)
                throw new ArgumentOutOfRangeException(nameof(pieces));
            var ordered = new List<PlacePieceObservation>(pieces.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlacePieceObservation piece in pieces)
            {
                if (string.IsNullOrWhiteSpace(piece.Id) || !unique.Add(piece.Id)) throw new ArgumentException("Piece identities must be unique.", nameof(pieces));
                ordered.Add(piece);
            }
            var uniqueSpaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlaceSpaceObservation space in spaces)
                if (string.IsNullOrWhiteSpace(space.Id) || !uniqueSpaces.Add(space.Kind + ":" + space.Id) ||
                    (space.Kind != PlaceKind.Room && space.Kind != PlaceKind.Opening && space.Kind != PlaceKind.Courtyard) ||
                    float.IsNaN(space.Confidence) || float.IsInfinity(space.Confidence) || space.Confidence < .5f || space.Confidence > 1)
                    throw new ArgumentException("Spaces require unique qualified observations.", nameof(spaces));
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            int[] parent = Parents(ordered.Count);
            for (int a = 0; a < ordered.Count; a++)
                for (int b = a + 1; b < ordered.Count; b++)
                    if (ordered[a].Bounds.HorizontalGap(ordered[b].Bounds) <= .35 && ordered[a].Bounds.VerticalGap(ordered[b].Bounds) <= .65)
                        Union(parent, a, b);
            var groups = new Dictionary<int, Group>();
            for (int i = 0; i < ordered.Count; i++)
            {
                int root = Root(parent, i); PlacePieceObservation piece = ordered[i];
                if (!groups.TryGetValue(root, out Group? group)) groups.Add(root, group = new Group(piece.Bounds));
                group.Bounds = group.Bounds.Union(piece.Bounds); group.Members.Add(piece.Id); group.Indices.Add(i);
                group.Built |= piece.PlayerConstructed;
                group.Partial |= !coverageComplete || origin.HorizontalDistance(piece.Bounds.Center) + piece.Bounds.Radius >= radius - 4;
            }
            var sorted = new List<Group>(groups.Values);
            sorted.Sort((a, b) => { int count = b.Members.Count.CompareTo(a.Members.Count); return count != 0 ? count : StringComparer.Ordinal.Compare(a.Members[0], b.Members[0]); });
            if (sorted.Count > MaximumBuildings) { sorted.RemoveRange(MaximumBuildings, sorted.Count - MaximumBuildings); coverageComplete = false; }
            history.RemoveAll(r => now - r.At > 300);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<PlaceNode>(); var buildings = new List<PlaceNode>();
            foreach (Group group in sorted)
            {
                bool partial = group.Partial || !coverageComplete;
                var node = new PlaceNode(Identity(PlaceKind.Building, group.Members, claimed, now), PlaceKind.Building, group.Bounds,
                    partial, partial ? .55f : .85f, group.Members.Count, group.Built);
                buildings.Add(node); nodes.Add(node);
            }
            // Districts connect nearby buildings while capping the aggregate
            // span; settlement connectivity may then span several districts.
            var districts = Aggregate(buildings, 18, 70, PlaceKind.District, claimed, now);
            nodes.AddRange(districts);
            var settlements = Aggregate(districts, 55, 300, PlaceKind.Settlement, claimed, now);
            nodes.AddRange(settlements);
            foreach (PlaceSpaceObservation space in spaces)
            {
                PlaceNode? owner = null;
                foreach (PlaceNode building in buildings)
                    if (building.Bounds.Contains(space.Bounds.Center, 3) && (owner == null || building.Bounds.Radius < owner.Bounds.Radius)) owner = building;
                if (owner == null && space.Kind == PlaceKind.Courtyard)
                    foreach (PlaceNode district in districts) if (district.Bounds.Contains(space.Bounds.Center, 12)) { owner = district; break; }
                if (owner == null) continue;
                string id = owner.Id + "/" + space.Kind + ":" + space.Id;
                var node = new PlaceNode(id, space.Kind, space.Bounds, owner.IsPartial, Math.Min(owner.Confidence, space.Confidence), 0, owner.HasPlayerConstruction);
                owner.AddChild(node); nodes.Add(node);
            }
            // Doors have direct geometric identity; their state and traversable
            // clearance are deliberately left to current scene/route queries.
            foreach (PlacePieceObservation piece in ordered)
            {
                if (!piece.HasDoor || nodes.Count >= MaximumBuildings * 4 + MaximumSpaces) continue;
                foreach (PlaceNode building in buildings)
                    if (building.Bounds.Contains(piece.Bounds.Center, .5f))
                    {
                        var door = new PlaceNode("opening:" + piece.Id, PlaceKind.Opening, piece.Bounds, building.IsPartial,
                            building.Confidence, 1, piece.PlayerConstructed); building.AddChild(door); nodes.Add(door); break;
                    }
            }
            nodes.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            string signature = Signature(ordered, spaces, origin, radius, coverageComplete);
            if (signature != fingerprint) { revision++; fingerprint = signature; }
            Snapshot = new PlaceSnapshot(revision, now, origin, radius, coverageComplete, nodes);
            return Snapshot;
        }

        private List<PlaceNode> Aggregate(List<PlaceNode> source, float gap, float maximumSpan, PlaceKind kind,
            HashSet<string> claimed, double now)
        {
            var sorted = new List<PlaceNode>(source); sorted.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            var groups = new List<List<PlaceNode>>(); var bounds = new List<PlaceBounds>();
            foreach (PlaceNode node in sorted)
            {
                int chosen = -1; double nearest = double.PositiveInfinity;
                for (int i = 0; i < groups.Count; i++)
                {
                    PlaceBounds combined = bounds[i].Union(node.Bounds);
                    if (combined.Width > maximumSpan || combined.Depth > maximumSpan) continue;
                    foreach (PlaceNode other in groups[i])
                    {
                        double distance = node.Bounds.HorizontalGap(other.Bounds);
                        if (distance <= gap && node.Bounds.VerticalGap(other.Bounds) <= 12 && distance < nearest)
                        { nearest = distance; chosen = i; }
                    }
                }
                if (chosen < 0) { groups.Add(new List<PlaceNode> { node }); bounds.Add(node.Bounds); }
                else { groups[chosen].Add(node); bounds[chosen] = bounds[chosen].Union(node.Bounds); }
            }
            // A later component can bridge groups formed earlier in stable-ID
            // order. Reconcile that connectivity instead of preserving arbitrary
            // insertion-order islands. Span limits still bound each aggregate.
            bool merged;
            do
            {
                merged = false;
                for (int a = 0; a < groups.Count && !merged; a++)
                    for (int b = a + 1; b < groups.Count && !merged; b++)
                    {
                        PlaceBounds combined = bounds[a].Union(bounds[b]);
                        if (combined.Width > maximumSpan || combined.Depth > maximumSpan) continue;
                        bool connected = false;
                        foreach (PlaceNode left in groups[a])
                        {
                            foreach (PlaceNode right in groups[b])
                                if (left.Bounds.HorizontalGap(right.Bounds) <= gap && left.Bounds.VerticalGap(right.Bounds) <= 12)
                                { connected = true; break; }
                            if (connected) break;
                        }
                        if (!connected) continue;
                        groups[a].AddRange(groups[b]); bounds[a] = combined;
                        groups.RemoveAt(b); bounds.RemoveAt(b); merged = true;
                    }
            } while (merged);
            var result = new List<PlaceNode>();
            for (int i = 0; i < groups.Count; i++)
            {
                var members = new List<string>(); bool partial = false, built = false; int count = 0; float confidence = 1;
                foreach (PlaceNode node in groups[i])
                { members.Add(node.Id); partial |= node.IsPartial; built |= node.HasPlayerConstruction; count += node.PieceCount; confidence = Math.Min(confidence, node.Confidence); }
                var aggregate = new PlaceNode(Identity(kind, members, claimed, now), kind, bounds[i], partial, confidence, count, built);
                foreach (PlaceNode child in groups[i]) aggregate.AddChild(child);
                result.Add(aggregate);
            }
            return result;
        }
        private string Identity(PlaceKind kind, List<string> members, HashSet<string> claimed, double now)
        {
            Record? best = null; int overlap = 0;
            foreach (Record record in history)
            {
                if (record.Kind != kind || claimed.Contains(record.Id)) continue;
                int common = 0; foreach (string member in members) if (record.Members.Contains(member)) common++;
                if (common > overlap || common > 0 && common == overlap && StringComparer.Ordinal.Compare(record.Id, best?.Id) < 0)
                { best = record; overlap = common; }
            }
            if (best == null)
            {
                best = new Record { Id = kind.ToString().ToLowerInvariant() + ":" + (++sequence), Kind = kind };
                history.Add(best);
            }
            best.Members.Clear(); foreach (string member in members) best.Members.Add(member); best.At = now;
            claimed.Add(best.Id);
            if (history.Count > 512)
            {
                int oldest = -1; double at = double.PositiveInfinity;
                for (int i = 0; i < history.Count; i++) if (!claimed.Contains(history[i].Id) && history[i].At < at) { oldest = i; at = history[i].At; }
                if (oldest >= 0) history.RemoveAt(oldest);
            }
            return best.Id;
        }
        private static string Signature(List<PlacePieceObservation> pieces, IReadOnlyList<PlaceSpaceObservation> spaces,
            AutonomyPosition origin, float radius, bool complete)
        {
            // Integer quantization prevents tiny transform noise from invalidating
            // a scene; identities and material geometry changes remain visible.
            var b = new System.Text.StringBuilder(); b.Append(complete).Append('|').Append(Q(origin.X)).Append(',').Append(Q(origin.Z)).Append('|').Append(Q(radius));
            foreach (PlacePieceObservation p in pieces)
            { b.Append('|').Append(p.Id).Append(':'); Append(b, p.Bounds); b.Append(p.PlayerConstructed).Append(p.HasDoor); }
            var ordered = new List<PlaceSpaceObservation>(spaces); ordered.Sort((a, c) => { int kind = a.Kind.CompareTo(c.Kind); return kind != 0 ? kind : StringComparer.Ordinal.Compare(a.Id, c.Id); });
            foreach (PlaceSpaceObservation space in ordered) { b.Append('|').Append(space.Kind).Append(':').Append(space.Id); Append(b, space.Bounds); b.Append(Q(space.Confidence)); }
            return b.ToString();
        }
        private static void Append(System.Text.StringBuilder b, PlaceBounds bounds) => b.Append(Q(bounds.Minimum.X)).Append(',').Append(Q(bounds.Minimum.Y)).Append(',').Append(Q(bounds.Minimum.Z)).Append(',')
            .Append(Q(bounds.Maximum.X)).Append(',').Append(Q(bounds.Maximum.Y)).Append(',').Append(Q(bounds.Maximum.Z));
        private static int Q(float value) => (int)Math.Round(value * 10);
        private static int[] Parents(int count) { var result = new int[count]; for (int i = 0; i < count; i++) result[i] = i; return result; }
        private static int Root(int[] parent, int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        private static void Union(int[] parent, int a, int b) { a = Root(parent, a); b = Root(parent, b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
        public void Reset()
        { history.Clear(); sequence = revision = 0; fingerprint = ""; Snapshot = new PlaceSnapshot(0, 0, default, 0, false, new List<PlaceNode>()); }
    }
}
