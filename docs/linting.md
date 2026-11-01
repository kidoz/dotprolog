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

The finding says that a predicate may be unreferenced; it is not proof that deleting it is safe.
Nonterminals contribute edges and exported nonterminals are roots, but unreferenced nonterminals do
not receive findings. Unused imports are also outside this rule.

Reachability requires a complete static call graph. Unresolved includes or imports, unknown calls or
modules, variable goals, unsupported meta-closures, runtime database mutation or source loading,
conditional compilation, source expansion, and unavailable source suppress `DPL3034` for the entire
analysis unit. Other diagnostics still run. As with every lint rule, no clause, directive,
initialization goal, or expansion is executed.

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
quality rules, unused-import analysis, and automatic fixes are not implemented. DotProlog does not
yet retain the comments and exact whitespace needed for a lossless formatter.
