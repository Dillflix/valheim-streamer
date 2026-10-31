# Validation and runtime test record

## Evidence available

- The director's **14 automated cases passed**, including observer exclusion, identity ordering, manual selection, wrapping, dwell, hold, target loss, readiness timeout, lone-target behavior, and invalid time inputs.
- The core compiled against actual .NET Standard 2.0 reference assemblies with warnings treated as errors. Its executable test harness ran on .NET 8 against that compiled core.
- The development container's ordinary `dotnet`/MSBuild path fails during process introspection (`Process.StartTime` / `ProcessName`). Direct invocation of the SDK's Roslyn compiler was used for the observed core checks. This is an environment limitation, not a replacement for checking the normal project build in CI or on a development machine.
- The plugin compiled with warnings treated as errors using Roslyn 4.11 from .NET SDK **8.0.427**, actual Microsoft .NET Framework 4.8 reference assemblies (**1.0.3**), Jötunn **2.30.2**, BepInEx **5.4.23.2**, and real Valheim references. This checked the source against those assemblies; it did not establish that the normal MSBuild graph executes successfully in this container.
- Real game references were obtained from the official Steam dedicated-server app **896660**, public Windows depot **896662**, manifest **171106503873955013** dated **2026-10-27**. Assembly metadata inspection found `Version` initializes `GameVersion(1, 0, 21)`, identifying this reference build as **Valheim 1.0.21**. Only reference DLLs were downloaded; no server was launched. These server references do not prove that a separately installed client matches.
- Jötunn's NuGet package identifies upstream commit `671c486ac8123a9b240eae80639b6c33f63b3884`. No client runtime result is implied by a dependency's availability or compilation success.
- The initial implementation has **not** been installed into or run in Valheim. No gameplay, dedicated-server coexistence, stream quality, or GPU-performance pass is recorded.

Run the normal core check with:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

CI checks this same core harness. A green core job does not compile or run the game adapter.

## Record the target before testing

Use a disposable world and character, a separate mod profile, and an unchanged copy of any world needed for comparison. Record:

| Field | Test value |
| --- | --- |
| Source commit and owned DLL SHA-256 hashes | Pending |
| Valheim client build / server build | Pending |
| BepInEx / Jötunn versions | Pending |
| OS, native machine or VM, GPU, driver | Pending |
| Connection backend: Steam or crossplay | Pending |
| Other client/server mods | Pending |
| Relevant configuration and target position-sharing settings | Pending |
| Test world/character and log location | Pending |

Install only the two owned plugin/core DLLs in the observer's profile. Other players should initially use unmodified clients and the server should initially have no broadcaster mod. Test the default loaded-target mode before enabling distant targeting.

## Default-mode checks

Every case below is **pending in-game validation**.

| Case | Required result |
| --- | --- |
| Startup/menu | Plugin logs its version and starts disabled; normal menus and camera work |
| Headless load | No camera/UI initialization or graphical errors; plugin does not require clients to install it |
| No other players | Enabling shows a waiting slate; stop restores ordinary view |
| Two nearby players | Both appear in `broadcast list`; the observer does not; duplicate names remain independently selectable |
| Follow shot | Target stays framed; FOV, height, azimuth, smoothing, and obstruction response match configuration |
| Manual cycle | Next/previous wrap predictably and reset the camera pose without displaying a stale target |
| Automatic cycle | Only ready footage consumes dwell time; rotation does not skip several targets after a frame stall |
| Hold/resume | Hold stops timed switching; resume continues; manual switching remains possible |
| Death/unavailable target | An unready target shows the slate; timeout chooses an alternative; holding keeps the target until operator action |
| Observer death | Broadcasting stops, the ordinary HUD/camera return, and experimental reference override is removed before respawn |
| Target disconnect | Selection falls back to a surviving target; an empty roster returns to the slate |
| Controls | Hotkeys are ignored in recognized UI/text entry; console commands report useful status and reject unknown IDs |
| Stop/disable | Camera/FOV and prior HUD state restore; the observer can continue normally |
| World/menu transitions | Leaving a world stops broadcasting; returning does not duplicate handlers or restore stale references |
| Failure | Adapter exception logs once for that activation, stops broadcasting, and restores owned state |
| OBS | Correct game window, waiting slate, target changes, audio, and focus changes are captured as intended |

Observe the parked character during these tests. Document damage, movement, enemy behavior, sleep participation, simulation ownership, and player-count effects; do not infer that an invisible HUD or detached camera makes the character passive.

## Experimental distant-mode checks

Set `Experimental / RemoteTargets = true` only after the default matrix succeeds. Use two target players in widely separated areas with public map positions enabled.

| Case | Required observation |
| --- | --- |
| Initial distant selection | Reference position changes and the target scene object eventually appears; no target footage is shown before local readiness |
| Scene completeness | Terrain, structures, creatures, effects, and player motion are compared with the target client's view; local readiness alone is insufficient evidence |
| Repeated cross-world cycling | Both areas load reliably through repeated switches without accumulating errors, stale objects, or memory growth |
| Hidden location | A distant private-position player is not silently exposed through a new relay; document nearby-player visibility separately |
| Stale position / teleport / logout | Loading waits remain bounded, alternatives work, and stop is always available |
| Restoration | Turning remote mode off or stopping restores normal reference updates and reloads the observer's own area |
| Parked observer | Body persistence, physics, health, and world interaction remain understood while its region unloads or reloads |
| Server effects | Compare ownership transfer, spawning, raids, enemy targeting, sleeping, and player-count behavior before/during/after observation |
| Backend variants | Record Steam and crossplay outcomes separately; one does not establish the other |

Do not promote distant mode to the default until the actual client/server combination passes these cases and the remaining observer effects are acceptable. Preserve logs and configuration with each result.
