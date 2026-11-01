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
