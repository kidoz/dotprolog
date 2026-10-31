### Fixed

- Package restoration rejects cache directories equal to or inside the project after path
  normalization, including case variants on Windows and macOS. Previously, an equal cache path
  or a case variant could place package snapshots inside the source tree, making those snapshots
  inputs when the project was consumed as a local package.
- The cache check handles trailing directory separators and filesystem roots while allowing
  caches in parent directories, sibling directories, and other filesystem roots.
- Regression tests cover equality, descendants, normalized paths, platform case rules, valid
  siblings, and CLI rejection before any cache or lock files are created.
