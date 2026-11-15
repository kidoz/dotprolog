#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "usage: $0 <version>" >&2
    exit 64
fi

version=$1
audit_root=$(mktemp -d "${TMPDIR:-/tmp}/dotprolog-public-release.XXXXXX")
audit_root=$(cd "$audit_root" && pwd -P)
cleanup() {
    rm -rf "$audit_root"
}
trap cleanup EXIT

export DOTNET_CLI_HOME="$audit_root/cli-home"
export NUGET_PACKAGES="$audit_root/packages"
export NUGET_HTTP_CACHE_PATH="$audit_root/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_NOLOGO=true
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

work="$audit_root/public consumer"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$work"
cd "$work"
cat >NuGet.Config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

retry_install() {
    local description=$1
    shift
    for attempt in $(seq 1 20); do
        if "$@"; then
            return 0
        fi
        sleep 30
    done
    echo "::error::$description $version did not become installable from NuGet.org." >&2
    return 1
}

retry_install "DotProlog.Tool" dotnet tool install DotProlog.Tool \
    --tool-path "$audit_root/prolog-tool" --version "$version" --no-http-cache
printf ':- initialization(main).\nmain :- writeln('\''public-tool: passed'\'').\n' >main.pl
"$audit_root/prolog-tool/dotnet-prolog" run main.pl | grep -Fx 'public-tool: passed'

retry_install "DotProlog.Templates" dotnet new install "DotProlog.Templates::$version" \
    --nuget-source https://api.nuget.org/v3/index.json

dotnet new prolog-console -n PublicConsole --DotPrologVersion "$version"
dotnet build PublicConsole/PublicConsole.dplproj -c Release
dotnet run --project PublicConsole/PublicConsole.dplproj -c Release --no-build |
    grep -Fx 'Hello from Prolog on .NET!'

dotnet new prolog-lib -n PublicLibrary --DotPrologVersion "$version"
dotnet build PublicLibrary/PublicLibrary.dplproj -c Release

dotnet new prolog-test -n PublicTests --DotPrologVersion "$version"
(
    cd PublicTests
    dotnet test --project PublicTests.dplproj --minimum-expected-tests 2 --no-ansi
)

retry_install "DotProlog.Compiler.Cli" dotnet tool install DotProlog.Compiler.Cli \
    --tool-path "$audit_root/plc" --version "$version" --no-http-cache
"$audit_root/plc/plc" --version | grep -Fx "plc $version"
"$audit_root/plc/plc" --help | grep -F -- '--packages DIR'
