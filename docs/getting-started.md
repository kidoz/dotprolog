# Getting started

DotProlog currently targets developers working from the repository. The packaged templates and tool
are exercised in CI, but they are not yet available from NuGet.org.

## Prerequisites

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Optional: [just](https://just.systems/) for the repository shortcuts
- [uv](https://docs.astral.sh/uv/) when running the documentation checks or the full `just check`

Check the SDK selected by the repository:

```console
dotnet --version
```

## Build and test

Clone the repository, then restore and build the solution:

```console
git clone https://github.com/kidoz/dotprolog.git
cd dotprolog
dotnet restore DotProlog.slnx
dotnet build DotProlog.slnx
dotnet test --solution DotProlog.slnx --no-ansi
```

With `just`, the equivalent full check is:

```console
just check
```

## Run the Hello World sample

```console
dotnet run --project src/DotProlog.Tool -- run samples/HelloProlog/hello.pl
```

Or:

```console
just hello
```

The `run` command consults the file, reports reader or compiler diagnostics, and then executes its
directives and initialization goal.

To reject known implementation-specific language features, run the file in strict ISO mode:

```console
dotnet run --project src/DotProlog.Tool -- run --strict-iso path/to/program.pl
```

Strict mode reports `DPL1018` when source calls a predefined DotProlog extension. The default mode
remains extended for backward compatibility.

## Run your own program

Create a UTF-8 `.pl` file:

```prolog
:- initialization(main).

main :-
    member(Name, [ada, grace, edsger]),
    format('Hello, ~w!~n', [Name]),
    fail.
main.
```

Run it from the repository root:

```console
dotnet run --project src/DotProlog.Tool -- run path/to/program.pl
```

## Run Prolog tests

A `.dplproj` test project discovers every zero-arity predicate whose name starts with `test_`.
Each test runs in a fresh engine:

```prolog
test_addition :-
    2 + 2 =:= 4.

test_lists :-
    append([a, b], [c], [a, b, c]).
```

Run the included sample with:

```console
dotnet test --project samples/PricingTests/PricingTests.dplproj
```

## Exercise NativeAOT

The integration suite can publish and run the NativeAOT acceptance sample. It is opt-in because a
native publish is slower than the normal test suite:

```console
DOTPROLOG_RUN_AOT_TESTS=1 dotnet test --project tests/Integration
```

The test publishes a self-contained executable, consults an external `.pl` file at run time,
enumerates solutions, changes the dynamic database, and verifies that trimming and AOT produce no
warnings.
