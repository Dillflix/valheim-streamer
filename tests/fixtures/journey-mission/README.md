# Journey lifecycle regression observations

`0.8.48-lifecycle.json` reduces observations from the supplied 0.8.48 diagnostic capture. It retains relative times, source sequence numbers, picture/attempt identities, fresh certified runway, preparation estimates and failure counts. Absolute positions, destination names, player identifiers, endpoints and raw log text are excluded.

The recovery excerpt starts at source sequence 60596, after an existing Journey entered local repair coverage. Ordinary Scenic dwell requested relocation at sequence 60701 during picture loss; sequence 60704 then reported interruption. The manual departure excerpt begins at sequence 61407 and ends at 80693, spanning 90.003 seconds, four departure attempts, picture cuts, repeated failed alternatives and an eventual request deadline.

These observations reproduce policy inputs and establish the ownership regression. They do not include complete world geometry, candidate admission evidence or a new successful flight. In particular, estimated preparation duration is not a successful job's measured throughput. A test of admission using recorded fresh runway is counterfactual; it does not alter the recorded outcome.

`JourneyMissionReplayTests` separately runs a **synthetic** kilometre with certified straight sections, delayed and failed jobs, changing loading horizons, scene generation changes, local obstruction, picture cuts and an actual arrival dwell through production Core policies. Its geometry producer and carrier are a test adapter. It verifies Core lifecycle/navigation policy integration, not Unity geometry, the plugin's carrier callback, rendering or multiplayer execution.
