# Valheim Broadcaster

A Jötunn/BepInEx mod for a dedicated broadcasting client: follow another player's character with a wide camera view, cycle targets automatically, and stream the rendered game view with game audio directly to an RTMP receiver.

**0.4.0 is a development prototype.** The director and real FFmpeg loopback tests pass, and the plugin compiles against real Valheim 1.0.21 reference assemblies. In-game startup, camera/audio capture, multiplayer streaming, and performance still need the checks in [RUNTIME-TESTS.md](docs/RUNTIME-TESTS.md). Ordinary players and the server do not need this mod for the implemented client-only mode.

The broadcaster remains an ordinary connected player. This mod does not provide invisibility, invulnerability, teleportation, input suppression, or protection from gameplay effects. Use a separate observer character parked safely in a disposable test world. The default mode follows player objects already loaded on that client; it does not promise to find every online player across the world.

## Implemented behavior

- Configurable camera distance, height, FOV, world-space azimuth, and follow smoothing.
- Obstruction checks between the target and camera.
- Stable-ID target selection, next/previous, timed rotation, hold, and disconnect fallback.
- Continuous coverage: ready players take priority; otherwise alternate a local scenic orbit and nearby loaded creatures. A recovery slate appears only when no ready shot can be rendered.
- Separate connected-player presence and scene readiness, with bounded experimental remote acquisition attempts.
- HUD hiding and restoration; matching main/sky camera FOV; restoration of camera state when broadcasting stops or the observer dies.
- An optional **experimental** distant-target mode using public map positions and the client's network reference position. It starts disabled and has no server-side position relay.

The mod captures the existing framebuffer and game audio listener, then manages a local FFmpeg process to publish H.264 video plus stereo AAC audio. No OBS, second rendered camera, microphone capture, or server mod is required. FFmpeg is a separate executable you supply. An embedded HLS/DASH web server is not implemented; a remote media server can convert the RTMP feed for browser playback.

## Build

Install the .NET 8 SDK, a matching Valheim installation, BepInEx 5, and Jötunn. The project targets .NET Framework 4.8 and pins its compile-time Jötunn package to **2.30.2**. Match runtime dependencies to the build; the installed game version is part of the test record, not inferred from this package number.

Run the director tests without game files:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

Build the plugin on Windows PowerShell:

```powershell
dotnet build src/ValheimBroadcaster.Plugin -c Release "-p:ValheimDir=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Or on Linux:

```sh
dotnet build src/ValheimBroadcaster.Plugin -c Release -p:ValheimDir="/path/to/Valheim"
```

For a mod-manager profile or separate assembly directory, specify both paths explicitly:

```sh
dotnet build src/ValheimBroadcaster.Plugin -c Release \
  -p:ValheimManagedDir="/path/to/valheim_Data/Managed" \
  -p:BepInExCoreDir="/path/to/profile/BepInEx/core"
```

The build requires real game and loader assemblies. It reads those files, excludes Jötunn's build hooks, and has no automatic installation step. Do not commit or distribute the referenced game/Unity/BepInEx/Jötunn DLLs.

GitHub Actions runs the director tests and a separate full plugin build against a pinned Valheim 1.0.21 baseline. The build job obtains real references from the free official dedicated-server depot using a checksummed downloader; it never launches the game. Successful runs provide a `valheim-broadcaster-<commit>` artifact containing only this project's two DLLs. These are test builds, not runtime-validated releases. Download them from the [Actions page](https://github.com/Dillflix/valheim-streamer/actions), or build locally against your own matching client assemblies.

Expected output directory: `src/ValheimBroadcaster.Plugin/bin/Release/net48/`.

## Install and try locally

1. Use a separate test profile with BepInEx 5 and Jötunn 2.30.2 available. Close that client before changing plugins.
2. Copy **only** `ValheimBroadcaster.dll` and `ValheimBroadcaster.Core.dll` from the build output into `BepInEx/plugins/ValheimBroadcaster/` in that profile.
3. Start Valheim, join the test world, and park the observer character safely near another player. Broadcasting starts disabled.
4. Press **F8** to start. Use **PageDown** / **PageUp** to cycle and **F9** to hold or resume. Press **F8** again to restore the normal camera.
5. Check `BepInEx/LogOutput.log` for the plugin startup line, state changes, or errors. Validate restoration before capturing a session.

Hotkeys are ignored while typing in chat/console or using the recognized menu, inventory, and text-input screens. The observer's normal gameplay controls remain active.

To remove the prototype, close the client and remove its two DLLs. Keep any existing configuration and test records. Do not remove dependencies used by other mods.

## Solo camera test

No second player or server mod is needed. This works in a local single-player world or while you are alone on your server.

1. Install the two DLLs from the latest test build into your active mod profile. With Thunderstore, open that profile folder and copy both into `BepInEx/plugins/ValheimBroadcaster/`, then launch modded as usual. No console launch argument is required.
2. Enter a test world with a living character and press **F10**. Alternatively, with the console enabled, run `broadcast self` and close the console.
3. Walk, run, jump, and move near walls or trees. The same broadcast camera used for other players now follows you, exercising framing, smoothing, obstruction checks, main/sky FOV, HUD hiding, and direct stream capture.
4. Press **F8** or run `broadcast off` to stop and restore the normal view. Run `broadcast on` to switch directly back to ordinary player targeting; alone, that mode starts scenic/creature coverage.

Self-follow is session-only and always shows a `SELF-FOLLOW TEST` status label, even when `ShowStatus=false`. Its roster contains only you, so next/previous and timed cycling stay on you. Other players joining will not become test targets. Distant-streaming overrides are bypassed even if `RemoteTargets=true`; entering self-follow restores any previous override. Death, world exit, and plugin disable stop the test. Starting again with F8 uses normal mode; press F10 or run `broadcast self` to re-enter the test.

Your normal movement controls remain active. This mode tests actual camera rendering with your own character; it does not validate remote-player replication, multi-target shots, or distant world loading.

## Controls and console

Use Valheim's `-console` launch option to make the F5 console available. Commands are also useful when hotkeys conflict with another mod.

| Command | Effect |
| --- | --- |
| `broadcast self` | Start solo testing with your own character as the only target |
| `broadcast on` / `broadcast off` | Start normal other-player mode / stop either mode |
| `broadcast next` / `broadcast previous` | Select the next / previous eligible target |
| `broadcast hold` / `broadcast resume` | Pause / resume automatic cycling and load-timeout skipping |
| `broadcast list` | Refresh and print eligible target IDs and names |
| `broadcast follow <id>` | Select an exact listed ID and hold it; start with `broadcast on` first |
| `broadcast status` | Show director state, target, hold/auto mode, and roster count |
| `broadcast stream on` / `off` / `status` | Start/stop RTMP audio + video / inspect encoder state; camera must be active |
| `broadcast anchor` | Reset the session scenic anchor to your character’s current position in normal mode |
| `broadcast help` | Print command usage |

Names are labels; selection uses character ZDO IDs. IDs are session-specific and should not be saved as permanent player identities.

## Configuration

After the first launch, close the game and edit `BepInEx/config/com.dillflix.valheimbroadcaster.cfg` in the active profile. Restart to apply the edited file.

| Section / key | Default | Meaning |
| --- | --- | --- |
| `Camera / Distance` | `18` | Horizontal metres from target |
| `Camera / Height` | `10` | Metres above the focus point |
| `Camera / FieldOfView` | `75` | Vertical degrees |
| `Camera / Azimuth` | `225` | Fixed world-space viewing direction |
| `Camera / Smoothing` | `5` | Follow response per second |
| `Director / DwellSeconds` | `30` | Ready footage before advancing |
| `Director / TargetTimeoutSeconds` | `20` | Maximum experimental remote acquisition attempt |
| `Director / AcquisitionCooldownSeconds` | `30` | Local-interest interval between remote attempts |
| `Idle / ScenicSeconds` | `45` | Scenic dwell before trying a creature |
| `Idle / CreatureSeconds` | `25` | Creature dwell before returning to scenery |
| `Idle / CreatureRadius` | `50` | Loaded-creature search radius around the anchor (metres) |
| `Idle / OrbitDegreesPerSecond` | `2` | Scenic orbit speed; zero holds the angle |
| `Display / HideHud` | `true` | Hide the ordinary HUD during broadcast |
| `Display / ShowStatus` | `true` | Show the director overlay |
| `Experimental / RemoteTargets` | `false` | Include public distant positions and change the network reference position |
| `Controls / StreamToggle` | `F11` | Start/stop RTMP audio + video |
| `Controls / SelfTest` | `F10` | Start self-follow test mode |
| `Controls / Toggle`, `Next`, `Previous`, `Hold` | `F8`, `PageDown`, `PageUp`, `F9` | BepInEx keyboard shortcuts |

Only ready players enter the rotation. Hold pauses player cycling or idle timing/orbit; a lost subject still falls back, and a newly ready player takes priority over idle. Connected but unavailable players remain visible in the status count while the mod rechecks their scenes.

For `RemoteTargets=true`, test first with two players sharing their map positions in widely separated areas. The client waits for both a target scene object and local area readiness. These signals do **not** prove complete server delivery or harmless observer behavior. Follow the remote test matrix before relying on this mode.

## Direct RTMP video and audio

See [STREAMING.md](docs/STREAMING.md) for Thunderstore setup, receiver testing, configuration, and troubleshooting. The short version: install an FFmpeg executable with `libx264` and `aac`, set `Streaming.FFmpegPath` and `Streaming.RtmpUrl` in your active profile's config, launch modded, press **F10** for solo follow, then **F11** to publish. F11 stops the stream; F8 stops both the camera and stream. Streaming never starts automatically.

A VM needs accelerated 3D rendering and a working audio device, even if no physical speakers are attached. This first encoder uses software H.264, so budget CPU as well as GPU/capture overhead. Streaming uses a normal authenticated game client; this project does not change platform authentication or game licensing.

See [architecture](docs/ARCHITECTURE.md), [runtime checks and evidence](docs/RUNTIME-TESTS.md), and [implementation sources](docs/SOURCES.md).

## Continuous coverage and solo idle test

Press **F8** while alone to test normal idle coverage (F10 is still the explicit self-follow test). Your character's position at activation becomes the session scenic anchor. The camera orbits it slowly for 45 seconds, follows a suitable nearby loaded creature for 25 seconds, then returns to scenery. With no creature, the orbit continues. Wildlife, tamed animals, and enemies are eligible; the mod does not spawn or move them.

When other players become ready, coverage switches to them and rotates if several are available. When none remain ready, idle resumes. Automatic transitions keep the same RTMP session alive. Press **F11** to publish as before. Use `broadcast anchor` to move the scenic anchor to your character's current location without restarting the stream. The anchor is cleared on stop/world exit and is not persisted across worlds.

[IDLE-DIRECTOR.md](docs/IDLE-DIRECTOR.md) documents the implemented policy, acquisition limits, and remaining unattended-operation work. Default coverage stays within loaded scenes; arbitrary world roaming, observer protection, and automatic reconnection are not implemented.
