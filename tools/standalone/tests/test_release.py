"""Negative tests for release licensing and asset inventory gates."""
from pathlib import Path
import json, sys, tempfile, unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from release_licenses import audit, NOTICE_FILES, NATIVE_FILES


class ReleaseLicenseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        files = ['LICENSE', 'ASSET_LICENSE.md', 'NOTICE.md', 'LICENSING.md']
        files += ['licenses/' + name for name in NOTICE_FILES] + NATIVE_FILES['linux-x64']
        for name in files:
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('Fixture only; not an actual license. ' * 3)
        records = [dict(path='map.png', sha256='0'*64, source='content/map.png')]
        (self.root/'release-assets.json').write_text(json.dumps(dict(packaged_files=records)))
        (self.root/'Content').mkdir()
        (self.root/'Content/content-manifest.json').write_text(json.dumps(dict(files=records)))

    def test_complete_inventory(self):
        self.assertTrue(audit(self.root, 'linux-x64')['passed'])

    def test_missing_binding_notice_rejected(self):
        (self.root/'licenses/lib-SDL3-CS-LICENSE').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing or empty'):
            audit(self.root, 'linux-x64')

    def test_unreviewed_assets_rejected(self):
        (self.root/'Content/content-manifest.json').write_text('{"files": []}')
        with self.assertRaisesRegex(ValueError, 'licensed inventory'):
            audit(self.root, 'linux-x64')

    def test_unreviewed_native_dependency_rejected(self):
        (self.root/'lib64/unreviewed.so').write_bytes(b'new dependency')
        with self.assertRaisesRegex(ValueError, 'Unexpected native'):
            audit(self.root, 'linux-x64')

    def test_agility_sdk_rejected(self):
        (self.root/'D3D12').mkdir()
        with self.assertRaisesRegex(ValueError, 'Agility'):
            audit(self.root, 'linux-x64')


if __name__=='__main__': unittest.main()
