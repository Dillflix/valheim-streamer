# Live Scenic timelapse — 0.8.38

This is a shot in the live programme: restrained camera motion while the sun, lighting and shadows pass through a visual day/night cycle. RTMP carries the shot directly with normal live game audio. It neither needs an MP4 recording nor accelerates the shared simulation, creatures or player actions. Weather, cloud simulation and wind remain natural; this implementation accelerates the visual solar phase.

## Selection and execution

Enabled by default (`ScenicTimelapse.Enabled=true`). After 45 seconds of available, readable Scenic coverage, the director can select an early portion of a measured coast, ridge or clearing shot. Journeys and saved operator fixtures retain their ownership; a timelapse cannot interrupt those. Completed/interrupted episodes get a five-minute cooldown. An explicit request can bypass programme spacing, but never authorization, geometry or ownership requirements.

The camera takes 90 seconds by default (configurable 45–180) to develop a short, restrained route segment. Candidate travel is bounded to eight metres, and three overhead collision observations reject obvious roof/canopy coverage. These queries share the camera work budget. This is a conservative outdoor eligibility test, not a semantic guarantee of visible sky or interesting lighting in every weather condition. Full terrain/water, foliage, native spline and final-pose validation still apply before admission. Physics-only overhead observations do not replace foliage validation.

The existing validated camera pipeline owns geometry, aim and lens. The live timelapse policy owns episode timing, selection and environment intent. Actual matching readable frame progress owns the accelerated phase; preparation and unrelated/held frames cannot advance it. The visual cycle eases into/out of acceleration and rejoins the **current** natural phase, including world-clock advancement during the shot. A deliberate operator hold pauses accelerated progression; natural time can continue normally.

Preparation is bounded by the existing 20-second route deadline and a 25-second episode guard. Three seconds of missing usable output ends the episode, and normal recovery remains active. Pixel/geometry readability concerns can still end a shot, including an overly dark night portion. The director does not suppress those safeguards to force completion.

## Authorization and restoration

Requires an authorized protected observer with procedural movement enabled. On a dedicated server, the **0.8.37-or-later companion's Environment1 capability** must grant fresh empty-server permission. Ordinary and joining players count; the 2.5-second request-bound lease and revocation remain unchanged. Older protocol-3 companions retain ordinary Scenic coverage but cannot enable these shots. No new server capability or observer protocol is introduced in 0.8.38.

Player coverage, travel, world exit, permission loss, disabled procedural movement, manual environment ownership or replacement by another camera owner ends the episode. Restoration also runs at the final camera hook before rendering a different coverage/shot. Normal completion rejoins natural time smoothly; interruption prioritizes immediate restoration and may visibly change lighting.

Manual `broadcast environment` controls take precedence. Turn manual overrides off before requesting an automatic timelapse. Another mod/operator's changes remain respected by the existing environment ownership logic. Closed offline observer sessions are supported, with the existing caveat that local EnvMan day/night classifications can affect local behavior even though shared world time is unchanged.

## Commands

```text
broadcast timelapse status
broadcast timelapse start
broadcast timelapse duration 90
broadcast timelapse stop
broadcast timelapse off
broadcast timelapse on
```

`start` requests the next eligible live shot. It does not force an immediate cut or create a video file. `stop` ends the current request/episode and preserves automatic selection after its cooldown; `off` disables automatic timelapses. The operator overlay and `broadcast diagnostics` identify preparation, live progress, composition and waiting reasons.

The previous independent interval recorder is still available under an explicit subcommand:

```text
broadcast timelapse record start 2 600 30
broadcast timelapse record status
broadcast timelapse record stop
```

## Install

Replace all three client DLLs and included notices with Valheim stopped; preserve configuration. Existing 0.8.37 server/core supports the complete live feature. A version-aligned 0.8.38 server package contains the same Environment1 behavior, and is optional when 0.8.37 is already installed. Ordinary players need no mod changes.

## Runtime acceptance

Automated state, timing and environment math tests plus real-reference compilation are separate from these unperformed in-game checks:

1. Alone on a compatible dedicated server, request a timelapse over a coast/ridge/clearing. Confirm slow coherent camera travel and visible lighting/shadow progression in the RTMP feed with normal audio.
2. Observe start/end at different natural phases. The initial phase should match the preceding view and the normal endpoint should match advancing natural world time, without a reset flash.
3. Hold and resume; confirm no accelerated-time catch-up. Obstruct the view or cause a scene gap; confirm bounded interruption and existing recovery.
4. Join from an ordinary account during preparation and during the shot; confirm restoration and Player priority. Repeat travel/return/disconnect and manual fixture/environment takeover.
5. Confirm natural weather, creatures, shared server time and ordinary clients remain unaffected by accelerated visual time.
6. Leave automatic coverage running to check variety and spacing among ordinary Scenic shots, journeys and timelapses. Review paired footage/diagnostics for actual native sky smoothing, exposure and frame cost.
