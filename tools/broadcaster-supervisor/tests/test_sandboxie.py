import ctypes
import importlib.util
import os
from pathlib import Path
import subprocess
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
            sandboxie.verify_private_path(credential)


if __name__ == '__main__':
    unittest.main()
