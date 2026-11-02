using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Owns scenic shot lifetime, progression and sequence memory.
    /// The adapter stages only fully validated routes and reports the progress
    /// actually shown by the live compositor. A proposal is never a commitment.</summary>
    public sealed class ScenicDirector
    {
        public const double BlockedLimitSeconds = 2;
        private const int MaximumHistory = 24;
        private readonly List<ShownShot> history = new List<ShownShot>(MaximumHistory);
        // The loaded-region survey exposes at most 32 subjects. A separate
        // bounded ledger keeps each subject's complete three-shot episode even
        // after its views leave the short global continuity history.
        private readonly Dictionary<string, List<ShownShot>> episodes = new Dictionary<string, List<ShownShot>>(StringComparer.Ordinal);
        private double lastObservation = -1, badPictureSeconds, missingPictureSeconds, now;
        private bool recorded;
        private enum ReplacementCause { None, Requested, BlockedRoute, MissingPicture, UnreadablePicture, Completed }
        private ReplacementCause replacementCause;

        public ScenicShotPlan? Current { get; private set; }
        public ScenicShotPlan? Prepared { get; private set; }
        public double Progress { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public double BlockedSeconds { get; private set; }
        public bool NeedsReplacement => replacementCause != ReplacementCause.None;
        public bool ShouldPrefetch => Current == null || NeedsReplacement ||
            Progress >= .65 || ElapsedSeconds >= Current.DurationSeconds - 4;
        public string Reason { get; private set; } = "Awaiting a measured scenic subject";
        public int RememberedShots => history.Count;

        private readonly struct ShownShot
        {
            internal readonly ScenicShotPlan Plan;
            internal readonly double At;
            internal ShownShot(ScenicShotPlan plan, double at) { Plan = plan; At = at; }
        }

        public void Stage(ScenicShotPlan plan)
        {
            CheckPlan(plan);
            if (Current != null && Current.Id == plan.Id)
                throw new ArgumentException("A prepared replacement needs its own shot identity.", nameof(plan));
            Prepared = plan;
        }

        /// <summary>Removing a staged route does not make it live. Call Accept
        /// only after the adapter has established that route's actual scene.</summary>
        public ScenicShotPlan? TakePrepared()
        { ScenicShotPlan? result = Prepared; Prepared = null; return result; }

        public void Accept(ScenicShotPlan plan, double at)
        {
            CheckPlan(plan); Time(at);
            Current = plan;
            if (Prepared?.Id == plan.Id) Prepared = null;
            Progress = ElapsedSeconds = BlockedSeconds = badPictureSeconds = missingPictureSeconds = 0;
            lastObservation = -1; now = at; recorded = false; replacementCause = ReplacementCause.None;
            Reason = "Establishing " + plan.FeatureKind + " / " + plan.Role;
        }

        /// <summary>Predict the next frame without changing accepted progress.
        /// Pauses discard time, and repeated previews return the same proposal.</summary>
        public double Propose(double deltaSeconds, bool canMove = true, float motionSpeed = 1)
        {
            Time(deltaSeconds);
            if (float.IsNaN(motionSpeed) || float.IsInfinity(motionSpeed) || motionSpeed < 0 || motionSpeed > 1.5f)
                throw new ArgumentOutOfRangeException(nameof(motionSpeed));
            if (Current == null || !canMove || NeedsReplacement || motionSpeed == 0) return Progress;
            return Math.Min(1, Progress + Math.Min(deltaSeconds, .1) * motionSpeed / Current.DurationSeconds);
        }

        /// <summary>Only actual matching live output commits developed progress.
        /// A rejected proposal reports the held accepted progress and its own
        /// motion failure; repeated previews, held textures and gaps earn none.</summary>
        public void Observe(long planId, double at, double displayedProgress, bool livePicture,
            bool readable, bool motionBlocked, bool held = false)
        {
            Time(at); Time(displayedProgress);
            if (displayedProgress > 1) throw new ArgumentOutOfRangeException(nameof(displayedProgress));
            if (Current == null || Current.Id != planId || at <= lastObservation) return;
            double gap = lastObservation >= 0 ? at - lastObservation : 0;
            double dt = gap <= .25 ? gap : 0;
            // Low frame rate must not suspend recovery deadlines indefinitely.
            // Long observation gaps prove no persistent failure and reset them;
            // regularly observed slow frames contribute bounded wall time.
            double recoveryDt = gap <= 2 ? Math.Min(gap, 1) : 0;
            if (gap > 2) BlockedSeconds = badPictureSeconds = missingPictureSeconds = 0;
            lastObservation = now = at;
            if (livePicture && readable)
            {
                Progress = Math.Max(Progress, displayedProgress);
                badPictureSeconds = missingPictureSeconds = 0;
                // Recovery is conditional on the failed picture still being
                // unavailable. Fresh matching live output may recover before
                // its replacement has been prepared; keep developing that shot.
                // A blocked route, completed shot or operator edit remains due.
                if (replacementCause == ReplacementCause.MissingPicture ||
                    replacementCause == ReplacementCause.UnreadablePicture)
                    replacementCause = ReplacementCause.None;
                if (!held)
                {
                    ElapsedSeconds += dt;
                    if (ElapsedSeconds >= 2 && !recorded)
                    {
                        history.Add(new ShownShot(Current, now));
                        if (history.Count > MaximumHistory) history.RemoveAt(0);
                        RememberEpisode(Current);
                        recorded = true;
                    }
                }
            }
            else if (!held)
            {
                badPictureSeconds += recoveryDt;
                missingPictureSeconds = livePicture ? 0 : missingPictureSeconds + recoveryDt;
            }
            if (!held)
                BlockedSeconds = motionBlocked ? BlockedSeconds + recoveryDt : 0;
            else BlockedSeconds = 0;
            if (NeedsReplacement) return;
            if (!held && BlockedSeconds >= BlockedLimitSeconds)
                RequestReplacement("Scenic route remained blocked; use a prepared shot or replan locally", ReplacementCause.BlockedRoute);
            else if (!held && missingPictureSeconds >= .5)
                RequestReplacement("Scenic live output unavailable", ReplacementCause.MissingPicture);
            else if (!held && badPictureSeconds >= 2)
                RequestReplacement("Scenic picture remained unreadable", ReplacementCause.UnreadablePicture);
            else if (!held && (Progress >= .999999 || ElapsedSeconds >= Current.DurationSeconds))
                RequestReplacement("Scenic movement completed; advance the environmental sequence", ReplacementCause.Completed);
            else Reason = held ? "Operator holding the scenic composition" : motionBlocked
                ? "Retaining the readable composition while preparing a different route"
                : "Developing " + Current.FeatureKind + " / " + Current.Role;
        }

        public void RequestReplacement(string reason) => RequestReplacement(reason, ReplacementCause.Requested);

        private void RequestReplacement(string reason, ReplacementCause cause)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            replacementCause = cause; Reason = reason;
        }

        /// <summary>Advance a short feature sequence only from shown footage.
        /// A failed candidate or a staged shot does not consume a sequence beat.</summary>
        public ScenicShotRole PreferredRole(ScenicFeature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            int count = RecentCount(feature.Id);
            if (count == 0) return ScenicShotRole.Establish;
            if (count == 1) return feature.Kind == ScenicFeatureKind.Ridge || feature.Kind == ScenicFeatureKind.Clearing
                ? ScenicShotRole.Reveal : ScenicShotRole.Traverse;
            if (feature.ScopeLabel == "Settlement" || feature.ScopeLabel == "District") return ScenicShotRole.Reveal;
            return feature.Kind == ScenicFeatureKind.Rock || feature.Kind == ScenicFeatureKind.Structure
                ? ScenicShotRole.Detail : ScenicShotRole.Reveal;
        }

        /// <summary>Novelty is admission, not just a score bonus. A completed
        /// three-shot subject episode and equivalent recently shown views must
        /// yield to a genuinely different composition or another place.</summary>
        public bool CanOffer(ScenicShotPlan plan)
        {
            CheckPlan(plan);
            if (!episodes.TryGetValue(plan.FeatureId, out List<ShownShot>? shots)) return true;
            int recent = 0;
            foreach (ShownShot shown in shots)
            {
                if (now - shown.At < 0 || now - shown.At > 300) continue;
                recent++;
                bool repairedCurrentRoute = BlockedSeconds >= BlockedLimitSeconds && Current?.Id == shown.Plan.Id &&
                    plan.Sample(1).CameraPosition.Distance(shown.Plan.Sample(1).CameraPosition) >= 4;
                if (EquivalentView(shown.Plan, plan) && !repairedCurrentRoute) return false;
            }
            return recent < 3;
        }

        /// <summary>Rank actual readable routes. The adapter still rejects
        /// unsafe or unreadable candidates before this editorial comparison.</summary>
        public double Rank(ScenicShotPlan plan, double quality)
        {
            CheckPlan(plan);
            if (double.IsNaN(quality) || double.IsInfinity(quality) || quality < 0 || quality > 1)
                throw new ArgumentOutOfRangeException(nameof(quality));
            double score = quality + plan.Feature!.Confidence * .08 + Math.Min(.1, plan.VisualChange * .2);
            int sameFeature = RecentCount(plan.FeatureId);
            foreach (ShownShot shown in history)
            {
                double age = now - shown.At;
                if (age < 0 || age > 300) continue;
                if (shown.Plan.FeatureId == plan.FeatureId)
                {
                    if (shown.Plan.Role == plan.Role) score -= .16;
                    if (EquivalentView(shown.Plan, plan)) score -= .35;
                }
                else if (!Related(shown.Plan.Feature!, plan.Feature) && shown.Plan.Feature!.Center.Distance(plan.Feature.Center) < Math.Max(8, Math.Min(shown.Plan.Feature.Radius, plan.Feature.Radius)))
                    score -= .12;
            }
            if (plan.Role == PreferredRole(plan.Feature)) score += .12;
            if (Current?.FeatureId == plan.FeatureId && sameFeature > 0 && sameFeature < 3) score += .1;
            if (Current != null && ElapsedSeconds >= 2 && sameFeature == 0)
            {
                if (plan.Feature.ParentId == Current.FeatureId) score += .24;
                else if (plan.Feature.ParentId != null && plan.Feature.ParentId == Current.Feature!.ParentId) score += .1;
            }
            // A partial observation is usable as a local composition, but must
            // not win the editorial comparison by pretending to show a whole base.
            if (plan.Feature.IsPartial && plan.Role == ScenicShotRole.Establish) score -= .06;
            if (sameFeature >= 3) score -= .6;
            return score;
        }

        private static bool Related(ScenicFeature a, ScenicFeature b) =>
            a.ParentId == b.Id || b.ParentId == a.Id || a.ParentId != null && a.ParentId == b.ParentId;

        private int RecentCount(string featureId)
        {
            if (!episodes.TryGetValue(featureId, out List<ShownShot>? shots)) return 0;
            int result = 0;
            foreach (ShownShot shot in shots) if (now >= shot.At && now - shot.At <= 300) result++;
            return result;
        }

        private void RememberEpisode(ScenicShotPlan plan)
        {
            if (!episodes.TryGetValue(plan.FeatureId, out List<ShownShot>? shots))
            {
                if (episodes.Count >= 64)
                {
                    string? oldest = null; double oldestAt = double.PositiveInfinity;
                    foreach (var pair in episodes)
                    {
                        double latest = pair.Value.Count == 0 ? 0 : pair.Value[pair.Value.Count - 1].At;
                        if (latest < oldestAt) { oldestAt = latest; oldest = pair.Key; }
                    }
                    if (oldest != null) episodes.Remove(oldest);
                }
                episodes.Add(plan.FeatureId, shots = new List<ShownShot>(3));
            }
            shots.RemoveAll(shot => now < shot.At || now - shot.At > 300);
            shots.Add(new ShownShot(plan, now));
            if (shots.Count > 3) shots.RemoveAt(0);
        }

        private static bool EquivalentView(ScenicShotPlan a, ScenicShotPlan b)
        {
            ScenicShotSample pa = a.Sample(.5), pb = b.Sample(.5);
            double ax = pa.CameraPosition.X - pa.AimPosition.X, az = pa.CameraPosition.Z - pa.AimPosition.Z;
            double bx = pb.CameraPosition.X - pb.AimPosition.X, bz = pb.CameraPosition.Z - pb.AimPosition.Z;
            double al = Math.Sqrt(ax * ax + az * az), bl = Math.Sqrt(bx * bx + bz * bz);
            double angle = al > .001 && bl > .001 ? Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + az * bz) / (al * bl)))) * 180 / Math.PI : 180;
            double scaleA = a.Feature!.Radius / Math.Max(2, pa.CameraPosition.Distance(pa.AimPosition)) /
                Math.Tan(a.FieldOfView * Math.PI / 360);
            double scaleB = b.Feature!.Radius / Math.Max(2, pb.CameraPosition.Distance(pb.AimPosition)) /
                Math.Tan(b.FieldOfView * Math.PI / 360);
            double ratio = Math.Max(scaleA, scaleB) / Math.Max(.001, Math.Min(scaleA, scaleB));
            return angle < 25 && ratio < 1.35;
        }

        public void Reset()
        {
            Current = Prepared = null; Progress = ElapsedSeconds = BlockedSeconds = 0;
            lastObservation = -1; now = badPictureSeconds = missingPictureSeconds = 0;
            recorded = false; replacementCause = ReplacementCause.None;
            history.Clear(); episodes.Clear(); Reason = "Awaiting a measured scenic subject";
        }
        private static void CheckPlan(ScenicShotPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsDirected) throw new ArgumentException("Dedicated scenic direction requires an immutable directed plan.", nameof(plan));
        }
        private static void Time(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
