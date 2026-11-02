using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Region discovery owns places and subjects; the shared Cinemachine rig owns
// presentation. The survey origin only bounds loaded-region searches.
internal sealed class IdleShots
{
    private const int MaximumCreatures = 32, SearchBudget = 72, MinimumSearch = 18;
    private readonly Dictionary<string, Character> creatures = new Dictionary<string, Character>(StringComparer.Ordinal);
    private readonly List<string> readyIds = new List<string>(MaximumCreatures);
    private readonly List<CreatureCandidate> nearby = new List<CreatureCandidate>(MaximumCreatures);
    private readonly Queue<ShownPlace> recentPlaces = new Queue<ShownPlace>(12);
    private readonly ScenicVisibility visibility = new ScenicVisibility();
    private float radius = 50;
    private Vector3 surveyOrigin, creatureOrigin, selectedPlace, bestPlace;
    private int sample, attemptsLeft, attemptsMade, viablePlans;
    private long nextPlanId;
    private ScenicShotPlan? bestPlan;
    private float bestScore, lastFraction, shownSeconds, presentationAt = -1, blockedSince = -1, nextViewCheck;
    private bool recorded, previousUsable, lastViewClear;
    internal bool Searching => attemptsLeft > 0;
    internal bool NeedsReplacement { get; private set; }
    internal ScenicVisibility Visibility => visibility;
    internal ScenicShotPlan? Plan { get; private set; }
    internal long PlanId => Plan?.Id ?? 0;
    internal Vector3 ScenicOffset
    {
        get
        {
            if (Plan == null) return Vector3.zero;
            ScenicShotSample point = Plan.Sample(lastFraction);
            return FromCore(point.CameraPosition) - FromCore(point.AimPosition);
        }
    }
    internal int RejectedViews { get; private set; }
    internal string ViewReason { get; private set; } = "No scenic shot";
    internal string Diagnostics => "Scenic view: " + ViewReason + "; style " + (Plan?.Style.ToString() ?? "none") +
        "; path " + PlanId + "; progress " + (lastFraction * 100).ToString("F0") + "%" +
        "; searching " + Searching + "; viable " + viablePlans + "; rejected " + RejectedViews +
        "; shown places " + recentPlaces.Count + "; loaded creatures " + creatures.Count;
    internal bool HasRegion { get; private set; }
    internal bool HasScenicPath => Plan != null;
    internal bool SceneReady => HasRegion && Ready(surveyOrigin);

    private readonly struct ShownPlace
    {
        internal readonly Vector3 Position;
        internal readonly ScenicShotStyle Style;
        internal ShownPlace(Vector3 position, ScenicShotStyle style) { Position = position; Style = style; }
    }
    private readonly struct CreatureCandidate
    {
        internal readonly string Id;
        internal readonly Character Actor;
        internal readonly float Distance;
        internal CreatureCandidate(string id, Character actor, float distance) { Id = id; Actor = actor; Distance = distance; }
    }

    internal void SetRegion(Vector3 origin) { Clear(); surveyOrigin = creatureOrigin = origin; HasRegion = TargetRoster.Finite(origin); }
    internal void Clear()
    {
        creatures.Clear(); readyIds.Clear(); nearby.Clear(); recentPlaces.Clear();
        creatureOrigin = default;
        HasRegion = NeedsReplacement = false; Plan = bestPlan = null;
        sample = attemptsLeft = attemptsMade = viablePlans = 0;
        blockedSince = presentationAt = -1; nextViewCheck = lastFraction = shownSeconds = 0;
        lastViewClear = recorded = previousUsable = false; RejectedViews = 0;
        ViewReason = "No scenic shot"; visibility.Reset();
        // IDs remain unique across region resets, so stale render proof cannot
        // credit a replacement with the same numeric identifier.
    }

    internal void BeginScenicPath()
    {
        NeedsReplacement = true;
        if (Searching) return;
        attemptsLeft = SceneReady ? SearchBudget : 0;
        attemptsMade = viablePlans = 0; bestPlan = null; bestScore = -1;
        ViewReason = HasScenicPath ? "Retaining the current scene while seeking fresh coverage" : "Searching scenic compositions";
    }

    // A retained plan must not rewind when CoverageDirector begins the next
    // scenic interval before its replacement has passed validation.
    internal float PlanFraction(double elapsed, double duration)
    {
        if (!Searching && !NeedsReplacement)
            lastFraction = (float)Math.Max(0, Math.Min(1, duration > 0 ? elapsed / duration : 0));
        return lastFraction;
    }

    // Rank a bounded batch spread over updates. All terrain and actor queries are
    // read-only and restricted to already loaded content.
    internal bool Search(BroadcastSettings settings)
    {
        if (!Searching) return false;
        if (!SceneReady) { attemptsLeft = 0; ViewReason = "Scenic region is not ready"; return false; }
        visibility.Refresh(Player.m_localPlayer ? Player.m_localPlayer.transform.position : surveyOrigin);
        Camera? rendering = Camera.main;
        float aspect = rendering ? rendering.aspect : Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        float nearClip = rendering ? rendering.nearClipPlane : 0.3f;
        for (int attempt = 0; attempt < 3 && attemptsLeft > 0; attempt++)
        {
            attemptsLeft--; attemptsMade++;
            int index = sample++;
            if (sample > 600000) sample = 0;
            int place = index / 6, view = index % 6;
            float angle = place * 137.50776f * Mathf.Deg2Rad;
            float distance = 18f + place % 5 * 8f;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            if (!Ground(surveyOrigin + direction * distance, out Vector3 start)) continue;
            bool recent = false;
            foreach (ShownPlace previous in recentPlaces)
                if ((previous.Position - start).sqrMagnitude < 144f) { recent = true; break; }
            if (recent) continue;
            Vector3 tangent = new Vector3(direction.z, 0, -direction.x);
            if (!Ground(start + tangent * 12f, out Vector3 end) || Mathf.Abs(end.y - start.y) > 8f ||
                !Ground((start + end) * 0.5f, out Vector3 middle) || Mathf.Abs(middle.y - (start.y + end.y) * 0.5f) > 3f) continue;
            float heading = (settings.Azimuth.Value + view * 60f) * Mathf.Deg2Rad;
            float height = Mathf.Clamp(settings.Height.Value + (view % 3) * 4f, 3f, 45f);
            float radiusScale = view < 3 ? 1f : 1.25f;
            float distanceFromFeature = Mathf.Clamp(settings.Distance.Value * radiusScale, 5f, 70f);
            Vector3 offset = new Vector3(Mathf.Sin(heading) * distanceFromFeature, height, Mathf.Cos(heading) * distanceFromFeature);
            ScenicShotStyle style = (ScenicShotStyle)((place + view) % 5);
            float screenX = view % 2 == 0 ? -0.12f : 0.12f;
            // Ground features below center leave room for the surrounding terrain
            // and sky; this is a landscape guide, not humanoid headroom.
            float screenY = style == ScenicShotStyle.Approach ? -0.04f : -0.1f;
            var candidate = new ScenicShotPlan(nextPlanId + 1, style, ToCore(start + Vector3.up * 1.5f),
                ToCore(end + Vector3.up * 1.5f), ToCore(offset), screenX, screenY);
            if (!EvaluatePath(candidate, settings, aspect, nearClip, out float score, out string reason))
            { RejectedViews++; ViewReason = reason; continue; }
            viablePlans++;
            // Measured terrain relief supplies a modest interest preference. It
            // cannot make an obscured or physically invalid view win.
            score += Mathf.Clamp01((Mathf.Abs(end.y - start.y) + Mathf.Abs(middle.y - start.y)) / 12f) * 0.04f;
            int styleCount = 0;
            foreach (ShownPlace previous in recentPlaces) if (previous.Style == style) styleCount++;
            score -= Mathf.Min(0.12f, styleCount * 0.025f);
            if (score > bestScore) { bestPlan = candidate; bestScore = score; bestPlace = start; }
        }
        // At least three locations/six views enter the competition. A good
        // candidate need not wait through all 72 probes in a dense region.
        bool enough = attemptsMade >= MinimumSearch && viablePlans >= 3 && bestScore >= 0.5f;
        if (!enough && attemptsLeft > 0) return false;
        if (bestPlan == null)
        {
            attemptsLeft = 0; ViewReason = "No fresh physically supported scenic path in this loaded region";
            return false;
        }
        Plan = bestPlan; selectedPlace = bestPlace; nextPlanId = Plan.Id;
        bestPlan = null; attemptsLeft = 0; NeedsReplacement = false;
        lastFraction = shownSeconds = 0; recorded = previousUsable = false; presentationAt = -1;
        blockedSince = -1; nextViewCheck = 0; lastViewClear = true;
        ViewReason = "Selected " + Plan.Style + " scenic path from " + viablePlans + " supported candidates";
        return true;
    }

    private bool EvaluatePath(ScenicShotPlan candidate, BroadcastSettings settings, float aspect, float nearClip,
        out float score, out string reason)
    {
        score = 0; reason = "Scenic path has unsupported geometry";
        Vector3 previous = default;
        float worst = 1f, average = 0;
        // Validate the full allowed movement envelope so changing motion strength
        // while the shot runs cannot enter a path that was never examined.
        for (int point = 0; point < 9; point++)
        {
            ScenicShotSample sample = candidate.Sample(point / 8d, 1.5f, true);
            Vector3 camera = FromCore(sample.CameraPosition), focus = FromCore(sample.AimPosition);
            if (!Ready(camera) || !Ready(focus) || !Ready(camera + Vector3.down * 2f) ||
                Vector3.ProjectOnPlane(focus - camera, Vector3.up).sqrMagnitude < 1f ||
                Player.m_localPlayer && Vector3.Distance(Player.m_localPlayer.transform.position, camera + Vector3.down * 2f) >= 91f)
                return false;
            if (point > 0 && !visibility.SegmentClear(previous, camera))
            { reason = "Scenic path crosses solid geometry"; return false; }
            Quaternion rotation = ScenicVisibility.Compose(camera, focus,
                settings.DeliberateComposition.Value ? sample.ScreenX : 0,
                settings.DeliberateComposition.Value ? sample.ScreenY : 0, settings.FieldOfView.Value, aspect);
            ScenicVisibility.Result result = visibility.Evaluate(camera, rotation, focus, settings.FieldOfView.Value, aspect, nearClip);
            if (!result.CameraClear) { reason = result.Reason; return false; }
            average += result.Score / 9f;
            worst = Mathf.Min(worst, result.Score);
            previous = camera;
        }
        // One branch-filled interval cannot hide inside a high path average.
        // This ranks picture quality; even poor but physically safe coverage is
        // retained as a fallback when the loaded region offers nothing better.
        score = worst * 0.55f + average * 0.45f;
        reason = "Supported scenic path";
        return true;
    }

    internal Vector3 ScenicFocus(float fraction)
    {
        if (!Searching && !NeedsReplacement) lastFraction = Mathf.Clamp01(fraction);
        return Plan == null ? surveyOrigin : FromCore(Plan.Sample(lastFraction).AimPosition);
    }
    internal bool ViewClear(Vector3 camera, Vector3 focus, float fov)
    {
        if (Time.unscaledTime < nextViewCheck) return lastViewClear;
        nextViewCheck = Time.unscaledTime + 0.25f;
        visibility.Refresh(camera);
        lastViewClear = visibility.Clear(camera, focus, fov, out string reason);
        ViewReason = lastViewClear ? "Readable scenic composition" : reason;
        return lastViewClear;
    }

    // Called with proof from the preceding actual live render, not merely a
    // solver success or held framebuffer. The caller also matches PlanId.
    internal bool ReportPresentation(bool ready, string reason, bool usable = true)
    {
        if (Plan == null) return false;
        float now = Time.unscaledTime;
        float dt = presentationAt >= 0 && now >= presentationAt && now - presentationAt <= 0.25f ? now - presentationAt : 0;
        presentationAt = now;
        if (ready && usable)
        {
            if (previousUsable && !recorded) shownSeconds += dt;
            previousUsable = true; blockedSince = -1;
            if (!recorded && shownSeconds >= 2f)
            {
                recentPlaces.Enqueue(new ShownPlace(selectedPlace, Plan.Style));
                if (recentPlaces.Count > 12) recentPlaces.Dequeue();
                recorded = true;
            }
            return false;
        }
        previousUsable = false;
        if (!recorded) shownSeconds = 0;
        ViewReason = reason;
        if (blockedSince < 0) blockedSince = now;
        // Start seeking before the compositor's 0.75s hold expires. Keep the
        // current plan available: poor foliage quality is not scene unavailability.
        if (now - blockedSince < 0.4f || Searching) return false;
        NeedsReplacement = true;
        return true;
    }

    private static bool Ground(Vector3 point, out Vector3 ground)
    {
        ground = default;
        if (!Ready(point) || !Physics.Raycast(point + Vector3.up * 80f, Vector3.down, out RaycastHit hit,
                240f, LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) return false;
        ground = hit.point;
        return Ready(ground);
    }

    internal void Refresh(float searchRadius, Vector3? creatureSearchOrigin = null)
    {
        radius = searchRadius;
        creatures.Clear(); nearby.Clear();
        // A wildlife cutaway searches the selected player's already loaded area.
        // It must not relocate the scenic survey or erase its paths/history.
        creatureOrigin = creatureSearchOrigin ?? surveyOrigin;
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
