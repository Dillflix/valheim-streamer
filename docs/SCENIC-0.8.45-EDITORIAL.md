# Scenic evaluation and editorial correction — 0.8.45

Built on merged 0.8.44, retaining the operator mode controls and 0.8.43 acquisition repair. Observer protocol remains 3; no server update is required.

## Supplied runtime evidence

The latest files inside the supplied archive identify **0.8.43**, Unity 6000.0.75f1 and protocol 3. The eight retained scenic traces cover about 182.5 seconds. The latest eight video segments cover about 144.6 seconds: 1,508 of 1,729 mapped diagnostic frames are Live, 220 Slate and one Hold. These are diagnostic-frame counts, not a guarantee of the RTMP receiver's frame delivery.

- Thirteen of fourteen admitted pictures/routes are Establish; only one is Detail. There are no Traverse, Reveal or Journey admissions in this window. Establish uses 75°; the Detail shot correctly uses 53° throughout validation and capture.
- Ten complete moving shots in the video span roughly 11–14 seconds. Many short routes hit the existing twelve-second Establish minimum. Changing the minimum would not repair the narrow shot portfolio.
- Adjacent wide shots 350→354 and 354→357 reverse the viewing direction by approximately 119° and 133°. The camera moves about 28 m and 18 m between these cuts. The footage shows repeated wide views of the same hillside and ruin.
- An independently admitted still picture lasts about 4.6 seconds, consistent with its four-second minimum. Some other short runs are clipped by the recording boundaries. This retained sample does **not** establish sub-second cinematic shot lifetimes. No claim is made that the user's other observed brief shots cannot occur.
- The 10,191 budget records include repeated step/wait reporting. Of 4,329 actual wait records, 3,270 (75.5%) report incomplete foliage observations, 582 an unacquired scene, 426 a point/corridor budget deferral, and 51 unknown surface geometry. Local projections repeatedly enumerate a shared inventory typically containing about 2,155 renderers. The spatial projection commits 512 times during the trace, with a median start-to-commit interval around 142 ms. This is elapsed preparation latency, not CPU time per frame.
- The 10,951 `fallback-live: Survey reset` events come from repeatedly resetting the inactive Player rig. They do not mean the active Scenic catalogue resets every frame.

The package also contains older nested archives. They are separate sessions and are not combined into these statistics.

## Demonstrated causes and corrections

### Successor preparation and actual shown context

Ordinary prefetch begins after two shown seconds, while per-subject establishing context requires six. The old preferred-role query therefore requested Establish again. Its role-major schedule tried many other subjects' Establish views before other roles, but the production search could finish after four attempts. Exhaustively testing the full schedule missed this early-exit interaction.

A separate next-shot query can prepare development of the current subject while its establishing shot continues. That preparation does not award context: ordinary activation waits for the actual shown context, and an unavailable picture can still recover. Nonurgent prefetch interleaves alternate roles early; urgent first-picture search preserves subject fairness. A bounded score preference discourages consecutive same-role shots even when the feature IDs differ. Once a validated route exists, ordinary comparison can finish after its representative four-attempt prefix regardless of editorial score, so variety penalties cannot prolong a search indefinitely.

### Duplicate structure observations

The persistent place survey and destination survey could offer the same ruin as a Building and as a separate legacy piece cluster. Subject memory then treated the second label as a new site. Matching measured building/cluster bounds are reconciled when capturing subjects. Different hierarchy levels, neighbouring buildings and unmatched landmarks remain distinct.

### Cut continuity

Validated successors receive a bounded soft cost for large viewing-direction and lateral-motion reversals between similar-scale views in the same local area. Deliberate scale changes and different places remain available. This ranks candidates; it neither bypasses visibility validation nor invents an unvalidated blend through terrain or foliage.

### Scene-owned local foliage lookup

A spatial index is built incrementally alongside the existing catalogue traversal and published with the same renderer inventory. Local projection queries retrieve nearby candidates instead of repeatedly scanning every stationary tree renderer. Existing current-bounds checks, nearest retention, omission/unknown coverage, mesh refinement, scene generations and observation budgets remain authoritative.

Only supported stationary mesh geometry is indexed. Mobile/unknown renderers, falling logs, oversized bounds and unsupported bounds remain in a conservative global candidate set. Arbitrary third-party scripts moving nominally static trees are not proven by this classification. The runtime capture does not record the stationary fraction, so the actual speedup must be measured in a new capture. Projection trace rows now distinguish local `sourceCount` from total `inventoryCount`.

No-op resets of an already-empty inactive visibility projection no longer emit a trace event. Actual invalidations still do.

## Validation and remaining runtime work

Validation results are recorded in `RUNTIME-TESTS.md`. The tests include the real short search prefix, context earned from shown footage, hierarchy-preserving structure identity, the recorded wide-cut geometry, conservative spatial query membership, negative/cell-edge bounds and large inventories.

The supplied trace can replay its recorded coverage/deadline decisions. This does not re-execute Unity physics, native rendering or the corrected planner. Compilation and pure-policy tests do not establish improved visual quality.

With the client closed, replace the three owned client DLLs and included notices from the matching ZIP, preserving configuration. Use `broadcast mode scenic` while another player is online if needed. The next synchronized capture should establish: role/scale mix, complete shot dwell, direction changes at edits, local candidate counts versus total inventory, survey deferral time, actual route acceptance and any sub-second shot interruption. In-game confirmation of 0.8.45 remains pending.
