# .NET integration

DotProlog offers two .NET-facing paths: embed the engine directly, or expose a `.dplproj` library
through a generated typed facade.

## Embed the engine

Create a `PrologEngine`, consult source, and pull solutions from a query:

```csharp
using DotProlog.Compiler;

var engine = new PrologEngine();
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

## Bind a predicate

`PrologHost` resolves a predicate once and exposes call shapes for common determinism modes:

```csharp
var host = new PrologHost(engine.Machine);
PrologPredicate discount = host.Bind("discount", 3);

PrologValue[]? result = host.CallOnce(
    discount,
    PrologInput.Float(100.0),
    PrologInput.Integer(10),
    PrologInput.Output);
```

- `Prove` is the semidet shape for a success/failure result.
- `CallOnce` returns one deterministic result.
- `CallAll` streams nondeterministic results.

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

A normal project reference is enough:

```xml
<ProjectReference Include="..\PricingRules\PricingRules.dplproj" />
```

The generated facade is ordinary .NET code, so C#, F#, and Visual Basic consume the same assembly.
The repository exercises all three languages against `samples/PricingRules`.

## NativeAOT behavior

NativeAOT applications may consult previously unseen `.pl` files at run time. Those predicates are
parsed and compiled into internal bytecode for the existing virtual machine. DotProlog does not use
runtime Roslyn, `Reflection.Emit`, dynamic assembly loading, or reflection-based predicate
discovery on the NativeAOT path.
