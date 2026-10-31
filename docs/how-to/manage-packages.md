# Manage portable Prolog packages

The experimental `propack` tool restores pure Prolog source packages independently of a Prolog
engine. The same manifest and lock work with SWI-Prolog, `dotnet prolog`, and DotProlog SDK projects.
An installable preview can be prepared from this repository; it has not been published to NuGet.org.
The [schema 1 format reference](../reference/propack-format.md) defines the manifest and lock contract.

## Prepare and install the preview

Requires Python 3, .NET 10, and the host platform's NativeAOT prerequisites:

```sh
python tests/Integration/prepare-propack-preview.py --rid osx-arm64 --version 0.17.0-preview.1 --output artifacts/propack-preview
dotnet tool install --tool-path ./artifacts/propack-tools --add-source ./artifacts/propack-preview --version 0.17.0-preview.1 DotProlog.Packages.Cli
./artifacts/propack-tools/propack --version
```

Use `linux-x64`, `win-x64`, or `osx-arm64` on the matching operating system. The producer requires
a new output directory. It creates a portable `.nupkg`, a native ZIP containing the executable,
license and instructions, and `SHA256SUMS`. The ZIP executable can be extracted and placed on PATH;
Windows uses `propack.exe`. Verify the SHA-256 digests against `SHA256SUMS` before installation.
The managed tool requires .NET 10; the native executable restores packages independently of .NET.
The command above creates only local artifacts. The ordinary solution/release pack excludes this
preview tool; explicit packing uses `-p:PropackPreview=true`.

The `Propack preview` CI workflow builds these artifacts on Windows, Linux and macOS, installs the
actual tool package from an isolated feed, and runs an unchanged, pinned SWI-Prolog `heaps` library
under both engines from clean temporary projects. It also extracts and tests the native ZIP, checks
its version and hashes, and verifies locked-restore failure followed by explicit update. The fixture's
upstream commit, file digest and BSD license are retained in `tests/fixtures/packages/swi-heaps`.
The probe checks that SWI loaded the restored source, rather than its bundled `heaps` library.
CI uploads verified artifacts; publication to a package feed is separate.

To run the same acceptance locally (requires `swipl`, Python and the native toolchain):

```sh
DOTPROLOG_RUN_PACKAGE_PREVIEW_TESTS=1 dotnet test --project tests/Integration -- --filter-class Integration.Tests.PropackPreviewTests
```

Set `DOTPROLOG_PREVIEW_ARTIFACTS` to an existing producer output directory to validate those exact
artifacts instead of producing another preview. The acceptance exercises all twelve exported heap
operations, including priority ordering, merge, deletion, empty-heap failure and validation. It does
not claim general SWI compatibility.

A preliminary unchanged upstream `pairs` probe found a DotProlog compatibility limitation:
its meta-predicate callback formed a nested module-qualified closure and raised
`existence_error(procedure, (:)/4)`. Root-level imports also conflicted with pair predicates already
defined in `user`. These results are not claims of support; that library is excluded from the
portable acceptance fixture. A package manifest cannot make engine-specific behavior portable.

## Run the portable example

From the repository root:

```sh
dotnet run --project src/DotProlog.Packages.Cli -- restore --locked --project samples/PortablePackages/app
dotnet run --project src/DotProlog.Packages.Cli -- run main.pl --engine swi --project samples/PortablePackages/app
dotnet run --project src/DotProlog.Tool -- run --packages samples/PortablePackages/app samples/PortablePackages/app/main.pl
dotnet run --project samples/PortablePackages/app/PortablePackages.dplproj
```

Each execution prints `hello`. The application depends on `example/greeting`, which depends on
`example/words`. SWI execution requires `swipl` on PATH. Standalone `propack run --engine dotprolog`
requires `dotnet-prolog` on PATH with package support (either the native executable or a .NET tool shim); the source-tree command above
uses the tool being developed here.

Build a standalone executable for your platform, for example:

```sh
dotnet publish src/DotProlog.Packages.Cli -c Release -r osx-arm64 -p:PublishAot=true -o ./artifacts/propack
./artifacts/propack/propack help
```

Native publication requires the platform's .NET NativeAOT prerequisites. The resulting executable
can restore packages without .NET, SWI-Prolog, or DotProlog installed. Git sources require `git`.
No packages are published by these commands.

## Manifest and sources

A project contains `propack.json`:

```json
{
  "schema": 1,
  "name": "example/app",
  "version": "0.1.0",
  "engine": "dotprolog",
  "dependencies": {
    "example/words": { "version": "^1.0.0", "path": "../words" }
  }
}
```

Names use `owner/name`, with lowercase ASCII letters, digits, underscores and hyphens. Each
component starts with a letter. Versions are stable `major.minor.patch`; requirements are exact,
`*`, or caret ranges. `^1.2.3` permits versions below 2.0.0; `^0.2.3` permits versions below 0.3.0;
`^0.0.3` permits only 0.0.3. Prereleases are not supported in schema 1.

A dependency specifies exactly one source, or omits the source to use a catalog:

| Source | Fields | Notes |
|---|---|---|
| Local directory | `path` | Relative to the declaring manifest; snapshot and verify its contents |
| Git | `git`, `revision` | Local repository, HTTPS or SSH URL; explicit revision locked to its commit |
| ZIP archive | `archive`, optional `sha256` | Local file or HTTPS URL; manifest must be at the archive root |
| Catalog | `version` only | Candidates come from the root project's `catalog` file |

A root manifest can set `"catalog": "catalog.json"`. The local catalog maps package identities
to candidate arrays; source paths are relative to the catalog:

```json
{
  "example/words": [
    { "version": "1.0.0", "path": "../words-v1" },
    { "version": "2.0.0", "git": "https://example.org/words.git", "revision": "v2.0.0" }
  ]
}
```

Resolution tries newer compatible candidates first and backtracks when dependencies disagree.
One version and one source identity are selected for each package. Cycles and unsatisfied
requirements fail with a diagnostic; failed resolution leaves the previous lock intact.
Only the root project's catalog is used. This version has no hosted discovery service.

## Define a library package

```json
{
  "schema": 1,
  "name": "example/words",
  "version": "1.0.0",
  "engines": { "dotprolog": "^0.16.0", "swi": "*" },
  "modules": { "portable_words": "src/portable_words.pl" }
}
```

The source starts with its matching module declaration, after optional whitespace and comments:

```prolog
:- module(portable_words, [word/1]).
word(hello).
```

Consumers use:

```prolog
:- use_module(library(portable_words)).
```

Every `.pl` file in a dependency package must be declared in `modules`. Schema 1 requires a simple
unquoted module identifier and a matching `<module>.pl` basename. It rejects duplicate module
names across the root's declared modules and dependencies. Additional textual include fragments
should use a different extension. Module validation checks the declaration header, not the behavior
of arbitrary directives. The complete dependency graph's declared modules are visible to imports.
Root applications may contain non-module entry files such as `main.pl`.

`engines` contains author-declared compatibility requirements. Empty or omitted means unspecified;
it does not prove portability. The adapter checks the selected installed engine's version when
running. Keep engine/toolchain versions and language flags pinned separately for reproducible
behavior. DotProlog packages currently use Modern mode; StrictIso still rejects `use_module`.
Packages requiring foreign libraries, build scripts, or automatic dialect conversion are outside
this milestone.

## Restore, update, and inspect

```sh
propack init example/app
propack add example/words --path ../words --version '^1.0.0'
propack update
propack restore --locked
propack tree
propack remove example/words
propack update
```

`add` and `remove` edit the manifest; `update` resolves it. `restore` creates a missing lock, but
preserves and validates an existing one. Changes to the manifest or local source packages require
explicit `update`. `restore --locked` fails if the lock is missing or stale. Commit the manifest and
`propack.lock.json` together. Catalog changes alone do not alter an existing lock.

The lock records the manifest hash, selected engine default, exact package versions, source
locations, requested Git revisions, resolved commits, and SHA-256 tree hashes. Local source paths
are relative to the root project, so equivalent checkouts can reuse the lock. Local dependencies
must remain present and unchanged even when their snapshot is cached.

The shared cache lives under the platform's local application data directory in `propack/cache-v1`.
Override it with `PROPACK_CACHE` or `--cache DIR`. The cache must be outside the project. Cache
snapshots are checked before use; missing snapshots can be restored from locked sources. With
all required snapshots present, locked restore does not contact Git/archive origins or the catalog.
There is no separate `--offline` switch yet; a missing remote snapshot requires its origin.

Package hashes include regular files, excluding `.git`, `bin`, `obj`, `.propack`, and
`propack.lock.json`. Symbolic links, archive traversal, and non-regular archive entries are rejected.
Packages are limited to 128 MiB and 10,000 files. Restore does not execute Prolog or package build
hooks. Checksums verify content, not publisher identity or safety of executing package code.

## DotProlog integration

`dotnet prolog restore --locked --project DIR` shares the standalone implementation. Other commands
are available under `dotnet prolog package`, for example `dotnet prolog package tree --project DIR`.
Pass `--packages DIR` to `dotnet prolog run` to use its lock. Normal `run` keeps its existing behavior.

A `.dplproj` with `propack.json` beside it automatically consumes that workspace; alternatively set
`DotPrologPackageDirectory`. Restore explicitly before building. Builds validate the lock and cache
without fetching. Lock/manifest changes invalidate generated source, while unchanged builds retain
normal incremental behavior. Changed local packages fail until `update`, even on an incremental
build. Design-time builds do not restore packages.

Embedding hosts can supply `PrologEngine.LibraryFiles`. Generated C# and direct-IL emitter APIs
also accept a library map. `library(Name)` resolves only explicit aliases, with no ambient fallback
or downloads. Missing aliases produce DPL1009; malformed specifications produce DPL1008.
Existing filename imports are unchanged. The `plc` CLI does not yet expose a package option.

Compiled applications contain imported predicates and do not need the source cache to run.
This does not embed a package resolver or source map for later runtime imports into a published
application; hosts that consult additional source at runtime must supply their own library map.
