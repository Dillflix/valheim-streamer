import ctypes
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock

MODULE = Path(__file__).resolve().parents[1] / 'sandboxie.py'
SPEC = importlib.util.spec_from_file_location('sandboxie_adapter_under_test', MODULE)
sandboxie = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sandboxie)


class WindowsPathTests(unittest.TestCase):
    def test_drive_shadow_mapping(self):
        root = Path('sandbox-root')
        self.assertEqual(sandboxie.guest_to_host(root, r'C:\BroadcasterControl\session'),
                         root / 'drive' / 'c' / 'BroadcasterControl' / 'session')
        self.assertEqual(sandboxie.guest_to_host(root, r'D:\Games\Valheim\valheim.exe'),
                         root / 'drive' / 'd' / 'Games' / 'Valheim' / 'valheim.exe')

    def test_rejects_aliases_traversal_streams_and_unc(self):
        for path in ['C:relative', r'\\server\share\control', r'\\?\C:\control',
                     r'C:\control\..\other', r'C:\control\.\other', r'C:\control:stream',
                     'C:\\control\x00file', '/tmp/control', r'\control', r'C:\control*']:
            with self.subTest(path=path), self.assertRaises(sandboxie.SandboxieError):
                sandboxie.guest_to_host(Path('root'), path)

    def test_rejects_user_profile_mapping_instead_of_guessing_drive_layout(self):
        with mock.patch.dict(os.environ, {"USERPROFILE": r'C:\Users\Broadcaster'}):
            with self.assertRaises(sandboxie.SandboxieError):
                sandboxie.guest_to_host(Path('root'), r'C:\Users\Broadcaster\Game\valheim.exe')
            sandboxie.guest_to_host(Path('root'), r'C:\Games\Valheim\valheim.exe')

    def test_exact_image_accepts_case_only_and_declared_virtual_alias(self):
        physical = r'C:\Sandbox\user\ValheimBroadcastTest\drive\c\Games\Valheim\valheim.exe'
        guest = r'C:\Games\Valheim\valheim.exe'
        self.assertTrue(sandboxie.expected_image(physical.upper(), physical, guest))
        self.assertTrue(sandboxie.expected_image(guest, physical, guest))
        self.assertFalse(sandboxie.expected_image(r'C:\Games\Other\valheim.exe', physical, guest))
        self.assertFalse(sandboxie.expected_image(r'C:\Program Files (x86)\Steam\steam.exe', physical, guest))

    def test_recycled_pid_or_wrong_sandbox_never_matches(self):
        original = dict(pid=123, created=123456, box='valheimbroadcasttest', image='c:\\games\\valheim.exe', windows_session=1)
        self.assertTrue(sandboxie.same_identity(dict(original), original))
        self.assertFalse(sandboxie.same_identity(None, original))
        for key, value in [('created', 999), ('box', 'anotherbox'), ('image', 'steam.exe'), ('windows_session', 2)]:
            changed = dict(original, **{key: value})
            self.assertFalse(sandboxie.same_identity(changed, original))

    @unittest.skipIf(os.name == 'nt', 'non-Windows import gate')
    def test_native_adapter_refuses_non_windows(self):
        with self.assertRaises(sandboxie.SandboxieError):
            sandboxie.Sandboxie(r'C:\Program Files\Sandboxie-Plus\Start.exe', 'ValheimBroadcastTest', r'C:\Sandbox\user\ValheimBroadcastTest')


class Kernel:
    def __init__(self):
        self.result = 0x102
        self.closed = []
        self.terminated = []
    def WaitForSingleObject(self, handle, timeout):
        return self.result
    def GetExitCodeProcess(self, handle, pointer):
        ctypes.cast(pointer, ctypes.POINTER(sandboxie.DWORD)).contents.value = 0
        return 1
    def TerminateProcess(self, handle, code):
        self.terminated.append(handle)
        self.result = 0
        return 1
    def CloseHandle(self, handle):
        self.closed.append(handle)
        return 1


class OwnedHandleTests(unittest.TestCase):
    def make(self):
        api = mock.Mock()
        api.kernel = Kernel()
        identity = dict(pid=123, created=99, box='valheimbroadcasttest', image='valheim.exe', windows_session=1)
        api._identity.return_value = dict(identity)
        proc = sandboxie.OwnedProcess(api, 456, identity)
        self.addCleanup(proc.close)
        return proc, api

    def test_termination_uses_retained_handle(self):
        proc, api = self.make()
        proc.terminate()
        self.assertEqual(api.kernel.terminated, [456])
        self.assertEqual(proc.poll(), 0)
        proc.terminate()
        self.assertEqual(api.kernel.terminated, [456])

    def test_identity_mismatch_refuses_termination(self):
        proc, api = self.make()
        api._identity.return_value['created'] += 1
        with self.assertRaises(sandboxie.SandboxieError):
            proc.terminate()
        self.assertEqual(api.kernel.terminated, [])

    def test_timeout_does_not_imply_process_exit(self):
        proc, api = self.make()
        with self.assertRaises(subprocess.TimeoutExpired):
            proc.wait(timeout=0.01)
        self.assertEqual(api.kernel.terminated, [])

    def test_failed_wait_is_not_an_exit(self):
        proc, api = self.make()
        api.kernel.result = 0xFFFFFFFF
        with self.assertRaises(sandboxie.SandboxieError):
            proc.poll()

    def test_close_is_idempotent(self):
        proc, api = self.make()
        proc.close()
        proc.close()
        self.assertEqual(api.kernel.closed, [456])


class ProcessInventoryTests(unittest.TestCase):
    def make(self):
        api = sandboxie.Sandboxie.__new__(sandboxie.Sandboxie)
        api.box, api.windows_session = 'ValheimBroadcastTest', 1
        api.dll = mock.Mock()
        api.kernel = Kernel()
        return api

    def test_only_native_missing_process_status_means_absent(self):
        api = self.make()
        # The pinned driver returns STATUS_INVALID_CID for a missing or
        # unboxed process. Driver failure is not evidence that a game exited.
        api.dll.SbieApi_QueryProcess.return_value = ctypes.c_int32(0xC000000B).value
        self.assertIsNone(api._query_box(123))
        for code in (0xC0000001, 0xC0000022, 0xC0000008):
            api.dll.SbieApi_QueryProcess.return_value = ctypes.c_int32(code).value
            with self.subTest(code=code), self.assertRaises(sandboxie.SandboxieError):
                api._query_box(123)

    def test_query_failure_cannot_authorize_an_empty_game_inventory(self):
        api = self.make()
        api.pids = mock.Mock(return_value=[123])
        api.kernel.OpenProcess = mock.Mock(return_value=456)
        api.dll.SbieApi_QueryProcess.return_value = ctypes.c_int32(0xC0000001).value
        with self.assertRaises(sandboxie.SandboxieError):
            api.game_processes(r'C:\Sandbox\game\valheim.exe', r'C:\Game\valheim.exe')
        self.assertEqual(api.kernel.closed, [456])

    def test_native_inventory_requests_all_sessions_for_install_conflicts(self):
        api = self.make()
        def enumerate_processes(box, all_sessions, session, buffer, count):
            self.assertEqual(box, 'ValheimBroadcastTest')
            self.assertEqual(all_sessions, 1)
            count = ctypes.cast(count, ctypes.POINTER(sandboxie.DWORD))
            if buffer is not None:
                buffer[0] = 123
            count.contents.value = 1
            return 0
        api.dll.SbieApi_EnumProcessEx.side_effect = enumerate_processes
        api.identity = mock.Mock(return_value=dict(pid=123, created=1, windows_session=2,
                                                   image=r'c:\game\valheim.exe', box=api.box.casefold()))
        self.assertEqual(api.game_processes(r'C:\Sandbox\game\valheim.exe', r'C:\Game\valheim.exe'), [123])
        api.identity.assert_called_once_with(123, require_current_session=False)

    def test_other_session_can_block_install_but_cannot_be_adopted(self):
        api = self.make()
        api._query_box = mock.Mock(return_value=dict(box=api.box.casefold(), windows_session=2))
        def image(handle, flags, target, length):
            target.value = r'C:\Game\valheim.exe'
            return 1
        api.kernel.QueryFullProcessImageNameW = mock.Mock(side_effect=image)
        api.kernel.GetProcessTimes = mock.Mock(return_value=1)
        self.assertIsNone(api._identity(123, 456))
        identity = api._identity(123, 456, require_current_session=False)
        self.assertEqual(identity['windows_session'], 2)
        api.kernel.OpenProcess = mock.Mock(return_value=456)
        self.assertIsNone(api.attach({'pid': 123, 'identity': identity}))
        self.assertEqual(api.kernel.terminated, [])
        self.assertEqual(api.kernel.closed, [456])


@unittest.skipUnless(os.name == 'nt', 'actual Windows process API requires Windows')
class WindowsKernelTests(unittest.TestCase):
    def test_real_child_identity_parent_and_retained_handle_shutdown(self):
        # Exercise the real ctypes/kernel layouts on a disposable child. Only
        # sandbox membership is substituted; this is not a Sandboxie runtime test.
        api = sandboxie.Sandboxie.__new__(sandboxie.Sandboxie)
        api.box, api.windows_session = 'ValheimBroadcastTest', 1
        api.dll = mock.Mock()
        api.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        api._bind()
        api._query_box = mock.Mock(return_value=dict(box=api.box.casefold(), windows_session=1))
        child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'],
                                 stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        owned = None
        try:
            identity = api.identity(child.pid)
            self.assertEqual(identity['pid'], child.pid)
            self.assertGreater(identity['created'], 0)
            self.assertEqual(identity['image'], sandboxie.normalized(sys.executable))
            self.assertEqual(api.parent_pid(child.pid), os.getpid())
            owned = api.attach({'pid': child.pid, 'identity': identity})
            self.assertIsNotNone(owned)
            self.assertIsNone(owned.poll())
            owned.terminate()
            self.assertEqual(owned.wait(timeout=5), 1)
            child.wait(timeout=5)
        finally:
            if owned is not None:
                owned.close()
            if child.poll() is None:
                child.terminate()
                child.wait(timeout=5)


class LaunchTests(unittest.TestCase):
    def make(self):
        api = sandboxie.Sandboxie.__new__(sandboxie.Sandboxie)
        api.root, api.box = Path('sandbox-root'), 'ValheimBroadcastTest'
        api.kernel = Kernel()
        api.dll = mock.Mock()
        api.game_processes = mock.Mock(return_value=[])
        guest = r'C:\Games\Valheim\valheim.exe'
        physical = sandboxie.guest_to_host(api.root, guest)
        api._identity = mock.Mock(return_value=dict(pid=123, created=99, box='valheimbroadcasttest',
                                                   image=sandboxie.normalized(guest), windows_session=1))
        def launch(box, cmd, cwd, flags, si, pi):
            self.assertEqual(box, 'ValheimBroadcastTest')
            self.assertIn('--vb-session', cmd)
            self.assertIn('--vb-control-dir', cmd)
            self.assertTrue(cmd.startswith(subprocess.list2cmdline([str(physical)])))
            self.assertEqual(cwd, str(physical.parent))
            self.assertEqual(os.environ.get('SteamAppId'), '892970')
            self.assertNotIn('GH_TOKEN', os.environ)
            self.assertNotIn('GITHUB_TOKEN', os.environ)
            result = ctypes.cast(pi, ctypes.POINTER(sandboxie.PROCESS_INFORMATION)).contents
            result.hProcess, result.hThread, result.dwProcessId = 456, 789, 123
            return 1
        api.dll.SbieDll_RunSandboxed.side_effect = launch
        return api, guest, physical

    def test_native_launch_retains_exact_handle_and_strips_tokens(self):
        api, guest, physical = self.make()
        with mock.patch.dict(os.environ, {'GH_TOKEN': 'fixture', 'GITHUB_TOKEN': 'fixture', 'SteamAppId': 'old'}):
            process = api.launch([guest, '-console'], physical, 'a' * 32, r'C:\Control\session')
            self.assertEqual(process.pid, 123)
            self.assertEqual(process.handle, 456)
            self.assertEqual(api.kernel.closed, [789])
            self.assertEqual(os.environ['GH_TOKEN'], 'fixture')
            self.assertEqual(os.environ['SteamAppId'], 'old')
            process.close()

    def test_wrong_box_cleans_only_newly_created_handle(self):
        api, guest, physical = self.make()
        api._identity.return_value = None
        with self.assertRaises(sandboxie.SandboxieError):
            api.launch([guest], physical, 'a' * 32, r'C:\Control\session')
        self.assertTrue(api.kernel.terminated)
        self.assertEqual(set(api.kernel.terminated), {456})
        self.assertIn(456, api.kernel.closed)

    def test_rejects_launch_into_wrong_copy(self):
        api, guest, physical = self.make()
        with self.assertRaises(sandboxie.SandboxieError):
            api.launch([guest], physical.with_name('other.exe'), 'a' * 32, r'C:\Control\session')
        api.dll.SbieDll_RunSandboxed.assert_not_called()

    def test_rejects_steam_shell_or_duplicate_bridge_arguments(self):
        api, guest, physical = self.make()
        for command in [[r'C:\Steam\steam.exe', '-applaunch', '892970'],
                        [r'C:\Windows\System32\cmd.exe', '/c', guest],
                        [guest, '--vb-session', 'operator-override']]:
            with self.subTest(command=command), self.assertRaises(sandboxie.SandboxieError):
                api.launch(command, physical, 'a' * 32, r'C:\Control\session')
        api.dll.SbieDll_RunSandboxed.assert_not_called()

    def test_existing_game_prevents_second_launch(self):
        api, guest, physical = self.make()
        api.game_processes.return_value = [999]
        with self.assertRaises(sandboxie.SandboxieError):
            api.launch([guest], physical, 'a' * 32, r'C:\Control\session')
        api.dll.SbieDll_RunSandboxed.assert_not_called()


class EncoderOwnershipTests(unittest.TestCase):
    def make(self):
        api = sandboxie.Sandboxie.__new__(sandboxie.Sandboxie)
        game = dict(pid=123, identity=dict(created=100))
        api.same_process = mock.Mock(return_value=True)
        api.parent_pid = mock.Mock(return_value=123)
        api.identity = mock.Mock(return_value=dict(pid=456, created=200, image=r'c:\tools\ffmpeg.exe',
                                                   box='valheimbroadcasttest', windows_session=1))
        return api, game

    def test_accepts_only_reported_live_child(self):
        api, game = self.make()
        self.assertEqual(api.encoder_identity(456, game)['pid'], 456)

    def test_rejects_dead_parent_reused_pid_or_other_program(self):
        api, game = self.make()
        api.same_process.return_value = False
        self.assertIsNone(api.encoder_identity(456, game))
        api.same_process.return_value = True
        api.parent_pid.return_value = 999
        self.assertIsNone(api.encoder_identity(456, game))
        api.parent_pid.return_value = 123
        api.identity.return_value['created'] = 99
        self.assertIsNone(api.encoder_identity(456, game))
        api.identity.return_value['created'] = 200
        api.identity.return_value['image'] = r'c:\steam\steam.exe'
        self.assertIsNone(api.encoder_identity(456, game))


class AclPolicyTests(unittest.TestCase):
    def test_user_system_admin_acl_allowed(self):
        user = 'S-1-5-21-123-456-789-1001'
        sandboxie.validate_acl(user, user, [(0, user), (0, 'S-1-5-18'), (0, 'S-1-5-32-544')])

    def test_broad_allow_or_unknown_ace_rejected(self):
        user = 'S-1-5-21-123-456-789-1001'
        for entries in [[(0, 'S-1-1-0')], [(0, 'S-1-5-11')], [(5, user)], [(9, user)]]:
            with self.subTest(entries=entries), self.assertRaises(sandboxie.SandboxieError):
                sandboxie.validate_acl(user, user, entries)

    def test_different_owner_rejected(self):
        with self.assertRaises(sandboxie.SandboxieError):
            sandboxie.validate_acl('S-1-5-32-544', 'S-1-5-21-1001', [])

    def test_deny_ace_does_not_grant_broad_access(self):
        sandboxie.validate_acl('user', 'user', [(0, 'user'), (1, 'S-1-1-0')])

    @unittest.skipUnless(os.name == 'nt', 'actual Windows ACL API requires Windows')
    def test_real_private_directory(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            private = Path(directory) / 'state'
            sandboxie.create_private_directory(private)
            sandboxie.verify_private_path(private)
            credential = private / 'token-fixture.txt'
            credential.write_text('not a credential', encoding='utf-8')
            # An elevated CI token can assign Administrators as a new file's
            # owner; ownership is not inherited from its private parent. Match
            # the documented non-elevated setup explicitly for this fixture,
            # keeping the product's current-user ownership check unchanged.
            advapi, kernel = sandboxie._security_api()
            owner, descriptor = ctypes.c_void_p(), ctypes.c_void_p()
            self.assertEqual(advapi.GetNamedSecurityInfoW(
                str(private), 1, 0x00000001, ctypes.byref(owner), None,
                None, None, ctypes.byref(descriptor)), 0)
            try:
                self.assertEqual(sandboxie._sid_text(advapi, kernel, owner),
                                 sandboxie._current_sid(advapi, kernel))
                self.assertEqual(advapi.SetNamedSecurityInfoW(
                    str(credential), 1, 0x00000001, owner, None, None, None), 0)
            finally:
                kernel.LocalFree(descriptor)
            sandboxie.verify_private_path(credential)


if __name__ == '__main__':
    unittest.main()
