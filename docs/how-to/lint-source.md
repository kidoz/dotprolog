# Lint source locally and in CI

Use this guide to check existing `.pl` files without executing their directives or initialization
goals. Commands below use the tool from a repository checkout with the .NET 10 SDK installed;
run them from its root. Replace `program.pl` and `library.pl` with your source paths.

## Check source

```console
dotnet run --project src/DotProlog.Tool -- lint program.pl library.pl
```

Review diagnostics at their reported file, line, and column. For an intentionally unused named
variable, prefix its name with an underscore; the anonymous variable `_` is always exempt.
See the [diagnostic reference](../linting.md#semantic-diagnostics) for each rule.

Named files form one analysis unit, so list the modules whose calls should resolve together.
Modern `use_module/1,2` imports resolve among those files. `include/1` is expanded relative to the
including file without executing its contents. Unavailable imports and dynamic call targets remain
unknown rather than being reported as undefined.

## Review possibly unreferenced predicates

`DPL3034` reports an ordinary predicate when no recognized entry point reaches it through static
calls. Exports, non-module predicates, initialization goals, zero-arity `test_*` predicates, and
dynamic or multifile predicates are entry points. Calls through imports, DCGs, and declared
meta-predicate closures contribute reachability edges.

`DPL3036` applies the same advisory analysis to unreferenced grammar nonterminals, displayed as
`Name//Arity`. Exported, non-module, dynamic, and multifile nonterminals are roots. Grammar bodies,
`phrase/2,3`, and DCG `call/1` count as references. Mixed ordinary and grammar definitions sharing
one lowered predicate identity do not produce a nonterminal finding.

These findings are refactoring hints, not proof that deletion is safe. If missing source, unresolved
calls, runtime goal construction, database mutation, conditional compilation, or source expansion
could hide call edges, the linter suppresses `DPL3034` and `DPL3036` for the whole analysis unit
while retaining other diagnostics.

## Review possibly unused imports

`DPL3035` reports imported predicates and nonterminals without static references in the importing
module. A selective `use_module/2` or `import/2` receives one warning per unused indicator. A
whole-module `use_module/1` or `import/1` receives one warning only when none of its bindings is
used. References through DCGs, meta-closures, initialization goals, and otherwise dead code count;
explicitly qualified calls and usage by other modules do not.

Reexports are excluded. Imports whose sources contain reader or initialization side effects are also
excluded, including transitive effects through other imports. Incomplete static analysis suppresses
the rule. A finding is an advisory hint, not a certificate that removing the import is safe.

## Review conflicting imports

`DPL3047` runs in the default semantic profile. It reports each later import binding rejected because
a different module already supplied the same lowered predicate or nonterminal identity. The warning
identifies the importer, earlier and later source modules, and rejected identity.

Selective findings occur at the rejected indicator; whole-import findings occur at the module or file
argument. Repeated same-source imports, local-definition overrides, and `user` fallback do not
conflict. Reexports follow the same first-accepted-binding rule. Unknown modules and exports remain
unknown without hiding independently resolved conflicts.

List all relevant source files in one command so the linter can resolve these bindings without
execution:

```console
dotnet run --project src/DotProlog.Tool -- lint main.pl module-a.pl module-b.pl
```

## Review missing exports

`DPL3051` runs in the default semantic profile. List the importing file and its source modules in one
command so selective imports can be checked without execution:

```console
dotnet run --project src/DotProlog.Tool -- lint --warnings-as-errors main.pl library.pl
```

The rule reports a `use_module/2`, `import/2`, or `reexport/2` binding whose lowered `name/arity`
identity is absent from the resolved source module's known exports. The warning is located at the
requested indicator and identifies both modules. Repeated requests from one source are deduplicated;
whole-module imports are excluded.

DCG requests retain `Name//Arity` notation while comparison uses the two-argument-lowered identity.
An ordinary export with that identity is not missing; source-kind confusion remains `DPL3031`.
Unknown modules, unresolved files, malformed requests, and malformed or incomplete export surfaces
produce no absence claim and do not hide independent findings. Failed reexports and their downstream
selective imports can each be reported. Missing bindings do not also produce `DPL3047` or `DPL3048`.
No source is executed, consulted, or rewritten.

## Review local import shadowing

`DPL3048` is disabled by default because deliberately replacing an imported binding with a local
procedure is common. Enable it explicitly and list the importing and imported source files:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3048 main.pl library.pl
```

The rule reports a resolved import whose predicate or DCG nonterminal identity is hidden by a local
clause, `dynamic` declaration, or `multifile` declaration. It locates the finding at the first local
clause name, or at the first eligible declaration when no clause exists. Local definitions take
priority regardless of whether they occur before or after the import.

Repeated imports from one source produce one finding per lowered identity; different hidden sources
remain separate. Selective and whole imports and reexports are covered. Unresolved modules and
exports produce no shadow finding, and local rejection does not also produce `DPL3047`. This is a
review hint, not a recommendation to remove an import or rename a working local procedure. No source
is executed or rewritten.

## Review predefined identity collisions

`DPL3049` is disabled by default because intentionally replacing a bundled library predicate can be
valid. Enable it explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3049 program.pl
```

The rule reports a local static, dynamic, or multifile procedure whose lowered identity matches a
native built-in or bundled Prolog library predicate. It covers ordinary predicates, DCG nonterminals,
modules, `user`, and included source. One finding per module and identity is located at the first
local clause name, or at the first eligible declaration when no clause exists.

The warning identifies a collision; it does not claim that the local definition always wins. Some
native redefinitions are ignored, and some module or grammar redefinitions are rejected. Imports and
reexports do not repeat the finding at each importing module. Review actual call resolution before
renaming or deleting either definition. No source is executed or rewritten.

## Review inconsistent meta-predicate declarations

`DPL3050` runs in the default semantic profile. It reports the first later declaration whose argument
modes disagree with the first declaration for the same module and `name/arity`:

```console
dotnet run --project src/DotProlog.Tool -- lint --warnings-as-errors program.pl
```

Modern `meta_predicate/1` and ISO `metapredicate/1` are supported across named files and includes.
The comparison preserves goal-zero, positive closure counts, and module-sensitive arguments. Modern
`:` is not treated as `0`, and ISO `:` and `*` remain distinct. Different ordinary Modern markers are
equivalent and do not produce a consistency warning.

The finding identifies the earlier and later templates without claiming either is selected. Imports
and reexports do not merge declaration histories. Exact duplicates remain `DPL3025`, while unresolved
calls and dynamic or multifile procedures do not hide explicit conflicts. No source is executed or
rewritten.

## Check leading fact outputs against a .NET contract

`DPL3052` runs by default. Pair each `.dpli` file explicitly with a named `.pl` file having the same
base name:

```console
dotnet run --project src/DotProlog.Tool -- lint --contract rules.dpli rules.pl
```

The rule reports a ground output in the first static fact that cannot be converted by the generated
façade's marshaller. Every declared input must also be ground and compatible with the typed input.
Findings are located at the incompatible output term and name the predicate, output, expected type,
actual runtime shape, and contract file.

Later facts, leading rules, variable or open outputs, reader-dependent text, incompatible or variable
inputs, mixed grammar definitions, imported-only definitions, dynamic or multifile targets, and
effectful modules are not established and remain silent. Output `float` accepts every numeric runtime
representation; input `float` requires a float. Output `integer` excludes big-integer representations.
Proper lists are checked recursively. No finding is not a proof of whole-predicate contract consistency.

A missing or ambiguous source pairing returns `64`; contract reader errors return `65`. SDK façade
generation reports the same warning through MSBuild. No source is consulted, executed, or rewritten.

## Check reachable later fact outputs

`DPL3053` is opt-in and does not run during SDK façade generation. Enable it with the same explicit
contract pairing:

```console
dotnet run --project src/DotProlog.Tool -- lint \
    --enable-rule DPL3053 --contract rules.dpli rules.pl
```

The rule analyzes only later facts in fully ground, reader-independent static fact tables. Every
declared input must be compatible with façade construction, and every argument in the table must be
ground. Deterministic and semi-deterministic exports require that no earlier input head unifies with
the candidate invocation. Streaming exports reach later facts during complete enumeration. Ground
unification uses ordinary Prolog term semantics, not arithmetic equality.

Rules, cuts, grammar definitions, variables, open or reader-dependent terms, incompatible inputs,
dynamic or multifile targets, imported or ambiguous definitions, and effectful modules remain silent.
`DPL3052` continues to analyze the first fact, so enabling both rules does not duplicate first-fact
findings. Silence is not a proof of whole-predicate correctness.

## Check for possible non-steadfast clauses

Enable the non-steadfast heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3037 program.pl
```

`DPL3037` is off in every default profile. It reports ordinary clauses and grammar rules where two
distinct head arguments share a named variable and the body contains a cut in the same clause scope.
Nested head terms are inspected, but difference-list arguments added by DCG lowering are not.
Multifile clauses are excluded; unrelated unresolved calls do not suppress this file-local rule.

For example, the first clause below can incorrectly accept a smaller maximum, while the second
pattern may be an intentional commitment to unification. Both match the heuristic and need review:

```prolog
max(X, Y, X) :- X >= Y, !.
same(X, X) :- !.
```

The warning is not a proof of non-steadfastness or a recommendation to remove every cut. Cuts in
if-then-else conditions, negation, all-solutions goals, and meta-calls have different scopes and are
not treated as clause cuts by this rule.

## Check direct non-tail recursion

Enable the recursion heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3038 program.pl
```

`DPL3038` is off in every default profile. It reports each direct self-call with remaining control
flow that prevents the compiler's last-call optimization. A call in the last conjunction position is
tail-recursive; disjunction and then/else branches have independent tail positions. Conditions are
never tail positions, and a following goal or cut makes a call non-tail even when that goal looks
redundant.

The rule covers ordinary predicates and direct DCG nonterminal recursion, including explicit
qualification by the current module. Indirect and mutual recursion, other modules, negation,
all-solutions goals, cleanup goals, and meta-calls are excluded. Dynamic and multifile source clauses
are eligible.

This is an optimization hint, not a correctness warning or a termination proof. Review whether an
accumulator or another formulation would help before rewriting working code. Opt-in rules can be
enabled together by repeating `--enable-rule` with their diagnostic identifiers.

## Check cuts in multifile clauses

Enable the multifile-cut heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3039 program.pl
```

`DPL3039` is off in every default profile. It reports one warning per multifile clause at its first
same-scope cut. Ordinary predicates and grammar nonterminals are supported, and declarations are
recognized in the current file, in other named files, and through includes. Clauses that occur in
several files without a `multifile` declaration are not enough to trigger the rule.

The warning says that a cut may discard alternatives contributed by other source units. It is a
modularity review hint, not a correctness proof or a recommendation to remove every such cut.
Dynamic multifile clauses are eligible, and unrelated unresolved calls do not suppress the rule.
The same compiler-aware cut scopes as `DPL3037` apply, so opaque and meta-called cuts are excluded.

## Check repeat loops without a same-scope cut

Enable the repeat-loop heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3040 program.pl
```

`DPL3040` is off in every default profile. It reports one warning per ordinary clause or grammar rule
at the first direct `repeat/0` when no cut occurs in that clause's own cut scope. A same-scope cut
anywhere in the written body suppresses the finding; the rule does not prove that the cut is
reachable or correctly placed.

Direct control flow and DCG `{}` goals are inspected. Negation, all-solutions and cleanup goals,
meta-calls, explicitly qualified goals, and term data are not. Static, dynamic, and multifile source
clauses are eligible. The warning is a backtracking review hint, not proof of non-termination or a
recommendation to add a cut.

## Check final cuts in last supplied clauses

Enable the final-cut heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3041 program.pl
```

`DPL3041` is off in every default profile. It reports each same-scope cut in a final control-flow
position of the last supplied static clause for a predicate or nonterminal. Final positions include
the rightmost conjunction and independent disjunction and then/else branches; conditions and earlier
goals are not final.

The last clause is selected across named files and includes using lowered predicate identity.
Dynamic and multifile procedures are excluded because other clauses may follow at execution time;
static discontiguous procedures remain eligible. The warning asks whether committing the remaining
choice points is intentional. It does not prove that the cut is redundant or recommend removing it.

## Check broad catch handlers

Enable the catch-all heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3042 program.pl
```

`DPL3042` is off in every default profile. It reports each direct `catch/3` whose catcher is an
anonymous or named variable, located at that variable. The warning says the catcher may match every
thrown term; it does not assume the variable is unbound. A previously assigned or shared variable is
therefore still reported as a syntactic review site.

Atoms and compound catcher patterns are excluded. Direct control flow, DCG `{}` goals, and the goal
and recovery arguments of nested `catch/3` calls are inspected; opaque and arbitrary meta-called goals
are not. This is a broad-recovery review hint, not a correctness finding or a recommended rewrite.

## Check conditionals without an explicit else

Enable the missing-else heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3043 program.pl
```

`DPL3043` is off in every default profile. It reports each direct `->/2` or `*->/2` conditional that
is not the left operand of its enclosing `;/2`. Any explicit else goal, including `true` or `fail`,
suppresses the outer finding; nested conditionals are still reviewed independently. Findings are
located at the conditional and identify the containing predicate or nonterminal.

Direct control flow and DCG `{}` goals are inspected. Negation, all-solutions and cleanup goals,
meta-calls, qualified goals, and term data are not. The warning asks whether condition-failure
behavior is intentional; it does not claim that an else branch is required or rewrite the source.

## Check predicate and nonterminal names

Enable the naming heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3044 program.pl
```

`DPL3044` is off in every default profile. It reports user-defined predicate and DCG nonterminal
names outside lower `snake_case`. Names must start with an ASCII lowercase letter and contain only
lowercase letters, digits, and single underscores between nonempty segments. Quoting does not change
the decoded name being checked.

Facts, rules, dynamic and multifile definitions, modules, and includes are supported. Only the first
supplied clause of each lowered procedure identity produces a finding. Declaration-only procedures,
symbolic operator-style names, and `$`-prefixed private names are excluded. This is a convention
review hint, not invalid-Prolog diagnostic or automatic rename.

## Check predicate and nonterminal arity

Enable the excessive-arity heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3045 program.pl
```

`DPL3045` is off in every default profile. It reports user-defined predicates and DCG nonterminals
whose source arity exceeds `4`. DCGs use their written `Name//Arity`, not the two arguments added by
lowering. Only the first supplied definition of each lowered procedure identity produces a finding.

Override the limit with a positive integer:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3045 --max-predicate-arity 6 program.pl
```

The option is inert without the rule; repeating it uses the last value. Zero, negative, malformed,
overflowing, and missing values are usage errors. This is an interface-review hint, not a correctness
finding or a recommendation to regroup every argument list.

## Check clause subgoal counts

Enable the excessive-subgoal-count heuristic explicitly:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3046 program.pl
```

`DPL3046` is off in every default profile. It reports rules with more than `8` direct subgoals.
Conjunctions, disjunctions, conditionals, and DCG `{}` wrappers add no count themselves; their
branches are counted. Every other direct goal counts once, while goal-shaped arguments remain opaque.
Each clause is assessed independently, including multiple clauses of one procedure.

Override the limit with a positive integer:

```console
dotnet run --project src/DotProlog.Tool -- lint --enable-rule DPL3046 --max-clause-subgoals 12 program.pl
```

The option is inert without the rule, and the last repeated value wins. This is a written-body-size
review hint, not an execution-cost estimate, complexity score, or automatic decomposition proposal.

## Fail CI on warnings

```console
dotnet run --project src/DotProlog.Tool -- lint --warnings-as-errors program.pl library.pl
```

A warning now produces exit code `1`; a clean run produces `0`. Preserve the command's exit code
in your CI step. Without this option, warnings are advisory. Other failures have distinct
[exit codes](../linting.md#exit-codes).

## Check layout

Enable the Covington profile:

```console
dotnet run --project src/DotProlog.Tool -- lint --profile covington --warnings-as-errors program.pl
```

To use your project's limits, supply positive integers:

```console
dotnet run --project src/DotProlog.Tool -- lint --profile covington --indent-size 2 --max-line-length 100 --max-clause-lines 40 program.pl
```

The linter reports changes to make; it does not rewrite the source. See
[layout diagnostics](../linting.md#covington-layout-diagnostics) for defaults and limits.

## Match the source's language settings

```console
dotnet run --project src/DotProlog.Tool -- lint --mode strict-iso program.pl
dotnet run --project src/DotProlog.Tool -- lint --flag double_quotes=codes program.pl
```

These settings seed the reader. A strict-mode lint pass does not replace compiling or running the
program to enforce the strict language surface. See [configure language modes and flags](configure-language.md).

If you already have the packaged tool installed, replace
`dotnet run --project src/DotProlog.Tool -- lint` with `dotnet prolog lint` in these commands.
