# Modern mode tracker

This page records how DotProlog's default language mode, `Modern`, relates to ISO/IEC 13211,
requirement by requirement. It is the companion of the
[ISO conformance tracker](iso-conformance-tracker.md), which covers the strictly conforming mode,
`StrictIso`. Modern is not meant to be strictly conforming: it adds features and keeps some
SWI-Prolog readings. This page classifies the reviewed differences from ISO, and keeps the extensions
that the standard permits apart from the changes it does not permit.

The original results come from DotProlog at commit `797eac5`. The focused recheck on 2026-09-29
used `41fd728`, whose runtime behavior is unchanged; the intervening commits changed documentation,
a code comment, and a test string. Requirements are paraphrased. The ISO publications remain
authoritative, and clause numbers refer to them.

## Scope and method

The documents are the same as for the ISO tracker: ISO/IEC 13211-1:1995 with Technical Corrigenda
1, 2, and 3, ISO/IEC 13211-2:2000 (modules), and ISO/IEC TS 13211-3:2025 (grammar rules).

The original local review reports record the following historical aggregates. Their complete
harnesses, source snapshots, and per-case verdicts are not retained in the repository, so these
figures are not independently reproducible from this page:

- 443 probe files were run in both modes, 886 runs in all. 326 had identical output, diagnostics,
  and exit status, and 117 differed. These are file comparisons, not counts of passing conformance
  cases; they include support files, load-only cases, and expected failures.
- The local snapshot contained 366 entries of Ulrich Neumerkel's
  [ISO conformity testing table](https://www.complang.tuwien.ac.at/ulrich/iso-prolog/conformity_testing)
  and was run in `Modern`. The report classified 335 as matching its ISO column, and the other
  31 as 8 modifications (CHG-01), 22 extensions (EXT-01 to EXT-05), and the float zero gap
  (GAP-67). The live link does not pin that snapshot or replace the normative publications.
- The Modern surface was enumerated from the source: native built-in predicates, the bundled
  library, the operator table, flags, evaluable functors, syntax, directives, and options. The
  items were compared with the documentation and local probes. This inventory does not establish
  complete behavioral coverage of every listed predicate or requirement.

The separate 365-case reader report recorded 303 automatic matches to the ISO baseline, 51
differences, and 11 manual cases in Modern, versus 337/17/11 in StrictIso. These raw differences
include permitted extensions and deliberate modifications as well as gaps. They are not a Modern
conformance-failure total, and the StrictIso tracker's 344/365 total must not be reused for Modern.

The [recheck evidence](#recheck-evidence) provides repository probes, stable case IDs, commands,
and expected and observed outcomes for the corrected findings. Those focused probes do not
reconstruct the historical corpora or validate their aggregate results.

Only the formal part of `error(Formal, Context)` is compared. Requirement rows whose Modern status
and cause match `StrictIso` are not repeated here; see
[requirements that differ from StrictIso](#requirements-that-differ-from-strictiso).

## Classes and status values

Part 1 5.5 lets a conforming processor add implementation specific features only for constructs
the standard leaves undefined, plus the additions that 5.5.1–5.5.12 name. Every Modern difference
from ISO is one of these classes:

| Class | Meaning |
|---|---|
| Conforms | Modern gives the result ISO requires |
| Extension | A permitted addition: a construct ISO leaves undefined, extra syntax that leaves the reading of conforming text unchanged (5.5.1), extra predefined operators (5.5.2), an extra type that meets 5.5.4, extra directives, side effects, control constructs, flags, built-in predicates and their error terms, evaluable functors, reserved atoms, or options (5.5.5–5.5.12). 5.4 requires each one to be documented |
| Modification | Modern changes the result ISO defines for conforming text or a conforming goal, and no 5.5 clause permits it. A claim of conformance for such behavior has to rest on `StrictIso` |
| Gap | An unintended deviation |

Requirement rows use these status values:

| Status | Meaning |
|---|---|
| Done | No deviation found in the reviewed Modern cases; subject to the coverage limits above |
| Extension | Conforms, and the extensions in this area are permitted and documented |
| Modified | A modification applies (CHG-NN) |
| Partial | A gap applies, including an undocumented extension |
| Missing, Unverified, Optional | As in the ISO tracker |
| N/A | The requirement concerns the strictly conforming mode only |

When several apply, a row shows the first of Modified, Partial, and Extension. Gap severities
follow the ISO tracker.

## Summary

| Area | Requirements | Done | Extension | Modified | Partial | Unverified | Optional | N/A |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Part 1 clause 5, compliance | 21 | 2 | 3 | 5 | 9 | 1 | 0 | 1 |
| Part 1 clause 6, syntax | 46 | 30 | 5 | 3 | 8 | 0 | 0 | 0 |
| Part 1 clause 7, concepts and semantics | 109 | 82 | 0 | 3 | 23 | 1 | 0 | 0 |
| Part 1 clause 8, built-in predicates | 87 | 59 | 1 | 3 | 24 | 0 | 0 | 0 |
| Part 1 clause 9, evaluable functors | 54 | 32 | 0 | 0 | 22 | 0 | 0 | 0 |
| Corrigenda 1, 2, and 3 | 70 | 64 | 0 | 0 | 6 | 0 | 0 | 0 |
| Implementation defined documentation | 32 | 18 | 0 | 0 | 14 | 0 | 0 | 0 |
| Part 2, modules | 63 | 22 | 0 | 4 | 32 | 0 | 4 | 1 |
| Part 3, grammar rules | 29 | 19 | 0 | 0 | 8 | 0 | 2 | 0 |
| **Total** | **511** | **328** | **9** | **18** | **146** | **2** | **6** | **2** |

- **Modifications:** 7 (CHG-01 to CHG-07). Five are deliberate and documented, CHG-02 is a
  deliberate feature whose conflict with ISO text is undocumented, and CHG-04 is contested.
- **ISO tracker gaps:** 56 of the 71 also occur in Modern. GAP-10 and GAP-15 do not occur, and
  GAP-35 is closed. Seven become permitted extensions, GAP-41 is a modification (CHG-07), and
  GAP-03, GAP-09, GAP-40, and GAP-69 take a different form in Modern.
- **Modern gaps:** 6 (MGAP-01 to MGAP-06): 4 Medium and 2 Low.
- **Extensions:** 20 groups (EXT-01 to EXT-20), including 163 extension predicates, 15 extra
  predefined operators, 12 evaluable functors, one extra type, and one extra flag.

## Modifications

A modification is resolved by making Modern conform, or it stays and is documented as a
modification with the `StrictIso` behavior named. It is not an extension (issue #11).

| ID | Ref | Modification | ISO result | Modern result | Status |
|---|---|---|---|---|---|
| CHG-01 | 6.3.1.2, 5.5.1 | A `-` followed by a numeric literal is a negative number only when it is unquoted and directly before the digits | `X = - 1, integer(X)` succeeds; `'-'1` and `- /**/ 1` are `-1`; `- 1^2` is `(-1)^2` | `X = -(1)` and `integer(X)` fails; `- 1^2` is `-(1^2)`; `2 ** - 1` and, after `op(0, fy, -)`, `- 1` are syntax errors. `number_chars/2` keeps the ISO reading, so `number_chars(N, [-, ' ', '1'])` gives `-1` while the reader gives `-(1)` (issue #12) | Documented as an SWI reading; the syntax errors and the `number_chars/2` disagreement are not |
| CHG-02 | 6.4, 5.5.1 | `NrM` is lexed as one rational token | After `:- op(200, xf, r3).`, `X = 1r3` reads as `r3(1)` | `X` is the rational `1r3`. With `op(200, xf, r0)`, `X = 1r0` is a load error. The related conversion `number_codes(N, [49,114,51])` gives `1r3` where StrictIso gives `syntax_error(illegal_number)`. With Modern's default `double_quotes=chars`, `number_codes(N, "1r3")` instead raises `type_error(integer,'1')` | Rational literals are documented; the conflict with ISO tokenization is not. The postfix-operator example establishes the modification; accepting otherwise invalid numeric text alone does not |
| CHG-03 | 5.5.4 b | Rationals are an extra type ordered by value among the integers | The order between an extra type and a standard type depends only on the two types | `msort([2, 1r2, 0, 2.0, 1, 3r2], L)` gives `[2.0, 0, 1r2, 1, 3r2, 2]` | Documented in the SWI ledger |
| CHG-04 | 7.11.2.5, 8.17.1.3 | `double_quotes` accepts the extra value `string` | `set_prolog_flag(double_quotes, string)` raises `domain_error(flag_value, double_quotes+string)` | Succeeds, and `"abc"` then reads as a string | Contested; documented, but described as an extension |
| CHG-05 | 7.11.2.1 | `char_conversion` starts `off` | The flag starts `on`, so `:- char_conversion(x, y). p(x).` defines `p(y)` | The flag starts `off`, and the text defines `p(x)`. The 8.14.5 example and Part 2 interface conversions have no effect until the flag is set. 5.5.3 permits a different initial mapping, not a different flag value | Documented |
| CHG-06 | 8.17.2.3 | `current_prolog_flag/2` fails for an atom that is not a flag | `current_prolog_flag(foo, V)` raises `domain_error(prolog_flag, foo)` | Fails. `set_prolog_flag(foo, x)` raises the ISO error | Documented |
| CHG-07 | Part 2 6.3.1, 6.6.3, 6.6.4 | A procedure exported by a module is callable from `user` under its plain name without an import | `existence_error(procedure, e/1)` | Succeeds. This also happens in `StrictIso`, where it is GAP-41 | Documented, but described as an extension |

CHG-04 is contested. The introduction of 7.11 says the range of values of some flags can be
extended with implementation specific values, but it does not name the flags. 7.11.2.5 lists three
values for `double_quotes`, and issue #11 reads the flag as not extensible. The value also creates
terms of the extra string type (EXT-08). Until the question is settled, this page treats the value
as a modification.

## Modern gaps

| ID | Severity | Ref | Gap | Status |
|---|---|---|---|---|
| MGAP-01 | Medium | 6.3.3, 6.3.4.2 | A prefix operator followed, in functional notation, by an atom that is an infix operator is read as a different term instead of being rejected, as in GAP-03 for `StrictIso`. `X = - '->'(a, b).` reads as `(X = -) -> (a, b)`, so the clause `c(X) :- X = - '->'(a, b).` calls `a/0`. `X = \+ ;(a, b).`, `X = - ','(a, b).`, and <code>X = - '&#124;'(a, b).</code> are misread in the same way. `X = - '.'(a, []).` is a syntax error because `.` is an operator in Modern. SWI-Prolog 10 reads these texts as ISO does | Open |
| MGAP-02 | Medium | 7.1.3, 5.5.10, 7.10.5 | The evaluable functors `inf/0`, `infinite/0`, and `nan/0` return IEEE infinity and NaN as floats. These values are outside the float set of 7.1.3. `writeq/1` writes them as `Infinity.0` and `NaN.0`, which read back as `'.'/2` terms, and `nan =:= nan` succeeds. The documentation says float arithmetic rejects NaN and infinity | Open |
| MGAP-03 | Medium | 5.4, 7.4.3 | A user definition of one of the 41 natively implemented extension predicates (marked † in [the predicate list](#extension-predicates)) or of `'*->'/2` is silently ignored: compiled calls and meta-calls run the built-in, with no diagnostic, and so they do when the definition is in another text unit. A definition of one of the 122 extension predicates written in Prolog replaces the library version. ISO leaves the effect of clauses for a built-in undefined (7.4.3, 7.4.1), but the documentation says user-defined names are unrestricted and does not describe the split. This is the Modern form of GAP-09 | Open |
| MGAP-04 | Low | Part 2 6.2.6, Part 3 7.4.4 | Module text and grammar rules cannot define library names that user text may redefine. The Part 2 6.2.6.1 `utilities` example, which defines `length/2` and `reverse/2`, is rejected with DPL1019, and `member --> [a].` is rejected with DPL1007. This is the Modern form of GAP-40 and GAP-69 | Open |
| MGAP-05 | Medium | 5.4 | These Modern extensions are undocumented: the operators `xor`, `*->`, `module`, `use_module`, and `meta_predicate`, the priorities and types of all extra operators, and the readings they change (`X = $ - 1.` and `X = dynamic - 1.` are syntax errors); `"-"1` under `double_quotes` = `atom` (GAP-05); an end char at the end of input (GAP-06); non-ASCII layout (GAP-07); the write option `spacing/1` (GAP-13); `(^)/2` as a callable predicate; `sumlist/2`; the evaluable functor `infinite/0`; clause conversion and evaluation of strings (5.5.4 c, e); writing and clause conversion of rationals; the reserved `$` names (GAP-14) | Open |
| MGAP-06 | Low | 5.4 | Documentation that the probes contradict in Modern: Modern is described as the ISO core plus extensions, although it has modifications (CHG-01 to CHG-07); strings exist under the default `double_quotes` = `chars` (`atom_string/2`, `split_string/4`); `print/1` writes unquoted, so its output does not read back as documented; writing depends on the mode, because Modern writes non-ASCII atoms and its extra operators without quotes, and `StrictIso` rejects that output; `occurs_check` is enumerated as a flag, although the ledgers say extension flags are hidden; back-quoted text is said to follow SWI-Prolog, which reads it as codes by default; the SWI ledger omits that SWI accepts the `\s` escape, which Modern rejects; a file loaded by `ensure_loaded/1` starts with the loading file's `double_quotes` value, not the mode or host value | Open |

## ISO tracker gaps in Modern

56 of the 71 gaps behave the same in Modern: GAP-02, GAP-04, GAP-08, GAP-14, GAP-16 to GAP-34,
GAP-36 to GAP-39, GAP-42 to GAP-68, GAP-70, and GAP-71. The other 15:

| Gap | Modern | Notes |
|---|---|---|
| GAP-01 | Extension (5.5.2) | Extra predefined operators are permitted; the five shared with `StrictIso` are undocumented as operators (MGAP-05) |
| GAP-03 | Gap, other form | MGAP-01 |
| GAP-05 | Extension (5.5.1) | Undocumented (MGAP-05) |
| GAP-06 | Extension (5.5.1) | Undocumented (MGAP-05) |
| GAP-07 | Extension (5.5.1) | The Unicode identifier classes are documented; non-ASCII layout is not (MGAP-05) |
| GAP-09 | Gap, other form | MGAP-03 |
| GAP-10 | Conforms | No process stops |
| GAP-11 | Extension (5.5.9) | The names are built-in predicates in Modern, and the database built-ins treat them as ISO requires for built-ins |
| GAP-12 | Extension (5.5.5) | Goal directives, documented |
| GAP-13 | Extension (Cor.3 5.5.12) | Undocumented (MGAP-05) |
| GAP-15 | Conforms | Every existential example passes, including Cor.3 `setof/3` example 20. A `^` below the top of the goal runs the `(^)/2` extension predicate |
| GAP-35 | Closed | As in the ISO tracker |
| GAP-40 | Gap, other form | MGAP-04. User text may redefine the same names |
| GAP-41 | Modification | CHG-07 |
| GAP-69 | Gap, other form | MGAP-04 |

## Extensions

| ID | Clause | Extension | Documented |
|---|---|---|---|
| EXT-01 | 5.5.1 | The `\e` escape (code 27), also in `0'\e` | Yes |
| EXT-02 | 5.5.1 | The `\uXXXX` and `\UXXXXXXXX` escapes; a surrogate escape is a syntax error | Yes |
| EXT-03 | 5.5.1 | Back-quoted text denotes an atom | Yes; the SWI comparison is wrong (MGAP-06) |
| EXT-04 | 5.5.1 | Additional characters: Unicode letters and letter numbers start atoms, combining marks and superscript digits continue them, and non-ASCII symbols are solo characters | Yes; non-ASCII layout is not (MGAP-05) |
| EXT-05 | 5.5.1 | An operator atom may stand as an operand (`X = -`, `- = -`). This affects only text that ISO rejects | Yes |
| EXT-06 | 5.5.2 | Modern-only operators: `op(1150, fx, Op)` for `dynamic`, `discontiguous`, `ensure_loaded`, `include`, `initialization`, and `multifile`; `op(990, xfx, :=)`; `op(400, yfx, rdiv)`; `op(100, yfx, '.')`; `op(1, fx, $)` | Named without priorities (MGAP-05) |
| EXT-07 | 5.5.2 | Operators in both modes: `op(500, yfx, xor)`, `op(1050, xfy, *->)`, and `op(1150, fx, Op)` for `module`, `use_module`, and `meta_predicate`. In `StrictIso` they are GAP-01 | No (MGAP-05) |
| EXT-08 | 5.5.4 | The string type. A string has one type and sorts after the numbers and before the atoms, so 5.5.4 a and b hold | Syntax and writing are documented; clause conversion and evaluation are not (MGAP-05) |
| EXT-09 | 5.5.5 | The directives `module/2`, `use_module/1,2`, and `meta_predicate/1`. `use_module/1` takes file names only: `use_module(library(lists))` is rejected with DPL1008 | Yes |
| EXT-10 | 5.5.5 | `initialization/2` with `now`, `after_load`, or `main` | Yes |
| EXT-11 | 5.5.5 | Any callable term as a directive; GAP-12 in `StrictIso` | Yes |
| EXT-12 | 5.5.6 | Side effects of extension predicates: global variables, `freeze/2` wakeups, `print_message/2` output with the `message_hook/3` hook, process exit from `initialization(_, main)`, and the `format/3` and `with_output_to/2` sinks | Yes |
| EXT-13 | 5.5.7 | The soft cut `*->/2` | Yes; a user definition is ignored (MGAP-03) |
| EXT-14 | Part 3 5.5.13 | Soft cut as a grammar control construct | Yes |
| EXT-15 | 5.5.8 | The flag `occurs_check`, with values `false` (initial), `true`, and `error` | Yes |
| EXT-16 | 5.5.9 | 163 extension predicates, [listed below](#extension-predicates) | Named, mostly by reference to SWI-Prolog; `sumlist/2` and `(^)/2` are not (MGAP-05) |
| EXT-17 | 5.5.9 | Extra error terms from extension predicates, such as `existence_error(variable, K)`, `domain_error(non_empty_atom, A)`, `type_error(rational, X)`, `type_error(text, X)`, `resource_error(finite_memory)`, and the formal `format_argument_type(D, Culprit)` | In part |
| EXT-18 | 5.5.10 | 12 evaluable functors: `e/0`, `inf/0`, `infinite/0`, `nan/0`, `max_tagged_integer/0`, `min_tagged_integer/0`, `integer/1`, `numerator/1`, `denominator/1`, `rational/1`, `rationalize/1`, and `rdiv/2`. No ISO expression yields a value of an extra type; `2^(-1)` raises `type_error(float, 2)` as Cor.3 requires. The values of `inf`, `infinite`, and `nan` are MGAP-02 | In part (MGAP-05) |
| EXT-19 | 5.5.11 | About 208 reserved internal `$` names | No (GAP-14, MGAP-05) |
| EXT-20 | Cor.3 5.5.12 | The write option `spacing/1`, with values `standard` and `next_argument`; also accepted in `StrictIso` (GAP-13). Invalid values raise the errors 5.5.12 requires | Changelog only (MGAP-05) |

### Extension predicates

The 163 extension predicates, grouped by area. † marks the 41 natively implemented predicates,
whose user definitions are ignored (MGAP-03). The others are written in Prolog, and a user
definition in consulted text replaces them.

- **Arithmetic helpers (4):** `between/3`†, `succ/2`†, `plus/3`†, `rational/1`†
- **Associations (18):** `empty_assoc/1`, `is_assoc/1`, `put_assoc/4`, `get_assoc/3,5`,
  `gen_assoc/3`, `list_to_assoc/2`, `ord_list_to_assoc/2`, `assoc_to_list/2`, `assoc_to_keys/2`,
  `assoc_to_values/2`, `min_assoc/3`, `max_assoc/3`, `map_assoc/2,3`, `del_assoc/4`,
  `del_min_assoc/4`, `del_max_assoc/4`
- **Control (7):** `(^)/2`, `ignore/1`, `not/1`, `setup_call_cleanup/3`, `call_cleanup/2`,
  `freeze/2`, `frozen/2`
- **Database (1):** `assert/1`†
- **Errors and validation (11):** `must_be/2`, `is_of_type/2`, `instantiation_error/1`,
  `uninstantiation_error/1`, `type_error/2`, `domain_error/2`, `existence_error/2`,
  `permission_error/3`, `representation_error/1`, `resource_error/1`, `syntax_error/1`
- **Global variables (5):** `nb_setval/2`†, `nb_getval/2`†, `b_setval/2`†, `b_getval/2`†,
  `nb_current/2`†
- **Higher order (13):** `maplist/2..8`, `foldl/4..6`, `include/3`, `exclude/3`, `partition/4`
- **Input and output (15):** `format/1,2,3`, `print/1,2`†, `writeln/1`†, `tab/1`†, `tab/2`,
  `print_message/2`, `portray_clause/1,2`, `with_output_to/2`, `current_stream/1`†,
  `read_term_from_atom/3`, `term_to_atom/2`
- **Lists (27):** `append/3`, `member/2`, `memberchk/2`, `length/2`, `reverse/2`, `nth0/3,4`,
  `nth1/3,4`, `last/2`, `select/3`, `selectchk/3`, `subtract/3`, `intersection/3`, `union/3`,
  `delete/3`, `list_to_set/2`, `permutation/2`, `flatten/2`, `numlist/3`, `sum_list/2`,
  `sumlist/2`, `max_list/2`, `min_list/2`, `max_member/2`, `min_member/2`, `is_list/1`†
- **Loading (3):** `consult/1`†, `ensure_loaded/1`† as a predicate, `initialization/2`
- **Ordered sets (14):** `list_to_ord_set/2`, `ord_empty/1`, `ord_memberchk/2`, `ord_subset/2`,
  `ord_disjoint/2`, `ord_seteq/2`, `ord_union/2,3`, `ord_intersection/2,3`, `ord_subtract/3`,
  `ord_symdiff/3`, `ord_add_element/3`, `ord_del_element/3`
- **Pairs (4):** `pairs_keys_values/3`, `pairs_keys/2`, `pairs_values/2`, `transpose_pairs/2`
- **Solutions (8):** `findall/4`, `forall/2`, `aggregate_all/3,4`, `aggregate/3,4`, `call_nth/2`,
  `countall/2`
- **Sorting (3):** `msort/2`†, `sort/4`†, `predsort/3`
- **Strings (15):** `string/1`†, `string_length/2`†, `string_chars/2`†, `string_codes/2`†,
  `string_to_atom/2`†, `string_lower/2`†, `string_upper/2`†, `number_string/2`†,
  `split_string/4`†, `text_to_string/2`†, `string_concat/3`, `sub_string/5`, `string_code/3`,
  `term_string/2,3`
- **Terms (6):** `?=/2`, `variant/2`, `numbervars/3`, `setarg/3`†, `nb_setarg/3`†,
  `term_size/2`†
- **Text (9):** `atom_number/2`†, `atom_string/2`†, `atomic_list_concat/2,3`†, `upcase_atom/2`†,
  `downcase_atom/2`†, `char_type/2`†, `code_type/2`†, `atom_to_term/3`

`bagof/3` and `setof/3` depend on the public `member/2`, `reverse/2`, and `append/3`, so a user
definition of one of them changes their results in both modes (GAP-66).

## Requirements that differ from StrictIso

These rows of the ISO tracker have a different status, cause, or reviewed outcome in Modern.
Every other row inherits its status, subject to the coverage limits above; numerical probe counts
and mode-specific observations in the StrictIso notes are not inherited. Where a row cites a gap, the table in
[ISO tracker gaps in Modern](#iso-tracker-gaps-in-modern) gives the gap's Modern form.

**Part 1 clause 5: compliance**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 5.1 a | Prepare all conforming text for execution | Partial | Modified | CHG-01 and CHG-02 change how conforming text reads. Also GAP-02, GAP-04, MGAP-01. GAP-09 and GAP-11 do not apply |
| 5.1 b | Execute conforming goals correctly | Partial | Partial | GAP-16 to GAP-21, GAP-66, MGAP-02. GAP-10, GAP-11, and GAP-15 do not occur |
| 5.1 c | Reject text and read-terms whose syntax does not conform | Partial | Partial | The historical Modern reader report has 303 automatic ISO-baseline matches, 51 differences, and 11 manual cases. Differences require the classifications in this page; they are not all gaps. StrictIso's 344/365 total does not apply |
| 5.1 e | Offer a strictly conforming mode that rejects implementation specific features in text and during execution | Partial | N/A | A requirement on the strictly conforming mode |
| 5.2 | Conforming and strictly conforming Prolog text | Partial | Modified | CHG-01, CHG-02 |
| 5.3 | Conforming and strictly conforming goals | Partial | Modified | CHG-04, CHG-06 and CHG-07 change the results of conforming goals. Extension goals run (5.5.9) |
| 5.5.1 | Extra syntax must not change the meaning of conforming text | Partial | Modified | CHG-01 and CHG-02 change the abstract syntax of conforming text. The syntax extensions EXT-01 to EXT-05 do not. Also GAP-02, MGAP-01 |
| 5.5.2 | Extra predefined operators | Partial | Partial | EXT-06, EXT-07. The operators `xor`, `*->`, `module`, `use_module`, and `meta_predicate`, and the readings the extra operators change, are undocumented (MGAP-05) |
| 5.5.4 | Extra types | Done | Modified | CHG-03: rationals break 5.5.4 b. The string type (EXT-08) meets a, b, d, and f; c and e are undocumented (MGAP-05) |
| 5.5.5 | Extra directives | Partial | Extension | EXT-09 to EXT-11 |
| 5.5.7 | Extra control constructs | Partial | Extension | EXT-13 |
| 5.5.8 | Extra flags | Done | Extension | EXT-15. The extra `double_quotes` value is CHG-04 |
| 5.5.9 | Extra built-in predicates | Partial | Partial | EXT-16, EXT-17. MGAP-03, GAP-66 |
| 5.5.10 | Extra evaluable functors | Done | Partial | EXT-18. MGAP-02 |
| Cor.3 5.5.12 | Extra options; an invalid option gives only an instantiation error or a domain error | Partial | Partial | EXT-20; `spacing/1` is undocumented (MGAP-05) |

**Part 1 clause 6: syntax**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 6.3.1.2 | `-` followed by a numeric literal is a negative number, including after layout, a comment, or with `-` quoted | Done | Modified | CHG-01 |
| 6.3.1.3 | An operator atom as an operand has priority 1201 | Done | Extension | EXT-05 |
| 6.3.3 | Functional notation needs `(` immediately after the name | Partial | Partial | GAP-04. GAP-03 becomes a silent misreading (MGAP-01) |
| 6.3.4.2 | Prefix, infix, and postfix operators | Partial | Modified | CHG-01 (`- 1^2` reads as `-(1^2)`). MGAP-01 |
| 6.3.4.4, table 7 | Initial operator table | Partial | Partial | EXT-06, EXT-07; the table is not fully documented (MGAP-05) |
| 6.4.2 | Name tokens | Done | Extension | `a.b` reads through the `.` operator (EXT-06), text that ISO rejects |
| 6.4.2.1 | Quoted characters and escapes; octal and hex escapes need a closing `\` | Done | Extension | EXT-01, EXT-02 |
| 6.4.4 | Integer tokens, including `0'` character codes | Done | Modified | CHG-02. `0'\e` is EXT-01 |
| 6.4.5 | Float tokens | Done | Extension | `1.e2` reads as `'.'(1, e2)` through the `.` operator, text that ISO rejects |
| 6.4.7 | A back-quoted string is a token that denotes no term | Done | Extension | EXT-03 |
| 6.4.8 | Other tokens; the end char | Partial | Partial | GAP-06 is an undocumented extension in Modern (MGAP-05) |
| 6.5 | Processor character set and character classes | Partial | Partial | EXT-04. Non-ASCII layout (GAP-07) is undocumented (MGAP-05) |

**Part 1 clause 7: language concepts and semantics**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 7.1.1.1–7.1.1.4 | Variable sets, existential variables, free variables | Partial | Done | GAP-15 does not occur |
| 7.1.3 | Floats | Partial | Partial | GAP-67, MGAP-02 |
| 7.1.6.3 | Iterated-goal term; Cor.3 applies it only to a term of the form `^(_, G)` | Partial | Done | GAP-15 does not occur |
| 7.4.2 | Directives | Partial | Partial | GAP-18. Goal directives are EXT-11 |
| 7.4.2.1 | `dynamic/1` | Partial | Done | `dynamic(length/2)` gets the error for a built-in, as `length/2` is one in Modern |
| 7.4.2.5 | `char_conversion/2` directive | Done | Modified | CHG-05: the directive has no effect while the flag is `off` |
| 7.4.2.8 | `ensure_loaded/1` | Partial | Partial | MGAP-03 for native extension names defined in the loaded unit |
| 7.7.7 | Clause selection; the `unknown` flag | Partial | Done | GAP-11 does not occur |
| 7.8.3 | `call/1` | Partial | Partial | GAP-10 does not occur, but GAP-70 remains: a file containing module text exposes internal names in the error culprit. See `module_call_culprit` in [recheck evidence](#recheck-evidence) |
| 7.8.9 | `catch/3` | Partial | Done | GAP-10 does not occur |
| 7.10.4 | Write options, including the Cor.3 `variable_names/1` | Partial | Partial | EXT-20 is undocumented (MGAP-05) |
| 7.10.5 a–e | Writing variables, numbers, atoms, and `'$VAR'(N)`; Cor.3 a1 writes a named variable unquoted | Partial | Partial | GAP-67, MGAP-02 (`Infinity.0` does not read back) |
| 7.11.2.1 | `char_conversion` | Done | Modified | CHG-05 |
| 7.11.2.5 | `double_quotes` | Done | Modified | CHG-04 (contested). The initial value `chars` is implementation defined and documented |
| 7.12.2 e | `permission_error` | Partial | Done | `implementation_specific_feature` never occurs |

**Part 1 clause 8: built-in predicates**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 8.10.1 | `findall/3` | Partial | Done | GAP-10 does not occur |
| 8.10.2 | `bagof/3` | Partial | Partial | GAP-66 remains; GAP-10 and GAP-15 do not. The local report records all examples except example 9 matching ISO and 6/6 error cases matching. Example 9 uses the nested callable `(^)/2` extension. The repository recheck covers existential goals, the six error cases, the nested extension, and the remaining grouping gap |
| 8.10.3 | `setof/3` | Partial | Partial | GAP-66 remains; GAP-10 and GAP-15 do not. The local report records all examples except example 11 matching ISO and 6/6 error cases matching. Example 11 uses the nested callable `(^)/2` extension. The repository recheck covers existential goals, the six error cases, the nested extension, and the remaining grouping gap |
| 8.10.2, 8.10.3 | `^` makes variables existential | Missing | Done | GAP-15 does not occur |
| Cor.1 8.14.4 | `current_op/3` | Partial | Extension | Enumerates the extra operators (EXT-06, EXT-07) |
| 8.14.5 | `char_conversion/2` | Done | Modified | CHG-05: the read example fails while the flag is `off` |
| 8.17.1 | `set_prolog_flag/2` | Partial | Modified | CHG-04. GAP-31 |
| 8.17.2 | `current_prolog_flag/2` | Done | Modified | CHG-06. Also enumerates `occurs_check` (EXT-15) |

**Corrigenda 1, 2, and 3**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| Cor.3 7.1.6.3 a | Iterated-goal term only for `^(_, G)` | Partial | Done | GAP-15 does not occur |
| Cor.3 8.10.3.4 | `setof/3` example 20 restores the 1995 list and must succeed with the existential qualifier retained | Partial | Done | Example 20 succeeds |

**Implementation defined documentation**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 7.1.3 | Float parameters | Done | Partial | MGAP-02 contradicts the documented finite binary64 model |

**Part 2: modules**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 4.1 a–c, 4.2, 4.3 | Prepare and execute conforming module text and goals | Partial | Modified | CHG-07. GAP-40, GAP-42 to GAP-47 |
| 4.1 e | The strictly conforming mode rejects implementation specific features | Partial | N/A | A requirement on the strictly conforming mode |
| 4.4.2 | The module `user` | Partial | Modified | CHG-07. GAP-42 |
| 6.2.4.7 | Interface `char_conversion/2` | Done | Modified | CHG-05 |
| 6.2.6 | Clauses in bodies | Partial | Partial | MGAP-04 |
| 6.2.6.1 | Examples | Partial | Partial | MGAP-04 |
| 6.3.1, 6.3.2 | Visible database | Partial | Modified | CHG-07 |

**Part 3: grammar rules**

| Ref | Requirement | StrictIso | Modern | Notes |
|---|---|---|---|---|
| 5.5.13 | Extra grammar controls are non-terminals in the strictly conforming mode | Partial | Partial | EXT-14. GAP-71 |
| 7.4.4 | Restrictions on grammar rule heads | Partial | Partial | MGAP-04 |

## Recheck evidence

The original probes in
[`tests/conformance/modern_tracker/`](https://github.com/kidoz/dotprolog/tree/main/tests/conformance/modern_tracker)
cover the corrected findings. Run each entry point in a fresh process from the repository root,
with Modern's default flags:

```sh
dotnet run --project src/DotProlog.Tool -- run --mode modern tests/conformance/modern_tracker/core.pl
dotnet run --project src/DotProlog.Tool -- run --mode modern tests/conformance/modern_tracker/module_call.pl
dotnet run --project src/DotProlog.Tool -- run --mode modern tests/conformance/modern_tracker/overridden_member.pl
```

The helper prints `observation(Id, Result)`, where `Result` is the complete `solutions(List)` or
`error(Formal)`. Compare it with the expectations below. Process success is not a conformance
pass: these audit programs deliberately expose open gaps. They do not alter the passing regression
corpus or its pinned counts. `report.pl` is a shared helper, not a fourth entry point.

The following results were reproduced at `41fd728` on 2026-09-29:

| Case ID | Expected outcome and basis | Observed in Modern |
|---|---|---|
| `default_double_quotes` | Documented Modern default: `solutions([chars])` | Matches |
| `rational_quoted_codes` | With that default, `error(type_error(integer,'1'))` | Matches; the quoted text is a list of characters, not codes |
| `rational_explicit_codes` | Modern rational syntax: `solutions([1r3])` | Matches; StrictIso instead reports `error(syntax_error(illegal_number))` |
| `bagof_existential` | 8.10.2: `solutions([[beta,alpha]])` | Matches; GAP-15 does not occur |
| `setof_existential` | 8.10.3: `solutions([[alpha,beta]])` | Matches; GAP-15 does not occur |
| `bagof_nested_caret` | Callable `(^)/2` extension: `solutions([[beta,alpha]])` | Matches; this is distinct from stripping a leading existential qualifier |
| `setof_nested_caret` | Callable `(^)/2` extension: `solutions([[alpha,beta]])` | Matches |
| `bagof_variable`, `bagof_nested_variable`, `setof_variable`, `setof_quantified_variable` | 8.10.2/3: `error(instantiation_error)` | All four match |
| `bagof_quantified_noncallable`, `setof_noncallable`, `setof_quantified_noncallable` | 8.10.2/3: `error(type_error(callable,23))` | All three match |
| `bagof_output_atom`, `setof_output_atom` | 8.10.2/3: `error(type_error(list,invalid))` | Both match |
| `bagof_output_tail`, `bagof_empty_bad_output`, `setof_output_tail` | 8.10.2/3: <code>error(type_error(list,[kept&#124;invalid]))</code> | All three match; six error cases per predicate are covered |
| `module_call_culprit` | 7.8.3.3: `error(type_error(callable,(write(marker),23)))`, without output from `write/1` | No side effect, but the culprit contains `'$write'(user,marker)` instead of `write(marker)`. GAP-70 |
| `bagof_overridden_member`, `setof_overridden_member` | Public-library redefinition must preserve both answer groups, in either group order: `a-[10,30]` and `b-[20]` | Both return only `solutions([a-[10,30]])`. GAP-66 |

The grouping probes record a Modern defect under the documented ability to redefine library
predicates. In StrictIso the same loss also affects strictly conforming user definitions; see
GAP-66 in the ISO tracker. The nested-caret cases exercise an extension and are not counted as ISO
matches merely because they succeed.

## Found while testing

- `dotnet prolog run --mode strict-iso --flag double_quotes=string` stops with an unhandled
  `System.ArgumentException` and exit status 134, instead of reporting an invalid flag override.
  This is a command-line defect, not a Modern behavior.

## Keeping this page current

1. When a modification is resolved, record how: Modern now conforms, or the behavior stays and is
   documented as a modification together with the `StrictIso` alternative.
2. When a gap is fixed, set its status to `Fixed in <version>` here and in the ISO tracker if it
   is shared, and add a regression case that runs in both modes.
3. When the ISO tracker gains or closes a gap, run its probes in both modes and update
   [ISO tracker gaps in Modern](#iso-tracker-gaps-in-modern) and the summary counts.
4. Document every new extension when it is added, under the 5.5 category that permits it.
