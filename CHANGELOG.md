# Changelog

All notable changes to DotProlog are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] — unreleased

The first release. Everything below is new.

### Language

- **Core.** Facts, rules, unification, backtracking, and the control constructs `,/2`, `;/2`,
  `->/2`, `*->/2`, `\+/1`, `!/0`, and `call/1..8`. Cut scopes as ISO specifies: opaque in the
  condition of if-then-else, transparent in its branches, clause-scoped elsewhere.
- **Exceptions.** `throw/1` and `catch/3`, with every engine error raised as a catchable
  `error(Formal, Context)` term.
- **Arithmetic** and the standard order of terms, including `compare/3`.
- **All solutions.** `findall/3`, `bagof/3`, `setof/3`, `forall/2`, and `aggregate_all/3`.
- **Database.** `assertz/1`, `asserta/1`, `retract/1`, `retractall/1`, `clause/2`, `abolish/1`, and
  `:- dynamic`, with logical-update-view semantics.
- **Standard library.** Text conversion, the list library, sorting, higher-order predicates,
  `copy_term/2`, `term_variables/2`, `succ/2`, `plus/3`, and `format/1,2,3` with column stops.
- **Operators.** `op/3` and `current_op/3`, with a term writer that honours them; whatever
  `writeq/1` produces reads back as the same term.
- **Grammars.** `-->/2` translated at load time, with `phrase/2,3`, `{}/1`, pushback lists, and
  terminals written as lists or double-quoted strings.
- **Streams.** `open/3,4`, `close/1`, the current-stream predicates, `read/1,2`, `read_term/2,3`,
  character I/O, `with_output_to/2`, `term_to_atom/2`, and `read_term_from_atom/3`.
- **Modules.** `:- module/2` hides what its export list omits, with `use_module/1,2`,
  `Module:Goal`, and `:- meta_predicate/1`.

### Toolchain

- **`.dplproj` projects** through `DotProlog.Sdk`, an additive MSBuild SDK.
- **Generated facades.** A `.dpli` contract turns a Prolog predicate into an idiomatic .NET method,
  consumable from C#, F#, and Visual Basic.
- **`dotnet new` templates**: `prolog-console` and `prolog-lib`.
- **`dotnet prolog run`**, a .NET tool.
- **NativeAOT.** A `.dplproj` publishes to a self-contained native executable with no trimming or
  AOT warnings, and consults Prolog files it has never seen at run time.
- **Testing.** `DotProlog.Testing` runs a project's `test_*` predicates under
  Microsoft.Testing.Platform.

### Embedding

- `PrologEngine.Query/1` enumerates solutions lazily, marshalling each binding into a
  `PrologValue`.
- `PrologHost` binds a predicate once and calls it as `Prove`, `CallOnce`, or `CallAll`.

### Known limitations

- `dotnet test` cannot yet drive a `.dplproj` test project; run the test host directly. See the
  README.
- No conformance claim. DotProlog is not verified against the ISO conformance suite, and does not
  claim ISO or SWI-Prolog compatibility. See [COMPATIBILITY.md](COMPATIBILITY.md).
- A cut inside a goal reached through `call/1` is local to that goal.
- No binary streams and no stream repositioning.
- No first-argument clause indexing, so a predicate with many facts is scanned linearly.

[Unreleased]: https://github.com/kidoz/dotprolog/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kidoz/dotprolog/releases/tag/v0.1.0
