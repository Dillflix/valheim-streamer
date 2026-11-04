using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ValheimBroadcaster.Core
{
    public enum SpatialEvidence { Unknown, Blocked, Clear }

    /// <summary>A measured viewpoint for one subject/lens/composition request.
    /// A free camera position alone is insufficient: Clear must include the
    /// complete view's surface, near-plane, visibility and reachability checks.</summary>
    public sealed class CameraSpatialNode
    {
        public int Id { get; }
        public AutonomyPosition Position { get; }
        public AutonomyPosition Aim { get; }
        public float Quality { get; }
        public float Clearance { get; }
        public float Novelty { get; }
        public double ObservedAt { get; }
        public long Epoch { get; }
        public SpatialEvidence Evidence { get; }

        public CameraSpatialNode(int id, AutonomyPosition position, AutonomyPosition aim,
            float quality, float clearance, float novelty, double observedAt, long epoch,
            SpatialEvidence evidence = SpatialEvidence.Clear)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            SpatialNumbers.Unit(quality, nameof(quality)); SpatialNumbers.Unit(novelty, nameof(novelty));
            SpatialNumbers.Clearance(clearance); SpatialNumbers.Time(observedAt, nameof(observedAt));
            SpatialNumbers.Epoch(epoch); SpatialNumbers.Evidence(evidence);
            Id = id; Position = position; Aim = aim; Quality = quality; Clearance = clearance;
            Novelty = novelty; ObservedAt = observedAt; Epoch = epoch; Evidence = evidence;
        }
    }

    /// <summary>Directed, measured movement between two viewpoints. Clear means
    /// the adapter checked the intervening camera/near-plane corridor and view,
    /// not merely that both endpoint cameras were individually unobstructed.</summary>
    public sealed class CameraSpatialEdge
    {
        public int FromId { get; }
        public int ToId { get; }
        public float Quality { get; }
        public float Clearance { get; }
        public double ObservedAt { get; }
        public long Epoch { get; }
        public SpatialEvidence Evidence { get; }

        public CameraSpatialEdge(int fromId, int toId, float quality, float clearance,
            double observedAt, long epoch, SpatialEvidence evidence = SpatialEvidence.Clear)
        {
            if (fromId < 0 || toId < 0 || fromId == toId) throw new ArgumentOutOfRangeException(nameof(fromId));
            SpatialNumbers.Unit(quality, nameof(quality)); SpatialNumbers.Clearance(clearance);
            SpatialNumbers.Time(observedAt, nameof(observedAt)); SpatialNumbers.Epoch(epoch); SpatialNumbers.Evidence(evidence);
            FromId = fromId; ToId = toId; Quality = quality; Clearance = clearance;
            ObservedAt = observedAt; Epoch = epoch; Evidence = evidence;
        }
    }

    /// <summary>Immutable, bounded evidence snapshot. Unknown and blocked data
    /// remain explicit; absence of a measured connection never means free space.</summary>
    public sealed class CameraSpatialGraph
    {
        public const int MaximumNodes = 128, MaximumEdges = 768;
        public long Epoch { get; }
        public IReadOnlyList<CameraSpatialNode> Nodes { get; }
        public IReadOnlyList<CameraSpatialEdge> Edges { get; }

        public CameraSpatialGraph(long epoch, IReadOnlyList<CameraSpatialNode> nodes, IReadOnlyList<CameraSpatialEdge> edges)
        {
            SpatialNumbers.Epoch(epoch);
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            if (nodes.Count > MaximumNodes || edges.Count > MaximumEdges)
                throw new ArgumentException("Camera spatial observations exceed their bounded capacity.");
            var nodeCopy = new List<CameraSpatialNode>(nodes.Count);
            var ids = new HashSet<int>();
            foreach (CameraSpatialNode node in nodes)
            {
                if (node == null || !ids.Add(node.Id)) throw new ArgumentException("Spatial node identities must be unique.", nameof(nodes));
                nodeCopy.Add(node);
            }
            nodeCopy.Sort((a, b) => a.Id.CompareTo(b.Id));
            var edgeCopy = new List<CameraSpatialEdge>(edges.Count);
            var pairs = new HashSet<long>();
            foreach (CameraSpatialEdge edge in edges)
            {
                if (edge == null || !ids.Contains(edge.FromId) || !ids.Contains(edge.ToId) ||
                    !pairs.Add(((long)edge.FromId << 32) | (uint)edge.ToId))
                    throw new ArgumentException("Spatial edges require distinct known endpoints and unique directed pairs.", nameof(edges));
                edgeCopy.Add(edge);
            }
            edgeCopy.Sort((a, b) => a.FromId != b.FromId ? a.FromId.CompareTo(b.FromId) : a.ToId.CompareTo(b.ToId));
            Epoch = epoch;
            Nodes = new ReadOnlyCollection<CameraSpatialNode>(nodeCopy);
            Edges = new ReadOnlyCollection<CameraSpatialEdge>(edgeCopy);
        }
    }

    public sealed class CameraSpatialRequest
    {
        public double Now { get; }
        public long Epoch { get; }
        public ScenicShotRole Role { get; }
        public double MinDistance { get; }
        public double TargetDistance { get; }
        public double MaxDistance { get; }
        public float MinQuality { get; }
        public float MinClearance { get; }
        public double MaxAge { get; }
        public int? StartId { get; }

        public CameraSpatialRequest(double now, long epoch, ScenicShotRole role, double minDistance = 5,
            double targetDistance = 16, double maxDistance = 40, float minQuality = .5f,
            float minClearance = .25f, double maxAge = 5, int? startId = null)
        {
            SpatialNumbers.Time(now, nameof(now)); SpatialNumbers.Epoch(epoch);
            if (!Enum.IsDefined(typeof(ScenicShotRole), role)) throw new ArgumentOutOfRangeException(nameof(role));
            if (!SpatialNumbers.Finite(minDistance) || minDistance < 1 || !SpatialNumbers.Finite(targetDistance) ||
                targetDistance < minDistance || !SpatialNumbers.Finite(maxDistance) || maxDistance < targetDistance || maxDistance > 120)
                throw new ArgumentOutOfRangeException(nameof(minDistance));
            SpatialNumbers.Unit(minQuality, nameof(minQuality)); SpatialNumbers.Clearance(minClearance);
            if (!SpatialNumbers.Finite(maxAge) || maxAge <= 0 || maxAge > 60) throw new ArgumentOutOfRangeException(nameof(maxAge));
            if (startId < 0) throw new ArgumentOutOfRangeException(nameof(startId));
            Now = now; Epoch = epoch; Role = role; MinDistance = minDistance; TargetDistance = targetDistance;
            MaxDistance = maxDistance; MinQuality = minQuality; MinClearance = minClearance; MaxAge = maxAge; StartId = startId;
        }
    }

    /// <summary>A proposal through measured viewpoints. A smoothing/native
    /// spline pass must independently validate its entire resulting trajectory.</summary>
    public sealed class CameraSpatialRoute
    {
        public IReadOnlyList<CameraSpatialNode> Nodes { get; }
        public double Distance { get; }
        public double Score { get; }
        public float MinimumQuality { get; }
        public double OldestObservation { get; }
        public double PlannedAt { get; }
        public long Epoch { get; }

        internal CameraSpatialRoute(CameraSpatialNode[] nodes, double distance, double score,
            float minimumQuality, double oldestObservation, double plannedAt, long epoch)
        {
            Nodes = new ReadOnlyCollection<CameraSpatialNode>((CameraSpatialNode[])nodes.Clone());
            Distance = distance; Score = score; MinimumQuality = minimumQuality;
            OldestObservation = oldestObservation; PlannedAt = plannedAt; Epoch = epoch;
        }

        public bool IsFresh(double now, long epoch, double maxAge) =>
            SpatialNumbers.Finite(now) && SpatialNumbers.Finite(maxAge) && maxAge > 0 &&
            epoch == Epoch && now >= PlannedAt && now >= OldestObservation && now - OldestObservation <= maxAge;
    }

    internal static class SpatialNumbers
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static void Unit(double value, string name)
        { if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name); }
        internal static void Clearance(double value)
        { if (!Finite(value) || value < 0 || value > 256) throw new ArgumentOutOfRangeException(nameof(value)); }
        internal static void Time(double value, string name)
        { if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
        internal static void Epoch(long epoch)
        { if (epoch <= 0) throw new ArgumentOutOfRangeException(nameof(epoch)); }
        internal static void Evidence(SpatialEvidence evidence)
        { if (!Enum.IsDefined(typeof(SpatialEvidence), evidence)) throw new ArgumentOutOfRangeException(nameof(evidence)); }
    }
}
