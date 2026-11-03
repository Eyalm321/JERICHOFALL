import hashlib
import importlib.util
import io
from pathlib import Path
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("guard", Path(__file__).parents[1] / "check_contents.py")
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class RepositoryGuardTests(unittest.TestCase):
    def test_renamed_excluded_is_rejected(self):
        payload = b"test-only synthetic excluded fixture"
        self.assertTrue(guard.inspect_payload("content/new.png", payload, {hashlib.sha256(payload).hexdigest()}))

    def test_excluded_inside_archive_is_rejected(self):
        payload = b"synthetic excluded member"
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("renamed/data.png", payload)
        self.assertTrue(guard.inspect_payload("content/arena.zip", stream.getvalue(), {hashlib.sha256(payload).hexdigest()}))

    def test_renamed_executable_is_rejected(self):
        self.assertTrue(guard.inspect_payload("content/texture.dat", b"MZ" + bytes(64), set()))

    def test_paths_are_rejected(self):
        for path in ["analysis/decompiled/a.cs", "build/port.bin", "assets/saves/progress.json", "content/a.dll", ".env"]:
            with self.subTest(path=path):
                self.assertTrue(guard.inspect_path(path, "100644"))

    def test_symlink_and_capture_are_rejected(self):
        self.assertTrue(guard.inspect_path("content/alias", "120000"))
        self.assertTrue(guard.inspect_path("assets/review/boss.png", "100644"))

    def test_authored_texture_is_allowed(self):
        self.assertFalse(guard.inspect_path("assets/actors/spirit.png", "100644"))
        self.assertFalse(guard.inspect_payload("content/authored.png", b"new test artwork", set()))

    def test_private_key_is_rejected_without_echoing_it(self):
        key = b"-----BEGIN " + b"PRIVATE KEY-----"
        issue = guard.inspect_payload("secret.txt", key, set())
        self.assertTrue(issue)
        self.assertNotIn(key.decode(), str(issue))

    def test_archive_path_escape_is_rejected(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("../escape.png", b"new test artwork")
        self.assertTrue(guard.inspect_payload("content/arena.zip", stream.getvalue(), set()))
