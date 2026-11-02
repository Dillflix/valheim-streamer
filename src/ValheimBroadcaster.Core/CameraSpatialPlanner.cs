using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Incremental bounded beam search over measured clear viewpoints
    /// and corridors. It ranks cinematic movement, not shortest navigation.
    /// This is deliberately a bounded local proposal search, not a claim of
    /// global optimality or proof that a rejected region has no possible shot.</summary>
    public sealed class CameraSpatialPlanner
    {
        public const int BeamWidth = 48, MaximumWaypoints = 12, MaximumExpansions = 8192;
        private readonly List<State> frontier = new List<State>(CameraSpatialGraph.MaximumNodes);
        private readonly List<State> next = new List<State>(BeamWidth);
        private readonly Dictionary<int, int> indices = new Dictionary<int, int>();
        private List<Connection>[] adjacent = Array.Empty<List<Connection>>();
        private CameraSpatialGraph? graph;
        private CameraSpatialRequest? request;
        private int stateIndex, edgeIndex;
        public bool Complete { get; private set; }
        public CameraSpatialRoute? Result { get; private set; }
        public int Expansions { get; private set; }
        public bool BudgetExhausted { get; private set; }
        public string Diagnostics { get; private set; } = "No spatial request";

        private readonly struct Connection
        {
            internal readonly int Target;
            internal readonly CameraSpatialEdge Edge;
            internal Connection(int target, CameraSpatialEdge edge) { Target = target; Edge = edge; }
        }
        private sealed class State
        {
            internal readonly int[] Path;
            internal readonly double Distance, WeightedQuality, Novelty, Turn, Oldest;
            internal readonly float MinimumQuality, Clearance;
            internal double Rank;
            internal State(int[] path, double distance, double weightedQuality, double novelty, double turn,
                float minimumQuality, float clearance, double oldest)
            {
                Path = path; Distance = distance; WeightedQuality = weightedQuality; Novelty = novelty; Turn = turn;
                MinimumQuality = minimumQuality; Clearance = clearance; Oldest = oldest;
            }
        }

        public void Begin(CameraSpatialGraph source, CameraSpatialRequest query)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (query == null) throw new ArgumentNullException(nameof(query));
            Reset(); graph = source; request = query;
            if (source.Epoch != query.Epoch) { Finish("Spatial epoch changed"); return; }
            adjacent = new List<Connection>[source.Nodes.Count];
            for (int i = 0; i < source.Nodes.Count; i++)
            { indices.Add(source.Nodes[i].Id, i); adjacent[i] = new List<Connection>(); }
            foreach (CameraSpatialEdge edge in source.Edges)
                if (Eligible(edge.Evidence, edge.Quality, edge.Clearance, edge.ObservedAt, edge.Epoch))
                    adjacent[indices[edge.FromId]].Add(new Connection(indices[edge.ToId], edge));
            for (int i = 0; i < source.Nodes.Count; i++)
            {
                CameraSpatialNode node = source.Nodes[i];
                if (query.StartId.HasValue && node.Id != query.StartId.Value || !Eligible(node) || adjacent[i].Count == 0) continue;
                var seed = new State(new[] { i }, 0, 0, node.Novelty, 0, node.Quality, node.Clearance, node.ObservedAt);
                seed.Rank = Rank(seed); frontier.Add(seed);
            }
            // Every eligible start gets one expansion layer before the beam is
            // narrowed. A promising but isolated cluster cannot hide all other
            // starts merely because its first camera scored slightly higher.
            if (frontier.Count == 0) { Finish("No fresh measured clear viewpoints with a corridor"); return; }
            Diagnostics = "Searching measured viewpoint corridors";
        }

        /// <summary>Budget counts considered edges, including rejected ones.
        /// Work and retained state are bounded independently of frame rate.</summary>
        public bool Step(int expansionBudget = 64)
        {
            if (expansionBudget < 1 || expansionBudget > 1024) throw new ArgumentOutOfRangeException(nameof(expansionBudget));
            if (Complete || graph == null || request == null) return Complete;
            int used = 0;
            while (used < expansionBudget && !Complete)
            {
                if (Expansions >= MaximumExpansions)
                { BudgetExhausted = true; Finish(Result != null ? "Best measured route within search budget" : "No useful route within search budget"); break; }
                if (stateIndex >= frontier.Count)
                {
                    if (next.Count == 0) { Finish(Result != null ? "Measured scenic route prepared" : "No connected route satisfies movement and readability"); break; }
                    frontier.Clear(); frontier.AddRange(next); next.Clear(); stateIndex = edgeIndex = 0;
                }
                State state = frontier[stateIndex];
                List<Connection> options = adjacent[state.Path[state.Path.Length - 1]];
                if (state.Path.Length >= MaximumWaypoints || edgeIndex >= options.Count)
                { stateIndex++; edgeIndex = 0; continue; }
                Connection connection = options[edgeIndex++]; used++; Expansions++;
                State? candidate = Extend(state, connection);
                if (candidate == null) continue;
                candidate.Rank = Rank(candidate);
                if (candidate.Distance >= request.MinDistance && Better(candidate)) Save(candidate);
                Insert(candidate);
            }
            if (!Complete) Diagnostics = "Searching measured corridors; edges " + Expansions + "; useful route " + (Result != null);
            return Complete;
        }

        private State? Extend(State previous, Connection connection)
        {
            CameraSpatialNode target = graph!.Nodes[connection.Target];
            if (!Eligible(target)) return null;
            foreach (int visited in previous.Path) if (visited == connection.Target) return null;
            CameraSpatialNode last = graph.Nodes[previous.Path[previous.Path.Length - 1]];
            double segment = last.Position.Distance(target.Position);
            if (segment < .2 || segment > 32 || previous.Distance + segment > request!.MaxDistance) return null;
            double bend = 0;
            if (previous.Path.Length >= 2)
            {
                AutonomyPosition before = graph.Nodes[previous.Path[previous.Path.Length - 2]].Position;
                double cosine = DirectionCosine(before, last.Position, last.Position, target.Position);
                // A moving scenic shot must not ping-pong through measured
                // points to manufacture duration. Wide arcs remain possible.
                if (cosine < -.1736481776669303) return null; // more than 100 degrees at one waypoint
                bend = Math.Acos(Math.Max(-1, Math.Min(1, cosine)));
                if (previous.Turn + bend > Math.PI * 1.15) return null;
            }
            int[] path = new int[previous.Path.Length + 1];
            Array.Copy(previous.Path, path, previous.Path.Length); path[path.Length - 1] = connection.Target;
            float quality = Math.Min(target.Quality, Math.Min(last.Quality, connection.Edge.Quality));
            return new State(path, previous.Distance + segment, previous.WeightedQuality + quality * segment,
                previous.Novelty + target.Novelty, previous.Turn + bend,
                Math.Min(previous.MinimumQuality, quality), Math.Min(previous.Clearance, Math.Min(target.Clearance, connection.Edge.Clearance)),
                Math.Min(previous.Oldest, Math.Min(target.ObservedAt, connection.Edge.ObservedAt)));
        }

        private double Rank(State state)
        {
            CameraSpatialNode first = graph!.Nodes[state.Path[0]], last = graph.Nodes[state.Path[state.Path.Length - 1]];
            double meanQuality = state.Distance > 0 ? state.WeightedQuality / state.Distance : first.Quality;
            double development = Math.Min(1, state.Distance / request!.TargetDistance);
            double overrun = Math.Max(0, state.Distance - request.TargetDistance) / request.TargetDistance;
            double directness = state.Distance > 0 ? first.Position.Distance(last.Position) / state.Distance : 1;
            double clearance = Math.Min(1, state.Clearance / Math.Max(.5, request.MinClearance * 4));
            double motion = RoleMotion(first, last, state.Distance);
            return state.MinimumQuality * .38 + meanQuality * .16 + state.Novelty / state.Path.Length * .10 +
                clearance * .05 + development * .18 + motion * .13 - state.Turn / Math.PI * .14 -
                (1 - directness) * .16 - overrun * .08;
        }

        private double RoleMotion(CameraSpatialNode first, CameraSpatialNode last, double distance)
        {
            if (distance <= .001) return 0;
            double startRange = first.Position.Distance(first.Aim), endRange = last.Position.Distance(last.Aim);
            double rise = Math.Max(-1, Math.Min(1, (last.Position.Y - first.Position.Y) / Math.Max(3, distance * .5)));
            double rangeChange = Math.Min(1, Math.Abs(endRange - startRange) / Math.Max(3, distance));
            double azimuth = Math.Acos(Math.Max(-1, Math.Min(1,
                DirectionCosine(first.Aim, first.Position, last.Aim, last.Position)))) / Math.PI;
            switch (request!.Role)
            {
                case ScenicShotRole.Reveal: return Math.Min(1, Math.Max(0, rise) * .7 + azimuth * 2 * .3);
                case ScenicShotRole.Detail: return Math.Min(1, Math.Max(0, startRange - endRange) / Math.Max(3, distance) * .65 + azimuth * 2 * .35);
                case ScenicShotRole.Traverse: return Math.Min(1, azimuth * 3) * (1 - rangeChange * .35) * (1 - Math.Abs(rise) * .3);
                default: return Math.Min(1, azimuth * 2 + Math.Max(0, rise) * .2) * (1 - rangeChange * .2);
            }
        }

        private bool Better(State state)
        {
            if (Result == null || state.Rank > Result.Score + 1e-9) return true;
            if (state.Rank < Result.Score - 1e-9) return false;
            int count = Math.Min(state.Path.Length, Result.Nodes.Count);
            for (int i = 0; i < count; i++)
            {
                int comparison = graph!.Nodes[state.Path[i]].Id.CompareTo(Result.Nodes[i].Id);
                if (comparison != 0) return comparison < 0;
            }
            return state.Path.Length < Result.Nodes.Count;
        }

        private void Save(State state)
        {
            var route = new CameraSpatialNode[state.Path.Length];
            for (int i = 0; i < route.Length; i++) route[i] = graph!.Nodes[state.Path[i]];
            Result = new CameraSpatialRoute(route, state.Distance, state.Rank, state.MinimumQuality, state.Oldest, request!.Now, request.Epoch);
        }

        private void Insert(State state)
        {
            int at = 0;
            while (at < next.Count && Compare(next[at], state) <= 0) at++;
            if (at >= BeamWidth) return;
            next.Insert(at, state); if (next.Count > BeamWidth) next.RemoveAt(BeamWidth);
        }
        private int Compare(State a, State b)
        {
            if (a.Rank > b.Rank + 1e-9) return -1;
            if (a.Rank < b.Rank - 1e-9) return 1;
            for (int i = 0; i < Math.Min(a.Path.Length, b.Path.Length); i++)
            {
                int order = graph!.Nodes[a.Path[i]].Id.CompareTo(graph.Nodes[b.Path[i]].Id);
                if (order != 0) return order;
            }
            return a.Path.Length.CompareTo(b.Path.Length);
        }
        private bool Eligible(CameraSpatialNode node) =>
            node.Position.Distance(node.Aim) >= .25 && Eligible(node.Evidence, node.Quality, node.Clearance, node.ObservedAt, node.Epoch);
        private bool Eligible(SpatialEvidence evidence, float quality, float clearance, double observedAt, long epoch) =>
            evidence == SpatialEvidence.Clear && epoch == request!.Epoch && quality >= request.MinQuality &&
            clearance >= request.MinClearance && request.Now >= observedAt && request.Now - observedAt <= request.MaxAge;
        private static double DirectionCosine(AutonomyPosition a, AutonomyPosition b, AutonomyPosition c, AutonomyPosition d)
        {
            double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
            double u = d.X - c.X, v = d.Y - c.Y, w = d.Z - c.Z;
            double length = Math.Sqrt((x * x + y * y + z * z) * (u * u + v * v + w * w));
            return length > .000001 ? Math.Max(-1, Math.Min(1, (x * u + y * v + z * w) / length)) : 1;
        }
        private void Finish(string reason) { Complete = true; Diagnostics = reason + "; edges " + Expansions; }
        public void Reset()
        {
            graph = null; request = null; frontier.Clear(); next.Clear(); indices.Clear(); adjacent = Array.Empty<List<Connection>>();
            stateIndex = edgeIndex = Expansions = 0; Complete = BudgetExhausted = false; Result = null; Diagnostics = "No spatial request";
        }
    }
}
