#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "usage: $0 <package-feed>" >&2
    exit 64
fi

repository_root=$(cd "$(dirname "$0")/../.." && pwd)
package_feed=$(cd "$1" && pwd)
version=$(grep -oE '<VersionPrefix>[^<]+' "$repository_root/Directory.Build.props" | cut -d'>' -f2)
audit_root=$(mktemp -d "${TMPDIR:-/tmp}/dotprolog-package-consumer.XXXXXX")

cleanup() {
    rm -rf "$audit_root"
}
trap cleanup EXIT

export DOTNET_CLI_HOME="$audit_root/cli-home"
export NUGET_PACKAGES="$audit_root/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_NOLOGO=true
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

work="$audit_root/consumer with spaces"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$work"

cd "$work"
dotnet new nugetconfig --force
dotnet nuget add source "$package_feed" --name dotprolog-local --configfile NuGet.Config
dotnet new install "$package_feed/DotProlog.Templates.$version.nupkg"

dotnet new prolog-console -n HelloProlog --DotPrologVersion "$version"
dotnet build HelloProlog/HelloProlog.dplproj -c Release
dotnet run --project HelloProlog/HelloProlog.dplproj -c Release --no-build |
    grep -Fx "Hello from Prolog on .NET!"

native_output="$audit_root/native"
dotnet publish HelloProlog/HelloProlog.dplproj -c Release --use-current-runtime \
    --self-contained true -p:PublishAot=true -o "$native_output"
native_executable="$native_output/HelloProlog"
if [[ -f "$native_output/HelloProlog.exe" ]]; then
    native_executable="$native_output/HelloProlog.exe"
fi
"$native_executable" | grep -Fx "Hello from Prolog on .NET!"

dotnet new prolog-test -n Tests --DotPrologVersion "$version"
(
    cd Tests
    dotnet test --project Tests.dplproj --minimum-expected-tests 2 --no-ansi
)

dotnet tool install --tool-path "$audit_root/tools" DotProlog.Tool \
    --version "$version" --add-source "$package_feed"
"$audit_root/tools/dotnet-prolog" run HelloProlog/main.pl |
    grep -Fx "Hello from Prolog on .NET!"
