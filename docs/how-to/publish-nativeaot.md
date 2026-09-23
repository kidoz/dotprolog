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
