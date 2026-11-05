# Journey lifecycle regression observations

`0.8.48-lifecycle.json` reduces observations from the supplied 0.8.48 diagnostic capture. It retains relative times, source sequence numbers, picture/attempt identities, fresh certified runway, preparation estimates and failure counts. Absolute positions, destination names, player identifiers, endpoints and raw log text are excluded.

The recovery excerpt starts at source sequence 60596, after an existing Journey entered local repair coverage. Ordinary Scenic dwell requested relocation at sequence 60701 during picture loss; sequence 60704 then reported interruption. The manual departure excerpt begins at sequence 61407 and ends at 80693, spanning 90.003 seconds, four departure attempts, picture cuts, repeated failed alternatives and an eventual request deadline.

These observations reproduce policy inputs and establish the ownership regression. They do not include complete world geometry, candidate admission evidence or a new successful flight. In particular, estimated preparation duration is not a successful job's measured throughput. A test of admission using recorded fresh runway is counterfactual; it does not alter the recorded outcome.

`JourneyMissionReplayTests` separately runs a **synthetic** kilometre with certified straight sections, delayed and failed jobs, changing loading horizons, scene generation changes, local obstruction, picture cuts and an actual arrival dwell through production Core policies. Its geometry producer and carrier are a test adapter. It verifies Core lifecycle/navigation policy integration, not Unity geometry, the plugin's carrier callback, rendering or multiplayer execution.

## Captured 0.8.49 planning stall

`0.8.49-planning-stall.json` contains observations from the subsequent supplied 0.8.49 trace and synchronized footage. All camera and aim coordinates share one translation: the first camera point of admitted shot 18 becomes `(0, 0, 0)`. The original offset, destination and structure identities, account information and wall clock are omitted. Translation preserves relative shape, distance, heading and height changes. Times start at the recorded departure-preparation event; source sequence numbers remain available for matching the private original.

The reduction contains:

- The ten captured camera and aim knots of admitted route 18, its duration, length and FOV.
- Query 22's first rejected continuation prefix.
- Query 27's first elevated candidate, including its descent and first foreground-foliage failure (source sequences 11218–11299).
- Query 34's nearly complete readable prefix, ending with the foreground-foliage failure at progress 0.953 (source sequences 17765–17857).
- The recorded openings in repair batches 27, 32, 33, 34 and 35, including repeated locations after each twelve-second timeout.
- All 105 Journey lifecycle observations, selected synchronized camera poses, and the 68.983-second interval of stationary **live** output while ordinary fallback 29 was prepared.

`candidateQueries.samples` are evaluated native poses, including intermediate interpolation samples. They are **not** the original authored knots for those rejected candidates. Recorded `readable` values are the historical visibility result, not a reusable geometry certificate. Ground samples, collider shapes and foliage geometry are absent. Reconstructing a curve from these samples or applying a constructed low surface is a counterfactual numerical test, never a replay of the complete Valheim environment. Neither query 27 nor query 34 includes a validated ending.

The lifecycle consumer runs the actual mission/watchdog and outer director against these observations. It checks that live stationary footage earns no travel, failed search batches do not renew progress or cause ordinary relocation, and the recorded explicit stop remains distinct from a timeout. It does not claim that the new planner completes this flight or that the Unity fallback-admission wiring has been executed offline.
