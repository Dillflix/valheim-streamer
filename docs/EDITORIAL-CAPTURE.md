# Editorial coverage and synchronized capture — 0.8.34

This client update builds directly on 0.8.33 (`3a3f425dd1e70e521cff09e8aee3b807bea75320`). It adds local footage paired with diagnostics, independently admitted Scenic pictures, observed trader/site sequences, and evidence-based Player edits. Observer admission remains protocol 3; no new RPC or mandatory companion update is introduced. The existing 0.8.33 companion remains required for the complete landscape/destination metadata feed.

Replace the three owned client DLLs and included Cinemachine notices together, preserving configuration. This change does not install into a running game or modify saves. Compilation and policy/encoder tests do not establish in-game picture quality; the runtime checks below remain required.

## Capture the picture and its decisions

`Diagnostics.VideoCapture` defaults to `true`. Recording runs while broadcasting in a world and trace capture is enabled, including when RTMP publishing is off. It uses the existing `Streaming.FFmpegPath`, a separate single-threaded software encoder, and the clean program compositor. Local diagnostic video is **640×360 at 12 fps, without audio**. Live RTMP retains its existing H.264/AAC game-audio output.

```text
broadcast capture status
broadcast capture off
broadcast capture on
```

`off` disables the setting and finalizes recording. `on` enables it, starts tracing if necessary, and retries a latched capture failure. `broadcast trace off` also prevents video recording. The status/log message reports the actual directory, frame/drop counts and capture failure. An unavailable encoder, GPU readback, storage or orientation calibration stops diagnostic video without stopping the live broadcast.

Video uses fragmented MP4 so normal finalization is not required to preserve all already-written fragments. Files belong to the broadcaster's active BepInEx installation/profile:

```text
BepInEx/diagnostics/ValheimBroadcaster/scenic-*.jsonl
BepInEx/diagnostics/ValheimBroadcaster/footage/footage-*.mp4
BepInEx/diagnostics/ValheimBroadcaster/footage/footage-*.jsonl
```

For Sandboxie, collect these from the sandboxed broadcaster's installation. Stop capture or exit normally after the relevant sequence, then preserve the diagnostic directory and matching `BepInEx/LogOutput.log` before rotation removes them. No automatic upload occurs. Video contains the visible gameplay; metadata includes camera/world coordinates. Review files before sharing.

The video ring retains at most eight paired segments, each up to 300 frames (25 seconds). Encoded video has an exact 8 MiB per-segment limit; metadata is bounded to 3 MiB. Rotation reserves space within a 96 MiB footage allowance and touches only owned `footage-*` pairs. The existing Scenic trace has its separate bounded ring. Capture uses one asynchronous readback, an eight-frame queue and a fixed ten-buffer pool; this bounds storage/memory, not measured GPU or encoder cost.

Each video frame maps to its segment/frame index and playback time, capture clock, source render frame/time, compositor generation, live/held/slate presentation, shot/scene identity, camera pose/lens, mode, perspective and reason. The matching trace session/sequence identifies the decision record. Source metadata belongs to the rendered picture, including held footage; it is not taken from a later GPU callback's current shot.

Short missing capture intervals can repeat the preceding diagnostic frame to preserve timing; these carry `diagnosticRepeat=true` and do **not** claim the live broadcast held. Gaps over 0.25 seconds and compositor-generation changes start new segments. Failure/incomplete segments are marked explicitly. This 12 fps evidence can miss brief render frames; it is not a frame-exact recording of every live output frame or full Unity scene replay.

Create a synchronized local review page from a source checkout:

```text
python tools/review-footage.py "path/to/ValheimBroadcaster/footage" --verify
```

This writes `footage-review.html` beside the recordings. It plays the MP4, steps through recorded samples, displays source/capture/playback timing and shows nearby trace records matching the frame's trace session and sequence. Keep the parent `scenic-*.jsonl` files with the footage; missing or rotated trace neighbours are reported explicitly. Videos are linked locally, not copied or embedded, so retain the relative directory layout when moving the report.

`--verify` uses `ffprobe` to compare decoded video-frame counts with the sidecar; specify `--ffprobe "path/to/ffprobe.exe"` if it is not on PATH. `--output` selects another HTML destination. The page distinguishes encoder-confirmed completion, independently checked frame counts and incomplete segments. It supplies inspection evidence, not an automatic cinematography-quality verdict, and uploads nothing.

## Independent Scenic pictures

A measured, readable viewpoint can be admitted before a complete moving route is available. Terrain/water, near plane, foliage, loaded-scene and subject checks still apply. A failed route does not invalidate its valid viewpoint; background cinematic planning continues while that live picture is presented.

An independent picture normally receives at least four seconds of actual readable presentation before yielding to a prepared route. Equivalent viewpoints are remembered, and a readable picture is not replaced on every newly discovered planning node. After a failed moving-route search and 24 seconds of accumulated settled footage, the director can request normal content-exhaustion travel while retaining the valid picture until travel actually begins. It does not report that departure as missing scene data. Held textures and unshown candidates earn no footage credit.

## Site and NPC coverage

The local survey recognizes active `Trader` components using the exact Haldor, Hildir and Bog Witch name tokens. It measures the trader's renderers and nearby loaded static surroundings, keeping the contextual site and NPC detail associated. A remote landmark label alone cannot create an NPC subject, and this update does not discover arbitrary modded NPC identities.

Six seconds of readable establishing context qualifies related NPC/detail development. Site memory favours relevant unshown details before equivalent repeats. Existing Building → District → Settlement relationships remain available, and local context stays explicitly partial. Trader portraits additionally require clear upper-body samples; the broad structural-subject allowance cannot make an intervening wall count as the trader. These are geometry/identity observations, not semantic image recognition or proof that an entire camp or settlement has been surveyed.

The client does not instantiate actors, activate dormant creatures, take ownership or change world conditions.

## Player action phases and complementary views

Observed ranged draw, attack animation, health loss, sustained movement stopping, work and post-engagement quiet influence editing. Important observed moments and doorway transitions protect the current readable shot from ordinary editorial changes. A draw ending receives follow-through without claiming a projectile release or hit; quiet after engagement does not establish victory or death. Stale/missing evidence resets the phase.

A related, already-loaded opponent/quarry can receive a short complementary view after established player coverage. The current implementation requires an open, connected scene, fresh relationship evidence, a validated complementary pose, compatible action-axis geography and a freshly solved player return. It adds virtual-camera solves, not another rendering camera or an acquisition/travel target.

Complementary footage is bounded to **2.75 seconds actually shown**, **four seconds wall time**, and a **30-second rearm**. Important new action, lost evidence or an unavailable return ends the insert. The player remains the selected story owner; an opponent insert cannot earn player-viewing or rotation credit. Broader ally/reaction/environment handoffs remain outside this implementation.

## Runtime acceptance

| Scenario | Required evidence |
| --- | --- |
| Capture with RTMP on/off, operator console visible, live/held/slate output and travel | Correct clean picture and orientation; matching frame/shot metadata; unchanged live audio; no stale pre-travel image |
| Disable/re-enable capture, remove FFmpeg, fill its storage allowance, exit/rejoin | Failure remains isolated; bounded files/queues; capture can be retried; old callbacks cannot contaminate the new session |
| Dense forest and a site with a readable viewpoint but no usable moving route | A valid live picture appears independently; it stays readable while planning continues; settled coverage does not become an endless fixed view |
| Haldor/Hildir/Bog Witch, a large base, coast, destroyed or unavailable landmark | Actual context before detail, recognizable subject when observed, honest partial coverage and preserved natural/biome variety |
| Bow draw/cancel/release, melee, doorway, stopping after travel | Important visible actions survive ordinary edit boundaries; no invented outcomes; visibility recovery still operates |
| Related enemy/quarry insert, actor loss, player movement and lost return view | Brief connected perspective, clear return, no acquisition teleport or false player coverage credit |

Compare captured sequences for unavailable-picture time, sustained obscuration, unnecessary cuts, movement/FOV discontinuities and CPU/GPU frame cost. Local tests validate decisions and media transport; these observations validate the broadcast.
