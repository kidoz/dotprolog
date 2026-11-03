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

## Stage 3: symbols and navigation — implemented with conservative predicate rename

The first navigation increment provides a predicate outline through LSP 3.17
`textDocument/documentSymbol`, with the following behavior:

- Advertise `documentSymbolProvider` and return hierarchical `DocumentSymbol`
  results when the client advertises support, otherwise flat `SymbolInformation` results.
- Identify definitions by module, source name, arity and ordinary/DCG identity.
  Group clauses of the same predicate in each physical file; hierarchical results expose individual
  clause children for predicates with multiple clauses. Directives and declarations are not definitions.
- Use zero-based UTF-16 ranges from the analyzed text, with selection ranges on
  the original name/operator token, including quoting. Definition ranges cover parsed terms, excluding
  their terminating full stops. Synthetic heads without a name token use the head range.
- Use unsaved buffers and the same project/include reader context as diagnostics.
  Return only definitions physically owned by the requested URI. Untracked documents return an empty
  list; unopened evaluated project sources and retained includes can have outlines.
- Requests arriving during analysis wait for a current committed snapshot without
  blocking protocol input. Edits supersede pending analysis; cancellation and shutdown complete pending
  requests with RequestCancelled. An analysis failure completes them with RequestFailed.
- Preserve definitions recovered around incomplete clauses without inventing
  definitions for malformed heads. Never execute source. Managed and NativeAOT wire behavior must agree.

Parser name-span tests, source-symbol tests, workspace ownership tests and framed protocol tests
verify these requirements; real-process managed/native smoke tests verify the combined contract.
The generic internal analysis-failure response is implemented but has no fault-injection check.

The compiler exposes `PrologSourceSymbol` records through `PrologSourceSymbols.Analyze` using the
existing lint model. Records retain module, original arity, DCG identity, file ownership and token
ranges. The server caches these records and source text with each analysis; LSP formatting stays
outside the compiler. Symbols are labeled `module:name/arity` (omitting `user:`) or `name//arity`
for DCGs. Hierarchical results group clauses; flat results navigate to the first clause.

No additional language semantics, rename, formatting,
`.dpli` services or editor extension are part of this increment.

### Builtin hover — implemented

- Advertise `hoverProvider` and implement LSP 3.17 `textDocument/hover` with
  a predicate indicator, concise help and the name-token range. Honor the client's preferred
  supported format (`markdown` or `plaintext`), defaulting to plaintext.
- Resolve only statically identifiable calls by module and arity. Source-defined,
  imported, dynamic and multifile predicates must not be mislabeled as builtins. Unknown modules,
  unresolved imports, data terms, variables, comments and whitespace return null.
- Use source name spans and zero-based UTF-16 positions, including quoted names,
  operators and CR/LF line endings. Negative/malformed positions are invalid parameters; positions
  beyond a line clamp to its end, and positions beyond the document return null.
- Read current analysis snapshots with unsaved buffers and physical include
  ownership. Pending requests remain cancellable and do not block protocol input. Hover requests
  invalidated by accepted text/workspace changes return ContentModified; clients can retry.
- Include ordinary goals in rules, initialization goals, statically known
  metapredicate arguments/closures and ordinary goals inside DCG braces. DCG nonterminal calls and
  arithmetic expression terms are not ordinary builtin calls.
- Derive signatures from actual registrations/bundled libraries plus explicit
  compiler control forms. Hide internal `$` predicates. State when a predefined predicate is a
  Modern extension unavailable in StrictIso; never execute user source to obtain hover information.

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

Predicate rename now follows the conservative contract below, refusing edits when static analysis
cannot establish a complete, unambiguous set of affected source locations in the analyzed workspace.

### Completion contract — implemented

This increment targets LSP 3.17 word completion in both language modes; it changes no
execution semantics, build targets or package distribution.

- Advertise `completionProvider` and accept `textDocument/completion`,
  returning function/variable items with plain name insertion and explicit UTF-16 text edits.
  Labels distinguish predicate arities. Invalid positions return InvalidParams; unknown documents
  and unsupported lexical positions return an empty list.
- Offer visible source predicates and actual registered/library builtins,
  resolving local definitions, imports and user-module fallback with the compiler model.
  Hide internal names and unavailable StrictIso builtins. Explicit atom module qualification
  uses that module's resolution context; unknown or variable modules yield no suggestions.
- Complete unquoted identifier prefixes, including the unfinished final clause,
  and replace the full identifier containing the cursor. Offer named variables only from the
  current lexical clause, excluding the anonymous variable. Suppress comments, quoted literals,
  numbers and symbolic-name completion. Word suggestions are lexical, including term arguments;
  this increment does not infer whether an argument expects a goal or data.
- Use current unsaved analysis snapshots, project sources and include contexts.
  Shared include contexts expose only candidates agreed by every context. Pending completion
  requests support RequestCancelled and return ContentModified when superseded by an edit.
- Build lexical and predicate indexes in the background worker without executing
  source or parsing on the protocol input loop. Verify completion through managed and NativeAOT
  stdio, including incomplete edits, variable scope, module visibility and Unicode ranges.

Snippets, automatic imports, quoted/operator-name completion, DCG-specific filtering and editor UI
verification are deferred.

Completion tokenizes the raw buffer with default Unicode lexical rules; it does not replay
`char_conversion/2` or reader flag changes while scanning partial words. Parsed definitions and
module visibility still come from the configured project/include analysis. Predicate arities are
callable arities (DCGs include their two generated arguments), and only recovered definitions are
candidates. A missing import conservatively suppresses uncertain builtin candidates. Comment and
quoted-token suppression uses the existing lexer, including its bare-CR line-comment behavior.

### Definition navigation contract — implemented

This increment adds LSP 3.17 source navigation in both language modes without executing source.

- Advertise `definitionProvider` and serve `textDocument/definition`.
  Return `LocationLink[]` when `textDocument.definition.linkSupport` is true, otherwise
  `Location[]`. Use UTF-16 origin/name/clause ranges and preserve physical include ownership.
- Resolve statically known calls by module and callable arity, following
  imports, reexports and user-module fallback. Return every matching source clause, including
  multifile definitions. Predicate heads navigate to the same definition set.
- Traverse ordinary rule/initialization goals, DCG nonterminals and escaped
  goals, and statically identifiable meta-goals/closures. Exclude ordinary data, comments,
  literals, builtins, variable call targets, intrinsic controls and unresolved targets.
  Dynamic declarations alone are not source implementations; available source clauses can be targets.
- Read committed analysis snapshots containing current unsaved buffers,
  project files and include expansions. Conflicting shared/repeated include resolutions return
  no destination. Unknown documents or unsupported positions return an empty array.
- Reject malformed positions with InvalidParams. Pending requests support
  RequestCancelled and ContentModified using the positional-request scheduler contract.
  No request-time parsing, source execution or arbitrary disk loading is permitted.

`SourceDefinitionTests` verifies source identity, arities, reexports in both source orders,
DCG/meta-goals, multifile clauses, quoted/operator names and unresolved calls.
`DefinitionTests` verifies link negotiation, URI/range conversion, edits, target overlays, project
membership and conflicting shared/repeated includes.
`AnalysisSchedulerTests` verifies invalidation, retry, cancellation and shutdown for definition
requests. `verify-language-server-definition.py` exercises the wire contract,
Unicode, DCGs, reexports and target-buffer/disk changes through managed and native executables.
Import dependencies are analyzed before clients, preserving declaration order inside each module.
Cyclic import graphs use a bounded conservative fallback; no fixed-point completeness is claimed.
Navigation covers recovered complete clauses. Import loading remains limited to evaluated project
sources and include closures; variable binding inference, declaration-indicator navigation,
rename and actual editor UI validation are outside this increment.

### Find references contract — implemented

This increment adds LSP 3.17 `textDocument/references` for source predicates in both language modes.

- Advertise `referencesProvider`; require a boolean `context.includeDeclaration` and return
  deterministic, deduplicated `Location[]` with UTF-16 name-token ranges.
- Resolve the selected head or call by module, callable arity and shared physical source
  definitions. Search the analyzed project/include/buffer workspace. Separate unrelated loose
  programs that happen to define the same module/name/arity.
- Report ordinary calls, DCG calls and statically known meta-goals/closures using the same model
  as definition navigation. Include parsed clause heads only when `includeDeclaration` is true.
  Predicate indicators in directives, variables, data, builtin calls and unresolved targets are excluded.
- Require agreement across shared/repeated include contexts both at the selected occurrence and
  at each returned location. Unsupported/ambiguous selections return an empty array; uncertain
  candidate locations are omitted rather than guessed. Results are static findings, not a claim
  that every runtime reference is known.
- Update the background reference index with unsaved edits, disk changes, project membership
  and document close. Validate malformed parameters with InvalidParams. Pending positional
  requests support RequestCancelled and ContentModified; no request-time parsing, source
  execution or arbitrary file loading is allowed.

`SourceDefinitionTests` verifies the explicit head/call metadata. `ReferenceTests` covers
protocol validation, optional heads, UTF-16 locations, module/arity resolution, DCGs/meta-goals,
multifile clauses, loose-program isolation, includes, overlays, disk changes and membership.
`AnalysisSchedulerTests` covers reference cancellation, invalidation/retry and shutdown during
blocked reevaluation. `verify-language-server-references.py` checks the combined managed/native
wire behavior with a real evaluated project and client-first reexports. Dynamic binding inference,
references to declarations without source bodies, rename and editor UI validation remain outside
this increment. Definition navigation shares the strengthened include/program identity check.

### Predicate rename contract — implemented

This increment implements LSP 3.17 `textDocument/prepareRename` and `textDocument/rename` in
Modern and StrictIso modes. It changes editor analysis only, with no new language semantics.

- Enable rename only for clients advertising `workspace.workspaceEdit.documentChanges`.
  Advertise `{ prepareProvider: true }` when the client also advertises `rename.prepareSupport`;
  otherwise advertise boolean rename support. Preparation returns the name-token range and
  semantic name, or null when the snapshot cannot support rename.
- Return a `WorkspaceEdit.documentChanges` array with deterministic, deduplicated UTF-16 edits.
  Open buffers carry their accepted version; unopened files carry `version: null` as specified by
  LSP. The server never applies edits. Disk content is the last analyzed snapshot: clients must
  deliver file-change notifications and handle external changes when applying edits.
- Rename all clauses and statically resolved calls of the selected module/callable-arity identity,
  including DCG and known meta-call targets. Also edit explicit module exports, `export`, selective
  imports/reexports, `multifile`, `discontiguous` and `meta_predicate` names. Preserve module names,
  arities, data terms and unrelated loose programs. Whole-module imports need no textual change.
- Accept plain ASCII names matching `[a-z][a-zA-Z0-9_]*`, excluding builtin names, default operators
  and expansion hooks. Quoted source tokens with an eligible semantic name can be replaced by a
  plain identifier. Reject invalid/reserved new names with InvalidParams. Reject an existing new
  name of the same callable arity anywhere in an affected analysis, conservatively including
  other modules, with RequestFailed. A valid unchanged name returns an empty edit.
- Require every retained interpretation of each edited physical token to agree on its identity,
  role and source definition. Refuse ambiguous includes, missing/invalid source content, unresolved
  or cyclic imports, declarations without source bodies, variable goals or module qualifiers,
  dynamic declarations, reader-changing and unsupported directives, and reflective/database calls.
  Builtin calls must belong to an audited allowlist. These checks apply to the whole affected
  project/include analysis; an unrelated unsupported construct in that analysis can disable rename.
  Custom meta-predicates whose bodies invoke variables and nested closures needing additional
  argument propagation are deliberately unsupported in this first implementation.
- Recheck eligibility and collisions on every rename, even after successful preparation. Failed
  project reevaluation disables rename until recovery. Pending requests support RequestCancelled
  and ContentModified; an expected rename refusal does not invalidate the workspace or other requests.
  No request performs parsing, source execution or filesystem reads.

Scope is the evaluated project, retained include closures and open buffers. Consumers outside that
snapshot, external APIs and runtime-created references are not inferred. General atom/operator
rename, variable rename, formatting and editor UI integration remain out of scope.

`RenameAnalysisTests` covers compiler eligibility and declaration tokens. `RenameTests` verifies
edit application, solution preservation in both modes, imports/reexports, DCGs, includes, overlays,
collisions and failure recovery. `AnalysisSchedulerTests` exercises cancellation, superseded edits
and shutdown during blocked reevaluation. `verify-language-server-rename.py` checks the combined
contract over managed and native stdio, including project reload failure and recovery.

## Stage 4: editor and distribution — VS Code preview implemented

`src/DotProlog.VSCode` supplies a TypeScript client using `vscode-languageclient`, source/contract
language registration, TextMate highlighting, snippets and server launch. Project files retain XML
editing. The negotiated server capabilities provide diagnostics, hover, Outline, completion,
definitions, references and versioned predicate rename through VS Code.

One client owns the window's Prolog buffers with one explicitly selected project or loose-file
session. Setting changes restart the server and synchronize unsaved buffers again. The client
launches a DLL using `dotnet`, or an executable directly, without a shell, `dotnet run` or a
`--stdio` argument. Dynamic client watchers deliver disk notifications, including arbitrary include
extensions. Workspace watch coverage and exclusions still apply.

Language services require Workspace Trust; syntax contributions remain available in Restricted Mode.
The workspace Node host keeps server and source locations together for remote development.
Browser-only and virtual workspaces are unsupported. Opening source triggers no builds or restores.

Local packaging bundles a managed server requiring the .NET 10 runtime, or a NativeAOT server for
an explicit platform target. Project mode still requires the .NET SDK. Packaging does not publish
releases. See the [extension guide](https://github.com/kidoz/dotprolog/blob/main/src/DotProlog.VSCode/README.md)
for installation and settings.

`tests/DotProlog.VSCode.Tests` exercises real Extension Hosts and server processes with isolated
profiles and temporary workspaces. The managed-package CI matrix runs integration and Restricted
Mode checks on both the manifest's minimum VS Code version and current stable for each OS.
`VSCODE_VERSION=minimum` derives the exact lower bound from the tested extension's manifest;
an exact version or `stable` can also be selected. The host reports its actual version and fails
if it differs from an exact/minimum selection, even with an executable override.
Cross-platform native checks, a second independent editor
client and Rider integration remain future work.

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
language-mode notes; completion suggests predicate names and current-clause variables; definitions
navigate to source clauses across the analyzed workspace; references list known calls and
optionally clause heads; rename returns versioned edits when its conservative eligibility checks
pass and the client supports versioned workspace edits. The VS Code preview exposes these services.
Project mode analyzes all evaluated sources;
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
python3 tests/Integration/verify-language-server-definition.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-definition.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-references.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-references.py /tmp/dotprolog-lsp-native/dotprolog-lsp
python3 tests/Integration/verify-language-server-rename.py dotnet src/DotProlog.LanguageServer/bin/Debug/net10.0/dotprolog-lsp.dll
python3 tests/Integration/verify-language-server-rename.py /tmp/dotprolog-lsp-native/dotprolog-lsp
```

Replace the RID and executable suffix for the host. The smoke test is repeatable and has a
40-second diagnostic deadline around the 30-second evaluation limit, a 5-second shutdown response
limit and a 10-second exit limit. Tests wait for specific diagnostic URI/version pairs; request replies
do not imply analysis completion. The POSIX cancellation fixture uses trusted MSBuild property functions
to stall evaluation and spawn a child, then checks request/shutdown responses within 2 seconds and
process-tree cleanup. It verifies wire output and exit status for either managed or native executables.

## Verification evidence

Cross-platform managed-package validation on 2026-11-03 at `c23c5a6` is tracked by
[the VS Code CI run](https://github.com/kidoz/dotprolog/actions/runs/41678580961):

| Host | VS Code 1.91.0 | VS Code 1.144.1 |
| --- | --- | --- |
| Windows x64 | Passed | Passed |
| Linux x64 | Passed | Passed |
| macOS arm64 | Queued for a hosted runner | Passed |

Each completed job passed formatting, TypeScript compilation, six launch/grammar tests,
managed VSIX packaging, eight editor integration tests against the extracted package and one
Restricted Mode test. Each uploaded its VSIX artifact. The minimum editor used Node 20.9.0;
stable used Node 24.22.1. The macOS minimum job is not counted as a CI pass while queued.

The preceding run exposed a Windows URI conversion defect: the `%3A` drive separator sent by
VS Code left an extra leading slash in the server's local path. Includes acquired duplicated
drive prefixes, and disk notifications did not match project source paths. Normalizing that
drive prefix fixed both failing editor cases. Regression coverage checks document identity,
include loading and disk updates, including filenames with spaces, `#` and literal `%3A`.
A clean-source local language-server run passed 133 tests and skipped two Windows-only cases.
Native packages were not exercised by this matrix.

Minimum/current-stable compatibility verified on macOS arm64 on 2026-11-03 using a freshly
packaged and extracted managed VSIX:

| VS Code | Embedded Node | Editor integration | Restricted Mode |
| --- | --- | --- | --- |
| 1.91.0 (declared minimum) | 20.9.0 | 8 passed | 1 passed |
| 1.144.1 (stable resolved for this run) | 24.22.1 | 8 passed | 1 passed |

The first minimum-version attempt failed before test execution because Node 20.9 could not
`require()` Mocha 12's ESM entrypoint. Bundling the serial test harness as CommonJS fixed the
failure; the extension's engine range and runtime dependencies did not need changes. The harness
remains outside the VSIX. A deliberate mismatch (requesting 1.91.1 with a 1.91.0 executable)
was rejected by the in-host version assertion. Six launch/grammar tests, TypeScript compilation
and Prettier also passed. The GitHub Actions matrix now covers minimum/stable on Windows, Linux
and macOS; remote CI execution and native packages were not rerun for this compatibility increment.

VS Code preview verified on macOS arm64 on 2026-11-03 with VS Code 1.130.0 and .NET SDK 10.0.403:

- Six launch/grammar checks passed, and the language server suite passed all 132 tests.
- The managed and native VSIX payloads passed eight real Extension Host tests each: unsaved
  diagnostic updates and cleanup, hover/Outline/completion/navigation/references/rename, arbitrary
  include-extension disk watching, restart and crash recovery with unsaved buffers, saved project
  import reloads, language identities and recovery after an invalid server override.
- A separate Restricted Mode Extension Host retained language registration and started no server.
  That runner deliberately avoids the test helper's default trust-disabling argument.
- Native publication for `osx-arm64` completed without warnings. Both VSIX packages include
  dependency notices and exclude source, tests, npm dependencies and debug symbols.
- TypeScript compilation, Prettier, the strict documentation build and `git diff --check` passed.

The Windows/Linux managed-package CI matrix is configured but was not executed locally. Native
packages for other targets and Marketplace publication remain
unverified or outside this local preview.

Verified on macOS arm64 on 2026-11-02:

- `DotProlog.LanguageServer.Tests`: 132 passed, zero failed or skipped, after a warning-free
  single-worker build (`dotnet build tests/DotProlog.LanguageServer.Tests --no-restore -m:1 -nr:false`).
- `DotProlog.Compiler.Tests`: 3,028 passed, zero failed or skipped, including rename analysis,
  source definitions, builtin hover and import-resolution regressions. After adding two further
  cases, the focused rename suite passed all 27 tests (cycles and nested static meta-goals included).
- The previous syntax baseline had 464 passing tests, including lexical completion and Unicode
  identifiers. Syntax code was unchanged and that suite was not rerun for predicate rename.
- The earlier CLI baseline had 53 passing tests; the CLI suite was not rerun for this increment.

Current rename, reference, definition, completion, hover, document-symbol and background-analysis checks:

- All ten smoke scripts passed against both managed and macOS arm64 native executables.
  The new rename script verifies versioned UTF-16 edits, unchanged source files before client
  application, DCGs, selective imports/reexports, current overlays, invalid names, collisions,
  unsupported/indirect calls, and failed project reevaluation followed by recovery. Unit tests
  also apply returned edits and preserve query solutions in Modern and StrictIso modes, check
  ambiguous/repeated includes, and isolate expected failures from other queued requests.

- The reference smoke test passed against managed and native executables: optional heads,
  exact UTF-16 call locations, DCG/meta-calls,
  unrelated loose-program isolation, real-project reexports, unsaved overlays, disk updates,
  project source removal and close cleanup. Unit tests additionally verify that ambiguous
  shared/repeated include positions are omitted both as origins and candidate results.
  Actual editor reference UI remains unverified.

- The definition smoke test passed against managed and native executables: links and plain
  locations, UTF-16 origin/target ranges,
  multiple clauses, DCGs, reexports with clients first in evaluated project order, unsaved target
  overlays, disk changes and close cleanup. Unit tests additionally check multifile predicates,
  custom meta-goals, shared/repeated include conflicts, and a token interpreted as data in one
  include context and a goal in another. Actual editor navigation UI remains unverified.

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
integration/conformance suite was not rerun for this predicate-rename increment.

## Design references

- [MSBuild evaluation-only queries](https://learn.microsoft.com/en-us/visualstudio/msbuild/evaluate-items-and-properties)
- [LSP specification](https://microsoft.github.io/language-server-protocol/)
- [LSP 3.17 rename contract](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_rename)
- [LSP 3.17 workspace edits](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspaceEdit)
- [LSP 3.17 references contract](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_references)
- [LSP 3.17 definition contract](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_definition)
- [LSP 3.17 completion contract](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_completion)
- [LSP 3.17 hover contract](https://raw.githubusercontent.com/microsoft/language-server-protocol/gh-pages/_specifications/lsp/3.17/language/hover.md)
- [StreamJsonRpc transport extensibility](https://microsoft.github.io/vs-streamjsonrpc/docs/extensibility.html)
- [StreamJsonRpc NativeAOT restrictions](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html)
- [VS Code language server guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide)
