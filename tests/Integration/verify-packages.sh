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

# The non-steadfast heuristic remains off by default and runs only through explicit opt-in.
printf '%s\n' 'max(X, Y, X) :- X >= Y, !.' 'max(_, Y, Y).' >HelloProlog/lint-steadfast.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-steadfast.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3037 --warnings-as-errors HelloProlog/lint-steadfast.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled non-steadfast clause" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3037'
printf '%s\n' "$lint_output" | grep -F 'max/3'

# Non-tail recursion is also opt-in and uses the same rule-selection path in the packed tool.
printf '%s\n' 'p(N) :- N > 0, p(N), writeln(N).' >HelloProlog/lint-recursion.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-recursion.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3038 --warnings-as-errors HelloProlog/lint-recursion.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled non-tail recursive call" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3038'
printf '%s\n' "$lint_output" | grep -F 'p/1'

# Multifile cuts use the shared cut-scope analysis only after explicit opt-in.
printf '%s\n' ':- multifile(p/0).' 'p :- !.' >HelloProlog/lint-multifile.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-multifile.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3039 --warnings-as-errors HelloProlog/lint-multifile.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled multifile cut" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3039'
printf '%s\n' "$lint_output" | grep -F 'p/0'

# Repeat loops use the same opt-in rule-selection path without executing the source.
printf '%s\n' 'p :- repeat, fail.' >HelloProlog/lint-repeat.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-repeat.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3040 --warnings-as-errors HelloProlog/lint-repeat.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled repeat loop without a same-scope cut" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3040'
printf '%s\n' "$lint_output" | grep -F 'p/0'

# Final cuts are reported only in the last supplied static clause after explicit opt-in.
printf '%s\n' 'p :- !.' 'p :- true, !.' >HelloProlog/lint-final-cut.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-final-cut.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3041 --warnings-as-errors HelloProlog/lint-final-cut.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled final cut" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3041'
printf '%s\n' "$lint_output" | grep -F 'p/0'

# Variable catchers are reported as advisory broad-recovery sites only after explicit opt-in.
printf '%s\n' 'p :- catch(true, _, true).' >HelloProlog/lint-catch-all.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-catch-all.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3042 --warnings-as-errors HelloProlog/lint-catch-all.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled variable catcher" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3042'
printf '%s\n' "$lint_output" | grep -F 'p/0'

# Bare hard and soft conditionals are reviewed for an explicit else only after opt-in.
printf '%s\n' 'p :- (true -> true).' >HelloProlog/lint-missing-else.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-missing-else.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3043 --warnings-as-errors HelloProlog/lint-missing-else.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled conditional without an else branch" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3043'
printf '%s\n' "$lint_output" | grep -F 'p/0'

# Non-snake-case procedure names are advisory convention findings only after opt-in.
printf '%s\n' 'badName.' >HelloProlog/lint-naming.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint HelloProlog/lint-naming.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3044 --warnings-as-errors HelloProlog/lint-naming.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled non-snake-case predicate name" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3044'
printf '%s\n' "$lint_output" | grep -F 'badName/0'

# Source-arity findings and their positive threshold override require explicit opt-in.
printf '%s\n' 'p(A, B, C, D, E).' >HelloProlog/lint-arity.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint --max-predicate-arity 1 HelloProlog/lint-arity.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3045 --max-predicate-arity 5 HelloProlog/lint-arity.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3045 --max-predicate-arity 4 --warnings-as-errors HelloProlog/lint-arity.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled excessive-arity definition" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3045'
printf '%s\n' "$lint_output" | grep -F 'p/5'

# Direct-subgoal counts and their positive threshold override require explicit opt-in.
printf '%s\n' 'p :- a,b,c,d,e,f,g,h,i.' >HelloProlog/lint-subgoals.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint --max-clause-subgoals 1 HelloProlog/lint-subgoals.pl
dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3046 --max-clause-subgoals 9 HelloProlog/lint-subgoals.pl
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3046 --max-clause-subgoals 8 --warnings-as-errors HelloProlog/lint-subgoals.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled excessive-subgoal-count rule" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3046'
printf '%s\n' "$lint_output" | grep -F 'p/0'
