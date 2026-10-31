#!/usr/bin/env python3
"""Build local release artifacts. This script does not publish or upload anything."""
import argparse
import hashlib
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', choices=['linux-x64', 'win-x64', 'osx-arm64'], required=True)
    parser.add_argument('--version', required=True, help='Stable SemVer, for example 0.17.0')
    parser.add_argument('--output', type=Path, required=True, help='New output directory')
    args = parser.parse_args()
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', args.version):
        parser.error('Release artifacts require a stable major.minor.patch version.')
    output = args.output.resolve()
    if output.exists():
        parser.error('The output directory must be new.')
    output.parent.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parents[2]
    project = repo / 'src/DotProlog.Packages.Cli'
    with tempfile.TemporaryDirectory(prefix='propack-release-', dir=output.parent) as temporary:
        staging = Path(temporary)
        artifacts = staging / 'artifacts'
        artifacts.mkdir()
        common = ['-c', 'Release', '-p:Version=' + args.version, '-p:ContinuousIntegrationBuild=true', '-nodereuse:false']
        subprocess.run(['dotnet', 'pack', str(project), *common, '-p:PropackRelease=true',
                        '--artifacts-path', str(staging / 'managed-build'), '-o', str(artifacts)], check=True, cwd=repo)
        subprocess.run(['dotnet', 'publish', str(project), *common, '-r', args.rid, '-p:PublishAot=true', '-p:IlcTreatWarningsAsErrors=true', '-p:ILLinkTreatWarningsAsErrors=true',
                        '--artifacts-path', str(staging / 'native-build'), '-o', str(staging / 'native')], check=True, cwd=repo)
        executable_name = 'propack.exe' if args.rid.startswith('win-') else 'propack'
        executable = staging / 'native' / executable_name
        result = subprocess.run([str(executable), '--version'], check=True, capture_output=True, text=True)
        if result.stdout.strip() != 'propack ' + args.version:
            raise RuntimeError('Native artifact version does not match requested release version.')
        readme = staging / 'README.md'
        readme.write_text(f'''# propack {args.version} ({args.rid})

Extract this ZIP, place {executable_name} on PATH, then run `propack --help`.
The native executable does not require .NET or a Prolog engine to restore packages.
Git sources require Git. Running Prolog requires `swipl` or `dotnet-prolog` on PATH.

.NET tool alternative (requires .NET 10):
`dotnet tool install --tool-path ./tools --add-source <download-directory> --version {args.version} DotProlog.Packages.Cli`

Verify SHA256SUMS before installation. Release schema 1 supports pure Prolog source packages.
Documentation: https://github.com/kidoz/dotprolog/blob/main/docs/how-to/manage-packages.md
''', encoding='utf-8')
        archive = artifacts / f'propack-{args.version}-{args.rid}.zip'
        with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as bundle:
            for path, name in [(executable, executable_name), (repo / 'LICENSE', 'LICENSE.txt'), (readme, 'README.md')]:
                entry = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
                entry.create_system = 3
                entry.external_attr = ((0o100755 if path == executable else 0o100644) << 16)
                entry.compress_type = zipfile.ZIP_DEFLATED
                bundle.writestr(entry, path.read_bytes())
        checksums = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                            for p in sorted(artifacts.iterdir()) if p.is_file())
        (artifacts / 'SHA256SUMS').write_text(checksums, encoding='utf-8')
        shutil.move(str(artifacts), str(output))
    print(f'Release artifacts prepared at {output}')


if __name__ == '__main__':
    main()
