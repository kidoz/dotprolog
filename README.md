# DotProlog

A Prolog language implementation for .NET 10, written in C# 14.

<https://github.com/kidoz/dotprolog>

The goal is a first-class Prolog experience on the .NET SDK, the way C# and F# have one: `.dplproj` projects, a `plc` compiler, `dotnet prolog`, `dotnet new` templates, and NativeAOT publishing.

**Status: early, but usable.** `dotnet new prolog-console` through `dotnet publish -p:PublishAot=true`
works today. What is missing: `dotnet test` on a `.dplproj`, the `plc` compiler, and generating IL for
predicate bodies — a `.dplproj` currently embeds its Prolog source and compiles it to bytecode at
startup. Nothing is published to NuGet: package identity is still an open decision.

## Hello, world

```prolog
% samples/HelloProlog/hello.pl
:- initialization(main).

main :-
    greeting(Greeting),
    write(Greeting),
    nl.

greeting('Hello! World!').
```

```console
$ dotnet run --project src/Prolog.DotNetTool -- run samples/HelloProlog/hello.pl
Hello! World!
```

Or, with [just](https://just.systems):

```console
$ just hello
```

## Calling Prolog from C#

```csharp
var engine = new PrologEngine();
engine.ConsultText("colour(red).  colour(green).  colour(blue).");

foreach (PrologSolution solution in engine.Query("colour(C)").Solutions())
{
    Console.WriteLine(solution["C"]);      // red, green, blue
}

bool ok = engine.Query("1 < 2").Prove();
```

Answers are produced on demand and marshalled into plain .NET objects as they arrive, so they stay valid after the query has moved on — and an unbounded goal is fine as long as you stop taking:

```csharp
var first = engine.Query("between(1, 1000000000, X)").Solutions().Take(4);
```

Predicates can also be called directly with .NET values. This is the surface the generated `.dplproj` facades sit on:

```csharp
var host = new PrologHost(engine.Machine);
PrologPredicate discount = host.Bind("discount", 3);

PrologValue[]? result = host.CallOnce(
    discount, PrologInput.Float(100.0), PrologInput.Integer(10), PrologInput.Output);
```

This works from F# and VB the same way. One engine runs one goal at a time and is not thread-safe.

## Prolog as a referenced project

`samples/PricingRules` is a `.dplproj` holding ordinary ISO Prolog plus a contract declaring its .NET
surface. `samples/PricingConsole` is a plain C# app that references it:

```xml
<ProjectReference Include="..\PricingRules\PricingRules.dplproj" />
```
```prolog
% pricing.dpli — modes and determinism live here, so pricing.pl stays portable
:- clr_module('Pricing').
:- clr_export(discount/3, det, [in(price, float), in(percent, integer), out(result, float)]).
:- clr_export(in_catalogue/1, semidet, [in(item, atom)]).
:- clr_export(bundle/2, nondet, [in(items, list(atom)), out(bundle, list(atom))]).
```
```csharp
IPricingModule pricing = PricingModule.Create();

pricing.Discount(100.0, 15);          // 85
pricing.InCatalogue("widget");        // true
foreach (var b in pricing.Bundle(["widget", "gadget"])) { /* streamed */ }
```

The facade is generated during the build, before the C# compiler runs. Nothing in the consuming code
mentions the engine, a goal, or a term — which is why the same reference works from F# and VB with no
extra work. That is checked rather than assumed: `samples/PricingFSharp` and
`samples/PricingVisualBasic` consume the same `.dplproj`, and the integration test builds and runs all
three.

The generated surface follows Microsoft's [F# component design
guidelines](https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines)
for "vanilla .NET" libraries: the `TryGetValue` pattern for `semidet` predicates with outputs, a named
record rather than a tuple for several outputs, collection interfaces rather than concrete types, null
checks at the boundary, and a `CancellationToken` on anything that streams. A predicate whose name
reads poorly in C# can be renamed in the contract, the equivalent of F#'s `[<CompiledName>]`:

```prolog
:- clr_export(nrev/2, det, [in(l, list(atom)), out(r, list(atom))], 'NaiveReverse').
```

```console
$ dotnet run --project samples/PricingConsole
100 less 15% = 85
total 1200 is gold
widget in catalogue: True
bundles of [widget, gadget]:
  [widget, gadget]
  [widget]
  [gadget]
  []
```

## Repository layout

| Path | What it is |
|---|---|
| `src/Prolog.Syntax` | Lexer, ISO operator table, operator-precedence reader, diagnostics |
| `src/Prolog.Runtime` | Tagged terms, heap, trail, choice points, bytecode VM, builtins |
| `src/Prolog.Compiler` | Clause and directive lowering to bytecode, consult and embedding API |
| `src/Prolog.CodeGen.CSharp` | `.dpli` contract reader and C# facade generator |
| `src/Prolog.Build.Tasks` | MSBuild task that runs the generator |
| `src/DotProlog.Sdk` | The `DotProlog.Sdk` MSBuild SDK package |
| `src/Prolog.Templates` | `dotnet new prolog-console` and `prolog-lib` |
| `src/Prolog.DotNetTool` | The `dotnet prolog` command |
| `tests/` | Unit tests per component, plus end-to-end execution tests |
| `benchmarks/` | BenchmarkDotNet suite for the reader, compiler, and engine |
| `samples/HelloProlog` | The Hello World sample |
| `samples/PricingRules` | A `.dplproj`: Prolog rules plus their `.dpli` contract |
| `samples/PricingConsole`, `samples/PricingFSharp`, `samples/PricingVisualBasic` | C#, F#, and VB apps referencing it |
| `samples/GreetingApp` | A Prolog application built from a `.dplproj` |
| `samples/AotAcceptance` | The NativeAOT acceptance sample |

## Common tasks

```console
just build          # build everything
just test           # run every test project
just format         # format all C# with CSharpier
just format-check   # fail if anything is unformatted
just check          # format-check + build + test
just run FILE.pl    # consult and run a Prolog file
just bench '*'      # run the benchmark suite
```

Without `just`, each recipe is a plain `dotnet` command — read the `justfile`.

## How it executes

Two paths are planned, and they are deliberately different:

```text
Build-time Prolog        : parser -> semantic IR -> generated C# -> Roslyn -> IL -> JIT/NativeAOT
Runtime consult / assert : parser -> semantic IR -> Prolog bytecode -> AOT-compatible bytecode VM
```

Only the second is implemented today. It never emits CLR IL, so it stays valid inside a NativeAOT process — runtime-loaded predicates execute as bytecode and are not turned into new machine code.

That is verified, not assumed. `samples/AotAcceptance` publishes to a self-contained native executable with no managed assemblies beside it, then at run time consults a `.pl` file it has never seen, enumerates solutions, asserts and retracts clauses, and catches an ISO error — with zero trimming or AOT warnings in the build. CI runs it on Windows, Linux, and macOS:

```console
$ DOTPROLOG_RUN_AOT_TESTS=1 dotnet test tests/Integration
```

Dynamic predicates use the logical update view, so a goal sees exactly the clauses that existed when it started:

```prolog
:- dynamic p/1.
p(1).
p(2).

% [1,2] — the asserted clauses are not visible to the goal that asserted them
?- findall(X, (p(X), assertz(p(9))), L).
```

The engine owns its control state: heap, trail, environment stack, choice-point stack, and argument registers are plain arrays, and Prolog calls are jumps inside a single dispatch loop. Prolog recursion depth therefore does not consume CLR stack, and failure is a return value rather than an exception. Last-call optimisation makes tail recursion run at constant stack depth.

## What the language supports today

| Area | Predicates |
|---|---|
| Terms | atoms, variables, integers, floats, double-quoted code lists, lists, structures |
| Control | `,/2`, `;/2`, `->/2`, `*->/2`, `\+/1`, `!/0`, `call/1`, `not/1`, `true/0`, `fail/0` |
| Exceptions | `throw/1`, `catch/3`, with catchable ISO `error/2` terms |
| All solutions | `findall/3`, `forall/2` |
| Database | `assertz/1`, `asserta/1`, `retract/1`, `clause/2`, `retractall/1`, `abolish/1`, `:- dynamic` |
| Ranges | `between/3` |
| Loading | `consult/1`, `ensure_loaded/1` at run time |
| Unification | `=/2`, `\=/2` |
| Arithmetic | `is/2`, `=:=/2`, `=\=/2`, `</2`, `>/2`, `=</2`, `>=/2` |
| Standard order | `==/2`, `\==/2`, `@</2`, `@>/2`, `@=</2`, `@>=/2`, `compare/3` |
| Term inspection | `functor/3`, `arg/3`, `=../2` |
| Type tests | `var/1`, `nonvar/1`, `atom/1`, `number/1`, `integer/1`, `float/1`, `atomic/1`, `compound/1`, `callable/1`, `is_list/1`, `ground/1` |
| Output | `write/1`, `writeq/1`, `print/1`, `writeln/1`, `nl/0` |
| Directives | `:- Goal`, `:- initialization(Goal)`, `halt/0`, `halt/1` |

Control constructs are compiled in place inside a clause body, so cut scopes the way ISO specifies: opaque in the condition of if-then-else, transparent in its branches, clause-scoped elsewhere. A bootstrap library written in Prolog makes the same constructs reachable when a goal is assembled at run time and passed to `call/1`.

```prolog
sign(N, S) :- ( N < 0 -> S = negative ; N =:= 0 -> S = zero ; S = positive ).

safe_divide(X, Y, R) :-
    catch(R is X / Y, error(evaluation_error(zero_divisor), _), R = undefined).

item(1).
item(2).
item(3).

squares(L) :- findall(S, (item(N), S is N * N), L).   % L = [1,4,9]
```

Every error the engine raises is a catchable `error(Formal, Context)` term, so `existence_error`, `type_error`, `instantiation_error`, and `evaluation_error` can all be handled in Prolog rather than aborting the run.

Not yet implemented: modules, DCGs, streams and file I/O, `bagof/3`, `setof/3`, `copy_term/2`, and `call/N` for N above one.

One known deviation: a cut inside a goal reached through `call/1` is local to that goal and prunes nothing in the meta-call, so `call((a, !, b))` behaves as `call((a, b))`.

DotProlog does not claim ISO or SWI-Prolog compatibility. It will not claim it until published conformance tests verify it.

## Diagnostics

Diagnostic identifiers are stable and product-specific: `DPL0xxx` from the reader, `DPL1xxx` from the compiler.

```text
hello.pl(4,12): error DPL0005: Expected '.' to end the clause but found 'b'.
```

## Starting a project

```console
$ dotnet new install Prolog.Templates
$ dotnet new prolog-console -n HelloProlog
$ dotnet run --project HelloProlog
Hello from Prolog on .NET!
```

`prolog-console` builds a Prolog program as a .NET application; `prolog-lib` builds a rule set as a
typed library for C#, F#, and VB. A project pins the SDK on the element itself, so no `global.json` is
needed:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="DotProlog.Sdk" Version="1.0.0" />
```

A `.dplproj` publishes with NativeAOT like any other project:

```console
$ dotnet publish HelloProlog -c Release -r osx-arm64 -p:PublishAot=true
```

**Nothing is published to NuGet yet** — package identity is still an open decision. The commands above
are verified against a local feed built by `dotnet pack`.

## Building from source

```console
$ git clone https://github.com/kidoz/dotprolog.git
$ cd dotprolog
$ just check          # format-check, build, and test
```

.NET SDK 10.0 or later is the only requirement; everything else restores from NuGet.

## Licence

MIT — see [LICENSE](LICENSE).

Author: Aleksandr Pavlov &lt;ckidoz@gmail.com&gt;
