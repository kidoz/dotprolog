"""Verify the four Marketplace VSIX payloads and write their SHA-256 checksums."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET
from zipfile import ZipFile


def verify(directory: Path, version: str) -> None:
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Expected an exact major.minor.patch extension version.")
    targets = {None, "linux-x64", "win32-x64", "darwin-arm64"}
    expected = {
        f"dotprolog-{version}{'-' + target if target else ''}.vsix": target
        for target in targets
    }
    packages = sorted(directory.rglob("*.vsix"))
    if len(packages) != 4 or {item.name for item in packages} != set(expected):
        raise ValueError(f"Expected exactly these four VSIX files: {sorted(expected)}")
    checksums = []
    for package in packages:
        target = expected[package.name]
        with ZipFile(package) as archive:
            manifest = json.loads(archive.read("extension/package.json"))
            if (manifest.get("publisher"), manifest.get("name"), manifest.get("version")) != (
                "kidoz", "dotprolog", version
            ):
                raise ValueError(f"Unexpected extension identity in {package.name}")
            if manifest.get("preview") is not True:
                raise ValueError(f"Missing preview listing flag in {package.name}")
            icon = archive.read("extension/" + manifest["icon"])
            if icon[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", icon[16:24]) != (256, 256):
                raise ValueError(f"Expected a 256x256 PNG icon in {package.name}")
            xml = ET.fromstring(archive.read("extension.vsixmanifest"))
            identities = [node for node in xml.iter() if node.tag.endswith("}Identity")]
            if len(identities) != 1 or identities[0].get("TargetPlatform") != target:
                raise ValueError(f"Unexpected target platform in {package.name}")
            if not any(
                node.get("Id") == "Microsoft.VisualStudio.Code.PreRelease" and node.get("Value") == "true"
                for node in xml.iter()
            ):
                raise ValueError(f"Missing pre-release channel flag in {package.name}")
            names = archive.namelist()
            for required in [
                "extension/bin/extension.js",
                "extension/bin/THIRD_PARTY_NOTICES.txt",
                "extension/bin/server/THIRD_PARTY_NOTICES.txt",
                "extension/images/editor.png",
                "extension/images/diagnostics.png",
            ]:
                if required not in names:
                    raise ValueError(f"Missing {required} in {package.name}")
            if any("/node_modules/" in name or name.endswith((".svg", ".pdb", ".dbg")) or ".dSYM/" in name for name in names):
                raise ValueError(f"Development files or debug symbols found in {package.name}")
            native = "extension/bin/server/dotprolog-lsp" + (".exe" if target == "win32-x64" else "")
            if target:
                if native not in names or "extension/bin/server/dotprolog-lsp.dll" in names:
                    raise ValueError(f"Missing native server or unexpected managed fallback in {package.name}")
                if target != "win32-x64" and not archive.getinfo(native).external_attr >> 16 & 0o111:
                    raise ValueError(f"Native server is not executable in {package.name}")
            elif "extension/bin/server/dotprolog-lsp.dll" not in names:
                raise ValueError(f"Missing managed fallback in {package.name}")
        digest = hashlib.sha256(package.read_bytes()).hexdigest()
        checksums.append(f"{digest}  {package.relative_to(directory).as_posix()}\n")
        print(f"Verified {package.name}")
    (directory / "SHA256SUMS").write_text("".join(checksums), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("version")
    args = parser.parse_args()
    verify(args.directory, args.version)
