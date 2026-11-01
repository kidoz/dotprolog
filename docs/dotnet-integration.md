# .NET integration reference {#net-integration}

DotProlog offers an embedded engine and generated typed facades over `.dplproj` libraries. This
page describes their contracts. For complete procedures, see [embed Prolog in C#](how-to/embed-engine.md),
[create a typed library](how-to/create-library.md), and [reference a Prolog project](how-to/reference-project.md).

## Embed the engine

`ConsultText` returns load diagnostics and a success flag. Check it before querying.
`Query(...).Solutions()` lazily enumerates named bindings. A minimal successful query is:

```csharp
using DotProlog.Compiler;
using DotProlog.Runtime;

using var engine = new PrologEngine(PrologLanguageMode.StrictIso);
engine.ConsultText("colour(red). colour(green). colour(blue).");

foreach (PrologSolution solution in engine.Query("colour(C)").Solutions())
{
    Console.WriteLine(solution["C"]);
}

bool trueStatement = engine.Query("1 < 2").Prove();
```

Each binding is marshalled into a `PrologValue` before the engine backtracks, so returned values
remain valid while the query advances. Solutions are lazy; callers may stop enumerating an
unbounded goal.

One engine runs one goal at a time and is not thread-safe. Use separate engine instances when
independent callers need to execute concurrently.

The parameterless constructor selects `PrologLanguageMode.Modern`, where double-quoted text reads
as a list of characters. Pass `PrologLanguageMode.StrictIso` before consulting any source to
restrict predefined features to the ISO Parts 1–3 inventory, or pass a `PrologFlagOverrides` to
seed another initial `double_quotes` value. The selection is immutable for the lifetime of the
engine.

## Bind a predicate

`PrologHost.Bind` resolves a predicate once. The predicate must already be loaded into the
engine; this example assumes `discount/3` from the library below:

```csharp
var host = new PrologHost(engine.Machine);
PrologPredicate discount = host.Bind("discount", 3);

PrologValue[]? result = host.CallOnce(
    discount,
    PrologInput.Float(100.0),
    PrologInput.Integer(10),
    PrologInput.Output);
```

| Method | Result |
|---|---|
| `Prove` | Success/failure, ignoring outputs |
| `CallOnce` | First solution's output values, or `null` on failure; later solutions are discarded |
| `CallAll` | Lazy sequence of output-value arrays, one per solution |

These methods do not validate a determinism declaration. Generated facades select the call shape
from the `.dpli` contract.

## Define a `.dplproj` library

A library contains portable Prolog source and a `.dpli` contract that describes its .NET surface.
The Prolog module contains no CLR-specific declarations:

```prolog
:- module(pricing, [discount/3, in_catalogue/1]).

discount(Price, Percent, Result) :-
    Result is Price * (100 - Percent) / 100.

in_catalogue(widget).
in_catalogue(gadget).
```

The contract assigns CLR types, argument modes, and determinism:

```prolog
:- clr_module('Pricing').
:- clr_export(
    discount/3,
    det,
    [in(price, float), in(percent, integer), out(result, float)]
).
:- clr_export(in_catalogue/1, semidet, [in(item, atom)]).
```

The SDK generates an interface and implementation during the build:

```csharp
IPricingModule pricing = PricingModule.Create();

double result = pricing.Discount(100.0, 15);
bool found = pricing.InCatalogue("widget");
```

## Reference Prolog from another .NET language

Consumers reference a `.dplproj` as an ordinary project:

```xml
<ProjectReference Include="..\PricingRules\PricingRules.dplproj" />
```

The generated facade is ordinary .NET code, so C#, F#, and Visual Basic consume the same assembly.
The repository exercises all three languages against `samples/PricingRules`.

### SDK language settings

| Property | Values and default |
|---|---|
| `DotPrologLanguageMode` | `modern` (default) or `strict-iso` |
| `DotPrologFlags` | Semicolon-separated `name=value` pairs overriding curated initial flags |

See [configure language modes and flags](how-to/configure-language.md) for project XML and CLI
commands, and the [language reference](language-guide.md#language-modes) for the mode contract.

The override becomes the value every source file starts from — and returns to when a
`set_prolog_flag/2` directive's load unit ends. The overridable flags are curated:
`double_quotes` (`codes`, `chars`, `atom`, and — outside strict ISO mode — `string`) is available
today; the three ISO values work in every mode. The same overrides are available through the CLI and engine constructor.

Strings are interned beside atom text and live for the program's lifetime: like atoms, they are
never reclaimed, which is worth knowing for a long-running host that mints unbounded distinct
strings. Marshalled string terms reach .NET as `PrologString` values.

Generated code records its language mode and initial `double_quotes` value and refuses to install
into an engine that starts elsewhere. This keeps build-time validation and runtime consultation
under the same language contract.

## Engine lifetime and memory

Reuse an engine for repeated calls over a bounded vocabulary. Synchronous `PrologHost.Prove` and
`CallOnce` accept `params ReadOnlySpan<PrologInput>`; on C# 14, expanded arguments select this
allocation-saving overload. Existing array overloads remain available. `CallAll` continues to use
arrays for lazy enumeration. Detached output values still allocate as needed.

The heap is reset between top-level calls, but the program has a longer lifetime:

- Distinct atoms, string texts, floats, big integers, and rationals stay interned. Arithmetic can
  create these entries even when a result is discarded. `SymbolTable.AtomCount`, `FloatCount`,
  `BigIntegerCount`, and `RationalCount` expose their counts; atom and string text share a table.
- Retraction retains clauses needed by suspended calls under logical update view. Batched collection
  unlinks dead clause records after their last protected generation is released. A long-running
  top-level call conservatively protects all clauses visible when it began.
- The built-in compiler also recycles privately owned bytecode and constant-pool ranges for
  retracted asserted and runtime-loaded dynamic clauses. Live code stays at its original address.
  Allocated arrays retain peak capacity; an old retained call can delay reuse.
- Static code, host-compiled goals, public `IRuntimeCompiler.CompileClause` results, and custom
  compiler output stay pinned. Build-time C#/IL methods also retain their existing lifetime.
  Repeated query compilation can therefore still grow a program.

`BytecodeProgram.CodeLength` and `ConstantCount` expose the current code and constant append
positions, including reusable gaps. Private ranges released from the tail can reduce these counts;
they do not count cumulative historical emission or distinct values.

For services processing unbounded distinct input, use a fresh engine for each request or a bounded
batch, reinstall the static program, and carry application state through detached `PrologValue`
values or an external store. A request limit or a measured memory/count threshold can trigger rotation.
Finish or abandon enumeration before rotation. Dispose the old engine and release references to it,
its host, queries, machine, and program so the GC can collect them; disposal alone closes streams.
Cells, predicate handles, and compiled goal addresses belong to their original program.

For reproducible growth and lookup measurements, run the benchmark project's `--profile-churn`
command described in [runtime performance measurements](explanation/runtime-performance.md).

## NativeAOT behavior

NativeAOT applications may consult previously unseen `.pl` files at run time. Those predicates are
parsed and compiled into internal bytecode for the existing virtual machine. DotProlog does not use
runtime Roslyn, `Reflection.Emit`, dynamic assembly loading, or reflection-based predicate
discovery on the NativeAOT path.

See [publish with NativeAOT](how-to/publish-nativeaot.md) for publishing and acceptance checks.
