# Contributing

DotProlog targets .NET 10 and C# 14. Keep the solution buildable and add focused tests when changing
runtime, language, compiler, SDK, or tooling behavior.

## Repository checks

The full local gate is:

```console
just check
```

Its individual commands are:

```console
just format-check
just docs
just build
just test
```

Without `just`:

```console
dotnet tool restore
dotnet csharpier check .
uv run --locked --only-group docs mkdocs build --strict
dotnet build DotProlog.slnx
dotnet test --solution DotProlog.slnx --no-ansi
```

NativeAOT and external conformance tests are opt-in locally because they take longer. CI exercises
them on their supported platforms.

## Documentation

The documentation site uses [MkDocs](https://www.mkdocs.org/) and manages its Python environment
with [uv](https://docs.astral.sh/uv/). Install uv, then synchronize the locked `docs` dependency
group:

```console
uv sync --locked --only-group docs
```

The project requires Python 3.14 in `pyproject.toml` and pins it in `.python-version`; uv downloads
that interpreter automatically when needed. Build with strict link and navigation validation:

```console
just docs
```

The generated site is written to `obj/docs/`. Preview it with live reload:

```console
just docs-serve
```

Then open <http://127.0.0.1:8000/>.

When adding a page, place it under `docs/`, add it to `nav` in `mkdocs.yml`, and use relative links
between documentation pages. The CI documentation job fails on omitted pages, missing targets, and
invalid anchors. Update dependencies through `pyproject.toml` and commit the refreshed `uv.lock`.

## Code placement

- Production components live under `src/<Component>/`.
- Unit tests mirror their component under `tests/<Component>.Tests/`.
- Slow end-to-end, packaging, and NativeAOT tests live under `tests/Integration/`.
- Benchmarks live under `benchmarks/`, never in the normal test projects.
- Runnable examples live under `samples/`.
- Public documentation belongs under `docs/`.

Public shipped APIs require XML documentation. Diagnostics are user-facing contracts: preserve
stable `DPL` identifiers and precise source spans.
