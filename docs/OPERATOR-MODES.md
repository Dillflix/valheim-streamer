# Operator coverage modes — 0.8.44

While normal broadcasting is active, open F5 and enter:

```text
broadcast mode scenic
```

Scenic coverage now runs even when another player is online and standing nearby. Player acquisition and creature cutaways are excluded; automatic scenic destination travel remains available under the existing travel settings and permissions. No manual teleport or player logout is required.

| Command | Programme intent |
| --- | --- |
| `broadcast mode auto` | Restore normal player priority, rotation, scenic fallback and eligible wildlife cutaways. |
| `broadcast mode scenic` | Scenic shots only, including enabled journeys and automatic scenic travel. |
| `broadcast mode player` | Follow/rotate players without wildlife cutaways. If no player is ready, retain safe scenic/recovery fallback while acquisition continues. |
| `broadcast mode creature` | Prefer eligible, loaded nearby creatures immediately. Use scenery if none can be shown; retain normal creature cooldowns and camera-readiness checks. No creature spawning or remote creature acquisition is added. |
| `broadcast mode status` | Show the requested preference, actual coverage and automation state. |

The operator overlay separately labels **MODE** and **ON AIR**. A Creature request with Scenic on air is an explicit fallback, not a claim that wildlife footage exists. Discovery still reports the actual connected players, and all server authorization and environment permissions use the real roster.

Selecting a mode releases operator hold and resumes automatic direction in an authorized remote observer session. This also recovers from the old `broadcast auto off` workaround. Existing `ScenicTravel=false` remains respected. Ordinary clients can choose local coverage, but gain no remote-travel authority.

`broadcast auto off` pauses automatic acquisition/travel and holds the shot without clearing the mode. Use `broadcast resume` to allow local camera editing again, or `broadcast auto on` to resume travel under the selected mode. Use **`broadcast mode auto`** to restore automatic coverage selection itself.

An active native teleport finishes normally before the selected mode is applied on arrival. Mode selection ends an explicit solo route test. Player/Creature selection interrupts an active Scenic fixture, Journey or live timelapse. Explicit `follow`, `next` and `previous` commands return to Player intent when issued from forced Scenic/Creature coverage. These commands do not stop or restart RTMP.

The preference belongs to the current broadcast session. Region changes preserve it; stopping/parking or leaving the world returns it to Auto. Self-follow and probe tests retain their own controls and reject mode selection until normal broadcasting resumes.

## Installation and validation

Replace the three client DLLs and included notices from the 0.8.44 ZIP while the client is closed. Preserve configuration. No server update is required; observer protocol remains 3.

Local client/server/replay compilation passed against the pinned real Valheim/Unity references with warnings treated as errors. **1,142 Core tests and 94 native math tests passed.** New cases exercise Scenic with ready/distant connected players, continued destination travel, native-travel handoff, return to Auto, Creature eligibility and failed-view cooldown, Player fallback, and preference lifetime. These checks do not execute Unity's console or render loop.

In-game validation remains pending: with another player nearby, select Scenic and verify that the player no longer takes over, local shots continue, and later automatic scenic travel still occurs. Switch back to Auto and verify player acquisition resumes without restarting the stream. Check Creature with and without eligible nearby actors, and issue a mode change during travel to verify that arrival completes normally.
