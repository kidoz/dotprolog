import { runTests, downloadAndUnzipVSCode } from "@vscode/test-electron";
import { spawn } from "node:child_process";
import { mkdtemp, mkdir, writeFile, rm, cp } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { root } from "./server.mjs";

const temp = await mkdtemp(path.join(os.tmpdir(), "dotprolog-vscode-"));
const workspace = path.join(temp, "workspace with spaces");
const restricted = process.argv.includes("--restricted");
await mkdir(workspace);
await mkdir(path.join(workspace, ".vscode"));
await writeFile(
    path.join(workspace, ".vscode/settings.json"),
    JSON.stringify({
        "files.autoSave": "off",
        "editor.wordBasedSuggestions": "off",
    }),
);
await mkdir(path.join(temp, "profile/User"), { recursive: true });
await writeFile(
    path.join(temp, "profile/User/settings.json"),
    JSON.stringify({ "security.workspace.trust.startupPrompt": "never" }),
);
// Use a real Extension Host with a fresh profile, not the developer's settings or extensions.
try {
    const options = {
        extensionDevelopmentPath: process.env.DOTPROLOG_EXTENSION_PATH || root,
        extensionTestsPath: path.join(root, "scripts/test-runner.cjs"),
        vscodeExecutablePath: process.env.VSCODE_EXECUTABLE_PATH,
        cachePath: path.join(root, "bin/vscode-test"),
        launchArgs: [
            workspace,
            "--disable-extensions",
            ...(restricted ? [] : ["--disable-workspace-trust"]),
            "--skip-welcome",
            "--skip-release-notes",
            "--extensions-dir",
            path.join(temp, "extensions"),
            "--user-data-dir",
            path.join(temp, "profile"),
        ],
        extensionTestsEnv: {
            DOTPROLOG_TEST_WORKSPACE: workspace,
            DOTPROLOG_TEST_RESTRICTED: restricted ? "1" : "",
        },
    };
    if (restricted) {
        // runTests always appends --disable-workspace-trust. Launch the same Extension Host
        // directly for this check so Restricted Mode is actually exercised.
        const executable =
            options.vscodeExecutablePath ||
            (await downloadAndUnzipVSCode({ cachePath: options.cachePath }));
        const child = spawn(
            executable,
            [
                ...options.launchArgs,
                "--no-sandbox",
                "--disable-gpu-sandbox",
                "--disable-updates",
                `--extensionDevelopmentPath=${options.extensionDevelopmentPath}`,
                `--extensionTestsPath=${options.extensionTestsPath}`,
            ],
            {
                stdio: "inherit",
                env: { ...process.env, ...options.extensionTestsEnv },
                shell: false,
            },
        );
        await new Promise((resolve, reject) => {
            child.on("error", reject);
            child.on("exit", (code) =>
                code === 0
                    ? resolve()
                    : reject(
                          new Error(
                              `Restricted Mode tests exited with ${code}`,
                          ),
                      ),
            );
        });
    } else {
        await runTests(options);
    }
} catch (error) {
    const logs = path.join(root, "bin/last-editor-run");
    await rm(logs, { recursive: true, force: true });
    await cp(temp, logs, { recursive: true });
    throw error;
} finally {
    await rm(temp, { recursive: true, force: true });
}
