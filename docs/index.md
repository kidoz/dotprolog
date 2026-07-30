# DotProlog

DotProlog is a Prolog language implementation for .NET 10, written in C# 14. It is building toward
the same first-class SDK experience that C# and F# provide: `.dplproj` projects, `dotnet prolog`,
`dotnet new` templates, ordinary .NET project references, and NativeAOT publishing.

!!! note "Project status"

    DotProlog is early but usable. The SDK, templates, `dotnet prolog`, Prolog tests through
    `dotnet test`, typed .NET facades, and NativeAOT runtime consultation work in the repository.
    `.dplproj` predicate bodies compile to generated C# and ordinary CLR IL; runtime-loaded source
    compiles to internal bytecode. The standalone `plc` command is not implemented yet, and
    packages are not currently published to NuGet.org.

## Try it from the repository

```console
dotnet run --project src/DotProlog.Tool -- run samples/HelloProlog/hello.pl
```

The sample is ordinary Prolog:

```prolog
:- initialization(main).

main :-
    greeting(Greeting),
    write(Greeting),
    nl.

greeting('Hello! World!').
```

```text
Hello! World!
```

## What you can build

- Run `.pl` files with the repository tool.
- Embed a Prolog engine in C#, F#, or Visual Basic and enumerate solutions lazily.
- Build `.dplproj` applications, libraries, and tests.
- Generate typed .NET facades from `.dpli` contracts.
- Reference a Prolog project from C#, F#, or Visual Basic.
- Publish self-contained NativeAOT applications that consult new Prolog source at run time.

## Where to go next

- [Getting started](getting-started.md) — build the repository and run a program.
- [A Gentle Introduction to Prolog](book/index.md) — a free beginner book, in English and Russian,
  that teaches Prolog from zero using DotProlog.
- [Language guide](language-guide.md) — learn the supported Prolog surface and current limits.
- [.NET integration](dotnet-integration.md) — embed the engine or consume a `.dplproj`.
- [Architecture](architecture/index.md) — understand the bytecode VM and NativeAOT design.
- [Contributing](contributing.md) — run the project and documentation checks.
