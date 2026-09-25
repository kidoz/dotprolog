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

From a repository checkout, `plc` translates Prolog directly into .NET IL and optionally invokes
NativeAOT. It is currently a checkout tool, not a published .NET tool package.

```console
dotnet run --project src/DotProlog.Compiler.Cli -- samples/HelloProlog/hello.pl --output artifacts/hello-native --aot
./artifacts/hello-native/native/PrologProgram
```

The output directory must not already exist. The native target defaults to the current host;
use `--rid` to select another target supported by your build toolchain. Windows produces
`PrologProgram.exe`. Use `:- initialization(main).` in the source to select the startup goal.

Omit `--aot` to emit the managed assembly and its publishing assets only, then run it with
`dotnet artifacts/hello-native/PrologProgram.dll`. Multiple input files are compiled in argument
order. The compiler also accepts `--mode strict-iso` and `--flag double_quotes=chars`.

This path emits no C# source and requires no `.dplproj`. NativeAOT consumes the emitted assembly
through a generated SDK publishing project. Runtime consultation and standard-library startup
use the existing bytecode engine; build-time predicates execute the emitted IL blocks.

## Verify runtime consultation

From the DotProlog repository root, the opt-in integration suite publishes and runs acceptance
applications, including runtime consultation and dynamic database changes:

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
