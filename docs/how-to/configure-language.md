# Configure language modes and flags

Use these settings when a program needs strict ISO validation or a different interpretation of
double-quoted text. The default is `modern` with `double_quotes=chars`. For the full contract, see
the [language reference](../language-guide.md#language-modes).

## Run a source file in strict mode

From the repository root:

```console
dotnet run --project src/DotProlog.Tool -- run --mode strict-iso path/to/program.pl
```

Replace the path with your file. A known predefined extension is rejected with `DPL1018` during
source compilation. User-defined predicates remain available. With the packaged tool installed,
use `dotnet prolog run` followed by the same options.

## Keep code-list text in Modern mode

For source written to read `"abc"` as `[97,98,99]`, run:

```console
dotnet run --project src/DotProlog.Tool -- run --flag double_quotes=codes path/to/program.pl
```

The former `extended` mode has been removed. Use `modern` with this flag override for its former
starting settings.

## Configure a project

Add a property group to your `.dplproj` to select strict mode:

```xml
<PropertyGroup>
  <DotPrologLanguageMode>strict-iso</DotPrologLanguageMode>
</PropertyGroup>
```

For Modern mode with code-list text, use:

```xml
<PropertyGroup>
  <DotPrologLanguageMode>modern</DotPrologLanguageMode>
  <DotPrologFlags>double_quotes=codes</DotPrologFlags>
</PropertyGroup>
```

Rebuild the project after changing these values. The generated code records the settings and
requires a matching engine. See the [SDK settings reference](../dotnet-integration.md#reference-prolog-from-another-net-language).

## Configure an embedded engine

Select strict mode before consulting source:

```csharp
var engine = new PrologEngine(PrologLanguageMode.StrictIso);
```

Use the `DotProlog.Compiler` and `DotProlog.Runtime` namespaces. The parameterless constructor
selects Modern mode. The engine's mode cannot be changed after construction.

## Change one source load unit

Place the directive before the clauses that need it:

```prolog
:- set_prolog_flag(double_quotes, codes).
```

This governs the rest of that load unit. Use the command-line or project override when every
source file should start with that value. See [language modes and text](../explanation/language-modes.md)
for how profiles and text representations relate.
