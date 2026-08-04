# ISO/IEC 13211-1 conformance

DotProlog's `StrictIso` processor mode implements ISO/IEC 13211-1:1995, Technical
Corrigendum 1:2007, Technical Corrigendum 2:2012, and Technical Corrigendum 3:2017. This
declaration applies to Part 1, General core. Parts 2 and 3 are separate publications and are not
included in this Part 1 declaration.

This page is an original traceability summary. It deliberately paraphrases the requirements and
does not reproduce the licensed standard. The authoritative text remains the ISO publication.

## Evidence model

Every row below is covered by one or more of these executable evidence classes:

| Code | Evidence |
|---|---|
| `R` | Focused reader, compiler, runtime, or integration tests |
| `D` | The 608-case repository corpus in `tests/conformance/iso_conformance.pl` |
| `I` | All 768 applicable declarations from the pinned Logtalk 3.101.0 ISO corpus |
| `C` | The generated-C# runner |
| `CB` | Generated C# calling consulted bytecode |
| `BC` | Consulted bytecode calling generated C# |
| `A` | Published NativeAOT execution |
| `P` | A documented processor-defined choice |

The independent inventory is run unchanged through `I`, `C`, `CB`, `BC`, and `A`. The inventory
gate rejects count drift, unsupported declarations, and changed upstream sources. The Part 1
predefined-predicate and evaluable-functor inventory is also pinned by
`IsoPartOneInventoryTests`.

## Clauses 5 and 6

| Requirement | Covered contract | Evidence |
|---|---|---|
| 5.1 processor preparation | Conforming source is prepared through the same reader and semantic loader in every execution path | R/I/C/CB/BC/A |
| 5.1 processor execution | Prepared goals preserve success, failure, bindings, solution order, errors, and side effects | R/D/I/C/CB/BC/A |
| 5.1 rejection | Invalid source and read terms produce source diagnostics or catchable syntax errors | R/D/I/A |
| 5.1 strict mode | `StrictIso` rejects known predefined implementation-specific features in source, meta-calls, assertions, host bindings, generated code, and NativeAOT | R/C/CB/BC/A |
| 5.2 conforming text | Standard syntax and documented processor-defined features are accepted | R/D/I/C/A |
| 5.3 conforming goals | Standard controls and predicates execute under the Part 1 contract | R/D/I/C/CB/BC/A |
| 5.4 documentation | Numeric, text, stream, error, flag, and ordering choices are recorded in the processor-characteristics page | P |
| 5.5.1 syntax extensions | Extended syntax cannot change the meaning of a Part 1 term in `StrictIso` | R/I/C/A |
| 5.5.2 operator extensions | The ISO operator table and `op/3` restrictions take precedence over extended operators | R/D/I/A |
| 5.5.3 character conversion | Program-owned conversion is gated by the ISO flag and applied before unquoted lexical classification | R/D/I/C/A |
| 5.5.4 type extensions | The Part 1 term order contains only variables, floats, integers, atoms, and compounds | R/D/I/A |
| 5.5.5 directives | Standard directives retain their preparation-time meaning and validation | R/I/C/A |
| 5.5.6 side effects | Stream, flag, database, and process effects occur at the specified execution point | R/D/I/C/A |
| 5.5.7 control extensions | Part 1 controls retain their cut and backtracking semantics; soft cut is rejected as a Part 1 control in strict source | R/D/I/C/A |
| 5.5.8 flag extensions | Only the nine Part 1 flags and the Part 2 `colon_sets_calling_context` flag enumerate; extension settings are not exposed as ISO flags | R/D/I/A |
| 5.5.9 predicate extensions | User predicates may use arbitrary names, while predefined extensions are excluded by strict mode | R/C/A |
| 5.5.10 evaluable extensions | Only Part 1 evaluable functors are admitted by strict arithmetic evaluation | R/D/I/C/A |
| 5.5.11 reserved atoms | Error, option, mode, and flag atoms keep their standardized meanings | R/D/I/A |
| Cor.3 5.5.12 options | Option names, rightmost precedence, list validation, and unknown-option errors share one contract | R/D/C/A |
| 6.1 notation | Concrete tokens lower to the specified abstract term forms | R/D/I |
| 6.2 Prolog text | Directives, clauses, and ordinary data are distinguished before semantic preparation | R/I/C/A |
| 6.3.1 atomic terms | Atoms, integers, floats, and negative numeric terms have the required abstract forms | R/D/I/A |
| 6.3.2 variables | Anonymous and named variables have the required identity and occurrence behavior | R/D/I/C/A |
| 6.3.3 functional notation | Compound functors, arguments, empty argument rejection, and arity limits are enforced | R/D/I/A |
| 6.3.4 operator notation | Prefix, infix, postfix, associativity, priority, quoted names, and operator-as-functor cases parse correctly | R/D/I/C/A |
| Cor.2 bar restrictions | `|` may only be an infix operator at priority 1001 or greater; `[]` and `{}` cannot be operators | R/D/I/A |
| 6.3.5 list notation | Proper, partial, and improper lists preserve their `./2` abstract representation | R/D/I/C/A |
| 6.3.6 curly notation | Empty and non-empty curly forms lower to the required atoms and compounds | R/D/I |
| 6.3.7 double-quoted notation | `double_quotes` selects the character-code list, one-char atom list, or atom form | R/D/I/C/A |
| 6.4.1 layout | Spaces, newlines, line comments, bracketed comments, and token boundaries are recognized without changing terms | R/D/I |
| 6.4.2 names | Alphanumeric, graphic, quoted, semicolon, and cut tokens follow the required lexical categories | R/D/I/A |
| 6.4.2.1 quoted characters | Doubled delimiters, meta escapes, control escapes, and delimited numeric escapes round-trip | R/D/I/A |
| 6.4.3 variable tokens | Uppercase and underscore starts, anonymous variables, and subsequent alphanumeric characters are distinguished | R/D/I |
| 6.4.4 integer tokens | Decimal, character-code, binary, octal, and hexadecimal forms enforce representation limits | R/D/I/A |
| 6.4.5 float tokens | Decimal point and exponent grammar, finite representation, overflow, and underflow choices are enforced | R/D/I/A/P |
| 6.4.6 double-quoted tokens | Delimiters, escapes, continuations, and flag-dependent abstract values are covered | R/D/I/A |
| 6.4.7 backquoted names | The processor choice is atom-valued; delimiters, escapes, and continuations are covered | R/D/I/A/P |
| 6.4.8 other tokens | punctuation, end token, and Corrigendum 2 bar-token behavior are covered | R/D/I |
| 6.5 character set | Required characters and the documented Unicode extension classes are recognized explicitly | R/D/I/P |
| 6.6 collating sequence | Character codes and atom ordering use the documented ordinal UTF-16 policy | R/D/I/P |

## Clause 7 language concepts and semantics

| Requirement | Covered contract | Evidence |
|---|---|---|
| 7.1.1 variables | Variable sets, existential/free-variable discovery, witness order, and sharing are preserved | R/D/I/C/A |
| Cor.2 witness variable list | `term_variables/2` reports distinct variables in first-occurrence traversal order | R/D/I/A |
| 7.1.2 integers and bytes | Bounded integers and byte values enforce their documented ranges | R/D/I/A/P |
| 7.1.3 floats | Finite binary64 values, exceptional arithmetic results, and conversions follow the declared model | R/D/I/A/P |
| 7.1.4 atoms and Booleans | Atom identity, one-character atoms, and `true`/`false` option values are enforced | R/D/I/A |
| 7.1.5 compounds | Functor identity, arity, and recursive term structure are preserved | R/D/I/C/A |
| 7.1.6 related terms | Variants, renamed copies, iterated goals, list prefixes, sorted lists, and predicate indicators are covered | R/D/I/A |
| 7.2 standard order | Variables precede floats, floats precede integers, integers precede atoms, and atoms precede compounds | R/D/I/C/A/P |
| 7.2 variable stability | Variable ordering remains stable during a sorting operation | R/D/I/A |
| 7.3 unification | Iterative unification, rollback, aliasing, and ordinary failure follow the Herbrand behavior | R/D/I/C/CB/BC/A |
| 7.3 occurs check | `unify_with_occurs_check/2` rejects only cycle-creating bindings and terminates on existing rational trees | R/D/I/A |
| 7.4 undefined source features | Ill-formed and unsupported declarations are rejected without changing earlier program state | R/I/C/A |
| 7.4.2.1 `dynamic/1` | Indicators are validated before clauses and declare logical-update-view procedures | R/I/C/A |
| 7.4.2.2 `multifile/1` | Static and dynamic clauses aggregate across source units with declaration validation | R/I/C/A |
| 7.4.2.3 `discontiguous/1` | Scattered clauses are accepted only under a valid declaration | R/I/C/A |
| 7.4.2.4 `op/3` directive | Arguments and permission rules match `op/3`; the new table applies at the source position | R/I/C/A |
| 7.4.2.5 `char_conversion/2` directive | Arguments match the builtin and conversion applies at the source position | R/I/C/A |
| 7.4.2.6 `initialization/1` | Goals are validated at their source position and execute after preparation in source order | R/I/C/A/P |
| 7.4.2.7 `include/1` | Included text is spliced at the directive with shared lexical state, relative paths, and cycle detection | R/I/C/A/P |
| 7.4.2.8 `ensure_loaded/1` | Canonical source identity gives load-once semantics at the directive position | R/I/C/A/P |
| 7.4.2.9 `set_prolog_flag/2` | Validation matches the builtin and the load-unit value applies to following source | R/I/C/A/P |
| 7.4.3 clauses | Source clauses use the same callable and arity rules as database insertion without static-update rejection | R/I/C/A |
| 7.5 database | Static and dynamic procedures, private procedure protection, generations, and declared-empty procedures are covered | R/D/I/C/CB/BC/A |
| 7.5.4 logical update view | A call sees exactly the clauses visible at its starting generation | R/D/I/C/CB/BC/A |
| 7.6 term/clause conversion | Heads, bodies, controls, callable errors, and predicate indicators convert consistently | R/D/I/A |
| 7.7 goal execution | Initial calls, success, failure, re-execution, clause order, and side effects use explicit machine state | R/D/I/C/CB/BC/A |
| 7.7.7 clause selection | Source order is observable; first-argument indexing does not change solutions | R/I/C/A |
| 7.7.8 backtracking | Choice points restore bindings, environments, streams of solutions, and cut barriers | R/D/I/C/A |
| 7.7.10 user procedures | Facts and rules execute with the specified head unification and body continuation | R/D/I/C/CB/BC/A |
| 7.7.11 undefined procedures | `unknown` selects error, warning-and-failure, or failure consistently across call paths | R/D/I/C/A/P |
| 7.7.12 built-ins | Native and bundled built-ins use the same machine failure, binding, retry, and exception model | R/D/I/C/CB/BC/A |
| 7.8.1 `true/0` | Succeeds once without bindings | R/D/I/C/A |
| 7.8.2 `fail/0` | Fails immediately | R/D/I/C/A |
| 7.8.3 `call/1` | Callable validation, opacity of cut, bindings, and all solutions are preserved | R/D/I/C/A |
| 7.8.4 cut | Commits exactly the enclosing predicate or control barrier | R/D/I/C/A |
| 7.8.5 conjunction | Left-to-right execution and nested backtracking are preserved | R/D/I/C/A |
| 7.8.6 disjunction | Left branch solutions precede right branch solutions | R/D/I/C/A |
| 7.8.7 if-then | Only the first condition solution commits; failure and cut scopes are preserved | R/D/I/C/A |
| 7.8.8 if-then-else | Else runs only when the condition has no solution; condition choice points are committed | R/D/I/C/A |
| 7.8.9 `catch/3` | Goal, catcher, recovery, active-catcher lifetime, unification, and error modes are covered | R/D/I/C/A |
| 7.8.10 `throw/1` | Nonvariable balls unwind to the nearest matching catcher; a variable raises instantiation error | R/D/I/C/A |
| 7.9 expression evaluation | Recursive evaluation, signatures, exceptional values, and non-evaluable terms are covered | R/D/I/C/A |
| 7.10.1 sources and sinks | Host paths are the documented source/sink domain and failures remain catchable | R/D/I/A/P |
| 7.10.2.1 modes | `read`, `write`, and `append` have the required creation, truncation, and positioning behavior | R/D/I/A |
| 7.10.2.2 aliases | An alias names at most one live stream and is released by close | R/D/I/A |
| 7.10.2.3 standard streams | Permanent input, output, and error streams are open text streams | R/D/I/A/P |
| 7.10.2.4 current streams | Current input/output default and reset behavior is covered | R/D/I/A |
| 7.10.2.5 target streams | Implicit and explicit stream arguments resolve through the same validation path | R/D/I/A |
| 7.10.2.6 text streams | Encoding, newline, control-character, and repositioning choices are documented | R/D/I/A/P |
| 7.10.2.7 binary streams | Raw byte identity and byte-domain enforcement are covered | R/D/I/A/P |
| 7.10.2.8 positions | Opaque positions restore parser lookahead, newline normalization, and raw byte offsets | R/D/I/A/P |
| 7.10.2.9 end positions | `not`, `at`, and `past` EOF states and terminating values are covered | R/D/I/A |
| 7.10.2.10 flushing | Explicit and implicit text output flushes translate host failures | R/D/I/A |
| 7.10.2.11 open options | `type`, `reposition`, `alias`, and `eof_action` validate before opening | R/D/I/A |
| 7.10.2.12 close options | `force(false)` preserves errors and `force(true)` performs best-effort cleanup | R/D/I/A |
| 7.10.2.13 stream properties | Required properties enumerate from a stable live stream model | R/D/I/A |
| 7.10.3 read options | `variables`, `variable_names`, and `singletons` preserve identity and first-occurrence order | R/D/I/C/A |
| 7.10.4 write options | `quoted`, `ignore_ops`, `numbervars`, and Corrigendum 3 `variable_names` obey rightmost precedence | R/D/I/C/A |
| 7.10.5 term writing | Variables, atoms, numbers, compounds, operators, lists, quoting, and canonical form round-trip | R/D/I/C/A |
| 7.11 integer flags | `bounded`, limits, rounding, and maximum arity expose the documented immutable values | R/D/I/A/P |
| 7.11 mutable flags | Character conversion, debug, double quotes, and unknown validate and take effect as documented | R/D/I/A/P |
| 7.12.1 error effect | Engine-raised errors are catchable `error(Formal, Context)` terms on every execution path | R/D/I/C/CB/BC/A/P |
| 7.12.2 error classes | Instantiation, type, domain, existence, permission, representation, evaluation, resource, syntax, and system errors are distinguished | R/D/I/C/A |
| 7.12 error priority | Predicate-specific validation order is tested when multiple arguments are invalid | R/D/I/A/P |

## Clause 8 predefined predicates

Each row covers the complete subclause contract: logical result, argument modes, bindings,
re-execution and solution order where applicable, errors and their priority, and bootstrapped
variants.

| Subclause | Predicates | Evidence |
|---|---|---|
| 8.2 | `=/2`, `unify_with_occurs_check/2`, `\=/2`, `subsumes_term/2` | R/D/I/C/A |
| 8.3 | `var/1`, `atom/1`, `integer/1`, `float/1`, `atomic/1`, `compound/1`, `nonvar/1`, `number/1`, `callable/1`, `ground/1`, `acyclic_term/1` | R/D/I/C/A |
| 8.4 | `==/2`, `\==/2`, `@</2`, `@=</2`, `@>/2`, `@>=/2`, `compare/3`, `sort/2`, `keysort/2` | R/D/I/C/A |
| 8.5 | `functor/3`, `arg/3`, `=../2`, `copy_term/2`, `term_variables/2` | R/D/I/C/A |
| 8.6 | `is/2` | R/D/I/C/A |
| 8.7 | `=:=/2`, `=\=/2`, `</2`, `=</2`, `>/2`, `>=/2` | R/D/I/C/A |
| 8.8 | `clause/2`, `current_predicate/1` | R/D/I/C/CB/BC/A |
| 8.9 | `asserta/1`, `assertz/1`, `retract/1`, `abolish/1`, `retractall/1` | R/D/I/C/CB/BC/A |
| 8.10 | `findall/3`, `bagof/3`, `setof/3` | R/D/I/C/A |
| 8.11.1-4 | `current_input/1`, `current_output/1`, `set_input/1`, `set_output/1` | R/D/I/A |
| 8.11.5 | `open/3`, `open/4` | R/D/I/A |
| 8.11.6-7 | `close/1,2`, `flush_output/0,1` | R/D/I/A |
| 8.11.8-9 | `stream_property/2`, `at_end_of_stream/0,1`, `set_stream_position/2` | R/D/I/A |
| 8.12 | `get_char/1,2`, `get_code/1,2`, `peek_char/1,2`, `peek_code/1,2`, `put_char/1,2`, `put_code/1,2`, `nl/0,1` | R/D/I/A |
| 8.13 | `get_byte/1,2`, `peek_byte/1,2`, `put_byte/1,2` | R/D/I/A |
| 8.14.1 | `read_term/2,3`, `read/1,2` | R/D/I/C/A |
| 8.14.2 | `write_term/2,3`, `write/1,2`, `writeq/1,2`, `write_canonical/1,2` | R/D/I/C/A |
| 8.14.3-4 | `op/3`, `current_op/3` | R/D/I/C/A |
| 8.14.5-6 | `char_conversion/2`, `current_char_conversion/2` | R/D/I/C/A |
| 8.15 | `\+/1`, `once/1`, `repeat/0`, `call/2..8`, `false/0` | R/D/I/C/A |
| 8.16 | `atom_length/2`, `atom_concat/3`, `sub_atom/5`, `atom_chars/2`, `atom_codes/2`, `char_code/2`, `number_chars/2`, `number_codes/2` | R/D/I/C/A |
| 8.17 | `set_prolog_flag/2`, `current_prolog_flag/2`, `halt/0`, `halt/1` | R/D/I/A/P |

## Clause 9 evaluable functors

Each row covers operand evaluation, result kind, exceptional values, signatures, errors, and the
processor choices recorded in the characteristics page.

| Subclause | Evaluable functors | Evidence |
|---|---|---|
| 9.1 integer arithmetic | unary `+`, unary `-`, `+`, `-`, `*`, `//`, `rem`, `mod`, Corrigendum 2 `div` | R/D/I/C/A/P |
| 9.1 floating and mixed arithmetic | `/`, `float/1`, `float_integer_part/1`, `float_fractional_part/1`, `floor/1`, `truncate/1`, `round/1`, `ceiling/1` | R/D/I/C/A/P |
| 9.3.1 | `**/2` | R/D/I/C/A |
| 9.3.2-7 | `sin/1`, `cos/1`, `atan/1`, `exp/1`, `log/1`, `sqrt/1` | R/D/I/C/A |
| Cor.2 9.3.8-10 | `max/2`, `min/2`, `^/2` | R/D/I/C/A |
| Cor.2 9.3.11-15 | `asin/1`, `acos/1`, `atan2/2`, `tan/1`, `pi/0` | R/D/I/C/A |
| 9.4 | `>>/2`, `<</2`, `/\\/2`, `\\//2`, `\\/1`, Corrigendum 2 `xor/2` | R/D/I/C/A/P |

## Corrigenda reconciliation

| Publication | Normative changes covered | Evidence |
|---|---|---|
| Corrigendum 1 | Double-quoted term grammar; control and arithmetic error corrections; clause, database, stream, term-input, operator-enumeration, atomic-conversion, and arithmetic-example corrections | R/D/I/A |
| Corrigendum 2 | Bar/operator restrictions; witness-variable and list-prefix definitions; `catch/3` and evaluation-error corrections; new term, type, comparison, sorting, variable, database, meta-call, atomic-conversion, and arithmetic features | R/D/I/C/A |
| Corrigendum 3 | General option model and validation priority; `variable_names/1` output naming; write-option behavior; expanded error types and permissions; corrected power examples | R/D/I/C/A |

## Continuing gate

Part 1 conformance is a maintained property, not a one-time test result. A release must keep all of
the following green:

1. The 608 repository cases.
2. The exact 768-declaration independent inventory with zero unsupported or failed cases.
3. All 2,304 generated and bidirectional cross-path checks.
4. The direct and generated NativeAOT runners for every release RID.
5. The focused parser, compiler, runtime, stream, database, arithmetic, flag, and strict-mode tests.

Any new implementation-defined behavior must be added to the processor-characteristics page and
covered by an executable test before release.
