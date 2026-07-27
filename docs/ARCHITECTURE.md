# Architecture

DotProlog compiles Prolog to bytecode and runs it on a virtual machine written in C#. It never
emits CLR IL, so everything below works unchanged inside a NativeAOT binary.

## The shape of it

```
DotProlog.Syntax      lexer, operator-precedence reader, diagnostics
      |
      v
DotProlog.Compiler    clause lowering, module and DCG rewriting, the engine entry point
      |
      v
DotProlog.Runtime     cells, heap, trail, choice points, the dispatch loop, builtins
```

The arrows are the reference direction, with one deliberate exception: `DotProlog.Syntax`
references `DotProlog.Runtime` for the operator table alone. That table is shared language state —
`op/3` changes it at run time and the term writer reads it — so it lives in the base assembly
rather than being duplicated ([ADR 0010](../.agents/contexts/decisions/0010-operator-table-shared-by-reader-and-writer.md)).

The runtime never references the compiler. Where a running program needs to compile something —
`assertz/1`, `consult/1`, `read/1`, and lowering a meta-called control term — it goes out through
`IRuntimeCompiler`, an interface the runtime declares and the engine implements. That seam is what
keeps the runtime free of the parser while letting a running program parse.

## How a program executes

A term is a 64-bit `Cell`: a 4-bit tag and a 60-bit payload. Compound terms live on a heap of cells,
variables are heap cells that point at themselves until bound, and binding a variable that is older
than the newest choice point pushes its address onto the trail so backtracking can undo it.

The machine is a bytecode interpreter with argument registers, an environment stack for clause-local
variables, and a choice-point stack. A predicate call is a jump, not a CLR call, so recursion depth
is bounded by the heap rather than by the CLR stack — 200,000 tail-recursive iterations run at
constant stack depth, which a test pins. Last-call optimisation makes a deterministic tail call
reuse its environment ([ADR 0002](../.agents/contexts/decisions/0002-wam-derived-bytecode-engine.md)).

Control constructs are compiled in place inside a clause body, which is what gives cut its ISO
scoping. A control term reached through `call/1` is lowered at run time into an anonymous clause
whose entry barrier is the meta-call barrier, so cut behaves the same either way
([ADR 0015](../.agents/contexts/decisions/0015-runtime-lowering-for-meta-called-control.md)).

## What happens while loading

`ProgramLoader` makes two passes. The first settles which module the unit belongs to, what it
exports and imports, and every predicate it defines. The second rewrites and emits. Three source
rewrites happen there and nowhere else:

- **Grammar rules.** `-->/2` becomes an ordinary clause threading a difference list.
- **Modules.** A predicate in module `m` is compiled under the name `m:p`, and a call inside `m` to
  something `m` defines is rewritten to that name.
- **Operators.** An `:- op/3` directive is applied while reading, so the file that declares an
  operator can use it.

The compiler and the machine know about none of these. They see ordinary clauses.

## The standard library

Split by what needs the machine. `sort/2` is native because comparing terms is; `append/3` is
Prolog because it is two clauses that read better as such
([ADR 0009](../.agents/contexts/decisions/0009-standard-library-split.md)). Both libraries are
`const string` sources compiled at engine construction, which costs about 220 µs and needs nothing
on disk — the reason a NativeAOT binary carries them without a file beside it.

## Reaching it from .NET

Two paths, and they are different in kind.

**Embedding** is a library call: construct a `PrologEngine`, `Query` a goal, and pull solutions.
Each binding is marshalled into a `PrologValue` before backtracking can invalidate it
([ADR 0007](../.agents/contexts/decisions/0007-solution-enumeration-and-embedding.md)).

**A `.dplproj`** is a project whose sources are Prolog. `DotProlog.Sdk` is an additive MSBuild SDK:
the .NET SDK builds the project, and this one adds a build step that reads a `.dpli` contract and
generates a typed C# facade over the predicates it names. The output is an ordinary assembly, which
is why C#, F#, and Visual Basic can all consume it, and why the samples prove that rather than
assert it ([ADR 0006](../.agents/contexts/decisions/0006-dotnet-language-interop.md),
[ADR 0008](../.agents/contexts/decisions/0008-vanilla-dotnet-facade-conventions.md)).

## Why NativeAOT works

Nothing on the reachable graph uses `Reflection.Emit`, runtime Roslyn, dynamic assembly loading, or
reflection-based discovery. Builtins are registered by an explicit list rather than scanned for.
Compiling at run time means appending bytecode to a program the machine already holds, so a
published binary can consult a `.pl` file it has never seen and run it — as internal bytecode, not
as new CLR IL. An acceptance test publishes a sample natively, runs it outside the build output, and
checks it consults a file, enumerates solutions, changes its database, declares an operator, parses
with a grammar, and captures output.

## Where to look

| Concern | File |
|---|---|
| Term representation | `DotProlog.Runtime/Cell.cs` |
| The dispatch loop | `DotProlog.Runtime/Machine.cs` |
| Instruction set | `DotProlog.Runtime/OpCode.cs` |
| Builtin registration | `DotProlog.Runtime/CoreBuiltins.cs` |
| Clause lowering | `DotProlog.Compiler/ClauseCompiler.cs` |
| Loading, modules, DCGs | `DotProlog.Compiler/ProgramLoader.cs` |
| The engine entry point | `DotProlog.Compiler/PrologEngine.cs` |
| Facade generation | `DotProlog.CodeGen.CSharp/FacadeGenerator.cs` |
| SDK targets | `DotProlog.Sdk/Sdk/Sdk.targets` |

The decision records in `.agents/contexts/decisions/` explain why each of these is as it is.
