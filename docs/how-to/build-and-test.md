# Build and test the repository

Use this guide when changing DotProlog itself or running the repository samples. Install the
.NET 10 SDK and Git, then clone the source and enter its root directory:

```console
git clone https://github.com/kidoz/dotprolog.git
cd dotprolog
dotnet restore DotProlog.slnx
dotnet build DotProlog.slnx
dotnet test --solution DotProlog.slnx --no-ansi
```

The build must succeed and the test summary must report no failures. Integration tests that
publish native binaries or invoke external toolchains are opt-in; a normal test run skips them.
See [NativeAOT verification](publish-nativeaot.md#verify-runtime-consultation).

For the full contributor gate, install [just](https://just.systems/) and
[uv](https://docs.astral.sh/uv/), then run:

```console
uv sync --locked --only-group docs
just check
```

This also checks C# formatting and builds the documentation. See
[contributing](../contributing.md#repository-checks) for the individual commands.
