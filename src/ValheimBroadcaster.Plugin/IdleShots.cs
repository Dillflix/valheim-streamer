using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Creature discovery stays separate from the dedicated environmental director.
internal sealed class IdleShots
{
    private const int MaximumCreatures = 32;
    private readonly Dictionary<string, Character> creatures = new Dictionary<string, Character>(StringComparer.Ordinal);
    private readonly List<string> readyIds = new List<string>(MaximumCreatures);
    private readonly List<CreatureCandidate> nearby = new List<CreatureCandidate>(MaximumCreatures);
    private readonly ScenicProduction scenic = new ScenicProduction();
    private float radius = 50;
    private Vector3 creatureOrigin;
    internal bool JourneyActive => scenic.JourneyActive;
    internal Vector3? JourneyLoadingPoint => scenic.JourneyLoadingPoint;
    internal string JourneyStatus => scenic.JourneyStatus;
    internal string RequestJourney() => scenic.RequestJourney();
    internal void StopJourney() => scenic.StopJourney();
    internal long FixtureOwnerId => scenic.FixtureOwnerId;
    internal bool FixturePreparing => scenic.FixturePreparing;
    internal bool FixtureActive => scenic.FixtureActive;
    internal string FixtureStatus => scenic.FixtureStatus;
    internal bool RequestFixture(ScenicShotPlan plan, out string reason, float playbackMotion = 1, bool playbackDynamic = true) =>
        scenic.RequestFixture(plan, out reason, playbackMotion, playbackDynamic);
    internal void StopFixture() => scenic.StopFixture();
    internal bool TryGetFixturePlayback(long planId, out float motion, out bool dynamic) => scenic.TryGetFixturePlayback(planId, out motion, out dynamic);
    internal bool Searching => scenic.Searching;
    internal bool NeedsReplacement => scenic.NeedsReplacement;
    internal bool RecoveryExhausted => scenic.RecoveryExhausted;
    internal bool RecoveryExpired => scenic.RecoveryExpired;
    internal bool HasUsableScenicPicture => scenic.HasUsablePicture;
    internal bool ContentExhausted => scenic.ContentExhausted;
    internal bool RelocationBoundary => scenic.RelocationBoundary;
    internal bool DestinationSubjectShown => scenic.DestinationSubjectShown;
    internal bool DestinationSubjectSatisfied => scenic.DestinationSubjectSatisfied;
    internal bool DestinationSubjectUnavailable => scenic.DestinationSubjectUnavailable;
    internal string DestinationSubjectStatus => scenic.DestinationSubjectStatus;
    internal void SetDestinationSubject(WorldInterestSite? site) => scenic.SetDestinationSubject(site);
    internal ScenicVisibility Visibility => scenic.Visibility;
    internal ScenicShotPlan? Plan => scenic.Plan;
    internal long PlanId => Plan?.Id ?? 0;
    internal Vector3 ScenicOffset => scenic.Offset;
    internal int RejectedViews => scenic.RejectedViews;
    internal string ViewReason => scenic.Reason;
    internal string ScenicSearchStatus => scenic.SearchStatus;
    internal string Diagnostics => scenic.Diagnostics + "; loaded creatures " + creatures.Count;
    internal bool HasRegion => scenic.HasRegion;
    internal bool HasScenicPath => Plan != null;
    internal bool CanContinue => scenic.CanContinue;
    internal bool SceneReady => scenic.SceneReady;

    private readonly struct CreatureCandidate
    {
        internal readonly string Id;
        internal readonly Character Actor;
        internal readonly float Distance;
        internal CreatureCandidate(string id, Character actor, float distance) { Id = id; Actor = actor; Distance = distance; }
    }
    internal void SetRegion(Vector3 origin)
    { creatures.Clear(); readyIds.Clear(); nearby.Clear(); creatureOrigin = origin; scenic.SetRegion(origin); }
    internal void Clear()
    { creatures.Clear(); readyIds.Clear(); nearby.Clear(); creatureOrigin = default; scenic.Clear(); }
    internal void BeginScenicPath(bool resume = false)
    { if (resume) scenic.ResumeShot(); else scenic.RequestShot(); }
    internal float ProposeScenic(float dt, BroadcastSettings settings, bool held) => scenic.Propose(dt, settings, held);
    internal bool Search(BroadcastSettings settings, bool moveCarrier, bool held, bool allowActivate = true) =>
        scenic.TickSearch(settings, moveCarrier, held, allowActivate);
    internal Vector3 ScenicFocus(float fraction) => scenic.Focus(fraction);
    internal bool ViewClear(Vector3 camera, Vector3 focus, float fov) => scenic.Visibility.Clear(camera, focus, fov, out _);
    internal bool ReportPresentation(bool ready, string reason, bool usable, long planId, float progress, bool blocked, bool held, Vector3? actualFocus, Vector3? actualCamera = null) =>
        scenic.Observe(ready, usable, planId, progress, blocked, held, reason, actualFocus, actualCamera);
    internal void UpdateRecovery(bool onAir, bool usableLive, long planId, bool held) =>
        scenic.UpdateRecovery(onAir, usableLive, planId, held);
    internal bool ObservePixelConcern(long planId, bool reconsider, string reason) =>
        scenic.ObservePixelConcern(planId, reconsider, reason);

    internal void Refresh(float searchRadius, Vector3? creatureSearchOrigin = null)
    {
        radius = searchRadius;
        creatures.Clear(); nearby.Clear();
        // A wildlife cutaway searches the selected player's already loaded area.
        // It must not relocate the scenic survey or erase its paths/history.
        creatureOrigin = creatureSearchOrigin ?? scenic.RegionOrigin;
        if (creatureSearchOrigin.HasValue ? !Ready(creatureOrigin) : !SceneReady) return;
        foreach (Character actor in Character.GetAllCharacters())
        {
            if (!Eligible(actor) || actor.GetZDOID().IsNone()) continue;
            string id = actor.GetZDOID().ToString();
            var candidate = new CreatureCandidate(id, actor, (actor.transform.position - creatureOrigin).sqrMagnitude);
            int insert = 0;
            while (insert < nearby.Count && (nearby[insert].Distance < candidate.Distance ||
                nearby[insert].Distance == candidate.Distance && StringComparer.Ordinal.Compare(nearby[insert].Id, id) < 0)) insert++;
            if (insert >= MaximumCreatures) continue;
            nearby.Insert(insert, candidate);
            if (nearby.Count > MaximumCreatures) nearby.RemoveAt(MaximumCreatures);
        }
        foreach (CreatureCandidate candidate in nearby) creatures[candidate.Id] = candidate.Actor;
    }

    internal IReadOnlyList<string> ReadyIds()
    {
        readyIds.Clear();
        foreach (var entry in creatures) if (Eligible(entry.Value, false)) readyIds.Add(entry.Key);
        readyIds.Sort(StringComparer.Ordinal);
        return readyIds;
    }
    internal Character? Find(string? id) => id != null && creatures.TryGetValue(id, out Character? actor) && Eligible(actor) ? actor : null;
    private bool Eligible(Character actor, bool checkScene = true) => actor && actor.isActiveAndEnabled && !actor.IsPlayer() && !actor.IsDead() &&
        TargetRoster.Finite(actor.transform.position) && (actor.transform.position - creatureOrigin).sqrMagnitude <= radius * radius && (!checkScene || Ready(actor.transform.position));
    internal static bool Ready(Vector3 position) => TargetRoster.Finite(position) && ZNetScene.instance && ZNetScene.instance.IsAreaReady(position);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
}
