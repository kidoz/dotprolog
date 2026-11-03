# DotProlog for Visual Studio Code

Language services for DotProlog, the Prolog implementation for .NET 10.

## Features

- Prolog highlighting, brackets, comments and snippets.
- Parser and lint diagnostics for unsaved `.pl` files and their includes.
- Builtin hover, predicate Outline, predicate and clause-local variable completion.
- Definitions, references and conservative predicate rename across analyzed sources.
- One explicitly selected `.dplproj` per window, with evaluated mode, flags and source items.
- Server restart, output and bounded automatic recovery after server crashes.
- Highlighting for `.dpli` contracts and XML editing for `.dplproj` files.

Analysis does not execute Prolog goals. Rename only covers the analyzed snapshot and can refuse
operations when the server cannot prove an unambiguous static edit. Contract diagnostics, formatting,
debugging and running queries are not provided by this preview.

## Install and use

Use **Extensions: Install from VSIX...**, or:

```sh
code --install-extension /absolute/path/to/dotprolog-0.1.0.vsix
```

The universal managed package requires the .NET 10 runtime on the machine holding the workspace.
Native platform packages include their server and need no separately installed .NET runtime for
loose files. **Project mode always requires the .NET 10 SDK**, including with native packages.
The extension does not install SDKs, restore packages or download servers automatically.

Open a `.pl` file. If another extension owns the suffix, select **DotProlog** in the language picker,
or configure `"files.associations": { "*.pl": "dotprolog" }`.

Use **DotProlog: Select Project** to select a workspace project or choose **Loose files**. The
selected project supplies its mode and flags; unrelated files in a project session use Modern mode.
One client owns all Prolog buffers in the window. Simultaneous independent project contexts are
not supported yet.

The extension runs beside source files in the local or remote workspace Node host, including SSH,
WSL, containers and Codespaces. Browser-only and virtual workspaces are unsupported. Restricted Mode
retains highlighting and snippets; language services start after trust is granted because project
evaluation and configured executables can execute code.

## Settings and commands

| Setting                   | Default  | Purpose                                                                |
| ------------------------- | -------- | ---------------------------------------------------------------------- |
| `dotprolog.serverPath`    | Empty    | Absolute alternative server executable or DLL; empty uses the bundle.  |
| `dotprolog.dotnetPath`    | `dotnet` | Executable used to launch a managed server.                            |
| `dotprolog.project`       | Empty    | Selected project; relative paths require exactly one workspace folder. |
| `dotprolog.configuration` | `Debug`  | Selected project's MSBuild configuration.                              |
| `dotprolog.mode`          | `modern` | Loose-session mode: `modern` or `strict-iso`.                          |

Settings changes restart the server. Use **DotProlog: Restart Language Server** to retry after a
failure and **DotProlog: Show Language Server Output** for startup and evaluation logs.

File changes follow VS Code's workspace watcher coverage and exclusions. Includes can have any
extension; dependencies outside that coverage may require reopening or a server restart. Save
project XML and imports before expecting their evaluated settings to change. Address missing SDKs
or unrestored project SDK packages explicitly outside the editor.

## Build and test

Requires Node.js 22 or newer, npm and the repository's .NET 10 SDK. From this directory:

```sh
npm ci
npm run build:server
npm test
npm run test:integration
npm run test:restricted
npm run package
```

Editor tests use isolated Extension Hosts, profiles and temporary workspaces with spaces in their
paths. VS Code stable is downloaded by default; set `VSCODE_EXECUTABLE_PATH` to use an installed
VS Code executable. On headless Linux, run editor tests under `xvfb-run -a`. Set
`DOTPROLOG_EXTENSION_PATH` to an extracted VSIX's `extension` directory to test the packaged payload.
Build outputs and VSIX files live under `bin/`; npm dependencies live in `node_modules/`.

`npm run package` creates `bin/dotprolog-0.1.0.vsix` with the managed server. To build a NativeAOT
platform package on a matching OS and native toolchain:

```sh
npm run package -- darwin-arm64
```

Targets are `darwin-arm64`, `darwin-x64`, `linux-x64`, `linux-arm64`, `win32-x64` and `win32-arm64`.
Targets map to .NET RIDs; cross-OS native compilation is unsupported. These commands create local
artifacts and never publish to the Marketplace. Test each generated platform package on its target.

## License

MIT. Bundled dependency notices accompany the extension.
