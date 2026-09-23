<img src="assets/dotprolog-logo.png" alt="DotProlog logo" width="720">

DotProlog is a Prolog language implementation for .NET 10, written in C# 14. It is building toward
the same first-class SDK experience that C# and F# provide: `.dplproj` projects, `dotnet prolog`,
`dotnet new` templates, ordinary .NET project references, and NativeAOT publishing.

!!! note "Project status"

    DotProlog is early but usable. The SDK, templates, `dotnet prolog`, Prolog tests through
    `dotnet test`, typed .NET facades, and NativeAOT runtime consultation work in the repository.
    `.dplproj` predicate bodies compile to generated C# and ordinary CLR IL; runtime-loaded source
    compiles to internal bytecode. The standalone `plc` command is not implemented yet. The
    packages are published on NuGet.org from 0.2.0 onwards.

## What you can build

- Run `.pl` files with the repository tool.
- Lint `.pl` files without consulting them or executing directives.
- Embed a Prolog engine in C#, F#, or Visual Basic and enumerate solutions lazily.
- Build `.dplproj` applications, libraries, and tests.
- Generate typed .NET facades from `.dpli` contracts.
- Reference a Prolog project from C#, F#, or Visual Basic.
- Publish self-contained NativeAOT applications that consult new Prolog source at run time.

<span id="try-it-from-the-repository"></span>
<span id="where-to-go-next"></span>

## Tutorials — learn by doing

Start with [your first DotProlog program](getting-started.md) for a short guided exercise.
[A Gentle Introduction to Prolog](book/index.md) is the longer path from facts and rules to a text
adventure, available in English and [Russian](book/ru/index.md).

## How-to guides — complete a task

- [Build and test the repository](how-to/build-and-test.md).
- [Embed Prolog in C#](how-to/embed-engine.md).
- [Create a typed Prolog library](how-to/create-library.md).
- [Reference Prolog from another .NET language](how-to/reference-project.md).
- [Configure language modes and flags](how-to/configure-language.md).
- [Lint source locally and in CI](how-to/lint-source.md).
- [Run Prolog tests](how-to/run-tests.md).
- [Publish with NativeAOT](how-to/publish-nativeaot.md).

## Reference — look up behavior

- [Language](language-guide.md) — syntax, modes, supported features, and limits.
- [Linting](linting.md) — options, diagnostic identifiers, and exit codes.
- [.NET integration](dotnet-integration.md) — embedding, facade, and SDK contracts.
- [ISO processor characteristics](reference/iso-processor-characteristics.md).
- [ISO Part 1 conformance](reference/iso-part1-conformance.md).
- [ISO Parts 2 and 3 conformance](reference/iso-parts2-3-conformance.md).
- [SWI compatibility ledger](reference/swi-compatibility.md).

## Explanation — understand the design

- [Runtime architecture](architecture/index.md) — the execution machine, build-time compilation,
  runtime consultation, and NativeAOT.
- [Language modes and text](explanation/language-modes.md) — profiles, text representations, and
  the scope of initial settings.

## Contributing

See [contributing](contributing.md) for repository checks and documentation authoring guidance.
