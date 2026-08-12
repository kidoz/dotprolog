# Changelog

All notable changes to DotProlog are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The SWI-aligned library surface: `library(error)` (`must_be/2`, `is_of_type/2`, and the
  error-raising helpers), `library(assoc)` as AVL trees, `library(ordsets)`, `foldl/6`,
  `findall/4`, `numbervars/3`, `atom_to_term/3`, and `tab/2` — available in the `Extended` and
  `Modern` modes, rejected by `StrictIso`.
- A [SWI compatibility ledger](docs/reference/swi-compatibility.md) recording, feature by feature,
  which parts of the SWI-Prolog surface the extended modes implement, which are later roadmap
  phases, and which are out of charter.
- More SWI-aligned predicates: `del_assoc/4`, `transpose_pairs/2`, `ord_union/2`,
  `ord_intersection/2`, `aggregate/3,4` and `aggregate_all/4` for the simple specs, `variant/2`,
  `?=/2`, `char_type/2` and `code_type/2` for bound characters, `between/3` with an `inf` upper
  bound, and the `~r`/`~R` radix format directives.
- An opt-in differential suite that runs a shared goal corpus against a locally installed
  SWI-Prolog and asserts the outputs agree (`DOTPROLOG_RUN_SWI_DIFFERENTIAL_TESTS=1`); the corpus
  also runs unconditionally against DotProlog alone.
- Engine-scoped global variables: `nb_setval/2` and `nb_getval/2` store and read a detached copy
  that survives backtracking, while `b_setval/2` and `b_getval/2` hold the live term and the
  assignment is undone when execution backtracks past it, including through `catch/3` and at the
  end of the top-level goal.
- `setup_call_cleanup/3` and `call_cleanup/2`: the cleanup runs exactly once on deterministic
  exit (including the redo that exhausts the alternatives), failure, or a thrown ball, with SWI's
  ball precedence. A surrounding cut that discards pending alternatives does not fire the
  deferred cleanup; `once(Goal)` gives commit semantics.
- `setarg/3` and `nb_setarg/3`: destructive argument assignment, undone on backtracking for the
  first form through a value-undo stack interleaved with trail unwinding. `nb_setarg/3` accepts
  atomic replacement values only, which the counter idiom needs and heap truncation allows.
- The `occurs_check` flag (`false`, `true`, `error`) in the `Extended` and `Modern` modes,
  guarding general unification with SWI's `occurs_check(Var, Term)` error term. Write-mode head
  unification is a documented unchecked window; `StrictIso` keeps the ISO flag set unchanged.
- A natural-language processing sample in `Modern` mode (`samples/NaturalLanguage`), and the
  DotProlog logo on the project landing pages.
- `nb_current/2`, which fails for an unset name and enumerates the set variables when the name
  is unbound; `ord_seteq/2` and `ord_symdiff/3`, completing `library(ordsets)`; and the witnessed
  `max/2` and `min/2` aggregation specs across `aggregate/3,4` and `aggregate_all/3,4`, which
  compare arithmetically, keep the first solution on a tie, and answer `max(Value, Witness)` /
  `min(Value, Witness)`. All verified against SWI-Prolog by the differential corpus.
- The rest of SWI's `library(assoc)` surface: `is_assoc/1`, `gen_assoc/3`, `get_assoc/5`,
  `map_assoc/2,3`, `del_min_assoc/4`, and `del_max_assoc/4`.
- Compound aggregation templates: `aggregate_all(r(sum(X), count), Goal, r(Sum, Count))` and the
  same shape in `aggregate/3,4` and `aggregate_all/4`, each template argument a spec, with SWI's
  instantiation, type, and domain errors for invalid templates.
- The `~@` and `~W` format directives: `~@` runs a goal once and inserts its output in place —
  a failing or throwing goal still emits the text before it, as SWI's streaming does — and `~W`
  writes a term under a `write_term/2` option list. `write_term` itself gains SWI's
  `spacing(standard|next_argument)` option.
- `portray_clause/1,2` with SWI's listing layout — one goal per line, bracketed disjunction,
  if-then-else, and soft-cut blocks, `A`, `B`, ... variable names, `_` singletons — byte-identical
  to SWI-Prolog 10 for the covered constructs, pinned by the differential corpus.
- `char_type/2` and `code_type/2` enumerate an unbound character over a bound type in code
  order; characters are UTF-16 code units, so Unicode-wide classes cover the BMP.
- `term_size/2`: the cells of a detached copy of a term, counting shared subterms once per
  occurrence and raising `representation_error(cyclic_term)` for a cyclic one.
- Project-level initial flag overrides: the `DotPrologFlags` property in a `.dplproj` (and the
  repeatable `--flag` option on `dotnet prolog run` and `lint`) layers an initial flag value over
  the language mode, e.g. `double_quotes=chars` while staying in `extended` mode. The mode remains
  the curated profile; `double_quotes` (`codes`, `chars`, `atom`) is the first overridable flag.
  Generated code records the initial `double_quotes` value and refuses to install into an engine
  that starts elsewhere, the same way it already guards the language mode.

### Fixed

- Meta-called control goals now operate on the caller's live terms: the runtime lowering compiles
  only the control skeleton and passes every leaf-goal argument through a register instead of
  rebuilding bound terms, which destructive assignment made observable.
- A compiled predicate that lowered a meta-called control goal could crash the dispatch loop with
  an index error when appending the lowered bytecode grew the program's code array; the loop now
  refreshes its cached arrays after compiled execution returns.
- A build-time-compiled source containing `:- set_prolog_flag(double_quotes, ...)` no longer leaks
  that value into the host engine when its generated `Install` replays the directive: the flag is
  restored to its entering value afterwards, matching what consulting the same file leaves behind.

### Changed

- An aggregation template that is unknown (`aggregate_all(foo, ...)`), a variable, or a compound
  mixing specs with other terms now raises the error SWI raises instead of failing silently.
- `char_type/2` and `code_type/2` with an unbound character now enumerate instead of raising an
  instantiation error; the error remains when the type is unbound.
- `Modern` mode is chartered as the SWI-aligned dialect: an ISO-conforming core, `double_quotes`
  seeded at `chars`, and SWI-Prolog as the documented reference for extension behavior.
- Documentation no longer states that ISO/IEC 13211-1 fixes the initial `double_quotes` value at
  `codes`; the standard leaves it implementation defined (7.11.2.5), and `codes` is DotProlog's
  documented choice for the `Extended` and `StrictIso` modes.

## [0.5.0] — 2026-08-03

A conformance release. `StrictIso` now implements the ISO Prolog family rather than its core alone:
ISO/IEC 13211-1:1995 with Technical Corrigenda 1:2007, 2:2012, and 3:2017, the ISO/IEC 13211-2:2000
module system, and the ISO/IEC TS 13211-3:2025 definite clause grammars. Two traceability ledgers
publish the licensed-text audit, one row per normative area with the executable evidence that
covers it, and the audit is what found the defects fixed below.

Modules moved to the standard interface-and-body representation. `Extended` and `Modern` keep the
Quintus-family declarations they already accepted, so existing programs are unaffected; strict mode
takes the standard spelling only.

### Added

- ISO/IEC 13211-2 module interfaces and bodies, including export, import, re-export,
  metapredicates, module-local reader state, visible-database reflection, context-sensitive I/O and
  database operations, static clause inspection, and the fixed `colon_sets_calling_context` flag.
- Generated-C# and NativeAOT preservation of Part 2 module metadata and retained static clauses.
- The Corrigendum 3 `variable_names/1` write option. It writes the leftmost applicable name for a
  variable and does not bind the option's terms.
- Traceability ledgers for [Part 1](docs/reference/iso-part1-conformance.md) and
  [Parts 2 and 3](docs/reference/iso-parts2-3-conformance.md). Each maps a normative area to the
  focused, corpus, generated-C#, cross-path, and NativeAOT evidence that covers it, and paraphrases
  rather than reproduces the licensed publications. The processor-characteristics reference records
  the choices they point at.
- A pinned Part 1 inventory of the predefined predicates and evaluable functors, so adding or
  removing one has to be a deliberate change rather than a silent drift.
- Corrigenda cases in the repository conformance corpus, which now stands at 608 and continues to
  run on every execution path.

### Changed

- StrictIso accepts the Part 2 `module/1` interface and `body/1` representation and rejects the
  Quintus-family `module/2`, `use_module/1,2`, and `meta_predicate/1` compatibility declarations.

### Fixed

- Runtime-built DCG bodies are expanded into one goal before execution, so `|` alternatives and
  grammar or embedded cuts have the same behavior as statically translated grammar rules.
- `phrase/3` is steadfast in its third argument, and strict mode treats a runtime-built soft cut as
  an ordinary nonterminal just as it does in static grammar rules.
- `phrase/2` reports the Part 3 `list` type for an invalid input sequence.
- Strict mode no longer inherits the extended predefined operators `:=`, infix `.`, and `$`. The
  strict initial table omits them; `Extended` and `Modern` keep them, and a strict program can
  still declare its own permitted operators with `op/3`.
- `op/3` rejects every prefix or postfix declaration of `|`, not only those below priority 1001.
- `sort/2`, `sort/4`, `msort/2`, and `keysort/2` validate the result argument before unifying with
  it, and `keysort/2` checks the pairs on both sides, so an improper result raises its ISO error
  instead of failing.
- `open/4`, `close/2`, and `write_term/2,3` reject an unknown option name with the domain error for
  that option type, before the option's value is inspected.

### Compatibility

- Part 2 module text is interfaces and bodies. Source that uses `module/2`, `use_module/1,2`, or
  `meta_predicate/1` still loads in `Extended` and `Modern`, and must move to the standard
  declarations to load under `StrictIso`.
- Removing `:=`, infix `.`, and `$` from the strict initial operator table changes how strict
  source that used them reads. Such source was outside Part 1 to begin with; `Extended` is
  unchanged.
- `current_prolog_flag/2` enumerates ten flags: the nine from Part 1 and the Part 2
  `colon_sets_calling_context`. A program that counted the flag set has to count ten.
- The complete applicable independent Part 1 corpus passes on every execution path. Parts 2 and 3
  rest on licensed traceability plus focused managed, generated-C#, and NativeAOT evidence, not on
  an independent suite. None of this is a claim of SWI-Prolog compatibility.

## [0.4.0] — 2026-08-02

A linting release. `dotnet prolog lint` reads source and reports on it without consulting it or
running a directive, so a file can be checked without being trusted. It ships with the semantic
rules on by default and an opt-in `covington` profile carrying the layout guidelines that are
automatically checkable, with every threshold configurable per project.

### Added

- A non-executing `dotnet prolog lint` command with stable `DPL3xxx` diagnostics for singleton
  variables and repeated underscore-prefixed singleton markers. It accepts multiple files and the
  shared language modes; warnings remain advisory unless `--warnings-as-errors` is selected.
- A reusable `PrologLinter` API in `DotProlog.Compiler`, package-consumer coverage for the installed
  tool, and source-linting documentation.
- An opt-in `covington` lint profile for spaces, indentation, line and clause length, comma spacing,
  clause and subgoal layout, and trailing whitespace. Numeric layout limits are configurable while
  the default profile remains semantic-only.

### Changed

- The codebase uses implicit types and the current language forms — pattern matching, switch
  expressions, collection expressions, and range and index operators. `.editorconfig` now states
  those preferences, so the style is expressed where tooling can apply it rather than by convention
  alone. No observable behaviour changes, and the engine benchmarks are unmoved.

## [0.3.0] — 2026-08-01

A language-mode release. A mode now carries the initial ISO flag values that go with its predefined
surface, not just the surface itself, and the new opt-in `Modern` mode starts `double_quotes` at
`chars` — so a double-quoted token reads as a list of one-character atoms, which is the convention
the newer Prolog systems settled on and what makes DCGs over text readable. `Extended` remains the
default and keeps the ISO initial value `codes`, so existing programs are unaffected.

Selecting a mode is now done by name everywhere, which replaces the 0.2.0 strict-ISO booleans.

### Added

- A `Modern` language mode: the extended predefined surface with `double_quotes` seeded at `chars`.
  It is available from the embedding constructor, `dotnet prolog`, generated code, and `.dplproj`
  builds, and generated source still refuses to install into an engine created in a different mode.
- `dotnet prolog run --mode extended|strict-iso|modern`, and the `DotPrologLanguageMode` property
  for `.dplproj` projects. Mode names parse case-insensitively from one shared table, so the command
  line and MSBuild cannot drift apart.
- `BytecodeProgram.InitialDoubleQuotes`, recording the value the flag was seeded with before any
  source was read.
- A `TextGrammar` sample: a `.dplproj` application in `Modern` mode that decomposes a string, sums a
  run of numbers, and splits a sentence into words with DCGs written against characters.
- NativeAOT acceptance coverage for `Modern` mode, exercising build-time compiled clauses, the
  reader running inside the published binary, a consulted grammar, load-unit flag scope, and the
  mode-mismatch guard.
- Benchmarks comparing engine construction and consulting across language modes.

### Changed

- `double_quotes` is scoped to the load unit. A `set_prolog_flag(double_quotes, _)` directive still
  governs the rest of the file that issued it, and the value in force when that file began is
  restored when it finishes, including when a directive throws. A library that declares its own
  convention can no longer change how whatever is consulted next is read. Deferred
  `initialization/1` goals observe the restored value.
- The bundled bootstrap and standard libraries are read under `codes` in every mode; they are
  processor implementation, so a host's choice of dialect does not reinterpret them.
- A program's language mode is validated against an explicit allowlist, so a mode cannot be accepted
  before it has been given its initial flag values.

### Removed

- `dotnet prolog run --strict-iso`, replaced by `--mode strict-iso`.
- The `DotPrologStrictIso` MSBuild property, replaced by `DotPrologLanguageMode`. A project setting
  the old property now fails the build rather than silently ignoring it.

### Compatibility

- The initial `double_quotes` value stays `codes` in `Extended` and `StrictIso`, as ISO/IEC 13211-1
  requires. `Modern` is an extension and sits outside the conformance claim.
- Scoping `double_quotes` to the load unit changes an observable behavior that no previous release
  pinned. Source that relied on a directive outliving its file must set the flag in each file that
  needs it.

## [0.2.0] — 2026-08-01

The second release of **DotProlog**, and the first with independent conformance evidence: all 768
applicable declarations of the pinned Logtalk 3.101.0 ISO Prolog corpus pass — as consulted
bytecode, as generated C#, across both compiled↔bytecode boundaries, and from a published
NativeAOT executable. The engine gained first-argument clause indexing, the language gained an
opt-in strict ISO mode, and `dotnet test` now drives Prolog test projects directly.

A changelog section dated 0.1.1 (2026-07-30) was never shipped — no tag, no GitHub release, and
nothing on NuGet.org — so its content is folded in here and this release is 0.2.0.

### Added

- First-argument clause indexing in the bytecode VM. Static multi-clause predicates dispatch
  through a clause table keyed on the first argument, and dynamic predicates skip clauses whose
  first argument could never unify — under the unchanged logical update view. A call with a bound
  first argument that can only reach one clause creates no choice point. Solutions and their order
  are unchanged; build-time generated C# keeps its existing in-order clause chains.
- The pinned 768-case independent ISO corpus now runs exhaustively through generated C#,
  compiled-to-bytecode, bytecode-to-compiled, and NativeAOT paths in CI.
- Part 3 DCG semicontexts and `Name//Arity` declarations for `dynamic/1`, `multifile/1`, and
  `discontiguous/1`.
- An opt-in strict ISO language mode for embedding, `dotnet prolog`, generated code, and
  `.dplproj` builds. It rejects known predefined extensions during source preparation and at
  runtime meta-call and host-binding boundaries.
- A `prolog-test` template whose `test_*` predicates are discovered and run by `dotnet test`.
- A clean local-feed consumer gate that installs the packed templates and .NET tool, then builds,
  runs, NativeAOT-publishes, and tests generated projects from a path containing spaces.
- A uv-managed MkDocs documentation site on Python 3.14 with getting-started, language, .NET
  integration, architecture, and contributing guides, plus strict link validation in CI.
- The ISO evaluable functors for trigonometry, logarithms, exponentials, rounding, float
  decomposition, and bitwise complement.
- ISO `unify_with_occurs_check/2` with transactional failure and cycle-safe rational-tree
  traversal.
- ISO `current_predicate/1` enumeration for static, dynamic, declared-empty, and runtime-created
  user procedures.

### Changed

- Module preparation now requires one leading `module/2` declaration and rejects malformed,
  unexported, or conflicting selected imports.
- DCG processing uses the Part 3 `|` operator priority, rejects reserved and predefined grammar
  heads, and applies the specified `phrase/2` and terminal-sequence validation.
- Extended DCGs lower soft cut consistently in static and runtime-loaded source; strict mode treats
  the additional control as an ordinary nonterminal.
- The repository test suite now uses Microsoft.Testing.Platform v2 through the .NET 10
  `global.json` test-runner contract, so xUnit and Prolog test projects run together.
- Release checksums now cover packages, symbols, the SBOM, and all native binaries; the CycloneDX
  tool is pinned, and a missing NuGet API key fails rather than silently skipping publication.
- Child build processes close standard input, are killed with their process tree if they exceed the
  integration-test limit, and every CI and release job has an explicit timeout.
- `DotProlog.Sdk` now carries a real package title and description.
- Arithmetic now enforces ISO operand signatures, float division result types, bounded-overflow
  errors, and the distinct exceptional cases for zero division, undefined results, and float
  overflow.
- The repository's ISO-derived conformance corpus now contains 563 passing cases.
- Integration tests no longer tolerate the zero-tests-ran exit code, child-process output reads
  share the process deadline instead of waiting on a held pipe forever, and a failing release
  verify platform no longer cancels its siblings.

### Fixed

- The repository gates pass again on Windows and Linux: conformance and strict-ISO runner
  rebuilds now use isolated build outputs instead of overwriting the shared test assembly that a
  later no-build step executes, MSBuild worker nodes no longer outlive their step holding task
  assemblies locked, host paths interpolated into quoted atoms escape their backslashes, the
  Logtalk adapter emits the same line endings on every platform, and the packed-package consumer
  gate finds `nuget.config` on case-sensitive filesystems.
- The machine protected new environment frames only against the newest choice point, so a frame
  deallocated by last-call optimisation could be overwritten while an older choice point still
  referenced it. Solutions were silently dropped — `forall/2` with a compound action and
  `findall/3` over goals with in-clause disjunction or negation were the visible cases — and an
  uncaught exception could vanish while backtracking through `catch/3`. The protection watermark
  is now monotone up the choice-point stack.
- `retract/1` and `clause/2` re-read the database generation on every redo, letting clauses
  asserted after the goal started appear mid-enumeration. Both now keep the generation captured at
  the first solution and resume from a stable clause cursor, so `asserta/1` between solutions can
  no longer shift or repeat answers.
- Copying a cyclic term — through `copy_term/2`, `findall/3`, `assertz/1`, or `throw/1` — looped
  forever. It now raises catchable `representation_error(cyclic_term)`, and the term writer prints
  cycles as `...` instead of hanging.
- Every meta-call of a control term compiled a fresh clause into the append-only program. Compiled
  control goals are now cached by shape, so long-running loops no longer grow memory without bound.
- Integer division by zero always raises `evaluation_error(zero_divisor)`; only float `0.0/0.0`
  remains `undefined`.
- `atom_chars/2`, `atom_codes/2`, `number_chars/2`, and `number_codes/2` with a bound first
  argument now convert it and unify with the list instead of raising `instantiation_error`.
- Number conversion no longer lets oversized float literals become IEEE infinities or wraps
  oversized radix literals; out-of-range input raises the reader's `syntax_error(float_overflow)`
  and `representation_error(max_integer|min_integer)`.
- `format/3` accepts real stream handles and aliases, and `format(user_error, ...)` writes to the
  error stream instead of the current output.
- `phrase/2,3` treated a run-time if-then-else as a plain disjunction and offered the else branch
  as an extra solution.
- `writeq/1` emits the named ISO escapes and delimited hex escapes for control characters, so its
  output always reads back.
- The build task deletes generated facades whose contract was removed or renamed, `dotnet clean`
  removes the generated directory, and facade generation reruns when the project file or the set
  of source files changes — not only when a surviving file's timestamp moves.
- Contract mistakes that previously escaped into raw C# compiler errors — or crashed the build
  task — are reported as `DPL2011`–`DPL2014` diagnostics, and a `nondet` export with no outputs
  streams one unit value per solution as ADR 0006 promises.
- Prolog test projects honour run filters, capture `user_error` into failure reports, and fail a
  looping test after a configurable per-test timeout instead of hanging `dotnet test`.

### Compatibility

- Requires **.NET 10**. Packages are platform-neutral; NativeAOT publishing is exercised on Linux,
  Windows, and macOS in CI.
- **Independent evidence, but still no full ISO claim.** The 768 applicable declarations of the
  pinned Logtalk 3.101.0 corpus pass on every execution path, and the repository's own 563 cases
  encoded from ISO/IEC 13211-1 pass as well. Licensed-text traceability and the Part 2 module and
  Part 3 grammar completion gates remain open. See [COMPATIBILITY.md](COMPATIBILITY.md).
- The strict ISO mode is **opt-in**; the default profile is unchanged, so existing programs that
  use predefined extensions keep working.
- There is still no string type: an atom is the only text term, and the SWI-Prolog string
  predicates are absent rather than aliased to atoms.

### Known limitations

- `plc` and the foreign-predicate source generator are designed but not implemented.
- First-argument indexing applies to the bytecode VM; build-time generated C# still tries a
  predicate's clauses in order.
- A character is a UTF-16 code unit, so `atom_length/2` counts a character outside the Basic
  Multilingual Plane as two.

**Full Changelog**: https://github.com/kidoz/dotprolog/compare/v0.1.0...v0.2.0

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

[Unreleased]: https://github.com/kidoz/dotprolog/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.5.0
[0.4.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.4.0
[0.3.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.3.0
[0.2.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.2.0
[0.1.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.1.0
