using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Observes replicated actors without invoking gameplay. It describes evidence,
// not player intent: an attack near a resource is only a work hint, for example.
internal sealed class PlayerSceneContext
{
    private sealed class Observation
    {
        internal Character Actor = null!;
        internal Vector3 Position, Velocity;
        internal float LastAt, Health, DamageUntil, AttackUntil, OpponentUntil;
        internal bool Connection;
    }

    private readonly CompanionGroupPolicy grouping = new CompanionGroupPolicy();
    private readonly LocalRoomSurvey rooms = new LocalRoomSurvey();
    private readonly LocomotionObservationPolicy locomotion = new LocomotionObservationPolicy();
    private readonly Dictionary<string, Observation> observations = new Dictionary<string, Observation>(StringComparer.Ordinal);
    private readonly List<CompanionObservation> companions = new List<CompanionObservation>(64);
    private readonly List<string> stale = new List<string>();
    private readonly List<Observation> opponents = new List<Observation>();
    private readonly EncounterPolicy encounters = new EncounterPolicy();
    private readonly List<EncounterObservation> encounterObservations = new List<EncounterObservation>(64);
    private readonly List<Character> encounterActors = new List<Character>(64);
    private Character? encounterActor;
    private readonly RangedAimContinuity rangedAim = new RangedAimContinuity();
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly CameraSceneFrame frame = new CameraSceneFrame { Description = "Awaiting loaded player context" };
    private Player? primaryActor;
    private ZNet? world;
    private string primaryId = "", primaryCharacter = "";
    private float surveyAt = -1, frameAt = -1, activitySince;
    private CinematicActivity pendingActivity;
    private int sampledFrame = -1, mask;
    private string interestId = "", pendingInterestId = "", interestKind = "none";
    private Character? interestActor;
    private Collider? interestResource;
    private Vector3 interestPoint;
    private float interestSince, interestSeenAt = -1, interestStrength;
    private bool facingInitialized;
    private long nextSceneId;
    private static readonly int SwimmingKey = unchecked(438569 + ZSyncAnimation.GetHash("inWater"));
    private static readonly int CraftingKey = unchecked(438569 + ZSyncAnimation.GetHash("crafting"));
    internal Func<string, string, ObserverPlayerContext?>? RemoteContext { get; set; }
    internal CameraSceneFrame Current => frame;
    internal string PrimaryId => primaryId;
    internal bool Fresh => Valid(primaryActor) && world == ZNet.instance && frameAt >= 0 &&
        Time.unscaledTime >= frameAt && Time.unscaledTime - frameAt <= 1;

    internal CameraSceneFrame Update(TargetRoster.Target primary, bool groupsEnabled = true)
    {
        Player? player = primary.LoadedPlayer;
        float now = Time.unscaledTime;
        if (!player || !player.gameObject.activeInHierarchy || player.IsDead() || !TargetRoster.Finite(player.transform.position) ||
            !Finite(now) || Mathf.Abs(player.transform.position.x) > 20000 || Mathf.Abs(player.transform.position.y) > 20000 ||
            Mathf.Abs(player.transform.position.z) > 20000)
        { Reset(); return frame; }
        string characterId = player.GetZDOID().ToString();
        bool changedPrimary = primaryActor != player || primaryId != primary.Identity.Id || primaryCharacter != characterId;
        bool continuousWorld = world == ZNet.instance && frameAt >= 0 && now >= frameAt && now - frameAt <= 1;
        bool handoff = changedPrimary && continuousWorld && groupsEnabled && TryGroupHandoff(player, characterId, now);
        if (!handoff && (changedPrimary || !continuousWorld ||
            Vector3.Distance(player.transform.position + Vector3.up * 1.5f, frame.PrimaryFocus) > 16f))
        {
            Reset(); frame.SceneId = ++nextSceneId;
        }
        if (changedPrimary) { rangedAim.Reset(); encounters.Reset(); ClearEncounter(); }
        if (changedPrimary || primaryActor == null)
        { primaryActor = player; primaryId = primary.Identity.Id; primaryCharacter = characterId; world = ZNet.instance; surveyAt = -1; sampledFrame = -1; }
        if (sampledFrame == Time.frameCount) return frame;
        sampledFrame = Time.frameCount;
        float dt = frameAt < 0 ? 0 : Mathf.Clamp(now - frameAt, 0, .1f); frameAt = now;
        frame.PrimaryFocus = player.transform.position + Vector3.up * 1.5f;
        frame.Interior = Character.InInterior(player.transform.position);
        rooms.Update(player, frame.Velocity, now, frame);
        if (surveyAt < 0 || now - surveyAt >= .25f)
        {
            surveyAt = now;
            Survey(primary, now, groupsEnabled);
        }
        // Membership changes slowly; actual actor positions remain current.
        Vector3 previousFocus = frame.Focus;
        frame.Focus = frame.PrimaryFocus; frame.Velocity = Vector3.zero; frame.GroupVelocity = Vector3.zero;
        frame.Spread = 0;
        float totalWeight = 0;
        Vector3 sum = Vector3.zero, velocity = Vector3.zero;
        for (int i = frame.Subjects.Count - 1; i >= 0; i--)
        {
            CameraSceneSubject subject = frame.Subjects[i];
            if (!subject.Actor || !subject.Actor.gameObject.activeInHierarchy || subject.Actor.IsDead() || !TargetRoster.Finite(subject.Actor.transform.position))
            { frame.Subjects.RemoveAt(i); continue; }
            subject.Focus = Focus(subject.Actor);
            if (observations.TryGetValue(subject.CharacterId, out Observation? observed)) subject.Velocity = observed.Velocity;
            float desiredWeight = subject.IsPrimary ? 2f : subject.IsOpponent ? 1.1f : 1f;
            subject.Weight = Mathf.MoveTowards(subject.Weight, desiredWeight, dt * .8f);
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
        if (dt > 0 && TargetRoster.Finite(previousFocus) && Vector3.Distance(previousFocus, frame.Focus) <= 12f)
            frame.Focus = frame.PrimaryFocus + Vector3.ClampMagnitude(Vector3.Lerp(previousFocus, frame.Focus,
                1f - Mathf.Exp(-3f * dt)) - frame.PrimaryFocus, frame.Interior ? 2.5f : 6f);
        UpdateEncounterFrame();
        UpdateCompositionEvidence(player, now, dt);
        return frame;
    }

    private bool TryGroupHandoff(Player player, string character, float now)
    {
        if (!Valid(primaryActor) || Character.InInterior(player.transform.position) != frame.Interior ||
            frame.SceneId <= 0 || !Valid(player)) return false;
        bool member = false;
        foreach (CameraSceneSubject subject in frame.Subjects)
            if (subject.Actor == player && !subject.IsPrimary && !subject.IsOpponent && subject.CharacterId == character)
            { member = true; break; }
        if (!member) return false;
        mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Vector3 before = primaryActor!.transform.position, next = player.transform.position;
        bool preserved = grouping.TryReanchor(now, character, new AutonomyPosition(next.x, next.y, next.z),
            new AutonomyPosition(before.x, before.y, before.z), Connected(Focus(primaryActor), Focus(player)));
        if (preserved)
        {
            // Preserve the encounter, observers and member weights. Direction and
            // room measurements still rebind to the new primary without carrying
            // stale body-specific locomotion evidence.
            locomotion.Reset(); rooms.Reset(); surveyAt = -1; sampledFrame = -1;
        }
        return preserved;
    }

    private void Survey(TargetRoster.Target primary, float now, bool groupsEnabled)
    {
        Player player = primary.LoadedPlayer!;
        mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Observation primaryObservation = Observe(player, now);
        ZDO? primaryZdo = player.GetComponent<ZNetView>()?.GetZDO();
        Vector3 planarVelocity = primaryObservation.Velocity; planarVelocity.y = 0;
        bool swimming = primaryZdo != null && primaryZdo.IsValid() && primaryZdo.GetInt(SwimmingKey, 0) != 0;
        locomotion.Observe(now, planarVelocity.magnitude, primaryObservation.Velocity.y, swimming, rooms.SupportingShip);
        frame.Locomotion = locomotion.Locomotion; frame.LocomotionConfidence = locomotion.Confidence;
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
        var old = new Dictionary<string, CameraSceneSubject>(StringComparer.Ordinal);
        foreach (CameraSceneSubject subject in frame.Subjects) old[subject.CharacterId] = subject;
        frame.Subjects.Clear();
        Add(player, primary.Identity.Id, true, false, old);
        foreach (string id in members)
            if (observations.TryGetValue(id, out Observation? observation) && Valid(observation.Actor) && observation.Connection)
                Add(observation.Actor, id, false, false, old);

        bool attack = false, damaged = false;
        foreach (CameraSceneSubject subject in frame.Subjects)
            if (observations.TryGetValue(subject.CharacterId, out Observation? observation))
            { attack |= observation.AttackUntil > now; damaged |= observation.DamageUntil > now; }
        SurveyEncounter(player, now, attack, old);

        bool work = ObservedWork(player, primaryObservation, now, out string workEvidence,
            out Collider? resource, out Vector3 resourceFocus, out string resourceId);
        if (encounterActor && frame.Encounter != EncounterKind.None)
            ConsiderInterest("encounter:" + encounterActor.GetZDOID(),
                frame.Encounter == EncounterKind.Hunting ? "hunting" : frame.Encounter == EncounterKind.Threat ? "threat" : "opponent",
                Focus(encounterActor), frame.EncounterConfidence, encounterActor, null, now);
        else if (resource)
            ConsiderInterest(resourceId, "resource", resourceFocus, .65f, null, resource, now);
        else if (rooms.SupportingShip && rooms.SupportingCollider)
            ConsiderInterest("vessel:" + rooms.SupportingShip.GetInstanceID(), "vessel",
                rooms.SupportingShip.transform.position + Vector3.up * 1.5f, .85f, null, rooms.SupportingCollider, now);
        else pendingInterestId = "";

        CinematicActivity desired;
        string evidence;
        float confidence;
        if (frame.Encounter == EncounterKind.Combat) { desired = CinematicActivity.Combat; evidence = frame.EncounterEvidence; confidence = frame.EncounterConfidence; }
        else if (work) { desired = CinematicActivity.Work; evidence = workEvidence; confidence = .65f; }
        else if (primaryObservation.Velocity.magnitude > 1.2f || frame.Locomotion == ShotLocomotion.Vertical)
        { desired = CinematicActivity.Travel; evidence = frame.Locomotion == ShotLocomotion.Boat ? "movement on a supported vessel" :
            frame.Locomotion == ShotLocomotion.Swim ? "replicated swimming animation and movement" : "sustained observed movement"; confidence = .9f; }
        else { desired = CinematicActivity.Idle; evidence = damaged ? "recent health loss; cause unknown" : attack ? "action observed; activity unknown" : "stationary or slow movement"; confidence = attack || damaged ? .35f : .85f; }
        if (desired != pendingActivity) { pendingActivity = desired; activitySince = now; }
        if (desired == frame.Activity || now - activitySince >= (desired == CinematicActivity.Combat ? .5f : 1.5f)) frame.Activity = desired;
        frame.Confidence = desired == frame.Activity ? confidence : Mathf.Min(.4f, confidence);
        int players = 0;
        foreach (CameraSceneSubject subject in frame.Subjects) if (subject.Player) players++;
        string place = $"; {frame.Environment} {frame.EnvironmentConfidence:F2}; {frame.Locomotion}; scene {frame.SceneId}";
        frame.Description = $"{frame.Activity}; {players} player(s), {frame.EncounterOpponentCount} opponent(s); {evidence}{place}; encounter {frame.Encounter} ({frame.EncounterConfidence:F2}, {frame.EncounterDistance:F0}m); {frame.EncounterEvidence}" +
            (frame.HasInterest ? $"; {frame.InterestKind} interest {frame.InterestConfidence:F2}" : "");
        stale.Clear();
        foreach (KeyValuePair<string, Observation> pair in observations)
            if (!pair.Value.Actor || now - pair.Value.LastAt > 4) stale.Add(pair.Key);
        foreach (string id in stale) observations.Remove(id);
    }

    private void SurveyEncounter(Player player, float now, bool groupAttack,
        Dictionary<string, CameraSceneSubject> old)
    {
        opponents.Clear(); encounterObservations.Clear(); encounterActors.Clear();
        bool drawing = IsDrawingRanged(player);
        // Brief release follow-through requires an observed draw in this scene;
        // a stale drawpercent or merely holding a bow never initiates hunting.
        bool aiming = rangedAim.Observe(now, drawing, encounters.Selected.Kind);
        Vector3 forward = player.transform.forward; forward.y = 0;
        if (!TargetRoster.Finite(forward) || forward.sqrMagnitude < .01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 origin = Focus(player);
        // Cheap bounded shortlist before at most 128 structural raycasts. Sort
        // by aim relevance and proximity, never Character-list insertion order.
        foreach (Character actor in Character.GetAllCharacters())
        {
            if (!Valid(actor) || actor is Player || actor.IsTamed() ||
                Character.InInterior(actor.transform.position) != frame.Interior ||
                Vector3.Distance(origin, Focus(actor)) > 96f) continue;
            float priority = EncounterPriority(actor, origin, forward, aiming);
            int at = 0;
            while (at < encounterActors.Count && EncounterPriority(encounterActors[at], origin, forward, aiming) <= priority) at++;
            if (at >= EncounterPolicy.MaximumCandidates) continue;
            encounterActors.Insert(at, actor);
            if (encounterActors.Count > EncounterPolicy.MaximumCandidates) encounterActors.RemoveAt(encounterActors.Count - 1);
        }
        foreach (Character actor in encounterActors)
        {
            Vector3 focus = Focus(actor), delta = focus - origin;
            float distance = delta.magnitude, nearest = distance;
            Vector3 nearestFocus = origin;
            foreach (CameraSceneSubject subject in frame.Subjects)
            {
                float toMember = Vector3.Distance(subject.Focus, focus);
                if (toMember < nearest) { nearest = toMember; nearestFocus = subject.Focus; }
            }
            bool connected = nearest <= 40 && Connected(nearestFocus, focus);
            Vector3 flat = delta; flat.y = 0;
            float alignment = flat.sqrMagnitude > .01f ? Mathf.Clamp(Vector3.Dot(forward, flat.normalized), -1, 1) : -1;
            // Remote body orientation has no reliable pitch. Reject large
            // elevation ambiguity instead of inventing a reticle or aim ray.
            if (Mathf.Abs(delta.y) > Mathf.Max(5, distance * .2f)) alignment = -1;
            bool aimLine = aiming && alignment >= .985f && ClearHuntingLine(origin, focus);
            connected |= aimLine;
            Observation observed = Observe(actor, now);
            BaseAI? ai = actor.GetBaseAI(); ZNetView? view = actor.GetComponent<ZNetView>();
            Character? target = ai && view && view.IsOwner() ? ai.GetTargetCreature() : null;
            bool targetsGroup = false;
            foreach (CameraSceneSubject subject in frame.Subjects) if (target && target == subject.Actor) targetsGroup = true;
            bool hostile = BaseAI.IsEnemy(player, actor);
            float health = actor.GetMaxHealth();
            float playerHealth = player.GetMaxHealth(), height = actor.GetHeight();
            float relative = Finite(health) ? health / (Finite(playerHealth) ? Mathf.Max(100, playerHealth) : 100) : 0;
            float prominence = actor.IsBoss() ? 1 : Mathf.Clamp01(Mathf.Max(relative / 8,
                Mathf.Max(Finite(height) ? (height - 1.5f) / 5 : 0, (actor.GetLevel() - 1) * .3f)));
            bool attacking = observed.AttackUntil > now, damaged = observed.DamageUntil > now;
            if (hostile && connected && ObservedEncounterEvidence.SupportsCombat(targetsGroup, attacking, groupAttack, damaged, nearest))
                observed.OpponentUntil = now + 3;
            encounterObservations.Add(new EncounterObservation(actor.GetZDOID().ToString(), distance, nearest, alignment, prominence,
                connected, hostile, drawing || actor.GetZDOID().ToString() == encounters.Selected.Id, targetsGroup, attacking, damaged, observed.OpponentUntil > now, aimLine));
        }
        EncounterCandidate selected = encounters.Update(now, aiming, groupAttack, encounterObservations);
        ClearEncounter();
        if (selected.Kind == EncounterKind.None) return;
        foreach (Character actor in encounterActors)
            if (actor.GetZDOID().ToString() == selected.Id) { encounterActor = actor; break; }
        if (!encounterActor) return;
        frame.Encounter = selected.Kind; frame.EncounterConfidence = selected.Confidence; frame.EncounterTargetId = selected.Id;
        frame.EncounterEvidence = selected.Kind == EncounterKind.Hunting ? (drawing ? "replicated ranged draw; target inferred from body-facing cone" : "recent qualified ranged aim; shot outcome unknown") :
            selected.Kind == EncounterKind.Threat ? "nearby hostile prominence; danger inferred, aggression unconfirmed" :
            selected.Confidence > .9f ? "owner-observed opponent target belongs to group" : "nearby hostile action/damage; exact victim unknown";
        // Selected actor comes first, then ranked nearby engaged opponents.
        // Reserve their slots before companions so a group cannot hide its foe.
        var admitted = new List<Observation>(3);
        if (selected.Distance <= (frame.Interior ? 12 : 28) && observations.TryGetValue(selected.Id, out Observation? main)) admitted.Add(main);
        int enemies = 0;
        foreach (EncounterCandidate candidate in encounters.Ranked)
        {
            if (candidate.Kind == EncounterKind.Combat) enemies++;
            if (admitted.Count >= 3 || candidate.Id == selected.Id || candidate.Kind != EncounterKind.Combat ||
                candidate.Distance > (frame.Interior ? 12 : 28)) continue;
            if (observations.TryGetValue(candidate.Id, out Observation? observation)) admitted.Add(observation);
        }
        frame.EncounterOpponentCount = Mathf.Max(1, enemies);
        while (frame.Subjects.Count + admitted.Count > 6) frame.Subjects.RemoveAt(frame.Subjects.Count - 1);
        // Keep primary, then key opponent, then companions/other threats. This
        // order is shared by all bounded visibility/group readers.
        int insertAt = 1;
        foreach (Observation observation in admitted)
        {
            Add(observation.Actor, observation.Actor.GetZDOID().ToString(), false, true, old);
            CameraSceneSubject subject = frame.Subjects[frame.Subjects.Count - 1];
            frame.Subjects.RemoveAt(frame.Subjects.Count - 1); frame.Subjects.Insert(insertAt++, subject);
            opponents.Add(observation);
        }
        UpdateEncounterFrame();
    }

    private float EncounterPriority(Character actor, Vector3 origin, Vector3 forward, bool aiming)
    {
        Vector3 delta = Focus(actor) - origin; float distance = delta.magnitude; delta.y = 0;
        float priority = distance;
        if (actor == encounterActor) priority -= 200;
        if (aiming && delta.sqrMagnitude > .01f && Vector3.Dot(forward, delta.normalized) >= .985f) priority -= 100;
        return priority;
    }

    internal static bool IsDrawingRanged(Player player)
    {
        if (!Valid(player)) return false;
        ZDO? zdo = player.GetComponent<ZNetView>()?.GetZDO();
        return zdo != null && zdo.IsValid() && (Drawing(zdo, zdo.GetInt(ZDOVars.s_rightItem, 0)) ||
            Drawing(zdo, zdo.GetInt(ZDOVars.s_leftItem, 0)));
    }

    private static bool Drawing(ZDO zdo, int itemHash)
    {
        if (itemHash == 0 || !ObjectDB.instance) return false;
        GameObject? prefab = ObjectDB.instance.GetItemPrefab(itemHash);
        ItemDrop? item = prefab ? prefab.GetComponent<ItemDrop>() : null;
        string? draw = item?.m_itemData?.m_shared?.m_attack?.m_drawAnimationState;
        return !string.IsNullOrEmpty(draw) && draw!.Length <= 128 &&
            zdo.GetInt(unchecked(438569 + ZSyncAnimation.GetHash(draw)), 0) != 0;
    }

    private bool ClearHuntingLine(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .01f) return true;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        if (count >= hits.Length) return false;
        for (int i = 0; i < count; i++)
            if (hits[i].collider && !hits[i].collider.GetComponentInParent<Character>()) return false;
        return true;
    }

    private void UpdateEncounterFrame()
    {
        if (!Valid(encounterActor) || frame.Encounter == EncounterKind.None ||
            Character.InInterior(encounterActor!.transform.position) != frame.Interior ||
            Vector3.Distance(frame.PrimaryFocus, Focus(encounterActor)) > 96)
        { ClearEncounter(); return; }
        frame.EncounterFocus = Focus(encounterActor!);
        Vector3 delta = frame.EncounterFocus - frame.PrimaryFocus;
        frame.EncounterDistance = delta.magnitude; frame.EncounterDirection = delta.sqrMagnitude > .01f ? delta.normalized : Vector3.zero;
        float radius = encounterActor!.GetRadius(), height = encounterActor.GetHeight();
        frame.EncounterTargetRadius = Finite(radius) ? Mathf.Clamp(radius, .45f, 8) : .6f;
        frame.EncounterTargetHeight = Finite(height) ? Mathf.Clamp(height, .5f, 16) : 2;
        frame.EncounterPlayerCount = 0;
        foreach (CameraSceneSubject subject in frame.Subjects) if (subject.Player) frame.EncounterPlayerCount++;
    }

    private void ClearEncounter()
    {
        encounterActor = null; frame.Encounter = EncounterKind.None; frame.EncounterTargetId = ""; frame.EncounterEvidence = "none";
        frame.EncounterConfidence = frame.EncounterDistance = frame.EncounterTargetRadius = frame.EncounterTargetHeight = 0;
        frame.EncounterOpponentCount = frame.EncounterPlayerCount = 0;
        frame.EncounterFocus = frame.PrimaryFocus; frame.EncounterDirection = Vector3.zero;
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
            Vector3 measured = (position - item.Position) / dt;
            item.Velocity = Vector3.Lerp(item.Velocity, Vector3.ClampMagnitude(measured, 20f), 1f - Mathf.Exp(-3f * dt));
            if (health < item.Health - .1f) item.DamageUntil = now + 4f;
        }
        else if (dt > 1 || Vector3.Distance(item.Position, position) > 16f)
        { item.Velocity = Vector3.zero; item.DamageUntil = item.AttackUntil = item.OpponentUntil = 0; }
        if (actor.InAttack()) item.AttackUntil = now + 1.5f;
        item.Position = position; item.LastAt = now; item.Health = health;
        return item;
    }

    private bool ObservedWork(Player player, Observation observation, float now, out string evidence,
        out Collider? resourceCollider, out Vector3 resourceFocus, out string resourceId)
    {
        resourceCollider = null; resourceFocus = Vector3.zero; resourceId = "";
        // These two APIs read local fields, not replicated remote-player state.
        if (player == Player.m_localPlayer && (player.InPlaceMode() || player.GetCurrentCraftingStation()))
        { evidence = "local building/crafting interface"; return true; }
        ZDO? zdo = player.GetComponent<ZNetView>()?.GetZDO();
        if (zdo != null && zdo.IsValid() && zdo.GetInt(CraftingKey, 0) != 0)
        { evidence = "replicated crafting animation; exact task unknown"; return true; }
        Vector3 facing = player.transform.forward;
        if (observation.AttackUntil > now && TargetRoster.Finite(facing) && facing.sqrMagnitude > .001f)
        {
            Vector3 start = player.transform.position + Vector3.up;
            int count = Physics.RaycastNonAlloc(start, facing.normalized, hits, 4f, mask, QueryTriggerInteraction.Ignore);
            // A saturated unordered hit buffer cannot establish the nearest hit.
            if (count >= hits.Length) { evidence = ""; return false; }
            float nearest = float.MaxValue; Collider? first = null;
            Vector3 nearestPoint = Vector3.zero;
            for (int i = 0; i < count; i++)
                if (hits[i].collider && !hits[i].collider.GetComponentInParent<Character>() &&
                    Finite(hits[i].distance) && TargetRoster.Finite(hits[i].point) && hits[i].distance < nearest)
                { nearest = hits[i].distance; first = hits[i].collider; nearestPoint = hits[i].point; }
            Component? resource = first ? first.GetComponentInParent<TreeBase>() : null;
            if (!resource && first) resource = first.GetComponentInParent<TreeLog>();
            if (!resource && first) resource = first.GetComponentInParent<MineRock>();
            if (!resource && first) resource = first.GetComponentInParent<MineRock5>();
            if (resource && Vector3.Distance(start, nearestPoint) <= 4.1f)
            {
                resourceCollider = first; resourceFocus = nearestPoint; resourceId = "resource:" + resource.GetInstanceID();
                evidence = "resource-facing attack; gathering inferred"; return true;
            }
        }
        evidence = ""; return false;
    }

    private void ConsiderInterest(string id, string kind, Vector3 point, float strength,
        Character? actor, Collider? resource, float now)
    {
        if (!TargetRoster.Finite(point) || Vector3.Distance(frame.PrimaryFocus, point) > (actor ? 96f : 24f)) return;
        if (id != interestId)
        {
            if (pendingInterestId != id) { pendingInterestId = id; interestSince = now; return; }
            if (now - interestSince < (kind == "resource" ? .75f : .5f)) return;
        }
        interestId = id; interestKind = kind; interestPoint = point; interestActor = actor; interestResource = resource;
        interestStrength = Mathf.Clamp01(strength); interestSeenAt = now; pendingInterestId = "";
    }

    private void UpdateCompositionEvidence(Player player, float now, float dt)
    {
        Vector3 observedFacing = frame.Locomotion == ShotLocomotion.Boat && rooms.SupportingShip
            ? rooms.SupportingShip.transform.forward : player.transform.forward;
        observedFacing.y = 0;
        if (TargetRoster.Finite(observedFacing) && observedFacing.sqrMagnitude > .001f)
        {
            observedFacing.Normalize();
            if (!facingInitialized) { frame.FacingDirection = observedFacing; facingInitialized = true; }
            float before = Mathf.Atan2(frame.FacingDirection.x, frame.FacingDirection.z) * Mathf.Rad2Deg;
            float after = Mathf.Atan2(observedFacing.x, observedFacing.z) * Mathf.Rad2Deg;
            float difference = Mathf.DeltaAngle(before, after);
            float turn = Mathf.Clamp(difference * (1f - Mathf.Exp(-2f * dt)), -45f * dt, 45f * dt);
            float radians = (before + turn) * Mathf.Deg2Rad;
            frame.FacingDirection = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
            // Large body turns temporarily weaken lead-room evidence instead of
            // swinging the camera immediately to the newly observed heading.
            float confidence = Mathf.Abs(difference) > 75f ? .2f : .7f;
            frame.FacingConfidence = Mathf.MoveTowards(frame.FacingConfidence, confidence, dt * .8f);
        }
        else frame.FacingConfidence = Mathf.MoveTowards(frame.FacingConfidence, 0, dt);

        bool fresh = interestSeenAt >= 0 && now - interestSeenAt <= 1.25f;
        if (interestActor)
        {
            fresh &= Valid(interestActor);
            if (fresh) interestPoint = Focus(interestActor);
        }
        else if (interestKind == "opponent" || interestKind == "hunting" || interestKind == "threat") fresh = false;
        if ((interestKind == "resource" || interestKind == "vessel") && (!interestResource || !interestResource.enabled ||
            !interestResource.gameObject.activeInHierarchy)) fresh = false;
        if (interestKind == "vessel" && fresh)
        {
            Ship? ship = interestResource ? interestResource.GetComponentInParent<Ship>() : null;
            fresh = ship && ship == rooms.SupportingShip;
            if (fresh) interestPoint = ship!.transform.position + Vector3.up * 1.5f;
        }
        fresh &= TargetRoster.Finite(interestPoint) && Vector3.Distance(frame.PrimaryFocus, interestPoint) <= (interestActor ? 96f : 24f);
        if (interestKind == "opponent" || interestKind == "hunting" || interestKind == "threat")
            fresh &= frame.Encounter != EncounterKind.None && interestActor == encounterActor;
        float desired = fresh ? interestStrength : 0;
        frame.InterestConfidence = Mathf.MoveTowards(frame.InterestConfidence, desired, dt * (fresh ? .8f : 1.2f));
        if (fresh)
        {
            if (frame.InterestConfidence <= .001f || !frame.HasInterest) frame.InterestFocus = interestPoint;
            else frame.InterestFocus = Vector3.Lerp(frame.InterestFocus, interestPoint, 1f - Mathf.Exp(-2f * dt));
            frame.InterestFocus = frame.PrimaryFocus + Vector3.ClampMagnitude(frame.InterestFocus - frame.PrimaryFocus, 24f);
        }
        // Removed/dead actors and expired points are never presented as live
        // interests, even while confidence decays for a later clean transition.
        frame.HasInterest = fresh && frame.InterestConfidence >= .15f;
        frame.InterestKind = frame.HasInterest ? interestKind : "none";
        if (!frame.HasInterest && frame.InterestConfidence <= .001f) frame.InterestFocus = frame.PrimaryFocus;
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

    private void Add(Character actor, string id, bool primary, bool opponent, Dictionary<string, CameraSceneSubject> old)
    {
        string character = actor.GetZDOID().ToString();
        if (!old.TryGetValue(character, out CameraSceneSubject? subject) || subject.Actor != actor)
            subject = new CameraSceneSubject { Actor = actor, Weight = primary ? 2f : .05f };
        subject.Id = id; subject.CharacterId = character; subject.Player = actor as Player;
        subject.IsPrimary = primary; subject.IsOpponent = opponent; subject.Focus = Focus(actor);
        float radius = actor.GetRadius();
        subject.Radius = actor is Player ? .6f : Finite(radius) ? Mathf.Clamp(radius, .45f, 8f) : .6f;
        float height = actor.GetHeight();
        subject.Height = Finite(height) ? Mathf.Clamp(height, .5f, actor is Player ? 8f : 16f) : 2f;
        subject.BottomOffset = subject.Focus.y - actor.transform.position.y;
        frame.Subjects.Add(subject);
    }

    private static Vector3 Focus(Character actor)
    {
        float height = actor.GetHeight();
        float offset = actor is Player ? 1.5f : Finite(height) ? Mathf.Clamp(height * .6f, .5f, 4f) : 1.2f;
        return actor.transform.position + Vector3.up * offset;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Valid(Character? actor) => actor && actor.gameObject.activeInHierarchy && !actor.IsDead() &&
        !actor.GetZDOID().IsNone() && TargetRoster.Finite(actor.transform.position) &&
        Mathf.Abs(actor.transform.position.x) <= 20000 && Mathf.Abs(actor.transform.position.y) <= 20000 && Mathf.Abs(actor.transform.position.z) <= 20000;

    internal void Reset()
    {
        grouping.Reset(); encounters.Reset(); encounterObservations.Clear(); encounterActors.Clear(); ClearEncounter(); rangedAim.Reset(); observations.Clear(); companions.Clear(); stale.Clear(); opponents.Clear(); frame.Subjects.Clear();
        rooms.Reset(); locomotion.Reset(); frame.SceneId = 0;
        frame.Environment = ShotEnvironment.Unknown; frame.Locomotion = ShotLocomotion.Stationary;
        frame.EnvironmentConfidence = frame.LocomotionConfidence = frame.CeilingHeight = 0;
        frame.DoorwayTransition = false; frame.OpeningDirection = Vector3.zero;
        frame.PrimaryFocus = frame.Focus = frame.Velocity = frame.GroupVelocity = Vector3.zero;
        frame.FacingDirection = Vector3.forward; frame.InterestFocus = Vector3.zero;
        frame.FacingConfidence = frame.InterestConfidence = 0; frame.HasInterest = false; frame.InterestKind = "none";
        interestId = pendingInterestId = ""; interestKind = "none"; interestActor = null; interestResource = null;
        interestPoint = Vector3.zero; interestSince = interestStrength = 0; interestSeenAt = -1; facingInitialized = false;
        frame.Spread = frame.Confidence = 0; frame.Interior = false; frame.Activity = pendingActivity = CinematicActivity.Idle;
        frame.Description = "Awaiting loaded player context";
        primaryActor = null; primaryId = primaryCharacter = ""; world = null;
        surveyAt = frameAt = -1; activitySince = 0; sampledFrame = -1;
    }
}
