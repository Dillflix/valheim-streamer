"""Host onboarding exercises the actual config edit and credential separation."""
import codecs
import json
from pathlib import Path
from types import SimpleNamespace
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import startup_setup as setup
import supervisor as sup


class StartupSetupTests(unittest.TestCase):
    def test_missing_config_and_wrong_json_shape_have_distinct_errors(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "supervisor.json"
            with self.assertRaisesRegex(sup.Failure, "config file not found") as missing:
                sup.Config.load(path)
            self.assertIn(str(path), str(missing.exception))
            for contents in ("null", "[]", '"private-value"'):
                with self.subTest(contents=contents):
                    path.write_text(contents, encoding="utf-8")
                    with self.assertRaisesRegex(sup.Failure, "Expected a JSON object") as invalid:
                        sup.Config.load(path)
                    self.assertNotIn("private-value", str(invalid.exception))

    def test_setup_preserves_other_settings_and_stores_credentials_in_separate_roots(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            box = root / "box"
            cfg = SimpleNamespace(game_dir=box / "game", sandbox_root=box, sandbox_name="Fixture",
                                  require_publishing=True, state_dir=root / "state", token_file=root / "state/token")
            path = cfg.game_dir / "BepInEx/config/com.dillflix.valheimbroadcaster.cfg"
            path.parent.mkdir(parents=True)
            path.write_bytes(codecs.BOM_UTF8 + b"# keep\r\n[Streaming]\r\nRtmpUrl = rtmp://example/%25\r\nAutoStart = false\r\n")
            private_dir = lambda p: p.mkdir(exist_ok=True, parents=True)
            with patch("builtins.input", side_effect=["the watcher", "Cloud", "test.invalid:2456", "set"]), \
                 patch("getpass.getpass", side_effect=["private-fixture", "github-fixture"]):
                setup.configure(cfg, sup.no_symlinks, private_dir, lambda p: self.assertTrue(p.is_file()))
            actual = path.read_bytes()
            self.assertIn(codecs.BOM_UTF8 + b"# keep\r\n", actual)
            self.assertIn(b"RtmpUrl = rtmp://example/%25\r\n", actual)
            self.assertIn(b"AutoStart = true\r\n", actual)
            self.assertIn(b"CharacterSource = Cloud", actual)
            self.assertIn(b"CharacterFile = the watcher", actual)
            self.assertNotIn(b"private-fixture", actual)
            self.assertNotIn(b"github-fixture", actual)
            self.assertEqual("private-fixture",(box/"drive/c/ValheimBroadcasterSecrets/server-password").read_text())
            self.assertEqual("github-fixture",cfg.token_file.read_text())
            with patch("builtins.input", side_effect=["", "", "", ""]), patch("getpass.getpass", side_effect=AssertionError):
                setup.configure(cfg, sup.no_symlinks, private_dir, lambda p: self.assertTrue(p.is_file()))
            self.assertEqual(path.read_bytes(), actual)

    def test_manual_handoff_requires_every_installed_file_to_match(self):
        with tempfile.TemporaryDirectory() as path:
            root = Path(path)
            plugin = root / "plugin"
            for name in sup.OWNED_FILES:
                f = plugin / name
                f.parent.mkdir(parents=True, exist_ok=True)
                f.write_bytes(b"new client")
            sha = "c" * 40
            manifest = root / "release-manifest.json"
            manifest.write_text(json.dumps({"schema":1, "observer_protocol":3, "supervisor_protocol":1,
                "version":"0.8.67","source_sha":sha,
                "files":[{"path":k, **v} for k,v in sup.inventory(plugin).items()]}))
            host = sup.Supervisor(SimpleNamespace(plugin_dir=plugin), github=object())
            self.assertEqual(sha, host.installed_baseline(manifest)["source_sha"])
            host.state["quarantined"] = [sha]
            with self.assertRaisesRegex(sup.Failure, "quarantined"):
                host.installed_baseline(manifest)
            host.state["quarantined"] = []
            (plugin / "ValheimBroadcaster.Core.dll").write_bytes(b"old client")
            with self.assertRaisesRegex(sup.Failure, "differs"):
                host.installed_baseline(manifest)
            self.assertNotIn("active",host.state)
