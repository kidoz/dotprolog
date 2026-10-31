# Portable package format, schema 1

This is the schema 1 contract for `propack.json`, a root catalog, and `propack.lock.json`.
Schema 1's fields and version rules are fixed for the stable release. Incompatible format
changes require a new schema number; additions to these strict objects are incompatible with older
readers. Tool releases may fix implementation defects without changing the format.

The [manifest editor schema](propack-manifest.schema.json) and
[lock editor schema](propack-lock.schema.json) describe object structure. The semantic checks below
also apply; JSON Schema alone cannot verify package contents, dependency graphs, or file hashes.
Configure schemas in the editor rather than adding `$schema` to a manifest: unknown fields are errors.

## Manifest

All names are case-sensitive. JSON is UTF-8; object property names use the spellings below. Unknown
fields and null values for non-nullable properties are rejected. Omitted properties take their defaults.
A root application and a dependency package use the same manifest format.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `schema` | integer | `1` | Only schema 1 is supported |
| `name` | string | required | `owner/name`; each component starts with `[a-z]`, followed by `[a-z0-9_-]*` |
| `version` | string | `0.1.0` | Stable three-component package version |
| `engine` | string | `dotprolog` | Default execution/resolution target: `dotprolog` or `swi` |
| `catalog` | string or null | null | Root catalog path, relative to the root manifest |
| `engines` | object of string requirements | `{}` | Author-declared support by engine identifier |
| `modules` | object of source paths | `{}` | Module identifier to relative `.pl` source path |
| `dependencies` | object of dependency specifications | `{}` | Package identity to version/source specification |

An empty `engines` object means support is unspecified, not independently verified. Resolution
checks whether the default engine is declared when a package restricts its engines. Execution checks
the installed engine version. The lock does not pin an installed engine, compiler, OS, or language
flags; consumers must pin their build environment separately.

A module identifier matches `[a-z][a-zA-Z0-9_]*`. Each module's file basename is `<identifier>.pl`.
The file starts with `:- module(identifier, Exports).`, after optional whitespace, `%` comments or
non-nested block comments. Schema 1 does not support quoted module identifiers in this header.
Paths must remain inside the package. Every `.pl` file in a dependency package is declared in
`modules`; textual include fragments can use another extension. Root applications can have
non-module entry files. Duplicate declared module names are errors across the whole graph,
including the root. Imports can see the whole graph, including transitive packages.

License and upstream provenance belong in package files such as `LICENSE.txt` and `UPSTREAM.json`;
they are not additional manifest fields. Packaging does not confer permission to redistribute code.

## Versions and dependencies

Versions are `major.minor.patch`, with decimal integers from 0 through 2147483647, no leading zeroes
except zero itself, no whitespace, prerelease suffix or build metadata. Dependency and engine
requirements are one of:

| Requirement | Matching versions |
|---|---|
| `1.2.3` | Exactly 1.2.3 |
| `*` | Any stable version |
| `^1.2.3` | At least 1.2.3 and below 2.0.0 |
| `^0.2.3` | At least 0.2.3 and below 0.3.0 |
| `^0.0.3` | Exactly 0.0.3 |

Each dependency is an object with the following fields; all except `version` default to null.

| Field | Meaning |
|---|---|
| `version` | Requirement, default `*` |
| `path` | Local source directory relative to the declaring manifest |
| `git` | Local repository path, HTTPS URL or `ssh://` URL |
| `revision` | Explicit Git branch, tag or commit; required with `git`, invalid without it |
| `archive` | Local ZIP path or HTTPS URL; package manifest at the archive root |
| `sha256` | Optional expected archive-byte digest; hexadecimal SHA-256, compared without regard to case |

Choose exactly one non-null transport (`path`, `git`, `archive`), or omit transports to use the
root catalog. Git submodules and native build hooks are unsupported. Remote dependency packages
should use catalog dependencies; relative path dependencies must resolve within the acquired
snapshot to remain available on another machine. SCP-style Git addresses are not a transport URL
in schema 1. Absolute local paths are accepted but reduce portability.

The root catalog is a JSON object mapping package identities to arrays of source specifications.
Candidate `version` values are exact versions and must match their package manifests. Local catalog
source paths are relative to the catalog file. Dependency packages do not select their own catalogs.
Resolution tries candidates from newer to older and backtracks for version/source constraints.
There is one version/source per package identity. Cycles, conflicting module exports, mismatched
package identities and unreachable or duplicate lock entries are errors. Resolution is bounded to
256 packages and 10,000 search steps.

## Lock and integrity

A generated lock contains:

| Field | Meaning |
|---|---|
| `schema` | `1` |
| `manifestHash` | Lowercase SHA-256 of the root manifest's exact bytes |
| `engine` | The manifest's default engine |
| `packages` | Selected packages sorted by name |

Each package contains `name`, its exact `version`, `source`, `requestedRevision` (nullable), and
`hash` (lowercase SHA-256 of the source tree). The source keeps its original requirement in
`source.version`. Git sources record the resolved commit in `source.revision` and the original
request in `requestedRevision`. Archive sources record their actual archive digest in `source.sha256`.
Local source locations in a generated lock are relative to the root project, including those
originally declared by dependencies. Transport objects use the same field spellings as dependencies.

A tree hash is SHA-256 of the concatenation of records, ordered by relative path using ordinal
comparison. Each record is UTF-8 encoding of:

```text
<relative path with / separators><NUL><lowercase SHA-256 of file bytes><LF>
```

Directories contribute no records. At any depth, `.git`, `bin`, `obj`, `.propack` and
`propack.lock.json` are excluded. Symbolic links are rejected. Source trees have at most 10,000
files and 128 MiB of included file bytes. ZIP inputs also limit entry count and expanded bytes;
traversal and non-regular entries are rejected. Integrity hashes authenticate bytes against the
committed lock; they do not authenticate a publisher or establish that package code is safe.

`restore` creates a missing lock. With an existing lock it validates without changing the graph;
a changed manifest or local source package requires `update`. `restore --locked` also requires an
existing lock. Cached content is verified before use; missing content is reacquired from its locked
origin. Local source dependencies must remain present and unchanged even when cached. Catalog
changes alone do not invalidate a lock. Failed resolution leaves the old lock intact.

The cache location is not part of the lock. `PROPACK_CACHE` or `--cache` can select a different cache,
which must be outside the root project. A complete cache permits locked restore without contacting
Git/archive origins or reading the catalog. There is no separate offline mode that forbids fetching
missing content. Compilation and graph inspection never fetch packages.

## Execution and exit codes

Restore requires no Prolog engine. `run` uses `swipl` or `dotnet-prolog` on PATH. DotProlog's Modern
`use_module(library(Name))` resolves the explicit alias map; StrictIso rejects `use_module`.
There is no automatic dialect conversion. Engine support declarations are limited claims requiring
separate engine-specific tests. Initialization goals in applications should explicitly use `halt(1)`
on failed acceptance conditions, because engine initialization-failure behavior can differ.

| Result | Exit code |
|---|---|
| Successful package command | 0 |
| Usage/argument error | 64 |
| Package, integrity, input/output or engine-launch error | 65 |
| Executed program | Child process exit code |

`--version` reports the installed tool's informational version. The CLI and package schema versions
are separate: tool release versions are independent of dependency package versions even though schema 1 package versions
support only stable versions.
