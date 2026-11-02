# Cinemachine runtime for Valheim Broadcaster

This project compiles the official **Cinemachine 3.1.7** runtime sources for the Valheim client. It produces `ValheimBroadcaster.Cinemachine.dll`, preserving the upstream `Unity.Cinemachine` namespace while using a separate assembly identity to avoid replacing another mod's Cinemachine dependency.

Prepare it before a client build:

```sh
python scripts/fetch-cinemachine.py
```

The script downloads the official Cinemachine package, verifies SHA-256 `8b77a98259c6726ce1795690dc2459f0f26d8d17345d9758f019d8de2653998a`, and extracts selected runtime sources and notices to ignored `obj/upstream`. The package records upstream revision `8cb586bf217170b0035e42685880fe1fdefc9729`. Existing archives can be supplied with `--archive` and `--splines-archive`; both receive the same checksum verification. Source files, downloaded archives and Unity/game DLLs are not committed or shipped.

The runtime includes all 131 Cinemachine runtime source files, including the eight spline camera helpers previously omitted. `CINEMACHINE_NO_CM2_SUPPORT` still omits deprecated CM2 adapters. Optional Timeline, input-system, URP/HDRP and postprocessing integrations remain disabled; Valheim's render pipeline is unchanged.

The same isolated DLL now contains 23 selected official **Unity Splines 2.0.0** runtime source files, the dependency version declared by Cinemachine 3.1.7. Its archive SHA-256 is `d58adb0438b3faa97ed3a7d774b501ff1e9194d6918b214ebc92a969ae7531fa`, source revision `3d1fb0ab442e2537dc536dd7cab58e4303804b6d`. The selection retains spline/knot/curve/container/data evaluation and excludes extrusion, instantiation, animation, shader, jobs and external assembly friend declarations. This provides the genuine native Cinemachine Spline Dolly and Unity Splines arc-length calculations without another distributed DLL or a replacement for any other mod's `Unity.Splines` assembly. The original namespaces remain, with types isolated by this assembly's identity.

The selected runtime references matching game `Unity.Mathematics.dll` and `Unity.Collections.dll`; neither is packaged. No Burst code or background jobs are enabled. Upstream Splines source, dependency metadata, selected inventory and compatibility patch are recorded under `obj/upstream/Splines`; its own `LICENSE.md`, `package.json` and `source-record.json` are packaged under `ThirdParty/Cinemachine/Splines`. The Unity Companion License applies to this Unity-dependent runtime separately from the broadcaster source license.

The fetch script applies checked substitutions, recorded with their reasons in `source-record.json`:

- `CinemachineCamera.cs` selects the enabled component when rebuilding each pipeline stage. Upstream caches the first component even when disabled, which otherwise leaves disabled Orbital Follow occupying the Body stage when native Spline Dolly is enabled. Existing component `OnEnable`/`OnDisable` callbacks invalidate the cache. The adapter still enables exactly one Body and one Aim component per candidate.
- `CinemachineCamera.cs` adds `partial` to its sealed declaration for the narrow mod-owned `CandidateQualityBridge.cs`. This lets native ClearShot rank the actual child cameras using externally validated quality without another solve. The adapter restores quality in `finally`; pose, lens and update clocks are not modified.
- `Splines/Runtime/BezierKnot.cs` replaces unavailable .NET 4.8 `System.HashCode.Combine` with an unchecked combination of the same value hashes, preserving equal-knot hash semantics.
- `CinemachineDebug.cs` replaces an index-from-end expression with `Count - 1`, preserving the operation without the unavailable `System.Index` API in .NET Framework 4.8.
- `CameraUpdateManager.cs` initializes its update-status dictionary at declaration. BepInEx may load this DLL after Unity's `RuntimeInitializeOnLoadMethod` phase; its existing reset hook is preserved. `UpdateTracker` already initializes its collections at declaration. `TargetPositionCache` is disabled and lazily initialized, and the enabled standard damping path requires no startup hook. Optional storyboard, input, render-pipeline and experimental damping hooks are not used by this adapter.
- `RuntimeUtility.cs` removes the scratch collider's per-query reference counter and makes destruction null-safe and idempotent. Upstream increments that counter on every penetration query but decrements only once per component destruction, preventing cleanup after repeated use. The collider is a shared cache, released by a decollider's existing teardown and recreated on demand if another camera still needs it. The hidden helper still uses a kinematic Rigidbody with `detectCollisions = false`; it exists only for explicit `Physics.ComputePenetration` queries and is not a gameplay collision body.

Upstream's two editor-only debug fields produce warning CS0649 in a player build; that warning is suppressed for this dependency project only. Other compiler warnings remain visible and CI treats them as errors.

This is a selected-source build of Cinemachine for this mod, not a drop-in distribution of the whole Unity package. The client package intentionally adds this dependency DLL plus the upstream `LICENSE.md`, `license.txt`, `Third Party Notices.md`, package metadata and generated source record under `ThirdParty/Cinemachine`. The server package does not include Cinemachine. The upstream license notices apply to this dependency independently of the mod's own code.

`ManualCameraUpdate.cs` is the mod-owned bridge for solving virtual cameras without a Brain. It advances the isolated Cinemachine update frame to Unity's frame count and temporarily selects the Late update filter, then invokes the standard update method. This preserves Cinemachine's per-camera duplicate-update guard while leaving the real game camera under the existing validated adapter.

The pure mathematical portion is exercised by `tests/ValheimBroadcaster.Cinemachine.Tests` against the actual compiled runtime and real Unity references. Those cases verify all five scenic styles, native distance-to-normalized preview agreement, authored endpoints/aim, immutable shape, non-mutating previews, degenerate-path fallback, bounded progress and end settling. They do not execute a Unity scene, native component callbacks, physics, or rendering; those remain runtime checks.
