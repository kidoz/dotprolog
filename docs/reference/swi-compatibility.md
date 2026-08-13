# SWI-Prolog compatibility ledger

`Modern` mode is chartered as DotProlog's SWI-aligned dialect: the ISO-conforming core,
`double_quotes` seeded at `chars`, and an extension surface whose reference behavior is
SWI-Prolog's. This page is the ledger that keeps the alignment honest. A row says **supported**
only when repository tests cover it; DotProlog does not claim SWI-Prolog compatibility as a
whole. Deliberate behavioral divergences stay listed in [COMPATIBILITY.md on GitHub](https://github.com/kidoz/dotprolog/blob/main/COMPATIBILITY.md).

Unless a row says otherwise, a supported feature is available in the `Extended` and `Modern`
modes and rejected by `StrictIso` as an implementation-specific extension. An opt-in
differential suite (`DOTPROLOG_RUN_SWI_DIFFERENTIAL_TESTS=1`, requires `swipl` on `PATH`) executes
a corpus of ledger goals against SWI-Prolog itself and asserts the outputs agree.

## Syntax and data types

| SWI feature | DotProlog status |
|---|---|
| `double_quotes` values `codes`, `chars`, `atom`, `string` | Supported. ISO leaves the initial value implementation defined; `Modern` starts at `chars`, other modes at `codes`, and the initial value can be moved with the `DotPrologFlags` project property or `dotnet prolog --flag`. The `string` value is an extension gated out of `StrictIso` (flag, directive, and override alike), and no mode defaults to it — SWI's `string` default is its best-known ISO departure |
| String type and `"..."` strings | Supported: a distinct interned term — `string/1` true, `atom/1` false, `atomic/1` true, unification by identity, `write/1` bare text, `writeq/1` as `"..."` with ISO escapes. Standard order follows SWI 10 **as probed** — strings sort after numbers and before atoms, although SWI's manual still documents the older `Atom < String` order. Interned strings live for the program's lifetime, an embedding consideration beside atom growth |
| Unbounded integers and rationals (GMP) | Integers are supported unbounded: 60-bit tagged fixnums promote to interned big integers on overflow, literals of any length read exactly, and `bounded` is `false`, differential-verified against SWI for arithmetic, literals, ordering, indexing, and `format/2`. Interned big integers live for the program's lifetime, an embedding consideration beside atom growth. Divergences: an absurd shift count or exponent raises `resource_error(memory)` where SWI names its stack, and `^` with a negative integer exponent keeps ISO's `type_error(float, Base)` where SWI answers a float or rational. Rationals are supported: `1r3` literals (gated out of `StrictIso`, whose lexing is unchanged), `rdiv/2` as a 400 `yfx` operator, exact arithmetic mixing integers and rationals with float contagion, `floor`/`ceiling`/`round`/`truncate`/`integer` of rationals, the `numerator/1`, `denominator/1`, `rational/1`, and `rationalize/1` evaluables, the `rational/1` type test (true of integers, as SWI), `must_be(rational, X)`, number conversion of the `NrM` spelling, and canonical demotion so a denominator of 1 is always an integer — differential-verified. Divergences: SWI's `prefer_rationals` and `rational_syntax` flags are absent (`/` on two integers keeps its documented float choice), and rationals rank with integers in the standard order while floats keep their ISO rank |
| Dicts and version-7 syntax (`point{x:1}`, `[\|]`, block operators, zero-arity compounds) | Absent; a later roadmap phase |
| Unicode source text | Partial. Unicode atoms, variables, and quoted text are supported; a character is a UTF-16 code unit, so astral characters count as two in `atom_length/2` |
| Cyclic terms | Supported: cycle-safe unification and `acyclic_term/1` |
| Quasi-quotations | Out of charter |

## Core language

| SWI feature | DotProlog status |
|---|---|
| ISO Part 1 core | Supported and independently verified — see [COMPATIBILITY.md on GitHub](https://github.com/kidoz/dotprolog/blob/main/COMPATIBILITY.md) for the claim, which is `StrictIso`'s, not SWI alignment |
| Module system (Quintus/SWI spellings `module/2`, `use_module/1,2`, `meta_predicate/1`) | Supported, alongside the ISO Part 2 forms |
| DCGs, `phrase/2,3`, pushback | Supported (ISO TS 13211-3), plus soft cut in grammar bodies outside strict mode |
| Exceptions, `catch/3` | Supported |
| Soft cut `*->/2` | Supported |
| Last-call optimization | Supported |
| Clause indexing | First-argument indexing in the bytecode VM; SWI-style multi-argument JITI is not planned as such |
| `unify_with_occurs_check/2`, the `occurs_check` flag | Supported: `false`, `true`, and `error` with SWI's `occurs_check(Var, Term)` error term, guarding general unification (`=/2`, builtins, read-mode head arguments). Divergence: write-mode head unification — an unbound call argument against a structure head embedding the same variable — is not checked and builds the rational tree |
| Tabling (`:- table`, SLG, WFS) | Absent; a later roadmap phase |
| Attributed variables, `dif/2`, `freeze/2`, `when/2` | Absent; a later roadmap phase |
| Constraint solvers CLP(FD), CLP(R,Q), CHR | Absent; follows attributed variables on the roadmap |
| Single-sided unification rules (`=>`) | Absent; a later roadmap phase |
| Delimited continuations | Absent; a later roadmap phase |
| Global variables | Supported: `nb_setval/2`, `nb_getval/2`, `b_setval/2`, `b_getval/2`, and `nb_current/2`, scoped to the engine as SWI scopes them to a thread. `nb_getval/2` and `nb_current/2` materialize a fresh copy per read, so term identity across reads differs from SWI while values agree. `nb_current/2` fails for an unset name — unlike `nb_getval/2`'s existence error — and enumerates every set variable when the name is unbound |
| `setup_call_cleanup/3`, `call_cleanup/2` | Supported for deterministic exit (including the redo that exhausts the alternatives), failure, and exceptions, with SWI's probed ball precedence. Divergence: a surrounding cut or commit that discards the goal's pending alternatives does not fire the deferred cleanup — write `once(Goal)` for commit semantics |
| Threads, engines, coroutining interactors | Absent. One machine runs one goal at a time; concurrency belongs to the .NET host today |
| Garbage collection | Provided by the .NET runtime rather than a Prolog-specific collector |

## Library predicates

| Area | DotProlog status |
|---|---|
| `library(apply)`: `maplist/2..5`, `foldl/4..6`, `include/3`, `exclude/3`, `partition/4` | Supported |
| `library(lists)`: `append/3`, `member/2`, `memberchk/2`, `length/2`, `reverse/2`, `nth0/3`, `nth1/3`, `last/2`, `select/3`, `selectchk/3`, `subtract/3`, `intersection/3`, `union/3`, `delete/3`, `list_to_set/2`, `permutation/2`, `flatten/2`, `numlist/3`, `sum_list/2`, `max_list/2`, `min_list/2`, `max_member/2`, `min_member/2` | Supported |
| `library(pairs)` | Supported: `pairs_keys_values/3`, `pairs_keys/2`, `pairs_values/2`, `transpose_pairs/2` |
| Sorting: `msort/2`, `sort/4`, `predsort/3`, `keysort/2` | Supported |
| `library(error)`: `must_be/2`, `is_of_type/2`, `instantiation_error/1`, `uninstantiation_error/1`, `type_error/2`, `domain_error/2`, `existence_error/2`, `permission_error/3`, `representation_error/1`, `resource_error/1`, `syntax_error/1` | Supported. The error split follows SWI's `is_not/2`; the type table includes `string` and `text` admits strings. List-typed failures report the requested type name (`chars`) rather than SWI's parametric `list(char)` |
| `library(assoc)`: `empty_assoc/1`, `is_assoc/1`, `put_assoc/4`, `get_assoc/3`, `get_assoc/5`, `gen_assoc/3`, `list_to_assoc/2`, `ord_list_to_assoc/2`, `assoc_to_list/2`, `assoc_to_keys/2`, `assoc_to_values/2`, `min_assoc/3`, `max_assoc/3`, `map_assoc/2,3`, `del_assoc/4`, `del_min_assoc/4`, `del_max_assoc/4` | Supported, as AVL trees — the full exported surface of SWI's `library(assoc)` |
| `library(ordsets)`: `list_to_ord_set/2`, `ord_empty/1`, `ord_memberchk/2`, `ord_subset/2`, `ord_disjoint/2`, `ord_seteq/2`, `ord_union/3`, `ord_intersection/3`, `ord_subtract/3`, `ord_symdiff/3`, `ord_add_element/3`, `ord_del_element/3`, `ord_union/2`, `ord_intersection/2` | Supported |
| All-solutions: `findall/3,4`, `bagof/3`, `setof/3`, `forall/2`, `aggregate_all/3,4`, `aggregate/3,4` | Supported for the simple specs `count`, `count/1`, `bag/1`, `set/1`, `sum/1`, `max/1`, `min/1` and the witnessed `max/2`, `min/2`, which compare arithmetically, keep the first solution on a tie, and answer `max(Value, Witness)` / `min(Value, Witness)`. Compound templates such as `r(sum(X), count)` aggregate each spec argument and keep the template's shape, with SWI's instantiation, type, and domain errors for invalid templates |
| Arithmetic helpers: `between/3`, `succ/2`, `plus/3` | Supported, including `between/3` with an `inf`/`infinite` upper bound |
| Formatting: `format/1,2,3` with column stops, `tab/1,2` | Supported for the directive set in the language guide, including radix `~r`/`~R`, `~W` (write_term options), and `~@` (a goal whose output is inserted in place; its failure or ball still emits the preceding text, as SWI's streaming does). Divergence: every `~@` goal runs before the surrounding text is written, so interleaving with *other* streams can differ. `format/3` writes to `atom(A)`, `codes(C)`, `chars(C)`, or a stream |
| Term text: `term_to_atom/2`, `atom_to_term/3`, `read_term_from_atom/3`, `with_output_to/2` | Supported, with `atom`, `codes`, `chars`, and `string` sinks |
| Terms: `copy_term/2`, `term_variables/2`, `subsumes_term/2`, `numbervars/3`, `variant/2`, `?=/2`, `setarg/3`, `nb_setarg/3` | Supported. `nb_setarg/3` accepts atomic replacement values only — a compound copy would dangle once backtracking truncates the heap; the counter idiom works unchanged. `term_size/2` answers the cells of a detached copy — shared subterms count once per occurrence, a cyclic term raises `representation_error(cyclic_term)`, and the counts are representation-specific rather than SWI's. `term_string/2` is provided by the string library below |
| Atoms: `atom_length/2`, `atom_concat/3`, `sub_atom/5`, `atomic_list_concat/2,3`, `atom_number/2`, `upcase_atom/2`, `downcase_atom/2` | Supported |
| String library | Supported: `string/1`, `string_length/2`, `string_concat/3` (with split enumeration), `atom_string/2`, `string_to_atom/2`, `string_chars/2`, `string_codes/2`, `number_string/2`, `term_string/2`, `sub_string/5`, `split_string/4`, `string_code/3`, `string_lower/2`, `string_upper/2`, plus `string(S)` sinks for `format/3` and `with_output_to/2` and `~s` accepting strings. Text inputs are strings, atoms, and numbers uniformly — SWI additionally accepts code and char lists in some positions and rejects atoms in others (`number_string/2`); `term_string/2` writes the canonical spelling like `term_to_atom/2`. The atom predicates deliberately keep ISO's `type_error(atom, …)` for strings — SWI's text-generic loosening is a deferred phase |
| `char_type/2`, `code_type/2` | Supported for the common type set, including enumeration of an unbound character in code order (the type must be bound). Characters are UTF-16 code units, so enumeration covers the BMP and counts for Unicode-wide classes can differ from SWI's full-codepoint ranges; ASCII-scoped classes such as `digit/1` agree. The counterintuitive SWI direction of `to_upper/1` and `to_lower/1` (a bound character answers its lowercase and uppercase respectively) is preserved, verified against SWI 10 |
| Edinburgh I/O (`see/1`, `tell/1`, …) | Absent; ISO streams are the supported model |
| `print_message/2`, `portray_clause/1,2` | `portray_clause/1,2` is supported with SWI's listing layout — facts, conjunction bodies, disjunction, if-then-else and soft-cut blocks, and negation, byte-identical to SWI 10 for the covered constructs, with `A`, `B`, ... variable names and `_` singletons. `print_message/2` is supported: the message becomes `Format-Args` lines, a user-defined `message_hook/3` may intercept them, and what remains goes to `user_error` behind SWI's kind prefixes (`ERROR: `, `Warning: `, `% ` for `informational`, none for `help` and unlisted kinds); `silent` and `debug(_)` print nothing, the latter matching SWI with no debug topic enabled. The `error(Formal, Context)` translations — type errors with SWI's culprit classification and article, domain, existence, permission with SWI's special cases, evaluation, representation, resource, syntax, uninstantiation, and occurs-check errors, with `context/2` callers before the text and its message after — are SWI 10's wording, differential-verified. Divergences: SWI's source-location, thread, and did-you-mean decorations are not reproduced, `informational` is always printed (there is no `verbose` flag), and the `message_context`, `message_property`, and `prolog:message//1` customization hooks are absent |
| Database: `assert/1`, `asserta/1`, `assertz/1`, `retract/1`, `retractall/1`, `abolish/1`, `clause/2`, `current_predicate/1`, `predicate_property/2` | Supported with logical update view |
| Loading: `consult/1`, `ensure_loaded/1`, `:- include/1`, `:- initialization/1` | Supported |

## Ecosystem — out of charter

The HTTP and semantic-web stacks, Pengines, sockets/TLS, Redis/STOMP/ROS2, JPL and Janus bridges,
the pack manager, PlDoc, PlUnit, SWISH, and the graphical tools are ecosystem rather than
language, and DotProlog does not plan them. Their DotProlog counterparts are the .NET platform
itself: NuGet packaging, `dotnet test` integration, C#/F# interop through generated facades, and
NativeAOT publishing.
