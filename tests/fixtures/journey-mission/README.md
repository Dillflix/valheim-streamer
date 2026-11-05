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

## Captured 0.8.50 focus veto and changed repair context

`0.8.50-focus-veto.json` reduces the next supplied trace. One translation places the first camera knot of admitted Journey route 17 at `(0, 0, 0)`; it is applied to every route, evaluated camera, aim and fallback position. Times begin at the manual Journey request. Original coordinates, destination identities, accounts and wall clock are omitted; source sequence numbers are retained.

The first rejected view has zero measured foreground foliage and no camera containment, but its forward focus ray hits a collider named `Lod0` at 27.58995 metres. The trace does not establish that collider's parent or object type. The fixture includes all 58 captured Focus-veto samples from queries 17, 21 and 26, comparison samples that passed with nonzero foreground foliage, and separate physical vegetation-containment failures. `physicalCameraChecksPassed` is inferred from the historical validation order: a reported Focus/Foliage failure follows the camera, near-plane, surface and loading checks. It is not an independent geometry certificate.

The reduction also contains the admitted 34.615-metre prefix, its original authored knots, all 42 mission observations, the 75.34084-metre change from the pending repair's camera origin to ordinary fallback 29, and twelve subsequent acquisition failures at the same attempted camera/aim. These are observed **camera positions**; the carrier transform was not captured. They can test repair-context invalidation without pretending to replay network acquisition.

No angular picture grid was captured by 0.8.50. The old single focus ray cannot be expanded into fifteen measured rays, nor can the old weighted foreground statistic be treated as a pixel coverage fraction. Tests may apply the new surface-depth classifier to the recorded ray and verify missing-grid handling, but cannot conclude that the complete new picture policy accepts this historical view or that the Journey now completes in Valheim.

`JourneyLoadingContextTests` uses the two recorded camera positions as explicitly synthetic carrier proxies. The old origin alone remains within the configured local reach after the 75.34-metre cut, while its required view-observation footprint does not. This tests the production retention policy and demonstrates why checking only the origin misses incompatible planning work. Separate constructed cases cover ordinary small movement, named-subject reach, and a path whose endpoints fit but interior observations do not. These tests do not reconstruct actual carrier motion or server loading.
