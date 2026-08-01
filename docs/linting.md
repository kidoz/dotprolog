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

The mode seeds the reader state used by linting. The current rules are source-local variable rules;
`lint --mode strict-iso` does not replace compiling or running a project when strict-surface
enforcement is required.

## Diagnostics

The initial rules are deliberately conservative:

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

The command analyzes each named file independently. It does not resolve imports, expand included
files, or perform whole-program call analysis. Name those files explicitly when they should also be
linted.

Formatting rules and automatic fixes are not part of the current linter. DotProlog does not yet
retain the comments and exact whitespace needed for a lossless formatter.
