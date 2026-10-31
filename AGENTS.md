# Working on Valheim Broadcaster

Read `README.md`, `docs/ARCHITECTURE.md`, and `docs/RUNTIME-TESTS.md` before changing behavior. Keep the distinction between implemented code, compilation evidence, core tests, and actual Valheim runtime results explicit.

- Use the optional `jotunn-reference`, `jotunn-create-mod`, `jotunn-maintain-mod`, and `jotunn-deploy-mod` skills when appropriate. Resolve the external Markdown snapshot from `JOTUNN_DOCS_ROOT`; do not hardcode a personal workspace or skill path.
- Match the target Valheim, BepInEx, Jötunn, and Unity assemblies. Check exact signatures. Do not invent game API stubs to make the plugin appear buildable.
- Inspect project imports and scripts before building. Builds must remain read-only with respect to game installations; no automatic deployment or publicizing of installed game files.
- Keep reusable scheduling/selection logic in `ValheimBroadcaster.Core` and game access in the plugin adapter. Preserve stable-ID selection, bounded readiness waits, and correct cleanup on stop/world transitions/errors.
- The main camera is the only rendered view controlled by this prototype. Video capture and transport remain external unless explicitly added to the scope.
- Keep experimental distant streaming disabled by default until the runtime matrix supplies evidence. A public coordinate is not a loaded scene, and local area readiness is not proof of complete server delivery.
- Preserve ordinary-client compatibility. A future relay must authenticate actual peers and bound its input; do not authorize claimed identities from a message payload.
- Do not assume the observer is harmless. Character presence, ownership, simulation, enemies, sleep, and input behavior require explicit implementation and testing.
- Add tests for meaningful core behavior and run `dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release`. Compile the plugin against real matching assemblies when available. Record blocked checks honestly.
- Never commit or package game/Unity DLLs, dependency binaries, saves, credentials, local configuration, or absolute user paths. Runtime payload consists of this project's plugin and core DLLs unless intentionally extended.
- Preserve plugin GUID and released identities. Keep assembly/plugin/package versions consistent when introducing release packaging. Source commits do not authorize installing into a live game or publishing a release outside the user's requested scope.
