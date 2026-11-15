#!/usr/bin/env bash
# Resolve and validate the version before release assets or publishing credentials are acquired.
set -euo pipefail
version="${1:-}"
if [ -z "$version" ]; then
  root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
  version=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$root/Directory.Build.props")
fi
if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?(\+[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
  echo 'Invalid release version: expected major.minor.patch with optional prerelease or build identifiers.' >&2
  exit 1
fi
printf '%s\n' "$version"
