const path = require("node:path");
const { default: Mocha } = require("mocha");
const assert = require("node:assert/strict");
const vscode = require("vscode");

exports.run = async () => {
    const expected = process.env.DOTPROLOG_TEST_VSCODE_VERSION;
    console.log(
        `Extension Host: VS Code ${vscode.version}, Node ${process.versions.node}, ${process.platform}/${process.arch}`,
    );
    if (expected && expected !== "stable") {
        // An executable override must not silently turn the minimum-version gate into a current-editor run.
        assert.equal(
            vscode.version,
            expected,
            "Unexpected VS Code version in the Extension Host",
        );
    }
    const mocha = new Mocha({ ui: "tdd", timeout: 45000, parallel: false });
    mocha.addFile(
        path.join(
            __dirname,
            `integration/${process.env.DOTPROLOG_TEST_RESTRICTED ? "restricted" : "editor"}.test.js`,
        ),
    );
    await new Promise((resolve, reject) =>
        mocha.run((failed) =>
            failed
                ? reject(new Error(`${failed} editor tests failed`))
                : resolve(),
        ),
    );
};
