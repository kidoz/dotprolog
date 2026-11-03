import * as fs from "node:fs/promises";
import * as path from "node:path";
import * as vscode from "vscode";
import { LanguageClient } from "vscode-languageclient/node";
import { launch, Settings } from "./launch";

let client: LanguageClient | undefined;
let pending: Promise<void> = Promise.resolve();
let disposed = false;
let output: vscode.LogOutputChannel;

export function activate(context: vscode.ExtensionContext) {
    disposed = false;
    output = vscode.window.createOutputChannel("DotProlog", { log: true });
    context.subscriptions.push(output);

    async function start() {
        const previous = client;
        client = undefined;
        if (previous) {
            await previous.dispose();
        }
        if (disposed || !vscode.workspace.isTrusted) {
            output.appendLine(
                "Language services require a trusted workspace. Syntax highlighting remains available.",
            );
            return;
        }
        const config = vscode.workspace.getConfiguration("dotprolog");
        const settings: Settings = {
            serverPath: config.get("serverPath", ""),
            dotnetPath: config.get("dotnetPath", "dotnet"),
            project: config.get("project", ""),
            configuration: config.get("configuration", "Debug"),
            mode: config.get("mode", "modern"),
        };
        const native = context.asAbsolutePath(
            path.join(
                "bin",
                "server",
                process.platform === "win32"
                    ? "dotprolog-lsp.exe"
                    : "dotprolog-lsp",
            ),
        );
        const bundled = await fs.access(native).then(
            () => native,
            () => context.asAbsolutePath("bin/server/dotprolog-lsp.dll"),
        );
        const command = launch(
            settings,
            bundled,
            (vscode.workspace.workspaceFolders ?? []).map(
                (folder) => folder.uri.fsPath,
            ),
        );
        const server = settings.serverPath || bundled;
        await fs.access(server);
        output.appendLine(
            `Starting ${server}${settings.project ? ` for ${settings.project}` : ` (${settings.mode})`}`,
        );
        const next = new LanguageClient(
            "dotprolog",
            "DotProlog",
            // Omitted transport uses stdio without appending the unsupported --stdio switch.
            { ...command, options: { shell: false } },
            {
                documentSelector: [
                    { scheme: "file", language: "dotprolog" },
                    { scheme: "untitled", language: "dotprolog" },
                ],
                outputChannel: output,
                traceOutputChannel: output,
                connectionOptions: { maxRestartCount: 3 },
            },
        );
        client = next;
        try {
            await next.start();
        } catch (error) {
            client = undefined;
            await next
                .dispose()
                .catch((cleanup) =>
                    output.error("Server cleanup failed", cleanup),
                );
            throw error;
        }
    }

    function restart(): Promise<void> {
        // Serialize configuration changes and commands so old processes cannot outlive new ones.
        pending = pending
            .catch(() => {})
            .then(start)
            .catch((error) => {
                const message = `DotProlog could not start: ${error instanceof Error ? error.message : String(error)}. Check the server path and .NET installation.`;
                output.appendLine(message);
                console.error(message);
                void vscode.window
                    .showErrorMessage(message, "Show Output")
                    .then((action) => {
                        if (action) {
                            output.show();
                        }
                    });
                throw error;
            });
        return pending;
    }

    context.subscriptions.push(
        vscode.commands.registerCommand("dotprolog.restartServer", restart),
        vscode.commands.registerCommand("dotprolog.showOutput", () =>
            output.show(),
        ),
        vscode.commands.registerCommand("dotprolog.selectProject", async () => {
            if (!vscode.workspace.isTrusted) {
                void vscode.window.showInformationMessage(
                    "Trust this workspace before selecting a DotProlog project.",
                );
                return;
            }
            const projects = await vscode.workspace.findFiles(
                "**/*.dplproj",
                "**/{bin,obj,node_modules,.git}/**",
            );
            const selected = await vscode.window.showQuickPick(
                [
                    {
                        label: "Loose files",
                        description: "Use the configured language mode",
                        project: "",
                    },
                    ...projects
                        .sort((a, b) => a.fsPath.localeCompare(b.fsPath))
                        .map((uri) => ({
                            label: path.basename(uri.fsPath),
                            description: vscode.workspace.asRelativePath(uri),
                            project: uri.fsPath,
                        })),
                ],
                { placeHolder: "Select one project for this window" },
            );
            if (selected) {
                await vscode.workspace
                    .getConfiguration("dotprolog")
                    .update(
                        "project",
                        selected.project,
                        vscode.workspace.workspaceFolders
                            ? vscode.ConfigurationTarget.Workspace
                            : vscode.ConfigurationTarget.Global,
                    );
            }
        }),
        vscode.workspace.onDidChangeConfiguration((event) => {
            if (event.affectsConfiguration("dotprolog")) {
                void restart().catch(() => {});
            }
        }),
        vscode.workspace.onDidGrantWorkspaceTrust(() => {
            void restart().catch(() => {});
        }),
    );
    void restart().catch(() => {});
    return {
        ready: () => pending,
        serverPid: () => client?.serverProcess?.pid,
    };
}

export async function deactivate() {
    disposed = true;
    await pending.catch(() => {});
    const previous = client;
    client = undefined;
    await previous?.dispose();
}
