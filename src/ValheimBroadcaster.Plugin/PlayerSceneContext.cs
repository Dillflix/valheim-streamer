using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Observes replicated actors without invoking gameplay. It describes evidence,
// not player intent: an attack near a resource is only a work hint, for example.
internal sealed class PlayerSceneContext
{
    internal sealed class Subject
    {
        internal Character Actor = null!;
        internal Player? Player;
        internal string Id = "", CharacterId = "";
        internal Vector3 Focus, Velocity;
        internal float Weight, Radius;
        internal bool IsPrimary, IsOpponent;
    }

    internal sealed class Frame
    {
        internal readonly List<Subject> Subjects = new List<Subject>(6);
        internal Vector3 PrimaryFocus, Focus, Velocity, GroupVelocity;
        internal float Spread, Confidence;
        internal bool Interior;
        internal CinematicActivity Activity;
        internal string Description = "Awaiting loaded player context";
    }

    private sealed class Observation
    {
        internal Character Actor = null!;
        internal Vector3 Position, Velocity;
        internal float LastAt, Health, DamageUntil, AttackUntil, OpponentUntil;
        internal bool Connection;
    }

    private readonly CompanionGroupPolicy grouping = new CompanionGroupPolicy();
    private readonly Dictionary<string, Observation> observations = new Dictionary<string, Observation>(StringComparer.Ordinal);
    private readonly List<CompanionObservation> companions = new List<CompanionObservation>(64);
    private readonly List<string> stale = new List<string>();
    private readonly List<Observation> opponents = new List<Observation>();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly Frame frame = new Frame();
    private Player? primaryActor;
    private ZNet? world;
    private string primaryId = "", primaryCharacter = "";
    private float surveyAt = -1, frameAt = -1, activitySince;
    private CinematicActivity pendingActivity;
    private int sampledFrame = -1, mask;
    internal Func<string, string, ObserverPlayerContext?>? RemoteContext { get; set; }

    internal Frame Update(TargetRoster.Target primary, bool groupsEnabled = true)
    {
        Player? player = primary.LoadedPlayer;
        float now = Time.unscaledTime;
        if (!player || !player.gameObject.activeInHierarchy || !TargetRoster.Finite(player.transform.position))
        { Reset(); return frame; }
        string characterId = player.GetZDOID().ToString();
        if (primaryActor != player || primaryId != primary.Identity.Id || primaryCharacter != characterId ||
            world != ZNet.instance || now < frameAt || frameAt >= 0 && now - frameAt > 1)
        {
            Reset(); primaryActor = player; primaryId = primary.Identity.Id; primaryCharacter = characterId; world = ZNet.instance;
        }
        if (sampledFrame == Time.frameCount) return frame;
        sampledFrame = Time.frameCount;
        float dt = frameAt < 0 ? 0 : Mathf.Clamp(now - frameAt, 0, .1f); frameAt = now;
        frame.PrimaryFocus = player.transform.position + Vector3.up * 1.5f;
        frame.Interior = Character.InInterior(player.transform.position);
        if (surveyAt < 0 || now - surveyAt >= .25f)
        {
            surveyAt = now;
            Survey(primary, now, groupsEnabled);
        }
        // Membership changes slowly; actual actor positions remain current.
        frame.Focus = frame.PrimaryFocus; frame.Velocity = Vector3.zero; frame.GroupVelocity = Vector3.zero;
        frame.Spread = 0;
        float totalWeight = 0;
        Vector3 sum = Vector3.zero, velocity = Vector3.zero;
        for (int i = frame.Subjects.Count - 1; i >= 0; i--)
        {
            Subject subject = frame.Subjects[i];
            if (!subject.Actor || !subject.Actor.gameObject.activeInHierarchy || subject.Actor.IsDead() || !TargetRoster.Finite(subject.Actor.transform.position))
            { frame.Subjects.RemoveAt(i); continue; }
            subject.Focus = Focus(subject.Actor);
            if (observations.TryGetValue(subject.CharacterId, out Observation? observed)) subject.Velocity = observed.Velocity;
            float desiredWeight = subject.IsPrimary ? 2f : subject.IsOpponent ? 1.1f : 1f;
            subject.Weight = subject.IsPrimary ? 2f : Mathf.MoveTowards(subject.Weight, desiredWeight, dt * .8f);
            sum += subject.Focus * subject.Weight; velocity += subject.Velocity * subject.Weight; totalWeight += subject.Weight;
            frame.Spread = Mathf.Max(frame.Spread, Vector3.Distance(frame.PrimaryFocus, subject.Focus));
            if (subject.IsPrimary) frame.Velocity = subject.Velocity;
        }
        if (totalWeight > 0)
        {
            // A companion can widen coverage, but never drag the focus away from the primary encounter.
            frame.Focus = frame.PrimaryFocus + Vector3.ClampMagnitude(sum / totalWeight - frame.PrimaryFocus, frame.Interior ? 2.5f : 6f);
            frame.GroupVelocity = velocity / totalWeight;
        }
        return frame;
    }

    private void Survey(TargetRoster.Target primary, float now, bool groupsEnabled)
    {
        Player player = primary.LoadedPlayer!;
        mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Observation primaryObservation = Observe(player, now);
        ObserverPlayerContext? remote = RemoteContext?.Invoke(primary.Identity.Id, primaryCharacter);
        if (remote != null && remote.RecentHealthLoss(0)) primaryObservation.DamageUntil = now + .5f;
        companions.Clear();
        if (groupsEnabled)
            foreach (Player other in Player.GetAllPlayers())
            {
                if (companions.Count >= 64) break;
                if (!Valid(other) || other == player || other == Player.m_localPlayer ||
                    Vector3.Distance(player.transform.position, other.transform.position) > CompanionGroupPolicy.HardDistance) continue;
                Observation observed = Observe(other, now);
                observed.Connection = Connected(Focus(player), Focus(other));
                bool sharedMotion = primaryObservation.Velocity.magnitude > 1f && observed.Velocity.magnitude > 1f &&
                    Vector3.Dot(primaryObservation.Velocity.normalized, observed.Velocity.normalized) > .7f &&
                    Mathf.Abs(primaryObservation.Velocity.magnitude - observed.Velocity.magnitude) < 3f;
                Vector3 p = other.transform.position;
                companions.Add(new CompanionObservation(other.GetZDOID().ToString(), new AutonomyPosition(p.x, p.y, p.z),
                    Character.InInterior(other.transform.position) == frame.Interior, observed.Connection, sharedMotion));
            }
        Vector3 position = player.transform.position;
        IReadOnlyList<string> members = grouping.Update(now, primaryCharacter, new AutonomyPosition(position.x, position.y, position.z), companions);
        var old = new Dictionary<string, Subject>(StringComparer.Ordinal);
        foreach (Subject subject in frame.Subjects) old[subject.CharacterId] = subject;
        frame.Subjects.Clear();
        Add(player, primary.Identity.Id, true, false, old);
        foreach (string id in members)
            if (observations.TryGetValue(id, out Observation? observation) && Valid(observation.Actor) && observation.Connection)
                Add(observation.Actor, id, false, false, old);

        bool attack = false, damaged = false;
        foreach (Subject subject in frame.Subjects)
            if (observations.TryGetValue(subject.CharacterId, out Observation? observation))
            { attack |= observation.AttackUntil > now; damaged |= observation.DamageUntil > now; }
        opponents.Clear();
        // AI target IDs are not replicated by vanilla SetTargetInfo (only a boolean
        // is). Use a real local target when present, otherwise label co-observed
        // hostile action as contextual evidence rather than claiming exact aggro.
        int inspectedOpponents = 0;
        foreach (Character actor in Character.GetAllCharacters())
        {
            if (!Valid(actor) || actor is Player || Vector3.Distance(position, actor.transform.position) > 24f ||
                Math.Abs(position.y - actor.transform.position.y) > (frame.Interior ? 3.5f : 8f) ||
                Character.InInterior(actor.transform.position) != frame.Interior || !BaseAI.IsEnemy(player, actor)) continue;
            if (++inspectedOpponents > 64) break;
            bool connected = Connected(Focus(player), Focus(actor));
            if (!connected) continue;
            Observation observed = Observe(actor, now);
            BaseAI? ai = actor.GetBaseAI();
            ZNetView? view = actor.GetComponent<ZNetView>();
            Character? target = ai && view && view.IsOwner() ? ai.GetTargetCreature() : null;
            bool targetsGroup = false;
            foreach (Subject subject in frame.Subjects) if (target && target == subject.Actor) targetsGroup = true;
            float nearest = float.MaxValue;
            foreach (Subject subject in frame.Subjects) nearest = Mathf.Min(nearest, Vector3.Distance(subject.Focus, Focus(actor)));
            bool nearbyAction = nearest <= 12f && (observed.AttackUntil > now || attack || damaged && observed.DamageUntil > now);
            if (targetsGroup || nearbyAction) observed.OpponentUntil = now + 3f;
            if (observed.OpponentUntil > now && nearest <= 20f) opponents.Add(observed);
        }
        opponents.Sort((a, b) =>
        {
            bool oldA = old.TryGetValue(a.Actor.GetZDOID().ToString(), out Subject? sa) && sa.IsOpponent;
            bool oldB = old.TryGetValue(b.Actor.GetZDOID().ToString(), out Subject? sb) && sb.IsOpponent;
            int retained = oldB.CompareTo(oldA);
            return retained != 0 ? retained : (a.Position - position).sqrMagnitude.CompareTo((b.Position - position).sqrMagnitude);
        });
        for (int i = 0; i < Math.Min(2, opponents.Count); i++)
            Add(opponents[i].Actor, opponents[i].Actor.GetZDOID().ToString(), false, true, old);

        CinematicActivity desired;
        string evidence;
        float confidence;
        if (opponents.Count > 0) { desired = CinematicActivity.Combat; evidence = "hostile action observed nearby"; confidence = .8f; }
        else if (ObservedWork(player, primaryObservation, now, out evidence)) { desired = CinematicActivity.Work; confidence = .65f; }
        else if (primaryObservation.Velocity.magnitude > 1.2f) { desired = CinematicActivity.Travel; evidence = "sustained observed movement"; confidence = .95f; }
        else { desired = CinematicActivity.Idle; evidence = damaged ? "recent health loss; cause unknown" : attack ? "action observed; activity unknown" : "stationary or slow movement"; confidence = attack || damaged ? .35f : .85f; }
        if (desired != pendingActivity) { pendingActivity = desired; activitySince = now; }
        if (desired == frame.Activity || now - activitySince >= (desired == CinematicActivity.Combat ? .5f : 1.5f)) frame.Activity = desired;
        frame.Confidence = confidence;
        int players = 0;
        foreach (Subject subject in frame.Subjects) if (subject.Player) players++;
        string place = player.GetBaseValue() >= 3 ? "; base-area hint" : frame.Interior ? "; interior" : "";
        frame.Description = $"{frame.Activity}; {players} player(s), {Math.Min(2, opponents.Count)} opponent(s); {evidence}{place}";
        stale.Clear();
        foreach (KeyValuePair<string, Observation> pair in observations)
            if (!pair.Value.Actor || now - pair.Value.LastAt > 4) stale.Add(pair.Key);
        foreach (string id in stale) observations.Remove(id);
    }

    private Observation Observe(Character actor, float now)
    {
        string id = actor.GetZDOID().ToString();
        Vector3 position = actor.transform.position;
        float health = actor.GetHealth();
        if (!observations.TryGetValue(id, out Observation? item) || item.Actor != actor)
        {
            observations[id] = item = new Observation { Actor = actor, Position = position, LastAt = now, Health = health };
        }
        float dt = now - item.LastAt;
        if (dt > .001f && dt <= 1f && Vector3.Distance(item.Position, position) <= 16f)
        {
            Vector3 measured = (position - item.Position) / dt; measured.y = 0;
            item.Velocity = Vector3.Lerp(item.Velocity, Vector3.ClampMagnitude(measured, 20f), 1f - Mathf.Exp(-3f * dt));
            if (health < item.Health - .1f) item.DamageUntil = now + 4f;
        }
        else if (dt > 1 || Vector3.Distance(item.Position, position) > 16f)
        { item.Velocity = Vector3.zero; item.DamageUntil = item.AttackUntil = item.OpponentUntil = 0; }
        if (actor.InAttack()) item.AttackUntil = now + 1.5f;
        item.Position = position; item.LastAt = now; item.Health = health;
        return item;
    }

    private bool ObservedWork(Player player, Observation observation, float now, out string evidence)
    {
        // These two APIs read local fields, not replicated remote-player state.
        if (player == Player.m_localPlayer && (player.InPlaceMode() || player.GetCurrentCraftingStation()))
        { evidence = "local building/crafting interface"; return true; }
        if (observation.AttackUntil > now)
        {
            Vector3 start = player.transform.position + Vector3.up;
            int count = Physics.RaycastNonAlloc(start, player.transform.forward, hits, 4f, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; Collider? first = null;
            for (int i = 0; i < count; i++)
                if (hits[i].collider && !hits[i].collider.GetComponentInParent<Character>() && hits[i].distance < nearest)
                { nearest = hits[i].distance; first = hits[i].collider; }
            if (first && (first.GetComponentInParent<TreeBase>() || first.GetComponentInParent<TreeLog>() ||
                first.GetComponentInParent<MineRock>() || first.GetComponentInParent<MineRock5>()))
            { evidence = "resource-facing attack; gathering inferred"; return true; }
        }
        evidence = ""; return false;
    }

    private bool Connected(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .01f) return true;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!collider || collider.GetComponentInParent<Character>() || collider.GetComponentInParent<TreeBase>() ||
                collider.GetComponentInParent<TreeLog>()) continue;
            return false;
        }
        return true;
    }

    private void Add(Character actor, string id, bool primary, bool opponent, Dictionary<string, Subject> old)
    {
        string character = actor.GetZDOID().ToString();
        if (!old.TryGetValue(character, out Subject? subject) || subject.Actor != actor)
            subject = new Subject { Actor = actor, Weight = primary ? 2f : .05f };
        subject.Id = id; subject.CharacterId = character; subject.Player = actor as Player;
        subject.IsPrimary = primary; subject.IsOpponent = opponent; subject.Focus = Focus(actor);
        subject.Radius = actor is Player ? .6f : Mathf.Clamp(actor.GetRadius(), .45f, 3f);
        frame.Subjects.Add(subject);
    }

    private static Vector3 Focus(Character actor) => actor.transform.position + Vector3.up * (actor is Player ? 1.5f : Mathf.Clamp(actor.GetHeight() * .6f, .5f, 4f));
    private static bool Valid(Character? actor) => actor && actor.gameObject.activeInHierarchy && !actor.IsDead() &&
        !actor.GetZDOID().IsNone() && TargetRoster.Finite(actor.transform.position) &&
        Mathf.Abs(actor.transform.position.x) <= 20000 && Mathf.Abs(actor.transform.position.y) <= 20000 && Mathf.Abs(actor.transform.position.z) <= 20000;

    internal void Reset()
    {
        grouping.Reset(); observations.Clear(); companions.Clear(); stale.Clear(); opponents.Clear(); frame.Subjects.Clear();
        frame.PrimaryFocus = frame.Focus = frame.Velocity = frame.GroupVelocity = Vector3.zero;
        frame.Spread = frame.Confidence = 0; frame.Interior = false; frame.Activity = pendingActivity = CinematicActivity.Idle;
        frame.Description = "Awaiting loaded player context";
        primaryActor = null; primaryId = primaryCharacter = ""; world = null;
        surveyAt = frameAt = -1; activitySince = 0; sampledFrame = -1;
    }
}
