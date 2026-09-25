# ISO processor characteristics

This page records the processor-defined choices that accompany DotProlog's ISO/IEC 13211
implementation. Requirement and execution-path evidence is recorded separately in the
[Part 1](iso-part1-conformance.md) and [Parts 2 and 3](iso-parts2-3-conformance.md) ledgers.

## Strict ISO mode

`PrologLanguageMode.StrictIso` is an immutable, program-owned processor mode. It admits the
explicitly inventoried predefined surface from ISO/IEC 13211 Parts 1, 2, and 3 and rejects known
predefined DotProlog extensions. Source preparation reports `DPL1018`; a runtime-constructed
meta-goal or host binding raises the catchable term
`permission_error(access, implementation_specific_feature, Name/Arity)`.

The bundled implementation library is trusted below this boundary so it may use private helpers
to implement standardized predicates. User-defined predicate names are unrestricted. The default
mode is `Modern`, which keeps the documented extensions.

## Representation limits

| Characteristic | DotProlog choice |
|---|---|
| Integer model | Unbounded, promoted from 60-bit tagged fixnums |
| `min_integer` | −576460752303423488 (the fixnum promotion threshold) |
| `max_integer` | 576460752303423487 (the fixnum promotion threshold) |
| `bounded` | `false` |
| Maximum predicate and compound arity | 255 |
| Float model | Finite IEEE 754 binary64 |
| Integer division rounding | Toward zero |

Integer arithmetic is unbounded: results outside the tagged fixnum range promote to an interned
big-integer representation, results that re-enter the range normalize back, and source literals of
any length read to their exact value. The `max_integer` and `min_integer` flags report the fixnum
bounds the way SWI-Prolog reports its word-size bounds beside GMP; the `max_tagged_integer` and
`min_tagged_integer` evaluables name the same threshold. Interned big integers live for the
program's lifetime, an embedding consideration beside atom growth. Converting a big integer to a
float that overflows binary64 raises `evaluation_error(float_overflow)`; a shift count or exponent
whose result would exceed the big-integer representation raises `resource_error(memory)`. Float
arithmetic rejects NaN and infinity with the applicable ISO evaluation error. A decimal float
literal that overflows binary64 is a `syntax_error(float_overflow)`; underflow rounds to signed
zero.

## Bitwise arithmetic

Bitwise functions use two’s-complement signed integer semantics. In particular, DotProlog fixes the
implementation-defined examples as follows:

| Expression | Result |
|---|---:|
| `\ 10` | −11 |
| `-10 \/ 12` | −2 |
| `-10 /\ 12` | 4 |
| `xor(-10, 12)` | −6 |
| `-16 << 2` | −64 |
| `-16 >> 2` | −4 |

Right shift is sign-extending. A left shift promotes past the tagged range instead of
overflowing.

## Text and syntax

Atoms and source text use .NET Unicode strings, and what counts as one character depends on the
mode. In `Modern`, a character is a Unicode code point: a code from 0 to 0x10FFFF outside the
surrogate range 0xD800–0xDFFF. A character outside the Basic Multilingual Plane is therefore one
character with one code, although .NET stores it as two UTF-16 code units, and an unpaired
surrogate arriving in a host string is read as U+FFFD. In `StrictIso`, a character is a UTF-16
code unit, 0 to 0xFFFF with the surrogates included, so the same character is two. Character
predicates require a one-character atom in the mode's sense, and a code outside the mode's range
raises `representation_error(character_code)`.

The reader accepts Unicode source characters and ISO's numeric escapes; `\x…\` reaches 0x10FFFF in
`Modern` and 0xFFFF in `StrictIso`. `Modern` also reads SWI-Prolog's `\uXXXX` and `\UXXXXXXXX`
escapes, and a surrogate escape is a syntax error there.

The required portable characters have their Unicode/ASCII ordinal values. Extended characters are
classified before tokenization: Unicode uppercase letters and underscore begin variables, other
Unicode letters begin unquoted atoms, ASCII digits begin numbers, and other supported punctuation
is classified by the explicit graphic, solo, layout, and meta-character tables. `Modern` extends
the classes along Unicode's identifier properties: letter numbers also begin atoms, combining marks
and superscript digits continue them, and a symbol or punctuation character outside ASCII, other
than a bracket or quotation mark, is a solo character. `StrictIso` keeps the letter classes alone
and rejects every other character outside ASCII. Atom and character
collation is by character code: code-point order in `Modern` and code-unit order in `StrictIso`.
The two orders differ only where a character outside the Basic Multilingual Plane meets one from
U+E000 to U+FFFF. The byte sequence associated with a character is its UTF-8 encoding for a text
file; binary streams do not perform character conversion.

ISO/IEC 13211-1 leaves the initial `double_quotes` value implementation defined (7.11.2.5), and
this page is where DotProlog defines it: `chars` in the default `Modern` language mode, as in the
newer Prolog systems, and `codes` in `StrictIso`, as in the older ISO-oriented ones. Both come
from the flag's ISO domain. A host may move the initial value to any of the three ISO values in
any mode — through the `DotPrologFlags` project property, `dotnet prolog --flag`, or the engine
constructor's flag overrides — and the chosen value then plays the role the mode default
otherwise would. The value is scoped to the load unit: a `set_prolog_flag(double_quotes, _)`
directive governs the rest of the file that issued it, and the entering value is restored when
that file finishes loading.

`double_quotes` accepts the extension value `string` in `Modern` only — a directive, flag call, or project override selecting
it inside `StrictIso` stays a domain error, so the strict mode keeps the three ISO values.

The standard order of terms places strings between numbers and atoms:
`Var < Float < Integer < String < Atom < Compound`. The string slot is SWI-Prolog 10's probed
behavior; ISO leaves no slot for the type, and the float/integer split remains DotProlog's
documented divergence.

The initial `char_conversion` flag is `on` in `StrictIso` (Part 1 7.11.2.1) and `off` in `Modern`.
Character conversion applies to unquoted lexical input while quoted text, escapes, character-code
literal payloads, and primitive character input remain unchanged.

The Part 2 `colon_sets_calling_context` flag is fixed and has the value `true`.

`current_prolog_flag/2` preserves the values selected when enumeration begins, even when a
later goal changes flags before redo. An unsupported flag atom raises
`domain_error(prolog_flag, Flag)` in `StrictIso`; in `Modern` it fails.

`write_term/2,3` implements the Corrigendum 3 `variable_names/1` option. Its value is a list of
`Atom=Term` entries; the leftmost entry whose term is the variable being written supplies the
output name. Inspecting the list neither unifies nor otherwise binds its terms.

The `StrictIso` initial operator table contains the ISO Part 1 table as corrected by Corrigendum 2,
together with the documented Part 2 and Part 3 operators. Modern mode additionally
predefines the convenience directive operators and `:=`, `.` as an infix operator, and `$`.
`current_op/3` enumerates a captured table version in ordinal operator-name order and then
operator-specifier order. Mutating the table while an enumeration is active does not change that
enumeration.

Variables are ordered by their stable heap identity. The relative order of distinct variables is
therefore implementation-dependent, but it remains constant for the lifetime of a sorting or
solution-collection operation. Atoms use ordinal name order. Every float precedes every integer,
including numerically equal cross-kind values.

## Occurs-check and cyclic terms

The extension flag `occurs_check` (`false`, `true`, `error`) exists in `Modern` only and starts at
`false`. With `error`, a unification that would create a cycle raises
`error(representation_error(term), occurs_check(Var, Term))`. This is DotProlog's extension
behavior: neither the flag nor the representation flag `term` is defined by ISO.

`StrictIso` rejects attempts to read or set `occurs_check` with `domain_error(prolog_flag, occurs_check)`.
In both modes, ISO `unify_with_occurs_check/2` fails when unification would create a cycle;
Modern's flag does not change that predicate. ISO leaves ordinary cycle-producing unification
undefined (Part 1 7.3.4), so the new error is not an ISO requirement.

The error shape is a compatibility choice. The implementation comparison made for
[issue 6](https://github.com/kidoz/dotprolog/issues/6) found:

| Implementation | Error when its occurs-check flag is `error` | Evidence |
|---|---|---|
| SWI-Prolog | `error(occurs_check(Var, Term), Context)` | [Flag documentation](https://www.swi-prolog.org/pldoc/man?section=flags); confirmed locally with SWI-Prolog 10.0.2 |
| Scryer Prolog | `representation_error(term)` as the formal error | [Unifier source](https://github.com/mthom/scryer-prolog/blob/97b85690fbf58e9a794af04a0a8096b7fbe3e216/src/machine/unify.rs#L567-L575) |
| Trealla Prolog | `representation_error(term)` as the formal error | [Unifier source](https://github.com/trealla-prolog/trealla/blob/58bb70e879072f38a1e57731a9691b2cf61ed486/src/unify.c#L810-L813) |

DotProlog retains the new Modern error: it follows an existing representation-error convention
and preserves the rejected pair in the context. It does not claim identical contexts across
these systems. Matching SWI's formal error would instead preserve existing SWI handlers; a
single exception cannot match both shapes. No additional compatibility flag is introduced.

The distinction between an ISO error class and a standardized error instance matters here.
Part 1 7.12.2(f) lists neither `term` nor `cyclic_term` as representation flags, and the
[error-class reference linked from the issue](https://www.complang.tuwien.ac.at/ulrich/iso-prolog/error_k#error_classes)
lists the same set. The issue's proposed classification is useful extension guidance, not a
specified result for ordinary cycle-producing unification. Part 1 8.2.2 separately requires
`unify_with_occurs_check/2` to fail when no finite-term unifier exists.

Existing `representation_error(cyclic_term)` errors are retained for operations that cannot
handle an already cyclic value. Renaming them would break more handlers without adding cycle
support. These paths include copying, collecting answers, asserting clauses, measuring detached
term size, and compiling cyclic control goals. SWI itself uses that error for cyclic clauses;
its [rational-tree support](https://www.swi-prolog.org/pldoc/man?section=cyclic) also shows that
copying cycles and rejecting their creation are separate capabilities.

The flag still has a documented gap: write-mode head unification can create a cycle without
passing through its check. Changing the error class does not close that gap; see the
[SWI compatibility ledger](swi-compatibility.md). Tests cover the mode boundary, all three Modern
flag values, generated and consulted calls in both directions, and the new error in NativeAOT.

## Source preparation and goal delivery

Hosts prepare text with `PrologEngine.ConsultText` or `ConsultFile`; applications use a `.dplproj`,
and the command-line surface uses `dotnet prolog run`. A host delivers a goal through
`PrologEngine.Query`, `RunGoal`, or a bound `PrologHost` predicate. Success and failure are returned
as host results, while bindings are exposed as solution values.

`include/1` inserts text at the directive position, resolves relative names from the containing
file, and shares the containing reader's operator, flag, and character-conversion state.
`ensure_loaded/1` also acts at its directive position, but canonical file identity ensures that a
source unit is prepared only once. Initialization goals execute in source order after successful
preparation. A failing ordinary directive prints a warning and preparation continues; an error it
raises and does not catch stops preparation and reaches the host, and `dotnet prolog run` reports
it with exit status 70. `initialization/1` remains deferred.

Operator and character-conversion directives affect the rest of their load unit and later runtime
term reading by the same program. Their program-owned tables remain in force for subsequently
loaded units. `set_prolog_flag/2` directives likewise change program state, except that
`double_quotes` is load-unit scoped: its entering value is restored when that unit finishes.

## Procedures, errors, and streams

The initial `unknown` flag is `error`, so calling an undefined procedure raises
`existence_error(procedure, Name/Arity)`. Dynamic predicates use the logical update view.

The `debug` flag starts as `off`. Setting it to `on` records the requested state but does not alter
goal execution; DotProlog currently has no processor debugger. The error context in
`error(Formal, Context)` is a fresh variable. If multiple error conditions apply, the explicitly
tested argument and option validation order of the affected predicate determines which error is
reported.

`halt/0` requests process status zero. `halt/1` passes its bounded integer argument to the host
process status after validating it. Embedding hosts observe the halt request through the engine
rather than having the runtime terminate the CLR process directly.

Text streams use the host .NET text readers and writers; binary streams use raw bytes. File-system
names, invalid paths, permissions, seekability, and durable I/O failures follow the host operating
system, translated to the documented Prolog `source_sink`, permission, and `system_error` terms.
The permanent `user_input`, `user_output`, and `user_error` streams are text streams and are not
repositionable.

A source/sink is an atom interpreted as a host file-system path. A program-created stream is named
by the opaque ground term `'$stream'(N)`; identifiers are monotonically allocated and never reused.
Closing a current stream restores the corresponding permanent standard stream. The standard
streams have the aliases `user_input`, `user_output`, and `user_error`.

Text files are read through the .NET Unicode file reader and written as UTF-8 without a byte-order
mark. DotProlog does not append a newline when a text sink is closed, does not treat text files as
record-based streams, and outputs control characters unchanged. Binary output writes exactly the
requested bytes and appends no padding bytes.

Disk streams are repositionable by default. An explicit `reposition(false)` prevents
`set_stream_position/2` even when the host file is seekable. Text positions are opaque logical
character positions and binary positions are byte offsets. The permanent standard streams and
in-memory capture streams are not repositionable.

The default EOF action is `eof_code`. The default stream type is `text`, and the default close
option is `force(false)`. A text stream reports its original file-system atom as `file_name/1` and
reports opaque positions only when repositioning is enabled.

## Modules

DotProlog implements the Part 2 interface/body representation. An interface is bracketed by
`module/1` and `end_module/1`; a body is bracketed by `body/1` and `end_body/1`. Interfaces may
export, re-export, declare metapredicates, and establish module-local reader state. Bodies may
import selectively or wholesale, and multiple bodies accumulate state in preparation order.
Unbracketed module text belongs to `user`; another body may be embedded in that user text.

Selected imports and re-exports must name exported procedures. Malformed selections, duplicate
interfaces, imported local definitions, and two modules supplying the same
visible predicate are rejected during source preparation.

`colon_sets_calling_context` is fixed to `true`. Explicit qualification therefore sets the context
of the Part 2 metapredicates, controls, reflection, database operations, operator and flag access,
and term I/O. DotProlog provides no optional mechanism for hiding a module procedure from explicit
qualification, so procedures in ISO modules report `public`; export remains a separate property.

An exported predicate is also published under its plain name when that name is still free. The
first loaded export therefore owns a plain-name alias; later modules remain reachable through
qualification or an unambiguous import. Loading source and its relationship to files are DotProlog
extensions rather than claims about the Part 2 filesystem model.

Modern mode additionally accepts the Quintus-family `module/2`, `use_module/1,2`, and
`meta_predicate/1` declarations. They are compatibility extensions and are rejected in StrictIso.

## Definite clause grammars

Grammar alternative `|` is predefined as `xfy` at priority 1105. DotProlog supports terminal
semicontexts, `Name//Arity` in `dynamic/1`, `multifile/1`, and `discontiguous/1`, and the standard
grammar control constructs. In Modern mode, the additional soft-cut grammar forms follow the
corresponding DotProlog control semantics. In strict mode, soft cut is translated as an ordinary
nonterminal rather than an additional grammar control construct.

If an ordinary `Name/(Arity+2)` clause and `Name//Arity` grammar rule coexist, DotProlog combines
their expanded clauses into one procedure in source order. Grammar-rule heads that are themselves
grammar control constructs or that expand to a predefined procedure are rejected.

`phrase/2` reports `type_error(list, Culprit)` when its sequence cannot be a terminal sequence.
`phrase/3` selects the specification's implementation-defined unchecked option for its
second and third arguments. A partial terminal list inside the grammar body raises
`instantiation_error`; an improper terminal list raises `type_error(list, Culprit)`.
