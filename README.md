# DotProlog

A Prolog language implementation for .NET 10, written in C# 14.

<https://github.com/kidoz/dotprolog>

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

| Area | Predicates |
|---|---|
| Terms | atoms, variables, integers, floats, double-quoted code lists, lists, structures |
| Control | `,/2`, `;/2`, `->/2`, `*->/2`, `\+/1`, `!/0`, `call/1`, `not/1`, `true/0`, `fail/0` |
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
```

Not yet implemented: modules, DCGs, exceptions (`throw/1`, `catch/3`), streams and file I/O, dynamic predicates, runtime `consult/1`, `findall/3`, `bagof/3`, `setof/3`, and `call/N` for N above one.

One known deviation: a cut inside a goal reached through `call/1` is local to that goal and prunes nothing in the meta-call, so `call((a, !, b))` behaves as `call((a, b))`.

DotProlog does not claim ISO or SWI-Prolog compatibility. It will not claim it until published conformance tests verify it.

## Diagnostics

Diagnostic identifiers are stable and product-specific: `DPL0xxx` from the reader, `DPL1xxx` from the compiler.

```text
hello.pl(4,12): error DPL0005: Expected '.' to end the clause but found 'b'.
```

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
