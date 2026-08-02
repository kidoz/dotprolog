# ISO processor characteristics

This page records the processor-defined choices that accompany DotProlog's ISO/IEC 13211-1
conformance declaration. Requirement and execution-path evidence is recorded separately in the
[Part 1 conformance ledger](iso-part1-conformance.md).

## Strict ISO mode

`PrologLanguageMode.StrictIso` is an immutable, program-owned processor mode. It admits the
explicitly inventoried predefined surface from ISO/IEC 13211 Parts 1, 2, and 3 and rejects known
predefined DotProlog extensions. Source preparation reports `DPL1018`; a runtime-constructed
meta-goal or host binding raises the catchable term
`permission_error(access, implementation_specific_feature, Name/Arity)`.

The bundled implementation library is trusted below this boundary so it may use private helpers
to implement standardized predicates. User-defined predicate names are unrestricted. Extended mode
is the default for backward compatibility.

## Representation limits

| Characteristic | DotProlog choice |
|---|---|
| Integer model | Bounded signed tagged integers |
| `min_integer` | −576460752303423488 |
| `max_integer` | 576460752303423487 |
| `bounded` | `true` |
| Maximum predicate and compound arity | 255 |
| Float model | Finite IEEE 754 binary64 |
| Integer division rounding | Toward zero |

Integer results outside the tagged range raise `evaluation_error(int_overflow)`. Integer source
literals outside the range raise `representation_error(min_integer)` or
`representation_error(max_integer)`. Float arithmetic rejects NaN and infinity with the applicable
ISO evaluation error. A decimal float literal that overflows binary64 is a
`syntax_error(float_overflow)`; underflow rounds to signed zero.

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

Right shift is sign-extending. A left shift whose result cannot be represented as a tagged integer
raises `evaluation_error(int_overflow)`.

## Text and syntax

Atoms and source text use .NET Unicode strings. The reader accepts Unicode source characters and
the ISO numeric character escapes supported by the language guide. Character predicates require a
one-character atom as represented by one .NET UTF-16 code unit.

The required portable characters have their Unicode/ASCII ordinal values. Extended characters are
classified before tokenization: Unicode uppercase letters and underscore begin variables, other
Unicode letters begin unquoted atoms, ASCII digits begin numbers, and other supported punctuation
is classified by the explicit graphic, solo, layout, and meta-character tables. Atom and character
collation is ordinal by UTF-16 code unit. The byte sequence associated with a character is its UTF-8
encoding for a text file; binary streams do not perform character conversion.

The initial `double_quotes` flag is `codes` in the `Extended` and `StrictIso` language modes, as
ISO/IEC 13211-1 requires. The opt-in `Modern` mode starts it at `chars` instead; that mode is an
extension and is outside the conformance claim. The value is scoped to the load unit: a
`set_prolog_flag(double_quotes, _)` directive governs the rest of the file that issued it, and the
entering value is restored when that file finishes loading.

The initial `char_conversion` flag is `off`.
Character conversion applies to unquoted lexical input while quoted text, escapes, character-code
literal payloads, and primitive character input remain unchanged.

`write_term/2,3` implements the Corrigendum 3 `variable_names/1` option. Its value is a list of
`Atom=Term` entries; the leftmost entry whose term is the variable being written supplies the
output name. Inspecting the list neither unifies nor otherwise binds its terms.

The `StrictIso` initial operator table contains the ISO Part 1 table as corrected by Corrigendum 2,
together with the documented Part 2 and Part 3 operators. Extended and Modern modes additionally
predefine the convenience directive operators and `:=`, `.` as an infix operator, and `$`.
`current_op/3` enumerates a captured table version in ordinal operator-name order and then
operator-specifier order. Mutating the table while an enumeration is active does not change that
enumeration.

Variables are ordered by their stable heap identity. The relative order of distinct variables is
therefore implementation-dependent, but it remains constant for the lifetime of a sorting or
solution-collection operation. Atoms use ordinal name order. Every float precedes every integer,
including numerically equal cross-kind values.

## Source preparation and goal delivery

Hosts prepare text with `PrologEngine.ConsultText` or `ConsultFile`; applications use a `.dplproj`,
and the command-line surface uses `dotnet prolog run`. A host delivers a goal through
`PrologEngine.Query`, `RunGoal`, or a bound `PrologHost` predicate. Success and failure are returned
as host results, while bindings are exposed as solution values.

`include/1` inserts text at the directive position, resolves relative names from the containing
file, and shares the containing reader's operator, flag, and character-conversion state.
`ensure_loaded/1` also acts at its directive position, but canonical file identity ensures that a
source unit is prepared only once. Initialization goals execute in source order after successful
preparation. A failing ordinary directive stops preparation; `initialization/1` remains deferred.

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

DotProlog's current module extension starts a source unit with one `module/2` declaration and uses
`use_module/1,2` and `meta_predicate/1`. This is the widely used Quintus-family module surface, not
the module-interface and module-body representation standardized by ISO/IEC 13211-2. It must not be
used as evidence of Part 2 conformance.

Selected imports must name predicates the source module exports; malformed selections and two
modules supplying the same visible predicate are rejected during source preparation. Local
definitions take precedence over imports.

An exported predicate is also published under its plain name when that name is still free. The
first loaded export therefore owns a plain-name alias; later modules remain reachable through
qualification or an unambiguous import. Loading source and its relationship to files are DotProlog
extensions rather than claims about the Part 2 filesystem model.

## Definite clause grammars

Grammar alternative `|` is predefined as `xfy` at priority 1105. DotProlog supports terminal
semicontexts, `Name//Arity` in `dynamic/1`, `multifile/1`, and `discontiguous/1`, and the standard
grammar control constructs. In extended mode, the additional soft-cut grammar forms follow the
corresponding DotProlog control semantics. In strict mode, soft cut is translated as an ordinary
nonterminal rather than an additional grammar control construct.

If an ordinary `Name/(Arity+2)` clause and `Name//Arity` grammar rule coexist, DotProlog combines
their expanded clauses into one procedure in source order. Grammar-rule heads that are themselves
grammar control constructs or that expand to a predefined procedure are rejected.

`phrase/2` reports `type_error(terminal_sequence, Culprit)` when its sequence cannot be a terminal
sequence. `phrase/3` selects the specification's implementation-defined unchecked option for its
second and third arguments. A partial terminal list inside the grammar body raises
`instantiation_error`; an improper terminal list raises `type_error(list, Culprit)`.
