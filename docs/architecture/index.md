# Runtime architecture

DotProlog parses Prolog into an intermediate representation, compiles it to bytecode, and executes
that bytecode on a virtual machine written in C#. Runtime compilation never emits CLR IL, which
makes the same path available inside a NativeAOT executable.

## Component flow

```text
DotProlog.Syntax
    lexer, operator-precedence reader, diagnostics
        |
        v
DotProlog.Compiler
    clause lowering, module and DCG rewriting, engine entry point
        |
        v
DotProlog.Runtime
    cells, heap, trail, choice points, dispatch loop, builtins
```

The arrows show the primary reference direction. `DotProlog.Syntax` also references
`DotProlog.Runtime` for the operator table. That table is shared language state: `op/3` changes it
at run time and the term writer reads it.

The runtime does not reference the compiler. When a running program needs to compile a term for
`assertz/1`, `consult/1`, term input, or a meta-call, it uses `IRuntimeCompiler`, an interface
declared by the runtime and implemented by the compiler layer.

## Term representation

A term is a 64-bit `Cell` with a 4-bit tag and a 60-bit payload. Compound terms live on a heap of
cells. A fresh variable is a heap cell that refers to itself; binding changes that reference.

When a variable predates the newest choice point, binding it records the cell address on the trail.
Backtracking restores the heap and unwinds the trail to undo those bindings.

## Execution machine

The bytecode interpreter owns:

- argument registers;
- an environment stack for clause-local variables and continuations;
- a choice-point stack for alternatives;
- a trail for reversible bindings; and
- an explicit bytecode instruction pointer.

A Prolog call is a jump in one dispatch loop, not a recursive CLR method call. Tail calls can reuse
their environments, so deterministic tail recursion runs at constant CLR stack depth. Ordinary
failure is control state, not an exception.

Control constructs compile inline in the containing clause. This preserves ISO cut scope. A
control term reached through `call/1` is lowered at run time with a meta-call barrier so the same
scope rules apply.

## Loading and rewriting

`ProgramLoader` first establishes the unit's module, imports, exports, and defined predicates. A
second pass rewrites and emits clauses. Language-level rewrites happen in the loader:

- A DCG rule using `-->/2` becomes an ordinary clause that threads a difference list.
- A predicate defined inside module `m` receives the qualified name `m:p`, and internal calls are
  resolved against module definitions and imports.
- An `op/3` directive is applied while the file is read, allowing later terms in that file to use
  the new operator.

The clause compiler and virtual machine receive ordinary resolved clauses after these rewrites.

## Standard library

The standard library is split by what requires direct machine access. Term sorting is native
because it depends on the runtime's standard-order comparison; list relations such as `append/3`
are Prolog because their declarative definitions are clearer.

Both portions are embedded source compiled when an engine is created. A deployed NativeAOT
application therefore needs no separate standard-library files.

## SDK and generated facades

`DotProlog.Sdk` is additive to the .NET SDK. A `.dplproj` build embeds Prolog source and reads a
`.dpli` contract to generate a typed C# facade. The result is an ordinary assembly consumable by
C#, F#, and Visual Basic.

Predicate bodies still execute as DotProlog bytecode. The planned build-time path that generates C#
for predicate bodies is not implemented:

```text
Planned build time : parser -> semantic IR -> generated C# -> Roslyn -> IL
Implemented runtime: parser -> semantic IR -> bytecode -> DotProlog VM
```

## NativeAOT constraints

No NativeAOT-reachable path uses:

- `Reflection.Emit`;
- runtime Roslyn compilation;
- dynamic assembly loading; or
- reflection-based predicate discovery.

Builtins use explicit registration. Runtime consultation appends bytecode to the existing program,
so a native executable can load and execute a `.pl` file it did not see during publish.

## Code map

| Concern | Source |
|---|---|
| Term representation | [`src/DotProlog.Runtime/Cell.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Runtime/Cell.cs) |
| Dispatch loop | [`src/DotProlog.Runtime/Machine.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Runtime/Machine.cs) |
| Instruction set | [`src/DotProlog.Runtime/OpCode.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Runtime/OpCode.cs) |
| Builtin registration | [`src/DotProlog.Runtime/CoreBuiltins.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Runtime/CoreBuiltins.cs) |
| Clause lowering | [`src/DotProlog.Compiler/ClauseCompiler.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Compiler/ClauseCompiler.cs) |
| Loading, modules, and DCGs | [`src/DotProlog.Compiler/ProgramLoader.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Compiler/ProgramLoader.cs) |
| Engine entry point | [`src/DotProlog.Compiler/PrologEngine.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Compiler/PrologEngine.cs) |
| Facade generation | [`src/DotProlog.CodeGen.CSharp/FacadeGenerator.cs`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.CodeGen.CSharp/FacadeGenerator.cs) |
| SDK targets | [`src/DotProlog.Sdk/Sdk/Sdk.targets`](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.Sdk/Sdk/Sdk.targets) |
