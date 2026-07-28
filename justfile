# DotProlog task shortcuts. Run `just` to list them.
# Requires the .NET 10 SDK; `just tools` installs the local tools (CSharpier).

solution := "DotProlog.slnx"
sample := "samples/HelloProlog/hello.pl"

default:
    @just --list

# Restore the local dotnet tools declared in .config/dotnet-tools.json.
tools:
    dotnet tool restore

# Restore NuGet packages for the whole solution.
restore:
    dotnet restore {{solution}}

# Build every project.
build configuration="Debug":
    dotnet build {{solution}} -c {{configuration}} --nologo

# Run every test project.
test configuration="Debug":
    dotnet test --solution {{solution}} -c {{configuration}} --no-ansi

# Consult and run a Prolog file: `just run path/to/file.pl`.
run file:
    dotnet run --project src/DotProlog.Tool -- run {{file}}

# Run the Hello World sample.
hello:
    @just run {{sample}}

# Format all C# with CSharpier.
format: tools
    dotnet csharpier format .

# Fail if any C# file is not formatted; this is what CI should run.
format-check: tools
    dotnet csharpier check .

# Build, format-check, and test — the gate before committing.
check: format-check build test

# Run the BenchmarkDotNet suite; pass a filter, e.g. `just bench '*Engine*'`.
bench filter="*":
    dotnet run -c Release --project benchmarks/DotProlog.Benchmarks -- --filter '{{filter}}'

# Delete build outputs.
clean:
    dotnet clean {{solution}} --nologo
    rm -rf BenchmarkDotNet.Artifacts

# Pack every publishable project into ./artifacts with checksums.
pack configuration="Release":
    rm -rf artifacts
    dotnet pack {{solution}} -c {{configuration}} -o artifacts --nologo
    cd artifacts && shasum -a 256 *.nupkg *.snupkg > SHA256SUMS
    @cat artifacts/SHA256SUMS
