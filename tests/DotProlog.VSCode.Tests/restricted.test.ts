import assert from "node:assert/strict";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import * as vscode from "vscode";

suite("DotProlog Restricted Mode", () => {
    test("language registration remains available and no server is launched", async () => {
        assert.equal(vscode.workspace.isTrusted, false);
        const extension = vscode.extensions.getExtension("kidoz.dotprolog")!;
        const api = await extension.activate();
        await api.ready();
        const uri = vscode.Uri.file(
            path.join(process.env.DOTPROLOG_TEST_WORKSPACE!, "restricted.pl"),
        );
        await fs.writeFile(uri.fsPath, "broken(.");
        const doc = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(doc);
        assert.equal(doc.languageId, "dotprolog");
        // Restart must also honor trust rather than launching the bundled server.
        await vscode.commands.executeCommand("dotprolog.restartServer");
        assert.deepEqual(vscode.languages.getDiagnostics(uri), []);
        assert.deepEqual(
            (await vscode.commands.executeCommand(
                "vscode.executeDocumentSymbolProvider",
                uri,
            )) ?? [],
            [],
        );
    });
});
