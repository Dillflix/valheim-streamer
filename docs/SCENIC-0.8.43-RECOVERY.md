# Scenic recovery repair — 0.8.43

## Recorded regression

The user reports that ordinary Scenic coverage worked before 0.8.42 and repeatedly displayed recovery slates after that update. This is a 0.8.42 runtime regression. The uncertainty is the precise release change that triggered it, not the reported before/after behavior.

The supplied 0.8.42 capture contains 517 video-mapped frames: 492 slate frames and 25 live frames. The first live picture arrives about 41.8 seconds after the first recorded region setup, following two recovery relocations. No Journey starts during that interval. The preceding 0.8.41 trace acquires its first live picture about seven seconds after region setup.

Two concrete failure paths are present in the 0.8.42 evidence:

1. A spatial query finds a readable viewpoint at 56.789 seconds. Independent picture admission checks that same pose with a separate, initially empty foliage projection. Admission consumes the proposal after one attempt even when the result is incomplete evidence. Its projection only advances when another spatial node is offered. The 2,212-entry catalogue needs at least nine collection slices, plus refinement; the nine offered candidates never complete admission before recovery travel. A clear viewpoint therefore fails to become a picture.
2. At the next region setup, the shared catalogue continues traversing the previous region. It publishes that obsolete scan before starting the destination scan. The destination catalogue appears about eleven seconds after region setup, after the local recovery budget has already been consumed.

The adjacent release source trees and packaged assembly identities were compared. The exact triggering 0.8.42 source change has not been isolated. These observations establish the failed scheduling and ownership paths; they do not justify attributing the failure to Journey geometry, an incompatible server, or the user's installation.

## Repair scope

Independent picture admission now retains one exact-pose proposal and advances its work each eligible frame, separately from cinematic graph search. A new graph node cannot replace unfinished admission. Full current terrain, water, near-plane, acquisition, carrier, framing and foliage checks still decide acceptance. Only a completed validation supplies fresh evidence to picture availability. World/region changes, invalidation and a bounded proposal lifetime cancel pending work.

The scene-owned foliage catalogue now cancels an obsolete traversal when an authoritative region/carrier anchor leaves the running survey’s usable coverage. It retains the last published observations conservatively while starting the new region. Unknown additions discovered before cancellation remain unknown, so cancellation cannot make a stale view falsely clear. A still-useful sweep survives ordinary sustained movement, and completed catalogues retain the existing 96-metre reanchor threshold. Loaded-region discovery also advances under its existing work budget while feature collection is still in progress, before a camera candidate exists.

The ten-second missing-picture deadline remains unchanged. A pending proposal does not reset it, count as footage, or prevent normal recovery travel. Player selection, observer authorization, simulation isolation and RTMP transport retain their existing behavior. No server update is required; observer protocol remains 3.

## Validation boundaries

Deterministic regression cases exercise the same admission and recenter decisions used by production, including sparse candidate arrivals, collection spanning multiple frames, invalidation, real obstruction, and obsolete in-progress traversal. They do not run Unity scene traversal, physics or rendering. Client/server/replay compilation against the project's real pinned Valheim/Unity references passed with warnings treated as errors. Core tests passed 1,132/1,132 and native math tests passed 94/94. Replay reproduces 4,893 decisions in the supplied failed capture with zero divergences; this confirms evidence interpretation, not corrected rendered output.

In-game validation of 0.8.43 is pending. The next paired footage/trace capture should establish that a pending first picture keeps the same identity until accepted or explicitly rejected; that a relocation survey targets the new region immediately; and that the first usable picture reaches the program feed. Dense foliage must still reject genuinely obscured views. The existing automatic capture records this without repeated console commands.

## Client installation

Exit the broadcasting client, replace the three owned client DLLs and included notices from the 0.8.43 client ZIP, preserve the existing configuration, and relaunch through the same profile. The existing protocol-3 server companion can remain installed. The package is a tested code repair awaiting game-runtime confirmation, not a claim that the supplied failed capture has become successful footage.
