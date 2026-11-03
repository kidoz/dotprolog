import assert from "node:assert/strict";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import * as vscode from "vscode";

const folder = process.env.DOTPROLOG_TEST_WORKSPACE!;
let ready: () => Promise<void>;
let serverPid: () => number | undefined;

async function eventually<T>(
    read: () => T | PromiseLike<T>,
    accept: (value: T) => boolean,
): Promise<T> {
    const end = Date.now() + 35000;
    while (Date.now() < end) {
        const value = await read();
        if (accept(value)) {
            return value;
        }
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    const diagnostics = vscode.languages
        .getDiagnostics()
        .map(([uri, items]) => ({
            uri: uri.toString(),
            diagnostics: items.map((item) => ({
                code: item.code,
                severity: item.severity,
                message: item.message,
            })),
        }));
    throw new Error(
        `Timed out waiting for editor state. Published diagnostics: ${JSON.stringify(diagnostics)}`,
    );
}

async function document(name: string, text: string) {
    const uri = vscode.Uri.file(path.join(folder, name));
    await fs.writeFile(uri.fsPath, text);
    const doc = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(doc);
    assert.equal(doc.languageId, "dotprolog");
    return doc;
}

async function replace(doc: vscode.TextDocument, text: string) {
    const edit = new vscode.WorkspaceEdit();
    edit.replace(
        doc.uri,
        new vscode.Range(
            doc.positionAt(0),
            doc.positionAt(doc.getText().length),
        ),
        text,
    );
    assert.ok(await vscode.workspace.applyEdit(edit));
}

suite("DotProlog in VS Code", () => {
    suiteSetup(async () => {
        const extension = vscode.extensions.getExtension("kidoz.dotprolog");
        assert.ok(extension);
        const api = await extension.activate();
        ready = api.ready;
        serverPid = api.serverPid;
        await ready();
    });

    teardown(async () => {
        for (const doc of vscode.workspace.textDocuments.filter(
            (doc) => doc.isDirty && doc.uri.scheme === "file",
        )) {
            await vscode.window.showTextDocument(doc);
            await vscode.commands.executeCommand(
                "workbench.action.files.revert",
            );
        }
        await vscode.commands.executeCommand(
            "workbench.action.closeAllEditors",
        );
    });

    test("unsaved diagnostics update and clear without changing disk", async () => {
        const original = "valid(a).\n";
        const doc = await document("diagnostics.pl", original);
        await replace(doc, "broken(.\n");
        await eventually(
            () => vscode.languages.getDiagnostics(doc.uri),
            (diagnostics) =>
                diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity ===
                            vscode.DiagnosticSeverity.Error &&
                        String(diagnostic.code).startsWith("DPL0"),
                ),
        );
        assert.equal(await fs.readFile(doc.uri.fsPath, "utf8"), original);
        await replace(doc, original);
        await eventually(
            () => vscode.languages.getDiagnostics(doc.uri),
            (diagnostics) =>
                !diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
    });

    test("builtin hover, outline, completion, definitions, references and rename use editor providers", async () => {
        const doc = await document(
            "navigation.pl",
            "item(a).\nrun :- item(a), member(a, [a]).\n",
        );
        const hover = await eventually(
            () =>
                vscode.commands.executeCommand<vscode.Hover[]>(
                    "vscode.executeHoverProvider",
                    doc.uri,
                    new vscode.Position(1, 18),
                ),
            (result) => !!result?.length,
        );
        assert.match(
            hover
                .flatMap((item) =>
                    item.contents.map((content) =>
                        typeof content === "string" ? content : content.value,
                    ),
                )
                .join("\n"),
            /member/,
        );
        const symbols = await vscode.commands.executeCommand<
            vscode.DocumentSymbol[]
        >("vscode.executeDocumentSymbolProvider", doc.uri);
        assert.ok(symbols.some((symbol) => symbol.name.includes("item/1")));
        const definitions = await vscode.commands.executeCommand<
            Array<vscode.Location | vscode.LocationLink>
        >(
            "vscode.executeDefinitionProvider",
            doc.uri,
            new vscode.Position(1, 8),
        );
        assert.ok(definitions.length > 0);
        const references = await vscode.commands.executeCommand<
            vscode.Location[]
        >(
            "vscode.executeReferenceProvider",
            doc.uri,
            new vscode.Position(0, 1),
        );
        assert.ok(references.length >= 1);
        const edits =
            await vscode.commands.executeCommand<vscode.WorkspaceEdit>(
                "vscode.executeDocumentRenameProvider",
                doc.uri,
                new vscode.Position(0, 1),
                "entry",
            );
        assert.ok(edits.size > 0);
        assert.ok(await vscode.workspace.applyEdit(edits));
        assert.match(doc.getText(), /^entry\(a\)\.\nrun :- entry\(a\)/);
        await replace(doc, "item(a).\nrun :- ite");
        const completions = await eventually(
            () =>
                vscode.commands.executeCommand<vscode.CompletionList>(
                    "vscode.executeCompletionItemProvider",
                    doc.uri,
                    new vscode.Position(1, 10),
                ),
            (result) =>
                !!result?.items.some((item) =>
                    JSON.stringify(item.label).includes("item"),
                ),
        );
        assert.ok(completions.items.length);
    });

    test("client file watchers refresh includes with arbitrary extensions", async () => {
        const include = path.join(folder, "included.source");
        await fs.writeFile(include, "included(ok).\n");
        const doc = await document(
            "includes.pl",
            ":- include('included.source').\nrun :- included(ok).\n",
        );
        await eventually(
            () =>
                vscode.commands.executeCommand<vscode.DocumentSymbol[]>(
                    "vscode.executeDocumentSymbolProvider",
                    doc.uri,
                ),
            (result) => !!result?.length,
        );
        await fs.writeFile(include, "included(.\n");
        await eventually(
            () => vscode.languages.getDiagnostics(vscode.Uri.file(include)),
            (diagnostics) =>
                diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
        await fs.writeFile(include, "included(ok).\n");
        await eventually(
            () => vscode.languages.getDiagnostics(vscode.Uri.file(include)),
            (diagnostics) =>
                !diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
    });

    test("server restart restores open unsaved buffers", async () => {
        const doc = await document("restart.pl", "run :- write(ok).\n");
        await replace(doc, "run :- member(ok, [ok]).\n");
        await vscode.commands.executeCommand("dotprolog.restartServer");
        await eventually(
            () =>
                vscode.commands.executeCommand<vscode.Hover[]>(
                    "vscode.executeHoverProvider",
                    doc.uri,
                    new vscode.Position(0, 9),
                ),
            (result) => !!result?.length,
        );
        assert.equal(
            await fs.readFile(doc.uri.fsPath, "utf8"),
            "run :- write(ok).\n",
        );
    });

    test("project configuration analyzes unopened sources and reloads saved imports", async () => {
        const source = path.join(folder, "project-source.pl");
        const project = path.join(folder, "Editor.dplproj");
        const props = path.join(folder, "editor.props");
        await fs.writeFile(source, "project_source(.\n");
        await fs.writeFile(
            props,
            "<Project><PropertyGroup><DotPrologLanguageMode>modern</DotPrologLanguageMode></PropertyGroup></Project>",
        );
        await fs.writeFile(
            project,
            '<Project><Import Project="editor.props"/><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PrologCompile Include="project-source.pl"/></ItemGroup></Project>',
        );
        const config = vscode.workspace.getConfiguration("dotprolog");
        await config.update(
            "project",
            project,
            vscode.ConfigurationTarget.Workspace,
        );
        await eventually(
            async () => {
                await ready();
                return vscode.languages.getDiagnostics(vscode.Uri.file(source));
            },
            (diagnostics) =>
                diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
        await fs.writeFile(source, "project_source(ok).\n");
        await eventually(
            () => vscode.languages.getDiagnostics(vscode.Uri.file(source)),
            (diagnostics) =>
                !diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
        await fs.writeFile(source, "project_source(1_000).\n");
        await fs.writeFile(
            props,
            "<Project><PropertyGroup><DotPrologLanguageMode>strict-iso</DotPrologLanguageMode></PropertyGroup></Project>",
        );
        await eventually(
            () => vscode.languages.getDiagnostics(vscode.Uri.file(source)),
            (diagnostics) =>
                diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
        await fs.writeFile(
            props,
            "<Project><PropertyGroup><DotPrologLanguageMode>modern</DotPrologLanguageMode></PropertyGroup></Project>",
        );
        await eventually(
            () => vscode.languages.getDiagnostics(vscode.Uri.file(source)),
            (diagnostics) =>
                !diagnostics.some(
                    (diagnostic) =>
                        diagnostic.severity === vscode.DiagnosticSeverity.Error,
                ),
        );
        await config.update(
            "project",
            "",
            vscode.ConfigurationTarget.Workspace,
        );
        await vscode.commands.executeCommand("dotprolog.restartServer");
    });

    test("contracts and project XML retain separate language identities", async () => {
        const contract = vscode.Uri.file(path.join(folder, "example.dpli"));
        await fs.writeFile(contract.fsPath, ":- module(example, []).\n");
        assert.equal(
            (await vscode.workspace.openTextDocument(contract)).languageId,
            "dotprolog-contract",
        );
        assert.equal(
            (
                await vscode.workspace.openTextDocument(
                    vscode.Uri.file(path.join(folder, "Editor.dplproj")),
                )
            ).languageId,
            "xml",
        );
    });

    test("a crashed server is replaced and unsaved buffers are restored", async () => {
        const doc = await document("crash.pl", "run :- write(ok).\n");
        await replace(doc, "run :- member(ok, [ok]).\n");
        const pid = serverPid();
        assert.ok(pid);
        process.kill(pid, "SIGKILL");
        await eventually(
            () => serverPid(),
            (next) => !!next && next !== pid,
        );
        await eventually(
            async () => {
                try {
                    return await vscode.commands.executeCommand<vscode.Hover[]>(
                        "vscode.executeHoverProvider",
                        doc.uri,
                        new vscode.Position(0, 9),
                    );
                } catch {
                    return [];
                }
            },
            (result) => !!result?.length,
        );
        assert.equal(
            await fs.readFile(doc.uri.fsPath, "utf8"),
            "run :- write(ok).\n",
        );
    });

    test("a bad server override reports failure and a corrected setting recovers", async () => {
        const config = vscode.workspace.getConfiguration("dotprolog");
        await config.update(
            "serverPath",
            path.join(folder, "missing-server.dll"),
            vscode.ConfigurationTarget.Workspace,
        );
        await assert.rejects(
            Promise.resolve(
                vscode.commands.executeCommand("dotprolog.restartServer"),
            ),
        );
        await config.update(
            "serverPath",
            "",
            vscode.ConfigurationTarget.Workspace,
        );
        await vscode.commands.executeCommand("dotprolog.restartServer");
        const doc = await document("recovered.pl", "run :- write(ok).\n");
        await eventually(
            () =>
                vscode.commands.executeCommand<vscode.Hover[]>(
                    "vscode.executeHoverProvider",
                    doc.uri,
                    new vscode.Position(0, 9),
                ),
            (result) => !!result?.length,
        );
    });
});
