using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Bounded parent/child coverage for outdoor place subjects. This
    /// orders geometric observations only; the adapter still validates surfaces.</summary>
    public static class PlaceScenicSelection
    {
        public const int MaximumAttempts = 24, MaximumFeatures = 16;

        public static IReadOnlyList<PlaceNode> Select(PlaceSnapshot snapshot, AutonomyPosition center,
            float radius, string? preferredId = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            var eligible = new Dictionary<string, PlaceNode>(StringComparer.Ordinal);
            foreach (PlaceNode node in snapshot.Nodes)
            {
                if (node.Kind == PlaceKind.Room || node.Kind == PlaceKind.Opening ||
                    (node.Kind == PlaceKind.District || node.Kind == PlaceKind.Settlement) && node.Children.Count <= 1 ||
                    node.Bounds.Radius < 1 || node.Bounds.Radius > 120 || node.Bounds.Height > 120 ||
                    Distance(node.Bounds.Center, center) > radius * radius) continue;
                eligible.Add(node.Id, node);
            }
            var roots = new List<PlaceNode>();
            var children = new Dictionary<string, List<PlaceNode>>(StringComparer.Ordinal);
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PlaceNode node in eligible.Values)
            {
                PlaceNode? parent = snapshot.Find(node.ParentId);
                while (parent != null && !eligible.ContainsKey(parent.Id)) parent = snapshot.Find(parent.ParentId);
                if (parent == null) roots.Add(node);
                else
                {
                    parents[node.Id] = parent.Id;
                    if (!children.TryGetValue(parent.Id, out List<PlaceNode>? list)) children.Add(parent.Id, list = new List<PlaceNode>());
                    list.Add(node);
                }
            }
            Comparison<PlaceNode> comparison = (a, b) => {
                int distance = Distance(a.Bounds.Center, center).CompareTo(Distance(b.Bounds.Center, center));
                return distance != 0 ? distance : StringComparer.Ordinal.Compare(a.Id, b.Id);
            };
            roots.Sort(comparison); foreach (List<PlaceNode> list in children.Values) list.Sort(comparison);
            var result = new List<PlaceNode>(MaximumAttempts);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<PlaceNode>();
            // Keep the current place's context and a representative descendant
            // before selecting other branches. This supports establish->detail.
            if (preferredId != null && eligible.TryGetValue(preferredId, out PlaceNode? preferred))
            {
                var ancestry = new List<PlaceNode>(); PlaceNode node = preferred;
                while (parents.TryGetValue(node.Id, out string? parent)) { node = eligible[parent]; ancestry.Add(node); }
                ancestry.Reverse();
                foreach (PlaceNode ancestor in ancestry)
                {
                    if (seen.Add(ancestor.Id)) result.Add(ancestor);
                    if (children.TryGetValue(ancestor.Id, out List<PlaceNode>? siblings)) foreach (PlaceNode sibling in siblings) pending.Enqueue(sibling);
                }
                Branch(preferred, children, result, seen, pending);
            }
            foreach (PlaceNode root in roots) pending.Enqueue(root);
            while (pending.Count > 0 && result.Count < MaximumAttempts)
                Branch(pending.Dequeue(), children, result, seen, pending);
            return result.AsReadOnly();
        }

        private static void Branch(PlaceNode node, Dictionary<string, List<PlaceNode>> children,
            List<PlaceNode> result, HashSet<string> seen, Queue<PlaceNode> pending)
        {
            while (result.Count < MaximumAttempts && seen.Add(node.Id))
            {
                result.Add(node);
                if (!children.TryGetValue(node.Id, out List<PlaceNode>? list) || list.Count == 0) return;
                // One representative child chain now; sibling branches follow.
                // A large building list cannot consume the whole prefix.
                for (int i = 1; i < list.Count; i++) pending.Enqueue(list[i]);
                node = list[0];
            }
        }
        private static double Distance(AutonomyPosition a, AutonomyPosition b)
        { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return x * x + y * y + z * z; }
    }
}
