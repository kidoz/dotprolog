### Added

- The standalone `propack` tool restores pure Prolog source packages from local directories,
  Git revisions, and ZIP archives, with dependency resolution through stable version requirements
  and local catalogs.
- Lockfiles pin package versions, Git commits, and content hashes. Locked restore verifies the
  existing dependency graph; changed manifests and local packages require an explicit update.
  Conflicting dependencies, duplicate module names, and changed cached content produce errors.
- SWI-Prolog and DotProlog adapters execute the same restored package graph. DotProlog SDK projects
  consume the lock without downloading during compilation. DotProlog execution requires a
  source-built engine with package support; the published `DotProlog.Tool` 0.16.1 predates it.
- `DotProlog.Packages.Cli` installs as a .NET 10 tool. Native ZIPs for Linux x64, Windows x64, and
  macOS ARM64 restore packages without a .NET installation. Release assets include SHA-256
  checksums and a software bill of materials.
- The schema 1 manifest and lock contract, with editor schemas, defines the package format.
  The [package guide](https://github.com/kidoz/dotprolog/blob/main/docs/how-to/manage-packages.md)
  documents installation, supported sources, and engine compatibility limits.
- Release validation installs the tool package and extracted native executable on all three
  platforms, exercises locked restore and update, and runs all twelve exported operations of
  the unchanged, pinned SWI-Prolog `heaps` library under both engines.
