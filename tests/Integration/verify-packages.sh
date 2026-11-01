#!/usr/bin/env bash

set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <package-feed> [expected-tool-rid]" >&2
    exit 64
fi

repository_root=$(cd "$(dirname "$0")/../.." && pwd)
package_feed=$(cd "$1" && pwd)
version=$(grep -oE '<VersionPrefix>[^<]+' "$repository_root/Directory.Build.props" | cut -d'>' -f2)
expected_tool_rid=${2:-}
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
dotnet nuget add source "$package_feed" --name dotprolog-local --configfile nuget.config
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

test -f "$package_feed/DotProlog.Tool.$version.nupkg"
test -f "$package_feed/DotProlog.Tool.any.$version.nupkg"
if [[ -n "$expected_tool_rid" ]]; then
    test -f "$package_feed/DotProlog.Tool.$expected_tool_rid.$version.nupkg"
fi

# One-shot execution proves that the pointer package resolves the best RID implementation from an
# isolated feed without leaving a global or local tool installation behind.
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" run HelloProlog/main.pl |
    grep -Fx "Hello from Prolog on .NET!"

# The packaged command must preserve its documented warning exit code and layout profile without
# consulting source.
printf 'pair(a,b).\n' >HelloProlog/lint-warning.pl
if dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --profile covington --warnings-as-errors HelloProlog/lint-warning.pl; then
    echo "lint unexpectedly accepted compact comma layout" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1

# Cross-file semantic analysis must ship in the packed tool as well as the source-tree command.
printf '%s\n' ':- module(main, [go/0]). :- use_module('\''lint-other.pl'\''). go :- helper.' >HelloProlog/lint-main.pl
printf '%s\n' ':- module(other, [helper/0]). helper.' >HelloProlog/lint-other.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-main.pl HelloProlog/lint-other.pl

# Advisory reachability analysis uses the same compiler-owned diagnostics in the packed tool.
printf '%s\n' ':- module(m, [go/0]). go. unused.' >HelloProlog/lint-reachability.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-reachability.pl 2>&1); then
    echo "lint unexpectedly accepted an unreferenced predicate" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3034'
printf '%s\n' "$lint_output" | grep -F 'm:unused/0'

# Selective import analysis retains its per-binding diagnostics in the packaged tool.
printf '%s\n' ':- module(main, [go/0]). :- use_module('\''lint-unused.pl'\'', [helper/0]). go.' >HelloProlog/lint-import.pl
printf '%s\n' ':- module(unused_source, [helper/0]). helper.' >HelloProlog/lint-unused.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-import.pl HelloProlog/lint-unused.pl 2>&1); then
    echo "lint unexpectedly accepted an unused selective import" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3035'
printf '%s\n' "$lint_output" | grep -F 'helper/0'

# Grammar reachability retains source-level nonterminal identities in the packaged tool.
printf '%s\n' ':- module(m, []). dead_grammar --> [a].' >HelloProlog/lint-grammar.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-grammar.pl 2>&1); then
    echo "lint unexpectedly accepted an unreferenced nonterminal" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3036'
printf '%s\n' "$lint_output" | grep -F 'm:dead_grammar//0'
