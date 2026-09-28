# ISO conformance tracker

This page lists what the ISO/IEC 13211 documents require of a conforming processor, requirement by
requirement, and records whether DotProlog's strictly conforming mode (`StrictIso`,
`--mode strict-iso`) meets each one. Every deviation found has a gap ID, so it can be fixed and
closed individually.

The results come from probing DotProlog at commit `797eac5` with examples and error cases derived
from the standards. The source interpretations were rechecked against the local publications,
including the final Cor.3 PDF, on 2026-09-28, and every gap was then reproduced again and re-read
against the text in an adversarial review. Where this page disagrees with the
[Part 1 ledger](iso-part1-conformance.md), the
[Parts 2 and 3 ledger](iso-parts2-3-conformance.md), or the
[runtime configuration and implementation characteristics](iso-processor-characteristics.md), this page records the observed
behavior. Terminology and the `/2` classification have been corrected; other differences remain
tracked below.

Requirements are paraphrased. The ISO publications remain authoritative, and clause numbers refer
to them.

## Scope and method

| Document | Reviewed |
|---|---|
| ISO/IEC 13211-1:1995, General core | Clauses 5–9 (Annex A, which is informative, was not used) |
| Technical Corrigendum 1:2007 | Every item |
| Technical Corrigendum 2:2012 | Every item |
| Technical Corrigendum 3:2017 | Every item |
| ISO/IEC 13211-2:2000, Modules | Every normative subclause |
| ISO/IEC TS 13211-3:2025, Definite clause grammar rules | Every normative subclause |

The table records the scope of the source review, not exhaustive executable coverage. Behavioral
evidence comes from goals run in `StrictIso`; documentation and editorial requirements were
reviewed directly. The original probe reports are local review records, not a complete checked-in
suite. Their example and error counts below are historical results unless a reproducible case is
linked. They do not establish that every example or error condition has been tested correctly.

The reader review ran 365 cases through `read_term/3`; selected cases were also checked as
consulted source text. It recorded 337 automatic matches, 17 deviations, and 11 manually reviewed
cases (7 conforming, 4 deviating): 344 conforming cases and 21 deviations in total. Only the formal
part of `error(Formal, Context)` is compared, because the context is implementation defined.

As an independent check of the Done rows, all 366 entries of Ulrich Neumerkel's public
[ISO conformity testing table](https://www.complang.tuwien.ac.at/ulrich/iso-prolog/conformity_testing)
were run in `StrictIso`. 365 give a result the table accepts for ISO. The exception is entry #364,
`writeq(-0.0)` (GAP-67). The table mostly covers reading and writing, so it supports the clause 6
and 7.10.5 rows but says little about modules, streams, or the database.

The [recheck evidence](#recheck-evidence) supplies stable case IDs, required outcomes, observed
results, and a repository probe for the corrected Cor.3 and arithmetic findings. It does not
reconstruct the entire original review corpus.

The Modern mode deliberately keeps readings that the standard does not allow (see
[language modes and text](../explanation/language-modes.md)), so it is outside this tracker. It is
mentioned only where it differs in a way that matters.

The terms *implementation defined*, *implementation dependent*, *implementation specific*, and
*undefined* have their Part 1 clause 3 meanings. *Extension* is used only in the sense of Part 1
5.5.

## Status values

| Status | Meaning |
|---|---|
| Done | No deviation found in the reviewed cases in `StrictIso`; subject to the coverage limits above. For an implementation defined feature, the choice is also documented |
| Partial | Present, but at least one required example, error case, or documentation item deviates. The notes name the gap |
| Missing | Not provided in `StrictIso` |
| Unverified | Present in the code but not demonstrated by a probe or a specific test |
| Optional | Permitted but not required. The notes say whether DotProlog provides it |

Gap severity:

- **High**: a strictly conforming program loads wrongly, gets wrong solutions, or stops.
- **Medium**: a wrong result or error term in a specified but less common case, or an undocumented
  implementation defined feature.
- **Low**: an edge case, a wrong error term where the text allows more than one reading, or a point
  where the standard's text is ambiguous.

Severity also weighs how often a construct occurs in real programs. A load failure on a rarely
written term, such as GAP-03 or GAP-04, is rated below one that ordinary programs meet.

## Summary

| Area | Requirements | Done | Partial | Missing | Unverified | Optional |
|---|---:|---:|---:|---:|---:|---:|
| Part 1 clause 5, compliance | 21 | 5 | 15 | 0 | 1 | 0 |
| Part 1 clause 6, syntax | 46 | 37 | 9 | 0 | 0 | 0 |
| Part 1 clause 7, concepts and semantics | 109 | 79 | 29 | 0 | 1 | 0 |
| Part 1 clause 8, built-in predicates | 87 | 59 | 27 | 1 | 0 | 0 |
| Part 1 clause 9, evaluable functors | 54 | 32 | 22 | 0 | 0 | 0 |
| Corrigenda 1, 2, and 3 | 70 | 62 | 8 | 0 | 0 | 0 |
| Implementation defined documentation | 32 | 19 | 13 | 0 | 0 | 0 |
| Part 2, modules | 63 | 23 | 36 | 0 | 0 | 4 |
| Part 3, grammar rules | 29 | 19 | 8 | 0 | 0 | 2 |
| **Total** | **511** | **335** | **167** | **1** | **2** | **6** |

There are 70 open gaps: 14 High, 24 Medium, and 32 Low. GAP-35 was closed after Cor.3 was
reviewed. Most of the Partial rows in Part 1 share a few causes: the strict-mode handling of
extension names (GAP-09 to GAP-11), existential `^` in `bagof/3` and `setof/3` and their reliance on
public library names (GAP-15, GAP-66), and arithmetic exception classes and integers outside the
float range (GAP-32 to GAP-39, GAP-67, GAP-68). Part 2 module qualification is the largest area of
nonconformity, and several of its gaps share one cause in run-time qualification.

## Open gaps

Set the status to `Fixed in <version>` when a gap closes, and name the regression test in the
matching checklist row.

### Syntax and the reader

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-01 | High | 5.5.2, 6.3.4.4 | `StrictIso` predefines five operators that appear in none of the three parts: `op(500, yfx, xor)`, `op(1050, xfy, *->)`, and `op(1150, fx, …)` for `module`, `use_module`, and `meta_predicate`. This changes the meaning of conforming text: `X = xor.` is a syntax error, and `module - 1` reads as `module(-1)`. Cor.2 adds only `div` and prefix `+` to table 7 | Open |
| GAP-02 | Medium | Cor.2 6.3.4.3 | After <code>op(1100, xfy, '&#124;')</code>, <code>(a&#124;b)</code> reads as `;(a, b)` instead of <code>'&#124;'(a, b)</code>. Priorities 1001 and 1105 are read correctly | Open |
| GAP-03 | Medium | 6.3.3, 6.3.4.2 | A prefix operator followed, in functional notation, by an atom that is an infix or postfix operator but not also a prefix operator is rejected: `X = - '->'(a, b).` is a syntax error, where ISO reads `X = -('->'(a, b))`. `f(- ;(a, b))`, `- mod(a, b)`, `\+ =(a, b)`, and `- ','(a, b)` are rejected too, and such a term makes a source file fail to load. Prefix-operator atoms (`- '-'(a)`, `- :-(a)`) and the same terms after an infix operator (`a - mod(a, b)`) are read correctly | Open |
| GAP-04 | Low | 6.3.3 | `{ }(a)` is rejected. `{ }` is the atom `{}`, so ISO reads `'{}'(a)`. `[ ](a)` and `{}(a)` are read correctly | Open |
| GAP-05 | Low | Cor.1 6.3.7 | With `double_quotes` set to `atom`, `"-"1` reads as `-(1)`. ISO requires a syntax error | Open |
| GAP-06 | Low | 6.4.8 | An end char at the very end of the input (`a.` with nothing after it) is accepted as an end token. ISO requires layout or `%` after it | Open |
| GAP-07 | Low | 6.5, 5.4 | Non-ASCII whitespace (U+00A0, U+2003, U+0085) counts as layout and non-ASCII decimal digits count as alphanumeric. Both contradict the documented `StrictIso` character classes | Open |
| GAP-08 | Low | 8.16.7, 8.16.8 | For `1.0e400`, `number_chars/2` raises `representation_error(max_float)` while the reader raises `syntax_error(float_overflow)`. `max_float` is not a representation flag in 7.12.2 f. Every other probed text agrees between the two parsers in `StrictIso`, including the texts from issues #2, #7, #10, and #12 | Open |

### Strict mode and extensions

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-09 | High | 5.1 b, 5.1 e, 5.2 | A user definition that shares its indicator with a natively implemented DotProlog extension is never used. A compiled call runs the extension instead, and a meta-call raises `permission_error(access, implementation_specific_feature, PI)`. A definition in another text unit is not seen at all, and the calling file is rejected with DPL1018. Affected names include `between/3`, `succ/2`, `msort/2`, `plus/3`, `print/1`, `writeln/1`, `tab/1`, `assert/1`, `atomic_list_concat/3`, and `is_list/1`. A text that defines and calls `'*->'/2` is rejected with DPL1018, and meta-calling it stops the process. Part 1 does not reserve these names. User definitions do replace library predicates written in Prolog, such as `length/2`, `append/3`, `member/2`, `forall/2`, and `format/2`, but see GAP-66. The characteristics page says user-defined names are unrestricted | Open |
| GAP-10 | High | 5.1 e, 7.8.3 | Meta-calling a control construct that contains an extension stops the process with exit status 70 instead of raising a catchable error, even when the extension is never reached: `catch(call((fail, msort([b,a], _))), E, true)`. `G =.. ['*->', true, true], catch(call(G), E, true)` stops the same way. A plain `call(msort([b,a], _))` raises a catchable error. The same happens when the extension is inside the goal of `catch/3`, `findall/3`, `bagof/3`, `setof/3`, or `\+`, and for library names such as `length/2`: `catch(call((fail, length(_, _))), E, true)` stops the process too | Open |
| GAP-11 | High | 5.1 a, 5.2, 7.4.2.1, 7.5.2, 7.7.7 b, 7.11.2.4, 7.12.2 e | A strictly conforming program that uses an extension name as an ordinary, undefined procedure is not treated as one. A clause that calls it (`p :- length([a], _).`) makes the whole file fail to load with DPL1018, so no initialization goal runs. A meta-call raises `permission_error(access, implementation_specific_feature, PI)` whatever the `unknown` flag says, and that permission type is not in 7.12.2 e. `:- dynamic(length/2).` stops the process with a permission error. `assertz(length(a, b))`, `clause(length(_, _), B)`, `abolish(length/2)`, `retract/1`, and `retractall/1` raise permission errors. For such a program these names are undefined procedures: a call must raise `existence_error(procedure, length/2)` or follow `unknown`, `dynamic/1` and `assertz/1` must create a dynamic procedure, `clause/2` and `retract/1` must fail, and `abolish/1` and `retractall/1` must succeed | Open |
| GAP-12 | Low | 5.1 e, 5.5.5, 7.4.2 | `StrictIso` executes any goal as a directive, for example `:- write(x).`. Directives beyond 7.4.2.1–7.4.2.9 are implementation specific, and the strictly conforming mode must reject them | Open |
| GAP-13 | Low | 5.1 e, Cor.3 5.5.12, 7.10.4 | `write_term/2,3` accepts the undocumented write option `spacing(next_argument)` in `StrictIso`. Cor.3 5.5.12 allows extra options only as an implementation specific feature, so the strictly conforming mode must reject it with `domain_error(write_option, spacing(next_argument))`. `max_depth/1` and `portray/1` are rejected | Open |
| GAP-14 | Low | 5.5.11, 5.4 | Internal `$` names (`'$write'/2`, `'$clause'/3`, `'$op'/4`, and others) are reserved in `StrictIso` but undocumented: a user definition of `'$write'/2` is ignored, and calling `'$op'/4` runs an internal built-in | Open |

### Control, database, and all-solutions

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-15 | High | 7.1.6.3, 8.10.2, 8.10.3 | `bagof/3` and `setof/3` never strip `^`: every `V^Goal` raises `permission_error(access, implementation_specific_feature, (^)/2)`. As a result 3 of 14 `bagof/3` examples and 4 of 24 `setof/3` examples fail. The bundled implementation passes the whole goal to `findall/3`, which calls `^/2` as a predicate. Modern is correct | Open |
| GAP-16 | Medium | 7.5.2, 7.5.3, 8.8.1, 8.9 | `call/1`, `(',')/2`, `(;)/2`, `(->)/2`, and `(\+)/1` are neither static nor private. `asserta((call(_) :- true))`, `abolish(call/1)`, and `retractall((_;_))` succeed, and `clause(call(_), B)` fails instead of raising `permission_error(access, private_procedure, call/1)`. The other control constructs and built-ins are protected | Open |
| GAP-17 | Medium | 7.6.2, 8.9.1, 8.9.2 | A clause body that cannot be converted is accepted when the non-callable part is nested: `assertz((foo :- (true, 4)))` succeeds, where ISO requires `type_error(callable, (true, 4))`. `assertz((foo :- 4))` is handled correctly | Open |
| GAP-18 | Medium | 7.4.2.2, 7.4.2.3, 7.4.3 | A procedure declared only with `discontiguous/1` or `multifile/1` does not exist: calling it raises `existence_error` instead of failing. `dynamic/1` is correct. `current_predicate/1` does not find such a procedure either, and `assertz/1` adds clauses to it instead of raising `permission_error(modify, static_procedure, PI)` | Open |
| GAP-19 | Low | Cor.2 8.9.5 | `retractall(mammal(_))` on an unknown procedure creates a dynamic `mammal/1`, so a later call fails instead of raising `existence_error`. Neither the Cor.2 description nor its reference definition in NOTE 2 creates a procedure, and Cor.3 leaves 8.9.5 unchanged. `current_predicate(mammal/1)` succeeds afterwards. SWI-Prolog creates the procedure as documented behavior | Open |
| GAP-20 | Low | 8.8.2.3, Part 2 7.3.2 | `current_predicate(4/1)` raises `type_error(atom, 4)` instead of `type_error(predicate_indicator, 4/1)`. `foo/bar` and `foo/(-1)` also get errors on the component. `current_predicate(foo/300)` raises `representation_error(max_arity)`, although `foo/300` is a predicate indicator and the goal must fail | Open |
| GAP-21 | Low | 8.5.3.3 b, d, e | `=..` checks its list only when the term is unbound. <code>foo(a) =.. [foo&#124;bar]</code> fails instead of raising `type_error(list, …)`. `foo(a) =.. [1, a]` and `foo =.. [f(a)]` fail instead of raising `type_error(atom, 1)` and `type_error(atomic, f(a))`. `foo(a) =.. 4` fails instead of raising `type_error(list, 4)` | Open |
| GAP-66 | High | 5.1 b, 8.10.2, 8.10.3 | A program that defines its own `member/2`, `reverse/2`, or `append/3` changes the results of `bagof/3` and `setof/3`, because the bundled implementation calls those public names. With the common deterministic <code>member(X, [X&#124;_]) :- !.</code>, `findall(K-L, bagof(V, p(K, V), L), R)` loses every group but the first (`[a-[1,3]]` instead of `[a-[1,3], b-[2]]`). Part 1 reserves none of these names, so the program is strictly conforming. Built-in predicates written in Prolog must call only internal helpers | Open |

### Streams and input/output

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-22 | Medium | 7.10.2.3, table 40 | The standard streams report properties that differ from table 40. `user_input` reports `eof_action(eof_code)` where the table has `reset`. `user_output` reports `mode(write)` where the table has `append`, and has no `eof_action(reset)`. Both report `file_name/1` although neither is connected to a file. `tests/conformance/iso_conformance.pl` asserts the `eof_code` value. No output stream reports `eof_action/1`, even one opened with `eof_action(error)` | Open |
| GAP-23 | Medium | 8.11.8 | `stream_property/2` does not enumerate the set of pairs fixed at call time. If the first argument is bound to a stream that is then closed, re-execution raises `existence_error(stream, S)`: `open(F, read, S), stream_property(S, P), close(S), fail`. A call with a closed stream-term also raises `existence_error` instead of failing, and 8.11.8.3 lists no existence error. With an unbound first argument the enumeration reads the live stream table: a loop that closes every stream works, but streams opened during the enumeration are returned and streams closed during it are skipped | Open |
| GAP-24 | Low | 8.11.8.3 | `stream_property/2` accepts an alias as its first argument. A non-stream atom raises `existence_error(stream, foo)` instead of `domain_error(stream, foo)`. A malformed property such as `file_name(1)` or `alias(1)` raises `type_error(atom, 1)` instead of `domain_error(stream_property, file_name(1))` | Open |
| GAP-25 | Medium | 8.12.1.3, 8.12.2.3 | Input bytes that are not valid UTF-8 are returned as U+FFFD instead of raising `representation_error(character)`. The replacement is undocumented. Text input also detects byte-order marks: a file that starts with FF FE is decoded as UTF-16LE, and a UTF-8 byte-order mark is dropped, contrary to the documented UTF-8 mapping | Open |
| GAP-26 | Low | 8.11.5.3 | Opening a file for `write` or `append` in a missing directory raises `existence_error(source_sink, F)`. The note in 8.11.5.3 gives `permission_error(open, source_sink, F)` | Open |
| GAP-27 | Low | 8.11.5.3 m | `reposition(true)` is accepted for a binary pipe that cannot be repositioned, and `stream_property(S, position(_))` then fails. Without the option, such a pipe also reports `reposition(true)` | Open |
| GAP-28 | Low | 8.11.8.3, 8.11.8.5 | `at_end_of_stream(user_output)` raises `permission_error(input, stream, user_output)`, an error 8.11.8.3 does not list | Open |
| GAP-29 | Low | 8.16.6.3 c, d | When the character is bound, `char_code(a, 1.0)`, `char_code(a, foo)`, and `char_code(a, -1)` fail. ISO requires the type error or representation error | Open |
| GAP-30 | Low | 8.17.4 | `halt/1` raises `type_error(integer, N)` for integers outside the tagged range. How the argument maps to the exit status (truncated to 32 bits, then modulo 256) is undocumented | Open |

### Flags

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-31 | Low | 7.11.1, 8.17.1.3, Part 2 6.9.1 | Setting a fixed flag to one of its other permitted values raises a domain error instead of `permission_error(modify, flag, F)`: `set_prolog_flag(bounded, true)`, `set_prolog_flag(integer_rounding_function, down)`, and `set_prolog_flag(colon_sets_calling_context, false)` | Open |

### Arithmetic

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-32 | Medium | 9.1.4 | `0.0/0`, `0/0.0`, and `0.0/0.0` raise `evaluation_error(undefined)`. Float division by zero is `zero_divisor`. `0/0` and `1/0.0` are correct | Open |
| GAP-33 | Medium | 9.3.1.3, Cor.2 9.3.10.3 d | `0^(-1)`, `0.0^(-1)`, and `0 ** -1` raise `zero_divisor`. ISO requires `undefined`. Cor.3's new error e excludes a zero base, so error d still applies | Open |
| GAP-34 | Medium | 9.3.6 | `log(0)` and `log(0.0)` raise `zero_divisor`. ISO requires `undefined`, and `log(0.0)` is one of the standard's examples | Open |
| GAP-35 | — | Cor.3 9.3.10 | `2^(-1)` raises `type_error(float, 2)`. Cor.2 required `evaluation_error(undefined)`, but Cor.3 replaces error e and the example: a negative integer exponent with a base other than 1, 0, or −1 is `type_error(float, VX)`. DotProlog conforms, and `(-1)^(-2)` gives `1` as required | Closed: conforms to Cor.3 |
| GAP-36 | Medium | 9.3.1.3, 9.3.5.3, Cor.2 9.3.10.3 g | Float underflow is never signaled. The 9.3 functions whose error lists include underflow return `0.0` for a nonzero result too small to represent: `exp(-1000)` and `exp(-(10^400))` (9.3.5.3 d); `10.0 ** -400`, `10 ** -400`, `0.5 ** (10^400)`, and `(10^400) ** -1` (9.3.1.3 f); `2.0^(-10000)` and `0.5^(10^400)` (Cor.2 9.3.10.3 g). These require `evaluation_error(underflow)`. For the basic operations (9.1.4, 9.1.5), 9.1.4.2 lets the processor return the rounded value instead (`1.0e-300*1.0e-300` gives `0.0`), but that implementation defined choice is undocumented (GAP-64) | Open |
| GAP-37 | Medium | 8.7.1, 9.1.5, 9.1.6 | Integer-to-float conversion overflow is not detected in mixed-mode arithmetic or comparisons. An integer outside the float range is widened to ±∞, and the result is computed from that value: `1/(10^400)`, `2/(10^400)`, and `1.0/(10^400)` give `0.0`; `0.0*(10^400)` and `(10^400)/(10^400)` raise `evaluation_error(undefined)`; `(10^400)/0.0` raises `zero_divisor`. Every mixed comparison answers as if the integer were infinite: `10^400 =:= 1.0` fails and `1.0 < 10^400` succeeds. 9.1.5 and the 8.7.1 axioms require `evaluation_error(float_overflow)` in each case, and `float(10^400)`, `1.0+10^400`, and `(10^400)/2` already raise it. The rule covers only the 9.1 operations and the 8.7.1 comparisons. The 9.3 functions have no conversion step: `atan(10^400)` correctly gives π/2, `exp(-(10^400))` is an underflow case (GAP-36), and the errors that other 9.3 functions raise for such integers are GAP-68. The characteristics page's statement that conversion overflow always raises `float_overflow` is inaccurate. See [recheck evidence](#recheck-evidence) | Open |
| GAP-38 | Medium | 8.7.1, 9.1.4.1, 9.1.6 | Integers outside the tagged range (magnitude 2^59 or more) are converted to float by truncation, while smaller integers round to nearest. As a result `float(2*(2^58+33))` differs from `2.0*float(2^58+33)`, and `2^59+96 =:= 2.0^59` succeeds. The rounding function must be one function, and it must be sign-symmetric and scale-invariant | Open |
| GAP-39 | Medium | 9.1.6.1 | `round/1` computes the floating-point value `floor(X + 0.5)`. So `round(0.49999999999999994)` gives `1` (ISO: `0`), and `round(4503599627370497.0)` gives `4503599627370498` | Open |
| GAP-67 | Medium | 7.1.3, 7.10.5 c | `writeq(-0.0)` writes `-0.0`, where ISO writes `0.0`: the float set of 7.1.3 has one zero, and a `-` is written only before a negative value (entry #364 of the conformity table). Floats are interned by value with `0.0` and `-0.0` sharing one entry, so the sign of every later zero depends on which was created first: after `X is -1.0*0`, `Y is 1.0*0, writeq(Y)` writes `-0.0` | Open |
| GAP-68 | Medium | 9.3.1, 9.3.2, 9.3.3, 9.3.6, 9.3.7, Cor.2 9.3.10, Cor.2 9.3.14 | 9.3 functions of an integer outside the float range raise spurious errors or return wrong values, because the integer is widened to ±∞. `sin(10^400)`, `cos(10^400)`, and `tan(10^400)` raise `evaluation_error(undefined)`, although their error lists allow no error. `sqrt(10^400)`, `log(10^400)`, and `(10^400)**0.5` raise `float_overflow`, although the results (about `1.0e200`, `921.03`, `1.0e200`) are representable. `(-1)**(10^400+1)` and `(-1.0)^(10^400+1)` give `1.0`; the required value is `-1.0`. `atan(10^400)` and `exp(10^400)` are correct | Open |

### Modules (Part 2)

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-40 | High | 6.2.4.2, 6.2.6 | Module text cannot define or export names that DotProlog provides as non-ISO predicates, whether written in Prolog (`length/2`, `reverse/2`) or native (`between/3`, `msort/2`, `writeln/1`). The standard's own 6.2.6.1 `utilities` example is rejected with DPL1019 and DPL1018. User text can redefine the names written in Prolog; for the native ones see GAP-09. The same classification rejects grammar rules (GAP-69) | Open |
| GAP-41 | High | 4.4.2, 6.2.2, 6.3.1, 6.6.3.1 d, 6.6.4 b, 7.3.2.1 | Exported procedures are visible from `user` without an import. If module `m` exports `e/1`, then `e(X)` in user text succeeds, `current_predicate(e/1)` succeeds, and `predicate_property(e(_), defined_in(D))` gives `D = user`. The characteristics page documents this alias as an extension, but it changes the meaning of conforming text and stays active in `StrictIso` | Open |
| GAP-42 | High | 4.4.2, 6.3, 7.3.2, Part 1 7.7.7 b | Module procedures share a name space with user atoms of the form `'M:Name'`. A user clause for `'m:h'/1` and module `m`'s `h/1` replace each other, and `current_predicate/1` in `user` lists internal names such as `'m:e'/1`. User atoms that contain `:` are split into a module and a name even in text without modules: `predicate_property('a:b'(_), P)` raises `existence_error(module, a)`, an asserted `'foo:bar'/1` reports `defined_in(foo)`, and an undefined `'foo:bar'(1)` raises `existence_error(procedure, foo:bar/1)` | Open |
| GAP-43 | High | 6.6.3, 6.6.4 | At run time, a qualified goal `M:G` does not search `M`'s visible database. If `c` imports `q/1` from `m2`, `c:q(X)` returns the `q/1` of a different module, `m1`. `k:a1` succeeds although `k` imports nothing. Calls written literally in body text are resolved correctly, but goals known only at run time are not, including `call(G)` inside the importing module's own body. When `user` defines the same name, `c:q(X)` runs `user`'s `q/1` | Open |
| GAP-44 | High | 6.2, 6.5.2.2 b 3, 6.7.1 | `M:call(G)` raises `existence_error(procedure, M:call/1)` | Open |
| GAP-45 | High | 6.4, 6.4.3, 6.6.7 | Qualified built-in metapredicates run their goal in `user`, whatever the calling context: `c:once(G)`, `c:findall(T, G, L)`, `c:bagof/3`, `c:setof/3`, and `c:catch/3` (goal and recovery) look `G` up in `user` instead of `c`, including when they are called from another module's body. Qualified control constructs and context-sensitive built-ins are correct | Open |
| GAP-46 | High | 6.4.3, 6.4.4.1 | The meta-arguments of user metapredicates are not passed as `M:X`. They arrive as internal atoms such as `'s:sp'(X)`, or unqualified. The standard's 6.4.4.1 trace example fails silently | Open |
| GAP-47 | High | 6.5.2, 7.4.1.4 | An asserted clause body loses the asserting context. In the standard's 7.4.1.4 example b, the stored clause is `horns(X) :- moose(X)`, and calling it raises `existence_error(procedure, animals:moose/1)` | Open |
| GAP-48 | Medium | 6.4.1, 7.3, 7.4 | From `user`, database built-ins whose qualified argument is only known at run time raise errors about `(:)/2`. For example, `G = clause(m:s(X), B), call(G)` raises `permission_error(access, private_procedure, (:)/2)`, and `G = abolish(m:foo/a), call(G)` raises `type_error(predicate_indicator, m:foo/a)`. The same goals written literally work, even inside `findall/3` or `call/1` | Open |
| GAP-49 | Medium | 6.2.2, 7.3.1.3 g, 7.4.1.3 g–7.4.4.3 k | Modifying an imported procedure through a `DM:` qualification is not rejected. `asserta(imp:e(zed))` creates a local `imp:e/1` that shadows the import, `abolish(imp:f/0)` succeeds, and `retract(imp:e(orig))` fails silently. ISO requires `permission_error(modify, implicit, PI)`. `clause(imp:e(X), B)` fails instead of raising `permission_error(access, implicit, e/1)`. For `retract/1`, error g takes precedence over the conflicting 7.4.3.4 example | Open |
| GAP-50 | Medium | 6.1.1.3, 7.4.1.1 | `asserta(m:(dm:h :- B))` stores a `(:-)/2` fact in `m`, instead of adding the clause `h :- B` to `dm` | Open |
| GAP-51 | Low | Part 1 7.4.2.4, 5.2.1, 6.2.5.4 | An `op/3` directive in a module body affects how the body is read, which conforms, but not the module's operator table at run time: `a:current_op(P, T, ~~>)` fails, and `writeq/1` ignores the operator. Part 1 7.4.2.4 makes the run-time effect implementation defined, and the characteristics page says directive operators affect later run-time reading. Interface and user-text `op/3` follow that choice; module bodies do not | Open |
| GAP-52 | Low | 7.3.1.3 a, c, 7.4.1.3 a, 7.4.3.3 a | In a module context, a variable head raises `type_error(callable, _)` instead of `instantiation_error`. Examples: `clause(_, true)`, `asserta((_ :- true))` | Open |
| GAP-53 | Medium | 7.3.1.3 f, 7.4.1.3 f, 7.4.3.3 f, 7.4.4.3 j | In a module context, built-ins and control constructs are treated as undefined user procedures. `clause(atom_length(_, _), B)` fails instead of raising `permission_error(access, private_procedure, atom_length/2)`, and `asserta(atom_length(a, 1))`, `retract/1`, and `abolish(atom_length/2)` succeed instead of raising `permission_error(modify, static_procedure, atom_length/2)`. The asserted clause creates a module-local `atom_length/2` that shadows the built-in in that module | Open |
| GAP-54 | Low | 7.4.1.3 f–7.4.4.3 j | Culprits in permission errors are internal names: `permission_error(modify, static_procedure, 'mammals:legs'/1)` instead of `legs/1` | Open |
| GAP-55 | Low | 6.8, Part 1 7.5.2, 7.5.3 | Some properties are wrong. A dynamic `user` procedure reports `private` (ISO: public). A built-in reports `public` and `defined_in(C)` for the calling context C, including `user`. Control constructs report no properties (ISO: `static`, `private`) | Open |
| GAP-56 | Low | 6.8 | `exported` is reported for a procedure that the module only imports | Open |
| GAP-57 | Low | 6.4.1, 6.8 | The built-in metapredicates, such as `findall/3`, `clause/2`, `asserta/1`, `once/1`, `\+/1`, `bagof/3`, and `setof/3`, have no `metapredicate(MI)` property | Open |
| GAP-58 | Low | 6.2.4.2 | Exporting an imported procedure is accepted without a diagnostic. The standard's own 7.2 example text does the same. Exporting a procedure that no body defines is also accepted silently | Open |
| GAP-59 | Low | 6.7.1.1 f, 6.7.1.3 b, d | `call(m:3)` raises `type_error(callable, 3)` instead of `type_error(callable, m:3)`. `call(m:(write(x), 3))` writes `x` before raising `type_error(callable, 3)`: 6.7.1.1 f converts the whole goal first, so the error must be `type_error(callable, m:(write(x), 3))` with no output. `call(m:X:foo)` raises `instantiation_error`, which conforms to 6.7.1.3 b and matches the parallel examples in 7.2.2.4, 7.3.1.4, and 7.4.4.4; the 6.7.1.4 example, which gives a type error, is inconsistent with them | Open |
| GAP-60 | Low | 6.6.4 | An undefined procedure called from `user` raises the unqualified Part 1 form, `existence_error(procedure, foo/0)`, while Part 2 gives `user:foo/0`. The two parts conflict, so record the choice | Open |
| GAP-61 | Low | 6.5.2.1–6.5.2.3, 6.5.3 | Stored clause bodies are not simplified as 6.5.2.1–6.5.2.3 require: `n:(X -> throw(B))` is kept instead of `(call(n:X) -> throw(B))`, and a redundant qualification by the defining module is kept (`asserta((m:h :- m:q))` stores `m:q`, where ISO stores `q`). 6.5.2.4 permits additional conversions only, so documentation does not resolve this | Open |
| GAP-70 | Low | Part 1 7.8.3.3 | In a file that also contains Part 2 module text, the culprit of a meta-call error shows internal names: `catch(call((write(side_effect), 3)), E, true)` in `user` gives `type_error(callable, ('$write'(user, side_effect), 3))`. Without module text in the file, the culprit is the goal as given | Open |

### Grammar rules (Part 3)

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-62 | Low | 7.13.3, 7.4.2, Part 2 6.2.4.2 | A negative non-terminal arity is accepted whenever `N + 2 >= 0`: `dynamic(foo//(-1))` makes `foo/1` dynamic, `multifile(bar//(-2))` affects `bar/0`, and the Part 2 `export(h//(-1))` exports `h/1`. `foo/(-1)` and `foo//(-3)` are rejected | Open |
| GAP-63 | Low | 8.18.1.5 | `phrase([a], foo)` raises `type_error(list, foo)`, while `phrase([a], foo, [])` fails. `phrase/2` is defined as `phrase(G, S0, [])`, so the two must agree | Open |
| GAP-69 | Medium | 7.4.4, 7.13.4 | In `StrictIso`, grammar rules whose `NT/(N+2)` is a non-ISO DotProlog predicate are rejected with DPL1007: `member --> [a].`, `succ --> [a].`, `between(X) --> [X].`. 7.4.4 forbids only built-in predicates and control constructs, and in the strictly conforming mode these names are neither. The equivalent plain clause `member(_, x).` is accepted | Open |
| GAP-71 | Low | 5.5.13, 7.13.5, 8.18.1.1 | `phrase(m:b, [x])` treats `m:b` as a module-qualified non-terminal and succeeds, while the rule `a --> m:b.` expands `m:b` as the non-terminal `(:)//2`, so `phrase(a, [x])` raises `existence_error(procedure, (:)/4)`. The two paths must agree; the TS defines no `:` grammar control, so the rule translation is the literal reading. Contestable, because the TS cites Part 2 | Open |

### Documentation

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| GAP-64 | Medium | Part 1 5.4, Part 2 4.1 d, Part 3 5.1 | These implementation defined features are not documented on the characteristics page: the collating integers of the control escapes (`\a` 7, `\b` 8, `\f` 12, `\n` 10, `\r` 13, `\t` 9, `\v` 11), and FF, VT, and CR as layout; the float rounding function, approximate addition, and underflow of arithmetic results (9.1.4.1–9.1.4.3); negative shift counts (`16 >> -2` gives 64 and `16 << -2` gives 4); position terms at and past end of stream (7.10.2.9); the effect of directives that add or remove clauses while text is prepared (7.4.3); invalid semicontexts, <code>'&#124;'</code> inside if-then-else, and whether `(\+)//1` and `(->)//2` are provided (Part 3 7.13.1, 7.14.6, 7.14.11, 7.14.12). Also GAP-14, GAP-25, and GAP-30 | Open |
| GAP-65 | Medium | Part 1 clause 3, 5.4 | Issue #13 terminology and arithmetic classification corrected: the characteristics reference and ledgers distinguish configuration, implementation-defined behavior, extensions, and required behavior. COMPATIBILITY.md and the SWI ledger identify float `/` on two integers as required by 9.1.1 and 9.1.5 (`1/1` is `1.0`). Evidence labels now distinguish documentation from tests. Broader conformance claims still need reconciliation with the behavioral gaps in this tracker | Partial |

## Requirement checklists

### Part 1 clause 5: compliance

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 5.1 a | Prepare all conforming text for execution | Partial | GAP-01 to GAP-04, GAP-09, GAP-11 |
| 5.1 b | Execute conforming goals correctly | Partial | GAP-09 to GAP-11, GAP-15 to GAP-21, GAP-66 |
| 5.1 c | Reject text and read-terms whose syntax does not conform | Partial | 344 of 365 reader probes behave as required, including 7 manual confirmations; 21 deviate. The accepted invalid texts are GAP-01, GAP-05, GAP-06, and GAP-07 |
| 5.1 d | Specify every permitted variation in the way the standard prescribes | Partial | GAP-64, GAP-65 |
| 5.1 e | Offer a strictly conforming mode that rejects implementation specific features in text and during execution | Partial | Extension flags, flag values, evaluables, and module-extension directives are rejected. Extension predicates are rejected only when the user has not defined them (GAP-09), and the rejection takes the form of a non-standard error, a load failure, or a process stop (GAP-10, GAP-11). Extension syntax (`a xor b`, `a *-> b`, `module a`) is accepted (GAP-01). GAP-12 to GAP-14 |
| 5.2 | Conforming and strictly conforming Prolog text | Partial | GAP-01, GAP-03, GAP-09, GAP-11 |
| 5.3 | Conforming and strictly conforming goals | Partial | GAP-09 to GAP-11 |
| 5.4 | Document every implementation defined and implementation specific feature | Partial | See [implementation defined documentation](#implementation-defined-documentation). GAP-64 |
| 5.5.1 | Extra syntax must not change the meaning of conforming text | Partial | GAP-01, GAP-02 |
| 5.5.1 note | An infix and a postfix operator with the same name only as an extension | Done | `op(200, xf, +)` gives `permission_error(create, operator, +)` |
| 5.5.2 | Extra predefined operators | Partial | GAP-01 |
| 5.5.3 | Another initial character-conversion mapping | Done | The initial mapping is the identity, and `char_conversion` starts `on` |
| 5.5.4 | Extra types | Done | `1r3` is a syntax error, `double_quotes = string` is refused, and back-quoted text denotes no term |
| 5.5.5 | Extra directives | Partial | `use_module/1` and `module/2` are rejected, and `table/1` is undefined. GAP-12 |
| 5.5.6 | Extra side effects | Unverified | Covered only as far as extension predicates are rejected |
| 5.5.7 | Extra control constructs | Partial | `*->` is rejected as a control construct. GAP-01, GAP-09, GAP-10 |
| 5.5.8 | Extra flags | Done | Only the Part 1 flags and Part 2 `colon_sets_calling_context` exist. `occurs_check` gives `domain_error(prolog_flag, occurs_check)` |
| 5.5.9 | Extra built-in predicates | Partial | GAP-09, GAP-11 |
| 5.5.10 | Extra evaluable functors | Done | `cot/1`, `rdiv/2`, `e/0`, `gcd/2`, and 9 others raise `type_error(evaluable, F/N)` |
| 5.5.11 | Reserved atoms | Partial | GAP-14 |
| Cor.3 5.5.12 | Extra options; an invalid option gives only an instantiation error or a domain error | Partial | The error conditions are correct for stream, close, read, and write options. GAP-13 |

### Part 1 clause 6: syntax

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 6.1.2 a | Same-named variables in a read-term are one variable, and each `_` is distinct | Done | 3/3 |
| 6.1.2 b | Atom names, including `[]` and `{}` with or without inner layout | Done | 5/5 |
| 6.1.2 c | Integer value of decimal, binary, octal, hex, and character code constants | Done | 37/37 |
| 6.1.2 d | Float value by rounding | Done | 19/19 |
| 6.1.2 e | Compound terms: functor, arity, arguments | Done | 25/25 |
| 6.2, 6.2.1 | Prolog text is a sequence of directives and clauses | Done | `:- op/3` takes effect for the rest of the text |
| 6.2.1.1, 6.2.1.2 | Directives and clauses | Done | |
| Cor.3 6.2.1 | Prolog text may end with layout text | Done | Trailing spaces, a line comment, or a bracketed comment after the last clause |
| 6.2.2 | A data read-term ends with an end token and may have priority 1201 | Done | |
| 6.3.1.1 | Number tokens are terms of priority 0 | Done | |
| 6.3.1.2 | `-` followed by a numeric literal is a negative number, including after layout, a comment, or with `-` quoted | Done | 28/28. `- 1` gives `-1`, and `- (1)` gives `-(1)` (issue #12) |
| 6.3.1.3 | An operator atom as an operand has priority 1201 | Done | 24/24 |
| 6.3.2 | Variables are terms of priority 0 | Done | |
| 6.3.3 | Functional notation needs `(` immediately after the name | Partial | 15/16. GAP-03, GAP-04 |
| 6.3.3.1 | Arguments have priority 999 or less, and a comma is not an atom | Done | 11/11 |
| 6.3.4, tables 5 and 6 | Operator notation, associativity, and brackets | Done | 13/13 |
| 6.3.4.1 | Operand priorities; `op (` versus `op(` | Done | 18/18 |
| 6.3.4.2 | Prefix, infix, and postfix operators | Partial | 25/25 cases from the text. GAP-03 |
| 6.3.4.3 | Operators are atoms; one per class and name; no infix and postfix pair; `','` cannot be changed | Done | |
| Cor.2 6.3.4.3 | The bar token stands for <code>'&#124;'</code> when it is an operator | Partial | GAP-02 |
| Cor.2 6.3.4.3 | `[]` and `{}` cannot be operators | Done | `permission_error(create, operator, _)` |
| Cor.2 6.3.4.3 | <code>'&#124;'</code> can only be an infix operator of priority 1001 or more, and the bar is not an atom | Done | |
| 6.3.4.4, table 7 | Initial operator table | Partial | All 41 Part 1 and Cor.2 operators, plus Part 2 `:` and Part 3 <code>'&#124;'</code>. GAP-01 |
| 6.3.5, 6.3.5.1 | List notation | Done | 11/11 |
| 6.3.6 | Curly bracketed terms | Done | 6/6 |
| 6.3.7 | Double-quoted lists under `codes`, `chars`, and `atom` | Done | Through the reader and consulted text |
| Cor.1 6.3.7 | Under `atom`, the atom keeps its operator priority | Partial | 5/6. GAP-05 |
| 6.4 | Layout before tokens; longest match | Done | |
| 6.4.1 | Layout text and comments; `/* */` does not nest | Done | 10/10 |
| 6.4.2 | Name tokens | Done | 18/18 |
| 6.4.2.1 | Quoted characters and escapes; octal and hex escapes need a closing `\` | Done | 34/34. `\e`, `\s`, `\z`, `\d`, `\N`, `\u0041`, and `\^@` are syntax errors |
| 6.4.3 | Variable tokens | Done | |
| 6.4.4 | Integer tokens, including `0'` character codes | Done | 37/37. `0''` is an error, `0'''` gives 39 (issues #7, #10, #12) |
| 6.4.5 | Float tokens | Done | 19/19. `1.e2`, `1e10`, and `.5` are errors |
| 6.4.6 | Double-quoted list tokens | Done | |
| 6.4.7 | A back-quoted string is a token that denotes no term | Done | A syntax error in `StrictIso` |
| 6.4.8 | Other tokens; the end char | Partial | 4/5. GAP-06 |
| Cor.2 6.4, 6.4.8 | Bar token | Done | |
| 6.5 | Processor character set and character classes | Partial | GAP-07 |
| 6.5.1 | Graphic characters | Done | |
| 6.5.2 | Alphanumeric characters | Done | |
| 6.5.3 | Solo characters, including the Cor.2 bar | Done | |
| 6.5.4 | Layout characters | Partial | GAP-64 |
| 6.5.4 | Collating integers of quoted characters | Partial | Octal and hex values are correct. Control escapes: GAP-64 |
| 6.5.5 | Meta characters | Done | |
| 6.6 | Collating sequence | Done | Documented as code-unit order |

### Part 1 clause 7: language concepts and semantics

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 7.1 | Every term has exactly one type | Done | |
| 7.1.1 | Variables | Done | |
| 7.1.1.1–7.1.1.4 | Variable sets, existential variables, free variables | Partial | Free-variable grouping is correct. GAP-15 |
| Cor.2 7.1.1.5 | Witness variable list | Done | |
| 7.1.2 | Integers | Done | Unbounded |
| 7.1.2.1 | Bytes | Done | |
| 7.1.2.2 | Character codes and their byte mapping | Done | UTF-8 text, raw binary |
| 7.1.3 | Floats | Partial | binary64. GAP-67 |
| 7.1.4, 7.1.4.1 | Atoms and the character set | Done | |
| 7.1.4.2 | Boolean option values | Done | |
| 7.1.5 | Compound terms | Done | |
| 7.1.6.1 | Variants | Done | |
| 7.1.6.2 | Renamed copies | Done | |
| 7.1.6.3 | Iterated-goal term; Cor.3 applies it only to a term of the form `^(_, G)` | Partial | GAP-15 |
| 7.1.6.4 | Proper sublists | Done | |
| 7.1.6.5 | Sorted lists | Done | |
| 7.1.6.6–7.1.6.8 | Predicate indicators and their sequences and lists | Done | |
| Cor.2 7.1.6.9 | List prefix | Done | |
| 7.2 | Term order: variable, float, integer, atom, compound | Done | |
| 7.2.1 | Variable order is implementation dependent and stable while sorting | Done | |
| 7.2.2, 7.2.3 | Floats and integers by value | Done | |
| 7.2.4 | Atoms by collating sequence | Done | |
| 7.2.5 | Compounds by arity, name, then arguments | Done | |
| 7.3.1, 7.3.2 | Most general unifier; the Herbrand algorithm | Done | 8/8 |
| 7.3.3, 7.3.4 | STO and NSTO; `unify_with_occurs_check/2` | Done | |
| 7.4.1 | Undefined features of Prolog text | Done | Observed behavior recorded; the standard leaves it undefined |
| 7.4.2 | Directives | Partial | All nine are supported, except `dynamic/1` for an extension name (GAP-11) and declarations without clauses (GAP-18). GAP-12 |
| 7.4.2.1 | `dynamic/1` | Partial | 5/5. GAP-11 |
| 7.4.2.2 | `multifile/1` | Partial | GAP-18 |
| 7.4.2.3 | `discontiguous/1` | Partial | GAP-18 |
| 7.4.2.4 | `op/3` directive | Done | |
| 7.4.2.5 | `char_conversion/2` directive | Done | |
| 7.4.2.6 | `initialization/1` | Done | Source order, after preparation |
| 7.4.2.7 | `include/1` | Done | |
| 7.4.2.8 | `ensure_loaded/1` | Partial | A native-name procedure defined in the loaded unit is not seen, and the calling file is rejected. GAP-09, GAP-11 |
| 7.4.2.9 | `set_prolog_flag/2` directive | Done | |
| 7.4.3 | Clauses; declared procedures without clauses exist | Partial | GAP-18 |
| 7.5 | The database | Done | |
| 7.5.1 | Preparation gives the initial database | Done | |
| 7.5.2 | Built-ins and control constructs are static | Partial | GAP-16, GAP-11 |
| 7.5.3 | Private and public procedures | Partial | GAP-16 |
| 7.5.4 | Logical update view | Done | 5/5 |
| 7.6.1 | Converting a term to a clause head | Done | |
| 7.6.2 | Converting a term to a clause body | Partial | GAP-17 |
| 7.6.3, 7.6.4 | Converting a clause back to a term | Done | |
| 7.7.1–7.7.3 | Execution model, answers, goal delivery | Done | |
| 7.7.4–7.7.6 | Success, failure, re-execution | Done | |
| 7.7.7 | Clause selection; the `unknown` flag | Partial | GAP-11 |
| 7.7.8, 7.7.9 | Backtracking; side effects survive it | Done | |
| 7.7.10–7.7.12 | User-defined procedures and built-in execution | Done | |
| 7.8.1 | `true/0` | Done | 1/1 example |
| 7.8.2 | `fail/0` | Done | 1/1 example |
| 7.8.3 | `call/1` | Partial | 15/15 examples, including the Cor.2 example. GAP-10 |
| 7.8.4 | `!/0` | Done | 12/12 examples |
| 7.8.5 | `(',')/2` | Done | 3/3 examples |
| 7.8.6 | `(;)/2` | Done | 5/5 examples |
| 7.8.7 | `(->)/2` | Done | 6/6 examples |
| 7.8.8 | if-then-else | Done | 9/9 examples, including the Cor.1 correction |
| 7.8.9 | `catch/3` | Partial | 8/8 examples. GAP-10 |
| 7.8.10 | `throw/1` | Done | |
| 7.8 | Control constructs are static | Partial | GAP-16 |
| 7.9.1 | Evaluating an expression | Done | |
| 7.9.2 a, b | Instantiation errors | Done | |
| 7.9.2 c | `type_error(evaluable, F/N)` | Done | |
| 7.9.2 d | `float_overflow` | Partial | GAP-37, GAP-68 |
| 7.9.2 e | `int_overflow` | Done | Unreachable, because `bounded` is `false` |
| 7.9.2 f | `underflow` | Partial | GAP-36 |
| 7.9.2 g, h | `zero_divisor` and `undefined` | Partial | GAP-32, GAP-33, GAP-34, GAP-68 |
| Cor.2 7.9.2 i | `type_error(integer, C)` and `type_error(float, C)` | Done | |
| 7.10.1, 7.10.1.1 | Sources, sinks, and I/O modes | Done | |
| 7.10.2.1 | Stream-terms | Done | |
| 7.10.2.2 | Stream aliases | Done | |
| 7.10.2.3 | Standard streams (table 40) | Partial | GAP-22 |
| 7.10.2.4 | Current streams | Done | |
| 7.10.2.5 | Target streams | Done | |
| 7.10.2.6 | Text streams | Done | |
| 7.10.2.7 | Binary streams | Done | |
| 7.10.2.8 | Stream positions | Done | |
| 7.10.2.9 | End of stream | Partial | The states are correct. GAP-64 |
| 7.10.2.10 | Flushing | Done | |
| 7.10.2.11 | Open options | Partial | GAP-27 |
| 7.10.2.12 | Close options | Done | |
| 7.10.2.13 | Stream properties | Partial | GAP-22, GAP-24, GAP-27 |
| 7.10.3 | Read options; Cor.3 fixes left-to-right order | Done | `f(B, A, _C, B, _, _D, A)` gives the variables `[B, A, _C, _, _D]`, the names `['B'=B, 'A'=A, '_C'=_C, '_D'=_D]`, and the singletons `['_C'=_C, '_D'=_D]` |
| 7.10.4 | Write options, including the Cor.3 `variable_names/1` | Partial | `variable_names/1` 10/10. GAP-13 |
| 7.10.5 a–e | Writing variables, numbers, atoms, and `'$VAR'(N)`; Cor.3 a1 writes a named variable unquoted | Partial | GAP-67 |
| 7.10.5 f, g, Cor.3 e2, e3 | Functional notation; list and curly notation only with `ignore_ops(false)` | Done | `{x}`, `{}(x)` with `ignore_ops(true)`, <code>[a&#124;(b:-c)]</code>, and `'.'(1,'.'(2,'.'(3,[])))` from `write_canonical/1` |
| 7.10.5 h | Operator form, brackets, and spacing, with the Cor.3 bracket rules | Done | 37 cases re-read as identical, including `- (1)`, `1- -1`, and `a=(\+)` (issue #8). Cor.3: `- (a^2)`, `- (-)`, and `\+ (-)` |
| 7.11 | Flag values and changeability | Done | |
| 7.11.1.1 | `bounded` | Partial | `false`. GAP-31 |
| 7.11.1.2, 7.11.1.3 | `max_integer` and `min_integer` | Done | No value, as required when unbounded |
| 7.11.1.4 | `integer_rounding_function` | Partial | `toward_zero`. GAP-31 |
| 7.11.2.1 | `char_conversion` | Done | Starts `on` |
| 7.11.2.2 | `debug` | Done | |
| 7.11.2.3 | `max_arity` | Done | 255 |
| 7.11.2.4 | `unknown` | Done | |
| 7.11.2.5 | `double_quotes` | Done | `codes` |
| 7.12, 7.12.1 | Errors and their effect | Done | The context is a fresh variable |
| 7.12.2 a | `instantiation_error` | Done | |
| 7.12.2 b | `type_error` for each valid type, with the Cor.2 changes and the Cor.3 `float` type | Done | 14/14 |
| 7.12.2 c | `domain_error`, including Cor.2 `order`; Cor.3 removes `character_code_list` | Done | No source raises the removed domain |
| 7.12.2 d | `existence_error` | Done | |
| 7.12.2 e | `permission_error` | Partial | Every standard type is raised where required. GAP-11 |
| 7.12.2 f | `representation_error` | Done | |
| 7.12.2 g | `evaluation_error` | Partial | GAP-32 to GAP-37, GAP-68 |
| 7.12.2 h | `resource_error` | Done | |
| 7.12.2 i | `syntax_error` | Done | |
| 7.12.2 j | `system_error` | Unverified | No probe could trigger one |
| Cor.2 7.12.2 k | `uninstantiation_error` | Done | |

### Part 1 clause 8: built-in predicates

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 8.2.1 | `(=)/2` | Done | 11/11 examples |
| 8.2.2 | `unify_with_occurs_check/2` | Done | 16/16 examples |
| 8.2.3 | `(\=)/2` | Done | 10/10 examples |
| Cor.2 8.2.4 | `subsumes_term/2` | Done | 6/6 examples |
| 8.3.1 | `var/1` | Done | 4/4 examples |
| 8.3.2 | `atom/1` | Done | 7/7 examples |
| 8.3.3 | `integer/1` | Done | 5/5 examples |
| 8.3.4 | `float/1` | Done | 5/5 examples |
| 8.3.5 | `atomic/1` | Done | 5/5 examples |
| 8.3.6 | `compound/1` | Done | 8/8 examples |
| 8.3.7 | `nonvar/1` | Done | 6/6 examples |
| 8.3.8 | `number/1` | Done | 5/5 examples |
| Cor.2 8.3.9 | `callable/1` | Done | 4/4 examples |
| Cor.2 8.3.10 | `ground/1` | Done | 2/2 examples |
| Cor.2 8.3.11 | `acyclic_term/1` | Done | 1/1 example |
| 8.4.1 | `(==)/2`, `(\==)/2` | Done | 5/5 examples |
| 8.4.1 | `(@<)/2`, `(@=<)/2`, `(@>)/2`, `(@>=)/2` | Done | 10/10 examples, 17 order checks |
| Cor.2 8.4.2 | `compare/3` | Done | 6/6 examples, 3/3 errors |
| Cor.2 8.4.3 | `sort/2` | Done | 5/5 examples, 8/8 errors |
| Cor.2 8.4.4 | `keysort/2` | Done | 3/3 examples, 14/14 errors |
| 8.5.1 | `functor/3` | Done | 18/18 examples, 6/6 errors |
| 8.5.2 | `arg/3` | Done | 11/11 examples, 4/4 errors |
| 8.5.3 | `(=..)/2` | Partial | 14/14 examples, 4/9 errors. `foo(a) =.. 4` fails too. GAP-21 |
| 8.5.4 | `copy_term/2` | Done | 8/8 examples |
| Cor.2 8.5.5 | `term_variables/2` | Done | 6/6 examples, 2/2 errors |
| 8.6.1 | `is/2` | Done | 6/6 examples, 2/2 errors |
| 8.7.1 | `(=:=)/2`, `(=\=)/2`, `(<)/2`, `(=<)/2`, `(>)/2`, `(>=)/2` | Partial | 24/24 examples, 6/12 errors. GAP-37, GAP-38 |
| 8.8.1 | `clause/2` | Partial | 10/10 examples, 8/12 errors. GAP-11, GAP-16 |
| 8.8.2 | `current_predicate/1` | Partial | 6/6 examples, 2/5 errors. GAP-18, GAP-20 |
| 8.9.1 | `asserta/1` | Partial | 7/7 examples, 5/9 errors. GAP-11, GAP-16, GAP-17, GAP-18 |
| 8.9.2 | `assertz/1` | Partial | 7/7 examples, 5/6 errors. GAP-11, GAP-16, GAP-17, GAP-18 |
| 8.9.3 | `retract/1` | Partial | 10/10 examples, 6/8 errors. GAP-11, GAP-16 |
| 8.9.4 | `abolish/1` | Partial | 5/5 examples, 11/15 errors. GAP-11, GAP-16 |
| Cor.2 8.9.5 | `retractall/1` | Partial | 6/6 examples, 3/5 errors. GAP-11, GAP-16, GAP-19 |
| 8.10.1 | `findall/3` | Partial | 8/8 examples, 4/4 errors. GAP-10 |
| 8.10.2 | `bagof/3` | Partial | 10/14 examples, 4/6 errors. Examples 7, 8, and 13 and the `_^_` and `_^1` error cases are GAP-15; example 9 is GAP-10. GAP-66 |
| 8.10.3 | `setof/3` | Partial | 19/24 examples, 4/6 errors. Examples 9, 10, 20, and 22 are GAP-15; example 11 is GAP-10. GAP-66 |
| 8.10.2, 8.10.3 | `^` makes variables existential | Missing | GAP-15 |
| 8.11.1 | `current_input/1` | Done | 3/3 errors |
| 8.11.2 | `current_output/1` | Done | 2/2 errors |
| 8.11.3 | `set_input/1` | Done | 6/6 errors |
| 8.11.4 | `set_output/1` | Done | 5/5 errors |
| 8.11.5 | `open/4` | Partial | 3/3 examples, 25/28 errors, 10/10 Cor.3 option errors. GAP-26, GAP-27 |
| 8.11.5.5 | `open/3` | Partial | GAP-26 |
| 8.11.6 | `close/2`, `close/1` | Done | 11/11 errors, 5/5 Cor.3 option errors |
| 8.11.7 | `flush_output/1`, `flush_output/0` | Done | 5/5 errors |
| 8.11.8 | `stream_property/2` | Partial | 1/2 examples, 3/4 errors; error c is raised as `type_error(atom, _)` for some malformed properties. GAP-22, GAP-23, GAP-24 |
| 8.11.8.5 | `at_end_of_stream/0` | Done | |
| 8.11.8.5 | `at_end_of_stream/1` | Partial | 4/4 errors. GAP-28 |
| 8.11.9 | `set_stream_position/2` | Done | 9/9 errors |
| 8.12.1 | `get_char/1,2` | Partial | 6/6 examples, 9/10 errors. GAP-25 |
| 8.12.1 | `get_code/1,2` | Partial | 6/6 examples, 10/11 errors. GAP-25 |
| 8.12.2 | `peek_char/1,2` | Partial | 7/7 examples, 6/7 errors. GAP-25 |
| 8.12.2 | `peek_code/1,2` | Partial | 6/6 examples, 5/6 errors. GAP-25 |
| 8.12.3 | `put_char/1,2` | Done | 4/4 examples, 9/9 errors |
| 8.12.3 | `put_code/1,2` | Done | 4/4 examples, 9/9 errors |
| 8.12.3 | `nl/0,1` | Done | 4/4 examples, 3/3 errors |
| 8.13.1 | `get_byte/1,2` | Done | 5/5 examples, 12/12 errors |
| 8.13.2 | `peek_byte/1,2` | Done | 5/5 examples, 8/8 errors |
| Cor.1 8.13.3 | `put_byte/1,2` | Done | 4/4 examples, 10/10 errors |
| 8.14.1 | `read_term/2,3` | Done | 1/1 example, 9/9 errors, 4/4 Cor.3 option errors |
| Cor.1 8.14.1 | `read/1,2` | Done | 5/5 examples, 10/10 errors |
| 8.14.2 | `write_term/2,3` | Partial | 4/4 examples, 4/4 Cor.3 examples, 12/12 errors, 4/4 Cor.3 option errors. GAP-13 |
| 8.14.2.5 | `write/1,2` | Done | 3/3 errors |
| 8.14.2.5 | `writeq/1,2` | Done | 2/2 examples, 2/2 errors |
| 8.14.2.5 | `write_canonical/1,2` | Done | 1/1 example, 2/2 errors |
| Cor.2 8.14.3 | `op/3` | Done | 17/17 examples, 14/14 errors |
| Cor.1 8.14.4 | `current_op/3` | Partial | 1/1 example, 6/6 errors. GAP-01 |
| 8.14.5 | `char_conversion/2` | Done | 4/4 examples, 6/6 errors |
| 8.14.6 | `current_char_conversion/2` | Done | 1/1 example, 4/4 errors |
| 8.15.1 | `(\+)/1` | Done | 7/7 examples |
| 8.15.2 | `once/1` | Done | 4/4 examples, 2/2 errors |
| 8.15.3 | `repeat/0` | Done | 2/2 examples |
| Cor.2 8.15.4 | `call/2..8` | Done | 8/8 examples, 11/11 errors |
| Cor.2 8.15.5 | `false/0` | Done | 1/1 example |
| 8.16.1 | `atom_length/2` | Done | 7/7 examples, 4/4 errors |
| 8.16.2 | `atom_concat/3` | Done | 5/5 examples, 7/7 errors |
| 8.16.3 | `sub_atom/5` | Done | 7/7 examples, 10/10 errors |
| Cor.1, Cor.2 8.16.4 | `atom_chars/2` | Done | 8/8 examples, 15/15 errors |
| Cor.1, Cor.2 8.16.5 | `atom_codes/2` | Done | 8/8 examples, 12/12 errors |
| 8.16.6 | `char_code/2` | Partial | 7/7 examples, 5/9 errors. GAP-29 |
| Cor.2 8.16.7 | `number_chars/2` | Partial | 12/12 examples, 17/17 errors, 71 extra cases (issues #2, #7, #10, #12). GAP-08 |
| Cor.2 8.16.8 | `number_codes/2` | Partial | 10/10 examples, 11/11 errors. GAP-08 |
| 8.17.1 | `set_prolog_flag/2` | Partial | 5/5 examples, 14/14 errors. GAP-31 |
| 8.17.2 | `current_prolog_flag/2` | Done | 3/3 examples, 2/2 errors |
| 8.17.3 | `halt/0` | Done | Exit status 0 |
| 8.17.4 | `halt/1` | Partial | 2/2 examples, 2/2 errors. GAP-30 |

### Part 1 clause 9: evaluable functors

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 9.1.1, Cor.2 7.9.2 i | The operation is chosen by operand types, and a wrong type gives `type_error(Type, Culprit)` | Done | 14/14 |
| 9.1.2 | Exceptional values surface as `evaluation_error(E)` | Partial | GAP-32 to GAP-37, GAP-68 |
| 9.1.3 | Integer operations and axioms | Done | `int_overflow` cannot arise |
| 9.1.3.1 | Integer division rounding | Done | `toward_zero`, documented |
| 9.1.4 | Float operations; division by zero is `zero_divisor` | Partial | GAP-32 |
| 9.1.4.1 | Float rounding function | Partial | GAP-38, GAP-64 |
| 9.1.4.2 | Float result function and underflow | Partial | GAP-64 |
| 9.1.4.3 | Approximate addition | Partial | GAP-64 |
| 9.1.5 | Mixed-mode operations; conversion overflow | Partial | GAP-37, GAP-38 |
| 9.1.6 | Type conversion functions | Partial | GAP-38, GAP-39 |
| 9.1.6.1 | Rounding axioms | Partial | GAP-39 |
| 9.1.7 | Examples of simple arithmetic | Done | 55/55 applicable examples, including the Cor.1 corrections. Examples 56–60 need `max_integer` |
| 9.1.1 | `(+)/2` | Done | 5/5 examples |
| 9.1.1 | `(-)/2` | Done | 5/5 examples |
| 9.1.1 | `(*)/2` | Partial | 5/5 examples. GAP-37 |
| 9.1.1 | `(//)/2` | Done | |
| 9.1.1, 9.1.5 | `(/)/2`: two integers give a float | Partial | `1/1` gives `1.0`, as required (issue #13). GAP-32, GAP-37 |
| 9.1.1 | `rem/2` | Done | 7/7 |
| 9.1.1 | `mod/2` | Done | 7/7 examples |
| 9.1.1 | `(-)/1` | Done | |
| Cor.2 9.1.1 | `(+)/1` | Done | 5/5 |
| Cor.2 9.1.1 | `(div)/2` | Done | 9/9 |
| 9.1.1 | `abs/1` | Done | 5/5 examples |
| 9.1.1 | `sign/1` | Done | 8/8 |
| 9.1.1 | `float_integer_part/1` | Done | 4/4 |
| 9.1.1 | `float_fractional_part/1` | Done | 4/4 |
| 9.1.1 | `float/1` | Partial | 5/5 examples. GAP-38 |
| 9.1.1 | `floor/1` | Done | |
| 9.1.1 | `truncate/1` | Done | |
| 9.1.1 | `round/1` | Partial | 4/4 examples. GAP-39 |
| 9.1.1 | `ceiling/1` | Done | |
| 9.2 | Result types follow the templates | Done | Results compared with `==` |
| 9.3.1 | `(**)/2`: the result is always a float | Partial | 7/7 examples, 4/6 errors. GAP-33, GAP-36, GAP-68 |
| 9.3.2 | `sin/1` | Partial | 5/5 examples. GAP-68 |
| 9.3.3 | `cos/1` | Partial | 5/5 examples. GAP-68 |
| 9.3.4 | `atan/1` | Done | 5/5 examples |
| 9.3.5 | `exp/1` | Partial | 5/5 examples, 3/4 errors. GAP-36 |
| 9.3.6 | `log/1` | Partial | 4/6 examples. GAP-34, GAP-68 |
| 9.3.7 | `sqrt/1` | Partial | 6/6 examples. GAP-68 |
| Cor.2 9.3.8 | `max/2` | Done | 4/4 examples. With mixed types, an integer is returned on equality (implementation dependent) |
| Cor.2 9.3.9 | `min/2` | Done | 4/4 examples |
| Cor.2, Cor.3 9.3.10 | `(^)/2` | Partial | 10/10 examples as amended by Cor.3, 5/7 errors. GAP-33, GAP-36, GAP-68 |
| Cor.2 9.3.11 | `asin/1` | Done | 3/3 examples |
| Cor.2 9.3.12 | `acos/1` | Done | 3/3 examples |
| Cor.2 9.3.13 | `atan2/2` | Done | 3/3 examples |
| Cor.2 9.3.14 | `tan/1` | Partial | 1/1 example. GAP-68 |
| Cor.2 9.3.15 | `pi/0` | Done | 1/1 example |
| 9.4 | Bitwise functors on integers; negative operands | Done | Two's complement, documented |
| 9.4.1 | `(>>)/2` | Partial | 5/5 examples. GAP-64 |
| 9.4.2 | `(<<)/2` | Partial | 5/5 examples. GAP-64 |
| 9.4.3 | `(/\)/2` | Done | 6/6 examples |
| 9.4.4 | `(\/)/2` | Done | 6/6 examples |
| 9.4.5 | `(\)/1` | Done | 5/5 examples |
| Cor.2 9.4.6 | `xor/2` | Done | 3/3 examples. Its operator definition is GAP-01 |

### Corrigenda 1, 2, and 3

Editorial items have no observable effect and are counted as Done. They are Cor.1 3.106, 3.108,
3.125, 3.148, 7.2.5 wording, 7.8.5.4, table 35, 7.12.2 i, 8.9.4.1, and 9.1.4.1; Cor.2 notes
to 6.3.4.3, 8.1.3 note 5, and the 8.4 paragraph move; and Cor.3 8.1.2.1 option types, 8.9.2.1 e,
8.14.1.1 k, 9.3.1.3 c, and the 7.12.2 e comma.

| Ref | Change | Status | Evidence / notes |
|---|---|---|---|
| Cor.1 editorial items | Wording, typography, and notes | Done | No behavior to check |
| Cor.1 4.1.3.5 | Square root is defined for zero | Done | `sqrt(0)` gives `0.0` |
| Cor.1 6.3.7 | Double-quoted atoms and operator priority | Partial | GAP-05 |
| Cor.1 7.8.8.4 | Corrected if-then-else example | Done | |
| Cor.1 7.9.2 | `type_error(integer, C)` and `type_error(float, C)` | Done | Later replaced by Cor.2 7.9.2 i |
| Cor.1 8.8.1.1 | `clause/2` unifies with `clause(H, B)` | Done | |
| Cor.1 8.10.3.4 | Changed `setof/3` example 20 | Done | Superseded by Cor.3 |
| Cor.1 8.13.3.4 | Corrected `put_byte` examples | Done | |
| Cor.1 8.14.1.4 | Read examples; stream state after an incomplete term is undefined | Done | |
| Cor.1 8.14.4.1 | `current_op/3` unifies all three arguments | Done | |
| Cor.1 8.16.4.1, 8.16.5.1 | `atom_chars/2` and `atom_codes/2` with a partial list | Done | |
| Cor.1 9.1.7 | Corrected examples 21, 23, 24, 48 | Done | |
| Cor.1 9.3.5.4, 9.3.6.4 | Corrected `exp` and `log` examples | Done | |
| Cor.1 9.4.1.4–9.4.4.4 | Bitwise examples give `type_error(evaluable, foo/0)` | Done | |
| Cor.2 editorial items | Notes and moved paragraphs | Done | No behavior to check |
| Cor.2 6.3.4.3 | The bar as an operator, and restrictions on `[]`, `{}`, and <code>'&#124;'</code> | Partial | GAP-02 |
| Cor.2 6.3.4.4 | `div` and prefix `+` added to table 7 | Done | Extra operators: GAP-01 |
| Cor.2 6.4, 6.4.8, 6.5.3 | The bar token and bar char | Done | |
| Cor.2 7.1.1.5 | Witness variable list | Done | |
| Cor.2 7.1.6.9 | List prefix | Done | |
| Cor.2 7.8.3.4 | `call/1` example 6 | Done | |
| Cor.2 7.8.9 | `catch/3` template | Done | |
| Cor.2 7.9.1 | Error when no operation applies | Done | |
| Cor.2 7.9.2 i | Type error names the expected type | Done | |
| Cor.2 7.12.2 b | `pair` added and `variable` removed as types | Done | |
| Cor.2 7.12.2 c | `order` domain | Done | |
| Cor.2 7.12.2 k | `uninstantiation_error` | Done | |
| Cor.2 8.2.4 | `subsumes_term/2` | Done | |
| Cor.2 8.3.9–8.3.11 | `callable/1`, `ground/1`, `acyclic_term/1` | Done | |
| Cor.2 8.4.2–8.4.4 | `compare/3`, `sort/2`, `keysort/2` | Done | |
| Cor.2 8.5.5 | `term_variables/2` | Done | |
| Cor.2 8.9.3.3 | `retract/1` on a static procedure is a `modify` error | Done | |
| Cor.2 8.9.5 | `retractall/1` | Partial | GAP-16, GAP-19 |
| Cor.2 8.11.5.3 f | `open/4` with a bound stream argument | Done | |
| Cor.2 8.14.3 | `op/3` errors l and m, and seven new examples | Done | |
| Cor.2 8.15.4 | `call/2..8` | Done | |
| Cor.2 8.15.5 | `false/0` | Done | |
| Cor.2 8.16.4.3 | `atom_chars/2` errors | Done | |
| Cor.2 8.16.5.3 | `atom_codes/2` errors | Done | |
| Cor.2 8.16.7.3 | `number_chars/2` errors | Done | |
| Cor.2 8.16.8.3 | `number_codes/2` errors | Done | |
| Cor.2 9.1.1, 9.1.3, 9.1.4 | `(div)/2` and `(+)/1` | Done | |
| Cor.2 9.3.8 | `max/2` | Done | |
| Cor.2 9.3.9 | `min/2` | Done | |
| Cor.2 9.3.10 | `(^)/2` | Partial | GAP-33, GAP-36, GAP-68 |
| Cor.2 9.3.11 | `asin/1` | Done | |
| Cor.2 9.3.12 | `acos/1` | Done | |
| Cor.2 9.3.13 | `atan2/2` | Done | |
| Cor.2 9.3.14 | `tan/1` | Partial | GAP-68 |
| Cor.2 9.3.15 | `pi/0` | Done | |
| Cor.2 9.4.6 | `xor/2` | Done | |
| Cor.3 editorial items | Types, references, and wording | Done | No behavior to check |
| Cor.3 5.5.12 | Extra options; errors for invalid options | Partial | GAP-13 |
| Cor.3 6.2.1 | Prolog text may end with layout text | Done | |
| Cor.3 7.1.6.3 a | Iterated-goal term only for `^(_, G)` | Partial | GAP-15 |
| Cor.3 7.8.3.4 | `call/1` examples use term-to-body conversion | Done | |
| Cor.3 7.10.3 | Left-to-right order for `variables/1`, `variable_names/1`, `singletons/1` | Done | |
| Cor.3 3.206, 7.10.4 | Write option `variable_names/1`; the leftmost element applies | Done | 10/10, including option errors |
| Cor.3 7.10.5 a1, a2 | Writing a variable named in `variable_names/1` unquoted | Done | |
| Cor.3 7.10.5 e1–e3 | `'$VAR'(N)` uses variable notation with `numbervars(true)`, independently of `ignore_ops`; list and curly notation require `ignore_ops(false)` | Done | `writer_numbervars_ignore_ops` in [recheck evidence](#recheck-evidence) |
| Cor.3 7.10.5 f, h | Brackets for operator atoms, after prefix `-`, and around infix arguments of prefix `-`; `','` and <code>'&#124;'</code> written as punctuation | Done | |
| Cor.3 7.12.2 | `float` type added; `character_code_list` domain removed | Done | |
| Cor.3 8.1.3 note 7 | Errors for options lists | Done | |
| Cor.3 8.11.5.3, 8.11.6.3, 8.14.1.3, 8.14.2.3 | Instantiation and domain errors for option elements | Done | 23/23 across the four predicates |
| Cor.3 8.5.1.4 | `functor/3` beyond `max_arity` | Done | `representation_error(max_arity)` |
| Cor.3 8.10.3.4 | `setof/3` example 20 restores the 1995 list and must succeed with the existential qualifier retained | Partial | GAP-15. The previous probe omitted the qualifier and tested a different goal. `setof_existential` in [recheck evidence](#recheck-evidence) reproduces the strict-mode error; Modern succeeds |
| Cor.3 8.11.4.1 b | `set_output/1` succeeds | Done | |
| Cor.3 8.14.2.4 | New `write_canonical/1` and `write_term/2` examples | Done | 4/4 |
| Cor.3 8.17.1.4 | `set_prolog_flag(date, 'July 1988')` gives `domain_error(prolog_flag, date)` | Done | |
| Cor.3 9.3.10.3 e, 9.3.10.4 | `(^)/2` with integers and a negative exponent gives `type_error(float, VX)` unless the base is 1, 0, or −1 | Done | 8/8 cases outside the zero-base error, including `2^(-1)`, `2.0^(-1)`, `(-1)^(-3)`. The ninth probe, `0^(-1)`, deviates under error d (GAP-33). `power_negative_integer` in [recheck evidence](#recheck-evidence) |

### Implementation defined documentation

Part 1 5.4 requires the documentation to complete the definition of every implementation defined
feature. A row is Done when [the characteristics page](iso-processor-characteristics.md) states
the choice and the probes confirm it.

| Ref | Feature | Status | Evidence / notes |
|---|---|---|---|
| 6.5 | Processor character set | Done | UTF-16 code units in `StrictIso` |
| 6.5 | Classes of extended characters | Partial | GAP-07 |
| 6.5.4 | Collating integers of control escapes; extra layout characters | Partial | GAP-64 |
| 6.6, 7.2.4 | Collating sequence | Done | |
| 6.4.4, 6.4.5 | Range of integer and float literals | Done | |
| 5.5.2, 6.3.4.4 | Initial operator table | Partial | The page lists only standard operators. GAP-01 |
| 7.1.2.2 | Character code to byte mapping | Partial | UTF-8 text files, raw binary; byte-order marks are detected, which is undocumented. GAP-25 |
| 7.1.3 | Float parameters | Done | binary64 |
| 7.4.2.4, 7.4.2.5, 7.4.2.9 | Effect of directives on other text and at run time | Done | |
| 7.4.2.6 | Order of initialization goals | Done | |
| 7.4.2.7, 7.4.2.8 | Text-unit designation and the position of `ensure_loaded/1` | Done | |
| 7.4.3 | How text is prepared | Done | |
| 7.4.3 | Clauses added or removed by directives during preparation | Partial | GAP-64 |
| 7.7.1, 7.7.3 | Form of answers; goal delivery | Done | |
| 7.10.1 | Source/sink representation | Done | |
| 7.10.2.6 | Record-based streams, line ends, final newline, control characters | Done | The page does not say explicitly that line ends are unchanged |
| 7.10.2.7 | Trailing zero bytes on binary input | Done | |
| 7.10.2.8 | Which streams can be repositioned | Partial | Disk streams are documented as repositionable, but a FIFO reports `reposition(true)` and cannot be repositioned. GAP-27 |
| 7.10.2.9 | Position terms at and past end of stream | Partial | GAP-64 |
| 7.10.2.11 | Effect of `reposition(false)`; default `eof_action` | Done | Table 40: GAP-22 |
| 7.10.2.13 | The `file_name/1` term | Done | The standard streams report their alias as `file_name/1`: GAP-22 |
| 7.11 | Flag values | Done | |
| 7.11.2.2 | Effect of `debug` = `on` | Done | |
| 7.12.1 | Error context term | Done | |
| 8.12.1 | Undecodable input | Partial | GAP-25 |
| 8.17.3 | Other effects of `halt/0` | Done | Exit status 0 |
| 9.4.3–9.4.5 | Bitwise values for negative operands | Done | Two's complement |
| 8.17.4 | `halt/1` exit status | Partial | GAP-30 |
| 9.1.4.1–9.1.4.3 | Float rounding, result function, approximate addition | Partial | GAP-64 |
| 9.4.1, 9.4.2 | Negative shift counts | Partial | GAP-64 |
| 5.5.11 | Reserved atoms | Partial | GAP-14 |
| Clause 3 terms | The page uses the standard's terms | Partial | GAP-65 |

### Part 2: modules

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 4.1 a–c, 4.2, 4.3 | Prepare and execute conforming module text and goals | Partial | GAP-40 to GAP-47 |
| 4.1 d, 4.5 | Document implementation defined and implementation specific features | Partial | GAP-51, GAP-60, GAP-64 |
| 4.1 e | The strictly conforming mode rejects implementation specific features | Partial | `module/2`, `use_module/1`, and `meta_predicate/1` are rejected. GAP-41 |
| 4.4.1 | Text without modules is the body of `user` | Partial | GAP-42 |
| 4.4.2 | The module `user` | Partial | GAP-41, GAP-42 |
| 4.5.1 | Dynamic modules | Optional | Not provided |
| 4.5.2 | Inaccessible procedures | Optional | Not provided: every procedure is reachable by qualification |
| 5.1 | Module text | Done | |
| 5.2.1 | `:` is `op(600, xfy)`; per-module operator tables | Partial | GAP-51 |
| 6.1.1.1–6.1.1.3 | Qualified terms and the qualifying module | Done | |
| 6.1.1.4 | Metapredicate mode indicators | Done | |
| 6.2 | Interfaces and bodies | Done | |
| 6.2 | Qualified `call/1`, `catch/3`, and built-in metapredicates set the context | Partial | GAP-44, GAP-45 |
| 6.2.1 | Module `user` and its interface | Done | |
| 6.2.2 | Accessibility and unique visible procedures | Partial | GAP-43, GAP-49 |
| 6.2.3 | Module interface | Done | |
| 6.2.4.1 | `module/1` | Done | |
| 6.2.4.2 | `export/1` | Partial | GAP-40, GAP-58 |
| 6.2.4.3 | `reexport/2` | Done | |
| 6.2.4.4 | `reexport/1` | Done | |
| 6.2.4.5 | `metapredicate/1` | Done | |
| 6.2.4.6 | Interface `op/3` | Done | |
| 6.2.4.7 | Interface `char_conversion/2` | Done | |
| 6.2.4.8 | Interface `set_prolog_flag/2` | Done | |
| 6.2.4.9 | `end_module/1` | Done | |
| 6.2.5 | Module bodies | Done | |
| 6.2.5.1 | `body/1` | Done | |
| 6.2.5.2 | `import/2` | Partial | GAP-43 |
| 6.2.5.3 | `import/1` | Partial | GAP-43 |
| 6.2.5.4 | `end_body/1` and reader state across bodies | Done | Accumulation during preparation conforms; the run-time effect is GAP-51 |
| 6.2.6 | Clauses in bodies | Partial | GAP-40 |
| 6.2.6.1 | Examples | Partial | 4/4 once the non-ISO names are renamed. GAP-40 |
| 6.3 | The complete database | Partial | GAP-42 |
| 6.3.1, 6.3.2 | Visible database | Partial | GAP-41 |
| 6.4 | `:` sets the calling context | Partial | GAP-44, GAP-45 |
| 6.4.1 | Built-in metapredicates | Partial | GAP-45, GAP-48, GAP-57 |
| 6.4.2 | Context-sensitive built-ins | Done | 17/17 |
| 6.4.3 | Meta-arguments are qualified | Partial | GAP-46 |
| 6.4.4.1 | Example: trace metapredicate | Partial | GAP-46 |
| 6.4.4.2 | Example for the flag set to `false` | Optional | The flag is fixed to `true` |
| 6.5.1 | Converting a term to a clause head | Done | |
| 6.5.2 | Converting a term to a clause body | Partial | GAP-47, GAP-50, GAP-61 |
| 6.5.2.4 | Further conversions | Optional | Permits additional conversions only; none is observable through `clause/2` |
| 6.5.3 | Converting a clause body back to a term | Partial | GAP-61 |
| 6.6.1–6.6.3 | Execution with a context module; import search | Partial | GAP-43 |
| 6.6.2 | Goal delivery and the initial context | Done | |
| 6.6.4 | Clause selection and unknown procedures | Partial | GAP-60 |
| 6.6.5, 6.6.6 | Backtracking; procedures run in their defining module | Partial | GAP-43 |
| 6.6.7 | Built-ins reach the calling context | Partial | GAP-45, GAP-48 |
| 6.7.1 | `call/1` | Partial | GAP-44, GAP-59 |
| 6.7.2 | `catch/3` | Partial | GAP-45 |
| 6.7.3 | `throw/1` | Done | |
| 6.8 | Predicate properties | Partial | GAP-55, GAP-56, GAP-57 |
| 6.9.1 | `colon_sets_calling_context` | Partial | `true`, documented. GAP-31 |
| 6.10.1 | Additional error types, domains, and permissions | Done | |
| 7.2.1 | `current_module/1` | Done | 2/2 examples |
| 7.2.2 | `predicate_property/2` | Partial | 6/6 examples, 5/5 errors. GAP-55, GAP-56, GAP-57 |
| 7.3.1 | `clause/2` | Partial | 9/9 examples in a module context. GAP-48, GAP-49, GAP-52, GAP-53 |
| 7.3.2 | `current_predicate/1` | Partial | 4/4 examples. GAP-20, GAP-42 |
| 7.4.1 | `asserta/1` | Partial | 6/7 examples. GAP-47, GAP-49, GAP-52, GAP-53, GAP-54 |
| 7.4.2 | `assertz/1` | Partial | GAP-49, GAP-54 |
| 7.4.3 | `retract/1` | Partial | 4/5 examples. GAP-49, GAP-52, GAP-53, GAP-54 |
| 7.4.4 | `abolish/1` | Partial | 3/3 examples, 11/12 errors. GAP-49, GAP-53, GAP-54 |

### Part 3: grammar rules

| Ref | Requirement | Status | Evidence / notes |
|---|---|---|---|
| 5.1 | Part 1 conformance applies to grammar rules | Partial | GAP-64 |
| 5.5.13 | Extra grammar controls are non-terminals in the strictly conforming mode | Partial | `*->` gives `existence_error(procedure, (*->)/4)`. GAP-71 |
| 6.1.2, 6.2.1.1 | Prolog text may contain grammar rules | Done | |
| 6.2.1.2, 6.2.1.3 | Grammar rule terms | Done | `--> a.` is a syntax error |
| 6.3.4.3 | <code>op(1105, xfy, '&#124;')</code> is predefined | Done | |
| 7.4.2 | Declarations accept `Name//N` | Partial | GAP-62 |
| 7.4.4 | Restrictions on grammar rule heads | Partial | GAP-69 |
| 7.5.1 | Rules for `NT//N` combined with clauses for `NT/(N+2)` | Done | Source order, documented |
| 7.13.1 | Grammar rule format; invalid semicontexts | Partial | Invalid semicontexts are rejected. GAP-64 |
| 7.13.2 | Terminals, non-terminals, and pushback | Done | 7/7 |
| 7.13.3 | Non-terminal indicators | Partial | GAP-62 |
| 7.13.4 | Undefined non-terminals | Done | |
| 7.13.5 | Logical expansion | Done | |
| 7.14.1 | `[]//0` | Done | |
| 7.14.2 | `'.'//2` | Done | |
| 7.14.3 | `(',')//2` | Done | |
| 7.14.4 | `(;)//2` | Done | |
| 7.14.5 | if-then-else | Done | 5/5 examples |
| 7.14.6 | <code>'&#124;'//2</code> | Partial | GAP-64 |
| 7.14.7 | `{}//1` | Done | |
| 7.14.8 | `call//1` | Done | 5/5 |
| 7.14.9 | `phrase//1` | Done | |
| 7.14.10 | `!//0` | Done | |
| 7.14.11 | `(\+)//1` | Optional | Provided. Documentation: GAP-64 |
| 7.14.12 | `(->)//2` outside if-then-else | Optional | Provided. Documentation: GAP-64 |
| 8.18.1.1 | `phrase/2,3` | Done | |
| 8.18.1.3 | `phrase` errors a–h | Done | |
| 8.18.1.4 | Examples | Done | 6/6 |
| 8.18.1.5 | `phrase/2` is `phrase(G, S0, [])` | Partial | GAP-63 |

## Recheck evidence

The repository probe
[`iso_tracker_recheck.pl`](https://github.com/kidoz/dotprolog/blob/main/tests/conformance/iso_tracker_recheck.pl)
contains original cases for the source corrections above. Run it from the repository root:

```sh
dotnet run --project src/DotProlog.Tool -- run --mode strict-iso tests/conformance/iso_tracker_recheck.pl
dotnet run --project src/DotProlog.Tool -- run --mode modern tests/conformance/iso_tracker_recheck.pl
```

Each `result(Id, Required, Observed)` line records the expected formal error or complete answer
list separately from the observed outcome. The writer case prints its expected and actual
characters. This is an audit report: process success is not a conformance pass, and known
deviations are not asserted as correct behavior. It is separate from the passing
`strict_iso_review.pl` regression corpus and does not change that corpus's count.

The following observations were reproduced at `797eac5` on 2026-09-28:

| Case ID | Source / required outcome | Observed in StrictIso |
|---|---|---|
| `setof_existential` | 8.10.3.4 with Cor.3: succeeds; the existential qualifier must be preserved | Permission error on `(^)/2`; Modern succeeds. GAP-15 |
| `setof_unquantified` | 8.10.3.4: the otherwise similar unquantified goal fails | Fails; this cannot establish that the quantified example conforms |
| `writer_numbervars_ignore_ops` | Cor.3 7.10.5 e1: `numbervars(true)` remains effective with `ignore_ops(true)` | Prints `A`, as required |
| `power_negative_integer` | Cor.3 9.3.10.3 e: `type_error(float,2)` | Correct; GAP-35 remains closed |
| `mixed_division` | 9.1.5: `evaluation_error(float_overflow)` | Returns `0.0`. GAP-37 |
| `mixed_multiplication` | 9.1.5: `evaluation_error(float_overflow)` | Raises `evaluation_error(undefined)`. GAP-37 |
| `mixed_comparison` | 8.7.1: `evaluation_error(float_overflow)` | Fails. GAP-37 |
| `atan_large_integer` | 9.3.4: principal arc tangent; the probe uses a coarse interval around π/2 | Succeeds within that interval; not evidence of a required conversion-overflow error |
| `exp_underflow` | 9.3.5.3 d: `evaluation_error(underflow)` | Returns `0.0`. GAP-36 |

The reader total remains a historical aggregate from the original review report: 337 automatic
matches + 7 manual confirmations = 344, and 17 automatic deviations + 4 manual deviations = 21.
The full 365-case reader corpus is not retained in the repository; this recheck does not claim to
reproduce that aggregate or coverage of every case through consulted source.

## Keeping this page current

When a gap is fixed:

1. Add a regression case to `tests/conformance/strict_iso_review.pl`, or to the focused test suite
   for the component.
2. Set the gap's status to `Fixed in <version>`, and update the checklist rows that cite it.
3. Update the summary counts.
4. Correct the ledger or characteristics page that the gap contradicts.

New conformance behavior should get a row here before it is claimed on the ledgers.
