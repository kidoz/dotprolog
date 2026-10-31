# propack 0.17.0

The first stable release of the standalone Prolog package manager.

- Restore pure Prolog source packages from local directories, Git revisions and ZIP archives.
- Resolve dependencies with stable semantic-version requirements and local catalogs.
- Lock package versions, Git commits and content hashes for repeatable restoration.
- Detect dependency conflicts, duplicate module exports and changed package contents.
- Run the same restored graph with SWI-Prolog or DotProlog, including DotProlog SDK projects.
- Use the schema 1 manifest and lock contract, with editor schemas.

Install the .NET 10 tool:

```sh
dotnet tool install --global DotProlog.Packages.Cli --version 0.17.0
propack --help
```

Alternatively, extract the native ZIP for Linux x64, Windows x64 or macOS ARM64 and place its
executable on PATH. Native restoration requires no .NET installation or Prolog engine.
Git sources require Git; execution requires `swipl` or `dotnet-prolog` on PATH.
DotProlog execution requires an engine built from this checkout with `run --packages` support;
the previously published DotProlog.Tool 0.16.1 does not include that command option. The package
guide includes source-tree execution commands. SWI-Prolog can be used directly with this release.
Verify downloaded files against `SHA256SUMS`.

Validation installs the actual tool package and extracted native executable on all three platforms,
then exercises locked restore, update and all twelve exported operations of an unchanged, pinned
BSD-licensed SWI-Prolog `heaps` library under both engines. The fixture retains its upstream license
and provenance. A software bill of materials accompanies the release assets.

Scope: pure Prolog packages, stable package versions, and explicit package sources/catalogs. Hosted
registry discovery, prerelease package versions, native extension builds and automatic dialect
conversion are not included. A package's engine declarations are author claims requiring tests.

Known DotProlog compatibility limitation: the upstream `pairs` callback probe formed a nested
module-qualified closure and raised `existence_error(procedure, (:)/4)`. Importing all pair
predicates into `user` also conflicts with existing definitions. The package manager does not
change those engine semantics. See the package guide for the complete format and compatibility limits.
