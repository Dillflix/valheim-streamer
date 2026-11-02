using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public readonly struct ActorSpatialProbe
    {
        public readonly AutonomyPosition From, Position, Correction;
        public readonly double FromSeconds, Seconds;
        internal readonly double ParentScore;
        internal readonly AutonomyPosition FirstCorrection, PreviousStep;
        internal readonly AutonomyPosition CorrectionVelocity;
        internal ActorSpatialProbe(AutonomyPosition from, AutonomyPosition position, AutonomyPosition correction,
            double fromSeconds, double seconds, double score, AutonomyPosition first, AutonomyPosition previousStep,
            AutonomyPosition correctionVelocity)
        { From = from; Position = position; Correction = correction; FromSeconds = fromSeconds; Seconds = seconds;
          ParentScore = score; FirstCorrection = first; PreviousStep = previousStep; CorrectionVelocity = correctionVelocity; }
    }

    /// <summary>A finite, time-layered beam of camera corridors for a moving
    /// subject. Only measured readable nodes connected by measured clear edges
    /// survive. The result advises a future follow anchor; it never certifies a
    /// live Cinemachine pose or takes ownership of its Body/Aim components.</summary>
    public sealed class ActorSpatialHorizon
    {
        public const int Layers = 3, BeamWidth = 4, Branches = 6, MaximumProbes = 65;
        public const double EvidenceLifetime = 1.25;
        private readonly List<Node> parents = new List<Node>(BeamWidth), children = new List<Node>(BeamWidth);
        private readonly AutonomyPosition[] nominal = new AutonomyPosition[Layers];
        private readonly ActorSpatialGuidance preview = new ActorSpatialGuidance();
        private AutonomyPosition side, seedCorrection;
        private double stepSize, maximumCorrection, stepSeconds, freedom;
        private int layer, parent, branch;
        private bool pending, preferred;
        private Node origin;
        private Node continuation;
        private bool continuationActive, currentContinuation;
        private int continuationLayer, continuationBudget;
        private ActorSpatialProbe current;
        public bool Active { get; private set; }
        public bool Complete { get; private set; }
        public bool Found { get; private set; }
        public int Probes { get; private set; }
        public double StartedAt { get; private set; }
        public long Epoch { get; private set; }
        public AutonomyPosition FirstCorrection { get; private set; }
        public double Score { get; private set; }

        private readonly struct Node
        {
            internal readonly AutonomyPosition Position, Correction, First, Step, Velocity;
            internal readonly double Score;
            internal Node(AutonomyPosition position, AutonomyPosition correction, AutonomyPosition first, AutonomyPosition step, double score,
                AutonomyPosition velocity = default)
            { Position = position; Correction = correction; First = first; Step = step; Score = score; Velocity = velocity; }
        }

        public void Begin(double now, long epoch, AutonomyPosition camera, IReadOnlyList<AutonomyPosition> nominalFuture,
            AutonomyPosition lateralAxis, AutonomyPosition existingCorrection, double freedom, double horizonSeconds = 1.5,
            AutonomyPosition correctionVelocity = default)
        {
            if (!Finite(now) || now < 0 || epoch < 0 || nominalFuture == null || nominalFuture.Count != Layers ||
                !Finite(freedom) || freedom < 0 || freedom > 1 || Length(lateralAxis) < .99 || Length(lateralAxis) > 1.01 ||
                Math.Abs(lateralAxis.Y) > .01 || Length(existingCorrection) > 6.001 ||
                !Finite(horizonSeconds) || horizonSeconds < 1 || horizonSeconds > 4.5 || Length(correctionVelocity) > 3.001)
                throw new ArgumentOutOfRangeException(nameof(nominalFuture));
            Reset(); StartedAt = now; Epoch = epoch; side = lateralAxis; seedCorrection = existingCorrection;
            stepSize = .35 + freedom * .95; maximumCorrection = 1.25 + freedom * 4.75;
            stepSeconds = horizonSeconds / Layers;
            this.freedom = freedom;
            for (int i = 0; i < Layers; i++) nominal[i] = nominalFuture[i];
            origin = new Node(camera, existingCorrection, existingCorrection, default, 0, correctionVelocity);
            parents.Add(origin); Active = preferred = true; continuationBudget = 8;
        }

        public bool TryProbe(double now, long epoch, out ActorSpatialProbe probe)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            probe = default;
            if (!Active || now < StartedAt || now - StartedAt > EvidenceLifetime || epoch != Epoch)
            { Active = false; pending = false; return false; }
            if (pending) { probe = current; return true; }
            if (continuationActive)
            {
                if (continuationBudget-- > 0 && CreateProbe(continuation, continuationLayer, continuation.First, out probe))
                { currentContinuation = true; return true; }
                continuationActive = false;
            }
            currentContinuation = false;
            while (Active)
            {
                if (parent >= parents.Count)
                {
                    if (children.Count == 0) { Finish(false); return false; }
                    layer++; parent = branch = 0;
                    if (layer == Layers)
                    { FirstCorrection = children[0].First; Score = children[0].Score / Layers; Finish(true); return false; }
                    parents.Clear(); parents.AddRange(children); children.Clear();
                }
                Node from = parents[parent];
                AutonomyPosition correction = from.Correction;
                // The zero-restoring branch prevents accumulating avoidance
                // offset forever; unchanged offsets retain continuity otherwise.
                if (branch == 0) correction = MoveTowards(correction, default, stepSize * .5);
                else if (branch == 2) correction = Add(correction, Scale(side, stepSize));
                else if (branch == 3) correction = Add(correction, Scale(side, -stepSize));
                else if (branch == 4) correction = Add(correction, new AutonomyPosition(0, (float)(stepSize * .65), 0));
                else if (branch == 5) correction = Add(correction, new AutonomyPosition(0, (float)(-stepSize * .65), 0));
                if (preferred) parent = parents.Count;
                else { branch++; if (branch == Branches) { branch = 0; parent++; } }
                // Existing larger offsets may be retained/relaxed in a newly
                // confined context, but cannot grow beyond its measured freedom.
                if (Length(correction) > Math.Max(maximumCorrection, Length(seedCorrection)) + .001) continue;
                if (CreateProbe(from, layer, correction, out probe)) return true;
            }
            return false;
        }

        /// <summary>Unknown observations terminate this branch, not the whole
        /// search. The adapter can retry an unchanged pending probe before
        /// reporting it, while the global evidence deadline remains finite.</summary>
        public void Observe(bool known, bool clear, double pictureQuality)
        {
            if (!pending || !Active) throw new InvalidOperationException("No actor spatial probe is pending.");
            if (!Finite(pictureQuality) || pictureQuality < 0 || pictureQuality > 1)
                throw new ArgumentOutOfRangeException(nameof(pictureQuality));
            pending = false; Probes++;
            if (currentContinuation)
            {
                if (!known || !clear || pictureQuality < .6) { continuationActive = false; return; }
                Node continued = MeasuredNode(pictureQuality);
                if (continuationLayer == Layers - 1)
                { FirstCorrection = continued.First; Score = continued.Score / Layers; Finish(true); }
                else { continuation = continued; continuationLayer++; }
                return;
            }
            // A high-quality nominal continuation needs three measured edges,
            // not thirty redundant alternatives. If it fails, resume bounded
            // search from the same frozen root; shared evidence remains reusable.
            if (preferred && (!known || !clear || pictureQuality < .6))
            {
                preferred = false; parents.Clear(); children.Clear(); parents.Add(origin);
                layer = parent = branch = 0;
                return;
            }
            if (!known || !clear) return;
            Node node = MeasuredNode(pictureQuality);
            if (!preferred && layer < Layers - 1 && pictureQuality >= .6 && continuationBudget > 0)
            {
                // Prove a useful complete continuation before waiting for every
                // sibling. On slow frames, an already readable escape is more
                // valuable than a marginally higher-scoring uncompleted search.
                continuation = node; continuationLayer = layer + 1; continuationActive = true;
            }
            // Restoring a zero offset and retaining it describe the same node.
            // Duplicate branches must not occupy both slots and erase detours.
            for (int i = 0; i < children.Count; i++)
                if (children[i].Position.Distance(node.Position) < .001 && children[i].First.Distance(node.First) < .001)
                { if (children[i].Score >= node.Score) return; children.RemoveAt(i); break; }
            int index = 0;
            while (index < children.Count && children[index].Score >= node.Score) index++;
            if (index < BeamWidth) children.Insert(index, node);
            if (children.Count > BeamWidth) children.RemoveAt(children.Count - 1);
        }

        private bool CreateProbe(Node from, int at, AutonomyPosition goal, out ActorSpatialProbe probe)
        {
            probe = default;
            // Search the same bounded control response that will execute,
            // not an instant sidestep the live camera could never reach.
            preview.Seed(from.Correction, from.Velocity);
            for (double remaining = stepSeconds; remaining > .000001; remaining -= .05)
                preview.Step(Math.Min(.05, remaining), goal, freedom);
            AutonomyPosition correction = preview.Offset;
            if (Length(correction) > Math.Max(maximumCorrection, Length(seedCorrection)) + .001) return false;
            AutonomyPosition delta = Subtract(correction, seedCorrection);
            double x = nominal[at].X + delta.X, y = nominal[at].Y + delta.Y, z = nominal[at].Z + delta.Z;
            if (Math.Abs(x) > 20000 || Math.Abs(y) > 20000 || Math.Abs(z) > 20000) return false;
            AutonomyPosition point = new AutonomyPosition((float)x, (float)y, (float)z);
            current = new ActorSpatialProbe(from.Position, point, correction, at * stepSeconds,
                (at + 1) * stepSeconds, from.Score, at == 0 ? goal : from.First, from.Step, preview.Velocity);
            pending = true; probe = current; return true;
        }

        private Node MeasuredNode(double pictureQuality)
        {
            AutonomyPosition step = Subtract(current.Position, current.From);
            double acceleration = Length(Subtract(step, current.PreviousStep));
            double correctionChange = Length(Subtract(current.Correction, seedCorrection));
            double utility = current.ParentScore + pictureQuality - .045 * correctionChange - .02 * Length(current.Correction)
                - .025 * Math.Abs(current.Correction.Y - seedCorrection.Y)
                - (current.FromSeconds == 0 ? 0 : .035 * acceleration);
            return new Node(current.Position, current.Correction, current.FirstCorrection, step, utility, current.CorrectionVelocity);
        }

        private void Finish(bool found) { Active = pending = continuationActive = false; Complete = true; Found = found; }
        public void Reset()
        {
            parents.Clear(); children.Clear(); Active = Complete = Found = pending = preferred = false;
            continuationActive = currentContinuation = false; continuationLayer = continuationBudget = 0;
            layer = parent = branch = Probes = 0; StartedAt = Score = 0; Epoch = 0; FirstCorrection = default;
        }
        internal static AutonomyPosition Add(AutonomyPosition a, AutonomyPosition b) => new AutonomyPosition(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        internal static AutonomyPosition Subtract(AutonomyPosition a, AutonomyPosition b) => new AutonomyPosition(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        internal static AutonomyPosition Scale(AutonomyPosition a, double b) => new AutonomyPosition((float)(a.X * b), (float)(a.Y * b), (float)(a.Z * b));
        internal static double Length(AutonomyPosition a) => a.Distance(default);
        internal static AutonomyPosition MoveTowards(AutonomyPosition a, AutonomyPosition b, double distance)
        { AutonomyPosition delta = Subtract(b, a); double length = Length(delta); return length <= distance || length < .000001 ? b : Add(a, Scale(delta, distance / length)); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double PredictionSeconds(bool confined, bool uncertainActivity, double coherence, double confidence, double speed)
        {
            if (!Finite(coherence) || coherence < 0 || coherence > 1 || !Finite(confidence) || confidence < 0 || confidence > 1 ||
                !Finite(speed) || speed < 0) throw new ArgumentOutOfRangeException(nameof(coherence));
            double desired = confined || uncertainActivity ? 1.25 : 1.5 + 3 * coherence * confidence;
            // Each edge remains within the shared scene's local 24m corridor.
            return Math.Max(1, Math.Min(desired, 60 / Math.Max(1, speed)));
        }
    }

    /// <summary>One bounded correction to the existing world follow anchor.
    /// A new future plan changes the goal, never the current position/velocity.</summary>
    public sealed class ActorSpatialGuidance
    {
        public AutonomyPosition Offset { get; private set; }
        public AutonomyPosition Velocity { get; private set; }
        public void Step(double dt, AutonomyPosition goal, double freedom)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0 ||
                double.IsNaN(freedom) || double.IsInfinity(freedom) || freedom < 0 || freedom > 1 || ActorSpatialHorizon.Length(goal) > 6.001)
                throw new ArgumentOutOfRangeException(nameof(dt));
            double elapsed = Math.Min(.1, dt), speed = .5 + 2.5 * freedom, acceleration = 1 + 3 * freedom;
            AutonomyPosition delta = ActorSpatialHorizon.Subtract(goal, Offset);
            double distance = ActorSpatialHorizon.Length(delta);
            // A proportional arrival envelope begins braking before the final
            // step. Snapping to the goal at an overshoot would erase velocity
            // and violate the same acceleration bound used by prediction.
            double braking = Math.Max(0, Math.Sqrt(acceleration * acceleration * elapsed * elapsed +
                2 * acceleration * distance) - acceleration * elapsed);
            AutonomyPosition wanted = distance < .00001 ? default : ActorSpatialHorizon.Scale(delta,
                Math.Min(speed, Math.Min(distance * 2.5, braking)) / distance);
            AutonomyPosition nextVelocity = ActorSpatialHorizon.MoveTowards(Velocity, wanted, acceleration * elapsed);
            AutonomyPosition step = ActorSpatialHorizon.Scale(ActorSpatialHorizon.Add(Velocity, nextVelocity), elapsed * .5);
            if (distance < .00005 && ActorSpatialHorizon.Length(Velocity) <= acceleration * elapsed)
            { Offset = goal; Velocity = default; }
            else { Offset = ActorSpatialHorizon.Add(Offset, step); Velocity = nextVelocity; }
        }
        public void Reject(AutonomyPosition retainedOffset)
        {
            if (ActorSpatialHorizon.Length(retainedOffset) > 6.001) throw new ArgumentOutOfRangeException(nameof(retainedOffset));
            Offset = retainedOffset; Velocity = default;
        }
        internal void Seed(AutonomyPosition offset, AutonomyPosition velocity)
        { Offset = offset; Velocity = velocity; }
        public void Reset() { Offset = Velocity = default; }
    }
}
