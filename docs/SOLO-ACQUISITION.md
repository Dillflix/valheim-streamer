# Distant scene acquisition with only the broadcaster

## Known failure and 0.5.1 containment

An actual Valheim 1.0.21 test on 0.5.0 went black after starting: probe status became Stopped at 0.1 seconds, local-player displacement was unavailable, and neither probe stop nor broadcast off recovered the view. The user exited the world.

Assembly inspection shows `ZNetScene.CreateDestroyObjects` builds retention lists around `ZNet.GetReferencePosition`, then `RemoveObjects` destroys instances outside those lists with no local-player exemption. This explains a concrete route to the observed player loss; the original log is still needed to exclude additional failures.

0.5.1 requires a matching `RemoveObjects(List<ZDO>, List<ZDO>)` guard before permitting reference overrides. The guard checks the captured observer ZDO against both retention lists. If absent, it aborts the acquisition, restores normal reference/camera/rendering state, stops broadcasting, and skips that unsafe removal pass so the next cycle recomputes local lists. Probe status remains Failed with the reason. This is not dual-region retention and not proof of successful distant acquisition. Do not interpret an abort as a passing scene test.

On the affected old build, exit/rejoin to recreate the normal client state; stop commands cannot reconstruct a removed local player. Keep RemoteTargets disabled for normal use. The containment change is compiled/unit-tested but still requires runtime verification on a disposable character/world, with logs preserved. After a guarded abort, the expected result is ordinary camera/player control and no persistent black view. If it still fails, exit and preserve LogOutput.log; do not repeat the probe.

## Original diagnostic workflow

0.5.0 added an explicit no-teleport scene probe. It tests real network-reference loading and camera rendering without inventing another player. Use a disposable world/character because the observer remains a normal simulated player.

1. Enable Valheim's console using `-console` in the launch arguments used by your Thunderstore profile.
2. Visit an identifiable location (terrain, structures and creatures you can compare later). Run `broadcast probe mark` in F5. This saves your position only for this world session; it creates no object.
3. Travel away normally. Several hundred metres is a useful starting point. You must be at least 200 metres away **and the destination must no longer be area-ready**. The start command refuses a destination that is still loaded, preventing a false acquisition pass.
4. Park somewhere suitable and run `broadcast probe start`. It starts normal broadcasting if needed, then explicitly overrides network interest to the marked position. You do not need to enable `RemoteTargets`; this command authorizes only the bounded test. It never teleports you. Exit F10 self-follow first.
5. Close F5. A labeled recovery view appears while acquiring; once destination and intended camera area are ready, a labeled shot of the marked location appears. No fake player is required. If desired, start F11 before the probe while normal coverage is active to record the entire transition on the same RTMP connection.
6. Run `broadcast probe status`: record state, elapsed time, first-ready time, destination distance, and observer displacement. Compare the rendered scene with what you saw when marking it. Terrain, buildings, creatures and effects need visual validation; readiness is not proof of complete delivery. Displacement is measured because physics may move the ordinary player even though the mod does not teleport it.
7. Run `broadcast probe stop` to restore normal interest and resume coverage without restarting RTMP. Acquisition times out after 30 seconds continuously unready, and every test ends after 120 seconds total. F8 stops the entire broadcast. Allow the observer's area to reload after restoration.

Repeat against the same unloaded marker to check reproducibility. A world change invalidates the marker. This test does not establish remote-player discovery/replication, private-position availability, teleport travel, or harmless observer simulation. Automatic teleport travel is still unimplemented.
