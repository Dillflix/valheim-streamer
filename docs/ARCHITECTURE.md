# Architecture

The first milestone is a single broadcasting client following and cycling eligible player characters while other players remain unmodded. The existing game camera supplies the picture; OBS captures that output separately.

## Components

| Component | Responsibility |
| --- | --- |
| `ValheimBroadcaster.Core` | Pure .NET Standard 2.0 director: identities, roster ordering, selection, dwell, hold, timeout, and states |
| `BroadcasterPlugin` | BepInEx/Jötunn lifecycle, configuration, controls, console command, camera patch, and experimental network-reference override |
| `TargetRoster` | Combine instantiated player objects with eligible public network positions when requested; exclude the observer |
| `CameraRig` | Smooth a wide shot, check obstructions, hide/restore the HUD, restore camera state |
| External OBS instance | Capture, encode, and transmit the rendered output |

No server plugin, RPC protocol, persistent player database, video transport, web controller, or automatic game launcher is implemented in 0.2.0.

## Director contract

The adapter supplies roster snapshots and elapsed time. Targets have a string ID and display name; the adapter currently uses the character's ZDO ID. Names never decide identity. The core excludes the observer, deduplicates by ID, and sorts ordinally, giving reproducible next/previous behavior independent of input enumeration order.

`Disabled` enters a waiting state when enabled. A nonempty roster selects a target; readiness transitions it to `Following`. Dwell time advances only with ready footage. An unavailable target resets dwell and accumulates a separate timeout; the timeout advances to an alternative target. Holding pauses both automatic behaviors. A disappearing target advances to the next surviving ID. One tick makes at most one selection change, so a stalled frame cannot skip the entire roster.

The adapter must re-read the selection after `Tick`, because that call can change the target. `SelectionRevision` lets the camera reset smoothing on a cut. No-target and not-ready states display a slate rather than presenting the observer's normal view as selected-player footage.

## Game lifecycle

The plugin checks Jötunn's headless predicate before graphical initialization and starts disabled. It verifies the expected camera patch target at startup and disables itself if that method is absent. A Harmony postfix on `GameCamera.UpdateCamera(float)` applies the shot after the normal camera update. Camera rendering stays in the game's normal pipeline.

The normal HUD root is hidden while requested and its previous state is retained for restoration. Both main and sky camera FOVs are updated and restored. Stop, observer death, world exit, component disable, and destruction restore owned state; destruction also removes this plugin's Harmony patches. Runtime adapter errors stop broadcasting and log the exception. These are implemented cleanup paths, not evidence that every game transition has passed a runtime test.

The observer remains an ordinary character with normal gameplay controls. It can be hurt, affect gameplay, or move if given input. This implementation does not claim the observer is passive or exempt from player-count and simulation rules.

## Self-follow test

`broadcast self` activates a session-only adapter flag. The roster then contains only the live local player, and `BroadcastDirector.SetSelfTarget` explicitly selects that singleton without normal observer exclusion. The same readiness check and `CameraRig` render the shot. The flag bypasses remote roster acquisition and reference-position overrides, and the overlay identifies test footage even if the ordinary status overlay is disabled.

Every activation first stops/restores the previous mode. `broadcast on` starts normal targeting, and stop/death/world exit/disable clears the self-test flag. Normal roster updates continue excluding the observer. No synthetic network player, spawned object, persistent config, or server protocol is introduced.

## Distant targets

`RemoteTargets=false` uses instantiated player objects. It is intentionally limited to the client's loaded scene and is not a complete online-player roster.

The experimental option combines those objects with the network roster's public map positions. A public position can bootstrap streaming, but the shot is shown only when the corresponding player object exists and `ZNetScene.IsAreaReady` accepts its position. Distant players who do not publish a location cannot be reliably discovered by this route.

While active, the adapter changes `ZNet.SetReferencePosition` and intercepts subsequent setter calls for its own client. It retains the normal reference position for restoration. It does not alter send timers or teleport the character. Changing the interest position may still affect scene unloading, object ownership, simulation, and the parked observer; local readiness does not certify complete server delivery. These are open runtime questions.

A future server companion may supply approved target positions and explicit load/observer coordination. That requires a defined protocol and authorization based on the actual sender peer, bounded messages, target consent rules, and ordinary-client compatibility. It must not trust a claimed identity inside a payload. Such a companion is not necessary for the implemented nearby-player prototype.

## Version and build boundary

The plugin targets .NET Framework 4.8; the pure core targets .NET Standard 2.0. The project pins Jötunn 2.30.2 for compilation and reads matching local game/BepInEx assemblies. Jötunn build assets are excluded, so the template's implicit deployment/publicizing hooks are not inherited.

Do not substitute fake game API stubs to claim a successful mod build. Compilation and pure-core tests establish different facts from client startup, camera correctness, multiplayer compatibility, and a successful broadcast. Record each separately.
