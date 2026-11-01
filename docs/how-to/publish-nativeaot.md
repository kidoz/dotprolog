# Publish with NativeAOT

Use this guide to publish an existing DotProlog console project as a self-contained native
executable. Install the .NET 10 SDK and the native build tools for your operating system, as
listed in the [.NET NativeAOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).

## Publish the application

For a project named `HelloProlog/HelloProlog.dplproj`, run:

```console
dotnet publish HelloProlog/HelloProlog.dplproj -c Release -r osx-arm64 -p:PublishAot=true
```

Replace the project path and runtime identifier with your application's path and target; for
example, use `linux-x64` on an x64 Linux build host or `win-x64` on x64 Windows. Build on a host
that supports your chosen native target.

Run the executable in the publish directory printed by `dotnet publish`. For the example above:

```console
./HelloProlog/bin/Release/net10.0/osx-arm64/publish/HelloProlog
```

On Windows, run `HelloProlog.exe` from the corresponding publish directory. Confirm that the
application produces the same results as its managed build, and investigate any trimming or AOT
warnings before distributing it.

## Compile a standalone Prolog file

`plc` translates Prolog directly into .NET IL and optionally invokes NativeAOT. From a repository
checkout, run:

```console
dotnet run --project src/DotProlog.Compiler.Cli -- samples/HelloProlog/hello.pl --output artifacts/hello-native --aot
./artifacts/hello-native/native/PrologProgram
```

Install the compiler from NuGet.org as the `DotProlog.Compiler.Cli` .NET tool:

```console
dotnet tool install DotProlog.Compiler.Cli --tool-path artifacts/compiler-tools --version 0.17.0
./artifacts/compiler-tools/plc --version
./artifacts/compiler-tools/plc samples/HelloProlog/hello.pl --output artifacts/installed-hello --aot
```

Replace `--tool-path artifacts/compiler-tools` with `--global` to install `plc` on your tool PATH, or
use `--local` inside a directory with a tool manifest. Run the installed command from any working
directory; it includes the required DotProlog libraries. To test an unreleased checkout, pack the
project and add `--add-source artifacts/compiler` to the installation command. The compiler requires
.NET 10. Native publishing also needs the SDK and native toolchain.
The installation options follow the [.NET tool installation reference](https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-install).

The output directory must not already exist. The native target defaults to the current host;
use `--rid` to select another target supported by your build toolchain. Windows produces
`PrologProgram.exe`. Use `:- initialization(main).` in the source to select the startup goal.

Omit `--aot` to emit the managed assembly and its publishing assets only, then run it with
`dotnet artifacts/hello-native/PrologProgram.dll`. Multiple input files are compiled in argument
order. The compiler also accepts `--mode strict-iso` and `--flag double_quotes=chars`. Use
`--packages DIR` to compile Modern `library(Name)` imports from a restored propack workspace; the
lock and cache are verified without fetching, and the output runs without the workspace.

This path emits no C# source and requires no `.dplproj`. The managed output includes a deterministic
Portable PDB mapping emitted IL to the original Prolog documents and source spans. NativeAOT consumes
the emitted assembly through a generated SDK publishing project. Runtime consultation and
standard-library startup use the existing bytecode engine; build-time predicates execute the emitted
IL blocks.

## Verify runtime consultation

From the DotProlog repository root, the opt-in integration suite publishes and runs acceptance
applications, including runtime consultation, dynamic database changes, and an installed `plc`
compiling outside the repository:

```console
DOTPROLOG_RUN_AOT_TESTS=1 dotnet test --project tests/Integration
```

The command above uses a POSIX shell. In PowerShell:

```powershell
$env:DOTPROLOG_RUN_AOT_TESTS = '1'
dotnet test --project tests/Integration
```

This enables the integration suite, including slower toolchain checks, and may take several
minutes. Check its summary for failures and confirm the acceptance tests actually ran.

Runtime-loaded `.pl` files execute as internal bytecode inside the native application. For the
relationship to build-time generated code, see
[runtime architecture](../architecture/index.md#sdk-and-generated-facades).
