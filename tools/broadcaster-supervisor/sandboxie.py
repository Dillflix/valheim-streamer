"""Windows/Sandboxie process adapter; no whole-box or process-name termination.

The public SbieDll functions are checked against Sandboxie Plus v1.18.8
(bde0890108a563f5d72eb8e57085be2b37177218), core/dll/sbieapi.h,
core/dll/sbiedll.h and core/dll/callsvc.c. Only the installed DLL is loaded.
The host updater stays outside the sandbox and uses its physical drive shadow.
"""
from __future__ import annotations

import ctypes
import ntpath
import os
from pathlib import Path, PureWindowsPath
import re
import subprocess


class SandboxieError(RuntimeError):
    """Safe diagnostic, never include a credential or arbitrary process command."""


def normalized(path: str | os.PathLike) -> str:
    return ntpath.normcase(ntpath.normpath(str(path)))


def guest_to_host(root: str | os.PathLike, guest: str | os.PathLike) -> Path:
    """Map only Sandboxie's ordinary drive-letter shadow layout, never UNC/user aliases."""
    raw = str(guest)
    path = PureWindowsPath(raw)
    if (not re.fullmatch(r"[A-Za-z]:", path.drive) or path.root != "\\" or
            any(part in {".", ".."} for part in raw.replace("/", "\\").split("\\")) or
            any(c in raw for c in ('\0', '\r', '\n', '"', '<', '>', '|', '?', '*')) or
            ":" in raw[2:]):
        raise SandboxieError("Only absolute ordinary drive-letter sandbox paths are supported")
    # Sandboxie uses separate user/current, user/all and user/public mappings.
    # This initial adapter deliberately does not guess those profile aliases.
    for key in ("USERPROFILE", "PUBLIC", "ALLUSERSPROFILE", "ProgramData"):
        prefix = os.environ.get(key)
        if prefix and re.fullmatch(r"[A-Za-z]:", PureWindowsPath(prefix).drive):
            candidate, excluded = normalized(raw), normalized(prefix).rstrip("\\")
            if candidate == excluded or candidate.startswith(excluded + "\\"):
                raise SandboxieError("User-profile sandbox aliases are unsupported; use an explicit ordinary drive location")
    return Path(root) / "drive" / path.drive[0].lower() / Path(*path.parts[1:])


def expected_image(actual: str, physical: str | os.PathLike, guest: str | os.PathLike) -> bool:
    # QueryFullProcessImageName can report the virtual or backing image depending
    # on the installed Sandboxie build. Sandbox membership and mapping are also required.
    return normalized(actual) in {normalized(physical), normalized(guest)}


def same_identity(actual: dict | None, expected: dict | None) -> bool:
    keys = ("pid", "created", "box", "image", "windows_session")
    return bool(actual and expected and all(actual.get(k) == expected.get(k) for k in keys))


DWORD = ctypes.c_uint32
LONG = ctypes.c_int32
HANDLE = ctypes.c_void_p
BOOL = ctypes.c_int32
WCHAR_PTR = ctypes.c_wchar_p


class FILETIME(ctypes.Structure):
    _fields_ = [("low", DWORD), ("high", DWORD)]


class STARTUPINFO(ctypes.Structure):
    _fields_ = [("cb", DWORD), ("lpReserved", WCHAR_PTR), ("lpDesktop", WCHAR_PTR),
                ("lpTitle", WCHAR_PTR), ("dwX", DWORD), ("dwY", DWORD),
                ("dwXSize", DWORD), ("dwYSize", DWORD), ("dwXCountChars", DWORD),
                ("dwYCountChars", DWORD), ("dwFillAttribute", DWORD), ("dwFlags", DWORD),
                ("wShowWindow", ctypes.c_uint16), ("cbReserved2", ctypes.c_uint16),
                ("lpReserved2", ctypes.c_void_p), ("hStdInput", HANDLE),
                ("hStdOutput", HANDLE), ("hStdError", HANDLE)]


class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [("hProcess", HANDLE), ("hThread", HANDLE), ("dwProcessId", DWORD), ("dwThreadId", DWORD)]


class PROCESSENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", DWORD), ("cntUsage", DWORD), ("th32ProcessID", DWORD),
                ("th32DefaultHeapID", ctypes.c_size_t), ("th32ModuleID", DWORD),
                ("cntThreads", DWORD), ("th32ParentProcessID", DWORD),
                ("pcPriClassBase", LONG), ("dwFlags", DWORD), ("szExeFile", ctypes.c_wchar * 260)]


class OwnedProcess:
    """A retained kernel handle, so a recycled numeric PID cannot receive termination."""
    def __init__(self, api, handle, identity: dict):
        self.api, self.handle, self.identity = api, handle, identity
        self.pid = identity["pid"]

    def poll(self):
        result = self.api.kernel.WaitForSingleObject(self.handle, 0)
        if result == 0x102:  # WAIT_TIMEOUT: the actual process is still alive
            return None
        if result != 0:
            raise SandboxieError("Unable to inspect the owned game process")
        code = DWORD()
        if not self.api.kernel.GetExitCodeProcess(self.handle, ctypes.byref(code)):
            raise SandboxieError("Unable to read the owned game exit status")
        return int(code.value)

    def wait(self, timeout=None):
        milliseconds = 0xFFFFFFFF if timeout is None else max(0, min(int(timeout * 1000), 0xFFFFFFFE))
        result = self.api.kernel.WaitForSingleObject(self.handle, milliseconds)
        if result == 0x102:
            raise subprocess.TimeoutExpired("owned sandboxed game", timeout)
        return self.poll()

    def terminate(self):
        if self.poll() is not None:
            return
        actual = self.api._identity(self.pid, self.handle)
        if not same_identity(actual, self.identity):
            raise SandboxieError("Owned process identity changed; refusing forced shutdown")
        if not self.api.kernel.TerminateProcess(self.handle, 1):
            raise SandboxieError("Owned game could not be terminated; installation is forbidden")

    kill = terminate

    def close(self):
        if self.handle:
            self.api.kernel.CloseHandle(self.handle)
            self.handle = None

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass


class Sandboxie:
    """Host-side adapter for one retained standard Sandboxie box in this Windows session."""
    def __init__(self, start_exe: str | os.PathLike, box: str, root: str | os.PathLike):
        if os.name != "nt" or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise SandboxieError("Windows Sandboxie supervision requires 64-bit Python on Windows")
        if not re.fullmatch(r"[A-Za-z0-9_]{1,32}", box):
            raise SandboxieError("Sandbox name must be an explicit supported box identifier")
        self.box = box
        self.start_exe = Path(start_exe)
        self.root = Path(root)
        dll = self.start_exe.with_name("SbieDll.dll")
        if (not self.start_exe.is_absolute() or self.start_exe.name.lower() != "start.exe" or
                not self.start_exe.is_file() or not dll.is_file()):
            raise SandboxieError("Configure the installed Sandboxie Start.exe and adjacent x64 SbieDll.dll")
        try:
            # Explicit safe DLL search includes only this installation and system DLLs.
            self.dll = ctypes.WinDLL(str(dll), use_last_error=True, winmode=0x00000100 | 0x00000800)
            self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            self._bind()
        except (OSError, AttributeError) as error:
            raise SandboxieError("Installed Sandboxie API is unavailable or has the wrong architecture") from error
        if self.dll.SbieApi_QueryConfBool(self.box, "UseVolumeSerialNumbers", 0):
            raise SandboxieError("UseVolumeSerialNumbers changes sandbox drive layout and is not supported by this adapter")
        current = self._query_box(os.getpid())
        if current is not None:
            raise SandboxieError("Run the updater outside Sandboxie")
        session = DWORD()
        if not self.kernel.ProcessIdToSessionId(os.getpid(), ctypes.byref(session)):
            raise SandboxieError("Cannot identify the current Windows session")
        self.windows_session = int(session.value)
        actual_root = self.box_root()
        if normalized(actual_root) != normalized(self.root):
            raise SandboxieError("Configured sandbox_root does not match Sandboxie's actual physical root")

    def _bind(self):
        # x64 uses one Windows calling convention. Exact exported argument layouts
        # still matter; no untyped pointer-sized integer calls are permitted.
        signatures = {
            "SbieApi_QueryProcess": ([HANDLE, WCHAR_PTR, WCHAR_PTR, WCHAR_PTR, ctypes.POINTER(DWORD)], LONG),
            "SbieApi_QueryConfBool": ([WCHAR_PTR, WCHAR_PTR, ctypes.c_ubyte], ctypes.c_ubyte),
            "SbieApi_EnumProcessEx": ([WCHAR_PTR, ctypes.c_ubyte, DWORD, ctypes.POINTER(DWORD), ctypes.POINTER(DWORD)], LONG),
            "SbieApi_QueryBoxPath": ([WCHAR_PTR, WCHAR_PTR, WCHAR_PTR, WCHAR_PTR, ctypes.POINTER(DWORD), ctypes.POINTER(DWORD), ctypes.POINTER(DWORD)], LONG),
            "SbieDll_TranslateNtToDosPath": ([WCHAR_PTR], ctypes.c_ubyte),
            "SbieDll_RunSandboxed": ([WCHAR_PTR, WCHAR_PTR, WCHAR_PTR, DWORD, ctypes.POINTER(STARTUPINFO), ctypes.POINTER(PROCESS_INFORMATION)], BOOL),
        }
        for name, (args, result) in signatures.items():
            method = getattr(self.dll, name)
            method.argtypes, method.restype = args, result
        signatures = {
            "OpenProcess": ([DWORD, BOOL, DWORD], HANDLE),
            "CloseHandle": ([HANDLE], BOOL),
            "QueryFullProcessImageNameW": ([HANDLE, DWORD, WCHAR_PTR, ctypes.POINTER(DWORD)], BOOL),
            "GetProcessTimes": ([HANDLE, ctypes.POINTER(FILETIME), ctypes.POINTER(FILETIME), ctypes.POINTER(FILETIME), ctypes.POINTER(FILETIME)], BOOL),
            "WaitForSingleObject": ([HANDLE, DWORD], DWORD),
            "GetExitCodeProcess": ([HANDLE, ctypes.POINTER(DWORD)], BOOL),
            "TerminateProcess": ([HANDLE, DWORD], BOOL),
            "ProcessIdToSessionId": ([DWORD, ctypes.POINTER(DWORD)], BOOL),
            "CreateToolhelp32Snapshot": ([DWORD, DWORD], HANDLE),
            "Process32FirstW": ([HANDLE, ctypes.POINTER(PROCESSENTRY32)], BOOL),
            "Process32NextW": ([HANDLE, ctypes.POINTER(PROCESSENTRY32)], BOOL),
        }
        for name, (args, result) in signatures.items():
            method = getattr(self.kernel, name)
            method.argtypes, method.restype = args, result

    def _query_box(self, pid: int) -> dict | None:
        box, image, sid = (ctypes.create_unicode_buffer(size) for size in (256, 96, 96))
        session = DWORD()
        result = self.dll.SbieApi_QueryProcess(HANDLE(pid), box, image, sid, ctypes.byref(session))
        if result != 0:
            return None
        return {"box": box.value.casefold(), "windows_session": int(session.value)}

    def box_root(self) -> Path:
        root = ctypes.create_unicode_buffer(16384)
        size = DWORD(ctypes.sizeof(root))
        result = self.dll.SbieApi_QueryBoxPath(self.box, root, None, None, ctypes.byref(size), None, None)
        if result != 0 or not root.value or size.value >= ctypes.sizeof(root):
            raise SandboxieError("Cannot resolve the configured sandbox's physical file root")
        if not self.dll.SbieDll_TranslateNtToDosPath(root):
            raise SandboxieError("Sandbox root is not an ordinary local Windows drive path")
        path = PureWindowsPath(root.value)
        if not re.fullmatch(r"[A-Za-z]:", path.drive) or path.root != "\\":
            raise SandboxieError("Only standard drive-backed Sandboxie roots are supported")
        return Path(root.value)

    def pids(self) -> list[int]:
        # Dynamic count form is present in the verified 1.18.8 public header.
        count = DWORD()
        if self.dll.SbieApi_EnumProcessEx(self.box, 0, 0xFFFFFFFF, None, ctypes.byref(count)) != 0:
            raise SandboxieError("Sandbox process enumeration failed")
        for _ in range(3):
            capacity = int(count.value) + 128
            if capacity > 16384:
                raise SandboxieError("Sandbox process inventory is unexpectedly large")
            buffer = (DWORD * capacity)()
            count.value = capacity
            result = self.dll.SbieApi_EnumProcessEx(self.box, 0, 0xFFFFFFFF, buffer, ctypes.byref(count))
            if result == 0 and count.value <= capacity:
                values = [int(buffer[i]) for i in range(count.value)]
                if any(pid <= 0 for pid in values) or len(set(values)) != len(values):
                    raise SandboxieError("Sandbox returned an invalid process inventory")
                return values
        raise SandboxieError("Sandbox process inventory did not stabilize")

    def _identity(self, pid: int, handle) -> dict | None:
        wait = self.kernel.WaitForSingleObject(handle, 0)
        if wait == 0:
            return None
        if wait != 0x102:
            raise SandboxieError("Cannot determine whether the sandboxed process exited")
        sandbox = self._query_box(pid)
        if sandbox is None or sandbox["box"] != self.box.casefold() or sandbox["windows_session"] != self.windows_session:
            return None
        image = ctypes.create_unicode_buffer(32768)
        length = DWORD(len(image))
        if not self.kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)):
            raise SandboxieError("Cannot inspect a sandboxed process image")
        times = [FILETIME() for _ in range(4)]
        if not self.kernel.GetProcessTimes(handle, *(ctypes.byref(t) for t in times)):
            raise SandboxieError("Cannot inspect a sandboxed process creation time")
        return {"pid": int(pid), "created": (int(times[0].high) << 32) | int(times[0].low),
                "image": normalized(image.value), **sandbox}

    def identity(self, pid: int) -> dict | None:
        if type(pid) is not int or pid <= 0:
            return None
        handle = self.kernel.OpenProcess(0x00100000 | 0x1000, 0, pid)
        if not handle:
            if ctypes.get_last_error() == 87:  # ERROR_INVALID_PARAMETER: no such PID
                return None
            raise SandboxieError("Cannot inspect a sandboxed process; refusing to infer that it exited")
        try:
            return self._identity(pid, handle)
        finally:
            self.kernel.CloseHandle(handle)

    def same_process(self, record: dict) -> bool:
        return same_identity(self.identity(record.get("pid", 0)), record.get("identity"))

    def game_processes(self, physical: str | os.PathLike, guest: str | os.PathLike) -> list[int]:
        found = []
        for pid in self.pids():
            identity = self.identity(pid)
            if identity and expected_image(identity["image"], physical, guest):
                found.append(pid)
        return found

    def attach(self, record: dict) -> OwnedProcess | None:
        """Reopen a recorded exact process after supervisor interruption, never adopt by name."""
        pid = record.get("pid", 0)
        handle = self.kernel.OpenProcess(0x00100000 | 0x1000 | 0x0001, 0, pid)
        if not handle:
            if ctypes.get_last_error() == 87:
                return None
            raise SandboxieError("Cannot control the recorded broadcaster process")
        try:
            actual = self._identity(pid, handle)
            if not same_identity(actual, record.get("identity")):
                return None
            process = OwnedProcess(self, handle, actual)
            handle = None
            return process
        finally:
            if handle:
                self.kernel.CloseHandle(handle)

    def parent_pid(self, pid: int) -> int | None:
        snapshot = self.kernel.CreateToolhelp32Snapshot(0x00000002, 0)  # TH32CS_SNAPPROCESS
        if not snapshot or snapshot == ctypes.c_void_p(-1).value:
            raise SandboxieError("Cannot inspect encoder process ancestry")
        try:
            entry = PROCESSENTRY32()
            entry.dwSize = ctypes.sizeof(entry)
            ok = self.kernel.Process32FirstW(snapshot, ctypes.byref(entry))
            while ok:
                if entry.th32ProcessID == pid:
                    return int(entry.th32ParentProcessID)
                ok = self.kernel.Process32NextW(snapshot, ctypes.byref(entry))
            if ctypes.get_last_error() not in (0, 18):  # ERROR_NO_MORE_FILES
                raise SandboxieError("Cannot inspect encoder process ancestry")
            return None
        finally:
            self.kernel.CloseHandle(snapshot)

    def encoder_identity(self, pid: int, game_record: dict) -> dict | None:
        """Capture only the bridge-reported FFmpeg child while its proven parent lives."""
        if type(pid) is not int or pid <= 0 or not self.same_process(game_record):
            return None
        identity = self.identity(pid)
        if (identity is None or ntpath.basename(identity["image"]).casefold() != "ffmpeg.exe" or
                identity["created"] < game_record["identity"]["created"] or
                self.parent_pid(pid) != game_record["pid"]):
            return None
        return identity

    def launch(self, command: list[str], physical_executable: Path, session_id: str,
               guest_control: str | os.PathLike) -> OwnedProcess:
        if not re.fullmatch(r"[0-9a-f]{32}", session_id):
            raise SandboxieError("Invalid launch session")
        if not command or any(not isinstance(arg, str) or not arg or any(c in arg for c in ('\0', '\r', '\n')) for arg in command):
            raise SandboxieError("Invalid direct game launch command")
        if PureWindowsPath(command[0]).name.casefold() != "valheim.exe":
            raise SandboxieError("Sandboxie launch must directly name the copied Valheim.exe, not Steam or a shell")
        if any(arg.startswith("--vb-") for arg in command[1:]):
            raise SandboxieError("Supervisor bridge arguments are managed automatically")
        if normalized(guest_to_host(self.root, command[0])) != normalized(physical_executable):
            raise SandboxieError("The guest launch executable does not map to the configured physical sandbox copy")
        guest_to_host(self.root, guest_control)  # reject unsupported paths before API launch
        if self.game_processes(physical_executable, command[0]):
            raise SandboxieError("Configured sandbox game is already running; refusing another launch")
        # SbieSvc creates the image before attaching its sandbox. A virtual guest
        # executable here could select the host's ordinary Steam installation.
        # Launch the verified physical copy and let the injected file layer resolve
        # subsequent game accesses; preserve guest paths only for bridge arguments.
        args = [str(physical_executable), *command[1:], "--vb-session", session_id,
                "--vb-control-dir", str(guest_control)]
        command_line = subprocess.list2cmdline(args)
        if len(command_line) > 24000:
            raise SandboxieError("Game launch command is too long")
        startup, process = STARTUPINFO(), PROCESS_INFORMATION()
        startup.cb = ctypes.sizeof(startup)
        # SbieDll builds the child environment from the caller. Keep GitHub
        # credentials outside the game even when configured via environment.
        replacements = {"GH_TOKEN": None, "GITHUB_TOKEN": None, "SteamAppId": "892970",
                        "VB_SESSION_ID": session_id, "VB_CONTROL_DIR": str(guest_control)}
        prior = {key: os.environ.get(key) for key in replacements}
        try:
            for key, value in replacements.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
            ok = self.dll.SbieDll_RunSandboxed(self.box, command_line, str(physical_executable.parent),
                                                0, ctypes.byref(startup), ctypes.byref(process))
        finally:
            for key, value in prior.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value
        if not ok or not process.hProcess or not process.dwProcessId:
            raise SandboxieError("Sandboxie could not launch the configured game")
        if process.hThread:
            self.kernel.CloseHandle(process.hThread)
        try:
            identity = self._identity(int(process.dwProcessId), process.hProcess)
            if identity is None or not expected_image(identity["image"], physical_executable, command[0]):
                # This handle was returned for our newly created process, so its
                # cleanup cannot target an existing Steam or unrelated process.
                self.kernel.TerminateProcess(process.hProcess, 1)
                raise SandboxieError("New game process did not match the configured sandbox and executable")
            result = OwnedProcess(self, process.hProcess, identity)
            process.hProcess = None
            return result
        except Exception:
            if process.hProcess:
                # Failure still refers only to the handle returned by this exact
                # launch, never to a PID inferred by scanning process names.
                self.kernel.TerminateProcess(process.hProcess, 1)
                self.kernel.WaitForSingleObject(process.hProcess, 10000)
            raise
        finally:
            if process.hProcess:
                self.kernel.CloseHandle(process.hProcess)


def validate_acl(owner: str, current_user: str, entries: list[tuple[int, str]]) -> None:
    """Fail closed for an existing credential/state object's owner and allow ACEs."""
    if owner != current_user:
        raise SandboxieError("Private state and credential paths must be owned by the current Windows user")
    permitted = {current_user, 'S-1-5-18', 'S-1-5-32-544'}
    for ace_type, sid in entries:
        if ace_type == 1:  # ordinary deny ACE only reduces rights
            continue
        if ace_type != 0 or sid not in permitted:
            raise SandboxieError("Private path grants access beyond this user, SYSTEM and Administrators")


def _security_api():
    if os.name != 'nt':
        raise SandboxieError("Windows security checks require Windows")
    advapi = ctypes.WinDLL('advapi32', use_last_error=True)
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    signatures = {
        'OpenProcessToken': ([HANDLE, DWORD, ctypes.POINTER(HANDLE)], BOOL),
        'GetTokenInformation': ([HANDLE, ctypes.c_int32, ctypes.c_void_p, DWORD, ctypes.POINTER(DWORD)], BOOL),
        'ConvertSidToStringSidW': ([ctypes.c_void_p, ctypes.POINTER(ctypes.c_wchar_p)], BOOL),
        'GetNamedSecurityInfoW': ([WCHAR_PTR, ctypes.c_int32, DWORD, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)], DWORD),
        'GetAce': ([ctypes.c_void_p, DWORD, ctypes.POINTER(ctypes.c_void_p)], BOOL),
        'ConvertStringSecurityDescriptorToSecurityDescriptorW': ([WCHAR_PTR, DWORD, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(DWORD)], BOOL),
        'GetSecurityDescriptorDacl': ([ctypes.c_void_p, ctypes.POINTER(BOOL), ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(BOOL)], BOOL),
        'GetSecurityDescriptorOwner': ([ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(BOOL)], BOOL),
        'SetNamedSecurityInfoW': ([WCHAR_PTR, ctypes.c_int32, DWORD, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p], DWORD),
    }
    for name, (args, result) in signatures.items():
        method = getattr(advapi, name)
        method.argtypes, method.restype = args, result
    kernel.GetCurrentProcess.argtypes, kernel.GetCurrentProcess.restype = [], HANDLE
    kernel.CloseHandle.argtypes, kernel.CloseHandle.restype = [HANDLE], BOOL
    kernel.LocalFree.argtypes, kernel.LocalFree.restype = [ctypes.c_void_p], ctypes.c_void_p
    return advapi, kernel


def _sid_text(advapi, kernel, sid) -> str:
    text = ctypes.c_wchar_p()
    if not sid or not advapi.ConvertSidToStringSidW(sid, ctypes.byref(text)):
        raise SandboxieError("Cannot inspect a Windows security identity")
    try:
        return text.value
    finally:
        kernel.LocalFree(ctypes.cast(text, ctypes.c_void_p))


def _current_sid(advapi, kernel) -> str:
    token = HANDLE()
    if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 0x0008, ctypes.byref(token)):
        raise SandboxieError("Cannot inspect the current Windows user")
    try:
        size = DWORD()
        advapi.GetTokenInformation(token, 1, None, 0, ctypes.byref(size))  # TokenUser
        if size.value < ctypes.sizeof(HANDLE) or size.value > 65536:
            raise SandboxieError("Invalid Windows token information")
        buffer = ctypes.create_string_buffer(size.value)
        if not advapi.GetTokenInformation(token, 1, buffer, len(buffer), ctypes.byref(size)):
            raise SandboxieError("Cannot inspect the current Windows user")
        sid = ctypes.cast(buffer, ctypes.POINTER(ctypes.c_void_p))[0]
        return _sid_text(advapi, kernel, sid)
    finally:
        kernel.CloseHandle(token)


def verify_private_path(path: str | os.PathLike) -> None:
    """Inspect Windows ownership/DACL; do not change an existing credential's ACL."""
    advapi, kernel = _security_api()
    current = _current_sid(advapi, kernel)
    owner, dacl, descriptor = ctypes.c_void_p(), ctypes.c_void_p(), ctypes.c_void_p()
    rc = advapi.GetNamedSecurityInfoW(str(path), 1, 0x00000001 | 0x00000004,
                                     ctypes.byref(owner), None, ctypes.byref(dacl), None, ctypes.byref(descriptor))
    if rc != 0:
        raise SandboxieError("Cannot inspect private path permissions")
    try:
        if not dacl:
            raise SandboxieError("Private path must not have a null DACL")
        # ACL header: BYTE revision, BYTE reserved, WORD size, WORD ACE count.
        count = ctypes.c_uint16.from_address(dacl.value + 4).value
        if count > 1024:
            raise SandboxieError("Unexpected private-path ACL size")
        entries = []
        for index in range(count):
            ace = ctypes.c_void_p()
            if not advapi.GetAce(dacl, index, ctypes.byref(ace)) or not ace:
                raise SandboxieError("Cannot inspect private path permissions")
            kind = ctypes.c_uint8.from_address(ace.value).value
            size = ctypes.c_uint16.from_address(ace.value + 2).value
            if kind == 1:
                entries.append((kind, ''))
                continue
            if kind != 0 or size < 16:
                raise SandboxieError("Unsupported access ACE in private path permissions")
            entries.append((kind, _sid_text(advapi, kernel, ctypes.c_void_p(ace.value + 8))))
        validate_acl(_sid_text(advapi, kernel, owner), current, entries)
    finally:
        kernel.LocalFree(descriptor)


def create_private_directory(path: str | os.PathLike) -> None:
    """Protect only a new managed directory; existing permissions must already be private."""
    path = Path(path)
    if path.exists():
        if not path.is_dir():
            raise SandboxieError("Private state path is not a directory")
        verify_private_path(path)
        return
    advapi, kernel = _security_api()
    current = _current_sid(advapi, kernel)
    descriptor, dacl, owner = ctypes.c_void_p(), ctypes.c_void_p(), ctypes.c_void_p()
    present, defaulted = BOOL(), BOOL()
    sddl = f'O:{current}D:P(A;OICI;FA;;;{current})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)'
    if not advapi.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, ctypes.byref(descriptor), None):
        raise SandboxieError("Cannot prepare private Windows state permissions")
    try:
        if not advapi.GetSecurityDescriptorDacl(descriptor, ctypes.byref(present), ctypes.byref(dacl), ctypes.byref(defaulted)) or not present.value or not dacl:
            raise SandboxieError("Cannot prepare private Windows state permissions")
        if not advapi.GetSecurityDescriptorOwner(descriptor, ctypes.byref(owner), ctypes.byref(defaulted)) or not owner:
            raise SandboxieError("Cannot prepare private Windows state ownership")
        path.mkdir(parents=True, exist_ok=False)
        rc = advapi.SetNamedSecurityInfoW(str(path), 1, 0x00000001 | 0x00000004 | 0x80000000,
                                         owner, None, dacl, None)
        if rc != 0:
            raise SandboxieError("Cannot protect the new state directory; no credentials were written")
    finally:
        kernel.LocalFree(descriptor)
    verify_private_path(path)
