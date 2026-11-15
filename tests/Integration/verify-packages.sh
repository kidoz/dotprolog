#!/usr/bin/env bash

set -euo pipefail

if [[ $# -lt 1 || $# -gt 3 ]]; then
    echo "usage: $0 <package-feed> [expected-tool-rid] [target-framework]" >&2
    exit 64
fi

repository_root=$(cd "$(dirname "$0")/../.." && pwd)
package_feed=$(cd "$1" && pwd)
version=$(grep -oE '<VersionPrefix>[^<]+' "$repository_root/Directory.Build.props" | cut -d'>' -f2)
expected_tool_rid=${2:-}
target_framework=${3:-net10.0}
case "$target_framework" in
    net10.0|net11.0) ;;
    *) echo "unsupported target framework: $target_framework" >&2; exit 64 ;;
esac
audit_root=$(mktemp -d "${TMPDIR:-/tmp}/dotprolog-package-consumer.XXXXXX")
# NuGet identifies projects by path; macOS temporary-directory aliases must resolve consistently.
audit_root=$(cd "$audit_root" && pwd -P)

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
cat >nuget.config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="dotprolog-local">
      <package pattern="DotProlog.*" />
    </packageSource>
    <packageSource key="dotprolog-user">
      <package pattern="Rules" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
dotnet nuget add source "$package_feed" --name dotprolog-local --configfile nuget.config
dotnet new install "$package_feed/DotProlog.Templates.$version.nupkg"

# A known package ID at an unavailable version must fail against only the isolated local feed.
mkdir UpstreamIsolation
cat >UpstreamIsolation/UpstreamIsolation.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>$target_framework</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="DotProlog.Compiler" Version="[0.0.0]" /></ItemGroup>
</Project>
EOF
if dotnet restore UpstreamIsolation/UpstreamIsolation.csproj >"$audit_root/upstream-isolation.log" 2>&1; then
    echo "restore unexpectedly resolved an unavailable package version" >&2
    exit 1
fi
grep -F 'DotProlog.Compiler' "$audit_root/upstream-isolation.log"
grep -F '0.0.0' "$audit_root/upstream-isolation.log"
grep -F 'dotprolog-local' "$audit_root/upstream-isolation.log"

# Adding another framework must preserve the existing template default.
dotnet new prolog-console -n DefaultConsole --DotPrologVersion "$version"
grep -F '<TargetFramework>net10.0</TargetFramework>' DefaultConsole/DefaultConsole.dplproj

dotnet new prolog-console -n HelloProlog --DotPrologVersion "$version" --Framework "$target_framework"
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

dotnet new prolog-test -n Tests --DotPrologVersion "$version" --Framework "$target_framework"
(
    cd Tests
    dotnet test --project Tests.dplproj --minimum-expected-tests 2 --no-ansi
)

# Prove the typed facade composes through a real ProjectReference on the selected framework.
dotnet new prolog-lib -n Rules --DotPrologVersion "$version" --Framework "$target_framework"
mkdir Consumer
cat >Consumer/Consumer.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$target_framework</TargetFramework>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="../Rules/Rules.dplproj" /></ItemGroup>
</Project>
EOF
cat >Consumer/Program.cs <<'EOF'
var rules = Rules.RulesModule.Create();
System.Console.WriteLine(rules.Double(21));
EOF
dotnet run --project Consumer/Consumer.csproj -c Release | grep -Fx "42"

# A user library remains composable after its generated facade crosses a NuGet package boundary.
# Keep the fixture package out of the product artifacts that this script audits and CI uploads.
user_feed="$audit_root/user-feed"
mkdir "$user_feed"
dotnet pack Rules/Rules.dplproj -c Release -o "$user_feed" -p:Version=1.2.3
test -f "$user_feed/Rules.1.2.3.nupkg"
dotnet nuget add source "$user_feed" --name dotprolog-user --configfile nuget.config
mkdir PackageConsumer
cat >PackageConsumer/PackageConsumer.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$target_framework</TargetFramework>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Rules" Version="1.2.3" /></ItemGroup>
</Project>
EOF
cat >PackageConsumer/Program.cs <<'EOF'
var rules = Rules.RulesModule.Create();
System.Console.WriteLine(rules.Double(21));
System.Console.WriteLine(rules.Positive(21));
System.Console.WriteLine(string.Join(",", rules.Upto(3)));
EOF
package_output=$(dotnet run --project PackageConsumer/PackageConsumer.csproj -c Release)
printf '%s\n' "$package_output" | grep -Fx "42"
printf '%s\n' "$package_output" | grep -Fx "True"
printf '%s\n' "$package_output" | grep -Fx "1,2,3"

test -f "$package_feed/DotProlog.Tool.$version.nupkg"
test -f "$package_feed/DotProlog.Tool.any.$version.nupkg"
if [[ -n "$expected_tool_rid" ]]; then
    test -f "$package_feed/DotProlog.Tool.$expected_tool_rid.$version.nupkg"
fi

# Check the actual archives as well as installation: a tool command without its declared entry
# point cannot run, and the pointer must reference implementations rather than contain one.
python3 - "$package_feed" "$version" "$expected_tool_rid" <<'PY'
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

feed = Path(sys.argv[1])
version = sys.argv[2]
native_rid = sys.argv[3]


def read_command(package_name, settings_path):
    with ZipFile(feed / package_name) as package:
        names = package.namelist()
        assert len(names) == len(set(names)), f"{package_name} contains duplicate entries"
        assert settings_path in names, f"{package_name} is missing {settings_path}"
        root = ET.fromstring(package.read(settings_path))
        commands = root.findall("./Commands/Command")
        assert len(commands) == 1, f"{package_name} must declare exactly one command"
        return package_name, names, settings_path, commands[0], root


def check_implementation(package_name, settings_directory, runner, entry_point):
    settings_path = f"{settings_directory}/DotnetToolSettings.xml"
    _, names, _, command, _ = read_command(package_name, settings_path)
    assert command.get("Name") == "dotnet-prolog"
    assert command.get("Runner") == runner
    assert command.get("EntryPoint") == entry_point
    packaged_entry_point = f"{settings_directory}/{entry_point}"
    assert packaged_entry_point in names, f"{package_name} is missing {packaged_entry_point}"


check_implementation(
    f"DotProlog.Tool.any.{version}.nupkg",
    "tools/net10.0/any",
    "dotnet",
    "dotnet-prolog.dll",
)
if native_rid:
    executable = "dotnet-prolog.exe" if native_rid.startswith("win-") else "dotnet-prolog"
    check_implementation(
        f"DotProlog.Tool.{native_rid}.{version}.nupkg",
        f"tools/any/{native_rid}",
        "executable",
        executable,
    )

pointer_name = f"DotProlog.Tool.{version}.nupkg"
_, _, _, pointer_command, pointer_root = read_command(pointer_name, "tools/any/any/DotnetToolSettings.xml")
assert pointer_command.attrib == {"Name": "dotnet-prolog"}
expected_packages = {
    rid: f"DotProlog.Tool.{rid}" for rid in ("linux-x64", "win-x64", "osx-arm64", "any")
}
actual_packages = {
    element.get("RuntimeIdentifier"): element.get("Id")
    for element in pointer_root.findall("./RuntimeIdentifierPackages/RuntimeIdentifierPackage")
}
assert actual_packages == expected_packages
PY

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

# Conflicting imports are reported by the packed tool's default semantic profile.
printf '%s\n' ':- module(a, [p/0]).' 'p.' >HelloProlog/lint-import-a.pl
printf '%s\n' ':- module(b, [p/0]).' 'p.' >HelloProlog/lint-import-b.pl
printf '%s\n' ':- module(m, []).' ':- import(a, [p/0]).' ':- import(b, [p/0]).' >HelloProlog/lint-import-main.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    HelloProlog/lint-import-main.pl HelloProlog/lint-import-a.pl HelloProlog/lint-import-b.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3047'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint --warnings-as-errors \
    HelloProlog/lint-import-main.pl HelloProlog/lint-import-a.pl HelloProlog/lint-import-b.pl 2>&1); then
    echo "lint unexpectedly accepted conflicting imports" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3047'
printf '%s\n' "$lint_output" | grep -F 'earlier import from module a'

# Local import shadowing is opt-in and never executes an initialization directive.
printf '%s\n' ':- module(shadow_source, [p/0]).' 'p.' >HelloProlog/lint-shadow-source.pl
printf '%s\n' ':- module(shadow_main, [p/0]).' ':- initialization(halt(9)).' \
    ':- import(shadow_source, [p/0]).' 'p.' >HelloProlog/lint-shadow-main.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    HelloProlog/lint-shadow-main.pl HelloProlog/lint-shadow-source.pl 2>&1)
if printf '%s\n' "$lint_output" | grep -F 'warning DPL3048'; then
    echo "lint enabled local import shadowing by default" >&2
    exit 1
fi
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3048 --warnings-as-errors \
    HelloProlog/lint-shadow-main.pl HelloProlog/lint-shadow-source.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled local import shadow" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3048'
printf '%s\n' "$lint_output" | grep -F 'from module shadow_source'

# Predefined identity collisions are opt-in and do not execute source.
printf '%s\n' ':- initialization(halt(9)).' 'member(_, _).' >HelloProlog/lint-builtin-shadow.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    HelloProlog/lint-builtin-shadow.pl 2>&1)
if printf '%s\n' "$lint_output" | grep -F 'warning DPL3049'; then
    echo "lint enabled predefined identity shadowing by default" >&2
    exit 1
fi
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3049 --warnings-as-errors HelloProlog/lint-builtin-shadow.pl 2>&1); then
    echo "lint unexpectedly accepted an enabled predefined identity collision" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3049'
printf '%s\n' "$lint_output" | grep -F 'predefined predicate member/2'

# Conflicting meta-predicate modes are reported by default without executing source.
printf '%s\n' ':- initialization(halt(9)).' ':- meta_predicate(p(0)).' ':- meta_predicate(p(1)).' \
    'p(_).' >HelloProlog/lint-meta-declarations.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    HelloProlog/lint-meta-declarations.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3050'
printf '%s\n' "$lint_output" | grep -F 'earlier template p(0)'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-meta-declarations.pl 2>&1); then
    echo "lint unexpectedly accepted conflicting meta-predicate declarations" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3050'

# Missing selective exports are reported by default without running initialization directives.
printf '%s\n' ':- module(export_source, [q/0]).' 'q.' >HelloProlog/lint-export-source.pl
printf '%s\n' ':- module(export_main, []).' ':- initialization(halt(9)).' \
    ':- use_module('\''lint-export-source.pl'\'', [p/0]).' >HelloProlog/lint-export-main.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    HelloProlog/lint-export-main.pl HelloProlog/lint-export-source.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3051'
printf '%s\n' "$lint_output" | grep -F 'Import of p/0 into module export_main from module export_source'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --warnings-as-errors HelloProlog/lint-export-main.pl HelloProlog/lint-export-source.pl 2>&1); then
    echo "lint unexpectedly accepted a missing selective export" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3051'

# Contract output analysis is default-enabled and never executes another module's initialization directive.
printf '%s\n' ':- module(contract_rules, [p/1]).' 'p(wrong).' >HelloProlog/lint-contract-rules.pl
printf '%s\n' ':- clr_module('\''ContractRules'\'').' \
    ':- clr_export(p/1, det, [out(value, integer)]).' >HelloProlog/lint-contract-rules.dpli
printf '%s\n' ':- module(contract_canary, []).' ':- initialization(halt(9)).' >HelloProlog/lint-contract-canary.pl
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --contract HelloProlog/lint-contract-rules.dpli \
    HelloProlog/lint-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3052'
printf '%s\n' "$lint_output" | grep -F 'Output value of contract_rules:p/1'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --enable-rule DPL3052 --contract HelloProlog/lint-contract-rules.dpli --warnings-as-errors \
    HelloProlog/lint-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1); then
    echo "lint unexpectedly accepted an incompatible contract output" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3052'
printf '%s\n' "$lint_output" | grep -F 'Output value of contract_rules:p/1'

# Later-fact contract analysis is also default-enabled in the packed tool.
printf '%s\n' ':- module(later_contract_rules, [p/1]).' 'p(1).' 'p(wrong).' >HelloProlog/lint-later-contract-rules.pl
printf '%s\n' ':- clr_module('\''LaterContractRules'\'').' \
    ':- clr_export(p/1, nondet, [out(value, integer)]).' >HelloProlog/lint-later-contract-rules.dpli
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --contract HelloProlog/lint-later-contract-rules.dpli \
    HelloProlog/lint-later-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3053'
printf '%s\n' "$lint_output" | grep -F 'Output value of later_contract_rules:p/1'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --contract HelloProlog/lint-later-contract-rules.dpli --warnings-as-errors \
    HelloProlog/lint-later-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1); then
    echo "lint unexpectedly accepted a reachable later contract output" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3053'

# Ground-rule contract analysis is default-enabled in the packed tool without executing source.
printf '%s\n' ':- module(ground_rule_contract_rules, [p/2]).' 'p(1, 1) :- a = b.' 'p(1, wrong) :- true.' \
    >HelloProlog/lint-ground-rule-contract-rules.pl
printf '%s\n' ':- clr_module('\''GroundRuleContractRules'\'').' \
    ':- clr_export(p/2, det, [in(item, integer), out(value, integer)]).' \
    >HelloProlog/lint-ground-rule-contract-rules.dpli
lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --contract HelloProlog/lint-ground-rule-contract-rules.dpli \
    HelloProlog/lint-ground-rule-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1)
printf '%s\n' "$lint_output" | grep -F 'warning DPL3054'
printf '%s\n' "$lint_output" | grep -F 'Output value of ground_rule_contract_rules:p/2'
if lint_output=$(dotnet tool exec --source "$package_feed" "DotProlog.Tool@$version" lint \
    --contract HelloProlog/lint-ground-rule-contract-rules.dpli --warnings-as-errors \
    HelloProlog/lint-ground-rule-contract-rules.pl HelloProlog/lint-contract-canary.pl 2>&1); then
    echo "lint unexpectedly accepted a reachable ground-rule contract output" >&2
    exit 1
else
    lint_exit=$?
fi
test "$lint_exit" -eq 1
printf '%s\n' "$lint_output" | grep -F 'warning DPL3054'

# The packed SDK reports all three warnings through MSBuild without executing an unrelated source unit.
dotnet new prolog-lib -n ContractLibrary --DotPrologVersion "$version" --Framework "$target_framework"
printf '%s\n' ':- module(rules, [p/1, q/1, r/2]).' 'p(wrong).' 'q(1).' 'q(wrong).' \
    'r(1, 1) :- a = b.' 'r(1, wrong) :- true.' >ContractLibrary/rules.pl
printf '%s\n' ':- clr_module('\''Rules'\'').' \
    ':- clr_export(p/1, det, [out(value, integer)]).' \
    ':- clr_export(q/1, nondet, [out(value, integer)]).' \
    ':- clr_export(r/2, det, [in(item, integer), out(value, integer)]).' >ContractLibrary/rules.dpli
printf '%s\n' ':- module(contract_canary, []).' ':- initialization(halt(9)).' >ContractLibrary/canary.pl
sdk_log="$audit_root/sdk-contract.log"
if ! dotnet build ContractLibrary/ContractLibrary.dplproj -c Release >"$sdk_log" 2>&1; then
    cat "$sdk_log" >&2
    exit 1
fi
grep -F 'rules.pl(2,3): warning DPL3052' "$sdk_log"
grep -F 'rules.pl(4,3): warning DPL3053' "$sdk_log"
grep -F 'rules.pl(6,6): warning DPL3054' "$sdk_log"
grep -F 'Output value of rules:p/1' "$sdk_log"
grep -F 'Output value of rules:q/1' "$sdk_log"
grep -F 'Output value of rules:r/2' "$sdk_log"
