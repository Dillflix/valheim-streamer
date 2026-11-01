# Cinemachine runtime for Valheim Broadcaster

This project compiles the official **Cinemachine 3.1.7** runtime sources for the Valheim client. It produces `ValheimBroadcaster.Cinemachine.dll`, preserving the upstream `Unity.Cinemachine` namespace while using a separate assembly identity to avoid replacing another mod's Cinemachine dependency.

Prepare it before a client build:

```sh
python scripts/fetch-cinemachine.py
```

The script downloads the official Unity package, verifies SHA-256 `8b77a98259c6726ce1795690dc2459f0f26d8d17345d9758f019d8de2653998a`, and extracts selected runtime sources and notices to ignored `obj/upstream`. The package records upstream revision `8cb586bf217170b0035e42685880fe1fdefc9729`. An existing archive can be supplied with `--archive`; it receives the same checksum verification. Source files, downloaded archives and Unity/game DLLs are not committed or shipped.

The runtime retains camera state, follow/composition, extensions, physics collision/deocclusion, and camera managers. It excludes eight spline-specific files, and defines `CINEMACHINE_NO_CM2_SUPPORT` to omit deprecated CM2 adapters. Optional Timeline, input-system, URP/HDRP and postprocessing integrations are not enabled. This avoids requiring Unity Splines or changing Valheim's render pipeline. Cinemachine's mathematical helpers reference the matching game's existing `Unity.Mathematics.dll`, which is not packaged.

The fetch script applies checked substitutions, recorded with their reasons in `source-record.json`:

- `CinemachineDebug.cs` replaces an index-from-end expression with `Count - 1`, preserving the operation without the unavailable `System.Index` API in .NET Framework 4.8.
- `CameraUpdateManager.cs` initializes its update-status dictionary at declaration. BepInEx may load this DLL after Unity's `RuntimeInitializeOnLoadMethod` phase; its existing reset hook is preserved. `UpdateTracker` already initializes its collections at declaration. `TargetPositionCache` is disabled and lazily initialized, and the enabled standard damping path requires no startup hook. Optional storyboard, input, render-pipeline and experimental damping hooks are not used by this adapter.
- `RuntimeUtility.cs` removes the scratch collider's per-query reference counter and makes destruction null-safe and idempotent. Upstream increments that counter on every penetration query but decrements only once per component destruction, preventing cleanup after repeated use. The collider is a shared cache, released by a decollider's existing teardown and recreated on demand if another camera still needs it. The hidden helper still uses a kinematic Rigidbody with `detectCollisions = false`; it exists only for explicit `Physics.ComputePenetration` queries and is not a gameplay collision body.

Upstream's two editor-only debug fields produce warning CS0649 in a player build; that warning is suppressed for this dependency project only. Other compiler warnings remain visible and CI treats them as errors.

This is a selected-source build of Cinemachine for this mod, not a drop-in distribution of the whole Unity package. The client package intentionally adds this dependency DLL plus the upstream `LICENSE.md`, `license.txt`, `Third Party Notices.md`, package metadata and generated source record under `ThirdParty/Cinemachine`. The server package does not include Cinemachine. The upstream license notices apply to this dependency independently of the mod's own code.

`ManualCameraUpdate.cs` is the mod-owned bridge for solving virtual cameras without a Brain. It advances the isolated Cinemachine update frame to Unity's frame count and temporarily selects the Late update filter, then invokes the standard update method. This preserves Cinemachine's per-camera duplicate-update guard while leaving the real game camera under the existing validated adapter.
