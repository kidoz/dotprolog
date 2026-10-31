"""Release collection must reject missing or modified platform artifacts."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('collect-propack-release.py')
VERSION = '0.17.0'


class ReleaseCollectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.inputs = self.root / 'inputs'
        for rid in ['linux-x64', 'win-x64', 'osx-arm64']:
            directory = self.inputs / ('propack-release-' + rid)
            directory.mkdir(parents=True)
            (directory / f'propack-{VERSION}-{rid}.zip').write_bytes(('native ' + rid).encode())
            (directory / f'DotProlog.Packages.Cli.{VERSION}.nupkg').write_bytes(('managed ' + rid).encode())
            self.checksum(directory)

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def checksum(directory):
        sums = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                       for p in sorted(directory.iterdir()) if p.name != 'SHA256SUMS')
        (directory / 'SHA256SUMS').write_text(sums)

    def collect(self):
        return subprocess.run([sys.executable, str(SCRIPT), '--version', VERSION, '--input', str(self.inputs),
                               '--output', str(self.root / 'output')], capture_output=True, text=True)

    def test_collects_all_native_binaries_and_one_portable_package(self):
        result = self.collect()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(list((self.root / 'output').iterdir())), 5)
        self.assertEqual((self.root / 'output' / f'DotProlog.Packages.Cli.{VERSION}.nupkg').read_bytes(), b'managed linux-x64')

    def test_modified_payload_is_rejected_before_output_is_created(self):
        target = next((self.inputs / 'propack-release-win-x64').glob('*.zip'))
        target.write_bytes(b'changed')
        self.assertNotEqual(self.collect().returncode, 0)
        self.assertFalse((self.root / 'output').exists())

    def test_missing_platform_is_rejected(self):
        target = next((self.inputs / 'propack-release-osx-arm64').glob('*.zip'))
        target.unlink()
        self.assertNotEqual(self.collect().returncode, 0)
        self.assertFalse((self.root / 'output').exists())


if __name__ == '__main__':
    unittest.main()
