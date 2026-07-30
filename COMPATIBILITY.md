# Compatibility

## The claim

DotProlog **does not claim ISO or SWI-Prolog compatibility**, and will not until published
conformance tests verify it. What follows is what is implemented, what is deliberately absent, and
where behaviour is known to differ. It is a description, not a conformance statement.

## What has been measured

**500 conformance cases encoded from ISO/IEC 13211-1 and its published corrigenda, all passing.**
They live in
[`tests/conformance/iso_conformance.pl`](tests/conformance/iso_conformance.pl) as ordinary Prolog —
a goal, and what the standard says that goal does — and run as part of the test suite.

Clause 7 cases cover reading terms from text: number syntax including character, hexadecimal,
octal, and binary literals; operator priority and associativity; list and curly notation; and what
`writeq/1` produces, down to the exact codes for a quoted atom. Clause 8 cases cover unification,
type testing, term comparison, term construction and decomposition, arithmetic and its comparisons,
the clause and database predicates, all-solutions predicates, text and binary streams, character
and byte I/O, term input and output, `op/3`, logic and control, and atomic term processing — with
an emphasis throughout on the error terms, which is where implementations diverge most. Sorting
is included too, though it sits outside clause 8.

**These cases are our own encoding of the standard, not third-party verification.** Ulrich
Neumerkel's conformity tables carry no licence, and the Logtalk suite is written against its own
test framework, so neither could be vendored. Reading the standard and encoding it is worth more
than nothing and less than an independent suite, and this file will say so until an independent one
has been run.

Writing them was worth it immediately: they found these real defects.

- `assertz(4)` raised an exception `catch/3` could not catch, aborting the run. Several standard
  errors were raw messages rather than Prolog terms.
- `functor(F, foo(a), 1)` gave `type_error(atom, …)` where the standard says `type_error(atomic, …)`.
- `X =.. [_, a]` gave a type error where the standard says `instantiation_error`.
- `call((fail, 4))` failed instead of raising `type_error(callable, (fail,4))`; a meta-called goal
  is now checked whole before any of it runs.
- `atom_chars(1.0, ['1', '.', '0'])` failed. With its first argument already bound the predicate
  built an atom from the list and unified, so a number could never match; it now compares text.
- `call((!, fail ; true))` succeeded because the cut could not prune the meta-called disjunction.
  Meta-called control now goes through the same bytecode lowering and cut barriers as source-level
  control.
- Exact integer quotients from `/2` were returned as integers rather than floats, float zero
  divisors leaked IEEE infinities, and bounded integer overflow could wrap before it reached a term.
  Arithmetic now preserves the ISO result kinds and raises catchable evaluation errors.
- Several ISO evaluable functors were absent, and float-only functions accepted integers. The
  conformance corpus now covers the mathematical, rounding, float-decomposition, and bitwise
  functors together with their operand signatures and exceptional results.
- `unify_with_occurs_check/2` was absent. It now rejects only cycle-creating bindings, restores
  tentative bindings after failure, and terminates while inspecting an existing rational tree.
- `current_predicate/1` was absent. It now enumerates user procedures on backtracking, distinguishes
  built-ins and bundled libraries from user definitions, includes declared-empty procedures, and
  stops reporting an abolished procedure without invalidating calls that already started.
- Prolog flags were accepted as inert declarations. The fixed and mutable ISO flags are now
  enumerable and validated; `double_quotes` controls source and runtime term reading, while
  `unknown` consistently controls direct, meta-called, and host calls.
- Character streams had only atom-valued input and output. `get_code/1,2`, `peek_code/1,2`, and
  `put_code/1,2` now implement code-valued input, lookahead, EOF, and ISO argument errors.
- `read_term/2,3` omitted `singletons/1`, and `variables/1` omitted anonymous variables. All three
  ISO variable-reporting options now preserve first-occurrence order and share with the read term.
- `stream_property/2` now enumerates explicit stream metadata and live `not`, `at`, and `past` EOF
  states; `current_stream/1` enumerates open handles without reflection.
- `current_input/1` and `current_output/1` now reject bound aliases, malformed handles, and stale
  handles with the exact ISO `domain_error(stream, Culprit)` instead of silently failing.
- `open/4` accepted only text streams. Its `type(text|binary)` option now selects encoded text or
  raw-byte storage, and the byte predicates enforce ISO byte domains, stream types, lookahead, and
  EOF behavior.
- Disk streams now expose opaque positions and are repositionable by default.
  `set_stream_position/2` restores logical text positions across parser lookahead and newline
  normalization as well as raw binary offsets; `reposition(false)` is enforced explicitly.
- `char_conversion/2` and `current_char_conversion/2` now maintain program-owned mappings with
  snapshot-stable enumeration. The `char_conversion` flag gates conversion before unquoted lexical
  classification, while quoted delimiters and text, escapes, character-code literal payloads, and
  primitive character input remain unchanged.
- `close/2` now validates `force(true|false)` and makes forced cleanup best-effort. Input streams
  own their selected `eof_action(error|eof_code|reset)`, report it as a property, and apply it
  consistently to term, character, code, and byte input after the first EOF marker.
- `open/3,4` now raises `uninstantiation_error/1` for a bound stream output and rejects an alias
  already owned by an open stream. Both checks happen before opening or truncating the source/sink,
  so error handling cannot leak a stream or replace an alias.
- A non-variable value outside the ISO `source_sink` domain now raises
  `domain_error(source_sink, Culprit)` from `open/3,4` instead of treating every source/sink as an
  atom-typed argument.
- Source/sink atoms that cannot denote a host pathname, including embedded null characters, now
  remain catchable as `domain_error(source_sink, Culprit)` instead of leaking a .NET exception.
- Stream permission errors now retain the caller's actual alias or handle as their culprit instead
  of manufacturing a predicate indicator. Malformed negative and oversized stream handles are
  rejected as domain errors rather than wrapping to another live identifier.
- `get_char/1,2` and `peek_char/1,2` now validate bound inputs as the ISO `in_character` type before
  consuming anything. One-character atoms and `end_of_file` are accepted; other terms raise the
  exact `type_error(in_character, Culprit)`.
- Character, code, and byte I/O now apply ISO error priority when more than one argument is invalid:
  explicit stream variables win first, value type checks precede stream lookup, and character-code
  representation checks follow stream permissions.
- `open/4`, `close/2`, `read_term/3`, `write_term/3`, and `set_stream_position/2` now separate
  option-list shape, argument domains, option semantics, stream lookup, and permissions so
  simultaneous errors are raised in ISO priority order without performing I/O.
- Host reader and writer failures now become catchable ISO `system_error` terms throughout stream
  input, output, EOF inspection, positioning, formatting, flushing, and closing. Implicit-current
  output predicates also enforce the same text/binary permissions as explicit-stream calls.
  Closing a disposed host stream follows the same rule, while `force(true)` suppresses the failure.
- `write_term/3` now writes to an explicit stream, and the ISO `quoted/1`, `ignore_ops/1`, and
  `numbervars/1` options are strict booleans with rightmost precedence. Numbered variables use the
  portable `A` through `Z`, `A1`, `B1`, and later naming sequence.
- `read_term/2,3` now validates its complete option list before asking the stream for input.
  Unknown options, variable elements, and partial lists therefore raise their ISO errors without
  consuming the next term.
- Runtime term input now raises catchable `representation_error(max_integer|min_integer)` when a
  decimal or radix literal exceeds the bounded tagged-integer range. Arbitrarily long literals
  retain their sign, and a rejected stream term does not discard the following term.
- Runtime term input enforces the advertised `max_arity` of 255. Reading a compound with 256 or
  more arguments raises catchable `representation_error(max_arity)`, including when the compound
  is nested, without discarding the following stream term.
- Runtime term input never turns an oversized decimal float into an incidental IEEE infinity.
  Because the ISO core does not assign oversized float input a representation flag, it raises the
  catchable `syntax_error(float_overflow)`; underflow follows the ISO-compatible choice of rounding
  to signed zero. Rejected stream input still leaves the following term available.
- `halt/1` now validates its input mode before requesting process termination. A variable raises
  `instantiation_error`, and any non-integer status raises `type_error(integer, Culprit)` instead of
  silently terminating with status zero.
- `compare/3` validates a bound order before comparing its terms. A non-atom raises
  `type_error(atom, Culprit)`, while an atom outside `<`, `=`, and `>` raises
  `domain_error(order, Culprit)` instead of ordinary failure.
- `current_op/3` validates bound filters before enumeration. Invalid priorities and specifiers raise
  their ISO domain errors, and a non-atom operator name raises `type_error(atom, Culprit)` rather
  than being treated as a query with no solutions.
- `current_op/3` now captures an immutable operator-table version when enumeration begins. Later
  `op/3` calls neither remove old definitions from that goal nor add new definitions to it, and
  bound filters scan the entire captured version rather than stopping at the first non-match.
- `clause/2` now protects static and built-in predicates as private procedures and validates a
  bound body as callable. `retract/1` and `retractall/1` consistently reject static procedures
  with a modification permission error instead of silently failing.
- `arg/3` now raises `instantiation_error` when its compound term is a variable and
  `domain_error(not_less_than_zero, Index)` for a negative index. Zero and indexes above the
  compound's arity retain their specified ordinary failure behavior.
- `=../2` now distinguishes partial lists from non-list and improper-list inputs, and validates
  that a one-element construction list contains an atomic term. Both `=../2` and `functor/3`
  accept arity 255 and raise `representation_error(max_arity)` at 256.
- `repeat/0` now supplies the ISO infinite sequence of retry points. Focused and NativeAOT tests
  use persistent database state to prove that failure after the first success really re-enters it
  until a cut commits the loop.
- A literal non-callable clause body is now accepted as a Prolog term and raises its catchable
  `type_error(callable, Culprit)` if execution reaches it. It no longer becomes a consultation
  diagnostic, and an unreachable body after `fail` does not raise.
- The control audit now covers goal modes for `once/1`, `call/1..8`, and `catch/3`, including
  recovery-goal validation and errors caught by the active catcher itself. A `call/8` result at
  arity 255 executes, while arity 256 raises `representation_error(max_arity)`.
- The flag audit covers all nine ISO flags, each fixed flag's modification permission, and every
  mutable value domain. Modules enumerate the same core set without exposing extension-specific
  flags, and NativeAOT retains the same values and permissions.
- Quoted atom spelling no longer suppresses the atom's operator definition. ISO prefix, infix, and
  postfix interpretation therefore applies equally to quoted default and dynamically declared
  operator names in compiled source and runtime term input.

Beyond the conformance cases, the engine and toolchain are covered by 1043 xUnit cases and seven
Prolog tests run through `dotnet test`, plus an opt-in integration suite that builds and runs the
C#, F#, and Visual Basic samples and exercises NativeAOT.

## Implemented

| Area | Status |
|---|---|
| Terms, occurs-check unification, backtracking, cut | Implemented |
| Control constructs, `call/1..8` | Implemented |
| Exceptions, ISO `error/2` terms | Implemented |
| Arithmetic, standard order, `compare/3` | Implemented |
| `findall/3`, `bagof/3`, `setof/3`, `forall/2` | Implemented |
| Database predicates, predicate enumeration, logical update view | Implemented |
| Text, list, sorting, and higher-order library | Implemented |
| `op/3`, `current_op/3`, operator-aware writing | Implemented |
| DCGs, `phrase/2,3` | Implemented |
| Repositionable text and binary streams; close options; configurable EOF actions; term, character, code, and byte I/O | Implemented |
| Modules, `use_module/1,2`, `meta_predicate/1` | Implemented |
| ISO Prolog flags and state-dependent reading/calls | Implemented |
| Character conversion and state-dependent lexical reading | Implemented |

## Known ISO gaps

| Feature | Current state |
|---|---|
| Complete standard clause audit | Syntax, modes, and remaining non-stream errors still require a clause-by-clause audit |
| Independent ISO verification | The repository corpus is not a substitute for a published independent suite |

## Non-ISO features absent deliberately

| Feature | Why |
|---|---|
| A string type, and the SWI string predicates | An atom is the only text term. Aliasing the string predicates to atoms would let portable code compile here and then behave differently. |
| Constraint solving, tabling, attributed variables | Out of scope for 0.1.0. |

## Known differences

| Behaviour | DotProlog | Elsewhere |
|---|---|---|
| `\+ 4` | `type_error(callable, 4)` — the inner goal | Same |
| A character is a UTF-16 code unit | A character outside the Basic Multilingual Plane is two codes, and `atom_length/2` counts it as two | SWI counts code points |
| Two modules exporting the same name | The first loaded gets the unqualified name | SWI reports a conflict |
| Clause selection | Linear scan; no first-argument indexing | Indexed |
| Arithmetic extensions | `div/2`, evaluable `integer/1`, `e/0`, `inf/0`, `nan/0`, and several utility functions remain available | Not part of the ISO core |

## Platforms

Built and tested on .NET 10. NativeAOT publishing is exercised on the host platform of whatever
machine runs the acceptance test; the packages themselves are platform-neutral.
