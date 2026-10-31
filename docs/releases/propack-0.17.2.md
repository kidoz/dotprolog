### Fixed

- HTTPS package archives require an explicit SHA-256 digest and reject redirects, including
  same-host redirects. The two-minute download timeout covers the response body as well as the
  response headers. Checksums for local ZIP archives remain optional.
- Package input reads reject FIFOs, devices and symbolic-link replacements before reading from the
  opened handle, and the 128 MiB limit counts bytes actually read during hashing and copying.
- Dependency resolution tries older catalog candidates after a transport failure and reports the
  unavailable candidates when no solution remains. Explicit source failures and integrity failures
  still stop the solve immediately and preserve the existing lock.
- Package module headers are validated by a linear scan of the first 64 KiB, bounding the work
  required by large comment-only inputs.
- Every package transport publishes only the files covered by the tree hash into the cache. Cache
  validation rejects excluded paths and unexpected empty directories, module declarations cannot
  point into excluded paths, and ZIP and Git snapshots can no longer retain unhashed executable
  sources.
- Git package acquisition ignores system, global and inherited Git configuration and checkout
  filters, disables local-clone optimization, recursive submodules and symbolic-link checkout, and
  uses an empty hooks directory on every platform. Resolved revisions must be hexadecimal commit
  IDs, and source locations and revisions cannot contain control characters.
