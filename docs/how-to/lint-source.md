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

The command reads each named file independently. Name imported and included files explicitly
when they should also be checked.

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
