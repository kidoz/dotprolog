# Source linting

`dotnet prolog lint` analyzes Prolog source without consulting it or executing directives. It uses
the same reader, operator table, language modes, source spans, and diagnostic format as the compiler.

```console
dotnet prolog lint program.pl library.pl
```

Warnings are advisory by default, so a warning does not make the command fail. Use
`--warnings-as-errors` in CI:

```console
dotnet prolog lint --warnings-as-errors src/*.pl
```

Layout policy is opt-in. The `covington` profile implements the automatically checkable layout
guidelines 2.1 through 2.7 from *Coding Guidelines for Prolog*:

```console
dotnet prolog lint --profile covington program.pl
dotnet prolog lint --profile covington --warnings-as-errors src/*.pl
```

The default `semantic` profile preserves the source-local variable checks without imposing a
project style. Profile names are case-insensitive.

The exit codes are:

| Code | Meaning |
|---:|---|
| `0` | Every named file was read; warnings may have been reported |
| `1` | Warnings were found with `--warnings-as-errors` |
| `64` | The command line is invalid |
| `65` | A file is missing or unreadable, or the reader reported an error |

Select the source language mode with the same names accepted by `run`:

```console
dotnet prolog lint --mode strict-iso program.pl
dotnet prolog lint --mode modern grammar.pl
```

The mode seeds the reader state used by linting. `lint --mode strict-iso` does not replace compiling
or running a project when strict-surface enforcement is required.

## Semantic diagnostics

These rules run in every profile:

| Diagnostic | Meaning |
|---|---|
| `DPL3001` | An ordinary named variable occurs once in its clause |
| `DPL3002` | An underscore-prefixed singleton marker occurs more than once in its clause |

Prefix an intentionally unused variable with an underscore:

```prolog
head(_Ignored) :-
    true.
```

The anonymous variable `_` is always exempt because every occurrence denotes a fresh variable.

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
limit. Override those positive integer thresholds when a project has a different convention:

```console
dotnet prolog lint --profile covington --indent-size 2 program.pl
dotnet prolog lint --max-line-length 100 --max-clause-lines 40 program.pl
```

A numeric option also enables that individual check when the profile is `semantic`. Options may
appear in any order. Comma analysis ignores quoted text and comments; all diagnostics retain exact
source locations, including in CRLF files.

The command analyzes each named file independently. It does not resolve imports, expand included
files, or perform whole-program call analysis. Name those files explicitly when they should also be
linted.

The layout profile reports warnings only. It does not rewrite source. Naming conventions, comment
quality rules, predicate call analysis, and automatic fixes are not implemented in this phase.
DotProlog does not yet retain the comments and exact whitespace needed for a lossless formatter.
