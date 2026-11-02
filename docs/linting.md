# Linting reference {#source-linting}

`dotnet prolog lint` analyzes Prolog source without consulting it or executing directives. It uses
the same reader, operator table, language modes, source spans, and diagnostic format as the compiler.
For a procedure, see [lint source locally and in CI](how-to/lint-source.md).

## Invocation and options

```text
dotnet prolog lint [options] <file.pl> [more.pl ...]
```

| Option | Contract |
|---|---|
| `--warnings-as-errors` | Return `1` if warnings are reported; warnings are advisory by default |
| `--profile semantic` | Default profile: source-local and whole-program semantic checks |
| `--profile covington` | Add automatically checkable Covington layout guidelines 2.1–2.7 |
| `--enable-rule DPL3037` | Enable the opt-in non-steadfast clause heuristic; repeatable |
| `--enable-rule DPL3038` | Enable the opt-in non-tail-recursion heuristic; repeatable |
| `--enable-rule DPL3039` | Enable the opt-in multifile-cut heuristic; repeatable |
| `--enable-rule DPL3040` | Enable the opt-in repeat-loop heuristic; repeatable |
| `--enable-rule DPL3041` | Enable the opt-in final-cut heuristic; repeatable |
| `--enable-rule DPL3042` | Enable the opt-in catch-all heuristic; repeatable |
| `--enable-rule DPL3043` | Enable the opt-in missing-else heuristic; repeatable |
| `--enable-rule DPL3044` | Enable the opt-in predicate naming heuristic; repeatable |
| `--mode modern` or `--mode strict-iso` | Seed the reader's language mode; default `modern` |
| `--flag name=value` | Override a curated initial flag; repeatable |
| `--indent-size N` | Positive indentation unit; Covington default `4` |
| `--max-line-length N` | Positive line limit; Covington default `80` |
| `--max-clause-lines N` | Positive clause limit; Covington default `24` |

Profile names are case-insensitive. Options may appear in any order. Language settings are defined
in the [language reference](language-guide.md#language-modes). Strict-mode linting seeds the reader;
it does not replace compiling or running a project to enforce the strict language surface.

## Exit codes

| Code | Meaning |
|---:|---|
| `0` | Every named file was read; warnings may have been reported |
| `1` | Warnings were found with `--warnings-as-errors` |
| `64` | The command line is invalid |
| `65` | A file is missing or unreadable, or the reader reported an error |

## Semantic diagnostics

These rules run in every profile:

| Diagnostic | Meaning |
|---|---|
| `DPL3001` | An ordinary named variable occurs once in its clause |
| `DPL3002` | An underscore-prefixed singleton marker occurs more than once in its clause |
| `DPL3011` | Double-quoted text is passed to a conversion that needs a different list kind |
| `DPL3012` | Double-quoted text read as characters is parsed by a grammar that compares character codes |
| `DPL3013` | A literal format string, argument count, or argument type is invalid |
| `DPL3014` | A literal predicate option or option value is invalid |
| `DPL3015` | An arithmetic expression contains a statically invalid function or operand |
| `DPL3016` | A call has no matching built-in or file-local predicate signature |
| `DPL3017` | A goal succeeds without changing bindings or producing another observable effect |
| `DPL3018` | A goal always fails when execution reaches it |
| `DPL3019` | An existentially qualified variable does not occur in its goal |
| `DPL3020` | An all-solutions template and goal share no named variables |
| `DPL3021` | A variable occurs only once inside a `bagof/3` or `setof/3` goal |
| `DPL3022` | A `findall/3` call is followed directly by failure |
| `DPL3023` | Clauses of one predicate are separated without a `discontiguous/1` declaration |
| `DPL3024` | A clause or grammar rule duplicates an earlier one up to variable renaming |
| `DPL3025` | A predicate declaration is duplicated |
| `DPL3026` | A clause or grammar rule starts with a variant of its own head |
| `DPL3027` | A `discontiguous/1` or `meta_predicate/1` declaration has no file-local definition |
| `DPL3028` | A variable occurs once in every alternative branch and nowhere outside them |
| `DPL3029` | A statically resolved predicate or nonterminal call has no definition in the supplied program |
| `DPL3030` | An exported or declared predicate has no definition in the supplied program |
| `DPL3031` | A predicate is called as a nonterminal, or a nonterminal as a predicate |
| `DPL3032` | An include cannot be read, forms a cycle, or exceeds the expansion limit |
| `DPL3033` | No supplied clause head matches a statically resolved ground call |
| `DPL3034` | No recognized entry point reaches an ordinary predicate through static calls |
| `DPL3035` | An imported predicate or nonterminal has no static reference in its importing module |
| `DPL3036` | No recognized entry point reaches a grammar nonterminal through static calls |
| `DPL3037` | Opt-in: distinct head arguments share variables and the clause contains a same-scope cut |
| `DPL3038` | Opt-in: a direct self-call is outside a compiler tail position |
| `DPL3039` | Opt-in: a same-scope cut occurs in a clause declared multifile |
| `DPL3040` | Opt-in: a direct `repeat/0` goal occurs without a same-scope cut |
| `DPL3041` | Opt-in: a same-scope cut is final in the last supplied static clause |
| `DPL3042` | Opt-in: a direct `catch/3` goal has a variable catcher |
| `DPL3043` | Opt-in: a direct hard or soft conditional has no explicit else branch |
| `DPL3044` | Opt-in: a user-defined predicate or nonterminal name is outside lower `snake_case` |

An underscore-prefixed name marks an intentionally unused variable:

```prolog
head(_Ignored) :-
    true.
```

The anonymous variable `_` is always exempt because every occurrence denotes a fresh variable.
`DPL3028` additionally recognizes semantic singletons. In `(first(X) ; second(X))`, `X` occurs twice
syntactically but communicates no binding between alternatives. Variables used outside the branches,
variables repeated within one branch, and variables occurring in an if-then-else condition are not
reported by this rule.

### Literal calls and predetermined goals

`DPL3013` checks literal `format/1,2,3` strings and proper argument lists, including `~*`, `~W`, and
`~@` argument consumption. It skips dynamic format strings and argument lists. `DPL3014` checks
literal options for `open/4`, `close/2`, `read_term/2,3`, and `write_term/2,3`, including options
nested in `~W`. `DPL3015` checks arithmetic used by `is/2` and arithmetic comparisons for unknown
functions, non-evaluable operands, and literal zero divisors.

`DPL3016` is the file-local form of possible arity analysis used by the reusable single-file API.
The command-line whole-program path suppresses it in favor of `DPL3029` when it can resolve calls.

`DPL3017` reports goals such as `X = X` or `atom(foo)` that succeed without a useful effect.
`DPL3018` reports statically predetermined failures such as `a = b`, two fresh variables compared by
`==/2`, or `atom(42)`. Goal-shaped terms nested in ordinary data are not treated as executable goals.

### All-solutions predicates

`DPL3019` checks `^/2` existential quantifiers, `DPL3020` checks that template and goal share a
variable, and `DPL3021` reports goal-local singletons that also occur elsewhere in the clause so the
finding does not duplicate the ordinary singleton warning. `DPL3022` recognizes a `findall/3` call
whose collected result is discarded by immediate failure; a clearer failure-driven loop may be more
appropriate when collection was not intended.

### Clauses and declarations

`DPL3023` checks clause grouping and honors `discontiguous/1`. `DPL3024` compares clauses and grammar
rules up to variable renaming, while `DPL3025` compares declarations exactly; different
`meta_predicate/1` templates are not exact duplicates. `DPL3026` is limited to a leftmost goal that is
a variant of the head, not every recursive call. `DPL3027` applies only to `discontiguous/1` and
`meta_predicate/1`; a dynamic predicate may intentionally start with no clauses.

### Whole-program call analysis

Every named file joins one analysis unit. Modern `module/2` exports and `use_module/1,2` imports
resolve among those files; ISO `module/1`, `export/1`, `import/1,2`, and `reexport/1,2` declarations
are also modeled. Local definitions take priority over imports, visible `user` predicates, and
built-ins. Explicit `Module:Goal` calls resolve only when the module is known.

`include/1` expands relative to the including file with a depth limit and cycle detection. Included
clauses retain their physical file names and source spans. `DPL3032` reports an unreadable include,
a cycle, or an exceeded limit. Layout checks apply to explicitly named source text; semantic checks
also cover expanded clauses.

Predicate identity includes module, name, and arity. DCG nonterminals additionally retain their
`Name//Arity` identity and the two added predicate arguments. Built-in and declared meta-predicate
templates guide goal traversal and closure arity. Variable goals, runtime-constructed calls,
unavailable imports, unknown modules, and dynamic declarations remain unknown rather than producing
`DPL3029`. `DPL3030` covers exports and declarations without definitions, while `DPL3031` covers
ordinary-predicate/nonterminal confusion.

`DPL3033` checks direct ground calls against every supplied clause head of the resolved static
predicate. Atoms, numbers, concrete strings, compounds, and lists are compared without running any
code. Head variables may match ground arguments, and repeated named variables must bind consistently.
Dynamic and multifile predicates, variable-containing calls, reader-dependent double-quoted terms,
built-ins, meta-closures, and unknown resolutions are skipped. A matching rule head counts even when
its body would fail or throw, because bodies are never evaluated.

### Advisory reachability

`DPL3034` reports an ordinary predicate when no recognized entry point reaches it through static
calls. It emits at most one warning per `module:name/arity`, at the first supplied clause head.
Exports, predicates outside named modules, initialization goals, zero-arity `test_*` predicates, and
dynamic or multifile predicates are entry roots. Reachability follows resolved imports, control
constructs, module-qualified calls, DCGs, `phrase/2,3`, and declared meta-predicate closures.
Recursive and mutually recursive predicates are reported when no root reaches their cycle.

The `DPL3034` finding says that a predicate may be unreferenced; it is not proof that deleting it is
safe. Unused imports are outside this rule.

`DPL3036` applies the same analysis to grammar nonterminals. It emits at most one warning per
`module:Name//Arity`, at the first grammar-rule head. Exported, non-module, dynamic, and multifile
nonterminals are roots. Grammar bodies, `phrase/2,3`, and DCG `call/1` contribute edges. Test
discovery does not root a nonterminal. Reachability uses the lowered predicate identity with two
added arguments, so a resolved call reaches that identity even when `DPL3031` also reports
source-kind confusion. When ordinary clauses and grammar rules share one lowered identity, the
nonterminal finding is suppressed and only the ordinary definitions remain eligible for `DPL3034`.

Reachability requires a complete static call graph. Unresolved includes or imports, unknown calls or
modules, variable goals, unsupported meta-closures, runtime database mutation or source loading,
conditional compilation, source expansion, and unavailable source suppress `DPL3034` and `DPL3036`
for the entire analysis unit. Other diagnostics still run. As with every lint rule, no clause,
directive, initialization goal, or expansion is executed.

### Possibly unused imports

`DPL3035` reports imported predicates and nonterminals without statically resolved references in
their importing module. Selective `use_module/2` and `import/2` declarations produce one finding per
unused indicator, located at that indicator. Whole-module `use_module/1` and `import/1` declarations
produce one finding at the source or module argument only when none of their bindings is used; the
rule does not enumerate the remaining unused bindings when another binding is referenced. Empty
selective lists and all `reexport/1,2` declarations are excluded.

References can occur in any supplied clause or initialization goal, including code that `DPL3034`
reports as unreachable. Direct calls, DCGs, `phrase/2,3`, and declared meta-closures count when they
resolve through the import. Calls resolved locally, to another module, to `user`, or to a built-in
do not count. Explicitly qualified calls and usage by other modules do not count for the import
site. Predicate/nonterminal mismatches do not count as usage.

An import whose supplied source has operator, character-conversion, flag, initialization, or other
executable effects is excluded, including effects propagated through nested imports. The same
whole-unit incomplete-analysis conditions as reachability suppress `DPL3035` without suppressing
other diagnostics. A finding is an advisory hint, not proof that removing the import preserves
behavior.

### Opt-in non-steadfast clauses

`DPL3037` is disabled by default in every profile. Enable it with `--enable-rule DPL3037`; repeating
the option does not duplicate findings, and unsupported identifiers are usage errors. The reusable
`PrologLintOptions.EnabledRules` collection provides the same selection to compiler API callers.

The rule reports an ordinary clause or grammar rule when two distinct source head arguments share a
named variable and the body contains a cut in the clause's own scope. Nested terms are traversed, but
repetition within only one argument and anonymous variables do not count. DCG analysis ignores the
added difference-list arguments and semicontext. Each matching clause produces one warning at its
first eligible cut; multifile clauses are excluded, while dynamic clauses are eligible.

Conjunction, disjunction, and then/else branches can contain eligible cuts. If-then-else conditions
and negation have their own cut barriers. Cuts inside all-solutions goals, meta-calls, or ordinary
term data do not count. A bare soft-cut conditional is treated as the conjunction emitted by the
compiler. Unrelated unresolved calls do not suppress this file-local rule, and no source is executed.

This is a heuristic, not a proof of non-steadfastness or general mode/determinism analysis. Review
whether committing to the head unification is intentional before changing the clause.

### Opt-in non-tail recursion

`DPL3038` is disabled by default in every profile. Enable it with `--enable-rule DPL3038`, alone or
alongside other opt-in rules. It reports each direct self-call that is outside a tail position
understood by the clause compiler, located at the recursive call. The message identifies the ordinary
predicate or DCG nonterminal and states that remaining control flow prevents last-call optimization.

The last conjunction position is a tail position. Disjunction branches and then/else branches have
independent tail positions; if-then-else and soft-cut conditions are never tail positions. A bare
soft-cut conditional follows the compiler's conjunction behavior. Any later goal or cut makes a
self-call non-tail, even when that later operation looks redundant.

Self identity uses the current module, name, and arity, or the source `Name//Arity` plus two for a
nonterminal. Qualification by the same current module counts; another module, indirect or mutual
recursion, and runtime-constructed goals do not. Negation, all-solutions goals, cleanup goals,
`call/1` and `call/N`, and other meta-calls are not traversed. DCG `{}` goals are inspected as
ordinary goals, while DCG `call//1` is excluded. Static, dynamic, and multifile clauses are eligible,
and unrelated unresolved calls do not suppress this file-local rule.

This is an optimization hint, not a correctness fault, termination analysis, or proof that rewriting
recursion will improve a workload.

### Opt-in multifile cuts

`DPL3039` is disabled by default in every profile. Enable it with `--enable-rule DPL3039`, alone or
alongside other opt-in rules. It reports one warning per multifile clause at its first same-scope
cut. No head-variable aliasing is required. The message identifies the predicate or nonterminal and
states that the cut may discard alternatives contributed by other source units.

Declarations match the current module, name, and arity, or `Name//Arity` plus two for a nonterminal.
They are honored in the same physical file, in other explicitly named files, and through expanded
includes. Merely placing clauses in several files without a `multifile` declaration does not trigger
the rule. Dynamic multifile clauses are eligible.

The cut-scope traversal is shared with `DPL3037`: eligible control branches count, while
if-then-else conditions, negation, all-solutions goals, opaque goals, arbitrary meta-calls, and
ordinary term data do not create findings. A bare soft-cut conditional follows the compiler's
conjunction behavior, and DCG `{}` goals are inspected as ordinary goals. Unrelated unresolved calls
do not suppress this declaration-based rule.

This is a modularity review hint, not a correctness proof or a recommendation to remove the cut. The
linter does not evaluate which alternatives exist or classify the cut as red or green.

### Opt-in repeat loops

`DPL3040` is disabled by default in every profile. Enable it with `--enable-rule DPL3040`, alone or
alongside other opt-in rules. It reports at most one warning per ordinary clause or grammar rule at
the first direct `repeat/0` when no cut occurs in that clause's own cut scope. The message identifies
the predicate or nonterminal and asks for review of backtracking and loop-exit behavior.

Conjunction, disjunction, if-then-else conditions and branches, and soft-cut conditions and branches
are inspected for direct repeat goals. DCG `{}` goals are inspected as ordinary goals. Negation,
all-solutions goals, cleanup goals, opaque goals, arbitrary meta-calls, explicitly qualified goals,
and ordinary term data are not traversed. Static, dynamic, and multifile clauses are eligible.

The cut-scope traversal is shared with the other opt-in cut rules. A same-scope cut anywhere in the
written body suppresses the finding, without attempting to prove its reachability or its position
relative to the loop. Cuts behind cut barriers or inside opaque and meta-called goals do not count.

This is a control-flow review hint, not proof of non-termination, a claim that every repeat loop
requires a cut, or a recommendation that adding a cut is safe. No source is executed.

### Opt-in final cuts

`DPL3041` is disabled by default in every profile. Enable it with `--enable-rule DPL3041`, alone or
alongside other opt-in rules. It reports each same-scope cut in a final control-flow position of the
last supplied static clause for a predicate or nonterminal. Each finding is located at its cut and
asks whether committing the remaining choice points is intentional.

Procedure identity uses the module and lowered name/arity, with two added arguments for a grammar
rule. Ordinary and grammar definitions sharing that identity form one procedure; the last clause's
source kind determines the displayed identity. Whole-program order follows explicitly named files in
their supplied order and included clauses at their expansion positions. Single-file analysis uses
the supplied syntax list. A later fact or rule without a final cut suppresses an earlier finding.

Dynamic and multifile procedures are excluded because runtime-created or externally contributed
clauses can follow the supplied ones. Static discontiguous procedures remain eligible. Unrelated
unresolved calls do not suppress this source-order rule.

The rightmost conjunction position and independent disjunction and then/else branches have final
positions. If-then-else and soft-cut conditions are not final. Only direct control flow and DCG `{}`
goals are inspected; negation, all-solutions goals, cleanup goals, opaque goals, meta-calls, and
ordinary term data do not expose final cuts. Multiple final branch cuts receive separate findings.

This is a choice-point review hint, not proof that a cut is redundant or that removing it is safe.
No source is executed, and runtime clause prediction and automatic cut removal are out of scope.

### Opt-in catch-all handlers

`DPL3042` is disabled by default in every profile. Enable it with `--enable-rule DPL3042`, alone or
alongside other opt-in rules. It reports each direct `catch/3` goal whose catcher argument is a
variable, located at that variable. The message identifies the containing predicate or nonterminal
and states that the catcher may match every thrown term.

Anonymous, underscore-prefixed, ordinary named, and shared variables are eligible. The rule performs
no binding, alias, or data-flow analysis, so a named variable remains eligible even when earlier
source may bind it. Atoms and compound catcher patterns are excluded, including compounds containing
variables.

Conjunction, disjunction, if-then-else conditions and branches, and soft-cut conditions and branches
are inspected. DCG `{}` goals are inspected as ordinary goals. A direct `catch/3` also has its goal
and recovery arguments inspected for nested handlers, while its catcher remains term data. Negation,
all-solutions goals, cleanup goals, other opaque goals, arbitrary meta-calls, qualified calls, and
ordinary term data are excluded. Static, dynamic, and multifile clauses are eligible.

This is a broad-recovery review hint, not proof that the catcher is unbound, that an unexpected error
is swallowed, or that a rewrite is required. No source is executed.

### Opt-in missing else branches

`DPL3043` is disabled by default in every profile. Enable it with `--enable-rule DPL3043`, alone or
alongside other opt-in rules. It reports each direct `->/2` or `*->/2` conditional that is not the
left operand of its enclosing `;/2`, located at the conditional goal. The message identifies the
containing predicate or nonterminal and asks whether its condition-failure behavior is intentional.

`(Condition -> Then ; Else)` and `(Condition *-> Then ; Else)` suppress the outer finding. Any else
goal counts, including `true` or `fail`; the rule checks presence rather than judging usefulness.
Condition, then, and else goals are inspected independently for nested unpaired conditionals. A bare
soft conditional is included because it has no separate else path.

Conjunction, disjunction, conditional conditions and branches, and DCG `{}` ordinary goals are
traversed. Negation, all-solutions goals, cleanup goals, other opaque goals, arbitrary meta-calls,
`call/1` and `call/N`, DCG `call//1`, explicitly qualified calls, and ordinary term data are excluded.
Static, dynamic, and multifile clauses are eligible in both language modes. Unrelated unresolved calls
do not suppress this file-local rule.

This is a control-flow review hint, not a claim that every conditional requires an else branch or
that inserting `; true` preserves the intended behavior. No source is executed or rewritten.

### Opt-in predicate and nonterminal names

`DPL3044` is disabled by default in every profile. Enable it with `--enable-rule DPL3044`, alone or
alongside other opt-in rules. It reports a user-defined ordinary predicate or grammar nonterminal
whose decoded atom name does not match `^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$`. Names therefore start with
an ASCII lowercase letter and contain only lowercase letters, decimal digits, and single underscores
between nonempty segments. Source quoting does not change the decoded name being checked.

Facts, rules, and grammar rules with statically recognized heads are eligible, including static,
dynamic, and multifile definitions. At most one warning is emitted per module and lowered name/arity,
at the first supplied clause's name token. Ordinary and grammar definitions sharing a lowered
identity do not produce duplicates. Whole-program order spans named files and includes, with findings
retaining their physical source ownership.

Declarations, exports, imports, calls, and declaration-only procedures do not independently produce
findings. Symbolic names containing no alphanumeric character and `$`-prefixed implementation-private
names are excluded. Variable, module, file, and foreign-function names are outside the rule. Both
language modes use the same grammar, and unrelated unresolved calls do not affect it.

This is a naming-convention review hint, not an invalid-Prolog diagnostic or proof that renaming is
safe. Custom patterns, cross-project naming policy, and automatic renaming are out of scope. No source
is executed or rewritten.

### Double-quoted text

`DPL3011` and `DPL3012` judge each double-quoted literal by the `double_quotes` value in force where
it appears. The file starts at the value `--mode` and `--flag` select — `chars` by default, `codes`
in `strict-iso` — and a `:- set_prolog_flag(double_quotes, Value).` directive governs the clauses
after it, as it does when the file is loaded. Nothing is executed to find the value.

`DPL3011` covers the second argument of the ISO conversions. `atom_codes/2` and `number_codes/2`
need character codes, `atom_chars/2` and `number_chars/2` need characters, and `string_codes/2` and
`string_chars/2` accept either list but not an atom or a string:

```prolog
answer(N) :-
    number_codes(N, "42").      % DPL3011 under double_quotes=chars; use number_chars/2
```

`DPL3012` catches the failure that produces no error at all: a grammar written for character codes
receiving text read as characters, where it simply stops matching. A literal is checked when it is
the list handed to `phrase/2,3`, or a terminal in a grammar rule body. The grammar is followed from
there through the nonterminals the same file defines, and a code comparison anywhere along the way
is reported: an integer in a list terminal such as `[0'a]`, or — inside `{}/1` — an arithmetic
comparison, `between/3`, or `code_type/2` applied to a variable that a list terminal matched.

```prolog
digit(D) --> [D], { D >= 0'0, D =< 0'9 }.

main :-
    phrase(digit(_), "7").      % DPL3012: the grammar compares codes at line 1
```

Either rewrite the grammar over characters — `{ char_type(D, digit(_)) }` and `number_chars/2` —
or keep the file on code lists with `:- set_prolog_flag(double_quotes, codes).` at its top.
Grammars in other files, and text reaching a grammar through a variable, are not followed.

## Covington layout diagnostics

`--profile covington` adds these source-text checks:

| Diagnostic | Meaning |
|---|---|
| `DPL3003` | A tab character is present; use spaces |
| `DPL3004` | A clause continuation is not indented by a positive multiple of the configured indent |
| `DPL3005` | A line exceeds the configured maximum length |
| `DPL3006` | A clause exceeds the configured maximum number of lines |
| `DPL3007` | A comma is not followed by whitespace |
| `DPL3008` | A clause does not start on its own line at column one, or a rule body shares its head's line |
| `DPL3009` | Consecutive conjunction subgoals share a line |
| `DPL3010` | A line ends in spaces or tabs |

The preset uses a four-space indentation unit, an 80-character line limit, and a 24-line clause
limit. `--indent-size`, `--max-line-length`, and `--max-clause-lines` accept positive integers.

A numeric option also enables that individual check when the profile is `semantic`. Options may
appear in any order. Comma analysis ignores quoted text and comments; all diagnostics retain exact
source locations, including in CRLF files.

The command does not perform ambient module lookup, restore packages, or resolve library aliases.
Name every module file whose definitions should participate in cross-file analysis. Included files
are read only through `include/1` expansion.

The layout profile reports warnings only. It does not rewrite source. Naming conventions, comment
quality rules, general mode, determinism, or termination inference, and automatic fixes are not
implemented. DotProlog does not yet retain the comments and exact whitespace needed for a lossless formatter.
