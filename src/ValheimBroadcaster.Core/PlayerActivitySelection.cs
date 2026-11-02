using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum PlayerActivityKind { Unknown, Idle, Travel, Work, Action, Combat }

    /// <summary>Observed activity, never claimed intent. Combat requires local
    /// encounter evidence; a remote attack or health loss alone is Action.</summary>
    public sealed class PlayerActivityEvidence
    {
        public string Id { get; }
        public PlayerActivityKind Kind { get; }
        public float Confidence { get; }
        public double FreshUntil { get; }
        public long SceneId { get; }
        public PlayerActivityEvidence(string id, PlayerActivityKind kind, float confidence, double freshUntil, long sceneId = 0)
        {
            Id = AutonomousDirector.Label(id, nameof(id));
            if (!Enum.IsDefined(typeof(PlayerActivityKind), kind) || float.IsNaN(confidence) || confidence < 0 || confidence > 1)
                throw new ArgumentOutOfRangeException(nameof(confidence));
            AutonomousDirector.Time(freshUntil, nameof(freshUntil));
            if (sceneId < 0) throw new ArgumentOutOfRangeException(nameof(sceneId));
            Kind = kind; Confidence = confidence; FreshUntil = freshUntil; SceneId = sceneId;
        }
        public bool Fresh(double now) => now < FreshUntil && FreshUntil - now <= PlayerActivitySelection.MaximumFreshSeconds + .01;
    }

    /// <summary>Bounded scheduling evidence shared by ordinary and autonomous
    /// coverage. It never chooses an unloaded scene or authorizes travel.</summary>
    public sealed class PlayerActivitySelection
    {
        public const double MaximumFreshSeconds = 6, EvidenceSeconds = 2, MaximumUncoveredSeconds = 120;
        public const double MaximumEncounterExtension = 20, AftermathSeconds = 3;
        private sealed class Entry
        {
            internal double UncoveredSince, EvidenceSince, LastObserved = -1;
            internal PlayerActivityEvidence? Evidence;
            internal long SceneId;
            internal double SceneFreshUntil;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly HashSet<string> roster = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> stale = new List<string>();
        private double lastNow = -1, retentionStarted = -1, lastCombat = -1;
        private string? retainedId;
        private long retainedScene;
        public string Reason { get; private set; } = "Fair player rotation";

        public static PlayerActivityEvidence? FromEvents(ObserverPlayerEvents events, double now, long sceneId = 0)
        {
            if (events == null || !events.Fresh(0)) return null;
            AutonomousDirector.Time(now, nameof(now));
            double remaining = Math.Min(MaximumFreshSeconds - events.SampleAgeSeconds, MaximumFreshSeconds - events.HeartbeatAgeSeconds);
            PlayerActivityKind kind = PlayerActivityKind.Idle;
            float confidence = .5f;
            // Damage is an action concern, never proof of combat. Specific
            // replicated crafting takes precedence over its generic animation.
            if (Recent(events.HealthLossAgeSeconds, 5)) { kind = PlayerActivityKind.Action; confidence = .65f; }
            else if (Recent(events.WorkAgeSeconds, 3)) { kind = PlayerActivityKind.Work; confidence = .8f; }
            else if (Recent(events.ActionAgeSeconds, 3)) { kind = PlayerActivityKind.Action; confidence = .65f; }
            else if (Recent(events.MovementAgeSeconds, 2)) { kind = PlayerActivityKind.Travel; confidence = .8f; }
            return new PlayerActivityEvidence(events.Id, kind, confidence, now + remaining, sceneId);
        }

        public static PlayerActivityEvidence? Merge(PlayerActivityEvidence? local, PlayerActivityEvidence? remote, double now)
        {
            if (local?.Fresh(now) != true) local = null;
            if (remote?.Fresh(now) != true) remote = null;
            if (local == null) return remote;
            if (remote == null) return local;
            if (local.Id != remote.Id) throw new ArgumentException("Cannot merge evidence for different players.");
            bool localSpecific = local.Confidence >= .65f &&
                (local.Kind == PlayerActivityKind.Combat || local.Kind == PlayerActivityKind.Work || local.Kind == PlayerActivityKind.Action);
            bool remotePositive = remote.Confidence >= .5f && remote.Kind != PlayerActivityKind.Unknown && remote.Kind != PlayerActivityKind.Idle;
            PlayerActivityEvidence chosen = !localSpecific && remotePositive ? remote : local;
            // The local scene cannot outlive its own independently fresh evidence.
            return new PlayerActivityEvidence(chosen.Id, chosen.Kind, chosen.Confidence,
                local.SceneId != 0 ? Math.Min(chosen.FreshUntil, local.FreshUntil) : chosen.FreshUntil, local.SceneId);
        }
        private static bool Recent(float? age, float within) => age.HasValue && age.Value >= 0 && age.Value <= within;

        public void Update(double now, IReadOnlyList<string> ids, IReadOnlyList<PlayerActivityEvidence>? evidence)
        {
            AutonomousDirector.Time(now, nameof(now));
            if (lastNow >= 0 && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now));
            if (ids == null || ids.Count > 128 || evidence?.Count > 128) throw new ArgumentException("Activity roster exceeds capacity.");
            bool gap = lastNow >= 0 && now - lastNow > 3;
            lastNow = now; roster.Clear();
            foreach (string id in ids)
            {
                AutonomousDirector.Label(id, nameof(ids));
                if (!roster.Add(id)) throw new ArgumentException("Activity roster contains duplicate IDs.");
                if (!entries.ContainsKey(id)) entries.Add(id, new Entry { UncoveredSince = now, EvidenceSince = now });
            }
            stale.Clear();
            foreach (var pair in entries) if (!roster.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (string id in stale) entries.Remove(id);
            roster.Clear();
            if (evidence != null) foreach (PlayerActivityEvidence item in evidence)
            {
                if (item == null || !entries.TryGetValue(item.Id, out Entry entry)) continue;
                if (!roster.Add(item.Id)) throw new ArgumentException("Activity evidence contains duplicate IDs.");
                entry.SceneId = item.Fresh(now) && item.Confidence >= .5f ? item.SceneId : 0;
                entry.SceneFreshUntil = item.FreshUntil;
                if (!item.Fresh(now) || item.Kind == PlayerActivityKind.Unknown || item.Confidence < .5f)
                { entry.Evidence = null; entry.EvidenceSince = now; continue; }
                if (gap || entry.Evidence == null || entry.Evidence.Kind != item.Kind || entry.Evidence.SceneId != item.SceneId ||
                    entry.LastObserved < 0 || now - entry.LastObserved > 3)
                    entry.EvidenceSince = now;
                entry.Evidence = item; entry.LastObserved = now;
            }
            // Absent entries are unknown now; do not coast on a previous frame's evidence.
            foreach (var pair in entries) if (!roster.Contains(pair.Key)) { pair.Value.Evidence = null; pair.Value.EvidenceSince = now; pair.Value.SceneId = 0; }
            if (retainedId != null && !entries.ContainsKey(retainedId)) ResetRetention();
        }

        public void MarkCovered(string id, double now)
        { if (entries.TryGetValue(id, out Entry entry)) entry.UncoveredSince = now; }

        public bool Overdue(string id, double now) => entries.TryGetValue(id, out Entry entry) && now - entry.UncoveredSince >= MaximumUncoveredSeconds;
        public double WaitingSeconds(string id, double now) => entries.TryGetValue(id, out Entry entry) ? Math.Max(0, now - entry.UncoveredSince) : 0;
        public long SceneId(string? id, double now) => id != null && entries.TryGetValue(id, out Entry entry) &&
            now < entry.SceneFreshUntil ? entry.SceneId : 0;

        public double Rank(string id, double now, bool loaded, long currentScene)
        {
            if (!entries.TryGetValue(id, out Entry entry)) return 0;
            // An overdue unshown subject is selected before any new activity.
            double age = Math.Max(0, now - entry.UncoveredSince);
            if (age >= MaximumUncoveredSeconds) return 1000 + age;
            double rank = age / MaximumUncoveredSeconds * .35;
            if (loaded && currentScene != 0 && entry.SceneId == currentScene && now < entry.SceneFreshUntil) rank += .1;
            PlayerActivityEvidence? item = entry.Evidence;
            if (item == null || !item.Fresh(now) || now - entry.EvidenceSince < EvidenceSeconds) return rank;
            double interest = item.Kind == PlayerActivityKind.Combat ? 1 : item.Kind == PlayerActivityKind.Action ? .65 :
                item.Kind == PlayerActivityKind.Work ? .4 : item.Kind == PlayerActivityKind.Travel ? .2 : 0;
            rank += interest * item.Confidence;
            // Cost preference applies only when activity offers a reason to depart
            // from ordinary rotation; an unknown feed preserves its stable order.
            if (interest > 0 && loaded) rank += .2;
            if (interest > 0 && loaded && currentScene != 0 && item.SceneId == currentScene) rank += .15;
            return rank;
        }

        public bool RetainEncounter(string id, double now, bool readable, bool alternativeOverdue)
        {
            if (!readable || alternativeOverdue || !entries.TryGetValue(id, out Entry entry) ||
                entry.Evidence == null || !entry.Evidence.Fresh(now))
            { Reason = !readable ? "Activity retention paused: footage unreadable" : "Fair player rotation"; return false; }
            PlayerActivityEvidence item = entry.Evidence;
            bool sameScene = retainedId == id || retainedScene != 0 && item.SceneId == retainedScene;
            if (!sameScene) ResetRetention();
            bool combat = item.Kind == PlayerActivityKind.Combat && item.Confidence >= .7f && now - entry.EvidenceSince >= EvidenceSeconds;
            if (combat)
            {
                if (retentionStarted < 0) { retentionStarted = now; retainedId = id; retainedScene = item.SceneId; }
                lastCombat = now;
            }
            bool aftermath = !combat && item.Kind != PlayerActivityKind.Unknown && lastCombat >= 0 && now - lastCombat < AftermathSeconds;
            bool retain = retentionStarted >= 0 && now - retentionStarted < MaximumEncounterExtension && (combat || aftermath);
            Reason = retain ? (combat ? "Retaining observed encounter" : "Showing encounter aftermath") : "Encounter extension complete; fair player rotation";
            return retain;
        }

        public void ResetRetention() { retainedId = null; retainedScene = 0; retentionStarted = lastCombat = -1; }
        public void Reset() { entries.Clear(); roster.Clear(); stale.Clear(); lastNow = -1; ResetRetention(); Reason = "Fair player rotation"; }
    }
}
