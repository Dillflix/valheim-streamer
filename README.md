# Valheim Broadcaster

A Jötunn/BepInEx mod for a dedicated broadcasting client: follow another player's character with a wide camera view, cycle targets automatically, and capture the normal game window in OBS.

**0.1.0 is a development prototype.** The director's 14 automated cases pass, and the plugin compiles against real Valheim 1.0.21 reference assemblies. In-game startup, camera behavior, multiplayer streaming, and OBS capture still need the checks in [RUNTIME-TESTS.md](docs/RUNTIME-TESTS.md). Ordinary players and the server do not need this mod for the implemented client-only mode.

The broadcaster remains an ordinary connected player. This mod does not provide invisibility, invulnerability, teleportation, input suppression, or protection from gameplay effects. Use a separate observer character parked safely in a disposable test world. The default mode follows player objects already loaded on that client; it does not promise to find every online player across the world.

## Implemented behavior

- Configurable camera distance, height, FOV, world-space azimuth, and follow smoothing.
- Obstruction checks between the target and camera.
- Stable-ID target selection, next/previous, timed rotation, hold, and disconnect fallback.
- A waiting slate while no eligible target is loaded, with a timeout that skips unavailable targets when alternatives exist.
- HUD hiding and restoration; matching main/sky camera FOV; restoration of camera state when broadcasting stops or the observer dies.
- An optional **experimental** distant-target mode using public map positions and the client's network reference position. It starts disabled and has no server-side position relay.

The mod controls the main camera; it does not create a second rendered view, encode video, or upload a stream. OBS performs capture and streaming on the broadcaster machine.

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

## Controls and console

Use Valheim's `-console` launch option to make the F5 console available. Commands are also useful when hotkeys conflict with another mod.

| Command | Effect |
| --- | --- |
| `broadcast on` / `broadcast off` | Start / stop the camera mode |
| `broadcast next` / `broadcast previous` | Select the next / previous eligible target |
| `broadcast hold` / `broadcast resume` | Pause / resume automatic cycling and load-timeout skipping |
| `broadcast list` | Refresh and print eligible target IDs and names |
| `broadcast follow <id>` | Select an exact listed ID and hold it; start with `broadcast on` first |
| `broadcast status` | Show director state, target, hold/auto mode, and roster count |
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
| `Director / TargetTimeoutSeconds` | `20` | Wait before skipping an unavailable target |
| `Display / HideHud` | `true` | Hide the ordinary HUD during broadcast |
| `Display / ShowStatus` | `true` | Show the director overlay |
| `Experimental / RemoteTargets` | `false` | Include public distant positions and change the network reference position |
| `Controls / Toggle`, `Next`, `Previous`, `Hold` | `F8`, `PageDown`, `PageUp`, `F9` | BepInEx keyboard shortcuts |

Holding an unavailable target deliberately keeps the waiting slate; resume or select another target to recover. A lone unavailable target is retained rather than repeatedly reselected.

For `RemoteTargets=true`, test first with two players sharing their map positions in widely separated areas. The client waits for both a target scene object and local area readiness. These signals do **not** prove complete server delivery or harmless observer behavior. Follow the remote test matrix before relying on this mode.

## Capture

On Windows, start with OBS **Game Capture → Capture specific window**, selecting Valheim. Use the appropriate window/screen capture source on other platforms. Disable `ShowStatus` for a clean shot. Test focus changes, waiting slates, audio, and reconnects before streaming; the mod does not manage OBS or suppress game menus and other mods' overlays.

A VM needs working accelerated 3D rendering. Hardware encoding is preferable, and the VM must be able to access the game window and audio device. Streaming uses a normal authenticated game client; this project does not change platform authentication or game licensing.

See [architecture](docs/ARCHITECTURE.md), [runtime checks and evidence](docs/RUNTIME-TESTS.md), and [implementation sources](docs/SOURCES.md).
