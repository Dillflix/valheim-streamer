# Scenic planning and Journey departure — 0.8.46

Built on the complete 0.8.45 source. This release corrects destination selection and departure scheduling, and replaces misleading Scenic comparison counters. Observer admission remains protocol 3. The existing companion remains compatible; no server installation is required for these corrections.

## What the supplied evidence establishes

The two supplied November 5 startup logs are byte-identical and identify client 0.8.45. The screenshot shows the previous selection result: `No fresh, connected known destination at the requested journey scale`, followed by explicit Journey requests. The retained capture contains 161.3 seconds of observations, fourteen ordinary Scenic admissions and no Journey trace rows. Most retained footage is usable ordinary scenery. It does not demonstrate a successful Journey.

The old trace does not contain the destination catalogue or distinguish individual selection exclusions. Therefore the screenshot establishes selection failure, while source inspection establishes the contract defect below; the exact catalogue membership at the failed request cannot be reconstructed from this capture.

## Destination metadata is not a travel map

Server discovery supplies sampled dry arrival destinations and sites of interest. It requires a generated surrounding neighbourhood, excludes zones without a sampled dry arrival, pages its inventory and combines natural locations with sites. Water-only zones can therefore leave gaps between coastal or island destinations. The client retains a bounded selection. Neither side promises a continuous chain of points between the current camera and every destination.

The old itinerary selector nevertheless required a chain with no metadata gap larger than 192 metres. Missing points prevented planning even when a real destination existed at the requested scale. A catalogue of 1,024 interesting locations can still fail that artificial connectivity requirement.

Selection now uses a connected observation guide when available, and otherwise a bearing toward a real eligible catalogue destination. It does not invent a destination, intermediate observations or a clearance certificate. Every local camera section and its finite ending still require current terrain, near-plane, foliage and loading validation. A selected kilometre-scale intention is not proof that a kilometre flight will succeed. An independently framed departure may choose a known destination behind the preceding view; forward direction remains a preference rather than a veto. Paging a selected destination out of the bounded catalogue no longer implies withdrawal: natural coordinates remain tied to the current world/feed, while structural reports retain their actual age and explicit retry restrictions remain authoritative.

Selection evidence separates catalogue size, usable observations, nearby/connected observations, eligible connected/bearing destinations, and exclusions for freshness, cooldown, scale, heading and recent coverage. This distinguishes lack of an eligible destination from lack of intermediate metadata and from a later camera-route failure.

## Departure is its own shot

Journey preparation runs while ordinary scenery remains on air. It no longer depends on an unrelated ordinary successor already being prepared or on a particular ordinary route reaching its endpoint. A departure is independently framed toward its destination and requires its own validated section and ending. The current picture's minimum useful dwell and current scene/loading evidence still govern admission.

The request remains bounded to 90 seconds of unheld operation. That is a request lifetime, not a promise of successful travel. The operator preparation line, status command and trace events identify the actual waiting stage rather than silently reporting an unchanged old outcome. Optional region dwell/novelty travel and creature cutaways defer to a pending explicit request while usable Scenic footage continues. Player priority and genuine picture recovery remain authoritative. Actual rendered travel and arrival remain necessary for completion credit.

## Scenic comparison counters

Normal preparation aims to compare four distinct completed route preflights and retain up to three meaningfully different eligible options, within a six-second comparison budget. If no option is ready when that budget expires, already-running work can finish within a ten-second hard limit; it cannot start another candidate. This accommodates the existing five-second spatial search plus subsequent full preflight without repeatedly cancelling a valid first route. Scheduled slots, cooldown skips and repeated salvage/revalidation passes do not count as new evaluated routes. Nearby parallel wide shots with merely different IDs or role names do not supply composition diversity.

First-picture recovery and selected-route revalidation can use their first valid result. A needed replacement, exhausted schedule or preparation deadline can also end comparison with fewer alternatives. No counter target manufactures valid coverage. Retained options are pruned against the current scene, editorial eligibility and Journey loading reach before staging; original proof timestamps remain intact.

| Counter | Meaning |
| --- | --- |
| Scheduled | Candidate slots visited in this search |
| Skipped | Scheduled/editorially screened work not sent through a full preflight |
| Planned | Routes produced for full evaluation |
| Evaluated | Distinct route IDs with a completed preflight |
| Passes | Completed evaluations, including retries of the same route |
| Validated | Distinct IDs that passed geometry during the episode |
| Eligible | Distinct validated IDs offered after editorial eligibility checks |
| Retained | Currently retained, distinct eligible alternatives, at most three |
| Salvage attempts | Measured subsections proposed after a route failed; formerly `clear sections` |

Validated and eligible counts describe completed work; retained counts can decrease when evidence expires or a composition becomes ineligible. The last completed comparison remains visible while its selected route is revalidated. `scenic-search` trace records include the counts, timing and completion/cancellation reason.

## Verification and remaining runtime checks

Core tests cover sparse realistic catalogues, real destination identity, selection exclusions, departure scheduling, comparison breadth, composition duplicates, negative rankings, retries, stale evidence and ended-search lifecycle. Real-reference builds and native camera-mathematics tests remain separate from runtime validation; final counts are recorded in `RUNTIME-TESTS.md`.

No Valheim scene/loading/render loop runs in this build environment. In-game confirmation is still required:

1. Enable capture, request Scenic mode if another player is online, then request a 1,000 m Journey at 6 m/s. Observe the waiting stage, selected destination/guidance type, validated departure and actual travelled distance.
2. Start from a structure view or restrained pan. Ordinary footage should continue while departure prepares, without requiring an incidental landscape travelling shot.
3. Exercise unavailable destinations, genuinely blocked terrain, hold/resume, stop and player takeover. Pending work must not claim on-air ownership or successful distance.
4. Compare ordinary Scenic prefetch with first-picture recovery. Inspect completed comparison summaries and confirm that additional alternatives do not shorten readable shots or introduce black gaps.

Preserve matching Scenic JSONL, footage MP4/JSONL and BepInEx logs. The existing commands remain valid; this release changes their implementation and diagnostics, not the required command sequence.
