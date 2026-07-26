# DotProlog

A Prolog language implementation for .NET 10, written in C# 14.

The goal is a first-class Prolog experience on the .NET SDK, the way C# and F# have one: `.dplproj` projects, a `plc` compiler, `dotnet prolog`, `dotnet new` templates, and NativeAOT publishing.

**Status: early.** The runtime-consult path works end to end — Prolog source is read, compiled to bytecode, and executed by the engine. The build-time path that generates C#, the MSBuild SDK, templates, and packaging are not built yet.

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

## Repository layout

| Path | What it is |
|---|---|
| `src/Prolog.Syntax` | Lexer, ISO operator table, operator-precedence reader, diagnostics |
| `src/Prolog.Runtime` | Tagged terms, heap, trail, choice points, bytecode VM, builtins |
| `src/Prolog.Compiler` | Clause and directive lowering to bytecode, consult and embedding API |
| `src/Prolog.DotNetTool` | The `dotnet prolog` command |
| `tests/` | Unit tests per component, plus end-to-end execution tests |
| `benchmarks/` | BenchmarkDotNet suite for the reader, compiler, and engine |
| `samples/HelloProlog` | The sample above |

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

The engine owns its control state: heap, trail, environment stack, choice-point stack, and argument registers are plain arrays, and Prolog calls are jumps inside a single dispatch loop. Prolog recursion depth therefore does not consume CLR stack, and failure is a return value rather than an exception. Last-call optimisation makes tail recursion run at constant stack depth.

## What the language supports today

Facts and rules; unification; backtracking; cut; conjunction; atoms, variables, integers, floats, strings, lists, and structures; integer and float arithmetic through `is/2` and the arithmetic comparisons; `write/1`, `writeq/1`, `writeln/1`, `nl/0`, `=/2`, `true/0`, `fail/0`, `halt/0`, `halt/1`; `:- Goal` directives and `:- initialization(Goal)`.

Not yet implemented, and reported as a `DPL1002` diagnostic rather than silently accepted: `;/2`, `->/2`, `\+/1`, `call/1`, modules, DCGs, exceptions, streams, dynamic predicates, and `findall/3`.

DotProlog does not claim ISO or SWI-Prolog compatibility. It will not claim it until published conformance tests verify it.

## Diagnostics

Diagnostic identifiers are stable and product-specific: `DPL0xxx` from the reader, `DPL1xxx` from the compiler.

```text
hello.pl(4,12): error DPL0005: Expected '.' to end the clause but found 'b'.
```

## Requirements

.NET SDK 10.0 or later. Everything else restores from NuGet.
