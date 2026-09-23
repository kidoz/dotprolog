# Contributing

DotProlog targets .NET 10 and C# 14. Keep the solution buildable and add focused tests when changing
runtime, language, compiler, SDK, or tooling behavior.

## Repository checks

For prerequisites and initial setup, see [build and test the repository](how-to/build-and-test.md).

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

### Write for a reader's need

The site follows [Diátaxis](https://diataxis.fr/) and continues to use MkDocs to build the pages.
Choose the page's primary purpose before writing:

| Form | Reader need | Write |
|---|---|---|
| Tutorial | Learn through a guided experience | A complete sequence with prerequisites, concrete actions, and expected results |
| How-to guide | Complete a known task | Goal-focused steps, relevant choices, and a way to check success |
| Reference | Look up an exact contract | Structured facts: signatures, options, defaults, limits, errors, and return values |
| Explanation | Understand why or how ideas relate | Context, design choices, tradeoffs, and connections |

New task guides go under `docs/how-to/`, new reference pages under `docs/reference/`, and new
explanations under `docs/explanation/`. Add new standalone tutorials under `docs/tutorials/` when
needed. Existing pages may keep their paths: navigation expresses their purpose independently of
directory names. The beginner book remains a continuous learning path under `docs/book/`, with
its Russian counterpart in `docs/book/ru/`.

Split substantial reference tables and design discussion out of procedures, then cross-link them.
Keep enough context in each page for it to stand on its own. Improve one useful page at a time;
do not add empty pages merely to fill the four categories. See the
[Diátaxis workflow](https://diataxis.fr/how-to-use-diataxis/).

Before submitting a documentation change:

- Check that its title and opening identify the task, learning outcome, contract, or concept.
- Run changed tutorial and how-to examples and compare their results with the stated expectations.
  State the required working directory and distinguish a repository tool from an installed tool.
- Keep supported behavior and compatibility claims aligned with the existing conformance records.
- Preserve existing page URLs and heading anchors, or leave a useful compatibility entry point.
  Check inbound links from the README and both book languages when splitting a page.
- Keep translated book changes synchronized when changing a shared example or navigation.
- Run `just docs`; a strict build checks navigation and links, but cannot prove that examples work
  or that a page serves its reader's need.

## Code placement

- Production components live under `src/<Component>/`.
- Unit tests mirror their component under `tests/<Component>.Tests/`.
- Slow end-to-end, packaging, and NativeAOT tests live under `tests/Integration/`.
- Benchmarks live under `benchmarks/`, never in the normal test projects.
- Runnable examples live under `samples/`.
- Public documentation belongs under `docs/`.

Public shipped APIs require XML documentation. Diagnostics are user-facing contracts: preserve
stable `DPL` identifiers and precise source spans.
