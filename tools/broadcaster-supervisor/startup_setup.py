"""One-time interactive setup of the sandbox copy's existing BepInEx configuration."""
import configparser
import ctypes
import getpass
import os
from pathlib import Path
import re
import tempfile

from sandboxie import guest_to_host

PASSWORD_PATH = r"C:\ValheimBroadcasterSecrets\server-password"


def patch_settings(text, updates):
    """Edit only named values. BepInEx comments, unknown settings and spelling survive."""
    # Refuse ambiguous settings before rewriting any section. Disable interpolation
    # because unrelated settings can legitimately contain '%' and URL credentials.
    parser = configparser.RawConfigParser(interpolation=None, delimiters=("=",))
    parser.optionxform = str
    parser.read_string(text)
    newline = "\r\n" if "\r\n" in text else "\n"
    lines = text.splitlines(keepends=True)
    section = None
    missing = {s: dict(v) for s, v in updates.items()}
    out = []

    def append_missing(name):
        if name not in missing:
            return
        if out and not out[-1].endswith(("\r", "\n")):
            out.append(newline)
        out.extend(f"{key} = {value}{newline}" for key, value in missing.pop(name).items())

    for line in lines:
        header = re.fullmatch(r"\[([^\]]+)\]", line.strip())
        if header:
            append_missing(section)
            section = header[1]
        key, sep, value = line.partition("=")
        if sep and key.strip() in missing.get(section, {}):
            ending = line[len(line.rstrip("\r\n")):]
            line = key + sep + " " + missing[section].pop(key.strip()) + ending
        out.append(line)
    append_missing(section)
    for name in list(missing):
        out.append(newline + f"[{name}]" + newline)
        append_missing(name)
    return "".join(out)


def replace_bytes(path, data):
    fd, temporary = tempfile.mkstemp(dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if os.name == "nt" and path.exists():
            # ReplaceFile preserves the destination DACL and security attributes.
            replace = ctypes.WinDLL("kernel32", use_last_error=True).ReplaceFileW
            replace.argtypes = [ctypes.c_wchar_p] * 3 + [ctypes.c_uint32, ctypes.c_void_p, ctypes.c_void_p]
            replace.restype = ctypes.c_int
            if not replace(str(path), temporary, None, 0, None, None):
                raise OSError(ctypes.get_last_error(), "Could not replace configuration")
        else:
            os.replace(temporary, path)
    finally:
        Path(temporary).unlink(missing_ok=True)


def configure(config, no_links, private_dir, private_file):
    path = config.game_dir / "BepInEx/config/com.dillflix.valheimbroadcaster.cfg"
    no_links(path)
    if not path.is_file():
        raise ValueError("Launch the mod once to generate its BepInEx configuration")
    raw = path.read_bytes()
    text = raw.decode("utf-8-sig")
    parser = configparser.RawConfigParser(interpolation=None, delimiters=("=",))
    parser.read_string(text)

    def ask(key, label, default=""):
        current = parser.get("Startup", key, fallback="") or default
        answer = input(label + (f" [{current}]" if current else "") + ": ").strip()
        return answer or current

    character = ask("CharacterFile", "Existing character filename, without .fch")
    source = ask("CharacterSource", "Save source (Local/Cloud)", "Local").capitalize()
    server = ask("ServerAddress", "Dedicated Steam server (DNS or IPv4:port)")
    if (not character or len(character) > 128 or character in {".", ".."} or
            re.search(r'[\\/:*?"<>|\x00-\x1f]', character) or character.lower().endswith(".fch") or source not in {"Local", "Cloud"}):
        raise ValueError("Invalid character filename or source")
    host, sep, port = server.rpartition(":")
    if (not sep or not re.fullmatch(r"[A-Za-z0-9.-]{1,253}", host) or
            not re.fullmatch(r"[0-9]{1,5}", port) or not 0 < int(port) <= 65535):
        raise ValueError("Invalid dedicated server address or port")
    old_password = parser.get("Startup", "PasswordFile", fallback="")
    action = input("Password: set, keep existing file, or none ["
                   + ("keep" if old_password else "set") + "]: ").strip().lower() or ("keep" if old_password else "set")
    password = None
    if action == "set":
        password = getpass.getpass("Dedicated server password (hidden): ")
        if not password or len(password.encode("utf-16-le")) > 512 or any(c in password for c in "\r\n\0"):
            raise ValueError("Expected a single nonempty password, at most 256 UTF-16 code units")
        guest = PASSWORD_PATH
    elif action == "keep" and old_password:
        guest = old_password
    elif action == "none":
        guest = ""
    else:
        raise ValueError("Choose set, keep, or none")
    updates = {"Startup": {"Enabled": "true", "CharacterFile": character, "CharacterSource": source,
                            "ServerAddress": server, "PasswordFile": guest},
               "Multiplayer": {"ObserverConnection": "true"}, "Unattended": {"Enabled": "true"}}
    if config.require_publishing:
        updates["Streaming"] = {"AutoStart": "true"}
    updated = patch_settings(text, updates).encode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")
    # The GitHub token stays on the host. An existing token file is never
    # replaced by the join wizard. Environment-based authentication also works.
    if config.token_file and not config.token_file.exists():
        if config.state_dir not in config.token_file.parents:
            raise ValueError("Create the configured token_file privately outside the sandbox before setup")
        token = getpass.getpass("GitHub read-only token for the configured repository (hidden): ").strip()
        if not token or len(token) > 8192 or any(c.isspace() for c in token):
            raise ValueError("A GitHub token is required for the configured token_file")
        private_dir(config.token_file.parent)
        no_links(config.token_file)
        replace_bytes(config.token_file, token.encode("utf-8"))
        private_file(config.token_file)
    if guest:
        secret = guest_to_host(config.sandbox_root, guest)
        no_links(secret)
        if password is not None:
            private_dir(secret.parent)
            if secret.exists():
                private_file(secret)
            replace_bytes(secret, password.encode("utf-8"))
        private_file(secret)
    replace_bytes(path, updated)
    print("Saved automatic character selection and dedicated-server startup in", config.sandbox_name)
