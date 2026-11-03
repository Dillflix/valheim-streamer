# Server-guided scenic destinations — 0.8.32

The companion now suggests player construction and already-placed world landmarks alongside generated scenic regions. The broadcaster can discover these destinations with no ordinary players online. It still needs a real client to load the area, measure its geometry and produce a readable picture. This does not activate creatures or supply remote camera/occlusion evidence.

## Upgrade

Install the **0.8.32 client and server companion** from the same successful build. Stop the respective process first, replace the three owned client DLLs plus included Cinemachine notices on the broadcaster, and the two owned server DLLs on the dedicated host. Preserve configuration and remove duplicate copies of the owned DLLs. Ordinary players need no additional mod. See [multiplayer setup](MULTIPLAYER-OBSERVER.md) for the existing observer allowlist.

Observer admission remains **protocol 3**, including explicit opt-in before admission. `InterestRequest1`, `InterestReply1` and `InterestFeedback1` are a separately versioned optional capability. A new client with an older protocol-3 companion continues ordinary player/generated-region discovery. An old client with this companion retains its previous behavior. Both endpoints need this update to use destination guidance and feedback; no server has been installed or restarted as part of publishing these artifacts.

The server adds one setting in `BepInEx/config/com.dillflix.valheimbroadcaster.server.cfg`:

```ini
[Observer]
WorldInterestEnabled = true
```

It defaults on and also requires `Enabled`, `DiscoveryEnabled` and a confirmed allowlisted observer. Existing client `Automation.Enabled` and `ScenicTravel` settings control travel. Disabling the interest capability preserves ordinary discovery and the observer role.

## What is discovered

| Source | Destination information | Limits |
| --- | --- | --- |
| Existing persistent player construction | Base/settlement center, approximate extent, piece count, functional bed/crafting anchors and a local approach hint | Construction pivots clustered in 32m horizontal / 64m vertical cells. Four pieces or a functional anchor qualify. These are approximate subjects, not exact building meshes or room detection. |
| Sprawling construction | Parent settlement plus up to 12 local area subjects | Connected footprints are bounded to 300m horizontal / 200m vertical spans. Larger/disconnected development becomes separate clusters. Approaches use a local dense area, not a settlement's potentially empty centroid. |
| Placed world-location records | Landmark prefab label, center, extent and approach | Only `m_placed` locations. No undiscovered planned locations, personal map pins or administrator-authored coordinate markers. Villages/ruins appear when represented by a placed world location. |

The scanner reads persistent ZDO metadata rather than relying on locally instantiated buildings. It does not instantiate prefabs, write ZDOs, acquire ownership, force generation or activate AI. A marker/location record remains a hint: the structures may have changed since the location was placed. Portal-only records in Valheim's separate portal inventory are not indexed as standalone destinations.

Construction indexing is bounded to 4,096 occupied cells; the published catalogue holds at most 512 base/area and 512 landmark destinations. Capacity truncation and detected traversal changes mark a survey partial. Scan work runs at most every 50ms, with at most 512 object reads and a 2ms object-loop budget; empty-sector traversal is also bounded. Clustering is bounded by retained cells; approach validation is spread across ticks, at most two candidates/four cardinal approaches each per tick. A finished sweep is followed by a 20-second interval. A first partial catalogue can be prepared before the full sweep finishes. These are code bounds, not measured production-host frame-time results.

Approaches must be on procedural dry land in an already-generated 3×3 zone neighborhood. An approach is **not** a collision/height guarantee: modified terrain, structures, water, foliage, focus and route clearance still require the existing client checks after arrival. Water-only sites and isolated generated zones can therefore be omitted. Existing local place discovery refines loaded buildings, districts and spaces for scenic composition.

IDs are coordinate-based and deterministic for unchanged construction; extending or splitting a cluster can change its ID. Remote data carries observation age, not just packet age. The client assembles a complete paginated catalogue before replacing the previous one, so deletions retire old entries. Packets expire after 45 seconds without updates; observations older than ten minutes are ineligible. Up to 16 pages are polled two seconds apart. Startup indexing and receiving the first catalogue take time; ordinary discovery remains usable meanwhile.

## Selection and feedback

Eligible bases/landmarks receive higher preference than generic terrain regions. Existing player priority, hold/manual controls, travel permissions, minimum/maximum distance, revisit history and travel cooldowns still apply. The merged list reserves at least 128 slots for generated-region fallback when that many are available. A highly ranked location cannot override those constraints.

Feedback applies only to an explicit automatic visit to a server interest. Starting nearby, reaching a coordinate, displaying a slate, showing a creature, or following a player does not earn positive feedback.

| Outcome | Evidence | Effect |
| --- | --- | --- |
| Usable footage | At least 15 seconds of usable Scenic footage after arrival | Modest preference increase; clears consecutive-failure cooldown. This proves usable footage near the destination, not that the named landmark/base was recognized in every frame. |
| Picture unavailable | Local picture-search budget expires without usable current Scenic output, before a successful visit result | Starts at five minutes; repeated failures increase cooldown up to 30 minutes. |
| Travel failed | Protected native travel rejects/fails the destination | Starts at two minutes; repeated failures increase cooldown up to 30 minutes. |
| Interrupted | Player takeover, creature cutaway, manual control, disabling automation or leaving the session | No destination penalty. |

There is at most one picture outcome per visit. Failure preference adjustments expire after a day; repeated failure is not a permanent blacklist. Local cooldowns apply immediately, before a server refresh. The server accepts bounded feedback only from a confirmed authorized peer, for a recently supplied current destination; picture outcomes also require a nearby peer reference position. It rate-limits messages and rejects replayed sequence numbers.

The server persists at most 1,024 entries per world in `BepInEx/config/ValheimBroadcaster/destinations-<world-id>.bin`, separately from Valheim saves. Writes use a temporary file and replacement, at most once a minute after changes, and flush on shutdown/world reset. Preserve that directory to retain history across upgrades. Corrupt or wrong-world history is rejected; in-memory operation can continue if saving is unavailable. No player identity, credential or stream URL is stored there.

## Inspect and test

- `broadcast destinations`: capability state, base/area versus landmark counts, and the top 12 IDs, labels, scores, source ages, cooldowns and approach coordinates. This ranking is before distance/revisit filtering.
- `broadcast diagnostics` / `broadcast auto status`: selected region ID, last travel decision, last feedback result and accumulated visit footage.
- BepInEx logs: published server catalogue counts, omitted approaches, automatic destination travel and accepted feedback. Existing scenic travel traces also include destination ID and whether it came from server interests.

Use an existing world with a small base, a sprawling settlement and a previously visited landmark in separated generated areas. With the broadcaster as the only client, wait for catalogue publication, inspect destination IDs, let automatic region dwell/travel run, and confirm a successful arrival earns `UsableFootage` only after 15 seconds of real Scenic output. Check that local construction subjects are discovered after loading; a server hint must never bypass the picture gate.

Then verify a difficult destination produces `PictureUnavailable` or `TravelFailed` as appropriate and remains excluded during its cooldown; reconnect/restart the companion and confirm persisted history. Interrupt a visit with a real player's arrival and with manual return: neither should create a negative result. Remove a test structure using an ordinary player, wait for a full rescan/catalogue cycle, and confirm its obsolete destination disappears. Finally disable `WorldInterestEnabled`, and test a new client against an older protocol-3 companion: ordinary discovery, admission, isolation and player following must continue.

Automated policy/codec tests and real-assembly compilation do not establish the quality of the resulting Valheim footage, indexing cost on a large production world, or live multiplayer behavior. Those runtime checks remain pending.
