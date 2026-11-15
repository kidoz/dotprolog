"""Release inputs are data and must be rejected before any external operation."""
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).with_name('resolve-release-version.sh').resolve()


class ReleaseVersionTests(unittest.TestCase):
    def resolve(self, version):
        return subprocess.run(['bash', str(SCRIPT), version], capture_output=True, text=True, check=False)

    def test_accepts_release_and_preview_versions(self):
        for version in ['0.17.0', '1.2.3-rc.1', '1.2.3+build.123']:
            with self.subTest(version=version):
                result = self.resolve(version)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(version + '\n', result.stdout)

    def test_empty_input_resolves_repository_version(self):
        result = self.resolve('')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertRegex(result.stdout, r'^\d+\.\d+\.\d+\n$')

    def test_rejects_shell_payloads_and_path_or_record_injection(self):
        with tempfile.TemporaryDirectory() as temporary:
            marker = Path(temporary) / 'executed'
            payloads = [f'$(touch {marker})', f'`touch {marker}`', f'"; touch {marker}; #',
                        '../1.2.3', '1.2.3\nOTHER=value', '1.2.3/../x', '-1.2.3', '1.2', '01.2.3']
            for version in payloads:
                with self.subTest(version=version):
                    result = self.resolve(version)
                    self.assertNotEqual(0, result.returncode)
                    self.assertEqual('', result.stdout)
                    self.assertFalse(marker.exists())


if __name__ == '__main__':
    unittest.main()
