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

Thrown balls use a detached `TermBuffer` copy so they survive heap restoration at `catch/3`.
Exception copies preserve cycles with buffer-relative back edges, which are relocated when
the ball is rebuilt on the restored heap. Other detached-copy consumers still reject cycles.

Control constructs compile inline in the containing clause. This preserves ISO cut scope. A
control term reached through `call/1` is lowered at run time with a meta-call barrier so the same
scope rules apply.

A goal frozen with `freeze/2` is kept in a table keyed by its variable's heap address and undone
through the trail like a backtrackable global variable. Binding that variable only records its
address, since a binding can happen while a structure is still being written. The next call,
return, builtin, cut, disjunction, or catch frame is a wake point: it saves its live argument
registers in a small frame, runs the recorded goals through the library's `'$wakeup'/1`, and
returns through a reserved instruction that runs the interrupted instruction again, on the bytecode
and the generated-C# paths alike.

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

`DotProlog.Sdk` is additive to the .NET SDK. A `.dplproj` build parses and lowers Prolog source,
then reads a `.dpli` contract to generate a typed C# facade. The result is an ordinary assembly
consumable by C#, F#, and Visual Basic.

Build-time predicates become direct-threaded generated C# blocks and then CLR IL. Runtime-loaded
source continues to become DotProlog bytecode. Both target the same explicit heap, trail,
environment, choice-point, and continuation state, so either path can call the other. Both also
share one front end — the reader, the loader, and the clause compiler — and there is no separate
intermediate representation: the generated C# is translated from the clause compiler's bytecode
instructions, emitted without the VM's first-argument index dispatch.

```text
SDK build  : reader -> loader -> bytecode -> generated C# -> Roslyn -> IL
plc build  : reader -> loader -> bytecode -> direct IL emission
Runtime    : reader -> loader -> bytecode -> DotProlog VM
```

Generated applications, facades, and Prolog test hosts do not embed or consult their build-time
source. Runtime `consult/1`, `ensure_loaded/1`, and database updates remain available and are never
converted into new CLR IL inside the process.

## Direct IL compiler

`DotProlog.Compiler.Cli` provides `plc`, which uses `DotProlog.CodeGen.IL` to serialize a managed
PE assembly with `System.Reflection.Metadata`. It emits no C# source. Both build-time backends
use the portable compilation model in `DotProlog.Compiler/CodeGeneration`.
Repeated method references share a metadata row within each emitted assembly, keyed by declaring
type, name, and full signature so overloads and calling conventions remain distinct.

Direct IL preserves the loader's first-argument indexes for multi-clause static predicates.
Installation relocates clause targets and term keys into the receiving engine before running
directives. Selection and redo reuse the VM's index machinery: candidate clauses retain source
order, variables match every key, and a single candidate creates no choice point. Generated C#
continues to use unindexed clause chains. The versioned installation reader also accepts older
images without index tables.

Fused IL uses an alternate clause chain when the first argument is unbound. The index-entry
primitive captures the first alternative and dispatches directly to the existing first clause.
Fallback targets are resolved from local block indexes only for unbound calls; bound calls go
directly to table selection without reading those targets.
Small generated retry/trust entries share the existing safe head blocks through static calls with inlining
requested; they do not duplicate the head IL. Wake-capable operations remain separately dispatched,
so resuming a wake cannot replay a clause-selection header. Bound arguments still use the index.

Each IL block statically calls the same `Machine.CompiledExecution` operations as the corresponding
C# blocks. The IL backend groups straight-line term, environment, and clause-selection operations
into one method, returning immediately on failure. Calls, wake-up checks, and other control-flow
operations remain separate blocks so resumption cannot replay a partially executed prefix. Branches,
continuations, and installed predicate entries retain explicit targets. Static delegates register
these blocks with the explicit machine. The installation
image holds only portable symbols, constants, module metadata, and registration/initialization
order; predicate instructions are IL methods, not serialized bytecode interpreted at startup.
Runtime consult/assert and the existing standard-library initialization retain their bytecode path.

The generated SDK publishing project replaces `CoreCompile` with a copy of the emitted assembly.
NativeAOT and the system linker produce the final target-specific executable. This project opts
out of surrounding `Directory.Build.props` and targets so an unrelated parent build cannot change
its input assembly. The compiler stages outputs in a sibling directory and publishes the complete
managed artifact set with a directory move, rejecting existing destinations.

See the [standalone compiler commands](../how-to/publish-nativeaot.md#compile-a-standalone-prolog-file).

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
