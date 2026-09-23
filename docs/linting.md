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
| `--profile semantic` | Default profile: source-local variable checks |
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

An underscore-prefixed name marks an intentionally unused variable:

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
limit. `--indent-size`, `--max-line-length`, and `--max-clause-lines` accept positive integers.

A numeric option also enables that individual check when the profile is `semantic`. Options may
appear in any order. Comma analysis ignores quoted text and comments; all diagnostics retain exact
source locations, including in CRLF files.

The command analyzes each named file independently. It does not resolve imports, expand included
files, or perform whole-program call analysis. Name those files explicitly when they should also be
linted.

The layout profile reports warnings only. It does not rewrite source. Naming conventions, comment
quality rules, predicate call analysis, and automatic fixes are not implemented in this phase.
DotProlog does not yet retain the comments and exact whitespace needed for a lossless formatter.
