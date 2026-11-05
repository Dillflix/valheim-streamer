using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ValheimBroadcaster.Core;

/// <summary>Recorded observations exercise ownership decisions; synthetic
/// certified geometry exercises navigation. Neither is a Unity/world replay.</summary>
internal static class JourneyMissionReplayTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Recorded 0.8.48 picture loss cannot turn Scenic dwell into Journey cancellation", RecordedDwellPreemption);
        yield return ("Recorded 0.8.48 cuts and failed alternatives preserve one manual departure deadline", RecordedDepartureOwnership);
        yield return ("Recorded single-section reserve permits physically bounded departure without a second-section wait", RecordedLaunchReserve);
        yield return ("Integrated synthetic Journey crosses a kilometre through loading changes failed alternatives and local repair", SyntheticKilometre);
        yield return ("Integrated Journey releases travel only after explicit player or operator preemption", ExplicitPreemption);
        yield return ("Recorded 0.8.49 readable stationary footage and twelve-second repair retries never earn travel progress", RecordedPlanningStall);
    }

    private const long Mission = 7, World = 42;
    private static AutonomyPosition P(double x) => new AutonomyPosition((float)x, 20, 0);
    private static readonly ScenicRegion[] Regions =
    {
        new ScenicRegion("local", "Local", P(0)), new ScenicRegion("alternative", "Alternative", P(1600))
    };
    private static void Check(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }
    private static void Near(double expected, double actual, double epsilon = .00001) =>
        Check(Math.Abs(expected - actual) <= epsilon, $"Expected {expected}, got {actual}");
    private static AutonomousDirector Director()
    {
        var director = new AutonomousDirector { RegionSeconds = 2, MinimumTravelSeconds = 1,
            MinimumScenicTravelDistance = 100, RevisitSeconds = 1000 };
        director.SetEnabled(true); return director;
    }
    private static void Tick(AutonomousDirector director, JourneyTravelController mission, double at,
        double dt, bool picture, double position = 0, IReadOnlyList<AutonomyPlayer>? players = null,
        bool searchExpired = false) =>
        director.Tick(at, dt, players ?? Array.Empty<AutonomyPlayer>(), Regions, P(position), null, picture,
            idleSceneAvailable: true, idleSearchExpired: searchExpired, scenicFootageReady: picture,
            scenicFootageDelta: picture ? dt : 0, scenicEditBoundary: true,
            scenicJourneyOwnsTravel: mission.SuppressesScenicRelocation);
    private static JourneyTravelController Begin(double at = 0, long epoch = 1)
    {
        var mission = new JourneyTravelController();
        mission.Begin(Mission, World, epoch, at, 90, 900);
        mission.Observe(new JourneyTravelObservation(at, World, epoch));
        return mission;
    }

    private static JsonDocument Fixture(string filename = "0.8.48-lifecycle.json")
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "tests", "fixtures", "journey-mission", filename);
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Sanitized Journey observation fixture was not found: " + filename);
    }

    private static void RecordedPlanningStall()
    {
        using var fixture = Fixture("0.8.49-planning-stall.json");
        JsonElement root = fixture.RootElement;
        Check(root.GetProperty("sourceVersion").GetString() == "0.8.49", "Wrong recorded failure version");
        var mission = Begin(epoch: 3); var director = Director(); var runway = new JourneyRunwayPolicy();
        double originalDistance = root.GetProperty("admittedRoute").GetProperty("travel").GetDouble();
        double previousAt = 0, previousRepair = -1, recordedRepairAt = -1, watchdogRepairAt = -1;
        double lastMeasured = 0; int repairFailures = 0, preparedWhileRetained = 0;
        bool admitted = false, sawStoppedReserve = false;
        foreach (JsonElement row in root.GetProperty("lifecycle").EnumerateArray())
        {
            double at = row.GetProperty("at").GetDouble();
            string phase = row.GetProperty("phase").GetString()!;
            bool repair = row.GetProperty("localRepair").GetBoolean();
            bool picture = row.GetProperty("usablePicture").GetBoolean();
            double measured = row.GetProperty("measuredMetres").GetDouble();
            double remaining = row.GetProperty("freshRunwayMetres").GetDouble();
            bool finish = phase == "finish";
            // These are the recorded decisions/observations. No failed
            // corridor is converted into a certified successor by this replay.
            var evidence = admitted && !repair && remaining > 0
                ? new JourneyRouteEvidence(18, 3, remaining) : default;
            mission.Observe(new JourneyTravelObservation(at, World, 3,
                preemption: finish ? JourneyTravelPreemption.Operator : JourneyTravelPreemption.None,
                navigation: admitted ? JourneyTravelNavigation.Clear : JourneyTravelNavigation.WaitingForObservation,
                pictureAvailable: picture, route: evidence, renderedTravelMetres: measured));
            if (phase == "start")
            {
                Check(!admitted && runway.TryStart(1, originalDistance, 12, out _),
                    "The recorded physically supported departure was rejected");
                // A current ready observation is required independently of a
                // readable picture. The complete captured admission supplies it.
                mission.Observe(new JourneyTravelObservation(at, World, 3, pictureAvailable: picture, renderedTravelMetres: measured));
                Check(mission.TryAcceptRoute(new JourneyRouteEvidence(18, 3, originalDistance), at, out _),
                    "Recorded admitted route did not acquire mission authority");
                admitted = true;
            }
            if (phase == "local-replan-start") recordedRepairAt = previousRepair = at;
            if (mission.LocalReplanRequested && watchdogRepairAt < 0)
            {
                watchdogRepairAt = at; mission.AcknowledgeLocalReplan();
            }
            if (repair && phase == "preparation-retry")
            {
                Check(previousRepair >= 0 && at - previousRepair >= 12 && at - previousRepair < 13,
                    "Fixture no longer represents the repeated twelve-second search budget");
                previousRepair = at; repairFailures++;
            }
            if (admitted && !repair && row.GetProperty("effectiveCruise").GetDouble() == 0)
            {
                Check(remaining >= JourneyRunwayPolicy.StopMarginMetres && remaining < .6,
                    "Recorded pause was not at the certified stopping margin");
                sawStoppedReserve = true;
            }
            if (repair && !finish)
            {
                Check(!mission.CanAdvance && mission.State == JourneyTravelState.Replanning,
                    "A retained live picture granted movement without a successor route");
                if (row.GetProperty("ordinaryPrepared").GetInt64() == 29)
                {
                    Check(picture && row.GetProperty("plan").GetInt64() == 18,
                        "The observed prepared-fallback/retained-picture conflict was lost");
                    preparedWhileRetained++;
                }
            }
            Tick(director, mission, at, Math.Min(.1, at - previousAt), picture);
            if (!finish)
                Check(mission.Active && mission.MissionId == Mission && director.PendingTravel == null,
                    "A captured failed search/camera pause was misclassified as completed travel or ordinary relocation");
            lastMeasured = measured; previousAt = at;
        }
        Check(admitted && sawStoppedReserve && repairFailures == 4 && preparedWhileRetained > 10,
            "Replay omitted admission, physical stopping, repeated failed batches or the blocked prepared fallback");
        Check(watchdogRepairAt >= recordedRepairAt && watchdogRepairAt - recordedRepairAt < 1.1,
            "Sparse captured observations failed to request repair within their one-second recording interval");
        Check(mission.State == JourneyTravelState.Cancelled && mission.Cause == JourneyTravelCause.OperatorPreempted &&
            mission.NoProgressSeconds > 68 && mission.NoProgressSeconds < 70,
            "Recorded manual stop was replaced by a false progress reset or unrelated timeout");
        Near(34.586807262476135, lastMeasured);

        JsonElement interval = root.GetProperty("stationaryInterval");
        double stationaryFrom = interval.GetProperty("from").GetDouble();
        var poses = new List<JsonElement>();
        foreach (JsonElement sample in root.GetProperty("pictureSamples").EnumerateArray())
            if (sample.GetProperty("at").GetDouble() >= stationaryFrom) poses.Add(sample);
        Check(poses.Count > 60 && interval.GetProperty("through").GetDouble() - stationaryFrom > 68.9 &&
            interval.GetProperty("heldFrameCount").GetInt32() == 0 && interval.GetProperty("slateFrameCount").GetInt32() == 0,
            "Recorded failure was incorrectly reduced to a held texture or loading slate");
        foreach (JsonElement pose in poses)
        {
            Check(pose.GetProperty("presentation").GetString() == "Live" && pose.GetProperty("poseAudit").GetString() == "Matched" &&
                pose.GetProperty("shot").GetInt64() == 18, "Static recorded footage lost its real live-shot identity");
            for (int coordinate = 0; coordinate < 3; coordinate++)
                Near(poses[0].GetProperty("camera")[coordinate].GetDouble(), pose.GetProperty("camera")[coordinate].GetDouble());
        }
        // This asserts the measured failure, not a successful new route. Native
        // geometry tests consume candidateQueries separately; a readable prefix
        // is neither a complete reconstructed corridor nor a certified ending.
    }


    private static void RecordedDwellPreemption()
    {
        using var fixture = Fixture();
        Check(fixture.RootElement.GetProperty("sourceVersion").GetString() == "0.8.48", "Wrong evidence version");
        var mission = Begin(); var director = Director(); var control = Director();
        // The capture starts during an existing mission, after >1km. Only its
        // retained identity is seeded; no absent departure or geometry is invented.
        Check(mission.TryAcceptRoute(new JourneyRouteEvidence(109, 1, 1), 0, out _), "Could not seed prior admitted mission");
        mission.Observe(new JourneyTravelObservation(0, World, 1,
            navigation: JourneyTravelNavigation.WaitingForObservation));
        // Ordinary dwell is already due, but an unfinished picture delays its edit.
        foreach (var d in new[] { director, control })
            for (int i = 0; i < 3; i++)
                d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), Regions, P(0), null, true, scenicEditBoundary: false);
        bool witnessedOldTravel = false; int events = 0;
        foreach (var row in fixture.RootElement.GetProperty("recoveryCut").EnumerateArray())
        {
            double at = row.GetProperty("at").GetDouble(); bool picture = row.GetProperty("pictureAvailable").GetBoolean();
            mission.Observe(new JourneyTravelObservation(at, World, 1,
                navigation: JourneyTravelNavigation.WaitingForObservation, pictureAvailable: picture,
                editorial: JourneyTravelEditorial.ChangingShot));
            Tick(director, mission, at + 3, .016, picture);
            Check(mission.Active && mission.MissionId == Mission && !mission.CanAdvance,
                "Picture-only recovery reset mission identity or manufactured physical movement");
            Check(director.PendingTravel == null, "Ordinary Scenic dwell stole retained Journey ownership");
            if (!picture && !witnessedOldTravel)
            {
                control.Tick(at + 3, .016, Array.Empty<AutonomyPlayer>(), Regions, P(0), null, false,
                    idleSceneAvailable: true, scenicJourneyActive: true, scenicJourneyPending: true);
                witnessedOldTravel = control.PendingTravel?.Kind == AutonomyTravelKind.Scenic;
            }
            events++;
        }
        Check(events == 6 && witnessedOldTravel,
            "Replay did not expose the old availability-dependent relocation guard");
    }

    private static void RecordedDepartureOwnership()
    {
        using var fixture = Fixture(); var mission = Begin(epoch: 6); var director = Director();
        int cuts = 0, retries = 0, resets = 0; long previousPicture = 242; double previousAt = 0;
        foreach (var row in fixture.RootElement.GetProperty("departure").EnumerateArray())
        {
            double at = row.GetProperty("at").GetDouble(); long pictureId = row.GetProperty("picture").GetInt64();
            bool picture = row.GetProperty("pictureAvailable").GetBoolean(); string kind = row.GetProperty("event").GetString()!;
            double metres = row.GetProperty("certifiedMetres").GetDouble();
            // Queued candidates are not accepted routes. Failure of one
            // alternative is not exhaustion of all physical navigation.
            mission.Observe(new JourneyTravelObservation(at, World, row.GetProperty("epoch").GetInt64(),
                navigation: JourneyTravelNavigation.WaitingForObservation, pictureAvailable: picture,
                route: metres > 0 ? new JourneyRouteEvidence(1000 + row.GetProperty("sourceSequence").GetInt64(), 6, metres) : default,
                editorial: pictureId == previousPicture ? JourneyTravelEditorial.Clear : JourneyTravelEditorial.ChangingShot));
            if (pictureId != previousPicture) cuts++;
            if (kind == "preparation-retry") retries++;
            if (kind == "departure-start") resets++;
            Tick(director, mission, at, Math.Min(.1, at - previousAt), picture);
            Check(!mission.CanAdvance && mission.MissionId == Mission && !mission.HasStarted,
                "Queued evidence or ordinary coverage admitted a route without a handoff");
            if (at < 90)
                Check(mission.Active && director.PendingTravel == null,
                    "A legacy departure reset/cut/failed alternative cancelled the original request");
            previousAt = at; previousPicture = pictureId;
        }
        Check(cuts >= 5 && retries == 3 && resets == 4, "Capture reduction lost critical lifecycle observations");
        Check(mission.State == JourneyTravelState.Cancelled && mission.Cause == JourneyTravelCause.DepartureExpired,
            "Failed alternatives extended the request beyond its original bounded deadline");
        Near(90.003173828125, mission.DepartureElapsedSeconds);
    }

    private static void RecordedLaunchReserve()
    {
        using var fixture = Fixture(); var runway = new JourneyRunwayPolicy(); bool first = true;
        foreach (var row in fixture.RootElement.GetProperty("departure").EnumerateArray())
        {
            double metres = row.GetProperty("certifiedMetres").GetDouble(); if (metres <= 0) continue;
            // This is a counterfactual admission test of the recorded fresh
            // reserve, not a claim that the recorded run then travelled.
            Check(row.GetProperty("queued").GetInt32() == 1, "Fixture no longer exercises single-section admission");
            Check(runway.TryStart(1, metres, 12, out _), "A useful certified route still waits for a second section");
            Check(runway.EffectiveCruise >= 2 && runway.EffectiveCruise <= 12 &&
                JourneyRunwayPolicy.StoppingDistance(runway.EffectiveCruise) + runway.EffectiveCruise * 2 <= metres + .000001,
                "Admission did not retain useful movement plus a physical stop reserve");
            if (first) { Near(2.01153564453125, row.GetProperty("at").GetDouble()); first = false; }
        }
        Check(!first && runway.Samples == 0,
            "Legacy retry latency was incorrectly turned into successful producer throughput");
    }

    private sealed class Section
    {
        internal readonly ScenicShotPlan Plan;
        internal readonly double Start, End;
        internal readonly long Epoch;
        internal Section(long id, long previous, long epoch, double start, double length)
        {
            Start = start; End = start + length; Epoch = epoch;
            var feature = new ScenicFeature("synthetic", ScenicFeatureKind.Landscape, P(start + length / 2),
                new AutonomyPosition(1, 0, 0), 20, 5, .9f);
            var plan = ScenicShotPlan.Spatial(id, feature, ScenicShotRole.Traverse,
                new[] { P(start), P(start + length / 2), P(End) },
                new[] { P(start + 21), P(start + length / 2 + 21), P(End + 21) }, 65);
            Plan = previous == 0 ? plan.AsJourneyDeparture(Mission, length / 6) : plan.AsJourney(Mission, previous, length / 6);
        }
    }

    private static void SyntheticKilometre()
    {
        // Deliberately synthetic: the geometry adapter certifies straight safe
        // sections after finite work. The real policies decide launch, loading
        // footprint, speed, admission, ownership, movement credit and arrival.
        foreach (int fps in new[] { 30, 60 }) RunSynthetic(fps);
    }

    private static void RunSynthetic(int fps)
    {
        var mission = Begin(); var director = Director(); var runway = new JourneyRunwayPolicy();
        Check(ScenicJourneyItinerary.TrySelect(P(0), new AutonomyPosition(1, 0, 0),
            new[] { new ScenicRegion("synthetic-arrival", "Synthetic arrival", P(1000)) }, null, 1000, 10, null,
            out var itinerary, out _), "Synthetic known destination could not be selected");
        var distance = new ScenicJourneyPolicy(); distance.Start(Mission, itinerary!, 6);
        var sections = new List<Section>(); Section? work = null; Section? lastPresented = null;
        double x = 0, carrier = 0, now = 0, workStarted = 0, due = 0, lastX = 0;
        double dt = 1d / fps; long epoch = 1, id = 100; bool changedEpoch = false, blockedOnce = false;
        int jobs = 0, successes = 0, failed = 0, handoffs = 0, stoppedFrames = 0, speculativeFrames = 0;
        bool failWork = false; double previousSpeed = 0;
        for (int frame = 0; frame < fps * 850 && !distance.TargetReached; frame++, now += dt)
        {
            bool blocked = now >= 100 && now < 106;
            bool loading = now >= 65 && now < 67;
            if (!changedEpoch && now >= 65 || !blockedOnce && blocked)
            {
                if (!changedEpoch && now >= 65) { epoch++; changedEpoch = true; }
                if (blocked) blockedOnce = true;
                sections.Clear(); work = null;
            }
            if (work != null && now >= due)
            {
                if (failWork) failed++;
                else
                {
                    long predecessor = sections.Count == 0 ? 0 : sections[sections.Count - 1].Plan.Id;
                    Check(JourneyRunwayPolicy.HeadMatches(work.Plan, predecessor, Mission, work.Epoch, now, epoch, now),
                        "Completed synthetic route had an invalid predecessor or scene identity");
                    sections.Add(work); successes++;
                    Check(runway.ObservePreparation(work.End - work.Start, workStarted, now), "Successful complete work was not observed");
                }
                work = null;
            }
            double tail = sections.Count == 0 ? x : sections[sections.Count - 1].End;
            double loadedReach = ((int)(now / 11) % 3) switch { 0 => 85, 1 => 61, _ => 75 };
            // This simulated carrier can only lead along already-certified
            // sections. It does not stand in for the plugin's Unity callback.
            carrier = sections.Count == 0 ? x : Math.Min(tail, x + 16);
            double length = JourneyRunwayPolicy.WindowMetres(6, Math.Max(0, tail - carrier), 32, loadedReach);
            if (work == null && !blocked && !loading && length >= 8 && sections.Count < 3 &&
                (sections.Count == 0 || runway.NeedsPreparation(tail - x, 6, loadedReach - 21)))
            {
                long predecessor = sections.Count == 0 ? 0 : sections[sections.Count - 1].Plan.Id;
                work = new Section(++id, predecessor, epoch, tail, length); workStarted = now;
                due = now + new[] { 2.5, 6, 3, 8 }[jobs % 4]; failWork = ++jobs % 5 == 0;
                Check(work.End - carrier + JourneyRunwayPolicy.GazeReach(6) <= loadedReach + .0001,
                    "Proposal exceeded changing loaded camera-and-gaze horizon");
            }
            var head = sections.Count == 0 ? null : sections[0];
            var navigation = blocked ? JourneyTravelNavigation.Blocked : loading ? JourneyTravelNavigation.WaitingForLoading :
                head == null ? JourneyTravelNavigation.WaitingForObservation : JourneyTravelNavigation.Clear;
            bool picture = frame % (fps * 17) >= 2; // ordinary two-frame presentation cuts
            var evidence = head == null ? default : new JourneyRouteEvidence(head.Plan.Id, epoch, Math.Max(0, tail - x));
            mission.Observe(new JourneyTravelObservation(now, World, epoch, navigation: navigation, pictureAvailable: picture,
                route: evidence, editorial: picture ? JourneyTravelEditorial.Clear : JourneyTravelEditorial.ChangingShot,
                renderedTravelMetres: distance.TravelMetres));
            if (head != null && !blocked && !loading && mission.AcceptedRouteId != head.Plan.Id)
            {
                // Prepared observation alone must not start physical movement.
                Check(!mission.CanAdvance, "An unaccepted queue head granted movement"); speculativeFrames++;
                if (!runway.Started) Check(runway.TryStart(sections.Count, tail - x, 6, out _), "Synthetic useful departure was rejected");
                Check(mission.TryAcceptRoute(evidence, now, out string admissionReason),
                    $"Validated current route could not resume the same mission at {now:0.00}s/{x:0.0}m: {admissionReason}; {mission.Cause}; measured {distance.TravelMetres:0.0}m");
                handoffs++;
            }
            double before = x;
            if (runway.Started)
            {
                double speed = runway.StepSpeed(6, Math.Max(0, tail - x), dt, braking: !mission.CanAdvance);
                if (previousSpeed > 0) Check(Math.Abs(speed - previousSpeed) <= JourneyRunwayPolicy.Deceleration * dt + .000001,
                    $"A planning interruption introduced a speed discontinuity: {previousSpeed} to {speed}");
                previousSpeed = speed;
                if (mission.CanAdvance && head != null)
                    x += Math.Min(speed * dt, Math.Min(head.End - x, Math.Max(0, 1000 - x)));
            }
            if (!mission.CanAdvance) { Near(before, x); stoppedFrames++; }
            Check(x <= tail + .00001 && carrier <= tail + .00001, "Speculative route length advanced camera or carrier");
            Tick(director, mission, now, dt, picture, x, searchExpired: !picture);
            Check(mission.Active && mission.MissionId == Mission && director.PendingTravel == null,
                $"Mission/ordinary travel escaped ownership at {now:0.00}s/{x:0.0}m");
            if (head != null) lastPresented = head;
            if (lastPresented != null)
            {
                double progress = Math.Max(0, Math.Min(1, (x - lastPresented.Start) / (lastPresented.End - lastPresented.Start)));
                distance.Observe(lastPresented.Plan, now, progress, picture, false, P(x));
                distance.ObserveArrival(now, picture, false, P(x), x >= 999.9);
            }
            Check(distance.Active && !distance.BudgetExpired, "Distance accounting rejected a legitimate multi-section mission");
            if (head != null && x >= head.End - .000001 && x < 1000) sections.RemoveAt(0);
            lastX = x;
        }
        Check(distance.TargetReached && lastX >= 999.9 && distance.TravelMetres > 975 && distance.Windows > 20,
            $"Integrated synthetic navigation failed to arrive: {lastX:0.0}m/{distance.TravelMetres:0.0}m, {now:0.0}s");
        Check(changedEpoch && blockedOnce && failed >= 4 && successes > 25 && handoffs > 20 && stoppedFrames > fps * 6 && speculativeFrames > 20,
            "Scenario bypassed producer failures, pending admission, load changes or transient blockage");
        mission.Observe(new JourneyTravelObservation(now, World, epoch, destinationReached: true, arrivalPresented: distance.TargetReached));
        Check(mission.State == JourneyTravelState.Completed && !mission.SuppressesScenicRelocation && !mission.CanAdvance,
            "Verified arrival did not release mission ownership");
    }

    private static void ExplicitPreemption()
    {
        foreach (var preemption in new[] { JourneyTravelPreemption.Operator, JourneyTravelPreemption.Player })
        {
            var mission = Begin(); var director = Director();
            Check(mission.TryAcceptRoute(new JourneyRouteEvidence(1, 1, 30), 0, out _), "Seed route rejected");
            Tick(director, mission, 0, 0, false);
            mission.Observe(new JourneyTravelObservation(1, World, 1, preemption: preemption, pictureAvailable: false));
            Check(!mission.Active && !mission.CanAdvance && !mission.SuppressesScenicRelocation,
                "Explicit preemption left movement authority behind");
            if (preemption == JourneyTravelPreemption.Player)
            {
                Tick(director, mission, 1, 1, false, players: new[] { new AutonomyPlayer("player", "Player", P(1600), 100, false) });
                Check(director.PendingTravel?.Kind == AutonomyTravelKind.Player,
                    "Released Journey prevented actual player acquisition");
            }
            else
            {
                Tick(director, mission, 1, 1, false, searchExpired: true);
                Check(director.PendingTravel?.Kind == AutonomyTravelKind.Scenic,
                    "Explicitly ended mission continued blocking ordinary Scenic recovery");
            }
            Check(!mission.TryAcceptRoute(new JourneyRouteEvidence(2, 1, 50), 2, out _),
                "Late asynchronous geometry resurrected a cancelled request");
        }
    }
}
