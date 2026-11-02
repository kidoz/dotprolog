# Language server implementation plan

DotProlog's language server adds editor services by reusing the parser and static linter.
It runs in a separate process over LSP stdio; editor operations never consult Prolog or execute
initialization goals. The implementation is staged so that each stage has an observable acceptance
check. This is a development prototype, not an installable release or a complete editor integration.

## Stage 1: protocol and file diagnostics — implemented

- `DotProlog.LanguageServer` produces `dotprolog-lsp`, targeting .NET 10 with AOT analysis enabled.
  It is non-packable and is not added to release workflows.
- StreamJsonRpc 2.26.150 supplies Content-Length framing. An explicit JSON DOM formatter and
  ordered dispatch avoid reflection-based serialization and handler registration.
- `initialize`, `initialized`, `shutdown`, `exit`, and full-text `didOpen`, `didChange`, `didClose`
  notifications are supported. Unknown requests receive method-not-found errors; unknown
  notifications are ignored. Exit without shutdown returns 1; orderly shutdown/exit returns 0.
- Open/change publish parser and file-local semantic lint diagnostics with original DPL codes,
  severity, source, ranges, URI and document version. Closing clears diagnostics. Changes for
  unopened documents or non-increasing versions are ignored.
- Positions use UTF-16 offsets and zero-based lines, independent of Prolog character semantics.
- Source is analyzed from the incoming buffer. Virtual documents remain file-local; file documents
  now use the include workspace described below. Modes are selected at startup with
  `--mode modern|strict-iso`; Modern is the default.

Acceptance: framed protocol tests, malformed input and incomplete source, stale versions,
Unicode and all common line endings, non-execution of directives, native publish without AOT
warnings, and the same process smoke test against managed and native executables.

## Stage 2: workspace and responsive analysis — includes, project reload and background scheduling implemented

Implemented:

- Immutable open-document snapshots and a source-provider overload on `PrologLintSourceLoader`.
  The existing overload continues reading disk for CLI callers. File URIs are mapped to absolute
  paths; spaces, escaped characters and dot segments resolve to the same include identity.
  Windows uses case-insensitive path keys; other systems use ordinal keys, matching the loader.
  Symlink aliases and case aliases on case-insensitive non-Windows volumes are not unified yet.
- File documents expand literal `include/1` targets, preferring unsaved buffers over disk. Virtual
  documents such as `untitled:` retain file-local analysis because they have no include base path.
- Each root caches transitive include dependencies, including failed reads. Opening, changing or
  closing an included buffer reanalyzes affected roots with fresh reader state. Disk reads within
  an update share a text cache. Missing files, cycles and include-depth limits use existing DPL
  diagnostics; source is never consulted or executed.
- Loose-file roots use the existing whole-program linter over their include closure. With an
  explicit project, evaluated source items are analyzed together, including unopened files.
  Unsaved buffers override disk in both paths. Imports are resolved among supplied sources;
  external imported modules are not automatically loaded.
- Diagnostics are published at each physical source URI using that source's text for ranges.
  Open included files use the including context rather than adding standalone syntax errors for
  inherited operators. Shared roots merge duplicate findings; multiple distinct parent contexts
  may contribute distinct findings. Cycles use a deterministic representative root.
- Closing an included buffer restores disk analysis while a parent remains open. Its diagnostics
  can remain, without a document version. Removing the last owning root clears its diagnostics.
  Unchanged diagnostics are not republished; open-buffer versions are retained on dependency updates.
- `workspace/didChangeWatchedFiles` batches refresh dependent roots for created, changed and
  deleted files. Duplicate paths are coalesced, shared disk reads are cached once per batch, and
  unrelated events do not publish diagnostics. Files with open buffers ignore disk events until
  close restores disk analysis. The full batch is validated before any state is changed.
- When the client advertises `workspace.didChangeWatchedFiles.dynamicRegistration`, the server
  requests one `client/registerCapability` registration after `initialized`. It watches `**/*`
  for create/change/delete, because includes are not restricted to `.pl`. Registration responses
  are handled as responses; rejection logs a warning and leaves buffer editing available.

File watching is performed by the client. Clients without dynamic registration can configure and
send these notifications themselves. There is no server-owned filesystem watcher. Watch coverage
and exclusions follow the client workspace; includes outside that coverage need client configuration
or an open/change/close event to refresh them. The current native smoke test simulates a client and
verifies the wire exchange; actual editor watcher delivery remains unverified.

Project configuration:

- Start with `--project /absolute/path/app.dplproj [--configuration Debug]`. The server invokes
  `dotnet msbuild` with `-getProperty`, `-getItem` and `DesignTimeBuild=true`, without a target or
  restore switch. MSBuild evaluates imports, conditions, globs, item removals and linked paths.
  No new SDK task or target is needed. Build-time item additions inside targets are not included.
- The evaluated configuration captures `DotPrologLanguageMode`, `DotPrologFlags`, `PrologCompile` and
  `PrologContract`. Mode and flags govern project sources and their includes; unrelated open
  files retain the default Modern mode. Contract paths are captured for future `.dpli` services;
  contract diagnostics are not implemented yet.
- Project diagnostics begin after `initialized` and remain available for closed project files.
  Buffer and disk edits refresh their contents using the existing watcher path. Closing a project
  source restores disk analysis rather than removing it from the selected project.
- Evaluation runs as a child process with a 30-second timeout, captured stdout/stderr, and a
  controlled startup error for missing projects, invalid modes/flags or evaluation failure.
  `--project` cannot be combined with `--mode`; project settings are authoritative. Debug is the
  default configuration. Multi-target projects without a single evaluated TargetFramework are
  rejected rather than selecting one implicitly.
- In project mode, each validated file-notification batch reevaluates the selected project once,
  using the startup `--configuration`. This intentionally covers imports with arbitrary extensions,
  file-existence conditions and glob membership. Empty/virtual-only batches and invalid batches
  do not evaluate. Unchanged evaluated settings use ordinary dependency-based source refresh.
- Changed mode, flags or source membership replace the project analysis together. Unsaved buffers
  and their versions survive the replacement. Removed closed sources have their diagnostics
  cleared; removed open sources fall back to loose-file analysis. Added sources are analyzed.
- Failed evaluation retains the last valid settings and still refreshes changed source contents.
  A `window/logMessage` error reports the failure, repeated identical failures are suppressed,
  and successful recovery logs an informational message. A later file notification retries.
- Reevaluation uses saved project/import files, not unsaved XML buffers. The selected project and
  Debug/Release configuration remain fixed for the process lifetime. Changes outside client watcher
  coverage require a notification or restart. Project references and package resolution remain planned.
- Project mode requires an installed .NET SDK even with the native server. The selected project
  and imports are trusted build configuration: MSBuild evaluation can run property functions and
  resolve SDKs. No build targets or Prolog goals are executed by this integration.

Background analysis:

- The protocol loop accepts document snapshots and disk events while one background worker evaluates
  and analyzes. A 150 ms trailing debounce coalesces rapid changes; parsing still uses complete text.
- Each update cancels the previous generation. Analysis uses a fork of the last committed workspace;
  superseded diagnostics, configuration and reload logs are discarded. Pending disk events survive
  cancellation so the next generation still reevaluates settings and refreshes affected sources.
- Publication and input acceptance share a gate, so an accepted newer generation cannot be followed
  by an obsolete publication. The client must keep draining stdout; slow output can delay dispatch.
- Cancellation is checked at clause, include and source boundaries and around lint passes. Individual
  clause parsing, synchronous file reads and lint passes are not forcibly interrupted. One worker
  bounds concurrent analysis, but an unfinished pass can delay the next results. No hard typing or
  process-exit latency guarantee is claimed.
- Shutdown cancels pending work and responds without waiting for analysis. Exit waits for worker
  cleanup. Cancelling a project evaluation kills its process tree and observes its captured streams;
  the existing 30-second evaluation timeout remains. These are internal cancellation tokens, not an
  additional analysis request capability. Document-symbol requests separately support `$/cancelRequest`.

Remaining:

1. Add import dependency resolution and verify client watcher coverage in an actual editor.
2. Add multi-project selection and project-reference handling.
3. Resolve restored package/library sources without downloading dependencies during editing.
4. Measure typing latency on a pinned corpus and consider incremental text synchronization before
   incremental parsing; complete-file parsing remains the baseline.

Acceptance already covered: loader/linter diagnostic parity for a file with includes, unsaved
include edits, close-to-disk transitions, inherited operators, nested missing includes, cycles,
source ownership and Unicode ranges, duplicate merging, diagnostic cleanup, watch registration,
disk create/change/delete, nested invalidation and open-buffer precedence. Project tests now cover
MSBuild imports, configuration conditions, source removals, cross-file resolution, mode and flags,
and failure handling without builds or restores. Live reload tests cover imported settings,
source glob additions/deletions, membership changes, failure retention, recovery and unsaved
buffer preservation. Scheduler tests cover edit bursts, close during pending work, discarded obsolete
results and responsive requests/shutdown during blocked reevaluation. A process test verifies that
cancellation terminates real MSBuild evaluation and its descendant. Establish performance
measurements on a pinned corpus before accepting a latency target; 250 ms after debounce on a
2,000-line file is a proposed initial target, not a measured claim.

## Stage 3: symbols and navigation — outlines, builtin hover and word completion implemented

The first navigation increment provides a predicate outline through LSP 3.17
`textDocument/documentSymbol`. Its requirements and acceptance checks are:

- **REQ-LSP-SYM-1:** Advertise `documentSymbolProvider` and return hierarchical `DocumentSymbol`
  results when the client advertises support, otherwise flat `SymbolInformation` results.
- **REQ-LSP-SYM-2:** Identify definitions by module, source name, arity and ordinary/DCG identity.
  Group clauses of the same predicate in each physical file; hierarchical results expose individual
  clause children for predicates with multiple clauses. Directives and declarations are not definitions.
- **REQ-LSP-SYM-3:** Use zero-based UTF-16 ranges from the analyzed text, with selection ranges on
  the original name/operator token, including quoting. Definition ranges cover parsed terms, excluding
  their terminating full stops. Synthetic heads without a name token use the head range.
- **REQ-LSP-SYM-4:** Use unsaved buffers and the same project/include reader context as diagnostics.
  Return only definitions physically owned by the requested URI. Untracked documents return an empty
  list; unopened evaluated project sources and retained includes can have outlines.
- **REQ-LSP-SYM-5:** Requests arriving during analysis wait for a current committed snapshot without
  blocking protocol input. Edits supersede pending analysis; cancellation and shutdown complete pending
  requests with RequestCancelled. An analysis failure completes them with RequestFailed.
- **REQ-LSP-SYM-6:** Preserve definitions recovered around incomplete clauses without inventing
  definitions for malformed heads. Never execute source. Managed and NativeAOT wire behavior must agree.

Parser name-span tests, source-symbol tests, workspace ownership tests and framed protocol tests
verify these requirements; real-process managed/native smoke tests verify the combined contract.
`NameSpanTests` covers token spans (REQ-LSP-SYM-3); `SourceSymbolTests` covers module context,
DCG identity and recovery (REQ-LSP-SYM-2/6); `DocumentSymbolTests` covers response shapes, physical
ownership, configured modes and membership (REQ-LSP-SYM-1/4); `AnalysisSchedulerTests` covers
fresh snapshots, explicit cancellation and shutdown during blocked work (REQ-LSP-SYM-5).
The generic internal analysis-failure response is implemented but has no fault-injection check.

The compiler exposes `PrologSourceSymbol` records through `PrologSourceSymbols.Analyze` using the
existing lint model. Records retain module, original arity, DCG identity, file ownership and token
ranges. The server caches these records and source text with each analysis; LSP formatting stays
outside the compiler. Symbols are labeled `module:name/arity` (omitting `user:`) or `name//arity`
for DCGs. Hierarchical results group clauses; flat results navigate to the first clause.

No additional language semantics, definitions/references, rename, formatting,
`.dpli` services or editor extension are part of this increment.

### Builtin hover — implemented

- **REQ-LSP-HOV-1:** Advertise `hoverProvider` and implement LSP 3.17 `textDocument/hover` with
  a predicate indicator, concise help and the name-token range. Honor the client's preferred
  supported format (`markdown` or `plaintext`), defaulting to plaintext.
- **REQ-LSP-HOV-2:** Resolve only statically identifiable calls by module and arity. Source-defined,
  imported, dynamic and multifile predicates must not be mislabeled as builtins. Unknown modules,
  unresolved imports, data terms, variables, comments and whitespace return null.
- **REQ-LSP-HOV-3:** Use source name spans and zero-based UTF-16 positions, including quoted names,
  operators and CR/LF line endings. Negative/malformed positions are invalid parameters; positions
  beyond a line clamp to its end, and positions beyond the document return null.
- **REQ-LSP-HOV-4:** Read current analysis snapshots with unsaved buffers and physical include
  ownership. Pending requests remain cancellable and do not block protocol input. Hover requests
  invalidated by accepted text/workspace changes return ContentModified; clients can retry.
- **REQ-LSP-HOV-5:** Include ordinary goals in rules, initialization goals, statically known
  metapredicate arguments/closures and ordinary goals inside DCG braces. DCG nonterminal calls and
  arithmetic expression terms are not ordinary builtin calls.
- **REQ-LSP-HOV-6:** Derive signatures from actual registrations/bundled libraries plus explicit
  compiler control forms. Hide internal `$` predicates. State when a predefined predicate is a
  Modern extension unavailable in StrictIso; never execute user source to obtain hover information.

`BuiltinHoverTests` verifies arity, module/import resolution, shadowing, meta closures, DCG escapes,
recovery and mode notes (REQ-LSP-HOV-2/5/6). `HoverTests` verifies format negotiation, URI/position
validation, UTF-16 ranges, include contexts and project changes (REQ-LSP-HOV-1/3/4).
`AnalysisSchedulerTests` verifies pending hover cancellation, ContentModified responses, retry and
shutdown during blocked reevaluation (REQ-LSP-HOV-4). The managed/native hover smoke test checks
the combined wire contract. These requirements have no remaining product decisions for this slice.

Help covers statically identified calls in recovered complete clauses. Signatures come from the
existing runtime/library catalog; compiler control forms have explicit entries. A separate static
help catalog supplies summaries; entries without a specific summary receive a generic predefined
predicate description. No user-predicate documentation or dynamic call inference is claimed.
Builtin signatures are shown as `name/arity`, not argument modes or determinism guarantees.
Conflicting builtin/user resolutions for a shared include suppress hover rather than choosing one.

Position mapping supports CR, LF and CRLF. Parsing retains the reader's existing behavior: a `%`
comment terminated only by bare CR currently continues until LF or EOF, so following text has no
hover targets. The new line-position tests use valid parsed source independently of that limitation.
Actual editor hover rendering remains unverified.

Remaining navigation work: definition locations and references across imports, exports, reexports
and multifile declarations.

### Completion contract — implemented

This increment targets LSP 3.17 word completion in both language modes; it changes no
execution semantics, build targets or package distribution.

- **REQ-LSP-CMP-1:** Advertise `completionProvider` and accept `textDocument/completion`,
  returning function/variable items with plain name insertion and explicit UTF-16 text edits.
  Labels distinguish predicate arities. Invalid positions return InvalidParams; unknown documents
  and unsupported lexical positions return an empty list.
- **REQ-LSP-CMP-2:** Offer visible source predicates and actual registered/library builtins,
  resolving local definitions, imports and user-module fallback with the compiler model.
  Hide internal names and unavailable StrictIso builtins. Explicit atom module qualification
  uses that module's resolution context; unknown or variable modules yield no suggestions.
- **REQ-LSP-CMP-3:** Complete unquoted identifier prefixes, including the unfinished final clause,
  and replace the full identifier containing the cursor. Offer named variables only from the
  current lexical clause, excluding the anonymous variable. Suppress comments, quoted literals,
  numbers and symbolic-name completion. Word suggestions are lexical, including term arguments;
  this increment does not infer whether an argument expects a goal or data.
- **REQ-LSP-CMP-4:** Use current unsaved analysis snapshots, project sources and include contexts.
  Shared include contexts expose only candidates agreed by every context. Pending completion
  requests support RequestCancelled and return ContentModified when superseded by an edit.
- **REQ-LSP-CMP-5:** Build lexical and predicate indexes in the background worker without executing
  source or parsing on the protocol input loop. Verify completion through managed and NativeAOT
  stdio, including incomplete edits, variable scope, module visibility and Unicode ranges.

`CompletionTests` covers protocol edits, overlays, project mode and include contexts (REQ-LSP-CMP-1/4).
`CompletionIndexTests` covers imports, declarations, mode filtering and physical module scopes
(REQ-LSP-CMP-2). `CompletionTextTests` covers incomplete clauses, comments, literals, variables and
Unicode identifiers (REQ-LSP-CMP-3). `AnalysisSchedulerTests` checks pending completion cancellation,
ContentModified and shutdown during blocked reevaluation (REQ-LSP-CMP-4/5). The completion smoke
script checks the managed/native wire contract (REQ-LSP-CMP-1/3/5). Snippets, automatic imports,
quoted/operator-name completion, DCG-specific filtering and editor UI verification are deferred.

Completion tokenizes the raw buffer with default Unicode lexical rules; it does not replay
`char_conversion/2` or reader flag changes while scanning partial words. Parsed definitions and
module visibility still come from the configured project/include analysis. Predicate arities are
callable arities (DCGs include their two generated arguments), and only recovered definitions are
candidates. A missing import conservatively suppresses uncertain builtin candidates. Comment and
quoted-token suppression uses the existing lexer, including its bare-CR line-comment behavior.

## Stage 4: editor and distribution — planned

Ship a thin VS Code extension for language registration, highlighting, settings and server launch.
Verify a second independent LSP client; add Rider integration separately. Establish tool packaging,
RID artifacts, licensing checks and Windows/Linux/macOS native tests before enabling releases.

Acceptance: clean installation, editor diagnostics from an unsaved file, server restart/recovery,
configuration, and native executable operation without a separately installed .NET runtime.

## Try the current prototype

Build once, then configure an LSP client to launch the DLL directly. Do not launch `dotnet run`
from the editor: build output can contaminate the protocol's stdout.

```sh
dotnet build src/DotProlog.LanguageServer
# Server command: dotnet
# Server arguments (use an absolute DLL path in the editor):
dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll --mode modern
```

For an explicit project (use an absolute DLL path in an editor):

```sh
dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll --project samples/TextGrammar/TextGrammar.dplproj --configuration Debug
```

The process expects LSP frames on stdin; it is not an interactive command prompt. It advertises
full-text synchronization only. Background analysis supports includes for file documents.
Document symbols provide predicate outlines; builtin hover provides signatures, short help and
language-mode notes; completion suggests predicate names and current-clause variables. Editor
extensions remain planned. Project mode analyzes all evaluated sources;
loose-file mode analyzes include closures. No performance or
cross-platform NativeAOT claim is made from the initial host check.

## Verification

```sh
dotnet test --project tests/DotProlog.LanguageServer.Tests
python3 tests/Integration/verify-language-server.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
dotnet publish src/DotProlog.LanguageServer -c Release -r osx-arm64 -p:PublishAot=true -p:IlcTreatWarningsAsErrors=true -o /tmp/dotprolog-lsp-native
python3 tests/Integration/verify-language-server.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-project.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-project.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-reload.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-reload.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-cancellation.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-cancellation.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-symbols.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-symbols.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-hover.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-hover.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-completion.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-completion.py /tmp/dotprolog-lsp-native/dotprolog-lsp
```

Replace the RID and executable suffix for the host. The smoke test is repeatable and has a
40-second diagnostic deadline around the 30-second evaluation limit, a 5-second shutdown response
limit and a 10-second exit limit. Tests wait for specific diagnostic URI/version pairs; request replies
do not imply analysis completion. The POSIX cancellation fixture uses trusted MSBuild property functions
to stall evaluation and spawn a child, then checks request/shutdown responses within 2 seconds and
process-tree cleanup. It verifies wire output and exit status for either managed or native executables.

## Verification evidence

Verified on macOS arm64 on 2026-11-02:

- `DotProlog.LanguageServer.Tests`: 81 passed, zero failed or skipped, after a warning-free
  single-worker build (`dotnet build tests/DotProlog.LanguageServer.Tests --no-restore -m:1 -nr:false`).
- `DotProlog.Syntax.Tests`: 464 passed, zero failed or skipped, including lexical completion,
  supplementary Unicode variable names and combining marks.
- The four `CompletionIndexTests` passed. A full compiler run passed 2,929 of 2,930 tests;
  the remaining failure was the concurrently developed excessive-arity linter's
  `MixedDefinitionsSharingLoweredIdentityProduceOneFinding` DCG-first case. Completion tests
  and existing hover/source analysis tests passed. This is not a clean full-suite result.
- The earlier CLI baseline had 53 passing tests; the CLI suite was not rerun for this increment.

Current completion, hover, document-symbol and background-analysis checks:

- The completion smoke test passed against managed and native executables: capability negotiation,
  UTF-16 replacement of a whole identifier, incomplete unsaved buffers, clause-local variables,
  module visibility and explicit qualification, source shadowing, comment exclusion, close cleanup
  and StrictIso filtering. Unit tests additionally cover repeated/shared include contexts, an
  unfinished include with no parsed clauses, project membership changes and request cancellation.

- The hover smoke test passed against managed and native executables: negotiated Markdown/plaintext,
  UTF-16 positions after supplementary Unicode characters, quoted names, source shadowing, exclusion
  of ordinary data terms, current unsaved edits, close cleanup and StrictIso availability notes.

- The symbol smoke test passed against managed and native executables: hierarchical and flat
  responses, Unicode selections, multi-clause predicates, DCGs, recovery from incomplete syntax,
  current unsaved edits and close cleanup. An actual editor outline UI remains unverified.

- The process smoke command passed against managed and native executables, covering the original lifecycle,
  include overlays, and a live session that mutates disk files between LSP notifications.
  The live check covers registration, change/create/delete, overlay precedence and close recovery.
  A burst of 20 edits publishes only the final version after debounce.
- The project smoke script passed against managed and native executables. It uses a real imported
  project with a deliberately failing InitialTargets
  target; evaluation succeeds without executing it or creating `obj`. Debug/Release flags, source
  removal, cross-file resolution, strict-mode diagnostics and invalid-flag errors are checked.
- The live-reload smoke script exercises real MSBuild imports, mode/flag changes, added/deleted
  globbed files, invalid-settings retention and recovery, preserved buffers, and source removal.
  It uses 40-second response deadlines around the server's 30-second evaluation limit.
- The cancellation script passed against managed and native executables with real MSBuild blocked
  inside evaluation: requests and shutdown responded within their 2-second limits, and evaluation
  and its child process terminated. This is a bounded test, not a general performance benchmark.
- A repeated lifecycle regression test verifies responses are flushed before server exit returns;
  direct stream-backed pipes avoid the earlier background-writer completion race.
- The NativeAOT publish command above completed with no warnings or AOT suppressions.
- CSharpier checked the changed feature files; `git diff --check` passed.

Windows/Linux native runs and editor-client verification remain unmeasured. The full repository
integration/conformance suite was not rerun for this completion increment.

## Design references

- [MSBuild evaluation-only queries](https://learn.microsoft.com/en-us/visualstudio/msbuild/evaluate-items-and-properties)
- [LSP specification](https://microsoft.github.io/language-server-protocol/)
- [LSP 3.17 completion contract](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_completion)
- [LSP 3.17 hover contract](https://raw.githubusercontent.com/microsoft/language-server-protocol/gh-pages/_specifications/lsp/3.17/language/hover.md)
- [StreamJsonRpc transport extensibility](https://microsoft.github.io/vs-streamjsonrpc/docs/extensibility.html)
- [StreamJsonRpc NativeAOT restrictions](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html)
- [VS Code language server guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide)
