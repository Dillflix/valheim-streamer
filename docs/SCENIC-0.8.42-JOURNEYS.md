# Scenic destination journeys — 0.8.42

Built on the complete 0.8.41 source. This replaces Journey's projected endpoint and inherited view direction with a selected known destination, an observed itinerary, independent forward gaze, and a prepared ending for every admitted section. Ordinary Scenic coverage prepares concurrently. **Observer protocol remains 3; no server update is required.**

## Failure established by the supplied recording

Journey 19 starts at about 89.07 seconds. The captured camera travels approximately 145.45 m, with 142.22 m displacement, against a nominal 1,000 m objective. Motion ends around 113.89 seconds; ordinary footage resumes around 146.63 seconds: roughly 32.7 seconds at the endpoint. The rendered pitch, yaw and FOV remain constant throughout the journey, so its eastward travel continues looking west. The route's aim follows a fixed offset rather than looking toward its destination.

The old destination was an artificial point projected along a heading. Ordinary replacement planning stopped while Journey owned coverage, leaving a cold search after continuation failed. Journey distance diagnostics also rejected the preceding completed render during the next Update, so actual travel was reported as zero. These are separate design and integration defects; they are not evidence that the game cannot support a longer flight.

## Resulting behavior

| Area | 0.8.42 contract |
| --- | --- |
| Destination | Select an identified landscape/coast/site from the current discovery catalogue. A bounded itinerary links known observations; disconnected or stale metadata does not become an invented route. Natural destinations remain competitive with structures, and recent destination families are remembered. |
| Distance control | `JourneyDistanceMetres` requests an itinerary scale. Selection accepts an observed route around that scale; it no longer creates a point exactly that distance away. The default remains 1,000 m at an independent 6 m/s cruise. |
| Departure | Prepare an independently framed forward-facing cut, its locally validated route and its ending while ordinary coverage continues. Inherited backward composition cannot determine the new Journey's gaze. |
| Movement and gaze | Native camera distance and a separately evaluated aim path follow the changing itinerary. Continuations preserve the accepted seam; approach/arrival gaze can develop toward an observed destination subject. |
| Ending | Every admitted section includes an independently validated six-second ending. If a successor is not ready before braking begins, commit to that ending; a late successor cannot cancel braking. Proof refresh preserves the exact ending and revokes it when new observations contradict it. Ordinary local preparation continues in the background and only off-horizon work is discarded. |
| Completion | A locally observed subject supplies a forward approach to a separated stand-off viewpoint inside the destination area. Require actual rendered travel, progress along the selected itinerary, proximity to the selected destination, and readable arrival presentation. A partial, hidden or blocked arrival is not completed. |
| Evidence | Consume one immutable matched capture from the current or preceding render, using its captured position, progress and clock. Held, stale, missing, duplicate or mismatched identities earn no distance. A missing frame breaks continuity without fabricating a newer timestamp. |

Catalogue observations establish **where to try going**, not clearance of the distant world. Terrain, water, foliage, native camera/aim checks and final live admission remain authoritative for each local section and ending. A route may end early or be interrupted by player coverage. The change does not promise an uninterrupted kilometre through unloaded or obstructed geometry, or eliminate ordinary relocation loading gaps.

## Compatibility and delivery

Replace the three client DLLs and included ThirdParty notices together; preserve configuration. Existing protocol-3 companions remain compatible, with their existing discovery capabilities. This introduces no protocol revision, server permission, game dependency or fixture schema change. Replay continues to read 0.8.29–0.8.42 traces. Existing fixtures and timelapse/environment controls retain their contracts.

## Runtime acceptance — not yet run

Enable synchronized capture, then request a journey during protected-observer Scenic coverage:

```text
broadcast capture on
broadcast camera dynamic
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
broadcast journey status
```

Record an open coast/ridge, a turning itinerary, a constrained forest route, and a destination containing an identifiable feature. Verify the selected destination name, forward/developing gaze, measured distance and speed, continuous section seams, prepared ending and actual arrival coverage. Repeat with a held frame, missing presentation, exhausted continuation, player takeover and world change. Preserve matching footage, Scenic JSONL and BepInEx logs. A blocked journey must retain its real distance/reason and hand back to ordinary coverage without the former cold-search endpoint stall.

Core and native-camera tests cover their executable policies and geometry; build/test results are recorded in [RUNTIME-TESTS.md](RUNTIME-TESTS.md). They do not execute Valheim's full loading/render loop or establish the visual quality of 0.8.42. **No 0.8.42 in-game visual validation is claimed.**
