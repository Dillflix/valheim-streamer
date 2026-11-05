using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

public enum JourneyNavigationEvidence { Unknown, Blocked, Clear }
public enum JourneyNavigationStatus { Idle, Searching, WaitingForObservations, Ready, Exhausted, BudgetExhausted, Invalidated }

public readonly struct JourneyNavigationPose
{
    public readonly AutonomyPosition Position, Aim;
    public JourneyNavigationPose(AutonomyPosition position, AutonomyPosition aim) { Position = position; Aim = aim; }
}

/// <summary>A fixed entry seam and a local loaded search envelope. The remote
/// goal supplies direction, never geometry. Start and Entry are the immutable
/// first two body/aim knots required by the caller's native continuity contract.</summary>
public sealed class JourneyNavigationRequest
{
    public readonly long MissionId, Epoch;
    public readonly JourneyNavigationPose Start, Entry;
    public readonly AutonomyPosition Goal, Carrier;
    public readonly double TargetMetres, MaximumMetres, ReachMetres, StepMetres, ViewDepth;
    public readonly int MaximumNodes, MaximumEdges, MaximumPending;
    public JourneyNavigationRequest(long missionId, long epoch, JourneyNavigationPose start, JourneyNavigationPose entry,
        AutonomyPosition goal, AutonomyPosition carrier, double targetMetres = 32, double maximumMetres = 80,
        double reachMetres = 85, double stepMetres = 4, int maximumNodes = 512, int maximumEdges = 8192, double viewDepth = 12,
        int maximumPending = JourneyNavigationSearch.MaximumPending)
    {
        if (missionId <= 0 || epoch <= 0 || !JourneyNavigationSearch.ValidPose(start) || !JourneyNavigationSearch.ValidPose(entry) ||
            !JourneyNavigationSearch.ValidPoint(goal) || !JourneyNavigationSearch.ValidPoint(carrier) ||
            !Finite(targetMetres) || !Finite(maximumMetres) || !Finite(reachMetres) || !Finite(stepMetres) || !Finite(viewDepth) ||
            targetMetres < 8 || targetMetres > maximumMetres || maximumMetres > 80 || reachMetres < 8 || reachMetres > 100 ||
            stepMetres < 2 || stepMetres > 8 || viewDepth < 0 || viewDepth > 32 || maximumNodes < 16 || maximumNodes > 4096 || maximumEdges < 32 || maximumEdges > 65536 ||
            maximumPending < 1 || maximumPending > JourneyNavigationSearch.MaximumPending ||
            start.Position.Distance(entry.Position) < .5 || start.Position.Distance(entry.Position) > 8 ||
            start.Position.HorizontalDistance(entry.Position) < .5)
            throw new ArgumentOutOfRangeException(nameof(targetMetres));
        MissionId = missionId; Epoch = epoch; Start = start; Entry = entry; Goal = goal; Carrier = carrier;
        TargetMetres = targetMetres; MaximumMetres = maximumMetres; ReachMetres = reachMetres; StepMetres = stepMetres;
        MaximumNodes = maximumNodes; MaximumEdges = maximumEdges; ViewDepth = viewDepth; MaximumPending = maximumPending;
    }
    private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
}

/// <summary>The oracle must evaluate the measured destination pose and swept
/// camera/near-plane body from From, including terrain/water and loading. Aim
/// is a composition parameter, not a requirement that a distant empty-space
/// focus ray remain unobstructed. Unknown must not become a blocked edge.</summary>
public readonly struct JourneyNavigationProbe
{
    public readonly long Id;
    public readonly JourneyNavigationPose From;
    public readonly AutonomyPosition Position, IncomingDirection;
    public readonly AutonomyPosition? RequiredAim;
    public readonly bool IsStart, IsEntry, IsOnward;
    internal JourneyNavigationProbe(long id, JourneyNavigationPose from, AutonomyPosition position,
        AutonomyPosition incomingDirection, AutonomyPosition? requiredAim, bool start, bool entry, bool onward)
    { Id = id; From = from; Position = position; IncomingDirection = incomingDirection; RequiredAim = requiredAim; IsStart = start; IsEntry = entry; IsOnward = onward; }

    /// <summary>Re-observe the already selected next native seam after route
    /// finalization. This creates a query, never a clearance certificate.
    /// Use a reserved positive identity outside the graph's bounded probe IDs.</summary>
    public static JourneyNavigationProbe ExactContinuation(long id, JourneyNavigationPose from, JourneyNavigationPose to)
    {
        if (id <= 0 || !JourneyNavigationSearch.ValidPose(from) || !JourneyNavigationSearch.ValidPose(to) ||
            Math.Abs(from.Position.Distance(to.Position) - 2) > .01)
            throw new ArgumentOutOfRangeException(nameof(id));
        double horizontal = from.Position.HorizontalDistance(to.Position);
        if (horizontal < .5) throw new ArgumentOutOfRangeException(nameof(to));
        var direction = new AutonomyPosition((float)((to.Position.X - from.Position.X) / horizontal), 0,
            (float)((to.Position.Z - from.Position.Z) / horizontal));
        return new JourneyNavigationProbe(id, from, to.Position, direction, to.Aim, false, false, true);
    }
}

public readonly struct JourneyNavigationObservation
{
    public readonly JourneyNavigationEvidence Evidence;
    public readonly JourneyNavigationPose Pose;
    public readonly double Quality, ObservedAt;
    public readonly long Epoch;
    public JourneyNavigationObservation(JourneyNavigationEvidence evidence, JourneyNavigationPose pose,
        double quality, long epoch, double observedAt)
    {
        if (evidence < JourneyNavigationEvidence.Unknown || evidence > JourneyNavigationEvidence.Clear ||
            double.IsNaN(quality) || double.IsInfinity(quality) || quality < 0 || quality > 1 ||
            double.IsNaN(observedAt) || double.IsInfinity(observedAt) || observedAt < 0)
            throw new ArgumentOutOfRangeException(nameof(evidence));
        Evidence = evidence; Pose = pose; Quality = quality; Epoch = epoch; ObservedAt = observedAt;
    }
}

/// <summary>A freshly observed polyline proposal with a separately observed
/// onward edge. Neither straight-edge clearance nor this result certifies the
/// regenerated native spline: the adapter must run complete native preflight
/// and ending validation before admission.</summary>
public sealed class JourneyNavigationPath
{
    public IReadOnlyList<JourneyNavigationPose> Poses { get; }
    public JourneyNavigationPose? OnwardPose { get; }
    public double Length { get; }
    public double GoalProgress { get; }
    public double OldestEvidence { get; }
    public double OnwardObservedAt { get; }
    public long Epoch { get; }
    public bool AtGoal { get; }
    internal JourneyNavigationPath(JourneyNavigationPose[] poses, JourneyNavigationPose? onward, double length,
        double progress, double oldest, double onwardObservedAt, long epoch, bool atGoal)
    { Poses = Array.AsReadOnly(poses); OnwardPose = onward; Length = length; GoalProgress = progress; OldestEvidence = oldest;
      OnwardObservedAt = onwardObservedAt; Epoch = epoch; AtGoal = atGoal; }
}

/// <summary>Incremental bounded kinodynamic graph search. Steering and grade
/// controls are composed at every measured node, allowing alternating turns,
/// local retreat and climbing routes. This is not a fan of whole-route offsets.
/// Every edge is observed separately; an unresolved frontier retains its exact
/// probe identity. Search memory and per-tick work have independent bounds.</summary>
public sealed class JourneyNavigationSearch
{
    public const int MaximumWaypoints = 16, MaximumPending = 256;
    public const double MaximumEvidenceAge = 5, MaximumGrade = .45, MaximumTurnDegrees = 75;
    private static readonly double[] Turns = { 0, -30, 30, -60, 60 };
    private static readonly double[] Grades = { 0, .35, -.35 };
    private static readonly double[] Strides = { 2, 1, .5 };
    private const int ControlCount = 54; // five steering angles + goal-directed steering, three grades/strides
    private readonly List<Node> nodes = new List<Node>();
    private readonly List<int> open = new List<int>();
    private readonly List<Pending> pending = new List<Pending>();
    private readonly Dictionary<string, int> best = new Dictionary<string, int>();
    private readonly Dictionary<string, int> expandedCells = new Dictionary<string, int>();
    private JourneyNavigationRequest? request;
    private long nextProbe;
    private double lastNow, initialGoalDistance;
    private int seedStage, activeNode = -1, activeOperations, scheduling, selections;
    private bool nodeLimit;
    private JourneyNavigationProbe seedProbe;
    private Candidate? candidate;
    public JourneyNavigationStatus Status { get; private set; }
    public JourneyNavigationPath? Result { get; private set; }
    public int Nodes => nodes.Count;
    public int Edges { get; private set; }
    public int Probes { get; private set; }
    public int PendingCount => pending.Count;
    public int RejectedCandidates { get; private set; }
    public bool SelectedProofPending => candidate != null && !Terminal;
    public long ProofRefreshes { get; private set; }
    public string Diagnostics { get; private set; } = "No connected navigation search";

    private sealed class Node
    {
        internal readonly JourneyNavigationPose Pose;
        internal readonly AutonomyPosition Heading;
        internal readonly int Parent, Depth;
        internal readonly double Length, Cost, Rank;
        internal double ObservedAt;
        internal bool CandidateRejected, OnwardTested;
        internal int NextControl;
        internal Node(JourneyNavigationPose pose, AutonomyPosition heading, int parent, int depth,
            double length, double cost, double rank, double at)
        { Pose = pose; Heading = heading; Parent = parent; Depth = depth; Length = length; Cost = cost; Rank = rank; ObservedAt = at; }
    }
    private sealed class Pending
    {
        internal readonly JourneyNavigationProbe Probe;
        internal readonly int Parent;
        internal double RetryAt;
        internal Pending(JourneyNavigationProbe probe, int parent, double retryAt)
        { Probe = probe; Parent = parent; RetryAt = retryAt; }
    }
    private sealed class Candidate
    {
        internal readonly int Node;
        internal readonly List<int> Path;
        internal readonly JourneyNavigationPose? Onward;
        internal double OnwardObservedAt;
        internal int Cursor;
        internal JourneyNavigationProbe? Probe;
        internal Candidate(int node, List<int> path, JourneyNavigationPose? onward, double at)
        { Node = node; Path = path; Onward = onward; OnwardObservedAt = at; }
    }

    public void Begin(JourneyNavigationRequest query, double now)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        Time(now); Reset(); request = query; lastNow = now;
        initialGoalDistance = query.Start.Position.Distance(query.Goal);
        Status = JourneyNavigationStatus.Searching;
        seedProbe = Probe(query.Start, query.Start.Position, Direction(query.Start.Position, query.Entry.Position), query.Start.Aim, true, false, false);
        Diagnostics = "Observing the exact entry pose and tangent";
    }

    /// <summary>Each candidate control, skipped boundary, oracle call and proof
    /// refresh consumes work. Unknown results retain an ID and retry fairly;
    /// they do not discard all other measured branches or restart the search.</summary>
    public JourneyNavigationStatus Step(int budget, double now, long currentEpoch,
        Func<JourneyNavigationProbe, JourneyNavigationObservation> observe)
    {
        if (budget < 1 || budget > 1024) throw new ArgumentOutOfRangeException(nameof(budget));
        if (observe == null) throw new ArgumentNullException(nameof(observe));
        Time(now);
        if (request == null) return Status;
        if (now < lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Navigation requires a monotonic observation clock");
        lastNow = now;
        if (currentEpoch != request.Epoch) { Invalidate(); return Status; }
        if (Terminal) return Status;
        Status = JourneyNavigationStatus.Searching;
        int used = 0;
        while (used < budget && !Terminal)
        {
            if (Probes >= request.MaximumEdges * 4)
            { Finish(JourneyNavigationStatus.BudgetExhausted, "Observation work limit reached; unresolved geometry is not blocked space"); break; }
            if (seedStage < 2)
            {
                used++; var observation = Observe(observe, seedProbe, now);
                if (observation.Evidence == JourneyNavigationEvidence.Unknown)
                { Wait("Waiting for the exact entry pose/tangent observations"); break; }
                if (observation.Evidence == JourneyNavigationEvidence.Blocked || !Exact(seedProbe, observation.Pose))
                { Finish(JourneyNavigationStatus.Exhausted, "The exact entry pose or tangent is physically unavailable"); break; }
                Seed(observation, now); continue;
            }
            if (candidate != null)
            {
                if (PublishOrRefresh(observe, now, ref used, budget)) continue;
                if (Status == JourneyNavigationStatus.WaitingForObservations) break;
                continue;
            }
            // The adapter's partial-observation capacity can be much smaller
            // than the graph. At capacity, finish existing exact probes before
            // producing more work: dropping partials would repeatedly restart
            // the same physical sweeps until their observation lifetime expires.
            bool pendingFull = pending.Count >= request.MaximumPending;
            if (pending.Count > 0 && (pendingFull || activeNode < 0 && open.Count == 0 || scheduling++ % 4 == 0))
            {
                int retry = DuePending(now);
                if (retry >= 0)
                {
                    Pending item = pending[retry]; pending.RemoveAt(retry); used++;
                    Process(item.Probe, item.Parent, Observe(observe, item.Probe, now), now); continue;
                }
                if (pendingFull || activeNode < 0 && open.Count == 0)
                { Wait(pendingFull ? "Observation capacity occupied; retaining and completing pending physical queries" :
                    "Known branches exhausted; waiting at unresolved observed map frontiers"); break; }
            }
            if (activeNode < 0)
            {
                activeNode = TakeBest(); activeOperations = 0;
                if (activeNode < 0)
                {
                    Finish(nodeLimit ? JourneyNavigationStatus.BudgetExhausted : JourneyNavigationStatus.Exhausted,
                        nodeLimit ? "Local graph capacity reached; no complete proposal within this bounded search" : "No connected useful route in the observed local graph");
                    break;
                }
                if (AtGoal(nodes[activeNode]) && !nodes[activeNode].CandidateRejected)
                { StartCandidate(activeNode, null, now); continue; }
            }
            if (Edges >= request.MaximumEdges)
            { Finish(JourneyNavigationStatus.BudgetExhausted, "Local edge expansion limit reached; no claim of global route exhaustion"); break; }
            int parentIndex = activeNode; Node parent = nodes[parentIndex];
            // The next native section begins with this exact two-metre body
            // tangent and extrapolated aim tangent. A convenient sideways
            // escape is not proof that that mandatory seam is traversable.
            if (Eligible(parent) && !parent.OnwardTested)
            {
                parent.OnwardTested = true; used++; Edges++;
                if (OnwardProbe(parentIndex, out JourneyNavigationProbe onwardProbe))
                    Process(onwardProbe, parentIndex, Observe(observe, onwardProbe, now), now);
                continue;
            }
            if (parent.NextControl >= ControlCount) { activeNode = -1; continue; }
            int option = parent.NextControl++; used++; Edges++; activeOperations++;
            // Explore useful depth before enumerating every combination at
            // one node. The remaining controls retain their exact cursor.
            if (activeOperations >= 3 || parent.NextControl >= ControlCount)
            { if (parent.NextControl < ControlCount) open.Add(parentIndex); activeNode = -1; }
            if (parent.Depth >= MaximumWaypoints) continue;
            if (!Control(parent, option, out AutonomyPosition target)) continue;
            var probe = Probe(parent.Pose, target, parent.Heading, null, false, false, false);
            Process(probe, parentIndex, Observe(observe, probe, now), now);
        }
        if (!Terminal && pending.Count >= request.MaximumPending)
            Wait("Observation capacity occupied; retaining and completing pending physical queries");
        else if (!Terminal && pending.Count > 0 && activeNode < 0 && open.Count == 0)
            Wait("Known branches exhausted; waiting at unresolved observed map frontiers");
        else if (!Terminal && Status != JourneyNavigationStatus.WaitingForObservations)
            Diagnostics = $"Connected navigation: {Nodes} nodes, {Edges} controls, {PendingCount} unresolved frontiers";
        return Status;
    }

    /// <summary>A later preparation batch may reuse this exact proposal, but
    /// cannot inherit an aged onward witness. No node or frontier is discarded;
    /// Step refreshes every stale physical observation before republishing.</summary>
    public void ResumeCandidateValidation()
    {
        if (Status != JourneyNavigationStatus.Ready || candidate == null)
            throw new InvalidOperationException("Only a returned navigation candidate can be revalidated");
        Result = null; candidate.Cursor = 0; candidate.Probe = null;
        Status = JourneyNavigationStatus.Searching; Diagnostics = "Revalidating the retained route and onward witness";
    }

    /// <summary>A failed regenerated native curve rejects only this candidate.
    /// It does not renew the episode or throw away other measured branches.</summary>
    public void RejectCandidate()
    {
        if (Status != JourneyNavigationStatus.Ready || candidate == null)
            throw new InvalidOperationException("Only a returned navigation candidate can be rejected");
        nodes[candidate.Node].CandidateRejected = true; candidate = null; Result = null; RejectedCandidates++;
        Status = JourneyNavigationStatus.Searching; Diagnostics = "Native preflight rejected the proposal; searching retained connected alternatives";
    }

    public void Invalidate()
    { Result = null; candidate = null; pending.Clear(); open.Clear(); Status = JourneyNavigationStatus.Invalidated; Diagnostics = "Physical scene context changed; navigation evidence invalidated"; }

    public void Reset()
    {
        nodes.Clear(); open.Clear(); pending.Clear(); best.Clear(); expandedCells.Clear(); request = null; candidate = null; Result = null;
        nextProbe = 0; lastNow = initialGoalDistance = 0; seedStage = activeOperations = scheduling = selections = 0; activeNode = -1;
        nodeLimit = false; Edges = Probes = RejectedCandidates = 0; ProofRefreshes = 0;
        Status = JourneyNavigationStatus.Idle; Diagnostics = "No connected navigation search";
    }

    private bool Terminal => Status == JourneyNavigationStatus.Ready || Status == JourneyNavigationStatus.Exhausted ||
        Status == JourneyNavigationStatus.BudgetExhausted || Status == JourneyNavigationStatus.Invalidated;
    private JourneyNavigationProbe Probe(JourneyNavigationPose from, AutonomyPosition position, AutonomyPosition heading,
        AutonomyPosition? aim, bool start, bool entry, bool onward) => new JourneyNavigationProbe(++nextProbe, from, position, heading, aim, start, entry, onward);

    private JourneyNavigationObservation Observe(Func<JourneyNavigationProbe, JourneyNavigationObservation> oracle,
        JourneyNavigationProbe probe, double now)
    {
        Probes++;
        JourneyNavigationRequest context = request!;
        JourneyNavigationObservation observation = oracle(probe);
        if (observation.Evidence == JourneyNavigationEvidence.Unknown || observation.Epoch != context.Epoch ||
            observation.ObservedAt > now + .000001 || now - observation.ObservedAt > MaximumEvidenceAge)
            return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, context.Epoch, now);
        if (observation.Evidence == JourneyNavigationEvidence.Clear && (!ValidPose(observation.Pose) || !Inside(observation.Pose) ||
            observation.Pose.Position.HorizontalDistance(probe.Position) > .05))
            return new JourneyNavigationObservation(JourneyNavigationEvidence.Blocked, default, 0, context.Epoch, now);
        return observation;
    }

    private void Seed(JourneyNavigationObservation observation, double now)
    {
        AutonomyPosition heading = Direction(request!.Start.Position, request.Entry.Position);
        double length = seedStage == 0 ? 0 : request.Start.Position.Distance(request.Entry.Position);
        var node = new Node(observation.Pose, heading, seedStage - 1, seedStage + 1, length, length,
            length + observation.Pose.Position.Distance(request.Goal) * 1.15, observation.ObservedAt);
        nodes.Add(node); best[Key(node)] = seedStage;
        seedStage++;
        if (seedStage == 1) seedProbe = Probe(request.Start, request.Entry.Position, heading, request.Entry.Aim, false, true, false);
        else open.Add(1);
    }

    private bool Control(Node parent, int option, out AutonomyPosition target)
    {
        int steering = option % 6, grade = option / 6 % 3, stride = option / 18;
        double yaw = steering == 1 ? GoalTurn(parent) : Turns[steering == 0 ? 0 : steering - 1];
        if (Math.Abs(yaw) > MaximumTurnDegrees) { target = default; return false; }
        double angle = yaw * Math.PI / 180;
        double x = parent.Heading.X * Math.Cos(angle) - parent.Heading.Z * Math.Sin(angle);
        double z = parent.Heading.X * Math.Sin(angle) + parent.Heading.Z * Math.Cos(angle);
        double length = request!.StepMetres * Strides[stride];
        if (steering == 1) length = Math.Min(length, parent.Pose.Position.HorizontalDistance(request.Goal));
        if (length < .5) { target = default; return false; }
        double px = parent.Pose.Position.X + x * length, py = parent.Pose.Position.Y + Grades[grade] * length, pz = parent.Pose.Position.Z + z * length;
        if (!Coordinates(px, py, pz)) { target = default; return false; }
        target = new AutonomyPosition((float)px, (float)py, (float)pz);
        return target.Distance(request.Carrier) + request.ViewDepth <= request.ReachMetres &&
            parent.Length + parent.Pose.Position.Distance(target) <= request.MaximumMetres + .000001;
    }

    private bool OnwardProbe(int index, out JourneyNavigationProbe probe)
    {
        Node last = nodes[index]; Node prior = nodes[last.Parent];
        double bodyStep = prior.Pose.Position.Distance(last.Pose.Position);
        double scale = 2 / bodyStep;
        var position = new AutonomyPosition(
            (float)(last.Pose.Position.X + (last.Pose.Position.X - prior.Pose.Position.X) * scale),
            (float)(last.Pose.Position.Y + (last.Pose.Position.Y - prior.Pose.Position.Y) * scale),
            (float)(last.Pose.Position.Z + (last.Pose.Position.Z - prior.Pose.Position.Z) * scale));
        var aim = new AutonomyPosition(
            (float)(last.Pose.Aim.X + (last.Pose.Aim.X - prior.Pose.Aim.X) * scale),
            (float)(last.Pose.Aim.Y + (last.Pose.Aim.Y - prior.Pose.Aim.Y) * scale),
            (float)(last.Pose.Aim.Z + (last.Pose.Aim.Z - prior.Pose.Aim.Z) * scale));
        var pose = new JourneyNavigationPose(position, aim);
        if (!ValidPose(pose) || !Inside(pose)) { probe = default; return false; }
        probe = Probe(last.Pose, position, last.Heading, aim, false, false, true); return true;
    }

    private void Process(JourneyNavigationProbe probe, int parentIndex, JourneyNavigationObservation observation, double now)
    {
        if (observation.Evidence == JourneyNavigationEvidence.Unknown)
        {
            pending.Add(new Pending(probe, parentIndex, now + .05)); return;
        }
        if (observation.Evidence != JourneyNavigationEvidence.Clear) return;
        Node parent = nodes[parentIndex];
        double horizontal = parent.Pose.Position.HorizontalDistance(observation.Pose.Position);
        if (horizontal < .5 || Math.Abs(observation.Pose.Position.Y - parent.Pose.Position.Y) / horizontal > MaximumGrade) return;
        AutonomyPosition heading = Direction(parent.Pose.Position, observation.Pose.Position);
        double turn = Angle(parent.Heading, heading);
        if (turn > MaximumTurnDegrees + .0001) return;
        if (Loops(parentIndex, observation.Pose.Position)) return;
        if (probe.IsOnward)
        {
            if (Eligible(parent) && Exact(probe, observation.Pose))
                StartCandidate(parentIndex, observation.Pose, observation.ObservedAt);
            return;
        }
        double segment = parent.Pose.Position.Distance(observation.Pose.Position), length = parent.Length + segment;
        if (parent.Depth >= MaximumWaypoints || length > request!.MaximumMetres + .000001) return;
        double cost = parent.Cost + segment * (1 + (1 - observation.Quality) * .35) + turn * .008;
        var node = new Node(observation.Pose, heading, parentIndex, parent.Depth + 1, length, cost,
            cost + observation.Pose.Position.Distance(request.Goal) * 1.15, observation.ObservedAt);
        string key = Key(node);
        if (best.TryGetValue(key, out int prior) && nodes[prior].Cost <= cost + .001) return;
        if (nodes.Count >= request.MaximumNodes) { nodeLimit = true; return; }
        int index = nodes.Count; nodes.Add(node); best[key] = index; open.Add(index);
    }

    private bool PublishOrRefresh(Func<JourneyNavigationProbe, JourneyNavigationObservation> oracle, double now, ref int used, int budget)
    {
        Candidate work = candidate!;
        while (work.Cursor < work.Path.Count + (work.Onward.HasValue ? 1 : 0))
        {
            bool onward = work.Cursor == work.Path.Count;
            Node? node = onward ? null : nodes[work.Path[work.Cursor]];
            double at = onward ? work.OnwardObservedAt : node!.ObservedAt;
            if (now - at <= MaximumEvidenceAge && at <= now + .000001) { work.Cursor++; continue; }
            if (used >= budget) return false;
            JourneyNavigationPose pose = onward ? work.Onward!.Value : node!.Pose;
            JourneyNavigationPose from = work.Cursor == 0 ? pose : nodes[work.Path[Math.Min(work.Cursor - 1, work.Path.Count - 1)]].Pose;
            AutonomyPosition heading = work.Cursor == 0 ? nodes[work.Path[0]].Heading : Direction(from.Position, pose.Position);
            work.Probe ??= Probe(from, pose.Position, heading, pose.Aim, work.Cursor == 0, work.Cursor == 1, onward);
            used++; var observed = Observe(oracle, work.Probe.Value, now);
            if (observed.Evidence == JourneyNavigationEvidence.Unknown)
            { Wait("Refreshing a selected route's unresolved physical evidence"); return false; }
            if (observed.Evidence != JourneyNavigationEvidence.Clear || !Exact(work.Probe.Value, observed.Pose))
            { nodes[work.Node].CandidateRejected = true; candidate = null; RejectedCandidates++; return false; }
            if (onward) work.OnwardObservedAt = observed.ObservedAt; else node!.ObservedAt = observed.ObservedAt;
            ProofRefreshes++;
            work.Probe = null; work.Cursor++;
        }
        double oldest = work.Onward.HasValue ? work.OnwardObservedAt : now;
        var poses = new JourneyNavigationPose[work.Path.Count];
        for (int i = 0; i < poses.Length; i++)
        { Node node = nodes[work.Path[i]]; poses[i] = node.Pose; oldest = Math.Min(oldest, node.ObservedAt); }
        // Long observation waits can stale an earlier revalidation. Retry from
        // that same immutable path; never relabel old evidence as fresh.
        if (now - oldest > MaximumEvidenceAge) { work.Cursor = 0; return false; }
        Node end = nodes[work.Node];
        Result = new JourneyNavigationPath(poses, work.Onward, end.Length,
            initialGoalDistance - end.Pose.Position.Distance(request!.Goal), oldest,
            work.Onward.HasValue ? work.OnwardObservedAt : 0, request.Epoch, AtGoal(end));
        Finish(JourneyNavigationStatus.Ready, "Fresh connected body/aim proposal and onward witness; native curve and ending still require full validation");
        return true;
    }

    private void StartCandidate(int index, JourneyNavigationPose? onward, double at)
    {
        var path = new List<int>();
        for (int node = index; node >= 0; node = nodes[node].Parent) path.Add(node);
        path.Reverse(); candidate = new Candidate(index, path, onward, at);
    }
    // Fine-grained turns can consume all native knots well before the nominal
    // distance target. That is a section boundary, not a mission/shot ending;
    // the exact next seam must still be observed before this prefix is offered.
    private bool Eligible(Node node) => !node.CandidateRejected && node.Depth >= 3 &&
        node.Length >= ScenicJourneyPolicy.MinimumWindowMetres &&
        (node.Length >= request!.TargetMetres || node.Depth == MaximumWaypoints) &&
        node.Length <= request!.MaximumMetres &&
        initialGoalDistance - node.Pose.Position.Distance(request.Goal) >= Math.Min(8, node.Length * .25);
    private bool AtGoal(Node node) => node.Depth >= 3 && node.Length >= ScenicJourneyPolicy.MinimumWindowMetres &&
        node.Pose.Position.Distance(request!.Goal) <= Math.Max(.5, request.StepMetres * .3);
    private bool Inside(JourneyNavigationPose pose) => pose.Position.Distance(request!.Carrier) + request.ViewDepth <= request.ReachMetres;
    private bool Loops(int parent, AutonomyPosition point)
    { for (int i = parent; i >= 0; i = nodes[i].Parent) if (nodes[i].Pose.Position.Distance(point) < .75) return true; return false; }
    private int TakeBest()
    {
        int bestSlot = -1; double rank = double.PositiveInfinity;
        // Goal-first search alone spends its bounded graph on attractive
        // cul-de-sacs. Interleave a spatially novel frontier to discover
        // connected ways out, including a temporary retreat from the goal.
        // Neither ordering changes clearance, curvature or admission rules.
        bool explore = selections++ % 4 == 3;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            int index = open[i]; Node node = nodes[index];
            if (best[Key(node)] != index) open.RemoveAt(i);
        }
        for (int i = 0; i < open.Count; i++)
        {
            int index = open[i]; Node node = nodes[index];
            expandedCells.TryGetValue(ExplorationKey(node), out int visits);
            double priority = node.Rank + (explore ? visits * request!.StepMetres * 2 : 0) + node.NextControl * .03;
            if (priority < rank - .000001 || Math.Abs(priority - rank) <= .000001 && (bestSlot < 0 || index < open[bestSlot]))
            { bestSlot = i; rank = priority; }
        }
        if (bestSlot < 0) return -1;
        int result = open[bestSlot]; open.RemoveAt(bestSlot);
        string cell = ExplorationKey(nodes[result]); expandedCells.TryGetValue(cell, out int count); expandedCells[cell] = count + 1;
        return result;
    }
    private string ExplorationKey(Node node) => Math.Floor(node.Pose.Position.X / request!.StepMetres).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
        Math.Floor(node.Pose.Position.Y / request.StepMetres).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
        Math.Floor(node.Pose.Position.Z / request.StepMetres).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private int DuePending(double now)
    {
        // Retried work is appended at the back. Selecting the first due item
        // gives every partial sweep a bounded revisit interval instead of
        // advancing an index while simultaneously removing/reordering items.
        for (int i = 0; i < pending.Count; i++) if (pending[i].RetryAt <= now) return i;
        return -1;
    }
    private double GoalTurn(Node node)
    {
        double x = request!.Goal.X - node.Pose.Position.X, z = request.Goal.Z - node.Pose.Position.Z;
        return Math.Atan2(node.Heading.X * z - node.Heading.Z * x, node.Heading.X * x + node.Heading.Z * z) * 180 / Math.PI;
    }
    private static string Key(Node node)
    {
        double heading = Math.Atan2(node.Heading.Z, node.Heading.X) * 180 / Math.PI;
        double gaze = Math.Atan2(node.Pose.Aim.Z - node.Pose.Position.Z, node.Pose.Aim.X - node.Pose.Position.X) * 180 / Math.PI;
        double pitch = Math.Atan2(node.Pose.Aim.Y - node.Pose.Position.Y, node.Pose.Aim.HorizontalDistance(node.Pose.Position)) * 180 / Math.PI;
        return Math.Round(node.Pose.Position.X).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            Math.Round(node.Pose.Position.Y).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            Math.Round(node.Pose.Position.Z).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            Math.Round(heading / 15).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
            Math.Round(gaze / 30).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + Math.Round(pitch / 15).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private static bool Exact(JourneyNavigationProbe probe, JourneyNavigationPose pose) => pose.Position.Distance(probe.Position) <= .005 &&
        (!probe.RequiredAim.HasValue || pose.Aim.Distance(probe.RequiredAim.Value) <= .005);
    private static AutonomyPosition Direction(AutonomyPosition a, AutonomyPosition b)
    { double x = b.X - a.X, z = b.Z - a.Z, n = Math.Sqrt(x * x + z * z); return new AutonomyPosition((float)(x / Math.Max(.000001, n)), 0, (float)(z / Math.Max(.000001, n))); }
    private static double Angle(AutonomyPosition a, AutonomyPosition b) => Math.Acos(Math.Max(-1, Math.Min(1, a.X * b.X + a.Z * b.Z))) * 180 / Math.PI;
    internal static bool ValidPose(JourneyNavigationPose pose) => ValidPoint(pose.Position) && ValidPoint(pose.Aim) &&
        pose.Position.Distance(pose.Aim) >= 2 && pose.Position.Distance(pose.Aim) <= 180 && pose.Position.HorizontalDistance(pose.Aim) >= .75;
    internal static bool ValidPoint(AutonomyPosition p) => Coordinates(p.X, p.Y, p.Z);
    private static bool Coordinates(double x, double y, double z) => !double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z) &&
        !double.IsInfinity(x) && !double.IsInfinity(y) && !double.IsInfinity(z) && Math.Abs(x) <= 20000 && Math.Abs(y) <= 20000 && Math.Abs(z) <= 20000;
    private void Wait(string message) { Status = JourneyNavigationStatus.WaitingForObservations; Diagnostics = message; }
    private void Finish(JourneyNavigationStatus status, string message) { Status = status; Diagnostics = message; }
    private static void Time(double now) { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
}
