using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// A rolling future corridor for an already displayed orbital actor shot.
// The only output is a small correction to its world follow helper. Cinemachine
// remains the sole Body/Aim owner and the rig validates the actual solved pose.
internal sealed class ActorSpatialPlanner
{
    private readonly ActorSpatialHorizon horizon = new ActorSpatialHorizon();
    private readonly ActorSpatialGuidance guidance = new ActorSpatialGuidance();
    private readonly AutonomyPosition[] nominal = new AutonomyPosition[ActorSpatialHorizon.Layers];
    private readonly List<PlayerShotVisibility.Subject> observed = new List<PlayerShotVisibility.Subject>(PlayerShotVisibility.MaximumSubjects);
    private readonly List<PlayerShotVisibility.Subject> predicted = new List<PlayerShotVisibility.Subject>(PlayerShotVisibility.MaximumSubjects);
    private readonly Vector3[] velocities = new Vector3[PlayerShotVisibility.MaximumSubjects];
    private readonly string[] subjectIds = new string[PlayerShotVisibility.MaximumSubjects];
    private Vector3 camera, focus, poseFocus, sceneVelocity, priorVelocity, priorFocus;
    private Quaternion rotation;
    private AutonomyPosition goal;
    private float fov, aspect, near, confidence, nextPlan, goalUntil, observedAt = -1, coherentSeconds;
    private float pendingAt = -1, started, forecastSeconds;
    private int pendingProbe = -1;
    private long sceneId, epoch;
    private string? sourceBuilding, sourceRoom;
    private bool sourceInterior, sourceDoorway;
    private float affordableHorizon = 4.5f;
    internal Vector3 Offset => FromCore(guidance.Offset);
    internal Vector3 Velocity => FromCore(guidance.Velocity);
    internal string Diagnostics { get; private set; } = "spatial horizon awaiting a displayed actor shot";

    internal void Update(float now, float dt, CameraSceneFrame context,
        IReadOnlyList<PlayerShotVisibility.Subject> subjects, ShotTrajectoryPolicy trajectory,
        Vector3 displayedPosition, Quaternion displayedRotation, Vector3 displayedFocus, Vector3 displayedAnchor,
        Vector3 currentFocus, float currentFov, float currentAspect, float nearClip, float freedom,
        PlayerShotVisibility visibility, NativeRoomConfiner? confiner, bool enabled)
    {
        if (!enabled || subjects.Count == 0 || subjects.Count > velocities.Length || subjects.Count > context.Subjects.Count ||
            float.IsNaN(context.Confidence) || context.Confidence < .45f || context.Confidence > 1 ||
            !TargetRoster.Finite(context.Velocity))
        { Suspend("spatial prediction paused; retaining the validated actor shot"); return; }
        for (int i = 0; i < subjects.Count; i++)
            if (FindSubject(context, subjects[i]) is not CameraSceneSubject member ||
                !TargetRoster.Finite(member.Velocity) || !TargetRoster.Finite(subjects[i].Focus))
            { Suspend("spatial subject motion evidence unavailable; retaining the validated actor shot"); return; }
        ObserveMotion(now, context, currentFocus);
        CameraSpatialScene scene = CameraSpatialScene.Shared;
        PlaceSurvey.Shared.Update(currentFocus, now);
        PlaceNode? place = PlaceSurvey.Shared.FindContext(currentFocus, now);
        long currentEpoch = scene.Epoch;
        // Spatial extent is context, never proof of free space. It limits how
        // far avoidance may depart from a measured room or narrow opening.
        float available = Mathf.Clamp01(freedom);
        if (context.Interior || context.Environment == ShotEnvironment.Room) available = Mathf.Min(available, .3f);
        if (context.DoorwayTransition || place?.Kind == PlaceKind.Opening) available = Mathf.Min(available, .15f);
        if (place?.Kind == PlaceKind.Room)
            available = Mathf.Min(available, Mathf.Clamp01((Mathf.Min(place.Bounds.Width, place.Bounds.Depth) - 2f) / 12f));
        if ((horizon.Active || now < goalUntil) && (Mathf.Abs(currentAspect - aspect) > .01f || Mathf.Abs(nearClip - near) > .01f ||
            !SnapshotMatches(context, subjects, currentFocus, currentFov, now, currentEpoch)))
            InvalidateForecast("spatial forecast changed; retaining current anchor while replanning");

        if (!horizon.Active && now >= nextPlan)
        {
            Begin(now, currentEpoch, context, subjects, trajectory, displayedPosition, displayedRotation,
                displayedFocus, displayedAnchor, currentFocus, currentFov, currentAspect, nearClip, available, place);
        }
        int work = 0;
        int frameProbes = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(dt, 1f / 60) * 120), 3, 8);
        while (work++ < frameProbes && horizon.TryProbe(now, currentEpoch, out ActorSpatialProbe probe))
        {
            Vector3 from = FromCore(probe.From), to = FromCore(probe.Position);
            Quaternion fromRotation = RotationAt(from, (float)probe.FromSeconds);
            Quaternion toRotation = RotationAt(to, (float)probe.Seconds);
            if (confiner != null && !confiner.Contains(to))
            { horizon.Observe(true, false, 0); continue; }
            CameraSpaceObservation physical = scene.ObserveSegment(from, fromRotation, to, toRotation,
                fov, aspect, near, CameraSpaceUse.Actor, false, CameraQueryPriority.OnAir);
            if (!physical.Known)
            {
                // Keep the exact proposal while the shared sampler resumes.
                // Unknown geometry cannot certify any layer of the corridor.
                if (pendingProbe != horizon.Probes) { pendingProbe = horizon.Probes; pendingAt = now; }
                Diagnostics = "spatial horizon observing a corridor: " + physical.Reason;
                if (now - pendingAt < .3f) break;
                horizon.Observe(false, false, 0); continue;
            }
            pendingProbe = -1;
            if (!physical.Clear) { horizon.Observe(true, false, 0); continue; }
            PredictSubjects((float)probe.Seconds);
            PlayerShotVisibility.Result view = visibility.Evaluate(to, toRotation, predicted, fov, aspect, near);
            float quality = view.Score * (.7f + .3f * PredictionConfidence((float)probe.Seconds));
            // Preserve the activity's observed spatial context rather than
            // drifting to another room/building for a marginal quality gain.
            // No context membership can admit an otherwise blocked corridor.
            if (sourceRoom != null || sourceBuilding != null)
            {
                PlaceNode? destination = PlaceSurvey.Shared.FindContext(to, now);
                if (sourceRoom != null && destination?.Id == sourceRoom) quality += .06f;
                if (sourceBuilding != null && Building(destination) == sourceBuilding) quality += .03f;
            }
            horizon.Observe(view.SurveyComplete, view.Picture().Readable, Mathf.Clamp01(quality));
        }
        if (horizon.Complete)
        {
            bool found = horizon.Found;
            if (found) { goal = horizon.FirstCorrection; goalUntil = now + .75f; }
            else { goal = guidance.Offset; goalUntil = 0; }
            Diagnostics = found
                ? $"spatial corridor {forecastSeconds:F1}s; {horizon.Probes} probes; correction {FromCore(goal).magnitude:F1}m"
                : $"spatial horizon found no complete measured corridor ({horizon.Probes} probes); retaining validated tracking";
            horizon.Reset(); nextPlan = now + .25f;
            if (found) affordableHorizon = Mathf.Min(4.5f, affordableHorizon + .25f);
        }
        else if (!horizon.Active && now >= nextPlan)
        {
            goal = guidance.Offset; goalUntil = 0; nextPlan = now + .15f;
            affordableHorizon = Mathf.Max(1.25f, forecastSeconds * .65f);
            Diagnostics = "spatial horizon evidence expired; retaining validated tracking";
        }
        // New plans preserve velocity. Expired/unknown forecasts smoothly brake
        // at the current correction; they never stop valid native tracking.
        guidance.Step(Mathf.Clamp(dt, 0, .1f), now < goalUntil ? goal : guidance.Offset, available);
    }

    private void Begin(float now, long currentEpoch, CameraSceneFrame context,
        IReadOnlyList<PlayerShotVisibility.Subject> subjects, ShotTrajectoryPolicy trajectory,
        Vector3 displayedPosition, Quaternion displayedRotation, Vector3 displayedFocus, Vector3 displayedAnchor,
        Vector3 currentFocus, float currentFov, float currentAspect, float nearClip, float freedom, PlaceNode? place)
    {
        camera = displayedPosition; rotation = displayedRotation; poseFocus = displayedFocus; focus = currentFocus;
        sceneVelocity = Vector3.ClampMagnitude(context.Velocity, 20); confidence = Mathf.Clamp01(context.Confidence);
        fov = currentFov; aspect = currentAspect; near = nearClip; started = now;
        sceneId = context.SceneId; epoch = currentEpoch; sourceInterior = context.Interior; sourceDoorway = context.DoorwayTransition;
        bool limited = context.Interior || context.DoorwayTransition || context.Environment == ShotEnvironment.Room ||
            place?.Kind == PlaceKind.Room || place?.Kind == PlaceKind.Opening;
        forecastSeconds = Mathf.Min(affordableHorizon, (float)ActorSpatialHorizon.PredictionSeconds(limited,
            context.Encounter != EncounterKind.None, Mathf.Clamp01(coherentSeconds / 3f), confidence, sceneVelocity.magnitude));
        sourceRoom = place?.Kind == PlaceKind.Room ? place.Id : null;
        // Building preference applies only if camera and activity already
        // share it; an exterior establishing shot is never forced indoors.
        string? actorBuilding = Building(place);
        sourceBuilding = Building(PlaceSurvey.Shared.FindContext(camera, now)) == actorBuilding ? actorBuilding : null;
        observed.Clear();
        for (int i = 0; i < subjects.Count && i < velocities.Length; i++)
        {
            CameraSceneSubject member = FindSubject(context, subjects[i])!;
            observed.Add(subjects[i]); velocities[i] = Vector3.ClampMagnitude(member.Velocity, 20);
            subjectIds[i] = member.Id;
        }
        ShotTrajectoryState far = trajectory.PreviewCoverage(1.5);
        for (int i = 0; i < nominal.Length; i++)
        {
            float seconds = forecastSeconds * (i + 1) / nominal.Length;
            ShotTrajectoryState state = seconds <= 1.5f ? trajectory.PreviewCoverage(seconds) : far;
            Vector3 futureAnchor = FromCore(state.Anchor) + (seconds > 1.5f ? FromCore(far.Velocity) * (seconds - 1.5f) : Vector3.zero);
            Vector3 position = camera + futureAnchor + Offset - displayedAnchor;
            if (!SupportedCoordinates(position))
            { Suspend("spatial prediction leaves supported world bounds"); nextPlan = now + .5f; return; }
            nominal[i] = ToCore(position);
        }
        Vector3 forward = Vector3.ProjectOnPlane(sceneVelocity.sqrMagnitude > 1 ? sceneVelocity : focus - camera, Vector3.up);
        Vector3 side = forward.sqrMagnitude > .01f ? Vector3.Cross(Vector3.up, forward).normalized : Vector3.right;
        horizon.Begin(now, currentEpoch, ToCore(camera), nominal, ToCore(side), guidance.Offset, freedom, forecastSeconds, guidance.Velocity);
        pendingProbe = -1;
    }

    private void ObserveMotion(float now, CameraSceneFrame context, Vector3 currentFocus)
    {
        Vector3 motion = Vector3.ClampMagnitude(context.Velocity, 20);
        float dt = now - observedAt;
        if (observedAt >= 0 && dt > 0 && dt <= .5f && motion.magnitude >= 1.5f &&
            Vector3.Distance(motion, priorVelocity) <= Mathf.Max(.75f, dt * 3f) &&
            Vector3.Distance(currentFocus, priorFocus + priorVelocity * dt) <= .4f)
            coherentSeconds = Mathf.Min(3, coherentSeconds + dt);
        else coherentSeconds = 0;
        observedAt = now; priorVelocity = motion; priorFocus = currentFocus;
    }

    private bool SnapshotMatches(CameraSceneFrame context, IReadOnlyList<PlayerShotVisibility.Subject> subjects,
        Vector3 currentFocus, float currentFov, float now, long currentEpoch)
    {
        if (epoch != currentEpoch || sceneId != context.SceneId || sourceInterior != context.Interior ||
            sourceDoorway != context.DoorwayTransition || subjects.Count != observed.Count ||
            Mathf.Abs(currentFov - fov) > 4 || now < started ||
            Vector3.Distance(currentFocus, focus + sceneVelocity * (now - started)) > .75f ||
            Vector3.Distance(Vector3.ClampMagnitude(context.Velocity, 20), sceneVelocity) > 2.5f) return false;
        for (int i = 0; i < observed.Count; i++)
            if (subjectIds[i] != FindSubject(context, subjects[i])?.Id ||
                Vector3.Distance(subjects[i].Focus, observed[i].Focus + velocities[i] * (now - started)) > 1.2f) return false;
        return true;
    }

    private Quaternion RotationAt(Vector3 position, float seconds)
    {
        Vector3 before = poseFocus - camera, after = focus + sceneVelocity * seconds - position;
        if (before.sqrMagnitude <= .01f || after.sqrMagnitude <= .01f) return rotation;
        Quaternion predictedRotation = Quaternion.FromToRotation(before, after) * rotation;
        Vector3 forward = predictedRotation * Vector3.forward;
        return Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < .995f ? Quaternion.LookRotation(forward, Vector3.up) : rotation;
    }

    private float PredictionConfidence(float seconds) => confidence / (1 + seconds * (.12f + (1 - confidence) * .4f));
    private void PredictSubjects(float seconds)
    {
        predicted.Clear();
        for (int i = 0; i < observed.Count; i++)
        {
            var subject = observed[i];
            // Spread the uncertain envelope instead of pretending distant
            // constant-velocity forecasts pinpoint a future actor exactly.
            float uncertainty = Mathf.Min(.6f, velocities[i].magnitude * seconds * .08f * (1 - PredictionConfidence(seconds)));
            predicted.Add(new PlayerShotVisibility.Subject(subject.Focus + velocities[i] * seconds, subject.Weight,
                subject.Radius + uncertainty, subject.Primary, subject.Height, subject.BottomOffset, subject.Scale, subject.ActorId));
        }
    }

    // Each shot may frame a different subset. A prefix index into the observed
    // encounter would attribute another actor's motion to this subject.
    private static CameraSceneSubject? FindSubject(CameraSceneFrame context, PlayerShotVisibility.Subject subject)
    {
        foreach (CameraSceneSubject member in context.Subjects)
            if (member.Actor && subject.ActorId != 0 && member.Actor.GetInstanceID() == subject.ActorId)
                return member;
        return null;
    }

    private static string? Building(PlaceNode? place)
    {
        for (int i = 0; place != null && i < 5; i++, place = PlaceSurvey.Shared.Snapshot.Find(place.ParentId))
            if (place.Kind == PlaceKind.Building) return place.Id;
        return null;
    }
    private void InvalidateForecast(string reason)
    { horizon.Reset(); goal = guidance.Offset; goalUntil = 0; Diagnostics = reason; }
    internal void Suspend(string reason)
    {
        // A disabled/off-air helper is stationary. Do not report an old
        // correction velocity or resume its hidden momentum later. Ordinary
        // forecast replacement uses InvalidateForecast and retains inertia.
        guidance.Reject(guidance.Offset);
        InvalidateForecast(reason);
    }
    internal void Reject(Vector3 acceptedOffset)
    { guidance.Reject(ToCore(acceptedOffset)); Suspend("spatial guidance rolled back to the last validated anchor"); }
    internal void Reset()
    {
        horizon.Reset(); guidance.Reset(); goal = default; observed.Clear(); nextPlan = goalUntil = coherentSeconds = 0;
        observedAt = pendingAt = -1; pendingProbe = -1; sceneId = epoch = 0; sourceBuilding = sourceRoom = null;
        affordableHorizon = 4.5f;
        Diagnostics = "spatial horizon awaiting a displayed actor shot";
    }
    private static bool SupportedCoordinates(Vector3 p) => TargetRoster.Finite(p) && Mathf.Abs(p.x) < 19990 && Mathf.Abs(p.y) < 19990 && Mathf.Abs(p.z) < 19990;
    private static AutonomyPosition ToCore(Vector3 p) => new AutonomyPosition(p.x, p.y, p.z);
    private static Vector3 FromCore(AutonomyPosition p) => new Vector3(p.X, p.Y, p.Z);
}
