#!/usr/bin/env python3
"""Verify release checksums and the exact DotProlog NuGet package inventory."""
import argparse
import hashlib
from pathlib import Path
import re


CORE_PACKAGES = [
    'DotProlog.Compiler',
    'DotProlog.Compiler.Cli',
    'DotProlog.Runtime',
    'DotProlog.Sdk',
    'DotProlog.Syntax',
    'DotProlog.Templates',
    'DotProlog.Testing',
    'DotProlog.Tool',
]
TOOL_IMPLEMENTATIONS = [
    'DotProlog.Tool.any',
    'DotProlog.Tool.linux-x64',
    'DotProlog.Tool.osx-arm64',
    'DotProlog.Tool.win-x64',
]
SYMBOL_PACKAGES = [
    'DotProlog.Compiler',
    'DotProlog.Compiler.Cli',
    'DotProlog.Runtime',
    'DotProlog.Syntax',
    'DotProlog.Testing',
    'DotProlog.Tool.any',
]


def verify(directory, version):
    manifest = directory / 'SHA256SUMS'
    lines = manifest.read_text(encoding='utf-8').splitlines()
    expected_checksums = set()
    for line in lines:
        digest, name = line.split('  ', 1)
        if Path(name).name != name or name in expected_checksums or not re.fullmatch('[a-f0-9]{64}', digest):
            raise ValueError('Invalid checksum entry: ' + name)
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != digest:
            raise ValueError('Checksum mismatch: ' + name)
        expected_checksums.add(name)
    actual_checksums = {p.name for p in directory.iterdir() if p.is_file() and p.name != 'SHA256SUMS'}
    if actual_checksums != expected_checksums or not actual_checksums:
        raise ValueError('Checksum manifest does not cover the complete asset set.')

    expected_packages = {f'{name}.{version}.nupkg' for name in CORE_PACKAGES + TOOL_IMPLEMENTATIONS}
    actual_packages = {p.name for p in directory.iterdir() if p.is_file() and p.suffix == '.nupkg'}
    if actual_packages != expected_packages:
        missing = sorted(expected_packages - actual_packages)
        unexpected = sorted(actual_packages - expected_packages)
        raise ValueError(f'NuGet package inventory mismatch. Missing: {missing}; unexpected: {unexpected}')

    expected_symbols = {f'{name}.{version}.snupkg' for name in SYMBOL_PACKAGES}
    actual_symbols = {p.name for p in directory.iterdir() if p.is_file() and p.suffix == '.snupkg'}
    if actual_symbols != expected_symbols:
        missing = sorted(expected_symbols - actual_symbols)
        unexpected = sorted(actual_symbols - expected_symbols)
        raise ValueError(f'Symbol package inventory mismatch. Missing: {missing}; unexpected: {unexpected}')
    print(f'Verified {len(actual_packages)} packages, {len(actual_symbols)} symbol packages, and SHA256SUMS.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--directory', type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9A-Za-z][0-9A-Za-z.+-]*', args.version):
        parser.error('Invalid package version.')
    verify(args.directory, args.version)


if __name__ == '__main__':
    main()
