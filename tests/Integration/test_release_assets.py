"""DotProlog release publication requires complete, unmodified package assets."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).with_name('verify-release-assets.py')
VERSION = '0.17.0'
PACKAGES = [
    'DotProlog.Compiler',
    'DotProlog.Compiler.Cli',
    'DotProlog.Runtime',
    'DotProlog.Sdk',
    'DotProlog.Syntax',
    'DotProlog.Templates',
    'DotProlog.Testing',
    'DotProlog.Tool',
    'DotProlog.Tool.any',
    'DotProlog.Tool.linux-x64',
    'DotProlog.Tool.osx-arm64',
    'DotProlog.Tool.win-x64',
]
SYMBOLS = [
    'DotProlog.Compiler',
    'DotProlog.Compiler.Cli',
    'DotProlog.Runtime',
    'DotProlog.Syntax',
    'DotProlog.Testing',
    'DotProlog.Tool.any',
]


class ReleaseAssetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.assets = Path(self.temporary.name)
        for name in PACKAGES:
            (self.assets / f'{name}.{VERSION}.nupkg').write_bytes(('package ' + name).encode())
        for name in SYMBOLS:
            (self.assets / f'{name}.{VERSION}.snupkg').write_bytes(('symbols ' + name).encode())
        (self.assets / 'sbom.json').write_bytes(b'{}')
        self.checksum()

    def tearDown(self):
        self.temporary.cleanup()

    def checksum(self):
        sums = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                       for p in sorted(self.assets.iterdir()) if p.name != 'SHA256SUMS')
        (self.assets / 'SHA256SUMS').write_text(sums, encoding='utf-8')

    def verify(self):
        return subprocess.run([sys.executable, str(SCRIPT), '--version', VERSION, '--directory', str(self.assets)],
                              capture_output=True, text=True)

    def test_accepts_the_complete_release_inventory(self):
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('Verified 12 packages, 6 symbol packages', result.stdout)

    def test_rejects_a_modified_package(self):
        (self.assets / f'DotProlog.Compiler.{VERSION}.nupkg').write_bytes(b'modified')
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Checksum mismatch', result.stderr)

    def test_rejects_an_unchecksummed_asset(self):
        (self.assets / 'unlisted.json').write_bytes(b'{}')
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('complete asset set', result.stderr)

    def test_rejects_a_missing_symbol_package_even_with_matching_checksums(self):
        (self.assets / f'DotProlog.Testing.{VERSION}.snupkg').unlink()
        self.checksum()
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Symbol package inventory mismatch', result.stderr)

    def test_rejects_an_unexpected_package_even_with_matching_checksums(self):
        name = f'DotProlog.Unexpected.{VERSION}.nupkg'
        (self.assets / name).write_bytes(b'unexpected')
        self.checksum()
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('NuGet package inventory mismatch', result.stderr)
        self.assertIn(name, result.stderr)

    def test_rejects_a_wrong_version_inventory(self):
        old = self.assets / f'DotProlog.Tool.{VERSION}.nupkg'
        old.rename(self.assets / f'DotProlog.Tool.0.0.0.nupkg')
        self.checksum()
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('NuGet package inventory mismatch', result.stderr)


if __name__ == '__main__':
    unittest.main()
