# Changelog

All notable changes to DotProlog are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The ISO evaluable functors for trigonometry, logarithms, exponentials, rounding, float
  decomposition, and bitwise complement.

### Changed

- Arithmetic now enforces ISO operand signatures, float division result types, bounded-overflow
  errors, and the distinct exceptional cases for zero division, undefined results, and float
  overflow.
- The repository's ISO-derived conformance corpus now contains 286 passing cases.

## [0.1.1] — 2026-07-28

### Added

- A `prolog-test` template whose `test_*` predicates are discovered and run by `dotnet test`.
- A clean local-feed consumer gate that installs the packed templates and .NET tool, then builds,
  runs, NativeAOT-publishes, and tests generated projects from a path containing spaces.

### Changed

- The repository test suite now uses Microsoft.Testing.Platform v2 through the .NET 10
  `global.json` test-runner contract, so xUnit and Prolog test projects run together.
- Release checksums now cover packages, symbols, the SBOM, and all native binaries; the CycloneDX
  tool is pinned, and a missing NuGet API key fails rather than silently skipping publication.
- Child build processes close standard input, are killed with their process tree if they exceed the
  integration-test limit, and every CI and release job has an explicit timeout.
- `DotProlog.Sdk` now carries a real package title and description.

## [0.1.0] — 2026-07-27

The first release of **DotProlog** — a Prolog implementation for .NET 10, built as a first-class
SDK language rather than an interpreter you embed. It compiles Prolog to bytecode and runs it on a
virtual machine written in C#, emits no CLR IL, and therefore works unchanged inside a NativeAOT
binary: a published executable can consult a `.pl` file it has never seen and run it.

### Added

- **The language.** Facts, rules, unification, backtracking, and the control constructs `,/2`,
  `;/2`, `->/2`, `*->/2`, `\+/1`, `!/0`, and `call/1..8`, with ISO cut scoping in clause bodies and
  in meta-called goals alike. Exceptions through `throw/1` and `catch/3`, with every engine error
  raised as a catchable `error(Formal, Context)` term.
- **All solutions.** `findall/3`, `bagof/3`, `setof/3`, `forall/2`, and `aggregate_all/3`.
- **The database.** `assertz/1`, `asserta/1`, `retract/1`, `retractall/1`, `clause/2`, `abolish/1`,
  and `:- dynamic`, with logical-update-view semantics.
- **A standard library.** Text conversion, the list library, sorting, higher-order predicates,
  `copy_term/2`, `term_variables/2`, `succ/2`, `plus/3`, and `format/1,2,3` with column stops.
- **Operators.** `op/3` and `current_op/3`, and a term writer that honours them — whatever
  `writeq/1` produces reads back as the same term.
- **Grammars.** `-->/2` translated at load time, with `phrase/2,3`, `{}/1`, pushback lists, and
  terminals written as lists or double-quoted strings.
- **Streams.** `open/3,4`, `close/1`, the current-stream predicates, `read/1,2`, `read_term/2,3`,
  character I/O, `with_output_to/2`, `term_to_atom/2`, and `read_term_from_atom/3`.
- **Modules.** `:- module/2` hides what its export list omits, with `use_module/1,2`, `Module:Goal`,
  and `:- meta_predicate/1`.
- **`.dplproj` projects** through `DotProlog.Sdk`, an additive MSBuild SDK. A `.dpli` contract turns
  a Prolog predicate into an idiomatic .NET method, consumable from C#, F#, and Visual Basic.
- **`dotnet new` templates**: `prolog-console` and `prolog-lib`.
- **`dotnet prolog run`**, a .NET tool.
- **Testing.** `DotProlog.Testing` runs a project's `test_*` predicates under
  Microsoft.Testing.Platform, each in a fresh engine.
- **Embedding.** `PrologEngine.Query/1` enumerates solutions lazily, marshalling each binding into a
  `PrologValue`; `PrologHost` binds a predicate once and calls it as `Prove`, `CallOnce`, or
  `CallAll`.

### Compatibility

- Requires **.NET 10**. Packages are platform-neutral; NativeAOT publishing is exercised on
  Linux, Windows, and macOS.
- **No independent conformance claim.** 244 cases encoded from ISO/IEC 13211-1 pass, but they are
  DotProlog's own reading of the standard rather than a third-party suite. Known differences are
  listed in [COMPATIBILITY.md](COMPATIBILITY.md).
- There is no string type: an atom is the only text term, and the SWI-Prolog string predicates are
  absent rather than aliased to atoms.

### Known limitations

- `dotnet test` cannot yet drive a `.dplproj` test project; run the test host directly.
- No binary streams and no stream repositioning.
- No first-argument clause indexing, so a predicate with many facts is scanned linearly.
- `plc` and the foreign-predicate source generator are designed but not implemented.

**Full Changelog**: https://github.com/kidoz/dotprolog/commits/v0.1.0

[Unreleased]: https://github.com/kidoz/dotprolog/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/kidoz/dotprolog/releases/tag/v0.1.1
[0.1.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.1.0
