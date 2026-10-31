#!/usr/bin/env python3
"""Collect verified platform artifacts into one stable release asset set."""
import argparse
import hashlib
from pathlib import Path
import re
import shutil


def verify(directory):
    lines = (directory / 'SHA256SUMS').read_text(encoding='utf-8').splitlines()
    expected = set()
    for line in lines:
        digest, name = line.split('  ', 1)
        if Path(name).name != name or name in expected or not re.fullmatch('[a-f0-9]{64}', digest):
            raise ValueError('Invalid checksum entry: ' + name)
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != digest:
            raise ValueError('Checksum mismatch: ' + name)
        expected.add(name)
    actual = {p.name for p in directory.iterdir() if p.is_file() and p.name != 'SHA256SUMS'}
    if actual != expected or not actual:
        raise ValueError('Checksum manifest does not cover the complete asset set.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--input', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', args.version):
        parser.error('Expected stable major.minor.patch version.')
    if args.verify:
        verify(args.output)
        package = args.output / f'DotProlog.Packages.Cli.{args.version}.nupkg'
        if not package.is_file():
            raise ValueError('Missing release tool package.')
        print('Release asset checksums verified.')
        return
    if args.input is None or args.output.exists():
        parser.error('Collection requires --input and a new output directory.')
    inputs = []
    for rid in ['linux-x64', 'win-x64', 'osx-arm64']:
        directory = args.input / ('propack-release-' + rid)
        verify(directory)
        archive = directory / f'propack-{args.version}-{rid}.zip'
        package = directory / f'DotProlog.Packages.Cli.{args.version}.nupkg'
        if not archive.is_file() or not package.is_file():
            raise ValueError('Missing or mismatched platform artifact: ' + rid)
        inputs.append(archive)
        if rid == 'linux-x64':
            inputs.append(package)
    args.output.mkdir(parents=True)
    for source in inputs:
        shutil.copyfile(source, args.output / source.name)
    sums = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n'
                   for p in sorted(args.output.iterdir()))
    (args.output / 'SHA256SUMS').write_text(sums, encoding='utf-8')
    verify(args.output)
    print('Stable release assets collected and verified.')


if __name__ == '__main__':
    main()
